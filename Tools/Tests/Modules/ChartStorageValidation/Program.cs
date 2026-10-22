#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MajdataPlay.Collections;
using MajdataPlay.IO.Storage;
using MajdataPlay.Settings;
using MajdataPlay.Utils;
using NativeDirectory = System.IO.Directory;
using NativeFile = System.IO.File;

namespace MajdataPlay.Tests.ChartStorageValidation
{
    /// <summary>Exercises the production consumers' file creation, replacement and backup behavior.</summary>
    internal static partial class Program
    {
        /// <summary>Runs every managed regression group with test-owned storage fixtures.</summary>
        /// <param name="arguments">Selects the isolated online-first scenario when requested by the parent runner.</param>
        /// <returns>Zero after all cases pass, or one when any case fails.</returns>
        private static async Task<int> Main(string[] arguments)
        {
            var workspacePath = Path.GetFullPath(Path.Combine(NativeDirectory.GetCurrentDirectory(), "Temp", "ChartStorageValidation", "Workspace"));
            var rootPath = Path.Combine(workspacePath, Guid.NewGuid().ToString("N"));
            NativeDirectory.CreateDirectory(rootPath);
            try
            {
                var cases = arguments.Contains("--online-first", StringComparer.Ordinal)
                    ? new (string Name, Func<Task> Run)[] { ("standalone online first refresh", () => TestOnlineFirstRefresh(rootPath)) }
                    : new (string Name, Func<Task> Run)[]
                {
                    ("collection identity", () => RunSynchronousCase(() => TestCollectionIdentity(rootPath))),
                    ("collection marker collision", () => RunSynchronousCase(() => TestCollectionMarkerCollision(rootPath))),
                    ("chart settings", () => TestChartSettings(rootPath)),
                    ("collection refresh", () => TestCollectionRefresh(rootPath)),
                    ("collection refresh rollback", () => TestCollectionRefreshRollback(rootPath)),
                    ("virtual and online refresh", () => TestVirtualAndOnlineRefresh(rootPath)),
                    ("scanner isolation and order", () => TestScannerIsolationAndOrder(rootPath)),
                    ("supported resource names", () => TestSupportedResourceNames(rootPath)),
                    ("scanner cancellation", () => TestScannerCancellation(rootPath)),
                    ("scanner shared budget", () => TestScannerSharedBudget(rootPath)),
                    ("root collection scheduling", () => TestLocalRootScheduling(rootPath)),
                    ("storage snapshot commit", () => TestStorageSnapshotCommit(rootPath)),
                    ("online cache directories", () => TestOnlineCacheDirectories(rootPath)),
                    ("retained online origins", () => TestRetainedOnlineOrigins(rootPath)),
                    ("standalone online first refresh", RunOnlineFirstProcessAsync),
                    ("independent source refreshes", () => TestIndependentSourceRefresh(rootPath))
                };
                var failedCases = 0;
                foreach (var testCase in cases)
                {
                    try
                    {
                        await testCase.Run();
                    }
                    catch (Exception exception)
                    {
                        failedCases++;
                        Console.Error.WriteLine("FAILED CASE: " + testCase.Name);
                        Console.Error.WriteLine(exception);
                    }
                }
                if (failedCases != 0)
                {
                    Console.Error.WriteLine("CHART_STORAGE_VALIDATION_FAILED (" + failedCases + " of " + cases.Length + " cases)");
                    return 1;
                }
                Console.WriteLine("CHART_STORAGE_VALIDATION_PASSED (" + cases.Length + " cases)");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                Console.Error.WriteLine("CHART_STORAGE_VALIDATION_FAILED");
                return 1;
            }
            finally
            {
                GameManager.Quit();
                if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(rootPath)), workspacePath, StringComparison.OrdinalIgnoreCase)
                    || (NativeFile.GetAttributes(rootPath) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Refusing cleanup outside the owned chart-storage fixture directory.");
                }
                NativeDirectory.Delete(rootPath, true);
            }
        }

        /// <summary>Adapts a synchronous case into the asynchronous case runner without changing its thread.</summary>
        /// <param name="action">The synchronous assertions to execute.</param>
        /// <returns>A completed task after the assertions return.</returns>
        private static Task RunSynchronousCase(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        /// <summary>Checks newly created collection IDs, stable reloads and replacement of invalid ID text.</summary>
        /// <param name="rootPath">The case-owned fixture root.</param>
        private static void TestCollectionIdentity(string rootPath)
        {
            var collectionPath = Path.Combine(rootPath, "Collection");
            NativeDirectory.CreateDirectory(collectionPath);
            var collection = SongCollection.Empty(FileSystem.Directory.Open(collectionPath), "Collection");
            var markerPath = Path.Combine(collectionPath, ".MajdataPlay");
            var idPath = Path.Combine(markerPath, "id");
            Require(collection.Id != Guid.Empty, "A local collection must receive a nonempty ID.");
            Require(Guid.Parse(NativeFile.ReadAllText(idPath)) == collection.Id, "The created ID file must match the collection ID.");
            Require(FileSystem.Directory.Open(markerPath).Entry!.IsHidden, "The newly created marker directory must stay hidden.");
            Require(SongCollection.Empty(FileSystem.Directory.Open(collectionPath), "Collection").Id == collection.Id, "Reloading must preserve the stored ID.");

            NativeFile.WriteAllText(idPath, "invalid and longer than a stored GUID................................................................");
            var recovered = SongCollection.Empty(FileSystem.Directory.Open(collectionPath), "Collection");
            Require(recovered.Id != Guid.Empty, "Malformed ID text must be replaced with a valid ID.");
            Require(Guid.Parse(NativeFile.ReadAllText(idPath)) == recovered.Id, "Replacing malformed ID text must truncate the file.");
            Require(SongCollection.Empty(FileSystem.Directory.Open(collectionPath), "Collection").Id == recovered.Id, "The replacement ID must survive reload.");
            Console.WriteLine("PASS collection: first creation, reload and malformed ID recovery");
        }

        /// <summary>Checks that marker-directory creation does not replace an existing sibling file.</summary>
        /// <param name="rootPath">The case-owned fixture root.</param>
        private static void TestCollectionMarkerCollision(string rootPath)
        {
            var collectionPath = Path.Combine(rootPath, "Collision");
            NativeDirectory.CreateDirectory(collectionPath);
            var markerPath = Path.Combine(collectionPath, ".MajdataPlay");
            NativeFile.WriteAllText(markerPath, "preserve this sibling file");
            var rejected = false;
            try
            {
                SongCollection.Empty(FileSystem.Directory.Open(collectionPath), "Collision");
            }
            catch (IOException)
            {
                rejected = true;
            }
            Require(rejected, "A file occupying the marker name must reject directory creation.");
            Require(NativeFile.ReadAllText(markerPath) == "preserve this sibling file", "A marker collision must preserve the original file.");
            Console.WriteLine("PASS collection: marker type collision preserves the sibling file");
        }

        /// <summary>Checks malformed-data backup and save behavior against the linked production settings storage.</summary>
        /// <param name="rootPath">The case-owned fixture root.</param>
        /// <returns>A task which completes after all load/save assertions.</returns>
        private static async Task TestChartSettings(string rootPath)
        {
            var settingsDirectory = CreateFixtureDirectory(rootPath, "Settings");
            var backend = new OpaqueFileSystem(settingsDirectory);
            MajEnv.RootDirectory = backend.Root;
            var settingsPath = Path.Combine(settingsDirectory, "ChartSetting.db");
            const string malformedJson = "{this is not an array or valid JSON}";
            NativeFile.WriteAllText(settingsPath, malformedJson);
            NativeFile.SetLastWriteTimeUtc(settingsPath, new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            var originalWriteTime = NativeFile.GetLastWriteTimeUtc(settingsPath);
            await ChartSettingStorage.InitAsync();
            var backups = NativeDirectory.GetFiles(settingsDirectory, "ChartSetting.db.*.bak");
            Require(backups.Length == 1, "Malformed settings must be backed up beside the source file.");
            Require(NativeFile.ReadAllText(backups[0]) == malformedJson, "A malformed-settings backup must preserve the source content.");
            Require(NativeFile.GetLastWriteTimeUtc(backups[0]) == originalWriteTime, "A malformed-settings backup must preserve the source modification time.");

            NativeFile.Delete(settingsPath);
            var setting = ChartSettingStorage.GetSetting("fixture-hash");
            setting.AudioOffset = 123456.75f;
            GameManager.Save();
            Require(NativeFile.Exists(settingsPath), "Saving must create a missing settings file before writing.");
            var initialJson = NativeFile.ReadAllText(settingsPath);
            var (initialSuccess, initialSettings, initialError) = await Serializer.Json.TryDeserializeAsync<ChartSetting[]>(initialJson);
            Require(initialSuccess && initialError is null && initialSettings is { Length: 1 }, "The first saved file must contain one valid settings record.");
            Require(initialSettings![0].Hash == "fixture-hash" && initialSettings[0].AudioOffset == 123456.75f, "The first save must preserve the selected values.");

            setting.AudioOffset = 1f;
            GameManager.Save();
            var replacementJson = NativeFile.ReadAllText(settingsPath);
            var (replacementSuccess, replacementSettings, replacementError) = await Serializer.Json.TryDeserializeAsync<ChartSetting[]>(replacementJson);
            Require(replacementJson.Length < initialJson.Length, "The replacement fixture must exercise a shorter write.");
            Require(replacementSuccess && replacementError is null && replacementSettings is { Length: 1 }, "A shorter write must leave valid JSON without a stale suffix.");
            Require(replacementSettings![0].AudioOffset == 1f, "The replacement save must update the stored value.");
            Require(NativeDirectory.GetFiles(settingsDirectory, "ChartSetting.db.*.bak").Single() == backups[0], "Saving must preserve the earlier malformed-data backup.");
            Console.WriteLine("PASS settings: sibling backup, missing-file creation and truncating replacement");
        }

        /// <summary>Fails a case when a required observable behavior does not hold.</summary>
        /// <param name="condition">Whether the expected behavior occurred.</param>
        /// <param name="message">The failure context to report.</param>
        /// <exception cref="InvalidOperationException">The supplied condition is false.</exception>
        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
