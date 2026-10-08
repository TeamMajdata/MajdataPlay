#include <d3d11.h>
#include <d3d11_1.h>
#include <d3d10.h>
#include <dxgi1_2.h>
#include "Bridge.h"
#include "IUnityGraphicsD3D11.h"
#include "D3D12.h"
#include "WglInterop.h"
#include "VulkanInterop.h"
#include "VulkanPortable.h"
#include "VulkanVideoDecode.h"
#include <mutex>
#include <algorithm>
#include <vector>
#include <new>
#include <thread>
#include <chrono>
#ifdef FFU_STAGE_BENCHMARK
#include <cstdio>
static double stageSetupMs = 0, stageConvertMs = 0, stageReleaseMs = 0, stageFlushMs = 0, stagePollMs = 0;
static int stageCalls = 0, stagePolls = 0;
void FfuD3D11Trace(bool reset) {
    if (!reset) std::printf("BENCH phases: calls=%d setup=%.2fms convert=%.2fms release_key=%.2fms flush=%.2fms polls=%d poll_cpu=%.2fms\n",
        stageCalls, stageSetupMs, stageConvertMs, stageReleaseMs, stageFlushMs, stagePolls, stagePollMs);
    stageSetupMs = stageConvertMs = stageReleaseMs = stageFlushMs = stagePollMs = 0; stageCalls = stagePolls = 0;
}
struct StagePollTrace {
    std::chrono::steady_clock::time_point begin = std::chrono::steady_clock::now();
    ~StagePollTrace() { ++stagePolls; stagePollMs += std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - begin).count(); }
};
#endif
extern "C" {
#include <libavutil/hwcontext.h>
#include <libavutil/hwcontext_d3d11va.h>
}

template<class T> static void Release(T*& value) { if (value) { value->Release(); value = nullptr; } }
static std::mutex deviceMutex;
static ID3D11Device* unityDevice = nullptr;
static ID3D11Device* decodeDevice = nullptr;
static UnityGfxRenderer activeRenderer = kUnityGfxRendererNull;
static std::atomic<int> inFlight{0};
static std::atomic<int> initializationStatus{3};
static bool StagedFrameReady(const AVFrame* frame);

