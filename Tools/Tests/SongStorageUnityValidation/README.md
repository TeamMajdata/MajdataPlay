# Song storage isolated Unity validation

This harness stages the current production song storage sources in a disposable
**Unity 6000.3.17f1** Editor project. It exercises the real local and online song
implementations, the real scanner, IO Storage handles, UniTask and **MajSimai
metadata/full-chart parsing**. It does not copy or replace chart discovery,
resource matching, construction, cache reading or collection-refresh algorithms.

The project is `Temp/SongStorageUnityValidation`, never the main checkout.
An Editor already open on the main project may remain open; do not close it or start another Unity
process against that checkout. Coordinate with the integration owner before any
run to avoid multiple processes on the isolated project as well.

## Prerequisites

1. Initialize the repository submodules (`git submodule update --init --recursive`).
2. Finish importing/compiling the main project in Unity 6000.3.17f1. The script
   reads its compiled MajSimai, UniTask, ZString and small UI/TMP references from
   `Library/ScriptAssemblies`. Never build Unity-generated root projects with dotnet.
3. Keep the installed Unity Newtonsoft.Json, Nito net461, Immutable net461,
   Unsafe net462 and PolySharp dependencies. No package restore/upgrade, root
   solution build or Player build is performed by this script.
4. Unity batch mode must connect to its licensing client. If a sandbox blocks
   licensing or DLL loading, rerun the same command with normal host permissions.
   A broken managed domain is not a passing validation.

The script checks both the repository's pinned version and the selected Editor
executable's product version. It stages unchanged current production bytes on
every run. `Assembly-CSharp.dll` and `Assembly-CSharp-Editor.dll` are never copied.

## Run after integration

From the repository root in PowerShell, after all production/harness edits finish:

```powershell
./Tools/Tests/ValidateSongStorageUnity.ps1 -UnityPath 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Unity.exe' -Repetitions 12
```

A smaller diagnostic run can use `-Repetitions 1`. Compilation-only mode uses a
separate success marker and does not execute constructor, cache or metadata cases:

```powershell
./Tools/Tests/ValidateSongStorageUnity.ps1 -CompileOnly
```

The separate .NET 9 regressions remain owned by ChartStorageValidation. Run them
according to its README; this harness does not change those sources or stubs:

```powershell
dotnet run --project Tools/Tests/ChartStorageValidation/ChartStorageValidation.csproj
```

On the current Windows host, that README also supplies an explicit .NET 9 SDK:

```powershell
dotnet 'C:/Program Files/dotnet/sdk/9.0.316/dotnet.dll' run --project Tools/Tests/ChartStorageValidation/ChartStorageValidation.csproj
```

The default process timeout is 300 seconds (`-TimeoutSeconds`). The script refuses a
locked isolated project. On timeout it reports the PID without killing any
Editor. Inspect the isolated log/PID before stopping only that process; do not
start another run while it remains alive.

## Production compilation coverage

The staged Editor assembly compiles current, unchanged source bytes for:

- SongStorage; SongCollection, OnlineSongCollection, MyFavoriteSongCollection and
  SongCollectionScanner.
- Real SongDetail and OnlineSongDetail, plus ISongDetail and the real
  MajnetSongDetail DTO.
- Production storage/location/sort enums and every IO Storage source in
  `Assets/Plugins/MajdataPlay/IO/Storage/*.cs`.
- The production HTTP range/error/exception and URI-combination types needed by
  the online implementation. Their inclusion provides compilation coverage, not
  protocol or download validation. No new HashHelper dependency is introduced.

SongDetail's only public constructor must accept `StorageDirectory, SimaiMetadata`.
OnlineSongDetail's only public constructor must accept
`StorageDirectory directory, ApiEndpoint serverInfo, MajnetSongDetail songDetail`.
Runtime assertions reject additional legacy constructors; compiling the staged
real scanner and SongStorage catches their removed-constructor call sites. The
legitimate SongDetail.ParseAsync(string, token) factory is retained and its
pre-cancellation case is still exercised. This is not a full-game call-site audit
or a substitute for main-project Unity compilation.

`staged-inputs.json` records SHA-256 hashes of production sources, harness C# sources
and dependency DLLs. After Unity exits, the script compares production and
harness hashes again and rejects a snapshot changed during the run. Rerun after
integration; a previous pass cannot certify new source bytes.

## Added integration cases

### Constructors and local resource tables

- The exact shared metadata name, five dot-prefixed track formats, all twelve
  bg/pv/mv video names, and exactly `bg.png` / `bg.jpg` covers.
- Real local construction for every table entry, uppercase files, lowercase
  matching directory decoys, absent optional media and unsupported optional names.
  Selected audio/cover paths are inspected without entering decoders; video paths
  use the public production method.
