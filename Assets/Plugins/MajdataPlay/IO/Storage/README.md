# Portable file and directory storage

The `MajdataPlay.IO.Storage` namespace belongs to the existing `MajdataPlay.IO` assembly.
It uses a small synchronous `IFileSystem` backend contract and typed `StorageFile` /
`StorageDirectory` handles. The local backend uses `System.IO` on Windows, Linux,
macOS, iOS, and Android. The Android-only assembly provides a SAF document backend
and cancellable system pickers, without adding an Android dependency to the portable assembly.

## Local storage

```csharp
using MajdataPlay.IO.Storage;

var library = FileSystem.CreateDirectory(localLibraryPath);
var folder = library.CreateDirectory("Charts");
var file = folder.CreateFile("notes.txt", "text/plain");
await file.WriteAllTextAsync("中文 chart", cancellationToken: cancellationToken);
var text = await file.ReadAllTextAsync(cancellationToken: cancellationToken);

foreach (var child in folder.EnumerateEntries())
{
    // Location is authoritative; Name is for display, not path construction.
}
var renamed = file.Rename("renamed.txt");
var copy = await renamed.CopyToAsync(library, "backup.txt", cancellationToken: cancellationToken);
```

`FileSystem.OpenFile(location)` and `OpenDirectory(location)` automatically select
the backend for native paths, `file://` URIs, and registered `content://` URIs.
`LocalFileSystem` is internal; `FileSystem` does not expose a local backend property.
Callers pass a location to the facade rather than constructing or selecting the
local implementation. Handles expose their backend only through `IFileSystem`.

Creating a handle does not create an entry. Handle methods `OpenWrite`, `WriteAllBytes`, and `WriteAllText` require an existing file;
use `StorageDirectory.CreateFile` first. Creating a file never overwrites a sibling;
creating a directory returns the existing directory if it already exists.
Child APIs accept one name, not paths: empty names, `.`, `..`, separators, and NUL
are rejected. The backend applies additional platform name restrictions.

Local handles are normalized to absolute paths. Relative paths are resolved when
the handle is created, not when a later operation runs. File URI queries and
fragments are not filesystem path components. OS permissions and mobile sandbox
boundaries still apply. The local backend does not provide an iOS Files picker,
security-scoped bookmarks, or access outside the iOS application sandbox.

## Unified application operations

Runtime consumers use storage handles for file queries, enumeration, reads and
writes. Editor-only tooling retains its existing `System.IO` implementation.
`Path` remains available for computing known local paths; text readers,
writers, hashing and ZIP processing consume storage streams rather than opening
filenames themselves.

Facade operations use the same public names for local paths, file URIs and content
URIs; callers do not choose a backend. `CreateDirectory(location)` creates missing
local parents or returns an existing content directory. New content entries must
use `CreateDirectory(parentLocation, name)` or
`CreateFile(parentLocation, name, mimeType, overwrite: false)` because a document
URI is opaque and cannot supply a parent location or a new child name. These
overloads also accept local parent directories and return the actual provider
location/name. They never create missing parents or overwrite a directory.

`CreateFile(location, overwrite: false)` creates an empty local file without
creating parents. For an existing content file, `overwrite: true` truncates it;
without overwrite the occupied location is rejected. `FileSystem.OpenWrite`
uses exclusive creation by default, `overwrite: true` truncates, and
`append: true` appends. Missing local files are created, while content files must
already exist. Local output streams are seekable and allow concurrent readers;
provider streams may be nonseekable. Use one output stream when creating and
writing a local file must share one open. `FileSystem.OpenRead(location)` opens
an existing readable stream through either backend.

`CopyFile(source, destination, overwrite)` uses native copying for local-to-local
transfers, retaining local metadata. For a content destination URI, the file must
already exist and overwrite must be enabled. Use
`CopyFile(source, parentLocation, name, overwrite)` to create a destination in
either backend. Provider/cross-provider copies reuse the bounded, nonseekable
stream transfer and alias guards of `StorageFile.CopyTo`; returned handles retain
provider-adjusted names and URIs. Failed overwrites may leave partial content;
newly created destinations are deleted best-effort after a failed copy. Copies
to an existing `StorageFile` handle are also available via `CopyTo(destination)`.

