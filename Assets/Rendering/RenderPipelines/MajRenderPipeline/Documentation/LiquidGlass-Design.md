# Maj liquid glass design and reference history

## Current controls and audit boundary

Refined's **Rendering Style** row (formerly Panel Style) retains its Gaussian /
Liquid preference. Custom now exposes five complete `UIOutlineMode` choices:
None=0, Performance=1, All=2, GaussianBlur=3 and LiquidGlass=4. All is ordinary
Alpha without capture; 3/4 derive the same All artwork theme plus the selected
capture treatment. Refined maps its saved `UIBackdropStyle` to 3/4 and entering
Custom inherits that state. Explicit legacy All never revives a hidden blur
flag; only documents missing the mode key migrate the old boolean and explicit
blur fields. `UIBackdropBlur` is read for migration but no longer serialized out.
See [UI policy](UI-Outline-Quality.md) for the complete table.

The current cleanup is static only: no tests, shader compilation, Unity/computer
use or device runs were requested or performed. It removes unused shader state,
variant-only work, service publishing/configuration and old investigation code,
and reuses CPU transforms/material release/sorting-layer names. Optical shape,
highlight/color recipes, hardware-filtered distance decoding, float coordinates
and capture topology were preserved by that cleanup. The subsequent explicit
quarter-grid Gaussian request changes its blur kernel/grid as described below
and remains untested. User-reported measurements from
the accepted version before this cleanup were Pad 5 Pro Setting approximately
100 FPS Gaussian / 103 FPS Liquid, and Xiaomi 15 approximately 121 FPS for both
with 1% lows about 100 / 92-100. The user reported no visible rendering anomaly.
These precede the cleanup and quarter-grid change, and do not demonstrate absolute optimality.
Older implementation/test sections below retain their historical scope.

## Current rendering path

The current menu path has three independent, ordered scene prefixes. RT1 is
captured after the drawn background prefix, including PV and the bound blue ring,
and before camera extensions, boards or the
background song cover. It supplies every Board surface, including both submonitor
cards, the Title loading plate and Login window. RT2 is captured after the boards
and background song cover but before categories; Setting also records it without
a cover. RT3 follows categories and ordinary foreground UI, before peripheral
button bodies and the Liquid List cover overlays. No later cut overwrites an
earlier result.

Beautiful and Refined now default to MSAA off (one scene sample). Custom
2x/4x/8x requests remain independent and apply to backdrop-capture cameras too;
there is no hidden single-sample menu override. Filter and raw snapshot targets
remain single-sample, so configured multisample scenes still require resolves
at the ordered capture cuts. Captures and normal final presentation remain;
this is not a zero-copy claim. Older forced-1x and Vulkan StoreAndResolve
experiments are historical evidence, not the current sampling policy.

Gaussian mode uses quarter-resolution downsample plus two-axis fixed Gaussian for
all three results. Liquid mode retains that quarter-size RT1, but RT2 and RT3 each
receive one bilinear scene-prefix Blit into an independent ceil-half target,
without Gaussian. Their owned snapshots use Bilinear/Clamp. The copy pass
supplies linear sampling without changing a
borrowed Point-filtered source. There is no small Gaussian, guard-sized RT, ROI,
small-kernel calibration or small-filter scratch. Existing refraction, color,
coverage and source ordering remain unchanged.

All glass surfaces read their result directly; Gaussian surfaces have no UV bend
or local nine-tap category blur. Texture-derived color and coverage policies are
unchanged.

### Liquid List cover overlay

Only Liquid mode uses the pooled List frame as a white glass body.
The genuine difficulty ring uses its ordinary Alpha theme before RT3, with its
original color and depth offset 151.5. The white frame then draws at queue 3500,
sharing RT3 with peripheral controls. The cover follows at queue 3501 using
`CoverForegroundAlpha`, without refraction or tint. Difficulty badge backgrounds,
text and the center Favorite badge draw last at queue 3502, so RT3 contains the
ring but excludes the frame, cover and these labels. Neither the ring nor badges
add an independent glass effect. Gaussian and other styles retain their ordinary
Image source materials. No RectTransform, position, size, scale, Sprite or upper
thumbnail geometry is changed.

Outside the ring, the frame mixes `0.25 * white + 0.75 * refractedRT3`. In the
colored-ring region it transmits captured RT3 RGB without another color multiply
or white wash, then applies the shared surface lighting. `_glassInlaySource`
binds the existing ring Image: the frame mesh's unused UV0.z/w carry the ring's
normalized radius and relative opacity. The existing mesh positions, colors and
UV0.xy remain intact; no inlay texture, new draw or per-song material is needed.
The radius is 0.45 and bevel width 0.075, with a transparent center inside 0.375.
The later cover therefore retains its scene-resolution pixels. The frame keeps
LEqual testing and depth offset 150.5; its outer shadow Alpha is 0.24, softness
0.018 and offset `(-0.004, 0.008)`.
The existing early mask, UGUI clipping, transitions and sensors retain their
ordering. No RT, Blit or capture cut is added.
If RT3 is unavailable, the coat becomes transparent over the inlay so the
already drawn ordinary ring remains visible; the outer white fallback is kept.

Late badge Alpha presets preserve depth 151 for the seven small backgrounds and
depth zero for the selected badge. Favorite's null-Sprite background uses a
queue-3502 circle Alpha preset to retain its circular shape; its heart and filled
hold indicator reuse the depth-zero AtlasAlpha preset through Main_Display-only
Liquid overrides. Shared Favorite and hold-circle prefabs are unchanged. Two
cloned TMP presets also use queue 3502 in every style: the seven small numbers
retain depth 150, and the selected number/title retain depth zero. This is static
draw order, with no Liquid-only TMP runtime branch. Font asset GUIDs, atlas
texture references and all other preset properties are retained; native SDF
fallback materials clone the chosen preset.

The cover-only rim uses available-light increments followed by Max instead of
an absolute brightness target, which previously disappeared on pale fills.
Its directional baseline and four-footprint Gaussian reflection follow the
category approach. `_HighlightWidth` is
0.0065753425: at the unchanged 365-unit centered frame this is 2.4 UI units,
with fine sigma 0.7 and underlying Gaussian FWHM about 1.65 units. Gain and pixel
integration affect the final AA line width; it is not fixed to that FWHM. The
wider reflection retains bevel 0.075, refraction, transmission, capture/order
and every category asset unchanged.

The shadow follow-up addresses separation that still looked weak in user review.
Its 4-14% inner shade is applied after broad reflection and before the fine glint,
so broad light cannot fill that dark lip back in. Exterior shadow coverage is
applied once, removing the previous duplicate attenuation. The larger shadow
Alpha/softness/offset above leave geometry, glints, capture and draw order intact;
the transparent-center and missing-RT3 fallback remain.

The final shadow follow-up passed 3/3 targeted D3D11 GPU cases with no skips in
isolated Unity 6000.3.17f1. On backgrounds 0.12/0.85, the visible white annulus's
opposing lip has mean composite darkening 0.01984/0.09294. Shadow width is at
least three significant radial pixels in 39 of 48 directions, with mean darkening
0.00732/0.05182; Alpha remains at most one. Positive glint floors remain
0.16341/0.02672. The other cases retain white mixing, native Canvas UV data,
half-size RT3 and fallback. Evidence is under
`CodexDiagnostics/CoverRimShadow-20261113/Validation-D3D11`. Complete main-UI
appearance, target devices and FPS were not verified.

Before the shadow follow-up, the rim-lighting revision passed 3/3 targeted GPU cases in isolated Unity
6000.3.17f1 / D3D11 against the final shader/material. A 48-angle annulus sweep
over light/dark backgrounds checks visible neutral glints, directionality,
bounded reflection and premultiplied shadow. On background 0.85, the weakest
glint increase is 0.02943 and front/back means are 0.10254/0.03937. The other
cases retain white 25/75 mixing, native Canvas UV transport, real RT3 and fallback.
Evidence is under `CodexDiagnostics/CoverRimLighting-20261113/Validation-D3D11`.
This does not verify complete main-Editor appearance, target devices or FPS.

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

