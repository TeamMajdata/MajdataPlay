// Standalone Unity Vulkan plug-in that uploads CPU RGBA8 pixels into a Unity-owned VkImage with the plug-in's
// own persistent staging buffer and vkCmdCopyBufferToImage. It exists so that the video frame path does not
// have to go through Texture2D.LoadRawTextureData + Apply, whose Vulkan implementation reproduces a driver
// hang (nvlddmkm event 153) on the target machine.
//
// The plug-in never submits work of its own: it reserves staging slots on the calling (main) thread, copies
// the pixels there, and records one vkCmdCopyBufferToImage plus a layout barrier into Unity's command buffer
// from a render event configured with graphics-queue access. A slot is reused only after Unity reported a safe
// frame number that covers the frame whose command buffer read it, so nothing ever waits for the GPU.
#define VK_NO_PROTOTYPES
#include "StagingRing.h"
#include "TextureUpload.h"
#include "IUnityGraphics.h"
#include "IUnityGraphicsVulkan.h"

#include <cstdint>
#include <cstring>
#include <mutex>

#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#endif

namespace
{
using majvku::MaxSlots;
using majvku::PendingUpload;
using majvku::StagingRing;

// Simultaneous staging buffers kept alive after a resolution growth. A retired buffer that cannot be added
// here is left to Unity's device teardown instead of being destroyed while a command buffer may read it.
constexpr int MaxRetiredBuffers = 4;

// Guards every mutable field below. Enqueue holds it only for bookkeeping; the multi-megabyte pixel copy runs
// under copyGate so a render event never waits for it.
std::mutex stateGate;
std::mutex copyGate;

IUnityInterfaces* unityInterfaces = nullptr;
IUnityGraphics* unityGraphics = nullptr;
IUnityGraphicsVulkanV2* unityVulkan = nullptr;
int uploadEventId = 0;

StagingRing ring;

// Unity's Vulkan device and the entry points this plug-in calls. Copied under stateGate so the render event
// works from a stable snapshot.
struct DeviceFunctions
{
    bool valid = false;
    UnityVulkanInstance instance{};
    PFN_vkCreateBuffer createBuffer = nullptr;
    PFN_vkDestroyBuffer destroyBuffer = nullptr;
    PFN_vkGetBufferMemoryRequirements getBufferMemoryRequirements = nullptr;
    PFN_vkAllocateMemory allocateMemory = nullptr;
    PFN_vkFreeMemory freeMemory = nullptr;
    PFN_vkBindBufferMemory bindBufferMemory = nullptr;
    PFN_vkMapMemory mapMemory = nullptr;
    PFN_vkUnmapMemory unmapMemory = nullptr;
    PFN_vkCmdCopyBufferToImage cmdCopyBufferToImage = nullptr;
    PFN_vkCmdPipelineBarrier cmdPipelineBarrier = nullptr;
    PFN_vkGetPhysicalDeviceMemoryProperties getMemoryProperties = nullptr;
    VkPhysicalDeviceMemoryProperties memory{};
};

DeviceFunctions device;

// Persistent staging buffer: host-visible, host-coherent, mapped once, split into MaxSlots equally sized slots.
VkBuffer stagingBuffer = VK_NULL_HANDLE;
VkDeviceMemory stagingMemory = VK_NULL_HANDLE;
uint8_t* stagingMapped = nullptr;
// Incremented whenever the device state is dropped, so a copy that overlaps a device reset is discarded.
uint64_t deviceGeneration = 0;

struct RetiredBuffer
{
    VkBuffer buffer = VK_NULL_HANDLE;
    VkDeviceMemory memory = VK_NULL_HANDLE;
    uint64_t frame = 0;
    bool referenced = false;
};

RetiredBuffer retired[MaxRetiredBuffers]{};
int retiredCount = 0;
uint64_t lastRecordedFrame = 0;
bool lastRecordedFrameValid = false;
bool warnedUnsupported = false;

// Returns the requested Unity interface, or null when the running player does not expose it.
template <class T>
T* Interface(IUnityInterfaces* interfaces)
{
    const auto id = GetUnityInterfaceGUID<T>();
    return static_cast<T*>(interfaces->GetInterfaceSplit(id.m_GUIDHigh, id.m_GUIDLow));
}

// Reports the one permanent diagnostic of this plug-in. Unity players have no console, so it goes to the
// debugger output only.
void WarnUnsupportedOnce()
{
    if (warnedUnsupported)
    {
        return;
    }

    warnedUnsupported = true;
#if defined(_WIN32)
    ::OutputDebugStringA(
        "[MajdataPlayTextureUpload] Refusing uploads for a texture whose Vulkan image is not a "
        "single-sample 2D RGBA8 image of the upload size; the caller must fall back.\n");
#endif
}

// Copies tightly packed RGBA8 rows into the staging slot.
void CopyPixels(uint8_t* destination, const void* pixels, int height, uint64_t rowBytes, uint64_t sourcePitch)
{
    const auto* source = static_cast<const uint8_t*>(pixels);
    if (sourcePitch == rowBytes)
    {
        std::memcpy(destination, source, static_cast<size_t>(rowBytes * static_cast<uint64_t>(height)));
        return;
    }

    for (int y = 0; y < height; ++y)
    {
        std::memcpy(destination + rowBytes * static_cast<uint64_t>(y),
            source + sourcePitch * static_cast<uint64_t>(y), static_cast<size_t>(rowBytes));
    }
}

// Whether this plug-in can copy an RGBA8 upload into the image Unity returned.
bool SupportedImage(const UnityVulkanImage& image, uint32_t width, uint32_t height)
{
    if (image.image == VK_NULL_HANDLE)
    {
        return false;
    }

    if (image.format != VK_FORMAT_R8G8B8A8_UNORM && image.format != VK_FORMAT_R8G8B8A8_SRGB)
    {
        return false;
    }

    if ((image.usage & VK_IMAGE_USAGE_TRANSFER_DST_BIT) == 0)
    {
        return false;
    }

    if (image.type != VK_IMAGE_TYPE_2D || image.samples != VK_SAMPLE_COUNT_1_BIT)
    {
        return false;
    }

    if (image.extent.width != width || image.extent.height != height)
    {
        return false;
    }

    if (image.mipCount < 1 || image.layers < 1)
    {
        return false;
    }

    // Unity sets the aspect of a colour texture; tolerate the zero value of older builds.
    return image.aspect == 0 || image.aspect == VK_IMAGE_ASPECT_COLOR_BIT;
}

// Releases retired staging buffers whose last recorded frame Unity called safe. Runs on the render thread
// before anything is recorded, so the buffer being destroyed is never the one about to be recorded into.
void CollectRetiredBuffers()
{
    if (!device.valid)
    {
        return;
    }

    const auto vkDevice = device.instance.device;
    const auto safeFrame = ring.SafeFrame();
    for (int i = 0; i < retiredCount;)
    {
        const auto& entry = retired[i];
        // A buffer is retired while a render event may still be recording the next frame into the buffer this
        // plug-in had captured, so it is destroyed only two frames after the last frame known at retirement.
        const bool safe = !entry.referenced || (safeFrame != 0 && safeFrame >= entry.frame + 2);
        if (!safe)
        {
            ++i;
            continue;
        }

        device.unmapMemory(vkDevice, entry.memory);
        device.destroyBuffer(vkDevice, entry.buffer, nullptr);
        device.freeMemory(vkDevice, entry.memory, nullptr);
        retired[i] = retired[--retiredCount];
    }
}

// Remembers the current staging buffer as retired so it is destroyed once Unity retires its frames.
void RetireStagingBuffer()
{
    if (stagingBuffer == VK_NULL_HANDLE)
    {
        return;
    }

    if (retiredCount == MaxRetiredBuffers)
    {
        // Several resolutions grew in quick succession. The oldest buffer stays mapped until Unity destroys
        // the device; that is preferable to destroying a buffer a queued command buffer may still read.
        return;
    }

    auto& entry = retired[retiredCount++];
    entry.buffer = stagingBuffer;
    entry.memory = stagingMemory;
    entry.frame = lastRecordedFrame;
    entry.referenced = lastRecordedFrameValid;
    stagingBuffer = VK_NULL_HANDLE;
    stagingMemory = VK_NULL_HANDLE;
    stagingMapped = nullptr;
}

// Creates or grows the persistent staging buffer. Returns false when the driver refuses the allocation.
bool PrepareStaging(uint64_t bytes)
{
    if (!device.valid)
    {
        return false;
    }

    if (stagingBuffer != VK_NULL_HANDLE && !ring.NeedsGrowth(bytes))
    {
        return true;
    }

    const auto stride = StagingRing::RequiredStride(bytes);
    const auto vkDevice = device.instance.device;
    VkBufferCreateInfo bufferInfo{};
    bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
    bufferInfo.size = stride * static_cast<uint64_t>(MaxSlots);
    bufferInfo.usage = VK_BUFFER_USAGE_TRANSFER_SRC_BIT;
    bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;

    VkBuffer buffer = VK_NULL_HANDLE;
    if (device.createBuffer(vkDevice, &bufferInfo, nullptr, &buffer) != VK_SUCCESS)
    {
        return false;
    }

    VkMemoryRequirements requirements{};
    device.getBufferMemoryRequirements(vkDevice, buffer, &requirements);

    constexpr VkMemoryPropertyFlags hostVisible =
        VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT;
    uint32_t type = 0;
    for (; type < device.memory.memoryTypeCount; ++type)
    {
        const bool allowed = (requirements.memoryTypeBits & (1u << type)) != 0;
        const bool coherent = (device.memory.memoryTypes[type].propertyFlags & hostVisible) == hostVisible;
        if (allowed && coherent)
        {
            break;
        }
    }

    if (type == device.memory.memoryTypeCount)
    {
        device.destroyBuffer(vkDevice, buffer, nullptr);
        return false;
    }

    VkMemoryAllocateInfo allocation{};
    allocation.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    allocation.allocationSize = requirements.size;
    allocation.memoryTypeIndex = type;
    VkDeviceMemory memory = VK_NULL_HANDLE;
    if (device.allocateMemory(vkDevice, &allocation, nullptr, &memory) != VK_SUCCESS)
    {
        device.destroyBuffer(vkDevice, buffer, nullptr);
        return false;
    }

    void* mapped = nullptr;
    if (device.bindBufferMemory(vkDevice, buffer, memory, 0) != VK_SUCCESS ||
        device.mapMemory(vkDevice, memory, 0, requirements.size, 0, &mapped) != VK_SUCCESS || mapped == nullptr)
    {
        device.freeMemory(vkDevice, memory, nullptr);
        device.destroyBuffer(vkDevice, buffer, nullptr);
        return false;
    }

    RetireStagingBuffer();
    stagingBuffer = buffer;
    stagingMemory = memory;
    stagingMapped = static_cast<uint8_t*>(mapped);
    ring.Configure(stride, MaxSlots);
    return true;
}

// Reads the interface, applies the event configuration and loads the device entry points. Returns false while
// Unity has no Vulkan device yet.
bool AcquireDevice()
{
    if (unityVulkan == nullptr && unityInterfaces != nullptr)
    {
        unityVulkan = Interface<IUnityGraphicsVulkanV2>(unityInterfaces);
        if (unityVulkan != nullptr && unityVulkan->ConfigureEvent != nullptr)
        {
            // The event records copies into Unity's own command buffer, so it needs graphics-queue access and
            // must not run inside a render pass.
            UnityVulkanPluginEventConfig config{};
            config.renderPassPrecondition = kUnityVulkanRenderPass_EnsureOutside;
            config.graphicsQueueAccess = kUnityVulkanGraphicsQueueAccess_Allow;
            config.flags = kUnityVulkanEventConfigFlag_EnsurePreviousFrameSubmission |
                kUnityVulkanEventConfigFlag_ModifiesCommandBuffersState;
            unityVulkan->ConfigureEvent(uploadEventId, &config);
        }
    }

    if (unityVulkan == nullptr)
    {
        return false;
    }

    const auto instance = unityVulkan->Instance();
    if (instance.instance == VK_NULL_HANDLE || instance.physicalDevice == VK_NULL_HANDLE ||
        instance.device == VK_NULL_HANDLE || instance.getInstanceProcAddr == nullptr)
    {
        return false;
    }

    const auto getDeviceProc = reinterpret_cast<PFN_vkGetDeviceProcAddr>(
        instance.getInstanceProcAddr(instance.instance, "vkGetDeviceProcAddr"));
    if (getDeviceProc == nullptr)
    {
        return false;
    }

    DeviceFunctions loaded;
#define MAJVKU_LOAD(member, name) \
    loaded.member = reinterpret_cast<PFN_vk##name>(getDeviceProc(instance.device, "vk" #name)); \
    if (loaded.member == nullptr) return false;
    MAJVKU_LOAD(createBuffer, CreateBuffer)
    MAJVKU_LOAD(destroyBuffer, DestroyBuffer)
    MAJVKU_LOAD(getBufferMemoryRequirements, GetBufferMemoryRequirements)
    MAJVKU_LOAD(allocateMemory, AllocateMemory)
    MAJVKU_LOAD(freeMemory, FreeMemory)
    MAJVKU_LOAD(bindBufferMemory, BindBufferMemory)
    MAJVKU_LOAD(mapMemory, MapMemory)
    MAJVKU_LOAD(unmapMemory, UnmapMemory)
    MAJVKU_LOAD(cmdCopyBufferToImage, CmdCopyBufferToImage)
    MAJVKU_LOAD(cmdPipelineBarrier, CmdPipelineBarrier)
#undef MAJVKU_LOAD

    loaded.getMemoryProperties = reinterpret_cast<PFN_vkGetPhysicalDeviceMemoryProperties>(
        instance.getInstanceProcAddr(instance.instance, "vkGetPhysicalDeviceMemoryProperties"));
    if (loaded.getMemoryProperties == nullptr)
    {
        return false;
    }

    loaded.instance = instance;
    loaded.getMemoryProperties(instance.physicalDevice, &loaded.memory);
    loaded.valid = true;
    device = loaded;
    ++deviceGeneration;
    return true;
}

// Makes the device state usable, or returns false when it is not available yet.
bool EnsureDevice()
{
    return device.valid || AcquireDevice();
}

// Forgets everything that belongs to Unity's device. No Vulkan object is destroyed: Unity tears the device
// down right after its shutdown event, so the staging buffer and its mapping simply go away with it.
void ForgetDevice()
{
    device = DeviceFunctions{};
    stagingBuffer = VK_NULL_HANDLE;
    stagingMemory = VK_NULL_HANDLE;
    stagingMapped = nullptr;
    retiredCount = 0;
    lastRecordedFrame = 0;
    lastRecordedFrameValid = false;
    ++deviceGeneration;
    ring.Forget();
}

// Releases a slot whose upload cannot be recorded and, for an unsupported image, refuses the texture.
void DropUpload(int slot, void* texture, bool reject)
{
    std::lock_guard<std::mutex> lock(stateGate);
    ring.MarkDropped(slot, texture, reject);
    if (reject)
    {
        WarnUnsupportedOnce();
    }
}

// Records one staged upload into Unity's command buffer and leaves the image ready for fragment sampling.
void RecordUpload(IUnityGraphicsVulkanV2* vulkan, const DeviceFunctions& functions, VkBuffer staging,
    const PendingUpload& upload)
{
    // Probe first: ObserveOnly reports the image without recording a barrier, so an unsupported image is
    // refused before Unity transitions it.
    UnityVulkanImage probe{};
    if (!vulkan->AccessTexture(upload.texture, UnityVulkanWholeImage, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
            VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, VK_ACCESS_SHADER_READ_BIT,
            kUnityVulkanResourceAccess_ObserveOnly, &probe) ||
        !SupportedImage(probe, upload.width, upload.height))
    {
        DropUpload(upload.slot, upload.texture, true);
        return;
    }

    UnityVulkanImage image{};
    if (!vulkan->AccessTexture(upload.texture, UnityVulkanWholeImage, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
            VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_WRITE_BIT,
            kUnityVulkanResourceAccess_PipelineBarrier, &image) ||
        !SupportedImage(image, upload.width, upload.height))
    {
        DropUpload(upload.slot, upload.texture, true);
        return;
    }

    // AccessTexture records commands, so the recording state has to be queried again afterwards.
    UnityVulkanRecordingState state{};
    if (!vulkan->CommandRecordingState(&state, kUnityVulkanGraphicsQueueAccess_Allow) ||
        state.commandBuffer == VK_NULL_HANDLE)
    {
        DropUpload(upload.slot, upload.texture, false);
        return;
    }

    VkBufferImageCopy copy{};
    copy.bufferOffset = upload.offset;
    copy.bufferRowLength = 0;   // rows of the staging slot are tightly packed
    copy.bufferImageHeight = 0; // the staging slot holds a tightly packed image
    copy.imageSubresource = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1};
    copy.imageOffset = {0, 0, 0};
    copy.imageExtent = {upload.width, upload.height, 1};
    functions.cmdCopyBufferToImage(state.commandBuffer, staging, image.image,
        VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &copy);

