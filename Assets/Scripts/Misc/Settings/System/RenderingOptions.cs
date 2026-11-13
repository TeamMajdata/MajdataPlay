using System;
using System.Runtime.Serialization;
using MajdataPlay.Rendering;
using MajdataPlay.Settings.OptionEnumerators;
using Newtonsoft.Json;
using UnityEngine.Scripting;
#nullable enable
namespace MajdataPlay.Settings
{
    [Preserve]
    public enum RenderingQualityOption
    {
        EnergySaving,
        Performance,
        Beautiful,
        Refined,
        Custom
    }

    [Preserve]
    public enum RenderingAntialiasingOption
    {
        Disabled = 1,
        MSAA2x = 2,
        MSAA4x = 4,
        MSAA8x = 8
    }

    [Preserve]
    public enum RenderingTextureQualityOption
    {
        Low,
        Medium,
        High
    }

    [Preserve]
    public enum RenderingOutputMode
    {
        Direct,
        Intermediate
    }

    [Preserve]
    public sealed class RenderingOptions
    {
        private RenderingQualityOption _quality = SettingDefaults.Rendering.RenderQuality;
        private RenderingAntialiasingOption _antiAliasing = SettingDefaults.Rendering.AntiAliasing;
        private RenderingTextureQualityOption _textureQuality = SettingDefaults.Rendering.TextureQuality;
        private RenderingOutputMode _outputMode = SettingDefaults.Rendering.OutputMode;
        /// <summary>Stores the complete UI artwork, compositing and backdrop choice independently of MSAA.</summary>
        private UiOutlineMode _uiOutlineMode = SettingDefaults.Rendering.UIOutlineMode;
        /// <summary>Stores the preferred Refined panel effect independently of the selected quality preset.</summary>
        private UiBackdropStyle _uiBackdropStyle = SettingDefaults.Rendering.UIBackdropStyle;
        /// <summary>Distinguishes the current saved choice from the legacy migration-only keys.</summary>
        private bool _hasSerializedUIOutlineMode;
        /// <summary>Retains the old boolean while reading an existing Custom configuration.</summary>
        private bool _legacyUIOutline;
        /// <summary>Retains the old background choice until all legacy keys have been read.</summary>
        private LegacyUiBackgroundMode _legacyUIBackgroundMode;
        /// <summary>Retains the retired backdrop flag only while migrating configurations without a unified UI mode.</summary>
        private bool _legacyUIBackdropBlur;
        private int _renderScale = SettingDefaults.Rendering.RenderScale;
        private int _fpsLimit = SettingDefaults.Rendering.FPSLimit;
        private bool _deserializing;

        [ShowInSettingUIWhen(nameof(CompatibilityMode), false)]
        [OptionEnumerator(typeof(RenderingEnumSettingEnumerator))]
        public RenderingQualityOption RenderQuality
        {
            get => _quality;
            set
            {
                _quality = value;
                if (!_deserializing)
                {
                    ApplyPreset();
                }
            }
        }

        [Step("1")]
        [Range("-1", null)]
        [OptionEnumerator(typeof(EngineNumberSettingEnumerator))]
        public int FPSLimit
        {
            get => _fpsLimit;
            set => _fpsLimit = Math.Max(-1, value);
        }

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        [HideInSettingUI]
#endif
        [OptionEnumerator(typeof(EngineBooleanSettingEnumerator))]
        public bool VSync { get; set; } = SettingDefaults.Rendering.VSync;

        [HideInSettingUI]
        [JsonIgnoreOnPlatform(SettingPlatform.Mobile)]
        public bool VulkanLowLatencyVSync { get; set; } = SettingDefaults.Rendering.VulkanLowLatencyVSync;

        [HideInSettingUI]
        [JsonIgnoreOnPlatform(SettingPlatform.Mobile)]
        public VulkanLowLatencyVSyncBackendOption VulkanLowLatencyVSyncBackend { get; set; } = SettingDefaults.Rendering.VulkanLowLatencyVSyncBackend;

