# Android graphics state capture

## Settlement surfaces and offline contour maintenance

Result's main plates use Board / RT1, its FastLate and NoteDiffGraph plates use
Category / RT2, and TotalResult's compound board uses Board / RT1. These are
explicit component opt-ins, including independent non-Ribbon Sprites; no new
capture stage or runtime work target is created. Game still suspends capture
while retaining the pool, and Result resumes through ordinary registration.

Result's two independent plates use fitted clipped circles. TotalResult opts
into artwork-alpha contour baking: the editor generates its compound Euclidean
distance field once, rather than fitting an incorrect single circle. The same
RGBA8 atlas lookup and decoding serve both sources at runtime. Tiles retain
source units with a 1020-pixel raster cap and two guard texels per side; the
additional result contours increase distance storage to 32 MiB. Rebuild the
atlas after editing opted-in source artwork or its contour parameters. Runtime
does not read artwork back or construct distance fields.

Unsupported system-scaling controls are read-only without normalizing saved
values. Foreground variants now respect LiquidOnly eligibility, and existing
TMP fallback descendants refresh with their owner when style changes. Old
lookup performance probes and historical runner instructions were removed;
functional regression coverage remains. This revision was reviewed statically
and its resource was baked in isolation; no game, regression or performance
test was run.

## Current UI policy and static audit

Refined exposes **Rendering Style** (formerly Panel Style): Gaussian Blur or
Liquid Glass. Custom uses one saved `UIOutlineMode` to select the complete UI
policy; MSAA and scene resolution remain independent.

| Value | UI style | Base theme | Capture |
| --- | --- | --- | --- |
| 0 / None | Opaque | None | Off |
| 1 / Performance | Mixed Transparency | Performance | Off |
| 2 / All | Translucent | All, ordinary Alpha Blend | Off |
| 3 / GaussianBlur | Gaussian Blur | All | Three Gaussian cuts |
| 4 / LiquidGlass | Liquid Glass | All | Three glass cuts |

EnergySaving/Performance/Beautiful select 0/1/2. Refined maps its retained
`UIBackdropStyle` preference to 3/4; entering Custom inherits the current
configuration. An explicit saved All stays plain Alpha even if an old hidden
blur flag is present. Only legacy documents without the mode key migrate their
boolean/background fields and explicit old blur choice. `UIBackdropBlur` is now
read-only migration input, never written. Compatibility temporarily uses None.

This revision was reviewed statically only at the user's request: no tests,
compilation, Unity interaction or ADB. Cleanup removes unused shader work,
publishing/configuration and old Android investigation code; it reuses clipping
transforms, Player sorting-layer names and theme material-release handling.
That cleanup preserved optics, color, precision and RT topology. The subsequent
user-requested quarter Gaussian changes the blur grid/kernel, with no added
targets; it is also untested. The user's previously accepted Pad 5 Pro measurements were about
100 FPS Gaussian / 103 FPS Liquid in Setting; Xiaomi 15 was about 121 FPS for
both, with 1% lows around 100 / 92-100 respectively. The user reported normal
rendering. These precede both the cleanup and quarter-grid change, not evidence of
absolute optimality. Dated validation below describes its original revision.

Android gameplay loading uses `GraphicsStateCollection` assets from
`Assets/Resources/GraphicsStates/Android/`. A usable collection must match the
Android runtime, graphics API and Unity quality level name. Capture actual
rendering on the target API before expecting this warmup to cover gameplay.

## Included capture

`Assets/Resources/GraphicsStates/Android/Android-Vulkan-Default.graphicsstate`
contains **25 shader variants / 160 graphics states** for Android/Vulkan and
Unity quality `Default`. It was captured on 2026-10-24 with Unity 6000.3.17f1 on
a Mi MIX 2S (Adreno 630), using the real MRP and the requested full **月下繚乱
~2022 Re:Master~ Master 14** chart and Result.

One process completed all eight requested **1x/2x/4x/8x samples ×
Direct/Intermediate** combinations. The downscaled intermediate group used 50%
and compatibility mode; the supersampled 4x intermediate group used 150%.
Both 8x requests fell back to 4x on this device. Unity's imported-state audit
found **56 states at 1x, 54 at 2x and 50 at 4x**, with **no true 8x states**.
The native union audit confirmed the matrix already contains every variant and
state from the earlier **24 / 38** Performance capture; no separate union file
was needed. The asset retains its original Unity GUID.

