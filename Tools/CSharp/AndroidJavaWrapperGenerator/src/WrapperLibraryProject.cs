using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Maintains the standalone extension project and publishes its successfully compiled Unity plugin.</summary>
    internal static class WrapperLibraryProject
    {
        /// <summary>Identifies the extension project within the repository's Tools directory.</summary>
        internal const string DirectoryName = "AndroidJavaWrapper";

        /// <summary>Identifies the explicit standalone project passed to the .NET SDK.</summary>
        internal const string ProjectFileName = "AndroidJavaWrapper.csproj";

        /// <summary>Identifies the managed runtime assembly consumed by Unity.</summary>
        internal const string AssemblyName = "MajdataPlay.Platform.Android.Runtime";

        /// <summary>Identifies the ignored, generated local Unity reference configuration.</summary>
        private const string LocalConfigurationName = "AndroidJavaWrapper.local.props";

        /// <summary>Encodes generated configuration without a byte-order mark.</summary>
        private static readonly Encoding s_utf8 = new UTF8Encoding(false);

        /// <summary>Creates missing project scaffolding and updates its local Unity references without modifying custom project files.</summary>
        /// <param name="projectRoot">The Unity repository root whose Tools directory contains the extension project.</param>
        /// <param name="unityContents">The already validated, pinned Editor Data or Contents directory.</param>
        /// <exception cref="IOException">A reference is absent, a target traverses a symbolic link, or configuration cannot be written.</exception>
        /// <exception cref="UnauthorizedAccessException">The references or extension project cannot be accessed.</exception>
        internal static void CreateProject(string projectRoot, string unityContents)
        {
            ValidateUnityReferences(unityContents);
            var directory = GetProjectDirectory(projectRoot);
            EnsureRealDirectory(directory);
            Directory.CreateDirectory(directory);
            EnsureRealDirectory(Path.Combine(directory, "src"));
            Directory.CreateDirectory(Path.Combine(directory, "src"));
            WriteMissing(Path.Combine(directory, ProjectFileName), GetProjectContents());
            WriteMissing(Path.Combine(directory, "global.json"), GetGlobalJsonContents());

            var configuration = new XDocument(
                new XComment(" Generated local configuration. Do not commit machine-specific Unity paths. "),
                new XElement("Project", new XElement("PropertyGroup",
                    new XElement("UnityData", Path.GetFullPath(unityContents)))));
            WriteIfChanged(Path.Combine(directory, LocalConfigurationName),
                s_utf8.GetBytes(NormalizeNewlines(configuration.ToString()) + "\r\n"));
        }

        /// <summary>Gets the portable initial project definition used only when no user project exists.</summary>
        /// <returns>The complete CRLF project definition with a source glob that includes handwritten extensions.</returns>
        /// <exception cref="InvalidOperationException">The embedded project template is missing.</exception>
        /// <exception cref="IOException">The embedded project template cannot be read.</exception>
        internal static string GetProjectContents()
        {
            return ReadTemplate("AndroidJavaWrapper.csproj.txt");
        }

        /// <summary>Gets the required .NET 9 SDK family selection for the standalone wrapper project.</summary>
        /// <returns>The portable SDK selection with feature-band roll-forward.</returns>
        internal static string GetGlobalJsonContents()
        {
            return "{\r\n  \"sdk\": {\r\n    \"version\": \"9.0.100\",\r\n    \"rollForward\": \"latestFeature\"\r\n  }\r\n}\r\n";
        }

        /// <summary>Builds only the standalone project with .NET 9, retaining installed Unity artifacts on cancellation or failure.</summary>
        /// <param name="projectRoot">The Unity repository root containing the extension project.</param>
        /// <param name="unityContents">The already validated, pinned Editor Data or Contents directory.</param>
        /// <param name="cancellationToken">The refresh lifetime that cancels the compiler and its child processes.</param>
        /// <returns>The successful assembly and documentation paths in a unique ignored build directory.</returns>
        /// <exception cref="IOException">The SDK is not .NET 9, building fails, output is missing, or its assembly identity differs.</exception>
        /// <exception cref="UnauthorizedAccessException">The extension sources, references, or build directory cannot be accessed.</exception>
        /// <exception cref="OperationCanceledException">The refresh lifetime is canceled.</exception>
        internal static async Task<WrapperLibraryBuild> BuildAsync(string projectRoot, string unityContents,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreateProject(projectRoot, unityContents);
            var directory = GetProjectDirectory(projectRoot);
            var version = await RunDotnetAsync(directory, new[] { "--version" }, cancellationToken, false).ConfigureAwait(false);
            if (!version.Trim().StartsWith("9.", StringComparison.Ordinal))
            {
                throw new IOException("The standalone wrapper project requires a .NET 9 SDK; selected SDK: " + version.Trim());
            }

            var outputDirectory = Path.Combine(directory, ".work", "builds", Guid.NewGuid().ToString("N"));
            EnsureRealDirectory(outputDirectory);
            Directory.CreateDirectory(outputDirectory);
            Console.WriteLine("Compiling Android wrapper library and handwritten extensions...");
            await RunDotnetAsync(directory, new[]
            {
                "build", Path.Combine(directory, ProjectFileName), "--configuration", "Release",
                "--nologo", "--disable-build-servers", "--verbosity", "minimal", "--output", outputDirectory,
                "-p:TargetFramework=netstandard2.1", "-p:LangVersion=9.0", "-p:AssemblyName=" + AssemblyName,
                "-p:DebugType=None", "-p:BaseIntermediateOutputPath=" + Path.Combine(outputDirectory, "obj") + Path.DirectorySeparatorChar
            }, cancellationToken, true).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            var dllPath = Path.Combine(outputDirectory, AssemblyName + ".dll");
            var xmlPath = Path.Combine(outputDirectory, AssemblyName + ".xml");
            ValidateBuild(dllPath, xmlPath);
            return new WrapperLibraryBuild(dllPath, xmlPath);
        }

        /// <summary>Publishes a successful build through temporary sibling files while retaining existing importer metadata and GUIDs.</summary>
        /// <param name="projectRoot">The Unity repository root containing the plugin destination.</param>
        /// <param name="build">The successful standalone compilation whose immutable output should be installed.</param>
        /// <returns>True when the assembly or its XML documentation bytes changed.</returns>
        /// <exception cref="IOException">The build is invalid, a destination traverses a symbolic link, or publishing fails.</exception>
        /// <exception cref="UnauthorizedAccessException">The build or plugin destination cannot be accessed.</exception>
        internal static bool Publish(string projectRoot, WrapperLibraryBuild build)
        {
            ValidateBuild(build.DllPath, build.XmlPath);
            var destination = Path.Combine(Path.GetFullPath(projectRoot), "Assets", "Plugins", "MajdataPlay", "Platform", "Android");
            EnsureRealDirectory(destination);
            var dllPath = Path.Combine(destination, AssemblyName + ".dll");
            var xmlPath = Path.Combine(destination, AssemblyName + ".xml");
            var metaPath = dllPath + ".meta";
            EnsureRealFile(dllPath);
            EnsureRealFile(xmlPath);
            EnsureRealFile(metaPath);
            var dll = File.ReadAllBytes(build.DllPath);
            var xml = File.ReadAllBytes(build.XmlPath);
            VerifyWritableChange(dllPath, dll);
            VerifyWritableChange(xmlPath, xml);
            var metadata = File.Exists(metaPath) ? null
                : s_utf8.GetBytes(ReadTemplate("AndroidRuntime.dll.meta.txt").Replace("__GUID__", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(destination);
            if (metadata != null)
            {
                WriteMissing(metaPath, s_utf8.GetString(metadata));
            }

            // The DLL is the final publication boundary: failed compilation never reaches this method.
            var documentationChanged = WriteIfChanged(xmlPath, xml);
            var assemblyChanged = WriteIfChanged(dllPath, dll);
            return documentationChanged || assemblyChanged;
        }

        /// <summary>Resolves the requested repository's standalone wrapper project directory.</summary>
        /// <param name="projectRoot">The repository root containing Tools.</param>
        /// <returns>The absolute extension project directory.</returns>
        private static string GetProjectDirectory(string projectRoot)
        {
            return Path.Combine(Path.GetFullPath(projectRoot), "Tools", DirectoryName);
        }

        /// <summary>Verifies the two actual pinned Unity managed references before starting a build.</summary>
        /// <param name="unityContents">The selected Editor Data or Contents directory.</param>
        /// <exception cref="FileNotFoundException">A required Unity managed reference is absent.</exception>
        private static void ValidateUnityReferences(string unityContents)
        {
            foreach (var module in new[] { "UnityEngine.CoreModule.dll", "UnityEngine.AndroidJNIModule.dll" })
            {
                var reference = Path.Combine(unityContents, "Managed", "UnityEngine", module);
                if (!File.Exists(reference))
                {
                    throw new FileNotFoundException("The pinned Unity Editor reference is missing: " + reference, reference);
                }
            }
        }

        /// <summary>Verifies successful compiler artifacts before exposing or installing a build.</summary>
        /// <param name="dllPath">The compiled managed assembly.</param>
        /// <param name="xmlPath">The XML documentation generated by the same compilation.</param>
        /// <exception cref="IOException">An artifact is absent or the assembly identity differs from the runtime contract.</exception>
        private static void ValidateBuild(string dllPath, string xmlPath)
        {
            if (!File.Exists(dllPath) || !File.Exists(xmlPath))
            {
                throw new IOException("The wrapper build did not produce its required DLL and XML documentation: " + dllPath);
            }

            if (System.Reflection.AssemblyName.GetAssemblyName(dllPath).Name != AssemblyName)
            {
                throw new IOException("The wrapper assembly must be named " + AssemblyName + ": " + dllPath);
            }
        }

        /// <summary>Runs the pinned SDK without a visible window and drains both diagnostics streams while cancellation remains observable.</summary>
        /// <param name="workingDirectory">The extension project directory whose global.json selects the SDK.</param>
        /// <param name="arguments">The explicit SDK command and individual arguments.</param>
        /// <param name="cancellationToken">The refresh cancellation token propagated to process execution.</param>
        /// <param name="reportOutput">Whether successful standard output should be displayed in CLI diagnostics.</param>
        /// <returns>The command's complete standard output.</returns>
        /// <exception cref="IOException">The command cannot start or exits unsuccessfully.</exception>
        /// <exception cref="OperationCanceledException">The token cancels and the compiler process tree is terminated.</exception>
        private static async Task<string> RunDotnetAsync(string workingDirectory, string[] arguments,
            CancellationToken cancellationToken, bool reportOutput)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = new Process { StartInfo = start };
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!process.Start())
                {
                    throw new IOException("Could not start dotnet for the standalone wrapper library.");
                }

                var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var error = process.StandardError.ReadToEndAsync(cancellationToken);
                try
                {
                    await Task.WhenAll(process.WaitForExitAsync(cancellationToken), output, error).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                        await process.WaitForExitAsync().ConfigureAwait(false);
                    }

                    throw;
                }

                if (reportOutput || process.ExitCode != 0)
                {
                    Console.Write(output.Result);
                }
                if (!string.IsNullOrEmpty(error.Result))
                {
                    Console.Error.Write(error.Result);
                }
                if (process.ExitCode != 0)
                {
                    throw new IOException("dotnet " + arguments[0] + " failed for the standalone wrapper library (exit " + process.ExitCode + ").");
                }

                return output.Result;
            }
            catch (Exception exception) when (exception is Win32Exception || exception is InvalidOperationException)
            {
                throw new IOException("Could not execute dotnet for the standalone wrapper library.", exception);
            }
        }

        /// <summary>Reads one portable embedded project or plugin-importer template.</summary>
        /// <param name="name">The template filename under the tool's Templates directory.</param>
        /// <returns>The full template normalized to CRLF.</returns>
        /// <exception cref="InvalidOperationException">The embedded template is absent.</exception>
        /// <exception cref="IOException">The embedded resource cannot be read.</exception>
        private static string ReadTemplate(string name)
        {
            var resourceName = typeof(WrapperLibraryProject).Namespace + ".Templates." + name;
            using var stream = typeof(WrapperLibraryProject).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException("Missing embedded wrapper project template: " + resourceName);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            return NormalizeNewlines(reader.ReadToEnd());
        }

        /// <summary>Normalizes generated project and importer text without changing user-authored files.</summary>
        /// <param name="value">The complete generated content.</param>
        /// <returns>The content using repository CRLF newlines.</returns>
        private static string NormalizeNewlines(string value)
        {
            return value.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
        }

        /// <summary>Creates missing portable scaffolding without overwriting user project customizations or metadata.</summary>
        /// <param name="path">The absolute file to create only when absent.</param>
        /// <param name="content">The complete default configuration text.</param>
        /// <exception cref="IOException">The path is symbolic or writing the missing file fails.</exception>
        private static void WriteMissing(string path, string content)
        {
            EnsureRealFile(path);
            if (!File.Exists(path))
            {
                WriteIfChanged(path, s_utf8.GetBytes(content));
            }
        }

        /// <summary>Atomically replaces changed bytes and avoids timestamp churn for identical assembly output.</summary>
        /// <param name="path">The absolute non-symbolic target file.</param>
        /// <param name="content">The complete prepared bytes.</param>
        /// <returns>True when a file was created or replaced.</returns>
        /// <exception cref="IOException">The path is symbolic or the temporary write or replacement fails.</exception>
        /// <exception cref="UnauthorizedAccessException">The file cannot be read or written.</exception>
        private static bool WriteIfChanged(string path, byte[] content)
        {
            EnsureRealFile(path);
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
            {
                return false;
            }
            VerifyWritableChange(path, content);

            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                {
                    stream.Write(content, 0, content.Length);
                }

                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null);
                }
                else
                {
                    File.Move(temporary, path);
                }
                return true;
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        /// <summary>Rejects symbolic or directory destinations before changing generated configuration or plugins.</summary>
        /// <param name="path">The absolute destination whose existing attributes are inspected.</param>
        /// <exception cref="IOException">The destination or an ancestor is unsafe to change.</exception>
        private static void EnsureRealFile(string path)
        {
            EnsureRealDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var attributes = GetAttributes(path);
            if (attributes.HasValue && (attributes.Value & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new IOException("Refusing to change a symbolic or directory destination: " + path);
            }
        }

        /// <summary>Preflights changes without rejecting preserved read-only project files, metadata, or identical build output.</summary>
        /// <param name="path">The existing destination that might require replacement.</param>
        /// <param name="content">The complete replacement bytes prepared before publishing.</param>
        /// <exception cref="IOException">The destination differs and is read-only.</exception>
        private static void VerifyWritableChange(string path, byte[] content)
        {
            var attributes = GetAttributes(path);
            if (attributes.HasValue && (attributes.Value & FileAttributes.ReadOnly) != 0
                && !File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
            {
                throw new IOException("Refusing to replace a read-only wrapper project or plugin file: " + path);
            }
        }

        /// <summary>Rejects symbolic links and junctions in every existing publication or build directory ancestor.</summary>
        /// <param name="path">The absolute build or destination directory.</param>
        /// <exception cref="IOException">An ancestor is a file or symbolic directory.</exception>
        private static void EnsureRealDirectory(string path)
        {
            var current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                var attributes = GetAttributes(current);
                if (attributes.HasValue && ((attributes.Value & FileAttributes.Directory) == 0
                    || (attributes.Value & FileAttributes.ReparsePoint) != 0))
                {
                    throw new IOException("The wrapper project cannot traverse a file, symbolic link, or junction: " + current);
                }
                current = Path.GetDirectoryName(current);
            }
        }

        /// <summary>Inspects existing attributes without treating a missing parent directory as a publication error.</summary>
        /// <param name="path">The absolute file or directory to inspect.</param>
        /// <returns>The existing attributes, or null when the path or an ancestor does not exist.</returns>
        /// <exception cref="IOException">Inspection fails for a reason other than a missing path.</exception>
        private static FileAttributes? GetAttributes(string path)
        {
            try
            {
                return File.GetAttributes(path);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
        }
    }

    /// <summary>Identifies immutable compiler artifacts from one successful standalone wrapper build.</summary>
    internal sealed class WrapperLibraryBuild
    {
        /// <summary>Gets the full compiled runtime assembly path.</summary>
        internal string DllPath { get; }

        /// <summary>Gets the full XML documentation path generated alongside the assembly.</summary>
        internal string XmlPath { get; }

        /// <summary>Captures a successful compilation without loading the Unity-dependent assembly into the generator process.</summary>
        /// <param name="dllPath">The immutable compiled assembly path.</param>
        /// <param name="xmlPath">The immutable XML documentation path.</param>
        internal WrapperLibraryBuild(string dllPath, string xmlPath)
        {
            DllPath = dllPath;
            XmlPath = xmlPath;
        }
    }
}
