#pragma once

// Optional Windows D3D11 -> Vulkan image sharing. No pixel data crosses the CPU.
// All instance methods run in a Unity plugin event with queue access and a flush
// of Unity's pending command buffers. The producer uses keyed mutex key 0 too.
#ifndef VK_USE_PLATFORM_WIN32_KHR
#define VK_USE_PLATFORM_WIN32_KHR
#endif
#ifndef VK_NO_PROTOTYPES
#define VK_NO_PROTOTYPES
#endif
#include <windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <vulkan/vulkan.h>
#include "Unity/IUnityGraphics.h"
#include "Unity/IUnityGraphicsVulkan.h"
#include <algorithm>
#include <cstring>
#include <vector>

class VulkanInterop final
{
public:
    struct Texture
    {
        VkImage image = VK_NULL_HANDLE;
        VkDeviceMemory memory = VK_NULL_HANDLE;
        ID3D11Texture2D* resource = nullptr;
        bool locked = false;
        void* NativePointer() { return &image; }
    };

    static void InstallHook(IUnityInterfaces* interfaces)
    {
        s_graphics = interfaces->Get<IUnityGraphicsVulkan>();
        auto* v2 = interfaces->Get<IUnityGraphicsVulkanV2>();
        if (v2)
            v2->AddInterceptInitialization(Intercept, nullptr, 100);
        else if (s_graphics)
            s_graphics->InterceptInitialization(Intercept, nullptr);
    }

    static bool GetAdapterLuid(LUID& luid)
    {
        if (!s_graphics || !s_enabledDevice)
            return false;
        auto instance = s_graphics->Instance();
        if (!instance.device || instance.device != s_enabledDevice)
            return false;
        auto properties2 = reinterpret_cast<PFN_vkGetPhysicalDeviceProperties2>(
            instance.getInstanceProcAddr(instance.instance, "vkGetPhysicalDeviceProperties2"));
        if (!properties2)
            return false;
        VkPhysicalDeviceIDProperties identity{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_ID_PROPERTIES};
        VkPhysicalDeviceProperties2 properties{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PROPERTIES_2};
        properties.pNext = &identity;
        properties2(instance.physicalDevice, &properties);
        if (!identity.deviceLUIDValid)
            return false;
        static_assert(sizeof(luid) == VK_LUID_SIZE, "Windows LUID size");
        std::memcpy(&luid, identity.deviceLUID, sizeof(luid));
        return true;
    }

    static void ConfigureEvents(int eventBase)
    {
        if (!s_graphics || !s_graphics->Instance().device) return;
        UnityVulkanPluginEventConfig config{};
        config.renderPassPrecondition = kUnityVulkanRenderPass_EnsureOutside;
        config.graphicsQueueAccess = kUnityVulkanGraphicsQueueAccess_Allow;
        config.flags = kUnityVulkanEventConfigFlag_FlushCommandBuffers |
            kUnityVulkanEventConfigFlag_SyncWorkerThreads;
        for (int id = 0; id <= 3; ++id) s_graphics->ConfigureEvent(eventBase + id, &config);
    }

    bool Initialize()
    {
        if (_commands)
            return true;
        // A prior partial initialization must never look like a usable device.
        Shutdown();
        LUID luid{};
        if (!GetAdapterLuid(luid))
            return Fail("Vulkan D3D11 import extensions or a matching adapter LUID are unavailable (restart after plugin preload)");
        _instance = s_graphics->Instance();
        _getDeviceProc = reinterpret_cast<PFN_vkGetDeviceProcAddr>(
            _instance.getInstanceProcAddr(_instance.instance, "vkGetDeviceProcAddr"));
        if (!_getDeviceProc)
            return FailInitialization("Vulkan device dispatch is unavailable");
#define VLC_VK_LOAD(name) name = reinterpret_cast<PFN_##name>(_getDeviceProc(_instance.device, #name)); if (!name) return FailInitialization(#name " is unavailable")
        VLC_VK_LOAD(vkCreateImage); VLC_VK_LOAD(vkDestroyImage);
        VLC_VK_LOAD(vkGetImageMemoryRequirements); VLC_VK_LOAD(vkAllocateMemory);
        VLC_VK_LOAD(vkFreeMemory); VLC_VK_LOAD(vkBindImageMemory);
        VLC_VK_LOAD(vkGetMemoryWin32HandlePropertiesKHR);
        VLC_VK_LOAD(vkCreateCommandPool); VLC_VK_LOAD(vkDestroyCommandPool);
        VLC_VK_LOAD(vkAllocateCommandBuffers); VLC_VK_LOAD(vkResetCommandPool);
        VLC_VK_LOAD(vkBeginCommandBuffer); VLC_VK_LOAD(vkEndCommandBuffer);
        VLC_VK_LOAD(vkCmdPipelineBarrier); VLC_VK_LOAD(vkQueueSubmit);
        VLC_VK_LOAD(vkQueueWaitIdle);
#undef VLC_VK_LOAD
        VkCommandPoolCreateInfo pool{VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO};
        pool.queueFamilyIndex = _instance.queueFamilyIndex;
        if (vkCreateCommandPool(_instance.device, &pool, nullptr, &_pool) != VK_SUCCESS)
            return FailInitialization("Vulkan video command pool creation failed");
        VkCommandBufferAllocateInfo allocation{VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO};
        allocation.commandPool = _pool;
        allocation.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
        allocation.commandBufferCount = 1;
        if (vkAllocateCommandBuffers(_instance.device, &allocation, &_commands) != VK_SUCCESS)
            return FailInitialization("Vulkan video command buffer allocation failed");
        return true;
    }

