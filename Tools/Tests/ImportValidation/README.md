# Chart import layout validation

This package-free .NET 9 executable links the production
`Assets/Scripts/Misc/Import/ChartImporter.cs` together with the production
`MajdataPlay.IO.Storage` sources. It verifies the import layout rules of the
unity `Import` method itself, not a reimplementation of them.

Run from anywhere inside the repository:

```powershell
dotnet run --project Tools/Tests/ImportValidation/ImportValidation.csproj
```

Build outputs and temporary fixture directories stay under the ignored
`Temp/ImportValidation/` directory. Restore uses this project's directory as its
package source and has no package references.

The cases verify that an archive holding `<chart>/maidata.txt` imports that chart
into the `Import` collection, that a `<collection>/<chart>/maidata.txt` layout
creates or reuses the named collection, that a colliding chart name is renamed
instead of overwritten, that a queued directory follows the same rules while a
queued chart folder imports as one chart, that entries without a chart file and
track file import nothing, that a failed archive is reported without discarding
the remaining queue, and that no extraction directory survives an import.

`Stubs.cs` supplies narrow scene, storage-refresh, diagnostic, UniTask and Unity
attribute substitutes. These tests verify the importer's control flow and file
effects, not production scene switching, `SongStorage.RefreshLocalAsync`, Unity
threading, Android SAF sources, the Android temp-file deletion, native archives
or Player integration. Those require the corresponding Unity and platform smoke
checks.
