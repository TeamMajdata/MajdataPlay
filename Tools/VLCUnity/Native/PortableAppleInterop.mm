// LibVLC GL/GLES output -> shared Apple GL textures or IOSurface -> Unity Metal.
// No CPU pixel lock, readback, or upload is performed.
#include "PortableAppleInterop.h"
#include "Unity/IUnityGraphicsMetal.h"
#import <TargetConditionals.h>
#import <Foundation/Foundation.h>
#import <Metal/Metal.h>
#define GL_SILENCE_DEPRECATION
#define GLES_SILENCE_DEPRECATION
#define COREVIDEO_SILENCE_GL_DEPRECATION
#if TARGET_OS_IPHONE
#import <OpenGLES/EAGL.h>
#import <OpenGLES/ES3/gl.h>
#import <OpenGLES/ES3/glext.h>
#else
#import <OpenGL/OpenGL.h>
#import <OpenGL/gl3.h>
#endif
// Import GL first so CoreVideo uses the chosen core-profile declarations.
#import <CoreVideo/CoreVideo.h>
#if TARGET_OS_IPHONE
#import <CoreVideo/CVOpenGLESTextureCache.h>
#else
#import <CoreVideo/CVOpenGLTextureCache.h>
#endif
#include <atomic>
#include <condition_variable>
#include <cstdio>
#include <dlfcn.h>
#include <mutex>
#include <vector>

#ifndef GL_BGRA
#define GL_BGRA 0x80E1
#endif

// OpenGL is the decoder output API exposed by the pinned LibVLC build. Apple
// still provides its interop APIs, although Metal is preferred for new renderers.
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"

namespace
{
// Unity's GL consumer needs an actual GL_TEXTURE_2D. CoreVideo's macOS GL
// texture cache may return GL_TEXTURE_RECTANGLE, so this path shares ordinary
// RGBA8 textures through a separate CGL/EAGL context instead of using that cache.
class SharedAppleGl final : public PortableGpuBackend
{
    struct GlSurface
    {
        unsigned width = 0, height = 0;
        GLuint texture = 0, framebuffer = 0;
    };
public:
    ~SharedAppleGl() override { Retire(); }

    bool Initialize(IUnityInterfaces*, UnityGfxRenderer renderer) override
    {
        @autoreleasepool
        {
#if TARGET_OS_IPHONE
            EAGLContext* unityContext = [EAGLContext currentContext];
            if (renderer != kUnityGfxRendererOpenGLES30 || !unityContext || unityContext.API != kEAGLRenderingAPIOpenGLES3)
                return Fail("Unity has no current OpenGL ES 3 context to share");
            _context = [[EAGLContext alloc] initWithAPI:kEAGLRenderingAPIOpenGLES3 sharegroup:unityContext.sharegroup];
            if (!_context) return Fail("Cannot create an independent shared EAGL producer context");
#else
            CGLContextObj unityContext = CGLGetCurrentContext();
            if (renderer != kUnityGfxRendererOpenGLCore || !unityContext)
                return Fail("Unity has no current OpenGL Core context to share");
            CGLPixelFormatObj pixelFormat = CGLGetPixelFormat(unityContext);
            if (!pixelFormat || CGLCreateContext(pixelFormat, unityContext, &_context) != kCGLNoError)
                return Fail("Cannot create an independent shared CGL producer context");
            GLint virtualScreen = 0;
            if (CGLGetVirtualScreen(unityContext, &virtualScreen) != kCGLNoError ||
                CGLSetVirtualScreen(_context, virtualScreen) != kCGLNoError)
                return Fail("Cannot select Unity's CGL GPU for the video producer");
#endif
            if (!MakeCurrent(true)) return false;
            const bool ready = Resize(16, 16) && FrameComplete();
            const bool restored = MakeCurrent(false);
            _current = nullptr; // Only expose a real LibVLC output frame.
            return ready && restored;
        }
    }

    int Capability() const override { return 3; }
    int Engine() const override { return TARGET_OS_IPHONE ? 2 : 1; }

