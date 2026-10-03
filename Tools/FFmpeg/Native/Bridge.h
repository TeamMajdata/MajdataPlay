#pragma once
#include "IUnityGraphics.h"
#include <atomic>
#include <cstdint>
extern "C" {
#include <libavutil/frame.h>
#include <libavutil/version.h>
}
#if LIBAVUTIL_VERSION_MAJOR != 61
#error "This bridge must use the same libavutil 61 ABI as the project's FFmpeg.AutoGen bindings."
#endif

#ifdef _WIN32
#define FFU_EXPORT extern "C" __declspec(dllexport)
#define FFU_CALL __cdecl
#else
#define FFU_EXPORT extern "C" __attribute__((visibility("default")))
#define FFU_CALL
#endif

// The C ABI is deliberately cdecl, independently of Unity's render callback ABI.
// All opaque packets transfer ownership to the render callback when submitted.
enum FfuEvent {
    FfuSubmitD3D11 = 1, FfuCompleteMetal = 2, FfuDrain = 3,
    FfuPrepareD3D12 = 4, FfuSubmitD3D12 = 5,
    FfuSubmitWgl = 6, FfuCompleteWgl = 7, FfuDestroyWgl = 8,
    FfuSubmitVulkan = 9, FfuReleaseVulkan = 10, FfuSubmitPortableVulkan = 11,
    FfuPrepareNativeD3D12 = 12, FfuSubmitNativeD3D12 = 13
};
enum FfuCapability {
    FfuD3D11GpuConversion = 1, FfuMetalPlaneZeroCopy = 2, FfuD3D12GpuCopy = 4,
    FfuWglGpuInterop = 8, FfuVulkanGpuCopy = 16, FfuD3D12NativeDecode = 128, FfuVulkanVideoDecode = 256
};
// UnityInterfaceGUID has a non-trivial C++ copy constructor. Its by-value ABI
// differs between 32-bit MinGW and Unity's MSVC build. Always use the C-compatible
// split GUID entry point instead of IUnityInterfaces::Get<T>() across the DLL.
template<class T> static T* FfuGetInterface(IUnityInterfaces* interfaces) {
    const auto guid = GetUnityInterfaceGUID<T>();
    return static_cast<T*>(interfaces->GetInterfaceSplit(guid.m_GUIDHigh, guid.m_GUIDLow));
}
struct FfuMetalPlanes {
    void* packet;
    void* luma;
    void* chroma;
    int width;
    int height;
    int chromaWidth;
    int chromaHeight;
    int fullRange;
    int matrix709;
};

void FfuPlatformInitialize(IUnityInterfaces*, UnityGfxRenderer);
void FfuPlatformShutdown();
int FfuPlatformCapabilities();
int FfuPlatformStatus();
void FfuPlatformRender(int event, void* data);
int FfuEventId(int event);
void FfuD3D11RetainPresenter(void* presenter);
void FfuD3D11SetError(void* presenter, int error);
void FfuD3D11Submit(void* packet);
FFU_EXPORT int FFU_CALL ffu_abi_version();
FFU_EXPORT int FFU_CALL ffu_capabilities();
FFU_EXPORT int FFU_CALL ffu_initialization_status();
FFU_EXPORT int FFU_CALL ffu_event_id(int event);
FFU_EXPORT UnityRenderingEventAndData FFU_CALL ffu_render_callback();
FFU_EXPORT void* FFU_CALL ffu_d3d11_acquire_device();
FFU_EXPORT void* FFU_CALL ffu_d3d11_create();
FFU_EXPORT void* FFU_CALL ffu_d3d11_create_output(void* presenter, int width, int height);
FFU_EXPORT void FFU_CALL ffu_d3d11_release_output(void* texture);
FFU_EXPORT void FFU_CALL ffu_d3d11_release(void* presenter);
FFU_EXPORT void* FFU_CALL ffu_d3d11_prepare(void* presenter, const AVFrame* frame, void* target);
FFU_EXPORT int FFU_CALL ffu_d3d11_error(void* presenter);
FFU_EXPORT void* FFU_CALL ffu_shared_surface_create(void* presenter, void* texture, unsigned int glName);
FFU_EXPORT void* FFU_CALL ffu_shared_prepare(void* presenter, const AVFrame* frame, void* texture, void* surface, void* unityTarget);
FFU_EXPORT int FFU_CALL ffu_shared_error(void* surface);
FFU_EXPORT int FFU_CALL ffu_metal_prepare(const AVFrame* frame, FfuMetalPlanes* result);
FFU_EXPORT void FFU_CALL ffu_packet_cancel(void* packet);
