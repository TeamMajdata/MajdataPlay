# MajdataPlay Vulkan pacing layer

`MajdataPlayVulkanPacing` is a small preloaded Unity native plug-in that lets the managed `LowLatencyVSync`
frame pacer (`Assets/Scripts/Misc/Rendering`) start each frame as late as possible with VSync on. Without it,
Unity's Vulkan FIFO swapchain lets the game run until every swapchain image is queued, so frames are rendered
several refresh intervals before they are shown. It has no dependency on FFmpeg or on the FFmpeg graphics
bridge.

At the start of each frame `LowLatencyVSync` uses, when available:

1. **Present waiting**: `VK_KHR_present_wait`, which Unity 6 enables itself when the driver supports it. The
   frame waits until the previous frame has been shown, like D3D11's waitable swapchain. This removes the VSync
   queue.
2. **Driver low-latency mode**: `VK_NV_low_latency2` (NVIDIA Reflex) or `VK_AMD_anti_lag`, which this layer
   enables on Unity's device. The driver may delay the frame further and receives the frame markers. It runs
   after present waiting, not instead of it: on an RTX 3060 Laptop GPU with FIFO presentation, Reflex's sleep
   stayed near zero and left the VSync queue in place.

When neither is available, **timer pacing** in C# (`VSyncFramePacer`) produces frames slightly slower than the
display refreshes, so input is sampled late; it needs nothing from this layer. A backend that keeps failing is
given up and the others continue. The hidden setting `Debug.LowLatencyVSyncBackend` (`Auto`, `PresentWait`,
`Timer`) limits the backends for comparisons.

## How it works

- `UnityPluginLoad` registers an initialization interceptor with
  `IUnityGraphicsVulkanV2::AddInterceptInitialization` at priority `0x40000000`, below
  `kUnityVulkanInitCallbackMaxPriority`. Unity chains interceptors by priority with the highest closest to the
  Vulkan loader, so plug-ins that use the maximum priority (the FFmpeg graphics bridge) stay inside this layer
  and every Unity swapchain call passes through this layer first.
- **Device creation.** Unity 6000.3.17f1 already requests `VK_KHR_present_id` and `VK_KHR_present_wait` with
  their feature structures when the driver supports them (observed on an NVIDIA RTX 3060 Laptop GPU); the layer
  only checks that request. When the driver supports a low-latency extension, the layer adds it to a copy of
  Unity's create info: `VK_NV_low_latency2` with `VK_KHR_timeline_semaphore` and a timeline semaphore feature
  structure (it needs Unity's present ids, and is skipped when Unity's chain disables timeline semaphores), or
  else `VK_AMD_anti_lag` with its feature structure. If the driver rejects that device, Unity's own
  configuration is created instead. Unity's memory is never modified.
- **Frames.** `majvk_frame_begin` (main thread, before input) numbers a frame and queues the number for the next
  presentation; `majvk_frame_end` closes the main-thread part. NVIDIA: `vkLatencySleepNV` signals a timeline
  semaphore that `majvk_frame_begin` waits on, then `SIMULATION_START`/`INPUT_SAMPLE` markers; `SIMULATION_END`
  at frame end; the first `vkQueueSubmit` of a frame sets `RENDERSUBMIT_START` and every submission carries
  `VkLatencySubmissionPresentIdNV`; presentation sets `RENDERSUBMIT_END`, `PRESENT_START` and `PRESENT_END`.
  Swapchains are created with `VkSwapchainLatencyCreateInfoNV` and each new swapchain gets
  `vkSetLatencySleepModeNV`. AMD: `vkAntiLagUpdateAMD` with the input stage at frame begin and the present stage,
  for the same frame number, right before presentation; it is called without any lock because the driver may
  sleep inside it.
