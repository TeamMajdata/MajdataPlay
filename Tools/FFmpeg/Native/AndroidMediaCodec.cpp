#if defined(__ANDROID__)
#include "AndroidMediaCodec.h"
#include "VulkanPortable.h"
#include <android/hardware_buffer.h>
#include <android/native_window_jni.h>
#include <media/NdkImageReader.h>
#include <dlfcn.h>
#include <unistd.h>
#include <time.h>
#include <algorithm>
#include <chrono>
#include <mutex>
#include <new>
#include <thread>
extern "C" {
#include <libavcodec/mediacodec.h>
#include <libavcodec/jni.h>
#include <libavutil/hwcontext.h>
#include <libavutil/hwcontext_mediacodec.h>
}

namespace {
// Resolve API 26 additions lazily: the bridge remains loadable at the project's
// API 23 minimum. Unsupported Android releases report no hardware capability.
struct AndroidApi {
    void* media = nullptr;
    void* android = nullptr;
#define MEDIA_FUNCTIONS(X) X(AImageReader_newWithUsage) X(AImageReader_getWindow) X(AImageReader_delete) \
    X(AImageReader_acquireNextImageAsync) X(AImage_deleteAsync) X(AImage_getHardwareBuffer) X(AImage_getTimestamp) X(AImage_getCropRect)
#define ANDROID_FUNCTIONS(X) X(AHardwareBuffer_describe) X(ANativeWindow_toSurface)
    // Explicit signatures avoid taking the address of NDK symbols marked
    // unavailable at API 23. None of these produce ELF undefined imports.
    media_status_t (*AImageReader_newWithUsage_fn)(int32_t, int32_t, int32_t, uint64_t, int32_t, AImageReader**) = nullptr;
    media_status_t (*AImageReader_getWindow_fn)(AImageReader*, ANativeWindow**) = nullptr;
    void (*AImageReader_delete_fn)(AImageReader*) = nullptr;
    media_status_t (*AImageReader_acquireNextImageAsync_fn)(AImageReader*, AImage**, int*) = nullptr;
    void (*AImage_deleteAsync_fn)(AImage*, int) = nullptr;
    media_status_t (*AImage_getHardwareBuffer_fn)(const AImage*, AHardwareBuffer**) = nullptr;
    media_status_t (*AImage_getTimestamp_fn)(const AImage*, int64_t*) = nullptr;
    media_status_t (*AImage_getCropRect_fn)(const AImage*, AImageCropRect*) = nullptr;
    void (*AHardwareBuffer_describe_fn)(const AHardwareBuffer*, AHardwareBuffer_Desc*) = nullptr;
    jobject (*ANativeWindow_toSurface_fn)(JNIEnv*, ANativeWindow*) = nullptr;
    bool available = false;
    AndroidApi() {
        media = dlopen("libmediandk.so", RTLD_NOW | RTLD_LOCAL);
        android = dlopen("libandroid.so", RTLD_NOW | RTLD_LOCAL);
        if (!media || !android) return;
#define LOAD_MEDIA(name) name##_fn = reinterpret_cast<decltype(name##_fn)>(dlsym(media, #name)); if (!name##_fn) return;
        MEDIA_FUNCTIONS(LOAD_MEDIA)
#undef LOAD_MEDIA
#define LOAD_ANDROID(name) name##_fn = reinterpret_cast<decltype(name##_fn)>(dlsym(android, #name)); if (!name##_fn) return;
        ANDROID_FUNCTIONS(LOAD_ANDROID)
#undef LOAD_ANDROID
        available = true;
    }
    // Keep handles loaded: captured images and AVBufferRefs can outlive a player.
};
AndroidApi& Api() { static AndroidApi api; return api; }
std::atomic<int> lastError{0};
enum Error { UnsupportedApi = -2001, MissingVm = -2002, SurfaceFailure = -2003,
    DeviceFailure = -2004, InvalidFrame = -2005, CaptureTimeout = -2006,
    ImageFailure = -2007, VulkanUnavailable = -2008, InvalidCrop = -2009,
    UnsupportedColor = -2010 };

struct JniScope {
    JavaVM* vm = nullptr;
    JNIEnv* env = nullptr;
    bool attached = false;
    JniScope() {
        vm = static_cast<JavaVM*>(av_jni_get_java_vm(nullptr));
        if (!vm) return;
        const jint state = vm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6);
        if (state == JNI_EDETACHED) attached = vm->AttachCurrentThread(&env, nullptr) == JNI_OK;
        if (state != JNI_OK && !attached) env = nullptr;
    }
    ~JniScope() { if (attached) vm->DetachCurrentThread(); }
    bool Failed() {
        if (!env || !env->ExceptionCheck()) return !env;
        env->ExceptionClear();
        return true;
    }
};

