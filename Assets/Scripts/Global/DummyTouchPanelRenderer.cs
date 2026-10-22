using MajdataPlay.Collections;
using MajdataPlay.IO;
using MajdataPlay.Settings;
using MajdataPlay.Utils;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Serialization;
#nullable enable
namespace MajdataPlay
{
    /// <summary>Visualizes touch sensor states while retaining the original input colliders.</summary>
    [DontDestroyOnLoad]
    [DefaultExecutionOrder(180)]
    internal sealed class DummyTouchPanelRenderer : MajComponent
    {
        /// <summary>Maps collider instance identifiers to the existing sensor indices.</summary>
        public IReadOnlyDictionary<int, int> InstanceID2SensorIndexMappingTable => _instanceID2SensorIndexMappingTable;

        /// <summary>Provides the texture-free transparent material shared by all debug styles.</summary>
        [SerializeField]
        [FormerlySerializedAs("SharedInstancedMaterial")]
        [FormerlySerializedAs("sharedInstancedMaterial")]
        private Material _sharedInstancedMaterial = null!;

        /// <summary>Retains the collider lookup independently of visualization visibility and style.</summary>
        private readonly Dictionary<int, int> _instanceID2SensorIndexMappingTable = new();
        /// <summary>Retains the renderer state for each of the thirty-four physical sensor areas.</summary>
        private readonly SensorRenderer?[] _sensorRenderers = new SensorRenderer?[34];
        /// <summary>Tracks the global visibility state to avoid redundant renderer writes.</summary>
        private bool _sensorsVisible;
        /// <summary>Reuses vertex storage when the original sensor layout must be measured.</summary>
        private readonly List<Vector3> _sourceVertices = new();
        /// <summary>Contains the full input areas in world space, independently of the selected style.</summary>
        private Bounds _sourceBounds;
        /// <summary>Identifies the root transform used for the cached source bounds.</summary>
        private Matrix4x4 _sourceRootMatrix;
        /// <summary>Indicates whether a complete source layout has been measured.</summary>
        private bool _hasSourceBounds;
        /// <summary>Leaves world positions unchanged on the main screen.</summary>
        private static readonly Vector4 s_identityDisplayTransform = new(1f, 1f, 0f, 0f);

        protected override void Awake()
        {
            base.Awake();
            Majdata<DummyTouchPanelRenderer>.SetAsSingleton(this);
            foreach (var (index, child) in Transform.ToEnumerable().WithIndex())
            {
                var collider = child.GetComponent<MeshCollider>();
                var renderer = child.GetComponent<MeshRenderer>();
                var filter = child.GetComponent<MeshFilter>();

                renderer.sortingLayerName = "Debug";
                renderer.sortingOrder = short.MaxValue;
                // Keep the baked border and the original full-area collider mesh; neither is cloned.
                _sensorRenderers[index] = new SensorRenderer(filter, renderer, collider, _sharedInstancedMaterial);
                _instanceID2SensorIndexMappingTable[collider.GetInstanceID()] = index;
            }
        }

        /// <summary>Applies visibility, selected geometry, opacity, and the current touch state.</summary>
        internal void OnPreUpdate()
        {
            var settings = MajEnv.Settings.Debug;
            var visible = settings.DisplaySensor;
            if (!visible)
            {
                SetSensorsVisible(false);
                return;
            }
            if (settings.SensorPosition == SensorDebugPosition.MainScreen)
            {
                SetSensorsVisible(true);
            }

            var style = settings.SensorStyle;
            var opacity = Math.Clamp(settings.SensorOpacity, 10, 90) / 100f;
            var idleColor = new Color(1, 0, 0, opacity);
            var activeColor = new Color(0, 1, 0, opacity);
            if (style == SensorDebugStyle.Brightness)
            {
                // The old Editor visualization used full areas with black alpha 0.1 / 0.4.
                // At the default 20% this restores those values on every platform.
                idleColor = new Color(0, 0, 0, opacity * 0.5f);
                activeColor = new Color(0, 0, 0, Math.Min(1f, opacity * 2f));
            }
            else if (style == SensorDebugStyle.Classic)
            {
                // Keep the requested RGB values independent of the shared opacity setting.
                idleColor = new Color(0f, 13f / 255f, 105f / 255f, opacity);
                activeColor = new Color(93f / 255f, 13f / 255f, 6f / 255f, opacity);
            }

            var rawData = InputManager.TouchPanelRawData;
            var sensorCount = Math.Min(rawData.Length, _sensorRenderers.Length);
            for (var i = 0; i < sensorCount; i++)
            {
                var sensor = _sensorRenderers[i];
                sensor?.SetStyle(style);
                sensor?.SetColor(rawData[i] ? activeColor : idleColor);
            }
        }

