using System;
using System.Collections.Generic;
using Live2D.Cubism.Core;
using Live2D.Cubism.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace MajdataPlay.Rendering.Live2D
{
    /// <summary>
    /// Cubism 对 MRP 的可选适配器。本程序集反向引用 MRP；MRP 本体不引用 SDK 或 RP 包。
    /// 仅将普通模型直接 DrawMesh 到调用者已绑定的目标，不创建模型颜色、临时合成或遮罩 RT。
    /// 当前项目的 xxlb 已审计：64 个 Normal/Over drawable，无遮罩、无 Offscreen。
    /// 后续若引入高级混合、遮罩或离屏模型，需另行适配其绘制流程。
    /// </summary>
    public sealed class MajCubismRenderExtension : IMajCameraRenderExtension
    {
        private struct RendererState
        {
            internal Renderer Renderer;
            internal bool ForceRenderingOff;
        }

        private readonly CubismRenderingExecutor _executor = new CubismRenderingExecutor();
        private readonly CubismRenderingContext _context = new CubismRenderingContext();
        private readonly List<CubismRenderController> _visible = new List<CubismRenderController>();
        private readonly List<RendererState> _suppressed = new List<RendererState>();
        private readonly Dictionary<Renderer, bool> _originalForceRenderingOff = new Dictionary<Renderer, bool>();
        private readonly Plane[] _planes = new Plane[6];
        private CubismRenderController[] _controllers = Array.Empty<CubismRenderController>();
        private CubismRenderControllerGroup.RenderControllerGroupData[] _groups =
            Array.Empty<CubismRenderControllerGroup.RenderControllerGroupData>();
        public bool RequiresIntermediateTarget => false;

        /// <summary>
        /// 在 Cull 前调用。用相机层、层级激活状态与 drawable bounds 过滤，
        /// 隐藏或其它相机的模型不参与绘制；模型的排序和自动 Renderer 抑制行为保持不变。
        /// </summary>
        public bool Prepare(Camera camera)
        {
            EndCamera();
            if (!camera || (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView))
                return false;

            _visible.Clear();
            var registered = CubismRenderControllerGroup.GetInstance().RenderControllers;
            if (registered.Length == 0) return false;
            GeometryUtility.CalculateFrustumPlanes(camera, _planes);
            for (int i = 0; i < registered.Length; i++)
            {
                var controller = registered[i];
                if (!controller || !controller.isActiveAndEnabled || !controller.gameObject.activeInHierarchy
                    || controller.Renderers == null || !IsVisible(controller, camera)) continue;
                _visible.Add(controller);
            }

            if (_visible.Count == 0) return false;
            UpdateGroups();
            _context.Camera = camera;
            _context.RenderControllers = _controllers;
            _context.RenderControllerGroupDaraArray = _groups;
            _context.OriginalForceRenderingOff = _originalForceRenderingOff;

            // Picking proxy 的材质和实际 DrawMaterial 不同。只抑制自动 RendererList 绘制，
            // 不改 enabled、材质、layer 或 SDK 的可见性；显式 DrawMesh 仍正常执行。
            for (int i = 0; i < _controllers.Length; i++)
            {
                var renderers = _controllers[i].Renderers;
                for (int j = 0; j < renderers.Length; j++)
                {
                    var renderer = renderers[j] ? renderers[j].MeshRenderer : null;
                    if (!renderer || _originalForceRenderingOff.ContainsKey(renderer)) continue;
                    _originalForceRenderingOff.Add(renderer, renderer.forceRenderingOff);
                    _suppressed.Add(new RendererState { Renderer = renderer, ForceRenderingOff = renderer.forceRenderingOff });
                    renderer.forceRenderingOff = true;
                }
            }
            return true;
        }

        private bool IsVisible(CubismRenderController controller, Camera camera)
        {
            var renderers = controller.Renderers;
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (!renderer || renderer.DrawObjectType != CubismModelTypes.DrawObjectType.Drawable
                    || !renderer.gameObject.activeInHierarchy || !renderer.MeshRenderer
                    || !renderer.MeshRenderer.enabled || renderer.MeshRenderer.forceRenderingOff
                    || renderer.RenderingOpacity <= 0f || (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0) continue;
                // Runtime 可以没有用于 Editor picking 的 MeshFilter，不能依赖 proxy 的零 bounds。
                // 同时包住实际 Mesh 经世界 Transform 和 SDK 自定义变换后的 AABB；无法确认时保守可见。
                if (!TryGetDrawableBounds(renderer, controller, out var bounds)
                    || GeometryUtility.TestPlanesAABB(_planes, bounds)) return true;
            }
            return false;
        }

        private static bool TryGetDrawableBounds(CubismRenderer renderer, CubismRenderController controller, out Bounds bounds)
        {
            var mesh = renderer.Mesh;
            bounds = default;
            if (!mesh || mesh.bounds.size.sqrMagnitude <= 1e-12f) return false;
            var local = mesh.bounds;
            bounds = TransformBounds(local, renderer.transform.localToWorldMatrix);

            // DrawMesh 使用 identity，SDK Shader 的 ApplyTransform 另按这两个 local Transform 运算。
            var modelTransform = controller.transform;
            var drawableTransform = renderer.transform;
            var offset = modelTransform.localPosition + drawableTransform.localPosition;
            var rotation = modelTransform.localRotation * drawableTransform.localRotation;
            var scale = new Vector3(modelTransform.localScale.x * drawableTransform.localScale.x,
                modelTransform.localScale.y * drawableTransform.localScale.y, 1f);
            bounds.Encapsulate(TransformBounds(local, Matrix4x4.TRS(offset, rotation, scale)));
            var proxy = renderer.MeshRenderer.bounds;
            if (proxy.size.sqrMagnitude > 1e-12f) bounds.Encapsulate(proxy);
            return IsFinite(bounds.center) && IsFinite(bounds.extents);
        }

        private static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
        {
            var extents = bounds.extents;
            var x = matrix.MultiplyVector(new Vector3(extents.x, 0, 0));
            var y = matrix.MultiplyVector(new Vector3(0, extents.y, 0));
            var z = matrix.MultiplyVector(new Vector3(0, 0, extents.z));
            var worldExtents = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return new Bounds(matrix.MultiplyPoint3x4(bounds.center), worldExtents * 2f);
        }

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z)
            && !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);

        private void UpdateGroups()
        {
            bool changed = _controllers.Length != _visible.Count;
            for (int i = 0; !changed && i < _controllers.Length; i++)
                changed = _controllers[i] != _visible[i];
            if (!changed)
            {
                // GroupedSortingIndex 可在运行时修改，即使 controller 数组本身未变化。
                for (int g = 0; !changed && g < _groups.Length; g++)
                    for (int i = 0; !changed && i < _groups[g].Controllers.Length; i++)
                        changed = _groups[g].Controllers[i].GroupedSortingIndex != _groups[g].SortingGroupIndex;
            }
            if (!changed) return;

            _controllers = _visible.ToArray();
            var groups = new SortedDictionary<int, List<CubismRenderController>>();
            for (int i = 0; i < _controllers.Length; i++)
            {
                int index = _controllers[i].GroupedSortingIndex;
                if (!groups.TryGetValue(index, out var group)) groups[index] = group = new List<CubismRenderController>();
                group.Add(_controllers[i]);
            }
            _groups = new CubismRenderControllerGroup.RenderControllerGroupData[groups.Count];
            int target = 0;
            foreach (var pair in groups)
                _groups[target++] = new CubismRenderControllerGroup.RenderControllerGroupData
                {
                    SortingGroupIndex = pair.Key, Controllers = pair.Value.ToArray()
                };
        }

        public void Render(CommandBuffer buffer, Camera camera, RenderTexture colorTarget,
            RenderTexture depthTarget, int width, int height)
        {
            if (_visible.Count == 0) return;
            _context.Width = width;
            _context.Height = height;
            // 不切目标、不复制颜色，直接加入 MRP 已绑定的场景 Pass。
            _executor.RenderSimple(buffer, _context);
        }

        /// <summary>调用者必须在 Submit 后 finally 调用；失败或剔除提前返回也要恢复。</summary>
        public void EndCamera()
        {
            for (int i = _suppressed.Count - 1; i >= 0; i--)
                if (_suppressed[i].Renderer) _suppressed[i].Renderer.forceRenderingOff = _suppressed[i].ForceRenderingOff;
            _suppressed.Clear();
            _originalForceRenderingOff.Clear();
        }

        public void Dispose()
        {
            EndCamera();
            _executor.Dispose();
        }
    }

    /// <summary>仅注册 factory；没有 Cubism 的相机不会生成附件或额外绘制。</summary>
    internal static class MajCubismRegistration
    {
        private static readonly Func<IMajCameraRenderExtension> Factory = () => new MajCubismRenderExtension();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register() => MajCameraRenderExtensions.Register(Factory);

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditor() => Register();
#endif
    }
}
