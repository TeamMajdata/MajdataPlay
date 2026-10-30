// Hardware probe for the pacing layer's driver low-latency backends, VK_AMD_anti_lag in particular.
// It loads MajdataPlayVulkanPacing.dll from its own folder, registers it the way Unity does, and drives a real
// Vulkan FIFO swapchain through it: a window, a device created like Unity 6 creates its own, and frames that only
// clear the image. Each mode runs the same frames and reports where the frame waits and, when the device has
// present waiting, how long each frame took from input sampling until it was shown. When a pacing backend works,
// the wait moves from presentation (after input is sampled) to the frame start (before it), so "sample->shown"
// drops. Windows only.
//
// MajdataPlayVulkanPacingProbe.exe [--require amd|nvidia|any] [--frames N] [--work-ms M] [--gpu INDEX]
// Exit code: 0 done, 1 Vulkan or layer error, 2 the required backend is not active.
#define VK_NO_PROTOTYPES
#define VK_USE_PLATFORM_WIN32_KHR
#include <windows.h>
#include <vulkan/vulkan.h>
#include "IUnityInterface.h"
#include "IUnityGraphics.h"
#include "IUnityGraphicsVulkan.h"
#include <algorithm>
#include <atomic>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <deque>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace {
// The layer's exports (VulkanPacing.h), resolved from the DLL at run time.
struct Layer {
    void (UNITY_INTERFACE_API* pluginLoad)(IUnityInterfaces*) = nullptr;
    int (__cdecl* abiVersion)() = nullptr;
    int (__cdecl* presentWaitStatus)() = nullptr;
    int (__cdecl* presentWaitQuery)(uint64_t*, uint64_t*) = nullptr;
    int (__cdecl* presentWaitPoll)(uint64_t, uint64_t) = nullptr;
    int (__cdecl* lowLatencyBackend)() = nullptr;
    int (__cdecl* lowLatencyStatus)() = nullptr;
    int (__cdecl* frameBegin)(uint64_t) = nullptr;
    int (__cdecl* frameEnd)() = nullptr;
};
Layer layer;

// Unity's side of the interceptor registration.
UnityVulkanInitCallback interceptor = nullptr;
IUnityGraphicsVulkanV2 unityVulkan{};
bool UNITY_INTERFACE_API AddIntercept(UnityVulkanInitCallback callback, void*, int32_t) { interceptor = callback; return true; }
IUnityInterface* UNITY_INTERFACE_API GetInterfaceSplit(unsigned long long high, unsigned long long low) {
    const auto id = GetUnityInterfaceGUID<IUnityGraphicsVulkanV2>();
    return high == id.m_GUIDHigh && low == id.m_GUIDLow ? reinterpret_cast<IUnityInterface*>(&unityVulkan) : nullptr;
}