        private void LateUpdate()
        {
            // Camera, Canvas layout, and SubScreenEdge are updated at orders 100 through 175.
            // Only visual vertices and their culling bounds move; input colliders never move.
            var settings = MajEnv.Settings.Debug;
            if (!settings.DisplaySensor)
            {
                SetSensorsVisible(false);
                return;
            }
            var secondary = settings.SensorPosition == SensorDebugPosition.SubScreen;
            var displayTransform = s_identityDisplayTransform;
            if (secondary && !TryGetSecondaryTransform(out displayTransform))
            {
                SetSensorsVisible(false);
                return;
            }
            SetSensorsVisible(true);
            foreach (var sensor in _sensorRenderers)
            {
                sensor?.SetDisplayTransform(displayTransform, secondary);
            }
        }

        /// <summary>Changes draw visibility without disabling colliders or their owning objects.</summary>
        /// <param name="visible">Whether all sensor renderers should submit geometry.</param>
        private void SetSensorsVisible(bool visible)
        {
            if (visible == _sensorsVisible)
            {
                return;
            }
            foreach (var sensor in _sensorRenderers)
            {
                sensor?.SetVisible(visible);
            }
            _sensorsVisible = visible;
        }

        /// <summary>Fits the full sensor layout to the secondary display height without changing its aspect ratio.</summary>
        /// <param name="displayTransform">The world XY scale and offset, or identity before valid layout is available.</param>
        /// <returns>Whether valid secondary edges and source geometry are available.</returns>
        private bool TryGetSecondaryTransform(out Vector4 displayTransform)
        {
            displayTransform = s_identityDisplayTransform;
            var edge = InputManager.SubScreenEdge;
            if (!IsFinite(edge) || edge.z <= edge.x || edge.y <= edge.w)
            {
                return false;
            }
            var matrix = Transform.localToWorldMatrix;
            if (!_hasSourceBounds || matrix != _sourceRootMatrix)
            {
                var initialized = false;
                var bounds = default(Bounds);
                foreach (var sensor in _sensorRenderers)
                {
                    sensor?.EncapsulateAreaBounds(ref bounds, ref initialized, _sourceVertices);
                }
                _sourceBounds = bounds;
                _sourceRootMatrix = matrix;
                _hasSourceBounds = initialized;
            }
            if (!_hasSourceBounds || _sourceBounds.size.y <= 0.0001f)
            {
                return false;
            }
            var scale = (edge.y - edge.w) / _sourceBounds.size.y;
            displayTransform = new Vector4(scale, scale,
                (edge.x + edge.z) * 0.5f - _sourceBounds.center.x * scale,
                (edge.y + edge.w) * 0.5f - _sourceBounds.center.y * scale);
            return IsFinite(displayTransform);
        }

        /// <summary>Rejects incomplete or non-finite layout data before it reaches rendering and culling.</summary>
        /// <param name="value">The layout vector to validate.</param>
        /// <returns>Whether all four components are finite.</returns>
        private static bool IsFinite(Vector4 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z) &&
                !float.IsNaN(value.w) && !float.IsInfinity(value.w);
        }

