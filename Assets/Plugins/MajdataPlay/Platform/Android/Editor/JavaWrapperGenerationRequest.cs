using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

#nullable enable

namespace MajdataPlay.Platform.Android.Editor
{
    /// <summary>
    /// Captures the inputs, paths, and process arguments for one independent Java wrapper refresh.
    /// </summary>
    /// <remarks>
    /// Creating a request only discovers files and builds an immutable execution plan. The controller
    /// builds the CLI and invokes project mode to generate, compile, and publish the runtime library.
    /// Every supported Unity source is included; missing source dependencies must be supplied as real
    /// Java metadata rather than omitting sources or inventing declarations.
    /// </remarks>
    internal sealed class JavaWrapperGenerationRequest
    {
        /// <summary>
        /// Gets the independent CLI project that the controller builds with the pinned .NET SDK.
        /// </summary>
        internal string ProjectPath { get; }

        /// <summary>
        /// Gets the compiled CLI assembly that the controller invokes directly through dotnet.
        /// </summary>
        internal string AssemblyPath { get; }

        /// <summary>
        /// Gets the CLI directory whose global.json selects the required .NET 9 SDK.
        /// </summary>
        internal string WorkingDirectory { get; }

        /// <summary>Gets the absolute Unity project root passed to the independent project pipeline.</summary>
        internal string ProjectRoot { get; }

        /// <summary>Gets the selected Unity Editor Data or macOS Contents directory.</summary>
        internal string UnityContentsPath { get; }

        /// <summary>Gets the Android SDK selected by the Editor's External Tools settings.</summary>
        internal string SdkDirectory { get; }

        /// <summary>
        /// Gets the unique tool-local output directory owned by this refresh invocation.
        /// </summary>
        internal string StagingDirectory { get; }

        /// <summary>
        /// Gets the independent library's source directory containing generated bindings and handwritten extensions.
        /// </summary>
        internal string OutputDirectory { get; }

        /// <summary>Gets the former generated asset directory whose owned sources must be retired after DLL publication.</summary>
        internal string PreviousOutputDirectory { get; }

        /// <summary>Gets the former Unity runtime source directory retired after successful DLL publication.</summary>
        internal string PreviousRuntimeDirectory { get; }

        /// <summary>Gets the Unity plugin directory receiving the compiled runtime library.</summary>
        internal string RuntimeAssemblyDirectory { get; }

        /// <summary>Gets the compiled runtime library published for Android and the Unity Editor.</summary>
        internal string RuntimeAssemblyPath { get; }

        /// <summary>
        /// Gets the persistent tool-local cache shared by official documentation requests.
        /// </summary>
        internal string DocumentationCacheDirectory { get; }

        /// <summary>
        /// Gets the namespace for generated JNI support. Android, Java, Unity, and project bindings
        /// use its Bindings child namespace.
        /// </summary>
        internal string RootNamespace { get; }

        /// <summary>
        /// Gets whether this invocation reads cached documentation without network requests.
        /// </summary>
        /// <remarks>
        /// Manual refreshes use online documentation by default. Automatic import refreshes should
        /// request offline documentation to avoid downloading the complete SDK reference repeatedly.
        /// The controller can retain this choice when resuming a refresh after a domain reload.
        /// </remarks>
        internal bool OfflineDocumentation { get; }

        /// <summary>
        /// Gets the deterministic, read-only list of additional Java source and bytecode files.
        /// </summary>
        internal IReadOnlyList<string> Inputs { get; }

        /// <summary>
        /// Gets dependency discovery notices, including unpinned cache versions and missing optional metadata.
        /// </summary>
        internal IReadOnlyList<string> Warnings { get; }

        /// <summary>
        /// Gets the safely quoted dotnet arguments that build the independent CLI in Release mode.
        /// </summary>
        internal string BuildArguments { get; }

        /// <summary>
        /// Gets the safely quoted dotnet arguments that invoke the built CLI assembly directly.
        /// </summary>
        internal string Arguments { get; }

