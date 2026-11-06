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
#include <memory>
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
// Frame ids waiting for their presentation. Unity's main thread runs at most one frame ahead of its render thread,
// so more means Unity skipped presentations (e.g. while minimized); the oldest are dropped so that later
// presentations do not stay paired with frames long past.
constexpr size_t MaxPendingFrames = 2;

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
// The swapchain the tracked one replaced (window resize) until Unity destroys it; Unity may still present its
// acquired images, and the layer keeps the ids it numbered on it increasing.
VkSwapchainKHR retiredSwapchain = VK_NULL_HANDLE;
bool presentIdEnabled = false;
PFN_vkWaitForPresentKHR waitForPresent = nullptr;
// One monotonic counter numbers frames begun by the main thread and presentations without such a frame, so
// ids always increase per swapchain. pendingFrames holds begun frames in presentation order.
uint64_t frameCounter = 0;
uint64_t simulatedFrame = 0;
std::deque<uint64_t> pendingFrames;
bool renderSubmitStarted = false;
// Whether the tracked swapchain presented since NVIDIA's last latency sleep; the driver releases one sleep per
// presentation, so frames without one (e.g. while minimized) are not slept.
bool presentedSinceSleep = true;
// Unity's ids that the layer replaced with its frame ids, oldest first, so Unity's own waits can be translated.
struct ReplacedId {
    VkSwapchainKHR swapchain;
    uint64_t unityId;
    uint64_t frameId;
};
std::deque<ReplacedId> replacedIds;
constexpr size_t MaxReplacedIds = 64;
// Readable without the lock; a stale pair is rejected by the generation check.
std::atomic<uint64_t> swapchainGeneration{0};
std::atomic<uint64_t> lastPresentId{0};
// Set when a call on Unity's device reports VK_ERROR_DEVICE_LOST, and cleared when Unity creates a new device. A
// lost device stays lost, so from then on the layer adds no ids, markers or sleeps and never waits in the driver.
std::atomic<bool> deviceLost{false};
std::atomic<int> deviceLostCall{MajvkCallNone};

