// Deterministic tests of the pacing layer against a modeled Unity interceptor chain and driver.
// They verify the layer protocol; they do not claim coverage of real drivers or presentation timing.
#define VK_NO_PROTOTYPES
#include "VulkanPacing.h"
#include "IUnityGraphics.h"
#include "IUnityGraphicsVulkan.h"
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <future>
#include <string>
#include <thread>
#include <utility>
#include <vector>

// The layer declares VK_AMD_anti_lag itself when the headers predate it; mirror the few values used here.
#ifndef VK_AMD_anti_lag
#define VK_AMD_ANTI_LAG_EXTENSION_NAME "VK_AMD_anti_lag"
constexpr VkStructureType AntiLagFeatures = static_cast<VkStructureType>(1000476000);
constexpr VkStructureType AntiLagData = static_cast<VkStructureType>(1000476001);
struct AntiLagPresentation { VkStructureType sType; void* pNext; int stage; uint64_t frameIndex; };
struct AntiLagDataAmd { VkStructureType sType; const void* pNext; int mode; uint32_t maxFPS; const AntiLagPresentation* pPresentationInfo; };
struct AntiLagFeaturesAmd { VkStructureType sType; void* pNext; VkBool32 antiLag; };
#else
constexpr VkStructureType AntiLagFeatures = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_ANTI_LAG_FEATURES_AMD;
constexpr VkStructureType AntiLagData = VK_STRUCTURE_TYPE_ANTI_LAG_DATA_AMD;
using AntiLagDataAmd = VkAntiLagDataAMD;
using AntiLagFeaturesAmd = VkPhysicalDeviceAntiLagFeaturesAMD;
#endif

extern "C" void UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* interfaces);
extern "C" void UNITY_INTERFACE_API UnityPluginUnload();

