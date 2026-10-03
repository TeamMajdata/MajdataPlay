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
extern "C" {
#include <libavutil/hwcontext.h>
#include <libavutil/hwcontext_d3d12va.h>
}

template<class T> static void Drop(T*& p) { if (p) { p->Release(); p = nullptr; } }
static std::mutex stateMutex;
static IUnityGraphicsD3D12v8* unity12 = nullptr;
static ID3D12Device* device12 = nullptr;
static ID3D11Device5* device11 = nullptr;
static ID3D11DeviceContext4* context11 = nullptr;
static ID3D12Fence* ready12 = nullptr;
static ID3D11Fence* ready11 = nullptr;
static ID3D12Fence* completed12 = nullptr;
static ID3D12VideoDevice* video12 = nullptr;
static ID3D12CommandQueue* processQueue = nullptr;
static ID3D12Fence* processed12 = nullptr;
static std::atomic<int> status{8}, inFlight{0};
static std::atomic<int> nativeStatus{8}, nativeInFlight{0};
static UINT64 sequence = 0; // submission thread only; each fence has a single producer
static void PollNative(bool drain);
static void RenderNative(int event, void* data);

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
    PollNative(drain);
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
    Drop(processed12); Drop(processQueue); Drop(video12);
    Drop(ready11); Drop(ready12); Drop(completed12); Drop(context11); Drop(device11); Drop(device12);
    status = nativeStatus = 8;
}
ID3D11Device* FfuD3D12Initialize(IUnityInterfaces* interfaces) {
    std::lock_guard<std::mutex> lock(stateMutex);
    unity12 = FfuGetInterface<IUnityGraphicsD3D12v8>(interfaces);
    if (!unity12) { status = nativeStatus = 8; return nullptr; }
    device12 = unity12->GetDevice();
    if (!device12) { status = nativeStatus = 9; return nullptr; }
    device12->AddRef();
    HRESULT nativeResult = device12->QueryInterface(IID_PPV_ARGS(&video12));
    D3D12_COMMAND_QUEUE_DESC processDesc{};
    processDesc.Type = D3D12_COMMAND_LIST_TYPE_VIDEO_PROCESS;
    if (SUCCEEDED(nativeResult)) nativeResult = device12->CreateCommandQueue(&processDesc, IID_PPV_ARGS(&processQueue));
    if (SUCCEEDED(nativeResult)) nativeResult = device12->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&processed12));
    if (SUCCEEDED(nativeResult)) nativeResult = device12->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&completed12));
    nativeStatus = nativeResult;
    if (FAILED(nativeResult)) { Drop(processed12); Drop(processQueue); Drop(video12); }
    UnityD3D12PluginEventConfig record{};
    record.graphicsQueueAccess = kUnityD3D12GraphicsQueueAccess_DontCare;
    unity12->ConfigureEvent(FfuEventId(FfuPrepareD3D12), &record);
    unity12->ConfigureEvent(FfuEventId(FfuPrepareNativeD3D12), &record);
    UnityD3D12PluginEventConfig submit{};
    submit.graphicsQueueAccess = kUnityD3D12GraphicsQueueAccess_Allow;
    submit.flags = kUnityD3D12EventConfigFlag_FlushCommandBuffers | kUnityD3D12EventConfigFlag_SyncWorkerThreads;
    unity12->ConfigureEvent(FfuEventId(FfuSubmitD3D12), &submit);
    unity12->ConfigureEvent(FfuEventId(FfuSubmitNativeD3D12), &submit);
    unity12->ConfigureEvent(FfuEventId(FfuDrain), &submit);
    sequence = 0;
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
    if (SUCCEEDED(result) && !completed12) result = device12->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&completed12));
    Drop(threading); Drop(immediate); Drop(adapter); Drop(factory);
    if (FAILED(result)) {
        status = result;
        Drop(device); Drop(ready11); Drop(ready12); Drop(context11); Drop(device11);
        return nullptr;
    }
    status = 0;
    return device; // owned reference becomes the shared decoder device in D3D11.cpp
}
int FfuD3D12Status() { return status.load() == 0 || nativeStatus.load() == 0 ? 0 : status.load(); }
int FfuD3D12Capabilities() {
    std::lock_guard<std::mutex> lock(stateMutex);
    return (device11 ? FfuD3D12GpuCopy : 0) | (video12 && processQueue && processed12 && completed12 ? FfuD3D12NativeDecode : 0);
}

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
    if (event == FfuPrepareNativeD3D12 || event == FfuSubmitNativeD3D12) { RenderNative(event, data); return; }
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

