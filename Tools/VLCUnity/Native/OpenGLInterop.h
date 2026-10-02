#pragma once

// Optional Windows OpenGL consumer for D3D11 video surfaces.
// WGL_NV_DX_interop2 is required: WGL_NV_DX_interop alone supports only D3D9.
// All operations, including cleanup, must run in the Unity render-thread GL
// context. A resource may be used by D3D only while it is unlocked, and by GL
// only while it is locked. The lock/unlock operations also synchronize the GPUs.

#include <windows.h>
#include <d3d11.h>
#include <GL/gl.h>
#include <cstdint>
#include <cstring>
#include <vector>

class OpenGLInterop final
{
public:
    struct Texture
    {
        GLuint name = 0;

        // CreateExternalTexture expects the integer GL name cast to a pointer,
        // unlike Vulkan, which expects a pointer to the VkImage handle.
        void* NativePointer() const
        {
            return reinterpret_cast<void*>(static_cast<std::uintptr_t>(name));
        }
    };

    OpenGLInterop() = default;
    OpenGLInterop(const OpenGLInterop&) = delete;
    OpenGLInterop& operator=(const OpenGLInterop&) = delete;

    // Cleanup is explicit because player destruction can run on a managed
    // thread. Keep this object alive until Shutdown succeeds on the render
    // thread; the retained COM references protect registered resources.
    ~OpenGLInterop() { Shutdown(); }

    bool Initialize(ID3D11Device* device)
    {
        if (_interopDevice)
            return device == _device && IsCurrentContext();
        if (!device || !wglGetCurrentContext() || !wglGetCurrentDC())
            return Fail("A current Unity OpenGL context is required");
        if (device->GetCreationFlags() & D3D11_CREATE_DEVICE_SINGLETHREADED)
            return Fail("The D3D11 device must support multithreaded access");

        auto getExtensionsARB = Load<GetExtensionsARB>("wglGetExtensionsStringARB");
        auto getExtensionsEXT = Load<GetExtensionsEXT>("wglGetExtensionsStringEXT");
        const char* extensions = getExtensionsARB ? getExtensionsARB(wglGetCurrentDC()) : nullptr;
        if (!extensions && getExtensionsEXT)
            extensions = getExtensionsEXT();
        if (!HasExtension(extensions, "WGL_NV_DX_interop") ||
            !HasExtension(extensions, "WGL_NV_DX_interop2"))
            return Fail("WGL_NV_DX_interop2 is unavailable");

        _openDevice = Load<OpenDevice>("wglDXOpenDeviceNV");
        _closeDevice = Load<CloseDevice>("wglDXCloseDeviceNV");
        _registerObject = Load<RegisterObject>("wglDXRegisterObjectNV");
        _unregisterObject = Load<UnregisterObject>("wglDXUnregisterObjectNV");
        _lockObjects = Load<LockObjects>("wglDXLockObjectsNV");
        _unlockObjects = Load<UnlockObjects>("wglDXUnlockObjectsNV");
        if (!_openDevice || !_closeDevice || !_registerObject ||
            !_unregisterObject || !_lockObjects || !_unlockObjects)
            return Fail("The WGL D3D11 interoperability entry points are incomplete");

        // This also rejects D3D devices whose display adapter GL cannot access.
        _interopDevice = _openDevice(device);
        if (!_interopDevice)
            return Fail("OpenGL cannot open the selected D3D11 device");
        _context = wglGetCurrentContext();
        _device = device;
        _device->AddRef();
        _error = nullptr;
        return true;
    }

    bool RegisterTexture(ID3D11Texture2D* texture, Texture& result)
    {
        if (!IsCurrentContext() || !_interopDevice || !texture || result.name)
            return Fail("Invalid context or texture for WGL registration");

        D3D11_TEXTURE2D_DESC description{};
        texture->GetDesc(&description);
        if (description.Usage != D3D11_USAGE_DEFAULT ||
            description.ArraySize != 1 || description.SampleDesc.Count != 1 ||
            description.MipLevels != 1 || description.CPUAccessFlags != 0)
            return Fail("WGL video interop requires a single default-usage 2D surface");
        ID3D11Device* textureDevice = nullptr;
        texture->GetDevice(&textureDevice);
        const bool sameDevice = textureDevice == _device;
        if (textureDevice)
            textureDevice->Release();
        if (!sameDevice)
            return Fail("The video texture belongs to a different D3D11 device");

        GLuint name = 0;
        glGenTextures(1, &name);
        if (!name)
            return Fail("OpenGL failed to allocate a video texture name");
        constexpr GLenum readOnly = 0x0000; // WGL_ACCESS_READ_ONLY_NV
        HANDLE object = _registerObject(_interopDevice, texture, name, GL_TEXTURE_2D, readOnly);
        if (!object)
        {
            glDeleteTextures(1, &name);
            return Fail("OpenGL rejected the D3D11 video surface");
        }

        texture->AddRef();
        _textures.push_back({name, object, texture, false});
        result.name = name;
        _error = nullptr;
        return true;
    }

