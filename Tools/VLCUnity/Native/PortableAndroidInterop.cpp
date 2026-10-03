// Android GPU consumers. GLES shares an independent EGL context with Unity;
// Vulkan imports an AHardwareBuffer rendered through an EGLImage. No CPU pixel
// mapping/readback is used. glFinish/vkQueueWaitIdle serialize ownership.
#include "PortableGpuBackend.h"
#include <EGL/egl.h>
#include <EGL/eglext.h>
#include <GLES3/gl3.h>
#include <GLES2/gl2ext.h>
#include <android/hardware_buffer.h>
#include <dlfcn.h>
#define VK_USE_PLATFORM_ANDROID_KHR
#define VK_NO_PROTOTYPES
#include <vulkan/vulkan.h>
#include "Unity/IUnityGraphicsVulkan.h"
#include <algorithm>
#include <atomic>
#include <cstring>
#include <vector>

namespace
{
IUnityGraphicsVulkan* unityVulkan = nullptr;
PFN_vkGetInstanceProcAddr originalGetProc = nullptr;
PFN_vkCreateInstance originalCreateInstance = nullptr;
PFN_vkCreateDevice originalCreateDevice = nullptr;
PFN_vkEnumerateDeviceExtensionProperties enumerateExtensions = nullptr;
PFN_vkGetPhysicalDeviceProperties getProperties = nullptr;
std::uint32_t instanceVersion = VK_API_VERSION_1_0;
VkDevice enabledDevice = VK_NULL_HANDLE;

VKAPI_ATTR VkResult VKAPI_CALL CreateInstance(const VkInstanceCreateInfo* info, const VkAllocationCallbacks* allocator, VkInstance* instance)
{
    const auto result = originalCreateInstance(info, allocator, instance);
    if (result == VK_SUCCESS) instanceVersion = info->pApplicationInfo && info->pApplicationInfo->apiVersion ? info->pApplicationInfo->apiVersion : VK_API_VERSION_1_0;
    return result;
}
VKAPI_ATTR VkResult VKAPI_CALL CreateDevice(VkPhysicalDevice physical, const VkDeviceCreateInfo* info, const VkAllocationCallbacks* allocator, VkDevice* device)
{
    enabledDevice = VK_NULL_HANDLE;
    std::uint32_t count = 0;
    VkPhysicalDeviceProperties properties{};
    if (getProperties) getProperties(physical, &properties);
    if (!enumerateExtensions || instanceVersion < VK_API_VERSION_1_1 || properties.apiVersion < VK_API_VERSION_1_1 ||
        enumerateExtensions(physical, nullptr, &count, nullptr) != VK_SUCCESS)
        return originalCreateDevice(physical, info, allocator, device);
    std::vector<VkExtensionProperties> available(count);
    if (enumerateExtensions(physical, nullptr, &count, available.data()) != VK_SUCCESS)
        return originalCreateDevice(physical, info, allocator, device);
    const char* required[]{VK_ANDROID_EXTERNAL_MEMORY_ANDROID_HARDWARE_BUFFER_EXTENSION_NAME, VK_EXT_QUEUE_FAMILY_FOREIGN_EXTENSION_NAME};
    std::vector<const char*> names;
    if (info->enabledExtensionCount)
        names.assign(info->ppEnabledExtensionNames, info->ppEnabledExtensionNames + info->enabledExtensionCount);
    for (const char* extension : required)
    {
        if (std::none_of(available.begin(), available.end(), [extension](const auto& item) { return std::strcmp(item.extensionName, extension) == 0; }))
            return originalCreateDevice(physical, info, allocator, device);
        if (std::none_of(names.begin(), names.end(), [extension](const char* item) { return std::strcmp(item, extension) == 0; })) names.push_back(extension);
    }
    VkDeviceCreateInfo augmented = *info;
    augmented.enabledExtensionCount = static_cast<std::uint32_t>(names.size()); augmented.ppEnabledExtensionNames = names.data();
    const auto result = originalCreateDevice(physical, &augmented, allocator, device);
    if (result == VK_SUCCESS) { enabledDevice = *device; return result; }
    return originalCreateDevice(physical, info, allocator, device);
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL GetProc(VkInstance instance, const char* name)
{
    auto original = originalGetProc(instance, name);
    if (!original) return nullptr;
    if (std::strcmp(name, "vkCreateInstance") == 0)
    { originalCreateInstance = reinterpret_cast<PFN_vkCreateInstance>(original); return reinterpret_cast<PFN_vkVoidFunction>(CreateInstance); }
    if (instance && std::strcmp(name, "vkCreateDevice") == 0)
    {
        originalCreateDevice = reinterpret_cast<PFN_vkCreateDevice>(original);
        enumerateExtensions = reinterpret_cast<PFN_vkEnumerateDeviceExtensionProperties>(originalGetProc(instance, "vkEnumerateDeviceExtensionProperties"));
        getProperties = reinterpret_cast<PFN_vkGetPhysicalDeviceProperties>(originalGetProc(instance, "vkGetPhysicalDeviceProperties"));
        return reinterpret_cast<PFN_vkVoidFunction>(CreateDevice);
    }
    return original;
}
PFN_vkGetInstanceProcAddr UNITY_INTERFACE_API Intercept(PFN_vkGetInstanceProcAddr next, void*)
{ originalGetProc = next; return GetProc; }

struct SavedEgl
{
    EGLDisplay display = eglGetCurrentDisplay();
    EGLContext context = eglGetCurrentContext();
    EGLSurface draw = eglGetCurrentSurface(EGL_DRAW), read = eglGetCurrentSurface(EGL_READ);
    bool Restore(EGLDisplay fallback) const
    {
        if (display != EGL_NO_DISPLAY) return eglMakeCurrent(display, draw, read, context) == EGL_TRUE;
        return eglMakeCurrent(fallback, EGL_NO_SURFACE, EGL_NO_SURFACE, EGL_NO_CONTEXT) == EGL_TRUE;
    }
};

class AndroidBackend final : public PortableGpuBackend
{
    struct Surface
    {
        unsigned width = 0, height = 0;
        GLuint texture = 0, framebuffer = 0;
        AHardwareBuffer* buffer = nullptr;
        EGLImageKHR image = EGL_NO_IMAGE_KHR;
        VkImage vkImage = VK_NULL_HANDLE;
        VkDeviceMemory memory = VK_NULL_HANDLE;
        bool registered = false, acquired = false;
        void* Native(bool vulkan) { return vulkan ? &vkImage : reinterpret_cast<void*>(static_cast<std::uintptr_t>(texture)); }
    };

public:
    bool Initialize(IUnityInterfaces*, UnityGfxRenderer renderer) override
    {
        vulkan = renderer == kUnityGfxRendererVulkan;
        if (!vulkan && renderer != kUnityGfxRendererOpenGLES30) return Fail("Android GPU output requires OpenGL ES 3 or Vulkan");
        SavedEgl previous;
        display = vulkan ? eglGetDisplay(EGL_DEFAULT_DISPLAY) : previous.display;
        if (display == EGL_NO_DISPLAY) return Fail("No compatible EGL display");
        if (vulkan && !eglInitialize(display, nullptr, nullptr)) return Fail("Cannot initialize EGL producer display");
        EGLConfig config = nullptr;
        EGLint count = 0;
        EGLint configId = 0;
        if (!vulkan && !eglQueryContext(display, previous.context, EGL_CONFIG_ID, &configId)) return Fail("Cannot inspect Unity EGL context");
        const EGLint sharedAttributes[]{EGL_CONFIG_ID, configId, EGL_NONE};
        const EGLint independentAttributes[]{EGL_RENDERABLE_TYPE, EGL_OPENGL_ES3_BIT_KHR, EGL_SURFACE_TYPE, EGL_PBUFFER_BIT,
            EGL_RED_SIZE, 8, EGL_GREEN_SIZE, 8, EGL_BLUE_SIZE, 8, EGL_ALPHA_SIZE, 8, EGL_NONE};
        if (!eglChooseConfig(display, vulkan ? independentAttributes : sharedAttributes, &config, 1, &count) || count != 1)
            return Fail("No compatible EGL configuration");
        const EGLint contextAttributes[]{EGL_CONTEXT_CLIENT_VERSION, 3, EGL_NONE};
        const EGLint surfaceAttributes[]{EGL_WIDTH, 1, EGL_HEIGHT, 1, EGL_NONE};
        eglBindAPI(EGL_OPENGL_ES_API);
        context = eglCreateContext(display, config, vulkan ? EGL_NO_CONTEXT : previous.context, contextAttributes);
        pbuffer = eglCreatePbufferSurface(display, config, surfaceAttributes);
        if (context == EGL_NO_CONTEXT || pbuffer == EGL_NO_SURFACE) return Fail("Cannot create shared EGL producer context");
        if (vulkan && !InitializeVulkan()) return false;
        if (!MakeCurrent(true)) return false;
        // Probe the entire allocation/import path before declaring GPU support.
        const bool created = Resize(16, 16) && FrameComplete();
        const bool unbound = MakeCurrent(false);
        const bool restored = previous.Restore(display);
        if (!unbound || !restored) return Fail("Cannot restore Unity's EGL context after the video probe");
        if (!created || !Pump()) return false;
        current = nullptr;
        return true;
    }
    int Capability() const override { return vulkan ? 5 : 3; }
    int Engine() const override { return 2; }
    bool MakeCurrent(bool enter) override
    {
        if (enter)
        {
            if (stopped) return false;
            if (!eglMakeCurrent(display, pbuffer, pbuffer, context)) return Fail("Cannot bind EGL producer context");
            if (current) glBindFramebuffer(GL_FRAMEBUFFER, current->framebuffer);
            return true;
        }
        return eglMakeCurrent(display, EGL_NO_SURFACE, EGL_NO_SURFACE, EGL_NO_CONTEXT) == EGL_TRUE;
    }
    bool Resize(unsigned width, unsigned height) override
    {
        if (!width || !height) return false;
        GLint maximum = 0; glGetIntegerv(GL_MAX_TEXTURE_SIZE, &maximum);
        if (width > static_cast<unsigned>(maximum) || height > static_cast<unsigned>(maximum)) return Fail("Video exceeds GLES texture limit");
        for (auto& item : surfaces)
            if (item->width == width && item->height == height)
            { current = item.get(); glBindFramebuffer(GL_FRAMEBUFFER, current->framebuffer); return true; }
        if (surfaces.size() >= 9) return Fail("Too many retained video resolutions; use CPU output");
        auto surface = std::make_unique<Surface>(); surface->width = width; surface->height = height;
        glGenTextures(1, &surface->texture); glBindTexture(GL_TEXTURE_2D, surface->texture);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR); glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE); glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
        if (vulkan)
        {
            AHardwareBuffer_Desc desc{}; desc.width = width; desc.height = height; desc.layers = 1;
            desc.format = AHARDWAREBUFFER_FORMAT_R8G8B8A8_UNORM;
            desc.usage = AHARDWAREBUFFER_USAGE_GPU_COLOR_OUTPUT | AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE;
            if (allocateBuffer(&desc, &surface->buffer) != 0) { DestroyGl(*surface); return Fail("AHardwareBuffer allocation failed"); }
            const EGLint attributes[]{EGL_IMAGE_PRESERVED_KHR, EGL_TRUE, EGL_NONE};
            surface->image = createImage(display, EGL_NO_CONTEXT, EGL_NATIVE_BUFFER_ANDROID, nativeClientBuffer(surface->buffer), attributes);
            if (surface->image == EGL_NO_IMAGE_KHR) { DestroyGl(*surface); return Fail("AHardwareBuffer EGLImage import failed"); }
            imageTarget(GL_TEXTURE_2D, surface->image);
        }
        else glTexStorage2D(GL_TEXTURE_2D, 1, GL_RGBA8, static_cast<GLsizei>(width), static_cast<GLsizei>(height));
        glGenFramebuffers(1, &surface->framebuffer); glBindFramebuffer(GL_FRAMEBUFFER, surface->framebuffer);
        glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, surface->texture, 0);
        if (glCheckFramebufferStatus(GL_FRAMEBUFFER) != GL_FRAMEBUFFER_COMPLETE || glGetError() != GL_NO_ERROR)
        { DestroyGl(*surface); return Fail("Video EGL framebuffer is incomplete"); }
        surface->registered = !vulkan;
        current = surface.get(); surfaces.push_back(std::move(surface));
        return true;
    }
    void* GetProcAddress(const char* name) override
    {
        auto address = reinterpret_cast<void*>(eglGetProcAddress(name));
        if (!address)
        {
            if (!glesLibrary) glesLibrary = dlopen("libGLESv3.so", RTLD_NOW | RTLD_LOCAL);
            if (glesLibrary) address = dlsym(glesLibrary, name);
        }
        return address;
    }
    bool FrameComplete() override { glFinish(); return glGetError() == GL_NO_ERROR; }
    bool Pump() override
    {
        if (!vulkan) return true;
        for (auto& surface : surfaces)
            if (!surface->registered && !Import(*surface)) return false;
        return true;
    }
    PortableTextureInfo TextureInfo() const override
    { return current && current->registered ? PortableTextureInfo{current->Native(vulkan), current->width, current->height} : PortableTextureInfo{}; }
    bool Begin() override
    {
        if (!vulkan) return true;
        for (auto& surface : surfaces)
            if (surface->registered && !Transition(*surface, true)) return false;
        return true;
    }
    bool End() override
    {
        if (!vulkan) { glFinish(); return glGetError() == GL_NO_ERROR; }
        bool success = true;
        for (auto& surface : surfaces)
            if (surface->acquired) success = Transition(*surface, false) && success;
        return success;
    }
    void StopProducer() override { stopped = true; }
    void Retire() override
    {
        SavedEgl previous;
        if (vulkan && instance.device && vkQueueWaitIdle) vkQueueWaitIdle(instance.graphicsQueue);
        const bool bound = context != EGL_NO_CONTEXT && eglMakeCurrent(display, pbuffer, pbuffer, context);
        for (auto& surface : surfaces)
        {
            if (surface->vkImage) vkDestroyImage(instance.device, surface->vkImage, nullptr);
            if (surface->memory) vkFreeMemory(instance.device, surface->memory, nullptr);
            if (bound) DestroyGl(*surface);
            else
            {
                // A lost EGL context owns the GL names until its destruction,
                // but the external image and AHardwareBuffer references can
                // still be released without making that context current.
                if (surface->image != EGL_NO_IMAGE_KHR && destroyImage) destroyImage(display, surface->image);
                if (surface->buffer && releaseBuffer) releaseBuffer(surface->buffer);
                surface->image = EGL_NO_IMAGE_KHR; surface->buffer = nullptr;
            }
        }
        surfaces.clear(); current = nullptr;
        if (bound) { glFinish(); previous.Restore(display); }
        if (context != EGL_NO_CONTEXT) eglDestroyContext(display, context);
        if (pbuffer != EGL_NO_SURFACE) eglDestroySurface(display, pbuffer);
        context = EGL_NO_CONTEXT; pbuffer = EGL_NO_SURFACE;
        if (pool) vkDestroyCommandPool(instance.device, pool, nullptr);
        pool = VK_NULL_HANDLE; commands = VK_NULL_HANDLE;
        if (androidLibrary) dlclose(androidLibrary);
        if (glesLibrary) dlclose(glesLibrary);
        androidLibrary = glesLibrary = nullptr;
        // Do not eglTerminate the default display: Unity or another plugin can
        // share it, including a later GLES player in the same process.
    }
    const char* LastError() const override { return error ? error : ""; }