Calibrator uses the established Board and Peripheral roles: its central cream
plate consumes strong RT1 in both styles and uses the calibrated main-board mix;
the two outer buttons consume RT3, with their existing symbol children in the
foreground. Their Image/Sprite geometry and material GUIDs are retained, and no
nonexistent blue-ring binding is introduced.

Setting's dark value plate consumes RT2, while its adjustment capsules consume
RT3 in both styles. `_usePeripheralBackdrop` selects the later source/queue
without changing `IsButton` or turning a capsule into an ellipse. Their Gaussian
path uses the shared texture-filter/artwork mixture and original Alpha/shadow;
it does not invent a rounded-rectangle bevel over the native artwork.

The dark Ribbon16 plate is an offset, slightly flattened ellipse clipped by
horizontal planes. Original Sprite-normalized center/radius and vertical aspect
are converted to original Sprite coordinates; subtracting the cropped body's
center keeps the ellipse independent of its top inset. `_LiquidGlassCircle`
publishes its center and both radii. A polynomial smooth maximum rounds the four
ellipse/slab intersections. A zero vertical radius falls back to the horizontal
radius; zero intersection smoothness retains the old hard maximum. Existing
unconfigured contours therefore keep their earlier circular model. Setting and
List's selected Ribbon6 categories both exclude the same transparent Sprite
padding and share their contour and highlight implementation.

The edge-light/ellipse revision passed 6/6 focused EditMode cases and 1/1
PlayMode geometry case on isolated Unity 6000.3.17f1 / D3D11, without skips.
The actual outlined atlas's fill-RGB boundary places all 16 measured contour
highlight peaks within 0-1 pixel. This replaces the earlier base-Alpha boundary
metric; preceding circle/slab results remain historical integration evidence.

The outside-circle Opaque mask restores queue 2000. The other dedicated queues
are background through 2899, boards 2900-2949, background song cover 2950-2989,
categories 2990-2999, ordinary UI 3000-3499, peripheral bodies and liquid List cover
overlays 3500, clear symbols and Liquid List cover images from 3501, late labels
at 3502, transition 4000 and sensors 5000. DisplayMaskAlpha again inherits its
original Default shader's Transparent queue (effectively 3000), retaining Alpha
blending and native Canvas ordering. Separate renderer lists enforce the three capture cuts.
Camera extensions such as Live2D run after RT1. The blue-ring queue-only clone
draws at 2899 before RT1, so its color survives the later glass body instead of
being omitted from a PV-only source. The half song cover remains at 2950 after
RT1; the other stage boundaries do not change.

### Early outside-circle mask and coverage-aware control sampling

Init retains the enabled persistent global mask and its original Sprite contour
mesh. List and Setting also retain enabled local CircleMask Images. Both global
and scene-local layers, including their overlapping drawing, remain as requested;
their GameObjects, material/Sprite references and other serialized data stay
intact. Standalone scene previews retain their own local mask. The preceding
choice to disable those Images and depend on the persistent global mask was
withdrawn. Other scenes' Alpha masks are retained. Sensor debug still uses queue
5000 and Always depth testing.

RT2 and RT3's existing Alpha channels store valid main-display circle coverage,
independently of the scene attachment's Alpha. `UiPvBackdropBlur` projects each
cut's cached `Main_Display` Rect through the current camera. Panels using the
Category source cache that Rect too, including scenes with no peripheral keys.
The results retain independent inverse affine UV mappings and safe bounds:
RT2 uses `_MajUiCategoryClipUvX/Y` and `_MajUiCategoryClipSampleBounds`, while RT3
keeps `_MajUiButtonClipUvX/Y` and `_MajUiButtonClipSampleBounds`. Bounds use each
actual result's dimensions; a later capture cannot overwrite another source's
consumer mapping.

Each first copy/downsample selects `_MajUiCaptureClipUvX/Y` for its current
source, writes coverage to Alpha and multiplies RGB by it. Gaussian H/V filter
the premultiplied RGBA together to exclude invalid exterior data. Board/RT1
disables this encoding and keeps its original sampling. The actual camera output
and masks are unchanged; no whole-capture color padding or mask reordering is used.

Every RT2 consumer uses its own Category slot, including native controls such as
Setting's dark value plate; selecting the slot follows the sampled source, not
just the `IsCategory` visual-role flag. RT3 applies its slot to real peripheral
keys and the explicitly registered Liquid List cover overlays. A visible frame
can request RT3 and provide the main-display mapping without a peripheral key.
These consumers radially limit refracted lookup positions to
their source's usable circle before the existing single RGBA read. The CPU
precomputes the projected center and a conservative two-texel inset using that
result's dimensions; points already inside the safe range are unchanged.
Filtered Alpha normalizes sampled RGB and represents valid filtering data, not
material transparency or a separate color-switch threshold. The former
three-pixel artwork-color transition is removed: there is no final surface blend
back to the unmodified artwork at the display boundary. Full glass palette,
ArtworkBlend/tint recipe, decorations and final highlights retain their order.
Geometry coverage does not classify RGB black as invalid and does not sample
depth: genuine black scene content stays black. Setting adjustment capsules
still share RT3 but are not `IsButton` keys, so their previous sampling is
unchanged. `MainDisplayUiClip` still prevents main-display UI from covering the
submonitor.

The circle bounds are CPU-precomputed for the current camera and capture size.
The shader retains only affine-coordinate/radius arithmetic and a reciprocal
square root when the sample lies beyond the safe radius. It adds no offset map
or other texture lookup; the existing offline contour and optical-profile atlases
are unchanged. A new baked UV-offset map would introduce storage and sampling
and still need to account for the camera-dependent boundary, so no performance
advantage over these few operations is assumed.

Independent targets, processing draws and the six-texture pool remain. The
consumer already reads RGBA, so this revision adds no texture read, RT,
per-control capture or Blit. Early depth rejection is still available for later
depth-tested outside-circle fragments. This RT2 extension passed three focused
isolated PlayMode GPU cases on Unity 6000.3.17f1 / D3D11 / RTX 3080 Ti, with no
skips: active copy/downsample coverage encoding, independent category bounds
with selected/unselected/native RT2 consumers and other-role isolation, and
peripheral safe sampling. Both styles are covered; changing the later RT3 mapping
leaves RT2 readback unchanged. Main-scene visual and mobile-device validation of
this extension remain pending.
The preceding RT3-only simplified path was inspected in
the real List and Setting scenes in Unity 6000.3.17f1 on Editor Vulkan: Liquid
in the tablet simulator at the saved 1.28 main-display scale / 0.22 offset, and
Gaussian at default layout in tablet and tall-phone views. No exterior black
bleed or added fill band was observed in those inspected views. Two focused
isolated D3D11 PlayMode GPU regressions passed: capture coverage encoding and
peripheral safe sampling/native-control isolation. Local configuration and
the original simulator device/zoom were restored. These historical observations
do not validate the RT2 extension or establish mobile-device behavior, all
scene/configuration coverage, or a
frame-time, bandwidth or power improvement.

Historically both mask variants used queue 4500, covering outside-circle UI
after transition and before sensor debug so captures would not contain black.
That late approach lost early-depth rejection. Current behavior restores the
original queues. An earlier consumer-color replacement was removed, followed by
an interim direct-black sampling path; the current implementation is the
coverage-aware RT2/RT3 path above. Both local and global Images remain enabled. No
duplicate-draw reduction is claimed. Earlier tests of the late layout, capture
padding, prior consumer-color replacement or disabled-local-Image revision
describe those earlier states, rather than revalidating the current treatment.

Startup retains six RGBA8, color-only, single-sample, mip-free targets: two
ceil-half raw liquid snapshots and four ceil-quarter textures for three Gaussian
results and shared scratch. Scene/style changes and Game stop capture but retain
the pool; source-size changes rebuild it and quit/reset releases it. For width
and height divisible by four it stores 0.75N pixels, half the prior all-half
pool's 1.5N: 11.71875 MiB at 1600x2560 and 5.932617 MiB at 1080x1920.
Exact storage is `(2*ceil(W/2)*ceil(H/2)+4*ceil(W/4)*ceil(H/4))*4` bytes.
This excludes scene/MSAA targets, video and optical/artwork textures; total
VRAM or bandwidth is not asserted to halve.

