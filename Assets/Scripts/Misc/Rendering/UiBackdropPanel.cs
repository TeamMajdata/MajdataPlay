using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
#nullable enable

namespace MajdataPlay.Rendering
{
    /// <summary>Identifies the ordered scene prefix sampled by an authored glass surface.</summary>
    internal enum UiBackdropSourceRole
    {
        /// <summary>Samples only the background drawn before glass boards and the song cover.</summary>
        Board,
        /// <summary>Samples completed boards and the background cover before categories.</summary>
        Category,
        /// <summary>Samples completed foreground UI before peripheral button bodies.</summary>
        Peripheral
    }

    /// <summary>Opts one authored menu panel into the shared same-frame blurred background.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(QualityUiImage))]
    public sealed class UiBackdropPanel : MonoBehaviour
    {
        /// <summary>Converts the menu's reference design units to root-Canvas units.</summary>
        private const float CanvasUnitsPerDesignUnit = 1.6f;

        /// <summary>Identifies the layout-owned circle used by the early black display mask.</summary>
        private const string MainDisplayName = "Main_Display";

        /// <summary>Keeps explicitly authored liquid overlays after the cover and score labels.</summary>
        private const int LiquidForegroundQueue = 3503;

        /// <summary>Selects the bounded liquid optical program independently of global scene-source state.</summary>
        internal const string LiquidStyleKeyword = "MAJ_UI_LIQUID_GLASS";

        /// <summary>Opts additional controls into liquid glass without changing their established Gaussian rendering.</summary>
        [SerializeField]
        [FormerlySerializedAs("LiquidOnly")]
        private bool _liquidOnly;

        /// <summary>Selects an elliptical contour independently of the peripheral-button render stage.</summary>
        [SerializeField]
        [FormerlySerializedAs("LiquidEllipse")]
        private bool _liquidEllipse;

        /// <summary>Selects a capsule contour for native-stage pill controls.</summary>
        [SerializeField]
        [FormerlySerializedAs("LiquidCapsule")]
        private bool _liquidCapsule;

        /// <summary>Matches an authored circular board cropped by its horizontal top and bottom edges.</summary>
        [SerializeField]
        [FormerlySerializedAs("LiquidClippedCircle")]
        private bool _liquidClippedCircle;

        /// <summary>Offsets a clipped circle's center by fractions of the original Sprite width and height, independently of its crop.</summary>
        [SerializeField]
        [FormerlySerializedAs("LiquidCircleCenter")]
        private Vector2 _liquidCircleCenter;

        /// <summary>Overrides a clipped circle's radius as a fraction of the original Sprite width; zero retains half the body width.</summary>
        [SerializeField]
        [FormerlySerializedAs("LiquidCircleRadius")]
        [Range(0f, 1f)]
        private float _liquidCircleRadius;

        /// <summary>Scales a clipped ellipse's vertical radius relative to its horizontal radius; one retains a circle.</summary>
        [SerializeField]
        [FormerlySerializedAs("LiquidCircleAspect")]
        [Range(0.1f, 4f)]
        private float _liquidCircleAspect = 1f;

        /// <summary>Smooths the intersection of a clipped ellipse and its crop planes by a fraction of the original Sprite width.</summary>
        [SerializeField]
        [FormerlySerializedAs("LiquidClipSmoothness")]
        [Range(0f, 0.1f)]
        private float _liquidClipSmoothness;

        /// <summary>Locates the filled body inside the original Sprite, as normalized left, bottom, right and top margins.</summary>
        [SerializeField]
        [FormerlySerializedAs("LiquidBodyInsets")]
        private Vector4 _liquidBodyInsets;

        /// <summary>Overrides the body's corner radius as a fraction of the original Sprite's shorter side.</summary>
        [SerializeField]
        [FormerlySerializedAs("LiquidCornerRadius")]
        private float _liquidCornerRadius;

        /// <summary>Bakes irregular or compound Sprite outlines from artwork alpha instead of an analytic primitive.</summary>
        [SerializeField]
        [FormerlySerializedAs("LiquidArtworkContour")]
        private bool _liquidArtworkContour;

        /// <summary>Scales this surface's liquid refraction region without changing its fine reflection width or coverage.</summary>
        [SerializeField]
        [FormerlySerializedAs("LiquidRefractionWidthScale")]
        [Range(0.25f, 2f)]
        private float _liquidRefractionWidthScale = 1f;

        /// <summary>Samples a solid part of the Sprite instead of an embedded symbol when identifying its fill.</summary>
        [SerializeField]
        [FormerlySerializedAs("FillSamplePosition")]
        private Vector2 _fillSamplePosition = new(0.5f, 0.5f);

        /// <summary>Identifies the authored blue ring drawn after the PV snapshot in the board stage.</summary>
        [SerializeField]
        [FormerlySerializedAs("BackgroundImage")]
        private QualityUiImage? _backgroundImage;

        /// <summary>Selects the final backdrop stage for a peripheral button body.</summary>
        [SerializeField]
        [FormerlySerializedAs("IsButton")]
        private bool _isButton;

        /// <summary>Uses the peripheral backdrop and draw stage without changing the control's authored contour.</summary>
        [SerializeField]
        [FormerlySerializedAs("UsePeripheralBackdrop")]
        private bool _usePeripheralBackdrop;

        /// <summary>Keeps the category body and its foreground together after the category snapshot.</summary>
        [SerializeField]
        [FormerlySerializedAs("IsCategory")]
        private bool _isCategory;

        /// <summary>Selects the pre-board scene prefix independently of the current glass style.</summary>
        [SerializeField]
        [FormerlySerializedAs("UseStrongBlur")]
        private bool _useStrongBlur;

        /// <summary>Consumes an existing first-stage snapshot without requesting filtering or sampleable camera attachments.</summary>
        [SerializeField]
        [FormerlySerializedAs("ReusePanelCaptureOnly")]
        private bool _reusePanelCaptureOnly;

        /// <summary>Limits clear-foreground discovery to the explicitly authored control or label hierarchy.</summary>
        [SerializeField]
        [FormerlySerializedAs("ButtonForegroundRoot")]
        private Transform? _buttonForegroundRoot;

        /// <summary>Keeps an authored overlay hierarchy clear and late only while liquid glass is active.</summary>
        [SerializeField]
        [FormerlySerializedAs("LiquidForegroundRoot")]
        private Transform? _liquidForegroundRoot;

