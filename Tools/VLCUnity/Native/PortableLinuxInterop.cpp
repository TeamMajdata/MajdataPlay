// Linux OpenGL texture sharing and EGL DMA-BUF -> Vulkan image import.
// GPU ownership is transferred only in render events with Unity queue access.
#include "PortableGpuBackend.h"
#define GL_GLEXT_PROTOTYPES
#include <GL/gl.h>
#include <GL/glext.h>
#include <GL/glx.h>
#include <GL/glxext.h>
#include <EGL/egl.h>
#include <EGL/eglext.h>
#define VK_NO_PROTOTYPES
#include "Unity/IUnityGraphicsVulkan.h"
#include <algorithm>
#include <atomic>
#include <cstring>
#include <string>
#include <mutex>
#include <vector>
#include <sys/stat.h>
#include <sys/sysmacros.h>
#include <unistd.h>

namespace
{
PFN_vkGetInstanceProcAddr originalGetProc = nullptr;
PFN_vkCreateInstance originalCreateInstance = nullptr;
PFN_vkCreateDevice originalCreateDevice = nullptr;
PFN_vkEnumerateDeviceExtensionProperties enumerateExtensions = nullptr;
PFN_vkGetPhysicalDeviceProperties getProperties = nullptr;
uint32_t instanceVersion = VK_API_VERSION_1_0;
VkDevice enabledDevice = VK_NULL_HANDLE;
std::mutex glxErrorScope;
std::atomic<Display*> trappedDisplay{nullptr};
std::atomic<int> trappedError{0};
std::atomic<XErrorHandler> previousErrorHandler{nullptr};
int GlxError(Display* display, XErrorEvent* error)
{
    if (display == trappedDisplay.load()) { trappedError = error->error_code; return 0; }
    const auto previous = previousErrorHandler.load();
    return previous ? previous(display, error) : 0;
}
constexpr const char* requiredExtensions[] = {
    VK_KHR_EXTERNAL_MEMORY_FD_EXTENSION_NAME,
    VK_EXT_EXTERNAL_MEMORY_DMA_BUF_EXTENSION_NAME,
    VK_EXT_IMAGE_DRM_FORMAT_MODIFIER_EXTENSION_NAME,
    VK_KHR_IMAGE_FORMAT_LIST_EXTENSION_NAME,
    VK_EXT_QUEUE_FAMILY_FOREIGN_EXTENSION_NAME,
    VK_EXT_PHYSICAL_DEVICE_DRM_EXTENSION_NAME
};

VKAPI_ATTR VkResult VKAPI_CALL CreateInstance(const VkInstanceCreateInfo* info,
    const VkAllocationCallbacks* allocator, VkInstance* instance)
{
    const auto status = originalCreateInstance(info, allocator, instance);
    if (status == VK_SUCCESS)
        instanceVersion = info->pApplicationInfo && info->pApplicationInfo->apiVersion ?
            info->pApplicationInfo->apiVersion : VK_API_VERSION_1_0;
    return status;
}

VKAPI_ATTR VkResult VKAPI_CALL CreateDevice(VkPhysicalDevice physical,
    const VkDeviceCreateInfo* info, const VkAllocationCallbacks* allocator, VkDevice* result)
{
    enabledDevice = VK_NULL_HANDLE;
    auto enumerate = enumerateExtensions;
    VkPhysicalDeviceProperties properties{};
    if (getProperties) getProperties(physical, &properties);
    std::vector<VkExtensionProperties> available;
    uint32_t count = 0;
    bool supported = enumerate && instanceVersion >= VK_API_VERSION_1_1 && properties.apiVersion >= VK_API_VERSION_1_1 &&
        enumerate(physical, nullptr, &count, nullptr) == VK_SUCCESS;
    if (supported) {
        available.resize(count);
        supported = enumerate(physical, nullptr, &count, available.data()) == VK_SUCCESS;
    }
    for (const auto* name : requiredExtensions)
        supported = supported && std::any_of(available.begin(), available.end(),
            [name](const auto& extension) { return std::strcmp(name, extension.extensionName) == 0; });
    VkDeviceCreateInfo request = *info;
    std::vector<const char*> names;
    if (info->enabledExtensionCount)
        names.assign(info->ppEnabledExtensionNames, info->ppEnabledExtensionNames + info->enabledExtensionCount);
    if (supported) {
        for (const auto* name : requiredExtensions)
            if (std::none_of(names.begin(), names.end(), [name](const char* enabled) { return std::strcmp(name, enabled) == 0; }))
                names.push_back(name);
        request.enabledExtensionCount = static_cast<uint32_t>(names.size());
        request.ppEnabledExtensionNames = names.data();
    }
    VkResult status = originalCreateDevice(physical, &request, allocator, result);
    if (status == VK_SUCCESS && supported) enabledDevice = *result;
    // These optional extensions must never prevent Unity creating its device.
    // An incompatible driver keeps the original device and uses CPU fallback.
    if (status != VK_SUCCESS && supported) status = originalCreateDevice(physical, info, allocator, result);
    return status;
}

VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL GetProc(VkInstance instance, const char* name)
{
    auto function = originalGetProc(instance, name);
    if (function && std::strcmp(name, "vkCreateInstance") == 0) {
        originalCreateInstance = reinterpret_cast<PFN_vkCreateInstance>(function);
        return reinterpret_cast<PFN_vkVoidFunction>(CreateInstance);
    }
    if (function && instance && std::strcmp(name, "vkCreateDevice") == 0) {
        originalCreateDevice = reinterpret_cast<PFN_vkCreateDevice>(function);
        enumerateExtensions = reinterpret_cast<PFN_vkEnumerateDeviceExtensionProperties>(
            originalGetProc(instance, "vkEnumerateDeviceExtensionProperties"));
        getProperties = reinterpret_cast<PFN_vkGetPhysicalDeviceProperties>(
            originalGetProc(instance, "vkGetPhysicalDeviceProperties"));
        return reinterpret_cast<PFN_vkVoidFunction>(CreateDevice);
    }
    return function;
}
PFN_vkGetInstanceProcAddr UNITY_INTERFACE_API Intercept(PFN_vkGetInstanceProcAddr function, void*)
{
    originalGetProc = function;
    return GetProc;
}

bool HasExtension(const char* extensions, const char* name)
{
    if (!extensions || !name || !*name) return false;
    const size_t length = std::strlen(name);
    const char* match = extensions;
    while ((match = std::strstr(match, name))) {
        if ((match == extensions || match[-1] == ' ') && (match[length] == ' ' || match[length] == '\0')) return true;
        match += length;
    }
    return false;
}

struct Surface
{
    unsigned width = 0, height = 0;
    GLuint texture = 0, framebuffer = 0;
    EGLImageKHR eglImage = EGL_NO_IMAGE_KHR;
    int fd = -1, planeCount = 0;
    EGLint strides[4]{}, offsets[4]{};
    EGLuint64KHR modifier = 0;
    VkImage image = VK_NULL_HANDLE;
    VkDeviceMemory memory = VK_NULL_HANDLE;
    bool imported = false, acquired = false;
};

class LinuxGpu final : public PortableGpuBackend
{
    bool vulkan = false;
    Display* xDisplay = nullptr;
    GLXContext glxContext = nullptr;
    GLXPbuffer glxSurface = 0;
    EGLDisplay eglDisplay = EGL_NO_DISPLAY;
    EGLContext eglContext = EGL_NO_CONTEXT;
    EGLSurface eglSurface = EGL_NO_SURFACE;
    EGLenum eglApi = EGL_OPENGL_API;
    IUnityGraphicsVulkan* graphics = nullptr;
    UnityVulkanInstance instance{};
    VkCommandPool pool = VK_NULL_HANDLE;
    VkCommandBuffer commands = VK_NULL_HANDLE;
    std::vector<std::unique_ptr<Surface>> surfaces;
    Surface* current = nullptr;
    std::string error;
    PFNEGLCREATEIMAGEKHRPROC createImage = nullptr;
    PFNEGLDESTROYIMAGEKHRPROC destroyImage = nullptr;
    PFNEGLEXPORTDMABUFIMAGEQUERYMESAPROC queryDma = nullptr;
    PFNEGLEXPORTDMABUFIMAGEMESAPROC exportDma = nullptr;
#define VK_FUNCTIONS(X) \
    X(vkCreateImage) X(vkDestroyImage) X(vkGetImageMemoryRequirements) X(vkAllocateMemory) \
    X(vkFreeMemory) X(vkBindImageMemory) X(vkGetMemoryFdPropertiesKHR) \
    X(vkCreateCommandPool) X(vkDestroyCommandPool) X(vkAllocateCommandBuffers) X(vkResetCommandPool) \
    X(vkBeginCommandBuffer) X(vkEndCommandBuffer) X(vkCmdPipelineBarrier) X(vkQueueSubmit) X(vkQueueWaitIdle)
#define DECLARE_FUNCTION(name) PFN_##name name = nullptr;
    VK_FUNCTIONS(DECLARE_FUNCTION)
#undef DECLARE_FUNCTION
    PFN_vkGetPhysicalDeviceImageFormatProperties2 imageProperties = nullptr;

