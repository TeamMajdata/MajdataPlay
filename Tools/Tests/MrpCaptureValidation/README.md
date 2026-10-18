# MRP capture bridge validation

Run from the repository root with .NET 9:

```powershell
dotnet run --project Tools/Tests/MrpCaptureValidation/MrpCaptureValidation.csproj
```

This package-free executable links the production `MajCameraCaptureBridge.cs`
and supplies narrow Unity type stubs. Build output goes to the repository's
ignored `Temp/MrpCaptureValidation` directory, outside `Assets`.

The checks cover shared empty snapshots, delegate deduplication, cached immutable
snapshots across registration changes, callbacks that remove themselves, camera
isolation, removing a destroyed Unity camera reference, releasing the last camera
key, invalid registrations, runtime subsystem reset, and zero managed allocation
during steady-state snapshot lookup and callback dispatch.

These are managed registry regressions only. They do not verify Unity lifecycle
callbacks, MRP render-path selection, graphics command ordering, MSAA resolve,
render scale, viewport mapping, pixel orientation, GPU readback, FFmpeg encoding,
or Player/device behavior. Validate those integration layers separately using
Unity 6000.3.17f1 and the relevant graphics API and hardware.
