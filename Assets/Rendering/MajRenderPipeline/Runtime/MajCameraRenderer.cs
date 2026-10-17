using System;
using UnityEngine;
using UnityEngine.Rendering;

#nullable enable

namespace MajdataPlay.Rendering
{
    /// <summary>
    /// 一次相机渲染：准备几何 → 剔除 → 场景 Pass → 按需 Resolve/FinalCopy → 提交。
    /// 保留 Unity 原生 Renderer 的排序/遮罩通路，不在这里重写音符 Renderer 或合并 Mesh。
    /// </summary>
    internal sealed partial class MajCameraRenderer : IDisposable
    {
        private const string BufferName = "MRP.RenderCamera";
        private static readonly ShaderTagId UnlitShaderTag = new ShaderTagId("SRPDefaultUnlit");

        private static readonly int TimeId = Shader.PropertyToID("_Time");
        private static readonly int SinTimeId = Shader.PropertyToID("_SinTime");
        private static readonly int CosTimeId = Shader.PropertyToID("_CosTime");
        private static readonly int DeltaTimeId = Shader.PropertyToID("unity_DeltaTime");
        private static readonly int TimeParametersId = Shader.PropertyToID("_TimeParameters");
        private static readonly int LastTimeParametersId = Shader.PropertyToID("_LastTimeParameters");
        private static readonly int ScreenParamsId = Shader.PropertyToID("_ScreenParams");
        private static readonly int ScaledScreenParamsId = Shader.PropertyToID("_ScaledScreenParams");
        private static readonly int ScreenSizeId = Shader.PropertyToID("_ScreenSize");
        private static readonly int RTHandleScaleId = Shader.PropertyToID("_RTHandleScale");
        private static readonly int GlobalMipBiasId = Shader.PropertyToID("_GlobalMipBias");
        private static readonly int TextureMipBiasId = Shader.PropertyToID("_MajTextureMipBias");
        private static readonly int AlphaToMaskAvailableId = Shader.PropertyToID("_MajAlphaToMaskAvailable");
        private static readonly int CameraColorTextureId = Shader.PropertyToID("_MajCameraColorTexture");
        private static readonly int FinalCopyScaleBiasId = Shader.PropertyToID("_MajFinalCopyScaleBias");
        private static readonly int FinalCopyViewportId = Shader.PropertyToID("_MajFinalCopyViewport");

        // 每个管线实例复用一个 CommandBuffer，不在每帧创建/销毁。
        // 命名 buffer 由 Unity 为每次 Execute 自动配对标记；手动阶段标记必须在同一次 Execute 内闭合。
        // 不再使用跨 Execute 的外层 Begin/EndSample，避免与自动标记交错。
        private readonly CommandBuffer _buffer = new CommandBuffer { name = BufferName };
        private readonly MajCameraAttachments _attachments = new MajCameraAttachments();
        private readonly Material? _finalCopyMaterial;
        private ScriptableRenderContext _context;
        private Camera _camera = null!;
        private CullingResults _cullingResults;
        private bool _screenSamplesOverridden;
        private int _previousScreenSamples;
        private int _screenSamplesChangeFrame = -1;
        private bool _reportedAttachmentFallback;
        private IMajCameraRenderExtension? _cameraExtension;
        private int _extensionVersion = -1;
        private bool _extensionActive;
        private bool _usingSceneAttachments;
        private int _submittedSceneWidth, _submittedSceneHeight;
        private int _outputWidth;
        private int _outputHeight;
        private int _outputSurfaceWidth;
        private int _outputSurfaceHeight;
        private Rect _outputPixelRect;
        private int _textureMipmapBias;
        private bool _reportedExtensionCameraFallback;

