using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using MajdataPlay.Diagnostics;

#if UNITY_EDITOR
using UnityEditor;
#endif
#nullable enable
namespace MajdataPlay.Rendering
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class RawSpriteRenderer : MonoBehaviour
    {
        // ============================================================
        // Inspector
        // ============================================================

        [Header("Sprite")]
        [FormerlySerializedAs("sprite")]
        [SerializeField]
        private Sprite _sprite;

        [FormerlySerializedAs("color")]
        [SerializeField]
        private Color _color = Color.white;

        [SerializeField]
        private bool _flipX;

        [SerializeField]
        private bool _flipY;

        [Header("Draw Mode")]
        [SerializeField]
        private SpriteDrawMode _drawMode = SpriteDrawMode.Simple;

        [SerializeField]
        private Vector2 _size = Vector2.one;

        [SerializeField]
        private SpriteTileMode _tileMode = SpriteTileMode.Continuous;

        [SerializeField, Range(0f, 1f)]
        private float _adaptiveModeThreshold = 0.5f;

        [Header("Sorting")]
        [SerializeField]
        private string _sortingLayerName = "Default";

        [SerializeField]
        private int _sortingOrder;

        [Header("Rendering")]
        [SerializeField]
        private Material _sharedMaterial;

        [SerializeField]
        private ShadowCastingMode _shadowCastingMode = ShadowCastingMode.Off;

        [SerializeField]
        private bool _receiveShadows;

        [SerializeField]
        private MotionVectorGenerationMode _motionVectorGenerationMode =
            MotionVectorGenerationMode.Object;

        // ============================================================
        // SpriteRenderer-like public API
        // ============================================================

        public Sprite Sprite
        {
            get => _sprite;
            set
            {
                if (_sprite == value)
                {
                    return;
                }

                _sprite = value;
                MarkSpriteDirty();
            }
        }

        public Color Color
        {
            get => _color;
            set
            {
                if (_color == value)
                {
                    return;
                }

                _color = value;
                MarkColorDirty();
            }
        }

        public bool FlipX
        {
            get => _flipX;
            set
            {
                if (_flipX == value)
                {
                    return;
                }

                _flipX = value;
                MarkGeometryDirty();
            }
        }

        public bool FlipY
        {
            get => _flipY;
            set
            {
                if (_flipY == value)
                {
                    return;
                }

                _flipY = value;
                MarkGeometryDirty();
            }
        }

        public SpriteDrawMode DrawMode
        {
            get => _drawMode;
            set
            {
                if (_drawMode == value)
                {
                    return;
                }
                _drawMode = value;
                MarkGeometryDirty();
            }
        }

        public Vector2 Size
        {
            get => _size;
            set
            {
                value = new Vector2(SanitizeSize(value.x), SanitizeSize(value.y));
                if (_size.Equals(value))
                {
                    return;
                }
                _size = value;
                MarkGeometryDirty();
            }
        }

        public SpriteTileMode TileMode
        {
            get => _tileMode;
            set
            {
                if (_tileMode == value)
                {
                    return;
                }
                _tileMode = value;
                MarkGeometryDirty();
            }
        }

        public float AdaptiveModeThreshold
        {
            get => _adaptiveModeThreshold;
            set
            {
                value = float.IsNaN(value) ? 0.5f : Mathf.Clamp01(value);
                if (_adaptiveModeThreshold == value)
                {
                    return;
                }
                _adaptiveModeThreshold = value;
                MarkGeometryDirty();
            }
        }

        public string SortingLayerName
        {
            get => _sortingLayerName;
            set
            {
                value ??= "Default";

                if (_sortingLayerName == value)
                {
                    return;
                }

                _sortingLayerName = value;
                MarkSortingDirty();
            }
        }

        public int SortingLayerID
        {
            get
            {
                return _meshRenderer.sortingLayerID;
            }

            set
            {

                if (_meshRenderer.sortingLayerID == value)
                {
                    return;
                }

                _meshRenderer.sortingLayerID = value;

                _sortingLayerName = SortingLayer.IDToName(value);
            }
        }

        public int SortingOrder
        {
            get => _sortingOrder;
            set
            {
                if (_sortingOrder == value)
                    return;

                _sortingOrder = value;
                MarkSortingDirty();
            }
        }

        public Material SharedMaterial
        {
            get
            {
                if (_sharedMaterial != null)
                {
                    return _sharedMaterial;
                }
                else
                {
                    return _meshRenderer.sharedMaterial;
                }
            }
            set
            {
                if (_sharedMaterial == value)
                {
                    return;
                }

                _sharedMaterial = value;
                ApplyMaterial();
            }
        }

        public ShadowCastingMode ShadowCastingMode
        {
            get => _shadowCastingMode;
            set
            {
                if (_shadowCastingMode == value)
                    return;

                _shadowCastingMode = value;
                ApplyRendererSettings();
            }
        }

        public bool ReceiveShadows
        {
            get => _receiveShadows;
            set
            {
                if (_receiveShadows == value)
                    return;

                _receiveShadows = value;
                ApplyRendererSettings();
            }
        }

        public MotionVectorGenerationMode MotionVectorGenerationMode
        {
            get => _motionVectorGenerationMode;
            set
            {
                if (_motionVectorGenerationMode == value)
                    return;

                _motionVectorGenerationMode = value;
                ApplyRendererSettings();
            }
        }


        public Bounds Bounds
        {
            get
            {
                return _meshRenderer.bounds;
            }
        }

        /// <summary>
        /// Gets or overrides the renderer's local-space axis-aligned bounds.
        /// Custom bounds are not serialized; use ResetLocalBounds to restore automatic bounds.
        /// </summary>
        public Bounds LocalBounds
        {
            get
            {
                // Property changes normally wait until PreLateUpdate to rebuild geometry.
                if (_spriteDirty || _geometryDirty)
                {
                    ApplyGeometry();
                }

                return _meshRenderer.localBounds;
            }
            set
            {
                _meshRenderer.localBounds = value;
            }
        }
        public bool IsVisible
        {
            get
            {
                return _meshRenderer.isVisible;
            }
        }

        public Renderer RendererComponent
        {
            get
            {
                return _meshRenderer;
            }
        }

        // ============================================================
        // Internal
        // ============================================================

        private static readonly int RendererColorID =
            Shader.PropertyToID("_RendererColor");

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;

        private RawSpriteResources.MeshEntry? _meshEntry;
        private RawSpriteResources.MaterialEntry? _materialEntry;
        private MaterialPropertyBlock _propertyBlock;

        private int _rawSpriteUpdaterIndex = -1; // Updater index

        // ------------------------------------------------------------
        // Cached state
        // ------------------------------------------------------------

        private Sprite _appliedSprite;
        private Color _appliedColor;

        private bool _appliedFlipX;
        private bool _appliedFlipY;
        private SpriteDrawMode _appliedDrawMode;
        private Vector2 _appliedSize;
        private SpriteTileMode _appliedTileMode;
        private float _appliedAdaptiveModeThreshold;

        private int _appliedSortingLayerID;
        private int _appliedSortingOrder;

        private Material _appliedMaterial;

        private bool _spriteDirty = true;
        private bool _geometryDirty = true;
        private bool _colorDirty = true;
        private bool _sortingDirty = true;
        private bool _materialDirty = true;
        private bool _rendererSettingsDirty = true;

        // ============================================================
        // Unity lifecycle
        // ============================================================

        private void Awake()
        {
            _propertyBlock = new();
            _meshRenderer = GetComponent<MeshRenderer>();
            _meshFilter = GetComponent<MeshFilter>();
            Updater.Register(this);
        }

        private void OnEnable()
        {
            _meshRenderer.enabled = true;

            _spriteDirty = true;
            _geometryDirty = true;
            _colorDirty = true;
            _sortingDirty = true;
            _materialDirty = true;
            _rendererSettingsDirty = true;

            ApplyAll();
        }

        protected virtual void OnPreLateUpdate()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            // ExecuteAlways 情况下，Inspector / Animation
            // 可能绕过 C# property setter，所以这里仍然做一次 cheap check。

            if (_sprite != _appliedSprite)
            {
                MarkSpriteDirty();
            }

            if (_color != _appliedColor)
            {
                MarkColorDirty();
            }

            if (_flipX != _appliedFlipX ||
                _flipY != _appliedFlipY ||
                _drawMode != _appliedDrawMode ||
                !_size.Equals(_appliedSize) ||
                _tileMode != _appliedTileMode ||
                _adaptiveModeThreshold != _appliedAdaptiveModeThreshold)
            {
                MarkGeometryDirty();
            }

            if (_appliedSortingLayerID != SortingLayer.NameToID(_sortingLayerName))
            {
                MarkSortingDirty();
            }

            if (_appliedSortingOrder != _sortingOrder)
            {
                MarkSortingDirty();
            }

            if (_sharedMaterial != _appliedMaterial)
            {
                MarkMaterialDirty();
            }

            ApplyDirty();
        }

