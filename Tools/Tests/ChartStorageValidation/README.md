# Chart storage and scanning validation

This package-free **.NET 9** executable links the real `SongStorage`,
`SongCollection`, `OnlineSongCollection`, `MyFavoriteSongCollection`,
`SongCollectionScanner`, `ChartSettingStorage`, storage handles and local
filesystem backend. It does not build the Unity-generated root projects and
**does not copy or substitute the production scanning/scheduling algorithms**.

## Run

Run from the repository root:

```powershell
dotnet run --project Tools/Tests/ChartStorageValidation/ChartStorageValidation.csproj
```

When multiple SDKs are installed, select the installed .NET 9 SDK explicitly.
For the current Windows development machine:

```powershell
dotnet 'C:/Program Files/dotnet/sdk/9.0.316/dotnet.dll' run --project Tools/Tests/ChartStorageValidation/ChartStorageValidation.csproj
```

A successful run exits with code zero and prints:

```text
CHART_STORAGE_VALIDATION_PASSED (16 cases)
```

The online-first group starts the same executable in a fresh process with
`--online-first`, so both initial-source scenarios are checked without resetting
production private fields or creating another test project. That child also
prints its own one-case success marker before the parent's 16-case marker.

Failures exit with code one and print the exception and
`CHART_STORAGE_VALIDATION_FAILED`. Expected injected bad-chart, provider,
playlist and cancellation diagnostics can appear before the success marker;
those diagnostic lines alone do not indicate a failed run. The linked,
unchanged chart-settings source currently emits a nullable warning (`CS8602`).

There are no package references. Restore uses this project's own directory as
its package source. Build outputs stay under the ignored
`Temp/ChartStorageValidation/{bin,obj}/` directories. Each run creates one
GUID-named fixture root beneath `Temp/ChartStorageValidation/Workspace/`, then
checks its parent and refuses reparse-point cleanup before deleting only that
owned root. Run from the repository root to keep these fixtures in the ignored
repository temporary directory. No real charts, settings or databases are read
or changed.

## Coverage

### Existing storage consumers

- Local collection ID creation, persisted reload, malformed ID replacement and
  truncation; `.MajdataPlay` marker-directory attributes and file collisions.
- Malformed chart-settings backup beside the source, including its original
  modification time; first-time save and shorter replacement writes without
  stale trailing bytes.

### Collections

- `StorageDirectory` constructor/`Empty` API and compatible `Path`; exact
  original handle/backend retention for opaque provider locations.
- Synchronous and asynchronous refreshes populate empty collections, add and
  remove charts, replace changed metadata and preserve the collection ID.
- Applied sort/filter settings are snapshotted rather than aliased to a mutable
  `SongOrder`; empty-filter recovery, refresh while filtered empty, restored
  cursor hashes and fallback after deleting the selected chart.
- Synchronous refresh under a rejecting, non-pumping synchronization context.
- Collection-level enumeration failures and pre-start/active cancellation keep
  the exact old snapshot, ID, cursor, ordering and undisposed old chart objects.
- Successful collection refresh **does not dispose old shared SongDetail
  objects**: aggregate/favorites views and captured arrays/enumerators keep using
  their previous snapshot. Only storage owns library replacement cleanup; the
  collection cannot infer exclusive ownership.
- Virtual and online refreshes are no-ops even with a canceled token. An online
  cache is never rescanned or substituted for its remote list.

### Centralized resource names and online caches

- Track candidates consult `SongStorage.SupportedTrackFileNames` with ordinal,
  case-insensitive matching. Every supported uppercase track name is accepted;
  same-named directories and missing metadata are rejected. A temporary test-only
  filename-table change verifies that the real scanner has no duplicate whitelist.
- Cover names are published as `bg.png` and `bg.jpg`; the shared video table keeps
  legacy `mv.*` names alongside `bg.*` and `pv.*`. Real resource selection in
  `SongDetail`/`OnlineSongDetail` is covered by the separate isolated Unity harness.
- Offline online-list responses run the production collection-loading branch.
  Each detail receives an existing per-song `StorageDirectory` retaining its
  endpoint cache backend and the legacy uppercase, hyphen-separated hash name.
  An unavailable remote list scans those same local cache directories.

### Scanner and root scheduler

- Production scanner order is creation time, then ordinal name/location, not
  provider enumeration or parse completion order. Repeated scans accumulate
  discovered/processed/loaded counters correctly.
- Backend-reported hidden chart directories, files, unsupported tracks and missing
  metadata are skipped. Non-hidden dot-prefixed and provider-reported linked chart
  directories remain eligible. Bad fake metadata, injected parse errors and denied
  **chart-child** enumeration do not discard healthy sibling charts.
- Pre-start and active cancellation reach the fake parser; all workers are
  observed before returning and already parsed, unpublished songs are released.
- Eight collections scan concurrently behind one deterministic parse barrier.
  Their combined parser concurrency cannot exceed `Environment.ProcessorCount`.
  All charts are loaded after barrier release; disposing a scanner rejects new
  scans but permits admitted scans to finish safely.
- The actual private
  `GetLocalCollections(StorageDirectory, IProgress<string>?, CancellationToken)`
  entry point is invoked by reflection; no private state-field names are used.
