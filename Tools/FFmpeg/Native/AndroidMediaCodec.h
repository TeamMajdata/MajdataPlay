#pragma once
#include "Bridge.h"

// All image pixels stay in PRIVATE AHardwareBuffer storage. Capture is a
// bounded decode-worker operation; prepare only imports GPU resources.
bool FfuAndroidAvailable();
FFU_EXPORT int FFU_CALL ffu_android_is_available();
FFU_EXPORT int FFU_CALL ffu_android_set_java_vm(void* javaVm);
FFU_EXPORT void* FFU_CALL ffu_android_create(int width, int height);
FFU_EXPORT AVBufferRef* FFU_CALL ffu_android_device(void* session); // new owned ref
FFU_EXPORT void* FFU_CALL ffu_android_capture(void* session, const AVFrame* frame, int timeoutMs);
FFU_EXPORT void FFU_CALL ffu_android_image_release(void* image);
FFU_EXPORT void FFU_CALL ffu_android_flush(void* session);
FFU_EXPORT void FFU_CALL ffu_android_release(void* session);
FFU_EXPORT int FFU_CALL ffu_android_error(void* session);
FFU_EXPORT void* FFU_CALL ffu_android_prepare(void* presenter, void* image, void* unityTexture);