    bool Fail(const char* message) { error = message; return false; }

    bool CreateEgl(EGLDisplay display, EGLContext share)
    {
        EGLint configId = 0, count = 0;
        EGLConfig config = nullptr;
        if (share != EGL_NO_CONTEXT) {
            if (!eglQueryContext(display, share, EGL_CONFIG_ID, &configId)) return Fail("Cannot query Unity's EGL configuration");
            const EGLint attributes[] = { EGL_CONFIG_ID, configId, EGL_NONE };
            if (!eglChooseConfig(display, attributes, &config, 1, &count) || count != 1) return false;
        } else {
            const EGLint attributes[] = { EGL_SURFACE_TYPE, EGL_PBUFFER_BIT, EGL_RENDERABLE_TYPE, EGL_OPENGL_BIT,
                EGL_RED_SIZE, 8, EGL_GREEN_SIZE, 8, EGL_BLUE_SIZE, 8, EGL_ALPHA_SIZE, 8, EGL_NONE };
            if (!eglChooseConfig(display, attributes, &config, 1, &count) || count != 1) return Fail("No EGL RGBA OpenGL configuration");
        }
        const EGLenum previousApi = eglQueryAPI();
        if (!eglBindAPI(eglApi)) return Fail("EGL cannot bind the requested client API");
        const EGLint desktop[] = { EGL_CONTEXT_MAJOR_VERSION_KHR, 3, EGL_CONTEXT_MINOR_VERSION_KHR, 3,
            EGL_CONTEXT_OPENGL_PROFILE_MASK_KHR, EGL_CONTEXT_OPENGL_CORE_PROFILE_BIT_KHR, EGL_NONE };
        const EGLint es[] = { EGL_CONTEXT_CLIENT_VERSION, 3, EGL_NONE };
        eglContext = eglCreateContext(display, config, share, eglApi == EGL_OPENGL_API ? desktop : es);
        eglBindAPI(previousApi);
        if (eglContext == EGL_NO_CONTEXT) return Fail("Cannot create a separate shared EGL context");
        const EGLint pbuffer[] = { EGL_WIDTH, 1, EGL_HEIGHT, 1, EGL_NONE };
        eglSurface = eglCreatePbufferSurface(display, config, pbuffer);
        if (eglSurface == EGL_NO_SURFACE) return Fail("Cannot create the EGL producer pbuffer");
        return true;
    }

