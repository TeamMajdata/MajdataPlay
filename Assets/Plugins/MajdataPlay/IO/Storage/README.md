# Portable file and directory storage

The `MajdataPlay.IO.Storage` namespace belongs to the existing `MajdataPlay.IO` assembly.
It uses a small synchronous `IFileSystem` backend contract and typed `StorageFile` /
`StorageDirectory` handles. The local backend uses `System.IO` on Windows, Linux,
macOS, iOS, and Android. The Android-only assembly provides a SAF document backend
and cancellable system pickers, without adding an Android dependency to the portable assembly.

## Local storage

```csharp
using MajdataPlay.IO.Storage;

var library = FileSystem.CreateLocalDirectory(localLibraryPath);
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

Creating a handle does not create an entry. `OpenWrite`, `WriteAllBytes`, and `WriteAllText` require an existing file;
use `StorageDirectory.CreateFile` first. Creating a file never overwrites a sibling;
creating a directory returns the existing directory if it already exists.
Child APIs accept one name, not paths: empty names, `.`, `..`, separators, and NUL
are rejected. The backend applies additional platform name restrictions.

Local handles are normalized to absolute paths. Relative paths are resolved when
the handle is created, not when a later operation runs. File URI queries and
fragments are not filesystem path components. OS permissions and mobile sandbox
boundaries still apply. The local backend does not provide an iOS Files picker,
security-scoped bookmarks, or access outside the iOS application sandbox.

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
- This layer does not migrate existing `System.IO` consumers. APIs such as native
  audio/video decoders that require a real path must first copy a SAF document into
  app-local storage. A content URI is not a replacement native filename.

## Validation

```powershell
dotnet run --project Tools/Tests/FileSystemValidation/FileSystemValidation.csproj
./Tools/Tests/ValidateFileSystemUnity.ps1
```

The standalone suite links production storage sources and a nonseekable, opaque-URI
provider double. The isolated Unity script stages only this layer and the real
`AndroidRuntime` event bridge, with narrow logging/keyboard initialization doubles;
it verifies Editor local smoke/Android guards, no-JNI picker cancellation/late-result
and deterministic commit/launch concurrency regressions, and requested player script compilation.
It also compiles the Java sources against the bundled Android SDK and Unity classes.
It does not build an APK, run JNI, exercise IL2CPP, or validate a real document provider.
For all five installed targets, run:

```powershell
./Tools/Tests/ValidateFileSystemUnity.ps1 -PlayerTargets StandaloneWindows64,StandaloneLinux64,StandaloneOSX,iOS,Android
```

Pass `-PlayerTargets` to select installed Unity target modules, or
`-SkipJavaCompilation` when intentionally testing only C# compilation.

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
