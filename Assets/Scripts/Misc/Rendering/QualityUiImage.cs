using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
#nullable enable

namespace MajdataPlay.Rendering
{
    /// <summary>Selects smooth UI materials and paired artwork without replacing serialized sprites or button states.</summary>
    [AddComponentMenu("UI/Maj Quality Image")]
    public sealed class QualityUiImage : Image
    {
        /// <summary>Includes this cover after boards and before the category snapshot while filtering is enabled.</summary>
        [SerializeField]
        [FormerlySerializedAs("CaptureBeforeBackdrop")]
        private bool _captureBeforeBackdrop;

        /// <summary>Draws explicit background artwork before camera extensions while preserving the active UI shader.</summary>
        [SerializeField]
        [FormerlySerializedAs("DrawBeforeCameraExtension")]
        private bool _drawBeforeCameraExtension;

        /// <summary>Selects an optional authored material only while liquid-glass rendering is enabled.</summary>
        [SerializeField]
        [FormerlySerializedAs("LiquidGlassMaterial")]
        private Material? _liquidGlassMaterial;

        /// <summary>References an existing concentric circular inlay whose radius and relative opacity define the liquid clearcoat.</summary>
        [SerializeField]
        [FormerlySerializedAs("GlassInlaySource")]
        private Image? _glassInlaySource;

        /// <summary>Tracks registration independently of Canvas rebuild and pooling callbacks.</summary>
        private bool _registered;

        /// <summary>Caches whether the authored liquid material requests a frame snapshot and owns a live capture registration.</summary>
        private bool _liquidFrameCaptureRegistered;

        /// <summary>Retains the optional large-panel effect without per-rebuild component searches.</summary>
        private UiBackdropPanel? _backdropPanel;

        /// <summary>Retains the board owner that includes this blue ring in the background capture.</summary>
        private UiBackdropPanel? _backdropBackgroundPanel;

        /// <summary>Owns this Image's queue-only background variant without editing its source material.</summary>
        private Material? _captureMaterial;

        /// <summary>Identifies the themed source whose shader and properties belong to the background variant.</summary>
        private Material? _captureSource;

        /// <summary>Gets the authored material before the active UI mode substitutes a runtime variant.</summary>
        internal Material SourceMaterial => base.material;

        /// <summary>Gets the cached main-display circle used by this explicit liquid-frame consumer.</summary>
        internal RectTransform? MainDisplayRect { get; private set; }

        /// <summary>Gets the camera rendering this Image's root Canvas, excluding screen-space overlays.</summary>
        internal Camera? RenderCamera
        {
            get
            {
                var imageCanvas = canvas;
                var root = imageCanvas != null ? imageCanvas.rootCanvas : null;
                return root != null && root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
            }
        }

        /// <summary>Gets whether this registered frame can consume the current liquid peripheral snapshot.</summary>
        internal bool IsLiquidFrameCaptureVisible
        {
            get
            {
                if (!_liquidFrameCaptureRegistered || !UiPvBackdropBlur.Enabled || !UiPvBackdropBlur.IsLiquidGlass ||
                    !isActiveAndEnabled || color.a <= 0f)
                {
                    return false;
                }
                var imageCanvas = canvas;
                var renderer = canvasRenderer;
                return imageCanvas != null && imageCanvas.isActiveAndEnabled && !renderer.cull &&
                    renderer.GetInheritedAlpha() > 0f;
            }
        }

        /// <summary>Gets the matching outlined texture when the active UI style requests it.</summary>
        /// <remarks>Both atlases retain identical expanded slice coordinates; logical layout is preserved separately.</remarks>
        public override Texture mainTexture
        {
            get
            {
                if (Application.isPlaying && UiOutlineTheme.TryGetOutlinedSprite(this, out var outlined))
                {
                    return outlined.texture;
                }
                return base.mainTexture;
            }
        }

        /// <summary>Gets the optional liquid material or themed artwork while preserving capture and panel policies.</summary>
        /// <remarks>The setter retains the original Graphic contract and never serializes a generated variant.</remarks>
        public override Material material
        {
            get
            {
                var original = SourceMaterial;
                if (Application.isPlaying)
                {
                    var themed = UiPvBackdropBlur.Enabled && UiPvBackdropBlur.IsLiquidGlass && _liquidGlassMaterial != null
                        ? _liquidGlassMaterial : UiOutlineTheme.GetMaterial(original);
                    if (_backdropBackgroundPanel != null)
                    {
                        var background = _backdropBackgroundPanel.ResolveBackgroundMaterial(themed);
                        if (background != themed)
                        {
                            return background;
                        }
                    }
                    var resolved = _backdropPanel != null ? _backdropPanel.ResolveMaterial(themed) : themed;
                    return ResolveOrderedMaterial(resolved);
                }
                return original;
            }
            set
            {
                if (SourceMaterial != value)
                {
                    ReleaseCaptureMaterial();
                }
                base.material = value;
            }
        }

