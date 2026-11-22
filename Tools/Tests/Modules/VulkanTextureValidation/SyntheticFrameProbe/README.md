# Synthetic frames inside the main Mono Player

This diagnostic replaces only `MajdataPlay.Scenes.Game.BGManager.SetMovieAsync(string, Sprite)` in a **new** `.work/MainSynthetic` Player copy. It retains the game's MRP, original RawImage, private background material and brightness control, Live2D, chart, input, and audio flow. The helper is a plain static class loaded by a direct managed assembly reference; it does not require a new Unity component or runtime-initialize registration.

This is not a process-wide no-FFmpeg build. Global menu/background players remain present. The game background's movie-preparation entry is the only patched method; its existing async state-machine type is left intact but is no longer entered through that method.

## Offline build and prepare

.NET SDK **9.0.200** is pinned locally. The helper disables implicit framework references and uses only the actual `Build/MajdataPlay_Data/Managed` BCL (`mscorlib`, `System`, `System.Core`) and Unity DLLs, with `/nostdlib` and `/noconfig`. Its `netstandard2.1` output directory is an SDK build label; the DLL must not reference `netstandard` or `System.Private.CoreLib`. Explicit assembly metadata preserves the `1.0.0.0` identity expected by the already patched Core. The Patcher uses installed .NET 9 reference packs and the Unity installation's existing Mono.Cecil 0.11.1 DLL. It links the existing production `Assets/Plugins/MajdataPlay/IO/Storage` sources and performs its own file/directory access through `MajdataPlay.IO.Storage.FileSystem`; `System.IO.Path` is used only for path calculations. `NuGet.Config` clears all package feeds. Neither project builds a Unity-generated project or restores third-party packages.

From this directory:

```powershell
dotnet build Helper.csproj -c Release --nologo
dotnet build Patcher.csproj -c Release --nologo

# Must be a new destination. This copies the original Build whitelist, not an active Player.
../prepare-main.ps1 -DestinationName MainSynthetic

dotnet ./bin/Patcher/net9.0/Patcher.dll `
  ../.work/MainSynthetic `
  E:/repos/MajdataPlay/MajdataPlay/Build `
  ./bin/Helper/netstandard2.1/MajdataPlay.SyntheticFrameProbe.dll
```

None of these commands starts a Player. The prepared copy contains only the selected `Transcend My Love` chart with the current H.264 854 x 480 @ 60 fps video, offline/recording-disabled keyboard-only configuration, and the supported legacy `Runtime/config.json` seed selecting Master (4). Personal runtime databases, other charts, logs, and original video backups are not copied. Preparation refuses to overwrite an existing target.

Outputs:

- `bin/Helper/netstandard2.1/MajdataPlay.SyntheticFrameProbe.dll`: runtime helper.
- `bin/Patcher/net9.0/Patcher.dll`: standalone Cecil patch tool.
- `../.work/MainSynthetic/MajdataPlay_Data/Managed/MajdataPlay.Core.dll`: patched copy.
- `../.work/MainSynthetic/patch-evidence/MajdataPlay.Core.original.dll`: byte-identical backup of the copied original Core.
- `../.work/MainSynthetic/patch-manifest.json`: original/backup/patched/helper SHA256 values, original and replacement IL, added reference, and the sole changed method.

The patch tool rejects strong-named inputs, wrong target paths, reparse-point target ancestors, an existing patch, a populated runtime database, unexpected helper dependencies, missing Player dependency files, BCL or Unity assembly identity mismatches (name, version, culture and public key token), unexpected background fields, or an unexpected original entry signature/IL. BCL references are never exempt: a `netstandard, Version=2.1.0.0` reference is rejected when that assembly is absent from the actual Player. It rereads the temporary assembly and compares every method's instructions, local types and exception handlers before replacing the isolated Core. Only one body may differ, with one new helper assembly reference. The original Build Core hash is checked again after patching. Existing PDBs are not used to interpret the modified entry IL.

## Optional suppression of video preparation

The default Core patch leaves global menu video players present. After that patch, the optional `--disable-video-prepares` mode can replace only `FFmpegVideoPlayer.PrepareAsync(CancellationToken) -> Task` in the isolated `MajdataPlay.FFmpeg.dll` with `call Task.get_CompletedTask; ret`. The getter is imported from the actual Player's `mscorlib.dll`; host framework metadata is never used. This suppresses video session creation through that method in menus and Game. It changes neither the Core patch nor the helper, audio, chart code, or other FFmpeg methods.

From this directory, with the isolated Player stopped:

```powershell
# Read-only: construct and reread the candidate entirely in memory, without evidence or DLL writes.
dotnet ./bin/Patcher/net9.0/Patcher.dll --disable-video-prepares `
  ../.work/MainSynthetic E:/repos/MajdataPlay/MajdataPlay/Build --verify-only

# Explicit application, only when this additional diagnostic variant is wanted.
dotnet ./bin/Patcher/net9.0/Patcher.dll --disable-video-prepares `
  ../.work/MainSynthetic E:/repos/MajdataPlay/MajdataPlay/Build
