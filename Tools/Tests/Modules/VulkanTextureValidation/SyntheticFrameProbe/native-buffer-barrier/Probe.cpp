#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <Windows.h>
#define VK_NO_PROTOTYPES
#include "IUnityGraphics.h"
#include "IUnityGraphicsVulkan.h"
#include <atomic>
#include <cstdint>
#include <cstring>

#define BB_EXPORT extern "C" __declspec(dllexport)
#define BB_CALL __cdecl

namespace {
constexpr int NotRegistered = -11001, NoDevice = -11002, NoCopy = -11003;
constexpr int NoBarrier = -11004, RecursiveResolver = -11005, NoInterface = -11006;
std::atomic<bool> enabled{false}, registered{false}, active{false};
std::atomic<int> fault{0};
std::atomic<uint64_t> copyCalls{0}, barrierCalls{0}, errors{0};
std::atomic<PFN_vkGetInstanceProcAddr> nextInstance{nullptr};
std::atomic<PFN_vkGetDeviceProcAddr> nextDevice{nullptr};
std::atomic<PFN_vkCmdCopyBuffer> copyBuffer{nullptr};
std::atomic<PFN_vkCmdPipelineBarrier> pipelineBarrier{nullptr};
IUnityGraphics* graphics = nullptr;
IUnityGraphicsVulkanV2* vulkan = nullptr;

void Error(int code) {
    errors.fetch_add(1, std::memory_order_relaxed);
    fault.store(code, std::memory_order_release);
}
template<class T> T* Interface(IUnityInterfaces* interfaces) {
    const auto id = GetUnityInterfaceGUID<T>();
    return static_cast<T*>(interfaces->GetInterfaceSplit(id.m_GUIDHigh, id.m_GUIDLow));
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL GetInstance(VkInstance instance, const char* name);
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL GetDevice(VkDevice device, const char* name);

// Calls the original copy before appending the sole diagnostic command.
VKAPI_ATTR void VKAPI_CALL CopyBuffer(VkCommandBuffer command, VkBuffer source, VkBuffer destination,
    uint32_t count, const VkBufferCopy* regions) {
    copyCalls.fetch_add(1, std::memory_order_relaxed);
    const auto copy = copyBuffer.load(std::memory_order_acquire);
    if (!copy || copy == CopyBuffer) { Error(NoCopy); return; }
    copy(command, source, destination, count, regions);
    if (!enabled.load(std::memory_order_acquire)) return;
    if (!active.load(std::memory_order_acquire)) { Error(NoDevice); return; }
    const auto barrier = pipelineBarrier.load(std::memory_order_acquire);
    if (!barrier) { Error(NoBarrier); return; }
    VkBufferMemoryBarrier dependency{};
    dependency.sType = VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER;
    dependency.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
    dependency.dstAccessMask = VK_ACCESS_MEMORY_READ_BIT;
    dependency.srcQueueFamilyIndex = dependency.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    dependency.buffer = destination;
    dependency.offset = 0;
    dependency.size = VK_WHOLE_SIZE;
    barrier(command, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_ALL_COMMANDS_BIT, 0,
        0, nullptr, 1, &dependency, 0, nullptr);
    barrierCalls.fetch_add(1, std::memory_order_relaxed);
}

// Preserve NULL and pass unrelated entry points through without replacing objects.
PFN_vkVoidFunction Wrap(PFN_vkVoidFunction function, const char* name) {
    if (!function || !name) return function;
    if (std::strcmp(name, "vkGetInstanceProcAddr") == 0) {
        if (function == reinterpret_cast<PFN_vkVoidFunction>(GetInstance)) Error(RecursiveResolver);
        return reinterpret_cast<PFN_vkVoidFunction>(GetInstance);
    }
    if (std::strcmp(name, "vkGetDeviceProcAddr") == 0) {
        if (function != reinterpret_cast<PFN_vkVoidFunction>(GetDevice))
            nextDevice.store(reinterpret_cast<PFN_vkGetDeviceProcAddr>(function), std::memory_order_release);
        else Error(RecursiveResolver);
        return reinterpret_cast<PFN_vkVoidFunction>(GetDevice);
    }
    if (std::strcmp(name, "vkCmdCopyBuffer") == 0) {
        if (function == reinterpret_cast<PFN_vkVoidFunction>(CopyBuffer)) {
            Error(RecursiveResolver);
            return function;
        }
        copyBuffer.store(reinterpret_cast<PFN_vkCmdCopyBuffer>(function), std::memory_order_release);
        return reinterpret_cast<PFN_vkVoidFunction>(CopyBuffer);
    }
    if (std::strcmp(name, "vkCmdPipelineBarrier") == 0)
        pipelineBarrier.store(reinterpret_cast<PFN_vkCmdPipelineBarrier>(function), std::memory_order_release);
    return function;
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL GetInstance(VkInstance instance, const char* name) {
    const auto next = nextInstance.load(std::memory_order_acquire);
    if (!next || next == GetInstance) { Error(RecursiveResolver); return nullptr; }
    return Wrap(next(instance, name), name);
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL GetDevice(VkDevice device, const char* name) {
    const auto next = nextDevice.load(std::memory_order_acquire);
    if (!next || next == GetDevice) { Error(RecursiveResolver); return nullptr; }
    return Wrap(next(device, name), name);
}
PFN_vkGetInstanceProcAddr UNITY_INTERFACE_API Initialize(PFN_vkGetInstanceProcAddr next, void*) {
    active.store(false, std::memory_order_release);
    copyBuffer.store(nullptr, std::memory_order_release);
    pipelineBarrier.store(nullptr, std::memory_order_release);
    nextDevice.store(nullptr, std::memory_order_release);
    if (!next || next == GetInstance) { Error(RecursiveResolver); return nullptr; }
    nextInstance.store(next, std::memory_order_release);
    return GetInstance;
}
void UNITY_INTERFACE_API DeviceEvent(UnityGfxDeviceEventType event) {
    if (event == kUnityGfxDeviceEventShutdown || event == kUnityGfxDeviceEventBeforeReset) {
        active.store(false, std::memory_order_release);
        return; // Never query an inactive device or wait for it here.
    }
    if (event != kUnityGfxDeviceEventInitialize && event != kUnityGfxDeviceEventAfterReset) return;
    active.store(false, std::memory_order_release);
    if (!enabled.load() || !registered.load() || !graphics || !vulkan
        || !vulkan->Instance || graphics->GetRenderer() != kUnityGfxRendererVulkan) return;
    const auto instance = vulkan->Instance();
    const auto gipa = nextInstance.load(std::memory_order_acquire);
    if (!instance.device || !gipa || gipa == GetInstance) return;
    const auto raw = gipa(instance.instance, "vkGetDeviceProcAddr");
    if (!raw || raw == reinterpret_cast<PFN_vkVoidFunction>(GetDevice)) {
        Error(RecursiveResolver); return;
    }
    const auto gdpa = reinterpret_cast<PFN_vkGetDeviceProcAddr>(raw);
    nextDevice.store(gdpa, std::memory_order_release);
    // Resolve through the original chain, retaining validation/lower-layer interception.
    const auto copy = gdpa(instance.device, "vkCmdCopyBuffer");
    const auto barrier = gdpa(instance.device, "vkCmdPipelineBarrier");
    if (copy) Wrap(copy, "vkCmdCopyBuffer");
    if (barrier) Wrap(barrier, "vkCmdPipelineBarrier");
    active.store(true, std::memory_order_release);
    if (!copy) Error(NoCopy);
    if (!barrier) Error(NoBarrier);
}
} // namespace

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* interfaces) {
    char value[4]{};
    const auto length = GetEnvironmentVariableA("MAJ_SYNTHETIC_BUFFER_BARRIER", value, sizeof(value));
    enabled.store(length == 1 && value[0] == '1', std::memory_order_release);
    if (!enabled.load()) return; // Disabled mode installs no Vulkan or lifecycle hook.
    if (!interfaces || !interfaces->GetInterfaceSplit) { Error(NoInterface); return; }
    graphics = Interface<IUnityGraphics>(interfaces);
    vulkan = Interface<IUnityGraphicsVulkanV2>(interfaces);
    if (!graphics || !graphics->RegisterDeviceEventCallback || !graphics->UnregisterDeviceEventCallback
        || !vulkan || !vulkan->AddInterceptInitialization || !vulkan->RemoveInterceptInitialization) {
        Error(NoInterface); return;
    }
    const bool installed = vulkan->AddInterceptInitialization(Initialize, nullptr, 0x40000000);
    registered.store(installed, std::memory_order_release);
    if (!installed) { Error(NotRegistered); return; }
    graphics->RegisterDeviceEventCallback(DeviceEvent);
}
extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload() {
    active.store(false, std::memory_order_release);
    if (registered.exchange(false)) {
        graphics->UnregisterDeviceEventCallback(DeviceEvent);
        vulkan->RemoveInterceptInitialization(Initialize);
    }
    graphics = nullptr;
    vulkan = nullptr;
    // Dynamic unloading with cached wrappers/in-flight command recording is unsupported.
}
BB_EXPORT int BB_CALL bb_abi_version() { return 1; }
BB_EXPORT int BB_CALL bb_mode() { return enabled.load(std::memory_order_acquire) ? 1 : 0; }
BB_EXPORT int BB_CALL bb_status() {
    if (!enabled.load(std::memory_order_acquire)) return 1;
    const int error = fault.load(std::memory_order_acquire);
    if (error) return error;
    if (!registered.load(std::memory_order_acquire)) return NotRegistered;
    if (!active.load(std::memory_order_acquire)) return NoDevice;
    if (!copyBuffer.load(std::memory_order_acquire)) return NoCopy;
    if (!pipelineBarrier.load(std::memory_order_acquire)) return NoBarrier;
    return 0;
}
BB_EXPORT uint64_t BB_CALL bb_copy_calls() { return copyCalls.load(std::memory_order_relaxed); }
BB_EXPORT uint64_t BB_CALL bb_barrier_calls() { return barrierCalls.load(std::memory_order_relaxed); }
BB_EXPORT uint64_t BB_CALL bb_errors() { return errors.load(std::memory_order_relaxed); }
