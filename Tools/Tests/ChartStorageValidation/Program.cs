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
    internal static class Program
    {
        /// <summary>Runs the collection and chart-settings regressions with isolated temporary data.</summary>
        /// <returns>Zero after all cases pass, or one when any case fails.</returns>
        private static async Task<int> Main()
        {
            var workspacePath = Path.GetFullPath(Path.Combine(NativeDirectory.GetCurrentDirectory(), "Temp", "ChartStorageValidation", "Workspace"));
            var rootPath = Path.Combine(workspacePath, Guid.NewGuid().ToString("N"));
            NativeDirectory.CreateDirectory(rootPath);
            try
            {
                TestCollectionIdentity(rootPath);
                TestCollectionMarkerCollision(rootPath);
                await TestChartSettings(rootPath);
                Console.WriteLine("CHART_STORAGE_VALIDATION_PASSED (3 cases)");
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
                if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(rootPath)), workspacePath, StringComparison.OrdinalIgnoreCase)
                    || (NativeFile.GetAttributes(rootPath) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Refusing cleanup outside the owned chart-storage fixture directory.");
                }
                NativeDirectory.Delete(rootPath, true);
            }
        }

        /// <summary>Checks newly created collection IDs, stable reloads and replacement of invalid ID text.</summary>
        /// <param name="rootPath">The case-owned fixture root.</param>
        private static void TestCollectionIdentity(string rootPath)
        {
            var collectionPath = Path.Combine(rootPath, "Collection");
            NativeDirectory.CreateDirectory(collectionPath);
            var collection = SongCollection.Empty(collectionPath, "Collection");
            var markerPath = Path.Combine(collectionPath, ".MajdataPlay");
            var idPath = Path.Combine(markerPath, "id");
            Require(collection.Id != Guid.Empty, "A local collection must receive a nonempty ID.");
            Require(Guid.Parse(NativeFile.ReadAllText(idPath)) == collection.Id, "The created ID file must match the collection ID.");
            Require(FileSystem.OpenDirectory(markerPath).Entry!.IsHidden, "The newly created marker directory must stay hidden.");
            Require(SongCollection.Empty(collectionPath, "Collection").Id == collection.Id, "Reloading must preserve the stored ID.");

            NativeFile.WriteAllText(idPath, "invalid and longer than a stored GUID................................................................");
            var recovered = SongCollection.Empty(collectionPath, "Collection");
            Require(recovered.Id != Guid.Empty, "Malformed ID text must be replaced with a valid ID.");
            Require(Guid.Parse(NativeFile.ReadAllText(idPath)) == recovered.Id, "Replacing malformed ID text must truncate the file.");
            Require(SongCollection.Empty(collectionPath, "Collection").Id == recovered.Id, "The replacement ID must survive reload.");
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
                SongCollection.Empty(collectionPath, "Collision");
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
            MajEnv.RootPath = Path.Combine(rootPath, "Settings");
            NativeDirectory.CreateDirectory(MajEnv.RootPath);
            var settingsPath = Path.Combine(MajEnv.RootPath, "ChartSetting.db");
            const string malformedJson = "{this is not an array or valid JSON}";
            NativeFile.WriteAllText(settingsPath, malformedJson);
            NativeFile.SetLastWriteTimeUtc(settingsPath, new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            var originalWriteTime = NativeFile.GetLastWriteTimeUtc(settingsPath);
            await ChartSettingStorage.InitAsync();
            var backups = NativeDirectory.GetFiles(MajEnv.RootPath, "ChartSetting.db.*.bak");
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
            Require(NativeDirectory.GetFiles(MajEnv.RootPath, "ChartSetting.db.*.bak").Single() == backups[0], "Saving must preserve the earlier malformed-data backup.");
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
