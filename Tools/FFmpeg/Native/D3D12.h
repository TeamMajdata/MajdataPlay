#pragma once
#include "Bridge.h"
struct ID3D11Device;
ID3D11Device* FfuD3D12Initialize(IUnityInterfaces* interfaces);
void FfuD3D12Shutdown();
int FfuD3D12Status();
int FfuD3D12Capabilities();
void FfuD3D12Render(int event, void* data);
void FfuD3D12Poll(bool drain);
void FfuD3D12ForgetPresenter(void* presenter);
FFU_EXPORT void* FFU_CALL ffu_d3d12_prepare(void* presenter, const AVFrame* frame, void* unityTexture);
FFU_EXPORT void FFU_CALL ffu_d3d12_cancel(void* packet);
// Acquires an owned FFmpeg AVBufferRef wrapping Unity's D3D12 device.
FFU_EXPORT void* FFU_CALL ffu_d3d12va_acquire_device();
FFU_EXPORT int FFU_CALL ffu_d3d12va_status();
// The borrowed AVFrame remains owned by the caller. Returns 1 when its decode
// fence is complete, 0 while pending, or a negative HRESULT for invalid/lost GPU state.
FFU_EXPORT int FFU_CALL ffu_d3d12va_frame_ready(const AVFrame* frame);
FFU_EXPORT void* FFU_CALL ffu_d3d12va_create();
FFU_EXPORT void FFU_CALL ffu_d3d12va_release(void* presenter);
FFU_EXPORT void* FFU_CALL ffu_d3d12va_prepare(void* presenter, const AVFrame* frame, void* unityTexture);
FFU_EXPORT void FFU_CALL ffu_d3d12va_cancel(void* packet);
FFU_EXPORT int FFU_CALL ffu_d3d12va_error(void* presenter);