The final packaged **25 / 160** matrix completed the production asynchronous
warmup on the device at **02:07:03 Asia/Shanghai** on 2026-10-24, taking
**44.4 ms**. This reused the device's existing driver cache and is **not a
cold-cache performance benchmark**. The Single replay subsequently completed
the full chart and Result, then exited normally. Its fresh, narrower **25 / 53**
trace is validation evidence; the included asset remains the full **25 / 160**
matrix. See the validation results for the lifecycle evidence,
actual output paths, asset audit and isolated-build limitations. This captures
the exercised chart, scene UI and effects, and preserves prior video-shader
states through baseline containment; it does not establish exhaustive chart,
skin or other-API coverage.

The game's EnergySaving, Performance, Beautiful and Refined presets all retain
Unity's `Default` quality level. Their states can accumulate in the same
collection during one capture session. A different Unity quality level is stored
separately by the collector and selected by name by the warmup loader.

## MRP settings to cover

The following capture plan follows `RenderingOptions.ApplyPreset` and the actual
MRP attachment/output paths. Coverage assumes the same rendered content, camera
configuration and device-supported attachment formats.

| Setting | Requested rendering configuration | Capture requirement |
| --- | --- | --- |
| Performance | 1x, 100%, direct output | Baseline; included capture covers the exercised content. |
| EnergySaving | Same as Performance, texture LOD bias +1 | Shares Performance states; the bias is a shader uniform, not another shader variant. |
| Beautiful | Default MSAA off (1x), 100%, direct output requested | Default state shares the single-sample scene path. Custom MSAA settings still require their own actual render-state coverage. |
| Refined | Default MSAA off (1x), 100%, three ordered sources with six persistent targets | RT1 includes the drawn PV and blue ring for Board surfaces and submonitor cards. RT2 follows boards and the background song cover for Category surfaces; RT3 follows ordinary foreground UI for Peripheral surfaces. Gaussian applies quarter-size fixed filtering at every cut; Liquid uses the same quarter RT1 and separate bilinear half-size copies without Gaussian for RT2/RT3. Custom MSAA remains independent and applies to capture cameras too. Revised filter passes, packed optics and sensor depth state require fresh graphics-state capture; the historical matrix does not establish their coverage. |
| Compatibility mode | 1x, 50%, intermediate output, texture LOD bias +2 | Capture the single-sample intermediate path; Refined alone does not establish this coverage. |

## Current menu resources and ordering

Named presets default to one scene sample. Custom MSAA applies to the menu
camera as well; no hidden one-sample override is imposed. The six work textures
remain single-sample, so multisampled scene prefixes still need resolves before
capture. Captures, normal presentation and their GPU costs remain. Earlier
forced-1x and Vulkan StoreAndResolve investigations are historical.

The three ordered sources remain independent:

| Source | Captured prefix and consumers | Gaussian style | Liquid style |
| --- | --- | --- | --- |
| RT1 | PV and bound blue ring, before Live2D, boards and the background song cover; Boards, submonitor cards and startup plates | Quarter-size fixed Gaussian | Same quarter-size fixed Gaussian, then existing glass optics |
| RT2 | After boards and the background song cover, before categories; Setting captures without a cover, and its dark value plate uses this result | Quarter-size fixed Gaussian | Half-size bilinear copy without Gaussian, Bilinear/Clamp |
| RT3 | After categories and ordinary foreground UI, before peripheral bodies, Setting adjustment capsules and Liquid List cover overlays | Quarter-size fixed Gaussian | Independent half-size bilinear copy without Gaussian, Bilinear/Clamp |

Only the two central Setting adjustment capsules now set the existing
`_liquidRefractionWidthScale` to 0.75. At the current matched reference scale,
their lens band changes from 47.3985 to 35.5489, between the unselected/selected
top categories' 34.56/37.035; the model's peak displacement is about 14.113.
This changes only these two controls' lens width and corresponding thickness,
not fine-highlight width, colors, interaction, other roles or the Gaussian path.
No shader field, contour/profile rebake, capture or texture read is added.
These are geometry/model values, not a new appearance or performance result.

