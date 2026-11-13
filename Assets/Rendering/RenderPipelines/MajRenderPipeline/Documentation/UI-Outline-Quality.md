# UI modes and outline quality

Implemented for Unity 6000.3.17f1. The single Custom **UI Style** setting now
selects artwork, compositing and whether captured backgrounds are enabled; MSAA
and scene resolution remain independent. None, Performance and All do not
capture. GaussianBlur and LiquidGlass derive the All theme and select their
respective three-stage backdrop treatment.

The Refined preset exposes **Rendering Style: Gaussian Blur / Liquid Glass**
(formerly Panel Style) outside the Custom-only controls. It keeps Refined selected
and persists the preference as `UIBackdropStyle`, mapping it to UI mode 3/4.
Entering Custom inherits the current mode. Compatibility temporarily selects
None and hides these controls without overwriting the saved configuration.

Current capture uses three independent cuts and six persistent RGBA8 textures:
four quarter-size Gaussian results/scratch and two half-size raw liquid snapshots.
Gaussian uses fixed sigma 4 / radius 8, nine paired bilinear reads per axis.
Liquid uses the same quarter-size Board result and separate half-size bilinear copies without Gaussian for
categories and peripheral buttons. Source-Sprite contours and the exact native
Rounded Rect coverage reuse retain their established optics; original artwork,
Alpha, decorations and neutral Max highlights are preserved. The earlier direct
shader port is no longer a dependency. See [current design](LiquidGlass-Design.md)
for source ordering, optical eligibility and historical reference details.

The latest cleanup was static only at the user's request: no tests, compilation,
Unity interaction or device measurements. It removes unused variant work and
dead code without changing the accepted optical recipe. The subsequent requested
quarter-grid Gaussian changes the filter/reconstruction and awaits the user's
visual review; no equality or measured FPS gain is claimed. User-accepted
pre-cleanup device observations are recorded in the MRP README; they are not
measurements of this cleanup. Validation counts below belong to their dated
implementation and do not establish current performance or absolute optimality.

## Shared List and Setting category carousel

Every pooled List category now delegates position and selected appearance to the
existing Setting `MenuTitleDisplayer`. The background, name and type icon move
together. Approaching the selection cursor expands the tab, blends to its blue
selected background and highlights the label. The former fixed central bar and
duplicate name/icon have been removed. The original feedback arrows remain as
an independent Navigation group at the expanded tab's left and right ends.

List keeps its existing input, collection identity, ordering, difficulty and
pooling logic. All nine nested prefab instances inherit the shared unselected
147.83 x 76.8 size rather than their old 100 x 100 overrides; the selected size is
254.69 x 82.3. Neighboring tabs remain visible without a screen-space fade.

The previous collection-occlusion globals, material flags and clones, shader
interpolators/calculations and `CollectionOcclusion.hlsl` have been removed.
Both backgrounds reuse the first panel capture and keep text/icons clear.
No additional render target, capture or Gaussian filtering stage is introduced.
Quality-dependent UI composition and optional backdrop blur remain in effect.
The arrow centers are inset to +/-96 logical UI units. Navigation uses a native
UI alpha material at queue 3301, after category glass
and before transitions (4000). It preserves the original input feedback
and does not participate in atlas material substitution or request a capture.
A Navigation-only CanvasGroup fades both arrows on the same 250ms transition
clock: 50ms out from current opacity, 150ms hidden, then 50ms in. The cursor
retains OutQuad motion while arrow opacity uses SmoothStep. Interruptions
continue from current cursor and opacity. Disabling cancels the transition and
restores arrows; enabling again normalizes the category layout. Immediate
positioning and instant boundary wrapping do not add a blink.

Validation in Unity 6000.3.17f1: isolated List/shared-Setting business tests 70/70,
category assets 5/5, navigation assets 1/1 and D3D11 category/button lifecycle
4/4 passed. Complete Core compiled for Windows Editor, Android Editor and
Android Player definitions. Business fixtures include expected scene-switch
stub exceptions; passing tests do not imply an exception-free log. The user
confirmed the carousel appearance. Restored arrows have no fresh main-scene
screenshot, target-device or performance validation.

Arrow-fade follow-up: 4 focused behavior tests and 1 CanvasGroup binding asset
test passed, covering the shared time/opacity curves, interruption, disable and
reenable, immediate positioning, wrapping and repeated selection. The earlier
70 cases were not rerun; this follow-up adds no main-Editor visual or device
verification.

