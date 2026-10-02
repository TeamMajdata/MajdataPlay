// Standalone driver smoke test; this file is not part of the Unity plugin DLL.
// Exit 77 means that this machine does not support WGL D3D11 interoperability.
#include "OpenGLInterop.h"
#include <array>
#include <cstdio>
#include <wrl/client.h>

using Microsoft::WRL::ComPtr;

int main()
{
    const HINSTANCE module = GetModuleHandleW(nullptr);
    WNDCLASSW windowClass{};
    windowClass.style = CS_OWNDC;
    windowClass.lpfnWndProc = DefWindowProcW;
    windowClass.hInstance = module;
    windowClass.lpszClassName = L"VLCUnityOpenGLInteropSmoke";
    if (!RegisterClassW(&windowClass))
        return 1;
    HWND window = CreateWindowExW(0, windowClass.lpszClassName, L"", WS_POPUP,
        0, 0, 16, 16, nullptr, nullptr, module, nullptr);
    if (!window)
        return 1;
    HDC dc = GetDC(window);
    PIXELFORMATDESCRIPTOR pixelFormat{};
    pixelFormat.nSize = sizeof(pixelFormat);
    pixelFormat.nVersion = 1;
    pixelFormat.dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL;
    pixelFormat.iPixelType = PFD_TYPE_RGBA;
    pixelFormat.cColorBits = 32;
    const int format = ChoosePixelFormat(dc, &pixelFormat);
    if (!format || !SetPixelFormat(dc, format, &pixelFormat))
        return 1;
    HGLRC contextGL = wglCreateContext(dc);
    if (!contextGL || !wglMakeCurrent(dc, contextGL))
        return 1;

    int result = [&]()
    {
        ComPtr<ID3D11Device> device;
        ComPtr<ID3D11DeviceContext> contextD3D;
        if (FAILED(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0, D3D11_SDK_VERSION,
            &device, nullptr, &contextD3D)))
        {
            std::puts("FAIL: unable to create the D3D11 hardware device");
            return 1;
        }

        std::printf("OpenGL vendor: %s\nOpenGL renderer: %s\n",
            reinterpret_cast<const char*>(glGetString(GL_VENDOR)),
            reinterpret_cast<const char*>(glGetString(GL_RENDERER)));
        OpenGLInterop interop;
        if (!interop.Initialize(device.Get()))
        {
            std::printf("SKIP: %s\n", interop.LastError());
            return 77;
        }
        D3D11_TEXTURE2D_DESC description{};
        description.Width = 4;
        description.Height = 4;
        description.MipLevels = 1;
        description.ArraySize = 1;
        description.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        description.SampleDesc.Count = 1;
        description.Usage = D3D11_USAGE_DEFAULT;
        description.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
        ComPtr<ID3D11Texture2D> resource;
        ComPtr<ID3D11RenderTargetView> renderTarget;
        if (FAILED(device->CreateTexture2D(&description, nullptr, &resource)) ||
            FAILED(device->CreateRenderTargetView(resource.Get(), nullptr, &renderTarget)))
        {
            std::puts("FAIL: unable to create the D3D11 test surface");
            return 1;
        }
        OpenGLInterop::Texture texture;
        if (!interop.RegisterTexture(resource.Get(), texture))
        {
            std::printf("FAIL: %s\n", interop.LastError());
            return 1;
        }

        // Repeat ownership transfers and verify all pixels, including nontrivial
        // channel values. Readback is intentionally confined to this smoke test.
        for (int frame = 0; frame < 32; ++frame)
        {
            const std::array<float, 4> color = frame % 2
                ? std::array<float, 4>{0.75f, 0.25f, 0.5f, 1.0f}
                : std::array<float, 4>{0.25f, 0.5f, 0.75f, 1.0f};
            contextD3D->ClearRenderTargetView(renderTarget.Get(), color.data());
            contextD3D->Flush();
            if (!interop.Lock(texture))
            {
                std::printf("FAIL: %s\n", interop.LastError());
                return 1;
            }
            std::array<unsigned char, 4 * 4 * 4> pixels{};
            glBindTexture(GL_TEXTURE_2D, texture.name);
            glGetTexImage(GL_TEXTURE_2D, 0, GL_RGBA, GL_UNSIGNED_BYTE, pixels.data());
            if (glGetError() != GL_NO_ERROR)
            {
                std::puts("FAIL: OpenGL could not read the registered texture");
                return 1;
            }
            for (size_t pixel = 0; pixel < 16; ++pixel)
                for (size_t channel = 0; channel < 4; ++channel)
                {
                    const int expected = static_cast<int>(color[channel] * 255.0f + 0.5f);
                    const int difference = pixels[pixel * 4 + channel] - expected;
                    if (difference < -1 || difference > 1)
                    {
                        std::printf("FAIL: frame %d pixel %zu channel %zu: %u, expected %d\n",
                            frame, pixel, channel, pixels[pixel * 4 + channel], expected);
                        return 1;
                    }
                }
            glBindTexture(GL_TEXTURE_2D, 0);
            if (!interop.Unlock(texture))
            {
                std::printf("FAIL: %s\n", interop.LastError());
                return 1;
            }
        }
        if (!interop.UnregisterTexture(texture) || !interop.Shutdown())
        {
            std::printf("FAIL: %s\n", interop.LastError());
            return 1;
        }
        std::puts("PASS: 32 D3D11/OpenGL ownership transfers, pixel values and cleanup");
        return 0;
    }();

    wglMakeCurrent(nullptr, nullptr);
    wglDeleteContext(contextGL);
    ReleaseDC(window, dc);
    DestroyWindow(window);
    UnregisterClassW(windowClass.lpszClassName, module);
    return result;
}
