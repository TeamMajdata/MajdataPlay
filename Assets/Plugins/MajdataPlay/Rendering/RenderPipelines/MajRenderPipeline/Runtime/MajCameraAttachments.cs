using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

#nullable enable

namespace MajdataPlay.Rendering
{
    /// <summary>
    /// 单主相机的附件生命周期。分辨率/样本数不变时复用 RT 和描述数组；不在每帧分配。
    /// 场景深度/Stencil 仅在一个原生 Pass 内使用，MSAA 颜色只 Resolve，不保存多采样结果。
    /// </summary>
    internal sealed class MajCameraAttachments : IDisposable
    {
        // Android Vulkan 的 CameraTarget 仅作为 Resolve 目标时，部分设备不会得到新的屏幕内容。
        // 使用显示缓冲的显式 RenderBuffer 句柄；平台/API 在进程内不变，只查询一次。
        private static readonly bool UseDisplayResolveTarget = Application.platform == RuntimePlatform.Android
            && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan;

        // Native Pass 接口使用 UTF-8 C 字符串，保留末尾 NUL；只在类型初始化时编码一次。
        private static readonly byte[] ScenePassName = System.Text.Encoding.UTF8.GetBytes("MRP.Scene\0");
        private static readonly byte[] FinalCopyPassName = System.Text.Encoding.UTF8.GetBytes("MRP.FinalCopy\0");

        private NativeArray<AttachmentDescriptor> _sceneAttachments =
            new NativeArray<AttachmentDescriptor>(2, Allocator.Persistent);
        private NativeArray<AttachmentDescriptor> _copyAttachments =
            new NativeArray<AttachmentDescriptor>(1, Allocator.Persistent);
        private NativeArray<SubPassDescriptor> _subPasses =
            new NativeArray<SubPassDescriptor>(1, Allocator.Persistent);

        private RenderTexture? _depthStencil;
        private RenderTexture? _msaaColor;
        private RenderTexture? _resolvedColor;
        private GraphicsFormat _colorFormat;
        private GraphicsFormat _depthFormat;
        private int _width;
        private int _height;
        private bool _allocationFailed;
        private bool _memorylessColorRejected;
        private int _requestedSamples;
        private ColorSpace _colorSpace;
        private bool _forceIntermediate;
        private bool _discardDepth;
        private bool _resolveToScreen;
        private FilterMode _copyFilter;

        internal int Samples { get; private set; }
        internal bool UsesIntermediateColor { get; private set; }
        internal bool ResolvesToScreen { get; private set; }
        internal RenderTexture ResolvedColor => _resolvedColor!;
        // 仅供保留 Store 的第三方绘制路径使用；颜色和深度的样本数、尺寸由本类统一选择。
        internal RenderTexture SceneColor => Samples > 1 ? _msaaColor! : _resolvedColor!;
        internal RenderTexture DepthStencil => _depthStencil!;

        internal MajCameraAttachments()
        {
            var outputs = new AttachmentIndexArray(1);
            outputs[0] = 0;
            _subPasses[0] = new SubPassDescriptor
            {
                colorOutputs = outputs,
                inputs = new AttachmentIndexArray(0),
                flags = SubPassFlags.None
            };
        }