    bool CreateGlx()
    {
        const auto unityContext = glXGetCurrentContext();
        xDisplay = glXGetCurrentDisplay();
        if (!unityContext || !xDisplay) return Fail("Unity has no current GLX/EGL context");
        int configId = 0, screen = 0, count = 0;
        if (glXQueryContext(xDisplay, unityContext, GLX_FBCONFIG_ID, &configId) != Success ||
            glXQueryContext(xDisplay, unityContext, GLX_SCREEN, &screen) != Success) return Fail("Cannot query the Unity GLX context");
        const int attributes[] = { GLX_FBCONFIG_ID, configId, None };
        auto configs = glXChooseFBConfig(xDisplay, screen, attributes, &count);
        if (!configs || count == 0) { if (configs) XFree(configs); return Fail("No matching shared GLX configuration"); }
        int drawableTypes = 0;
        glXGetFBConfigAttrib(xDisplay, configs[0], GLX_DRAWABLE_TYPE, &drawableTypes);
        if (!(drawableTypes & GLX_PBUFFER_BIT) || !HasExtension(glXQueryExtensionsString(xDisplay, screen), "GLX_ARB_create_context_profile")) {
            XFree(configs); return Fail("The Unity GLX configuration cannot share a core-profile pbuffer");
        }
        auto create = reinterpret_cast<PFNGLXCREATECONTEXTATTRIBSARBPROC>(glXGetProcAddressARB(
            reinterpret_cast<const GLubyte*>("glXCreateContextAttribsARB")));
        const int contextAttributes[] = { GLX_CONTEXT_MAJOR_VERSION_ARB, 3, GLX_CONTEXT_MINOR_VERSION_ARB, 2,
            GLX_CONTEXT_PROFILE_MASK_ARB, GLX_CONTEXT_CORE_PROFILE_BIT_ARB, None };
        const int pbuffer[] = { GLX_PBUFFER_WIDTH, 1, GLX_PBUFFER_HEIGHT, 1, None };
        // GLX allocation failures are asynchronous X errors; the default Xlib
        // handler would terminate Unity instead of allowing CPU fallback.
        // Exclude other users of this display and forward other displays' errors.
        {
            std::lock_guard<std::mutex> trap(glxErrorScope);
            XLockDisplay(xDisplay); XSync(xDisplay, False);
            trappedDisplay = xDisplay; trappedError = 0;
            previousErrorHandler = XSetErrorHandler(GlxError);
            if (create) glxContext = create(xDisplay, configs[0], unityContext, True, contextAttributes);
            XSync(xDisplay, False);
            if (glxContext && !trappedError) glxSurface = glXCreatePbuffer(xDisplay, configs[0], pbuffer);
            XSync(xDisplay, False);
            if (trappedError) {
                if (glxSurface) glXDestroyPbuffer(xDisplay, glxSurface);
                if (glxContext) glXDestroyContext(xDisplay, glxContext);
                XSync(xDisplay, False); glxSurface = 0; glxContext = nullptr;
            }
            XSetErrorHandler(previousErrorHandler.load()); trappedDisplay = nullptr;
            XUnlockDisplay(xDisplay);
        }
        XFree(configs);
        return (glxContext && glxSurface) || Fail("Cannot create shared GLX producer resources");
    }