`RenderingOptions.UIOutlineMode` is the single complete UI policy. The Custom
menu calls it **UI Style**, exposing all five saved values:

| UI outline mode | Originally cutout quality-aware UI | Originally alpha quality-aware UI |
| --- | --- | --- |
| None | Unoutlined, original cutout | Unoutlined, hard cutoff with blending disabled; procedural rounded panels retain their shape |
| Performance | Unoutlined, original cutout | Outlined where paired artwork exists, existing Alpha Blend |
| All | Outlined, premultiplied Alpha Blend without visual cutoff or A2C | Outlined where paired artwork exists, existing Alpha Blend |
| GaussianBlur | All artwork/compositing, with Gaussian backdrop capture | All artwork/compositing, with Gaussian backdrop capture |
| LiquidGlass | All artwork/compositing, with liquid-glass backdrop capture | All artwork/compositing, with liquid-glass backdrop capture |

The serialized values are None=0, Performance=1, All=2, GaussianBlur=3 and
LiquidGlass=4. **All is plain Alpha and never requests capture.** Values 3/4
derive All for `UiOutlineTheme` and independently derive capture/style from that
same choice; there is no separately writable blur flag.

| Rendering preset | UI outline mode | Scene MSAA |
| --- | --- | --- |
| EnergySaving | None | Off |
| Performance | Performance | Off |
| Beautiful | All | Off |
| Refined | GaussianBlur / LiquidGlass from the saved Rendering Style preference | Off |
| Compatibility mode | Temporarily None | Off |
| Custom | Inherited, independently selectable and saved | Independently configured |

New or empty settings default to Beautiful: full scene resolution, MSAA off and
All UI outlines. An explicit saved `UIOutlineMode=All` remains plain Alpha even
when an old hidden blur key is present. Custom documents missing the mode key
migrate `UIOutline=true` to All, an old outlined-alpha background to Performance,
and other combinations to None. Only that legacy All plus an explicitly true
old `UIBackdropBlur` migrates to GaussianBlur or LiquidGlass according to the old
style preference. Invalid explicit modes become None. `UIBackdropBlur`,
`UIOutline` and `UIBackgroundMode` are read-only migration keys and are not
written back. Missing anti-aliasing remains disabled. Entering Custom
interactively inherits the currently selected preset and Refined style.
Legacy URP Display quality levels and render scales are discarded during migration
and start at Beautiful; existing MRP Rendering categories remain authoritative.

The policy applies to all opted-in `QualityUiImage` components, including List
and Setting alpha panels. It classifies the authored material rather than scene
names or manually set background flags. Ordinary `Image` components, text, video,
and note renderers are not globally converted by this feature. Procedural shaders
outside the explicit supported set retain their existing behavior.

## Authored alpha composition

`QualityUiImage.SourceMaterial` reads `base.material` before any runtime material
conversion. The theme recognizes `UI/Default`, `Maj/UI/Default`, and
`Maj/UI/Atlas Alpha` as ordinary sprite-alpha sources. This includes the List
panels, both Setting RingCircle panels, and SelectedBackground in MenuTitleDisplayer.
The old `_isBackgroundPanel` field and its asset overrides have been removed:
the earlier five-panel List-only policy silently missed the Setting panels and
was not sufficient for a project-wide UI Mode. Original materials, Sprite IDs,
RectTransforms and hierarchy order are retained.

The Performance outline mode uses the paired outlined atlas for these alpha images while retaining their
existing alpha materials. It does not enable smooth materials for ordinary buttons.
The None outline mode uses the base atlas and `Maj/UI/Atlas Cutoff` for plain sprite-alpha sources.
The shader keeps the transparent Canvas queue and disables depth writes, blending
and A2C. Native Stencil, RectMask2D, tint and List depth-offset interfaces remain
available, so the panel cannot gain depth-writing occlusion over foreground icons.

The cutoff tests texture alpha at 0.1: the large plate's authored body alpha is
128/255, so a conventional 0.5 threshold would be unsafe after filtering or
compression. Accepted fragments write solid color. Image/CanvasGroup alpha only
controls hard visibility; zero alpha still discards. EnergySaving therefore drops
partial translucency and continuous panel fades rather than approximating them
with dithering, A2C or an extra render target. The global scene transition remains
on its existing alpha path.

