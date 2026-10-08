/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using Live2D.Cubism.Rendering.URP.RenderingInterceptor;
using System;
using UnityEngine;
using Object = UnityEngine.Object;


namespace Live2D.Cubism.Rendering
{
    /// <summary>
    /// Controls rendering of a <see cref="CubismModel"/>.
    /// </summary>
    [ExecuteInEditMode, CubismDontMoveOnReimport]
    public sealed partial class CubismRenderController : MonoBehaviour, ICubismUpdatable
    {
        /// <summary>
        /// Model opacity.
        /// </summary>
        /// <remarks>
        /// This is turned into a field to be available to <see cref="AnimationClip"/>s...
        /// </remarks>
        [SerializeField, HideInInspector]
        public float Opacity = 1f;

        /// <summary>
        /// <see cref="LastOpacity"/> backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private float _lastOpacity;

        /// <summary>
        /// Last model opacity.
        /// </summary>
        private float LastOpacity
        {
            get { return _lastOpacity; }
            set { _lastOpacity = value; }
        }

        /// <summary>
        /// <see cref="MultiplyColorEnabled"/> backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private bool _isOverriddenModelMultiplyColors;

        /// <summary>
        /// Whether to override with multiply color from the model.
        /// </summary>
        public bool MultiplyColorEnabled
        {
            get { return _isOverriddenModelMultiplyColors; }
            set { _isOverriddenModelMultiplyColors = value; }
        }

        /// <summary>
        /// <see cref="ScreenColorEnabled"/> backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private bool _isOverriddenModelScreenColors;

        /// <summary>
        /// Whether to override with screen color from the model.
        /// </summary>
        public bool ScreenColorEnabled
        {
            get { return _isOverriddenModelScreenColors; }
            set { _isOverriddenModelScreenColors = value; }
        }

        /// <summary>
        /// <see cref="ModelMultiplyColor"/> backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private Color _modelMultiplyColor;

        /// <summary>
        /// Multiply colors used throughout the model.
        /// </summary>
        public Color ModelMultiplyColor
        {
            get { return _modelMultiplyColor; }
            set { _modelMultiplyColor = value; }
        }

        /// <summary>
        /// <see cref="ModelScreenColor"/> backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private Color _modelScreenColor;

        /// <summary>
        /// Screen colors used throughout the model.
        /// </summary>
        public Color ModelScreenColor
        {
            get { return _modelScreenColor; }
            set { _modelScreenColor = value; }
        }

        /// <summary>
        /// Sorting layer name.
        /// </summary>
        public string SortingLayer
        {
            get
            {
                return UnityEngine.SortingLayer.IDToName(SortingLayerId);
            }
            set
            {
                SortingLayerId = UnityEngine.SortingLayer.NameToID(value);
            }
        }

        /// <summary>
        /// <see cref="SortingLayerId"/> backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private int _sortingLayerId;

        /// <summary>
        /// Sorting layer Id.
        /// </summary>
        public int SortingLayerId
        {
            get
            {
                return _sortingLayerId;
            }
            set
            {
                if (value == _sortingLayerId)
                {
                    return;
                }


                _sortingLayerId = value;


                // Apply sorting layer.
                var renderers = Renderers;


                for (var i = 0; i < renderers.Length; ++i)
                {
                    renderers[i].OnControllerSortingLayerDidChange(_sortingLayerId);
                }

                SortRenderers();
            }
        }


        /// <summary>
        /// <see cref="SortingMode"/> backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private CubismSortingMode _sortingMode;

        /// <summary>
        /// <see cref="CubismDrawable"/> sorting.
        /// </summary>
        public CubismSortingMode SortingMode
        {
            get
            {
                return _sortingMode;
            }
            set
            {
                // Return early if same value given.
                if (value == _sortingMode)
                {
                    return;
                }


                _sortingMode = value;


                // Flip sorting.
                var renderers = Renderers;


                for (var i = 0; i < renderers.Length; ++i)
                {
                    renderers[i].OnControllerSortingModeDidChange(_sortingMode);
                }

                SortRenderers();
            }
        }


        /// <summary>
        /// Order in sorting layer.
        /// </summary>
        [SerializeField, HideInInspector]
        private int _sortingOrder;

