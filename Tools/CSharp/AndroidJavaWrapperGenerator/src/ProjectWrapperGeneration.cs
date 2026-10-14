using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using MajdataPlay.Platform.Android.Editor;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Generates the independent wrapper project and installs its successfully compiled runtime library.</summary>
    internal static class ProjectWrapperGeneration
    {
        /// <summary>Generates or rebuilds wrapper sources and publishes their DLL only after successful compilation.</summary>
        /// <param name="arguments">The project root, Unity contents directory, optional SDK, and generation options.</param>
        /// <returns>Zero on success, two for invalid arguments, or the generation failure code.</returns>
        internal static async Task<int> RunAsync(string[] arguments)
        {
            try
            {
                var buildOnly = arguments[0] == "--build-wrapper";
                if (arguments.Length < 2 || string.IsNullOrWhiteSpace(arguments[1]) || arguments[1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException(arguments[0] + " requires a Unity project directory.");
                }

                var projectRoot = Path.GetFullPath(arguments[1]);
                string? unityContents = null;
                string? sdk = null;
                var forwarded = new List<string>();
                for (var index = 2; index < arguments.Length; index++)
                {
                    var option = arguments[index];
                    if (option == "--unity-data" || option == "--sdk")
                    {
                        if (++index >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index])
                            || arguments[index].StartsWith("--", StringComparison.Ordinal))
                        {
                            throw new ArgumentException(option + " requires a directory.");
                        }

                        if (option == "--unity-data")
                        {
                            unityContents = Path.GetFullPath(arguments[index]);
                        }
                        else
                        {
                            sdk = Path.GetFullPath(arguments[index]);
                        }
                    }
                    else if (option == "--offline-docs" || option == "--no-docs" || option == "--refresh-docs")
                    {
                        forwarded.Add(option);
                    }
                    else if (option == "--jobs" || option == "--docs-jobs" || option == "--docs-cache" || option == "--api-level")
                    {
                        forwarded.Add(option);
                        if (++index >= arguments.Length || arguments[index].StartsWith("--", StringComparison.Ordinal))
                        {
                            throw new ArgumentException(option + " requires a value.");
                        }
                        forwarded.Add(arguments[index]);
                    }
                    else
                    {
                        throw new ArgumentException("Unsupported project generation option: " + option);
                    }
                }

                if (unityContents is null)
                {
                    throw new ArgumentException("Project generation requires --unity-data with the pinned Unity Editor Data or Contents directory.");
                }

                await ValidatePinnedEditorAsync(projectRoot, unityContents).ConfigureAwait(false);
                if (buildOnly)
                {
                    return await BuildAndPublishAsync(projectRoot, unityContents).ConfigureAwait(false);
                }

                sdk ??= Path.Combine(unityContents, "PlaybackEngines", "AndroidPlayer", "SDK");
                var request = JavaWrapperGenerationRequest.Create(projectRoot, unityContents, sdk);
                foreach (var warning in request.Warnings)
                {
                    Console.Error.WriteLine("Warning: " + warning);
                }

                var generatorArguments = new List<string>
                {
                    "--sdk", sdk,
                    "--output", request.StagingDirectory,
                    "--namespace", request.RootNamespace,
                    "--docs-cache", request.DocumentationCacheDirectory
                };
                foreach (var input in request.Inputs)
                {
                    generatorArguments.Add("--input");
                    generatorArguments.Add(input);
                }
                generatorArguments.AddRange(forwarded);

                var exitCode = await Program.Main(generatorArguments.ToArray()).ConfigureAwait(false);
                if (exitCode != 0)
                {
                    return exitCode;
                }

                JavaWrapperOutput.Publish(request.StagingDirectory, request.OutputDirectory);

                return await BuildAndPublishAsync(projectRoot, unityContents).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("Wrapper library compilation canceled; the installed DLL was retained.");
                return 130;
            }
            catch (ArgumentException exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 2;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException)
            {
                Console.Error.WriteLine("Project generation failed: " + exception.Message);
                return 1;
            }
        }

        /// <summary>Compiles extensions and publishes the runtime library before retiring owned legacy Unity sources.</summary>
        /// <param name="projectRoot">The Unity repository containing the standalone wrapper project.</param>
        /// <param name="unityContents">The validated pinned Unity Editor Data or Contents directory.</param>
        /// <returns>Zero after successful publication.</returns>
        /// <exception cref="IOException">Compilation, ownership validation, or publication fails.</exception>
        /// <exception cref="UnauthorizedAccessException">A source or publication destination cannot be accessed.</exception>
        /// <exception cref="OperationCanceledException">The compiler is canceled by the CLI user.</exception>
        private static async Task<int> BuildAndPublishAsync(string projectRoot, string unityContents)
        {
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancelHandler = (sender, args) =>
            {
                args.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;
            try
            {
                var build = await WrapperLibraryProject.BuildAsync(projectRoot, unityContents, cancellation.Token).ConfigureAwait(false);
                cancellation.Token.ThrowIfCancellationRequested();
                using var cleanup = LegacyWrapperSources.Prepare(projectRoot);
                cancellation.Token.ThrowIfCancellationRequested();
                WrapperLibraryProject.Publish(projectRoot, build);
                cleanup.Cleanup();
                Console.WriteLine("Published runtime library: " + Path.Combine(projectRoot, "Assets", "Plugins", "MajdataPlay", "Platform", "Android", WrapperLibraryProject.AssemblyName + ".dll"));
                return 0;
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
            }
        }

        /// <summary>Verifies that bootstrap metadata comes from the exact Unity version declared by the target project.</summary>
        /// <param name="projectRoot">The target Unity project containing its shared Editor version pin.</param>
        /// <param name="unityContents">The selected installed Editor Data or Contents directory.</param>
        /// <returns>A task that completes only after the project and installed Editor versions match.</returns>
        /// <exception cref="IOException">The version pin, installed Editor metadata, or version command is missing, invalid, or mismatched.</exception>
        /// <exception cref="UnauthorizedAccessException">The project pin or installed Editor cannot be read or executed.</exception>
        private static async Task ValidatePinnedEditorAsync(string projectRoot, string unityContents)
        {
            var pinPath = Path.Combine(projectRoot, "ProjectSettings", "ProjectVersion.txt");
            var pin = Regex.Match(File.ReadAllText(pinPath), @"^\s*m_EditorVersion:\s*(?<version>[0-9]+\.[0-9]+\.[0-9]+[abfp][0-9]+)\s*$", RegexOptions.Multiline);
            if (!pin.Success)
            {
                throw new IOException("ProjectVersion.txt does not declare an exact Unity Editor version: " + pinPath);
            }

            var required = pin.Groups["version"].Value;
            var editorContents = Path.TrimEndingDirectorySeparator(unityContents);
            var executable = OperatingSystem.IsMacOS()
                ? Path.Combine(editorContents, "MacOS", "Unity")
                : Path.Combine(Path.GetDirectoryName(editorContents)!, OperatingSystem.IsWindows() ? "Unity.exe" : "Unity");
            if (!File.Exists(executable))
            {
                throw new FileNotFoundException("The selected Unity installation has no Editor executable: " + executable, executable);
            }

            string? installed = null;
            if (OperatingSystem.IsWindows())
            {
                var productVersion = FileVersionInfo.GetVersionInfo(executable).ProductVersion;
                installed = GetExactEditorVersion(productVersion == null ? null : productVersion.Split('_')[0]);
            }
            else if (OperatingSystem.IsMacOS())
            {
                installed = ReadMacEditorVersion(Path.Combine(editorContents, "Info.plist"));
            }

            installed ??= await ReadEditorCommandVersionAsync(executable).ConfigureAwait(false);
            if (installed != required)
            {
                throw new IOException("Project bootstrap requires Unity " + required + ", but the selected Editor is " + installed + ": " + unityContents);
            }
        }

        /// <summary>Recognizes an exact Unity version including its release channel and revision number.</summary>
        /// <param name="value">The complete version value obtained from trusted installation metadata.</param>
        /// <returns>The validated Unity version, or null when the value is not exact.</returns>
        private static string? GetExactEditorVersion(string? value)
        {
            return value != null && Regex.IsMatch(value, @"\A[0-9]+\.[0-9]+\.[0-9]+[abfp][0-9]+\z") ? value : null;
        }

        /// <summary>Reads exact macOS bundle version metadata without resolving external XML entities.</summary>
        /// <param name="path">The installed Unity application's Info.plist path.</param>
        /// <returns>An exact declared Unity version, or null when bundle metadata cannot establish it.</returns>
        /// <exception cref="IOException">Reading an existing plist fails.</exception>
        /// <exception cref="UnauthorizedAccessException">The plist cannot be read.</exception>
        private static string? ReadMacEditorVersion(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
                using var reader = XmlReader.Create(path, settings);
                var document = new XmlDocument { XmlResolver = null };
                document.Load(reader);
                var dictionary = document.SelectSingleNode("/plist/dict");
                if (dictionary == null)
                {
                    return null;
                }

                foreach (XmlNode entry in dictionary.ChildNodes)
                {
                    if (entry.Name != "key" || (entry.InnerText != "CFBundleVersion" && entry.InnerText != "CFBundleShortVersionString"))
                    {
                        continue;
                    }

                    var version = GetExactEditorVersion(entry.NextSibling?.InnerText);
                    if (version != null)
                    {
                        return version;
                    }
                }
            }
            catch (XmlException)
            {
                // Binary or incomplete plists require the installed Editor's bounded version command.
            }

            return null;
        }

        /// <summary>Queries an installed Editor's version without opening any Unity project.</summary>
        /// <param name="executable">The verified installed Editor executable.</param>
        /// <returns>A task containing the exact version reported by the Editor.</returns>
        /// <exception cref="IOException">Starting or completing the command fails, takes over 30 seconds, or reports no exact version.</exception>
        private static async Task<string> ReadEditorCommandVersionAsync(string executable)
        {
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("-version");
            using var process = new Process { StartInfo = start };
            try
            {
                if (!process.Start())
                {
                    throw new IOException("Could not start the selected Unity Editor's version command: " + executable);
                }

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var error = process.StandardError.ReadToEndAsync(timeout.Token);
                try
                {
                    await Task.WhenAll(process.WaitForExitAsync(timeout.Token), output, error).ConfigureAwait(false);
                }
                catch (OperationCanceledException exception)
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }

                    throw new IOException("The selected Unity Editor's version command exceeded 30 seconds: " + executable, exception);
                }

                var version = Regex.Match(output.Result + "\n" + error.Result, @"(?<![0-9.])(?<version>[0-9]+\.[0-9]+\.[0-9]+[abfp][0-9]+)(?![0-9])");
                if (process.ExitCode != 0 || !version.Success)
                {
                    throw new IOException("The selected Unity Editor's version command did not report an exact successful version: " + executable);
                }

                return version.Groups["version"].Value;
            }
            catch (Exception exception) when (exception is Win32Exception || exception is InvalidOperationException)
            {
                throw new IOException("Could not query the selected Unity Editor's version: " + executable, exception);
            }
        }
    }
}
