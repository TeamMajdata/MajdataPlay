# Android FFmpeg native audit — 2026-10-08

## Scope and outcome

Audited `Assets/Plugins/MajdataPlay/FFmpeg/Native/Android/arm64-v8a` and
`Assets/Plugins/MajdataPlay/FFmpeg/Native/Android/armeabi-v7a` in the shared workspace.
Only the two `libFFmpegUnityBridge.so` files and their `bridge-manifest.json` files
were replaced. This report is the only added tracked file from this task.

Subsequently authorized fixture follow-ups minimally corrected
`Tools/Tests/FFmpegValidation/AndroidNativeSmoke.c` and the three stale ABI3 lines
in `Tools/FFmpeg/Native/tests/AndroidCapabilitySmoke.cpp`, and updated this report.
Neither fixture follow-up nor the later bounded device run modified Android
native libraries, manifests or `.meta` files.

**The seven FFmpeg libraries in each ABI already match the repository locks and
were not rebuilt or replaced.** Their manifests, licenses, patches and other
assets are unchanged. All 50 existing `.meta` files under the Android directory
remain byte-for-byte identical to the initial SHA256 snapshot, including GUIDs.
No shared build/verifier script, Native source, CMakeLists, other platform asset,
submodule content or submodule pointer was edited by this task.

## Initial mismatch and rebuild decision

The two old bridge manifests had identical non-EOL source differences:

- `CMakeLists.txt`
- `D3D11.cpp`
- `VulkanInterop.cpp`
- `VulkanPortable.cpp`
- `VulkanPortable.h`
- `tests/D3D11Smoke.cpp`

The Android production inputs affected by that list are **CMakeLists.txt and
VulkanPortable.cpp/h**. The Windows-only files and the test fixture are not reasons
to rebuild Android FFmpeg. Another 37 old raw source hashes differ only because
Windows CRLF and Linux LF encode the same source differently; those differences
were explicitly accepted rather than reported as stale production code.

The enhanced repository verifier independently confirmed the pre-rebuild state:
all 14 FFmpeg libraries passed, then the Android bridge source check rejected the
old `CMakeLists.txt` fingerprint. After replacement the same command passed.

## FFmpeg version and locked-header ABI evidence

Fixed source: official `n9.0.1`, commit
`bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa`.
The initialized FFmpeg.AutoGen submodule was read, not updated.
For **each ABI**, 143 non-generated public headers in the existing development
prefix matched both the locked FFmpeg source and the managed-binding header
snapshot after CRLF normalization. Generated `avconfig.h` and `ffversion.h` were
excluded from this public-header comparison. The prefix's avconfig declares
little-endian operation and fast unaligned access.

All seven exported version functions were inspected in the ELF disassembly and
compared with the exact major/minor/micro header macros, not merely SONAMEs:

| Library | Exact version, both ABIs |
| --- | --- |
| avcodec | 63.1.101 |
| avdevice | 63.1.101 |
| avfilter | 12.1.101 |
| avformat | 63.1.101 |
| avutil | 61.1.101 |
| swresample | 7.1.101 |
| swscale | 10.1.101 |

Every FFmpeg binary's size and SHA256 match its original build manifest.
The relevant dav1d, Meson, Vulkan-Headers and software-encoder dependency records
match the current dependency lock. The repository verifier also passed the four
pinned static PIC software encoders, checked patches, GPLv3 and dependency notices.
None of these existing FFmpeg files was rewritten.

## Architecture, API, layout, dependencies and importer checks

Checked all eight libraries per ABI, including the newly built bridge:

| Check | ARM64 | ARMv7 |
| --- | --- | --- |
| ELF class / machine | ELF64 / AArch64 (183) | ELF32 / ARM (40) |
| Android ident note | API 23, r27c, build 12479018 | API 23, r27c, build 12479018 |
| Procedure-call ABI | AArch64 NDK ABI | EABI5, no hard-float ABI flag |
| Every PT_LOAD alignment | 16384 bytes | 16384 bytes |
| PT_LOAD offset/address congruence | PASS | PASS |
| DT_TEXTREL | absent | absent |
| SONAME | exact unversioned Android filename | exact unversioned Android filename |
| Unity Android CPU | ARM64 | ARMv7 |
| Unity Editor / Any platform | disabled | disabled |
| Unity Android platform | enabled | enabled |

