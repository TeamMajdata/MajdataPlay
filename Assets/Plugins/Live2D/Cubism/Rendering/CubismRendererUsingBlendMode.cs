/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using Live2D.Cubism.Core;
using Live2D.Cubism.Rendering.URP;
using Live2D.Cubism.Rendering.Util;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;


namespace Live2D.Cubism.Rendering
{
    /// <summary>
    /// Partial class that describes processing related to the rendering method added in Cubism 5.3 and later.
    /// </summary>
    public partial class CubismRenderer
    {
        #region Values

        /// <summary>
        /// High precision mask tile for mask rendering.
        /// </summary>
        private static Vector4 HighPrecisionMaskTile = new Vector4(
            0, // Channel R
            0, // Column
            0, // Row
            1 // Size
        );

        /// <summary>
        /// Vertices for offscreen rendering.
        /// </summary>
        internal static Vector3[] OffscreenVertices = new Vector3[]
        {
            new Vector3(-1, -1, 0),
            new Vector3(1, -1, 0),
            new Vector3(1, 1, 0),
            new Vector3(-1, 1, 0)
        };

        /// <summary>
        /// UVs for offscreen rendering.
        /// </summary>
        internal static Vector2[] OffscreenUVs = new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(1, 1),
            new Vector2(0, 1)
        };

        /// <summary>
        /// Triangle indices for offscreen rendering.
        /// </summary>
        internal static int[] OffscreenTriangle = new int[] { 0, 1, 2, 0, 2, 3 };

        /// <summary>
        /// Masks used by this renderer.
        /// </summary>
        [SerializeField, HideInInspector]
        private CubismRenderer[] _masks;

        /// <summary>
        /// Transform values for mask rendering.
        /// </summary>
        private Vector4 _maskTransform;

        /// <summary>
        /// Bounds that contain all masks.
        /// </summary>
        private Bounds _maskBounds;

#if UNITY_EDITOR
        /// <summary>
        /// Whether <see cref="_masks"/> have null.
        /// </summary>
        private bool _haveMasksNull;
#endif

        /// <summary>
        /// Index of the draw object.
        /// </summary>
        public int DrawObjectUnmanagedIndex { get; set; }

        /// <summary>
        /// <see cref="CubismOffscreen"/>.
        /// </summary>
        public CubismOffscreen Offscreen { get; set; }


        /// <summary>
        /// Color blend types.
        /// </summary>
        [SerializeField]
        public BlendTypes.ColorBlend ColorBlendType;

        /// <summary>
        /// Alpha blend types.
        /// </summary>
        [SerializeField]
        public BlendTypes.AlphaBlend AlphaBlendType;

        /// <summary>
        /// Type of draw object this renderer is used for.
        /// </summary>
        [SerializeField]
        public CubismModelTypes.DrawObjectType DrawObjectType;

        /// <summary>
        /// Whether this is the last draw object in the model.
        /// </summary>
        internal bool IsLastDrawObjectInModel;

        /// <summary>
        /// <see cref="OffscreenFrameBuffer"/>'s backing field.
        /// </summary>
        private RenderTexture _offscreenFrameBuffer;

        /// <summary>
        /// Offscreen render texture used for rendering.
        /// </summary>
        public RenderTexture OffscreenFrameBuffer
        {
            get
            {
                if (!_offscreenFrameBuffer
                    && RenderController.CurrentFrameBuffer
                    && (RenderController.OffscreenRenderers?.Length ?? 0) > 0)
                {
                    _offscreenFrameBuffer = CubismOffscreenRenderTextureManager.GetInstance().GetOffscreenRenderTexture(
                        RenderController.CurrentFrameBuffer);
                }

                return _offscreenFrameBuffer;
            }

            set
            {
                if (_offscreenFrameBuffer != null)
                {
                    CubismOffscreenRenderTextureManager.GetInstance().StopUsingRenderTexture(RenderController, _offscreenFrameBuffer);
                    _offscreenFrameBuffer = null;
                }
                _offscreenFrameBuffer = value;
            }
        }

