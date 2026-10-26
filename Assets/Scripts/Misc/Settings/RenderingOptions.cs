using System;
using System.Runtime.Serialization;
using MajdataPlay.Rendering;
using MajdataPlay.Settings.OptionEnumerators;
using Newtonsoft.Json;
using UnityEngine.Scripting;
#nullable enable
namespace MajdataPlay.Settings
{
    // 现行渲染预设与 Unity Quality 等级分开；旧 JSON 的六个画质值只在 GameSettingMigration 中迁移。
    /// <summary>
    /// Lists the rendering presets; <see cref="Custom"/> keeps the detailed parameters chosen by the user.
    /// </summary>
    [Preserve]
    public enum RenderingQualityOption
    {
        /// <summary>Renders at 100% without anti-aliasing and with medium texture detail.</summary>
        EnergySaving,
        /// <summary>Renders at 100% without anti-aliasing and with high texture detail.</summary>
        Performance,
        /// <summary>Renders at 100% with 4x MSAA and high texture detail.</summary>
        Beautiful,
        /// <summary>Renders at 150% through an intermediate target with 4x MSAA and high texture detail.</summary>
        Refined,
        /// <summary>Keeps the detailed parameters, which are only shown in the setting UI for this preset.</summary>
        Custom
    }

    /// <summary>
    /// Lists the anti-aliasing modes; each value is the MSAA sample count.
    /// </summary>
    [Preserve]
    public enum RenderingAntialiasingOption
    {
        /// <summary>Disables anti-aliasing.</summary>
        Disabled = 1,
        /// <summary>Uses 2x MSAA.</summary>
        MSAA2x = 2,
        /// <summary>Uses 4x MSAA.</summary>
        MSAA4x = 4,
        /// <summary>Uses 8x MSAA.</summary>
        MSAA8x = 8
    }

    /// <summary>
    /// Lists the texture detail levels.
    /// </summary>
    [Preserve]
    public enum RenderingTextureQualityOption
    {
        /// <summary>Samples two mipmap levels coarser than the automatic level.</summary>
        Low,
        /// <summary>Samples one mipmap level coarser than the automatic level.</summary>
        Medium,
        /// <summary>Samples the automatic mipmap level.</summary>
        High
    }

    /// <summary>
    /// Lists how the scene is presented on the screen.
    /// </summary>
    [Preserve]
    public enum RenderingOutputMode
    {
        /// <summary>Resolves the scene directly to the screen, which requires a render scale of 100%.</summary>
        Direct,
        /// <summary>Renders the scene to an intermediate target that is then copied to the screen.</summary>
        Intermediate
    }

    /// <summary>
    /// Contains the rendering quality, frame pacing and output options.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The options store what the user selected. <see cref="CompatibilityMode"/> only overrides the effective
    /// parameters returned by <see cref="GetEffectiveParameters"/>, so the selection is restored when it is turned off.
    /// </para>
    /// <para>
    /// The setting UI always shows the frame pacing options and the compatibility switch. The preset selector is hidden
    /// in compatibility mode, and the detailed parameters and diagnostics are only shown for the <see cref="RenderingQualityOption.Custom"/>
    /// preset outside compatibility mode, as declared by <see cref="ShowInSettingUIWhenAttribute"/>.
    /// </para>
    /// </remarks>
    [Preserve]
    public sealed class RenderingOptions
    {
        private RenderingQualityOption _quality = RenderingQualityOption.Performance;
        private RenderingAntialiasingOption _antiAliasing = RenderingAntialiasingOption.Disabled;
        private RenderingTextureQualityOption _textureQuality = RenderingTextureQualityOption.High;
        private RenderingOutputMode _outputMode = RenderingOutputMode.Direct;
        private int _renderScale = 100;
        private int _fpsLimit = 120;
        // 反序列化期间不应用预设，也不把细项改为 Custom；JSON 属性顺序由 OnRead 统一处理。
        private bool _deserializing;

        /// <summary>
        /// Gets or sets the rendering preset. Selecting a preset other than <see cref="RenderingQualityOption.Custom"/>
        /// replaces the detailed parameters with the parameters of the preset.
        /// </summary>
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

        /// <summary>
        /// Gets or sets the maximum frame rate; -1 removes the limit and smaller values are raised to -1.
        /// </summary>
        [Step("1")]
        [Range("-1", null)]
        [OptionEnumerator(typeof(EngineNumberSettingEnumerator))]
        public int FPSLimit
        {
            get => _fpsLimit;
            set => _fpsLimit = Math.Max(-1, value);
        }