The outside-circle Opaque mask restores its original queue 2000. Other ranges
remain background/blue ring through 2899, Boards 2900-2949, background cover
2950-2989, Categories 2990-2999, ordinary UI 3000-3499, Peripheral bodies and Liquid
List cover overlays 3500,
clear symbols and Liquid List cover images from 3501, late labels at 3502, transition 4000 and
sensors 5000. DisplayMaskAlpha
again inherits the original Default shader's Transparent queue, effectively
3000, while retaining Alpha blending and native Canvas ordering. RT1 remains a
drawn scene-prefix capture, not a decoder-only sample, so the blue ring survives.
Color, artwork proportions, coverage, decoration, shadows and highlights remain.

Init keeps the persistent global mask Image and its Sprite contour mesh. List
and Setting also keep their local CircleMask Images enabled, retaining both
global and scene-local layers and their overlapping drawing as requested. All
objects and resource references remain; each scene still has its own mask for
standalone previews. The preceding choice to disable local Images and depend on
the global mask was withdrawn. Other scenes' Alpha masks are retained. Sensors
still use the last queue and Always depth testing.

RT2 and RT3 carry main-display circle coverage in their existing Alpha channels.
`UiPvBackdropBlur` projects a consumer's cached `Main_Display` Rect through the
current camera. Category consumers cache that Rect even in a scene without
peripheral keys. RT2 retains its own `_MajUiCategoryClipUvX/Y` and
`_MajUiCategoryClipSampleBounds`; RT3 independently retains `_MajUiButtonClipUvX/Y`
and `_MajUiButtonClipSampleBounds`. Each safe radius uses its own result's actual
dimensions, and the later capture does not overwrite the earlier result's mapping.

The first copy/downsample selects `_MajUiCaptureClipUvX/Y` for the current cut,
writes coverage to Alpha and multiplies RGB by it; Gaussian H/V filter that
premultiplied RGBA together. Board/RT1 disables this capture encoding and retains
its prior behavior. Scene output and both mask layers are unchanged.

All RT2 consumers, including native controls such as Setting's dark value plate,
use the Category slot; sharing that source does not require the `IsCategory`
visual-role flag. RT3 safe sampling applies to actual peripheral keys and the
explicit Liquid List cover overlays;
Setting adjustment capsules share RT3 but are not `IsButton` keys and retain
their previous sampling. The affected consumers radially limit refracted lookup
positions outside their source's CPU-precomputed safe circle before the existing
single RGBA read. Bounds include the projected center and a conservative
two-texel inset. Dividing RGB by filtered Alpha reconstructs valid scene color;
Alpha is a filtering weight, not another material opacity or color switch.
There is no three-pixel artwork-color transition or exterior surface replacement.
The full existing palette, ArtworkBlend/tint mixture, ink and glass highlights
remain. This uses geometry, not depth or an RGB-black test: genuine black scene
content remains black.
`MainDisplayUiClip` still isolates main-display UI from the submonitor.

Circle bounds are calculated on the CPU for the current camera/capture size.
Per-fragment work is the affine mapping and radius check, with a reciprocal
square root only for out-of-range positions. No new precomputed offset texture
is introduced; existing baked contour and refraction-profile data are unchanged.

No texture read, RT or Blit is added: the existing sample already reads RGBA,
and the six-target pool and three independent cuts remain. Early depth rejection
remains available for later depth-tested outside-circle fragments. This RT2
extension passed three focused isolated D3D11 PlayMode GPU cases on Unity
6000.3.17f1 / RTX 3080 Ti: active capture coverage encoding, independent category
bounds with selected/unselected/native consumers and other-role isolation, and
peripheral safe sampling. Both styles are covered, including a later RT3 mapping
change that leaves RT2 readback unchanged. Main-scene visual and mobile-device
validation of this extension remain pending. The preceding RT3-only simplified revision was
inspected in the real List and Setting scenes in Unity 6000.3.17f1
on Editor Vulkan: Liquid in the tablet view at the existing 1.28 main-display
scale / 0.22 offset, and Gaussian with default layout in tablet and tall-phone views.
No exterior black bleed or added fill band was observed in the inspected views.
Two focused isolated D3D11 PlayMode GPU cases passed: capture coverage encoding,
and peripheral safe sampling with native-control isolation. Those historical
checks do not validate the RT2 extension and do not
establish mobile-device behavior, all scene/configuration coverage or FPS/power
improvement; preview zoom and local configuration were restored after inspection.
The late-4500 mask, whole-capture padding, prior color-replacement and
local-Image suppression results are historical, as is the interim direct-black
sampling revision. They do not validate this implementation, and no duplicate-
draw reduction is claimed. Early drawing and both global/local mask layers remain.

