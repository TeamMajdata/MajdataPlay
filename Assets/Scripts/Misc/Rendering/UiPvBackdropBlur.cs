using System;
using System.Collections.Generic;
using MajdataPlay.Diagnostics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
#nullable enable

namespace MajdataPlay.Rendering
{
    /// <summary>Retains three independently captured menu backgrounds and reusable filtering targets.</summary>
    /// <remarks>The historical name is retained; each source is an ordered MRP scene prefix, never a decoder texture.</remarks>
    internal static class UiPvBackdropBlur
    {
        /// <summary>Loads the included filter and panel templates without shader-name lookup.</summary>
        private const string ResourcesPath = "Rendering/UI/OutlineTheme";
        /// <summary>Sets the Gaussian standard deviation in quarter-resolution texels.</summary>
        private const double GaussianSigma = 4.0;
        /// <summary>Bounds the Gaussian support to eight quarter-resolution texels on either side.</summary>
        private const int GaussianRadius = 8;
        /// <summary>Names the scene-prefix board source before any foreground board or song cover.</summary>
        private const string BoardSampleName = "MRP.UI.BoardBackgroundSource";
        /// <summary>Names the source after board and song-cover drawing, before categories.</summary>
        private const string CategorySampleName = "MRP.UI.CategoryBackgroundSource";
        /// <summary>Names the source after ordinary foreground drawing, before peripheral buttons.</summary>
        private const string ButtonSampleName = "MRP.UI.ButtonBackgroundSource";
        /// <summary>Publishes the independently retained category result.</summary>
        private static readonly int BlurTextureId = Shader.PropertyToID("_MajPvBlurTexture");
        /// <summary>Publishes the category result's actual texel size.</summary>
        private static readonly int BlurTexelSizeId = Shader.PropertyToID("_MajPvBlurTexture_TexelSize");
        /// <summary>Publishes category readiness only after its current-frame capture.</summary>
        private static readonly int ReadyId = Shader.PropertyToID("_MajPvBlurReady");
        /// <summary>Publishes the scene-prefix board result.</summary>
        private static readonly int PanelBlurTextureId = Shader.PropertyToID("_MajUiPanelBlurTexture");
        /// <summary>Publishes the board result's actual texel size.</summary>
        private static readonly int PanelBlurTexelSizeId = Shader.PropertyToID("_MajUiPanelBlurTexture_TexelSize");
        /// <summary>Publishes board readiness independently of categories and peripheral buttons.</summary>
        private static readonly int PanelReadyId = Shader.PropertyToID("_MajUiPanelBlurReady");
        /// <summary>Publishes the independently retained peripheral-button result.</summary>
        private static readonly int ButtonBlurTextureId = Shader.PropertyToID("_MajUiButtonBlurTexture");
        /// <summary>Publishes the peripheral-button result's actual texel size.</summary>
        private static readonly int ButtonBlurTexelSizeId = Shader.PropertyToID("_MajUiButtonBlurTexture_TexelSize");
        /// <summary>Publishes peripheral readiness only after its current-frame capture.</summary>
        private static readonly int ButtonReadyId = Shader.PropertyToID("_MajUiButtonBlurReady");
        /// <summary>Maps capture UVs to the main circle's normalized horizontal coordinate; W enables the mapping.</summary>
        private static readonly int ButtonClipUvXId = Shader.PropertyToID("_MajUiButtonClipUvX");
        /// <summary>Maps capture UVs to the main circle's normalized vertical coordinate.</summary>
        private static readonly int ButtonClipUvYId = Shader.PropertyToID("_MajUiButtonClipUvY");
        /// <summary>Publishes the projected circle center and an inset normalized radius for valid peripheral samples.</summary>
        private static readonly int ButtonClipSampleBoundsId = Shader.PropertyToID("_MajUiButtonClipSampleBounds");
        /// <summary>Maps category capture UVs to the main circle's normalized horizontal coordinate.</summary>
        private static readonly int CategoryClipUvXId = Shader.PropertyToID("_MajUiCategoryClipUvX");
        /// <summary>Maps category capture UVs to the main circle's normalized vertical coordinate.</summary>
        private static readonly int CategoryClipUvYId = Shader.PropertyToID("_MajUiCategoryClipUvY");
        /// <summary>Publishes the projected circle center and an inset normalized radius for valid category samples.</summary>
        private static readonly int CategoryClipSampleBoundsId = Shader.PropertyToID("_MajUiCategoryClipSampleBounds");
        /// <summary>Supplies horizontal circle coverage for the source currently being copied.</summary>
        private static readonly int CaptureClipUvXId = Shader.PropertyToID("_MajUiCaptureClipUvX");
        /// <summary>Supplies vertical circle coverage for the source currently being copied.</summary>
        private static readonly int CaptureClipUvYId = Shader.PropertyToID("_MajUiCaptureClipUvY");
        /// <summary>Defines the low-pass footprint from the actual downsample destination.</summary>
        private static readonly int DownsampleStepId = Shader.PropertyToID("_MajPvDownsampleStep");
        /// <summary>Requests manual bilinear sampling only for a borrowed Point-filtered source on fallback APIs.</summary>
        private static readonly int SourcePointFilterId = Shader.PropertyToID("_MajUiSourceNeedsManualLinear");
        /// <summary>Maps one quarter-resolution source texel along each Gaussian axis.</summary>
        private static readonly int PanelBlurStepId = Shader.PropertyToID("_MajUiPanelBlurStep");
        /// <summary>Supplies the normalized center weight shared by both Gaussian axes.</summary>
        private static readonly int GaussianCenterWeightId = Shader.PropertyToID("_MajUiGaussianCenterWeight");
        /// <summary>Supplies the four adjacent-texel pairs shared by both Gaussian axes.</summary>
        private static readonly int GaussianPairsId = Shader.PropertyToID("_MajUiGaussianPairs");
        /// <summary>Retains registered consumers without per-frame snapshots or scene searches.</summary>
        private static readonly List<UiBackdropPanel> s_panels = new(40);
        /// <summary>Retains explicit static-material frame consumers independently of the peripheral-button panels.</summary>
        private static readonly List<QualityUiImage> s_liquidFrameConsumers = new(16);
        /// <summary>Owns the allocation-free three-stage MRP adapter.</summary>
        private static readonly Capture s_capture = new();
        /// <summary>Retains the shared Gaussian weight/offset pairs in XY without per-frame allocation.</summary>
        private static readonly Vector4[] s_gaussianKernelPairs = new Vector4[GaussianRadius / 2];
        /// <summary>Builds the resolution-independent Gaussian once, without per-frame or resize work.</summary>
        private static readonly float s_gaussianCenterWeight = BuildPanelBlurKernel();
        /// <summary>Stores requested effect activation independently of persistent resource ownership.</summary>
        private static bool s_enabled;
        /// <summary>Retains the selected style without reallocating its prebuilt target pool.</summary>
        private static UiBackdropStyle s_style;
        /// <summary>Tracks paired capture-bridge and camera callbacks.</summary>
        private static bool s_subscribed;
        /// <summary>Keeps quit cleanup subscribed even while gameplay has no active captures.</summary>
        private static bool s_shutdownSubscribed;
        /// <summary>Prevents repeated missing-resource loads.</summary>
        private static bool s_resourcesLoaded;
        /// <summary>Prevents retrying a rejected pool descriptor every frame.</summary>
        private static bool s_allocationFailed;
        /// <summary>Suppresses repeated recoverable allocation or filter warnings.</summary>
        private static bool s_reportedFailure;
        /// <summary>Retains the authored Gaussian template without modifying it.</summary>
        private static Material? s_blurTemplate;
        /// <summary>Retains the authored panel template without modifying it.</summary>
        private static Material? s_glassTemplate;
        /// <summary>Owns one reusable filter material for every stage and style.</summary>
        private static Material? s_blurMaterial;
        /// <summary>Owns the half-resolution bilinear liquid category snapshot without a Gaussian pass.</summary>
        private static RenderTexture? s_liquidCategoryTexture;
        /// <summary>Owns the half-resolution bilinear liquid peripheral snapshot without a Gaussian pass.</summary>
        private static RenderTexture? s_liquidButtonTexture;
        /// <summary>Owns the quarter-resolution scene-prefix board result shared by both styles.</summary>
        private static RenderTexture? s_boardBlurTexture;
        /// <summary>Owns the quarter-resolution Gaussian category result.</summary>
        private static RenderTexture? s_categoryBlurTexture;
        /// <summary>Owns the quarter-resolution Gaussian peripheral result.</summary>
        private static RenderTexture? s_buttonBlurTexture;
        /// <summary>Shares the quarter-resolution horizontal intermediate across ordered Gaussian stages.</summary>
        private static RenderTexture? s_blurScratchTexture;
        /// <summary>Caches the full source size and persistent target-pool descriptor.</summary>
        private static Vector2Int s_sourceSize;
        /// <summary>Caches the positive half-resolution liquid snapshot size.</summary>
        private static Vector2Int s_halfResolutionSize;
        /// <summary>Caches the positive quarter-resolution Gaussian result and scratch size.</summary>
        private static Vector2Int s_gaussianResolutionSize;

