#include <d3d11_4.h>
#include <d3d10.h>
#include <d3d12.h>
#include <dxgi1_4.h>
#include "D3D12.h"
#include "IUnityGraphicsD3D12.h"
#include <mutex>
#include <vector>
#include <new>
#include <thread>
#include <chrono>

template<class T> static void Drop(T*& p) { if (p) { p->Release(); p = nullptr; } }
static std::mutex stateMutex;
static IUnityGraphicsD3D12v8* unity12 = nullptr;
static ID3D12Device* device12 = nullptr;
static ID3D11Device5* device11 = nullptr;
static ID3D11DeviceContext4* context11 = nullptr;
static ID3D12Fence* ready12 = nullptr;
static ID3D11Fence* ready11 = nullptr;
static ID3D12Fence* completed12 = nullptr;
static std::atomic<int> status{8}, inFlight{0};
static UINT64 sequence = 0; // submission thread only; each fence has a single producer

struct D3D12Packet {
    void* presenter = nullptr;
    void* conversion = nullptr;
    ID3D12Resource* shared12 = nullptr;
    ID3D11Texture2D* shared11 = nullptr;
    ID3D12Resource* target = nullptr;
    ID3D12CommandAllocator* allocator = nullptr;
    ID3D12GraphicsCommandList* command = nullptr;
    ID3D12Fence* complete = nullptr;
    UINT64 value = 0;
    bool statePrepared = false;
    ~D3D12Packet() {
        if (conversion) ffu_packet_cancel(conversion);
        Drop(command); Drop(allocator); Drop(target); Drop(shared11); Drop(shared12); Drop(complete);
        if (presenter) ffu_d3d11_release(presenter);
        --inFlight;
    }
};
static std::vector<D3D12Packet*> pending; // only submission-thread events access this list

void FfuD3D12Poll(bool drain) {
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
    for (auto it = pending.begin(); it != pending.end();) {
        D3D12Packet* packet = *it;
        UINT64 done = packet->complete ? packet->complete->GetCompletedValue() : 0;
        while (drain && done < packet->value && std::chrono::steady_clock::now() < deadline) {
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
            done = packet->complete->GetCompletedValue();
        }
        if (done >= packet->value && packet->value != 0) { delete packet; it = pending.erase(it); }
        else ++it; // bounded retention rather than reusing GPU resources after a driver timeout
    }
}
void FfuD3D12Shutdown() {
    FfuD3D12Poll(true);
    std::unique_lock<std::mutex> lock(stateMutex);
    unity12 = nullptr;
    Drop(ready11); Drop(ready12); Drop(completed12); Drop(context11); Drop(device11); Drop(device12);
    status = 8;
}
ID3D11Device* FfuD3D12Initialize(IUnityInterfaces* interfaces) {
    std::lock_guard<std::mutex> lock(stateMutex);
    unity12 = FfuGetInterface<IUnityGraphicsD3D12v8>(interfaces);
    if (!unity12) { status = 8; return nullptr; }
    device12 = unity12->GetDevice();
    if (!device12) { status = 9; return nullptr; }
    device12->AddRef();
    IDXGIFactory4* factory = nullptr;
    IDXGIAdapter1* adapter = nullptr;
    ID3D11Device* device = nullptr;
    ID3D11DeviceContext* immediate = nullptr;
    ID3D10Multithread* threading = nullptr;
    HANDLE handle = nullptr;
    HRESULT result = CreateDXGIFactory1(IID_PPV_ARGS(&factory));
    if (SUCCEEDED(result)) result = factory->EnumAdapterByLuid(device12->GetAdapterLuid(), IID_PPV_ARGS(&adapter));
    if (SUCCEEDED(result)) result = D3D11CreateDevice(adapter, D3D_DRIVER_TYPE_UNKNOWN, nullptr,
        D3D11_CREATE_DEVICE_VIDEO_SUPPORT, nullptr, 0, D3D11_SDK_VERSION, &device, nullptr, &immediate);
    if (SUCCEEDED(result)) result = device->QueryInterface(IID_PPV_ARGS(&device11));
    if (SUCCEEDED(result)) result = immediate->QueryInterface(IID_PPV_ARGS(&context11));
    if (SUCCEEDED(result)) result = immediate->QueryInterface(IID_PPV_ARGS(&threading));
    if (SUCCEEDED(result)) threading->SetMultithreadProtected(TRUE);
    if (SUCCEEDED(result)) result = device12->CreateFence(0, D3D12_FENCE_FLAG_SHARED, IID_PPV_ARGS(&ready12));
    if (SUCCEEDED(result)) result = device12->CreateSharedHandle(ready12, nullptr, GENERIC_ALL, nullptr, &handle);
    if (SUCCEEDED(result)) result = device11->OpenSharedFence(handle, IID_PPV_ARGS(&ready11));
    if (handle) CloseHandle(handle);
    if (SUCCEEDED(result)) result = device12->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&completed12));
    Drop(threading); Drop(immediate); Drop(adapter); Drop(factory);
    if (FAILED(result)) {
        status = result;
        Drop(device); Drop(ready11); Drop(ready12); Drop(completed12); Drop(context11); Drop(device11); Drop(device12);
        unity12 = nullptr;
        return nullptr;
    }
    UnityD3D12PluginEventConfig record{};
    record.graphicsQueueAccess = kUnityD3D12GraphicsQueueAccess_DontCare;
    unity12->ConfigureEvent(FfuEventId(FfuPrepareD3D12), &record);
    UnityD3D12PluginEventConfig submit{};
    submit.graphicsQueueAccess = kUnityD3D12GraphicsQueueAccess_Allow;
    submit.flags = kUnityD3D12EventConfigFlag_FlushCommandBuffers | kUnityD3D12EventConfigFlag_SyncWorkerThreads;
    unity12->ConfigureEvent(FfuEventId(FfuSubmitD3D12), &submit);
    unity12->ConfigureEvent(FfuEventId(FfuDrain), &submit);
    sequence = 0;
    status = 0;
    return device; // owned reference becomes the shared decoder device in D3D11.cpp
}
int FfuD3D12Status() { return status.load(); }

