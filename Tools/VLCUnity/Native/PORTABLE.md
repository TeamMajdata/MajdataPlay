# Portable native video bridge

`PortableRenderingPlugin.cpp` preserves the bundled managed binding's
single-argument `libvlc_unity_media_player_new` / `release` ABI on Linux, macOS,
Android and iOS. `PortableGpuContext.h` owns the LibVLC GL/GLES callbacks and
render-event synchronization; each platform supplies a `PortableGpuBackend`.
The managed `VlcVideoOutput` selects CPU RGBA callbacks when the native GPU
capability probe fails or the user forces CPU output.

| Platform / Unity renderer | Native GPU path | Requirements |
| --- | --- | --- |
| Linux OpenGL Core | Independent shared GLX/EGL producer context | Compatible Unity context and driver |
| Linux Vulkan | EGL texture → DMA-BUF → imported Vulkan image | Same DRM render node; Vulkan 1.1 and required external-memory/DRM-modifier/foreign-queue extensions |
| Android OpenGL ES 3 | Independent EGL producer sharing Unity GL textures | Compatible EGL configuration and GLES 3 driver |
| Android Vulkan | AHardwareBuffer → EGLImage and imported Vulkan image | Android 8+; Vulkan 1.1; Android hardware-buffer and foreign-queue extensions |
| macOS / iOS Metal | IOSurface-backed CoreVideo buffer shared by CGL/EAGL and Metal | Compatible GL/Metal CoreVideo texture caches and device |
| macOS OpenGL Core | Independent CGL context sharing RGBA8 2D textures | Actual Unity OpenGL Core renderer, compatible CGL share group/device |
| iOS OpenGL ES 3 | Independent EAGL context sharing RGBA8 2D textures | Only when Unity exposes a GLES 3 renderer and compatible EAGL share group |

These production GPU paths never map video pixels into CPU memory and do not
perform CPU texture uploads. They do perform GPU color conversion/rendering and
the managed final blit. GL/Vulkan handoff deliberately serializes GPU completion
(`glFinish` and `vkQueueWaitIdle`); Metal records completion handlers and blocks
the next producer outside the bridge mutex. This favors correctness over
parallel throughput.

GPU sharing does **not** prove that the selected codec is decoding in hardware.
End-to-end decoding without CPU pixel transfers additionally requires LibVLC to
select a compatible hardware decoder and opaque GPU output (for example
MediaCodec, VideoToolbox or VAAPI). Codec/profile/driver support can cause LibVLC
to choose software decoding. Check the actual decoder logs on each device;
`UsesGpuInterop` reports the presentation path only.

## Matching decoder bundle

This repository includes a Windows LibVLC distribution. Other targets need their
own engine, modules, helper classes and dependent libraries matching both the
target CPU architecture and LibVLC source commit `d94fd0473f`. A bridge `.so`,
`.dylib` or `.a` alone cannot decode media. LibVLC 3 and incompatible LibVLC 4
snapshots are rejected before calling their different player constructor.
Changing `VLCUNITY_REQUIRED_CHANGESET` does not adapt an incompatible ABI.

Linux/macOS/Android load `libvlc.so` / `libvlc.dylib` from the bridge directory or
normal native-library namespace and retain the decoder handle for process
lifetime. Do not load multiple different LibVLC builds into one process.

## Runtime contract

- `get_bridge_abi()` returns `1`; `validate_libvlc()` returns `1` or `0`, with a
  thread-local UTF-8 diagnostic from `get_last_error()`. Validate **before**
  constructing LibVLC, especially on iOS where static modules are registered.
- Capability values are `0` CPU, `3` OpenGL/GLES, `4` pending initialization,
  `5` Vulkan and `6` Metal. Windows reserves `1` / `2` for D3D11 / D3D12.
- Queue event `base + 0` with `get_render_context(player)` to initialize on the
  Unity render thread, then poll capability before starting playback. Queue it
  during updates to import new surfaces. Late Vulkan preload or unsupported
  extensions resolve to CPU rather than using incompatible native handles.
