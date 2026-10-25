# Translation editor validation

## Editor workflow

Open **Window > Manage translations** in Unity 6000.3.17f1.

1. Finish script compilation, then choose **Analyze MajdataPlay.Core**. The scan reads method
   bodies, setting metadata and the `LocalizeTMP` keys of scenes and prefabs; it shows a
   cancelable progress bar and keeps the previous results when cancelled.
2. Inspect **Review**: it lists the template diff. **+ Add** keys were found but are missing
   from the template; **- Remove** keys are in the template but were not found. Required
   additions start checked; optional additions (numeric option values from Range/Step) and
   removals start unchecked. Filter by change type or search, use **Check listed** /
   **Uncheck listed** / **Check required additions** for batches, and choose **Apply checked**.
   Hover over a key for its sources. Nothing is written until **Save template** or **Save all**.
3. Inspect **Analysis**: hover over a fixed key for method/IL, setting-property or asset sources;
   conditional settings show their ShowInSettingUIWhen condition. Review unresolved calls and
   runtime-dependent values separately.
4. Select a language in **Translations**, choose **Add missing template keys** (or the
   all-languages variant), edit its text and metadata, and choose **Save language**. **Remove
   keys not in template** cleans up one or all languages after confirmation. Empty translations
   require confirmation when saving. **Remove filtered keys** in **Template** removes every key
   matching the search after confirmation.

Editing uses Unity-serializable entries rather than KeyValuePair. Unsaved documents survive
assembly reloads and participate in the window's save/discard close prompt. Changes support
Undo until a save or reload resets the window's undo history. Files changed externally after
loading are not overwritten: reload the resources first. Existing asset GUIDs are retained.
Both Translations dictionaries and legacy MappingTable resources are supported; their schema
and additional top-level JSON metadata are retained on save.

## Static analysis scope

The IL analyzer matches the real MajdataPlay.StringExtensions.i18n and Tryi18n methods and
MajdataPlay.i18n.Localization.TryGetLocalizedText. It interprets evaluation stacks, locals,
control-flow joins and supported constant string operations rather than collecting every
nearby string literal. Nested compiler-generated async/coroutine/lambda methods are included.
Readonly initializers and finite custom option arrays are inspected as IL, never executed.

The setting analyzer models the current SettingManager/Menu/OptionData/OptionEnumeratorBase
schema: visible categories (including Audio.Volume and ChartSetting), name/description
attributes, finite Boolean/enum/string labels, property-specific and general value fallbacks,
null sentinels and the fixed offset-unit labels. Numeric options that opt into value
translation contribute their finite Range/Step grid (formatted with the invariant culture) as
optional keys; other numeric values, skin/language inventories and arbitrary user input are not
treated as fixed values. ShowInSettingUIWhen conditions are recorded in the sources, and
unresolvable condition paths are reported. Adding a new built-in enumerator or changing the
setting UI's key-generation rules requires updating this schema.

Before decoding a method body, the IL analyzer scans it for a call to a localization entry
point, resolving only call tokens, and skips bodies without one.

Analysis is conservative and bounded. Dynamic getter results, arbitrary helper methods,
keys assigned to components other than `LocalizeTMP`, binary-serialized assets, runtime array
mutations and inactive platform branches are not an exhaustive key inventory. Unsupported or unresolved cases produce diagnostics. Only the
currently compiled assembly is available; this does not replace target-platform review.

## Fast managed regression tests

With .NET SDK 9 or newer, run from the repository root:

~~~powershell
dotnet run --project Tools/Tests/Modules/TranslationValidation/TranslationValidation.csproj
~~~

See TranslationValidation/README.md for the fixture coverage and limitations. These tests
link the production analyzers but do not exercise Unity's UI or native lifecycle.

## Isolated Unity editor validation

~~~powershell
./Tools/Tests/Modules/ValidateTranslationEditor.ps1
~~~

The script requires Unity **6000.3.17f1** and the main project's locally cached Newtonsoft JSON
package. It copies only the production editor files, guarded runtime fixtures and language
resources into ignored Temp/TranslationEditorValidation. It runs a hidden batch-mode editor
with a graphics device, which UI Toolkit needs to dispatch control events. It never opens or
rewrites the main project's scenes, settings or language resources.

The harness checks:

- Production runtime/editor assembly boundaries, fixed-key discovery and zero guarded game
  method/getter/attribute/enumerator/initializer execution.
- Repeated UI creation, language selection, recycled text fields and metadata editing.
- Actual UI Toolkit panel geometry for all four tabs at 760x420, 1200x760, 480x360 and
  960x320: wrapped toolbars/search/summary labels, bounded list viewports, visible manual-key
  controls and non-overlapping editing columns with long keys and multiline translations.
- Reviewed template synchronization (analysis leaves the template untouched; applying checked
  additions is idempotent) and translation synchronization, preserving existing text.
- Serialized language state, unsaved-change flags, template/language save and JSON round trips.
- Both language schemas, duplicate mapping semantics, retained metadata, invalid-key rejection
  and conflict protection when a file changes externally.

Expected marker: **TRANSLATION_EDITOR_VALIDATION_PASSED** with an assertion count. Logs and
all writes remain under Temp/TranslationEditorValidation. These are structural/control-flow
checks, not a rendered-pixel or manual interaction test. Main-project import/Console review,
visual layout inspection and other target-platform branches must be verified separately.
