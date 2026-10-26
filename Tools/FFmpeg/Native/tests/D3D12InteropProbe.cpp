// Real-device capability probe: D3D11 video processing -> shared D3D12 RGBA,
// cross-API fence wait, D3D12 GPU copy, then a test-only CPU pixel readback.
#include <d3d11_4.h>
#include <d3d12.h>
#include <dxgi1_4.h>
#include <cstdio>
#include <vector>
#include <thread>
#include <chrono>
#define CHECK(call) do { HRESULT result = (call); if (FAILED(result)) { std::printf("FAIL line %d: %s = %08lx\n", __LINE__, #call, static_cast<unsigned long>(result)); return 1; } } while (0)
int main() {
    ID3D12Device* device12 = nullptr;
    CHECK(D3D12CreateDevice(nullptr, D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&device12)));
    LUID luid = device12->GetAdapterLuid();
    IDXGIFactory4* factory = nullptr;
    IDXGIAdapter1* adapter = nullptr;
    CHECK(CreateDXGIFactory1(IID_PPV_ARGS(&factory)));
    CHECK(factory->EnumAdapterByLuid(luid, IID_PPV_ARGS(&adapter)));
    ID3D11Device* device11 = nullptr;
    ID3D11DeviceContext* context11 = nullptr;
    CHECK(D3D11CreateDevice(adapter, D3D_DRIVER_TYPE_UNKNOWN, nullptr, D3D11_CREATE_DEVICE_VIDEO_SUPPORT,
        nullptr, 0, D3D11_SDK_VERSION, &device11, nullptr, &context11));
    ID3D11Device5* device5 = nullptr;
    ID3D11DeviceContext4* context4 = nullptr;
    ID3D11VideoDevice* video = nullptr;
    ID3D11VideoContext* videoContext = nullptr;
    CHECK(device11->QueryInterface(IID_PPV_ARGS(&device5)));
    CHECK(context11->QueryInterface(IID_PPV_ARGS(&context4)));
    CHECK(device11->QueryInterface(IID_PPV_ARGS(&video)));
    CHECK(context11->QueryInterface(IID_PPV_ARGS(&videoContext)));
    D3D12_HEAP_PROPERTIES heap{}; heap.Type = D3D12_HEAP_TYPE_DEFAULT;
    D3D12_RESOURCE_DESC outputDesc{};
    outputDesc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
    outputDesc.Width = outputDesc.Height = 32;
    outputDesc.DepthOrArraySize = outputDesc.MipLevels = 1;
    outputDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    outputDesc.SampleDesc.Count = 1;
    outputDesc.Flags = D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET;
    ID3D12Resource* output12 = nullptr;
    CHECK(device12->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_SHARED, &outputDesc,
        D3D12_RESOURCE_STATE_COMMON, nullptr, IID_PPV_ARGS(&output12)));
    HANDLE shared = nullptr;
    CHECK(device12->CreateSharedHandle(output12, nullptr, GENERIC_ALL, nullptr, &shared));
    ID3D11Texture2D* output11 = nullptr;
    HRESULT opened = device5->OpenSharedResource1(shared, IID_PPV_ARGS(&output11));
    CloseHandle(shared);
    CHECK(opened);
    ID3D12Fence* fence12 = nullptr;
    CHECK(device12->CreateFence(0, D3D12_FENCE_FLAG_SHARED, IID_PPV_ARGS(&fence12)));
    CHECK(device12->CreateSharedHandle(fence12, nullptr, GENERIC_ALL, nullptr, &shared));
    ID3D11Fence* fence11 = nullptr;
    opened = device5->OpenSharedFence(shared, IID_PPV_ARGS(&fence11));
    CloseHandle(shared);
    CHECK(opened);
    D3D12_COMMAND_QUEUE_DESC queueDesc{}; queueDesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
    ID3D12CommandQueue* queue = nullptr;
    CHECK(device12->CreateCommandQueue(&queueDesc, IID_PPV_ARGS(&queue)));

    std::vector<unsigned char> pixels(32 * 32 * 3 / 2, 128);
    for (int y = 0; y < 32; ++y) for (int x = 0; x < 32; ++x) pixels[y * 32 + x] = y < 16 ? 235 : 16;
    D3D11_TEXTURE2D_DESC inputDesc{};
    inputDesc.Width = inputDesc.Height = 32; inputDesc.MipLevels = inputDesc.ArraySize = 1;
    inputDesc.Format = DXGI_FORMAT_NV12; inputDesc.SampleDesc.Count = 1; inputDesc.BindFlags = D3D11_BIND_DECODER;
    D3D11_SUBRESOURCE_DATA initial{}; initial.pSysMem = pixels.data(); initial.SysMemPitch = 32;
    ID3D11Texture2D* input = nullptr;
    CHECK(device11->CreateTexture2D(&inputDesc, &initial, &input));
    D3D11_VIDEO_PROCESSOR_CONTENT_DESC content{};
    content.InputFrameFormat = D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE;
    content.InputWidth = content.InputHeight = content.OutputWidth = content.OutputHeight = 32;
    content.InputFrameRate = content.OutputFrameRate = {30, 1}; content.Usage = D3D11_VIDEO_USAGE_PLAYBACK_NORMAL;
    ID3D11VideoProcessorEnumerator* enumerator = nullptr;
    ID3D11VideoProcessor* processor = nullptr;
    CHECK(video->CreateVideoProcessorEnumerator(&content, &enumerator));
    CHECK(video->CreateVideoProcessor(enumerator, 0, &processor));
    D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC iv{}; iv.ViewDimension = D3D11_VPIV_DIMENSION_TEXTURE2D;
    D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC ov{}; ov.ViewDimension = D3D11_VPOV_DIMENSION_TEXTURE2D;
    ID3D11VideoProcessorInputView* inputView = nullptr;
    ID3D11VideoProcessorOutputView* outputView = nullptr;
    CHECK(video->CreateVideoProcessorInputView(input, enumerator, &iv, &inputView));
    CHECK(video->CreateVideoProcessorOutputView(output11, enumerator, &ov, &outputView));
    D3D11_VIDEO_PROCESSOR_COLOR_SPACE inputColor{}; inputColor.Nominal_Range = 1;
    videoContext->VideoProcessorSetStreamColorSpace(processor, 0, &inputColor);
    D3D11_VIDEO_PROCESSOR_COLOR_SPACE outputColor{}; outputColor.Nominal_Range = 2;
    videoContext->VideoProcessorSetOutputColorSpace(processor, &outputColor);
    videoContext->VideoProcessorSetStreamAutoProcessingMode(processor, 0, FALSE);
    D3D11_VIDEO_PROCESSOR_STREAM stream{}; stream.Enable = TRUE; stream.pInputSurface = inputView;
    CHECK(videoContext->VideoProcessorBlt(processor, outputView, 0, 1, &stream));
    CHECK(context4->Signal(fence11, 1));
    context11->Flush();
    CHECK(queue->Wait(fence12, 1));

    ID3D12CommandAllocator* allocator = nullptr;
    ID3D12GraphicsCommandList* list = nullptr;
    CHECK(device12->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&allocator)));
    CHECK(device12->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, allocator, nullptr, IID_PPV_ARGS(&list)));
    D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint{};
    UINT64 bytes = 0;
    device12->GetCopyableFootprints(&outputDesc, 0, 1, 0, &footprint, nullptr, nullptr, &bytes);
    D3D12_HEAP_PROPERTIES readHeap{}; readHeap.Type = D3D12_HEAP_TYPE_READBACK;
    D3D12_RESOURCE_DESC readDesc{}; readDesc.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
    readDesc.Width = bytes; readDesc.Height = readDesc.DepthOrArraySize = readDesc.MipLevels = 1;
    readDesc.SampleDesc.Count = 1; readDesc.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
    ID3D12Resource* readback = nullptr;
    CHECK(device12->CreateCommittedResource(&readHeap, D3D12_HEAP_FLAG_NONE, &readDesc,
        D3D12_RESOURCE_STATE_COPY_DEST, nullptr, IID_PPV_ARGS(&readback)));
    D3D12_RESOURCE_BARRIER barrier{}; barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barrier.Transition.pResource = output12; barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_COMMON; barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_COPY_SOURCE;
    list->ResourceBarrier(1, &barrier);
    D3D12_TEXTURE_COPY_LOCATION from{}; from.pResource = output12; from.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
    D3D12_TEXTURE_COPY_LOCATION to{}; to.pResource = readback; to.Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT; to.PlacedFootprint = footprint;
    list->CopyTextureRegion(&to, 0, 0, 0, &from, nullptr);
    CHECK(list->Close());
    ID3D12CommandList* submit[] = {list}; queue->ExecuteCommandLists(1, submit);
    CHECK(queue->Signal(fence12, 2));
    auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
    while (fence12->GetCompletedValue() < 2 && std::chrono::steady_clock::now() < deadline)
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    if (fence12->GetCompletedValue() != 2) { std::puts("FAIL: D3D12 fence timeout/device removal"); return 1; }
    unsigned char* mapped = nullptr;
    D3D12_RANGE range{0, static_cast<SIZE_T>(bytes)};
    CHECK(readback->Map(0, &range, reinterpret_cast<void**>(&mapped)));
    int white = mapped[8 * footprint.Footprint.RowPitch + 8 * 4];
    int black = mapped[24 * footprint.Footprint.RowPitch + 8 * 4];
    D3D12_RANGE written{}; readback->Unmap(0, &written);
    if (white < 240 || black > 15) { std::printf("FAIL: shared pixels white=%d black=%d\n", white, black); return 1; }
    std::printf("PASS: D3D11 video -> shared D3D12 RGBA + shared fence GPU wait + D3D12 copy (white=%d black=%d)\n", white, black);
    readback->Release(); list->Release(); allocator->Release(); inputView->Release(); outputView->Release();
    processor->Release(); enumerator->Release(); input->Release(); fence11->Release(); fence12->Release();
    output11->Release(); output12->Release(); queue->Release(); videoContext->Release(); video->Release();
    context4->Release(); device5->Release(); context11->Release(); device11->Release(); adapter->Release(); factory->Release(); device12->Release();
    return 0;
}