`Maj/UI/Rounded Rect` is classified separately so its unselected Setting title
panels keep their procedural rounded shape. MenuTitleDisplayer's root Image also
uses `QualityUiImage`. None creates a shared clone with `_MajUIOpaque=1` and
`_MajUISrcBlend=One` / `_MajUIDstBlend=Zero`; its SDF contour and sampled alpha are
hard-clipped, and accepted pixels write alpha one. Performance and All use the
original alpha material. The new blend properties have distinct names because
ten existing materials retain obsolete `_SrcBlend=1` / `_DstBlend=0` saved values:
declaring those old names would accidentally change their original alpha state.
Defaults remain SrcAlpha / OneMinusSrcAlpha and `_MajUIOpaque=0`, with no added
shader keyword, pass or render target.

The single serialized `RenderingOptions.UIOutlineMode` retains the complete policy
when entering Custom, including across a restart. Its None / Performance / All
setting appears only in Custom and outside compatibility mode. Compatibility
temporarily selects None and restores the saved mode afterward. The old
`UIOutline` and `UIBackgroundMode` keys are read-only migration aliases: they are
not public settings or written back. An explicitly saved new mode takes precedence
regardless of JSON property order; named presets reapply their own mode.

## Standalone circles, covers and icons

The List's purple cover ring is a tinted solid-circle Sprite behind a smaller
circular cover; the selected difficulty badge is another tinted circle with
independent TMP text. Neither belongs to the paired `newui` atlas, so changing
only the atlas-backed Images had left these edges on the old A2C/cutout path.

The relevant standalone Images now explicitly use `QualityUiImage` as well.
With the smooth style enabled, ordinary cutout circles/icons retain their Sprite
and use the existing premultiplied alpha template. Procedural circular covers and
the favorite badge's white background use `Maj/UI/List/Cover Alpha`, retaining
radius, center, brightness, stencil state and the float List depth offset. The
circle's derivative transition uses approximately one pixel unless an explicitly
larger feather is requested. Both alpha shaders disable A2C and visual cutoff.
No Gaussian preblur, additional texture, render target or copy pass is required.

The None and Performance styles select the original material again, preserving its hard cutoff
when MSAA is off and its existing A2C behavior if MSAA is manually enabled. Custom
style follows the independent `UIOutlineMode` selection, rather than silently changing
when MSAA changes. Other transparent UI and the opaque black CircleMask retain
their material and component. TMP glyph rendering is not changed.

## Refined background capture

Refined requests 100% resolution, MSAA off, full texture precision and UI mode
GaussianBlur or LiquidGlass. Custom can select either captured style explicitly;
plain All does not capture, and compatibility temporarily suspends capture. Actual capture needs
sampleable stored scene attachments and reports `IntermediateBackdrop` unless
recording has higher status priority. Game and other inactive paths retain their
existing direct/native/memoryless gates.

### Historical two-stage path (superseded by the current three cuts)

1. Draw the background PV, opted-in List background cover and explicitly bound
   blue RingLine through queue 2999.
2. Capture and filter that resolved prefix once. Large panels, horizontal
   categories and opted-in secondary plates consume the same first result.
3. Draw ordinary UI, complete category groups at 3300 and navigation at 3301.
4. Capture and filter the completed UI once more for peripheral circular bodies
   at 3500; their symbols and hold progress stay sharp at 3501.
5. Draw transitions at 4000 and debug sensors at 5000, outside both blur sources.

The former category-specific interface, callback, ready flag and render split
are removed. A fully active menu runs two captures and six small filter passes,
down from three and nine. Each capture uses a low-pass downsample and separable
radius-eight/sigma-four Gaussian. All stages reuse the same two single-sample,
depth-free, mip-free RGBA8 textures in command-stream order. Working dimensions
are one quarter of the actual scene attachment, capped proportionally at 1024.
No mip generation or additional render target is introduced.

The first capture is requested by visible large panels or categories; a category
can request it without any large plate. Reuse-only secondary plates cannot
activate capture alone. Peripheral buttons still require their later snapshot
because they overlap completed UI. A stage without a visible consumer skips its
filter. Every camera frame resets readiness; unavailable resources retain the
original alpha artwork. Disable or removal of the last capture owner releases
the pair, and resized output recreates only that pair. The renderer restores
attachments, viewport and camera globals after every callback, including errors.

### List background-cover source correction

Main-Editor Frame Debugger event 55 exposed a real source omission: the 512x683
first Gaussian result contained the default white pattern, blue ring and Live2D,
but not the large illustrated song cover visible in final event 121. List's
`Background/SongCover` was an ordinary Image at transparent queue 3000, drawn
after the capture. It is updated by `CenterCoverDisplayer`, with an authored
0–0.3 fade, and is separate from the background video SpriteRenderer.

