/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using Live2D.Cubism.Rendering.URP;
using System;
using System.Collections.Generic;
using Live2D.Cubism.Rendering.URP.RenderingInterceptor;
using UnityEngine;
using UnityEngine.Rendering;


namespace Live2D.Cubism.Rendering
{
    /// <summary>
    /// Partial class that describes processing related to the rendering method added in Cubism 5.3 and later.
    /// </summary>
    public partial class CubismRenderController
    {
        #region Values

        /// <summary>
        /// Material property block for the model.
        /// </summary>
        private MaterialPropertyBlock _properties;

        /// <summary>
        /// Whether the model has a root part offscreen.
        /// </summary>
        internal bool HasRootPartOffscreen = true;

        /// <summary>
        /// <see cref="HasMask"/>'s backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private bool _hasMask;

        /// <summary>
        /// Is the model using masks?
        /// </summary>
        public bool HasMask
        {
            get
            {
                return _hasMask;
            }
            set
            {
                _hasMask = value;
            }
        }

        /// <summary>
        /// Current frame buffer used for rendering.
        /// Offscreen rendering uses this to render the model.
        /// </summary>
        public RenderTexture CurrentFrameBuffer { get; set; }

        /// <summary>
        /// Index of the current offscreen owner in the unmanaged array.
        /// </summary>
        public int CurrentOffscreenUnmanagedIndex { get; set; }

        #region Sorting

        /// <summary>
        /// <see cref="GroupedSortingIndex"/>'s backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private int _groupedSortingIndex;

        /// <summary>
        /// Sorting index for grouped rendering.
        /// </summary>
        public int GroupedSortingIndex
        {
            get
            {
                return _groupedSortingIndex;
            }

            set
            {
                // Remove from the common rendering controller's groups.
                CubismRenderControllerGroup.GetInstance().RemoveRenderControllerFromGroups(this, true);

                _groupedSortingIndex = value;

                // Re-add to the common rendering controller's groups.
                CubismRenderControllerGroup.GetInstance().AddRenderControllerGroups(this);
            }
        }

        /// <summary>
        /// Whether the render order of the <see cref="CubismDrawable"/>s did change.
        /// </summary>
        internal bool DidChangeDrawableRenderOrder;

        /// <summary>
        /// Whether the sorting order of the <see cref="CubismRenderer"/>s did change.
        /// </summary>
        internal bool DidChangeSorting;

        [NonSerialized]
        private CubismRenderer[] _sortedRenderers;

        /// <summary>
        /// Sorted <see cref="Renderers"/>s for using <see cref="ICubismRenderingInterceptor"/>.
        /// </summary>
        public CubismRenderer[] SortedRenderers
        {
            get
            {
                if (_sortedRenderers == null)
                {
                    SortRenderers();
                }

                return _sortedRenderers;
            }
            private set { _sortedRenderers = value; }
        }

        /// <summary>
        /// Sorts the <see cref="Renderers"/> by their draw object render order.
        /// </summary>
        private void SortRenderers()
        {
            if (Renderers == null)
            {
                return;
            }

            for (var i = 0; i < Renderers.Length; i++)
            {
                if (Renderers[i].DrawObjectType != CubismModelTypes.DrawObjectType.Offscreen)
                {
                    continue;
                }

                Renderers[i].SetDrawObjectRenderOrder(
                    Model.AllDrawObjectsRenderOrder[
                        DrawableRenderers.Length + Renderers[i].Offscreen.UnmanagedIndex]);
            }

            var renderers = new List<CubismRenderer>(Renderers);

            SortBySortingOrder(renderers);

            _sortedRenderers = renderers.ToArray();
        }

        /// <summary>
        /// Sorts the given renderers by their sorting order.
        /// </summary>
        /// <param name="renderers"> Renderers to sort. </param>
        private void SortBySortingOrder(List<CubismRenderer> renderers)
        {
            renderers.Sort(CompareBySortingOrder);
        }

        /// <summary>
        /// Compares two <see cref="CubismRenderer"/>s by their sorting order.
        /// </summary>
        /// <param name="a"> First renderer. </param>
        /// <param name="b"> Second renderer. </param>
        /// <returns></returns>
        private int CompareBySortingOrder(CubismRenderer a, CubismRenderer b)
        {
            return a.MeshRenderer.sortingOrder - b.MeshRenderer.sortingOrder;
        }

        #endregion

        /// <summary>
        /// <see cref="DrawableRenderers"/>'s backing field.
        /// </summary>
        [NonSerialized]
        private CubismRenderer[] _drawableRenderers;

        /// <summary>
        /// Drawable <see cref="CubismRenderer"/>s.
        /// </summary>
        public CubismRenderer[] DrawableRenderers
        {
            get
            {
                if (_drawableRenderers == null)
                {
                    _drawableRenderers = Model.Drawables.GetComponentsMany<CubismRenderer>();
                }
                return _drawableRenderers;
            }
            private set { _drawableRenderers = value; }
        }

        /// <summary>
        /// <see cref="OffscreenRenderers"/>'s backing field.
        /// </summary>
        private CubismRenderer[] _offscreenRenderers;

