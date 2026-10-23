#nullable enable
using System;
using MajdataPlay.IO.Storage;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MajdataPlay.Tests.GraphicsStates
{
    /// <summary>Builds the isolated Android graphics-state probe.</summary>
    public static class GraphicsStateDeviceBuild
    {
        /// <summary>Builds a dedicated Vulkan IL2CPP application and exits the batch Editor.</summary>
        public static void Run()
        {
            try
            {
                PlayerSettings.companyName = "MajdataPlay Tests";
                PlayerSettings.productName = "Graphics State Validation";
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "net.majdata.graphicsstatevalidation");
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
                PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Android, ApiCompatibilityLevel.NET_Standard);
                PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.High);
                // Unity's empty-scene native stripper removes FontAsset support used by the development UI.
                PlayerSettings.stripEngineCode = false;
                PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Android, Il2CppCompilerConfiguration.Release);
                PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                PlayerSettings.Android.buildApkPerCpuArchitecture = false;
                PlayerSettings.Android.splitApplicationBinary = false;
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
                PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel35;
                PlayerSettings.Android.useCustomKeystore = false;
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
                EditorUserBuildSettings.buildAppBundle = false;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
                FileSystem.Directory.Create("Assets/Resources/Probe");
                var shaderNames = new[] { "Maj/Sprites/Default", "Maj/UI/Default", "Unlit/Color" };
                for (var i = 0; i < shaderNames.Length; i++)
                {
                    var shader = Shader.Find(shaderNames[i]) ?? throw new InvalidOperationException("Missing shader: " + shaderNames[i]);
                    var path = "Assets/Resources/Probe/Material" + i + ".mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null)
                    {
                        AssetDatabase.CreateAsset(new Material(shader), path);
                    }
                    else
                    {
                        material.shader = shader;
                        EditorUtility.SetDirty(material);
                    }
                }
                AssetDatabase.SaveAssets();
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, "Assets/Probe.unity");
                FileSystem.Directory.Create("Artifacts");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/Probe.unity" },
                    locationPathName = "Artifacts/GraphicsStates.apk",
                    target = BuildTarget.Android,
                    options = BuildOptions.Development | BuildOptions.StrictMode
                });
                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new InvalidOperationException("Android build failed: " + report.summary.result);
                }
                Debug.Log("GRAPHICS_STATE_BUILD_PASSED");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("GRAPHICS_STATE_BUILD_FAILED: " + exception);
                EditorApplication.Exit(1);
            }
        }
    }
}
