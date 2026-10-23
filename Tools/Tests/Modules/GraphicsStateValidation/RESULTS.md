# Graphics state validation results

## Unity regression tests

Using Unity **6000.3.17f1** in the isolated project:

- Graphics-state warmup EditMode tests: **17 / 17 passed**.
- SceneSwitcher PlayMode tests: **10 / 10 passed**.

Executed commands, confirmed by the test runner and Unity logs:

```powershell
./Tools/Tests/Run-UnityTests.ps1 -TestPlatform EditMode -AssemblyNames MajdataPlay.Tests.Game -TestFilter MajdataPlay.Tests.Game.Rendering.AndroidGraphicsStateWarmupTests
./Tools/Tests/Run-UnityTests.ps1 -TestPlatform PlayMode -AssemblyNames MajdataPlay.Tests.Game.PlayMode -TestFilter MajdataPlay.Tests.Game.Global.PlayMode.SceneSwitcherPlayModeTests
```

These verify metadata selection, empty/canceled warmup behavior and the existing
scene transition behavior. They are separate from Android native device evidence
and do not establish target-platform coverage by themselves.

## 2026-10-24 — focused Android device probe

Command, from the repository root in PowerShell 7:

```powershell
./Tools/Tests/Modules/GraphicsStateValidation/Run-Android.ps1 -Serial <adb-serial>
```

- Unity: **6000.3.17f1** (`cf0352b38e81`), development Player.
- Device: Mi MIX 2S, Android API 35, Adreno 630.
- Backend/ABI/API: **IL2CPP / ARM64 / Vulkan**; High managed stripping,
  native engine stripping disabled for this minimal probe.
- Quality: `Medium`.
- Result: **PASS**, 4 shader variants, 6 recorded graphics states,
  `completedWarmupCount = 6`, `isWarmedUp = true`.
- Traced shaders: `Maj/UI/Default`, `Maj/Sprites/Default`, `Unlit/Color`,
  and Unity's `Hidden/Internal-GUITexture`.
- Production helper assertions passed: matching Android/API/quality accepted;
  wrong API, wrong quality and null rejected; an already canceled token canceled;
  actual asynchronous warmup reached completion.
- Native collection assertions passed: begin/end trace, nonempty state capture,
  save/reload, unchanged variant and graphics-state counts.

The production sprite/UI shaders are unmodified. The camera command buffer
explicitly draws their pass zero: the built-in camera does not select their
`SRPDefaultUnlit` passes automatically. This verifies actual Vulkan shader-state
recording and warmup, **not the production MRP rendering pipeline**. The normal
mesh renderer exercises the built-in shader; the scene also contains a
SpriteRenderer and an overlay Canvas.

Evidence is retained in ignored `Temp/GSC/build.log`, `device-result.txt`,
`device.log`, `probe.graphicsstate`, and `Artifacts/GraphicsStates.apk`. The
dedicated installed package is `net.majdata.graphicsstatevalidation`.

The initial probe iterations exposed harness issues (result-file creation,
native stripping of development font support, and built-in selection of SRP
shader passes); the passing run includes their fixes. The probe capture is not
a shipping asset. Full gameplay/scene state coverage, MRP integration, ARMv7,
OpenGLES3, mid-flight cancellation, other devices and cold-start performance
are **not verified by this probe**. Production app capture is a separate result.

## Full application capture: 2026-10-24

- Unity **6000.3.17f1**, Android **ARM64 / IL2CPP / Vulkan**, quality `Default`.
- The full source project was built in an isolated copy with the production MRP,
  scene assets, shader GUIDs, skin loader, gameplay and graphics-state collector.
  The main open checkout was not built or reconfigured.
- Package: `net.majdata.majdataplay.graphicscapture`; target SDK **36**, with its
  own app-private settings and data.
- Observed Title initialization, List, EX-AID Master autoplay, a subsequent
  user-selected song, Result, return to List and normal exit. The driver logged
  `GSC_FULL_CAPTURE_FINISH_SCENE: Result` and `GSC_FULL_CAPTURE_COMPLETE`.
