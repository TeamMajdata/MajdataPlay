# MajdataPlay Vulkan pacing layer

`MajdataPlayVulkanPacing` is a small preloaded Unity native plug-in that lets the managed
`LowLatencyVSync` frame pacer (`Assets/Scripts/Misc/Rendering`) wait at the start of each frame until the
previous frame has been shown, like D3D11's waitable swapchain. Without it, Unity's Vulkan FIFO swapchain lets
the game run until every swapchain image is queued, so frames are rendered several refresh intervals before
they are shown. It has no dependency on FFmpeg or on the FFmpeg graphics bridge.

## How it works

- `UnityPluginLoad` registers an initialization interceptor with
  `IUnityGraphicsVulkanV2::AddInterceptInitialization` at priority `0x40000000`, below
  `kUnityVulkanInitCallbackMaxPriority`. Unity chains interceptors by priority with the highest closest to the
  Vulkan loader, so plug-ins that use the maximum priority (the FFmpeg graphics bridge) stay inside this layer
  and every Unity swapchain call passes through this layer first.
- Device creation is never changed. Unity 6000.3.17f1 already requests `VK_KHR_present_id` and
  `VK_KHR_present_wait` with their feature structures when the driver supports them (observed on an NVIDIA
  RTX 3060 Laptop GPU). The layer only checks Unity's create info and then loads `vkWaitForPresentKHR` through
  the inner layers, so it always gets the driver's function rather than its own wrapper.
- `vkCreateSwapchainKHR`, `vkDestroySwapchainKHR`, `vkQueuePresentKHR`, `vkAcquireNextImageKHR`,
  `vkAcquireNextImage2KHR` and Unity's own `vkWaitForPresentKHR` are wrapped through both the instance and the
  device lookup. The newest swapchain of the device is tracked with a generation counter. Each of its
  presentations carries an increasing `VkPresentIdKHR`: when Unity attaches its own id chain it is left alone
  and its ids are followed, otherwise the layer adds one (other swapchains in the same call get id 0).
  Results of inner layers, such as the FFmpeg bridge's acquisition workaround, pass through unchanged.
- `vkWaitForPresentKHR` externally synchronizes the swapchain. Unity's swapchain calls register themselves
  under a lock but run unlocked, so a blocking acquisition never holds it; the exported poll only try-locks the
  lock and calls the zero-timeout wait while no Unity call is in progress.

## C ABI (version 1, Cdecl)

| Export | Result |
| --- | --- |
| `majvk_abi_version()` | `1` |
| `majvk_present_wait_status()` | `0` enabled, `1` no Vulkan device yet, `2` Unity did not enable both features, `3` `vkWaitForPresentKHR` missing |
| `majvk_present_wait_query(uint64_t* generation, uint64_t* presentId)` | `1`, `-2` when not enabled, `-3` for null arguments |
| `majvk_present_wait_poll(uint64_t generation, uint64_t presentId)` | `1` shown, `0` pending or busy, `-1` swapchain changed, `-2` not enabled, otherwise a negative `VkResult` |

`Assets/Scripts/Misc/Rendering/VulkanPresentWaitSource.cs` declares these entry points. When the layer is
missing, outdated or not enabled, `LowLatencyVSync` falls back to its timer pacing and logs the reason.

## Building

Requirements: llvm-mingw (the toolchain pinned in `Tools/FFmpeg/dependencies.lock.json`), CMake and Ninja
(the copies bundled with the project's Unity Editor Android support are found automatically), and the
repository's Vulkan-Headers copy in `Tools/FFmpeg/Native/ThirdParty/Vulkan-Headers`. The Unity plug-in API
headers in `Unity/` are copied from Unity and keep their license (`Unity/LICENSE.md`).

```powershell
./Tools/Native/VulkanPacing/build.ps1 -Test
```

The script looks for llvm-mingw in `-LlvmMingw`, `LLVM_MINGW`, then `Tools/FFmpeg/.build/toolchains` or
`FFMPEG_BUILD_ROOT/toolchains`; `Tools/FFmpeg/build-dependencies.py` (`ensure_llvm`) fetches and verifies the
pinned archive. `-Test` builds and runs `tests/VulkanPacingTests.cpp` for the host first; it models Unity's
interceptor registration, the inner layers and the driver, and does not cover real presentation timing.

Each target (`win-x86`, `win-x64`, `win-arm64`) is staged to
`Assets/Plugins/MajdataPlay/Rendering/Native/Windows/<x86|x86_64|arm64>/` together with a
`build-manifest.json` recording the compiler, the DLL hash and LF-normalized hashes of every source input.
Existing `.meta` files are kept: the DLLs are preloaded and enabled only for their Windows player platform and
CPU. The Editor does not load the layer, because it always uses timer pacing. CMake build trees go to the
ignored `.build/` folder. The source is platform independent; only Windows players ship it for now.

## Limitations

- Present waiting needs a driver with both extensions and a Unity version that enables them on its device.
- The presentation time reported by `vkWaitForPresentKHR` depends on the driver and on Windows composition.
- The layer tracks one swapchain at a time (the newest), which matches Unity players.
