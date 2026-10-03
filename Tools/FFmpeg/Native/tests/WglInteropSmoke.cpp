// Standalone, invisible-window native test for the product WGL helper.
// CPU readback appears only here, to assert GPU contents; the helper has none.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <GL/gl.h>
#include "../WglInterop.h"
#include <cstdio>
#include <vector>

#define CHECK(test) do { if (!(test)) { std::printf("FAIL line %d: %s\n", __LINE__, #test); return false; } } while (0)
using CreateContext = HGLRC(WINAPI*)(HDC,HGLRC,const int*);
using TexStorage = void(APIENTRY*)(GLenum,GLsizei,GLenum,GLsizei,GLsizei);

static bool VideoConversion(ID3D11Device* device, ID3D11DeviceContext* context, ID3D11Texture2D* output) {
    constexpr int width = 32, height = 32;
    std::vector<unsigned char> pixels(width * height * 3 / 2, 128);
    for (int y = 0; y < height; ++y)
        for (int x = 0; x < width; ++x) pixels[y * width + x] = y < height / 2 ? 235 : 16;
    D3D11_TEXTURE2D_DESC desc{};
    desc.Width = width; desc.Height = height; desc.ArraySize = desc.MipLevels = 1;
    desc.Format = DXGI_FORMAT_NV12; desc.SampleDesc.Count = 1; desc.BindFlags = D3D11_BIND_DECODER;
    D3D11_SUBRESOURCE_DATA initial{}; initial.pSysMem = pixels.data(); initial.SysMemPitch = width;
    ID3D11Texture2D* input = nullptr;
    CHECK(SUCCEEDED(device->CreateTexture2D(&desc, &initial, &input)));
    ID3D11VideoDevice* vd = nullptr; ID3D11VideoContext* vc = nullptr;
    CHECK(SUCCEEDED(device->QueryInterface(__uuidof(ID3D11VideoDevice), reinterpret_cast<void**>(&vd))));
    CHECK(SUCCEEDED(context->QueryInterface(__uuidof(ID3D11VideoContext), reinterpret_cast<void**>(&vc))));
    D3D11_VIDEO_PROCESSOR_CONTENT_DESC content{};
    content.InputFrameFormat = D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE;
    content.InputWidth = content.OutputWidth = width; content.InputHeight = content.OutputHeight = height;
    content.InputFrameRate = content.OutputFrameRate = {30,1}; content.Usage = D3D11_VIDEO_USAGE_PLAYBACK_NORMAL;
    ID3D11VideoProcessorEnumerator* enumerator = nullptr; ID3D11VideoProcessor* processor = nullptr;
    CHECK(SUCCEEDED(vd->CreateVideoProcessorEnumerator(&content, &enumerator)));
    CHECK(SUCCEEDED(vd->CreateVideoProcessor(enumerator, 0, &processor)));
    D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC inDesc{}; inDesc.ViewDimension = D3D11_VPIV_DIMENSION_TEXTURE2D;
    D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC outDesc{}; outDesc.ViewDimension = D3D11_VPOV_DIMENSION_TEXTURE2D;
    ID3D11VideoProcessorInputView* inView = nullptr; ID3D11VideoProcessorOutputView* outView = nullptr;
    CHECK(SUCCEEDED(vd->CreateVideoProcessorInputView(input,enumerator,&inDesc,&inView)));
    CHECK(SUCCEEDED(vd->CreateVideoProcessorOutputView(output,enumerator,&outDesc,&outView)));
    RECT rect{0,0,width,height};
    vc->VideoProcessorSetStreamFrameFormat(processor,0,D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE);
    vc->VideoProcessorSetStreamAutoProcessingMode(processor,0,FALSE);
    vc->VideoProcessorSetStreamSourceRect(processor,0,TRUE,&rect);
    vc->VideoProcessorSetStreamDestRect(processor,0,TRUE,&rect);
    vc->VideoProcessorSetOutputTargetRect(processor,TRUE,&rect);
    D3D11_VIDEO_PROCESSOR_COLOR_SPACE inColor{}, outColor{};
    inColor.YCbCr_Matrix = 1; inColor.Nominal_Range = 1; outColor.Nominal_Range = 2;
    vc->VideoProcessorSetStreamColorSpace(processor,0,&inColor);
    vc->VideoProcessorSetOutputColorSpace(processor,&outColor);
    D3D11_VIDEO_PROCESSOR_STREAM stream{}; stream.Enable = TRUE; stream.pInputSurface = inView;
    CHECK(SUCCEEDED(vc->VideoProcessorBlt(processor,outView,0,1,&stream)));
    context->Flush();
    outView->Release(); inView->Release(); processor->Release(); enumerator->Release(); vc->Release(); vd->Release(); input->Release();
    return true;
}

