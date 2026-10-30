#pragma once
#include <cstdint>

#if defined(_WIN32)
#define MAJVK_EXPORT extern "C" __declspec(dllexport)
#define MAJVK_CALL __cdecl
#else
#define MAJVK_EXPORT extern "C" __attribute__((visibility("default")))
#define MAJVK_CALL
#endif

// Present waiting status reported by majvk_present_wait_status.
constexpr int MajvkStatusEnabled = 0;           // Unity's device has VK_KHR_present_id and VK_KHR_present_wait.
constexpr int MajvkStatusNoDevice = 1;          // No Vulkan device was created through this layer yet.
constexpr int MajvkStatusNotEnabledByUnity = 2; // Unity created its device without both features.
constexpr int MajvkStatusMissingFunction = 3;   // vkWaitForPresentKHR could not be loaded.

// Version of the exported C ABI below.
MAJVK_EXPORT int MAJVK_CALL majvk_abi_version();
// Returns one of the MajvkStatus values.
MAJVK_EXPORT int MAJVK_CALL majvk_present_wait_status();
// Reads the tracked swapchain generation and its latest successful presentation id.
// Returns 1, -2 when present waiting is not enabled, or -3 for null arguments.
MAJVK_EXPORT int MAJVK_CALL majvk_present_wait_query(uint64_t* generation, uint64_t* presentId);
// Polls without blocking. Returns 1 once the id is shown, 0 while pending or while a Unity swapchain call is in
// progress, -1 when the swapchain changed, -2 when present waiting is not enabled, or a negative VkResult error.
MAJVK_EXPORT int MAJVK_CALL majvk_present_wait_poll(uint64_t generation, uint64_t presentId);