- **Swapchains and ids.** `vkCreateSwapchainKHR`, `vkDestroySwapchainKHR`, `vkQueuePresentKHR`, `vkQueueSubmit`,
  `vkAcquireNextImageKHR`, `vkAcquireNextImage2KHR`, `vkDestroyDevice` and Unity's own `vkWaitForPresentKHR` are
  wrapped through both the instance and the device lookup. The newest swapchain of the device is tracked with a
  generation counter: a window resize creates the new swapchain from the old one (tracking moves to the new
  handle at once and the retired one, which may still present, is ignored), a fullscreen toggle destroys the
  swapchain first and then creates a new one. Queued frame numbers are dropped on recreation. One monotonic
  counter numbers frames, so presentation ids keep increasing on every swapchain. When Unity attaches its own
  `VkPresentIdKHR` chain it is left alone and its ids are followed, except with the NVIDIA backend: its latency
  markers must name presentations by frame id, so when Unity's chain is the first structure the layer presents a
  copy with the frame id and translates Unity's own `vkWaitForPresentKHR` calls from Unity's ids to the frame
  ids. Results of inner layers, such as the FFmpeg bridge's acquisition workaround, pass through unchanged.
- **Synchronization.** `vkWaitForPresentKHR` externally synchronizes the swapchain. Unity's swapchain calls
  register themselves under a lock but run unlocked, so a blocking acquisition never holds it; the present wait
  poll only try-locks the lock and calls the zero-timeout wait while no Unity call is in progress. The NVIDIA
  calls, which take the swapchain and never block, are serialized with each other and with swapchain destruction.

## C ABI (version 2, Cdecl)

| Export | Result |
| --- | --- |
| `majvk_abi_version()` | `2` |
| `majvk_present_wait_status()` | `0` enabled, `1` no Vulkan device yet, `2` Unity did not enable both features, `3` `vkWaitForPresentKHR` missing |
| `majvk_present_wait_query(uint64_t* generation, uint64_t* presentId)` | `1`, `-2` when not enabled, `-3` for null arguments |
| `majvk_present_wait_poll(uint64_t generation, uint64_t presentId)` | `1` shown, `0` pending or busy, `-1` swapchain changed, `-2` not enabled, otherwise a negative `VkResult` |
| `majvk_low_latency_backend()` | `0` none, `1` `VK_NV_low_latency2`, `2` `VK_AMD_anti_lag` |
| `majvk_low_latency_status()` | `0` active, `1` no device yet, `2` unsupported, `3` rejected by the driver, `4` entry point or timeline semaphore missing |
| `majvk_frame_begin(uint64_t timeoutNanoseconds)` | `1` started, `0` skipped (no swapchain), `2` the NVIDIA sleep timed out, `-2` no backend, otherwise a negative `VkResult` |
| `majvk_frame_end()` | `1` recorded, `0` skipped, `-2` no backend |
| `majvk_swapchain_generation()` | changes whenever the tracked swapchain is created or destroyed |
| `majvk_present_id_source()` | `0` none yet, `1` the layer attaches ids, `2` Unity does, `3` Unity does and the NVIDIA backend replaces them |

`Assets/Scripts/Misc/Rendering/VulkanPacingLayer.cs` declares these entry points and requires ABI 2. When the
layer is missing or outdated, or a backend is unavailable or keeps failing, `LowLatencyVSync` logs the reason
and continues with the remaining backends or the timer.

## Building

