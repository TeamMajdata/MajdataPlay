#nullable enable
using MajdataPlay.Collections;
using MajdataPlay.IO.Storage;
using MajdataPlay.Net;
using MajdataPlay.Utils;
using MajSimai;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Storage = MajdataPlay.IO.Storage.FileSystem;

namespace MajdataPlay.Tests
{
    /// <summary>Validates the staged production song code in a real, disposable Unity Editor project.</summary>
    public static partial class SongStorageUnityValidation
    {
        /// <summary>Defines the number of visible fixture collections, including one empty directory.</summary>
        private const int CollectionCount = 41;
        /// <summary>Defines how many valid charts each nonempty fixture collection contains.</summary>
        private const int ChartsPerCollection = 3;
        /// <summary>Stores the asynchronous validation task observed by Editor.update.</summary>
        private static Task? s_validationTask;
        /// <summary>Stores the real Unity synchronization context for explicitly scheduled main-thread assertions.</summary>
        private static SynchronizationContext? s_editorContext;
        /// <summary>Stores the wall-clock deadline for the isolated harness.</summary>
        private static double s_deadline;
        /// <summary>Tracks fixture chart instances created outside the storage-owned library.</summary>
        private static readonly List<ISongDetail> s_fixtureCharts = new List<ISongDetail>();