- Once `get_texture_info` publishes a frame, bracket the Unity blit with render
  events `base + 1` and `base + 2`, using the same retained context pointer.
  Previous-size surfaces remain alive until retirement so queued blits are safe.
  Repeated sizes are reused; excessively many retained sizes fall back to CPU.
- Native player release cancels producer waits, joins LibVLC video callbacks and
  retires the player mapping. Queue event `base + 3` **after** release and after
  earlier blits to retire the graphics resources. Metal completion handlers keep
  in-flight textures alive independently of that event. The managed wrapper
  implements this order, including CPU fallback and immediate disposal.
- Graphics-device shutdown disables new producers and retires resources while
  Unity's device still exists. Callback context objects remain alive until VLC
  joins, and a later cleanup event does not call a destroyed Vulkan device.
- GL/GLES outputs use LibVLC `bottom_right` orientation. The managed double flip
  matches the Windows D3D output. Metal external textures use BGRA32; GL/Vulkan
  use RGBA32. Color-space configuration is shared with the managed wrapper.
- `SetPluginPath` sets `VLC_PLUGIN_PATH` to native filesystem paths/lists, not APK
  `jar:` URLs. A build with `VLCUNITY_ENABLE_GPU=OFF` retains the same API and
  provides harmless no-op render events for CPU-only presentation.

## Build and package

Linux needs a C++17 compiler, CMake, EGL/OpenGL and X11 development packages
(for example `libegl-dev`, `libgl-dev`, `libx11-dev`).

```sh
bash Tools/VLCUnity/Native/build-portable.sh linux
# Copy package/libVLCUnityPlugin.so to Runtime/Plugins/Linux/x86_64/,
# alongside libvlc.so, libvlccore.so, modules and dependencies.
```

macOS needs Xcode command-line tools. The script builds arm64/x86_64 universal
bridge code; the decoder and its dependencies must also contain both slices.

```sh
bash Tools/VLCUnity/Native/build-portable.sh macos
# Copy package/libVLCUnityPlugin.dylib to Runtime/Plugins/MacOS/universal/.
```

Dylib install names/dependencies must resolve inside the app bundle using
`@loader_path` / `@rpath`. Code-sign the complete bundle after copying all files.

Android builds on a host with the NDK and CMake/Ninja:

```powershell
./Tools/VLCUnity/Native/build-android.ps1 -Ndk '/path/to/ndk' -Install
```

The default ABIs are `arm64-v8a`, `armeabi-v7a`, `x86_64` (override with `-Abi`).
The bridge targets API 23, dynamically probes API 26 hardware-buffer functions,
uses static libc++ and aligns ELF segments to 16 KiB. Supply the same ABI's
decoder libraries under `Runtime/Plugins/Android/libs/<abi>/`; these libraries
must also satisfy device page-size requirements. Desktop modules cannot be used
in an APK.

Android MediaCodec also needs the **matching LibVLC Java helper/AAR classes**,
including `org.videolan.libvlc.AWindow` and its `SurfaceCallback` constructor.
The managed runtime loads `vlc` and then `VLCUnityPlugin` through
`java.lang.System.loadLibrary`, allowing each library's `JNI_OnLoad` to run once.
The bridge caches the application-visible AWindow class, creates one helper per
player, and calls `libvlc_media_player_set_android_context`. Its global Java
reference survives until native player release has joined all decoder callbacks.
Missing helper classes or the setter are reported in logs; GPU presentation may
still work with software decoding. Preserve these reflectively loaded classes
and native JNI helper methods when enabling minification (use the matching AAR's
consumer ProGuard rules, and keep `org.videolan.libvlc.**` if its rules do not).
Package extra Java/native dependencies required by that exact decoder build;
arbitrary current `libvlc-all` AAR releases need not match this pinned ABI.

