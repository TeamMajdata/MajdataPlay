#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include "WglInterop.h"
#include <windows.h>
#include <d3d11.h>
#include <GL/gl.h>
#include <atomic>
#include <cstring>
#include <new>

namespace {
using GetExtensions = const char* (WINAPI*)(HDC);
using OpenDevice = HANDLE (WINAPI*)(void*);
using CloseDevice = BOOL (WINAPI*)(HANDLE);
using RegisterObject = HANDLE (WINAPI*)(HANDLE, void*, GLuint, GLenum, GLenum);
using UnregisterObject = BOOL (WINAPI*)(HANDLE, HANDLE);
using LockObjects = BOOL (WINAPI*)(HANDLE, GLint, HANDLE*);
struct Functions {
    OpenDevice open = nullptr;
    CloseDevice close = nullptr;
    RegisterObject registerObject = nullptr;
    UnregisterObject unregisterObject = nullptr;
    LockObjects lock = nullptr;
    LockObjects unlock = nullptr;
};
template<class T> T Load(const char* name) {
    const auto value = wglGetProcAddress(name);
    const auto address = reinterpret_cast<uintptr_t>(value);
    // Windows ICDs may return these sentinel values instead of nullptr.
    return !value || address <= 3 || address == ~uintptr_t(0) ? nullptr : reinterpret_cast<T>(value);
}
bool HasExtension(const char* list, const char* name) {
    if (!list) return false;
    const size_t length = std::strlen(name);
    for (const char* found = list; (found = std::strstr(found, name)); found += length)
        if ((found == list || found[-1] == ' ') && (found[length] == ' ' || found[length] == '\0')) return true;
    return false;
}
int Probe(Functions* functions) {
    if (!wglGetCurrentContext() || !wglGetCurrentDC()) return FfuWglNoContext;
    const auto extensions = Load<GetExtensions>("wglGetExtensionsStringARB");
    if (!extensions) return FfuWglMissingFunction;
    const char* names = extensions(wglGetCurrentDC());
    if (!HasExtension(names, "WGL_NV_DX_interop") || !HasExtension(names, "WGL_NV_DX_interop2"))
        return FfuWglMissingExtension;
    Functions result;
    result.open = Load<OpenDevice>("wglDXOpenDeviceNV");
    result.close = Load<CloseDevice>("wglDXCloseDeviceNV");
    result.registerObject = Load<RegisterObject>("wglDXRegisterObjectNV");
    result.unregisterObject = Load<UnregisterObject>("wglDXUnregisterObjectNV");
    result.lock = Load<LockObjects>("wglDXLockObjectsNV");
    result.unlock = Load<LockObjects>("wglDXUnlockObjectsNV");
    if (!result.open || !result.close || !result.registerObject || !result.unregisterObject || !result.lock || !result.unlock)
        return FfuWglMissingFunction;
    if (functions) *functions = result;
    return FfuWglOk;
}
}

struct FfuWglSurface {
    ID3D11Device* device = nullptr;
    ID3D11Texture2D* texture = nullptr;
    uint32_t requestedName = 0;
    bool ownsName = false;
    GLuint name = 0;
    HGLRC context = nullptr;
    HANDLE interop = nullptr;
    HANDLE object = nullptr;
    Functions functions;
    std::atomic<uint32_t> publishedName{0};
    std::atomic<int> status{FfuWglOk};
    std::atomic<uint32_t> nativeError{0};
    std::atomic<bool> locked{false};
    std::atomic<bool> retiring{false};

    bool Error(int code, uint32_t native = 0) {
        nativeError.store(native);
        status.store(code);
        return false;
    }
    bool Current() {
        if (!wglGetCurrentContext()) return Error(FfuWglNoContext);
        if (context && context != wglGetCurrentContext()) return Error(FfuWglWrongContext);
        return true;
    }
    void Success() { nativeError.store(0); status.store(FfuWglOk); }
};

bool FfuWglSupports() { return Probe(nullptr) == FfuWglOk; }
int FfuWglContextStatus() { return Probe(nullptr); }

FfuWglSurface* FfuWglCreatePending(ID3D11Device* device, ID3D11Texture2D* texture, uint32_t existingTextureName) {
    if (!device || !texture) return nullptr;
    D3D11_TEXTURE2D_DESC desc{};
    texture->GetDesc(&desc);
    if (desc.Usage != D3D11_USAGE_DEFAULT || desc.Format != DXGI_FORMAT_R8G8B8A8_UNORM ||
        desc.MipLevels != 1 || desc.ArraySize != 1 || desc.SampleDesc.Count != 1 || desc.CPUAccessFlags != 0)
        return nullptr;
    ID3D11Device* owner = nullptr;
    texture->GetDevice(&owner);
    const bool sameDevice = owner == device;
    if (owner) owner->Release();
    if (!sameDevice) return nullptr;
    auto* surface = new (std::nothrow) FfuWglSurface();
    if (!surface) return nullptr;
    device->AddRef(); texture->AddRef();
    surface->device = device;
    surface->texture = texture;
    surface->requestedName = existingTextureName;
    return surface;
}