        /// <summary>Conservatively transforms a local box without allocating corner arrays.</summary>
        /// <param name="bounds">The source local bounds.</param>
        /// <param name="matrix">The local-to-world matrix.</param>
        /// <returns>The enclosing world-space box.</returns>
        private static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
        {
            var x = matrix.MultiplyVector(new Vector3(bounds.extents.x, 0, 0));
            var y = matrix.MultiplyVector(new Vector3(0, bounds.extents.y, 0));
            var z = matrix.MultiplyVector(new Vector3(0, 0, bounds.extents.z));
            var extents = new Vector3(
                Math.Abs(x.x) + Math.Abs(y.x) + Math.Abs(z.x),
                Math.Abs(x.y) + Math.Abs(y.y) + Math.Abs(z.y),
                Math.Abs(x.z) + Math.Abs(y.z) + Math.Abs(z.z));
            return new Bounds(matrix.MultiplyPoint3x4(bounds.center), extents * 2f);
        }

        /// <summary>Retains shared geometry and caches per-renderer state without allocating each frame.</summary>
        private sealed class SensorRenderer
        {
            /// <summary>Receives the geometry selected for rendering only.</summary>
            private readonly MeshFilter _meshFilter;
            /// <summary>Controls visibility without disabling input or the owning GameObject.</summary>
            private readonly MeshRenderer _meshRenderer;
            /// <summary>References the baked inward border used by the red/green style.</summary>
            private readonly Mesh? _borderMesh;
            /// <summary>References the original full-area collider mesh used by the brightness style.</summary>
            private readonly Mesh? _areaMesh;
            /// <summary>Retains the property block used for renderer-local colors.</summary>
            private readonly MaterialPropertyBlock _propBlock = new();
            /// <summary>Records the previously submitted color to skip unchanged writes.</summary>
            private Color _lastColor;
            /// <summary>Distinguishes an untouched property block from a previously submitted color.</summary>
            private bool _hasColor;
            /// <summary>Retains the last shader scale and offset to avoid stable-frame property writes.</summary>
            private Vector4 _displayTransform;
            /// <summary>Distinguishes untouched display properties from an explicit identity transform.</summary>
            private bool _hasDisplayTransform;
            /// <summary>Tracks whether culling uses an explicit world-space override.</summary>
            private bool _hasCustomBounds;
            /// <summary>Identifies the mesh used for the last culling-bounds calculation.</summary>
            private Mesh? _boundsMesh;
            /// <summary>Identifies the object transform used for the last culling-bounds calculation.</summary>
            private Matrix4x4 _boundsMatrix;
            /// <summary>Records the applied style; construction uses the existing border mesh.</summary>
            private SensorDebugStyle _style = SensorDebugStyle.RedGreen;
            /// <summary>Identifies the common material color property.</summary>
            private static readonly int ColorPropId = Shader.PropertyToID("_BaseColor");
            /// <summary>Identifies visual-only world XY remapping in the sensor shader.</summary>
            private static readonly int s_displayTransformPropId = Shader.PropertyToID("_SensorDisplayTransform");

            /// <summary>Retains geometry and starts hidden until the display setting is applied.</summary>
            /// <param name="meshFilter">The filter containing the baked inward border.</param>
            /// <param name="meshRenderer">The renderer used for debug visualization.</param>
            /// <param name="meshCollider">The unchanged full-area input collider.</param>
            /// <param name="sharedMaterial">The transparent material shared by all areas.</param>
            public SensorRenderer(MeshFilter meshFilter, MeshRenderer meshRenderer, MeshCollider meshCollider, Material sharedMaterial)
            {
                _meshFilter = meshFilter;
                _meshRenderer = meshRenderer;
                _borderMesh = meshFilter.sharedMesh;
                _areaMesh = meshCollider.sharedMesh;
                _meshRenderer.sharedMaterial = sharedMaterial;
                _meshRenderer.enabled = false;
            }

            /// <summary>Enables or stops draw submission for this visualization.</summary>
            /// <param name="visible">Whether this area should render.</param>
            public void SetVisible(bool visible)
            {
                _meshRenderer.enabled = visible;
            }

