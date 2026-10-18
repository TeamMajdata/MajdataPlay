# Rendering layout validation

Run this read-only regression check from the repository root with PowerShell 5.1
or newer and Git available on `PATH`:

```powershell
./Tools/Tests/RenderingLayoutValidation/Validate.ps1
```

The check covers the reusable pipeline under
`Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline` and the
project presets and documentation under
`Assets/Rendering/RenderPipelines/MajRenderPipeline`. It verifies asset/metadata
pairs, uniqueness of moved GUIDs, the preserved assembly and active preset
identities, Editor partial assembly membership, project settings references,
shader include paths with case-sensitive comparisons, and the production source
link used by SettingValidation. Repository-owned text under `Assets` and `Tools`
is checked for obsolete pipeline paths.

The reusable assembly lives at
`Assets/Plugins/MajdataPlay/Rendering/MajdataPlay.Rendering.asmdef`. Its preserved
name is `MajdataPlay.MajRenderPipe`; the check follows that parent-level asset,
validates its unique GUID, and rejects non-engine assembly references.

Files are enumerated through Git, including untracked files but excluding ignored
generated outputs and submodule contents. Shader checks cover the pipeline,
project shaders, and the Live2D and TMP includes affected by this relocation;
Unity built-in includes and unrelated SDK-local includes are outside their scope.
No generated files or temporary project are created.

This is a static asset and reference check. It does not import assets, compile C#
or shaders, or verify rendered output. Open the main project in its pinned Unity
Editor version for import/compilation, and use Play Mode or the relevant isolated
rendering checks for runtime verification.
