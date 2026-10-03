#define VK_NO_PROTOTYPES
#define VK_USE_PLATFORM_WIN32_KHR
#include "VulkanInterop.h"
#include "VulkanVideoDecode.h"
#include "IUnityGraphicsVulkan.h"
#include <dxgi1_2.h>
#include <d3d10.h>
#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstring>
#include <memory>
#include <mutex>
#include <new>
#include <vector>

// Bridge.cpp reserves a Unity event range. Keep this helper independent of the
// FFmpeg ABI headers; the logical Vulkan submit event is Bridge.h's value 9.
int FfuEventId(int event);

namespace {
template<class T> void Drop(T*& p) { if (p) { p->Release(); p = nullptr; } }
template<class T> T* Interface(IUnityInterfaces* interfaces) {
    const auto id = GetUnityInterfaceGUID<T>();
    return static_cast<T*>(interfaces->GetInterfaceSplit(id.m_GUIDHigh, id.m_GUIDLow));
}
PFN_vkCreateDevice realCreateDevice = nullptr;
PFN_vkGetInstanceProcAddr originalLoader = nullptr;
VkInstance loaderInstance = VK_NULL_HANDLE;
PFN_vkEnumerateDeviceExtensionProperties enumerateExtensions = nullptr;
IUnityGraphicsVulkanV2* unity = nullptr;
std::atomic<bool> extensionsEnabled{false};
std::atomic<int> initError{1};
constexpr const char* required[] = {
    VK_KHR_EXTERNAL_MEMORY_EXTENSION_NAME, VK_KHR_EXTERNAL_MEMORY_WIN32_EXTENSION_NAME,
    VK_KHR_GET_MEMORY_REQUIREMENTS_2_EXTENSION_NAME, VK_KHR_DEDICATED_ALLOCATION_EXTENSION_NAME,
    VK_KHR_WIN32_KEYED_MUTEX_EXTENSION_NAME
};

VKAPI_ATTR VkResult VKAPI_CALL CreateDevice(VkPhysicalDevice physical, const VkDeviceCreateInfo* original,
                                           const VkAllocationCallbacks* allocator, VkDevice* device) {
    auto enumerate = enumerateExtensions;
    extensionsEnabled = false;
    if (!enumerate) return FfuVulkanVideoCreateDevice(realCreateDevice, originalLoader, loaderInstance, physical, original, allocator, device);
    uint32_t count = 0;
    if (enumerate(physical, nullptr, &count, nullptr) != VK_SUCCESS) return FfuVulkanVideoCreateDevice(realCreateDevice, originalLoader, loaderInstance, physical, original, allocator, device);
    std::vector<VkExtensionProperties> available(count);
    if (enumerate(physical, nullptr, &count, available.data()) != VK_SUCCESS) return FfuVulkanVideoCreateDevice(realCreateDevice, originalLoader, loaderInstance, physical, original, allocator, device);
    for (const char* name : required) {
        if (std::none_of(available.begin(), available.end(), [name](const VkExtensionProperties& e) { return std::strcmp(e.extensionName, name) == 0; }))
            return FfuVulkanVideoCreateDevice(realCreateDevice, originalLoader, loaderInstance, physical, original, allocator, device);
    }
    std::vector<const char*> names;
    if (original->enabledExtensionCount)
        names.assign(original->ppEnabledExtensionNames, original->ppEnabledExtensionNames + original->enabledExtensionCount);
    for (const char* name : required)
        if (std::none_of(names.begin(), names.end(), [name](const char* e) { return std::strcmp(e, name) == 0; })) names.push_back(name);
    VkDeviceCreateInfo info = *original;
    info.enabledExtensionCount = static_cast<uint32_t>(names.size());
    info.ppEnabledExtensionNames = names.data();
    const VkResult result = FfuVulkanVideoCreateDevice(realCreateDevice, originalLoader, loaderInstance, physical, &info, allocator, device);
    extensionsEnabled = result == VK_SUCCESS;
    // Device creation must remain functional when a driver rejects a supported
    // extension combination. The player will use its software fallback.
    if (result != VK_SUCCESS) return FfuVulkanVideoCreateDevice(realCreateDevice, originalLoader, loaderInstance, physical, original, allocator, device);
    return result;
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL GetProc(VkInstance instance, const char* name) {
    auto function = FfuVulkanVideoInstanceProc(originalLoader, instance, name);
    if (std::strcmp(name, "vkCreateDevice") == 0 && function) {
        realCreateDevice = reinterpret_cast<PFN_vkCreateDevice>(function);
        loaderInstance = instance;
        enumerateExtensions = reinterpret_cast<PFN_vkEnumerateDeviceExtensionProperties>(originalLoader(instance, "vkEnumerateDeviceExtensionProperties"));
        return reinterpret_cast<PFN_vkVoidFunction>(CreateDevice);
    }
    return function;
}
PFN_vkGetInstanceProcAddr UNITY_INTERFACE_API InitializeLoader(PFN_vkGetInstanceProcAddr value, void*) {
    originalLoader = value;
    extensionsEnabled = false;
    return GetProc;
}

struct Context {
    UnityVulkanInstance instance{};
    std::atomic<bool> active{true};
    // Device shutdown can coincide with main-thread import/release. Never call
    // a Vulkan device function after the synchronized active=false transition.
    std::recursive_mutex resources;
    PFN_vkGetDeviceProcAddr GetDeviceProcAddr = nullptr;
#define FFU_DEVICE_FUNCTIONS(X) \
    X(CreateImage) X(DestroyImage) X(GetImageMemoryRequirements) X(AllocateMemory) X(FreeMemory) X(BindImageMemory) \
    X(GetMemoryWin32HandlePropertiesKHR) X(CreateCommandPool) X(DestroyCommandPool) X(AllocateCommandBuffers) \
    X(BeginCommandBuffer) X(EndCommandBuffer) X(CmdPipelineBarrier) X(CmdCopyImage) X(CreateFence) X(DestroyFence) \
    X(GetFenceStatus) X(WaitForFences) X(QueueSubmit)
#define DECLARE(name) PFN_vk##name name = nullptr;
    FFU_DEVICE_FUNCTIONS(DECLARE)
#undef DECLARE
    PFN_vkGetPhysicalDeviceMemoryProperties GetMemoryProperties = nullptr;
    PFN_vkGetPhysicalDeviceImageFormatProperties2 GetImageFormatProperties = nullptr;
    VkPhysicalDeviceMemoryProperties memory{};
    bool Load() {
        GetDeviceProcAddr = reinterpret_cast<PFN_vkGetDeviceProcAddr>(instance.getInstanceProcAddr(instance.instance, "vkGetDeviceProcAddr"));
        if (!GetDeviceProcAddr) return false;
#define LOAD(name) name = reinterpret_cast<PFN_vk##name>(GetDeviceProcAddr(instance.device, "vk" #name)); if (!name) return false;
        FFU_DEVICE_FUNCTIONS(LOAD)
#undef LOAD
        GetMemoryProperties = reinterpret_cast<PFN_vkGetPhysicalDeviceMemoryProperties>(instance.getInstanceProcAddr(instance.instance, "vkGetPhysicalDeviceMemoryProperties"));
        GetImageFormatProperties = reinterpret_cast<PFN_vkGetPhysicalDeviceImageFormatProperties2>(instance.getInstanceProcAddr(instance.instance, "vkGetPhysicalDeviceImageFormatProperties2"));
        if (!GetMemoryProperties || !GetImageFormatProperties) return false;
        GetMemoryProperties(instance.physicalDevice, &memory);
        return true;
    }
};
std::mutex stateMutex;
std::shared_ptr<Context> current;

struct Surface {
    std::atomic<int> references{1};
    std::atomic<int> error{0};
    std::shared_ptr<Context> context;
    ID3D11Texture2D* texture = nullptr;
    ID3D11DeviceContext* producer = nullptr;
    IDXGIKeyedMutex* keyed = nullptr;
    VkImage image = VK_NULL_HANDLE;
    VkDeviceMemory memory = VK_NULL_HANDLE;
    uint32_t width = 0, height = 0;
    bool writing = false;
    void Retain() { ++references; }
    void Release() { if (--references == 0) delete this; }
    ~Surface() {
        std::lock_guard<std::recursive_mutex> lock(context->resources);
        if (context && context->active) {
            if (image) context->DestroyImage(context->instance.device, image, nullptr);
            if (memory) context->FreeMemory(context->instance.device, memory, nullptr);
        }
        Drop(keyed); Drop(producer); Drop(texture);
    }
};
std::atomic<int> inFlight{0};
struct Job {
    Surface* surface = nullptr;
    UnityVulkanImage target{};
    VkCommandPool pool = VK_NULL_HANDLE;
    VkFence fence = VK_NULL_HANDLE;
    bool submitted = false;
    ~Job() {
        const auto context = surface->context;
        auto& c = *context;
        std::lock_guard<std::recursive_mutex> lock(c.resources);
        if (c.active) {
            if (fence) c.DestroyFence(c.instance.device, fence, nullptr);
            if (pool) c.DestroyCommandPool(c.instance.device, pool, nullptr);
        }
        surface->Release();
        --inFlight;
    }
};
// AccessQueue may execute on Unity's submission worker, so all jobs are protected.
std::mutex jobsMutex;
std::vector<Job*> jobs;

void ResetProducerKey(Surface* surface) {
    if (surface->keyed->AcquireSync(1, 5) == S_OK) surface->keyed->ReleaseSync(0);
}
void UNITY_INTERFACE_API CopyOnQueue(int, void* pointer) {
    auto* job = static_cast<Job*>(pointer);
    auto* surface = job->surface;
    const auto context = surface->context;
    auto& c = *context;
    std::lock_guard<std::mutex> lock(jobsMutex);
    std::lock_guard<std::recursive_mutex> resourceLock(c.resources);
    if (!c.active) { delete job; return; }
    VkCommandPoolCreateInfo pool{}; pool.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
    pool.flags = VK_COMMAND_POOL_CREATE_TRANSIENT_BIT; pool.queueFamilyIndex = c.instance.queueFamilyIndex;
    VkResult result = c.CreateCommandPool(c.instance.device, &pool, nullptr, &job->pool);
    VkCommandBuffer command = VK_NULL_HANDLE;
    VkCommandBufferAllocateInfo allocate{}; allocate.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
    allocate.commandPool = job->pool; allocate.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY; allocate.commandBufferCount = 1;
    if (result == VK_SUCCESS) result = c.AllocateCommandBuffers(c.instance.device, &allocate, &command);
    VkCommandBufferBeginInfo begin{}; begin.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    begin.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    if (result == VK_SUCCESS) result = c.BeginCommandBuffer(command, &begin);
    if (result == VK_SUCCESS) {
        VkImageMemoryBarrier acquire{}; acquire.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        acquire.srcAccessMask = 0; acquire.dstAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
        acquire.oldLayout = VK_IMAGE_LAYOUT_GENERAL; acquire.newLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;
        acquire.srcQueueFamilyIndex = VK_QUEUE_FAMILY_EXTERNAL; acquire.dstQueueFamilyIndex = c.instance.queueFamilyIndex;
        acquire.image = surface->image; acquire.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
        c.CmdPipelineBarrier(command, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, VK_PIPELINE_STAGE_TRANSFER_BIT, 0, 0, nullptr, 0, nullptr, 1, &acquire);
        VkImageCopy copy{}; copy.srcSubresource = copy.dstSubresource = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1};
        copy.extent = {surface->width, surface->height, 1};
        c.CmdCopyImage(command, surface->image, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                      job->target.image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &copy);
        VkImageMemoryBarrier release = acquire;
        release.srcAccessMask = VK_ACCESS_TRANSFER_READ_BIT; release.dstAccessMask = 0;
        release.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL; release.newLayout = VK_IMAGE_LAYOUT_GENERAL;
        release.srcQueueFamilyIndex = c.instance.queueFamilyIndex; release.dstQueueFamilyIndex = VK_QUEUE_FAMILY_EXTERNAL;
        c.CmdPipelineBarrier(command, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT, 0, 0, nullptr, 0, nullptr, 1, &release);
        result = c.EndCommandBuffer(command);
    }
    VkFenceCreateInfo fence{}; fence.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
    if (result == VK_SUCCESS) result = c.CreateFence(c.instance.device, &fence, nullptr, &job->fence);
    if (result == VK_SUCCESS) {
        const uint64_t acquireKey = 1, releaseKey = 0;
        const uint32_t timeout = 1000;
        VkWin32KeyedMutexAcquireReleaseInfoKHR keyed{}; keyed.sType = VK_STRUCTURE_TYPE_WIN32_KEYED_MUTEX_ACQUIRE_RELEASE_INFO_KHR;
        keyed.acquireCount = keyed.releaseCount = 1; keyed.pAcquireSyncs = keyed.pReleaseSyncs = &surface->memory;
        keyed.pAcquireKeys = &acquireKey; keyed.pReleaseKeys = &releaseKey; keyed.pAcquireTimeouts = &timeout;
        VkSubmitInfo submit{}; submit.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO; submit.pNext = &keyed;
        submit.commandBufferCount = 1; submit.pCommandBuffers = &command;
        result = c.QueueSubmit(c.instance.graphicsQueue, 1, &submit, job->fence);
        job->submitted = result == VK_SUCCESS;
    }
    if (result != VK_SUCCESS) {
        surface->error = result;
        ResetProducerKey(surface);
        delete job;
    } else jobs.push_back(job);
}
} // namespace

