// Windows VLCUnity bridge: D3D11 decoding/rendering, API-specific GPU consumers.
// Pixel data never enters CPU memory on the GPU paths. The first implementation
// deliberately serializes producer/consumer GPU completion instead of relying on
// Flush() alone, which does not prevent tearing or cross-device data races.
#define NOMINMAX
#include <windows.h>
#include <d3d11_1.h>
#include <d3d12.h>
#include <dxgi1_4.h>
#include <wrl/client.h>
#include <atomic>
#include <memory>
#include <mutex>
#include <unordered_map>
#include <vector>
#include "Unity/IUnityGraphics.h"
#include "Unity/IUnityGraphicsD3D11.h"
#include "Unity/IUnityGraphicsD3D12.h"
#include "LibVlcAbi.h"
#include "OpenGLInterop.h"
#if __has_include("VulkanInterop.h")
#include "VulkanInterop.h"
#define VLC_HAS_VULKAN_INTEROP 1
#else
#define VLC_HAS_VULKAN_INTEROP 0
#endif

using Microsoft::WRL::ComPtr;
#define API extern "C" __declspec(dllexport)

namespace
{
enum Backend { Cpu = 0, D3D11 = 1, D3D12 = 2, OpenGL = 3, Pending = 4, Vulkan = 5 };
IUnityInterfaces* interfaces = nullptr;
IUnityGraphics* graphics = nullptr;
int renderEventBase = 0;
IUnityGraphicsD3D12v7* unity12 = nullptr;
ComPtr<ID3D11Device> unity11Device;
ComPtr<ID3D11DeviceContext> unity11Context;
ComPtr<ID3D12Device> unity12Device;
ComPtr<ID3D12CommandQueue> unity12Queue;
LibVlcApi vlc;
std::atomic<int> colorSpace{0};
std::atomic<bool> shuttingDown{false};

bool Wait11(ID3D11DeviceContext* context, ID3D11Query* query)
{
    context->End(query);
    context->Flush();
    HRESULT status;
    while ((status = context->GetData(query, nullptr, 0, 0)) == S_FALSE)
    {
        if (shuttingDown.load()) return false;
        Sleep(0);
    }
    return status == S_OK;
}

struct Surface
{
    unsigned width = 0, height = 0;
    ComPtr<ID3D11Texture2D> producer;
    ComPtr<ID3D11RenderTargetView> target;
    ComPtr<IDXGIKeyedMutex> keyedMutex;
    ComPtr<ID3D11Texture2D> consumer11;
    ComPtr<ID3D11ShaderResourceView> view11;
    ComPtr<ID3D12Resource> consumer12;
    OpenGLInterop::Texture consumerGL;
#if VLC_HAS_VULKAN_INTEROP
    VulkanInterop::Texture consumerVK;
#endif
    bool registered = false;
    bool presented = false;
    void* native = nullptr;
};

struct Context
{
    libvlc_media_player_t* player = nullptr;
    std::atomic<int> backend{Pending};
    std::atomic<bool> retired{false};
    // Only the producer owns this mutex outside rendering events. Rendering
    // acquires it in Begin and releases it in End after Unity's blit completes.
    std::recursive_mutex mutex;
    bool consuming = false;
    bool acquireFailed = false;
    Surface* keyedAcquired = nullptr;
    bool hardwareDecoding = true;
    std::uint64_t version = 0;
    Surface* current = nullptr;
    std::vector<std::unique_ptr<Surface>> surfaces;
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> immediate;
    ComPtr<ID3D11Query> producerQuery, consumerQuery;
    ComPtr<ID3D12Fence> consumerFence;
    UINT64 fenceValue = 0;
    HANDLE fenceEvent = nullptr;
    HANDLE contextMutex = nullptr;
    OpenGLInterop gl;
#if VLC_HAS_VULKAN_INTEROP
    VulkanInterop vk;
#endif

    ~Context()
    {
        // Destruction is deferred to event 3, on Unity's render thread.
        for (auto& surface : surfaces)
        {
            if (surface->consumerGL.name) gl.UnregisterTexture(surface->consumerGL);
#if VLC_HAS_VULKAN_INTEROP
            vk.UnregisterTexture(surface->consumerVK);
#endif
        }
        gl.Shutdown();
#if VLC_HAS_VULKAN_INTEROP
        vk.Shutdown();
#endif
        if (fenceEvent) CloseHandle(fenceEvent);
        if (contextMutex) CloseHandle(contextMutex);
    }