        /// <summary>Gets whether the effective rendering configuration requests menu backgrounds.</summary>
        internal static bool Enabled => s_enabled;

        /// <summary>Gets whether the selected style uses raw control snapshots with liquid refraction.</summary>
        internal static bool IsLiquidGlass => s_style == UiBackdropStyle.LiquidGlass;

        /// <summary>Gets the included panel template, or null when ordinary Alpha fallback is required.</summary>
        internal static Material? PvGlassMaterial
        {
            get
            {
                if (!s_enabled)
                {
                    return null;
                }
                LoadResources();
                return s_glassTemplate;
            }
        }

        /// <summary>Precreates both style pools once the display and graphics device are available.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            EnsureShutdownSubscription();
            if (s_sourceSize == default && Screen.width > 0 && Screen.height > 0
                && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                if (!EnsureTargets(GetSourceSize(Screen.width, Screen.height)))
                {
                    s_allocationFailed = false;
                }
            }
        }

        /// <summary>Changes activation or style without discarding the persistent target pools.</summary>
        /// <param name="enabled">Whether menu backgrounds should be captured.</param>
        /// <param name="style">The requested Gaussian or liquid background treatment.</param>
        internal static void Configure(bool enabled, UiBackdropStyle style = UiBackdropStyle.GaussianBlur)
        {
            EnsureShutdownSubscription();
            if (Application.isPlaying && s_sourceSize == default)
            {
                Initialize();
            }
            if (style != UiBackdropStyle.LiquidGlass)
            {
                style = UiBackdropStyle.GaussianBlur;
            }
            if (s_enabled == enabled && s_style == style)
            {
                return;
            }
            s_enabled = enabled;
            s_style = style;
            SetUnavailable();
            UpdateSubscriptions();
            for (var index = s_panels.Count - 1; index >= 0; index--)
            {
                var panel = s_panels[index];
                if (panel != null)
                {
                    panel.RefreshMaterial();
                }
            }
            UiOutlineTheme.RefreshMaterials();
        }

