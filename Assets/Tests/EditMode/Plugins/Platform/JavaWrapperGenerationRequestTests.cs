using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Linq;
using MajdataPlay.IO.Storage;
using MajdataPlay.Platform.Android.Editor;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Platform
{
    /// <summary>Verifies input discovery, dependency resolution and process arguments of the Android Java wrapper refresh plan.</summary>
    public class JavaWrapperGenerationRequestTests
    {
        /// <summary>Owns the fixture project, Editor and SDK directories.</summary>
        private TemporaryDirectory _directory = null!;
        /// <summary>Stores the fixture Unity project root.</summary>
        private StorageDirectory _project = null!;
        /// <summary>Stores the fixture Unity Editor contents directory.</summary>
        private StorageDirectory _unity = null!;
        /// <summary>Stores the fixture Android SDK directory.</summary>
        private StorageDirectory _sdk = null!;
        /// <summary>Stores the GRADLE_USER_HOME value that existed before the test.</summary>
        private string? _gradleHome;

        /// <summary>Creates empty fixture roots.</summary>
        [SetUp]
        public void SetUp()
        {
            _directory = new TemporaryDirectory();
            _project = _directory.Directory.CreateDirectory("project");
            _unity = _directory.Directory.CreateDirectory("unity");
            _sdk = _directory.Directory.CreateDirectory("sdk");
            _gradleHome = Environment.GetEnvironmentVariable("GRADLE_USER_HOME");
        }

        /// <summary>Restores the environment and deletes the fixtures.</summary>
        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable("GRADLE_USER_HOME", _gradleHome);
            _directory.Dispose();
        }

        /// <summary>Creates nested directories below a parent.</summary>
        /// <param name="parent">The parent directory.</param>
        /// <param name="names">The directory names from the outermost to the innermost.</param>
        /// <returns>The innermost directory.</returns>
        private static StorageDirectory Directory(StorageDirectory parent, params string[] names)
        {
            var current = parent;
            foreach (var name in names)
            {
                current = current.CreateDirectory(name);
            }

            return current;
        }

        /// <summary>Creates a text file.</summary>
        /// <param name="parent">The parent directory.</param>
        /// <param name="name">The file name.</param>
        /// <param name="text">The file contents.</param>
        /// <returns>The created file.</returns>
        private static StorageFile File(StorageDirectory parent, string name, string text = "")
        {
            var file = parent.CreateFile(name);
            file.WriteAllText(text);
            return file;
        }

        /// <summary>Creates a zip archive containing the named empty entries.</summary>
        /// <param name="parent">The parent directory.</param>
        /// <param name="name">The archive name.</param>
        /// <param name="entries">The entry names.</param>
        /// <returns>The created archive.</returns>
        private static StorageFile Archive(StorageDirectory parent, string name, params string[] entries)
        {
            using var buffer = new MemoryStream();
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            {
                foreach (var entry in entries)
                {
                    archive.CreateEntry(entry);
                }
            }

            var file = parent.CreateFile(name);
            file.WriteAllBytes(buffer.ToArray());
            return file;
        }

        /// <summary>Creates the minimum valid project and Editor layout.</summary>
        /// <returns>The Unity Android source directory.</returns>
        private StorageDirectory CreateValidLayout()
        {
            var tool = Directory(_project, "Tools", "CSharp", "AndroidJavaWrapperGenerator");
            File(tool, "AndroidJavaWrapperGenerator.csproj", "<Project />");
            File(tool, "global.json", "{}");
            var player = Directory(_unity, "PlaybackEngines", "AndroidPlayer");
            var source = Directory(player, "Source");
            File(source, "UnityPlayer.java", "class UnityPlayer {}");
            File(Directory(source, "nested"), "Helper.CLASS");
            File(source, "readme.txt");
            File(Directory(player, "Variations", "mono", "Release", "Classes"), "classes.jar");
            return source;
        }

        /// <summary>Creates the plan for the fixture layout.</summary>
        /// <param name="offline">Whether documentation is read offline.</param>
        /// <returns>The plan.</returns>
        private JavaWrapperGenerationRequest Create(bool offline = false)
        {
            return JavaWrapperGenerationRequest.Create(_project.Location, _unity.Location, _sdk.Location, offline);
        }

        /// <summary>Checks argument quoting for empty values, whitespace, quotes and trailing backslashes.</summary>
        /// <param name="value">The literal argument.</param>
        /// <param name="expected">The quoted argument.</param>
        [TestCase("", "\"\"")]
        [TestCase("a b", "\"a b\"")]
        [TestCase("a\"b", "\"a\\\"b\"")]
        [TestCase("C:\\dir\\", "\"C:\\dir\\\\\"")]
        [TestCase("a\\\\\"b", "\"a\\\\\\\\\\\"b\"")]
        [TestCase("x\\y", "\"x\\y\"")]
        public void QuoteArgument_EscapesForProcessArguments(string value, string expected)
        {
            Assert.That(JavaWrapperGenerationRequest.QuoteArgument(value), Is.EqualTo(expected));
        }

        /// <summary>Checks that invalid arguments cannot be quoted.</summary>
        [Test]
        public void QuoteArgument_RejectsNullAndNul()
        {
            Assert.Throws<ArgumentNullException>(() => JavaWrapperGenerationRequest.QuoteArgument(null!));
            Assert.Throws<ArgumentException>(() => JavaWrapperGenerationRequest.QuoteArgument("a\0b"));
        }

        /// <summary>Checks recognition of Java source, bytecode and archive inputs.</summary>
        /// <param name="path">The candidate path.</param>
        /// <param name="expected">Whether the path is a Java input.</param>
        [TestCase("Assets/Plugins/Android/A.java", true)]
        [TestCase("B.CLASS", true)]
        [TestCase("lib.Jar", true)]
        [TestCase("lib.aar", true)]
        [TestCase("A.cs", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsJavaInput_RecognizesExtensions(string? path, bool expected)
        {
            Assert.That(JavaWrapperGenerationRequest.IsJavaInput(path!), Is.EqualTo(expected));
        }

        /// <summary>Checks validation of the directory arguments.</summary>
        [Test]
        public void Create_ValidatesDirectories()
        {
            Assert.Throws<ArgumentNullException>(() => JavaWrapperGenerationRequest.Create(null!, _unity.Location, _sdk.Location));
            Assert.Throws<ArgumentException>(() => JavaWrapperGenerationRequest.Create(" ", _unity.Location, _sdk.Location));
            Assert.Throws<ArgumentException>(() => JavaWrapperGenerationRequest.Create(_project.Location + "\n", _unity.Location, _sdk.Location));
            Assert.Throws<DirectoryNotFoundException>(() => JavaWrapperGenerationRequest.Create(_project.Location + "-missing", _unity.Location, _sdk.Location));
            Assert.Throws<DirectoryNotFoundException>(() => JavaWrapperGenerationRequest.Create(_project.Location, _unity.Location, _sdk.Location + "-missing"));
        }

        /// <summary>Checks each missing tool or Editor component in the order the plan validates them.</summary>
        [Test]
        public void Create_ReportsMissingComponents()
        {
            Assert.Throws<FileNotFoundException>(() => Create());
            var tool = Directory(_project, "Tools", "CSharp", "AndroidJavaWrapperGenerator");
            File(tool, "AndroidJavaWrapperGenerator.csproj");
            Assert.That(Assert.Throws<FileNotFoundException>(() => Create())!.Message, Does.Contain("global.json"));
            File(tool, "global.json");
            Assert.Throws<DirectoryNotFoundException>(() => Create());
            var player = Directory(_unity, "PlaybackEngines", "AndroidPlayer");
            var source = Directory(player, "Source");
            Assert.That(Assert.Throws<FileNotFoundException>(() => Create())!.Message, Does.Contain("classes.jar"));
            File(Directory(player, "Variations", "il2cpp", "Release", "Classes"), "classes.jar");
            Assert.Throws<InvalidDataException>(() => Create());
            File(source, "A.java");

            var request = Create();

            Assert.That(request.Inputs.Any(input => input.Contains("il2cpp")), Is.True);
        }

        /// <summary>Checks the discovered inputs, derived paths and process arguments of a valid plan.</summary>
        [Test]
        public void Create_BuildsDeterministicPlan()
        {
            CreateValidLayout();
            var plugins = Directory(_project, "Assets", "Plugins", "Android");
            File(plugins, "Project.java", "class Project {}");
            Archive(plugins, "extra.aar", "classes.jar");

            var online = Create();
            var offline = Create(true);

            var root = Path.GetFullPath(_project.Location);
            var tool = Path.Combine(root, "Tools", "CSharp", "AndroidJavaWrapperGenerator");
            Assert.That(online.ProjectRoot, Is.EqualTo(root));
            Assert.That(online.WorkingDirectory, Is.EqualTo(tool));
            Assert.That(online.ProjectPath, Is.EqualTo(Path.Combine(tool, "AndroidJavaWrapperGenerator.csproj")));
            Assert.That(online.AssemblyPath, Does.EndWith(Path.Combine("bin", "Release", "net9.0", "AndroidJavaWrapperGenerator.dll")));
            Assert.That(online.StagingDirectory, Does.StartWith(Path.Combine(tool, ".work", "editor-output")));
            Assert.That(online.StagingDirectory, Is.Not.EqualTo(offline.StagingDirectory));
            Assert.That(online.OutputDirectory, Is.EqualTo(Path.Combine(root, "Tools", "AndroidJavaWrapper", "src")));
            Assert.That(online.RuntimeAssemblyPath, Does.EndWith("MajdataPlay.Platform.Android.Runtime.dll"));
            Assert.That(online.PreviousOutputDirectory, Does.EndWith("Generated"));
            Assert.That(online.PreviousRuntimeDirectory, Does.EndWith("Runtime"));
            Assert.That(online.DocumentationCacheDirectory, Is.EqualTo(Path.Combine(tool, ".work", "editor-docs")));
            Assert.That(online.RootNamespace, Is.EqualTo("MajdataPlay.Platform.Android.Runtime"));
            Assert.That(online.UnityContentsPath, Is.EqualTo(Path.GetFullPath(_unity.Location)));
            Assert.That(online.SdkDirectory, Is.EqualTo(Path.GetFullPath(_sdk.Location)));
            Assert.That(online.Inputs.Count, Is.EqualTo(5));
            Assert.That(online.Inputs, Is.Ordered.Using((IComparer)StringComparer.Ordinal));
            Assert.That(online.Inputs.Any(input => input.EndsWith("readme.txt", StringComparison.Ordinal)), Is.False);
            Assert.That(online.Warnings, Is.Empty);
            Assert.That(online.BuildArguments, Is.EqualTo("build " + JavaWrapperGenerationRequest.QuoteArgument(online.ProjectPath)
                + " --configuration Release --nologo --disable-build-servers"));
            Assert.That(online.Arguments, Does.StartWith(JavaWrapperGenerationRequest.QuoteArgument(online.AssemblyPath) + " --project "));
            Assert.That(online.Arguments, Does.Contain(" --jobs " + Math.Min(1024, Math.Max(1, Environment.ProcessorCount))));
            Assert.That(online.Arguments, Does.Contain(" --docs-jobs 4"));
            Assert.That(online.Arguments, Does.Not.Contain("--offline-docs"));
            Assert.That(online.OfflineDocumentation, Is.False);
            Assert.That(offline.Arguments, Does.EndWith(" --offline-docs"));
            Assert.That(offline.OfflineDocumentation, Is.True);
        }

        /// <summary>Checks GameActivity dependency resolution from a Gradle cache and template-declared versions.</summary>
        [Test]
        public void Create_ResolvesGameActivityDependenciesFromGradleCache()
        {
            var source = CreateValidLayout();
            File(source, "UnityPlayerGameActivity.java", "import com.google.androidgamesdk.GameActivity;");
            var player = Directory(_unity, "PlaybackEngines", "AndroidPlayer");
            File(Directory(player, "Tools", "GradleTemplates"), "mainTemplate.gradle", "implementation 'androidx.games:games-activity:2.0.2'");
            File(Directory(_project, "Assets", "Plugins", "Android"), "launcherTemplate.gradle", "implementation \"androidx.core:core:1.9.0\"");
            var gradle = _directory.Directory.CreateDirectory("gradle");
            var cache = Directory(gradle, "caches", "modules-2", "files-2.1");
            var activity = Directory(cache, "androidx.games", "games-activity", "2.0.2", "hash1");
            Archive(activity, "games-activity-2.0.2.aar", "classes.jar");
            File(activity, "games-activity-2.0.2.pom",
                "<project xmlns=\"http://maven.apache.org/POM/4.0.0\"><dependencies>"
                + "<dependency><groupId>androidx.core</groupId><artifactId>core</artifactId><version>1.9.0</version></dependency>"
                + "<dependency><groupId>junit</groupId><artifactId>junit</artifactId><version>4.0</version><scope>test</scope></dependency>"
                + "<dependency><groupId>a.b</groupId><artifactId>optional</artifactId><version>1.0</version><optional>true</optional></dependency>"
                + "<dependency><groupId>a.b</groupId><artifactId>bom</artifactId><version>1.0</version><type>pom</type></dependency>"
                + "<dependency><groupId>org.jetbrains.kotlin</groupId><artifactId>kotlin-stdlib-common</artifactId><version>1.0</version></dependency>"
                + "<dependency><groupId>../bad</groupId><artifactId>path</artifactId></dependency>"
                + "</dependencies></project>");
            Archive(Directory(cache, "androidx.core", "core", "1.9.0", "hash2"), "core-1.9.0.aar", "classes.jar");
            Archive(Directory(cache, "androidx.core", "core", "1.10.0", "hash3"), "core-1.10.0.aar", "classes.jar");
            Directory(cache, "androidx.core", "core", "1.11.0-beta01");
            File(Directory(cache, "androidx.appcompat", "appcompat", "1.6.1", "hash4"), "appcompat-1.6.1.jar");
            Environment.SetEnvironmentVariable("GRADLE_USER_HOME", gradle.Location);

            var request = Create();

            Assert.That(request.Inputs.Any(input => input.EndsWith("games-activity-2.0.2.aar", StringComparison.Ordinal)), Is.True);
            Assert.That(request.Inputs.Any(input => input.EndsWith("core-1.9.0.aar", StringComparison.Ordinal)), Is.True);
            Assert.That(request.Inputs.Any(input => input.EndsWith("core-1.10.0.aar", StringComparison.Ordinal)), Is.False);
            Assert.That(request.Inputs.Any(input => input.EndsWith("appcompat-1.6.1.jar", StringComparison.Ordinal)), Is.True);
            Assert.That(request.Warnings.Any(warning => warning.Contains("androidx.appcompat:appcompat") && warning.Contains("1.6.1")), Is.True);
            Assert.That(request.Warnings.Any(warning => warning.Contains("com.google.android.play:core") && warning.Contains("is not cached")), Is.True);
            Assert.That(request.Warnings.Any(warning => warning.Contains("androidx.games:games-activity")), Is.False);
        }

        /// <summary>Checks the warning for a missing GameActivity and that an explicit project archive suppresses the cache lookup.</summary>
        [Test]
        public void Create_ReportsMissingGameActivityUnlessSuppliedExplicitly()
        {
            var source = CreateValidLayout();
            File(source, "UnityPlayerGameActivity.java", "com.google.androidgamesdk.GameActivity");
            var gradle = _directory.Directory.CreateDirectory("empty-gradle");
            Environment.SetEnvironmentVariable("GRADLE_USER_HOME", gradle.Location);

            var missing = Create();

            Assert.That(missing.Warnings.Any(warning => warning.Contains("UnityPlayerGameActivity.java requires real")), Is.True);

            var plugins = Directory(_project, "Assets", "Plugins", "Android");
            var inner = new MemoryStream();
            using (var nested = new ZipArchive(inner, ZipArchiveMode.Create, true))
            {
                nested.CreateEntry("com/google/androidgamesdk/GameActivity.class");
            }

            using (var outer = new MemoryStream())
            {
                using (var archive = new ZipArchive(outer, ZipArchiveMode.Create, true))
                {
                    using var stream = archive.CreateEntry("classes.jar").Open();
                    stream.Write(inner.ToArray(), 0, (int)inner.Length);
                }

                var file = plugins.CreateFile("games-activity-9.9.aar");
                file.WriteAllBytes(outer.ToArray());
            }

            var explicitActivity = Create();

            Assert.That(explicitActivity.Warnings.Any(warning => warning.Contains("UnityPlayerGameActivity.java requires real")), Is.False);
            Assert.That(explicitActivity.Warnings.Any(warning => warning.Contains("androidx.core:core")), Is.True);
        }

        /// <summary>Checks that explicit GameActivity sources, class files and malformed archives are recognized or tolerated.</summary>
        /// <param name="kind">The kind of explicit input.</param>
        [TestCase("source")]
        [TestCase("class")]
        [TestCase("jar")]
        [TestCase("malformed")]
        public void Create_RecognizesExplicitGameActivityInputs(string kind)
        {
            var source = CreateValidLayout();
            File(source, "UnityPlayerGameActivity.java", "com.google.androidgamesdk.GameActivity");
            Environment.SetEnvironmentVariable("GRADLE_USER_HOME", _directory.Directory.CreateDirectory("gradle-" + kind).Location);
            var plugins = Directory(_project, "Assets", "Plugins", "Android");
            switch (kind)
            {
                case "source":
                    File(plugins, "GameActivity.java", "package com.google.androidgamesdk;\npublic class GameActivity {}");
                    break;
                case "class":
                    File(plugins, "GameActivity.class");
                    break;
                case "jar":
                    Archive(plugins, "activity.jar", "com/google/androidgamesdk/GameActivity.class");
                    break;
                default:
                    File(plugins, "broken.aar", "not a zip archive");
                    break;
            }

            var request = Create();
            var warned = request.Warnings.Any(warning => warning.Contains("UnityPlayerGameActivity.java requires real"));

            Assert.That(warned, Is.EqualTo(kind == "malformed"));
        }
    }
}