        [ShowInSettingUIWhen(nameof(CompatibilityMode), false)]
        [ShowInSettingUIWhen(nameof(RenderQuality), RenderingQualityOption.Custom)]
        [OptionEnumerator(typeof(RenderingEnumSettingEnumerator))]
        public RenderingAntialiasingOption AntiAliasing
        {
            get => _antiAliasing;
            set
            {
                if (_antiAliasing == value)
                {
                    return;
                }
                _antiAliasing = value;
                MarkCustom();
            }
        }

        [ShowInSettingUIWhen(nameof(CompatibilityMode), false)]
        [ShowInSettingUIWhen(nameof(RenderQuality), RenderingQualityOption.Custom)]
        [OptionEnumerator(typeof(RenderingEnumSettingEnumerator))]
        public RenderingTextureQualityOption TextureQuality
        {
            get => _textureQuality;
            set
            {
                if (_textureQuality == value)
                {
                    return;
                }
                _textureQuality = value;
                MarkCustom();
            }
        }

        /// <summary>Gets or sets the complete Custom UI style independently of multisample anti-aliasing.</summary>
        [ShowInSettingUIWhen(nameof(CompatibilityMode), false)]
        [ShowInSettingUIWhen(nameof(RenderQuality), RenderingQualityOption.Custom)]
        [OptionEnumerator(typeof(RenderingEnumSettingEnumerator))]
        public UiOutlineMode UIOutlineMode
        {
            get => _uiOutlineMode;
            set
            {
                if (_deserializing)
                {
                    _hasSerializedUIOutlineMode = true;
                }
                if (_uiOutlineMode == value)
                {
                    return;
                }
                _uiOutlineMode = value;
                MarkCustom();
            }
        }

        /// <summary>Gets or sets the Refined panel effect without changing the selected rendering quality.</summary>
        [ShowInSettingUIWhen(nameof(CompatibilityMode), false)]
        [ShowInSettingUIWhen(nameof(RenderQuality), RenderingQualityOption.Refined)]
        [OptionEnumerator(typeof(RenderingEnumSettingEnumerator))]
        public UiBackdropStyle UIBackdropStyle
        {
            get => _uiBackdropStyle;
            set
            {
                _uiBackdropStyle = value;
                if (!_deserializing && _quality == RenderingQualityOption.Refined)
                {
                    _uiOutlineMode = value == UiBackdropStyle.LiquidGlass
                        ? UiOutlineMode.LiquidGlass : UiOutlineMode.GaussianBlur;
                }
            }
        }

        /// <summary>Reads the retired backdrop flag for legacy migration without writing a second UI control.</summary>
        [Preserve]
        [JsonProperty("UIBackdropBlur")]
        private bool LegacyUIBackdropBlur
        {
            set
            {
                _legacyUIBackdropBlur = value;
            }
        }

        /// <summary>Reads the old boolean UI style without writing it to new settings files.</summary>
        [Preserve]
        [JsonProperty("UIOutline")]
        private bool LegacyUIOutline
        {
            set
            {
                _legacyUIOutline = value;
            }
        }

        /// <summary>Reads the old background choice without exposing or writing a second UI setting.</summary>
        [Preserve]
        [JsonProperty("UIBackgroundMode")]
        private LegacyUiBackgroundMode LegacyUIBackgroundMode
        {
            set
            {
                _legacyUIBackgroundMode = value;
            }
        }

        [ShowInSettingUIWhen(nameof(CompatibilityMode), false)]
        [ShowInSettingUIWhen(nameof(RenderQuality), RenderingQualityOption.Custom)]
        [OptionEnumerator(typeof(RenderingEnumSettingEnumerator))]
        public RenderingOutputMode OutputMode
        {
            get => _outputMode;
            set
            {
                if (!_deserializing && _renderScale != 100)
                {
                    value = RenderingOutputMode.Intermediate;
                }
                if (_outputMode == value)
                {
                    return;
                }
                _outputMode = value;
                MarkCustom();
            }
        }