```

The mode requires the existing single-method Core manifest and exact patched Core SHA256, validates the original Build path and recorded FFmpeg/Core source hashes, rejects strong names and unexpected original preparation IL/signatures, and checks that the synthetic Player is not running. It allows an existing runtime database but does not open or alter it. A reread must show exactly one FFmpeg method body changed and an identical assembly-reference sequence. Application creates uniquely named FFmpeg backup/candidate files under `patch-evidence` and a separate `disable-video-prepares-*.json` manifest. Existing evidence is retained; rerunning application on an already modified FFmpeg DLL is rejected. Neither mode starts a Player or moves native bridge DLLs. Runtime logs are still required to establish which native video sessions actually open.

## Optional suppression of native render passes

`--disable-native-render-pass` is an independent control for the already Core-patched synthetic fixture. It targets the actual `MajdataPlay.MajRenderPipe.dll`, type `MajdataPlay.Rendering.MajRenderPipeline`, and its internal constructor `(MajRenderPipelineAsset asset, bool useDynamicBatching, bool useGPUInstancing, bool useSRPBatcher, bool optimizeAttachments, UnityEngine.Shader finalCopyShader)`. It finds the unique store to the private readonly Boolean `_optimizeAttachments` across all methods, requires its immediate predecessors to load `this` and the exact `optimizeAttachments` parameter, and replaces only that argument load with `ldc.i4.0` with no operand. It uses no fixed IL offset. Other constructor parameters, methods, assembly references, assets, the Core/helper patches, and any optional FFmpeg patch remain unchanged.

NativeRenderPass is separate from NativeGraphicsJobs. Selecting `OutputMode=Intermediate` does not turn off this pass. This control does not change graphics jobs, boot configuration, pacing DLLs, or native video bridges.

From this directory, after the synthetic Player has stopped:

```powershell
dotnet build Patcher.csproj -c Release --nologo --configfile NuGet.Config

# Entire candidate and reread stay in memory; no fixture/evidence files are written.
dotnet ./bin/Patcher/net9.0/Patcher.dll --disable-native-render-pass `
  ../.work/MainSynthetic E:/repos/MajdataPlay/MajdataPlay/Build --verify-only

# Apply only when this optional diagnostic control is explicitly wanted and the Player is stopped.
dotnet ./bin/Patcher/net9.0/Patcher.dll --disable-native-render-pass `
  ../.work/MainSynthetic E:/repos/MajdataPlay/MajdataPlay/Build
