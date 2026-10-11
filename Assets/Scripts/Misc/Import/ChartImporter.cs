using Cysharp.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO.Storage;
#if UNITY_ANDROID
using MajdataPlay.Platform.Android;
using MajdataPlay.Platform.Android.Runtime.Content;
using MajdataPlay.Platform.Android.Runtime.Java.Lang;
#endif
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
#define ANDROID_RUNTIME
#endif

#nullable enable
namespace MajdataPlay.Import
{
    internal static class ChartImporter
    {
        /// <summary>The chart definition file that marks a directory as an importable chart.</summary>
        private const string ChartFileName = "maidata.txt";

        /// <summary>The track file names that mark a directory as an importable chart together with <see cref="ChartFileName"/>.</summary>
        private static readonly string[] s_trackFileNames = new string[]
        {
            "track.mp3",
            "track.opus",
            "track.ogg",
            "track.aac",
            "track.wav",
        };

        private static string s_importRoot = string.Empty;
        private static ValueTask s_importTask = UniTask.CompletedTask;
        private static readonly ConcurrentQueue<FileSystemEntry> s_pendingImportTasks = new ConcurrentQueue<FileSystemEntry>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
#if ANDROID_RUNTIME
            AndroidRuntime.OnNewIntent += OnNewIntent;
#endif
        }

        public static ValueTask OnStartAsync()
        {
            s_importRoot = Path.Combine(MajEnv.ChartPath, "Import");
#if ANDROID_RUNTIME
            var intent = AndroidRuntime.CurrentActivity.GetIntent();
            if (intent != null)
            {
                OnNewIntent(null, intent);
            }
#endif
            if (s_pendingImportTasks.Count != 0)
            {
                s_importTask = Import();
                return s_importTask;
            }
            return UniTask.CompletedTask;
        }
        public static void AddImportTask(string tempFilePath)
        {
            var entry = FileSystem.GetEntry(tempFilePath);
            if (entry != null)
            {
                s_pendingImportTasks.Enqueue(entry);
            }
        }
        public static void OnUpdate()
        {
            if (SceneSwitcher.CurrentScene == MajScenes.Empty ||
                s_pendingImportTasks.Count == 0 ||
                !s_importTask.IsCompleted)
            {
                return;
            }
            s_importTask = Import();
        }
        /// <summary>Imports queued local chart archives and chart directories with traversal protection and storage-backed extraction.</summary>
        /// <remarks>
        /// A queued <c>.zip</c> or <c>.adx</c> archive is extracted as a ZIP archive, and a queued directory is walked directly.
        /// Both then follow the same layout rules: <c>&lt;chart&gt;/maidata.txt</c> becomes a chart of the <c>Import</c> collection,
        /// while <c>&lt;collection&gt;/&lt;chart&gt;/maidata.txt</c> becomes a chart of the named collection below <see cref="MajEnv.ChartPath"/>.
        /// </remarks>
        /// <returns>An operation that completes after importing the queued entries and refreshing the chart list.</returns>
        private static async ValueTask Import()
        {
            var nextScene = (MajScenes?)MajScenes.List;
            if (SceneSwitcher.CurrentScene == MajScenes.Init)
            {
                await MajInstances.SceneSwitcher.FadeInAsync();
                nextScene = null;
            }
            else
            {
                while (SceneSwitcher.CurrentScene == MajScenes.Title)
                {
                    nextScene = MajScenes.Login;
                    await UniTask.Yield();
                }
                await MajInstances.SceneSwitcher.SwitchSceneAsync("Empty", false);
                await UniTask.Delay(300);
            }
            FileSystem.Directory.Create(s_importRoot);
            while (s_pendingImportTasks.TryDequeue(out var entry))
            {
                MajDebug.LogDebug("[ChartImporter] Got entry: " + entry.Location);

                try
                {
                    if (entry.IsDirectory)
                    {
                        await ImportDirectoryAsync(entry);
                    }
                    else
                    {
                        await ImportArchiveAsync(entry);
                    }
                }
                catch (Exception e)
                {
                    // A single unreadable package must not discard the remaining queued entries.
                    MajDebug.LogError($"[ChartImporter] Import FAILED: {entry.Location}\n{e}");
                }
            }
            var progress = new Progress<string>();
            progress.ProgressChanged += (o, e) =>
            {
                MajInstances.SceneSwitcher.SetLoadingText(e);
            };
            var task = SongStorage.RefreshLocalAsync(progress);
            while (!task.IsCompleted)
            {
                await UniTask.Yield();
            }
            if (!task.IsCompletedSuccessfully)
            {
                MajInstances.SceneSwitcher.SetLoadingText("MAJTEXT_ERR_SCAN_CHARTS_FAILED".i18n(), Color.red);
            }
            else
            {
                MajInstances.SceneSwitcher.SetLoadingText(string.Empty);
            }
            await UniTask.Delay(3000);
            if (nextScene is MajScenes scene)
            {
                await MajInstances.SceneSwitcher.SwitchSceneAsync(scene.ToString());
            }
        }

