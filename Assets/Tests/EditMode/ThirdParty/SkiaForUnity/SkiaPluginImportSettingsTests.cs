using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

#nullable enable
namespace MajdataPlay.Tests.ThirdParty.SkiaSharp
{
    /// <summary>
    /// Covers the plugin import settings of the libraries that the SkiaForUnity package ships: every player target gets exactly the
    /// native SkiaSharp and HarfBuzzSharp libraries built for it with the CPU of their folder, the Editor loads only the libraries of
    /// its host operating system, and the managed dependencies are available everywhere. Without these settings a player build
    /// leaves out <c>libSkiaSharp</c> and fails at runtime with a <see cref="DllNotFoundException"/>.
    /// </summary>
    public class SkiaPluginImportSettingsTests
    {
        /// <summary>The asset path of the package folder that holds the native libraries and most managed dependencies.</summary>
        private const string LibraryFolder = "Packages/com.ammariqais.skiaforunity/Library/";

        /// <summary>The file names, without extension, of the native libraries every platform folder contains.</summary>
        private static readonly string[] NativeLibraryNames =
        {
            "libSkiaSharp",
            "libHarfBuzzSharp",
        };

        /// <summary>
        /// The asset paths of the managed assemblies the SkiaSharp bindings depend on. The package's
        /// <c>System.Runtime.CompilerServices.Unsafe.dll</c> is left out because Unity replaces it with the newer copy under
        /// <c>Assets/Packages</c>.
        /// </summary>
        private static readonly string[] ManagedDependencyPaths =
        {
            LibraryFolder + "ExCSS.dll",
            LibraryFolder + "Svg.Custom.dll",
            LibraryFolder + "System.Buffers.dll",
            LibraryFolder + "System.Memory.dll",
        };

        /// <summary>The player targets the project builds for.</summary>
        private static readonly BuildTarget[] PlayerTargets =
        {
            BuildTarget.Android,
            BuildTarget.iOS,
            BuildTarget.StandaloneLinux64,
            BuildTarget.StandaloneOSX,
            BuildTarget.StandaloneWindows,
            BuildTarget.StandaloneWindows64,
        };

        /// <summary>
        /// Provides each player target with the asset paths of the native libraries it must include.
        /// </summary>
        /// <returns>One case per player target in <see cref="PlayerTargets"/>.</returns>
        public static IEnumerable<TestCaseData> PlayerTargetCases()
        {
            yield return new TestCaseData(BuildTarget.Android, NativeLibraryPaths(".so", "arm64-v8a", "armeabi-v7a", "x86", "x86_64"))
                .SetName("Android");
            yield return new TestCaseData(BuildTarget.iOS, NativeLibraryPaths(".framework", "ios"))
                .SetName("iOS");
            yield return new TestCaseData(BuildTarget.StandaloneLinux64, NativeLibraryPaths(".so", "linux/linux-x64/native"))
                .SetName("Linux64");
            yield return new TestCaseData(BuildTarget.StandaloneOSX, NativeLibraryPaths(".dylib", "osx"))
                .SetName("macOS");
            yield return new TestCaseData(BuildTarget.StandaloneWindows, NativeLibraryPaths(".dll", "win-x86"))
                .SetName("Windows32");
            yield return new TestCaseData(BuildTarget.StandaloneWindows64, NativeLibraryPaths(".dll", "win-x64", "win-arm64"))
                .SetName("Windows64");
        }

        /// <summary>
        /// Checks that a player target includes exactly the native libraries of its own folder, so the libraries are present at runtime
        /// and no library of another platform or ABI with the same file name is copied into the player.
        /// </summary>
        /// <param name="target">The player target whose plugins are checked.</param>
        /// <param name="expectedPaths">The asset paths of the native libraries the target must include.</param>
        [TestCaseSource(nameof(PlayerTargetCases))]
        public void PlayerTarget_IncludesOnlyItsNativeLibraries(BuildTarget target, string[] expectedPaths)
        {
            var includedPaths = PluginImporter.GetImporters(target)
                .Where(importer => importer.isNativePlugin && importer.assetPath.StartsWith(LibraryFolder, StringComparison.Ordinal))
                .Select(importer => importer.assetPath)
                .ToArray();

            Assert.That(includedPaths, Is.EquivalentTo(expectedPaths));
        }

        /// <summary>
        /// Checks that the Android libraries of each ABI folder target the CPU of that folder, so Unity packs them into the matching
        /// <c>lib/&lt;abi&gt;</c> directory of the APK or AAB.
        /// </summary>
        /// <param name="abiFolder">The folder under <see cref="LibraryFolder"/> named after the Android ABI.</param>
        /// <param name="expectedCpu">The value of the Android <c>CPU</c> plugin setting for that ABI.</param>
        [TestCase("arm64-v8a", "ARM64")]
        [TestCase("armeabi-v7a", "ARMv7")]
        [TestCase("x86", "X86")]
        [TestCase("x86_64", "X86_64")]
        public void AndroidLibraries_TargetTheCpuOfTheirFolder(string abiFolder, string expectedCpu)
        {
            foreach (var path in NativeLibraryPaths(".so", abiFolder))
            {
                var importer = GetPluginImporter(path);

                Assert.That(importer.GetPlatformData(BuildTarget.Android, "CPU"), Is.EqualTo(expectedCpu), path);
            }
        }

