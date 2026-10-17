using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MajdataPlay.Rendering
{
    /// <summary>
    /// 最小无光照管线：负责相机调度与生命周期，不负责音符逻辑或资源加载。
    /// 使用引擎原生 RenderPass 管理 MSAA/附件；第三方绘制通过可选扩展接入，不依赖其 SDK。
    /// </summary>
    public sealed class MajRenderPipeline : RenderPipeline
    {
        private static readonly Comparison<Camera> CameraDepthOrder = CompareCameraDepth;
        private static readonly int TextureMipBiasId = Shader.PropertyToID("_MajTextureMipBias");
        private readonly MajCameraRenderer _renderer;
        private readonly MajRenderPipelineAsset _asset;
        private readonly bool _useDynamicBatching;
        private readonly bool _useGPUInstancing;
        private readonly bool _previousSRPBatching;
        private readonly float _previousTextureMipBias;
        private AnisotropicFiltering _restoreAnisotropicFiltering;
        private readonly bool _optimizeAttachments;
        private readonly SupportedRenderingFeatures _previousRenderingFeatures;

        internal MajRenderPipeline(MajRenderPipelineAsset asset, bool useDynamicBatching, bool useGPUInstancing, bool useSRPBatcher,
            bool optimizeAttachments, Shader finalCopyShader)
        {
            _asset = asset;
            _useDynamicBatching = useDynamicBatching;
            _useGPUInstancing = useGPUInstancing;

            // SRP Batcher 是 Unity 的全局开关，而不是某个相机的选项。
            // 启用管线时保存旧值，退出时还原，避免影响随后启用的其他管线。
            _previousSRPBatching = GraphicsSettings.useScriptableRenderPipelineBatching;
            _previousTextureMipBias = Shader.GetGlobalFloat(TextureMipBiasId);
            GraphicsSettings.useScriptableRenderPipelineBatching = useSRPBatcher;
            _optimizeAttachments = optimizeAttachments;
            _renderer = new MajCameraRenderer(finalCopyShader);

            // Overlay 统一留给引擎在 SRP 之后绘制一次；MRP 不再手动画第二次。
            // 相机/World Space Canvas 仍在场景 RendererList 内绘制，保留它们的排序及 Stencil。
            _previousRenderingFeatures = SupportedRenderingFeatures.active;
            SupportedRenderingFeatures.active = new SupportedRenderingFeatures
            {
                supportsHDR = false,
                rendersUIOverlay = false
            };

            // MRP 面向正面 Sprite/UI，不提供各向异性过滤选项；由全局 Disable 覆盖纹理的 Aniso 设置。
            // 保存外部状态，退出 MRP 后交还给其他管线，和 SRP Batcher 的生命周期保持一致。
            _restoreAnisotropicFiltering = QualitySettings.anisotropicFiltering;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
            QualitySettings.activeQualityLevelChanged += OnActiveQualityLevelChanged;
        }

        private void OnActiveQualityLevelChanged(int previousLevel, int newLevel)
        {
            // 外部 SetQualityLevel 会重新应用过滤设置，管线资产相同时管线可能不重建，
            // 所以在画质切换事件中维持约束，无须每帧检查或修改纹理导入设置。
            // 退出时交还新档位刚应用的值，不能跨画质恢复构造时捕获的旧档位值。
            _restoreAnisotropicFiltering = QualitySettings.anisotropicFiltering;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
        }

        protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            // 每个引擎帧只重置一次；同帧追加的 SceneView/Preview context 不抹掉主屏结果。
            // 下一帧空相机列表则不能让上一帧路径冒充本帧结果。
            MajRenderDiagnostics.BeginFrame(Time.frameCount);
            bool hasMainScreenCamera = false;
            for (int i = 0; i < cameras.Count; i++)
            {
                Camera camera = cameras[i];
                if (camera != null && camera.cameraType == CameraType.Game
                    && camera.targetTexture == null && camera.targetDisplay == 0)
                {
                    hasMainScreenCamera = true;
                    break;
                }
            }
            try
            {
                // 使用 Unity 6 的 List 入口，避免旧 Camera[] 入口的弃用警告和数组转换。
                if (cameras.Count > 1) cameras.Sort(CameraDepthOrder);
                // 回调保留给相机相关组件；它们本身不是 URP RendererFeature。
                BeginContextRendering(context, cameras);
                try
                {
                    for (int i = 0; i < cameras.Count; i++)
                    {
                        Camera camera = cameras[i];
                        if (camera == null)
                        {
                            continue;
                        }

                        BeginCameraRendering(context, camera);
                        try
                        {
                            // 只有共享同一屏幕的相机会互相影响；录制 RT、Preview 或其它 Display
                            // 不应让主相机的 50%/75% 分辨率和预设 MSAA 悄悄变成引擎默认值。
                            _renderer.Render(context, camera, _useDynamicBatching, _useGPUInstancing,
                                _optimizeAttachments, _asset.ResolveToScreen,
                                HasExclusiveBackBuffer(camera, cameras), _asset.RenderScale, _asset.MsaaMode,
                                _asset.ForceIntermediateOutput, _asset.TextureMipmapBias);
                        }
                        finally
                        {
                            // 剔除失败或渲染异常时，也要配对结束相机回调。
                            EndCameraRendering(context, camera);
                        }
                    }
                }
                finally
                {
                    EndContextRendering(context, cameras);
                }
            }
            catch
            {
                // 主屏 context 的回调失败也必须失效；独立 Preview/RT context 的异常
                // 不能抹掉同帧已经成功提交的主屏结果。
                if (hasMainScreenCamera) MajRenderDiagnostics.Invalidate();
                throw;
            }
        }

        private static int CompareCameraDepth(Camera a, Camera b)
        {
            if (a == b) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            return a.depth.CompareTo(b.depth);
        }

        private static bool HasExclusiveBackBuffer(Camera camera, List<Camera> cameras)
        {
            for (int i = 0; i < cameras.Count; i++)
            {
                Camera other = cameras[i];
                if (other == null || other == camera || other.cameraType != CameraType.Game
                    || other.targetTexture != null || other.targetDisplay != camera.targetDisplay) continue;
                return false;
            }
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                MajRenderDiagnostics.Invalidate();
                QualitySettings.activeQualityLevelChanged -= OnActiveQualityLevelChanged;
                _renderer.Dispose();
                QualitySettings.anisotropicFiltering = _restoreAnisotropicFiltering;
                Shader.SetGlobalFloat(TextureMipBiasId, _previousTextureMipBias);
                GraphicsSettings.useScriptableRenderPipelineBatching = _previousSRPBatching;
                SupportedRenderingFeatures.active = _previousRenderingFeatures;
            }

            base.Dispose(disposing);
        }
    }
}