- Final capture: **24 shader variants / 38 graphics states**, **215,228 bytes**.
  Recorded shader families include MRP sprites and UI, note effects and HoldShine,
  TMP, FFmpeg video planes, list covers and the scene transition.
- Rendering settings at capture completion: **Performance**, antialiasing
  **Disabled**, texture quality **High**, output **Direct**, compatibility mode
  **false**, render scale **100%**, system resolution scaling **100%**, FPS limit
  **120** and VSync **true**. Only the `Rendering` section was exported to
  `rendering-settings.json`; other settings were not included in the evidence.
- Unity's importer retained all **24 / 38** entries, Android platform `11`,
  Vulkan API `21`, quality `Default`. The production asset GUID is
  `a4411e3d6ff24ef4eba0c81909b7389c`; its SHA-256 is
  `F11F72501CB8950467E668EAB3F1E81CB14E17AB9B150B9B56646EFE46E3F2AA`.

Preparation limitations:

- Staged `Assets/Tests` was excluded because the existing recorder PlayMode tests
  reference desktop-only `GameOptions.RecordMode` when importing for Android.
  The separate desktop test results are unaffected.
- The first APK omitted the existing Android Skia/HarfBuzz native libraries.
  Skin initialization failed in that attempt, so its trace was discarded. The
  capture-only builder explicitly imported the existing ARM64 libraries; both
  were verified in the rebuilt APK before the successful capture. This does not
  certify the ordinary project's native packaging.
- Initial packaged-resource extraction was very slow. The dedicated capture app
  was stopped and seeded with the same staged `StreamingAssets` bytes: all files
  under `files/ExtStreamingAssets/`, bundled charts under
  `files/MaiCharts/Original/`, and the default skin under `files/Skins/default/`.
  This bypassed first-install extraction; no existing game package or user data
  was changed.
- Scene teardown logged an existing FFmpeg `SetTexture(null)` argument exception;
  app shutdown also logged existing HttpClient/Bass disposal exceptions. They did
  not prevent final collection serialization or the observed Result scene.

Evidence is retained outside the checkout under
`%LOCALAPPDATA%/MajdataPlay.GraphicsCapture/Rebuild/`: `build-Vulkan.log`,
`device.log`, `runtime.log`, and `Android-Vulkan-Quality0.checkpoint.graphicsstate`.
This captures the exercised content, not every chart, skin, note type, rendering
preset or device. OpenGLES3, ARMv7 and cold-start performance are not established
by this run. Loading and warming a baked collection require the separate replay
validation below.

## Baked collection replay: 2026-10-24

The final production `.graphicsstate` and its Unity-generated `.meta` were copied
into the isolated full project's Resources folder, rebuilt and installed under
the same dedicated capture package. Unity imported all **24 variants / 38
graphics states** and the Android player used that packaged collection on the
normal `SceneSwitcher` path before entering EX-AID Master gameplay.

Observed production log at **00:43:48 Asia/Shanghai**:

```text
[AndroidGraphicsStateWarmup] Warmed 24 variants / 38 graphics states in 22.3 ms (Vulkan).
```

There was no missing-capture warning at this transition. This confirms actual
packaged-resource loading and completion of the production asynchronous warmup
job in the real app. The elapsed time is a same-device observation after earlier
capture runs, **not a cold-cache performance benchmark** or a guarantee that all
future gameplay states are covered. The original, broader capture remains the
production asset; the replay's fresh trace does not replace it.

The Game scene subsequently reported loading complete and loaded **541 timing
points** for EX-AID. A later manual transition through List into another Game
loaded **696 timing points** without warming the same completed collection again.
No Skia or skin-initialization failure recurred in this replay.

The replay subsequently reached Result at **00:47:23** and the automatic driver
completed and exited at **00:47:43**. The `manual-capture` marker was written
after that exit; it did not stop this completed run. It remains in the dedicated
capture application's directory and disables automatic scene control on the
next launch. The application was not stopped or relaunched after writing it.