        /// <summary>
        /// <see cref="OffsetScale"/>'s backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private Vector4 _offsetScale = new Vector4(0, 0, 1, 1);

        /// <summary>
        /// <see cref="Quaternion"/>'s backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private Vector4 _quaternion = Vector4.zero;

        /// <summary>
        /// <see cref="ZOffset"/>'s backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private float _zOffset = 0.0f;

        /// <summary>
        /// Offscreen mesh used for rendering.
        /// </summary>
        private Mesh _offscreenMesh;

        /// <summary>
        /// Previous offscreen unmanaged index.
        /// </summary>
        private int _previousOffscreenUnmanagedIndex;

        /// <summary>
        /// Whether to skip rendering for this draw object.
        /// </summary>
        public bool SkipRendering
        {
            get;
            set;
        }

        #endregion

        #region Interface For CubismRenderController

        /// <summary>
        /// Sets draw object's render order.
        /// </summary>
        /// <param name="newRenderOrder"></param>
        internal void SetDrawObjectRenderOrder(int newRenderOrder)
        {
            if (RenderOrder == newRenderOrder) return;

            RenderOrder = newRenderOrder;

            ApplySorting();
        }

        #endregion

        /// <summary>
        /// Sorting direction at the last sorting.
        /// </summary>
        internal Vector3 LastDirection;

        /// <summary>
        /// Whether the direction has been updated since the last sorting.
        /// </summary>
        /// <returns>True if the direction has changed, false otherwise.</returns>
        internal bool DidUpdateDirectionFromLastSorted(Vector3 cameraPosition)
        {
            return LastDirection != (transform.position - cameraPosition);
        }

        /// <summary>
        /// Distance from the active camera.
        /// </summary>
        internal float DistanceToCamera;

        /// <summary>
        /// Calculates the distance by projecting the vector from camera to renderer onto the camera's forward direction.
        /// </summary>
        /// <param name="cameraPosition">Position of the camera.</param>
        /// <param name="cameraForward">Forward direction of the camera (normalized vector).</param>
        internal void CalculateDistanceToCamera(Vector3 cameraPosition, Vector3 cameraForward)
        {
            // Vector from cameraPosition to transform.position
            var directionToRenderer = transform.position - cameraPosition;

            LastDirection = directionToRenderer;

            // Project the vector onto the camera's forward direction
            var projection = Vector3.Project(directionToRenderer, cameraForward);

            // The magnitude of the projected vector is the distance along the camera's forward direction
            // Formula: projection = dot(directionToRenderer, cameraForward) * cameraForward
            // Distance = |projection| = |dot(directionToRenderer, cameraForward)|
            DistanceToCamera = projection.magnitude;
        }

        /// <summary>
        /// <see cref="PropertyBlock"/> backing field.
        /// </summary>
        private MaterialPropertyBlock _propertyBlock;

        /// <summary>
        /// <see cref="MaterialPropertyBlock"/>
        /// </summary>
        private MaterialPropertyBlock PropertyBlock
        {
            get
            {
                // Lazily initialize.
                if (_propertyBlock == null)
                {
                    _propertyBlock = new MaterialPropertyBlock();
                }


                return _propertyBlock;
            }
        }

        /// <summary>
        /// Applies common rendering texture for rendering.
        /// </summary>
        /// <param name="passData">Pass data containing render controllers and camera data.</param>
        private void ApplyBlendedRenderTexture(CubismRenderPassFeature.CubismRenderPass.PassData passData)
        {
            if (!RenderController?.CurrentFrameBuffer)
            {
                return;
            }

            var property = PropertyBlock;

            MeshRenderer.GetPropertyBlock(property);

            // Write property.
            property.SetTexture(CubismShaderVariables.RenderTexture, passData.CommonTemporaryTextureHandle);

            MeshRenderer.SetPropertyBlock(property);
        }