private:
    bool InitializeVulkan()
    {
        if (!unityVulkan) return Fail("Unity Vulkan interface is unavailable");
        instance = unityVulkan->Instance();
        if (!instance.device || instance.device != enabledDevice) return Fail("Android Vulkan interop extensions were not enabled; restart after preloading plugin");
        androidLibrary = dlopen("libandroid.so", RTLD_NOW | RTLD_LOCAL);
        if (!androidLibrary) return Fail("Android native buffer library is unavailable");
        allocateBuffer = reinterpret_cast<decltype(allocateBuffer)>(dlsym(androidLibrary, "AHardwareBuffer_allocate"));
        releaseBuffer = reinterpret_cast<decltype(releaseBuffer)>(dlsym(androidLibrary, "AHardwareBuffer_release"));
        nativeClientBuffer = reinterpret_cast<PFNEGLGETNATIVECLIENTBUFFERANDROIDPROC>(eglGetProcAddress("eglGetNativeClientBufferANDROID"));
        createImage = reinterpret_cast<PFNEGLCREATEIMAGEKHRPROC>(eglGetProcAddress("eglCreateImageKHR"));
        destroyImage = reinterpret_cast<PFNEGLDESTROYIMAGEKHRPROC>(eglGetProcAddress("eglDestroyImageKHR"));
        imageTarget = reinterpret_cast<PFNGLEGLIMAGETARGETTEXTURE2DOESPROC>(eglGetProcAddress("glEGLImageTargetTexture2DOES"));
        if (!allocateBuffer || !releaseBuffer || !nativeClientBuffer || !createImage || !destroyImage || !imageTarget)
            return Fail("AHardwareBuffer EGL interop requires Android 8 or newer and compatible drivers");
        auto getDeviceProc = reinterpret_cast<PFN_vkGetDeviceProcAddr>(instance.getInstanceProcAddr(instance.instance, "vkGetDeviceProcAddr"));
        if (!getDeviceProc) return false;
#define LOAD(name) name = reinterpret_cast<PFN_##name>(getDeviceProc(instance.device, #name)); if (!name) return Fail(#name " unavailable")
        LOAD(vkGetAndroidHardwareBufferPropertiesANDROID); LOAD(vkCreateImage); LOAD(vkDestroyImage);
        LOAD(vkAllocateMemory); LOAD(vkFreeMemory); LOAD(vkBindImageMemory);
        LOAD(vkCreateCommandPool); LOAD(vkDestroyCommandPool); LOAD(vkAllocateCommandBuffers);
        LOAD(vkResetCommandPool); LOAD(vkBeginCommandBuffer); LOAD(vkEndCommandBuffer);
        LOAD(vkCmdPipelineBarrier); LOAD(vkQueueSubmit); LOAD(vkQueueWaitIdle);
#undef LOAD
        VkCommandPoolCreateInfo description{}; description.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
        description.queueFamilyIndex = instance.queueFamilyIndex;
        if (vkCreateCommandPool(instance.device, &description, nullptr, &pool) != VK_SUCCESS) return Fail("Vulkan command pool allocation failed");
        VkCommandBufferAllocateInfo allocation{}; allocation.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
        allocation.commandPool = pool; allocation.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY; allocation.commandBufferCount = 1;
        return vkAllocateCommandBuffers(instance.device, &allocation, &commands) == VK_SUCCESS;
    }
    bool Import(Surface& surface)
    {
        VkAndroidHardwareBufferFormatPropertiesANDROID format{};
        format.sType = VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_FORMAT_PROPERTIES_ANDROID;
        VkAndroidHardwareBufferPropertiesANDROID properties{};
        properties.sType = VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_PROPERTIES_ANDROID; properties.pNext = &format;
        if (vkGetAndroidHardwareBufferPropertiesANDROID(instance.device, surface.buffer, &properties) != VK_SUCCESS ||
            format.format != VK_FORMAT_R8G8B8A8_UNORM || !(format.formatFeatures & VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT))
            return Fail("Vulkan cannot sample this EGL hardware buffer as RGBA8");
        VkExternalMemoryImageCreateInfo external{}; external.sType = VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO;
        external.handleTypes = VK_EXTERNAL_MEMORY_HANDLE_TYPE_ANDROID_HARDWARE_BUFFER_BIT_ANDROID;
        VkImageCreateInfo image{}; image.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO; image.pNext = &external;
        image.imageType = VK_IMAGE_TYPE_2D; image.format = VK_FORMAT_R8G8B8A8_UNORM;
        image.extent = {surface.width, surface.height, 1}; image.mipLevels = image.arrayLayers = 1;
        image.samples = VK_SAMPLE_COUNT_1_BIT; image.tiling = VK_IMAGE_TILING_OPTIMAL;
        image.usage = VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
        image.sharingMode = VK_SHARING_MODE_EXCLUSIVE; image.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        if (vkCreateImage(instance.device, &image, nullptr, &surface.vkImage) != VK_SUCCESS) return Fail("AHardwareBuffer Vulkan image creation failed");
        std::uint32_t memoryType = 0;
        while (memoryType < 32 && !(properties.memoryTypeBits & (1u << memoryType))) ++memoryType;
        if (memoryType == 32) return Fail("AHardwareBuffer has no compatible Vulkan memory type");
        VkImportAndroidHardwareBufferInfoANDROID imported{};
        imported.sType = VK_STRUCTURE_TYPE_IMPORT_ANDROID_HARDWARE_BUFFER_INFO_ANDROID; imported.buffer = surface.buffer;
        VkMemoryDedicatedAllocateInfo dedicated{}; dedicated.sType = VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO;
        dedicated.pNext = &imported; dedicated.image = surface.vkImage;
        VkMemoryAllocateInfo allocation{}; allocation.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        allocation.pNext = &dedicated; allocation.allocationSize = properties.allocationSize; allocation.memoryTypeIndex = memoryType;
        if (vkAllocateMemory(instance.device, &allocation, nullptr, &surface.memory) != VK_SUCCESS ||
            vkBindImageMemory(instance.device, surface.vkImage, surface.memory, 0) != VK_SUCCESS) return Fail("AHardwareBuffer Vulkan import failed");
        surface.registered = true;
        return true;
    }
    bool Transition(Surface& surface, bool acquire)
    {
        if (surface.acquired == acquire) return true;
        if (vkResetCommandPool(instance.device, pool, 0) != VK_SUCCESS) return false;
        VkCommandBufferBeginInfo begin{}; begin.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO; begin.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
        if (vkBeginCommandBuffer(commands, &begin) != VK_SUCCESS) return false;
        VkImageMemoryBarrier barrier{}; barrier.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        barrier.srcAccessMask = acquire ? 0 : VK_ACCESS_SHADER_READ_BIT;
        barrier.dstAccessMask = acquire ? VK_ACCESS_SHADER_READ_BIT : 0;
        barrier.oldLayout = acquire ? VK_IMAGE_LAYOUT_GENERAL : VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        barrier.newLayout = acquire ? VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL : VK_IMAGE_LAYOUT_GENERAL;
        barrier.srcQueueFamilyIndex = acquire ? VK_QUEUE_FAMILY_FOREIGN_EXT : instance.queueFamilyIndex;
        barrier.dstQueueFamilyIndex = acquire ? instance.queueFamilyIndex : VK_QUEUE_FAMILY_FOREIGN_EXT;
        barrier.image = surface.vkImage; barrier.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
        vkCmdPipelineBarrier(commands, VK_PIPELINE_STAGE_ALL_COMMANDS_BIT, VK_PIPELINE_STAGE_ALL_COMMANDS_BIT, 0, 0, nullptr, 0, nullptr, 1, &barrier);
        if (vkEndCommandBuffer(commands) != VK_SUCCESS) return false;
        VkSubmitInfo submit{}; submit.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO; submit.commandBufferCount = 1; submit.pCommandBuffers = &commands;
        if (vkQueueSubmit(instance.graphicsQueue, 1, &submit, VK_NULL_HANDLE) != VK_SUCCESS || vkQueueWaitIdle(instance.graphicsQueue) != VK_SUCCESS)
            return Fail("Vulkan external-image ownership transfer failed");
        surface.acquired = acquire;
        return true;
    }
    void DestroyGl(Surface& surface)
    {
        if (surface.framebuffer) glDeleteFramebuffers(1, &surface.framebuffer);
        if (surface.texture) glDeleteTextures(1, &surface.texture);
        if (surface.image != EGL_NO_IMAGE_KHR && destroyImage) destroyImage(display, surface.image);
        if (surface.buffer && releaseBuffer) releaseBuffer(surface.buffer);
        surface.framebuffer = surface.texture = 0; surface.image = EGL_NO_IMAGE_KHR; surface.buffer = nullptr;
    }
    bool Fail(const char* message) { error = message; return false; }

    bool vulkan = false;
    std::atomic<bool> stopped{false};
    const char* error = nullptr;
    EGLDisplay display = EGL_NO_DISPLAY;
    EGLContext context = EGL_NO_CONTEXT;
    EGLSurface pbuffer = EGL_NO_SURFACE;
    std::vector<std::unique_ptr<Surface>> surfaces;
    Surface* current = nullptr;
    void* androidLibrary = nullptr;
    void* glesLibrary = nullptr;
    int (*allocateBuffer)(const AHardwareBuffer_Desc*, AHardwareBuffer**) = nullptr;
    void (*releaseBuffer)(AHardwareBuffer*) = nullptr;
    PFNEGLGETNATIVECLIENTBUFFERANDROIDPROC nativeClientBuffer = nullptr;
    PFNEGLCREATEIMAGEKHRPROC createImage = nullptr;
    PFNEGLDESTROYIMAGEKHRPROC destroyImage = nullptr;
    PFNGLEGLIMAGETARGETTEXTURE2DOESPROC imageTarget = nullptr;
    UnityVulkanInstance instance{};
    VkCommandPool pool = VK_NULL_HANDLE;
    VkCommandBuffer commands = VK_NULL_HANDLE;
