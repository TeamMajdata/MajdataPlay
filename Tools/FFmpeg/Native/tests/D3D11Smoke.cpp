// Headless native integration test: real adapter, NV12 -> RGBA, array slice,
// color range, and AVFrame lifetime until GPU completion. No Unity license needed.
#include <d3d11.h>
#include "../Bridge.h"
#include "IUnityGraphicsD3D11.h"
#include <cstdio>
#include <chrono>
#include <vector>
extern "C" {
#include <libavutil/buffer.h>
}

static ID3D11Device* device = nullptr;
static int released = 0;
static UnityGfxRenderer UNITY_INTERFACE_API Renderer() { return kUnityGfxRendererD3D11; }
static ID3D11Device* UNITY_INTERFACE_API Device() { return device; }
static void UNITY_INTERFACE_API Register(IUnityGraphicsDeviceEventCallback) {}
static void UNITY_INTERFACE_API Unregister(IUnityGraphicsDeviceEventCallback) {}
static int UNITY_INTERFACE_API Reserve(int) { return 100; }
static IUnityGraphics graphics{};
static IUnityGraphicsD3D11 d3d{};
static IUnityInterface* UNITY_INTERFACE_API Interface(UnityInterfaceGUID id) {
    if (id == GetUnityInterfaceGUID<IUnityGraphics>()) return &graphics;
    if (id == GetUnityInterfaceGUID<IUnityGraphicsD3D11>()) return &d3d;
    return nullptr;
}
static IUnityInterface* UNITY_INTERFACE_API InterfaceSplit(unsigned long long high, unsigned long long low) {
    return Interface(UnityInterfaceGUID(high, low));
}
static void FreeFrame(void*, uint8_t* value) {
    reinterpret_cast<ID3D11Texture2D*>(value)->Release();
    ++released;
}
static AVFrame* CreateFrame(int width, int height, uint8_t luma, AVColorRange range) {
    std::vector<uint8_t> pixels(width * height * 3 / 2, 128);
    for (int i = 0; i < width * height; ++i) pixels[i] = luma;
    D3D11_SUBRESOURCE_DATA initial{};
    initial.pSysMem = pixels.data(); initial.SysMemPitch = width;
    D3D11_TEXTURE2D_DESC desc{};
    desc.Width = width; desc.Height = height; desc.ArraySize = desc.MipLevels = 1;
    desc.Format = DXGI_FORMAT_NV12; desc.SampleDesc.Count = 1; desc.BindFlags = D3D11_BIND_DECODER;
    ID3D11Texture2D* texture = nullptr;
    if (FAILED(device->CreateTexture2D(&desc, &initial, &texture))) return nullptr;
    AVFrame* frame = av_frame_alloc();
    if (!frame) { texture->Release(); return nullptr; }
    frame->width = width; frame->height = height; frame->format = AV_PIX_FMT_D3D11;
    frame->colorspace = AVCOL_SPC_BT709; frame->color_range = range;
    frame->data[0] = reinterpret_cast<uint8_t*>(texture);
    frame->buf[0] = av_buffer_create(reinterpret_cast<uint8_t*>(texture), 1, FreeFrame, nullptr, 0);
    if (!frame->buf[0]) { texture->Release(); av_frame_free(&frame); }
    return frame;
}
static int ReadPixel(ID3D11DeviceContext* context, ID3D11Texture2D* output) {
    D3D11_TEXTURE2D_DESC desc{};
    output->GetDesc(&desc);
    desc.Usage = D3D11_USAGE_STAGING; desc.BindFlags = 0; desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    ID3D11Texture2D* staging = nullptr;
    if (FAILED(device->CreateTexture2D(&desc, nullptr, &staging))) return -1;
    context->CopyResource(staging, output);
    D3D11_MAPPED_SUBRESOURCE mapped{};
    const HRESULT result = context->Map(staging, 0, D3D11_MAP_READ, 0, &mapped);
    const int pixel = SUCCEEDED(result) ? static_cast<uint8_t*>(mapped.pData)[0] : -1;
    if (SUCCEEDED(result)) context->Unmap(staging, 0);
    staging->Release();
    return pixel;
}
int main() {
    ID3D11DeviceContext* context = nullptr;
    D3D_FEATURE_LEVEL level{};
    HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
        D3D11_CREATE_DEVICE_VIDEO_SUPPORT, nullptr, 0, D3D11_SDK_VERSION, &device, &level, &context);
    if (FAILED(hr)) { std::printf("SKIP: no D3D11 video device (%08lx)\n", static_cast<unsigned long>(hr)); return 77; }
    graphics.GetRenderer = Renderer;
    graphics.RegisterDeviceEventCallback = Register;
    graphics.UnregisterDeviceEventCallback = Unregister;
    graphics.ReserveEventIDRange = Reserve;
    d3d.GetDevice = Device;
    IUnityInterfaces interfaces{};
    interfaces.GetInterface = Interface;
    interfaces.GetInterfaceSplit = InterfaceSplit;
    UnityPluginLoad(&interfaces);
    void* presenter = ffu_d3d11_create();
    if (!presenter) { std::puts("SKIP: driver has no video processor"); UnityPluginUnload(); context->Release(); device->Release(); return 77; }

    // Use slice 1 of an array, like FFmpeg's actual D3D11VA decoder pool.
    constexpr int width = 32, height = 32;
    std::vector<uint8_t> black(width * height * 3 / 2, 128), pattern(black);
    for (int y = 0; y < height; ++y)
        for (int x = 0; x < width; ++x) { black[y * width + x] = 16; pattern[y * width + x] = y < height / 2 ? 235 : 16; }
    D3D11_SUBRESOURCE_DATA initial[2]{};
    initial[0].pSysMem = black.data(); initial[0].SysMemPitch = width;
    initial[1].pSysMem = pattern.data(); initial[1].SysMemPitch = width;
    D3D11_TEXTURE2D_DESC desc{};
    desc.Width = width; desc.Height = height; desc.ArraySize = 2; desc.MipLevels = 1;
    desc.Format = DXGI_FORMAT_NV12; desc.SampleDesc.Count = 1; desc.BindFlags = D3D11_BIND_DECODER;
    ID3D11Texture2D* input = nullptr;
    hr = device->CreateTexture2D(&desc, initial, &input);
    if (FAILED(hr)) { std::printf("SKIP: NV12 array creation unsupported (%08lx)\n", static_cast<unsigned long>(hr)); return 77; }
    AVFrame* frame = av_frame_alloc();
    frame->width = width; frame->height = height; frame->format = AV_PIX_FMT_D3D11;
    frame->colorspace = AVCOL_SPC_BT709; frame->color_range = AVCOL_RANGE_MPEG;
    frame->data[0] = reinterpret_cast<uint8_t*>(input);
    frame->data[1] = reinterpret_cast<uint8_t*>(1);
    frame->buf[0] = av_buffer_create(reinterpret_cast<uint8_t*>(input), 1, FreeFrame, nullptr, 0);
    auto* output = static_cast<ID3D11Texture2D*>(ffu_d3d11_create_output(presenter, width, height));
    void* packet = ffu_d3d11_prepare(presenter, frame, output);
    if (!packet) { std::printf("FAIL: surface preparation (%08x)\n", ffu_d3d11_error(presenter)); return 1; }
    // Compare this CPU-only prepare/cancel loop between builds. The first live
    // packet keeps the configuration warm; no GPU work or readback is timed.
    constexpr int iterations = 256;
    const auto start = std::chrono::steady_clock::now();
    for (int i = 0; i < iterations; ++i) {
        void* canceled = ffu_d3d11_prepare(presenter, frame, output);
        if (!canceled) { std::puts("FAIL: repeated surface preparation"); return 1; }
        ffu_packet_cancel(canceled);
    }
    const double microseconds = std::chrono::duration<double, std::micro>(std::chrono::steady_clock::now() - start).count() / iterations;
    std::printf("BENCH: D3D11 prepare/cancel mean %.3f us (%d iterations, warm configuration)\n", microseconds, iterations);
    av_frame_free(&frame);
    if (released != 0) { std::puts("FAIL: decoder surface recycled before rendering"); return 1; }
    ffu_render_callback()(ffu_event_id(FfuSubmitD3D11), packet);
    ffu_render_callback()(ffu_event_id(FfuDrain), nullptr);
    if (released != 1 || ffu_d3d11_error(presenter) != 0) {
        std::printf("FAIL: retirement released=%d, error=%08x\n", released, ffu_d3d11_error(presenter)); return 1;
    }
    D3D11_TEXTURE2D_DESC read{};
    output->GetDesc(&read);
    read.Usage = D3D11_USAGE_STAGING; read.BindFlags = 0; read.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    ID3D11Texture2D* staging = nullptr;
    hr = device->CreateTexture2D(&read, nullptr, &staging);
    if (FAILED(hr)) return 1;
    context->CopyResource(staging, output);
    D3D11_MAPPED_SUBRESOURCE mapped{};
    hr = context->Map(staging, 0, D3D11_MAP_READ, 0, &mapped);
    if (FAILED(hr)) return 1;
    auto* pixels = static_cast<uint8_t*>(mapped.pData);
    const int white = pixels[4 * 8 + mapped.RowPitch * 8];
    const int dark = pixels[4 * 8 + mapped.RowPitch * 24];
    context->Unmap(staging, 0);
    staging->Release();
    if (white < 240 || dark > 15) { std::printf("FAIL: wrong array slice/range white=%d dark=%d\n", white, dark); return 1; }

    // Prepare multiple packets before submitting any. Replace the target at the
    // same dimensions, cancel a packet, resize, then restore the old dimensions.
    // Old packets must retain their original views/processors and color state.
    AVFrame* limited = CreateFrame(width, height, 16, AVCOL_RANGE_MPEG);
    AVFrame* full = limited ? av_frame_clone(limited) : nullptr;
    AVFrame* resized = CreateFrame(64, 48, 235, AVCOL_RANGE_MPEG);
    if (!limited || !full || !resized) { std::puts("FAIL: cache test input allocation"); return 1; }
    full->color_range = AVCOL_RANGE_JPEG;
    auto* other = static_cast<ID3D11Texture2D*>(ffu_d3d11_create_output(presenter, width, height));
    auto* large = static_cast<ID3D11Texture2D*>(ffu_d3d11_create_output(presenter, 64, 48));
    auto* restored = static_cast<ID3D11Texture2D*>(ffu_d3d11_create_output(presenter, width, height));
    void* first = ffu_d3d11_prepare(presenter, limited, output);
    void* canceled = ffu_d3d11_prepare(presenter, full, other);
    if (!canceled) { std::puts("FAIL: canceled cache packet preparation"); return 1; }
    ffu_packet_cancel(canceled);
    void* second = ffu_d3d11_prepare(presenter, full, other);
    void* third = ffu_d3d11_prepare(presenter, resized, large);
    void* fourth = ffu_d3d11_prepare(presenter, limited, restored);
    if (!first || !second || !third || !fourth) { std::puts("FAIL: cache/resize packet preparation"); return 1; }
    av_frame_free(&limited); av_frame_free(&full); av_frame_free(&resized);
    if (released != 1) { std::puts("FAIL: cache replacement released in-flight decoder surfaces"); return 1; }
    // Packets also keep the presenter/cache alive after the managed owner leaves.
    ffu_d3d11_release(presenter);
    presenter = nullptr;
    for (void* prepared : {first, second, third, fourth})
        ffu_render_callback()(ffu_event_id(FfuSubmitD3D11), prepared);
    ffu_render_callback()(ffu_event_id(FfuDrain), nullptr);
    if (released != 3) { std::printf("FAIL: cache retirement released=%d\n", released); return 1; }
    const int limitedPixel = ReadPixel(context, output), fullPixel = ReadPixel(context, other);
    const int resizedPixel = ReadPixel(context, large), restoredPixel = ReadPixel(context, restored);
    if (limitedPixel < 0 || limitedPixel > 5 || fullPixel < 12 || fullPixel > 20 || resizedPixel < 240 || restoredPixel < 0 || restoredPixel > 5) {
        std::printf("FAIL: cache target/resize/color state pixels=%d,%d,%d,%d\n", limitedPixel, fullPixel, resizedPixel, restoredPixel);
        return 1;
    }
    ffu_d3d11_release_output(other); ffu_d3d11_release_output(large); ffu_d3d11_release_output(restored);
    ffu_d3d11_release_output(output);
    UnityPluginUnload();
    context->Release(); device->Release();
    std::printf("PASS: D3D11 NV12 array slice conversion, range and GPU surface lifetime (white=%d dark=%d)\n", white, dark);
    std::printf("PASS: D3D11 cached resources, cancellation, target replacement, resize and presenter lifetime (pixels=%d,%d,%d,%d)\n",
        limitedPixel, fullPixel, resizedPixel, restoredPixel);
    return 0;
}
