#define VK_NO_PROTOTYPES
#include "IUnityGraphics.h"
#include "IUnityGraphicsVulkan.h"
#include <array>
#include <cstdint>
#include <memory>
#include <mutex>

#define MF_EXPORT extern "C" __declspec(dllexport)
#define MF_CALL __cdecl

namespace {
constexpr int Invalid = -10001, Unavailable = -10002, OldGeneration = -10003;
constexpr int Duplicate = -10004, Internal = -10005;
enum class Phase { Created, Queued, Submitted, Failed };
struct Device {
    UnityVulkanInstance instance{};
    PFN_vkCreateFence create = nullptr;
    PFN_vkDestroyFence destroy = nullptr;
    PFN_vkGetFenceStatus poll = nullptr;
    PFN_vkQueueSubmit submit = nullptr;
    uint64_t generation = 0;
    bool active = false;
};
struct Ticket {
    uintptr_t token = 0;
    std::shared_ptr<Device> device;
    VkFence fence = VK_NULL_HANDLE;
    Phase phase = Phase::Created;
    int error = 0;
    bool ownerRetired = false;
};
std::mutex gate;
std::array<std::unique_ptr<Ticket>, 4> tickets;
std::shared_ptr<Device> current;
IUnityGraphics* graphics = nullptr;
IUnityGraphicsVulkanV2* vulkan = nullptr;
uintptr_t nextToken = 1;
uint64_t nextGeneration = 1;
int eventId = -1;
int status = Unavailable;

template<class T> T* Interface(IUnityInterfaces* interfaces) {
    const auto id = GetUnityInterfaceGUID<T>();
    return static_cast<T*>(interfaces->GetInterfaceSplit(id.m_GUIDHigh, id.m_GUIDLow));
}
Ticket* Find(void* opaque) {
    const auto token = reinterpret_cast<uintptr_t>(opaque);
    for (auto& ticket : tickets) {
        if (ticket && ticket->token == token) return ticket.get();
    }
    return nullptr;
}
bool Active(const Ticket& ticket) {
    return ticket.device->active && current == ticket.device;
}
// Called only with the state gate held and a valid active device.
int Poll(Ticket& ticket) {
    if (!Active(ticket)) return OldGeneration;
    if (ticket.ownerRetired) return Invalid;
    if (ticket.error) return ticket.error;
    if (ticket.phase != Phase::Submitted) return 0;
    const auto result = ticket.device->poll(ticket.device->instance.device, ticket.fence);
    if (result == VK_SUCCESS) return 1;
    if (result == VK_NOT_READY) return 0;
    ticket.error = static_cast<int>(result);
    return ticket.error;
}
void UNITY_INTERFACE_API QueueMarker(int id, void* opaque) {
    std::lock_guard<std::mutex> lock(gate);
    auto* ticket = Find(opaque);
    if (!ticket || ticket->ownerRetired) return;
    if (id != eventId || ticket->phase != Phase::Queued) {
        ticket->error = Duplicate;
        return;
    }
    if (!Active(*ticket)) {
        ticket->error = OldGeneration;
        return;
    }
    // Unity AccessQueue supplies external queue synchronization. No resource work is added.
    // A fence signal covers earlier queue submissions in submission order, including this empty submit.
    VkSubmitInfo submit{};
    submit.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
    const auto result = ticket->device->submit(ticket->device->instance.graphicsQueue, 1, &submit, ticket->fence);
    ticket->phase = result == VK_SUCCESS ? Phase::Submitted : Phase::Failed;
    ticket->error = result == VK_SUCCESS ? 0 : static_cast<int>(result);
}
void UNITY_INTERFACE_API RenderMarker(int id, void* opaque) {
    IUnityGraphicsVulkanV2* access = nullptr;
    {
        std::lock_guard<std::mutex> lock(gate);
        auto* ticket = Find(opaque);
        if (!ticket || ticket->ownerRetired) return;
        if (id != eventId || ticket->phase != Phase::Created) {
            ticket->error = Duplicate;
            return;
        }
        if (!Active(*ticket) || !vulkan || !vulkan->AccessQueue) {
            ticket->error = OldGeneration;
            return;
        }
        ticket->phase = Phase::Queued;
        access = vulkan;
    }
    // May invoke QueueMarker synchronously, so never hold the state gate here.
    access->AccessQueue(QueueMarker, id, opaque, true);
}
void UNITY_INTERFACE_API DeviceEvent(UnityGfxDeviceEventType event) {
    std::lock_guard<std::mutex> lock(gate);
    if (event == kUnityGfxDeviceEventShutdown || event == kUnityGfxDeviceEventBeforeReset) {
        if (current) current->active = false;
        current.reset();
        status = Unavailable;
        // No device wait and no destruction of potentially submitted fences. Their slots stay occupied.
        return;
    }
    if (event != kUnityGfxDeviceEventInitialize && event != kUnityGfxDeviceEventAfterReset) return;
    if (current && current->active) return;
    status = Unavailable;
    if (!graphics || graphics->GetRenderer() != kUnityGfxRendererVulkan || !vulkan || !vulkan->Instance
        || !vulkan->AccessQueue || !vulkan->ConfigureEvent || eventId < 0) return;
    std::shared_ptr<Device> device;
    try { device = std::make_shared<Device>(); }
    catch (...) { status = Internal; return; }
    device->instance = vulkan->Instance();
    if (!device->instance.device || !device->instance.graphicsQueue || !device->instance.getInstanceProcAddr) return;
    const auto getDevice = reinterpret_cast<PFN_vkGetDeviceProcAddr>(
        device->instance.getInstanceProcAddr(device->instance.instance, "vkGetDeviceProcAddr"));
    if (!getDevice) return;
    device->create = reinterpret_cast<PFN_vkCreateFence>(getDevice(device->instance.device, "vkCreateFence"));
    device->destroy = reinterpret_cast<PFN_vkDestroyFence>(getDevice(device->instance.device, "vkDestroyFence"));
    device->poll = reinterpret_cast<PFN_vkGetFenceStatus>(getDevice(device->instance.device, "vkGetFenceStatus"));
    device->submit = reinterpret_cast<PFN_vkQueueSubmit>(getDevice(device->instance.device, "vkQueueSubmit"));
    if (!device->create || !device->destroy || !device->poll || !device->submit) return;
    UnityVulkanPluginEventConfig config{};
    config.renderPassPrecondition = kUnityVulkanRenderPass_EnsureOutside;
    config.graphicsQueueAccess = kUnityVulkanGraphicsQueueAccess_DontCare;
    config.flags = kUnityVulkanEventConfigFlag_EnsurePreviousFrameSubmission;
    vulkan->ConfigureEvent(eventId, &config);
    device->generation = nextGeneration++;
    device->active = true;
    current = device;
    status = 0;
}
} // namespace

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* interfaces) {
    if (!interfaces) return;
    graphics = Interface<IUnityGraphics>(interfaces);
    vulkan = Interface<IUnityGraphicsVulkanV2>(interfaces);
    if (!graphics || !graphics->ReserveEventIDRange) return;
    eventId = graphics->ReserveEventIDRange(1);
    graphics->RegisterDeviceEventCallback(DeviceEvent);
    DeviceEvent(kUnityGfxDeviceEventInitialize);
}
extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload() {
    if (graphics) graphics->UnregisterDeviceEventCallback(DeviceEvent);
    DeviceEvent(kUnityGfxDeviceEventShutdown);
    std::lock_guard<std::mutex> lock(gate);
    graphics = nullptr;
    vulkan = nullptr;
}
MF_EXPORT int MF_CALL mf_abi_version() { return 1; }
MF_EXPORT int MF_CALL mf_status() {
    std::lock_guard<std::mutex> lock(gate);
    return status;
}
MF_EXPORT int MF_CALL mf_event_id() {
    std::lock_guard<std::mutex> lock(gate);
    return eventId;
}
MF_EXPORT UnityRenderingEventAndData MF_CALL mf_render_callback() { return RenderMarker; }
MF_EXPORT void* MF_CALL mf_ticket_create() {
    std::lock_guard<std::mutex> lock(gate);
    if (status != 0 || !current || !current->active || nextToken == 0) return nullptr;
    for (auto& slot : tickets) {
        if (slot) continue;
        std::unique_ptr<Ticket> ticket;
        try { ticket = std::make_unique<Ticket>(); }
        catch (...) { status = Internal; return nullptr; }
        ticket->device = current;
        VkFenceCreateInfo info{};
        info.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
        const auto result = current->create(current->instance.device, &info, nullptr, &ticket->fence);
        if (result != VK_SUCCESS) { status = static_cast<int>(result); return nullptr; }
        ticket->token = nextToken++;
        auto* opaque = reinterpret_cast<void*>(ticket->token);
        slot = std::move(ticket);
        return opaque;
    }
    return nullptr;
}
MF_EXPORT int MF_CALL mf_ticket_poll(void* opaque) {
    std::lock_guard<std::mutex> lock(gate);
    auto* ticket = Find(opaque);
    return ticket ? Poll(*ticket) : Invalid;
}
MF_EXPORT int MF_CALL mf_ticket_release(void* opaque) {
    std::lock_guard<std::mutex> lock(gate);
    auto* ticket = Find(opaque);
    if (!ticket || ticket->ownerRetired) return 0;
    if (!Active(*ticket)) return 0;
    const auto result = Poll(*ticket);
    if (result == VK_ERROR_DEVICE_LOST) {
        ticket->ownerRetired = true;
        return VK_ERROR_DEVICE_LOST; // Host owner retired; retained fence/slot never authorizes reuse.
    }
    if (ticket->phase != Phase::Created && result != 1) return 0;
    ticket->device->destroy(ticket->device->instance.device, ticket->fence, nullptr);
    for (auto& slot : tickets) {
        if (slot.get() == ticket) { slot.reset(); return 1; }
    }
    return Internal;
}
