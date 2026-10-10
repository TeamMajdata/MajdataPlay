# Chart storage migration validation

This package-free .NET 9 executable links the production `SongCollection`,
`ChartSettingStorage`, and `MajdataPlay.IO.Storage` sources. It is independent of
the Unity project's generated solution and project files.

Run from the repository root:

```powershell
dotnet run --project Tools/Tests/ChartStorageValidation/ChartStorageValidation.csproj
```

Build outputs and temporary fixture directories stay under the ignored
`Temp/ChartStorageValidation/` directory. Restore uses this project's directory as
its package source and has no package references.

The cases verify collection ID creation, persistence and recovery from malformed
IDs, rejection of a file in place of the marker directory, sibling backups of
malformed chart settings that preserve source modification times, first-time settings file creation and repeated writes
which truncate rather than append.

`Stubs.cs` supplies narrow game-model, logging, numeric/collection extension and
UniTask substitutes. Its JSON serializer uses `System.Text.Json`; these tests
verify the storage consumers' control flow and file effects, not production
Newtonsoft.Json options, Unity threading, scene lifetimes, native audio/video,
online download resumption, Android SAF or Player integration. Those require
the corresponding Unity and platform smoke checks.
