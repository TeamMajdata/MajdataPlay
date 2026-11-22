# Isolated Vulkan texture upload probe

This tool builds a small Unity **6000.3.17f1** Windows x64 Mono Player for controlled graphics diagnostics. It contains one camera, one fullscreen quad, one sampling-only unlit shader, and one runtime script. It does not copy the game, FFmpeg, graphics bridge DLLs, media files, or third-party packages. The generated package manifest has no dependencies. All generated Unity metadata, the isolated project, build outputs, and logs stay under `.work/`.

## Build only

Run from the repository root:

```powershell
./Tools/Tests/Modules/VulkanTextureValidation/build.ps1
```

The default editor is `E:/Program Files/Unity/6000.3.17f1/Editor/Unity.exe`. The script checks its ProductVersion before use; override it with `-UnityEditor` only for another installation of the same exact version. It invokes Unity with `-batchmode -nographics`, builds `StandaloneWindows64` with Mono, and **does not start the Player**. Unity creates its own `.meta` files in the isolated project.

Outputs:

- `.work/Project/`: generated empty Unity project; only the runtime script and shader are staged into its runtime assets, plus the editor-only build entry.
- `.work/Player/RenderProbe.exe`: executable for a controlled GPU run.
- `.work/build.log`: Unity import, compilation, shader compilation, and build results.
- `.work/build-manifest.json`: exact editor ProductVersion, source SHA256 values, and executable / UnityPlayer.dll SHA256 values. Build completion also requires the current log's `[TextureProbe] BUILD DONE` marker.

The build uses the Built-in Render Pipeline, Gamma color space, Graphics Jobs enabled, no MSAA, and Vulkan / Direct3D 12 graphics APIs. The default Player is borderless fullscreen at a requested 1080 x 1920. Actual window size and display refresh are logged; the operating system can select the current desktop dimensions in borderless mode. `build.ps1` only overwrites the tool's recognized generated project and does not delete directories or change the main project's settings.

## Controlled Player commands

These commands **run GPU work**. Building the probe does not run any of them. Use separate log files for each controlled experiment.

```powershell
$probe = './Tools/Tests/Modules/VulkanTextureValidation/.work/Player/RenderProbe.exe'
$logs = './Tools/Tests/Modules/VulkanTextureValidation/.work'

# Dynamic 854 x 480 texture, 30 uploads/s, VSync on.
& $probe -force-vulkan -screen-fullscreen 1 -window-mode borderless `
  -screen-width 1080 -screen-height 1920 `
  -probe-width 854 -probe-height 480 -probe-upload-hz 30 -probe-seconds 180 `
  -probe-static 0 -probe-vsync 1 -probe-fps 144 -logFile "$logs/vulkan-dynamic.log"

# Same drawing and presentation, but upload exactly once.
& $probe -force-vulkan -probe-width 854 -probe-height 480 -probe-upload-hz 30 `
  -probe-seconds 180 -probe-static 1 -probe-vsync 1 -probe-fps 144 `
  -logFile "$logs/vulkan-static.log"

# Same dynamic upload through Direct3D 12.
& $probe -force-d3d12 -probe-width 854 -probe-height 480 -probe-upload-hz 30 `
  -probe-seconds 180 -probe-static 0 -probe-vsync 1 -probe-fps 144 `
  -logFile "$logs/d3d12-dynamic.log"

# Vulkan with a real application frame cap: turn VSync off.
& $probe -force-vulkan -probe-width 854 -probe-height 480 -probe-upload-hz 30 `
  -probe-seconds 180 -probe-static 0 -probe-vsync 0 -probe-fps 60 `
  -logFile "$logs/vulkan-60fps.log"

# Graphics Jobs controls use a backed-up isolated boot.config and require
# an actual kGfxThreadingModeThreaded log. See VULKAN-RENDERING-RESULTS.md.
```

The matrix runner performs the static Vulkan, dynamic Vulkan, dynamic Direct3D 12, and VSync-off 60 fps cases, records loaded modules and driver events, and stops at the first invalid or failing case. It requires a new output directory and also runs GPU work:

```powershell
./Tools/Tests/Modules/VulkanTextureValidation/run-matrix.ps1 `
  -Player ./Tools/Tests/Modules/VulkanTextureValidation/.work/Player/RenderProbe.exe `
  -OutputDirectory ./Tools/Tests/Modules/VulkanTextureValidation/.work/matrix-run-01 `
  -SecondsPerCase 180 -DynamicRepeats 1
```

