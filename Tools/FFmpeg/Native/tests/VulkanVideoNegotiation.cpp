// Deterministic negotiation/lifetime tests. These model driver capabilities and
// verify the bridge protocol; they do not claim hardware video decode coverage.
#include "../VulkanVideoDecode.cpp"
#include <cstdio>
#include <cstdlib>
#include <chrono>
#include <future>
#include <thread>
#ifdef _WIN32
#include "../VulkanInterop.h"
#endif

int FfuEventId(int event) { return event; }
#define REQUIRE(value) do { if (!(value)) { std::printf("FAIL line %d: %s\n", __LINE__, #value); std::exit(1); } } while (0)
namespace {
void FreeProfile(AVHWFramesContext* frames) {
    REQUIRE(frames->free == FreeProfile);
    auto* count = static_cast<int*>(frames->user_opaque); ++*count;
}
void VerifyFrameGate() {
    AVHWDeviceContext device{}; device.type = AV_HWDEVICE_TYPE_VULKAN;
    AVVulkanFramesContext pool{};
    AVHWFramesContext frames{}; frames.device_ctx = &device; frames.hwctx = &pool;
    int profileFreed = 0; frames.user_opaque = &profileFreed; frames.free = FreeProfile;
    AVBufferRef reference{}; reference.data = reinterpret_cast<uint8_t*>(&frames);
    REQUIRE(ffu_vulkan_video_configure_frames(nullptr) < 0);
    REQUIRE(ffu_vulkan_video_configure_frames(&reference) == 0);
    REQUIRE(frames.user_opaque == &profileFreed);
    REQUIRE(ffu_vulkan_video_configure_frames(&reference) < 0); // never replace a live gate
    auto* gate = GetFrameGate(&frames, &pool); REQUIRE(gate);
    std::promise<void> entered, release;
    auto enteredFuture = entered.get_future(); auto releaseFuture = release.get_future();
    std::thread decoder([&] {
        pool.lock_frame(&frames, nullptr); pool.lock_frame(&frames, nullptr);
        entered.set_value(); releaseFuture.wait();
        pool.unlock_frame(&frames, nullptr); pool.unlock_frame(&frames, nullptr);
    });
    REQUIRE(enteredFuture.wait_for(std::chrono::seconds(1)) == std::future_status::ready);
    {
        FrameOwner owner; owner.context = std::make_shared<FfuVkContext>(); owner.gate = gate;
        FfuVkSample sample;
        const auto start = std::chrono::steady_clock::now();
        for (int i = 0; i < 1000; ++i) REQUIRE(!owner.Lock(sample) && sample.pending && !owner.locked);
        REQUIRE(std::chrono::steady_clock::now() - start < std::chrono::milliseconds(100));
        owner.gate = nullptr; sample.pending = false;
        REQUIRE(!owner.Lock(sample) && sample.pending); // foreign pool never calls its blocking callback
    }
    release.set_value(); decoder.join();
    REQUIRE(gate->mutex.try_lock()); gate->mutex.unlock();
    frames.free(&frames);
    REQUIRE(profileFreed == 1 && frames.user_opaque == &profileFreed && frames.free == FreeProfile && frameGates.empty());
    std::puts("PASS: public Vulkan frame gate, recursive DPB locks, 1000 renderer contention skips without GPU submission, foreign-pool rejection");
}
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

#ifdef _WIN32
namespace {
UnityVulkanInitCallback startupInterceptor = nullptr;
IUnityGraphicsVulkanV2 startupGraphics{};
VkResult acquireResult = VK_SUCCESS;
bool acquisitionAvailable = true;
int acquireCalls = 0;
int reloadedAcquireCalls = 0;
const VkSwapchainKHR testSwapchain = (VkSwapchainKHR)(uintptr_t(5));
const VkSemaphore testSemaphore = (VkSemaphore)(uintptr_t(6));
const VkFence testFence = (VkFence)(uintptr_t(7));
const VkAcquireNextImageInfoKHR* expectedAcquireInfo = nullptr;
VKAPI_ATTR VkResult VKAPI_CALL TestAcquire(VkDevice device, VkSwapchainKHR swapchain, uint64_t timeout,
    VkSemaphore semaphore, VkFence fence, uint32_t* index) {
    REQUIRE(device == testDevice && swapchain == testSwapchain && timeout == UINT64_MAX);
    REQUIRE(semaphore == testSemaphore && fence == testFence);
    ++acquireCalls;
    if (acquireResult == VK_SUCCESS || acquireResult == VK_SUBOPTIMAL_KHR) *index = 2;
    return acquireResult;
}
VKAPI_ATTR VkResult VKAPI_CALL TestAcquire2(VkDevice device, const VkAcquireNextImageInfoKHR* info, uint32_t* index) {
    REQUIRE(info == expectedAcquireInfo && info->sType == VK_STRUCTURE_TYPE_ACQUIRE_NEXT_IMAGE_INFO_KHR);
    REQUIRE(info->pNext == nullptr && info->deviceMask == 5);
    return TestAcquire(device, info->swapchain, info->timeout, info->semaphore, info->fence, index);
}
VKAPI_ATTR VkResult VKAPI_CALL TestPresent(VkQueue, const VkPresentInfoKHR*) { return VK_SUBOPTIMAL_KHR; }
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL StartupDeviceProc(VkDevice device, const char* name) {
    REQUIRE(device == testDevice);
    if (!std::strcmp(name, "vkAcquireNextImageKHR")) return acquisitionAvailable ? reinterpret_cast<PFN_vkVoidFunction>(TestAcquire) : nullptr;
    if (!std::strcmp(name, "vkAcquireNextImage2KHR")) return acquisitionAvailable ? reinterpret_cast<PFN_vkVoidFunction>(TestAcquire2) : nullptr;
    if (!std::strcmp(name, "vkQueuePresentKHR")) return reinterpret_cast<PFN_vkVoidFunction>(TestPresent);
    return TestDeviceProc(device, name);
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL StartupLoader(VkInstance, const char* name) {
    if (!std::strcmp(name, "vkGetDeviceProcAddr")) return reinterpret_cast<PFN_vkVoidFunction>(StartupDeviceProc);
    if (!std::strcmp(name, "vkAcquireNextImageKHR")) return acquisitionAvailable ? reinterpret_cast<PFN_vkVoidFunction>(TestAcquire) : nullptr;
    if (!std::strcmp(name, "vkAcquireNextImage2KHR")) return acquisitionAvailable ? reinterpret_cast<PFN_vkVoidFunction>(TestAcquire2) : nullptr;
    if (!std::strcmp(name, "vkQueuePresentKHR")) return reinterpret_cast<PFN_vkVoidFunction>(TestPresent);
    return TestLoader(testInstance, name);
}
VKAPI_ATTR VkResult VKAPI_CALL ReloadedAcquire(VkDevice device, VkSwapchainKHR swapchain, uint64_t timeout,
    VkSemaphore semaphore, VkFence fence, uint32_t* index) {
    ++reloadedAcquireCalls;
    return TestAcquire(device, swapchain, timeout, semaphore, fence, index);
}
VKAPI_ATTR VkResult VKAPI_CALL ReloadedAcquire2(VkDevice device, const VkAcquireNextImageInfoKHR* info, uint32_t* index) {
    ++reloadedAcquireCalls;
    return TestAcquire2(device, info, index);
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL ReloadedDeviceProc(VkDevice device, const char* name) {
    if (!std::strcmp(name, "vkAcquireNextImageKHR")) return reinterpret_cast<PFN_vkVoidFunction>(ReloadedAcquire);
    if (!std::strcmp(name, "vkAcquireNextImage2KHR")) return reinterpret_cast<PFN_vkVoidFunction>(ReloadedAcquire2);
    return StartupDeviceProc(device, name);
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL ReloadedLoader(VkInstance instance, const char* name) {
    if (!std::strcmp(name, "vkGetDeviceProcAddr")) return reinterpret_cast<PFN_vkVoidFunction>(ReloadedDeviceProc);
    if (!std::strcmp(name, "vkAcquireNextImageKHR")) return reinterpret_cast<PFN_vkVoidFunction>(ReloadedAcquire);
    if (!std::strcmp(name, "vkAcquireNextImage2KHR")) return reinterpret_cast<PFN_vkVoidFunction>(ReloadedAcquire2);
    return StartupLoader(instance, name);
}
bool UNITY_INTERFACE_API InterceptStartup(UnityVulkanInitCallback callback, void*) { startupInterceptor = callback; return true; }
IUnityInterface* UNITY_INTERFACE_API StartupInterface(unsigned long long high, unsigned long long low) {
    const auto id = GetUnityInterfaceGUID<IUnityGraphicsVulkanV2>();
    return high == id.m_GUIDHigh && low == id.m_GUIDLow ? &startupGraphics : nullptr;
}
void VerifyStartupAcquisition() {
    startupGraphics.InterceptInitialization = InterceptStartup;
    IUnityInterfaces interfaces{}; interfaces.GetInterfaceSplit = StartupInterface;
    FfuVulkanPreload(&interfaces); REQUIRE(startupInterceptor);
    const auto loader = startupInterceptor(StartupLoader, nullptr); REQUIRE(loader);
    const auto deviceProc = reinterpret_cast<PFN_vkGetDeviceProcAddr>(loader(testInstance, "vkGetDeviceProcAddr")); REQUIRE(deviceProc);
    const VkResult results[] = {VK_SUBOPTIMAL_KHR, VK_SUCCESS, VK_TIMEOUT, VK_NOT_READY,
        VK_ERROR_OUT_OF_DATE_KHR, VK_ERROR_DEVICE_LOST, VK_ERROR_SURFACE_LOST_KHR,
        VK_ERROR_OUT_OF_HOST_MEMORY, VK_ERROR_OUT_OF_DEVICE_MEMORY};
    VkAcquireNextImageInfoKHR info{VK_STRUCTURE_TYPE_ACQUIRE_NEXT_IMAGE_INFO_KHR};
    info.swapchain = testSwapchain; info.timeout = UINT64_MAX;
    info.semaphore = testSemaphore; info.fence = testFence; info.deviceMask = 5;
    expectedAcquireInfo = &info;
    for (int lookup = 0; lookup < 2; ++lookup) {
        auto get = [&](const char* name) { return lookup == 0 ? loader(testInstance, name) : deviceProc(testDevice, name); };
        auto acquire = reinterpret_cast<PFN_vkAcquireNextImageKHR>(get("vkAcquireNextImageKHR")); REQUIRE(acquire);
        auto acquire2 = reinterpret_cast<PFN_vkAcquireNextImage2KHR>(get("vkAcquireNextImage2KHR")); REQUIRE(acquire2);
        REQUIRE(get("vkQueuePresentKHR") == reinterpret_cast<PFN_vkVoidFunction>(TestPresent));
        REQUIRE(!get("vkMissingTestFunction"));
        for (const VkResult result : results) {
            acquireResult = result;
            const VkResult expected = result == VK_SUBOPTIMAL_KHR ? VK_SUCCESS : result;
            const uint32_t expectedIndex = result == VK_SUCCESS || result == VK_SUBOPTIMAL_KHR ? 2 : UINT32_MAX;
            uint32_t index = UINT32_MAX; const int calls = acquireCalls;
            REQUIRE(acquire(testDevice, testSwapchain, UINT64_MAX, testSemaphore, testFence, &index) == expected);
            REQUIRE(index == expectedIndex && acquireCalls == calls + 1);
            index = UINT32_MAX;
            REQUIRE(acquire2(testDevice, &info, &index) == expected);
            REQUIRE(index == expectedIndex && acquireCalls == calls + 2);
        }
        acquisitionAvailable = false;
        REQUIRE(!get("vkAcquireNextImageKHR") && !get("vkAcquireNextImage2KHR"));
        acquisitionAvailable = true;
    }
    REQUIRE(DecodeProc(testInstance, "vkAcquireNextImageKHR") == reinterpret_cast<PFN_vkVoidFunction>(TestAcquire));
    REQUIRE(DecodeProc(testInstance, "vkAcquireNextImage2KHR") == reinterpret_cast<PFN_vkVoidFunction>(TestAcquire2));
    REQUIRE(loader(testInstance, "vkDestroyInstance") == reinterpret_cast<PFN_vkVoidFunction>(DestroyInstance));
    REQUIRE(deviceProc(testDevice, "vkDestroyDevice") == reinterpret_cast<PFN_vkVoidFunction>(DestroyDevice));
    // A fresh Unity Vulkan initialization must refresh both lookup routes;
    // cached interception wrappers must call the new loader's entry points.
    const auto reloaded = startupInterceptor(ReloadedLoader, nullptr);
    const auto reloadedDevice = reinterpret_cast<PFN_vkGetDeviceProcAddr>(reloaded(testInstance, "vkGetDeviceProcAddr")); REQUIRE(reloadedDevice);
    acquireResult = VK_SUBOPTIMAL_KHR;
    for (int lookup = 0; lookup < 2; ++lookup) {
        auto get = [&](const char* name) { return lookup == 0 ? reloaded(testInstance, name) : reloadedDevice(testDevice, name); };
        auto acquire = reinterpret_cast<PFN_vkAcquireNextImageKHR>(get("vkAcquireNextImageKHR")); REQUIRE(acquire);
        auto acquire2 = reinterpret_cast<PFN_vkAcquireNextImage2KHR>(get("vkAcquireNextImage2KHR")); REQUIRE(acquire2);
        uint32_t index = UINT32_MAX;
        REQUIRE(acquire(testDevice, testSwapchain, UINT64_MAX, testSemaphore, testFence, &index) == VK_SUCCESS && index == 2);
        index = UINT32_MAX;
        REQUIRE(acquire2(testDevice, &info, &index) == VK_SUCCESS && index == 2);
    }
    REQUIRE(reloadedAcquireCalls == 4);
    std::puts("PASS: Windows Unity instance/device acquisition defers SUBOPTIMAL rebuild, preserves images/synchronization/errors/presentation, FFmpeg bypass and destruction interception");
}
}
#endif

int main() {
    VerifyFrameGate();
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
#ifdef _WIN32
    VerifyStartupAcquisition();
#endif
    std::puts("PASS: Vulkan negotiation fallback, feature preservation, private queue remapping, deferred device and instance destruction through real FFmpeg buffer references");
}