        /// <summary>Normalizes the original translucent fill into complete glass coverage.</summary>
        [SerializeField]
        [FormerlySerializedAs("FillAlpha")]
        private float _fillAlpha = 128f / 255f;

        /// <summary>Controls the Gaussian board's texture and Graphic color multiplier; buttons use the shared glass color policy.</summary>
        [SerializeField]
        [FormerlySerializedAs("TintStrength")]
        [Range(0f, 1f)]
        private float _tintStrength = 0.4f;

        /// <summary>Controls the texture and Graphic color multiplier for liquid surfaces and Gaussian buttons.</summary>
        [SerializeField]
        [FormerlySerializedAs("LiquidTintStrength")]
        [Range(0f, 1f)]
        private float _liquidTintStrength = 0.9f;

        /// <summary>Blends original artwork with transmitted color for liquid surfaces and Gaussian buttons without changing coverage.</summary>
        [SerializeField]
        [FormerlySerializedAs("LiquidArtworkBlend")]
        [Range(0f, 1f)]
        private float _liquidArtworkBlend = 0.25f;

        /// <summary>Preserves original left-side decorations using a normalized Sprite cutoff and feather half-width.</summary>
        [SerializeField]
        [FormerlySerializedAs("LiquidOriginalArtworkLeft")]
        private Vector2 _liquidOriginalArtworkLeft;

        /// <summary>Supplies the original artwork tint of an explicitly underlying panel without another scene capture.</summary>
        [SerializeField]
        [FormerlySerializedAs("LiquidTintSource")]
        private UiBackdropPanel? _liquidTintSource;

        /// <summary>Weights the source panel's original fill, independently of that panel's own liquid tint strength.</summary>
        [SerializeField]
        [FormerlySerializedAs("LiquidSourceTintStrength")]
        [Range(0f, 1f)]
        private float _liquidSourceTintStrength = 0.14f;

        /// <summary>Controls the Gaussian-style white veil independently of liquid artwork blending.</summary>
        [SerializeField]
        [FormerlySerializedAs("FrostStrength")]
        [Range(0f, 1f)]
        private float _frostStrength = 0.08f;

        /// <summary>Matches the authored Image opacity at rest so glass fully replaces the sharp scene, while relative fades remain intact.</summary>
        [SerializeField]
        [FormerlySerializedAs("ReferenceOpacity")]
        [Tooltip("Match the Image's alpha at rest. Tint and frost strengths control RGB; this baseline preserves only relative animation and CanvasGroup fades.")]
        private float _referenceOpacity = 1f;

        /// <summary>Caches the quality-aware Image whose authored layout remains unchanged.</summary>
        private QualityUiImage? _image;

        /// <summary>Pairs service ownership even when Play Mode ends before OnDisable is delivered.</summary>
        private bool _registered;

        /// <summary>Owns the panel-specific parameters without changing source or shared UI materials.</summary>
        private Material? _material;

        /// <summary>Identifies the themed source whose stencil, tint and List depth parameters were copied.</summary>
        private Material? _themedMaterial;

        /// <summary>Owns the explicit background's queue variant without changing its authored material.</summary>
        private Material? _backgroundMaterial;

        /// <summary>Identifies the themed background source whose properties and keywords were cloned.</summary>
        private Material? _backgroundSource;

        /// <summary>Identifies the base atlas already assigned to the current material.</summary>
        private Texture? _baseTexture;

        /// <summary>Identifies the slice whose immutable logical UV mapping belongs to the current variant.</summary>
        private Sprite? _sprite;

        /// <summary>Tracks whether the Image's mesh mode permits an affine atlas-space shape approximation.</summary>
        private bool _supportsShape;

        /// <summary>Identifies the exact procedural Rounded Rect shader copied into the cached variant.</summary>
        private bool _materialIsRoundedRect;

        /// <summary>Identifies the render-stage role whose queue and shader selector belong to the cached variant.</summary>
        private bool _materialIsButton;

        /// <summary>Identifies the category role whose queue and readiness selector belong to the cached variant.</summary>
        private bool _materialIsCategory;

        /// <summary>Identifies the scene prefix and draw stage owned by the cached material.</summary>
        private UiBackdropSourceRole _materialSourceRole;

        /// <summary>Identifies the captured background source selected by the cached material.</summary>
        private bool _materialUsesStrongBlur;

        /// <summary>Identifies the texture-derived filter strength baked into the cached material.</summary>
        private float _materialTintStrength;

        /// <summary>Identifies the liquid-only color multiplier baked into the cached material.</summary>
        private float _materialLiquidTintStrength;

        /// <summary>Identifies the original-artwork share baked into the cached liquid material.</summary>
        private float _materialLiquidArtworkBlend;

        /// <summary>Identifies the neutral veil strength baked into the cached material.</summary>
        private float _materialFrostStrength;

        /// <summary>Identifies the normalized fill anchor baked into the cached variant.</summary>
        private Vector2 _materialFillSamplePosition;

        /// <summary>Caches the unbatched panel transform used by the liquid-glass contour.</summary>
        private Matrix4x4 _liquidWorldToLocal;

        /// <summary>Caches the logical panel center and half size.</summary>
        private Vector4 _liquidRect;

        /// <summary>Caches radius, ellipse and clipped-circle selectors, and the original-Sprite contour selector.</summary>
        private Vector4 _liquidShape;

        /// <summary>Caches the independent circle center relative to the cropped body and its optional source-space radius.</summary>
        private Vector4 _liquidCircle;

        /// <summary>Caches the original-Sprite-unit blend width at a clipped ellipse's crop intersections.</summary>
        private float _liquidClipBlend;

        /// <summary>Caches top-left, top-right, bottom-right and bottom-left corner radii.</summary>
        private Vector4 _liquidCorners;

        /// <summary>Caches the filled body's center and half size in original Sprite pixels.</summary>
        private Vector4 _liquidSpriteRect;

        /// <summary>Caches the body's corner radii before native UGUI nine-slice deformation.</summary>
        private Vector4 _liquidSpriteCorners;

        /// <summary>Caches bevel width, cap thickness, highlight width and softness in local UI units.</summary>
        private Vector4 _liquidOptics;

        /// <summary>Tracks whether the current material has received its initial contour parameters.</summary>
        private bool _liquidGeometryInitialized;

        /// <summary>Allows the native Rounded Rect to reuse its existing coverage distance for optics.</summary>
        private bool _liquidNativeContour;

        /// <summary>Identifies the Canvas-owned stencil variant whose contour was synchronized.</summary>
        private Material? _liquidRenderMaterial;

