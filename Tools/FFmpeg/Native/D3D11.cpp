#include <d3d11.h>
#include <d3d10.h>
#include "Bridge.h"
#include "IUnityGraphicsD3D11.h"
#include "D3D12.h"
#include "WglInterop.h"
#include "VulkanInterop.h"
#include "VulkanPortable.h"
#include "VulkanVideoDecode.h"
#include <mutex>
#include <vector>
#include <new>
#include <thread>
#include <chrono>

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
    if (unityDevice) unityDevice->AddRef();
    return unityDevice;
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
        if (!FfuVulkanBeginWrite(packet->interopSurface)) { packet->owner->error = E_FAIL; delete packet; return; }
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
