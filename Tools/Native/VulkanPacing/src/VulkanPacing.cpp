// Standalone Unity Vulkan layer for frame pacing. It registers its own Unity initialization interceptor below
// the maximum priority, so other plug-ins (such as the FFmpeg graphics bridge) stay closer to the Vulkan loader
// and this layer sees every Unity swapchain call first. It offers, in order of preference:
// - a driver low-latency backend that it enables on Unity's device: VK_NV_low_latency2 (Reflex) or
//   VK_AMD_anti_lag; the managed pacer calls majvk_frame_begin/majvk_frame_end around each frame;
// - present waiting through VK_KHR_present_wait, which Unity 6 already enables when the driver supports it;
//   the managed pacer polls majvk_present_wait_poll.
#define VK_NO_PROTOTYPES
#include "VulkanPacing.h"
#include "IUnityGraphics.h"
#include "IUnityGraphicsVulkan.h"
#include <atomic>
#include <cstring>
#include <deque>
#include <mutex>
#include <utility>
#include <vector>

#ifndef VK_AMD_anti_lag
// VK_AMD_anti_lag from Vulkan-Headers 1.4.328, for header copies that predate it.
#define VK_AMD_anti_lag 1
#define VK_AMD_ANTI_LAG_EXTENSION_NAME "VK_AMD_anti_lag"
constexpr VkStructureType VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_ANTI_LAG_FEATURES_AMD = static_cast<VkStructureType>(1000476000);
constexpr VkStructureType VK_STRUCTURE_TYPE_ANTI_LAG_DATA_AMD = static_cast<VkStructureType>(1000476001);
constexpr VkStructureType VK_STRUCTURE_TYPE_ANTI_LAG_PRESENTATION_INFO_AMD = static_cast<VkStructureType>(1000476002);
typedef enum VkAntiLagModeAMD {
    VK_ANTI_LAG_MODE_DRIVER_CONTROL_AMD = 0,
    VK_ANTI_LAG_MODE_ON_AMD = 1,
    VK_ANTI_LAG_MODE_OFF_AMD = 2,
    VK_ANTI_LAG_MODE_MAX_ENUM_AMD = 0x7FFFFFFF
} VkAntiLagModeAMD;
typedef enum VkAntiLagStageAMD {
    VK_ANTI_LAG_STAGE_INPUT_AMD = 0,
    VK_ANTI_LAG_STAGE_PRESENT_AMD = 1,
    VK_ANTI_LAG_STAGE_MAX_ENUM_AMD = 0x7FFFFFFF
} VkAntiLagStageAMD;
typedef struct VkPhysicalDeviceAntiLagFeaturesAMD {
    VkStructureType sType;
    void* pNext;
    VkBool32 antiLag;
} VkPhysicalDeviceAntiLagFeaturesAMD;
typedef struct VkAntiLagPresentationInfoAMD {
    VkStructureType sType;
    void* pNext;
    VkAntiLagStageAMD stage;
    uint64_t frameIndex;
} VkAntiLagPresentationInfoAMD;
typedef struct VkAntiLagDataAMD {
    VkStructureType sType;
    const void* pNext;
    VkAntiLagModeAMD mode;
    uint32_t maxFPS;
    const VkAntiLagPresentationInfoAMD* pPresentationInfo;
} VkAntiLagDataAMD;
typedef void (VKAPI_PTR* PFN_vkAntiLagUpdateAMD)(VkDevice device, const VkAntiLagDataAMD* pData);
#endif

