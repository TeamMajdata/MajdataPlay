# Bounded Vulkan startup capture

`capture-vulkan.ps1` prepares or supervises one GFXReconstruct capture from process startup. It reuses the existing `run-main.ps1` for owner-checked navigation, application evidence and driver-event observation. It never launches Replay, Unity Editor, a replacement server, WPR, a dump tool, or an ETL conversion. No existing capture or other evidence is deleted.

The script defaults to **plan only**. The initial revision (SHA256 `9DC257887BB12189911578710B3C1A70EAC47338538CFE506C15F27ECC22ADE7`) was subsequently run by the lead, not by the preparation agent. It produced a readable startup capture with confirmed layer/PID evidence. This revision adds a volume-free-space consumption guard after that run exposed stale active-writer directory lengths. The new guard has only syntax/plan validation here; its runtime stop boundary has not been exercised. Replay remains a separate lead-controlled GPU experiment and is never invoked by this script.

## Known local tools

The inspected existing installation is `E:/_Game/majp-test/vkdiag/sdk/Bin`:

| File | Inspected role |
| --- | --- |
| `VkLayer_gfxreconstruct.json` | Declares `VK_LAYER_LUNARG_gfxreconstruct`, version 1.0.5, Vulkan API 1.4.363. |
| `VkLayer_gfxreconstruct.dll` | The layer library named by that manifest. |
| `gfxrecon-info.exe` | CPU-only capture metadata/statistics reader; `--help` inspected. |
| `gfxrecon-convert.exe` | CPU-only conversion; `--help` confirms `--format jsonl --frame-range`. Not automatically invoked. |
| `gfxrecon-replay.exe` | GPU replay tool. Presence was inspected, but it is never run by this script or during preparation. |

These are preexisting files; there is no installation, download or registry change. The local vendor `gfxrecon-capture-vulkan.py` lines 279–289 confirm the environment-variable mapping. The layer manifest describes an empty `capture_frames` value as **all frames** and `capture_file_flush` as flushing after each written packet.

## Baseline and safe preparation

From the repository root:

```powershell
# Plan only: inspect files, hash the exact baseline and write a new ignored output directory.
./Tools/Tests/Modules/VulkanTextureValidation/capture-vulkan.ps1

# This separate command runs GPU work. Do not run it merely to inspect or syntax-check the script.
./Tools/Tests/Modules/VulkanTextureValidation/capture-vulkan.ps1 -Run `
  -OutputDirectory ./Tools/Tests/Modules/VulkanTextureValidation/.work/capture-case-01