        /// <summary>
        /// Order in sorting layer.
        /// </summary>
        public int SortingOrder
        {
            get
            {
                return _sortingOrder;
            }
            set
            {
                // Return early in case same value given.
                if (value == _sortingOrder)
                {
                    return;
                }


                _sortingOrder = value;


                // Apply new sorting order.
                var renderers = Renderers;


                for (var i = 0; i < renderers.Length; ++i)
                {
                    renderers[i].OnControllerSortingOrderDidChange(SortingOrder);
                }

                SortRenderers();
            }
        }


        /// <summary>
        /// [Optional] Camera to face.
        /// </summary>
        [SerializeField]
        public Camera CameraToFace;



        /// <summary>
        /// <see cref="DrawOrderHandler"/> backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private Object _drawOrderHandler;

        /// <summary>
        /// Draw order handler proxy object.
        /// </summary>
        public Object DrawOrderHandler
        {
            get { return _drawOrderHandler; }
            set { _drawOrderHandler = value.ToNullUnlessImplementsInterface<ICubismDrawOrderHandler>(); }
        }


        /// <summary>
        /// <see cref="DrawOrderHandlerInterface"/> backing field.
        /// </summary>
        [NonSerialized]
        private ICubismDrawOrderHandler _drawOrderHandlerInterface;

        /// <summary>
        /// Listener for draw order changes.
        /// </summary>
        private ICubismDrawOrderHandler DrawOrderHandlerInterface
        {
            get
            {
                if (_drawOrderHandlerInterface == null)
                {
                    _drawOrderHandlerInterface = DrawOrderHandler.GetInterface<ICubismDrawOrderHandler>();
                }


                return _drawOrderHandlerInterface;
            }
        }


        /// <summary>
        /// <see cref="OpacityHandler"/> backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private Object _opacityHandler;

        /// <summary>
        /// Opacity handler proxy object.
        /// </summary>
        public Object OpacityHandler
        {
            get { return _opacityHandler; }
            set { _opacityHandler = value.ToNullUnlessImplementsInterface<ICubismOpacityHandler>(); }
        }


        /// <summary>
        /// <see cref="OpacityHandler"/> backing field.
        /// </summary>
        private ICubismOpacityHandler _opacityHandlerInterface;

        /// <summary>
        /// Listener for opacity changes.
        /// </summary>
        private ICubismOpacityHandler OpacityHandlerInterface
        {
            get
            {
                if (_opacityHandlerInterface == null)
                {
                    _opacityHandlerInterface = OpacityHandler.GetInterface<ICubismOpacityHandler>();
                }


                return _opacityHandlerInterface;
            }
        }


        /// <summary>
        /// <see cref="MultiplyColorHandler"/> backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private Object _multiplyColorHandler;

        /// <summary>
        /// Opacity handler proxy object.
        /// </summary>
        public Object MultiplyColorHandler
        {
            get { return _multiplyColorHandler; }
            set { _multiplyColorHandler = value.ToNullUnlessImplementsInterface<ICubismBlendColorHandler>(); }
        }


        /// <summary>
        /// <see cref="MultiplyColorHandler"/> backing field.
        /// </summary>
        private ICubismBlendColorHandler _multiplyColorHandlerInterface;

        /// <summary>
        /// Listener for blend color changes.
        /// </summary>
        private ICubismBlendColorHandler MultiplyColorHandlerInterface
        {
            get
            {
                if (_multiplyColorHandlerInterface == null)
                {
                    _multiplyColorHandlerInterface = MultiplyColorHandler?.GetInterface<ICubismBlendColorHandler>();
                }


                return _multiplyColorHandlerInterface;
            }
        }

        /// <summary>
        /// <see cref="ScreenColorHandler"/> backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private Object _screenColorHandler;

        /// <summary>
        /// Screen color handler proxy object.
        /// </summary>
        public Object ScreenColorHandler
        {
            get { return _screenColorHandler; }
            set { _screenColorHandler = value.ToNullUnlessImplementsInterface<ICubismBlendColorHandler>(); }
        }


        /// <summary>
        /// <see cref="MultiplyColorHandler"/> backing field.
        /// </summary>
        private ICubismBlendColorHandler _screenColorHandlerInterface;

        /// <summary>
        /// Listener for blend color changes.
        /// </summary>
        private ICubismBlendColorHandler ScreenColorHandlerInterface
        {
            get
            {
                if (_screenColorHandlerInterface == null)
                {
                    _screenColorHandlerInterface = ScreenColorHandler?.GetInterface<ICubismBlendColorHandler>();
                }


                return _screenColorHandlerInterface;
            }
        }

