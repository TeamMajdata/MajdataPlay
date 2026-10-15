# Portable file and directory storage

The `MajdataPlay.IO.Storage` namespace belongs to the existing `MajdataPlay.IO` assembly.
It uses a small synchronous `IFileSystem` backend contract and typed `StorageFile` /
`StorageDirectory` handles. The local backend uses `System.IO` on Windows, Linux,
macOS, iOS, and Android. The Android-only assembly provides a SAF document backend
and cancellable system pickers, without adding an Android dependency to the portable assembly.

File operations live on the nested `FileSystem.File` and directory operations on
`FileSystem.Directory`; the facade itself only selects the backend and keeps
`RegisterContentProvider`. Method names dropped the redundant word, so
`FileSystem.OpenFile` is now `FileSystem.File.Open` and `FileSystem.CreateDirectory`
is now `FileSystem.Directory.Create`. Attribute and timestamp writes are handle methods
(`StorageFile.SetAttributes` / `SetLastWriteTime` and their `StorageDirectory` counterparts).

## Local storage

```csharp
using MajdataPlay.IO.Storage;

var library = FileSystem.Directory.Create(localLibraryPath);
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

`FileSystem.File.Open(location)` and `FileSystem.Directory.Open(location)` automatically select
the backend for native paths, `file://` URIs, read-only `jar:file://...!/` URIs,
and registered `content://` URIs.
`LocalFileSystem` is internal; `FileSystem` does not expose a local backend property.
Callers pass a location to the facade rather than constructing or selecting the
local implementation. Handles expose their backend only through `IFileSystem`.

Creating a handle does not create an entry. Handle methods `OpenWrite`, `WriteAllBytes`, and `WriteAllText` require an existing file;
use `StorageDirectory.CreateFile` first. Creating a file never overwrites a sibling;
creating a directory returns the existing directory if it already exists.
Child APIs accept one name, not paths: empty names, `.`, `..`, separators, and NUL
are rejected. The backend applies additional platform name restrictions.
`FindEntry(name)` locates an immediate child of either kind.

## Relative paths

Application code never derives a child location from `Location` with `Path.Combine`:
a content URI is opaque, so the combined string names nothing. A child that may
not exist yet has no portable location at all. Resolve children through the
parent handle instead, and let the backend supply the authoritative location:

```csharp
var charts = root.CreateDirectory("MaiCharts");          // idempotent, works on every backend
var settings = root.FindFile("settings.json")            // null while absent
    ?? root.CreateFile("settings.json", "application/json");
var sfx = assets.FindFileAt("SFX/calibrate.ogg");        // descendant lookup, null when any level is missing
var skins = assets.FindDirectoryAt("Skins/default");
var nested = root.CreateDirectoryAt("Cache/Runtime");    // creates each missing level in turn

var names = FileSystem.Path.Split(archiveEntry.FullName);    // validated child names, never a location
var parent = root.CreateDirectoryAt(names.AsSpan(0, names.Length - 1));
var file = FileSystem.File.Create(parent.Location, names[names.Length - 1], overwrite: true);
```

