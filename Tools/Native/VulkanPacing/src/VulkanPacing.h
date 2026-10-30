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

// Driver low-latency backend reported by majvk_low_latency_backend.
constexpr int MajvkBackendNone = 0;
constexpr int MajvkBackendNvidia = 1; // VK_NV_low_latency2 (Reflex)
constexpr int MajvkBackendAmd = 2;    // VK_AMD_anti_lag

// Driver low-latency status reported by majvk_low_latency_status.
constexpr int MajvkLowLatencyActive = 0;
constexpr int MajvkLowLatencyNoDevice = 1;
constexpr int MajvkLowLatencyUnsupported = 2;     // Neither extension (with its requirements) is available.
constexpr int MajvkLowLatencyRejected = 3;        // The driver rejected the device with the extension enabled.
constexpr int MajvkLowLatencyMissingFunction = 4; // An entry point or the timeline semaphore was unavailable.

// Where present ids come from, reported by majvk_present_id_source.
constexpr int MajvkPresentIdsNone = 0;  // No tracked presentation carried an id yet.
constexpr int MajvkPresentIdsLayer = 1; // This layer attaches the ids.
constexpr int MajvkPresentIdsUnity = 2; // Unity attaches its own ids and the layer follows them.
constexpr int MajvkPresentIdsUnityReplaced = 3; // Unity attaches ids; the NVIDIA backend replaces them with frame ids.

// Results of majvk_frame_begin and majvk_frame_end.
constexpr int MajvkFrameDone = 1;
constexpr int MajvkFrameSkipped = 0;   // No swapchain yet, or the driver had nothing to do.
constexpr int MajvkFrameTimedOut = 2;  // The driver's sleep did not finish within the timeout.
constexpr int MajvkFrameInactive = -2; // No driver low-latency backend is active.

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
// Returns one of the MajvkBackend values.
MAJVK_EXPORT int MAJVK_CALL majvk_low_latency_backend();
// Returns one of the MajvkLowLatency status values.
MAJVK_EXPORT int MAJVK_CALL majvk_low_latency_status();
// Starts a frame on the main thread before input is sampled: registers a new frame id for the next presentation
// and lets the driver delay the frame (NVIDIA latency sleep, AMD anti-lag input stage). Returns a MajvkFrame value
// or a negative VkResult error. timeoutNanoseconds bounds the wait for NVIDIA's sleep semaphore.
MAJVK_EXPORT int MAJVK_CALL majvk_frame_begin(uint64_t timeoutNanoseconds);
// Ends the main-thread part of the frame started last (NVIDIA simulation end marker). Returns a MajvkFrame value.
MAJVK_EXPORT int MAJVK_CALL majvk_frame_end();
// Returns the generation of the tracked swapchain; it changes whenever a swapchain is created or destroyed.
MAJVK_EXPORT uint64_t MAJVK_CALL majvk_swapchain_generation();
// Returns one of the MajvkPresentIds values.
MAJVK_EXPORT int MAJVK_CALL majvk_present_id_source();
