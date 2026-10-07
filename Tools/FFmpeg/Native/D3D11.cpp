#include <d3d11.h>
#include <d3d11_1.h>
#include <d3d10.h>
#include "Bridge.h"
#include "IUnityGraphicsD3D11.h"
#include "D3D12.h"
#include "WglInterop.h"
#include "VulkanInterop.h"
#include "VulkanPortable.h"
#include "VulkanVideoDecode.h"
#include <mutex>
#include <array>
#include <memory>
#include <vector>
#include <new>
#include <thread>
#include <chrono>
extern "C" {
#include <libavutil/hwcontext.h>
#include <libavutil/hwcontext_d3d11va.h>
}

template<class T> static void Release(T*& value) { if (value) { value->Release(); value = nullptr; } }
static std::mutex deviceMutex;
static ID3D11Device* unityDevice = nullptr;
static UnityGfxRenderer activeRenderer = kUnityGfxRendererNull;
static std::atomic<int> inFlight{0};
static std::atomic<int> initializationStatus{3};

struct Presenter {
    std::atomic<int> references{1};
    std::atomic<int> error{0};
    ID3D11Device* device = nullptr;
    ID3D11DeviceContext* immediate = nullptr;
    ID3D11VideoDevice* videoDevice = nullptr;
    ID3D11VideoContext* videoContext = nullptr;
    std::mutex prepareMutex;
    ID3D11VideoProcessorEnumerator* enumerator = nullptr;
    ID3D11VideoProcessor* processor = nullptr;
    ID3D11VideoProcessorOutputView* output = nullptr;
    ID3D11Texture2D* outputTarget = nullptr;
    UINT width = 0, height = 0;
    DXGI_FORMAT inputFormat = DXGI_FORMAT_UNKNOWN, outputFormat = DXGI_FORMAT_UNKNOWN;
    void Retain() { ++references; }
    void Drop() { if (--references == 0) delete this; }
    ~Presenter() {
        Release(output); Release(outputTarget); Release(processor); Release(enumerator);
        Release(videoContext); Release(videoDevice); Release(immediate); Release(device);
    }

    // Called under prepareMutex. Packets retain their own references, so a resize
    // can replace the cache while earlier submissions are still using it.
    HRESULT EnsureProcessor(UINT nextWidth, UINT nextHeight, DXGI_FORMAT nextInput, DXGI_FORMAT nextOutput) {
        if (enumerator && width == nextWidth && height == nextHeight &&
            inputFormat == nextInput && outputFormat == nextOutput) return S_OK;
        D3D11_VIDEO_PROCESSOR_CONTENT_DESC desc{};
        desc.InputFrameFormat = D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE;
        desc.InputWidth = desc.OutputWidth = nextWidth;
        desc.InputHeight = desc.OutputHeight = nextHeight;
        desc.InputFrameRate = desc.OutputFrameRate = {30, 1};
        desc.Usage = D3D11_VIDEO_USAGE_PLAYBACK_NORMAL;
        ID3D11VideoProcessorEnumerator* nextEnumerator = nullptr;
        ID3D11VideoProcessor* nextProcessor = nullptr;
        HRESULT result = videoDevice->CreateVideoProcessorEnumerator(&desc, &nextEnumerator);
        UINT inputSupport = 0, outputSupport = 0;
        if (SUCCEEDED(result)) result = nextEnumerator->CheckVideoProcessorFormat(nextInput, &inputSupport);
        if (SUCCEEDED(result)) result = nextEnumerator->CheckVideoProcessorFormat(nextOutput, &outputSupport);
        if (SUCCEEDED(result) && (!(inputSupport & D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_INPUT) ||
                                  !(outputSupport & D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_OUTPUT))) result = E_NOTIMPL;
        if (SUCCEEDED(result)) result = videoDevice->CreateVideoProcessor(nextEnumerator, 0, &nextProcessor);
        if (FAILED(result)) { Release(nextProcessor); Release(nextEnumerator); return result; }
        Release(output); Release(outputTarget);
        Release(processor); Release(enumerator);
        enumerator = nextEnumerator; processor = nextProcessor;
        width = nextWidth; height = nextHeight; inputFormat = nextInput; outputFormat = nextOutput;
        return S_OK;
    }

    HRESULT EnsureOutput(ID3D11Texture2D* target) {
        if (output && outputTarget == target) return S_OK;
        D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC desc{};
        desc.ViewDimension = D3D11_VPOV_DIMENSION_TEXTURE2D;
        ID3D11VideoProcessorOutputView* nextOutput = nullptr;
        HRESULT result = videoDevice->CreateVideoProcessorOutputView(target, enumerator, &desc, &nextOutput);
        if (FAILED(result)) { Release(nextOutput); return result; }
        Release(output); Release(outputTarget);
        output = nextOutput; outputTarget = target; target->AddRef();
        return S_OK;
    }
};