        internal bool TryPrepare(int width, int height, int requestedSamples, bool canCopyResolvedColor,
            bool forceIntermediate, bool discardDepth, FilterMode copyFilter, bool resolveToScreen = false)
        {
            var colorSpace = QualitySettings.activeColorSpace;
            bool sameInputs = _width == width && _height == height && _requestedSamples == requestedSamples
                && _colorSpace == colorSpace && _forceIntermediate == forceIntermediate && _discardDepth == discardDepth
                && _resolveToScreen == resolveToScreen;
            // 稳定分辨率/画质下连设备格式及 MSAA 能力查询也复用，避免每帧进入驱动查询。
            // IsCreated 仍要检查，以便窗口/设备丢失 RT 后重新创建。
            if (sameInputs)
            {
                if (_allocationFailed || (UsesIntermediateColor && !canCopyResolvedColor)) return false;
                if (TexturesReady())
                {
                    if (_resolvedColor != null && _copyFilter != copyFilter)
                        _resolvedColor.filterMode = copyFilter;
                    _copyFilter = copyFilter;
                    return true;
                }
            }

            var colorFormat = SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);
            colorFormat = colorSpace == ColorSpace.Linear
                ? GraphicsFormatUtility.GetSRGBFormat(colorFormat)
                : GraphicsFormatUtility.GetLinearFormat(colorFormat);
            var depthFormat = SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil);
            if (width <= 0 || height <= 0 || colorFormat == GraphicsFormat.None
                || depthFormat == GraphicsFormat.None)
            {
                return false;
            }

