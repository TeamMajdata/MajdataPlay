#nullable enable
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MajdataPlay.Tests
{
    /// <summary>Builds a standalone SAF/JNI IL2CPP test application without changing the main project.</summary>
    public static class AndroidStorageDeviceBuild
    {
        /// <summary>Configures only the isolated project and builds the selected native Android architectures.</summary>
        /// <exception cref="ArgumentException">The APK path or architecture is invalid.</exception>
        /// <exception cref="InvalidOperationException">The IL2CPP Player build fails.</exception>
        public static void Run()
        {
            try
            {
                var output = Argument("-storageApk") ?? throw new ArgumentException("Specify -storageApk.");
                var architecture = Argument("-storageArchitecture") ?? "Both";
                var architectures = architecture switch
                {
                    "ARM64" => AndroidArchitecture.ARM64,
                    "ARMv7" => AndroidArchitecture.ARMv7,
                    "Both" => AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7,
                    _ => throw new ArgumentException("Unknown architecture " + architecture)
                };
                PlayerSettings.companyName = "MajdataPlay Tests";
                PlayerSettings.productName = "SAF JNI Validation";
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "net.majdata.storagevalidation");
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
                PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Android, ApiCompatibilityLevel.NET_Standard);
                PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.High);
                PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Android, Il2CppCompilerConfiguration.Release);
                PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
                PlayerSettings.Android.targetArchitectures = architectures;
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
                PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel35;
                PlayerSettings.Android.useCustomKeystore = false;
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
                EditorUserBuildSettings.buildAppBundle = false;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
                System.IO.Directory.CreateDirectory("Assets/Validation");
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                const string ScenePath = "Assets/Validation/StorageValidation.unity";
                EditorSceneManager.SaveScene(scene, ScenePath);
                var apk = Path.GetFullPath(output);
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(apk)!);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = apk,
                    target = BuildTarget.Android,
                    options = BuildOptions.Development | BuildOptions.StrictMode
                });
                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new InvalidOperationException("Isolated Android IL2CPP build failed: " + report.summary.result);
                }
                Debug.Log("FILE_SYSTEM_DEVICE_IL2CPP_BUILD_PASSED: " + architecture + " (build only; device execution is separate).");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("FILE_SYSTEM_DEVICE_IL2CPP_BUILD_FAILED: " + exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Reads one explicit command-line setting.</summary>
        /// <param name="name">The option name whose following value is required.</param>
        /// <returns>The following value, or null when the option is absent.</returns>
        private static string? Argument(string name)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < arguments.Length; i++)
            {
                if (arguments[i] == name)
                {
                    return arguments[i + 1];
                }
            }
            return null;
        }
    }
}