#if UNITY_EDITOR

        private void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                OnPreLateUpdate();
            }
        }

        private void OnValidate()
        {
            // OnValidate 可能发生在对象还没完成初始化的时候。
            if (!Application.isPlaying)
            {
                EditorApplication.delayCall += DelayedValidate;
            }
            else
            {
                MarkAllDirty();
            }
        }

        private void DelayedValidate()
        {
            if (this == null)
                return;

            if (gameObject == null)
                return;

            MarkAllDirty();

            if (isActiveAndEnabled)
                ApplyAll();
        }

#endif

        private void OnDisable()
        {
            if (_meshRenderer != null)
                _meshRenderer.enabled = false;
        }

        private void OnDestroy()
        {
            Updater.Unregister(this);
            if (_meshFilter != null)
                _meshFilter.sharedMesh = null;
            if (_meshRenderer != null)
                _meshRenderer.sharedMaterial = null;
            RawSpriteResources.Release(_meshEntry);
            RawSpriteResources.Release(_materialEntry);
        }
        public void ResetLocalBounds()
        {
            _meshRenderer.ResetLocalBounds();
        }
        public void ResetBounds()
        {
            _meshRenderer.ResetBounds();
        }
        public void GetPropertyBlock(MaterialPropertyBlock properties)
        {
            if (properties == null)
            {
                throw new ArgumentNullException(nameof(properties));
            }

            _meshRenderer.GetPropertyBlock(properties);
        }

        public void SetPropertyBlock(MaterialPropertyBlock properties)
        {
            if (properties == null)
            {
                throw new ArgumentNullException(nameof(properties));
            }

            _meshRenderer.SetPropertyBlock(properties);
        }

        public void GetPropertyBlock(MaterialPropertyBlock properties, int materialIndex)
        {
            if (properties == null)
            {
                throw new ArgumentNullException(nameof(properties));
            }

            _meshRenderer.GetPropertyBlock(properties, materialIndex);
        }

        public void SetPropertyBlock(MaterialPropertyBlock properties, int materialIndex)
        {
            if (properties == null)
            {
                throw new ArgumentNullException(nameof(properties));
            }

            _meshRenderer.SetPropertyBlock(properties, materialIndex);
        }


        // ============================================================
        // Dirty
        // ============================================================

        private void MarkSpriteDirty()
        {
            _spriteDirty = true;
            _geometryDirty = true;

            _materialDirty = true;
        }

        private void MarkGeometryDirty()
        {
            _geometryDirty = true;
        }

        private void MarkColorDirty()
        {
            _colorDirty = true;
        }

        private void MarkSortingDirty()
        {
            _sortingDirty = true;
        }

        private void MarkMaterialDirty()
        {
            _materialDirty = true;
        }

        protected void MarkAllDirty()
        {
            MarkColorDirty();
            MarkGeometryDirty();
            MarkSpriteDirty();
            MarkSortingDirty();
            MarkMaterialDirty();
            _rendererSettingsDirty = true;
        }

        // ============================================================
        // Apply
        // ============================================================

        private void ApplyAll()
        {
            ApplyDirty();
        }

        private void ApplyDirty()
        {

            if (_spriteDirty || _geometryDirty)
            {
                ApplyGeometry();
            }

            if (_colorDirty)
            {
                ApplyColor();
            }

            if (_sortingDirty)
            {
                ApplySorting();
            }

            if (_materialDirty)
            {
                ApplyMaterial();
            }

            if (_rendererSettingsDirty)
            {
                ApplyRendererSettings();
            }
        }

        // ============================================================
        // Sprite / Mesh
        // ============================================================

        private void ApplyGeometry()
        {
            _spriteDirty = false;
            _geometryDirty = false;

            _size = new Vector2(SanitizeSize(_size.x), SanitizeSize(_size.y));
            _adaptiveModeThreshold = float.IsNaN(_adaptiveModeThreshold)
                ? 0.5f : Mathf.Clamp01(_adaptiveModeThreshold);
            _meshFilter.sharedMesh = AcquireMesh(_sprite, _flipX, _flipY);

            _appliedSprite = _sprite;
            _appliedFlipX = _flipX;
            _appliedFlipY = _flipY;
            _appliedDrawMode = _drawMode;
            _appliedSize = _size;
            _appliedTileMode = _tileMode;
            _appliedAdaptiveModeThreshold = _adaptiveModeThreshold;
            _materialDirty = true;
        }

        protected virtual Mesh? AcquireMesh(Sprite? sprite, bool flipX, bool flipY)
        {
            // Instancing requires the same Mesh object, not just identical vertices.
            var next = RawSpriteResources.AcquireMesh(sprite, flipX, flipY,
                _drawMode, _size, _tileMode, _adaptiveModeThreshold);
            RawSpriteResources.Release(_meshEntry);
            _meshEntry = next;

            return next?.Mesh;
        }

        protected virtual Material AcquireMaterial(Material? source, Texture? texture)
        {
            // Textures belong to a shared material. A texture in the property block
            // disables GPU instancing even when every renderer uses the same texture.
            var next = RawSpriteResources.AcquireMaterial(source, texture);
            var material = next != null ? next.Material : source;
            RawSpriteResources.Release(_materialEntry);
            _materialEntry = next;

            return material;
        }

        // ============================================================
        // Color
        // ============================================================

        private void ApplyColor()
        {
            _colorDirty = false;

            _appliedColor = _color;

            _meshRenderer.GetPropertyBlock(_propertyBlock);

            // 与 SpriteRenderer / Sprites shader 使用方式一致。
            _propertyBlock.SetColor(
                RendererColorID,
                _color);

            _meshRenderer.SetPropertyBlock(_propertyBlock);
        }

        // ============================================================
        // Sorting
        // ============================================================

        private void ApplySorting()
        {
            _sortingDirty = false;

            int sortingLayerID =
                SortingLayer.NameToID(_sortingLayerName);

            _meshRenderer.sortingLayerID = sortingLayerID;
            _meshRenderer.sortingOrder = _sortingOrder;

            _appliedSortingLayerID = sortingLayerID;
            _appliedSortingOrder = _sortingOrder;
        }

        // ============================================================
        // Material
        // ============================================================

        private void ApplyMaterial()
        {
            _materialDirty = false;

            _meshRenderer.sharedMaterial = AcquireMaterial(_sharedMaterial, _sprite != null ? _sprite.texture : null);

            _appliedMaterial = _sharedMaterial;
        }

        // ============================================================
        // Renderer settings
        // ============================================================

        private void ApplyRendererSettings()
        {
            _rendererSettingsDirty = false;

            _meshRenderer.shadowCastingMode =
                _shadowCastingMode;

            _meshRenderer.receiveShadows =
                _receiveShadows;

            _meshRenderer.motionVectorGenerationMode =
                _motionVectorGenerationMode;
        }
        private static float SanitizeSize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
        }

        public static class Updater
        {
            private sealed class PlayerLoopMarker
            {
            }

            private static RawSpriteRenderer[] s_Renderers;
            private static int s_Count;

            private static bool s_Installed;

            private static readonly PlayerLoopSystem.UpdateFunction s_UpdateDelegate = UpdateAll;


            public static void Register(RawSpriteRenderer renderer)
            {
                if (renderer == null)
                    return;

                int index = renderer._rawSpriteUpdaterIndex;

                // 防止重复注册
                if (index >= 0)
                    return;

                EnsureInstalled();

                if (s_Renderers == null)
                {
                    s_Renderers = new RawSpriteRenderer[16];
                }
                else if (s_Count == s_Renderers.Length)
                {
                    Array.Resize(
                        ref s_Renderers,
                        s_Renderers.Length < 1024
                            ? s_Renderers.Length * 2
                            : s_Renderers.Length + 1024
                    );
                }

                index = s_Count++;

                s_Renderers[index] = renderer;
                renderer._rawSpriteUpdaterIndex = index;
            }

            public static void Unregister(RawSpriteRenderer renderer)
            {
                if (renderer == null)
                    return;

                int index = renderer._rawSpriteUpdaterIndex;

                if ((uint)index >= (uint)s_Count)
                {
                    renderer._rawSpriteUpdaterIndex = -1;
                    return;
                }

                RawSpriteRenderer[] renderers = s_Renderers;

                int lastIndex = --s_Count;
                RawSpriteRenderer last = renderers[lastIndex];

                if (index != lastIndex)
                {
                    renderers[index] = last;
                    last._rawSpriteUpdaterIndex = index;
                }

                renderers[lastIndex] = null;
                renderer._rawSpriteUpdaterIndex = -1;
            }

            private static void UpdateAll()
            {
                var renderers = s_Renderers;
                var count = s_Count;

                for (var i = 0; i < count; i++)
                {
                    RawSpriteRenderer renderer = renderers[i];

                    renderer.OnPreLateUpdate();
                }
            }

            private static void EnsureInstalled()
            {
                if (s_Installed)
                    return;

                PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();

                if (!InsertAtEndOfPhase<PreLateUpdate>(
                        ref loop,
                        typeof(PlayerLoopMarker),
                        s_UpdateDelegate))
                {
                    MajDebug.LogError(
                        "RawSpriteUpdater: Failed to install into PreLateUpdate."
                    );
                    return;
                }

                PlayerLoop.SetPlayerLoop(loop);
                s_Installed = true;
            }

            private static bool InsertAtEndOfPhase<TPhase>(
                ref PlayerLoopSystem root,
                Type markerType,
                PlayerLoopSystem.UpdateFunction updateDelegate)
                where TPhase : struct
            {
                return InsertAtEndOfPhase(
                    ref root,
                    typeof(TPhase),
                    markerType,
                    updateDelegate);
            }

            private static bool InsertAtEndOfPhase(
                ref PlayerLoopSystem system,
                Type phaseType,
                Type markerType,
                PlayerLoopSystem.UpdateFunction updateDelegate)
            {
                PlayerLoopSystem[] children = system.subSystemList;

                if (children == null)
                    return false;

                for (int i = 0; i < children.Length; i++)
                {
                    PlayerLoopSystem child = children[i];

                    if (child.type == phaseType)
                    {
                        if (ContainsMarker(child, markerType))
                            return true;

                        PlayerLoopSystem[] oldChildren = child.subSystemList;

                        int oldCount = oldChildren?.Length ?? 0;

                        PlayerLoopSystem[] newChildren =
                            new PlayerLoopSystem[oldCount + 1];

                        if (oldCount != 0)
                        {
                            Array.Copy(
                                oldChildren,
                                0,
                                newChildren,
                                0,
                                oldCount);
                        }

                        newChildren[oldCount] = new PlayerLoopSystem
                        {
                            type = markerType,
                            updateDelegate = updateDelegate
                        };

                        child.subSystemList = newChildren;
                        children[i] = child;
                        system.subSystemList = children;

                        return true;
                    }

                    if (InsertAtEndOfPhase(
                            ref child,
                            phaseType,
                            markerType,
                            updateDelegate))
                    {
                        children[i] = child;
                        system.subSystemList = children;
                        return true;
                    }
                }

                return false;
            }

            private static bool ContainsMarker(
                PlayerLoopSystem system,
                Type markerType)
            {
                if (system.type == markerType)
                    return true;

                PlayerLoopSystem[] children = system.subSystemList;

                if (children == null)
                    return false;

                for (int i = 0; i < children.Length; i++)
                {
                    if (ContainsMarker(children[i], markerType))
                        return true;
                }

                return false;
            }

            /// <summary>
            /// 处理 Domain Reload Disabled / PlayMode 重启。
            /// </summary>
            [RuntimeInitializeOnLoadMethod(
                RuntimeInitializeLoadType.SubsystemRegistration)]
            private static void ResetStatics()
            {
                s_Renderers = null;
                s_Count = 0;
                s_Installed = false;
            }
        }
    }
}