        /// <summary>
        /// Creates an immutable process plan after its required input locations have been validated.
        /// </summary>
        /// <param name="projectRoot">The absolute Unity project root.</param>
        /// <param name="toolDirectory">The independent CLI directory containing its SDK pin.</param>
        /// <param name="unityContentsPath">The selected Unity Editor's Data or macOS Contents directory.</param>
        /// <param name="sdkDirectory">The Android SDK selected by the Editor's settings.</param>
        /// <param name="inputs">The discovered source and bytecode files.</param>
        /// <param name="warnings">The dependency discovery notices to report before execution.</param>
        /// <param name="offlineDocumentation">Whether to prohibit documentation network requests.</param>
        private JavaWrapperGenerationRequest(string projectRoot, string toolDirectory, string unityContentsPath, string sdkDirectory,
            IReadOnlyList<string> inputs, IReadOnlyList<string> warnings, bool offlineDocumentation)
        {
            WorkingDirectory = toolDirectory;
            ProjectRoot = projectRoot;
            UnityContentsPath = unityContentsPath;
            SdkDirectory = sdkDirectory;
            ProjectPath = Path.Combine(toolDirectory, "AndroidJavaWrapperGenerator.csproj");
            AssemblyPath = Path.Combine(toolDirectory, "bin", "Release", "net9.0", "AndroidJavaWrapperGenerator.dll");
            StagingDirectory = Path.Combine(toolDirectory, ".work", "editor-output", Guid.NewGuid().ToString("N"));
            OutputDirectory = Path.Combine(projectRoot, "Tools", "AndroidJavaWrapper", "src");
            RuntimeAssemblyDirectory = Path.Combine(projectRoot, "Assets", "Plugins", "MajdataPlay", "Platform", "Android");
            RuntimeAssemblyPath = Path.Combine(RuntimeAssemblyDirectory, "MajdataPlay.Platform.Android.Runtime.dll");
            PreviousOutputDirectory = Path.Combine(RuntimeAssemblyDirectory, "Generated");
            PreviousRuntimeDirectory = Path.Combine(RuntimeAssemblyDirectory, "Runtime");
            DocumentationCacheDirectory = Path.Combine(toolDirectory, ".work", "editor-docs");
            RootNamespace = "MajdataPlay.Platform.Android.Runtime";
            OfflineDocumentation = offlineDocumentation;
            Inputs = inputs;
            Warnings = warnings;
            BuildArguments = "build " + QuoteArgument(ProjectPath)
                + " --configuration Release --nologo --disable-build-servers";
            Arguments = CreateGeneratorArguments();
        }

        /// <summary>
        /// Discovers all project and Unity Java inputs and captures one refresh execution plan.
        /// </summary>
        /// <param name="projectRoot">The Unity project root containing Assets and the independent CLI.</param>
        /// <param name="unityContentsPath">The active Editor's Data directory or macOS Contents directory.</param>
        /// <param name="sdkDirectory">The Android SDK directory selected by the Editor's settings.</param>
        /// <param name="offlineDocumentation">Whether to use cached official documentation without network access.</param>
        /// <returns>The validated paths, inputs, and process arguments for one refresh.</returns>
        /// <exception cref="ArgumentNullException">A required directory argument is null.</exception>
        /// <exception cref="ArgumentException">A directory argument is empty or contains invalid path characters.</exception>
        /// <exception cref="DirectoryNotFoundException">The project, Editor, SDK, or Unity Android source directory is missing.</exception>
        /// <exception cref="FileNotFoundException">The CLI project, SDK pin, or Unity Release classes.jar is missing.</exception>
        /// <exception cref="InvalidDataException">The Unity Android source directory contains no supported Java inputs.</exception>
        /// <exception cref="IOException">An input directory cannot be enumerated.</exception>
        /// <exception cref="UnauthorizedAccessException">An input directory cannot be accessed.</exception>
        internal static JavaWrapperGenerationRequest Create(string projectRoot, string unityContentsPath,
            string sdkDirectory, bool offlineDocumentation = false)
        {
            var root = GetDirectoryPath(projectRoot, nameof(projectRoot), "Unity project");
            var unityContents = GetDirectoryPath(unityContentsPath, nameof(unityContentsPath), "Unity Editor contents");
            var sdk = GetDirectoryPath(sdkDirectory, nameof(sdkDirectory), "configured Android SDK");
            var toolDirectory = Path.Combine(root, "Tools", "CSharp", "AndroidJavaWrapperGenerator");
            var projectPath = Path.Combine(toolDirectory, "AndroidJavaWrapperGenerator.csproj");
            if (!File.Exists(projectPath))
            {
                throw new FileNotFoundException("The independent Android Java wrapper CLI project is missing: "
                    + projectPath + ". Restore the repository's Tools/CSharp/AndroidJavaWrapperGenerator directory.", projectPath);
            }

            var sdkPinPath = Path.Combine(toolDirectory, "global.json");
            if (!File.Exists(sdkPinPath))
            {
                throw new FileNotFoundException("The Android Java wrapper CLI's .NET 9 SDK pin is missing: "
                    + sdkPinPath + ". Restore the tool's global.json before building wrappers.", sdkPinPath);
            }

            var androidPlayer = Path.Combine(unityContents, "PlaybackEngines", "AndroidPlayer");
            var unitySourceDirectory = Path.Combine(androidPlayer, "Source");
            if (!Directory.Exists(unitySourceDirectory))
            {
                throw new DirectoryNotFoundException("Unity Android Java sources are missing: " + unitySourceDirectory
                    + ". Install Android Build Support for the active Unity Editor.");
            }

            var classesJar = FindUnityClassesJar(androidPlayer);
            var inputs = new List<string>();
            CollectJavaInputs(unitySourceDirectory, inputs);
            if (inputs.Count == 0)
            {
                throw new InvalidDataException("Unity Android Source contains no .java, .class, .jar, or .aar inputs: "
                    + unitySourceDirectory + ". Repair Android Build Support for the active Unity Editor.");
            }

            var projectInputsDirectory = Path.Combine(root, "Assets", "Plugins", "Android");
            if (Directory.Exists(projectInputsDirectory))
            {
                CollectJavaInputs(projectInputsDirectory, inputs);
            }

            inputs.Add(classesJar);
            var warnings = new List<string>();
            DiscoverUnityDependencies(root, androidPlayer, inputs, warnings);
            inputs.Sort(StringComparer.Ordinal);
            return new JavaWrapperGenerationRequest(root, toolDirectory, unityContents, sdk, inputs.AsReadOnly(), warnings.AsReadOnly(), offlineDocumentation);
        }