// Native D3D12VA: VIDEO_PROCESS conversion followed by a Unity-queue GPU copy.
// No D3D11 resource, shared handle, staging buffer or CPU pixel map is involved.
struct NativeD3D12Presenter {
    std::atomic<int> references{1};
    std::atomic<int> error{0};
    void Retain() { ++references; }
    void Release() { if (--references == 0) delete this; }
};
struct NativeD3D12Packet {
    NativeD3D12Presenter* owner = nullptr;
    AVFrame* frame = nullptr;
    ID3D12Device* device = nullptr;
    ID3D12Resource* rgba = nullptr;
    ID3D12Resource* target = nullptr;
    ID3D12VideoProcessor* processor = nullptr;
    ID3D12CommandAllocator* processAllocator = nullptr;
    ID3D12VideoProcessCommandList* processCommand = nullptr;
    ID3D12CommandAllocator* copyAllocator = nullptr;
    ID3D12GraphicsCommandList* copyCommand = nullptr;
    ID3D12Fence* complete = nullptr;
    UINT64 value = 0;
    bool statePrepared = false;
    ~NativeD3D12Packet() {
        Drop(copyCommand); Drop(copyAllocator); Drop(processCommand); Drop(processAllocator);
        Drop(processor); Drop(rgba); Drop(target); Drop(complete); Drop(device);
        av_frame_free(&frame);
        if (owner) owner->Release();
        --nativeInFlight;
    }
};
static std::vector<NativeD3D12Packet*> nativePending;

static void PollNative(bool drain) {
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
    for (auto it = nativePending.begin(); it != nativePending.end();) {
        auto* packet = *it;
        bool removed = FAILED(packet->device->GetDeviceRemovedReason());
        UINT64 done = packet->complete->GetCompletedValue();
        while (drain && !removed && (!packet->value || done < packet->value) &&
               std::chrono::steady_clock::now() < deadline) {
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
            removed = FAILED(packet->device->GetDeviceRemovedReason());
            done = packet->complete->GetCompletedValue();
        }
        // A failed signal may leave work in flight: retain until device removal.
        if (removed || (packet->value && done >= packet->value)) {
            delete packet;
            it = nativePending.erase(it);
        } else ++it;
    }
}

FFU_EXPORT int FFU_CALL ffu_d3d12va_status() { return nativeStatus.load(); }
FFU_EXPORT int FFU_CALL ffu_d3d12va_error(void* presenter) {
    return presenter ? static_cast<NativeD3D12Presenter*>(presenter)->error.load() : E_INVALIDARG;
}
FFU_EXPORT void* FFU_CALL ffu_d3d12va_create() {
    std::lock_guard<std::mutex> lock(stateMutex);
    if (!device12 || !video12 || !processQueue || !processed12 || !completed12) return nullptr;
    return new (std::nothrow) NativeD3D12Presenter();
}
FFU_EXPORT void FFU_CALL ffu_d3d12va_release(void* presenter) {
    if (presenter) static_cast<NativeD3D12Presenter*>(presenter)->Release();
}
FFU_EXPORT void* FFU_CALL ffu_d3d12va_acquire_device() {
    std::lock_guard<std::mutex> lock(stateMutex);
    if (!device12 || !video12) return nullptr;
    AVHWDeviceType type = AV_HWDEVICE_TYPE_NONE;
    do { type = av_hwdevice_iterate_types(type); }
    while (type != AV_HWDEVICE_TYPE_NONE && type != AV_HWDEVICE_TYPE_D3D12VA);
    if (type == AV_HWDEVICE_TYPE_NONE) { nativeStatus = E_NOTIMPL; return nullptr; }
    AVBufferRef* buffer = av_hwdevice_ctx_alloc(AV_HWDEVICE_TYPE_D3D12VA);
    if (!buffer) { nativeStatus = E_OUTOFMEMORY; return nullptr; }
    auto* context = reinterpret_cast<AVHWDeviceContext*>(buffer->data);
    auto* hardware = static_cast<AVD3D12VADeviceContext*>(context->hwctx);
    hardware->device = device12;
    device12->AddRef(); // av_hwdevice_ctx_init/unref owns this reference.
    // DPB references can be read concurrently by decode and processing queues.
    // This flag permits both readers and their independent state transitions.
    // Holding the AVFrame until completion prevents concurrent pixel writes.
    hardware->resource_flags = D3D12_RESOURCE_FLAG_ALLOW_SIMULTANEOUS_ACCESS;
    const int result = av_hwdevice_ctx_init(buffer);
    if (result < 0) { nativeStatus = result; av_buffer_unref(&buffer); return nullptr; }
    nativeStatus = 0;
    return buffer;
}