That same Image/fileID now uses `QualityUiImage` with `_captureBeforeBackdrop`.
Only while backdrop blur is enabled, it owns a queue-only material clone at 2999.
The original circular-fade shader, source material, dynamic Sprite and animation
are retained. Disabling restores the original material and composition order;
other covers are not moved. This source correction adds no target, capture,
Gaussian pass or mip generation and does not change the tint/blur parameters.

The corrected main Editor was restarted from Init without a hot reload. D3D11
Frame Debugger event 56 now shows the illustrated cover in the first 512x683
Gaussian result. Background UI draws increase from one to two, ordinary UI
decreases by one, and the frame still has 121 draws and two captures. The final
category bodies now show variation from the cover. The temporary read-only
Editor probe was removed.

Focused asset validation passes 1/1 and PlayMode material refresh, source
replacement and ownership pass 2/2. A separate actual-MRP GPU test passes 1/1
with both 1x/4x attachments: its real BlurCircleFade material and 0.3-alpha cover
are absent from the unmarked first prefix and present with the marker. Broad
cover-pattern changes produce category RGB response 0 before the fix and
0.105882 afterwards. An off-center interior probe avoids a symmetric pattern
averaging itself away. The test also retains first1/button1 capture counts and
the same two mip-free work textures, and restores the source queue on disable.
The whole Core compiles under Windows Editor, Android Editor and Android Player
definitions. No Player, mobile-device, Vulkan or power validation was added.
The earlier constant-color and step-edge results below remain limited to their
controlled input and do not independently establish main-scene source ownership.

### Category-local blur and color

Setting and List use the same moving `MenuTitleDisplayer` presentation. Their
selected/unselected bodies, labels, icons and later TMP submeshes retain native
internal ordering at queue 3300. They no longer capture the large plate drawn
behind them. Root-owned foreground bindings skip other panel bodies and do not
scan the hierarchy each frame.

Only a category fragment samples the first blurred texture nine times: a 3x3
binomial kernel with offsets of eight blur texels. The first texture stays
unchanged; stronger blur comes from the category shader, not another Blit or
mipmap chain. UVs and offset geometry stay float; color accumulation uses half.
Global texture dimensions are explicitly published with the filtered result.
Other panels and peripheral buttons keep their previous single backdrop sample.

All glass roles now use the adaptive multiplication described below. Categories
and peripheral buttons use tint strength 1 and frost strength 0, preserving
blurred contrast while applying the actual artwork/control color. There is no
fixed pale-cream ambient term or category-specific RGB palette. The earlier
0.3375/0.525 alpha tint mixing and 66.25%/47.5% background-response measurements
are historical results, not the current color formula. Authored shape,
edge/shadow coverage, Image/CanvasGroup animation and Ready-zero original alpha
remain unchanged.

### Panels, secondary surfaces and overlays

List `Background/RingCircle`, Setting `BG/RingCircle` and SortFind's search plate
retain their explicit RingLine background bindings and original layer order.
Only the large cream plate uses blur; Setting's smaller white Ribbon19 plate
stays ordinary alpha. Resting Image alpha 0.85 matches reference opacity 0.85,
normalizing the roughly half-alpha artwork interior to full RT coverage while
preserving feather, decoration, outlined shadow and relative animation fades.

`UiBackdropPanel` stores only tint/frost scalar strengths, not fill or glass RGB
palettes. The vertex shader samples the actual `_MainTex` slice's interior center
at explicit LOD0; the sampled fill drives both decoration unmatting and the color
filter. The paired original/outlined cream artwork has different center RGB and
alpha, so another atlas's fixed fill must not be substituted. Sprite-less rounded
controls use a white texture and their authored Graphic color. The filter is
`lerp(1, textureFill * GraphicRGB, tintStrength)` and the glass surface is
`(blur * (1 - frostStrength) + frostStrength) * filter`. The interpolation forms a
multiplier; it does not replace blurred scene color with a uniform dyed fill.
Changing an artwork/control color therefore changes its glass without a palette
code edit. The material variant cache tracks both scalar strengths.

| Surface | Tint strength | Neutral frost strength |
| --- | --- | --- |
| List / Setting / SortFind large cream plates | 0.4 | 0.08 |
| Submonitor user/chart white plates | 1 | 0.02 |
| Setting / List categories and eight peripheral buttons | 1 | 0 |

