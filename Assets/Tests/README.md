# MajdataPlay Tests

Unity Test Framework suite for the main project (`Assets/Scripts`, compiled into `MajdataPlay.Core` and
`MajdataPlay.Core.Editor`) and the reusable libraries under `Assets/Plugins/MajdataPlay`, including
MonoBehaviour components. Per-module standalone validation programs live in
[`Tools/Tests/Modules`](../../Tools/Tests/Modules/README.md).

The tests live in the main project but are not compiled by it. Every test assembly has
`defineConstraints: ["UNITY_INCLUDE_TESTS", "MAJDATAPLAY_TESTS"]`. `MAJDATAPLAY_TESTS` is defined only in
the isolated copy that [`Tools/Tests/Run-UnityTests.ps1`](../../Tools/Tests/Run-UnityTests.ps1) stages,
together with the `InternalsVisibleTo` attributes the tests need, so the main project, its Test Runner
window and its Player builds never see the test assemblies.

## Layout

| Folder | Assembly | Platform |
| --- | --- | --- |
| `Shared/` | `MajdataPlay.Tests.Shared` | Any; helpers for EditMode and PlayMode tests |
| `EditMode/Game/<Area>/` | `MajdataPlay.Tests.Game` (one asmdef for all areas) | Editor (EditMode) |
| `PlayMode/Game/<Area>/` | `MajdataPlay.Tests.Game.PlayMode` (one asmdef for all areas) | Any (PlayMode) |
| `EditMode/Plugins/<Module>/` | `MajdataPlay.Tests.<Module>` | Editor (EditMode) |
| `PlayMode/Plugins/<Module>/` | `MajdataPlay.Tests.<Module>.PlayMode` (optional) | Any (PlayMode) |

`<Area>` mirrors the folder under `Assets/Scripts` that is tested (`GamePlay`, `Misc`, `Scenes`, ...) and
`<Module>` mirrors the library folder under `Assets/Plugins/MajdataPlay` (`Buffers`, `Net`, ...).

Every test is written with the Unity Test Framework (`[Test]` for synchronous logic, `[UnityTest]`
coroutines for anything that needs frames, lifecycle messages or async code).

MonoBehaviour components are covered mainly by PlayMode `[UnityTest]`s: the test adds the component,
yields frames and asserts on the result while Unity calls `Awake`, `OnEnable`, `Start`, `Update` and
coroutines itself. Entering Play Mode runs the game's `[RuntimeInitializeOnLoadMethod]` hooks, such as
`MajEnv.Init`. In EditMode Unity does not call these messages on ordinary behaviours, so EditMode tests of
components create them with `TestObjects`, set serialized fields with `PrivateMember` and deliver messages
with `UnityMessages`.

The `Harness` folders check the setup itself: that the production assemblies and their internals are
reachable, that the helpers work, and that PlayMode tests run inside Play Mode.

### Access to internals

The repository holds no `InternalsVisibleTo` attributes for the tests. In the staged copy only, the runner
writes a `MajdataPlay.Tests.Internals.cs` file next to every asmdef under `Assets/Scripts`,
`Assets/Plugins/MajdataPlay` and `Assets/Tests/PlayMode`, granting every test asmdef found under
`Assets/Tests` access to its internals. Tests therefore reach `internal` members directly, and EditMode
tests can use the internal test doubles of the PlayMode test assemblies. New test asmdefs are picked up
automatically.

### Shared helpers (`MajdataPlay.Tests`)

- `TestObjects`: creates and tracks game objects and components; `Dispose` destroys them.
- `PrivateMember`: reads and writes non-public fields and properties (including get-only auto property
  backing fields) and invokes non-public methods across the type hierarchy.
- `UnityMessages`: sends parameterless Unity messages and runs coroutine messages to completion.
- `TemporaryDirectory`: a unique directory under `Application.temporaryCachePath`, created and deleted
  through `MajdataPlay.IO.Storage.FileSystem`.

