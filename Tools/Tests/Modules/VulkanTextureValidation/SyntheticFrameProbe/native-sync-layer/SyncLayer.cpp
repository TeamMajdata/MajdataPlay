// MajdataPlay diagnostic explicit Vulkan layer: buffer-visibility dependency injection.
//
// Purpose: while a gfxrecon capture is replayed, append one explicit
// transfer-write -> vertex-input buffer memory dependency after qualifying
// vkCmdCopyBuffer calls, so a replay with injection (mode 1 or 2) can be compared
// against a pure passthrough replay (mode 0) of the same capture. This is a
// diagnostic instrument, not a production fix: it changes queue scheduling.
//
// The layer is an ordinary explicit Vulkan layer. It exports
// vkNegotiateLoaderLayerInterfaceVersion, vkGetInstanceProcAddr and
// vkGetDeviceProcAddr, reaches the next layer through the loader provided
// pNextGIPA / pNextGDPA link information, and never links against vulkan-1.lib:
// VK_NO_PROTOTYPES is defined and every Vulkan entry point is a function pointer
// resolved through the chain.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <Windows.h>
#define VK_NO_PROTOTYPES
#include <vulkan/vulkan_core.h>

#include <atomic>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <mutex>
#include <unordered_map>

#define MSL_EXPORT __declspec(dllexport)
#define MSL_CALL __cdecl