        /// <summary>Retains the source panel's actual themed texture without owning or reading it back.</summary>
        private Texture? _liquidSourceTexture;

        /// <summary>Locates the source artwork's fill in its actual themed atlas.</summary>
        private Vector4 _liquidSourceUv;

        /// <summary>Stores working-space source Graphic/material RGB and the independent inheritance weight.</summary>
        private Vector4 _liquidSourceColor;

        /// <summary>Ensures each newly created material receives an explicit source binding, including the neutral fallback.</summary>
        private bool _liquidSourceTintInitialized;

        /// <summary>Retains the source-distance texture selected only when contour geometry changes.</summary>
        private Texture2D? _bakedContourTexture;

        /// <summary>Retains the compact preintegrated cap response shared by all liquid surfaces.</summary>
        private Texture2D? _bakedProfileTexture;

        /// <summary>Maps the existing artwork UVs into this contour's packed data tile.</summary>
        private Vector4 _bakedContourUv;

        /// <summary>Clamps data reads to the tile's padded pixel-center bounds.</summary>
        private Vector4 _bakedContourClamp;

        /// <summary>Restores manually encoded normalized distance to original source units.</summary>
        private Vector2 _bakedContourDecode;

        /// <summary>Stores source-space cutoff, feather half-width and enable state for original artwork protection.</summary>
        private Vector4 _liquidArtworkProtection;

        /// <summary>Caches the legacy Sprite identity to avoid allocating its native name each frame.</summary>
        private Sprite? _opticalSprite;

        /// <summary>Retains the source name used by offline contour descriptors.</summary>
        private string _opticalSpriteName = string.Empty;

        /// <summary>Retains only queue modifiers bound to the panel's explicitly authored native and liquid layers.</summary>
        private readonly List<UiBackdropForeground> _foreground = new(2);

        /// <summary>Gets whether this panel consumes the final snapshot after categories have rendered.</summary>
        internal bool IsButton => _isButton && !_isCategory;

        /// <summary>Gets the authored display circle followed by category consumers and physical peripheral buttons after layout.</summary>
        internal RectTransform? MainDisplayRect { get; private set; }

        /// <summary>Gets whether this panel and its foreground retain native order in the category stage.</summary>
        internal bool IsCategory => _isCategory;

        /// <summary>Gets whether the current draw material enables supported contour and precomputed-profile optics; access only on the Unity thread.</summary>
        internal bool RefractionReady
        {
            get
            {
                var material = _liquidRenderMaterial != null ? _liquidRenderMaterial : _material;
                return isActiveAndEnabled && SupportsCurrentStyle && UiPvBackdropBlur.Enabled &&
                    material != null && material.IsKeywordEnabled(LiquidStyleKeyword) &&
                    material.GetFloat("_LiquidRefractionReady") > 0.5f;
            }
        }

        /// <summary>Retains the authored board-source flag without changing category or peripheral stages.</summary>
        internal bool UsesStrongBlur => _useStrongBlur && !IsButton && !IsCategory && !_usePeripheralBackdrop;

        /// <summary>Gets the ordered scene prefix consumed independently of the surface's contour role.</summary>
        internal UiBackdropSourceRole SourceRole => IsButton || (_usePeripheralBackdrop && !IsCategory) ? UiBackdropSourceRole.Peripheral
            : UsesStrongBlur ? UiBackdropSourceRole.Board : UiBackdropSourceRole.Category;

        /// <summary>Gets whether this surface must fall back to original Alpha unless another panel requests the first capture.</summary>
        internal bool ReusesPanelCaptureOnly => _reusePanelCaptureOnly && !UiPvBackdropBlur.IsLiquidGlass;

        /// <summary>Gets whether this component participates in the current surface style.</summary>
        internal bool SupportsCurrentStyle => !_liquidOnly || UiPvBackdropBlur.IsLiquidGlass;

        /// <summary>Gets the actual camera that renders this panel's root Canvas.</summary>
        internal Camera? RenderCamera
        {
            get
            {
                var image = _image;
                var canvas = image != null ? image.canvas : null;
                var root = canvas != null ? canvas.rootCanvas : null;
                return root != null && root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
            }
        }

        /// <summary>Gets whether a live, non-culled panel can consume the shared background snapshot.</summary>
        internal bool IsVisible
        {
            get
            {
                var image = _image;
                if (!SupportsCurrentStyle || !isActiveAndEnabled || image == null || !image.isActiveAndEnabled)
                {
                    return false;
                }
                var canvas = image.canvas;
                var renderer = image.canvasRenderer;
                return canvas != null && canvas.isActiveAndEnabled && !renderer.cull && image.color.a > 0f
                    && renderer.GetInheritedAlpha() > 0f;
            }
        }

        private void OnEnable()
        {
            _image = GetComponent<QualityUiImage>();
            MainDisplayRect = null;
            if (IsButton || SourceRole == UiBackdropSourceRole.Category)
            {
                for (var ancestor = transform.parent; ancestor != null; ancestor = ancestor.parent)
                {
                    if (ancestor.name == MainDisplayName)
                    {
                        MainDisplayRect = ancestor as RectTransform;
                        break;
                    }
                }
            }
            if (Application.isPlaying && gameObject.scene.IsValid() && _image != null && !_registered)
            {
                _image.SetBackdropPanel(this);
                if (_backgroundImage != null)
                {
                    _backgroundImage.SetBackdropBackground(this);
                }
                BindForeground();
                UiPvBackdropBlur.Register(this);
                _registered = true;
                Canvas.willRenderCanvases += UpdateLiquidGeometry;
            }
        }

        private void OnDisable()
        {
            Canvas.willRenderCanvases -= UpdateLiquidGeometry;
            if (_registered)
            {
                UiPvBackdropBlur.Unregister(this);
                _registered = false;
            }
            if (_image != null)
            {
                _image.SetBackdropPanel(null);
            }
            if (_backgroundImage != null)
            {
                _backgroundImage.SetBackdropBackground(null);
            }
            UnbindForeground();
            ReleaseMaterial();
            ReleaseBackgroundMaterial();
        }

        private void OnDestroy()
        {
            Canvas.willRenderCanvases -= UpdateLiquidGeometry;
            UnbindForeground();
            ReleaseMaterial();
            ReleaseBackgroundMaterial();
        }

        /// <summary>Refreshes only material selection when the effective glass setting changes.</summary>
        internal void RefreshMaterial()
        {
            SynchronizeStyleKeywords();
            if (_image != null)
            {
                _image.SetMaterialDirty();
            }
            if (_backgroundImage != null)
            {
                _backgroundImage.SetMaterialDirty();
            }
            foreach (var foreground in _foreground)
            {
                if (foreground != null)
                {
                    foreground.RefreshMaterial();
                }
            }
        }

