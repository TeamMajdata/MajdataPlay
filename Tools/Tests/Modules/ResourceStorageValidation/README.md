# Resource storage migration validation

This independent, package-free **.NET 9** executable links the production storage
implementation, the resource importer and checkers under
`Assets/Scripts/Misc/ResourceUpdate` (`MobileResourceImporter`, `MobileResourceChecker`,
`MobileManagedAssetMover`, `DesktopResourceChecker` and their shared helpers),
`ProgressReporter`, and the shared `StorageDirectoryTransfer` helper, plus the in-memory content provider double from
`FileSystemValidation` for cross-backend cases. It does
not build Unity's generated solution or project files. Output and fixtures stay
under the ignored `Temp/ResourceStorageValidation/` directory.

Run from the repository root:

```powershell
dotnet run --project Tools/Tests/Modules/ResourceStorageValidation/ResourceStorageValidation.csproj
dotnet run --project Tools/Tests/Modules/ResourceStorageValidation/ResourceStorageValidation.csproj -p:MobileStorageTarget=Android
```

The default selects the iOS packaged-file branch. The second command selects the
Android branch, whose mobile cases read compressed entries from a temporary APK-shaped
ZIP archive through production `jar:file://...!/assets` storage. Both also compile
and exercise the Windows restorer against a native package directory. Narrow
substitutes provide only Unity paths, logging, manifest dictionaries, and a
`FrameBudget` that never yields, so the asynchronous workflows complete synchronously. Packaged reads use production storage with no request
transport substitute. Manifest JSON parsing and Android filesystem permissions
are outside this executable's scope.

Cases cover empty/nonempty desktop chart and skin roots, copy timestamps and
Windows hidden attributes; initial mobile
extraction and directory moves; literal URI delimiters, percent sequences, spaces,
and Unicode in packaged paths; existing player-managed trees; incomplete
extraction retry; official hash-gated updates; preserved customizations,
deletions, and new player-owned files; missing unmanaged resources; packaged
hash rejection; pending move markers; atomic file replacement; temporary
file cleanup when destination commitment fails; and managed moves whose chart and
skin roots live on a content provider: the tree crosses backends with its file
timestamps, an existing provider tree is preserved, and an interrupted transfer
removes its temporary copy, keeps the extracted source and marker, and completes
on the next start; and the progress reports of mobile extraction, the later mobile
check, and the desktop restore.

Each case prints `PASS` or `FAIL`, followed by `RESOURCE_STORAGE_VALIDATION_PASSED`
or `RESOURCE_STORAGE_VALIDATION_FAILED`. Managed results do not establish Unity
lifecycle, Mono/IL2CPP, installed APK access, Player, device, other-host native file
replacement, or Android provider behavior.
Chart ZIP extraction still needs a Unity smoke test with valid, nested, and
traversal archives, plus hidden/system folder filtering on Windows.