    bool Wait12()
    {
        if (!unity12Queue || !consumerFence || !fenceEvent) return false;
        const UINT64 value = ++fenceValue;
        if (FAILED(unity12Queue->Signal(consumerFence.Get(), value))) return false;
        if (consumerFence->GetCompletedValue() >= value) return true;
        if (FAILED(consumerFence->SetEventOnCompletion(value, fenceEvent))) return false;
        return WaitForSingleObject(fenceEvent, 10000) == WAIT_OBJECT_0;
    }

    bool Initialize()
    {
        std::lock_guard<std::recursive_mutex> lock(mutex);
        if (backend != Pending) return backend != Cpu;
        if (!graphics || retired) { backend = Cpu; return false; }
        const auto renderer = graphics->GetRenderer();
        ComPtr<IDXGIAdapter> adapter;
        LUID luid{};
        bool useLuid = false;
        int selected = Cpu;
        if (renderer == kUnityGfxRendererD3D11 && unity11Device)
        {
            ComPtr<IDXGIDevice> dxgi;
            if (FAILED(unity11Device.As(&dxgi)) || FAILED(dxgi->GetAdapter(&adapter)))
                { backend = Cpu; return false; }
            selected = D3D11;
        }
        else if (renderer == kUnityGfxRendererD3D12 && unity12Device && unity12Queue)
        {
            luid = unity12Device->GetAdapterLuid(); useLuid = true; selected = D3D12;
        }
        else if (renderer == kUnityGfxRendererOpenGLCore)
            selected = OpenGL;
#if VLC_HAS_VULKAN_INTEROP
        else if (renderer == kUnityGfxRendererVulkan && VulkanInterop::GetAdapterLuid(luid))
            { useLuid = true; selected = Vulkan; }
#endif
        if (selected == Cpu) { backend = Cpu; return false; }
        if (useLuid)
        {
            ComPtr<IDXGIFactory4> factory;
            if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory))) ||
                FAILED(factory->EnumAdapterByLuid(luid, IID_PPV_ARGS(&adapter))))
                { backend = Cpu; return false; }
        }
        auto createDevice = [this](IDXGIAdapter* candidate)
        {
            device.Reset(); immediate.Reset(); hardwareDecoding = true;
            UINT flags = D3D11_CREATE_DEVICE_VIDEO_SUPPORT | D3D11_CREATE_DEVICE_BGRA_SUPPORT;
            const auto driver = candidate ? D3D_DRIVER_TYPE_UNKNOWN : D3D_DRIVER_TYPE_HARDWARE;
            HRESULT result = D3D11CreateDevice(candidate, driver,
                nullptr, flags, nullptr, 0, D3D11_SDK_VERSION, &device, nullptr, &immediate);
            if (FAILED(result))
            {
                // Software decoding may still render through D3D11 without readback.
                hardwareDecoding = false;
                flags &= ~D3D11_CREATE_DEVICE_VIDEO_SUPPORT;
                result = D3D11CreateDevice(candidate, driver,
                    nullptr, flags, nullptr, 0, D3D11_SDK_VERSION, &device, nullptr, &immediate);
            }
            return SUCCEEDED(result);
        };
        bool deviceReady = createDevice(adapter.Get());
        if (selected == OpenGL)
        {
            bool shared = deviceReady && gl.Initialize(device.Get());
            // WGL may run on a different GPU from the default DXGI adapter on
            // hybrid systems. The interop driver is the authority on whether
            // a candidate can share with the current Unity GL context.
            if (!shared)
            {
                ComPtr<IDXGIFactory1> factory;
                if (SUCCEEDED(CreateDXGIFactory1(IID_PPV_ARGS(&factory))))
                {
                    for (UINT index = 0; !shared; ++index)
                    {
                        ComPtr<IDXGIAdapter1> candidate;
                        if (factory->EnumAdapters1(index, &candidate) == DXGI_ERROR_NOT_FOUND) break;
                        if (!candidate) break;
                        DXGI_ADAPTER_DESC1 description{};
                        if (FAILED(candidate->GetDesc1(&description)) || (description.Flags & DXGI_ADAPTER_FLAG_SOFTWARE)) continue;
                        shared = createDevice(candidate.Get()) && gl.Initialize(device.Get());
                    }
                }
            }
            if (!shared) { backend = Cpu; return false; }
            deviceReady = true;
        }
        if (!deviceReady) { backend = Cpu; return false; }
        ComPtr<ID3D10Multithread> multithread;
        if (FAILED(immediate.As(&multithread))) { backend = Cpu; return false; }
        multithread->SetMultithreadProtected(TRUE);
        contextMutex = CreateMutexW(nullptr, FALSE, nullptr);
        D3D11_QUERY_DESC query{D3D11_QUERY_EVENT, 0};
        if (!contextMutex || FAILED(device->CreateQuery(&query, &producerQuery)))
            { backend = Cpu; return false; }
        if (selected == D3D11 && FAILED(unity11Device->CreateQuery(&query, &consumerQuery)))
            { backend = Cpu; return false; }
        if (selected == D3D12)
        {
            fenceEvent = CreateEventW(nullptr, FALSE, FALSE, nullptr);
            if (!fenceEvent || FAILED(unity12Device->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&consumerFence))))
                { backend = Cpu; return false; }
        }
