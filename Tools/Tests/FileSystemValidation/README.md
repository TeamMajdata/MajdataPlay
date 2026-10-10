# File system managed regression validation

This is an **independent, package-free .NET 9 executable**, not a build of the
Unity project's generated solution or project files. It links
`Assets/Plugins/MajdataPlay/IO/Storage/*.cs` directly, including the production
contracts, local backend, facade, file handles, and directory handles. It does
not replace those implementations with stubs and requires no Unity assemblies.

## Run

From the repository root, with a .NET 9 SDK/runtime (or a newer SDK capable of
targeting .NET 9 and the .NET 9 runtime) installed:

```powershell
dotnet run --project Tools/Tests/FileSystemValidation/FileSystemValidation.csproj
```

Restore has no package references and uses only this directory as its package
source; it does not implicitly fetch dependencies or update packages. The
project sets `BaseIntermediateOutputPath` **before importing SDK.props**, with
all build intermediates and executables under the ignored
`Temp/FileSystemValidation/obj/` and `Temp/FileSystemValidation/bin/` directories.
There must be no `bin/` or `obj/` beneath this source directory.

The executable runs cases sequentially and reports each `PASS`, `FAIL`, or
capability `SKIP`, followed by `FILESYSTEM_VALIDATION_PASSED` (exit code 0) or
`FILESYSTEM_VALIDATION_FAILED` (exit code 1), with case and assertion counts.
Failures include exception types, assertion messages, and stack traces; a
failure in one case does not prevent the other cases from running.

## Coverage

- Local backend encapsulation (internal type, no public `FileSystem.Local`),
  automatic native-path/file-URI selection through the public facade, and shared
  local backend ownership.
- Local path and facade dispatch, nested local directory creation, idempotent
  directory creation, UTF/non-ASCII names, and percent-encoded `file://` URIs.
- Application local output: exclusive creation, explicit overwrite, append,
  seekable streams and concurrent read sharing; native file/directory moves,
  copy timestamps, atomic replacement with a backup, UTC creation times,
  Windows hidden/system flags, all through the unified facade operations.
- Unified `CreateDirectory`, `CreateFile`, `OpenRead`, `OpenWrite` and `CopyFile`
  dispatch for local paths, file URIs and registered content URIs. Provider
  child creation/copy retains assigned names and opaque authoritative IDs;
  existing documents require explicit overwrite or append before writable opens.
  Both copy directions support non-seekable streams with unknown lengths.
- Unified facade self-copy rejection for exact document URIs and distinct grant
  aliases sharing a global `ResourceId`; failed new copies clean up after stream
  closure, while failed/cancelled existing-location copies retain their target
  identity. Pre-cancellation and denied queries must precede writable opens.
- Unified provider mutations: provider and same-parent moves that return the new
  authoritative identity, wrong-type source rejection, sibling collisions that
  preserve both entries, directory moves that carry their children, streamed
  provider replacement that consumes the source and keeps the destination
  identity, rejected self-replacement, cross-backend replacement in both
  directions, attribute requests accepted without a provider mutation, and
  modification-time writes including local-kind conversion and provider rejection.
- Location-based moves and provider replacements with a backup operand fail
  before mutation. Native file/directory operands remain intact.
- Existing-only reads/writes, complete binary round trips, append, truncation,
  empty writes, refreshed lengths/UTC timestamps, text encodings, UTF-8/UTF-16/
  UTF-32 BOM detection, BOM-only files, and preservation of line endings.
- Immediate enumeration without recursion, file/directory filters, nullable
  typed lookup, exclusive file creation, type collisions, returned renamed
  handles, non-overwriting rename, and descendant preservation.
- Missing-file/missing-directory errors, harmless repeated file deletion,
  non-recursive rejection of non-empty directories, and recursive deletion.
- All child-taking APIs reject null/empty names, traversal segments, separators,
  NUL characters, and absolute paths/URI names. Encoded-looking `%2F`/`%5C`
  display names remain literal names rather than decoded traversal components.
- Conditional directory and file symbolic-link checks cover direct unlink and
  recursive containing-directory removal while proving outside targets remain
  intact. Direct file-link copy-overwrite is rejected before it can truncate an
  outside target. Inability to create links is reported as `SKIP`, not a pass.
- Synchronous and asynchronous copy, exclusive/overwrite behavior, truncation,
  file/directory collisions, and self-copy rejection for native paths, canonical
  path aliases, file URIs, and exact opaque content IDs.
- A non-null globally scoped `ResourceId` prevents self-copy when different
  authoritative URI grant aliases identify the same resource, including across
  separate provider instances. Sync/async overwrite must reject before opening
  a writable target stream. Equal document IDs on different authorities remain
  distinct resources; `ResourceId` is comparison-only and is never opened.
