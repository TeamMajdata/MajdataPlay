using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using MajdataPlay.Platform.Android.Editor;
using MajdataPlay.Tools.AndroidJavaWrapperGenerator;

#nullable enable

namespace MajdataPlay.Tests.AndroidJavaWrapperGeneratorValidation
{
    /// <summary>Validates Android library archives with real SDK class bytes and isolated dependency discovery.</summary>
    internal static partial class Program
    {
        /// <summary>Exercises AAR runtime JARs, bounded ZIP failures, and explicit/cache Editor inputs without Java tools.</summary>
        /// <param name="settings">The real Android SDK used only to supply independent class-file fixtures.</param>
        /// <param name="workspace">The exclusively owned ignored validation directory.</param>
        /// <exception cref="IOException">A fixture or SDK archive cannot be read or written.</exception>
        /// <exception cref="InvalidOperationException">An archive or discovery contract fails.</exception>
        private static void VerifyUnityArchives(ValidationSettings settings, string workspace)
        {
            var directory = Path.Combine(workspace, "unity-archives");
            Directory.CreateDirectory(directory);
            var platform = JavaToolchain.FindPlatformJar(new GeneratorOptions { SdkDirectory = settings.Sdk }, CancellationToken.None)!;
            using var sdk = ZipFile.OpenRead(platform);
            var objectClass = ReadUnityArchiveEntry(sdk, "java/lang/Object.class");
            var stringClass = ReadUnityArchiveEntry(sdk, "java/lang/String.class");
            var objectJar = CreateUnityArchiveBytes(new[] { new KeyValuePair<string, byte[]>("java/lang/Object.class", objectClass) });
            var stringJar = CreateUnityArchiveBytes(new[] { new KeyValuePair<string, byte[]>("java/lang/String.class", stringClass) });
            var source = Path.Combine(directory, "Consumer.java");
            File.WriteAllText(source, "package archivefixture; public class Consumer { public java.lang.String label; }");
            var aar = Path.Combine(directory, "arbitrary-library-name.AAR");
            File.WriteAllBytes(aar, CreateUnityArchiveBytes(new[]
            {
                new KeyValuePair<string, byte[]>("classes.jar", objectJar),
                new KeyValuePair<string, byte[]>("libs/text.JAR", stringJar),
                new KeyValuePair<string, byte[]>("assets/unrelated.jar", Encoding.UTF8.GetBytes("not a runtime JAR")),
                new KeyValuePair<string, byte[]>("assets/NotJava.class", Encoding.UTF8.GetBytes("not class metadata")),
                new KeyValuePair<string, byte[]>("jni/arm64-v8a/libfixture.so", Encoding.UTF8.GetBytes("not JVM metadata"))
            }));
            var api = ScanUnityArchiveInputs(4, aar, source);
            Require(api.Types.ContainsKey("java.lang.Object") && api.Types.ContainsKey("java.lang.String"), "AAR classes.jar and libs/*.jar contribute real SDK declarations");
            Require(api.Types["archivefixture.Consumer"].Fields.Single().Descriptor == "Ljava/lang/String;", "Sources resolve types provided by nested AAR dependency JARs");
            Require(api.Types.Count == 3, "AAR resources, native libraries, and unrelated archives are not treated as class inputs");
            var plainObjectJar = Path.Combine(directory, "object.jar");
            var plainStringJar = Path.Combine(directory, "string.jar");
            File.WriteAllBytes(plainObjectJar, objectJar);
            File.WriteAllBytes(plainStringJar, stringJar);
            Require(ApiFingerprint(api) == ApiFingerprint(ScanUnityArchiveInputs(1, plainObjectJar, plainStringJar, source)), "AAR and equivalent standalone JAR inputs preserve metadata identity across worker counts");
            Require(ApiFingerprint(api) == ApiFingerprint(ScanUnityArchiveInputs(1, aar, source)), "AAR serial and parallel scanning preserve exact metadata identity");
            Require(ApiFingerprint(api) == ApiFingerprint(ScanUnityArchiveInputs(4, aar, plainObjectJar, source)), "Byte-identical nested and standalone repeated classes remain accepted");

            var libsOnly = Path.Combine(directory, "libs-only.aar");
            File.WriteAllBytes(libsOnly, CreateUnityArchiveBytes(new[]
            {
                new KeyValuePair<string, byte[]>("classes.jar", CreateUnityArchiveBytes(Array.Empty<KeyValuePair<string, byte[]>>())),
                new KeyValuePair<string, byte[]>("libs/main.jar", objectJar)
            }));
            Require(ScanUnityArchiveInputs(1, libsOnly).Types.ContainsKey("java.lang.Object"), "AAR accepts runtime classes in libs/*.jar when classes.jar is empty");
            var versioned = Path.Combine(directory, "versioned.aar");
            var versionedJar = CreateUnityArchiveBytes(new[]
            {
                new KeyValuePair<string, byte[]>("java/lang/Object.class", objectClass),
                new KeyValuePair<string, byte[]>("META-INF/versions/17/Unexpected.class", Encoding.UTF8.GetBytes("unsupported version variant"))
            });
            File.WriteAllBytes(versioned, CreateUnityArchiveBytes(new[] { new KeyValuePair<string, byte[]>("classes.jar", versionedJar) }));
            var versionedApi = ScanUnityArchiveInputs(1, versioned);
            Require(versionedApi.Types.Count == 1 && versionedApi.Warnings.Any(warning => warning.Contains("!classes.jar", StringComparison.Ordinal)
                && warning.Contains("META-INF/versions", StringComparison.Ordinal)), "Nested multi-release JARs preserve the base-only rule and complete warning provenance");

            VerifyUnityArchiveFailures(directory, objectJar, objectClass);
            VerifyUnityArchiveDiscovery(directory, objectJar);
        }

