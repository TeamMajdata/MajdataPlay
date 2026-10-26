#!/usr/bin/env bash
set -euo pipefail
here="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
backend="${1:-glcore}"
case "$backend" in glcore|vulkan) ;; *) echo 'Usage: run-linux-player.sh [glcore|vulkan] [player directory] [--hardware]' >&2; exit 2 ;; esac
result="${2:-$here/.work/Linux-x64-Mono}"
result="$(cd -- "$result" && pwd)"
player="$result/VideoSmoke.x86_64"
if [[ ! -f "$player" ]]; then
    echo 'Build first: run-unity.ps1 -Platform Linux -Architecture x64 -Backend Mono -Graphics glcore' >&2
    exit 2
fi
chmod +x "$player"
hardware=false
suffix=software
if [[ "${3:-}" == --hardware ]]; then hardware=true; suffix=hardware; fi
report="$result/$backend-$suffix.txt"
log="$result/$backend-$suffix.log"
: > "$report"
# A Linux Unity graphics session is required (desktop X11/Wayland or WSLg).
# Do not use -nographics: this checks actual texture pixels, not just startup.
if ! env -u LD_LIBRARY_PATH timeout 120 "$player" -batchmode "-force-$backend" \
    -logFile "$log" -videoHardware "$hardware" -videoRequireHardware "$hardware" -videoReport "$report" > "$result/$backend-console.log" 2>&1; then
    tail -n 60 "$log"
    echo "Linux Unity Player exited unsuccessfully; see $log" >&2
    exit 1
fi
first_line=''
IFS= read -r first_line < "$report" || true
if [[ "$first_line" != PASS:* ]]; then
    tail -n 60 "$log"
    echo "Linux Unity playback failed; see $log" >&2
    exit 1
fi
cat "$report"
