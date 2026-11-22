#nullable enable
using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MajdataPlay.Validation.VulkanTexture
{
    /// <summary>Builds only the isolated Windows x64 Mono texture probe.</summary>
    public static class ProbeBuild
    {
        /// <summary>Creates an empty camera scene and builds a standalone probe without running it.</summary>
        /// <exception cref="InvalidOperationException">The editor version, output argument, or build result is invalid.</exception>
        public static void Run()
        {
            if (Application.unityVersion != "6000.3.17f1")
            {
                throw new InvalidOperationException("Use Unity 6000.3.17f1.");
            }

            var arguments = Environment.GetCommandLineArgs();
            string? output = null;
            for (var i = 0; i + 1 < arguments.Length; i++)
            {
                if (arguments[i] == "-probe-build-output")
                {
                    output = arguments[i + 1];
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                throw new InvalidOperationException("Specify -probe-build-output with an absolute executable path.");
            }

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Standalone, ManagedStrippingLevel.Disabled);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,
                new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.Direct3D12 });
            PlayerSettings.graphicsJobs = true;
            PlayerSettings.colorSpace = ColorSpace.Gamma;
            PlayerSettings.companyName = "MajdataPlay";
            PlayerSettings.productName = "Vulkan Texture Validation";
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultScreenWidth = 1080;
            PlayerSettings.defaultScreenHeight = 1920;
            PlayerSettings.runInBackground = true;
            GraphicsSettings.defaultRenderPipeline = null;
            for (var level = 0; level < QualitySettings.names.Length; level++)
            {
                QualitySettings.SetQualityLevel(level, false);
                QualitySettings.renderPipeline = null;
                QualitySettings.antiAliasing = 0;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.orthographic = true;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.allowHDR = false;
            camera.allowMSAA = false;
            EditorSceneManager.SaveScene(scene, "Assets/Probe.unity");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Probe.unity" },
                target = BuildTarget.StandaloneWindows64,
                locationPathName = output,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException("Texture probe build failed: " + report.summary.result);
            }

            Debug.Log("[TextureProbe] BUILD DONE " + output + "; Player was not launched.");
        }
    }
}