No library has a RUNPATH/RPATH entry. The FFmpeg dependency closure uses only the
other delivered FFmpeg libraries and NDK API23 system libraries: `libc.so`,
`libm.so`, `libdl.so`, `libandroid.so` and `libmediandk.so`. The new bridge directly
needs `libavutil.so`, `libavcodec.so`, `libdl.so`, `libm.so` and `libc.so`.
There is no dynamic `libc++_shared.so` or private build-path dependency.

As an additional static API check, every strong undefined dynamic symbol in all
16 libraries resolves against the matching delivered dependency closure and the
appropriate architecture's **NDK API23 system stubs**. This guards against an
unintentional direct import of newer Android APIs; it does not prove a particular
physical device's loader, driver or dynamically looked-up APIs work.

Both bridges export `ffu_abi_version` returning 4. All 27 `ffu_*` C entry points
currently declared by `Bridge.h`, plus `UnityPluginLoad` and `UnityPluginUnload`,
are present. This is static export/ABI evidence, not a managed runtime ABI test.

## Rebuild toolchain and isolation

Used the repository's fixed Unity Editor **6000.3.17f1** Android toolchain:

- NDK: `C:/Program Files/Unity Editors/6000.3.17f1/Editor/Data/PlaybackEngines/AndroidPlayer/NDK`
- NDK revision: **27.2.12479018 / r27c**, Clang **18.0.3**
- CMake: adjacent Android SDK `cmake/3.22.1/bin/cmake.exe`
- Ninja: adjacent Android SDK `cmake/3.22.1/bin/ninja.exe`, version **1.10.2**
- Pinned Vulkan-Headers: SDK **1.4.328.1**, commit
  `19725e4d48082fe78e26622b15d3080ccd54112b`; cached repository HEAD matched and
  its tracked working tree was clean.
- API: **23**; Release, C++17, PIC, static libc++, 16 KiB maximum linker page size.
- Warnings: `-Wall -Wextra -Werror`, with the existing missing-field-initializer
  and deprecated-declaration exclusions; explicit `--driver-mode=g++` protects
  Windows NDK short-path compiler invocation.

All build/test writes were isolated to:
`Tools/FFmpeg/.build/android-audit-20261008`.
The per-target prefixes contain private copies of the already verified FFmpeg
headers and shipped libraries. Shared FFmpeg source, development prefixes and
download/toolchain caches were never configured, patched or rewritten.

WSL Ubuntu-24.04 was detected after approval, but no WSL build or Linux-host NDK
was needed. The first Windows sandbox CMake/Ninja compiler probe stalled; the
read-only process diagnostic also reported access denied. After explicit
`require_escalated` approval, only this task's stalled private CMake process tree
was stopped and the same build was retried outside the sandbox. Both targets
then configured, compiled all five production translation units and linked
successfully. No system software or packages were installed.

Each manifest records the exact CMake invocation. The essential configure flags
used for each private target directory were:

```text
-G Ninja -DCMAKE_BUILD_TYPE=Release
-DCMAKE_TOOLCHAIN_FILE=<fixed Unity NDK>/build/cmake/android.toolchain.cmake
-DANDROID_ABI=arm64-v8a or armeabi-v7a
-DANDROID_PLATFORM=android-23 -DANDROID_STL=c++_static
-DCMAKE_CXX_FLAGS=--driver-mode=g++ -Wall -Wextra -Werror -Wno-missing-field-initializers -Wno-deprecated-declarations
-DFFMPEG_ROOT=<private per-target prefix>
-DAVUTIL_INCLUDE_DIR=<private prefix>/include
-DAVUTIL_LIBRARY=<private prefix>/lib/libavutil.so
-DAVCODEC_LIBRARY=<private prefix>/lib/libavcodec.so
-DFFU_VULKAN_HEADERS=<verified pinned Vulkan-Headers>/include
-DFFU_BUILD_TESTS=OFF -DFFU_BUILD_VULKAN_TESTS=OFF
-DCMAKE_EXPORT_COMPILE_COMMANDS=ON
```

Build command: `cmake --build <private per-target cmake directory> --parallel 8`.
The native CMake target supplies `-Wl,-z,max-page-size=16384` for Android.

## Installed bridge provenance

| ABI | Previous bridge SHA256 | New bridge SHA256 | New bytes |
| --- | --- | --- | --- |
| arm64-v8a | `1ed648d6ba7ba7f05e7e96f2f729f2aca6ddd2927f2b521278d0476e096b74d8` | `8d8e5dbc942feb46c8eca38b993a148856b945d433b4aae2f8b3bdb3135a4ef4` | 2497400 |
| armeabi-v7a | `aa623791fd8c3f0c6e4baa38680384291a0f9ebb2792c732534ee8a955dc9795` | `d8e62b5e35276064f168fe608835f96b0ca99ad29d59ba816d067e5e15490d78` | 2170484 |

