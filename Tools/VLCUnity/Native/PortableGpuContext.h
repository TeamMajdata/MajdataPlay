#pragma once
#include "PortableGpuBackend.h"
#include <atomic>
#include <mutex>
#include <unordered_map>
#include <cstring>
#include <cstdio>
#include <vector>

struct libvlc_media_player_t;
struct PortableSetupConfig { bool hardwareDecoding; };
struct PortableSetupInfo { void* context; void* mutex; };
struct PortableRenderConfig
{
    unsigned width, height, bitDepth;
    bool fullRange;
    int colorSpace, primaries, transfer;
    void* device;
};
struct PortableOutputConfig
{
    union { int format; void* surface; } u;
    bool fullRange;
    int colorSpace, primaries, transfer, orientation;
};
using PortableSetOutputCallbacks = bool (*)(libvlc_media_player_t*, int,
    bool (*)(void**, const PortableSetupConfig*, PortableSetupInfo*), void (*)(void*),
    void*, bool (*)(void*, const PortableRenderConfig*, PortableOutputConfig*),
    void (*)(void*), bool (*)(void*, bool), void* (*)(void*, const char*), void*, void*, void*);

namespace PortableGpu
{
inline IUnityInterfaces* interfaces = nullptr;
inline IUnityGraphics* graphics = nullptr;
inline int eventBase = 0;
inline std::atomic<int> colorSpace{0};
inline std::atomic<bool> deviceReady{false};

struct Context
{
    libvlc_media_player_t* player = nullptr;
    PortableSetOutputCallbacks setOutput = nullptr;
    std::unique_ptr<PortableGpuBackend> gpu;
    std::recursive_mutex mutex;
    std::atomic<int> capability{4};
    std::atomic<bool> retired{false};
    bool consuming = false;
    bool resourcesRetired = false; // Protected by mutex; backend object outlives callbacks.
    bool publishedTexture = false; // Previously returned handles can still have queued Unity blits.
    bool frameReady = false;
    bool beginFailed = false;
    unsigned width = 0, height = 0;
    std::uint64_t version = 0;