        /// <summary>
        /// Adds this renderer to the command buffer for rendering.
        /// </summary>
        /// <param name="buffer">Command buffer to record draw commands.</param>
        /// <param name="passData">Pass data containing render controllers and camera data.</param>
        public void DrawObject(CommandBuffer buffer, CubismRenderPassFeature.CubismRenderPass.PassData passData)//, RenderTexture frameBuffer)
        {
            if (!MeshRenderer)
            {
                return;
            }

            switch (DrawObjectType)
            {
                case CubismModelTypes.DrawObjectType.Offscreen:
                    // Set offscreen frame buffer.
                    SetOffscreen(buffer, passData);
                    break;
                case CubismModelTypes.DrawObjectType.Drawable:
                    // Draw a drawable.
                    DrawDrawable(buffer, passData);
                    break;
            }
        }

        /// <summary>
        /// Applies transform properties to the material property block.
        /// </summary>
        private void ApplyTransform()
        {
            var property = PropertyBlock;
            MeshRenderer.GetPropertyBlock(property);

            // Set offset and scale from transform.
            var offsetScale = _offsetScale;

            offsetScale.Set(RenderController.transform.localPosition.x + transform.localPosition.x, RenderController.transform.localPosition.y + transform.localPosition.y,
                RenderController.transform.localScale.x * transform.localScale.x, RenderController.transform.localScale.y * transform.localScale.y);
            _offsetScale = offsetScale;
            // Write property.
            property.SetVector(CubismShaderVariables.OffsetScale, _offsetScale);

            // Set rotation from transform.
            var combinedRotation = RenderController.transform.localRotation * transform.localRotation;
            _quaternion.Set(combinedRotation.x, combinedRotation.y, combinedRotation.z, combinedRotation.w);

            // Write property.
            property.SetVector(CubismShaderVariables.RotationQuaternion, _quaternion);

            // Set z offset from transform.
            _zOffset = RenderController.transform.localPosition.z + transform.localPosition.z;
            // Write property.
            property.SetFloat(CubismShaderVariables.ZOffset, _zOffset);

            MeshRenderer.SetPropertyBlock(property);
        }

        /// <summary>
        /// Sets the offscreen frame buffer for rendering.
        /// </summary>
        /// <param name="buffer">Command buffer to record draw commands.</param>
        /// <param name="passData">Pass data containing render controllers and camera data.</param>
        private void SetOffscreen(CommandBuffer buffer, CubismRenderPassFeature.CubismRenderPass.PassData passData)
        {
            var currentOffscreenUnmanagedIndex = RenderController.CurrentOffscreenUnmanagedIndex;
            SubmitDrawToParentOffscreen(ref passData, ref currentOffscreenUnmanagedIndex,
                buffer, this);

            // Ready the render target to the offscreen frame buffer.
            OffscreenFrameBuffer = CubismOffscreenRenderTextureManager.GetInstance().GetOffscreenRenderTexture(passData.CommonRenderingTextureHandle);

            // Set current frame buffer to offscreen frame buffer.
            buffer.SetRenderTarget(OffscreenFrameBuffer);
            // Clear the offscreen frame buffer.
            buffer.ClearRenderTarget(false, true, Color.clear);

            // Set up for drawing to offscreen.
            RenderController.CurrentFrameBuffer = OffscreenFrameBuffer;
            RenderController.CurrentOffscreenUnmanagedIndex = Offscreen.UnmanagedIndex;
        }

