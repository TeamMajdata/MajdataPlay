/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using Live2D.Cubism.Core;
using Live2D.Cubism.Framework.Json;
using Live2D.Cubism.Rendering.Util;
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;


namespace Live2D.Cubism.Rendering
{
    /// <summary>
    /// Wrapper for drawing <see cref="CubismDrawable"/>s.
    /// </summary>
    [ExecuteInEditMode, RequireComponent(typeof(MeshRenderer))]
    public sealed partial class CubismRenderer : MonoBehaviour
    {
        /// <summary>
        /// <see cref="LocalSortingOrder"/> backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private int _localSortingOrder;

        /// <summary>
        /// Local sorting order.
        /// </summary>
        public int LocalSortingOrder
        {
            get
            {
                return _localSortingOrder;
            }
            set
            {
                // Return early if same value given.
                if (value == _localSortingOrder)
                {
                    return;
                }


                // Store value.
                _localSortingOrder = value;


                // Apply it.
                ApplySorting();
            }
        }


        /// <summary>
        /// <see cref="Color"/> backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private Color _color = Color.white;

        /// <summary>
        /// Color.
        /// </summary>
        public Color Color
        {
            get { return _color; }
            set
            {
                // Return early if same value given.
                if (value == _color)
                {
                    return;
                }


                // Store value.
                _color = value;

                // Apply color.
                ApplyVertexColors();
            }
        }

        /// <summary>
        /// <see cref="DrawObjectMultiplyColorEnabled"/> backing field.
        /// </summary>
        [FormerlySerializedAs("_isOverriddenDrawableMultiplyColors")] [SerializeField, HideInInspector]
        private bool isOverriddenDrawObjectMultiplyColors;

        /// <summary>
        /// Whether to override with multiply color from the model.
        /// </summary>
        public bool DrawObjectMultiplyColorEnabled
        {
            get { return isOverriddenDrawObjectMultiplyColors; }
            set { isOverriddenDrawObjectMultiplyColors = value; }
        }

        /// <summary>
        /// Last <see cref="DrawObjectMultiplyColorEnabled"/>.
        /// </summary>
        public bool LastIsUseUserMultiplyColor { get; set; }

        /// <summary>
        /// <see cref="DrawObjectScreenColorEnabled"/> backing field.
        /// </summary>
        [FormerlySerializedAs("_isOverriddenDrawableScreenColors")] [SerializeField, HideInInspector]
        private bool _isOverriddenDrawObjectScreenColors;

        /// <summary>
        /// Whether to override with screen color from the model.
        /// </summary>
        public bool DrawObjectScreenColorEnabled
        {
            get { return _isOverriddenDrawObjectScreenColors; }
            set { _isOverriddenDrawObjectScreenColors = value; }
        }

        /// <summary>
        /// Last <see cref="DrawObjectScreenColorEnabled"/>.
        /// </summary>
        public bool LastIsUseUserScreenColors { get; set; }

        /// <summary>
        /// <see cref="MultiplyColor"/> backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private Color _multiplyColor = Color.white;

        /// <summary>
        /// Drawable Multiply Color.
        /// </summary>
        public Color MultiplyColor
        {
            get
            {
                // If it can overwrite multiply color, return it.
                if (RenderController.MultiplyColorEnabled
                    || DrawObjectMultiplyColorEnabled)
                {
                    return _multiplyColor;
                }

                switch (DrawObjectType)
                {
                    case CubismModelTypes.DrawObjectType.Drawable:
                        return Drawable.MultiplyColor;
                    case CubismModelTypes.DrawObjectType.Offscreen:
                        return Offscreen.MultiplyColor;
                    default:
                        // If we are not a drawable or offscreen, return the default value.
                        return _multiplyColor;
                }
            }
            set
            {
                // Return early if same value given.
                if (value == _multiplyColor)
                {
                    return;
                }


                // Store value.
                _multiplyColor = (value != null)
                    ? value
                    : Color.white;
            }
        }

        /// <summary>
        /// Last Drawable Multiply Color.
        /// </summary>
        public Color LastMultiplyColor { get; set; }

        /// <summary>
        /// <see cref="ScreenColor"/> backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private Color _screenColor = Color.clear;