- **Eight repetitions of 96 collections**, with mixed empty/nonempty and hidden
  root folders, reversed provider enumeration, unique stable IDs, exact folder/chart
  membership, name-sorted results, the shared parse ceiling and one terminal
  100-percent progress report. Empty chart roots and cancellation are included.
- **Collection-level** enumeration and ID-persistence failures fail the entire
  root scan, drain all admitted workers and release unpublished songs. They must
  not claim a successful partial library with a missing folder. Throwing progress
  callbacks also drain workers and release unpublished charts. This is distinct
  from the intentionally isolated chart-child failures.

### Independent source snapshots

- `InitAsync(CancellationToken)` creates a missing chart root/default directory,
  initializes favorites DB/export hashes and save handling, but leaves the exact
  `Collections` array, chart count and parser-entry count unchanged. A favorites
  query failure during initialization publishes nothing and can be retried.
- A first `RefreshLocal` builds local bases/All/favorites without an online
  refresh. A fresh-process first `RefreshOnline` builds only online bases and
  shared views without a local refresh, filesystem access or local parsing. The
  saved initial collection GUID/chart hash is restored rather than clamped away.
- Portable favorites are loaded **during initialization**. Both source refreshes
  use committed runtime hashes, never reread the favorites DB/export, and do not
  resurrect a removed runtime favorite. Query-failure injection remains enabled
  while exercising both refresh paths to reject an unexpected database query.
  There is no production full-refresh/favorites-reload branch anymore.
- Successful commits publish `Collections`, All, favorites and successful base
  chart totals together. Derived duplicates do not inflate totals. Invalid JSON,
  missing `Name` or `SongHashs`, and currently unmatched definitions are isolated
  without losing healthy neighboring playlists or charts.
- `RefreshLocal` preserves online base collection/chart references and rebinds
  retained online playlists/courses, even when online settings were disabled
  after loading. No chart-list or online-favorites request is entered.
- `RefreshOnline` preserves local base collection/chart references, never
  enumerates or parses the local source, and rebinds local playlists from loaded
  definitions rather than JSON. A rejecting, counted content provider detects
  even an attempted local query. Modified/deleted JSON does not affect online
  rebinding; an unmatched remote-only definition retains its GUID and recovers
  when its chart returns. Disabled-online removal belongs only to this path.
- Both source paths rebind All, favorites, local and online playlists and restore
  selection by collection identity/chart hash. Filtered-empty retained online
  bases contribute their unfiltered origins to the aggregate and count.
- Canceled scans, inaccessible roots, collection-ID errors, queued cancellation
  and deterministic failures before publication retain the exact committed
  array, aggregate references, favorites and count. New unpublished source
  charts are released; the other source and old shared charts remain usable.
- Local/online admission is serialized through one shared refresh lock in both
  directions. A local refresh cannot enumerate while an online request is held;
  an online refresh cannot request while local parsing is held. Successful
  retries replace only their owned source instances and retain coherent totals.
- The test-only `RefreshBothSourcesAsync` explicitly awaits local then online
  refreshes for migrated legacy cases. The two commits are independent; this
  helper is not a production full-refresh API or a combined transaction.
- Complete online playlist/course request definitions survive partially or
  entirely missing chart sets, preserving IDs, course metadata and null public
  playlist `DanInfo`. Reimporting charts restores the requested hashes.
- Offline endpoint favorites responses still verify replacement without
  duplicate courses, failed-endpoint object retention, successful empty-response
  removal and cancellation of `RefreshUserOnlineFavAsync`.

## Substitutes and validation limits

`SongDetailStub.cs` reads **fake maidata JSON**, not Simai markup, through the
original backend. It injects delays, parse failures and cancellation gates and
records active/peak parses, backend instances and disposal. It does not link or
validate the real `SongDetail`/Simai parser, chart hashes, covers, tracks, media
preloading or native resources.

`OpaqueFileSystem.cs` wraps only test-owned local fixtures with an opaque URI
map. Scanner cases keep it unregistered; root-refresh cases explicitly register
its content URI variant. It injects provider order, timestamps, hidden flags and
counted/rejected access without depending on host ACLs. These checks detect losing the original
`IFileSystem` or deriving child paths from URI text; they are **not Android SAF
or mobile-device integration tests**.

`Stubs.cs` and `StorageStubs.cs` provide narrow settings/game-model, formatting,
logging, UniTask, SQLite and online API boundaries. Serialization uses
`System.Text.Json`, not production Newtonsoft.Json. Online responses are
in-process fixture callbacks: no actual HTTP request, login or download occurs.
The narrow DanInfo doubles leave absent required JSON fields null, matching the
production DTO, so malformed-definition isolation is not hidden by test defaults.
The real storage scheduling, collection publication and lifetime decisions are
still linked production code. Reflection is limited to the root-scan and online-collection loading
entry points and injecting an online fixture array through `Collections`' setter;
private scanner counters and snapshot shapes are not reproduced.

Passing this executable confirms managed control flow and fixture file effects
only. It does **not** establish Unity 6000.3.17f1 script compilation, Unity main
thread/lifecycle behavior, native SQLite persistence, upstream parser correctness,
Player behavior, online protocol compatibility, Android/iOS permissions or
hardware/media integration. Run the corresponding Editor, Player and target
platform smoke checks separately. A synchronous provider call or tokenless
upstream parser may block cancellation; the fake parser intentionally supports
cancellation to verify that production scheduling forwards and observes it.