        /// <summary>Includes the bound blue ring in the scene-prefix capture before glass boards are drawn.</summary>
        /// <param name="themed">The active UI mode's authored background variant.</param>
        /// <returns>A cached queue-only clone, or the unchanged themed material when the effect is disabled.</returns>
        internal Material ResolveBackgroundMaterial(Material themed)
        {
            if (!isActiveAndEnabled || !SupportsCurrentStyle || !UiPvBackdropBlur.Enabled)
            {
                return themed;
            }
            if (_backgroundMaterial == null || _backgroundSource != themed)
            {
                ReleaseBackgroundMaterial();
                _backgroundMaterial = new Material(themed)
                {
                    name = themed.name + " (Backdrop Background)",
                    hideFlags = HideFlags.HideAndDontSave,
                    renderQueue = MajUiBackdropBridge.BackgroundQueueUpperBound
                };
                _backgroundSource = themed;
            }
            return _backgroundMaterial;
        }

        /// <summary>Resolves this panel's glass material while leaving non-glass presets unchanged.</summary>
        /// <param name="themed">The active UI mode's material, including original mask and tint properties.</param>
        /// <returns>A panel-owned glass variant, or the original themed material when unavailable.</returns>
        internal Material ResolveMaterial(Material themed)
        {
            var image = _image;
            var template = UiPvBackdropBlur.PvGlassMaterial;
            if (!isActiveAndEnabled || !SupportsCurrentStyle || !UiPvBackdropBlur.Enabled || image == null || template == null)
            {
                return themed;
            }
            var sprite = image.overrideSprite;
            var roundedRect = sprite == null && themed.shader.name == "Maj/UI/Rounded Rect";
            if (sprite == null && !roundedRect)
            {
                ReleaseMaterial();
                return themed;
            }
            var supportsShape = !IsButton && !IsCategory && image.type == Image.Type.Simple && !image.useSpriteMesh;
            if (_material == null || _themedMaterial != themed || _sprite != sprite || _supportsShape != supportsShape ||
                _materialIsRoundedRect != roundedRect ||
                _materialIsButton != IsButton || _materialIsCategory != IsCategory || _materialUsesStrongBlur != UsesStrongBlur ||
                _materialSourceRole != SourceRole ||
                _materialTintStrength != _tintStrength || _materialLiquidTintStrength != _liquidTintStrength ||
                _materialLiquidArtworkBlend != _liquidArtworkBlend ||
                _materialFrostStrength != _frostStrength ||
                _materialFillSamplePosition != _fillSamplePosition)
            {
                ReleaseMaterial();
                _material = new Material(template)
                {
                    name = "Maj PV Glass (Panel)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _material.CopyMatchingPropertiesFromMaterial(themed);
                SetStyleKeyword(_material);
                _material.renderQueue = SourceRole == UiBackdropSourceRole.Board ? MajUiBackdropBridge.BoardQueue
                    : IsCategory ? MajUiBackdropBridge.CategoryQueue
                    : SourceRole == UiBackdropSourceRole.Peripheral ? MajUiBackdropBridge.ButtonBodyQueue : themed.renderQueue;
                if (themed.IsKeywordEnabled("UNITY_UI_ALPHACLIP"))
                {
                    _material.EnableKeyword("UNITY_UI_ALPHACLIP");
                }
                _themedMaterial = themed;
                _sprite = sprite;
                _supportsShape = supportsShape;
                _materialIsRoundedRect = roundedRect;
                _materialIsButton = IsButton;
                _materialIsCategory = IsCategory;
                _materialSourceRole = SourceRole;
                _materialUsesStrongBlur = UsesStrongBlur;
                _materialTintStrength = _tintStrength;
                _materialLiquidTintStrength = _liquidTintStrength;
                _materialLiquidArtworkBlend = _liquidArtworkBlend;
                _materialFrostStrength = _frostStrength;
                _materialFillSamplePosition = _fillSamplePosition;
                _material.SetFloat("_FillAlpha", Mathf.Max(_fillAlpha, 1f / 255f));
                _material.SetFloat("_ReferenceOpacity", Mathf.Max(_referenceOpacity, 1f / 255f));
                _material.SetFloat("_TintStrength", Mathf.Clamp01(_tintStrength));
                _material.SetFloat("_LiquidTintStrength", Mathf.Clamp01(_liquidTintStrength));
                _material.SetFloat("_LiquidArtworkBlend", Mathf.Clamp01(_liquidArtworkBlend));
                _material.SetFloat("_FrostStrength", Mathf.Clamp01(_frostStrength));
                _material.SetFloat("_BackdropIsButton", SourceRole == UiBackdropSourceRole.Peripheral ? 1f : 0f);
                _material.SetFloat("_BackdropIsCategory", IsCategory ? 1f : 0f);
                _material.SetFloat("_BackdropIsNativeControl", !IsButton && !IsCategory && SourceRole != UiBackdropSourceRole.Board ? 1f : 0f);
                _material.SetFloat("_BackdropUsesPanelBlur", UsesStrongBlur ? 1f : 0f);
                _material.SetFloat("_BackdropIsRoundedRect", roundedRect ? 1f : 0f);
                var fillUv = sprite != null ? UnityEngine.Sprites.DataUtility.GetOuterUV(sprite) : new Vector4(0f, 0f, 1f, 1f);
                _material.SetVector("_FillSampleUV", new Vector4(
                    Mathf.Lerp(fillUv.x, fillUv.z, Mathf.Clamp01(_fillSamplePosition.x)),
                    Mathf.Lerp(fillUv.y, fillUv.w, Mathf.Clamp01(_fillSamplePosition.y)), 0f, 0f));
                if (sprite != null)
                {
                    ConfigureShape(_material, sprite, supportsShape);
                }
            }
            var baseTexture = sprite != null ? sprite.texture : Texture2D.whiteTexture;
            if (_baseTexture != baseTexture)
            {
                _baseTexture = baseTexture;
                _material.SetTexture("_PanelBaseTex", _baseTexture);
            }
            UpdateLiquidGeometry();
            return _material;
        }

        /// <summary>Synchronizes the liquid contour and explicit tint source after layout without rebuilding native UI meshes.</summary>
        private void UpdateLiquidGeometry()
        {
            if (!UiPvBackdropBlur.Enabled || !UiPvBackdropBlur.IsLiquidGlass)
            {
                return;
            }
            var image = _image;
            var material = _material;
            if (image == null || material == null)
            {
                return;
            }
            var tintChanged = UpdateLiquidTintSource();
            var rect = image.GetPixelAdjustedRect();
            var sprite = _sprite;
            var nativeContour = sprite == null && _materialIsRoundedRect && image.type == Image.Type.Simple &&
                !image.useSpriteMesh && !IsButton && !_liquidEllipse && !_liquidClippedCircle &&
                rect.width > 0f && rect.height > 0f;
            if (UiAtlasMeshGenerator.TryGetLegacy(sprite, out var legacy, out _))
            {
                sprite = legacy;
            }
            var hasOpticalContour = sprite != null;
            if (sprite != null && image.preserveAspect && image.type == Image.Type.Simple &&
                rect.width > 0f && rect.height > 0f && sprite.rect.height > 0f)
            {
                var aspect = sprite.rect.width / sprite.rect.height;
                if (aspect > rect.width / rect.height)
                {
                    var oldHeight = rect.height;
                    rect.height = rect.width / aspect;
                    rect.y += (oldHeight - rect.height) * image.rectTransform.pivot.y;
                }
                else
                {
                    var oldWidth = rect.width;
                    rect.width = rect.height * aspect;
                    rect.x += (oldWidth - rect.width) * image.rectTransform.pivot.x;
                }
            }
            var halfWidth = Mathf.Max(rect.width * 0.5f, 0.0001f);
            var halfHeight = Mathf.Max(rect.height * 0.5f, 0.0001f);
            var shortHalfSize = Mathf.Min(halfWidth, halfHeight);
            var ellipse = IsButton || _liquidEllipse;
            var capsule = IsCategory || _liquidCapsule;
            var radius = capsule || ellipse ? shortHalfSize : shortHalfSize * 0.12f;
            if (!capsule && !ellipse && sprite != null && image.type == Image.Type.Sliced)
            {
                var border = sprite.border;
                var borderRadius = Mathf.Min(Mathf.Max(border.x, border.z), Mathf.Max(border.y, border.w));
                var reference = image.canvas != null ? image.canvas.referencePixelsPerUnit : 100f;
                var pixelsPerUnit = sprite.pixelsPerUnit / reference * image.pixelsPerUnitMultiplier;
                if (borderRadius > 0f && pixelsPerUnit > 0f)
                {
                    radius = Mathf.Min(borderRadius / pixelsPerUnit, shortHalfSize);
                }
            }
            var worldToLocal = image.rectTransform.worldToLocalMatrix;
            var logicalRect = new Vector4(rect.center.x, rect.center.y, halfWidth, halfHeight);
            var shape = new Vector4(radius, ellipse ? 1f : 0f, _liquidClippedCircle ? 1f : 0f, hasOpticalContour ? 1f : 0f);
            var corners = new Vector4(radius, radius, radius, radius);
            var sourceRect = Vector4.zero;
            var sourceCorners = Vector4.zero;
            var opticalContour = hasOpticalContour ? CreateOpticalContour(image, sprite!) : default;
            if (hasOpticalContour)
            {
                sourceRect = opticalContour.Rect;
                sourceCorners = opticalContour.Corners;
            }
            if (sprite == null)
            {
                corners = new Vector4(Mathf.Clamp(material.GetFloat("_RadiusTL"), 0f, shortHalfSize),
                    Mathf.Clamp(material.GetFloat("_RadiusTR"), 0f, shortHalfSize),
                    Mathf.Clamp(material.GetFloat("_RadiusBR"), 0f, shortHalfSize),
                    Mathf.Clamp(material.GetFloat("_RadiusBL"), 0f, shortHalfSize));
            }
            var circleRadius = Mathf.Max(0f, _liquidCircleRadius) * rect.width;
            var circle = hasOpticalContour ? opticalContour.Circle : new Vector4(
                _liquidCircleCenter.x * rect.width, _liquidCircleCenter.y * rect.height,
                circleRadius, circleRadius * Mathf.Clamp(_liquidCircleAspect, 0.1f, 4f));
            var clipBlend = hasOpticalContour ? opticalContour.ClipSmoothness
                : Mathf.Clamp(_liquidClipSmoothness, 0f, 0.1f) * rect.width;
            var rootCanvas = image.canvas != null ? image.canvas.rootCanvas : null;
            var toCanvas = rootCanvas != null
                ? rootCanvas.transform.worldToLocalMatrix * image.rectTransform.localToWorldMatrix : Matrix4x4.identity;
            var scaleX = toCanvas.MultiplyVector(Vector3.right).magnitude;
            var scaleY = toCanvas.MultiplyVector(Vector3.up).magnitude;
            var unitsPerDesignUnit = Mathf.Min(CanvasUnitsPerDesignUnit / Mathf.Max(Mathf.Min(scaleX, scaleY), 0.0001f),
                shortHalfSize * 2f / 48f);
            // Small native and category controls use a body-relative cap; peripheral keys retain their authored reflection band.
            // The cap ends before the central ten percent for those wide controls. Peripheral displacement
            // adds a separate smooth body tail without stretching reflection, board or submonitor parameters.
            // Its thickness and normal come from the same cap, rather than an exterior strip.
            // Reflection width and softness remain continuous, with no one-pixel minimum.
            // Background filtering stays in the shared ordered capture targets.
            var bevelWidth = !IsButton && (ellipse || capsule) ? shortHalfSize * 0.9f
                : Mathf.Min(20f * unitsPerDesignUnit, shortHalfSize * 0.4f);
            // Apply an explicit per-surface multiplier after the established role policy.
            // A missing serialized value retains one, so unrelated controls keep their current optics.
            bevelWidth *= Mathf.Clamp(_liquidRefractionWidthScale, 0.25f, 2f);
            // A 1.2 thickness-to-width ratio retains an ordered inward lens without image folds.
            var optics = new Vector4(bevelWidth, bevelWidth * 1.2f, 1.5f * unitsPerDesignUnit, 0.875f * unitsPerDesignUnit);
            var artworkProtection = hasOpticalContour && _liquidOriginalArtworkLeft.x > 0f
                ? new Vector4((Mathf.Clamp01(_liquidOriginalArtworkLeft.x) - 0.5f) * opticalContour.Size.x,
                    Mathf.Clamp(_liquidOriginalArtworkLeft.y, 0f, 0.5f) * opticalContour.Size.x, 1f, 0f)
                : Vector4.zero;
            var changed = !_liquidGeometryInitialized || _liquidWorldToLocal != worldToLocal ||
                _liquidNativeContour != nativeContour ||
                _liquidRect != logicalRect || _liquidShape != shape || _liquidCorners != corners || _liquidOptics != optics ||
                _liquidSpriteRect != sourceRect || _liquidSpriteCorners != sourceCorners || _liquidCircle != circle ||
                _liquidClipBlend != clipBlend || _liquidArtworkProtection != artworkProtection;
            if (changed)
            {
                _liquidWorldToLocal = worldToLocal;
                _liquidNativeContour = nativeContour;
                _liquidRect = logicalRect;
                _liquidShape = shape;
                _liquidCircle = circle;
                _liquidClipBlend = clipBlend;
                _liquidCorners = corners;
                _liquidOptics = optics;
                _liquidSpriteRect = sourceRect;
                _liquidSpriteCorners = sourceCorners;
                _liquidArtworkProtection = artworkProtection;
                _liquidGeometryInitialized = true;
                var atlas = UiGlassOpticsAtlas.Load();
                _bakedProfileTexture = atlas != null ? atlas.ProfileTexture : null;
                _bakedContourTexture = atlas != null && hasOpticalContour &&
                    atlas.TryResolve(in opticalContour, material.GetVector("_LiquidGlassUvToLocal"),
                        out _bakedContourUv, out _bakedContourClamp, out _bakedContourDecode) ? atlas.DistanceTexture : null;
            }
            if (changed || tintChanged)
            {
                SetLiquidGeometry(material);
            }
            // MaskableGraphic can own a separate stencil clone. Update that actual draw material
            // as well, otherwise moving categories would retain an earlier contour under a Mask.
            var rendered = image.canvasRenderer.materialCount > 0 ? image.canvasRenderer.GetMaterial(0) : null;
            if (rendered != null && rendered != material && rendered.shader == material.shader &&
                (changed || tintChanged || rendered != _liquidRenderMaterial))
            {
                SetLiquidGeometry(rendered);
            }
            _liquidRenderMaterial = rendered;
        }

        /// <summary>Describes the authored source contour for both offline baking and runtime lookup.</summary>
        /// <param name="contour">The original Sprite-space shape, independent of Canvas size and nine-slice stretching.</param>
        /// <returns>Whether the opted-in Image has an authored Sprite contour rather than a procedural shape.</returns>
        internal bool TryGetOpticalContour(out UiGlassOpticsAtlas.Contour contour)
        {
            contour = default;
            var image = _image != null ? _image : GetComponent<QualityUiImage>();
            if (image == null || image.overrideSprite == null)
            {
                return false;
            }
            var sprite = image.overrideSprite;
            if (UiAtlasMeshGenerator.TryGetLegacy(sprite, out var legacy, out _))
            {
                sprite = legacy;
            }
            contour = CreateOpticalContour(image, sprite);
            return true;
        }

        /// <summary>Builds one source-space descriptor without repeating Sprite lookup or native name allocation.</summary>
        /// <param name="image">The control providing native image geometry.</param>
        /// <param name="sprite">The already resolved original logical Sprite.</param>
        /// <returns>The contour consumed identically by the baker and runtime material.</returns>
        private UiGlassOpticsAtlas.Contour CreateOpticalContour(QualityUiImage image, Sprite sprite)
        {
            if (_opticalSprite != sprite)
            {
                _opticalSprite = sprite;
                _opticalSpriteName = sprite.name;
            }
            var size = sprite.rect.size;
            var left = Mathf.Clamp(_liquidBodyInsets.x, 0f, 0.49f);
            var bottom = Mathf.Clamp(_liquidBodyInsets.y, 0f, 0.49f);
            var right = Mathf.Clamp(_liquidBodyInsets.z, 0f, 0.49f);
            var top = Mathf.Clamp(_liquidBodyInsets.w, 0f, 0.49f);
            var bodySize = new Vector2(size.x * (1f - left - right), size.y * (1f - bottom - top));
            var sourceRect = new Vector4(size.x * (left - right) * 0.5f, size.y * (bottom - top) * 0.5f,
                bodySize.x * 0.5f, bodySize.y * 0.5f);
            var ellipse = IsButton || _liquidEllipse;
            var capsule = IsCategory || _liquidCapsule;
            var radius = capsule || ellipse ? Mathf.Min(bodySize.x, bodySize.y) * 0.5f
                : Mathf.Min(bodySize.x, bodySize.y) * 0.12f;
            if (_liquidCornerRadius > 0f)
            {
                radius = Mathf.Min(size.x, size.y) * _liquidCornerRadius;
            }
            else if (!capsule && !ellipse && image.type == Image.Type.Sliced)
            {
                var border = sprite.border;
                radius = Mathf.Min(Mathf.Max(border.x, border.z), Mathf.Max(border.y, border.w));
            }
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(bodySize.x, bodySize.y) * 0.5f);
            var center = new Vector2(_liquidCircleCenter.x * size.x - sourceRect.x,
                _liquidCircleCenter.y * size.y - sourceRect.y);
            var circleRadius = Mathf.Max(0f, _liquidCircleRadius) * size.x;
            var circle = new Vector4(center.x, center.y, circleRadius,
                circleRadius * Mathf.Clamp(_liquidCircleAspect, 0.1f, 4f));
            return new UiGlassOpticsAtlas.Contour(_opticalSpriteName, size, sourceRect,
                new Vector4(radius, radius, radius, radius),
                new Vector2(ellipse ? 1f : 0f, _liquidClippedCircle ? 1f : 0f), circle,
                Mathf.Clamp(_liquidClipSmoothness, 0f, 0.1f) * size.x,
                _liquidArtworkContour ? Mathf.Clamp01(_fillAlpha) * 0.5f : 0f);
        }