        /// <summary>
        /// Gets the bounds that can contain all masks.
        /// </summary>
        /// <returns></returns>
        private Bounds GetMaskBounds()
        {
            // If there are no masks, return empty bounds.
            if (_masks == null
                || _masks.Length < 1)
            {
                return new Bounds();
            }

            var min = _masks[0]?.Mesh?.bounds.min ?? Vector3.zero;
            var max = _masks[0]?.Mesh?.bounds.max ?? Vector3.zero;


            for (var i = 1; i < _masks.Length; ++i)
            {
                // Skip if the mask is null.
                if (!_masks[i])
                {
                    continue;
                }

                var boundsI = _masks[i].Mesh.bounds;


                if (boundsI.min.x < min.x)
                {
                    min.x = boundsI.min.x;
                }

                if (boundsI.max.x > max.x)
                {
                    max.x = boundsI.max.x;
                }


                if (boundsI.min.y < min.y)
                {
                    min.y = boundsI.min.y;
                }

                if (boundsI.max.y > max.y)
                {
                    max.y = boundsI.max.y;
                }
            }

            var bounds = _maskBounds;
            bounds.SetMinMax(min, max);
            _maskBounds = bounds;

            return _maskBounds;
        }

        /// <summary>
        /// Calculates the transform values for mask rendering based on mask bounds.
        /// </summary>
        private void CalcMaskTransform()
        {
            // Compute bounds and scale.
            var bounds = GetMaskBounds();
            var scale = (bounds.size.x > bounds.size.y)
                ? bounds.size.x
                : bounds.size.y;

            // Compute mask transform.
            var maskTransform = _maskTransform;
            maskTransform.Set(
                bounds.center.x, // Offset X
                bounds.center.y, // Offset Y
                1.0f / scale, // Scale
                0 // Dummy, unused.
                  );
            _maskTransform = maskTransform;
        }

        /// <summary>
        /// Draws the mask.
        /// </summary>
        /// <param name="buffer">Command buffer to record draw commands.</param>
        /// <param name="passData">Pass data containing render controllers and camera data.</param>
        internal void DrawMasks(CommandBuffer buffer, CubismRenderPassFeature.CubismRenderPass.PassData passData)
        {
            // If the model doesn't have masks, return.
            if (!RenderController.HasMask)
            {
                return;
            }

#if UNITY_EDITOR
            // If the masks have null, try to initialize them.
            if (_haveMasksNull)
            {
                TryInitializeMasks();
                _haveMasksNull = false;
            }
#endif

            if (_masks?.Length > 0)
            {
                var maskTexture = passData.MaskTextureHandle;

                buffer.SetRenderTarget(maskTexture);
                buffer.ClearRenderTarget(true, true, Color.clear);

                // Calculate mask transform.
                CalcMaskTransform();

                // Draw the masks.
                for (var maskIndex = 0; maskIndex < _masks.Length; maskIndex++)
                {
                    var mask = _masks[maskIndex];

                    if (!mask)
                    {
#if UNITY_EDITOR
                        _haveMasksNull = true;
#endif
                        continue;
                    }

                    switch (DrawObjectType)
                    {
                        case CubismModelTypes.DrawObjectType.Drawable:
                            mask.PropertyBlock.SetTexture(CubismShaderVariables.MainTexture, mask.MainTexture);
                            mask.PropertyBlock.SetVector(CubismShaderVariables.MaskTile, HighPrecisionMaskTile);
                            mask.PropertyBlock.SetVector(CubismShaderVariables.MaskTransform, _maskTransform);

                            // Draw the mesh with the material.
                            buffer.DrawMesh(
                                mask.Mesh,
                                Matrix4x4.identity,
                                mask.Drawable.IsDoubleSided
                                    ? CubismBuiltinMaterials.Mask
                                    : CubismBuiltinMaterials.MaskCulling,
                                0,
                                0,
                                mask.PropertyBlock);
                            break;
                        case CubismModelTypes.DrawObjectType.Offscreen:
                            mask.PropertyBlock.SetTexture(CubismShaderVariables.MainTexture, mask.MainTexture);
                            mask.ApplyTransform();

                            // Draw the mesh with the material.
                            buffer.DrawMesh(
                                mask.Mesh,
                                Matrix4x4.identity,
                                CubismBuiltinMaterials.OffscreenMask,
                                0,
                                0,
                                mask.PropertyBlock);
                            break;
                        default:
                            Debug.LogWarning("Unknown DrawObjectType.");
                            break;
                    }
                }

                ApplyMask(maskTexture);
            }
        }