        /// <summary>
        /// Identifies supported Java source, class, and JAR paths, including removed asset paths.
        /// </summary>
        /// <param name="path">The asset or filesystem path to inspect.</param>
        /// <returns>True for a .java, .class, .jar, or .aar extension, compared without regard to case.</returns>
        internal static bool IsJavaInput(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return path.EndsWith(".java", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".class", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".aar", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Quotes one argument using double quotes and the standard backslash-before-quote rules.
        /// </summary>
        /// <remarks>
        /// The result is intended for ProcessStartInfo.Arguments with UseShellExecute disabled on
        /// Windows and Unix. It preserves empty values, whitespace, embedded quotes, and trailing
        /// backslashes. Shell expansion and shell command composition are not used.
        /// </remarks>
        /// <param name="value">The literal argument value to preserve.</param>
        /// <returns>A complete double-quoted process argument.</returns>
        /// <exception cref="ArgumentNullException">The argument value is null.</exception>
        /// <exception cref="ArgumentException">The value contains a NUL character.</exception>
        internal static string QuoteArgument(string value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (value.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("A process argument cannot contain a NUL character.", nameof(value));
            }

            var builder = new StringBuilder();
            builder.Append('"');
            var backslashes = 0;
            foreach (var character in value)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (character == '"')
                {
                    builder.Append('\\', (backslashes * 2) + 1);
                    builder.Append('"');
                }
                else
                {
                    builder.Append('\\', backslashes);
                    builder.Append(character);
                }

                backslashes = 0;
            }

            builder.Append('\\', backslashes * 2);
            builder.Append('"');
            return builder.ToString();
        }

        /// <summary>
        /// Normalizes and verifies one required directory without creating or modifying it.
        /// </summary>
        /// <param name="value">The caller's directory path.</param>
        /// <param name="parameterName">The parameter identified by validation errors.</param>
        /// <param name="description">The directory's role in the execution plan.</param>
        /// <returns>The existing directory's absolute path.</returns>
        /// <exception cref="ArgumentNullException">The path is null.</exception>
        /// <exception cref="ArgumentException">The path is empty or contains NUL or a line break.</exception>
        /// <exception cref="NotSupportedException">The operating system does not support the path format.</exception>
        /// <exception cref="PathTooLongException">The path exceeds the operating system's supported length.</exception>
        /// <exception cref="DirectoryNotFoundException">The required directory does not exist.</exception>
        private static string GetDirectoryPath(string value, string parameterName, string description)
        {
            if (value is null)
            {
                throw new ArgumentNullException(parameterName);
            }

            if (string.IsNullOrWhiteSpace(value) || value.IndexOf('\0') >= 0
                || value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0)
            {
                throw new ArgumentException("The " + description + " path is empty or contains NUL or a line break.", parameterName);
            }

            var directory = Path.GetFullPath(value);
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException("The " + description + " directory does not exist: " + directory + ".");
            }

            return directory;
        }

