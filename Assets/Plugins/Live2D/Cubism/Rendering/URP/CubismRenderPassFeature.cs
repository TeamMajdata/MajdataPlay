/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using Live2D.Cubism.Core;
using Live2D.Cubism.Rendering.URP.RenderingInterceptor;
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

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
        public CubismRenderPassFeature.CubismRenderPass.PassData PassData;
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
        public TextureHandle DepthBuffer;
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

    /// <summary>
    /// Scriptable renderer feature for rendering Cubism models in URP.
    /// </summary>
    public class CubismRenderPassFeature : ScriptableRendererFeature
    {
        /// <summary>
        /// Greater than or equal to comparison value on Z Test.
        /// </summary>
        internal static readonly int GEqual = 7;

        /// <summary>
        /// Less than or equal to comparison value on Z Test.
        /// </summary>
        internal static readonly int LEqual = 4;

        /// <summary>
        /// Scriptable render pass for rendering Cubism models.
        /// </summary>
        public class CubismRenderPass : ScriptableRenderPass
        {
            /// <summary>
            /// Structure that groups renderers by their sorting index.
            /// </summary>
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
            private static CommandBuffer _commandBuffer;

            /// <summary>
            /// Array of renderer groups sorted by their sorting index.
            /// </summary>
            private static RendererGroupData[] _sortedRendererGroupDataArray;

            /// <summary>
            /// Mesh used for blitting render textures.
            /// </summary>
            private static Mesh _blitRenderTextureMesh;

            /// <summary>
            /// Material used for blitting render textures.
            /// </summary>
            private static Material _blitRenderTextureMaterial;

            /// <summary>
            /// This class stores the data needed by the RenderGraph pass.
            /// It is passed as a parameter to the delegate function that executes the RenderGraph pass.
            /// </summary>
            public class PassData
            {
                /// <summary>
                /// Array of render controllers to be rendered.
                /// </summary>
                public CubismRenderController[] RenderControllers;

                /// <summary>
                /// Array of render controller groups.
                /// </summary>
                public CubismRenderControllerGroup.RenderControllerGroupData[] RenderControllerGroupDaraArray;

                /// <summary>
                /// Camera data for the current rendering pass.
                /// </summary>
                public UniversalCameraData CameraData;

                /// <summary>
                /// Resource data for the current rendering pass.
                /// </summary>
                public UniversalResourceData ResourceData;

                /// <summary>
                /// Texture handle for mask rendering.
                /// </summary>
                public TextureHandle MaskTextureHandle;

                /// <summary>
                /// Texture handle for the camera render target.
                /// </summary>
                public TextureHandle CameraTextureHandle;

                /// <summary>
                /// Texture handle for the camera depth buffer.
                /// </summary>
                public TextureHandle CameraDepthTextureHandle;

                /// <summary>
                /// Texture handle for common rendering operations.
                /// </summary>
                public TextureHandle CommonRenderingTextureHandle;

                /// <summary>
                /// Texture handle for temporary rendering operations.
                /// </summary>
                public TextureHandle CommonTemporaryTextureHandle;
            }

            /// <summary>
            /// Sets up and fills the renderer group.
            /// </summary>
            /// <param name="targetIndex">Target index in the sorted renderer groups array.</param>
            /// <param name="target">Added target renderer group.</param>
            /// <param name="source">Source render controller group.</param>
            /// <param name="cameraPos">Position of the camera</param>
            /// <param name="data">Pass data containing render controller groups and camera data</param>
            private static void SetUpRendererGroup(int targetIndex, RendererGroupData target, CubismRenderControllerGroup.RenderControllerGroupData source, Vector3 cameraPos, PassData data)
            {
                var previousCount = 0;
                for (var renderControllerIndex = 0; renderControllerIndex < source.Controllers.Length; renderControllerIndex++)
                {
                    var controller = source.Controllers[renderControllerIndex];

                    controller.CurrentFrameBuffer = data.CommonRenderingTextureHandle;

                    if (!controller || controller.Renderers == null)
                    {
                        continue;
                    }

                    // Copy the renderers from the controller to the sorted array.
                    for (var rendererIndex = 0; rendererIndex < controller.Renderers.Length; rendererIndex++)
                    {
                        target.Renderers[previousCount + rendererIndex] = controller.Renderers[rendererIndex];
                        target.Renderers[previousCount + rendererIndex].IsLastDrawObjectInModel = false;
                    }

                    // Update the previous count for the next controller
                    previousCount += controller.Renderers.Length;
                }

                // Sort the renderers by their sorting order.
                SortBySortingOrder(target.Renderers, cameraPos, data.CameraData.camera.transform.forward);

                // Assign the filled target to the sorted renderer groups array.
                _sortedRendererGroupDataArray[targetIndex] = target;
            }

            /// <summary>
            /// Sorts the renderer groups based on their sorting order.
            /// </summary>
            /// <param name="data">Pass data containing render controller groups.</param>
            private static void SortingRendererGroups(PassData data)
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
                        controller.UpdateDidChangeSortingFromZ(data.CameraData.worldSpaceCameraPos);

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
                    if (!didChangeSortingOrder)
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
                        SetUpRendererGroup(targetIndex, target, group, data.CameraData.worldSpaceCameraPos, data);

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
                    SetUpRendererGroup(_sortedRendererGroupDataArray.Length - 1, newRendererGroup, group, data.CameraData.worldSpaceCameraPos,data);
                }
            }

            /// <summary>
            /// Sorts the renderers by their sorting order.
            /// Calculates the distance to camera for each renderer and sorts them accordingly.
            /// </summary>
            /// <param name="renderers">Array of renderers to sort</param>
            /// <param name="cameraPosition">Position of the camera</param>
            /// <param name="cameraForward">Forward direction of the camera (normalized vector)</param>
            private static void SortBySortingOrder(CubismRenderer[] renderers, Vector3 cameraPosition, Vector3 cameraForward)
            {
                for (var index = 0; index < renderers.Length; index++)
                {
                    renderers[index].CalculateDistanceToCamera(cameraPosition, cameraForward);
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
            private static void CheckRenderingSkip()
            {
                // Reset skip rendering flag for each renderer.
                for (var groupIndex = 0; groupIndex < _sortedRendererGroupDataArray.Length; groupIndex++)
                {
                    var rendererGroup = _sortedRendererGroupDataArray[groupIndex];

                    for (var rendererIndex = 0; rendererIndex < rendererGroup.Renderers.Length; rendererIndex++)
                    {
                        rendererGroup.Renderers[rendererIndex].SkipRendering = false;
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
                                     || !renderer.RenderController.gameObject.activeSelf
                                     || !renderer.RenderController.enabled
                                     || !renderer.gameObject.activeSelf
                                     || !renderer.MeshRenderer
                                     || !renderer.MeshRenderer.enabled;

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
                        targetRenderer.IsLastDrawObjectInModel = false;
                    }

                    for (var controllerIndex = 0; controllerIndex < target.RenderControllers.Length; controllerIndex++)
                    {
                        var controller = target.RenderControllers[controllerIndex];

                        if (!controller
                            || !controller.enabled
                            || !controller.gameObject.activeSelf)
                        {
                            continue;
                        }

                        var lastIndex = target.Renderers.Length - 1;
                        while (lastIndex >= 0)
                        {
                            var targetRenderer = target.Renderers[lastIndex];

                            // Find the last draw renderer in the sorted array.
                            if (!targetRenderer.SkipRendering
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
            private static void DrawObjects(CommandBuffer commandBuffer, PassData data)
            {
                // Clear offscreen render textures at the beginning of the draw call.
                CubismOffscreenRenderTextureManager.GetInstance().ClearRenderTextures(_commandBuffer);

                // Check and set skip rendering flags for each renderer.
                CheckRenderingSkip();

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

                        target.CalculateDistanceToCamera(data.CameraData.worldSpaceCameraPos, data.CameraData.camera.transform.forward);
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
                            CameraPos = data.CameraData.worldSpaceCameraPos,
                            CameraForward = data.CameraData.camera.transform.forward
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
                    if (!controller || !controller.enabled || !controller.gameObject.activeSelf)
                    {
                        continue;
                    }

                    controller.DidChangeSorting = false;
                    controller.DidChangeDrawableRenderOrder = false;
                }

                CubismRenderControllerGroup.GetInstance().DidChangeSortingRenderControllerGroup = false;
            }

            /// <summary>
            /// ExecutePass is the function that executes the render pass.
            /// This static method is passed as the RenderFunc delegate to the RenderGraph render pass.
            /// It is used to execute draw commands.
            /// </summary>
            /// <param name="data">Pass data containing render controllers, camera data, and texture handles.</param>
            /// <param name="context">Render graph context for executing render commands.</param>
            private static void ExecutePass(PassData data, UnsafeGraphContext context)
            {
                // Check if we have any render controllers to process
                if (data.RenderControllers == null || data.RenderControllers.Length == 0)
                {
                    return;
                }

                if (_commandBuffer == null)
                {
                    _commandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                }

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

#if UNITY_EDITOR
                // HACK: In the editor, Scene view camera may not have the latest texture data.
                if (data.CameraData.isSceneViewCamera)
                {
                    _commandBuffer.Blit(data.CameraTextureHandle, data.CommonRenderingTextureHandle);
                }
#endif

                // Sort the renderers by their sorting order.
                SortingRendererGroups(data);

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

            /// <summary>
            /// This method is called by the render graph to record render passes.
            /// RecordRenderGraph is where the RenderGraph handle can be accessed, through which render passes can be added to the graph.
            /// FrameData is a context container through which URP resources can be accessed and managed.
            /// </summary>
            /// <param name="renderGraph">Render graph to add render passes to.</param>
            /// <param name="frameData">Context container providing access to URP resources and camera data.</param>
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                const string renderCustomPass = "Cubism URP Render Pass";

                // Get render controllers - this should work in both play mode and edit mode
                var renderControllers = CubismRenderControllerGroup.GetInstance().RenderControllers;

                // Skip if no render controllers are available
                if (renderControllers == null || renderControllers.Length == 0)
                {
                    return;
                }

                // This adds a raster render pass to the graph, specifying the name and the data type that will be passed to the ExecutePass function.
                using (var builder = renderGraph.AddUnsafePass<PassData>(renderCustomPass, out var passData))
                {
                    // Set up pass data
                    passData.RenderControllers = renderControllers;

                    var renderControllerGroups = CubismRenderControllerGroup.GetInstance().GroupDataArray;
                    passData.RenderControllerGroupDaraArray = renderControllerGroups;

                    var cameraData = frameData.Get<UniversalCameraData>();
                    var resourceData = frameData.Get<UniversalResourceData>();

                    passData.CameraData = cameraData;
                    passData.ResourceData = resourceData;

                    var descriptor = resourceData.activeColorTexture.GetDescriptor(renderGraph);
                    descriptor.wrapMode = TextureWrapMode.Repeat;
                    descriptor.filterMode = FilterMode.Point;

                    descriptor.name = "CommonTexture";
                    passData.CommonRenderingTextureHandle = renderGraph.CreateTexture(descriptor);
                    builder.UseTexture(passData.CommonRenderingTextureHandle, AccessFlags.ReadWrite);

                    descriptor.name = "TempTexture";
                    passData.CommonTemporaryTextureHandle = renderGraph.CreateTexture(descriptor);
                    builder.UseTexture(passData.CommonTemporaryTextureHandle, AccessFlags.ReadWrite);

                    descriptor.name = "MaskTexture";
                    var maskTextureHandle = renderGraph.CreateTexture(descriptor);
                    builder.UseTexture(maskTextureHandle, AccessFlags.ReadWrite);
                    passData.MaskTextureHandle = maskTextureHandle;

                    // This sets the render target of the pass to the active color texture. Change it to your own render target as needed.
                    passData.CameraTextureHandle = resourceData.activeColorTexture;
                    builder.UseTexture(passData.CameraTextureHandle, AccessFlags.ReadWrite);

                    // Set up depth buffer for proper depth testing with other Unity objects
                    passData.CameraDepthTextureHandle = resourceData.activeDepthTexture;
                    builder.UseTexture(passData.CameraDepthTextureHandle);

                    // Assigns the ExecutePass function to the render pass delegate. This will be called by the render graph when executing the pass.
                    builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
                }
            }
        }

        /// <summary>
        /// Scriptable render pass that will be injected into the renderer.
        /// </summary>
        private CubismRenderPass _mScriptablePass;

        /// <summary>
        /// Creates the scriptable render pass and configures its injection point.
        /// </summary>
        public override void Create()
        {
            _mScriptablePass = new CubismRenderPass
            {
                // Configures where the render pass should be injected.
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents
            };
        }

        /// <summary>
        /// Injects one or multiple render passes into the renderer.
        /// This method is called when setting up the renderer once per-camera.
        /// </summary>
        /// <param name="renderer">The scriptable renderer to add passes to.</param>
        /// <param name="renderingData">Rendering state information.</param>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
#if UNITY_EDITOR
            // Skip adding the pass for scene view and game view cameras in the editor.
            if (!(renderingData.cameraData.cameraType == CameraType.Game
                || renderingData.cameraData.cameraType == CameraType.SceneView))
            {
                return;
            }
#endif

            renderer.EnqueuePass(_mScriptablePass);
        }
    }
}
