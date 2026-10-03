#pragma once

#include "Unity/IUnityGraphics.h"
#include <cstdint>
#include <memory>

// All platforms use LibVLC's OpenGL/GLES output callbacks. The shared bridge
// owns producer/consumer exclusion and media-player lifetime. Backend methods
// never expose CPU pixel buffers and never call managed Unity APIs.
struct PortableTextureInfo
{
    void* texture = nullptr; // GL GLuint cast to void*, Vulkan VkImage*, or MTLTexture*.
    unsigned width = 0;
    unsigned height = 0;
};

class PortableGpuBackend
{
public:
    virtual ~PortableGpuBackend() = default;
    // Unity render thread, current graphics context available. Probe the exact
    // interop capability here. False selects CPU output before playback begins.
    virtual bool Initialize(IUnityInterfaces* interfaces, UnityGfxRenderer renderer) = 0;
    virtual int Capability() const = 0; // 3 OpenGL/GLES, 5 Vulkan, 6 Metal.
    virtual int Engine() const = 0; // libvlc_video_engine_opengl=1, gles2=2.
    virtual int OutputFormat() const { return 0x1908; } // LibVLC GL_RGBA output; storage itself is RGBA8.
    // An API such as Metal cannot wait on Unity's unsubmitted command buffer
    // from inside a render event. End may record an asynchronous completion;
    // the producer waits OUTSIDE the bridge mutex, then checks again while
    // holding it. Implementations must synchronize their completion state.
    virtual bool WaitForConsumer() { return true; }
    virtual bool IsConsumerComplete() const { return true; }
    // Called before libvlc_media_player_release joins callbacks. Interrupt any
    // producer-side wait on unsubmitted Unity work; defer owned resources to
    // its completion handler if the consumer GPU is still using them.
    virtual void StopProducer() {}

    // VLC producer thread; bridge mutex held. true binds its independent GL
    // context; false unbinds. Never move Unity's own context to this thread.
    virtual bool MakeCurrent(bool enter) = 0;
    // VLC output-update callback, producer GL context current. Preserve every
    // previously exposed native handle until Retire, or reuse same-size storage
    // only while the bridge's producer/consumer mutex is held.
    virtual bool Resize(unsigned width, unsigned height) = 0;
    virtual void* GetProcAddress(const char* name) = 0;
    // Called BEFORE MakeCurrent(false)/unlock. Must complete GPU writes (e.g.
    // glFinish), not merely submit them, unless a real shared fence is used.
    virtual bool FrameComplete() = 0;

    // Unity render thread, bridge mutex held. Import newly allocated surfaces
    // here if the consumer API requires its rendering context. False triggers
    // stop + managed CPU fallback. TextureInfo describes the current surface.
    virtual bool Pump() = 0;
    virtual PortableTextureInfo TextureInfo() const = 0;
    // Bracket Unity's queued Blit. End either completes consumer GPU reads or
    // records a completion dependency honored by WaitForConsumer/IsConsumerComplete.
    // Lock every exposed retained surface if queued Blits may still
    // reference an older resolution. Unity's original context must be restored.
    virtual bool Begin() = 0;
    virtual bool End() = 0;
    // Render thread after LibVLC joins and queued events complete, OR during
    // device shutdown after the bridge excludes active producer work, disables
    // further GPU callbacks and calls StopProducer. Free device resources here;
    // the backend object itself remains alive until callbacks have joined.
    virtual void Retire() = 0;
    virtual const char* LastError() const = 0;
};

// One platform implementation defines these two functions. Install is called
// from UnityPluginLoad BEFORE device creation so Vulkan extensions can be
// requested through Unity's interception API. It must not access an uncreated
// or different graphics backend. Configure is called on graphics-device init.
void InstallPortableGpuHooks(IUnityInterfaces* interfaces);
void ConfigurePortableGpuEvents(IUnityInterfaces* interfaces, UnityGfxRenderer renderer, int eventBase);
std::unique_ptr<PortableGpuBackend> CreatePortableGpuBackend(UnityGfxRenderer renderer);