// AcquireSync returns Win32 wait statuses, not just HRESULT failures. Never let
// WAIT_ABANDONED (a positive value) masquerade as completed staging to callers.
static HRESULT KeyedMutexFailure(HRESULT result) {
    return FAILED(result) ? result : HRESULT_FROM_WIN32(static_cast<DWORD>(result));
}

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
    ID3D11Texture2D* rgba = nullptr;
    ID3D11VideoProcessorEnumerator* enumerator = nullptr;
    ID3D11VideoProcessor* processor = nullptr;
    ID3D11VideoProcessorInputView* input = nullptr;
    ID3D11VideoProcessorOutputView* output = nullptr;
    ID3D11Query* complete = nullptr;
    void* interopSurface = nullptr;
    void* unityTarget = nullptr;
    ~Packet() {
        Release(complete); Release(output); Release(input); Release(processor); Release(enumerator); Release(rgba); Release(target);
        av_frame_free(&frame);
        if (owner) owner->Drop();
        if (counted) --inFlight;
    }
};
// Accessed on the Unity graphics callback thread (the D3D12 submission thread)
// or during synchronized plug-in unload.
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
    Release(decodeDevice);
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
    Release(decodeDevice);
    Release(unityDevice);
}
int FfuPlatformCapabilities() {
    std::lock_guard<std::mutex> guard(deviceMutex);
    switch (activeRenderer) {
        case kUnityGfxRendererD3D11: return unityDevice ? FfuD3D11GpuConversion : 0;
        case kUnityGfxRendererD3D12: return FfuD3D12Capabilities();
        case kUnityGfxRendererOpenGLCore: return unityDevice ? FfuWglGpuInterop : 0;
        case kUnityGfxRendererVulkan: return (unityDevice ? FfuVulkanGpuCopy : 0) |
            (FfuVulkanVideoAvailable() ? FfuVulkanVideoDecode : 0);
        default: return 0;
    }
}
int FfuPlatformStatus() { return initializationStatus.load(); }
FFU_EXPORT void* FFU_CALL ffu_d3d11_acquire_device() {
    std::lock_guard<std::mutex> guard(deviceMutex);
    if (!unityDevice) return nullptr;
    if (!decodeDevice) {
        // Decode driver calls can wait while holding D3D11's multithread lock.
        // Never share that immediate context with Unity or its copy presenter.
        IDXGIDevice* dxgi = nullptr;
        IDXGIAdapter* adapter = nullptr;
        ID3D11DeviceContext* context = nullptr;
        ID3D10Multithread* threading = nullptr;
        HRESULT result = unityDevice->QueryInterface(IID_PPV_ARGS(&dxgi));
        if (SUCCEEDED(result)) result = dxgi->GetAdapter(&adapter);
        if (SUCCEEDED(result)) result = D3D11CreateDevice(adapter, D3D_DRIVER_TYPE_UNKNOWN, nullptr,
            D3D11_CREATE_DEVICE_VIDEO_SUPPORT, nullptr, 0, D3D11_SDK_VERSION, &decodeDevice, nullptr, &context);
        if (SUCCEEDED(result)) result = context->QueryInterface(IID_PPV_ARGS(&threading));
        if (SUCCEEDED(result)) threading->SetMultithreadProtected(TRUE);
        Release(threading); Release(context); Release(adapter); Release(dxgi);
        if (FAILED(result)) { Release(decodeDevice); initializationStatus = result; return nullptr; }
    }
    decodeDevice->AddRef();
    return decodeDevice;
}
static Presenter* CreatePresenter(ID3D11Device* device) {
    auto* presenter = new (std::nothrow) Presenter();
    if (!presenter) return nullptr;
    if (!device) { delete presenter; return nullptr; }
    presenter->device = device;
    device->AddRef();
    presenter->device->GetImmediateContext(&presenter->immediate);
    if (FAILED(presenter->device->QueryInterface(__uuidof(ID3D11VideoDevice), reinterpret_cast<void**>(&presenter->videoDevice))) ||
        FAILED(presenter->immediate->QueryInterface(__uuidof(ID3D11VideoContext), reinterpret_cast<void**>(&presenter->videoContext)))) {
        delete presenter;
        return nullptr;
    }
    return presenter;
}
FFU_EXPORT void* FFU_CALL ffu_d3d11_create() {
    std::lock_guard<std::mutex> guard(deviceMutex);
    return CreatePresenter(unityDevice);
}
FFU_EXPORT void FFU_CALL ffu_d3d11_release(void* value) {
    if (value) { FfuD3D12ForgetPresenter(value); static_cast<Presenter*>(value)->Drop(); }
}
FFU_EXPORT int FFU_CALL ffu_d3d11_error(void* value) { return value ? static_cast<Presenter*>(value)->error.load() : -1; }

// FFmpeg submits asynchronously and may block inside DecoderBeginFrame or
// GetDecoderBuffer if the worker outruns the GPU. Pace codec calls on the worker
// with an explicit completion query, before those driver waits can reach Unity's
// render-thread conversion or shared immediate-context submissions.
struct DecodeSynchronization {
    AVBufferRef* deviceReference = nullptr;
    AVD3D11VADeviceContext* device = nullptr;
    ID3D11Query* complete = nullptr;
    bool started = false;
    ~DecodeSynchronization() { Release(complete); av_buffer_unref(&deviceReference); }
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
    return result == S_OK ? 1 : 0;
}
FFU_EXPORT void FFU_CALL ffu_d3d11_decode_sync_release(void* value) { delete static_cast<DecodeSynchronization*>(value); }

