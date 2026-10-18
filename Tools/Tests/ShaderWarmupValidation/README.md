# Shader warmup diagnostic validation

This package-free .NET 9 executable links the production
`Assets/Scripts/Rendering/ShaderWarmupDiagnostics.cs` and the real
`MajdataPlay.IO.Storage` implementations. Narrow Unity stubs observe warmup
requests without compiling shaders or initializing a graphics device.

Run from the repository root:

```powershell
dotnet run --project Tools/Tests/ShaderWarmupValidation/ShaderWarmupValidation.csproj -c Release
dotnet run --project Tools/Tests/ShaderWarmupValidation/ShaderWarmupValidation.csproj -c Release -p:WarmupValidationTarget=Editor
dotnet run --project Tools/Tests/ShaderWarmupValidation/ShaderWarmupValidation.csproj -c Release -p:WarmupValidationTarget=Desktop
```

The default target defines `UNITY_ANDROID` without `UNITY_EDITOR` and does not
define `DEVELOPMENT_BUILD`. The other targets verify that Android Editor and
desktop compilation retain the original global warmup path. Build output and
fixture files stay under the ignored repository `Temp/ShaderWarmupValidation/`
directory; no Unity assets, project settings, or device files are modified.

The native-boundary assertion opens the already written journal using a strictly
read-only `FileStream` with `FileShare.ReadWrite`. This models an external reader:
on Windows the storage facade's normal read mode does not share an existing
writer. All fixture creation and ordinary reads/writes use the real project
storage facade. The test does not change production storage behavior.

## Using the temporary Android probe

Build the main project using the pinned Unity **6000.3.17f1**, keeping the original
**Release, IL2CPP, ARM64, and Vulkan** configuration. Leave Development Build,
Autoconnect Profiler, and Deep Profiling disabled. Entering the Game scene enables
the probe automatically on Android Player; Editor and other platforms keep the
original `Shader.WarmupAllShaders()` call.

On the first game entry the probe creates this local configuration:

`Application.persistentDataPath/ShaderWarmupDiagnostics.json`

```json
{
  "Mode": "individual",
  "ShaderName": "",
  "StartIndex": 0,
  "Count": 0
}
```

- `Mode`: `individual` isolates each currently loaded shader, `all` invokes the
  original bulk API without first enumerating shaders, and `skip` omits warmup
  for an A/B test. The latter two modes require an empty name and zero range.
- `ShaderName`: optional exact, case-sensitive shader name. Duplicate names
  remain separate loaded shader identities; all matching entries are considered.
- `StartIndex`: zero-based index in the **complete** sorted inventory, ordered
  first by ordinal shader name and then instance ID. Instance IDs can change
  between process launches.
- `Count`: number of complete-inventory positions to visit; zero means all
  remaining positions. The name filter intersects this range without renumbering.
  A nonempty inventory with no selected live shader is rejected explicitly.

The file is re-read before every game entry. Options affect only this probe and
do not change saved game rendering settings. Invalid JSON, invalid selections,
and configuration or journal I/O errors intentionally abort warmup rather than
silently invoke native code without diagnostics.

Every game entry creates a unique journal under:

`Application.persistentDataPath/Logs/ShaderWarmup/`

For the usual Android package this is
`/sdcard/Android/data/net.majdata.majdataplay/files/Logs/ShaderWarmup/`.
Old sessions are preserved. The same operation markers are sent synchronously
to Unity's native logger with the `[ShaderWarmupDiagnostics]` prefix.

`SESSION` identifies the build/backend, `INVENTORY` maps indices to shader names,
and `BEGIN_SHADER` is flushed before each per-shader native call. `RETURN_SHADER`
means that CPU call returned; it does **not** guarantee asynchronous GPU work
completed. `BEGIN_ALL`/`RETURN_ALL` serve the same purpose for original bulk mode.
`MANAGED_EXCEPTION` records catchable errors, while native process crashes cannot
be caught by this C# helper. No `RETURN` or `COMPLETE` is emitted for a throwing
call.

An unfinished `BEGIN_SHADER` is only a suspect. Confirm it with repeated fresh
processes, an exact-name filter, and range/order comparisons against `all` and
`skip`. This API does not expose the specific failing Pass or keyword variant;
the probe deliberately does not inspect keyword tables or create materials/RTs.
Changing the API, order, and vertex layout may prevent the original crash from
reproducing. A missing crash is not a fix.

This is a **temporary diagnostic**, not a release optimization: per-marker disk
flushes and native logger calls can substantially lengthen loading and change
timing. Remove or explicitly disable the probe before distribution or performance
and power measurements. Preserve the journals before uninstalling or clearing
application data.

## Scope of the managed regressions

These tests verify managed control flow, deterministic selection, configuration
validation, real file visibility before the simulated native call, preservation
of previous diagnostic sessions, and failure propagation. They do **not** verify
Unity serialization, IL2CPP stripping, actual shader compilation, Vulkan render
pass setup, native crashes, or Android filesystem behavior. A successful run is
not evidence that the application crash has been fixed or that the final
unfinished shader entry is its root cause.

Last managed validation: Android Release 20 cases / 269 assertions, Android Editor
Release 2 cases / 9 assertions, and desktop Release 2 cases / 9 assertions. All
passed. Unity Player, IL2CPP, GPU, and physical-device execution remain separate
validation steps.