Startup precreates six RGBA8, color-only, single-sample, mip-free targets: two
ceil-half raw liquid snapshots and four ceil-quarter textures for three Gaussian
results plus shared scratch. Style/scene changes and Game retain the pool;
source-size changes rebuild it, quit/reset releases it. At dimensions divisible
by four the pool is 0.75N pixels, half the prior all-half pool's 1.5N:
11.71875 MiB at 1600x2560 or 5.932617 MiB at 1080x1920. Exact storage is
`(2*ceil(W/2)*ceil(H/2)+4*ceil(W/4)*ceil(H/4))*4` bytes. Scene/MSAA attachments,
video RTs and optical/artwork atlases are unchanged; total VRAM/bandwidth does
not follow this pool's reduction automatically.

`s_sourceSize`, `s_halfResolutionSize` and `s_gaussianResolutionSize` separate
source, raw and Gaussian dimensions. `GetSourceSize`, `GetHalfResolutionSize`
and `GetGaussianResolutionSize` compute them without a long-edge cap. The six
role-named textures and three independent cuts remain; actual texel sizes are
published. `Configure(enabled, style = GaussianBlur)` remains the single entry.

Fixed sigma 4 / radius 8 gives 17 logical taps and nine paired bilinear reads per
axis. Downsample plus H/V totals 4+9+9=22 sampling calls over equal quarter output
areas across three passes, not 22 reads in one pixel invocation or a bandwidth
count. GL Point-source reconstruction can expand the downsample calls; H/V use
owned Bilinear inputs. Pure-math `BuildPanelBlurKernel()` precomputes four shared
axis pairs and center weight once during type initialization. Resize/reset does
not rebuild or clear that CPU kernel; release clears GPU bindings. The shader
uses `_MajUiGaussianCenterWeight` and `_MajUiGaussianPairs[4]`, with four pairs
unrolled. Old half-grid sigma 8 / radius 16, 17 reads/axis and the size-dependent
kernel/1024 reference cap are historical. Quarter sigma 4 suggests roughly the
same 16 source-pixel continuous sigma as half sigma 8, but downsample/reconstruction
can alter detail and PSF. This requested revision is untested; the user will
review its appearance. Neither image equivalence nor measured FPS gain is claimed.
Liquid RT2/RT3 each perform one bilinear prefix Blit into an independent
ceil-half target and expose Bilinear/Clamp snapshots to the existing glass shader.
A borrowed Point-filtered source is
sampled linearly by the copy pass without modifying it. Small Gaussian, guard,
ROI and their scratch/kernel resources are removed, including the obsolete UV
mapping. Gaussian-style RT2/RT3 and both styles' RT1 retain strong filtering.
The complete Gaussian path has nine background-filter draws; Liquid has three
strong RT1 draws plus two raw copies. These counts exclude scene/UI drawing and
final presentation and do not imply measured device FPS or power savings.
The half-size snapshots can lose high-frequency detail relative to the preceding
full-size copies; image equivalence, performance and power require fresh checks.
Production enables no Mipmap blur. Isolated direct-LOD, mip-reconstruction and
few-tap candidates altered thin blue rings, edge transitions or PSFs. The close
mip1-plus-complete-Gaussian result retained the full kernel and added snapshot
and mip generation, so it was not selected either. Mip generation/downsampling,
storage and synchronization have real costs; mip1 is half width/height and mip2
is quarter, not a different-resolution-free blur.

Liquid refraction uses `Rendering/UI/GlassOpticsAtlas` encoding v7: a single
4096x2048 RGBA8 UNORM atlas, 32 MiB after settlement coverage, with Bilinear/Clamp sampling, linear
color data, no compression and no Mipmap. RGB hold three monotone coarse
channels; A holds the fine residual. Each tile has a positive range and signed
minimum. One native sample is decoded in float as
`normalized = dot(rgb, 1/3) + a/765`, then `distance = normalized*range + minimum`.
The distance discretization is about 17.6 bits, rather than 32-bit float
precision. Filtered channels are decoded linearly without rounding or fract.

