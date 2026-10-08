#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace MajdataPlay.SourceGenerators.AndroidJava
{
    /// <summary>Holds merged attribute options before filesystem and toolchain resolution.</summary>
    internal sealed class JavaApiOptions
    {
        /// <summary>Gets the source files, archives, or source directories.</summary>
        internal List<string> Sources { get; } = new List<string>();

        /// <summary>Gets the explicit classpath entries.</summary>
        internal List<string> ClassPath { get; } = new List<string>();

        /// <summary>Gets optional Java documentation roots.</summary>
        internal List<string> DocumentationPaths { get; } = new List<string>();

        /// <summary>Gets or sets the explicitly selected Android SDK root.</summary>
        internal string? AndroidSdkPath { get; set; }

        /// <summary>Gets or sets the explicitly selected JDK root.</summary>
        internal string? JavaHome { get; set; }

        /// <summary>Gets or sets the requested Android API, or zero for the highest installed platform.</summary>
        internal int ApiLevel { get; set; }

        /// <summary>Gets or sets whether inherited public members are extracted.</summary>
        internal bool IncludeInheritedMembers { get; set; } = true;

        /// <summary>Copies global options and applies explicitly supplied local overrides.</summary>
        /// <param name="global">The assembly-level defaults.</param>
        /// <param name="attribute">The class-level JavaClass attribute.</param>
        /// <returns>The merged options without sharing mutable arrays.</returns>
        internal static JavaApiOptions Merge(JavaApiOptions global, AttributeData attribute)
        {
            var options = new JavaApiOptions
            {
                AndroidSdkPath = global.AndroidSdkPath,
                JavaHome = global.JavaHome,
                ApiLevel = global.ApiLevel,
                IncludeInheritedMembers = global.IncludeInheritedMembers
            };
            options.Sources.AddRange(global.Sources);
            options.ClassPath.AddRange(global.ClassPath);
            options.DocumentationPaths.AddRange(global.DocumentationPaths);
            options.Apply(attribute);
            return options;
        }

        /// <summary>Applies named attribute arguments, appending arrays and replacing scalars.</summary>
        /// <param name="attribute">The semantic attribute data.</param>
        /// <exception cref="GeneratorException">An option is malformed.</exception>
        internal void Apply(AttributeData attribute)
        {
            foreach (var argument in attribute.NamedArguments)
            {
                switch (argument.Key)
                {
                    case "Sources":
                        AddPaths(Sources, argument.Value, argument.Key);
                        break;
                    case "ClassPath":
                        AddPaths(ClassPath, argument.Value, argument.Key);
                        break;
                    case "DocumentationPaths":
                        AddPaths(DocumentationPaths, argument.Value, argument.Key);
                        break;
                    case "AndroidSdkPath":
                        AndroidSdkPath = argument.Value.Value as string;
                        break;
                    case "JavaHome":
                        JavaHome = argument.Value.Value as string;
                        break;
                    case "ApiLevel":
                        ApiLevel = argument.Value.Value is int value ? value : 0;
                        break;
                    case "IncludeInheritedMembers":
                        IncludeInheritedMembers = argument.Value.Value is bool enabled && enabled;
                        break;
                }
            }
        }

        /// <summary>Appends valid non-null paths from an attribute array.</summary>
        /// <param name="paths">The destination path list.</param>
        /// <param name="constant">The attribute array constant.</param>
        /// <param name="option">The option name for diagnostics.</param>
        /// <exception cref="GeneratorException">The array contains an empty or non-string path.</exception>
        private static void AddPaths(List<string> paths, TypedConstant constant, string option)
        {
            if (constant.IsNull)
            {
                return;
            }
            if (constant.Kind != TypedConstantKind.Array)
            {
                throw JavaApiConfiguration.Invalid(option + " must be a string array.");
            }
            foreach (var item in constant.Values)
            {
                if (!(item.Value is string path) || string.IsNullOrWhiteSpace(path))
                {
                    throw JavaApiConfiguration.Invalid(option + " contains an empty or non-string path.");
                }
                paths.Add(path);
            }
        }
    }

    /// <summary>Represents the complete resolved inputs for one Java extraction process.</summary>
    internal sealed class JavaApiConfiguration
    {
        /// <summary>Gets the project root used for relative paths and process execution.</summary>
        internal string ProjectRoot { get; }

        /// <summary>Gets the absolute JDK java executable path.</summary>
        internal string JavaExecutable { get; }

        /// <summary>Gets the absolute source-file-mode Java helper path.</summary>
        internal string ExtractorPath { get; }

        /// <summary>Gets the selected platform's actual android.jar.</summary>
        internal string AndroidJar { get; }

        /// <summary>Gets the deterministically ordered .java source paths.</summary>
        internal IReadOnlyList<string> Sources { get; }

        /// <summary>Gets the classpath entries in preserved dependency-resolution order.</summary>
        internal IReadOnlyList<string> ClassPath { get; }

        /// <summary>Gets the explicit documentation paths followed by discovered SDK sources.</summary>
        internal IReadOnlyList<string> DocumentationPaths { get; }

        /// <summary>Gets whether public inherited members are requested.</summary>
        internal bool IncludeInheritedMembers { get; }

        /// <summary>Gets the maximum helper execution duration in milliseconds.</summary>
        internal int TimeoutMilliseconds { get; }

        /// <summary>Gets a collision-free configuration key used only within this compilation.</summary>
        internal string Key { get; }

        /// <summary>Creates an immutable snapshot of resolved extraction inputs.</summary>
        /// <param name="root">The project root.</param>
        /// <param name="java">The Java executable.</param>
        /// <param name="extractor">The Java helper source.</param>
        /// <param name="androidJar">The selected Android platform archive.</param>
        /// <param name="sources">The Java source files.</param>
        /// <param name="classPath">The dependency search paths.</param>
        /// <param name="documentation">The documentation inputs.</param>
        /// <param name="includeInherited">Whether inherited public members are requested.</param>
        /// <param name="timeout">The helper timeout in milliseconds.</param>
        private JavaApiConfiguration(string root, string java, string extractor, string androidJar,
            string[] sources, string[] classPath, string[] documentation, bool includeInherited, int timeout)
        {
            ProjectRoot = root;
            JavaExecutable = java;
            ExtractorPath = extractor;
            AndroidJar = androidJar;
            Sources = sources;
            ClassPath = classPath;
            DocumentationPaths = documentation;
            IncludeInheritedMembers = includeInherited;
            TimeoutMilliseconds = timeout;
            var key = new StringBuilder();
            AddKey(key, root);
            AddKey(key, java);
            AddKey(key, extractor);
            AddKey(key, androidJar);
            AddKey(key, includeInherited.ToString());
            AddKey(key, timeout.ToString(CultureInfo.InvariantCulture));
            AddPathsKey(key, sources);
            AddPathsKey(key, classPath);
            AddPathsKey(key, documentation);
            Key = key.ToString();
        }

        /// <summary>Resolves merged attribute options into validated helper inputs.</summary>
        /// <param name="options">The merged assembly and wrapper options.</param>
        /// <param name="compilation">The compilation whose references may locate Unity.</param>
        /// <param name="tree">The wrapper declaration's syntax tree.</param>
        /// <param name="cancellationToken">The compilation cancellation token.</param>
        /// <returns>The resolved extraction configuration.</returns>
        /// <exception cref="GeneratorException">A path, platform, or toolchain is invalid.</exception>
        /// <exception cref="IOException">A configured input cannot be inspected.</exception>
        /// <exception cref="OperationCanceledException">Compilation has been canceled.</exception>
        internal static JavaApiConfiguration Resolve(JavaApiOptions options, Compilation compilation, SyntaxTree tree,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = FindProjectRoot(tree.FilePath);
            var unityData = FindUnityData(compilation, root, cancellationToken);
            var sdk = SelectToolRoot(options.AndroidSdkPath, root,
                new[] { "UNITY_ANDROID_SDK", "ANDROID_SDK_ROOT", "ANDROID_HOME" }, unityData, "SDK");
            var jdk = SelectToolRoot(options.JavaHome, root,
                new[] { "UNITY_JAVA_HOME", "JAVA_HOME" }, unityData, "OpenJDK");
            var java = Path.Combine(jdk, "bin", Path.DirectorySeparatorChar == '\\' ? "java.exe" : "java");
            if (!File.Exists(java) || !File.Exists(Path.Combine(jdk, "bin", Path.DirectorySeparatorChar == '\\' ? "javac.exe" : "javac")))
            {
                throw Invalid("JavaHome '" + jdk + "' must contain a JDK with bin/java and bin/javac (Java 11 or newer).");
            }
            var androidJar = SelectAndroidJar(sdk, options.ApiLevel, cancellationToken);
            var extractorSetting = Environment.GetEnvironmentVariable("MAJDATA_JAVA_EXTRACTOR");
            var extractor = ResolvePath(string.IsNullOrWhiteSpace(extractorSetting)
                ? "Tools/AndroidJavaGenerator/Java/JavaApiExtractor.java" : extractorSetting!, root);
            if (!File.Exists(extractor))
            {
                throw Invalid("Java API helper does not exist: '" + extractor + "'. Set MAJDATA_JAVA_EXTRACTOR or install the repository helper.");
            }
            var sources = new HashSet<string>(PathComparer());
            var classPath = new List<string>();
            foreach (var entry in options.Sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = ResolvePath(entry, root);
                if (Directory.Exists(path))
                {
                    AddJavaSources(path, sources, cancellationToken);
                }
                else if (File.Exists(path) && HasExtension(path, ".java"))
                {
                    sources.Add(path);
                }
                else
                {
                    AddClassPath(path, classPath, cancellationToken, "Sources");
                }
            }
            foreach (var entry in options.ClassPath)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddClassPath(ResolvePath(entry, root), classPath, cancellationToken, "ClassPath");
            }
            var documentation = new List<string>();
            foreach (var entry in options.DocumentationPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = ResolvePath(entry, root);
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    throw Invalid("Documentation path does not exist: '" + path + "'.");
                }
                documentation.Add(path);
            }
            var platform = Path.GetDirectoryName(androidJar)!;
            var platformSources = Path.Combine(platform, "android-stubs-src.jar");
            if (File.Exists(platformSources))
            {
                documentation.Add(platformSources);
            }
            var sdkSources = Path.Combine(sdk, "sources", Path.GetFileName(platform));
            if (Directory.Exists(sdkSources))
            {
                documentation.Add(sdkSources);
            }
            return new JavaApiConfiguration(root, java, extractor, androidJar, Order(sources), OrderedPaths(classPath),
                OrderedPaths(documentation), options.IncludeInheritedMembers, ResolveTimeout());
        }

        /// <summary>Finds a Unity project root from a declaration path, otherwise uses the current directory.</summary>
        /// <param name="filePath">The declaration's syntax-tree path.</param>
        /// <returns>The absolute project root.</returns>
        internal static string FindProjectRoot(string filePath)
        {
            if (!string.IsNullOrEmpty(filePath) && Path.IsPathRooted(filePath))
            {
                var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
                while (directory != null)
                {
                    if (Directory.Exists(Path.Combine(directory.FullName, "Assets")) &&
                        Directory.Exists(Path.Combine(directory.FullName, "ProjectSettings")))
                    {
                        return directory.FullName;
                    }
                    directory = directory.Parent;
                }
            }
            return Path.GetFullPath(Directory.GetCurrentDirectory());
        }

        /// <summary>Expands environment variables and anchors a configured path to the project root.</summary>
        /// <param name="configuredPath">The attribute or environment path.</param>
        /// <param name="root">The project root for relative paths.</param>
        /// <returns>The normalized absolute path.</returns>
        /// <exception cref="GeneratorException">The path is empty, invalid, or contains an unresolved variable.</exception>
        internal static string ResolvePath(string configuredPath, string root)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                throw Invalid("A configured path is empty.");
            }
            var expanded = Environment.ExpandEnvironmentVariables(configuredPath);
            // A dollar within a Java identifier (Outer$Inner.class) is literal, not a shell variable.
            expanded = Regex.Replace(expanded, @"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}|(?<![\p{L}\p{Nd}_$])\$(?<name>[A-Za-z_][A-Za-z0-9_]*)", match =>
            {
                var name = match.Groups["name"].Value;
                var value = Environment.GetEnvironmentVariable(name);
                if (value == null)
                {
                    throw Invalid("Environment variable '" + name + "' in path '" + configuredPath + "' is not defined.");
                }
                return value;
            });
            if (Regex.IsMatch(expanded, @"%[A-Za-z_][A-Za-z0-9_]*%"))
            {
                throw Invalid("An environment variable in path '" + configuredPath + "' is not defined.");
            }
            try
            {
                return Path.GetFullPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(root, expanded));
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException)
            {
                throw Invalid("Invalid configured path '" + configuredPath + "': " + exception.Message);
            }
        }

        /// <summary>Creates an actionable extraction-configuration failure.</summary>
        /// <param name="message">The invalid configuration explanation.</param>
        /// <returns>The failure to report at the wrapper attribute.</returns>
        internal static GeneratorException Invalid(string message)
        {
            return new GeneratorException(GeneratorDiagnostics.InvalidConfiguration, message);
        }

        /// <summary>Locates Unity's Data directory from an editor path or managed references.</summary>
        /// <param name="compilation">The compilation carrying Unity assembly references.</param>
        /// <param name="root">The project root used for environment-path resolution.</param>
        /// <param name="cancellationToken">The compilation cancellation token.</param>
        /// <returns>The editor Data directory, or null if no editor was identified.</returns>
        /// <exception cref="OperationCanceledException">Compilation has been canceled.</exception>
        private static string? FindUnityData(Compilation compilation, string root, CancellationToken cancellationToken)
        {
            var editor = Environment.GetEnvironmentVariable("UNITY_EDITOR_PATH");
            if (!string.IsNullOrWhiteSpace(editor))
            {
                var path = ResolvePath(editor!, root);
                var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path)!;
                var ancestor = new DirectoryInfo(directory);
                while (ancestor != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var candidate in new[] { Path.Combine(ancestor.FullName, "Data"), Path.Combine(ancestor.FullName, "Contents"), ancestor.FullName })
                    {
                        if (Directory.Exists(Path.Combine(candidate, "PlaybackEngines", "AndroidPlayer")))
                        {
                            return candidate;
                        }
                    }
                    ancestor = ancestor.Parent;
                }
            }
            foreach (var reference in compilation.References.OfType<PortableExecutableReference>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = reference.FilePath;
                if (string.IsNullOrEmpty(file) || !Path.IsPathRooted(file))
                {
                    continue;
                }
                var directory = new DirectoryInfo(Path.GetDirectoryName(file)!);
                while (directory != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.Equals(directory.Name, "Managed", StringComparison.OrdinalIgnoreCase))
                    {
                        var data = directory.Parent;
                        if (data != null && Directory.Exists(Path.Combine(data.FullName, "PlaybackEngines", "AndroidPlayer")))
                        {
                            return data.FullName;
                        }
                    }
                    directory = directory.Parent;
                }
            }
            return null;
        }

        /// <summary>Selects an explicit, environmental, or Unity-bundled SDK/JDK root.</summary>
        /// <param name="explicitPath">The attribute override, if any.</param>
        /// <param name="root">The project root.</param>
        /// <param name="variables">The environment variables in precedence order.</param>
        /// <param name="unityData">The identified Unity editor Data directory.</param>
        /// <param name="component">The AndroidPlayer SDK or OpenJDK component name.</param>
        /// <returns>The existing absolute toolchain root.</returns>
        /// <exception cref="GeneratorException">The selected toolchain is missing.</exception>
        private static string SelectToolRoot(string? explicitPath, string root, string[] variables, string? unityData, string component)
        {
            string? selected = explicitPath;
            if (string.IsNullOrWhiteSpace(selected))
            {
                foreach (var variable in variables)
                {
                    selected = Environment.GetEnvironmentVariable(variable);
                    if (!string.IsNullOrWhiteSpace(selected))
                    {
                        break;
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(selected) && unityData != null)
            {
                selected = Path.Combine(unityData, "PlaybackEngines", "AndroidPlayer", component);
            }
            if (string.IsNullOrWhiteSpace(selected))
            {
                throw Invalid("Cannot locate Android " + component + ". Configure its attribute path, " +
                    string.Join("/", variables) + ", or UNITY_EDITOR_PATH.");
            }
            var path = ResolvePath(selected!, root);
            if (!Directory.Exists(path))
            {
                throw Invalid("Android " + component + " directory does not exist: '" + path + "'.");
            }
            return path;
        }

        /// <summary>Selects a real installed Android platform rather than an SDK directory label.</summary>
        /// <param name="sdk">The SDK root.</param>
        /// <param name="apiLevel">The requested API, or zero for the highest installed API.</param>
        /// <param name="cancellationToken">The compilation cancellation token.</param>
        /// <returns>The selected existing android.jar.</returns>
        /// <exception cref="GeneratorException">The requested platform is not installed.</exception>
        /// <exception cref="OperationCanceledException">Compilation has been canceled.</exception>
        private static string SelectAndroidJar(string sdk, int apiLevel, CancellationToken cancellationToken)
        {
            if (apiLevel < 0)
            {
                throw Invalid("ApiLevel cannot be negative.");
            }
            var platforms = Path.Combine(sdk, "platforms");
            if (apiLevel > 0)
            {
                var requested = Path.Combine(platforms, "android-" + apiLevel.ToString(CultureInfo.InvariantCulture), "android.jar");
                if (!File.Exists(requested))
                {
                    throw Invalid("Android API " + apiLevel.ToString(CultureInfo.InvariantCulture) + " is not installed: '" + requested + "'.");
                }
                return requested;
            }
            var highest = -1;
            string? selected = null;
            if (Directory.Exists(platforms))
            {
                foreach (var directory in Directory.EnumerateDirectories(platforms))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(directory);
                    if (name.StartsWith("android-", StringComparison.Ordinal) &&
                        int.TryParse(name.Substring(8), NumberStyles.None, CultureInfo.InvariantCulture, out var level) && level > highest && level > 0)
                    {
                        var jar = Path.Combine(directory, "android.jar");
                        if (File.Exists(jar))
                        {
                            highest = level;
                            selected = jar;
                        }
                    }
                }
            }
            if (selected == null)
            {
                throw Invalid("No installed platforms/android-N/android.jar was found under SDK '" + sdk + "'.");
            }
            return selected;
        }

        /// <summary>Recursively enumerates only .java files without following directory symlink loops.</summary>
        /// <param name="root">The source directory.</param>
        /// <param name="sources">The deduplicated destination set.</param>
        /// <param name="cancellationToken">The compilation cancellation token.</param>
        /// <exception cref="IOException">A source directory cannot be read.</exception>
        /// <exception cref="OperationCanceledException">Compilation has been canceled.</exception>
        private static void AddJavaSources(string root, HashSet<string> sources, CancellationToken cancellationToken)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = pending.Pop();
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (HasExtension(file, ".java"))
                    {
                        sources.Add(Path.GetFullPath(file));
                    }
                }
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push(child);
                    }
                }
            }
        }

        /// <summary>Adds a directory, JAR, or package-validated class-file root to the classpath.</summary>
        /// <param name="path">The absolute configured path.</param>
        /// <param name="classPath">The deduplicated classpath set.</param>
        /// <param name="cancellationToken">The compilation cancellation token.</param>
        /// <param name="option">The option name used in diagnostics.</param>
        /// <exception cref="GeneratorException">The input is missing or unsupported.</exception>
        private static void AddClassPath(string path, ICollection<string> classPath, CancellationToken cancellationToken, string option)
        {
            if (Directory.Exists(path) || (File.Exists(path) && HasExtension(path, ".jar")))
            {
                classPath.Add(path);
            }
            else if (File.Exists(path) && HasExtension(path, ".class"))
            {
                classPath.Add(JavaClassFile.GetClassPathRoot(path, cancellationToken));
            }
            else
            {
                throw Invalid(option + " input is missing or unsupported: '" + path + "'. Expected " +
                    (option == "Sources" ? "a .java file, source directory, .jar, or .class." : "a classpath directory, .jar, or .class."));
            }
        }

        /// <summary>Compares extensions using Java input conventions on all hosts.</summary>
        /// <param name="path">The file path.</param>
        /// <param name="extension">The expected dotted extension.</param>
        /// <returns>Whether the extension matches.</returns>
        private static bool HasExtension(string path, string extension)
        {
            return string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Gets the filesystem-appropriate path deduplication comparer.</summary>
        /// <returns>The platform path comparer.</returns>
        private static StringComparer PathComparer()
        {
            return Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        }

        /// <summary>Orders inputs deterministically for stable process grouping and generated output.</summary>
        /// <param name="paths">The deduplicated paths.</param>
        /// <returns>The ordered path snapshot.</returns>
        private static string[] Order(HashSet<string> paths)
        {
            return paths.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        }

        /// <summary>Deduplicates dependency and documentation paths without changing resolution precedence.</summary>
        /// <param name="paths">The merged paths in user-specified order.</param>
        /// <returns>The ordered, deduplicated path snapshot.</returns>
        private static string[] OrderedPaths(IEnumerable<string> paths)
        {
            return paths.Distinct(PathComparer()).ToArray();
        }

        /// <summary>Reads the optional helper timeout override.</summary>
        /// <returns>The timeout in milliseconds, defaulting to 120 seconds.</returns>
        /// <exception cref="GeneratorException">The override is not an integer between 1 and 3600.</exception>
        private static int ResolveTimeout()
        {
            var configured = Environment.GetEnvironmentVariable("MAJDATA_JAVA_EXTRACTOR_TIMEOUT_SECONDS");
            if (string.IsNullOrWhiteSpace(configured))
            {
                return 120000;
            }
            if (!int.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds < 1 || seconds > 3600)
            {
                throw Invalid("MAJDATA_JAVA_EXTRACTOR_TIMEOUT_SECONDS must be an integer between 1 and 3600.");
            }
            return seconds * 1000;
        }

        /// <summary>Adds a count-delimited path array to a grouping key.</summary>
        /// <param name="key">The key builder.</param>
        /// <param name="paths">The resolved path array.</param>
        private static void AddPathsKey(StringBuilder key, string[] paths)
        {
            AddKey(key, paths.Length.ToString(CultureInfo.InvariantCulture));
            foreach (var path in paths)
            {
                AddKey(key, path);
            }
        }

        /// <summary>Adds one length-prefixed configuration value to a grouping key.</summary>
        /// <param name="key">The key builder.</param>
        /// <param name="value">The configuration value.</param>
        private static void AddKey(StringBuilder key, string value)
        {
            key.Append(value.Length.ToString(CultureInfo.InvariantCulture));
            key.Append(':');
            key.Append(value);
        }
    }
}