        /// <summary>Refreshes the explicit source's actual texture, fill UV and working-space color without rebuilding materials.</summary>
        /// <returns>True when an owned or stencil material needs an updated source binding.</returns>
        private bool UpdateLiquidTintSource()
        {
            Texture? texture = null;
            var fillUv = Vector4.zero;
            var color = new Vector4(1f, 1f, 1f, 0f);
            var source = _liquidTintSource;
            if (source != null && source != this && source.isActiveAndEnabled && _liquidSourceTintStrength > 0f)
            {
                var sourceImage = source._image;
                if (sourceImage == null)
                {
                    sourceImage = source.GetComponent<QualityUiImage>();
                    source._image = sourceImage;
                }
                if (sourceImage != null && sourceImage.isActiveAndEnabled)
                {
                    // mainTexture follows the outlined atlas; the original Sprite retains its paired UV layout.
                    texture = sourceImage.mainTexture;
                    var sprite = sourceImage.overrideSprite;
                    var bounds = sprite != null ? UnityEngine.Sprites.DataUtility.GetOuterUV(sprite) : new Vector4(0f, 0f, 1f, 1f);
                    var sourceMaterial = source._material != null ? source._material : sourceImage.SourceMaterial;
                    var scale = sourceMaterial.GetTextureScale("_MainTex");
                    var offset = sourceMaterial.GetTextureOffset("_MainTex");
                    fillUv = new Vector4(
                        Mathf.Lerp(bounds.x, bounds.z, Mathf.Clamp01(source._fillSamplePosition.x)) * scale.x + offset.x,
                        Mathf.Lerp(bounds.y, bounds.w, Mathf.Clamp01(source._fillSamplePosition.y)) * scale.y + offset.y, 0f, 0f);
                    // Canvas ultimately supplies linear vertex colors in a Linear project, whether
                    // conversion happens in Canvas or its UI shader. Vector uniforms avoid another conversion.
                    var graphic = sourceImage.color;
                    if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                    {
                        graphic = graphic.linear;
                    }
                    var materialTint = sourceMaterial.HasProperty("_Color") ? sourceMaterial.GetVector("_Color") : Vector4.one;
                    color = new Vector4(graphic.r * materialTint.x, graphic.g * materialTint.y,
                        graphic.b * materialTint.z, Mathf.Clamp01(_liquidSourceTintStrength));
                }
            }
            var changed = !_liquidSourceTintInitialized || _liquidSourceTexture != texture ||
                _liquidSourceUv != fillUv || _liquidSourceColor != color;
            _liquidSourceTexture = texture;
            _liquidSourceUv = fillUv;
            _liquidSourceColor = color;
            _liquidSourceTintInitialized = true;
            return changed;
        }

