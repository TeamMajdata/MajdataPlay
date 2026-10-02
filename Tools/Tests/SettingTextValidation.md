# Setting text animation regression

Run from the repository root after opening the project in Unity at least once:

```powershell
./Tools/Tests/ValidateSettingText.ps1 -UnityPath 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Unity.exe'
```

The script stages an isolated project in `Temp/SettingTextValidation`, references
the locally cached uGUI package, and copies TMP Essential Resources and the test
font into that project. It runs Unity in batch mode without graphics. It does not
open or modify the main project's scene or font assets. The log is
`Temp/SettingTextValidation/validation.log`.

The test uses the production `SettingTextTint`, `MenuTitleDisplayer`, and
`SettingFontWarmup` with real TMP components and font atlases. Only localization
is stubbed. Ordinary title lifecycle methods are invoked explicitly because the
harness runs in Edit Mode.

Measured with Unity 6000.3.17f1, 120 animation samples per case:

| Case | TMP vertices dirty callbacks | TMP layout dirty callbacks | TMP mesh generation callbacks |
| --- | ---: | ---: | ---: |
| Baseline `TMP_Text.color` animation | 120 | 0 | 120 |
| Renderer tint with position/scale animation | 0 | 0 | 0 |
| Production category title animation | 0 | 0 | 0 |

The harness also checks real fallback submesh tint throughout animation and after
text changes; changed text still regenerates. Bulk font warmup handles fallback
cycles, static fonts, surrogate pairs, invalid UTF-16 and control characters
without changing font feature preferences. Rendering the warmed Chinese/numeric
corpus, and a second locale corpus, adds no glyphs during PreRender.

Expected result: `SETTING_TEXT_VALIDATION_PASSED` (currently 514 assertions).
These are regeneration and glyph-growth checks, not a rendered-image comparison
or a measurement of the full Setting scene's frame time.
