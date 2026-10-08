/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using System;
using System.Collections.Generic;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Live2D.Cubism.Rendering.URP.RenderingInterceptor
{
    /// <summary>
    /// Base class for rendering interceptors.
    /// Place custom rendering logic by inheriting from this class.
    /// </summary>
    public class CubismRenderingInterceptController : MonoBehaviour, ICubismUpdatable, ICubismRenderingInterceptor
    {
        /// <summary>
        /// Timing for invoking.
        /// </summary>
        public enum InvokeTiming
        {
            PreRendering,
            PostRendering,
        }

        /// <summary>
        /// Timing for invoking setting.
        /// </summary>
        [SerializeField]
        public InvokeTiming InvokeTimingSetting;

        /// <summary>
        /// Modes for invoking.
        /// </summary>
        public enum InterceptingMode
        {
            SortingOrder,
            ZDepth,
            Drawable,
        }

        /// <summary>
        /// Mode for invoking setting.
        /// </summary>
        [SerializeField]
        public InterceptingMode Mode;

        /// <summary>
        /// Group sorting order for the interceptor.
        /// </summary>
        [SerializeField]
        public int GroupSortingOrder = 0;

        /// <summary>
        /// Sorting order for the interceptor.
        /// </summary>
        [SerializeField]
        public int SortingOrder = 0;

        /// <summary>
        /// Target art mesh for the interceptor.
        /// </summary>
        [SerializeField]
        protected CubismDrawable _targetArtMesh;

        /// <summary>
        /// If attached <see cref="CubismRenderController"/>, reference.
        /// </summary>
        protected CubismRenderController _renderController;

        /// <summary>
        /// Renderer reference.
        /// </summary>
        protected Renderer _renderer;

        /// <summary>
        /// <see cref="_rederer"/>'s material reference.
        /// </summary>
        protected Material _material;

        /// <summary>
        /// Camera draw status.
        /// </summary>
        private struct CameraDrawStatus
        {
            public Camera Camera;
            public int LastRenderedPassCount;
        }

        /// <summary>
        /// Camera draw status array.
        /// </summary>
        private CameraDrawStatus[] _cameraDrawStatus;

        /// <summary>
        /// Render pass counter.
        /// </summary>
        private int _renderPassCounter = 0;

        /// <summary>
        /// Execution order of the interceptor.
        /// </summary>
        public virtual int ExecutionOrder
        {
            get
            {
                return CubismUpdateExecutionOrder.CubismPhysicsController + 10;
            }
        }

        /// <summary>
        /// Whether this interceptor needs to be updated during editing.
        /// </summary>
        public virtual bool NeedsUpdateOnEditing
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Whether a <see cref="CubismUpdateController"/> is attached to the same GameObject.
        /// </summary>
        public bool HasUpdateController
        {
            get;
            set;
        }

        /// <summary>
        /// Called by Unity when the component is enabled.
        /// </summary>
        public void OnEnable()
        {
            _renderController = GetComponent<CubismRenderController>();

            if (!_renderController)
            {
                _renderer = GetComponent<Renderer>();

                _renderer.enabled = false;
                _material = _renderer.material;

#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    _material = _renderer.sharedMaterial;
                }
#endif
            }

            _cameraDrawStatus = Array.Empty<CameraDrawStatus>();

            CubismRenderingInterceptorsManager.GetInstance().AddInterceptors(this);

            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
        }

        /// <summary>
        /// Called by Unity when the component starts.
        /// </summary>
        public void Start()
        {
            HasUpdateController = TryGetComponent<CubismUpdateController>(out _);
        }

        /// <summary>
        /// Called by Unity when the component is disabled.
        /// </summary>
        public void OnDisable()
        {
            CubismRenderingInterceptorsManager.GetInstance().RemoveInterceptors(this);

            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
        }

        /// <summary>
        /// Called by Unity at the beginning of context rendering.
        /// </summary>
        private void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            _renderPassCounter++;
        }

        /// <summary>
        /// Called by Unity during LateUpdate.
        /// </summary>
        public void LateUpdate()
        {
            // Skip if an update controller is present.
            if (HasUpdateController)
            {
                return;
            }

            OnLateUpdate();
        }

        /// <summary>
        /// Called during LateUpdate if no <see cref="CubismUpdateController"/> is present.
        /// </summary>
        public virtual void OnLateUpdate()
        {

        }

        /// <summary>
        /// Called before rendering for each pass.
        /// </summary>
        /// <param name="args"> Event arguments. </param>
        void ICubismRenderingInterceptor.OnPreRenderingForPass(CubismRenderedEventArgs args)
        {
            OnPreRendering(args);
        }

        /// <summary>
        /// Called before rendering for each pass.
        /// </summary>
        /// <param name="args"> Event arguments. </param>
        protected virtual void OnPreRendering(CubismRenderedEventArgs args)
        {
            // Skip if not invoking pre-rendering.
            if (InvokeTimingSetting != InvokeTiming.PreRendering)
            {
                return;
            }

            TryDraw(args);
        }

        /// <summary>
        /// Called after rendering for each pass.
        /// </summary>
        /// <param name="args"> Event arguments. </param>
        void ICubismRenderingInterceptor.OnPostRenderingForPass(CubismRenderedEventArgs args)
        {
            OnPostRendering(args);
        }

        /// <summary>
        /// Called after rendering for each pass.
        /// </summary>
        /// <param name="args"> Event arguments. </param>
        protected virtual void OnPostRendering(CubismRenderedEventArgs args)
        {
            // Skip if not invoking post-rendering.
            if (InvokeTimingSetting != InvokeTiming.PostRendering)
            {
                return;
            }

            TryDraw(args);
        }

        /// <summary>
        /// Attempts to draw the object based on the interceptor's settings.
        /// </summary>
        /// <param name="args"> Event arguments. </param>
        private void TryDraw(CubismRenderedEventArgs args)
        {
            var currentCamera = args.PassData.CameraData.camera;

            // Find or create camera draw status.
            var statusIndex = -1;
            for (var i = 0; i < _cameraDrawStatus.Length; i++)
            {
                if (_cameraDrawStatus[i].Camera == currentCamera)
                {
                    statusIndex = i;
                    break;
                }
            }

            // Create a new entry if not found.
            if (statusIndex < 0)
            {
                // Create a new camera draw status entry.
                Array.Resize(ref _cameraDrawStatus, _cameraDrawStatus.Length + 1);
                statusIndex = _cameraDrawStatus.Length - 1;

                // Initialize the new entry.
                _cameraDrawStatus[statusIndex] = new CameraDrawStatus
                {
                    Camera = currentCamera,
                    LastRenderedPassCount = -1,
                };
            }

            // Get the current rendered frame count.
            if (_cameraDrawStatus[statusIndex].LastRenderedPassCount == _renderPassCounter)
            {
                return;
            }

            var doDraw = true;
            switch (Mode)
            {
                case InterceptingMode.SortingOrder:
                    // Decide whether to draw based on sorting order comparisons.
                    if (args.GroupSortingOrder != GroupSortingOrder || args.SortingOrder < SortingOrder)
                    {
                        doDraw = false;
                    }
                    break;
                case InterceptingMode.ZDepth:
                    // Calculate the distance from the camera to the renderer.
                    var directionToRenderer = transform.position - args.CameraPos;
                    var projection = Vector3.Project(directionToRenderer, args.CameraForward);
                    var distance = projection.magnitude;

                    // Determine whether to draw based on distance comparisons.
                    var canDraw = (args.Distance < distance || args.NextDistance > distance) &&
                                !(distance > args.Distance && distance > args.NextDistance &&
                                  distance > args.PreviousDistance);

                    // Decide whether to draw.
                    if (args.PreviousDistance != null
                        && args.NextDistance != null
                        && canDraw)
                    {
                        doDraw = false;
                        break;
                    }

                    if (distance < args.Distance)
                    {
                        doDraw = false;
                    }
                    break;
                case InterceptingMode.Drawable:
                    if (!_targetArtMesh || args.Drawable != _targetArtMesh)
                    {
                        doDraw = false;
                    }

                    break;
                default:
                    // Skip drawing for unknown modes.
                    break;
            }

            if (!doDraw)
            {
                return;
            }

            if (!_renderController)
            {
                // Draw the object.
                args.CommandBuffer.DrawRenderer(_renderer, _material);
            }
            else
            {
                for (var index = 0; index < _renderController.SortedRenderers.Length; index++)
                {
                    var targetRenderer = _renderController.SortedRenderers[index];

                    // Check whether to skip rendering.
                    CheckSkipRendering(targetRenderer);

                    if (targetRenderer.SkipRendering)
                    {
                        continue;
                    }

                    // Draw the object.
                    targetRenderer.DrawObject(args.CommandBuffer, args.PassData);
                }

                // Submit offscreen draws.
                _renderController.SubmitDrawOffscreen(args.CommandBuffer, args.PassData);
            }

            // Mark as drawn.
            _cameraDrawStatus[statusIndex].LastRenderedPassCount = _renderPassCounter;
        }

        /// <summary>
        /// Checks whether to skip rendering for the given renderer.
        /// </summary>
        /// <param name="targetRenderer"> The target renderer. </param>
        private void CheckSkipRendering(CubismRenderer targetRenderer)
        {
            targetRenderer.SkipRendering |= !targetRenderer
                                                    || !targetRenderer.gameObject.activeSelf
                                                    || !targetRenderer.MeshRenderer
                                                    || !targetRenderer.MeshRenderer.enabled;

            switch (targetRenderer.DrawObjectType)
            {
                case CubismModelTypes.DrawObjectType.Drawable:
                    targetRenderer.SkipRendering |= targetRenderer.Opacity <= 0.0f;
                    break;
                case CubismModelTypes.DrawObjectType.Offscreen:
                    targetRenderer.SkipRendering |= targetRenderer.Offscreen.Opacity <= 0.0f;

                    if (!targetRenderer.SkipRendering)
                    {
                        break;
                    }

                    // Skip rendering for all child drawables and offscreens of the offscreen part.
                    var parts = targetRenderer.RenderController.Model.Parts;
                    CubismPart part = null;
                    for (var partIndex = 0; partIndex < parts.Length; partIndex++)
                    {
                        if (parts[partIndex].UnmanagedIndex != targetRenderer.Offscreen.OwnerIndex)
                        {
                            continue;
                        }

                        part = parts[partIndex];
                        break;
                    }

                    if (!part)
                    {
                        break;
                    }

                    // Skip rendering for all child drawables and offscreens of the offscreen part.
                    for (var drawablesIndex = 0; drawablesIndex < part.AllChildDrawables?.Length; drawablesIndex++)
                    {
                        var childDrawable = part.AllChildDrawables[drawablesIndex];

                        for (var rendererIndex = 0; rendererIndex < targetRenderer.RenderController.Renderers.Length; rendererIndex++)
                        {
                            var checkedRenderer = targetRenderer.RenderController.Renderers[rendererIndex];

                            if (checkedRenderer.DrawObjectType != CubismModelTypes.DrawObjectType.Drawable
                                || checkedRenderer.Drawable.UnmanagedIndex != childDrawable.UnmanagedIndex)
                            {
                                continue;
                            }

                            checkedRenderer.SkipRendering = true;
                            break;
                        }
                    }

                    for (var offscreensIndex = 0; offscreensIndex < part.AllChildOffscreens?.Length; offscreensIndex++)
                    {
                        var childOffscreen = part.AllChildOffscreens[offscreensIndex];

                        for (var j = 0; j < targetRenderer.RenderController.Renderers.Length; j++)
                        {
                            var checkedRenderer = targetRenderer.RenderController.Renderers[j];

                            if (checkedRenderer.DrawObjectType != CubismModelTypes.DrawObjectType.Offscreen
                                || checkedRenderer.Offscreen.UnmanagedIndex != childOffscreen.UnmanagedIndex)
                            {
                                continue;
                            }

                            checkedRenderer.SkipRendering = true;
                            break;
                        }
                    }
                    break;
                default:
                    targetRenderer.SkipRendering = true;
                    break;
            }
        }
    }
}