Append `-Mode high-rate` and use a new output directory to run only the dynamic Vulkan case with VSync off and a requested 144 FPS cap. This does not change the desktop refresh rate or recreate a 144 Hz FIFO presentation condition on a 120 Hz display; inspect the measured frame-loop rate and actual settings.

`-UploadHz` selects the upload frequency for every case (default 30). Use `-Mode refresh-rate -UploadHz 60` to run one dynamic Vulkan case with VSync on and 60 uploads/s. Check the actual media frame rate before choosing a matched control: the current local 854 x 480 fault fixture is 60 FPS.

Append `-DisableObsCapture` to set `DISABLE_VULKAN_OBS_CAPTURE=1` only while starting each child, restoring the caller's original process environment immediately afterward. This uses the disable condition declared by the installed OBS Vulkan layer manifest; it changes no registry or system settings and closes no existing capture application. Inspect the child's `.modules.csv` to establish whether `graphics-hook64.dll` was actually absent. It does not disable NVIDIA Game Proxy or other hooks. A short run can validate removal but cannot establish TDR stability.

`launch-without-obs.ps1 -GameDirectory <Player directory>` provides the same child-specific switch for normal interactive launch, without menu automation or termination. It is a diagnostic control, not an established fix: in this investigation, three hook-disabled games and three subsequent hook-restored games all completed. Disabling the layer also disables the corresponding OBS Vulkan game-capture route. See `RESULTS.md` for the actual evidence.

Do not pass `-batchmode` or `-nographics` to these Player runs: the experiment needs normal rendering and presentation. On Windows, use a process handle's `WaitForExit()` if the caller must wait only for the GUI executable itself. The matrix runner manages controlled process lifetimes and evidence collection.

## Main game fixture

`prepare-main.ps1` makes a complete copy of a selected whitelist from the existing local `Build` into the new `.work/MainPlayer` directory. It refuses an existing destination, hashes every copied file, and checks that the source settings did not change. It keeps only the current `Transcend My Love` chart and the local skin, excludes original-video backups and built-in chart copies, and does not import personal runtime databases, caches, scores, or recordings. The current fixture is H.264 854 x 480 at 60 FPS.

The isolated settings disable game recording and online business, prefer hardware video, enable VSync, request FPSLimit 144, disable Vulkan pacing, select keyboard input, and disable touch-panel / LED connections. The existing application can still check GitHub versions and enumerate devices. A fresh runtime configuration seed selects Master through the supported legacy import, because this chart has no Easy notes. No registry, firewall, driver, or source-build setting is changed.

```powershell
./Tools/Tests/Modules/VulkanTextureValidation/prepare-main.ps1
./Tools/Tests/Modules/VulkanTextureValidation/run-main.ps1 `
  -Attempts 1 -SecondsInGame 200 -Graphics vulkan
# Repeat under identical isolated settings with only the child OBS layer disabled:
./Tools/Tests/Modules/VulkanTextureValidation/run-main.ps1 `
  -Attempts 1 -SecondsInGame 200 -Graphics vulkan -DisableObsCapture
```

The main runner activates only its own PID and verifies foreground ownership before menu keystrokes. If ordinary activation fails, it restores the verified child window and temporarily attaches input queues only for activation, detaching in `finally`; it never sends input to an unverified foreground owner. Navigation stops once Game is observed. It saves settings hashes, the isolated boot configuration and its before/after hashes, engine/runtime logs, module versions, and driver-event XML in a new per-run evidence directory. `-ExpectedThreadingMode` rejects a mode mismatch. Returning to List early, timeouts, navigation failure, device loss, and early natural exits remain separate invalid/failing outcomes. A deliberate cleanup termination after Result is not an application crash.

For an already prepared synthetic fixture, `-FrameSource synthetic -PlayerDirectory <isolated MainSynthetic>` requires INSTALL/PROGRESS and no target H.264 decode. `-StaticSyntheticFrame` uploads only the initial image while preserving its drawing; the runner requires every progress count to remain one. `-DecoupleSyntheticUpload` keeps dynamic uploads on one texture while the original RawImage displays an independent static texture; both modes allocate/initialize the same two textures. It requires the requested display binding, one display initialization, nonzero CPU binding checks and zero binding mismatches. These checks do not measure GPU sampling or prove that an unused resource's update became a GPU copy. Both switches use child-only environment values and restore the caller immediately after launching.