`UI/UserInfo.prefab` and `UI/ChartVisualDisplayer.prefab` now retain 98% blur plus
2% neutral white frost before multiplication, preserving sliced borders and sharp
avatar/name/chart foreground. They never request another capture or activate
blur alone in Game/Result. Chart loading/help/error placeholders remain
transparent; their owned 1x1 texture is destroyed with the component, while
borrowed cached graph textures remain owned by the cache.

The adaptive fill adds one uniform texture fetch and reciprocal math per vertex,
plus interpolated values. It does not add a fragment texture fetch, RT, Blit,
blur pass or capture. This is additional work, not a zero-cost color change.
The existing first/button capture order and full body coverage remain intact.

The eight peripheral `Button1..8/BG` Images retain the second capture; Setting's
hidden Button1/8 and separate plus/minus controls keep their existing behavior.
UiTransitionOverlay changes only owned native material queue variants and
preserves shader, stencil, hierarchy and animation. Later TMP submeshes follow
the same queue. Sensor mesh, input mapping, transparency and disable behavior
are unchanged, with queue 5000/ZTest Always above transitions.

### Verification boundary

Current adaptive-color validation on Unity 6000.3.17f1: D3D11 focused EditMode
36/36 (34 asset cases plus 1x/4x GPU cases) and all 15/15
UiPvBackdropLifecycle cases pass, with no skips. Independent Vulkan Shader GPU
checks pass 2/2. The external actual-MRP D3D11/native-UGUI atlas test passes 1/1,
covering eight combinations: selected sprite6 Sliced and sprite20/21/22 Simple,
each with actual 1x/4x scene attachments. Actual CanvasRenderer output matches
independently sampled artwork fill multiplied by the blurred background.

The actual-MRP Vulkan external run is not verified: before its assertions and
readback validation it reports `Missing Vulkan framebuffer attachment image?`,
then reaches a native `GfxDeviceVK::ReadbackImage` stack without producing XML.
The fixture/backend limitation remains unresolved. The independent Shader test
is not proof of Android/Vulkan render-path integration. Full main-menu visuals,
mobile-device appearance and power were not measured; temporary main-project
probe scripts are absent.

At tint 1 / frost 0 the filter physically attenuates background RGB by the source
fill. Dark button20 therefore retains roughly 31–39% per-channel brightness;
this darkness is not a body-alpha or capture bug. Changing the strength to tune
appearance is separate from verifying the color model.

The measurements below describe earlier color implementations and establish
only their stated historical boundaries.

Before the List source correction, Unity 6000.3.17f1 focused EditMode 6/6,
PlayMode 4/4 and actual MRP D3D11 GPU
2/2 passed, covering 1x/4x attachments, first/button capture counts 1/1 with no
category callback, one working pair reused across stages/frames, category-only
activation, scale/resize, fallback, alpha coverage and sharp foreground.
A rendered step edge's 10–90% transition widened from 39px for the shared blur
to 72px for category sampling, while the first filtered texture stayed identical.
Test-owned readback targets/copies are observation probes, not production RTs.

A follow-up dual-background response test uses the actual selected sprite6 /
Default material with tint 0.7, the unselected procedural rounded material with
tint 0.45, and resting Image alpha 1. At both 1x/4x, the measured center RGB
response is 0.662745 for unselected and approximately 0.475 for selected tabs.
Ready-zero response is zero and matches original Alpha fallback. These values
prove background response only: a uniform black/white input cannot reveal that
the real List cover was excluded from the capture. This corrects
the earlier fixture's uniform 0.45 tint and partial-alpha blind spot; the full
MenuTitle hierarchy is not instantiated by this controlled rendering fixture.

The placeholder ownership regression passed 1/1; shared-menu/category tests
passed 19/19 after removing an unused field and skipping unchanged selection
appearance writes. The runtime MRP and whole Core compile under Windows Editor,
Android Editor and Android Player definitions using the pinned Unity Roslyn.
These are not Player builds or a rerun of the source correction. The later
main-Editor Frame Debugger observation found the omission described above;
Vulkan, Android/iOS visual, GPU-time, bandwidth and power remain unverified for
the correction.

## Resources and bindings

- `Assets/Sprites/Ribbon/newui@3x.png` contains the unoutlined artwork. Its existing
  texture GUID and all Sprite identifiers are retained.
- `Assets/Sprites/Ribbon/newui-outlined@3x.png` contains the outlined
  artwork. Both atlases use the same 34 slice names, rects, borders, pivots and PPU.
  Twenty-five rectangles were expanded to include measured shadow extents; nine
  already included their shadows and keep their authored rectangles. Both use
  FullRect so the old hard-edge Tight outline cannot trim soft artwork.