static D3D12_RESOURCE_BARRIER Transition(ID3D12Resource* texture, D3D12_RESOURCE_STATES before,
                                        D3D12_RESOURCE_STATES after, UINT subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES) {
    D3D12_RESOURCE_BARRIER barrier{};
    barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barrier.Transition.pResource = texture;
    barrier.Transition.StateBefore = before;
    barrier.Transition.StateAfter = after;
    barrier.Transition.Subresource = subresource;
    return barrier;
}

FFU_EXPORT void* FFU_CALL ffu_d3d12va_prepare(void* presenter, const AVFrame* frame, void* unityTexture) {
    auto* owner = static_cast<NativeD3D12Presenter*>(presenter);
    if (!owner) return nullptr;
    if (!frame || !unityTexture || frame->format != AV_PIX_FMT_D3D12 || !frame->data[0] ||
        frame->width <= 0 || frame->height <= 0) { owner->error = E_INVALIDARG; return nullptr; }
    if (frame->crop_left || frame->crop_top || frame->colorspace == AVCOL_SPC_BT2020_NCL ||
        frame->colorspace == AVCOL_SPC_BT2020_CL || frame->color_trc == AVCOL_TRC_SMPTE2084 ||
        frame->color_trc == AVCOL_TRC_ARIB_STD_B67) { owner->error = E_NOTIMPL; return nullptr; }
    auto* input = reinterpret_cast<AVD3D12VAFrame*>(frame->data[0]);
    auto* target = static_cast<ID3D12Resource*>(unityTexture);
    if (!input->texture || !input->sync_ctx.fence) { owner->error = E_INVALIDARG; return nullptr; }
    const auto sourceDesc = input->texture->GetDesc();
    auto targetDesc = target->GetDesc();
    if (sourceDesc.Dimension != D3D12_RESOURCE_DIMENSION_TEXTURE2D || sourceDesc.MipLevels != 1 ||
        (sourceDesc.Format != DXGI_FORMAT_NV12 && sourceDesc.Format != DXGI_FORMAT_P010) ||
        !(sourceDesc.Flags & D3D12_RESOURCE_FLAG_ALLOW_SIMULTANEOUS_ACCESS) ||
        sourceDesc.Width < static_cast<UINT64>(frame->width) || sourceDesc.Height < static_cast<UINT>(frame->height) ||
        input->subresource_index < 0 || input->subresource_index >= sourceDesc.DepthOrArraySize ||
        targetDesc.Dimension != D3D12_RESOURCE_DIMENSION_TEXTURE2D || targetDesc.SampleDesc.Count != 1 ||
        targetDesc.DepthOrArraySize != 1 || targetDesc.MipLevels != 1 ||
        targetDesc.Width != static_cast<UINT64>(frame->width) || targetDesc.Height != static_cast<UINT>(frame->height)) {
        owner->error = E_INVALIDARG; return nullptr;
    }
    if (targetDesc.Format == DXGI_FORMAT_R8G8B8A8_TYPELESS || targetDesc.Format == DXGI_FORMAT_R8G8B8A8_UNORM_SRGB)
        targetDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    if (targetDesc.Format == DXGI_FORMAT_B8G8R8A8_TYPELESS || targetDesc.Format == DXGI_FORMAT_B8G8R8A8_UNORM_SRGB)
        targetDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    if (targetDesc.Format != DXGI_FORMAT_R8G8B8A8_UNORM && targetDesc.Format != DXGI_FORMAT_B8G8R8A8_UNORM) {
        owner->error = E_NOTIMPL; return nullptr;
    }
    std::lock_guard<std::mutex> lock(stateMutex);
    if (!device12 || !video12 || !processQueue || !processed12 || !completed12) { owner->error = E_FAIL; return nullptr; }
    ID3D12Device* sourceDevice = nullptr;
    ID3D12Device* targetDevice = nullptr;
    HRESULT result = input->texture->GetDevice(IID_PPV_ARGS(&sourceDevice));
    if (SUCCEEDED(result)) result = target->GetDevice(IID_PPV_ARGS(&targetDevice));
    const bool sameDevice = sourceDevice == device12 && targetDevice == device12;
    Drop(sourceDevice); Drop(targetDevice);
    if (FAILED(result) || !sameDevice) { owner->error = E_INVALIDARG; return nullptr; }
    if (nativeInFlight.fetch_add(1) >= 24) { --nativeInFlight; owner->error = E_OUTOFMEMORY; return nullptr; }
    auto* packet = new (std::nothrow) NativeD3D12Packet();
    if (!packet) { --nativeInFlight; owner->error = E_OUTOFMEMORY; return nullptr; }
    packet->owner = owner; owner->Retain();
    packet->frame = av_frame_clone(frame);
    if (!packet->frame) { owner->error = E_OUTOFMEMORY; delete packet; return nullptr; }
    packet->device = device12; device12->AddRef();
    packet->target = target; target->AddRef();
    packet->complete = completed12; completed12->AddRef();
    const bool matrix709 = frame->colorspace == AVCOL_SPC_BT709 ||
        (frame->colorspace == AVCOL_SPC_UNSPECIFIED && frame->height >= 720);
    const bool fullRange = frame->color_range == AVCOL_RANGE_JPEG;
    const DXGI_COLOR_SPACE_TYPE inputColor = matrix709
        ? (fullRange ? DXGI_COLOR_SPACE_YCBCR_FULL_G22_LEFT_P709 : DXGI_COLOR_SPACE_YCBCR_STUDIO_G22_LEFT_P709)
        : (fullRange ? DXGI_COLOR_SPACE_YCBCR_FULL_G22_LEFT_P601 : DXGI_COLOR_SPACE_YCBCR_STUDIO_G22_LEFT_P601);
    D3D12_FEATURE_DATA_VIDEO_PROCESS_SUPPORT support{};
    support.InputSample = {static_cast<UINT>(sourceDesc.Width), sourceDesc.Height, {sourceDesc.Format, inputColor}};
    support.InputFrameRate = support.OutputFrameRate = {30, 1};
    support.OutputFormat = {targetDesc.Format, DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709};
    result = video12->CheckFeatureSupport(D3D12_FEATURE_VIDEO_PROCESS_SUPPORT, &support, sizeof(support));
    if (SUCCEEDED(result) && !(support.SupportFlags & D3D12_VIDEO_PROCESS_SUPPORT_FLAG_SUPPORTED)) result = E_NOTIMPL;
    D3D12_VIDEO_PROCESS_INPUT_STREAM_DESC inputDesc{};
    inputDesc.Format = sourceDesc.Format;
    inputDesc.ColorSpace = inputColor;
    inputDesc.SourceAspectRatio = inputDesc.DestinationAspectRatio = {1, 1};
    inputDesc.FrameRate = {30, 1};
    inputDesc.SourceSizeRange = {static_cast<UINT>(frame->width), static_cast<UINT>(frame->height),
                               static_cast<UINT>(frame->width), static_cast<UINT>(frame->height)};
    inputDesc.DestinationSizeRange = {static_cast<UINT>(frame->width), static_cast<UINT>(frame->height),
                                    static_cast<UINT>(frame->width), static_cast<UINT>(frame->height)};
    D3D12_VIDEO_PROCESS_OUTPUT_STREAM_DESC outputDesc{};
    outputDesc.Format = targetDesc.Format;
    outputDesc.ColorSpace = DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709;
    outputDesc.AlphaFillMode = D3D12_VIDEO_PROCESS_ALPHA_FILL_MODE_OPAQUE;
    outputDesc.BackgroundColor[3] = 1;
    outputDesc.FrameRate = {30, 1};
    if (SUCCEEDED(result)) result = video12->CreateVideoProcessor(0, &outputDesc, 1, &inputDesc, IID_PPV_ARGS(&packet->processor));
    D3D12_HEAP_PROPERTIES heap{}; heap.Type = D3D12_HEAP_TYPE_DEFAULT;
    targetDesc.Flags = D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET;
    targetDesc.Layout = D3D12_TEXTURE_LAYOUT_UNKNOWN;
    if (SUCCEEDED(result)) result = device12->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_NONE, &targetDesc,
        D3D12_RESOURCE_STATE_COMMON, nullptr, IID_PPV_ARGS(&packet->rgba));
    if (SUCCEEDED(result)) result = device12->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_VIDEO_PROCESS, IID_PPV_ARGS(&packet->processAllocator));
    if (SUCCEEDED(result)) result = device12->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_VIDEO_PROCESS,
        packet->processAllocator, nullptr, IID_PPV_ARGS(&packet->processCommand));
    if (SUCCEEDED(result)) result = device12->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&packet->copyAllocator));
    if (SUCCEEDED(result)) result = device12->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT,
        packet->copyAllocator, nullptr, IID_PPV_ARGS(&packet->copyCommand));
    if (FAILED(result)) { owner->error = result; delete packet; return nullptr; }
    // Only this array slice's two planes; other slices may be decoded in parallel.
    const UINT slice = static_cast<UINT>(input->subresource_index);
    D3D12_RESOURCE_BARRIER barriers[] = {
        Transition(input->texture, D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_VIDEO_PROCESS_READ, slice),
        Transition(input->texture, D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_VIDEO_PROCESS_READ, slice + sourceDesc.DepthOrArraySize),
        Transition(packet->rgba, D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_VIDEO_PROCESS_WRITE)
    };
    packet->processCommand->ResourceBarrier(3, barriers);
    D3D12_VIDEO_PROCESS_INPUT_STREAM_ARGUMENTS inputArgs{};
    inputArgs.InputStream[0].pTexture2D = input->texture;
    inputArgs.InputStream[0].Subresource = slice;
    inputArgs.Transform.SourceRectangle = {0, 0, frame->width, frame->height};
    inputArgs.Transform.DestinationRectangle = {0, 0, frame->width, frame->height};
    inputArgs.AlphaBlending.Alpha = 1;
    D3D12_VIDEO_PROCESS_OUTPUT_STREAM_ARGUMENTS outputArgs{};
    outputArgs.OutputStream[0].pTexture2D = packet->rgba;
    outputArgs.TargetRectangle = {0, 0, frame->width, frame->height};
    packet->processCommand->ProcessFrames(packet->processor, &outputArgs, 1, &inputArgs);
    for (auto& barrier : barriers) {
        barrier.Transition.StateBefore = barrier.Transition.StateAfter;
        barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_COMMON;
    }
    packet->processCommand->ResourceBarrier(3, barriers);
    result = packet->processCommand->Close();
    auto copyBarrier = Transition(packet->rgba, D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_COPY_SOURCE);
    packet->copyCommand->ResourceBarrier(1, &copyBarrier);
    D3D12_TEXTURE_COPY_LOCATION source{}; source.pResource = packet->rgba;
    D3D12_TEXTURE_COPY_LOCATION destination{}; destination.pResource = packet->target;
    packet->copyCommand->CopyTextureRegion(&destination, 0, 0, 0, &source, nullptr);
    if (SUCCEEDED(result)) result = packet->copyCommand->Close();
    if (FAILED(result)) { owner->error = result; delete packet; return nullptr; }
    return packet;
}