`s_sourceSize`, `s_halfResolutionSize` and `s_gaussianResolutionSize` retain
independent source, raw and Gaussian sizes. `GetSourceSize`,
`GetHalfResolutionSize` and `GetGaussianResolutionSize` compute these extents;
there is no long-edge cap. Actual result texel sizes are published. With all
roles visible, Gaussian still has nine filter draws and Liquid five processing
draws: three quarter-size RT1 filters plus two half-size raw copies.

The Gaussian is fixed sigma 4 / radius 8 on the quarter grid: 17 logical taps,
four paired offsets each sampled on both sides plus center, nine bilinear reads
per axis. With four downsample calls the stage sums 4+9+9=22 over equal quarter
output areas across three passes, not in one pixel invocation or as a physical
bandwidth count. GL Point-source reconstruction can expand only the downsample
calls here; owned H/V inputs use Bilinear. Pure-math `BuildPanelBlurKernel()`
builds the shared pairs and center once at type initialization. Resize/reset
never rebuilds or clears the CPU kernel; release clears GPU bindings.
`_MajUiGaussianCenterWeight` and `_MajUiGaussianPairs[4]` drive a fixed unrolled
loop. The previous half-grid sigma 8 / radius 16, 17 reads/axis and size-scaled
kernel/1024 reference cap are historical. Both grid/kernel choices suggest
approximately 16 source-pixel continuous sigma, but downsample/reconstruction
can alter fine detail and PSF. No tests, compilation or device runs cover this
requested revision; the user will assess it. Visual equality and FPS gain are
not claimed.
No mipmap blur is enabled in production. Isolated A/B tests compared explicit
LOD, mip-prefiltered Gaussian and a nine-tap reconstruction against the half-size
reference. Cheap candidates changed thin rings, edge widths or PSFs. The close
mip1-plus-complete-Gaussian candidate kept the complete kernel and added snapshot
and mip generation, so it offered no justified reduction. Mip1 is half width and
height, mip2 is quarter; generation and storage are real work rather than a free
Gaussian. These experiments establish neither target-device FPS nor power.

### Historical Windows steady-state CPU and shader specialization

`PvGlass` compiles Gaussian and liquid shading as two material-local programs
(`MAJ_UI_LIQUID_GLASS`, `multi_compile_local`). The existing global style still
controls capture and geometry, while each owned material and its installed
Canvas stencil material receive the same local keyword on style changes. Both
variants are retained for runtime switching. This removes unused optical state
from Gaussian draws without changing the filter, palette, coverage or draw order.

Imported Ribbon Sprite-to-layout and Sprite-to-outline mappings cache successful
validation by actual Sprite/Texture identity. The bounded caches are cleared on
quality/resource reset and Editor asset changes; misses are not cached, and
outline compatibility still checks texture dimensions. Visibility queries test
the requested source role before native Canvas properties and stop after the
first matching consumer. Visibility and transforms are never frozen across
frames, so fades, movement and masks continue to update normally.

Unity 6000.3.17f1 / D3D11 isolated old/new comparisons matched all 442 RGBA
captures exactly. A separate Release shader benchmark using 14 actual UI meshes
at 1600x2560 measured 0.013-0.020 ms less GPU time (6.2-12.7%, depending on style
and 1x/4x MSAA). This is a shader-specific benchmark, not the complete Setting
frame. A warmed 8192-query mapping benchmark measured 8.6719 ms before versus
1.8257 ms after. The Mono allocation counter failed its positive control, so
these measurements do not establish a GC reduction.

D3D11 native StoreAndResolve segmentation was also compared outside production:
pixels and stencil matched, but the 4x fixed-target medians were approximately
0.453 ms for both paths. Production therefore retains the D3D compatible resolve
path; a capability flag or disappearance of an explicit Resolve command does not
prove lower bandwidth. These Windows results do not establish Android frame time
or power savings.

### Historical Android device follow-up before the 1x-menu revision, 2026-11-11

An earlier four-sample-menu source was built with Unity 6000.3.17f1, `BuildOptions.None`, and
installed over the earlier diagnostic APK on a Xiaomi Pad 5 Pro / Adreno 650.
The device uses Vulkan, a 1600x2560 display, 100% scene/system scaling and 4x
MSAA. These tests retained the user's screen transforms and sensor overlays.
SurfaceFlinger present timestamps were collected continuously for at least
30 seconds per case; source histories had no coverage gaps and real long
frames were retained. The external raw data and source/APK hashes are under
`CodexDiagnostics/TabletPerf-20261111`, outside the repository.

Setting Beautiful reached 120.00 FPS (1% low 116.41). With physical touch to
reduce automatic display downshifts, Gaussian reached 75.32 FPS (1% low 59.32),
and Liquid reached 69.71 FPS (1% low 39.86). Their corroborating GPU window
means were 13.14 and 14.17 ms. The display was still dynamic: 120 Hz appeared in
49.18% and 91.80% of the respective header polls, versus 100% for Beautiful.
These are polling proportions, not exact time-weighted refresh shares. Neither
effect demonstrated stable 120 FPS. Liquid's near-saturated GPU and mostly
120-Hz headers show that a 60-Hz cap alone does not explain its result.

List cases and the older build used different touch/refresh conditions, so their
FPS values are not a controlled percentage comparison. One then-current List Gaussian
GPU diagnostic window contained an impossible value; its GPU mean is invalid,
while the independent SF present record, including a real 291.664-ms stall,
remains intact. The external parser now distinguishes `Frames` from
`MaxQueuedFrames` and preserves invalid GPU windows without treating them as
GPU performance evidence. This does not fix the production timing collector.
Frame-timing diagnostics and temporary display requests were restored after
measurement. No power-saving or target-device 120-FPS claim follows from the
Windows shader benchmark.

The earlier full-size Liquid RT2/RT3 revision optionally drew only a disjoint union of consumer regions at
the unchanged full resolution. A 64-pixel tile mask bounds native world corners,
the conservative maximum lens displacement (below 0.4 times bevel width) and
bilinear sampling. Horizontal scratch includes the vertical filter's Y support;
all H regions finish before V begins. Procedural triangles retain whole-target
UVs, while scissors limit execution. High coverage, fragmentation or uncertain
geometry retains the full-frame path. No additional RT, lower-resolution liquid
small source or color/optical change is introduced. The historical seven Liquid
filter draws describe the full-frame baseline; regional draw counts depend on
the merged mask. The focused isolated D3D11 PlayMode case passed 1/1 without
skips, with eight internal GPU combinations for category/peripheral sources,
1x/4x resolve, movement, rotation, nonuniform scale and viewport offset. Every
compared consumer-region RGBA pixel matched full-frame H/V exactly. The 768x1280
fixture used 3-4 category or eight peripheral output rectangles, covering
45,056-180,224 pixels, without resizing sources or replacing the seven targets.
The report is external `roi-play.{xml,log}`; this is not Vulkan hardware or power
validation. Reproduce with the UI validation runner's PlayMode/Graphics filter
`MajdataPlay.Tests.Game.Rendering.UiPvBackdropLifecycleTests.LiquidSmallBlur_RoiMatchesFullOnMovingDisjointConsumers`.

That earlier full-size build used nine Gaussian or seven liquid filter draws. Isolated Unity
6000.3.17f1 / D3D11 verification passed 25/25 targeted EditMode cases, including
the initial actual-MRP 1x fixture, shader 1x/4x three-source selection, 38 authored
Panels / eight Boards and size mapping. PlayMode passed 7/7 for the seven-target
pool lifetime, independent results, filtering dimensions and the Game capture
guard. A later actual-MRP batch passed 2/2 at 1x/4x, asserting real attachment
Samples, all three prefix colors and camera-extension order. Updated legacy
white-strip asset expectations passed a separate 8/8 without production changes.
Every batch had zero failures or skips; overlapping batches are not a unique
combined test total.

Reports are `three-source-edit`, `three-source-play`, `three-source-msaa-edit` and
`three-source-assets-edit` XML/log pairs under the current diagnostics. Exact
commands are in the UI validation README. Main-Editor interaction, target-device
appearance and power remain unverified. The following resource-path discussion
describes the superseded dual-source implementation; its optical/color
derivations remain reference material.