The current shader uses one native bilinear sample and float decoding, with no
Gather, texel-cell floor, four Point reads or manual bilinear reconstruction.
Its baseline is target 3.0, using a full-float data sampler on GL/GLES and a
typed float texture elsewhere. RGBA8 Sample/Linear support and shader level are
checked directly; there is no OS/backend whitelist. The independent 256x1
RGHalf optical profile still requires Sample/Linear support, so broader distance
format support does not guarantee every device can render the complete effect.
Missing/stale/unmatched Sprite contour data or invalid decode parameters disable
Sprite refraction. Both optical paths retain the existing format/filter/profile
gates. No four-Point or runtime-Snell fallback is used. Coverage-only handling preserves authored color/Alpha,
symbols and shadows. Sprite UVs, Sliced mapping, baked tile guards, curve-before-
slab crop composition and color/thickness parameters remain. The base profile
lookup is still skipped beyond its original band; the peripheral-only polynomial
continuation described below can extend displacement through that region.
These data reads ignore artwork LOD bias and add no capture/filter RT.
Native bilinear still filters neighboring texels; fewer sampling instructions
do not imply the same proportional reduction in memory traffic.

The unselected List/Setting capsules use the original `Maj/UI/Rounded Rect`
shader without a Sprite. `_LiquidNativeContourReady` is enabled only for this
exact source shader, a positive Rect, `Image.Type.Simple`, no Sprite mesh, no
peripheral-key role and no ellipse/clipped-circle shape, after the same hardware
and profile gates pass. It reuses the fragment's existing Alpha-coverage distance,
screen gradient and units-per-pixel value, then samples the same precomputed
optical response profile. It adds no second SDF evaluation, contour texture read,
RT or capture. Existing `roundedCoverage` clips once, with extra body coverage
kept at one; original Alpha/colors and animated dimensions/corner radii remain.
This exact native-contour path is not a generic analytic fallback: missing Sprite
maps, unknown procedural shapes and unsupported formats/profiles still disable
refraction. GPU, visual and device validation of this fix is recorded separately.

### Peripheral center falloff, 2026-11-12

Only `UiBackdropPanel.IsButton` sets `_LiquidRefractionTail` to 0.22. Sharing the
Peripheral/RT3 source alone does not enable it: native category controls, Setting
plus/minus controls, large boards and submonitor cards keep their established
displacement. The outer keys retain their narrow base width and unchanged RGHalf
profile, but add a low-frequency tail instead of becoming wholly flat immediately
after that band. The original response still uses logical inward depth
`dLogical` and band `W`; the ending envelope independently uses the sampled,
post-crop source depth `dSource` and authored Sprite body radius `RbodySource`:

```text
t = saturate(dLogical/W)
dSource = max(-sourceDistance, 0)
RbodySource = min(_LiquidGlassSpriteRect.zw)
u = saturate(dSource/RbodySource)
q = t*t*t * (10 + t*(-15 + 6*t))
tail = 0.22 * q * (1-u)*(1-u) * (1-saturate(oldR/0.397008))
displacement = (oldR + tail) * W * strength
```

The ending radius excludes transparent padding and shadows. The real
`newui@3x_21` fixture has UI half-size 100 logical units but source-body radius
about 233.33 source pixels; these are different spaces, not interchangeable
lengths. Nonuniform transforms must not collapse the two normalized distances
into one `d/R`. The tail and its first derivative vanish at the source-body
center; the full result also retains the unchanged base-profile displacement.
The headroom factor
retains approximately the existing strongest bend rather than simply increasing
all displacement; the peak constant is a model approximation, not a bit-exact
claim for the half-precision profile. This is a small runtime polynomial shaping
term, not a new Snell solve or a wholly precomputed response. The original G
response, fine-highlight parameters, colors, opacity, decorations and Gaussian
mode stay unchanged. There are no new texture samples, captures, Blits or RTs,
and invalid optical data still disables refraction. This continuation itself
does not recolor capture pixels; the separate current RT2/RT3 coverage treatment
is described above. Previous pixel-parity and
performance records do not validate the newly requested appearance.

This choice prioritizes the user's requested portability and native filtering,
with a small measured precision tradeoff. Against the same optical code with
the v6 RFloat/Gather reference, actual Simple/Sliced/clipped-ellipse controls
have at most 1/255 difference. Thin blue rings with 1200/600 input reach 5/255
and 4/255; 33 and 2 pixels respectively exceed 2/255 out of 1,440,000 output
pixels. The legacy maximum 2/255 test remains recorded as failed. The selected
result is not exact parity or proof of all-platform device support. Functional
regression, cross-compilation, device frame time and power are separate records.

