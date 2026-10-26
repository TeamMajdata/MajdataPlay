/**
 * Copyright(c) Live2D Inc. All rights reserved.
 * Shared rendering extracted from the SDK URP feature for MajdataPlay.
 * See https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */
using System;
using Live2D.Cubism.Core;
using Live2D.Cubism.Rendering.URP;
using Live2D.Cubism.Rendering.URP.RenderingInterceptor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Live2D.Cubism.Rendering
{
    /// <summary>
    /// URP/MRP 共用的 Cubism 排序和合成器。只录制调用者的 CommandBuffer，
    /// 不提交、不清空缓冲、不持有 RenderGraph/RTHandle，也不拥有调用者传入的 RT。
    /// </summary>
    public sealed class CubismRenderingExecutor : IDisposable
    {
        private const int GEqual = (int)CompareFunction.GreaterEqual;
        private const int LEqual = (int)CompareFunction.LessEqual;
        private Camera _previousCamera;
        private CubismRenderControllerGroup.RenderControllerGroupData[] _previousGroups;
        private struct RendererGroupData
        {
            /// <summary>
            /// Sorting index for the renderer group.
            /// </summary>
            public int SortingIndex;

            /// <summary>
            /// Array of render controllers in this group.
            /// </summary>
            public CubismRenderController[] RenderControllers;

            /// <summary>
            /// Array of renderers in this group.
            /// </summary>
            public CubismRenderer[] Renderers;
        }

        /// <summary>
        /// Command buffer used for rendering operations.
        /// </summary>
        private CommandBuffer _commandBuffer;

        /// <summary>
        /// Array of renderer groups sorted by their sorting index.
        /// </summary>
        private RendererGroupData[] _sortedRendererGroupDataArray = Array.Empty<RendererGroupData>();

        /// <summary>
        /// Mesh used for blitting render textures.
        /// </summary>
        private Mesh _blitRenderTextureMesh;

        /// <summary>
        /// Material used for blitting render textures.
        /// </summary>
        private Material _blitRenderTextureMaterial;
        /// <summary>
        /// Sets up and fills the renderer group.
        /// </summary>
        /// <param name="targetIndex">Target index in the sorted renderer groups array.</param>
        /// <param name="target">Added target renderer group.</param>
        /// <param name="source">Source render controller group.</param>
        /// <param name="cameraPos">Position of the camera</param>
        /// <param name="data">Pass data containing render controller groups and camera data</param>
        private void SetUpRendererGroup(int targetIndex, RendererGroupData target, CubismRenderControllerGroup.RenderControllerGroupData source, Vector3 cameraPos, CubismRenderingContext data)
        {
            var previousCount = 0;
            for (var renderControllerIndex = 0; renderControllerIndex < source.Controllers.Length; renderControllerIndex++)
            {
                var controller = source.Controllers[renderControllerIndex];



                if (!controller || controller.Renderers == null)
                {
                    continue;
                }

                controller.CurrentFrameBuffer = data.CommonRenderingTextureHandle;
                // Copy the renderers from the controller to the sorted array.
                for (var rendererIndex = 0; rendererIndex < controller.Renderers.Length; rendererIndex++)
                {
                    target.Renderers[previousCount + rendererIndex] = controller.Renderers[rendererIndex];
                    if (target.Renderers[previousCount + rendererIndex])
                        target.Renderers[previousCount + rendererIndex].IsLastDrawObjectInModel = false;
                }

                // Update the previous count for the next controller
                previousCount += controller.Renderers.Length;
            }

            // Sort the renderers by their sorting order.
            SortBySortingOrder(target.Renderers, cameraPos, data.Camera.transform.forward);

            // Assign the filled target to the sorted renderer groups array.
            _sortedRendererGroupDataArray[targetIndex] = target;
        }

        /// <summary>
        /// Sorts the renderer groups based on their sorting order.
        /// </summary>
        /// <param name="data">Pass data containing render controller groups.</param>
        private void SortingRendererGroups(CubismRenderingContext data)
        {
            var didChangeSortingRenderControllerGroup = CubismRenderControllerGroup.GetInstance().DidChangeSortingRenderControllerGroup;
            if (data.RenderControllerGroupDaraArray == null
                || data.RenderControllerGroupDaraArray.Length < 1)
            {
                return;
            }

            // Iterate through each render controller group.
            for (var groupsIndex = 0; groupsIndex < data.RenderControllerGroupDaraArray.Length; groupsIndex++)
            {
                var group = data.RenderControllerGroupDaraArray[groupsIndex];
                var rendererCount = 0;
                var didChangeSortingOrder = false;

                for (var i = 0; i < group.Controllers.Length; i++)
                {
                    var controller = group.Controllers[i];

                    // Skip if controller is null or has no renderers
                    if (!controller || controller.Renderers == null)
                    {
                        continue;
                    }

                    // Update sorting state from camera position.
                    controller.UpdateDidChangeSortingFromZ(data.Camera.transform.position);

                    // Update offscreen render order if it has changed.
                    if (controller.DidChangeDrawableRenderOrder)
                    {
                        for (var offscreenIndex = 0; offscreenIndex < controller.OffscreenRenderers?.Length; offscreenIndex++)
                        {
                            var offscreenRenderer = controller.OffscreenRenderers[offscreenIndex];
                            offscreenRenderer.SetDrawObjectRenderOrder(controller.Model.AllDrawObjectsRenderOrder[controller.DrawableRenderers.Length + offscreenRenderer.Offscreen.UnmanagedIndex]);
                        }
                    }

                    // Check if any controller has changed its drawable render order.
                    didChangeSortingOrder |=
                        controller.DidChangeSorting
                         || controller.DidChangeDrawableRenderOrder
                         || didChangeSortingRenderControllerGroup;

                    rendererCount += controller.Renderers.Length;
                }

                // If nothing changed, no need to sort again.
                if (!didChangeSortingOrder && !data.ForceSorting)
                {
                    continue;
                }

                // Create an empty array if it doesn't exist or its size doesn't match the number of groups.
                if (_sortedRendererGroupDataArray == null
                    || _sortedRendererGroupDataArray.Length > data.RenderControllerGroupDaraArray.Length)
                {
                    _sortedRendererGroupDataArray = Array.Empty<RendererGroupData>();
                }

                var hasGroupFound = false;
                for (var targetIndex = 0; targetIndex < _sortedRendererGroupDataArray.Length; targetIndex++)
                {
                    var target = _sortedRendererGroupDataArray[targetIndex];

                    if (target.Renderers == null
                        || target.SortingIndex != group.SortingGroupIndex)
                    {
                        continue;
                    }

                    if (target.Renderers.Length != rendererCount)
                    {
                        // Resize the renderers array to fit all renderers in the group.
                        Array.Resize(ref target.Renderers, rendererCount);
                    }

                    var hasControllersNull = false;
                    for (var controllerIndex = 0; controllerIndex < target.RenderControllers.Length; controllerIndex++)
                    {
                        if (target.RenderControllers[controllerIndex])
                        {
                            continue;
                        }

                        hasControllersNull = true;
                        break;
                    }

                    if (hasControllersNull
                        || didChangeSortingRenderControllerGroup
                        || target.RenderControllers.Length != data.RenderControllerGroupDaraArray[groupsIndex].Controllers.Length)
                    {
                        target.RenderControllers = data.RenderControllerGroupDaraArray[groupsIndex].Controllers;
                    }

                    // Set up and fill the renderer group.
                    SetUpRendererGroup(targetIndex, target, group, data.Camera.transform.position, data);

                    hasGroupFound = true;
                    break;
                }

                // Initialize the renderer group if necessary
                if (hasGroupFound)
                {
                    continue;
                }

                // Resize the sorted renderer groups array to add a new group.
                Array.Resize(ref _sortedRendererGroupDataArray, _sortedRendererGroupDataArray.Length + 1);
                var newRendererGroup = new RendererGroupData
                {
                    SortingIndex = group.SortingGroupIndex,
                    RenderControllers = data.RenderControllerGroupDaraArray[groupsIndex].Controllers,
                    Renderers = new CubismRenderer[rendererCount]
                };

                // Set up and fill the renderer group.
                SetUpRendererGroup(_sortedRendererGroupDataArray.Length - 1, newRendererGroup, group, data.Camera.transform.position,data);
            }
        }

        /// <summary>
        /// Sorts the renderers by their sorting order.
        /// Calculates the distance to camera for each renderer and sorts them accordingly.
        /// </summary>
        /// <param name="renderers">Array of renderers to sort</param>
        /// <param name="cameraPosition">Position of the camera</param>
        /// <param name="cameraForward">Forward direction of the camera (normalized vector)</param>
        private void SortBySortingOrder(CubismRenderer[] renderers, Vector3 cameraPosition, Vector3 cameraForward)
        {
            for (var index = 0; index < renderers.Length; index++)
            {
                if (renderers[index]) renderers[index].CalculateDistanceToCamera(cameraPosition, cameraForward);
            }

            Array.Sort(renderers, CompareBySortingOrder);
        }

        /// <summary>
        /// Compares two renderers by their sorting order.
        /// </summary>
        /// <param name="a">First renderer to compare</param>
        /// <param name="b">Second renderer to compare</param>
        /// <returns>Negative value if a should be rendered before b, positive if after, zero if equal.</returns>
        private static int CompareBySortingOrder(CubismRenderer a, CubismRenderer b)
        {
            // Fail-safe check
            if (!a || !b
                   || !a.MeshRenderer || !b.MeshRenderer
                   || !a.RenderController || !b.RenderController)
            {
                return 0;
            }

            var result = a.MeshRenderer.sortingOrder - b.MeshRenderer.sortingOrder;

            if (result != 0
                || !(a.SortingMode.SortByDepth() && b.SortingMode.SortByDepth()))
            {
                return result;
            }

            // If sorting order is the same, compare by local Z position.
            var sortValue = (b.DistanceToCamera / b.RenderController.DepthOffset) - (a.DistanceToCamera / a.RenderController.DepthOffset);
            result = sortValue >= 0.0f
                ? Mathf.CeilToInt(sortValue)
                : Mathf.FloorToInt(sortValue);

            return result;
        }

        /// <summary>
        /// Checks and sets the skip rendering flag for each renderer.
        /// </summary>
        private void CheckRenderingSkip(CubismRenderingContext data)
        {
            // Reset skip rendering flag for each renderer.
            for (var groupIndex = 0; groupIndex < _sortedRendererGroupDataArray.Length; groupIndex++)
            {
                var rendererGroup = _sortedRendererGroupDataArray[groupIndex];

                for (var rendererIndex = 0; rendererIndex < rendererGroup.Renderers.Length; rendererIndex++)
                {
                    if (rendererGroup.Renderers[rendererIndex]) rendererGroup.Renderers[rendererIndex].SkipRendering = false;
                }
            }

            // Check and set skip rendering flag for each renderer.
            for (var groupIndex = 0; groupIndex < _sortedRendererGroupDataArray.Length; groupIndex++)
            {
                var rendererGroup = _sortedRendererGroupDataArray[groupIndex];

                for (var rendererIndex = 0; rendererIndex < rendererGroup.Renderers.Length; rendererIndex++)
                {
                    var renderer = rendererGroup.Renderers[rendererIndex];

                    if (!renderer
                        || renderer.SkipRendering)
                    {
                        continue;
                    }

                    renderer.SkipRendering |= !renderer.RenderController
                                 || !renderer.RenderController.gameObject.activeInHierarchy
                                 || !renderer.RenderController.enabled
                                 || !renderer.gameObject.activeInHierarchy
                                 || !renderer.MeshRenderer
                                 || !renderer.MeshRenderer.enabled || data.IsForcedOff(renderer.MeshRenderer)
                                 || (data.Camera.cullingMask & (1 << renderer.gameObject.layer)) == 0;

                    switch (renderer.DrawObjectType)
                    {
                        case CubismModelTypes.DrawObjectType.Drawable:
                            renderer.SkipRendering |= renderer.Opacity <= 0.0f;
                            break;
                        case CubismModelTypes.DrawObjectType.Offscreen:
                            renderer.SkipRendering |= renderer.Offscreen.Opacity <= 0.0f;

                            if (!renderer.SkipRendering)
                            {
                                break;
                            }

                            // Skip rendering for all child drawables and offscreens of the offscreen part.
                            var parts = renderer.RenderController?.Model?.Parts;
                            CubismPart part = null;
                            for (var partIndex = 0; partIndex < parts?.Length; partIndex++)
                            {
                                if (parts[partIndex].UnmanagedIndex != renderer.Offscreen.OwnerIndex)
                                {
                                    continue;
                                }

                                part = parts[partIndex];
                                break;
                            }

                            if (!part)
                            {
                                renderer.SkipRendering = true;
                                break;
                            }

                            // Skip rendering for all child drawables and offscreens of the offscreen part.
                            for (var drawablesIndex = 0; drawablesIndex < part.AllChildDrawables?.Length; drawablesIndex++)
                            {
                                var childDrawable = part.AllChildDrawables[drawablesIndex];

                                for (var targetRendererIndex = 0; targetRendererIndex < renderer.RenderController.Renderers.Length; targetRendererIndex++)
                                {
                                    var targetRenderer = renderer.RenderController.Renderers[targetRendererIndex];

                                    if (targetRenderer.DrawObjectType != CubismModelTypes.DrawObjectType.Drawable
                                        || targetRenderer.Drawable.UnmanagedIndex != childDrawable.UnmanagedIndex)
                                    {
                                        continue;
                                    }

                                    targetRenderer.SkipRendering = true;
                                    break;
                                }
                            }

                            for (var offscreensIndex = 0; offscreensIndex < part.AllChildOffscreens?.Length; offscreensIndex++)
                            {
                                var childOffscreen = part.AllChildOffscreens[offscreensIndex];

                                for (var j = 0; j < renderer.RenderController.Renderers.Length; j++)
                                {
                                    var targetRenderer = renderer.RenderController.Renderers[j];

                                    if (targetRenderer.DrawObjectType != CubismModelTypes.DrawObjectType.Offscreen
                                        || targetRenderer.Offscreen.UnmanagedIndex != childOffscreen.UnmanagedIndex)
                                    {
                                        continue;
                                    }

                                    targetRenderer.SkipRendering = true;
                                    break;
                                }
                            }
                            break;
                        default:
                            renderer.SkipRendering = true;
                            break;
                    }
                }
            }

            for (var i= 0; i < _sortedRendererGroupDataArray.Length; i++)
            {
                var target = _sortedRendererGroupDataArray[i];

                // Reset the last draw object flag for each renderer.
                for (var rendererIndex = 0; rendererIndex < target.Renderers.Length; rendererIndex++)
                {
                    var targetRenderer = target.Renderers[rendererIndex];
                    if (targetRenderer) targetRenderer.IsLastDrawObjectInModel = false;
                }

                for (var controllerIndex = 0; controllerIndex < target.RenderControllers.Length; controllerIndex++)
                {
                    var controller = target.RenderControllers[controllerIndex];

                    if (!controller
                        || !controller.enabled
                        || !controller.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    var lastIndex = target.Renderers.Length - 1;
                    while (lastIndex >= 0)
                    {
                        var targetRenderer = target.Renderers[lastIndex];

                        // Find the last draw renderer in the sorted array.
                        if (targetRenderer && !targetRenderer.SkipRendering
                            && targetRenderer.RenderController == controller)
                        {
                            // Mark it as the last draw object in the model.
                            targetRenderer.IsLastDrawObjectInModel = true;
                            break;
                        }

                        lastIndex--;
                    }
                }
            }
        }

        /// <summary>
        /// Draws the objects using the provided command buffer and pass data.
        /// </summary>
        /// <param name="commandBuffer">Command buffer to record draw commands.</param>
        /// <param name="data">Pass data containing render controllers and camera data.</param>
        private void DrawObjects(CommandBuffer commandBuffer, CubismRenderingContext data)
        {
            // Check and set skip rendering flags for each renderer.
            CheckRenderingSkip(data);

            for (var groupIndex = 0; groupIndex < _sortedRendererGroupDataArray?.Length; groupIndex++)
            {
                var rendererGroup = _sortedRendererGroupDataArray[groupIndex];

                if (rendererGroup.Renderers == null)
                {
                    continue;
                }

#if UNITY_EDITOR
                // Calculate distance to camera for each renderer for CubismRenderInterceptorsManager events.
                // HACK: Unity may mix scene camera and game camera information, so recalculate the distance each time in the Editor.
                for (var rendererIndex = 0; rendererIndex < rendererGroup.Renderers.Length; rendererIndex++)
                {
                    var target = rendererGroup.Renderers[rendererIndex];

                    if (!target)
                    {
                        continue;
                    }

                    target.CalculateDistanceToCamera(data.Camera.transform.position, data.Camera.transform.forward);
                }
#endif

                for (var rendererIndex = 0; rendererIndex < rendererGroup.Renderers.Length; rendererIndex++)
                {
                    var renderer = rendererGroup.Renderers[rendererIndex];

                    // Skip if renderer is null or inactive
                    if (!renderer
                        || renderer.SkipRendering)
                    {
                        continue;
                    }

                    var previousRenderer = rendererIndex > 0
                        ? rendererGroup.Renderers[rendererIndex - 1]
                        : null;
                    var nextRenderer = rendererIndex < rendererGroup.Renderers.Length - 1
                        ? rendererGroup.Renderers[rendererIndex + 1]
                        : null;

                    var args = new CubismRenderedEventArgs()
                    {
                        PassData = data,
                        CommandBuffer = commandBuffer,
                        ColorBuffer = renderer.RenderController.CurrentFrameBuffer,
                        DepthBuffer = data.CameraDepthTextureHandle,
                        SortingOrder = renderer.MeshRenderer.sortingOrder,
                        Drawable = renderer.Drawable,
                        SortingMode = renderer.RenderController.SortingMode,
                        GroupSortingOrder = rendererGroup.SortingIndex,
                        Distance = renderer.DistanceToCamera,
                        NextDistance = nextRenderer != null ? nextRenderer.DistanceToCamera : null,
                        PreviousDistance = previousRenderer != null ? previousRenderer.DistanceToCamera : null,
                        CameraPos = data.Camera.transform.position,
                        CameraForward = data.Camera.transform.forward
                    };

                    // Pre-rendering event.
                    CubismRenderingInterceptorsManager.GetInstance().OnPreRendering(args);

                    // Draw the object.
                    renderer.DrawObject(commandBuffer, data);

                    // Post-rendering event.
                    CubismRenderingInterceptorsManager.GetInstance().OnPostRendering(args);

                    if (renderer.IsLastDrawObjectInModel)
                    {
                        renderer.RenderController.SubmitDrawOffscreen(commandBuffer, data);
                    }
                }

                // Blit the result back to the camera texture if needed.
                if (CubismRenderControllerGroup.GetInstance().IsCopiedToCameraTexture)
                {
                    _commandBuffer.SetRenderTarget(data.CameraTextureHandle, data.CameraDepthTextureHandle);

                    // Draw the full-screen quad to blit the common rendering texture to the camera texture.
                    _blitRenderTextureMaterial.SetTexture(CubismShaderVariables.MainTexture, data.CommonRenderingTextureHandle);

                    // Check for reversed Z buffer.
                    var reversedZ = SystemInfo.usesReversedZBuffer ? GEqual : LEqual;
                    _blitRenderTextureMaterial.SetInt(CubismShaderVariables.ReversedZ, reversedZ);

                    // Draw the full-screen quad.
                    _commandBuffer.DrawMesh(_blitRenderTextureMesh, Matrix4x4.identity, _blitRenderTextureMaterial);

                    // Clear the common rendering texture for the next group.
                    _commandBuffer.SetRenderTarget(data.CommonRenderingTextureHandle);
                    _commandBuffer.ClearRenderTarget(true, true, Color.clear);
                }
            }

            // Reset the flag after processing.
            for (var i = 0; i < data.RenderControllers?.Length; i++)
            {
                var controller = data.RenderControllers[i];
                if (!controller || !controller.enabled || !controller.gameObject.activeInHierarchy)
                {
                    continue;
                }

                controller.DidChangeSorting = false;
                controller.DidChangeDrawableRenderOrder = false;
            }

            CubismRenderControllerGroup.GetInstance().DidChangeSortingRenderControllerGroup = false;
        }
        /// <summary>
        /// 录制完整 Cubism 合成：排序、遮罩、drawable、嵌套 offscreen、拦截事件与最终混合。
        /// 调用者在外部管理场景 Pass 和 Submit，本方法只切换 SDK 合成目标。
        /// </summary>
        /// <param name="data">Pass data containing render controllers, camera data, and texture handles.</param>
        /// <param name="commandBuffer">调用者提供的命令缓冲。</param>
        public void Render(CommandBuffer commandBuffer, CubismRenderingContext data)
        {
            // Check if we have any render controllers to process
            if (data.RenderControllers == null || data.RenderControllers.Length == 0)
            {
                return;
            }

            _commandBuffer = commandBuffer;

            if (!_blitRenderTextureMesh)
            {
                _blitRenderTextureMesh = new Mesh
                {
                    vertices = CubismRenderer.OffscreenVertices,
                    uv = CubismRenderer.OffscreenUVs,
                    triangles = CubismRenderer.OffscreenTriangle
                };

                _blitRenderTextureMesh.RecalculateBounds();
            }

            if (!_blitRenderTextureMaterial)
            {
                _blitRenderTextureMaterial = new Material(CubismBuiltinMaterials.UnlitBlit);
            }

            // 不读取 sceneColor：MRP 的场景 MSAA RT 可为 bindMS=true；高级混合只读 Common/Temporary。

            // Sort the renderers by their sorting order.
            PrepareSorting(data);

            // 清理 offscreen 会切换当前目标，必须放在下面重新绑定 Common 之前。
            // 否则第一个 PreRendering 拦截器（例如背景视频）会画到最后一张 offscreen。
            CubismOffscreenRenderTextureManager.GetInstance().ClearRenderTextures(_commandBuffer);

            // Set render target with both color and depth buffers for proper depth testing
            _commandBuffer.SetRenderTarget(data.CommonRenderingTextureHandle, data.CameraDepthTextureHandle);
            _commandBuffer.ClearRenderTarget(false, true, Color.clear);

            // Draw the objects.
            DrawObjects(_commandBuffer, data);

            // Blit the result back to the camera texture.
            if (!CubismRenderControllerGroup.GetInstance().IsCopiedToCameraTexture)
            {
                _commandBuffer.SetRenderTarget(data.CameraTextureHandle, data.CameraDepthTextureHandle);

                // Draw the full-screen quad to blit the common rendering texture to the camera texture.
                _blitRenderTextureMaterial.SetTexture(CubismShaderVariables.MainTexture, data.CommonRenderingTextureHandle);

                // Check for reversed Z buffer.
                var reversedZ = SystemInfo.usesReversedZBuffer? GEqual : LEqual;
                _blitRenderTextureMaterial.SetInt(CubismShaderVariables.ReversedZ, reversedZ);

                // Draw the full-screen quad.
                _commandBuffer.DrawMesh(_blitRenderTextureMesh, Matrix4x4.identity, _blitRenderTextureMaterial);
            }

            // Clear the common rendering texture for the next frame.
            _commandBuffer.SetRenderTarget(data.CommonRenderingTextureHandle);
            _commandBuffer.ClearRenderTarget(true, true, Color.clear);
        }
        private void PrepareSorting(CubismRenderingContext data)
        {
            // 排序可复用，framebuffer 不能复用：RenderScale/MSAA/窗口或 RG 资源可能已重建。
            for (int i = 0; i < data.RenderControllers.Length; i++)
            {
                if (!data.RenderControllers[i]) continue;
                data.RenderControllers[i].CurrentFrameBuffer = data.CommonRenderingTextureHandle;
                data.RenderControllers[i].CurrentOffscreenUnmanagedIndex = -1;
            }
            // 过滤组或相机改变时，旧 SDK 的全局 dirty 标志不足以识别本实例缓存失效。
            data.ForceSorting |= _previousCamera != data.Camera || _previousGroups != data.RenderControllerGroupDaraArray;
            if (_previousGroups != data.RenderControllerGroupDaraArray)
                _sortedRendererGroupDataArray = Array.Empty<RendererGroupData>();
            _previousCamera = data.Camera;
            _previousGroups = data.RenderControllerGroupDaraArray;
            SortingRendererGroups(data);
            data.ForceSorting = false;
        }

        /// <summary>
        /// 只有 Normal/Over、无遮罩/离屏/拦截器时的快路径。
        /// 调用者已经绑定目标；本方法不切 RT、不 Blit、不申请任何临时颜色纹理。
        /// </summary>
        public void RenderSimple(CommandBuffer buffer, CubismRenderingContext data)
        {
            if (data.RenderControllers == null || data.RenderControllers.Length == 0) return;
            PrepareSorting(data);
            CheckRenderingSkip(data);
            for (int group = 0; group < _sortedRendererGroupDataArray.Length; group++)
            {
                var renderers = _sortedRendererGroupDataArray[group].Renderers;
                for (int i = 0; i < renderers.Length; i++)
                    if (renderers[i] && !renderers[i].SkipRendering) renderers[i].DrawSimple(buffer);
            }
            for (int i = 0; i < data.RenderControllers.Length; i++)
            {
                var controller = data.RenderControllers[i];
                if (!controller) continue;
                controller.DidChangeSorting = false;
                controller.DidChangeDrawableRenderOrder = false;
            }
            CubismRenderControllerGroup.GetInstance().DidChangeSortingRenderControllerGroup = false;
        }

        public void Dispose()
        {
            DestroyOwned(_blitRenderTextureMesh);
            DestroyOwned(_blitRenderTextureMaterial);
            _blitRenderTextureMesh = null;
            _blitRenderTextureMaterial = null;
            _commandBuffer = null;
            _sortedRendererGroupDataArray = Array.Empty<RendererGroupData>();
        }

        private static void DestroyOwned(UnityEngine.Object target)
        {
            if (!target) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