void FfuVulkanPreload(IUnityInterfaces* interfaces) {
    unity = Interface<IUnityGraphicsVulkanV2>(interfaces);
    if (unity) unity->InterceptInitialization(InitializeLoader, nullptr);
}
ID3D11Device* FfuVulkanInitialize(IUnityInterfaces* interfaces) {
    unity = Interface<IUnityGraphicsVulkanV2>(interfaces);
    if (!unity || !extensionsEnabled) { initError = 10; return nullptr; }
    auto context = std::make_shared<Context>(); context->instance = unity->Instance();
    if (!context->instance.device || !context->Load()) { initError = 11; return nullptr; }
    auto properties = reinterpret_cast<PFN_vkGetPhysicalDeviceProperties2>(context->instance.getInstanceProcAddr(context->instance.instance, "vkGetPhysicalDeviceProperties2"));
    if (!properties) { initError = 12; return nullptr; }
    VkPhysicalDeviceIDProperties id{}; id.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_ID_PROPERTIES;
    VkPhysicalDeviceProperties2 props{}; props.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PROPERTIES_2; props.pNext = &id;
    properties(context->instance.physicalDevice, &props);
    if (!id.deviceLUIDValid) { initError = 13; return nullptr; }
    IDXGIFactory1* factory = nullptr;
    if (FAILED(CreateDXGIFactory1(__uuidof(IDXGIFactory1), reinterpret_cast<void**>(&factory)))) return nullptr;
    ID3D11Device* device = nullptr;
    for (UINT index = 0; !device; ++index) {
        IDXGIAdapter1* adapter = nullptr;
        if (factory->EnumAdapters1(index, &adapter) == DXGI_ERROR_NOT_FOUND) break;
        DXGI_ADAPTER_DESC1 desc{}; adapter->GetDesc1(&desc);
        if (std::memcmp(&desc.AdapterLuid, id.deviceLUID, VK_LUID_SIZE) == 0) {
            D3D11CreateDevice(adapter, D3D_DRIVER_TYPE_UNKNOWN, nullptr, D3D11_CREATE_DEVICE_VIDEO_SUPPORT,
                nullptr, 0, D3D11_SDK_VERSION, &device, nullptr, nullptr);
        }
        Drop(adapter);
    }
    Drop(factory);
    if (!device) { initError = 14; return nullptr; }
    ID3D11DeviceContext* immediate = nullptr; ID3D10Multithread* multithread = nullptr;
    device->GetImmediateContext(&immediate);
    if (FAILED(immediate->QueryInterface(__uuidof(ID3D10Multithread), reinterpret_cast<void**>(&multithread)))) {
        Drop(immediate); Drop(device); initError = 15; return nullptr;
    }
    multithread->SetMultithreadProtected(TRUE); Drop(multithread); Drop(immediate);
    if (unity->ConfigureEvent) {
        UnityVulkanPluginEventConfig config{};
        config.renderPassPrecondition = kUnityVulkanRenderPass_EnsureOutside;
        config.graphicsQueueAccess = kUnityVulkanGraphicsQueueAccess_DontCare;
        config.flags = kUnityVulkanEventConfigFlag_EnsurePreviousFrameSubmission |
                       kUnityVulkanEventConfigFlag_ModifiesCommandBuffersState;
        unity->ConfigureEvent(FfuEventId(9), &config);
    }
    { std::lock_guard<std::mutex> lock(stateMutex); current = context; }
    initError = 0;
    return device;
}
void* FfuVulkanImport(ID3D11Texture2D* texture) {
    std::shared_ptr<Context> context;
    { std::lock_guard<std::mutex> lock(stateMutex); context = current; }
    if (!context || !texture || !context->active) return nullptr;
    std::lock_guard<std::recursive_mutex> resourceLock(context->resources);
    if (!context->active) return nullptr;
    auto* surface = new (std::nothrow) Surface();
    if (!surface) return nullptr;
    surface->context = context; surface->texture = texture; texture->AddRef();
    ID3D11Device* device = nullptr; texture->GetDevice(&device); device->GetImmediateContext(&surface->producer); Drop(device);
    D3D11_TEXTURE2D_DESC desc{}; texture->GetDesc(&desc);
    surface->width = desc.Width; surface->height = desc.Height;
    if (desc.Format != DXGI_FORMAT_R8G8B8A8_UNORM || desc.ArraySize != 1 || desc.MipLevels != 1 || desc.SampleDesc.Count != 1 ||
        FAILED(texture->QueryInterface(__uuidof(IDXGIKeyedMutex), reinterpret_cast<void**>(&surface->keyed)))) { surface->Release(); return nullptr; }
    VkPhysicalDeviceExternalImageFormatInfo externalFormat{}; externalFormat.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_EXTERNAL_IMAGE_FORMAT_INFO;
    externalFormat.handleType = VK_EXTERNAL_MEMORY_HANDLE_TYPE_D3D11_TEXTURE_BIT;
    VkPhysicalDeviceImageFormatInfo2 format{}; format.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_IMAGE_FORMAT_INFO_2; format.pNext = &externalFormat;
    format.format = VK_FORMAT_R8G8B8A8_UNORM; format.type = VK_IMAGE_TYPE_2D; format.tiling = VK_IMAGE_TILING_OPTIMAL;
    format.usage = VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
    VkExternalImageFormatProperties externalProperties{}; externalProperties.sType = VK_STRUCTURE_TYPE_EXTERNAL_IMAGE_FORMAT_PROPERTIES;
    VkImageFormatProperties2 properties{}; properties.sType = VK_STRUCTURE_TYPE_IMAGE_FORMAT_PROPERTIES_2; properties.pNext = &externalProperties;
    auto& c = *context;
    if (c.GetImageFormatProperties(c.instance.physicalDevice, &format, &properties) != VK_SUCCESS ||
        !(externalProperties.externalMemoryProperties.externalMemoryFeatures & VK_EXTERNAL_MEMORY_FEATURE_IMPORTABLE_BIT)) { surface->Release(); return nullptr; }
    IDXGIResource1* resource = nullptr; HANDLE handle = nullptr;
    HRESULT hr = texture->QueryInterface(__uuidof(IDXGIResource1), reinterpret_cast<void**>(&resource));
    if (SUCCEEDED(hr)) hr = resource->CreateSharedHandle(nullptr, DXGI_SHARED_RESOURCE_READ | DXGI_SHARED_RESOURCE_WRITE, nullptr, &handle);
    Drop(resource);
    if (FAILED(hr)) { surface->Release(); return nullptr; }
    VkExternalMemoryImageCreateInfo external{}; external.sType = VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO;
    external.handleTypes = VK_EXTERNAL_MEMORY_HANDLE_TYPE_D3D11_TEXTURE_BIT;
    VkImageCreateInfo image{}; image.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO; image.pNext = &external;
    image.imageType = VK_IMAGE_TYPE_2D; image.format = VK_FORMAT_R8G8B8A8_UNORM; image.extent = {desc.Width, desc.Height, 1};
    image.mipLevels = image.arrayLayers = 1; image.samples = VK_SAMPLE_COUNT_1_BIT; image.tiling = VK_IMAGE_TILING_OPTIMAL;
    image.usage = VK_IMAGE_USAGE_TRANSFER_SRC_BIT; image.sharingMode = VK_SHARING_MODE_EXCLUSIVE; image.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    VkResult result = c.CreateImage(c.instance.device, &image, nullptr, &surface->image);
    VkMemoryWin32HandlePropertiesKHR handleProperties{}; handleProperties.sType = VK_STRUCTURE_TYPE_MEMORY_WIN32_HANDLE_PROPERTIES_KHR;
    if (result == VK_SUCCESS) result = c.GetMemoryWin32HandlePropertiesKHR(c.instance.device, VK_EXTERNAL_MEMORY_HANDLE_TYPE_D3D11_TEXTURE_BIT, handle, &handleProperties);
    if (result == VK_SUCCESS) {
        VkMemoryRequirements requirements{}; c.GetImageMemoryRequirements(c.instance.device, surface->image, &requirements);
        uint32_t types = requirements.memoryTypeBits & handleProperties.memoryTypeBits, type = 0;
        while (type < c.memory.memoryTypeCount && !(types & (1u << type))) ++type;
        if (type == c.memory.memoryTypeCount) result = VK_ERROR_FEATURE_NOT_PRESENT;
        else {
            VkMemoryDedicatedAllocateInfo dedicated{}; dedicated.sType = VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO; dedicated.image = surface->image;
            VkImportMemoryWin32HandleInfoKHR imported{}; imported.sType = VK_STRUCTURE_TYPE_IMPORT_MEMORY_WIN32_HANDLE_INFO_KHR; imported.pNext = &dedicated;
            imported.handleType = VK_EXTERNAL_MEMORY_HANDLE_TYPE_D3D11_TEXTURE_BIT; imported.handle = handle;
            VkMemoryAllocateInfo allocate{}; allocate.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO; allocate.pNext = &imported;
            allocate.allocationSize = requirements.size; allocate.memoryTypeIndex = type;
            result = c.AllocateMemory(c.instance.device, &allocate, nullptr, &surface->memory);
            if (result == VK_SUCCESS) result = c.BindImageMemory(c.instance.device, surface->image, surface->memory, 0);
        }
    }
    CloseHandle(handle); // NT handle ownership is retained by the caller on import.
    if (result != VK_SUCCESS) { surface->Release(); return nullptr; }
    return surface;
}
void FfuVulkanRelease(void* pointer) { if (pointer) static_cast<Surface*>(pointer)->Release(); }
bool FfuVulkanBeginWrite(void* pointer) {
    FfuVulkanPoll(false);
    auto* surface = static_cast<Surface*>(pointer);
    if (!surface || !surface->context->active || surface->writing || surface->error) return false;
    const HRESULT result = surface->keyed->AcquireSync(0, 5);
    if (result != S_OK) { surface->error = result; return false; }
    surface->writing = true;
    return true;
}
bool FfuVulkanEndWrite(void* pointer, void* unityTexture) {
    auto* surface = static_cast<Surface*>(pointer);
    if (!surface || !surface->writing) return false;
    surface->writing = false;
    const HRESULT hr = surface->keyed->ReleaseSync(1);
    surface->producer->Flush();
    if (hr != S_OK) { surface->error = hr; return false; }
    if (!surface->context->active || !unity || !unityTexture) { ResetProducerKey(surface); return false; }
    if (inFlight.fetch_add(1) >= 32) { --inFlight; surface->error = -1001; ResetProducerKey(surface); return false; }
    auto* job = new (std::nothrow) Job();
    if (!job) { --inFlight; ResetProducerKey(surface); return false; }
    job->surface = surface; surface->Retain();
    if (!unity->AccessTexture(unityTexture, UnityVulkanWholeImage, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
        VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_WRITE_BIT, kUnityVulkanResourceAccess_PipelineBarrier, &job->target) ||
        job->target.format != VK_FORMAT_R8G8B8A8_UNORM || job->target.extent.width != surface->width || job->target.extent.height != surface->height ||
        !(job->target.usage & VK_IMAGE_USAGE_TRANSFER_DST_BIT)) {
        surface->error = -1000; ResetProducerKey(surface); delete job; return false;
    }
    unity->AccessQueue(CopyOnQueue, 0, job, true);
    return true;
}
int FfuVulkanError(void* pointer) { return pointer ? static_cast<Surface*>(pointer)->error.load() : initError.load(); }
void FfuVulkanPoll(bool drain) {
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
    std::lock_guard<std::mutex> lock(jobsMutex);
    for (auto it = jobs.begin(); it != jobs.end();) {
        auto* job = *it;
        const auto context = job->surface->context;
        auto& c = *context;
        std::lock_guard<std::recursive_mutex> resourceLock(c.resources);
        VkResult result = c.active ? c.GetFenceStatus(c.instance.device, job->fence) : VK_ERROR_DEVICE_LOST;
        if (drain && result == VK_NOT_READY) {
            const auto remaining = std::chrono::duration_cast<std::chrono::nanoseconds>(deadline - std::chrono::steady_clock::now()).count();
            if (remaining > 0) result = c.WaitForFences(c.instance.device, 1, &job->fence, VK_TRUE, static_cast<uint64_t>(remaining));
        }
        if (result == VK_SUCCESS || result == VK_ERROR_DEVICE_LOST) {
            if (result != VK_SUCCESS) job->surface->error = result;
            delete job; it = jobs.erase(it);
        } else ++it;
    }
}
void FfuVulkanShutdown() {
    FfuVulkanPoll(true);
    std::lock_guard<std::mutex> lock(stateMutex);
    // Outstanding references after a hung queue are intentionally abandoned;
    // Unity owns the device and may destroy it after this shutdown callback.
    if (current) {
        std::lock_guard<std::recursive_mutex> resourceLock(current->resources);
        current->active = false;
    }
    current.reset();
}
