# Windows ARM64 FFmpeg build regression tests

Run from the repository root with Python 3.10 or newer:

```powershell
python -B Tools/Tests/FFmpegBuildValidation/test_windows_arm64.py
```

The offline suite imports the production build recipe and uses inert compiler files
and narrow subprocess substitutes. It checks the Windows AArch64 target drivers,
Direct3D/Vulkan selection, absence of NVENC/AMF, dav1d machine file, all four static
software encoder recipes, target-specific C++ runtimes, target strip/import-library tools, PE/ELF export controls, LLVM driver alias/NTFS path
handling, Unity Player-only ARM64 import metadata, GUID/byte preservation and complete new-target folder/record metadata, and raw/LF
bridge source hashes including `.def` link inputs. It does not compile or download
dependencies, load an ARM64 DLL, or demonstrate runtime decoding/GPU integration.

Verify real staged outputs separately with:

```powershell
python Tools/FFmpeg/verify-artifacts.py --targets win-x86,win-x64,win-arm64 --skip-host-load
```

Windows ARM64 decoding, encoding, and Unity Player/GPU behavior require an ARM64
Windows machine. Build success and offline recipe tests do not substitute for that run.

## Independent PE inspection

```powershell
python -B Tools/Tests/FFmpegBuildValidation/verify_windows_dlls.py --targets win-x86,win-x64,win-arm64
```

This uses `llvm-readobj` (override with `--llvm-readobj`) from the authenticated
LLVM-MinGW cache and independently checks PE machine IDs, mandatory FFmpeg/Unity
exports, colocated dependency architectures, system-DLL closure, and absence of
dynamic C++/pthread runtimes. `--output <ignored evidence.json>` records imports
and export counts. Static inspection intentionally does not load ARM64 code.

## Native staged-DLL loader smoke

`NativeLoadSmoke.c` is a small Windows executable that uses `LoadLibraryExA` with
only `LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32`. Compile
with the selected target driver and the pinned binding headers; no FFmpeg import
archive or compiler DLL is needed. For example, from the repository root:

```powershell
$llvm = 'Tools/FFmpeg/.build/toolchains/llvm-mingw-20260922-ucrt-x86_64/bin'
& "$llvm/x86_64-w64-mingw32-clang.exe" -std=c11 -Wall -Wextra -Werror -I ThirdParty/FFmpeg.AutoGen/FFmpeg/include Tools/Tests/FFmpegBuildValidation/NativeLoadSmoke.c -o Tools/FFmpeg/.build/NativeLoadSmoke-x64.exe
& Tools/FFmpeg/.build/NativeLoadSmoke-x64.exe "$PWD/Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows/x86_64"
```

It checks all seven exact public-header library versions, the libdav1d decoder,
five requested software encoders, bridge ABI 4, and undecorated Unity lifecycle
exports. Compile with `i686-...` or `aarch64-...` for the other Windows targets.
The ARM64 executable additionally requires that the unavailable NVENC/AMF
encoders are absent. Run it only on the matching Windows machine; compilation
is not execution. DLL registration does not prove encoding/decoding or GPU/Unity
Player integration. Existing native GPU smokes are described in the FFmpeg
Native README and should be run separately.