namespace {

constexpr int ModeDisabled = 0, ModeNarrow = 1, ModeBroad = 2;

// The vendored Vulkan-Headers copy has no vk_layer.h, so the loader/layer ABI used by
// an explicit layer is declared here. The numeric values come from the Vulkan loader
// layer contract: the loader create-info structure types are part of vulkan_core.h
// (47 / 48) and VK_LAYER_LINK_INFO is the first VkLayerFunction value.
constexpr VkStructureType LayerInstanceCreateInfoType = static_cast<VkStructureType>(47);
constexpr VkStructureType LayerDeviceCreateInfoType = static_cast<VkStructureType>(48);
constexpr VkStructureType NegotiateLayerInterfaceType = static_cast<VkStructureType>(100000);
constexpr int32_t LayerLinkInfo = 0;
constexpr uint32_t SupportedLayerInterfaceVersion = 2;

using PfnGetPhysicalDeviceProcAddr = VKAPI_ATTR PFN_vkVoidFunction(VKAPI_CALL*)(VkInstance, const char*);

// Vendor neutral mirror of VkLayerInstanceLink / VkLayerDeviceLink. Only the fields the
// layer reads are named; the loader owns the storage and never NULL-checks them for us.
struct LayerInstanceLink {
    VkStructureType sType;
    LayerInstanceLink* pNext;
    PFN_vkGetInstanceProcAddr pfnNextGetInstanceProcAddr;
    PfnGetPhysicalDeviceProcAddr pfnNextGetPhysicalDeviceProcAddr;
};

struct LayerDeviceLink {
    VkStructureType sType;
    LayerDeviceLink* pNext;
    PFN_vkGetInstanceProcAddr pfnNextGetInstanceProcAddr;
    PFN_vkGetDeviceProcAddr pfnNextGetDeviceProcAddr;
};

struct LayerInstanceCreateInfo {
    VkStructureType sType;
    const void* pNext;
    int32_t function;
    union {
        LayerInstanceLink* pLayerInfo;
        void* pfnSetInstanceLoaderData;
    } u;
};

struct LayerDeviceCreateInfo {
    VkStructureType sType;
    const void* pNext;
    int32_t function;
    union {
        LayerDeviceLink* pLayerInfo;
        void* pfnSetDeviceLoaderData;
    } u;
};

// VkNegotiateLayerInterface (vk_layer.h). The loader owns the instance; the layer only
// writes the outputs.
struct NegotiateLayerInterface {
    VkStructureType sType;
    void* pNext;
    uint32_t loaderLayerInterfaceVersion;
    PFN_vkGetInstanceProcAddr pfnGetInstanceProcAddr;
    PFN_vkGetDeviceProcAddr pfnGetDeviceProcAddr;
    PfnGetPhysicalDeviceProcAddr pfnGetPhysicalDeviceProcAddr;
};

static_assert(sizeof(void*) == 8, "This diagnostic layer supports Windows x64 only.");
static_assert(offsetof(NegotiateLayerInterface, loaderLayerInterfaceVersion) == 16, "VkNegotiateLayerInterface layout");
static_assert(offsetof(NegotiateLayerInterface, pfnGetInstanceProcAddr) == 24, "VkNegotiateLayerInterface layout");
static_assert(offsetof(NegotiateLayerInterface, pfnGetDeviceProcAddr) == 32, "VkNegotiateLayerInterface layout");
static_assert(offsetof(NegotiateLayerInterface, pfnGetPhysicalDeviceProcAddr) == 40, "VkNegotiateLayerInterface layout");
static_assert(sizeof(NegotiateLayerInterface) == 48, "VkNegotiateLayerInterface layout");
static_assert(sizeof(LayerInstanceCreateInfo) == 32, "VkLayerInstanceCreateInfo layout");
static_assert(sizeof(LayerDeviceCreateInfo) == 32, "VkLayerDeviceCreateInfo layout");

std::atomic<int> mode{ModeDisabled};
std::atomic<bool> modeLoaded{false};
std::atomic<uint64_t> copyCalls{0}, barrierCalls{0}, skippedCalls{0}, errorCount{0};
std::atomic<PFN_vkGetInstanceProcAddr> nextInstanceProcAddr{nullptr};
std::atomic<PFN_vkGetDeviceProcAddr> nextDeviceProcAddr{nullptr};
std::atomic<PFN_vkCmdCopyBuffer> realCmdCopyBuffer{nullptr};
std::atomic<PFN_vkCmdPipelineBarrier> realCmdPipelineBarrier{nullptr};
std::atomic<PFN_vkCreateBuffer> realCreateBuffer{nullptr};
std::atomic<PFN_vkDestroyBuffer> realDestroyBuffer{nullptr};
std::mutex bufferUsageMutex;
std::unordered_map<VkBuffer, VkBufferUsageFlags> bufferUsage;

VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL LayerGetInstanceProcAddr(VkInstance instance, const char* name);
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL LayerGetDeviceProcAddr(VkDevice device, const char* name);
VKAPI_ATTR VkResult VKAPI_CALL LayerCreateInstance(const VkInstanceCreateInfo* createInfo,
    const VkAllocationCallbacks* allocator, VkInstance* instance);
VKAPI_ATTR VkResult VKAPI_CALL LayerCreateDevice(VkPhysicalDevice physicalDevice, const VkDeviceCreateInfo* createInfo,
    const VkAllocationCallbacks* allocator, VkDevice* device);
VKAPI_ATTR VkResult VKAPI_CALL LayerCreateBuffer(VkDevice device, const VkBufferCreateInfo* createInfo,
    const VkAllocationCallbacks* allocator, VkBuffer* buffer);
VKAPI_ATTR void VKAPI_CALL LayerDestroyBuffer(VkDevice device, VkBuffer buffer,
    const VkAllocationCallbacks* allocator);
VKAPI_ATTR void VKAPI_CALL LayerCmdCopyBuffer(VkCommandBuffer commandBuffer, VkBuffer srcBuffer, VkBuffer dstBuffer,
    uint32_t regionCount, const VkBufferCopy* pRegions);

void CountError() { errorCount.fetch_add(1, std::memory_order_relaxed); }

bool IsOwnEntryPoint(PFN_vkVoidFunction function) {
    return function == reinterpret_cast<PFN_vkVoidFunction>(LayerGetInstanceProcAddr)
        || function == reinterpret_cast<PFN_vkVoidFunction>(LayerGetDeviceProcAddr)
        || function == reinterpret_cast<PFN_vkVoidFunction>(LayerCreateInstance)
        || function == reinterpret_cast<PFN_vkVoidFunction>(LayerCreateDevice)
        || function == reinterpret_cast<PFN_vkVoidFunction>(LayerCmdCopyBuffer)
        || function == reinterpret_cast<PFN_vkVoidFunction>(LayerCreateBuffer)
        || function == reinterpret_cast<PFN_vkVoidFunction>(LayerDestroyBuffer);
}

// Caches the next layer's entry point. The wrapper itself is never cached: doing so
// would make the wrapper call itself. The first resolution wins so a later, narrower
// resolution (for example an instance-level trampoline) cannot replace a device level
// function pointer.
template <class T>
void Remember(std::atomic<T>& slot, PFN_vkVoidFunction function) {
    if (!function || IsOwnEntryPoint(function)) return;
    T expected = nullptr;
    slot.compare_exchange_strong(expected, reinterpret_cast<T>(function), std::memory_order_release,
        std::memory_order_relaxed);
}

// MAJ_SYNC_LAYER_MODE is read once, on the first entry point that runs.
void LoadMode() {
    if (modeLoaded.load(std::memory_order_acquire)) return;
    char value[8]{};
    const auto length = GetEnvironmentVariableA("MAJ_SYNC_LAYER_MODE", value, static_cast<DWORD>(sizeof(value)));
    int requested = ModeDisabled;
    if (length == 1 && value[0] == '1') requested = ModeNarrow;
    if (length == 1 && value[0] == '2') requested = ModeBroad;
    mode.store(requested, std::memory_order_release);
    modeLoaded.store(true, std::memory_order_release);
}

// Mode 1 only injects when the destination was created with index and/or vertex usage.
bool UsesVertexInput(VkBuffer buffer) {
    VkBufferUsageFlags usage = 0;
    {
        std::lock_guard<std::mutex> lock(bufferUsageMutex);
        const auto found = bufferUsage.find(buffer);
        if (found != bufferUsage.end()) usage = found->second;
    }
    return (usage & (VK_BUFFER_USAGE_INDEX_BUFFER_BIT | VK_BUFFER_USAGE_VERTEX_BUFFER_BIT)) != 0;
}

LayerInstanceCreateInfo* FindInstanceLink(const VkInstanceCreateInfo* createInfo) {
    if (!createInfo) return nullptr;
    auto* info = static_cast<const LayerInstanceCreateInfo*>(createInfo->pNext);
    while (info) {
        if (info->sType == LayerInstanceCreateInfoType && info->function == LayerLinkInfo && info->u.pLayerInfo) {
            return const_cast<LayerInstanceCreateInfo*>(info);
        }
        info = static_cast<const LayerInstanceCreateInfo*>(info->pNext);
    }
    return nullptr;
}

LayerDeviceCreateInfo* FindDeviceLink(const VkDeviceCreateInfo* createInfo) {
    if (!createInfo) return nullptr;
    auto* info = static_cast<const LayerDeviceCreateInfo*>(createInfo->pNext);
    while (info) {
        if (info->sType == LayerDeviceCreateInfoType && info->function == LayerLinkInfo && info->u.pLayerInfo) {
            return const_cast<LayerDeviceCreateInfo*>(info);
        }
        info = static_cast<const LayerDeviceCreateInfo*>(info->pNext);
    }
    return nullptr;
}

PFN_vkGetDeviceProcAddr ResolveNextDeviceProcAddr() {
    auto gdpa = nextDeviceProcAddr.load(std::memory_order_acquire);
    if (gdpa && gdpa != LayerGetDeviceProcAddr) return gdpa;
    const auto gipa = nextInstanceProcAddr.load(std::memory_order_acquire);
    if (!gipa || gipa == LayerGetInstanceProcAddr) return nullptr;
    const auto function = gipa(nullptr, "vkGetDeviceProcAddr");
    if (!function || IsOwnEntryPoint(function)) return nullptr;
    gdpa = reinterpret_cast<PFN_vkGetDeviceProcAddr>(function);
    nextDeviceProcAddr.store(gdpa, std::memory_order_release);
    return gdpa;
}

// Resolves the wrapped device commands once, as soon as the layer owns a real device.
// The copy wrapper has no way to look up its device from a command buffer, so these
// pointers must exist before the first recorded command buffer is submitted.
void ResolveDeviceEntryPoints(VkDevice device, PFN_vkGetDeviceProcAddr gdpa) {
    if (!device || !gdpa || gdpa == LayerGetDeviceProcAddr) return;
    Remember(realCmdCopyBuffer, gdpa(device, "vkCmdCopyBuffer"));
    Remember(realCmdPipelineBarrier, gdpa(device, "vkCmdPipelineBarrier"));
    Remember(realCreateBuffer, gdpa(device, "vkCreateBuffer"));
    Remember(realDestroyBuffer, gdpa(device, "vkDestroyBuffer"));
}

VKAPI_ATTR VkResult VKAPI_CALL LayerCreateInstance(const VkInstanceCreateInfo* createInfo,
    const VkAllocationCallbacks* allocator, VkInstance* instance) {
    LoadMode();
    auto* linkInfo = FindInstanceLink(createInfo);
    if (!linkInfo || !linkInfo->u.pLayerInfo || !linkInfo->u.pLayerInfo->pfnNextGetInstanceProcAddr) {
        CountError();
        return VK_ERROR_INITIALIZATION_FAILED;
    }
    const auto next = linkInfo->u.pLayerInfo->pfnNextGetInstanceProcAddr;
    if (next == LayerGetInstanceProcAddr) {
        CountError();
        return VK_ERROR_INITIALIZATION_FAILED;
    }
    // The next layer's vkCreateInstance lives behind its own GIPA; resolving it before
    // the link entry is consumed keeps the chain intact.
    const auto create = reinterpret_cast<PFN_vkCreateInstance>(next(nullptr, "vkCreateInstance"));
    if (!create) {
        CountError();
        return VK_ERROR_INITIALIZATION_FAILED;
    }
    nextInstanceProcAddr.store(next, std::memory_order_release);
    linkInfo->u.pLayerInfo = linkInfo->u.pLayerInfo->pNext;
    return create(createInfo, allocator, instance);
}

VKAPI_ATTR VkResult VKAPI_CALL LayerCreateDevice(VkPhysicalDevice physicalDevice,
    const VkDeviceCreateInfo* createInfo, const VkAllocationCallbacks* allocator, VkDevice* device) {
    LoadMode();
    auto* linkInfo = FindDeviceLink(createInfo);
    if (!linkInfo || !linkInfo->u.pLayerInfo || !linkInfo->u.pLayerInfo->pfnNextGetDeviceProcAddr) {
        CountError();
        return VK_ERROR_INITIALIZATION_FAILED;
    }
    const auto gdpa = linkInfo->u.pLayerInfo->pfnNextGetDeviceProcAddr;
    if (gdpa == LayerGetDeviceProcAddr) {
        CountError();
        return VK_ERROR_INITIALIZATION_FAILED;
    }
    const auto gipa = linkInfo->u.pLayerInfo->pfnNextGetInstanceProcAddr
        ? linkInfo->u.pLayerInfo->pfnNextGetInstanceProcAddr
        : nextInstanceProcAddr.load(std::memory_order_acquire);
    const auto create = gipa ? reinterpret_cast<PFN_vkCreateDevice>(gipa(nullptr, "vkCreateDevice")) : nullptr;
    if (!create || IsOwnEntryPoint(reinterpret_cast<PFN_vkVoidFunction>(create))) {
        CountError();
        return VK_ERROR_INITIALIZATION_FAILED;
    }
    nextDeviceProcAddr.store(gdpa, std::memory_order_release);
    linkInfo->u.pLayerInfo = linkInfo->u.pLayerInfo->pNext;
    const auto result = create(physicalDevice, createInfo, allocator, device);
    if (result == VK_SUCCESS && device && *device != VK_NULL_HANDLE) ResolveDeviceEntryPoints(*device, gdpa);
    return result;
}

VKAPI_ATTR VkResult VKAPI_CALL LayerCreateBuffer(VkDevice device, const VkBufferCreateInfo* createInfo,
    const VkAllocationCallbacks* allocator, VkBuffer* buffer) {
    const auto create = realCreateBuffer.load(std::memory_order_acquire);
    if (!create || create == LayerCreateBuffer) {
        CountError();
        return VK_ERROR_INITIALIZATION_FAILED;
    }
    const auto result = create(device, createInfo, allocator, buffer);
    if (result == VK_SUCCESS && buffer && *buffer != VK_NULL_HANDLE) {
        const auto usage = createInfo ? createInfo->usage : 0;
        std::lock_guard<std::mutex> lock(bufferUsageMutex);
        bufferUsage[*buffer] = usage;
    }
    return result;
}

VKAPI_ATTR void VKAPI_CALL LayerDestroyBuffer(VkDevice device, VkBuffer buffer, const VkAllocationCallbacks* allocator) {
    {
        std::lock_guard<std::mutex> lock(bufferUsageMutex);
        bufferUsage.erase(buffer);
    }
    const auto destroy = realDestroyBuffer.load(std::memory_order_acquire);
    if (!destroy || destroy == LayerDestroyBuffer) {
        CountError();
        return;
    }
    destroy(device, buffer, allocator);
}

// Forwards the original copy unchanged, then appends the selected diagnostic dependency.
VKAPI_ATTR void VKAPI_CALL LayerCmdCopyBuffer(VkCommandBuffer commandBuffer, VkBuffer srcBuffer, VkBuffer dstBuffer,
    uint32_t regionCount, const VkBufferCopy* pRegions) {
    copyCalls.fetch_add(1, std::memory_order_relaxed);
    const auto copy = realCmdCopyBuffer.load(std::memory_order_acquire);
    if (!copy || copy == LayerCmdCopyBuffer) {
        CountError();
        return;
    }
    copy(commandBuffer, srcBuffer, dstBuffer, regionCount, pRegions);

    const int current = mode.load(std::memory_order_acquire);
    if (current == ModeDisabled) return;
    if (current == ModeNarrow && !UsesVertexInput(dstBuffer)) {
        skippedCalls.fetch_add(1, std::memory_order_relaxed);
        return;
    }
    // Resolved through the loader chain, so any later layer (including a validation
    // layer) observes the injected barrier as an ordinary recorded command.
    const auto barrier = realCmdPipelineBarrier.load(std::memory_order_acquire);
    if (!barrier) {
        CountError();
        return;
    }

    VkBufferMemoryBarrier dependency{};
    dependency.sType = VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER;
    dependency.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
    dependency.dstAccessMask = current == ModeBroad
        ? static_cast<VkAccessFlags>(VK_ACCESS_MEMORY_READ_BIT)
        : static_cast<VkAccessFlags>(VK_ACCESS_INDEX_READ_BIT | VK_ACCESS_VERTEX_ATTRIBUTE_READ_BIT);
    dependency.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    dependency.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    dependency.buffer = dstBuffer;
    dependency.offset = 0;
    dependency.size = VK_WHOLE_SIZE;
    const VkPipelineStageFlags dstStageMask = current == ModeBroad
        ? static_cast<VkPipelineStageFlags>(VK_PIPELINE_STAGE_ALL_COMMANDS_BIT)
        : static_cast<VkPipelineStageFlags>(VK_PIPELINE_STAGE_VERTEX_INPUT_BIT);
    barrier(commandBuffer, static_cast<VkPipelineStageFlags>(VK_PIPELINE_STAGE_TRANSFER_BIT), dstStageMask,
        static_cast<VkDependencyFlags>(0), 0, nullptr, 1, &dependency, 0, nullptr);
    barrierCalls.fetch_add(1, std::memory_order_relaxed);
}

VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL LayerGetInstanceProcAddr(VkInstance instance, const char* name) {
    LoadMode();
    if (!name) return nullptr;
    // Self queries must return the wrapper: a cached lower resolver would bypass the
    // interceptor entirely.
    if (std::strcmp(name, "vkGetInstanceProcAddr") == 0) return reinterpret_cast<PFN_vkVoidFunction>(LayerGetInstanceProcAddr);
    if (std::strcmp(name, "vkGetDeviceProcAddr") == 0) return reinterpret_cast<PFN_vkVoidFunction>(LayerGetDeviceProcAddr);
    if (std::strcmp(name, "vkCreateInstance") == 0) return reinterpret_cast<PFN_vkVoidFunction>(LayerCreateInstance);
    if (std::strcmp(name, "vkCreateDevice") == 0) return reinterpret_cast<PFN_vkVoidFunction>(LayerCreateDevice);
    if (std::strcmp(name, "vkCmdCopyBuffer") == 0) return reinterpret_cast<PFN_vkVoidFunction>(LayerCmdCopyBuffer);
    if (std::strcmp(name, "vkCreateBuffer") == 0) return reinterpret_cast<PFN_vkVoidFunction>(LayerCreateBuffer);
    if (std::strcmp(name, "vkDestroyBuffer") == 0) return reinterpret_cast<PFN_vkVoidFunction>(LayerDestroyBuffer);

    const auto next = nextInstanceProcAddr.load(std::memory_order_acquire);
    if (!next || next == LayerGetInstanceProcAddr) return nullptr;
    // Instance level device command pointers are loader trampolines, so they are
    // forwarded but never cached; the device level resolution is authoritative.
    return next(instance, name);
}

VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL LayerGetDeviceProcAddr(VkDevice device, const char* name) {
    LoadMode();
    if (!name) return nullptr;
    if (std::strcmp(name, "vkGetDeviceProcAddr") == 0) return reinterpret_cast<PFN_vkVoidFunction>(LayerGetDeviceProcAddr);
    const auto next = ResolveNextDeviceProcAddr();
    if (!next) return nullptr;
    const auto function = next(device, name);
    if (!function) return nullptr;
    // A lower resolver that hands back one of this layer's own entry points is
    // recursive. It is reported and it is never cached, because caching it would make a
    // wrapper call itself.
    const bool recursive = IsOwnEntryPoint(function);
    if (recursive) CountError();
    if (std::strcmp(name, "vkCmdCopyBuffer") == 0) {
        if (!recursive) Remember(realCmdCopyBuffer, function);
        return reinterpret_cast<PFN_vkVoidFunction>(LayerCmdCopyBuffer);
    }
    if (std::strcmp(name, "vkCreateBuffer") == 0) {
        if (!recursive) Remember(realCreateBuffer, function);
        return reinterpret_cast<PFN_vkVoidFunction>(LayerCreateBuffer);
    }
    if (std::strcmp(name, "vkDestroyBuffer") == 0) {
        if (!recursive) Remember(realDestroyBuffer, function);
        return reinterpret_cast<PFN_vkVoidFunction>(LayerDestroyBuffer);
    }
    if (std::strcmp(name, "vkCmdPipelineBarrier") == 0) {
        // Returning a wrapper with the wrong signature would corrupt the call.
        if (recursive) return nullptr;
        Remember(realCmdPipelineBarrier, function);
        return function;
    }
    return recursive ? nullptr : function;
}

} // namespace