#define CHECK(call) do { const VkResult checkResult = (call); if (checkResult != VK_SUCCESS) { \
    std::printf("FAIL: %s returned %d (line %d)\n", #call, static_cast<int>(checkResult), __LINE__); std::exit(1); } } while (0)

double Now() {
    static LARGE_INTEGER frequency = [] { LARGE_INTEGER value; QueryPerformanceFrequency(&value); return value; }();
    LARGE_INTEGER counter;
    QueryPerformanceCounter(&counter);
    return counter.QuadPart * 1000.0 / frequency.QuadPart;
}
void BusyWork(double milliseconds) {
    const double end = Now() + milliseconds;
    while (Now() < end) {}
}

// Per-mode timings in milliseconds.
struct Stats {
    std::vector<double> pacing, sampleToPresent, acquire, present, interval, sampleToShown;
};

// Polls presented frames on a second thread, through the layer's thread-safe non-blocking poll, and records
// when each one is shown.
struct ShownTracker {
    struct Pending { uint64_t generation, id; double sampled; };
    std::mutex mutex;
    std::deque<Pending> pending;
    std::vector<double>* results = nullptr;
    std::atomic<bool> running{true};
    std::thread thread;

    void Start() {
        thread = std::thread([this] {
            while (running.load()) {
                Pending next{};
                {
                    std::lock_guard<std::mutex> lock(mutex);
                    if (pending.empty()) { next.id = 0; } else next = pending.front();
                }
                if (next.id == 0) { SwitchToThread(); continue; }
                const int result = layer.presentWaitPoll(next.generation, next.id);
                if (result == 0) { SwitchToThread(); continue; }
                const double shown = Now();
                std::lock_guard<std::mutex> lock(mutex);
                if (result == 1 && results) results->push_back(shown - next.sampled);
                pending.pop_front();
            }
        });
    }
    void Add(uint64_t generation, uint64_t id, double sampled) {
        std::lock_guard<std::mutex> lock(mutex);
        pending.push_back({generation, id, sampled});
    }
    // Waits until every presented frame was seen, then switches the destination of new results.
    void Drain(std::vector<double>* next) {
        const double end = Now() + 500;
        while (Now() < end) {
            {
                std::lock_guard<std::mutex> lock(mutex);
                if (pending.empty()) { results = next; return; }
            }
            Sleep(1);
        }
        std::lock_guard<std::mutex> lock(mutex);
        pending.clear();
        results = next;
    }
    void Stop() { running = false; if (thread.joinable()) thread.join(); }
};
double Average(const std::vector<double>& values) {
    double sum = 0;
    for (double value : values) sum += value;
    return values.empty() ? 0 : sum / values.size();
}
double Percentile(std::vector<double> values, double fraction) {
    if (values.empty()) return 0;
    std::sort(values.begin(), values.end());
    return values[static_cast<size_t>(fraction * (values.size() - 1))];
}

const char* BackendName(int backend) {
    return backend == 1 ? "VK_NV_low_latency2" : backend == 2 ? "VK_AMD_anti_lag" : "none";
}

LRESULT CALLBACK WindowProcedure(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    return DefWindowProcW(window, message, wParam, lParam);
}
} // namespace