## Previous dual-source path (historical)

`Assets/Rendering/Shaders/UI/PvGlass.shader` implements Maj's menu optics directly.
The previous `KyantLiquidGlass.hlsl` port is no longer included and has been removed.
The current implementation does not reproduce the reference shader's circle-map
lens, vibrancy, clipped pixel-rounded stroke, or per-fragment nine-tap blur.

The first-stage source is selected by surface role in both styles. At the
first scene-prefix cut, categories and ordinary small surfaces use a
full-resolution small Gaussian. Six explicitly authored `_useStrongBlur` panels
use a separate half-resolution strong Gaussian: the List main board, Setting main
board and lower white strip, Search main board, and the UserInfo and
UI/ChartVisualDisplayer cards. These flags do not override LiquidOnly gating:
Setting's lower white strip still uses original Alpha in Gaussian mode. Setting's
dark OptionBG and the Practice/Calibrator
LiquidOnly boards keep the small full-resolution source in liquid mode and their
original Alpha fallback in Gaussian mode. The peripheral eight keys retain the
later capture after ordinary UI and categories. Liquid mode uses full-resolution
small Gaussian; Gaussian mode restores strong frost using the half-resolution
pair after queues 3000-3499 have finished consuming it. Button draw order stays
unchanged.

The service allocates the full-resolution pair and half-resolution pair only as
needed. At the first cut, the current command stream records the strong result's
downsample and two axes first (three draws), then directly filters the borrowed
resolved scene horizontally and vertically for the small result (two draws). Both
read the same scene prefix and finish before the first foreground UI; neither
result depends on the other. Late liquid buttons reuse the full pair for two
further draws, totaling seven for a complete mixed menu. Late Gaussian buttons
reuse the consumed half pair for downsample and both strong axes, totaling eight.
The late half result is published as the button source; the first-stage and panel
Ready flags are cleared because their earlier consumers have finished. This does
not add a target, and Gaussian buttons alone need only the half pair. Both styles
use at most four RGBA8 targets, with no additional scene Copy, per-control
capture, depth attachment or mip chain. At divisible dimensions, the targets
cover approximately 2.5 screen pixel areas; MRP's sampleable scene attachments
are separate from this working-storage count.

The small Gaussian retains sigma 1.5 at a reference short side of 1080 pixels.
On a size change, normalized weights are computed for actual integer source
texels and adjacent pairs are combined exactly with bilinear filtering. The even
support radius is two to eight, with three to nine reads per axis; sigma is
capped at 8/3. The UV step is one actual texel, without scaling already paired
offsets. Kernel storage is reused and weights are not recomputed every frame.
The half-resolution strong filter retains the earlier Gaussian style's effective
screen-space blur strength. Resolution and optical/tint coefficients remain
independent.

Liquid fragments sample their role-selected prefiltered result once at a displaced screen UV.
The colored edge strip uses this same read, without another RenderTexture,
capture, Blit or per-control filter. No chromatic dispersion, forced saturation,
or extra normal map is used.

## Curved surface and refraction

`UiGlassOpticsAtlas` encoding v7 stores manually encoded source distances in
one uncompressed, linear-color, mip-free, Bilinear RGBA8 UNORM atlas. Its
4096x2048 storage is 32 MiB after adding the settlement contours. RGB hold three monotone coarse channels and
A holds the fine residual. Each tile has its own positive range and signed
minimum. After one native bilinear sample, float decoding uses
`normalized = dot(rgb, 1/3) + a/765`, then `distance = normalized*range + minimum`.
The distance discretization is about 17.6 bits; four 8-bit channels do not mean
32-bit floating-point distance precision. No rounding, fract or bit reinterpretation
is applied to the filtered channels.

The current contour lookup needs one native texture sample and this linear
decode. It has no Gather, per-fragment texel-cell floor, four Point reads or
manual bilinear reconstruction. The shader baseline is target 3.0, with an
explicit full-float data sampler on GL/GLES and a typed float texture elsewhere.
Eligibility checks RGBA8 Sample/Linear support and the shader level rather than
an operating-system/backend whitelist. This improves distance-format portability;
it does not establish that every platform or device supports the complete effect.

The independent 256x1 RGHalf profile still requires Sample/Linear support. It
retains displacement divided by bevel width and curved-surface edge weight for
the fixed IOR 1.45 / thickness-to-width 1.2 model, including existing flat-center
skips. Neither resource supplies UI color or captured PV, and neither follows
artwork texture LOD bias. Missing/unsupported formats or profiles disable both
optical paths. Stale/unmatched Sprite contours and invalid decode parameters
disable Sprite refraction. There is no four-Point or runtime-Snell optical fallback. Coverage-only
processing continues to protect original artwork color/Alpha, symbols and shadows.

The exact native `Maj/UI/Rounded Rect` source has a separate contour-ready flag,
`_LiquidNativeContourReady`, for the unselected List/Setting capsules that have
no Sprite atlas entry. Eligibility requires a null Sprite, `Image.Type.Simple`,
`useSpriteMesh = false`, positive Rect dimensions, no peripheral-key role and no
ellipse/clipped-circle shape, in addition to the existing format/filter/profile
gates. The fragment already evaluates the original rounded-rectangle distance
for Alpha coverage, including independently animated corner radii. Optics reuse
that distance, its screen gradient and units-per-pixel conversion; no second SDF
evaluation or contour texture read is added. The same precomputed RGHalf response
profile remains required. Original `roundedCoverage` clips once and extra body
coverage stays one, retaining original Alpha/colors and size/corner animations.
This is exact native-contour reuse, not a substitute analytic lens when Sprite
data is missing. Unknown procedural shapes and invalid Sprite maps remain
non-refractive. This fix does not add an RT, capture or filter, and its GPU,
appearance and device validation must be reported separately.

The compatibility/native-filter choice accepts a small measured precision
tradeoff. Current plain-dot v7 compared with the same optical code and original
v6 RFloat/Gather reference differs by at most 1/255 for actual Simple, Sliced and
clipped-ellipse controls. Thin blue rings with 1200/600 source inputs reach 5/255
and 4/255; 33 and 2 pixels respectively exceed 2/255 out of 1,440,000 output pixels.
The old strict maximum 2/255 comparison remains recorded as failed. The images
were reviewed, and v7 was selected to prioritize the user's explicit portability
and native-bilinear preference; this is not exact parity or proof that every
platform/device has been tested. Functional regression, cross-compilation,
actual device frame time and power remain separate validation layers.

For historical context, v6's isolated Gather candidate passed 3/3 desktop D3D11
optical cases and 3/3 Vulkan cases. Its 960 neighborhood/border probes and six
actual Simple/Sliced/clipped-ellipse groups had zero maximum error; earlier
1200/600 thin-ring comparisons with profile off/on differed by 1/2/1/1 codes.
Those comparisons concern the preceding sampling layout, not v7 precision.
The tile mapping composes with the existing Sprite UV-to-original-source mapping,
so original slicing, expanded shadows and nine-slice stretching remain unchanged.
The same artwork slice may have separate contour variants for different body
insets. Baked guard texels and per-tile bounds prevent adjacent tiles from
bleeding. Helper derivatives sample those stored guards; fragments outside the
valid tile do not acquire an extrapolated analytic lens or normal. Data reads
use explicit LOD0 and ignore the user's texture-precision bias.

The CPU selects a matching Sprite tile when geometry changes. Newly edited
unmatched Sprite contours and procedural shapes other than the exact native
Rounded Rect path above have no refractive fallback until valid baked data
exists. Missing hardware/profile support likewise disables refraction.
A small coverage-only geometry function remains to protect authored RGB/Alpha
and decorations; it does not generate a replacement lens. Valid v7 optics
still convert source distance and normals through the current Canvas/UV mapping,
including nonuniform scale, Sliced geometry and capture-coordinate derivatives.
A fixed precomputed final screen-UV displacement would not preserve those
changing layouts. These are screen-space optics, not captured scene normals or
3D ray tracing.
The older RHalf contour format quantized broad bevel distances enough to create
visible steps after differentiation and normalization. RFloat v2, interpolated
RGBA v3, RGHalf hi/lo v4 and RGB24 four-Point v5 are historical. V3/v4 failed
actual GPU precision comparisons. V5 retained full-float reconstruction through
four exact encoded reads but required those Point instructions and decoding.

