#pragma once
#include "PortableGpuBackend.h"

// The implementation is Objective-C++ and must be compiled with ARC. Metal
// receives an IOSurface-backed BGRA8Unorm texture; Unity uses TextureFormat.BGRA32.
std::unique_ptr<PortableGpuBackend> CreateApplePortableGpuBackend();
