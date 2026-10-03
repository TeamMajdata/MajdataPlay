// Exercise the real exported bridge and common context without a graphics
// driver or decoder. The fake backend controls completion, not context logic.
#include "PortableGpuContext.h"
#include <chrono>
#include <condition_variable>
#include <cstdlib>
#include <thread>

#define CHECK(value) do { if (!(value)) { std::fprintf(stderr, "FAIL line %d: %s\n", __LINE__, #value); std::exit(1); } } while (false)
using namespace std::chrono_literals;

struct Callbacks
{
    bool (*setup)(void**, const PortableSetupConfig*, PortableSetupInfo*) = nullptr;
    void (*cleanup)(void*) = nullptr;
    bool (*update)(void*, const PortableRenderConfig*, PortableOutputConfig*) = nullptr;
    void (*swap)(void*) = nullptr;
    bool (*current)(void*, bool) = nullptr;
    void* (*proc)(void*, const char*) = nullptr;
    void* opaque = nullptr;
    int engine = -1;
};
struct libvlc_instance_t {};
struct libvlc_media_player_t
{
    Callbacks callbacks;
    std::thread producer;
    bool alive = false;
};

namespace
{
struct State
{
    bool create = true, initialize = true, setCallbacks = true;
    bool resize = true, pump = true, bind = true, finish = true, unbind = true;
    bool begin = true, end = true, asynchronous = false;
    int capability = 5;
    std::atomic<int> initialized{0}, resized{0}, pumped{0}, bound{0}, finished{0};
    std::atomic<int> begun{0}, ended{0}, stoppedCount{0}, retired{0}, destroyed{0};
    std::atomic<int> waits{0}, rechecks{0}, incompleteRechecks{0};
    std::mutex completionMutex;
    std::condition_variable completion;
    bool pending = false, stopped = false;