- Missing maidata, maidata-as-directory, strict-backend uppercase maidata,
  missing track, track-as-directory and unsupported tracks fail real construction
  and parsing. The real scanner excludes those candidates with exact counters.
- Maidata follows the backend's FindFile rules: strict and case-insensitive
  backends are both tested. Track/video/cover matching follows the central tables
  with OrdinalIgnoreCase, independently of those backend rules.
- Temporary central-table sentinel entries under tr-TR expose duplicated lists or
  culture-sensitive matching in local construction, the real scanner and the real
  online cache lookup. Tables/culture are restored in finally after awaited work;
  no whole-library scan runs concurrently with this bounded assertion.

### Original-backend metadata and real online cache reading

- Real local ParseAsync, GetMaidataAsync, unchanged in-memory cache and forced
  changed-byte reparse use an unregistered original backend. File-open counters
  prove which backend read the genuine Simai bytes.
- Sequential-only provider streams throw on Length, Position and Seek. Real local
  and online metadata paths must buffer those original bytes before MajSimai
  parsing. This fixture is an IFileSystem boundary, not Android SAF/JNI validation.
- Real OnlineSongDetail receives already-created local/provider directories,
  retains the identical `_cacheDirectory` object and does not create a second
  canonical cache path. Pre-cached maidata is parsed/reparsed with the real parser,
  and in-memory cache hits do not reopen the original provider.
- Pre-cached default-name resources and uppercase legacy-mv/PNG/OGG resources are
  found through the production central lookup. Private `FindCachedFile(IEnumerable<string>)`
  is invoked only as an inspection seam for audio/cover handles: its selection
  algorithm is neither copied nor substituted. No decoder runs.
- Public online video-path lookup uses the actual cached provider location. An
  existing `bg.mp4.cache` negative marker returns no video, even with a BG.MP4
  directory decoy. Optional cached audio/cover files need not exist to parse a chart.
- Pre-canceled online metadata refresh propagates OperationCanceledException.
- Real private SongStorage.GetOnlineCollection receives one explicitly supplied
  offline DTO list. It creates/passes the per-song directory and must preserve the
  literal uppercase, hyphen-separated hash directory name. A supplied null list
  exercises the real cached fallback scanner and real SongDetail parsing.

HTTP sends are rejected before sockets are opened and counted, including failures
caught by production. Successful cases require **zero HTTP sends**. Provider
cache cases also require zero mutation attempts. The remote DTO list is supplied
by a narrow boundary; no live online response, authentication or HTTP protocol
is validated. Default cache names are covered, but actual missing-resource
request/download names and remote downloads are not executed.

## Existing integration cases retained

- Real MajSimai title/artist/difficulty/designer, MD5/Base64 identity, full chart
  parsing, and pre-canceled directory/string metadata factories.
- Real shared scanner across 41 concurrent collections (one empty middle folder)
  with 120 valid charts, invalid candidates and a reserved .MajdataPlay child.
- Directory-backed RefreshAsync and main-thread Refresh, persistent identity and
  backend handle, cursor/filter/origin retention, invalidation and canceled snapshots.
- Virtual, online-cache and favorites refresh no-ops.
- Real SongStorage.InitAsync leaves collections/count unchanged until explicit
  RefreshLocal; 12 local refreshes by default and final explicit Local then Online
  calls retain exact per-folder/aggregate counts, ordering, selection, local JSON
  playlist and portable favorites, plus canceled-publication retention.
- Favorites DB/export hashes are initialized once by InitAsync. Both refresh
  paths rebind committed runtime hashes rather than reload the DB/export; no
  production full-refresh API or favorites-reload branch remains.
- Missing local JSON Name/SongHashs are isolated without discarding healthy
  playlists or charts. An initially unmatched remote-only local definition is
  cached and resolves after online loading even when its JSON was deleted.
- Real independent online refresh preserves all local base/chart references and
  selection with a rejecting/counted local provider. Local refresh keeps the exact
  online base/chart references without requests even when online is disabled.
  Only RefreshOnline removes online content on disable; returning online hashes
  restore cached playlist GUIDs and favorite membership without local access.
- Real local and online DisposeAsync, including UniTask's main-thread switches,
  while the real Editor update loop continues.

The execute method never waits synchronously for unfinished work. Editor.update
polls Task.IsCompleted; GetAwaiter().GetResult observes only completed tasks. The
script deliberately omits -quit. Final success requires the selected marker,
actual exit code zero and no C# errors/broken managed-assembly loading.

## Boundary doubles and limitations

