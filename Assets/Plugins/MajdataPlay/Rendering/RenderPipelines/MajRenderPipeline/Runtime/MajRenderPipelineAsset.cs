using UnityEngine;
using UnityEngine.Rendering;

#nullable enable

namespace MajdataPlay.Rendering
{
    /// <summary>0 表示跟随游戏画质；其余值就是主场景请求的 MSAA 样本数。</summary>
    public enum MajMSAAMode
    {
        UseQualitySettings = 0,
        Disabled = 1,
        MSAA2x = 2,
        MSAA4x = 4,
        MSAA8x = 8
    }

    /// <summary>
    /// 管线的配置资产。它只保存选项，真正的逐帧渲染由 MajRenderPipeline 执行。
    /// 分工对应参考项目里的 CustomRenderPipelineAsset。
    /// </summary>
    [CreateAssetMenu(menuName = "Rendering/Maj Render Pipeline", fileName = "MajRenderPipeline")]
    public sealed class MajRenderPipelineAsset : RenderPipelineAsset<MajRenderPipeline>
    {
        [SerializeField, Tooltip("启用 CPU 动态合批。是否更快需要实测，首版默认关闭。")]
        private bool _useDynamicBatching = false;

        [SerializeField, Tooltip("允许符合条件的 Mesh 使用 GPU Instancing；不会自动把全部 Sprite 合成一个批次。")]
        private bool _useGPUInstancing = true;

        [SerializeField, Tooltip("启用 SRP Batcher；Shader 和 Renderer 仍须满足它的兼容要求。")]
        private bool _useSRPBatcher = true;

        [SerializeField, Tooltip("对单台全屏 Game 相机启用原生 RenderPass：显式 Resolve，并丢弃帧末不再使用的深度/Stencil。关闭可用于对照旧路径。")]
        private bool _optimizeAttachments = true;

        [SerializeField, Tooltip("100% 分辨率时，在满足条件的 Vulkan/iOS Metal Gamma 屏幕上尝试直接 Resolve，省去中间颜色 RT 和 FinalCopy。不满足条件的后端或相机保留普通 RT + FinalCopy，MSAA 档位不变；游戏的渲染设置可在运行时覆盖此选项。")]
        private bool _resolveToScreen = true;

        [SerializeField, Tooltip("控制独立场景附件的 MSAA。可跟随 Unity Quality，或由各个 MRP 预设显式指定；相机禁止 MSAA 时使用 1x。实际样本数按设备支持值选择。")]
        private MajMSAAMode _msaaMode = MajMSAAMode.UseQualitySettings;

        [SerializeField, Range(0.1f, 2f), Tooltip("预设的场景分辨率比例，游戏设置可在运行时覆盖。0.5 表示宽高各减半，1.5 表示超采样后缩小；Overlay UI 仍使用最终输出分辨率。")]
        private float _renderScale = 1f;

        // 游戏设置只覆盖运行时值，不改写序列化预设，避免 Play 后把 UltraLow 的 50% 存成 100%。
        [System.NonSerialized] private float? _renderScaleOverride;
        [System.NonSerialized] private MajMSAAMode? _msaaModeOverride;
        [System.NonSerialized] private bool? _resolveToScreenOverride;
        [System.NonSerialized] private bool _forceIntermediateOutput;
        [System.NonSerialized] private int _textureMipmapBias;

        public float DefaultRenderScale => MajRenderScale.Clamp(_renderScale);
        public float RenderScale
        {
            get => _renderScaleOverride ?? DefaultRenderScale;
            set => _renderScaleOverride = MajRenderScale.Clamp(value);
        }

        /// <summary>当前请求的 MSAA；设备支持值仍由附件创建时选择。</summary>
        public MajMSAAMode MsaaMode => _msaaModeOverride ?? _msaaMode;