For the retired-region readback experiment, add `-ReadbackGate` with `-FrameSource synthetic`; it cannot be combined with static or decoupled controls. The runner requires a supported three-slot installation, matching contexts, successful readback/completion and reuse counters, and zero logged readback errors, illegal reuse or binding mismatches. Its `ArtifactBeforeSHA256`/`ArtifactAfterSHA256` fields preserve the actual Core/FFmpeg/MRP/helper hashes and native-plugin absence. Changed artifacts or settings invalidate a normal Result completion. Readback acknowledgements are not a last-use GPU fence, and this experimental schedule did not prevent the local Vulkan fault; see the rendering report before treating it as a workaround.

A single clean run cannot establish a fix for an intermittent failure; reproduce the matched baseline and repeat comparisons. Actual Player refresh/window settings must be retained because desktop WMI refresh need not match an exclusive Player mode. Findings and controlled limitations are recorded in `VULKAN-RENDERING-RESULTS.md`.

## Runtime options

Each option accepts a separated value (`-probe-width 854`), an attached value (`-probe-width854`), or an equals value (`-probe-width=854`).

| Option | Default | Meaning |
| --- | --- | --- |
| `-probe-width` | 854 | Texture width, from 1 through `SystemInfo.maxTextureSize`. |
| `-probe-height` | 480 | Texture height, from 1 through `SystemInfo.maxTextureSize`. |
| `-probe-upload-hz` | 30 | Upload scheduling frequency, 1–1000 Hz. At most one upload occurs in each Unity frame. |
| `-probe-seconds` | 180 | Duration, 1–86400 seconds. |
| `-probe-static` | 0 | 1 uploads once; 0 continues changing and uploading the same texture. |
| `-probe-vsync` | 1 | `QualitySettings.vSyncCount`, 0–4. |
| `-probe-fps` | 144 | `Application.targetFrameRate`, 1–1000. Unity ignores this cap when VSync is active. |

Upload frequency and frame rate are separate variables. Missed upload slots are skipped instead of being submitted in a burst after a stall. Test other texture sizes in separate runs, such as 848 x 480, 480 x 270, and 3840 x 2160. Resizing and repeated close/reopen are deliberately absent from the sustained-update fixture.

## Upload and measurement contract

A single persistent `byte[]` is pinned with `GCHandle`. Every upload calls `Texture2D.LoadRawTextureData(IntPtr, size)` and then `Apply(false, false)`, matching the production CPU-upload overload. The texture is `RGBA32`, with mipmaps disabled and `linear: false`, and uses clamp / bilinear sampling. The source is tightly packed and remains pinned until destruction. Dynamic mode changes an eight-row stripe before each upload, with no per-frame allocations. The entire texture is still uploaded each time. Static mode uploads the same initial pixels exactly once. The shader only samples that texture; its vertices directly cover clip space.

`[TextureProbe] START` and `SETTINGS` log the Unity version, actual graphics backend, GPU/driver, texture configuration, quality, VSync, target frame rate, active color space, display refresh, window size, and **actual `SystemInfo.renderingThreadingMode`**. A graphics-jobs disable argument is logged separately from the actual mode.

Every ten seconds, `[TextureProbe] PROGRESS` reports:

- `fps`: measured Unity **Update frame-loop rate**, not a measurement of swapchain presents or GPU execution.
- `uploadsPerSecond`: uploads observed in the reporting window.
- `uploads`: cumulative uploads, including initialization.
- `uploadCpuMs`: mean wall-clock CPU time spent inside `LoadRawTextureData` plus `Apply` in that window. This can include driver synchronization and is not a GPU timestamp.

Normal timed completion logs `[TextureProbe] DONE` with final window statistics and exits with code 0. Initialization / upload exceptions log `[TextureProbe] FAIL` and request exit code 1. A hang, driver reset, early process exit, or missing DONE must be recorded separately; a missing failure message does not establish success. There is no `ReadPixels`, asynchronous readback, runtime file access, per-frame object creation, or per-frame logging. Unity's `-logFile` owns log output.

Build success verifies compilation and packaging only. A completed run on one renderer, size, refresh rate, or driver does not prove other configurations are safe. CPU upload timing alone cannot establish GPU TDR, and no-FFmpeg reproduction would implicate the remaining Unity / rendering / driver path without identifying which layer is defective.