        [OptionEnumerator(typeof(RenderingBooleanSettingEnumerator))]
        public bool CompatibilityMode { get; set; } = SettingDefaults.Rendering.CompatibilityMode;

        [Step("5")]
        [Range("10", "100")]
        [ShowInSettingUIWhen(nameof(CompatibilityMode), false)]
        [ShowInSettingUIWhen(nameof(RenderQuality), RenderingQualityOption.Custom)]
        [OptionEnumerator(typeof(RenderingNumberSettingEnumerator))]
        public int SystemResolutionScaling { get; set; } = SettingDefaults.Rendering.SystemResolutionScaling;

        [Step("5")]
        [Range("10", "200")]
        [ShowInSettingUIWhen(nameof(CompatibilityMode), false)]
        [ShowInSettingUIWhen(nameof(RenderQuality), RenderingQualityOption.Custom)]
        [OptionEnumerator(typeof(RenderingNumberSettingEnumerator))]
        public int RenderScale
        {
            get => _renderScale;
            set
            {
                var scale = Math.Max(10, Math.Min(200, value));
                if (_renderScale == scale)
                {
                    return;
                }
                _renderScale = scale;
                if (!_deserializing && scale != 100)
                {
                    _outputMode = RenderingOutputMode.Intermediate;
                }
                MarkCustom();
            }
        }

        [JsonIgnore]
        [ReadOnlyOption]
        [ShowInSettingUIWhen(nameof(CompatibilityMode), false)]
        [ShowInSettingUIWhen(nameof(RenderQuality), RenderingQualityOption.Custom)]
        public MajSystemScalingStatus SystemScalingStatus => MajAndroidDisplayResolution.Status;

        [JsonIgnore]
        [ReadOnlyOption]
        [ShowInSettingUIWhen(nameof(CompatibilityMode), false)]
        [ShowInSettingUIWhen(nameof(RenderQuality), RenderingQualityOption.Custom)]
        public string DisplayBufferResolution => MajRenderDiagnostics.DisplayResolution;

        [JsonIgnore]
        [ReadOnlyOption]
        [ShowInSettingUIWhen(nameof(CompatibilityMode), false)]
        [ShowInSettingUIWhen(nameof(RenderQuality), RenderingQualityOption.Custom)]
        public string SceneRenderResolution => MajRenderDiagnostics.SceneResolution;

        [JsonIgnore]
        [ReadOnlyOption]
        [ShowInSettingUIWhen(nameof(CompatibilityMode), false)]
        [ShowInSettingUIWhen(nameof(RenderQuality), RenderingQualityOption.Custom)]
        public MajRenderOutputStatus CurrentOutputPath => MajRenderDiagnostics.OutputStatus;

        public bool DisplayRuntimeInfo { get; set; } = SettingDefaults.Rendering.DisplayRuntimeInfo;

#if UNITY_STANDALONE
        [HideInSettingUI]
        public string Resolution { get; set; } = SettingDefaults.Rendering.Resolution;

        [HideInSettingUI]
        public bool FullScreen { get; set; } = SettingDefaults.Rendering.FullScreen;

        [HideInSettingUI]
        public bool Topmost { get; set; } = SettingDefaults.Rendering.Topmost;
#endif

        public bool MainScreenTransform { get; set; } = SettingDefaults.Rendering.MainScreenTransform;

        [Step("0.01")]
        [Range("0.05", "1.5")]
        public float MainScreenScale { get; set; } = SettingDefaults.Rendering.MainScreenScale;

        [Step("0.01")]
        [Range("-1", "1")]
        public float MainScreenOffset { get; set; } = SettingDefaults.Rendering.MainScreenOffset;