`MoveFile`, `MoveDirectory`, `ReplaceFile`, `SetAttributes` and `SetLastWriteTime`
resolve their backends internally. Local branches preserve native move, atomic
replacement, attribute and ZIP timestamp semantics, including volume and
permission restrictions. The current content backend contract does not support
these native operations: content/cross-provider operands throw
`NotSupportedException` before any mutation, including a content backup for
replacement. Moves and replacement never fall back to copy/delete. Content
renaming within a parent remains available through handle `Rename`.

Entry snapshots also expose optional UTC creation times and backend-reported
hidden/system flags. Local enumeration supplies them for chart ordering and
filtering; providers which do not report them retain null/false defaults.

## Android SAF

Use these APIs only in an Android player, and start a picker on Unity's main
thread after initialization. They remain declared in the Editor, where invoking
Android-only operations throws `PlatformNotSupportedException` before JNI.

```csharp
#if UNITY_ANDROID && !UNITY_EDITOR
using MajdataPlay.Platform.Android.Storage;
#endif

// In an async operation owned by a scene/component:
#if UNITY_ANDROID && !UNITY_EDITOR
var library = await AndroidStorageAccess.PickDirectoryAsync(
    writable: true,
    persistPermission: true,
    cancellationToken: cancellationToken);
if (library is null)
{
    return; // User dismissed the picker.
}
var file = library.CreateFile("notes.txt", "text/plain");
await file.WriteAllTextAsync("chart data", cancellationToken: cancellationToken);

// Save this string in application settings; it is a URI, never a native path.
var savedLocation = library.Location;
// On a later launch, after runtime initialization:
var reopened = FileSystem.OpenDirectory(savedLocation);
#endif
```

- `PickDirectoryAsync` uses `ACTION_OPEN_DOCUMENT_TREE`.
- `PickFileAsync` uses `ACTION_OPEN_DOCUMENT` with an openable MIME filter.
- `CreateFileAsync` uses `ACTION_CREATE_DOCUMENT`; the system may change the proposed name.
- The picker validates the returned read/write grant, and persists only the flags
  Android actually granted when persistence was requested. A provider without
  persistable access fails explicitly rather than silently promising restart access.
- `GetPersistedPermissions()` returns original grant URIs, access flags, and UTC
  timestamps. `ReleasePermission(originalGrantUri)` releases held read/write flags.
  Release the tree grant URI, not the URI of an arbitrary descendant.
- A saved URI alone is not an authorization. Providers can revoke grants, remove
  documents, go offline, or refuse operations. Handle `UnauthorizedAccessException`,
  `IOException`, and `NotSupportedException` at the application boundary.
- Cancellation requested before result commitment completes the Task with
  cancellation. Once permission/result commitment begins, its result or error wins
  concurrent cancellation, so a persisted grant is not hidden behind a cancelled Task.
  Cancellation does not forcibly close Android's UI. Dismiss the outstanding picker before requesting another;
  a late result for an already-cancelled request is ignored. App shutdown cancels
  outstanding requests. User dismissal returns null, not cancellation.
- The backend registers automatically before scenes load. An application with its
  own storage provider can explicitly register another `IFileSystem`; existing
  handles retain their original backend.

The Java bridge uses framework `ContentResolver` and `DocumentsContract` APIs,
not AndroidX or broad storage access. No manifest permission is added. Do not use
`Path.Combine`, `Path.GetFileName`, filesystem watchers, or native fd assumptions
on content URIs. Tree document IDs are provider-owned and can contain encoded
slashes. Find/create/enumerate children through `StorageDirectory`; the backend
constructs URIs through `DocumentsContract` and returns the actual new URI after rename.

If Android Java minification is enabled in a future build configuration, preserve
the JNI bridge names and protocol fields (current project settings disable minification):

```proguard
-keep class net.majdata.majdataplay.StorageAccess { *; }
-keep class net.majdata.majdataplay.StorageAccess$* { *; }
```

The existing managed Activity-result proxy callback is marked `Preserve` for
reflection-based JNI dispatch. These annotations and rules do not replace IL2CPP
Player/device validation.

## Semantics and limits

- Entry metadata is a snapshot. Optional `ResourceId` identifies a resource
  across different grant URI aliases and is never a location to open. `Entry` returns
  null only for absence, not for a permission failure. `Exists` can throw on inaccessible providers.
