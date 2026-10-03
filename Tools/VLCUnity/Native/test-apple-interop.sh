#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../../.." && pwd)"
build_dir="$repo_root/Temp/VLCUnityAppleInteropSmoke"
if [[ "$(uname -s)" != Darwin ]]; then
    echo 'This real CGL/Metal driver test requires macOS with Xcode command-line tools.' >&2
    exit 1
fi
mkdir -p "$build_dir"
xcrun --sdk macosx clang++ -std=c++17 -fobjc-arc -Wall -Wextra -Werror \
    "$script_dir/PortableAppleInterop.mm" "$script_dir/PortableAppleInteropSmoke.mm" \
    -framework Foundation -framework Metal -framework CoreVideo -framework IOSurface -framework OpenGL \
    -o "$build_dir/PortableAppleInteropSmoke"
"$build_dir/PortableAppleInteropSmoke"
