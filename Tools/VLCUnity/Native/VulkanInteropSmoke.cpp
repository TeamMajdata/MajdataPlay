// Real-driver smoke test with a minimal Unity interface shim. This exercises
// optional extension injection, D3D11 import, split keyed-mutex acquire/release
// submissions and external image ownership/layout transitions. No Unity editor
// process is needed. Exit 77 means that this GPU lacks the required extensions.
#include "VulkanInterop.h"
#include <wrl/client.h>
#include <array>
#include <cstdio>

using Microsoft::WRL::ComPtr;

namespace
{
UnityVulkanInstance instance{};
IUnityGraphicsVulkan graphics{};
PFN_vkGetInstanceProcAddr loader = nullptr;
PFN_vkGetInstanceProcAddr intercepted = nullptr;

UnityVulkanInstance UNITY_INTERFACE_API Instance() { return instance; }
bool UNITY_INTERFACE_API Intercept(UnityVulkanInitCallback callback, void* data)
{
    intercepted = callback(loader, data);
    return intercepted != nullptr;
}
IUnityInterface* UNITY_INTERFACE_API GetInterface(UnityInterfaceGUID guid)
{
    return guid == GetUnityInterfaceGUID<IUnityGraphicsVulkan>() ? &graphics : nullptr;
}
template<typename T> T Function(const char* name)
{
    return reinterpret_cast<T>(intercepted(instance.instance, name));
}

int TestInterop()
{
    VulkanInterop interop;
    if (!interop.Initialize())
    {
        std::printf("SKIP: %s\n", interop.LastError());
        return 77;
    }
    LUID luid{};
    if (!VulkanInterop::GetAdapterLuid(luid))
        return 1;
    ComPtr<IDXGIFactory1> factory;
    if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory))))
        return 1;
    ComPtr<IDXGIAdapter1> matchingAdapter;
    for (UINT index = 0;; ++index)
    {
        ComPtr<IDXGIAdapter1> adapter;
        if (factory->EnumAdapters1(index, &adapter) == DXGI_ERROR_NOT_FOUND)
            break;
        DXGI_ADAPTER_DESC1 description{};
        if (SUCCEEDED(adapter->GetDesc1(&description)) &&
            std::memcmp(&description.AdapterLuid, &luid, sizeof(luid)) == 0)
        {
            matchingAdapter = adapter;
            break;
        }
    }
    if (!matchingAdapter)
    {
        std::puts("FAIL: Vulkan device LUID has no matching DXGI adapter");
        return 1;
    }
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    if (FAILED(D3D11CreateDevice(matchingAdapter.Get(), D3D_DRIVER_TYPE_UNKNOWN,
        nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0, D3D11_SDK_VERSION,
        &device, nullptr, &context)))
        return 1;

    D3D11_TEXTURE2D_DESC description{};
    description.Width = 4;
    description.Height = 4;
    description.MipLevels = 1;
    description.ArraySize = 1;
    description.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    description.SampleDesc.Count = 1;
    description.Usage = D3D11_USAGE_DEFAULT;
    description.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
    description.MiscFlags = D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX | D3D11_RESOURCE_MISC_SHARED_NTHANDLE;
    ComPtr<ID3D11Texture2D> resource;
    ComPtr<ID3D11RenderTargetView> renderTarget;
    ComPtr<IDXGIKeyedMutex> mutex;
    if (FAILED(device->CreateTexture2D(&description, nullptr, &resource)) ||
        FAILED(device->CreateRenderTargetView(resource.Get(), nullptr, &renderTarget)) ||
        FAILED(resource.As(&mutex)))
    {
        std::puts("FAIL: unable to create a D3D11 keyed NT-handle surface");
        interop.Shutdown();
        return 1;
    }
    // Staging is only used to verify data preservation after Vulkan returns
    // ownership. Production video has no staging resource or CPU pixel copy.
    description.Usage = D3D11_USAGE_STAGING;
    description.BindFlags = 0;
    description.MiscFlags = 0;
    description.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    ComPtr<ID3D11Texture2D> staging;
    if (FAILED(device->CreateTexture2D(&description, nullptr, &staging)))
        return 1;

    VulkanInterop::Texture texture;
    if (!interop.RegisterTexture(resource.Get(), texture))
    {
        std::printf("FAIL: %s\n", interop.LastError());
        interop.Shutdown();
        return 1;
    }
    auto createView = Function<PFN_vkCreateImageView>("vkCreateImageView");
    auto destroyView = Function<PFN_vkDestroyImageView>("vkDestroyImageView");
    VkImageViewCreateInfo viewInfo{VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO};
    viewInfo.image = texture.image;
    viewInfo.viewType = VK_IMAGE_VIEW_TYPE_2D;
    viewInfo.format = VK_FORMAT_R8G8B8A8_UNORM;
    viewInfo.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
    VkImageView view = VK_NULL_HANDLE;
    int result = 0;
    if (createView(instance.device, &viewInfo, nullptr, &view) != VK_SUCCESS)
    {
        std::puts("FAIL: Vulkan could not create a sampled view of the imported texture");
        result = 1;
    }
    for (int frame = 0; result == 0 && frame < 32; ++frame)
    {
        const std::array<float, 4> color = frame % 2
            ? std::array<float, 4>{0.75f, 0.25f, 0.5f, 1.0f}
            : std::array<float, 4>{0.25f, 0.5f, 0.75f, 1.0f};
        if (mutex->AcquireSync(0, 1000) != S_OK)
        {
            std::puts("FAIL: D3D11 producer could not reacquire the keyed mutex");
            result = 1;
            break;
        }
        context->ClearRenderTargetView(renderTarget.Get(), color.data());
        context->Flush();
        if (FAILED(mutex->ReleaseSync(0)) || !interop.Lock(texture) || !interop.Unlock(texture))
        {
            std::printf("FAIL: Vulkan split acquire/release: %s\n", interop.LastError());
            result = 1;
            break;
        }
        if (mutex->AcquireSync(0, 1000) != S_OK)
        {
            std::puts("FAIL: Vulkan did not return the keyed mutex to D3D11");
            result = 1;
            break;
        }
        context->CopyResource(staging.Get(), resource.Get());
        D3D11_MAPPED_SUBRESOURCE mapped{};
        if (FAILED(context->Map(staging.Get(), 0, D3D11_MAP_READ, 0, &mapped)))
            result = 1;
        else
        {
            for (size_t y = 0; y < 4; ++y)
                for (size_t x = 0; x < 4; ++x)
                    for (size_t channel = 0; channel < 4; ++channel)
                    {
                        const int expected = static_cast<int>(color[channel] * 255.0f + 0.5f);
                        const auto* row = static_cast<const unsigned char*>(mapped.pData) + y * mapped.RowPitch;
                        const int difference = row[x * 4 + channel] - expected;
                        if (difference < -1 || difference > 1)
                        {
                            std::printf("FAIL: Vulkan ownership/layout handoff changed frame %d pixel data\n", frame);
                            result = 1;
                        }
                    }
            context->Unmap(staging.Get(), 0);
        }
        context->Flush();
        if (FAILED(mutex->ReleaseSync(0)))
            result = 1;
    }
    if (view) destroyView(instance.device, view, nullptr);
    if (!interop.UnregisterTexture(texture) || !interop.Shutdown())
        result = 1;
    if (!result)
        std::puts("PASS: D3D11 import, sampled VkImageView, 32 split keyed-mutex submissions, pixel preservation and cleanup");
    return result;
}
}