#if VLC_HAS_VULKAN_INTEROP
        if (selected == Vulkan && !vk.Initialize()) { backend = Cpu; return false; }
#endif
        backend = selected;
        // Probe sharing before Play, so a driver lacking interop has a reliable
        // CPU fallback rather than failing halfway through the first video.
        if (!CreateSurface(16, 16) || !RegisterSurfaces()) { backend = Cpu; return false; }
        current = nullptr;
        if (!vlc.output(player, 3, Setup, Cleanup, nullptr, Update, Swap, MakeCurrent,
            nullptr, nullptr, SelectPlane, this)) { backend = Cpu; return false; }
        return true;
    }

    bool CreateSurface(unsigned width, unsigned height)
    {
        if (!width || !height || width > D3D11_REQ_TEXTURE2D_U_OR_V_DIMENSION || height > D3D11_REQ_TEXTURE2D_U_OR_V_DIMENSION)
            return false;
        for (auto& existing : surfaces)
            if (existing->width == width && existing->height == height)
            { current = existing.get(); current->presented = false; return true; }
        // A pathological adaptive stream must not retain unbounded external
        // textures. CPU fallback handles further distinct output sizes.
        if (surfaces.size() >= 9) return false;
        auto surface = std::make_unique<Surface>();
        surface->width = width; surface->height = height;
        D3D11_TEXTURE2D_DESC desc{};
        desc.Width = width; desc.Height = height;
        desc.MipLevels = desc.ArraySize = desc.SampleDesc.Count = 1;
        desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        desc.Usage = D3D11_USAGE_DEFAULT;
        desc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
        desc.MiscFlags = D3D11_RESOURCE_MISC_SHARED_NTHANDLE |
            (backend == Vulkan ? D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX : D3D11_RESOURCE_MISC_SHARED);
        if (FAILED(device->CreateTexture2D(&desc, nullptr, &surface->producer)) ||
            FAILED(device->CreateRenderTargetView(surface->producer.Get(), nullptr, &surface->target))) return false;
        if (backend == Vulkan && FAILED(surface->producer.As(&surface->keyedMutex))) return false;
        current = surface.get();
        surfaces.push_back(std::move(surface));
        return true;
    }

    bool RegisterSurfaces()
    {
        for (auto& surface : surfaces)
        {
            if (surface->registered) continue;
            HANDLE shared = nullptr;
            if (backend == D3D11 || backend == D3D12)
            {
                ComPtr<IDXGIResource1> resource;
                if (FAILED(surface->producer.As(&resource)) ||
                    FAILED(resource->CreateSharedHandle(nullptr, DXGI_SHARED_RESOURCE_READ | DXGI_SHARED_RESOURCE_WRITE, nullptr, &shared))) return false;
            }
            HRESULT result = S_OK;
            if (backend == D3D11)
            {
                ComPtr<ID3D11Device1> device1;
                result = unity11Device.As(&device1);
                if (SUCCEEDED(result)) result = device1->OpenSharedResource1(shared, IID_PPV_ARGS(&surface->consumer11));
                if (SUCCEEDED(result)) result = unity11Device->CreateShaderResourceView(surface->consumer11.Get(), nullptr, &surface->view11);
                surface->native = surface->view11.Get();
            }
            else if (backend == D3D12)
            {
                result = unity12Device->OpenSharedHandle(shared, IID_PPV_ARGS(&surface->consumer12));
                // Shared D3D11 resources open in COMMON. Simultaneous-access
                // textures promote to read states and decay back to COMMON at
                // ExecuteCommandLists completion; End waits that completion
                // before permitting D3D11 writes. Reject resources for which
                // that cross-API state contract cannot hold.
                if (SUCCEEDED(result) && !(surface->consumer12->GetDesc().Flags & D3D12_RESOURCE_FLAG_ALLOW_SIMULTANEOUS_ACCESS))
                    result = E_FAIL;
                surface->native = surface->consumer12.Get();
            }
            else if (backend == OpenGL)
            {
                if (!gl.RegisterTexture(surface->producer.Get(), surface->consumerGL)) result = E_FAIL;
                surface->native = surface->consumerGL.NativePointer();
            }
#if VLC_HAS_VULKAN_INTEROP
            else if (backend == Vulkan)
            {
                if (!vk.RegisterTexture(surface->producer.Get(), surface->consumerVK)) result = E_FAIL;
                surface->native = surface->consumerVK.NativePointer();
            }
#endif
            if (shared) CloseHandle(shared);
            if (FAILED(result)) return false;
            surface->registered = true;
        }
        return true;
    }

    bool Begin()
    {
        mutex.lock();
        if (consuming || backend == Cpu || backend == Pending) { mutex.unlock(); return false; }
        consuming = true;
        acquireFailed = false;
        for (auto& surface : surfaces)
        {
            if (!surface->registered) continue;
            if (backend == OpenGL && !gl.Lock(surface->consumerGL)) { acquireFailed = true; return false; }
#if VLC_HAS_VULKAN_INTEROP
            if (backend == Vulkan && !vk.Lock(surface->consumerVK)) { acquireFailed = true; return false; }
#endif
        }
        return true;
    }

    void End()
    {
        if (!consuming) return;
        bool completed = true;
        if (backend == D3D11) completed = Wait11(unity11Context.Get(), consumerQuery.Get());
        if (backend == D3D12) completed = Wait12();
        for (auto& surface : surfaces)
        {
            if (!surface->registered) continue;
            if (backend == OpenGL) completed = gl.Unlock(surface->consumerGL) && completed;
#if VLC_HAS_VULKAN_INTEROP
            if (backend == Vulkan) completed = vk.Unlock(surface->consumerVK) && completed;
#endif
        }
        if (!completed || acquireFailed) backend = Cpu; // Never let the producer overwrite a still-owned surface.
        consuming = false;
        mutex.unlock();
    }

    static bool Setup(void** opaque, const SetupConfig* config, SetupInfo* output)
    {
        auto& self = *static_cast<Context*>(*opaque);
        if (!self.immediate || self.retired || (config->hardwareDecoding && !self.hardwareDecoding)) return false;
        output->deviceContext = self.immediate.Get();
        output->contextMutex = self.contextMutex;
        return true;
    }
    static void Cleanup(void* opaque)
    {
        auto& self = *static_cast<Context*>(opaque);
        std::lock_guard<std::recursive_mutex> lock(self.mutex);
        // Stop/reload must not publish a frame from the preceding media. Only
        // the publication state is cleared; queued Unity commands still own
        // the external surfaces until the final render-thread cleanup event.
        self.current = nullptr;
        for (auto& surface : self.surfaces) surface->presented = false;
    }
    static bool Update(void* opaque, const RenderConfig* config, OutputConfig* output)
    {
        auto& self = *static_cast<Context*>(opaque);
        std::lock_guard<std::recursive_mutex> lock(self.mutex);
        if (self.retired || self.backend == Cpu) return false;
        if (!self.current || self.current->width != config->width || self.current->height != config->height)
            if (!self.CreateSurface(config->width, config->height)) { self.backend = Cpu; return false; }
        output->format.dxgiFormat = DXGI_FORMAT_R8G8B8A8_UNORM;
        output->fullRange = true;
        output->colorSpace = 2; output->primaries = 3;
        output->transfer = colorSpace == 1 ? 1 : 2;
        output->orientation = 1; // Same orientation as the bundled D3D11 plugin.
        return true;
    }
    static bool MakeCurrent(void* opaque, bool enter)
    {
        auto& self = *static_cast<Context*>(opaque);
        if (!enter)
        {
            // LibVLC calls Swap outside make-current in some output paths.
            // Complete GPU writes before letting Unity acquire the CPU lock.
            if (!Wait11(self.immediate.Get(), self.producerQuery.Get())) self.backend = Cpu;
            if (self.keyedAcquired) { self.keyedAcquired->keyedMutex->ReleaseSync(0); self.keyedAcquired = nullptr; }
            self.mutex.unlock(); return true;
        }
        self.mutex.lock();
        if (self.retired || self.backend == Cpu) { self.mutex.unlock(); return false; }
        if (self.current && self.current->keyedMutex)
        {
            if (self.current->keyedMutex->AcquireSync(0, 10000) != S_OK) { self.mutex.unlock(); return false; }
            self.keyedAcquired = self.current;
        }
        return true;
    }
    static bool SelectPlane(void* opaque, size_t plane, void* output)
    {
        auto& self = *static_cast<Context*>(opaque);
        if (plane != 0 || !self.current) return false;
        if (self.current->keyedMutex && self.keyedAcquired != self.current)
        {
            if (self.keyedAcquired) self.keyedAcquired->keyedMutex->ReleaseSync(0);
            self.keyedAcquired = nullptr;
            if (self.current->keyedMutex->AcquireSync(0, 10000) != S_OK) return false;
            self.keyedAcquired = self.current;
        }
        *static_cast<ID3D11RenderTargetView**>(output) = self.current->target.Get();
        return true;
    }
    static void Swap(void* opaque)
    {
        auto& self = *static_cast<Context*>(opaque);
        std::lock_guard<std::recursive_mutex> lock(self.mutex);
        if (self.retired || !self.current) return;
        if (Wait11(self.immediate.Get(), self.producerQuery.Get())) { ++self.version; self.current->presented = true; }
        else self.backend = Cpu;
    }
};