        /// <summary>Checks malformed, conflicting, oversized, and traversal-shaped nested archives before extraction.</summary>
        /// <param name="directory">The isolated archive fixture directory.</param>
        /// <param name="objectJar">A JAR containing the real SDK java.lang.Object.</param>
        /// <param name="objectClass">The original SDK class-file bytes.</param>
        /// <exception cref="IOException">An archive fixture cannot be written.</exception>
        /// <exception cref="InvalidOperationException">An invalid archive is accepted.</exception>
        private static void VerifyUnityArchiveFailures(string directory, byte[] objectJar, byte[] objectClass)
        {
            var malformed = Path.Combine(directory, "malformed.aar");
            foreach (var entries in new[]
            {
                new[] { new KeyValuePair<string, byte[]>("classes.jar", Encoding.UTF8.GetBytes("not ZIP metadata")) },
                new[] { new KeyValuePair<string, byte[]>("AndroidManifest.xml", Encoding.UTF8.GetBytes("no JVM declarations")) },
                new[] { new KeyValuePair<string, byte[]>("classes.jar", objectJar), new KeyValuePair<string, byte[]>("classes.jar", objectJar) },
                new[] { new KeyValuePair<string, byte[]>("libs/../escape.jar", objectJar) },
                new[] { new KeyValuePair<string, byte[]>("libs/nested/library.jar", objectJar) }
            })
            {
                File.WriteAllBytes(malformed, CreateUnityArchiveBytes(entries));
                Expect<InvalidDataException>(() => ScanUnityArchiveInputs(1, malformed), "Invalid AAR nested/archive/path structure is rejected");
            }

            var changedClass = (byte[])objectClass.Clone();
            changedClass[5] = 1;
            var changedJar = CreateUnityArchiveBytes(new[] { new KeyValuePair<string, byte[]>("java/lang/Object.class", changedClass) });
            File.WriteAllBytes(malformed, CreateUnityArchiveBytes(new[]
            {
                new KeyValuePair<string, byte[]>("classes.jar", objectJar),
                new KeyValuePair<string, byte[]>("libs/conflict.jar", changedJar)
            }));
            Expect<InvalidDataException>(() => ScanUnityArchiveInputs(1, malformed), "AAR nested JARs retain strict conflicting bytecode duplicate rejection");

            var oversized = CreateUnityArchiveBytes(new[] { new KeyValuePair<string, byte[]>("classes.jar", objectJar) });
            var central = FindUnityZipSignature(oversized, 0x02014b50);
            WriteUnityZipUInt32(oversized, central + 20, 64 * 1024 * 1024);
            WriteUnityZipUInt32(oversized, central + 24, (64 * 1024 * 1024) + 1);
            File.WriteAllBytes(malformed, oversized);
            Expect<InvalidDataException>(() => ScanUnityArchiveInputs(1, malformed), "Oversized nested JAR declarations are rejected before buffer allocation");

            var ratio = CreateUnityArchiveBytes(new[] { new KeyValuePair<string, byte[]>("classes.jar", objectJar) });
            central = FindUnityZipSignature(ratio, 0x02014b50);
            WriteUnityZipUInt32(ratio, central + 20, 1);
            WriteUnityZipUInt32(ratio, central + 24, 2 * 1024 * 1024);
            File.WriteAllBytes(malformed, ratio);
            Expect<InvalidDataException>(() => ScanUnityArchiveInputs(1, malformed), "Nested JAR compression-ratio limits reject suspicious archive metadata");

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            var options = new GeneratorOptions { NoSdk = true };
            options.Inputs.Add(malformed);
            Expect<OperationCanceledException>(() => JavaApiScanner.Scan(options, canceled.Token), "AAR scanning observes cancellation before archive work");
            Require(!File.Exists(Path.Combine(directory, "escape.jar")), "AAR processing never extracts archive paths to disk");
        }

