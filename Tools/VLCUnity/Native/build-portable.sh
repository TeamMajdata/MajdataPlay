#!/usr/bin/env bash
set -euo pipefail

# Native Linux/macOS and macOS-hosted iOS builds. Decoder binaries are packaged
# separately; bridge generation never downloads a different LibVLC snapshot.
source_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
target="${1:-}"
build_dir="${2:-$source_dir/../../../Build/VLCUnityPortable/$target}"
case "$target" in
  linux)
    cmake -S "$source_dir" -B "$build_dir" -DCMAKE_BUILD_TYPE=Release
    ;;
  macos)
    cmake -S "$source_dir" -B "$build_dir" -DCMAKE_BUILD_TYPE=Release \
      '-DCMAKE_OSX_ARCHITECTURES=arm64;x86_64' -DCMAKE_OSX_DEPLOYMENT_TARGET=10.15
    ;;
  ios-device)
    cmake -S "$source_dir" -B "$build_dir" -G Xcode -DCMAKE_SYSTEM_NAME=iOS \
      -DCMAKE_OSX_SYSROOT=iphoneos -DCMAKE_OSX_ARCHITECTURES=arm64 \
      -DCMAKE_OSX_DEPLOYMENT_TARGET=13.0 -DCMAKE_XCODE_ATTRIBUTE_CODE_SIGNING_ALLOWED=NO
    ;;
  ios-simulator)
    cmake -S "$source_dir" -B "$build_dir" -G Xcode -DCMAKE_SYSTEM_NAME=iOS \
      -DCMAKE_OSX_SYSROOT=iphonesimulator '-DCMAKE_OSX_ARCHITECTURES=arm64;x86_64' \
      -DCMAKE_OSX_DEPLOYMENT_TARGET=13.0 -DCMAKE_XCODE_ATTRIBUTE_CODE_SIGNING_ALLOWED=NO
    ;;
  *)
    printf 'Usage: %s {linux|macos|ios-device|ios-simulator} [build-directory]\n' "$0" >&2
    exit 2
    ;;
esac
cmake --build "$build_dir" --config Release
cmake --install "$build_dir" --config Release --prefix "$build_dir/package"
printf 'Bridge output: %s/package\n' "$build_dir"
printf 'Supply the matching LibVLC native decoder, modules and their dependencies separately.\n'
