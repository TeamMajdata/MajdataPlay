# Native audio codec validation — 2026-10-17

All 11 build targets use pinned FFmpeg n9.0.1, Opus 1.6.1 and LAME 3.100.
AAC, Opus, MP3 and PCM encoding/decoding are mandatory build capabilities.
The seven FFmpeg libraries are rebuilt for each target; the existing Unity
bridges and their ABI remain unchanged. Existing asset GUIDs are preserved.
Simulator archives remain in ignored `Tools/FFmpeg/.build/artifacts/ios-simulator-*`.

| Target | Native build / architecture / provenance | Audio runtime |
| --- | --- | --- |
| Windows x86 | PASS | PASS, 35 round trips |
| Windows x64 | PASS | PASS, 35 round trips |
| Windows ARM64 | PASS; smoke executable also compiles/links | Not executed: no Windows ARM64 host |
| Linux x64 | PASS | PASS in WSL, 35 round trips, without `LD_LIBRARY_PATH` |
| Android ARM64 | PASS; Android 16 KiB ELF alignment retained | PASS on Mi MIX 2S / API 35, 35 round trips |
| Android ARMv7 | PASS; Android 16 KiB ELF alignment retained | PASS on Mi MIX 2S / API 35, 35 round trips |
| macOS ARM64 | PASS on the SSH-connected Mac | PASS, 35 round trips |
| macOS x64 | PASS; smoke executable also compiles/links | Not executed: this Mac rejects x64 execution (`Bad CPU type in executable`) |
| iOS ARM64 | PASS; merged audio dependencies and smoke executable link | Not executed: no signed device run |
| iOS Simulator ARM64 | PASS; merged audio dependencies | PASS on iOS 27.0 Simulator, 35 round trips |
| iOS Simulator x64 | PASS; merged audio dependencies and smoke executable link | Not executed: `simctl spawn` fails with LaunchdSimError 111 on this ARM64 Mac |

The connected Xiaomi Mi MIX 2S runs Android API 35 and reports both
`arm64-v8a` and `armeabi-v7a` support. Each ABI was compiled using the matching
NDK r27c compiler, deployed with its seven newly built libraries to a fresh
private `/data/local/tmp/majdata-audio-*` directory and executed with only that
directory in `LD_LIBRARY_PATH`. Both runs passed all 35 cases and the five
container output files were pulled back to ignored evidence directories.
No game installation or user data was changed.

## What the native smoke verifies

`Tools/Tests/Modules/FFmpegValidation/AudioCodecSmoke.c` performs 35 one-second,
48 kHz stereo encoder → decoder round trips: AAC; Opus with the built-in and
libopus decoders; MP3 with fixed-point and floating-point decoders; and 30 PCM
variants. Left/right channels carry 440/880 Hz respectively. Checks cover codec
identity, sample rate, channel layout, sample counts/padding, finite samples,
waveform amplitude/energy, channel separation, encoder/decoder drain and stable
EOF. AAC/ADTS, Opus/Ogg, MP3 and PCM/WAV files are also muxed, reopened and
demuxed through EOF. Each successful run reports 3,391,382 assertions.

The 31st PCM entry, legacy Archimedes `pcm_vidc`, is registered but its pinned
upstream encoder produces a distorted waveform. It receives a registration
check only and is explicitly excluded from the 35 passing fidelity cases.
This limitation does not affect the tested integer/float/planar PCM, A-law,
mu-law, DVD, Blu-ray or D-Cinema implementations.

The smoke does not exercise speakers, microphones, Unity audio, audio capture,
hardware audio codecs, all sample rates/profiles or real-time throughput.
`FFmpegVideoPlayer` still skips audio; native audio support does not add audio
playback or a Camera recording audio track to the managed components.

## Reproducible checks

From the repository root:

```powershell
python -B -m unittest discover -s Tools/Tests/Modules/FFmpegBuildValidation -p 'test_*.py'
python -B -m unittest discover -s Tools/Tests/Modules/FFmpegArtifactValidation
python -B Tools/FFmpeg/verify-artifacts.py
python -B Tools/FFmpeg/verify-artifacts.py --targets ios-simulator-arm64,ios-simulator-x64 --skip-host-load
python -B Tools/Tests/Modules/FFmpegBuildValidation/verify_windows_dlls.py --targets win-x86,win-x64,win-arm64
./Tools/Tests/Modules/FFmpegArtifactValidation/run-unity-importers.ps1
```

The offline suites pass **31 + 47 tests**. Windows PE checks cover all 24 DLLs,
required exports, architecture and dependency closure, with no added codec or
dynamic C++ runtime DLLs. The Unity **6000.3.17f1** isolated importer audit passes
all **24 Windows native importers**. Its first invocation produced a passing
report but the Editor crashed during shutdown with exit `0xC0000005`; the
second complete invocation exited successfully. The first run is not counted
as an overall pass. Main-project import, Player playback and GPU integration
are not inferred from the importer audit.

The final artifact audit passes **77 FFmpeg libraries across all 11 targets**,
including both ignored Simulator packages. It checks source/ABI pins, every
staged library's SHA256/size/machine, Apple SDK platform, paired Unity metadata,
audio components/provenance/notices and Android 16 KiB segment alignment.
The matching Windows x64 ABI is loaded during the audit; a separate Linux x64
audit also loads the installed libraries without `LD_LIBRARY_PATH`.

Native smoke compile/link commands and platform deployment details are in
`Tools/Tests/Modules/FFmpegValidation/README.md`. Use the matching build prefix for
headers/import libraries and execute against the newly staged libraries.
The Windows x86/x64, Linux, macOS ARM64, Simulator ARM64 and both Android ABI
runs above each passed all 35 round trips (**245 total**). Test executables, recordings and complete logs are
kept under ignored `.build/audio-evidence` and `.build/audio-apple-evidence`.

## Build details and resolved failures

Audio dependencies are authenticated release archives, built out of tree as
static PIC libraries. Opus retains its stable encoder without experimental
mode, and its optional DRED/OSCE features are disabled. Windows ARM64 uses
portable Opus C code because upstream's MinGW ARM runtime CPU detection and
presume-only NEON CMake modes do not compile with the selected toolchain.
LAME's decoder/frontend/NASM are disabled; FFmpeg supplies MP3 decoding.
Redistribution notices include Opus COPYING and LAME COPYING/README/LICENSE.

The first Android ARM64 device run crashed in `getauxval` before `main`.
Symbolication located an embedded Bionic implementation, reached by the LLVM
atomic constructor. The NDK places static C++ runtime archives next to
`libc.a`/`libm.a`; exposing that directory with `-L` caused FFmpeg's final link
to select static Bionic instead of the compiler's API-specific shared stubs.
The build now copies only the three selected C++/ABI/unwind archives into
private, content-addressed directories and uses those isolated search paths.
Both Android ABIs are rebuilt with that correction. An offline regression
ensures the exposed directories contain no Bionic libc/libm archives.

Apple builds used the installed Xcode iOS 27.1 SDK with iOS minimum 15.0 and
macOS minimum 11.0. Stale dependency files referring to the previous iOS 27.0
SDK and old bridge CMake source locations were handled only in ignored build
caches. Linux's initial Meson attempt rejected a transient clock skew; its
fresh retry passed. These failed attempts are not counted as validation passes.
