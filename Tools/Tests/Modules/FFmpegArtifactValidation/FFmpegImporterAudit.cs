#nullable enable

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MajdataPlay.Tests.FFmpeg
{
    /// <summary>
    /// Validates the Windows FFmpeg importer selections inside an isolated Unity project.
    /// </summary>
    public static class FFmpegImporterAudit
    {
        /// <summary>
        /// Checks all three installed Windows architectures and writes an audit report.
        /// </summary>
        /// <exception cref="InvalidOperationException">An importer or the Unity version is incorrect.</exception>
        public static void Run()
        {
            if (Application.unityVersion != "6000.3.17f1")
            {
                throw new InvalidOperationException("The importer audit requires Unity 6000.3.17f1.");
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var count = 0;
            var libraries = new[]
            {
                "avcodec-63.dll", "avdevice-63.dll", "avfilter-12.dll", "avformat-63.dll",
                "avutil-61.dll", "swresample-7.dll", "swscale-10.dll", "FFmpegUnityBridge.dll"
            };
            var architectures = new[]
            {
                (Directory: "x86", Cpu: "x86", Target: BuildTarget.StandaloneWindows, Editor: false),
                (Directory: "x86_64", Cpu: "x86_64", Target: BuildTarget.StandaloneWindows64, Editor: true),
                (Directory: "arm64", Cpu: "ARM64", Target: BuildTarget.StandaloneWindows64, Editor: false)
            };
            foreach (var architecture in architectures)
            {
                foreach (var library in libraries)
                {
                    var path = $"Assets/FFmpeg/Native/Windows/{architecture.Directory}/{library}";
                    Validate(path, architecture.Cpu, architecture.Target, architecture.Editor);
                    count++;
                }
            }
            var report = $"PASS: Unity {Application.unityVersion}; {count} Windows x86/x64/ARM64 native importers; ARM64 disabled for the x64 Editor.";
            File.WriteAllText(Path.Combine(Application.dataPath, "..", "importer-audit.txt"), report);
            Debug.Log(report);
        }

        /// <summary>
        /// Ensures a native asset is restricted to its intended Player and Editor architectures.
        /// </summary>
        /// <param name="path">The native asset path within the isolated project.</param>
        /// <param name="cpu">The required Unity Player CPU selection.</param>
        /// <param name="target">The required Unity Player platform.</param>
        /// <param name="editorEnabled">Whether the Windows x64 Editor may load the asset.</param>
        /// <exception cref="InvalidOperationException">The asset is missing or has incorrect importer settings.</exception>
        private static void Validate(string path, string cpu, BuildTarget target, bool editorEnabled)
        {
            var importer = AssetImporter.GetAtPath(path) as PluginImporter;
            if (importer == null || importer.GetCompatibleWithAnyPlatform() ||
                !importer.GetCompatibleWithPlatform(target) || importer.GetPlatformData(target, "CPU") != cpu ||
                importer.GetCompatibleWithEditor() != editorEnabled)
            {
                throw new InvalidOperationException(
                    $"Incorrect native platform/CPU importer: {path}; expected {target}/{cpu}. " +
                    $"Actual: Any={importer?.GetCompatibleWithAnyPlatform()}, " +
                    $"Target={importer?.GetCompatibleWithPlatform(target)}, " +
                    $"CPU={importer?.GetPlatformData(target, "CPU")}, " +
                    $"Editor={importer?.GetCompatibleWithEditor()}.");
            }
            if (editorEnabled && (importer.GetEditorData("CPU") != "x86_64" || importer.GetEditorData("OS") != "Windows"))
            {
                throw new InvalidOperationException($"Incorrect x64 Editor importer: {path}.");
            }
            var otherTarget = target == BuildTarget.StandaloneWindows ? BuildTarget.StandaloneWindows64 : BuildTarget.StandaloneWindows;
            if (importer.GetCompatibleWithPlatform(otherTarget) || importer.GetCompatibleWithPlatform(BuildTarget.Android) ||
                importer.GetCompatibleWithPlatform(BuildTarget.StandaloneLinux64) || importer.GetCompatibleWithPlatform(BuildTarget.iOS))
            {
                throw new InvalidOperationException($"Native importer enables an unrelated platform: {path}.");
            }
        }
    }
}