        /// <summary>Extracts one queued archive into a private temporary directory and imports the chart layout it contains.</summary>
        /// <param name="entry">The queued <c>.zip</c> or <c>.adx</c> archive entry.</param>
        /// <returns>An operation that completes after the archive contents have been imported.</returns>
        /// <remarks>The temporary extraction directory is always removed, and an Android queue entry is additionally consumed.</remarks>
        private static async ValueTask ImportArchiveAsync(FileSystemEntry entry)
        {
            var extension = Path.GetExtension(entry.Name).ToLowerInvariant();
            if (extension != ".zip" && extension != ".adx")
            {
                MajDebug.LogError("[ChartImporter] Unsupported extension: " + extension);
                return;
            }
            if (FileSystem.GetEntry(entry.Location) is not { IsDirectory: false })
            {
                MajDebug.LogError("[ChartImporter] File not found: " + entry.Location);
                return;
            }
            var tempOutDir = Path.Combine(MajEnv.TempPath, Guid.NewGuid().ToString());

            try
            {
                var outputDirectory = FileSystem.Directory.Create(tempOutDir);
                await ExtractArchiveAsync(entry, outputDirectory.Location);
                // An archive may hold a single chart, or a collection directory that groups several charts.
                await ImportContainerAsync(outputDirectory, Path.GetFileNameWithoutExtension(entry.Name));
            }
            finally
            {
                TryDeleteDirectory(tempOutDir);
#if UNITY_ANDROID
                TryDeleteFile(entry.Location);
#endif
            }
        }

        /// <summary>Imports one queued directory using the same layout rules as an extracted archive.</summary>
        /// <param name="entry">The queued directory entry.</param>
        /// <returns>An operation that completes after the directory contents have been imported.</returns>
        private static async ValueTask ImportDirectoryAsync(FileSystemEntry entry)
        {
            var directory = FileSystem.Directory.Open(entry.Location);
            if (IsChartDirectory(directory))
            {
                // The queued directory is the chart itself instead of a package that contains charts.
                await ImportChartAsync(directory, s_importRoot, entry.Name);
                return;
            }
            await ImportContainerAsync(directory, entry.Name);
        }

        /// <summary>Imports every chart found in an extracted archive root or in a queued directory.</summary>
        /// <param name="container">The directory holding the import layout, which may belong to any storage backend.</param>
        /// <param name="containerName">The chart directory name used when the container itself is a chart.</param>
        /// <returns>An operation that completes after every detected chart has been copied.</returns>
        /// <remarks>
        /// A container chart and an immediate chart child are imported into the <c>Import</c> collection, while charts grouped
        /// one level deeper are imported into the collection named after their parent.
        /// </remarks>
        private static async ValueTask ImportContainerAsync(StorageDirectory container, string containerName)
        {
            if (IsChartDirectory(container))
            {
                await ImportChartAsync(container, s_importRoot, containerName);
                return;
            }
            foreach (var childEntry in container.EnumerateEntries())
            {
                if (!childEntry.IsDirectory || IsExcluded(childEntry))
                {
                    continue;
                }
                var childDirectory = new StorageDirectory(container.FileSystem, childEntry.Location);
                if (IsChartDirectory(childDirectory))
                {
                    await ImportChartAsync(childDirectory, s_importRoot, childEntry.Name);
                    continue;
                }
                var charts = GetChartDirectories(childDirectory);
                if (charts.Count == 0)
                {
                    MajDebug.LogDebug("[ChartImporter] No chart found in " + childEntry.Location);
                    continue;
                }
                // A named collection is reused when it exists, so a package extends it instead of duplicating it.
                var collectionDirectory = FileSystem.Directory.Create(Path.Combine(MajEnv.ChartPath, childEntry.Name));
                foreach (var (chartDirectory, chartName) in charts)
                {
                    await ImportChartAsync(chartDirectory, collectionDirectory.Location, chartName);
                }
            }
        }

