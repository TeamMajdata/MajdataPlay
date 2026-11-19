// Public C ABI of the MajdataPlay texture upload plug-in. Every export uses the C calling convention
// (__cdecl) so the managed side can declare it with CallingConvention.Cdecl on every platform.
#pragma once

#if defined(_WIN32)
#define MAJVKU_EXPORT extern "C" __declspec(dllexport)
#define MAJVKU_CALL __cdecl
#else
#define MAJVKU_EXPORT extern "C" __attribute__((visibility("default")))
#define MAJVKU_CALL
#endif

// Version of the exported C ABI below; the managed wrapper refuses to run against another version.
constexpr int MajVkuAbiVersion = 1;

// Returns MajVkuAbiVersion. Lets the managed side detect a stale DLL without loading it twice.
MAJVKU_EXPORT int MAJVKU_CALL MajVkUpload_AbiVersion();

// Returns 1 while Unity exposes a Vulkan device this plug-in can copy into, otherwise 0.
MAJVKU_EXPORT int MAJVKU_CALL MajVkUpload_Ready();

// Copies tightly packed RGBA8 pixels into a persistent staging slot and queues them for the next render event.
// `rowPitch` is the byte distance between two source rows and must be at least `width * 4`.
// Returns 1 when the upload is queued, 0 when the caller must fall back to its own upload path.
MAJVKU_EXPORT int MAJVKU_CALL MajVkUpload_Enqueue(void* unityTexture, const void* pixels, int width, int height,
    int rowPitch);

// Returns the number of queued uploads that no render event has recorded yet.
MAJVKU_EXPORT int MAJVKU_CALL MajVkUpload_Pending();

// Returns the Unity render event identifier this plug-in configured for itself.
MAJVKU_EXPORT int MAJVKU_CALL MajVkUpload_EventId();

// Returns the UnityRenderingEvent callback to pass to GL.IssuePluginEvent together with MajVkUpload_EventId.
MAJVKU_EXPORT void* MAJVKU_CALL MajVkUpload_RenderEventFunc();

// Drops queued uploads and forgotten refusal state, for callers that recreated their Unity texture.
MAJVKU_EXPORT void MAJVKU_CALL MajVkUpload_Reset();
