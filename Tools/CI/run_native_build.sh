#!/bin/bash
# Run the two Linux builds directly with the user-installed Unity Editor.
set -euo pipefail
: "${UNITY_EDITOR:?Set UNITY_EDITOR to the native editor executable}"
: "${PROJECT_ROOT:?Set PROJECT_ROOT}"
: "${CONFIGURATION:?Set CONFIGURATION}"
: "${TARGET_PLATFORM:?Set TARGET_PLATFORM}"
output="$PROJECT_ROOT/$CONFIGURATION/$TARGET_PLATFORM/MajdataPlay"
parameters=()
case "$TARGET_PLATFORM" in
    Android)
        method=MajdataPlay.Editor.CustomBuild.BuildAndroidExport
        version="$BUNDLE_VERSION_WITH_HASH"
        parameters+=(-androidVersionCode "$BUNDLE_VERSION_CODE_FOR_APK")
        ;;
    StandaloneOSX)
        method=MajdataPlay.Editor.CustomBuild.BuildRelease
        version="$BUNDLE_VERSION"
        output="$output.app"
        parameters+=(-macOSArchitecture ARM64)
        ;;
    *) echo "Unsupported native build target: $TARGET_PLATFORM" >&2; exit 2 ;;
esac
"$UNITY_EDITOR" -batchmode -nographics -quit \
    -projectPath "$PROJECT_ROOT" -buildTarget "$TARGET_PLATFORM" \
    -executeMethod "$method" -customBuildPath "$output" \
    -customBuildName MajdataPlay -buildVersion "$version" \
    "${parameters[@]}" -logFile -