        /// <summary>
        /// Selects exactly one Unity Release archive, preferring Mono over IL2CPP.
        /// </summary>
        /// <param name="androidPlayer">The active Editor's AndroidPlayer directory.</param>
        /// <returns>The existing Mono Release archive, or the IL2CPP Release fallback.</returns>
        /// <exception cref="FileNotFoundException">Neither supported Release archive exists.</exception>
        private static string FindUnityClassesJar(string androidPlayer)
        {
            var monoJar = Path.Combine(androidPlayer, "Variations", "mono", "Release", "Classes", "classes.jar");
            if (File.Exists(monoJar))
            {
                return monoJar;
            }

            var il2cppJar = Path.Combine(androidPlayer, "Variations", "il2cpp", "Release", "Classes", "classes.jar");
            if (File.Exists(il2cppJar))
            {
                return il2cppJar;
            }

            throw new FileNotFoundException("Unity Android classes.jar is missing. Expected Mono Release at "
                + monoJar + " or IL2CPP Release at " + il2cppJar
                + ". Install Android Build Support for the active Unity Editor.", monoJar);
        }

        /// <summary>
        /// Recursively collects every supported Java input from one explicitly selected source root.
        /// </summary>
        /// <param name="directory">The project plugin or Unity Source directory to enumerate.</param>
        /// <param name="inputs">The destination list receiving absolute paths.</param>
        /// <exception cref="IOException">An input directory cannot be enumerated.</exception>
        /// <exception cref="UnauthorizedAccessException">An input directory cannot be accessed.</exception>
        private static void CollectJavaInputs(string directory, List<string> inputs)
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (IsJavaInput(path))
                {
                    inputs.Add(path);
                }
            }
        }

        /// <summary>
        /// Adds only cached Unity runtime artifacts and their bounded Maven dependency graph.
        /// </summary>
        /// <param name="projectRoot">The Unity project containing optional explicit Android archives.</param>
        /// <param name="androidPlayer">The active Editor's AndroidPlayer directory.</param>
        /// <param name="inputs">The discovered project inputs and selected Unity inputs.</param>
        /// <param name="warnings">The destination for cache version and missing dependency notices.</param>
        /// <exception cref="InvalidDataException">The dependency graph or a metadata file exceeds its bound.</exception>
        /// <exception cref="IOException">A selected cache directory or metadata file cannot be read.</exception>
        /// <exception cref="UnauthorizedAccessException">A selected cache directory or metadata file cannot be accessed.</exception>
        private static void DiscoverUnityDependencies(string projectRoot, string androidPlayer, List<string> inputs, List<string> warnings)
        {
            var requiresGameActivity = false;
            foreach (var input in inputs)
            {
                if (Path.GetFileName(input) == "UnityPlayerGameActivity.java"
                    && ReadDependencyText(input).IndexOf("com.google.androidgamesdk.GameActivity", StringComparison.Ordinal) >= 0)
                {
                    requiresGameActivity = true;
                    break;
                }
            }
            if (!requiresGameActivity)
            {
                return;
            }

            var gradleHome = Environment.GetEnvironmentVariable("GRADLE_USER_HOME");
            if (string.IsNullOrWhiteSpace(gradleHome))
            {
                gradleHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gradle");
            }
            var cache = Path.Combine(Path.GetFullPath(gradleHome!), "caches", "modules-2", "files-2.1");
            var declaredVersions = ReadGradleVersions(projectRoot, androidPlayer);
            var pending = new Queue<KeyValuePair<string, string?>>();
            foreach (var coordinate in new[]
            {
                "androidx.games:games-activity", "androidx.core:core", "androidx.appcompat:appcompat",
                "com.google.android.play:asset-delivery", "com.google.android.play:core", "com.google.android.gms:play-services-tasks"
            })
            {
                declaredVersions.TryGetValue(coordinate, out var version);
                pending.Enqueue(new KeyValuePair<string, string?>(coordinate, version));
            }
            var selected = new Dictionary<string, CachedArtifact>(StringComparer.Ordinal);
            var processed = new HashSet<string>(StringComparer.Ordinal);
            var projectArchives = new List<string>(inputs);
            var explicitGameActivity = HasGameActivityInput(projectRoot, projectArchives);
            while (pending.Count != 0)
            {
                if (processed.Count >= 128)
                {
                    throw new InvalidDataException("Unity Java metadata dependency discovery exceeds the 128-artifact limit.");
                }
                var dependency = pending.Dequeue();
                var coordinate = dependency.Key;
                var requestedVersion = NormalizeDependencyVersion(dependency.Value);
                if (!processed.Add(coordinate + ":" + requestedVersion))
                {
                    continue;
                }
                var parts = coordinate.Split(':');
                if (parts.Length != 2 || !IsArtifactName(parts[0]) || !IsArtifactName(parts[1])
                    || coordinate == "org.jetbrains.kotlin:kotlin-stdlib-common")
                {
                    continue;
                }
                if ((coordinate == "androidx.games:games-activity" && explicitGameActivity)
                    || (coordinate != "androidx.games:games-activity" && HasProjectArtifact(projectArchives, parts[1])))
                {
                    continue;
                }
                if (selected.TryGetValue(coordinate, out var previous)
                    && (requestedVersion is null || CompareVersions(previous.Version, requestedVersion) >= 0))
                {
                    continue;
                }
                var artifact = FindCachedArtifact(cache, parts[0], parts[1], requestedVersion);
                if (artifact is null)
                {
                    if (coordinate == "androidx.games:games-activity")
                    {
                        warnings.Add("UnityPlayerGameActivity.java requires real com.google.androidgamesdk.GameActivity metadata. "
                            + "Restore Android Build Support and resolve the Unity Android Gradle dependency androidx.games:games-activity"
                            + (requestedVersion is null ? "" : ":" + requestedVersion)
                            + " into " + cache + ", or place its official games-activity-VERSION.aar and dependency archives in Assets/Plugins/Android. "
                            + "GRADLE_USER_HOME selects the cache. Sources are retained; the CLI will diagnose any unresolved dependency. No dependency is downloaded or fabricated.");
                        continue;
                    }
                    warnings.Add("Unity Java metadata dependency '" + coordinate + "'"
                        + (requestedVersion is null ? "" : " version '" + requestedVersion + "'")
                        + " is not cached. Supply its runtime AAR/JAR in Assets/Plugins/Android to preserve typed public API dependencies.");
                    continue;
                }
                if (requestedVersion is null || requestedVersion != artifact.Version)
                {
                    warnings.Add("Unity Java metadata dependency '" + coordinate + "' uses cached stable version '" + artifact.Version
                        + "'" + (requestedVersion is null ? " because no version is declared in the available Gradle templates."
                            : " because requested version '" + requestedVersion + "' is not cached.")
                        + " Supply an explicit runtime archive in Assets/Plugins/Android to select different metadata.");
                }
                if (previous is not null)
                {
                    inputs.Remove(previous.ArchivePath);
                }
                selected[coordinate] = artifact;
                inputs.Add(artifact.ArchivePath);
                EnqueuePomDependencies(artifact.PomPath, pending);
            }
        }

        /// <summary>Reads literal runtime artifact versions from the explicit and installed Gradle templates.</summary>
        /// <param name="projectRoot">The Unity project root.</param>
        /// <param name="androidPlayer">The active Editor's AndroidPlayer directory.</param>
        /// <returns>Explicit group:artifact versions, with project declarations taking precedence.</returns>
        /// <exception cref="IOException">A template cannot be read.</exception>
        /// <exception cref="InvalidDataException">A template exceeds the metadata size limit.</exception>
        private static Dictionary<string, string> ReadGradleVersions(string projectRoot, string androidPlayer)
        {
            var versions = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var directory in new[]
            {
                Path.Combine(androidPlayer, "Tools", "GradleTemplates"), Path.Combine(projectRoot, "Assets", "Plugins", "Android")
            })
            {
                foreach (var name in new[] { "mainTemplate.gradle", "launcherTemplate.gradle" })
                {
                    var path = Path.Combine(directory, name);
                    if (!File.Exists(path))
                    {
                        continue;
                    }
                    foreach (Match match in Regex.Matches(ReadDependencyText(path), "['\"](?<coordinate>[A-Za-z0-9_.-]+:[A-Za-z0-9_.-]+):(?<version>[0-9]+(?:\\.[0-9]+){1,3})['\"]"))
                    {
                        versions[match.Groups["coordinate"].Value] = match.Groups["version"].Value;
                    }
                }
            }
            return versions;
        }

        /// <summary>Recognizes explicitly supplied GameActivity declarations without parsing JVM bytecode in the Editor.</summary>
        /// <param name="projectRoot">The Unity project containing explicit Android inputs.</param>
        /// <param name="inputs">The discovered inputs before cache dependencies were added.</param>
        /// <returns>True for an exact source declaration, named class file, or standard archive class entry.</returns>
        /// <exception cref="IOException">An explicit dependency cannot be read.</exception>
        /// <exception cref="UnauthorizedAccessException">An explicit dependency cannot be accessed.</exception>
        private static bool HasGameActivityInput(string projectRoot, IReadOnlyList<string> inputs)
        {
            var pluginRoot = Path.Combine(projectRoot, "Assets", "Plugins", "Android") + Path.DirectorySeparatorChar;
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            foreach (var input in inputs)
            {
                if (!input.StartsWith(pluginRoot, comparison))
                {
                    continue;
                }
                var extension = Path.GetExtension(input);
                if (extension.Equals(".java", StringComparison.OrdinalIgnoreCase))
                {
                    var source = ReadDependencyText(input);
                    if (Regex.IsMatch(source, "\\bpackage\\s+com\\.google\\.androidgamesdk\\s*;")
                        && Regex.IsMatch(source, "\\bclass\\s+GameActivity\\b"))
                    {
                        return true;
                    }
                }
                else if (extension.Equals(".class", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(input) == "GameActivity.class")
                {
                    return true;
                }
                else if (extension.Equals(".jar", StringComparison.OrdinalIgnoreCase) || extension.Equals(".aar", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using var archive = ZipFile.OpenRead(input);
                        if (archive.GetEntry("com/google/androidgamesdk/GameActivity.class") is not null)
                        {
                            return true;
                        }
                        if (!extension.Equals(".aar", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        foreach (var entry in archive.Entries)
                        {
                            if (entry.FullName != "classes.jar" && !(entry.FullName.StartsWith("libs/", StringComparison.Ordinal)
                                && entry.FullName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)))
                            {
                                continue;
                            }
                            if (entry.Length < 0 || entry.Length > (64L * 1024 * 1024))
                            {
                                continue;
                            }
                            using var stream = entry.Open();
                            var bytes = new byte[(int)entry.Length];
                            var offset = 0;
                            while (offset < bytes.Length)
                            {
                                var count = stream.Read(bytes, offset, Math.Min(64 * 1024, bytes.Length - offset));
                                if (count == 0)
                                {
                                    throw new InvalidDataException("Truncated explicit Android archive: " + input);
                                }
                                offset += count;
                            }
                            if (stream.ReadByte() != -1)
                            {
                                throw new InvalidDataException("Inconsistent explicit Android archive length: " + input);
                            }
                            using var buffer = new MemoryStream(bytes, false);
                            using var nested = new ZipArchive(buffer, ZipArchiveMode.Read, false);
                            if (nested.GetEntry("com/google/androidgamesdk/GameActivity.class") is not null)
                            {
                                return true;
                            }
                        }
                    }
                    catch (InvalidDataException)
                    {
                        // Preserve malformed explicit inputs for the CLI's authoritative archive diagnostics.
                    }
                }
            }
            return false;
        }
        /// <summary>Checks whether a project archive explicitly supplies a named runtime artifact.</summary>
        /// <param name="inputs">The inputs discovered before cache dependencies were added.</param>
        /// <param name="artifact">The Maven artifact name to recognize.</param>
        /// <returns>True for an explicit artifact.jar, artifact.aar, or artifact-VERSION archive.</returns>
        private static bool HasProjectArtifact(IReadOnlyList<string> inputs, string artifact)
        {
            foreach (var input in inputs)
            {
                var extension = Path.GetExtension(input);
                if (!extension.Equals(".aar", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".jar", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var name = Path.GetFileNameWithoutExtension(input);
                if (name == artifact || (name.StartsWith(artifact + "-", StringComparison.Ordinal)
                    && NormalizeDependencyVersion(name.Substring(artifact.Length + 1)) is not null))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Selects one artifact version without scanning unrelated Gradle artifacts or tools.</summary>
        /// <param name="cache">The Gradle modules-2/files-2.1 cache root.</param>
        /// <param name="group">The validated Maven group name.</param>
        /// <param name="artifact">The validated Maven artifact name.</param>
        /// <param name="requestedVersion">The preferred literal version, or null for the newest stable cached version.</param>
        /// <returns>The exact cached runtime archive and optional POM, or null when none is available.</returns>
        /// <exception cref="InvalidDataException">An artifact cache contains too many versions or content directories.</exception>
        /// <exception cref="IOException">The selected artifact cache cannot be read.</exception>
        /// <exception cref="UnauthorizedAccessException">The selected artifact cache cannot be accessed.</exception>
        private static CachedArtifact? FindCachedArtifact(string cache, string group, string artifact, string? requestedVersion)
        {
            var directory = Path.Combine(cache, group, artifact);
            if (!Directory.Exists(directory))
            {
                return null;
            }
            var versions = new List<string>();
            foreach (var versionDirectory in Directory.EnumerateDirectories(directory))
            {
                if (versions.Count >= 256)
                {
                    throw new InvalidDataException("Gradle artifact cache exceeds the 256-version limit: " + directory);
                }
                var version = Path.GetFileName(versionDirectory);
                if (NormalizeDependencyVersion(version) is not null)
                {
                    versions.Add(version);
                }
            }
            versions.Sort((left, right) => CompareVersions(right, left));
            if (requestedVersion is not null && versions.Remove(requestedVersion))
            {
                versions.Insert(0, requestedVersion);
            }
            foreach (var version in versions)
            {
                var contentDirectories = new List<string>();
                foreach (var contentDirectory in Directory.EnumerateDirectories(Path.Combine(directory, version)))
                {
                    if (contentDirectories.Count >= 64)
                    {
                        throw new InvalidDataException("Gradle artifact cache exceeds the 64-content-directory limit: " + directory);
                    }
                    contentDirectories.Add(contentDirectory);
                }
                contentDirectories.Sort(StringComparer.Ordinal);
                string? archivePath = null;
                string? pomPath = null;
                foreach (var contentDirectory in contentDirectories)
                {
                    var prefix = Path.Combine(contentDirectory, artifact + "-" + version);
                    if (archivePath is null)
                    {
                        foreach (var extension in new[] { ".aar", ".jar" })
                        {
                            if (File.Exists(prefix + extension))
                            {
                                archivePath = prefix + extension;
                                break;
                            }
                        }
                    }
                    if (pomPath is null && File.Exists(prefix + ".pom"))
                    {
                        pomPath = prefix + ".pom";
                    }
                }
                if (archivePath is not null)
                {
                    return new CachedArtifact(version, archivePath, pomPath);
                }
            }
            return null;
        }

        /// <summary>Queues literal compile/runtime dependencies from one cached POM without external XML resolution.</summary>
        /// <param name="path">The optional cached Maven POM.</param>
        /// <param name="pending">The bounded runtime dependency queue.</param>
        /// <exception cref="InvalidDataException">The POM is malformed, too large, or has excessive dependencies.</exception>
        /// <exception cref="IOException">The POM cannot be read.</exception>
        private static void EnqueuePomDependencies(string? path, Queue<KeyValuePair<string, string?>> pending)
        {
            if (path is null)
            {
                return;
            }
            try
            {
                var document = new XmlDocument { XmlResolver = null };
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 };
                using var text = new StringReader(ReadDependencyText(path));
                using var reader = XmlReader.Create(text, settings);
                document.Load(reader);
                var namespaces = new XmlNamespaceManager(document.NameTable);
                namespaces.AddNamespace("m", "http://maven.apache.org/POM/4.0.0");
                var nodes = document.SelectNodes("/m:project/m:dependencies/m:dependency", namespaces);
                if (nodes is null)
                {
                    return;
                }
                foreach (XmlNode node in nodes)
                {
                    var scope = node.SelectSingleNode("m:scope", namespaces)?.InnerText;
                    var type = node.SelectSingleNode("m:type", namespaces)?.InnerText;
                    if ((scope is not null && scope != "compile" && scope != "runtime") || type == "pom"
                        || node.SelectSingleNode("m:optional", namespaces)?.InnerText == "true")
                    {
                        continue;
                    }
                    if (pending.Count >= 256)
                    {
                        throw new InvalidDataException("Unity Java metadata dependency queue exceeds the 256-artifact limit.");
                    }
                    var group = node.SelectSingleNode("m:groupId", namespaces)?.InnerText;
                    var artifact = node.SelectSingleNode("m:artifactId", namespaces)?.InnerText;
                    var version = node.SelectSingleNode("m:version", namespaces)?.InnerText;
                    if (group is not null && artifact is not null)
                    {
                        pending.Enqueue(new KeyValuePair<string, string?>(group + ":" + artifact, version));
                    }
                }
            }
            catch (XmlException exception)
            {
                throw new InvalidDataException("Invalid cached Unity Java dependency POM: " + path, exception);
            }
        }

        /// <summary>Reads at most one MiB of source, template, or dependency metadata.</summary>
        /// <param name="path">The text metadata path.</param>
        /// <returns>The complete text within the permitted size.</returns>
        /// <exception cref="InvalidDataException">The metadata exceeds one MiB.</exception>
        /// <exception cref="IOException">The metadata cannot be read.</exception>
        private static string ReadDependencyText(string path)
        {
            if (new FileInfo(path).Length > 1024 * 1024)
            {
                throw new InvalidDataException("Unity Java dependency metadata exceeds the 1 MiB limit: " + path);
            }
            return File.ReadAllText(path);
        }

        /// <summary>Recognizes stable numeric artifact versions, including exact Maven version brackets.</summary>
        /// <param name="value">The requested or cached version text.</param>
        /// <returns>The stable numeric version, or null for an unsupported range or prerelease.</returns>
        private static string? NormalizeDependencyVersion(string? value)
        {
            if (value is null)
            {
                return null;
            }
            var version = value.Trim().Trim('[', ']');
            return Regex.IsMatch(version, "^[0-9]+(?:\\.[0-9]+){1,3}$") && Version.TryParse(version, out _) ? version : null;
        }

        /// <summary>Compares validated stable versions with an ordinal tie-break for deterministic selection.</summary>
        /// <param name="left">The first stable numeric version.</param>
        /// <param name="right">The second stable numeric version.</param>
        /// <returns>The numeric ordering, or the ordinal ordering when numeric components are equal.</returns>
        private static int CompareVersions(string left, string right)
        {
            var comparison = Version.Parse(left).CompareTo(Version.Parse(right));
            return comparison == 0 ? StringComparer.Ordinal.Compare(left, right) : comparison;
        }

        /// <summary>Rejects paths or expressions masquerading as Maven coordinate names.</summary>
        /// <param name="value">The proposed group or artifact name.</param>
        /// <returns>True for a literal coordinate component without directory separators.</returns>
        private static bool IsArtifactName(string value)
        {
            return Regex.IsMatch(value, "^[A-Za-z0-9_][A-Za-z0-9_.-]*$");
        }

        /// <summary>Retains the selected runtime archive and its own cached dependency metadata.</summary>
        private sealed class CachedArtifact
        {
            /// <summary>Gets the stable cached version.</summary>
            internal string Version { get; }

            /// <summary>Gets the AAR or JAR containing the actual JVM declarations.</summary>
            internal string ArchivePath { get; }

            /// <summary>Gets the optional POM used to discover runtime dependencies.</summary>
            internal string? PomPath { get; }

            /// <summary>Captures one deterministic artifact selection.</summary>
            /// <param name="version">The stable cached artifact version.</param>
            /// <param name="archivePath">The actual cached AAR or JAR.</param>
            /// <param name="pomPath">The optional dependency POM.</param>
            internal CachedArtifact(string version, string archivePath, string? pomPath)
            {
                Version = version;
                ArchivePath = archivePath;
                PomPath = pomPath;
            }
        }
        /// <summary>
        /// Builds project-mode CLI assembly arguments with full SDK defaults and bounded parallel work.
        /// </summary>
        /// <returns>The complete process argument string for generation, compilation, and DLL publication.</returns>
        private string CreateGeneratorArguments()
        {
            var jobs = Math.Min(1024, Math.Max(1, Environment.ProcessorCount));
            var builder = new StringBuilder();
            builder.Append(QuoteArgument(AssemblyPath));
            builder.Append(" --project ");
            builder.Append(QuoteArgument(ProjectRoot));
            builder.Append(" --unity-data ");
            builder.Append(QuoteArgument(UnityContentsPath));
            builder.Append(" --sdk ");
            builder.Append(QuoteArgument(SdkDirectory));
            builder.Append(" --docs-cache ");
            builder.Append(QuoteArgument(DocumentationCacheDirectory));
            builder.Append(" --jobs ");
            builder.Append(jobs.ToString(CultureInfo.InvariantCulture));
            builder.Append(" --docs-jobs 4");
            if (OfflineDocumentation)
            {
                builder.Append(" --offline-docs");
            }

            return builder.ToString();
        }
    }
}