        /// <summary>Copies one chart directory into a collection directory under a name that is still available.</summary>
        /// <param name="source">The chart directory to copy.</param>
        /// <param name="destinationRoot">The local collection directory receiving the chart.</param>
        /// <param name="name">The requested chart directory name.</param>
        /// <returns>An operation that completes after the chart has been copied.</returns>
        private static async ValueTask ImportChartAsync(StorageDirectory source, string destinationRoot, string name)
        {
            var destinationPath = GetUniqueDirectoryPath(destinationRoot, name);
            MajInstances.SceneSwitcher.SetLoadingText($"Importing...\n{name}");
            CopyDirectory(source, FileSystem.Directory.Create(destinationPath));
            await UniTask.Yield();
        }

        /// <summary>Collects the immediate chart children of a collection directory.</summary>
        /// <param name="collection">The directory that may group several charts.</param>
        /// <returns>The chart directories paired with their authoritative names, or an empty list when none is a chart.</returns>
        private static List<(StorageDirectory Directory, string Name)> GetChartDirectories(StorageDirectory collection)
        {
            var charts = new List<(StorageDirectory Directory, string Name)>();
            foreach (var entry in collection.EnumerateEntries())
            {
                if (!entry.IsDirectory || IsExcluded(entry))
                {
                    continue;
                }
                var directory = new StorageDirectory(collection.FileSystem, entry.Location);
                if (IsChartDirectory(directory))
                {
                    charts.Add((directory, entry.Name));
                }
            }
            return charts;
        }