        internal MajCameraRenderer(Shader finalCopyShader)
        {
            if (finalCopyShader != null && finalCopyShader.isSupported)
            {
                _finalCopyMaterial = new Material(finalCopyShader)
                {
                    name = "MRP FinalCopy",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
        }

        internal void Render(ScriptableRenderContext context, Camera camera,
            bool useDynamicBatching, bool useGPUInstancing, bool optimizeAttachments,
            bool resolveToScreen, bool isSingleCamera, float renderScale, MajMSAAMode msaaMode,
            bool forceIntermediateOutput, int textureMipmapBias = 0)
        {
            _context = context;
            _camera = camera;
            _textureMipmapBias = Mathf.Clamp(textureMipmapBias, 0, 2);
            bool reportOutput = UsesMainScreenTarget();
            if (reportOutput) MajRenderDiagnostics.BeginCamera();
            try
            {
                var extension = GetCameraExtension();
                MajRenderOutputStatus outputStatus;
                try
                {
                    // SDK 手绘接管必须发生在 Cull 前，避免对象同时进入普通 RendererList。
                    _extensionActive = extension != null && extension.Prepare(camera);
                    if (_extensionActive && extension!.RequiresIntermediateTarget
                        && !CanUseExtensionIntermediateTarget(isSingleCamera))
                    {
                        // 当前兼容通路从清色开始，不读取屏幕已有颜色/深度，不能冒充相机叠加。
                        // 跳过该相机的 SDK 合成，保留已有屏幕；其余相机及普通绘制不受影响。
                        if (!_reportedExtensionCameraFallback)
                        {
                            Debug.LogWarning("MRP: 复杂相机扩展需要 Color/Skybox 清屏；偏移屏幕 viewport 仅支持独立主相机，Depth/Nothing 或相机叠加暂不执行扩展合成。");
                            _reportedExtensionCameraFallback = true;
                        }
                        _extensionActive = false;
                    }
                    outputStatus = RenderCamera(useDynamicBatching, useGPUInstancing, optimizeAttachments,
                        resolveToScreen, isSingleCamera, renderScale, msaaMode, forceIntermediateOutput);
                }
                finally
                {
                    // Prepare 中途失败也恢复 SDK 暂时修改的 Renderer 状态。
                    try { extension?.EndCamera(); }
                    finally
                    {
                        _extensionActive = false;
                        _usingSceneAttachments = false;
                    }
                }
                // 各 Render 路径正常返回意味着 Submit 已完成；SDK 清理异常也不能发布成功。
                if (reportOutput)
                {
                    MajRenderDiagnostics.PublishDimensions(_outputSurfaceWidth, _outputSurfaceHeight,
                        _submittedSceneWidth, _submittedSceneHeight);
                    MajRenderDiagnostics.Publish(camera.pixelWidth > 0 && camera.pixelHeight > 0
                        ? outputStatus : MajRenderOutputStatus.Unavailable);
                }
            }
            catch
            {
                if (reportOutput) MajRenderDiagnostics.Invalidate();
                throw;
            }
        }

        private IMajCameraRenderExtension? GetCameraExtension()
        {
            if (_extensionVersion == MajCameraRenderExtensions.Version) return _cameraExtension;
            _cameraExtension?.Dispose();
            _cameraExtension = MajCameraRenderExtensions.Create();
            _extensionVersion = MajCameraRenderExtensions.Version;
            return _cameraExtension;
        }

        private MajRenderOutputStatus RenderCamera(bool useDynamicBatching, bool useGPUInstancing, bool optimizeAttachments,
            bool resolveToScreen, bool isSingleCamera, float renderScale, MajMSAAMode msaaMode,
            bool forceIntermediateOutput)
        {
            PrepareGeometry();
            if (!Cull())
            {
                return MajRenderOutputStatus.Unavailable;
            }

            _context.SetupCameraProperties(_camera);
            bool canUseIntermediate = CanUseIndependentTarget(isSingleCamera);
            // pixelWidth/Height 描述相机逻辑 viewport，不能用来推断整张屏幕附件尺寸。
            // Android 显示缓冲实验只影响 GPU 目标域，CPU 的 Camera / Canvas / 触摸不再乘比例。
            int outputWidth = Mathf.Max(1, _camera.targetTexture != null ? _camera.targetTexture.width : _camera.pixelWidth);
            int outputHeight = Mathf.Max(1, _camera.targetTexture != null ? _camera.targetTexture.height : _camera.pixelHeight);
            _outputPixelRect = _camera.pixelRect;
            _outputSurfaceWidth = outputWidth;
            _outputSurfaceHeight = outputHeight;
            if (UsesMainScreenTarget())
            {
                MajAndroidDisplayResolution.GetRenderingSize(out _outputSurfaceWidth, out _outputSurfaceHeight);
#if UNITY_ANDROID && !UNITY_EDITOR
                // 用 normalized rect 映射一次，不假定 pixelRect 是否已经被引擎缩放。
                _outputPixelRect = MajFinalCopyMapping.GetSurfaceViewport(_camera.rect,
                    _outputSurfaceWidth, _outputSurfaceHeight);
                outputWidth = Mathf.Max(1, (int)_outputPixelRect.width);
                outputHeight = Mathf.Max(1, (int)_outputPixelRect.height);
#endif
            }
            _submittedSceneWidth = outputWidth;
            _submittedSceneHeight = outputHeight;
            _outputWidth = outputWidth;
            _outputHeight = outputHeight;
            float scale = canUseIntermediate
                ? MajRenderScale.LimitToTextureSize(MajRenderScale.Effective(renderScale), outputWidth, outputHeight)
                : 1f;
            int sceneWidth = MajRenderScale.ScaledDimension(outputWidth, scale);
            int sceneHeight = MajRenderScale.ScaledDimension(outputHeight, scale);
            bool isScaled = sceneWidth != outputWidth || sceneHeight != outputHeight;
            int requestedSamples = _camera.allowMSAA
                ? (msaaMode == MajMSAAMode.UseQualitySettings
                    ? QualitySettings.antiAliasing : (int)msaaMode)
                : 1;
            requestedSamples = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(1, requestedSamples)), 1, 8);
            bool extensionIntermediate = _extensionActive && _cameraExtension!.RequiresIntermediateTarget;
            // 复杂模型会在内部切 RT/读颜色；仅其相机转普通 Store 路径，MSAA 样本数不降低。
            // 原生屏幕附件必须与实际显示缓冲同尺寸；相机尺寸若被引擎裁切，保留普通兼容路径。
            // 偏移仍可在 local RT 内用原生 Pass 绘制，最后以完整屏幕 viewport 复制。
            bool nativePass = canUseIntermediate && optimizeAttachments && !extensionIntermediate
                && MajFinalCopyMapping.SupportsNativeScreenTarget(_camera.rect, outputWidth, outputHeight,
                    _outputSurfaceWidth, _outputSurfaceHeight);
            DisableNativePassForEditorGizmos(ref nativePass);

            // 缩放属于画质功能，不和附件丢弃开关绑定。非原生路径保留深度，仍画缩放 RT。
            // 长屏的 100% / 1x 默认保留直绘；缩放、MSAA 或明确选择兼容输出时使用中间颜色。
            // 关闭原生附件优化也不能顺带关闭预设的 MSAA。
            bool useSceneAttachments = (canUseIntermediate
                && (nativePass || isScaled || requestedSamples > 1 || forceIntermediateOutput))
                || extensionIntermediate;
            var directStatus = MajRenderOutputStatus.Direct;
            if (useSceneAttachments)
            {
                useSceneAttachments = _attachments.TryPrepare(sceneWidth, sceneHeight, requestedSamples,
                    _finalCopyMaterial != null, isScaled || !nativePass || forceIntermediateOutput, nativePass,
                    MajRenderScale.AutoFilter(outputWidth, outputHeight, scale),
                    resolveToScreen && !forceIntermediateOutput && nativePass && !isScaled
                        && MajFinalCopyMapping.CanResolveViewportToScreen(_camera.rect) && CanResolveToScreen());
                if (!useSceneAttachments)
                {
                    directStatus = MajRenderOutputStatus.DirectAttachmentFallback;
                    if (!_reportedAttachmentFallback)
                    {
                        Debug.LogWarning("MRP: 设备附件或 FinalCopy Shader 不可用，本相机回退到原分辨率兼容绘制。");
                        _reportedAttachmentFallback = true;
                    }
                }
            }

            if (useSceneAttachments)
            {
                if (!UsesMainScreenTarget() || PrepareSingleSampleScreen())
                {
                    _submittedSceneWidth = sceneWidth;
                    _submittedSceneHeight = sceneHeight;
                    _usingSceneAttachments = true;
                    if (nativePass)
                    {
                        RenderNative(useDynamicBatching, useGPUInstancing, outputWidth, outputHeight, sceneWidth, sceneHeight);
                        if (!_attachments.UsesIntermediateColor)
                            return _attachments.ResolvesToScreen ? MajRenderOutputStatus.DirectResolve : MajRenderOutputStatus.Direct;
                    }
                    else
                        RenderIntermediateCompatible(useDynamicBatching, useGPUInstancing,
                            outputWidth, outputHeight, sceneWidth, sceneHeight);
                    // 偏移 MSAA 不再直接 Resolve 到显示缓冲，避免 RT/display viewport Y 约定不同。
                    // 仍保留原生场景的 memoryless / Resolve-only，不误报成硬件不支持 MSAA。
                    if (!isScaled && resolveToScreen && !forceIntermediateOutput
                        && !MajFinalCopyMapping.CanResolveViewportToScreen(_camera.rect))
                        return MajRenderOutputStatus.IntermediateViewport;
                    return GetIntermediateOutputStatus(isScaled, resolveToScreen, forceIntermediateOutput, nativePass);
                }
                // 改变系统缓冲的请求可能下一帧才生效；这一帧不假定旧缓冲已经是 1x。
                directStatus = MajRenderOutputStatus.DirectScreenTransition;
            }
            else
            {
                // 离屏/Preview/其它 Display 不能撤销主相机的系统 1x 请求，避免逐帧反复重建。
                if (UsesMainScreenTarget())
                {
                    // 显式关闭 MSAA 的独立主相机仍维持系统 1x；不能恢复为跟随 Unity Quality
                    // 的 0 请求，否则 Low 的全局 antiAliasing=2 可能重新打开系统 MSAA。
                    if (canUseIntermediate && requestedSamples == 1)
                    {
                        if (!PrepareSingleSampleScreen() && directStatus == MajRenderOutputStatus.Direct)
                            directStatus = MajRenderOutputStatus.DirectScreenTransition;
                    }
                    else RestoreScreenSamples();
                }
            }

            RenderCompatible(useDynamicBatching, useGPUInstancing);
            return directStatus;
        }