void FfuD3D11RetainPresenter(void* value) { if (value) static_cast<Presenter*>(value)->Retain(); }
void FfuD3D11DropPresenter(void* value) { if (value) static_cast<Presenter*>(value)->Drop(); }
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
    if ((sourceDesc.Format != DXGI_FORMAT_NV12 && sourceDesc.Format != DXGI_FORMAT_R8G8B8A8_UNORM) || targetDesc.SampleDesc.Count != 1 ||
        targetDesc.Width != static_cast<UINT>(frame->width) || targetDesc.Height != static_cast<UINT>(frame->height)) return nullptr;
    const auto slice = reinterpret_cast<uintptr_t>(frame->data[1]);
    if (slice >= sourceDesc.ArraySize) return nullptr;
    ID3D11Device* frameDevice = nullptr;
    source->GetDevice(&frameDevice);
    bool sameDevice = frameDevice == presenter->device;
    Release(frameDevice);
    if (!sameDevice) return nullptr;
    if (sourceDesc.Format == DXGI_FORMAT_R8G8B8A8_UNORM && !StagedFrameReady(frame)) return nullptr;
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
    if (sourceDesc.Format == DXGI_FORMAT_R8G8B8A8_UNORM) {
        if (slice != 0 || sourceDesc.Width != targetDesc.Width || sourceDesc.Height != targetDesc.Height ||
            targetDesc.Format != DXGI_FORMAT_R8G8B8A8_UNORM) { delete packet; return nullptr; }
        packet->rgba = source; source->AddRef();
        D3D11_QUERY_DESC query{}; query.Query = D3D11_QUERY_EVENT;
        result = presenter->device->CreateQuery(&query, &packet->complete);
        if (FAILED(result)) { presenter->error = result; delete packet; return nullptr; }
        return packet;
    }
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

static HRESULT ConvertFrame(Presenter* presenter, const AVFrame* frame, ID3D11VideoProcessor* processor,
                            ID3D11VideoProcessorInputView* view, ID3D11VideoProcessorOutputView* target) {
    auto* context = presenter->videoContext;
    const RECT rect{0, 0, frame->width, frame->height};
    context->VideoProcessorSetStreamFrameFormat(processor, 0, D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE);
    context->VideoProcessorSetStreamAutoProcessingMode(processor, 0, FALSE);
    context->VideoProcessorSetStreamSourceRect(processor, 0, TRUE, &rect);
    context->VideoProcessorSetStreamDestRect(processor, 0, TRUE, &rect);
    context->VideoProcessorSetOutputTargetRect(processor, TRUE, &rect);
    D3D11_VIDEO_PROCESSOR_COLOR_SPACE input{};
    input.YCbCr_Matrix = frame->colorspace == AVCOL_SPC_BT709 ||
        (frame->colorspace == AVCOL_SPC_UNSPECIFIED && frame->height >= 720);
    input.Nominal_Range = frame->color_range == AVCOL_RANGE_JPEG ? 2 : 1;
    context->VideoProcessorSetStreamColorSpace(processor, 0, &input);
    D3D11_VIDEO_PROCESSOR_COLOR_SPACE output{};
    output.Nominal_Range = 2;
    context->VideoProcessorSetOutputColorSpace(processor, &output);
    D3D11_VIDEO_PROCESSOR_STREAM stream{};
    stream.Enable = TRUE;
    stream.pInputSurface = view;
    return context->VideoProcessorBlt(processor, target, 0, 1, &stream);
}