        /// <summary>
        /// Offscreen <see cref="CubismRenderer"/>s.
        /// </summary>
        public CubismRenderer[] OffscreenRenderers
        {
            get
            {
                if (_offscreenRenderers == null && Model?.Offscreens != null)
                {
                    _offscreenRenderers = Model.Offscreens.GetComponentsMany<CubismRenderer>();
                }
                return _offscreenRenderers;
            }
            private set { _offscreenRenderers = value; }
        }

        #endregion

        /// <summary>
        /// Initializes the render controller.
        /// </summary>
        /// <param name="renderers">Array of renderers to initialize.</param>
        private void TryInitializeRenderers(CubismRenderer[] renderers)
        {
            // HACK: It doesn't work correctly when placed inside a function.
            if (renderers != null && renderers.Length != 0)
            {
                return;
            }

            // Initialize renderers.
            renderers = Array.Empty<CubismRenderer>();

            // Create renders and apply it to backing field...
            var drawables = Model.Drawables;

            var drawableRenderers = drawables.AddComponentEach<CubismRenderer>();
            Array.Resize(ref renderers, drawableRenderers.Length);
            Array.Copy(drawableRenderers, renderers, drawableRenderers.Length);

            for (var index = 0; index < renderers.Length; index++)
            {
                var targetRenderer = renderers[index];
                targetRenderer.DrawObjectType = CubismModelTypes.DrawObjectType.Drawable;
                targetRenderer.Drawable = drawables[index];

                if (!HasRootPartOffscreen)
                {
                    continue;
                }
                HasRootPartOffscreen = CheckHasRootPartOffscreen(targetRenderer);
            }

            // Store drawable renderers.
            DrawableRenderers = drawableRenderers;

            var offscreens = Model.Offscreens;

            if (offscreens != null)
            {
                var offscreenRenderers = offscreens.AddComponentEach<CubismRenderer>();
                Array.Resize(ref renderers, renderers.Length + offscreenRenderers.Length);
                Array.Copy(offscreenRenderers, 0, renderers, renderers.Length - offscreenRenderers.Length, offscreenRenderers.Length);

                for (var index = drawableRenderers.Length; index < renderers.Length; ++index)
                {
                    var targetRenderer = renderers[index];
                    targetRenderer.DrawObjectType = CubismModelTypes.DrawObjectType.Offscreen;
                    targetRenderer.Offscreen = offscreens[index - drawableRenderers.Length];

                    if (!HasRootPartOffscreen)
                    {
                        continue;
                    }
                    HasRootPartOffscreen = CheckHasRootPartOffscreen(targetRenderer);
                }

                // Store offscreen renderers.
                OffscreenRenderers = offscreenRenderers;
            }

            // Store renderers.
            Renderers = renderers;
        }

        /// <summary>
        /// Called after all <see cref="CubismRenderer"/>s are initialized.
        /// </summary>
        /// <param name="renderers"></param>
        private void OnAfterRenderersInitialize(CubismRenderer[] renderers)
        {
            // Set the render order and call `OnAfterAllRendererInitialize` method for each renderer.
            for (var i = 0; i < renderers.Length; i++)
            {
                var initRenderer = renderers[i];
                switch (initRenderer.DrawObjectType)
                {
                    case CubismModelTypes.DrawObjectType.Drawable:
                        initRenderer.SetDrawObjectRenderOrder(Model.AllDrawObjectsRenderOrder[initRenderer.Drawable.UnmanagedIndex]);
                        break;
                    case CubismModelTypes.DrawObjectType.Offscreen:
                        initRenderer.SetDrawObjectRenderOrder(Model.AllDrawObjectsRenderOrder[DrawableRenderers.Length + initRenderer.Offscreen.UnmanagedIndex]);
                        break;
                    default:
                        Debug.LogWarning($"Unknown draw object type: {initRenderer.DrawObjectType} for renderer: {initRenderer.name}");
                        break;
                }
                initRenderer.OnAfterAllRendererInitialize();
            }
        }

        /// <summary>
        /// Does the model have an offscreen for the root part?
        /// </summary>
        /// <param name="targetRenderer">The <see cref="CubismRenderer"/> to check.</param>
        /// <returns>True if it has one, false if it does not.</returns>
        private bool CheckHasRootPartOffscreen(CubismRenderer targetRenderer)
        {
            var parentPartIndex = -1;
            switch (targetRenderer.DrawObjectType)
            {
                case CubismModelTypes.DrawObjectType.Drawable:
                    parentPartIndex = targetRenderer.Drawable.ParentPartIndex;
                    break;
                case CubismModelTypes.DrawObjectType.Offscreen:
                    parentPartIndex = Model.Parts[targetRenderer.Offscreen.OwnerIndex].UnmanagedParentIndex;
                    break;
                default:
                    Debug.LogWarning($"Unknown draw object type: {targetRenderer.DrawObjectType} for renderer: {targetRenderer.name}");
                    break;
            }

            CubismPart part = null;
            while (parentPartIndex > 0)
            {
                for (var partIndex = 0; partIndex < Model.Parts.Length; partIndex++)
                {
                    if (Model.Parts[partIndex].UnmanagedIndex != parentPartIndex)
                    {
                        continue;
                    }

                    // Found the part.
                    part = Model.Parts[partIndex];
                    break;
                }

                parentPartIndex = part?.UnmanagedParentIndex ?? -1;
            }

            return parentPartIndex == 0 && Model.Parts[parentPartIndex].OffscreenIndex > -1;
        }