`FileSystem.Path.Split` accepts `/` and `\` as separators, drops empty and `.`
segments, and rejects rooted paths, URIs, `..`, and NUL, so an archive entry or
manifest path can be walked without a `Path.GetFullPath` prefix check. The
descendant APIs resolve one level per backend query; keep them off the game
loop on provider storage, where each level is a provider round trip.
`Path` remains appropriate for computing a known native path from a Unity
application path such as `Application.persistentDataPath`, which is then
opened once through the facade.

Local handles are normalized to absolute paths. Relative paths are resolved when
the handle is created, not when a later operation runs. File URI queries and
fragments are not filesystem path components. OS permissions and mobile sandbox
boundaries still apply. The local backend does not provide an iOS Files picker,
security-scoped bookmarks, or access outside the iOS application sandbox.

## Unified application operations

Runtime consumers use storage handles for file queries, enumeration, reads and
writes. Editor-only tooling retains its existing `System.IO` implementation.
`Path` remains available for computing known local paths from Unity application
paths, never from a handle's `Location`; text readers, writers, hashing and ZIP
processing consume storage streams rather than opening filenames themselves.

Facade operations use the same public names for local paths, file URIs and content
URIs; callers do not choose a backend. `CreateDirectory(location)` creates missing
local parents or returns an existing content directory. New content entries must
use `FileSystem.Directory.Create(parentLocation, name)` or
`FileSystem.File.Create(parentLocation, name, mimeType, overwrite: false)` because a
document URI is opaque and cannot supply a parent location or a new child name.
These overloads also accept local parent directories and return the actual provider
location/name. They never create missing parents or overwrite a directory.

`FileSystem.File.Create(location, overwrite: false)` creates an empty local file without
creating parents. For an existing content file, `overwrite: true` truncates it;
without overwrite the occupied location is rejected. `FileSystem.File.OpenWrite`
uses exclusive creation by default, `overwrite: true` truncates, and
`append: true` appends. Missing local files are created, while content files must
already exist. Local output streams are seekable and allow concurrent readers;
provider streams may be nonseekable. Use one output stream when creating and
writing a local file must share one open. `FileSystem.File.OpenRead(location)` opens
an existing readable stream through either backend.

`FileSystem.File.Copy(source, destination, overwrite)` uses native copying for
local-to-local transfers, retaining local metadata. For a content destination URI,
the file must already exist and overwrite must be enabled. Use
`FileSystem.File.Copy(source, parentLocation, name, overwrite)` to create a destination
in either backend. Provider/cross-provider copies reuse the bounded, nonseekable
stream transfer and alias guards of `StorageFile.CopyTo`; returned handles retain
provider-adjusted names and URIs. Failed overwrites may leave partial content;
newly created destinations are deleted best-effort after a failed copy. Copies
to an existing `StorageFile` handle are also available via `CopyTo(destination)`.

`FileSystem.File.Move`, `FileSystem.Directory.Move` and `FileSystem.File.Replace`
resolve their backends internally, and none of them falls back to copying and
deleting. A location-based move requires native local operands, because an opaque
document URI cannot name a destination that does not exist yet. Use
`FileSystem.File.Move(source, parentLocation, name)` or
`FileSystem.Directory.Move(source, parentLocation, name)`
to move an entry inside one backend: the local backend renames natively, and the
content backend performs one provider move. Both overloads return the authoritative
destination handle and reject a source of the wrong type. Cross-backend moves and
moves into the source's own subtree are refused before mutation.

`FileSystem.File.Replace` keeps native atomic replacement, including a backup, when
every operand is local. When the source or destination belongs to a provider, the
destination must already exist: the source is streamed into it and then deleted,
so the destination keeps its storage identity while the source is consumed. That
provider replacement is not atomic, can leave the destination truncated on
failure, never deletes the source on failure, and rejects a backup path before
mutating anything. `StorageFile.SetAttributes` and `StorageDirectory.SetAttributes`
apply native flags to local entries and are accepted without effect for provider
entries, which have no portable hidden or system flags; the entry must exist.

`StorageFile.SetLastWriteTime` and `StorageDirectory.SetLastWriteTime` write native
local timestamps, or ask a provider to store the UTC value. Providers may reject the
update, in which case `NotSupportedException` is reported, and they may store a rounded
or provider-owned value, so re-read the metadata instead of assuming the requested time
was kept. Content renaming within a parent remains available through handle `Rename` and
does not require an existing destination.

Entry snapshots also expose optional UTC creation times and backend-reported
hidden/system flags. Local enumeration supplies them for chart ordering and
filtering; providers which do not report them retain null/false defaults.

## Native-only data

Native libraries such as SQLite open files by filename only, so the data they own
must stay on native storage even when the application root is a provider location:

- `MajEnv.PersistentDirectory` is always a native path: it is the root on Windows,
  Linux and macOS, and the application-private persistent data directory on
  Android and iOS whatever the root is. The score database `MajScores.db` always
  lives there, so on a device whose default root is that directory, switching
  between the default root and a picked SAF tree never changes where scores are
  stored.
- When the persistent directory holds no non-empty database and the root is a
  different directory, a `MajScores.db` found in the root (a SAF tree, or the
  legacy shared folder on Android 10 and earlier) is streamed once with
  `StorageFile.CopyToAsync` into a temporary sibling, which then replaces an empty
  persistent database by rename. The copy in the root stays in place as a backup:
  it is never read again while a non-empty persistent database exists, and a
  failed import leaves the live name untouched. The legacy JSON score file is
  still read from the root through its backend when no database exists anywhere.
- The favorites database already lives below the always-native cache directory.

## Directory trees across backends

The facade never emulates a directory move by copying. Application code that must
move a tree between backends uses the internal `StorageDirectoryTransfer` helper
in the game assembly. `Copy(source, destination)` walks a tree iteratively through
handles, overwrites files with the same name, and keeps file modification times
where the destination stores them. `Move(source, parent, name)` performs the
facade's native move inside one backend; across backends it copies the tree below
a temporary sibling name, renames it to the requested name, and deletes the source
only afterwards. An interrupted transfer keeps the source and never leaves a
partial tree under the final name, and the temporary copy is removed best-effort.
Bundled chart and skin extraction and chart import both use it, so a chart root
on an Android SAF tree receives the packaged resources from the native extraction
directory, and a failed transfer is retried on the next start through the pending
move marker. `Tools/Tests/ResourceStorageValidation` covers the cross-backend move,
preservation and retry paths against the in-memory content provider.

## Packaged APK/JAR resources

`FileSystem` recognizes Unity Android streaming asset locations such as
`jar:file:///data/app/example/base.apk!/assets/Config/options.txt`. The portable,
read-only backend opens the local ZIP/APK with `System.IO.Compression.ZipArchive`;
it requires neither UnityWebRequest nor JNI or a registered Android provider.
It also works with local ZIP fixtures in the Editor and on desktop hosts.