            // 用实际颜色 + Depth/Stencil 组合询问设备，而不是假定每台设备支持 2/4/8x。
            var descriptor = new RenderTextureDescriptor(width, height)
            {
                graphicsFormat = colorFormat,
                depthStencilFormat = depthFormat,
                msaaSamples = requestedSamples,
                useMipMap = false,
                autoGenerateMips = false
            };
            int samples = Mathf.Max(1, SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor));
            // 调用端已排除屏幕转换/不支持的后端。只有原生全尺寸路径可把 Resolve 直接写入屏幕；
            // 缩放和需要保留深度的兼容路径仍分配单采样 RT，不能丢掉其 FinalCopy。
            bool directResolve = resolveToScreen && !forceIntermediate && discardDepth && samples > 1;
            bool useIntermediate = forceIntermediate || (samples > 1 && !directResolve);
            if ((samples > 1 && SystemInfo.supportsMultisampledTextures == 0)
                || (useIntermediate && !canCopyResolvedColor))
            {
                // 不能绑定多采样纹理或最终拷贝不可用时，在分配 RT 前回退，不擅自降低画质。
                return false;
            }

            // 同一附件配置下只尝试一次 memoryless；普通 RT 回退成功后，设备丢失 RT 也不反复试错。
            if (!sameInputs) _memorylessColorRejected = false;
            ReleaseTextures();
            _width = width;
            _height = height;
            Samples = samples;
            _colorFormat = colorFormat;
            _depthFormat = depthFormat;
            _allocationFailed = false;
            _requestedSamples = requestedSamples;
            _colorSpace = colorSpace;
            _forceIntermediate = forceIntermediate;
            _discardDepth = discardDepth;
            _resolveToScreen = resolveToScreen;
            UsesIntermediateColor = useIntermediate;
            ResolvesToScreen = directResolve;
            _copyFilter = copyFilter;

            // 保留 Stencil，供 UI Mask/SpriteMask 使用；帧末不采样，也不保存到下一帧。
            var depthDescriptor = descriptor;
            depthDescriptor.graphicsFormat = GraphicsFormat.None;
            depthDescriptor.msaaSamples = samples;
            depthDescriptor.bindMS = samples > 1;
            // 非原生路径要在后续 Gizmos 中继续读深度，不能使用 memoryless。
            depthDescriptor.memoryless = discardDepth && SystemInfo.supportsMemorylessTextures
                ? RenderTextureMemoryless.Depth : RenderTextureMemoryless.None;
            _depthStencil = CreateTexture(depthDescriptor, "MRP Scene DepthStencil");

            if (samples > 1)
            {
                var colorDescriptor = descriptor;
                colorDescriptor.depthStencilFormat = GraphicsFormat.None;
                colorDescriptor.msaaSamples = samples;
                colorDescriptor.bindMS = true; // 禁止隐式 resolve；由 attachment 指定唯一 resolve 目标。
                // 只丢弃 MSAA 样本存储，不标记 Color：Resolve 结果仍需写入屏幕或普通 1x RT。
                // 兼容路径跨 Pass 保存颜色，不能使用 memoryless，即使设备宣称支持也不请求。
                bool tryMemoryless = discardDepth && SystemInfo.supportsMemorylessTextures
                    && !_memorylessColorRejected;
                _msaaColor = CreateMSAAColor(colorDescriptor, tryMemoryless, out bool rejected);
                _memorylessColorRejected |= rejected;
            }

            if (useIntermediate)
            {
                // 50% + 1x 也需要场景颜色 RT；它没有 MSAA Resolve，但仍须放大到完整输出。
                var resolvedDescriptor = descriptor;
                resolvedDescriptor.depthStencilFormat = GraphicsFormat.None;
                resolvedDescriptor.msaaSamples = 1;
                resolvedDescriptor.bindMS = false;
                resolvedDescriptor.memoryless = RenderTextureMemoryless.None;
                _resolvedColor = CreateTexture(resolvedDescriptor, "MRP Scene Color");
                if (_resolvedColor != null) _resolvedColor.filterMode = copyFilter;
            }

            bool ready = TexturesReady();
            if (!ready)
            {
                ReleaseTextures();
                _allocationFailed = true;
            }
            // Android MSAA 兼容调查：仅在附件重建时记录，不在稳定帧执行或拼接字符串。
            // prepared 只代表资源创建通过，不能证明驱动实际执行 Resolve 后的画面正确。
            if (Application.platform == RuntimePlatform.Android && requestedSamples > 1)
            {
                Debug.Log($"[MRP.Attachments] prepared={ready}; device={SystemInfo.deviceModel}; "
                    + $"gpu={SystemInfo.graphicsDeviceName}; api={SystemInfo.graphicsDeviceType}; "
                    + $"driver={SystemInfo.graphicsDeviceVersion}; colorSpace={colorSpace}; "
                    + $"size={width}x{height}; requestedMSAA={requestedSamples}; actualMSAA={Samples}; "
                    + $"color={colorFormat}; depth={depthFormat}; directResolve={ResolvesToScreen}; "
                    + $"intermediateColor={UsesIntermediateColor}; "
                    + $"resolveTarget={(ResolvesToScreen ? (UseDisplayResolveTarget ? "DisplayColorBuffer" : "CameraTarget") : "IntermediateColor")}; "
                    + $"memorylessDepth={(_depthStencil != null ? _depthStencil.memorylessMode.ToString() : "missing")}; "
                    + $"memorylessMSAA={(_msaaColor != null ? _msaaColor.memorylessMode.ToString() : "missing")}; "
                    + $"actualDepthFormat={(_depthStencil != null ? _depthStencil.depthStencilFormat.ToString() : "missing")}; "
                    + $"depthBindMS={(_depthStencil != null && _depthStencil.bindTextureMS)}; "
                    + $"colorBindMS={(_msaaColor != null && _msaaColor.bindTextureMS)}; "
                    + $"backBufferMSAACap={SystemInfo.supportsMultisampledBackBuffer}; screenMSAARequest={Screen.msaaSamples}");
            }
            return ready;
        }

        private bool TexturesReady()
        {
            return _depthStencil != null && _depthStencil.IsCreated()
                && _depthStencil.width == _width && _depthStencil.height == _height
                && _depthStencil.antiAliasing == Samples
                && (Samples == 1 || (_msaaColor != null && _msaaColor.IsCreated()))
                // 直接 Resolve 没有 Shader 来转换格式，驱动替换 RT 格式时不能继续走这条路径。
                && (!ResolvesToScreen || (_msaaColor != null && _msaaColor.graphicsFormat == _colorFormat))
                && (!UsesIntermediateColor || (_resolvedColor != null && _resolvedColor.IsCreated()));
        }

        internal void BeginScene(CommandBuffer buffer, Color background)
        {
            var color = new AttachmentDescriptor(_colorFormat);
            if (Samples > 1)
            {
                color.ConfigureTarget(_msaaColor, false, false);
                // 直接 Resolve 不采样场景颜色，也不创建 _resolvedColor；保留 MSAA 样本数与 Stencil。
                var resolveTarget = ResolvesToScreen
                    ? GetScreenResolveTarget()
                    : new RenderTargetIdentifier(_resolvedColor!);
                color.ConfigureResolveTarget(resolveTarget);
                color.storeAction = RenderBufferStoreAction.Resolve;
            }
            else if (UsesIntermediateColor)
            {
                color.ConfigureTarget(_resolvedColor, false, true);
                color.storeAction = RenderBufferStoreAction.Store;
            }
            else
            {
                color.ConfigureTarget(BuiltinRenderTextureType.CameraTarget, false, true);
                color.storeAction = RenderBufferStoreAction.Store;
            }
            color.ConfigureClear(background);

            var depth = new AttachmentDescriptor(_depthFormat);
            depth.ConfigureTarget(_depthStencil, false, false);
            // Unity 的 Native RenderPass clearDepth 使用 1f，底层负责 reversed-Z 约定。
            depth.ConfigureClear(Color.clear, 1f, 0);
            depth.storeAction = RenderBufferStoreAction.DontCare;
            _sceneAttachments[0] = color;
            _sceneAttachments[1] = depth;
            buffer.BeginRenderPass(_width, _height, Samples, _sceneAttachments, 1, _subPasses, ScenePassName);
            buffer.SetViewport(new Rect(0f, 0f, _width, _height));
        }

        private static RenderTargetIdentifier GetScreenResolveTarget()
        {
            // 每帧取当前显示缓冲，不缓存跨帧/交换链重建的 RenderBuffer。
            // 保持系统 1x、自建 memoryless MSAA 和 Resolve-only；不增加 RT、绑定或 FinalCopy。
            // iOS Metal 与其它后端保留原来的 CameraTarget 接法，不扩大本次真机验证范围。
            return UseDisplayResolveTarget
                ? new RenderTargetIdentifier(Display.main.colorBuffer)
                : new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
        }

        internal void BeginFinalCopy(CommandBuffer buffer, int outputWidth, int outputHeight,
            Rect pixelRect, Color background)
        {
            // 全屏复制无需加载旧颜色；偏移 viewport 会留下屏幕边缘，改用整附件 Clear，
            // 防止 DontCare 的未覆盖区域显示旧帧。均不需要场景深度附件。
            var color = new AttachmentDescriptor(_colorFormat);
            color.ConfigureTarget(BuiltinRenderTextureType.CameraTarget, false, true);
            if (pixelRect == new Rect(0f, 0f, outputWidth, outputHeight))
                color.loadAction = RenderBufferLoadAction.DontCare;
            else
                color.ConfigureClear(background);
            color.storeAction = RenderBufferStoreAction.Store;
            _copyAttachments[0] = color;
            // 场景可为 540x960，最终输出仍为 1080x1920；不能复用场景附件尺寸。
            buffer.BeginRenderPass(outputWidth, outputHeight, 1, _copyAttachments, -1, _subPasses, FinalCopyPassName);
            // 最终目标始终使用完整、零原点的物理屏幕 viewport。
            // 原逻辑 pixelRect 由 FinalCopy shader 的 clip-space 变换应用，避免 RT/display 偏移约定不同。
            buffer.SetViewport(new Rect(0f, 0f, outputWidth, outputHeight));
        }

        internal void SetCompatibleSceneTarget(CommandBuffer buffer, bool preserveContents = false)
        {
            // 附件优化关闭或 Gizmos 需要深度时，保留普通 Store 路径，RenderScale 仍然生效。
            RenderTargetIdentifier color = Samples > 1 ? _msaaColor! : _resolvedColor!;
            var load = preserveContents ? RenderBufferLoadAction.Load : RenderBufferLoadAction.DontCare;
            buffer.SetRenderTarget(color, load, RenderBufferStoreAction.Store,
                _depthStencil!, load, RenderBufferStoreAction.Store);
            buffer.SetViewport(new Rect(0f, 0f, _width, _height));
        }

        internal void ResolveCompatible(CommandBuffer buffer)
        {
            if (Samples > 1) buffer.ResolveAntiAliasedSurface(_msaaColor!, _resolvedColor!);
        }

        // factory 仅在重建附件时使用，允许隔离验证创建失败、标记被忽略以及普通 RT 重试。
        // 稳定帧只复用附件，不调用此方法或分配委托。
        internal static RenderTexture? CreateMSAAColor(RenderTextureDescriptor descriptor, bool tryMemoryless,
            out bool memorylessRejected,
            Func<RenderTextureDescriptor, string, RenderTexture?>? factory = null)
        {
            factory ??= CreateTexture;
            memorylessRejected = false;
            descriptor.memoryless = tryMemoryless ? RenderTextureMemoryless.MSAA : RenderTextureMemoryless.None;
            var texture = factory(descriptor, "MRP Scene MSAA Color");
            if (MatchesMSAAColor(texture, descriptor)) return texture;
            ReleaseTexture(ref texture);
            if (!tryMemoryless) return null;

            // 支持标志不保证本附件组合可用；创建失败或引擎清除 memoryless 标记时退回相同画质。
            memorylessRejected = true;
            descriptor.memoryless = RenderTextureMemoryless.None;
            texture = factory(descriptor, "MRP Scene MSAA Color");
            if (MatchesMSAAColor(texture, descriptor)) return texture;
            ReleaseTexture(ref texture);
            return null;
        }

        private static bool MatchesMSAAColor(RenderTexture? texture, RenderTextureDescriptor descriptor)
        {
            return texture != null && texture.IsCreated()
                && texture.antiAliasing == descriptor.msaaSamples
                && texture.graphicsFormat == descriptor.graphicsFormat
                && texture.bindTextureMS == descriptor.bindMS
                && texture.memorylessMode == descriptor.memoryless;
        }

        private static RenderTexture? CreateTexture(RenderTextureDescriptor descriptor, string name)
        {
            RenderTexture? texture = null;
            try
            {
                // 构造、属性设置与 GPU 创建均可能拒绝附件组合；全部纳入同一清理/回退边界。
                texture = new RenderTexture(descriptor);
                texture.name = name;
                texture.hideFlags = HideFlags.HideAndDontSave;
                texture.filterMode = FilterMode.Point;
                // 场景颜色只做 Point/Bilinear 拷贝，不为 MRP 自建 RT 请求各向异性采样。
                texture.anisoLevel = 0;
                texture.wrapMode = TextureWrapMode.Clamp;
                if (texture.Create() && texture.IsCreated()) return texture;
            }
            catch (Exception exception) when (exception is UnityException || exception is ArgumentException)
            {
                // 不支持的附件组合可同步抛出异常；清理后交给调用方选择普通 RT 或兼容绘制。
            }
            ReleaseTexture(ref texture);
            return null;
        }

        /// <summary>Releases temporary targets when camera capture stops using the compatible direct path.</summary>
        /// <remarks>Descriptor arrays remain valid for the next attachment request.</remarks>
        internal void ReleaseTargets()
        {
            ReleaseTextures();
            _width = 0;
            _height = 0;
            _allocationFailed = false;
            Samples = 1;
            UsesIntermediateColor = false;
            ResolvesToScreen = false;
        }

        private void ReleaseTextures()
        {
            ReleaseTexture(ref _depthStencil);
            ReleaseTexture(ref _msaaColor);
            ReleaseTexture(ref _resolvedColor);
        }

        private static void ReleaseTexture(ref RenderTexture? texture)
        {
            if (texture == null) return;
            texture.Release();
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
            texture = null;
        }

        public void Dispose()
        {
            ReleaseTextures();
            if (_sceneAttachments.IsCreated) _sceneAttachments.Dispose();
            if (_copyAttachments.IsCreated) _copyAttachments.Dispose();
            if (_subPasses.IsCreated) _subPasses.Dispose();
        }
    }
}
