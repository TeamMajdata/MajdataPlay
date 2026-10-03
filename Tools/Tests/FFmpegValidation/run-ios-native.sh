#!/bin/bash
set -euo pipefail
repo="$(cd "$(dirname "$0")/../../.." && pwd)"
target="${1:-ios-simulator-arm64}"
media="${2:-$repo/Assets/StreamingAssets/MaiCharts/Original/Zunda Overdance/bg.mp4}"
case "$target" in
  ios-arm64) sdk=iphoneos; triple=arm64-apple-ios15.0; libraries="$repo/Assets/Plugins/MajdataPlay/FFmpeg/Native/iOS";;
  ios-simulator-arm64) sdk=iphonesimulator; triple=arm64-apple-ios15.0-simulator; libraries="$repo/Tools/FFmpeg/.build/artifacts/$target";;
  ios-simulator-x64) sdk=iphonesimulator; triple=x86_64-apple-ios15.0-simulator; libraries="$repo/Tools/FFmpeg/.build/artifacts/$target";;
  *) echo "Unsupported iOS target: $target" >&2; exit 2;;
esac
work="$repo/Tools/Tests/FFmpegValidation/.work/Apple-$target"
mkdir -p "$work"
xcrun --sdk "$sdk" clang++ -target "$triple" -isysroot "$(xcrun --sdk "$sdk" --show-sdk-path)" \
    -std=c++17 -fobjc-arc -Wall -Wextra -Werror \
    -DUnityPluginLoad=FfuUnityPluginLoad -DUnityPluginUnload=FfuUnityPluginUnload \
    -I "$repo/ThirdParty/FFmpeg.AutoGen/FFmpeg/include" -I "$repo/Tools/FFmpeg/Native/Unity" \
    "$repo/Tools/FFmpeg/Native/tests/MetalSmoke.mm" "$libraries"/*.a \
    -framework Metal -framework CoreVideo -framework Foundation -framework VideoToolbox \
    -framework AudioToolbox -framework CoreMedia -framework CoreFoundation -framework Security \
    -o "$work/MetalSmoke"
echo "PASS: $target linked all FFmpeg static archives and the registered Metal bridge" | tee "$work/link.txt"
if [[ -n "${FFMPEG_SIMULATOR_UDID:-}" && "$target" == ios-simulator-arm64 ]]; then
    codesign --force --sign - "$work/MetalSmoke"
    xcrun simctl spawn "$FFMPEG_SIMULATOR_UDID" "$work/MetalSmoke" "$media" | tee "$work/metal.txt"
fi
