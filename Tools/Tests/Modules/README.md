# RawSprite Draw Mode Validation

Run with the project's Unity Editor version. The script stages only the mesh builder,
resource cache, and validation harness in `Temp/RawSpriteDrawModeValidation`, then
runs Unity in batch mode. It does not close or modify an open scene in the main project.

```powershell Tools/Tests/Modules/ValidateRawSpriteDrawModes.ps1
./Tools/Tests/Modules/ValidateRawSpriteDrawModes.ps1 -UnityPath 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Unity.exe'
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

## Rendering library layout

Run `./Tools/Tests/Modules/RenderingLayoutValidation/Validate.ps1` to check the reusable
pipeline and project-asset directory split, preserved GUIDs and assembly
membership, shader include paths, and the settings harness source link.
See [Rendering layout validation](RenderingLayoutValidation/README.md) for scope
and limitations. This read-only check does not replace Unity compilation or
rendered-output verification.

## Android graphics state warmup

See [Graphics state validation](GraphicsStateValidation/README.md) for the isolated
Android ARM64 Vulkan tracing, serialization and asynchronous warmup device probe.
Production scene capture is documented in
[Rendering](../../../Assets/Scripts/Rendering/README.md).

## Translation Editor

See [Translation editor validation](TranslationEditorValidation.md) for the static
key-analysis regression tests, isolated Unity editor checks, and editor workflow.

## Android Java bindings and legacy generator

The Unity runtime uses the standalone CLI to generate `Tools/AndroidJavaWrapper/src`
and compile/install `MajdataPlay.Platform.Android.Runtime.dll` as a Unity plugin.
See `AndroidJavaWrapperGeneratorValidation/README.md` for generation, namespace
directory migration, project bootstrap, and production consumer compilation.

```powershell
Push-Location Tools/Tests/Modules/AndroidJavaWrapperGeneratorValidation
dotnet run --project AndroidJavaWrapperGeneratorValidation.csproj -- --editor-inputs
Pop-Location
./Tools/Tests/Modules/ValidateAndroidJavaWrapperRefreshUnity.ps1
```

See `AndroidJavaGeneratorValidation/README.md` for .NET 9 regression tests using
real Unity reference assemblies, archived legacy runtime fixtures, and the bundled Android SDK/JDK. See
`../../CSharp/AndroidJavaGenerator/README.md` for wrapper usage, toolchain configuration,
documentation provenance, and legacy runtime ownership. The analyzer is no longer
required by the main project's runtime consumers.

```powershell
dotnet run --project Tools/Tests/Modules/AndroidJavaGeneratorValidation/AndroidJavaGeneratorValidation.csproj
./Tools/Tests/Modules/ValidateAndroidJavaGeneratorUnity.ps1
```

The second command creates an ignored, isolated Unity 6000.3.17f1 project; it
verifies real analyzer loading, generated SDK classes/interfaces, readonly
properties, overload preservation, editor-companion compilation, and the
non-Android JNI guard. Neither check validates an Android Player or hardware.

## Portable file and directory storage

See `FileSystemValidation/README.md` and
`../../../Assets/Plugins/MajdataPlay/IO/Storage/README.md` for the portable local/opaque-URI
regressions, Android SAF API, and target/hardware validation limitations.

```powershell
dotnet run --project Tools/Tests/Modules/FileSystemValidation/FileSystemValidation.csproj
./Tools/Tests/Modules/ValidateFileSystemUnity.ps1
```

The second command stages an ignored isolated Unity 6000.3.17f1 project, checks
Editor smoke/Android guards, compiles Windows64 and Android player scripts, and
compiles Android Java sources. It does not exercise SAF on a device or build a player.

## Song storage and collection refresh

See `ChartStorageValidation/README.md` for the package-free .NET 9 regression
suite linking the production storage, collection and scanning implementations.
See `SongStorageUnityValidation/README.md` for isolated Unity 6000.3.17f1
compilation and real MajSimai metadata/chart parsing.

```powershell
dotnet run --project Tools/Tests/Modules/ChartStorageValidation/ChartStorageValidation.csproj
./Tools/Tests/Modules/ValidateSongStorageUnity.ps1 -Repetitions 12
```

The managed suite repeats 96-collection scans and verifies shared concurrency,
non-publishing initialization, independent local/online refresh, zero cross-source
I/O/requests, cached playlist rebinding, cancellation/rollback and retained chart
references/selection. Portable favorites DB/export hashes are loaded at InitAsync;
both refreshes use committed hashes and never reload the DB/export. Legacy tests
explicitly await Local then Online; no production full-refresh API remains.
The Unity harness uses real parser and UniTask dependencies, also checks
independent source references and cached definitions, and keeps narrow
game/media/network/database substitutes in an ignored isolated project. Neither suite validates real devices, native media/SQLite,
online protocols, Player lifecycle or Android SAF permissions.

## Chart import layout

See `ImportValidation/README.md` for the archive and directory import layout
regressions of the production `ChartImporter`.

```powershell
dotnet run --project Tools/Tests/Modules/ImportValidation/ImportValidation.csproj
```

The cases link the production importer and storage sources, so they verify layout
classification and file effects rather than Unity scene switching, Android SAF
sources or Player integration.

## Android storage / JNI / IL2CPP device validation

See `AndroidStorageDeviceValidation/README.md` for the isolated test APK and
`AndroidStorageDeviceValidation/RESULTS.md` for actual ARM64/ARMv7 physical-device
evidence and the local-provider validation boundary. The main game project is
not rebuilt or changed by these tools. Unlock the selected authorized device
and use the system picker to choose only a disposable scratch directory.

```powershell
./Tools/Tests/Modules/Build-AndroidStorageDeviceValidation.ps1 -Architecture Both
./Tools/Tests/Modules/Run-AndroidStorageDeviceValidation.ps1 -Serial '<device-serial>' -Install -Abi arm64-v8a -Mode full
./Tools/Tests/Modules/Run-AndroidStorageDeviceValidation.ps1 -Serial '<device-serial>' -Abi arm64-v8a -Mode replay
./Tools/Tests/Modules/Run-AndroidStorageDeviceValidation.ps1 -Serial '<device-serial>' -Abi arm64-v8a -Mode release
```

Repeat full/replay/release with `-Install -Abi armeabi-v7a` to verify an actual
32-bit process. Release only grants newly acquired by the dedicated test app;
never clear another application's data or delete the selected parent directory.

## ManagedBass callbacks on CoreCLR and IL2CPP

See `ManagedBassValidation/README.md`. The first command builds ManagedBass as separate
.NET assemblies and runs its callback wrappers against the bundled Windows x64 BASS; the
second builds an isolated Android IL2CPP Player, checks the generated reverse P/Invoke
wrappers and, with `-Serial`, runs the Player on a device.

```powershell
dotnet run -c Release --project Tools/Tests/Modules/ManagedBassValidation/ManagedBassValidation.csproj
./Tools/Tests/Modules/ManagedBassValidation/Il2Cpp/run-android.ps1 -Architecture arm64
```

## CustomSkin loading memory and atlas layout

```powershell
dotnet run --project Tools/Tests/Modules/AtlasLayoutValidation/AtlasLayoutValidation.csproj
./Tools/Tests/Modules/CustomSkinMemoryValidation/run.ps1 -Player
./Tools/Tests/Modules/CustomSkinAtlasValidation/run.ps1
```

`AtlasLayoutValidation` checks the atlas packer arithmetic without Unity.
`CustomSkinMemoryValidation` loads a real skin (default `Assets/StreamingAssets/Skins/default`)
through the production `CustomSkin` and real SkiaSharp in an isolated Editor or standalone
Windows player and fails when the sampled memory peak exceeds `-BudgetMiB` (default 1024).
`CustomSkinAtlasValidation` checks atlas limits, borders and sprite assignment under simulated
device texture limits. See each directory's README for what is and is not covered.