#define MEMBER(name) PFN_##name name = nullptr
    MEMBER(vkGetAndroidHardwareBufferPropertiesANDROID); MEMBER(vkCreateImage); MEMBER(vkDestroyImage);
    MEMBER(vkAllocateMemory); MEMBER(vkFreeMemory); MEMBER(vkBindImageMemory);
    MEMBER(vkCreateCommandPool); MEMBER(vkDestroyCommandPool); MEMBER(vkAllocateCommandBuffers);
    MEMBER(vkResetCommandPool); MEMBER(vkBeginCommandBuffer); MEMBER(vkEndCommandBuffer);
    MEMBER(vkCmdPipelineBarrier); MEMBER(vkQueueSubmit); MEMBER(vkQueueWaitIdle);
#undef MEMBER
};
}

void InstallPortableGpuHooks(IUnityInterfaces* interfaces)
{
    if (!interfaces) return;
    unityVulkan = interfaces->Get<IUnityGraphicsVulkan>();
    if (auto* v2 = interfaces->Get<IUnityGraphicsVulkanV2>()) v2->AddInterceptInitialization(Intercept, nullptr, 100);
    else if (unityVulkan) unityVulkan->InterceptInitialization(Intercept, nullptr);
}
void ConfigurePortableGpuEvents(IUnityInterfaces*, UnityGfxRenderer renderer, int eventBase)
{
    if (renderer != kUnityGfxRendererVulkan || !unityVulkan || !unityVulkan->Instance().device) return;
    UnityVulkanPluginEventConfig config{}; config.renderPassPrecondition = kUnityVulkanRenderPass_EnsureOutside;
    config.graphicsQueueAccess = kUnityVulkanGraphicsQueueAccess_Allow;
    config.flags = kUnityVulkanEventConfigFlag_FlushCommandBuffers | kUnityVulkanEventConfigFlag_SyncWorkerThreads;
    for (int event = 0; event < 4; ++event) unityVulkan->ConfigureEvent(eventBase + event, &config);
}
std::unique_ptr<PortableGpuBackend> CreatePortableGpuBackend(UnityGfxRenderer renderer)
{
    if (renderer != kUnityGfxRendererOpenGLES30 && renderer != kUnityGfxRendererVulkan) return nullptr;
    return std::make_unique<AndroidBackend>();
}
