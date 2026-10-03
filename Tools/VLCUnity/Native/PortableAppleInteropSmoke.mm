// Real macOS CGL -> IOSurface -> Metal test, independent of LibVLC/Unity.
// CPU readback is confined to this test to validate the shared pixels.
#include "PortableAppleInterop.h"
#include "Unity/IUnityGraphicsMetal.h"
#import <Foundation/Foundation.h>
#import <Metal/Metal.h>
#import <OpenGL/OpenGL.h>
#import <OpenGL/gl3.h>
#include <chrono>
#include <array>
#include <cstdio>
#include <future>
#include <stdexcept>
#include <thread>

#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"

namespace
{
id<MTLDevice> device;
id<MTLCommandQueue> queue;
id<MTLCommandBuffer> commands;
IUnityGraphicsMetalV2 metal{};

IUnityInterface* UNITY_INTERFACE_API GetInterface(UnityInterfaceGUID guid)
{
    return guid == GetUnityInterfaceGUID<IUnityGraphicsMetalV2>() ? &metal : nullptr;
}
id<MTLDevice> UNITY_INTERFACE_API GetDevice() { return device; }
id<MTLCommandBuffer> UNITY_INTERFACE_API GetCommands() { return commands; }
void Check(bool success, const char* reason) { if (!success) throw std::runtime_error(reason); }

void CheckSharedOpenGl()
{
    const CGLPixelFormatAttribute attributes[] = {
        kCGLPFAAccelerated, kCGLPFAAllowOfflineRenderers,
        kCGLPFAOpenGLProfile, static_cast<CGLPixelFormatAttribute>(kCGLOGLPVersion_3_2_Core),
        static_cast<CGLPixelFormatAttribute>(0)
    };
    CGLPixelFormatObj format = nullptr;
    CGLContextObj unityContext = nullptr;
    GLint count = 0;
    Check(CGLChoosePixelFormat(attributes, &format, &count) == kCGLNoError && count,
        "Cannot choose the test Unity CGL pixel format");
    const auto created = CGLCreateContext(format, nullptr, &unityContext);
    CGLReleasePixelFormat(format);
    Check(created == kCGLNoError && unityContext, "Cannot create the test Unity CGL consumer context");
    const auto previous = CGLGetCurrentContext();
    Check(CGLSetCurrentContext(unityContext) == kCGLNoError, "Cannot activate the CGL consumer");
    auto backend = CreatePortableGpuBackend(kUnityGfxRendererOpenGLCore);
    Check(backend && backend->Initialize(nullptr, kUnityGfxRendererOpenGLCore), "Cannot initialize shared CGL backend");
    Check(backend->Capability() == 3 && backend->Engine() == 1, "Wrong shared CGL capability/decoder engine");
    Check(CGLGetCurrentContext() == unityContext, "Shared CGL initialization changed Unity's current context");
    GLuint firstTexture = 0;
    unsigned width = 16, height = 12;
    for (unsigned frame = 0; frame < 32; ++frame)
    {
        const bool red = (frame & 1) != 0;
        if (frame == 16) { width = 21; height = 17; }
        bool produced = false;
        std::thread producer([&] {
            if (!backend->MakeCurrent(true)) return;
            if (backend->Resize(width, height))
            {
                glClearColor(red ? 1 : 0, red ? 0 : 1, 0, 1);
                glClear(GL_COLOR_BUFFER_BIT);
                produced = backend->FrameComplete();
            }
            produced = backend->MakeCurrent(false) && produced;
        });
        producer.join();
        Check(produced, backend->LastError());
        Check(CGLGetCurrentContext() == unityContext, "Producer moved Unity's CGL context to another thread");
        Check(backend->Pump() && backend->Begin(), backend->LastError());
        const auto info = backend->TextureInfo();
        const auto texture = static_cast<GLuint>(reinterpret_cast<std::uintptr_t>(info.texture));
        Check(info.width == width && info.height == height && glIsTexture(texture), "Shared GL texture is missing or the wrong size");
        if (!frame) firstTexture = texture;
        Check((firstTexture == texture) == (frame < 16) && glIsTexture(firstTexture), "GL resize invalidated a retained texture");
        GLuint framebuffer = 0;
        glGenFramebuffers(1, &framebuffer);
        glBindFramebuffer(GL_FRAMEBUFFER, framebuffer);
        // A rectangle texture would fail this 2D attachment; this also checks
        // the exact texture target required by Unity CreateExternalTexture.
        glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, texture, 0);
        Check(glCheckFramebufferStatus(GL_FRAMEBUFFER) == GL_FRAMEBUFFER_COMPLETE, "Shared texture is not a renderable GL_TEXTURE_2D");
        std::array<unsigned char, 4> pixel{};
        glReadPixels(width - 1, height - 1, 1, 1, GL_RGBA, GL_UNSIGNED_BYTE, pixel.data());
        Check(pixel[0] == (red ? 255 : 0) && pixel[1] == (red ? 0 : 255) && pixel[2] == 0 && pixel[3] == 255,
            "Unity CGL consumer observed stale or invalid shared RGBA pixels");
        Check(backend->End(), backend->LastError());
        glBindFramebuffer(GL_FRAMEBUFFER, 0);
        glDeleteFramebuffers(1, &framebuffer);
    }
    backend->Retire();
    Check(CGLGetCurrentContext() == unityContext, "Shared GL Retire failed to restore Unity's CGL context");
    Check(!glIsTexture(firstTexture), "Shared GL Retire leaked a retained texture");
    backend.reset();
    CGLSetCurrentContext(previous);
    CGLReleaseContext(unityContext);
    std::puts("PASS: 32 shared CGL GL_TEXTURE_2D RGBA frames, producer thread, resize and context restoration");
}

