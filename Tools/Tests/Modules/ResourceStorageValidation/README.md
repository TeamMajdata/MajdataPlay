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
`FrameBudget` that never yields. Initial mobile extraction uses the production
bounded background workers. A test-only completed-copy log hook gates workers to
check their overlap, worker limit, and completion after cancellation or a throwing
progress callback. Another case pumps a calling synchronization context and guards
Unity's packaged path against background access. Packaged reads use production
storage with no request transport substitute. Manifest JSON parsing and Android
filesystem permissions are outside this executable's scope.

Cases cover empty/nonempty desktop chart and skin roots, copy timestamps and
Windows hidden attributes; initial mobile
extraction and directory moves; concurrent extraction into shared nested parents,
ordered progress on the calling context, main-thread Unity path access, the
processor/four-worker limit, cancellation before launch and from progress, draining
in-flight workers after cancellation or progress failure, hash rejection without
commitment, and immediate retry from leftover staging; literal URI delimiters,
percent sequences, spaces, and Unicode in packaged paths; existing player-managed trees; incomplete
extraction retry; official hash-gated updates; preserved customizations,
deletions, and new player-owned files; missing unmanaged resources; packaged
hash rejection; pending move markers; atomic file replacement; temporary
file cleanup when destination commitment fails; and managed moves whose chart and
skin roots live on a content provider: the tree crosses backends with its file
timestamps, an existing provider tree is preserved, and an interrupted transfer
removes its temporary copy, keeps the extracted source and marker, and completes
on the next start; and the progress reports of mobile extraction, the later mobile
check, and the desktop restore.

Run the two target variants sequentially because they share the validation work
and cleanup root. Each case prints `PASS` or `FAIL`, followed by `RESOURCE_STORAGE_VALIDATION_PASSED`
or `RESOURCE_STORAGE_VALIDATION_FAILED`. Managed results do not establish Unity
lifecycle, Mono/IL2CPP, installed APK access, Player, device, other-host native file
replacement, or Android provider behavior.
Separate stress diagnostics on the Windows validation host observed intermittent
`Access denied` errors during rapid native nested-directory moves with both the
original serial importer and the parallel importer. This host behavior does not
establish whether real mobile targets are affected.
Chart ZIP extraction still needs a Unity smoke test with valid, nested, and
traversal archives, plus hidden/system folder filtering on Windows.