    bool CreateVulkanProducer()
    {
        auto properties = reinterpret_cast<PFN_vkGetPhysicalDeviceProperties2>(
            instance.getInstanceProcAddr(instance.instance, "vkGetPhysicalDeviceProperties2"));
        if (!properties) return Fail("Vulkan 1.1 device identity queries are unavailable");
        VkPhysicalDeviceDrmPropertiesEXT drm{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DRM_PROPERTIES_EXT};
        VkPhysicalDeviceProperties2 physical{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PROPERTIES_2};
        physical.pNext = &drm;
        properties(instance.physicalDevice, &physical);
        if (physical.properties.apiVersion < VK_API_VERSION_1_1 || !drm.hasRender)
            return Fail("Vulkan DMA-BUF requires a verified DRM render device");
        auto enumerate = reinterpret_cast<PFNEGLQUERYDEVICESEXTPROC>(eglGetProcAddress("eglQueryDevicesEXT"));
        auto query = reinterpret_cast<PFNEGLQUERYDEVICESTRINGEXTPROC>(eglGetProcAddress("eglQueryDeviceStringEXT"));
        auto getDisplay = reinterpret_cast<PFNEGLGETPLATFORMDISPLAYEXTPROC>(eglGetProcAddress("eglGetPlatformDisplayEXT"));
        if (!enumerate || !query || !getDisplay) return Fail("EGL device enumeration is unavailable");
        EGLint count = 0;
        EGLDeviceEXT devices[32]{};
        if (!enumerate(32, devices, &count)) return Fail("Cannot enumerate EGL devices");
        for (int i = 0; i < count; ++i) {
            const char* node = query(devices[i], EGL_DRM_RENDER_NODE_FILE_EXT);
            struct stat status{};
            if (!node || stat(node, &status) != 0 || !S_ISCHR(status.st_mode) ||
                major(status.st_rdev) != static_cast<unsigned>(drm.renderMajor) ||
                minor(status.st_rdev) != static_cast<unsigned>(drm.renderMinor)) continue;
            eglDisplay = getDisplay(EGL_PLATFORM_DEVICE_EXT, devices[i], nullptr);
            if (eglDisplay != EGL_NO_DISPLAY && eglInitialize(eglDisplay, nullptr, nullptr)) {
                break;
            }
            eglDisplay = EGL_NO_DISPLAY;
        }
        if (eglDisplay == EGL_NO_DISPLAY) return Fail("No EGL device matches Unity's Vulkan GPU");
        const char* extensions = eglQueryString(eglDisplay, EGL_EXTENSIONS);
        if (!HasExtension(extensions, "EGL_MESA_image_dma_buf_export") || !HasExtension(extensions, "EGL_KHR_gl_texture_2D_image"))
            return Fail("EGL cannot export rendered RGBA textures as DMA-BUF");
        createImage = reinterpret_cast<PFNEGLCREATEIMAGEKHRPROC>(eglGetProcAddress("eglCreateImageKHR"));
        destroyImage = reinterpret_cast<PFNEGLDESTROYIMAGEKHRPROC>(eglGetProcAddress("eglDestroyImageKHR"));
        queryDma = reinterpret_cast<PFNEGLEXPORTDMABUFIMAGEQUERYMESAPROC>(eglGetProcAddress("eglExportDMABUFImageQueryMESA"));
        exportDma = reinterpret_cast<PFNEGLEXPORTDMABUFIMAGEMESAPROC>(eglGetProcAddress("eglExportDMABUFImageMESA"));
        if (!createImage || !destroyImage || !queryDma || !exportDma) return Fail("EGL DMA-BUF entry points are unavailable");
        return CreateEgl(eglDisplay, EGL_NO_CONTEXT);
    }