        private static MajRenderOutputStatus GetIntermediateOutputStatus(bool isScaled, bool resolveToScreen,
            bool forceIntermediateOutput, bool nativePass)
        {
            // 缩放必需 Copy，优先于用户输出偏好；其余原因只在已经实际完成 Copy 的分支报告。
            if (isScaled) return MajRenderOutputStatus.IntermediateScaled;
            if (forceIntermediateOutput || !resolveToScreen) return MajRenderOutputStatus.IntermediateRequested;
            return nativePass ? MajRenderOutputStatus.IntermediateMSAAUnsupported
                : MajRenderOutputStatus.IntermediateNativeUnavailable;
        }

        private bool CanUseIndependentTarget(bool isSingleCamera)
        {
            // 独立长屏相机允许偏移 viewport；直绘或最终复制只应用一次原 pixelRect。
            // SceneView/录制 RT/相机叠加保留各自的目标与尺寸，不修改相机布局或触摸映射。
            return isSingleCamera && _camera.cameraType == CameraType.Game && _camera.targetTexture == null
                && _camera.targetDisplay == 0
                && !_camera.stereoEnabled && !_camera.allowDynamicResolution
                && (_camera.clearFlags == CameraClearFlags.Color || _camera.clearFlags == CameraClearFlags.Skybox)
                && _camera.pixelWidth > 0 && _camera.pixelHeight > 0;
        }