struct Packet {
    bool counted = false;
    Presenter* owner = nullptr;
    AVFrame* frame = nullptr;
    ID3D11Texture2D* target = nullptr;
    ID3D11VideoProcessorEnumerator* enumerator = nullptr;
    ID3D11VideoProcessor* processor = nullptr;
    ID3D11VideoProcessorInputView* input = nullptr;
    ID3D11VideoProcessorOutputView* output = nullptr;
    ID3D11Query* complete = nullptr;
    void* interopSurface = nullptr;
    void* unityTarget = nullptr;
    ~Packet() {
        Release(complete); Release(output); Release(input); Release(processor); Release(enumerator); Release(target);
        av_frame_free(&frame);
        if (owner) owner->Drop();
        if (counted) --inFlight;
    }
};
// Accessed only on the Unity render thread (or during synchronized plug-in unload).
static std::vector<Packet*> pending;
struct WglRetirement { FfuWglSurface* surface; std::atomic<bool> complete{false}; };
static std::vector<WglRetirement*> retiringWgl;

static void Retire(bool drain) {
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
    for (auto it = pending.begin(); it != pending.end();) {
        Packet* packet = *it;
        if (drain) packet->owner->immediate->Flush();
        HRESULT result;
        do {
            result = packet->owner->immediate->GetData(packet->complete, nullptr, 0, D3D11_ASYNC_GETDATA_DONOTFLUSH);
            if (result != S_FALSE || !drain) break;
            if (FAILED(packet->owner->device->GetDeviceRemovedReason())) { result = E_FAIL; break; }
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
        } while (std::chrono::steady_clock::now() < deadline);
        if (result == S_OK || FAILED(packet->owner->device->GetDeviceRemovedReason())) {
            delete packet;
            it = pending.erase(it);
        } else {
            if (FAILED(result)) packet->owner->error.store(result);
            // Never recycle an FFmpeg array slice still in use by the GPU.
            // A hung but not-yet-removed driver may retain the final packet until
            // the next event; shutdown prefers a leak over GPU use-after-free.
            ++it;
        }
    }
}

