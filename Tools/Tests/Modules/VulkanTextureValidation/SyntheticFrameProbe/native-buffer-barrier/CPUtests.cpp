// Host-only tests. These fake entry points never load Vulkan or create a GPU device.
#include "Probe.cpp"
#include <array>
#include <iostream>
#include <stdexcept>

namespace {
std::array<int, 8> order{};
int callIndex = 0, registrations = 0, removals = 0, lifecycleRegistrations = 0;
bool missingCopy = false, missingBarrier = false, selfResolver = false;
VkCommandBuffer expectedCommand = reinterpret_cast<VkCommandBuffer>(11);
VkBuffer expectedSource = reinterpret_cast<VkBuffer>(12), expectedDestination = reinterpret_cast<VkBuffer>(13);
VkBufferCopy inputRegions[2]{{3, 7, 936}, {1000, 2000, 12}};
void Require(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
void VKAPI_CALL Other() {}
void VKAPI_CALL FakeCopy(VkCommandBuffer command, VkBuffer source, VkBuffer destination,
    uint32_t count, const VkBufferCopy* regions) {
    Require(command == expectedCommand && source == expectedSource && destination == expectedDestination,
        "copy handles changed");
    Require(count == 2 && regions == inputRegions, "copy count or region pointer changed");
    Require(std::memcmp(regions, inputRegions, sizeof(inputRegions)) == 0, "copy region contents changed");
    order[callIndex++] = 1;
}
void VKAPI_CALL FakeBarrier(VkCommandBuffer command, VkPipelineStageFlags sourceStage,
    VkPipelineStageFlags destinationStage, VkDependencyFlags flags, uint32_t memoryCount,
    const VkMemoryBarrier* memory, uint32_t bufferCount, const VkBufferMemoryBarrier* buffer,
    uint32_t imageCount, const VkImageMemoryBarrier* image) {
    Require(command == expectedCommand && sourceStage == VK_PIPELINE_STAGE_TRANSFER_BIT
        && destinationStage == VK_PIPELINE_STAGE_ALL_COMMANDS_BIT && flags == 0, "stage or dependency flags");
    Require(memoryCount == 0 && memory == nullptr && bufferCount == 1 && buffer != nullptr
        && imageCount == 0 && image == nullptr, "barrier count/type");
    Require(buffer->sType == VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER && buffer->pNext == nullptr,
        "buffer barrier structure");
    Require(buffer->srcAccessMask == VK_ACCESS_TRANSFER_WRITE_BIT && buffer->dstAccessMask == VK_ACCESS_MEMORY_READ_BIT,
        "access masks");
    Require(buffer->srcQueueFamilyIndex == VK_QUEUE_FAMILY_IGNORED
        && buffer->dstQueueFamilyIndex == VK_QUEUE_FAMILY_IGNORED, "queue ownership changed");
    Require(buffer->buffer == expectedDestination && buffer->offset == 0 && buffer->size == VK_WHOLE_SIZE,
        "destination range");
    order[callIndex++] = 2;
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL FakeGipa(VkInstance, const char*);
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL FakeGdpa(VkDevice, const char*);
PFN_vkVoidFunction Resolve(const char* name) {
    if (!std::strcmp(name, "missing")) return nullptr;
    if (!std::strcmp(name, "vkCmdCopyBuffer")) return missingCopy ? nullptr : reinterpret_cast<PFN_vkVoidFunction>(FakeCopy);
    if (!std::strcmp(name, "vkCmdPipelineBarrier")) return missingBarrier ? nullptr : reinterpret_cast<PFN_vkVoidFunction>(FakeBarrier);
    if (!std::strcmp(name, "vkGetInstanceProcAddr"))
        return reinterpret_cast<PFN_vkVoidFunction>(selfResolver ? GetInstance : FakeGipa);
    if (!std::strcmp(name, "vkGetDeviceProcAddr"))
        return reinterpret_cast<PFN_vkVoidFunction>(selfResolver ? GetDevice : FakeGdpa);
    return Other;
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL FakeGipa(VkInstance, const char* name) { return Resolve(name); }
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL FakeGdpa(VkDevice, const char* name) { return Resolve(name); }
UnityGfxRenderer UNITY_INTERFACE_API Renderer() { return kUnityGfxRendererVulkan; }
UnityVulkanInstance UNITY_INTERFACE_API Instance() {
    UnityVulkanInstance value{};
    value.instance = reinterpret_cast<VkInstance>(1);
    value.device = reinterpret_cast<VkDevice>(2);
    return value;
}
bool UNITY_INTERFACE_API Add(UnityVulkanInitCallback, void*, int32_t priority) {
    Require(priority == 0x40000000, "interceptor priority"); ++registrations; return true;
}
bool UNITY_INTERFACE_API Remove(UnityVulkanInitCallback) { ++removals; return true; }
void UNITY_INTERFACE_API RegisterDevice(IUnityGraphicsDeviceEventCallback) { ++lifecycleRegistrations; }
void UNITY_INTERFACE_API UnregisterDevice(IUnityGraphicsDeviceEventCallback) { --lifecycleRegistrations; }
IUnityGraphics fakeGraphics{};
IUnityGraphicsVulkanV2 fakeVulkan{};
IUnityInterface* UNITY_INTERFACE_API Lookup(unsigned long long high, unsigned long long low) {
    const auto graphicsId = GetUnityInterfaceGUID<IUnityGraphics>();
    const auto vulkanId = GetUnityInterfaceGUID<IUnityGraphicsVulkanV2>();
    if (high == graphicsId.m_GUIDHigh && low == graphicsId.m_GUIDLow) return &fakeGraphics;
    if (high == vulkanId.m_GUIDHigh && low == vulkanId.m_GUIDLow) return &fakeVulkan;
    return nullptr;
}
void Reset() {
    enabled = false; registered = false; active = false; fault = 0;
    nextInstance = nullptr; nextDevice = nullptr; copyBuffer = nullptr; pipelineBarrier = nullptr;
    copyCalls = 0; barrierCalls = 0; errors = 0;
    callIndex = 0; order.fill(0); missingCopy = missingBarrier = selfResolver = false;
    graphics = &fakeGraphics; vulkan = &fakeVulkan;
}
void Ready() {
    enabled = true; registered = true;
    Require(Initialize(FakeGipa, nullptr) == GetInstance, "initialization wrapper");
    DeviceEvent(kUnityGfxDeviceEventInitialize);
    Require(bb_status() == 0, "enabled device readiness");
}
void InvokeCopy() { CopyBuffer(expectedCommand, expectedSource, expectedDestination, 2, inputRegions); }
}
int main() {
    try {
        static_assert(sizeof(void*) == 8 && sizeof(uint64_t) == 8, "Windows x64 ABI");
        fakeGraphics.GetRenderer = Renderer;
        fakeGraphics.RegisterDeviceEventCallback = RegisterDevice;
        fakeGraphics.UnregisterDeviceEventCallback = UnregisterDevice;
        fakeVulkan.Instance = Instance;
        fakeVulkan.AddInterceptInitialization = Add;
        fakeVulkan.RemoveInterceptInitialization = Remove;
        IUnityInterfaces interfaces{}; interfaces.GetInterfaceSplit = Lookup;
        SetEnvironmentVariableA("MAJ_SYNTHETIC_BUFFER_BARRIER", "0");
        UnityPluginLoad(&interfaces);
        Require(bb_mode() == 0 && bb_status() == 1 && registrations == 0 && lifecycleRegistrations == 0,
            "disabled installed a hook");
        Reset();
        SetEnvironmentVariableA("MAJ_SYNTHETIC_BUFFER_BARRIER", "1");
        UnityPluginLoad(&interfaces);
        Require(bb_mode() == 1 && registrations == 1 && lifecycleRegistrations == 1, "enabled registration");
        Require(bb_status() == NoDevice, "premature ready status");
        Initialize(FakeGipa, nullptr); DeviceEvent(kUnityGfxDeviceEventInitialize);
        const auto before = inputRegions[0];
        InvokeCopy();
        Require(callIndex == 2 && order[0] == 1 && order[1] == 2, "copy/barrier order");
        Require(inputRegions[0].srcOffset == before.srcOffset && inputRegions[0].dstOffset == before.dstOffset
            && inputRegions[0].size == before.size && bb_copy_calls() == 1 && bb_barrier_calls() == 1
            && bb_errors() == 0, "data or counters");
        Require(GetInstance(nullptr, "vkGetInstanceProcAddr") == reinterpret_cast<PFN_vkVoidFunction>(GetInstance), "GIPA self");
        Require(GetInstance(nullptr, "vkGetDeviceProcAddr") == reinterpret_cast<PFN_vkVoidFunction>(GetDevice), "GIPA GDPA");
        Require(GetDevice(nullptr, "vkGetDeviceProcAddr") == reinterpret_cast<PFN_vkVoidFunction>(GetDevice), "GDPA self");
        Require(GetDevice(nullptr, "vkGetInstanceProcAddr") == reinterpret_cast<PFN_vkVoidFunction>(GetInstance), "GDPA GIPA");
        Require(GetInstance(nullptr, "missing") == nullptr && GetDevice(nullptr, "missing") == nullptr, "NULL changed");
        Require(GetInstance(nullptr, "other") == Other && GetDevice(nullptr, "other") == Other, "unrelated function changed");
        Require(GetDevice(nullptr, "vkCmdPipelineBarrier") == reinterpret_cast<PFN_vkVoidFunction>(FakeBarrier), "original barrier chain changed");
        UnityPluginUnload(); Require(removals == 1 && lifecycleRegistrations == 0, "unload registration");
        Reset(); Ready(); enabled = false; InvokeCopy();
        Require(callIndex == 1 && order[0] == 1 && bb_barrier_calls() == 0, "disabled appended command");
        Reset(); enabled = true; registered = true; missingBarrier = true;
        Initialize(FakeGipa, nullptr); DeviceEvent(kUnityGfxDeviceEventInitialize); InvokeCopy();
        Require(bb_status() == NoBarrier && bb_errors() >= 1 && callIndex == 1, "missing barrier was silent");
        Reset(); enabled = true; registered = true; missingCopy = true;
        Initialize(FakeGipa, nullptr); DeviceEvent(kUnityGfxDeviceEventInitialize); InvokeCopy();
        Require(bb_status() == NoCopy && bb_errors() >= 1 && callIndex == 0, "missing copy was silent");
        Reset(); Ready(); selfResolver = true;
        Require(GetDevice(nullptr, "vkGetDeviceProcAddr") == reinterpret_cast<PFN_vkVoidFunction>(GetDevice)
            && nextDevice.load() == FakeGdpa && bb_status() == RecursiveResolver, "recursive GDPA was cached");
        Require(GetInstance(nullptr, "vkGetInstanceProcAddr") == reinterpret_cast<PFN_vkVoidFunction>(GetInstance)
            && nextInstance.load() == FakeGipa, "recursive GIPA was cached");
        Reset(); Ready(); DeviceEvent(kUnityGfxDeviceEventShutdown); InvokeCopy();
        Require(callIndex == 1 && bb_barrier_calls() == 0 && bb_status() == NoDevice, "inactive device injected work");
        SetEnvironmentVariableA("MAJ_SYNTHETIC_BUFFER_BARRIER", nullptr);
        std::cout << "PASS: fake-only copy preservation/order/barrier/disabled/missing-PFN/resolver/lifecycle checks\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "FAIL: " << error.what() << '\n'; return 1;
    }
}