            /// <summary>Selects shared full-area or border geometry without changing collision.</summary>
            /// <param name="style">The visualization style selected in the debug menu.</param>
            public void SetStyle(SensorDebugStyle style)
            {
                if (_style == style)
                {
                    return;
                }
                _meshFilter.sharedMesh = style == SensorDebugStyle.RedGreen ? _borderMesh : _areaMesh;
                _style = style;
            }

            /// <summary>Accumulates unchanged full-area vertices for the shared layout measurement.</summary>
            /// <param name="bounds">The aggregate world-space bounds.</param>
            /// <param name="initialized">Whether any previous area supplied a point.</param>
            /// <param name="vertices">Reusable vertex storage for readable meshes.</param>
            public void EncapsulateAreaBounds(ref Bounds bounds, ref bool initialized, List<Vector3> vertices)
            {
                if (_areaMesh == null)
                {
                    return;
                }
                var matrix = _meshFilter.transform.localToWorldMatrix;
                if (_areaMesh.isReadable)
                {
                    _areaMesh.GetVertices(vertices);
                    foreach (var vertex in vertices)
                    {
                        var world = matrix.MultiplyPoint3x4(vertex);
                        if (!initialized)
                        {
                            bounds = new Bounds(world, Vector3.zero);
                            initialized = true;
                        }
                        bounds.Encapsulate(world);
                    }
                }
                else
                {
                    // Imported project meshes are readable; retain a conservative fallback for other assets.
                    var areaBounds = TransformBounds(_areaMesh.bounds, matrix);
                    if (!initialized)
                    {
                        bounds = areaBounds;
                        initialized = true;
                    }
                    bounds.Encapsulate(areaBounds);
                }
            }

            /// <summary>Applies the same visual remapping to GPU vertices and CPU culling bounds.</summary>
            /// <param name="displayTransform">World XY scale followed by XY translation.</param>
            /// <param name="secondary">Whether to override culling for secondary-display geometry.</param>
            public void SetDisplayTransform(Vector4 displayTransform, bool secondary)
            {
                var changed = !_hasDisplayTransform || _displayTransform != displayTransform;
                if (changed)
                {
                    _meshRenderer.GetPropertyBlock(_propBlock);
                    _propBlock.SetVector(s_displayTransformPropId, displayTransform);
                    _meshRenderer.SetPropertyBlock(_propBlock);
                    _displayTransform = displayTransform;
                    _hasDisplayTransform = true;
                }
                if (!secondary)
                {
                    if (_hasCustomBounds)
                    {
                        _meshRenderer.ResetBounds();
                        _hasCustomBounds = false;
                    }
                    return;
                }
                var mesh = _meshFilter.sharedMesh;
                var matrix = _meshFilter.transform.localToWorldMatrix;
                if (mesh == null || (_hasCustomBounds && !changed && mesh == _boundsMesh && matrix == _boundsMatrix))
                {
                    return;
                }
                var bounds = TransformBounds(mesh.bounds, matrix);
                bounds.center = new Vector3(
                    bounds.center.x * displayTransform.x + displayTransform.z,
                    bounds.center.y * displayTransform.y + displayTransform.w,
                    bounds.center.z);
                bounds.size = new Vector3(
                    bounds.size.x * Math.Abs(displayTransform.x),
                    bounds.size.y * Math.Abs(displayTransform.y), bounds.size.z);
                _meshRenderer.bounds = bounds;
                _hasCustomBounds = true;
                _boundsMesh = mesh;
                _boundsMatrix = matrix;
            }

            /// <summary>Updates the renderer-local color only when its value changes.</summary>
            /// <param name="color">The state color, including the requested opacity.</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetColor(Color color)
            {
                if (_hasColor && _lastColor == color)
                {
                    return;
                }
                _meshRenderer.GetPropertyBlock(_propBlock);
                _propBlock.SetColor(ColorPropId, color);
                _meshRenderer.SetPropertyBlock(_propBlock);
                _lastColor = color;
                _hasColor = true;
            }
        }
    }
}
