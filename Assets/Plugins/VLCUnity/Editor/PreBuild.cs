using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Videolabs.VLCUnity.Editor
{
    public sealed class PreProcessBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;
        public void OnPreprocessBuild(BuildReport report) => Validate(report.summary.platform);

        [MenuItem("Tools/VLCUnity/Validate current platform bundle")]
        public static void ValidateCurrentPlatform()
        {
            VLCNativePluginProcessor.ConfigureAll();
            Validate(EditorUserBuildSettings.activeBuildTarget);
            UnityEngine.Debug.Log("VLCUnity: required native bundle files and selected architectures are present. Playback still requires a runtime check on the target device.");
        }

        internal static void Validate(BuildTarget target)
        {
            var errors = new List<string>();
            Require(target == BuildTarget.iOS ? VlcPluginLayout.IosBinding : VlcPluginLayout.SharedBinding, errors);
            switch (target)
            {
                case BuildTarget.StandaloneWindows64:
                    ValidateDesktop("Windows/x86_64", "VLCUnityPlugin.dll", "libvlc.dll", "libvlccore.dll", "*.dll", errors);
                    break;
                case BuildTarget.StandaloneLinux64:
                    ValidateDesktop("Linux/x86_64", "libVLCUnityPlugin.so", "libvlc.so", "libvlccore.so", "*.so*", errors);
                    ValidateElfDirectory(VlcPluginLayout.FullPath("Linux/x86_64"), 2, 62, errors);
                    break;
                case BuildTarget.StandaloneOSX:
                    var mac = VlcPluginLayout.MacDirectory;
                    ValidateDesktop(mac, "libVLCUnityPlugin.dylib", "libvlc.dylib", "libvlccore.dylib", "*.dylib", errors);
                    var expected = mac.EndsWith("/universal", StringComparison.Ordinal)
                        ? new[] { 0x01000007u, 0x0100000cu }
                        : new[] { mac.EndsWith("/ARM64", StringComparison.Ordinal) ? 0x0100000cu : 0x01000007u };
                    if (Directory.Exists(VlcPluginLayout.FullPath(mac)))
                        foreach (var file in Directory.GetFiles(VlcPluginLayout.FullPath(mac), "*.dylib", SearchOption.AllDirectories))
                            ValidateMachO(file, expected, errors);
                    break;
                case BuildTarget.Android:
                    ValidateAndroid(errors);
                    break;
                case BuildTarget.iOS:
                    ValidateIos(errors);
                    break;
                default:
                    errors.Add("No VLCUnity native bundle is configured for " + target + ". Supported bundle targets are Windows x64, Linux x64, macOS, Android and iOS.");
                    break;
            }
            if (errors.Count != 0)
                throw new BuildFailedException("VLCUnity cannot build " + target + ":\n - " + string.Join("\n - ", errors) +
                    "\nBuild/install the matching LibVLC and bridge bundle described in Tools/VLCUnity/README.md, then run Tools > VLCUnity > Configure platform plugins.");
        }

        private static void ValidateDesktop(string directory, string bridge, string libvlc, string core, string modulePattern, List<string> errors)
        {
            Require(VlcPluginLayout.FullPath(directory + "/" + bridge), errors);
            Require(VlcPluginLayout.FullPath(directory + "/" + libvlc), errors);
            Require(VlcPluginLayout.FullPath(directory + "/" + core), errors);
            var modules = VlcPluginLayout.FullPath(directory + "/plugins");
            if (!Directory.Exists(modules) || !Directory.EnumerateFiles(modules, modulePattern, SearchOption.AllDirectories).Any())
                errors.Add("Missing LibVLC decoder/output modules: " + modules + ". Copy the plugins directory from the same LibVLC build; the three loader libraries alone are insufficient.");
        }

        private static void ValidateAndroid(List<string> errors)
        {
            var architectures = PlayerSettings.Android.targetArchitectures;
            if (architectures == AndroidArchitecture.None) errors.Add("No Android target ABI is selected.");
            var supported = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64 | AndroidArchitecture.X86_64;
            if ((architectures & ~supported) != 0) errors.Add("VLCUnity supports Android ARMv7, ARM64 and x86_64; the selected target contains an unsupported ABI.");
            ValidateAndroidAbi(architectures, AndroidArchitecture.ARMv7, "armeabi-v7a", 1, 40, errors);
            ValidateAndroidAbi(architectures, AndroidArchitecture.ARM64, "arm64-v8a", 2, 183, errors);
            ValidateAndroidAbi(architectures, AndroidArchitecture.X86_64, "x86_64", 2, 62, errors);
        }

        private static void ValidateAndroidAbi(AndroidArchitecture selected, AndroidArchitecture flag, string abi, byte elfClass, ushort machine, List<string> errors)
        {
            if ((selected & flag) == 0) return;
            var directory = VlcPluginLayout.FullPath("Android/libs/" + abi);
            Require(Path.Combine(directory, "libVLCUnityPlugin.so"), errors);
            Require(Path.Combine(directory, "libvlc.so"), errors);
            ValidateElfDirectory(directory, elfClass, machine, errors);
        }

        private static void ValidateIos(List<string> errors)
        {
            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.iOS) != ScriptingImplementation.IL2CPP)
                errors.Add("iOS static LibVLC bindings require the IL2CPP scripting backend.");
            var directory = VlcPluginLayout.IosDirectory;
            Require(VlcPluginLayout.FullPath("iOS/LoadPlugin.mm"), errors);
            foreach (var library in new[] { "libVLCUnityPlugin.a", "libvlc.a", "libvlccore.a" })
                RequireArchive(VlcPluginLayout.FullPath(directory + "/" + library), errors);
            VlcPluginLayout.IosBundleManifest manifest;
            try { manifest = VlcPluginLayout.ReadIosManifest(); }
            catch (BuildFailedException error) { errors.Add(error.Message); return; }
            try
            {
                var registration = VlcPluginLayout.BundleFile(directory, manifest.staticPluginRegistration);
                Require(registration, errors);
                var extension = Path.GetExtension(registration).ToLowerInvariant();
                if (extension != ".c" && extension != ".cpp" && extension != ".m" && extension != ".mm")
                    errors.Add("staticPluginRegistration must name a Unity native source file (.c/.cpp/.m/.mm), implementing VLCUnityRegisterStaticModules for the pinned LibVLC build.");
                foreach (var archive in manifest.forceLoadArchives)
                {
                    var file = VlcPluginLayout.BundleFile(directory, archive);
                    if (!file.EndsWith(".a", StringComparison.OrdinalIgnoreCase) || archive.IndexOfAny(new[] { '"', ',', '\r', '\n' }) >= 0)
                        errors.Add("Invalid iOS forceLoadArchives entry: " + archive);
                    else RequireArchive(file, errors);
                }
                foreach (var framework in manifest.frameworks)
                    if (framework == null || !Regex.IsMatch(framework, @"^[A-Za-z0-9_]+\.framework$"))
                        errors.Add("iOS frameworks must be SDK framework names, for example AudioToolbox.framework.");
                foreach (var library in manifest.libraries)
                    if (library == null || !Regex.IsMatch(library, @"^[A-Za-z0-9_+.-]+$"))
                        errors.Add("iOS libraries must be SDK library names without the lib prefix or .tbd suffix, for example c++ or z.");
            }
            catch (BuildFailedException error) { errors.Add(error.Message); }
        }

        private static void Require(string file, List<string> errors)
        {
            if (!File.Exists(file) || new FileInfo(file).Length == 0) errors.Add("Missing or empty file: " + file);
        }

        private static void RequireArchive(string file, List<string> errors)
        {
            Require(file, errors);
            if (!File.Exists(file)) return;
            using (var stream = File.OpenRead(file))
            {
                var header = new byte[8];
                if (stream.Read(header, 0, header.Length) != header.Length) return;
                // Apple universal .a packages have a fat Mach-O container.
                if (System.Text.Encoding.ASCII.GetString(header) != "!<arch>\n" &&
                    ReadUInt32(header, 0, false) != 0xcafebabe && ReadUInt32(header, 0, false) != 0xcafebabf)
                    errors.Add("Not a static archive or Apple universal archive: " + file);
            }
        }

        private static void ValidateElfDirectory(string directory, byte elfClass, ushort machine, List<string> errors)
        {
            if (!Directory.Exists(directory)) return;
            foreach (var file in Directory.GetFiles(directory, "*.so*", SearchOption.AllDirectories))
            {
                // Unity metadata and symbol sidecars are not shared objects.
                if (!Regex.IsMatch(Path.GetFileName(file), @"\.so(?:\.[0-9]+)*$")) continue;
                using (var stream = File.OpenRead(file))
                {
                    var header = new byte[20];
                    if (stream.Read(header, 0, header.Length) != header.Length || header[0] != 0x7f || header[1] != 'E' || header[2] != 'L' || header[3] != 'F' ||
                        header[4] != elfClass || header[5] != 1 || (header[18] | header[19] << 8) != machine)
                        errors.Add("Native ELF architecture does not match its platform/ABI directory: " + file);
                }
            }
        }

        private static void ValidateMachO(string file, uint[] required, List<string> errors)
        {
            var architectures = new HashSet<uint>();
            using (var stream = File.OpenRead(file))
            using (var reader = new BinaryReader(stream))
            {
                var header = reader.ReadBytes(8);
                if (header.Length != 8) { errors.Add("Invalid Mach-O library: " + file); return; }
                var magic = ReadUInt32(header, 0, false);
                if (magic == 0xcafebabe || magic == 0xcafebabf || magic == 0xbebafeca || magic == 0xbfbafeca)
                {
                    bool little = magic == 0xbebafeca || magic == 0xbfbafeca;
                    int recordSize = magic == 0xcafebabf || magic == 0xbfbafeca ? 32 : 20;
                    uint count = ReadUInt32(header, 4, little);
                    if (count > 64 || stream.Length < 8L + count * recordSize)
                    { errors.Add("Invalid universal Mach-O header: " + file); return; }
                    for (uint index = 0; index < count; ++index)
                    {
                        var record = reader.ReadBytes(recordSize);
                        architectures.Add(ReadUInt32(record, 0, little));
                    }
                }
                else if (magic == 0xfeedfacf || magic == 0xcffaedfe)
                    architectures.Add(ReadUInt32(header, 4, magic == 0xcffaedfe));
            }
            if (required.Any(architecture => !architectures.Contains(architecture)))
                errors.Add("Mach-O library lacks the required CPU slice(s): " + file + (required.Length == 2 ? " (universal requires both x86_64 and arm64)." : "."));
        }

        private static uint ReadUInt32(byte[] bytes, int offset, bool little)
        {
            return little
                ? (uint)(bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24)
                : (uint)(bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3]);
        }
    }
}