Build and runtime evidence are under
`%LOCALAPPDATA%/MajdataPlay.GraphicsCapture/Baked/build-Vulkan.log` and
`Baked/device.log`. This build includes the test driver's manual-capture marker
support described in the README. The same capture-only native packaging and
resource seeding limitations from the prior run apply.

## MRP Vulkan matrix with the requested full chart: 2026-10-24

The dedicated full-app capture package recorded all eight requested MRP
combinations in one Android **ARM64 / IL2CPP / Vulkan** process, using Unity
**6000.3.17f1**, the Mi MIX 2S (Adreno 630), and Unity quality `Default`.
Each group used **月下繚乱 ~2022 Re:Master~**, **Master 14**, with autoplay at
**1x playback speed**. No song or difficulty was substituted.

The first group began at **01:22:36 Asia/Shanghai** on 2026-10-24. Every group
reached actual Running gameplay, played the full **147.29-second** audio, reached
Result, and retained Result for at least **12 seconds** before returning to List.
Observed elapsed gameplay was **146.97–147.13 seconds**, within the driver's
full-chart tolerance. Both Game and Result supplied thirty stable completed
frames per group. The final group completed at **01:48:38**, the driver logged
`GSC_FULL_CAPTURE_COMPLETE groups=8; fallbackGroups=2` at **01:48:44.4758**, and
the production collector saved the final trace at **01:48:44.5551** on normal
exit. All these lifecycle markers identify the same process.

| Group | Requested samples | Requested output | Scene scale | Actual samples | Actual Game and Result output |
| --- | --- | --- | --- | --- | --- |
| 1 | 1x | Direct | 100% | 1x | `Direct` |
| 2 | 1x | Intermediate, compatibility mode | 50% | 1x | `IntermediateScaled` |
| 3 | 2x | Direct | 100% | 2x | `DirectResolve` |
| 4 | 2x | Intermediate | 100% | 2x | `IntermediateRequested` |
| 5 | 4x | Direct | 100% | 4x | `DirectResolve` |
| 6 | 4x | Intermediate | 150% | 4x | `IntermediateScaled` |
| 7 | 8x | Direct | 100% | **4x fallback** | `DirectResolve` |
| 8 | 8x | Intermediate | 100% | **4x fallback** | `IntermediateRequested` |

Every stage reported `outputFallback=False`; sampling fallback is recorded
separately. Groups 7 and 8 establish the device's 8x-request fallback behavior,
**not true 8x graphics-state coverage**. The display was 1080 x 2160; the 50% and
150% intermediate groups allocated 540 x 1080 and 1620 x 3240 scene attachments,
respectively. System resolution scaling remained 100% and texture quality High.

The final pulled raw capture, `Final.graphicsstate`, contains **25 shader
variants / 160 graphics states** and is **393,318 bytes**. Its SHA-256 is
`D95D78AB44AEC421EAC7DC881D854327EC7340F18740AF6249D214F5810A84E5`.
Unity's imported-asset audit passed with **23 distinct shaders** and exact
agreement between declared and enumerated variant/state counts. Every shader
reference resolved to a persistent GUID/local file ID and asset path. Metadata
was Android / Vulkan / `Default`, collection version `0`.

| Imported capture | Variants | States | 1x states | 2x states | 4x states |
| --- | --- | --- | --- | --- | --- |
| New full-chart matrix | 25 | 160 | 56 | 54 | 50 |
| Earlier Performance baseline | 24 | 38 | 30 | 8 | 0 |
| In-memory native union | 25 | 160 | 56 | 54 | 50 |

The native union added **0 variants / 0 states** from the earlier baseline.
A repeated insertion pass over both inputs added nothing; the matrix already
preserves all earlier coverage. Unity logged `GSC_UNION_UNNECESSARY` and
`GSC_ASSET_AUDIT_PASSED`, so no redundant union file was written. The matrix was
selected for the same production resource path while preserving GUID
`a4411e3d6ff24ef4eba0c81909b7389c`. The raw input and old baseline remain separate
evidence. The selected **25 / 160** resource subsequently completed packaged
production warmup on the device, as recorded below; this confirmation is
separate from the historical 24 / 38 replay.