        /// <summary>Writes the same contour and source-tint binding to an owned glass material or its Canvas stencil variant.</summary>
        /// <param name="material">The actual glass draw material receiving the current contour.</param>
        private void SetLiquidGeometry(Material material)
        {
            SetStyleKeyword(material);
            material.SetVector("_LiquidArtworkProtection", _liquidArtworkProtection);
            var contourTexture = _bakedContourTexture != null ? _bakedContourTexture : Texture2D.blackTexture;
            material.SetTexture("_LiquidContourTex", contourTexture);
            material.SetVector("_LiquidContourUV", _bakedContourUv);
            material.SetVector("_LiquidContourClamp", _bakedContourClamp);
            material.SetVector("_LiquidContourDecode", new Vector4(_bakedContourDecode.x, _bakedContourDecode.y, 0f, 0f));
            material.SetFloat("_LiquidContourReady", _bakedContourTexture != null ? 1f : 0f);
            var profileReady = _bakedProfileTexture != null &&
                UiGlassOpticsAtlas.SupportsContourFiltering &&
                Mathf.Abs(_liquidOptics.y / Mathf.Max(_liquidOptics.x, 0.0001f) - 1.2f) < 0.0001f;
            // Native Rounded Rect already evaluates its exact distance for alpha coverage. Reuse that
            // result instead of requiring a Sprite atlas entry or evaluating a substitute contour.
            var nativeContourReady = profileReady && _liquidNativeContour;
            material.SetFloat("_LiquidNativeContourReady", nativeContourReady ? 1f : 0f);
            var refractionReady = profileReady && (_bakedContourTexture != null || nativeContourReady);
            material.SetFloat("_LiquidRefractionReady", refractionReady ? 1f : 0f);
            material.SetTexture("_LiquidProfileTex", _bakedProfileTexture != null ? _bakedProfileTexture : Texture2D.blackTexture);
            material.SetFloat("_LiquidProfileReady", _bakedProfileTexture != null ? 1f : 0f);
            if (_bakedProfileTexture != null)
            {
                var width = _bakedProfileTexture.width;
                material.SetVector("_LiquidProfileUV", new Vector4((width - 1f) / width, 0.5f / width, 0f, 0f));
            }
            material.SetTexture("_LiquidTintSourceTex", _liquidSourceTexture != null ? _liquidSourceTexture : Texture2D.whiteTexture);
            material.SetVector("_LiquidTintSourceUV", _liquidSourceUv);
            material.SetVector("_LiquidTintSourceColor", _liquidSourceColor);
            material.SetMatrix("_GlassWorldToLocal", _liquidWorldToLocal);
            material.SetVector("_LiquidGlassRect", _liquidRect);
            material.SetVector("_LiquidGlassShape", _liquidShape);
            material.SetVector("_LiquidGlassCircle", _liquidCircle);
            material.SetFloat("_LiquidGlassClipSmoothness", _liquidClipBlend);
            material.SetVector("_LiquidGlassCorners", _liquidCorners);
            material.SetVector("_LiquidGlassOptics", _liquidOptics);
            // Keep the authored reflection band while extending only peripheral displacement toward the center.
            material.SetFloat("_LiquidRefractionTail", IsButton ? 0.22f : 0f);
            material.SetVector("_LiquidGlassSpriteRect", _liquidSpriteRect);
            material.SetVector("_LiquidGlassSpriteCorners", _liquidSpriteCorners);
        }

