# Android graphics state validation

Run with PowerShell 7, Unity **6000.3.17f1** and its Android SDK/NDK/IL2CPP support:

```powershell
./Tools/Tests/Modules/GraphicsStateValidation/Run-Android.ps1 -Serial <adb-serial>
```

The script stages a small independent project at ignored `Temp/GSC`, copies the
production `AndroidGraphicsStateWarmup` and storage implementation, and uses the
repository's actual UniTask package. Only diagnostics are replaced by a forwarding
stub. It builds an ARM64 IL2CPP development APK with Vulkan and High managed stripping,
with native engine stripping disabled (the minimal scene otherwise loses native
FontAsset support used by Unity's development UI), installs the dedicated package
`net.majdata.graphicsstatevalidation`, wakes the screen, and launches the probe.
Existing game packages and their data are preserved.
Keep the screen unlocked and the probe in the foreground.

The probe creates a real camera, SpriteRenderer, mesh and overlay Canvas. Its camera
command buffer explicitly selects pass zero of the production `Maj/Sprites/Default`
and `Maj/UI/Default` shaders, because their `SRPDefaultUnlit` passes are not selected
by the built-in camera automatically. A regular MeshRenderer uses `Unlit/Color`. The probe
asserts that all three appear in a nonempty GraphicsStateCollection, saves and
reloads the capture, checks variant/state count preservation and API/quality/null
rejection, verifies precanceled warmup, then awaits production asynchronous warmup
and checks actual completion. Runtime output, the device log, and the probe capture
are retained under `Temp/GSC`. `-SkipBuild` reinstalls the existing APK; do not use
it after changing any source under test.

This is a focused API and lifecycle regression. The capture is **not a production
gameplay collection**: it does not cover the MRP pipeline, gameplay scenes, full
material/keyword/state combinations, ARMv7, OpenGLES3, or other devices. It must not
be shipped as if it covered the game. Scene capture and production integration
remain separate validation steps. Warmup speed measured after trace is also not
a cold-start performance benchmark.

## Full application capture

`Build-FullAppCapture.ps1` builds the real scenes and MRP in a separately prepared
copy of this project. Use Unity 6000.3.17f1 and initialize its dependencies exactly
as for the main project. Preserve the source `.meta` files so captures continue
to refer to the production shaders. Exclude `Assets/Tests` from this capture copy;
it is a development player, and unrelated desktop recorder tests do not compile
when the Editor switches to the Android target. Never use the main open project.

```powershell
./Tools/Tests/Modules/GraphicsStateValidation/Build-FullAppCapture.ps1 `
    -ProjectPath C:/Capture/MajdataPlay -GraphicsApi Vulkan
adb -s <serial> install -r C:/Capture/GraphicsCapture/MajdataPlay.GraphicsCapture-Vulkan.apk
adb -s <serial> shell monkey -p net.majdata.majdataplay.graphicscapture 1
```

The script copies `FullAppCaptureBuild.cs` and `FullAppCaptureDriver.cs` into the
isolated copy only. It builds an ARM64 IL2CPP Development player with the requested
graphics API, target SDK 36 and package `net.majdata.majdataplay.graphicscapture`.
This keeps its settings, databases and exported captures separate from the
installed game and its legacy shared storage directory.

The capture script also copies the existing `libSkiaSharp.so` and
`libHarfBuzzSharp.so` from the isolated copy's
`ThirdParty/SkiaForUnity/Library/arm64-v8a/` into its
`Assets/Plugins/Android/arm64-v8a/` and configures Android ARM64 importers. The
source Android plugin metadata currently contains no importer configuration,
and these native libraries were absent from the initial full-app APK. This
capture-only setup keeps skin decoding available without changing the source
dependency or its metadata. It does not validate the ordinary build's native
packaging.

Keep the device awake and unlocked. `-CaptureMode Matrix` is the default; it
initializes Title and List once, enables autoplay at normal speed, then records
the complete bundled **月下繚乱 ~2022 Re:Master~ / Master** chart and twelve seconds of Result for
each row below. List is only a short lifecycle transition between runs. Menu
coverage at every configuration is not claimed.

| Group | Requested MSAA | Requested output | Scene scale |
| --- | --- | --- | --- |
| 1 | 1x | Direct | 100% |
| 2 | 1x | Intermediate, compatibility mode | 50% |
| 3 | 2x | Direct | 100% |
| 4 | 2x | Intermediate | 100% |
| 5 | 4x | Direct | 100% |
| 6 | 4x | Intermediate | 150% |
| 7 | 8x | Direct | 100% |
| 8 | 8x | Intermediate | 100% |

All groups run in the same process and Unity `Default` quality level, retaining
one cumulative collection. Allow about twenty-five minutes. `-CaptureMode Single`
runs only group 1 for a shorter packaged-collection replay. The driver waits
for actual Running gameplay and checks elapsed playback against the full audio
length, then observes thirty stable rendered frames in both Game and Result.
`GSC_GROUP_STAGE` records the actual output path and allocated attachment sample
count, plus requested output and any supported output fallback. Correlate it
with `[MRP.Attachments]` logs; an 8x request falling back to
4x does not establish true 8x coverage. Attachment failures and timeouts emit
`GSC_FULL_CAPTURE_FAILED` and prevent batch completion.

To select another chart without rebuilding, put its exact local song title in
the dedicated package's `files/GraphicsStates/capture-song.txt` before launch.
The default is the bundled song above. An unreadable file, unknown title, or
missing Master difficulty fails the run; the driver never substitutes a song.

For an OpenGLES3 variant-only capture, `-GraphicsApi OpenGLES3 -CaptureMode Legacy`
selects matrix configurations 1, 2 and 6: direct 1x, intermediate 1x/50%, and
intermediate 4x/150%. Each still plays the complete chart and Result. Compare
the collections saved after each group to establish whether the final MSAA group
adds shader variants; add other configurations if the observed variants require
them. Pull each group's cumulative checkpoint immediately after its
`GSC_GROUP_COMPLETE` and the following production `Saved` message, giving each
host file a different name. The device's single filename is overwritten by later
checkpoints, so the final file alone cannot establish per-group changes.
This smaller mode is not evidence of Vulkan PSO coverage or support for
every GLES output path. Do not start another Unity batch process while capturing
device logs, because Editor shutdown may stop the ADB server and disconnect the
reader.

Require every `GSC_GROUP_COMPLETE` marker for the selected mode (Matrix: eight,
Legacy: three, Single: one), `GSC_FULL_CAPTURE_COMPLETE`,
and a later successful production collector save before pulling the final
file. The completion marker deliberately says the final quitting save is still
pending. Import and inspect the pulled collection before replacing a previous
capture; keep useful earlier states by merging collections in Unity. The
production collector saves scene checkpoints and the final collection to:

```powershell
adb -s <serial> pull /sdcard/Android/data/net.majdata.majdataplay.graphicscapture/files/GraphicsStates/Android-Vulkan-Quality0.graphicsstate ./Android-Vulkan-Quality0.graphicsstate
```

For manual song selection, opt out of the test driver's scene changes and automatic
exit by creating `files/GraphicsStates/manual-capture` in the capture application's
directory. The current driver template checks this marker before starting, once
per second while active and before transitions or quitting. The production
collector continues tracing. Set the marker before launch for a manual session;
remove only this marker before requesting a later automatic session. Older
capture APKs built without this template do not support the marker.

```powershell
adb -s <serial> shell mkdir -p /sdcard/Android/data/net.majdata.majdataplay.graphicscapture/files/GraphicsStates
adb -s <serial> shell touch /sdcard/Android/data/net.majdata.majdataplay.graphicscapture/files/GraphicsStates/manual-capture
```

Review the capture, import it under `Assets/Resources/GraphicsStates/Android/`
using Unity, and preserve its `.meta`. Rebuild with that asset and verify gameplay
loading warms the matching collection. A single chart covers only the scenes,
materials and states encountered during that run. Repeat with more charts,
skins and runtime rendering settings for broader coverage. `-GraphicsApi
OpenGLES3` creates a separate API capture; Vulkan data cannot substitute for it.

If the existing first-install resource extraction is too slow for a capture
session, stop only the dedicated capture package and seed its own persistent
directory with the **same staged** `Assets/StreamingAssets` bytes via ADB. Copy
all streaming assets to `files/ExtStreamingAssets/`, the bundled
`MaiCharts/Original` tree to `files/MaiCharts/Original/`, and `Skins/default` to
`files/Skins/default/`, then relaunch. Exclude `.meta` files from the device seed.
Do not seed another installed game's data or settings. Record this setup in the
results: it exercises rendering of the bundled content but bypasses first-install
resource extraction.