void FfuPlatformInitialize(IUnityInterfaces* interfaces, UnityGfxRenderer renderer) {
    std::lock_guard<std::mutex> guard(deviceMutex);
    Release(unityDevice);
    activeRenderer = renderer;
    initializationStatus = 3;
    if (renderer == kUnityGfxRendererD3D12) {
        unityDevice = FfuD3D12Initialize(interfaces);
        initializationStatus = FfuD3D12Status();
        return;
    }
    if (renderer == kUnityGfxRendererVulkan) {
        // Native Vulkan Video uses Unity's device independently of D3D11 and
        // the external-memory extensions required by the legacy interop path.
        FfuVkInitialize(interfaces);
        unityDevice = FfuVulkanInitialize(interfaces);
        initializationStatus = unityDevice || FfuVulkanVideoAvailable() ? 0 : FfuVulkanError(nullptr);
        return;
    }
    if (renderer == kUnityGfxRendererOpenGLCore) {
        ID3D11DeviceContext* immediate = nullptr;
        HRESULT result = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
            D3D11_CREATE_DEVICE_VIDEO_SUPPORT, nullptr, 0, D3D11_SDK_VERSION, &unityDevice, nullptr, &immediate);
        ID3D10Multithread* threading = nullptr;
        if (SUCCEEDED(result)) result = immediate->QueryInterface(IID_PPV_ARGS(&threading));
        if (SUCCEEDED(result)) threading->SetMultithreadProtected(TRUE);
        Release(threading); Release(immediate);
        if (FAILED(result)) Release(unityDevice);
        initializationStatus = result;
        return; // WGL registration validates actual context/adapter on the render thread.
    }
    if (renderer != kUnityGfxRendererD3D11) return;
    auto* graphics = FfuGetInterface<IUnityGraphicsD3D11>(interfaces);
    initializationStatus = 4;
    if (!graphics) return;
    ID3D11Device* device = graphics->GetDevice();
    ID3D11VideoDevice* video = nullptr;
    ID3D11DeviceContext* context = nullptr;
    ID3D10Multithread* threading = nullptr;
    initializationStatus = 5;
    if (!device) return;
    HRESULT result = device->QueryInterface(__uuidof(ID3D11VideoDevice), reinterpret_cast<void**>(&video));
    initializationStatus = static_cast<int>(result);
    if (FAILED(result)) return;
    Release(video);
    device->GetImmediateContext(&context);
    // FFmpeg decodes on a worker while Unity submits from the render thread.
    // FFmpeg's private hwcontext lock alone cannot synchronize Unity accesses.
    initializationStatus = 6;
    if (context && SUCCEEDED(context->QueryInterface(__uuidof(ID3D10Multithread), reinterpret_cast<void**>(&threading)))) {
        threading->SetMultithreadProtected(TRUE);
        device->AddRef();
        unityDevice = device;
        initializationStatus = 0;
    }
    Release(threading);
    Release(context);
}
void FfuPlatformShutdown() {
    Retire(true);
    FfuD3D12Shutdown();
    FfuVulkanShutdown();
    FfuVkShutdown();
    std::lock_guard<std::mutex> guard(deviceMutex);
    Release(unityDevice);
}
int FfuPlatformCapabilities() {
    std::lock_guard<std::mutex> guard(deviceMutex);
    switch (activeRenderer) {
        case kUnityGfxRendererD3D11: return unityDevice ? FfuD3D11GpuConversion | (FfuD3D11DecodeIsolation | FfuD3D11QueuedSnapshots) : 0;
        case kUnityGfxRendererD3D12: return FfuD3D12Capabilities() | (unityDevice ? (FfuD3D11DecodeIsolation | FfuD3D11QueuedSnapshots) : 0);
        case kUnityGfxRendererOpenGLCore: return unityDevice ? FfuWglGpuInterop : 0;
        case kUnityGfxRendererVulkan: return (unityDevice ? FfuVulkanGpuCopy | (FfuD3D11DecodeIsolation | FfuD3D11QueuedSnapshots) : 0) |
            (FfuVulkanVideoAvailable() ? FfuVulkanVideoDecode : 0);
        default: return 0;
    }
}
int FfuPlatformStatus() { return initializationStatus.load(); }
FFU_EXPORT void* FFU_CALL ffu_d3d11_acquire_device() {
    std::lock_guard<std::mutex> guard(deviceMutex);
    if (unityDevice) unityDevice->AddRef();
    return unityDevice;
}
// Keep the codec's immediate context out of Unity and the cross-API presenter.
// The adapter stays unchanged; this is still the same D3D11VA decoder backend.
FFU_EXPORT void* FFU_CALL ffu_d3d11_acquire_decode_device() {
    auto* presentation = static_cast<ID3D11Device*>(ffu_d3d11_acquire_device());
    if (!presentation) return nullptr;
    IDXGIDevice* dxgi = nullptr;
    IDXGIAdapter* adapter = nullptr;
    ID3D11Device* device = nullptr;
    ID3D11DeviceContext* immediate = nullptr;
    ID3D10Multithread* threading = nullptr;
    HRESULT result = presentation->QueryInterface(IID_PPV_ARGS(&dxgi));
    if (SUCCEEDED(result)) result = dxgi->GetAdapter(&adapter);
    if (SUCCEEDED(result)) result = D3D11CreateDevice(adapter, D3D_DRIVER_TYPE_UNKNOWN, nullptr,
        D3D11_CREATE_DEVICE_VIDEO_SUPPORT, nullptr, 0, D3D11_SDK_VERSION, &device, nullptr, &immediate);
    if (SUCCEEDED(result)) result = immediate->QueryInterface(IID_PPV_ARGS(&threading));
    if (SUCCEEDED(result)) threading->SetMultithreadProtected(TRUE);
    Release(threading); Release(immediate); Release(adapter); Release(dxgi); Release(presentation);
    if (FAILED(result)) Release(device);
    return device;
}
FFU_EXPORT void* FFU_CALL ffu_d3d11_create() {
    auto* presenter = new (std::nothrow) Presenter();
    if (!presenter) return nullptr;
    presenter->device = static_cast<ID3D11Device*>(ffu_d3d11_acquire_device());
    if (!presenter->device) { delete presenter; return nullptr; }
    presenter->device->GetImmediateContext(&presenter->immediate);
    if (FAILED(presenter->device->QueryInterface(__uuidof(ID3D11VideoDevice), reinterpret_cast<void**>(&presenter->videoDevice))) ||
        FAILED(presenter->immediate->QueryInterface(__uuidof(ID3D11VideoContext), reinterpret_cast<void**>(&presenter->videoContext)))) {
        delete presenter;
        return nullptr;
    }
    return presenter;
}
FFU_EXPORT void FFU_CALL ffu_d3d11_release(void* value) { if (value) static_cast<Presenter*>(value)->Drop(); }
FFU_EXPORT int FFU_CALL ffu_d3d11_error(void* value) { return value ? static_cast<Presenter*>(value)->error.load() : -1; }

