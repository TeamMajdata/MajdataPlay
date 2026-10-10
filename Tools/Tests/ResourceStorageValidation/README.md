# Resource storage migration validation

This independent, package-free **.NET 9** executable links the production storage
implementation, `MobileResourceUpdater`, and `DesktopResourceRestorer`. It does
not build Unity's generated solution or project files. Output and fixtures stay
under the ignored `Temp/ResourceStorageValidation/` directory.

Run from the repository root:

```powershell
dotnet run --project Tools/Tests/ResourceStorageValidation/ResourceStorageValidation.csproj
dotnet run --project Tools/Tests/ResourceStorageValidation/ResourceStorageValidation.csproj -p:MobileStorageTarget=Android
```

The default selects the iOS packaged-file branch. The second command selects the
Android packaged-request branch. Both also compile and exercise the Windows
restorer, using production local storage operations. Narrow substitutes provide
Unity paths, logging, manifest dictionaries, and (for Android) a completed
`UnityWebRequest` that reads a test fixture. Manifest JSON parsing and Android
networking/JNI behavior are outside this executable's scope.

Cases cover empty/nonempty desktop chart and skin roots, copy timestamps and
Windows hidden attributes; initial mobile
extraction and directory moves; existing player-managed trees; incomplete
extraction retry; official hash-gated updates; preserved customizations,
deletions, and new player-owned files; missing unmanaged resources; packaged
hash rejection; pending move markers; atomic file replacement; and temporary
file cleanup when destination commitment fails.

Each case prints `PASS` or `FAIL`, followed by `RESOURCE_STORAGE_VALIDATION_PASSED`
or `RESOURCE_STORAGE_VALIDATION_FAILED`. Managed results do not establish Unity
lifecycle, Player, device, other-host native file replacement, or Android provider behavior.
Chart ZIP extraction still needs a Unity smoke test with valid, nested, and
traversal archives, plus hidden/system folder filtering on Windows.
