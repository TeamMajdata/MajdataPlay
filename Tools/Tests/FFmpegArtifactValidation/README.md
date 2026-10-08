# FFmpeg native artifact audit regression tests

Run from the repository root with Python 3 (standard library only):

```powershell
python -m unittest discover -s Tools/Tests/FFmpegArtifactValidation -v
python Tools/FFmpeg/verify-artifacts.py
# Audit only selected installed targets; do not load any library:
python Tools/FFmpeg/verify-artifacts.py --targets win-x64,win-arm64 --skip-host-load
```

Tests use temporary synthetic PE headers, manifests and Unity importers. They
cover Windows x86/x64/ARM64 machine and pointer width, emulated host selection,
locked FFmpeg revision/ABI and seven-library inventory, platform/CPU import
settings, paired asset/folder metadata, bridge Preload, Android 16 KiB load
alignment/congruence, ARM64-only dependency preparation, and stale bridge source detection with LF/CRLF compatibility. Tests and
platform-irrelevant bridge sources do not force a production library rebuild.

The real artifact audit checks SHA256 and sizes, source and dependency pins,
licenses, production bridge source fingerprints, native machine/Apple SDK,
Android 16 KiB load alignment and importer settings. On a matching interpreter
architecture it also loads the host libraries and compares their full versions
with the binding headers, checks decoder/encoder exports, and queries bridge ABI.
Use `--skip-host-load` for static-only validation. This is not a substitute for
Unity Player, GPU, device or foreign-architecture runtime tests.

Simulator staging remains under the ignored `FFMPEG_BUILD_ROOT/artifacts` path
(default `Tools/FFmpeg/.build/artifacts`); it is not installed in the main Unity
project. Explicit `--targets` filters must find every requested FFmpeg target and its bridge.
The default `all` audit also requires every production architecture, including
Windows ARM64; simulator staging is optional until explicitly requested.

## Unity importer integration

After installing all three Windows targets, validate their real importers with
exactly Unity 6000.3.17f1:

```powershell
./Tools/Tests/FFmpegArtifactValidation/run-unity-importers.ps1
```

This copies only the installed Windows native assets into the ignored `.work`
fixture, imports them using the real Editor and checks all 24 Player/CPU/Editor
selections. It does not edit the main project's assets or Player Settings. ARM64
and x86 plugins must not be enabled for the x64 Editor. Reports and logs remain
in `.work/unity-importers`. A successful importer check does not prove a Windows
ARM64 Player build or execution on an ARM64 device.

## Windows integration results — 2026-10-08

Using the installed x64 FFmpeg libraries and rebuilt bridge:

- `dotnet run --project Tools/Tests/FFmpegValidation/FFmpegValidation.csproj --
  Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows/x86_64
  "Assets/StreamingAssets/MaiCharts/Original/Zunda Overdance/bg.mp4"`:
  **PASS, 773 assertions**, including real H.264/D3D11VA CPU transport, decode,
  seek, cancellation, bounded frame queues and clocks. Existing nullable/ref
  escape compiler warnings were reported by the independent test project.
  Evidence: ignored `.work/managed-native-x64.txt`.
- `run-unity.ps1 -Platform Windows -Backend Mono -Architecture x64 -Graphics
  d3d11 -RequireHardware -WorkDirectory
  Tools/Tests/FFmpegArtifactValidation/.work/windows-player` with Unity
  **6000.3.17f1**: **PASS, 39 assertions, 88 frames**, D3D11 GPU conversion with
  no CPU readback or software fallback, Mono High stripping. Evidence:
  `.work/windows-player/x64-Mono/build.txt`, `d3d11-hardware.txt` and logs.

- The same newly built Player with `-Graphics d3d12 -RequireHardware -SkipBuild`:
  **PASS, 40 assertions, 77 frames**, D3D12 GPU conversion and shared-resource copy,
  no CPU readback or software fallback; `.work/windows-player/x64-Mono/d3d12-hardware.txt`.
- The same Player with `-Graphics vulkan -RequireHardware -RegularPlayer -SkipBuild`:
  **PASS, 41 assertions, 88 frames**, Vulkan GPU conversion and shared-resource copy,
  no CPU readback or software fallback; `.work/windows-player/x64-Mono/vulkan-hardware-regular.txt`.
  The first Vulkan **batchmode** attempt crashed inside UnityPlayer immediately
  after startup's `UnloadTime`, before producing any test report. It is **not**
  counted as PASS; the evidence is preserved in `vulkan-hardware-first-crash.log`.
  Passing the normal-render-loop run does not establish batchmode/startup stability.

These x64 results do not validate Windows ARM64 execution, native Vulkan Video
or D3D12VA decoding on this RX 580, every graphics API, startup stability or the
main Unity project. Other platform audit results and limitations are in
`Tools/FFmpeg/APPLE-RESULTS.md`, `LINUX-AUDIT-20261008.md`,
`ANDROID-AUDIT-20261008.md` and `BUILD-RESULTS.md`.