RHalf, RFloat v2, hardware-interpolated RGBA v3, RGHalf hi/lo v4, RGB24
four-Point v5 and RFloat/Gather v6 are historical. V6's isolated candidate passed
3/3 optical cases on desktop D3D11 and 3/3 on Vulkan: 960 neighborhood/border
probes and six actual Simple/Sliced/clipped-ellipse groups had zero maximum
error. Its earlier 1200/600 thin-ring comparisons with profile off/on differed
by 1/2/1/1 eight-bit codes. These are v6 evidence, not v7 precision results.
The earlier native-bilinear R16_UNorm candidate reached 13/255 thin-ring error;
native-bilinear RFloat reached 3/255. Neither was selected under that earlier
strict 2/255 policy. V6's Gather returned original float neighbors followed by
three float lerps; it was not hardware-filtered RFloat.
Rebuild through **Tools > MajdataPlay > Rendering > Bake Glass Optics Atlas**.
The generated asset is a runtime dependency, not a Library cache.
The preceding v6 design passed targeted D3D11 batches 112/112 and 38/38,
Vulkan 17/17 and offline 5/5. These overlapping historical batches are not v7
revalidation counts or proof of all-platform device coverage. Fresh graphics-
state coverage and target-device measurements must follow the current strong-
half-size snapshot/native-RGBA8 implementation and the configured MSAA policy.
Older quarter, full-size ROI and prior contour-encoding reports are historical,
not new 120-FPS or power evidence. Reproduction commands live in
`Tools/Tests/Modules/UIOutlineValidation/README.md`.

### List cover row and selection details

Each pooled `SongCoverDisplayer` owns its frame, difficulty circle, cover Image,
loading indicator and difficulty labels throughout navigation. The same Image
and Sprite binding remain in place while `selectedProgress` changes its size
and decorations. The selected target uses a centered 365-unit frame, a
285-unit cover and a proportionally scaled 310-unit difficulty circle. The
existing small difficulty labels move continuously from the side layout into
the selected arrangement; the large difficulty badge fades in with selection.

`CoverListManager` binds visible songs once and explicitly propagates difficulty
changes, including changes within the same collection. There is no fixed
`MainCover` visual copy, immediate-Sprite handoff, renderer suppression or
quality-dependent ownership branch. In every UI style, the row draws each cover
once. `CenterCoverDisplayer` retains its serialized script identity but now
handles only selection information: metadata, scores, radar/chart, online data,
preview, Favorite and the independently faded half-background cover. Settling
updates those services without replacing a foreground Image. Cancelled
same-song requests can restart after interrupted navigation.

The frame and cover retain their existing source materials and UVs. The pooled
difficulty circle's ordinary source uses depth offset 151.5, between the frame
at 152 and cover at 151. This preserves both cutout depth testing and transparent Canvas
ordering. The upper thumbnail row is unchanged.

`QualityUiImage._liquidGlassMaterial` still selects authored optics only in
Liquid Glass mode. The genuine difficulty ring draws through its ordinary Alpha
theme before RT3, retaining depth 151.5 and its original vertex color. The white
glass frame draws next at queue 3500, then the untinted cover at queue 3501.
Difficulty labels and the center Favorite badge draw at queue 3502. RT3 therefore
contains the ring, but excludes the late frame, cover and labels.

The frame uses `0.25 * white + 0.75 * refractedRT3` outside the ring. Its colored
inlay transmits captured RT3 RGB without another multiply or white wash, before
the shared lighting. Frame `_glassInlaySource` references the existing ring;
unused UV0.z/w store its normalized radius and relative opacity, preserving all
positions, colors and UV0.xy. The 0.075-wide annulus inside radius 0.45 samples
RT3; the center stays transparent and the later cover preserves scene-resolution
pixels. Frame depth remains 150.5 with LEqual testing, and its outer shadow Alpha
is 0.24, with softness 0.018 and offset `(-0.004, 0.008)`. No RectTransform,
size, position, scale, Sprite or thumbnail changes,
additional RT, Blit or capture cut are introduced.
If RT3 is unavailable, the coat becomes transparent over the inlay, preserving
the ordinary ring underneath; the outer white fallback remains unchanged.

Five authored queue-3502 material presets keep labels above the clearcoat:

| Preset | Consumers | Preserved depth |
| --- | --- | --- |
| CoverLabelForegroundAlpha | Selected badge, Favorite heart/hold; Liquid only | 0 |
| CoverLevelForegroundAlpha | Seven small badge backgrounds; Liquid only | 151 |
| CoverLevelForegroundText | Seven small numbers; all styles | 150 |
| CoverTitleForegroundText | Selected number and title; all styles | 0 |
| FavoriteForegroundCircleAlpha | Favorite null-Sprite circle; Liquid only | 0 |