        /// <summary>
        /// Submits the offscreen frame buffer and draws to the parent offscreen if necessary.
        /// </summary>
        /// <param name="passData">Pass data containing render controllers and camera data.</param>
        /// <param name="currentOffscreenUnmanagedIndex">Current offscreen's unmanaged index.</param>
        /// <param name="buffer">Command buffer to record draw commands.</param>
        /// <param name="targetRenderer">Current target rendering object.</param>
        private void SubmitDrawToParentOffscreen(ref CubismRenderPassFeature.CubismRenderPass.PassData passData, ref int currentOffscreenUnmanagedIndex,
            CommandBuffer buffer, CubismRenderer targetRenderer)
        {
            if (passData == null
                || RenderController.CurrentFrameBuffer == (RenderTexture)passData.CommonRenderingTextureHandle
                || currentOffscreenUnmanagedIndex == -1)
            {
                return;
            }

            // Get the current offscreen renderer.
            CubismRenderer offscreenRenderer = null;
            for (var offscreenRendererIndex = 0; offscreenRendererIndex < RenderController.OffscreenRenderers.Length; offscreenRendererIndex++)
            {
                if (RenderController.OffscreenRenderers[offscreenRendererIndex].Offscreen.UnmanagedIndex !=
                    currentOffscreenUnmanagedIndex)
                {
                    continue;
                }

                offscreenRenderer = RenderController.OffscreenRenderers[offscreenRendererIndex];
                break;
            }
            var currentOwnerIndex = offscreenRenderer?.Offscreen.OwnerIndex ?? -1;

            if (currentOwnerIndex == -1)
            {
                // Fail silently if the offscreen renderer is not found.
                return;
            }

            var targetParentIndex = -1;
            RenderTexture previousOffscreen = null;
            switch (targetRenderer.DrawObjectType)
            {
                case CubismModelTypes.DrawObjectType.Drawable:
                    targetParentIndex = targetRenderer.Drawable.ParentPartIndex;
                    break;
                case CubismModelTypes.DrawObjectType.Offscreen:
                    // Check target renderer's offscreen owner type.
                    targetParentIndex = RenderController.Model.Parts[targetRenderer.Offscreen.OwnerIndex].UnmanagedParentIndex;
                    break;
                default:
                    // If the draw object type is not drawable or offscreen, return.
                    return;
            }

            // If targetRenderer isn't child of the current offscreen renderer.
            while (targetParentIndex != -1)
            {
                // If the target parent index is the same as the current owner index, return.
                if (targetParentIndex == RenderController.Model.Parts[currentOwnerIndex].UnmanagedIndex)
                {
                    return;
                }

                targetParentIndex = RenderController.Model.Parts[targetParentIndex].UnmanagedParentIndex;
            }
            var parentIndex = RenderController.Model.Parts[currentOwnerIndex].UnmanagedParentIndex;

            // Find the offscreen renderer with the previous offscreen unmanaged index.
            while (parentIndex != -1)
            {
                var part = RenderController.Model.Parts[parentIndex];
                if (part.OffscreenIndex != -1)
                {
                    for (var offscreenRendererIndex = 0; offscreenRendererIndex < RenderController.OffscreenRenderers.Length; offscreenRendererIndex++)
                    {
                        var element = RenderController.OffscreenRenderers[offscreenRendererIndex];

                        if (!element.isActiveAndEnabled
                            || !element.MeshRenderer.enabled
                            || element.Offscreen.UnmanagedIndex != part.OffscreenIndex)
                        {
                            continue;
                        }

                        previousOffscreen = RenderController.OffscreenRenderers[offscreenRendererIndex].OffscreenFrameBuffer;
                        _previousOffscreenUnmanagedIndex = part.OffscreenIndex;
                        break;
                    }
                }

                parentIndex = RenderController.Model.Parts[parentIndex].UnmanagedParentIndex;

                if (!previousOffscreen)
                {
                    continue;
                }

                break;
            }

            // Try to get the root part offscreen if there is no parent offscreen.
            if (!previousOffscreen && RenderController.HasRootPartOffscreen)
            {
                var offscreenRenderers = RenderController.OffscreenRenderers;

                for (var offscreenIndex = 0; offscreenIndex < offscreenRenderers.Length; offscreenIndex++)
                {
                    if (offscreenRenderers[offscreenIndex].Offscreen.UnmanagedIndex != 0)
                    {
                        continue;
                    }

                    previousOffscreen = offscreenRenderers[offscreenIndex].OffscreenFrameBuffer;
                    break;
                }
            }

            // If there is no parent offscreen, use the common rendering texture.
            if (!previousOffscreen)
            {
                previousOffscreen = passData.CommonRenderingTextureHandle;
            }

            if (previousOffscreen == offscreenRenderer?.OffscreenFrameBuffer)
            {
                return;
            }

            // If It can copy the parent offscreen, copy it to the current offscreen renderer.
            DrawOffscreen(buffer, previousOffscreen, offscreenRenderer, passData);

            offscreenRenderer.OffscreenFrameBuffer = null;
            RenderController.CurrentFrameBuffer = previousOffscreen;
            currentOffscreenUnmanagedIndex = _previousOffscreenUnmanagedIndex;

            // If the current offscreen is the parent of the target renderer, draw to the parent offscreen.
            SubmitDrawToParentOffscreen(ref passData, ref currentOffscreenUnmanagedIndex,
                buffer, targetRenderer);
        }