- All asynchronous byte/text helpers, successful multi-buffer I/O, existing-only
  behavior, deterministic mid-read cancellation, and pre-cancelled operations
  which must not truncate existing data.
- Both local-to-content and content-to-local copy directions, synchronous and
  asynchronous, including overwrite and non-seekable source/destination streams.
- Deterministic source-read and destination-write failures, pre-cancellation,
  and cancellation triggered by the first source read. Newly created failed or
  cancelled destinations are cleaned up after streams are closed. Failed or
  cancelled overwrite never deletes or replaces an existing destination's ID.

`MemoryFileSystem.cs` is a deliberately narrow **test provider**, not an Android
SAF implementation. Its document IDs contain percent-encoded reserved
characters and do not encode parent/child paths. Parent relationships and
Unicode display names are stored separately. Creation can assign a different
name, rename and move issue a different ID, file size stays unknown, the
modification time stays unknown until an accepted update, and optional resource
identities model grant aliases. Every stream rejects seek/length/position
queries. The provider tracks stream ownership and rejects deletion of open
documents, allowing the suite to catch incorrect cleanup ordering and leaks.
Configurable query errors verify that permission/I/O failures are propagated
instead of being mistaken for absence, and a configurable timestamp rejection
models a provider that ignores modification-time updates.

Overwrite is **not asserted to be transactional**: a failed write may leave
partial/truncated existing content. The required guarantee tested here is that
rollback never deletes a pre-existing destination. Pre-cancelled writes must
preserve the original contents.

Resource identity may be unknown. These tests do not promise detection of hard
links, aliases through linked ancestor directories, concurrent path replacement,
or provider aliases which report neither matching locations nor a matching
non-null resource identity. Direct symbolic-link destination rejection is a
specific copy safety rule, not general sandboxing of arbitrary input paths.

## Unified facade contract

The facade chooses its backend internally. File operations live on the nested
`FileSystem.File` (`Open`, `Create`, `OpenRead`, `OpenWrite`, `Copy`, `Move`,
`Replace`) and directory operations on `FileSystem.Directory` (`Open`, `Create`,
`Move`); `FileSystem` itself only registers the content provider. Attribute and
timestamp writes are handle methods: `StorageFile.SetAttributes` /
`SetLastWriteTime` and their `StorageDirectory` counterparts. There are no
separate public `*Local*` operations. Existing handle operations keep their
existing-only write semantics. The facade's output stream defaults to exclusive
native creation and requires explicit `overwrite: true` or `append: true` for an
existing content document.

Opaque content URIs do not identify a creatable child path. For a new provider
document, pass its authorized parent URI and one display name to the
`FileSystem.File.Create`, `FileSystem.Directory.Create` or `FileSystem.File.Copy`
child overload. Direct URI copying requires an existing content destination. Child
overloads return the provider's actual name/location, which may differ from the request.

The `FileSystem.File.Move` / `FileSystem.Directory.Move` child overloads move
natively inside one backend and never copy and delete; the location-based overloads
remain native-local because a provider URI cannot name a destination that does not
exist yet. Provider replacement streams into the existing destination and consumes
the source without atomicity, and a backup path is refused before mutation. Provider
entries accept attribute requests without effect, and modification-time writes are
provider-dependent.

## Isolated Unity and production generated-wrapper validation

Use the repository's **Unity 6000.3.17f1**, Android Build Support with SDK API 36
and its bundled OpenJDK, a .NET 9 SDK, and the Unity modules for every requested
Player target. Android SDK/JDK are required even for Editor or desktop-only
checks because the production wrappers are generated from Java metadata.
See `Tools/CSharp/AndroidJavaGenerator/README.md` for analyzer build/restore prerequisites.

From the repository root:

```powershell
./Tools/Tests/ValidateFileSystemUnity.ps1
./Tools/Tests/ValidateFileSystemUnity.ps1 -PlayerTargets StandaloneWindows64,StandaloneLinux64,StandaloneOSX,iOS,Android
```

The script calls `Tools/CSharp/AndroidJavaGenerator/build.ps1 -Install` with the selected
Editor path **before staging**, then creates the ignored isolated project under
`Temp/FileSystemUnityValidation/`. It copies production storage and Android core
sources, `JavaClassAttribute` / `JavaApiConfigurationAttribute`, the entire production
`Runtime/` tree recursively (including App/Content/Net/Provider/Unity/Java/Storage
categories and metadata), and the installed analyzer DLL with its
`RoslynAnalyzer`-labelled `.meta`. The only game-service doubles are the existing
logging and keyboard initialization stubs; wrappers and picker logic are real
production code, not validation-only replacements.

