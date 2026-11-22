// Deterministic host-only state checks: all Vulkan functions below are fakes.
#include "Marker.cpp"
#include <iostream>
#include <stdexcept>
namespace {
int created = 0, destroyed = 0, queried = 0, submitted = 0;
VkResult completion = VK_NOT_READY;
void* deferred = nullptr;
VkResult VKAPI_CALL FakeCreate(VkDevice, const VkFenceCreateInfo*, const VkAllocationCallbacks*, VkFence* fence) {
    *fence = reinterpret_cast<VkFence>(static_cast<uintptr_t>(++created));
    return VK_SUCCESS;
}
void VKAPI_CALL FakeDestroy(VkDevice, VkFence, const VkAllocationCallbacks*) { ++destroyed; }
VkResult VKAPI_CALL FakePoll(VkDevice, VkFence) { ++queried; return completion; }
VkResult VKAPI_CALL FakeSubmit(VkQueue, uint32_t count, const VkSubmitInfo* info, VkFence) {
    if (count != 1 || info->commandBufferCount != 0) throw std::runtime_error("marker was not empty");
    ++submitted;
    return VK_SUCCESS;
}
void UNITY_INTERFACE_API FakeAccess(UnityRenderingEventAndData, int, void* token, bool flush) {
    if (!flush) throw std::runtime_error("Unity commands were not flushed");
    deferred = token;
}
void Require(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
}
int main() {
    try {
        static_assert(sizeof(void*) == 8, "x64 ABI required");
        auto device = std::make_shared<Device>();
        device->instance.device = reinterpret_cast<VkDevice>(1);
        device->instance.graphicsQueue = reinterpret_cast<VkQueue>(2);
        device->active = true; device->generation = 1;
        device->create = FakeCreate; device->destroy = FakeDestroy;
        device->poll = FakePoll; device->submit = FakeSubmit;
        IUnityGraphicsVulkanV2 fakeUnity{}; fakeUnity.AccessQueue = FakeAccess;
        current = device; vulkan = &fakeUnity; status = 0; eventId = 50;
        Require(mf_abi_version() == 1 && mf_status() == 0, "ABI/status");
        auto* cancelled = mf_ticket_create();
        Require(cancelled && mf_ticket_release(cancelled) == 1, "created cancellation");
        RenderMarker(50, cancelled);
        Require(deferred == nullptr && mf_ticket_poll(cancelled) == Invalid, "late event after cancel");
        auto* ticket = mf_ticket_create();
        Require(ticket != cancelled && mf_ticket_poll(ticket) == 0, "token ABA/presubmit poll");
        RenderMarker(50, ticket);
        Require(deferred == ticket && mf_ticket_release(ticket) == 0, "deferred callback ownership");
        QueueMarker(50, deferred);
        Require(submitted == 1 && mf_ticket_poll(ticket) == 0 && mf_ticket_release(ticket) == 0, "pending fence");
        completion = VK_SUCCESS;
        Require(mf_ticket_poll(ticket) == 1 && mf_ticket_release(ticket) == 1, "completed retirement");
        Require(destroyed == 2, "destroy count");
        auto* lost = mf_ticket_create();
        RenderMarker(50, lost); QueueMarker(50, deferred);
        Require(mf_ticket_poll(lost) == 1, "pre-loss completion observation");
        completion = VK_ERROR_DEVICE_LOST;
        Require(mf_ticket_release(lost) == VK_ERROR_DEVICE_LOST && mf_ticket_poll(lost) == Invalid,
            "device loss during release must never grant reuse");
        std::array<void*, 3> held{};
        for (auto& item : held) item = mf_ticket_create();
        Require(mf_ticket_create() == nullptr, "resident bound includes device-loss retention");
        const int queriesBeforeShutdown = queried;
        DeviceEvent(kUnityGfxDeviceEventShutdown);
        for (auto* item : held) {
            Require(mf_ticket_poll(item) == OldGeneration && mf_ticket_release(item) == 0, "old generation rejection");
        }
        Require(queried == queriesBeforeShutdown && destroyed == 2 && mf_ticket_create() == nullptr,
            "shutdown must not query/destroy unknown fences or grow retention");
        std::cout << "PASS: host-only marker state, cancellation, deferred ownership, completion, bound, shutdown\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "FAIL: " << error.what() << '\n';
        return 1;
    }
}