        private bool UsesMainScreenTarget() => _camera.cameraType == CameraType.Game
            && _camera.targetTexture == null && _camera.targetDisplay == 0;

        private bool CanUseExtensionIntermediateTarget(bool isSingleCamera)
        {
            if (_camera.clearFlags != CameraClearFlags.Color && _camera.clearFlags != CameraClearFlags.Skybox)
                return false;
            if (_camera.targetTexture != null || _camera.rect == new Rect(0f, 0f, 1f, 1f))
                return true;

            // 保留其它调用者设置偏移 viewport 的兼容路径；主相机已取消旧长屏自动上移。
            // 场景 RT 使用相机自己的 pixelWidth/Height；最终拷贝再应用 pixelRect 一次。
            // 不修改 Camera 的 rect/投影，保证 Canvas 与 ScreenToWorldPoint 使用同一映射。
            // 只允许独立主相机完整清屏，不能借此清掉另一台相机已有的屏幕内容。
            return isSingleCamera && UsesMainScreenTarget()
                && !_camera.stereoEnabled && !_camera.allowDynamicResolution
                && _camera.pixelWidth > 0 && _camera.pixelHeight > 0;
        }

        private static bool CanResolveToScreen()
        {
            // Unity 在不支持多采样系统缓冲的平台上，会拒绝 MSAA attachment 的 back-buffer 目标。
            // D3D11/Windows（包括 Editor）即使能 Resolve 到普通 RT，也不代表能 Resolve 到 CameraTarget；
            // BeginRenderPass 被拒绝后几何仍可能画进旧目标，造成多轮廓残留并反复报告 EndRenderPass 错误。
            if (!SystemInfo.supportsMultisampledBackBuffer) return false;

            // 没有“任意 MSAA RT 能 Resolve 到交换链”的统一能力标志。
            // 仅在支持多采样系统缓冲的 Vulkan / iOS Metal LDR Gamma 路径尝试，其它情况保留拷贝。
            // DefaultFormat.LDR 采用与 Unity URP 相同的屏幕格式约定，但仍须做目标设备画面验证。
            GraphicsDeviceType device = SystemInfo.graphicsDeviceType;
            bool supportedBackend = device == GraphicsDeviceType.Vulkan
                || (device == GraphicsDeviceType.Metal && Application.platform == RuntimePlatform.IPhonePlayer);
            if (!supportedBackend || QualitySettings.activeColorSpace != ColorSpace.Gamma)
                return false;

            Display display = Display.main;
            if (display == null || display.requiresBlitToBackbuffer || display.requiresSrgbBlitToBackbuffer)
                return false;

            HDROutputSettings hdr = HDROutputSettings.main;
            return hdr == null || !hdr.active;
        }

