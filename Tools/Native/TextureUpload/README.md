# MajdataPlay Vulkan texture upload plug-in

`MajdataPlayTextureUpload` uploads CPU RGBA8 pixels into a Unity-owned Vulkan image with the plug-in's own
persistent staging buffer and `vkCmdCopyBufferToImage`. It exists for the driver-hang workaround on Vulkan:
`Texture2D.LoadRawTextureData` + `Apply` reproduces an `nvlddmkm` event 153 engine reset and a hang inside
`nvoglv64+0xe1491a` on the target machine, while an equivalent plain staging copy is clean. The texture stays a
normal Unity `Texture2D`, so a `SpriteRenderer` samples it as before. It has no FFmpeg dependency and never calls
into FFmpeg; the managed wrapper lives in the FFmpeg assembly because that is where the video frames are decoded
(`Assets/Plugins/MajdataPlay/FFmpeg/Runtime/Interop/VulkanUploadBridge.cs`).

## How a frame is uploaded

1. `MajVkUpload_Enqueue(texture, pixels, width, height, rowPitch)` runs on the calling (main) thread. It reserves a
   staging slot, copies the pixels into the mapped `HOST_VISIBLE | HOST_COHERENT` buffer (tightly packed RGBA8; a
   `rowPitch` wider than `width * 4` is copied row by row) and queues `(texture, extent, slot)`.
2. The managed side then calls `GL.IssuePluginEvent(MajVkUpload_RenderEventFunc(), MajVkUpload_EventId())`. The
   plug-in registered that event with `IUnityGraphicsVulkanV2::ConfigureEvent` as
   `kUnityVulkanRenderPass_EnsureOutside`, `kUnityVulkanGraphicsQueueAccess_Allow` and
   `EnsurePreviousFrameSubmission | ModifiesCommandBuffersState`.
3. Inside the event, for each queued upload: `AccessTexture(...)` in `kUnityVulkanResourceAccess_ObserveOnly` mode
   validates the image without recording a barrier, then `AccessTexture(unityTexture, UnityVulkanWholeImage,
   VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_WRITE_BIT,
   kUnityVulkanResourceAccess_PipelineBarrier, &image)` transitions the image, `CommandRecordingState` is queried
   again (resource access invalidates it), and one `vkCmdCopyBufferToImage` with
   `VkBufferImageCopy{bufferOffset, bufferRowLength = 0, bufferImageHeight = 0, imageSubresource{COLOR,0,1},
   imageOffset{0,0,0}, imageExtent{width,height,1}}` is recorded. A `VkImageMemoryBarrier` from
   `TRANSFER_DST_OPTIMAL` to `SHADER_READ_ONLY_OPTIMAL` with `TRANSFER` → `FRAGMENT_SHADER` follows, so the texture
   is sampleable in the same frame.
   Because Unity tracks image layouts itself, the image is then acquired once more with
   `VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL` in `kUnityVulkanResourceAccess_PipelineBarrier` mode. Without that
   step the next frame's `TRANSFER_DST_OPTIMAL` request would look like the current layout already and the copy
   would run against a shader-read image; Unity records a redundant barrier for it instead.

The plug-in never submits its own command buffer and never calls a blocking Vulkan function on the render thread.

## Staging ring

- One persistent buffer holds `4` slots of `align(width * height * 4, 256)` bytes each, mapped once at creation
  (`src/StagingRing.h` owns the slot bookkeeping and is tested without a GPU).
- A slot is reused only when it holds no queued or recording upload **and** Unity reported a
  `UnityVulkanRecordingState::safeFrameNumber` that covers the frame whose command buffer last read it. Unity's
  frame numbers are the only completion signal available: the copies run inside Unity's command buffer, so a
  plug-in-owned `VkFence` could never be signalled. `vkWaitForFences` is therefore not used and nothing waits.
- When no slot is provably free, `MajVkUpload_Enqueue` returns `0` immediately and the caller uploads the frame the
  usual way. A full ring is backpressure, never a stall.
- A frame larger than the current slot stride (a resolution change) allocates a new buffer; the old one is retired
  and destroyed from a later render event once Unity's safe frame number proves it is unused. Retirements that
  cannot be proven safe are left to Unity's device teardown instead of being destroyed early.
- The pixel copy runs without holding the state mutex, so a render event that is recording the frame never waits
  for a multi-megabyte `memcpy`. `MajVkUpload_Reset` (called when the caller recreated its texture) drops queued
  uploads and the refusal cache but keeps slots a recorded command buffer still reads.
- No allocation happens per frame after warm-up.

## Supported images and the fallback contract

- Accepted formats: `VK_FORMAT_R8G8B8A8_UNORM` and `VK_FORMAT_R8G8B8A8_SRGB`, which is what
  `TextureFormat.RGBA32` and sRGB `RGBA32` become on Vulkan.
- Accepted images: `VK_IMAGE_TYPE_2D`, single sample, `VK_IMAGE_USAGE_TRANSFER_DST_BIT`, an extent equal to the
  uploaded width and height, at least one mip level and layer.