    bool InitializeVulkan(IUnityInterfaces* interfaces)
    {
        graphics = interfaces->Get<IUnityGraphicsVulkan>();
        if (!graphics || !enabledDevice) return Fail("Restart Unity with the Linux Vulkan bridge preloaded to enable DMA-BUF extensions");
        instance = graphics->Instance();
        if (instance.device != enabledDevice) return Fail("The current Vulkan device has no external-image extensions");
        auto deviceProc = reinterpret_cast<PFN_vkGetDeviceProcAddr>(instance.getInstanceProcAddr(instance.instance, "vkGetDeviceProcAddr"));
        if (!deviceProc) return false;
#define LOAD_FUNCTION(name) name = reinterpret_cast<PFN_##name>(deviceProc(instance.device, #name)); if (!name) return Fail(#name " unavailable");
        VK_FUNCTIONS(LOAD_FUNCTION)
#undef LOAD_FUNCTION
        imageProperties = reinterpret_cast<PFN_vkGetPhysicalDeviceImageFormatProperties2>(
            instance.getInstanceProcAddr(instance.instance, "vkGetPhysicalDeviceImageFormatProperties2"));
        if (!imageProperties) return false;
        VkCommandPoolCreateInfo commandPool{VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO};
        commandPool.queueFamilyIndex = instance.queueFamilyIndex;
        if (vkCreateCommandPool(instance.device, &commandPool, nullptr, &pool) != VK_SUCCESS) return Fail("Cannot allocate Vulkan interop commands");
        VkCommandBufferAllocateInfo allocate{VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO};
        allocate.commandPool = pool; allocate.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY; allocate.commandBufferCount = 1;
        if (vkAllocateCommandBuffers(instance.device, &allocate, &commands) != VK_SUCCESS) return false;
        return CreateVulkanProducer();
    }

    bool Import(Surface& surface)
    {
        VkPhysicalDeviceImageDrmFormatModifierInfoEXT modifierQuery{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_IMAGE_DRM_FORMAT_MODIFIER_INFO_EXT};
        modifierQuery.drmFormatModifier = surface.modifier;
        modifierQuery.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
        VkPhysicalDeviceExternalImageFormatInfo externalQuery{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_EXTERNAL_IMAGE_FORMAT_INFO};
        externalQuery.pNext = &modifierQuery;
        externalQuery.handleType = VK_EXTERNAL_MEMORY_HANDLE_TYPE_DMA_BUF_BIT_EXT;
        VkPhysicalDeviceImageFormatInfo2 query{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_IMAGE_FORMAT_INFO_2};
        query.pNext = &externalQuery; query.type = VK_IMAGE_TYPE_2D; query.format = VK_FORMAT_R8G8B8A8_UNORM;
        query.tiling = VK_IMAGE_TILING_DRM_FORMAT_MODIFIER_EXT; query.usage = VK_IMAGE_USAGE_SAMPLED_BIT;
        VkExternalImageFormatProperties externalProperties{VK_STRUCTURE_TYPE_EXTERNAL_IMAGE_FORMAT_PROPERTIES};
        VkImageFormatProperties2 properties{VK_STRUCTURE_TYPE_IMAGE_FORMAT_PROPERTIES_2}; properties.pNext = &externalProperties;
        if (imageProperties(instance.physicalDevice, &query, &properties) != VK_SUCCESS ||
            !(externalProperties.externalMemoryProperties.externalMemoryFeatures & VK_EXTERNAL_MEMORY_FEATURE_IMPORTABLE_BIT) ||
            surface.width > properties.imageFormatProperties.maxExtent.width || surface.height > properties.imageFormatProperties.maxExtent.height)
            return Fail("Vulkan cannot import this EGL RGBA DMA-BUF modifier");
        VkSubresourceLayout planes[4]{};
        for (int i = 0; i < surface.planeCount; ++i) {
            planes[i].offset = static_cast<VkDeviceSize>(surface.offsets[i]);
            planes[i].rowPitch = static_cast<VkDeviceSize>(surface.strides[i]);
        }
        VkImageDrmFormatModifierExplicitCreateInfoEXT modifier{VK_STRUCTURE_TYPE_IMAGE_DRM_FORMAT_MODIFIER_EXPLICIT_CREATE_INFO_EXT};
        modifier.drmFormatModifier = surface.modifier;
        modifier.drmFormatModifierPlaneCount = static_cast<uint32_t>(surface.planeCount); modifier.pPlaneLayouts = planes;
        VkExternalMemoryImageCreateInfo external{VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO};
        external.pNext = &modifier; external.handleTypes = externalQuery.handleType;
        VkImageCreateInfo image{VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO}; image.pNext = &external;
        image.imageType = VK_IMAGE_TYPE_2D; image.format = query.format; image.extent = {surface.width, surface.height, 1};
        image.mipLevels = 1; image.arrayLayers = 1; image.samples = VK_SAMPLE_COUNT_1_BIT;
        image.tiling = query.tiling; image.usage = query.usage; image.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
        if (vkCreateImage(instance.device, &image, nullptr, &surface.image) != VK_SUCCESS) return Fail("Cannot create Vulkan DMA-BUF image");
        VkMemoryRequirements requirements{}; vkGetImageMemoryRequirements(instance.device, surface.image, &requirements);
        VkMemoryFdPropertiesKHR fdProperties{VK_STRUCTURE_TYPE_MEMORY_FD_PROPERTIES_KHR};
        if (vkGetMemoryFdPropertiesKHR(instance.device, externalQuery.handleType, surface.fd, &fdProperties) != VK_SUCCESS) return false;
        uint32_t bits = fdProperties.memoryTypeBits & requirements.memoryTypeBits;
        if (!bits) return Fail("The DMA-BUF has no compatible Vulkan memory type");
        uint32_t memoryType = 0; while (!(bits & (1u << memoryType))) ++memoryType;
        const int importedFd = dup(surface.fd);
        if (importedFd < 0) return Fail("Cannot duplicate the DMA-BUF descriptor");
        VkMemoryDedicatedAllocateInfo dedicated{VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO}; dedicated.image = surface.image;
        VkImportMemoryFdInfoKHR imported{VK_STRUCTURE_TYPE_IMPORT_MEMORY_FD_INFO_KHR};
        imported.pNext = &dedicated; imported.handleType = externalQuery.handleType; imported.fd = importedFd;
        VkMemoryAllocateInfo allocate{VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO};
        allocate.pNext = &imported; allocate.allocationSize = requirements.size; allocate.memoryTypeIndex = memoryType;
        if (vkAllocateMemory(instance.device, &allocate, nullptr, &surface.memory) != VK_SUCCESS) {
            close(importedFd); return Fail("Vulkan DMA-BUF memory import failed");
        }
        if (vkBindImageMemory(instance.device, surface.image, surface.memory, 0) != VK_SUCCESS) return false;
        close(surface.fd); surface.fd = -1;
        surface.imported = true;
        return true;
    }