        /// <summary>
        /// The value to offset the <see cref="CubismDrawable"/>s by.
        /// </summary>
        /// <remarks>
        /// You only need to adjust this value when using perspective cameras.
        /// </remarks>
        [SerializeField, HideInInspector]
        private float _depthOffset = 0.00001f;

        /// <summary>
        /// Depth offset used when sorting by depth.
        /// </summary>
        public float DepthOffset
        {
            get { return _depthOffset; }
            set
            {
                // Return if same value given.
                if (Mathf.Abs(value - _depthOffset) < Mathf.Epsilon)
                {
                    return;
                }


                // Store value.
                _depthOffset = value;


                // Apply it.
                var renderers = Renderers;


                for (var i = 0; i < renderers.Length; ++i)
                {
                    renderers[i].OnControllerDepthOffsetDidChange(_depthOffset);
                }
            }
        }

        /// <summary>
        /// <see cref="Model"/>'s backing field.
        /// </summary>
        [NonSerialized]
        private CubismModel _cubismModel;

        /// <summary>
        /// Model the controller belongs to.
        /// </summary>
        public CubismModel Model
        {
            get
            {
                if (_cubismModel == null)
                {
                    _cubismModel = this.FindCubismModel();
                }

                return _cubismModel;
            }
        }


        /// <summary>
        /// <see cref="DrawablesRootTransform"/> backing field.
        /// </summary>
        private Transform _drawablesRootTransform;

        /// <summary>
        /// Root transform of all <see cref="CubismDrawable"/>s of the model.
        /// </summary>
        private Transform DrawablesRootTransform
        {
            get
            {
                if (_drawablesRootTransform == null)
                {
                    _drawablesRootTransform = Model.Drawables[0].transform.parent;
                }


                return _drawablesRootTransform;
            }
        }

        /// <summary>
        /// <see cref="Renderers"/>s backing field.
        /// </summary>
        [SerializeField]
        private CubismRenderer[] _renderers;

        /// <summary>
        /// <see cref="CubismRenderer"/>s.
        /// </summary>
        public CubismRenderer[] Renderers
        {
            get
            {
                if (_renderers == null)
                {
                    TryInitialize();
                }

                return _renderers;
            }
            private set { _renderers = value; }
        }


        /// <summary>
        /// multiply color buffer.
        /// </summary>
        private Color[] _newMultiplyColors;

        /// <summary>
        /// screen color buffer.
        /// </summary>
        private Color[] _newScreenColors;


        /// <summary>
        /// Model has update controller component.
        /// </summary>
        [HideInInspector]
        public bool HasUpdateController { get; set; }

        /// <summary>
        /// <see cref="IsInitialized"/>s backing field.
        /// </summary>
        private bool _isInitialized = false;

        /// <summary>
        /// Is renderers initialized.
        /// </summary>
        [HideInInspector]
        public bool IsInitialized
        {
            get
            {
                return _isInitialized;
            }
            private set
            {
                _isInitialized = value;
            }
        }

        /// <summary>
        /// Makes sure all <see cref="CubismDrawable"/>s have <see cref="CubismRenderer"/>s attached to them.
        /// </summary>
        public void TryInitialize()
        {
            // If the number of drawables has changed,
            // discard the cached renderers so they are rebuilt to match the new drawable count.
            if (_renderers != null && Model && Model.Drawables != null)
            {
                var newCount = Model.Drawables.Length;

                if (Model.Offscreens != null)
                {
                    newCount += Model.Offscreens.Length;
                }

                if (_renderers.Length != newCount)
                {
                    _renderers = null;
                }
            }

            // Try to get renderers.
            var renderers = _renderers;
            TryInitializeRenderers(renderers);

            if (_renderers == null
                || _renderers.Length < 1)
            {
                return;
            }

            // Make sure renderers are initialized.
            for (var i = 0; i < Renderers.Length; ++i)
            {
                var targetRenderer = Renderers[i];
                targetRenderer.TryInitialize(this);
                if (!HasRootPartOffscreen)
                {
                    continue;
                }

                HasRootPartOffscreen = CheckHasRootPartOffscreen(targetRenderer);
            }

            // Initialize sorting layer.
            // We set the backing field here directly because we pull the sorting layer directly from the renderer.
            _sortingLayerId = Renderers[0]
                .MeshRenderer
                .sortingLayerID;

            OnAfterRenderersInitialize(Renderers);

            IsInitialized = true;
        }