        /// <summary>Starts compilation-only or asynchronous metadata validation without blocking the Editor loop.</summary>
        public static void Run()
        {
            if (Application.unityVersion != "6000.3.17f1")
            {
                Debug.LogError("SONG_STORAGE_UNITY_FAILED: incorrect Unity version: " + Application.unityVersion);
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log("SONG_STORAGE_COMPILE_PASSED: real SongStorage, four collection/scanner sources, SongDetail, OnlineSongDetail, online DTO/helpers, ISongDetail and IO Storage.");
            var arguments = Environment.GetCommandLineArgs();
            if (arguments.Contains("-songStorageValidationCompileOnly"))
            {
                Debug.Log("SONG_STORAGE_UNITY_COMPILE_PASSED");
                EditorApplication.Exit(0);
                return;
            }
            var repetitions = 12;
            var repetitionIndex = Array.IndexOf(arguments, "-songStorageValidationRepetitions");
            if (repetitionIndex >= 0 && repetitionIndex + 1 < arguments.Length)
            {
                repetitions = int.Parse(arguments[repetitionIndex + 1]);
            }
            s_editorContext = SynchronizationContext.Current;
            if (s_editorContext is null)
            {
                Debug.LogError("SONG_STORAGE_UNITY_FAILED: Unity synchronization context is unavailable.");
                EditorApplication.Exit(1);
                return;
            }
            s_deadline = EditorApplication.timeSinceStartup + 240;
            var projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            s_validationTask = Task.Run(() => ValidateAsync(projectPath, repetitions));
            EditorApplication.update += Poll;
        }

        /// <summary>Observes completion without waiting for work that needs Unity's main thread.</summary>
        private static void Poll()
        {
            var task = s_validationTask;
            if (task is null || !task.IsCompleted)
            {
                if (EditorApplication.timeSinceStartup > s_deadline)
                {
                    EditorApplication.update -= Poll;
                    Debug.LogError("SONG_STORAGE_UNITY_FAILED: metadata validation exceeded four minutes.");
                    EditorApplication.Exit(1);
                }
                return;
            }
            EditorApplication.update -= Poll;
            try
            {
                // GetResult is safe only here, after IsCompleted; never block Unity while tasks run.
                task.GetAwaiter().GetResult();
                Debug.Log("SONG_STORAGE_UNITY_PASSED");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("SONG_STORAGE_UNITY_FAILED");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Runs real local/online cache parsing, resource lookup, collection refresh and repeated whole-library scans.</summary>
        /// <param name="project">The isolated project path captured on Unity's main thread.</param>
        /// <param name="repetitions">The number of local-only library replacements to validate.</param>
        /// <returns>A task completing after all assertions and real chart cleanup.</returns>
        /// <exception cref="InvalidOperationException">A validation assertion fails.</exception>
        private static async Task ValidateAsync(string project, int repetitions)
        {
            var fixtures = Path.Combine(project, "Fixtures", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixtures);
            try
            {
                ValidateConstructorContracts();
                await ValidateMetadataAsync(fixtures).ConfigureAwait(false);
                await ValidateResourceTablesAsync().ConfigureAwait(false);
                await ValidateOnlineCacheAsync(fixtures).ConfigureAwait(false);
                await ValidateCollectionRefreshAsync(fixtures).ConfigureAwait(false);
                await ValidateStorageAsync(fixtures, repetitions).ConfigureAwait(false);
                await ValidateIndependentSourcesAsync(fixtures).ConfigureAwait(false);
                Require(ValidationHttpHandler.RequestCount == 0, "The offline harness attempted an HTTP request.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
            finally
            {
                // These are real local/online DisposeAsync calls, including UniTask main-thread switches.
                foreach (var chart in s_fixtureCharts.OfType<SongDetail>())
                {
                    await chart.DisposeAsync().ConfigureAwait(false);
                }
                foreach (var chart in s_fixtureCharts.OfType<OnlineSongDetail>())
                {
                    await chart.DisposeAsync().ConfigureAwait(false);
                }
                s_fixtureCharts.Clear();
            }
        }

        /// <summary>Verifies true MajSimai parsing, hashing and both cancellable entry points.</summary>
        /// <param name="fixtures">The ignored fixture directory.</param>
        /// <returns>A task completing after metadata and cancellation assertions.</returns>
        /// <exception cref="InvalidOperationException">A metadata assertion fails.</exception>
        private static async Task ValidateMetadataAsync(string fixtures)
        {
            var chartPath = WriteChart(Path.Combine(fixtures, "Metadata"), 7);
            var directory = Storage.Directory.Open(chartPath);
            var song = await SongDetail.ParseAsync(directory, CancellationToken.None).ConfigureAwait(false);
            s_fixtureCharts.Add(song);
            Require(song.Title == "Fixture-0007", "The real MajSimai title was not parsed.");
            Require(song.Artist == "Fixture Artist", "The real MajSimai artist was not parsed.");
            Require(song.Levels[4] == "12+", "The real MajSimai difficulty was not parsed.");
            Require(song.Designers[4] == "Fixture Designer", "The real MajSimai designer was not parsed.");
            using (var md5 = MD5.Create())
            {
                var bytes = File.ReadAllBytes(Path.Combine(chartPath, "maidata.txt"));
                Require(song.Hash == Convert.ToBase64String(md5.ComputeHash(bytes)), "Metadata hash differs from the fixture MD5.");
            }
            var parsed = await song.GetMaidataAsync(token: CancellationToken.None).ConfigureAwait(false);
            Require(parsed.Title == song.Title && song.IsMaidataLoaded, "Subsequent real Simai chart parsing failed.");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await ExpectCancellationAsync(() => SongDetail.ParseAsync(directory, cancellation.Token)).ConfigureAwait(false);
            await ExpectCancellationAsync(() => SongDetail.ParseAsync(chartPath, cancellation.Token)).ConfigureAwait(false);
            Debug.Log("SONG_STORAGE_METADATA_PASSED: real MajSimai metadata/hash/chart parse and StorageDirectory/string pre-cancellation.");
        }

        /// <summary>Checks refresh replacement, cursor/filter retention, cancellation and derived no-op behavior.</summary>
        /// <param name="fixtures">The ignored fixture directory.</param>
        /// <returns>A task completing after synchronous and asynchronous refresh assertions.</returns>
        /// <exception cref="InvalidOperationException">A collection assertion fails.</exception>
        private static async Task ValidateCollectionRefreshAsync(string fixtures)
        {
            var path = Path.Combine(fixtures, "Collection");
            WriteChart(path, 1);
            WriteChart(path, 2);
            var directory = Storage.Directory.Open(path);
            using var scanner = new SongCollectionScanner();
            var songs = await scanner.ScanAsync(directory).ConfigureAwait(false);
            s_fixtureCharts.AddRange(songs);
            Require(songs.Length == 2 && scanner.LoadedChartCount == 2, "Standalone scanner dropped a fixture chart.");
            var collection = new SongCollection(directory, "Collection", songs);
            var id = collection.Id;
            var selectedHash = songs.Single(song => song.Title == "Fixture-0002").Hash;
            collection.SetCursor(selectedHash);
            WriteChart(path, 3);
            await collection.RefreshAsync().ConfigureAwait(false);
            s_fixtureCharts.AddRange(collection.GetOriginSnapshot());
            Require(collection.Count == 3 && collection.Id == id, "RefreshAsync changed identity or lost a chart.");
            Require(ReferenceEquals(collection.Directory, directory), "RefreshAsync replaced the authoritative directory handle.");
            Require(collection.Current.Hash == selectedHash, "RefreshAsync failed to preserve a surviving selected chart.");
            collection.SortAndFilter(new SongOrder { Keyword = "Fixture-0002", SortBy = SortType.ByTitle });
            WriteChart(path, 4);
            await collection.RefreshAsync().ConfigureAwait(false);
            s_fixtureCharts.AddRange(collection.GetOriginSnapshot());
            Require(collection.Count == 1 && collection.GetOriginSnapshot().Length == 4, "RefreshAsync failed to retain the filter independently of origin.");
            var beforeCancel = collection.ToArray();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await ExpectCancellationAsync(() => collection.RefreshAsync(cancellation.Token)).ConfigureAwait(false);
            Require(ReferenceEquals(beforeCancel[0], collection.Current), "Canceled refresh replaced the previous snapshot.");
            collection.Reset();
            Require(collection.Count == 4, "Reset failed to restore all refreshed charts.");
            File.Move(Path.Combine(path, "Chart-0002", "maidata.txt"), Path.Combine(path, "Chart-0002", "disabled.txt"));
            await RunOnEditorAsync(() => collection.Refresh()).ConfigureAwait(false);
            s_fixtureCharts.AddRange(collection.GetOriginSnapshot());
            Require(collection.Count == 3 && collection.Id == id, "Main-thread synchronous Refresh failed to remove an invalidated chart.");
            Require(collection.Index >= 0 && collection.Index < collection.Count, "Refresh left an invalid cursor.");

            var virtualCollection = new SongCollection("Virtual", collection.ToArray());
            var onlineCollection = new OnlineSongCollection(new ApiEndpoint(), directory, "Online", collection.ToArray());
            var favorites = new MyFavoriteSongCollection();
            favorites.Add(collection[0]);
            foreach (var derived in new SongCollection[] { virtualCollection, onlineCollection, favorites })
            {
                var before = derived.ToArray();
                derived.Refresh(cancellation.Token);
                await derived.RefreshAsync(cancellation.Token).ConfigureAwait(false);
                Require(derived.Count == before.Length && ReferenceEquals(derived[0], before[0]), "Refresh changed a virtual/online/favorite view.");
            }
            Debug.Log("SONG_STORAGE_COLLECTION_REFRESH_PASSED: real scanner, RefreshAsync, main-thread Refresh, identity/cursor/filter/cancellation and derived no-ops.");
        }

        /// <summary>Verifies concurrent collection scans and repeated complete library publication without dropped folders.</summary>
        /// <param name="fixtures">The ignored fixture directory.</param>
        /// <param name="repetitions">The number of local replacement scans.</param>
        /// <returns>A task completing after library scans and selection assertions.</returns>
        /// <exception cref="InvalidOperationException">A library assertion fails.</exception>
        private static async Task ValidateStorageAsync(string fixtures, int repetitions)
        {
            var rootPath = Path.Combine(fixtures, "Library");
            Directory.CreateDirectory(rootPath);
            var directories = new List<StorageDirectory>();
            for (var collectionIndex = 0; collectionIndex < CollectionCount; collectionIndex++)
            {
                var path = Path.Combine(rootPath, "Group-" + collectionIndex.ToString("D3"));
                Directory.CreateDirectory(path);
                if (collectionIndex != CollectionCount / 2)
                {
                    for (var chartIndex = 0; chartIndex < ChartsPerCollection; chartIndex++)
                    {
                        WriteChart(path, 100 + collectionIndex * ChartsPerCollection + chartIndex);
                    }
                    var missingTrack = WriteChart(path, 10000 + collectionIndex);
                    File.Move(Path.Combine(missingTrack, "track.mp3"), Path.Combine(missingTrack, "not-track.mp3"));
                    Directory.CreateDirectory(Path.Combine(path, "NotAChart"));
                }
                directories.Add(Storage.Directory.Open(path));
            }
            WriteChart(Path.Combine(rootPath, "Group-000", ".MajdataPlay"), 20000);
            File.WriteAllText(Path.Combine(rootPath, "MissingHashes.json"), "{\"Name\":\"Invalid missing hashes\"}", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(rootPath, "MissingName.json"), "{\"SongHashs\":[\"missing\"]}", new UTF8Encoding(false));
            var expectedChartCount = (CollectionCount - 1) * ChartsPerCollection;
            using (var scanner = new SongCollectionScanner())
            {
                var scans = await Task.WhenAll(directories.Select(directory => scanner.ScanAsync(directory))).ConfigureAwait(false);
                var scannedCharts = scans.SelectMany(scan => scan).ToArray();
                s_fixtureCharts.AddRange(scannedCharts);
                Require(scannedCharts.Length == expectedChartCount, "Shared concurrent scanner lost a chart.");
                Require(scanner.DiscoveredChartCount == expectedChartCount && scanner.ProcessedChartCount == expectedChartCount && scanner.LoadedChartCount == expectedChartCount,
                    "Shared scanner counters disagree with valid fixture candidates.");
                var hashes = scannedCharts.Take(2).Select(chart => chart.Hash).ToArray();
                File.WriteAllText(Path.Combine(rootPath, "FixturePlaylist.json"), Serializer.Json.Serialize(new DanInfo
                {
                    Name = "Fixture Playlist",
                    SongHashs = hashes,
                    IsPlayList = true
                }), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(rootPath, "MyFavorites.json"), Serializer.Json.Serialize(new DanInfo
                {
                    Name = "My Favorites",
                    SongHashs = new string[] { hashes[0] },
                    IsPlayList = true
                }), new UTF8Encoding(false));
            }
            Debug.Log("SONG_STORAGE_SHARED_SCANNER_PASSED: 41 concurrent collections, 120 real charts, invalid candidates skipped, exact shared counters.");

            MajEnv.ChartDirectory = FileSystem.Directory.Open(rootPath);
            MajEnv.CacheDirectory = FileSystem.Directory.Create(Path.Combine(fixtures, "Cache"));
            MajEnv.Settings.Online.Enable = false;
            var beforeInitialization = SongStorage.Collections;
            var countBeforeInitialization = SongStorage.TotalChartCount;
            await SongStorage.InitAsync().ConfigureAwait(false);
            Require(ReferenceEquals(beforeInitialization, SongStorage.Collections) && SongStorage.Collections.Length == 0
                && SongStorage.TotalChartCount == countBeforeInitialization,
                "Initialization published collections or charts before an explicit source refresh.");
            await SongStorage.RefreshLocal().ConfigureAwait(false);
            AssertLibrary(expectedChartCount);
            var ids = SongStorage.Collections.Where(collection => !collection.IsVirtual && !collection.IsOnline)
                .ToDictionary(collection => collection.Name, collection => collection.Id);
            var selectedDirectory = Array.FindIndex(SongStorage.Collections, collection => collection.Name == "Group-040");
            MajEnv.RuntimeConfig.List.SelectedDir = selectedDirectory;
            MajEnv.RuntimeConfig.List.SelectedSongIndex = 1;
            var selectedId = SongStorage.Collections[selectedDirectory].Id;
            var selectedHash = SongStorage.Collections[selectedDirectory][1].Hash;
            for (var iteration = 0; iteration < repetitions; iteration++)
            {
                await SongStorage.RefreshLocal().ConfigureAwait(false);
                AssertLibrary(expectedChartCount);
                foreach (var collection in SongStorage.Collections.Where(collection => !collection.IsVirtual && !collection.IsOnline))
                {
                    Require(ids[collection.Name] == collection.Id, "A directory ID changed during repeated scanning.");
                }
                Require(SongStorage.WorkingCollection.Id == selectedId && SongStorage.WorkingCollection.Current.Hash == selectedHash,
                    "Local refresh failed to retain the selected collection and chart.");
            }
            var beforeCancellation = SongStorage.Collections;
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await ExpectCancellationAsync(() => SongStorage.RefreshLocal(cancellationToken: cancellation.Token)).ConfigureAwait(false);
            Require(ReferenceEquals(beforeCancellation, SongStorage.Collections), "Canceled storage refresh replaced the library.");
            await SongStorage.RefreshLocal().ConfigureAwait(false);
            await SongStorage.RefreshOnline().ConfigureAwait(false);
            AssertLibrary(expectedChartCount);
            Debug.Log("SONG_STORAGE_REPEATED_SCAN_PASSED: " + repetitions + " local refreshes plus non-scanning init and explicit source refreshes; no missing/duplicate folder, exact aggregate count, playlist/favorites and selection retained.");
        }

        /// <summary>Checks the complete published library, including the empty middle folder and derived views.</summary>
        /// <param name="expectedChartCount">The number of owned base charts.</param>
        /// <exception cref="InvalidOperationException">The library has missing, duplicate or incorrectly counted entries.</exception>
        private static void AssertLibrary(int expectedChartCount)
        {
            var collections = SongStorage.Collections;
            var baseCollections = collections.Where(collection => !collection.IsVirtual && !collection.IsOnline).ToArray();
            var expectedNames = Enumerable.Range(0, CollectionCount).Select(index => "Group-" + index.ToString("D3")).ToArray();
            Require(baseCollections.Length == CollectionCount, "Storage folder set differs: expected " + CollectionCount + ", actual " + baseCollections.Length + "; names: " + string.Join(", ", baseCollections.Select(collection => collection.Name)));
            Require(baseCollections.Select(collection => collection.Name).SequenceEqual(expectedNames), "Storage names/order differ from every expected fixture directory.");
            Require(baseCollections.Single(collection => collection.Name == "Group-020").Count == 0, "The empty middle directory was omitted or gained invalid charts.");
            Require(baseCollections.Sum(collection => collection.Count) == expectedChartCount && SongStorage.TotalChartCount == expectedChartCount,
                "Storage chart ownership count is incorrect or includes derived duplicates.");
            Require(collections.Single(collection => collection.Name == "All").Count == expectedChartCount, "The aggregate collection lost charts.");
            Require(collections.Single(collection => collection.Type == ChartStorageType.FavoriteList).Count == 1, "Portable favorites were lost.");
            Require(collections.Single(collection => collection.Name == "Fixture Playlist").Count == 2, "The local playlist was lost.");
        }

        /// <summary>Writes a unique Simai chart and a deliberately undecodable placeholder audio file.</summary>
        /// <param name="parent">The disposable collection directory.</param>
        /// <param name="number">The unique fixture chart number.</param>
        /// <returns>The created local chart directory.</returns>
        private static string WriteChart(string parent, int number)
        {
            var path = Path.Combine(parent, "Chart-" + number.ToString("D4"));
            Directory.CreateDirectory(path);
            var content = CreateChartContent(number);
            File.WriteAllText(Path.Combine(path, "maidata.txt"), content, new UTF8Encoding(false));
            File.WriteAllBytes(Path.Combine(path, "track.mp3"), new byte[] { 0 });
            return path;
        }

        /// <summary>Schedules an assertion on the captured real Unity main-thread context.</summary>
        /// <param name="action">The synchronous assertion to run in the Editor.</param>
        /// <returns>A task completing after the main-thread action.</returns>
        private static Task RunOnEditorAsync(Action action)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            s_editorContext!.Post(_ =>
            {
                try
                {
                    action();
                    completion.SetResult(true);
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            }, null);
            return completion.Task;
        }

        /// <summary>Requires a pre-canceled operation to fail rather than silently publishing results.</summary>
        /// <param name="operation">The operation using an already canceled token.</param>
        /// <returns>A task completing after cancellation is observed.</returns>
        /// <exception cref="InvalidOperationException">The operation succeeds instead of observing cancellation.</exception>
        private static async Task ExpectCancellationAsync(Func<Task> operation)
        {
            try
            {
                await operation().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            throw new InvalidOperationException("Expected cancellation was not observed.");
        }

        /// <summary>Reports a precise fixture assertion failure.</summary>
        /// <param name="condition">The invariant that must hold.</param>
        /// <param name="message">The failure description.</param>
        /// <exception cref="InvalidOperationException">The invariant does not hold.</exception>
        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