The more recent R16_UNorm native-bilinear candidate produced 13/255 thin-ring
error; native-bilinear RFloat produced 3/255, over the unchanged 2/255 acceptance
limit. Neither was selected under that earlier strict policy. Historical v6 used
the original float Point neighbors followed by float interpolation, rather than
accepting a hardware-filtered approximation.

Historical encoding version 2 stored only the smooth ellipse term for horizontally cropped
ellipses. The shader combines that sampled term with the exact horizontal slab
using the original Max / polynomial smooth-Max equations. Interpolating a baked
final Max had smoothed its internal branch crease and moved the refraction normal
even with RFloat. Sampling the curve before combining these inexpensive planes
retains the analytic operation order, shape, coverage and intersection softness.
Encoding v7 retains this curve/plane separation; the resource loader rejects
stale encodings instead of silently decoding them as v7.

The historical v2 precision/intersection revision passed 4/4 focused EditMode and 7/7 formal
PlayMode tests on isolated Unity 6000.3.17f1 / D3D11. An additional external
four-view thin-ring probe passed separately. The actual Setting board with a
1.3-pixel blue-ring stress source differs from the analytic reference by at most
3/255 with full-resolution capture and 2/255 with half-resolution capture;
independent profile and contour switches are covered. The original six
Simple/Sliced and 1x/4x comparisons remain within 1/255. Capture dimensions and
filtering are unchanged; half-resolution input can still lose fine source detail.
This is not main-game or mobile-device validation.

The current Sprite contour path uses one native bilinear RGBA8 sample and float
affine decoding, with no four-Point or substitute analytic optical fallback. The
exact native Rounded Rect path instead reuses its original Alpha-coverage distance
as described above. Native bilinear still
filters neighboring texels, so fewer shader instructions do not prove an equal
reduction in memory traffic. Settlement coverage increases the atlas to 32 MiB. The independent profile
and flat-region skips remain. Unsupported/missing Sprite data permits coverage-only
artwork protection, with no substitute Snell or normal calculation for refraction.
This adds no RT, capture or Gaussian pass. Rebuild through the glass-optics bake menu
after contour changes; device frame time and power require separate measurement.
The earlier RHalf revision contained 14 distinct contours at 2048x2048 RHalf (8 MiB of
GPU distance data) and a 256x1 RGHalf profile (1 KiB). CPU pixel copies are
discarded. Isolated Unity 6000.3.17f1 / D3D11 verification passed 4/4 focused
EditMode and 6/6 PlayMode cases, without skips. All 36 authored Sprite-based
controls resolve matching data; two procedural controls retain analytic contours.
Six GPU comparisons of actual category/value-plate artwork, including Simple,
Sliced, nonuniform scaling, rotation and 1x/4x MSAA, differ from the analytic
reference by at most 1/255 per color channel, with mean absolute differences
below 0.00002; original alpha coverage is retained. Native MRP tests separately
verify that RT1 contains the blue ring while the half song cover enters only
RT2. These checks do not establish main-Editor appearance or mobile power.

The current lens samples inward through a smooth raised surface. It replaces the
rejected exterior-source shelf, which stretched a nearly constant background
strip around the perimeter. Let `d` be the nonnegative inward distance from the
body contour, `W` the bevel width, and `T` the surface height. The following
equations generate the offline optical profile; the runtime only samples that
matching profile and never executes them as a missing-data Snell fallback:

```text
t = clamp(d / W, 0, 1)
e = 1 - t
H(d) = T (1 - e^3)
dH/dd = 3 T/W e^2

n = normalize((dH/dd, 1))
eta = 1 / 1.45
c = sqrt(max(1 - eta^2 n.x^2, 0))
k = c - eta n.y
raySlope = k n.x / max(eta + k n.y, epsilon)
D(d) = 1.15 H(d) raySlope
sourceDepth(d) = d + D(d)
```

The polynomial height, its derivative, and the refracted ray belong to the same
surface model. Snell refraction uses square roots rather than inverse
trigonometric functions. This is an inexpensive screen-space approximation of a
raised refractive surface, not a full two-interface volume or an energy-conserving
caustics simulation. Refraction changes the source coordinate; it does not invent
an edge color or repeat one fixed source strip around the control.

At the inner endpoint `d = W`, both `D` and `dD/dd` of the analytic base model
are zero. Untailed surfaces therefore join the unchanged center without a
sudden change in magnification. The cubic
surface also has zero first and second height derivatives at this endpoint.
This smooth join describes the bevel-to-center boundary, not the complete
artwork contour or every point of its two-dimensional distance field. The actual
linearly sampled half-precision LUT retains small piecewise-linear slope changes;
analytic continuity does not imply a bit-exact smooth derivative at each texel.

`UiBackdropPanel` selects width by the existing contour role. The 19 native
category controls and explicitly elliptical or capsule controls use
`W = 0.90 R`, where `R` is the logical short half-size. These controls no longer
have a 20-design-unit width ceiling. At the selected profile the peak displacement
occurs near `d = 0.32 W`, not at the middle of the band: the wider band moves the
visible refraction into the button rather than leaving it only at the perimeter.
A circular control retains a flat central radius of `0.10 R`.

The eight peripheral keys and other surfaces, including the large cream boards and the two submonitor cards,
use the base width `W0 = min(20 design units, 0.40 R)`. This remains the peripheral
reflection/base-lens band, not the full displacement range after the continuation
below. The cards' original Sprite-space
contours, body insets and nine-slice mapping remain unchanged. The scale from
design units to root-Canvas units is 1.6, adjusted for nested control transforms
and small controls.
The fine reflection width and softness are independent of this wider lens band.

An explicit per-surface `_liquidRefractionWidthScale` multiplies the selected
base width afterwards; its default is one. The List, Setting and SortFind
background boards set it to 2, and the UserInfo and UI/ChartVisualDisplayer
white cards set it to 1.5. Only the two central Setting plus/minus capsules set
it to 0.75 as of 2026-11-12. Other boards and buttons retain scale one.
This changes refraction range and the cap's corresponding thickness, not the
fine reflection width, tint, white veil, coverage, capture count or Gaussian.

At the current matched reference scale, the Setting adjustment band changes
from 47.3985 to 35.5489, between the top categories' unselected/selected widths
of 34.56/37.035. Its model peak displacement is approximately 14.113. Only the
two adjustment panels change: the fine-highlight width, colors, interaction,
other roles and Gaussian path are retained. The existing field is reused;
neither the profile nor contour needs rebaking, and no shader field or texture
read is added. These numbers describe the current geometry/model, not measured
appearance, frame time or target-device performance.

### Peripheral-only polynomial continuation, 2026-11-12

The old narrow peripheral band left a large flat center, despite the base
profile's continuous analytic join. Only `UiBackdropPanel.IsButton`
(`_isButton && !_isCategory`) now sets `_LiquidRefractionTail = 0.22` on its owned
material and actual stencil clone. Reusing RT3 does not itself select this
policy. Categories, Setting plus/minus controls, boards and submonitor cards
retain their previous response.

Keep `P` as the old profile's normalized R displacement, or zero beyond its
original band. Its lookup still uses logical distance `dLogical` and band `W`.
The ending envelope instead uses the original sampled `sourceDistance`, after
the existing contour crop composition, and the authored source-Sprite body
radius `RbodySource = min(_LiquidGlassSpriteRect.zw)`. The Shader adds:

```text
t = saturate(dLogical/W)
dSource = max(-sourceDistance, 0)
RbodySource = min(_LiquidGlassSpriteRect.zw)
u = saturate(dSource/RbodySource)
q = t^3 (10 - 15t + 6t^2)
headroom = 1 - saturate(P / 0.397008)
F = P + 0.22 q (1-u)^2 headroom
Dperipheral = W strength F
```