    // Keep locked for the entire interval in which Unity may sample this name.
    bool Lock(Texture& texture)
    {
        Entry* entry = Find(texture);
        if (!entry || !IsCurrentContext())
            return Fail("Invalid context or texture for WGL lock");
        if (entry->locked)
            return true;
        if (!_lockObjects(_interopDevice, 1, &entry->object))
            return Fail("OpenGL failed to acquire the D3D11 video surface");
        entry->locked = true;
        return true;
    }

    bool Unlock(Texture& texture)
    {
        Entry* entry = Find(texture);
        if (!entry || !IsCurrentContext())
            return Fail("Invalid context or texture for WGL unlock");
        if (!entry->locked)
            return true;
        if (!_unlockObjects(_interopDevice, 1, &entry->object))
            return Fail("OpenGL failed to release the D3D11 video surface");
        entry->locked = false;
        return true;
    }

    // Call only after Unity has stopped using the external texture. GL unlock
    // waits for previous GL accesses, but cannot prevent future Unity sampling.
    bool UnregisterTexture(Texture& texture)
    {
        if (!texture.name)
            return true;
        if (!IsCurrentContext())
            return Fail("A current Unity OpenGL context is required for cleanup");
        for (auto it = _textures.begin(); it != _textures.end(); ++it)
        {
            if (it->name != texture.name)
                continue;
            if (!Unlock(texture) || !_unregisterObject(_interopDevice, it->object))
                return Fail("OpenGL failed to unregister a D3D11 video surface");
            glDeleteTextures(1, &it->name);
            it->resource->Release();
            _textures.erase(it);
            texture.name = 0;
            return true;
        }
        return Fail("The video texture is not registered with this WGL device");
    }

    bool Shutdown()
    {
        if (!_interopDevice)
            return true;
        if (!IsCurrentContext())
            return Fail("WGL cleanup must be scheduled on Unity's render thread");
        while (!_textures.empty())
        {
            Texture texture{_textures.back().name};
            if (!UnregisterTexture(texture))
                return false;
        }
        if (!_closeDevice(_interopDevice))
            return Fail("OpenGL failed to close the D3D11 interop device");
        _interopDevice = nullptr;
        _context = nullptr;
        _device->Release();
        _device = nullptr;
        _error = nullptr;
        return true;
    }

    const char* LastError() const { return _error ? _error : ""; }

private:
    using GetExtensionsARB = const char* (WINAPI*)(HDC);
    using GetExtensionsEXT = const char* (WINAPI*)();
    using OpenDevice = HANDLE (WINAPI*)(void*);
    using CloseDevice = BOOL (WINAPI*)(HANDLE);
    using RegisterObject = HANDLE (WINAPI*)(HANDLE, void*, GLuint, GLenum, GLenum);
    using UnregisterObject = BOOL (WINAPI*)(HANDLE, HANDLE);
    using LockObjects = BOOL (WINAPI*)(HANDLE, GLint, HANDLE*);
    using UnlockObjects = BOOL (WINAPI*)(HANDLE, GLint, HANDLE*);

    struct Entry
    {
        GLuint name;
        HANDLE object;
        ID3D11Texture2D* resource;
        bool locked;
    };

    template<typename T> static T Load(const char* name)
    {
        PROC address = wglGetProcAddress(name);
        const auto value = reinterpret_cast<std::uintptr_t>(address);
        // Windows ICDs can return these sentinel values for unavailable names.
        if (!address || value == 1 || value == 2 || value == 3 ||
            value == static_cast<std::uintptr_t>(-1))
            return nullptr;
        return reinterpret_cast<T>(address);
    }

    static bool HasExtension(const char* extensions, const char* requested)
    {
        if (!extensions)
            return false;
        const size_t length = std::strlen(requested);
        for (const char* match = std::strstr(extensions, requested); match;
             match = std::strstr(match + length, requested))
        {
            if ((match == extensions || match[-1] == ' ') &&
                (match[length] == '\0' || match[length] == ' '))
                return true;
        }
        return false;
    }

    Entry* Find(const Texture& texture)
    {
        for (auto& entry : _textures)
            if (entry.name == texture.name)
                return &entry;
        return nullptr;
    }

    bool IsCurrentContext() const
    {
        return _context && wglGetCurrentContext() == _context;
    }

    bool Fail(const char* message)
    {
        _error = message;
        return false;
    }

    HGLRC _context = nullptr;
    HANDLE _interopDevice = nullptr;
    ID3D11Device* _device = nullptr;
    OpenDevice _openDevice = nullptr;
    CloseDevice _closeDevice = nullptr;
    RegisterObject _registerObject = nullptr;
    UnregisterObject _unregisterObject = nullptr;
    LockObjects _lockObjects = nullptr;
    UnlockObjects _unlockObjects = nullptr;
    std::vector<Entry> _textures;
    const char* _error = nullptr;
};