static bool Run(ID3D11Device* device, ID3D11DeviceContext* immediate, HDC dc, HGLRC owner, int mode) {
    GLuint borrowed = 0;
    if (mode) {
        glGenTextures(1,&borrowed); glBindTexture(GL_TEXTURE_2D,borrowed);
        if (mode == 2) {
            auto storage = reinterpret_cast<TexStorage>(wglGetProcAddress("glTexStorage2D")); CHECK(storage);
            storage(GL_TEXTURE_2D,1,0x8058,32,32);
        } else glTexImage2D(GL_TEXTURE_2D,0,0x8058,32,32,0,GL_RGBA,GL_UNSIGNED_BYTE,nullptr);
        glTexParameteri(GL_TEXTURE_2D,GL_TEXTURE_MIN_FILTER,GL_LINEAR);
    }
    D3D11_TEXTURE2D_DESC desc{};
    desc.Width = desc.Height = 32; desc.MipLevels = desc.ArraySize = 1; desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.SampleDesc.Count = 1; desc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
    ID3D11Texture2D* texture = nullptr; ID3D11RenderTargetView* rtv = nullptr;
    CHECK(SUCCEEDED(device->CreateTexture2D(&desc,nullptr,&texture)));
    CHECK(SUCCEEDED(device->CreateRenderTargetView(texture,nullptr,&rtv)));
    CHECK(wglMakeCurrent(nullptr,nullptr));
    auto* surface = FfuWglCreatePending(device,texture,borrowed); CHECK(surface);
    CHECK(FfuWglTexture(surface) == 0);
    CHECK(!FfuWglInitialize(surface)); CHECK(FfuWglStatus(surface) == FfuWglNoContext);
    CHECK(wglMakeCurrent(dc,owner));
    CHECK(FfuWglInitialize(surface)); CHECK(FfuWglInitialize(surface));
    GLuint name = FfuWglTexture(surface); CHECK(name != 0); CHECK(!mode || name == borrowed);
    CHECK(!FfuWglUnlock(surface)); CHECK(FfuWglStatus(surface) == FfuWglNotLocked);
    // A non-shared context must not be allowed to operate on the registered texture.
    HGLRC other = wglCreateContext(dc); CHECK(other); CHECK(wglMakeCurrent(dc,other));
    CHECK(!FfuWglLock(surface)); CHECK(FfuWglStatus(surface) == FfuWglWrongContext);
    CHECK(wglMakeCurrent(dc,owner));
    std::vector<unsigned char> result(32*32*4);
    for (int frame = 0; frame < 10; ++frame) {
        const float color[]{frame % 2 ? 0.f : 1.f, frame % 2 ? 1.f : 0.f,0.f,1.f};
        immediate->ClearRenderTargetView(rtv,color); immediate->Flush();
        CHECK(FfuWglLock(surface)); CHECK(FfuWglIsLocked(surface));
        CHECK(!FfuWglLock(surface)); CHECK(FfuWglStatus(surface) == FfuWglAlreadyLocked);
        glBindTexture(GL_TEXTURE_2D,name); glGetTexImage(GL_TEXTURE_2D,0,GL_RGBA,GL_UNSIGNED_BYTE,result.data());
        CHECK(glGetError() == GL_NO_ERROR); CHECK(result[0] == (frame % 2 ? 0 : 255)); CHECK(result[1] == (frame % 2 ? 255 : 0));
        CHECK(FfuWglUnlock(surface)); CHECK(!FfuWglIsLocked(surface));
    }
    CHECK(VideoConversion(device,immediate,texture));
    CHECK(FfuWglLock(surface));
    glBindTexture(GL_TEXTURE_2D,name); glGetTexImage(GL_TEXTURE_2D,0,GL_RGBA,GL_UNSIGNED_BYTE,result.data());
    CHECK(glGetError() == GL_NO_ERROR);
    CHECK(result[(8 * 32 + 8) * 4] >= 240); CHECK(result[(24 * 32 + 8) * 4] <= 15);
    // Drop original COM references before cleanup: the helper owns its resources.
    rtv->Release(); texture->Release();
    // Destruction on a wrong context must preserve resources and remain retryable.
    CHECK(wglMakeCurrent(dc,other)); CHECK(!FfuWglDestroy(surface)); CHECK(FfuWglTexture(surface) == 0);
    CHECK(FfuWglIsLocked(surface)); CHECK(FfuWglStatus(surface) == FfuWglWrongContext);
    CHECK(wglMakeCurrent(dc,owner)); CHECK(FfuWglDestroy(surface));
    CHECK(!!glIsTexture(name) == !!mode); // owned name deleted; Unity-owned name survives
    if (mode) glDeleteTextures(1,&borrowed);
    CHECK(wglDeleteContext(other));
    std::printf("PASS WGL mode=%s: 10 ownership cycles, NV12 GPU conversion, wrong-context rejection, retryable cleanup, texture ownership\n",
                mode == 2 ? "borrowed immutable" : mode == 1 ? "borrowed mutable" : "owned");
    return true;
}