// FFmpeg submits asynchronously and may block inside DecoderBeginFrame or
// GetDecoderBuffer if the worker outruns the GPU. Pace codec calls on the worker
// with an explicit completion query, before those driver waits can reach Unity's
// render-thread conversion or shared immediate-context submissions.
// An immutable, completed snapshot replaces the live codec/DPB surface at the
// worker/presenter boundary. Reuse waits for every AVFrame clone, including GPU
// retirement packets, to return its lease. There is no CPU pixel transfer.
struct PresentationSnapshot {
    ID3D11Texture2D* writer = nullptr;
    ID3D11Texture2D* reader = nullptr;
    ID3D11Query* complete = nullptr;
    AVFrame* source = nullptr;
    bool ready = true;
    std::atomic<bool> borrowed{false};
    UINT width = 0, height = 0;
    DXGI_FORMAT format = DXGI_FORMAT_UNKNOWN;
    ~PresentationSnapshot() { av_frame_free(&source); Release(complete); Release(reader); Release(writer); }
    void Complete() { av_frame_free(&source); ready = true; }
};
struct SnapshotLease {
    std::shared_ptr<PresentationSnapshot> slot;
    AVD3D11FrameDescriptor descriptor{};
};
static void ReturnSnapshot(void* opaque, uint8_t*) {
    auto* lease = static_cast<SnapshotLease*>(opaque);
    lease->slot->borrowed.store(false, std::memory_order_release);
    delete lease;
}
struct DecodeSynchronization {
    AVBufferRef* deviceReference = nullptr;
    AVD3D11VADeviceContext* device = nullptr;
    ID3D11Query* complete = nullptr;
    ID3D11Device1* presentation = nullptr;
    AVBufferRef* presentationFrames = nullptr;
    std::array<std::shared_ptr<PresentationSnapshot>, 32> snapshots{};
    bool started = false;
    ~DecodeSynchronization() {
        av_buffer_unref(&presentationFrames); Release(presentation);
        Release(complete); av_buffer_unref(&deviceReference);
    }
};
FFU_EXPORT void* FFU_CALL ffu_d3d11_decode_sync_create(void* hardwareDevice) {
    auto* reference = static_cast<AVBufferRef*>(hardwareDevice);
    if (!reference || !reference->data) return nullptr;
    auto* context = reinterpret_cast<AVHWDeviceContext*>(reference->data);
    if (context->type != AV_HWDEVICE_TYPE_D3D11VA || !context->hwctx) return nullptr;
    auto* device = static_cast<AVD3D11VADeviceContext*>(context->hwctx);
    if (!device->device || !device->device_context) return nullptr;
    auto* sync = new (std::nothrow) DecodeSynchronization();
    if (!sync) return nullptr;
    sync->deviceReference = av_buffer_ref(reference);
    sync->device = device;
    D3D11_QUERY_DESC query{}; query.Query = D3D11_QUERY_EVENT;
    if (!sync->deviceReference || FAILED(device->device->CreateQuery(&query, &sync->complete))) {
        delete sync; return nullptr;
    }
    return sync;
}
// Returns 1 with an owned frame after submitting its snapshot copy, 0 for bounded
// pool backpressure, or a negative error. The worker MUST complete its query
// before publishing the frame; render callbacks never wait for decoding.
FFU_EXPORT int FFU_CALL ffu_d3d11_decode_snapshot(void* value, const AVFrame* source, AVFrame** output) {
    auto* sync = static_cast<DecodeSynchronization*>(value);
    if (output) *output = nullptr;
    if (!sync || !source || !output || source->format != AV_PIX_FMT_D3D11 || !source->data[0]) return E_INVALIDARG;
    auto* presentation = static_cast<ID3D11Device*>(ffu_d3d11_acquire_device());
    if (!presentation && sync->presentation) return DXGI_ERROR_DEVICE_REMOVED;
    if (sync->presentation && static_cast<ID3D11Device*>(sync->presentation) != presentation) {
        Release(presentation); return DXGI_ERROR_DEVICE_REMOVED;
    }
    // Standalone decoder clients with no Unity device continue to own raw frames.
    const bool needsCopy = presentation && presentation != sync->device->device;
    if (!needsCopy) {
        Release(presentation);
        *output = av_frame_clone(source);
        return *output ? 1 : E_OUTOFMEMORY;
    }
    HRESULT result = S_OK;
    if (!sync->presentation) result = presentation->QueryInterface(IID_PPV_ARGS(&sync->presentation));
    Release(presentation);
    if (FAILED(result)) return result;
    auto* texture = reinterpret_cast<ID3D11Texture2D*>(source->data[0]);
    ID3D11Device* sourceDevice = nullptr;
    texture->GetDevice(&sourceDevice);
    const bool sameDevice = sourceDevice == sync->device->device;
    Release(sourceDevice);
    if (!sameDevice) return E_INVALIDARG;
    D3D11_TEXTURE2D_DESC desc{};
    texture->GetDesc(&desc);
    const auto slice = reinterpret_cast<uintptr_t>(source->data[1]);
    if (slice >= desc.ArraySize || desc.MipLevels != 1 || desc.SampleDesc.Count != 1 ||
        (desc.Format != DXGI_FORMAT_NV12 && desc.Format != DXGI_FORMAT_P010)) return E_INVALIDARG;
    // A READER-device FFmpeg context is essential: reusing the codec's context
    // for a foreign texture would make av_hwframe_transfer_data unsafe.
    auto* frames = sync->presentationFrames ? reinterpret_cast<AVHWFramesContext*>(sync->presentationFrames->data) : nullptr;
    const auto swFormat = desc.Format == DXGI_FORMAT_NV12 ? AV_PIX_FMT_NV12 : AV_PIX_FMT_P010;
    if (!frames || frames->width != static_cast<int>(desc.Width) || frames->height != static_cast<int>(desc.Height) || frames->sw_format != swFormat) {
        AVBufferRef* deviceReference = av_hwdevice_ctx_alloc(AV_HWDEVICE_TYPE_D3D11VA);
        if (!deviceReference) return E_OUTOFMEMORY;
        auto* hardware = reinterpret_cast<AVHWDeviceContext*>(deviceReference->data);
        auto* d3d11 = static_cast<AVD3D11VADeviceContext*>(hardware->hwctx);
        d3d11->device = sync->presentation; d3d11->device->AddRef();
        int status = av_hwdevice_ctx_init(deviceReference);
        AVBufferRef* next = status >= 0 ? av_hwframe_ctx_alloc(deviceReference) : nullptr;
        av_buffer_unref(&deviceReference);
        if (!next) return status < 0 ? status : E_OUTOFMEMORY;
        auto* nextFrames = reinterpret_cast<AVHWFramesContext*>(next->data);
        nextFrames->format = AV_PIX_FMT_D3D11; nextFrames->sw_format = swFormat;
        nextFrames->width = desc.Width; nextFrames->height = desc.Height;
        status = av_hwframe_ctx_init(next);
        if (status < 0) { av_buffer_unref(&next); return status; }
        av_buffer_unref(&sync->presentationFrames);
        sync->presentationFrames = next;
    }
    std::shared_ptr<PresentationSnapshot> slot;
    for (auto& cached : sync->snapshots) {
        if (cached && cached->borrowed.load(std::memory_order_acquire)) continue;
        if (cached && !cached->ready) {
            const HRESULT status = sync->device->device_context->GetData(cached->complete, nullptr, 0, D3D11_ASYNC_GETDATA_DONOTFLUSH);
            if (FAILED(status)) return status;
            if (status != S_OK) continue;
            cached->Complete();
        }
        if (!cached || cached->width != desc.Width || cached->height != desc.Height || cached->format != desc.Format) {
            std::shared_ptr<PresentationSnapshot> next;
            try { next = std::make_shared<PresentationSnapshot>(); }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
            D3D11_TEXTURE2D_DESC shared = desc;
            shared.ArraySize = shared.MipLevels = 1;
            shared.Usage = D3D11_USAGE_DEFAULT; shared.CPUAccessFlags = 0;
            shared.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
            shared.MiscFlags = D3D11_RESOURCE_MISC_SHARED;
            result = sync->device->device->CreateTexture2D(&shared, nullptr, &next->writer);
            IDXGIResource* resource = nullptr;
            HANDLE handle = nullptr;
            if (SUCCEEDED(result)) result = next->writer->QueryInterface(IID_PPV_ARGS(&resource));
            if (SUCCEEDED(result)) result = resource->GetSharedHandle(&handle);
            if (SUCCEEDED(result)) result = sync->presentation->OpenSharedResource(handle, IID_PPV_ARGS(&next->reader));
            // GetSharedHandle returns a DXGI handle, not an owned NT handle.
            Release(resource);
            D3D11_QUERY_DESC query{}; query.Query = D3D11_QUERY_EVENT;
            if (SUCCEEDED(result)) result = sync->device->device->CreateQuery(&query, &next->complete);
            if (FAILED(result)) return result;
            next->width = desc.Width; next->height = desc.Height; next->format = desc.Format;
            cached = std::move(next);
        }
        slot = cached;
        break;
    }
    if (!slot) return 0;
    AVFrame* frame = av_frame_alloc();
    auto* lease = new (std::nothrow) SnapshotLease{slot};
    if (!frame || !lease) { av_frame_free(&frame); delete lease; return E_OUTOFMEMORY; }
    lease->descriptor.texture = slot->reader;
    lease->descriptor.index = 0;
    frame->buf[0] = av_buffer_create(reinterpret_cast<uint8_t*>(&lease->descriptor), sizeof(lease->descriptor), ReturnSnapshot, lease, AV_BUFFER_FLAG_READONLY);
    if (!frame->buf[0]) { delete lease; av_frame_free(&frame); return E_OUTOFMEMORY; }
    slot->borrowed.store(true, std::memory_order_release);
    frame->hw_frames_ctx = av_buffer_ref(sync->presentationFrames);
    const int status = av_frame_copy_props(frame, source);
    if (!frame->hw_frames_ctx || status < 0) { av_frame_free(&frame); return status < 0 ? status : E_OUTOFMEMORY; }
    frame->format = source->format; frame->width = source->width; frame->height = source->height;
    frame->data[0] = reinterpret_cast<uint8_t*>(slot->reader);
    frame->data[1] = nullptr; // A snapshot has one slice, not the codec's array index.
    // The copy can remain pending while the same worker feeds bounded later
    // codec packets. Retain its exact source slice until actual GPU completion.
    slot->source = av_frame_clone(source);
    if (!slot->source) { av_frame_free(&frame); return E_OUTOFMEMORY; }
    slot->ready = false;
    if (sync->device->lock) sync->device->lock(sync->device->lock_ctx);
    sync->device->device_context->CopySubresourceRegion(slot->writer, 0, 0, 0, 0, texture, static_cast<UINT>(slice), nullptr);
    sync->device->device_context->End(slot->complete);
    sync->device->device_context->Flush();
    if (sync->device->unlock) sync->device->unlock(sync->device->lock_ctx);
    *output = frame;
    return 1;
}
// Worker-only, nonblocking completion check. A packet must not reach Unity
// until this returns 1; unrelated later codec/DPB work is not waited here.
FFU_EXPORT int FFU_CALL ffu_d3d11_decode_snapshot_ready(void* value, const AVFrame* frame) {
    auto* sync = static_cast<DecodeSynchronization*>(value);
    if (!sync || !frame || frame->format != AV_PIX_FMT_D3D11 || !frame->data[0] || frame->data[1]) return E_INVALIDARG;
    const HRESULT removed = sync->device->device->GetDeviceRemovedReason();
    if (FAILED(removed)) return removed;
    for (auto& slot : sync->snapshots) {
        if (!slot || reinterpret_cast<uint8_t*>(slot->reader) != frame->data[0] || !slot->borrowed.load(std::memory_order_acquire)) continue;
        if (slot->ready) return 1;
        const HRESULT result = sync->device->device_context->GetData(slot->complete, nullptr, 0, D3D11_ASYNC_GETDATA_DONOTFLUSH);
        if (FAILED(result)) return result;
        if (result != S_OK) return 0;
        slot->Complete();
        return 1;
    }
    return E_INVALIDARG;
}
FFU_EXPORT int FFU_CALL ffu_d3d11_decode_sync_begin(void* value) {
    auto* sync = static_cast<DecodeSynchronization*>(value);
    if (!sync) return E_INVALIDARG;
    if (sync->device->lock) sync->device->lock(sync->device->lock_ctx);
    sync->device->device_context->End(sync->complete);
    sync->device->device_context->Flush();
    if (sync->device->unlock) sync->device->unlock(sync->device->lock_ctx);
    sync->started = true;
    return sync->device->device->GetDeviceRemovedReason();
}
FFU_EXPORT int FFU_CALL ffu_d3d11_decode_sync_poll(void* value) {
    auto* sync = static_cast<DecodeSynchronization*>(value);
    if (!sync || !sync->started) return E_INVALIDARG;
    const HRESULT removed = sync->device->device->GetDeviceRemovedReason();
    if (FAILED(removed)) return removed;
    const HRESULT result = sync->device->device_context->GetData(sync->complete, nullptr, 0, D3D11_ASYNC_GETDATA_DONOTFLUSH);
    if (FAILED(result)) return result;
    if (result == S_OK) {
        // This marker was submitted after every outstanding snapshot on the
        // owning worker. Legacy synchronous clients can release their sources too.
        for (auto& slot : sync->snapshots) if (slot && !slot->ready) slot->Complete();
    }
    return result == S_OK ? 1 : 0;
}
FFU_EXPORT void FFU_CALL ffu_d3d11_decode_sync_release(void* value) { delete static_cast<DecodeSynchronization*>(value); }