        private bool PrepareSingleSampleScreen()
        {
            if (!SystemInfo.supportsMultisampledBackBuffer) return true;
            // 0 表示跟随 QualitySettings，不代表 1x；自建附件已做 MSAA 时也必须显式请求 1。
            if (Screen.msaaSamples != 1)
            {
                _previousScreenSamples = Screen.msaaSamples;
                _screenSamplesOverridden = true;
                _screenSamplesChangeFrame = Time.frameCount;
                // MSAA 在场景附件内完成；最终屏幕只接收单采样颜色，避免再次保存/解析 MSAA。
                Screen.SetMSAASamples(1);
                return false;
            }
            return _screenSamplesChangeFrame != Time.frameCount;
        }

        private void RestoreScreenSamples()
        {
            if (!_screenSamplesOverridden) return;
            // 原请求为 0 时恢复“跟随画质”，不能改成强制 1x。
            Screen.SetMSAASamples(_previousScreenSamples);
            _screenSamplesOverridden = false;
            _screenSamplesChangeFrame = -1;
        }

        private void RenderNative(bool useDynamicBatching, bool useGPUInstancing,
            int outputWidth, int outputHeight, int sceneWidth, int sceneHeight)
        {
            bool passOpen = false;
            SetShaderGlobals(outputWidth, outputHeight, sceneWidth, sceneHeight);
            try
            {
                _attachments.BeginScene(_buffer, GetBackgroundColor());
                passOpen = true;
                // 中间颜色仍在 local 0..width/height 绘制，由 FinalCopy 应用一次原 viewport。
                // 无中间颜色时直接把场景绘制到屏幕位置，保留越出屏幕部分的自然裁切。
                // 不能缩短 viewport 高度来适配剩余区域，否则会压缩画面并破坏触摸映射。
                if (!_attachments.UsesIntermediateColor && _camera.rect != new Rect(0f, 0f, 1f, 1f))
                    _buffer.SetViewport(_outputPixelRect);
                // 与 Unity 的普通相机绘制相同，传入非 GPU 投影矩阵，由引擎适配当前附件/API。
                _buffer.SetViewProjectionMatrices(_camera.worldToCameraMatrix, _camera.projectionMatrix);
                RecordGeometry(useDynamicBatching, useGPUInstancing);
                _buffer.EndRenderPass();
                passOpen = false;

                if (_attachments.UsesIntermediateColor)
                {
                    // MSAA 在场景末尾已 Resolve；1x 缩放直接采样已 Store 的场景颜色。
                    SetFinalCopySource();
                    _attachments.BeginFinalCopy(_buffer, _outputSurfaceWidth, _outputSurfaceHeight,
                        _outputPixelRect, GetBackgroundColor());
                    passOpen = true;
                    RecordFinalCopy();
                    _buffer.EndRenderPass();
                    passOpen = false;
                }
                // 引擎随后绘制全分辨率 Overlay，不能让它继承低分辨率的 scaled globals。
                if (sceneWidth != outputWidth || sceneHeight != outputHeight)
                    SetScreenGlobals(outputWidth, outputHeight, outputWidth, outputHeight);
            }
            finally
            {
                if (passOpen) _buffer.EndRenderPass();
                // 引擎的 Overlay 在单采样屏幕绘制，不能继承场景 MSAA 的覆盖率状态。
                _buffer.SetGlobalFloat(AlphaToMaskAvailableId, 0f);
                ExecuteBuffer();
                _context.Submit();
            }
        }

        private void RenderIntermediateCompatible(bool useDynamicBatching, bool useGPUInstancing,
            int outputWidth, int outputHeight, int sceneWidth, int sceneHeight)
        {
            try
            {
                _attachments.SetCompatibleSceneTarget(_buffer);
                _buffer.ClearRenderTarget(true, true, GetBackgroundColor());
                _buffer.SetViewProjectionMatrices(_camera.worldToCameraMatrix, _camera.projectionMatrix);
                SetShaderGlobals(outputWidth, outputHeight, sceneWidth, sceneHeight);
                RecordGeometry(useDynamicBatching, useGPUInstancing);
                ExecuteBuffer();
                // 深度在普通 Store 路径中仍然有效，Gizmos 结束后再 Resolve/放大。
                DrawEditorGizmos();
                _attachments.ResolveCompatible(_buffer);
                SetFinalCopySource();
                SetCameraOutputTarget();
                RecordFinalCopy();
                if (sceneWidth != outputWidth || sceneHeight != outputHeight)
                    SetScreenGlobals(outputWidth, outputHeight, outputWidth, outputHeight);
            }
            finally
            {
                _buffer.SetGlobalFloat(AlphaToMaskAvailableId, 0f);
                ExecuteBuffer();
                _context.Submit();
            }
        }

        private void RecordFinalCopy()
        {
            BeginDebugSample("MRP.FinalCopy");
            try
            {
                _buffer.DrawProcedural(Matrix4x4.identity, _finalCopyMaterial, 0,
                    MeshTopology.Triangles, 3, 1);
            }
            finally { EndDebugSample("MRP.FinalCopy"); }
        }

