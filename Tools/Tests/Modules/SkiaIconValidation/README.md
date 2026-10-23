# Skia editor icon validation

Run from the repository root with Unity **6000.3.17f1**:

```powershell
./Tools/Tests/Modules/SkiaIconValidation/run.ps1
```

Pass `-UnityPath 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Unity.exe'`
if the Editor is installed elsewhere. The runner checks its version and opens
only the ignored isolated project at `Temp/SkiaIconValidation` in hidden batch
mode. The main Unity project is neither opened nor changed.

The isolated project copies the production `HBIconAssigner.cs`, the storage
filesystem sources, and the relevant production assembly definitions and GUIDs.
A local `com.ammariqais.skiaforunity` package contains four empty MonoBehaviour
fixtures in place of the real Skia runtime. No native Skia library or registry
package is required. The icon directory starts empty on each run.

The harness checks real Unity Editor behavior:

- Four PNGs and their metadata are generated physically under
  `ThirdParty/SkiaForUnity/Editor/Icons` and imported through
  `Packages/com.ammariqais.skiaforunity/Editor/Icons`.
- All four script icons and texture import settings are correct.
- Repeated assignment, including clearing script icons and reusing package
  textures, changes neither the generated PNGs nor metadata bytes or timestamps.
- A custom script icon is preserved, while an icon from the obsolete
  `Assets/SkiaSharp` directory is rebound to its package texture.
- Ordinary assignment creates neither `Assets/SkiaSharp` nor its `.meta` file;
  removing the isolated migration fixture does not cause them to reappear.

Expected marker: `SKIA_ICON_VALIDATION_PASSED`. The detailed log is
`Temp/SkiaIconValidation/validation.log`.

To prove the regression against the currently committed production callback:

```powershell
./Tools/Tests/Modules/SkiaIconValidation/run.ps1 -Baseline
```

This reads the callback from `git show HEAD:ThirdParty/SkiaForUnity/Editor/HBIconAssigner.cs`
without modifying the working tree. Before the fix is committed, it should fail
with `Assets/SkiaSharp was not created.` in `validation-baseline.log`.

This is an Editor import and icon-assignment check with small component fixtures.
It does not validate the main project, Skia rendering, Player builds, other
platforms, or native library integration.