The Java extractor is staged at
`Tools/CSharp/AndroidJavaGenerator/Java/JavaApiExtractor.java` and the custom bridge at
`Assets/Plugins/Android/src/java/net/majdata/majdataplay/StorageAccess.java`, retaining
their project-relative paths. Extraction uses the selected Editor's SDK/JDK,
the staged extractor, and production `{UnityData}` source/classpath inputs; process
environment overrides are restored afterward. Generated C# remains in Roslyn's
compilation, is not materialized as `.g.cs` assets, and must not be committed.
The analyzer installation is ignored build output, not a runtime plugin or an
assembly reference. No Unity-generated root solution/project files are built.

Coverage adds to, rather than replaces, the existing Editor local-storage smoke,
Android API guards, reflection-based picker cancellation/dismissal/late-result
checks, and deterministic commit/cancellation and launch/shutdown concurrency:

- The five public production `MajdataPlay.Platform.Android.Runtime.Storage` wrappers
  map the exact `StorageAccess` and nested Java binary names at API 36, with
  `IncludeInheritedMembers = false`, no Java-private construction APIs, and real
  generated `JavaObject` bases, constants, and reference constructors.
- Storage methods return typed `StorageResult` envelopes; getter-only result fields
  map to `DocumentEntry` / `DocumentCursor` / `DocumentStream`. Java byte arrays use
  `sbyte[]` for result `Data` and stream writes; metadata integers, booleans,
  strings, and 64-bit values retain their exact types.
- Borrowed wrappers retain the supplied reference without disposing it; other
  borrowers remain intact. Default adopting ownership disposes exactly once,
  repeated disposal is harmless, disposed wrappers reject reference access,
  and null references are rejected. These use an inert managed disposal probe
  allocated without Java-object construction or JNI handles, not a live JVM.
- All storage entry points, generated instance field getters and cursor/stream
  operations throw the Editor platform guard before JNI. SDK/Unity picker
  signatures retain typed activity/intent/URI/resolver/list/runnable mappings;
  non-storage wrappers are located by Java binary names, not category namespaces.
- The real internal `RequireResult(StorageResult?)` rejects a null envelope with
  `IOException` and preserves a nonnull caller-owned envelope unchanged. The
  bridge error translator and managed stream constructor accept typed wrappers.
- Unity compiles actual production assemblies for requested Player targets
  (Windows64 and Android by default), preserving the Android conditional branch.
  Separately, `javac` compiles production Java sources against the bundled API-36
  SDK and Unity classes, unless explicitly skipped.

New log markers are `FILE_SYSTEM_GENERATED_STORAGE_PASSED`,
`FILE_SYSTEM_GENERATED_PICKER_PASSED`, and `FILE_SYSTEM_GENERATED_BRIDGE_PASSED`.
The aggregate success marker remains `FILE_SYSTEM_UNITY_PASSED`; the script rejects
nonzero exit codes or missing success markers and retains
`Temp/FileSystemUnityValidation/validation.log` for inspection.
`-SkipJavaCompilation` skips only the separate Java bytecode compilation check:
it does **not** skip analyzer installation, Java metadata extraction, or SDK/JDK
requirements.

This checks Editor behavior, analyzer loading and Player **script compilation**,
not APK/AAB packaging, a running Player, JNI marshaling, real JVM reference
ownership, provider-resource closure, persisted grants, IL2CPP/AOT, ARMv7/ARM64
runtime behavior, or a real local/cloud document provider. Device/platform
smoke tests remain necessary; passing the managed-only probe must not be reported
as native-reference or provider validation.

## Temporary data and cleanup

Runtime fixtures use unique `MajdataPlay.FileSystemValidation-<guid>` directories
immediately under the operating system's temp directory, never production
`Assets/` or the source folder. Each fixture owns its exact root. Cleanup checks
its absolute parent, prefix, GUID and delimited child paths before deletion;
it manually unlinks reparse points rather than traversing them. It refuses an
owned root that has been replaced by a link. An interrupted process can leave
its uniquely named OS-temp fixtures; it does not scan or delete other runs'
fixtures. Build output remains in the ignored repository `Temp/` directory.

## Managed-suite validation boundary

Passing this suite demonstrates managed behavior on the **host OS and .NET 9**
only. Run it independently on Windows, Linux and macOS to validate those hosts.
It does **not** validate Unity import/compilation, Mono or IL2CPP, Android SAF
permissions/persisted grants, Java/JNI wrappers, Android providers, cancellation
of blocking device I/O, an Android Player, or a real device. Content-provider
streams here are deterministic in-memory doubles; cross-platform .NET results
must not be presented as Android SAF device validation.