int main(int argc, char** argv) {
    std::string require = "any";
    int frames = 300, gpu = -1;
    double workMs = 2.0;
    for (int i = 1; i + 1 < argc; i += 2) {
        if (!std::strcmp(argv[i], "--require")) require = argv[i + 1];
        else if (!std::strcmp(argv[i], "--frames")) frames = std::max(60, std::atoi(argv[i + 1]));
        else if (!std::strcmp(argv[i], "--work-ms")) workMs = std::atof(argv[i + 1]);
        else if (!std::strcmp(argv[i], "--gpu")) gpu = std::atoi(argv[i + 1]);
    }

    // Load the layer next to the executable and register it like Unity's preload does.
    HMODULE layerModule = LoadLibraryW(L"MajdataPlayVulkanPacing.dll");
    HMODULE loaderModule = LoadLibraryW(L"vulkan-1.dll");
    if (!layerModule || !loaderModule) { std::puts("FAIL: MajdataPlayVulkanPacing.dll or vulkan-1.dll not found"); return 1; }
#define RESOLVE(field, name) layer.field = reinterpret_cast<decltype(layer.field)>(GetProcAddress(layerModule, name)); \
    if (!layer.field) { std::printf("FAIL: the layer lacks %s\n", name); return 1; }
    RESOLVE(pluginLoad, "UnityPluginLoad") RESOLVE(abiVersion, "majvk_abi_version")
    RESOLVE(presentWaitStatus, "majvk_present_wait_status") RESOLVE(presentWaitQuery, "majvk_present_wait_query")
    RESOLVE(presentWaitPoll, "majvk_present_wait_poll") RESOLVE(lowLatencyBackend, "majvk_low_latency_backend")
    RESOLVE(lowLatencyStatus, "majvk_low_latency_status") RESOLVE(frameBegin, "majvk_frame_begin") RESOLVE(frameEnd, "majvk_frame_end")
#undef RESOLVE
    unityVulkan.AddInterceptInitialization = AddIntercept;
    IUnityInterfaces interfaces{};
    interfaces.GetInterfaceSplit = GetInterfaceSplit;
    layer.pluginLoad(&interfaces);
    const auto loaderProc = reinterpret_cast<PFN_vkGetInstanceProcAddr>(GetProcAddress(loaderModule, "vkGetInstanceProcAddr"));
    if (!interceptor || !loaderProc) { std::puts("FAIL: the layer did not register an interceptor"); return 1; }
    const PFN_vkGetInstanceProcAddr gipa = interceptor(loaderProc, nullptr);
    std::printf("layer ABI %d\n", layer.abiVersion());

    // Instance at Vulkan 1.1, as Unity 6000.3 creates it.
    const char* instanceExtensions[] = {VK_KHR_SURFACE_EXTENSION_NAME, VK_KHR_WIN32_SURFACE_EXTENSION_NAME};
    VkApplicationInfo application{VK_STRUCTURE_TYPE_APPLICATION_INFO};
    application.pApplicationName = "MajdataPlayVulkanPacingProbe";
    application.apiVersion = VK_API_VERSION_1_1;
    VkInstanceCreateInfo instanceInfo{VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO};
    instanceInfo.pApplicationInfo = &application;
    instanceInfo.enabledExtensionCount = 2;
    instanceInfo.ppEnabledExtensionNames = instanceExtensions;
    VkInstance instance = VK_NULL_HANDLE;
    CHECK(reinterpret_cast<PFN_vkCreateInstance>(gipa(nullptr, "vkCreateInstance"))(&instanceInfo, nullptr, &instance));
#define INSTANCE(name) const auto name = reinterpret_cast<PFN_##name>(gipa(instance, #name))
    INSTANCE(vkEnumeratePhysicalDevices); INSTANCE(vkGetPhysicalDeviceProperties); INSTANCE(vkGetPhysicalDeviceQueueFamilyProperties);
    INSTANCE(vkCreateWin32SurfaceKHR); INSTANCE(vkGetPhysicalDeviceSurfaceSupportKHR); INSTANCE(vkGetPhysicalDeviceSurfaceCapabilitiesKHR);
    INSTANCE(vkGetPhysicalDeviceSurfaceFormatsKHR); INSTANCE(vkEnumerateDeviceExtensionProperties); INSTANCE(vkGetPhysicalDeviceFeatures2);
    INSTANCE(vkCreateDevice); INSTANCE(vkGetDeviceProcAddr); INSTANCE(vkDestroySurfaceKHR); INSTANCE(vkDestroyInstance);
#undef INSTANCE

    // A fixed-size window: recreation is covered by the deterministic tests, not by this probe.
    const HINSTANCE module = GetModuleHandleW(nullptr);
    WNDCLASSW windowClass{};
    windowClass.lpfnWndProc = WindowProcedure;
    windowClass.hInstance = module;
    windowClass.lpszClassName = L"MajdataPlayVulkanPacingProbe";
    RegisterClassW(&windowClass);
    HWND window = CreateWindowW(windowClass.lpszClassName, L"MajdataPlay Vulkan pacing probe",
        WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_VISIBLE, CW_USEDEFAULT, CW_USEDEFAULT, 640, 360, nullptr, nullptr, module, nullptr);
    VkWin32SurfaceCreateInfoKHR surfaceInfo{VK_STRUCTURE_TYPE_WIN32_SURFACE_CREATE_INFO_KHR};
    surfaceInfo.hinstance = module;
    surfaceInfo.hwnd = window;
    VkSurfaceKHR surface = VK_NULL_HANDLE;
    CHECK(vkCreateWin32SurfaceKHR(instance, &surfaceInfo, nullptr, &surface));

    // Physical device: --gpu, else the first discrete GPU, else the first one; plus a graphics+present queue.
    uint32_t count = 0;
    CHECK(vkEnumeratePhysicalDevices(instance, &count, nullptr));
    std::vector<VkPhysicalDevice> physicals(count);
    CHECK(vkEnumeratePhysicalDevices(instance, &count, physicals.data()));
    VkPhysicalDevice physical = VK_NULL_HANDLE;
    for (uint32_t i = 0; i < count; ++i) {
        VkPhysicalDeviceProperties properties{};
        vkGetPhysicalDeviceProperties(physicals[i], &properties);
        std::printf("GPU %u: %s (vendor 0x%04x)\n", i, properties.deviceName, properties.vendorID);
        if (gpu == static_cast<int>(i) || (gpu < 0 && !physical && properties.deviceType == VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU)) physical = physicals[i];
    }
    if (!physical && count) physical = physicals[0];
    if (!physical) { std::puts("FAIL: no Vulkan GPU"); return 1; }
    vkGetPhysicalDeviceQueueFamilyProperties(physical, &count, nullptr);
    std::vector<VkQueueFamilyProperties> families(count);
    vkGetPhysicalDeviceQueueFamilyProperties(physical, &count, families.data());
    uint32_t family = UINT32_MAX;
    for (uint32_t i = 0; i < count && family == UINT32_MAX; ++i) {
        VkBool32 present = VK_FALSE;
        vkGetPhysicalDeviceSurfaceSupportKHR(physical, i, surface, &present);
        if ((families[i].queueFlags & VK_QUEUE_GRAPHICS_BIT) && present) family = i;
    }
    if (family == UINT32_MAX) { std::puts("FAIL: no graphics queue can present to the window"); return 1; }

    // Device: swapchain plus present id/wait with their features when supported, as Unity 6 requests them.
    CHECK(vkEnumerateDeviceExtensionProperties(physical, nullptr, &count, nullptr));
    std::vector<VkExtensionProperties> available(count);
    CHECK(vkEnumerateDeviceExtensionProperties(physical, nullptr, &count, available.data()));
    auto supports = [&](const char* name) {
        return std::any_of(available.begin(), available.end(), [name](const VkExtensionProperties& e) { return !std::strcmp(e.extensionName, name); });
    };
    VkPhysicalDevicePresentIdFeaturesKHR presentId{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PRESENT_ID_FEATURES_KHR};
    VkPhysicalDevicePresentWaitFeaturesKHR presentWait{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PRESENT_WAIT_FEATURES_KHR, &presentId};
    VkPhysicalDeviceFeatures2 features{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2, &presentWait};
    vkGetPhysicalDeviceFeatures2(physical, &features);
    std::vector<const char*> deviceExtensions = {VK_KHR_SWAPCHAIN_EXTENSION_NAME};
    const bool unityPresentWait = supports(VK_KHR_PRESENT_ID_EXTENSION_NAME) && supports(VK_KHR_PRESENT_WAIT_EXTENSION_NAME) &&
        presentId.presentId && presentWait.presentWait;
    if (unityPresentWait) {
        deviceExtensions.push_back(VK_KHR_PRESENT_ID_EXTENSION_NAME);
        deviceExtensions.push_back(VK_KHR_PRESENT_WAIT_EXTENSION_NAME);
    }
    std::printf("driver advertises: VK_AMD_anti_lag=%d VK_NV_low_latency2=%d present_wait=%d\n",
        supports("VK_AMD_anti_lag"), supports(VK_NV_LOW_LATENCY_2_EXTENSION_NAME), unityPresentWait);
    const float priority = 1.0f;
    VkDeviceQueueCreateInfo queueInfo{VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO};
    queueInfo.queueFamilyIndex = family;
    queueInfo.queueCount = 1;
    queueInfo.pQueuePriorities = &priority;
    VkDeviceCreateInfo deviceInfo{VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO};
    deviceInfo.pNext = unityPresentWait ? &presentWait : nullptr;
    deviceInfo.queueCreateInfoCount = 1;
    deviceInfo.pQueueCreateInfos = &queueInfo;
    deviceInfo.enabledExtensionCount = static_cast<uint32_t>(deviceExtensions.size());
    deviceInfo.ppEnabledExtensionNames = deviceExtensions.data();
    VkDevice device = VK_NULL_HANDLE;
    CHECK(vkCreateDevice(physical, &deviceInfo, nullptr, &device));

    const int backend = layer.lowLatencyBackend();
    std::printf("layer: low-latency backend %s (status %d), present wait status %d\n",
        BackendName(backend), layer.lowLatencyStatus(), layer.presentWaitStatus());
    const bool backendOk = require == "any" || (require == "amd" && backend == 2) || (require == "nvidia" && backend == 1);

#define DEVICE(name) const auto name = reinterpret_cast<PFN_##name>(vkGetDeviceProcAddr(device, #name))
    DEVICE(vkGetDeviceQueue); DEVICE(vkCreateSwapchainKHR); DEVICE(vkGetSwapchainImagesKHR); DEVICE(vkAcquireNextImageKHR);
    DEVICE(vkQueuePresentKHR); DEVICE(vkQueueSubmit); DEVICE(vkCreateCommandPool); DEVICE(vkAllocateCommandBuffers);
    DEVICE(vkBeginCommandBuffer); DEVICE(vkEndCommandBuffer); DEVICE(vkCmdPipelineBarrier); DEVICE(vkCmdClearColorImage);
    DEVICE(vkCreateSemaphore); DEVICE(vkCreateFence); DEVICE(vkWaitForFences); DEVICE(vkResetFences); DEVICE(vkResetCommandBuffer);
    DEVICE(vkDeviceWaitIdle); DEVICE(vkDestroySwapchainKHR); DEVICE(vkDestroySemaphore); DEVICE(vkDestroyFence);
    DEVICE(vkDestroyCommandPool); DEVICE(vkDestroyDevice);
#undef DEVICE
    VkQueue queue = VK_NULL_HANDLE;
    vkGetDeviceQueue(device, family, 0, &queue);

    // FIFO swapchain, cleared with a transfer each frame.
    VkSurfaceCapabilitiesKHR capabilities{};
    CHECK(vkGetPhysicalDeviceSurfaceCapabilitiesKHR(physical, surface, &capabilities));
    if (!(capabilities.supportedUsageFlags & VK_IMAGE_USAGE_TRANSFER_DST_BIT)) { std::puts("FAIL: swapchain images cannot be cleared"); return 1; }
    CHECK(vkGetPhysicalDeviceSurfaceFormatsKHR(physical, surface, &count, nullptr));
    std::vector<VkSurfaceFormatKHR> formats(count);
    CHECK(vkGetPhysicalDeviceSurfaceFormatsKHR(physical, surface, &count, formats.data()));
    VkSwapchainCreateInfoKHR swapchainInfo{VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR};
    swapchainInfo.surface = surface;
    swapchainInfo.minImageCount = std::max(2u, capabilities.minImageCount);
    if (capabilities.maxImageCount) swapchainInfo.minImageCount = std::min(swapchainInfo.minImageCount, capabilities.maxImageCount);
    swapchainInfo.imageFormat = formats[0].format;
    swapchainInfo.imageColorSpace = formats[0].colorSpace;
    swapchainInfo.imageExtent = capabilities.currentExtent;
    swapchainInfo.imageArrayLayers = 1;
    swapchainInfo.imageUsage = VK_IMAGE_USAGE_TRANSFER_DST_BIT;
    swapchainInfo.imageSharingMode = VK_SHARING_MODE_EXCLUSIVE;
    swapchainInfo.preTransform = capabilities.currentTransform;
    swapchainInfo.compositeAlpha = (capabilities.supportedCompositeAlpha & VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR)
        ? VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR : static_cast<VkCompositeAlphaFlagBitsKHR>(capabilities.supportedCompositeAlpha & -capabilities.supportedCompositeAlpha);
    swapchainInfo.presentMode = VK_PRESENT_MODE_FIFO_KHR;
    swapchainInfo.clipped = VK_TRUE;
    VkSwapchainKHR swapchain = VK_NULL_HANDLE;
    CHECK(vkCreateSwapchainKHR(device, &swapchainInfo, nullptr, &swapchain));
    CHECK(vkGetSwapchainImagesKHR(device, swapchain, &count, nullptr));
    std::vector<VkImage> images(count);
    CHECK(vkGetSwapchainImagesKHR(device, swapchain, &count, images.data()));

    // Two frames in flight, like Unity's default MaxQueuedFrames.
    constexpr uint32_t InFlight = 2;
    VkCommandPoolCreateInfo poolInfo{VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO};
    poolInfo.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
    poolInfo.queueFamilyIndex = family;
    VkCommandPool pool = VK_NULL_HANDLE;
    CHECK(vkCreateCommandPool(device, &poolInfo, nullptr, &pool));
    VkCommandBufferAllocateInfo allocateInfo{VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO};
    allocateInfo.commandPool = pool;
    allocateInfo.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
    allocateInfo.commandBufferCount = InFlight;
    VkCommandBuffer commands[InFlight];
    CHECK(vkAllocateCommandBuffers(device, &allocateInfo, commands));
    VkSemaphoreCreateInfo semaphoreInfo{VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO};
    VkFenceCreateInfo fenceInfo{VK_STRUCTURE_TYPE_FENCE_CREATE_INFO, nullptr, VK_FENCE_CREATE_SIGNALED_BIT};
    VkSemaphore acquired[InFlight];
    VkFence done[InFlight];
    for (uint32_t i = 0; i < InFlight; ++i) {
        CHECK(vkCreateSemaphore(device, &semaphoreInfo, nullptr, &acquired[i]));
        CHECK(vkCreateFence(device, &fenceInfo, nullptr, &done[i]));
    }
    std::vector<VkSemaphore> rendered(images.size());
    for (auto& semaphore : rendered) CHECK(vkCreateSemaphore(device, &semaphoreInfo, nullptr, &semaphore));

    // Modes: no pacing; the driver's mode only; present waiting then the driver's mode (LowLatencyVSync's order).
    struct Mode { const char* name; bool driver; bool wait; };
    const Mode modes[] = {{"off", false, false}, {"driver", true, false}, {"wait+driver", true, true}};
    const int warmup = 60;
    uint64_t frameNumber = 0;
    const bool measureShown = layer.presentWaitStatus() == 0;
    ShownTracker tracker;
    if (measureShown) tracker.Start();
    std::printf("\n%-12s %6s %9s %9s %9s %16s %14s %14s\n", "mode", "fps", "pacing", "acquire", "present",
        "sample->present", "sample->shown", "shown p95");
    for (const Mode& mode : modes) {
        if (mode.driver && backend == 0) { std::printf("%-12s skipped: no driver low-latency backend\n", mode.name); continue; }
        if (mode.wait && layer.presentWaitStatus() != 0) { std::printf("%-12s skipped: present waiting unavailable\n", mode.name); continue; }
        Stats stats;
        if (measureShown) tracker.Drain(nullptr);
        double previousStart = 0;
        for (int frame = 0; frame < frames + warmup; ++frame, ++frameNumber) {
            MSG message;
            while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
            const double start = Now();
            if (mode.wait) {
                // Wait until the previous frame is shown, as PresentWaitPacer does for a single-threaded renderer.
                uint64_t generation = 0, id = 0;
                if (layer.presentWaitQuery(&generation, &id) == 1 && id != 0) {
                    while (layer.presentWaitPoll(generation, id) == 0 && Now() - start < 100) Sleep(0);
                }
            }
            if (mode.driver) {
                const int begun = layer.frameBegin(100000000ull);
                if (begun < 0) { std::printf("FAIL: majvk_frame_begin returned %d\n", begun); return 1; }
            }
            const double sampled = Now(); // the game would read input here
            BusyWork(workMs);             // simulated game logic
            if (mode.driver) layer.frameEnd();

            const uint32_t slot = static_cast<uint32_t>(frameNumber % InFlight);
            CHECK(vkWaitForFences(device, 1, &done[slot], VK_TRUE, UINT64_MAX));
            const double acquireStart = Now();
            uint32_t image = 0;
            const VkResult acquiredResult = vkAcquireNextImageKHR(device, swapchain, UINT64_MAX, acquired[slot], VK_NULL_HANDLE, &image);
            if (acquiredResult != VK_SUCCESS && acquiredResult != VK_SUBOPTIMAL_KHR) { std::printf("FAIL: acquire returned %d\n", acquiredResult); return 1; }
            const double acquireEnd = Now();
            CHECK(vkResetFences(device, 1, &done[slot]));
            CHECK(vkResetCommandBuffer(commands[slot], 0));
            VkCommandBufferBeginInfo begin{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO};
            begin.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
            CHECK(vkBeginCommandBuffer(commands[slot], &begin));
            VkImageMemoryBarrier barrier{VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER};
            barrier.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
            barrier.oldLayout = VK_IMAGE_LAYOUT_UNDEFINED;
            barrier.newLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
            barrier.srcQueueFamilyIndex = barrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            barrier.image = images[image];
            barrier.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
            vkCmdPipelineBarrier(commands[slot], VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, VK_PIPELINE_STAGE_TRANSFER_BIT, 0, 0, nullptr, 0, nullptr, 1, &barrier);
            const float shade = (frameNumber % 120) / 120.0f;
            VkClearColorValue color{{shade, 0.2f, 1.0f - shade, 1.0f}};
            vkCmdClearColorImage(commands[slot], images[image], VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, &color, 1, &barrier.subresourceRange);
            barrier.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
            barrier.dstAccessMask = 0;
            barrier.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
            barrier.newLayout = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;
            vkCmdPipelineBarrier(commands[slot], VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT, 0, 0, nullptr, 0, nullptr, 1, &barrier);
            CHECK(vkEndCommandBuffer(commands[slot]));
            const VkPipelineStageFlags waitStage = VK_PIPELINE_STAGE_TRANSFER_BIT;
            VkSubmitInfo submit{VK_STRUCTURE_TYPE_SUBMIT_INFO};
            submit.waitSemaphoreCount = 1;
            submit.pWaitSemaphores = &acquired[slot];
            submit.pWaitDstStageMask = &waitStage;
            submit.commandBufferCount = 1;
            submit.pCommandBuffers = &commands[slot];
            submit.signalSemaphoreCount = 1;
            submit.pSignalSemaphores = &rendered[image];
            CHECK(vkQueueSubmit(queue, 1, &submit, done[slot]));
            VkPresentInfoKHR present{VK_STRUCTURE_TYPE_PRESENT_INFO_KHR};
            present.waitSemaphoreCount = 1;
            present.pWaitSemaphores = &rendered[image];
            present.swapchainCount = 1;
            present.pSwapchains = &swapchain;
            present.pImageIndices = &image;
            const double presentCall = Now();
            const VkResult presented = vkQueuePresentKHR(queue, &present);
            const double presentEnd = Now();
            if (presented != VK_SUCCESS && presented != VK_SUBOPTIMAL_KHR) { std::printf("FAIL: present returned %d\n", presented); return 1; }
            if (frame == warmup && measureShown) tracker.Drain(&stats.sampleToShown);
            if (frame >= warmup) {
                stats.pacing.push_back(sampled - start);
                stats.acquire.push_back(acquireEnd - acquireStart);
                stats.present.push_back(presentEnd - presentCall);
                stats.sampleToPresent.push_back(presentCall - sampled);
                stats.interval.push_back(start - previousStart);
                uint64_t generation = 0, id = 0;
                if (measureShown && layer.presentWaitQuery(&generation, &id) == 1 && id != 0) tracker.Add(generation, id, sampled);
            }
            previousStart = start;
        }
        if (measureShown) tracker.Drain(nullptr);
        const double interval = Average(stats.interval);
        std::printf("%-12s %6.1f %7.2fms %7.2fms %7.2fms %14.2fms ", mode.name, interval > 0 ? 1000.0 / interval : 0,
            Average(stats.pacing), Average(stats.acquire), Average(stats.present), Average(stats.sampleToPresent));
        if (stats.sampleToShown.empty()) std::puts("           n/a            n/a");
        else std::printf("%12.2fms %12.2fms\n", Average(stats.sampleToShown), Percentile(stats.sampleToShown, 0.95));
    }
    if (measureShown) tracker.Stop();
    std::puts("\npacing: wait before input is sampled. acquire/present: swapchain waits after it. sample->shown: from input");
    std::puts("sampling until vkWaitForPresentKHR reports the frame shown (needs present waiting). A working backend moves");
    std::puts("the wait into pacing and lowers sample->shown.");

    CHECK(vkDeviceWaitIdle(device));
    for (auto semaphore : rendered) vkDestroySemaphore(device, semaphore, nullptr);
    for (uint32_t i = 0; i < InFlight; ++i) { vkDestroySemaphore(device, acquired[i], nullptr); vkDestroyFence(device, done[i], nullptr); }
    vkDestroyCommandPool(device, pool, nullptr);
    vkDestroySwapchainKHR(device, swapchain, nullptr);
    vkDestroyDevice(device, nullptr);
    vkDestroySurfaceKHR(instance, surface, nullptr);
    vkDestroyInstance(instance, nullptr);
    DestroyWindow(window);
    if (!backendOk) {
        std::printf("RESULT: required backend '%s' is not active (active: %s)\n", require.c_str(), BackendName(backend));
        return 2;
    }
    std::printf("RESULT: done with backend %s\n", BackendName(backend));
    return 0;
}
