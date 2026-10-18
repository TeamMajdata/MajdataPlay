# Maj Render Pipeline library

This directory contains the reusable render pipeline implementation. Project
presets, integration notes, and validation history live in
[`Assets/Rendering/RenderPipelines/MajRenderPipeline`](../../../../../Rendering/RenderPipelines/MajRenderPipeline/README.md).

## Layout

- `Runtime/`: pipeline, camera renderer, attachment management, diagnostics,
  and display resolution support.
- `Runtime/Shaders/`: the required `Hidden/Maj/FinalCopy` shader and the reusable
  `Maj/Unlit` shader. Keep their `.meta` files with the shaders.
- `Editor/`: Scene View support and the Unlit material inspector.
- `Library/`: shared HLSL includes, upstream source notes, and Unity license.

The `MajdataPlay.MajRenderPipe` assembly is declared by
`Assets/Plugins/MajdataPlay/Rendering/MajdataPlay.Rendering.asmdef`. It does not
depend on URP, Core RP, Live2D, or game scripts.
`Editor/MajdataPlay.MajRenderPipe.asmref` deliberately joins the editor sources
to that assembly: `MajCameraRenderer.Editor.cs` is a partial declaration of the
runtime camera renderer. Editor sources must retain their `UNITY_EDITOR` guards.

## Project integration

Assign a `MajRenderPipelineAsset` in Graphics or Quality settings. This repository
keeps its configured assets under
`Assets/Rendering/RenderPipelines/MajRenderPipeline/Presets/`.
The pipeline asset retains a serialized reference to `Hidden/Maj/FinalCopy`;
moving the shader must preserve its GUID.

Project shaders import shared HLSL from
`Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline/Library/`.
The include files use relative paths internally. Their provenance and license
are recorded in [SOURCE.md](Library/SOURCE.md) and
[LICENSE-Unity.md](Library/LICENSE-Unity.md).

## Camera capture

`MajCameraCaptureBridge.AddCaptureAction` / `RemoveCaptureAction` let a consumer
record commands against the current camera's resolved scene color. Registration
and removal run on the main thread. The supplied texture is valid for commands
recorded during that callback; consumers must not retain it. The bridge depends
only on Unity Engine. FFmpeg references MRP, not the other way around.

While capture is registered, an eligible independent main-display Game camera
uses a sampleable intermediate color target. MRP ends the scene pass and resolves
MSAA before invoking capture actions, then restores the output target and viewport
for FinalCopy. Direct-to-screen Resolve is disabled only during capture; without
capture, the original direct and direct-Resolve choices remain available. The
runtime output status reports `IntermediateCapture` (`RT+Copy`, camera capture).

Unsupported shared targets, secondary displays, stereo cameras, or cameras that
preserve previous color are not redirected for capture: MRP logs a warning and
continues rendering. Explicit camera RenderTextures remain the consumer's
end-context capture path. Screen Space Camera and World Space UI are included;
Screen Space Overlay UI is outside camera capture. Capturing still costs the
consumer's copy, readback, and encoding work. Game settings currently control OBS;
this bridge does not add a game-menu FFmpeg recording mode.

## Validation

From the repository root:

```powershell
./Tools/Tests/RenderingLayoutValidation/Validate.ps1
dotnet run --project Tools/Tests/SettingValidation/SettingValidation.csproj
dotnet run --project Tools/Tests/MrpCaptureValidation/MrpCaptureValidation.csproj -c Release
```

Use the Unity version in `ProjectSettings/ProjectVersion.txt` for import and
script compilation. The capture harness verifies registration snapshots,
unregistration, destroyed-camera cleanup, reset, and allocation-free steady-state
lookup. Static layout and managed checks do not verify rendered pixels, recorded
orientation, Player behavior, or platform hardware.