- `Assets/Resources/Rendering/UI/OutlineTheme.asset` lazily references those
  sprites and the Alpha, CoverAlpha, AtlasCutoff, Gaussian and glass material templates; artwork
  stays in Ribbon and the registry makes their build dependencies explicit.
- Both PNG files are byte copies of the supplied artwork; no image processing or
  pixel changes are performed. Only the necessary import rectangles/borders and
  mesh type change together.
- `Assets/Resources/Rendering/UI/AtlasLayout.asset` retains 34 pre-expansion
  native Sprite subassets and captured normalized outer UVs. They share the
  existing unoutlined texture; there is no second copy of its PNG or GPU texture.
  This is runtime layout data, not a disposable Unity import cache.
- `QualityUiImage` derives from UGUI Image. It changes the effective `mainTexture`
  and material while preserving serialized Sprite references, including Button
  SpriteSwap overrides, Image color, logical size, and nine-slice layout. Existing
  authored Images using this atlas have been migrated to this component.
- New controls using this atlas should also use `QualityUiImage`. The asset tests
  check that ordinary Images do not bypass the feature.

The two textures must remain identically laid out and unpacked. The runtime checks
rect/pivot/border/PPU and imported texture dimensions before reusing the original
UVs. Mismatched resources fall back to the original artwork and log once.

### Content size and shadow margins

`UiAtlasMeshGenerator` uses one disabled hidden Image and an empty Canvas only as
a native UGUI mesh generator. It never enables a second UI renderer, adds a
camera, or allocates a render target. The old Sprite geometry feeds Unity's own
Simple/Sliced/Tiled/Filled implementation rather than copying its tiling and
radial-fill algorithms into the game. The helper is reused across mesh rebuilds
and recovered across Editor script reloads.

Unoutlined UI retains the original native mesh. Outlined UI extrapolates only
the quad's old outer UV/position mapping to include the complete new slice.
Inner nine-slice positions, border thickness and tile periods remain unchanged.
When native border compression intentionally omits a texture region, that crop
is retained rather than adding previously invisible body content with its shadow.
Filled meshes retain their original linear/radial clipping planes while allowing
the outer slice boundary to expand. `preferredWidth`, `preferredHeight`, and
`SetNativeSize` use the original logical Sprite dimensions. Serialized
RectTransforms, raycast rectangles and Button Sprite Swap references stay intact.

The original Tight crop was itself significant: the +/- sprites imported with a
163x105 logical rectangle but sampled only 140x82. Their authored rectangles did
not need expansion; switching to FullRect and retaining the old texel-to-UI
mapping recovers the clipped shadow without enlarging the button body.

Setting's six vector arrows are authored after their background siblings. The
old Opaque queue had hidden the reversed hierarchy; alpha composition requires
backgrounds before their foreground icons. Their positions and depth are retained.

The current List's ring has eight buttons in one shared Buttons prefab instance,
with nine added foreground Images (one button has two). Those added-object
overrides append the symbols after each inherited BG, rather than inserting
them before it. The old opaque symbols wrote depth and concealed that reversed
Canvas order; once both layers blend, a later background covers the symbol.
Only the nine List-local insertion indices change. Shared button artwork,
positions, rotations, native sizing, materials and component IDs are preserved.

### Main and sub display boundaries

The List's upper circular buttons intentionally extend beyond the main-display
square. The old Opaque/Transparent pass order hid their overflow behind the sub
display; after alpha conversion both groups can render in the transparent pass.
The square CircleMask does not cover the region beyond its own rectangle and
is retained unchanged.

An enabled QualityUiImage under a real `Main_Display` with a sibling `Sub_Display`
installs one runtime `MainDisplayUiClip` and native RectMask2D. The main rectangle
is the outer boundary, and its upper clipping edge is clamped to the actual sub
display's lower edge after ScreenPos layout runs. World corners are transformed
back into main-local space, so offsets, scale and mirrored sub-display transforms
are handled without assuming a screen resolution. Existing mask padding is kept.
Controls under `Sub_Display`, the global transition overlay, and non-UI video or
Live2D renderers are not assigned this runtime controller. Neither black masks,
global Canvas sorting, render queues, nor display transforms are modified.

This native UI clipping adds no render target, copy or mask draw pass. It uses
the alpha shader's existing RectMask variant; the original Opaque low-quality
rendering path is retained.

