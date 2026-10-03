#include "Bridge.h"
#include "VulkanVideoDecode.h"
extern "C" {
#include <libavutil/hwcontext.h>
#include <libavutil/hwcontext_vulkan.h>
}
#include <algorithm>
#include <cstring>
#include <map>
#include <new>
#include <string>
#include <vector>

namespace {
// Unity and FFmpeg share images, but never submit from different threads to the
// same VkQueue. FFmpeg sees queue index zero, remapped to a queue reserved while
// Unity's device is being created. Its queue mutexes are shared between players.
struct Device {
    VkInstance instance = VK_NULL_HANDLE;
    VkPhysicalDevice physical = VK_NULL_HANDLE;
    VkDevice device = VK_NULL_HANDLE;
    PFN_vkGetInstanceProcAddr loader = nullptr;
    PFN_vkGetDeviceProcAddr deviceProc = nullptr;
    VkPhysicalDeviceFeatures features{};
    uint32_t computeFamily = UINT32_MAX, decodeFamily = UINT32_MAX;
    uint32_t computeIndex = 0, decodeIndex = 0;
    VkQueueFlags computeFlags = 0;
    VkVideoCodecOperationFlagsKHR codecs = 0;
    std::vector<std::string> extensionStorage, instanceExtensionStorage;
    std::vector<const char*> extensions, instanceExtensions;
    std::recursive_mutex queues;
    int owners = 0; // protected by devicesMutex
    bool destroyRequested = false, hasAllocator = false;
    std::atomic<bool> destroyed{false};
    VkAllocationCallbacks allocator{};
};
struct InstanceLifetime {
    PFN_vkDestroyInstance destroy = nullptr;
    int devices = 0;
    bool destroyRequested = false, hasAllocator = false;
    VkAllocationCallbacks allocator{};
};
std::mutex devicesMutex;
std::map<VkDevice, std::shared_ptr<Device>> devices;
std::atomic<int> videoStatus{400};
std::atomic<PFN_vkGetInstanceProcAddr> instanceLoader{nullptr};
std::atomic<PFN_vkGetDeviceProcAddr> unityDeviceProc{nullptr};
PFN_vkCreateInstance createInstance = nullptr;
std::map<VkInstance, uint32_t> instanceVersions;
std::map<VkInstance, std::vector<std::string>> instanceExtensions;
std::map<VkInstance, InstanceLifetime> instances;

void DestroyInstanceIfUnused(VkInstance instance) {
    InstanceLifetime lifetime;
    {
        std::lock_guard<std::mutex> lock(devicesMutex);
        const auto it = instances.find(instance);
        if (it == instances.end() || !it->second.destroyRequested || it->second.devices) return;
        lifetime = it->second;
        instances.erase(it); instanceVersions.erase(instance); instanceExtensions.erase(instance);
    }
    lifetime.destroy(instance, lifetime.hasAllocator ? &lifetime.allocator : nullptr);
}
void DestroyDeviceIfUnused(const std::shared_ptr<Device>& state) {
    {
        std::lock_guard<std::mutex> lock(devicesMutex);
        if (!state->destroyRequested || state->owners || state->destroyed) return;
        state->destroyed = true; devices.erase(state->device);
    }
    const auto destroy = reinterpret_cast<PFN_vkDestroyDevice>(state->deviceProc(state->device, "vkDestroyDevice"));
    destroy(state->device, state->hasAllocator ? &state->allocator : nullptr);
    {
        std::lock_guard<std::mutex> lock(devicesMutex);
        const auto it = instances.find(state->instance);
        if (it != instances.end()) --it->second.devices;
    }
    DestroyInstanceIfUnused(state->instance);
}

std::shared_ptr<Device> Find(VkDevice device) {
    std::lock_guard<std::mutex> lock(devicesMutex);
    const auto it = devices.find(device);
    return it == devices.end() ? nullptr : it->second;
}
VKAPI_ATTR VkResult VKAPI_CALL CreateInstance(const VkInstanceCreateInfo* original,
    const VkAllocationCallbacks* allocation, VkInstance* instance) {
    VkApplicationInfo app{};
    if (original->pApplicationInfo) app = *original->pApplicationInfo;
    app.sType = VK_STRUCTURE_TYPE_APPLICATION_INFO;
    uint32_t version = VK_API_VERSION_1_0;
    const auto enumerate = reinterpret_cast<PFN_vkEnumerateInstanceVersion>(instanceLoader.load()(VK_NULL_HANDLE, "vkEnumerateInstanceVersion"));
    if (enumerate) enumerate(&version);
    const uint32_t requested = app.apiVersion ? app.apiVersion : VK_API_VERSION_1_0;
    if (version >= VK_API_VERSION_1_3) app.apiVersion = std::max(requested, uint32_t(VK_API_VERSION_1_3));
    VkInstanceCreateInfo info = *original; info.pApplicationInfo = &app;
    VkResult result = createInstance(&info, allocation, instance);
    if (result != VK_SUCCESS && app.apiVersion != requested) {
        app.apiVersion = requested;
        result = createInstance(original, allocation, instance);
    }
    if (result == VK_SUCCESS) {
        std::lock_guard<std::mutex> lock(devicesMutex);
        instanceVersions[*instance] = app.apiVersion ? app.apiVersion : VK_API_VERSION_1_0;
        instances[*instance].destroy = reinterpret_cast<PFN_vkDestroyInstance>(instanceLoader.load()(*instance, "vkDestroyInstance"));
        auto& names = instanceExtensions[*instance]; names.clear();
        for (uint32_t i = 0; i < original->enabledExtensionCount; ++i) names.emplace_back(original->ppEnabledExtensionNames[i]);
    }
    return result;
}

// Copy Unity's chain before changing any enabled feature. Unknown extension
// structures are left untouched by declining this optional hardware backend.
// Existing Unity functionality and the legacy GPU import routes remain usable.
size_t StructureSize(VkStructureType type) {
#define FEATURE(name, tag) case tag: return sizeof(name)
    switch (type) {
    FEATURE(VkPhysicalDeviceFeatures2, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2);
    FEATURE(VkPhysicalDeviceVulkan11Features, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_1_FEATURES);
    FEATURE(VkPhysicalDeviceVulkan12Features, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES);
    FEATURE(VkPhysicalDeviceVulkan13Features, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES);
    FEATURE(VkPhysicalDeviceTimelineSemaphoreFeatures, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TIMELINE_SEMAPHORE_FEATURES);
    FEATURE(VkPhysicalDeviceSynchronization2Features, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SYNCHRONIZATION_2_FEATURES);
    FEATURE(VkPhysicalDeviceSamplerYcbcrConversionFeatures, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES);
    FEATURE(VkPhysicalDeviceDynamicRenderingFeatures, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DYNAMIC_RENDERING_FEATURES);
    FEATURE(VkPhysicalDeviceMaintenance4Features, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_MAINTENANCE_4_FEATURES);
    FEATURE(VkPhysicalDeviceBufferDeviceAddressFeatures, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_BUFFER_DEVICE_ADDRESS_FEATURES);
    FEATURE(VkPhysicalDeviceDescriptorIndexingFeatures, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DESCRIPTOR_INDEXING_FEATURES);
    FEATURE(VkPhysicalDeviceMultiviewFeatures, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_MULTIVIEW_FEATURES);
    FEATURE(VkPhysicalDevice16BitStorageFeatures, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_16BIT_STORAGE_FEATURES);
    FEATURE(VkPhysicalDevice8BitStorageFeatures, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_8BIT_STORAGE_FEATURES);
    FEATURE(VkPhysicalDeviceShaderFloat16Int8Features, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SHADER_FLOAT16_INT8_FEATURES);
    FEATURE(VkPhysicalDeviceScalarBlockLayoutFeatures, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SCALAR_BLOCK_LAYOUT_FEATURES);
    FEATURE(VkPhysicalDeviceImagelessFramebufferFeatures, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_IMAGELESS_FRAMEBUFFER_FEATURES);
    FEATURE(VkPhysicalDeviceUniformBufferStandardLayoutFeatures, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_UNIFORM_BUFFER_STANDARD_LAYOUT_FEATURES);
    FEATURE(VkPhysicalDeviceHostQueryResetFeatures, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_HOST_QUERY_RESET_FEATURES);
    FEATURE(VkPhysicalDeviceSeparateDepthStencilLayoutsFeatures, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SEPARATE_DEPTH_STENCIL_LAYOUTS_FEATURES);
    FEATURE(VkPhysicalDeviceVulkanMemoryModelFeatures, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_MEMORY_MODEL_FEATURES);
    FEATURE(VkPhysicalDeviceShaderDrawParametersFeatures, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SHADER_DRAW_PARAMETERS_FEATURES);
    FEATURE(VkPhysicalDeviceRobustness2FeaturesEXT, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_ROBUSTNESS_2_FEATURES_EXT);
    FEATURE(VkPhysicalDeviceExtendedDynamicStateFeaturesEXT, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_EXTENDED_DYNAMIC_STATE_FEATURES_EXT);
    FEATURE(VkPhysicalDeviceExtendedDynamicState2FeaturesEXT, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_EXTENDED_DYNAMIC_STATE_2_FEATURES_EXT);
    FEATURE(VkPhysicalDeviceExtendedDynamicState3FeaturesEXT, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_EXTENDED_DYNAMIC_STATE_3_FEATURES_EXT);
    FEATURE(VkPhysicalDeviceCustomBorderColorFeaturesEXT, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_CUSTOM_BORDER_COLOR_FEATURES_EXT);
    FEATURE(VkPhysicalDeviceFragmentShadingRateFeaturesKHR, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FRAGMENT_SHADING_RATE_FEATURES_KHR);
    default: return 0;
    }
#undef FEATURE
}
struct FeatureChain {
    std::vector<std::vector<uint64_t>> storage;
    VkPhysicalDeviceTimelineSemaphoreFeatures timeline{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TIMELINE_SEMAPHORE_FEATURES, nullptr, VK_TRUE};
    VkPhysicalDeviceSynchronization2Features sync{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SYNCHRONIZATION_2_FEATURES, nullptr, VK_TRUE};
    VkPhysicalDeviceSamplerYcbcrConversionFeatures ycbcr{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES, nullptr, VK_TRUE};
    void* head = nullptr;
    bool Copy(const void* source, VkPhysicalDeviceFeatures& features) {
        bool hasTimeline = false, hasSync = false, hasYcbcr = false;
        for (auto* node = static_cast<const VkBaseInStructure*>(source); node; node = node->pNext) {
            const size_t size = StructureSize(node->sType);
            if (!size) return false;
            storage.emplace_back((size + sizeof(uint64_t) - 1) / sizeof(uint64_t));
            std::memcpy(storage.back().data(), node, size);
        }
        for (auto it = storage.rbegin(); it != storage.rend(); ++it) {
            auto* node = reinterpret_cast<VkBaseOutStructure*>(it->data());
            node->pNext = static_cast<VkBaseOutStructure*>(head); head = node;
            switch (node->sType) {
            case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2:
                features = reinterpret_cast<VkPhysicalDeviceFeatures2*>(node)->features; break;
            case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_1_FEATURES:
                reinterpret_cast<VkPhysicalDeviceVulkan11Features*>(node)->samplerYcbcrConversion = VK_TRUE; hasYcbcr = true; break;
            case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES:
                reinterpret_cast<VkPhysicalDeviceVulkan12Features*>(node)->timelineSemaphore = VK_TRUE; hasTimeline = true; break;
            case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES:
                reinterpret_cast<VkPhysicalDeviceVulkan13Features*>(node)->synchronization2 = VK_TRUE; hasSync = true; break;
            case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TIMELINE_SEMAPHORE_FEATURES:
                reinterpret_cast<VkPhysicalDeviceTimelineSemaphoreFeatures*>(node)->timelineSemaphore = VK_TRUE; hasTimeline = true; break;
            case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SYNCHRONIZATION_2_FEATURES:
                reinterpret_cast<VkPhysicalDeviceSynchronization2Features*>(node)->synchronization2 = VK_TRUE; hasSync = true; break;
            case VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES:
                reinterpret_cast<VkPhysicalDeviceSamplerYcbcrConversionFeatures*>(node)->samplerYcbcrConversion = VK_TRUE; hasYcbcr = true; break;
            default: break;
            }
        }
        if (!hasTimeline) { timeline.pNext = head; head = &timeline; }
        if (!hasSync) { sync.pNext = head; head = &sync; }
        if (!hasYcbcr) { ycbcr.pNext = head; head = &ycbcr; }
        return true;
    }
};

VKAPI_ATTR void VKAPI_CALL GetDeviceQueue(VkDevice device, uint32_t family, uint32_t index, VkQueue* queue) {
    auto state = Find(device);
    if (!state) { *queue = VK_NULL_HANDLE; return; }
    if (family == state->computeFamily) index += state->computeIndex;
    else if (family == state->decodeFamily) index += state->decodeIndex;
    const auto function = reinterpret_cast<PFN_vkGetDeviceQueue>(state->deviceProc(device, "vkGetDeviceQueue"));
    function(device, family, index, queue);
}
VKAPI_ATTR void VKAPI_CALL GetDeviceQueue2(VkDevice device, const VkDeviceQueueInfo2* info, VkQueue* queue) {
    auto state = Find(device);
    if (!state) { *queue = VK_NULL_HANDLE; return; }
    VkDeviceQueueInfo2 copy = *info;
    if (copy.queueFamilyIndex == state->computeFamily) copy.queueIndex += state->computeIndex;
    else if (copy.queueFamilyIndex == state->decodeFamily) copy.queueIndex += state->decodeIndex;
    const auto function = reinterpret_cast<PFN_vkGetDeviceQueue2>(state->deviceProc(device, "vkGetDeviceQueue2"));
    function(device, &copy, queue);
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL DeviceProc(VkDevice device, const char* name) {
    if (!std::strcmp(name, "vkGetDeviceQueue")) return reinterpret_cast<PFN_vkVoidFunction>(GetDeviceQueue);
    if (!std::strcmp(name, "vkGetDeviceQueue2")) return reinterpret_cast<PFN_vkVoidFunction>(GetDeviceQueue2);
    auto state = Find(device);
    return state ? state->deviceProc(device, name) : nullptr;
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL DecodeProc(VkInstance instance, const char* name) {
    if (!std::strcmp(name, "vkGetDeviceProcAddr")) return reinterpret_cast<PFN_vkVoidFunction>(DeviceProc);
    if (!std::strcmp(name, "vkGetDeviceQueue")) return reinterpret_cast<PFN_vkVoidFunction>(GetDeviceQueue);
    if (!std::strcmp(name, "vkGetDeviceQueue2")) return reinterpret_cast<PFN_vkVoidFunction>(GetDeviceQueue2);
    const auto loader = instanceLoader.load();
    return loader ? loader(instance, name) : nullptr;
}
VKAPI_ATTR void VKAPI_CALL DestroyDevice(VkDevice device, const VkAllocationCallbacks* allocation) {
    auto state = Find(device);
    if (state) {
        {
            std::lock_guard<std::mutex> lock(devicesMutex);
            state->destroyRequested = true; state->hasAllocator = allocation != nullptr;
            if (allocation) state->allocator = *allocation;
        }
        DestroyDeviceIfUnused(state);
        return;
    }
    const auto proc = state ? state->deviceProc : unityDeviceProc.load();
    const auto destroy = proc ? reinterpret_cast<PFN_vkDestroyDevice>(proc(device, "vkDestroyDevice")) : nullptr;
    if (destroy) destroy(device, allocation);
}
VKAPI_ATTR void VKAPI_CALL DestroyInstance(VkInstance instance, const VkAllocationCallbacks* allocation) {
    {
        std::lock_guard<std::mutex> lock(devicesMutex);
        auto it = instances.find(instance);
        if (it != instances.end()) {
            it->second.destroyRequested = true; it->second.hasAllocator = allocation != nullptr;
            if (allocation) it->second.allocator = *allocation;
        } else {
            const auto destroy = reinterpret_cast<PFN_vkDestroyInstance>(instanceLoader.load()(instance, "vkDestroyInstance"));
            destroy(instance, allocation); return;
        }
    }
    DestroyInstanceIfUnused(instance);
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL UnityDeviceProc(VkDevice device, const char* name) {
    if (!std::strcmp(name, "vkDestroyDevice")) return reinterpret_cast<PFN_vkVoidFunction>(DestroyDevice);
    const auto proc = unityDeviceProc.load();
    return proc ? proc(device, name) : nullptr;
}
struct DeviceOwner {
    std::shared_ptr<Device> device;
    std::shared_ptr<FfuVkContext> context;
    VkPhysicalDeviceTimelineSemaphoreFeatures timeline{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TIMELINE_SEMAPHORE_FEATURES, nullptr, VK_TRUE};
    VkPhysicalDeviceSynchronization2Features sync{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SYNCHRONIZATION_2_FEATURES, nullptr, VK_TRUE};
    VkPhysicalDeviceSamplerYcbcrConversionFeatures ycbcr{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES, nullptr, VK_TRUE};
};
void FreeDevice(AVHWDeviceContext* context) {
    auto* owner = static_cast<DeviceOwner*>(context->user_opaque);
    const auto state = owner->device;
    delete owner;
    {
        std::lock_guard<std::mutex> lock(devicesMutex);
        --state->owners;
    }
    DestroyDeviceIfUnused(state);
}
#if FF_API_VULKAN_SYNC_QUEUES
void LockQueue(AVHWDeviceContext* context, uint32_t, uint32_t) { static_cast<DeviceOwner*>(context->user_opaque)->device->queues.lock(); }
void UnlockQueue(AVHWDeviceContext* context, uint32_t, uint32_t) { static_cast<DeviceOwner*>(context->user_opaque)->device->queues.unlock(); }
#endif
} // namespace

PFN_vkVoidFunction FfuVulkanVideoInstanceProc(PFN_vkGetInstanceProcAddr loader, VkInstance instance, const char* name) {
    instanceLoader = loader;
    auto function = loader(instance, name);
    if (!std::strcmp(name, "vkCreateInstance") && function) {
        createInstance = reinterpret_cast<PFN_vkCreateInstance>(function);
        return reinterpret_cast<PFN_vkVoidFunction>(CreateInstance);
    }
    if (!std::strcmp(name, "vkGetDeviceProcAddr") && function) {
        unityDeviceProc = reinterpret_cast<PFN_vkGetDeviceProcAddr>(function);
        return reinterpret_cast<PFN_vkVoidFunction>(UnityDeviceProc);
    }
    if (!std::strcmp(name, "vkDestroyDevice") && function) return reinterpret_cast<PFN_vkVoidFunction>(DestroyDevice);
    if (!std::strcmp(name, "vkDestroyInstance") && function) return reinterpret_cast<PFN_vkVoidFunction>(DestroyInstance);
    return function;
}

VkResult FfuVulkanVideoCreateDevice(PFN_vkCreateDevice create, PFN_vkGetInstanceProcAddr loader, VkInstance instance,
    VkPhysicalDevice physical, const VkDeviceCreateInfo* original, const VkAllocationCallbacks* allocation, VkDevice* device) {
    auto fallback = [&](int reason) { videoStatus = reason; return create(physical, original, allocation, device); };
    unityDeviceProc = reinterpret_cast<PFN_vkGetDeviceProcAddr>(loader(instance, "vkGetDeviceProcAddr"));
    if (!unityDeviceProc.load()) return fallback(402);
    uint32_t apiVersion = 0;
    {
        std::lock_guard<std::mutex> lock(devicesMutex);
        const auto it = instanceVersions.find(instance);
        if (it != instanceVersions.end()) apiVersion = it->second;
    }
    if (apiVersion < VK_API_VERSION_1_3) return fallback(401);
    const auto properties = reinterpret_cast<PFN_vkGetPhysicalDeviceProperties>(loader(instance, "vkGetPhysicalDeviceProperties"));
    const auto enumerate = reinterpret_cast<PFN_vkEnumerateDeviceExtensionProperties>(loader(instance, "vkEnumerateDeviceExtensionProperties"));
    const auto queryFeatures = reinterpret_cast<PFN_vkGetPhysicalDeviceFeatures2>(loader(instance, "vkGetPhysicalDeviceFeatures2"));
    const auto queryQueues = reinterpret_cast<PFN_vkGetPhysicalDeviceQueueFamilyProperties2>(loader(instance, "vkGetPhysicalDeviceQueueFamilyProperties2"));
    if (!properties || !enumerate || !queryFeatures || !queryQueues) return fallback(402);
    VkPhysicalDeviceProperties props{}; properties(physical, &props);
    if (props.apiVersion < VK_API_VERSION_1_3) return fallback(401);
    uint32_t count = 0;
    if (enumerate(physical, nullptr, &count, nullptr) != VK_SUCCESS) return fallback(402);
    std::vector<VkExtensionProperties> available(count);
    if (enumerate(physical, nullptr, &count, available.data()) != VK_SUCCESS) return fallback(402);
    auto has = [&](const char* name) { return std::any_of(available.begin(), available.end(), [&](const VkExtensionProperties& e) { return !std::strcmp(e.extensionName, name); }); };
    if (!has(VK_KHR_VIDEO_QUEUE_EXTENSION_NAME) || !has(VK_KHR_VIDEO_DECODE_QUEUE_EXTENSION_NAME)) return fallback(403);
    VkPhysicalDeviceSamplerYcbcrConversionFeatures ycbcr{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES};
    VkPhysicalDeviceTimelineSemaphoreFeatures timeline{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TIMELINE_SEMAPHORE_FEATURES, &ycbcr};
    VkPhysicalDeviceSynchronization2Features sync{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SYNCHRONIZATION_2_FEATURES, &timeline};
    VkPhysicalDeviceFeatures2 supported{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2, &sync}; queryFeatures(physical, &supported);
    if (!timeline.timelineSemaphore || !sync.synchronization2 || !ycbcr.samplerYcbcrConversion) return fallback(404);
    auto state = std::make_shared<Device>(); state->loader = loader; state->physical = physical; state->instance = instance;
    if (original->pEnabledFeatures) state->features = *original->pEnabledFeatures;
    FeatureChain features;
    if (!features.Copy(original->pNext, state->features)) return fallback(405);
    queryQueues(physical, &count, nullptr);
    std::vector<VkQueueFamilyProperties2> queues(count);
    std::vector<VkQueueFamilyVideoPropertiesKHR> video(count);
    for (uint32_t i = 0; i < count; ++i) {
        video[i].sType = VK_STRUCTURE_TYPE_QUEUE_FAMILY_VIDEO_PROPERTIES_KHR;
        queues[i].sType = VK_STRUCTURE_TYPE_QUEUE_FAMILY_PROPERTIES_2; queues[i].pNext = &video[i];
    }
    queryQueues(physical, &count, queues.data());
    std::vector<VkDeviceQueueCreateInfo> requested(original->pQueueCreateInfos, original->pQueueCreateInfos + original->queueCreateInfoCount);
    std::vector<std::vector<float>> priorities;
    priorities.reserve(requested.size() + 1);
    for (auto& queue : requested) priorities.emplace_back(queue.pQueuePriorities, queue.pQueuePriorities + queue.queueCount);
    // A spare queue in Unity's graphics family provides FFmpeg compute/transfer
    // operations and also makes its output images concurrent with Unity.
    for (uint32_t i = 0; i < requested.size(); ++i) {
        auto& request = requested[i];
        if (request.queueFamilyIndex >= count || request.flags) continue;
        const auto& props = queues[request.queueFamilyIndex].queueFamilyProperties;
        if ((props.queueFlags & (VK_QUEUE_GRAPHICS_BIT | VK_QUEUE_COMPUTE_BIT)) != (VK_QUEUE_GRAPHICS_BIT | VK_QUEUE_COMPUTE_BIT) ||
            request.queueCount >= props.queueCount) continue;
        state->computeFamily = request.queueFamilyIndex; state->computeIndex = request.queueCount;
        // Transfer commands are mandatory on graphics/compute queues even when
        // the driver omits the optional TRANSFER bit in queueFlags. FFmpeg's
        // queue selector requires that capability to be represented explicitly.
        state->computeFlags = (props.queueFlags & (VK_QUEUE_GRAPHICS_BIT | VK_QUEUE_COMPUTE_BIT)) | VK_QUEUE_TRANSFER_BIT;
        priorities[i].push_back(1.0f); ++request.queueCount;
        break;
    }
    if (state->computeFamily == UINT32_MAX) return fallback(406);
    VkVideoCodecOperationFlagsKHR codecs = 0;
    struct CodecExtension { const char* name; VkVideoCodecOperationFlagBitsKHR flag; };
    const CodecExtension codecExtensions[] = {
        {VK_KHR_VIDEO_DECODE_H264_EXTENSION_NAME, VK_VIDEO_CODEC_OPERATION_DECODE_H264_BIT_KHR},
        {VK_KHR_VIDEO_DECODE_H265_EXTENSION_NAME, VK_VIDEO_CODEC_OPERATION_DECODE_H265_BIT_KHR},
#ifdef VK_KHR_video_decode_av1
        {VK_KHR_VIDEO_DECODE_AV1_EXTENSION_NAME, VK_VIDEO_CODEC_OPERATION_DECODE_AV1_BIT_KHR},
#endif
#ifdef VK_KHR_video_decode_vp9
        {VK_KHR_VIDEO_DECODE_VP9_EXTENSION_NAME, VK_VIDEO_CODEC_OPERATION_DECODE_VP9_BIT_KHR},
#endif
    };
    for (const auto& codec : codecExtensions) if (has(codec.name)) codecs |= codec.flag;
    if (!codecs) return fallback(407);
    for (uint32_t family = 0; family < count; ++family) {
        if (!(queues[family].queueFamilyProperties.queueFlags & VK_QUEUE_VIDEO_DECODE_BIT_KHR) || !(video[family].videoCodecOperations & codecs)) continue;
        auto it = std::find_if(requested.begin(), requested.end(), [&](const VkDeviceQueueCreateInfo& q) { return q.queueFamilyIndex == family; });
        if (family == state->computeFamily) {
            state->decodeIndex = state->computeIndex;
        } else if (it != requested.end()) {
            if (it->flags || it->queueCount >= queues[family].queueFamilyProperties.queueCount) continue;
            state->decodeIndex = it->queueCount; ++it->queueCount;
            priorities[static_cast<size_t>(it - requested.begin())].push_back(1.0f);
        } else {
            VkDeviceQueueCreateInfo request{VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO};
            request.queueFamilyIndex = family; request.queueCount = 1;
            requested.push_back(request); priorities.push_back({1.0f}); state->decodeIndex = 0;
        }
        state->decodeFamily = family; state->codecs = video[family].videoCodecOperations & codecs;
        break;
    }
    if (state->decodeFamily == UINT32_MAX) return fallback(407);
    for (uint32_t i = 0; i < requested.size(); ++i) requested[i].pQueuePriorities = priorities[i].data();
    for (uint32_t i = 0; i < original->enabledExtensionCount; ++i) state->extensionStorage.emplace_back(original->ppEnabledExtensionNames[i]);
    auto add = [&](const char* name) {
        if (std::find(state->extensionStorage.begin(), state->extensionStorage.end(), name) == state->extensionStorage.end()) state->extensionStorage.emplace_back(name);
    };
    add(VK_KHR_VIDEO_QUEUE_EXTENSION_NAME); add(VK_KHR_VIDEO_DECODE_QUEUE_EXTENSION_NAME);
    for (const auto& codec : codecExtensions) if (state->codecs & codec.flag) add(codec.name);
    for (const auto& name : state->extensionStorage) state->extensions.push_back(name.c_str());
    VkDeviceCreateInfo info = *original; info.pNext = features.head;
    info.queueCreateInfoCount = static_cast<uint32_t>(requested.size()); info.pQueueCreateInfos = requested.data();
    info.enabledExtensionCount = static_cast<uint32_t>(state->extensions.size()); info.ppEnabledExtensionNames = state->extensions.data();
    const VkResult result = create(physical, &info, allocation, device);
    if (result != VK_SUCCESS) return fallback(408);
    state->device = *device; state->deviceProc = reinterpret_cast<PFN_vkGetDeviceProcAddr>(loader(instance, "vkGetDeviceProcAddr"));
    {
        std::lock_guard<std::mutex> lock(devicesMutex);
        state->instanceExtensionStorage = instanceExtensions[instance];
        for (const auto& name : state->instanceExtensionStorage) state->instanceExtensions.push_back(name.c_str());
        devices[*device] = state;
        ++instances[instance].devices;
    }
    videoStatus = 0;
    return result;
}

bool FfuVulkanVideoAvailable() {
    auto context = FfuVkCurrent();
    if (!context || !context->active) return false;
    const auto state = Find(context->instance.device);
    return state && state->computeFamily == context->instance.queueFamilyIndex;
}
int FfuVulkanVideoStatus() { return videoStatus.load(); }
FFU_EXPORT int FFU_CALL ffu_vulkan_video_status() { return FfuVulkanVideoStatus(); }
FFU_EXPORT void* FFU_CALL ffu_vulkan_video_acquire_device() {
    auto context = FfuVkCurrent();
    if (!context || !context->active) return nullptr;
    std::lock_guard<std::recursive_mutex> resources(context->resources);
    auto state = Find(context->instance.device);
    if (!state || !context->active || state->computeFamily != context->instance.queueFamilyIndex) return nullptr;
    AVBufferRef* result = av_hwdevice_ctx_alloc(AV_HWDEVICE_TYPE_VULKAN);
    if (!result) { videoStatus = 409; return nullptr; }
    auto* owner = new (std::nothrow) DeviceOwner();
    if (!owner) { av_buffer_unref(&result); return nullptr; }
    {
        std::lock_guard<std::mutex> lock(devicesMutex);
        if (state->destroyRequested) { delete owner; av_buffer_unref(&result); return nullptr; }
        ++state->owners;
    }
    owner->device = state; owner->context = context; owner->timeline.pNext = &owner->sync; owner->sync.pNext = &owner->ycbcr;
    auto* device = reinterpret_cast<AVHWDeviceContext*>(result->data);
    device->user_opaque = owner; device->free = FreeDevice;
    auto* vk = static_cast<AVVulkanDeviceContext*>(device->hwctx);
    vk->inst = state->instance; vk->phys_dev = state->physical; vk->act_dev = state->device; vk->get_proc_addr = DecodeProc;
    vk->device_features.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2;
    vk->device_features.features = state->features; vk->device_features.pNext = &owner->timeline;
    vk->enabled_dev_extensions = state->extensions.data(); vk->nb_enabled_dev_extensions = static_cast<int>(state->extensions.size());
    vk->enabled_inst_extensions = state->instanceExtensions.data(); vk->nb_enabled_inst_extensions = static_cast<int>(state->instanceExtensions.size());
    auto& compute = vk->qf[vk->nb_qf++]; compute.idx = static_cast<int>(state->computeFamily); compute.num = 1;
    compute.flags = static_cast<VkQueueFlagBits>(state->computeFlags);
    if (state->decodeFamily == state->computeFamily) {
        compute.flags = static_cast<VkQueueFlagBits>(compute.flags | VK_QUEUE_VIDEO_DECODE_BIT_KHR);
        compute.video_caps = static_cast<VkVideoCodecOperationFlagBitsKHR>(state->codecs);
    } else {
        auto& decode = vk->qf[vk->nb_qf++]; decode.idx = static_cast<int>(state->decodeFamily); decode.num = 1;
        decode.flags = VK_QUEUE_VIDEO_DECODE_BIT_KHR; decode.video_caps = static_cast<VkVideoCodecOperationFlagBitsKHR>(state->codecs);
    }
#if FF_API_VULKAN_SYNC_QUEUES
    vk->lock_queue = LockQueue; vk->unlock_queue = UnlockQueue;
#endif
    const int error = av_hwdevice_ctx_init(result);
    if (error < 0) { videoStatus = error; av_buffer_unref(&result); return nullptr; }
    return result;
}

namespace {
struct FrameOwner {
    std::shared_ptr<FfuVkContext> context;
    std::shared_ptr<Device> device;
    AVFrame* frame = nullptr;
    AVHWFramesContext* frames = nullptr;
    AVVulkanFramesContext* pool = nullptr;
    AVVkFrame* image = nullptr;
    VkImageView view = VK_NULL_HANDLE;
    VkSampler sampler = VK_NULL_HANDLE;
    VkSamplerYcbcrConversion conversion = VK_NULL_HANDLE;
    bool locked = false;
    uint64_t signalValue = 0;
    PFN_vkDestroyImageView destroyView = nullptr;
    PFN_vkDestroySampler destroySampler = nullptr;
    PFN_vkDestroySamplerYcbcrConversion destroyConversion = nullptr;
    ~FrameOwner() {
        if (locked) pool->unlock_frame(frames, image);
        std::lock_guard<std::recursive_mutex> resources(context->resources);
        if (!device || !device->destroyed) {
            if (view) destroyView(context->instance.device, view, nullptr);
            if (sampler) destroySampler(context->instance.device, sampler, nullptr);
            if (conversion) destroyConversion(context->instance.device, conversion, nullptr);
            av_frame_free(&frame);
        }
        // The AVFrame holds an AVHWDeviceContext reference, so Unity's device
        // destruction remains deferred until this FFmpeg pool teardown finishes.
    }
    bool Lock(FfuVkSample& sample) {
        pool->lock_frame(frames, image); locked = true;
        if (!image->sem[0] || image->sem_value[0] == UINT64_MAX || image->layout[0] == VK_IMAGE_LAYOUT_UNDEFINED ||
            image->layout[0] == VK_IMAGE_LAYOUT_PREINITIALIZED || (image->queue_family[0] != VK_QUEUE_FAMILY_IGNORED &&
            image->queue_family[0] != context->instance.queueFamilyIndex)) {
            pool->unlock_frame(frames, image); locked = false; return false;
        }
        sample.initialLayout = image->layout[0]; sample.foreignQueue = VK_QUEUE_FAMILY_IGNORED;
        sample.acquireSemaphore = image->sem[0]; sample.timeline = true;
        sample.waitValue = image->sem_value[0]; sample.signalValue = signalValue = sample.waitValue + 1;
        return true;
    }
    void Unlock(bool submitted) {
        if (!locked) return;
        if (submitted) {
            image->sem_value[0] = signalValue;
            // The compute pass restores the original decode layout. Its read
            // completes before this timeline value, which future FFmpeg submits
            // must wait on before decoding into or referencing this image.
            image->access[0] = 0;
        }
        pool->unlock_frame(frames, image); locked = false;
    }
};
}

FFU_EXPORT void* FFU_CALL ffu_vulkan_video_prepare(void* presenter, const AVFrame* frame, void* target) {
    auto context = FfuVkCurrent();
    if (!presenter || !target || !context || !frame || frame->width <= 0 || frame->height <= 0 ||
        frame->format != AV_PIX_FMT_VULKAN || !frame->hw_frames_ctx || !frame->data[0]) return nullptr;
    std::lock_guard<std::recursive_mutex> resources(context->resources);
    if (!context->active) return nullptr;
    auto fail = [&](int error) -> void* { FfuVkPresenterSetError(presenter, error); return nullptr; };
    if (frame->crop_left || frame->crop_top || frame->color_trc == AVCOL_TRC_SMPTE2084 || frame->color_trc == AVCOL_TRC_ARIB_STD_B67 ||
        (frame->colorspace != AVCOL_SPC_UNSPECIFIED && frame->colorspace != AVCOL_SPC_BT709 &&
         frame->colorspace != AVCOL_SPC_SMPTE170M && frame->colorspace != AVCOL_SPC_BT470BG)) return fail(412);
    auto owner = std::make_shared<FrameOwner>(); owner->context = context; owner->device = Find(context->instance.device);
    owner->frame = av_frame_clone(frame);
    if (!owner->frame) return fail(410);
    owner->frames = reinterpret_cast<AVHWFramesContext*>(owner->frame->hw_frames_ctx->data);
    if (!owner->frames->device_ref) return fail(411);
    auto* device = reinterpret_cast<AVHWDeviceContext*>(owner->frames->device_ref->data);
    if (device->type != AV_HWDEVICE_TYPE_VULKAN || static_cast<AVVulkanDeviceContext*>(device->hwctx)->act_dev != context->instance.device) return fail(411);
    owner->pool = static_cast<AVVulkanFramesContext*>(owner->frames->hwctx);
    owner->image = reinterpret_cast<AVVkFrame*>(owner->frame->data[0]);
    if (!owner->pool->lock_frame || !owner->pool->unlock_frame || !owner->image->img[0] || owner->image->img[1] || owner->pool->nb_layers > 1) return fail(412);
    const VkFormat format = owner->pool->format[0];
    if (format != VK_FORMAT_G8_B8R8_2PLANE_420_UNORM && format != VK_FORMAT_G10X6_B10X6R10X6_2PLANE_420_UNORM_3PACK16 &&
        format != VK_FORMAT_G16_B16R16_2PLANE_420_UNORM) return fail(412);
    auto proc = [&](const char* name) { return context->Proc(name); };
    const auto createView = reinterpret_cast<PFN_vkCreateImageView>(proc("vkCreateImageView"));
    const auto createSampler = reinterpret_cast<PFN_vkCreateSampler>(proc("vkCreateSampler"));
    const auto createConversion = reinterpret_cast<PFN_vkCreateSamplerYcbcrConversion>(proc("vkCreateSamplerYcbcrConversion"));
    owner->destroyView = reinterpret_cast<PFN_vkDestroyImageView>(proc("vkDestroyImageView"));
    owner->destroySampler = reinterpret_cast<PFN_vkDestroySampler>(proc("vkDestroySampler"));
    owner->destroyConversion = reinterpret_cast<PFN_vkDestroySamplerYcbcrConversion>(proc("vkDestroySamplerYcbcrConversion"));
    const auto getFormat = reinterpret_cast<PFN_vkGetPhysicalDeviceFormatProperties>(context->InstanceProc("vkGetPhysicalDeviceFormatProperties"));
    const auto getImageFormat = reinterpret_cast<PFN_vkGetPhysicalDeviceImageFormatProperties2>(context->InstanceProc("vkGetPhysicalDeviceImageFormatProperties2"));
    if (!createView || !createSampler || !createConversion || !owner->destroyView || !owner->destroySampler || !owner->destroyConversion || !getFormat || !getImageFormat) return fail(413);
    VkFormatProperties properties{}; getFormat(context->instance.physicalDevice, format, &properties);
    const VkFormatFeatureFlags flags = owner->image->tiling == VK_IMAGE_TILING_LINEAR ? properties.linearTilingFeatures : properties.optimalTilingFeatures;
    if (!(flags & VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT)) return fail(414);
    VkSamplerYcbcrConversionImageFormatProperties conversionProperties{VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_IMAGE_FORMAT_PROPERTIES};
    VkImageFormatProperties2 imageProperties{VK_STRUCTURE_TYPE_IMAGE_FORMAT_PROPERTIES_2, &conversionProperties};
    VkPhysicalDeviceImageFormatInfo2 imageInfo{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_IMAGE_FORMAT_INFO_2};
    imageInfo.format = format; imageInfo.type = VK_IMAGE_TYPE_2D; imageInfo.tiling = owner->image->tiling; imageInfo.usage = VK_IMAGE_USAGE_SAMPLED_BIT;
    if (getImageFormat(context->instance.physicalDevice, &imageInfo, &imageProperties) != VK_SUCCESS || !conversionProperties.combinedImageSamplerDescriptorCount) return fail(414);
    VkSamplerYcbcrConversionCreateInfo conversion{VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_CREATE_INFO};
    conversion.format = format;
    conversion.ycbcrModel = frame->colorspace == AVCOL_SPC_BT709 ||
        (frame->colorspace == AVCOL_SPC_UNSPECIFIED && frame->height > 576) ? VK_SAMPLER_YCBCR_MODEL_CONVERSION_YCBCR_709 : VK_SAMPLER_YCBCR_MODEL_CONVERSION_YCBCR_601;
    conversion.ycbcrRange = frame->color_range == AVCOL_RANGE_JPEG ? VK_SAMPLER_YCBCR_RANGE_ITU_FULL : VK_SAMPLER_YCBCR_RANGE_ITU_NARROW;
    conversion.components = {VK_COMPONENT_SWIZZLE_IDENTITY, VK_COMPONENT_SWIZZLE_IDENTITY, VK_COMPONENT_SWIZZLE_IDENTITY, VK_COMPONENT_SWIZZLE_IDENTITY};
    auto offset = [&](bool cosited) {
        if (cosited && (flags & VK_FORMAT_FEATURE_COSITED_CHROMA_SAMPLES_BIT)) return VK_CHROMA_LOCATION_COSITED_EVEN;
        if (flags & VK_FORMAT_FEATURE_MIDPOINT_CHROMA_SAMPLES_BIT) return VK_CHROMA_LOCATION_MIDPOINT;
        return VK_CHROMA_LOCATION_COSITED_EVEN;
    };
    if (!(flags & (VK_FORMAT_FEATURE_COSITED_CHROMA_SAMPLES_BIT | VK_FORMAT_FEATURE_MIDPOINT_CHROMA_SAMPLES_BIT))) return fail(414);
    conversion.xChromaOffset = offset(frame->chroma_location == AVCHROMA_LOC_LEFT || frame->chroma_location == AVCHROMA_LOC_TOPLEFT);
    conversion.yChromaOffset = offset(frame->chroma_location == AVCHROMA_LOC_TOPLEFT || frame->chroma_location == AVCHROMA_LOC_TOP);
    conversion.chromaFilter = VK_FILTER_NEAREST;
    VkResult result = createConversion(context->instance.device, &conversion, nullptr, &owner->conversion);
    if (result != VK_SUCCESS) return fail(result);
    VkSamplerYcbcrConversionInfo converted{VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_INFO, nullptr, owner->conversion};
    VkSamplerCreateInfo sampler{VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO, &converted};
    sampler.magFilter = sampler.minFilter = VK_FILTER_NEAREST;
    sampler.mipmapMode = VK_SAMPLER_MIPMAP_MODE_NEAREST;
    sampler.addressModeU = sampler.addressModeV = sampler.addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    result = createSampler(context->instance.device, &sampler, nullptr, &owner->sampler);
    if (result != VK_SUCCESS) return fail(result);
    VkImageViewCreateInfo view{VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO, &converted};
    view.image = owner->image->img[0]; view.viewType = VK_IMAGE_VIEW_TYPE_2D; view.format = format;
    view.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
    result = createView(context->instance.device, &view, nullptr, &owner->view);
    if (result != VK_SUCCESS) return fail(result);
    FfuVkSample sample;
    sample.context = context; sample.image[0] = owner->image->img[0]; sample.view[0] = owner->view; sample.sampler[0] = owner->sampler;
    sample.planeCount = 1; sample.combinedDescriptorCount = conversionProperties.combinedImageSamplerDescriptorCount;
    sample.width = static_cast<uint32_t>(frame->width); sample.height = static_cast<uint32_t>(frame->height);
    if (owner->frames->width < frame->width || owner->frames->height < frame->height) return fail(415);
    sample.uvScaleOffset[0] = float(frame->width) / owner->frames->width;
    sample.uvScaleOffset[1] = float(frame->height) / owner->frames->height;
    sample.owner = owner;
    sample.lock = [owner](FfuVkSample& value) { return owner->Lock(value); };
    sample.unlock = [owner](bool submitted) { owner->Unlock(submitted); };
    return FfuVkPrepare(presenter, sample, target, false, false);
}

#if defined(_WIN32)
// Linux and Android expose the same portable presenter ABI in VulkanPlatform.
FFU_EXPORT void* FFU_CALL ffu_vulkan_create() { return FfuVkPresenterCreate(); }
FFU_EXPORT void FFU_CALL ffu_vulkan_release(void* presenter) { FfuVkPresenterRelease(presenter); }
FFU_EXPORT int FFU_CALL ffu_vulkan_error(void* presenter) { return FfuVkPresenterError(presenter); }
FFU_EXPORT void FFU_CALL ffu_vulkan_cancel(void* packet) { FfuVkCancel(packet); }
FFU_EXPORT int FFU_CALL ffu_vulkan_poll() { FfuVkPoll(false); return FfuVkPendingCount(); }
#endif