        /// <summary>
        /// Gets or sets whether vertical sync is enabled.
        /// </summary>
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        // 仅移动端 Player 不使用 vSyncCount；Editor 不能跟随目标平台隐藏。
        [HideInSettingUI]
#endif
        [OptionEnumerator(typeof(EngineBooleanSettingEnumerator))]
        public bool VSync { get; set; } = true;

        /// <summary>
        /// Gets or sets the anti-aliasing mode; changing it switches to <see cref="RenderingQualityOption.Custom"/>.
        /// </summary>
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

        /// <summary>
        /// Gets or sets the texture detail level; changing it switches to <see cref="RenderingQualityOption.Custom"/>.
        /// </summary>
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

        /// <summary>
        /// Gets or sets the preferred output mode; changing it switches to <see cref="RenderingQualityOption.Custom"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="RenderingOutputMode.Direct"/> is rejected while <see cref="RenderScale"/> is not 100%, because a
        /// scaled scene needs an intermediate target of a different size.
        /// </remarks>
        [ShowInSettingUIWhen(nameof(CompatibilityMode), false)]
        [ShowInSettingUIWhen(nameof(RenderQuality), RenderingQualityOption.Custom)]
        [OptionEnumerator(typeof(RenderingEnumSettingEnumerator))]
        public RenderingOutputMode OutputMode
        {
            get => _outputMode;
            set
            {
                // 缩放需要一张不同尺寸的场景颜色；非 100% 不允许选成直接输出。
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

        /// <summary>
        /// Gets or sets whether the conservative compatibility parameters temporarily replace the selected ones.
        /// </summary>
        [OptionEnumerator(typeof(RenderingBooleanSettingEnumerator))]
        public bool CompatibilityMode { get; set; }

        /// <summary>
        /// Gets or sets the system display buffer resolution in percent, from 10 to 100.
        /// </summary>
        /// <remarks>
        /// The value is independent of the preset and multiplies with <see cref="RenderScale"/>. It is restored to the
        /// valid range when settings are read.
        /// </remarks>
        [Step("5")]
        [Range("10", "100")]
        [ShowInSettingUIWhen(nameof(CompatibilityMode), false)]
        [ShowInSettingUIWhen(nameof(RenderQuality), RenderingQualityOption.Custom)]
        [OptionEnumerator(typeof(RenderingNumberSettingEnumerator))]
        public int SystemResolutionScaling { get; set; } = 100;

        /// <summary>
        /// Gets or sets the scene render resolution in percent, from 10 to 200; changing it switches to
        /// <see cref="RenderingQualityOption.Custom"/>, and any value other than 100% forces intermediate output.
        /// </summary>
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

        /// <summary>
        /// Gets whether system resolution scaling is supported and active; shown for diagnostics and never saved.
        /// </summary>
        [JsonIgnore]
        [ReadOnlyOption]
        [ShowInSettingUIWhen(nameof(CompatibilityMode), false)]
        [ShowInSettingUIWhen(nameof(RenderQuality), RenderingQualityOption.Custom)]
        public MajSystemScalingStatus SystemScalingStatus => MajAndroidDisplayResolution.Status;

        /// <summary>
        /// Gets the actual display buffer resolution; shown for diagnostics and never saved.
        /// </summary>
        [JsonIgnore]
        [ReadOnlyOption]
        [ShowInSettingUIWhen(nameof(CompatibilityMode), false)]
        [ShowInSettingUIWhen(nameof(RenderQuality), RenderingQualityOption.Custom)]
        public string DisplayBufferResolution => MajRenderDiagnostics.DisplayResolution;

        /// <summary>
        /// Gets the actual scene render resolution; shown for diagnostics and never saved.
        /// </summary>
        [JsonIgnore]
        [ReadOnlyOption]
        [ShowInSettingUIWhen(nameof(CompatibilityMode), false)]
        [ShowInSettingUIWhen(nameof(RenderQuality), RenderingQualityOption.Custom)]
        public string SceneRenderResolution => MajRenderDiagnostics.SceneResolution;

        // 无 setter 的枚举沿用只读枚举显示和字体预热；主屏状态变化由 OptionData 自动刷新。
        /// <summary>
        /// Gets the output path taken by the latest main screen submission; shown for diagnostics, never saved, and
        /// independent of the preferred <see cref="OutputMode"/>.
        /// </summary>
        [JsonIgnore]
        [ReadOnlyOption]
        [ShowInSettingUIWhen(nameof(CompatibilityMode), false)]
        [ShowInSettingUIWhen(nameof(RenderQuality), RenderingQualityOption.Custom)]
        public MajRenderOutputStatus CurrentOutputPath => MajRenderDiagnostics.OutputStatus;

        /// <summary>
        /// Switches to the <see cref="RenderingQualityOption.Custom"/> preset while keeping the current parameters, as
        /// done by the hidden gesture of the setting UI.
        /// </summary>
        /// <returns>True when the preset changed; false when it already was <see cref="RenderingQualityOption.Custom"/>.</returns>
        public bool EnterCustomMode()
        {
            if (_quality == RenderingQualityOption.Custom)
            {
                return false;
            }
            _quality = RenderingQualityOption.Custom;
            return true;
        }

        /// <summary>
        /// Computes the parameters the renderer applies, without writing the compatibility override back to the options.
        /// </summary>
        /// <returns>The compatibility parameters in <see cref="CompatibilityMode"/>; otherwise the selected parameters.</returns>
        public RenderingParameters GetEffectiveParameters()
        {
            return CompatibilityMode
                ? new RenderingParameters(50, 1, 2, false)
                : new RenderingParameters(_renderScale, (int)_antiAliasing, 2 - (int)_textureQuality,
                    _renderScale == 100 && _outputMode == RenderingOutputMode.Direct);
        }

        /// <summary>
        /// Switches to <see cref="RenderingQualityOption.Custom"/> after a detailed parameter was edited.
        /// </summary>
        private void MarkCustom()
        {
            if (!_deserializing)
            {
                _quality = RenderingQualityOption.Custom;
            }
        }

        /// <summary>
        /// Replaces the detailed parameters with the parameters of the selected preset; <see cref="RenderingQualityOption.Custom"/> keeps them.
        /// </summary>
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

        /// <summary>
        /// Stops presets and detailed parameters from affecting each other while the stored values are read.
        /// </summary>
        /// <param name="context">The serialization context, which is not used.</param>
        [OnDeserializing]
        private void OnReading(StreamingContext context)
        {
            _deserializing = true;
        }

        /// <summary>
        /// Repairs out-of-range values and applies the stored preset once every value has been read.
        /// </summary>
        /// <param name="context">The serialization context, which is not used.</param>
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
            // JSON 属性顺序不应决定哪个预设生效。自定义保留细项，其他档位恢复其完整参数。
            ApplyPreset();
            if (_renderScale != 100)
            {
                _outputMode = RenderingOutputMode.Intermediate;
            }
        }
    }

