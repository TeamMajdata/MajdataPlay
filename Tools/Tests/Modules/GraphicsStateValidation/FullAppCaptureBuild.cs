#nullable enable
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.Rendering;

namespace MajdataPlay.Editor
{
    /// <summary>Builds the isolated full-app Android graphics capture player.</summary>
    public static class AndroidFullCaptureBuild
    {
        /// <summary>Builds a debug-signed, isolated ARM64 capture APK for the requested graphics API.</summary>
        /// <exception cref="ArgumentException">The output path or graphics API argument is invalid.</exception>
        /// <exception cref="InvalidOperationException">Unity failed to build the capture player.</exception>
        public static void Build()
        {
            var output = GetArgument("-captureOutput");
            if (string.IsNullOrWhiteSpace(output))
            {
                throw new ArgumentException("Pass an absolute APK path using -captureOutput.");
            }
            var apiName = GetArgument("-captureApi") ?? nameof(GraphicsDeviceType.Vulkan);
            if (!Enum.TryParse<GraphicsDeviceType>(apiName, out var api)
                || (api != GraphicsDeviceType.Vulkan && api != GraphicsDeviceType.OpenGLES3))
            {
                throw new ArgumentException("-captureApi must be Vulkan or OpenGLES3.");
            }
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "net.majdata.majdataplay.graphicscapture");
            var mode = GetArgument("-captureMode") ?? "Matrix";
            if (mode != "Matrix" && mode != "Single" && mode != "Legacy")
            {
                throw new ArgumentException("-captureMode must be Matrix, Legacy or Single.");
            }
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.buildApkPerCpuArchitecture = false;
            PlayerSettings.Android.splitApplicationBinary = false;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel36;
            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { api });
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            ConfigureCaptureNativePlugins();
            AssetDatabase.SaveAssets();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                target = BuildTarget.Android,
                locationPathName = output,
                extraScriptingDefines = mode switch
                {
                    "Matrix" => new[] { "GSC_FULL_CAPTURE_MATRIX" },
                    "Legacy" => new[] { "GSC_FULL_CAPTURE_LEGACY" },
                    _ => Array.Empty<string>()
                },
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException($"Capture build failed: {report.summary.result}.");
            }
        }

        /// <summary>Includes the existing ARM64 Skia libraries copied into this isolated capture project.</summary>
        /// <exception cref="InvalidOperationException">A staged native plugin could not be imported.</exception>
        private static void ConfigureCaptureNativePlugins()
        {
            AssetDatabase.Refresh();
            foreach (var name in new[] { "libSkiaSharp.so", "libHarfBuzzSharp.so" })
            {
                var path = $"Assets/Plugins/Android/arm64-v8a/{name}";
                if (AssetImporter.GetAtPath(path) is not PluginImporter importer)
                {
                    throw new InvalidOperationException($"Native capture dependency missing: {path}.");
                }
                importer.SetCompatibleWithAnyPlatform(false);
                importer.SetCompatibleWithEditor(false);
                importer.SetCompatibleWithPlatform(BuildTarget.Android, true);
                importer.SetPlatformData(BuildTarget.Android, "CPU", "ARM64");
                importer.SaveAndReimport();
            }
        }

        /// <summary>Reads a value following a named Unity command-line argument.</summary>
        /// <param name="name">The command-line option to find.</param>
        /// <returns>The option value, or null if it was not supplied.</returns>
        private static string? GetArgument(string name)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (arguments[index] == name)
                {
                    return arguments[index + 1];
                }
            }
            return null;
        }
    }
}
