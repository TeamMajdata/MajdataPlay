using Live2D.Cubism.Rendering;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MajdataPlay.Editor
{
    [BuildCallbackVersion(1)]
    class CubismPickingBuildProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // Unity also invokes this callback when entering Editor Play Mode.
            // Strip picking geometry only from the scene copies used by player builds,
            // including Development builds.
            if (!BuildPipeline.isBuildingPlayer)
            {
                return;
            }

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var renderer in root.GetComponentsInChildren<CubismRenderer>(true))
                {
                    if (renderer.TryGetComponent<MeshFilter>(out var meshFilter))
                    {
                        // Cubism draws its own Mesh through a command buffer. This
                        // MeshFilter exists for Scene View picking and otherwise submits
                        // an empty transparent draw. Keep MeshRenderer: Cubism uses its
                        // enabled state, sorting settings and material property block.
                        Object.DestroyImmediate(meshFilter);
                    }
                }
            }
        }
    }
}