void FfuD3D11RetainPresenter(void* value) { if (value) static_cast<Presenter*>(value)->Retain(); }
void FfuD3D11SetError(void* value, int error) { if (value) static_cast<Presenter*>(value)->error.store(error); }

FFU_EXPORT void* FFU_CALL ffu_d3d11_create_output(void* value, int width, int height) {
    auto* presenter = static_cast<Presenter*>(value);
    if (!presenter || width <= 0 || height <= 0) return nullptr;
    D3D11_TEXTURE2D_DESC desc{};
    desc.Width = width;
    desc.Height = height;
    desc.MipLevels = desc.ArraySize = 1;
    desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
    if (activeRenderer == kUnityGfxRendererVulkan)
        desc.MiscFlags = D3D11_RESOURCE_MISC_SHARED_NTHANDLE | D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX;
    ID3D11Texture2D* output = nullptr;
    return SUCCEEDED(presenter->device->CreateTexture2D(&desc, nullptr, &output)) ? output : nullptr;
}
FFU_EXPORT void FFU_CALL ffu_d3d11_release_output(void* value) {
    auto* texture = static_cast<ID3D11Texture2D*>(value);
    Release(texture);
}

FFU_EXPORT void* FFU_CALL ffu_d3d11_prepare(void* value, const AVFrame* frame, void* texture) {
    auto* presenter = static_cast<Presenter*>(value);
    if (!presenter || !frame || !texture || frame->format != AV_PIX_FMT_D3D11 || !frame->data[0]) return nullptr;
    {
        std::lock_guard<std::mutex> guard(deviceMutex);
        if (presenter->device != unityDevice) return nullptr;
    }
    if (frame->crop_left || frame->crop_top) return nullptr;
    if (frame->colorspace == AVCOL_SPC_BT2020_NCL || frame->colorspace == AVCOL_SPC_BT2020_CL) return nullptr;
    auto* source = reinterpret_cast<ID3D11Texture2D*>(frame->data[0]);
    auto* target = static_cast<ID3D11Texture2D*>(texture);
    D3D11_TEXTURE2D_DESC sourceDesc{}, targetDesc{};
    source->GetDesc(&sourceDesc);
    target->GetDesc(&targetDesc);
    if (sourceDesc.Format != DXGI_FORMAT_NV12 || targetDesc.SampleDesc.Count != 1 ||
        targetDesc.Width != static_cast<UINT>(frame->width) || targetDesc.Height != static_cast<UINT>(frame->height)) return nullptr;
    const auto slice = reinterpret_cast<uintptr_t>(frame->data[1]);
    if (slice >= sourceDesc.ArraySize) return nullptr;
    ID3D11Device* frameDevice = nullptr;
    source->GetDevice(&frameDevice);
    bool sameDevice = frameDevice == presenter->device;
    Release(frameDevice);
    if (!sameDevice) return nullptr;
    if (inFlight.fetch_add(1) >= 24) { --inFlight; return nullptr; }
    auto* packet = new (std::nothrow) Packet();
    if (!packet) { --inFlight; return nullptr; }
    packet->counted = true;
    packet->frame = av_frame_clone(frame);
    if (!packet->frame) { delete packet; return nullptr; }
    packet->owner = presenter;
    presenter->Retain();
    packet->target = target;
    target->AddRef();
    HRESULT result;
    {
        std::lock_guard<std::mutex> guard(presenter->prepareMutex);
        result = presenter->EnsureProcessor(targetDesc.Width, targetDesc.Height, sourceDesc.Format, targetDesc.Format);
        if (SUCCEEDED(result)) result = presenter->EnsureOutput(target);
        if (SUCCEEDED(result)) {
            packet->enumerator = presenter->enumerator; packet->enumerator->AddRef();
            packet->processor = presenter->processor; packet->processor->AddRef();
            packet->output = presenter->output; packet->output->AddRef();
        }
    }
    D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC input{};
    input.ViewDimension = D3D11_VPIV_DIMENSION_TEXTURE2D;
    input.Texture2D.ArraySlice = static_cast<UINT>(slice);
    if (SUCCEEDED(result)) result = presenter->videoDevice->CreateVideoProcessorInputView(source, packet->enumerator, &input, &packet->input);
    D3D11_QUERY_DESC query{};
    query.Query = D3D11_QUERY_EVENT;
    if (SUCCEEDED(result)) result = presenter->device->CreateQuery(&query, &packet->complete);
    if (FAILED(result)) { presenter->error.store(result); delete packet; return nullptr; }
    return packet;
}