        /// <summary>Validates bounded cache selection, POM dependency discovery, explicit sources, and import extensions.</summary>
        /// <param name="directory">The isolated fixture root.</param>
        /// <param name="jar">A valid runtime JAR used only as a discovery fixture without parsing fake Unity archives.</param>
        /// <exception cref="IOException">A discovery fixture cannot be written.</exception>
        /// <exception cref="InvalidOperationException">A cache or explicit-input discovery contract fails.</exception>
        private static void VerifyUnityArchiveDiscovery(string directory, byte[] jar)
        {
            var project = Path.Combine(directory, "discovery-project");
            var tool = Path.Combine(project, "Tools", "CSharp", "AndroidJavaWrapperGenerator");
            var plugins = Path.Combine(project, "Assets", "Plugins", "Android");
            var unity = Path.Combine(directory, "Unity.app", "Contents");
            var player = Path.Combine(unity, "PlaybackEngines", "AndroidPlayer");
            var sourceDirectory = Path.Combine(player, "Source");
            var classes = Path.Combine(player, "Variations", "mono", "Release", "Classes");
            var sdk = Path.Combine(directory, "empty-sdk-fixture");
            var gradle = Path.Combine(directory, "isolated-gradle");
            foreach (var path in new[] { tool, plugins, sourceDirectory, classes, sdk, gradle })
            {
                Directory.CreateDirectory(path);
            }
            File.WriteAllText(Path.Combine(tool, "AndroidJavaWrapperGenerator.csproj"), "isolated project path fixture");
            File.WriteAllText(Path.Combine(tool, "global.json"), "isolated SDK pin path fixture");
            File.WriteAllText(Path.Combine(classes, "classes.jar"), "request creation must not parse Unity classes.jar");
            var unitySource = Path.Combine(sourceDirectory, "UnityPlayerActivity.java");
            File.WriteAllText(unitySource, "package com.unity3d.player; public class UnityPlayerActivity { }");
            var previousGradle = Environment.GetEnvironmentVariable("GRADLE_USER_HOME");
            try
            {
                Environment.SetEnvironmentVariable("GRADLE_USER_HOME", gradle);
                var request = JavaWrapperGenerationRequest.Create(project, unity, sdk, true);
                Require(request.Inputs.Count == 2 && request.Warnings.Count == 0, "Unity Activity-only discovery neither parses classes.jar nor requires GameActivity cache");
                File.WriteAllText(Path.Combine(sourceDirectory, "UnityPlayerGameActivity.java"),
                    "package com.unity3d.player; import com.google.androidgamesdk.GameActivity; public class UnityPlayerGameActivity extends GameActivity { }");
                request = JavaWrapperGenerationRequest.Create(project, unity, sdk, true);
                Require(request.Inputs.Count == 3 && request.Warnings.Any(warning => warning.Contains("GameActivity", StringComparison.Ordinal)
                    && warning.Contains("Assets/Plugins/Android", StringComparison.Ordinal)), "Missing GameActivity cache preserves inputs and provides an actionable resolver warning");
                var cache = Path.Combine(gradle, "caches", "modules-2", "files-2.1");
                var aar = CreateUnityArchiveBytes(new[] { new KeyValuePair<string, byte[]>("classes.jar", jar) });
                var older = WriteUnityCachedArtifact(cache, "androidx.games", "games-activity", "4.3.0", ".aar", aar);
                var latest = WriteUnityCachedArtifact(cache, "androidx.games", "games-activity", "4.4.0", ".aar", aar);
                WriteUnityCachedArtifact(cache, "androidx.games", "games-activity", "99.0.0-alpha01", ".aar", aar);
                var core = WriteUnityCachedArtifact(cache, "androidx.core", "core", "1.9.0", ".aar", aar);
                var appcompat = WriteUnityCachedArtifact(cache, "androidx.appcompat", "appcompat", "1.6.1", ".aar", aar);
                var hierarchy = WriteUnityCachedArtifact(cache, "androidx.fixture", "hierarchy", "2.0.0", ".jar", jar);
                var pomPath = Path.Combine(Path.GetDirectoryName(appcompat)!, "appcompat-1.6.1.pom");
                File.WriteAllText(pomPath, "<project xmlns=\"http://maven.apache.org/POM/4.0.0\"><dependencies><dependency><groupId>androidx.fixture</groupId><artifactId>hierarchy</artifactId><version>2.0.0</version><scope>compile</scope></dependency></dependencies></project>");
                request = JavaWrapperGenerationRequest.Create(project, unity, sdk, true);
                Require(request.Inputs.Contains(latest) && !request.Inputs.Contains(older), "Artifact-specific cache fallback selects the newest stable version deterministically");
                Require(request.Inputs.Contains(core) && request.Inputs.Contains(appcompat) && request.Inputs.Contains(hierarchy), "Unity runtime roots follow only their cached compile/runtime POM dependencies");
                Require(request.Warnings.Any(warning => warning.Contains("4.4.0", StringComparison.Ordinal)), "Unpinned cached GameActivity version is reported explicitly");
                File.WriteAllText(Path.Combine(plugins, "mainTemplate.gradle"), "dependencies { implementation 'androidx.games:games-activity:4.3.0' }");
                request = JavaWrapperGenerationRequest.Create(project, unity, sdk, true);
                Require(request.Inputs.Contains(older) && !request.Inputs.Contains(latest), "Explicit Gradle artifact versions take precedence over cached newest versions");
                File.Delete(Path.Combine(plugins, "mainTemplate.gradle"));
                var suppliedSource = Path.Combine(plugins, "arbitrary-input.java");
                File.WriteAllText(suppliedSource, "package com.google.androidgamesdk; public class GameActivity { }");
                request = JavaWrapperGenerationRequest.Create(project, unity, sdk, true);
                Require(request.Inputs.Contains(suppliedSource) && !request.Inputs.Contains(latest), "Explicit GameActivity Java source suppresses conflicting cached bytecode selection");
                File.Delete(suppliedSource);
                var explicitJar = Path.Combine(plugins, "custom-name.jar");
                var gameEntryJar = CreateUnityArchiveBytes(new[] { new KeyValuePair<string, byte[]>("com/google/androidgamesdk/GameActivity.class", Array.Empty<byte>()) });
                File.WriteAllBytes(explicitJar, gameEntryJar);
                request = JavaWrapperGenerationRequest.Create(project, unity, sdk, true);
                Require(request.Inputs.Contains(explicitJar) && !request.Inputs.Contains(latest), "Editor recognizes exact GameActivity class entries in arbitrary-named JARs without parsing bytecode");
                File.Delete(explicitJar);
                var explicitAar = Path.Combine(plugins, "custom-name.aar");
                File.WriteAllBytes(explicitAar, CreateUnityArchiveBytes(new[] { new KeyValuePair<string, byte[]>("classes.jar", gameEntryJar) }));
                request = JavaWrapperGenerationRequest.Create(project, unity, sdk, true);
                Require(request.Inputs.Contains(explicitAar) && !request.Inputs.Contains(latest), "Editor recognizes exact GameActivity class entries in arbitrary-named nested AAR JARs");
                Require(JavaWrapperGenerationRequest.IsJavaInput("library.AAR") && !JavaWrapperGenerationRequest.IsJavaInput("library.aar.meta"), "AAR asset changes trigger the shared Java input predicate");
            }
            finally
            {
                Environment.SetEnvironmentVariable("GRADLE_USER_HOME", previousGradle);
            }
        }