int main()
{
    HMODULE vulkan = LoadLibraryW(L"vulkan-1.dll");
    if (!vulkan)
    {
        std::puts("SKIP: Vulkan loader is unavailable");
        return 77;
    }
    loader = reinterpret_cast<PFN_vkGetInstanceProcAddr>(GetProcAddress(vulkan, "vkGetInstanceProcAddr"));
    if (!loader) return 1;
    graphics.Instance = Instance;
    graphics.InterceptInitialization = Intercept;
    IUnityInterfaces interfaces{};
    interfaces.GetInterface = GetInterface;
    VulkanInterop::InstallHook(&interfaces);
    VkApplicationInfo application{VK_STRUCTURE_TYPE_APPLICATION_INFO};
    application.pApplicationName = "VLCUnity Vulkan interop smoke";
    application.apiVersion = VK_API_VERSION_1_1;
    VkInstanceCreateInfo instanceInfo{VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO};
    instanceInfo.pApplicationInfo = &application;
    auto createInstance = Function<PFN_vkCreateInstance>("vkCreateInstance");
    if (createInstance(&instanceInfo, nullptr, &instance.instance) != VK_SUCCESS)
    {
        std::puts("SKIP: Vulkan 1.1 instance creation failed");
        return 77;
    }
    instance.getInstanceProcAddr = intercepted;
    auto enumerate = Function<PFN_vkEnumeratePhysicalDevices>("vkEnumeratePhysicalDevices");
    auto queueProperties = Function<PFN_vkGetPhysicalDeviceQueueFamilyProperties>("vkGetPhysicalDeviceQueueFamilyProperties");
    auto deviceProperties = Function<PFN_vkGetPhysicalDeviceProperties>("vkGetPhysicalDeviceProperties");
    uint32_t count = 0;
    enumerate(instance.instance, &count, nullptr);
    if (!count) return 77;
    std::vector<VkPhysicalDevice> devices(count);
    enumerate(instance.instance, &count, devices.data());
    instance.physicalDevice = devices.front();
    VkPhysicalDeviceProperties properties{};
    deviceProperties(instance.physicalDevice, &properties);
    std::printf("Vulkan device: %s\n", properties.deviceName);
    uint32_t queueCount = 0;
    queueProperties(instance.physicalDevice, &queueCount, nullptr);
    std::vector<VkQueueFamilyProperties> families(queueCount);
    queueProperties(instance.physicalDevice, &queueCount, families.data());
    uint32_t queueIndex = 0;
    while (queueIndex < queueCount && !(families[queueIndex].queueFlags & VK_QUEUE_GRAPHICS_BIT))
        ++queueIndex;
    if (queueIndex == queueCount) return 77;
    instance.queueFamilyIndex = queueIndex;
    float priority = 1.0f;
    VkDeviceQueueCreateInfo queueInfo{VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO};
    queueInfo.queueFamilyIndex = queueIndex;
    queueInfo.queueCount = 1;
    queueInfo.pQueuePriorities = &priority;
    VkDeviceCreateInfo deviceInfo{VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO};
    deviceInfo.queueCreateInfoCount = 1;
    deviceInfo.pQueueCreateInfos = &queueInfo;
    auto createDevice = Function<PFN_vkCreateDevice>("vkCreateDevice");
    if (createDevice(instance.physicalDevice, &deviceInfo, nullptr, &instance.device) != VK_SUCCESS)
        return 1;
    auto getQueue = Function<PFN_vkGetDeviceQueue>("vkGetDeviceQueue");
    getQueue(instance.device, queueIndex, 0, &instance.graphicsQueue);
    const int result = TestInterop();
    Function<PFN_vkDeviceWaitIdle>("vkDeviceWaitIdle")(instance.device);
    Function<PFN_vkDestroyDevice>("vkDestroyDevice")(instance.device, nullptr);
    Function<PFN_vkDestroyInstance>("vkDestroyInstance")(instance.instance, nullptr);
    FreeLibrary(vulkan);
    return result;
}