    bool Transfer(bool acquire)
    {
        if (!vulkan) return true;
        if (vkResetCommandPool(instance.device, pool, 0) != VK_SUCCESS) return false;
        VkCommandBufferBeginInfo begin{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO}; begin.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
        if (vkBeginCommandBuffer(commands, &begin) != VK_SUCCESS) return false;
        std::vector<VkImageMemoryBarrier> barriers;
        for (auto& surface : surfaces) {
            if (!surface->imported || surface->acquired == acquire) continue;
            VkImageMemoryBarrier barrier{VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER};
            barrier.image = surface->image;
            barrier.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };
            barrier.srcQueueFamilyIndex = acquire ? VK_QUEUE_FAMILY_FOREIGN_EXT : instance.queueFamilyIndex;
            barrier.dstQueueFamilyIndex = acquire ? instance.queueFamilyIndex : VK_QUEUE_FAMILY_FOREIGN_EXT;
            barrier.oldLayout = acquire ? VK_IMAGE_LAYOUT_GENERAL : VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
            barrier.newLayout = acquire ? VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL : VK_IMAGE_LAYOUT_GENERAL;
            barrier.srcAccessMask = acquire ? 0 : VK_ACCESS_SHADER_READ_BIT;
            barrier.dstAccessMask = acquire ? VK_ACCESS_SHADER_READ_BIT : 0;
            barriers.push_back(barrier);
        }
        if (!barriers.empty()) vkCmdPipelineBarrier(commands, VK_PIPELINE_STAGE_ALL_COMMANDS_BIT, VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,
            0, 0, nullptr, 0, nullptr, static_cast<uint32_t>(barriers.size()), barriers.data());
        if (vkEndCommandBuffer(commands) != VK_SUCCESS) return false;
        VkSubmitInfo submit{VK_STRUCTURE_TYPE_SUBMIT_INFO}; submit.commandBufferCount = 1; submit.pCommandBuffers = &commands;
        if (vkQueueSubmit(instance.graphicsQueue, 1, &submit, VK_NULL_HANDLE) != VK_SUCCESS ||
            vkQueueWaitIdle(instance.graphicsQueue) != VK_SUCCESS) return Fail("Vulkan interop ownership transfer failed");
        for (auto& surface : surfaces) if (surface->imported) surface->acquired = acquire;
        return true;
    }

public:
    bool Initialize(IUnityInterfaces* interfaces, UnityGfxRenderer renderer) override
    {
        vulkan = renderer == kUnityGfxRendererVulkan;
        if (vulkan) return InitializeVulkan(interfaces);
        if (eglGetCurrentContext() != EGL_NO_CONTEXT) {
            eglDisplay = eglGetCurrentDisplay(); eglApi = eglQueryAPI();
            return CreateEgl(eglDisplay, eglGetCurrentContext());
        }
        return CreateGlx();
    }
    int Capability() const override { return vulkan ? 5 : 3; }
    int Engine() const override { return eglApi == EGL_OPENGL_ES_API ? 2 : 1; }
    bool MakeCurrent(bool enter) override
    {
        bool success;
        if (glxContext) success = glXMakeContextCurrent(xDisplay, enter ? glxSurface : None,
            enter ? glxSurface : None, enter ? glxContext : nullptr) != 0;
        else {
            if (enter) eglBindAPI(eglApi);
            success = eglMakeCurrent(eglDisplay, enter ? eglSurface : EGL_NO_SURFACE,
                enter ? eglSurface : EGL_NO_SURFACE, enter ? eglContext : EGL_NO_CONTEXT) != 0;
        }
        if (!success) return Fail("Cannot make the independent VLC OpenGL context current");
        if (enter && current) glBindFramebuffer(GL_FRAMEBUFFER, current->framebuffer);
        return true;
    }
    bool Resize(unsigned width, unsigned height) override
    {
        if (!width || !height || width > 16384 || height > 16384) return Fail("Unsupported GPU video dimensions");
        for (auto& surface : surfaces) if (surface->width == width && surface->height == height) {
            current = surface.get(); glBindFramebuffer(GL_FRAMEBUFFER, current->framebuffer); return true;
        }
        if (surfaces.size() >= 9) return Fail("Video changed dimensions too often; selecting bounded CPU fallback");
        auto surface = std::make_unique<Surface>();
        surface->width = width; surface->height = height;
        glGenTextures(1, &surface->texture); glBindTexture(GL_TEXTURE_2D, surface->texture);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
        glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, width, height, 0, GL_RGBA, GL_UNSIGNED_BYTE, nullptr);
        glGenFramebuffers(1, &surface->framebuffer); glBindFramebuffer(GL_FRAMEBUFFER, surface->framebuffer);
        glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, surface->texture, 0);
        current = surface.get(); surfaces.push_back(std::move(surface)); // Also own partially initialized resources.
        if (glCheckFramebufferStatus(GL_FRAMEBUFFER) != GL_FRAMEBUFFER_COMPLETE || glGetError() != GL_NO_ERROR)
            return Fail("Cannot allocate an RGBA video render target");
        if (vulkan) {
            const EGLint attributes[] = { EGL_GL_TEXTURE_LEVEL_KHR, 0, EGL_IMAGE_PRESERVED_KHR, EGL_TRUE, EGL_NONE };
            current->eglImage = createImage(eglDisplay, eglContext, EGL_GL_TEXTURE_2D_KHR,
                reinterpret_cast<EGLClientBuffer>(static_cast<uintptr_t>(current->texture)), attributes);
            int fourcc = 0, planes = 0;
            // DRM_FORMAT_ABGR8888 is the little-endian R,G,B,A byte layout.
            constexpr int rgbaFourcc = 'A' | ('B' << 8) | ('2' << 16) | ('4' << 24);
            // Query the plane count before supplying modifier/FD arrays: some
            // drivers add auxiliary compression planes even for RGBA textures.
            if (current->eglImage == EGL_NO_IMAGE_KHR || !queryDma(eglDisplay, current->eglImage, &fourcc, &planes, nullptr) ||
                planes < 1 || planes > 4 || fourcc != rgbaFourcc)
                return Fail("EGL cannot export an RGBA8 DMA-BUF for this video");
            EGLuint64KHR modifiers[4]{};
            int descriptors[4] = { -1, -1, -1, -1 };
            if (!queryDma(eglDisplay, current->eglImage, nullptr, nullptr, modifiers)) return false;
            const bool exported = exportDma(eglDisplay, current->eglImage, descriptors, current->strides, current->offsets);
            bool compatible = exported && descriptors[0] >= 0;
            struct stat first{};
            compatible = compatible && fstat(descriptors[0], &first) == 0;
            for (int i = 0; i < planes; ++i) {
                compatible = compatible && modifiers[i] == modifiers[0] && current->strides[i] > 0 && current->offsets[i] >= 0;
                // Auxiliary compression planes may be in the same DMA-BUF.
                // Distinct allocations would require disjoint image binding,
                // which this non-YUV RGBA import deliberately does not guess.
                if (i && descriptors[i] >= 0) {
                    struct stat other{};
                    compatible = compatible && fstat(descriptors[i], &other) == 0 && first.st_dev == other.st_dev && first.st_ino == other.st_ino;
                }
            }
            current->fd = descriptors[0];
            for (int i = 1; i < 4; ++i) {
                bool unique = descriptors[i] >= 0;
                for (int previous = 0; previous < i; ++previous) unique = unique && descriptors[i] != descriptors[previous];
                if (unique) close(descriptors[i]);
            }
            if (!compatible) return Fail("EGL returned an unsupported RGBA DMA-BUF plane allocation");
            current->modifier = modifiers[0]; current->planeCount = planes;
        }
        return true;
    }
    void* GetProcAddress(const char* name) override
    {
        return glxContext ? reinterpret_cast<void*>(glXGetProcAddressARB(reinterpret_cast<const GLubyte*>(name))) :
            reinterpret_cast<void*>(eglGetProcAddress(name));
    }
    bool FrameComplete() override { glFinish(); return glGetError() == GL_NO_ERROR; }
    bool Pump() override
    {
        if (vulkan) for (auto& surface : surfaces) if (!surface->imported && !Import(*surface)) return false;
        return true;
    }
    PortableTextureInfo TextureInfo() const override
    {
        if (!current || (vulkan && !current->imported)) return {};
        return { vulkan ? static_cast<void*>(&current->image) : reinterpret_cast<void*>(static_cast<uintptr_t>(current->texture)), current->width, current->height };
    }
    bool Begin() override { return Transfer(true); }
    bool End() override
    {
        if (vulkan) return Transfer(false);
        glFinish(); return glGetError() == GL_NO_ERROR;
    }
    void Retire() override
    {
        // Render events run in Unity's context. Save/restore it while deleting
        // FBOs belonging only to the independent producer context.
        auto previousGlx = glXGetCurrentContext(); auto previousXDisplay = glXGetCurrentDisplay();
        auto previousDraw = glXGetCurrentDrawable(); auto previousRead = glXGetCurrentReadDrawable();
        auto previousEgl = eglGetCurrentContext(); auto previousEglDisplay = eglGetCurrentDisplay();
        auto previousEglDraw = eglGetCurrentSurface(EGL_DRAW); auto previousEglRead = eglGetCurrentSurface(EGL_READ);
        auto previousApi = eglQueryAPI();
        if (vulkan && instance.device && vkQueueWaitIdle) vkQueueWaitIdle(instance.graphicsQueue);
        const bool hasContext = glxContext || eglContext != EGL_NO_CONTEXT;
        const bool active = hasContext && MakeCurrent(true);
        for (auto& surface : surfaces) {
            if (surface->image && vkDestroyImage) vkDestroyImage(instance.device, surface->image, nullptr);
            if (surface->memory && vkFreeMemory) vkFreeMemory(instance.device, surface->memory, nullptr);
            if (surface->fd >= 0) close(surface->fd);
            if (surface->eglImage != EGL_NO_IMAGE_KHR && destroyImage) destroyImage(eglDisplay, surface->eglImage);
            if (active) { glDeleteFramebuffers(1, &surface->framebuffer); glDeleteTextures(1, &surface->texture); }
        }
        surfaces.clear(); current = nullptr;
        if (active) MakeCurrent(false);
        if (pool && vkDestroyCommandPool) vkDestroyCommandPool(instance.device, pool, nullptr);
        pool = VK_NULL_HANDLE; commands = VK_NULL_HANDLE;
        if (glxSurface) glXDestroyPbuffer(xDisplay, glxSurface);
        if (glxContext) glXDestroyContext(xDisplay, glxContext);
        glxSurface = 0; glxContext = nullptr;
        if (eglSurface != EGL_NO_SURFACE) eglDestroySurface(eglDisplay, eglSurface);
        if (eglContext != EGL_NO_CONTEXT) eglDestroyContext(eglDisplay, eglContext);
        eglSurface = EGL_NO_SURFACE; eglContext = EGL_NO_CONTEXT;
        // eglGetPlatformDisplayEXT returns a process-wide shared display. EGL
        // initialization is not reference-counted: terminating it here could
        // invalidate another player's producer context. Keep the display alive;
        // all per-player contexts, surfaces and image resources are released.
        eglDisplay = EGL_NO_DISPLAY;
        eglBindAPI(previousApi);
        if (previousGlx) glXMakeContextCurrent(previousXDisplay, previousDraw, previousRead, previousGlx);
        else if (previousEgl != EGL_NO_CONTEXT) eglMakeCurrent(previousEglDisplay, previousEglDraw, previousEglRead, previousEgl);
    }
    const char* LastError() const override { return error.c_str(); }
};
}