        /// <summary>Gets the original logical width independently of shadow padding.</summary>
        public override float preferredWidth => GetLogicalPreferredSize(0);

        /// <summary>Gets the original logical height independently of shadow padding.</summary>
        public override float preferredHeight => GetLogicalPreferredSize(1);

        /// <summary>Preserves the original native content size when atlas slices gain shadow margins.</summary>
        public override void SetNativeSize()
        {
            if (!UiAtlasMeshGenerator.TryGetLegacy(overrideSprite, out var legacy, out _))
            {
                base.SetNativeSize();
                return;
            }
            var imageCanvas = canvas;
            var reference = imageCanvas != null ? imageCanvas.referencePixelsPerUnit : 100f;
            var logicalPixelsPerUnit = legacy.pixelsPerUnit / reference;
            rectTransform.anchorMax = rectTransform.anchorMin;
            rectTransform.sizeDelta = legacy.rect.size / logicalPixelsPerUnit;
            SetAllDirty();
        }

        /// <summary>Generates original UGUI content geometry and adds only the outlined sampling margins.</summary>
        /// <param name="target">The Canvas mesh receiving the compensated geometry.</param>
        protected override void OnPopulateMesh(VertexHelper target)
        {
            if (!UiAtlasMeshGenerator.TryGetLegacy(overrideSprite, out var legacy, out var outerUV))
            {
                base.OnPopulateMesh(target);
            }
            else
            {
                var expanded = Application.isPlaying && UiOutlineTheme.TryGetOutlinedSprite(this, out _);
                UiAtlasMeshGenerator.Populate(this, legacy, outerUV, expanded, target);
            }
            ApplyGlassInlayData(target);
        }

        /// <summary>Stores the existing concentric inlay's normalized radius and relative opacity in unused UV0 components.</summary>
        /// <param name="target">The ordinary Image mesh whose positions, colors and texture coordinates remain unchanged.</param>
        /// <remarks>The authored frame and source share a center, scale and parent CanvasGroup; shared group opacity is applied by UGUI once.</remarks>
        private void ApplyGlassInlayData(VertexHelper target)
        {
            var source = _glassInlaySource;
            if (source == null || !Application.isPlaying || !UiPvBackdropBlur.Enabled || !UiPvBackdropBlur.IsLiquidGlass)
            {
                return;
            }
            var frameWidth = rectTransform.rect.width;
            var radius = frameWidth > 0f ? 0.5f * source.rectTransform.rect.width / frameWidth : 0f;
            var opacity = source.isActiveAndEnabled ? Mathf.Clamp01(source.color.a) : 0f;
            var vertex = default(UIVertex);
            for (var index = 0; index < target.currentVertCount; index++)
            {
                target.PopulateUIVertex(ref vertex, index);
                var uv = vertex.uv0;
                uv.z = radius;
                uv.w = opacity;
                vertex.uv0 = uv;
                target.SetUIVertex(vertex, index);
            }
        }

        /// <summary>Gets native layout dimensions without counting the expanded artwork margins.</summary>
        /// <param name="axis">Zero for width, one for height.</param>
        /// <returns>The original Image preferred size for this axis.</returns>
        private float GetLogicalPreferredSize(int axis)
        {
            if (!UiAtlasMeshGenerator.TryGetLegacy(overrideSprite, out var legacy, out _))
            {
                return axis == 0 ? base.preferredWidth : base.preferredHeight;
            }
            var imageCanvas = canvas;
            var reference = imageCanvas != null ? imageCanvas.referencePixelsPerUnit : 100f;
            var logicalPixelsPerUnit = legacy.pixelsPerUnit / reference;
            var size = type == Type.Sliced || type == Type.Tiled
                ? UnityEngine.Sprites.DataUtility.GetMinSize(legacy) : legacy.rect.size;
            return size[axis] / logicalPixelsPerUnit;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (Application.isPlaying)
            {
                MainDisplayUiClip.EnsureFor(this);
                if (!_registered)
                {
                    UiOutlineTheme.Register(this);
                    _registered = true;
                }
            }
            RefreshLiquidFrameCaptureRegistration();
        }

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            if (Application.isPlaying && isActiveAndEnabled)
            {
                MainDisplayUiClip.EnsureFor(this);
                if (_liquidFrameCaptureRegistered)
                {
                    CacheMainDisplayRect();
                }
            }
        }