        /// <summary>
        /// Renders a drawable.
        /// </summary>
        /// <param name="buffer">Command buffer to record draw commands.</param>
        /// <param name="passData">Pass data containing render controllers and camera data.</param>
        private void DrawDrawable(CommandBuffer buffer, CubismRenderPassFeature.CubismRenderPass.PassData passData)
        {
            if (!RenderController)
            {
                return;
            }

            if (RenderController.CurrentOffscreenUnmanagedIndex != -1)
            {
                var currentOffscreenOwnerUnmanagedIndex = RenderController.CurrentOffscreenUnmanagedIndex;
                SubmitDrawToParentOffscreen(ref passData, ref currentOffscreenOwnerUnmanagedIndex,
                    buffer, this);

                RenderController.CurrentOffscreenUnmanagedIndex = currentOffscreenOwnerUnmanagedIndex;
            }

            // Mask rendering.
            DrawMasks(buffer, passData);

            // Set property block.
            ApplyMainTexture();
            ApplyBlendedRenderTexture(passData);
            ApplyScreenColor();
            ApplyMultiplyColor();
            ApplyVertexColors();
            ApplyTransform();

            // In the case of color blending before Cubism 5.2.
            if ((ColorBlendType == BlendTypes.ColorBlend.Normal
                && AlphaBlendType == BlendTypes.AlphaBlend.Over)
                || ColorBlendType == BlendTypes.ColorBlend.Add
                || ColorBlendType == BlendTypes.ColorBlend.Multiply)
            {
                // If the current frame buffer is different from the common rendering texture, update it.
                // HACK: Assumes that the size has already been corrected in the `SetOffscreen()` function for cases where drawing occurs to the offscreen.
                if (!RenderController.CurrentFrameBuffer
                    || RenderController.CurrentFrameBuffer.width != ((RenderTexture)passData.CameraDepthTextureHandle).width
                    || RenderController.CurrentFrameBuffer.height != ((RenderTexture)passData.CameraDepthTextureHandle).height)
                {
                    RenderController.CurrentFrameBuffer = passData.CommonRenderingTextureHandle;
                }

                // Set render target with depth buffer for proper depth testing
                buffer.SetRenderTarget(RenderController.CurrentFrameBuffer, passData.CameraDepthTextureHandle);

                // Draw the mesh with the material.
                buffer.DrawMesh(Mesh, Matrix4x4.identity, DrawMaterial ?? Material, 0, 0, PropertyBlock);

                return;
            }

            // Blit to temporary texture.
            buffer.Blit(RenderController.CurrentFrameBuffer, passData.CommonTemporaryTextureHandle);

            // Set temporary render target.
            buffer.SetRenderTarget(RenderController.CurrentFrameBuffer, passData.CameraDepthTextureHandle);

            // Draw the mesh with the material.
            buffer.DrawMesh(Mesh, Matrix4x4.identity, DrawMaterial ?? Material, 0, 0, PropertyBlock);
        }