        /// <summary>当前是否允许直接 Resolve；后端、尺寸和相机兼容检查仍然生效。</summary>
        public bool ResolveToScreen => _resolveToScreenOverride ?? _resolveToScreen;

        /// <summary>游戏选择兼容输出时，即使 100% / 1x 也使用中间颜色 RT 和最终复制。</summary>
        public bool ForceIntermediateOutput => _forceIntermediateOutput;

        /// <summary>纹理精度的采样 LOD 偏移；不改变纹理尺寸、mip 驻留或场景附件。</summary>
        public int TextureMipmapBias => _textureMipmapBias;

        /// <summary>
        /// 游戏渲染设置的运行时覆盖。只修改非序列化值，不改写预设或重新创建管线。
        /// 已有渲染器在下一次相机渲染时读取新值，附件仅在实际配置改变时重建。
        /// </summary>
        public void ConfigureRuntimeRendering(float renderScale, MajMSAAMode msaaMode, bool resolveToScreen,
            int textureMipmapBias = 0)
        {
            _renderScaleOverride = MajRenderScale.Clamp(renderScale);
            _msaaModeOverride = msaaMode == MajMSAAMode.UseQualitySettings
                || msaaMode == MajMSAAMode.Disabled || msaaMode == MajMSAAMode.MSAA2x
                || msaaMode == MajMSAAMode.MSAA4x || msaaMode == MajMSAAMode.MSAA8x
                ? msaaMode : MajMSAAMode.Disabled;
            _resolveToScreenOverride = resolveToScreen;
            // 旧资产的 Resolve 关闭仅禁止 MSAA 直出；游戏菜单的兼容输出明确要求 RT + Copy。
            // 单独保留运行时标记，避免改变未使用新设置时原有的 100% / 1x 直绘行为。
            _forceIntermediateOutput = !resolveToScreen;
            _textureMipmapBias = Mathf.Clamp(textureMipmapBias, 0, 2);
        }

        /// <summary>取消整组游戏运行时覆盖，恢复本资产的序列化默认值。</summary>
        public void ResetRuntimeRenderingOverrides()
        {
            _renderScaleOverride = null;
            _msaaModeOverride = null;
            _resolveToScreenOverride = null;
            _forceIntermediateOutput = false;
            _textureMipmapBias = 0;
        }

        /// <summary>切换画质时恢复资产默认值；留在同档修改滑条时保留用户自定义比例。</summary>
        public void ResetRenderScaleOverride() => _renderScaleOverride = null;

        // 必须序列化引用，确保独立 Player 构建保留这个隐藏 Shader。
        // Shader 只使用 Unity 内置 include，不依赖 URP/Core RP 包。
        [SerializeField, HideInInspector] private Shader _finalCopyShader = null!;

        // 保留现有无光照 Shader 的 SubShader 标签；它们的公共函数已迁至 MRP 本地 Library。
        // 标签名称本身不依赖 URP 包，也不需要为迁移改动所有材质的 Shader 名称。
        // 这不会创建 UniversalRenderPipeline 或执行 URP Feature；Live2D 通过可选的 MRP 适配器绘制。
        public override string renderPipelineShaderTag => "UniversalPipeline";

        protected override RenderPipeline CreatePipeline()
        {
            // Unity 在启用此资产时调用 RenderPipelineAsset.CreatePipeline。
            // 将配置传给独立的管线实例；这里不修改 Graphics/Quality 的资产引用。
            return new MajRenderPipeline(this, _useDynamicBatching, _useGPUInstancing, _useSRPBatcher,
                _optimizeAttachments, _finalCopyShader);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            _renderScale = MajRenderScale.Clamp(_renderScale);
            if (_finalCopyShader == null)
            {
                _finalCopyShader = Shader.Find("Hidden/Maj/FinalCopy");
            }
            // 保留 RenderPipelineAsset 的管线失效处理，Inspector 更改后重新创建实例。
            base.OnValidate();
        }
#endif
    }
}