    VkImageMemoryBarrier release{};
    release.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
    release.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
    release.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
    release.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
    release.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    release.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    release.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    release.image = image.image;
    release.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
    functions.cmdPipelineBarrier(state.commandBuffer, VK_PIPELINE_STAGE_TRANSFER_BIT,
        VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, 0, 0, nullptr, 0, nullptr, 1, &release);

    // Unity tracks image layouts itself. Telling it about the layout this plug-in left the image in keeps the
    // next frame's transition real; otherwise AccessTexture would see TRANSFER_DST_OPTIMAL as already current
    // and the copy would run against a shader-read image. Unity records a redundant barrier for this call.
    UnityVulkanImage reacquired{};
    const bool tracked = vulkan->AccessTexture(upload.texture, UnityVulkanWholeImage,
        VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, VK_ACCESS_SHADER_READ_BIT,
        kUnityVulkanResourceAccess_PipelineBarrier, &reacquired);

    {
        std::lock_guard<std::mutex> lock(stateGate);
        ring.MarkRecorded(upload.slot, state.currentFrameNumber);
        lastRecordedFrame = state.currentFrameNumber;
        lastRecordedFrameValid = true;
        if (!tracked)
        {
            // Unity no longer resolves this texture; stop touching it instead of leaving a stale layout behind.
            ring.Reject(upload.texture);
        }
    }
}