        /// <summary>
        /// Updates opacity if necessary.
        /// </summary>
        private void UpdateOpacity()
        {
            // Return if same value given.
            if (Mathf.Abs(Opacity - LastOpacity) < Mathf.Epsilon)
            {
                return;
            }


            // Store value.
            Opacity = Mathf.Clamp(Opacity, 0f, 1f);
            LastOpacity = Opacity;


            // Apply opacity.
            var applyOpacityToRenderers = (OpacityHandlerInterface == null || Opacity > (1f - Mathf.Epsilon));


            if (applyOpacityToRenderers && Renderers != null)
            {
                var renderers = Renderers;


                for (var i = 0; i < renderers.Length; ++i)
                {
                    renderers[i].OnModelOpacityDidChange(Opacity);
                }
            }


            // Call handler.
            if (OpacityHandlerInterface != null)
            {
                OpacityHandlerInterface.OnOpacityDidChange(this, Opacity);
            }
        }

        /// <summary>
        /// Updates Blend Colors if necessary.
        /// </summary>
        private void UpdateDrawableBlendColors()
        {
            if (Renderers == null
                || !IsInitialized)
            {
                return;
            }

            var isMultiplyColorUpdated = false;
            var isScreenColorUpdated = false;

            if ((_newMultiplyColors?.Length ?? 0) != Renderers.Length)
            {
                _newMultiplyColors = new Color[Renderers.Length];
            }

            if ((_newScreenColors?.Length ?? 0) != Renderers.Length)
            {
                _newScreenColors = new Color[Renderers.Length];
            }

            for (var i = 0; i < Renderers.Length; i++)
            {
                if (!Renderers[i])
                {
                    continue;
                }

                var isUseUserMultiplyColor = (Renderers[i].DrawObjectMultiplyColorEnabled ||
                                              MultiplyColorEnabled);

                if (isUseUserMultiplyColor)
                {
                    // If you switch from a setting that uses the color of the model, revert to the color that was retained.
                    if (!Renderers[i].LastIsUseUserMultiplyColor)
                    {
                        Renderers[i].MultiplyColor = Renderers[i].LastMultiplyColor;
                        Renderers[i].ApplyMultiplyColor();
                        isMultiplyColorUpdated = true;
                    }
                    else if (Renderers[i].LastMultiplyColor != Renderers[i].MultiplyColor)
                    {
                        Renderers[i].ApplyMultiplyColor();
                        isMultiplyColorUpdated = true;
                    }

                    Renderers[i].LastMultiplyColor = Renderers[i].MultiplyColor;
                }
                else if (Renderers[i].LastIsUseUserMultiplyColor)
                {
                    Renderers[i].MultiplyColor = Renderers[i].LastMultiplyColor;
                    Renderers[i].ApplyMultiplyColor();
                    isMultiplyColorUpdated = true;
                }

                _newMultiplyColors[i] = Renderers[i].MultiplyColor;
                Renderers[i].LastIsUseUserMultiplyColor = isUseUserMultiplyColor;

                var isUseUserScreenColor = (Renderers[i].DrawObjectScreenColorEnabled ||
                                            ScreenColorEnabled);

                if (isUseUserScreenColor)
                {
                    // If you switch from a setting that uses the color of the model, revert to the color that was retained.
                    if (!Renderers[i].LastIsUseUserScreenColors)
                    {
                        Renderers[i].ScreenColor = Renderers[i].LastScreenColor;
                        Renderers[i].ApplyScreenColor();
                        isScreenColorUpdated = true;
                    }
                    else if (Renderers[i].LastScreenColor != Renderers[i].ScreenColor)
                    {
                        Renderers[i].ApplyScreenColor();
                        isScreenColorUpdated = true;
                    }

                    Renderers[i].LastScreenColor = Renderers[i].ScreenColor;
                }
                else if (Renderers[i].LastIsUseUserScreenColors)
                {
                    Renderers[i].ScreenColor = Renderers[i].LastScreenColor;
                    Renderers[i].ApplyScreenColor();
                    isScreenColorUpdated = true;
                }

                _newScreenColors[i] = Renderers[i].ScreenColor;
                Renderers[i].LastIsUseUserScreenColors = isUseUserScreenColor;
            }

            if (MultiplyColorHandler != null && isMultiplyColorUpdated)
            {
                MultiplyColorHandlerInterface.OnBlendColorDidChange(this, _newMultiplyColors);
            }

            if (ScreenColorHandler != null && isScreenColorUpdated)
            {
                ScreenColorHandlerInterface.OnBlendColorDidChange(this, _newScreenColors);
            }
        }

