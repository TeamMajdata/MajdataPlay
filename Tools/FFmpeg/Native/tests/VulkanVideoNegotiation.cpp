// Deterministic negotiation/lifetime tests. These model driver capabilities and
// verify the bridge protocol; they do not claim hardware video decode coverage.
#include "../VulkanVideoDecode.cpp"
#include <cstdio>
#include <cstdlib>

int FfuEventId(int event) { return event; }
#define REQUIRE(value) do { if (!(value)) { std::printf("FAIL line %d: %s\n", __LINE__, #value); std::exit(1); } } while (0)
namespace {
int deviceCreates = 0, deviceDestroys = 0, instanceDestroys = 0;
bool videoExtensions = true, privateQueue = true, rejectVideo = false;
uint32_t obtainedQueueIndex = UINT32_MAX, obtainedQueueFamily = UINT32_MAX;
const VkInstance testInstance = reinterpret_cast<VkInstance>(uintptr_t(1));
const VkDevice testDevice = reinterpret_cast<VkDevice>(uintptr_t(2));
const VkPhysicalDevice testPhysical = reinterpret_cast<VkPhysicalDevice>(uintptr_t(3));
VKAPI_ATTR void VKAPI_CALL TestDestroyDevice(VkDevice device, const VkAllocationCallbacks*) { REQUIRE(device == testDevice); ++deviceDestroys; }
VKAPI_ATTR void VKAPI_CALL TestDestroyInstance(VkInstance instance, const VkAllocationCallbacks*) { REQUIRE(instance == testInstance); ++instanceDestroys; }
VKAPI_ATTR void VKAPI_CALL TestGetQueue(VkDevice, uint32_t family, uint32_t index, VkQueue* queue) {
    obtainedQueueFamily = family; obtainedQueueIndex = index; *queue = reinterpret_cast<VkQueue>(uintptr_t(4));
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL TestDeviceProc(VkDevice, const char* name) {
    if (!std::strcmp(name, "vkDestroyDevice")) return reinterpret_cast<PFN_vkVoidFunction>(TestDestroyDevice);
    if (!std::strcmp(name, "vkGetDeviceQueue")) return reinterpret_cast<PFN_vkVoidFunction>(TestGetQueue);
    return nullptr;
}
VKAPI_ATTR void VKAPI_CALL TestProperties(VkPhysicalDevice, VkPhysicalDeviceProperties* p) { *p = {}; p->apiVersion = VK_API_VERSION_1_3; }
VKAPI_ATTR VkResult VKAPI_CALL TestExtensions(VkPhysicalDevice, const char*, uint32_t* count, VkExtensionProperties* p) {
    const char* names[] = {VK_KHR_VIDEO_QUEUE_EXTENSION_NAME, VK_KHR_VIDEO_DECODE_QUEUE_EXTENSION_NAME, VK_KHR_VIDEO_DECODE_H264_EXTENSION_NAME};
    if (!p) { *count = videoExtensions ? 3 : 0; return VK_SUCCESS; }
    for (uint32_t i = 0; i < *count; ++i) { p[i] = {}; std::strcpy(p[i].extensionName, names[i]); }
    return VK_SUCCESS;
}
VKAPI_ATTR void VKAPI_CALL TestFeatures(VkPhysicalDevice, VkPhysicalDeviceFeatures2* p) {
    for (auto* item = static_cast<VkBaseOutStructure*>(p->pNext); item; item = item->pNext) {
        if (item->sType == VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TIMELINE_SEMAPHORE_FEATURES) reinterpret_cast<VkPhysicalDeviceTimelineSemaphoreFeatures*>(item)->timelineSemaphore = VK_TRUE;
        if (item->sType == VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SYNCHRONIZATION_2_FEATURES) reinterpret_cast<VkPhysicalDeviceSynchronization2Features*>(item)->synchronization2 = VK_TRUE;
        if (item->sType == VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES) reinterpret_cast<VkPhysicalDeviceSamplerYcbcrConversionFeatures*>(item)->samplerYcbcrConversion = VK_TRUE;
    }
}
VKAPI_ATTR void VKAPI_CALL TestQueues(VkPhysicalDevice, uint32_t* count, VkQueueFamilyProperties2* p) {
    if (!p) { *count = 2; return; }
    // Reporting TRANSFER separately is optional for graphics/compute families.
    p[0].queueFamilyProperties.queueFlags = VK_QUEUE_GRAPHICS_BIT | VK_QUEUE_COMPUTE_BIT;
    p[0].queueFamilyProperties.queueCount = privateQueue ? 2 : 1;
    p[1].queueFamilyProperties.queueFlags = VK_QUEUE_VIDEO_DECODE_BIT_KHR; p[1].queueFamilyProperties.queueCount = 1;
    static_cast<VkQueueFamilyVideoPropertiesKHR*>(p[0].pNext)->videoCodecOperations = 0;
    static_cast<VkQueueFamilyVideoPropertiesKHR*>(p[1].pNext)->videoCodecOperations = VK_VIDEO_CODEC_OPERATION_DECODE_H264_BIT_KHR;
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL TestLoader(VkInstance, const char* name) {
#define FUNCTION(text, function) if (!std::strcmp(name, text)) return reinterpret_cast<PFN_vkVoidFunction>(function)
    FUNCTION("vkGetDeviceProcAddr", TestDeviceProc);
    FUNCTION("vkGetPhysicalDeviceProperties", TestProperties);
    FUNCTION("vkEnumerateDeviceExtensionProperties", TestExtensions);
    FUNCTION("vkGetPhysicalDeviceFeatures2", TestFeatures);
    FUNCTION("vkGetPhysicalDeviceQueueFamilyProperties2", TestQueues);
    FUNCTION("vkDestroyInstance", TestDestroyInstance);
#undef FUNCTION
    return nullptr;
}
VKAPI_ATTR VkResult VKAPI_CALL TestCreateDevice(VkPhysicalDevice, const VkDeviceCreateInfo* info, const VkAllocationCallbacks*, VkDevice* device) {
    ++deviceCreates;
    if (info->enabledExtensionCount) {
        REQUIRE(info->enabledExtensionCount == 3);
        REQUIRE(info->queueCreateInfoCount == 2);
        REQUIRE(info->pQueueCreateInfos[0].queueCount == 2);
        REQUIRE(info->pQueueCreateInfos[1].queueCount == 1);
        bool timeline = false, sync = false, ycbcr = false;
        for (auto* item = static_cast<const VkBaseInStructure*>(info->pNext); item; item = item->pNext) {
            if (item->sType == VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES) {
                auto* value = reinterpret_cast<const VkPhysicalDeviceVulkan12Features*>(item);
                timeline = value->timelineSemaphore; REQUIRE(value->drawIndirectCount);
            }
            if (item->sType == VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SYNCHRONIZATION_2_FEATURES) sync = reinterpret_cast<const VkPhysicalDeviceSynchronization2Features*>(item)->synchronization2;
            if (item->sType == VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES) ycbcr = reinterpret_cast<const VkPhysicalDeviceSamplerYcbcrConversionFeatures*>(item)->samplerYcbcrConversion;
        }
        REQUIRE(timeline && sync && ycbcr);
        if (rejectVideo) return VK_ERROR_FEATURE_NOT_PRESENT;
    }
    *device = testDevice; return VK_SUCCESS;
}
}

int main() {
    instanceLoader = TestLoader;
    instances[testInstance].destroy = TestDestroyInstance;
    instanceVersions[testInstance] = VK_API_VERSION_1_2;
    const float priority = 0.5f;
    VkDeviceQueueCreateInfo queue{VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO};
    queue.queueCount = 1; queue.pQueuePriorities = &priority;
    VkPhysicalDeviceVulkan12Features features{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES};
    features.drawIndirectCount = VK_TRUE;
    VkDeviceCreateInfo info{VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO}; info.pNext = &features;
    info.queueCreateInfoCount = 1; info.pQueueCreateInfos = &queue;
    VkDevice device;
    auto create = [&]() { REQUIRE(FfuVulkanVideoCreateDevice(TestCreateDevice, TestLoader, testInstance, testPhysical, &info, nullptr, &device) == VK_SUCCESS); };
    create(); REQUIRE(FfuVulkanVideoStatus() == 401 && devices.empty());
    instanceVersions[testInstance] = VK_API_VERSION_1_3; videoExtensions = false;
    create(); REQUIRE(FfuVulkanVideoStatus() == 403 && devices.empty());
    videoExtensions = true; privateQueue = false;
    create(); REQUIRE(FfuVulkanVideoStatus() == 406 && devices.empty());
    privateQueue = true; rejectVideo = true; const int beforeRetry = deviceCreates;
    create(); REQUIRE(FfuVulkanVideoStatus() == 408 && devices.empty() && deviceCreates == beforeRetry + 2);
    rejectVideo = false;
    create(); REQUIRE(FfuVulkanVideoStatus() == 0 && devices.size() == 1);
    REQUIRE(Find(device)->computeFlags & VK_QUEUE_TRANSFER_BIT);
    REQUIRE(!features.timelineSemaphore && features.drawIndirectCount && queue.queueCount == 1); // borrowed Unity memory unchanged
    VkQueue obtained;
    GetDeviceQueue(device, 0, 0, &obtained); REQUIRE(obtainedQueueFamily == 0 && obtainedQueueIndex == 1);
    GetDeviceQueue(device, 1, 0, &obtained); REQUIRE(obtainedQueueFamily == 1 && obtainedQueueIndex == 0);
    AVBufferRef* deviceReference = av_hwdevice_ctx_alloc(AV_HWDEVICE_TYPE_VULKAN); REQUIRE(deviceReference);
    auto state = Find(device); state->owners = 1;
    auto* owner = new DeviceOwner(); owner->device = state;
    auto* avDevice = reinterpret_cast<AVHWDeviceContext*>(deviceReference->data);
    avDevice->user_opaque = owner; avDevice->free = FreeDevice;
    AVBufferRef* heldFrameDeviceReference = av_buffer_ref(deviceReference); REQUIRE(heldFrameDeviceReference);
    DestroyDevice(device, nullptr); DestroyInstance(testInstance, nullptr);
    REQUIRE(deviceDestroys == 0 && instanceDestroys == 0);
    av_buffer_unref(&deviceReference); REQUIRE(deviceDestroys == 0 && instanceDestroys == 0);
    av_buffer_unref(&heldFrameDeviceReference);
    REQUIRE(deviceDestroys == 1 && instanceDestroys == 1 && devices.empty() && instances.empty());
    std::puts("PASS: Vulkan negotiation fallback, feature preservation, private queue remapping, deferred device and instance destruction through real FFmpeg buffer references");
}
