#!/bin/bash
set -euo pipefail
repo="$(cd "$(dirname "$0")/../../.." && pwd)"
arch="${1:-$(uname -m)}"
media="${2:-$repo/Assets/StreamingAssets/MaiCharts/Original/Zunda Overdance/bg.mp4}"
case "$arch" in arm64|x86_64) ;; *) echo "Unsupported macOS architecture: $arch" >&2; exit 2;; esac
libraries="$repo/Assets/Plugins/MajdataPlay/FFmpeg/Native/macOS/$arch"
work="$repo/Tools/Tests/FFmpegValidation/.work/Apple-$arch"
mkdir -p "$work"
# FFmpeg's relocatable install IDs intentionally use @loader_path. A linked test
# executable needs the same sibling layout as the deployed Unity plug-ins.
for library in "$libraries"/*.dylib; do ln -sf "$library" "$work/$(basename "$library")"; done
includes="$repo/ThirdParty/FFmpeg.AutoGen/FFmpeg/include"
clang -arch "$arch" -std=gnu11 -Wall -Wextra -Werror -I "$includes" \
    "$repo/Tools/Tests/FFmpegValidation/AppleLoaderSmoke.c" -o "$work/AppleLoaderSmoke"
env -u DYLD_LIBRARY_PATH "$work/AppleLoaderSmoke" "$libraries" "$media" | tee "$work/loader.txt"
clang++ -arch "$arch" -std=c++17 -fobjc-arc -Wall -Wextra -Werror \
    -I "$includes" -I "$repo/Tools/FFmpeg/Native/Unity" \
    "$repo/Tools/FFmpeg/Native/tests/MetalSmoke.mm" \
    "$libraries/libFFmpegUnityBridge.dylib" "$libraries/libavformat.63.dylib" \
    "$libraries/libavcodec.63.dylib" "$libraries/libavutil.61.dylib" "$libraries/libswscale.10.dylib" \
    -framework Metal -framework CoreVideo -framework Foundation \
    -Wl,-rpath,"$libraries" -o "$work/MetalSmoke"
env -u DYLD_LIBRARY_PATH "$work/MetalSmoke" "$media" | tee "$work/metal.txt"
