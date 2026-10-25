# MajdataPlay Tests

Unity Test Framework suite for the main project (`Assets/Scripts`, compiled into `MajdataPlay.Core` and
`MajdataPlay.Core.Editor`) and the reusable libraries under `Assets/Plugins/MajdataPlay`, including
MonoBehaviour components. Per-module standalone validation programs live in
[`Tools/Tests/Modules`](../../Tools/Tests/Modules/README.md).

The test assemblies are ordinary Unity Test Framework assemblies of the main project: they appear in
**Window > General > Test Runner** and run there as well as from
[`Tools/Tests/Run-UnityTests.ps1`](../../Tools/Tests/Run-UnityTests.ps1). Every test asmdef has
`defineConstraints: ["UNITY_INCLUDE_TESTS"]` and `autoReferenced: false`, so Player builds never contain
them and production assemblies cannot reference them.

## Layout

| Folder | Assembly | Platform |
| --- | --- | --- |
| `Shared/` | `MajdataPlay.Tests.Shared` | Any; helpers for EditMode and PlayMode tests |
| `EditMode/Game/<Area>/` | `MajdataPlay.Tests.Game` (one asmdef for all areas) | Editor (EditMode) |
| `PlayMode/Game/<Area>/` | `MajdataPlay.Tests.Game.PlayMode` (one asmdef for all areas) | Any (PlayMode) |
| `EditMode/Plugins/<Module>/` | `MajdataPlay.Tests.<Module>` | Editor (EditMode) |
| `PlayMode/Plugins/<Module>/` | `MajdataPlay.Tests.<Module>.PlayMode` (optional) | Any (PlayMode) |
| `EditMode/ThirdParty/<Library>/` | `MajdataPlay.Tests.ThirdParty.<Library>` | Editor (EditMode) |
| `PlayMode/ThirdParty/<Library>/` | `MajdataPlay.Tests.ThirdParty.<Library>.PlayMode` | Any (PlayMode) |

`<Area>` mirrors the folder under `Assets/Scripts` that is tested (`GamePlay`, `Misc`, `Scenes`, ...),
`<Module>` mirrors the library folder under `Assets/Plugins/MajdataPlay` (`Buffers`, `Net`, ...) and
`<Library>` names the third-party library whose changes in this repository are tested (`UniTask` and
`SkiaForUnity` under `ThirdParty`, the `ManagedBass` submodule under `Assets/Plugins`).

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

Every assembly that a test asmdef references (the production asmdefs under `Assets/Scripts` and
`Assets/Plugins/MajdataPlay`, and the PlayMode test asmdefs that EditMode tests reference) has an
`AssemblyInfo.cs` next to its asmdef with one `[assembly: InternalsVisibleTo("...")]` per test assembly
that references it. Tests therefore reach `internal` members directly, and EditMode tests can use the
internal test doubles of the PlayMode test assemblies.

When a test asmdef is added, renamed, or gains a reference to another `MajdataPlay.*` assembly, add or
update the matching `InternalsVisibleTo` line in that assembly's `AssemblyInfo.cs` (create the file and its
`.meta` when the assembly has none). Grant only test assemblies, and only the ones that reference it.

### Shared helpers (`MajdataPlay.Tests`)

- `TestObjects`: creates and tracks game objects and components; `Dispose` destroys them.
- `PrivateMember`: reads and writes non-public fields and properties (including get-only auto property
  backing fields) and invokes non-public methods across the type hierarchy.
- `UnityMessages`: sends parameterless Unity messages and runs coroutine messages to completion.
- `TemporaryDirectory`: a unique directory under `Application.temporaryCachePath`, created and deleted
  through `MajdataPlay.IO.Storage.FileSystem`.

## Running

### In the Editor

Open the project in Unity 6000.3.17f1, then **Window > General > Test Runner**. The **EditMode** tab lists
the `MajdataPlay.Tests.<Module>` and `MajdataPlay.Tests.Game` assemblies, the **PlayMode** tab the
`*.PlayMode` assemblies. Run all, a selection, or a single test, and debug them with an attached IDE.
PlayMode runs enter Play Mode in a temporary scene and restore the open scenes afterwards; save scene
changes first.

### Batch mode

[`Tools/Tests/Run-UnityTests.ps1`](../../Tools/Tests/Run-UnityTests.ps1) runs the suite in batch mode in an
isolated copy of the project, so the main project, its `Library` and its serialized assets are only read
and the suite can run while the project is open in the Editor. The staged copy is the repository project
unchanged except for the Code Coverage package, so it compiles and runs the same tests as the Test Runner
window.

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
   `robocopy /MIR` (`rsync --delete` elsewhere); files that exist only in the staged copy are deleted.
   `ThirdParty` keeps its relative location because `Packages/manifest.json` references local packages
   with `file:../ThirdParty/...`. Untracked nested Git repositories under `Assets` (for example a stray
   `Assets/Plugins/UniTask` checkout that duplicates the UniTask package) are skipped with a warning.
2. `Packages/manifest.json` is copied with `com.unity.testtools.codecoverage` added. `packages-lock.json`
   is copied only when the repository's lock file changes.
3. A missing staged `Library` is seeded from the main project's `Library` when it exists, skipping the
   full import of the first run.

## Writing tests

- Follow the repository `.editorconfig` and `AGENTS.md`: `#nullable enable`, block namespaces, `using`
  directives outside the namespace, CRLF, braces on every statement, `var`, and English XML documentation
  on every member, including test methods.
- Namespaces follow the tested code: `MajdataPlay.Tests.Game.<Area>` for `EditMode/Game/<Area>`,
  `MajdataPlay.Tests.Game.<Area>.PlayMode` for `PlayMode/Game/<Area>`,
  `MajdataPlay.Tests.Plugins.<Module>` for `EditMode/Plugins/<Module>`,
  `MajdataPlay.Tests.Plugins.<Module>.PlayMode` for `PlayMode/Plugins/<Module>` (some existing PlayMode
  helpers shared with EditMode tests keep the EditMode namespace),
  `MajdataPlay.Tests.ThirdParty.<RootNamespace>` for `EditMode/ThirdParty/<Library>` and
  `MajdataPlay.Tests.ThirdParty.<RootNamespace>.PlayMode` for `PlayMode/ThirdParty/<Library>`, where
  `<RootNamespace>` is the library's root namespace (`Cysharp` for UniTask, `SkiaSharp` for SkiaForUnity,
  `ManagedBass` for ManagedBass) so that a namespace segment does not hide a type of the same name such as
  `UniTask`. Inside `MajdataPlay.Tests.ThirdParty.ManagedBass`, refer to ManagedBass types through `using`
  directives, as a qualified `ManagedBass.` name resolves to the test namespace. Code under `PlayMode/`
  compiles for every platform and must not use `UnityEditor`.
- Each module has exactly one EditMode asmdef named `MajdataPlay.Tests.<Module>` and, optionally, one
  PlayMode asmdef named `MajdataPlay.Tests.<Module>.PlayMode`. Test asmdefs follow the Unity Test Framework
  layout: they reference `UnityEngine.TestRunner` (EditMode asmdefs also `UnityEditor.TestRunner` and
  `includePlatforms: ["Editor"]`), set `overrideReferences: true` with `nunit.framework.dll` in
  `precompiledReferences`, `autoReferenced: false`, and `defineConstraints: ["UNITY_INCLUDE_TESTS"]`.
  Reference `MajdataPlay.Tests.Shared` when using the helpers, and grant internals as described under
  [Access to internals](#access-to-internals). Production asmdefs never reference test assemblies.
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