// Unity render event: records every queued upload into the frame's command buffer. Runs on the render thread
// with graphics-queue access and never waits on the GPU or on a mutex held by a pixel copy.
void UNITY_INTERFACE_API OnUploadEvent(int eventId)
{
    if (eventId != uploadEventId)
    {
        return;
    }

    auto* vulkan = unityVulkan;
    if (vulkan == nullptr)
    {
        return;
    }

    UnityVulkanRecordingState state{};
    if (!vulkan->CommandRecordingState(&state, kUnityVulkanGraphicsQueueAccess_Allow))
    {
        // Unity has no command buffer this frame. Keep the uploads queued for the next event instead of
        // dropping a frame; the caller falls back on its own when no slot frees up.
        return;
    }

    DeviceFunctions functions;
    VkBuffer staging = VK_NULL_HANDLE;
    {
        std::lock_guard<std::mutex> lock(stateGate);
        if (!device.valid)
        {
            ring.DropPending();
            return;
        }

        ring.ObserveSafeFrame(state.safeFrameNumber);
        CollectRetiredBuffers();
        functions = device;
        staging = stagingBuffer;
    }

    if (staging == VK_NULL_HANDLE)
    {
        return;
    }

    for (;;)
    {
        PendingUpload upload{};
        {
            std::lock_guard<std::mutex> lock(stateGate);
            if (!ring.TakePending(&upload))
            {
                return;
            }
        }

        RecordUpload(unityVulkan, functions, staging, upload);
    }
}

