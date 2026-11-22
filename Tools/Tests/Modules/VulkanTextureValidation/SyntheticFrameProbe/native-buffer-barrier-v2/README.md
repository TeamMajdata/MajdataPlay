# Buffer-visibility diagnostic probe v2 (narrow / broad)

New, isolation-only Windows x64 diagnostic binary. Its output filename is deliberately `MajdataPlayVulkanPacing.dll` so an already-built diagnostic Player can reuse its existing native-loader registration. It is **not** the original pacing layer, and **not** a production fix.

## Two diagnostic modes

`MAJ_SYNTHETIC_BUFFER_BARRIER` is read once at preload:

| Value | Mode | Injected dependency |
| --- | --- | --- |
| unset/`0` | disabled | nothing; no interceptor or device callback is registered |
| `1` | **narrow** | only when the destination buffer was created with `INDEX_BUFFER` and/or `VERTEX_BUFFER` usage: `TRANSFER_WRITE` → `INDEX_READ`/`VERTEX_ATTRIBUTE_READ`, source stage `TRANSFER`, destination stage `VERTEX_INPUT` |
| `2` | **broad** | every `vkCmdCopyBuffer`: `TRANSFER_WRITE` → `MEMORY_READ`, destination stage `ALL_COMMANDS` (the earlier coarse control) |

Narrow mode exists to separate two explanations that the broad mode could not: actually repairing the index/vertex visibility dependency, versus incidentally serialising the whole graphics queue with an `ALL_COMMANDS` wait.

Buffer usage is recorded by wrapping `vkCreateBuffer`/`vkDestroyBuffer` under a small mutex; the driver call is always outside the lock and always forwarded unchanged first. Self-queries of `vkGetInstanceProcAddr`/`vkGetDeviceProcAddr` keep returning the wrapper so a lower resolver cannot be cached and bypass the interceptor.

## Evidence this targets

A full-startup capture, an independent virtual-swapchain replay, and an in-process Khronos core/synchronization validation run of the same diagnostic Player all reported the same source-addressable gap: two index buffers of 936 and 12 bytes are written by `vkCmdCopyBuffer` and then read by `vkCmdDrawIndexed` with no transfer-write → index-read memory dependency (55 initial-frame barriers, all image barriers only).

With the broad mode the index RAW diagnostics disappeared and four consecutive 120 Hz runs completed. That is correlation, not proof: the broad barrier also serialises far more work than the dependency requires.

## ABI 2

All `bb_*` exports are C linkage / `__cdecl`:

| Export | Contract |
| --- | --- |
| `int bb_abi_version()` | 2 |
| `int bb_mode()` | 0 disabled, 1 narrow, 2 broad |
| `int bb_status()` | 0 ready, 1 disabled, negative unavailable |
| `uint64_t bb_copy_calls()` | intercepted `vkCmdCopyBuffer` calls |
| `uint64_t bb_barrier_calls()` | appended barriers |
| `uint64_t bb_skipped_calls()` | narrow-mode copies skipped because the buffer is not index/vertex |
| `uint64_t bb_errors()` | diagnostic errors |

Negative statuses: -11001 not registered, -11002 no active device, -11003 required function missing, -11004 barrier entry point missing, -11005 recursive resolver, -11006 Unity interface unavailable.

## Limits

- Does not address the image-side gaps: swapchain clear → render-pass implicit layout transition (WAW) and acquire → `TRANSFER` layout transition (WAR) remain.
- Narrow mode only helps if the affected buffer really is created with index/vertex usage; a buffer used as index without that usage flag would be skipped.
- No Vulkan object is created and no work is submitted; the probe adds one recorded barrier per qualifying copy.
- Dynamic plug-in unloading while callbacks are queued is unsupported. `Shutdown`/`BeforeReset` clears readiness.
- A successful narrow run would still have to be reproduced with alternating order and on the unmodified game path before any fix claim.

## Build only

```powershell
./build.ps1
```

Uses the existing Visual Studio 2022 Community x64 MSVC compiler and local CMake with the repository's Unity/Vulkan headers. Outputs `.build/Release/MajdataPlayVulkanPacing.dll` and hashes in `.build/build-manifest.json`. `BufferBarrierV2StateTests` runs against fake function pointers: mode selection, copy-before-barrier ordering, narrow range/stage/access fields, skipping non-index/vertex buffers, retained usage after destroy, and shutdown rejection.
