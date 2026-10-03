# VLCUnity graphics bridge

MajdataPlay uses `LibVLCSharp.VlcVideoOutput` for Windows, Linux, macOS, Android
and iOS. It selects a shared GPU surface when the renderer, device and decoder
output support the required interop, and falls back to aligned RGBA memory
callbacks when they do not. If VLC itself cannot initialize, `BGManager` uses
Unity VideoPlayer and logs the availability diagnostic.

The complete decoder distribution currently included in this repository is
**Windows x64 only**. Portable bridge source or a compiled bridge library is not
a complete VLC installation: non-Windows targets still need their own matching
engine, decoder modules and native dependencies. Build validation rejects
incomplete target bundles even though the editor/runtime has a diagnostic
fallback.

## GPU paths and CPU fallback

| Platform / Unity renderer | Shared GPU presentation path | Capability requirements |
| --- | --- | --- |
| Windows / Direct3D 11 | D3D11 output and shared D3D11 texture | Compatible D3D11 device/driver |
| Windows / Direct3D 12 | D3D11 output imported by D3D12 | Same DXGI adapter and shared-resource support |
| Windows / OpenGL Core | D3D11 output imported by WGL | `WGL_NV_DX_interop2` |
| Windows / Vulkan | D3D11 output imported by Vulkan | Matching adapter LUID, Win32 external memory and keyed-mutex extensions |
| Linux / OpenGL Core | Independent VLC GLX/EGL context sharing textures with Unity | Compatible shared context and framebuffer format |
| Linux / Vulkan | EGL-rendered RGBA exported as DMA-BUF, imported with its DRM format modifier | Matching DRM render device, EGL DMA-BUF export, Vulkan external-memory/modifier/foreign-queue extensions |
| Android / OpenGL ES 3 | Independent VLC EGL context sharing textures with Unity | Compatible EGL share group and framebuffer format |
| Android / Vulkan | EGL-rendered AHardwareBuffer imported by Vulkan | Android 8/API 26 or newer for AHardwareBuffer interop, Vulkan 1.1 and the required external-memory/foreign-queue extensions |
| macOS / Metal | CGL output into an IOSurface-backed CoreVideo pixel buffer, exposed as a Metal texture | Compatible CoreVideo GL/Metal texture caches and BGRA8 IOSurface |
| iOS / Metal | EAGL output into an IOSurface-backed CoreVideo pixel buffer, exposed as a Metal texture | Compatible CoreVideo GLES/Metal texture caches and BGRA8 IOSurface |
| macOS / OpenGL Core | Independent CGL producer sharing RGBA8 2D textures with Unity | Actual OpenGL Core renderer with a compatible CGL share group |
| iOS / OpenGL ES 3 | Independent EAGL producer sharing RGBA8 2D textures with Unity | Only when the Unity version/platform exposes an actual GLES 3 renderer and compatible EAGL share group |

These are implemented backend routes, **not a claim that every listed platform
has passed playback testing**. The validation record below distinguishes compiled
code, surface-sharing tests and actual decoded playback.

“Zero CPU copy” refers to avoiding CPU pixel readback/upload between VLC's GPU
output and Unity. GPU color conversion and the final blit into a Unity-owned
RenderTexture still occur. End-to-end hardware decoding additionally requires
a compatible codec, hardware decoder module and GPU output path in the supplied
LibVLC build. `UsesGpuInterop` reports presentation interop; it does not report
which decoder VLC selected, and it must not be interpreted as proof of hardware
decoding for an individual video.

Interop is checked on the actual Unity rendering device. A failed capability or
surface import selects CPU callbacks, including an asynchronous stop/restart if
the failure is discovered after playback begins. CPU output uses Unity RGBA32
uploads and works independently of the active renderer; it is not a zero-copy
path. Shared resources remain alive through queued render events and GPU consumer
completion. GPU handoff waits favor correctness over maximum asynchronous
throughput.

## Version and managed bindings

The native ABI is pinned to **LibVLC `4.0.0-dev-33583-gd94fd0473f`** and the
bundled LibVLCSharp `4.0.0+a296e6f14b326bde2e7796439ea883b6c6040fb9`.
LibVLC 3 and newer LibVLC 4 nightlies are not substitutes. The portable bridge
validates the changeset before using the single-argument media-player constructor.
Changing the expected revision string does not adapt a changed ABI.