        /// <summary>
        /// Drawable Screen Color.
        /// </summary>
        public Color ScreenColor
        {
            get
            {
                if (RenderController.ScreenColorEnabled
                    || DrawObjectScreenColorEnabled)
                {
                    return _screenColor;
                }

                switch (DrawObjectType)
                {
                    case CubismModelTypes.DrawObjectType.Drawable:
                        return Drawable.ScreenColor;
                    case CubismModelTypes.DrawObjectType.Offscreen:
                        return Offscreen.ScreenColor;
                    default:
                        // If we are not a drawable or offscreen, return the default value.
                        return _screenColor;
                }
            }
            set
            {
                // Return early if same value given.
                if (value == _screenColor)
                {
                    return;
                }


                // Store value.
                _screenColor = (value != null)
                    ? value
                    : Color.black;
            }
        }

        /// <summary>
        /// Last Drawable Screen Color.
        /// </summary>
        public Color LastScreenColor { get; set; }


        /// <summary>
        /// <see cref="UnityEngine.Material"/>.
        /// </summary>
        public Material Material
        {
            get
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    if (!MeshRenderer.sharedMaterial)
                    {
                        MeshRenderer.sharedMaterial = SetMaterialFromPicker();
                    }

                    return MeshRenderer.sharedMaterial;
                }
#endif

                if (!MeshRenderer.material)
                {
                    MeshRenderer.material = SetMaterialFromPicker();
                }

                return MeshRenderer.material;
            }
            set
            {
                #if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    MeshRenderer.sharedMaterial = value;

                    return;
                }
                #endif


