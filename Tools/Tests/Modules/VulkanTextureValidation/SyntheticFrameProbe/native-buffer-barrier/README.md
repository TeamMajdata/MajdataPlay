# Buffer-visibility diagnostic probe

This is a **new, isolation-only** Windows x64 diagnostic binary. Its output filename is deliberately `MajdataPlayVulkanPacing.dll` solely so an already-built diagnostic Player can reuse its existing native-loader registration. It is **not** the original pacing layer and **not** the `mf_*` queue-fence marker; do not overwrite any Assets plug-in or original Build with it.

## Why it exists

A full-startup GFXReconstruct capture, a virtual-swapchain replay, and a direct in-process Khronos core/synchronization validation run of the same diagnostic Player all reported the same source-addressable gap: after two `vkCmdCopyBuffer` uploads into index buffers (936 and 12 bytes), the secondary command buffer's `vkCmdDrawIndexed` reads them with no visible transfer-write to index-read memory dependency. The initial-frame CPU scan found 55 `vkCmdPipelineBarrier` calls and none with a global or buffer memory barrier.

That gap is confirmed in the application's own API stream. It is **not** proven to be the cause of the later device loss, which occurred roughly 120 seconds into the captured run.

## What the probe does

Only when the child process sets `MAJ_SYNTHETIC_BUFFER_BARRIER=1`:

1. `UnityPluginLoad` registers one `AddInterceptInitialization` callback at priority `0x40000000`.
2. `vkGetInstanceProcAddr` and `vkGetDeviceProcAddr` are wrapped so that self-queries keep returning the wrapper instead of caching a lower resolver.
3. `vkCmdCopyBuffer` is wrapped. The original call is made first with unchanged arguments; a `VkBufferMemoryBarrier` is then appended to the same command buffer for the whole destination buffer: `TRANSFER_WRITE` → `MEMORY_READ`, source stage `TRANSFER`, destination stage `ALL_COMMANDS`, queue families `IGNORED`.
4. `vkCmdPipelineBarrier` is resolved through the original chain so validation and lower layers still see the injected command.

`ALL_COMMANDS`/`MEMORY_READ` is chosen because the diagnostic must stay valid on transfer-only queue families without assuming vertex-input support. It can serialize unrelated reads as well; it is a diagnostic hypothesis, not a production fix.

The probe installs **no** loader, device, swapchain, present or feature interceptor, creates no Vulkan object and submits no GPU work of its own. It does not touch image layouts, acquisition ordering, or the render-pass attachment gaps that the same validation run also reported.

## ABI 1

All `bb_*` exports use C linkage and `__cdecl`:

| Export | Contract |
| --- | --- |
| `int bb_abi_version()` | 1 |
| `int bb_mode()` | 0 disabled, 1 enabled by the preload environment variable |
| `int bb_status()` | 0 ready, 1 disabled, negative unavailable (see below) |
| `uint64_t bb_copy_calls()` | cumulative intercepted `vkCmdCopyBuffer` calls |
| `uint64_t bb_barrier_calls()` | cumulative appended barriers |
| `uint64_t bb_errors()` | cumulative diagnostic errors |

Negative statuses: -11001 not registered, -11002 no active device, -11003 `vkCmdCopyBuffer` missing, -11004 `vkCmdPipelineBarrier` missing, -11005 recursive/self resolver, -11006 Unity interface unavailable.

Disabled mode registers nothing and installs no lifecycle callback. If a required entry point is missing while enabled, `bb_status` reports it and the managed side must fail the case instead of claiming the gap was repaired. A managed copy is still forwarded to the original function first, so a missing barrier function never replaces the application's own command.

## Lifecycle limits

Device callbacks share no lock with the hot copy path beyond atomics. `kUnityGfxDeviceEventShutdown`/`BeforeReset` clear readiness so no work is injected without a confirmed device. Dynamic plug-in unloading while callbacks are queued is unsupported: keep the DLL loaded until the diagnostic Player exits. This is a single-device diagnostic, not a general-purpose Vulkan layer.

## Build only

```powershell
./build.ps1
```

Uses the existing Visual Studio 2022 Community x64 MSVC compiler and local CMake with the repository's Unity and Vulkan headers; no download, package restore, Editor or GPU run. Output: `.build/Release/MajdataPlayVulkanPacing.dll`, hashes in `.build/build-manifest.json`. `BufferBarrierStateTests` links the probe against fake function pointers and covers disabled-mode passivity, argument preservation, copy-before-barrier ordering, barrier fields, missing-function reporting, resolver self-query, and shutdown rejection.

Build and host-only tests prove nothing about real device loading, real queue-family capability, or whether the injected dependency changes the observed faults.
