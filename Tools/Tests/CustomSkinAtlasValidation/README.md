# CustomSkin Atlas Validation

Run the isolated Unity check with the Editor version specified by the repository:

```powershell
./Tools/Tests/CustomSkinAtlasValidation/run.ps1
./Tools/Tests/CustomSkinAtlasValidation/run.ps1 -GraphicsApi glcore
./Tools/Tests/CustomSkinAtlasValidation/run.ps1 -GraphicsApi vulkan
./Tools/Tests/CustomSkinAtlasValidation/run.ps1 -Baseline
```

The script stages the current, complete production `CustomSkin.cs` and real UniTask
runtime in `Temp/CustomSkinAtlasValidation`. It uses cached Burst, Collections and
Mathematics packages, plus Unity's built-in Vector Graphics module. No main-project
scene or Player setting is opened or modified. A graphics device is required:
the script deliberately omits `-nographics`. The selected API must be available on
the host. `-UnityPath` can specify the same repository-pinned Editor elsewhere.

Only the `SystemInfo` capability query is aliased in the staged source, allowing
device limits of 2048, 4096, 8192 and 16384 to exercise the real production build
methods and Unity `Texture2D.PackTextures`. Five narrow PNG inputs, each up to
8192 by 32 pixels (limited by the actual host maximum), force downscaling at the
2048 and 4096 limits on hosts supporting 8192-wide textures. Both synchronous and
asynchronous paths check atlas
dimensions, device-limit lookup, sprite assignment, scaled borders, sprite
rectangles, shared texture ownership and release of atlas CPU pixel data.
Four inputs use zero borders and transparent margins; one input uses nonzero
borders to check their scale. In this isolated Editor environment, all five
sprites emit `Not allowed to override geometry on sprite` and retain their full
rectangles. Each case expects exactly these five messages with the source's full
path and fails on any other Unity error observed while packing. This existing
Editor behavior is outside the atlas-size repair; applied custom outline geometry
is **not verified** by this tool.

`-Baseline` stages the committed `HEAD` version of `CustomSkin.cs` and runs one
synchronous fixture through the same packing, border and expected-error checks.
It omits the new device-query requirement and demonstrates whether the existing
Editor geometry error also occurs before the change. This comparison requires a
host supporting at least 8192, because the previous implementation allocates an
8192-square texture before packing. The baseline does not check simulated limits.
Empty and missing inputs are covered, and one pass uses the actual device limit.
The larger-limit cases use this same narrow fixture; they do not allocate a
16384-square atlas or exhaust device memory. On hosts whose real limit is at most
4096, the 4096 case checks the limit but cannot force a wider source. The initial
2-by-2 allocation is exercised through the production build methods; simulated
limits do not change the host driver's actual constructor checks. Cases whose
effective atlas ceiling exceeds the host's real limit are skipped and logged.

Narrow service substitutes supply a disposable list/array pool, Unity PNG decoding
in place of Skia, logging and the unused skin preload service. The production
packing, border scaling, outline extraction and triangulation remain intact.
This validates the atlas path in the Editor with the selected desktop graphics
API; it does **not** validate Skia decoding, complete skin loading, rendered output,
Android/OpenGLES, other target architectures, hardware limits or allocation failure.

Success is marked by `CUSTOM_SKIN_ATLAS_VALIDATION_PASSED` in
`Temp/CustomSkinAtlasValidation/validation-<api>.log`; the script also checks the
Editor exit code. All generated inputs, package files, assemblies and logs stay in
the ignored isolated project.

## Local result (2026-10-06)

Unity 6000.3.17f1 on Windows, AMD Radeon RX 580 2048SP, Direct3D11,
and OpenGL Core, actual maximum texture dimension 16384: both API commands passed
all nine
packing cases plus empty/missing inputs. Simulated 2048 and 4096 limits produced
2048-by-256 and 4096-by-256 atlases; 8192 and 16384 limits produced 8192-by-256
atlases. Synchronous and asynchronous results agreed. The narrow source reduced
only its width, so the checks also exercised independent X/Y border scaling.
Each packing case observed exactly five expected geometry errors; applied custom
outline geometry and Android/OpenGLES remain unverified.
The Direct3D11 `-Baseline` command also passed with the committed production
source and the same five geometry errors, confirming they predate this repair.