void FfuD3D11Submit(void* data) {
    Retire(false);
    if (!data) return;
    auto* packet = static_cast<Packet*>(data);
    auto* presenter = packet->owner;
    auto* context = presenter->videoContext;
    // Cached processors are shared by packets. All processor state and blits
    // remain serialized here on Unity's render thread, including color changes.
    const RECT rect{0, 0, packet->frame->width, packet->frame->height};
    context->VideoProcessorSetStreamFrameFormat(packet->processor, 0, D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE);
    context->VideoProcessorSetStreamAutoProcessingMode(packet->processor, 0, FALSE);
    context->VideoProcessorSetStreamSourceRect(packet->processor, 0, TRUE, &rect);
    context->VideoProcessorSetStreamDestRect(packet->processor, 0, TRUE, &rect);
    context->VideoProcessorSetOutputTargetRect(packet->processor, TRUE, &rect);
    D3D11_VIDEO_PROCESSOR_COLOR_SPACE input{};
    input.YCbCr_Matrix = packet->frame->colorspace == AVCOL_SPC_BT709 ||
        (packet->frame->colorspace == AVCOL_SPC_UNSPECIFIED && packet->frame->height >= 720);
    input.Nominal_Range = packet->frame->color_range == AVCOL_RANGE_JPEG ? 2 : 1;
    context->VideoProcessorSetStreamColorSpace(packet->processor, 0, &input);
    D3D11_VIDEO_PROCESSOR_COLOR_SPACE output{};
    output.Nominal_Range = 2;
    context->VideoProcessorSetOutputColorSpace(packet->processor, &output);
    D3D11_VIDEO_PROCESSOR_STREAM stream{};
    stream.Enable = TRUE;
    stream.pInputSurface = packet->input;
    const HRESULT result = context->VideoProcessorBlt(packet->processor, packet->output, 0, 1, &stream);
    presenter->error.store(FAILED(result) ? result : 0);
    // Even a failed driver operation may have consumed resources: retire after
    // an actual GPU completion query, never after a fixed number of Unity frames.
    presenter->immediate->End(packet->complete);
    pending.push_back(packet);
}
FFU_EXPORT void* FFU_CALL ffu_shared_surface_create(void* value, void* texture, unsigned int glName) {
    if (!value || !texture) return nullptr;
    if (activeRenderer == kUnityGfxRendererOpenGLCore)
        return FfuWglCreatePending(static_cast<Presenter*>(value)->device, static_cast<ID3D11Texture2D*>(texture), glName);
    if (activeRenderer == kUnityGfxRendererVulkan) return FfuVulkanImport(static_cast<ID3D11Texture2D*>(texture));
    return nullptr;
}
FFU_EXPORT void* FFU_CALL ffu_shared_prepare(void* presenter, const AVFrame* frame, void* texture, void* surface, void* target) {
    auto* packet = static_cast<Packet*>(ffu_d3d11_prepare(presenter, frame, texture));
    if (packet) { packet->interopSurface = surface; packet->unityTarget = target; }
    return packet;
}
FFU_EXPORT int FFU_CALL ffu_shared_error(void* surface) {
    if (!surface) return 0;
    if (activeRenderer == kUnityGfxRendererVulkan) return FfuVulkanError(surface);
    if (activeRenderer == kUnityGfxRendererOpenGLCore) {
        int error = FfuWglStatus(static_cast<FfuWglSurface*>(surface));
        return error == FfuWglNotInitialized ? 0 : error;
    }
    return 0;
}
FFU_EXPORT void* FFU_CALL ffu_wgl_retirement_create(void* surface) {
    if (!surface) return nullptr;
    return new (std::nothrow) WglRetirement{static_cast<FfuWglSurface*>(surface)};
}
FFU_EXPORT int FFU_CALL ffu_wgl_retirement_poll(void* value) {
    auto* ticket = static_cast<WglRetirement*>(value);
    if (!ticket || !ticket->complete.load()) return 0;
    delete ticket;
    return 1;
}
void FfuPlatformRender(int event, void* data) {
    if (event == FfuPrepareD3D12 || event == FfuSubmitD3D12 ||
        event == FfuPrepareNativeD3D12 || event == FfuSubmitNativeD3D12) { FfuD3D12Render(event, data); return; }
    if (event == FfuSubmitPortableVulkan && data) { FfuVkSubmit(data); return; }
    if (event == FfuSubmitD3D11) { FfuD3D11Submit(data); return; }
    if (event == FfuSubmitWgl && data) {
        auto* packet = static_cast<Packet*>(data);
        auto* surface = static_cast<FfuWglSurface*>(packet->interopSurface);
        if (!FfuWglInitialize(surface) || FfuWglIsLocked(surface)) { packet->owner->error = E_FAIL; delete packet; return; }
        FfuD3D11Submit(packet);
        if (!FfuWglLock(surface)) packet->owner->error = E_FAIL;
        return;
    }
    if (event == FfuCompleteWgl && data) { FfuWglUnlock(static_cast<FfuWglSurface*>(data)); Retire(false); return; }
    if (event == FfuDestroyWgl && data) {
        auto* ticket = static_cast<WglRetirement*>(data);
        if (FfuWglDestroy(ticket->surface)) ticket->complete = true;
        else retiringWgl.push_back(ticket);
    }
    if (event == FfuSubmitVulkan && data) {
        auto* packet = static_cast<Packet*>(data);
        if (!FfuVulkanBeginWrite(packet->interopSurface)) {
            const int error = FfuVulkanError(packet->interopSurface);
            if (error) packet->owner->error = error;
            delete packet; return;
        }
        FfuD3D11Submit(packet);
        if (!FfuVulkanEndWrite(packet->interopSurface, packet->unityTarget)) packet->owner->error = E_FAIL;
        return;
    }
    if (event == FfuReleaseVulkan && data) FfuVulkanRelease(data);
    if (event == FfuDrain) {
        if (activeRenderer == kUnityGfxRendererD3D12) FfuD3D12Poll(true);
        if (activeRenderer == kUnityGfxRendererVulkan) FfuVulkanPoll(true);
        if (activeRenderer == kUnityGfxRendererVulkan) FfuVkPoll(true);
        for (auto it = retiringWgl.begin(); it != retiringWgl.end();) {
            if (FfuWglDestroy((*it)->surface)) { (*it)->complete = true; it = retiringWgl.erase(it); }
            else ++it;
        }
        Retire(true);
    }
}
FFU_EXPORT void FFU_CALL ffu_packet_cancel(void* value) { delete static_cast<Packet*>(value); }
FFU_EXPORT int FFU_CALL ffu_metal_prepare(const AVFrame*, FfuMetalPlanes*) { return 0; }