        /// <summary>
        /// Updates <see cref="DidChangeSorting"/> from direction changes.
        /// </summary>
        internal void UpdateDidChangeSortingFromZ(Vector3 cameraPosition)
        {
            // Return early if not sorting by depth.
            if (!SortingMode.SortByDepth())
            {
                return;
            }

            for (var i = 0; i < Renderers?.Length; i++)
            {
                var cubismRenderer = Renderers[i];

                if (!cubismRenderer)
                {
                    continue;
                }

                // Check if direction updated from last sorted.
                DidChangeSorting |= cubismRenderer.DidUpdateDirectionFromLastSorted(cameraPosition);
            }
        }

        /// <summary>
        /// Called by cubism update controller. Order to invoke OnLateUpdate.
        /// </summary>
        public int ExecutionOrder
        {
            get { return CubismUpdateExecutionOrder.CubismRenderController; }
        }

        /// <summary>
        /// Called by cubism update controller. Needs to invoke OnLateUpdate on Editing.
        /// </summary>
        public bool NeedsUpdateOnEditing
        {
            get { return true; }
        }

        /// <summary>
        /// Called by cubism update controller. Applies billboarding.
        /// </summary>
        public void OnLateUpdate()
        {
            // Fail silently...
            if (!enabled)
            {
                return;
            }

            // Update opacity if necessary.
            UpdateOpacity();

            // Updates Blend Colors if necessary.
            UpdateDrawableBlendColors();

            // Return early in case no camera is to be faced.
            if (CameraToFace == null)
            {
                return;
            }

            // Face camera.
            DrawablesRootTransform.rotation = (Quaternion.LookRotation(CameraToFace.transform.forward, Vector3.up));
        }

        #region Unity Event Handling

        /// <summary>
        /// Called by Unity.
        /// </summary>
        private void Start()
        {
            // Get cubism update controller.
            HasUpdateController = (GetComponent<CubismUpdateController>() != null);
        }

        /// <summary>
        /// Called by Unity. Enables listening to render data updates.
        /// </summary>
        private void OnEnable()
        {
            // Fail silently.
            if (!Model)
            {
                return;
            }

            CurrentOffscreenUnmanagedIndex = -1;

            // Make sure renderers are available.
            if (!IsInitialized)
            {
                Model.Revive();
                TryInitialize();
            }


            // Register listener.
            Model.OnDynamicDrawableData += OnDynamicDrawableData;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Model.ForceUpdateNow();

                for (var drawableIndex = 0; drawableIndex < DrawableRenderers.Length; drawableIndex++)
                {
                    DrawableRenderers[drawableIndex].SwapMeshes();
                }
            }
#endif

            if (GetComponent<ICubismRenderingInterceptor>() != null)
            {
                // Do not register at common controller when a rendering interceptor is attached.
                return;
            }

            // Register at common controller.
            CubismRenderControllerGroup.GetInstance().AddRenderController(this);
        }

        /// <summary>
        /// Called by Unity. Disables listening to render data updates.
        /// </summary>
        private void OnDisable()
        {
            // Fail silently.
            if (!Model)
            {
                return;
            }

            // Deregister listener.
            Model.OnDynamicDrawableData -= OnDynamicDrawableData;

            // Deregister at common controller.
            CubismRenderControllerGroup.GetInstance().RemoveRenderController(this);
        }

#endregion

        #region Cubism Event Handling

        /// <summary>
        /// Called by Unity.
        /// </summary>
        private void LateUpdate()
        {
            if (!HasUpdateController)
            {
                OnLateUpdate();
            }
        }