struct Session {
    std::atomic<int> references{1};
    std::atomic<int> error{0};
    std::mutex io;
    AImageReader* reader = nullptr;
    ANativeWindow* window = nullptr; // reader-owned
    jobject surface = nullptr; // global reference
    int64_t lastTag = 0;
    void Retain() { ++references; }
    void Release() { if (--references == 0) delete this; }
    ~Session() {
        if (surface) {
            JniScope jni;
            if (jni.env) {
                jclass type = jni.env->GetObjectClass(surface);
                jmethodID release = type ? jni.env->GetMethodID(type, "release", "()V") : nullptr;
                if (release && !jni.Failed()) jni.env->CallVoidMethod(surface, release);
                jni.Failed();
                if (type) jni.env->DeleteLocalRef(type);
                jni.env->DeleteGlobalRef(surface);
            }
        }
        if (reader) Api().AImageReader_delete_fn(reader);
    }
};
void FreeDevice(AVHWDeviceContext* device) { static_cast<Session*>(device->user_opaque)->Release(); }
struct CapturedImage {
    std::atomic<int> references{1};
    Session* session = nullptr;
    AImage* image = nullptr;
    AHardwareBuffer* buffer = nullptr; // image-owned; image retained until GPU done
    int acquireFence = -1;
    int width = 0, height = 0;
    AVColorSpace colorSpace = AVCOL_SPC_UNSPECIFIED;
    AVColorRange colorRange = AVCOL_RANGE_UNSPECIFIED;
    AVColorTransferCharacteristic colorTransfer = AVCOL_TRC_UNSPECIFIED;
    AImageCropRect crop{};
    void Retain() { ++references; }
    void Release() { if (--references == 0) delete this; }
    ~CapturedImage() {
        // If never submitted, return the producer fence to the reader without
        // waiting on the CPU. After sampling this fence is already signalled.
        if (image) Api().AImage_deleteAsync_fn(image, acquireFence);
        else if (acquireFence >= 0) close(acquireFence);
        if (session) session->Release();
    }
};
void DiscardImage(AImage* image, int fence) { Api().AImage_deleteAsync_fn(image, fence); }
int Fail(Session* session, int error) { if (session) session->error = error; lastError = error; return error; }

struct ImportedImage {
    std::shared_ptr<FfuVkContext> context;
    CapturedImage* source = nullptr;
    VkImage image = VK_NULL_HANDLE;
    VkDeviceMemory memory = VK_NULL_HANDLE;
    VkImageView view = VK_NULL_HANDLE;
    VkSampler sampler = VK_NULL_HANDLE;
    VkSamplerYcbcrConversion conversion = VK_NULL_HANDLE;
    VkSemaphore semaphore = VK_NULL_HANDLE;
    ~ImportedImage() {
        if (context) {
            std::lock_guard<std::recursive_mutex> lock(context->resources);
            if (context->active) {
#define DESTROY(name, value) if (value) reinterpret_cast<PFN_vk##name>(context->Proc("vk" #name))(context->instance.device, value, nullptr)
                DESTROY(DestroySampler, sampler);
                DESTROY(DestroyImageView, view);
                DESTROY(DestroySamplerYcbcrConversion, conversion);
                DESTROY(DestroyImage, image);
                DESTROY(FreeMemory, memory);
                DESTROY(DestroySemaphore, semaphore);
#undef DESTROY
            }
        }
        if (source) source->Release();
    }
};