    bool MakeCurrent(bool enter) override
    {
        if (!_context) return Fail("The shared Apple GL producer context is unavailable");
#if TARGET_OS_IPHONE
        if (enter) _previous = [EAGLContext currentContext];
        if (![EAGLContext setCurrentContext:(enter ? _context : _previous)])
            return Fail("Cannot bind/restore the shared EAGL producer context");
        if (!enter) _previous = nil;
#else
        if (enter) _previous = CGLGetCurrentContext();
        if (CGLSetCurrentContext(enter ? _context : _previous) != kCGLNoError)
            return Fail("Cannot bind/restore the shared CGL producer context");
        if (!enter) _previous = nullptr;
#endif
        if (enter && _current) glBindFramebuffer(GL_FRAMEBUFFER, _current->framebuffer);
        return true;
    }

    bool Resize(unsigned width, unsigned height) override
    {
        GLint maximum = 0;
        glGetIntegerv(GL_MAX_TEXTURE_SIZE, &maximum);
        if (!width || !height || maximum <= 0 || width > static_cast<unsigned>(maximum) || height > static_cast<unsigned>(maximum))
            return Fail("Video dimensions exceed the Apple GL texture limit");
        for (auto& surface : _surfaces)
            if (surface->width == width && surface->height == height)
            { _current = surface.get(); glBindFramebuffer(GL_FRAMEBUFFER, _current->framebuffer); return true; }
        if (_surfaces.size() >= 9) return Fail("Too many retained video surface sizes; select CPU output");
        auto surface = std::make_unique<GlSurface>();
        surface->width = width; surface->height = height;
        glGenTextures(1, &surface->texture);
        glBindTexture(GL_TEXTURE_2D, surface->texture);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
        glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, width, height, 0, GL_RGBA, GL_UNSIGNED_BYTE, nullptr);
        glGenFramebuffers(1, &surface->framebuffer);
        glBindFramebuffer(GL_FRAMEBUFFER, surface->framebuffer);
        glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, surface->texture, 0);
        const bool ready = glCheckFramebufferStatus(GL_FRAMEBUFFER) == GL_FRAMEBUFFER_COMPLETE && glGetError() == GL_NO_ERROR;
        if (!ready)
        {
            glDeleteFramebuffers(1, &surface->framebuffer);
            glDeleteTextures(1, &surface->texture);
            return Fail("Cannot allocate a shared Apple GL RGBA8 video framebuffer");
        }
        glClearColor(0, 0, 0, 1);
        glClear(GL_COLOR_BUFFER_BIT);
        _current = surface.get();
        _surfaces.push_back(std::move(surface));
        return true;
    }

    void* GetProcAddress(const char* name) override { return dlsym(RTLD_DEFAULT, name); }
    bool FrameComplete() override
    {
        glFinish(); // Complete producer writes before releasing the bridge mutex.
        return glGetError() == GL_NO_ERROR || Fail("Apple GL producer rendering failed");
    }
    bool Pump() override { return IsSharedConsumer(); }
    PortableTextureInfo TextureInfo() const override
    {
        return _current ? PortableTextureInfo{reinterpret_cast<void*>(static_cast<std::uintptr_t>(_current->texture)), _current->width, _current->height} : PortableTextureInfo{};
    }
    bool Begin() override { return IsSharedConsumer() || Fail("Unity's current GL context no longer shares the video textures"); }
    bool End() override
    {
        if (!IsSharedConsumer()) return Fail("The Apple GL video consumer context was lost");
        glFinish(); // Complete Unity's reads before allowing producer overwrite.
        return glGetError() == GL_NO_ERROR || Fail("Apple GL consumer rendering failed");
    }
    void Retire() override
    {
        if (!_context) return;
        // MakeCurrent saves Unity's context, including its sharegroup. FBOs are
        // private to the producer context, so delete them only with it current.
        if (MakeCurrent(true))
        {
            for (auto& surface : _surfaces)
            {
                glDeleteFramebuffers(1, &surface->framebuffer);
                glDeleteTextures(1, &surface->texture);
            }
            glFinish();
            MakeCurrent(false);
        }
        // If the GL context is lost, destroying it frees its private FBO names;
        // inaccessible shared texture names live until the sharegroup is gone.
        _surfaces.clear(); _current = nullptr;
#if TARGET_OS_IPHONE
        if ([EAGLContext currentContext] == _context) [EAGLContext setCurrentContext:nil];
        _context = nil;
#else
        if (CGLGetCurrentContext() == _context) CGLSetCurrentContext(nullptr);
        CGLReleaseContext(_context);
        _context = nullptr;
#endif
    }
    const char* LastError() const override { return _error.load(); }

