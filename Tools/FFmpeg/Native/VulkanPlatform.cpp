#include "Bridge.h"
#include "VulkanPortable.h"
#include "VulkanVideoDecode.h"
#if defined(__ANDROID__)
#include "AndroidMediaCodec.h"
#else
bool FfuLinuxInitialize();
#endif

namespace { std::atomic<int> capability{0}; }
void FfuPlatformInitialize(IUnityInterfaces* interfaces, UnityGfxRenderer renderer) {
    capability = 0;
    if (renderer != kUnityGfxRendererVulkan || !FfuVkInitialize(interfaces)) return;
    if (FfuVulkanVideoAvailable()) capability = 256;
    if (!FfuVkExternalImportsAvailable()) {
        if (!capability) FfuVkSetStatus(FfuVulkanVideoStatus());
        return;
    }
#if defined(__ANDROID__)
    if (FfuAndroidAvailable()) capability = capability.load() | 64;
    else FfuVkSetStatus(300);
#else
    if (FfuLinuxInitialize()) capability = capability.load() | 32;
#endif
}
void FfuPlatformShutdown() { capability = 0; FfuVkShutdown(); }
int FfuPlatformCapabilities() { return capability.load(); }
int FfuPlatformStatus() { return FfuVkStatus(); }
void FfuPlatformRender(int event, void* data) {
    if (event == 11) FfuVkSubmit(data);
    else if (event == FfuDrain) FfuVkPoll(true);
}
FFU_EXPORT void* FFU_CALL ffu_vulkan_create() { return FfuVkPresenterCreate(); }
FFU_EXPORT void FFU_CALL ffu_vulkan_release(void* presenter) { FfuVkPresenterRelease(presenter); }
FFU_EXPORT int FFU_CALL ffu_vulkan_error(void* presenter) { return FfuVkPresenterError(presenter); }
FFU_EXPORT void FFU_CALL ffu_vulkan_cancel(void* packet) { FfuVkCancel(packet); }
FFU_EXPORT int FFU_CALL ffu_vulkan_poll() { FfuVkPoll(false); return FfuVkPendingCount(); }
// Legacy ABI exports keep ordinary portable C# wrappers link/load compatible;
// capability bits always gate their use.
FFU_EXPORT void* FFU_CALL ffu_d3d11_acquire_device() { return nullptr; }
FFU_EXPORT void* FFU_CALL ffu_d3d11_create() { return nullptr; }
FFU_EXPORT void* FFU_CALL ffu_d3d11_create_output(void*, int, int) { return nullptr; }
FFU_EXPORT void FFU_CALL ffu_d3d11_release_output(void*) {}
FFU_EXPORT void FFU_CALL ffu_d3d11_release(void*) {}
FFU_EXPORT void* FFU_CALL ffu_d3d11_prepare(void*, const AVFrame*, void*) { return nullptr; }
FFU_EXPORT int FFU_CALL ffu_d3d11_error(void*) { return -1; }
FFU_EXPORT int FFU_CALL ffu_metal_prepare(const AVFrame*, FfuMetalPlanes*) { return 0; }
FFU_EXPORT void FFU_CALL ffu_packet_cancel(void*) {}