`GameManager.ApplyRenderingSettings` publishes the single effective UIOutlineMode
through `UiOutlineTheme.Configure(UiOutlineMode)`. The theme derives ordinary UI
and marked background behavior from that mode; it does not receive a preset name
or maintain a separately configured background policy.
Enabled Images register for style changes; pooled controls apply the current style
when re-enabled. There is no per-frame hierarchy scan or Sprite replacement.
The outlined resources load on first use and remain cached for subsequent toggles;
turning the option off restores rendering but does not promise immediate texture
memory reclamation.

## Alpha rendering

`Maj/UI/Atlas Alpha` retains UGUI Stencil/RectMask contracts and the List material's
`_ListDepthOffset`. It uses Transparent queue, ZWrite Off, premultiplied Alpha
Blend, and AlphaToMask Off. Ordinary UI keeps the full fractional alpha. The
optional 0.001 discard is reserved for Unity's structural stencil Mask operation,
not a visual cutout threshold.

Material variants are shared by original material and never modify those assets.
Only `_MainTex` with its scale/offset, `_Color`, and `_ListDepthOffset` are copied
from the original Opaque material. Circular List variants additionally copy their
shape, brightness and authored stencil/color-write properties. The Opaque-to-alpha
template keeps its own color-write,
stencil, keyword, and queue state. Do not use `CopyPropertiesFromMaterial` across
these different shaders: real legacy Opaque assets can replace the template's
property sheet, making `_ColorMask` and stencil defaults zero and hiding the UI.
The shader is referenced through the Resources registry and alpha template, so
build inclusion does not rely on Shader.Find. New Android graphics-state warmup coverage requires a future
device trace of this alpha UI path; this change does not fabricate PSO assets.

## Historical validation records

See [UIOutlineValidation](../../../../../Tools/Tests/Modules/UIOutlineValidation/README.md)
and the RenderingOptions tests in the main Unity test suite.

Verified with Unity 6000.3.17f1:

- The initial targeted settings/defaults/serialization and asset suite passed
  37/37 in an isolated copy of the main project. The subsequent resource-path
  and material-conversion fixes are covered by the focused suites below.
- Final isolated EditMode checks: 61/61 passed (4 atlas/resource checks, 12
  standalone UI binding cases, 22 geometry cases, 16 display-clipping cases,
  and 7 alpha-panel/Setting binding cases).
  They compare all 34 logical Sprites with native UGUI, including
  narrow Sliced borders, native cropping, Tiled and radial Filled geometry, and
  preferred/native size. Editor PlayMode lifecycle/material checks: 13/13 passed,
  including the actual legacy Opaque, RingLine, and List material assets,
  non-atlas artwork, sprite-less/Filled circles, unchanged ordinary Images,
  background material/texture switching through a native CanvasRenderer, actual
  Setting source bindings and the original rounded-panel material.
- The Setting omission was reproduced with the previous marked-panel source:
  its unmarked RingCircle retained Alpha Blend in None and the base atlas in
  Performance. New-source D3D11 and Vulkan GPU checks each passed 15 actual-asset
  configurations (RingCircle 22, SelectedBackground 6, Opaque controls 16/20 and
  the real rounded-panel material, across three UI modes). None's RingCircle
  body difference under two back-layer colors changed from 0.35294 to zero;
  Performance restored outlined margins while original Opaque controls retained
  their original path. Rounded-panel clones remain solid and keep their corners;
  original alpha materials keep new blend defaults 5/10 despite old saved 1/0.
  These use production UI components and actual authored configurations in a
  narrow Canvas/SRP fixture, not full Setting initialization or mobile Present.
- The unified UI-mode revision retained 57/57 EditMode and 11/11 PlayMode
  lifecycle checks, including Performance / All / Performance transitions through
  the single theme API. The existing standalone settings harness passed 1490
  assertions, including enum cycling, exactly one Apply per accepted input,
  passive refresh without Apply, independent MSAA and compatibility restoration.
  Its narrow substitutes do not establish main-project or device integration.
- A separate linked-production model check using actual Newtonsoft.Json passed
  187 scenarios / 787 assertions: legacy booleans/background values, new-key
  precedence in either property order, only-new-key serialization, preset
  authority, Custom inheritance and unchanged MSAA/scale/FPS values. This is a
  serialization/model check rather than a Unity lifecycle or rendering test.
- List background settings passed 44 scenarios / 240 assertions; the legacy URP
  migration retained its 80 scenarios / 1950 assertions. A focused native UGUI GPU
  fixture passed 6 tests / 24 configurations across D3D11 and Vulkan: the actual
  half-alpha plate survives cutoff, blending is disabled only in cutoff mode,
  Performance selects outlined alpha independently of ordinary UI, and masking,
  sub-display clipping, real +/- icons and pooled state restoration remain valid.
  This fixture uses a narrow SRP rather than the complete List or MRP backbuffer;
  it does not measure mobile power, bandwidth or performance.
