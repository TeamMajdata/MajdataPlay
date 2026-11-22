// Host-only tests for the diagnostic sync layer. Every Vulkan entry point used here is a
// fake defined below: no Vulkan loader, instance, device, command buffer or GPU work.
#include "SyncLayer.cpp"

#include <iostream>
#include <stdexcept>

namespace {

int callIndex = 0;
int order[8]{};
int barrierCount = 0;
int destroyCalls = 0;
bool captureBarrier = true;

VkBuffer const indexBuffer = reinterpret_cast<VkBuffer>(101);
VkBuffer const vertexBuffer = reinterpret_cast<VkBuffer>(102);
VkBuffer const uniformBuffer = reinterpret_cast<VkBuffer>(103);
VkCommandBuffer const commandBuffer = reinterpret_cast<VkCommandBuffer>(201);
VkInstance const instanceHandle = reinterpret_cast<VkInstance>(301);
VkDevice const deviceHandle = reinterpret_cast<VkDevice>(302);
VkBufferCopy regions[1]{{0, 0, 936}};

VkBufferMemoryBarrier lastBarrier{};
VkPipelineStageFlags lastSourceStages = 0, lastDestinationStages = 0;
VkDependencyFlags lastDependencyFlags = 0;
uint32_t lastMemoryBarrierCount = 0, lastBufferBarrierCount = 0, lastImageBarrierCount = 0;
VkCommandBuffer lastBarrierCommand = VK_NULL_HANDLE;
VkCommandBuffer lastCopyCommand = VK_NULL_HANDLE;
VkBuffer lastCopySource = VK_NULL_HANDLE, lastCopyDestination = VK_NULL_HANDLE;
uint32_t lastCopyRegionCount = 0;
const VkBufferCopy* lastCopyRegions = nullptr;

void Require(bool value, const char* message) {
    if (!value) throw std::runtime_error(message);
}

VKAPI_ATTR void VKAPI_CALL FakeInstanceOther() {}
VKAPI_ATTR void VKAPI_CALL FakeDeviceOther() {}

uintptr_t nextBufferHandle = 1000;

VKAPI_ATTR VkResult VKAPI_CALL FakeCreate(VkDevice, const VkBufferCreateInfo* createInfo,
    const VkAllocationCallbacks*, VkBuffer* buffer) {
    Require(createInfo != nullptr && buffer != nullptr, "create buffer arguments");
    *buffer = reinterpret_cast<VkBuffer>(++nextBufferHandle);
    return VK_SUCCESS;
}

VKAPI_ATTR void VKAPI_CALL FakeDestroy(VkDevice, VkBuffer, const VkAllocationCallbacks*) { ++destroyCalls; }

VKAPI_ATTR void VKAPI_CALL FakeCopy(VkCommandBuffer command, VkBuffer source, VkBuffer destination, uint32_t count,
    const VkBufferCopy* copyRegions) {
    lastCopyCommand = command;
    lastCopySource = source;
    lastCopyDestination = destination;
    lastCopyRegionCount = count;
    lastCopyRegions = copyRegions;
    order[callIndex++] = 1;
}

VKAPI_ATTR void VKAPI_CALL FakeBarrier(VkCommandBuffer command, VkPipelineStageFlags sourceStages,
    VkPipelineStageFlags destinationStages, VkDependencyFlags dependencyFlags, uint32_t memoryBarrierCount,
    const VkMemoryBarrier*, uint32_t bufferBarrierCount, const VkBufferMemoryBarrier* bufferBarriers,
    uint32_t imageBarrierCount, const VkImageMemoryBarrier*) {
    lastBarrierCommand = command;
    lastSourceStages = sourceStages;
    lastDestinationStages = destinationStages;
    lastDependencyFlags = dependencyFlags;
    lastMemoryBarrierCount = memoryBarrierCount;
    lastBufferBarrierCount = bufferBarrierCount;
    lastImageBarrierCount = imageBarrierCount;
    if (captureBarrier && bufferBarriers && bufferBarrierCount > 0) lastBarrier = *bufferBarriers;
    ++barrierCount;
    order[callIndex++] = 2;
}

VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL FakeGdpa(VkDevice, const char* name);

VKAPI_ATTR VkResult VKAPI_CALL FakeCreateInstance(const VkInstanceCreateInfo* createInfo,
    const VkAllocationCallbacks*, VkInstance* instance) {
    Require(createInfo != nullptr && instance != nullptr, "create instance arguments");
    Require(createInfo->pNext != nullptr, "instance chain must be forwarded");
    const auto* info = static_cast<const LayerInstanceCreateInfo*>(createInfo->pNext);
    Require(info->sType == LayerInstanceCreateInfoType && info->function == LayerLinkInfo,
        "instance create info must survive forwarding");
    Require(info->u.pLayerInfo == nullptr, "this layer's instance link entry must be consumed");
    *instance = instanceHandle;
    return VK_SUCCESS;
}

VKAPI_ATTR VkResult VKAPI_CALL FakeCreateDevice(VkPhysicalDevice, const VkDeviceCreateInfo* createInfo,
    const VkAllocationCallbacks*, VkDevice* device) {
    Require(createInfo != nullptr && device != nullptr, "create device arguments");
    Require(createInfo->pNext != nullptr, "device chain must be forwarded");
    const auto* info = static_cast<const LayerDeviceCreateInfo*>(createInfo->pNext);
    Require(info->sType == LayerDeviceCreateInfoType && info->function == LayerLinkInfo,
        "device create info must survive forwarding");
    Require(info->u.pLayerInfo == nullptr, "this layer's device link entry must be consumed");
    *device = deviceHandle;
    return VK_SUCCESS;
}

VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL FakeGipa(VkInstance, const char* name) {
    if (!std::strcmp(name, "vkCreateInstance")) return reinterpret_cast<PFN_vkVoidFunction>(FakeCreateInstance);
    if (!std::strcmp(name, "vkCreateDevice")) return reinterpret_cast<PFN_vkVoidFunction>(FakeCreateDevice);
    if (!std::strcmp(name, "vkGetDeviceProcAddr")) return reinterpret_cast<PFN_vkVoidFunction>(FakeGdpa);
    return reinterpret_cast<PFN_vkVoidFunction>(FakeInstanceOther);
}

VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL FakeGdpa(VkDevice, const char* name) {
    if (!std::strcmp(name, "vkCmdCopyBuffer")) return reinterpret_cast<PFN_vkVoidFunction>(FakeCopy);
    if (!std::strcmp(name, "vkCmdPipelineBarrier")) return reinterpret_cast<PFN_vkVoidFunction>(FakeBarrier);
    if (!std::strcmp(name, "vkCreateBuffer")) return reinterpret_cast<PFN_vkVoidFunction>(FakeCreate);
    if (!std::strcmp(name, "vkDestroyBuffer")) return reinterpret_cast<PFN_vkVoidFunction>(FakeDestroy);
    if (!std::strcmp(name, "vkGetDeviceProcAddr")) return reinterpret_cast<PFN_vkVoidFunction>(LayerGetDeviceProcAddr);
    return reinterpret_cast<PFN_vkVoidFunction>(FakeDeviceOther);
}

// A broken lower chain that answers every query with this layer's own resolver.
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL FakeGdpaRecursive(VkDevice, const char*) {
    return reinterpret_cast<PFN_vkVoidFunction>(LayerGetDeviceProcAddr);
}

struct FakeChain {
    LayerInstanceLink instanceLink{};
    LayerInstanceCreateInfo instanceCreateInfo{};
    LayerDeviceLink deviceLink{};
    LayerDeviceCreateInfo deviceCreateInfo{};
    VkInstanceCreateInfo instanceCreate{};
    VkDeviceCreateInfo deviceCreate{};
};

FakeChain chain{};
VkInstance layerInstance = VK_NULL_HANDLE;
VkDevice layerDevice = VK_NULL_HANDLE;

void TestReset() {
    mode.store(ModeDisabled);
    modeLoaded.store(false);
    copyCalls.store(0);
    barrierCalls.store(0);
    skippedCalls.store(0);
    errorCount.store(0);
    nextInstanceProcAddr.store(nullptr);
    nextDeviceProcAddr.store(nullptr);
    realCmdCopyBuffer.store(nullptr);
    realCmdPipelineBarrier.store(nullptr);
    realCreateBuffer.store(nullptr);
    realDestroyBuffer.store(nullptr);
    {
        std::lock_guard<std::mutex> lock(bufferUsageMutex);
        bufferUsage.clear();
    }
    callIndex = 0;
    barrierCount = 0;
    destroyCalls = 0;
    captureBarrier = true;
    for (int& index : order) index = 0;
    layerInstance = VK_NULL_HANDLE;
    layerDevice = VK_NULL_HANDLE;
    chain = FakeChain{};
}

void Boot(int requested) {
    TestReset();
    SetEnvironmentVariableA("MAJ_SYNC_LAYER_MODE",
        requested == ModeNarrow ? "1" : requested == ModeBroad ? "2" : "0");
    LoadMode();
    Require(msl_mode() == requested, "mode must come from MAJ_SYNC_LAYER_MODE");

    chain.instanceLink.pfnNextGetInstanceProcAddr = FakeGipa;
    chain.instanceCreateInfo.sType = LayerInstanceCreateInfoType;
    chain.instanceCreateInfo.function = LayerLinkInfo;
    chain.instanceCreateInfo.u.pLayerInfo = &chain.instanceLink;
    chain.instanceCreate.sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO;
    chain.instanceCreate.pNext = &chain.instanceCreateInfo;
    Require(LayerCreateInstance(&chain.instanceCreate, nullptr, &layerInstance) == VK_SUCCESS,
        "instance creation through the loader link chain");
    Require(chain.instanceCreateInfo.u.pLayerInfo == nullptr, "instance link entry must be consumed");
    Require(nextInstanceProcAddr.load() == FakeGipa, "pNextGIPA must be retained");

    chain.deviceLink.pfnNextGetInstanceProcAddr = FakeGipa;
    chain.deviceLink.pfnNextGetDeviceProcAddr = FakeGdpa;
    chain.deviceCreateInfo.sType = LayerDeviceCreateInfoType;
    chain.deviceCreateInfo.function = LayerLinkInfo;
    chain.deviceCreateInfo.u.pLayerInfo = &chain.deviceLink;
    chain.deviceCreate.sType = VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO;
    chain.deviceCreate.pNext = &chain.deviceCreateInfo;
    Require(LayerCreateDevice(reinterpret_cast<VkPhysicalDevice>(1), &chain.deviceCreate, nullptr, &layerDevice)
        == VK_SUCCESS, "device creation through the loader link chain");
    Require(chain.deviceCreateInfo.u.pLayerInfo == nullptr, "device link entry must be consumed");
    Require(nextDeviceProcAddr.load() == FakeGdpa, "pNextGDPA must be retained");
}

VkBuffer MakeBuffer(VkBufferUsageFlags usage) {
    VkBufferCreateInfo createInfo{};
    createInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
    createInfo.usage = usage;
    VkBuffer buffer = VK_NULL_HANDLE;
    Require(LayerCreateBuffer(layerDevice, &createInfo, nullptr, &buffer) == VK_SUCCESS && buffer != VK_NULL_HANDLE,
        "buffer creation");
    return buffer;
}

bool UsageTracked(VkBuffer buffer) {
    std::lock_guard<std::mutex> lock(bufferUsageMutex);
    return bufferUsage.find(buffer) != bufferUsage.end();
}

bool BarrierIsWholeBufferOn(VkBuffer buffer) {
    return lastBarrier.sType == VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER
        && lastBarrier.buffer == buffer
        && lastBarrier.offset == 0
        && lastBarrier.size == VK_WHOLE_SIZE
        && lastBarrier.srcQueueFamilyIndex == VK_QUEUE_FAMILY_IGNORED
        && lastBarrier.dstQueueFamilyIndex == VK_QUEUE_FAMILY_IGNORED;
}

void CheckNegotiation() {
    TestReset();
    static_assert(static_cast<uint32_t>(NegotiateLayerInterfaceType) == 100000u,
        "VK_STRUCTURE_TYPE_NEGOTIATE_LAYER_INTERFACE_VERSION_1_1_0");
    NegotiateLayerInterface version{};
    version.sType = NegotiateLayerInterfaceType;
    version.loaderLayerInterfaceVersion = 2;
    Require(vkNegotiateLoaderLayerInterfaceVersion(&version) == VK_SUCCESS, "negotiation must succeed");
    Require(version.loaderLayerInterfaceVersion == 2, "layer must report interface version 2");
    Require(version.pfnGetInstanceProcAddr == LayerGetInstanceProcAddr, "negotiated vkGetInstanceProcAddr");
    Require(version.pfnGetDeviceProcAddr == LayerGetDeviceProcAddr, "negotiated vkGetDeviceProcAddr");
    Require(version.pfnGetPhysicalDeviceProcAddr == nullptr, "physical device resolver must stay empty");

    Require(vkNegotiateLoaderLayerInterfaceVersion(nullptr) == VK_ERROR_INITIALIZATION_FAILED,
        "a null negotiation structure must fail");
    Require(msl_errors() == 1, "the failed negotiation must be counted");

    // A version 1 loader only owns the shorter structure, so the layer must not claim
    // version 2 and must not write the physical device member.
    TestReset();
    NegotiateLayerInterface legacy{};
    legacy.sType = static_cast<VkStructureType>(100001);
    legacy.loaderLayerInterfaceVersion = 1;
    Require(vkNegotiateLoaderLayerInterfaceVersion(&legacy) == VK_SUCCESS, "version 1 negotiation must succeed");
    Require(legacy.loaderLayerInterfaceVersion == 1, "version 1 must be echoed back");
    Require(legacy.pfnGetInstanceProcAddr == LayerGetInstanceProcAddr
        && legacy.pfnGetDeviceProcAddr == LayerGetDeviceProcAddr, "version 1 functions");
    TestReset();
}

void CheckMode0Passthrough() {
    Boot(ModeDisabled);
    Require(msl_mode() == ModeDisabled, "mode 0");
    const auto index = MakeBuffer(VK_BUFFER_USAGE_INDEX_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT);
    const auto uniform = MakeBuffer(VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT);
    Require(UsageTracked(index) && UsageTracked(uniform), "usage is recorded in every mode");

    callIndex = 0;
    barrierCount = 0;
    LayerCmdCopyBuffer(commandBuffer, uniform, index, 1, regions);
    Require(callIndex == 1 && order[0] == 1, "mode 0 must forward exactly one copy");
    Require(barrierCount == 0 && msl_barrier_calls() == 0, "mode 0 must not inject a barrier");
    Require(lastCopyCommand == commandBuffer && lastCopySource == uniform && lastCopyDestination == index
        && lastCopyRegionCount == 1 && lastCopyRegions == regions, "mode 0 must not touch copy arguments");
    Require(msl_copy_calls() == 1 && msl_skipped_calls() == 0 && msl_errors() == 0, "mode 0 counters");

    Require(LayerGetInstanceProcAddr(layerInstance, "vkEnumeratePhysicalDevices")
        == reinterpret_cast<PFN_vkVoidFunction>(FakeInstanceOther), "GIPA must forward unknown names");
    Require(LayerGetDeviceProcAddr(layerDevice, "vkQueueSubmit")
        == reinterpret_cast<PFN_vkVoidFunction>(FakeDeviceOther), "GDPA must forward unknown names");
}

void CheckSelfQueries() {
    Boot(ModeNarrow);
    Require(LayerGetInstanceProcAddr(layerInstance, "vkGetInstanceProcAddr")
        == reinterpret_cast<PFN_vkVoidFunction>(LayerGetInstanceProcAddr), "GIPA self query must return the wrapper");
    Require(LayerGetDeviceProcAddr(layerDevice, "vkGetDeviceProcAddr")
        == reinterpret_cast<PFN_vkVoidFunction>(LayerGetDeviceProcAddr), "GDPA self query must return the wrapper");
    Require(LayerGetInstanceProcAddr(layerInstance, "vkGetDeviceProcAddr")
        == reinterpret_cast<PFN_vkVoidFunction>(LayerGetDeviceProcAddr),
        "GIPA query of the device resolver must return the wrapper");
    Require(LayerGetDeviceProcAddr(layerDevice, "vkCmdCopyBuffer")
        == reinterpret_cast<PFN_vkVoidFunction>(LayerCmdCopyBuffer), "GDPA must return the copy wrapper");
    Require(realCmdCopyBuffer.load() == FakeCopy, "a self query must not replace the cached inner copy");
    Require(realCreateBuffer.load() == FakeCreate && realDestroyBuffer.load() == FakeDestroy,
        "a self query must not replace the cached inner buffer entry points");
    Require(nextInstanceProcAddr.load() == FakeGipa && nextDeviceProcAddr.load() == FakeGdpa,
        "a self query must not replace the cached resolvers");
}

void CheckNarrowMode() {
    Boot(ModeNarrow);
    const auto index = MakeBuffer(VK_BUFFER_USAGE_INDEX_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT);
    const auto vertex = MakeBuffer(VK_BUFFER_USAGE_VERTEX_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT);
    const auto uniform = MakeBuffer(VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT);
    Require(realCmdCopyBuffer.load() == FakeCopy && realCmdPipelineBarrier.load() == FakeBarrier,
        "device entry points must be resolved when the device is created");

    callIndex = 0;
    barrierCount = 0;
    LayerCmdCopyBuffer(commandBuffer, uniform, index, 1, regions);
    Require(callIndex == 2 && order[0] == 1 && order[1] == 2, "the original copy must be recorded before the barrier");
    Require(barrierCount == 1 && msl_barrier_calls() == 1, "index buffer must be injected");
    Require(lastCopySource == uniform && lastCopyDestination == index && lastCopyRegions == regions,
        "copy arguments must be forwarded unchanged");
    Require(lastBarrierCommand == commandBuffer, "barrier must target the same command buffer");
    Require(BarrierIsWholeBufferOn(index), "barrier must cover the whole destination buffer");
    Require(lastBarrier.srcAccessMask == static_cast<VkAccessFlags>(VK_ACCESS_TRANSFER_WRITE_BIT),
        "srcAccessMask must be TRANSFER_WRITE");
    Require(lastBarrier.dstAccessMask == static_cast<VkAccessFlags>(
        VK_ACCESS_INDEX_READ_BIT | VK_ACCESS_VERTEX_ATTRIBUTE_READ_BIT),
        "dstAccessMask must be INDEX_READ|VERTEX_ATTRIBUTE_READ");
    Require(lastSourceStages == static_cast<VkPipelineStageFlags>(VK_PIPELINE_STAGE_TRANSFER_BIT)
        && lastDestinationStages == static_cast<VkPipelineStageFlags>(VK_PIPELINE_STAGE_VERTEX_INPUT_BIT),
        "narrow mode stages must be TRANSFER -> VERTEX_INPUT");
    Require(lastDependencyFlags == 0, "dependency flags");
    Require(lastMemoryBarrierCount == 0 && lastBufferBarrierCount == 1 && lastImageBarrierCount == 0,
        "exactly one buffer barrier and no memory/image barrier");

    callIndex = 0;
    barrierCount = 0;
    LayerCmdCopyBuffer(commandBuffer, uniform, vertex, 1, regions);
    Require(callIndex == 2 && order[0] == 1 && order[1] == 2, "vertex buffer copy ordering");
    Require(barrierCount == 1 && BarrierIsWholeBufferOn(vertex), "vertex buffer must be injected");

    callIndex = 0;
    barrierCount = 0;
    LayerCmdCopyBuffer(commandBuffer, uniform, uniform, 1, regions);
    Require(callIndex == 1 && order[0] == 1 && barrierCount == 0, "a uniform destination must not be injected");
    Require(msl_skipped_calls() == 1 && msl_errors() == 0, "the skip must be counted");
    Require(msl_barrier_calls() == 2, "barrier counter must match the two injections");

    // A buffer that was never created by this layer has no recorded usage.
    LayerCmdCopyBuffer(commandBuffer, uniform, reinterpret_cast<VkBuffer>(9001), 1, regions);
    Require(msl_skipped_calls() == 2 && barrierCount == 0, "an unrecorded destination must be skipped");
}

void CheckBroadMode() {
    Boot(ModeBroad);
    const auto uniform = MakeBuffer(VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT);
    const auto index = MakeBuffer(VK_BUFFER_USAGE_INDEX_BUFFER_BIT);
    callIndex = 0;
    barrierCount = 0;
    LayerCmdCopyBuffer(commandBuffer, uniform, uniform, 1, regions);
    Require(barrierCount == 1 && callIndex == 2 && order[0] == 1 && order[1] == 2, "broad mode must inject");
    Require(BarrierIsWholeBufferOn(uniform), "broad barrier range");
    Require(lastBarrier.srcAccessMask == static_cast<VkAccessFlags>(VK_ACCESS_TRANSFER_WRITE_BIT),
        "broad srcAccessMask");
    Require(lastBarrier.dstAccessMask == static_cast<VkAccessFlags>(VK_ACCESS_MEMORY_READ_BIT),
        "broad dstAccessMask must be MEMORY_READ");
    Require(lastSourceStages == static_cast<VkPipelineStageFlags>(VK_PIPELINE_STAGE_TRANSFER_BIT)
        && lastDestinationStages == static_cast<VkPipelineStageFlags>(VK_PIPELINE_STAGE_ALL_COMMANDS_BIT),
        "broad mode stages must be TRANSFER -> ALL_COMMANDS");
    Require(msl_skipped_calls() == 0, "broad mode must not skip any copy");

    barrierCount = 0;
    LayerCmdCopyBuffer(commandBuffer, uniform, index, 1, regions);
    Require(barrierCount == 1 && BarrierIsWholeBufferOn(index), "broad mode injects for index buffers too");
}

void CheckUsageLifetime() {
    Boot(ModeNarrow);
    const auto doomed = MakeBuffer(VK_BUFFER_USAGE_INDEX_BUFFER_BIT);
    Require(UsageTracked(doomed), "usage must be recorded when the buffer is created");
    Require(msl_errors() == 0, "buffer creation must not fail");

    LayerDestroyBuffer(layerDevice, doomed, nullptr);
    Require(destroyCalls == 1, "the driver destroy must be forwarded");
    Require(!UsageTracked(doomed), "usage must be forgotten when the buffer is destroyed");

    callIndex = 0;
    barrierCount = 0;
    LayerCmdCopyBuffer(commandBuffer, doomed, doomed, 1, regions);
    Require(callIndex == 1 && order[0] == 1 && barrierCount == 0 && msl_skipped_calls() == 1,
        "a destroyed buffer must not be injected");
}

void CheckFailurePaths() {
    // Resolvers have no chain before the instance is created.
    TestReset();
    Require(LayerGetInstanceProcAddr(nullptr, "vkEnumerateInstanceVersion") == nullptr,
        "an unresolved instance chain must answer null");
    Require(LayerGetDeviceProcAddr(nullptr, "vkQueueSubmit") == nullptr,
        "an unresolved device chain must answer null");

    // A create call that is not part of a loader chain must fail closed instead of
    // handing the application a device the layer cannot forward to.
    TestReset();
    VkInstanceCreateInfo plainInstance{};
    plainInstance.sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO;
    VkInstance rejectedInstance = VK_NULL_HANDLE;
    Require(LayerCreateInstance(&plainInstance, nullptr, &rejectedInstance) == VK_ERROR_INITIALIZATION_FAILED,
        "an instance create without link information must fail");
    const auto afterInstance = msl_errors();
    Require(afterInstance == 1, "the rejected instance create must be counted");

    VkDeviceCreateInfo plainDevice{};
    plainDevice.sType = VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO;
    VkDevice rejectedDevice = VK_NULL_HANDLE;
    Require(LayerCreateDevice(reinterpret_cast<VkPhysicalDevice>(1), &plainDevice, nullptr, &rejectedDevice)
        == VK_ERROR_INITIALIZATION_FAILED, "a device create without link information must fail");
    Require(msl_errors() == afterInstance + 1, "the rejected device create must be counted");

    // An unresolved copy entry point is reported, never silently dropped.
    Boot(ModeNarrow);
    const auto index = MakeBuffer(VK_BUFFER_USAGE_INDEX_BUFFER_BIT);
    realCmdCopyBuffer.store(nullptr);
    const auto beforeCopy = msl_errors();
    callIndex = 0;
    barrierCount = 0;
    LayerCmdCopyBuffer(commandBuffer, index, index, 1, regions);
    Require(msl_errors() == beforeCopy + 1, "an unresolved copy entry point must be reported");
    Require(callIndex == 0 && barrierCount == 0 && msl_copy_calls() == 1, "nothing can be forwarded or injected");

    // A missing barrier entry point is reported after the copy itself is forwarded.
    Boot(ModeNarrow);
    const auto secondIndex = MakeBuffer(VK_BUFFER_USAGE_INDEX_BUFFER_BIT);
    realCmdPipelineBarrier.store(nullptr);
    const auto beforeBarrier = msl_errors();
    callIndex = 0;
    barrierCount = 0;
    LayerCmdCopyBuffer(commandBuffer, secondIndex, secondIndex, 1, regions);
    Require(callIndex == 1 && order[0] == 1 && barrierCount == 0, "the original copy must still be forwarded");
    Require(msl_errors() == beforeBarrier + 1 && msl_barrier_calls() == 0, "the missing barrier must be reported");

    // A recursive lower resolver cannot replace the cached real entry point.
    Boot(ModeNarrow);
    nextDeviceProcAddr.store(FakeGdpaRecursive);
    const auto beforeRecursive = msl_errors();
    Require(LayerGetDeviceProcAddr(layerDevice, "vkCmdCopyBuffer")
        == reinterpret_cast<PFN_vkVoidFunction>(LayerCmdCopyBuffer), "a recursive resolver still yields the wrapper");
    Require(msl_errors() == beforeRecursive + 1, "the recursive resolver must be reported");
    Require(realCmdCopyBuffer.load() == FakeCopy, "the recursive resolver must not replace the cached copy");
    Require(LayerGetDeviceProcAddr(layerDevice, "vkCmdPipelineBarrier") == nullptr,
        "a recursive resolver must not hand back a mismatched wrapper");
}
} // namespace