                MeshRenderer.material = value;
            }
        }


        /// <summary>
        /// <see cref="MainTexture"/> backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private Texture2D _mainTexture;

        /// <summary>
        /// <see cref="MeshRenderer"/>'s main texture.
        /// </summary>
        public Texture2D MainTexture
        {
            get
            {
                if (_mainTexture == null && DrawObjectType != CubismModelTypes.DrawObjectType.Drawable)
                {
                    _mainTexture = new Texture2D(1, 1);
                    _mainTexture.SetPixel(1, 1, Color.clear);
                }

                return _mainTexture;
            }
            set
            {
                // Return early if same value given and main texture is valid.
                if (value == _mainTexture && _mainTexture != null)
                {
                    return;
                }

                if (DrawObjectType != CubismModelTypes.DrawObjectType.Drawable)
                {
                    _mainTexture = new Texture2D(1, 1);
                    _mainTexture.SetPixel(1, 1, Color.clear);
                }
                else
                {
                    // Store value.
                    _mainTexture = (value != null)
                        ? value
                        : Texture2D.whiteTexture;
                }


                // Apply it.
                ApplyMainTexture();
            }
        }


        /// <summary>
        /// Meshes.
        /// </summary>
        /// <remarks>
        /// Double buffering dynamic meshes increases performance on mobile, so we double-buffer them here.
        /// </remarks>

        private Mesh[] Meshes { get; set; }

        /// <summary>
        /// Index of front buffer mesh.
        /// </summary>
        private int FrontMesh { get; set; }

        /// <summary>
        /// Index of back buffer mesh.
        /// </summary>
        private int BackMesh { get; set; }

        /// <summary>
        /// <see cref="UnityEngine.Mesh"/>.
        /// </summary>
        public Mesh Mesh
        {
            get
            {
                if (DrawObjectType == CubismModelTypes.DrawObjectType.Offscreen)
                {
                    return _offscreenMesh;
                }

                return FrontMesh < Meshes?.Length ? Meshes?[FrontMesh] : null;
            }
        }

        /// <summary>
        /// <see cref="MeshRenderer"/> backing field.
        /// </summary>
        [NonSerialized]
        private MeshRenderer _meshRenderer;

        /// <summary>
        /// <see cref="UnityEngine.MeshRenderer"/>.
        /// </summary>
        public MeshRenderer MeshRenderer
        {
            get
            {
                TryInitializeMeshRenderer();

                return _meshRenderer;
            }
        }

        /// <summary>
        /// <see cref="MeshFilter"/>'s backing field.
        /// </summary>
        [NonSerialized]
        private MeshFilter _meshFilter;

        /// <summary>
        /// <see cref="UnityEngine.MeshFilter"/> for Scene View picking support.
        /// </summary>
        public MeshFilter MeshFilter
        {
            get
            {
                TryInitializeMeshFilter();
                return _meshFilter;
            }

            set
            {
                if (value == _meshFilter || value == null)
                {
                    return;
                }

                _meshFilter = value;
            }
        }

        /// <summary>
        /// <see cref="DrawMaterial"/>'s backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private Material _drawMaterial;

        /// <summary>
        /// Material used for CommandBuffer rendering (the actual rendering material).
        /// </summary>
        public Material DrawMaterial
        {
            get
            {
                // Only return _drawMaterial for Drawable type (picking is only for Drawable).
                if (DrawObjectType != CubismModelTypes.DrawObjectType.Drawable)
                {
                    return null;
                }
                return _drawMaterial;
            }
            set { _drawMaterial = value; }
        }


        /// <summary>
        /// <see cref="CubismDrawable"/>.
        /// </summary>
        public CubismDrawable Drawable { get; set; }

        /// <summary>
        /// <see cref="CubismRenderController"/>.
        /// </summary>
        internal CubismRenderController RenderController { get; set; }


        #region Interface For CubismRenderController

        /// <summary>
        /// <see cref="SortingMode"/> backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private CubismSortingMode _sortingMode;

        /// <summary>
        /// Sorting mode.
        /// </summary>
        internal CubismSortingMode SortingMode
        {
            get
            {
                return _sortingMode;
            }
            set { _sortingMode = value; }
        }


        /// <summary>
        /// <see cref="SortingOrder"/> backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private int _sortingOrder;

        /// <summary>
        /// Sorting mode.
        /// </summary>
        private int SortingOrder
        {
            get { return _sortingOrder; }
            set { _sortingOrder = value; }
        }


        /// <summary>
        /// <see cref="RenderOrder"/> backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private int _renderOrder;

        /// <summary>
        /// Sorting mode.
        /// </summary>
        private int RenderOrder
        {
            get { return _renderOrder; }
            set { _renderOrder = value; }
        }


        /// <summary>
        /// <see cref="DepthOffset"/> backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private float _depthOffset = 0.00001f;

        /// <summary>
        /// Offset to apply in case of depth sorting.
        /// </summary>
        private float DepthOffset
        {
            get { return _depthOffset; }
            set { _depthOffset = value; }
        }


        /// <summary>
        /// <see cref="Opacity"/> backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private float _opacity;

        /// <summary>
        /// Opacity.
        /// </summary>
        internal float Opacity
        {
            get { return _opacity; }
            set { _opacity = value; }
        }


        /// <summary>
        /// Buffer for vertex colors.
        /// </summary>
        private Color[] VertexColors { get; set; }


        /// <summary>
        /// Allows tracking of what vertex data was updated last swap.
        /// </summary>
        private SwapInfo LastSwap { get; set; }

        /// <summary>
        /// Allows tracking of what vertex data will be swapped.
        /// </summary>
        private SwapInfo ThisSwap { get; set; }


        /// <summary>
        /// Swaps mesh buffers.
        /// </summary>
        /// <remarks>
        /// Make sure to manually call this method in case you changed the <see cref="Color"/>.
        /// </remarks>
        public void SwapMeshes()
        {
            // Perform internal swap.
            BackMesh = FrontMesh;
            FrontMesh = (FrontMesh == 0) ? 1 : 0;

            // Update colors.
            Meshes[BackMesh].colors = VertexColors;


            // Update swap info.
            LastSwap = ThisSwap;


            ResetSwapInfoFlags();
#if UNITY_EDITOR
            SyncMeshFilterForPicking();
#endif
        }


        /// <summary>
        /// Updates visibility.
        /// </summary>
        public void UpdateVisibility()
        {
            if (LastSwap.DidBecomeVisible)
            {
                MeshRenderer.enabled = true;
            }
            else if (LastSwap.DidBecomeInvisible)
            {
                MeshRenderer.enabled = false;
            }


            ResetVisibilityFlags();
        }


        /// <summary>
        /// Updates render order.
        /// </summary>
        public void UpdateRenderOrder()
        {
            if (LastSwap.NewRenderOrder)
            {
                ApplySorting();
            }


            ResetRenderOrderFlag();
        }

        /// <summary>
        /// Updates sorting layer.
        /// </summary>
        /// <param name="newSortingLayer">New sorting layer.</param>
        internal void OnControllerSortingLayerDidChange(int newSortingLayer)
        {
            MeshRenderer.sortingLayerID = newSortingLayer;
        }

        /// <summary>
        /// Updates sorting mode.
        /// </summary>
        /// <param name="newSortingMode">New sorting mode.</param>
        internal void OnControllerSortingModeDidChange(CubismSortingMode newSortingMode)
        {
            SortingMode = newSortingMode;


            ApplySorting();
        }

        /// <summary>
        /// Updates sorting order.
        /// </summary>
        /// <param name="newSortingOrder">New sorting order.</param>
        internal void OnControllerSortingOrderDidChange(int newSortingOrder)
        {
            SortingOrder = newSortingOrder;


            ApplySorting();
        }

        /// <summary>
        /// Updates depth offset.
        /// </summary>
        /// <param name="newDepthOffset">New depth offset value.</param>
        internal void OnControllerDepthOffsetDidChange(float newDepthOffset)
        {
            DepthOffset = newDepthOffset;


            ApplySorting();
        }


        /// <summary>
        /// Sets the opacity.
        /// </summary>
        /// <param name="newOpacity">New opacity.</param>
        internal void OnDrawableOpacityDidChange(float newOpacity)
        {
            Opacity = newOpacity;


            ApplyVertexColors();
        }

        /// <summary>
        /// Updates render order.
        /// </summary>
        /// <param name="newRenderOrder">New render order.</param>
        internal void OnDrawableRenderOrderDidChange(int newRenderOrder)
        {
            if (RenderOrder == newRenderOrder) return;


            RenderOrder = newRenderOrder;


            SetNewRenderOrder();
        }

        /// <summary>
        /// Sets the <see cref="UnityEngine.Mesh.vertices"/>.
        /// </summary>
        /// <param name="newVertexPositions">Vertex positions to set.</param>
        internal void OnDrawableVertexPositionsDidChange(Vector3[] newVertexPositions)
        {
            var mesh = Mesh;


            // Apply positions and update bounds.
            mesh.vertices = newVertexPositions;


            mesh.RecalculateBounds();


            // Set swap flag.
            SetNewVertexPositions();
        }

        /// <summary>
        /// Sets visibility.
        /// </summary>
        /// <param name="newVisibility">New visibility.</param>
        internal void OnDrawableVisiblityDidChange(bool newVisibility)
        {
            // Set swap flag if visible.
            if (newVisibility)
            {
                BecomeVisible();
            }
            else
            {
                BecomeInvisible();
            }
        }


        /// <summary>
        /// Sets model opacity.
        /// </summary>
        /// <param name="newModelOpacity">Opacity to set.</param>
        internal void OnModelOpacityDidChange(float newModelOpacity)
        {
            var property = PropertyBlock;
            _meshRenderer.GetPropertyBlock(property);


            // Write property.
            property.SetFloat(CubismShaderVariables.ModelOpacity, newModelOpacity);

            MeshRenderer.SetPropertyBlock(property);
        }

        #endregion

        /// <summary>
        /// Applies main texture for rendering.
        /// </summary>
        private void ApplyMainTexture()
        {
            var property = PropertyBlock;
            MeshRenderer.GetPropertyBlock(property);

            // Write property.
            property.SetTexture(CubismShaderVariables.MainTexture, MainTexture);

            MeshRenderer.SetPropertyBlock(property);
        }

        /// <summary>
        /// Applies sorting.
        /// </summary>
        private void ApplySorting()
        {
            // Return early if no controller or model.
            if (!RenderController
                || !RenderController.Model)
            {
                return;
            }

            RenderController.DidChangeSorting = true;

            // Sort by order.
            if (SortingMode.SortByOrder())
            {
                MeshRenderer.sortingOrder = SortingOrder + ((SortingMode == CubismSortingMode.BackToFrontOrder)
                    ? (RenderOrder + LocalSortingOrder)
                    : -(RenderOrder + LocalSortingOrder));


                transform.localPosition = Vector3.zero;


                return;
            }


            // Sort by depth.
            var offset = (SortingMode == CubismSortingMode.BackToFrontZ)
                    ? -DepthOffset
                    : DepthOffset;


            MeshRenderer.sortingOrder = SortingOrder + LocalSortingOrder;

            transform.localPosition = new Vector3(0f, 0f, RenderOrder * offset);
        }

        /// <summary>
        /// Uploads mesh vertex colors.
        /// </summary>
        public void ApplyVertexColors()
        {


            var vertexColors = VertexColors;
            var color = Color;


            color.a *= Opacity;


            for (var i = 0; i < vertexColors.Length; ++i)
            {
                vertexColors[i] = color;
            }


            // Set swap flag.
            SetNewVertexColors();
        }

        /// <summary>
        /// Uploads diffuse colors.
        /// </summary>
        public void ApplyMultiplyColor()
        {
            if (DrawObjectType != CubismModelTypes.DrawObjectType.Drawable)
            {
                return;
            }

            var property = PropertyBlock;
            MeshRenderer.GetPropertyBlock(property);


            // Write property.
            property.SetColor(CubismShaderVariables.MultiplyColor, MultiplyColor);

            MeshRenderer.SetPropertyBlock(property);
        }

        /// <summary>
        /// Initializes the main texture if possible.
        /// </summary>
        private void TryInitializeMultiplyColor()
        {
            LastIsUseUserMultiplyColor = false;

            LastMultiplyColor = MultiplyColor;

            if (DrawObjectType != CubismModelTypes.DrawObjectType.Drawable)
            {
                return;
            }

            ApplyMultiplyColor();
        }

        /// <summary>
        /// Uploads tint colors.
        /// </summary>
        public void ApplyScreenColor()
        {
            if (DrawObjectType != CubismModelTypes.DrawObjectType.Drawable)
            {
                return;
            }

            var property = PropertyBlock;
            MeshRenderer.GetPropertyBlock(property);


            // Write property.
            property.SetColor(CubismShaderVariables.ScreenColor, ScreenColor);

            MeshRenderer.SetPropertyBlock(property);
        }

        /// <summary>
        /// Initializes the main texture if possible.
        /// </summary>
        private void TryInitializeScreenColor()
        {
            LastIsUseUserScreenColors = false;

            LastScreenColor = ScreenColor;

            if (DrawObjectType != CubismModelTypes.DrawObjectType.Drawable)
            {
                return;
            }

            ApplyScreenColor();
        }

        /// <summary>
        /// Sets material from picker.
        /// </summary>
        public Material SetMaterialFromPicker()
        {
            Material material = null;

            switch (DrawObjectType)
            {
                case CubismModelTypes.DrawObjectType.Drawable:
                    if (!Drawable)
                    {
                        break;
                    }

                    material = CubismBuiltinPickers.DrawableMaterialPicker(null, Drawable);
                    break;
                case CubismModelTypes.DrawObjectType.Offscreen:
                    if (!Offscreen)
                    {
                        break;
                    }

                    material = CubismBuiltinPickers.OffscreenMaterialPicker(null, Offscreen);
                    break;
                default:
                    material = CubismBuiltinMaterials.GetBlendModeMaterial("UnlitBlendMode", BlendTypes.ColorBlend.Normal, BlendTypes.AlphaBlend.Over, false, false, true);
                    Debug.LogError("Unsupported DrawObjectType.");
                    break;
            }

            return material;
        }

        /// <summary>
        /// Initializes the mesh renderer.
        /// </summary>
        private void TryInitializeMeshRenderer()
        {
            if (!_meshRenderer)
            {
                _meshRenderer = GetComponent<MeshRenderer>();


                // Lazily add component.
                if (!_meshRenderer)
                {
                    _meshRenderer = gameObject.AddComponent<MeshRenderer>();
                    _meshRenderer.hideFlags = HideFlags.HideInInspector;
                    _meshRenderer.receiveShadows = false;
                    _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    _meshRenderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                }
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (!_meshRenderer.sharedMaterial)
                {
                    _meshRenderer.sharedMaterial = SetMaterialFromPicker();
                }

                return;
            }
#endif

            if (!_meshRenderer.material)
            {
                _meshRenderer.material = SetMaterialFromPicker();
            }
        }

        /// <summary>
        /// Initializes the mesh filter for Scene View picking.
        /// </summary>
        private void TryInitializeMeshFilter()
        {
#if UNITY_EDITOR
            if (DrawObjectType != CubismModelTypes.DrawObjectType.Drawable
                || Application.isPlaying)
            {
                return;
            }

            if (_meshFilter != null)
            {
                return;
            }

            _meshFilter = GetComponent<MeshFilter>();

            // Lazily add component if missing.
            if (_meshFilter == null)
            {
                _meshFilter = gameObject.AddComponent<MeshFilter>();
                _meshFilter.hideFlags = HideFlags.HideInInspector;
            }

            _meshFilter.sharedMesh = Mesh;
             SetupPickingMaterial();
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// Sets up materials for Scene View picking.
        /// </summary>
        private void SetupPickingMaterial()
        {
            if (_meshRenderer == null)
            {
                return;
            }

            var currentMaterial = _meshRenderer.sharedMaterial;

            if (_drawMaterial == null)
            {
                // If current material is TransparentPicking or null, get the correct material via picker.
                if (currentMaterial == null
                    || currentMaterial == CubismBuiltinMaterials.TransparentPicking
                    || (currentMaterial.shader != null && currentMaterial.shader.name == "Live2D Cubism/TransparentPicking"))
                {
                    _drawMaterial = SetMaterialFromPicker();
                }
                else
                {
                    _drawMaterial = currentMaterial;
                }
            }

            _meshRenderer.sharedMaterial = CubismBuiltinMaterials.TransparentPicking;

            // Set MainTexture for alpha test in picking shader.
            if (MainTexture != null)
            {
                ApplyMainTexture();
            }
        }

        /// <summary>
        /// Syncs the current mesh to the MeshFilter for Scene View picking.
        /// </summary>
        private void SyncMeshFilterForPicking()
        {
            if (DrawObjectType != CubismModelTypes.DrawObjectType.Drawable)
            {
                return;
            }

            if (_meshFilter == null || Mesh == null)
            {
                return;
            }

            _meshFilter.sharedMesh = Mesh;
        }
#endif


        /// <summary>
        /// Initializes the mesh if necessary.
        /// </summary>
        private void TryInitializeMesh()
        {
            // Only create mesh if necessary.
            // HACK: 'Mesh != null' is individually implemented to avoid errors caused by the absence of a backing field.
            // HACK: 'Mesh.vertex > 0' makes sure mesh is recreated in case of runtime instantiation.
            if ((Meshes != null && Meshes.Length == 2
                && Mesh != null && Mesh.vertexCount > 0
                && Drawable?.VertexPositions != null && Mesh.vertexCount == Drawable?.VertexPositions.Length)
                || (DrawObjectType == CubismModelTypes.DrawObjectType.Offscreen && _offscreenMesh))
            {
                return;
            }

            if (DrawObjectType == CubismModelTypes.DrawObjectType.Offscreen)
            {
                Meshes = new Mesh[1];
                _offscreenMesh = new Mesh
                {
                    vertices = OffscreenVertices,
                    uv = OffscreenUVs,
                    triangles = OffscreenTriangle
                };
                Meshes[0] = _offscreenMesh;
                return;
            }


            if (Meshes != null)
            {
                for (var i = 0; i < Meshes.Length; i++)
                {
                    DestroyImmediate(Meshes[i]);
                }
            }

            Meshes = new Mesh[2];

            for (var i = 0; i < 2; ++i)
            {
                var mesh = new Mesh();

                mesh.name = Drawable.name;
                mesh.vertices = Drawable.VertexPositions;
                mesh.uv = Drawable.VertexUvs;
                mesh.triangles = Drawable.Indices;

                mesh.MarkDynamic();
                mesh.RecalculateBounds();


                // Store mesh.
                Meshes[i] = mesh;
            }
        }

        /// <summary>
        /// Initializes vertex colors.
        /// </summary>
        private void TryInitializeVertexColor()
        {
            if (Mesh == null)
            {
                return;
            }

            var mesh = Mesh;


            VertexColors = new Color[mesh.vertexCount];


            for (var i = 0; i < VertexColors.Length; ++i)
            {
                VertexColors[i] = Color;
                VertexColors[i].a *= Opacity;
            }
        }

        /// <summary>
        /// Initializes the main texture if possible.
        /// </summary>
        private void TryInitializeMainTexture()
        {
            if (!MainTexture)
            {
                MainTexture = Texture2D.whiteTexture;
            }


            ApplyMainTexture();
        }

        /// <summary>
        /// Initializes components if possible.
        /// </summary>
        public void TryInitialize(CubismRenderController renderController)
        {
            RenderController = renderController;

            if (!RenderController.Model)
            {
                return;
            }

            InitializeDrawObject();

            TryInitializeMeshRenderer();

            TryInitializeMesh();
            TryInitializeMeshFilter();
            TryInitializeVertexColor();
            TryInitializeMainTexture();
            TryInitializeMultiplyColor();
            TryInitializeScreenColor();
            _previousOffscreenUnmanagedIndex = -1;

            ApplySorting();
#if UNITY_EDITOR
            SyncMeshFilterForPicking();
#endif
        }

        #region Swap Info

        /// <summary>
        /// Sets <see cref="NewVertexPositions"/>.
        /// </summary>
        private void SetNewVertexPositions()
        {
            var swapInfo = ThisSwap;
            swapInfo.NewVertexPositions = true;
            ThisSwap = swapInfo;
        }


        /// <summary>
        /// Sets <see cref="NewVertexColors"/>.
        /// </summary>
        private void SetNewVertexColors()
        {
            var swapInfo = ThisSwap;
            swapInfo.NewVertexColors = true;
            ThisSwap = swapInfo;
        }


        /// <summary>
        /// Sets <see cref="DidBecomeVisible"/> on visible.
        /// </summary>
        private void BecomeVisible()
        {
            var swapInfo = ThisSwap;
            swapInfo.DidBecomeVisible = true;
            ThisSwap = swapInfo;
        }


        /// <summary>
        /// Sets <see cref="DidBecomeInvisible"/> on invisible.
        /// </summary>
        private void BecomeInvisible()
        {
            var swapInfo = ThisSwap;
            swapInfo.DidBecomeInvisible = true;
            ThisSwap = swapInfo;
        }


        /// <summary>
        /// Sets <see cref="SetNewRenderOrder"/>.
        /// </summary>
        private void SetNewRenderOrder()
        {
            var swapInfo = ThisSwap;
            swapInfo.NewRenderOrder = true;
            ThisSwap = swapInfo;
        }


        /// <summary>
        /// Resets flags.
        /// </summary>
        private void ResetSwapInfoFlags()
        {
            ThisSwap = default;
        }


        /// <summary>
        /// Reset visibility flags.
        /// </summary>
        private void ResetVisibilityFlags()
        {
            var swapInfo = LastSwap;
            swapInfo.DidBecomeVisible = false;
            swapInfo.DidBecomeInvisible = false;
            LastSwap = swapInfo;
        }


        /// <summary>
        /// Reset render order flag.
        /// </summary>
        private void ResetRenderOrderFlag()
        {
            var swapInfo = LastSwap;
            swapInfo.NewRenderOrder = false;
            LastSwap = swapInfo;
        }


        /// <summary>
        /// Allows tracking of <see cref="Mesh"/> data changed on a swap.
        /// </summary>
        private struct SwapInfo
        {
            /// <summary>
            /// Vertex positions were changed.
            /// </summary>
            public bool NewVertexPositions { get; set; }

            /// <summary>
            /// Vertex colors were changed.
            /// </summary>
            public bool NewVertexColors { get; set; }

            /// <summary>
            /// Visibility were changed to visible.
            /// </summary>
            public bool DidBecomeVisible { get; set; }

            /// <summary>
            /// Visibility were changed to invisible.
            /// </summary>
            public bool DidBecomeInvisible { get; set; }

            /// <summary>
            /// Render order were changed.
            /// </summary>
            public bool NewRenderOrder { get; set; }
        }

        #endregion



        #region Unity Events Handling

        /// <summary>
        /// Finalizes instance.
        /// </summary>
        private void OnDestroy()
        {
            if (Meshes == null)
            {
                return;
            }


            for (var i = 0; i < Meshes.Length; i++)
            {
                DestroyImmediate(Meshes[i]);
            }
        }

        #endregion
    }
}