        [HideInSettingUI]
        public float MainScreenCachedScreenCenterY { get; set; } = SettingDefaults.Rendering.MainScreenCachedScreenCenterY;

        [Step("0.01")]
        [Range("-5", "5")]
        public float SubDisplayOffset { get; set; } = SettingDefaults.Rendering.SubDisplayOffset;

        [Step("0.01")]
        public float SubDisplayScale { get; set; } = SettingDefaults.Rendering.SubDisplayScale;

        public GameplayScreenRotationAngleOption GameplayScreenRotationAngle { get; set; } = SettingDefaults.Rendering.GameplayScreenRotationAngle;

        public bool EnterCustomMode()
        {
            if (_quality == RenderingQualityOption.Custom)
            {
                return false;
            }
            _quality = RenderingQualityOption.Custom;
            return true;
        }

        public RenderingParameters GetEffectiveParameters()
        {
            return CompatibilityMode
                ? new RenderingParameters(50, 1, 2, false)
                : new RenderingParameters(_renderScale, (int)_antiAliasing, 2 - (int)_textureQuality,
                    _renderScale == 100 && _outputMode == RenderingOutputMode.Direct);
        }

        /// <summary>Gets the base artwork strategy after applying the temporary compatibility override.</summary>
        /// <returns>None in compatibility mode; All for either captured-backdrop style; otherwise the saved base strategy.</returns>
        public UiOutlineMode GetEffectiveUIOutlineMode()
        {
            if (CompatibilityMode)
            {
                return UiOutlineMode.None;
            }
            return _uiOutlineMode switch
            {
                UiOutlineMode.GaussianBlur or UiOutlineMode.LiquidGlass => UiOutlineMode.All,
                _ => _uiOutlineMode
            };
        }

        /// <summary>Gets whether the selected UI style requires same-frame captured backdrops.</summary>
        /// <returns>True only for GaussianBlur or LiquidGlass outside compatibility mode; All remains ordinary alpha blending.</returns>
        public bool GetEffectiveUIBackdropBlur()
        {
            return !CompatibilityMode && (_uiOutlineMode == UiOutlineMode.GaussianBlur || _uiOutlineMode == UiOutlineMode.LiquidGlass);
        }

        /// <summary>Gets the backdrop effect derived from the active unified UI style.</summary>
        /// <returns>LiquidGlass for that explicit UI style; GaussianBlur otherwise, including when capture is disabled.</returns>
        public UiBackdropStyle GetEffectiveUIBackdropStyle()
        {
            return _uiOutlineMode == UiOutlineMode.LiquidGlass ? UiBackdropStyle.LiquidGlass : UiBackdropStyle.GaussianBlur;
        }

        private void MarkCustom()
        {
            if (!_deserializing)
            {
                _quality = RenderingQualityOption.Custom;
            }
        }

        /// <summary>Applies the selected named preset without replacing manually saved Custom parameters.</summary>
        private void ApplyPreset()
        {
            if (_quality == RenderingQualityOption.Custom)
            {
                return;
            }
            _renderScale = 100;
            _antiAliasing = RenderingAntialiasingOption.Disabled;
            _textureQuality = _quality == RenderingQualityOption.EnergySaving
                ? RenderingTextureQualityOption.Medium : RenderingTextureQualityOption.High;
            _outputMode = RenderingOutputMode.Direct;
            _uiOutlineMode = _quality switch
            {
                RenderingQualityOption.EnergySaving => UiOutlineMode.None,
                RenderingQualityOption.Performance => UiOutlineMode.Performance,
                RenderingQualityOption.Refined => _uiBackdropStyle == UiBackdropStyle.LiquidGlass
                    ? UiOutlineMode.LiquidGlass : UiOutlineMode.GaussianBlur,
                _ => UiOutlineMode.All
            };
        }