bool Import(const std::shared_ptr<FfuVkContext>& context, CapturedImage* source, FfuVkSample& sample, int& error) {
    std::lock_guard<std::recursive_mutex> lock(context->resources);
    if (!context->active) { error = VulkanUnavailable; return false; }
    auto resource = std::make_shared<ImportedImage>(); resource->context = context;
    resource->source = source; source->Retain();
#define LOAD(name) auto name = reinterpret_cast<PFN_vk##name>(context->Proc("vk" #name)); if (!name) { error = VulkanUnavailable; return false; }
    LOAD(GetAndroidHardwareBufferPropertiesANDROID); LOAD(CreateImage); LOAD(AllocateMemory); LOAD(BindImageMemory);
    LOAD(CreateSamplerYcbcrConversion); LOAD(CreateSampler); LOAD(CreateImageView); LOAD(CreateSemaphore); LOAD(ImportSemaphoreFdKHR);
#undef LOAD
    VkAndroidHardwareBufferFormatPropertiesANDROID format{}; format.sType = VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_FORMAT_PROPERTIES_ANDROID;
    VkAndroidHardwareBufferPropertiesANDROID properties{}; properties.sType = VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_PROPERTIES_ANDROID;
    properties.pNext = &format;
    VkResult result = GetAndroidHardwareBufferPropertiesANDROID(context->instance.device, source->buffer, &properties);
    if (result != VK_SUCCESS) { error = result; return false; }
    if (!(format.formatFeatures & VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT)) { error = VulkanUnavailable; return false; }
    // The managed SDR presentation shader performs gamma conversion. Sampling
    // an sRGB view here would decode twice; HDR/gamut conversion is not claimed.
    if (format.format == VK_FORMAT_R8G8B8A8_SRGB || format.format == VK_FORMAT_B8G8R8A8_SRGB) {
        error = UnsupportedColor; return false;
    }
    AHardwareBuffer_Desc desc{}; Api().AHardwareBuffer_describe_fn(source->buffer, &desc);
    if (!desc.width || !desc.height || desc.layers != 1 || source->crop.left < 0 || source->crop.top < 0 ||
        source->crop.right <= source->crop.left || source->crop.bottom <= source->crop.top ||
        static_cast<uint32_t>(source->crop.right) > desc.width || static_cast<uint32_t>(source->crop.bottom) > desc.height) {
        error = InvalidCrop; return false;
    }
    // PRIVATE decoder buffers commonly expose only an opaque external format.
    // The producer's suggested conversion describes its implementation layout.
    const bool external = format.externalFormat != 0;
    VkExternalFormatANDROID externalFormat{}; externalFormat.sType = VK_STRUCTURE_TYPE_EXTERNAL_FORMAT_ANDROID;
    externalFormat.externalFormat = external ? format.externalFormat : 0;
    VkExternalMemoryImageCreateInfo externalMemory{}; externalMemory.sType = VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO;
    externalMemory.handleTypes = VK_EXTERNAL_MEMORY_HANDLE_TYPE_ANDROID_HARDWARE_BUFFER_BIT_ANDROID;
    externalMemory.pNext = external ? &externalFormat : nullptr;
    VkImageCreateInfo image{}; image.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO; image.pNext = &externalMemory;
    image.imageType = VK_IMAGE_TYPE_2D; image.format = external ? VK_FORMAT_UNDEFINED : format.format;
    image.extent = {desc.width, desc.height, 1}; image.mipLevels = image.arrayLayers = 1;
    image.samples = VK_SAMPLE_COUNT_1_BIT; image.tiling = VK_IMAGE_TILING_OPTIMAL;
    image.usage = VK_IMAGE_USAGE_SAMPLED_BIT; image.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    image.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    result = CreateImage(context->instance.device, &image, nullptr, &resource->image);
    if (result != VK_SUCCESS) { error = result; return false; }
    const int memoryType = context->MemoryType(properties.memoryTypeBits);
    if (memoryType < 0) { error = VK_ERROR_FEATURE_NOT_PRESENT; return false; }
    VkImportAndroidHardwareBufferInfoANDROID imported{}; imported.sType = VK_STRUCTURE_TYPE_IMPORT_ANDROID_HARDWARE_BUFFER_INFO_ANDROID;
    imported.buffer = source->buffer;
    VkMemoryDedicatedAllocateInfo dedicated{}; dedicated.sType = VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO;
    dedicated.pNext = &imported; dedicated.image = resource->image;
    VkMemoryAllocateInfo allocate{}; allocate.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO; allocate.pNext = &dedicated;
    allocate.allocationSize = properties.allocationSize; allocate.memoryTypeIndex = static_cast<uint32_t>(memoryType);
    result = AllocateMemory(context->instance.device, &allocate, nullptr, &resource->memory);
    if (result == VK_SUCCESS) result = BindImageMemory(context->instance.device, resource->image, resource->memory, 0);
    if (result != VK_SUCCESS) { error = result; return false; }
    const bool rgb = !external && (format.format == VK_FORMAT_R8G8B8A8_UNORM || format.format == VK_FORMAT_B8G8R8A8_UNORM);
    VkSamplerYcbcrConversionInfo conversionInfo{}; conversionInfo.sType = VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_INFO;
    VkFilter filter = (format.formatFeatures & VK_FORMAT_FEATURE_SAMPLED_IMAGE_YCBCR_CONVERSION_LINEAR_FILTER_BIT) ? VK_FILTER_LINEAR : VK_FILTER_NEAREST;
    if (!rgb) {
        VkSamplerYcbcrConversionCreateInfo conversion{}; conversion.sType = VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_CREATE_INFO;
        conversion.pNext = external ? &externalFormat : nullptr; conversion.format = image.format;
        conversion.ycbcrModel = format.suggestedYcbcrModel; conversion.ycbcrRange = format.suggestedYcbcrRange;
        conversion.components = format.samplerYcbcrConversionComponents;
        conversion.xChromaOffset = format.suggestedXChromaOffset; conversion.yChromaOffset = format.suggestedYChromaOffset;
        conversion.chromaFilter = filter;
        if (source->colorSpace == AVCOL_SPC_BT709) conversion.ycbcrModel = VK_SAMPLER_YCBCR_MODEL_CONVERSION_YCBCR_709;
        else if (source->colorSpace == AVCOL_SPC_SMPTE170M || source->colorSpace == AVCOL_SPC_BT470BG)
            conversion.ycbcrModel = VK_SAMPLER_YCBCR_MODEL_CONVERSION_YCBCR_601;
        if (source->colorRange == AVCOL_RANGE_JPEG) conversion.ycbcrRange = VK_SAMPLER_YCBCR_RANGE_ITU_FULL;
        else if (source->colorRange == AVCOL_RANGE_MPEG) conversion.ycbcrRange = VK_SAMPLER_YCBCR_RANGE_ITU_NARROW;
        result = CreateSamplerYcbcrConversion(context->instance.device, &conversion, nullptr, &resource->conversion);
        if (result != VK_SUCCESS) { error = result; return false; }
        conversionInfo.conversion = resource->conversion;
    }
    VkSamplerCreateInfo sampler{}; sampler.sType = VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO;
    sampler.pNext = rgb ? nullptr : &conversionInfo;
    sampler.magFilter = sampler.minFilter = filter; sampler.mipmapMode = VK_SAMPLER_MIPMAP_MODE_NEAREST;
    sampler.addressModeU = sampler.addressModeV = sampler.addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    sampler.maxLod = 0;
    result = CreateSampler(context->instance.device, &sampler, nullptr, &resource->sampler);
    VkImageViewCreateInfo view{}; view.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO; view.pNext = rgb ? nullptr : &conversionInfo;
    view.image = resource->image; view.viewType = VK_IMAGE_VIEW_TYPE_2D; view.format = image.format;
    view.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
    if (result == VK_SUCCESS) result = CreateImageView(context->instance.device, &view, nullptr, &resource->view);
    if (result != VK_SUCCESS) { error = result; return false; }
    if (source->acquireFence >= 0) {
        VkSemaphoreCreateInfo semaphore{}; semaphore.sType = VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO;
        result = CreateSemaphore(context->instance.device, &semaphore, nullptr, &resource->semaphore);
        if (result != VK_SUCCESS) { error = result; return false; }
        int fd = dup(source->acquireFence);
        if (fd < 0) { error = ImageFailure; return false; }
        VkImportSemaphoreFdInfoKHR fence{}; fence.sType = VK_STRUCTURE_TYPE_IMPORT_SEMAPHORE_FD_INFO_KHR;
        fence.semaphore = resource->semaphore; fence.flags = VK_SEMAPHORE_IMPORT_TEMPORARY_BIT;
        fence.handleType = VK_EXTERNAL_SEMAPHORE_HANDLE_TYPE_SYNC_FD_BIT; fence.fd = fd;
        result = ImportSemaphoreFdKHR(context->instance.device, &fence);
        if (result != VK_SUCCESS) { close(fd); error = result; return false; }
    }
    sample.image[0] = resource->image; sample.view[0] = resource->view; sample.sampler[0] = resource->sampler;
    sample.planeCount = 1; sample.width = source->width; sample.height = source->height;
    // External formats have no image-format descriptor-count query before
    // maintenance6. Reserve four entries, the conservative Android convention.
    sample.combinedDescriptorCount = rgb ? 1 : 4;
    sample.acquireSemaphore = resource->semaphore;
    sample.initialLayout = VK_IMAGE_LAYOUT_GENERAL; sample.foreignQueue = VK_QUEUE_FAMILY_FOREIGN_EXT;
    sample.uvScaleOffset[0] = static_cast<float>(source->crop.right - source->crop.left) / desc.width;
    sample.uvScaleOffset[1] = static_cast<float>(source->crop.bottom - source->crop.top) / desc.height;
    sample.uvScaleOffset[2] = static_cast<float>(source->crop.left) / desc.width;
    sample.uvScaleOffset[3] = static_cast<float>(source->crop.top) / desc.height;
    sample.owner = resource;
    sample.context = context;
    return true;
}
} // namespace

