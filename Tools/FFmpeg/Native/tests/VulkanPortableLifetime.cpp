// Deterministic tests of the portable Vulkan retirement policy. No Vulkan
// loader, GPU, FFmpeg decoder or Unity runtime is used by this fixture.
#include "../VulkanPortable.cpp"
#include <cstdio>
#include <cstdlib>
#include <future>
#include <type_traits>

int FfuEventId(int event) { return event; }
PFN_vkVoidFunction FfuVulkanVideoInstanceProc(PFN_vkGetInstanceProcAddr loader, VkInstance instance, const char* name) {
    return loader(instance, name);
}
VkResult FfuVulkanVideoCreateDevice(PFN_vkCreateDevice create, PFN_vkGetInstanceProcAddr, VkInstance,
    VkPhysicalDevice physical, const VkDeviceCreateInfo* info, const VkAllocationCallbacks* allocation, VkDevice* device) {
    return create(physical, info, allocation, device);
}

#define REQUIRE(value) do { if (!(value)) { std::printf("FAIL line %d: %s\n", __LINE__, #value); std::exit(1); } } while (0)
namespace {
VkResult fenceResult = VK_NOT_READY;
int fenceQueries = 0, resourcesDestroyed = 0, ownersReleased = 0;

// Non-dispatchable handles are pointers on 64-bit Vulkan and integers on 32-bit.
template<class T> T TestHandle(uintptr_t value) {
    if constexpr (std::is_pointer_v<T>) {
        return reinterpret_cast<T>(value);
    } else {
        return static_cast<T>(value);
    }
}
VKAPI_ATTR VkResult VKAPI_CALL FenceStatus(VkDevice, VkFence) {
    ++fenceQueries;
    return fenceResult;
}
VKAPI_ATTR void VKAPI_CALL DestroyFence(VkDevice, VkFence, const VkAllocationCallbacks*) {
    ++resourcesDestroyed;
}
VKAPI_ATTR void VKAPI_CALL DestroyPool(VkDevice, VkCommandPool, const VkAllocationCallbacks*) {
    ++resourcesDestroyed;
}

// Model a packet whose actual QueueSubmit succeeded and whose GPU fence has
// not yet signaled. Its owner represents the retained decoder/imported image.
void SetCurrent(const std::shared_ptr<Context>& context) {
    std::lock_guard<std::mutex> lock(contextMutex);
    current = context;
    status = 0;
}
void ClearCurrent() {
    std::lock_guard<std::mutex> lock(contextMutex);
    current.reset();
    status = 100;
}
std::shared_ptr<Context> PendingJob(Presenter* presenter) {
    REQUIRE(jobs.empty() && FfuVkPendingCount() == 0);
    fenceResult = VK_NOT_READY;
    fenceQueries = resourcesDestroyed = ownersReleased = 0;
    auto context = std::make_shared<Context>();
    SetCurrent(context);
    context->GetFenceStatus = FenceStatus;
    context->DestroyFence = DestroyFence;
    context->DestroyCommandPool = DestroyPool;
    auto* job = new PortableJob();
    job->context = context;
    job->presenter = presenter;
    presenter->Retain();
    job->sample.owner = std::shared_ptr<void>(new int(0), [](void* value) {
        delete static_cast<int*>(value);
        ++ownersReleased;
    });
    job->fence = TestHandle<VkFence>(1);
    job->commandPool = TestHandle<VkCommandPool>(2);
    job->submitted = true;
    ++context->inFlight;
    jobs.push_back(job);
    return context;
}
void VerifyInactiveRetention() {
    auto* presenter = new Presenter();
    auto context = PendingJob(presenter);
    FfuVkPoll(false);
    REQUIRE(fenceQueries == 1 && ownersReleased == 0 && resourcesDestroyed == 0 && FfuVkPendingCount() == 1);
    // Shutdown can time out with this job pending, then marks its context
    // inactive. That flag is not proof that the device or GPU work was lost.
    context->active = false;
    for (int i = 0; i < 100; ++i) {
        FfuVkPoll(false);
    }
    FfuVkPoll(true);
    REQUIRE(fenceQueries == 1 && ownersReleased == 0 && resourcesDestroyed == 0 && FfuVkPendingCount() == 0);
    // Restore the modeled device only to clean up this test fixture. Production
    // shutdown never reactivates a generation or frees its abandoned packets.
    context->active = true;
    fenceResult = VK_SUCCESS;
    FfuVkPoll(false);
    REQUIRE(ownersReleased == 1 && resourcesDestroyed == 2 && FfuVkPendingCount() == 0);
    presenter->Drop();
    ClearCurrent();
    std::puts("PASS: inactive Vulkan generation retains pending GPU resources and image owner, including drain and repeated polls");
}
void VerifyQueryResults() {
    auto* presenter = new Presenter();
    auto context = PendingJob(presenter);
    fenceResult = VK_ERROR_OUT_OF_HOST_MEMORY;
    FfuVkPoll(false);
    REQUIRE(presenter->error == VK_ERROR_OUT_OF_HOST_MEMORY && ownersReleased == 0 && resourcesDestroyed == 0 && FfuVkPendingCount() == 1);
    fenceResult = VK_SUCCESS;
    FfuVkPoll(false);
    REQUIRE(ownersReleased == 1 && resourcesDestroyed == 2 && FfuVkPendingCount() == 0);
    context = PendingJob(presenter);
    fenceResult = VK_ERROR_DEVICE_LOST;
    FfuVkPoll(false);
    REQUIRE(fenceQueries == 1 && ownersReleased == 1 && resourcesDestroyed == 2 && FfuVkPendingCount() == 0);
    presenter->Drop();
    ClearCurrent();
    std::puts("PASS: only a completed fence or an actual device-lost query retires a submitted Vulkan packet");
}
// Reproduce the public lock order used by the video/VAAPI/Android importers:
// prepare holds resources while it enters FfuVkPrepare; shutdown must not hold
// contextMutex while waiting for those same resources.
void VerifyPrepareShutdownLockOrder() {
    REQUIRE(jobs.empty() && FfuVkPendingCount() == 0 && !FfuVkCurrent());
    auto context = std::make_shared<Context>();
    auto* presenter = new Presenter();
    FfuVkSample sample;
    sample.context = context;
    sample.planeCount = sample.width = sample.height = 1;
    sample.image[0] = TestHandle<VkImage>(1);
    sample.view[0] = TestHandle<VkImageView>(2);
    sample.sampler[0] = TestHandle<VkSampler>(3);
    sample.owner = std::make_shared<int>(0);
    {
        std::lock_guard<std::mutex> lock(contextMutex);
        current = context;
        status = 0;
    }
    std::promise<void> resourcesHeld, resumePrepare, prepared, stopped;
    auto resourcesHeldFuture = resourcesHeld.get_future();
    auto resumePrepareFuture = resumePrepare.get_future();
    auto preparedFuture = prepared.get_future();
    auto stoppedFuture = stopped.get_future();
    std::thread prepare([&] {
        std::lock_guard<std::recursive_mutex> resources(context->resources);
        resourcesHeld.set_value();
        resumePrepareFuture.wait();
        REQUIRE(!FfuVkPrepare(presenter, sample, presenter, false, false));
        prepared.set_value();
    });
    REQUIRE(resourcesHeldFuture.wait_for(std::chrono::seconds(5)) == std::future_status::ready);
    std::thread shutdown([&] {
        FfuVkShutdown();
        stopped.set_value();
    });
    bool shutdownReachedContext = false;
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
    while (!shutdownReachedContext && std::chrono::steady_clock::now() < deadline) {
        std::unique_lock<std::mutex> lock(contextMutex, std::try_to_lock);
        // Old shutdown blocks with contextMutex held. Correct shutdown detaches
        // current and releases that mutex before waiting for resources.
        if (lock.owns_lock()) {
            shutdownReachedContext = !current;
            lock.unlock();
        }
        std::this_thread::yield();
    }
    REQUIRE(shutdownReachedContext);
    resumePrepare.set_value();
    REQUIRE(preparedFuture.wait_for(std::chrono::seconds(5)) == std::future_status::ready);
    REQUIRE(stoppedFuture.wait_for(std::chrono::seconds(5)) == std::future_status::ready);
    prepare.join();
    shutdown.join();
    REQUIRE(!FfuVkCurrent() && !context->active && FfuVkStatus() == 100 && FfuVkPendingCount() == 0);
    presenter->Drop();
    ClearCurrent();
    std::puts("PASS: resource-locked prepare and concurrent shutdown complete without ABBA deadlock or a stale-generation packet");
}
}
int main() {
    VerifyInactiveRetention();
    VerifyQueryResults();
    VerifyPrepareShutdownLockOrder();
}