void InstallPortableGpuHooks(IUnityInterfaces* interfaces)
{
    if (!interfaces) return;
    auto* v2 = interfaces->Get<IUnityGraphicsVulkanV2>();
    auto* vulkan = interfaces->Get<IUnityGraphicsVulkan>();
    if (v2) v2->AddInterceptInitialization(Intercept, nullptr, 100);
    else if (vulkan) vulkan->InterceptInitialization(Intercept, nullptr);
}
void ConfigurePortableGpuEvents(IUnityInterfaces* interfaces, UnityGfxRenderer renderer, int eventBase)
{
    if (renderer != kUnityGfxRendererVulkan) return;
    auto* graphics = interfaces->Get<IUnityGraphicsVulkan>();
    if (!graphics) return;
    UnityVulkanPluginEventConfig config{};
    config.renderPassPrecondition = kUnityVulkanRenderPass_EnsureOutside;
    config.graphicsQueueAccess = kUnityVulkanGraphicsQueueAccess_Allow;
    config.flags = kUnityVulkanEventConfigFlag_FlushCommandBuffers | kUnityVulkanEventConfigFlag_SyncWorkerThreads;
    for (int i = 0; i < 4; ++i) graphics->ConfigureEvent(eventBase + i, &config);
}
std::unique_ptr<PortableGpuBackend> CreatePortableGpuBackend(UnityGfxRenderer renderer)
{
    if (renderer == kUnityGfxRendererOpenGLCore || renderer == kUnityGfxRendererVulkan)
        return std::make_unique<LinuxGpu>();
    return nullptr;
}