        /// <summary>
        /// Checks that the 64-bit Windows libraries of each architecture folder target the CPU of that folder in players and in the
        /// Editor, so Windows x64 and ARM64 players each ship only the binaries they can load and the Editor loads the binaries of its
        /// own architecture.
        /// </summary>
        /// <param name="architectureFolder">The folder under <see cref="LibraryFolder"/> named after the Windows runtime identifier.</param>
        /// <param name="expectedCpu">The value of the Standalone Win64 and Editor <c>CPU</c> plugin settings for that architecture.</param>
        [TestCase("win-x64", "x86_64")]
        [TestCase("win-arm64", "ARM64")]
        public void Windows64Libraries_TargetTheCpuOfTheirFolder(string architectureFolder, string expectedCpu)
        {
            foreach (var path in NativeLibraryPaths(".dll", architectureFolder))
            {
                var importer = GetPluginImporter(path);

                Assert.That(importer.GetPlatformData(BuildTarget.StandaloneWindows64, "CPU"), Is.EqualTo(expectedCpu), path);
                Assert.That(importer.GetEditorData("CPU"), Is.EqualTo(expectedCpu), path);
            }
        }

        /// <summary>
        /// Checks that the Editor loads only the native libraries built for the operating system it runs on.
        /// </summary>
        [Test]
        public void Editor_LoadsOnlyTheLibrariesOfItsOperatingSystem()
        {
            var expectedOperatingSystems = new Dictionary<string, string>();
            AddEditorLibraries(expectedOperatingSystems, NativeLibraryPaths(".dll", "win-x64", "win-arm64"), "Windows");
            AddEditorLibraries(expectedOperatingSystems, NativeLibraryPaths(".dylib", "osx"), "OSX");
            AddEditorLibraries(expectedOperatingSystems, NativeLibraryPaths(".so", "linux/linux-x64/native"), "Linux");

            var editorLibraries = PluginImporter.GetAllImporters()
                .Where(importer => importer.isNativePlugin
                    && importer.assetPath.StartsWith(LibraryFolder, StringComparison.Ordinal)
                    && IsCompatibleWithEditor(importer))
                .ToDictionary(importer => importer.assetPath, importer => importer.GetEditorData("OS"));

            Assert.That(editorLibraries, Is.EquivalentTo(expectedOperatingSystems));
        }

        /// <summary>
        /// Checks that every player target and the Editor include the managed assemblies the SkiaSharp bindings reference.
        /// </summary>
        [Test]
        public void ManagedDependencies_AreIncludedEverywhere()
        {
            foreach (var target in PlayerTargets)
            {
                var includedPaths = PluginImporter.GetImporters(target)
                    .Select(importer => importer.assetPath)
                    .ToArray();

                foreach (var path in ManagedDependencyPaths)
                {
                    Assert.That(includedPaths, Does.Contain(path), $"{target}: {path}");
                }
            }
            foreach (var path in ManagedDependencyPaths)
            {
                Assert.That(IsCompatibleWithEditor(GetPluginImporter(path)), Is.True, path);
            }
        }

        /// <summary>
        /// Builds the asset paths of the native libraries in the given platform folders.
        /// </summary>
        /// <param name="extension">The file extension of the libraries, including the dot.</param>
        /// <param name="folders">The folders under <see cref="LibraryFolder"/> that hold the libraries.</param>
        /// <returns>The asset path of every library in <see cref="NativeLibraryNames"/> for every folder.</returns>
        private static string[] NativeLibraryPaths(string extension, params string[] folders)
        {
            return folders
                .SelectMany(folder => NativeLibraryNames.Select(name => $"{LibraryFolder}{folder}/{name}{extension}"))
                .ToArray();
        }

        /// <summary>
        /// Records that the Editor must load the given libraries on one operating system.
        /// </summary>
        /// <param name="expected">The expected operating system of each Editor library, keyed by asset path.</param>
        /// <param name="paths">The asset paths of the libraries.</param>
        /// <param name="operatingSystem">The value of the Editor <c>OS</c> plugin setting for the libraries.</param>
        private static void AddEditorLibraries(Dictionary<string, string> expected, string[] paths, string operatingSystem)
        {
            foreach (var path in paths)
            {
                expected.Add(path, operatingSystem);
            }
        }

        /// <summary>
        /// Gets the plugin importer of an asset in the package.
        /// </summary>
        /// <param name="path">The asset path of the plugin.</param>
        /// <returns>The plugin importer of the asset.</returns>
        /// <exception cref="AssertionException">The asset does not exist or is not imported as a plugin.</exception>
        private static PluginImporter GetPluginImporter(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as PluginImporter;
            Assert.That(importer, Is.Not.Null, $"No plugin importer for {path}");
            return importer!;
        }

        /// <summary>
        /// Determines whether the Editor loads a plugin, either through its Editor setting or through the Any Platform setting.
        /// </summary>
        /// <param name="importer">The plugin importer to check.</param>
        /// <returns><see langword="true"/> when the plugin is compatible with the Editor; otherwise <see langword="false"/>.</returns>
        private static bool IsCompatibleWithEditor(PluginImporter importer)
        {
            if (importer.GetCompatibleWithAnyPlatform())
            {
                return !importer.GetExcludeEditorFromAnyPlatform();
            }
            return importer.GetCompatibleWithEditor();
        }
    }
}