bool FfuAndroidAvailable() { return Api().available; }
FFU_EXPORT int FFU_CALL ffu_android_is_available() { return FfuAndroidAvailable() ? 1 : 0; }
FFU_EXPORT int FFU_CALL ffu_android_set_java_vm(void* javaVm) { return av_jni_set_java_vm(javaVm, nullptr); }
FFU_EXPORT void* FFU_CALL ffu_android_create(int width, int height) {
    if (!Api().available) { Fail(nullptr, UnsupportedApi); return nullptr; }
    if (width <= 0 || height <= 0 || width > 16384 || height > 16384) { Fail(nullptr, InvalidFrame); return nullptr; }
    JniScope jni;
    if (!jni.env) { Fail(nullptr, MissingVm); return nullptr; }
    auto* session = new (std::nothrow) Session();
    if (!session) { Fail(nullptr, AVERROR(ENOMEM)); return nullptr; }
    media_status_t result = Api().AImageReader_newWithUsage_fn(width, height, AIMAGE_FORMAT_PRIVATE,
        AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE, 32, &session->reader);
    if (result == AMEDIA_OK) result = Api().AImageReader_getWindow_fn(session->reader, &session->window);
    if (result != AMEDIA_OK || !session->window) { Fail(nullptr, result ? result : SurfaceFailure); session->Release(); return nullptr; }
    jobject surface = Api().ANativeWindow_toSurface_fn(jni.env, session->window);
    if (surface && !jni.Failed()) session->surface = jni.env->NewGlobalRef(surface);
    if (surface) jni.env->DeleteLocalRef(surface);
    if (!session->surface || jni.Failed()) { Fail(nullptr, SurfaceFailure); session->Release(); return nullptr; }
    lastError = 0;
    return session;
}
FFU_EXPORT AVBufferRef* FFU_CALL ffu_android_device(void* pointer) {
    auto* session = static_cast<Session*>(pointer);
    if (!session) return nullptr;
    AVBufferRef* reference = av_hwdevice_ctx_alloc(AV_HWDEVICE_TYPE_MEDIACODEC);
    if (!reference) { Fail(session, DeviceFailure); return nullptr; }
    auto* device = reinterpret_cast<AVHWDeviceContext*>(reference->data);
    auto* media = static_cast<AVMediaCodecDeviceContext*>(device->hwctx);
    media->surface = session->surface; media->native_window = session->window;
    device->free = FreeDevice; device->user_opaque = session; session->Retain();
    const int result = av_hwdevice_ctx_init(reference);
    if (result < 0) { Fail(session, result); av_buffer_unref(&reference); return nullptr; }
    return reference;
}
FFU_EXPORT void* FFU_CALL ffu_android_capture(void* pointer, const AVFrame* frame, int timeoutMs) {
    auto* session = static_cast<Session*>(pointer);
    if (!session || !frame || frame->format != AV_PIX_FMT_MEDIACODEC || !frame->data[3] ||
        frame->width <= 0 || frame->height <= 0 || frame->width > 16384 || frame->height > 16384) {
        Fail(session, InvalidFrame); return nullptr;
    }
    if (frame->color_trc == AVCOL_TRC_SMPTE2084 || frame->color_trc == AVCOL_TRC_ARIB_STD_B67 ||
        frame->color_primaries == AVCOL_PRI_BT2020 || frame->color_primaries == AVCOL_PRI_SMPTE431 ||
        frame->color_primaries == AVCOL_PRI_SMPTE432 ||
        (frame->colorspace != AVCOL_SPC_UNSPECIFIED && frame->colorspace != AVCOL_SPC_BT709 &&
         frame->colorspace != AVCOL_SPC_BT470BG && frame->colorspace != AVCOL_SPC_SMPTE170M)) {
        Fail(session, UnsupportedColor); return nullptr;
    }
    std::lock_guard<std::mutex> lock(session->io);
    timespec now{}; clock_gettime(CLOCK_MONOTONIC, &now);
    const int64_t tag = std::max<int64_t>(static_cast<int64_t>(now.tv_sec) * 1000000000LL + now.tv_nsec, session->lastTag + 1);
    session->lastTag = tag;
    // A unique surface timestamp disambiguates late images from before a seek.
    // This does not change the media PTS retained by the managed playback clock.
    const int release = av_mediacodec_render_buffer_at_time(reinterpret_cast<AVMediaCodecBuffer*>(frame->data[3]), tag);
    if (release < 0) { Fail(session, release); return nullptr; }
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(std::max(1, std::min(timeoutMs, 500)));
    do {
        AImage* image = nullptr; int fence = -1;
        const media_status_t result = Api().AImageReader_acquireNextImageAsync_fn(session->reader, &image, &fence);
        if (result == AMEDIA_OK && image) {
            int64_t timestamp = 0;
            if (Api().AImage_getTimestamp_fn(image, &timestamp) != AMEDIA_OK || timestamp != tag) {
                DiscardImage(image, fence); continue;
            }
            auto* captured = new (std::nothrow) CapturedImage();
            if (!captured) { DiscardImage(image, fence); Fail(session, AVERROR(ENOMEM)); return nullptr; }
            captured->image = image; captured->acquireFence = fence; captured->session = session; session->Retain();
            captured->width = frame->width; captured->height = frame->height;
            captured->colorSpace = frame->colorspace; captured->colorRange = frame->color_range;
            captured->colorTransfer = frame->color_trc;
            if (Api().AImage_getHardwareBuffer_fn(image, &captured->buffer) != AMEDIA_OK || !captured->buffer ||
                Api().AImage_getCropRect_fn(image, &captured->crop) != AMEDIA_OK) {
                captured->Release(); Fail(session, ImageFailure); return nullptr;
            }
            session->error = 0;
            return captured;
        }
        if (result != AMEDIA_IMGREADER_NO_BUFFER_AVAILABLE && result != AMEDIA_IMGREADER_MAX_IMAGES_ACQUIRED) {
            Fail(session, result); return nullptr;
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    } while (std::chrono::steady_clock::now() < deadline);
    Fail(session, CaptureTimeout);
    return nullptr;
}
FFU_EXPORT void FFU_CALL ffu_android_flush(void* pointer) {
    auto* session = static_cast<Session*>(pointer);
    if (!session) return;
    std::lock_guard<std::mutex> lock(session->io);
    // Bounded drain: already acquired images are independently ref-counted.
    for (int i = 0; i < 32; ++i) {
        AImage* image = nullptr; int fence = -1;
        if (Api().AImageReader_acquireNextImageAsync_fn(session->reader, &image, &fence) != AMEDIA_OK || !image) break;
        DiscardImage(image, fence);
    }
    session->error = 0;
}
FFU_EXPORT void FFU_CALL ffu_android_image_release(void* pointer) { if (pointer) static_cast<CapturedImage*>(pointer)->Release(); }
FFU_EXPORT void FFU_CALL ffu_android_release(void* pointer) { if (pointer) static_cast<Session*>(pointer)->Release(); }
FFU_EXPORT int FFU_CALL ffu_android_error(void* pointer) { return pointer ? static_cast<Session*>(pointer)->error.load() : lastError.load(); }
FFU_EXPORT void* FFU_CALL ffu_android_prepare(void* presenter, void* pointer, void* unityTexture) {
    auto* source = static_cast<CapturedImage*>(pointer);
    if (!source || !presenter || !unityTexture) return nullptr;
    auto context = FfuVkCurrent();
    if (!context) { FfuVkPresenterSetError(presenter, VulkanUnavailable); return nullptr; }
    FfuVkSample sample{}; int error = 0;
    if (!Import(context, source, sample, error)) { FfuVkPresenterSetError(presenter, error); return nullptr; }
    return FfuVkPrepare(presenter, sample, unityTexture, source->colorRange == AVCOL_RANGE_JPEG,
        source->colorSpace == AVCOL_SPC_BT709 || (source->colorSpace == AVCOL_SPC_UNSPECIFIED && source->height >= 720));
}
#endif
