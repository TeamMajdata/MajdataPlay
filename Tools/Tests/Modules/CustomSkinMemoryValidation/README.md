# CustomSkin memory validation

Measures the process memory peak while the production `CustomSkin` loads a real skin, on
Windows with the repository-pinned Unity Editor:

```powershell
./Tools/Tests/Modules/CustomSkinMemoryValidation/run.ps1            # Editor, budget on what the load adds
./Tools/Tests/Modules/CustomSkinMemoryValidation/run.ps1 -Player    # standalone player, budget on the whole process
./Tools/Tests/Modules/CustomSkinMemoryValidation/run.ps1 -Baseline  # same measurement of the committed HEAD sources
```

Options: `-SkinPath <dir>` (default `Assets/StreamingAssets/Skins/default`), `-BudgetMiB`
(default 1024), `-GraphicsApi d3d11|glcore|vulkan`, `-UnityPath` (default: the Unity Hub or
`C:/Program Files/Unity Editors` install of the pinned version).

The script stages an isolated project in `Temp/CustomSkinMemoryValidation` with the current
(or, with `-Baseline`, committed) `CustomSkin.cs`, `AtlasLayout.cs`, `DecodedImage.cs`, `EncodedImage.cs`,
`TextureLoader.cs` and `SKBitmapExtensions.cs`, the portable storage sources, the real
SkiaSharp binding compiled from `Assets/Plugins/SkiaForUnity/SkiaSharp-Bindings` with its
Windows x64 native library, and UniTask, Burst, Collections and Mathematics. `Services.cs`
stubs only the pool list, logging, the empty-skin placeholder and the note mesh cache.

Each load mode (`async` = `CustomSkin.LoadAsync`, `sync` = the loading constructor) runs once
in a fresh process, so the first load's peak is not hidden by memory reused from an earlier
load. A background thread samples `PrivateUsage` and the working set every millisecond while
the skin loads. The probe then checks that the atlas sprites share one non-readable texture and
reports memory released by `UnloadAsync`. `CUSTOM_SKIN_MEMORY_MANAGED` reports Mono heap growth
during the load; in the player the collector is paused for that span (`collections=0`), so the
growth is the managed memory the load allocated. In the Editor the collector cannot be paused and
the figure is only indicative. The managed figure is reported, not budgeted.

- **Editor**: the Editor alone commits more than 1 GiB, so the budget applies to what the load
  adds: peak private-byte growth plus graphics-driver growth (`CUSTOM_SKIN_MEMORY_CASE`).
- **Player** (`-Player`): a Windows x64 development player is built and launched windowed.
  The budget applies to the absolute process: peak private bytes plus all graphics-driver
  allocations after loading (`CUSTOM_SKIN_MEMORY_PROGRAM_PEAK`). This double-counts driver
  memory that is already in private bytes, so it is conservative.

Success is `CUSTOM_SKIN_MEMORY_VALIDATION_PASSED` in each log
(`validation-<mode>-<api>.log` or `player-<mode>-<api>.log`) and a zero exit code.

This does **not** validate the full game (its scenes, audio and other resources add to the
player figure), other graphics APIs than the one selected, Android, iOS, Linux, macOS,
32-bit Windows, IL2CPP, or GPU-side memory on a discrete card beyond Unity's driver counter.
In the Editor, `Sprite.OverrideGeometry` logs "Not allowed to override geometry" for every
sprite; this happens with the committed sources too and is outside this check.