    bool RegisterTexture(ID3D11Texture2D* resource, Texture& texture)
    {
        if (!_commands || !resource || texture.image)
            return Fail("Invalid Vulkan video image registration");
        D3D11_TEXTURE2D_DESC desc{};
        resource->GetDesc(&desc);
        if (desc.Format != DXGI_FORMAT_R8G8B8A8_UNORM || desc.MipLevels != 1 ||
            desc.ArraySize != 1 || desc.SampleDesc.Count != 1 ||
            desc.Usage != D3D11_USAGE_DEFAULT || desc.CPUAccessFlags != 0 ||
            !(desc.MiscFlags & D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX) ||
            !(desc.MiscFlags & D3D11_RESOURCE_MISC_SHARED_NTHANDLE))
            return Fail("Vulkan video import requires a shared keyed RGBA8 surface");
        ID3D11Device* d3dDevice = nullptr;
        resource->GetDevice(&d3dDevice);
        IDXGIDevice* dxgiDevice = nullptr;
        IDXGIAdapter* adapter = nullptr;
        DXGI_ADAPTER_DESC adapterDescription{};
        HRESULT adapterResult = d3dDevice->QueryInterface(IID_PPV_ARGS(&dxgiDevice));
        d3dDevice->Release();
        if (SUCCEEDED(adapterResult))
            adapterResult = dxgiDevice->GetAdapter(&adapter);
        if (SUCCEEDED(adapterResult))
            adapterResult = adapter->GetDesc(&adapterDescription);
        if (adapter) adapter->Release();
        if (dxgiDevice) dxgiDevice->Release();
        LUID vulkanLuid{};
        if (FAILED(adapterResult) || !GetAdapterLuid(vulkanLuid) ||
            std::memcmp(&vulkanLuid, &adapterDescription.AdapterLuid, sizeof(vulkanLuid)) != 0)
            return Fail("D3D11 and Vulkan video surfaces must use the same GPU adapter");
        auto imageProperties = reinterpret_cast<PFN_vkGetPhysicalDeviceImageFormatProperties2>(
            _instance.getInstanceProcAddr(_instance.instance, "vkGetPhysicalDeviceImageFormatProperties2"));
        if (!imageProperties)
            return Fail("Vulkan external image capability query is unavailable");
        VkPhysicalDeviceExternalImageFormatInfo externalQuery{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_EXTERNAL_IMAGE_FORMAT_INFO};
        externalQuery.handleType = VK_EXTERNAL_MEMORY_HANDLE_TYPE_D3D11_TEXTURE_BIT;
        VkPhysicalDeviceImageFormatInfo2 query{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_IMAGE_FORMAT_INFO_2};
        query.pNext = &externalQuery;
        query.format = VK_FORMAT_R8G8B8A8_UNORM;
        query.type = VK_IMAGE_TYPE_2D;
        query.tiling = VK_IMAGE_TILING_OPTIMAL;
        query.usage = VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
        VkExternalImageFormatProperties externalProperties{VK_STRUCTURE_TYPE_EXTERNAL_IMAGE_FORMAT_PROPERTIES};
        VkImageFormatProperties2 properties{VK_STRUCTURE_TYPE_IMAGE_FORMAT_PROPERTIES_2};
        properties.pNext = &externalProperties;
        if (imageProperties(_instance.physicalDevice, &query, &properties) != VK_SUCCESS ||
            !(externalProperties.externalMemoryProperties.externalMemoryFeatures & VK_EXTERNAL_MEMORY_FEATURE_IMPORTABLE_BIT) ||
            desc.Width > properties.imageFormatProperties.maxExtent.width || desc.Height > properties.imageFormatProperties.maxExtent.height)
            return Fail("This Vulkan driver cannot import the D3D11 video image");

        IDXGIResource1* shared = nullptr;
        HANDLE handle = nullptr;
        HRESULT hr = resource->QueryInterface(IID_PPV_ARGS(&shared));
        if (SUCCEEDED(hr))
        {
            hr = shared->CreateSharedHandle(nullptr, DXGI_SHARED_RESOURCE_READ | DXGI_SHARED_RESOURCE_WRITE, nullptr, &handle);
            shared->Release();
        }
        if (FAILED(hr))
            return Fail("D3D11 video NT handle export failed");

        VkExternalMemoryImageCreateInfo external{VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO};
        external.handleTypes = VK_EXTERNAL_MEMORY_HANDLE_TYPE_D3D11_TEXTURE_BIT;
        VkImageCreateInfo image{VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO};
        image.pNext = &external;
        image.imageType = VK_IMAGE_TYPE_2D;
        image.format = query.format;
        image.extent = {desc.Width, desc.Height, 1};
        image.mipLevels = 1;
        image.arrayLayers = 1;
        image.samples = VK_SAMPLE_COUNT_1_BIT;
        image.tiling = query.tiling;
        image.usage = query.usage;
        image.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
        image.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        VkResult result = vkCreateImage(_instance.device, &image, nullptr, &texture.image);
        if (result == VK_SUCCESS)
        {
            VkMemoryRequirements requirements{};
            vkGetImageMemoryRequirements(_instance.device, texture.image, &requirements);
            VkMemoryWin32HandlePropertiesKHR handleProperties{VK_STRUCTURE_TYPE_MEMORY_WIN32_HANDLE_PROPERTIES_KHR};
            result = vkGetMemoryWin32HandlePropertiesKHR(_instance.device, externalQuery.handleType, handle, &handleProperties);
            uint32_t bits = requirements.memoryTypeBits & handleProperties.memoryTypeBits;
            if (!bits)
                result = VK_ERROR_INVALID_EXTERNAL_HANDLE;
            if (result == VK_SUCCESS)
            {
                uint32_t memoryType = 0;
                while (!(bits & (1u << memoryType))) ++memoryType;
                VkMemoryDedicatedAllocateInfo dedicated{VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO};
                dedicated.image = texture.image;
                VkImportMemoryWin32HandleInfoKHR import{VK_STRUCTURE_TYPE_IMPORT_MEMORY_WIN32_HANDLE_INFO_KHR};
                import.pNext = &dedicated;
                import.handleType = externalQuery.handleType;
                import.handle = handle;
                VkMemoryAllocateInfo allocation{VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO};
                allocation.pNext = &import;
                allocation.allocationSize = requirements.size;
                allocation.memoryTypeIndex = memoryType;
                result = vkAllocateMemory(_instance.device, &allocation, nullptr, &texture.memory);
                if (result == VK_SUCCESS)
                    result = vkBindImageMemory(_instance.device, texture.image, texture.memory, 0);
            }
        }
        CloseHandle(handle); // Vulkan retains the allocation, not this NT handle.
        if (result != VK_SUCCESS)
        {
            UnregisterTexture(texture);
            return Fail("Vulkan D3D11 external image import failed");
        }
        resource->AddRef();
        texture.resource = resource;
        return true;
    }

