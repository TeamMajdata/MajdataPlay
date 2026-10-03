#!/usr/bin/env bash
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../../.." && pwd)"
libraries="$(realpath "${1:-$repo/Assets/Plugins/MajdataPlay/FFmpeg/Native/Linux/x86_64}")"
media="${2:-$repo/Assets/StreamingAssets/MaiCharts/Original/Zunda Overdance/bg.mp4}"
output="${TMPDIR:-/tmp}/majdata-linux-native-smoke-${UID}"
mkdir -p "$here/.work"
cc -std=gnu11 -Wall -Wextra -Werror -I"$repo/ThirdParty/FFmpeg.AutoGen/FFmpeg/include" \
    "$here/LinuxNativeSmoke.c" -ldl -o "$output"
{
    for library in "$libraries"/lib*.so.*; do
        case "$library" in *.meta) continue ;; esac
        printf '%s: ' "$(basename "$library")"
        readelf -d "$library" | grep -E 'RUNPATH|RPATH' | grep -F '[$ORIGIN]'
    done
    env -u LD_LIBRARY_PATH ldd "$libraries/libavformat.so.63"
    env -u LD_LIBRARY_PATH "$output" "$libraries" "$media"
} | tee "$here/.work/linux-native.txt"