        /// <summary>Scans isolated Java inputs with a controlled worker count and no SDK discovery.</summary>
        /// <param name="jobs">The CPU worker count to exercise.</param>
        /// <param name="inputs">The standalone or Android archives and optional Java sources.</param>
        /// <returns>The actual scanner metadata without emitting wrappers.</returns>
        /// <exception cref="IOException">An input cannot be read or is invalid.</exception>
        private static JavaApi ScanUnityArchiveInputs(int jobs, params string[] inputs)
        {
            var options = new GeneratorOptions { NoSdk = true, Jobs = jobs };
            options.Inputs.AddRange(inputs);
            return JavaApiScanner.Scan(options, CancellationToken.None);
        }

        /// <summary>Reads one real SDK declaration for independent archive-format comparisons.</summary>
        /// <param name="archive">The real platform archive.</param>
        /// <param name="name">The exact class entry name.</param>
        /// <returns>The complete original SDK class bytes.</returns>
        /// <exception cref="InvalidDataException">The expected SDK declaration is absent.</exception>
        /// <exception cref="IOException">The SDK entry cannot be read.</exception>
        private static byte[] ReadUnityArchiveEntry(ZipArchive archive, string name)
        {
            var entry = archive.GetEntry(name) ?? throw new InvalidDataException("SDK fixture class is missing: " + name);
            using var source = entry.Open();
            using var output = new MemoryStream();
            source.CopyTo(output);
            return output.ToArray();
        }

