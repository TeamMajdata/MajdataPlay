#pragma once
#include "VulkanPortable.h"

// Called by the existing platform loader hooks; never install a second Unity
// initialization interceptor, since that would replace the first one.
PFN_vkVoidFunction FfuVulkanVideoInstanceProc(PFN_vkGetInstanceProcAddr loader, VkInstance instance, const char* name);
VkResult FfuVulkanVideoCreateDevice(PFN_vkCreateDevice create, PFN_vkGetInstanceProcAddr loader, VkInstance instance,
    VkPhysicalDevice physical, const VkDeviceCreateInfo* original, const VkAllocationCallbacks* allocation, VkDevice* device);
bool FfuVulkanVideoAvailable();
int FfuVulkanVideoStatus();
