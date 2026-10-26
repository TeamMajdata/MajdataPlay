#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace MajdataPlay.Tests.AndroidJavaGeneratorValidation
{
    /// <summary>
    /// Supervises finite child-process regression cases against the production generator.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Runs the suite supervisor or exactly one explicitly selected worker case.
        /// </summary>
        /// <param name="arguments">The optional toolchain, timeout, and case-selection arguments.</param>
        /// <returns>Zero on success, one on regression failure, or two on invalid prerequisites.</returns>
        public static int Main(string[] arguments)
        {
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
                Console.InputEncoding = new UTF8Encoding(false);
                var options = HarnessOptions.Parse(arguments);
                if (options.ShowHelp)
                {
                    PrintHelp();
                    return 0;
                }
                if (options.ListCases)
                {
                    foreach (var name in RegressionCases.Names)
                    {
                        Console.WriteLine(name);
                    }
                    return 0;
                }
                options.ApplyEnvironment();
                options.Validate();
                if (options.WorkerCase is not null)
                {
                    return RunWorker(options);
                }
                return RunSuite(options);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 2;
            }
        }

        /// <summary>
        /// Runs one case and emits a compact assertion count for the suite summary.
        /// </summary>
        /// <param name="options">The supervisor's explicit worker configuration.</param>
        /// <returns>Zero when the case passes, otherwise one.</returns>
        private static int RunWorker(HarnessOptions options)
        {
            var caseName = options.WorkerCase!;
            try
            {
                if (options.WorkspacePath is null)
                {
                    throw new ArgumentException("Worker mode requires --workspace.");
                }
                var workspace = new FixtureWorkspace(options, options.WorkspacePath);
                RegressionCases.Run(caseName, workspace);
                Console.WriteLine("PASS " + caseName + " (" + Check.Count + " assertions)");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL " + caseName + ": " + exception);
                return 1;
            }
        }

        /// <summary>
        /// Prepares the fixtures once and runs each selected case with a process-tree deadline.
        /// </summary>
        /// <param name="options">The configured toolchain and finite timeouts.</param>
        /// <returns>Zero when all selected cases pass, otherwise one.</returns>
        private static int RunSuite(HarnessOptions options)
        {
            var cases = RegressionCases.Names.Where(name => options.Filter is null
                || name.Contains(options.Filter, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (cases.Length == 0)
            {
                throw new ArgumentException("No regression case matches --filter " + options.Filter + ".");
            }
            var workspace = FixtureWorkspace.Prepare(options);
            Console.WriteLine("Unity references: " + options.UnityEditorData);
            Console.WriteLine("Android boot classpath: " + options.AndroidJar);
            Console.WriteLine("Java home: " + options.JavaHome);
            Console.WriteLine("Production extractor: " + options.ExtractorPath);
            Console.WriteLine("Artifacts: " + workspace.Root);
            Console.WriteLine("Selected cases: " + cases.Length + "; maximum " + options.TimeoutSeconds + " seconds per case.");
            var summary = new StringBuilder();
            var failures = 0;
            var watch = Stopwatch.StartNew();
            foreach (var caseName in cases)
            {
                var caseWatch = Stopwatch.StartNew();
                var passed = false;
                try
                {
                    var child = StartWorker(options, workspace, caseName);
                    passed = child.ExitCode == 0;
                    var stdout = child.StandardOutput;
                    var stderr = child.StandardError;
                    File.WriteAllText(Path.Combine(workspace.CaseDirectory(caseName), "Worker.log"), stdout + "\r\n" + stderr, new UTF8Encoding(false));
                    if (!string.IsNullOrWhiteSpace(stdout))
                    {
                        Console.Write(stdout);
                    }
                    if (!string.IsNullOrWhiteSpace(stderr))
                    {
                        Console.Error.Write(stderr);
                    }
                    if (!passed && string.IsNullOrWhiteSpace(stderr))
                    {
                        Console.Error.WriteLine("FAIL " + caseName + " exited " + child.ExitCode + ".");
                    }
                }
                catch (Exception exception)
                {
                    var error = "FAIL " + caseName + ": " + exception;
                    Console.Error.WriteLine(error);
                    File.WriteAllText(Path.Combine(workspace.CaseDirectory(caseName), "Worker.log"), error, new UTF8Encoding(false));
                }
                if (!passed)
                {
                    failures++;
                }
                summary.Append(passed ? "PASS " : "FAIL ").Append(caseName).Append(" ")
                    .Append(caseWatch.Elapsed.TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append("s\r\n");
            }
            var final = (cases.Length - failures) + "/" + cases.Length + " cases passed in "
                + watch.Elapsed.TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "s."
                + " Managed validation only; Unity Editor, Player, device JNI, and packaging remain unverified.";
            summary.AppendLine(final);
            File.WriteAllText(Path.Combine(workspace.Root, "Summary.txt"), summary.ToString(), new UTF8Encoding(false));
            Console.WriteLine(final);
            return failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// Starts the same executable using an explicit dotnet host when necessary.
        /// </summary>
        /// <param name="options">The complete parent configuration.</param>
        /// <param name="workspace">The prepared fixture workspace shared read-only by most workers.</param>
        /// <param name="caseName">The single case to run.</param>
        /// <returns>The bounded worker result.</returns>
        /// <exception cref="TimeoutException">The worker exceeded its finite deadline.</exception>
        private static ProcessResult StartWorker(HarnessOptions options, FixtureWorkspace workspace, string caseName)
        {
            var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("The current executable path is unavailable.");
            var arguments = new List<string>();
            if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                arguments.Add(typeof(Program).Assembly.Location);
            }
            arguments.AddRange(options.WorkerArguments(caseName, workspace.Root));
            return ProcessRunner.Run(processPath, arguments, options.RepositoryRoot, options.TimeoutSeconds);
        }

        /// <summary>
        /// Prints the deliberately small standalone harness command surface.
        /// </summary>
        private static void PrintHelp()
        {
            Console.WriteLine("Standalone .NET 9 Android Java generator validation; not a Unity project build.");
            Console.WriteLine("--list                        List all finite regression cases.");
            Console.WriteLine("--filter <text>                Run matching cases only.");
            Console.WriteLine("--unity-editor-data <path>     Runtime Unity reference and discovery directory.");
            Console.WriteLine("--android-sdk <path>           SDK root (default UNITY_ANDROID_SDK or Unity SDK).");
            Console.WriteLine("--java-home <path>             JDK root (default UNITY_JAVA_HOME or Unity OpenJDK).");
            Console.WriteLine("--extractor <path>             Production JavaApiExtractor.java helper.");
            Console.WriteLine("--api-level <number>           Android platform API (default 36).");
            Console.WriteLine("--timeout-seconds <number>     Per-worker limit (default 180; maximum 3600).");
            Console.WriteLine("--tool-timeout-seconds <number> javac/jar limit (default 90; maximum 3600).");
            Console.WriteLine("--repo-root <path>             Repository relocation override.");
            Console.WriteLine("Build-time -p:UnityEditorData must also change when using a different Unity install.");
        }
    }
}