    void AwaitWaits(int count)
    {
        std::unique_lock<std::mutex> lock(completionMutex);
        CHECK(completion.wait_for(lock, 2s, [&] { return waits >= count; }));
    }
    void CompleteConsumer()
    {
        std::lock_guard<std::mutex> lock(completionMutex);
        pending = false; completion.notify_all();
    }
};
std::shared_ptr<State> nextState;
libvlc_media_player_t playerStorage; // Deliberately reuse a native player address.
libvlc_instance_t library;
IUnityGraphics fakeGraphics{};
IUnityInterfaces fakeInterfaces{};
IUnityGraphicsDeviceEventCallback deviceCallback = nullptr;
int hookCalls = 0, configureCalls = 0, callbackRegistrations = 0, releases = 0;
constexpr int reservedEventBase = 712;

class FakeBackend final : public PortableGpuBackend
{
    std::shared_ptr<State> state;
    std::vector<std::unique_ptr<int>> surfaces;
    PortableTextureInfo texture;
    std::thread::id producerThread;
    bool current = false, completed = false, consuming = false;
public:
    explicit FakeBackend(std::shared_ptr<State> value) : state(std::move(value)) {}
    ~FakeBackend() override { ++state->destroyed; }
    bool Initialize(IUnityInterfaces* supplied, UnityGfxRenderer renderer) override
    {
        CHECK(supplied == &fakeInterfaces && renderer == kUnityGfxRendererVulkan);
        ++state->initialized; return state->initialize;
    }
    int Capability() const override { return state->capability; }
    int Engine() const override { return 1; }
    bool WaitForConsumer() override
    {
        std::unique_lock<std::mutex> lock(state->completionMutex);
        ++state->waits; state->completion.notify_all();
        state->completion.wait(lock, [&] { return !state->pending || state->stopped; });
        return !state->stopped;
    }
    bool IsConsumerComplete() const override
    {
        ++state->rechecks;
        // Simulate a consumer dependency appearing between the unlocked wait
        // and acquiring the context mutex; the producer must retry its wait.
        if (state->incompleteRechecks > 0) { --state->incompleteRechecks; return false; }
        std::lock_guard<std::mutex> lock(state->completionMutex);
        return !state->pending;
    }
    void StopProducer() override
    {
        std::lock_guard<std::mutex> lock(state->completionMutex);
        ++state->stoppedCount; state->stopped = true; state->completion.notify_all();
    }
    bool MakeCurrent(bool enter) override
    {
        if (enter)
        {
            CHECK(!current && !consuming);
            if (!state->bind) return false;
            current = true; completed = false; producerThread = std::this_thread::get_id();
            ++state->bound; return true;
        }
        CHECK(current && completed && producerThread == std::this_thread::get_id());
        current = false; return state->unbind;
    }
    bool Resize(unsigned width, unsigned height) override
    {
        CHECK(current && producerThread == std::this_thread::get_id());
        ++state->resized;
        if (!state->resize) return false;
        surfaces.push_back(std::make_unique<int>(state->resized.load()));
        texture = { surfaces.back().get(), width, height }; return true;
    }
    void* GetProcAddress(const char* name) override
    { return std::strcmp(name, "fakeFunction") == 0 ? &fakeGraphics : nullptr; }
    bool FrameComplete() override
    {
        CHECK(current && producerThread == std::this_thread::get_id());
        completed = true; ++state->finished; return state->finish;
    }
    bool Pump() override { ++state->pumped; return state->pump; }
    PortableTextureInfo TextureInfo() const override { return texture; }
    bool Begin() override
    { CHECK(!current && !consuming); consuming = true; ++state->begun; return state->begin; }
    bool End() override
    {
        CHECK(consuming && !current); consuming = false; ++state->ended;
        std::lock_guard<std::mutex> lock(state->completionMutex);
        state->pending = state->asynchronous; return state->end;
    }
    void Retire() override
    { CHECK(!current && !consuming && state->stopped); ++state->retired; surfaces.clear(); texture = {}; }
    const char* LastError() const override { return "injected test backend failure"; }
};

UnityGfxRenderer UNITY_INTERFACE_API Renderer() { return kUnityGfxRendererVulkan; }
void UNITY_INTERFACE_API RegisterDevice(IUnityGraphicsDeviceEventCallback callback) { CHECK(!deviceCallback); deviceCallback = callback; }
void UNITY_INTERFACE_API UnregisterDevice(IUnityGraphicsDeviceEventCallback callback) { CHECK(deviceCallback == callback); deviceCallback = nullptr; }
int UNITY_INTERFACE_API ReserveEvents(int count) { CHECK(count == 4); return reservedEventBase; }
IUnityInterface* UNITY_INTERFACE_API GetInterface(UnityInterfaceGUID guid)
{ return guid == GetUnityInterfaceGUID<IUnityGraphics>() ? &fakeGraphics : nullptr; }
}

void InstallPortableGpuHooks(IUnityInterfaces* supplied) { CHECK(supplied == &fakeInterfaces); ++hookCalls; }
void ConfigurePortableGpuEvents(IUnityInterfaces* supplied, UnityGfxRenderer renderer, int base)
{ CHECK(supplied == &fakeInterfaces && renderer == kUnityGfxRendererVulkan && base == reservedEventBase); ++configureCalls; }
std::unique_ptr<PortableGpuBackend> CreatePortableGpuBackend(UnityGfxRenderer renderer)
{ CHECK(renderer == kUnityGfxRendererVulkan && nextState); return nextState->create ? std::make_unique<FakeBackend>(nextState) : nullptr; }

