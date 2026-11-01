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
        private RenderingQualityOption _quality = RenderingQualityOption.Performance;
        private RenderingAntialiasingOption _antiAliasing = RenderingAntialiasingOption.Disabled;
        private RenderingTextureQualityOption _textureQuality = RenderingTextureQualityOption.High;
        private RenderingOutputMode _outputMode = RenderingOutputMode.Direct;
        private int _renderScale = 100;
        private int _fpsLimit = 120;
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
        public bool VSync { get; set; } = true;

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
        public bool CompatibilityMode { get; set; }

        [Step("5")]
        [Range("10", "100")]
        [ShowInSettingUIWhen(nameof(CompatibilityMode), false)]
        [ShowInSettingUIWhen(nameof(RenderQuality), RenderingQualityOption.Custom)]
        [OptionEnumerator(typeof(RenderingNumberSettingEnumerator))]
        public int SystemResolutionScaling { get; set; } = 100;

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

        public bool DisplayRuntimeInfo { get; set; } = true;

#if UNITY_STANDALONE
        [HideInSettingUI]
        public string Resolution { get; set; } = "1080x1920";

        [HideInSettingUI]
        public bool FullScreen { get; set; } = true;

        [HideInSettingUI]
        public bool Topmost { get; set; } = false;
#endif

#if UNITY_ANDROID || UNITY_IOS
        public bool MainScreenTransform { get; set; } = true;
#else
        public bool MainScreenTransform { get; set; } = false;
#endif

        [Step("0.01")]
        [Range("0.05", "1.5")]
        public float MainScreenScale { get; set; } = 1f;

        [Step("0.01")]
        [Range("-1", "1")]
        public float MainScreenOffset { get; set; } = 1f;

        [HideInSettingUI]
        public float MainScreenCachedScreenCenterY { get; set; } = 960f;

        [Step("0.01")]
        [Range("-5", "5")]
        public float SubDisplayOffset { get; set; } = 0f;

        [Step("0.01")]
        public float SubDisplayScale { get; set; } = 1f;

        public GameplayScreenRotationAngleOption GameplayScreenRotationAngle { get; set; } = GameplayScreenRotationAngleOption.Zero;

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

        private void MarkCustom()
        {
            if (!_deserializing)
            {
                _quality = RenderingQualityOption.Custom;
            }
        }

        private void ApplyPreset()
        {
            if (_quality == RenderingQualityOption.Custom)
            {
                return;
            }
            _renderScale = _quality == RenderingQualityOption.Refined ? 150 : 100;
            _antiAliasing = _quality == RenderingQualityOption.Beautiful || _quality == RenderingQualityOption.Refined
                ? RenderingAntialiasingOption.MSAA4x : RenderingAntialiasingOption.Disabled;
            _textureQuality = _quality == RenderingQualityOption.EnergySaving
                ? RenderingTextureQualityOption.Medium : RenderingTextureQualityOption.High;
            _outputMode = _quality == RenderingQualityOption.Refined
                ? RenderingOutputMode.Intermediate : RenderingOutputMode.Direct;
        }

        [OnDeserializing]
        private void OnReading(StreamingContext context)
        {
            _deserializing = true;
        }

        [OnDeserialized]
        private void OnRead(StreamingContext context)
        {
            _deserializing = false;
            SystemResolutionScaling = Math.Max(10, Math.Min(100, SystemResolutionScaling));
            if (!Enum.IsDefined(typeof(RenderingQualityOption), _quality))
            {
                _quality = RenderingQualityOption.Performance;
            }
            if (!Enum.IsDefined(typeof(RenderingAntialiasingOption), _antiAliasing))
            {
                _antiAliasing = RenderingAntialiasingOption.Disabled;
            }
            if (!Enum.IsDefined(typeof(RenderingTextureQualityOption), _textureQuality))
            {
                _textureQuality = RenderingTextureQualityOption.High;
            }
            if (!Enum.IsDefined(typeof(RenderingOutputMode), _outputMode))
            {
                _outputMode = RenderingOutputMode.Direct;
            }
            _renderScale = Math.Max(10, Math.Min(200, _renderScale));
            ApplyPreset();
            if (_renderScale != 100)
            {
                _outputMode = RenderingOutputMode.Intermediate;
            }
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