        private void SetFinalCopySource()
        {
            _buffer.SetGlobalTexture(CameraColorTextureId, _attachments.ResolvedColor);
            // Game 屏幕保留已实测正常的 raw UV。targetTexture / SceneView 的最终目标
            // 使用纹理方向，和实际 back buffer 不同；TopLeft API 的 raw 三角形需补一次翻转。
            // 仅按 graphicsUVStartsAtTop 翻所有输出，会让已正常的屏幕路径再次倒置。
            bool textureOutput = _camera.targetTexture != null || _camera.cameraType == CameraType.SceneView;
            bool flipY = textureOutput && SystemInfo.graphicsUVStartsAtTop;
            _buffer.SetGlobalVector(FinalCopyScaleBiasId, flipY
                ? new Vector4(1f, -1f, 0f, 1f)
                : new Vector4(1f, 1f, 0f, 0f));

            // Copy 的源 UV 方向和输出矩形是两件事。主屏使用完整零原点 viewport，
            // 在 raw clip-space 放置逻辑 pixelRect，避免偏移 viewport 在 RT/display 间反向。
            // 只取目标投影的 Y 符号来换算平移；不把 GPU 投影再次传给场景 SetViewProjectionMatrices。
            Vector4 copyViewport = new Vector4(1f, 1f, 0f, 0f);
            if (UsesMainScreenTarget())
            {
                float projectionYSign = GL.GetGPUProjectionMatrix(Matrix4x4.identity, false).m11 < 0f ? -1f : 1f;
                copyViewport = MajFinalCopyMapping.GetClipScaleBias(_outputPixelRect,
                    _outputSurfaceWidth, _outputSurfaceHeight, projectionYSign);
            }
            _buffer.SetGlobalVector(FinalCopyViewportId, copyViewport);
        }

