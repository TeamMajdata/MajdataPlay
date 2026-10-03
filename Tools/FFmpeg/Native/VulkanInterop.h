#pragma once
#include <d3d11.h>
#include "IUnityGraphics.h"

// Windows D3D11VA -> shared RGBA -> Unity Vulkan RenderTexture. GPU-only path;
// the copy into Unity-owned storage avoids exposing externally owned layouts.
void FfuVulkanPreload(IUnityInterfaces* interfaces);
ID3D11Device* FfuVulkanInitialize(IUnityInterfaces* interfaces); // caller owns AddRef
void FfuVulkanShutdown();
void* FfuVulkanImport(ID3D11Texture2D* texture); // owns its own texture reference
void FfuVulkanRelease(void* surface);
bool FfuVulkanBeginWrite(void* surface); // render thread, acquires producer key 0
bool FfuVulkanEndWrite(void* surface, void* unityTexture); // releases key 1 and queues copy
int FfuVulkanError(void* surface);
void FfuVulkanPoll(bool drain);
