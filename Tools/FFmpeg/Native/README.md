# FFmpeg Unity graphics bridge

Native companion to `FFmpegVideoPlayer`. It links the same **libavutil 61** ABI as the project's generated FFmpeg bindings. With `RequireHardwareDecoding` enabled, an unsupported renderer, driver, pixel format, missing native plug-in or unsafe resource sharing fails preparation/playback with a diagnostic. In ordinary hardware-preferred mode, unavailable GPU sharing first selects hardware decoding with CPU RGBA upload; software decoding is the final fallback if hardware decoding or download also fails. Software-preferred mode skips hardware setup unless strict GPU mode overrides it.

| Unity renderer | Native path implemented here | Pixel transfer |
| --- | --- | --- |
| D3D11 / Windows | FFmpeg D3D11VA using Unity's own device, NV12 texture-array slice input to `ID3D11VideoProcessor` | GPU conversion to RGBA, then Unity shader orientation; **no CPU readback, not strict zero-copy** |
| Metal / macOS and iOS | VideoToolbox CVPixelBuffer → CVMetalTextureCache → Unity R8/RG8 external textures | NV12 planes are shared without pixel copies; RGB output requires a GPU conversion draw |
| D3D12 / Windows | Same-adapter D3D11VA → shared RGBA → Unity D3D12 render target | Video conversion and an explicit GPU copy, shared producer fence and separate completion fence; no CPU pixel transfer |
| Vulkan / Windows | Same-adapter D3D11VA → NT-handle shared RGBA → Unity VkImage | Video conversion and GPU copy, keyed-mutex ownership, Unity resource-state/queue APIs; no CPU pixel transfer |
| OpenGL Core / Windows | D3D11VA → RGBA registered with WGL_NV_DX_interop2 | Converted RGBA allocation shared with Unity GL texture; render-context lock/unlock; no CPU pixel transfer |
| Vulkan / Linux | VAAPI → DRM PRIME separate R8/RG8 layers → modifier-aware DMA-BUF import | GPU NV12 conversion and copy into Unity RGBA8; no CPU pixel mapping/readback |
| Vulkan / Android API 26+ | Hardware MediaCodec → PRIVATE AImage/AHardwareBuffer → Vulkan external image and YCbCr sampler | GPU color conversion and copy into Unity RGBA8; acquire sync-fd semaphore and foreign queue ownership; no CPU pixel mapping/readback |
| OpenGL on Linux / OpenGL ES on Android | VAAPI hardware-frame download / MediaCodec ByteBuffer output, when the decoder supports it | CPU RGBA conversion and texture upload; software decoding is the final fallback. Native EGLImage/dma-buf texture sharing is not implemented |

This table describes the implemented bridge, not the full capabilities of those graphics APIs. Cross-API import uses explicit OS sharing handles on the same adapter; raw texture pointers are never reinterpreted as another API's objects.

The native GPU paths support 8-bit NV12 and BT.601 / BT.709 full or limited range. P010/HDR, BT.2020 and incompatible surface sizes are rejected by native texture sharing. Windows, Metal and Linux GPU paths also reject left/top crop offsets; Android applies the AImage crop rectangle while sampling. These restrictions do not by themselves prevent hardware decoding followed by CPU conversion. The final shader applies the appropriate gamma conversion for Unity Gamma and Linear project settings. Clockwise 90/180/270 degree display rotations are applied before publication.

`PreferNativeTextures = false` requests CPU upload while retaining the preferred hardware decoder; strict GPU mode overrides this preference. `DecoderType`/`DecoderName` describe decoding; `TransferMode` separately reports native GPU transport, hardware decode plus CPU RGBA upload, or software RGBA upload. A CPU-backed frame is not evidence of software decoding. D3D11VA, VAAPI and VideoToolbox download through `av_hwframe_transfer_data` on the decode worker. Android instead opens MediaCodec without a Surface and receives owned CPU buffers; it does not map an existing PRIVATE AHardwareBuffer. FFmpeg JNI initialization calls `libavcodec` directly, so this Android path does not depend on the optional graphics bridge. Generic decode devices may differ from Unity's graphics device; diagnostic descriptions distinguish a matched device from FFmpeg's default selection.

## Ownership and threads