        /// <summary>Registers one consumer without allocating a per-panel target.</summary>
        /// <param name="panel">The panel whose visibility requests an ordered source.</param>
        internal static void Register(UiBackdropPanel panel)
        {
            if (!s_panels.Contains(panel))
            {
                s_panels.Add(panel);
            }
            EnsureShutdownSubscription();
            UpdateSubscriptions();
        }

        /// <summary>Stops an unregistering consumer without releasing persistent target pools.</summary>
        /// <param name="panel">The disabled or destroyed panel to remove.</param>
        internal static void Unregister(UiBackdropPanel panel)
        {
            s_panels.Remove(panel);
            UpdateSubscriptions();
            if (!HasCaptureOwner())
            {
                SetUnavailable();
            }
        }

        /// <summary>Registers an authored liquid frame to reuse the existing peripheral snapshot.</summary>
        /// <param name="image">The enabled Image whose cached liquid material requests a frame capture.</param>
        internal static void Register(QualityUiImage image)
        {
            if (!s_liquidFrameConsumers.Contains(image))
            {
                s_liquidFrameConsumers.Add(image);
            }
            EnsureShutdownSubscription();
            UpdateSubscriptions();
        }

        /// <summary>Removes a liquid-frame request without releasing shared persistent targets.</summary>
        /// <param name="image">The Image whose frame request is ending.</param>
        internal static void Unregister(QualityUiImage image)
        {
            s_liquidFrameConsumers.Remove(image);
            UpdateSubscriptions();
            if (!HasCaptureOwner())
            {
                SetUnavailable();
            }
        }