int main() {
    WNDCLASSW wc{}; wc.lpfnWndProc = DefWindowProcW; wc.hInstance = GetModuleHandleW(nullptr); wc.lpszClassName = L"FfuWglHelperSmoke"; wc.style = CS_OWNDC;
    if (!RegisterClassW(&wc)) return 1;
    HWND window = CreateWindowW(wc.lpszClassName,L"Hidden native WGL smoke",WS_OVERLAPPEDWINDOW,0,0,64,64,nullptr,nullptr,wc.hInstance,nullptr);
    HDC dc = GetDC(window);
    PIXELFORMATDESCRIPTOR pfd{}; pfd.nSize = sizeof(pfd); pfd.nVersion = 1;
    pfd.dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER; pfd.iPixelType = PFD_TYPE_RGBA; pfd.cColorBits = 32;
    const int pixel = ChoosePixelFormat(dc,&pfd); if (!pixel || !SetPixelFormat(dc,pixel,&pfd)) return 1;
    HGLRC context = wglCreateContext(dc); if (!context || !wglMakeCurrent(dc,context)) return 1;
    auto create = reinterpret_cast<CreateContext>(wglGetProcAddress("wglCreateContextAttribsARB"));
    if (create) {
        const int attributes[]{0x2091,4,0x2092,5,0x9126,1,0}; HGLRC core = create(dc,nullptr,attributes);
        if (core) { wglMakeCurrent(nullptr,nullptr); wglDeleteContext(context); context = core; wglMakeCurrent(dc,context); }
    }
    std::printf("GL_RENDERER=%s\nGL_VERSION=%s\n",glGetString(GL_RENDERER),glGetString(GL_VERSION));
    if (!FfuWglSupports()) { std::printf("SKIP WGL interop2: status=%d\n",FfuWglContextStatus()); return 77; }
    ID3D11Device* device = nullptr; ID3D11DeviceContext* immediate = nullptr; D3D_FEATURE_LEVEL level{};
    if (FAILED(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_VIDEO_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&device,&level,&immediate))) return 77;
    bool success = Run(device,immediate,dc,context,0) && Run(device,immediate,dc,context,1) && Run(device,immediate,dc,context,2);
    immediate->Release(); device->Release(); wglMakeCurrent(nullptr,nullptr); wglDeleteContext(context); ReleaseDC(window,dc); DestroyWindow(window);
    return success ? 0 : 1;
}