- Anything else is refused. The image is only discovered during the render event, so the first enqueue for an
  unsupported texture succeeds and its copy is dropped; from then on the same texture pointer is refused by
  `MajVkUpload_Enqueue` (returning `0`), which is when the caller falls back. At most one frame is lost per
  texture, and `MajVkUpload_Reset` clears the refusal cache.
- The managed wrapper additionally requires `SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan` and
  `texture.format == TextureFormat.RGBA32`, and tolerates a missing or outdated plug-in
  (`IsAvailable == false`, `AvailabilityReason` explains why).

Callers must treat every return value as advisory and keep their own upload path:

```csharp
if (VulkanUploadBridge.TryEnqueue(texture, frame.Data, frame.Width, frame.Height, frame.Stride))
{
    VulkanUploadBridge.IssueRenderEvent();
}
else
{
    texture.LoadRawTextureData(frame.Data, frame.DataSize);
    texture.Apply(false, false);
}
```

## C ABI (version 1, Cdecl)

| Export | Result |
| --- | --- |
| `MajVkUpload_AbiVersion()` | `1`; the managed wrapper refuses any other version |
| `MajVkUpload_Ready()` | `1` while Unity exposes a Vulkan device, otherwise `0` |
| `MajVkUpload_Enqueue(void* unityTexture, const void* pixels, int width, int height, int rowPitch)` | `1` queued, `0` the caller must fall back |
| `MajVkUpload_Pending()` | queued uploads that no render event has recorded yet |
| `MajVkUpload_EventId()` | the Unity event identifier reserved in `UnityPluginLoad` |
| `MajVkUpload_RenderEventFunc()` | the `UnityRenderingEvent` callback for `GL.IssuePluginEvent` |
| `MajVkUpload_Reset()` | drops queued uploads and the refusal cache |

`UnityPluginLoad` reserves one event id with `IUnityGraphics::ReserveEventIDRange`, registers a device event
callback and configures the event. `UnityPluginUnload`, `kUnityGfxDeviceEventShutdown` and
`kUnityGfxDeviceEventBeforeReset` forget the device state without destroying Vulkan objects: Unity tears the
device down immediately afterwards, so the staging buffer and its mapping are freed with it.

## Building

```powershell
./Tools/Native/TextureUpload/build.ps1 -Test
```

Requirements: CMake ≥ 3.20, Ninja and the installed MSVC toolset with the Windows SDK (both are discovered below
`C:\Program Files\Microsoft Visual Studio\2022\Community`, and can be overridden with `-Cl`, `-VcVars`, `-CMake`
and `-Ninja`). The build fails at configure time when the MSVC environment is not imported, so the script imports
`vcvars64.bat` into its own session first. Vulkan headers come from the repository's pinned copy in
`Tools/FFmpeg/Native/ThirdParty/Vulkan-Headers`; the plug-in defines `VK_NO_PROTOTYPES`, resolves every entry point
through Unity's device and links no Vulkan library.

`-Test` builds and runs `tests/StagingRingTests.cpp` for the host first. Those tests cover slot exhaustion, the
safe-frame gating of slot reuse, dropping uploads, the refusal cache and its eviction, the growth rules of the
persistent buffer, FIFO ordering, `DropPending`, `Forget` and `Reset`. They do not touch Vulkan.

Output is staged to `Assets/Plugins/MajdataPlay/TextureUpload/Native/Windows/x86_64/` as
`MajdataPlayTextureUpload.dll` next to a `build-manifest.json` with the compiler, SDK, DLL hash and LF-normalized
hashes of every source input. The `.meta` files mark the DLL as preloaded and enabled for the Windows x64 player
and the Windows Editor; existing GUIDs are never rewritten. CMake trees go to the ignored `.build/` folder.

## Limitations and unverified behaviour

- Only the Windows x64 player and the Windows Editor are staged. No other platform builds this plug-in, and the
  managed wrapper reports it as unavailable there.
- Nothing here has run against a real driver yet: the copy, the barrier sequence, Unity's layout tracking and the
  `safeFrameNumber` reuse rule are reasoned from the Unity plug-in API and from the FFmpeg graphics bridge in
  `Tools/FFmpeg/Native/VulkanInterop.cpp`, not observed. The deterministic tests only exercise the slot ring.
- Accessing textures from an event configured with `kUnityVulkanGraphicsQueueAccess_Allow` is what the workaround
  requires, but `IUnityGraphicsVulkanV2.h` documents `AccessTexture` as "must not be called from event callbacks
  configured for queue access". If Unity asserts on that combination the event has to move to
  `AccessQueue(callback, eventId, data, flush)`, which gives the same graphics-queue access with a submission-thread
  callback.
- Unity's `safeFrameNumber` is assumed to advance every frame. If it does not, the ring starves and every enqueue
  returns `0`, which degrades to the old upload path instead of corrupting a frame.
- The plug-in keeps the image in `VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL` and re-acquires it to keep Unity's
  bookkeeping in sync. If Unity's resource tracking does not accept that sequence, the texture has to be left in
  `VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL` and sampled through Unity's own transition instead.