        protected override void OnDisable()
        {
            UnregisterLiquidFrameCapture();
            if (_registered)
            {
                UiOutlineTheme.Unregister(this);
                _registered = false;
            }
            ReleaseCaptureMaterial();
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            UnregisterLiquidFrameCapture();
            ReleaseCaptureMaterial();
            base.OnDestroy();
        }

        /// <summary>Rebuilds only material selection when the optional background capture setting changes.</summary>
        internal void RefreshMaterialSelection()
        {
            RefreshLiquidFrameCaptureRegistration();
            if (!isActiveAndEnabled || (!_drawBeforeCameraExtension && (!_captureBeforeBackdrop || !UiPvBackdropBlur.Enabled)))
            {
                ReleaseCaptureMaterial();
            }
            SetMaterialDirty();
            if (_glassInlaySource != null)
            {
                SetVerticesDirty();
            }
        }

        /// <summary>Checks the authored frame flag only when lifecycle or material selection changes.</summary>
        private void RefreshLiquidFrameCaptureRegistration()
        {
            var eligible = Application.isPlaying && isActiveAndEnabled && _liquidGlassMaterial != null &&
                _liquidGlassMaterial.HasProperty("_Frame") && _liquidGlassMaterial.GetFloat("_Frame") > 0.5f;
            if (!eligible)
            {
                UnregisterLiquidFrameCapture();
                return;
            }
            CacheMainDisplayRect();
            if (!_liquidFrameCaptureRegistered)
            {
                _liquidFrameCaptureRegistered = true;
                UiPvBackdropBlur.Register(this);
            }
        }

        /// <summary>Releases only this Image's explicit frame registration without changing its authored material.</summary>
        private void UnregisterLiquidFrameCapture()
        {
            if (_liquidFrameCaptureRegistered)
            {
                _liquidFrameCaptureRegistered = false;
                UiPvBackdropBlur.Unregister(this);
            }
            MainDisplayRect = null;
        }

        /// <summary>Finds the existing main-display ancestor after enable or reparenting, without changing the UI hierarchy.</summary>
        private void CacheMainDisplayRect()
        {
            MainDisplayRect = null;
            for (var ancestor = transform.parent; ancestor != null; ancestor = ancestor.parent)
            {
                if (ancestor.name == "Main_Display")
                {
                    MainDisplayRect = ancestor as RectTransform;
                    break;
                }
            }
        }

        /// <summary>Orders explicit background artwork or the song-cover prefix without changing the active shader.</summary>
        /// <param name="source">The material selected by the normal UI styling and panel policy.</param>
        /// <returns>The component-owned queue variant while its ordering policy is active; otherwise the unchanged source.</returns>
        private Material ResolveOrderedMaterial(Material source)
        {
            if (!isActiveAndEnabled || (!_drawBeforeCameraExtension && (!_captureBeforeBackdrop || !UiPvBackdropBlur.Enabled)))
            {
                ReleaseCaptureMaterial();
                return source;
            }
            if (_captureMaterial == null || _captureSource != source || _captureMaterial.shader != source.shader)
            {
                ReleaseCaptureMaterial();
                _captureMaterial = new Material(source)
                {
                    name = source.name + " (Ordered UI)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _captureSource = source;
            }
            else
            {
                _captureMaterial.CopyPropertiesFromMaterial(source);
            }
            _captureMaterial.renderQueue = _drawBeforeCameraExtension
                ? (int)UnityEngine.Rendering.RenderQueue.GeometryLast : MajUiBackdropBridge.CategoryBackgroundQueue;
            return _captureMaterial;
        }

        /// <summary>Releases only the background variant owned by this Image.</summary>
        private void ReleaseCaptureMaterial()
        {
            if (_captureMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_captureMaterial);
                }
                else
                {
                    DestroyImmediate(_captureMaterial);
                }
            }
            _captureMaterial = null;
            _captureSource = null;
        }

        /// <summary>Refreshes artwork and shadow margins while preserving logical layout, tint and button state.</summary>
        internal void RefreshOutline()
        {
            SetMaterialDirty();
            SetVerticesDirty();
        }

        /// <summary>Connects or removes a panel-owned material effect without changing the authored Image material.</summary>
        /// <param name="panel">The active panel component, or null when its effect is disabled.</param>
        internal void SetBackdropPanel(UiBackdropPanel? panel)
        {
            _backdropPanel = panel;
            SetMaterialDirty();
        }

        /// <summary>Connects or removes the explicit owner of this Image's board-stage blue-ring queue.</summary>
        /// <param name="panel">The active background-capture owner, or null when its effect is disabled.</param>
        internal void SetBackdropBackground(UiBackdropPanel? panel)
        {
            _backdropBackgroundPanel = panel;
            SetMaterialDirty();
        }
    }
}