int main() {
    try {
        static_assert(sizeof(void*) == 8 && sizeof(uint64_t) == 8, "x64 ABI");

        CheckNegotiation();
        std::cout << "ok - negotiation: interface version 2, GIPA/GDPA filled, physical device resolver empty\n";

        CheckMode0Passthrough();
        std::cout << "ok - mode 0: copies forwarded unchanged, no barrier, counters\n";

        CheckSelfQueries();
        std::cout << "ok - self queries return the wrapper and never cache the inner entry points\n";

        CheckNarrowMode();
        std::cout << "ok - mode 1: index/vertex injected with exact fields, uniform skipped\n";

        CheckBroadMode();
        std::cout << "ok - mode 2: every copy injected with ALL_COMMANDS/MEMORY_READ\n";

        CheckUsageLifetime();
        std::cout << "ok - usage recording: create/destroy tracked, destroyed buffer skipped\n";

        CheckFailurePaths();
        std::cout << "ok - failures: unresolved chain, missing link info, missing entry points, recursion\n";

        TestReset();
        SetEnvironmentVariableA("MAJ_SYNC_LAYER_MODE", "2");
        Require(msl_mode() == ModeBroad, "the mode must be read from the environment after a reload");
        SetEnvironmentVariableA("MAJ_SYNC_LAYER_MODE", "0");
        TestReset();
        Require(msl_mode() == ModeDisabled, "an explicit 0 must mean mode 0");
        SetEnvironmentVariableA("MAJ_SYNC_LAYER_MODE", nullptr);
        TestReset();
        Require(msl_mode() == ModeDisabled, "an unset MAJ_SYNC_LAYER_MODE must mean mode 0");
        std::cout << "PASS: sync layer state tests (fake entry points only; no loader, device or GPU work)\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "FAIL: " << error.what() << '\n';
        return 1;
    }
}