        /// <summary>
        /// Submits drawing offscreen for all offscreen renderers.
        /// </summary>
        /// <param name="commandBuffer">Command buffer to record draw commands.</param>
        /// <param name="passData">Pass data containing render controllers and camera data.</param>
        internal void SubmitDrawOffscreen(CommandBuffer commandBuffer, CubismRenderPassFeature.CubismRenderPass.PassData passData)
        {
            if (!IsInitialized
                || !Model
                || OffscreenRenderers == null)
            {
                return;
            }

            while (CurrentFrameBuffer != (RenderTexture)passData.CommonRenderingTextureHandle)
            {
                CubismRenderer offscreenRenderer = null;
                if (CurrentOffscreenUnmanagedIndex == -1 && HasRootPartOffscreen)
                {
                    for (var offscreenIndex = 0; offscreenIndex < OffscreenRenderers.Length; offscreenIndex++)
                    {
                        if (OffscreenRenderers[offscreenIndex].Offscreen.UnmanagedIndex != 0)
                        {
                            continue;
                        }

                        offscreenRenderer = OffscreenRenderers[offscreenIndex];

                        break;
                    }

                    if (!offscreenRenderer)
                    {
                        break;
                    }

                    offscreenRenderer.DrawOffscreen(commandBuffer, passData.CommonRenderingTextureHandle, offscreenRenderer, passData);
                    offscreenRenderer.OffscreenFrameBuffer = null;
                    CurrentFrameBuffer = passData.CommonRenderingTextureHandle;
                    CurrentOffscreenUnmanagedIndex = -1;
                    break;
                }

                for (var offscreenRendererIndex = 0; offscreenRendererIndex < OffscreenRenderers.Length; offscreenRendererIndex++)
                {
                    if (OffscreenRenderers[offscreenRendererIndex].Offscreen.UnmanagedIndex != CurrentOffscreenUnmanagedIndex)
                    {
                        continue;
                    }

                    offscreenRenderer = OffscreenRenderers[offscreenRendererIndex];
                    break;
                }

                RenderTexture previousOffscreen = null;
                var currentOwnerIndex = offscreenRenderer?.Offscreen?.OwnerIndex ?? -1;

                var parentIndex = -1;
                if (currentOwnerIndex != -1)
                {
                    parentIndex = Model.Parts[currentOwnerIndex]?.UnmanagedParentIndex ?? -1;
                }

                var previousIndex = -1;
                // Find the offscreen renderer with the previous offscreen unmanaged index.
                while (parentIndex != -1)
                {
                    var part = Model.Parts[parentIndex];
                    if (part && part.OffscreenIndex != -1)
                    {
                        for (var offscreenRendererIndex = 0; offscreenRendererIndex < OffscreenRenderers.Length; offscreenRendererIndex++)
                        {
                            var element = OffscreenRenderers[offscreenRendererIndex];

                            if (!element.isActiveAndEnabled
                                || !element.MeshRenderer.enabled)
                            {
                                continue;
                            }

                            if (element.Offscreen.UnmanagedIndex != part?.OffscreenIndex)
                            {
                                continue;
                            }

                            previousOffscreen = OffscreenRenderers[offscreenRendererIndex].OffscreenFrameBuffer;
                            previousIndex = part.OffscreenIndex;
                            break;
                        }
                    }
                    parentIndex = Model.Parts[parentIndex]?.UnmanagedParentIndex ?? -1;

                    if (!previousOffscreen)
                    {
                        continue;
                    }

                    break;
                }

                // Try to get the root part offscreen if there is no parent offscreen.
                if (!previousOffscreen && HasRootPartOffscreen)
                {
                    for (var offscreenIndex = 0; offscreenIndex < OffscreenRenderers.Length; offscreenIndex++)
                    {
                        if (OffscreenRenderers[offscreenIndex].Offscreen.UnmanagedIndex != 0)
                        {
                            continue;
                        }

                        previousOffscreen = OffscreenRenderers[offscreenIndex].OffscreenFrameBuffer;

                        break;
                    }
                }

                // If there is no previous offscreen, or it is the same as the current offscreen, use the common rendering texture.
                if (!previousOffscreen
                    || previousOffscreen == offscreenRenderer?.OffscreenFrameBuffer)
                {
                    previousOffscreen = passData.CommonRenderingTextureHandle;
                }

                // If It can copy the parent offscreen, copy it to the current offscreen renderer.
                offscreenRenderer?.DrawOffscreen(commandBuffer, previousOffscreen, offscreenRenderer, passData);

                if (offscreenRenderer)
                {
                    offscreenRenderer.OffscreenFrameBuffer = null;
                }

                CurrentFrameBuffer = previousOffscreen;
                CurrentOffscreenUnmanagedIndex = previousIndex;
            }
        }
    }
}
