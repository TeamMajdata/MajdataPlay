// Standalone Unity Vulkan layer that lets the managed frame pacer wait until a frame has been shown, like
// D3D11's waitable swapchain. It registers its own Unity initialization interceptor below the maximum
// priority, so other plug-ins (such as the FFmpeg graphics bridge) stay closer to the Vulkan loader and this
// layer sees every Unity swapchain call first. It never changes device creation: Unity 6 already enables
// VK_KHR_present_id and VK_KHR_present_wait when the driver supports them.
#define VK_NO_PROTOTYPES
#include "VulkanPacing.h"
#include "IUnityGraphics.h"
#include "IUnityGraphicsVulkan.h"
#include <atomic>
#include <cstring>
#include <mutex>
#include <vector>

namespace {
// Lower than kUnityVulkanInitCallbackMaxPriority, which Unity reserves for the innermost interceptor.
constexpr int32_t InterceptPriority = 0x40000000;

IUnityGraphicsVulkanV2* unityVulkan = nullptr;
PFN_vkGetInstanceProcAddr nextInstanceProc = nullptr;
std::atomic<PFN_vkGetDeviceProcAddr> nextDeviceProc{nullptr};
PFN_vkCreateDevice nextCreateDevice = nullptr;
VkInstance deviceInstance = VK_NULL_HANDLE;
std::atomic<PFN_vkAcquireNextImageKHR> acquireNextImage{nullptr};
std::atomic<PFN_vkAcquireNextImage2KHR> acquireNextImage2{nullptr};
std::atomic<PFN_vkQueuePresentKHR> queuePresent{nullptr};
std::atomic<PFN_vkCreateSwapchainKHR> createSwapchain{nullptr};
std::atomic<PFN_vkDestroySwapchainKHR> destroySwapchain{nullptr};
std::atomic<PFN_vkWaitForPresentKHR> unityWaitForPresent{nullptr};
std::atomic<int> status{MajvkStatusNoDevice};

// vkWaitForPresentKHR externally synchronizes the swapchain, as acquisition, presentation, waiting and
// destruction do. Unity's own calls register themselves in swapchainUsers under this lock but run unlocked,
// so a blocking acquisition never holds it. The managed poll try-locks it and calls the zero-timeout wait
// only while no Unity call is in progress.
std::mutex presentMutex;
int swapchainUsers = 0;
PFN_vkWaitForPresentKHR waitForPresent = nullptr;
VkDevice presentDevice = VK_NULL_HANDLE;
VkSwapchainKHR presentSwapchain = VK_NULL_HANDLE;
uint64_t nextPresentId = 0;
// Readable without the lock; a stale pair is rejected by the generation check.
std::atomic<uint64_t> presentGeneration{0};
std::atomic<uint64_t> lastPresentId{0};

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
bool HasExtension(const VkDeviceCreateInfo* info, const char* name) {
    for (uint32_t i = 0; i < info->enabledExtensionCount; ++i)
        if (!std::strcmp(info->ppEnabledExtensionNames[i], name)) return true;
    return false;
}
bool EnablesPresentWait(const VkDeviceCreateInfo* info) {
    const auto* id = reinterpret_cast<const VkPhysicalDevicePresentIdFeaturesKHR*>(
        FindStructure(info->pNext, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PRESENT_ID_FEATURES_KHR));
    const auto* wait = reinterpret_cast<const VkPhysicalDevicePresentWaitFeaturesKHR*>(
        FindStructure(info->pNext, VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PRESENT_WAIT_FEATURES_KHR));
    return id && wait && id->presentId && wait->presentWait &&
        HasExtension(info, VK_KHR_PRESENT_ID_EXTENSION_NAME) && HasExtension(info, VK_KHR_PRESENT_WAIT_EXTENSION_NAME);
}

VKAPI_ATTR VkResult VKAPI_CALL CreateDevice(VkPhysicalDevice physical, const VkDeviceCreateInfo* info,
                                           const VkAllocationCallbacks* allocator, VkDevice* device) {
    {
        std::lock_guard<std::mutex> lock(presentMutex);
        presentDevice = VK_NULL_HANDLE; presentSwapchain = VK_NULL_HANDLE; waitForPresent = nullptr;
        ++presentGeneration; lastPresentId = 0;
    }
    status = MajvkStatusNotEnabledByUnity;
    const VkResult result = nextCreateDevice(physical, info, allocator, device);
    if (result != VK_SUCCESS || !EnablesPresentWait(info)) return result;
    // The inner layers' device lookup returns the driver's wait function, never this layer's wrapper.
    const auto deviceProc = reinterpret_cast<PFN_vkGetDeviceProcAddr>(nextInstanceProc(deviceInstance, "vkGetDeviceProcAddr"));
    const auto wait = deviceProc ? reinterpret_cast<PFN_vkWaitForPresentKHR>(deviceProc(*device, "vkWaitForPresentKHR")) : nullptr;
    if (!wait) { status = MajvkStatusMissingFunction; return result; }
    {
        std::lock_guard<std::mutex> lock(presentMutex);
        presentDevice = *device; waitForPresent = wait;
    }
    status = MajvkStatusEnabled;
    return result;
}
VKAPI_ATTR VkResult VKAPI_CALL CreateSwapchain(VkDevice device, const VkSwapchainCreateInfoKHR* info,
                                              const VkAllocationCallbacks* allocator, VkSwapchainKHR* swapchain) {
    const auto create = createSwapchain.load(std::memory_order_relaxed);
    SwapchainUse use;
    const VkResult result = create(device, info, allocator, swapchain);
    if (result == VK_SUCCESS) {
        std::lock_guard<std::mutex> lock(presentMutex);
        if (device == presentDevice) {
            // The newest swapchain is the one Unity presents to; ids restart with it.
            presentSwapchain = *swapchain; nextPresentId = 0;
            ++presentGeneration; lastPresentId = 0;
        }
    }
    return result;
}
VKAPI_ATTR void VKAPI_CALL DestroySwapchain(VkDevice device, VkSwapchainKHR swapchain, const VkAllocationCallbacks* allocator) {
    const auto destroy = destroySwapchain.load(std::memory_order_relaxed);
    SwapchainUse use;
    {
        std::lock_guard<std::mutex> lock(presentMutex);
        if (swapchain != VK_NULL_HANDLE && swapchain == presentSwapchain) {
            presentSwapchain = VK_NULL_HANDLE;
            ++presentGeneration; lastPresentId = 0;
        }
    }
    destroy(device, swapchain, allocator);
}
VKAPI_ATTR VkResult VKAPI_CALL QueuePresent(VkQueue queue, const VkPresentInfoKHR* info) {
    const auto present = queuePresent.load(std::memory_order_relaxed);
    SwapchainUse use;
    uint32_t tracked = info->swapchainCount;
    // Zero leaves the other swapchains of a multi-swapchain present without an id.
    std::vector<uint64_t> ids;
    uint64_t trackedId = 0;
    {
        std::lock_guard<std::mutex> lock(presentMutex);
        if (presentSwapchain != VK_NULL_HANDLE) {
            for (uint32_t i = 0; i < info->swapchainCount; ++i)
                if (info->pSwapchains[i] == presentSwapchain) { tracked = i; break; }
        }
        if (tracked != info->swapchainCount) {
            const auto* existing = reinterpret_cast<const VkPresentIdKHR*>(FindStructure(info->pNext, VK_STRUCTURE_TYPE_PRESENT_ID_KHR));
            if (existing) {
                // Unity attaches its own ids when it uses present waiting itself; follow them.
                trackedId = existing->pPresentIds && tracked < existing->swapchainCount ? existing->pPresentIds[tracked] : 0;
                if (trackedId > nextPresentId) nextPresentId = trackedId;
            } else {
                ids.assign(info->swapchainCount, 0);
                trackedId = ids[tracked] = ++nextPresentId;
            }
        }
    }
    if (tracked == info->swapchainCount) return present(queue, info);
    VkPresentIdKHR presentId{VK_STRUCTURE_TYPE_PRESENT_ID_KHR, info->pNext, info->swapchainCount, ids.data()};
    VkPresentInfoKHR copy = *info;
    copy.pNext = &presentId;
    const VkResult result = present(queue, ids.empty() ? info : &copy);
    const VkResult trackedResult = info->pResults ? info->pResults[tracked] : result;
    if (trackedId != 0 && (trackedResult == VK_SUCCESS || trackedResult == VK_SUBOPTIMAL_KHR)) {
        std::lock_guard<std::mutex> lock(presentMutex);
        if (info->pSwapchains[tracked] == presentSwapchain && trackedId > lastPresentId) lastPresentId = trackedId;
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
    SwapchainUse use;
    return wait(device, swapchain, presentId, timeout);
}

PFN_vkVoidFunction WrapSwapchainFunction(PFN_vkVoidFunction function, const char* name) {
    if (!function) return nullptr;
#define WRAP(text, slot, type, wrapper) \
    if (!std::strcmp(name, text)) { slot = reinterpret_cast<type>(function); return reinterpret_cast<PFN_vkVoidFunction>(wrapper); }
    WRAP("vkAcquireNextImageKHR", acquireNextImage, PFN_vkAcquireNextImageKHR, AcquireNextImage)
    WRAP("vkAcquireNextImage2KHR", acquireNextImage2, PFN_vkAcquireNextImage2KHR, AcquireNextImage2)
    WRAP("vkQueuePresentKHR", queuePresent, PFN_vkQueuePresentKHR, QueuePresent)
    WRAP("vkCreateSwapchainKHR", createSwapchain, PFN_vkCreateSwapchainKHR, CreateSwapchain)
    WRAP("vkDestroySwapchainKHR", destroySwapchain, PFN_vkDestroySwapchainKHR, DestroySwapchain)
    WRAP("vkWaitForPresentKHR", unityWaitForPresent, PFN_vkWaitForPresentKHR, WaitForPresent)
#undef WRAP
    return function;
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL GetDeviceProc(VkDevice device, const char* name) {
    const auto next = nextDeviceProc.load();
    return next ? WrapSwapchainFunction(next(device, name), name) : nullptr;
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
    return WrapSwapchainFunction(function, name);
}
// Unity passes the next vkGetInstanceProcAddr in its interceptor chain on every Vulkan initialization.
PFN_vkGetInstanceProcAddr UNITY_INTERFACE_API Initialize(PFN_vkGetInstanceProcAddr next, void*) {
    nextInstanceProc = next;
    status = MajvkStatusNoDevice;
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

MAJVK_EXPORT int MAJVK_CALL majvk_abi_version() { return 1; }
MAJVK_EXPORT int MAJVK_CALL majvk_present_wait_status() { return status.load(); }
MAJVK_EXPORT int MAJVK_CALL majvk_present_wait_query(uint64_t* generation, uint64_t* presentId) {
    if (!generation || !presentId) return -3;
    if (status.load() != MajvkStatusEnabled) return -2;
    *generation = presentGeneration.load();
    *presentId = lastPresentId.load();
    return 1;
}
MAJVK_EXPORT int MAJVK_CALL majvk_present_wait_poll(uint64_t generation, uint64_t presentId) {
    if (status.load() != MajvkStatusEnabled) return -2;
    std::unique_lock<std::mutex> lock(presentMutex, std::try_to_lock);
    if (!lock.owns_lock() || swapchainUsers) return 0;
    if (presentSwapchain == VK_NULL_HANDLE || generation != presentGeneration.load()) return -1;
    if (presentId == 0 || presentId > lastPresentId.load()) return 0;
    const VkResult result = waitForPresent(presentDevice, presentSwapchain, presentId, 0);
    if (result == VK_SUCCESS || result == VK_SUBOPTIMAL_KHR) return 1;
    if (result == VK_TIMEOUT) return 0;
    return static_cast<int>(result);
}
