#include "Bridge.h"
#if defined(_WIN32)
#include "VulkanInterop.h"
#elif defined(__linux__) || defined(__ANDROID__)
#include "VulkanPortable.h"
#endif

static IUnityInterfaces* interfaces = nullptr;
static IUnityGraphics* graphics = nullptr;
static int eventBase = 0;
static void UNITY_INTERFACE_API DeviceEvent(UnityGfxDeviceEventType event) {
    if (event == kUnityGfxDeviceEventShutdown || event == kUnityGfxDeviceEventBeforeReset)
        FfuPlatformShutdown();
    else if (event == kUnityGfxDeviceEventInitialize || event == kUnityGfxDeviceEventAfterReset)
        FfuPlatformInitialize(interfaces, graphics->GetRenderer());
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* value) {
    interfaces = value;
#if defined(_WIN32)
    FfuVulkanPreload(value);
#elif defined(__linux__) || defined(__ANDROID__)
    FfuVkPreload(value);
#endif
    graphics = FfuGetInterface<IUnityGraphics>(value);
    if (graphics) {
        eventBase = graphics->ReserveEventIDRange ? graphics->ReserveEventIDRange(32) : 0;
        graphics->RegisterDeviceEventCallback(DeviceEvent);
        DeviceEvent(kUnityGfxDeviceEventInitialize);
    }
}
extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload() {
    if (graphics) graphics->UnregisterDeviceEventCallback(DeviceEvent);
    FfuPlatformShutdown();
    graphics = nullptr;
    interfaces = nullptr;
}
static void UNITY_INTERFACE_API RenderEvent(int event, void* data) { FfuPlatformRender(event - eventBase, data); }
int FfuEventId(int event) { return eventBase + event; }
FFU_EXPORT int FFU_CALL ffu_event_id(int event) { return FfuEventId(event); }
FFU_EXPORT int FFU_CALL ffu_abi_version() {
#if defined(_WIN32) || defined(__linux__) || defined(__ANDROID__)
    return 4;
#else
    return 2;
#endif
}
FFU_EXPORT int FFU_CALL ffu_capabilities() { return FfuPlatformCapabilities(); }
FFU_EXPORT int FFU_CALL ffu_initialization_status() {
    if (!interfaces) return 1;
    if (!graphics) return 2;
    return FfuPlatformStatus();
}
FFU_EXPORT UnityRenderingEventAndData FFU_CALL ffu_render_callback() { return RenderEvent; }

#if !defined(_WIN32) && !defined(__APPLE__) && !defined(__linux__) && !defined(__ANDROID__)
// Linux / Android currently use portable Unity texture upload. A loaded bridge
// must report no capability, never manufacture a GPU handle for another API.
void FfuPlatformInitialize(IUnityInterfaces*, UnityGfxRenderer) {}
void FfuPlatformShutdown() {}
int FfuPlatformCapabilities() { return 0; }
int FfuPlatformStatus() { return 3; }
void FfuPlatformRender(int, void*) {}
FFU_EXPORT void* FFU_CALL ffu_d3d11_acquire_device() { return nullptr; }
FFU_EXPORT void* FFU_CALL ffu_d3d11_create() { return nullptr; }
FFU_EXPORT void* FFU_CALL ffu_d3d11_create_output(void*, int, int) { return nullptr; }
FFU_EXPORT void FFU_CALL ffu_d3d11_release_output(void*) {}
FFU_EXPORT void FFU_CALL ffu_d3d11_release(void*) {}
FFU_EXPORT void* FFU_CALL ffu_d3d11_prepare(void*, const AVFrame*, void*) { return nullptr; }
FFU_EXPORT int FFU_CALL ffu_d3d11_error(void*) { return -1; }
FFU_EXPORT int FFU_CALL ffu_metal_prepare(const AVFrame*, FfuMetalPlanes*) { return 0; }
FFU_EXPORT void FFU_CALL ffu_packet_cancel(void*) {}
#endif

#if !defined(_WIN32)
// Preserve __Internal linkability on iOS even if IL2CPP retains an unused Windows
// P/Invoke wrapper. Unsupported platforms never advertise these capabilities.
FFU_EXPORT void* FFU_CALL ffu_d3d12_prepare(void*, const AVFrame*, void*) { return nullptr; }
FFU_EXPORT void FFU_CALL ffu_d3d12_cancel(void*) {}
FFU_EXPORT void* FFU_CALL ffu_d3d12va_acquire_device() { return nullptr; }
FFU_EXPORT int FFU_CALL ffu_d3d12va_status() { return -1; }
FFU_EXPORT void* FFU_CALL ffu_d3d12va_create() { return nullptr; }
FFU_EXPORT void FFU_CALL ffu_d3d12va_release(void*) {}
FFU_EXPORT void* FFU_CALL ffu_d3d12va_prepare(void*, const AVFrame*, void*) { return nullptr; }
FFU_EXPORT void FFU_CALL ffu_d3d12va_cancel(void*) {}
FFU_EXPORT int FFU_CALL ffu_d3d12va_error(void*) { return -1; }
FFU_EXPORT void* FFU_CALL ffu_shared_surface_create(void*, void*, unsigned int) { return nullptr; }
FFU_EXPORT void* FFU_CALL ffu_shared_prepare(void*, const AVFrame*, void*, void*, void*) { return nullptr; }
FFU_EXPORT int FFU_CALL ffu_shared_error(void*) { return -1; }
FFU_EXPORT void* FFU_CALL ffu_wgl_retirement_create(void*) { return nullptr; }
FFU_EXPORT int FFU_CALL ffu_wgl_retirement_poll(void*) { return 0; }
#endif
