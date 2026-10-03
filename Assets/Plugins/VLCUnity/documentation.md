# VLCUnity in MajdataPlay

This repository integrates VLC with Unity on Windows, Linux, macOS, Android and
iOS. Use `LibVLCSharp.VlcRuntime` and `LibVLCSharp.VlcVideoOutput`; `BGManager`
already uses them and falls back to Unity VideoPlayer when VLC is unavailable.

**Only the Windows x64 decoder distribution is included.** Other targets need
the matching native VLC engine, modules and dependencies in addition to the
bridge. Source support, a compiled bridge and a complete working decoder bundle
are different deliverables. The build validator rejects incomplete bundles.

Full build commands, dependency layout, iOS manifest examples and the validation
record are in [`Tools/VLCUnity/README.md`](../../../Tools/VLCUnity/README.md).

## Graphics output

| Platform | GPU presentation route |
| --- | --- |
| Windows | D3D11 shared output for D3D11, D3D12, OpenGL Core through WGL interop, and Vulkan through Win32 external memory |
| Linux | GLX/EGL context sharing for OpenGL Core; EGL DMA-BUF export and Vulkan import with DRM modifiers for Vulkan |
| Android | EGL context sharing for OpenGL ES 3; AHardwareBuffer sharing for Vulkan on compatible API 26+ devices |
| macOS | CoreVideo/IOSurface sharing with Metal; independent shared CGL context for OpenGL Core |
| iOS | CoreVideo/IOSurface sharing with Metal; independent shared EAGL context when Unity exposes an actual GLES 3 renderer |

The selected route depends on the active device, extensions and decoder output.
An unavailable interop route falls back to portable RGBA callbacks and Unity
texture uploads. The CPU path is available independently of the active graphics
API. `new VlcVideoOutput(library, forceCpu: true)` explicitly exercises it.

GPU interop avoids CPU pixel readback and upload between VLC output and Unity.
Video color conversion and a GPU blit into a Unity-owned texture still occur.
`UsesGpuInterop` reports this presentation path; it does **not** prove that VLC
selected a hardware decoder. End-to-end hardware decoding also depends on the
codec, driver and modules supplied with the pinned VLC build.

Actual decoded Unity playback has been verified on Windows D3D11, D3D12,
OpenGL Core and Vulkan, including forced CPU output. Linux has a separate Mesa
llvmpipe EGL and GLX texture-sharing tests; these are software-renderer surface tests,
not VLC decoding or hardware-decoding validation. Android device playback and
Apple native builds/playback remain unverified. Consult the main validation
record before treating a platform as tested.

## Required binaries and bindings

The native ABI is pinned to LibVLC **`4.0.0-dev-33583-gd94fd0473f`** and
LibVLCSharp **`4.0.0+a296e6f14b326bde2e7796439ea883b6c6040fb9`**. LibVLC 3 or a
newer nightly cannot be substituted without adapting and validating the ABI.
The portable bridge checks the engine changeset before creating a player.

The shared managed binding is `Runtime/Plugins/LibVLCSharp.dll`. The iOS player
uses `Runtime/Plugins/iOS/LibVLCSharp.dll`, whose native imports resolve through
`__Internal`. Editors always use the shared binding for their host platform,
regardless of the selected player target. The original binding is preserved
outside Assets at `Tools/VLCUnity/Bindings/LibVLCSharp.upstream.dll`.

Run `Tools/VLCUnity/Prepare-Bindings.ps1` to regenerate both variants. Its pinned
input hash and explicit transformations are documented in
[Bindings/README.md](../../../Tools/VLCUnity/Bindings/README.md).

Native bundles belong under `Runtime/Plugins/`:

- `Windows/x86_64/` and `Linux/x86_64/`: bridge, engine, core, dependencies and
  the complete matching `plugins/` directory.
- `MacOS/universal/`, or `MacOS/x86_64/` / `MacOS/ARM64/`: matching bridge,
  engine, core and module dylibs with the selected slices.
- `Android/libs/<abi>/`: bridge, engine with its registered decoder modules,
  and all transitive dependencies for `armeabi-v7a`, `arm64-v8a` or `x86_64`.
- `iOS/Device/` and `iOS/Simulator/`: platform-specific static archives, static
  module registration source and `vlc-unity-bundle.json`.

Use **Tools > VLCUnity > Configure platform plugins** and **Validate current
platform bundle** after installing a bundle. Desktop staging preserves decoder
module subdirectories. Keep the renderer bridge preloaded and restart the
editor after replacing native binaries.

Android initialization loads `vlc` and then `VLCUnityPlugin` through Java so the
libraries receive the real JavaVM in `JNI_OnLoad`. A decoder using MediaCodec
also needs the matching VideoLAN Java helpers, including
`org.videolan.libvlc.AWindow`; native `.so` files alone do not supply those
classes. Package the corresponding helpers/AAR from the pinned decoder build.
If using minification, retain `AWindow`, its `SurfaceCallback` and JNI helpers
with that AAR's consumer ProGuard rules or equivalent application rules.
An Android Vulkan presentation path does not by itself establish MediaCodec
decoding. Network media requires the application's ordinary Internet permission.

iOS requires IL2CPP, the generated binding, static renderer registration and an
implementation of `VLCUnityRegisterStaticModules(void)` from the actual decoder
bundle. Its manifest records module/contrib archives and SDK dependencies.
Device and simulator archives are distinct, even when both use ARM64. See the
main build guide for the manifest and UnityFramework linking requirements.
UWP, WebGL and 32-bit Windows are outside this integration's build targets.

## Playback and resource lifetime

On Unity's main thread:

1. Call `VlcRuntime.TryCreateLibrary(out library, out diagnostic)` and handle an
   unavailable library. The portable bootstrap validates native dependencies
   and registers static modules before constructing LibVLC.
2. Construct `VlcVideoOutput`, wait for `IsReady`, assign `output.Player.Media`
   and start playback. Initialization can require a rendering event.
3. Call `output.TryUpdateTexture()` each frame and use `output.Texture` for
   display. Wait for an actual first frame before assuming playback is ready.
4. Dispose the output before disposing its LibVLC library. The output owns its
   player and presentation textures.

Do not sample `MediaPlayer.GetTexture` directly. The wrapper schedules the
acquire, blit and release operations required to synchronize shared GPU
surfaces. It also handles resize, unavailable imports and decoder callbacks.
CPU callbacks run without Unity API calls and keep aligned buffers/delegates
alive until player release joins the native output threads. LibVLC 4's
asynchronous `Stop()` alone does not finish resource cleanup.

`BGManager` waits for output initialization and a first decoded frame, cancels
preparation on destruction, and keeps a still-image background when playback
cannot be prepared.

## Upstream references

These links describe the upstream projects, not the contents or validation
status of this repository:

- [LibVLCSharp documentation](https://code.videolan.org/videolan/LibVLCSharp/-/blob/master/docs/home.md)
- [VLC Unity source](https://code.videolan.org/videolan/vlc-unity)
- [Pinned managed source](https://github.com/videolan/libvlcsharp/tree/a296e6f14b326bde2e7796439ea883b6c6040fb9)

The original package's platform matrix, bundled-binary, watermark and demo
scene instructions were historical vendor documentation. This project guide
replaces those claims with the actual integration and dependency requirements.
