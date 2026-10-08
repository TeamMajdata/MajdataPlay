#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MajdataPlay.Tests.AndroidJavaGeneratorValidation
{
    /// <summary>
    /// Stages tracked Java fixtures exclusively below the ignored validation Temp directory.
    /// </summary>
    internal sealed class FixtureWorkspace
    {
        /// <summary>
        /// Gets the explicit validation configuration.
        /// </summary>
        public HarnessOptions Options { get; }

        /// <summary>
        /// Gets the absolute workspace containing fixture sources and compiled archives.
        /// </summary>
        public string Root { get; }

        /// <summary>
        /// Gets the copied Java source tree, never the real Assets directory.
        /// </summary>
        public string SourceDirectory
        {
            get
            {
                return Path.Combine(Root, "sources");
            }
        }

        /// <summary>
        /// Gets the javac output directory used by class-file tests.
        /// </summary>
        public string ClassesDirectory
        {
            get
            {
                return Path.Combine(Root, "classes");
            }
        }

        /// <summary>
        /// Gets the fixture archive used by jar tests.
        /// </summary>
        public string JarPath
        {
            get
            {
                return Path.Combine(Root, "fixtures.jar");
            }
        }

        /// <summary>
        /// Gets the class root missing the dependent fixture's superclass.
        /// </summary>
        public string MissingDependencyDirectory
        {
            get
            {
                return Path.Combine(Root, "missing-dependency");
            }
        }

        /// <summary>
        /// Gets the source documents supplied to every positive input mode.
        /// </summary>
        public string[] DocumentationPaths
        {
            get
            {
                return new[] { SourceDirectory };
            }
        }

        /// <summary>
        /// Opens a workspace and verifies its location before any write operation.
        /// </summary>
        /// <param name="options">The configured toolchain and repository paths.</param>
        /// <param name="root">The prepared or new absolute temporary workspace.</param>
        /// <exception cref="ArgumentException">The workspace is outside the validation Temp root.</exception>
        public FixtureWorkspace(HarnessOptions options, string root)
        {
            Options = options;
            Root = Path.GetFullPath(root);
            var allowedRoot = Path.GetFullPath(Path.Combine(options.RepositoryRoot, "Temp", "AndroidJavaGeneratorValidation", "runs"));
            var relative = Path.GetRelativePath(allowedRoot, Root);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || relative == ".")
            {
                throw new ArgumentException("Workspace must be a child of " + allowedRoot + ".", nameof(root));
            }
        }

        /// <summary>
        /// Creates a unique fixture workspace without deleting any existing files.
        /// </summary>
        /// <param name="options">The configured toolchain and repository paths.</param>
        /// <returns>The prepared source, class-file, jar, and missing-dependency fixtures.</returns>
        /// <exception cref="InvalidOperationException">javac or jar reports a failure.</exception>
        public static FixtureWorkspace Prepare(HarnessOptions options)
        {
            var runName = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N");
            var workspace = new FixtureWorkspace(options, Path.Combine(options.RepositoryRoot, "Temp", "AndroidJavaGeneratorValidation", "runs", runName));
            Directory.CreateDirectory(workspace.Root);
            var trackedFixtures = Path.Combine(options.RepositoryRoot, "Tools", "Tests", "AndroidJavaGeneratorValidation", "Fixtures");
            CopyJavaSources(trackedFixtures, workspace.SourceDirectory);
            workspace.CompileSources(workspace.SourceDirectory, workspace.ClassesDirectory);
            workspace.RequireSuccess(options.ToolPath("jar"), new[] { "cf", workspace.JarPath, "-C", workspace.ClassesDirectory, "." }, "jar");
            var target = Path.Combine(workspace.MissingDependencyDirectory, "fixtures", "NeedsDependency.class");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(workspace.ClassesDirectory, "fixtures", "NeedsDependency.class"), target);
            return workspace;
        }

        /// <summary>
        /// Lists the bounded fixture source inputs in ordinal, deterministic order.
        /// </summary>
        /// <returns>The absolute Java source file paths.</returns>
        public string[] SourceFiles()
        {
            return Directory.GetFiles(SourceDirectory, "*.java", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Lists the fixture class-file inputs without supplying a jar fallback.
        /// </summary>
        /// <returns>The absolute compiled class file paths.</returns>
        public string[] ClassFiles()
        {
            return Directory.GetFiles(ClassesDirectory, "*.class", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Creates a private source copy for the source-change invalidation case.
        /// </summary>
        /// <returns>The private absolute Java source directory.</returns>
        public string CreateRefreshSources()
        {
            var target = Path.Combine(Root, "source-refresh", "sources");
            CopyJavaSources(SourceDirectory, target);
            return target;
        }

        /// <summary>
        /// Creates a bounded per-case artifact directory within the verified workspace.
        /// </summary>
        /// <param name="caseName">The simple case identifier.</param>
        /// <returns>The absolute artifact directory path.</returns>
        /// <exception cref="ArgumentException">The name contains path separators.</exception>
        public string CaseDirectory(string caseName)
        {
            if (caseName.Length == 0 || caseName.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || caseName == "..")
            {
                throw new ArgumentException("Case names must be simple file-name components.", nameof(caseName));
            }
            var path = Path.Combine(Root, "results", caseName);
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>
        /// Compiles Java 8 bytecode with parameter names and the selected Android boot classpath.
        /// </summary>
        /// <param name="sourceDirectory">The isolated fixture source root.</param>
        /// <param name="classesDirectory">The isolated output root for class files.</param>
        /// <exception cref="InvalidOperationException">javac reports a failure.</exception>
        private void CompileSources(string sourceDirectory, string classesDirectory)
        {
            Directory.CreateDirectory(classesDirectory);
            var arguments = new List<string>
            {
                "-encoding", "UTF-8",
                "-source", "8", "-target", "8",
                "-parameters",
                "-bootclasspath", Options.AndroidJar,
                "-d", classesDirectory
            };
            arguments.AddRange(Directory.GetFiles(sourceDirectory, "*.java", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal));
            RequireSuccess(Options.ToolPath("javac"), arguments, "javac");
        }

        /// <summary>
        /// Runs a bounded preparation tool and saves its diagnostics for inspection.
        /// </summary>
        /// <param name="executable">The absolute configured tool path.</param>
        /// <param name="arguments">The shell-free tool arguments.</param>
        /// <param name="toolName">The preparation log's simple file-name prefix.</param>
        /// <exception cref="InvalidOperationException">The tool exits unsuccessfully.</exception>
        private void RequireSuccess(string executable, IReadOnlyList<string> arguments, string toolName)
        {
            var result = ProcessRunner.Run(executable, arguments, Root, Options.ToolTimeoutSeconds);
            var log = result.StandardOutput + Environment.NewLine + result.StandardError;
            File.WriteAllText(Path.Combine(Root, toolName + ".log"), log, new UTF8Encoding(false));
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(toolName + " failed with exit code " + result.ExitCode + ":" + Environment.NewLine + log);
            }
        }

        /// <summary>
        /// Copies only the small tracked Java fixtures, retaining their package directories.
        /// </summary>
        /// <param name="sourceRoot">The existing fixture source tree.</param>
        /// <param name="targetRoot">The new isolated temporary source tree.</param>
        /// <exception cref="InvalidOperationException">The fixture count or size exceeds its bound.</exception>
        private static void CopyJavaSources(string sourceRoot, string targetRoot)
        {
            var files = Directory.GetFiles(sourceRoot, "*.java", SearchOption.AllDirectories);
            if (files.Length == 0 || files.Length > 64)
            {
                throw new InvalidOperationException("Expected 1 through 64 small tracked Java fixtures.");
            }
            foreach (var source in files)
            {
                if (new FileInfo(source).Length > 1024 * 1024)
                {
                    throw new InvalidOperationException("Fixture exceeds one MiB: " + source);
                }
                var target = Path.Combine(targetRoot, Path.GetRelativePath(sourceRoot, source));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: false);
            }
        }
    }

    /// <summary>
    /// Restores temporarily overridden generator environment settings after a negative case.
    /// </summary>
    internal sealed class EnvironmentOverride : IDisposable
    {
        /// <summary>
        /// Stores the original process environment values.
        /// </summary>
        private readonly Dictionary<string, string?> _originalValues = new Dictionary<string, string?>(StringComparer.Ordinal);

        /// <summary>
        /// Applies the specified overrides and remembers their previous values.
        /// </summary>
        /// <param name="values">The environment keys and their temporary values.</param>
        public EnvironmentOverride(IReadOnlyDictionary<string, string?> values)
        {
            foreach (var value in values)
            {
                _originalValues.Add(value.Key, Environment.GetEnvironmentVariable(value.Key));
                Environment.SetEnvironmentVariable(value.Key, value.Value);
            }
        }

        /// <summary>
        /// Restores every overridden process environment value.
        /// </summary>
        public void Dispose()
        {
            foreach (var value in _originalValues)
            {
                Environment.SetEnvironmentVariable(value.Key, value.Value);
            }
        }
    }
}