private:
    bool IsSharedConsumer() const
    {
#if TARGET_OS_IPHONE
        EAGLContext* consumer = [EAGLContext currentContext];
        return _context && consumer && consumer != _context && consumer.sharegroup == _context.sharegroup;
#else
        CGLContextObj consumer = CGLGetCurrentContext();
        if (!_context || !consumer || consumer == _context || CGLGetShareGroup(consumer) != CGLGetShareGroup(_context)) return false;
        GLint producerScreen = 0, consumerScreen = 0;
        return CGLGetVirtualScreen(_context, &producerScreen) == kCGLNoError &&
            CGLGetVirtualScreen(consumer, &consumerScreen) == kCGLNoError && producerScreen == consumerScreen;
#endif
    }
    bool Fail(const char* reason) { _error = reason; return false; }
#if TARGET_OS_IPHONE
    EAGLContext* _context = nil;
    EAGLContext* _previous = nil;
#else
    CGLContextObj _context = nullptr;
    CGLContextObj _previous = nullptr;
#endif
    std::vector<std::unique_ptr<GlSurface>> _surfaces;
    GlSurface* _current = nullptr;
    std::atomic<const char*> _error{""};
};

struct Surface
{
    unsigned width = 0, height = 0;
    CVPixelBufferRef pixels = nullptr;
    CVMetalTextureRef metal = nullptr;
#if TARGET_OS_IPHONE
    CVOpenGLESTextureRef glTexture = nullptr;
#else
    CVOpenGLTextureRef glTexture = nullptr;
#endif
    GLuint framebuffer = 0;

    ~Surface()
    {
        // Resources clears surfaces with the dedicated producer context current.
        if (framebuffer) glDeleteFramebuffers(1, &framebuffer);
        if (metal) CFRelease(metal);
        if (glTexture) CFRelease(glTexture);
        if (pixels) CFRelease(pixels);
    }
};

struct Resources
{
#if TARGET_OS_IPHONE
    EAGLContext* context = nil;
    EAGLContext* previousContext = nil;
    CVOpenGLESTextureCacheRef glCache = nullptr;
#else
    CGLContextObj context = nullptr;
    CGLContextObj previousContext = nullptr;
    CGLPixelFormatObj pixelFormat = nullptr;
    CVOpenGLTextureCacheRef glCache = nullptr;
#endif
    CVMetalTextureCacheRef metalCache = nullptr;
    id<MTLDevice> device = nil;
    std::vector<std::unique_ptr<Surface>> surfaces;

    ~Resources()
    {
        // A Metal completion handler may be the final owner after Retire. The
        // LibVLC has joined, or device shutdown has excluded active producer
        // work and disabled further callbacks under the bridge mutex. This
        // context is no longer bound on another thread. Preserve prior context.
        @autoreleasepool
        {
#if TARGET_OS_IPHONE
            EAGLContext* previous = [EAGLContext currentContext];
            if (previous == context) previous = nil;
            if (context) [EAGLContext setCurrentContext:context];
#else
            CGLContextObj previous = CGLGetCurrentContext();
            if (previous == context) previous = nullptr;
            if (context) CGLSetCurrentContext(context);
#endif
            surfaces.clear();
            if (glCache)
            {
#if TARGET_OS_IPHONE
                CVOpenGLESTextureCacheFlush(glCache, 0);
#else
                CVOpenGLTextureCacheFlush(glCache, 0);
#endif
                CFRelease(glCache);
            }
            if (metalCache) { CVMetalTextureCacheFlush(metalCache, 0); CFRelease(metalCache); }
#if TARGET_OS_IPHONE
            if (context) [EAGLContext setCurrentContext:previous];
            context = nil;
#else
            if (context) { CGLSetCurrentContext(previous); CGLReleaseContext(context); }
            if (pixelFormat) CGLReleasePixelFormat(pixelFormat);
#endif
        }
    }
};

struct Completion
{
    mutable std::mutex mutex;
    std::condition_variable changed;
    unsigned pending = 0;
    bool stopped = false;
    bool failed = false;
};

class AppleInterop final : public PortableGpuBackend
{
public:
    ~AppleInterop() override { Retire(); }

