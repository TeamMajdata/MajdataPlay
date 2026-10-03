#pragma once
#ifndef VK_NO_PROTOTYPES
#define VK_NO_PROTOTYPES
#endif
#if defined(__ANDROID__) && !defined(VK_USE_PLATFORM_ANDROID_KHR)
#define VK_USE_PLATFORM_ANDROID_KHR
#endif
#include "IUnityGraphics.h"
#include "IUnityGraphicsVulkan.h"
#include <atomic>
#include <functional>
#include <memory>
#include <mutex>

struct FfuVkContext {
    UnityVulkanInstance instance{};
    IUnityGraphicsVulkanV2* unity = nullptr;
    std::atomic<bool> active{true};
    std::recursive_mutex resources;
    PFN_vkGetDeviceProcAddr getDeviceProcAddr = nullptr;
    VkPhysicalDeviceMemoryProperties memory{};
    PFN_vkVoidFunction Proc(const char* name) const { return getDeviceProcAddr(instance.device, name); }
    PFN_vkVoidFunction InstanceProc(const char* name) const { return instance.getInstanceProcAddr(instance.instance, name); }
    int MemoryType(uint32_t bits) const;
};

// Importers own images, views, samplers and acquire semaphore via owner. The
// common queue layer retains that owner until its actual GPU fence completes.
// One plane means sampler conversion already returns RGB; two means NV12 R8/RG8.
struct FfuVkSample {
    std::shared_ptr<FfuVkContext> context;
    VkImage image[2]{};
    VkImageView view[2]{};
    VkSampler sampler[2]{};
    uint32_t combinedDescriptorCount = 1;
    uint32_t planeCount = 0, width = 0, height = 0;
    VkImageLayout initialLayout = VK_IMAGE_LAYOUT_GENERAL;
    uint32_t foreignQueue = VK_QUEUE_FAMILY_FOREIGN_EXT;
    VkSemaphore acquireSemaphore = VK_NULL_HANDLE;
    // FFmpeg frames use a timeline semaphore. lock is called immediately before
    // recording/submission so layout and semaphore values cannot become stale.
    bool timeline = false;
    uint64_t waitValue = 0, signalValue = 0;
    std::function<bool(FfuVkSample&)> lock;
    std::function<void(bool)> unlock;
    float uvScaleOffset[4]{1, 1, 0, 0};
    std::shared_ptr<void> owner;
};

void FfuVkPreload(IUnityInterfaces* interfaces);
bool FfuVkInitialize(IUnityInterfaces* interfaces);
void FfuVkShutdown();
std::shared_ptr<FfuVkContext> FfuVkCurrent();
int FfuVkStatus();
bool FfuVkExternalImportsAvailable();
void FfuVkSetStatus(int status);
void* FfuVkPresenterCreate();
void FfuVkPresenterRelease(void* presenter);
int FfuVkPresenterError(void* presenter);
void FfuVkPresenterSetError(void* presenter, int error);
void* FfuVkPrepare(void* presenter, const FfuVkSample& sample, void* unityTexture, bool fullRange, bool matrix709);
void FfuVkSubmit(void* packet); // Unity render thread
void FfuVkCancel(void* packet); // unsubmitted packet only
void FfuVkPoll(bool drain);
int FfuVkPendingCount();