Windows and Metal submission packets independently clone the `AVFrame`; disposing the managed presentation frame cannot recycle the decoder's texture-array slice or pixel buffer. Metal packets additionally retain both `CVMetalTextureRef` objects. Linux retains its mapped DRM PRIME frame, and Android retains its acquired AImage, as described below. D3D11 protects the shared immediate context with `ID3D10Multithread`. Rendering uses Unity render-thread plug-in events.

D3D11 frame retirement checks a GPU event query after video processing. Query failure alone is not treated as successful GPU completion. Metal records the Unity command buffer **after** the conversion draw and retires only when that buffer completes or errors. A final render event drains pending work when the presenter is disposed. Both paths cap outstanding packets at 24. Drain waits are bounded; an unresponsive driver retains resources instead of allowing GPU use-after-free. Device removal permits D3D11 retirement. A driver hang may therefore leave a bounded amount of memory retained until device/process teardown.

The Windows output texture is native typed RGBA UNORM, since video-processor output views may reject Unity's typeless/sRGB render targets. The final shader applies gamma decoding in Linear projects. D3D12 and Vulkan use a Unity-owned explicit RGBA8 UNORM intermediate. Metal luma/chroma textures are always sampled as linear data.

D3D12 uses Unity V8 plug-in events: a render-thread event requests COPY_DEST state, then a flushed submission-thread event waits for the D3D11 producer and submits the copy. An independent completion fence retains the command allocator, shared source and Unity target. D3D11 and D3D12 never signal the same fence from competing queues. Vulkan enables external-memory/keyed-mutex device extensions through the preload hook, matches the device LUID, acquires producer key 0, releases key 1 and performs a GPU copy through Unity's synchronized queue callback. It releases key 0 only after the copy. WGL registers, locks, unlocks and unregisters on the owning GL context. A retirement ticket keeps the Unity GL texture alive until unregister succeeds; a failed driver cleanup retains storage instead of deleting a registered GL name.

Vulkan plug-ins must have **Preload enabled** for extension interception before device creation. Capability and asynchronous submission failures produce a diagnostic. The player's strict GPU mode rejects unsupported configurations and never uploads CPU pixels, including pixels from a hardware decoder; ordinary hardware-preferred mode can recover through hardware download before attempting software decoding. Restart the Editor when updating the bridge. Windows/Apple retain ABI 2; Linux/Android use ABI 3 and additional platform-specific entry points. A GPU color conversion or copy is explicitly reported as such, never as strict decoded-plane zero-copy.

## Linux and Android Vulkan ownership

`VulkanPortable.cpp` shares the graphics-queue, compute conversion, copy and retirement implementation. Importers retain their native frame/image in a `FfuVkSample` owner. A render event transitions the Unity target through `AccessTexture`; `AccessQueue(flush=true)` submits only after Unity's earlier commands. The compute pass reads either two NV12 planes or one image with an immutable YCbCr conversion sampler. A native RGBA8 storage image is copied into Unity's explicitly formatted RGBA8 target. No product path calls `vkMapMemory` on video images. `VideoConvertSpirv.h` is generated from the included GLSL compute shader; shader tools are not required to compile the plug-in. A context-local pipeline cache reuses driver compilation data.

Linux creates the VAAPI device using the render node whose device major/minor numbers match `VkPhysicalDeviceDrmPropertiesEXT`. The worker maps VAAPI to DRM PRIME with `AV_HWFRAME_MAP_READ | AV_HWFRAME_MAP_DIRECT`: FFmpeg waits for `vaSyncSurface`, exports handles and retains the original decoder surface. It does not map pixels into CPU address space. Import validates R8/GR88 layers, object/plane indices, offsets/pitches, explicit DRM modifier layouts, modifier plane counts, importable format features, memory-type compatibility and allocation size. Every imported fd is independently duplicated and ownership transfers only after successful Vulkan allocation. Unknown modifiers, P010, unsupported layouts/HDR and left/top cropping are rejected with an error. VAAPI needs a compatible host GPU driver; its hardware driver is not bundled with FFmpeg.

Android's decode worker renders each MediaCodec output into an `AImageReader` with PRIVATE GPU-sampled storage. The imported AHardwareBuffer is retained with its AImage. A duplicated acquire sync-fd is imported into a temporary Vulkan semaphore. Both platforms acquire `VK_QUEUE_FAMILY_FOREIGN_EXT`, sample on Unity's graphics queue, and release ownership before the submission fence completes. The importer owner is released only after that fence. Failed/late GPU work retains resources with a 24-packet limit; the managed retirement collector polls even after the last player closes. Device-generation checks reject frames imported for an older Unity device.

