// Exercise the actual preload interceptor with a mock Vulkan loader. No Vulkan
// device or display is needed; GL libraries only satisfy the backend linkage.
#include "PortableLinuxInterop.cpp"
#include <cstdio>
#include <stdexcept>

namespace
{
bool extensionsAvailable = true, rejectAugmented = false, expectAugmented = false;
uint32_t physicalVersion = VK_API_VERSION_1_1;
unsigned deviceCalls = 0, originalExtensionCount = 0;
const auto mockDevice = reinterpret_cast<VkDevice>(uintptr_t{0x1234});

void Check(bool valid, const char* message)
{
    if (!valid) throw std::runtime_error(message);
}
VKAPI_ATTR VkResult VKAPI_CALL MockInstance(const VkInstanceCreateInfo*, const VkAllocationCallbacks*, VkInstance* instance)
{
    *instance = reinterpret_cast<VkInstance>(uintptr_t{1});
    return VK_SUCCESS;
}
VKAPI_ATTR void VKAPI_CALL MockProperties(VkPhysicalDevice, VkPhysicalDeviceProperties* properties)
{ properties->apiVersion = physicalVersion; }
VKAPI_ATTR VkResult VKAPI_CALL MockExtensions(VkPhysicalDevice, const char*, uint32_t* count, VkExtensionProperties* properties)
{
    const auto size = static_cast<uint32_t>(std::size(requiredExtensions)) - (extensionsAvailable ? 0u : 1u);
    if (!properties) { *count = size; return VK_SUCCESS; }
    for (uint32_t i = 0; i < std::min(*count, size); ++i)
        std::strcpy(properties[i].extensionName, requiredExtensions[i]);
    *count = size;
    return VK_SUCCESS;
}
VKAPI_ATTR VkResult VKAPI_CALL MockDevice(VkPhysicalDevice, const VkDeviceCreateInfo* info,
    const VkAllocationCallbacks*, VkDevice* device)
{
    ++deviceCalls;
    if (deviceCalls == 1 && expectAugmented) {
        Check(info->ppEnabledExtensionNames && info->enabledExtensionCount >= std::size(requiredExtensions),
            "The augmented request omitted required extensions");
        for (const auto* extension : requiredExtensions)
            Check(std::any_of(info->ppEnabledExtensionNames, info->ppEnabledExtensionNames + info->enabledExtensionCount,
                [extension](const char* name) { return std::strcmp(name, extension) == 0; }),
                "The successful augmented request omitted a required interop extension");
    } else
        Check(info->enabledExtensionCount == originalExtensionCount, "Fallback did not preserve Unity's original extensions");
    if (rejectAugmented && info->enabledExtensionCount > originalExtensionCount)
        return VK_ERROR_EXTENSION_NOT_PRESENT;
    for (uint32_t i = 0; i < info->enabledExtensionCount; ++i)
        for (uint32_t j = i + 1; j < info->enabledExtensionCount; ++j)
            Check(std::strcmp(info->ppEnabledExtensionNames[i], info->ppEnabledExtensionNames[j]) != 0,
                "The interceptor duplicated an already enabled extension");
    *device = mockDevice;
    return VK_SUCCESS;
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL MockProc(VkInstance, const char* name)
{
    if (std::strcmp(name, "vkCreateInstance") == 0) return reinterpret_cast<PFN_vkVoidFunction>(MockInstance);
    if (std::strcmp(name, "vkCreateDevice") == 0) return reinterpret_cast<PFN_vkVoidFunction>(MockDevice);
    if (std::strcmp(name, "vkEnumerateDeviceExtensionProperties") == 0) return reinterpret_cast<PFN_vkVoidFunction>(MockExtensions);
    if (std::strcmp(name, "vkGetPhysicalDeviceProperties") == 0) return reinterpret_cast<PFN_vkVoidFunction>(MockProperties);
    return nullptr;
}
void Run(uint32_t api, bool available, bool reject, bool alreadyEnabled, bool expectInterop, unsigned expectedCalls)
{
    extensionsAvailable = available; rejectAugmented = reject; deviceCalls = 0;
    expectAugmented = available && api >= VK_API_VERSION_1_1 && physicalVersion >= VK_API_VERSION_1_1;
    auto proc = Intercept(MockProc, nullptr);
    Check(proc(VK_NULL_HANDLE, "missingFunction") == nullptr, "Unknown loader functions must remain null");
    VkApplicationInfo application{VK_STRUCTURE_TYPE_APPLICATION_INFO}; application.apiVersion = api;
    VkInstanceCreateInfo instanceInfo{VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO}; instanceInfo.pApplicationInfo = &application;
    VkInstance instance = VK_NULL_HANDLE;
    auto createInstance = reinterpret_cast<PFN_vkCreateInstance>(proc(VK_NULL_HANDLE, "vkCreateInstance"));
    Check(createInstance(&instanceInfo, nullptr, &instance) == VK_SUCCESS, "Instance creation failed");
    VkDeviceCreateInfo info{VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO};
    if (alreadyEnabled) { info.enabledExtensionCount = 1; info.ppEnabledExtensionNames = requiredExtensions; }
    originalExtensionCount = info.enabledExtensionCount;
    VkDevice device = VK_NULL_HANDLE;
    auto createDevice = reinterpret_cast<PFN_vkCreateDevice>(proc(instance, "vkCreateDevice"));
    Check(createDevice(VK_NULL_HANDLE, &info, nullptr, &device) == VK_SUCCESS, "Optional interop prevented device creation");
    Check(device == mockDevice && deviceCalls == expectedCalls, "Unexpected original/fallback device creation count");
    Check((enabledDevice == device) == expectInterop, "Incorrect capability, possibly a reused stale device handle");
    Check(info.enabledExtensionCount == originalExtensionCount, "Unity's original request was mutated");
}
}

int main()
{
    try {
        Run(VK_API_VERSION_1_1, true, false, false, true, 1); // Empty extension array.
        Run(VK_API_VERSION_1_1, true, false, true, true, 1);  // No duplicates.
        Run(VK_API_VERSION_1_1, true, true, false, false, 2); // Retry unmodified request; reused handle is not enabled.
        Run(VK_API_VERSION_1_1, false, false, false, false, 1);
        Run(VK_API_VERSION_1_0, true, false, false, false, 1);
        physicalVersion = VK_API_VERSION_1_0;
        Run(VK_API_VERSION_1_1, true, false, false, false, 1);
        std::puts("PASS: Linux Vulkan preload, optional-extension fallback, API versions and reused device handles");
        return 0;
    } catch (const std::exception& error) {
        std::fprintf(stderr, "FAIL: %s\n", error.what());
        return 1;
    }
}