## Running

[`Tools/Tests/Run-UnityTests.ps1`](../../Tools/Tests/Run-UnityTests.ps1) runs the suite in batch mode in an
isolated copy of the project, so the main project, its `Library` and its serialized assets are only read
and the suite can run while the project is open in the Editor. To debug tests interactively, open the
staged copy (`<WorkRoot>/Project`) in Unity 6000.3.17f1 and use **Window > General > Test Runner** there.

```powershell
./Tools/Tests/Run-UnityTests.ps1
./Tools/Tests/Run-UnityTests.ps1 -Coverage
./Tools/Tests/Run-UnityTests.ps1 -TestPlatform EditMode -AssemblyNames 'MajdataPlay.Tests.Game;MajdataPlay.Tests.Buffers'
./Tools/Tests/Run-UnityTests.ps1 -TestFilter 'MajdataPlay.Tests.Plugins.Buffers'
```

| Parameter | Meaning |
| --- | --- |
| `-UnityPath` | Unity executable. Defaults to the `ProjectVersion.txt` version under the Unity Hub or `Program Files/Unity Editors` install. |
| `-WorkRoot` | Staging root. Defaults to `%LOCALAPPDATA%/MajdataPlay.Tests` on Windows and the temp directory elsewhere. |
| `-TestPlatform` | `EditMode`, `PlayMode` or `All` (default). |
| `-AssemblyNames` | Semicolon separated test assemblies, passed to Unity's `-assemblyNames`. Default: all. |
| `-TestFilter` | Passed to Unity's `-testFilter`, e.g. a namespace, fixture or test name. Default: all. |
| `-Coverage` | Collects coverage of the `MajdataPlay.*` production assemblies with `com.unity.testtools.codecoverage` and writes an HTML report. |
| `-SkipProjectSync` | Reuses the staged project as is; repository changes since the last sync, including test changes, are not picked up. |
| `-NoLibrarySeed` | Imports from scratch instead of seeding a missing staged `Library` from the main project. |
| `-NoGraphics` | Passes `-nographics`; tests that need a graphics device then fail. |
| `-TimeoutMinutes` | Limit for each Unity invocation (default 90). |

The script exits with code 1 when a run fails, has no results (usually a compile error, whose `error CS`
lines are printed) or times out. Outputs under the work root:

- `Project/`: the staged project. Its `Library` is reused, so only the first run imports everything.
- `Results/EditMode.xml`, `Results/PlayMode.xml`: NUnit 3 results.
- `Logs/EditMode.log`, `Logs/PlayMode.log`: Unity editor logs.
- `Coverage/Report/index.html`: merged EditMode and PlayMode coverage report (`-Coverage`).

The default work root is short and outside OneDrive or other synced folders on purpose: Unity package
paths in `Library/PackageCache` reach about 200 characters, so a deep root exceeds the Windows `MAX_PATH`
limit when long paths are not enabled. A full staged copy needs about 5 GB of disk space.

Staging steps:

1. `Assets` (including `Assets/Tests`), `ProjectSettings` and `ThirdParty` are mirrored with
   `robocopy /MIR` (`rsync --delete` elsewhere); files that exist only in the staged copy are deleted,
   except the generated `MajdataPlay.Tests.Internals.cs` files.
   `ThirdParty` keeps its relative location because `Packages/manifest.json` references local packages
   with `file:../ThirdParty/...`. Untracked nested Git repositories under `Assets` (for example a stray
   `Assets/Plugins/UniTask` checkout that duplicates the UniTask package) are skipped with a warning.
2. `Packages/manifest.json` is copied with `com.unity.testtools.codecoverage` added. `packages-lock.json`
   is copied only when the repository's lock file changes.
3. A missing staged `Library` is seeded from the main project's `Library` when it exists, skipping the
   full import of the first run.
