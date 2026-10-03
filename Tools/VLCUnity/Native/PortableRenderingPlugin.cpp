// Native bridge for Linux, macOS, Android and iOS. GPU sharing is selected on
// the Unity render thread; managed RGBA callbacks provide the CPU fallback.
#include "Unity/IUnityGraphics.h"
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include <string>

#if !defined(VLCUNITY_STATIC_LIBVLC)
#include <dlfcn.h>
#endif

#ifndef VLCUNITY_REQUIRED_CHANGESET
#define VLCUNITY_REQUIRED_CHANGESET "d94fd0473f"
#endif

#if defined(_WIN32)
#define VLCUNITY_EXPORT extern "C" __declspec(dllexport)
#else
#define VLCUNITY_EXPORT extern "C" __attribute__((visibility("default")))
#endif

struct libvlc_instance_t;
struct libvlc_media_player_t;
#if defined(__ANDROID__)
#include "PortableAndroidJava.h"
#endif
#if defined(VLCUNITY_ENABLE_GPU)
#include "PortableGpuContext.h"
#endif

#if defined(VLCUNITY_STATIC_LIBVLC)
extern "C"
{
    libvlc_media_player_t* libvlc_media_player_new(libvlc_instance_t*);
    void libvlc_media_player_release(libvlc_media_player_t*);
    const char* libvlc_get_version();
    const char* libvlc_get_changeset();
#if defined(VLCUNITY_ENABLE_GPU)
    bool libvlc_video_set_output_callbacks(libvlc_media_player_t*, int,
        bool (*)(void**, const PortableSetupConfig*, PortableSetupInfo*), void (*)(void*),
        void*, bool (*)(void*, const PortableRenderConfig*, PortableOutputConfig*),
        void (*)(void*), bool (*)(void*, bool), void* (*)(void*, const char*), void*, void*, void*);
#endif
#if defined(VLCUNITY_IOS)
    // The matching decoder bundle supplies this adapter together with its
    // generated static-module registration data and complete module archives.
    void VLCUnityRegisterStaticModules();
#endif
}
#endif

