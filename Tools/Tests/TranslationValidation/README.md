# Translation analyzer validation

From the repository root, with the .NET 9 SDK and runtime installed:

```powershell
dotnet run --project Tools/Tests/TranslationValidation/TranslationValidation.csproj
dotnet run --configuration Release --project Tools/Tests/TranslationValidation/TranslationValidation.csproj
```

Expected result: `TRANSLATION_VALIDATION_PASSED` with an assertion count and exit code 0.
A failed group or unexpected scan error prints `TRANSLATION_VALIDATION_FAILED` and
returns exit code 1. Debug and Release exercise different compiler IL shapes.

This is a standalone `net9.0` console project, not a build of the Unity-generated
root solution or project. It uses no NuGet packages, Unity stubs, or third-party
libraries. Restore sources are restricted to this directory and NuGet auditing is
disabled. Generated outputs go to the ignored `Temp/TranslationValidation` tree;
the local `.gitignore` only unignores this independent test project.

## Verified results on October 8, 2026

Both commands above passed all 14 test groups:

| Configuration | Result | Assertions | Exit code |
| --- | --- | --- | --- |
| Debug | TRANSLATION_VALIDATION_PASSED | 591 | 0 |
| Release | TRANSLATION_VALIDATION_PASSED | 591 | 0 |

The execution guard remained at zero. The regression for two different literal
arrays joining before an OptionValues assignment is retained unchanged; both
branch alternatives must be returned by the raw extractor and produce setting
value keys. This test caught and verified the IL analyzer's branch-array fix.

The parent task separately reported a hidden, GPU-backed Unity 6000.3.17f1
isolation run passing 42 assertions, including runtime/editor assembly boundaries,
NoteMask discovery, window controls, saving and serialization. See the existing
[translation editor validation guide](../TranslationEditorValidation.md) for that
companion harness and its integration limits. That result was reported by the
parent task, not executed or inferred by this standalone .NET harness.

## Linked production code

- `Assets/Scripts/Editor/Windows/TranslationCallAnalyzer.cs`
- `Assets/Scripts/Editor/Windows/TranslationSettingAnalyzer.cs`

The pure managed analyzers live in Unity's Editor directory, but this standalone
project does not reference Unity assemblies. The language version is C# 9. The
fixture sources and `ExecutionGuard.cs` can also be copied into an isolated Unity
runtime assembly; `Program.cs` must remain in this standalone project because it
references the analyzers directly.

## Coverage

### Localization call analysis

Fixtures use the exact production declaring types and signatures:

- `MajdataPlay.StringExtensions.i18n(string, params object[])`
- `MajdataPlay.StringExtensions.Tryi18n(string, out string)`
- `MajdataPlay.i18n.Localization.TryGetLocalizedText(string, out string)`

The harness checks literals, constants, extension-call syntax, local propagation
and reassignment, branch/switch joins, string concatenation, case sensitivity,
and preservation of multiple origins. Unused nearby strings, overwritten
constants, format payloads, existing out values, and identically named methods
on unrelated declaring types must not become keys. An out write must invalidate
its old local value. Nested methods, lambdas, async state machines and coroutine
state machines must be scanned. Dynamic parameters, mixed dynamic branches and
unresolved readonly fields must produce diagnostics identifying their source.

### Setting metadata and finite values

The fixtures declare production-named `GameSetting` and `ChartSetting` types and
narrow attribute/option-enumerator types. Checks follow the current UI schema:

- Visible category names are based on the actual bound property; `Audio.Volume`
  binds the `Volume` category and does not expose other `SoundOptions` properties.
- `OptionName` and `Description` replace default name/description keys;
  `NoDescription` suppresses description keys. `MenuName` does not replace
  category names in the current setting UI.
- Boolean labels use `False` and `True`; only `Optional` adds `UNSET` to the
  nullable boolean enumerator. Both property-specific and general fallback keys
  are required, using the original property name even with an override.
- Enum values follow `Enum.GetValues(...).ToString()`, including aliases, flags,
  nonsequential underlying numbers and numeric-looking names such as `_90`.
- Note-mask labels and custom string arrays assigned through locals/branches are
  finite. The raw array extractor preserves numeric strings; the setting scanner
  excludes numeric display strings from its non-numeric key inventory.
- Hidden categories/options, indexers and properties without public getters are
  excluded. Numeric values and runtime skin/language names are not guessed;
  the language enumerator's fixed `Unavailable` fallback is retained.
- Dynamic custom enumerators and free-form strings must not leak filesystem
  identifiers, logging literals, default values or superseded array contents.

Both collectors must preserve existing keys, source sets and diagnostics;
repeated collection must leave key/source inventories unchanged. Required null
arguments must be rejected.

## Safety and limitations

Localization calls, setting constructors/getters, custom attribute constructors,
option-enumerator constructors, runtime value providers and static readonly
initializers are guarded with throwing counters. The harness requires zero
executions. A literal readonly field may be recovered from IL or reported as
unresolved; it must never be read by executing the type initializer.

This project checks the actual linked managed analysis logic against narrow
fixtures. Its scanners and fixtures share one .NET assembly; it does **not**
verify Unity's editor/runtime assembly boundary, the real Assembly-CSharp IL,
platform-specific compiled settings branches, translation JSON, window UI,
asset loading, Unity serialization, Player builds or rendered behavior. The
fixture API methods intentionally throw if executed and do not implement actual
localization or option interaction. The parent task owns isolated Unity Editor
and window integration validation with Unity 6000.3.17f1.

Additional regression fixtures check superseded direct OptionValues field writes,
known element mutations, dynamic overwrites, and arrays escaped to opaque helpers.
Stale values must not be reported as fixed keys.
