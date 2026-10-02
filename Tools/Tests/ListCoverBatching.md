# List cover UI batching

`Main_Display_ribbon/CoverList/SongCovers` and the visible graphics in
`SongCoverDisplayer` use a shared RectTransform Z plane. Four shared List material
presets restore the original depth in the vertex shader through `_ListDepthOffset`.

| Graphic | Material offset in Canvas units |
| --- | ---: |
| Difficulty text | 150 |
| Cover art and difficulty background | 151 |
| Cover background/shadow | 152 |

The depth offsets preserve the existing depth tests and occlusion while allowing
Canvas to batch matching materials across the nine pooled cards. Keeping the
original Z hierarchy, or moving that Z into CPU mesh vertices, prevented that
cross-card batching. Simply clearing Z without restoring GPU depth lost cover art,
backgrounds and icons.

The shader property defaults to zero. Existing materials, the center cover's
shared LevelDisplayer, render queues, blending and stencil settings retain their
behavior. The List level-text preset references the existing Jua font atlas;
normal TMP fallback materials inherit its properties. No per-frame helper,
extra Canvas, material instantiation or additional vertex channel is required.

Loading retains its ordinary UI material and a local Z of 150. Its geometry is
only active while loading, so its original occlusion can be retained without
another shader variant. List movement and selection continue to use XY scale
with Z scale 1. The material offsets are applied before object-to-clip projection,
so parent Canvas scaling also scales the restored depth.

## Validation

Measured with Unity 6000.3.25f1, URP/Metal, using the actual prefab dependencies in
an isolated project. The project's requested editor version is 6000.3.17f1; the
complete game and its external submodules were not built by this rendering check.

- Main UI prefab: **222 -> 78** draw calls, with **3021 vertices** in both cases.
  Edit-mode Game view also measured 221 -> 77; the reduction remained 144.
- UI Profiler: CoverList's own batches **162 -> 18**; the nine cards **153 -> 9**.
  Canvas count, rendered-object membership, and vertex count were unchanged.
- Profiler batch breaks labeled `DifferentMaterialInstance` fell by 144.
  Texture and Canvas-injection break counts were unchanged. The material-boundary
  label describes the observed break; the before/after depth experiment identifies
  why those matching materials could not previously be combined across cards.
- All **16 paired 1080 x 1080 image comparisons were pixel-identical**: authored
  preview, scrolling through 0/0.25/0.49/0.5/0.51/0.75/1 and negative fractions,
  different cover textures, sparse difficulties, Unicode fallback text, loading,
  parent XYZ scales 0.7/1.3, and Screen Space Camera references 1080/1440.
- Different-cover scroll cases measured 143 -> 73/74; loading measured 146 -> 97.

For a full-project check, open List in Game view and compare its Stats at the same
resolution, quality and hierarchy state. In Play Mode, record the UI Profiler and
expand CoverList to inspect material groups across cards. Unity exposes the
per-batch UI details in Editor Play Mode, not in edit-mode profiling.

These counts cover the main UI prefab, not every draw in the complete List scene;
other canvases, Live2D and different song textures contribute to the scene total.
