#if UNITY_EDITOR
// Editor 目录通过 asmref 并入 Runtime 程序集，保持 MajCameraRenderer 的 partial 是同一类型。
// Player 排除依赖整文件的 UNITY_EDITOR 守卫，而不是仅依赖目录名。
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MajdataPlay.Rendering
{
    internal sealed partial class MajCameraRenderer
    {
        partial void PrepareForSceneView()
        {
            if (_camera.cameraType == CameraType.SceneView)
            {
                // Scene View 的 World Space UI 需要在剔除之前发射到相机。
                ScriptableRenderContext.EmitWorldGeometryForSceneView(_camera);
            }
        }

        partial void DrawEditorGizmos()
        {
            if (Handles.ShouldRenderGizmos())
            {
                _context.DrawGizmos(_camera, GizmoSubset.PreImageEffects);
                _context.DrawGizmos(_camera, GizmoSubset.PostImageEffects);
            }
        }

        partial void DisableNativePassForEditorGizmos(ref bool enabled)
        {
            // Scene/Game View 的 Gizmos 可能读取场景深度，不能在它们之前丢弃附件。
            if (Handles.ShouldRenderGizmos()) enabled = false;
        }
    }
}
#endif