    bool Lock(Texture& texture)
    {
        if (texture.locked) return true;
        if (!Transition(texture, true)) return false;
        texture.locked = true;
        return true;
    }
    bool Unlock(Texture& texture)
    {
        if (!texture.locked) return true;
        if (!Transition(texture, false)) return false;
        texture.locked = false;
        return true;
    }
    bool UnregisterTexture(Texture& texture)
    {
        if (!Unlock(texture)) return false;
        if (texture.image) vkDestroyImage(_instance.device, texture.image, nullptr);
        if (texture.memory) vkFreeMemory(_instance.device, texture.memory, nullptr);
        if (texture.resource) texture.resource->Release();
        texture = {};
        return true;
    }
    bool Shutdown()
    {
        if (_pool) vkDestroyCommandPool(_instance.device, _pool, nullptr);
        _pool = VK_NULL_HANDLE;
        _commands = VK_NULL_HANDLE;
        _instance = {};
        return true;
    }
    const char* LastError() const { return _error ? _error : ""; }

private:
    bool Transition(Texture& texture, bool acquire)
    {
        if (!texture.image || !texture.memory || !_commands) return false;
        if (vkResetCommandPool(_instance.device, _pool, 0) != VK_SUCCESS)
            return Fail("Vulkan command pool reset failed");
        VkCommandBufferBeginInfo begin{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO};
        begin.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
        if (vkBeginCommandBuffer(_commands, &begin) != VK_SUCCESS) return false;
        VkImageMemoryBarrier barrier{VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER};
        barrier.srcAccessMask = acquire ? 0 : VK_ACCESS_SHADER_READ_BIT;
        barrier.dstAccessMask = acquire ? VK_ACCESS_SHADER_READ_BIT : 0;
        barrier.oldLayout = acquire ? VK_IMAGE_LAYOUT_GENERAL : VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        barrier.newLayout = acquire ? VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL : VK_IMAGE_LAYOUT_GENERAL;
        barrier.srcQueueFamilyIndex = acquire ? VK_QUEUE_FAMILY_EXTERNAL : _instance.queueFamilyIndex;
        barrier.dstQueueFamilyIndex = acquire ? _instance.queueFamilyIndex : VK_QUEUE_FAMILY_EXTERNAL;
        barrier.image = texture.image;
        barrier.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
        vkCmdPipelineBarrier(_commands, VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,
            VK_PIPELINE_STAGE_ALL_COMMANDS_BIT, 0, 0, nullptr, 0, nullptr, 1, &barrier);
        if (vkEndCommandBuffer(_commands) != VK_SUCCESS) return false;
        uint64_t key = 0;
        uint32_t timeout = 5000;
        VkWin32KeyedMutexAcquireReleaseInfoKHR keyed{VK_STRUCTURE_TYPE_WIN32_KEYED_MUTEX_ACQUIRE_RELEASE_INFO_KHR};
        if (acquire)
        {
            keyed.acquireCount = 1;
            keyed.pAcquireSyncs = &texture.memory;
            keyed.pAcquireKeys = &key;
            keyed.pAcquireTimeouts = &timeout;
        }
        else
        {
            keyed.releaseCount = 1;
            keyed.pReleaseSyncs = &texture.memory;
            keyed.pReleaseKeys = &key;
        }
        VkSubmitInfo submit{VK_STRUCTURE_TYPE_SUBMIT_INFO};
        submit.pNext = &keyed;
        submit.commandBufferCount = 1;
        submit.pCommandBuffers = &_commands;
        if (vkQueueSubmit(_instance.graphicsQueue, 1, &submit, VK_NULL_HANDLE) != VK_SUCCESS ||
            vkQueueWaitIdle(_instance.graphicsQueue) != VK_SUCCESS)
            return Fail("Vulkan video ownership handoff failed");
        return true;
    }