4. Test-only changes that exist only in the staged copy: `MAJDATAPLAY_TESTS` is appended to every
   platform's scripting define symbols in `ProjectSettings.asset`, and the `MajdataPlay.Tests.Internals.cs`
   files described under [Access to internals](#access-to-internals) are written.

## Writing tests

- Follow the repository `.editorconfig` and `AGENTS.md`: `#nullable enable`, block namespaces, `using`
  directives outside the namespace, CRLF, braces on every statement, `var`, and English XML documentation
  on every member, including test methods.
- Namespaces follow the tested code: `MajdataPlay.Tests.Game.<Area>` for `EditMode/Game/<Area>`,
  `MajdataPlay.Tests.Game.<Area>.PlayMode` for `PlayMode/Game/<Area>`,
  `MajdataPlay.Tests.Plugins.<Module>` for `EditMode/Plugins/<Module>` and
  `MajdataPlay.Tests.Plugins.<Module>.PlayMode` for `PlayMode/Plugins/<Module>` (some existing PlayMode
  helpers shared with EditMode tests keep the EditMode namespace). Code under `PlayMode/`
  compiles for every platform and must not use `UnityEditor`.
- Each module has exactly one EditMode asmdef named `MajdataPlay.Tests.<Module>` and, optionally, one
  PlayMode asmdef named `MajdataPlay.Tests.<Module>.PlayMode`. A new test asmdef must reference
  `MajdataPlay.Tests.Shared` when it uses the helpers and keep both the `UNITY_INCLUDE_TESTS` and
  `MAJDATAPLAY_TESTS` define constraints. Never add `InternalsVisibleTo` attributes for tests to the
  repository.
- MonoBehaviour test doubles go in their own file named after the class, under `PlayMode/`. Unity refuses
  to attach behaviours compiled into editor assemblies, so doubles used by EditMode tests live in the
  matching PlayMode assembly (`MajdataPlay.Tests.Game.PlayMode` or `MajdataPlay.Tests.<Module>.PlayMode`),
  which the EditMode asmdef references.
- Tests are Unity assets: commit every new file and folder together with its `.meta` file, and move or
  delete them together with it.
- Keep tests deterministic and isolated. Restore any static state a test changes (for example `MajEnv` or
  `MajInstances` members) in `TearDown`, destroy every Unity object, and use `TemporaryDirectory` instead of
  user or project data. Unexpected error logs fail a test; expect them with `LogAssert.Expect`.
- Async code uses `[UnityTest]` with `UniTask.ToCoroutine(async () => ...)`.
- Never replace a `static readonly` field (or a get-only static auto property's backing field) through
  reflection, for example `MajEnv.SharedHttpClient`. Mono's JIT folds such fields into compiled code, so
  methods compiled while the replacement is installed keep its address after it is collected and later
  tests read freed memory. Change the state inside the existing object instead; for HTTP clients, swap the
  `HttpMessageInvoker` handler as `EditMode/Game/Misc/Support/StubHttpScope` does.
- Do not expect logs from different threads in a fixed order with `LogAssert.Expect`; record them with
  `Application.logMessageReceived` and assert on the set instead.
- Do not change production code from a test change. A test that documents a known production defect is
  marked `[Ignore("Known issue: ...")]` and reported.

## Limitations

- The suite runs in the Windows (or host) Editor. Branches under `UNITY_ANDROID`, `UNITY_IOS`,
  `UNITY_STANDALONE_LINUX`, `UNITY_STANDALONE_OSX` or `!UNITY_EDITOR` are not compiled and cannot be
  covered; use the target-specific validations in [`Tools/Tests/Modules`](../../Tools/Tests/Modules/README.md).
- Native libraries are loaded only where the tests explicitly need them. Real audio output, FFmpeg
  encode/decode on devices, serial/HID hardware, touch panels, network services and GPU sharing are not
  exercised; see the corresponding validations in [`Tools/Tests/Modules`](../../Tools/Tests/Modules/README.md).
- Passing tests do not prove Player builds, IL2CPP, or target devices work.
