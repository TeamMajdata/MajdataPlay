# Vulkan index/vertex visibility hook

Windows x64 native **Unity preload plugin** (not a Vulkan explicit layer). It repairs one specific Vulkan synchronization defect that the engine's own command stream contains, and it is loaded automatically by the Unity player because it occupies the already-registered preload slot `MajdataPlay_Data/Plugins/x86_64/MajdataPlayVulkanPacing.dll`.

It is **not** the original low-latency VSync pacing implementation. Keep the original file backed up and restore it to get pacing back.

## Defect it repairs

The engine records `vkCmdCopyBuffer` into a small index/vertex buffer and then draws from it with `vkCmdDrawIndexed` in the same submission, without any availability/visibility dependency. Vulkan requires one; queue order alone does not make a buffer write visible. Khronos synchronization validation reports it as `SYNC-HAZARD-READ-AFTER-WRITE` for the 936-byte and 12-byte index buffers, and it is reproducible on a pristine player in the `Init → Title` scene before any chart content exists, so the defect belongs to the engine path rather than to game code.

## What it does

`UnityPluginLoad` registers one `AddInterceptInitialization` callback at priority `0x40000000` and wraps `vkGetInstanceProcAddr`/`vkGetDeviceProcAddr` so the wrappers are reached before the swapchain exists. `vkCmdCopyBuffer` is wrapped: the original call is forwarded with unchanged arguments, and then - only when the destination buffer was created with `INDEX_BUFFER` and/or `VERTEX_BUFFER` usage - one `vkCmdPipelineBarrier` is appended to the same command buffer.

| Field | Value |
| --- | --- |
| `srcStageMask` | `VK_PIPELINE_STAGE_TRANSFER_BIT` |
| `dstStageMask` | `VK_PIPELINE_STAGE_VERTEX_INPUT_BIT` |
| `srcAccessMask` | `VK_ACCESS_TRANSFER_WRITE_BIT` |
| `dstAccessMask` | `VK_ACCESS_INDEX_READ_BIT \| VK_ACCESS_VERTEX_ATTRIBUTE_READ_BIT` |
| `buffer` / `offset` / `size` | destination buffer / 0 / `VK_WHOLE_SIZE` |
| queue families | `VK_QUEUE_FAMILY_IGNORED` |

Buffer usage is tracked by wrapping `vkCreateBuffer`/`vkDestroyBuffer` behind a mutex; driver calls always happen outside the lock and are forwarded first. `vkCmdPipelineBarrier` is resolved through the next-layer chain so validation and any lower layer still observe the injected command.

## Modes

`MAJ_VULKAN_SYNC_HOOK` is read once at preload:

| Value | Behaviour |
| --- | --- |
| unset | **narrow** repair (default) |
| `0` | disabled; nothing is registered and no interceptor is installed |
| `2` | broad form: every copy gets `TRANSFER_WRITE → MEMORY_READ` with `ALL_COMMANDS` |

A single line is appended to `vulkan-sync-hook.log` beside the player executable at load and when interception is registered, so activation can be verified without a debugger.

## What it does not fix

Image-side gaps remain: the swapchain clear → render-pass implicit layout transition (`WRITE_AFTER_WRITE`) and the acquire → `TRANSFER` layout transition (`WRITE_AFTER_READ`) are untouched. The hook does not alter engine rendering, resources, queues or the swapchain.

## Evidence status

- Verified with Khronos core+synchronization validation on a pristine player: with the hook the 936/12-byte `READ_AFTER_WRITE` diagnostics disappear; only the image-side hazards remain.
- Not proven to reduce GPU device loss. A 12-vs-12 game soak gave 0/12 with the narrow repair against 2/12 without, which is not statistically significant (Fisher one-sided p ≈ 0.24). Do not treat this as a confirmed fix.

## Build only

```powershell
./build.ps1
```

Uses the existing Visual Studio 2022 Community x64 MSVC compiler and local CMake with the repository Unity/Vulkan headers. Outputs `.build/Release/MajdataPlayVulkanPacing.dll` plus hashes in `.build/build-manifest.json`. `VulkanSyncHookStateTests` covers mode selection, copy-before-barrier ordering, narrow field values, skipping non-index/vertex buffers, usage lifetime after destroy, and shutdown rejection.