extern "C"
{
libvlc_media_player_t* libvlc_media_player_new(libvlc_instance_t* instance)
{
    CHECK(instance == &library && !playerStorage.alive && !playerStorage.producer.joinable());
    playerStorage.callbacks = {}; playerStorage.alive = true; return &playerStorage;
}
void libvlc_media_player_release(libvlc_media_player_t* player)
{
    CHECK(player == &playerStorage && player->alive);
    // The actual export must stop a blocked producer BEFORE this join.
    if (player->producer.joinable()) player->producer.join();
    player->alive = false; ++releases;
}
const char* libvlc_get_version() { return "4.0.0-dev"; }
const char* libvlc_get_changeset() { return "4.0.0-dev-33583-gd94fd0473f"; }
bool libvlc_video_set_output_callbacks(libvlc_media_player_t* player, int engine,
    bool (*setup)(void**, const PortableSetupConfig*, PortableSetupInfo*), void (*cleanup)(void*),
    void* resizeCallback, bool (*update)(void*, const PortableRenderConfig*, PortableOutputConfig*),
    void (*swap)(void*), bool (*current)(void*, bool), void* (*proc)(void*, const char*),
    void* metadata, void* plane, void* opaque)
{
    CHECK(player->alive && !resizeCallback && !metadata && !plane);
    ++callbackRegistrations;
    player->callbacks = { setup, cleanup, update, swap, current, proc, opaque, engine };
    return nextState->setCallbacks;
}
libvlc_media_player_t* libvlc_unity_media_player_new(libvlc_instance_t*);
void libvlc_unity_media_player_release(libvlc_media_player_t*);
int libvlc_unity_get_capabilities(libvlc_media_player_t*);
void libvlc_unity_disable_gpu(libvlc_media_player_t*);
void* libvlc_unity_get_render_context(libvlc_media_player_t*);
void* libvlc_unity_get_texture_info(libvlc_media_player_t*, unsigned*, unsigned*, std::uint64_t*);
UnityRenderingEventAndData libvlc_unity_get_render_event_func();
int libvlc_unity_get_render_event_base();
void libvlc_unity_set_color_space(int);
}