FFU_EXPORT void* FFU_CALL ffu_d3d12_prepare(void* presenter, const AVFrame* frame, void* unityTexture) {
    if (!presenter || !frame || !unityTexture || inFlight.fetch_add(1) >= 24) {
        if (presenter && frame && unityTexture) --inFlight;
        return nullptr;
    }
    auto* packet = new (std::nothrow) D3D12Packet();
    if (!packet) { --inFlight; return nullptr; }
    packet->presenter = presenter;
    FfuD3D11RetainPresenter(presenter);
    packet->target = static_cast<ID3D12Resource*>(unityTexture);
    packet->target->AddRef();
    auto desc = packet->target->GetDesc();
    if (desc.Dimension != D3D12_RESOURCE_DIMENSION_TEXTURE2D || desc.SampleDesc.Count != 1 ||
        desc.Width != static_cast<UINT64>(frame->width) || desc.Height != static_cast<UINT>(frame->height)) {
        delete packet; return nullptr;
    }
    if (desc.Format == DXGI_FORMAT_R8G8B8A8_TYPELESS || desc.Format == DXGI_FORMAT_R8G8B8A8_UNORM_SRGB)
        desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    if (desc.Format == DXGI_FORMAT_B8G8R8A8_TYPELESS || desc.Format == DXGI_FORMAT_B8G8R8A8_UNORM_SRGB)
        desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    if (desc.Format != DXGI_FORMAT_R8G8B8A8_UNORM && desc.Format != DXGI_FORMAT_B8G8R8A8_UNORM) { delete packet; return nullptr; }
    desc.Flags = D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET;
    desc.DepthOrArraySize = desc.MipLevels = 1;
    desc.Layout = D3D12_TEXTURE_LAYOUT_UNKNOWN;
    D3D12_HEAP_PROPERTIES heap{}; heap.Type = D3D12_HEAP_TYPE_DEFAULT;
    HANDLE handle = nullptr;
    std::unique_lock<std::mutex> lock(stateMutex);
    if (!device12 || !device11) { delete packet; return nullptr; }
    HRESULT result = device12->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_SHARED, &desc,
        D3D12_RESOURCE_STATE_COMMON, nullptr, IID_PPV_ARGS(&packet->shared12));
    if (SUCCEEDED(result)) result = device12->CreateSharedHandle(packet->shared12, nullptr, GENERIC_ALL, nullptr, &handle);
    if (SUCCEEDED(result)) result = device11->OpenSharedResource1(handle, IID_PPV_ARGS(&packet->shared11));
    if (handle) CloseHandle(handle);
    if (SUCCEEDED(result)) result = device12->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&packet->allocator));
    if (SUCCEEDED(result)) result = device12->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT,
        packet->allocator, nullptr, IID_PPV_ARGS(&packet->command));
    if (FAILED(result)) { FfuD3D11SetError(presenter, result); delete packet; return nullptr; }
    packet->complete = completed12; completed12->AddRef();
    lock.unlock();
    packet->conversion = ffu_d3d11_prepare(presenter, frame, packet->shared11);
    if (!packet->conversion) { delete packet; return nullptr; }
    D3D12_RESOURCE_BARRIER barrier{};
    barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barrier.Transition.pResource = packet->shared12;
    barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_COMMON;
    barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_COPY_SOURCE;
    packet->command->ResourceBarrier(1, &barrier);
    D3D12_TEXTURE_COPY_LOCATION source{}; source.pResource = packet->shared12; source.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
    D3D12_TEXTURE_COPY_LOCATION target{}; target.pResource = packet->target; target.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
    packet->command->CopyTextureRegion(&target, 0, 0, 0, &source, nullptr);
    result = packet->command->Close();
    if (FAILED(result)) { FfuD3D11SetError(presenter, result); delete packet; return nullptr; }
    return packet;
}
void FfuD3D12Render(int event, void* data) {
    auto* packet = static_cast<D3D12Packet*>(data);
    if (event == FfuPrepareD3D12 && packet) {
        if (unity12) {
            unity12->RequestResourceState(packet->target, D3D12_RESOURCE_STATE_COPY_DEST);
            packet->statePrepared = true;
        }
        return;
    }
    if (event == FfuDrain) { FfuD3D12Poll(true); return; }
    if (event != FfuSubmitD3D12 || !packet) return;
    FfuD3D12Poll(false);
    if (!packet->statePrepared || !unity12 || !context11) { FfuD3D11SetError(packet->presenter, E_FAIL); delete packet; return; }
    // This event runs on Unity's submission thread after earlier Unity command
    // buffers have been flushed. No arbitrary-thread access to Unity's queue.
    ID3D12CommandQueue* queue = unity12->GetCommandQueue();
    if (!queue) { FfuD3D11SetError(packet->presenter, E_FAIL); delete packet; return; }
    FfuD3D11Submit(packet->conversion);
    packet->conversion = nullptr;
    const UINT64 value = ++sequence;
    HRESULT result = context11->Signal(ready11, value);
    context11->Flush();
    if (SUCCEEDED(result)) result = queue->Wait(ready12, value);
    if (FAILED(result)) { FfuD3D11SetError(packet->presenter, result); pending.push_back(packet); return; }
    ID3D12CommandList* commands[] = {packet->command};
    queue->ExecuteCommandLists(1, commands);
    result = queue->Signal(completed12, value);
    if (FAILED(result)) FfuD3D11SetError(packet->presenter, result);
    else packet->value = value;
    // Target stays COPY_DEST, the state already tracked by RequestResourceState.
    // Unity transitions it for the following shader read. A fence keeps the copy
    // allocator, shared texture and target alive until GPU work actually finishes.
    pending.push_back(packet);
}
FFU_EXPORT void FFU_CALL ffu_d3d12_cancel(void* packet) { delete static_cast<D3D12Packet*>(packet); }