// Unity graphics device callback: Unity's Vulkan device appears, is reset, or goes away.
void UNITY_INTERFACE_API OnDeviceEvent(UnityGfxDeviceEventType eventType)
{
    std::lock_guard<std::mutex> lock(stateGate);
    if (eventType == kUnityGfxDeviceEventShutdown || eventType == kUnityGfxDeviceEventBeforeReset)
    {
        ForgetDevice();
        return;
    }

    EnsureDevice();
}
} // namespace

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* interfaces)
{
    unityInterfaces = interfaces;
    if (interfaces == nullptr)
    {
        return;
    }

    unityGraphics = Interface<IUnityGraphics>(interfaces);
    if (unityGraphics != nullptr)
    {
        // Reserve one event id so this plug-in cannot collide with another one's events.
        if (unityGraphics->ReserveEventIDRange != nullptr)
        {
            uploadEventId = unityGraphics->ReserveEventIDRange(1);
        }
        unityGraphics->RegisterDeviceEventCallback(OnDeviceEvent);
    }

    std::lock_guard<std::mutex> lock(stateGate);
    EnsureDevice();
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload()
{
    if (unityGraphics != nullptr)
    {
        unityGraphics->UnregisterDeviceEventCallback(OnDeviceEvent);
    }

    std::lock_guard<std::mutex> lock(stateGate);
    ForgetDevice();
    unityVulkan = nullptr;
    unityGraphics = nullptr;
    unityInterfaces = nullptr;
}

MAJVKU_EXPORT int MAJVKU_CALL MajVkUpload_AbiVersion()
{
    return MajVkuAbiVersion;
}

MAJVKU_EXPORT int MAJVKU_CALL MajVkUpload_Ready()
{
    std::lock_guard<std::mutex> lock(stateGate);
    return EnsureDevice() ? 1 : 0;
}

MAJVKU_EXPORT int MAJVKU_CALL MajVkUpload_Enqueue(void* unityTexture, const void* pixels, int width, int height,
    int rowPitch)
{
    if (unityTexture == nullptr || pixels == nullptr || width <= 0 || height <= 0 || rowPitch <= 0)
    {
        return 0;
    }

    const auto rowBytes = static_cast<uint64_t>(width) * 4;
    const auto sourcePitch = static_cast<uint64_t>(rowPitch);
    if (sourcePitch < rowBytes)
    {
        return 0;
    }

    const auto bytes = rowBytes * static_cast<uint64_t>(height);
    uint8_t* destination = nullptr;
    uint64_t generation = 0;
    int slot = -1;
    {
        std::lock_guard<std::mutex> lock(stateGate);
        if (!EnsureDevice() || !PrepareStaging(bytes))
        {
            return 0;
        }

        uint64_t offset = 0;
        slot = ring.Acquire(unityTexture, static_cast<uint32_t>(width), static_cast<uint32_t>(height), &offset);
        if (slot < 0)
        {
            return 0;
        }

        destination = (stagingMapped != nullptr ? stagingMapped + offset : nullptr);
        if (destination == nullptr)
        {
            ring.MarkDropped(slot, unityTexture, false);
            return 0;
        }
        generation = deviceGeneration;
    }

    {
        // The copy runs unlocked so a render event that is recording the frame never waits for it.
        std::lock_guard<std::mutex> copy(copyGate);
        CopyPixels(destination, pixels, height, rowBytes, sourcePitch);
    }

    {
        std::lock_guard<std::mutex> lock(stateGate);
        if (generation != deviceGeneration || !ring.Queue(slot))
        {
            // Unity's device was replaced while the pixels were copied; this frame falls back.
            ring.MarkDropped(slot, unityTexture, false);
            return 0;
        }
    }

    return 1;
}

MAJVKU_EXPORT int MAJVKU_CALL MajVkUpload_Pending()
{
    std::lock_guard<std::mutex> lock(stateGate);
    return ring.PendingCount();
}

MAJVKU_EXPORT int MAJVKU_CALL MajVkUpload_EventId()
{
    return uploadEventId;
}

MAJVKU_EXPORT void* MAJVKU_CALL MajVkUpload_RenderEventFunc()
{
    return reinterpret_cast<void*>(&OnUploadEvent);
}

MAJVKU_EXPORT void MAJVKU_CALL MajVkUpload_Reset()
{
    std::lock_guard<std::mutex> lock(stateGate);
    ring.Reset();
}
