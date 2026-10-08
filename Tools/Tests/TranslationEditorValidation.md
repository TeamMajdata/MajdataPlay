# Translation editor validation

## Editor workflow

Open **Window > Manage translations** in Unity 6000.3.17f1.

1. Finish script compilation, then choose **Analyze Assembly-CSharp**.
2. Inspect **Analysis**: hover over a fixed key for method/IL or setting-property sources;
   review unresolved calls and runtime-dependent values separately.
3. In **Template**, choose **Add discovered keys** and **Save template**. Synchronization
   is additive: manually maintained, scene/prefab and other-platform keys are retained.
4. Select a language in **Translations**, choose **Add missing template keys**, edit its
   text and metadata, and choose **Save language**. Empty translations require confirmation.

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
null sentinels and the fixed offset-unit labels. Numeric values, skin/language inventories and
arbitrary user input are not treated as fixed values. Adding a new built-in enumerator or
changing the setting UI's key-generation rules requires updating this schema.

Analysis is conservative and bounded. Dynamic getter results, arbitrary helper methods,
serialized scene/prefab keys, runtime array mutations and inactive platform branches are not
an exhaustive key inventory. Unsupported or unresolved cases produce diagnostics. Only the
currently compiled assembly is available; this does not replace target-platform review.

## Fast managed regression tests

With .NET SDK 9 or newer, run from the repository root:

~~~powershell
dotnet run --project Tools/Tests/TranslationValidation/TranslationValidation.csproj
~~~

See TranslationValidation/README.md for the fixture coverage and limitations. These tests
link the production analyzers but do not exercise Unity's UI or native lifecycle.

## Isolated Unity editor validation

~~~powershell
./Tools/Tests/ValidateTranslationEditor.ps1
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
- Actual UI Toolkit panel geometry for all three tabs at 760x420, 1200x760, 480x360 and
  960x320: wrapped toolbars/search/summary labels, bounded list viewports, visible manual-key
  controls and non-overlapping editing columns with long keys and multiline translations.
- Additive template and translation synchronization, preserving existing text.
- Serialized language state, unsaved-change flags, template/language save and JSON round trips.
- Both language schemas, duplicate mapping semantics, retained metadata, invalid-key rejection
  and conflict protection when a file changes externally.

Expected marker: **TRANSLATION_EDITOR_VALIDATION_PASSED** with an assertion count. Logs and
all writes remain under Temp/TranslationEditorValidation. These are structural/control-flow
checks, not a rendered-pixel or manual interaction test. Main-project import/Console review,
visual layout inspection and other target-platform branches must be verified separately.