Android is compiled at API 23 while AImage/AHardwareBuffer functions are resolved dynamically; native GPU image interop requires API 26+. MediaCodec ByteBuffer decoding does not require these image-reader functions or Vulkan extensions. Both Android modes use FFmpeg's JNI codec selection, which filters software-only codecs where Android exposes that flag and excludes known software codec names on older releases. Device metadata is still supplied by the platform; a CPU pixel format alone cannot establish the decoder's implementation. Missing extensions, Java VM/surface support or a hardware codec produce an explicit capability/decoder diagnostic.

The bridge's C ABI uses `cdecl`; Unity lifecycle/render callbacks use Unity's own calling convention. Win32 builds expose decorated and undecorated lifecycle aliases. All interface lookups use `GetInterfaceSplit`: `UnityInterfaceGUID` has a non-trivial C++ copy constructor, whose by-value calling convention differs between Win32 MinGW and Unity's MSVC build. Using the otherwise convenient `Get<T>()` can silently return no interface on Win32. iOS uses unique lifecycle symbols and explicit `UnityRegisterPlugin` registration from C# on Unity's initialized main thread, including Unity-as-a-Library. No AppController subclass is injected.

## Build and native test

The parent FFmpeg build scripts can invoke this CMake project after installing FFmpeg. It includes the Unity 6000.3 native headers under `Unity/` with their original license. Override `UNITY_PLUGIN_API` to use another installed Unity SDK.

```sh
cmake -S Tools/FFmpeg/Native -B Tools/FFmpeg/.build/bridge \
  -DFFMPEG_ROOT=/absolute/path/to/ffmpeg/install -DFFU_BUILD_TESTS=ON
cmake --build Tools/FFmpeg/.build/bridge --config Release
```

Windows produces `FFmpegUnityBridge.dll`; Linux and Android produce `libFFmpegUnityBridge.so`; macOS produces `libFFmpegUnityBridge.dylib`; iOS produces `libFFmpegUnityBridge.a`. Cross builds need matching CMake compiler/toolchain settings. CMake does not download FFmpeg.

`FFmpegUnityBridgeSmoke.exe` uses the actual Windows D3D11 adapter without launching Unity. It checks NV12 array slice 1 → RGBA pixels, limited-range black/white levels, and delayed AVFrame recycling. Exit 77 means the host lacks the necessary device/driver support; it is **not** a passing test. Keep `avutil-61.dll` and any toolchain runtime dependencies available beside the test or on `PATH`. The smoke test does not validate Unity's shader, Metal, or the actual hardware video codec; those require platform playback tests.

The shared Vulkan compute test needs a Vulkan loader/ICD, but no FFmpeg, VAAPI device or Android SDK. `FFU_VULKAN_TESTING` bypasses only the platform import-extension gate in this **test executable**; production bridge builds never define it. It still runs real compute, copies, deferred queue callbacks and GPU fences:

```sh
g++ -std=c++17 -O2 -Wall -Wextra -Werror -DFFU_VULKAN_TESTING \
  -ITools/FFmpeg/Native/Unity -ITools/FFmpeg/Native/ThirdParty/Vulkan-Headers/include \
  Tools/FFmpeg/Native/VulkanPortable.cpp Tools/FFmpeg/Native/tests/VulkanPortableSmoke.cpp \
  -l:libvulkan.so.1 -pthread -o vulkan-portable-smoke
./vulkan-portable-smoke
env -u LD_LIBRARY_PATH python3 Tools/FFmpeg/Native/tests/LoadLinuxBridge.py \
  Assets/Plugins/FFmpeg/Native/Linux/x86_64/libFFmpegUnityBridge.so
```

### Local validation, 2026-10-03

