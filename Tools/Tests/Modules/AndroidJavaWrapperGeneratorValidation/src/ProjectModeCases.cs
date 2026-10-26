using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading;
using System.Xml.Linq;
using MajdataPlay.Platform.Android.Editor;
using MajdataPlay.Tools.AndroidJavaWrapperGenerator;

#nullable enable

namespace MajdataPlay.Tests.AndroidJavaWrapperGeneratorValidation
{
    /// <summary>Checks project bootstrap commands without opening or writing to the production Unity project.</summary>
    internal static partial class Program
    {
        /// <summary>Checks malformed project arguments before any project discovery or generation can run.</summary>
        /// <param name="workspace">The exclusively owned ignored validation directory.</param>
        /// <exception cref="InvalidOperationException">An argument or side-effect contract fails.</exception>
        /// <exception cref="IOException">Preparing or inspecting a fixture fails.</exception>
        private static void VerifyProjectModeArguments(string workspace)
        {
            var root = Path.Combine(workspace, "project-mode-arguments");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "Handwritten.cs"), "// project mode arguments must leave this source untouched\r\n");
            SetEditorFixtureTimestamps(root);
            var before = SnapshotEditorFiles(root);
            var cases = new Dictionary<string, string[]>
            {
                { "missing-project", new[] { "--project" } },
                { "empty-project", new[] { "--project", "" } },
                { "project-option-value", new[] { "--project", "--unity-data", root } },
                { "missing-unity-option", new[] { "--project", root } },
                { "missing-unity-value", new[] { "--project", root, "--unity-data" } },
                { "unity-option-value", new[] { "--project", root, "--unity-data", "--sdk", root } },
                { "missing-sdk-value", new[] { "--project", root, "--sdk" } },
                { "missing-jobs-value", new[] { "--project", root, "--jobs" } },
                { "missing-docs-jobs-value", new[] { "--project", root, "--docs-jobs", "--no-docs" } },
                { "missing-docs-cache-value", new[] { "--project", root, "--docs-cache" } },
                { "missing-api-value", new[] { "--project", root, "--api-level" } },
                { "unsupported-namespace", new[] { "--project", root, "--namespace", "Injected.Root" } },
                { "unsupported-output", new[] { "--project", root, "--output", Path.Combine(root, "injected-output") } },
                { "unsupported-input", new[] { "--project", root, "--input", Path.Combine(root, "Injected.java") } },
                { "unsupported-filter", new[] { "--project", root, "--include-package", "java.lang" } },
                { "unsupported-no-sdk", new[] { "--project", root, "--no-sdk" } },
                { "unsupported-option", new[] { "--project", root, "--not-a-project-option" } }
            };
            foreach (var test in cases)
            {
                var result = CaptureProjectModeCommand(test.Value);
                Require(result.ExitCode == 2 && !string.IsNullOrWhiteSpace(result.Error), "Project mode diagnoses invalid arguments: " + test.Key);
                RequireEditorFilesUnchanged(root, before, "Rejected project arguments " + test.Key);
                Require(Directory.GetDirectories(root, "*", SearchOption.AllDirectories).Length == 0, "Rejected project arguments create no staging or asset directories: " + test.Key);
            }
        }

        /// <summary>Builds the real SDK and handwritten extensions in an isolated project and checks DLL publication and source ownership.</summary>
        /// <param name="settings">The real pinned Unity and Android SDK installations.</param>
        /// <param name="workspace">The exclusively owned ignored validation directory.</param>
        /// <param name="productionProject">The production project whose Java inputs and Editor version pin are copied.</param>
        /// <param name="expectedSources">Independently generated complete SDK, project, and Unity sources.</param>
        /// <param name="deviceProbeSource">The validation Java probe compiled into the same independent wrapper library.</param>
        /// <returns>The independently compiled installed wrapper library used to validate production consumers.</returns>
        /// <exception cref="InvalidOperationException">Bootstrap, deterministic output, or asset identity checks fail.</exception>
        /// <exception cref="IOException">Copying prerequisites or inspecting output fails.</exception>
        private static byte[] VerifyProjectModeBootstrap(ValidationSettings settings, string workspace, string productionProject,
            IReadOnlyDictionary<string, string> expectedSources, string deviceProbeSource)
        {
            var root = Path.Combine(workspace, "project-mode-bootstrap");
            CreateProjectModeFixture(productionProject, root);
            var probeDirectory = Path.Combine(root, "Assets", "Plugins", "Android", "src", "java", "net", "majdata", "validation");
            Directory.CreateDirectory(probeDirectory);
            File.Copy(deviceProbeSource, Path.Combine(probeDirectory, "DeviceJniProbe.java"));
            var android = Path.Combine(root, "Assets", "Plugins", "MajdataPlay", "Platform", "Android");
            var former = Path.Combine(android, "Runtime");
            var sourceProject = Path.Combine(root, "Tools", "AndroidJavaWrapper");
            var output = Path.Combine(sourceProject, "src");
            var libraryPath = Path.Combine(android, "MajdataPlay.Platform.Android.Runtime.dll");
            var oldSources = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "AndroidJni.cs", expectedSources["AndroidJni.cs"] },
                { "Bindings/Android/Net/Uri.g.cs", expectedSources["Bindings/Android/Net/Uri.g.cs"] }
            };
            GeneratedOutput.Write(new GeneratorOptions { RootNamespace = EditorRootNamespace, OutputDirectory = former }, null, oldSources, CancellationToken.None);
            foreach (var source in oldSources)
            {
                File.WriteAllText(Path.Combine(former, source.Key + ".meta"), "former source GUID");
            }
            File.WriteAllText(Path.Combine(former, EditorManifestName + ".meta"), "former manifest GUID");
            File.WriteAllText(Path.Combine(former, "Handwritten.cs"), "// unowned Unity source survives migration\r\n");
            File.WriteAllText(Path.Combine(former, "Handwritten.cs.meta"), "handwritten project GUID");
            var extensionPath = Path.Combine(output, "Bindings", "Java", "Lang", "Integer.Validation.e.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(extensionPath)!);
            File.WriteAllText(extensionPath, ProjectExtensionSource);
            var arguments = new[]
            {
                "--project", root, "--unity-data", settings.UnityData, "--sdk", settings.Sdk,
                "--jobs", "4", "--docs-jobs", "1", "--no-docs"
            };
            Console.WriteLine("Running project bootstrap with real SDK, copied project Java inputs, and pinned Unity metadata...");
            var first = CaptureProjectModeCommand(arguments);
            Require(first.ExitCode == 0, "Project mode builds and installs the complete wrapper DLL before Unity imports the scratch project: " + first.Error);
            Require(first.Output.Contains("Published ", StringComparison.Ordinal), "Project mode reports completed wrapper DLL publication");
            var manifest = JsonSerializer.Deserialize<GeneratedOutput.OutputManifest>(File.ReadAllText(Path.Combine(output, EditorManifestName)))!;
            Require(manifest.RootNamespace == EditorRootNamespace && manifest.Files.Count == expectedSources.Count
                && expectedSources.Keys.All(path => manifest.Files.ContainsKey(path)), "Project bootstrap manifest contains every independent SDK, project, and Unity binding at its namespace path");
            Require(expectedSources.All(source => File.ReadAllText(Path.Combine(output, source.Key))
                == source.Value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal)), "Tools source project receives byte-equivalent complete bindings at namespace paths");
            Require(!Directory.Exists(Path.Combine(android, "Generated")), "Bootstrap does not create another Unity source output directory");
            Require(oldSources.Keys.All(path => !File.Exists(Path.Combine(former, path)) && !File.Exists(Path.Combine(former, path + ".meta")))
                && !File.Exists(Path.Combine(former, EditorManifestName)) && !File.Exists(Path.Combine(former, EditorManifestName + ".meta")), "DLL publication retires only former manifest-owned Unity sources and metadata");
            Require(File.ReadAllText(Path.Combine(former, "Handwritten.cs")) == "// unowned Unity source survives migration\r\n"
                && File.ReadAllText(Path.Combine(former, "Handwritten.cs.meta")) == "handwritten project GUID", "DLL migration preserves unowned Unity sources and GUIDs");
            Require(File.ReadAllText(extensionPath) == ProjectExtensionSource, "Generation retains a handwritten partial extension in the Tools source project");
            var projectPath = Path.Combine(sourceProject, "AndroidJavaWrapper.csproj");
            var definition = XDocument.Load(projectPath);
            Require(definition.Descendants("TargetFramework").Single().Value == "netstandard2.1"
                && definition.Descendants("LangVersion").Single().Value == "9.0"
                && definition.Descendants("AssemblyName").Single().Value == "MajdataPlay.Platform.Android.Runtime", "Independent project targets C# 9 and .NET Standard 2.1 with the requested assembly identity");
            Require(File.Exists(libraryPath) && File.Exists(libraryPath + ".meta"), "Bootstrap installs the compiled DLL with Unity plugin metadata");
            var importer = File.ReadAllText(libraryPath + ".meta");
            Require(importer.Contains("PluginImporter:", StringComparison.Ordinal)
                && importer.Contains("Editor: Editor", StringComparison.Ordinal)
                && importer.Contains("Android: Android", StringComparison.Ordinal), "Published library metadata exposes the plugin to the Editor and Android");
            var libraryBytes = File.ReadAllBytes(libraryPath);
            VerifyPublishedLibrary(settings, libraryBytes);
            File.WriteAllText(projectPath, File.ReadAllText(projectPath).Replace("</Project>", "  <!-- User-owned project customization is retained. -->\r\n</Project>", StringComparison.Ordinal));
            SetEditorFixtureTimestamps(output);
            SetEditorFixtureTimestamps(android);
            var publishedSources = SnapshotEditorFiles(output);
            var published = SnapshotEditorFiles(android);
            var projectBefore = File.ReadAllBytes(projectPath);
            var repeated = CaptureProjectModeCommand(arguments);
            Require(repeated.ExitCode == 0, "Project mode rebuilds an existing independent wrapper project: " + repeated.Error);
            RequireEditorFilesUnchanged(android, published, "Repeated publication retains DLL bytes, Unity GUIDs, and timestamps");
            RequireEditorFilesUnchanged(output, publishedSources, "Repeated generation preserves source bytes, extensions, and timestamps");
            Require(File.ReadAllBytes(projectPath).SequenceEqual(projectBefore), "Repeated generation preserves the user-editable independent csproj");
            Expect<IOException>(() => WrapperLibraryProject.Publish(root, new WrapperLibraryBuild(libraryPath, Path.Combine(workspace, "missing-library-documentation.xml"))), "DLL publication rejects incomplete compiler artifacts");
            RequireEditorFilesUnchanged(android, published, "Rejected incomplete compilation artifacts retain the installed DLL and importer metadata");

            File.AppendAllText(extensionPath, "\r\n#error Validation intentionally rejects this extension\r\n");
            var invalidExtension = CaptureProjectModeCommand(new[] { "--build-wrapper", root, "--unity-data", settings.UnityData });
            Require(invalidExtension.ExitCode == 1 && !invalidExtension.Output.Contains("Published ", StringComparison.Ordinal), "A user extension compilation failure prevents DLL publication");
            RequireEditorFilesUnchanged(android, published, "Failed extension compilation retains the last working DLL and plugin metadata");
            File.WriteAllText(extensionPath, ProjectExtensionSource);

            foreach (var options in new[]
            {
                new[] { "--jobs", "0", "--no-docs" },
                new[] { "--docs-jobs", "1000", "--no-docs" },
                new[] { "--api-level", "0", "--no-docs" },
                new[] { "--offline-docs", "--refresh-docs" }
            })
            {
                var rejected = CaptureProjectModeCommand(new[] { "--project", root, "--unity-data", settings.UnityData, "--sdk", settings.Sdk }.Concat(options).ToArray());
                Require(rejected.ExitCode == 2 && !rejected.Output.Contains("Published ", StringComparison.Ordinal), "Project mode validates forwarded generation options before publication: " + string.Join(" ", options));
                RequireEditorFilesUnchanged(android, published, "Invalid project generation options preserve the imported library");
            }

            var unavailableApi = CaptureProjectModeCommand(new[]
            {
                "--project", root, "--unity-data", settings.UnityData, "--sdk", settings.Sdk,
                "--api-level", "2147483647", "--no-docs"
            });
            Require(unavailableApi.ExitCode == 1 && !unavailableApi.Output.Contains("Published ", StringComparison.Ordinal), "Project mode reports an unavailable SDK without publishing staged assets");
            RequireEditorFilesUnchanged(android, published, "Failed project metadata generation preserves the imported library");

            var pinPath = Path.Combine(root, "ProjectSettings", "ProjectVersion.txt");
            var originalPin = File.ReadAllText(pinPath);
            try
            {
                foreach (var invalidPin in new[] { "m_EditorVersion: 1.0.0f1\r\n", "m_EditorVersion: 6000.3\r\n" })
                {
                    File.WriteAllText(pinPath, invalidPin);
                    var rejected = CaptureProjectModeCommand(arguments);
                    Require(rejected.ExitCode == 1 && !rejected.Output.Contains("Scanning Java", StringComparison.Ordinal)
                        && !rejected.Output.Contains("Published ", StringComparison.Ordinal), "Project mode rejects an unpinned or mismatched Unity installation before scanning inputs");
                    RequireEditorFilesUnchanged(android, published, "Rejected Unity version pin preserves the imported library");
                }
            }
            finally
            {
                File.WriteAllText(pinPath, originalPin);
            }
            return libraryBytes;
        }

        /// <summary>Loads the actual Android-enabled DLL and checks extension inclusion and off-platform JNI guards.</summary>
        /// <param name="settings">The pinned real Unity modules used as build references.</param>
        /// <param name="libraryBytes">The installed independent library bytes.</param>
        /// <exception cref="InvalidOperationException">Assembly identity, extension inclusion, or a runtime guard fails.</exception>
        private static void VerifyPublishedLibrary(ValidationSettings settings, byte[] libraryBytes)
        {
            foreach (var module in new[] { "UnityEngine.CoreModule.dll", "UnityEngine.AndroidJNIModule.dll" })
            {
                AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(settings.UnityData, "Managed", "UnityEngine", module));
            }
            var assembly = Assembly.Load(libraryBytes);
            Require(assembly.GetName().Name == "MajdataPlay.Platform.Android.Runtime", "Installed DLL has the requested runtime assembly identity");
            var integer = assembly.GetType(EditorRootNamespace + ".Bindings.Java.Lang.Integer", true)!;
            Require((int)integer.GetProperty("ValidationValue", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)! == 17, "A handwritten XXX.e.cs partial class is compiled into the installed DLL");
            var helper = assembly.GetType(EditorRootNamespace + ".AndroidJni", true)!;
            Expect<PlatformNotSupportedException>(() => Invoke(helper.GetMethod("GetClass")!, new object?[] { "java.lang.Object" }), "Android-enabled standalone DLL rejects desktop JNI before touching Unity native methods");
        }

        /// <summary>Provides a safe reflection probe for a user-authored partial extension compiled into the library.</summary>
        private const string ProjectExtensionSource = @"#nullable enable
