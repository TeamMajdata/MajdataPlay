/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */

using Live2D.Cubism.Core;
using UnityEngine;
using UnityEngine.Rendering;
namespace Live2D.Cubism.Rendering.URP
{
    /// <summary>
    /// Provides data for rendering events `CubismRenderingInterceptorsManager.GetInstance().OnPreRendering` and `CubismRenderingInterceptorsManager.GetInstance().OnPostRendering`.
    /// </summary>
    public struct CubismRenderedEventArgs
    {
        /// <summary>
        /// Render pass data.
        /// </summary>
        public CubismRenderingContext PassData;
        /// <summary>
        /// Command buffer for rendering.
        /// </summary>
        public CommandBuffer CommandBuffer;
        /// <summary>
        /// Color buffer render texture.
        /// </summary>
        public RenderTexture ColorBuffer;
        /// <summary>
        /// Depth buffer texture handle.
        /// </summary>
        public RenderTargetIdentifier DepthBuffer;
        /// <summary>
        /// Sorting mode of the draw object's render controller.
        /// </summary>
        public CubismSortingMode SortingMode;
        /// <summary>
        /// Group sorting order.
        /// </summary>
        public int GroupSortingOrder;
        /// <summary>
        /// Sorting order of the draw object.
        /// NOTE: If SortingMode is set to sort by depth, multiple draw objects may have the same sorting order.
        /// </summary>
        public int SortingOrder;
        /// <summary>
        /// The drawable being rendered.
        /// </summary>
        public CubismDrawable Drawable;
        /// <summary>
        /// Distance to the camera.
        /// NOTE: If SortingMode is set to sort by order, multiple draw objects may have the same distance.
        /// </summary>
        public float Distance;
        /// <summary>
        /// Distance to the previous draw object in the sorted order.
        /// </summary>
        public float? PreviousDistance;
        /// <summary>
        /// Distance to the next draw object in the sorted order.
        /// </summary>
        public float? NextDistance;
        /// <summary>
        /// Position of the camera.
        /// </summary>
        public Vector3 CameraPos;
        /// <summary>
        /// Forward direction of the camera.
        /// </summary>
        public Vector3 CameraForward;
    }
}
