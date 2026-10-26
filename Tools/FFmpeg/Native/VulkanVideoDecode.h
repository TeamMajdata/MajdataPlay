#pragma once
#include "Bridge.h"
#include "VulkanPortable.h"

// Called by the existing platform loader hooks; never install a second Unity
// initialization interceptor, since that would replace the first one.
PFN_vkVoidFunction FfuVulkanVideoInstanceProc(PFN_vkGetInstanceProcAddr loader, VkInstance instance, const char* name);
VkResult FfuVulkanVideoCreateDevice(PFN_vkCreateDevice create, PFN_vkGetInstanceProcAddr loader, VkInstance instance,
    VkPhysicalDevice physical, const VkDeviceCreateInfo* original, const VkAllocationCallbacks* allocation, VkDevice* device);
bool FfuVulkanVideoAvailable();
int FfuVulkanVideoStatus();
// Installs public frame locks on a borrowed, uninitialized AVHWFramesContext.
// Returns 0 on success or a negative error; never replaces a live pool's locks.
FFU_EXPORT int FFU_CALL ffu_vulkan_video_configure_frames(void* framesReference);
// Polls a borrowed hardware frame on the decode worker. Returns 1 when its
// current timeline is complete, 0 while pending, or a negative error code.
FFU_EXPORT int FFU_CALL ffu_vulkan_video_frame_ready(const AVFrame* frame);
