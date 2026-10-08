#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace MajdataPlay.Tests.AndroidJavaGeneratorValidation
{
    /// <summary>
    /// Resolves the explicit toolchain and the bounded validation settings.
    /// </summary>
    internal sealed class HarnessOptions
    {
        /// <summary>
        /// Gets the repository containing the linked production sources.
        /// </summary>
        public string RepositoryRoot { get; private set; }

        /// <summary>
        /// Gets the installed Unity 6000.3.17f1 Editor/Data directory.
        /// </summary>
        public string UnityEditorData { get; private set; }

        /// <summary>
        /// Gets the Android SDK containing the selected platform.
        /// </summary>
        public string AndroidSdk { get; private set; }

        /// <summary>
        /// Gets the JDK used for javac, jar, and the production extractor.
        /// </summary>
        public string JavaHome { get; private set; }

        /// <summary>
        /// Gets the production JavaApiExtractor.java helper path.
        /// </summary>
        public string ExtractorPath { get; private set; }

        /// <summary>
        /// Gets the Android platform API used as the fixture boot classpath.
        /// </summary>
        public int ApiLevel { get; private set; } = 36;

        /// <summary>
        /// Gets the wall-clock limit for an entire isolated test case.
        /// </summary>
        public int TimeoutSeconds { get; private set; } = 180;

        /// <summary>
        /// Gets the wall-clock limit for fixture compiler and archiver processes.
        /// </summary>
        public int ToolTimeoutSeconds { get; private set; } = 90;

        /// <summary>
        /// Gets an optional case-name filter.
        /// </summary>
        public string? Filter { get; private set; }

        /// <summary>
        /// Gets the selected worker case, or null for the suite supervisor.
        /// </summary>
        public string? WorkerCase { get; private set; }

        /// <summary>
        /// Gets the prepared workspace shared by isolated workers.
        /// </summary>
        public string? WorkspacePath { get; private set; }

        /// <summary>
        /// Gets whether to print available cases without running them.
        /// </summary>
        public bool ListCases { get; private set; }

        /// <summary>
        /// Gets whether command-line help was requested.
        /// </summary>
        public bool ShowHelp { get; private set; }

        /// <summary>
        /// Gets the real Android platform archive used by extraction and javac.
        /// </summary>
        public string AndroidJar
        {
            get
            {
                return Path.Combine(AndroidSdk, "platforms", "android-" + ApiLevel, "android.jar");
            }
        }

        /// <summary>
        /// Creates options from the build-time paths and environment overrides.
        /// </summary>
        private HarnessOptions()
        {
            RepositoryRoot = ReadBuildMetadata("RepositoryRoot");
            UnityEditorData = ReadBuildMetadata("UnityEditorData");
            AndroidSdk = Environment.GetEnvironmentVariable("UNITY_ANDROID_SDK")
                ?? Path.Combine(UnityEditorData, "PlaybackEngines", "AndroidPlayer", "SDK");
            JavaHome = Environment.GetEnvironmentVariable("UNITY_JAVA_HOME")
                ?? Path.Combine(UnityEditorData, "PlaybackEngines", "AndroidPlayer", "OpenJDK");
            ExtractorPath = Environment.GetEnvironmentVariable("MAJDATA_JAVA_EXTRACTOR")
                ?? Path.Combine(RepositoryRoot, "Tools", "AndroidJavaGenerator", "Java", "JavaApiExtractor.java");
        }

        /// <summary>
        /// Parses the supported options without modifying the repository.
        /// </summary>
        /// <param name="arguments">The application arguments after dotnet's separator.</param>
        /// <returns>The resolved validation options.</returns>
        /// <exception cref="ArgumentException">An option is unknown or its value is invalid.</exception>
        public static HarnessOptions Parse(string[] arguments)
        {
            var options = new HarnessOptions();
            var sdkSpecified = Environment.GetEnvironmentVariable("UNITY_ANDROID_SDK") is not null;
            var javaSpecified = Environment.GetEnvironmentVariable("UNITY_JAVA_HOME") is not null;
            var extractorSpecified = Environment.GetEnvironmentVariable("MAJDATA_JAVA_EXTRACTOR") is not null;
            for (var index = 0; index < arguments.Length; index++)
            {
                var argument = arguments[index];
                if (argument == "--help" || argument == "-h")
                {
                    options.ShowHelp = true;
                    continue;
                }
                if (argument == "--list")
                {
                    options.ListCases = true;
                    continue;
                }
                if (index + 1 >= arguments.Length)
                {
                    throw new ArgumentException("Missing value for " + argument + ".");
                }
                var value = arguments[++index];
                switch (argument)
                {
                    case "--repo-root":
                        options.RepositoryRoot = Path.GetFullPath(value);
                        break;
                    case "--unity-editor-data":
                        options.UnityEditorData = Path.GetFullPath(value);
                        break;
                    case "--android-sdk":
                        options.AndroidSdk = Path.GetFullPath(value);
                        sdkSpecified = true;
                        break;
                    case "--java-home":
                        options.JavaHome = Path.GetFullPath(value);
                        javaSpecified = true;
                        break;
                    case "--extractor":
                        options.ExtractorPath = Path.GetFullPath(value);
                        extractorSpecified = true;
                        break;
                    case "--api-level":
                        options.ApiLevel = ParsePositive(value, argument);
                        break;
                    case "--timeout-seconds":
                        options.TimeoutSeconds = ParsePositive(value, argument);
                        break;
                    case "--tool-timeout-seconds":
                        options.ToolTimeoutSeconds = ParsePositive(value, argument);
                        break;
                    case "--filter":
                        options.Filter = value;
                        break;
                    case "--worker":
                        options.WorkerCase = value;
                        break;
                    case "--workspace":
                        options.WorkspacePath = Path.GetFullPath(value);
                        break;
                    default:
                        throw new ArgumentException("Unknown option " + argument + ".");
                }
            }
            if (!sdkSpecified)
            {
                options.AndroidSdk = Path.Combine(options.UnityEditorData, "PlaybackEngines", "AndroidPlayer", "SDK");
            }
            if (!javaSpecified)
            {
                options.JavaHome = Path.Combine(options.UnityEditorData, "PlaybackEngines", "AndroidPlayer", "OpenJDK");
            }
            if (!extractorSpecified)
            {
                options.ExtractorPath = Path.Combine(options.RepositoryRoot, "Tools", "AndroidJavaGenerator", "Java", "JavaApiExtractor.java");
            }
            return options;
        }

        /// <summary>
        /// Applies the production generator's documented environment fallbacks.
        /// </summary>
        public void ApplyEnvironment()
        {
            Environment.SetEnvironmentVariable("UNITY_ANDROID_SDK", AndroidSdk);
            Environment.SetEnvironmentVariable("UNITY_JAVA_HOME", JavaHome);
            Environment.SetEnvironmentVariable("MAJDATA_JAVA_EXTRACTOR", ExtractorPath);
        }

        /// <summary>
        /// Verifies the real Unity and Java inputs before starting any test process.
        /// </summary>
        /// <exception cref="FileNotFoundException">A required production input is unavailable.</exception>
        public void Validate()
        {
            RequireFile(Path.Combine(UnityEditorData, "Managed", "UnityEngine", "UnityEngine.CoreModule.dll"));
            RequireFile(Path.Combine(UnityEditorData, "Managed", "UnityEngine", "UnityEngine.AndroidJNIModule.dll"));
            RequireFile(AndroidJar);
            RequireFile(ToolPath("java"));
            RequireFile(ToolPath("javac"));
            RequireFile(ToolPath("jar"));
            RequireFile(ExtractorPath);
            RequireFile(Path.Combine(RepositoryRoot, "Tools", "Tests", "AndroidJavaGeneratorValidation", "Fixtures", "fixtures", "Widget.java"));
        }

        /// <summary>
        /// Resolves a JDK executable without searching the user's PATH.
        /// </summary>
        /// <param name="tool">The executable's name without its Windows extension.</param>
        /// <returns>The absolute configured JDK executable path.</returns>
        public string ToolPath(string tool)
        {
            return Path.Combine(JavaHome, "bin", tool + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        }

        /// <summary>
        /// Creates worker arguments that preserve the supervisor's complete toolchain.
        /// </summary>
        /// <param name="caseName">The case to run in the new process.</param>
        /// <param name="workspace">The already prepared temporary fixture workspace.</param>
        /// <returns>The worker application arguments.</returns>
        public IReadOnlyList<string> WorkerArguments(string caseName, string workspace)
        {
            return new[]
            {
                "--worker", caseName,
                "--workspace", workspace,
                "--repo-root", RepositoryRoot,
                "--unity-editor-data", UnityEditorData,
                "--android-sdk", AndroidSdk,
                "--java-home", JavaHome,
                "--extractor", ExtractorPath,
                "--api-level", ApiLevel.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--timeout-seconds", TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--tool-timeout-seconds", ToolTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
        }

        /// <summary>
        /// Reads a path embedded by the standalone project's AssemblyMetadata items.
        /// </summary>
        /// <param name="key">The metadata key to locate.</param>
        /// <returns>The absolute configured path.</returns>
        /// <exception cref="InvalidOperationException">The build did not supply the key.</exception>
        private static string ReadBuildMetadata(string key)
        {
            foreach (var attribute in typeof(HarnessOptions).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            {
                if (attribute.Key == key && !string.IsNullOrEmpty(attribute.Value))
                {
                    return Path.GetFullPath(attribute.Value);
                }
            }
            throw new InvalidOperationException("Missing build metadata: " + key + ".");
        }

        /// <summary>
        /// Parses a bounded positive integer command-line value.
        /// </summary>
        /// <param name="value">The supplied decimal value.</param>
        /// <param name="argument">The option name used for error reporting.</param>
        /// <returns>The positive value, limited to one hour for timeouts.</returns>
        /// <exception cref="ArgumentException">The value is outside the accepted range.</exception>
        private static int ParsePositive(string value, string argument)
        {
            if (!int.TryParse(value, out var parsed) || parsed < 1 || parsed > 3600)
            {
                throw new ArgumentException(argument + " must be an integer from 1 through 3600.");
            }
            return parsed;
        }

        /// <summary>
        /// Checks a required input without creating a substitute.
        /// </summary>
        /// <param name="path">The required absolute input path.</param>
        /// <exception cref="FileNotFoundException">The specified input does not exist.</exception>
        private static void RequireFile(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Required validation input is missing: " + path, path);
            }
        }
    }

    /// <summary>
    /// Runs tool and worker processes with a finite wall-clock limit.
    /// </summary>
    internal static class ProcessRunner
    {
        /// <summary>
        /// Executes a process, drains both output streams, and kills its process tree on timeout.
        /// </summary>
        /// <param name="executable">The absolute executable path.</param>
        /// <param name="arguments">Arguments passed without shell interpolation.</param>
        /// <param name="workingDirectory">The process's absolute working directory.</param>
        /// <param name="timeoutSeconds">The maximum execution duration.</param>
        /// <returns>The exit code and captured standard output and error.</returns>
        /// <exception cref="TimeoutException">The process exceeded its deadline.</exception>
        /// <exception cref="InvalidOperationException">The process could not start.</exception>
        public static ProcessResult Run(string executable, IReadOnlyList<string> arguments, string workingDirectory, int timeoutSeconds)
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start " + executable + ".");
            var stdout = ReadBoundedAsync(process.StandardOutput);
            var stderr = ReadBoundedAsync(process.StandardError);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                Task.WhenAll(process.WaitForExitAsync(cancellation.Token), stdout, stderr)
                    .WaitAsync(cancellation.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
                catch (InvalidOperationException)
                {
                    // The child can exit concurrently with the timeout cancellation.
                }
                throw new TimeoutException(executable + " exceeded " + timeoutSeconds + " seconds; its process tree was terminated.");
            }
            return new ProcessResult(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
        }

        /// <summary>
        /// Drains a stream while retaining at most one MiB for failure reporting.
        /// </summary>
        /// <param name="reader">The redirected output reader.</param>
        /// <returns>The bounded captured output with a truncation marker when necessary.</returns>
        private static async Task<string> ReadBoundedAsync(StreamReader reader)
        {
            const int maximumCharacters = 1024 * 1024;
            var captured = new System.Text.StringBuilder();
            var buffer = new char[4096];
            var truncated = false;
            while (true)
            {
                var count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }
                var retained = Math.Min(count, maximumCharacters - captured.Length);
                if (retained > 0)
                {
                    captured.Append(buffer, 0, retained);
                }
                truncated |= retained < count;
            }
            if (truncated)
            {
                captured.AppendLine("[additional output truncated]");
            }
            return captured.ToString();
        }
    }

    /// <summary>
    /// Holds the finite output captured from one tool or worker process.
    /// </summary>
    internal sealed class ProcessResult
    {
        /// <summary>
        /// Gets the child's exit code.
        /// </summary>
        public int ExitCode { get; }

        /// <summary>
        /// Gets the captured standard output.
        /// </summary>
        public string StandardOutput { get; }

        /// <summary>
        /// Gets the captured standard error.
        /// </summary>
        public string StandardError { get; }

        /// <summary>
        /// Creates an immutable process result.
        /// </summary>
        /// <param name="exitCode">The process's exit code.</param>
        /// <param name="standardOutput">The captured normal output.</param>
        /// <param name="standardError">The captured diagnostic output.</param>
        public ProcessResult(int exitCode, string standardOutput, string standardError)
        {
            ExitCode = exitCode;
            StandardOutput = standardOutput;
            StandardError = standardError;
        }
    }
}