    void Initialize()
    {
        std::lock_guard<std::recursive_mutex> guard(mutex);
        if (capability != 4 || retired || resourcesRetired) return;
        if (!deviceReady || !interfaces || !graphics || !setOutput) { capability = 0; return; }
        const auto renderer = graphics->GetRenderer();
        gpu = CreatePortableGpuBackend(renderer);
        if (!gpu || !gpu->Initialize(interfaces, renderer))
        {
            if (gpu) std::fprintf(stderr, "[VLCUnity] GPU initialization failed: %s\n", gpu->LastError());
            capability = 0; return;
        }
        if (!setOutput(player, gpu->Engine(), Setup, Cleanup, nullptr, Update, Swap,
            MakeCurrent, ProcAddress, nullptr, nullptr, this)) { capability = 0; return; }
        capability = gpu->Capability();
    }
    static bool Setup(void** opaque, const PortableSetupConfig*, PortableSetupInfo* output)
    {
        auto& self = *static_cast<Context*>(*opaque);
        if (output) std::memset(output, 0, sizeof(*output));
        return self.gpu && !self.retired && self.capability != 0;
    }
    static void Cleanup(void* opaque)
    {
        auto& self = *static_cast<Context*>(opaque);
        std::lock_guard<std::recursive_mutex> guard(self.mutex);
        self.frameReady = false; self.width = self.height = 0;
    }
    static bool Update(void* opaque, const PortableRenderConfig* config, PortableOutputConfig* output)
    {
        auto& self = *static_cast<Context*>(opaque);
        std::lock_guard<std::recursive_mutex> guard(self.mutex);
        if (!self.gpu || self.retired || self.capability == 0) return false;
        if (self.width != config->width || self.height != config->height)
        {
            self.frameReady = false;
            if (!self.gpu->Resize(config->width, config->height)) { self.capability = 0; return false; }
            self.width = config->width; self.height = config->height;
        }
        output->u.format = self.gpu->OutputFormat();
        output->fullRange = true; output->colorSpace = 2; output->primaries = 3;
        // GL uses bottom-left framebuffer coordinates, unlike D3D. Match the
        // upstream VLCUnity GL/GLES orientation so the managed double flip has
        // the same visible result as the Windows D3D11 top-right output.
        output->transfer = colorSpace == 1 ? 1 : 2; output->orientation = 3;
        return true;
    }
    static bool MakeCurrent(void* opaque, bool enter)
    {
        auto& self = *static_cast<Context*>(opaque);
        if (!enter)
        {
            const bool finished = self.gpu->FrameComplete();
            const bool unbound = self.gpu->MakeCurrent(false);
            if (!finished || !unbound) self.capability = 0;
            self.mutex.unlock();
            return finished && unbound;
        }
        for (;;)
        {
            if (self.retired || !self.gpu || self.capability == 0) return false;
            // Metal completion can depend on Unity submitting its command
            // buffer later. Never wait while blocking the render-event mutex.
            if (!self.gpu->WaitForConsumer()) { self.capability = 0; return false; }
            self.mutex.lock();
            if (self.gpu->IsConsumerComplete()) break;
            self.mutex.unlock();
        }
        if (self.retired || self.capability == 0 || !self.gpu->MakeCurrent(true))
        { self.capability = 0; self.mutex.unlock(); return false; }
        return true;
    }
    static void* ProcAddress(void* opaque, const char* name)
    {
        auto& self = *static_cast<Context*>(opaque);
        std::lock_guard<std::recursive_mutex> guard(self.mutex);
        return self.gpu && !self.retired && self.capability && !self.resourcesRetired ? self.gpu->GetProcAddress(name) : nullptr;
    }
    static void Swap(void* opaque)
    {
        auto& self = *static_cast<Context*>(opaque);
        std::lock_guard<std::recursive_mutex> guard(self.mutex);
        if (!self.retired && self.capability && self.width && self.height)
        { self.frameReady = true; ++self.version; }
    }
};
inline std::mutex registryMutex;
inline std::unordered_map<libvlc_media_player_t*, std::shared_ptr<Context>> players;
inline std::unordered_map<Context*, std::shared_ptr<Context>> contexts;

inline std::shared_ptr<Context> Find(libvlc_media_player_t* player)
{
    std::lock_guard<std::mutex> guard(registryMutex);
    auto found = players.find(player); return found == players.end() ? nullptr : found->second;
}
inline void Register(libvlc_media_player_t* player, PortableSetOutputCallbacks setter)
{
    auto context = std::make_shared<Context>(); context->player = player; context->setOutput = setter;
    if (!setter) context->capability = 0;
    std::lock_guard<std::mutex> guard(registryMutex);
    players.emplace(player, context); contexts.emplace(context.get(), context);
}
inline void BeforeRelease(libvlc_media_player_t* player)
{
    auto context = Find(player); if (!context) return;
    context->retired = true;
    // Initialization also assigns gpu while holding this mutex. Do not race
    // an immediate Dispose against the queued render-thread initialization.
    std::lock_guard<std::recursive_mutex> guard(context->mutex);
    if (context->gpu) context->gpu->StopProducer();
}
inline void AfterRelease(libvlc_media_player_t* player)
{ std::lock_guard<std::mutex> guard(registryMutex); players.erase(player); }
inline void Disable(libvlc_media_player_t* player)
{
    auto context = Find(player); if (!context) return;
    std::lock_guard<std::recursive_mutex> guard(context->mutex);
    context->capability = 0;
    if (context->setOutput) context->setOutput(player, 0, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr);
}
inline void* Texture(libvlc_media_player_t* player, unsigned* width, unsigned* height, std::uint64_t* version)
{
    auto context = Find(player); if (!context) return nullptr;
    std::lock_guard<std::recursive_mutex> guard(context->mutex);
    if (!context->gpu || context->retired || !context->capability || !context->frameReady) return nullptr;
    auto info = context->gpu->TextureInfo(); if (!info.texture) return nullptr;
    context->publishedTexture = true;
    if (width) *width = info.width;
    if (height) *height = info.height;
    if (version) *version = context->version;
    return info.texture;
}
inline void UNITY_INTERFACE_API RenderEvent(int event, void* opaque)
{
    std::shared_ptr<Context> context;
    {
        std::lock_guard<std::mutex> guard(registryMutex);
        auto found = contexts.find(static_cast<Context*>(opaque));
        if (found == contexts.end()) return;
        context = found->second;
    }
    event -= eventBase;
    if (event == 0)
    {
        if (context->retired) return;
        context->Initialize();
        std::lock_guard<std::recursive_mutex> guard(context->mutex);
        if (context->gpu && context->capability && !context->gpu->Pump())
        {
            std::fprintf(stderr, "[VLCUnity] GPU surface import failed: %s\n", context->gpu->LastError());
            context->capability = 0;
        }
    }
    else if (event == 1)
    {
        context->mutex.lock();
        // A capability failure stops new frames, but Unity may already have
        // queued a Blit for a texture returned before the failure. Its native
        // acquire/release must still run until the retirement event. Without
        // a published handle, a failed initialization has nothing to acquire.
        if (!context->gpu || context->consuming || (!context->capability && !context->publishedTexture) || context->resourcesRetired)
        { context->mutex.unlock(); return; }
        context->consuming = true;
        context->beginFailed = !context->gpu->Begin();
    }
    else if (event == 2 && context->consuming)
    {
        if (!context->resourcesRetired && (!context->gpu->End() || context->beginFailed)) context->capability = 0;
        context->consuming = false; context->mutex.unlock();
    }
    else if (event == 3 && context->retired)
    {
        {
            std::lock_guard<std::recursive_mutex> guard(context->mutex);
            if (context->gpu && !context->resourcesRetired) context->gpu->Retire();
            context->resourcesRetired = true;
        }
        std::lock_guard<std::mutex> guard(registryMutex); contexts.erase(context.get());
    }
}
inline void UNITY_INTERFACE_API GraphicsEvent(UnityGfxDeviceEventType event)
{
    if (!graphics || !interfaces) return;
    if (event == kUnityGfxDeviceEventInitialize)
    {
        ConfigurePortableGpuEvents(interfaces, graphics->GetRenderer(), eventBase);
        deviceReady = true;
    }
    if (event == kUnityGfxDeviceEventShutdown)
    {
        // Also reject players registered after the shutdown snapshot.
        deviceReady = false;
        std::vector<std::shared_ptr<Context>> snapshot;
        {
            std::lock_guard<std::mutex> guard(registryMutex);
            for (auto& item : contexts) snapshot.push_back(item.second);
        }
        for (auto& context : snapshot)
        {
            context->capability = 0;
            std::lock_guard<std::recursive_mutex> guard(context->mutex);
            // Initialize might have been holding the mutex and published a
            // capability after the first store. Disable it again under lock.
            context->capability = 0;
            if (context->gpu && !context->resourcesRetired)
            {
                context->gpu->StopProducer();
                // Unity still owns a live device during this callback. A later
                // event 3 could run after vkDestroyDevice, so release native
                // device resources now, once all current producer work left
                // the mutex. Keep the backend object itself until VLC joins:
                // WaitForConsumer may still be unwinding outside this lock.
                context->gpu->Retire();
            }
            context->resourcesRetired = true;
        }
    }
}
inline void Load(IUnityInterfaces* supplied)
{
    interfaces = supplied; graphics = supplied ? supplied->Get<IUnityGraphics>() : nullptr;
    eventBase = graphics && graphics->ReserveEventIDRange ? graphics->ReserveEventIDRange(4) : 0;
    InstallPortableGpuHooks(supplied);
    if (graphics) { graphics->RegisterDeviceEventCallback(GraphicsEvent); GraphicsEvent(kUnityGfxDeviceEventInitialize); }
}
inline void Unload()
{
    deviceReady = false;
    if (graphics) graphics->UnregisterDeviceEventCallback(GraphicsEvent);
    graphics = nullptr; interfaces = nullptr;
}
}
