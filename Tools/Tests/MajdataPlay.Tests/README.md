# MajdataPlay.Tests

Unity Test Framework suite for the main project (`Assets/Scripts`, compiled into `Assembly-CSharp` and
`Assembly-CSharp-Editor`) and the reusable libraries under `Assets/Plugins/MajdataPlay`, including
MonoBehaviour components. Per-module standalone validation programs live in [`../Modules`](../Modules/README.md).

The tests never live inside the main project. `Run-Tests.ps1` mirrors the project into an isolated copy,
stages the tests into that copy and runs Unity 6000.3.17f1 in batch mode, so the main project, its
`Library` and its serialized assets are only read.

## Running

```powershell
./Tools/Tests/MajdataPlay.Tests/Run-Tests.ps1
./Tools/Tests/MajdataPlay.Tests/Run-Tests.ps1 -Coverage
./Tools/Tests/MajdataPlay.Tests/Run-Tests.ps1 -TestPlatform EditMode -Area Game/Misc,Plugins/Buffers
./Tools/Tests/MajdataPlay.Tests/Run-Tests.ps1 -TestFilter 'MajdataPlay.Tests.Plugins.Buffers'
```

| Parameter | Meaning |
| --- | --- |
| `-UnityPath` | Unity executable. Defaults to the `ProjectVersion.txt` version under the Unity Hub or `Program Files/Unity Editors` install. |
| `-WorkRoot` | Staging root. Defaults to `%LOCALAPPDATA%/MajdataPlay.Tests` on Windows and the temp directory elsewhere. |
| `-TestPlatform` | `EditMode`, `PlayMode` or `All` (default). |
| `-Area` | Areas to stage, e.g. `Game/Misc` or `Plugins/Net`. `Shared` is always staged. Default: all. |
| `-TestFilter`, `-AssemblyNames` | Passed to Unity's `-testFilter` and `-assemblyNames`. |
| `-Coverage` | Collects coverage with `com.unity.testtools.codecoverage` and writes an HTML report. |
| `-SkipProjectSync` | Restages only the tests and reuses the staged project files. |
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

### Staging

1. `Assets`, `ProjectSettings` and `ThirdParty` are mirrored with `robocopy /MIR` (`rsync` elsewhere).
   `ThirdParty` keeps its relative location because `Packages/manifest.json` references local packages
   with `file:../ThirdParty/...`. Untracked nested Git repositories under `Assets` (for example a stray
   `Assets/Plugins/UniTask` checkout that duplicates the UniTask package) are skipped with a warning.
2. `Packages/manifest.json` is copied with `com.unity.testtools.codecoverage` added. `packages-lock.json`
   is copied only when the repository's lock file changes.
3. A missing staged `Library` is seeded from the main project's `Library` when it exists, skipping the
   full import of the first run.
4. `Shared/` is staged to `Assets/MajdataPlay.Tests/Shared`, `Game/<Area>` (without its `PlayMode`
   folder) to `Assets/MajdataPlay.Tests/Editor/Game/<Area>`, `Game/<Area>/PlayMode` to
   `Assets/MajdataPlay.Tests/PlayMode/Game/<Area>` and `Plugins/<Module>` to
   `Assets/MajdataPlay.Tests/Plugins/<Module>`. Unity creates the `.meta` files of staged tests; they are
   not committed.