namespace
{
using NewPlayer = libvlc_media_player_t* (*)(libvlc_instance_t*);
using ReleasePlayer = void (*)(libvlc_media_player_t*);
using TextQuery = const char* (*)();

// Error messages belong to the calling thread, so another player's concurrent
// initialization cannot replace a diagnostic before managed code reads it.
thread_local std::string lastError;
std::mutex apiMutex;
NewPlayer newPlayer = nullptr;
ReleasePlayer releasePlayer = nullptr;
TextQuery version = nullptr;
TextQuery changeset = nullptr;
#if defined(__ANDROID__)
PortableAndroidSetContext setAndroidContext = nullptr;
#endif
#if defined(VLCUNITY_ENABLE_GPU)
PortableSetOutputCallbacks setOutputCallbacks = nullptr;
#endif

#if !defined(VLCUNITY_STATIC_LIBVLC)
void* decoderLibrary = nullptr;

bool Bind(void* library)
{
    auto create = reinterpret_cast<NewPlayer>(dlsym(library, "libvlc_media_player_new"));
    auto release = reinterpret_cast<ReleasePlayer>(dlsym(library, "libvlc_media_player_release"));
    auto getVersion = reinterpret_cast<TextQuery>(dlsym(library, "libvlc_get_version"));
    auto getChangeset = reinterpret_cast<TextQuery>(dlsym(library, "libvlc_get_changeset"));
    if (!create || !release || !getVersion || !getChangeset) return false;
    newPlayer = create; releasePlayer = release; version = getVersion; changeset = getChangeset;
#if defined(__ANDROID__)
    setAndroidContext = reinterpret_cast<PortableAndroidSetContext>(dlsym(library, "libvlc_media_player_set_android_context"));
#endif
#if defined(VLCUNITY_ENABLE_GPU)
    setOutputCallbacks = reinterpret_cast<PortableSetOutputCallbacks>(dlsym(library, "libvlc_video_set_output_callbacks"));
#endif
    return true;
}

bool Open(const char* path)
{
    void* library = dlopen(path, RTLD_NOW | RTLD_LOCAL);
    if (!library) return false;
    if (!Bind(library)) { dlclose(library); return false; }
    // Retain one handle for the bridge lifetime. Managed LibVLC instances and
    // players may be finalized independently, and function pointers must stay
    // valid while their last native callbacks are being joined.
    decoderLibrary = library;
    return true;
}

bool LoadDecoder()
{
    if (newPlayer) return true;
    // The LibVLC instance normally exists already. Reuse a globally visible
    // binding first; do not mix two engine copies in a single process.
    if (Bind(RTLD_DEFAULT)) return true;
#if defined(__APPLE__)
    constexpr const char* filename = "libvlc.dylib";
#else
    constexpr const char* filename = "libvlc.so";
#endif
    // An adjacent dependency works without modifying the user's process-wide
    // library search path. Android packaged native libraries also resolve by
    // their SONAME through the app's linker namespace below.
    Dl_info location{};
    if (dladdr(reinterpret_cast<void*>(&LoadDecoder), &location) && location.dli_fname)
    {
        std::string adjacent(location.dli_fname);
        const auto slash = adjacent.find_last_of('/');
        if (slash != std::string::npos)
        {
            adjacent.resize(slash + 1); adjacent += filename;
            if (Open(adjacent.c_str())) return true;
        }
    }
    if (Open(filename)) return true;
#if defined(__linux__) && !defined(__ANDROID__)
    if (Open("libvlc.so.12")) return true;
#endif
    const char* loaderError = dlerror();
    lastError = "Unable to load the matching LibVLC 4 native engine";
    if (loaderError) { lastError += ": "; lastError += loaderError; }
    return false;
}
#else
bool LoadDecoder()
{
#if defined(VLCUNITY_IOS)
    static std::once_flag registration;
    std::call_once(registration, VLCUnityRegisterStaticModules);
#endif
    newPlayer = libvlc_media_player_new; releasePlayer = libvlc_media_player_release;
    version = libvlc_get_version; changeset = libvlc_get_changeset;
#if defined(VLCUNITY_ENABLE_GPU)
    setOutputCallbacks = libvlc_video_set_output_callbacks;
#endif
    return true;
}
#endif

bool ValidateDecoder()
{
    std::lock_guard<std::mutex> lock(apiMutex);
    lastError.clear();
    if (!LoadDecoder()) return false;
    const char* engineVersion = version();
    const char* engineChangeset = changeset();
    if (!engineVersion || std::strncmp(engineVersion, "4.", 2) != 0)
    {
        lastError = "This managed binding requires LibVLC 4; system LibVLC 3 is incompatible";
        return false;
    }
    constexpr const char* required = VLCUNITY_REQUIRED_CHANGESET;
    if (*required && (!engineChangeset || !std::strstr(engineChangeset, required)))
    {
        lastError = "LibVLC ABI mismatch: expected source changeset ";
        lastError += required; lastError += ", found ";
        lastError += engineChangeset ? engineChangeset : "unknown";
        lastError += ". New LibVLC 4 snapshots changed media_player_new; supply the pinned native bundle.";
        return false;
    }
    return true;
}

#if !defined(VLCUNITY_ENABLE_GPU)
void UNITY_INTERFACE_API RenderEvent(int, void*) {}
#endif
void UNITY_INTERFACE_API LegacyRenderEvent(int) {}
}

VLCUNITY_EXPORT int libvlc_unity_get_bridge_abi() { return 1; }
VLCUNITY_EXPORT int libvlc_unity_validate_libvlc() { return ValidateDecoder() ? 1 : 0; }
VLCUNITY_EXPORT const char* libvlc_unity_get_last_error() { return lastError.c_str(); }

VLCUNITY_EXPORT libvlc_media_player_t* libvlc_unity_media_player_new(libvlc_instance_t* instance)
{
    if (!instance) { lastError = "LibVLC instance is null"; return nullptr; }
    if (!ValidateDecoder()) return nullptr;
    auto* player = newPlayer(instance);
    if (!player) lastError = "LibVLC could not create a media player";
#if defined(__ANDROID__)
    if (player) PortableAndroidAttachPlayer(player, setAndroidContext);
#endif
#if defined(VLCUNITY_ENABLE_GPU)
    if (player) PortableGpu::Register(player, setOutputCallbacks);
#endif
    return player;
}

VLCUNITY_EXPORT void libvlc_unity_media_player_release(libvlc_media_player_t* player)
{
    if (!player) return;
#if defined(VLCUNITY_ENABLE_GPU)
    PortableGpu::BeforeRelease(player);
#endif
    // LibVLC release joins the video callbacks. Managed callback buffers must
    // be kept rooted until this call returns. Stop() alone is asynchronous.
    if (releasePlayer) releasePlayer(player);
#if defined(__ANDROID__)
    PortableAndroidReleasePlayer(player);
#endif
#if defined(VLCUNITY_ENABLE_GPU)
    PortableGpu::AfterRelease(player);
#endif
}

