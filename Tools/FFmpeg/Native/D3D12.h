#pragma once
#include "Bridge.h"
struct ID3D11Device;
ID3D11Device* FfuD3D12Initialize(IUnityInterfaces* interfaces);
void FfuD3D12Shutdown();
int FfuD3D12Status();
void FfuD3D12Render(int event, void* data);
void FfuD3D12Poll(bool drain);
FFU_EXPORT void* FFU_CALL ffu_d3d12_prepare(void* presenter, const AVFrame* frame, void* unityTexture);
FFU_EXPORT void FFU_CALL ffu_d3d12_cancel(void* packet);