std::mutex registryMutex;
std::unordered_map<libvlc_media_player_t*, std::shared_ptr<Context>> players;
std::unordered_map<Context*, std::shared_ptr<Context>> contexts;

std::shared_ptr<Context> FindPlayer(libvlc_media_player_t* player)
{
    std::lock_guard<std::mutex> lock(registryMutex);
    auto found = players.find(player);
    return found == players.end() ? nullptr : found->second;
}

void UNITY_INTERFACE_API RenderEvent(int event, void* opaque)
{
    event -= renderEventBase;
    std::shared_ptr<Context> context;
    {
        std::lock_guard<std::mutex> lock(registryMutex);
        auto found = contexts.find(static_cast<Context*>(opaque));
        if (found == contexts.end()) return;
        context = found->second;
    }
    if (event == 0)
    {
        if (context->retired) return;
        context->Initialize();
        std::lock_guard<std::recursive_mutex> lock(context->mutex);
        if (context->backend != Cpu && !context->RegisterSurfaces()) context->backend = Cpu;
    }
    else if (event == 1) context->Begin();
    else if (event == 2) context->End();
    else if (event == 3 && context->retired)
    {
        std::lock_guard<std::mutex> lock(registryMutex);
        contexts.erase(context.get());
    }
}

void UNITY_INTERFACE_API GraphicsEvent(UnityGfxDeviceEventType event)
{
    if (event == kUnityGfxDeviceEventInitialize)
    {
        shuttingDown = false;
#if VLC_HAS_VULKAN_INTEROP
        if (graphics && graphics->GetRenderer() == kUnityGfxRendererVulkan)
            VulkanInterop::ConfigureEvents(renderEventBase);
#endif
        if (graphics && graphics->GetRenderer() == kUnityGfxRendererD3D11)
        {
            auto* api = interfaces->Get<IUnityGraphicsD3D11>();
            if (api) unity11Device = api->GetDevice();
            if (unity11Device) unity11Device->GetImmediateContext(&unity11Context);
        }
        unity12 = interfaces->Get<IUnityGraphicsD3D12v7>();
        if (unity12 && graphics && graphics->GetRenderer() == kUnityGfxRendererD3D12)
        {
            unity12Device = unity12->GetDevice(); unity12Queue = unity12->GetCommandQueue();
            UnityD3D12PluginEventConfig config{};
            config.graphicsQueueAccess = kUnityD3D12GraphicsQueueAccess_Allow;
            config.flags = kUnityD3D12EventConfigFlag_FlushCommandBuffers | kUnityD3D12EventConfigFlag_SyncWorkerThreads;
            for (int id = 0; id <= 3; ++id) unity12->ConfigureEvent(renderEventBase + id, &config);
        }
    }
    else if (event == kUnityGfxDeviceEventShutdown)
    {
        shuttingDown = true;
        std::lock_guard<std::mutex> lock(registryMutex);
        for (auto& item : contexts) item.second->backend = Cpu;
        unity11Context.Reset(); unity11Device.Reset(); unity12Queue.Reset(); unity12Device.Reset();
    }
}
void UNITY_INTERFACE_API LegacyRenderEvent(int) {}
}

