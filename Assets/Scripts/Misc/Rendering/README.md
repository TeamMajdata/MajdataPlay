# Android graphics state capture

Android gameplay loading uses `GraphicsStateCollection` assets from
`Assets/Resources/GraphicsStates/Android/`. A usable collection must match the
Android runtime, graphics API and Unity quality level name. Capture actual
rendering on the target API before expecting this warmup to cover gameplay.

## Included capture

`Assets/Resources/GraphicsStates/Android/Android-Vulkan-Default.graphicsstate`
contains **25 shader variants / 160 graphics states** for Android/Vulkan and
Unity quality `Default`. It was captured on 2026-10-24 with Unity 6000.3.17f1 on
a Mi MIX 2S (Adreno 630), using the real MRP and the requested full **月下繚乱
~2022 Re:Master~ Master 14** chart and Result.

One process completed all eight requested **1x/2x/4x/8x samples ×
Direct/Intermediate** combinations. The downscaled intermediate group used 50%
and compatibility mode; the supersampled 4x intermediate group used 150%.
Both 8x requests fell back to 4x on this device. Unity's imported-state audit
found **56 states at 1x, 54 at 2x and 50 at 4x**, with **no true 8x states**.
The native union audit confirmed the matrix already contains every variant and
state from the earlier **24 / 38** Performance capture; no separate union file
was needed. The asset retains its original Unity GUID.

The final packaged **25 / 160** matrix completed the production asynchronous
warmup on the device at **02:07:03 Asia/Shanghai** on 2026-10-24, taking
**44.4 ms**. This reused the device's existing driver cache and is **not a
cold-cache performance benchmark**. The Single replay subsequently completed
the full chart and Result, then exited normally. Its fresh, narrower **25 / 53**
trace is validation evidence; the included asset remains the full **25 / 160**
matrix. See the validation results for the lifecycle evidence,
actual output paths, asset audit and isolated-build limitations. This captures
the exercised chart, scene UI and effects, and preserves prior video-shader
states through baseline containment; it does not establish exhaustive chart,
skin or other-API coverage.

The game's EnergySaving, Performance, Beautiful and Refined presets all retain
Unity's `Default` quality level. Their states can accumulate in the same
collection during one capture session. A different Unity quality level is stored
separately by the collector and selected by name by the warmup loader.

## MRP settings to cover

The following capture plan follows `RenderingOptions.ApplyPreset` and the actual
MRP attachment/output paths. Coverage assumes the same rendered content, camera
configuration and device-supported attachment formats.

| Setting | Requested rendering configuration | Capture requirement |
| --- | --- | --- |
| Performance | 1x, 100%, direct output | Baseline; included capture covers the exercised content. |
| EnergySaving | Same as Performance, texture LOD bias +1 | Shares Performance states; the bias is a shader uniform, not another shader variant. |
| Beautiful | 4x MSAA, 100%, direct output requested | Capture; sample count changes, and supported Vulkan devices can resolve directly to the screen. |
| Refined | 4x MSAA, 150%, intermediate output | Capture; scene color uses an intermediate target followed by `Hidden/Maj/FinalCopy`. |
| Compatibility mode | 1x, 50%, intermediate output, texture LOD bias +2 | Capture the single-sample intermediate path; Refined alone does not establish this coverage. |

For custom settings, cover the actual supported sample counts 1x, 2x, 4x and 8x
with both direct output requested and intermediate output. The presets above
already exercise several combinations. Check `[MRP.Attachments]` and the current
output-path display: a request for 8x that falls back to 4x does not capture true
8x states. Direct output can also fall back to an intermediate path on a given
device; do not treat the menu selection as proof of the path rendered.

There is no need to enumerate every resolution percentage, texture quality,
FPS limit or VSync value solely for PSO coverage. Size and sampler/uniform changes
within the same attachment/pass configuration do not introduce a distinct MRP
shader variant. However, crossing from direct output to a scaled intermediate
target must be exercised. A representative downscale and supersampling path
also provide useful image validation without treating every slider value as a
separate collection.

Visit actual gameplay, UI masks, note effects, backgrounds and any Live2D content
under each distinct configuration; changing a setting only in the menu does not
record the gameplay states. The table describes why these paths require
coverage. The matrix above records them for the requested chart and its
packaged production warmup has been verified; other content needs coverage.

See [validation results](../../../Tools/Tests/Modules/GraphicsStateValidation/RESULTS.md)
for the device evidence and isolated-build setup limitations.

## Capture on a device

1. Build with **Unity 6000.3.17f1**, the Android target and **Development Build**
   enabled. `AndroidGraphicsStateTrace` starts before the first scene. It is
   excluded from release players, other platforms and Editor play mode.
2. Run the development player on the device. Visit Init, Title, Login, List,
   Game and Result, and exercise the note types, skins, backgrounds and rendering
   settings that the release should cover. Unity's collection only records what
   is rendered. A Unity quality level can contain several runtime rendering
   presets; exercise their MSAA/render-scale settings as needed.
3. Return to another scene or background the application to checkpoint. Scene
   loads, quality-level changes, loss of application focus and normal exit save
   the trace; forced termination is not a reliable final checkpoint. Tracing
   resumes when the app regains focus. Files are written only at these boundaries,
   never in a per-frame loop.
4. Check the device's MajdataPlay log for `AndroidGraphicsStateTrace` and a
   successful save with a nonzero variant count. The file is under
   `Application.persistentDataPath/GraphicsStates/`, named for its API and quality
   index, for example `Android-Vulkan-Quality0.graphicsstate`.
5. Pull the file with ADB. The usual application identifier and external storage
   location give this command; use the actual player identifier/path if different:

   ```powershell
   adb pull /sdcard/Android/data/net.majdata.majdataplay/files/GraphicsStates/Android-Vulkan-Quality0.graphicsstate ./Android-Vulkan-Quality0.graphicsstate
   ```

6. Import the captured `.graphicsstate` into
   `Assets/Resources/GraphicsStates/Android/` using Unity. Keep its generated
   `.meta` file with the asset. Rebuild the Android player and confirm that the
   gameplay loading log reports the matching collection being warmed up.

Each run starts a fresh capture and overwrites the corresponding device files.
Pull a completed capture before beginning another run. Capture each shipping
graphics API and Unity quality level separately. Keep the shader/material setup
and build stripping settings consistent with the release, and recapture after
changes that affect shader variants or graphics states. The included capture and the subsequently recorded matrix are described above;
new content or configurations still need their own coverage.

On devices where `SystemInfo.supportsParallelPSOCreation` is false, Unity can
record shader variants without graphics states. A successful capture or warmup
does not by itself demonstrate elimination of shader compilation stalls; verify
the rebuilt player on the target device, including a cold startup and gameplay.

API reference: [Unity 6000.3 GraphicsStateCollection.BeginTrace](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/experimental/rendering/graphicsstatecollection/begintrace).