Managed bindings now live at:

- `Assets/Plugins/VLCUnity/Runtime/Plugins/LibVLCSharp.dll`: shared desktop/Android
  binding, also used by every editor host.
- `Assets/Plugins/VLCUnity/Runtime/Plugins/iOS/LibVLCSharp.dll`: iOS player binding,
  with native imports rewritten to `__Internal`.
- `Tools/VLCUnity/Bindings/LibVLCSharp.upstream.dll`: unchanged pinned input,
  outside Assets so Unity does not import it.

Regenerate the variants reproducibly:

```powershell
./Tools/VLCUnity/Prepare-Bindings.ps1
./Tools/Tests/VLCUnityValidation/Validate-Bindings.ps1
```

The transformation preserves the Windows loader, bypasses obsolete macOS paths
on other hosts, distinguishes x64 from ARM64 for native log arguments, and fixes
iOS platform predicates and imports. The script checks the original input hash.
See [binding details](Bindings/README.md). Do not copy the old Windows-folder
managed DLL back into the project alongside the shared version.

## Build the bridge

Windows x64 requires Visual Studio C++ tools and a Windows SDK:

```powershell
./Tools/VLCUnity/Native/build.ps1
./Tools/VLCUnity/Native/build.ps1 -Install
```

The default output is `Temp/VLCUnityNative/VLCUnityPlugin.dll`.

Linux requires a native C++/CMake toolchain and OpenGL, EGL, GLX and X11 development
libraries. macOS/iOS require Xcode on an Apple host:

```sh
bash Tools/VLCUnity/Native/build-portable.sh linux
bash Tools/VLCUnity/Native/build-portable.sh macos
bash Tools/VLCUnity/Native/build-portable.sh ios-device
bash Tools/VLCUnity/Native/build-portable.sh ios-simulator
```

These scripts build the bridge into `Build/VLCUnityPortable/<target>/package`.
They do not obtain or build the decoder distribution.

For Android, supply an Android NDK plus CMake/Ninja:

```powershell
./Tools/VLCUnity/Native/build-android.ps1 -Ndk '/path/to/ndk' -Install
```

The supported packaged ABIs are `armeabi-v7a`, `arm64-v8a` and `x86_64`.
The bridge uses static libc++ and 16 KiB ELF page alignment; all supplied decoder
libraries must also match the device ABI and page-size requirements. The baseline
Android build API does not imply support for the higher-API AHardwareBuffer path.
For CPU-only bridge builds, configure CMake with `-DVLCUNITY_ENABLE_GPU=OFF`.

Keep native renderer bridges **preloaded** and restart Unity after installing or
replacing one. Vulkan extension interception and the iOS renderer registration
must run before Unity creates the graphics device. See
[native portable build notes](Native/PORTABLE.md).

## Native bundle layout and packaging

All paths below are relative to
`Assets/Plugins/VLCUnity/Runtime/Plugins/`.

| Target | Bundle directory | Required native payload |
| --- | --- | --- |
| Windows x64 | `Windows/x86_64/` | `VLCUnityPlugin.dll`, `libvlc.dll`, `libvlccore.dll`, matching `plugins/` and dependencies |
| Linux x64 | `Linux/x86_64/` | `libVLCUnityPlugin.so`, `libvlc.so`, `libvlccore.so`, matching `plugins/` and dependencies |
| macOS | `MacOS/universal/`, or selected `MacOS/x86_64/` / `MacOS/ARM64/` | Matching bridge/engine/core dylibs, `plugins/` and dependencies |
| Android | `Android/libs/<abi>/` | `libVLCUnityPlugin.so`, `libvlc.so` with its static modules or matching registration mechanism, and every transitive dependency |
| iOS device | `iOS/Device/` | Bridge/engine/core static archives, module/contrib archives, registration source and bundle manifest |
| iOS simulator | `iOS/Simulator/` | The equivalent **simulator** archives, registration source and manifest |

Run **Tools > VLCUnity > Configure platform plugins**, then **Validate current
platform bundle** in Unity. Pre-build checks verify required files, selected
architectures, desktop decoder modules and the iOS manifest. Presence and CPU
architecture checks do not replace ABI validation and target playback tests.
Editor import settings follow the editor host, not the selected player target.

Desktop post-build staging preserves the decoder module hierarchy:

- Windows/Linux: `<Player>_Data/Plugins/x86_64/plugins/`.
- macOS: `<Player>.app/Contents/Plugins/plugins/`, including Xcode-export staging.

macOS libraries must have resolvable `@loader_path`/`@rpath` dependencies and be
signed with the finished application. Universal bridge, engine and module dylibs
must all contain both required CPU slices; one universal bridge cannot make a
single-architecture decoder universal.

Android libraries are packaged by PluginImporter for each selected ABI. Desktop
module folders cannot be substituted for Android modules. Android does not use
the iOS JSON manifest; ordinary Android Internet access configuration is needed
only for media loaded over the network.

The Android bootstrap loads `vlc` and then `VLCUnityPlugin` through
`java.lang.System.loadLibrary`, so their `JNI_OnLoad` functions receive the real
JavaVM. MediaCodec decoding also requires the matching VideoLAN Java helpers,
including `org.videolan.libvlc.AWindow`; package those helpers/AAR from the pinned
decoder build. Native `.so` files do not provide Java classes. Successful GPU
presentation alone does not verify that the Java/MediaCodec decoder integration
is available or working on a device. When minification is enabled, preserve
`org.videolan.libvlc.AWindow`, its `SurfaceCallback` and native JNI helpers using
the matching AAR's consumer ProGuard rules or equivalent application rules.

### iOS bundle manifest

Each selected Device/Simulator directory needs `vlc-unity-bundle.json` with
schema version 1. This is a **schema example**, not a complete decoder dependency
list; use archive names and dependencies from the exact pinned LibVLC build:

```json
{
  "schemaVersion": 1,
  "staticPluginRegistration": "VLCStaticPlugins.c",
  "forceLoadArchives": [
    "libvlc.a",
    "libvlccore.a",
    "plugins/libavcodec_plugin.a"
  ],
  "frameworks": [
    "AudioToolbox.framework",
    "VideoToolbox.framework",
    "CoreMedia.framework"
  ],
  "libraries": ["c++", "z", "iconv", "bz2"]
}
```

The registration source must implement the C symbol
`VLCUnityRegisterStaticModules(void)` using that engine build's real static
module table. An empty function cannot register decoders. The portable bootstrap
calls it before `libvlc_new`. Archive/source paths must stay within the selected
bundle directory.

The exporter adds the registration source and `iOS/LoadPlugin.mm` to
UnityFramework, applies `-force_load` to the listed archives, and links the
listed SDK frameworks/libraries plus the renderer's required Apple frameworks.
`LoadPlugin.mm` registers the statically linked rendering bridge with Unity.
iOS uses IL2CPP and the generated `__Internal` binding.

Device and simulator ARM64 archives are different platform binaries: never
combine them with `lipo`. Creating a bridge archive with unresolved decoder
symbols is not a successful final UnityFramework link. Native decoder bundles,
Apple compilation, complete linking and device validation remain required.
UWP, WebGL and 32-bit Windows are outside this integration's build targets.

## Managed use and lifetime

Call `VlcRuntime.TryCreateLibrary(out library, out diagnostic)` on the Unity main
thread. Portable initialization first validates the bridge/engine ABI and sets
desktop module search paths. Android/iOS static modules do not use APK/IPA asset
URLs as `VLC_PLUGIN_PATH`.

Construct `VlcVideoOutput`, wait for `IsReady`, then assign and play
`output.Player.Media`. Call `TryUpdateTexture()` on the main thread and display
`output.Texture`. Metal surfaces use BGRA32; the other shared surfaces use
RGBA32. Dispose the output once; it owns its player and presentation textures.
Keep the library alive until its outputs have been disposed.

Do not bypass this wrapper with `MediaPlayer.GetTexture` or sample a native
texture without its acquire/blit/release render events. Queued native contexts
are retired by an ordered cleanup event after media-player release.

CPU callbacks negotiate RGBA, handle aligned/padded rows, bound buffering,
normalize orientation and avoid Unity APIs on decoder threads. Delegates and
handles remain rooted until `MediaPlayer.Dispose()` joins the output threads;
LibVLC 4's asynchronous `Stop()` is not a cleanup boundary. `BGManager` waits for
an actual first frame instead of a fixed delay and cancels preparation on
destruction.

## Validation and recorded results

