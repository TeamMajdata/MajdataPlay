// Deterministic tests of the present pacing layer against a modeled Unity interceptor chain and driver.
// They verify the layer protocol; they do not claim coverage of real drivers or presentation timing.
#define VK_NO_PROTOTYPES
#include "VulkanPacing.h"
#include "IUnityGraphics.h"
#include "IUnityGraphicsVulkan.h"
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <future>
#include <thread>

extern "C" void UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* interfaces);
extern "C" void UNITY_INTERFACE_API UnityPluginUnload();

#define REQUIRE(value) do { if (!(value)) { std::printf("FAIL line %d: %s\n", __LINE__, #value); std::exit(1); } } while (0)
namespace {
const VkInstance testInstance = reinterpret_cast<VkInstance>(uintptr_t(1));
const VkPhysicalDevice testPhysical = reinterpret_cast<VkPhysicalDevice>(uintptr_t(3));
const VkDevice testDevice = reinterpret_cast<VkDevice>(uintptr_t(8));
const VkSwapchainKHR testSwapchain = (VkSwapchainKHR)(uintptr_t(9));
const VkSwapchainKHR otherSwapchain = (VkSwapchainKHR)(uintptr_t(10));
const VkQueue testQueue = reinterpret_cast<VkQueue>(uintptr_t(4));

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
bool waitAvailable = true;
int creates = 0, presentCalls = 0, waitCalls = 0, acquireCalls = 0, destroyCalls = 0;
uint64_t lastPresentedId = 0, lastWaitedId = 0, lastWaitTimeout = UINT64_MAX;
VkResult presentResult = VK_SUCCESS, waitResult = VK_TIMEOUT, acquireResult = VK_SUBOPTIMAL_KHR;
std::promise<void>* acquireEntered = nullptr;
std::shared_future<void> acquireRelease;
VKAPI_ATTR VkResult VKAPI_CALL InnerCreateDevice(VkPhysicalDevice physical, const VkDeviceCreateInfo*, const VkAllocationCallbacks*, VkDevice* device) {
    REQUIRE(physical == testPhysical); ++creates; *device = testDevice; return VK_SUCCESS;
}
VKAPI_ATTR VkResult VKAPI_CALL InnerCreateSwapchain(VkDevice device, const VkSwapchainCreateInfoKHR*, const VkAllocationCallbacks*, VkSwapchainKHR* swapchain) {
    REQUIRE(device == testDevice); *swapchain = testSwapchain; return VK_SUCCESS;
}
VKAPI_ATTR void VKAPI_CALL InnerDestroySwapchain(VkDevice device, VkSwapchainKHR, const VkAllocationCallbacks*) { REQUIRE(device == testDevice); ++destroyCalls; }
VKAPI_ATTR VkResult VKAPI_CALL InnerPresent(VkQueue queue, const VkPresentInfoKHR* info) {
    REQUIRE(queue == testQueue);
    ++presentCalls; lastPresentedId = 0;
    for (auto* item = static_cast<const VkBaseInStructure*>(info->pNext); item; item = item->pNext) {
        if (item->sType != VK_STRUCTURE_TYPE_PRESENT_ID_KHR) continue;
        auto* ids = reinterpret_cast<const VkPresentIdKHR*>(item);
        REQUIRE(ids->swapchainCount == info->swapchainCount);
        for (uint32_t i = 0; i < ids->swapchainCount; ++i)
            if (info->pSwapchains[i] == testSwapchain) lastPresentedId = ids->pPresentIds[i];
            else REQUIRE(ids->pPresentIds[i] == 0);
    }
    return presentResult;
}
VKAPI_ATTR VkResult VKAPI_CALL InnerWait(VkDevice device, VkSwapchainKHR swapchain, uint64_t id, uint64_t timeout) {
    REQUIRE(device == testDevice && swapchain == testSwapchain);
    ++waitCalls; lastWaitedId = id; lastWaitTimeout = timeout;
    return waitResult;
}
VKAPI_ATTR VkResult VKAPI_CALL InnerAcquire(VkDevice, VkSwapchainKHR, uint64_t, VkSemaphore, VkFence, uint32_t* index) {
    ++acquireCalls;
    if (acquireEntered) { acquireEntered->set_value(); acquireRelease.wait(); }
    *index = 2; return acquireResult;
}
VKAPI_ATTR VkResult VKAPI_CALL InnerAcquire2(VkDevice device, const VkAcquireNextImageInfoKHR* info, uint32_t* index) {
    return InnerAcquire(device, info->swapchain, info->timeout, info->semaphore, info->fence, index);
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL InnerDeviceProc(VkDevice, const char* name) {
#define FUNCTION(text, function) if (!std::strcmp(name, text)) return reinterpret_cast<PFN_vkVoidFunction>(function)
    FUNCTION("vkCreateSwapchainKHR", InnerCreateSwapchain);
    FUNCTION("vkDestroySwapchainKHR", InnerDestroySwapchain);
    FUNCTION("vkQueuePresentKHR", InnerPresent);
    FUNCTION("vkAcquireNextImageKHR", InnerAcquire);
    FUNCTION("vkAcquireNextImage2KHR", InnerAcquire2);
    if (waitAvailable) FUNCTION("vkWaitForPresentKHR", InnerWait);
#undef FUNCTION
    return nullptr;
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL InnerInstanceProc(VkInstance, const char* name) {
#define FUNCTION(text, function) if (!std::strcmp(name, text)) return reinterpret_cast<PFN_vkVoidFunction>(function)
    FUNCTION("vkGetDeviceProcAddr", InnerDeviceProc);
    FUNCTION("vkCreateDevice", InnerCreateDevice);
    FUNCTION("vkQueuePresentKHR", InnerPresent);
    FUNCTION("vkAcquireNextImageKHR", InnerAcquire);
#undef FUNCTION
    return nullptr;
}

void VerifyRegistration() {
    unityVulkan.AddInterceptInitialization = AddIntercept;
    unityVulkan.RemoveInterceptInitialization = RemoveIntercept;
    IUnityInterfaces interfaces{}; interfaces.GetInterfaceSplit = GetInterface;
    UnityPluginLoad(&interfaces);
    REQUIRE(registered && registeredPriority < kUnityVulkanInitCallbackMaxPriority); // stays outside the innermost layer
    REQUIRE(majvk_abi_version() == 1 && majvk_present_wait_status() == MajvkStatusNoDevice);
    std::puts("PASS: registers below the maximum interceptor priority");
}

void VerifyPresentWait() {
    const auto loader = registered(InnerInstanceProc, nullptr); REQUIRE(loader);
    const auto createDevice = reinterpret_cast<PFN_vkCreateDevice>(loader(testInstance, "vkCreateDevice")); REQUIRE(createDevice);
    const auto deviceProc = reinterpret_cast<PFN_vkGetDeviceProcAddr>(loader(testInstance, "vkGetDeviceProcAddr")); REQUIRE(deviceProc);
    uint64_t generation = 0, presentId = 0;
    VkDevice device = VK_NULL_HANDLE;

    // Unity did not request the features: nothing to wait for, device creation untouched.
    VkDeviceCreateInfo plain{VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO};
    REQUIRE(createDevice(testPhysical, &plain, nullptr, &device) == VK_SUCCESS && creates == 1);
    REQUIRE(majvk_present_wait_status() == MajvkStatusNotEnabledByUnity);
    REQUIRE(majvk_present_wait_query(&generation, &presentId) == -2 && majvk_present_wait_poll(0, 1) == -2);

    VkPhysicalDevicePresentIdFeaturesKHR idFeature{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PRESENT_ID_FEATURES_KHR, nullptr, VK_TRUE};
    VkPhysicalDevicePresentWaitFeaturesKHR waitFeature{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PRESENT_WAIT_FEATURES_KHR, &idFeature, VK_FALSE};
    const char* extensions[] = {VK_KHR_PRESENT_ID_EXTENSION_NAME, VK_KHR_PRESENT_WAIT_EXTENSION_NAME};
    VkDeviceCreateInfo info{VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO};
    info.pNext = &waitFeature; info.enabledExtensionCount = 2; info.ppEnabledExtensionNames = extensions;
    REQUIRE(createDevice(testPhysical, &info, nullptr, &device) == VK_SUCCESS);
    REQUIRE(majvk_present_wait_status() == MajvkStatusNotEnabledByUnity); // a disabled feature is respected

    waitFeature.presentWait = VK_TRUE; waitAvailable = false;
    REQUIRE(createDevice(testPhysical, &info, nullptr, &device) == VK_SUCCESS);
    REQUIRE(majvk_present_wait_status() == MajvkStatusMissingFunction);

    waitAvailable = true;
    REQUIRE(createDevice(testPhysical, &info, nullptr, &device) == VK_SUCCESS && device == testDevice && creates == 4);
    REQUIRE(majvk_present_wait_status() == MajvkStatusEnabled);
    REQUIRE(info.pNext == &waitFeature && info.enabledExtensionCount == 2); // Unity's configuration is passed on unchanged
    REQUIRE(majvk_present_wait_query(nullptr, &presentId) == -3);
    REQUIRE(majvk_present_wait_query(&generation, &presentId) == 1 && presentId == 0);
    REQUIRE(majvk_present_wait_poll(generation, 1) == -1); // no swapchain yet

    const auto createSwapchain = reinterpret_cast<PFN_vkCreateSwapchainKHR>(deviceProc(device, "vkCreateSwapchainKHR"));
    const auto destroySwapchain = reinterpret_cast<PFN_vkDestroySwapchainKHR>(deviceProc(device, "vkDestroySwapchainKHR"));
    const auto present = reinterpret_cast<PFN_vkQueuePresentKHR>(deviceProc(device, "vkQueuePresentKHR"));
    const auto acquire = reinterpret_cast<PFN_vkAcquireNextImageKHR>(deviceProc(device, "vkAcquireNextImageKHR"));
    const auto acquire2 = reinterpret_cast<PFN_vkAcquireNextImage2KHR>(deviceProc(device, "vkAcquireNextImage2KHR"));
    const auto unityWait = reinterpret_cast<PFN_vkWaitForPresentKHR>(deviceProc(device, "vkWaitForPresentKHR"));
    REQUIRE(createSwapchain && destroySwapchain && present && acquire && acquire2 && unityWait);
    REQUIRE(present != InnerPresent && unityWait != InnerWait && !deviceProc(device, "vkMissingTestFunction"));
    REQUIRE(loader(testInstance, "vkQueuePresentKHR") != reinterpret_cast<PFN_vkVoidFunction>(InnerPresent)); // instance route too

    // Inner results, such as another layer's acquisition workaround, pass through unchanged.
    uint32_t index = UINT32_MAX;
    REQUIRE(acquire(device, testSwapchain, UINT64_MAX, VK_NULL_HANDLE, VK_NULL_HANDLE, &index) == VK_SUBOPTIMAL_KHR && index == 2);
    VkAcquireNextImageInfoKHR acquireInfo{VK_STRUCTURE_TYPE_ACQUIRE_NEXT_IMAGE_INFO_KHR};
    acquireInfo.swapchain = testSwapchain;
    REQUIRE(acquire2(device, &acquireInfo, &index) == VK_SUBOPTIMAL_KHR && acquireCalls == 2);

    // Without a tracked swapchain a presentation is forwarded unchanged.
    VkPresentInfoKHR untracked{VK_STRUCTURE_TYPE_PRESENT_INFO_KHR};
    untracked.swapchainCount = 1; untracked.pSwapchains = &otherSwapchain;
    REQUIRE(present(testQueue, &untracked) == VK_SUCCESS && lastPresentedId == 0);

    VkSwapchainCreateInfoKHR swapchainInfo{VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR};
    VkSwapchainKHR swapchain = VK_NULL_HANDLE;
    const uint64_t before = generation;
    REQUIRE(createSwapchain(device, &swapchainInfo, nullptr, &swapchain) == VK_SUCCESS && swapchain == testSwapchain);
    REQUIRE(majvk_present_wait_query(&generation, &presentId) == 1 && generation != before && presentId == 0);

    const VkSwapchainKHR both[] = {otherSwapchain, testSwapchain};
    VkPresentInfoKHR presentInfo{VK_STRUCTURE_TYPE_PRESENT_INFO_KHR};
    presentInfo.swapchainCount = 2; presentInfo.pSwapchains = both;
    REQUIRE(present(testQueue, &presentInfo) == VK_SUCCESS && lastPresentedId == 1);
    REQUIRE(present(testQueue, &presentInfo) == VK_SUCCESS && lastPresentedId == 2);
    REQUIRE(majvk_present_wait_query(&generation, &presentId) == 1 && presentId == 2);
    presentResult = VK_ERROR_OUT_OF_DATE_KHR;
    REQUIRE(present(testQueue, &presentInfo) == VK_ERROR_OUT_OF_DATE_KHR && lastPresentedId == 3);
    REQUIRE(majvk_present_wait_query(&generation, &presentId) == 1 && presentId == 2); // a failed present is never waited for
    presentResult = VK_SUCCESS;
    uint64_t unityIds[] = {0, 77};
    VkPresentIdKHR unityChain{VK_STRUCTURE_TYPE_PRESENT_ID_KHR, nullptr, 2, unityIds};
    presentInfo.pNext = &unityChain;
    REQUIRE(present(testQueue, &presentInfo) == VK_SUCCESS && lastPresentedId == 77); // Unity's own ids are left alone
    REQUIRE(majvk_present_wait_query(&generation, &presentId) == 1 && presentId == 77); // and followed
    presentInfo.pNext = nullptr;
    REQUIRE(present(testQueue, &presentInfo) == VK_SUCCESS && lastPresentedId == 78); // added ids keep increasing
    REQUIRE(majvk_present_wait_query(&generation, &presentId) == 1 && presentId == 78);
    REQUIRE(presentCalls == 6); // every presentation reached the inner layer exactly once

    // Unity's own waits are forwarded to the driver.
    waitCalls = 0; waitResult = VK_SUCCESS;
    REQUIRE(unityWait(device, swapchain, 78, 5) == VK_SUCCESS && waitCalls == 1 && lastWaitedId == 78 && lastWaitTimeout == 5);

    waitCalls = 0;
    REQUIRE(majvk_present_wait_poll(generation, 79) == 0 && waitCalls == 0); // not presented yet: no driver call
    waitResult = VK_TIMEOUT;
    REQUIRE(majvk_present_wait_poll(generation, 78) == 0 && waitCalls == 1 && lastWaitedId == 78 && lastWaitTimeout == 0);
    waitResult = VK_SUCCESS;
    REQUIRE(majvk_present_wait_poll(generation, 78) == 1);
    waitResult = VK_SUBOPTIMAL_KHR;
    REQUIRE(majvk_present_wait_poll(generation, 1) == 1);
    waitResult = VK_ERROR_DEVICE_LOST;
    REQUIRE(majvk_present_wait_poll(generation, 78) == VK_ERROR_DEVICE_LOST);
    REQUIRE(majvk_present_wait_poll(generation + 1, 78) == -1); // stale generation

    // A Unity swapchain call in progress, such as a blocking acquisition, keeps the poll off the swapchain.
    std::promise<void> entered, release;
    acquireEntered = &entered; acquireRelease = release.get_future().share();
    std::thread renderThread([&] { uint32_t image = 0; acquire(device, swapchain, UINT64_MAX, VK_NULL_HANDLE, VK_NULL_HANDLE, &image); });
    entered.get_future().wait();
    waitCalls = 0; waitResult = VK_SUCCESS;
    REQUIRE(majvk_present_wait_poll(generation, 78) == 0 && waitCalls == 0);
    release.set_value(); renderThread.join(); acquireEntered = nullptr;
    REQUIRE(majvk_present_wait_poll(generation, 78) == 1 && waitCalls == 1);

    destroySwapchain(device, swapchain, nullptr);
    REQUIRE(destroyCalls == 1 && majvk_present_wait_poll(generation, 78) == -1);
    uint64_t after = 0;
    REQUIRE(majvk_present_wait_query(&after, &presentId) == 1 && after != generation && presentId == 0);

    // A new Unity Vulkan initialization starts over.
    REQUIRE(registered(InnerInstanceProc, nullptr) && majvk_present_wait_status() == MajvkStatusNoDevice);
    std::puts("PASS: reuses Unity's present wait configuration, ids per swapchain, zero-timeout polls, generations and in-progress Unity calls");
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
    VerifyUnload();
}