The TMP presets clone all original properties and retain the font asset/atlas
references; native SDF fallback materials inherit the selected preset. There is
no Liquid-only TMP runtime branch. Favorite overrides are restricted to the
Main_Display instance; its shared prefabs are unchanged. Gaussian and other
styles retain their ordinary Image materials.

The cover rim now raises available light before Max, so pale fills no longer
hide its fine reflection. Its category-like directional baseline and
four-footprint Gaussian use width 0.0065753425, equivalent to 2.4 UI units on the
365-unit centered frame (fine sigma 0.7, underlying Gaussian FWHM about 1.65).
Gain and pixel integration affect the final AA line width. The shadow follow-up
addresses separation that still looked weak: a 4-14% inner shade now follows
broad reflection and precedes the fine glint. Exterior shadow coverage is applied
once rather than attenuated twice. Shadow Alpha/softness/offset are listed above;
geometry, glints, bevel, refraction, transmission, categories and capture order
remain unchanged, as do center transparency and the missing-RT3 fallback.
The final shadow follow-up passed 3/3 isolated Unity 6000.3.17f1 / D3D11 GPU
cases with no skips. On backgrounds 0.12/0.85, opposing white-annulus lip
darkening averages 0.01984/0.09294. Shadow width reaches three significant radial
pixels in 39 of 48 directions, with mean darkening 0.00732/0.05182 and Alpha at
most one; positive glint floors remain 0.16341/0.02672. White mixing, native
Canvas UV data, half-size RT3 and fallback also pass. Evidence is under
`CodexDiagnostics/CoverRimShadow-20261113/Validation-D3D11`. Complete main-UI
appearance, target devices and FPS were not verified.

Before the shadow follow-up, the preceding shader/material passed 3/3 isolated
Unity 6000.3.17f1 / D3D11 GPU cases:
a 48-angle light/dark annulus sweep, white 25/75 mixing, native Canvas UV data,
real RT3 and fallback. The sweep checks visible bounded glints, front/back
directionality and premultiplied shadows; on background 0.85, the weakest glint
increase is 0.02943 and front/back means are 0.10254/0.03937. Evidence is under
`CodexDiagnostics/CoverRimLighting-20261113/Validation-D3D11`. Complete main-Editor
appearance, target devices and FPS were not validated.

Before the rim-lighting revision, the clearcoat passed 3/3 PlayMode and 6/6
EditMode cases in isolated
Unity 6000.3.17f1 / D3D11. PlayMode covers native Canvas UV0 radius/opacity
transport at opacities 1, 0.5 and 0 without extra Canvas channels, the real MRP
service's half-size RT3, preserved ring RGB before lighting, transparent-center
and Ready-zero fallback, and normal white/style behavior. Two asset cases cover
font/atlas references, preserved depths and all nine TMP labels at queue 3502.
Four MRP variants (1x/4x samples, optimization off/on) verify that RT3 includes
the ring but excludes the late frame, cover and badges, followed by the clearcoat,
untinted cover and queue-3502 labels, with masks, stencil and transitions intact.
Evidence is under `CodexDiagnostics/CoverClearcoat-20261113/Validation-D3D11`.
Earlier cover-iteration reports remain historical external diagnostics. Complete
main-Editor UI appearance, target devices and performance were not validated.

## Historical two-cut implementations (superseded)

The previous dual-source revision used two full-resolution and two half-resolution
working targets, about 2.5 screen pixel areas, allocated on demand. It shared the
first scene prefix between large boards and categories, then refreshed the
peripheral source. Its late Gaussian fix reused the already consumed half pair,
overwrote the earlier panel result and cleared the obsolete Ready flag. These
source, reuse and release rules are no longer the current implementation.

That Gaussian-peripheral fix passed 3/3 targeted PlayMode cases on isolated Unity
6000.3.17f1 / D3D11, with no failures or skips. Reports were
`gaussian-peripheral-strong-play.xml/log`. The preceding dual-source version
passed 27/27 EditMode and 2/2 PlayMode cases with no skips, covering its then-current
full/half selectors, colors, two-axis filters, late reuse, resize and style-change
cleanup. These are evidence for those historical versions, not the new three-stage
persistent pools. The former restriction that kept Setting's lower white strip
Alpha-only in Gaussian mode has also been superseded.