        /// <summary>Preserves historical missing-field defaults while a saved Custom configuration is read.</summary>
        /// <param name="context">The serializer's streaming context.</param>
        [OnDeserializing]
        private void OnReading(StreamingContext context)
        {
            _deserializing = true;
            _hasSerializedUIOutlineMode = false;
            _legacyUIOutline = false;
            _legacyUIBackgroundMode = LegacyUiBackgroundMode.OriginalAlpha;
            _legacyUIBackdropBlur = false;
            _uiBackdropStyle = SettingDefaults.Rendering.UIBackdropStyle;
            // Named presets are reapplied by OnRead, so new or empty settings still use Beautiful.
            // A legacy Custom configuration must not gain MSAA or outlined UI merely by omitting a key.
            _antiAliasing = RenderingAntialiasingOption.Disabled;
        }

        [OnDeserialized]
        private void OnRead(StreamingContext context)
        {
            _deserializing = false;
            SystemResolutionScaling = Math.Max(10, Math.Min(100, SystemResolutionScaling));
            if (!Enum.IsDefined(typeof(RenderingQualityOption), _quality))
            {
                _quality = SettingDefaults.Rendering.RenderQuality;
            }
            if (!Enum.IsDefined(typeof(RenderingAntialiasingOption), _antiAliasing))
            {
                _antiAliasing = SettingDefaults.Rendering.AntiAliasing;
            }
            if (!Enum.IsDefined(typeof(RenderingTextureQualityOption), _textureQuality))
            {
                _textureQuality = SettingDefaults.Rendering.TextureQuality;
            }
            if (!Enum.IsDefined(typeof(RenderingOutputMode), _outputMode))
            {
                _outputMode = SettingDefaults.Rendering.OutputMode;
            }
            if (!Enum.IsDefined(typeof(UiBackdropStyle), _uiBackdropStyle))
            {
                _uiBackdropStyle = SettingDefaults.Rendering.UIBackdropStyle;
            }
            if (_quality == RenderingQualityOption.Custom)
            {
                if (!_hasSerializedUIOutlineMode)
                {
                    _uiOutlineMode = _legacyUIOutline ? UiOutlineMode.All
                        : _legacyUIBackgroundMode == LegacyUiBackgroundMode.OutlinedAlpha
                            ? UiOutlineMode.Performance : UiOutlineMode.None;
                    if (_uiOutlineMode == UiOutlineMode.All && _legacyUIBackdropBlur)
                    {
                        _uiOutlineMode = _uiBackdropStyle == UiBackdropStyle.LiquidGlass
                            ? UiOutlineMode.LiquidGlass : UiOutlineMode.GaussianBlur;
                    }
                }
                else if (!Enum.IsDefined(typeof(UiOutlineMode), _uiOutlineMode))
                {
                    _uiOutlineMode = UiOutlineMode.None;
                }
            }
            _renderScale = Math.Max(10, Math.Min(200, _renderScale));
            ApplyPreset();
            if (_renderScale != 100)
            {
                _outputMode = RenderingOutputMode.Intermediate;
            }
        }

        /// <summary>Names values accepted only when reading the retired independent background key.</summary>
        [Preserve]
        private enum LegacyUiBackgroundMode
        {
            /// <summary>Represents the original alpha-blended background artwork.</summary>
            OriginalAlpha,

            /// <summary>Represents the performance style's outlined alpha-blended backgrounds.</summary>
            OutlinedAlpha,

            /// <summary>Represents the no-outline style's hard-cutout backgrounds.</summary>
            Cutoff
        }
    }

    public readonly struct RenderingParameters
    {
        public readonly int RenderScale;

        public readonly int MSAASamples;

        public readonly int MipmapBias;

        public readonly bool ResolveToScreen;

        public RenderingParameters(int renderScale, int msaaSamples, int mipmapBias, bool resolveToScreen)
        {
            RenderScale = renderScale;
            MSAASamples = msaaSamples;
            MipmapBias = mipmapBias;
            ResolveToScreen = resolveToScreen;
        }
    }
}
