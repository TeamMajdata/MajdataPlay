# Gameplay note sorting validation

Run with the repository-pinned Unity Editor:

```powershell
./Tools/Tests/Modules/NoteSortingValidation/run.ps1
./Tools/Tests/Modules/NoteSortingValidation/run.ps1 -SkipRendering
```

The script stages the complete production `NoteHelper.cs`, its sensor/slide enums,
the repository sorting-layer table and this harness in the ignored isolated project
`Temp/NoteSortingValidation`. It never opens a scene or changes settings in the main
project. The two unused sensor extension symbols are narrow substitutes that throw;
sensor mapping and other `NoteHelper` methods are outside this check.

The checks cover all category bands and every signed 16-bit order on the exact
2^-17 grid (0.5 units per category), clamping of int32 extremes to the 16-bit
boundaries, and body/end/EX priorities at in-range boundary orders. A world-depth
check reads the gameplay pool, NoteEffects and Init camera depths from the
serialized scenes, then requires every adjacent order in every category to keep a
distinct, exact world Z and camera distance under a real Transform, below 128
units and behind NoteEffects. Consecutive Tap, Hold, Touch and TouchHold
hierarchies are walked across the full 16-bit counter range, so every component of
an earlier note stays in front of the next note; pooled
SpriteRenderer layer/order/pivot reset; assignment of the Z index to local position
under translated, rotated and Z-scaled parents; and world/local XY movement while
preserving that index. Parent movement and reparenting with local coordinates
preserve the assigned local index while world depth follows the new parent. A tilted
parent also checks that setting local Z preserves local X and Y. Nested Tap/Hold
body, EX and endpoint renderers use relative child offsets, preserving their world
priority without duplicating the body category depth. Collapsed and expanded XY
scales preserve each child's depth as well.
The serialized scan checks complete SpriteRenderer records in `Game.unity`, every
Game prefab and UI TouchFeedback, plus sorting overrides in prefab variants and the
scene. It also checks the Hold particle, RawSprite TouchHold border and three
migrated world canvases. Relevant gameplay and feedback animation files cannot
restore renderer sorting properties. The real imported animation Z curves and
`AnimationClip.SampleAnimation` on disposable transform hierarchies are checked at
31 points per binding: local position Z stays zero, local scale Z stays one and the
mapped root depth is preserved. The scan does not load the complete game or resolve
arbitrary prefab inheritance.

The default command creates two overlapping opaque sprites at different nonzero
XY positions and checks rendered pixels with a real orthographic camera using
`CustomAxis`/forward sorting. Their camera distances oppose their Z priority, and
swapping their depth categories must swap the visible color. It then moves both
sprites under the serialized pool depth with the serialized camera depth and
renders four pairs exactly one order step apart, including a category boundary;
swapping the orders must swap the visible color. Direct3D11 is required on the
host; `-SkipRendering` uses `-nographics` and leaves rendering unverified.

Orders outside the signed 16-bit range are clamped and share the boundary depth;
that caps one chart at 65,536 counter units per counter (for example 32,768 Taps
or 10,922 Touches). The exact-grid guarantee also needs world Z and camera distance
below 128 units and note ancestors that only translate, rotate around Z and keep Z
scale one; the scene reader rejects tilted or Z-scaled ancestors. The script runs in
Windows PowerShell 5.1 or PowerShell 7. This harness does not validate the gameplay lifecycle,
Animator component integration, real chart playback, other graphics APIs, Player builds or
mobile devices. Success markers and the Editor exit code are checked; all generated
assets, assemblies and logs remain under `Temp/NoteSortingValidation`.

Local result (2026-10-18, exact-grid depths): Unity 6000.3.17f1 on Windows
PowerShell 5.1, Direct3D11, NVIDIA GeForce RTX 3060 Laptop GPU passed the full
command: 458,752 world-depth orders at pool depth 100 with camera -10 and
NoteEffects 95; 32,768 Tap, 21,845 Hold, 10,922 Touch and 10,922 TouchHold
consecutive notes; four one-step rendered pairs; and the same asset counts as below.

Earlier result (2026-10-16): Unity 6000.3.17f1 on Windows, Direct3D11,
AMD Radeon RX 580 2048SP passed the full command: 1,330 complete renderer records,
37 SpriteRenderer overrides across 71 scene/prefab files, and 26 animation clips.
The separate structural asset audit resolves inherited instances; its renderer
total can therefore differ from this direct-record count.