    bool Initialize(IUnityInterfaces* interfaces, UnityGfxRenderer renderer) override
    {
        if (renderer != kUnityGfxRendererMetal || !interfaces)
            return Fail("Apple IOSurface interop requires Unity's Metal renderer");
        @autoreleasepool
        {
            id<MTLDevice> device = nil;
            if (auto* api = interfaces->Get<IUnityGraphicsMetalV2>())
            { device = api->MetalDevice(); _commandBuffer = api->CurrentCommandBuffer; }
            else if (auto* api = interfaces->Get<IUnityGraphicsMetalV1>())
            { device = api->MetalDevice(); _commandBuffer = api->CurrentCommandBuffer; }
            else if (auto* api = interfaces->Get<IUnityGraphicsMetal>())
            { device = api->MetalDevice(); _commandBuffer = api->CurrentCommandBuffer; }
            if (!device || !_commandBuffer) return Fail("Unity Metal device/command buffer interface is unavailable");
            _resources = std::make_shared<Resources>();
            _resources->device = device;
#if TARGET_OS_IPHONE
            _resources->context = [[EAGLContext alloc] initWithAPI:kEAGLRenderingAPIOpenGLES2];
            if (!_resources->context) return Fail("An independent OpenGL ES producer context could not be created");
#else
            const CGLPixelFormatAttribute attributes[] = {
                kCGLPFAAccelerated, kCGLPFAAllowOfflineRenderers,
                kCGLPFAOpenGLProfile, static_cast<CGLPixelFormatAttribute>(kCGLOGLPVersion_3_2_Core),
                static_cast<CGLPixelFormatAttribute>(0)
            };
            GLint formats = 0;
            if (CGLChoosePixelFormat(attributes, &_resources->pixelFormat, &formats) != kCGLNoError || !formats ||
                CGLCreateContext(_resources->pixelFormat, nullptr, &_resources->context) != kCGLNoError)
                return Fail("An independent accelerated CGL producer context could not be created");
#endif
            if (!MakeCurrent(true)) return false;
#if TARGET_OS_IPHONE
            CVReturn result = CVOpenGLESTextureCacheCreate(kCFAllocatorDefault, nullptr, _resources->context, nullptr, &_resources->glCache);
#else
            CVReturn result = CVOpenGLTextureCacheCreate(kCFAllocatorDefault, nullptr, _resources->context, _resources->pixelFormat, nullptr, &_resources->glCache);
#endif
            bool ready = result == kCVReturnSuccess &&
                CVMetalTextureCacheCreate(kCFAllocatorDefault, nullptr, device, nullptr, &_resources->metalCache) == kCVReturnSuccess;
            if (!ready) Fail("CoreVideo GL/Metal texture-cache creation failed");
            // Probe the actual cross-API surface/FBO before enabling GPU output.
            if (ready) ready = Resize(16, 16) && FrameComplete();
            bool restored = MakeCurrent(false);
            return ready && restored;
        }
    }

    int Capability() const override { return 6; }
    int Engine() const override { return TARGET_OS_IPHONE ? 2 : 1; }
    int OutputFormat() const override { return GL_RGBA; }

    bool WaitForConsumer() override
    {
        std::unique_lock<std::mutex> lock(_completion->mutex);
        _completion->changed.wait(lock, [this] { return !_completion->pending || _completion->stopped || _completion->failed; });
        return !_completion->stopped && !_completion->failed;
    }
    bool IsConsumerComplete() const override
    {
        std::lock_guard<std::mutex> lock(_completion->mutex);
        return !_completion->pending && !_completion->stopped && !_completion->failed;
    }
    void StopProducer() override
    {
        std::lock_guard<std::mutex> lock(_completion->mutex);
        _completion->stopped = true;
        _completion->changed.notify_all();
    }

    bool MakeCurrent(bool enter) override
    {
        if (!_resources || !_resources->context) return Fail("The Apple video producer context is unavailable");
#if TARGET_OS_IPHONE
        if (enter) _resources->previousContext = [EAGLContext currentContext];
        if (![EAGLContext setCurrentContext:(enter ? _resources->context : _resources->previousContext)])
            return Fail("Could not bind/restore the OpenGL ES video context");
        if (!enter) _resources->previousContext = nil;
#else
        if (enter) _resources->previousContext = CGLGetCurrentContext();
        if (CGLSetCurrentContext(enter ? _resources->context : _resources->previousContext) != kCGLNoError)
            return Fail("Could not bind/restore the CGL video context");
        if (!enter) _resources->previousContext = nullptr;
#endif
        if (enter && _current) glBindFramebuffer(GL_FRAMEBUFFER, _current->framebuffer);
        return true;
    }

