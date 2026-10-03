using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
#if UNITY_STANDALONE_OSX
using UnityEditor.iOS.Xcode;
#endif

namespace Videolabs.VLCUnity.Editor
{
    // LibVLC resolves decoder modules below its own plugins directory. Preserve
    // that hierarchy instead of relying on Unity's native importer placement.
    public sealed class CopyLibVLCFiles : IPostprocessBuildWithReport
    {
        public int callbackOrder => 50;

        public void OnPostprocessBuild(BuildReport report)
        {
            var output = report.summary.outputPath;
            string source, destination;
            switch (report.summary.platform)
            {
                case BuildTarget.StandaloneWindows64:
                    source = VlcPluginLayout.FullPath("Windows/x86_64");
                    destination = Path.Combine(Path.GetDirectoryName(output), Path.GetFileNameWithoutExtension(output) + "_Data", "Plugins", "x86_64");
                    break;
                case BuildTarget.StandaloneLinux64:
                    source = VlcPluginLayout.FullPath("Linux/x86_64");
                    destination = Path.Combine(Path.GetDirectoryName(output), Path.GetFileNameWithoutExtension(output) + "_Data", "Plugins", "x86_64");
                    break;
                case BuildTarget.StandaloneOSX:
                    source = VlcPluginLayout.FullPath(VlcPluginLayout.MacDirectory);
                    if (!output.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                    {
#if UNITY_STANDALONE_OSX
                        StageMacXcodeProject(source, output);
                        return;
#else
                        throw new BuildFailedException("VLCUnity: switch the active build target to macOS before exporting a macOS Xcode project.");
#endif
                    }
                    destination = Path.Combine(output, "Contents", "Plugins");
                    break;
                default:
                    return; // Android native libraries and iOS archives use PluginImporter.
            }
            CopyDirectory(source, destination);
        }

        internal static void CopyDirectory(string source, string destination)
        {
            if (!Directory.Exists(source))
                throw new BuildFailedException("VLCUnity: native bundle disappeared during the build: " + source);
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source))
            {
                var name = Path.GetFileName(file);
                if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                    name == "LibVLCSharp.dll") continue;
                File.Copy(file, Path.Combine(destination, name), true);
            }
            foreach (var directory in Directory.GetDirectories(source))
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }

#if UNITY_STANDALONE_OSX
        private static void StageMacXcodeProject(string source, string output)
        {
            // Use the actual Xcode project API; do not rewrite VALID_ARCHS or
            // architecture-specific dylib paths with a regular expression.
            foreach (var projectPath in Directory.GetFiles(output, "project.pbxproj", SearchOption.AllDirectories))
            {
                var project = new PBXProject();
                project.ReadFromFile(projectPath);
                var target = project.TargetGuidByName("MacPlayerExec");
                if (string.IsNullOrEmpty(target)) continue;
                var projectRoot = Directory.GetParent(Path.GetDirectoryName(projectPath)).FullName;
                CopyDirectory(source, Path.Combine(projectRoot, "VLCUnityRuntime"));
                const string phaseName = "Copy VLC runtime modules and data";
                const string script = "set -eu\n/usr/bin/ditto \"$SRCROOT/VLCUnityRuntime\" \"$TARGET_BUILD_DIR/$CONTENTS_FOLDER_PATH/Plugins\"\n";
                if (string.IsNullOrEmpty(project.GetShellScriptBuildPhaseForTarget(target, phaseName, "/bin/sh", script)))
                    project.AddShellScriptBuildPhaseBeforeTargetPostprocess(target, phaseName, "/bin/sh", script);
                project.WriteToFile(projectPath);
                return;
            }
            throw new BuildFailedException("VLCUnity: could not locate the MacPlayerExec Xcode application target for LibVLC runtime staging.");
        }
#endif
    }
}