The fifth-degree onset reaches its plateau with zero first and second
derivatives. The quadratic remaining-distance factor makes the continuation's
displacement and first derivative zero at the source-body center
`dSource = RbodySource`; the full displacement also includes the unchanged `P`.
The ending radius must exclude transparent Sprite padding and shadows. In the
real `newui@3x_21` fixture the UI rectangle has short half-size 100 logical units,
while the source body has radius about 233.33 source pixels. Its visible body
ends before the full UI rectangle does, so ending at the UI half-size left a
residual bend at the actual body center.

The conversion to `dLogical` uses the existing screen gradients and local-scale
mapping. Under nonuniform scaling it is direction dependent; source and logical
distances must therefore not be merged into a single `d/R`. The earlier
one-dimensional `W/R` sweep applies only to a uniform-scale, consistent-unit
model. The 0.8 bound from the authored width multiplier concerns the full UI
half-size, not this padding-excluded source-body radius, and is not a general
proof that every transformed body center has already ended the base profile.
Extremely small shader epsilon-clamped sizes and complete two-dimensional
contours require their own checks. The headroom keeps the new normalized
displacement approximately bounded by the original peak, rather than treating a
longer reach as a simple intensity boost.
`0.397008` approximates the analytic peak; the actual half-precision LUT is
quantized, so exact peak preservation is not asserted. Changing the baked cap,
IOR or optical gain requires checking this coupled peak normalizer again.

This tail is a bounded visual shaping term, not a second physical Snell solution.
It uses a few runtime polynomial multiply/add operations and the existing
distance, without additional profile or backdrop samples, RTs, Blits or baking.
The original profile G response and fine-highlight band, palette, Alpha,
decoration and Gaussian path are unchanged. The no-data path still does not
generate substitute refraction. The continuation itself does not recolor
captured pixels; current RT2/RT3 coverage-aware exterior handling is a separate
treatment described above. One-dimensional continuity of the center does
not prove the complete two-dimensional contour/medial-axis mapping is globally
smooth or invertible. Fresh visual and target-device validation remain separate
from the earlier profile-encoding and pixel-parity reports.

The unchanged base model for all roles retains `T = 1.2 W`. The shader bounds the thickness-to-width ratio to
1.2 and the material's displacement strength to zero through one. Large boards
do not increase the lens height in proportion to their complete height. A
one-dimensional normal-direction numerical sweep at
IOR 1.45 and default strength, including the 1.15 optical gain, gives a maximum
offset of about `0.397 W` and a minimum source-depth derivative of about 0.069.
The gain strengthens refraction by 15 percent without changing the band width,
highlight, tint or capture/filter passes. These numbers describe the
mathematical profile, not GPU, image-quality or device validation; they do not
prove that every curved contour is globally invertible.

The shader transforms the displacement through the unwarped capture-UV
derivatives and the actual screen-space contour normal. Coordinate and distance
calculations stay in float, and denominators have finite lower bounds. Body
antialiasing is applied to the final color composition, not multiplied into the
UV displacement. Sampling clamps to the existing captured image. The ordered
capture precedes the current glass surface, avoiding self-sampling. Raising the
thickness beyond the selected profile can create a local reversal; that is not
automatically a physically lit caustic.

## Contours and original artwork

Circles, capsules, independent rounded corners and circular boards cropped by
horizontal edges have separate contour models. The large cream Ribbon22 board
is the intersection of a large circle and a horizontal band, not a rounded box.
This contour was measured from Maj's actual artwork, not supplied by the reference
project. The five existing large-board consumers explicitly select it.

The two submonitor cards retain their original transparent padding. Normalized
body insets and corner radii locate the actual card, excluding UserInfo's binding
rings and retaining its blue frame. The contour is evaluated in preserved original
Sprite coordinates. Its screen derivatives convert the signed distance to actual
UI units along the normal, following native nine-slice and nonuniform deformation.
The original Sprite alpha remains the coverage mask. Artwork outside the card
body is restored before the separate narrow edge reflection; the original Alpha
clips that reflection only at final output.
UserInfo's slightly slanted top remains a rounded-rectangle approximation.

Ribbon16 has an independently fitted center and vertical-to-horizontal radius
ratio. Its crop join uses `max(ellipse, slab) + max(k - abs(ellipse - slab), 0)^2 /
(4 k)` only when `k > 0`; `k = 0` retains the prior hard intersection. This
geometric adjustment is separate from both artwork color and the lens width.
The two selected Ribbon6 categories exclude the same transparent source padding;
they do not have menu-specific light parameters.

## Reflection and color

The fine neutral reflection is centered on signed distance zero, rather than an
inset peak or a clamped exterior plateau. Its stored width/softness remain 1.5 /
0.875 design units, transformed with the control's layout, but the fine profile
uses half the earlier sigma. Four analytic points integrate the pixel footprint
without texture reads:

```text
sigma = 0.5 * max(softness, 0.35 * width)
profile_i = exp(-0.5 * (signedDepth + footprint_i)^2 / sigma^2)
headroom = saturate(1 - max(finishedBody.rgb))
gain = 1.6 * max(materialHighlight / 0.045, 0)
fineDirection = min(1, 1.2 * (0.3 + 0.7 * light^2))
reflection = mean(min(headroom, headroom * profile_i * gain)) * fineDirection
edgeTarget = finishedBody + reflection
surface = saturate(max(finishedBody, edgeTarget))
```

Body masking is completed before this fine light. The original PNG coverage then
clips it once at final output, instead of attenuating it again with body coverage.
Brightness supplies only the available neutral-light budget; it no longer
creates a threshold that can erase a light button's narrow profile. Increasing
gain affects the profile while its peak remains bounded by headroom. The separate
inner lip uses a pixel-variance Gaussian approximation. Widths remain continuous,
with no integer-width rounding or minimum-one-pixel stroke.
A weak, soft inner lip makes a white card's edge readable against a white scene.
This lip is independent of the reflection gain.

Liquid glass separates the original artwork fill from the processed transmitted
background. Their current mix and self-tint strengths are:

| Surface | Raw authored fill share | Transmission share | Transmission self-tint strength |
| --- | --- | --- | --- |
| List, Setting, Search and Calibrator main background boards | 0.4233333 | 0.5766667 | 0 |
| Practice background board | 0.25 | 0.75 | 0.23 |
| Two submonitor cards | 0.25 | 0.75 | 0.42 |
| Title loading plate | 0.4233333 | 0.5766667 | 0 |
| Other glass controls | 0.25 | 0.75 | 0.90 |

The transmission includes the selected texture-derived filter and existing
angular sheen. Mixing occurs before reconstruction of the original symbols,
blue frames and other decoration, and before the separate fine neutral rim.
The calibrated main-background overrides do not change the top cards, peripheral
buttons, other small controls or Practice.

The previous liquid-only white-frost parameter has been removed. The submonitor
cards use 25-percent original artwork and 75-percent processed transmission,
without a second white veil. Title follows the main-board color mixture while
retaining its calibrated sliced geometry and refraction width. Large-panel Gaussian tint and
frost settings remain separate and unchanged. Body coverage remains normalized;
the mixture does not blend the sharp, unfiltered scene back through alpha and
requires no additional capture, RT or texture read.

### Targeted decoration and Gaussian peripheral policies

UserInfo alone enables `_liquidOriginalArtworkLeft`. Original Sprite-space X
selects a soft region preserving the binding rings, which overlap the glass
body in the same Sprite26 artwork. Approximately the first 400 raw-source pixels
retain the original premultiplied RGBA; the next 60 pixels transition back to
glass. This happens after tint, ink reconstruction and the fine rim, before
ordinary UGUI masking. The region follows existing sliced UVs, Graphic tint and
fades; it does not use a runtime RGB detector, alpha clip or a new texture read.
Other liquid surfaces have the protection disabled.

Search's bottom confirmation circle now uses the standard peripheral narrow
bevel and RT3 in both styles, with its existing tick bound as clear foreground.
Its source ellipse descriptor is unchanged; no optics atlas rebake is needed.
Other liquid peripheral controls retain their previous settings.

Gaussian peripheral bodies reduce transmission self-tint to the minimum of the
authored strength and `_GaussianButtonTintStrength` (default 0.65), retaining
the original 25-percent artwork share. This reduces the extra absorption of a
strongly blurred dark surround. Categories, boards and native non-peripheral
controls keep their current Gaussian color policy. Liquid bypasses this
override entirely; its existing 0.9 / 0.25 peripheral mixture is unchanged.