```

Both forms reject a running synthetic fixture before reading its manifests or DLL contents. They require the original repository Build, the existing exact single-method Core patch and its source hash, ordinary source/target/evidence paths without reparse points, and a target render pipeline DLL byte-identical to the source. Strong names, an already patched load, or an unexpected constructor/field/store are rejected. Source and target SHA256 are checked before and after reading and again before application. Existing Core/helper/FFmpeg DLLs and Core/fixture evidence hashes are preserved and rechecked.

Cecil rereads the candidate and the existing full-method snapshot comparison must report exactly the permitted constructor change. An additional opcode/operand comparison requires exactly one changed instruction, with branch/switch operands normalized to instruction indices so the shorter opcode cannot create false differences. Local initialization/types, exception-handler boundaries, maximum stack size, instruction count, and the complete assembly-reference sequence must match. The JSON output records the candidate SHA256, unchanged source SHA256, changed method and instruction index, both IL listings, and verification evidence.

Application retains unique `MajdataPlay.MajRenderPipe.original-*.dll` and `MajdataPlay.MajRenderPipe.no-native-render-pass-*.dll` files under `patch-evidence`, plus a new `disable-native-render-pass-*.json` manifest. Existing evidence is never overwritten. `--verify-only` does not create any of these files or directories. Neither form starts a Player; offline validation does not establish whether this control prevents a Vulkan driver reset.

## Runtime behavior

`Helper.Install(MonoBehaviour)` reflects the existing BGManager fields once. It closes and disables that component's FFmpeg player before assigning the helper texture. It hides the cover, enables the existing RawImage, retains its material and UVs, and reproduces the original `(1, 480/854, 1)` local scale. It does not reference `MajdataPlay.Core` or `MajdataPlay.FFmpeg`; its only Unity references are the **actual Player** `UnityEngine.CoreModule`, `UnityEngine.UIModule`, and `UnityEngine.UI` assemblies.

A persistent pinned RGBA32 array backs the readable upload texture with mipmaps disabled and `linear: false`, clamp wrapping, and bilinear filtering. An eight-row stripe changes before each full upload. `Canvas.willRenderCanvases` checks `Time.frameCount` so multiple Canvas callbacks cannot upload more than once per Unity frame. Stopwatch deadlines request 60 uploads/s and skip missed slots. Each upload calls `LoadRawTextureData(IntPtr, size)` followed by `Apply(false, false)`.

### Optional display/upload decoupling

The helper reads `MAJ_SYNTHETIC_DECOUPLE` once during installation. Only the value `1` enables decoupling. Both modes create **two** identically configured 854 x 480 RGBA32 textures and initialize each exactly once from the same pinned source pixels. The existing upload target remains `s_texture`; the second, `s_displayTexture`, is never updated after initialization. Having both allocations in both modes avoids changing the resource count between the compared conditions.

| Child process environment | Fixed RawImage binding | Later upload target |
| --- | --- | --- |
| Absent or `MAJ_SYNTHETIC_DECOUPLE=0` | `s_texture` | `s_texture` |
| `MAJ_SYNTHETIC_DECOUPLE=1` | `s_displayTexture` | `s_texture`, which is never assigned to a renderer or another consumer by this helper |

The RawImage is bound once during installation, retaining its original material, UVs, and aspect scale. Pixel generation, upload API calls, deadlines, Canvas phase, and requested frequency are otherwise identical. The existing `MAJ_SYNTHETIC_STATIC=1` control remains independent: it suppresses updates after the upload target's initial upload in either binding mode. The display texture's separate initialization is recorded as `displayInitialUploads=1`; it does not increment `uploads`, so the upload target still starts at `uploads=1` for the existing static-frame checks.

Each unique Canvas frame checks only that `RawImage.texture` is the expected CPU texture object. `bindingChecks` and `bindingMismatches` are cumulative from installation and do not reset with reporting windows. These values are **not GPU sampling counts** and do not establish that an unused texture's `Apply` produced or completed the same GPU copy work as a displayed texture. A clean decoupled condition would support involvement of displayed-resource updates or their interaction with other rendering work; it would not prove a race, a synchronization defect, or a particular responsible layer. This control has no texture ring, fence, shader-format change, or GPU readback.

Cleanup unsubscribes first, clears the RawImage only if it references either owned texture, destroys each texture separately, and then frees the pin. No Player, GPU, or Unity execution is part of compiling this optional control; installation and paired runtime validation are separate steps.

Logs use `[SyntheticVideoProbe]`:

- `INSTALL width=854 height=480 uploadHz=60` confirms the patched entry ran and names the scene/material/backend. Its existing `static` field is followed by `decoupled`, `displayInitialUploads=1`, and `uploadTextureDisplayed`, which reports the CPU object binding at installation.
- `PROGRESS uploadHz=60 fps=... uploadsPerSecond=... uploads=... uploadCpuMs=...` appears after the first elapsed second and then about every ten seconds. `uploadHz=60` is the requested target; `uploadsPerSecond` is the measured upload rate. `fps` counts unique frames observed by the Canvas callback, not GPU presents. Upload CPU time is not GPU execution time. The appended `static` and `decoupled` fields identify the controls; `bindingChecks` and `bindingMismatches` are cumulative CPU binding checks, while `fps`, `uploadsPerSecond`, and `uploadCpuMs` describe the reporting window.
- `CLEANUP uploads=...` confirms callback detachment and owned-resource release.
- `FAIL ...` records a helper callback exception; such a case is invalid even if the game later reaches Result.

The stripped Player has no public SceneManager.sceneUnloaded accessors or SystemInfo.renderingThreadingMode property. The helper does not access or alter Unity's private static event fields. It detects destroyed manager/RawImage objects at the beginning of the next Canvas callback, unsubscribes first, clears its own texture reference, destroys its texture, and frees its pin. A retained Application.quitting callback provides an additional cleanup path. Immediate cleanup at the scene-unload instant, or cleanup when no subsequent Canvas callback occurs and the process is force-terminated, has not been verified. Use the normal Player engine log to confirm actual graphics threading mode.

## Optional retired-readback acknowledgement gate

Set `MAJ_SYNTHETIC_READBACK_GATE=1` only in a diagnostic child to dispatch `Helper.Install` to `ReadbackGate.Install`. Normal installations keep their existing behavior and log format. This experiment uses three identically configured 854 x 480 RGBA32 readable textures, one persistent pinned CPU buffer, and the original RawImage/material/aspect. It closes/disables that background's original video component. It rejects overlapping installations while a previous pool is still draining, and it does not alter a running fixture or launch a Player during compilation.

During installation the helper resolves `image.canvas.rootCanvas.worldCamera` through public members that are present in the actual stripped Player. It rejects an overlay/root camera without an explicit rendering camera, and requires actual `SystemInfo.supportsAsyncGPUReadback`. Every unique Canvas frame consumes only its own slots' atomic acknowledgements. A nominal 60 Hz deadline may select only a noncurrent `Available` slot. After uploading it, the previous displayed slot becomes `RetiredAwaitMarker`; a request is never issued for the current displayed slot. If no slot is available, that upload is skipped and counted as `backpressure`, preserving the current image instead of overwriting storage.

Only `RenderPipelineManager.endContextRendering` callbacks whose camera list contains the cached root Canvas camera may record retired markers, at most once per Unity frame. Current MRP source submits each camera before its end-context event; the reused CommandBuffer then records the actual Player's public `RequestAsyncReadback(Texture, mip, x, width, y, height, z, depth, callback)` for a **1 x 1** region of each retired texture and executes that buffer. Each slot has one cached callback and at most one outstanding request. The callback reads `done`/`hasError`, atomically publishes its status/timestamp, and performs no Unity-object access, resource cleanup, or logging. The main-thread event path returns a slot to `Available` only after consuming success. Submission uncertainty or readback error quarantines the affected slots and fails the experiment; it does not silently infer completion or reuse them.

This is **retired-region readback acknowledgement feedback**, not a last-use fence, a GPU-idle guarantee, or a proven race fix. A 1 x 1 completed request does not, by public contract alone, prove that all earlier sampling reads of the whole texture or all pipeline stages/queues have finished. CPU camera matching and the inspected MRP submission order support the intended ordering but do not replace a GPU command trace. There is no `GraphicsFence.passed`, private Unity API/event access, fixed-frame-delay safety claim, GPU `GetData`, or deliberate unguarded overwrite baseline. The extra textures, requests and backpressure differ from the original single-texture baseline, so a clean result cannot be described as a strict one-variable proof.

`INSTALL` retains the `[SyntheticVideoProbe] INSTALL width=854 height=480 uploadHz=60` prefix and appends `static=False`, `decoupled=False`, `readbackGate=True`, `poolSize=3`, `readbackSupported=True`, `region=1x1`, `lastUseNotProven=True`, the initial-upload count and cached camera name. `PROGRESS` occurs after one elapsed second and then every ten seconds. Stable counter keys include `uploads`, `uploadsPerSecond`, `initUploads`, `displaySwitches`, `readbackRequests`, `readbackCompleted`, `readbackErrors`, `reuseAfterAck`, `illegalReuse`, `unexpectedCurrentMarker`, `bindingChecks`, `bindingMismatches`, `backpressure`, `contextFrames`, `contextCameraMatched`, `pending`, `pendingPeak`, and observed mean/max callback latency. Counts are cumulative except the window's frame rate, upload rate and CPU upload mean. The requested 60 Hz is not an achieved-rate assertion; CPU binding checks are not GPU sampling counts.

Closing detaches Canvas uploads, clears any owned RawImage binding, retires the current slot, and leaves end-context/quit subscriptions attached to drain already exposed storage. No new pool may be installed until the old pool releases. Unity textures, the command buffer and the pin are released only by a main-thread path once all slots are `Available` (or were never exposed during failed initialization) and no retired/pending/quarantined storage remains. `CLEANUP_COMPLETE` reports that acknowledgement drain, not a proven last-use GPU fence. `CLEANUP_PENDING pending=... retired=... quarantined=...` means up to three textures, the buffer and pin are retained. If the cached camera is destroyed, no later matching context exists, the device hangs, or the runner terminates the process before callbacks drain, bounded storage may remain until process exit. Quitting never blocks for GPU completion. Do not claim complete reclamation without `CLEANUP_COMPLETE`.

The gate is compiled with the same SDK 9.0.200 command and actual Mono/Unity references as the other helper modes. Building and metadata verification establish compatibility only; runtime support, matching contexts, successful requests/acknowledgements, nonzero `reuseAfterAck`, zero error/illegal/binding-mismatch counters, actual throughput, and Game-to-Result outcome must be verified separately by the lead's runner.

## Optional native Unity graphics-queue fence gate

Set `MAJ_SYNTHETIC_NATIVE_FENCE=1` only in a diagnostic child to select the native backend of the **same** `ReadbackGate.Session` ownership state machine. It is mutually exclusive with `MAJ_SYNTHETIC_READBACK_GATE=1`; selecting both throws before installing either backend. The ordinary static/decoupled helper paths are unchanged. This backend does not require asynchronous readback support and creates no cached readback callback per slot.

The separate diagnostic DLL uses the existing Player preload name `MajdataPlayVulkanPacing`, but it is **not the original pacing implementation**. Its queue-marker ABI is Cdecl: `mf_abi_version` must return 1, `mf_status` must return 0, `mf_render_callback` must be nonzero, and `mf_event_id` supplies the reserved Unity event. `mf_ticket_create` returns one owned opaque pointer-sized token; the managed code never dereferences it. `mf_ticket_poll` returns 0 pending, 1 after native `vkGetFenceStatus` success, or a negative error. `mf_ticket_release` returns exactly 1 only after safe successful release, 0 when refused, or a negative native error, including device loss. Only an exact 1 permits reuse; owner retirement alone is not fence success. No native export is called by offline build or metadata checks. A missing, wrong, or uninitialized DLL is a runtime initialization failure, not a validated fallback or successful experiment.

Each of the three slots owns at most one ticket. Only a retired, noncurrent slot may create one. The matching root-Canvas camera's end-context path records `CommandBuffer.IssuePluginEventAndData(callback, eventId, ticket)` and executes the command buffer; managed main-thread code does not submit Vulkan work directly. The diagnostic native render callback uses Unity's `AccessQueue(flush=true)` to submit an empty graphics-queue batch with a real Vulkan fence. A slot remains `PendingAck` until its own poll returns exactly 1 **and** release returns exactly 1. Only then is the token cleared and the shared state machine returns the slot to `Available`. Pending polls retain storage; negative polls, release refusal, uncertain submission, and API exceptions quarantine the slot and fail the experiment. No stale readback callback acknowledgement can admit a native slot. Pool exhaustion skips an upload and preserves the current displayed texture, with no additional pool/session allocation or blocking wait.

Native logs keep the `[SyntheticVideoProbe]` prefix but identify `nativeQueueFence=True`, `nativeMarkerAvailable=True`, `poolSize=3`, and `markerScope=UnityGraphicsQueue`. Progress uses `markerRequests`, `markerCompleted`, and `markerErrors`, plus `reuseAfterAck`, `illegalReuse`, `unexpectedCurrentMarker`, `bindingMismatches`, `backpressure`, `contextCameraMatched`, `pending`, and `pendingPeak`; it does **not** relabel native submissions as successful readbacks. `markerPollLatencyMs` and its maximum are CPU request-to-poll-observation times, not GPU timestamps. Readback mode retains its existing `readbackGate` keys and region-acknowledgement limits.

Cleanup uses the same bounded retirement path. A native ticket is never discarded or used to justify texture destruction while release is refused or completion is uncertain; up to three quarantined/pending textures and tickets remain retained, including across scene changes or device-generation failure. No new session is admitted while the old session remains. The native component owns deferred callback and device-generation safety. Successful polling observes a real Vulkan fence completed on the Unity graphics queue; unlike retired-region readback acknowledgement, it is not merely a region-completion report. Whether that marker covers the actual last draw using the earlier binding still depends on Unity's binding snapshots and command ordering and requires a command trace. It is not a promise that other queues, capture layers, all GPU activity, or driver shutdown are idle, and does not establish a race fix merely because a fence exists.

Compile with the existing actual-Player Mono references and unchanged assembly identity. Do not install or run as part of building. Runtime validation must separately confirm the diagnostic DLL identity, ABI/status, nonzero marker/request/completion/reuse counts, zero marker/state/binding errors, actual throughput, and complete-game or failure outcome.

## Controlled validation and limits

Use the main-scene runner's synthetic frame-source mode for a controlled GPU run. Require INSTALL, sustained progress near the intended upload rate, actual Game-to-Result completion, and no helper FAIL / device-lost / driver reset. The original target H.264 854 x 480 decoder-open and native-transfer evidence should be absent; global VP8 background decoding may still occur. The runner must not require original H.264 or Hardware Prepared logs for this synthetic mode.

`CurrentSec` remains zero, so the original video timing error text differs. Source and actual entry inspection establish that the game timeline is timer/audio based and completion depends on chart/audio completion; video timing error is diagnostic. Existing video control methods are not patched and can still call the closed player, though PlayVideo returns when IsPrepared is false.

Canvas-time uploads occur later than the original FFmpeg player Update, and synthetic stripe generation has different CPU work and pixels. This fixture preserves the main rendering structure but is not identical in every scheduling detail. A clean result cannot prove the original video route safe or establish OBS as a cause. Record backend, real frame-loop rate, loaded capture modules, and source hashes for each controlled comparison.