```powershell
dotnet run --project Tools/Tests/VLCUnityValidation/CallbackValidation.csproj
./Tools/Tests/VLCUnityValidation/Validate-Bindings.ps1
./Tools/Tests/VLCUnityValidation/Compile-PlatformMatrix.ps1
./Tools/VLCUnity/Native/test-interop.ps1
./Tools/Tests/VLCUnityValidation/Run-GraphicsValidation.ps1 -NativePlugin Temp/VLCUnityNative/VLCUnityPlugin.dll
./Tools/Tests/VLCUnityValidation/Run-GraphicsValidation.ps1 -ForceCpu -NativePlugin Temp/VLCUnityNative/VLCUnityPlugin.dll
```

The Unity harness creates an isolated project under `Temp`, checks the requested
renderer, quadrant colors, odd-sized resolution changes, real WebM decoding,
pause, rewind and disposal. Results are under
`Temp/VLCUnityGraphicsValidation/Results`. It requires real graphics
initialization; do not use `-nographics`. The macro compilation matrix checks
managed/editor declarations and editor-host versus target selection; it is not
a native cross-platform playback test.

Recorded Windows validation on **2026-10-02**, Unity 6000.3.17f1 and an AMD Radeon
RX 580 2048SP, before the portable expansion:

| Renderer | GPU sharing, colors, resize, seek and disposal | Forced CPU callbacks |
| --- | --- | --- |
| D3D11 | Passed | Passed |
| D3D12 | Passed | Passed |
| Vulkan | Passed | Passed |
| OpenGL Core | Passed | Passed |

The native D3D11/D3D12 tests each decoded 20 frames. WGL and Vulkan surface
fixtures each completed 32 ownership round trips. Callback tests cover 10,000
dropped frames and 5,000 concurrent producer frames.

Portable validation recorded during the **2026-10-03** work:

| Target / check | Evidence and limit |
| --- | --- |
| Managed bindings and platform branches | Deterministic binding/290 native-import validation and all eight C# host/target combinations passed. |
| Windows regression after portable expansion | D3D11 and Vulkan Unity decoded-playback tests passed with GPU sharing enabled. |
| Portable ABI and GPU lifecycle | MSVC and GCC tests passed, including async producer waits, shutdown-before-device-destruction, late events, resize and CPU fallback. |
| Linux native compilation | GCC 13.3 compilation with warnings as errors passed. |
| Linux EGL and GLX texture sharing | Each passed 32 cross-thread sharing/readback checks using Mesa llvmpipe; software-renderer interop only, with no VLC decoder and no hardware-decoding claim. |
| Linux Vulkan preload | Mock-loader tests passed for extension negotiation, original-device fallback, API versions and reused device handles. |
| Linux Vulkan DMA-BUF | Target-device import and decoded playback have not been verified. |
| Android | GPU bridge cross-compilation passed for ARMv7, ARM64 and x86_64; no device playback or hardware-decoding result is recorded here. |
| macOS / iOS | Source and packaging support are present; an Apple host was unavailable, so native compilation, final linking and playback remain unverified. |

A separate whole-game `dotnet build` was blocked by the existing
`UnsafeKit/RefDisposable.cs` CS9064 target-runtime error. Plugin checks do not
constitute a verified full game build. Update this record with concrete target,
driver, decoder and test results after running the remaining validation.

## Sources and licenses

`Native/Unity` contains Unity Native Plugin API headers under the Unity Companion
License. `Native/ThirdParty` contains Khronos Vulkan 1.3.275 headers under
Apache-2.0. Their notices remain alongside the headers. LibVLCSharp and the
supplied VLC libraries retain their original licenses and notices.

- [Pinned LibVLCSharp sources](https://github.com/videolan/libvlcsharp/tree/a296e6f14b326bde2e7796439ea883b6c6040fb9)
- [VideoLAN VLC Unity](https://github.com/videolan/vlc-unity)
- [LibVLC output callback reference](https://videolan.videolan.me/vlc/master/group__libvlc__media__player.html) — current upstream reference; the shipped ABI remains pinned.
- [WGL D3D11 interop](https://registry.khronos.org/OpenGL/extensions/NV/WGL_NV_DX_interop2.txt)
- [Vulkan Win32 keyed mutex](https://docs.vulkan.org/refpages/latest/refpages/source/VK_KHR_win32_keyed_mutex.html)