- RuntimeStubs: global settings/events, fixture format arrays matching the game's
  dot-prefixed formats, sort/filter primitives, logging, Newtonsoft JSON adapter
  and rejecting image/audio/preview services. Scene-runtime and Unity-object
  compile helpers live in MajdataPlay, where the staged song sources resolve them;
  metadata cases still never enter cover/media loaders.
- StorageBoundaryStubs: endpoint/config shapes, offline DTO-list injection,
  rejecting other network-service calls and in-memory SQLite. OnlineSongDetail is
  **not** a stub; its production source is staged and compiled.
- OnlineBoundaryStubs: rejecting HTTP handler and unused transfer-buffer compile
  surface. The real download method compiles, but transfer-buffer use is forbidden.
- ValidationFileSystem/ValidationSequentialReadStream: opaque seeded IFileSystem
  entries, exact backend name rules, original byte streams, rejected local
  access and mutation/query/read counters. They do not contain any chart discovery/matching/parsing algorithm.

UnityEngine, UniTask, MajSimai, Nito locks and IO Storage are real. The UI/TMP DLLs
only satisfy precompiled references; no scene/rendering stack is exercised.
Placeholder media bytes are deliberately not decodable. The in-memory SQLite
boundary does not validate native persistence or production serializer policy.

**Not validated:** the full main-game assembly dependency graph, Play Mode or
scene/lifecycle behavior, image/audio/video decoding, remote HTTP/downloads,
authentication/retries/resume/online failure semantics, native SQLite, Android
SAF permissions/JNI, IL2CPP/Mono Players, other architectures/platforms or real
hardware. Controlled opaque/sequential fixtures are not real-device evidence.
Exhaustive in-flight cancellation/fault injection remains in the separate portable
suite and device tests. Repeated scans do not prove every possible schedule.

The Editor log may contain licensing/service TLS messages and the precompiled
UniTask particle-trigger signature diagnostic (OnParticleUpdateJobScheduled).
Those are not production C# errors, and those services/triggers are not exercised.

## Outputs

All generated project settings, metadata, dependencies, fixtures, logs and
compilation products stay below git-ignored `Temp/SongStorageUnityValidation`:

- `validation.log`: latest Unity run and SONG_STORAGE markers.
- `staged-inputs.json`: staged production/harness/dependency provenance.
- `Fixtures/<run-id>/`: disposable chart/playlist/favorites/cache fixtures.
- `Library/`, `Logs/` and staged `Assets/Validation/`: isolated generated products.

The script removes only its checked staging tree, refuses junctions/reparse
points and retains the isolated Unity cache. It never modifies production
sources/scenes/settings, user charts/cache or the main Editor.

## Recorded expanded-harness validation (historical)

On **2026-10-11**, the then-current expanded harness completed with process **exit 0**
and `SONG_STORAGE_UNITY_PASSED`:

```powershell
./Tools/Tests/ValidateSongStorageUnity.ps1 -UnityPath 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Unity.exe' -Repetitions 12
```

The successful run compiled both real song-detail implementations and validated
all centralized resource names, original-backend/non-seekable metadata reads,
real per-song online-cache construction and cached-list fallback. It also passed
collection refresh, 41 simultaneous collections / 120 real charts, initialization,
twelve local replacements and the final full refresh without missing/duplicate
folders. No HTTP send, transfer-buffer allocation or media decoder was entered.

All staged production and harness C# hashes matched the current workspace after
Unity exited; `Temp/SongStorageUnityValidation/staged-inputs.json` records the
exact source/dependency set for that run. The main Editor was left running.
The sandboxed attempt could not connect to licensing/Package Manager; the
approved, unsandboxed run of the same isolated command passed. This result does
not remove the runtime/platform limitations listed above. Rerun after source
changes rather than treating this record as certification of later source bytes.

## Independent-refresh validation on 2026-10-12

The current independent-refresh harness passed the same Unity 6000.3.17f1 command
with `-Repetitions 12`, process **exit 0**, `SONG_STORAGE_UNITY_PASSED` and the new
`SONG_STORAGE_INDEPENDENT_SOURCES_PASSED` marker. The run validated 41 collections /
120 real local charts, non-publishing initialization, incomplete JSON isolation,
independent online/local reference retention, cached unmatched-definition
recovery, selection and zero local-provider access / HTTP sends.

The first attempt exposed the existing scene-runtime/Unity-object helpers in the
wrong stub namespace. Only the harness boundary namespace was corrected; the
real production song implementations, parser and refresh algorithms were not
replaced. The subsequent run passed with production/harness SHA-256 hashes
matching the current workspace in `Temp/SongStorageUnityValidation/staged-inputs.json`.
The main Editor remained open. This is isolated Editor evidence, not Play Mode,
Player, real network, native persistence or hardware certification.
