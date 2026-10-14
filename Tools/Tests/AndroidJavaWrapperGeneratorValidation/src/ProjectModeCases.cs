using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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

        /// <summary>Runs real SDK and project bootstrap commands in a scratch project and checks publication and repeat identity.</summary>
        /// <param name="settings">The real pinned Unity and Android SDK installations.</param>
        /// <param name="workspace">The exclusively owned ignored validation directory.</param>
        /// <param name="productionProject">The production project whose Java inputs and Editor version pin are copied.</param>
        /// <param name="expectedSources">Independently generated complete SDK, project, and Unity sources.</param>
        /// <exception cref="InvalidOperationException">Bootstrap, deterministic output, or asset identity checks fail.</exception>
        /// <exception cref="IOException">Copying prerequisites or inspecting output fails.</exception>
        private static void VerifyProjectModeBootstrap(ValidationSettings settings, string workspace, string productionProject,
            IReadOnlyDictionary<string, string> expectedSources)
        {
            var root = Path.Combine(workspace, "project-mode-bootstrap");
            CreateProjectModeFixture(productionProject, root);
            var output = Path.Combine(root, "Assets", "Plugins", "MajdataPlay", "Platform", "Android", "Runtime");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "Handwritten.cs"), "// handwritten partial extension survives project bootstrap\r\n");
            File.WriteAllText(Path.Combine(output, "Handwritten.cs.meta"), "handwritten project GUID");
            var arguments = new[]
            {
                "--project", root, "--unity-data", settings.UnityData, "--sdk", settings.Sdk,
                "--jobs", "4", "--docs-jobs", "1", "--no-docs"
            };
            Console.WriteLine("Running project bootstrap with real SDK, copied project Java inputs, and pinned Unity metadata...");
            var first = CaptureProjectModeCommand(arguments);
            Require(first.ExitCode == 0, "Project mode installs complete bindings before Unity imports the scratch project: " + first.Error);
            Require(first.Output.Contains("Published ", StringComparison.Ordinal), "Project mode reports completed Runtime publication");
            var manifest = JsonSerializer.Deserialize<GeneratedOutput.OutputManifest>(File.ReadAllText(Path.Combine(output, EditorManifestName)))!;
            Require(manifest.RootNamespace == EditorRootNamespace && manifest.Files.Count == expectedSources.Count
                && expectedSources.Keys.All(path => manifest.Files.ContainsKey(path)), "Project bootstrap manifest contains every independent SDK, project, and Unity binding at its namespace path");
            Require(expectedSources.All(source => File.ReadAllText(Path.Combine(output, source.Key))
                == source.Value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal)), "Project bootstrap publishes byte-equivalent sources to the independent complete Editor-input generation");
            Require(!Directory.Exists(Path.Combine(root, "Assets", "Plugins", "MajdataPlay", "Platform", "Android", "Generated")), "Fresh project bootstrap creates only the Runtime output root");
            Require(File.ReadAllText(Path.Combine(output, "Handwritten.cs")) == "// handwritten partial extension survives project bootstrap\r\n"
                && File.ReadAllText(Path.Combine(output, "Handwritten.cs.meta")) == "handwritten project GUID", "Project bootstrap preserves handwritten Runtime extensions and GUIDs");

            foreach (var relativePath in new[] { "AndroidJni.cs", "Bindings/Android/Net/Uri.g.cs", "Bindings/Net/Majdata/Majdataplay/StorageAccess.g.cs", EditorManifestName })
            {
                Require(File.Exists(Path.Combine(output, relativePath)), "Bootstrap publishes the expected runtime or project binding path: " + relativePath);
                File.WriteAllText(Path.Combine(output, relativePath + ".meta"), "fileFormatVersion: 2\r\nguid: " + Guid.NewGuid().ToString("N") + "\r\n");
            }

            SetEditorFixtureTimestamps(output);
            var published = SnapshotEditorFiles(output);
            var repeated = CaptureProjectModeCommand(arguments);
            Require(repeated.ExitCode == 0, "Project mode can bootstrap an already published Runtime directory: " + repeated.Error);
            RequireEditorFilesUnchanged(output, published, "Repeated project bootstrap retains bytes, Unity GUIDs, and timestamps");

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
                RequireEditorFilesUnchanged(output, published, "Invalid project generation options preserve imported Runtime assets");
            }

            var unavailableApi = CaptureProjectModeCommand(new[]
            {
                "--project", root, "--unity-data", settings.UnityData, "--sdk", settings.Sdk,
                "--api-level", "2147483647", "--no-docs"
            });
            Require(unavailableApi.ExitCode == 1 && !unavailableApi.Output.Contains("Published ", StringComparison.Ordinal), "Project mode reports an unavailable SDK without publishing staged assets");
            RequireEditorFilesUnchanged(output, published, "Failed project metadata generation preserves imported Runtime assets");

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
                    RequireEditorFilesUnchanged(output, published, "Rejected Unity version pin preserves imported Runtime assets");
                }
            }
            finally
            {
                File.WriteAllText(pinPath, originalPin);
            }
        }

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