    bool Resize(unsigned width, unsigned height) override
    {
        if (!_resources || !width || !height) return Fail("Invalid Apple video surface size");
        for (auto& surface : _resources->surfaces)
            if (surface->width == width && surface->height == height)
            { _current = surface.get(); glBindFramebuffer(GL_FRAMEBUFFER, _current->framebuffer); return true; }
        // Unity can still reference previous resolutions in its queued commands.
        // Retain them until player retirement, but cap hostile adaptive streams.
        if (_resources->surfaces.size() >= 9) return Fail("Too many retained video surface sizes; select CPU output");
        @autoreleasepool
        {
            auto surface = std::make_unique<Surface>();
            surface->width = width; surface->height = height;
            NSMutableDictionary* attributes = [@{
                (__bridge NSString*)kCVPixelBufferIOSurfacePropertiesKey: @{},
                (__bridge NSString*)kCVPixelBufferMetalCompatibilityKey: @YES,
                (__bridge NSString*)kCVPixelBufferBytesPerRowAlignmentKey: @64
            } mutableCopy];
#if TARGET_OS_IPHONE
            attributes[(__bridge NSString*)kCVPixelBufferOpenGLESCompatibilityKey] = @YES;
            attributes[(__bridge NSString*)kCVPixelBufferIOSurfaceOpenGLESFBOCompatibilityKey] = @YES;
            attributes[(__bridge NSString*)kCVPixelBufferIOSurfaceOpenGLESTextureCompatibilityKey] = @YES;
#else
            attributes[(__bridge NSString*)kCVPixelBufferOpenGLCompatibilityKey] = @YES;
#endif
            if (CVPixelBufferCreate(kCFAllocatorDefault, width, height, kCVPixelFormatType_32BGRA,
                (__bridge CFDictionaryRef)attributes, &surface->pixels) != kCVReturnSuccess)
                return Fail("CoreVideo could not allocate a GL/Metal-compatible IOSurface");
            GLenum target;
            GLuint texture;
#if TARGET_OS_IPHONE
            if (CVOpenGLESTextureCacheCreateTextureFromImage(kCFAllocatorDefault, _resources->glCache,
                surface->pixels, nullptr, GL_TEXTURE_2D, GL_RGBA, width, height, GL_BGRA,
                GL_UNSIGNED_BYTE, 0, &surface->glTexture) != kCVReturnSuccess)
                return Fail("CoreVideo could not import the IOSurface into OpenGL ES");
            target = CVOpenGLESTextureGetTarget(surface->glTexture);
            texture = CVOpenGLESTextureGetName(surface->glTexture);
#else
            if (CVOpenGLTextureCacheCreateTextureFromImage(kCFAllocatorDefault, _resources->glCache,
                surface->pixels, nullptr, &surface->glTexture) != kCVReturnSuccess)
                return Fail("CoreVideo could not import the IOSurface into CGL");
            target = CVOpenGLTextureGetTarget(surface->glTexture);
            texture = CVOpenGLTextureGetName(surface->glTexture);
#endif
            glBindTexture(target, texture);
            glTexParameteri(target, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
            glTexParameteri(target, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
            glTexParameteri(target, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
            glTexParameteri(target, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
            glGenFramebuffers(1, &surface->framebuffer);
            glBindFramebuffer(GL_FRAMEBUFFER, surface->framebuffer);
            glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, target, texture, 0);
            if (glCheckFramebufferStatus(GL_FRAMEBUFFER) != GL_FRAMEBUFFER_COMPLETE)
                return Fail("The IOSurface is not a renderable GL video target");
            if (CVMetalTextureCacheCreateTextureFromImage(kCFAllocatorDefault, _resources->metalCache,
                surface->pixels, nullptr, MTLPixelFormatBGRA8Unorm, width, height, 0, &surface->metal) != kCVReturnSuccess ||
                !CVMetalTextureGetTexture(surface->metal))
                return Fail("CoreVideo could not share the GL IOSurface with Unity's Metal device");
            glClearColor(0, 0, 0, 1);
            glClear(GL_COLOR_BUFFER_BIT);
            _current = surface.get();
            _resources->surfaces.push_back(std::move(surface));
            return true;
        }
    }

    void* GetProcAddress(const char* name) override { return dlsym(RTLD_DEFAULT, name); }
    bool FrameComplete() override
    {
        glFinish(); // Producer completion before the Metal consumer can sample.
        return glGetError() == GL_NO_ERROR || Fail("OpenGL video rendering failed");
    }
    bool Pump() override { return _current && !Failed(); }
    PortableTextureInfo TextureInfo() const override
    {
        return _current ? PortableTextureInfo{(__bridge void*)CVMetalTextureGetTexture(_current->metal), _current->width, _current->height} : PortableTextureInfo{};
    }
    bool Begin() override
    {
        _consumerActive = _resources != nullptr;
        return _consumerActive && !Failed();
    }
    bool End() override
    {
        _consumerActive = false;
        if (!_resources || !_commandBuffer) return Fail("Metal video consumer is unavailable");
        id<MTLCommandBuffer> commands = _commandBuffer();
        if (!commands)
        {
            // A queued Blit may already reference these textures. With no
            // command buffer there is no safe completion boundary, even during
            // Retire. Quarantine this failed backend's resources rather than
            // freeing memory the GPU could still read; CPU fallback stops it.
            Quarantine("Metal lost its command buffer");
            return Fail("Unity has no Metal command buffer for video synchronization");
        }
        if (commands.status >= MTLCommandBufferStatusCommitted)
        {
            // Adding a handler after commit is illegal. An already submitted
            // buffer can be waited here without depending on a future Unity
            // submit; never take this path for a NotEnqueued/Enqueued buffer.
            [commands waitUntilCompleted];
            return commands.status == MTLCommandBufferStatusCompleted || Fail("The submitted Metal video command buffer failed");
        }
        const auto completion = _completion;
        const auto keepAlive = _resources;
        {
            std::lock_guard<std::mutex> lock(completion->mutex);
            ++completion->pending;
        }
        [commands addCompletedHandler:^(id<MTLCommandBuffer> completed) {
            (void)keepAlive; // Retain every possibly referenced surface and context.
            std::lock_guard<std::mutex> lock(completion->mutex);
            completion->failed = completion->failed || completed.status == MTLCommandBufferStatusError;
            --completion->pending;
            completion->changed.notify_all();
        }];
        // Never wait or commit Unity's current (not yet submitted) buffer here.
        // The next decoder callback waits outside the bridge mutex instead.
        return true;
    }
    void Retire() override
    {
        StopProducer();
        // Device shutdown can interrupt a render-event transaction. If its
        // matching End never ran, no completion handler owns the pending Blit.
        // Do not query Unity's Metal interface during late destruction.
        if (_consumerActive) Quarantine("Metal device shut down before video End");
        _consumerActive = false;
        _current = nullptr;
        _resources.reset(); // In-flight completion handlers retain GPU resources.
    }
    const char* LastError() const override { return Failed() ? "Metal video command buffer failed" : _error.load(); }

private:
    void Quarantine(const char* reason)
    {
        if (_quarantined || !_resources) return;
        (void)new std::shared_ptr<Resources>(_resources);
        _quarantined = true;
        std::fprintf(stderr, "[VLCUnity] %s; retaining the failed GPU surfaces until process exit.\n", reason);
    }
    bool Fail(const char* message) { _error = message; return false; }
    bool Failed() const { std::lock_guard<std::mutex> lock(_completion->mutex); return _completion->failed; }
    std::shared_ptr<Resources> _resources;
    std::shared_ptr<Completion> _completion = std::make_shared<Completion>();
    Surface* _current = nullptr;
    id<MTLCommandBuffer> (UNITY_INTERFACE_API* _commandBuffer)() = nullptr;
    std::atomic<const char*> _error{""};
    bool _quarantined = false;
    bool _consumerActive = false; // Accessed only under the bridge mutex.
};
}

std::unique_ptr<PortableGpuBackend> CreateApplePortableGpuBackend() { return std::make_unique<AppleInterop>(); }
void InstallPortableGpuHooks(IUnityInterfaces*) {}
void ConfigurePortableGpuEvents(IUnityInterfaces*, UnityGfxRenderer, int) {}
std::unique_ptr<PortableGpuBackend> CreatePortableGpuBackend(UnityGfxRenderer renderer)
{
    if (renderer == kUnityGfxRendererMetal) return CreateApplePortableGpuBackend();
#if TARGET_OS_IPHONE
    if (renderer == kUnityGfxRendererOpenGLES30) return std::make_unique<SharedAppleGl>();
#else
    if (renderer == kUnityGfxRendererOpenGLCore) return std::make_unique<SharedAppleGl>();
#endif
    return nullptr;
}
#pragma clang diagnostic pop