Existing bridge GUIDs remain:

- ARM64: `d7eee23d39ef52848017e4cecfe107fc`
- ARMv7: `4ac0e92108ed5065a7ec7ead8b512daa`

Both new bridge manifests contain **51 `sourceSha256` entries and 51
`sourceSha256Lf` entries**, with the latter hashing source after CRLF-to-LF
normalization. Current Android production fingerprints match. The normalized
field was added for the new build's provenance; no existing matched FFmpeg
manifest was changed merely to normalize line endings.

## Validation commands and retained evidence

The following commands were run from the repository root:

```powershell
python Tools/FFmpeg/.build/android-audit-20261008/android-audit.py build
python Tools/FFmpeg/.build/android-audit-20261008/android-link-audit.py
python Tools/FFmpeg/.build/android-audit-20261008/android-audit.py install
python Tools/FFmpeg/verify-artifacts.py --targets android-arm64,android-armv7 --skip-host-load
adb devices -l
```

Results:

- Strict fixed-NDK bridge builds: **PASS**, ARM64 and ARMv7.
- Public-header, exact version, artifact SHA/size, architecture, API note,
  dependencies, importer, all PT_LOAD and C export checks: **PASS**.
- Strong dynamic import resolution using API23 NDK stubs: **PASS**, both ABIs.
- Enhanced target-filtered repository verifier: **PASS**, 14 FFmpeg libraries and
  2 bridges; independently repeated and confirmed by the main agent.
- `tests/VulkanPortableLifetime.cpp`: **PASS**, three deterministic regression
  groups on the Windows x64 host with the cached fixed LLVM-MinGW 20260922 toolchain:
  inactive-generation owner/resource retention, completed-fence/device-lost-only
  retirement, and concurrent prepare/shutdown without ABBA deadlock or stale
  packets. This fixture uses modeled callbacks and no GPU/Unity/decoder; it is
  **not Android runtime or hardware validation**. No fixture source was edited.
- ADB checked initially and again after replacement: **no attached devices**.
- `git diff --check`: **PASS**; scoped final diff contains only the intended
  Android bridge artifacts/manifests and this report. Other agents' shared
  workspace changes were observed but not modified.

Ignored local evidence retained under the private work directory:

- `baseline-sha256.json`: all initial Android files; preservation checks allow
  changes only to the four bridge files.
- `header-comparison.json`: 143 compared public headers per ABI.
- `before-audit.json`, `after-audit.json`: versions, ELF/API/dependencies/importers,
  source mismatches, page-alignment and export evidence.
- `api23-import-resolution.json`: import counts and dependency closures.
- `android-arm64-configure.log`, `android-arm64-build.log`,
  `android-armv7-configure.log`, `android-armv7-build.log`.
- Per-target `cmake/compile_commands.json`, built bridges and private prefixes.
- `vulkan-lifetime-host-command.json`, `vulkan-lifetime-host-compile.txt`,
  `vulkan-lifetime-host-result.txt`.
- Per-library version-function disassembly files.

These evidence files are intentionally ignored local build/test outputs, not
new repository artifacts.

## Unverified boundaries and corrected Android fixture

**Latest device coverage:** both ARM64 and ARMv7 now pass real-device native
library loading/ABI/profile checks and both native prerequisite fixtures, as
recorded in the bounded device-validation section below. The earlier empty ADB
checks and compile-only stages are historical, **not a current reason to omit
device validation**.

**Still not verified:** real MediaCodec or Vulkan Video decoding,
AImage/AHardwareBuffer GPU image import/transfer, GPU-fence synchronization under
real playback, 16 KiB device execution/APK zip alignment, Unity Editor
import/compile, and Android Unity Mono/IL2CPP Player lifecycle/rendering. No Unity
Player/APK was built or installed, and no device/global graphics configuration
was changed. Native loader/profile/prerequisite success must not be reported as
these playback/integration results. The tested device uses 4096-byte pages and
reports Vulkan 1.1.128, below the production Vulkan Video path's Vulkan 1.3
requirement; compiled Vulkan Video codec entries are not evidence that this GPU
supports actual Vulkan Video decoding.

### Fixture correction after explicit follow-up authorization

The previously reported stale LGPL assertion in
`Tools/Tests/FFmpegValidation/AndroidNativeSmoke.c` is **now corrected**. The change
is confined to the existing profile assertion and one small capability loop:

- Require `--enable-gpl`, `--enable-version3` and `--disable-nonfree`, matching both
  unchanged production manifests.
- Preserve the existing MPEG4 software encoder and libdav1d decoder checks.
- Since this fixture did not already check the four external software encoders,
  add presence and non-hardware capability assertions for `libx264`, `libx265`,
  `libaom-av1` and `libvpx-vp9`, following `NativeSoftwareEncoderSmoke.c`.
- Leave muxer, MediaCodec, Vulkan Video, bridge ABI, AImage/AHardwareBuffer and
  physical-GPU prerequisite checks unchanged. Preserve the file's CRLF line
  endings and dynamic-loading design; no broader source cleanup was performed.

The corrected fixture was compiled **and linked** for both targets with the
fixed Unity NDK r27c, API23, the verified private FFmpeg prefix headers and the
pinned Vulkan headers, using `-std=gnu11 -Wall -Wextra -Werror` and `-ldl`.
ARMv7 additionally used `-march=armv7-a -mfpu=neon -mfloat-abi=softfp`.
Both commands used `-Wl,-z,max-page-size=16384`; no FFmpeg library was linked.

| Follow-up fixture check | ARM64 | ARMv7 |
| --- | --- | --- |
| Strict C compile and complete executable link | PASS | PASS |
| ELF class / machine | ELF64 / AArch64 | ELF32 / ARM |
| Android note / toolchain | API23 / r27c / 12479018 | API23 / r27c / 12479018 |
| Every PT_LOAD alignment | 16384 bytes | 16384 bytes |
| Dynamic dependencies | libdl.so, libc.so | libdl.so, libc.so |
| DT_TEXTREL | absent | absent |
| Physical-device execution, completed in the subsequent bounded run | PASS, 58 checks | PASS, 58 checks |

Exact invocations, zero exit codes, executable hashes and ELF evidence are in
`android-native-smoke-compile-commands.json` under the existing private audit work
directory. Compiler/linker logs are `android-arm64-native-smoke-compile-link.log`
and `android-armv7-native-smoke-compile-link.log`; binaries are the per-target
`android-native-smoke` executables. All remain ignored local outputs.

The fixture-only follow-up's final `adb devices -l` check listed one connected
device, unlike the original audit's earlier empty checks. That stage was
compile/link-only. The later bounded device task below **uploaded and ran the
corrected fixture for both supported ABIs**; its real-device results supersede
that earlier no-execution status. No APK or Unity Player was involved.

A follow-up SHA256 snapshot confirmed that every existing Android native file,
including all seven FFmpeg libraries per ABI, both bridges/manifests and all
`.meta` files, remains unchanged from the already reviewed audit result. The
fixture correction does not require regenerating native assets or bridge source
fingerprints. Scoped final diff and `git diff --check` passed.


## Latest bounded Android device validation — 2026-10-08

Before uploading, read `get-state`, the CPU ABI properties and Android SDK level.
The selected device was already authorized and online; no authorization or device
configuration was changed.

| Device property | Observed value |
| --- | --- |
| Model | Mi MIX 2S |
| Android | 15 / API35 |
| CPU ABI list | arm64-v8a, armeabi-v7a, armeabi |
| 64-bit ABI | arm64-v8a |
| 32-bit ABIs | armeabi-v7a, armeabi |
| Page size | 4096 bytes |
| GPU reported by both fixtures | Adreno (TM) 630 |
| GPU Vulkan version | 1.1.128 |

Both requested ABIs are supported and API35 exceeds the artifacts' API23 minimum.
Neither ABI was skipped. Both target directories were checked for existing files
or symlinks and were **absent**. Each directory was then created atomically
without `mkdir -p`; an existing directory would have caused a refusal/skip rather
than an overwrite:

- `/data/local/tmp/majdata-ffmpeg-audit-20261008-arm64-v8a`
- `/data/local/tmp/majdata-ffmpeg-audit-20261008-armeabi-v7a`

Uploaded exactly the current seven FFmpeg libraries, the current bridge and two
matching fixture ELFs to each new directory. Every library was first checked
against its production manifest. Device-side `sha256sum` then matched **all ten
uploaded files per ABI** to the current host bytes, including both fixtures.
Only the two test executables received `chmod 700` inside these newly created
directories. The fixtures ran with process-local `LD_LIBRARY_PATH` set to their
own absolute directory and a 90-second timeout. No global search path or system
setting was changed.

Equivalent isolated commands per directory were:

```sh
LD_LIBRARY_PATH=<private-directory> /system/bin/timeout 90 <private-directory>/android-native-smoke <private-directory>
LD_LIBRARY_PATH=<private-directory> /system/bin/timeout 90 <private-directory>/android-capability-smoke
```

### Actual native results

| ABI | Corrected AndroidNativeSmoke | Corrected AndroidCapabilitySmoke |
| --- | --- | --- |
| arm64-v8a | **PASS, 58 checks, exit 0, 64-bit** | **PASS, exit 0, 64-bit** |
| armeabi-v7a | **PASS, 58 checks, exit 0, 32-bit** | **PASS, exit 0, 32-bit** |

Each NativeSmoke invocation successfully checked:

- Loading all seven current FFmpeg libraries and the current bridge with complete
  runtime dependency resolution; `dladdr` confirms the staged avcodec source.
- Exact loaded avcodec/avformat/avutil versions against the fixed public headers,
  the checked source-patch marker and bridge ABI4.
- GPL/version3/nonfree profile assertions, built-in MPEG4 plus the four pinned
  external software encoders, and actual software-backed libdav1d presence.
- Required muxer and MediaCodec decoder entries and the H.264/HEVC/AV1/VP9 compiled
  Vulkan Video backend entries. These are build/capability entries, not bitstream
  decode tests.
- Dynamic API26 AImage/AHardwareBuffer symbols, the expected standalone bridge
  Vulkan Video status 400 without Unity preload, and physical-GPU
  AHB/SYNC_FD/FOREIGN/YCbCr importer prerequisites.

Both capability-fixture invocations independently passed ABI4, dynamic image
symbols, the four MediaCodec decoder entries and those GPU prerequisites. No
fixture failed or timed out. This does **not** create a Java VM, decode actual
MediaCodec/Vulkan Video frames, import a real AHardwareBuffer image, compare
rendered pixels, or exercise Unity graphics-device ownership/lifecycle.

### Additional capability-fixture source correction

`Tools/FFmpeg/Native/tests/AndroidCapabilitySmoke.cpp` was changed only in the
three specifically authorized lines: `abi() != 3` became `abi() != 4`, and the
two ABI3 diagnostics became ABI4. The Android production `Bridge.cpp` branch
returns 4. Existing line endings and every other source byte were preserved.

The corrected C++ fixture compiled and linked for ARM64 and ARMv7 with the same
Unity NDK r27c/API23 using C++17, `-Wall -Wextra -Werror`, static libc++, `-ldl`
and 16 KiB maximum linker page size. ARMv7 uses the existing ARMv7/NEON/softfp
flags. Its system dependencies are only libdl/libm/libc; no extra C++ shared
runtime or FFmpeg link was introduced. The compile and device results are both
**PASS** for both targets.

This file is a test, not a bridge production input. Existing bridge
`sourceSha256`/`sourceSha256Lf` maps remain the provenance of the original build;
the platform-filtered verifier correctly excludes changed test inputs. **No
bridge or FFmpeg library was rebuilt or rewritten because of this test change.**

### Evidence and unchanged state

All new build/test evidence remains in the existing ignored private audit work
root. Exact capability-fixture build invocations and ELF checks are in
`android-capability-smoke-compile-commands.json` and the per-target
`*-capability-smoke-compile-link.log` files.

Bounded device evidence is in its `device-native-20261008/` subdirectory:

- `results.json`: device properties, all file hashes, isolated commands, four zero
  exit codes and complete fixture stdout/stderr.
- `android-arm64-android-native-smoke-device.txt`
- `android-arm64-android-capability-smoke-device.txt`
- `android-armv7-android-native-smoke-device.txt`
- `android-armv7-android-capability-smoke-device.txt`
- Per-target `*-device-sha256.txt`, individual push logs and the host native-file
  SHA256 snapshot.

The two newly created device test directories are retained with these artifacts;
no pre-existing user directory was overwritten or deleted. No APK was installed,
no foreground app was switched, and no device-global configuration was modified.
Host SHA256 snapshots confirm that all Android native libraries, manifests and
`.meta` files stayed unchanged throughout both the fixture correction and device
validation. Platform-filtered artifact verification and final diff checks passed.

**Remaining limits:** the actual native loader/ABI/profile and GPU-prerequisite
checks are now verified on this API35/4-KiB device for both ABIs. Actual decoding,
GPU image transport/synchronization, Unity Editor/Player integration and 16-KiB
hardware execution remain unverified. Vulkan Video playback additionally cannot
be inferred on this device's reported Vulkan 1.1.128 GPU.