    /// <summary>
    /// Describes the rendering parameters the renderer applies.
    /// </summary>
    public readonly struct RenderingParameters
    {
        /// <summary>
        /// The scene render resolution in percent.
        /// </summary>
        public readonly int RenderScale;

        /// <summary>
        /// The MSAA sample count; 1 disables anti-aliasing.
        /// </summary>
        public readonly int MSAASamples;

        // 在自动选择的纹理 LOD 上追加的级数，不裁掉或卸载高分辨率 mip。
        /// <summary>
        /// The number of mipmap levels added to the automatically selected texture level.
        /// </summary>
        public readonly int MipmapBias;

        /// <summary>
        /// Whether the scene is resolved directly to the screen instead of through an intermediate target.
        /// </summary>
        public readonly bool ResolveToScreen;

        /// <summary>
        /// Initializes a new instance of the <see cref="RenderingParameters"/> struct.
        /// </summary>
        /// <param name="renderScale">The scene render resolution in percent.</param>
        /// <param name="msaaSamples">The MSAA sample count; 1 disables anti-aliasing.</param>
        /// <param name="mipmapBias">The number of mipmap levels added to the automatically selected texture level.</param>
        /// <param name="resolveToScreen">Whether the scene is resolved directly to the screen.</param>
        public RenderingParameters(int renderScale, int msaaSamples, int mipmapBias, bool resolveToScreen)
        {
            RenderScale = renderScale;
            MSAASamples = msaaSamples;
            MipmapBias = mipmapBias;
            ResolveToScreen = resolveToScreen;
        }
    }
}