        private void SetCameraOutputTarget()
        {
            if (_camera.targetTexture != null)
            {
                _buffer.SetRenderTarget(_camera.targetTexture,
                    RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                _buffer.SetViewport(new Rect(0f, 0f, _camera.targetTexture.width, _camera.targetTexture.height));
            }
            else
            {
                _buffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget,
                    RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                if (_camera.rect != new Rect(0f, 0f, 1f, 1f))
                {
                    // 独立 Color/Skybox 相机的偏移 viewport 留下屏幕边缘；先清完整输出，
                    // 避免 DontCare 未覆盖区域显示旧帧。逻辑区域由 FinalCopy 顶点变换和裁剪限定。
                    _buffer.ClearRenderTarget(false, true, GetBackgroundColor());
                }
                _buffer.SetViewport(UsesMainScreenTarget()
                    ? new Rect(0f, 0f, _outputSurfaceWidth, _outputSurfaceHeight) : _outputPixelRect);
            }
        }

        private void RenderCompatible(bool useDynamicBatching, bool useGPUInstancing)
        {
            Setup();
            try
            {
                RecordGeometry(useDynamicBatching, useGPUInstancing);
                // Gizmos 是 context 的独立命令，必须先提交缓冲内的场景几何以保持顺序。
                ExecuteBuffer();
                DrawEditorGizmos();
            }
            finally
            {
                _buffer.SetGlobalFloat(AlphaToMaskAvailableId, 0f);
                ExecuteBuffer();
                _context.Submit();
            }
        }

        private void RecordGeometry(bool useDynamicBatching, bool useGPUInstancing)
        {
            DrawGeometry(RenderQueueRange.opaque, SortingCriteria.CommonOpaque,
                useDynamicBatching, useGPUInstancing, "MRP.Opaque");
            if (_camera.clearFlags == CameraClearFlags.Skybox)
            {
                BeginDebugSample("MRP.Skybox");
                try { _buffer.DrawRendererList(_context.CreateSkyboxRendererList(_camera)); }
                finally { EndDebugSample("MRP.Skybox"); }
            }
            DrawCameraExtension();
            // Camera/World Space Canvas 在这个 RendererList 中，保留 SortingLayer/CanvasOrder。
            DrawGeometry(RenderQueueRange.transparent, SortingCriteria.CommonTransparent,
                useDynamicBatching, useGPUInstancing, "MRP.Transparent");
        }

        private void DrawCameraExtension()
        {
            if (!_extensionActive || _cameraExtension == null) return;
            bool intermediate = _cameraExtension.RequiresIntermediateTarget;
            // 分配失败时不得把需采样/切 RT 的 SDK 命令录进屏幕或原生 Pass。
            if (intermediate && !_usingSceneAttachments) return;

            RenderTexture? color = intermediate ? _attachments.SceneColor : null;
            RenderTexture? depth = intermediate ? _attachments.DepthStencil : null;
            int width = _usingSceneAttachments ? _attachments.DepthStencil.width : _outputWidth;
            int height = _usingSceneAttachments ? _attachments.DepthStencil.height : _outputHeight;
            // 当前注册者是 Live2D；保持通用扩展名，不让 MRP 本体依赖 SDK 类型。
            BeginDebugSample("MRP.CameraExtension");
            try { _cameraExtension.Render(_buffer, _camera, color, depth, width, height); }
            finally { EndDebugSample("MRP.CameraExtension"); }

            if (intermediate)
            {
                // SDK 最后可能清理其工作 RT；继续普通透明队列前恢复场景，并加载已有颜色/Stencil。
                _attachments.SetCompatibleSceneTarget(_buffer, preserveContents: true);
                _buffer.SetViewProjectionMatrices(_camera.worldToCameraMatrix, _camera.projectionMatrix);
                SetShaderGlobals(_outputWidth, _outputHeight, width, height);
            }
        }

        private void PrepareGeometry()
        {
            PrepareForSceneView();

            // Unity 的 Game 相机会正常发射 UI 几何；Preview/Reflection 相机需要显式补充。
            // 必须在 Cull 前调用，否则新发射的 Canvas 几何不会进入本次可见集合。
            if (_camera.cameraType == CameraType.Preview || _camera.cameraType == CameraType.Reflection)
            {
                ScriptableRenderContext.EmitGeometryForCamera(_camera);
            }
        }

        private bool Cull()
        {
            if (!_camera.TryGetCullingParameters(out ScriptableCullingParameters parameters))
            {
                return false;
            }

            // 只保留几何视锥剔除。无光照的 2D 首版不需要收集灯、阴影投射物或探针。
            parameters.cullingOptions &= ~(CullingOptions.NeedsLighting
                | CullingOptions.NeedsReflectionProbes | CullingOptions.ShadowCasters
                | CullingOptions.OcclusionCull);
            parameters.shadowDistance = 0f;
            _cullingResults = _context.Cull(ref parameters);
            return true;
        }

        private void Setup()
        {
            // SetupCameraProperties 已在剔除后执行；这里只记录兼容路径的目标和清屏。
            // 时间和尺寸 globals 放在引擎的相机设置之后，避免被覆盖。
            RenderTexture targetTexture = _camera.targetTexture;
            Rect viewport;
            if (targetTexture != null)
            {
                _buffer.SetRenderTarget(targetTexture);
                // Unity 的 targetTexture 相机渲染整张 RT，不使用 camera.rect/pixelRect。
                viewport = new Rect(0f, 0f, targetTexture.width, targetTexture.height);
            }
            else
            {
                _buffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                viewport = _outputPixelRect;
            }

            CameraClearFlags clearFlags = _camera.clearFlags;
            bool clearDepth = clearFlags != CameraClearFlags.Nothing;
            bool clearColor = clearFlags == CameraClearFlags.Color || clearFlags == CameraClearFlags.Skybox;

            // 首版以独立目标/单主相机为基线。这里清的是整张 attachment，
            // 不能把它当成多相机叠加时的局部 viewport 清屏实现。
            _buffer.ClearRenderTarget(clearDepth, clearColor, GetBackgroundColor());
            _buffer.SetViewport(viewport);
            SetShaderGlobals(viewport.width, viewport.height, viewport.width, viewport.height);
        }

        private Color GetBackgroundColor()
        {
            return QualitySettings.activeColorSpace == ColorSpace.Linear
                ? _camera.backgroundColor.linear : _camera.backgroundColor;
        }

        private void SetScreenGlobals(float outputWidth, float outputHeight, float width, float height)
        {
            outputWidth = Mathf.Max(1f, outputWidth);
            outputHeight = Mathf.Max(1f, outputHeight);
            width = Mathf.Max(1f, width);
            height = Mathf.Max(1f, height);
            float inverseWidth = 1f / width;
            float inverseHeight = 1f / height;
            Vector4 screenParams = new Vector4(width, height, 1f + inverseWidth, 1f + inverseHeight);

            // 对齐 URP：TMP 等读取的 _ScreenParams 是输出尺寸；场景采样使用 scaled 参数。
            _buffer.SetGlobalVector(ScreenParamsId, new Vector4(outputWidth, outputHeight,
                1f + 1f / outputWidth, 1f + 1f / outputHeight));
            _buffer.SetGlobalVector(ScaledScreenParamsId, screenParams);
            _buffer.SetGlobalVector(ScreenSizeId, new Vector4(width, height, inverseWidth, inverseHeight));
            _buffer.SetGlobalVector(RTHandleScaleId, Vector4.one);
            _buffer.SetGlobalVector(GlobalMipBiasId, MajRenderScale.MipBias((int)width, (int)outputWidth));
            // 与 RenderScale 的历史补偿分开：质量偏置只加一次，Overlay 也继承相同设置。
            _buffer.SetGlobalFloat(TextureMipBiasId, _textureMipmapBias);
        }

        private void SetShaderGlobals(float outputWidth, float outputHeight, float width, float height)
        {
            SetScreenGlobals(outputWidth, outputHeight, width, height);
            // 使用已创建附件的实际样本数，不把预设请求值当作已生效的 MSAA。
            // 普通离屏目标也能给出实际样本；未知系统目标保守关闭 A2C，沿用硬裁切。
            int samples = _usingSceneAttachments ? _attachments.Samples
                : (_camera.targetTexture != null ? _camera.targetTexture.antiAliasing : 1);
            _buffer.SetGlobalFloat(AlphaToMaskAvailableId, samples > 1 ? 1f : 0f);

            // 沿用 Unity 的游戏时间，不引入另一个音符/判定时钟。
            float time = Time.time;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                time = Time.realtimeSinceStartup;
            }
#endif
            float deltaTime = Time.deltaTime;
            float smoothDeltaTime = Time.smoothDeltaTime;
            float sinTime = Mathf.Sin(time);
            float cosTime = Mathf.Cos(time);
            float lastTime = time - deltaTime;

            _buffer.SetGlobalVector(TimeId, new Vector4(time / 20f, time, time * 2f, time * 3f));
            _buffer.SetGlobalVector(SinTimeId,
                new Vector4(Mathf.Sin(time / 8f), Mathf.Sin(time / 4f), Mathf.Sin(time / 2f), sinTime));
            _buffer.SetGlobalVector(CosTimeId,
                new Vector4(Mathf.Cos(time / 8f), Mathf.Cos(time / 4f), Mathf.Cos(time / 2f), cosTime));
            // 暂停或编辑器首帧的 deltaTime 可能为零，倒数必须避免 Infinity。
            _buffer.SetGlobalVector(DeltaTimeId, new Vector4(deltaTime,
                deltaTime > 0f ? 1f / deltaTime : 0f, smoothDeltaTime,
                smoothDeltaTime > 0f ? 1f / smoothDeltaTime : 0f));
            _buffer.SetGlobalVector(TimeParametersId, new Vector4(time, sinTime, cosTime, 0f));
            _buffer.SetGlobalVector(LastTimeParametersId,
                new Vector4(lastTime, Mathf.Sin(lastTime), Mathf.Cos(lastTime), 0f));
        }

