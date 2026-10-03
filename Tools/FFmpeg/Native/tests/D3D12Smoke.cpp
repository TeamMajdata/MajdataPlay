// Exercise the actual linked bridge, including both D3D11 and D3D12 packet
// destructors. The independent capability probe cannot detect cross-TU ODR bugs.
#include <d3d11.h>
#include <d3d12.h>
#include <dxgi1_4.h>
#include "../D3D12.h"
#include "IUnityGraphicsD3D12.h"
#include <cstdio>
#include <vector>
#include <thread>
#include <chrono>
extern "C" {
#include <libavutil/buffer.h>
}
#define CHECK(call) do { HRESULT h = (call); if (FAILED(h)) { std::printf("FAIL line %d HRESULT %08lx\n", __LINE__, static_cast<unsigned long>(h)); return 1; } } while (0)
static ID3D12Device* device12;
static ID3D12CommandQueue* queue12;
static IUnityGraphics graphics{};
static IUnityGraphicsD3D12v8 api12{};
static int released = 0, requested = 0, configured = 0;
static UnityGfxRenderer UNITY_INTERFACE_API Renderer() { return kUnityGfxRendererD3D12; }
static void UNITY_INTERFACE_API Register(IUnityGraphicsDeviceEventCallback) {}
static void UNITY_INTERFACE_API Unregister(IUnityGraphicsDeviceEventCallback) {}
static int UNITY_INTERFACE_API Reserve(int) { return 160; }
static ID3D12Device* UNITY_INTERFACE_API Device() { return device12; }
static ID3D12CommandQueue* UNITY_INTERFACE_API Queue() { return queue12; }
static void UNITY_INTERFACE_API Configure(int id, const UnityD3D12PluginEventConfig*) { if (id > 160) ++configured; }
static void UNITY_INTERFACE_API Request(ID3D12Resource*, D3D12_RESOURCE_STATES state) { if (state == D3D12_RESOURCE_STATE_COPY_DEST) ++requested; }
static IUnityInterface* UNITY_INTERFACE_API InterfaceSplit(unsigned long long high, unsigned long long low) {
    const UnityInterfaceGUID id(high, low);
    if (id == GetUnityInterfaceGUID<IUnityGraphics>()) return &graphics;
    if (id == GetUnityInterfaceGUID<IUnityGraphicsD3D12v8>()) return &api12;
    return nullptr;
}
static void FreeFrame(void*, uint8_t* texture) { reinterpret_cast<ID3D11Texture2D*>(texture)->Release(); ++released; }
int main() {
    CHECK(D3D12CreateDevice(nullptr, D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&device12)));
    D3D12_COMMAND_QUEUE_DESC queueDesc{}; queueDesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
    CHECK(device12->CreateCommandQueue(&queueDesc, IID_PPV_ARGS(&queue12)));
    graphics.GetRenderer = Renderer; graphics.RegisterDeviceEventCallback = Register;
    graphics.UnregisterDeviceEventCallback = Unregister; graphics.ReserveEventIDRange = Reserve;
    api12.GetDevice = Device; api12.GetCommandQueue = Queue; api12.ConfigureEvent = Configure; api12.RequestResourceState = Request;
    IUnityInterfaces interfaces{}; interfaces.GetInterfaceSplit = InterfaceSplit;
    UnityPluginLoad(&interfaces);
    if (ffu_abi_version() != 2 || ffu_capabilities() != FfuD3D12GpuCopy || configured != 3) {
        std::printf("FAIL initialization status=%d configured=%d\n", ffu_initialization_status(), configured); return 1;
    }
    auto* device11 = static_cast<ID3D11Device*>(ffu_d3d11_acquire_device());
    void* presenter = ffu_d3d11_create();
    if (!device11 || !presenter) return 1;
    D3D12_RESOURCE_DESC desc{};
    desc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D; desc.Width = desc.Height = 32;
    desc.DepthOrArraySize = desc.MipLevels = 1; desc.SampleDesc.Count = 1;
    desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM; desc.Flags = D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET;
    D3D12_HEAP_PROPERTIES heap{}; heap.Type = D3D12_HEAP_TYPE_DEFAULT;
    ID3D12Resource* output = nullptr;
    CHECK(device12->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_NONE, &desc,
        D3D12_RESOURCE_STATE_COPY_DEST, nullptr, IID_PPV_ARGS(&output)));
    for (int iteration = 0; iteration < 5; ++iteration) {
        std::vector<uint8_t> pixels(32 * 32 * 3 / 2, 128);
        for (int y = 0; y < 32; ++y) for (int x = 0; x < 32; ++x) pixels[y * 32 + x] = y < 16 ? 235 : 16;
        D3D11_TEXTURE2D_DESC inputDesc{}; inputDesc.Width = inputDesc.Height = 32;
        inputDesc.MipLevels = inputDesc.ArraySize = 1; inputDesc.SampleDesc.Count = 1;
        inputDesc.Format = DXGI_FORMAT_NV12; inputDesc.BindFlags = D3D11_BIND_DECODER;
        D3D11_SUBRESOURCE_DATA data{}; data.pSysMem = pixels.data(); data.SysMemPitch = 32;
        ID3D11Texture2D* input = nullptr;
        CHECK(device11->CreateTexture2D(&inputDesc, &data, &input));
        AVFrame* frame = av_frame_alloc(); frame->width = frame->height = 32;
        frame->format = AV_PIX_FMT_D3D11; frame->color_range = AVCOL_RANGE_MPEG; frame->colorspace = AVCOL_SPC_BT709;
        frame->data[0] = reinterpret_cast<uint8_t*>(input);
        frame->buf[0] = av_buffer_create(frame->data[0], 1, FreeFrame, nullptr, 0);
        void* packet = ffu_d3d12_prepare(presenter, frame, output);
        av_frame_free(&frame);
        if (!packet || released != iteration) { std::puts("FAIL: prepare/early release"); return 1; }
        if (iteration == 4) ffu_d3d12_cancel(packet);
        else {
            ffu_render_callback()(ffu_event_id(FfuPrepareD3D12), packet);
            ffu_render_callback()(ffu_event_id(FfuSubmitD3D12), packet);
            ffu_render_callback()(ffu_event_id(FfuDrain), nullptr);
        }
        if (released != iteration + 1 || ffu_d3d11_error(presenter)) { std::puts("FAIL: packet retirement"); return 1; }
    }
    if (requested != 4) return 1;
    D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint{}; UINT64 size = 0;
    device12->GetCopyableFootprints(&desc, 0, 1, 0, &footprint, nullptr, nullptr, &size);
    D3D12_RESOURCE_DESC readDesc{}; readDesc.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
    readDesc.Width = size; readDesc.Height = readDesc.DepthOrArraySize = readDesc.MipLevels = 1;
    readDesc.SampleDesc.Count = 1; readDesc.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
    heap.Type = D3D12_HEAP_TYPE_READBACK;
    ID3D12Resource* readback = nullptr;
    CHECK(device12->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_NONE, &readDesc,
        D3D12_RESOURCE_STATE_COPY_DEST, nullptr, IID_PPV_ARGS(&readback)));
    ID3D12CommandAllocator* allocator = nullptr; ID3D12GraphicsCommandList* list = nullptr; ID3D12Fence* done = nullptr;
    CHECK(device12->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&allocator)));
    CHECK(device12->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, allocator, nullptr, IID_PPV_ARGS(&list)));
    D3D12_RESOURCE_BARRIER barrier{}; barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barrier.Transition.pResource = output; barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_COPY_DEST; barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_COPY_SOURCE;
    list->ResourceBarrier(1, &barrier);
    D3D12_TEXTURE_COPY_LOCATION from{}; from.pResource = output; from.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
    D3D12_TEXTURE_COPY_LOCATION to{}; to.pResource = readback; to.Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT; to.PlacedFootprint = footprint;
    list->CopyTextureRegion(&to, 0, 0, 0, &from, nullptr); CHECK(list->Close());
    ID3D12CommandList* commands[] = {list}; queue12->ExecuteCommandLists(1, commands);
    CHECK(device12->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&done))); CHECK(queue12->Signal(done, 1));
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
    while (done->GetCompletedValue() < 1 && std::chrono::steady_clock::now() < deadline) std::this_thread::sleep_for(std::chrono::milliseconds(1));
    if (done->GetCompletedValue() != 1) return 1;
    uint8_t* mapped = nullptr; D3D12_RANGE range{0, static_cast<SIZE_T>(size)};
    CHECK(readback->Map(0, &range, reinterpret_cast<void**>(&mapped)));
    const int white = mapped[8 * footprint.Footprint.RowPitch + 32], black = mapped[24 * footprint.Footprint.RowPitch + 32];
    D3D12_RANGE written{}; readback->Unmap(0, &written);
    done->Release(); list->Release(); allocator->Release(); readback->Release(); output->Release();
    ffu_d3d11_release(presenter); device11->Release(); UnityPluginUnload(); queue12->Release(); device12->Release();
    if (white < 240 || black > 15) return 1;
    std::printf("PASS: linked D3D12 bridge GPU copy, 4 completed packets + cancellation, AVFrame retirement, white=%d black=%d\n", white, black);
}
