# RawSprite Draw Mode Validation

Run with the project's Unity Editor version. The script stages only the mesh builder,
resource cache, and validation harness in `Temp/RawSpriteDrawModeValidation`, then
runs Unity in batch mode. It does not close or modify an open scene in the main project.

```powershell Tools/Tests/ValidateRawSpriteDrawModes.ps1
./Tools/Tests/ValidateRawSpriteDrawModes.ps1 -UnityPath 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Unity.exe'
```

The harness checks Simple geometry, nine-slice borders, compressed borders, pivot,
flips and winding, subrect UVs, Continuous partial tiles, Adaptive thresholds,
zero size, null sprites, the quad budget, mesh sharing, and reference release.
It does not perform rendered-image comparisons against Unity's SpriteRenderer.

## Renderer Settings

- `DrawMode`: Unity's `SpriteDrawMode.Simple`, `Sliced`, or `Tiled`.
- `Size`: local-space width/height, default `(1, 1)`. Ignored in Simple mode.
  Set it explicitly when switching to Sliced/Tiled. To use the sprite's original
  dimensions, assign `sprite.rect.size / sprite.pixelsPerUnit`.
- `TileMode`: Unity's `SpriteTileMode.Continuous` or `Adaptive`.
- `AdaptiveModeThreshold`: range `[0, 1]`, default `0.5`.

Use Full Rect sprite meshes for Sliced/Tiled and define borders in the Sprite Editor.
Border values are converted from pixels using the sprite's pixels-per-unit value.
Without borders, Sliced stretches the whole rectangle and Tiled repeats it.
Use Size to resize without scaling the borders; Transform scale still scales everything.
Negative and non-finite Size components are clamped to zero.

Rectangular atlas packing is supported, including rotated/flipped UV mappings.
Tight atlas packing logs a warning and falls back to Simple geometry.
A conservative estimate above 65,536 quads logs a warning and falls back to Sliced
geometry, preventing unbounded mesh allocation for very large tiled sizes.

Identical settings share immutable meshes. Changing Size can allocate a new mesh;
continuous size animation is not allocation-free. NoteRenderer keeps its preloaded
Simple meshes and uses the shared cache for Sliced/Tiled, still ignoring flips.

## Translation Editor

See [Translation editor validation](TranslationEditorValidation.md) for the static
key-analysis regression tests, isolated Unity editor checks, and editor workflow.

## Android Java source generator

See `AndroidJavaGeneratorValidation/README.md` for .NET 9 regression tests using
real Unity reference assemblies and the bundled Android SDK/JDK. See
`../AndroidJavaGenerator/README.md` for wrapper usage, toolchain configuration,
documentation provenance, and runtime ownership.

```powershell
dotnet run --project Tools/Tests/AndroidJavaGeneratorValidation/AndroidJavaGeneratorValidation.csproj
./Tools/Tests/ValidateAndroidJavaGeneratorUnity.ps1
```

The second command creates an ignored, isolated Unity 6000.3.17f1 project; it
verifies real analyzer loading, generated SDK classes/interfaces, readonly
properties, overload preservation, editor-companion compilation, and the
non-Android JNI guard. Neither check validates an Android Player or hardware.

## Portable file and directory storage

See `FileSystemValidation/README.md` and
`../../Assets/Plugins/MajdataPlay/IO/Storage/README.md` for the portable local/opaque-URI
regressions, Android SAF API, and target/hardware validation limitations.

```powershell
dotnet run --project Tools/Tests/FileSystemValidation/FileSystemValidation.csproj
./Tools/Tests/ValidateFileSystemUnity.ps1
```

The second command stages an ignored isolated Unity 6000.3.17f1 project, checks
Editor smoke/Android guards, compiles Windows64 and Android player scripts, and
compiles Android Java sources. It does not exercise SAF on a device or build a player.

## Android storage / JNI / IL2CPP device validation

See `AndroidStorageDeviceValidation/README.md` for the isolated test APK and
`AndroidStorageDeviceValidation/RESULTS.md` for actual ARM64/ARMv7 physical-device
evidence and the local-provider validation boundary. The main game project is
not rebuilt or changed by these tools. Unlock the selected authorized device
and use the system picker to choose only a disposable scratch directory.

```powershell
./Tools/Tests/Build-AndroidStorageDeviceValidation.ps1 -Architecture Both
./Tools/Tests/Run-AndroidStorageDeviceValidation.ps1 -Serial '<device-serial>' -Install -Abi arm64-v8a -Mode full
./Tools/Tests/Run-AndroidStorageDeviceValidation.ps1 -Serial '<device-serial>' -Abi arm64-v8a -Mode replay
./Tools/Tests/Run-AndroidStorageDeviceValidation.ps1 -Serial '<device-serial>' -Abi arm64-v8a -Mode release
```

Repeat full/replay/release with `-Install -Abi armeabi-v7a` to verify an actual
32-bit process. Release only grants newly acquired by the dedicated test app;
never clear another application's data or delete the selected parent directory.
