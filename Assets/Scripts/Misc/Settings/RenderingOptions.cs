using System;
using System.Runtime.Serialization;
using MajdataPlay.Rendering;
using MajdataPlay.Settings.OptionEnumerators;
using Newtonsoft.Json;
using UnityEngine.Scripting;

namespace MajdataPlay.Settings
{
    // 现行渲染预设与 Unity Quality 等级分开；旧 JSON 的六个画质值只在内部迁移。
    [Preserve]
    public enum RenderingQualityOption { EnergySaving, Performance, Beautiful, Refined, Custom }
    [Preserve]
    public enum RenderingAntialiasingOption { Disabled = 1, MSAA2x = 2, MSAA4x = 4, MSAA8x = 8 }
    [Preserve]
    public enum RenderingTextureQualityOption { Low, Medium, High }
    [Preserve]
    public enum RenderingOutputMode { Direct, Intermediate }

    /// <summary>保存用户选择；兼容模式只覆盖有效参数，不覆盖关闭后需要恢复的原设置。</summary>
    [Preserve]
    public sealed class RenderingOptions
    {
        private RenderingQualityOption _quality = RenderingQualityOption.Performance;
        private RenderingAntialiasingOption _antiAliasing = RenderingAntialiasingOption.Disabled;
        private RenderingTextureQualityOption _textureQuality = RenderingTextureQualityOption.High;
        private RenderingOutputMode _outputMode = RenderingOutputMode.Direct;
        private int _renderScale = 100;
        private bool _deserializing;
        private int _fpsLimit = 120;
        private bool _vSync = true;
        private bool _hasFPSLimit, _hasVSync;

        // 新键优先；旧 Display 的独立字段按缺项迁移，不依赖 JSON 属性顺序。
        internal void MigrateFramePacing(int? fpsLimit, bool? vSync)
        {
            if (!_hasFPSLimit && fpsLimit.HasValue) FPSLimit = fpsLimit.Value;
            if (!_hasVSync && vSync.HasValue) VSync = vSync.Value;
        }

        [OptionEnumerator(typeof(RenderingEnumSettingEnumerator))]
        public RenderingQualityOption RenderQuality
        {
            get => _quality;
            set
            {
                _quality = value;
                if (!_deserializing) ApplyPreset();
            }
        }

        [Step("1")]
        [Range("-1", null, HasMax = false, HasMin = true)]
        [OptionEnumerator(typeof(EngineNumberSettingEnumerator))]
        public int FPSLimit
        {
            get => _fpsLimit;
            set { _fpsLimit = Math.Max(-1, value); _hasFPSLimit = true; }
        }
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        // 仅移动端 Player 不使用 vSyncCount；Editor 不能跟随目标平台隐藏。
        [HideInSettingUI]
#endif
        [OptionEnumerator(typeof(EngineBooleanSettingEnumerator))]
        public bool VSync
        {
            get => _vSync;
            set { _vSync = value; _hasVSync = true; }
        }

        [OptionEnumerator(typeof(RenderingEnumSettingEnumerator))]
        public RenderingAntialiasingOption AntiAliasing
        {
            get => _antiAliasing;
            set
            {
                if (_antiAliasing == value) return;
                _antiAliasing = value;
                MarkCustom();
            }
        }

        [OptionEnumerator(typeof(RenderingEnumSettingEnumerator))]
        public RenderingTextureQualityOption TextureQuality
        {
            get => _textureQuality;
            set
            {
                if (_textureQuality == value) return;
                _textureQuality = value;
                MarkCustom();
            }
        }

        [OptionEnumerator(typeof(RenderingEnumSettingEnumerator))]
        public RenderingOutputMode OutputMode
        {
            get => _outputMode;
            set
            {
                // 缩放需要一张不同尺寸的场景颜色；非 100% 不允许选成直接输出。
                if (!_deserializing && _renderScale != 100) value = RenderingOutputMode.Intermediate;
                if (_outputMode == value) return;
                _outputMode = value;
                MarkCustom();
            }
        }

        [OptionEnumerator(typeof(RenderingBooleanSettingEnumerator))]
        public bool CompatibilityMode { get; set; }

        // 独立显示缓冲比例，保存到 JSON；不改预设，与场景比例相乘。
        [Step("5")]
        [Range("10", "100", HasMax = true, HasMin = true)]
        [OptionEnumerator(typeof(RenderingNumberSettingEnumerator))]
        public int SystemResolutionScaling { get; set; } = 100;

        // 仅在自定义菜单显示；预设及兼容模式仍统一控制此值。
        [Step("5")]
        [Range("10", "200", HasMax = true, HasMin = true)]
        [OptionEnumerator(typeof(RenderingNumberSettingEnumerator))]
        public int RenderScale
        {
            get => _renderScale;
            set
            {
                int scale = Math.Max(10, Math.Min(200, value));
                if (_renderScale == scale) return;
                _renderScale = scale;
                if (!_deserializing && scale != 100) _outputMode = RenderingOutputMode.Intermediate;
                MarkCustom();
            }
        }

        [JsonIgnore, ReadOnlyOption]
        public MajSystemScalingStatus SystemScalingStatus => MajAndroidDisplayResolution.Status;
        [JsonIgnore, ReadOnlyOption]
        public string DisplayBufferResolution => MajRenderDiagnostics.DisplayResolution;
        [JsonIgnore, ReadOnlyOption]
        public string SceneRenderResolution => MajRenderDiagnostics.SceneResolution;

