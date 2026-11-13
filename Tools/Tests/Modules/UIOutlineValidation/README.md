# UI rendering validation

This runner stages a small Unity project for the current UI rendering regressions.
Use **Unity 6000.3.17f1**, matching `ProjectSettings/ProjectVersion.txt`, and the
main project's populated Unity package cache. It is not a performance benchmark
or a replacement for complete-menu and target-device verification.

## Running

From the repository root:

```powershell
./Tools/Tests/Modules/UIOutlineValidation/ValidateLifecycle.ps1
```

The default runs the selected EditMode and PlayMode fixtures with `-nographics`.
GPU pixel-readback cases require a graphics device and must be selected explicitly
where they are outside the runner's default filter:

```powershell
./Tools/Tests/Modules/UIOutlineValidation/ValidateLifecycle.ps1 -Mode EditMode -Graphics -TestFilter 'MajdataPlay.Tests.Game.Rendering.AtlasAlphaMaterialTests;MajdataPlay.Tests.Game.Rendering.UiBackdropMrpStageTests'
./Tools/Tests/Modules/UIOutlineValidation/ValidateLifecycle.ps1 -Mode PlayMode -Graphics -TestFilter 'MajdataPlay.Tests.Game.Rendering.UiPvBackdropLifecycleTests;MajdataPlay.Tests.Game.Rendering.UiGlassOpticsAtlasTests'
```

`-Mode EditMode` or `-Mode PlayMode` selects one suite. `-TestFilter` accepts a
Unity test filter; using class names avoids depending on retired experiment
method names. `-Graphics` requests D3D11 by default. For isolated offscreen optics
on Vulkan, select that fixture explicitly:

```powershell
./Tools/Tests/Modules/UIOutlineValidation/ValidateLifecycle.ps1 -Mode PlayMode -Graphics -GraphicsApi Vulkan -TestFilter 'MajdataPlay.Tests.Game.Rendering.UiGlassOpticsAtlasTests'
```

A previous complete-MRP Vulkan fixture crashed in Unity's native readback path.
Keep complete-MRP camera-readback checks on D3D11 unless that Vulkan path is
separately investigated. Offscreen optics coverage does not validate Android's
real display, presentation or driver behavior.

To refresh only the isolated inputs, without launching Unity or running tests:

```powershell
./Tools/Tests/Modules/UIOutlineValidation/ValidateLifecycle.ps1 -StageOnly
```

The script writes results and logs under ignored
`Temp/UIOutlineLifecycleValidation/`. It emits `UI_OUTLINE_EditMode_PASSED` or
`UI_OUTLINE_PlayMode_PASSED` only after a zero process exit and a Passed Unity XML
result. Inspect failures and skipped cases rather than treating a launched process
as a passing result.

## Isolation and dependencies

The script copies production UI components, the real MRP renderer and bridge,
optical baker, shaders/materials, paired Ribbon artwork and metadata, Resources,
authored scenes/prefabs, and the explicitly listed test fixtures. It preserves
source asset GUIDs in the copy and does not open or modify main-project scenes.
The package manifest references the main project's installed UGUI, Test Framework
and NUnit cache, alongside its Unity modules.

Before staging, a Windows process guard rejects an Editor already using this
exact isolated project. Save and close that preview first; the runner never
closes it. Known obsolete files from earlier staging layouts are removed only
inside that isolated directory, preventing stale shaders or resources from
contaminating the current copy.

Narrow substitutes provide `MajDebug` logging, read-only test text access, and
`SceneSwitcher` camera/scene identity. They do not replace capture/filtering,
Sprite mapping, material conversion, mesh generation, Camera projection or native
UGUI lifecycle. They also do not prove the real game's initialization, scene
switching, persistent configuration, video decoding or recording integration.

## Retained regression coverage

| Fixtures | Purpose |
| --- | --- |
| `QualityUiImageAssetTests`, `IndependentUiImageAssetTests`, `UiAtlasGeometryTests` | Paired artwork, serialized references, Sprite coordinates, Sliced/Filled/Tiled geometry and layout preservation |
| `ListButtonLayerTests`, `UiPvBackdropAssetTests`, `SensorOverlayMaterialTests` | Authored role opt-ins, visible foreground symbols, button/card source policies and sensor render state |
| `MainDisplayUiClipTests` | Main/sub-display boundaries, transformed layouts, padding and exclusion rules |
| `UiPvBackdropMappingTests` | Independent raw-half/Gaussian-quarter sizing, fixed sigma-4/radius-8 paired kernel and bridge ownership |
| `QualityUiImagePlayModeTests` | Material/texture switching, mapping-cache correctness and invalidation, pooling, Selectable state and stencil keyword synchronization |
| `UiPvBackdropLifecycleTests` | Six persistent targets, style switches, readiness, three independent captures, raw bilinear filtering and disposal |
| `UiGlassOpticsAtlasTests` | Current RGBA8 hardware-filtered contours, profile gating, exact native Rounded Rect controls, source-space mapping, missing-data fallback, refraction and highlight invariants |
| `AtlasAlphaMaterialTests` | Real Alpha/cutoff conversion, artwork/tint/coverage preservation and optical fallback composition |
| `UiBackdropMrpStageTests`, `UiTransitionOverlayPlayModeTests` | Actual MRP stage order, mask/depth/stencil behavior and transition material ownership |

Settings defaults, the five UI styles and legacy JSON migration belong to the
main settings fixtures; this narrow runner does not substitute for those suites.
Configuration, resource lifecycle, selected/unselected controls, sorting/masks
and texture sampling remain functional regressions, not disposable experiments.

## Evidence limits

Only report results from the source revision and platform actually executed.
Asset/CPU checks, isolated GPU readback, full Editor scenes, Player builds and real
devices are separate evidence. Passing one does not establish another, and no
fixed shader-read count proves frame rate, bandwidth or power savings.

Historical ROI/guard kernels, old contour encodings, A/B shader/filter timing and
retired circle-color substitution commands are no longer runner instructions.
Their investigation records are kept outside the production test suite; use the
current architecture/design documents for rendering behavior. The obsolete
8192-lookup timing comparison was removed while retaining its cache-correctness
and invalidation regression.