An earlier two-cut version deliberately included List's background song-cover
Image in its first snapshot. Frame Debugger event 55 had shown a 512x683 result
missing that cover, although the final event 121 contained it. The historical fix
moved the opted-in Image to queue 2999, and event 56 then included it. The frame
retained 121 draws and two cuts; its focused asset 1/1, lifecycle 2/2 and actual-MRP
GPU 1/1 cases exercised that old ordering, internally covering 1x/4x samples.
That first-source requirement is explicitly obsolete: the current RT1 excludes
the cover and Live2D, while RT2 intentionally includes the cover. The opt-in flag
is retained, but its current queue is 2950. Sprite loading and the original cover
fade remain intact. No fresh device graphics-state capture has established the
new ordering's coverage.

## Additional capture coverage

For custom settings, cover the actual supported sample counts 1x, 2x, 4x and 8x
with both direct output requested and intermediate output. The presets above
already exercise several combinations. Check `[MRP.Attachments]` and the current
output-path display: a request for 8x that falls back to 4x does not capture true
8x states. Direct output can also fall back to an intermediate path on a given
device; do not treat the menu selection as proof of the path rendered.

There is no need to enumerate every resolution percentage, texture quality,
FPS limit or VSync value solely for PSO coverage. Size and sampler/uniform changes
within the same attachment/pass configuration do not introduce a distinct MRP
shader variant. However, crossing from direct output to a scaled intermediate
target must be exercised. A representative downscale and supersampling path
also provide useful image validation without treating every slider value as a
separate collection.

Visit actual gameplay, UI masks, note effects, backgrounds and any Live2D content
under each distinct configuration; changing a setting only in the menu does not
record the gameplay states. The table describes why these paths require
coverage. The matrix above records them for the requested chart and its
packaged production warmup has been verified; other content needs coverage.

See [validation results](../../../Tools/Tests/Modules/GraphicsStateValidation/RESULTS.md)
for the device evidence and isolated-build setup limitations.

## Capture on a device

1. Build with **Unity 6000.3.17f1**, the Android target and **Development Build**
   enabled. `AndroidGraphicsStateTrace` starts before the first scene. It is
   excluded from release players, other platforms and Editor play mode.
2. Run the development player on the device. Visit Init, Title, Login, List,
   Game and Result, and exercise the note types, skins, backgrounds and rendering
   settings that the release should cover. Unity's collection only records what
   is rendered. A Unity quality level can contain several runtime rendering
   presets; exercise their MSAA/render-scale settings as needed.
3. Return to another scene or background the application to checkpoint. Scene
   loads, quality-level changes, loss of application focus and normal exit save
   the trace; forced termination is not a reliable final checkpoint. Tracing
   resumes when the app regains focus. Files are written only at these boundaries,
   never in a per-frame loop.
4. Check the device's MajdataPlay log for `AndroidGraphicsStateTrace` and a
   successful save with a nonzero variant count. The file is under
   `Application.persistentDataPath/GraphicsStates/`, named for its API and quality
   index, for example `Android-Vulkan-Quality0.graphicsstate`.
5. Pull the file with ADB. The usual application identifier and external storage
   location give this command; use the actual player identifier/path if different:

   ```powershell
   adb pull /sdcard/Android/data/net.majdata.majdataplay/files/GraphicsStates/Android-Vulkan-Quality0.graphicsstate ./Android-Vulkan-Quality0.graphicsstate
   ```

6. Import the captured `.graphicsstate` into
   `Assets/Resources/GraphicsStates/Android/` using Unity. Keep its generated
   `.meta` file with the asset. Rebuild the Android player and confirm that the
   gameplay loading log reports the matching collection being warmed up.

Each run starts a fresh capture and overwrites the corresponding device files.
Pull a completed capture before beginning another run. Capture each shipping
graphics API and Unity quality level separately. Keep the shader/material setup
and build stripping settings consistent with the release, and recapture after
changes that affect shader variants or graphics states. The included capture and the subsequently recorded matrix are described above;
new content or configurations still need their own coverage.

On devices where `SystemInfo.supportsParallelPSOCreation` is false, Unity can
record shader variants without graphics states. A successful capture or warmup
does not by itself demonstrate elimination of shader compilation stalls; verify
the rebuilt player on the target device, including a cold startup and gameplay.

API reference: [Unity 6000.3 GraphicsStateCollection.BeginTrace](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/experimental/rendering/graphicsstatecollection/begintrace).
