/**
 * Copyright(c) Live2D Inc. All rights reserved.
 * Use of this source code is governed by the Live2D Open Software license.
 * See https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Live2D.Cubism.Rendering.URP
{
    /// <summary>
    /// 只负责 URP / RenderGraph 资源适配。Cubism 绘制逻辑在中立 Executor 中，
    /// 此程序集可随 URP 包一起停用，不再把包类型传播给 SDK Renderer / 事件。
    /// </summary>
    public class CubismRenderPassFeature : ScriptableRendererFeature
    {
        public class CubismRenderPass : ScriptableRenderPass, System.IDisposable
        {
            private readonly CubismRenderingExecutor _executor = new CubismRenderingExecutor();

            public class PassData
            {
                public CubismRenderingExecutor Executor;
                public CubismRenderingContext RenderingContext;
                public TextureHandle CameraColor;
                public TextureHandle CameraDepth;
                public TextureHandle CommonColor;
                public TextureHandle TemporaryColor;
                public TextureHandle Mask;
            }

            private static void ExecutePass(PassData data, UnsafeGraphContext graphContext)
            {
                // TextureHandle 必须在 RenderGraph 执行期转换，不能在 Record 期取得 RT。
                RTHandle color = data.CameraColor;
                RTHandle depth = data.CameraDepth;
                var context = data.RenderingContext;
                context.CameraTextureHandle = color.nameID;
                context.CameraDepthTextureHandle = depth.nameID;
                context.CommonRenderingTextureHandle = data.CommonColor;
                context.CommonTemporaryTextureHandle = data.TemporaryColor;
                context.MaskTextureHandle = data.Mask;
                context.Width = context.CommonRenderingTextureHandle.width;
                context.Height = context.CommonRenderingTextureHandle.height;
                data.Executor.Render(CommandBufferHelpers.GetNativeCommandBuffer(graphContext.cmd), context);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var manager = CubismRenderControllerGroup.GetInstance();
                if (manager.RenderControllers.Length == 0) return;

                using (var builder = renderGraph.AddUnsafePass<PassData>("Cubism URP Render Pass", out var data))
                {
                    var camera = frameData.Get<UniversalCameraData>();
                    var resources = frameData.Get<UniversalResourceData>();
                    data.Executor = _executor;
                    data.RenderingContext = new CubismRenderingContext
                    {
                        Camera = camera.camera,
                        RenderControllers = manager.RenderControllers,
                        RenderControllerGroupDaraArray = manager.GroupDataArray
                    };

                    // 保留 SDK 的尺寸、格式和 MSAA 约定；普通 Blit 读取中间纹理的已解析面。
                    var descriptor = resources.activeColorTexture.GetDescriptor(renderGraph);
                    descriptor.wrapMode = TextureWrapMode.Repeat;
                    descriptor.filterMode = FilterMode.Point;
                    descriptor.bindTextureMS = false;
                    descriptor.memoryless = RenderTextureMemoryless.None;
                    descriptor.name = "Cubism CommonTexture";
                    data.CommonColor = renderGraph.CreateTexture(descriptor);
                    descriptor.name = "Cubism TempTexture";
                    data.TemporaryColor = renderGraph.CreateTexture(descriptor);
                    descriptor.name = "Cubism MaskTexture";
                    data.Mask = renderGraph.CreateTexture(descriptor);
                    data.CameraColor = resources.activeColorTexture;
                    data.CameraDepth = resources.activeDepthTexture;
                    builder.UseTexture(data.CommonColor, AccessFlags.ReadWrite);
                    builder.UseTexture(data.TemporaryColor, AccessFlags.ReadWrite);
                    builder.UseTexture(data.Mask, AccessFlags.ReadWrite);
                    builder.UseTexture(data.CameraColor, AccessFlags.ReadWrite);
                    builder.UseTexture(data.CameraDepth, AccessFlags.ReadWrite);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc((PassData pass, UnsafeGraphContext context) => ExecutePass(pass, context));
                }
            }

            public void Dispose() => _executor.Dispose();
        }

        private CubismRenderPass _mScriptablePass;

        public override void Create()
        {
            _mScriptablePass?.Dispose();
            _mScriptablePass = new CubismRenderPass
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType != CameraType.Game
                && renderingData.cameraData.cameraType != CameraType.SceneView) return;
            renderer.EnqueuePass(_mScriptablePass);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _mScriptablePass?.Dispose();
            _mScriptablePass = null;
        }
    }
}