## Application consumer regressions

The migration also has focused independent consumer checks:

```powershell
dotnet run --project Tools/Tests/ChartStorageValidation/ChartStorageValidation.csproj
dotnet run --project Tools/Tests/ResourceStorageValidation/ResourceStorageValidation.csproj
dotnet run --project Tools/Tests/ResourceStorageValidation/ResourceStorageValidation.csproj -p:MobileStorageTarget=Android
```

See each project's README for its production source links and narrow substitutes.
They cover persisted collection IDs, corrupt settings backups, file creation and
truncation, resource customization/hash rules, copy metadata and local atomic
replacement. Windows sandbox restrictions can reject native `File.Replace`; run
the same isolated fixture command with normal host permissions when that occurs.

## Migration verification on 2026-10-10

Windows host results for the commands above: storage suite **29 cases / 840
assertions**, chart storage **3 cases**, and resource storage **9 cases each**
for its iOS and Android conditional branches, all passed. Local replacement was
rerun outside the Windows sandbox to exercise the real host operation.

After unifying the facade operations, the expanded storage suite passed **32
cases / 950 assertions**, with no failures or skips. This rerun used a fresh
build of the independent validation project with **.NET SDK 9.0.316** selected
explicitly on this Windows host:

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) 'Temp/FileSystemValidation/DotnetHome'
dotnet 'C:/Program Files/dotnet/sdk/9.0.316/dotnet.dll' build Tools/Tests/FileSystemValidation/FileSystemValidation.csproj --no-incremental
dotnet 'C:/Program Files/dotnet/sdk/9.0.316/dotnet.dll' run --project Tools/Tests/FileSystemValidation/FileSystemValidation.csproj --no-build
```

The build completed without warnings or errors. Native replacement was again
exercised with normal host permissions. The content cases remain managed
in-memory provider checks; they do not establish Android device behavior.

After adding provider move, provider replacement, attribute no-ops and
modification-time writes to the unified facade, the suite passed **33 cases / 990
assertions** with no failures or skips on the same Windows host with .NET SDK
**9.0.316** selected explicitly:

```powershell
dotnet build Tools/Tests/FileSystemValidation/FileSystemValidation.csproj --no-incremental
dotnet run --project Tools/Tests/FileSystemValidation/FileSystemValidation.csproj --no-build
```

That run's recorded result is the suite's own summary marker,
`FILESYSTEM_VALIDATION_PASSED`.

The unified facade consumer rerun also passed chart storage **3 cases**, resource
storage **9 cases each** for iOS and Android, and FFmpeg **81 managed assertions**.
Each independent project was forcibly rebuilt with SDK 9.0.316 before running.
The final sources also passed the isolated Unity **6000.3.17f1** consumer script
compile described below, with exit code 0. Its additional
`STORAGE_CONSUMER_UNIFIED_FACADE_PASSED` reflection check confirmed that the loaded
IO assembly exposes the new facade names and no public `Local` operation names.

`dotnet run --project Tools/Tests/FFmpegValidation/FFmpegValidation.csproj`
also passed its **81 managed assertions** without native media arguments.
Editor-only implementations retain their original `System.IO` calls and are
outside the migration scope. `./Tools/Tests/CustomSkinAtlasValidation/run.ps1`
passed **9 packing cases and empty/missing inputs** with Direct3D11; its first run hit an unstable
count of existing geometry error logs, and the repeat passed unchanged.

After restoring the Editor-only implementations, Unity **6000.3.17f1** compiled
the current full `Assets/Scripts` and
modified IO, Drawing, Net and FFmpeg source assemblies in the ignored
`Temp/StorageConsumerCompile` project. It used the production .NET 4.8 API profile
and existing DLLs for unchanged dependencies. Reproduction scripts for this
local check are `./Temp/StageStorageConsumerCompile.ps1` followed by
`./Temp/RunStorageConsumerCompile.ps1`; success is marked by
`STORAGE_CONSUMER_COMPILE_PASSED` in the isolated `validation.log`. This verifies
Windows Editor script compilation, not a full fresh main-project import or
Player compilation. Unchanged third-party Editor DLLs reported initialization
exceptions because the isolated project lacks their complete package resources;
the success marker and exit code 0 establish script compilation only, not a
clean main-project Console. Staged production C# source hashes matched the final
workspace. Production `ProjectSettings`, packages, scenes and assets
were not rewritten.

Player/device behavior, Android SAF, other host operating systems, full native
audio/video execution, online HTTP resumption and chart ZIP import scene smoke
tests remain unverified by these checks.