        /// <summary>Updates both owned and already installed Canvas stencil variants when the effective style changes.</summary>
        private void SynchronizeStyleKeywords()
        {
            var material = _material;
            if (material == null)
            {
                return;
            }
            SetStyleKeyword(material);
            var image = _image;
            if (image == null)
            {
                return;
            }
            var renderer = image.canvasRenderer;
            var rendered = renderer.materialCount > 0 ? renderer.GetMaterial(0) : null;
            if (rendered != null && rendered != material && rendered.shader == material.shader)
            {
                SetStyleKeyword(rendered);
            }
        }

        /// <summary>Synchronizes the effective style without rebuilding materials or changing draw order.</summary>
        /// <param name="material">The owned surface or its actual Canvas stencil clone.</param>
        private static void SetStyleKeyword(Material material)
        {
            var liquid = UiPvBackdropBlur.IsLiquidGlass;
            if (material.IsKeywordEnabled(LiquidStyleKeyword) == liquid)
            {
                return;
            }
            if (liquid)
            {
                material.EnableKeyword(LiquidStyleKeyword);
            }
            else
            {
                material.DisableKeyword(LiquidStyleKeyword);
            }
        }

        /// <summary>Binds authored native foregrounds and explicit liquid-only layers once per enabled owner lifetime.</summary>
        private void BindForeground()
        {
            UnbindForeground();
            if (SourceRole == UiBackdropSourceRole.Peripheral || IsCategory)
            {
                BindRoot(_buttonForegroundRoot);
            }
            BindRoot(_liquidForegroundRoot, LiquidForegroundQueue, true);
        }