5. Test-only changes that exist only in the staged copy:
   - `playModeTestRunnerEnabled` is set in `ProjectSettings.asset` ("Enable playmode tests for all
     assemblies"), so `Assembly-CSharp` references the test runner and can hold PlayMode tests.
   - `MajdataPlay.Tests.Internals.cs` files add `InternalsVisibleTo` attributes to `Assembly-CSharp` (for
     `Assembly-CSharp-Editor`) and to every asmdef under `Assets/Plugins/MajdataPlay` (for every test
     asmdef and its `.PlayMode` variant), so tests reach `internal` members directly.

## Layout

| Folder | Compiles into | Platform |
| --- | --- | --- |
| `Shared/` | `MajdataPlay.Tests.Shared` (auto referenced) | EditMode and PlayMode helpers |
| `Game/<Area>/` | `Assembly-CSharp-Editor` (staged under an `Editor` folder, no asmdef) | EditMode |
| `Game/<Area>/PlayMode/` | `Assembly-CSharp` (staged outside `Editor` folders, no asmdef) | PlayMode |
| `Plugins/<Module>/` | `MajdataPlay.Tests.<Module>` asmdef with `includePlatforms: ["Editor"]` | EditMode |
| `Plugins/<Module>/PlayMode/` | `MajdataPlay.Tests.<Module>.PlayMode` asmdef without platform restriction | PlayMode |

`Assembly-CSharp` cannot be referenced from an asmdef, so tests of `Assets/Scripts` compile into the
predefined assemblies: EditMode tests into `Assembly-CSharp-Editor`, PlayMode tests into
`Assembly-CSharp` itself. Every test is written with the Unity Test Framework (`[Test]` for synchronous
logic, `[UnityTest]` coroutines for anything that needs frames, lifecycle messages or async code).

MonoBehaviour components are covered mainly by PlayMode `[UnityTest]`s: the test adds the component,
yields frames and asserts on the result while Unity calls `Awake`, `OnEnable`, `Start`, `Update` and
coroutines itself. Entering Play Mode runs the game's `[RuntimeInitializeOnLoadMethod]` hooks, such as
`MajEnv.Init`, inside the staged project. In EditMode Unity does not call these messages on ordinary
behaviours, so EditMode tests of components create them with `TestObjects`, set serialized fields with
`PrivateMember` and deliver messages with `UnityMessages`.

`Game/Harness` and `Plugins/Harness` check the staging itself: that `Assembly-CSharp` and its internals
are reachable, that the helpers work, and that both kinds of PlayMode tests run inside Play Mode.

### Shared helpers (`MajdataPlay.Tests`)

- `TestObjects`: creates and tracks game objects and components; `Dispose` destroys them.
- `PrivateMember`: reads and writes non-public fields and properties (including get-only auto property
  backing fields) and invokes non-public methods across the type hierarchy.
- `UnityMessages`: sends parameterless Unity messages and runs coroutine messages to completion.
- `TemporaryDirectory`: a unique directory under `Application.temporaryCachePath`, created and deleted
  through `MajdataPlay.IO.Storage.FileSystem`.

## Writing tests

- Follow the repository `.editorconfig` and `AGENTS.md`: `#nullable enable`, block namespaces, `using`
  directives outside the namespace, CRLF, braces on every statement, `var`, and English XML documentation
  on every member, including test methods.
- Namespaces follow folders: `MajdataPlay.Tests.Game.<Area>[.PlayMode]` and
  `MajdataPlay.Tests.Plugins.<Module>[.PlayMode]`. Code under `Game/<Area>/PlayMode` compiles into
  `Assembly-CSharp` and must not use `UnityEditor`.
- MonoBehaviour test doubles go in their own file named after the class, inside a `PlayMode` folder.
  Unity refuses to attach behaviours compiled into editor assemblies, so doubles used by EditMode tests
  live in `Game/<Area>/PlayMode` (visible from `Assembly-CSharp-Editor`) or in
  `Plugins/<Module>/PlayMode` (referenced by the module's EditMode asmdef).
- Every `Plugins/<Module>` folder keeps exactly one EditMode asmdef named `MajdataPlay.Tests.<Module>`;
  an optional `PlayMode` subfolder holds `MajdataPlay.Tests.<Module>.PlayMode`. These names are what the
  staged `InternalsVisibleTo` attributes grant.
- Keep tests deterministic and isolated. Restore any static state a test changes (for example `MajEnv` or
  `MajInstances` members) in `TearDown`, destroy every Unity object, and use `TemporaryDirectory` instead of
  user or project data. Unexpected error logs fail a test; expect them with `LogAssert.Expect`.
- Async code uses `[UnityTest]` with `UniTask.ToCoroutine(async () => ...)`.
- Never replace a `static readonly` field (or a get-only static auto property's backing field) through
  reflection, for example `MajEnv.SharedHttpClient`. Mono's JIT folds such fields into compiled code, so
  methods compiled while the replacement is installed keep its address after it is collected and later
  tests read freed memory. Change the state inside the existing object instead; for HTTP clients, swap the
  `HttpMessageInvoker` handler as `Game/Misc/Support/StubHttpScope` does.
- Do not expect logs from different threads in a fixed order with `LogAssert.Expect`; record them with
  `Application.logMessageReceived` and assert on the set instead.
- Do not change production code from a test change. A test that documents a known production defect is
  marked `[Ignore("Known issue: ...")]` and reported.

## Limitations

- The suite runs in the Windows (or host) Editor. Branches under `UNITY_ANDROID`, `UNITY_IOS`,
  `UNITY_STANDALONE_LINUX`, `UNITY_STANDALONE_OSX` or `!UNITY_EDITOR` are not compiled and cannot be
  covered; use the target-specific validations in `../Modules`.
- Native libraries are loaded only where the tests explicitly need them. Real audio output, FFmpeg
  encode/decode on devices, serial/HID hardware, touch panels, network services and GPU sharing are not
  exercised; see the corresponding validations in `../Modules`.
- Passing tests do not prove Player builds, IL2CPP, or target devices work.