        /// <summary>
        /// Copies the current drawable to the parent offscreen.
        /// </summary>
        /// <param name="buffer">Command buffer to record draw commands.</param>
        /// <param name="previousOffscreen">Previous offscreen render texture.</param>
        /// <param name="currentOffscreenRenderer">Current offscreen renderer.</param>
        /// <param name="passData">Pass data containing render controllers and camera data.</param>
        internal void DrawOffscreen(CommandBuffer buffer, RenderTexture previousOffscreen, CubismRenderer currentOffscreenRenderer, CubismRenderPassFeature.CubismRenderPass.PassData passData)
        {
            if (previousOffscreen == null
                || currentOffscreenRenderer == null)
            {
                return;
            }

            // Draw the mesh with the material.
            currentOffscreenRenderer.DrawOffscreenMesh(buffer, previousOffscreen, passData);
        }

        /// <summary>
        /// Draws the mesh for offscreen rendering.
        /// </summary>
        /// <param name="buffer">Command buffer to record draw commands.</param>
        /// <param name="previousOffscreen">Previous offscreen render texture.</param>
        /// <param name="passData">Pass data containing render controllers and camera data.</param>
        internal void DrawOffscreenMesh(CommandBuffer buffer, RenderTexture previousOffscreen, CubismRenderPassFeature.CubismRenderPass.PassData passData)
        {
            // Mask rendering.
            DrawMasks(buffer, passData);

            ApplyPropertyForOffscreen(previousOffscreen);

            // In the case of color blending before Cubism 5.2.
            if ((ColorBlendType == BlendTypes.ColorBlend.Normal
                && AlphaBlendType == BlendTypes.AlphaBlend.Over)
                || ColorBlendType == BlendTypes.ColorBlend.Add
                || ColorBlendType == BlendTypes.ColorBlend.Multiply)
            {
                buffer.SetRenderTarget(previousOffscreen, passData.CameraDepthTextureHandle);

                // Draw the mesh with the material.
                buffer.DrawMesh(Mesh, Matrix4x4.identity, DrawMaterial ?? Material, 0, 0, PropertyBlock);

                return;
            }

            // Set temporary render target.
            buffer.SetRenderTarget(passData.CommonTemporaryTextureHandle, passData.CameraDepthTextureHandle);
            // Clear the render target.
            buffer.ClearRenderTarget(false, true, Color.clear);

            // Draw the mesh with the material.
            buffer.DrawMesh(Mesh, Matrix4x4.identity, DrawMaterial ?? Material, 0, 0, PropertyBlock);

            // Blit to previous offscreen.
            buffer.Blit(passData.CommonTemporaryTextureHandle, previousOffscreen);
        }

