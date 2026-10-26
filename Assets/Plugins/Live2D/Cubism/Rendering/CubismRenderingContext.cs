/**
 * Copyright(c) Live2D Inc. All rights reserved.
 * Adapted for pipeline-independent rendering in MajdataPlay.
 * See https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Live2D.Cubism.Rendering
{
    /// <summary>
    /// Cubism 绘制所需的中立数据。这里只保存引擎相机、目标和普通 RT，
    /// 不向 Renderer / interceptor 暴露 URP、RenderGraph 或 RTHandle。
    /// Handle 后缀沿用 SDK 名称；字段本身不再是 RenderGraph TextureHandle。
    /// </summary>
    public sealed class CubismRenderingContext
    {
        public CubismRenderController[] RenderControllers;
        public CubismRenderControllerGroup.RenderControllerGroupData[] RenderControllerGroupDaraArray;
        public Camera Camera;
        public RenderTargetIdentifier CameraTextureHandle;
        public RenderTargetIdentifier CameraDepthTextureHandle;
        public RenderTexture CommonRenderingTextureHandle;
        public RenderTexture CommonTemporaryTextureHandle;
        public RenderTexture MaskTextureHandle;
        public int Width;
        public int Height;

        /// <summary>
        /// MRP 接管前的 forceRenderingOff。桥接器会临时把它们全部设为 true，
        /// SDK 显式绘制必须查询原值；不在字典中的对象仍查询实时属性。
        /// </summary>
        public Dictionary<Renderer, bool> OriginalForceRenderingOff;

        public bool IsForcedOff(Renderer renderer)
        {
            if (!renderer) return true;
            if (OriginalForceRenderingOff != null && OriginalForceRenderingOff.TryGetValue(renderer, out var original))
                return original;
            return renderer.forceRenderingOff;
        }

        /// <summary>更换相机或过滤后的组成员时，重新建立排序缓存。</summary>
        public bool ForceSorting;
    }
}