extern "C" MSL_EXPORT VKAPI_ATTR VkResult VKAPI_CALL vkNegotiateLoaderLayerInterfaceVersion(
    NegotiateLayerInterface* versionStruct) {
    LoadMode();
    if (!versionStruct) {
        CountError();
        return VK_ERROR_INITIALIZATION_FAILED;
    }
    // loaderLayerInterfaceVersion is IN: the loader's version, OUT: the version the
    // layer implements. Echoing the lower value keeps this ABI safe with a loader that
    // only provides the shorter (version 1) structure, where the physical-device member
    // does not exist yet.
    if (versionStruct->loaderLayerInterfaceVersion < 1) {
        CountError();
        return VK_ERROR_INITIALIZATION_FAILED;
    }
    const bool version2 = versionStruct->loaderLayerInterfaceVersion >= 2;
    versionStruct->loaderLayerInterfaceVersion = version2 ? SupportedLayerInterfaceVersion : 1;
    versionStruct->pfnGetInstanceProcAddr = LayerGetInstanceProcAddr;
    versionStruct->pfnGetDeviceProcAddr = LayerGetDeviceProcAddr;
    // Not implemented: the loader falls back to vkGetInstanceProcAddr for physical
    // device level entry points, which is sufficient for this layer.
    if (version2) versionStruct->pfnGetPhysicalDeviceProcAddr = nullptr;
    return VK_SUCCESS;
}

extern "C" MSL_EXPORT VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL vkGetInstanceProcAddr(VkInstance instance,
    const char* name) {
    return LayerGetInstanceProcAddr(instance, name);
}

extern "C" MSL_EXPORT VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL vkGetDeviceProcAddr(VkDevice device, const char* name) {
    return LayerGetDeviceProcAddr(device, name);
}

// Diagnostic ABI. Plain C / __cdecl so a PowerShell or C host can load the DLL and read
// the counters without going through the Vulkan loader.
extern "C" MSL_EXPORT uint64_t MSL_CALL msl_copy_calls() { return copyCalls.load(std::memory_order_relaxed); }
extern "C" MSL_EXPORT uint64_t MSL_CALL msl_barrier_calls() { return barrierCalls.load(std::memory_order_relaxed); }
extern "C" MSL_EXPORT uint64_t MSL_CALL msl_skipped_calls() { return skippedCalls.load(std::memory_order_relaxed); }
extern "C" MSL_EXPORT uint64_t MSL_CALL msl_errors() { return errorCount.load(std::memory_order_relaxed); }
extern "C" MSL_EXPORT int MSL_CALL msl_mode() {
    LoadMode();
    return mode.load(std::memory_order_acquire);
}
