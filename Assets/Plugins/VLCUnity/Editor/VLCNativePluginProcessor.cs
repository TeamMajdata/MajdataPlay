using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEngine;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif

namespace Videolabs.VLCUnity.Editor
{
    internal static class VlcPluginLayout
    {
        internal const string Root = "Assets/Plugins/VLCUnity/Runtime/Plugins";
        internal const string SharedBinding = Root + "/LibVLCSharp.dll";
        internal const string IosBinding = Root + "/iOS/LibVLCSharp.dll";
        internal const string ManifestName = "vlc-unity-bundle.json";
        internal static string FullPath(string relative) => Path.GetFullPath(Root + "/" + relative);
        internal static string IosDirectory => PlayerSettings.iOS.sdkVersion == iOSSdkVersion.SimulatorSDK ? "iOS/Simulator" : "iOS/Device";
        internal static bool HasUniversalMac => File.Exists(FullPath("MacOS/universal/libVLCUnityPlugin.dylib"));
        internal static string MacDirectory
        {
            get
            {
                if (HasUniversalMac) return "MacOS/universal";
                var architecture = PlayerSettings.GetArchitecture(NamedBuildTarget.Standalone);
                return architecture == 1 ? "MacOS/ARM64" : architecture == 2 ? "MacOS/universal" : "MacOS/x86_64";
            }
        }

        [Serializable]
        internal sealed class IosBundleManifest
        {
            public int schemaVersion;
            public string staticPluginRegistration;
            public string[] forceLoadArchives;
            public string[] frameworks;
            public string[] libraries;
        }

        internal static IosBundleManifest ReadIosManifest()
        {
            var path = FullPath(IosDirectory + "/" + ManifestName);
            if (!File.Exists(path))
                throw new BuildFailedException("VLCUnity: missing iOS decoder bundle manifest: " + path +
                    ". Supply the matching LibVLC static module registration source, module/contrib archives and system dependencies; see Tools/VLCUnity/README.md.");
            IosBundleManifest manifest;
            try { manifest = JsonUtility.FromJson<IosBundleManifest>(File.ReadAllText(path)); }
            catch (Exception error) { throw new BuildFailedException("VLCUnity: invalid " + path + ": " + error.Message); }
            if (manifest == null || manifest.schemaVersion != 1 || string.IsNullOrWhiteSpace(manifest.staticPluginRegistration) ||
                manifest.forceLoadArchives == null || manifest.frameworks == null || manifest.libraries == null)
                throw new BuildFailedException("VLCUnity: " + path + " must declare schemaVersion 1, staticPluginRegistration, forceLoadArchives, frameworks and libraries.");
            return manifest;
        }