This coefficient is evaluated in the same actual GPU working RGB space as the
Beautiful preset's source-over appearance. The project currently uses Gamma
color space. No new linear-light interpolation curve is inserted for this tint
adjustment, and no RGB palette is supplied from C#. Texture sampling retains
Unity's configured sRGB behavior; the Graphic and material colors must match the
values used by that UI's rendering path, without an additional gamma conversion.

For example, the current large-board fill is RGB `(255, 249, 231)`. Over white,
its Beautiful alpha `127/255 * 0.85`, about 42.3 percent, produces approximately
`#FFFCF5`. Multiplying white by the complete fill instead produces `#FFF9E7`.
The List, Setting and Search main boards now use this actual Beautiful opacity
as their raw-fill share, with no additional self-color multiplication of the
transmission:

```text
A = (127 / 255) * 0.85 = 0.4233333
body = A * rawArtworkFill + (1 - A) * processedGlassRT
```

Their transmission self-tint strength is zero, giving a neutral multiplier.
Adding another yellow multiplier would count the authored fill twice. The raw
share therefore matches Beautiful's source-over color weight for white, black
and other solid backgrounds, rather than only matching one white test swatch.
The processed RT still supplies blur, refraction and angular sheen, and the fine
rim is applied afterward; equal color weights do not mean pixel-identical output
to unfiltered Beautiful.

Practice retains the earlier 25-percent raw-fill share and 0.23
transmission self-tint. Their effective fill share over white is
`0.25 + 0.75 * 0.23 = 0.4225`, approximately `#FFFCF5`; this older calibration
does not assert equivalence for every possible PV color.

Other controls default to a transmission tint strength of 0.90. Together with
their 25-percent raw-fill share this yields an effective fill share of 0.925
over white, slightly lighter than the complete authored fill. The two submonitor
cards retain transmission tint strength 0.42; their actual white fill makes that
multiplier neutral. Only these mixing strengths are fixed. All RGB values still
come from the current texture and Graphic, with no saved cream or white palette.
These are flat-center color calculations, excluding refraction and highlights.

Setting's dark option panel and small white panel have an explicit reference to
the large cream panel as their tint source. Their transmission retains an
additional 14-percent contribution of that source's raw fill color:

```text
own = actualTextureFill * ownGraphicAndMaterialRGB
sourceFactor = lerp(1, sourceRawTextureFill * sourceGraphicAndMaterialRGB, 0.14)
filter = lerp(1, own, ownLiquidTintStrength) * sourceFactor
transmission = capturedBackground * filter
transmission = existingAngularSheen(transmission, capturedBackground)
body = lerp(transmission, own, liquidArtworkBlend)
```

The source coefficient refers to the raw authored fill, not the already reduced
source panel's transmission filter. It affects the transmission only; the final
raw-artwork share is not tinted by the source a second time.
This association does not depend on a Canvas parent or Transform hierarchy, and
it does not sample the rendered parent panel. The dark option panel keeps its
own blue-gray fill rather than replacing it with cream.

The source texture comes from the linked `QualityUiImage.mainTexture`, so the
currently selected outlined or unoutlined artwork is used. Its slice UV and
fill anchor identify a solid interior. Linked panels fetch that fill at LOD0 in
the vertex shader only when the source weight is positive. The supplied source
color uses the Graphic color and the material's stored RGB values; its weight
is separate from source alpha. A `half3` at `TEXCOORD8` carries the resulting
factor, bringing the target-3.0 shader to ten interpolator registers including
COLOR. This adds a conditional vertex texture read and interpolation, not a new
RT, capture, Blit, CPU texture readback or fragment texture read.

Artwork reconstruction still uses the original, unattenuated fill color to
separate symbols, borders and dashed decoration. Tint strength and artwork share
change glass RGB, not its normalized body coverage, source alpha, shadows,
relative fades or clipping. Along the curved surface only, the combined filter
is relaxed `0.36 * edgeBand` toward the neutral multiplier, where
`edgeBand = (1 - n.y) * strength`; this existing bevel treatment is unchanged.
No fixed yellow, blue or gray palette is painted into the bevel.

A separate angular background sheen reuses the same displaced background sample.
Its weight is `0.36 * edgeBand^2 * direction`; its target is the component-wise
maximum of the tinted transmitted background and `saturate(background * 1.12)`.
This sheen runs before foreground artwork is reconstructed, so it cannot lighten
the original dark glyphs or dashed decoration. The raw-artwork mixture follows
this sheen, reducing its background response to about 57.7 percent on the three
main background boards, 75 percent on ordinary controls and 50 percent on the
submonitor cards; there is no hidden gain compensation.
The fine rim is composed separately after the artwork and body masking, as above.
This colored transition adds no texture read and
fades to zero in the center. It is an artistic edge reflection rather than a claim
of energy-conserving reflection and transmission.

Texture tint, artwork reconstruction and body masking run before the internal
Lighten/Max composition. All bodies share the same narrow signed-distance profile;
the final neutral-light increment is bounded by the finished body's scalar room
below white. The previous inset-peak/brightness-threshold formula is historical
and is not the current composition. The fine neutral reflection and
background-colored strip have separate responsibilities. This edge revision
does not alter texture tint, Snell refraction, lens-band width or RT routing.
The final UGUI framebuffer blend remains
premultiplied Alpha Blend, preserving coverage, fades and draw order.
Original shadows, UGUI coverage, relative animation opacity, stencil and clip
rectangles retain their existing responsibilities. Both styles use the source
routing above. Gaussian categories and peripheral keys now share the same
texture-derived transmission tint and raw-artwork share as liquid glass, without
liquid refraction; large Gaussian panels retain their existing neutral veil and
Gaussian tint coefficients. LiquidOnly controls retain their Alpha fallback.

## Reference history