        /// <summary>
        /// Applies properties for offscreen rendering.
        /// </summary>
        /// <param name="previousOffscreen">Previous offscreen render texture.</param>
        private void ApplyPropertyForOffscreen(RenderTexture previousOffscreen)
        {
            var property = PropertyBlock;
            MeshRenderer.GetPropertyBlock(property);

            // Write property.
            property.SetTexture(CubismShaderVariables.MainTexture, OffscreenFrameBuffer);
            property.SetTexture(CubismShaderVariables.RenderTexture, previousOffscreen);
            property.SetColor(CubismShaderVariables.MultiplyColor, MultiplyColor);
            property.SetColor(CubismShaderVariables.ScreenColor, ScreenColor);
            property.SetFloat(CubismShaderVariables.OffscreenOpacity, Offscreen.Opacity);

            var reversedZ = SystemInfo.usesReversedZBuffer ? CubismRenderPassFeature.GEqual : CubismRenderPassFeature.LEqual;
            property.SetInt(CubismShaderVariables.ReversedZ, reversedZ);

            MeshRenderer.SetPropertyBlock(property);
        }

        /// <summary>
        /// Applies mask texture for rendering.
        /// </summary>
        /// <param name="maskTextureHandle">Texture handle for the mask texture.</param>
        private void ApplyMask(TextureHandle maskTextureHandle)
        {
            MeshRenderer.GetPropertyBlock(PropertyBlock);

            // Write property.
            PropertyBlock.SetTexture(CubismShaderVariables.MaskTexture, maskTextureHandle);
            if (DrawObjectType == CubismModelTypes.DrawObjectType.Drawable)
            {
                PropertyBlock.SetVector(CubismShaderVariables.MaskTile, HighPrecisionMaskTile);
                PropertyBlock.SetVector(CubismShaderVariables.MaskTransform, _maskTransform);
            }

            MeshRenderer.SetPropertyBlock(PropertyBlock);
        }

        /// <summary>
        /// Initializes masks if possible.
        /// </summary>
        private void TryInitializeMasks()
        {
            if (!RenderController.HasMask)
            {
                return;
            }

            CubismDrawable[] maskDrawables = null;
            switch (DrawObjectType)
            {
                case CubismModelTypes.DrawObjectType.Drawable:
                    _masks = new CubismRenderer[Drawable.Masks.Length];
                    maskDrawables = Drawable.Masks;
                    break;
                case CubismModelTypes.DrawObjectType.Offscreen:
                    _masks = new CubismRenderer[Offscreen.Masks.Length];
                    maskDrawables = Offscreen.Masks;
                    break;
                default:
                    Debug.LogWarning($"{name} has unknown DrawObjectType.");
                    return;
            }

            for (var maskIndex = 0; maskIndex < maskDrawables.Length; maskIndex++)
            {
                for (var drawableRendererIndex = 0; drawableRendererIndex < RenderController.DrawableRenderers.Length; drawableRendererIndex++)
                {
                    if (RenderController.DrawableRenderers[drawableRendererIndex].Drawable.UnmanagedIndex != maskDrawables[maskIndex].UnmanagedIndex)
                    {
                        continue;
                    }

                    _masks[maskIndex] = RenderController.DrawableRenderers[drawableRendererIndex];
                    break;
                }
            }
        }

        /// <summary>
        /// Initializes the draw object based on its type.
        /// </summary>
        private void InitializeDrawObject()
        {
            switch (DrawObjectType)
            {
                case CubismModelTypes.DrawObjectType.Drawable:
                {
                    Drawable = GetComponent<CubismDrawable>();
                    DrawObjectUnmanagedIndex = Drawable.UnmanagedIndex;
                    break;
                }
                case CubismModelTypes.DrawObjectType.Offscreen:
                {
                    Offscreen = GetComponent<CubismOffscreen>();
                    DrawObjectUnmanagedIndex = Offscreen.UnmanagedIndex;
                    break;
                }
                default:
                    DrawObjectUnmanagedIndex = -1;
                    break;
            }
        }

        /// <summary>
        /// Called after all <see cref="CubismRenderer"/> are initialized.
        /// </summary>
        public void OnAfterAllRendererInitialize()
        {
            TryInitializeMasks();
        }
    }
}