- Sizes and modification times may be unknown; file size is `long?`. No provider
  stream is assumed to be seekable or to expose length/position. Virtual documents
  without a normal binary representation are not converted automatically.
- Enumeration is immediate-child only. Recursive workflows must explicitly decide
  how to handle links, cycles, and provider latency. Local recursive deletion does
  not follow directory links into another tree. Nonrecursive deletion refuses a
  nonempty directory. Recursive deletion is not hardened against concurrent path
  substitution; only delete trees whose mutation you control.
- `Rename` is within the same parent and returns a new handle; the old handle may
  become invalid. Do not keep using an old URI after a provider changes it.
- `CopyTo` / `CopyToAsync` stream between any two backends without whole-file
  buffering or seeking. Copy rejects matching locations/resource identities and
  overwriting a local symbolic link. Hard links and aliases through linked ancestor
  directories cannot be reliably identified by this portable backend. Case-insensitive
  volume aliases outside Windows also require care. Copy is not atomic; an overwrite can leave the existing destination truncated or partially written on failure.
  A newly created destination is removed best-effort after a failed/cancelled copy;
  an existing overwritten destination is never removed as cleanup.
- Providers may auto-adjust creation/rename names and have different capabilities.
  Always use the returned handle and metadata, not an assumed child URI.
- Reading all bytes/text intentionally buffers the whole file; use streams for large
  media. Synchronous enumeration/metadata/create/rename/delete can block. Move slow
  storage work away from Unity's game loop; never access Unity objects from that worker.
- APIs such as native
  audio/video decoders that require a real path must first copy a SAF document into
  app-local storage. A content URI is not a replacement native filename.

## Validation

```powershell
dotnet run --project Tools/Tests/FileSystemValidation/FileSystemValidation.csproj
./Tools/Tests/ValidateFileSystemUnity.ps1
```

The standalone suite links production storage sources and a nonseekable, opaque-URI
provider double. The isolated Unity script first builds/installs the production
Java analyzer, then stages the real `AndroidRuntime` event bridge, attributes,
all production `Runtime/` wrapper categories, analyzer metadata, Java extractor,
and custom `StorageAccess.java` at their project-relative paths. Only unrelated
logging/keyboard initialization uses narrow doubles; generated C# remains inside
compilation and is not committed as assets. The selected Editor's Android SDK
API 36 and OpenJDK are required even for Editor/desktop-only checks.

It verifies Editor local smoke/Android guards, generated storage signatures and
signed-byte/typed-result mappings, managed-only borrowing/disposal regressions,
null-envelope rejection, no-JNI picker cancellation/late-result and deterministic
commit/launch concurrency regressions, and requested Player script compilation.
It also compiles the Java sources against the bundled Android SDK and Unity classes.
See `Tools/Tests/FileSystemValidation/README.md` for staging, coverage and log markers.
It does not build an APK, run JNI, verify native reference lifetimes/provider
resource closure, exercise IL2CPP, or validate a real document provider.
For all five installed targets, run:

```powershell
./Tools/Tests/ValidateFileSystemUnity.ps1 -PlayerTargets StandaloneWindows64,StandaloneLinux64,StandaloneOSX,iOS,Android
```

Pass `-PlayerTargets` to select installed Unity target modules, or
`-SkipJavaCompilation` to skip the separate Java bytecode check; analyzer
installation and Java metadata extraction still require the SDK/JDK.

Before release, test a real Android device with local and cloud providers: selection,
user dismissal, owner cancellation/late results, concurrent request rejection,
read-only grants, revocation, persistence after restart, nested trees and escaped
IDs, unknown metadata, large transfers, append/truncate, creation name adjustments,
rename URI changes, deletion, stream failures, and both ARMv7/ARM64 player backends.
Windows/Linux/macOS/iOS runtime and platform permissions still need their respective
platform smoke checks. Script compilation alone does not establish runtime correctness.

## Platform references

- Android: https://developer.android.com/training/data-storage/shared/documents-files
- DocumentsContract: https://developer.android.com/reference/android/provider/DocumentsContract
- Scoped JNI thread attachment: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AndroidJNI.InvokeAttached.html