namespace {
// Lower than kUnityVulkanInitCallbackMaxPriority, which Unity reserves for the innermost interceptor.
constexpr int32_t InterceptPriority = 0x40000000;
// Frame ids waiting for their presentation; more means Unity skipped presentations (e.g. while minimized).
constexpr size_t MaxPendingFrames = 16;

IUnityGraphicsVulkanV2* unityVulkan = nullptr;
PFN_vkGetInstanceProcAddr nextInstanceProc = nullptr;
std::atomic<PFN_vkGetDeviceProcAddr> nextDeviceProc{nullptr};
PFN_vkCreateDevice nextCreateDevice = nullptr;
VkInstance deviceInstance = VK_NULL_HANDLE;
std::atomic<PFN_vkDestroyDevice> destroyDevice{nullptr};
std::atomic<PFN_vkAcquireNextImageKHR> acquireNextImage{nullptr};
std::atomic<PFN_vkAcquireNextImage2KHR> acquireNextImage2{nullptr};
std::atomic<PFN_vkQueuePresentKHR> queuePresent{nullptr};
std::atomic<PFN_vkQueueSubmit> queueSubmit{nullptr};
std::atomic<PFN_vkCreateSwapchainKHR> createSwapchain{nullptr};
std::atomic<PFN_vkDestroySwapchainKHR> destroySwapchain{nullptr};
std::atomic<PFN_vkWaitForPresentKHR> unityWaitForPresent{nullptr};
std::atomic<int> presentWaitStatus{MajvkStatusNoDevice};
std::atomic<int> lowLatencyBackend{MajvkBackendNone};
std::atomic<int> lowLatencyStatus{MajvkLowLatencyNoDevice};
std::atomic<int> presentIdSource{MajvkPresentIdsNone};

// presentMutex guards the tracked device and swapchain state below. vkWaitForPresentKHR externally synchronizes
// the swapchain, as acquisition, presentation, waiting and destruction do, so Unity's own swapchain calls
// register themselves in swapchainUsers under this lock but run unlocked: a blocking acquisition never holds it.
// The managed poll try-locks it and calls the zero-timeout wait only while no Unity call is in progress.
std::mutex presentMutex;
int swapchainUsers = 0;
VkDevice trackedDevice = VK_NULL_HANDLE;
VkSwapchainKHR trackedSwapchain = VK_NULL_HANDLE;
bool presentIdEnabled = false;
PFN_vkWaitForPresentKHR waitForPresent = nullptr;
// One monotonic counter numbers frames begun by the main thread and presentations without such a frame, so
// ids always increase per swapchain. pendingFrames holds begun frames in presentation order.
uint64_t frameCounter = 0;
uint64_t simulatedFrame = 0;
std::deque<uint64_t> pendingFrames;
bool renderSubmitStarted = false;
// Unity's ids replaced by frame ids for the NVIDIA backend, oldest first, so Unity's own waits can be translated.
std::deque<std::pair<uint64_t, uint64_t>> replacedIds;
constexpr size_t MaxReplacedIds = 64;
// Readable without the lock; a stale pair is rejected by the generation check.
std::atomic<uint64_t> swapchainGeneration{0};
std::atomic<uint64_t> lastPresentId{0};

// nvMutex guards nvDevice/nvSwapchain and serializes the NVIDIA latency calls, which take the swapchain, with
// each other and with swapchain destruction. Those calls never block.
std::mutex nvMutex;
VkDevice nvDevice = VK_NULL_HANDLE;
VkSwapchainKHR nvSwapchain = VK_NULL_HANDLE;
PFN_vkSetLatencySleepModeNV setLatencySleepMode = nullptr;
PFN_vkLatencySleepNV latencySleep = nullptr;
PFN_vkSetLatencyMarkerNV setLatencyMarker = nullptr;
PFN_vkWaitSemaphoresKHR waitSemaphores = nullptr;
PFN_vkDestroySemaphore destroySemaphore = nullptr;
VkSemaphore sleepSemaphore = VK_NULL_HANDLE;
uint64_t sleepValue = 0;
// vkAntiLagUpdateAMD may sleep inside the driver, so it is called without holding any lock.
std::atomic<PFN_vkAntiLagUpdateAMD> antiLagUpdate{nullptr};
std::atomic<VkDevice> amdDevice{VK_NULL_HANDLE};

// Registers one Unity swapchain call for its lifetime.
struct SwapchainUse {
    SwapchainUse() { std::lock_guard<std::mutex> lock(presentMutex); ++swapchainUsers; }
    ~SwapchainUse() { std::lock_guard<std::mutex> lock(presentMutex); --swapchainUsers; }
    SwapchainUse(const SwapchainUse&) = delete;
    SwapchainUse& operator=(const SwapchainUse&) = delete;
};

const VkBaseInStructure* FindStructure(const void* chain, VkStructureType type) {
    for (auto* node = static_cast<const VkBaseInStructure*>(chain); node; node = node->pNext)
        if (node->sType == type) return node;
    return nullptr;
}
bool HasName(const std::vector<const char*>& names, const char* name) {
    for (const char* entry : names)
        if (!std::strcmp(entry, name)) return true;
    return false;
}
bool HasExtension(const VkDeviceCreateInfo* info, const char* name) {
    for (uint32_t i = 0; i < info->enabledExtensionCount; ++i)
        if (!std::strcmp(info->ppEnabledExtensionNames[i], name)) return true;
    return false;
}
bool EnablesPresentId(const VkDeviceCreateInfo* info) {
    const auto* id = reinterpret_cast<const VkPhysicalDevicePresentIdFeaturesKHR*>(
        FindStructure(info->pNext, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PRESENT_ID_FEATURES_KHR));
    return id && id->presentId && HasExtension(info, VK_KHR_PRESENT_ID_EXTENSION_NAME);
}
bool EnablesPresentWait(const VkDeviceCreateInfo* info) {
    const auto* wait = reinterpret_cast<const VkPhysicalDevicePresentWaitFeaturesKHR*>(
        FindStructure(info->pNext, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PRESENT_WAIT_FEATURES_KHR));
    return EnablesPresentId(info) && wait && wait->presentWait && HasExtension(info, VK_KHR_PRESENT_WAIT_EXTENSION_NAME);
}
template<class T> T InstanceFunction(const char* name) { return reinterpret_cast<T>(nextInstanceProc(deviceInstance, name)); }

// The extensions and feature structures a driver low-latency backend adds to Unity's device.
struct LowLatencyPlan {
    int backend = MajvkBackendNone;
    std::vector<const char*> extensions;
    VkPhysicalDeviceTimelineSemaphoreFeatures timeline{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TIMELINE_SEMAPHORE_FEATURES, nullptr, VK_TRUE};
    VkPhysicalDeviceAntiLagFeaturesAMD antiLag{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_ANTI_LAG_FEATURES_AMD, nullptr, VK_TRUE};
    const void* head = nullptr;
};
// Picks VK_NV_low_latency2 or VK_AMD_anti_lag when the physical device supports it and Unity's configuration
// allows it. NVIDIA needs Unity's present ids and a timeline semaphore for its sleep.
void PlanLowLatency(VkPhysicalDevice physical, const VkDeviceCreateInfo* info, LowLatencyPlan& plan) {
    const auto enumerate = InstanceFunction<PFN_vkEnumerateDeviceExtensionProperties>("vkEnumerateDeviceExtensionProperties");
    auto features2 = InstanceFunction<PFN_vkGetPhysicalDeviceFeatures2>("vkGetPhysicalDeviceFeatures2");
    if (!features2) features2 = InstanceFunction<PFN_vkGetPhysicalDeviceFeatures2>("vkGetPhysicalDeviceFeatures2KHR");
    uint32_t count = 0;
    if (!enumerate || !features2 || enumerate(physical, nullptr, &count, nullptr) != VK_SUCCESS) return;
    std::vector<VkExtensionProperties> available(count);
    if (enumerate(physical, nullptr, &count, available.data()) != VK_SUCCESS) return;
    auto supports = [&](const char* name) {
        for (const auto& extension : available)
            if (!std::strcmp(extension.extensionName, name)) return true;
        return false;
    };
    plan.extensions.assign(info->ppEnabledExtensionNames, info->ppEnabledExtensionNames + info->enabledExtensionCount);
    auto add = [&](const char* name) { if (!HasName(plan.extensions, name)) plan.extensions.push_back(name); };

    if (supports(VK_NV_LOW_LATENCY_2_EXTENSION_NAME) && supports(VK_KHR_TIMELINE_SEMAPHORE_EXTENSION_NAME) && EnablesPresentId(info)) {
        VkPhysicalDeviceTimelineSemaphoreFeatures timeline{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TIMELINE_SEMAPHORE_FEATURES, nullptr, VK_FALSE};
        VkPhysicalDeviceFeatures2 features{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2, &timeline, {}};
        features2(physical, &features);
        const auto* existing = reinterpret_cast<const VkPhysicalDeviceTimelineSemaphoreFeatures*>(
            FindStructure(info->pNext, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TIMELINE_SEMAPHORE_FEATURES));
        const auto* vulkan12 = reinterpret_cast<const VkPhysicalDeviceVulkan12Features*>(
            FindStructure(info->pNext, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES));
        // Unity's own feature structures cannot be changed; a disabled timeline feature rules NVIDIA out.
        const bool unityDisabled = (existing && !existing->timelineSemaphore) || (vulkan12 && !vulkan12->timelineSemaphore);
        if (timeline.timelineSemaphore && !unityDisabled) {
            plan.backend = MajvkBackendNvidia;
            add(VK_NV_LOW_LATENCY_2_EXTENSION_NAME);
            add(VK_KHR_TIMELINE_SEMAPHORE_EXTENSION_NAME);
            plan.head = info->pNext;
            if (!existing && !vulkan12) {
                plan.timeline.pNext = const_cast<void*>(info->pNext);
                plan.head = &plan.timeline;
            }
            return;
        }
    }
    if (supports(VK_AMD_ANTI_LAG_EXTENSION_NAME) && !FindStructure(info->pNext, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_ANTI_LAG_FEATURES_AMD)) {
        VkPhysicalDeviceAntiLagFeaturesAMD antiLag{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_ANTI_LAG_FEATURES_AMD, nullptr, VK_FALSE};
        VkPhysicalDeviceFeatures2 features{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2, &antiLag, {}};
        features2(physical, &features);
        if (antiLag.antiLag) {
            plan.backend = MajvkBackendAmd;
            add(VK_AMD_ANTI_LAG_EXTENSION_NAME);
            plan.antiLag.pNext = const_cast<void*>(info->pNext);
            plan.head = &plan.antiLag;
        }
    }
}
// Loads the backend's entry points and creates NVIDIA's sleep semaphore.
bool StartLowLatency(VkDevice device, int backend, PFN_vkGetDeviceProcAddr deviceProc) {
    auto load = [&](const char* name) { return deviceProc ? deviceProc(device, name) : nullptr; };
    if (backend == MajvkBackendAmd) {
        const auto update = reinterpret_cast<PFN_vkAntiLagUpdateAMD>(load("vkAntiLagUpdateAMD"));
        amdDevice = device;
        antiLagUpdate = update;
        return update != nullptr;
    }
    setLatencySleepMode = reinterpret_cast<PFN_vkSetLatencySleepModeNV>(load("vkSetLatencySleepModeNV"));
    latencySleep = reinterpret_cast<PFN_vkLatencySleepNV>(load("vkLatencySleepNV"));
    setLatencyMarker = reinterpret_cast<PFN_vkSetLatencyMarkerNV>(load("vkSetLatencyMarkerNV"));
    waitSemaphores = reinterpret_cast<PFN_vkWaitSemaphoresKHR>(load("vkWaitSemaphoresKHR"));
    destroySemaphore = reinterpret_cast<PFN_vkDestroySemaphore>(load("vkDestroySemaphore"));
    const auto create = reinterpret_cast<PFN_vkCreateSemaphore>(load("vkCreateSemaphore"));
    if (!setLatencySleepMode || !latencySleep || !setLatencyMarker || !waitSemaphores || !destroySemaphore || !create) return false;
    VkSemaphoreTypeCreateInfo type{VK_STRUCTURE_TYPE_SEMAPHORE_TYPE_CREATE_INFO, nullptr, VK_SEMAPHORE_TYPE_TIMELINE, 0};
    VkSemaphoreCreateInfo semaphore{VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO, &type, 0};
    std::lock_guard<std::mutex> lock(nvMutex);
    sleepValue = 0;
    if (create(device, &semaphore, nullptr, &sleepSemaphore) != VK_SUCCESS) return false;
    nvDevice = device;
    return true;
}
void StopLowLatency(VkDevice device) {
    std::lock_guard<std::mutex> lock(nvMutex);
    if (sleepSemaphore != VK_NULL_HANDLE && destroySemaphore) destroySemaphore(device, sleepSemaphore, nullptr);
    sleepSemaphore = VK_NULL_HANDLE;
    setLatencySleepMode = nullptr; latencySleep = nullptr; setLatencyMarker = nullptr; waitSemaphores = nullptr;
    nvDevice = VK_NULL_HANDLE; nvSwapchain = VK_NULL_HANDLE;
    antiLagUpdate = nullptr; amdDevice = VK_NULL_HANDLE;
    lowLatencyBackend = MajvkBackendNone;
}
// Called with nvMutex held.
void SetMarker(uint64_t frame, VkLatencyMarkerNV marker) {
    if (!setLatencyMarker || nvSwapchain == VK_NULL_HANDLE || frame == 0) return;
    VkSetLatencyMarkerInfoNV info{VK_STRUCTURE_TYPE_SET_LATENCY_MARKER_INFO_NV, nullptr, frame, marker};
    setLatencyMarker(nvDevice, nvSwapchain, &info);
}
void AntiLag(VkAntiLagStageAMD stage, uint64_t frame) {
    const auto update = antiLagUpdate.load();
    const VkDevice device = amdDevice.load();
    if (!update || device == VK_NULL_HANDLE || frame == 0) return;
    VkAntiLagPresentationInfoAMD presentation{VK_STRUCTURE_TYPE_ANTI_LAG_PRESENTATION_INFO_AMD, nullptr, stage, frame};
    VkAntiLagDataAMD data{VK_STRUCTURE_TYPE_ANTI_LAG_DATA_AMD, nullptr, VK_ANTI_LAG_MODE_ON_AMD, 0, &presentation};
    update(device, &data);
}

VKAPI_ATTR VkResult VKAPI_CALL CreateDevice(VkPhysicalDevice physical, const VkDeviceCreateInfo* info,
                                           const VkAllocationCallbacks* allocator, VkDevice* device) {
    {
        std::lock_guard<std::mutex> lock(presentMutex);
        trackedDevice = VK_NULL_HANDLE; trackedSwapchain = VK_NULL_HANDLE; waitForPresent = nullptr;
        presentIdEnabled = false; pendingFrames.clear(); replacedIds.clear(); renderSubmitStarted = false;
        ++swapchainGeneration; lastPresentId = 0;
    }
    presentWaitStatus = MajvkStatusNotEnabledByUnity;
    lowLatencyStatus = MajvkLowLatencyUnsupported;
    lowLatencyBackend = MajvkBackendNone;
    presentIdSource = MajvkPresentIdsNone;

    LowLatencyPlan plan;
    PlanLowLatency(physical, info, plan);
    VkResult result = VK_ERROR_INITIALIZATION_FAILED;
    if (plan.backend != MajvkBackendNone) {
        VkDeviceCreateInfo augmented = *info;
        augmented.pNext = plan.head;
        augmented.enabledExtensionCount = static_cast<uint32_t>(plan.extensions.size());
        augmented.ppEnabledExtensionNames = plan.extensions.data();
        result = nextCreateDevice(physical, &augmented, allocator, device);
        if (result != VK_SUCCESS) {
            // Keep Unity's own configuration working when a driver rejects the backend.
            lowLatencyStatus = MajvkLowLatencyRejected;
            plan.backend = MajvkBackendNone;
        }
    }
    if (plan.backend == MajvkBackendNone) result = nextCreateDevice(physical, info, allocator, device);
    if (result != VK_SUCCESS) return result;

    // The inner layers' device lookup returns driver functions, never this layer's wrappers.
    const auto deviceProc = InstanceFunction<PFN_vkGetDeviceProcAddr>("vkGetDeviceProcAddr");
    if (plan.backend != MajvkBackendNone) {
        if (StartLowLatency(*device, plan.backend, deviceProc)) {
            lowLatencyBackend = plan.backend;
            lowLatencyStatus = MajvkLowLatencyActive;
        } else {
            lowLatencyStatus = MajvkLowLatencyMissingFunction;
            StopLowLatency(*device);
        }
    }
    PFN_vkWaitForPresentKHR wait = nullptr;
    if (EnablesPresentWait(info)) {
        wait = deviceProc ? reinterpret_cast<PFN_vkWaitForPresentKHR>(deviceProc(*device, "vkWaitForPresentKHR")) : nullptr;
        presentWaitStatus = wait ? MajvkStatusEnabled : MajvkStatusMissingFunction;
    }
    std::lock_guard<std::mutex> lock(presentMutex);
    trackedDevice = *device;
    presentIdEnabled = EnablesPresentId(info);
    waitForPresent = presentWaitStatus == MajvkStatusEnabled ? wait : nullptr;
    return result;
}
VKAPI_ATTR void VKAPI_CALL DestroyDevice(VkDevice device, const VkAllocationCallbacks* allocator) {
    const auto destroy = destroyDevice.load(std::memory_order_relaxed);
    bool tracked;
    {
        std::lock_guard<std::mutex> lock(presentMutex);
        tracked = device != VK_NULL_HANDLE && device == trackedDevice;
    }
    if (tracked) {
        StopLowLatency(device);
        std::lock_guard<std::mutex> lock(presentMutex);
        trackedDevice = VK_NULL_HANDLE; trackedSwapchain = VK_NULL_HANDLE; waitForPresent = nullptr;
        pendingFrames.clear(); replacedIds.clear(); ++swapchainGeneration; lastPresentId = 0;
        presentWaitStatus = MajvkStatusNoDevice;
        lowLatencyStatus = MajvkLowLatencyNoDevice;
    }
    destroy(device, allocator);
}
VKAPI_ATTR VkResult VKAPI_CALL CreateSwapchain(VkDevice device, const VkSwapchainCreateInfoKHR* info,
                                              const VkAllocationCallbacks* allocator, VkSwapchainKHR* swapchain) {
    const auto create = createSwapchain.load(std::memory_order_relaxed);
    SwapchainUse use;
    bool tracked;
    {
        std::lock_guard<std::mutex> lock(presentMutex);
        tracked = device == trackedDevice;
    }
    // NVIDIA's latency calls need swapchains created with latency mode enabled.
    VkSwapchainLatencyCreateInfoNV latency{VK_STRUCTURE_TYPE_SWAPCHAIN_LATENCY_CREATE_INFO_NV, info->pNext, VK_TRUE};
    VkSwapchainCreateInfoKHR copy = *info;
    const bool nvidia = tracked && lowLatencyBackend.load() == MajvkBackendNvidia &&
        !FindStructure(info->pNext, VK_STRUCTURE_TYPE_SWAPCHAIN_LATENCY_CREATE_INFO_NV);
    if (nvidia) copy.pNext = &latency;
    const VkResult result = create(device, nvidia ? &copy : info, allocator, swapchain);
    if (result != VK_SUCCESS || !tracked) return result;
    if (lowLatencyBackend.load() == MajvkBackendNvidia) {
        // Sleep mode belongs to a swapchain, so every recreated swapchain gets it again.
        std::lock_guard<std::mutex> lock(nvMutex);
        if (setLatencySleepMode && device == nvDevice) {
            nvSwapchain = *swapchain;
            VkLatencySleepModeInfoNV mode{VK_STRUCTURE_TYPE_LATENCY_SLEEP_MODE_INFO_NV, nullptr, VK_TRUE, VK_FALSE, 0};
            setLatencySleepMode(device, *swapchain, &mode);
        }
    }
    std::lock_guard<std::mutex> lock(presentMutex);
    if (device == trackedDevice) {
        // The newest swapchain is the one Unity presents to; a swapchain it replaces may still present until
        // Unity destroys it, but those presentations are not tracked.
        // Frames begun before it were meant for the old swapchain or none; new presentations take fresh ids.
        trackedSwapchain = *swapchain; renderSubmitStarted = false; pendingFrames.clear(); replacedIds.clear();
        ++swapchainGeneration; lastPresentId = 0;
    }
    return result;
}
VKAPI_ATTR void VKAPI_CALL DestroySwapchain(VkDevice device, VkSwapchainKHR swapchain, const VkAllocationCallbacks* allocator) {
    const auto destroy = destroySwapchain.load(std::memory_order_relaxed);
    SwapchainUse use;
    std::lock_guard<std::mutex> nvLock(nvMutex);
    if (swapchain != VK_NULL_HANDLE && swapchain == nvSwapchain) nvSwapchain = VK_NULL_HANDLE;
    {
        std::lock_guard<std::mutex> lock(presentMutex);
        if (swapchain != VK_NULL_HANDLE && swapchain == trackedSwapchain) {
            trackedSwapchain = VK_NULL_HANDLE; replacedIds.clear();
            ++swapchainGeneration; lastPresentId = 0;
        }
    }
    destroy(device, swapchain, allocator);
}
VKAPI_ATTR VkResult VKAPI_CALL QueueSubmit(VkQueue queue, uint32_t count, const VkSubmitInfo* submits, VkFence fence) {
    const auto submit = queueSubmit.load(std::memory_order_relaxed);
    if (lowLatencyBackend.load() != MajvkBackendNvidia || count == 0) return submit(queue, count, submits, fence);
    uint64_t frame = 0;
    bool first = false;
    {
        std::lock_guard<std::mutex> lock(presentMutex);
        if (trackedSwapchain != VK_NULL_HANDLE && presentIdEnabled && !pendingFrames.empty()) {
            frame = pendingFrames.front();
            first = !renderSubmitStarted; renderSubmitStarted = true;
        }
    }
    if (frame == 0) return submit(queue, count, submits, fence);
    if (first) {
        std::lock_guard<std::mutex> lock(nvMutex);
        SetMarker(frame, VK_LATENCY_MARKER_RENDERSUBMIT_START_NV);
    }
    // Associates the GPU work with the frame it renders.
    std::vector<VkLatencySubmissionPresentIdNV> ids(count);
    std::vector<VkSubmitInfo> copies(submits, submits + count);
    for (uint32_t i = 0; i < count; ++i) {
        if (FindStructure(submits[i].pNext, VK_STRUCTURE_TYPE_LATENCY_SUBMISSION_PRESENT_ID_NV)) continue;
        ids[i] = {VK_STRUCTURE_TYPE_LATENCY_SUBMISSION_PRESENT_ID_NV, submits[i].pNext, frame};
        copies[i].pNext = &ids[i];
    }
    return submit(queue, count, copies.data(), fence);
}
VKAPI_ATTR VkResult VKAPI_CALL QueuePresent(VkQueue queue, const VkPresentInfoKHR* info) {
    const auto present = queuePresent.load(std::memory_order_relaxed);
    SwapchainUse use;
    uint32_t tracked = info->swapchainCount;
    VkSwapchainKHR swapchain = VK_NULL_HANDLE;
    // Zero leaves the other swapchains of a multi-swapchain present without an id.
    std::vector<uint64_t> ids;
    uint64_t frame = 0, trackedId = 0;
    bool submitted = false;
    const VkPresentIdKHR* replaced = nullptr;
    {
        std::lock_guard<std::mutex> lock(presentMutex);
        if (trackedSwapchain != VK_NULL_HANDLE) {
            for (uint32_t i = 0; i < info->swapchainCount; ++i)
                if (info->pSwapchains[i] == trackedSwapchain) { tracked = i; break; }
        }
        if (tracked != info->swapchainCount) {
            swapchain = trackedSwapchain;
            if (!pendingFrames.empty()) { frame = pendingFrames.front(); pendingFrames.pop_front(); }
            else frame = ++frameCounter;
            submitted = renderSubmitStarted; renderSubmitStarted = false;
            if (presentIdEnabled) {
                const auto* existing = reinterpret_cast<const VkPresentIdKHR*>(FindStructure(info->pNext, VK_STRUCTURE_TYPE_PRESENT_ID_KHR));
                const uint64_t unityId = existing && existing->pPresentIds && tracked < existing->swapchainCount ? existing->pPresentIds[tracked] : 0;
                if (existing && unityId != 0 && lowLatencyBackend.load() == MajvkBackendNvidia &&
                    info->pNext == existing && existing->swapchainCount == info->swapchainCount) {
                    // NVIDIA's latency markers must name the presentation with the frame id; take the ids over and
                    // remember Unity's so that its own waits still find the right presentation.
                    replaced = existing;
                    ids.assign(existing->pPresentIds, existing->pPresentIds + existing->swapchainCount);
                    trackedId = ids[tracked] = frame;
                    replacedIds.emplace_back(unityId, frame);
                    if (replacedIds.size() > MaxReplacedIds) replacedIds.pop_front();
                    presentIdSource = MajvkPresentIdsUnityReplaced;
                } else if (existing) {
                    // Unity attaches its own ids when it uses present waiting itself; follow them.
                    trackedId = unityId;
                    if (trackedId > frameCounter) frameCounter = trackedId;
                    presentIdSource = MajvkPresentIdsUnity;
                } else {
                    ids.assign(info->swapchainCount, 0);
                    trackedId = ids[tracked] = frame;
                    presentIdSource = MajvkPresentIdsLayer;
                }
            }
        }
    }
    if (tracked == info->swapchainCount) return present(queue, info);
    const int backend = lowLatencyBackend.load();
    const uint64_t markerFrame = trackedId ? trackedId : frame;
    if (backend == MajvkBackendNvidia) {
        std::lock_guard<std::mutex> lock(nvMutex);
        if (swapchain == nvSwapchain) {
            if (submitted) SetMarker(markerFrame, VK_LATENCY_MARKER_RENDERSUBMIT_END_NV);
            SetMarker(markerFrame, VK_LATENCY_MARKER_PRESENT_START_NV);
        }
    } else if (backend == MajvkBackendAmd) {
        AntiLag(VK_ANTI_LAG_STAGE_PRESENT_AMD, frame);
    }
    // A replaced chain of Unity's is the first structure, so skipping it keeps the rest of Unity's chain intact.
    VkPresentIdKHR presentId{VK_STRUCTURE_TYPE_PRESENT_ID_KHR, replaced ? replaced->pNext : info->pNext, info->swapchainCount, ids.data()};
    VkPresentInfoKHR copy = *info;
    copy.pNext = &presentId;
    const VkResult result = present(queue, ids.empty() ? info : &copy);
    if (backend == MajvkBackendNvidia) {
        std::lock_guard<std::mutex> lock(nvMutex);
        if (swapchain == nvSwapchain) SetMarker(markerFrame, VK_LATENCY_MARKER_PRESENT_END_NV);
    }
    const VkResult trackedResult = info->pResults ? info->pResults[tracked] : result;
    if (trackedId != 0 && (trackedResult == VK_SUCCESS || trackedResult == VK_SUBOPTIMAL_KHR)) {
        std::lock_guard<std::mutex> lock(presentMutex);
        if (info->pSwapchains[tracked] == trackedSwapchain && trackedId > lastPresentId) lastPresentId = trackedId;
    }
    return result;
}
VKAPI_ATTR VkResult VKAPI_CALL AcquireNextImage(VkDevice device, VkSwapchainKHR swapchain, uint64_t timeout,
    VkSemaphore semaphore, VkFence fence, uint32_t* index) {
    const auto acquire = acquireNextImage.load(std::memory_order_relaxed);
    SwapchainUse use;
    return acquire(device, swapchain, timeout, semaphore, fence, index);
}
VKAPI_ATTR VkResult VKAPI_CALL AcquireNextImage2(VkDevice device, const VkAcquireNextImageInfoKHR* info, uint32_t* index) {
    const auto acquire = acquireNextImage2.load(std::memory_order_relaxed);
    SwapchainUse use;
    return acquire(device, info, index);
}
// Unity's own present waits share the swapchain with the managed poll.
VKAPI_ATTR VkResult VKAPI_CALL WaitForPresent(VkDevice device, VkSwapchainKHR swapchain, uint64_t presentId, uint64_t timeout) {
    const auto wait = unityWaitForPresent.load(std::memory_order_relaxed);
    uint64_t translated = presentId;
    {
        // Unity waits for its own ids; after the NVIDIA backend replaced them, wait for the matching frame id.
        std::lock_guard<std::mutex> lock(presentMutex);
        if (swapchain == trackedSwapchain && !replacedIds.empty()) {
            const auto& newest = replacedIds.back();
            if (presentId >= newest.first) translated = newest.second + (presentId - newest.first);
            else {
                translated = replacedIds.front().second;
                for (const auto& entry : replacedIds)
                    if (entry.first <= presentId) translated = entry.second;
            }
        }
    }
    SwapchainUse use;
    return wait(device, swapchain, translated, timeout);
}

PFN_vkVoidFunction WrapDeviceFunction(PFN_vkVoidFunction function, const char* name) {
    if (!function) return nullptr;
#define WRAP(text, slot, type, wrapper) \
    if (!std::strcmp(name, text)) { slot = reinterpret_cast<type>(function); return reinterpret_cast<PFN_vkVoidFunction>(wrapper); }
    WRAP("vkDestroyDevice", destroyDevice, PFN_vkDestroyDevice, DestroyDevice)
    WRAP("vkAcquireNextImageKHR", acquireNextImage, PFN_vkAcquireNextImageKHR, AcquireNextImage)
    WRAP("vkAcquireNextImage2KHR", acquireNextImage2, PFN_vkAcquireNextImage2KHR, AcquireNextImage2)
    WRAP("vkQueuePresentKHR", queuePresent, PFN_vkQueuePresentKHR, QueuePresent)
    WRAP("vkQueueSubmit", queueSubmit, PFN_vkQueueSubmit, QueueSubmit)
    WRAP("vkCreateSwapchainKHR", createSwapchain, PFN_vkCreateSwapchainKHR, CreateSwapchain)
    WRAP("vkDestroySwapchainKHR", destroySwapchain, PFN_vkDestroySwapchainKHR, DestroySwapchain)
    WRAP("vkWaitForPresentKHR", unityWaitForPresent, PFN_vkWaitForPresentKHR, WaitForPresent)
#undef WRAP
    return function;
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL GetDeviceProc(VkDevice device, const char* name) {
    const auto next = nextDeviceProc.load();
    return next ? WrapDeviceFunction(next(device, name), name) : nullptr;
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL GetInstanceProc(VkInstance instance, const char* name) {
    const auto function = nextInstanceProc(instance, name);
    if (!std::strcmp(name, "vkGetDeviceProcAddr") && function) {
        nextDeviceProc = reinterpret_cast<PFN_vkGetDeviceProcAddr>(function);
        return reinterpret_cast<PFN_vkVoidFunction>(GetDeviceProc);
    }
    if (!std::strcmp(name, "vkCreateDevice") && function) {
        nextCreateDevice = reinterpret_cast<PFN_vkCreateDevice>(function);
        deviceInstance = instance;
        return reinterpret_cast<PFN_vkVoidFunction>(CreateDevice);
    }
    return WrapDeviceFunction(function, name);
}
// Unity passes the next vkGetInstanceProcAddr in its interceptor chain on every Vulkan initialization.
PFN_vkGetInstanceProcAddr UNITY_INTERFACE_API Initialize(PFN_vkGetInstanceProcAddr next, void*) {
    nextInstanceProc = next;
    presentWaitStatus = MajvkStatusNoDevice;
    lowLatencyStatus = MajvkLowLatencyNoDevice;
    lowLatencyBackend = MajvkBackendNone;
    return GetInstanceProc;
}
} // namespace

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* interfaces) {
    // GetInterfaceSplit avoids passing UnityInterfaceGUID by value, whose ABI differs between MinGW and MSVC on Win32.
    const auto id = GetUnityInterfaceGUID<IUnityGraphicsVulkanV2>();
    auto* vulkan = static_cast<IUnityGraphicsVulkanV2*>(interfaces->GetInterfaceSplit(id.m_GUIDHigh, id.m_GUIDLow));
    if (vulkan && vulkan->AddInterceptInitialization && vulkan->AddInterceptInitialization(Initialize, nullptr, InterceptPriority))
        unityVulkan = vulkan;
}
extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload() {
    if (unityVulkan && unityVulkan->RemoveInterceptInitialization) unityVulkan->RemoveInterceptInitialization(Initialize);
    unityVulkan = nullptr;
}

MAJVK_EXPORT int MAJVK_CALL majvk_abi_version() { return 2; }
MAJVK_EXPORT int MAJVK_CALL majvk_present_wait_status() { return presentWaitStatus.load(); }
MAJVK_EXPORT int MAJVK_CALL majvk_present_wait_query(uint64_t* generation, uint64_t* presentId) {
    if (!generation || !presentId) return -3;
    if (presentWaitStatus.load() != MajvkStatusEnabled) return -2;
    *generation = swapchainGeneration.load();
    *presentId = lastPresentId.load();
    return 1;
}
MAJVK_EXPORT int MAJVK_CALL majvk_present_wait_poll(uint64_t generation, uint64_t presentId) {
    if (presentWaitStatus.load() != MajvkStatusEnabled) return -2;
    std::unique_lock<std::mutex> lock(presentMutex, std::try_to_lock);
    if (!lock.owns_lock() || swapchainUsers || !waitForPresent) return 0;
    if (trackedSwapchain == VK_NULL_HANDLE || generation != swapchainGeneration.load()) return -1;
    if (presentId == 0 || presentId > lastPresentId.load()) return 0;
    const VkResult result = waitForPresent(trackedDevice, trackedSwapchain, presentId, 0);
    if (result == VK_SUCCESS || result == VK_SUBOPTIMAL_KHR) return 1;
    if (result == VK_TIMEOUT) return 0;
    return static_cast<int>(result);
}
MAJVK_EXPORT int MAJVK_CALL majvk_low_latency_backend() { return lowLatencyBackend.load(); }
MAJVK_EXPORT int MAJVK_CALL majvk_low_latency_status() { return lowLatencyStatus.load(); }
MAJVK_EXPORT int MAJVK_CALL majvk_frame_begin(uint64_t timeoutNanoseconds) {
    const int backend = lowLatencyBackend.load();
    if (backend == MajvkBackendNone) return MajvkFrameInactive;
    uint64_t frame;
    {
        std::lock_guard<std::mutex> lock(presentMutex);
        frame = ++frameCounter;
        pendingFrames.push_back(frame);
        if (pendingFrames.size() > MaxPendingFrames) pendingFrames.pop_front();
        simulatedFrame = frame;
    }
    if (backend == MajvkBackendAmd) {
        AntiLag(VK_ANTI_LAG_STAGE_INPUT_AMD, frame);
        return MajvkFrameDone;
    }
    VkDevice device;
    VkSemaphore semaphore;
    PFN_vkWaitSemaphoresKHR wait;
    uint64_t value;
    {
        std::lock_guard<std::mutex> lock(nvMutex);
        if (nvSwapchain == VK_NULL_HANDLE || !latencySleep) return MajvkFrameSkipped;
        device = nvDevice; semaphore = sleepSemaphore; wait = waitSemaphores;
        value = ++sleepValue;
        VkLatencySleepInfoNV sleep{VK_STRUCTURE_TYPE_LATENCY_SLEEP_INFO_NV, nullptr, semaphore, value};
        const VkResult result = latencySleep(device, nvSwapchain, &sleep);
        if (result != VK_SUCCESS) return static_cast<int>(result);
    }
    // The driver signals the semaphore when the frame should start; wait without holding any lock.
    VkSemaphoreWaitInfo waitInfo{VK_STRUCTURE_TYPE_SEMAPHORE_WAIT_INFO, nullptr, 0, 1, &semaphore, &value};
    const VkResult waited = wait(device, &waitInfo, timeoutNanoseconds);
    if (waited != VK_SUCCESS && waited != VK_TIMEOUT) return static_cast<int>(waited);
    std::lock_guard<std::mutex> lock(nvMutex);
    SetMarker(frame, VK_LATENCY_MARKER_SIMULATION_START_NV);
    SetMarker(frame, VK_LATENCY_MARKER_INPUT_SAMPLE_NV);
    return waited == VK_TIMEOUT ? MajvkFrameTimedOut : MajvkFrameDone;
}
MAJVK_EXPORT int MAJVK_CALL majvk_frame_end() {
    if (lowLatencyBackend.load() != MajvkBackendNvidia) return lowLatencyBackend.load() == MajvkBackendNone ? MajvkFrameInactive : MajvkFrameSkipped;
    uint64_t frame;
    {
        std::lock_guard<std::mutex> lock(presentMutex);
        frame = simulatedFrame;
    }
    std::lock_guard<std::mutex> lock(nvMutex);
    if (nvSwapchain == VK_NULL_HANDLE || frame == 0) return MajvkFrameSkipped;
    SetMarker(frame, VK_LATENCY_MARKER_SIMULATION_END_NV);
    return MajvkFrameDone;
}
MAJVK_EXPORT uint64_t MAJVK_CALL majvk_swapchain_generation() { return swapchainGeneration.load(); }
MAJVK_EXPORT int MAJVK_CALL majvk_present_id_source() { return presentIdSource.load(); }