// nvMutex guards nvDevice/nvSwapchain and serializes the NVIDIA latency calls, which take the swapchain, with each
// other; swapchain destruction clears nvSwapchain under it first, so no latency call reaches a destroyed swapchain.
// Those calls never block.
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
// Latches a device loss reported by a call, remembering the first call that reported it, and returns the result.
VkResult Observe(VkResult result, int call) {
    if (result == VK_ERROR_DEVICE_LOST) {
        int none = MajvkCallNone;
        deviceLostCall.compare_exchange_strong(none, call);
        deviceLost = true;
    }
    return result;
}
// Returns the index of a swapchain in a presentation, or swapchainCount when it is not presented.
uint32_t FindSwapchain(const VkPresentInfoKHR* info, VkSwapchainKHR swapchain) {
    if (swapchain == VK_NULL_HANDLE) return info->swapchainCount;
    for (uint32_t i = 0; i < info->swapchainCount; ++i)
        if (info->pSwapchains[i] == swapchain) return i;
    return info->swapchainCount;
}
// Returns the size of a structure that Vulkan (headers 1.3.275) allows in a presentation's pNext chain besides
// VkPresentIdKHR, or 0 for a type the layer does not know.
size_t PresentStructureSize(VkStructureType type) {
    switch (type) {
    case VK_STRUCTURE_TYPE_DEVICE_GROUP_PRESENT_INFO_KHR: return sizeof(VkDeviceGroupPresentInfoKHR);
    case VK_STRUCTURE_TYPE_DISPLAY_PRESENT_INFO_KHR: return sizeof(VkDisplayPresentInfoKHR);
    case VK_STRUCTURE_TYPE_PRESENT_REGIONS_KHR: return sizeof(VkPresentRegionsKHR);
    case VK_STRUCTURE_TYPE_PRESENT_TIMES_INFO_GOOGLE: return sizeof(VkPresentTimesInfoGOOGLE);
    case VK_STRUCTURE_TYPE_SWAPCHAIN_PRESENT_FENCE_INFO_EXT: return sizeof(VkSwapchainPresentFenceInfoEXT);
    case VK_STRUCTURE_TYPE_SWAPCHAIN_PRESENT_MODE_INFO_EXT: return sizeof(VkSwapchainPresentModeInfoEXT);
    case VK_STRUCTURE_TYPE_FRAME_BOUNDARY_EXT: return sizeof(VkFrameBoundaryEXT);
    default: return 0;
    }
}
// Builds a presentation chain in which `replacement` takes the place of Unity's VkPresentIdKHR. The structures in
// front of Unity's are copied into `copies` and the ones behind it are shared, so Unity's memory is never modified.
// Returns the new chain head, or nullptr when a structure in front of Unity's has a type the layer cannot copy.
const void* ReplacePresentId(const void* head, const VkPresentIdKHR* unity, VkPresentIdKHR& replacement,
                             std::vector<std::unique_ptr<unsigned char[]>>& copies) {
    std::vector<const VkBaseInStructure*> front;
    const auto* target = reinterpret_cast<const VkBaseInStructure*>(unity);
    for (auto* node = static_cast<const VkBaseInStructure*>(head); node != target; node = node->pNext) {
        if (!node || !PresentStructureSize(node->sType)) return nullptr;
        front.push_back(node);
    }
    replacement.pNext = unity->pNext;
    const void* next = &replacement;
    // Copied back to front, so each copy can point at the one after it.
    for (auto structure = front.rbegin(); structure != front.rend(); ++structure) {
        const size_t size = PresentStructureSize((*structure)->sType);
        auto copy = std::make_unique<unsigned char[]>(size);
        std::memcpy(copy.get(), *structure, size);
        reinterpret_cast<VkBaseOutStructure*>(copy.get())->pNext = static_cast<VkBaseOutStructure*>(const_cast<void*>(next));
        next = copy.get();
        copies.push_back(std::move(copy));
    }
    return next;
}
// Called with presentMutex held. Remembers a replaced Unity id for translating Unity's own waits.
void RememberUnityId(VkSwapchainKHR swapchain, uint64_t unityId, uint64_t frameId) {
    replacedIds.push_back({swapchain, unityId, frameId});
    if (replacedIds.size() > MaxReplacedIds) replacedIds.pop_front();
}
// Called with presentMutex held. Forgets the replaced ids of a swapchain handle that was destroyed or reissued.
void ForgetUnityIds(VkSwapchainKHR swapchain) {
    for (auto entry = replacedIds.begin(); entry != replacedIds.end();)
        entry = entry->swapchain == swapchain ? replacedIds.erase(entry) : entry + 1;
}
// Called with presentMutex held. Translates the id of Unity's own wait into the frame id the layer presented in its
// place. An id older than the remembered ones maps to the oldest, which has been presented; an id newer than every
// replaced one is a presentation still to come and maps to as many frame ids after the newest, which the
// presentations to come reach no later than Unity's own ids would, because frame ids grow at least as fast.
uint64_t TranslateUnityId(VkSwapchainKHR swapchain, uint64_t presentId) {
    const ReplacedId* oldest = nullptr;
    const ReplacedId* newest = nullptr;
    const ReplacedId* covering = nullptr;
    for (const auto& entry : replacedIds) {
        if (entry.swapchain != swapchain) continue;
        if (!oldest) oldest = &entry;
        newest = &entry;
        if (entry.unityId <= presentId) covering = &entry;
    }
    if (!newest) return presentId;
    if (presentId >= newest->unityId) return newest->frameId + (presentId - newest->unityId);
    return covering ? covering->frameId : oldest->frameId;
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
        trackedDevice = VK_NULL_HANDLE; trackedSwapchain = VK_NULL_HANDLE; retiredSwapchain = VK_NULL_HANDLE;
        waitForPresent = nullptr; presentIdEnabled = false; pendingFrames.clear(); replacedIds.clear();
        renderSubmitStarted = false; presentedSinceSleep = true;
        ++swapchainGeneration; lastPresentId = 0;
        deviceLost = false; deviceLostCall = MajvkCallNone;
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
        trackedDevice = VK_NULL_HANDLE; trackedSwapchain = VK_NULL_HANDLE; retiredSwapchain = VK_NULL_HANDLE;
        waitForPresent = nullptr; pendingFrames.clear(); replacedIds.clear(); ++swapchainGeneration; lastPresentId = 0;
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
    // NVIDIA's latency calls need swapchains created with latency mode enabled; a lost device gets none of it.
    const bool nvidia = tracked && !deviceLost.load() && lowLatencyBackend.load() == MajvkBackendNvidia;
    VkSwapchainLatencyCreateInfoNV latency{VK_STRUCTURE_TYPE_SWAPCHAIN_LATENCY_CREATE_INFO_NV, info->pNext, VK_TRUE};
    VkSwapchainCreateInfoKHR copy = *info;
    const bool addLatency = nvidia && !FindStructure(info->pNext, VK_STRUCTURE_TYPE_SWAPCHAIN_LATENCY_CREATE_INFO_NV);
    if (addLatency) copy.pNext = &latency;
    const VkResult result = Observe(create(device, addLatency ? &copy : info, allocator, swapchain), MajvkCallCreateSwapchain);
    if (!tracked) return result;
    if (result != VK_SUCCESS) {
        // The old swapchain is retired even when its replacement could not be created, so it can no longer be
        // waited on or slept on; it may still present its acquired images.
        const VkSwapchainKHR old = info->oldSwapchain;
        if (old != VK_NULL_HANDLE) {
            {
                std::lock_guard<std::mutex> nvLock(nvMutex);
                if (old == nvSwapchain) nvSwapchain = VK_NULL_HANDLE;
            }
            std::lock_guard<std::mutex> lock(presentMutex);
            if (old == trackedSwapchain) {
                retiredSwapchain = old; trackedSwapchain = VK_NULL_HANDLE;
                ++swapchainGeneration; lastPresentId = 0;
            }
        }
        return result;
    }
    if (nvidia) {
        // Sleep mode belongs to a swapchain, so every recreated swapchain gets it again. Without it the driver
        // never releases a sleep, so a swapchain whose sleep mode failed is not slept on.
        std::lock_guard<std::mutex> lock(nvMutex);
        if (setLatencySleepMode && device == nvDevice) {
            VkLatencySleepModeInfoNV mode{VK_STRUCTURE_TYPE_LATENCY_SLEEP_MODE_INFO_NV, nullptr, VK_TRUE, VK_FALSE, 0};
            const VkResult set = Observe(setLatencySleepMode(device, *swapchain, &mode), MajvkCallSetLatencySleepMode);
            nvSwapchain = set == VK_SUCCESS ? *swapchain : VK_NULL_HANDLE;
        }
    }
    std::lock_guard<std::mutex> lock(presentMutex);
    if (device == trackedDevice) {
        // The newest swapchain is the one Unity presents to; a swapchain it replaces may still present until
        // Unity destroys it, and keeps increasing ids then. Frames already begun keep their ids: they are the
        // frames Unity presents next, now on the new swapchain.
        if (info->oldSwapchain != VK_NULL_HANDLE && info->oldSwapchain == trackedSwapchain) retiredSwapchain = trackedSwapchain;
        trackedSwapchain = *swapchain; renderSubmitStarted = false; presentedSinceSleep = true;
        ForgetUnityIds(*swapchain);
        ++swapchainGeneration; lastPresentId = 0;
    }
    return result;
}
VKAPI_ATTR void VKAPI_CALL DestroySwapchain(VkDevice device, VkSwapchainKHR swapchain, const VkAllocationCallbacks* allocator) {
    const auto destroy = destroySwapchain.load(std::memory_order_relaxed);
    SwapchainUse use;
    {
        // Once cleared, no latency call can reach the swapchain, so the driver's destruction, which may be slow,
        // runs without holding the lock the main thread's frame calls need.
        std::lock_guard<std::mutex> nvLock(nvMutex);
        if (swapchain != VK_NULL_HANDLE && swapchain == nvSwapchain) nvSwapchain = VK_NULL_HANDLE;
    }
    {
        std::lock_guard<std::mutex> lock(presentMutex);
        if (swapchain != VK_NULL_HANDLE && swapchain == trackedSwapchain) {
            trackedSwapchain = VK_NULL_HANDLE;
            ++swapchainGeneration; lastPresentId = 0;
        }
        if (swapchain != VK_NULL_HANDLE && swapchain == retiredSwapchain) retiredSwapchain = VK_NULL_HANDLE;
        ForgetUnityIds(swapchain);
    }
    destroy(device, swapchain, allocator);
}
VKAPI_ATTR VkResult VKAPI_CALL QueueSubmit(VkQueue queue, uint32_t count, const VkSubmitInfo* submits, VkFence fence) {
    const auto submit = queueSubmit.load(std::memory_order_relaxed);
    if (deviceLost.load() || lowLatencyBackend.load() != MajvkBackendNvidia || count == 0)
        return Observe(submit(queue, count, submits, fence), MajvkCallQueueSubmit);
    uint64_t frame = 0;
    bool first = false;
    {
        std::lock_guard<std::mutex> lock(presentMutex);
        if (trackedSwapchain != VK_NULL_HANDLE && presentIdEnabled && !pendingFrames.empty()) {
            frame = pendingFrames.front();
            first = !renderSubmitStarted; renderSubmitStarted = true;
        }
    }
    if (frame == 0) return Observe(submit(queue, count, submits, fence), MajvkCallQueueSubmit);
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
    return Observe(submit(queue, count, copies.data(), fence), MajvkCallQueueSubmit);
}
VKAPI_ATTR VkResult VKAPI_CALL QueuePresent(VkQueue queue, const VkPresentInfoKHR* info) {
    const auto present = queuePresent.load(std::memory_order_relaxed);
    // A lost device shows nothing more; Unity's presentation passes through untouched.
    if (deviceLost.load()) return Observe(present(queue, info), MajvkCallQueuePresent);
    SwapchainUse use;
    // The swapchain whose id the layer sets: the tracked one, or else the retired one.
    uint32_t numbered = info->swapchainCount;
    bool tracked = false;
    VkSwapchainKHR swapchain = VK_NULL_HANDLE;
    // Zero leaves the other swapchains of a multi-swapchain present without an id.
    std::vector<uint64_t> ids;
    uint64_t frame = 0, trackedId = 0;
    bool submitted = false;
    VkPresentIdKHR presentId{VK_STRUCTURE_TYPE_PRESENT_ID_KHR, info->pNext, info->swapchainCount, nullptr};
    std::vector<std::unique_ptr<unsigned char[]>> copies;
    // The presentation's chain with the layer's ids, or null to present Unity's chain unchanged.
    const void* chain = nullptr;
    {
        std::lock_guard<std::mutex> lock(presentMutex);
        numbered = FindSwapchain(info, trackedSwapchain);
        tracked = numbered != info->swapchainCount;
        if (!tracked) numbered = FindSwapchain(info, retiredSwapchain);
        if (numbered != info->swapchainCount) {
            swapchain = info->pSwapchains[numbered];
            if (tracked) {
                if (!pendingFrames.empty()) { frame = pendingFrames.front(); pendingFrames.pop_front(); }
                else frame = ++frameCounter;
                submitted = renderSubmitStarted; renderSubmitStarted = false;
                presentedSinceSleep = true;
            } else {
                frame = ++frameCounter;
            }
            const auto* existing = presentIdEnabled
                ? reinterpret_cast<const VkPresentIdKHR*>(FindStructure(info->pNext, VK_STRUCTURE_TYPE_PRESENT_ID_KHR))
                : nullptr;
            const void* replaced = existing && existing->swapchainCount == info->swapchainCount
                ? ReplacePresentId(info->pNext, existing, presentId, copies)
                : nullptr;
            if (replaced) {
                // Unity attaches its own ids when it uses present waiting itself. The layer numbers every
                // presentation, so ids keep increasing per swapchain and NVIDIA's latency markers can name them by
                // frame id; Unity's ids are remembered so that its own waits still find the right presentation.
                if (existing->pPresentIds) ids.assign(existing->pPresentIds, existing->pPresentIds + info->swapchainCount);
                else ids.assign(info->swapchainCount, 0);
                const uint64_t unityId = ids[numbered];
                ids[numbered] = frame;
                if (unityId != 0) RememberUnityId(swapchain, unityId, frame);
                if (tracked) { trackedId = frame; presentIdSource = MajvkPresentIdsUnityReplaced; }
                chain = replaced;
            } else if (existing) {
                // A structure the layer cannot copy sits in front of Unity's ids; follow Unity's id instead.
                if (tracked && existing->pPresentIds && numbered < existing->swapchainCount) {
                    trackedId = existing->pPresentIds[numbered];
                    if (trackedId > frameCounter) frameCounter = trackedId;
                    presentIdSource = MajvkPresentIdsUnity;
                }
            } else if (presentIdEnabled && tracked) {
                ids.assign(info->swapchainCount, 0);
                trackedId = ids[numbered] = frame;
                presentIdSource = MajvkPresentIdsLayer;
                chain = &presentId;
            }
            presentId.pPresentIds = ids.data();
        }
    }
    if (numbered == info->swapchainCount) return Observe(present(queue, info), MajvkCallQueuePresent);
    // Presentations of the retired swapchain only keep their ids increasing; they are not frames of the backends.
    const int backend = tracked ? lowLatencyBackend.load() : MajvkBackendNone;
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
    VkPresentInfoKHR copy = *info;
    copy.pNext = chain;
    const VkResult result = Observe(present(queue, chain ? &copy : info), MajvkCallQueuePresent);
    if (backend == MajvkBackendNvidia) {
        std::lock_guard<std::mutex> lock(nvMutex);
        if (swapchain == nvSwapchain) SetMarker(markerFrame, VK_LATENCY_MARKER_PRESENT_END_NV);
    }
    const VkResult trackedResult = info->pResults ? info->pResults[numbered] : result;
    if (tracked && trackedId != 0 && (trackedResult == VK_SUCCESS || trackedResult == VK_SUBOPTIMAL_KHR)) {
        std::lock_guard<std::mutex> lock(presentMutex);
        if (info->pSwapchains[numbered] == trackedSwapchain && trackedId > lastPresentId) lastPresentId = trackedId;
    }
    return result;
}
VKAPI_ATTR VkResult VKAPI_CALL AcquireNextImage(VkDevice device, VkSwapchainKHR swapchain, uint64_t timeout,
    VkSemaphore semaphore, VkFence fence, uint32_t* index) {
    const auto acquire = acquireNextImage.load(std::memory_order_relaxed);
    SwapchainUse use;
    return Observe(acquire(device, swapchain, timeout, semaphore, fence, index), MajvkCallAcquireNextImage);
}
VKAPI_ATTR VkResult VKAPI_CALL AcquireNextImage2(VkDevice device, const VkAcquireNextImageInfoKHR* info, uint32_t* index) {
    const auto acquire = acquireNextImage2.load(std::memory_order_relaxed);
    SwapchainUse use;
    return Observe(acquire(device, info, index), MajvkCallAcquireNextImage);
}
// Unity's own present waits share the swapchain with the managed poll.
VKAPI_ATTR VkResult VKAPI_CALL WaitForPresent(VkDevice device, VkSwapchainKHR swapchain, uint64_t presentId, uint64_t timeout) {
    const auto wait = unityWaitForPresent.load(std::memory_order_relaxed);
    // Nothing is presented on a lost device any more, so a wait could only end by its timeout, which Unity may
    // not have bounded; the specification allows reporting the loss instead.
    if (deviceLost.load()) return VK_ERROR_DEVICE_LOST;
    uint64_t translated;
    {
        // Unity waits for its own ids; the layer presented frame ids in their place.
        std::lock_guard<std::mutex> lock(presentMutex);
        translated = TranslateUnityId(swapchain, presentId);
    }
    SwapchainUse use;
    return Observe(wait(device, swapchain, translated, timeout), MajvkCallUnityWaitForPresent);
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

MAJVK_EXPORT int MAJVK_CALL majvk_abi_version() { return 3; }
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
    if (deviceLost.load()) return VK_ERROR_DEVICE_LOST;
    std::unique_lock<std::mutex> lock(presentMutex, std::try_to_lock);
    if (!lock.owns_lock() || swapchainUsers || !waitForPresent) return 0;
    if (trackedSwapchain == VK_NULL_HANDLE || generation != swapchainGeneration.load()) return -1;
    if (presentId == 0 || presentId > lastPresentId.load()) return 0;
    const VkResult result = Observe(waitForPresent(trackedDevice, trackedSwapchain, presentId, 0), MajvkCallPresentWaitPoll);
    if (result == VK_SUCCESS || result == VK_SUBOPTIMAL_KHR) return 1;
    if (result == VK_TIMEOUT) return 0;
    return static_cast<int>(result);
}
MAJVK_EXPORT int MAJVK_CALL majvk_low_latency_backend() { return lowLatencyBackend.load(); }
MAJVK_EXPORT int MAJVK_CALL majvk_low_latency_status() { return lowLatencyStatus.load(); }
MAJVK_EXPORT int MAJVK_CALL majvk_frame_begin(uint64_t timeoutNanoseconds) {
    const int backend = lowLatencyBackend.load();
    if (backend == MajvkBackendNone) return MajvkFrameInactive;
    if (deviceLost.load()) return VK_ERROR_DEVICE_LOST;
    uint64_t frame;
    bool presented;
    {
        std::lock_guard<std::mutex> lock(presentMutex);
        frame = ++frameCounter;
        pendingFrames.push_back(frame);
        if (pendingFrames.size() > MaxPendingFrames) pendingFrames.pop_front();
        simulatedFrame = frame;
        presented = presentedSinceSleep;
        presentedSinceSleep = false;
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
        if (!presented) {
            // The driver releases one sleep per presentation; without one (e.g. while minimized) the sleep would
            // only run into the timeout. The frame still starts for the markers.
            SetMarker(frame, VK_LATENCY_MARKER_SIMULATION_START_NV);
            SetMarker(frame, VK_LATENCY_MARKER_INPUT_SAMPLE_NV);
            return MajvkFrameSkipped;
        }
        device = nvDevice; semaphore = sleepSemaphore; wait = waitSemaphores;
        value = ++sleepValue;
        VkLatencySleepInfoNV sleep{VK_STRUCTURE_TYPE_LATENCY_SLEEP_INFO_NV, nullptr, semaphore, value};
        const VkResult result = Observe(latencySleep(device, nvSwapchain, &sleep), MajvkCallLatencySleep);
        if (result != VK_SUCCESS) return static_cast<int>(result);
    }
    // The driver signals the semaphore when the frame should start; wait without holding any lock.
    VkSemaphoreWaitInfo waitInfo{VK_STRUCTURE_TYPE_SEMAPHORE_WAIT_INFO, nullptr, 0, 1, &semaphore, &value};
    const VkResult waited = Observe(wait(device, &waitInfo, timeoutNanoseconds), MajvkCallWaitSemaphores);
    if (waited != VK_SUCCESS && waited != VK_TIMEOUT) return static_cast<int>(waited);
    std::lock_guard<std::mutex> lock(nvMutex);
    SetMarker(frame, VK_LATENCY_MARKER_SIMULATION_START_NV);
    SetMarker(frame, VK_LATENCY_MARKER_INPUT_SAMPLE_NV);
    return waited == VK_TIMEOUT ? MajvkFrameTimedOut : MajvkFrameDone;
}
MAJVK_EXPORT int MAJVK_CALL majvk_frame_end() {
    if (lowLatencyBackend.load() != MajvkBackendNvidia) return lowLatencyBackend.load() == MajvkBackendNone ? MajvkFrameInactive : MajvkFrameSkipped;
    if (deviceLost.load()) return VK_ERROR_DEVICE_LOST;
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
MAJVK_EXPORT int MAJVK_CALL majvk_device_lost_call() { return deviceLostCall.load(); }