    static PFN_vkGetInstanceProcAddr UNITY_INTERFACE_API Intercept(PFN_vkGetInstanceProcAddr original, void*)
    {
        s_original = original;
        s_enabledDevice = VK_NULL_HANDLE;
        s_instanceApiVersion = VK_API_VERSION_1_0;
        return GetProc;
    }
    static PFN_vkVoidFunction VKAPI_CALL GetProc(VkInstance instance, const char* name)
    {
        auto original = s_original(instance, name);
        if (std::strcmp(name, "vkCreateInstance") == 0)
        {
            s_createInstance = reinterpret_cast<PFN_vkCreateInstance>(original);
            return reinterpret_cast<PFN_vkVoidFunction>(CreateInstance);
        }
        if (instance && std::strcmp(name, "vkCreateDevice") == 0)
        {
            s_createDevice = reinterpret_cast<PFN_vkCreateDevice>(original);
            s_enumerateExtensions = reinterpret_cast<PFN_vkEnumerateDeviceExtensionProperties>(s_original(instance, "vkEnumerateDeviceExtensionProperties"));
            s_getProperties = reinterpret_cast<PFN_vkGetPhysicalDeviceProperties>(s_original(instance, "vkGetPhysicalDeviceProperties"));
            return reinterpret_cast<PFN_vkVoidFunction>(CreateDevice);
        }
        return original;
    }
    static VkResult VKAPI_CALL CreateInstance(const VkInstanceCreateInfo* info,
        const VkAllocationCallbacks* allocator, VkInstance* instance)
    {
        const VkResult result = s_createInstance(info, allocator, instance);
        if (result == VK_SUCCESS)
            s_instanceApiVersion = info->pApplicationInfo && info->pApplicationInfo->apiVersion
                ? info->pApplicationInfo->apiVersion : VK_API_VERSION_1_0;
        return result;
    }
    static VkResult VKAPI_CALL CreateDevice(VkPhysicalDevice physical, const VkDeviceCreateInfo* info,
        const VkAllocationCallbacks* allocator, VkDevice* device)
    {
        uint32_t count = 0;
        VkPhysicalDeviceProperties properties{};
        if (s_getProperties) s_getProperties(physical, &properties);
        // The instance's requested version determines promoted functionality,
        // not just the version advertised by the physical device.
        if (!s_enumerateExtensions || s_instanceApiVersion < VK_API_VERSION_1_1 ||
            properties.apiVersion < VK_API_VERSION_1_1 ||
            s_enumerateExtensions(physical, nullptr, &count, nullptr) != VK_SUCCESS)
            return s_createDevice(physical, info, allocator, device);
        std::vector<VkExtensionProperties> supported(count);
        if (s_enumerateExtensions(physical, nullptr, &count, supported.data()) != VK_SUCCESS)
            return s_createDevice(physical, info, allocator, device);
        const char* required[] = {VK_KHR_EXTERNAL_MEMORY_WIN32_EXTENSION_NAME, VK_KHR_WIN32_KEYED_MUTEX_EXTENSION_NAME};
        std::vector<const char*> extensions;
        for (uint32_t i = 0; i < info->enabledExtensionCount; ++i) extensions.push_back(info->ppEnabledExtensionNames[i]);
        for (const char* extension : required)
        {
            if (std::none_of(supported.begin(), supported.end(), [extension](const auto& item) { return std::strcmp(item.extensionName, extension) == 0; }))
                return s_createDevice(physical, info, allocator, device);
            if (std::none_of(extensions.begin(), extensions.end(), [extension](const char* item) { return std::strcmp(item, extension) == 0; }))
                extensions.push_back(extension);
        }
        VkDeviceCreateInfo augmented = *info;
        augmented.enabledExtensionCount = static_cast<uint32_t>(extensions.size());
        augmented.ppEnabledExtensionNames = extensions.data();
        VkResult result = s_createDevice(physical, &augmented, allocator, device);
        if (result == VK_SUCCESS) s_enabledDevice = *device;
        else result = s_createDevice(physical, info, allocator, device); // Optional interop must not prevent Unity startup.
        return result;
    }
    bool Fail(const char* error) { _error = error; return false; }
    bool FailInitialization(const char* error)
    {
        Shutdown();
        return Fail(error);
    }

