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
        // Presentation is wrapped for present pacing; without a tracked swapchain it forwards unchanged.
        auto present = reinterpret_cast<PFN_vkQueuePresentKHR>(get("vkQueuePresentKHR")); REQUIRE(present);
        VkPresentInfoKHR presentInfo{VK_STRUCTURE_TYPE_PRESENT_INFO_KHR};
        presentInfo.swapchainCount = 1; presentInfo.pSwapchains = &testSwapchain;
        REQUIRE(present(reinterpret_cast<VkQueue>(uintptr_t(4)), &presentInfo) == VK_SUBOPTIMAL_KHR);
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

// Present pacing: a driver model with VK_KHR_present_id / VK_KHR_present_wait.
const VkDevice presentTestDevice = reinterpret_cast<VkDevice>(uintptr_t(8));
const VkSwapchainKHR presentTestSwapchain = (VkSwapchainKHR)(uintptr_t(9));
const VkSwapchainKHR otherTestSwapchain = (VkSwapchainKHR)(uintptr_t(10));
bool presentFeatures = false, rejectPresentFeatures = false;
int presentDeviceCreates = 0;
bool lastCreateHadPresentWait = false;
uint64_t lastPresentedId = 0, lastWaitedId = 0, lastWaitTimeout = UINT64_MAX;
int presentCalls = 0, waitCalls = 0;
VkResult presentResult = VK_SUCCESS, waitResult = VK_TIMEOUT;
std::promise<void>* acquireEntered = nullptr;
std::shared_future<void> acquireRelease;
VKAPI_ATTR VkResult VKAPI_CALL PresentExtensions(VkPhysicalDevice, const char*, uint32_t* count, VkExtensionProperties* p) {
    const char* names[] = {VK_KHR_PRESENT_ID_EXTENSION_NAME, VK_KHR_PRESENT_WAIT_EXTENSION_NAME};
    if (!p) { *count = 2; return VK_SUCCESS; }
    for (uint32_t i = 0; i < *count; ++i) { p[i] = {}; std::strcpy(p[i].extensionName, names[i]); }
    return VK_SUCCESS;
}
VKAPI_ATTR void VKAPI_CALL PresentFeatures(VkPhysicalDevice, VkPhysicalDeviceFeatures2* p) {
    for (auto* item = static_cast<VkBaseOutStructure*>(p->pNext); item; item = item->pNext) {
        if (item->sType == VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PRESENT_ID_FEATURES_KHR)
            reinterpret_cast<VkPhysicalDevicePresentIdFeaturesKHR*>(item)->presentId = presentFeatures;
        if (item->sType == VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PRESENT_WAIT_FEATURES_KHR)
            reinterpret_cast<VkPhysicalDevicePresentWaitFeaturesKHR*>(item)->presentWait = presentFeatures;
    }
}
VKAPI_ATTR VkResult VKAPI_CALL PresentCreateDevice(VkPhysicalDevice, const VkDeviceCreateInfo* info, const VkAllocationCallbacks*, VkDevice* device) {
    ++presentDeviceCreates;
    bool id = false, wait = false, idExtension = false, waitExtension = false;
    for (auto* item = static_cast<const VkBaseInStructure*>(info->pNext); item; item = item->pNext) {
        if (item->sType == VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PRESENT_ID_FEATURES_KHR)
            id = reinterpret_cast<const VkPhysicalDevicePresentIdFeaturesKHR*>(item)->presentId;
        if (item->sType == VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PRESENT_WAIT_FEATURES_KHR)
            wait = reinterpret_cast<const VkPhysicalDevicePresentWaitFeaturesKHR*>(item)->presentWait;
    }
    for (uint32_t i = 0; i < info->enabledExtensionCount; ++i) {
        idExtension |= !std::strcmp(info->ppEnabledExtensionNames[i], VK_KHR_PRESENT_ID_EXTENSION_NAME);
        waitExtension |= !std::strcmp(info->ppEnabledExtensionNames[i], VK_KHR_PRESENT_WAIT_EXTENSION_NAME);
    }
    REQUIRE(id == wait && id == idExtension && id == waitExtension); // features and extensions travel together
    lastCreateHadPresentWait = id;
    if (id && rejectPresentFeatures) return VK_ERROR_FEATURE_NOT_PRESENT;
    *device = presentTestDevice; return VK_SUCCESS;
}
VKAPI_ATTR VkResult VKAPI_CALL PresentCreateSwapchain(VkDevice device, const VkSwapchainCreateInfoKHR*, const VkAllocationCallbacks*, VkSwapchainKHR* swapchain) {
    REQUIRE(device == presentTestDevice); *swapchain = presentTestSwapchain; return VK_SUCCESS;
}
VKAPI_ATTR void VKAPI_CALL PresentDestroySwapchain(VkDevice device, VkSwapchainKHR, const VkAllocationCallbacks*) { REQUIRE(device == presentTestDevice); }
VKAPI_ATTR VkResult VKAPI_CALL PresentQueue(VkQueue, const VkPresentInfoKHR* info) {
    ++presentCalls; lastPresentedId = 0;
    for (auto* item = static_cast<const VkBaseInStructure*>(info->pNext); item; item = item->pNext) {
        if (item->sType == VK_STRUCTURE_TYPE_PRESENT_ID_KHR) {
            auto* ids = reinterpret_cast<const VkPresentIdKHR*>(item);
            REQUIRE(ids->swapchainCount == info->swapchainCount);
            for (uint32_t i = 0; i < ids->swapchainCount; ++i)
                if (info->pSwapchains[i] == presentTestSwapchain) lastPresentedId = ids->pPresentIds[i];
                else REQUIRE(ids->pPresentIds[i] == 0);
        }
    }
    return presentResult;
}
VKAPI_ATTR VkResult VKAPI_CALL PresentWait(VkDevice device, VkSwapchainKHR swapchain, uint64_t id, uint64_t timeout) {
    REQUIRE(device == presentTestDevice && swapchain == presentTestSwapchain);
    ++waitCalls; lastWaitedId = id; lastWaitTimeout = timeout;
    return waitResult;
}
VKAPI_ATTR VkResult VKAPI_CALL PresentAcquire(VkDevice, VkSwapchainKHR, uint64_t, VkSemaphore, VkFence, uint32_t* index) {
    if (acquireEntered) { acquireEntered->set_value(); acquireRelease.wait(); }
    *index = 0; return VK_SUCCESS;
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL PresentDeviceProc(VkDevice, const char* name) {
#define FUNCTION(text, function) if (!std::strcmp(name, text)) return reinterpret_cast<PFN_vkVoidFunction>(function)
    FUNCTION("vkCreateSwapchainKHR", PresentCreateSwapchain);
    FUNCTION("vkDestroySwapchainKHR", PresentDestroySwapchain);
    FUNCTION("vkQueuePresentKHR", PresentQueue);
    FUNCTION("vkWaitForPresentKHR", PresentWait);
    FUNCTION("vkAcquireNextImageKHR", PresentAcquire);
#undef FUNCTION
    return nullptr;
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL PresentLoader(VkInstance, const char* name) {
#define FUNCTION(text, function) if (!std::strcmp(name, text)) return reinterpret_cast<PFN_vkVoidFunction>(function)
    FUNCTION("vkGetDeviceProcAddr", PresentDeviceProc);
    FUNCTION("vkCreateDevice", PresentCreateDevice);
    FUNCTION("vkEnumerateDeviceExtensionProperties", PresentExtensions);
    FUNCTION("vkGetPhysicalDeviceFeatures2", PresentFeatures);
    FUNCTION("vkGetPhysicalDeviceProperties", TestProperties);
#undef FUNCTION
    return nullptr;
}
}
extern "C" int FFU_CALL ffu_present_wait_status();
extern "C" int FFU_CALL ffu_present_wait_query(uint64_t* generation, uint64_t* presentId);
extern "C" int FFU_CALL ffu_present_wait_poll(uint64_t generation, uint64_t presentId);
namespace {
void VerifyPresentWait() {
    const auto loader = startupInterceptor(PresentLoader, nullptr); REQUIRE(loader);
    const auto createDevice = reinterpret_cast<PFN_vkCreateDevice>(loader(testInstance, "vkCreateDevice")); REQUIRE(createDevice);
    const auto deviceProc = reinterpret_cast<PFN_vkGetDeviceProcAddr>(loader(testInstance, "vkGetDeviceProcAddr")); REQUIRE(deviceProc);
    VkDeviceCreateInfo info{VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO};
    VkDevice device = VK_NULL_HANDLE;
    uint64_t generation = 0, presentId = 0;

    REQUIRE(createDevice(testPhysical, &info, nullptr, &device) == VK_SUCCESS);
    REQUIRE(ffu_present_wait_status() == 2 && !lastCreateHadPresentWait); // unsupported: Unity's configuration only
    REQUIRE(ffu_present_wait_query(&generation, &presentId) == -2 && ffu_present_wait_poll(0, 1) == -2);

    presentFeatures = true; rejectPresentFeatures = true; int creates = presentDeviceCreates;
    REQUIRE(createDevice(testPhysical, &info, nullptr, &device) == VK_SUCCESS);
    REQUIRE(ffu_present_wait_status() == 3 && presentDeviceCreates >= creates + 2 && !lastCreateHadPresentWait);

    rejectPresentFeatures = false;
    // Unity 6 requests both features itself when the driver supports them: reuse that configuration unchanged.
    VkPhysicalDevicePresentIdFeaturesKHR unityId{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PRESENT_ID_FEATURES_KHR, nullptr, VK_TRUE};
    VkPhysicalDevicePresentWaitFeaturesKHR unityWait{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PRESENT_WAIT_FEATURES_KHR, &unityId, VK_TRUE};
    const char* unityExtensions[] = {VK_KHR_PRESENT_ID_EXTENSION_NAME, VK_KHR_PRESENT_WAIT_EXTENSION_NAME};
    VkDeviceCreateInfo unityInfo{VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO};
    unityInfo.pNext = &unityWait; unityInfo.enabledExtensionCount = 2; unityInfo.ppEnabledExtensionNames = unityExtensions;
    creates = presentDeviceCreates;
    REQUIRE(createDevice(testPhysical, &unityInfo, nullptr, &device) == VK_SUCCESS);
    REQUIRE(ffu_present_wait_status() == 0 && presentDeviceCreates == creates + 1 && lastCreateHadPresentWait);
    unityId.presentId = VK_FALSE; unityWait.presentWait = VK_FALSE; unityInfo.enabledExtensionCount = 0;
    creates = presentDeviceCreates;
    REQUIRE(createDevice(testPhysical, &unityInfo, nullptr, &device) == VK_SUCCESS);
    REQUIRE(ffu_present_wait_status() == 2 && presentDeviceCreates == creates + 1 && !lastCreateHadPresentWait);

    REQUIRE(createDevice(testPhysical, &info, nullptr, &device) == VK_SUCCESS && device == presentTestDevice);
    REQUIRE(ffu_present_wait_status() == 0 && lastCreateHadPresentWait);
    REQUIRE(info.pNext == nullptr && info.enabledExtensionCount == 0); // borrowed Unity memory unchanged
    REQUIRE(ffu_present_wait_query(nullptr, &presentId) == -3);
    REQUIRE(ffu_present_wait_query(&generation, &presentId) == 1 && presentId == 0);
    REQUIRE(ffu_present_wait_poll(generation, 1) == -1); // no swapchain yet

    const auto createSwapchain = reinterpret_cast<PFN_vkCreateSwapchainKHR>(deviceProc(device, "vkCreateSwapchainKHR"));
    const auto destroySwapchain = reinterpret_cast<PFN_vkDestroySwapchainKHR>(deviceProc(device, "vkDestroySwapchainKHR"));
    const auto present = reinterpret_cast<PFN_vkQueuePresentKHR>(deviceProc(device, "vkQueuePresentKHR"));
    const auto acquire = reinterpret_cast<PFN_vkAcquireNextImageKHR>(deviceProc(device, "vkAcquireNextImageKHR"));
    REQUIRE(createSwapchain && destroySwapchain && present && acquire);
    VkSwapchainCreateInfoKHR swapchainInfo{VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR};
    VkSwapchainKHR swapchain = VK_NULL_HANDLE;
    const uint64_t before = generation;
    REQUIRE(createSwapchain(device, &swapchainInfo, nullptr, &swapchain) == VK_SUCCESS && swapchain == presentTestSwapchain);
    REQUIRE(ffu_present_wait_query(&generation, &presentId) == 1 && generation != before && presentId == 0);

    const VkQueue queue = reinterpret_cast<VkQueue>(uintptr_t(4));
    const VkSwapchainKHR both[] = {otherTestSwapchain, presentTestSwapchain};
    VkPresentInfoKHR presentInfo{VK_STRUCTURE_TYPE_PRESENT_INFO_KHR};
    presentInfo.swapchainCount = 2; presentInfo.pSwapchains = both;
    REQUIRE(present(queue, &presentInfo) == VK_SUCCESS && lastPresentedId == 1);
    REQUIRE(present(queue, &presentInfo) == VK_SUCCESS && lastPresentedId == 2);
    REQUIRE(ffu_present_wait_query(&generation, &presentId) == 1 && presentId == 2);
    presentResult = VK_ERROR_OUT_OF_DATE_KHR;
    REQUIRE(present(queue, &presentInfo) == VK_ERROR_OUT_OF_DATE_KHR && lastPresentedId == 3);
    REQUIRE(ffu_present_wait_query(&generation, &presentId) == 1 && presentId == 2); // a failed present is never waited for
    presentResult = VK_SUCCESS;
    uint64_t ownIds[] = {0, 77};
    VkPresentIdKHR own{VK_STRUCTURE_TYPE_PRESENT_ID_KHR, nullptr, 2, ownIds};
    presentInfo.pNext = &own;
    REQUIRE(present(queue, &presentInfo) == VK_SUCCESS && lastPresentedId == 77); // Unity's own ids are left alone
    REQUIRE(ffu_present_wait_query(&generation, &presentId) == 1 && presentId == 77); // and followed
    presentInfo.pNext = nullptr;
    REQUIRE(present(queue, &presentInfo) == VK_SUCCESS && lastPresentedId == 78); // injected ids keep increasing
    REQUIRE(ffu_present_wait_query(&generation, &presentId) == 1 && presentId == 78);

    // Unity's own vkWaitForPresentKHR calls are forwarded.
    const auto unityWaitFunction = reinterpret_cast<PFN_vkWaitForPresentKHR>(deviceProc(device, "vkWaitForPresentKHR"));
    REQUIRE(unityWaitFunction && unityWaitFunction != PresentWait);
    waitCalls = 0; waitResult = VK_SUCCESS;
    REQUIRE(unityWaitFunction(device, swapchain, 78, 5) == VK_SUCCESS && waitCalls == 1 && lastWaitedId == 78 && lastWaitTimeout == 5);

    waitCalls = 0;
    REQUIRE(ffu_present_wait_poll(generation, 79) == 0 && waitCalls == 0); // not presented yet: no driver call
    waitResult = VK_TIMEOUT;
    REQUIRE(ffu_present_wait_poll(generation, 2) == 0 && waitCalls == 1 && lastWaitedId == 2 && lastWaitTimeout == 0);
    waitResult = VK_SUCCESS;
    REQUIRE(ffu_present_wait_poll(generation, 2) == 1);
    waitResult = VK_SUBOPTIMAL_KHR;
    REQUIRE(ffu_present_wait_poll(generation, 1) == 1);
    waitResult = VK_ERROR_DEVICE_LOST;
    REQUIRE(ffu_present_wait_poll(generation, 2) == VK_ERROR_DEVICE_LOST);
    REQUIRE(ffu_present_wait_poll(generation + 1, 2) == -1); // stale generation

    // A Unity swapchain call in progress, such as a blocking acquisition, keeps the poll off the swapchain.
    std::promise<void> entered, release;
    acquireEntered = &entered; acquireRelease = release.get_future().share();
    std::thread renderThread([&] { uint32_t index = 0; acquire(device, swapchain, UINT64_MAX, VK_NULL_HANDLE, VK_NULL_HANDLE, &index); });
    entered.get_future().wait();
    waitCalls = 0; waitResult = VK_SUCCESS;
    REQUIRE(ffu_present_wait_poll(generation, 2) == 0 && waitCalls == 0);
    release.set_value(); renderThread.join(); acquireEntered = nullptr;
    REQUIRE(ffu_present_wait_poll(generation, 2) == 1 && waitCalls == 1);

    destroySwapchain(device, swapchain, nullptr);
    REQUIRE(ffu_present_wait_poll(generation, 2) == -1);
    uint64_t after = 0;
    REQUIRE(ffu_present_wait_query(&after, &presentId) == 1 && after != generation && presentId == 0);
    std::puts("PASS: present wait negotiation/fallback, present id injection per swapchain, zero-timeout polls, swapchain generations and in-progress Unity calls");
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
    VerifyPresentWait();
#endif
    std::puts("PASS: Vulkan negotiation fallback, feature preservation, private queue remapping, deferred device and instance destruction through real FFmpeg buffer references");
}