id<MTLBuffer> RecordRead(PortableGpuBackend& backend)
{
    Check(backend.Pump() && backend.Begin(), backend.LastError());
    const auto info = backend.TextureInfo();
    id<MTLTexture> texture = (__bridge id<MTLTexture>)info.texture;
    Check(texture && texture.pixelFormat == MTLPixelFormatBGRA8Unorm, "Wrong Metal texture/format");
    id<MTLBuffer> pixels = [device newBufferWithLength:256 * info.height options:MTLResourceStorageModeShared];
    Check(pixels != nil, "Cannot allocate the test readback buffer");
    commands = [queue commandBuffer];
    id<MTLBlitCommandEncoder> encoder = [commands blitCommandEncoder];
    [encoder copyFromTexture:texture sourceSlice:0 sourceLevel:0 sourceOrigin:MTLOriginMake(0, 0, 0)
                  sourceSize:MTLSizeMake(info.width, info.height, 1) toBuffer:pixels destinationOffset:0
       destinationBytesPerRow:256 destinationBytesPerImage:256 * info.height];
    [encoder endEncoding];
    Check(backend.End(), backend.LastError());
    Check(!backend.IsConsumerComplete(), "Consumer incorrectly completed an unsubmitted command buffer");
    return pixels;
}
void CheckPixels(id<MTLBuffer> buffer, unsigned width, unsigned height, bool red)
{
    const auto* pixels = static_cast<const unsigned char*>(buffer.contents);
    for (unsigned y = 0; y < height; ++y)
        for (unsigned x = 0; x < width; ++x)
        {
            const auto* pixel = pixels + y * 256 + x * 4;
            Check(pixel[0] == 0 && pixel[1] == (red ? 0 : 255) && pixel[2] == (red ? 255 : 0) && pixel[3] == 255,
                "Metal observed stale or incorrectly ordered BGRA pixels");
        }
}
}

int main()
{
    @autoreleasepool
    {
        try
        {
            CheckSharedOpenGl();
            device = MTLCreateSystemDefaultDevice();
            Check(device != nil, "No Metal device");
            queue = [device newCommandQueue];
            metal.MetalDevice = GetDevice;
            metal.CurrentCommandBuffer = GetCommands;
            IUnityInterfaces interfaces{};
            interfaces.GetInterface = GetInterface;
            auto backend = CreateApplePortableGpuBackend();
            const auto previous = CGLGetCurrentContext();
            Check(backend->Initialize(&interfaces, kUnityGfxRendererMetal), backend->LastError());
            Check(CGLGetCurrentContext() == previous, "Initialization changed the consumer CGL context");
            std::printf("Metal device: %s\n", device.name.UTF8String);
            unsigned width = 16, height = 12;
            void* originalTexture = nullptr;
            for (unsigned frame = 0; frame < 32; ++frame)
            {
                const bool red = (frame & 1) != 0;
                if (frame == 16) { width = 21; height = 17; }
                bool produced = false;
                std::thread producer([&] {
                    @autoreleasepool
                    {
                        if (!backend->WaitForConsumer() || !backend->MakeCurrent(true)) return;
                        if (backend->Resize(width, height))
                        {
                            glClearColor(red ? 1 : 0, red ? 0 : 1, 0, 1);
                            glClear(GL_COLOR_BUFFER_BIT);
                            produced = backend->FrameComplete();
                        }
                        produced = backend->MakeCurrent(false) && produced;
                    }
                });
                producer.join();
                Check(produced, backend->LastError());
                const auto info = backend->TextureInfo();
                Check(info.width == width && info.height == height, "Incorrect shared surface dimensions");
                if (!frame) originalTexture = info.texture;
                Check((originalTexture == info.texture) == (frame < 16), "Unexpected surface reuse across resize");
                id<MTLBuffer> pixels = RecordRead(*backend);
                auto waiter = std::async(std::launch::async, [&] { return backend->WaitForConsumer(); });
                [commands commit];
                [commands waitUntilCompleted]; // Legal: this test owns and committed this buffer.
                const bool completed = waiter.wait_for(std::chrono::seconds(5)) == std::future_status::ready;
                if (!completed) backend->StopProducer();
                const bool canWrite = waiter.get();
                Check(completed && canWrite, "Producer did not resume after Metal completed");
                Check(commands.status == MTLCommandBufferStatusCompleted, "Metal test command failed");
                CheckPixels(pixels, width, height, red);
            }

            // Exercise Dispose before Unity commits this frame. Stopping must
            // wake VLC immediately, while completion keeps the IOSurface alive.
            id<MTLBuffer> finalPixels = RecordRead(*backend);
            auto stoppedWaiter = std::async(std::launch::async, [&] { return backend->WaitForConsumer(); });
            backend->StopProducer();
            Check(!stoppedWaiter.get(), "StopProducer did not cancel the outstanding producer wait");
            backend->Retire();
            backend.reset();
            [commands commit];
            [commands waitUntilCompleted];
            Check(commands.status == MTLCommandBufferStatusCompleted, "Retire invalidated an in-flight Metal read");
            CheckPixels(finalPixels, width, height, true);
            commands = nil;
            queue = nil;
            Check(CGLGetCurrentContext() == previous, "Resource destruction changed the consumer CGL context");
            std::puts("PASS: 32 CGL/Metal BGRA frames, resize, async completion, cancellation and deferred retirement");
            return 0;
        }
        catch (const std::exception& error)
        {
            std::fprintf(stderr, "FAIL: %s\n", error.what());
            return 1;
        }
    }
}
#pragma clang diagnostic pop