The pinned engine's [`src/android/specific.c`](https://github.com/videolan/vlc/blob/d94fd0473f/src/android/specific.c)
initializes its JavaVM through `JNI_OnLoad`; its
[`android/utils.c`](https://github.com/videolan/vlc/blob/d94fd0473f/modules/video_output/android/utils.c)
registers AWindow's native mouse/window callbacks itself. This bridge therefore
does not require or force-load a separate `libvlcjni.so` merely for those methods,
and it does not instantiate the Java `LibVLC` wrapper. Additional JNI libraries
are needed only when required by the particular matching bundle's other classes.

iOS archives require macOS and Xcode:

```sh
bash Tools/VLCUnity/Native/build-portable.sh ios-device
bash Tools/VLCUnity/Native/build-portable.sh ios-simulator
```

Copy the archive into `Runtime/Plugins/iOS/Device/` or `Simulator/`. Device and
simulator arm64 are different platform binaries; never combine them with `lipo`.
Supply matching engine/core/module/contrib archives and a registration source
defining `extern "C" void VLCUnityRegisterStaticModules(void)`. That adapter must
reference the matching engine's actual static-module registration table; an
empty function does not register codecs. Validation calls it once before LibVLC
initialization, and its native reference prevents dead stripping.

The supplied iOS `LoadPlugin.mm` registers prefixed native graphics entrypoints
with Unity. The Editor build processor adds this source, the bundle manifest's
registration source, frameworks and archives to UnityFramework. The patched iOS
managed DLL imports `__Internal`; callback delegates retain AOT-compatible static
entrypoints. Merely producing the bridge archive does not validate the final
decoder link, device signing, IL2CPP callbacks or playback.

## Verification

Host tests use a mock decoder/backend to test revision rejection, player
ownership, callback negotiation, producer/consumer exclusion, asynchronous waits,
shutdown, joining release, delayed retirement and CPU fallback. They do not
claim media playback:

```sh
cmake -S Tools/VLCUnity/Native -B Build/VLCUnityPortableTests -DVLCUNITY_TEST_ONLY=ON
cmake --build Build/VLCUnityPortableTests --config Release
ctest --test-dir Build/VLCUnityPortableTests -C Release --output-on-failure
```

`test-portable.ps1` runs the same host tests with MSVC. Linux has an optional real
driver test (`VLCUNITY_BUILD_LINUX_INTEROP_SMOKE=ON`) for cross-thread shared
textures, pixel correctness, resize and lifetime. Its pixel readback is test
instrumentation, absent from the production backend. Apple supplies a separate
CGL sharing plus Metal/IOSurface smoke source for an Apple SDK host. To run Linux driver and
preload tests on a Linux host with the development dependencies installed:

```sh
cmake -S Tools/VLCUnity/Native -B Build/VLCUnityLinux \
  -DVLCUNITY_BUILD_TESTS=ON -DVLCUNITY_BUILD_LINUX_INTEROP_SMOKE=ON
cmake --build Build/VLCUnityLinux --parallel
ctest --test-dir Build/VLCUnityLinux --output-on-failure
Build/VLCUnityLinux/PortableLinuxInteropSmoke
Build/VLCUnityLinux/PortableLinuxInteropSmoke --glx # Requires an X display.
```

The Vulkan preload fixture tests optional-extension failure without a Vulkan
driver. It does not validate real DMA-BUF import or GPU ownership transfers.

Android GLES/Vulkan bridge code has been cross-compiled with warnings as errors
for the three packaged ABIs. Native architecture, exported ABI and ELF alignment
can be verified on Windows. Android playback and hardware decoding still require
a matching decoder bundle and real device. Linux shared GL textures have been
tested under WSL's EGL and GLX software driver; this does not prove a real hardware
decoder or DMA-BUF/Vulkan path. macOS/iOS compilation and execution require an
Apple SDK host and have not been performed on the Windows development machine.

References: [Unity iOS native plugins](https://docs.unity3d.com/Manual/ios-native-plugin-create.html),
[VideoLAN Unity sources](https://github.com/videolan/vlc-unity),
[LibVLC callback contract](https://videolan.videolan.me/vlc/master/group__libvlc__media__player.html),
[Vulkan Android hardware buffers](https://registry.khronos.org/vulkan/specs/latest/man/html/VK_ANDROID_external_memory_android_hardware_buffer.html).