static void RenderNative(int event, void* data) {
    auto* packet = static_cast<NativeD3D12Packet*>(data);
    if (!packet) return;
    auto* owner = packet->owner;
    if (event == FfuPrepareNativeD3D12) {
        if (unity12 && packet->device == device12) {
            unity12->RequestResourceState(packet->target, D3D12_RESOURCE_STATE_COPY_DEST);
            packet->statePrepared = true;
        }
        return;
    }
    PollNative(false);
    if (!packet->statePrepared || !unity12 || packet->device != device12 || !processQueue) {
        owner->error = E_FAIL; delete packet; return;
    }
    auto* queue = unity12->GetCommandQueue();
    if (!queue) { owner->error = E_FAIL; delete packet; return; }
    auto* frame = reinterpret_cast<AVD3D12VAFrame*>(packet->frame->data[0]);
    HRESULT result = processQueue->Wait(frame->sync_ctx.fence, frame->sync_ctx.fence_value);
    if (FAILED(result)) { owner->error = result; delete packet; return; }
    ID3D12CommandList* conversion[] = {packet->processCommand};
    processQueue->ExecuteCommandLists(1, conversion);
    const UINT64 value = ++sequence;
    result = processQueue->Signal(processed12, value);
    if (SUCCEEDED(result)) result = queue->Wait(processed12, value);
    if (SUCCEEDED(result)) {
        ID3D12CommandList* commands[] = {packet->copyCommand};
        queue->ExecuteCommandLists(1, commands);
        result = queue->Signal(completed12, value);
        if (SUCCEEDED(result)) packet->value = value;
    }
    if (FAILED(result)) owner->error = result;
    // Keep target COPY_DEST for Unity; retain the AVFrame and all commands until
    // conversion and copy both finish. Never change FFmpeg's decoder fence value.
    nativePending.push_back(packet);
}

FFU_EXPORT void FFU_CALL ffu_d3d12va_cancel(void* packet) { delete static_cast<NativeD3D12Packet*>(packet); }
