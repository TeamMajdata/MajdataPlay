#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MajdataPlay.SourceGenerators.AndroidJava;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MajdataPlay.Tests.AndroidJavaGeneratorValidation
{
    /// <summary>
    /// Tests configured paths directly without extracting Java APIs or writing generated wrappers.
    /// </summary>
    internal static class ConfigurationPathCases
    {
        /// <summary>
        /// Executes one focused Unity path-token regression case.
        /// </summary>
        /// <param name="name">The registered configuration case name.</param>
        /// <param name="workspace">The isolated fixture workspace and real toolchain configuration.</param>
        /// <exception cref="ArgumentException">The requested case is unknown.</exception>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        /// <exception cref="IOException">A temporary input cannot be staged.</exception>
        public static void Run(string name, FixtureWorkspace workspace)
        {
            switch (name)
            {
                case "unity-data-paths":
                    AssertPathExpansion(workspace);
                    break;
                case "unity-data-managed-references":
                    AssertManagedReferenceDiscovery(workspace);
                    break;
                case "unity-data-player-references":
                    AssertPlayerReferenceDiscovery(workspace);
                    break;
                case "unity-data-editor-paths":
                    AssertEditorPathDiscovery(workspace);
                    break;
                case "unity-data-missing":
                    AssertMissingUnity(workspace);
                    break;
                default:
                    throw new ArgumentException("Unknown configuration case: " + name, nameof(name));
            }
        }

        /// <summary>
        /// Preserves relative paths, environment expansion, and literal Java dollars alongside Unity tokens.
        /// </summary>
        /// <param name="workspace">The project root and isolated path-test directory.</param>
        /// <exception cref="InvalidOperationException">A path differs from the platform-normalized expectation.</exception>
        private static void AssertPathExpansion(FixtureWorkspace workspace)
        {
            var root = workspace.Options.RepositoryRoot;
            var relative = "Assets/Plugins/Android/src/java/Outer$Inner.java";
            var expectedRelative = Path.GetFullPath(Path.Combine(root, relative));
            var suffix = "PlaybackEngines/AndroidPlayer/Variations/mono/Release/Classes/classes.jar";
            var nativeSuffix = Path.Combine("PlaybackEngines", "AndroidPlayer", "Variations", "mono", "Release", "Classes", "classes.jar");
            using var environment = new EnvironmentOverride(new Dictionary<string, string?>
            {
                ["MAJDATA_JAVA_VALIDATION_INPUT_PATH"] = relative,
                ["MAJDATA_JAVA_VALIDATION_UNITY_PATH"] = "{UnityData}/" + suffix,
                ["MAJDATA_JAVA_VALIDATION_UNSET"] = null,
                ["Inner"] = "IncorrectNestedClass"
            });
            foreach (var unityData in new[]
            {
                Path.Combine(workspace.Root, "Unity installation $MAJDATA_JAVA_VALIDATION_UNSET", "Editor", "Data"),
                Path.Combine(workspace.Root, "Unity installation $MAJDATA_JAVA_VALIDATION_UNSET", "Unity.app", "Contents")
            })
            {
                var expected = Path.GetFullPath(Path.Combine(unityData, nativeSuffix));
                Check.Equal(expected, JavaApiConfiguration.ResolvePath("{UnityData}/" + suffix, root, unityData),
                    "Forward-slash Unity suffix must resolve on the current host.");
                Check.Equal(expected, JavaApiConfiguration.ResolvePath(Path.Combine("{UnityData}", nativeSuffix), root, unityData),
                    "Native separators must resolve identically.");
                Check.Equal(expected, JavaApiConfiguration.ResolvePath("{UnityData}/../" + Path.GetFileName(unityData) + "/" + suffix, root, unityData),
                    "Relative segments following a Unity token must be normalized.");
                Check.Equal(expected, JavaApiConfiguration.ResolvePath("%MAJDATA_JAVA_VALIDATION_UNITY_PATH%", root, unityData),
                    "An environment-expanded Unity token must use discovered Unity data.");
                Check.Equal(expectedRelative, JavaApiConfiguration.ResolvePath(relative, root, unityData),
                    "Token-free relative paths must remain project-relative.");
                Check.Equal(expectedRelative, JavaApiConfiguration.ResolvePath(expectedRelative, root, unityData),
                    "Token-free absolute paths must remain unchanged.");
                foreach (var configuredPath in new[]
                {
                    "%MAJDATA_JAVA_VALIDATION_INPUT_PATH%",
                    "$" + "{MAJDATA_JAVA_VALIDATION_INPUT_PATH}",
                    "$MAJDATA_JAVA_VALIDATION_INPUT_PATH"
                })
                {
                    Check.Equal(expectedRelative, JavaApiConfiguration.ResolvePath(configuredPath, root, unityData),
                        "Existing environment syntax must preserve a nested Java identifier.");
                }
            }
            var nestedClass = Path.Combine("fixtures", "Outer$Inner.class");
            Check.Equal(Path.GetFullPath(Path.Combine(root, nestedClass)), JavaApiConfiguration.ResolvePath(nestedClass, root),
                "A Java nested class dollar must not expand even when Inner is defined.");
            foreach (var configuredPath in new[]
            {
                "%MAJDATA_JAVA_VALIDATION_UNSET%",
                "$" + "{MAJDATA_JAVA_VALIDATION_UNSET}",
                "$MAJDATA_JAVA_VALIDATION_UNSET"
            })
            {
                var failure = ExpectInvalidConfiguration(() => JavaApiConfiguration.ResolvePath(configuredPath, root));
                Check.True(failure.Message.Contains("not defined", StringComparison.Ordinal),
                    "An unresolved environment variable must retain its actionable diagnostic.");
            }
        }

        /// <summary>
        /// Resolves the production Unity classes.jar explicitly using only real managed assembly references.
        /// </summary>
        /// <param name="workspace">The real Unity installation and explicit SDK/JDK settings.</param>
        /// <exception cref="InvalidOperationException">Discovery fails or adds an unrequested dependency.</exception>
        private static void AssertManagedReferenceDiscovery(FixtureWorkspace workspace)
        {
            using var environment = new EnvironmentOverride(new Dictionary<string, string?> { ["UNITY_EDITOR_PATH"] = null });
            var tree = CreateTree(workspace);
            var reference = MetadataReference.CreateFromFile(Path.Combine(workspace.Options.UnityEditorData,
                "Managed", "UnityEngine", "UnityEngine.CoreModule.dll"));
            var compilation = CSharpCompilation.Create("UnityDataManagedReferences", new[] { tree }, new[] { reference });
            var options = CreateOptions(workspace);
            var baseline = JavaApiConfiguration.Resolve(options, compilation, tree, CancellationToken.None);
            Check.Equal(0, baseline.ClassPath.Count, "Unity discovery alone must not auto-add Unity jars.");
            options.ClassPath.Add("{UnityData}/PlaybackEngines/AndroidPlayer/Variations/mono/Release/Classes/classes.jar");
            var configuration = JavaApiConfiguration.Resolve(options, compilation, tree, CancellationToken.None);
            var expected = Path.GetFullPath(Path.Combine(workspace.Options.UnityEditorData,
                "PlaybackEngines", "AndroidPlayer", "Variations", "mono", "Release", "Classes", "classes.jar"));
            Check.Equal(1, configuration.ClassPath.Count, "Only the explicitly requested Unity jar may be added.");
            Check.Equal(expected, configuration.ClassPath[0], "Unity references must resolve the production classpath token.");
        }

        /// <summary>
        /// Infers the bundled SDK, JDK, and explicit Unity jar from real nested Android Player metadata only.
        /// </summary>
        /// <param name="workspace">The real Unity installation and repository used for path discovery.</param>
        /// <exception cref="InvalidOperationException">Player metadata is unavailable or toolchain discovery adds incorrect paths.</exception>
        /// <exception cref="IOException">A real Player assembly or configured toolchain input cannot be inspected.</exception>
        /// <exception cref="BadImageFormatException">The installed Player CoreModule is not valid managed assembly metadata.</exception>
        /// <exception cref="GeneratorException">The production configuration cannot discover or validate the inferred paths.</exception>
        private static void AssertPlayerReferenceDiscovery(FixtureWorkspace workspace)
        {
            using var environment = new EnvironmentOverride(new Dictionary<string, string?>
            {
                ["UNITY_EDITOR_PATH"] = null,
                ["UNITY_ANDROID_SDK"] = null,
                ["ANDROID_SDK_ROOT"] = null,
                ["ANDROID_HOME"] = null,
                ["UNITY_JAVA_HOME"] = null,
                ["JAVA_HOME"] = null,
                ["MAJDATA_JAVA_EXTRACTOR"] = null
            });
            var androidPlayer = Path.Combine(workspace.Options.UnityEditorData, "PlaybackEngines", "AndroidPlayer");
            var variation = Path.Combine(androidPlayer, "Variations", "il2cpp");
            var playerCoreModule = new[]
            {
                Path.Combine(variation, "Release", "Managed", "UnityEngine", "UnityEngine.CoreModule.dll"),
                Path.Combine(variation, "Release", "Managed", "UnityEngine.CoreModule.dll"),
                Path.Combine(variation, "Managed", "UnityEngine", "UnityEngine.CoreModule.dll"),
                Path.Combine(variation, "Managed", "UnityEngine.CoreModule.dll")
            }.FirstOrDefault(File.Exists);
            Check.True(playerCoreModule is not null,
                "Install the real Android IL2CPP Player CoreModule under Variations/il2cpp/(Release/)Managed; Editor references cannot substitute.");
            var tree = CreateTree(workspace);
            var reference = MetadataReference.CreateFromFile(playerCoreModule!);
            var compilation = CSharpCompilation.Create("UnityDataPlayerReferences", new[] { tree }, new[] { reference });
            Check.Equal(1, compilation.References.Count(), "Discovery must use only the Android Player reference, without Editor metadata.");
            var assembly = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
            Check.True(assembly is not null && assembly.Identity.Name == "UnityEngine.CoreModule",
                "The regression must load genuine installed Player CoreModule metadata, not a synthetic path.");
            var options = new JavaApiOptions
            {
                ApiLevel = 36
            };
            Check.True(options.AndroidSdkPath is null && options.JavaHome is null,
                "SDK and JDK options must remain unset so Player references are the only discovery input.");
            var expectedAndroidJar = Path.GetFullPath(Path.Combine(androidPlayer, "SDK", "platforms", "android-36", "android.jar"));
            var javaName = OperatingSystem.IsWindows() ? "java.exe" : "java";
            var expectedJava = Path.GetFullPath(Path.Combine(androidPlayer, "OpenJDK", "bin", javaName));
            var baseline = JavaApiConfiguration.Resolve(options, compilation, tree, CancellationToken.None);
            Check.Equal(expectedAndroidJar, baseline.AndroidJar, "Player metadata must infer the bundled API-36 SDK without environment hints.");
            Check.Equal(expectedJava, baseline.JavaExecutable, "Player metadata must infer the bundled JDK without environment hints.");
            Check.Equal(0, baseline.ClassPath.Count, "Discovering the Player toolchain must not auto-inject Unity or SDK jars.");
            options.ClassPath.Add("{UnityData}/PlaybackEngines/AndroidPlayer/Variations/mono/Release/Classes/classes.jar");
            var configuration = JavaApiConfiguration.Resolve(options, compilation, tree, CancellationToken.None);
            var expectedUnityJar = Path.GetFullPath(Path.Combine(androidPlayer, "Variations", "mono", "Release", "Classes", "classes.jar"));
            Check.Equal(expectedAndroidJar, configuration.AndroidJar, "An explicit Unity jar must retain the inferred API-36 SDK.");
            Check.Equal(expectedJava, configuration.JavaExecutable, "An explicit Unity jar must retain the inferred JDK.");
            Check.Equal(1, configuration.ClassPath.Count, "Only the explicitly configured Unity jar may be present.");
            Check.Equal(expectedUnityJar, configuration.ClassPath[0], "Nested Android Player metadata must expand the UnityData classpath token.");
        }

        /// <summary>
        /// Resolves all three input arrays through relative editor paths in Data and macOS Contents layouts.
        /// </summary>
        /// <param name="workspace">The isolated directory for fake editor layouts and real input fixtures.</param>
        /// <exception cref="InvalidOperationException">An input array resolves incorrectly.</exception>
        /// <exception cref="IOException">A temporary Java source or archive cannot be copied.</exception>
        private static void AssertEditorPathDiscovery(FixtureWorkspace workspace)
        {
            var root = workspace.Options.RepositoryRoot;
            var tree = CreateTree(workspace);
            var compilation = CSharpCompilation.Create("UnityDataEditorPaths", new[] { tree });
            foreach (var layout in new[] { Path.Combine("Editor", "Data"), Path.Combine("Unity.app", "Contents") })
            {
                var unityData = Path.GetFullPath(Path.Combine(workspace.CaseDirectory("unity-data-editor-paths"), layout));
                var source = Path.Combine(unityData, "Outer$Inner.java");
                var classes = Path.Combine(unityData, "PlaybackEngines", "AndroidPlayer", "Variations", "mono", "Release", "Classes");
                Directory.CreateDirectory(classes);
                File.Copy(Path.Combine(workspace.SourceDirectory, "fixtures", "Widget.java"), source);
                var jar = Path.Combine(classes, "classes.jar");
                File.Copy(workspace.JarPath, jar);
                var documentation = Path.Combine(unityData, "docs");
                Directory.CreateDirectory(documentation);
                using var environment = new EnvironmentOverride(new Dictionary<string, string?>
                {
                    ["UNITY_EDITOR_PATH"] = Path.GetRelativePath(root, Path.Combine(unityData, "Unity"))
                });
                var options = CreateOptions(workspace);
                options.Sources.Add("{UnityData}/Outer$Inner.java");
                var relativeSource = Path.Combine(workspace.SourceDirectory, "fixtures", "BaseWidget.java");
                options.Sources.Add(Path.GetRelativePath(root, relativeSource));
                options.ClassPath.Add("{UnityData}/PlaybackEngines/AndroidPlayer/Variations/mono/Release/Classes/classes.jar");
                options.DocumentationPaths.Add("{UnityData}/docs");
                var configuration = JavaApiConfiguration.Resolve(options, compilation, tree, CancellationToken.None);
                Check.Equal(2, configuration.Sources.Count, "Both Unity-token and project-relative source inputs must remain present.");
                Check.True(configuration.Sources.Contains(source), "Sources must use the discovered editor directory.");
                Check.True(configuration.Sources.Contains(relativeSource), "Sources without a token must remain project-relative.");
                Check.Equal(1, configuration.ClassPath.Count, "Editor discovery must not add any other Unity jars.");
                Check.Equal(jar, configuration.ClassPath[0], "ClassPath must expand the discovered editor directory.");
                Check.Equal(documentation, configuration.DocumentationPaths[0], "DocumentationPaths must expand the discovered editor directory.");
            }
        }

        /// <summary>
        /// Requires a clear Unity-specific configuration diagnostic for each input array before toolchain lookup.
        /// </summary>
        /// <param name="workspace">The project root used for resolving paths without Unity references.</param>
        /// <exception cref="InvalidOperationException">A missing Unity token does not produce the required diagnostic.</exception>
        private static void AssertMissingUnity(FixtureWorkspace workspace)
        {
            var configuredPath = "{UnityData}/missing-input.jar";
            using var environment = new EnvironmentOverride(new Dictionary<string, string?>
            {
                ["UNITY_EDITOR_PATH"] = null,
                ["MAJDATA_JAVA_VALIDATION_UNITY_PATH"] = configuredPath
            });
            var tree = CreateTree(workspace);
            var compilation = CSharpCompilation.Create("UnityDataMissing", new[] { tree });
            var options = new JavaApiOptions();
            foreach (var inputs in new[] { options.Sources, options.ClassPath, options.DocumentationPaths })
            {
                inputs.Add(configuredPath);
                AssertMissingUnityDiagnostic(() => JavaApiConfiguration.Resolve(options, compilation, tree, CancellationToken.None), configuredPath);
                inputs.Clear();
            }
            AssertMissingUnityDiagnostic(() => JavaApiConfiguration.ResolvePath(configuredPath, workspace.Options.RepositoryRoot), configuredPath);
            options.ClassPath.Add("%MAJDATA_JAVA_VALIDATION_UNITY_PATH%");
            AssertMissingUnityDiagnostic(() => JavaApiConfiguration.Resolve(options, compilation, tree, CancellationToken.None),
                "%MAJDATA_JAVA_VALIDATION_UNITY_PATH%");
            Check.Equal(Path.GetFullPath(Path.Combine(workspace.Options.RepositoryRoot, "Assets", "Outer$Inner.class")),
                JavaApiConfiguration.ResolvePath("Assets/Outer$Inner.class", workspace.Options.RepositoryRoot),
                "Unity must remain optional for paths without a Unity token.");
        }

        /// <summary>
        /// Creates explicit toolchain options independent of Unity editor discovery.
        /// </summary>
        /// <param name="workspace">The validated real SDK/JDK settings.</param>
        /// <returns>Fresh options without implicit source, classpath, or documentation entries.</returns>
        private static JavaApiOptions CreateOptions(FixtureWorkspace workspace)
        {
            return new JavaApiOptions
            {
                AndroidSdkPath = workspace.Options.AndroidSdk,
                JavaHome = workspace.Options.JavaHome,
                ApiLevel = workspace.Options.ApiLevel
            };
        }

        /// <summary>
        /// Creates an in-memory declaration tree anchored to the real project root without writing an asset.
        /// </summary>
        /// <param name="workspace">The repository containing Assets and ProjectSettings.</param>
        /// <returns>The empty syntax tree used only for project-root discovery.</returns>
        private static SyntaxTree CreateTree(FixtureWorkspace workspace)
        {
            return CSharpSyntaxTree.ParseText(string.Empty,
                path: Path.Combine(workspace.Options.RepositoryRoot, "Assets", "UnityDataValidation.cs"));
        }

        /// <summary>
        /// Checks the missing-token diagnostic category, path, token, and editor-discovery guidance.
        /// </summary>
        /// <param name="operation">The path or configuration resolution that requires unavailable Unity data.</param>
        /// <param name="configuredPath">The original input path that must appear in the diagnostic.</param>
        /// <exception cref="InvalidOperationException">The diagnostic is absent or lacks actionable context.</exception>
        private static void AssertMissingUnityDiagnostic(Action operation, string configuredPath)
        {
            var failure = ExpectInvalidConfiguration(operation);
            foreach (var requiredText in new[] { configuredPath, "{UnityData}", "Unity managed", "UNITY_EDITOR_PATH" })
            {
                Check.True(failure.Message.Contains(requiredText, StringComparison.Ordinal),
                    "Missing-Unity diagnostic must include '" + requiredText + "': " + failure.Message);
            }
        }

        /// <summary>
        /// Captures an expected configuration error without accepting internal generator failures.
        /// </summary>
        /// <param name="operation">The deliberately invalid resolution operation.</param>
        /// <returns>The production configuration exception for additional message assertions.</returns>
        /// <exception cref="InvalidOperationException">No configuration error occurs or its descriptor is incorrect.</exception>
        private static GeneratorException ExpectInvalidConfiguration(Action operation)
        {
            try
            {
                operation();
            }
            catch (GeneratorException exception)
            {
                Check.Equal("AJG003", exception.Descriptor.Id, "Path failures must use the invalid-configuration diagnostic.");
                return exception;
            }
            throw new InvalidOperationException("Expected an invalid configuration diagnostic.");
        }
    }
}