    inline static IUnityGraphicsVulkan* s_graphics = nullptr;
    inline static PFN_vkGetInstanceProcAddr s_original = nullptr;
    inline static PFN_vkCreateInstance s_createInstance = nullptr;
    inline static uint32_t s_instanceApiVersion = VK_API_VERSION_1_0;
    inline static PFN_vkCreateDevice s_createDevice = nullptr;
    inline static PFN_vkEnumerateDeviceExtensionProperties s_enumerateExtensions = nullptr;
    inline static PFN_vkGetPhysicalDeviceProperties s_getProperties = nullptr;
    inline static VkDevice s_enabledDevice = VK_NULL_HANDLE;
    UnityVulkanInstance _instance{};
    PFN_vkGetDeviceProcAddr _getDeviceProc = nullptr;
    VkCommandPool _pool = VK_NULL_HANDLE;
    VkCommandBuffer _commands = VK_NULL_HANDLE;
    const char* _error = nullptr;
#define VLC_VK_MEMBER(name) PFN_##name name = nullptr
    VLC_VK_MEMBER(vkCreateImage); VLC_VK_MEMBER(vkDestroyImage);
    VLC_VK_MEMBER(vkGetImageMemoryRequirements); VLC_VK_MEMBER(vkAllocateMemory);
    VLC_VK_MEMBER(vkFreeMemory); VLC_VK_MEMBER(vkBindImageMemory);
    VLC_VK_MEMBER(vkGetMemoryWin32HandlePropertiesKHR);
    VLC_VK_MEMBER(vkCreateCommandPool); VLC_VK_MEMBER(vkDestroyCommandPool);
    VLC_VK_MEMBER(vkAllocateCommandBuffers); VLC_VK_MEMBER(vkResetCommandPool);
    VLC_VK_MEMBER(vkBeginCommandBuffer); VLC_VK_MEMBER(vkEndCommandBuffer);
    VLC_VK_MEMBER(vkCmdPipelineBarrier); VLC_VK_MEMBER(vkQueueSubmit);
    VLC_VK_MEMBER(vkQueueWaitIdle);
#undef VLC_VK_MEMBER
};