        // 仅显示最近一次主屏提交采用的路径，不作为配置保存，也不修改输出偏好。
        // 无 setter 的枚举沿用只读枚举显示和字体预热；主屏状态变化由 OptionData 自动刷新。
        [JsonIgnore]
        [ReadOnlyOption]
        public MajRenderOutputStatus CurrentOutputPath => MajRenderDiagnostics.OutputStatus;

        private void MarkCustom()
        {
            if (!_deserializing) _quality = RenderingQualityOption.Custom;
        }

        /// <summary>隐藏入口只切换模式，保留当前预设的实际参数；保存的 Custom 就是下次入口。</summary>
        public bool EnterCustomMode()
        {
            if (_quality == RenderingQualityOption.Custom) return false;
            _quality = RenderingQualityOption.Custom;
            return true;
        }

        private void ApplyPreset()
        {
            if (_quality == RenderingQualityOption.Custom) return;
            _renderScale = _quality == RenderingQualityOption.Refined ? 150 : 100;
            _antiAliasing = _quality == RenderingQualityOption.Beautiful || _quality == RenderingQualityOption.Refined
                ? RenderingAntialiasingOption.MSAA4x : RenderingAntialiasingOption.Disabled;
            _textureQuality = _quality == RenderingQualityOption.EnergySaving
                ? RenderingTextureQualityOption.Medium : RenderingTextureQualityOption.High;
            _outputMode = _quality == RenderingQualityOption.Refined
                ? RenderingOutputMode.Intermediate : RenderingOutputMode.Direct;
        }

        /// <summary>只计算有效值，不把临时兼容参数写回用户的自定义设置。</summary>
        public RenderingParameters GetEffectiveParameters()
        {
            return CompatibilityMode
                ? new RenderingParameters(50, 1, 2, false)
                : new RenderingParameters(_renderScale, (int)_antiAliasing, 2 - (int)_textureQuality,
                    _renderScale == 100 && _outputMode == RenderingOutputMode.Direct);
        }

        [OnDeserializing]
        private void OnReading(StreamingContext context) => _deserializing = true;

        [OnDeserialized]
        private void OnRead(StreamingContext context)
        {
            _deserializing = false;
            SystemResolutionScaling = Math.Max(10, Math.Min(100, SystemResolutionScaling));
            if (!Enum.IsDefined(typeof(RenderingQualityOption), _quality)) _quality = RenderingQualityOption.Performance;
            if (!Enum.IsDefined(typeof(RenderingAntialiasingOption), _antiAliasing)) _antiAliasing = RenderingAntialiasingOption.Disabled;
            if (!Enum.IsDefined(typeof(RenderingTextureQualityOption), _textureQuality)) _textureQuality = RenderingTextureQualityOption.High;
            if (!Enum.IsDefined(typeof(RenderingOutputMode), _outputMode)) _outputMode = RenderingOutputMode.Direct;
            _renderScale = Math.Max(10, Math.Min(200, _renderScale));
            // JSON 属性顺序不应决定哪个预设生效。自定义保留细项，其他档位恢复其完整参数。
            ApplyPreset();
            if (_renderScale != 100) _outputMode = RenderingOutputMode.Intermediate;
        }

        internal static RenderingOptions FromLegacy(RenderQualityOption? legacyQuality, int? legacyRenderScale)
        {
            // 空文件或只有其它 Display 设置时，使用当前 Performance / 100% 默认值。
            // 只有旧键实际提供了值才迁移，避免桌面旧 Medium 默认把新配置变成 Custom / 2x。
            if (!legacyQuality.HasValue && !legacyRenderScale.HasValue) return new RenderingOptions();

            // 部分旧配置仍按当时的平台默认补齐缺项；这些值不再参与现行菜单初始化。
#if UNITY_ANDROID || UNITY_IOS
            var quality = legacyQuality ?? RenderQualityOption.Low;
#else
            var quality = legacyQuality ?? RenderQualityOption.Medium;
#endif
            int renderScale = legacyRenderScale ?? 100;
            var result = new RenderingOptions
            {
                _quality = RenderingQualityOption.Custom,
                _renderScale = Math.Max(10, Math.Min(200, renderScale)),
                _antiAliasing = quality == RenderQualityOption.Medium ? RenderingAntialiasingOption.MSAA2x
                    : quality == RenderQualityOption.High ? RenderingAntialiasingOption.MSAA4x
                    : quality == RenderQualityOption.VeryHigh || quality == RenderQualityOption.Ultra
                        ? RenderingAntialiasingOption.MSAA8x : RenderingAntialiasingOption.Disabled,
                _textureQuality = quality == RenderQualityOption.VeryLow
                    ? RenderingTextureQualityOption.Medium : RenderingTextureQualityOption.High
            };
            // 旧比例和 MSAA 能精确对应新档位时才显示档位名；否则作为自定义保留。
            if (result._renderScale == 100)
            {
                if (quality == RenderQualityOption.VeryLow) result._quality = RenderingQualityOption.EnergySaving;
                else if (quality == RenderQualityOption.Low) result._quality = RenderingQualityOption.Performance;
                else if (quality == RenderQualityOption.High) result._quality = RenderingQualityOption.Beautiful;
            }
            if (result._renderScale != 100) result._outputMode = RenderingOutputMode.Intermediate;
            return result;
        }
    }

    public readonly struct RenderingParameters
    {
        public readonly int RenderScale;
        public readonly int MSAASamples;
        // 在自动选择的纹理 LOD 上追加的级数，不裁掉或卸载高分辨率 mip。
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