        private static int IndexOfDrawable(CubismRenderer[] renderers, int unmanagedIndex)
        {
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].Drawable.UnmanagedIndex == unmanagedIndex)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Called whenever new render data is available.
        /// </summary>
        /// <param name="sender">Model with new render data.</param>
        /// <param name="data">New render data.</param>
        private void OnDynamicDrawableData(CubismModel sender, CubismDynamicDrawableData[] data)
        {
            // Get drawables.
            var drawables = sender.Drawables;
            var renderers = DrawableRenderers;


            // Handle render data changes.
            for (var dataIndex = 0; dataIndex < data.Length; ++dataIndex)
            {
                var rendererIndex = IndexOfDrawable(renderers, dataIndex);

                // Skip if no renderer found.
                if (rendererIndex < 0) {
                    continue;
                }

                // Controls whether mesh buffers are to be swapped.
                var swapMeshes = false;

                // Update visibility if last SwapInfo flag is true.
                renderers[rendererIndex].UpdateVisibility();

                // Update render order if last SwapInfo flags is true.
                renderers[rendererIndex].UpdateRenderOrder();

                // Skip completely non-dirty data.
                if (!data[dataIndex].IsAnyDirty)
                {
                    continue;
                }


                // Update visibility.
                if (data[dataIndex].IsVisibilityDirty)
                {
                    renderers[rendererIndex].OnDrawableVisiblityDidChange(data[dataIndex].IsVisible);

                    swapMeshes = true;
                }


                // Update render order.
                if (data[dataIndex].IsRenderOrderDirty)
                {
                    renderers[rendererIndex].OnDrawableRenderOrderDidChange(data[dataIndex].RenderOrder);
                    DidChangeDrawableRenderOrder = true;
                    swapMeshes = true;
                }


                // Update opacity.
                if (data[dataIndex].IsOpacityDirty)
                {
                    renderers[rendererIndex].OnDrawableOpacityDidChange(data[dataIndex].Opacity);


                    swapMeshes = true;
                }


                // Update vertex positions.
                if (data[dataIndex].AreVertexPositionsDirty)
                {
                    renderers[rendererIndex].OnDrawableVertexPositionsDidChange(data[dataIndex].VertexPositions);


                    swapMeshes = true;
                }


                // Swap buffers if necessary.
                // [INV] Swapping only half of the meshes might improve performance even. Would that be visually feasible?
                if (swapMeshes)
                {
                    renderers[rendererIndex].SwapMeshes();
                }
            }


            // Pass draw order changes to handler (if available).
            var drawOrderHandler = DrawOrderHandlerInterface;


            if (drawOrderHandler != null)
            {
                for (var i = 0; i < data.Length; ++i)
                {
                    if (data[i].IsDrawOrderDirty)
                    {
                        drawOrderHandler.OnDrawOrderDidChange(this, drawables[i], data[i].DrawOrder);
                    }
                }
            }

            var isMultiplyColorUpdated = false;
            var isScreenColorUpdated = false;
            _newMultiplyColors ??= new Color[renderers.Length];
            _newScreenColors ??= new Color[renderers.Length];
            var newMultiplyColors = _newMultiplyColors;
            var newScreenColors = _newScreenColors;

            for (var dataIndex = 0; dataIndex < data.Length; ++dataIndex)
            {
                var rendererIndex = IndexOfDrawable(renderers, dataIndex);

                // Skip if no renderer found.
                if (rendererIndex < 0)
                {
                    continue;
                }

                var isUseModelMultiplyColor = !(renderers[rendererIndex].DrawObjectMultiplyColorEnabled ||
                                                MultiplyColorEnabled);

                // Skip processing when not using model colors.
                if (data[dataIndex].IsBlendColorDirty && isUseModelMultiplyColor)
                {
                    renderers[rendererIndex].ApplyMultiplyColor();
                    isMultiplyColorUpdated = true;
                }

                newMultiplyColors[rendererIndex] = renderers[rendererIndex].MultiplyColor;
            }

            for (var dataIndex = 0; dataIndex < data.Length; ++dataIndex)
            {
                var rendererIndex = IndexOfDrawable(renderers, dataIndex);

                // Skip if no renderer found.
                if (rendererIndex < 0)
                {
                    continue;
                }

                var isUseModelScreenColor = !(renderers[rendererIndex].DrawObjectScreenColorEnabled ||
                                              ScreenColorEnabled);

                // Skip processing when not using model colors.
                if (data[dataIndex].IsBlendColorDirty && isUseModelScreenColor)
                {
                    renderers[rendererIndex].ApplyScreenColor();
                    isScreenColorUpdated = true;
                }

                newScreenColors[rendererIndex] = renderers[rendererIndex].ScreenColor;
            }

            // Pass blend color changes to handler (if available).
            var multiplyColorHandlerInterface = MultiplyColorHandlerInterface;
            var screenColorHandlerInterface = ScreenColorHandlerInterface;

            if (MultiplyColorHandler != null && isMultiplyColorUpdated)
            {
                multiplyColorHandlerInterface.OnBlendColorDidChange(this, newMultiplyColors);
            }

            if (ScreenColorHandler != null && isScreenColorUpdated)
            {
                screenColorHandlerInterface.OnBlendColorDidChange(this, newScreenColors);
            }
        }

        #endregion
    }
}