        internal static string BundleFile(string directory, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
                throw new BuildFailedException("VLCUnity: bundle file paths must be nonempty relative paths.");
            var root = FullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var resolved = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
            if (!resolved.StartsWith(root, Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new BuildFailedException("VLCUnity: bundle path escapes its platform directory: " + relative);
            return resolved;
        }
    }

    public sealed class VLCNativePluginProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => -200;
        private static readonly BuildTarget[] ManagedTargets =
        {
            BuildTarget.StandaloneWindows64, BuildTarget.StandaloneLinux64,
            BuildTarget.StandaloneOSX, BuildTarget.Android
        };
        private static readonly BuildTarget[] KnownTargets =
        {
            BuildTarget.StandaloneWindows, BuildTarget.StandaloneWindows64,
            BuildTarget.StandaloneLinux64, BuildTarget.StandaloneOSX,
            BuildTarget.Android, BuildTarget.iOS, BuildTarget.WebGL, BuildTarget.WSAPlayer
        };
        public void OnPreprocessBuild(BuildReport report) => ConfigureAll();

        [MenuItem("Tools/VLCUnity/Configure platform plugins")]
        public static void ConfigureAll()
        {
            foreach (var importer in PluginImporter.GetAllImporters())
                if (Configure(importer)) importer.SaveAndReimport();
        }

        internal static bool Configure(PluginImporter importer)
        {
            var path = importer.assetPath.Replace('\\', '/');
            if (!path.StartsWith(VlcPluginLayout.Root + "/", StringComparison.Ordinal)) return false;
            var relative = path.Substring(VlcPluginLayout.Root.Length + 1);
            bool dirty = false;
            if (path.EndsWith("/LibVLCSharp.dll", StringComparison.OrdinalIgnoreCase))
            {
                // The shared binary has verified platform-neutral library names.
                // Only the generated iOS variant imports statically via __Internal.
                bool shared = path == VlcPluginLayout.SharedBinding;
                bool ios = path == VlcPluginLayout.IosBinding;
                SetCompatibility(importer, target => shared ? ManagedTargets.Contains(target) : ios && target == BuildTarget.iOS, shared, ref dirty);
                SetEditorData(importer, "CPU", "AnyCPU", ref dirty);
                SetEditorData(importer, "OS", "AnyOS", ref dirty);
                return dirty;
            }
            if (!importer.isNativePlugin) return false;
            var filename = Path.GetFileName(path);
            // Vulkan extension interception must run before Unity creates its
            // device. Newly installed bundles otherwise default to lazy load.
            if ((filename == "VLCUnityPlugin.dll" || filename == "libVLCUnityPlugin.so" || filename == "libVLCUnityPlugin.dylib") && !importer.isPreloaded)
            { importer.isPreloaded = true; dirty = true; }

            BuildTarget? platform = null;
            string cpu = "AnyCPU", editorOS = "", editorCPU = "AnyCPU";
            bool editor = false, player = true;
            if (relative.StartsWith("Windows/x86_64/", StringComparison.Ordinal))
            { platform = BuildTarget.StandaloneWindows64; cpu = editorCPU = "x86_64"; editorOS = "Windows"; editor = true; }
            else if (relative.StartsWith("Linux/x86_64/", StringComparison.Ordinal))
            { platform = BuildTarget.StandaloneLinux64; cpu = editorCPU = "x86_64"; editorOS = "Linux"; editor = true; }
            else if (relative.StartsWith("MacOS/", StringComparison.Ordinal))
            {
                platform = BuildTarget.StandaloneOSX; editorOS = "OSX";
                var parts = relative.Split('/');
                var architecture = parts.Length > 2 ? parts[1] : "";
                cpu = editorCPU = architecture == "universal" ? "AnyCPU" : architecture;
                var editorArchitecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "ARM64" : "x86_64";
                editor = VlcPluginLayout.HasUniversalMac ? architecture == "universal" : architecture == editorArchitecture;
                player = relative.StartsWith(VlcPluginLayout.MacDirectory + "/", StringComparison.Ordinal);
                if (architecture != "universal" && architecture != "ARM64" && architecture != "x86_64") player = editor = false;
            }
            else if (relative.StartsWith("Android/libs/", StringComparison.Ordinal))
            {
                platform = BuildTarget.Android;
                var parts = relative.Split('/');
                cpu = parts.Length < 4 ? "" : AndroidCpu(parts[2]);
                player = cpu.Length != 0;
            }
            else if (relative.StartsWith("iOS/", StringComparison.Ordinal))
            {
                platform = BuildTarget.iOS;
                // arm64 device and arm64 simulator archives use different SDKs.
                player = relative == "iOS/LoadPlugin.mm" || relative.StartsWith(VlcPluginLayout.IosDirectory + "/", StringComparison.Ordinal);
                SetPlatformData(importer, BuildTarget.iOS, "AddToEmbeddedBinaries", "false", ref dirty);
            }
            SetCompatibility(importer, target => player && platform == target, editor, ref dirty);
            if (platform.HasValue) SetPlatformData(importer, platform.Value, "CPU", cpu, ref dirty);
            if (editorOS.Length > 0)
            {
                SetEditorData(importer, "OS", editorOS, ref dirty);
                SetEditorData(importer, "CPU", editorCPU, ref dirty);
            }
            return dirty;
        }

        private static string AndroidCpu(string abi)
        {
            switch (abi)
            {
                case "armeabi-v7a": return "ARMv7";
                case "arm64-v8a": return "ARM64";
                case "x86_64": return "X86_64";
                default: return "";
            }
        }
        private static void SetCompatibility(PluginImporter importer, Func<BuildTarget, bool> allow, bool editor, ref bool dirty)
        {
            if (importer.GetCompatibleWithAnyPlatform()) { importer.SetCompatibleWithAnyPlatform(false); dirty = true; }
            if (importer.GetCompatibleWithEditor() != editor) { importer.SetCompatibleWithEditor(editor); dirty = true; }
            foreach (var target in KnownTargets)
                if (importer.GetCompatibleWithPlatform(target) != allow(target))
                { importer.SetCompatibleWithPlatform(target, allow(target)); dirty = true; }
        }
        private static void SetEditorData(PluginImporter importer, string key, string value, ref bool dirty)
        {
            if (importer.GetEditorData(key) == value) return;
            importer.SetEditorData(key, value); dirty = true;
        }
        private static void SetPlatformData(PluginImporter importer, BuildTarget target, string key, string value, ref bool dirty)
        {
            if (importer.GetPlatformData(target, key) == value) return;
            importer.SetPlatformData(target, key, value); dirty = true;
        }

#if UNITY_IOS
        [PostProcessBuild(100)]
        public static void OnPostprocessBuild(BuildTarget target, string outputPath)
        {
            if (target != BuildTarget.iOS) return;
            var manifest = VlcPluginLayout.ReadIosManifest();
            var projectPath = PBXProject.GetPBXProjectPath(outputPath);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            var frameworkTarget = project.GetUnityFrameworkTargetGuid();
            var mainTarget = project.GetUnityMainTargetGuid();
            project.SetBuildProperty(frameworkTarget, "ENABLE_BITCODE", "NO");
            project.SetBuildProperty(mainTarget, "ENABLE_BITCODE", "NO");
            foreach (var framework in new[] { "Foundation.framework", "Metal.framework", "CoreVideo.framework", "IOSurface.framework", "OpenGLES.framework" })
                project.AddFrameworkToProject(frameworkTarget, framework, false);

            // Static archives are linked to UnityFramework, never embedded.
            var registration = ProjectPluginPath(manifest.staticPluginRegistration);
            var registrationGuid = project.FindFileGuidByProjectPath(registration);
            if (string.IsNullOrEmpty(registrationGuid))
                throw new BuildFailedException("VLCUnity: static module registration source was not exported to Xcode: " + registration);
            project.RemoveFileFromBuild(mainTarget, registrationGuid);
            project.RemoveFileFromBuild(frameworkTarget, registrationGuid);
            project.AddFileToBuild(frameworkTarget, registrationGuid);
            var loader = "Libraries/Plugins/VLCUnity/Runtime/Plugins/iOS/LoadPlugin.mm";
            var loaderGuid = project.FindFileGuidByProjectPath(loader);
            if (string.IsNullOrEmpty(loaderGuid))
                throw new BuildFailedException("VLCUnity: rendering plugin registration source was not exported to Xcode: " + loader);
            project.RemoveFileFromBuild(mainTarget, loaderGuid);
            project.RemoveFileFromBuild(frameworkTarget, loaderGuid);
            project.AddFileToBuild(frameworkTarget, loaderGuid);
            foreach (var archive in manifest.forceLoadArchives)
                project.AddBuildProperty(frameworkTarget, "OTHER_LDFLAGS", "-Wl,-force_load,\"$(PROJECT_DIR)/" + ProjectPluginPath(archive) + "\"");
            foreach (var framework in manifest.frameworks)
                project.AddFrameworkToProject(frameworkTarget, framework, false);
            foreach (var library in manifest.libraries)
            {
                var sdkPath = "usr/lib/lib" + library + ".tbd";
                var guid = project.FindFileGuidByProjectPath(sdkPath);
                if (string.IsNullOrEmpty(guid)) guid = project.AddFile(sdkPath, sdkPath, PBXSourceTree.Sdk);
                project.RemoveFileFromBuild(frameworkTarget, guid);
                project.AddFileToBuild(frameworkTarget, guid);
            }
            project.WriteToFile(projectPath);
        }
        private static string ProjectPluginPath(string relative) =>
            "Libraries/Plugins/VLCUnity/Runtime/Plugins/" + VlcPluginLayout.IosDirectory + "/" + relative.Replace('\\', '/');
#endif
    }

    internal sealed class VlcPluginAssetPostprocessor : AssetPostprocessor
    {
        private void OnPreprocessAsset()
        {
            var importer = assetImporter as PluginImporter;
            if (importer != null) VLCNativePluginProcessor.Configure(importer);
        }
    }
}
