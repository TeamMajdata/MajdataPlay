#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MajdataPlay;
using MajdataPlay.Diagnostics;
using UnityEngine;

namespace ResourceStorageValidation
{
    /// <summary>Exercises linked resource migration behavior using isolated local fixtures.</summary>
    internal static class Program
    {
        /// <summary>Runs all cases independently and reports a deterministic validation marker.</summary>
        /// <returns>Zero when all cases pass, otherwise one.</returns>
        public static int Main()
        {
            var cases = new (string name, Action run)[]
            {
                ("Desktop restores empty chart and skin roots", DesktopRestoresEmptyRoots),
                ("Desktop preserves nonempty managed roots", DesktopPreservesExistingRoots),
                ("Mobile initial extraction moves managed assets", MobileInitialExtraction),
                ("Mobile extraction preserves existing managed assets", MobilePreservesExistingManagedAssets),
                ("Mobile incomplete extraction retries safely", MobileRetriesIncompleteExtraction),
                ("Mobile update respects official hashes and player choices", MobileUpdatePreservesPlayerChoices),
                ("Mobile rejects packaged hash mismatch", MobileRejectsPackagedHashMismatch),
                ("Mobile resumes pending managed moves", MobileResumesPendingMoves),
                ("Mobile atomic commitment failure cleans temporary file", MobileCleansFailedAtomicWrite)
            };
            var failures = 0;
            foreach (var (name, run) in cases)
            {
                try
                {
                    run();
                    Console.WriteLine($"PASS {name}");
                }
                catch (Exception exception)
                {
                    failures++;
                    Console.WriteLine($"FAIL {name}: {exception}");
                }
            }
            Console.WriteLine($"{cases.Length - failures}/{cases.Length} cases passed.");
            Console.WriteLine(failures == 0
                ? "RESOURCE_STORAGE_VALIDATION_PASSED"
                : "RESOURCE_STORAGE_VALIDATION_FAILED");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>Checks desktop copying creates nested charts and skins in otherwise empty roots.</summary>
        private static void DesktopRestoresEmptyRoots()
        {
            using var fixture = new Fixture();
            fixture.Package("MaiCharts/Original/song/maidata.txt", "official chart");
            fixture.Package("Skins/default/skin.json", "official skin");
            var packagedChartPath = Path.Combine(Application.streamingAssetsPath, "MaiCharts/Original/song/maidata.txt");
            var timestamp = new DateTime(2023, 4, 5, 6, 7, 8, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(packagedChartPath, timestamp);
            File.SetAttributes(packagedChartPath, File.GetAttributes(packagedChartPath) | FileAttributes.Hidden);
            DesktopResourceRestorer.RestoreManagedAssetsIfMissing();
            fixture.AssertText("MaiCharts/Original/song/maidata.txt", "official chart");
            fixture.AssertText("Skins/default/skin.json", "official skin");
            var restoredChartPath = fixture.PathFor("MaiCharts/Original/song/maidata.txt");
            Assert(File.GetLastWriteTimeUtc(restoredChartPath) == timestamp,
                "Desktop restore must preserve the packaged modification timestamp.");
            if (OperatingSystem.IsWindows())
            {
                Assert((File.GetAttributes(restoredChartPath) & FileAttributes.Hidden) != 0,
                    "Desktop restore must preserve the packaged hidden attribute on Windows.");
            }
            AssertNoErrors();
        }

        /// <summary>Checks a populated chart root stays intact while an empty skin root is restored.</summary>
        private static void DesktopPreservesExistingRoots()
        {
            using var fixture = new Fixture();
            fixture.Package("MaiCharts/Original/maidata.txt", "official chart");
            fixture.Package("Skins/default/skin.json", "official skin");
            fixture.Write("MaiCharts/custom/maidata.txt", "player chart");
            DesktopResourceRestorer.RestoreManagedAssetsIfMissing();
            fixture.AssertText("MaiCharts/custom/maidata.txt", "player chart");
            fixture.AssertMissing("MaiCharts/Original/maidata.txt");
            fixture.AssertText("Skins/default/skin.json", "official skin");
            AssertNoErrors();
        }

        /// <summary>Checks initial extraction commits unmanaged files before moving bundled managed directories.</summary>
        private static void MobileInitialExtraction()
        {
            using var fixture = new Fixture();
            fixture.Package("Config/options.txt", "official options");
            fixture.Package("MaiCharts/Original/song/maidata.txt", "official chart");
            fixture.Package("Skins/default/skin.json", "official skin");
            InitializeMobile();
            AssertNoErrors();
            fixture.AssertText("Assets/Config/options.txt", "official options");
            fixture.AssertText("MaiCharts/Original/song/maidata.txt", "official chart");
            fixture.AssertText("Skins/default/skin.json", "official skin");
            fixture.AssertMissing("Assets/MaiCharts/Original");
            fixture.AssertMissing("Assets/Skins/default");
            fixture.AssertMissing("Assets/.pending-managed-asset-moves");
            fixture.AssertMissing("Assets.extracting-v2");
            AssertNoErrors();
        }

        /// <summary>Checks extraction never overwrites an existing customized player-managed tree.</summary>
        private static void MobilePreservesExistingManagedAssets()
        {
            using var fixture = new Fixture();
            fixture.Package("Config/options.txt", "official options");
            fixture.Package("MaiCharts/Original/maidata.txt", "new official chart", "old official chart", changed: true);
            fixture.Package("Skins/default/skin.json", "official skin");
            fixture.Write("MaiCharts/Original/maidata.txt", "player customization");
            InitializeMobile();
            fixture.AssertText("MaiCharts/Original/maidata.txt", "player customization");
            fixture.AssertText("Skins/default/skin.json", "official skin");
            fixture.AssertMissing("Assets/.pending-managed-asset-moves");
            AssertNoErrors();
        }

        /// <summary>Checks missing packaged input prevents commitment and a later retry recreates the staging tree.</summary>
        private static void MobileRetriesIncompleteExtraction()
        {
            using var fixture = new Fixture();
            fixture.Package("MaiCharts/Original/maidata.txt", "official chart");
            fixture.Package("Skins/default/skin.json", "official skin");
            ResourceManifestLoader.V2Hashes["Config/missing.txt"] = Hash("later options");
            InitializeMobile();
            fixture.AssertMissing("Assets");
            fixture.AssertMissing("MaiCharts/Original");
            Assert(MajDebug.Errors.Count != 0, "Incomplete extraction must log the failed source.");
            MajDebug.Errors.Clear();
            fixture.Package("Config/missing.txt", "later options");
            InitializeMobile();
            fixture.AssertText("Assets/Config/missing.txt", "later options");
            fixture.AssertText("MaiCharts/Original/maidata.txt", "official chart");
            fixture.AssertMissing("Assets.extracting-v2");
            AssertNoErrors();
        }

        /// <summary>Checks hash-gated replacement, customization/deletion protection, and missing resource synchronization.</summary>
        private static void MobileUpdatePreservesPlayerChoices()
        {
            using var fixture = new Fixture();
            fixture.Package("Config/official.txt", "new official", "old official", changed: true);
            fixture.Package("Config/custom.txt", "new official", "old official", changed: true);
            fixture.Package("Config/player.txt", "new path official", changed: true);
            fixture.Package("Config/missing.txt", "missing official");
            fixture.Package("MaiCharts/Original/deleted.txt", "new chart", "old chart", changed: true);
            fixture.Package("MaiCharts/Original/new.txt", "new chart path", changed: true);
            fixture.Package("Skins/default/missing.txt", "missing skin");
            fixture.Write("Assets/Config/official.txt", "old official");
            fixture.Write("Assets/Config/custom.txt", "player customization");
            fixture.Write("Assets/Config/player.txt", "player new path");
            InitializeMobile();
            AssertNoErrors();
            fixture.AssertText("Assets/Config/official.txt", "new official");
            fixture.AssertText("Assets/Config/custom.txt", "player customization");
            fixture.AssertText("Assets/Config/player.txt", "player new path");
            fixture.AssertText("Assets/Config/missing.txt", "missing official");
            fixture.AssertMissing("MaiCharts/Original/deleted.txt");
            fixture.AssertMissing("MaiCharts/Original/new.txt");
            fixture.AssertMissing("Skins/default/missing.txt");
            Assert(!Directory.EnumerateFiles(MajEnv.AssetsPath, "*.tmp", SearchOption.AllDirectories).Any(),
                "Successful updates must consume their temporary files.");
            AssertNoErrors();
        }

        /// <summary>Checks a bad package hash prevents replacement of an otherwise eligible official local file.</summary>
        private static void MobileRejectsPackagedHashMismatch()
        {
            using var fixture = new Fixture();
            fixture.Package("Config/official.txt", "corrupt package", "old official", changed: true);
            ResourceManifestLoader.V2Hashes["Config/official.txt"] = Hash("expected official");
            fixture.Write("Assets/Config/official.txt", "old official");
            InitializeMobile();
            fixture.AssertText("Assets/Config/official.txt", "old official");
            Assert(MajDebug.Errors.Any(message => message.Contains("hash mismatch", StringComparison.Ordinal)),
                "The rejected package hash must be reported.");
        }

        /// <summary>Checks an interrupted managed move resumes while protecting an existing destination tree.</summary>
        private static void MobileResumesPendingMoves()
        {
            using var fixture = new Fixture();
            fixture.Write("Assets/.pending-managed-asset-moves", string.Empty);
            fixture.Write("Assets/MaiCharts/Original/maidata.txt", "staged official chart");
            fixture.Write("Assets/Skins/default/skin.json", "staged official skin");
            fixture.Write("MaiCharts/Original/maidata.txt", "player customization");
            InitializeMobile();
            fixture.AssertText("MaiCharts/Original/maidata.txt", "player customization");
            fixture.AssertText("Skins/default/skin.json", "staged official skin");
            fixture.AssertMissing("Assets/MaiCharts/Original");
            fixture.AssertMissing("Assets/.pending-managed-asset-moves");
            AssertNoErrors();
        }

        /// <summary>Checks a destination directory collision leaves the directory intact and removes staged bytes.</summary>
        private static void MobileCleansFailedAtomicWrite()
        {
            using var fixture = new Fixture();
            var destination = fixture.PathFor("Assets/conflict.bin");
            Directory.CreateDirectory(destination);
            var failed = false;
            try
            {
                InvokeMobile("WriteAllBytesAtomically", destination, Encoding.UTF8.GetBytes("replacement"));
            }
            catch (IOException)
            {
                failed = true;
            }
            Assert(failed, "Committing a staged file over a directory must fail.");
            Assert(Directory.Exists(destination), "Commitment failure must preserve the existing directory.");
            Assert(!Directory.EnumerateFiles(MajEnv.AssetsPath, "*.tmp").Any(),
                "Commitment failure must remove the new temporary file.");
            AssertNoErrors();
        }

        /// <summary>Invokes the production initialization entry without substituting its resource behavior.</summary>
        private static void InitializeMobile()
        {
            InvokeMobile("InitializeOrUpdate");
        }

        /// <summary>Invokes a private production updater method and preserves its underlying exception type.</summary>
        /// <param name="name">The production method name.</param>
        /// <param name="arguments">The arguments expected by that method.</param>
        /// <exception cref="MissingMethodException">The linked method no longer exists.</exception>
        private static void InvokeMobile(string name, params object[] arguments)
        {
            var method = typeof(MobileResourceUpdater).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(typeof(MobileResourceUpdater).FullName, name);
            try
            {
                method.Invoke(null, arguments);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            }
        }

        /// <summary>Computes the same lowercase SHA-256 representation used by validated resource manifests.</summary>
        /// <param name="text">The UTF-8 fixture content.</param>
        /// <returns>The content's lowercase SHA-256 hexadecimal hash.</returns>
        private static string Hash(string text)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        }

        /// <summary>Fails when the production workflow logged an unexpected resource error.</summary>
        /// <exception cref="InvalidOperationException">An error was recorded.</exception>
        private static void AssertNoErrors()
        {
            Assert(MajDebug.Errors.Count == 0, string.Join(Environment.NewLine, MajDebug.Errors));
        }

        /// <summary>Fails a case when a behavioral requirement is not satisfied.</summary>
        /// <param name="condition">Whether the requirement was satisfied.</param>
        /// <param name="message">The failure context.</param>
        /// <exception cref="InvalidOperationException">The condition is false.</exception>
        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        /// <summary>Owns one isolated tree and resets all narrow global substitutes for a validation case.</summary>
        private sealed class Fixture : IDisposable
        {
            /// <summary>Owns the absolute fixture root beneath the ignored validation work directory.</summary>
            private readonly string _root;

            /// <summary>Creates isolated app and package roots with empty player-managed directories.</summary>
            public Fixture()
            {
                var workRoot = Path.GetFullPath(Path.Combine("Temp", "ResourceStorageValidation", "work"));
                _root = Path.Combine(workRoot, Guid.NewGuid().ToString("N"));
                MajEnv.RootPath = Path.Combine(_root, "App");
                Application.streamingAssetsPath = Path.Combine(_root, "Package");
                Directory.CreateDirectory(MajEnv.ChartPath);
                Directory.CreateDirectory(MajEnv.SkinPath);
                Directory.CreateDirectory(Application.streamingAssetsPath);
                ResourceManifestLoader.V1Hashes.Clear();
                ResourceManifestLoader.V2Hashes.Clear();
                ResourceManifestLoader.DiffPaths.Clear();
                MajDebug.Errors.Clear();
            }

            /// <summary>Writes packaged fixture content and registers its validated manifest entries.</summary>
            /// <param name="relativePath">The portable packaged relative path.</param>
            /// <param name="text">The packaged content.</param>
            /// <param name="previousText">Previous official content, when upgrading an existing resource.</param>
            /// <param name="changed">Whether the path appears in the official update diff.</param>
            public void Package(string relativePath, string text, string? previousText = null, bool changed = false)
            {
                WriteText(Path.Combine(Application.streamingAssetsPath, relativePath), text);
                ResourceManifestLoader.V2Hashes[relativePath] = Hash(text);
                if (previousText is not null)
                {
                    ResourceManifestLoader.V1Hashes[relativePath] = Hash(previousText);
                }
                if (changed)
                {
                    ResourceManifestLoader.DiffPaths.Add(relativePath);
                }
            }

            /// <summary>Writes local application fixture content without involving a production operation.</summary>
            /// <param name="relativePath">The path relative to the fixture's application root.</param>
            /// <param name="text">The local content to seed.</param>
            public void Write(string relativePath, string text)
            {
                WriteText(PathFor(relativePath), text);
            }

            /// <summary>Resolves an application fixture path.</summary>
            /// <param name="relativePath">The path relative to the fixture's application root.</param>
            /// <returns>The absolute local fixture path.</returns>
            public string PathFor(string relativePath)
            {
                return Path.Combine(MajEnv.RootPath, relativePath);
            }

            /// <summary>Checks committed fixture bytes directly through the platform filesystem.</summary>
            /// <param name="relativePath">The path relative to the application root.</param>
            /// <param name="expectedText">The expected decoded content.</param>
            /// <exception cref="InvalidOperationException">The committed file content differs.</exception>
            public void AssertText(string relativePath, string expectedText)
            {
                Assert(File.ReadAllText(PathFor(relativePath)) == expectedText,
                    $"Unexpected content at {relativePath}.");
            }

            /// <summary>Checks that neither a local file nor a local directory occupies a fixture path.</summary>
            /// <param name="relativePath">The path relative to the application root.</param>
            /// <exception cref="InvalidOperationException">A filesystem entry still exists.</exception>
            public void AssertMissing(string relativePath)
            {
                var path = PathFor(relativePath);
                Assert(!File.Exists(path) && !Directory.Exists(path), $"Unexpected entry at {relativePath}.");
            }

            /// <summary>Removes only this case's verified fixture tree.</summary>
            /// <exception cref="InvalidOperationException">The cleanup target escaped the work directory.</exception>
            public void Dispose()
            {
                var workRoot = Path.GetFullPath(Path.Combine("Temp", "ResourceStorageValidation", "work"))
                    + Path.DirectorySeparatorChar;
                Assert(Path.GetFullPath(_root).StartsWith(workRoot, StringComparison.OrdinalIgnoreCase),
                    "Fixture cleanup target escaped the validation work directory.");
                Directory.Delete(_root, recursive: true);
            }

            /// <summary>Seeds fixture bytes and creates their parent directories.</summary>
            /// <param name="path">The fixture's absolute local destination.</param>
            /// <param name="text">The UTF-8 content to seed without a byte order mark.</param>
            private static void WriteText(string path, string text)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text, new UTF8Encoding(false));
            }
        }
    }
}