#if defined(VLCUNITY_ENABLE_GPU)
VLCUNITY_EXPORT int libvlc_unity_get_capabilities(libvlc_media_player_t* player)
{ auto context = PortableGpu::Find(player); return context ? context->capability.load() : 0; }
VLCUNITY_EXPORT void libvlc_unity_disable_gpu(libvlc_media_player_t* player) { PortableGpu::Disable(player); }
VLCUNITY_EXPORT void* libvlc_unity_get_render_context(libvlc_media_player_t* player)
{ auto context = PortableGpu::Find(player); return context.get(); }
#else
VLCUNITY_EXPORT int libvlc_unity_get_capabilities(libvlc_media_player_t*) { return 0; }
VLCUNITY_EXPORT void libvlc_unity_disable_gpu(libvlc_media_player_t*) {}
VLCUNITY_EXPORT void* libvlc_unity_get_render_context(libvlc_media_player_t* player)
{
    // CPU output owns no native render resources and render events never
    // dereference this token, even when queued after player disposal.
    return player;
}
#endif
VLCUNITY_EXPORT void* libvlc_unity_get_texture_info(libvlc_media_player_t* player, unsigned* width, unsigned* height, std::uint64_t* frame)
{
    if (width) *width = 0;
    if (height) *height = 0;
    if (frame) *frame = 0;
#if defined(VLCUNITY_ENABLE_GPU)
    return PortableGpu::Texture(player, width, height, frame);
#else
    (void)player;
    return nullptr;
#endif
}
VLCUNITY_EXPORT void* libvlc_unity_get_texture(libvlc_media_player_t* player, unsigned, unsigned, bool* updated)
{
    void* texture = libvlc_unity_get_texture_info(player, nullptr, nullptr, nullptr);
    if (updated) *updated = texture != nullptr;
    return texture;
}
#if defined(VLCUNITY_ENABLE_GPU)
VLCUNITY_EXPORT UnityRenderingEventAndData libvlc_unity_get_render_event_func() { return PortableGpu::RenderEvent; }
VLCUNITY_EXPORT int libvlc_unity_get_render_event_base() { return PortableGpu::eventBase; }
VLCUNITY_EXPORT void libvlc_unity_set_color_space(int value) { PortableGpu::colorSpace = value; }
#else
VLCUNITY_EXPORT UnityRenderingEventAndData libvlc_unity_get_render_event_func() { return RenderEvent; }
VLCUNITY_EXPORT int libvlc_unity_get_render_event_base() { return 0; }
VLCUNITY_EXPORT void libvlc_unity_set_color_space(int) {} // Managed CPU texture owns color-space conversion.
#endif
VLCUNITY_EXPORT UnityRenderingEvent GetRenderEventFunc() { return LegacyRenderEvent; }

VLCUNITY_EXPORT void SetPluginPath(const char* path)
{
    if (!path || !*path) return;
#if defined(_WIN32)
    _putenv_s("VLC_PLUGIN_PATH", path); // Host-only portable bridge unit tests.
#else
    setenv("VLC_PLUGIN_PATH", path, 1);
#endif
}
VLCUNITY_EXPORT void Print(const char* message)
{
    if (message) std::fprintf(stderr, "[VLCUnity] %s\n", message);
}

// iOS statically links several plugins into UnityFramework; omit generic
// UnityPluginLoad symbols to avoid clashes. LoadPlugin.mm registers the prefixed
// entrypoints for Unity's Metal graphics interface and render events.
#if !defined(VLCUNITY_IOS)
VLCUNITY_EXPORT void UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* supplied)
{
#if defined(VLCUNITY_ENABLE_GPU)
    PortableGpu::Load(supplied);
#else
    (void)supplied;
#endif
}
VLCUNITY_EXPORT void UNITY_INTERFACE_API UnityPluginUnload()
{
#if defined(VLCUNITY_ENABLE_GPU)
    PortableGpu::Unload();
#endif
}
#endif
VLCUNITY_EXPORT void UNITY_INTERFACE_API VLCUnity_UnityPluginLoad(IUnityInterfaces* supplied)
{
#if defined(VLCUNITY_ENABLE_GPU)
    PortableGpu::Load(supplied);
#else
    (void)supplied;
#endif
}
VLCUNITY_EXPORT void UNITY_INTERFACE_API VLCUnity_UnityPluginUnload()
{
#if defined(VLCUNITY_ENABLE_GPU)
    PortableGpu::Unload();
#endif
}
