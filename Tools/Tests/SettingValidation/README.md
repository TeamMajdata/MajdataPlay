# Setting behavior validation

From the repository root, with .NET SDK 9 or newer installed:

```powershell
dotnet run --project Tools/Tests/SettingValidation/SettingValidation.csproj
```

The project links the production `Menu`, `Option`, `SettingManager`, option
enumerators, settings attributes, and input repeat implementation directly. It
uses no NuGet packages and writes generated files under `Temp/SettingValidation`.

The harness checks:

- One shared pool creates four option views while traversing settings and
  categories; stationary menus show at most three, with four during a slide.
- Rapid reversal, cancellation, menu disable/re-enable, and category transitions
  return and reuse the existing views.
- External numeric, boolean, enum, nullable boolean, and read-only value changes
  update the display. Input received before the next display poll uses the latest
  numeric value; offscreen changes are picked up on rebind.
- 120 unchanged frames perform no TMP text assignments; localization refreshes
  text without accumulating event subscriptions.
- Returning a view preserves its enumerator; destroying its menu disposes it.
- Changing offset units while the offset is zero updates step and description;
  engine/audio enumerators synchronize external changes and preserve side effects.
- Reflection arrays, option descriptors, attributes, and enum values are reused
  across scene recreation without retaining old settings instances. Every menu's
  enumerators are initialized before input; first traversal creates no additional
  enumerators or attribute instances.
- Eager preparation preserves a deserialized note-mask selection; activation
  callbacks do not reparent pooled views inside a changing hierarchy.

Expected result: `SETTING_VALIDATION_PASSED` with an assertion count.

Unity objects, TMP, LitMotion scheduling, localization, and external game services
are narrow test doubles. The tests check production control flow and TMP setter
calls, not rendered pixels, Canvas rebuilds, real LitMotion timing, or Unity's
native lifecycle. Compile the project with Unity and inspect Setting in Play Mode
for those integration checks.

[Setting text validation](../SettingTextValidation.md) additionally runs the
production tint, category title, and font warmup against real TMP in an isolated
Unity project, including dirty callbacks and fallback glyph meshes.
