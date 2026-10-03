// A real EGL/GL driver test. Readback is used ONLY to validate pixel contents.
// The production backend never maps or reads video pixels on the CPU.
#include "PortableGpuBackend.h"
#define GL_GLEXT_PROTOTYPES
#include <GL/gl.h>
#include <GL/glext.h>
#include <GL/glx.h>
#include <GL/glxext.h>
#include <EGL/egl.h>
#include <EGL/eglext.h>
#include <array>
#include <cstdio>
#include <cstring>
#include <stdexcept>
#include <thread>

static void Check(bool success, const char* message)
{
    if (!success) throw std::runtime_error(message);
}

int main(int argc, char** argv)
{
    try {
        const bool useGlx = argc > 1 && std::strcmp(argv[1], "--glx") == 0;
        Display* xDisplay = nullptr;
        GLXContext glxContext = nullptr;
        GLXPbuffer glxSurface = 0;
        EGLDisplay display = EGL_NO_DISPLAY;
        EGLContext context = EGL_NO_CONTEXT;
        EGLSurface surface = EGL_NO_SURFACE;
        if (useGlx) {
            Check(XInitThreads() != 0, "XInitThreads failed");
            xDisplay = XOpenDisplay(nullptr);
            Check(xDisplay != nullptr, "No X display for GLX test");
            const int attributes[] = { GLX_X_RENDERABLE, True, GLX_DRAWABLE_TYPE, GLX_PBUFFER_BIT,
                GLX_RENDER_TYPE, GLX_RGBA_BIT, GLX_RED_SIZE, 8, GLX_GREEN_SIZE, 8,
                GLX_BLUE_SIZE, 8, GLX_ALPHA_SIZE, 8, None };
            int count = 0;
            auto configs = glXChooseFBConfig(xDisplay, DefaultScreen(xDisplay), attributes, &count);
            Check(configs && count, "No GLX RGBA pbuffer configuration");
            auto create = reinterpret_cast<PFNGLXCREATECONTEXTATTRIBSARBPROC>(glXGetProcAddressARB(
                reinterpret_cast<const GLubyte*>("glXCreateContextAttribsARB")));
            Check(create != nullptr, "No core GLX context creation entry point");
            const int contextAttributes[] = { GLX_CONTEXT_MAJOR_VERSION_ARB, 3, GLX_CONTEXT_MINOR_VERSION_ARB, 3,
                GLX_CONTEXT_PROFILE_MASK_ARB, GLX_CONTEXT_CORE_PROFILE_BIT_ARB, None };
            glxContext = create(xDisplay, configs[0], nullptr, True, contextAttributes);
            const int surfaceAttributes[] = { GLX_PBUFFER_WIDTH, 1, GLX_PBUFFER_HEIGHT, 1, None };
            glxSurface = glXCreatePbuffer(xDisplay, configs[0], surfaceAttributes);
            XFree(configs);
            Check(glxContext && glxSurface && glXMakeContextCurrent(xDisplay, glxSurface, glxSurface, glxContext),
                "Cannot activate the GLX consumer context");
        } else {
            auto getDisplay = reinterpret_cast<PFNEGLGETPLATFORMDISPLAYEXTPROC>(eglGetProcAddress("eglGetPlatformDisplayEXT"));
            display = getDisplay ? getDisplay(EGL_PLATFORM_SURFACELESS_MESA, EGL_DEFAULT_DISPLAY, nullptr) : EGL_NO_DISPLAY;
            if (display == EGL_NO_DISPLAY || !eglInitialize(display, nullptr, nullptr)) {
                display = eglGetDisplay(EGL_DEFAULT_DISPLAY);
                Check(eglInitialize(display, nullptr, nullptr), "Cannot initialize a test EGL display");
            }
            Check(eglBindAPI(EGL_OPENGL_API), "Cannot bind desktop OpenGL");
            const EGLint attributes[] = { EGL_SURFACE_TYPE, EGL_PBUFFER_BIT, EGL_RENDERABLE_TYPE, EGL_OPENGL_BIT,
                EGL_RED_SIZE, 8, EGL_GREEN_SIZE, 8, EGL_BLUE_SIZE, 8, EGL_ALPHA_SIZE, 8, EGL_NONE };
            EGLConfig config = nullptr; EGLint count = 0;
            Check(eglChooseConfig(display, attributes, &config, 1, &count) && count == 1, "No test EGL RGBA configuration");
            const EGLint contextAttributes[] = { EGL_CONTEXT_MAJOR_VERSION_KHR, 3, EGL_CONTEXT_MINOR_VERSION_KHR, 3,
                EGL_CONTEXT_OPENGL_PROFILE_MASK_KHR, EGL_CONTEXT_OPENGL_CORE_PROFILE_BIT_KHR, EGL_NONE };
            context = eglCreateContext(display, config, EGL_NO_CONTEXT, contextAttributes);
            const EGLint surfaceAttributes[] = { EGL_WIDTH, 1, EGL_HEIGHT, 1, EGL_NONE };
            surface = eglCreatePbufferSurface(display, config, surfaceAttributes);
            Check(context != EGL_NO_CONTEXT && surface != EGL_NO_SURFACE && eglMakeCurrent(display, surface, surface, context), "Cannot activate the consumer context");
        }
        auto consumerCurrent = [&] { return useGlx ? glXGetCurrentContext() == glxContext : eglGetCurrentContext() == context; };
        std::printf("OpenGL renderer: %s\n", glGetString(GL_RENDERER));
        auto backend = CreatePortableGpuBackend(kUnityGfxRendererOpenGLCore);
        Check(backend && backend->Initialize(nullptr, kUnityGfxRendererOpenGLCore), backend ? backend->LastError() : "No GL backend");
        Check(consumerCurrent(), "Initialization moved Unity's context");
        unsigned width = 64, height = 48;
        void* firstTexture = nullptr;
        for (int frame = 0; frame < 32; ++frame) {
            if (frame >= 16) { width = 79; height = 53; }
            bool produced = false;
            std::thread producer([&] {
                if (!backend->MakeCurrent(true)) return;
                if (backend->Resize(width, height)) {
                    glClearColor(frame % 2 ? 1.f : 0.f, frame % 2 ? 0.f : 1.f, 0.f, 1.f);
                    glClear(GL_COLOR_BUFFER_BIT);
                    produced = backend->FrameComplete();
                }
                produced = backend->MakeCurrent(false) && produced;
            });
            producer.join();
            Check(produced, backend->LastError());
            Check(consumerCurrent(), "Producer disturbed the consumer context");
            Check(backend->Pump() && backend->Begin(), backend->LastError());
            const auto info = backend->TextureInfo();
            Check(info.texture && info.width == width && info.height == height, "Wrong texture size/handle");
            if (frame == 0) firstTexture = info.texture;
            if (frame < 16) Check(firstTexture == info.texture, "Same-size video allocated new textures");
            if (frame >= 16) Check(firstTexture != info.texture && glIsTexture(static_cast<GLuint>(reinterpret_cast<uintptr_t>(firstTexture))),
                "Resolution change invalidated an in-flight old texture");
            GLuint framebuffer = 0;
            glGenFramebuffers(1, &framebuffer); glBindFramebuffer(GL_FRAMEBUFFER, framebuffer);
            glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D,
                static_cast<GLuint>(reinterpret_cast<uintptr_t>(info.texture)), 0);
            Check(glCheckFramebufferStatus(GL_FRAMEBUFFER) == GL_FRAMEBUFFER_COMPLETE, "Consumer cannot sample the producer texture");
            std::array<unsigned char, 4> pixel{};
            glReadPixels(width - 1, height - 1, 1, 1, GL_RGBA, GL_UNSIGNED_BYTE, pixel.data());
            Check(pixel[0] == (frame % 2 ? 255 : 0) && pixel[1] == (frame % 2 ? 0 : 255) && pixel[2] == 0 && pixel[3] == 255,
                "Incorrect shared RGBA pixels or stale producer writes");
            glBindFramebuffer(GL_FRAMEBUFFER, 0); glDeleteFramebuffers(1, &framebuffer);
            Check(backend->End(), backend->LastError());
        }
        backend->Retire();
        Check(consumerCurrent(), "Retire did not restore Unity's context");
        Check(!glIsTexture(static_cast<GLuint>(reinterpret_cast<uintptr_t>(firstTexture))), "Retire leaked the old texture");
        backend.reset();
        if (useGlx) {
            glXMakeContextCurrent(xDisplay, None, None, nullptr);
            glXDestroyPbuffer(xDisplay, glxSurface); glXDestroyContext(xDisplay, glxContext); XCloseDisplay(xDisplay);
        } else {
            eglMakeCurrent(display, EGL_NO_SURFACE, EGL_NO_SURFACE, EGL_NO_CONTEXT);
            eglDestroySurface(display, surface); eglDestroyContext(display, context); eglTerminate(display);
        }
        std::printf("PASS: 32 cross-thread %s shared frames, RGBA, resize, retained handles and cleanup\n", useGlx ? "GLX" : "EGL");
        return 0;
    } catch (const std::exception& error) {
        std::fprintf(stderr, "FAIL: %s\n", error.what());
        return 1;
    }
}