The earlier prototype translated shader functions from
[Kyant0/AndroidLiquidGlass](https://github.com/Kyant0/AndroidLiquidGlass), commit
`65ab177e90e5c1d8c62e70cf7755841982da65f6` on `kmp`. Its layered design helped
separate background filtering, shape-dependent refraction, final lighting and
foreground artwork. The direct translation has since been replaced at the user's
request; this document distinguishes that history from the current implementation.

The previous source port and its unused standalone license file have been removed
from the current project after a source-dependency audit. The link above records
a design reference and the prototype's history; it is not a current runtime
source dependency. The Unity-derived UGUI wrapper retains its own applicable
notice in the shader. This record does not assert visual parity with Compose,
Skia, or Apple's effects.

The subsequent review compared these primary implementations as conceptual
references, without copying or translating their shader code into Maj:

- [liquid-glass-studio, `fragment-main.glsl`](https://github.com/iyinchao/liquid-glass-studio/blob/f7b28c36305a862f5cffed3ddd51511cf1204f56/src/shaders/fragment-main.glsl),
  commit `f7b28c36305a862f5cffed3ddd51511cf1204f56`: separate shape-dependent
  refraction, background filtering and directional edge lighting.
- [flutter_liquid_glass, final rendering core](https://github.com/whynotmake-it/flutter_liquid_glass/blob/8e1e7d649bcc220312ba3d3493cfaf76a91382f2/packages/liquid_glass_renderer/lib/assets/shaders/liquid_glass_final_render_core.glsl),
  commit `8e1e7d649bcc220312ba3d3493cfaf76a91382f2`: separate geometry, optical
  response and material appearance, retaining high-precision screen coordinates.
- [drei, `MeshTransmissionMaterial.tsx`](https://github.com/pmndrs/drei/blob/bf6f4addf47467d3885de272d94ca5127f6ef68f/src/core/MeshTransmissionMaterial.tsx),
  commit `bf6f4addf47467d3885de272d94ca5127f6ef68f`: use the surface normal,
  refractive index and thickness to obtain a background-sampling ray.

Maj uses its own cubic cap, bounded screen-space ray calculation, Sprite-derived
color and existing ordered captures. These references do not make their complete
3D, Flutter or browser rendering paths dependencies of the Unity project.

## Validation boundary

The preceding v6 design passed targeted D3D11 batches 112/112 and 38/38, Vulkan
17/17 and offline 5/5. These overlapping historical batches are not v7 revalidation
counts and do not establish every platform/device or power target. V7's native
filter precision tradeoff is stated above; its functional regression, cross-
compilation and actual-device records are maintained separately. The records
below describe earlier revisions and remain historical evidence.

The latest Ribbon16 ellipse/smooth-intersection, selected-category padding and
fine-rim revision passed **6/6 EditMode** cases (two actual-atlas GPU contour
cases and four selected/unselected asset cases across both category prefabs)
and **1/1 PlayMode** geometry case, with zero skips. The checks cover vertical
ellipse radius, crop smoothing, owned/stencil material updates, zero-width light
disabling and preserved center color. They compare the highlight peak against
the current outlined atlas's fill-RGB boundary, rather than old base Alpha:
all 16 measured contour segments are within 0-1 pixel. Under the same white
background, Setting and List differ at only one of 88,800 pixels by 1/255.

The actual default-style baseline/after and final white/pattern PNGs were visually
compared. A temporary preview case also passed 1/1 and is not counted in the
formal 6+1. Reports are `rim-final-EditMode.{xml,log}` and
`rim-final-PlayMode.{xml,log}`; final captures are `rim-final-white-0.png` and
`rim-final-pattern-0.png`, held only in external Diagnostics. The exact command
is recorded in the UI validation README. This does not establish target-device
appearance or power consumption. Earlier 0-2 pixel circle/slab assertions do not
replace this revision's fill-RGB contour and PNG comparisons.

The Gaussian-peripheral strong-blur restoration passed 3/3 targeted PlayMode
cases in isolated Unity 6000.3.17f1 / D3D11, with no failures or skips. The capture
case uses an inverted XOR background: Gaussian late output is half-size 32x24
with strong X/Y filtering, while liquid late output remains full-size 64x48.
It checks consumed-half overwrite, cleared obsolete Ready flags and preserved
first-stage full content. Button-only and category-only cases verify the half-only
and full-only allocation paths, target reuse, resize and cleanup. Reports are
`gaussian-peripheral-strong-play.xml/log`; commands are in the validation README.
These three cases do not repeat the preceding 27 EditMode checks or establish
main-menu appearance, target-device behavior or power consumption.

The preceding dual-source revision passed 27/27 EditMode and 2/2 PlayMode cases in isolated
Unity 6000.3.17f1 / D3D11, with no skips. Actual GPU reads check full/half source
selection, independent Ready fallback and cross-style button colors. Lifecycle
tests execute both Gaussian axes, preserve the half result when late buttons
overwrite the reused full pair, and check four-target reuse, resize and style
cleanup. Asset and mapping cases check all six flags and actual full/ceil-half
sizes after scene scaling. Commands are in `Tools/Tests/Modules/UIOutlineValidation/README.md`;
local reports are `dual-source-{edit,play}.{xml,log}` under the 2026-11-10 diagnostics.
The main Editor was not operated; menu appearance, target devices and power are
not verified by these tests. The six explicit source flags do not change
the previously calibrated tint, artwork share, body coverage or lens widths.
Earlier single-pair results below do not verify this resource path. The
three-main-background Beautiful-opacity calibration and preceding raw-artwork /
transmission mixing revisions have separate validation scopes; their earlier
batches must not be used as proof of this source-routing revision. The recorded width, frost and tint
results below retain their original scopes; in particular, removed 18-percent
liquid-frost tests do not establish the later 50/50 submonitor mixture.

The five-panel 2x/1.5x range adjustment passed 8/8 focused asset cases and the
single PlayMode geometry case on D3D11, with no skips. These check the five exact
component overrides, unchanged peripheral/Practice/Calibrator defaults, scales
1/1.5/2 across Simple/Sliced roles, unchanged reflection width/softness and in-place
owned/stencil material updates. The shader was unchanged; GPU pixel tests were
not repeated for this adjustment. Complete-menu appearance remains unverified.

The eight peripheral keys were subsequently restored to their previous bounded
width; only the 19 native/category controls retain the wider band. The single
focused PlayMode geometry case passed 1/1 on D3D11 with no skips, checking both
Simple/Sliced controls and the peripheral Canvas stencil variant after movement
and resizing. The white-card frost and shader were unchanged in this correction.

The initial wider 27-control lens band and two submonitor-card liquid-frost revision
passed 11/11 focused EditMode cases (nine GPU cases and two card-asset cases) and
2/2 PlayMode cases on Unity 6000.3.17f1 / D3D11 / Gamma, with no skips. Two final
GPU reruns additionally check that white frost precedes a colored multiplier.
At inward distance `d/R = 0.646274`, the 1x/4x ramp probes both measure source-UV
displacement 0.044118; the flat center and old narrow-band control remain
unchanged. Thus bending reaches about one third of the full short dimension,
rather than merely changing an unconsumed width uniform. Black/gray/white frost,
opaque foreground ink, relative fades and Ready=0 fallback are covered. The
lifecycle cases exercise Simple/Sliced controls, the derived wide-button roles,
stencil updates and independent liquid-frost material caching. Complete-menu
appearance and mobile behavior remain unverified; older results below retain
their original scopes.

The per-surface liquid tint and explicit Setting tint-source revision passed
15/15 focused EditMode cases (four existing color cases, two 1x/4x layered-color
GPU cases and nine asset cases) and one PlayMode source-binding case on Unity
6000.3.17f1 / D3D11 / Gamma, with no skips. A white-background cream fixture matches
Beautiful's `#FFFCF5` baseline within one sRGB step per channel. Changing the same
source texture from cream to green updates both dark and white children while
preserving their own tint. The lifecycle case exercises a real Canvas Mask
stencil clone, source Sprite and Graphic changes without material replacement,
and disabled/self/null fallbacks. Complete-menu appearance and Linear/mobile
behavior remain unverified. The earlier lens results below belong to their
recorded implementation scopes.

The final cubic inward-refraction revision passed 7/7 focused EditMode GPU cases
and 2/2 PlayMode lifecycle cases on Unity 6000.3.17f1 / D3D11, with no skipped
cases. The GPU scope was rerun after moving the angular sheen before foreground
artwork reconstruction; the shader-only ink correction did not change lifecycle
code. Ramp readback verifies inward, monotonic but nonuniform sampling, while a
bright-circle/black-exterior fixture checks unchanged interiors and no black
bleed into the fully covered inner bevel. The 1x/4x ink checks require the colored
sheen to change adjacent fill while leaving opaque foreground ink unchanged.
Color and actual-atlas fine-highlight checks remain included. These are isolated
fixtures, not proof that every menu has the intended appearance. The rejected
exterior-strip fixtures are not acceptance criteria for this lens.

The subsequent 1.15 displacement-gain adjustment reran the five relevant D3D11
EditMode GPU cases (lens mapping and panel tint/ink): 5/5 passed with no skips.
The 32-pixel ramp measured maximum inward UV displacement 0.078125 and adjacent
source increments 0.004902 through 0.089652. Neither lifecycle nor fine-highlight
code changed in this adjustment, so the earlier two PlayMode and two highlight
cases were not repeated.

Use the isolated `Tools/Tests/Modules/UIOutlineValidation/ValidateLifecycle.ps1`
runner with Unity 6000.3.17f1. Relevant checks cover actual atlas contours,
inward background sampling, unchanged interiors, continuous reflection, color
independence, full-resolution Gaussian filtering, resource ownership and both
ordered stages. Record each newly executed scope separately. Numerical checks
of the optical profile do not replace GPU tests or visual review.

The isolated Unity Editor lab was inspected through computer use with rounded
cards, capsules and circles, light and dark fills, black/white grids, colored
icons and moving backgrounds. The selected cubic candidate was compared with
the rejected exterior strip; at preview width 32 and thickness 38 it showed
inward refraction. These are observations from the inspected preview, not a
complete production-menu acceptance pass. The same-size separable Gaussian and
independently adjustable controls help compare candidates but do not establish
visual parity with iOS. Android/iOS hardware, final menu appearance, GPU cost and
power consumption remain unverified for this revision.