        /// <summary>Determines whether a directory directly contains a playable chart.</summary>
        /// <param name="directory">The directory to inspect.</param>
        /// <returns>True when the directory holds both the chart definition file and a supported track file.</returns>
        /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Enumeration was denied.</exception>
        /// <exception cref="IOException">Enumeration failed or the location is not a directory.</exception>
        private static bool IsChartDirectory(StorageDirectory directory)
        {
            var hasChartFile = false;
            var hasTrackFile = false;
            foreach (var entry in directory.EnumerateEntries())
            {
                if (entry.IsDirectory)
                {
                    continue;
                }
                var fileName = entry.Name.ToLowerInvariant();
                if (string.Equals(fileName, ChartFileName, StringComparison.Ordinal))
                {
                    hasChartFile = true;
                }
                else if (Array.IndexOf(s_trackFileNames, fileName) >= 0)
                {
                    hasTrackFile = true;
                }
                if (hasChartFile && hasTrackFile)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Determines whether an entry is ignored while the import layout is inspected.</summary>
        /// <param name="entry">The entry to inspect.</param>
        /// <returns>True for hidden, system and dot-prefixed entries.</returns>
        private static bool IsExcluded(FileSystemEntry entry)
        {
            return entry.IsHidden || entry.IsSystem || entry.Name.StartsWith(".");
        }

        /// <summary>Builds a destination directory path that does not collide with an existing child.</summary>
        /// <param name="root">The existing parent directory.</param>
        /// <param name="name">The requested child directory name.</param>
        /// <returns>The requested path, or a <c>name (i)</c> variant when that name is already occupied.</returns>
        private static string GetUniqueDirectoryPath(string root, string name)
        {
            var path = Path.Combine(root, name);
            for (var i = 1; FileSystem.Directory.Open(path).Exists; i++)
            {
                path = Path.Combine(root, $"{name} ({i})");
            }
            return path;
        }

        /// <summary>Extracts one archive into a temporary directory while rejecting entries that escape it.</summary>
        /// <param name="entry">The archive entry to read.</param>
        /// <param name="destinationDirectory">The local temporary directory receiving the extracted entries.</param>
        /// <returns>An operation that completes after every archive entry has been extracted.</returns>
        /// <exception cref="FileNotFoundException">The archive no longer exists.</exception>
        /// <exception cref="UnauthorizedAccessException">Reading the archive or writing the destination was denied.</exception>
        /// <exception cref="IOException">An archive entry escapes the destination directory, or extraction failed.</exception>
        private static async ValueTask ExtractArchiveAsync(FileSystemEntry entry, string destinationDirectory)
        {
            var destinationRoot = Path.GetFullPath(destinationDirectory);
            if (!destinationRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                destinationRoot += Path.DirectorySeparatorChar;
            }
            using var archiveStream = FileSystem.File.Open(entry.Location).OpenRead();
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
            var archiveEntries = archive.Entries;
            var totalSize = 0L;
            foreach (var archiveEntry in archiveEntries)
            {
                totalSize += archiveEntry.Length;
            }
            var extractedSize = 0L;
            foreach (var archiveEntry in archiveEntries)
            {
                if (string.IsNullOrEmpty(archiveEntry.Name))
                {
                    continue;
                }
                var fullPath = Path.GetFullPath(Path.Combine(destinationDirectory, archiveEntry.FullName));
                if (!fullPath.StartsWith(destinationRoot, StringComparison.Ordinal))
                {
                    throw new IOException("Zip entry escapes destination: " + archiveEntry.FullName);
                }
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    FileSystem.Directory.Create(directory);
                }
                using (var entryStream = archiveEntry.Open())
                using (var outputStream = FileSystem.File.Create(fullPath, overwrite: true).OpenWrite())
                {
                    entryStream.CopyTo(outputStream);
                }
                FileSystem.File.Open(fullPath).SetLastWriteTime(archiveEntry.LastWriteTime.DateTime);
                extractedSize += archiveEntry.Length;
                var percentage = totalSize == 0 ? 100d : extractedSize * 100 / (double)totalSize;
                MajInstances.SceneSwitcher.SetLoadingText($"Extracting...\n{percentage:F2}%");
                await UniTask.Yield();
            }
        }

        /// <summary>Copies a directory tree with each copied file's modification time.</summary>
        /// <param name="sourceDirectory">The existing source directory, which may belong to any storage backend.</param>
        /// <param name="destinationDirectory">The local destination directory, which may be empty.</param>
        /// <exception cref="DirectoryNotFoundException">A source directory disappeared during the copy.</exception>
        /// <exception cref="UnauthorizedAccessException">Reading or writing was denied.</exception>
        /// <exception cref="IOException">Creating or copying an entry failed.</exception>
        private static void CopyDirectory(StorageDirectory sourceDirectory, StorageDirectory destinationDirectory)
        {
            var directories = new Stack<(StorageDirectory Source, StorageDirectory Destination)>();
            directories.Push((sourceDirectory, destinationDirectory));

            while (directories.Count > 0)
            {
                var (source, destination) = directories.Pop();

                foreach (var entry in source.EnumerateEntries())
                {
                    if (entry.IsDirectory)
                    {
                        directories.Push((
                            new StorageDirectory(source.FileSystem, entry.Location),
                            destination.CreateDirectory(entry.Name)));
                        continue;
                    }
                    var destinationPath = Path.Combine(destination.Location, entry.Name);
                    FileSystem.File.Copy(entry.Location, destinationPath, overwrite: true);
                    if (entry.LastWriteTimeUtc is DateTime lastWriteTimeUtc)
                    {
                        // A provider copy does not carry the source timestamp on its own.
                        FileSystem.File.Open(destinationPath).SetLastWriteTime(lastWriteTimeUtc);
                    }
                }
            }
        }

        /// <summary>Deletes a temporary directory without masking an import failure.</summary>
        /// <param name="path">The local directory to remove.</param>
        private static void TryDeleteDirectory(string path)
        {
            try
            {
                var directory = FileSystem.Directory.Open(path);
                if (directory.Exists)
                {
                    directory.Delete(recursive: true);
                }
            }
            catch (Exception e)
            {
                MajDebug.LogWarning("[ChartImporter] Failed to clean up " + path + ": " + e.Message);
            }
        }

        /// <summary>Deletes an imported temporary file without masking an import failure.</summary>
        /// <param name="location">The local path or content URI of the consumed file.</param>
        private static void TryDeleteFile(string location)
        {
            try
            {
                FileSystem.File.Open(location).Delete();
            }
            catch (Exception e)
            {
                MajDebug.LogWarning("[ChartImporter] Failed to delete " + location + ": " + e.Message);
            }
        }

#if ANDROID_RUNTIME
        private static void OnNewIntent(object? sender, Intent? intent)
        {
            if (intent is null)
            {
                return;
            }
            var action = intent.GetAction();
            if (action == Intent.ActionSendMultiple)
            {
                var uris = intent.GetParcelableArrayListExtra(Intent.ExtraStream, MajdataPlay.Platform.Android.Runtime.Net.Uri.Class)!;
                for (var i = 0; i < uris.Size(); i++)
                {
                    var uri = ((JavaObject?)(object?)uris.Get(i)) as MajdataPlay.Platform.Android.Runtime.Net.Uri;
                    if (uri == null)
                    {
                        continue;
                    }
                    var entry = FileSystem.GetEntry(uri.ToString()!);
                    if (entry is null)
                    {
                        MajDebug.LogError(nameof(ChartImporter), $"GetEntry failed for {uri}");
                        continue;
                    }
                    s_pendingImportTasks.Enqueue(entry);
                }
            }
            else if (action == Intent.ActionSend)
            {
                var uri = intent.GetDataString();
                var entry = FileSystem.GetEntry(uri!);
                if (entry is null)
                {
                    MajDebug.LogError(nameof(ChartImporter), $"GetEntry failed for {uri}");
                    return;
                }

                s_pendingImportTasks.Enqueue(entry);
            }
        }
#endif
    }
}