```csharp
var packagedAssets = FileSystem.Directory.Open(Application.streamingAssetsPath);
var config = packagedAssets.FindDirectory("Config")!.FindFile("options.txt")!;
var text = await config.ReadAllTextAsync(cancellationToken: cancellationToken);
var extracted = await config.CopyToAsync(
    writableDirectory, "options.txt", cancellationToken: cancellationToken);
// Pass extracted.Location to a native library that requires a local filename.
```

Reads stream and decompress a single entry; copying through the existing file
APIs extracts it into local storage or another writable provider. Directory
lookup and immediate enumeration include implicit ZIP directories. Each open
stream independently owns its archive and closes it on disposal. Metadata and
directory queries read the archive afresh; perform substantial packaged I/O off
the game loop, as with the other synchronous storage backends.

ZIP entry names use ordinal, case-sensitive comparison. URI path segments are
percent-decoded exactly once: a literal percent sign must be encoded as `%25`.
Use directory lookup by child name to obtain authoritative escaped locations
for spaces, Unicode, `#`, `?`, or names that look like encoded separators.
Do not combine jar locations with `Path`. Nested archives, non-file archive
URLs, URI queries/fragments, traversal, and encoded path separators are rejected.
Unsafe archive records are not exposed; ambiguous duplicate names are errors.

Packaged entries cannot be created, written, renamed, moved, deleted, or have
their attributes/timestamps changed. Copying out is supported; replacement
rejects archive operands before changing the destination because it consumes
the source. As with other backends, an idempotent directory create may return
an already existing directory without changing it.

The isolated Unity validation also executes compressed APK reads and extraction
in the Editor's Mono runtime, including escaped names, archive stream disposal,
and replacement refusal. Run `./Tools/Tests/ValidateFileSystemUnity.ps1 -PlayerTargets Android -SkipJavaCompilation`
to include Android storage assembly script compilation. The jar smoke marker is
`FILE_SYSTEM_JAR_EDITOR_SMOKE_PASSED`; this does not run an Android player or device.

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
var reopened = FileSystem.Directory.Open(savedLocation);
#endif
```

- `PickDirectoryAsync` uses `ACTION_OPEN_DOCUMENT_TREE`.
- `PickFileAsync` uses `ACTION_OPEN_DOCUMENT` with an openable MIME filter.
- `CreateFileAsync` uses `ACTION_CREATE_DOCUMENT`; the system may change the proposed name.
- Provider moves use `DocumentsContract.moveDocument` inside one provider authority, and a
  same-parent move reuses the collision-safe rename path. A single-document grant from
  `PickFileAsync` or `CreateFileAsync` cannot be moved or renamed because its parent is not
  discoverable, and cross-provider moves are refused before any mutation.
- `StorageFile.SetLastWriteTime` and `StorageDirectory.SetLastWriteTime` write `COLUMN_LAST_MODIFIED`
  through the content resolver. A provider
  that rejects the update reports `NotSupportedException`; a provider that accepts it may
  still store a rounded or provider-owned value, so re-read the entry instead of assuming
  the requested time. Attribute flags are not writable through SAF and are a no-op.
- Deleting a tree document makes its URI unresolvable: a later metadata query for that URI
  can fail with `IOException` instead of reporting absence, on a real `ExternalStorageProvider`
  because it can no longer decide whether the document belongs to the tree. Confirm removal
  through the parent directory rather than through the deleted URI.
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

## Android stream capabilities

SAF read streams and explicit truncate (`wt`) write streams support `Seek`,
`Length`, and getting/setting `Position` only when the **opened descriptor** can
seek and exposes a reliable logical length. Check `CanSeek`; a document metadata
size or provider flag does not establish these capabilities. An unknown declared
asset length can still be resolved from the opened regular file's current size.
Whole-file write lengths are refreshed after writes rather than cached metadata.

Positions start at zero for the document's asset range. A nonzero asset start
and declared slice length are preserved: reads cannot expose the surrounding
file, and writes cannot cross a declared asset boundary. Begin/current/end seeks
and returned positions use signed 64-bit Java/JNI values on both Android ABIs.
A seek past logical EOF is allowed; subsequent reads return EOF. Negative and
overflowing positions fail without intentionally moving the descriptor.

Pipes, unknown-length resources which cannot supply complete seek semantics,
and append (`wa`) streams retain sequential operations. Append does not emulate
its mode by seeking and cannot become overwrite through `Position`. Unsupported
seek/length/position operations throw `NotSupportedException`; `SetLength` remains
unsupported for all SAF streams. No whole-file cache or increased read/write URI
permission is introduced to manufacture seeking support. Dispose every stream
deterministically, including after cancellation.

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
rename URI changes, deletion, stream failures, same-parent and cross-directory moves
with collision and unsupported-flag handling, modification-time acceptance and
rejection, and both ARMv7/ARM64 player backends.
Windows/Linux/macOS/iOS runtime and platform permissions still need their respective
platform smoke checks. Script compilation alone does not establish runtime correctness.

## Platform references

- Android: https://developer.android.com/training/data-storage/shared/documents-files
- DocumentsContract: https://developer.android.com/reference/android/provider/DocumentsContract
- Scoped JNI thread attachment: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AndroidJNI.InvokeAttached.html