bool FfuWglInitialize(FfuWglSurface* surface) {
    if (!surface) return false;
    if (surface->retiring.load()) return surface->Error(FfuWglRetiring);
    if (!surface->Current()) return false;
    if (surface->object) { surface->Success(); return true; }
    const int capability = Probe(&surface->functions);
    if (capability != FfuWglOk) return surface->Error(capability);
    surface->context = wglGetCurrentContext();
    if (surface->requestedName && !glIsTexture(surface->requestedName)) return surface->Error(FfuWglInvalidTexture);
    if (!surface->interop) {
        SetLastError(ERROR_SUCCESS);
        surface->interop = surface->functions.open(surface->device);
        if (!surface->interop) return surface->Error(FfuWglOpenDeviceFailed, GetLastError());
    }
    if (!surface->name) {
        surface->name = surface->requestedName;
        if (!surface->name) {
            glGenTextures(1, &surface->name);
            if (!surface->name) return surface->Error(FfuWglInvalidTexture);
            surface->ownsName = true;
            GLint previous = 0;
            glGetIntegerv(GL_TEXTURE_BINDING_2D, &previous);
            glBindTexture(GL_TEXTURE_2D, surface->name);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, 0x812F); // GL_CLAMP_TO_EDGE
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, 0x812F);
            glBindTexture(GL_TEXTURE_2D, static_cast<GLuint>(previous));
        }
    }
    SetLastError(ERROR_SUCCESS);
    surface->object = surface->functions.registerObject(surface->interop, surface->texture, surface->name, GL_TEXTURE_2D, 0); // WGL_ACCESS_READ_ONLY_NV
    if (!surface->object) return surface->Error(FfuWglRegisterFailed, GetLastError());
    surface->publishedName.store(surface->name);
    surface->Success();
    return true;
}

bool FfuWglLock(FfuWglSurface* surface) {
    if (!surface) return false;
    if (surface->retiring.load()) return surface->Error(FfuWglRetiring);
    if (!surface->Current()) return false;
    if (!surface->object) return surface->Error(FfuWglNotInitialized);
    if (surface->locked.load()) return surface->Error(FfuWglAlreadyLocked);
    SetLastError(ERROR_SUCCESS);
    if (!surface->functions.lock(surface->interop, 1, &surface->object)) return surface->Error(FfuWglLockFailed, GetLastError());
    surface->locked.store(true);
    surface->Success();
    return true;
}

bool FfuWglUnlock(FfuWglSurface* surface) {
    if (!surface) return false;
    if (!surface->Current()) return false;
    if (!surface->object) return surface->Error(FfuWglNotInitialized);
    if (!surface->locked.load()) return surface->Error(FfuWglNotLocked);
    SetLastError(ERROR_SUCCESS);
    if (!surface->functions.unlock(surface->interop, 1, &surface->object)) return surface->Error(FfuWglUnlockFailed, GetLastError());
    surface->locked.store(false);
    surface->Success();
    return true;
}

bool FfuWglDestroy(FfuWglSurface* surface) {
    if (!surface) return true;
    surface->retiring.store(true);
    surface->publishedName.store(0);
    // Pending creation retains only COM objects and can be cancelled without a GL context.
    if (surface->context && !surface->Current()) return false;
    if (surface->locked.load() && !FfuWglUnlock(surface)) return false;
    if (surface->object) {
        SetLastError(ERROR_SUCCESS);
        if (!surface->functions.unregisterObject(surface->interop, surface->object))
            return surface->Error(FfuWglUnregisterFailed, GetLastError());
        surface->object = nullptr;
    }
    if (surface->interop) {
        SetLastError(ERROR_SUCCESS);
        if (!surface->functions.close(surface->interop)) return surface->Error(FfuWglCloseDeviceFailed, GetLastError());
        surface->interop = nullptr;
    }
    if (surface->ownsName && surface->name) glDeleteTextures(1, &surface->name);
    surface->texture->Release();
    surface->device->Release();
    delete surface;
    return true;
}

uint32_t FfuWglTexture(const FfuWglSurface* surface) { return surface ? surface->publishedName.load() : 0; }
int FfuWglStatus(const FfuWglSurface* surface) { return surface ? surface->status.load() : FfuWglInvalidTexture; }
uint32_t FfuWglNativeError(const FfuWglSurface* surface) { return surface ? surface->nativeError.load() : 0; }
bool FfuWglIsLocked(const FfuWglSurface* surface) { return surface && surface->locked.load(); }
#endif
