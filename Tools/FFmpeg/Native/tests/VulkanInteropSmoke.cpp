// Uses real D3D11 shared memory and Vulkan driver. Readback exists only here to
// validate pixels; the production helper never maps or reads image pixels.
#define VK_NO_PROTOTYPES
#define VK_USE_PLATFORM_WIN32_KHR
#include "../VulkanInterop.h"
#include "IUnityGraphicsVulkan.h"
#include <cstdio>
#include <cstring>
#include <vector>
#define CHECK(x) do { if (!(x)) { std::printf("FAIL line %d: %s\n", __LINE__, #x); return 1; } } while (0)
static UnityVulkanInitCallback interceptor = nullptr;
static UnityVulkanInstance instance{};
static UnityVulkanImage target{};
static IUnityGraphicsVulkanV2 graphics{};
static bool deferQueue = false;
static UnityRenderingEventAndData deferredCallback = nullptr;
static int deferredId = 0;
static void* deferredData = nullptr;
int FfuEventId(int event) { return 1000 + event; }
static bool UNITY_INTERFACE_API Intercept(UnityVulkanInitCallback callback, void*) { interceptor = callback; return true; }
static UnityVulkanInstance UNITY_INTERFACE_API Instance() { return instance; }
static bool UNITY_INTERFACE_API Access(void*, const VkImageSubresource*, VkImageLayout, VkPipelineStageFlags,
    VkAccessFlags, UnityVulkanResourceAccessMode, UnityVulkanImage* result) { *result = target; return true; }
