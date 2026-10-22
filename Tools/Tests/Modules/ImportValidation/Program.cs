#nullable enable
using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.Import;
using MajdataPlay.IO.Storage;
using NativeDirectory = System.IO.Directory;
using NativeFile = System.IO.File;

namespace MajdataPlay.Tests.ImportValidation
{
    /// <summary>Exercises the production chart importer's archive and directory layout rules.</summary>
    internal static class Program
    {
        /// <summary>Runs the chart import layout regressions with isolated temporary data.</summary>
        /// <returns>Zero after all cases pass, or one when any case fails.</returns>
        private static async Task<int> Main()
        {
            var workspacePath = ResolveWorkspacePath();
            var rootPath = Path.Combine(workspacePath, Guid.NewGuid().ToString("N"));
            NativeDirectory.CreateDirectory(rootPath);
            try
            {
                await TestChartArchiveAsync(rootPath);
                await TestCollectionArchiveAsync(rootPath);
                await TestExistingCollectionAsync(rootPath);
                await TestChartNameCollisionAsync(rootPath);
                await TestQueuedDirectoryAsync(rootPath);
                await TestQueuedChartDirectoryAsync(rootPath);
                await TestIgnoredLayoutsAsync(rootPath);
                await TestUnreadableArchiveAsync(rootPath);
                await TestOpaqueChartRootAsync(rootPath);
                Console.WriteLine("IMPORT_VALIDATION_PASSED (9 cases)");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                Console.Error.WriteLine("IMPORT_VALIDATION_FAILED");
                return 1;
            }
            finally
            {
                if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(rootPath)), workspacePath, StringComparison.OrdinalIgnoreCase)
                    || (NativeFile.GetAttributes(rootPath) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Refusing cleanup outside the owned import fixture directory.");
                }
                NativeDirectory.Delete(rootPath, true);
            }
        }

        /// <summary>Checks that an archive holding one chart folder imports that chart into the import collection.</summary>
        /// <param name="rootPath">The fixture root shared by the cases.</param>
        /// <returns>A task that completes after the case assertions.</returns>
        private static async Task TestChartArchiveAsync(string rootPath)
        {
            var chartPath = PrepareCase(rootPath, "ChartArchive");
            var archivePath = Path.Combine(chartPath, "single.zip");
            WriteArchive(archivePath, ChartEntries("My Chart"));

            await ImportAsync(archivePath);

            RequireChart(Path.Combine(chartPath, "Import", "My Chart"));
            Require(NativeFile.Exists(Path.Combine(chartPath, "Import", "My Chart", "bg.png")), "A chart folder must be copied with its extra files.");
            Require(!NativeDirectory.Exists(Path.Combine(chartPath, "My Chart")), "A single chart folder must not become a collection.");
            Require(NativeDirectory.GetDirectories(MajEnv.TempDirectory.Location).Length == 0, "The extraction directory must be removed after the import.");
            Require(NativeFile.Exists(archivePath), "A local archive must be kept because consuming it is an Android-only behavior.");
            RequireNoErrors();
            Console.WriteLine("PASS archive: one chart folder imports into the Import collection");
        }

        /// <summary>Checks that a collection layout inside an archive imports every chart into that named collection.</summary>
        /// <param name="rootPath">The fixture root shared by the cases.</param>
        /// <returns>A task that completes after the case assertions.</returns>
        private static async Task TestCollectionArchiveAsync(string rootPath)
        {
            var chartPath = PrepareCase(rootPath, "CollectionArchive");
            var entries = new (string Name, string Content)[]
            {
                ("Original/EX-AID/maidata.txt", "&title=Fixture\n"),
                ("Original/EX-AID/track.mp3", "ID3"),
                ("Original/Song B/maidata.txt", "&title=Fixture\n"),
                ("Original/Song B/track.mp3", "ID3"),
                ("Original/cover.png", "PNG"),
            };
            WriteArchive(Path.Combine(chartPath, "collection.adx"), entries);

            await ImportAsync(Path.Combine(chartPath, "collection.adx"));

            Require(!NativeDirectory.Exists(Path.Combine(chartPath, "Import", "Original")), "A collection must not be imported as a chart of the Import collection.");
            RequireChart(Path.Combine(chartPath, "Original", "EX-AID"));
            RequireChart(Path.Combine(chartPath, "Original", "Song B"));
            Require(!NativeFile.Exists(Path.Combine(chartPath, "Original", "cover.png")), "A collection must receive only its chart folders.");
            RequireNoErrors();
            Console.WriteLine("PASS archive: a collection layout creates the named collection with its charts");
        }

        /// <summary>Checks that an existing collection is extended instead of duplicated.</summary>
        /// <param name="rootPath">The fixture root shared by the cases.</param>
        /// <returns>A task that completes after the case assertions.</returns>
        private static async Task TestExistingCollectionAsync(string rootPath)
        {
            var chartPath = PrepareCase(rootPath, "ExistingCollection");
            var collectionPath = Path.Combine(chartPath, "Original");
            NativeDirectory.CreateDirectory(collectionPath);
            NativeFile.WriteAllText(Path.Combine(collectionPath, "keep.txt"), "preserved");
            WriteChart(Path.Combine(collectionPath, "EX-AID"));
            NativeFile.WriteAllText(Path.Combine(collectionPath, "EX-AID", "existing.txt"), "preserved");
            WriteArchive(Path.Combine(chartPath, "update.zip"), ChartEntries("Original/EX-AID"));

            await ImportAsync(Path.Combine(chartPath, "update.zip"));

            Require(NativeFile.Exists(Path.Combine(collectionPath, "keep.txt")), "An existing collection must keep its own files.");
            Require(NativeFile.Exists(Path.Combine(collectionPath, "EX-AID", "existing.txt")), "An existing chart must keep its own files.");
            RequireChart(Path.Combine(collectionPath, "EX-AID (1)"));
            Require(!NativeDirectory.Exists(Path.Combine(collectionPath, "Original (1)")), "An existing collection must not be renamed.");
            RequireNoErrors();
            Console.WriteLine("PASS archive: an existing collection is reused and a colliding chart gets a new name");
        }

        /// <summary>Checks that a colliding chart name inside the import collection stays unique.</summary>
        /// <param name="rootPath">The fixture root shared by the cases.</param>
        /// <returns>A task that completes after the case assertions.</returns>
        private static async Task TestChartNameCollisionAsync(string rootPath)
        {
            var chartPath = PrepareCase(rootPath, "ChartCollision");
            var importPath = Path.Combine(chartPath, "Import");
            NativeDirectory.CreateDirectory(Path.Combine(importPath, "My Chart"));
            NativeFile.WriteAllText(Path.Combine(importPath, "My Chart", "existing.txt"), "preserved");
            WriteArchive(Path.Combine(chartPath, "single.zip"), ChartEntries("My Chart"));

            await ImportAsync(Path.Combine(chartPath, "single.zip"));

            Require(NativeFile.Exists(Path.Combine(importPath, "My Chart", "existing.txt")), "An existing chart must keep its own files.");
            RequireChart(Path.Combine(importPath, "My Chart (1)"));
            RequireNoErrors();
            Console.WriteLine("PASS archive: a colliding chart name is renamed instead of overwritten");
        }

        /// <summary>Checks that a queued directory follows the archive layout rules.</summary>
        /// <param name="rootPath">The fixture root shared by the cases.</param>
        /// <returns>A task that completes after the case assertions.</returns>
        private static async Task TestQueuedDirectoryAsync(string rootPath)
        {
            var chartPath = PrepareCase(rootPath, "QueuedDirectory");
            var packagePath = Path.Combine(chartPath, "package");
            WriteChart(Path.Combine(packagePath, "Sub"));
            WriteChart(Path.Combine(packagePath, "Coll", "Sub2"));
            NativeFile.WriteAllText(Path.Combine(packagePath, "notes.txt"), "ignored");

            await ImportAsync(packagePath);

            RequireChart(Path.Combine(chartPath, "Import", "Sub"));
            RequireChart(Path.Combine(chartPath, "Coll", "Sub2"));
            Require(!NativeDirectory.Exists(Path.Combine(chartPath, "Import", "Coll")), "A nested collection must not be imported as a chart of the Import collection.");
            RequireNoErrors();
            Console.WriteLine("PASS directory: a queued folder imports one-level charts and a named collection");
        }

        /// <summary>Checks that a queued directory which is itself a chart imports as a single chart.</summary>
        /// <param name="rootPath">The fixture root shared by the cases.</param>
        /// <returns>A task that completes after the case assertions.</returns>
        private static async Task TestQueuedChartDirectoryAsync(string rootPath)
        {
            var chartPath = PrepareCase(rootPath, "QueuedChartDirectory");
            var soloPath = Path.Combine(chartPath, "Solo");
            WriteChart(soloPath);

            await ImportAsync(soloPath);

            RequireChart(Path.Combine(chartPath, "Import", "Solo"));
            Require(NativeDirectory.GetDirectories(chartPath).Length == 2, "A queued chart folder must be copied instead of moved.");
            RequireNoErrors();
            Console.WriteLine("PASS directory: a queued chart folder imports as one chart of the Import collection");
        }

        /// <summary>Checks that non-chart and dot-prefixed entries import nothing.</summary>
        /// <param name="rootPath">The fixture root shared by the cases.</param>
        /// <returns>A task that completes after the case assertions.</returns>
        private static async Task TestIgnoredLayoutsAsync(string rootPath)
        {
            var chartPath = PrepareCase(rootPath, "IgnoredLayouts");
            var entries = new (string Name, string Content)[]
            {
                (".hidden/maidata.txt", "&title=Fixture\n"),
                (".hidden/track.mp3", "ID3"),
                ("Notes/readme.txt", "nothing to import"),
                ("Notes/cover.png", "PNG"),
                ("My Chart/maidata.txt", "&title=Fixture\n"),
            };
            WriteArchive(Path.Combine(chartPath, "ignored.zip"), entries);

            await ImportAsync(Path.Combine(chartPath, "ignored.zip"));

            Require(NativeDirectory.Exists(Path.Combine(chartPath, "Import")), "The Import collection must exist after an import.");
            Require(NativeDirectory.GetDirectories(Path.Combine(chartPath, "Import")).Length == 0, "Entries without a chart file and track file must be ignored.");
            Require(!NativeDirectory.Exists(Path.Combine(chartPath, "Notes")), "A collection without any chart must not be created.");
            Require(!NativeDirectory.Exists(Path.Combine(chartPath, ".hidden")), "A dot-prefixed entry must be ignored.");
            RequireNoErrors();
            Console.WriteLine("PASS archive: entries without a chart file and track file import nothing");
        }

        /// <summary>Checks that an unreadable archive is reported without discarding the remaining queue.</summary>
        /// <param name="rootPath">The fixture root shared by the cases.</param>
        /// <returns>A task that completes after the case assertions.</returns>
        private static async Task TestUnreadableArchiveAsync(string rootPath)
        {
            var chartPath = PrepareCase(rootPath, "UnreadableArchive");
            var brokenPath = Path.Combine(chartPath, "broken.zip");
            NativeFile.WriteAllText(brokenPath, "this is not an archive");
            WriteArchive(Path.Combine(chartPath, "valid.zip"), ChartEntries("Valid Chart"));

            await ImportAsync(brokenPath, Path.Combine(chartPath, "valid.zip"));

            RequireChart(Path.Combine(chartPath, "Import", "Valid Chart"));
            Require(MajDebug.Errors.Count == 1, "An unreadable archive must report exactly one error.");
            Require(MajDebug.Errors[0].Contains(brokenPath, StringComparison.Ordinal), "The reported error must name the unreadable archive.");
            Require(NativeDirectory.GetDirectories(MajEnv.TempDirectory.Location).Length == 0, "A failed extraction must not leave its directory behind.");
            Console.WriteLine("PASS archive: an unreadable archive is reported and the queue continues");
        }

        /// <summary>Checks that chart creation, collisions and file copying retain an opaque destination backend.</summary>
        /// <param name="rootPath">The fixture root shared by the cases.</param>
        /// <returns>A task that completes after validating imported charts and preserved entries.</returns>
        private static async Task TestOpaqueChartRootAsync(string rootPath)
        {
            var chartPath = PrepareCase(rootPath, "OpaqueChartRoot");
            var backend = new OpaqueFileSystem(chartPath);
            MajEnv.ChartDirectory = backend.Root;
            var packagePath = Path.Combine(chartPath, "package");
            var sourceChartPath = Path.Combine(packagePath, "Sub");
            WriteChart(sourceChartPath);
            NativeDirectory.CreateDirectory(Path.Combine(sourceChartPath, "extras"));
            NativeFile.WriteAllText(Path.Combine(sourceChartPath, "extras", "cover.png"), "PNG");
            var modificationTime = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            NativeFile.SetLastWriteTimeUtc(Path.Combine(sourceChartPath, "maidata.txt"), modificationTime);
            WriteChart(Path.Combine(packagePath, "Coll", "Nested"));
            var importPath = Path.Combine(chartPath, "Import");
            NativeDirectory.CreateDirectory(Path.Combine(importPath, "Sub"));
            NativeFile.WriteAllText(Path.Combine(importPath, "Sub", "keep.txt"), "preserved");

            await ImportAsync(packagePath);

            RequireChart(Path.Combine(importPath, "Sub (1)"));
            RequireChart(Path.Combine(chartPath, "Coll", "Nested"));
            Require(NativeFile.ReadAllText(Path.Combine(importPath, "Sub", "keep.txt")) == "preserved", "A directory collision must preserve the existing chart.");
            Require(NativeFile.ReadAllText(Path.Combine(importPath, "Sub (1)", "extras", "cover.png")) == "PNG", "Nested chart files must copy through the retained destination backend.");
            Require(NativeFile.GetLastWriteTimeUtc(Path.Combine(importPath, "Sub (1)", "maidata.txt")) == modificationTime, "Provider-backed copying must retain each file's modification time.");
            RequireNoErrors();
            Console.WriteLine("PASS opaque root: chart and collection creation retain the original destination backend");
        }

        /// <summary>Resolves the ignored validation workspace below the repository root.</summary>
        /// <returns>The absolute workspace path.</returns>
        /// <exception cref="DirectoryNotFoundException">No repository root exists above the current directory.</exception>
        private static string ResolveWorkspacePath()
        {
            var directory = new DirectoryInfo(NativeDirectory.GetCurrentDirectory());
            while (directory is not null)
            {
                if (NativeDirectory.Exists(Path.Combine(directory.FullName, "Assets")) &&
                    NativeDirectory.Exists(Path.Combine(directory.FullName, "Tools")))
                {
                    return Path.Combine(directory.FullName, "Temp", "ImportValidation", "Workspace");
                }
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Run this validation from inside the MajdataPlay repository.");
        }

        /// <summary>Creates an isolated chart root for one case and points the production importer at it.</summary>
        /// <param name="rootPath">The fixture root shared by the cases.</param>
        /// <param name="name">The case name used as its child directory name.</param>
        /// <returns>The chart root the case imports into.</returns>
        private static string PrepareCase(string rootPath, string name)
        {
            var casePath = Path.Combine(rootPath, name);
            var chartPath = Path.Combine(casePath, "MaiCharts");
            var tempPath = Path.Combine(casePath, "Temp");
            NativeDirectory.CreateDirectory(chartPath);
            NativeDirectory.CreateDirectory(tempPath);
            MajEnv.ChartDirectory = FileSystem.Directory.Open(chartPath);
            MajEnv.TempDirectory = FileSystem.Directory.Open(tempPath);
            MajDebug.Clear();
            return chartPath;
        }

        /// <summary>Queues the supplied paths and runs the production import flow to completion.</summary>
        /// <param name="paths">The archive or directory paths to import.</param>
        /// <returns>A task that completes after the importer refreshed the chart list.</returns>
        private static async Task ImportAsync(params string[] paths)
        {
            var refreshCount = SongStorage.RefreshCount;
            foreach (var path in paths)
            {
                ChartImporter.AddImportTask(path);
            }
            await ChartImporter.OnStartAsync();
            Require(SongStorage.RefreshCount == refreshCount + 1, "A completed import must refresh the chart list exactly once.");
        }

        /// <summary>Writes a minimal chart into a directory.</summary>
        /// <param name="chartDirectory">The chart directory to create.</param>
        private static void WriteChart(string chartDirectory)
        {
            NativeDirectory.CreateDirectory(chartDirectory);
            NativeFile.WriteAllText(Path.Combine(chartDirectory, "maidata.txt"), "&title=Fixture\n&artist=MajdataPlay\n");
            NativeFile.WriteAllBytes(Path.Combine(chartDirectory, "track.mp3"), new byte[] { 0x49, 0x44, 0x33 });
        }

        /// <summary>Builds the archive entries of one minimal chart.</summary>
        /// <param name="chartDirectoryName">The chart folder name inside the archive.</param>
        /// <returns>The relative entry names paired with their text contents.</returns>
        private static (string Name, string Content)[] ChartEntries(string chartDirectoryName)
        {
            return new (string Name, string Content)[]
            {
                ($"{chartDirectoryName}/maidata.txt", "&title=Fixture\n"),
                ($"{chartDirectoryName}/track.mp3", "ID3"),
                ($"{chartDirectoryName}/bg.png", "PNG"),
            };
        }

        /// <summary>Builds a ZIP archive from the supplied relative entry names and text contents.</summary>
        /// <param name="archivePath">The archive file to create.</param>
        /// <param name="entries">The relative entry names paired with their text contents.</param>
        private static void WriteArchive(string archivePath, (string Name, string Content)[] entries)
        {
            using var stream = NativeFile.Create(archivePath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        /// <summary>Fails the case unless the directory holds an imported chart.</summary>
        /// <param name="chartDirectory">The imported chart directory to inspect.</param>
        private static void RequireChart(string chartDirectory)
        {
            Require(NativeFile.Exists(Path.Combine(chartDirectory, "maidata.txt")), $"An imported chart must keep its maidata.txt: {chartDirectory}");
            Require(NativeFile.Exists(Path.Combine(chartDirectory, "track.mp3")), $"An imported chart must keep its track: {chartDirectory}");
        }

        /// <summary>Fails the case when the import reported any error.</summary>
        private static void RequireNoErrors()
        {
            if (MajDebug.Errors.Count == 0)
            {
                return;
            }
            throw new InvalidOperationException("The import reported: " + string.Join(" | ", MajDebug.Errors));
        }

        /// <summary>Fails the case when a required observable behavior does not hold.</summary>
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