        /// <summary>Applies one queue policy to the existing Graphics inside an explicitly authored hierarchy.</summary>
        /// <param name="root">The authored hierarchy, or null when this panel has no such layer.</param>
        /// <param name="renderQueueOverride">The explicit queue, or minus one for the owner's native foreground stage.</param>
        /// <param name="liquidOnly">Whether Gaussian and disabled effects retain the original materials.</param>
        private void BindRoot(Transform? root, int renderQueueOverride = -1, bool liquidOnly = false)
        {
            if (root == null)
            {
                return;
            }
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                BindGraphic(graphic, renderQueueOverride, liquidOnly);
            }
        }

        /// <summary>Reuses one queue-only modifier without replacing the Graphic, material source or layout.</summary>
        /// <param name="graphic">The existing Graphic, excluding this or another backdrop body.</param>
        /// <param name="renderQueueOverride">The explicit queue, or minus one for the owner's native foreground stage.</param>
        /// <param name="liquidOnly">Whether the queue policy applies only to liquid glass.</param>
        private void BindGraphic(Graphic? graphic, int renderQueueOverride = -1, bool liquidOnly = false)
        {
            if (graphic == null || graphic == _image || graphic.GetComponent<UiBackdropPanel>() != null)
            {
                return;
            }
            var foreground = graphic.GetComponent<UiBackdropForeground>();
            if (foreground == null)
            {
                foreground = graphic.gameObject.AddComponent<UiBackdropForeground>();
                foreground.hideFlags = HideFlags.DontSave;
            }
            foreground.Bind(this, renderQueueOverride, liquidOnly);
            if (!_foreground.Contains(foreground))
            {
                _foreground.Add(foreground);
            }
        }

        /// <summary>Returns borrowed foreground components to their original materials without destroying reusable helpers.</summary>
        private void UnbindForeground()
        {
            foreach (var foreground in _foreground)
            {
                if (foreground != null && foreground.IsBoundTo(this))
                {
                    foreground.Bind(null);
                }
            }
            _foreground.Clear();
        }

        /// <summary>Preserves original Sprite coordinates for liquid contours and optional Gaussian lighting.</summary>
        /// <param name="material">The panel-owned variant receiving only its shape parameters.</param>
        /// <param name="sprite">The authored slice whose pre-expansion logical geometry must be retained.</param>
        /// <param name="supported">Whether the actual Image uses a simple, non-SpriteMesh affine mapping.</param>
        /// <remarks>The approximation never replaces sprite coverage, decorations, shadow alpha or UGUI masks.</remarks>
        private static void ConfigureShape(Material material, Sprite sprite, bool supported)
        {
            material.SetVector("_GlassUvToLocal", Vector4.zero);
            material.SetVector("_GlassShape", Vector4.zero);
            material.SetVector("_LiquidGlassUvToLocal", Vector4.zero);
            if (UiAtlasMeshGenerator.TryGetLegacy(sprite, out var legacy, out var originalOuterUv))
            {
                sprite = legacy;
            }
            else
            {
                originalOuterUv = UnityEngine.Sprites.DataUtility.GetOuterUV(sprite);
            }
            var size = sprite.rect.size;
            var padding = UnityEngine.Sprites.DataUtility.GetPadding(sprite);
            var contentWidth = size.x - padding.x - padding.z;
            var contentHeight = size.y - padding.y - padding.w;
            if (size.x <= 0f || size.y <= 0f || contentWidth <= 0f || contentHeight <= 0f)
            {
                return;
            }
            // The captured Tight range omits padding; restore the full original logical rectangle first.
            var pixelUv = new Vector2((originalOuterUv.z - originalOuterUv.x) / contentWidth,
                (originalOuterUv.w - originalOuterUv.y) / contentHeight);
            if (!float.IsFinite(pixelUv.x) || !float.IsFinite(pixelUv.y) || pixelUv.x <= 0f || pixelUv.y <= 0f)
            {
                return;
            }
            var centerUv = new Vector2(originalOuterUv.x - padding.x * pixelUv.x + size.x * pixelUv.x * 0.5f,
                originalOuterUv.y - padding.y * pixelUv.y + size.y * pixelUv.y * 0.5f);
            var mapping = new Vector4(1f / pixelUv.x, 1f / pixelUv.y,
                -centerUv.x / pixelUv.x, -centerUv.y / pixelUv.y);
            material.SetVector("_LiquidGlassUvToLocal", mapping);
            if (!supported)
            {
                return;
            }
            material.SetVector("_GlassUvToLocal", mapping);
            material.SetVector("_GlassShape", new Vector4(size.x * 0.5f, size.y * 0.5f,
                Mathf.Min(size.x, size.y) * 0.045f, 1f));
        }

        /// <summary>Releases the cached variant when pooling, scene changes or source-material changes invalidate it.</summary>
        private void ReleaseMaterial()
        {
            if (_material != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_material);
                }
                else
                {
                    DestroyImmediate(_material);
                }
            }
            _material = null;
            _themedMaterial = null;
            _baseTexture = null;
            _sprite = null;
            _supportsShape = false;
            _materialIsRoundedRect = false;
            _materialIsButton = false;
            _materialIsCategory = false;
            _materialSourceRole = default;
            _materialUsesStrongBlur = false;
            _liquidGeometryInitialized = false;
            _liquidNativeContour = false;
            _liquidRenderMaterial = null;
            _liquidSourceTintInitialized = false;
            _liquidSourceTexture = null;
            _bakedContourTexture = null;
            _bakedContourDecode = default;
            _bakedProfileTexture = null;
            _opticalSprite = null;
            _opticalSpriteName = string.Empty;
        }

        /// <summary>Releases the explicit background clone when its owner or themed source changes.</summary>
        private void ReleaseBackgroundMaterial()
        {
            if (_backgroundMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_backgroundMaterial);
                }
                else
                {
                    DestroyImmediate(_backgroundMaterial);
                }
            }
            _backgroundMaterial = null;
            _backgroundSource = null;
        }
    }
}