        private void DrawGeometry(RenderQueueRange queueRange, SortingCriteria sortingCriteria,
            bool useDynamicBatching, bool useGPUInstancing, string sampleName)
        {
            BeginDebugSample(sampleName);
            try
            {
                var sortingSettings = new SortingSettings(_camera) { criteria = sortingCriteria };
                var drawingSettings = new DrawingSettings(UnlitShaderTag, sortingSettings)
                {
                    enableDynamicBatching = useDynamicBatching,
                    enableInstancing = useGPUInstancing,
                    // Shader 只需基础变换/排序，不要求逐对象光照、光照贴图或探针数据。
                    perObjectData = PerObjectData.None
                };
                var filteringSettings = new FilteringSettings(queueRange, _camera.cullingMask);
                var listParameters = new RendererListParams(_cullingResults, drawingSettings, filteringSettings);
                RendererList rendererList = _context.CreateRendererList(ref listParameters);

                // RendererList 是可见对象的绘制描述，不是我们创建的一张合并 Mesh。
                // 两个队列在同一 CommandBuffer 中依次执行，不在原生 Pass 中途切目标。
                _buffer.DrawRendererList(rendererList);
            }
            finally { EndDebugSample(sampleName); }
        }

        // Release 不记录细分 GPU 采样命令；名称均为常量，不逐帧拼接相机名或分配字符串。
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void BeginDebugSample(string sampleName) => _buffer.BeginSample(sampleName);

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void EndDebugSample(string sampleName) => _buffer.EndSample(sampleName);

        private void ExecuteBuffer()
        {
            _context.ExecuteCommandBuffer(_buffer);
            _buffer.Clear();
        }

        public void Dispose()
        {
            RestoreScreenSamples();
            _cameraExtension?.Dispose();
            _cameraExtension = null;
            _attachments.Dispose();
            if (_finalCopyMaterial != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_finalCopyMaterial);
                else UnityEngine.Object.DestroyImmediate(_finalCopyMaterial);
            }
            _buffer.Release();
        }

        // Player 构建中没有实现，编译器会移除这些调用；Editor 的代码在另一个 partial 文件。
        partial void PrepareForSceneView();
        partial void DrawEditorGizmos();
        partial void DisableNativePassForEditorGizmos(ref bool enabled);
    }
}