Evidence is retained in
`%LOCALAPPDATA%/MajdataPlay.GraphicsCapture/Matrix-Vulkan-Gekka/`:

- `build-Vulkan.log` and `MajdataPlay.GraphicsCapture-Vulkan.apk`: isolated
  development build using `-GraphicsApi Vulkan -CaptureMode Matrix`.
- `device.log`: the original live log, retained through group 6 Game. The host
  environment interruption stopped the reader rather than the device run.
- `device-final.log`: the recovered device log buffer with group 6 Result,
  groups 7–8, the eight-group completion marker and final successful save.
  Use the two logs together for complete lifecycle evidence.
- `Final.graphicsstate`: final raw device capture, preserved before import.
- `Audit/audit.log`, `collections.tsv`, `variants.tsv`, `states.tsv` and
  `union-additions.tsv`: successful imported-asset/native union audit.
- `Group1.graphicsstate` through `Group5.graphicsstate`: earlier cumulative
  checkpoints. No group 6–8 snapshots were retained; do not infer per-group
  native-state additions for those groups from the final file alone.
- Screenshots cover Game and Result for groups 1–5 and Game for group 6. They
  are supporting evidence, **not eight complete screenshot pairs**; the runtime
  markers and output diagnostics establish the remaining stages.

The capture-only native-library packaging, staged-test exclusion and resource
seeding limitations described in the earlier full-app capture still apply.
This run covers the requested full chart and Result under the observed MRP
paths, rather than every chart, skin or material keyword. OpenGLES3 capture was
**not requested for completion** by the user and was not added to this matrix.
ARMv7, other devices, genuine 8x attachments and cold-cache performance remain
**unverified**.

## Final 25 / 160 packaged production warmup: 2026-10-24

The selected matrix was copied into the production Resources path and the
isolated full-app project without changing its GUID. The dedicated package was
rebuilt with `-GraphicsApi Vulkan -CaptureMode Single`, using the selected
**25 shader variants / 160 graphics states** asset. The driver selected
**月下繚乱 ~2022 Re:Master~ Master** at **02:07:02.3497 Asia/Shanghai**.

The actual packaged collection completed production warmup through the normal
`SceneSwitcher` path before gameplay, logged at **02:07:03.3229 Asia/Shanghai**:

```text
[AndroidGraphicsStateWarmup]Warmed 25 variants / 160 graphics states in 44.4 ms (Vulkan).
```

This confirms the rebuilt Android player loaded the selected resource and
completed the native asynchronous job for all **160** recorded states. The
**44.4 ms** observation follows previous captures and replays on the same
device, reusing its existing driver cache; it is **not a cold-cache benchmark**
or proof that every future rendering state will be covered.

The Single replay subsequently completed the requested full chart and Result:

- Game reached thirty stable frames at **02:07:33.9834**, with **1x / Direct**
  output and no output fallback. The chart loaded **696 timing points**.
- Result reached thirty stable frames at **02:10:13.3797**, retaining
  **1x / Direct** with no output fallback. The group completed at
  **02:10:13.3806**, with **147.39 seconds** of observed playback for
  **147.29 seconds** of audio.
- `GSC_FULL_CAPTURE_COMPLETE groups=1; fallbackGroups=0` was logged at
  **02:10:19.4066**. The same process then exited normally.
- `Game.png` was visually inspected for the chart notes, UI and background.

The replay's fresh trace contains **25 variants / 53 graphics states**. It
exercises a narrower single configuration and is retained only as validation
evidence; it does **not replace the selected 25 / 160 matrix**. Build and
runtime evidence are retained in
`%LOCALAPPDATA%/MajdataPlay.GraphicsCapture/Final-Vulkan-Gekka/`, including
`build-Vulkan.log`, `device.log` and `Game.png`. OpenGLES3 was not rebuilt or
supplemented, as requested by the user.
