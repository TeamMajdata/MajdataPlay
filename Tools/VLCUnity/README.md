# VLCUnity Windows graphics bridge

This repository builds its Windows x64 bridge from `Native/RenderingPlugin.cpp`.
It preserves the ABI of the bundled LibVLCSharp and
`LibVLC 4.0.0-dev-33583-gd94fd0473f`; newer LibVLC 4 nightlies are **not** drop-in
replacements. The decoder DLLs and their plugins are unchanged.

| Unity graphics API | Preferred video path | Fallback |
| --- | --- | --- |
| Direct3D 11 | D3D11 hardware decoder/output and a shared D3D11 texture | RGBA memory callbacks |
| Direct3D 12 | D3D11 hardware decoder/output on the same DXGI adapter; shared resource imported by D3D12 | RGBA memory callbacks |
| OpenGL Core | D3D11 output shared through `WGL_NV_DX_interop2` | RGBA memory callbacks |
| Vulkan | D3D11 output imported with `VK_KHR_external_memory_win32` and `VK_KHR_win32_keyed_mutex`, matching adapter LUID | RGBA memory callbacks |

The GPU paths avoid CPU video readback/upload. VLC still converts the decoder's
video surface to RGBA on the GPU, and Unity blits that shared texture to an owned
RenderTexture. This is zero **CPU** copy, not a claim that YUV conversion or every
GPU copy disappears. Hardware decoding also depends on codec and driver support;
`VlcVideoOutput.UsesGpuInterop` describes presentation, not the decoder selected
for every individual file.

Shared surfaces have explicit producer/consumer ownership. This implementation
waits for GPU completion at the handoff; it prioritizes correct lifetime and
pixel contents over maximum asynchronous throughput. The Vulkan path additionally
uses keyed mutex submissions and external queue-family ownership barriers.
Unsupported device/import capabilities select the portable callback path.

## Build

Install the Visual Studio C++ x64 tools and a Windows 10/11 SDK, then run from the
repository root:

```powershell
./Tools/VLCUnity/Native/build.ps1
# After validation, copy the built DLL into the Unity plugin directory:
./Tools/VLCUnity/Native/build.ps1 -Install
```

The default output is `Temp/VLCUnityNative/VLCUnityPlugin.dll`. Close/restart
Unity when replacing a loaded native DLL. Keep the plugin's `isPreloaded: 1`
import setting: Vulkan device extensions must be requested before Unity creates
the graphics device. A plugin imported after editor startup may use CPU fallback
until the next editor launch.

The bridge targets Windows x64. Other platforms continue using the existing
Unity VideoPlayer paths in this application.

## Managed use

Use `LibVLCSharp.VlcVideoOutput` instead of manually calling `MediaPlayer.GetTexture`.
Construct it on the Unity main thread, wait for `IsReady`, then assign/play
`output.Player.Media`. Call `TryUpdateTexture()` from the main thread and display
`output.Texture`. Dispose the output once; it owns its player and presentation
textures. Render events bracket access to shared surfaces, so directly sampling a
raw native texture without these events is unsupported.

The CPU path negotiates packed RGBA, handles padded/aligned VLC rows, normalizes
orientation, and only calls Unity texture APIs on the main thread. Native callbacks
remain rooted until `MediaPlayer.Dispose()` joins the output threads; asynchronous
`Stop()` alone is not a resource-lifetime boundary.

## Validation

```powershell
dotnet run --project Tools/Tests/VLCUnityValidation/CallbackValidation.csproj
./Tools/VLCUnity/Native/test-interop.ps1
./Tools/Tests/VLCUnityValidation/Run-GraphicsValidation.ps1 -NativePlugin Temp/VLCUnityNative/VLCUnityPlugin.dll
./Tools/Tests/VLCUnityValidation/Run-GraphicsValidation.ps1 -ForceCpu -NativePlugin Temp/VLCUnityNative/VLCUnityPlugin.dll
```

The graphics harness creates an isolated Unity project under `Temp`, verifies the
requested graphics API, checks RGBA quadrant images and odd-sized resolution
changes, decodes a bundled WebM, and exercises pause/stop/disposal. Results are
written under `Temp/VLCUnityGraphicsValidation/Results`. It requires real graphics
initialization; do not add `-nographics`.

Validated on 2026-10-02 with Unity 6000.3.17f1 and an AMD Radeon RX 580 2048SP:

| Backend | GPU sharing + colors/resize/seek/disposal | Forced CPU callbacks |
| --- | --- | --- |
| D3D11 | Passed | Passed |
| D3D12 | Passed | Passed |
| Vulkan | Passed | Passed |
| OpenGL Core | Passed | Passed |

The native D3D11/D3D12 decoder tests each verified 20 decoded frames. The WGL
and Vulkan interop fixtures each verified 32 ownership round trips. Callback
tests include 10,000 dropped frames and 5,000 concurrent producer frames.
These are plugin/isolated-project checks. A separate `dotnet build` of the whole
game was blocked by the existing `UnsafeKit/RefDisposable.cs` CS9064 target-runtime
error; a complete game build was not verified in this change.

## Included headers

`Native/Unity` contains Unity Native Plugin API headers (Unity Companion License).
`Native/ThirdParty` contains Khronos Vulkan 1.3.275 headers (Apache-2.0).
Their license files and copyright notices are retained alongside the headers.
`LibVlcAbi.h` contains the small pinned ABI declaration subset used by this bridge.

References:

- [VideoLAN VLC Unity](https://github.com/videolan/vlc-unity)
- [LibVLC video output callbacks](https://videolan.videolan.me/vlc/master/group__libvlc__media__player.html)
- [WGL D3D11 interop](https://registry.khronos.org/OpenGL/extensions/NV/WGL_NV_DX_interop2.txt)
- [Vulkan Win32 keyed mutex](https://docs.vulkan.org/refpages/latest/refpages/source/VK_KHR_win32_keyed_mutex.html)