```

A directory must be new and under this module's `.work`; it is never overwritten. The accepted baseline is the prepared `MainSynthetic` copy, with exact recorded Core, FFmpeg preparation-suppression, NativeRenderPass control, helper and native-marker hashes. Original `Build`, `Assets`, settings, DLLs and Windows registry are not changed by this script. The application itself may create its normal local data in the isolated copy when a future run is requested; the existing runner records settings/artifact hashes.

Default capture mode is **ordinary sampled synthetic pixels**, with no static/decoupled/readback/native-fence schedule. OBS Vulkan capture is disabled for this child. The current native DLL under `MajdataPlayVulkanPacing.dll` is the diagnostic `mf_*` queue marker (SHA256 `A8827B4468A2A4A1CEA534D362406016BCE0D0FE14C4A71131D402751961BCDF`), not the original pacing implementation. Its loader initialization remains, but ordinary mode does not issue marker events. `-NativeQueueFence` optionally captures the previously tested marker schedule instead and is explicitly recorded as a different baseline.

The helper expected by this revision is SHA256 `269781F9A748152003F07B267C379CBA002CE7A51DE715AD9429D555FDEB5313`. Hash mismatch is a preflight failure, not permission to copy or patch anything. Review and update the capture baseline deliberately if diagnostic binaries change.

## Capture environment

The environment is set only on a newly launched console PowerShell wrapper through `ProcessStartInfo.EnvironmentVariables`. The parent environment is unchanged. The wrapper invokes the existing runner once and the Player inherits those values. Inherited `GFXRECON_*` values and selected Vulkan loader overrides are removed only in that child before the following values are applied:

```text
VK_LAYER_PATH=<inspected SDK Bin>
VK_INSTANCE_LAYERS=VK_LAYER_LUNARG_gfxreconstruct
VK_LAYER_SETTINGS_PATH=<new output>/layer-settings
VK_LOADER_DEBUG=error,warn,layer
GFXRECON_CAPTURE_FILE=<new output>/capture/startup.gfxr
GFXRECON_CAPTURE_FILE_TIMESTAMP=false
GFXRECON_CAPTURE_COMPRESSION_TYPE=LZ4
GFXRECON_CAPTURE_FILE_FLUSH=true
GFXRECON_MEMORY_TRACKING_MODE=page_guard
GFXRECON_PAGE_GUARD_EXTERNAL_MEMORY=false
GFXRECON_CAPTURE_PROCESS_NAME=MajdataPlay.exe
GFXRECON_LOG_LEVEL=info
GFXRECON_LOG_FILE=<new output>/capture-layer.log
GFXRECON_LOG_FILE_FLUSH_AFTER_WRITE=true
GFXRECON_LOG_OUTPUT_TO_CONSOLE=true
```

In particular, `GFXRECON_DISABLE`, `GFXRECON_CAPTURE_FRAMES`, `GFXRECON_CAPTURE_TRIGGER` and `GFXRECON_CAPTURE_TRIGGER_FRAMES` are absent. The child layer-settings search path points to a newly created, intentionally empty `vk_layer_settings.txt`; no existing vkconfig or application setting file is edited. There is no trimmed frame window or late hotkey trigger. Capture starts with Vulkan initialization. `GFXRECON_CAPTURE_ENVIRONMENT` is cleared rather than collecting the user's whole environment. The page-guard external-host-memory option is explicitly false so this control does not deliberately add that external-memory route.

LZ4 and flush-on-packet are documented local settings, but neither makes capture timing transparent. Page guards allocate shadow memory; the layer, logging, compression and flush operations can alter scheduling, CPU/memory load, driver behavior and fault probability. A clean or early capped capture cannot establish a fix or disprove a previously observed fault.

## Budgets and process ownership

Defaults:

- `-MaxOutputGB 6`: soft budget checked against both **visible lengths of all new output files** and `max(0, InitialFreeBytes - currentVolumeFreeBytes)`.
- The volume difference is a conservative secondary signal when an active capture writer has stale directory-entry length. It is **not capture/process-attributable bytes**: other applications writing to the same volume may cause an early false-positive stop; other applications deleting or releasing space can mask capture growth. The peak records the maximum observed net decrease, not cumulative bytes written.
- `-ReserveGB 30`: independently stop when free space falls below 30 GiB.
- Preflight requires output budget + reserve + 1 GiB polling allowance.
- `-SecondsInGame 200`: passed to the existing runner; permitted maximum 240.
- `-MaxTotalSeconds 360`: supervisor lifetime, including at most the runner's 120-second navigation allowance. It never allows an unbounded navigation/capture loop.
- Poll interval 250 ms. A write burst or in-flight buffered data **can overshoot** a soft cap. Neither the directory measurement nor the volume proxy is a filesystem quota or a guaranteed hard 6-GiB maximum.

The first controlled capture (original supervisor SHA256 above) exposed `startup.gfxr` reporting length zero while its writer remained open and volume free space had already fallen. A read-only `FileStream` length query was sharing-denied by that writer; this was expected file sharing, not an ACL problem. No alternate open method or permission repair is used. The lead additionally watched volume free-space loss during that first run, which finished with a 1,853,570,378-byte capture and 18,925 untrimmed frames; final observed net volume loss was approximately 1.727 GiB, below the 6-GiB budget. These facts do **not** validate the original directory-length check as a hard live-write cap. Existing first-case plans, results and capture files are preserved; its detailed audit note is maintained separately by the lead.

Future runs stop with `output-budget`, `volume-consumption-budget`, or `free-space-reserve` according to the first observed condition. Plans identify `SupervisorSHA256`, `BudgetSources`, `DirectoryLengthsMayBeStale`, `DiskConsumptionScope` and `InitialFreeBytes`; results preserve them and add `FinalObservedFreeBytes`, `FinalObservedDiskConsumptionBytes` and `PeakObservedDiskConsumptionBytes`. The final free-space sample includes the short post-capture CPU info phase; it is not an application I/O counter.

Only one wrapper is started with `-Attempts 1`. Its two output streams are drained asynchronously to files, without waiting for process exit to obtain `MAIN START attempt=1 pid=...`. The supervisor discovers only a **direct CIM child** of its own wrapper, with exact isolated executable path and creation time after the recorded start. The stdout PID must match that child. Failure to establish both within 15 seconds produces `unverified-child` and stops the wrapper early; it does not keep recording until the full budget is spent. If an alternate launch broker breaks the direct-parent relationship, do not weaken ownership checks: add an explicit child-identity handoff in the runner after review.

On a budget/timeout/invalid-layer stop, the supervisor first terminates **its own wrapper instance**, preventing it from starting another game. It then rechecks the recorded Player's PID, parent PID, creation time, executable path and process start time before terminating that exact instance. There is no `Get-Process MajdataPlay | Stop-Process`, global process-name kill, or arbitrary command-line/window inspection. If identity cannot be proved, cleanup is refused and recorded; an unverified game is not guessed at or killed. In that exceptional case, the lead must reconcile the known launch evidence manually or improve the runner handoff before another run.

Forced termination can truncate the final packet despite flush-on-write. The script does not promise a graceful capture footer or wait indefinitely for a hung driver. Existing runner driver-stop behavior is retained: the supervised process may be deliberately terminated after a driver event; its exit code does not prove natural application crash.

## Evidence and validity

Output retains:

- `capture-plan.json`: exact application/diagnostic/settings/boot hashes, capture environment, removed keys, tool/layer hashes, budget and preflight desktop state.
- `wrapper-launch.json`, `owned-child.json`, `runner-stdout.txt`, `runner-stderr.txt`.
- `capture-modules.csv`: exact capture-layer DLL path observed in this Player. Merely setting environment values is insufficient.
- `run/attempt-1/`: the existing runner's actual helper/backend/threading logs, settings/artifact hashes, display state in helper INSTALL, driver-event XML and result.
- `capture/*.gfxr`, layer log and a separate supervisor `driver-events.xml`.
- Per-capture `.info.txt`/`.info-stderr.txt` from a **CPU-only** `gfxrecon-info` process, limited to 60 seconds per file.
- `capture-result.json`: stop reason, PID proof, observed sizes/free reserve, layer proof, untrimmed info, case validity and explicit `ReplayPerformed=false`.

A valid recorded game requires module proof, a produced nonempty capture whose info reader succeeds with positive frame count and no `trimmed frame range`, and matching actual frame-source/backend evidence from a valid runner outcome. Budget/reserve/timeout/invalid-child/layer failures remain truncation or invalid outcomes. Navigation failure, early return to List and missing Game evidence are never treated as clean GPU tests. A readable startup capture is not by itself evidence that it contains the precise failing GPU work; correlate its frame/time range with the original driver XML and helper logs.

`gfxrecon-info` reports device/memory/pipeline/frame metadata; it is not a complete GPU timeline. Subsequent conversion should target a narrow frame range and JSONL, for example only after an exact range has been established:

```powershell
# CPU conversion only; choose a new output file and a bounded, justified frame range.
# This is not automatically executed by capture-vulkan.ps1.
& 'E:/_Game/majp-test/vkdiag/sdk/Bin/gfxrecon-convert.exe' `
  './capture.gfxr' --format jsonl --frame-range '0-3,1000-1020' --output './selected-events.jsonl'
```

Do not dump the full stream as unrestricted JSON/CSV or convert a multi-GiB ETL wholesale. Replay is a separate GPU experiment requiring the lead's decision. A last captured API without a return value identifies where observation ended, not automatically the original GPU fault cause.

## Why the old capture is insufficient

The inspected `gc3-info.txt` reports **5401 frames, trimmed range 1200–6600**, with a state load at frame 1200. The old `soak-gfxr*.ps1` variants set that trim range; they also used process-name termination and deleted prior files, so they are not reused. `gfxr-report.md` has a case with no confirmed capture output. The inspected `replay2.log` and `replay3.log` stop at `vkGetMemoryWin32HandlePropertiesKHR` with `VK_ERROR_INITIALIZATION_FAILED` and unsupported external-object warnings. That does not show successful replay of the failing frame. The present no-video/no-FFmpeg-handle baseline is more suitable for a new startup capture, but replayability and actual layer loading still require new runtime evidence.