- Production automatic display clipping passed Native Canvas GPU checks on
  D3D11 and Vulkan for adjacent displays and a main display shifted up by 200
  units. Sub-display leakage and changes inside the visible main region were
  both zero, with original transforms and CircleMask references retained. The
  Vulkan overlap case also passed with a real 4x target and A2C available.
  A further overlap check included the actual List +/- icon Sprites, transforms
  and original Opaque material: no glyph remained over the sub display, so the
  shared Opaque/note shader did not need modification.
- Separate D3D11 shader GPU checks: 32 passed, covering continuous alpha on
  single-sample and 4x targets, RectMask/Stencil, and List depth offset.
- A real-asset GPU reproduction on both D3D11 and Vulkan confirmed that the old
  material-copy path writes no color; the allowlisted copy preserves the alpha
  template's render state and writes the expected visible color.
- `AtlasAlphaMaterialTests` calls the production material conversion with the
  actual legacy Opaque and List Cover sources. D3D11 and Vulkan each passed
  four 1x/4x cases (eight total), including RGBA readback at 0.25/0.75 alpha.
- The new circular alpha shader separately passed 47 GPU checks on each of
  D3D11 and Vulkan: tint/texture alpha, brightness and instanced brightness,
  texture ST, RectMask with the depth offset, stencil comparison and structural
  masking. The isolated purple circle had 39 intermediate coverage values and
  approximately a one-pixel radial edge band. Its 1x and 4x outputs matched;
  this does not claim identical output for every shape, transform or device.
- A Native Canvas composite with production components/materials and the real
  circle sprites, Jua TMP text, heart texture and opaque black mask passed on
  both APIs at 1x/4x with main-display offsets 0/200. Cover/ring/badge layering
  and black occlusion were correct, sub-display leakage was zero, and overlapping
  pooled covers retained `_ListDepthOffset=151` while switching the front sibling.
  Covers used a solid test texture; this is not a complete List-scene run.
- A subsequent Vulkan regression loaded the actual nested List Buttons hierarchy
  before and after its insertion-order fix. At 1x/4x, enabling all eight visible
  symbols changed zero pixels in the broken smooth path; the corrected hierarchy
  restored all eight (3764/3770 changed pixels in that fixture). Cutout-path
  output matched before/after, and an offset-200 case retained zero sub-display
  leakage. The ninth authored foreground is hold progress and remains initialized
  to zero fill. This targeted reproduction does not replace full-game validation.
- Both atlases were reimported as FullRect in Unity: all 68 Sprite texture
  rectangles match their authored rectangles and all report zero trim padding.
  PNG bytes and original GUIDs/Sprite IDs remain unchanged. Only the measured
  rectangle expansions, corresponding borders and mesh type changed together.
- Setting's actual six vector-arrow sibling orders passed the real-scene import
  regression. Native Canvas GPU layering checks passed on D3D11/Vulkan at
  1x/4x, without changing icon positions or depth tests.
- Actual production QualityUiImage/Canvas GPU comparisons on D3D11 and Vulkan
  covered +/- buttons, a nine-sliced button, and both 270-degree ring backgrounds.
  Unoutlined rendering matched old native UGUI pixels exactly; the opaque cores
  retained their positions and the soft buttons recovered the missing shadow.
- The `_81` nine-slice also passed a Vulkan Canvas GPU comparison for compressed
  and uncompressed borders: old native cropping is retained, no extra opaque
  body is introduced, and the newly visible pixels are shadow. Its soft source
  changes some internal colors, so geometry comparison uses that same soft source.

Subsequent real D3D11 Editor checks covered Setting at five Game View sizes
(portrait, long portrait, landscape and square), with direct 1x, requested 4x and
75% intermediate output: 15 stable screenshots and 45 transition checkpoints.
Three Setting/List round trips added 48 captured frames; no stale menu UI or
sub-display overdraw was observed in those samples. The existing layout can take
one frame to refresh clipping and the sub-display input boundary after an aspect
change; the third frame was stable. Same-size MSAA/output changes did not show it.

Player builds and Android/iOS visual, power and driver behavior remain unverified
for this UI change. Isolated GPU readbacks and desktop Editor checks do not establish
mobile performance or exhaustive scene, skin and chart coverage.