static void UNITY_INTERFACE_API Queue(UnityRenderingEventAndData callback, int id, void* data, bool) {
    if (deferQueue) { deferredCallback = callback; deferredId = id; deferredData = data; }
    else callback(id, data);
}
static IUnityInterface* UNITY_INTERFACE_API Interface(unsigned long long high, unsigned long long low) {
    const auto id = GetUnityInterfaceGUID<IUnityGraphicsVulkanV2>();
    return high == id.m_GUIDHigh && low == id.m_GUIDLow ? &graphics : nullptr;
}
int main() {
    setvbuf(stdout, nullptr, _IONBF, 0);
    HMODULE module = LoadLibraryW(L"vulkan-1.dll"); CHECK(module);
    auto get = reinterpret_cast<PFN_vkGetInstanceProcAddr>(GetProcAddress(module, "vkGetInstanceProcAddr")); CHECK(get);
    graphics.InterceptInitialization = Intercept; graphics.Instance = Instance; graphics.AccessTexture = Access; graphics.AccessQueue = Queue;
    IUnityInterfaces interfaces{}; interfaces.GetInterfaceSplit = Interface;
    FfuVulkanPreload(&interfaces); CHECK(interceptor);
    auto wrapped = interceptor(get, nullptr);
    auto createInstance = reinterpret_cast<PFN_vkCreateInstance>(wrapped(VK_NULL_HANDLE, "vkCreateInstance")); CHECK(createInstance);
    VkApplicationInfo app{}; app.sType = VK_STRUCTURE_TYPE_APPLICATION_INFO; app.apiVersion = VK_API_VERSION_1_1;
    VkInstanceCreateInfo create{}; create.sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO; create.pApplicationInfo = &app;
    CHECK(createInstance(&create, nullptr, &instance.instance) == VK_SUCCESS);
    instance.getInstanceProcAddr = get;
#define INSTANCE(name) auto name = reinterpret_cast<PFN_vk##name>(get(instance.instance, "vk" #name)); CHECK(name)
    INSTANCE(EnumeratePhysicalDevices); INSTANCE(GetPhysicalDeviceQueueFamilyProperties); INSTANCE(GetPhysicalDeviceMemoryProperties);
    INSTANCE(GetDeviceProcAddr); INSTANCE(DestroyInstance); INSTANCE(GetPhysicalDeviceProperties);
    uint32_t count = 0; CHECK(EnumeratePhysicalDevices(instance.instance, &count, nullptr) == VK_SUCCESS && count);
    std::vector<VkPhysicalDevice> physical(count); CHECK(EnumeratePhysicalDevices(instance.instance, &count, physical.data()) == VK_SUCCESS);
    instance.physicalDevice = physical[0];
    VkPhysicalDeviceProperties props{}; GetPhysicalDeviceProperties(physical[0], &props); std::printf("GPU: %s\n", props.deviceName);
    GetPhysicalDeviceQueueFamilyProperties(physical[0], &count, nullptr); std::vector<VkQueueFamilyProperties> families(count);
    GetPhysicalDeviceQueueFamilyProperties(physical[0], &count, families.data());
    bool found = false;
    for (uint32_t i = 0; i < count; ++i) if (families[i].queueFlags & VK_QUEUE_GRAPHICS_BIT) { instance.queueFamilyIndex = i; found = true; break; }
    CHECK(found);
    float priority = 1;
    VkDeviceQueueCreateInfo queue{}; queue.sType = VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO;
    queue.queueFamilyIndex = instance.queueFamilyIndex; queue.queueCount = 1; queue.pQueuePriorities = &priority;
    VkDeviceCreateInfo dev{}; dev.sType = VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO; dev.queueCreateInfoCount = 1; dev.pQueueCreateInfos = &queue;
    auto createDevice = reinterpret_cast<PFN_vkCreateDevice>(wrapped(instance.instance, "vkCreateDevice")); CHECK(createDevice);
    CHECK(createDevice(physical[0], &dev, nullptr, &instance.device) == VK_SUCCESS);
#define DEVICE(name) auto name = reinterpret_cast<PFN_vk##name>(GetDeviceProcAddr(instance.device, "vk" #name)); CHECK(name)
    DEVICE(GetDeviceQueue); DEVICE(CreateImage); DEVICE(DestroyImage); DEVICE(GetImageMemoryRequirements);
    DEVICE(AllocateMemory); DEVICE(FreeMemory); DEVICE(BindImageMemory); DEVICE(CreateCommandPool); DEVICE(DestroyCommandPool);
    DEVICE(AllocateCommandBuffers); DEVICE(BeginCommandBuffer); DEVICE(EndCommandBuffer); DEVICE(CmdPipelineBarrier);
    DEVICE(QueueSubmit); DEVICE(QueueWaitIdle); DEVICE(CreateBuffer); DEVICE(DestroyBuffer); DEVICE(GetBufferMemoryRequirements);
    DEVICE(BindBufferMemory); DEVICE(CmdCopyImageToBuffer); DEVICE(MapMemory); DEVICE(UnmapMemory); DEVICE(DestroyDevice);
    GetDeviceQueue(instance.device, instance.queueFamilyIndex, 0, &instance.graphicsQueue);
    ID3D11Device* d3d = FfuVulkanInitialize(&interfaces);
    if (!d3d) std::printf("Initialization error: %d\n", FfuVulkanError(nullptr));
    CHECK(d3d);
    constexpr uint32_t width = 64, height = 48;
    D3D11_TEXTURE2D_DESC desc{}; desc.Width = width; desc.Height = height; desc.MipLevels = desc.ArraySize = 1;
    desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM; desc.SampleDesc.Count = 1;
    desc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
    desc.MiscFlags = D3D11_RESOURCE_MISC_SHARED_NTHANDLE | D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX;
    ID3D11Texture2D* shared = nullptr; CHECK(SUCCEEDED(d3d->CreateTexture2D(&desc, nullptr, &shared)));
    void* surface = FfuVulkanImport(shared); CHECK(surface);
    ID3D11DeviceContext* immediate = nullptr; d3d->GetImmediateContext(&immediate);
    VkPhysicalDeviceMemoryProperties memory{}; GetPhysicalDeviceMemoryProperties(physical[0], &memory);
    auto memoryType = [&](uint32_t bits, VkMemoryPropertyFlags flags) {
        for (uint32_t i = 0; i < memory.memoryTypeCount; ++i)
            if ((bits & (1u << i)) && (memory.memoryTypes[i].propertyFlags & flags) == flags) return i;
        return UINT32_MAX;
    };
    VkImageCreateInfo image{}; image.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO; image.imageType = VK_IMAGE_TYPE_2D;
    image.format = VK_FORMAT_R8G8B8A8_UNORM; image.extent = {width, height, 1}; image.mipLevels = image.arrayLayers = 1;
    image.samples = VK_SAMPLE_COUNT_1_BIT; image.tiling = VK_IMAGE_TILING_OPTIMAL;
    image.usage = VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
    CHECK(CreateImage(instance.device, &image, nullptr, &target.image) == VK_SUCCESS);
    target.format = image.format; target.extent = image.extent; target.usage = image.usage; target.layout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
    VkMemoryRequirements requirements{}; GetImageMemoryRequirements(instance.device, target.image, &requirements);
    VkMemoryAllocateInfo allocate{}; allocate.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO; allocate.allocationSize = requirements.size;
    allocate.memoryTypeIndex = memoryType(requirements.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT); CHECK(allocate.memoryTypeIndex != UINT32_MAX);
    VkDeviceMemory imageMemory = VK_NULL_HANDLE; CHECK(AllocateMemory(instance.device, &allocate, nullptr, &imageMemory) == VK_SUCCESS);
    CHECK(BindImageMemory(instance.device, target.image, imageMemory, 0) == VK_SUCCESS);
    VkBufferCreateInfo buffer{}; buffer.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO; buffer.size = width * height * 4; buffer.usage = VK_BUFFER_USAGE_TRANSFER_DST_BIT;
    VkBuffer readback = VK_NULL_HANDLE; CHECK(CreateBuffer(instance.device, &buffer, nullptr, &readback) == VK_SUCCESS);
    GetBufferMemoryRequirements(instance.device, readback, &requirements);
    allocate.allocationSize = requirements.size; allocate.memoryTypeIndex = memoryType(requirements.memoryTypeBits, VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT);
    CHECK(allocate.memoryTypeIndex != UINT32_MAX);
    VkDeviceMemory readMemory = VK_NULL_HANDLE; CHECK(AllocateMemory(instance.device, &allocate, nullptr, &readMemory) == VK_SUCCESS);
    CHECK(BindBufferMemory(instance.device, readback, readMemory, 0) == VK_SUCCESS);
    VkCommandPoolCreateInfo pool{}; pool.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO; pool.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
    pool.queueFamilyIndex = instance.queueFamilyIndex;
    VkCommandPool commandPool = VK_NULL_HANDLE; CHECK(CreateCommandPool(instance.device, &pool, nullptr, &commandPool) == VK_SUCCESS);
    VkCommandBufferAllocateInfo commandAllocate{}; commandAllocate.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
    commandAllocate.commandPool = commandPool; commandAllocate.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY; commandAllocate.commandBufferCount = 1;
    VkCommandBuffer command = VK_NULL_HANDLE; CHECK(AllocateCommandBuffers(instance.device, &commandAllocate, &command) == VK_SUCCESS);
    auto beginCommand = [&]() {
        VkCommandBufferBeginInfo start{}; start.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO; start.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
        return BeginCommandBuffer(command, &start) == VK_SUCCESS;
    };
    auto submitCommand = [&]() {
        if (EndCommandBuffer(command) != VK_SUCCESS) return false;
        VkSubmitInfo submit{}; submit.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO; submit.commandBufferCount = 1; submit.pCommandBuffers = &command;
        return QueueSubmit(instance.graphicsQueue, 1, &submit, VK_NULL_HANDLE) == VK_SUCCESS && QueueWaitIdle(instance.graphicsQueue) == VK_SUCCESS;
    };
    auto barrier = [&](VkImageLayout before, VkImageLayout after, VkAccessFlags source, VkAccessFlags destination) {
        VkImageMemoryBarrier change{}; change.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        change.srcAccessMask = source; change.dstAccessMask = destination; change.oldLayout = before; change.newLayout = after;
        change.srcQueueFamilyIndex = change.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        change.image = target.image; change.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
        CmdPipelineBarrier(command, VK_PIPELINE_STAGE_ALL_COMMANDS_BIT, VK_PIPELINE_STAGE_ALL_COMMANDS_BIT, 0, 0, nullptr, 0, nullptr, 1, &change);
    };
    CHECK(beginCommand()); barrier(VK_IMAGE_LAYOUT_UNDEFINED, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 0, VK_ACCESS_TRANSFER_WRITE_BIT); CHECK(submitCommand());
    std::vector<uint8_t> pixels(width * height * 4);
    for (int iteration = 0; iteration < 64; ++iteration) {
        for (uint32_t y = 0; y < height; ++y) for (uint32_t x = 0; x < width; ++x) {
            const size_t p = (y * width + x) * 4;
            pixels[p] = static_cast<uint8_t>(iteration * 13 + x); pixels[p + 1] = static_cast<uint8_t>(y * 4);
            pixels[p + 2] = static_cast<uint8_t>(x ^ y); pixels[p + 3] = 255;
        }
        CHECK(FfuVulkanBeginWrite(surface)); immediate->UpdateSubresource(shared, 0, nullptr, pixels.data(), width * 4, 0);
        deferQueue = iteration == 63;
        CHECK(FfuVulkanEndWrite(surface, &target));
        if (deferQueue) {
            // Simulate Unity's asynchronous submission thread after managed
            // code releases its output: the queued job must own the surface.
            FfuVulkanRelease(surface); surface = nullptr;
            shared->Release(); shared = nullptr;
            CHECK(deferredCallback); deferredCallback(deferredId, deferredData);
        }
        // BeginWrite must retire preceding completed jobs without an explicit
        // drain: this loop exceeds the helper's 32-packet resource bound.
        CHECK(!surface || FfuVulkanError(surface) == 0);
        CHECK(beginCommand()); barrier(VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_ACCESS_TRANSFER_WRITE_BIT, VK_ACCESS_TRANSFER_READ_BIT);
        VkBufferImageCopy copy{}; copy.imageSubresource = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1}; copy.imageExtent = {width, height, 1};
        CmdCopyImageToBuffer(command, target.image, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, readback, 1, &copy);
        barrier(VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_ACCESS_TRANSFER_READ_BIT, VK_ACCESS_TRANSFER_WRITE_BIT);
        CHECK(submitCommand());
        void* data = nullptr; CHECK(MapMemory(instance.device, readMemory, 0, pixels.size(), 0, &data) == VK_SUCCESS);
        const bool same = std::memcmp(data, pixels.data(), pixels.size()) == 0;
        if (!same) std::printf("MISMATCH iteration %d expected %u,%u,%u,%u actual %u,%u,%u,%u\n", iteration,
            pixels[0], pixels[1], pixels[2], pixels[3], static_cast<uint8_t*>(data)[0], static_cast<uint8_t*>(data)[1], static_cast<uint8_t*>(data)[2], static_cast<uint8_t*>(data)[3]);
        UnmapMemory(instance.device, readMemory); CHECK(same);
    }
    FfuVulkanPoll(true); FfuVulkanRelease(surface); FfuVulkanShutdown();
    immediate->Release(); if (shared) shared->Release(); d3d->Release();
    DestroyCommandPool(instance.device, commandPool, nullptr); DestroyImage(instance.device, target.image, nullptr);
    FreeMemory(instance.device, imageMemory, nullptr); DestroyBuffer(instance.device, readback, nullptr); FreeMemory(instance.device, readMemory, nullptr);
    DestroyDevice(instance.device, nullptr); DestroyInstance(instance.instance, nullptr); FreeLibrary(module);
    std::puts("PASS: 64 D3D11 NT shared RGBA frames, keyed-mutex GPU copies, automatic retirement, async release, 786432 exact bytes verified");
    return 0;
}