        /// <summary>Clears state before a new runtime session, including domain-reload-free Play.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            s_enabled = false;
            s_style = UiBackdropStyle.GaussianBlur;
            s_panels.Clear();
            s_liquidFrameConsumers.Clear();
            UpdateSubscriptions();
            ReleaseResources();
        }

        /// <summary>Retains application cleanup independently of menu capture subscriptions.</summary>
        private static void EnsureShutdownSubscription()
        {
            if (!s_shutdownSubscribed)
            {
                Application.quitting += ReleaseResources;
                s_shutdownSubscribed = true;
            }
        }

        /// <summary>Checks registered capture owners without allowing reuse-only surfaces to start Gaussian capture.</summary>
        /// <returns>Whether any supported panel or authored liquid frame owns capture activation.</returns>
        private static bool HasCaptureOwner()
        {
            foreach (var panel in s_panels)
            {
                if (panel != null && panel.SupportsCurrentStyle && !panel.ReusesPanelCaptureOnly)
                {
                    return true;
                }
            }
            if (IsLiquidGlass)
            {
                foreach (var image in s_liquidFrameConsumers)
                {
                    if (image != null)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Pairs only the bridge and camera callbacks needed by active menu capture owners.</summary>
        private static void UpdateSubscriptions()
        {
            var subscribe = s_enabled && HasCaptureOwner();
            if (subscribe == s_subscribed)
            {
                return;
            }
            s_subscribed = subscribe;
            if (subscribe)
            {
                MajUiBackdropBridge.Register(s_capture);
                RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            }
            else
            {
                MajUiBackdropBridge.Unregister(s_capture);
                RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
                SetUnavailable();
            }
        }

        /// <summary>Prevents a skipped camera or gameplay frame from using stale menu sources.</summary>
        /// <param name="context">The current rendering context; no GPU work is submitted here.</param>
        /// <param name="camera">The camera about to render.</param>
        private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            SetUnavailable();
        }

        /// <summary>Checks visible source consumers without scene searches or temporary collections.</summary>
        /// <param name="camera">The intended menu camera.</param>
        /// <param name="role">The requested source role, or null for any role.</param>
        /// <param name="ownersOnly">Whether reuse-only consumers must be excluded from activation.</param>
        /// <returns>Whether a supported visible consumer matches this camera and role.</returns>
        private static bool HasVisibleConsumer(Camera camera, UiBackdropSourceRole? role = null, bool ownersOnly = false)
        {
            for (var index = s_panels.Count - 1; index >= 0; index--)
            {
                var panel = s_panels[index];
                if (panel == null)
                {
                    s_panels.RemoveAt(index);
                }
                else if ((!role.HasValue || panel.SourceRole == role.Value)
                    && (!ownersOnly || !panel.ReusesPanelCaptureOnly)
                    && panel.IsVisible && panel.RenderCamera == camera)
                {
                    // This is a boolean query: avoid repeating native Canvas queries after a match.
                    // OnDisable unregisters live panels; encountered destroyed entries are still pruned.
                    return true;
                }
            }
            if (!IsLiquidGlass || (role.HasValue && role.Value != UiBackdropSourceRole.Peripheral))
            {
                return false;
            }
            for (var index = s_liquidFrameConsumers.Count - 1; index >= 0; index--)
            {
                var image = s_liquidFrameConsumers[index];
                if (image == null)
                {
                    s_liquidFrameConsumers.RemoveAt(index);
                }
                else if (image.IsLiquidFrameCaptureVisible && image.RenderCamera == camera)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Loads supported filter templates, retaining original Alpha on failure.</summary>
        private static void LoadResources()
        {
            if (s_resourcesLoaded)
            {
                return;
            }
            s_resourcesLoaded = true;
            var resources = Resources.Load<UiOutlineResources>(ResourcesPath);
            var blur = resources != null ? resources.PvBlurMaterial : null;
            var glass = resources != null ? resources.PvGlassMaterial : null;
            const int requiredPasses = 4;
            if (glass == null || !glass.shader.isSupported || blur == null ||
                !blur.shader.isSupported || blur.passCount < requiredPasses)
            {
                ReportFailure("Background blur shaders are unavailable; preserving ordinary Alpha panels.");
                return;
            }
            s_blurTemplate = blur;
            s_glassTemplate = glass;
        }

        /// <summary>Normalizes full scene dimensions for persistent pool resizing.</summary>
        /// <param name="width">The scene attachment width.</param>
        /// <param name="height">The scene attachment height.</param>
        /// <returns>The positive source scene size before half- and quarter-resolution target calculations.</returns>
        private static Vector2Int GetSourceSize(int width, int height)
        {
            return new Vector2Int(Mathf.Max(1, width), Mathf.Max(1, height));
        }

        /// <summary>Gets the half-resolution grid for unblurred liquid snapshots.</summary>
        /// <param name="width">The scene attachment width.</param>
        /// <param name="height">The scene attachment height.</param>
        /// <returns>The positive half-resolution target size rounded upward on each axis.</returns>
        private static Vector2Int GetHalfResolutionSize(int width, int height)
        {
            return new Vector2Int(Mathf.Max(1, Mathf.CeilToInt(width * 0.5f)), Mathf.Max(1, Mathf.CeilToInt(height * 0.5f)));
        }

        /// <summary>Gets the quarter-resolution grid shared by Gaussian results and their horizontal intermediate.</summary>
        /// <param name="width">The scene attachment width.</param>
        /// <param name="height">The scene attachment height.</param>
        /// <returns>The positive quarter-resolution size rounded upward on each axis.</returns>
        private static Vector2Int GetGaussianResolutionSize(int width, int height)
        {
            return new Vector2Int(Mathf.Max(1, Mathf.CeilToInt(width * 0.25f)), Mathf.Max(1, Mathf.CeilToInt(height * 0.25f)));
        }

        /// <summary>Maps camera capture UVs to the actual main-display circle after layout transforms.</summary>
        /// <param name="camera">The camera rendering the scene-prefix capture.</param>
        /// <param name="display">The main-display rectangle defining the circle or transformed ellipse.</param>
        /// <param name="uvX">Receives the horizontal affine row in XYZ and an enabled flag in W.</param>
        /// <param name="uvY">Receives the vertical affine row in XYZ, with W unused.</param>
        /// <returns>True when the projected circle has a finite, invertible basis in front of the camera.</returns>
        internal static bool TryGetCircleUvMapping(Camera camera, RectTransform display, out Vector4 uvX, out Vector4 uvY)
        {
            uvX = Vector4.zero;
            uvY = Vector4.zero;
            if (camera == null || display == null)
            {
                return false;
            }
            var rect = display.rect;
            var center = camera.WorldToViewportPoint(display.TransformPoint(rect.center));
            var horizontal = camera.WorldToViewportPoint(display.TransformPoint(rect.center + Vector2.right * (rect.width * 0.5f))) - center;
            var vertical = camera.WorldToViewportPoint(display.TransformPoint(rect.center + Vector2.up * (rect.height * 0.5f))) - center;
            var determinant = horizontal.x * vertical.y - horizontal.y * vertical.x;
            if (center.z <= 0f || !float.IsFinite(determinant) || Mathf.Abs(determinant) <= 0.00000001f)
            {
                return false;
            }
            var inverse = 1f / determinant;
            uvX = new Vector4(vertical.y * inverse, -vertical.x * inverse,
                (vertical.x * center.y - vertical.y * center.x) * inverse, 1f);
            uvY = new Vector4(-horizontal.y * inverse, horizontal.x * inverse,
                (horizontal.y * center.x - horizontal.x * center.y) * inverse, 0f);
            return true;
        }

        /// <summary>Builds the fixed sigma-four, radius-eight kernel shared by horizontal and vertical filtering.</summary>
        /// <returns>The normalized center weight; each retained XY pair stores its weight and bilinear offset.</returns>
        private static float BuildPanelBlurKernel()
        {
            var sum = 1.0;
            var inverseVariance = 1.0 / (2.0 * GaussianSigma * GaussianSigma);
            for (var pair = 0; pair < s_gaussianKernelPairs.Length; pair++)
            {
                var first = pair * 2 + 1;
                var firstWeight = Math.Exp(-first * first * inverseVariance);
                var secondWeight = Math.Exp(-(first + 1) * (first + 1) * inverseVariance);
                var weight = firstWeight + secondWeight;
                var offset = (float)(first + secondWeight / weight);
                s_gaussianKernelPairs[pair] = new Vector4((float)weight, offset, 0f, 0f);
                sum += 2.0 * weight;
            }
            for (var pair = 0; pair < s_gaussianKernelPairs.Length; pair++)
            {
                var packed = s_gaussianKernelPairs[pair];
                packed.x /= (float)sum;
                s_gaussianKernelPairs[pair] = packed;
            }
            return (float)(1.0 / sum);
        }

        /// <summary>Prepares all six targets for both styles, replacing objects only for size changes or lost targets.</summary>
        /// <param name="size">The positive full scene size.</param>
        /// <returns>Whether both persistent style pools are available.</returns>
        private static bool EnsureTargets(Vector2Int size)
        {
            if (size != s_sourceSize)
            {
                SetUnavailable();
                ClearSourceBindings();
                ReleaseTargets();
                s_sourceSize = size;
                s_halfResolutionSize = GetHalfResolutionSize(size.x, size.y);
                s_gaussianResolutionSize = GetGaussianResolutionSize(size.x, size.y);
                s_allocationFailed = false;
            }
            if (s_allocationFailed)
            {
                return false;
            }
            if (AreTargetsReady())
            {
                return true;
            }
            ReleaseTargets();
            try
            {
                var halfDescriptor = CreateDescriptor(s_halfResolutionSize);
                var gaussianDescriptor = CreateDescriptor(s_gaussianResolutionSize);
                s_liquidCategoryTexture = CreateTarget(halfDescriptor, "Maj UI Half Category Snapshot");
                s_liquidButtonTexture = CreateTarget(halfDescriptor, "Maj UI Half Peripheral Snapshot");
                s_boardBlurTexture = CreateTarget(gaussianDescriptor, "Maj UI Quarter Gaussian Board Result");
                s_categoryBlurTexture = CreateTarget(gaussianDescriptor, "Maj UI Quarter Gaussian Category Result");
                s_buttonBlurTexture = CreateTarget(gaussianDescriptor, "Maj UI Quarter Gaussian Peripheral Result");
                s_blurScratchTexture = CreateTarget(gaussianDescriptor, "Maj UI Quarter Gaussian Shared Horizontal");
                if (AreTargetsReady())
                {
                    return true;
                }
            }
            catch (Exception exception)
            {
                ReportFailure("Menu background pool allocation failed: " + exception.Message);
            }
            ReleaseTargets();
            s_allocationFailed = true;
            ReportFailure("Menu background targets are unavailable; preserving ordinary Alpha surfaces.");
            return false;
        }

        /// <summary>Checks the complete persistent pool before reuse and after allocation.</summary>
        /// <returns>Whether native storage is available for all six ordered capture and filtering targets.</returns>
        private static bool AreTargetsReady()
        {
            return IsTargetReady(s_liquidCategoryTexture) && IsTargetReady(s_liquidButtonTexture)
                && IsTargetReady(s_boardBlurTexture) && IsTargetReady(s_categoryBlurTexture)
                && IsTargetReady(s_buttonBlurTexture) && IsTargetReady(s_blurScratchTexture);
        }

        /// <summary>Checks one retained target without allocating.</summary>
        /// <param name="target">The owned target to inspect.</param>
        /// <returns>Whether its native storage is available.</returns>
        private static bool IsTargetReady(RenderTexture? target)
        {
            return target != null && target.IsCreated();
        }

        /// <summary>Builds the shared LDR, single-sample and color-only target descriptor.</summary>
        /// <param name="size">The positive target dimensions.</param>
        /// <returns>A bilinear-compatible descriptor with no depth, mipmaps or memoryless storage.</returns>
        private static RenderTextureDescriptor CreateDescriptor(Vector2Int size)
        {
            return new RenderTextureDescriptor(size.x, size.y)
            {
                graphicsFormat = QualitySettings.activeColorSpace == ColorSpace.Linear
                    ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm,
                depthStencilFormat = GraphicsFormat.None,
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false,
                useDynamicScale = false,
                memoryless = RenderTextureMemoryless.None
            };
        }

        /// <summary>Creates one owned bilinear LDR texture with safe allocation failure handling.</summary>
        /// <param name="descriptor">The bounded color-only descriptor.</param>
        /// <param name="name">The debugger-visible target name.</param>
        /// <returns>The created target, or null if allocation is rejected.</returns>
        private static RenderTexture? CreateTarget(RenderTextureDescriptor descriptor, string name)
        {
            RenderTexture? target = null;
            try
            {
                target = new RenderTexture(descriptor)
                {
                    name = name,
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = 0
                };
                if (target.Create() && target.IsCreated() && target.graphicsFormat == descriptor.graphicsFormat)
                {
                    return target;
                }
            }
            catch (Exception exception) when (exception is UnityException || exception is ArgumentException)
            {
                ReportFailure("Background blur allocation failed: " + exception.Message);
            }
            DestroyOwned(target);
            return null;
        }

        /// <summary>Returns all source consumers to original Alpha without releasing their persistent pools.</summary>
        private static void SetUnavailable()
        {
            Shader.SetGlobalFloat(PanelReadyId, 0f);
            Shader.SetGlobalFloat(ReadyId, 0f);
            Shader.SetGlobalFloat(ButtonReadyId, 0f);
            Shader.SetGlobalVector(ButtonClipUvXId, Vector4.zero);
            Shader.SetGlobalVector(ButtonClipUvYId, Vector4.zero);
            Shader.SetGlobalVector(ButtonClipSampleBoundsId, Vector4.zero);
            Shader.SetGlobalVector(CategoryClipUvXId, Vector4.zero);
            Shader.SetGlobalVector(CategoryClipUvYId, Vector4.zero);
            Shader.SetGlobalVector(CategoryClipSampleBoundsId, Vector4.zero);
            Shader.SetGlobalVector(CaptureClipUvXId, Vector4.zero);
            Shader.SetGlobalVector(CaptureClipUvYId, Vector4.zero);
        }

        /// <summary>Records unavailable sources in command-stream order.</summary>
        /// <param name="buffer">The current camera's ordered command buffer.</param>
        private static void SetUnavailable(CommandBuffer buffer)
        {
            buffer.SetGlobalFloat(PanelReadyId, 0f);
            buffer.SetGlobalFloat(ReadyId, 0f);
            buffer.SetGlobalFloat(ButtonReadyId, 0f);
            buffer.SetGlobalVector(ButtonClipUvXId, Vector4.zero);
            buffer.SetGlobalVector(ButtonClipUvYId, Vector4.zero);
            buffer.SetGlobalVector(ButtonClipSampleBoundsId, Vector4.zero);
            buffer.SetGlobalVector(CategoryClipUvXId, Vector4.zero);
            buffer.SetGlobalVector(CategoryClipUvYId, Vector4.zero);
            buffer.SetGlobalVector(CategoryClipSampleBoundsId, Vector4.zero);
            buffer.SetGlobalVector(CaptureClipUvXId, Vector4.zero);
            buffer.SetGlobalVector(CaptureClipUvYId, Vector4.zero);
        }

        /// <summary>Clears texture bindings before a descriptor replacement or final release.</summary>
        private static void ClearSourceBindings()
        {
            Shader.SetGlobalTexture(PanelBlurTextureId, Texture2D.blackTexture);
            Shader.SetGlobalVector(PanelBlurTexelSizeId, Vector4.zero);
            Shader.SetGlobalTexture(BlurTextureId, Texture2D.blackTexture);
            Shader.SetGlobalVector(BlurTexelSizeId, Vector4.zero);
            Shader.SetGlobalTexture(ButtonBlurTextureId, Texture2D.blackTexture);
            Shader.SetGlobalVector(ButtonBlurTexelSizeId, Vector4.zero);
            Shader.SetGlobalVector(ButtonClipUvXId, Vector4.zero);
            Shader.SetGlobalVector(ButtonClipUvYId, Vector4.zero);
            Shader.SetGlobalVector(ButtonClipSampleBoundsId, Vector4.zero);
            Shader.SetGlobalVector(CategoryClipUvXId, Vector4.zero);
            Shader.SetGlobalVector(CategoryClipUvYId, Vector4.zero);
            Shader.SetGlobalVector(CategoryClipSampleBoundsId, Vector4.zero);
            Shader.SetGlobalVector(CaptureClipUvXId, Vector4.zero);
            Shader.SetGlobalVector(CaptureClipUvYId, Vector4.zero);
        }

        /// <summary>Logs a recoverable resource or filtering failure once.</summary>
        /// <param name="message">The failure context without private user data.</param>
        private static void ReportFailure(string message)
        {
            if (!s_reportedFailure)
            {
                s_reportedFailure = true;
                MajDebug.LogWarning(nameof(UiPvBackdropBlur), message);
            }
        }

        /// <summary>Releases only service-owned targets during descriptor replacement or shutdown.</summary>
        private static void ReleaseTargets()
        {
            DestroyOwned(s_liquidCategoryTexture);
            DestroyOwned(s_liquidButtonTexture);
            DestroyOwned(s_boardBlurTexture);
            DestroyOwned(s_categoryBlurTexture);
            DestroyOwned(s_buttonBlurTexture);
            DestroyOwned(s_blurScratchTexture);
            s_liquidCategoryTexture = null;
            s_liquidButtonTexture = null;
            s_boardBlurTexture = null;
            s_categoryBlurTexture = null;
            s_buttonBlurTexture = null;
            s_blurScratchTexture = null;
        }

        /// <summary>Disposes persistent resources only when the application exits or the runtime session resets.</summary>
        private static void ReleaseResources()
        {
            s_enabled = false;
            UpdateSubscriptions();
            if (s_shutdownSubscribed)
            {
                Application.quitting -= ReleaseResources;
                s_shutdownSubscribed = false;
            }
            SetUnavailable();
            ClearSourceBindings();
            ReleaseTargets();
            DestroyOwned(s_blurMaterial);
            s_blurMaterial = null;
            s_blurTemplate = null;
            s_glassTemplate = null;
            s_resourcesLoaded = false;
            s_allocationFailed = false;
            s_reportedFailure = false;
            s_sourceSize = default;
            s_halfResolutionSize = default;
            s_gaussianResolutionSize = default;
            Shader.SetGlobalVector(PanelBlurStepId, Vector4.zero);
            Shader.SetGlobalFloat(GaussianCenterWeightId, 0f);
            Shader.SetGlobalFloat(SourcePointFilterId, 0f);
        }

        /// <summary>Destroys one service-owned object safely inside and outside Play Mode.</summary>
        /// <param name="owned">The owned object to release, or null.</param>
        private static void DestroyOwned(UnityEngine.Object? owned)
        {
            if (owned == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(owned);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(owned);
            }
        }

        /// <summary>Connects the game-owned backgrounds to MRP's three ordered scene prefixes.</summary>
        private sealed class Capture : IMajUiCategoryBackdropCapture
        {
            /// <summary>Requests attachments only for visible menu owners, never during gameplay.</summary>
            /// <param name="camera">The camera being prepared.</param>
            /// <returns>Whether the menu pipeline should record its ordered background sources.</returns>
            public bool IsActive(Camera camera)
            {
                if (!s_enabled || SceneSwitcher.CurrentScene == MajScenes.Game || camera == null
                    || camera.cameraType != CameraType.Game || camera != SceneSwitcher.MainCamera
                    || !HasVisibleConsumer(camera, ownersOnly: true))
                {
                    return false;
                }
                LoadResources();
                return s_glassTemplate != null && s_blurTemplate != null;
            }

            /// <summary>Records RT1 from scene-prefix color including the blue ring before board and background-cover drawing.</summary>
            /// <param name="buffer">The camera's ordered command buffer.</param>
            /// <param name="camera">The intended menu camera.</param>
            /// <param name="resolvedColor">The borrowed scene prefix including the blue ring.</param>
            public void Record(CommandBuffer buffer, Camera camera, RenderTexture resolvedColor)
            {
                SetUnavailable(buffer);
                RecordStage(buffer, camera, resolvedColor, UiBackdropSourceRole.Board);
            }

            /// <summary>Requests RT2 whenever a supported category-source consumer is visible.</summary>
            /// <param name="camera">The intended menu camera.</param>
            /// <returns>Whether the middle capture cut is needed, including Setting without a song cover.</returns>
            public bool RequiresCategoryCapture(Camera camera)
            {
                return s_enabled && SceneSwitcher.CurrentScene != MajScenes.Game
                    && HasVisibleConsumer(camera, UiBackdropSourceRole.Category);
            }

            /// <summary>Records RT2 after board and background-song-cover drawing and before category drawing.</summary>
            /// <param name="buffer">The camera's ordered command buffer.</param>
            /// <param name="camera">The intended menu camera.</param>
            /// <param name="resolvedColor">The borrowed prefix including boards and any background song cover.</param>
            public void RecordCategories(CommandBuffer buffer, Camera camera, RenderTexture resolvedColor)
            {
                buffer.SetGlobalFloat(ReadyId, 0f);
                RecordStage(buffer, camera, resolvedColor, UiBackdropSourceRole.Category);
            }

            /// <summary>Requests RT3 only while supported peripheral consumers are visible outside gameplay.</summary>
            /// <param name="camera">The intended menu camera.</param>
            /// <returns>Whether the final peripheral capture cut is needed.</returns>
            public bool RequiresButtonCapture(Camera camera)
            {
                return s_enabled && SceneSwitcher.CurrentScene != MajScenes.Game
                    && HasVisibleConsumer(camera, UiBackdropSourceRole.Peripheral);
            }

            /// <summary>Records RT3 after ordinary UI and before the eight peripheral button bodies.</summary>
            /// <param name="buffer">The camera's ordered command buffer.</param>
            /// <param name="camera">The intended menu camera.</param>
            /// <param name="resolvedColor">The borrowed scene prefix before peripheral bodies.</param>
            public void RecordButtons(CommandBuffer buffer, Camera camera, RenderTexture resolvedColor)
            {
                buffer.SetGlobalFloat(ButtonReadyId, 0f);
                RecordStage(buffer, camera, resolvedColor, UiBackdropSourceRole.Peripheral);
            }
        }

        /// <summary>Records one visible role without letting reuse-only consumers independently start capture.</summary>
        /// <param name="buffer">The ordered camera command buffer.</param>
        /// <param name="camera">The intended menu camera.</param>
        /// <param name="source">The borrowed readable scene prefix.</param>
        /// <param name="role">The result that may be written by this capture cut.</param>
        private static void RecordStage(CommandBuffer buffer, Camera camera, RenderTexture source, UiBackdropSourceRole role)
        {
            if (!s_enabled || SceneSwitcher.CurrentScene == MajScenes.Game)
            {
                SetUnavailable(buffer);
                return;
            }
            if (!HasVisibleConsumer(camera, ownersOnly: true) || !HasVisibleConsumer(camera, role))
            {
                return;
            }
            FilterBackground(buffer, camera, source, role);
        }

        /// <summary>Publishes independent category or peripheral sample bounds and selects coverage for the current capture.</summary>
        /// <param name="buffer">The ordered capture command stream.</param>
        /// <param name="camera">The camera whose viewport defines the captured UVs.</param>
        /// <param name="role">The source stage; boards clear capture coverage without changing retained control mappings.</param>
        /// <param name="width">The actual selected capture target width.</param>
        /// <param name="height">The actual selected capture target height.</param>
        private static void SetCaptureClipMapping(CommandBuffer buffer, Camera camera, UiBackdropSourceRole role, int width, int height)
        {
            var uvX = Vector4.zero;
            var uvY = Vector4.zero;
            var sampleBounds = Vector4.zero;
            if (role != UiBackdropSourceRole.Board)
            {
                foreach (var panel in s_panels)
                {
                    if (panel == null || panel.SourceRole != role ||
                        (role == UiBackdropSourceRole.Peripheral && !panel.IsButton) ||
                        !panel.IsVisible || panel.RenderCamera != camera)
                    {
                        continue;
                    }
                    var display = panel.MainDisplayRect;
                    if (display != null && display.gameObject.activeInHierarchy &&
                        TryGetCircleUvMapping(camera, display, out uvX, out uvY))
                    {
                        break;
                    }
                }
                if (role == UiBackdropSourceRole.Peripheral && uvX.w <= 0.5f)
                {
                    foreach (var image in s_liquidFrameConsumers)
                    {
                        if (image == null || !image.IsLiquidFrameCaptureVisible || image.RenderCamera != camera)
                        {
                            continue;
                        }
                        var display = image.MainDisplayRect;
                        if (display != null && display.gameObject.activeInHierarchy &&
                            TryGetCircleUvMapping(camera, display, out uvX, out uvY))
                        {
                            break;
                        }
                    }
                }
                if (uvX.w > 0.5f)
                {
                    var inverse = 1f / (uvX.x * uvY.y - uvX.y * uvY.x);
                    var centerX = (uvX.y * uvY.z - uvY.y * uvX.z) * inverse;
                    var centerY = (uvY.x * uvX.z - uvX.x * uvY.z) * inverse;
                    var horizontalStep = new Vector2(uvX.x / width, uvY.x / width);
                    var verticalStep = new Vector2(uvX.y / height, uvY.y / height);
                    var inset = 2f * Mathf.Sqrt(horizontalStep.sqrMagnitude + verticalStep.sqrMagnitude);
                    sampleBounds = new Vector4(centerX, centerY, Mathf.Max(0f, 1f - inset), 0f);
                }
            }
            if (role == UiBackdropSourceRole.Category)
            {
                buffer.SetGlobalVector(CategoryClipUvXId, uvX);
                buffer.SetGlobalVector(CategoryClipUvYId, uvY);
                buffer.SetGlobalVector(CategoryClipSampleBoundsId, sampleBounds);
            }
            else if (role == UiBackdropSourceRole.Peripheral)
            {
                buffer.SetGlobalVector(ButtonClipUvXId, uvX);
                buffer.SetGlobalVector(ButtonClipUvYId, uvY);
                buffer.SetGlobalVector(ButtonClipSampleBoundsId, sampleBounds);
            }
            buffer.SetGlobalVector(CaptureClipUvXId, uvX);
            buffer.SetGlobalVector(CaptureClipUvYId, uvY);
        }

        /// <summary>Copies liquid control prefixes or filters strong prefixes into independent persistent results.</summary>
        /// <param name="buffer">The ordered camera command buffer outside native render passes.</param>
        /// <param name="camera">The camera whose capture UVs define control-circle coverage.</param>
        /// <param name="source">The borrowed scene prefix, valid in this command stream.</param>
        /// <param name="role">The independent board, category or peripheral result to publish.</param>
        private static void FilterBackground(CommandBuffer buffer, Camera camera, RenderTexture source, UiBackdropSourceRole role)
        {
            var readyId = role == UiBackdropSourceRole.Board ? PanelReadyId
                : role == UiBackdropSourceRole.Category ? ReadyId : ButtonReadyId;
            var sampleName = role == UiBackdropSourceRole.Board ? BoardSampleName
                : role == UiBackdropSourceRole.Category ? CategorySampleName : ButtonSampleName;
            try
            {
                LoadResources();
                if (s_blurTemplate == null || !EnsureTargets(GetSourceSize(source.width, source.height)))
                {
                    return;
                }
                s_blurMaterial ??= new Material(s_blurTemplate)
                {
                    name = "Maj UI Ordered Background Capture",
                    hideFlags = HideFlags.HideAndDontSave
                };
                var rawSnapshot = IsLiquidGlass && role != UiBackdropSourceRole.Board;
                var result = rawSnapshot
                    ? (role == UiBackdropSourceRole.Category ? s_liquidCategoryTexture! : s_liquidButtonTexture!)
                    : role == UiBackdropSourceRole.Board ? s_boardBlurTexture!
                    : role == UiBackdropSourceRole.Category ? s_categoryBlurTexture! : s_buttonBlurTexture!;
                SetCaptureClipMapping(buffer, camera, role, result.width, result.height);
                buffer.BeginSample(sampleName);
                try
                {
                    buffer.SetGlobalFloat(SourcePointFilterId, source.filterMode == FilterMode.Point ? 1f : 0f);
                    if (rawSnapshot)
                    {
                        // Capture this exact scene prefix once at half size, using hardware bilinear reduction.
                        // Consumers read the owned Bilinear/Clamp snapshot without another Gaussian pass.
                        // The pass controls borrowed Point-filtered input without changing its sampler or ownership.
                        buffer.Blit(source, result, s_blurMaterial, 3);
                        buffer.SetGlobalFloat(SourcePointFilterId, 0f);
                    }
                    else
                    {
                        buffer.SetGlobalVector(DownsampleStepId,
                            new Vector4(0.25f / s_gaussianResolutionSize.x, 0.25f / s_gaussianResolutionSize.y, 0f, 0f));
                        buffer.Blit(source, result, s_blurMaterial, 0);
                        buffer.SetGlobalFloat(SourcePointFilterId, 0f);
                        buffer.SetGlobalVector(PanelBlurStepId,
                            new Vector4(1f / s_gaussianResolutionSize.x, 1f / s_gaussianResolutionSize.y, 0f, 0f));
                        buffer.SetGlobalFloat(GaussianCenterWeightId, s_gaussianCenterWeight);
                        buffer.SetGlobalVectorArray(GaussianPairsId, s_gaussianKernelPairs);
                        buffer.Blit(result, s_blurScratchTexture!, s_blurMaterial, 1);
                        buffer.Blit(s_blurScratchTexture!, result, s_blurMaterial, 2);
                    }
                }
                finally
                {
                    buffer.EndSample(sampleName);
                }
                var textureId = role == UiBackdropSourceRole.Board ? PanelBlurTextureId
                    : role == UiBackdropSourceRole.Category ? BlurTextureId : ButtonBlurTextureId;
                var texelId = role == UiBackdropSourceRole.Board ? PanelBlurTexelSizeId
                    : role == UiBackdropSourceRole.Category ? BlurTexelSizeId : ButtonBlurTexelSizeId;
                buffer.SetGlobalTexture(textureId, result);
                buffer.SetGlobalVector(texelId, new Vector4(1f / result.width, 1f / result.height, result.width, result.height));
                buffer.SetGlobalFloat(readyId, 1f);
            }
            catch (Exception exception)
            {
                buffer.SetGlobalFloat(readyId, 0f);
                ReportFailure("Ordered menu background capture failed; preserving ordinary Alpha surfaces. " + exception.Message);
            }
        }
    }
}