Requirements: llvm-mingw (the toolchain pinned in `Tools/FFmpeg/dependencies.lock.json`), CMake and Ninja
(the copies bundled with the project's Unity Editor Android support are found automatically), and the
repository's Vulkan-Headers copy in `Tools/FFmpeg/Native/ThirdParty/Vulkan-Headers`. That copy predates
`VK_AMD_anti_lag`, so the layer declares the few definitions it needs, taken from Vulkan-Headers 1.4.328, when
the headers lack them. The Unity plug-in API headers in `Unity/` are copied from Unity and keep their license
(`Unity/LICENSE.md`).

```powershell
./Tools/Native/VulkanPacing/build.ps1 -Test
```

The script looks for llvm-mingw in `-LlvmMingw`, `LLVM_MINGW`, then `Tools/FFmpeg/.build/toolchains` or
`FFMPEG_BUILD_ROOT/toolchains`; `Tools/FFmpeg/build-dependencies.py` (`ensure_llvm`) fetches and verifies the
pinned archive. `-Test` builds and runs `tests/VulkanPacingTests.cpp` for the host first. The tests model Unity's
interceptor registration, the inner layers and a driver, and cover present waiting, window resize, fullscreen
toggle and out-of-date recreation, the NVIDIA and AMD backends and their fallbacks. They do not cover real
drivers or presentation timing; pass `-DMAJVK_VULKAN_HEADERS=<newer include>` to CMake to also check them
against headers that define `VK_AMD_anti_lag`.

Each target (`win-x86`, `win-x64`, `win-arm64`) is staged to
`Assets/Plugins/MajdataPlay/Rendering/Native/Windows/<x86|x86_64|arm64>/` together with a
`build-manifest.json` recording the compiler, the DLL hash and LF-normalized hashes of every source input.
Existing `.meta` files are kept: the DLLs are preloaded and enabled only for their Windows player platform and
CPU. The Editor does not load the layer, because it always uses timer pacing. CMake build trees go to the
ignored `.build/` folder. The source is platform independent; only Windows players ship it for now.

## Hardware probe

`tests/VulkanPacingProbe.cpp` is a minimal Windows program for checking a backend on real hardware, in particular
`VK_AMD_anti_lag`, which the deterministic tests only model. It loads `MajdataPlayVulkanPacing.dll` from its own
folder, registers it the way Unity's preload does, creates a Vulkan 1.1 instance, a device like Unity 6's (present
id/wait with their features when supported) and a small window with a FIFO swapchain, and clears and presents
frames after 2 ms of simulated game logic. It runs the same frames four times: without pacing, with timer pacing
(a port of `VSyncFramePacer`, the managed fallback, timed with the refresh period DWM reports), with the driver's
mode only, and with present waiting followed by the driver's mode (the order `LowLatencyVSync` uses).

```powershell
./Tools/Native/VulkanPacing/build.ps1 -Probe            # or -Targets @() -Probe to reuse the staged DLL
./Tools/Native/VulkanPacing/.build/probe/MajdataPlayVulkanPacingProbe.exe --require amd
```

Options: `--require amd|nvidia|any` (exit code 2 when that backend is not active), `--frames N` per mode (default
300 after 60 warm-up frames), `--work-ms M` simulated game logic, `--gpu INDEX`. The `probe` folder holds the exe
and the DLL, so it can be copied to a test machine as is; it needs nothing but the GPU driver's Vulkan loader.

For each mode it prints the frame rate, the wait before input is sampled (`pacing`), the swapchain waits after it
(`acquire`, `present`), `sample->present`, and, when the device has present waiting, `sample->shown`: from input
sampling until `vkWaitForPresentKHR` reports the frame shown, measured on a second thread through the layer's
non-blocking poll (minimum, average, 95th percentile and maximum). A working backend moves the wait into `pacing`
and lowers `sample->shown`. On an RTX 3060 Laptop GPU at 60 Hz (windowed, `--frames 2000`) the probe printed:

| mode | pacing | acquire | sample->shown (min / avg / p95 / max) |
| --- | --- | --- | --- |
| off | 0.00 ms | 14.34 ms | 36.7 / 49.6 / 49.9 / 50.2 ms |
| timer | 14.52 ms | 0.01 ms | 2.7 / 11.2 / 18.6 / 20.2 ms |
| driver (`VK_NV_low_latency2`) | 12.30 ms | 0.19 ms | 31.3 / 34.8 / 49.7 / 50.0 ms |
| wait+driver | 14.53 ms | 0.01 ms | 14.4 / 16.6 / 16.7 / 18.8 ms |

Timer pacing produces frames slightly slower than the display refreshes, so input sampling drifts through the
refresh interval and `sample->shown` sweeps a range about one refresh period wide; the average depends on which part
of that sweep a run covers. Present waiting samples input right after the previous frame is shown, which holds
`sample->shown` at one refresh period.

## Limitations

- The FFmpeg bridge's Vulkan Video negotiation copies only the feature structures it knows, so on a device where
  this layer adds the AMD anti-lag feature it would decline Vulkan Video. Today it declines earlier anyway,
  because Unity creates a Vulkan 1.1 instance and Vulkan Video needs 1.3.
- `VK_NV_low_latency2` needs Unity's present ids, and `RENDERSUBMIT` markers only cover `vkQueueSubmit` (Unity
  does not use `vkQueueSubmit2` with its Vulkan 1.1 instance).
- Present waiting needs a driver with both extensions and a Unity version that enables them on its device.
- The presentation time reported by `vkWaitForPresentKHR` depends on the driver and on Windows composition.
- The layer tracks one swapchain at a time (the newest), which matches Unity players.
