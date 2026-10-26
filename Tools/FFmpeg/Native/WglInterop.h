#pragma once
#include <cstdint>

struct ID3D11Device;
struct ID3D11Texture2D;
struct FfuWglSurface;

enum FfuWglError {
    FfuWglOk = 0,
    FfuWglNoContext = 1,
    FfuWglMissingExtension = 2,
    FfuWglMissingFunction = 3,
    FfuWglWrongContext = 4,
    FfuWglInvalidTexture = 5,
    FfuWglOpenDeviceFailed = 6,
    FfuWglRegisterFailed = 7,
    FfuWglLockFailed = 8,
    FfuWglUnlockFailed = 9,
    FfuWglUnregisterFailed = 10,
    FfuWglCloseDeviceFailed = 11,
    FfuWglNotInitialized = 12,
    FfuWglAlreadyLocked = 13,
    FfuWglNotLocked = 14,
    FfuWglRetiring = 15
};

// Call during a Unity render event, with Unity's actual OpenGL context current.
bool FfuWglSupports();
int FfuWglContextStatus();

// No GL work: may be called on the managed/main thread. Retains device/texture.
// existingTextureName must belong to Unity's render-context share group and
// describe the same width/height/RGBA8, single-mip Texture2D without MSAA.
// A borrowed name is NEVER deleted by this helper. Caller keeps the Unity
// Texture2D alive until successful FfuWglDestroy on the render thread.
// Pass zero to create an owned GL name asynchronously in FfuWglInitialize.
// Returns null for an incompatible D3D resource, device mismatch or allocation failure.
FfuWglSurface* FfuWglCreatePending(ID3D11Device* device, ID3D11Texture2D* texture,
                                  uint32_t existingTextureName = 0);

// These operations require the SAME current GL context that initialized the
// surface, on Unity's render thread. No D3D writes may happen while locked.
bool FfuWglInitialize(FfuWglSurface* surface);
bool FfuWglLock(FfuWglSurface* surface);   // after D3D video processing, before Unity samples
bool FfuWglUnlock(FfuWglSurface* surface); // after Unity's final draw/blit referencing it

// Retires, unlocks if necessary, unregisters, closes the interop device and
// releases COM references. Deletes only an owned GL name. False means cleanup
// is incomplete: keep the surface alive and retry on its owning GL context;
// never delete/release it on another thread or during a context reset callback.
bool FfuWglDestroy(FfuWglSurface* surface);

// Atomic queries; caller must ensure the surface itself is still alive.
// Texture is zero until Initialize succeeds (including for a borrowed name).
uint32_t FfuWglTexture(const FfuWglSurface* surface);
int FfuWglStatus(const FfuWglSurface* surface);
uint32_t FfuWglNativeError(const FfuWglSurface* surface);
bool FfuWglIsLocked(const FfuWglSurface* surface);