#define REQUIRE(value) do { if (!(value)) { std::printf("FAIL line %d: %s\n", __LINE__, #value); std::exit(1); } } while (0)
namespace {
const VkInstance testInstance = reinterpret_cast<VkInstance>(uintptr_t(1));
const VkPhysicalDevice testPhysical = reinterpret_cast<VkPhysicalDevice>(uintptr_t(3));
const VkDevice testDevice = reinterpret_cast<VkDevice>(uintptr_t(8));
const VkQueue testQueue = reinterpret_cast<VkQueue>(uintptr_t(4));
const VkSemaphore testSemaphore = (VkSemaphore)(uintptr_t(50));
const VkSwapchainKHR foreignSwapchain = (VkSwapchainKHR)(uintptr_t(10));

// Modeled Unity Vulkan interface: records the interceptor registration.
UnityVulkanInitCallback registered = nullptr;
int32_t registeredPriority = 0;
int removals = 0;
bool UNITY_INTERFACE_API AddIntercept(UnityVulkanInitCallback callback, void*, int32_t priority) {
    registered = callback; registeredPriority = priority; return true;
}
bool UNITY_INTERFACE_API RemoveIntercept(UnityVulkanInitCallback callback) { REQUIRE(callback == registered); ++removals; return true; }
IUnityGraphicsVulkanV2 unityVulkan{};
IUnityInterface* UNITY_INTERFACE_API GetInterface(unsigned long long high, unsigned long long low) {
    const auto id = GetUnityInterfaceGUID<IUnityGraphicsVulkanV2>();
    return high == id.m_GUIDHigh && low == id.m_GUIDLow ? reinterpret_cast<IUnityInterface*>(&unityVulkan) : nullptr;
}

// Modeled inner layers and driver.
struct Driver {
    bool nvidia = false, amd = false, timelineFeature = true, rejectAdditions = false, waitAvailable = true;
    bool latencyFunctions = true;
    int creates = 0, presentCalls = 0, waitCalls = 0, acquireCalls = 0, swapchainDestroys = 0, semaphoreDestroys = 0;
    std::vector<std::string> lastExtensions;
    std::vector<VkStructureType> lastChain;
    bool lastSwapchainLatency = false;
    uintptr_t nextSwapchain = 100;
    VkSwapchainKHR lastPresented = VK_NULL_HANDLE;
    uint64_t lastPresentedId = 0, lastWaitedId = 0, lastWaitTimeout = UINT64_MAX;
    VkResult presentResult = VK_SUCCESS, waitResult = VK_TIMEOUT, acquireResult = VK_SUBOPTIMAL_KHR, semaphoreWait = VK_SUCCESS;
    std::vector<VkSwapchainKHR> sleepModes;
    std::vector<std::pair<VkSemaphore, uint64_t>> sleeps, semaphoreWaits;
    std::vector<std::pair<uint64_t, VkLatencyMarkerNV>> markers;
    std::vector<uint64_t> submittedIds;
    std::vector<std::pair<int, uint64_t>> antiLag;
    std::promise<void>* acquireEntered = nullptr;
    std::shared_future<void> acquireRelease;
};
Driver driver;

bool Has(const std::vector<std::string>& names, const char* name) {
    for (const auto& entry : names)
        if (entry == name) return true;
    return false;
}
bool HasType(const std::vector<VkStructureType>& chain, VkStructureType type) {
    for (auto entry : chain)
        if (entry == type) return true;
    return false;
}
VKAPI_ATTR VkResult VKAPI_CALL InnerExtensions(VkPhysicalDevice, const char*, uint32_t* count, VkExtensionProperties* properties) {
    std::vector<const char*> names = {VK_KHR_PRESENT_ID_EXTENSION_NAME, VK_KHR_PRESENT_WAIT_EXTENSION_NAME};
    if (driver.nvidia) { names.push_back(VK_NV_LOW_LATENCY_2_EXTENSION_NAME); names.push_back(VK_KHR_TIMELINE_SEMAPHORE_EXTENSION_NAME); }
    if (driver.amd) names.push_back(VK_AMD_ANTI_LAG_EXTENSION_NAME);
    if (!properties) { *count = static_cast<uint32_t>(names.size()); return VK_SUCCESS; }
    for (uint32_t i = 0; i < *count && i < names.size(); ++i) { properties[i] = {}; std::strcpy(properties[i].extensionName, names[i]); }
    return VK_SUCCESS;
}
VKAPI_ATTR void VKAPI_CALL InnerFeatures(VkPhysicalDevice, VkPhysicalDeviceFeatures2* features) {
    for (auto* item = static_cast<VkBaseOutStructure*>(features->pNext); item; item = item->pNext) {
        if (item->sType == VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TIMELINE_SEMAPHORE_FEATURES)
            reinterpret_cast<VkPhysicalDeviceTimelineSemaphoreFeatures*>(item)->timelineSemaphore = driver.timelineFeature;
        if (item->sType == AntiLagFeatures) reinterpret_cast<AntiLagFeaturesAmd*>(item)->antiLag = driver.amd;
    }
}
VKAPI_ATTR VkResult VKAPI_CALL InnerCreateDevice(VkPhysicalDevice physical, const VkDeviceCreateInfo* info, const VkAllocationCallbacks*, VkDevice* device) {
    REQUIRE(physical == testPhysical);
    ++driver.creates;
    driver.lastExtensions.assign(info->ppEnabledExtensionNames, info->ppEnabledExtensionNames + info->enabledExtensionCount);
    driver.lastChain.clear();
    for (auto* item = static_cast<const VkBaseInStructure*>(info->pNext); item; item = item->pNext) driver.lastChain.push_back(item->sType);
    const bool added = Has(driver.lastExtensions, VK_NV_LOW_LATENCY_2_EXTENSION_NAME) || Has(driver.lastExtensions, VK_AMD_ANTI_LAG_EXTENSION_NAME);
    if (added && driver.rejectAdditions) return VK_ERROR_FEATURE_NOT_PRESENT;
    *device = testDevice; return VK_SUCCESS;
}
VKAPI_ATTR void VKAPI_CALL InnerDestroyDevice(VkDevice device, const VkAllocationCallbacks*) { REQUIRE(device == testDevice); }
VKAPI_ATTR VkResult VKAPI_CALL InnerCreateSwapchain(VkDevice device, const VkSwapchainCreateInfoKHR* info, const VkAllocationCallbacks*, VkSwapchainKHR* swapchain) {
    REQUIRE(device == testDevice);
    driver.lastSwapchainLatency = false;
    for (auto* item = static_cast<const VkBaseInStructure*>(info->pNext); item; item = item->pNext)
        if (item->sType == VK_STRUCTURE_TYPE_SWAPCHAIN_LATENCY_CREATE_INFO_NV)
            driver.lastSwapchainLatency = reinterpret_cast<const VkSwapchainLatencyCreateInfoNV*>(item)->latencyModeEnable;
    *swapchain = (VkSwapchainKHR)(driver.nextSwapchain++);
    return VK_SUCCESS;
}
VKAPI_ATTR void VKAPI_CALL InnerDestroySwapchain(VkDevice device, VkSwapchainKHR, const VkAllocationCallbacks*) { REQUIRE(device == testDevice); ++driver.swapchainDestroys; }
VKAPI_ATTR VkResult VKAPI_CALL InnerPresent(VkQueue queue, const VkPresentInfoKHR* info) {
    REQUIRE(queue == testQueue);
    ++driver.presentCalls; driver.lastPresentedId = 0; driver.lastPresented = VK_NULL_HANDLE;
    for (auto* item = static_cast<const VkBaseInStructure*>(info->pNext); item; item = item->pNext) {
        if (item->sType != VK_STRUCTURE_TYPE_PRESENT_ID_KHR) continue;
        auto* ids = reinterpret_cast<const VkPresentIdKHR*>(item);
        REQUIRE(ids->swapchainCount == info->swapchainCount);
        for (uint32_t i = 0; i < ids->swapchainCount; ++i) {
            if (ids->pPresentIds[i] == 0 || info->pSwapchains[i] == foreignSwapchain) continue;
            REQUIRE(driver.lastPresentedId == 0); // one tracked swapchain per presentation
            driver.lastPresentedId = ids->pPresentIds[i]; driver.lastPresented = info->pSwapchains[i];
        }
    }
    return driver.presentResult;
}
VKAPI_ATTR VkResult VKAPI_CALL InnerSubmit(VkQueue queue, uint32_t count, const VkSubmitInfo* submits, VkFence) {
    REQUIRE(queue == testQueue);
    for (uint32_t i = 0; i < count; ++i) {
        uint64_t id = 0;
        for (auto* item = static_cast<const VkBaseInStructure*>(submits[i].pNext); item; item = item->pNext)
            if (item->sType == VK_STRUCTURE_TYPE_LATENCY_SUBMISSION_PRESENT_ID_NV) id = reinterpret_cast<const VkLatencySubmissionPresentIdNV*>(item)->presentID;
        driver.submittedIds.push_back(id);
    }
    return VK_SUCCESS;
}
VKAPI_ATTR VkResult VKAPI_CALL InnerWait(VkDevice device, VkSwapchainKHR, uint64_t id, uint64_t timeout) {
    REQUIRE(device == testDevice);
    ++driver.waitCalls; driver.lastWaitedId = id; driver.lastWaitTimeout = timeout;
    return driver.waitResult;
}
VKAPI_ATTR VkResult VKAPI_CALL InnerAcquire(VkDevice, VkSwapchainKHR, uint64_t, VkSemaphore, VkFence, uint32_t* index) {
    ++driver.acquireCalls;
    if (driver.acquireEntered) { driver.acquireEntered->set_value(); driver.acquireRelease.wait(); }
    *index = 2; return driver.acquireResult;
}
VKAPI_ATTR VkResult VKAPI_CALL InnerAcquire2(VkDevice device, const VkAcquireNextImageInfoKHR* info, uint32_t* index) {
    return InnerAcquire(device, info->swapchain, info->timeout, info->semaphore, info->fence, index);
}
VKAPI_ATTR VkResult VKAPI_CALL InnerCreateSemaphore(VkDevice, const VkSemaphoreCreateInfo* info, const VkAllocationCallbacks*, VkSemaphore* semaphore) {
    auto* type = reinterpret_cast<const VkSemaphoreTypeCreateInfo*>(info->pNext);
    REQUIRE(type && type->sType == VK_STRUCTURE_TYPE_SEMAPHORE_TYPE_CREATE_INFO && type->semaphoreType == VK_SEMAPHORE_TYPE_TIMELINE);
    *semaphore = testSemaphore; return VK_SUCCESS;
}
VKAPI_ATTR void VKAPI_CALL InnerDestroySemaphore(VkDevice, VkSemaphore semaphore, const VkAllocationCallbacks*) { REQUIRE(semaphore == testSemaphore); ++driver.semaphoreDestroys; }
VKAPI_ATTR VkResult VKAPI_CALL InnerWaitSemaphores(VkDevice, const VkSemaphoreWaitInfo* info, uint64_t) {
    REQUIRE(info->semaphoreCount == 1);
    driver.semaphoreWaits.push_back({info->pSemaphores[0], info->pValues[0]});
    return driver.semaphoreWait;
}
VKAPI_ATTR VkResult VKAPI_CALL InnerSleepMode(VkDevice, VkSwapchainKHR swapchain, const VkLatencySleepModeInfoNV* info) {
    REQUIRE(info->lowLatencyMode); driver.sleepModes.push_back(swapchain); return VK_SUCCESS;
}
VKAPI_ATTR VkResult VKAPI_CALL InnerSleep(VkDevice, VkSwapchainKHR, const VkLatencySleepInfoNV* info) {
    driver.sleeps.push_back({info->signalSemaphore, info->value}); return VK_SUCCESS;
}
VKAPI_ATTR void VKAPI_CALL InnerMarker(VkDevice, VkSwapchainKHR, const VkSetLatencyMarkerInfoNV* info) {
    driver.markers.push_back({info->presentID, info->marker});
}
VKAPI_ATTR void VKAPI_CALL InnerAntiLag(VkDevice device, const AntiLagDataAmd* data) {
    REQUIRE(device == testDevice && data->sType == AntiLagData && data->mode == 1 && data->pPresentationInfo);
    driver.antiLag.push_back({static_cast<int>(data->pPresentationInfo->stage), data->pPresentationInfo->frameIndex});
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL InnerDeviceProc(VkDevice, const char* name) {
#define FUNCTION(text, function) if (!std::strcmp(name, text)) return reinterpret_cast<PFN_vkVoidFunction>(function)
    FUNCTION("vkDestroyDevice", InnerDestroyDevice);
    FUNCTION("vkCreateSwapchainKHR", InnerCreateSwapchain);
    FUNCTION("vkDestroySwapchainKHR", InnerDestroySwapchain);
    FUNCTION("vkQueuePresentKHR", InnerPresent);
    FUNCTION("vkQueueSubmit", InnerSubmit);
    FUNCTION("vkAcquireNextImageKHR", InnerAcquire);
    FUNCTION("vkAcquireNextImage2KHR", InnerAcquire2);
    FUNCTION("vkCreateSemaphore", InnerCreateSemaphore);
    FUNCTION("vkDestroySemaphore", InnerDestroySemaphore);
    if (driver.waitAvailable) FUNCTION("vkWaitForPresentKHR", InnerWait);
    if (driver.latencyFunctions) {
        FUNCTION("vkWaitSemaphoresKHR", InnerWaitSemaphores);
        FUNCTION("vkSetLatencySleepModeNV", InnerSleepMode);
        FUNCTION("vkLatencySleepNV", InnerSleep);
        FUNCTION("vkSetLatencyMarkerNV", InnerMarker);
        FUNCTION("vkAntiLagUpdateAMD", InnerAntiLag);
    }
#undef FUNCTION
    return nullptr;
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL InnerInstanceProc(VkInstance, const char* name) {
#define FUNCTION(text, function) if (!std::strcmp(name, text)) return reinterpret_cast<PFN_vkVoidFunction>(function)
    FUNCTION("vkGetDeviceProcAddr", InnerDeviceProc);
    FUNCTION("vkCreateDevice", InnerCreateDevice);
    FUNCTION("vkEnumerateDeviceExtensionProperties", InnerExtensions);
    FUNCTION("vkGetPhysicalDeviceFeatures2", InnerFeatures);
    FUNCTION("vkQueuePresentKHR", InnerPresent);
    FUNCTION("vkAcquireNextImageKHR", InnerAcquire);
#undef FUNCTION
    return nullptr;
}

// One modeled Unity Vulkan initialization through the layer.
struct Unity {
    PFN_vkGetInstanceProcAddr loader = nullptr;
    PFN_vkGetDeviceProcAddr deviceProc = nullptr;
    PFN_vkCreateDevice createDevice = nullptr;
    PFN_vkDestroyDevice destroyDevice = nullptr;
    PFN_vkCreateSwapchainKHR createSwapchain = nullptr;
    PFN_vkDestroySwapchainKHR destroySwapchain = nullptr;
    PFN_vkQueuePresentKHR present = nullptr;
    PFN_vkQueueSubmit submit = nullptr;
    PFN_vkAcquireNextImageKHR acquire = nullptr;
    PFN_vkAcquireNextImage2KHR acquire2 = nullptr;
    PFN_vkWaitForPresentKHR wait = nullptr;
    VkDevice device = VK_NULL_HANDLE;
    // Unity 6's own request when the driver supports present waiting.
    VkPhysicalDevicePresentIdFeaturesKHR idFeature{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PRESENT_ID_FEATURES_KHR, nullptr, VK_TRUE};
    VkPhysicalDevicePresentWaitFeaturesKHR waitFeature{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PRESENT_WAIT_FEATURES_KHR, &idFeature, VK_TRUE};
    const char* extensions[2] = {VK_KHR_PRESENT_ID_EXTENSION_NAME, VK_KHR_PRESENT_WAIT_EXTENSION_NAME};
    VkDeviceCreateInfo info{VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO};

    explicit Unity(bool presentWait = true) {
        loader = registered(InnerInstanceProc, nullptr); REQUIRE(loader);
        createDevice = reinterpret_cast<PFN_vkCreateDevice>(loader(testInstance, "vkCreateDevice"));
        deviceProc = reinterpret_cast<PFN_vkGetDeviceProcAddr>(loader(testInstance, "vkGetDeviceProcAddr"));
        REQUIRE(createDevice && deviceProc);
        if (presentWait) { info.pNext = &waitFeature; info.enabledExtensionCount = 2; info.ppEnabledExtensionNames = extensions; }
    }
    VkResult Create() {
        const VkResult result = createDevice(testPhysical, &info, nullptr, &device);
        if (result != VK_SUCCESS) return result;
#define LOAD(field, type, name) field = reinterpret_cast<type>(deviceProc(device, name)); REQUIRE(field)
        LOAD(destroyDevice, PFN_vkDestroyDevice, "vkDestroyDevice");
        LOAD(createSwapchain, PFN_vkCreateSwapchainKHR, "vkCreateSwapchainKHR");
        LOAD(destroySwapchain, PFN_vkDestroySwapchainKHR, "vkDestroySwapchainKHR");
        LOAD(present, PFN_vkQueuePresentKHR, "vkQueuePresentKHR");
        LOAD(submit, PFN_vkQueueSubmit, "vkQueueSubmit");
        LOAD(acquire, PFN_vkAcquireNextImageKHR, "vkAcquireNextImageKHR");
        LOAD(acquire2, PFN_vkAcquireNextImage2KHR, "vkAcquireNextImage2KHR");
#undef LOAD
        wait = reinterpret_cast<PFN_vkWaitForPresentKHR>(deviceProc(device, "vkWaitForPresentKHR"));
        return result;
    }
    VkSwapchainKHR Swapchain(VkSwapchainKHR old = VK_NULL_HANDLE) {
        VkSwapchainCreateInfoKHR create{VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR};
        create.oldSwapchain = old;
        VkSwapchainKHR swapchain = VK_NULL_HANDLE;
        REQUIRE(createSwapchain(device, &create, nullptr, &swapchain) == VK_SUCCESS);
        return swapchain;
    }
    VkResult Present(VkSwapchainKHR swapchain) {
        const VkSwapchainKHR both[] = {foreignSwapchain, swapchain};
        VkPresentInfoKHR info{VK_STRUCTURE_TYPE_PRESENT_INFO_KHR};
        info.swapchainCount = 2; info.pSwapchains = both;
        return present(testQueue, &info);
    }
    void Submit() {
        VkSubmitInfo submits[2] = {{VK_STRUCTURE_TYPE_SUBMIT_INFO}, {VK_STRUCTURE_TYPE_SUBMIT_INFO}};
        REQUIRE(submit(testQueue, 2, submits, VK_NULL_HANDLE) == VK_SUCCESS);
    }
};
uint64_t Generation() { return majvk_swapchain_generation(); }
uint64_t LatestId() {
    uint64_t generation = 0, id = 0;
    REQUIRE(majvk_present_wait_query(&generation, &id) == 1);
    return id;
}

void VerifyRegistration() {
    unityVulkan.AddInterceptInitialization = AddIntercept;
    unityVulkan.RemoveInterceptInitialization = RemoveIntercept;
    IUnityInterfaces interfaces{}; interfaces.GetInterfaceSplit = GetInterface;
    UnityPluginLoad(&interfaces);
    REQUIRE(registered && registeredPriority < kUnityVulkanInitCallbackMaxPriority); // stays outside the innermost layer
    REQUIRE(majvk_abi_version() == 2 && majvk_present_wait_status() == MajvkStatusNoDevice);
    REQUIRE(majvk_low_latency_status() == MajvkLowLatencyNoDevice && majvk_low_latency_backend() == MajvkBackendNone);
    std::puts("PASS: registers below the maximum interceptor priority");
}

void VerifyPresentWait() {
    driver = Driver{};
    {
        Unity plain(false);
        REQUIRE(plain.Create() == VK_SUCCESS && driver.creates == 1);
        REQUIRE(majvk_present_wait_status() == MajvkStatusNotEnabledByUnity);
        uint64_t generation = 0, presentId = 0;
        REQUIRE(majvk_present_wait_query(&generation, &presentId) == -2 && majvk_present_wait_poll(0, 1) == -2);
        REQUIRE(majvk_low_latency_backend() == MajvkBackendNone && majvk_frame_begin(0) == MajvkFrameInactive && majvk_frame_end() == MajvkFrameInactive);
    }
    {
        Unity disabled;
        disabled.waitFeature.presentWait = VK_FALSE; // a disabled feature is respected
        REQUIRE(disabled.Create() == VK_SUCCESS && majvk_present_wait_status() == MajvkStatusNotEnabledByUnity);
        Unity missing;
        driver.waitAvailable = false;
        REQUIRE(missing.Create() == VK_SUCCESS && majvk_present_wait_status() == MajvkStatusMissingFunction);
        driver.waitAvailable = true;
    }
    Unity unity;
    REQUIRE(unity.Create() == VK_SUCCESS && unity.device == testDevice);
    REQUIRE(majvk_present_wait_status() == MajvkStatusEnabled && majvk_low_latency_status() == MajvkLowLatencyUnsupported);
    REQUIRE(driver.lastExtensions.size() == 2 && driver.lastChain.size() == 2); // Unity's configuration passed on unchanged
    uint64_t generation = 0, presentId = 0;
    REQUIRE(majvk_present_wait_query(nullptr, &presentId) == -3);
    REQUIRE(majvk_present_wait_query(&generation, &presentId) == 1 && presentId == 0);
    REQUIRE(majvk_present_wait_poll(generation, 1) == -1); // no swapchain yet

    // Inner results, such as another layer's acquisition workaround, pass through unchanged.
    uint32_t index = UINT32_MAX;
    REQUIRE(unity.acquire(unity.device, foreignSwapchain, UINT64_MAX, VK_NULL_HANDLE, VK_NULL_HANDLE, &index) == VK_SUBOPTIMAL_KHR && index == 2);
    VkAcquireNextImageInfoKHR acquireInfo{VK_STRUCTURE_TYPE_ACQUIRE_NEXT_IMAGE_INFO_KHR};
    REQUIRE(unity.acquire2(unity.device, &acquireInfo, &index) == VK_SUBOPTIMAL_KHR && driver.acquireCalls == 2);
    REQUIRE(unity.loader(testInstance, "vkQueuePresentKHR") != reinterpret_cast<PFN_vkVoidFunction>(InnerPresent)); // instance route too
    VkPresentInfoKHR untracked{VK_STRUCTURE_TYPE_PRESENT_INFO_KHR};
    untracked.swapchainCount = 1; untracked.pSwapchains = &foreignSwapchain;
    REQUIRE(unity.present(testQueue, &untracked) == VK_SUCCESS && driver.lastPresentedId == 0);

    const uint64_t before = Generation();
    const VkSwapchainKHR swapchain = unity.Swapchain();
    REQUIRE(Generation() != before && LatestId() == 0 && !driver.lastSwapchainLatency);
    generation = Generation();
    REQUIRE(unity.Present(swapchain) == VK_SUCCESS && driver.lastPresented == swapchain);
    const uint64_t first = driver.lastPresentedId;
    REQUIRE(first != 0 && LatestId() == first);
    REQUIRE(unity.Present(swapchain) == VK_SUCCESS && driver.lastPresentedId == first + 1 && LatestId() == first + 1);
    driver.presentResult = VK_ERROR_OUT_OF_DATE_KHR;
    REQUIRE(unity.Present(swapchain) == VK_ERROR_OUT_OF_DATE_KHR && driver.lastPresentedId == first + 2);
    REQUIRE(LatestId() == first + 1); // a failed present is never waited for
    driver.presentResult = VK_SUCCESS;
    uint64_t unityIds[] = {0, 500};
    VkPresentIdKHR unityChain{VK_STRUCTURE_TYPE_PRESENT_ID_KHR, nullptr, 2, unityIds};
    const VkSwapchainKHR both[] = {foreignSwapchain, swapchain};
    VkPresentInfoKHR own{VK_STRUCTURE_TYPE_PRESENT_INFO_KHR, &unityChain};
    own.swapchainCount = 2; own.pSwapchains = both;
    REQUIRE(unity.present(testQueue, &own) == VK_SUCCESS && driver.lastPresentedId == 500); // Unity's ids are left alone
    REQUIRE(LatestId() == 500 && majvk_present_id_source() == MajvkPresentIdsUnity); // and followed
    REQUIRE(unity.Present(swapchain) == VK_SUCCESS && driver.lastPresentedId == 501 && majvk_present_id_source() == MajvkPresentIdsLayer);

    // Unity's own waits are forwarded to the driver.
    driver.waitCalls = 0; driver.waitResult = VK_SUCCESS;
    REQUIRE(unity.wait && unity.wait(unity.device, swapchain, 501, 5) == VK_SUCCESS && driver.waitCalls == 1 && driver.lastWaitTimeout == 5);
    driver.waitCalls = 0;
    REQUIRE(majvk_present_wait_poll(generation, 502) == 0 && driver.waitCalls == 0); // not presented yet: no driver call
    driver.waitResult = VK_TIMEOUT;
    REQUIRE(majvk_present_wait_poll(generation, 501) == 0 && driver.waitCalls == 1 && driver.lastWaitedId == 501 && driver.lastWaitTimeout == 0);
    driver.waitResult = VK_SUCCESS;
    REQUIRE(majvk_present_wait_poll(generation, 501) == 1);
    driver.waitResult = VK_SUBOPTIMAL_KHR;
    REQUIRE(majvk_present_wait_poll(generation, first) == 1);
    driver.waitResult = VK_ERROR_DEVICE_LOST;
    REQUIRE(majvk_present_wait_poll(generation, 501) == VK_ERROR_DEVICE_LOST);
    REQUIRE(majvk_present_wait_poll(generation + 1, 501) == -1); // stale generation

    // A Unity swapchain call in progress, such as a blocking acquisition, keeps the poll off the swapchain.
    std::promise<void> entered, release;
    driver.acquireEntered = &entered; driver.acquireRelease = release.get_future().share();
    std::thread renderThread([&] { uint32_t image = 0; unity.acquire(unity.device, swapchain, UINT64_MAX, VK_NULL_HANDLE, VK_NULL_HANDLE, &image); });
    entered.get_future().wait();
    driver.waitCalls = 0; driver.waitResult = VK_SUCCESS;
    REQUIRE(majvk_present_wait_poll(generation, 501) == 0 && driver.waitCalls == 0);
    release.set_value(); renderThread.join(); driver.acquireEntered = nullptr;
    REQUIRE(majvk_present_wait_poll(generation, 501) == 1 && driver.waitCalls == 1);

    unity.destroySwapchain(unity.device, swapchain, nullptr);
    REQUIRE(driver.swapchainDestroys == 1 && majvk_present_wait_poll(generation, 501) == -1);
    REQUIRE(Generation() != generation && LatestId() == 0);
    std::puts("PASS: present waiting reuses Unity's configuration, ids per swapchain, zero-timeout polls and in-progress Unity calls");
}

// Window resize: Unity creates the new swapchain from the old one, may still present the old one's acquired
// image, then destroys the old swapchain.
void VerifyResize() {
    driver = Driver{};
    Unity unity;
    REQUIRE(unity.Create() == VK_SUCCESS);
    const VkSwapchainKHR old = unity.Swapchain();
    REQUIRE(unity.Present(old) == VK_SUCCESS);
    const uint64_t oldId = driver.lastPresentedId, oldGeneration = Generation();
    REQUIRE(LatestId() == oldId);

    const VkSwapchainKHR resized = unity.Swapchain(old);
    REQUIRE(resized != old);
    const uint64_t newGeneration = Generation();
    REQUIRE(newGeneration != oldGeneration && LatestId() == 0);
    REQUIRE(majvk_present_wait_poll(oldGeneration, oldId) == -1); // waits for the old swapchain end
    REQUIRE(unity.Present(old) == VK_SUCCESS && driver.lastPresentedId == 0 && LatestId() == 0); // retired: untracked
    unity.destroySwapchain(unity.device, old, nullptr);
    REQUIRE(Generation() == newGeneration); // destroying the retired swapchain keeps tracking the new one
    REQUIRE(unity.Present(resized) == VK_SUCCESS && driver.lastPresented == resized && driver.lastPresentedId > oldId);
    const uint64_t newId = driver.lastPresentedId;
    REQUIRE(LatestId() == newId);
    driver.waitResult = VK_SUCCESS;
    REQUIRE(majvk_present_wait_poll(newGeneration, newId) == 1 && driver.lastWaitedId == newId);
    std::puts("PASS: window resize moves tracking to the new swapchain; the retired one is ignored and ids keep increasing");
}

// Fullscreen toggle: Unity destroys the swapchain first and creates a new one without oldSwapchain.
void VerifyFullscreenToggle() {
    driver = Driver{};
    Unity unity;
    REQUIRE(unity.Create() == VK_SUCCESS);
    const VkSwapchainKHR windowed = unity.Swapchain();
    REQUIRE(unity.Present(windowed) == VK_SUCCESS);
    const uint64_t windowedId = driver.lastPresentedId, windowedGeneration = Generation();
    unity.destroySwapchain(unity.device, windowed, nullptr);
    const uint64_t gap = Generation();
    REQUIRE(gap != windowedGeneration && LatestId() == 0 && majvk_present_wait_poll(gap, windowedId) == -1);
    unity.Present(windowed); // a stray present to the destroyed handle stays untracked
    REQUIRE(LatestId() == 0);
    const VkSwapchainKHR fullscreen = unity.Swapchain();
    REQUIRE(Generation() != gap && fullscreen != windowed);
    REQUIRE(unity.Present(fullscreen) == VK_SUCCESS && driver.lastPresented == fullscreen && driver.lastPresentedId > windowedId);
    REQUIRE(LatestId() == driver.lastPresentedId);

    // An out-of-date presentation, the usual trigger for recreation, records no id; recreation then resumes.
    driver.presentResult = VK_ERROR_OUT_OF_DATE_KHR;
    const uint64_t beforeOutOfDate = LatestId();
    REQUIRE(unity.Present(fullscreen) == VK_ERROR_OUT_OF_DATE_KHR && LatestId() == beforeOutOfDate);
    driver.presentResult = VK_SUCCESS;
    const VkSwapchainKHR recreated = unity.Swapchain(fullscreen);
    unity.destroySwapchain(unity.device, fullscreen, nullptr);
    REQUIRE(unity.Present(recreated) == VK_SUCCESS && driver.lastPresented == recreated && LatestId() == driver.lastPresentedId);
    std::puts("PASS: fullscreen toggle and out-of-date recreation track the new swapchain handle and fresh ids");
}

void VerifyNvidia() {
    driver = Driver{};
    driver.nvidia = true;
    {
        // The driver rejects the device with the extension: Unity's own configuration is created instead.
        driver.rejectAdditions = true;
        Unity rejected;
        REQUIRE(rejected.Create() == VK_SUCCESS && driver.creates == 2 && !Has(driver.lastExtensions, VK_NV_LOW_LATENCY_2_EXTENSION_NAME));
        REQUIRE(majvk_low_latency_status() == MajvkLowLatencyRejected && majvk_low_latency_backend() == MajvkBackendNone);
        REQUIRE(majvk_present_wait_status() == MajvkStatusEnabled && majvk_frame_begin(0) == MajvkFrameInactive);
        driver.rejectAdditions = false;
    }
    {
        driver.latencyFunctions = false;
        Unity missing;
        REQUIRE(missing.Create() == VK_SUCCESS && majvk_low_latency_status() == MajvkLowLatencyMissingFunction);
        REQUIRE(majvk_low_latency_backend() == MajvkBackendNone);
        driver.latencyFunctions = true;
    }
    {
        // NVIDIA needs Unity's present ids, and a timeline feature Unity disabled cannot be turned on.
        Unity noIds(false);
        REQUIRE(noIds.Create() == VK_SUCCESS && majvk_low_latency_status() == MajvkLowLatencyUnsupported);
        Unity disabledTimeline;
        VkPhysicalDeviceTimelineSemaphoreFeatures timeline{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TIMELINE_SEMAPHORE_FEATURES, nullptr, VK_FALSE};
        disabledTimeline.idFeature.pNext = &timeline;
        REQUIRE(disabledTimeline.Create() == VK_SUCCESS && majvk_low_latency_status() == MajvkLowLatencyUnsupported);
    }
    Unity unity;
    REQUIRE(unity.Create() == VK_SUCCESS);
    REQUIRE(majvk_low_latency_backend() == MajvkBackendNvidia && majvk_low_latency_status() == MajvkLowLatencyActive);
    REQUIRE(Has(driver.lastExtensions, VK_NV_LOW_LATENCY_2_EXTENSION_NAME) && Has(driver.lastExtensions, VK_KHR_TIMELINE_SEMAPHORE_EXTENSION_NAME));
    REQUIRE(HasType(driver.lastChain, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TIMELINE_SEMAPHORE_FEATURES));
    REQUIRE(HasType(driver.lastChain, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PRESENT_WAIT_FEATURES_KHR)); // Unity's chain kept
    REQUIRE(unity.info.enabledExtensionCount == 2 && unity.info.pNext == &unity.waitFeature); // Unity's memory untouched
    REQUIRE(majvk_frame_begin(1000) == MajvkFrameSkipped && driver.sleeps.empty()); // no swapchain yet

    const VkSwapchainKHR swapchain = unity.Swapchain();
    REQUIRE(driver.lastSwapchainLatency && driver.sleepModes.size() == 1 && driver.sleepModes[0] == swapchain);
    driver.markers.clear();
    REQUIRE(majvk_frame_begin(1000) == MajvkFrameDone);
    REQUIRE(driver.sleeps.size() == 1 && driver.sleeps[0].first == testSemaphore && driver.semaphoreWaits.size() == 1);
    REQUIRE(driver.semaphoreWaits[0] == driver.sleeps[0]);
    REQUIRE(driver.markers.size() == 2 && driver.markers[0].second == VK_LATENCY_MARKER_SIMULATION_START_NV &&
            driver.markers[1].second == VK_LATENCY_MARKER_INPUT_SAMPLE_NV);
    const uint64_t frame = driver.markers[0].first;
    REQUIRE(driver.markers[1].first == frame && majvk_frame_end() == MajvkFrameDone);
    REQUIRE(driver.markers.back() == std::make_pair(frame, VK_LATENCY_MARKER_SIMULATION_END_NV));

    // The render thread's submissions and presentation carry the simulated frame's id.
    driver.markers.clear();
    unity.Submit(); unity.Submit();
    REQUIRE(driver.submittedIds == std::vector<uint64_t>({frame, frame, frame, frame}));
    REQUIRE(driver.markers.size() == 1 && driver.markers[0] == std::make_pair(frame, VK_LATENCY_MARKER_RENDERSUBMIT_START_NV));
    REQUIRE(unity.Present(swapchain) == VK_SUCCESS && driver.lastPresentedId == frame && LatestId() == frame);
    REQUIRE(driver.markers.size() == 4 && driver.markers[1] == std::make_pair(frame, VK_LATENCY_MARKER_RENDERSUBMIT_END_NV) &&
            driver.markers[2] == std::make_pair(frame, VK_LATENCY_MARKER_PRESENT_START_NV) &&
            driver.markers[3] == std::make_pair(frame, VK_LATENCY_MARKER_PRESENT_END_NV));

    // Two frames in flight keep their own ids in presentation order.
    REQUIRE(majvk_frame_begin(1000) == MajvkFrameDone);
    const uint64_t second = driver.markers.back().first;
    REQUIRE(majvk_frame_begin(1000) == MajvkFrameDone);
    const uint64_t third = driver.markers.back().first;
    REQUIRE(second > frame && third > second);
    driver.submittedIds.clear(); unity.Submit();
    REQUIRE(driver.submittedIds.front() == second);
    REQUIRE(unity.Present(swapchain) == VK_SUCCESS && driver.lastPresentedId == second);
    REQUIRE(unity.Present(swapchain) == VK_SUCCESS && driver.lastPresentedId == third);
    REQUIRE(unity.Present(swapchain) == VK_SUCCESS && driver.lastPresentedId == third + 1); // no begun frame left

    // Unity attaches its own ids (as Unity 6 does): NVIDIA's markers need frame ids, so the layer replaces them,
    // keeps the rest of Unity's chain, and translates Unity's own waits back to the frame ids.
    REQUIRE(majvk_frame_begin(1000) == MajvkFrameDone);
    const uint64_t owned = driver.markers.back().first;
    uint64_t unityIds[] = {7, 900};
    VkPresentIdKHR unityChain{VK_STRUCTURE_TYPE_PRESENT_ID_KHR, nullptr, 2, unityIds};
    const VkSwapchainKHR both[] = {foreignSwapchain, swapchain};
    VkPresentInfoKHR unityPresent{VK_STRUCTURE_TYPE_PRESENT_INFO_KHR, &unityChain};
    unityPresent.swapchainCount = 2; unityPresent.pSwapchains = both;
    driver.markers.clear();
    REQUIRE(unity.present(testQueue, &unityPresent) == VK_SUCCESS && driver.lastPresentedId == owned && LatestId() == owned);
    REQUIRE(majvk_present_id_source() == MajvkPresentIdsUnityReplaced && unityIds[1] == 900); // Unity's memory untouched
    REQUIRE(driver.markers.size() == 2 && driver.markers[0] == std::make_pair(owned, VK_LATENCY_MARKER_PRESENT_START_NV));
    unityIds[1] = 901;
    REQUIRE(unity.present(testQueue, &unityPresent) == VK_SUCCESS && driver.lastPresentedId == owned + 1);
    driver.waitResult = VK_SUCCESS;
    REQUIRE(unity.wait(unity.device, swapchain, 900, 7) == VK_SUCCESS && driver.lastWaitedId == owned && driver.lastWaitTimeout == 7);
    REQUIRE(unity.wait(unity.device, swapchain, 901, 7) == VK_SUCCESS && driver.lastWaitedId == owned + 1);
    REQUIRE(unity.wait(unity.device, swapchain, 903, 7) == VK_SUCCESS && driver.lastWaitedId == owned + 3); // ahead: same step
    REQUIRE(unity.wait(unity.device, swapchain, 899, 7) == VK_SUCCESS && driver.lastWaitedId == owned); // before the first
    // Unity's chain behind another structure cannot be replaced safely; the layer follows Unity's id then.
    VkLatencySubmissionPresentIdNV other{VK_STRUCTURE_TYPE_LATENCY_SUBMISSION_PRESENT_ID_NV, &unityChain, 0};
    unityPresent.pNext = &other;
    unityIds[1] = 5000;
    REQUIRE(unity.present(testQueue, &unityPresent) == VK_SUCCESS && driver.lastPresentedId == 5000);
    REQUIRE(majvk_present_id_source() == MajvkPresentIdsUnity);

    // The sleep semaphore's wait timing out is reported; the sleep values keep increasing.
    driver.semaphoreWait = VK_TIMEOUT;
    REQUIRE(majvk_frame_begin(1000) == MajvkFrameTimedOut && driver.sleeps.back().second > driver.sleeps.front().second);
    driver.semaphoreWait = VK_SUCCESS;

    // Resizing re-applies the sleep mode to the new swapchain and keeps sleeping on it.
    const VkSwapchainKHR resized = unity.Swapchain(swapchain);
    REQUIRE(driver.lastSwapchainLatency && driver.sleepModes.back() == resized);
    unity.destroySwapchain(unity.device, swapchain, nullptr);
    REQUIRE(majvk_frame_begin(1000) == MajvkFrameDone);
    REQUIRE(unity.Present(resized) == VK_SUCCESS && driver.lastPresented == resized);
    // Fullscreen toggle: while no swapchain exists the frame is registered but not slept.
    unity.destroySwapchain(unity.device, resized, nullptr);
    const size_t sleeps = driver.sleeps.size();
    REQUIRE(majvk_frame_begin(1000) == MajvkFrameSkipped && driver.sleeps.size() == sleeps && majvk_frame_end() == MajvkFrameSkipped);
    const VkSwapchainKHR fullscreen = unity.Swapchain();
    REQUIRE(driver.sleepModes.back() == fullscreen && majvk_frame_begin(1000) == MajvkFrameDone && driver.sleeps.size() == sleeps + 1);

    unity.destroyDevice(unity.device, nullptr);
    REQUIRE(driver.semaphoreDestroys == 1 && majvk_low_latency_status() == MajvkLowLatencyNoDevice);
    REQUIRE(majvk_low_latency_backend() == MajvkBackendNone && majvk_frame_begin(0) == MajvkFrameInactive);
    std::puts("PASS: VK_NV_low_latency2 negotiation/fallback, sleep and markers per frame id, submissions, recreation and device destruction");
}

void VerifyAmd() {
    driver = Driver{};
    driver.amd = true;
    Unity unity;
    REQUIRE(unity.Create() == VK_SUCCESS);
    REQUIRE(majvk_low_latency_backend() == MajvkBackendAmd && majvk_low_latency_status() == MajvkLowLatencyActive);
    REQUIRE(Has(driver.lastExtensions, VK_AMD_ANTI_LAG_EXTENSION_NAME) && HasType(driver.lastChain, AntiLagFeatures));
    const VkSwapchainKHR swapchain = unity.Swapchain();
    REQUIRE(!driver.lastSwapchainLatency);
    REQUIRE(majvk_frame_begin(0) == MajvkFrameDone && driver.antiLag.size() == 1 && driver.antiLag[0].first == 0);
    const uint64_t frame = driver.antiLag[0].second;
    REQUIRE(frame != 0 && majvk_frame_end() == MajvkFrameSkipped);
    unity.Submit();
    REQUIRE(unity.Present(swapchain) == VK_SUCCESS && driver.lastPresentedId == frame);
    REQUIRE(driver.antiLag.size() == 2 && driver.antiLag[1] == std::make_pair(1, frame)); // present stage, same frame
    // Recreation keeps the frame pairing.
    const VkSwapchainKHR resized = unity.Swapchain(swapchain);
    unity.destroySwapchain(unity.device, swapchain, nullptr);
    REQUIRE(majvk_frame_begin(0) == MajvkFrameDone);
    const uint64_t next = driver.antiLag.back().second;
    REQUIRE(unity.Present(resized) == VK_SUCCESS && driver.antiLag.back() == std::make_pair(1, next) && next > frame);
    {
        driver.rejectAdditions = true;
        Unity rejected;
        REQUIRE(rejected.Create() == VK_SUCCESS && majvk_low_latency_status() == MajvkLowLatencyRejected);
        REQUIRE(majvk_low_latency_backend() == MajvkBackendNone && majvk_present_wait_status() == MajvkStatusEnabled);
    }
    std::puts("PASS: VK_AMD_anti_lag negotiation/fallback with input and present stages paired by frame across recreation");
}

void VerifyUnload() {
    UnityPluginUnload();
    REQUIRE(removals == 1);
    UnityPluginUnload();
    REQUIRE(removals == 1);
    std::puts("PASS: unload removes the interceptor once");
}
} // namespace

int main() {
    VerifyRegistration();
    VerifyPresentWait();
    VerifyResize();
    VerifyFullscreenToggle();
    VerifyNvidia();
    VerifyAmd();
    VerifyUnload();
}