namespace
{
void Event(int kind, void* token) { libvlc_unity_get_render_event_func()(reservedEventBase + kind, token); }
libvlc_media_player_t* Create(bool initialize = true)
{
    CHECK(PortableGpu::players.empty());
    auto* player = libvlc_unity_media_player_new(&library);
    CHECK(player && libvlc_unity_get_capabilities(player) == 4);
    CHECK(libvlc_unity_get_render_context(player) != player);
    if (initialize) Event(0, libvlc_unity_get_render_context(player));
    return player;
}
void Release(libvlc_media_player_t* player)
{
    void* token = libvlc_unity_get_render_context(player);
    libvlc_unity_media_player_release(player); Event(3, token);
    CHECK(PortableGpu::players.empty() && PortableGpu::contexts.empty());
}
void Produce(libvlc_media_player_t* player, unsigned width, unsigned height)
{
    auto callbacks = player->callbacks;
    CHECK(callbacks.current(callbacks.opaque, true));
    PortableRenderConfig config{ width, height, 8, false, 0, 0, 0, nullptr };
    PortableOutputConfig output{};
    CHECK(callbacks.update(callbacks.opaque, &config, &output));
    CHECK(output.u.format == 0x1908 && output.fullRange);
    CHECK(output.colorSpace == 2 && output.primaries == 3 && output.orientation == 3);
    CHECK(output.transfer == (PortableGpu::colorSpace == 1 ? 1 : 2));
    callbacks.swap(callbacks.opaque);
    CHECK(callbacks.current(callbacks.opaque, false));
}
void CheckNoTexture(libvlc_media_player_t* player)
{
    unsigned width = 999, height = 999; std::uint64_t version = 999;
    CHECK(!libvlc_unity_get_texture_info(player, &width, &height, &version));
    CHECK(width == 0 && height == 0 && version == 0);
}

void FramesAndRetirement()
{
    nextState = std::make_shared<State>(); auto state = nextState;
    auto* player = Create(false); void* token = libvlc_unity_get_render_context(player);
    CheckNoTexture(player); Event(3, token); // Live contexts cannot be retired.
    CHECK(PortableGpu::contexts.size() == 1);
    Event(0, token); CHECK(libvlc_unity_get_capabilities(player) == 5);
    CHECK(state->initialized == 1 && state->pumped == 1);
    const int initialRegistrations = callbackRegistrations;
    auto callbacks = player->callbacks;
    CHECK(callbacks.engine == 1 && callbacks.opaque == token);
    PortableSetupInfo setup{ &library, &library }; void* opaque = callbacks.opaque;
    CHECK(callbacks.setup(&opaque, nullptr, &setup) && opaque == token);
    CHECK(!setup.context && !setup.mutex);
    CHECK(callbacks.proc(opaque, "fakeFunction") == &fakeGraphics);
    CHECK(!callbacks.proc(opaque, "unknown"));
    libvlc_unity_set_color_space(1); Produce(player, 64, 48);
    unsigned width = 0, height = 0; std::uint64_t version = 0;
    void* oldTexture = libvlc_unity_get_texture_info(player, &width, &height, &version);
    CHECK(oldTexture && width == 64 && height == 48 && version == 1);
    libvlc_unity_set_color_space(0); Produce(player, 64, 48);
    CHECK(state->resized == 1);
    CHECK(callbacks.current(opaque, true));
    PortableRenderConfig config{79, 53, 8, false, 0, 0, 0, nullptr}; PortableOutputConfig output{};
    CHECK(callbacks.update(opaque, &config, &output)); CheckNoTexture(player);
    callbacks.swap(opaque); CHECK(callbacks.current(opaque, false));
    void* newTexture = libvlc_unity_get_texture_info(player, &width, &height, &version);
    CHECK(newTexture && newTexture != oldTexture && width == 79 && height == 53 && version == 3);
    CHECK(*static_cast<int*>(oldTexture) == 1); // Earlier queued blit remains valid.
    callbacks.cleanup(opaque); CheckNoTexture(player);
    Produce(player, 79, 53); CHECK(state->resized == 3);
    Event(0, token); CHECK(state->initialized == 1 && state->pumped == 2);
    CHECK(callbackRegistrations == initialRegistrations);
    std::weak_ptr<PortableGpu::Context> weak = PortableGpu::Find(player);
    libvlc_unity_media_player_release(player);
    CHECK(!PortableGpu::Find(player) && !weak.expired());
    CHECK(state->stoppedCount == 1 && state->retired == 0 && state->destroyed == 0);
    CHECK(!callbacks.setup(&opaque, nullptr, &setup)); CheckNoTexture(player);
    Event(0, token); CHECK(state->pumped == 2); // Late init cannot resurrect output.
    Event(1, token); Event(2, token); // Blit already queued before Dispose can finish.
    CHECK(state->begun == 1 && state->ended == 1);
    Event(3, token); CHECK(weak.expired() && state->retired == 1 && state->destroyed == 1);
    for (int kind = 0; kind != 4; ++kind) Event(kind, token);
    CHECK(state->retired == 1 && state->begun == 1 && PortableGpu::contexts.empty());
}

void PlayerAddressReuse()
{
    nextState = std::make_shared<State>(); auto old = nextState;
    auto* player = Create(); void* oldToken = libvlc_unity_get_render_context(player);
    Produce(player, 16, 16); libvlc_unity_media_player_release(player);
    nextState = std::make_shared<State>(); auto fresh = nextState;
    auto* replacement = Create(); CHECK(replacement == player);
    void* newToken = libvlc_unity_get_render_context(replacement); CHECK(newToken != oldToken);
    Event(0, oldToken); Event(1, oldToken); Event(2, oldToken); Event(3, oldToken);
    CHECK(old->retired == 1 && fresh->retired == 0 && fresh->begun == 0);
    CHECK(libvlc_unity_get_render_context(replacement) == newToken);
    Produce(replacement, 20, 21); Release(replacement); CHECK(fresh->retired == 1);
}

void ProducerConsumerOrdering()
{
    nextState = std::make_shared<State>(); auto state = nextState;
    auto* player = Create(); auto callbacks = player->callbacks;
    void* token = libvlc_unity_get_render_context(player);
    Event(2, token); Event(1, token); Event(1, token); // Unmatched/duplicate events.
    CHECK(state->begun == 1 && state->ended == 0);
    player->producer = std::thread([&] { Produce(player, 32, 24); });
    state->AwaitWaits(1); CHECK(state->bound == 0);
    Event(2, token); player->producer.join(); CHECK(state->ended == 1 && state->bound == 1);
    state->incompleteRechecks = 1;
    const int previousWaits = state->waits;
    CHECK(callbacks.current(callbacks.opaque, true)); CHECK(callbacks.current(callbacks.opaque, false));
    CHECK(state->waits == previousWaits + 2);
    Release(player);
}

void AsynchronousConsumer(bool shutdown)
{
    nextState = std::make_shared<State>(); auto state = nextState;
    state->capability = 6; state->asynchronous = true;
    auto* player = Create(); CHECK(libvlc_unity_get_capabilities(player) == 6);
    void* token = libvlc_unity_get_render_context(player); auto callbacks = player->callbacks;
    Event(1, token); Event(2, token);
    std::atomic<bool> producerExited{false};
    player->producer = std::thread([&]
    {
        CHECK(!callbacks.current(callbacks.opaque, true)); producerExited = true;
    });
    state->AwaitWaits(1); CHECK(!producerExited);
    const int pumps = state->pumped;
    Event(0, token); CHECK(state->pumped == pumps + 1); // Wait holds no context lock.
    if (shutdown)
    {
        deviceCallback(kUnityGfxDeviceEventShutdown);
        player->producer.join(); CHECK(producerExited && libvlc_unity_get_capabilities(player) == 0);
        CHECK(state->retired == 1 && state->destroyed == 0);
        CHECK(!callbacks.proc(callbacks.opaque, "fakeFunction"));
        deviceCallback(kUnityGfxDeviceEventShutdown); CHECK(state->retired == 1);
    }
    libvlc_unity_media_player_release(player); // BeforeRelease must wake before join.
    CHECK(producerExited && state->stoppedCount >= 1 && state->retired == (shutdown ? 1 : 0));
    state->CompleteConsumer(); Event(3, token);
    CHECK(state->retired == 1 && state->destroyed == 1);
    if (shutdown)
    {
        nextState = std::make_shared<State>(); auto unavailable = nextState;
        auto* latePlayer = Create();
        CHECK(libvlc_unity_get_capabilities(latePlayer) == 0 && unavailable->initialized == 0);
        Release(latePlayer); deviceCallback(kUnityGfxDeviceEventInitialize);
    }
}

void FailureFallbacks()
{
    for (int failure = 0; failure != 9; ++failure)
    {
        nextState = std::make_shared<State>(); auto state = nextState;
        if (failure == 0) state->create = false;
        if (failure == 1) state->initialize = false;
        if (failure == 2) state->setCallbacks = false;
        if (failure == 3) state->pump = false;
        auto* player = Create(); void* token = libvlc_unity_get_render_context(player);
        auto callbacks = player->callbacks;
        if (failure >= 4)
        {
            CHECK(libvlc_unity_get_capabilities(player) == 5);
            if (failure == 4)
            {
                state->resize = false; CHECK(callbacks.current(token, true));
                PortableRenderConfig config{32, 24, 8, false, 0, 0, 0, nullptr}; PortableOutputConfig output{};
                CHECK(!callbacks.update(token, &config, &output)); CHECK(callbacks.current(token, false));
            }
            if (failure == 5) { state->bind = false; CHECK(!callbacks.current(token, true)); }
            if (failure == 6 || failure == 7)
            {
                CHECK(callbacks.current(token, true));
                if (failure == 6) state->finish = false; else state->unbind = false;
                CHECK(!callbacks.current(token, false));
            }
            if (failure == 8)
            {
                state->begin = false; Event(1, token); Event(2, token);
                CHECK(state->ended == 1); // Failed Begin must still release the bracket.
            }
        }
        CHECK(libvlc_unity_get_capabilities(player) == 0); CheckNoTexture(player);
        const int previousBegins = state->begun;
        Event(1, token); Event(2, token); CHECK(state->begun == previousBegins);
        Release(player); CHECK(state->retired == (state->create ? 1 : 0));
    }
    nextState = std::make_shared<State>(); auto state = nextState;
    auto* player = Create(false);
    libvlc_unity_disable_gpu(player); Event(0, libvlc_unity_get_render_context(player));
    CHECK(libvlc_unity_get_capabilities(player) == 0 && state->initialized == 0);
    CHECK(player->callbacks.engine == 0 && !player->callbacks.setup && !player->callbacks.opaque);
    Release(player);
}

void QueuedBlitAfterFallback()
{
    for (int failure = 0; failure != 3; ++failure)
    {
        nextState = std::make_shared<State>(); auto state = nextState;
        auto* player = Create(); void* token = libvlc_unity_get_render_context(player);
        auto callbacks = player->callbacks;
        Produce(player, 64, 48);
        // Model the main thread's external texture snapshot and queued blit.
        void* published = libvlc_unity_get_texture_info(player, nullptr, nullptr, nullptr);
        CHECK(published && *static_cast<int*>(published) == 1);
        if (failure == 0)
        {
            // A preceding import event discovers an incompatible new surface.
            state->pump = false; Event(0, token);
        }
        else if (failure == 1)
        {
            // A producer resize can fail after the old handle was published.
            state->resize = false; CHECK(callbacks.current(token, true));
            PortableRenderConfig config{79, 53, 8, false, 0, 0, 0, nullptr}; PortableOutputConfig output{};
            CHECK(!callbacks.update(token, &config, &output)); CHECK(callbacks.current(token, false));
        }
        else libvlc_unity_disable_gpu(player);
        CHECK(libvlc_unity_get_capabilities(player) == 0); CheckNoTexture(player);
        CHECK(!callbacks.current(token, true)); // No new producer work is accepted.
        Event(1, token); CHECK(state->begun == 1 && state->ended == 0);
        CHECK(*static_cast<int*>(published) == 1); // The queued blit still samples this storage.
        Event(2, token); CHECK(state->ended == 1);
        CHECK(libvlc_unity_get_capabilities(player) == 0); // Draining cannot re-enable interop.
        Release(player); CHECK(state->retired == 1 && state->destroyed == 1);
    }
}
}