API libvlc_media_player_t* libvlc_unity_media_player_new(libvlc_instance_t* instance)
{
    if (!instance || !vlc.Load()) return nullptr;
    auto* player = vlc.create(instance);
    if (!player) return nullptr;
    auto context = std::make_shared<Context>(); context->player = player;
    if (!graphics || graphics->GetRenderer() == kUnityGfxRendererNull) context->backend = Cpu;
    std::lock_guard<std::mutex> lock(registryMutex);
    players.emplace(player, context); contexts.emplace(context.get(), context);
    return player;
}
API void libvlc_unity_media_player_release(libvlc_media_player_t* player)
{
    if (!player) return;
    auto context = FindPlayer(player);
    // Do not hold either mutex while LibVLC joins callback threads.
    if (context) context->retired = true;
    if (vlc.release) vlc.release(player);
    std::lock_guard<std::mutex> lock(registryMutex);
    players.erase(player);
    // Context and surfaces remain owned by contexts until render event 3.
}
API void* libvlc_unity_get_render_context(libvlc_media_player_t* player)
{ auto context = FindPlayer(player); return context.get(); }
API int libvlc_unity_get_capabilities(libvlc_media_player_t* player)
{ auto context = FindPlayer(player); return context ? context->backend.load() : Cpu; }
API void libvlc_unity_disable_gpu(libvlc_media_player_t* player)
{
    auto context = FindPlayer(player); if (!context) return;
    std::lock_guard<std::recursive_mutex> lock(context->mutex);
    context->backend = Cpu;
    if (vlc.output) vlc.output(player, 0, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr);
}
API void* libvlc_unity_get_texture_info(libvlc_media_player_t* player, unsigned* width, unsigned* height, std::uint64_t* version)
{
    if (width) *width = 0; if (height) *height = 0; if (version) *version = 0;
    auto context = FindPlayer(player); if (!context) return nullptr;
    std::lock_guard<std::recursive_mutex> lock(context->mutex);
    if (!context->current || !context->current->registered || !context->current->presented || context->backend == Cpu) return nullptr;
    if (width) *width = context->current->width;
    if (height) *height = context->current->height;
    if (version) *version = context->version;
    return context->current->native;
}
API void* libvlc_unity_get_texture(libvlc_media_player_t* player, unsigned, unsigned, bool* updated)
{
    std::uint64_t version = 0;
    void* texture = libvlc_unity_get_texture_info(player, nullptr, nullptr, &version);
    if (updated) *updated = texture != nullptr;
    return texture;
}
API UnityRenderingEventAndData libvlc_unity_get_render_event_func() { return RenderEvent; }
API int libvlc_unity_get_render_event_base() { return renderEventBase; }
API UnityRenderingEvent GetRenderEventFunc() { return LegacyRenderEvent; }
API void libvlc_unity_set_color_space(int value) { colorSpace = value; }
API void Print(const char* value) { if (value) OutputDebugStringA(value); }
API void SetPluginPath(const char*) {}
API void UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* supplied)
{
    interfaces = supplied; graphics = supplied->Get<IUnityGraphics>();
    // Event configuration belongs to Unity's shared graphics backend. Reserve
    // our own IDs so another plugin cannot replace these synchronization flags.
    renderEventBase = graphics && graphics->ReserveEventIDRange ? graphics->ReserveEventIDRange(4) : 0;
#if VLC_HAS_VULKAN_INTEROP
    VulkanInterop::InstallHook(supplied);
#endif
    if (graphics) { graphics->RegisterDeviceEventCallback(GraphicsEvent); GraphicsEvent(kUnityGfxDeviceEventInitialize); }
}
API void UNITY_INTERFACE_API UnityPluginUnload()
{
    if (graphics) graphics->UnregisterDeviceEventCallback(GraphicsEvent);
    shuttingDown = true;
    // Active LibVLC owners must be disposed before plugin unload. Never invoke
    // GL/Vulkan destruction from the unload thread without a current context.
    interfaces = nullptr; graphics = nullptr;
}
API void UNITY_INTERFACE_API VLCUnity_UnityPluginLoad(IUnityInterfaces* supplied) { UnityPluginLoad(supplied); }
API void UNITY_INTERFACE_API VLCUnity_UnityPluginUnload() { UnityPluginUnload(); }