struct StagingPool;
static void ScheduleStagingCleanup(StagingPool* pool);
struct StagingSurface;
static std::mutex stagingRegistryMutex;
static std::vector<StagingSurface*> stagingRegistry;
struct StagingSurface {
    StagingPool* owner = nullptr;
    std::atomic<bool> leased{false};
    ID3D11Texture2D* producer = nullptr;
    ID3D11Texture2D* reader = nullptr;
    IDXGIKeyedMutex* producerKey = nullptr;
    IDXGIKeyedMutex* readerKey = nullptr;
    ID3D11Query* complete = nullptr;
    ID3D11VideoProcessorInputView* inputView = nullptr;
    ID3D11VideoProcessorOutputView* outputView = nullptr;
    ID3D11VideoProcessor* processor = nullptr;
    ID3D11VideoProcessorEnumerator* enumerator = nullptr;
    AVFrame* input = nullptr;
    bool begun = false;
    bool conversionComplete = false;
    bool readerAcquired = false;
    UINT width = 0, height = 0;
    void ReleaseConversionViews() {
        Release(inputView); Release(outputView); Release(processor); Release(enumerator);
    }
    ~StagingSurface() {
        {
            std::lock_guard<std::mutex> guard(stagingRegistryMutex);
            stagingRegistry.erase(std::remove(stagingRegistry.begin(), stagingRegistry.end(), this), stagingRegistry.end());
        }
        av_frame_free(&input);
        ReleaseConversionViews();
        Release(complete); Release(readerKey); Release(producerKey); Release(reader); Release(producer);
    }
};
struct StagingPool {
    std::atomic<int> references{1};
    std::atomic<int> error{0};
    Presenter* converter = nullptr;
    ID3D11Device1* readerDevice = nullptr;
    std::vector<StagingSurface*> surfaces;
    StagingSurface* current = nullptr;
    void Retain() { ++references; }
    void Drop() {
        if (--references != 0) return;
        // Cancellation may release the unpublished output while conversion is
        // pending. Never block a final render-thread AVFrame release or destroy
        // its decoder input before completion; a lost driver retains the pool.
        if (!CanDestroy()) {
            ScheduleStagingCleanup(this);
            return;
        }
        delete this;
    }
    bool CanDestroy() {
        for (auto* surface : surfaces) {
            if (surface->begun && !surface->conversionComplete && converter->immediate->GetData(surface->complete, nullptr, 0,
                    D3D11_ASYNC_GETDATA_DONOTFLUSH) != S_OK && SUCCEEDED(converter->device->GetDeviceRemovedReason())) return false;
        }
        return true;
    }
    ~StagingPool() {
        for (auto* surface : surfaces) delete surface;
        Release(readerDevice);
        if (converter) converter->Drop();
    }
    bool Recycle(StagingSurface* surface) {
        if (surface->leased.load(std::memory_order_acquire)) return false;
        if (!surface->begun) return true;
        const HRESULT done = surface->conversionComplete ? S_OK : converter->immediate->GetData(surface->complete, nullptr, 0, D3D11_ASYNC_GETDATA_DONOTFLUSH);
        if (done == S_FALSE) return false;
        if (FAILED(done)) { error = done; return false; }
        if (!surface->readerAcquired) {
            const HRESULT acquire = surface->readerKey->AcquireSync(1, 0);
            if (acquire == WAIT_TIMEOUT) return false;
            if (acquire != S_OK) { error = KeyedMutexFailure(acquire); return false; }
        }
        const HRESULT release = surface->readerKey->ReleaseSync(0);
        if (release != S_OK) { error = KeyedMutexFailure(release); return false; }
        surface->readerAcquired = false; surface->begun = false; surface->conversionComplete = false;
        surface->ReleaseConversionViews();
        av_frame_free(&surface->input);
        return true;
    }
    StagingSurface* Create(UINT width, UINT height) {
        auto* surface = new (std::nothrow) StagingSurface();
        if (!surface) { error = E_OUTOFMEMORY; return nullptr; }
        surface->owner = this; surface->width = width; surface->height = height;
        D3D11_TEXTURE2D_DESC desc{};
        desc.Width = width; desc.Height = height; desc.MipLevels = desc.ArraySize = 1;
        desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM; desc.SampleDesc.Count = 1;
        desc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
        desc.MiscFlags = D3D11_RESOURCE_MISC_SHARED_NTHANDLE | D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX;
        HRESULT result = converter->device->CreateTexture2D(&desc, nullptr, &surface->producer);
        IDXGIResource1* shared = nullptr;
        HANDLE handle = nullptr;
        if (SUCCEEDED(result)) result = surface->producer->QueryInterface(IID_PPV_ARGS(&shared));
        if (SUCCEEDED(result)) result = shared->CreateSharedHandle(nullptr, DXGI_SHARED_RESOURCE_READ | DXGI_SHARED_RESOURCE_WRITE, nullptr, &handle);
        if (SUCCEEDED(result)) result = readerDevice->OpenSharedResource1(handle, IID_PPV_ARGS(&surface->reader));
        if (handle) CloseHandle(handle);
        Release(shared);
        if (SUCCEEDED(result)) result = surface->producer->QueryInterface(IID_PPV_ARGS(&surface->producerKey));
        if (SUCCEEDED(result)) result = surface->reader->QueryInterface(IID_PPV_ARGS(&surface->readerKey));
        D3D11_QUERY_DESC query{}; query.Query = D3D11_QUERY_EVENT;
        if (SUCCEEDED(result)) result = converter->device->CreateQuery(&query, &surface->complete);
        if (FAILED(result)) { error = result; delete surface; return nullptr; }
        surfaces.push_back(surface);
        {
            std::lock_guard<std::mutex> guard(stagingRegistryMutex);
            stagingRegistry.push_back(surface);
        }
        return surface;
    }
};
struct StagingCleanup {
    StagingPool* pool;
    HMODULE module;
};
static DWORD WINAPI CleanupStaging(void* opaque) {
    auto* cleanup = static_cast<StagingCleanup*>(opaque);
    auto* pool = cleanup->pool;
    const HMODULE module = cleanup->module;
    delete cleanup;
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
    while (!pool->CanDestroy() && std::chrono::steady_clock::now() < deadline)
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    if (pool->CanDestroy()) delete pool;
    // Timed-out drivers retain storage. Keep this callback's code loaded until
    // its final instruction even when Unity has already unloaded the plug-in.
    FreeLibraryAndExitThread(module, 0);
}
static void ScheduleStagingCleanup(StagingPool* pool) {
    auto* cleanup = new (std::nothrow) StagingCleanup{pool, nullptr};
    if (!cleanup) return;
    if (!GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
            reinterpret_cast<LPCSTR>(&CleanupStaging), &cleanup->module)) { delete cleanup; return; }
    HANDLE thread = CreateThread(nullptr, 0, CleanupStaging, cleanup, 0, nullptr);
    if (thread) CloseHandle(thread);
    else { FreeLibrary(cleanup->module); delete cleanup; }
}
#ifdef FFU_STAGE_BENCHMARK
// Test-only interleaving: the real worker may delete a retired slot on resize.
static void (*stagingReleaseTestHook)(StagingSurface*) = nullptr;
#endif
static void ReleaseStagedFrame(void* opaque, uint8_t*) {
    auto* surface = static_cast<StagingSurface*>(opaque);
    auto* owner = surface->owner;
    if (surface->conversionComplete) av_frame_free(&surface->input);
    // The worker can reuse or delete this surface immediately after publication.
    // Only the independently retained pool may be accessed from here onward.
    surface->leased.store(false, std::memory_order_release);
#ifdef FFU_STAGE_BENCHMARK
    if (stagingReleaseTestHook) stagingReleaseTestHook(surface);
#endif
    owner->Drop();
}
FFU_EXPORT void* FFU_CALL ffu_d3d11_stage_create(void* hardwareDevice) {
    auto* reference = static_cast<AVBufferRef*>(hardwareDevice);
    if (!reference || !reference->data) return nullptr;
    auto* hardware = reinterpret_cast<AVHWDeviceContext*>(reference->data);
    if (hardware->type != AV_HWDEVICE_TYPE_D3D11VA || !hardware->hwctx) return nullptr;
    auto* d3d11 = static_cast<AVD3D11VADeviceContext*>(hardware->hwctx);
    auto* pool = new (std::nothrow) StagingPool();
    if (!pool) return nullptr;
    pool->converter = CreatePresenter(d3d11->device);
    HRESULT result = E_FAIL;
    {
        std::lock_guard<std::mutex> guard(deviceMutex);
        if (unityDevice && unityDevice != d3d11->device) result = unityDevice->QueryInterface(IID_PPV_ARGS(&pool->readerDevice));
    }
    if (!pool->converter || FAILED(result)) {
        initializationStatus = FAILED(result) ? result : E_NOINTERFACE;
        delete pool; return nullptr;
    }
    return pool;
}
FFU_EXPORT AVFrame* FFU_CALL ffu_d3d11_stage_frame(void* value, const AVFrame* frame) {
#ifdef FFU_STAGE_BENCHMARK
    auto phaseBegin = std::chrono::steady_clock::now();
#endif
    auto* pool = static_cast<StagingPool*>(value);
    if (!pool) return nullptr;
    pool->error = 0;
    pool->current = nullptr;
    if (!frame || frame->format != AV_PIX_FMT_D3D11 || !frame->data[0] || frame->width <= 0 || frame->height <= 0 ||
        frame->crop_left || frame->crop_top || frame->colorspace == AVCOL_SPC_BT2020_NCL ||
        frame->colorspace == AVCOL_SPC_BT2020_CL || frame->color_trc == AVCOL_TRC_SMPTE2084 ||
        frame->color_trc == AVCOL_TRC_ARIB_STD_B67) { pool->error = E_INVALIDARG; return nullptr; }
    auto* source = reinterpret_cast<ID3D11Texture2D*>(frame->data[0]);
    D3D11_TEXTURE2D_DESC desc{}; source->GetDesc(&desc);
    ID3D11Device* sourceDevice = nullptr; source->GetDevice(&sourceDevice);
    const bool sameDevice = sourceDevice == pool->converter->device;
    Release(sourceDevice);
    const auto slice = reinterpret_cast<uintptr_t>(frame->data[1]);
    if (!sameDevice || desc.Format != DXGI_FORMAT_NV12 || slice >= desc.ArraySize ||
        desc.Width < static_cast<UINT>(frame->width) || desc.Height < static_cast<UINT>(frame->height)) { pool->error = E_INVALIDARG; return nullptr; }
    StagingSurface* surface = nullptr;
    for (auto it = pool->surfaces.begin(); it != pool->surfaces.end();) {
        auto* candidate = *it;
        if (!pool->Recycle(candidate)) { ++it; continue; }
        if (candidate->width == static_cast<UINT>(frame->width) && candidate->height == static_cast<UINT>(frame->height)) {
            surface = candidate; break;
        }
        delete candidate; it = pool->surfaces.erase(it);
    }
    // Leave room beyond the eight-output queue for a replacement candidate
    // and render packets, so a full queue can continue evicting older frames.
    if (!surface && pool->surfaces.size() < 24) surface = pool->Create(frame->width, frame->height);
    if (!surface) return nullptr;
    HRESULT result = surface->producerKey->AcquireSync(0, 0);
    if (result == WAIT_TIMEOUT) return nullptr;
    if (result != S_OK) { pool->error = KeyedMutexFailure(result); return nullptr; }
    AVFrame* output = av_frame_alloc();
    surface->input = av_frame_clone(frame);
    if (!output || !surface->input) {
        av_frame_free(&output); av_frame_free(&surface->input); surface->producerKey->ReleaseSync(0);
        pool->error = E_OUTOFMEMORY; return nullptr;
    }
    auto* converter = pool->converter;
    result = converter->EnsureProcessor(frame->width, frame->height, DXGI_FORMAT_NV12, DXGI_FORMAT_R8G8B8A8_UNORM);
    if (SUCCEEDED(result)) result = converter->EnsureOutput(surface->producer);
    D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC view{};
    view.ViewDimension = D3D11_VPIV_DIMENSION_TEXTURE2D; view.Texture2D.ArraySlice = static_cast<UINT>(slice);
    if (SUCCEEDED(result)) result = converter->videoDevice->CreateVideoProcessorInputView(source, converter->enumerator, &view, &surface->inputView);
    if (SUCCEEDED(result)) {
        surface->processor = converter->processor; surface->processor->AddRef();
        surface->enumerator = converter->enumerator; surface->enumerator->AddRef();
        surface->outputView = converter->output; surface->outputView->AddRef();
#ifdef FFU_STAGE_BENCHMARK
        ++stageCalls; stageSetupMs += std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - phaseBegin).count();
        phaseBegin = std::chrono::steady_clock::now();
#endif
        result = ConvertFrame(converter, frame, surface->processor, surface->inputView, surface->outputView);
    }
