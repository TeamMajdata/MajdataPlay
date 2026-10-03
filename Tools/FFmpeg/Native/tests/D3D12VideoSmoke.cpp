// Real H.264/D3D12VA decode and native video-processing regression. Pixel
// download occurs only in this test to compare GPU output with a CPU reference.
// Usage: D3D12VideoSmoke.exe path/to/video.mp4
#include <d3d11.h>
#include <d3d12.h>
#include <d3d12sdklayers.h>
#include <dxgi1_4.h>
#include "../D3D12.h"
#include "IUnityGraphicsD3D12.h"
#include <cstdio>
#include <vector>
#include <cmath>
#include <cstring>
extern "C" {
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libavutil/hwcontext.h>
#include <libswscale/swscale.h>
}
static ID3D12Device* dev = nullptr;
static ID3D12CommandQueue* queue = nullptr;
static IUnityGraphicsD3D12v8 api{};
static bool targetPrepared = false;
static bool nativeFormatUnavailable = false;
static ID3D12Device* UNITY_INTERFACE_API Device() { return dev; }
static ID3D12CommandQueue* UNITY_INTERFACE_API Queue() { return queue; }
static void UNITY_INTERFACE_API Configure(int, const UnityD3D12PluginEventConfig*) {}
static void Wait() {
    ID3D12Fence* fence = nullptr;
    dev->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&fence));
    queue->Signal(fence, 1);
    HANDLE event = CreateEvent(nullptr, FALSE, FALSE, nullptr);
    fence->SetEventOnCompletion(1, event);
    WaitForSingleObject(event, 10000);
    CloseHandle(event);
    fence->Release();
}
static void UNITY_INTERFACE_API State(ID3D12Resource* texture, D3D12_RESOURCE_STATES state) {
    if (targetPrepared) return;
    ID3D12CommandAllocator* allocator = nullptr;
    ID3D12GraphicsCommandList* list = nullptr;
    dev->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&allocator));
    dev->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, allocator, nullptr, IID_PPV_ARGS(&list));
    D3D12_RESOURCE_BARRIER barrier{};
    barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barrier.Transition = {texture, D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES, D3D12_RESOURCE_STATE_COMMON, state};
    list->ResourceBarrier(1, &barrier); list->Close();
    ID3D12CommandList* commands[]{list}; queue->ExecuteCommandLists(1, commands); Wait();
    list->Release(); allocator->Release(); targetPrepared = true;
}
static IUnityInterface* UNITY_INTERFACE_API InterfaceSplit(unsigned long long high, unsigned long long low) {
    if (UnityInterfaceGUID(high, low) == GetUnityInterfaceGUID<IUnityGraphicsD3D12v8>()) return &api;
    return nullptr;
}
int FfuEventId(int value) { return value; }
void FfuD3D11RetainPresenter(void*) {}
void FfuD3D11SetError(void*, int) {}
void FfuD3D11Submit(void*) {}
void FFU_CALL ffu_d3d11_release(void*) {}
void FFU_CALL ffu_packet_cancel(void*) {}
void* FFU_CALL ffu_d3d11_prepare(void*, const AVFrame*, void*) { return nullptr; }
static AVPixelFormat Format(AVCodecContext*, const AVPixelFormat* formats) {
    for (; *formats != AV_PIX_FMT_NONE; ++formats) if (*formats == AV_PIX_FMT_D3D12) return *formats;
    nativeFormatUnavailable = true;
    return AV_PIX_FMT_NONE;
}
static bool Check(int result, const char* step) {
    if (result >= 0) return true;
    char message[256]; av_strerror(result, message, sizeof(message));
    std::printf("FAIL %s: %08x %s\n", step, result, message);
    return false;
}
static bool VerifyPixels(ID3D12Resource* texture, const AVFrame* source) {
    // Both downloads below are validation only, never part of bridge playback.
    AVFrame* software = av_frame_alloc();
    if (!Check(av_hwframe_transfer_data(software, source, 0), "reference download")) return false;
    const int width = source->width, height = source->height;
    std::vector<unsigned char> expected(width * height * 4);
    SwsContext* converter = sws_getContext(width, height, static_cast<AVPixelFormat>(software->format),
        width, height, AV_PIX_FMT_RGBA, SWS_BILINEAR, nullptr, nullptr, nullptr);
    const bool matrix709 = source->colorspace == AVCOL_SPC_BT709 ||
        (source->colorspace == AVCOL_SPC_UNSPECIFIED && height >= 720);
    const int* coefficients = sws_getCoefficients(matrix709 ? SWS_CS_ITU709 : SWS_CS_ITU601);
    sws_setColorspaceDetails(converter, coefficients, source->color_range == AVCOL_RANGE_JPEG,
        coefficients, 1, 0, 1 << 16, 1 << 16);
    uint8_t* output[] = {expected.data()}; int stride[] = {width * 4};
    sws_scale(converter, software->data, software->linesize, 0, height, output, stride);
    sws_freeContext(converter); av_frame_free(&software);
    auto desc = texture->GetDesc();
    D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint{}; UINT64 bytes = 0;
    dev->GetCopyableFootprints(&desc, 0, 1, 0, &footprint, nullptr, nullptr, &bytes);
    D3D12_RESOURCE_DESC buffer{}; buffer.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
    buffer.Width = bytes; buffer.Height = buffer.DepthOrArraySize = buffer.MipLevels = buffer.SampleDesc.Count = 1;
    buffer.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
    D3D12_HEAP_PROPERTIES heap{}; heap.Type = D3D12_HEAP_TYPE_READBACK;
    ID3D12Resource* readback = nullptr;
    if (FAILED(dev->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_NONE, &buffer,
        D3D12_RESOURCE_STATE_COPY_DEST, nullptr, IID_PPV_ARGS(&readback)))) return false;
    ID3D12CommandAllocator* allocator = nullptr; ID3D12GraphicsCommandList* list = nullptr;
    dev->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&allocator));
    dev->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, allocator, nullptr, IID_PPV_ARGS(&list));
    D3D12_RESOURCE_BARRIER barrier{}; barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barrier.Transition = {texture, D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES, D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_COPY_SOURCE};
    list->ResourceBarrier(1, &barrier);
    D3D12_TEXTURE_COPY_LOCATION from{}; from.pResource = texture;
    D3D12_TEXTURE_COPY_LOCATION to{}; to.pResource = readback; to.Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT; to.PlacedFootprint = footprint;
    list->CopyTextureRegion(&to, 0, 0, 0, &from, nullptr);
    barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_COPY_SOURCE;
    barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_COPY_DEST;
    list->ResourceBarrier(1, &barrier); list->Close();
    ID3D12CommandList* commands[] = {list}; queue->ExecuteCommandLists(1, commands); Wait();
    unsigned char* pixels = nullptr; D3D12_RANGE range{0, static_cast<SIZE_T>(bytes)};
    if (FAILED(readback->Map(0, &range, reinterpret_cast<void**>(&pixels)))) return false;
    double difference = 0; unsigned samples = 0, badAlpha = 0;
    for (int y = 8; y < height - 8; y += 13)
        for (int x = 8; x < width - 8; x += 13) {
            const auto* actual = pixels + footprint.Offset + y * footprint.Footprint.RowPitch + x * 4;
            const auto* reference = expected.data() + (y * width + x) * 4;
            for (int channel = 0; channel < 3; ++channel) { difference += std::abs(int(actual[channel]) - reference[channel]); ++samples; }
            if (actual[3] < 250) ++badAlpha;
        }
    D3D12_RANGE written{0, 0}; readback->Unmap(0, &written);
    readback->Release(); list->Release(); allocator->Release();
    std::printf("pixel comparison mean error %.3f, alpha failures %u\n", difference / samples, badAlpha);
    return samples > 0 && difference / samples < 12 && badAlpha == 0;
}
static unsigned ValidationMessages() {
    ID3D12InfoQueue* info = nullptr;
    unsigned errors = 0;
    if (SUCCEEDED(dev->QueryInterface(IID_PPV_ARGS(&info)))) {
        for (UINT64 i = 0; i < info->GetNumStoredMessages(); ++i) {
            SIZE_T length = 0; info->GetMessage(i, nullptr, &length);
            std::vector<unsigned char> buffer(length); auto* message = reinterpret_cast<D3D12_MESSAGE*>(buffer.data());
            info->GetMessage(i, message, &length);
            if (message->Severity <= D3D12_MESSAGE_SEVERITY_WARNING) { std::printf("D3D12 %u %s\n", message->Severity, message->pDescription); ++errors; }
        }
        info->Release();
    }
    return errors;
}
static int TestFrames(AVBufferRef* hardware) {
    std::puts("SYNTHETIC GPU PRESENTATION: fixture CPU upload is test-only; no codec decode asserted.");
    AVBufferRef* frames = nullptr;
    ID3D12Resource* target = nullptr;
    void* presenter = ffu_d3d12va_create();
    unsigned pixelChecks = 0;
    for (int count = 0; count < 100; ++count) {
        const int width = count < 50 ? 64 : 128, height = 64;
        if (count == 0 || count == 50) {
            av_buffer_unref(&frames);
            if (target) target->Release();
            frames = av_hwframe_ctx_alloc(hardware);
            auto* context = reinterpret_cast<AVHWFramesContext*>(frames->data);
            context->format = AV_PIX_FMT_D3D12; context->sw_format = AV_PIX_FMT_NV12;
            context->width = width; context->height = height;
            if (!Check(av_hwframe_ctx_init(frames), "synthetic frame pool")) return 1;
            D3D12_RESOURCE_DESC desc{}; desc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
            desc.Width = width; desc.Height = height; desc.DepthOrArraySize = desc.MipLevels = desc.SampleDesc.Count = 1;
            desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM; desc.Flags = D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET;
            D3D12_HEAP_PROPERTIES heap{}; heap.Type = D3D12_HEAP_TYPE_DEFAULT;
            if (FAILED(dev->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_NONE, &desc,
                D3D12_RESOURCE_STATE_COMMON, nullptr, IID_PPV_ARGS(&target)))) return 1;
            targetPrepared = false;
        }
        AVFrame* frame = av_frame_alloc();
        AVFrame* pixels = av_frame_alloc();
        pixels->width = width; pixels->height = height; pixels->format = AV_PIX_FMT_NV12;
        av_frame_get_buffer(pixels, 32);
        const bool fullRange = count % 2 != 0;
        for (int y = 0; y < height; ++y)
            std::memset(pixels->data[0] + y * pixels->linesize[0], y < height / 2 ? (fullRange ? 255 : 235) : (fullRange ? 0 : 16), width);
        for (int y = 0; y < height / 2; ++y) std::memset(pixels->data[1] + y * pixels->linesize[1], 128, width);
        if (!Check(av_hwframe_get_buffer(frames, frame, 0), "synthetic GPU frame") ||
            !Check(av_hwframe_transfer_data(frame, pixels, 0), "test-only fixture upload")) return 1;
        av_frame_free(&pixels);
        frame->colorspace = AVCOL_SPC_BT709; frame->color_range = fullRange ? AVCOL_RANGE_JPEG : AVCOL_RANGE_MPEG;
        void* render = ffu_d3d12va_prepare(presenter, frame, target);
        if (!render) { std::printf("FAIL synthetic prepare %08x\n", ffu_d3d12va_error(presenter)); ValidationMessages(); return 1; }
        if (count == 5) {
            void* canceled = ffu_d3d12va_prepare(presenter, frame, target);
            if (!canceled) return 1;
            ffu_d3d12va_cancel(canceled);
        }
        AVFrame* reference = count % 25 == 0 ? av_frame_clone(frame) : nullptr;
        av_frame_free(&frame); // Packet must retain the original surface.
        FfuD3D12Render(FfuPrepareNativeD3D12, render); FfuD3D12Render(FfuSubmitNativeD3D12, render);
        if (ffu_d3d12va_error(presenter)) { std::printf("FAIL synthetic render %08x\n", ffu_d3d12va_error(presenter)); ValidationMessages(); return 1; }
        if (reference) {
            FfuD3D12Poll(true);
            if (!VerifyPixels(target, reference)) { ValidationMessages(); return 1; }
            av_frame_free(&reference); ++pixelChecks;
        }
        Sleep(1);
    }
    FfuD3D12Poll(true); Wait();
    const unsigned errors = ValidationMessages();
    ffu_d3d12va_release(presenter); av_buffer_unref(&frames); av_buffer_unref(&hardware); target->Release();
    FfuD3D12Shutdown(); queue->Release(); dev->Release();
    const bool passed = !errors && pixelChecks == 4;
    std::printf("%s SYNTHETIC native GPU conversion/range/size change/cancel/lifetime, 100 frames, %u pixel checks, %u validation messages\n", passed ? "PASS" : "FAIL", pixelChecks, errors);
    return passed ? 0 : 1;
}
int main(int argc, char** argv) {
    if (argc < 2) return 2;
    ID3D12Debug* debug = nullptr;
    const bool debugEnabled = SUCCEEDED(D3D12GetDebugInterface(IID_PPV_ARGS(&debug)));
    if (debugEnabled) { debug->EnableDebugLayer(); debug->Release(); }
    std::printf("D3D12 debug layer: %s\n", debugEnabled ? "enabled" : "unavailable");
    HRESULT hr = D3D12CreateDevice(nullptr, D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&dev));
    if (FAILED(hr)) { std::printf("SKIP device %08lx\n", hr); return 77; }
    IDXGIFactory4* factory = nullptr; IDXGIAdapter1* adapter = nullptr;
    if (SUCCEEDED(CreateDXGIFactory1(IID_PPV_ARGS(&factory)))) {
        if (SUCCEEDED(factory->EnumAdapterByLuid(dev->GetAdapterLuid(), IID_PPV_ARGS(&adapter)))) {
            DXGI_ADAPTER_DESC1 description{}; adapter->GetDesc1(&description);
            std::printf("Adapter: %ls\n", description.Description); adapter->Release();
        }
        factory->Release();
    }
    D3D12_COMMAND_QUEUE_DESC q{}; q.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
    if (FAILED(dev->CreateCommandQueue(&q, IID_PPV_ARGS(&queue)))) return 1;
    api.GetDevice = Device; api.GetCommandQueue = Queue; api.ConfigureEvent = Configure; api.RequestResourceState = State;
    IUnityInterfaces interfaces{}; interfaces.GetInterfaceSplit = InterfaceSplit;
    ID3D11Device* legacy = FfuD3D12Initialize(&interfaces); if (legacy) legacy->Release();
    std::printf("native status %08x capabilities %x\n", ffu_d3d12va_status(), FfuD3D12Capabilities());
    auto* hardware = static_cast<AVBufferRef*>(ffu_d3d12va_acquire_device());
    if (!hardware) { std::printf("SKIP avdevice %08x\n", ffu_d3d12va_status()); return 77; }
    if (std::strcmp(argv[1], "--frames") == 0) return TestFrames(hardware);
    AVFormatContext* format = nullptr;
    if (!Check(avformat_open_input(&format, argv[1], nullptr, nullptr), "input")) return 1;
    if (!Check(avformat_find_stream_info(format, nullptr), "streams")) return 1;
    int stream = av_find_best_stream(format, AVMEDIA_TYPE_VIDEO, -1, -1, nullptr, 0);
    if (stream < 0) return 1;
    const AVCodec* codec = avcodec_find_decoder(format->streams[stream]->codecpar->codec_id);
    AVCodecContext* decoder = avcodec_alloc_context3(codec);
    avcodec_parameters_to_context(decoder, format->streams[stream]->codecpar);
    decoder->hw_device_ctx = hardware; decoder->get_format = Format; decoder->thread_count = 1;
    if (!Check(avcodec_open2(decoder, codec, nullptr), "open")) return 1;
    void* presenter = ffu_d3d12va_create();
    void* rejected = ffu_d3d12va_create();
    if (!presenter || !rejected || ffu_d3d12va_prepare(rejected, nullptr, nullptr) ||
        !ffu_d3d12va_error(rejected) || ffu_d3d12va_error(presenter)) return 1;
    ffu_d3d12va_release(rejected);
    AVPacket* packet = av_packet_alloc(); AVFrame* frame = av_frame_alloc();
    ID3D12Resource* target = nullptr;
    int count = 0, width = 0, height = 0, pixelChecks = 0, seeks = 0;
    while (av_read_frame(format, packet) >= 0) {
        if (packet->stream_index == stream) {
            const int sent = avcodec_send_packet(decoder, packet);
            if (sent < 0 && nativeFormatUnavailable) {
                std::puts("SKIP: FFmpeg D3D12VA rejected this adapter/codec combination; synthetic presentation can be tested using --frames.");
                return 77;
            }
            if (!Check(sent, "send")) return 1;
            int result;
            while ((result = avcodec_receive_frame(decoder, frame)) >= 0) {
                if (!target) {
                    width = frame->width; height = frame->height;
                    D3D12_RESOURCE_DESC desc{}; desc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
                    desc.Width = width; desc.Height = height; desc.DepthOrArraySize = desc.MipLevels = desc.SampleDesc.Count = 1;
                    desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM; desc.Flags = D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET;
                    D3D12_HEAP_PROPERTIES heap{}; heap.Type = D3D12_HEAP_TYPE_DEFAULT;
                    hr = dev->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_NONE, &desc, D3D12_RESOURCE_STATE_COMMON, nullptr, IID_PPV_ARGS(&target));
                    if (FAILED(hr)) return 1;
                }
                void* render = ffu_d3d12va_prepare(presenter, frame, target);
                if (!render) { std::printf("FAIL prepare %08x\n", ffu_d3d12va_error(presenter)); return 1; }
                if (count == 5) {
                    void* canceled = ffu_d3d12va_prepare(presenter, frame, target);
                    if (!canceled) return 1;
                    ffu_d3d12va_cancel(canceled);
                }
                AVFrame* reference = count % 25 == 0 ? av_frame_clone(frame) : nullptr;
                av_frame_unref(frame);
                FfuD3D12Render(FfuPrepareNativeD3D12, render); FfuD3D12Render(FfuSubmitNativeD3D12, render);
                if (ffu_d3d12va_error(presenter)) { std::printf("FAIL render %08x\n", ffu_d3d12va_error(presenter)); return 1; }
                if (reference) {
                    FfuD3D12Poll(true);
                    if (!VerifyPixels(target, reference)) return 1;
                    av_frame_free(&reference); ++pixelChecks;
                }
                ++count; Sleep(5);
            }
            if (result != AVERROR(EAGAIN) && result != AVERROR_EOF) { Check(result, "receive"); return 1; }
        }
        av_packet_unref(packet);
        if ((count >= 40 && seeks == 0) || (count >= 70 && seeks == 1)) {
            FfuD3D12Poll(true);
            if (!Check(av_seek_frame(format, stream, 0, AVSEEK_FLAG_BACKWARD), "seek")) return 1;
            avcodec_flush_buffers(decoder); ++seeks;
        }
        if (count >= 100) break;
    }
    FfuD3D12Poll(true); Wait();
    std::printf("frames %d, size %dx%d, device removed %08lx, error %08x\n", count, width, height, dev->GetDeviceRemovedReason(), ffu_d3d12va_error(presenter));
    const unsigned errors = ValidationMessages();
    ffu_d3d12va_release(presenter); av_frame_free(&frame); av_packet_free(&packet);
    avcodec_free_context(&decoder); avformat_close_input(&format); if (target) target->Release();
    FfuD3D12Shutdown(); queue->Release(); dev->Release();
    const bool passed = count >= 100 && pixelChecks >= 4 && seeks == 2 && !errors;
    std::printf("%s native D3D12VA decode/process/pixels/cancel/seek/drain (%u validation messages)\n", passed ? "PASS" : "FAIL", errors);
    return passed ? 0 : 1;
}
