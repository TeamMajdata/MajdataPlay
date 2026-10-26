#import <Metal/Metal.h>
#import <CoreVideo/CoreVideo.h>
#include "Bridge.h"
#include "IUnityGraphicsMetal.h"
#include <mutex>
#include <vector>
#include <new>
#include <thread>
#include <chrono>

static std::mutex cacheMutex;
static IUnityGraphicsMetalV2* unityMetal = nullptr;
static CVMetalTextureCacheRef textureCache = nullptr;
static std::atomic<int> inFlight{0};
struct Packet {
    bool counted = false;
    AVFrame* frame = nullptr;
    CVMetalTextureRef luma = nullptr;
    CVMetalTextureRef chroma = nullptr;
    ~Packet() {
        if (luma) CFRelease(luma);
        if (chroma) CFRelease(chroma);
        av_frame_free(&frame);
        if (counted) --inFlight;
    }
};
struct Submitted { id<MTLCommandBuffer> buffer; Packet* packet; };
// Render-thread list. Buffer status is an actual GPU completion check. This
// avoids callbacks into a dylib after Unity unloads it during a domain reload.
static std::vector<Submitted> pending;
static void Retire(bool wait) {
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
    for (auto it = pending.begin(); it != pending.end();) {
        while (wait && it->buffer &&
               (it->buffer.status == MTLCommandBufferStatusCommitted || it->buffer.status == MTLCommandBufferStatusScheduled) &&
               std::chrono::steady_clock::now() < deadline)
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
        if (it->buffer && (it->buffer.status == MTLCommandBufferStatusCompleted || it->buffer.status == MTLCommandBufferStatusError)) {
            delete it->packet;
            it = pending.erase(it);
        } else ++it;
    }
}
void FfuPlatformInitialize(IUnityInterfaces* interfaces, UnityGfxRenderer renderer) {
    std::lock_guard<std::mutex> guard(cacheMutex);
    if (textureCache) { CFRelease(textureCache); textureCache = nullptr; }
    unityMetal = nullptr;
    if (renderer != kUnityGfxRendererMetal) return;
    auto* metal = FfuGetInterface<IUnityGraphicsMetalV2>(interfaces);
    // V2 supplies Unity's commit API, needed to drain the final frame safely.
    if (metal && metal->MetalDevice() && CVMetalTextureCacheCreate(kCFAllocatorDefault, nullptr,
            metal->MetalDevice(), nullptr, &textureCache) == kCVReturnSuccess)
        unityMetal = metal;
}
void FfuPlatformShutdown() {
    Retire(true);
    std::lock_guard<std::mutex> guard(cacheMutex);
    unityMetal = nullptr;
    if (textureCache) { CFRelease(textureCache); textureCache = nullptr; }
    // Any uncommitted command retains its frame rather than risking reuse of
    // memory still referenced by Unity. Normal Dispose queues FfuDrain first.
}
int FfuPlatformCapabilities() {
    std::lock_guard<std::mutex> guard(cacheMutex);
    return textureCache && unityMetal ? FfuMetalPlaneZeroCopy : 0;
}
int FfuPlatformStatus() { return FfuPlatformCapabilities() ? 0 : 7; }
FFU_EXPORT int FFU_CALL ffu_metal_prepare(const AVFrame* frame, FfuMetalPlanes* result) {
    if (!result) return 0;
    *result = {};
    if (!frame || frame->format != AV_PIX_FMT_VIDEOTOOLBOX || !frame->data[3]) return 0;
    if (frame->crop_left || frame->crop_top) return 0;
    if (frame->colorspace == AVCOL_SPC_BT2020_NCL || frame->colorspace == AVCOL_SPC_BT2020_CL) return 0;
    auto pixelBuffer = reinterpret_cast<CVPixelBufferRef>(frame->data[3]);
    OSType format = CVPixelBufferGetPixelFormatType(pixelBuffer);
    if (format != kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange && format != kCVPixelFormatType_420YpCbCr8BiPlanarFullRange) return 0;
    if (CVPixelBufferGetPlaneCount(pixelBuffer) != 2 ||
        CVPixelBufferGetWidthOfPlane(pixelBuffer, 0) != static_cast<size_t>(frame->width) ||
        CVPixelBufferGetHeightOfPlane(pixelBuffer, 0) != static_cast<size_t>(frame->height)) return 0;
    if (inFlight.fetch_add(1) >= 24) { --inFlight; return 0; }
    auto* packet = new (std::nothrow) Packet();
    if (!packet) { --inFlight; return 0; }
    packet->counted = true;
    packet->frame = av_frame_clone(frame);
    if (!packet->frame) { delete packet; return 0; }
    std::lock_guard<std::mutex> guard(cacheMutex);
    if (!textureCache || !unityMetal) { delete packet; return 0; }
    size_t cw = CVPixelBufferGetWidthOfPlane(pixelBuffer, 1), ch = CVPixelBufferGetHeightOfPlane(pixelBuffer, 1);
    CVReturn status = CVMetalTextureCacheCreateTextureFromImage(kCFAllocatorDefault, textureCache, pixelBuffer, nullptr,
        MTLPixelFormatR8Unorm, frame->width, frame->height, 0, &packet->luma);
    if (status == kCVReturnSuccess)
        status = CVMetalTextureCacheCreateTextureFromImage(kCFAllocatorDefault, textureCache, pixelBuffer, nullptr,
            MTLPixelFormatRG8Unorm, cw, ch, 1, &packet->chroma);
    if (status != kCVReturnSuccess || !CVMetalTextureGetTexture(packet->luma) || !CVMetalTextureGetTexture(packet->chroma)) {
        delete packet;
        return 0;
    }
    result->packet = packet;
    result->luma = (__bridge void*)CVMetalTextureGetTexture(packet->luma);
    result->chroma = (__bridge void*)CVMetalTextureGetTexture(packet->chroma);
    result->width = frame->width;
    result->height = frame->height;
    result->chromaWidth = static_cast<int>(cw);
    result->chromaHeight = static_cast<int>(ch);
    result->fullRange = format == kCVPixelFormatType_420YpCbCr8BiPlanarFullRange;
    result->matrix709 = frame->colorspace == AVCOL_SPC_BT709 ||
        (frame->colorspace == AVCOL_SPC_UNSPECIFIED && frame->height >= 720);
    return 1;
}
void FfuPlatformRender(int event, void* data) {
    Retire(false);
    if (event == FfuCompleteMetal && data) {
        id<MTLCommandBuffer> command = unityMetal ? unityMetal->CurrentCommandBuffer() : nil;
        pending.push_back({command, static_cast<Packet*>(data)});
    } else if (event == FfuDrain && unityMetal) {
        unityMetal->EndCurrentCommandEncoder();
        unityMetal->CommitCurrentCommandBuffer();
        Retire(true);
    }
}
FFU_EXPORT void FFU_CALL ffu_packet_cancel(void* value) { delete static_cast<Packet*>(value); }
FFU_EXPORT void* FFU_CALL ffu_d3d11_acquire_device() { return nullptr; }
FFU_EXPORT void* FFU_CALL ffu_d3d11_create() { return nullptr; }
FFU_EXPORT void* FFU_CALL ffu_d3d11_create_output(void*, int, int) { return nullptr; }
FFU_EXPORT void FFU_CALL ffu_d3d11_release_output(void*) {}
FFU_EXPORT void FFU_CALL ffu_d3d11_release(void*) {}
FFU_EXPORT void* FFU_CALL ffu_d3d11_prepare(void*, const AVFrame*, void*) { return nullptr; }
FFU_EXPORT int FFU_CALL ffu_d3d11_error(void*) { return -1; }
