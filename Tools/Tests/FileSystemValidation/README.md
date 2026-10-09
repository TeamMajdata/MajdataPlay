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

- Local path and facade dispatch, nested local directory creation, idempotent
  directory creation, UTF/non-ASCII names, and percent-encoded `file://` URIs.
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
name, rename issues a different ID, file size and modification time remain
unknown, and optional resource identities model grant aliases. Every stream
rejects seek/length/position queries. The provider tracks stream ownership and
rejects deletion of open documents, allowing the suite to catch
incorrect cleanup ordering and leaks. Configurable query errors verify that
permission/I/O failures are propagated instead of being mistaken for absence.

Overwrite is **not asserted to be transactional**: a failed write may leave
partial/truncated existing content. The required guarantee tested here is that
rollback never deletes a pre-existing destination. Pre-cancelled writes must
preserve the original contents.

Resource identity may be unknown. These tests do not promise detection of hard
links, aliases through linked ancestor directories, concurrent path replacement,
or provider aliases which report neither matching locations nor a matching
non-null resource identity. Direct symbolic-link destination rejection is a
specific copy safety rule, not general sandboxing of arbitrary input paths.
## Temporary data and cleanup

Runtime fixtures use unique `MajdataPlay.FileSystemValidation-<guid>` directories
immediately under the operating system's temp directory, never production
`Assets/` or the source folder. Each fixture owns its exact root. Cleanup checks
its absolute parent, prefix, GUID and delimited child paths before deletion;
it manually unlinks reparse points rather than traversing them. It refuses an
owned root that has been replaced by a link. An interrupted process can leave
its uniquely named OS-temp fixtures; it does not scan or delete other runs'
fixtures. Build output remains in the ignored repository `Temp/` directory.

## Validation boundary

Passing this suite demonstrates managed behavior on the **host OS and .NET 9**
only. Run it independently on Windows, Linux and macOS to validate those hosts.
It does **not** validate Unity import/compilation, Mono or IL2CPP, Android SAF
permissions/persisted grants, Java/JNI wrappers, Android providers, cancellation
of blocking device I/O, an Android Player, or a real device. Content-provider
streams here are deterministic in-memory doubles; cross-platform .NET results
must not be presented as Android SAF device validation.