        /// <summary>Builds a ZIP fixture in memory while preserving duplicate entries when requested.</summary>
        /// <param name="entries">The exact entry names and independently selected payloads.</param>
        /// <returns>The complete archive bytes.</returns>
        private static byte[] CreateUnityArchiveBytes(IEnumerable<KeyValuePair<string, byte[]>> entries)
        {
            using var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                foreach (var pair in entries)
                {
                    using var output = archive.CreateEntry(pair.Key, CompressionLevel.NoCompression).Open();
                    output.Write(pair.Value, 0, pair.Value.Length);
                }
            }
            return stream.ToArray();
        }

        /// <summary>Creates one exact modules-2 artifact file without populating unrelated Gradle libraries.</summary>
        /// <param name="cache">The isolated Gradle runtime artifact cache.</param>
        /// <param name="group">The literal Maven group.</param>
        /// <param name="artifact">The literal Maven artifact.</param>
        /// <param name="version">The stable or prerelease test version.</param>
        /// <param name="extension">The runtime archive extension.</param>
        /// <param name="bytes">The discovery fixture archive bytes.</param>
        /// <returns>The created artifact's absolute path.</returns>
        /// <exception cref="IOException">The fixture cannot be written.</exception>
        private static string WriteUnityCachedArtifact(string cache, string group, string artifact, string version, string extension, byte[] bytes)
        {
            var directory = Path.Combine(cache, group, artifact, version, "content-hash-fixture");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, artifact + "-" + version + extension);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        /// <summary>Finds a central-directory marker in a small independently generated ZIP fixture.</summary>
        /// <param name="bytes">The fixture's complete ZIP bytes.</param>
        /// <param name="signature">The ZIP structure marker to locate.</param>
        /// <returns>The first matching byte offset.</returns>
        /// <exception cref="InvalidDataException">The expected ZIP structure is absent.</exception>
        private static int FindUnityZipSignature(byte[] bytes, uint signature)
        {
            for (var offset = 0; offset <= bytes.Length - 4; offset++)
            {
                if (bytes[offset] == (byte)signature && bytes[offset + 1] == (byte)(signature >> 8)
                    && bytes[offset + 2] == (byte)(signature >> 16) && bytes[offset + 3] == (byte)(signature >> 24))
                {
                    return offset;
                }
            }
            throw new InvalidDataException("Expected ZIP fixture signature is missing.");
        }

        /// <summary>Changes one declared ZIP size to test rejection without allocating oversized fixture payloads.</summary>
        /// <param name="bytes">The independently generated archive bytes.</param>
        /// <param name="offset">The central-directory size field offset.</param>
        /// <param name="value">The unsigned size declaration.</param>
        private static void WriteUnityZipUInt32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }
    }
}
