// Host-only tests. Fake Vulkan/Unity entry points; no loader, device or GPU work.
#include "Probe.cpp"
#include <iostream>
#include <stdexcept>

namespace {
int order[4]{};
int callIndex = 0;
int registrations = 0, removals = 0, lifecycle = 0;
VkBuffer const indexBuffer = reinterpret_cast<VkBuffer>(101);
VkBuffer const uniformBuffer = reinterpret_cast<VkBuffer>(102);
VkCommandBuffer const command = reinterpret_cast<VkCommandBuffer>(201);
VkBufferCopy regions[1]{{0, 0, 936}};
VkBufferMemoryBarrier lastBarrier{};
VkPipelineStageFlags lastSourceStages = 0, lastDestinationStages = 0;
int barrierCount = 0;
bool captureBarrier = true;

void Require(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
void VKAPI_CALL Other() {}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL FakeGdpa(VkDevice, const char*);
uintptr_t nextHandle = 1000;
VKAPI_ATTR VkResult VKAPI_CALL FakeCreate(VkDevice, const VkBufferCreateInfo* info,
    const VkAllocationCallbacks*, VkBuffer* buffer) {
    (void)info;
    *buffer = reinterpret_cast<VkBuffer>(++nextHandle);
    return VK_SUCCESS;
}
VKAPI_ATTR void VKAPI_CALL FakeDestroy(VkDevice, VkBuffer, const VkAllocationCallbacks*) { order[callIndex++] = 3; }
VKAPI_ATTR void VKAPI_CALL FakeCopy(VkCommandBuffer c, VkBuffer s, VkBuffer d, uint32_t n, const VkBufferCopy* r) {
    Require(c == command && n == 1 && r == regions && regions[0].size == 936, "copy arguments changed");
    (void)s; (void)d;
    order[callIndex++] = 1;
}
VKAPI_ATTR void VKAPI_CALL FakeBarrier(VkCommandBuffer c, VkPipelineStageFlags src, VkPipelineStageFlags dst,
    VkDependencyFlags, uint32_t memoryCount, const VkMemoryBarrier*, uint32_t bufferCount,
    const VkBufferMemoryBarrier* buffer, uint32_t, const VkImageMemoryBarrier*) {
    Require(c == command && memoryCount == 0 && bufferCount == 1, "barrier type");
    if (captureBarrier) { lastBarrier = *buffer; lastSourceStages = src; lastDestinationStages = dst; }
    ++barrierCount;
    order[callIndex++] = 2;
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL FakeGipa(VkInstance, const char* name) {
    if (!std::strcmp(name, "vkGetDeviceProcAddr")) return reinterpret_cast<PFN_vkVoidFunction>(FakeGdpa);
    if (!std::strcmp(name, "vkCmdCopyBuffer")) return reinterpret_cast<PFN_vkVoidFunction>(FakeCopy);
    if (!std::strcmp(name, "vkCmdPipelineBarrier")) return reinterpret_cast<PFN_vkVoidFunction>(FakeBarrier);
    if (!std::strcmp(name, "vkCreateBuffer")) return reinterpret_cast<PFN_vkVoidFunction>(FakeCreate);
    if (!std::strcmp(name, "vkDestroyBuffer")) return reinterpret_cast<PFN_vkVoidFunction>(FakeDestroy);
    if (!std::strcmp(name, "vkGetInstanceProcAddr")) return reinterpret_cast<PFN_vkVoidFunction>(GetInstance);
    return Other;
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL FakeGdpa(VkDevice, const char* name) {
    if (!std::strcmp(name, "vkCmdCopyBuffer")) return reinterpret_cast<PFN_vkVoidFunction>(FakeCopy);
    if (!std::strcmp(name, "vkCmdPipelineBarrier")) return reinterpret_cast<PFN_vkVoidFunction>(FakeBarrier);
    if (!std::strcmp(name, "vkCreateBuffer")) return reinterpret_cast<PFN_vkVoidFunction>(FakeCreate);
    if (!std::strcmp(name, "vkDestroyBuffer")) return reinterpret_cast<PFN_vkVoidFunction>(FakeDestroy);
    if (!std::strcmp(name, "vkGetDeviceProcAddr")) return reinterpret_cast<PFN_vkVoidFunction>(GetDevice);
    return Other;
}
UnityGfxRenderer UNITY_INTERFACE_API Renderer() { return kUnityGfxRendererVulkan; }
UnityVulkanInstance UNITY_INTERFACE_API InstanceValue() {
    UnityVulkanInstance value{};
    value.instance = reinterpret_cast<VkInstance>(1);
    value.device = reinterpret_cast<VkDevice>(2);
    return value;
}
bool UNITY_INTERFACE_API Add(UnityVulkanInitCallback, void*, int32_t priority) {
    Require(priority == 0x40000000, "priority"); ++registrations; return true;
}
bool UNITY_INTERFACE_API Remove(UnityVulkanInitCallback) { ++removals; return true; }
void UNITY_INTERFACE_API RegisterDevice(IUnityGraphicsDeviceEventCallback) { ++lifecycle; }
void UNITY_INTERFACE_API UnregisterDevice(IUnityGraphicsDeviceEventCallback) { --lifecycle; }
IUnityGraphics fakeGraphics{};
IUnityGraphicsVulkanV2 fakeVulkan{};
IUnityInterface* UNITY_INTERFACE_API Lookup(unsigned long long high, unsigned long long low) {
    const auto g = GetUnityInterfaceGUID<IUnityGraphics>();
    const auto v = GetUnityInterfaceGUID<IUnityGraphicsVulkanV2>();
    if (high == g.m_GUIDHigh && low == g.m_GUIDLow) return &fakeGraphics;
    if (high == v.m_GUIDHigh && low == v.m_GUIDLow) return &fakeVulkan;
    return nullptr;
}
void Reset() {
    mode = ModeDisabled; registered = false; active = false; fault = 0;
    nextInstance = nullptr; nextDevice = nullptr; copyBuffer = nullptr; pipelineBarrier = nullptr;
    createBuffer = nullptr; destroyBuffer = nullptr;
    copyCalls = 0; barrierCalls = 0; errors = 0; skippedCalls = 0;
    { std::lock_guard<std::mutex> lock(bufferMutex); bufferUsage.clear(); }
    callIndex = 0; order[0] = order[1] = order[2] = order[3] = 0; barrierCount = 0; captureBarrier = true;
    lifecycle = 0;
    graphics = &fakeGraphics; vulkan = &fakeVulkan;
}
void Boot(int requested) {
    Reset();
    SetEnvironmentVariableA("MAJ_SYNTHETIC_BUFFER_BARRIER", requested == ModeNarrow ? "1" : requested == ModeBroad ? "2" : "0");
    IUnityInterfaces interfaces{}; interfaces.GetInterfaceSplit = Lookup;
    UnityPluginLoad(&interfaces);
    if (requested != ModeDisabled) { Initialize(FakeGipa, nullptr); DeviceEvent(kUnityGfxDeviceEventInitialize); }
}
VkBuffer MakeBuffer(VkBufferUsageFlags usage) {
    VkBufferCreateInfo info{}; info.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO; info.usage = usage;
    VkBuffer buffer = VK_NULL_HANDLE;
    Require(CreateBuffer(reinterpret_cast<VkDevice>(2), &info, nullptr, &buffer) == VK_SUCCESS && buffer, "create");
    return buffer;
}
}
int main() {
    try {
        static_assert(sizeof(void*) == 8 && sizeof(uint64_t) == 8, "x64 ABI");
        fakeGraphics.GetRenderer = Renderer; fakeGraphics.RegisterDeviceEventCallback = RegisterDevice;
        fakeGraphics.UnregisterDeviceEventCallback = UnregisterDevice;
        fakeVulkan.Instance = InstanceValue; fakeVulkan.AddInterceptInitialization = Add;
        fakeVulkan.RemoveInterceptInitialization = Remove;

        Boot(ModeDisabled);
        Require(bb_abi_version() == 2 && bb_mode() == ModeDisabled && bb_status() == 1 && registrations == 0,
            "disabled mode installed a hook");
        CopyBuffer(command, indexBuffer, indexBuffer, 1, regions);
        Require(barrierCount == 0, "disabled mode injected a barrier");

        Boot(ModeNarrow);
        Require(bb_mode() == ModeNarrow && registrations == 1 && lifecycle == 1, "narrow registration");
        const auto index = MakeBuffer(VK_BUFFER_USAGE_INDEX_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT);
        const auto uniform = MakeBuffer(VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT);
        callIndex = 0;
        CopyBuffer(command, uniform, index, 1, regions);
        Require(callIndex == 2 && order[0] == 1 && order[1] == 2, "copy must precede the narrow barrier");
        Require(lastBarrier.buffer == index && lastBarrier.size == VK_WHOLE_SIZE && lastBarrier.offset == 0,
            "narrow barrier range");
        Require(lastBarrier.srcAccessMask == VK_ACCESS_TRANSFER_WRITE_BIT
            && lastBarrier.dstAccessMask == VK_ACCESS_INDEX_READ_BIT, "narrow access masks");
        Require(lastSourceStages == VK_PIPELINE_STAGE_TRANSFER_BIT
            && lastDestinationStages == VK_PIPELINE_STAGE_VERTEX_INPUT_BIT, "narrow stages");
        Require(lastBarrier.srcQueueFamilyIndex == VK_QUEUE_FAMILY_IGNORED
            && lastBarrier.dstQueueFamilyIndex == VK_QUEUE_FAMILY_IGNORED, "queue ownership");
        callIndex = 0; barrierCount = 0;
        CopyBuffer(command, uniform, uniform, 1, regions);
        Require(callIndex == 1 && order[0] == 1 && barrierCount == 0 && bb_skipped_calls() == 1,
            "narrow mode must skip buffers without index/vertex usage");

        Boot(ModeBroad);
        Require(bb_mode() == ModeBroad, "broad mode");
        const auto broadUniform = MakeBuffer(VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT);
        CopyBuffer(command, broadUniform, broadUniform, 1, regions);
        Require(barrierCount == 1 && lastDestinationStages == VK_PIPELINE_STAGE_ALL_COMMANDS_BIT
            && lastBarrier.dstAccessMask == VK_ACCESS_MEMORY_READ_BIT, "broad mode fields");

        Boot(ModeNarrow);
        const auto doomed = MakeBuffer(VK_BUFFER_USAGE_INDEX_BUFFER_BIT);
        DestroyBuffer(reinterpret_cast<VkDevice>(2), doomed, nullptr);
        copyBuffer.store(FakeCopy); pipelineBarrier.store(FakeBarrier);
        active.store(true);
        callIndex = 0; barrierCount = 0;
        CopyBuffer(command, doomed, doomed, 1, regions);
        Require(barrierCount == 0 && bb_skipped_calls() == 1, "destroyed buffer usage must be forgotten");

        Boot(ModeNarrow);
        DeviceEvent(kUnityGfxDeviceEventShutdown);
        callIndex = 0; barrierCount = 0;
        CopyBuffer(command, indexBuffer, indexBuffer, 1, regions);
        Require(callIndex == 1 && barrierCount == 0 && bb_status() == NoDevice, "inactive device injected work");
        UnityPluginUnload();
        Require(removals == 1 && lifecycle == 0, "unload");
        SetEnvironmentVariableA("MAJ_SYNTHETIC_BUFFER_BARRIER", nullptr);
        std::cout << "PASS: narrow/broad/disabled modes, copy ordering, range and stage fields, usage tracking, shutdown\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "FAIL: " << error.what() << '\n';
        return 1;
    }
}