namespace MajdataPlay.Platform.Android.Runtime.Bindings.Java.Lang
{
    /// <summary>Adds a validation-only managed extension.</summary>
    public partial class Integer
    {
        /// <summary>Returns the known managed value used to verify extension inclusion.</summary>
        public static int ValidationValue
        {
            get
            {
                return 17;
            }
        }
    }
}
";

        /// <summary>Copies project metadata and supported Android inputs into an isolated bootstrap prerequisite fixture.</summary>
        /// <param name="productionProject">The production project read only for prerequisites.</param>
        /// <param name="scratchProject">The exclusively owned scratch project receiving copied files.</param>
        /// <exception cref="IOException">Copying project prerequisites fails.</exception>
        private static void CreateProjectModeFixture(string productionProject, string scratchProject)
        {
            var tool = Path.Combine(scratchProject, "Tools", "CSharp", "AndroidJavaWrapperGenerator");
            Directory.CreateDirectory(tool);
            File.WriteAllText(Path.Combine(tool, "AndroidJavaWrapperGenerator.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />\r\n");
            File.Copy(Path.Combine(productionProject, "Tools", "CSharp", "AndroidJavaWrapperGenerator", "global.json"), Path.Combine(tool, "global.json"));
            var settings = Path.Combine(scratchProject, "ProjectSettings");
            Directory.CreateDirectory(settings);
            File.Copy(Path.Combine(productionProject, "ProjectSettings", "ProjectVersion.txt"), Path.Combine(settings, "ProjectVersion.txt"));
            var extensions = Path.Combine(productionProject, "Tools", "AndroidJavaWrapper", "src");
            foreach (var source in Directory.GetFiles(extensions, "*.e.cs", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(scratchProject, "Tools", "AndroidJavaWrapper", "src", Path.GetRelativePath(extensions, source));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination);
            }
            var plugins = Path.Combine(productionProject, "Assets", "Plugins", "Android");
            foreach (var source in Directory.GetFiles(plugins, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(source);
                if (!JavaWrapperGenerationRequest.IsJavaInput(source) && name != "mainTemplate.gradle" && name != "launcherTemplate.gradle")
                {
                    continue;
                }

                var destination = Path.Combine(scratchProject, "Assets", "Plugins", "Android", Path.GetRelativePath(plugins, source));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination);
                if (File.Exists(source + ".meta"))
                {
                    File.Copy(source + ".meta", destination + ".meta");
                }
            }
        }

        /// <summary>Runs the actual CLI entry point while restoring both console streams after completion or failure.</summary>
        /// <param name="arguments">The literal project-mode command arguments.</param>
        /// <returns>The exit code, standard output, and error diagnostics produced by the completed command.</returns>
        private static (int ExitCode, string Output, string Error) CaptureProjectModeCommand(string[] arguments)
        {
            var standardOutput = Console.Out;
            var standardError = Console.Error;
            using var output = new StringWriter();
            using var error = new StringWriter();
            try
            {
                Console.SetOut(output);
                Console.SetError(error);
                var exitCode = MajdataPlay.Tools.AndroidJavaWrapperGenerator.Program.Main(arguments).GetAwaiter().GetResult();
                return (exitCode, output.ToString(), error.ToString());
            }
            finally
            {
                Console.SetOut(standardOutput);
                Console.SetError(standardError);
            }
        }
    }
}
