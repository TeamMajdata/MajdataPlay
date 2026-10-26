# Sensor visualization integration validation

Run with the project's pinned Unity 6000.3.17f1 Editor:

```powershell
./Tools/Tests/Modules/SensorDebugValidation/Validate.ps1 -UnityPath 'C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe'
dotnet run --project Tools/Tests/Modules/SettingValidation/SettingValidation.csproj
```

For actual GPU drawing and readback on Windows, run the optional D3D11 check:

```powershell
./Tools/Tests/Modules/SensorDebugValidation/ValidateGpu.ps1 -UnityPath 'C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe'
```

This also runs the headless prerequisite. A minimal test SRP culls and draws the
production sensor shader and controller into an isolated test RenderTexture.
Known rectangular source geometry starts outside the camera; the production
visual mapping and culling-bounds override must make it visible in the requested
secondary rectangle. Readback checks top/bottom alignment and horizontal center,
classic idle/active colors blended at 20% and 90%, return-to-main behavior, and
zero output after disabling visualization. The test SRP and RT exist only here;
the production feature adds neither a pipeline nor a render target.

Verified on 2026-10-23: 17565 headless assertions, 34 GPU assertions with Unity
6000.3.17f1 / Direct3D11 / RTX 3080 Ti. This is an isolated rendering check, not a
main-game Play Mode, Android, or iOS validation.

`Validate.ps1` first runs `ValidateSceneFlags.ps1`, which checks that exactly the
34 sensor children (29 scene objects and five FBX instance overrides) have static
batching disabled. Their meshes must remain replaceable at runtime. Use
`ValidateSceneFlags.ps1 -BaselinePath <original-Init.unity>` to additionally verify
that reversing only those 34 flag changes reproduces the baseline scene text.

The first command stages an isolated project under ignored
`Temp/SensorDebugValidation`. It imports the real full-area FBX, five baked
border meshes, sensor material, shader, and MRP includes. It runs the production
`DummyTouchPanelRenderer`, `DebugOptions`, `SensorDebugStyle`, and
`SensorDebugPosition` with real Unity MeshFilter, MeshRenderer, MeshCollider, and
MaterialPropertyBlock objects. Only game services, input data, secondary display
edges, unrelated timing enumerator type, and hierarchy enumeration are narrow
substitutes. The installed Editor's Newtonsoft DLL checks string enum serialization
with the same StringEnumConverter / replacement policy used by the game.

Checks cover all 34 mappings, geometry selection, both trigger states and all
three styles, every opacity step from 10% through 90%, and shared material
identity. The secondary-screen checks read actual renderer property blocks,
transform every real mesh vertex independently, and verify vertical alignment,
horizontal centering, uniform scale, and culling bounds. They also cover changing
secondary display edges, parent translation/rotation/scale, the production
0.021432761 root scale combined with independently rotated 100-scale children,
invalid-edge hiding and recovery, style/position changes while hidden, and
resetting automatic bounds
on return to the main screen. Collider geometry, input transforms, and material
colors must remain unchanged by visualization; switching debug display off must
disable every renderer. The second command covers the actual DebugOptions model,
default/JSON behavior, localized enum menus, and setting refresh.

The headless Editor test does not render pixels, exercise real touch I/O, run the
main scenes or a Player, or measure GPU overdraw or device power. Those require
Play Mode and target-device testing. Generated projects and logs are local only.