#ifdef FFU_STAGE_BENCHMARK
    stageConvertMs += std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - phaseBegin).count();
    phaseBegin = std::chrono::steady_clock::now();
#endif
    converter->immediate->End(surface->complete);
    surface->begun = true;
    // The producer texture is shared with another D3D11 device and, on the
    // Vulkan backend, is handed to a foreign queue after the keyed-mutex
    // release. Submit the conversion before publishing key 1; otherwise the
    // consumer can acquire the key while the copy is still only client-side.
    converter->immediate->Flush();
#ifdef FFU_STAGE_BENCHMARK
    stageFlushMs += std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - phaseBegin).count();
    phaseBegin = std::chrono::steady_clock::now();
#endif
    const HRESULT released = surface->producerKey->ReleaseSync(1);
#ifdef FFU_STAGE_BENCHMARK
    stageReleaseMs += std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - phaseBegin).count();
#endif
    if (SUCCEEDED(result)) result = released == S_OK ? S_OK : KeyedMutexFailure(released);
    if (FAILED(result)) { pool->error = result; av_frame_free(&output); return nullptr; }
    const int copied = av_frame_copy_props(output, frame);
    if (copied < 0) { pool->error = copied; av_frame_free(&output); return nullptr; }
    surface->leased = true; pool->Retain();
    output->buf[0] = av_buffer_create(reinterpret_cast<uint8_t*>(surface->reader), 1, ReleaseStagedFrame, surface, 0);
    if (!output->buf[0]) {
        surface->leased = false; pool->Drop();
        av_frame_free(&output); pool->error = E_OUTOFMEMORY; return nullptr;
    }
    output->format = AV_PIX_FMT_D3D11; output->width = frame->width; output->height = frame->height;
    output->data[0] = reinterpret_cast<uint8_t*>(surface->reader); output->data[1] = nullptr;
    output->crop_right = output->crop_bottom = 0;
    pool->current = surface;
    return output;
}
// Publish readiness only after successful reader ownership; Win32 wait errors
// must follow the negative-error convention of the managed staging poll.
static int CompleteStagingReadiness(StagingSurface* surface, HRESULT acquire) {
    if (acquire == WAIT_TIMEOUT) return 0;
    if (acquire != S_OK) return KeyedMutexFailure(acquire);
    surface->readerAcquired = true;
    surface->conversionComplete = true;
    surface->ReleaseConversionViews();
    return 1;
}
#ifdef FFU_STAGE_BENCHMARK
// Deterministic no-GPU regressions linked only by the existing smoke target.
bool FfuD3D11TestStagingRetirement() {
    auto* pool = new StagingPool();
    auto* surface = new StagingSurface();
    surface->owner = pool;
    surface->leased = true;
    pool->surfaces.push_back(surface);
    pool->Retain();
    stagingReleaseTestHook = [](StagingSurface* retired) {
        auto* owner = retired->owner;
        if (retired->leased.load(std::memory_order_acquire)) return;
        owner->surfaces.clear();
        delete retired;
    };
    ReleaseStagedFrame(surface, nullptr);
    stagingReleaseTestHook = nullptr;
    const bool valid = pool->references == 1 && pool->surfaces.empty();
    pool->Drop();
    return valid;
}
int FfuD3D11TestStagingReadiness(HRESULT acquire, bool* conversionComplete, bool* readerAcquired) {
    StagingSurface surface;
    const int result = CompleteStagingReadiness(&surface, acquire);
    *conversionComplete = surface.conversionComplete;
    *readerAcquired = surface.readerAcquired;
    return result;
}
#endif
FFU_EXPORT int FFU_CALL ffu_d3d11_stage_ready(void* value) {
#ifdef FFU_STAGE_BENCHMARK
    StagePollTrace trace;
#endif
    auto* pool = static_cast<StagingPool*>(value);
    if (!pool || !pool->current) return E_INVALIDARG;
    auto* surface = pool->current;
    const HRESULT removed = pool->converter->device->GetDeviceRemovedReason();
    if (FAILED(removed)) return removed;
    const HRESULT done = pool->converter->immediate->GetData(surface->complete, nullptr, 0, D3D11_ASYNC_GETDATA_DONOTFLUSH);
    if (done == S_FALSE) return 0;
    if (FAILED(done)) return done;
    const HRESULT acquire = surface->readerAcquired ? S_OK : surface->readerKey->AcquireSync(1, 0);
    return CompleteStagingReadiness(surface, acquire);
}
FFU_EXPORT int FFU_CALL ffu_d3d11_stage_error(void* value) {
    return value ? static_cast<StagingPool*>(value)->error.load() : E_INVALIDARG;
}
FFU_EXPORT void FFU_CALL ffu_d3d11_stage_release(void* value) {
    if (value) static_cast<StagingPool*>(value)->Drop();
}
FFU_EXPORT const AVFrame* FFU_CALL ffu_d3d11_stage_source_frame(const AVFrame* frame) {
    if (!frame || frame->format != AV_PIX_FMT_D3D11 || frame->hw_frames_ctx || !frame->buf[0]) return nullptr;
    const auto* opaque = av_buffer_get_opaque(frame->buf[0]);
    std::lock_guard<std::mutex> guard(stagingRegistryMutex);
    for (auto* surface : stagingRegistry) {
        if (surface == opaque && surface->leased.load(std::memory_order_acquire) &&
            reinterpret_cast<uint8_t*>(surface->reader) == frame->data[0]) return surface->input;
    }
    return nullptr;
}
static bool StagedFrameReady(const AVFrame* frame) {
    if (!frame->buf[0]) return false;
    const auto* opaque = av_buffer_get_opaque(frame->buf[0]);
    std::lock_guard<std::mutex> guard(stagingRegistryMutex);
    for (auto* surface : stagingRegistry) {
        if (surface == opaque && reinterpret_cast<uint8_t*>(surface->reader) == frame->data[0])
            return surface->conversionComplete && surface->readerAcquired;
    }
    return false;
}
void FfuD3D11Submit(void* data) {
    Retire(false);
    if (!data) return;
    auto* packet = static_cast<Packet*>(data);
    auto* presenter = packet->owner;
    HRESULT result = S_OK;
    if (packet->rgba) {
        // Only completed worker-converted storage reaches this path. This
        // immediate context is independent of the hardware decoder's context.
        presenter->immediate->CopyResource(packet->target, packet->rgba);
    } else {
        result = ConvertFrame(presenter, packet->frame, packet->processor, packet->input, packet->output);
    }
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
    if (event == FfuPoll) {
        if (activeRenderer == kUnityGfxRendererD3D12) FfuD3D12Poll(false);
        if (activeRenderer == kUnityGfxRendererVulkan) { FfuVulkanPoll(false); FfuVkPoll(false); }
        Retire(false);
        return;
    }
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
