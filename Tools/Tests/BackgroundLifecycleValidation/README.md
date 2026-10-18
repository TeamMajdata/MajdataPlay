# Background lifecycle validation

Requires .NET 9. Run from the repository root:

```powershell
dotnet run --project Tools/Tests/BackgroundLifecycleValidation/BackgroundLifecycleValidation.csproj -c Release
```

The executable compiles the complete production `BGManager.cs` and `Majdata.cs`
against narrow managed Unity, FFmpeg, and UniTask substitutes. It invokes the
production Awake and OnDestroy paths and checks ownership of the single private
background material shared by the cover and video graphics.

Cases cover normal destruction, decoder Close failures, incomplete initialization,
repeated destruction, preservation of an already registered successor manager,
and singleton cleanup even if material destruction throws. Original UI materials
must never be destroyed. Failed partial Awake initialization is also exercised.

Build artifacts stay under ignored `Temp/BackgroundLifecycleValidation/`.
These checks exercise managed cleanup control flow only. They do not run Unity,
render a frame, decode video, or validate actual native GPU resource destruction.