int main()
{
    // Turn a mutex regression into a bounded test failure rather than a hung CI.
    std::mutex watchdogMutex; std::condition_variable watchdogCondition; bool done = false;
    std::thread watchdog([&]
    {
        std::unique_lock<std::mutex> lock(watchdogMutex);
        if (!watchdogCondition.wait_for(lock, 15s, [&] { return done; }))
        { std::fputs("FAIL: GPU context lifecycle test timed out\n", stderr); std::_Exit(2); }
    });
    fakeGraphics.GetRenderer = Renderer; fakeGraphics.RegisterDeviceEventCallback = RegisterDevice;
    fakeGraphics.UnregisterDeviceEventCallback = UnregisterDevice; fakeGraphics.ReserveEventIDRange = ReserveEvents;
    fakeInterfaces.GetInterface = GetInterface;
    UnityPluginLoad(&fakeInterfaces);
    CHECK(hookCalls == 1 && configureCalls == 1 && deviceCallback);
    CHECK(libvlc_unity_get_render_event_base() == reservedEventBase);
    FramesAndRetirement(); PlayerAddressReuse(); ProducerConsumerOrdering();
    AsynchronousConsumer(false); AsynchronousConsumer(true); FailureFallbacks(); QueuedBlitAfterFallback();
    CHECK(PortableGpu::players.empty() && PortableGpu::contexts.empty());
    UnityPluginUnload(); CHECK(!deviceCallback && !PortableGpu::interfaces && !PortableGpu::graphics);
    CHECK(releases == 20 && !playerStorage.alive && !playerStorage.producer.joinable());
    { std::lock_guard<std::mutex> lock(watchdogMutex); done = true; watchdogCondition.notify_all(); }
    watchdog.join();
    std::puts("PASS: GPU callbacks, frame/resize publication, producer exclusion, async stop/shutdown, fallback blit draining, player reuse and retirement");
    return 0;
}