- Windows x86 and x64 native smoke: **PASS**, sampled white = 255 / black = 0, array slice 1 selected correctly, AVFrame buffer retained before GPU completion and released afterward. Built with LLVM-MinGW clang 23.1.2 and `-Wall -Wextra -Werror`; exported Unity lifecycle names checked without x86 stdcall suffixes. Compiler runtimes are linked statically.
- Native CMake configure/build: **PASS**, including the smoke executable.
- Unity 6000.3.17f1 on AMD Radeon RX 580 2048SP: Windows x86/x64, Mono/IL2CPP, D3D11/D3D12/OpenGL/Vulkan **all 16 combinations PASS**, 18 assertions each with real pixel readback and no software fallback. Use `-RequireHardware` with `Tools/Tests/FFmpegValidation/run-unity.ps1`; unlike `-Hardware`, this fails on fallback.
- D3D12 real-adapter native probe: shared D3D11 video-processor output, cross-API fence wait and D3D12 GPU copy **PASS**, white = 255 / black = 0.
- D3D12 x86/x64 linked-product smoke (`tests/D3D12Smoke.cpp`): **PASS**, four completed GPU submissions and one cancelled packet, correct AVFrame retirement and white = 255 / black = 0. This links the actual bridge translation units and also guards against packet-type linkage/ownership regressions.
- WGL x86/x64 helper probes: **PASS**, 10 write/lock/sample/unlock iterations for owned, borrowed mutable and borrowed immutable GL storage, including NV12 conversion, wrong-context rejection and COM/GL ownership checks.
- Vulkan x86/x64 helper probes: **PASS**, 64 shared-resource transfers, 786432 bytes compared exactly, automatic retirement past the queue bound, and caller release before a deferred Unity-style queue callback.
- Final Unity renderer/backend/architecture results are recorded in `Tools/Tests/FFmpegValidation/RESULTS.md`; native probes alone do not prove Unity integration.
- Apple builds were completed on the connected Mac for macOS x64/ARM64 and iOS device/simulator targets; the Apple M4 VideoToolbox + Metal native validation passed 231 checks. iOS device playback still requires a device run.
- Linux/Android shared Vulkan compute smoke (`tests/VulkanPortableSmoke.cpp`) on llvmpipe: **PASS**, eight deferred NV12/RGB submissions, 16384 output bytes compared, actual GPU-fence owner retirement, cancellation, the 24-packet cap and stale-device rejection. llvmpipe verifies the Vulkan compute/queue protocol; it does **not** verify VAAPI DMA-BUF or Android AHardwareBuffer import.
- Linux ABI 3 bridge CMake build, strict-warning compile, SPIR-V validation and staged absolute-path `dlopen` are validated separately. This WSL host has no `/dev/dri` render node, so actual VAAPI decode + DMA-BUF import remains untested here.
- Android ARM64 and ARMv7 IL2CPP on Xiaomi Mi MIX 2S / Adreno 630, Android API 35, Vulkan 1.1.128: **both PASS**, 33 assertions and 332 presented frames per architecture, `Android MediaCodec AHardwareBuffer + Vulkan GPU conversion (no CPU readback)`, no software fallback. Each run includes 300 frames of sustained playback (approximately 10 seconds), 10 `SeekAsync` calls, and assertions that close clears the displayed texture and prepared state. This validates the actual codec, AHardwareBuffer import and Unity Vulkan conversion; sustained playback exercises image recycling. The device test does not inspect native pending-image counts after close. GPU-fence owner retirement is checked independently by the native Vulkan compute test above.

Staged platform directories include `bridge-manifest.json` build records with binary/source hashes and toolchain information. Unity test logs and reports are under `Tools/Tests/FFmpegValidation/.work/` (ignored build output).

## API references

- [Unity external textures](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D.CreateExternalTexture.html)
- [Unity native rendering plug-in example and headers](https://github.com/Unity-Technologies/NativeRenderingPlugin)
- [Apple CVMetalTextureCache image mapping and required GPU lifetime](https://developer.apple.com/documentation/corevideo/cvmetaltexturecachecreatetexturefromimage(_:_:_:_:_:_:_:_:_:))
- [Microsoft D3D11 video processing](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11videocontext-videoprocessorblt)
- [Khronos DRM format modifiers and explicit plane layouts](https://docs.vulkan.org/refpages/latest/refpages/source/VK_EXT_image_drm_format_modifier.html)
- [Khronos foreign queue ownership](https://registry.khronos.org/vulkan/specs/latest/man/html/VK_EXT_queue_family_foreign.html)
- [Khronos Android hardware-buffer imports](https://registry.khronos.org/vulkan/specs/latest/man/html/VK_ANDROID_external_memory_android_hardware_buffer.html)
