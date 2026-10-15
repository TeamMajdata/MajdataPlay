using Cysharp.Text;
using MajdataPlay.Collections;
using MajdataPlay.Diagnostics;
using MajdataPlay.Extensions;
using MajdataPlay.IO.Storage;
using MajdataPlay.Net;
using MajdataPlay.Numerics;
using MajdataPlay.Utils;
using SQLite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay
{
    /// <summary>Owns the published song library and coordinates independent local and online collection refreshes.</summary>
    public static class SongStorage
    {
        /// <summary>Defines the metadata filename identifying a Simai chart directory.</summary>
        public const string ChartFileName = "maidata.txt";
        /// <summary>Lists the track filenames supported by the configured, dot-prefixed audio extensions.</summary>
        public static readonly string[] SupportedTrackFileNames = MajEnv.SupportedAudioFormats
            .Select(extension => "track" + extension)
            .ToArray();

        /// <summary>Lists supported bg, pv and legacy mv video filenames using dot-prefixed video extensions.</summary>
        public static readonly string[] SupportedVideoFileNames = MajEnv.SupportedVideoFormats
            .Select(extension => "bg" + extension)
            .Concat(MajEnv.SupportedVideoFormats.Select(extension => "pv" + extension))
            .Concat(MajEnv.SupportedVideoFormats.Select(extension => "mv" + extension))
            .ToArray();

        /// <summary>Lists the supported PNG and JPEG background-cover filenames.</summary>
        public static readonly string[] SupportedCoverFileNames = new string[]
        {
            "bg.png",
            "bg.jpg"
        };

        /// <summary>Gets whether the published library has no visible songs.</summary>
        public static bool IsEmpty
        {
            get => Collections.IsEmpty() || Collections.All(x => x.Count == 0);
        }
        /// <summary>
        /// Current song collection index
        /// </summary>
        public static int CollectionIndex
        {
            get => s_collectionIndex;
            set => s_collectionIndex = Collections.Length == 0 ? 0 : value.Clamp(0, Collections.Length - 1);
        }
        /// <summary>
        /// Current song collection
        /// </summary>
        public static SongCollection WorkingCollection
        {
            get
            {
                var collections = Collections;
                if (collections.IsEmpty())
                {
                    return s_emptyCollection;
                }
                var index = Volatile.Read(ref s_collectionIndex).Clamp(0, collections.Length - 1);
                return collections[index];
            }
        }
        /// <summary>
        /// Loaded song collections
        /// </summary>
        public static SongCollection[] Collections { get; private set; } = Array.Empty<SongCollection>();
        /// <summary>Gets the runtime song sorting and filtering settings.</summary>
        public static SongOrder OrderBy
        {
            get => MajEnv.RuntimeConfig.List.OrderBy;
        }
        /// <summary>Gets the number of base chart instances in the last successfully published library.</summary>
        public static long TotalChartCount
        {
            get
            {
                return Interlocked.Read(ref s_totalChartCount);
            }
        }

        /// <summary>Stores the selected collection index within the published library.</summary>
        private static int s_collectionIndex = 0;
        /// <summary>Stores the count of chart instances belonging to the committed library.</summary>
        private static long s_totalChartCount = 0;


        /// <summary>Stores the chart instances owned by the last successfully published library.</summary>
        private static ISongDetail[] s_allCharts = Array.Empty<ISongDetail>();

        /// <summary>Serializes initialization, collection refreshes and online favorite refreshes.</summary>
        private static readonly SemaphoreSlim s_refreshLock = new(1, 1);
        /// <summary>Stores local playlist definitions independently of the charts currently available to resolve them.</summary>
        private static LocalPlaylistDefinition[] s_localPlaylistDefinitions = Array.Empty<LocalPlaylistDefinition>();

        /// <summary>Stores the mutable favorites view owned by the committed library.</summary>
        private static MyFavoriteSongCollection s_myFavorites = new();

        /// <summary>Records whether favorites storage and save handling have been initialized.</summary>
        private static bool s_isInitialized = false;
        /// <summary>Stores the shared connection to the persistent favorites database.</summary>
        private static SQLiteAsyncConnection? s_favoritesDatabase;

        /// <summary>Supplies a harmless selection when the library contains no collections.</summary>
        private static readonly SongCollection s_emptyCollection = SongCollection.Empty("default");
        /// <summary>Defines the portable favorites export filename.</summary>
        private const string MyFavoriteFileName = "MyFavorites.json";
        /// <summary>The SQLite favorites database file name inside <see cref="MajEnv.RuntimeCacheDirectory"/>.</summary>
        private const string FavoriteDatabaseFileName = "MajFavorites.db";
        /// <summary>Initializes favorites storage and save handling without loading or refreshing collections.</summary>
        /// <param name="cancellationToken">Cancels waiting or initialization before save handling is registered.</param>
        /// <returns>A task which completes when storage is ready for explicit collection refreshes.</returns>
        /// <exception cref="OperationCanceledException">Initialization was canceled.</exception>
        /// <exception cref="IOException">The root directory or favorite storage could not be accessed.</exception>
        /// <exception cref="SQLiteException">The favorites database could not be initialized.</exception>
        internal static async Task InitAsync(CancellationToken cancellationToken = default)
        {
            await s_refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (s_isInitialized)
                {
                    return;
                }
                var favoriteHashes = await Task.Run(async () =>
                {
                    var rootDirectory = MajEnv.ChartDirectory;
                    if (!rootDirectory.Exists)
                    {
                        FileSystem.Directory.Create(rootDirectory.Location);
                        rootDirectory.CreateDirectory("default");
                    }
                    return await LoadFavoriteHashesAsync().ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                s_myFavorites = new MyFavoriteSongCollection(new List<ISongDetail>(), favoriteHashes);
                s_isInitialized = true;
                GameManager.OnSave += OnSave;
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
                throw;
            }
            finally
            {
                s_refreshLock.Release();
            }
        }

        /// <summary>Refreshes local collections and playlists while retaining all published online base collections.</summary>
        /// <param name="progressReporter">Receives local loading and cleanup status messages.</param>
        /// <param name="cancellationToken">Cancels waiting or loading before publication; committed cleanup is not canceled.</param>
        /// <returns>A task which completes after publication and obsolete local-chart cleanup.</returns>
        /// <exception cref="OperationCanceledException">The refresh was canceled before publication.</exception>
        /// <exception cref="IOException">The root directory could not be accessed.</exception>
        internal static Task RefreshLocal(IProgress<string>? progressReporter = null, CancellationToken cancellationToken = default)
        {
            return RefreshCollectionsAsync(false, progressReporter, cancellationToken);
        }

        /// <summary>Refreshes online collections without reloading local charts or playlist files.</summary>
        /// <param name="progressReporter">Receives online loading and cleanup status messages.</param>
        /// <param name="cancellationToken">Cancels waiting or loading before publication; committed cleanup is not canceled.</param>
        /// <returns>A task which completes after publication and obsolete online-chart cleanup.</returns>
        /// <exception cref="OperationCanceledException">The refresh was canceled before publication.</exception>
        /// <exception cref="IOException">An endpoint cache directory could not be accessed.</exception>
        internal static Task RefreshOnline(IProgress<string>? progressReporter = null, CancellationToken cancellationToken = default)
        {
            return RefreshCollectionsAsync(true, progressReporter, cancellationToken);
        }

        /// <summary>Replaces one collection source and rebuilds shared views under the storage refresh lock.</summary>
        /// <param name="refreshOnline">Whether to replace online collections rather than local collections.</param>
        /// <param name="progressReporter">Receives loading and cleanup status messages.</param>
        /// <param name="cancellationToken">Cancels work before the replacement library is committed.</param>
        /// <returns>A task which completes after publication and obsolete-chart cleanup.</returns>
        /// <exception cref="OperationCanceledException">Waiting or loading was canceled.</exception>
        /// <exception cref="IOException">The selected source's storage could not be accessed.</exception>
        private static async Task RefreshCollectionsAsync(bool refreshOnline, IProgress<string>? progressReporter, CancellationToken cancellationToken)
        {
            await s_refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            StorageSnapshot? snapshot = null;
            var isPublished = false;
            try
            {
                if (!s_isInitialized)
                {
                    return;
                }
                var listConfig = MajEnv.RuntimeConfig.List;
                var selectedIndex = listConfig.SelectedSongIndex;
                var selectedDir = listConfig.SelectedDir;
                var previousCollection = Collections.Length == 0
                    ? s_emptyCollection
                    : Collections[selectedDir.Clamp(0, Collections.Length - 1)];
                var selectedId = Collections.Length == 0 ? listConfig.SelectedDirGuid : previousCollection.Id;
                var selectedHash = Collections.Length == 0
                    ? listConfig.SelectedSongHash
                    : previousCollection.Count == 0
                        ? null
                        : previousCollection[selectedIndex.Clamp(0, previousCollection.Count - 1)].Hash;
                var oldCharts = s_allCharts;
                snapshot = await Task.Run(() => refreshOnline
                    ? CreateOnlineSnapshotAsync(progressReporter, cancellationToken)
                    : CreateLocalSnapshotAsync(progressReporter, cancellationToken), cancellationToken).ConfigureAwait(false);
                progressReporter?.Report("MAJTEXT_CLEANING_UP".i18n());
                cancellationToken.ThrowIfCancellationRequested();
                PublishSnapshot(snapshot);
                isPublished = true;
                RestoreSelection(selectedId, selectedHash, selectedDir, selectedIndex);
                await DisposeReplacedChartsAsync(oldCharts, snapshot.AllCharts).ConfigureAwait(false);
                MajDebug.LogInfo($"Loaded chart count: {TotalChartCount}");
                if (refreshOnline)
                {
                    Online.ClearResponseCache();
                }
                GC.Collect();
                await Task.Delay(1000);
            }
            catch (Exception e)
            {
                if (!isPublished && snapshot is not null)
                {
                    var retained = new HashSet<ISongDetail>(s_allCharts, SongReferenceComparer.Instance);
                    DisposeUnpublishedCharts(snapshot.AllCharts.Where(chart => !retained.Contains(chart)));
                }
                MajDebug.LogException(e);
                throw;
            }
            finally
            {
                s_refreshLock.Release();
            }
        }

        /// <summary>Loads local base collections and playlist definitions without fetching or replacing online charts.</summary>
        /// <param name="progressReporter">Receives local chart loading status messages.</param>
        /// <param name="cancellationToken">Cancels loading or rebuilding shared views.</param>
        /// <returns>An unpublished replacement retaining the online source's chart instances.</returns>
        /// <exception cref="OperationCanceledException">Loading was canceled.</exception>
        /// <exception cref="IOException">The chart root could not be accessed.</exception>
        private static async Task<StorageSnapshot> CreateLocalSnapshotAsync(IProgress<string>? progressReporter, CancellationToken cancellationToken)
        {
            var rootDirectory = MajEnv.ChartDirectory;
            var collections = await GetLocalCollections(rootDirectory, progressReporter, cancellationToken).ConfigureAwait(false);
            var localCharts = collections.SelectMany(collection => collection.GetOriginSnapshot()).ToArray();
            try
            {
                var localPlaylists = await LoadLocalPlaylistDefinitionsAsync(rootDirectory, cancellationToken).ConfigureAwait(false);
                collections.AddRange(Collections.Where(collection => collection.IsOnline && collection.Type == ChartStorageType.List));
                var onlinePlaylists = Collections.OfType<OnlineSongCollection>()
                    .Where(collection => collection.Type is ChartStorageType.PlayList or ChartStorageType.Dan)
                    .ToArray();
                var favoriteHashes = new HashSet<string>(s_myFavorites.ExportHashSet());
                var snapshot = await BuildSnapshotAsync(collections, localPlaylists, favoriteHashes, cancellationToken).ConfigureAwait(false);
                return await RebindOnlinePlaylistsAsync(snapshot, onlinePlaylists, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                DisposeUnpublishedCharts(localCharts);
                throw;
            }
        }

        /// <summary>Loads online base collections while reusing local charts and previously loaded local playlist definitions.</summary>
        /// <param name="progressReporter">Receives online chart loading status messages.</param>
        /// <param name="cancellationToken">Cancels loading or rebuilding shared views.</param>
        /// <returns>An unpublished replacement retaining the local source's chart instances.</returns>
        /// <exception cref="OperationCanceledException">Loading was canceled.</exception>
        /// <exception cref="IOException">An endpoint cache directory could not be accessed.</exception>
        private static async Task<StorageSnapshot> CreateOnlineSnapshotAsync(IProgress<string>? progressReporter, CancellationToken cancellationToken)
        {
            var onlineCollections = await GetOnlineCollections(progressReporter, cancellationToken).ConfigureAwait(false);
            try
            {
                var collections = Collections.Where(collection => !collection.IsOnline && !collection.IsVirtual
                        && collection.Type == ChartStorageType.List)
                    .ToList();
                collections.AddRange(onlineCollections);
                var onlinePlaylists = MajEnv.Settings.Online.Enable
                    ? Collections.OfType<OnlineSongCollection>()
                        .Where(collection => collection.Type is ChartStorageType.PlayList or ChartStorageType.Dan)
                        .ToArray()
                    : Array.Empty<OnlineSongCollection>();
                var favoriteHashes = new HashSet<string>(s_myFavorites.ExportHashSet());
                var snapshot = await BuildSnapshotAsync(collections, s_localPlaylistDefinitions, favoriteHashes, cancellationToken).ConfigureAwait(false);
                return await RebindOnlinePlaylistsAsync(snapshot, onlinePlaylists, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                DisposeUnpublishedCharts(onlineCollections.SelectMany(collection => collection.GetOriginSnapshot()));
                throw;
            }
        }

        /// <summary>Publishes the chart, favorite, collection and count snapshots after the selected source and all shared views are ready.</summary>
        /// <param name="snapshot">The completed replacement library.</param>
        private static void PublishSnapshot(StorageSnapshot snapshot)
        {
            s_allCharts = snapshot.AllCharts;
            s_myFavorites = snapshot.Favorites;
            s_localPlaylistDefinitions = snapshot.LocalPlaylists;
            Interlocked.Exchange(ref s_totalChartCount, snapshot.AllCharts.LongLength);
            Collections = snapshot.Collections;
            s_collectionIndex = Collections.Length == 0 ? 0 : s_collectionIndex.Clamp(0, Collections.Length - 1);
        }

        /// <summary>Restores a selection by stable collection ID and chart hash, falling back to clamped indices.</summary>
        /// <param name="collectionId">The identity of the previously selected collection.</param>
        /// <param name="songHash">The identity of the previously selected chart, if any.</param>
        /// <param name="directoryIndex">The fallback collection index.</param>
        /// <param name="songIndex">The fallback chart index.</param>
        private static void RestoreSelection(Guid collectionId, string? songHash, int directoryIndex, int songIndex)
        {
            var index = Array.FindIndex(Collections, collection => collection.Id == collectionId);
            CollectionIndex = index < 0 ? directoryIndex : index;
            var collection = WorkingCollection;
            var listConfig = MajEnv.RuntimeConfig.List;
            listConfig.SelectedDir = CollectionIndex;
            listConfig.SelectedDirGuid = collection.Id;
            if (collection.IsEmpty)
            {
                listConfig.SelectedSongIndex = 0;
                listConfig.SelectedSongHash = string.Empty;
                return;
            }
            collection.Index = songIndex;
            if (songHash is not null && collection.Any(song => song.Hash == songHash))
            {
                collection.SetCursor(songHash);
            }
            listConfig.SelectedSongIndex = collection.Index;
            listConfig.SelectedSongHash = collection.Current.Hash;
        }

        /// <summary>Disposes obsolete chart instances while retaining chart objects reused from the unchanged source.</summary>
        /// <param name="oldCharts">The previous library's chart instances.</param>
        /// <param name="newCharts">The chart instances owned by the committed replacement library.</param>
        /// <returns>A task which completes when every obsolete chart has finished cleanup.</returns>
        private static async Task DisposeReplacedChartsAsync(ISongDetail[] oldCharts, ISongDetail[] newCharts)
        {
            var retained = new HashSet<ISongDetail>(newCharts, SongReferenceComparer.Instance);
            var tasks = new Task[oldCharts.Length];
            for (var i = 0; i < oldCharts.Length; i++)
            {
                tasks[i] = retained.Contains(oldCharts[i]) ? Task.CompletedTask : DisposeChartAsync(oldCharts[i]);
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /// <summary>Observes native chart cleanup failures without invalidating an already committed library.</summary>
        /// <param name="chart">The obsolete chart instance to release.</param>
        /// <returns>A task which completes after cleanup or logging a cleanup failure.</returns>
        private static async Task DisposeChartAsync(ISongDetail chart)
        {
            try
            {
                switch (chart)
                {
                    case OnlineSongDetail online:
                        await online.DisposeAsync().ConfigureAwait(false);
                        break;
                    case SongDetail local:
                        await local.DisposeAsync().ConfigureAwait(false);
                        break;
                }
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
            }
        }

        /// <summary>Rebuilds user online playlists without interleaving a collection refresh or publishing canceled work.</summary>
        /// <param name="progressReporter">Receives favorite-list fetch status messages.</param>
        /// <param name="cancellationToken">Cancels waiting or fetching before the playlist list is published.</param>
        /// <returns>True when every enabled, logged-in endpoint was fetched successfully.</returns>
        /// <exception cref="OperationCanceledException">Waiting or fetching was canceled.</exception>
        internal static async Task<bool> RefreshUserOnlineFavAsync(IProgress<string>? progressReporter = null, CancellationToken cancellationToken = default)
        {
            await s_refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!MajEnv.Settings.Online.Enable)
                {
                    return true;
                }
                var isSuccessful = true;
                var collections = new List<SongCollection>(Collections);
                foreach (var api in MajEnv.ApiEndpoints.OrderBy(endpoint => endpoint.Name))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (api is null || string.IsNullOrEmpty(api.Name) || !api.RuntimeConfig.IsLoggedIn)
                    {
                        continue;
                    }
                    try
                    {
                        MajDebug.LogInfo($"[Online]Fetching fav list from {api.Url.OriginalString}");
                        progressReporter?.Report(ZString.Format("MAJTEXT_SCANNING_FAVORITES_FROM_{0}".i18n(), api.Name));
                        var danInfos = await Online.GetUserOnlineFavCollection(api, cancellationToken).ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (danInfos is null)
                        {
                            isSuccessful = false;
                            continue;
                        }
                        var replacement = new List<SongCollection>(danInfos.Length);
                        foreach (var dan in danInfos)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var collection = await GetOnlineDanCollectionAsync(s_allCharts, dan, api).ConfigureAwait(false);
                            if (collection is not null)
                            {
                                replacement.Add(collection);
                            }
                        }
                        cancellationToken.ThrowIfCancellationRequested();
                        collections.RemoveAll(collection => collection is OnlineSongCollection online
                            && ReferenceEquals(online.Source, api)
                            && online.Type is ChartStorageType.PlayList or ChartStorageType.Dan);
                        collections.AddRange(replacement);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        isSuccessful = false;
                        MajDebug.LogError($"[Online]Fetching fav list from {api.Url.OriginalString} failed because of {e.Message}");
                        MajDebug.LogException(e);
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                Collections = collections.ToArray();
                s_collectionIndex = Collections.Length == 0 ? 0 : s_collectionIndex.Clamp(0, Collections.Length - 1);
                return isSuccessful;
            }
            finally
            {
                s_refreshLock.Release();
            }
        }

        /// <summary>Loads enabled online base collections independently of the local chart root.</summary>
        /// <param name="progressReporter">Receives online loading status messages.</param>
        /// <param name="cancellationToken">Cancels fetching or cache loading.</param>
        /// <returns>The completed online base collections in endpoint-name order.</returns>
        /// <exception cref="OperationCanceledException">Loading was canceled.</exception>
        /// <exception cref="IOException">An endpoint cache directory could not be accessed.</exception>
        private static async Task<List<SongCollection>> GetOnlineCollections(IProgress<string>? progressReporter, CancellationToken cancellationToken)
        {
            var collections = new List<SongCollection>();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!MajEnv.Settings.Online.Enable)
                {
                    MajDebug.LogInfo("[MaiChart Refresh]Online function was disabled, skipping.");
                    return collections;
                }
                foreach (var api in MajEnv.ApiEndpoints.OrderBy(endpoint => endpoint.Name))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (api is null || string.IsNullOrEmpty(api.Name))
                    {
                        continue;
                    }
                    MajDebug.LogInfo($"[MaiChart Refresh]Fetching chart list from {api.Url.OriginalString}");
                    progressReporter?.Report(ZString.Format("MAJTEXT_SCANNING_CHARTS_FROM_{0}".i18n(), api.Name));
                    var collection = await GetOnlineCollection(api, progressReporter, cancellationToken).ConfigureAwait(false);
                    if (!collection.IsEmpty)
                    {
                        collections.Add(collection);
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                return collections;
            }
            catch
            {
                DisposeUnpublishedCharts(collections.SelectMany(collection => collection.GetOriginSnapshot()));
                throw;
            }
        }

        /// <summary>Scans each local collection exactly once using bounded workers and index-owned result slots.</summary>
        /// <param name="rootDirectory">The chart root and its authoritative storage backend.</param>
        /// <param name="progressReporter">Receives chart parsing progress, including failed candidates.</param>
        /// <param name="cancellationToken">Cancels enumeration or parsing.</param>
        /// <returns>Every root collection, including empty directories, ordered by name after the entire scan succeeds.</returns>
        /// <remarks>Collection-level access and identity failures fault the scan rather than publishing a library with missing folders.</remarks>
        /// <exception cref="OperationCanceledException">Scanning was canceled.</exception>
        /// <exception cref="IOException">The chart root could not be enumerated.</exception>
        private static Task<List<SongCollection>> GetLocalCollections(StorageDirectory rootDirectory, IProgress<string>? progressReporter, CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directories = rootDirectory.EnumerateEntries()
                    .Where(entry => entry.IsDirectory)
                    .ToArray();
                var results = new SongCollection?[directories.Length];
                using var scanner = new SongCollectionScanner(cancellationToken);
                var nextIndex = -1;
                var workerCount = Math.Min(Math.Max(1, Environment.ProcessorCount), directories.Length);
                var workers = new Task[workerCount];
                for (var i = 0; i < workers.Length; i++)
                {
                    workers[i] = Task.CompletedTask;
                }
                try
                {
                    try
                    {
                        for (var i = 0; i < workers.Length; i++)
                        {
                            workers[i] = Task.Run(async () =>
                            {
                                while (true)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    var index = Interlocked.Increment(ref nextIndex);
                                    if (index >= directories.Length)
                                    {
                                        return;
                                    }
                                    var entry = directories[index];
                                    try
                                    {
                                        var directory = new StorageDirectory(rootDirectory.FileSystem, entry.Location);
                                        results[index] = await GetCollection(directory, scanner).ConfigureAwait(false);
                                    }
                                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                                    {
                                        throw;
                                    }
                                    catch (Exception e)
                                    {
                                        MajDebug.LogError($"[MaiChart Scanner][{entry.Name}]Failed to scan collection: {e}");
                                        throw;
                                    }
                                }
                            }, cancellationToken);
                        }
                        var allTasks = Task.WhenAll(workers);
                        while (!allTasks.IsCompleted)
                        {
                            var discovered = scanner.DiscoveredChartCount;
                            var processed = scanner.ProcessedChartCount;
                            var percent = discovered == 0 ? 0f : Math.Min(99.99f, processed * 100f / discovered);
                            progressReporter?.Report($"{"MAJTEXT_SCANNING_CHARTS".i18n()}...({percent:F2}%)");
                            await Task.WhenAny(allTasks, Task.Delay(33)).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        // Observe all started workers before cleanup, even if scheduling or progress reporting fails.
                        await Task.WhenAll(workers).ConfigureAwait(false);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    progressReporter?.Report($"{"MAJTEXT_SCANNING_CHARTS".i18n()}...(100.00%)");
                    return results.Where(collection => collection is not null)
                        .Select(collection => collection!)
                        .OrderBy(collection => collection.Name)
                        .ToList();
                }
                catch
                {
                    DisposeUnpublishedCharts(results.Where(collection => collection is not null)
                                                    .SelectMany(collection => collection!.GetOriginSnapshot())
                                                    );
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>Loads local playlist definitions without requiring their requested charts to be available yet.</summary>
        /// <param name="rootDirectory">The chart root containing local playlist JSON files.</param>
        /// <param name="cancellationToken">Cancels playlist loading.</param>
        /// <returns>The successfully loaded definitions in storage enumeration order.</returns>
        /// <exception cref="OperationCanceledException">Playlist loading was canceled.</exception>
        /// <exception cref="IOException">The chart root could not be enumerated.</exception>
        private static async Task<LocalPlaylistDefinition[]> LoadLocalPlaylistDefinitionsAsync(StorageDirectory rootDirectory, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileNameComparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var danFiles = rootDirectory.EnumerateEntries()
                .Where(entry => !entry.IsDirectory && entry.Name.EndsWith(".json", fileNameComparison) && entry.Name != MyFavoriteFileName)
                .ToArray();
            var loadDanTasks = new Task<LocalPlaylistDefinition?>[danFiles.Length];
            for (var i = 0; i < danFiles.Length; i++)
            {
                var file = new StorageFile(rootDirectory.FileSystem, danFiles[i].Location);
                loadDanTasks[i] = LoadDanFileAsync(file, cancellationToken);
            }
            var definitions = await Task.WhenAll(loadDanTasks).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return definitions.Where(definition => definition is not null)
                .Select(definition => definition!)
                .ToArray();
        }

        /// <summary>Rebuilds aggregate, favorite and local playlist views from both sources' completed base collections.</summary>
        /// <param name="collections">The local and online base collections in display order.</param>
        /// <param name="localPlaylists">The loaded local playlist definitions to resolve without further storage access.</param>
        /// <param name="favoriteHashes">The committed favorite identities to retain in the replacement library.</param>
        /// <param name="cancellationToken">Cancels playlist rebinding before publication.</param>
        /// <returns>A complete, unpublished library snapshot with local derived views.</returns>
        /// <exception cref="OperationCanceledException">Playlist rebinding was canceled.</exception>
        private static async Task<StorageSnapshot> BuildSnapshotAsync(List<SongCollection> collections, LocalPlaylistDefinition[] localPlaylists, HashSet<string> favoriteHashes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var allCharts = collections.SelectMany(collection => collection.GetOriginSnapshot()).ToArray();
            collections.Add(new SongCollection("All", allCharts));
            favoriteHashes = new HashSet<string>(favoriteHashes);
            var favoriteOrder = new Dictionary<string, int>(favoriteHashes.Count);
            foreach (var hash in favoriteHashes)
            {
                favoriteOrder.Add(hash, favoriteOrder.Count);
            }
            var favoriteSongs = allCharts.Where(chart => favoriteHashes.Contains(chart.Hash))
                .GroupBy(chart => chart.Hash)
                .Select(group => group.First())
                .OrderBy(chart => favoriteOrder[chart.Hash])
                .ToList();
            var favorites = new MyFavoriteSongCollection(favoriteSongs, favoriteHashes);
            collections.Add(favorites);
            foreach (var playlist in localPlaylists)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var collection = await GetLocalDanCollectionAsync(allCharts, playlist.Id, playlist.Info).ConfigureAwait(false);
                if (collection is null)
                {
                    continue;
                }
                collections.Add(collection);
                MajDebug.LogInfo($"Loaded Dan: {playlist.Info.Name}");
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new StorageSnapshot(collections.ToArray(), allCharts, favorites, localPlaylists);
        }

        /// <summary>Rebinds retained online playlists and courses to the replacement library's current chart instances.</summary>
        /// <param name="snapshot">The replacement base library with local derived collections.</param>
        /// <param name="playlists">The online derived collections to retain without network requests.</param>
        /// <param name="cancellationToken">Cancels rebinding before publication.</param>
        /// <returns>The complete replacement library including rebound online derived collections.</returns>
        /// <exception cref="OperationCanceledException">Rebinding was canceled.</exception>
        private static async Task<StorageSnapshot> RebindOnlinePlaylistsAsync(StorageSnapshot snapshot, OnlineSongCollection[] playlists, CancellationToken cancellationToken)
        {
            var collections = new List<SongCollection>(snapshot.Collections);
            foreach (var playlist in playlists)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = playlist.Definition ?? playlist.DanInfo ?? new DanInfo
                {
                    Name = playlist.Name,
                    IsPlayList = playlist.Type == ChartStorageType.PlayList,
                    SongHashs = playlist.GetOriginSnapshot().Select(chart => chart.Hash).ToArray()
                };
                var rebound = await GetDanCollectionAsync(snapshot.AllCharts, playlist.Id, info, playlist.Source).ConfigureAwait(false);
                if (rebound is null)
                {
                    // Keep the complete definition even when every requested chart is temporarily unavailable.
                    rebound = new OnlineSongCollection(playlist.Source, playlist.Name, Array.Empty<ISongDetail>())
                    {
                        Id = playlist.Id,
                        Type = playlist.Type,
                        DanInfo = playlist.Type == ChartStorageType.Dan ? info : null,
                        Definition = info
                    };
                }
                collections.Add(rebound);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new StorageSnapshot(collections.ToArray(), snapshot.AllCharts, snapshot.Favorites, snapshot.LocalPlaylists);
        }

        /// <summary>Loads one local playlist definition while isolating malformed or unreadable files from neighboring playlists.</summary>
        /// <param name="file">The playlist JSON file and its storage backend.</param>
        /// <param name="cancellationToken">Cancels playlist loading.</param>
        /// <returns>The complete playlist definition, or null when the file is invalid.</returns>
        /// <exception cref="OperationCanceledException">Playlist loading was canceled.</exception>
        private static async Task<LocalPlaylistDefinition?> LoadDanFileAsync(StorageFile file, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Guid id;
                using (var idStream = file.OpenRead())
                {
                    id = GetGuidFromStream(idStream);
                }
                using var jsonStream = file.OpenRead();
                var (success, dan, exception) = await Serializer.Json.TryDeserializeAsync<DanInfo>(jsonStream).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!success || dan is null)
                {
                    MajDebug.LogError($"Failed to load dan from {file.Location}\nException: {exception}");
                    return null;
                }
                if (dan.Name is null || dan.SongHashs is null)
                {
                    MajDebug.LogError($"Failed to load dan from {file.Location}: the name or song hashes are missing.");
                    return null;
                }
                return new LocalPlaylistDefinition(id, dan);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                MajDebug.LogError($"Failed to load dan from {file.Location}\nException: {e}");
                return null;
            }
        }

        /// <summary>Builds one directory-backed collection using the scan's shared parsing budget.</summary>
        /// <param name="directory">The collection directory and its authoritative storage backend.</param>
        /// <param name="scanner">The scanner shared by all collection workers in this scan.</param>
        /// <returns>The scanned collection, including an empty collection when no charts are present.</returns>
        /// <exception cref="DirectoryNotFoundException">The collection directory no longer exists.</exception>
        /// <exception cref="OperationCanceledException">The scanner's cancellation token was canceled.</exception>
        /// <exception cref="IOException">The directory or its identity marker could not be accessed.</exception>
        private static async Task<SongCollection> GetCollection(StorageDirectory directory, SongCollectionScanner scanner)
        {
            var entry = directory.Entry;
            if (entry is null || !entry.IsDirectory)
            {
                throw new DirectoryNotFoundException($"Collection directory '{directory.Location}' does not exist.");
            }
            var charts = await scanner.ScanAsync(directory).ConfigureAwait(false);
            try
            {
                return new SongCollection(directory, entry.Name, charts);
            }
            catch
            {
                DisposeUnpublishedCharts(charts);
                throw;
            }
        }

        /// <summary>Releases never-published chart instances without waiting for Unity's main-thread cleanup.</summary>
        /// <param name="charts">The newly parsed instances which no published view owns.</param>
        private static void DisposeUnpublishedCharts(IEnumerable<ISongDetail> charts)
        {
            foreach (var chart in charts)
            {
                try
                {
                    if (chart is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                catch (Exception e)
                {
                    MajDebug.LogException(e);
                }
            }
        }

        /// <summary>Compares chart ownership by object identity rather than a chart implementation's value equality.</summary>
        private sealed class SongReferenceComparer : IEqualityComparer<ISongDetail>
        {
            /// <summary>Gets the reusable ownership comparer.</summary>
            public static SongReferenceComparer Instance { get; } = new();

            /// <summary>Checks whether two chart references denote the same owned instance.</summary>
            /// <param name="left">The first chart instance.</param>
            /// <param name="right">The second chart instance.</param>
            /// <returns>True only for identical object references.</returns>
            public bool Equals(ISongDetail? left, ISongDetail? right)
            {
                return ReferenceEquals(left, right);
            }

            /// <summary>Gets a hash code which is unaffected by a chart implementation's equality overrides.</summary>
            /// <param name="chart">The chart instance whose ownership identity is hashed.</param>
            /// <returns>The runtime identity hash code of the chart instance.</returns>
            public int GetHashCode(ISongDetail chart)
            {
                return RuntimeHelpers.GetHashCode(chart);
            }
        }

        /// <summary>Retains the complete local playlist definition so either source can resolve newly available charts.</summary>
        private sealed class LocalPlaylistDefinition
        {
            /// <summary>Gets the stable identity derived from the playlist file contents.</summary>
            public Guid Id { get; }

            /// <summary>Gets the requested chart hashes and course rules, including currently unresolved hashes.</summary>
            public DanInfo Info { get; }

            /// <summary>Stores a successfully loaded local playlist without resolving chart ownership yet.</summary>
            /// <param name="id">The identity derived from the playlist file contents.</param>
            /// <param name="info">The complete requested chart hashes and course rules.</param>
            public LocalPlaylistDefinition(Guid id, DanInfo info)
            {
                Id = id;
                Info = info;
            }
        }

        /// <summary>Owns the collections and chart instances of a complete, unpublished library.</summary>
        private sealed class StorageSnapshot
        {
            /// <summary>Gets all base and derived collections to publish together.</summary>
            public SongCollection[] Collections { get; }

            /// <summary>Gets the base chart instances owned by the snapshot.</summary>
            public ISongDetail[] AllCharts { get; }

            /// <summary>Gets the mutable favorites view belonging to the snapshot.</summary>
            public MyFavoriteSongCollection Favorites { get; }

            /// <summary>Gets the complete local playlist definitions retained for subsequent source refreshes.</summary>
            public LocalPlaylistDefinition[] LocalPlaylists { get; }

            /// <summary>Stores the results of a completed refresh without mutating the published library.</summary>
            /// <param name="collections">All collections in display order.</param>
            /// <param name="allCharts">All chart instances owned by the base collections.</param>
            /// <param name="favorites">The favorites view sharing the snapshot's chart instances.</param>
            /// <param name="localPlaylists">The complete local playlist definitions retained for later refreshes.</param>
            public StorageSnapshot(SongCollection[] collections, ISongDetail[] allCharts, MyFavoriteSongCollection favorites, LocalPlaylistDefinition[] localPlaylists)
            {
                Collections = collections;
                AllCharts = allCharts;
                Favorites = favorites;
                LocalPlaylists = localPlaylists;
            }
        }

        /// <summary>Fetches one online collection, falling back to cached local charts after a fetch timeout or unavailable list.</summary>
        /// <param name="api">The online endpoint whose chart list is requested.</param>
        /// <param name="progressReporter">Receives fetch failure status messages.</param>
        /// <param name="cancellationToken">Cancels fetching or cached-chart scanning.</param>
        /// <returns>The online collection, cached charts, or an empty collection when loading fails.</returns>
        /// <exception cref="OperationCanceledException">The caller canceled loading.</exception>
        /// <exception cref="IOException">The endpoint's cache directory could not be created.</exception>
        private static async Task<SongCollection> GetOnlineCollection(ApiEndpoint api, IProgress<string>? progressReporter, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = api.Name;
            var cacheDirectory = MajEnv.CacheDirectory.CreateDirectory("Net").CreateDirectory(name);
            var collection = new OnlineSongCollection(api, cacheDirectory, name, Array.Empty<ISongDetail>());
            if (api.Url is null)
            {
                return collection;
            }
            MajDebug.LogInfo("Loading online charts from:" + api.Url);
            try
            {
                var chartList = await Online.GetChartListAsync(api, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (chartList is null)
                {
                    progressReporter?.Report(ZString.Format("Failed to fetch list from {0}".i18n(), api.Name));
                    return await GetCachedOnlineCollectionAsync(api, cacheDirectory, cancellationToken).ConfigureAwait(false);
                }
                var gameList = new ISongDetail[chartList.Length];
                try
                {
                    for (var i = 0; i < chartList.Length; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var cacheName = BitConverter.ToString(Convert.FromBase64String(chartList[i].Hash));
                        var chartDirectory = cacheDirectory.CreateDirectory(cacheName);
                        gameList[i] = new OnlineSongDetail(chartDirectory, api, chartList[i]);
                    }
                    MajDebug.LogInfo("Loaded online charts list:" + gameList.Length);
                    return new OnlineSongCollection(api, cacheDirectory, name, gameList);
                }
                catch
                {
                    DisposeUnpublishedCharts(gameList.Where(chart => chart is not null));
                    throw;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return await GetCachedOnlineCollectionAsync(api, cacheDirectory, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
                return collection;
            }
        }

        /// <summary>Scans an endpoint's cached charts without losing the online collection identity.</summary>
        /// <param name="api">The endpoint which owns the cache.</param>
        /// <param name="cacheDirectory">The existing cache directory and its storage backend.</param>
        /// <param name="cancellationToken">Cancels cached-chart parsing.</param>
        /// <returns>An online-tagged collection backed by locally cached chart details.</returns>
        /// <exception cref="OperationCanceledException">Cached-chart scanning was canceled.</exception>
        /// <exception cref="IOException">The cache could not be accessed.</exception>
        private static async Task<SongCollection> GetCachedOnlineCollectionAsync(ApiEndpoint api, StorageDirectory cacheDirectory, CancellationToken cancellationToken)
        {
            using var scanner = new SongCollectionScanner(cancellationToken);
            var charts = await scanner.ScanAsync(cacheDirectory).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                MajDebug.LogInfo("Loaded cached online charts list:" + charts.Length);
                return new OnlineSongCollection(api, cacheDirectory, api.Name, charts);
            }
            catch
            {
                DisposeUnpublishedCharts(charts);
                throw;
            }
        }

        //import one dan file
        #region Course and playlist resolution
        /// <summary>Resolves a local course or playlist against the scanned base chart instances.</summary>
        /// <param name="allCharts">The base charts to search.</param>
        /// <param name="id">The identity derived from the playlist file contents.</param>
        /// <param name="danInfo">The requested chart hashes and course rules.</param>
        /// <returns>The resolved collection, or null when no charts match.</returns>
        private static Task<SongCollection?> GetLocalDanCollectionAsync(IEnumerable<ISongDetail> allCharts, Guid id, DanInfo danInfo)
        {
            return GetDanCollectionAsync(allCharts, id, danInfo, null);
        }
        /// <summary>Converts and resolves an online course or playlist against the scanned chart instances.</summary>
        /// <param name="allCharts">The base charts to search.</param>
        /// <param name="onlineDanInfo">The endpoint-provided playlist metadata.</param>
        /// <param name="source">The endpoint which owns the playlist.</param>
        /// <returns>The resolved online collection, or null when no charts match.</returns>
        private static Task<SongCollection?> GetOnlineDanCollectionAsync(IEnumerable<ISongDetail> allCharts, MajNetOnlineDanInfo onlineDanInfo, ApiEndpoint source)
        {
            var danInfo = new DanInfo()
            {
                Name = onlineDanInfo.Name,
                Description = onlineDanInfo.Description,
                SongHashs = onlineDanInfo.SongHashs.ToArray(),
                IsPlayList = onlineDanInfo.IsPlayList,
                IsForceGameover = onlineDanInfo.IsForceGameover
            };
            var id = ObjectIdHelper.ToGuid(onlineDanInfo.Id);
            return GetDanCollectionAsync(allCharts, id, danInfo, source);
        }
        /// <summary>Resolves playlist hashes in their requested order, preferring online charts with matching identities.</summary>
        /// <param name="allCharts">The base charts to search.</param>
        /// <param name="id">The stable collection identity.</param>
        /// <param name="danInfo">The requested chart hashes and course rules.</param>
        /// <param name="source">The owning endpoint, or null for a local playlist.</param>
        /// <returns>The resolved collection, or null when no charts match.</returns>
        private static async Task<SongCollection?> GetDanCollectionAsync(IEnumerable<ISongDetail> allCharts, Guid id, DanInfo danInfo, ApiEndpoint? source)
        {
            return await Task.Run(() =>
            {
                var songHashs = danInfo.SongHashs.ToList();
                var targetCharts = allCharts.Where(x => songHashs.Any(y => y == x.Hash))
                                            .OrderByDescending(x => x.IsOnline)
                                            .GroupBy(x => x.Hash)
                                            .Select(group => group.First())
                                            .OrderBy(x => songHashs.IndexOf(x.Hash))
                                            .ToArray();
                if (targetCharts.Length == 0)
                {
                    MajDebug.LogError("Failed to load dan, songs are empty or unable to find:" + danInfo.Name);
                    return default;
                }
                if (source is not null)
                {
                    return new OnlineSongCollection(source, danInfo.Name, targetCharts)
                    {
                        Definition = danInfo,
                        Id = id,
                        Type = danInfo.IsPlayList ? ChartStorageType.PlayList : ChartStorageType.Dan,
                        DanInfo = danInfo.IsPlayList ? null : danInfo
                    };
                }
                else
                {
                    return new SongCollection(danInfo.Name, targetCharts)
                    {
                        Id = id,
                        Type = danInfo.IsPlayList ? ChartStorageType.PlayList : ChartStorageType.Dan,
                        DanInfo = danInfo.IsPlayList ? null : danInfo
                    };
                }
            });
        }
        #endregion
        /// <summary>Exports the committed favorites view when the game saves its state.</summary>
        /// <param name="sender">The source of the save event.</param>
        /// <param name="args">The save event arguments.</param>
        private static void OnSave(object? sender, EventArgs? args)
        {
            try
            {
                if (!s_isInitialized)
                {
                    return;
                }
                var hashSet = s_myFavorites.ExportHashSet();
                var exportDirectory = MajEnv.ChartDirectory;
                var exportFile = exportDirectory.FindFile(MyFavoriteFileName)
                    ?? exportDirectory.CreateFile(MyFavoriteFileName, "application/json");
                exportFile.WriteAllText(
                                  Serializer.Json.Serialize(new DanInfo()
                                  {
                                      Name = "My Favorites",
                                      SongHashs = hashSet.ToArray(),
                                      IsPlayList = true
                                  }
                    ));
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
            }
        }
        /// <summary>Derives a repeatable collection identity from the complete playlist file contents.</summary>
        /// <param name="stream">The readable stream containing the playlist file.</param>
        /// <returns>A GUID containing the MD5 digest of the file contents.</returns>
        /// <exception cref="IOException">The stream could not be read.</exception>
        private static Guid GetGuidFromStream(Stream stream)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(stream);
            var idBytes = (stackalloc byte[16]);
            hash.AsSpan(0, 16).CopyTo(idBytes);
            var id = new Guid(idBytes);

            return id;
        }
        /// <summary>Adds a chart to the committed favorites view and schedules persistence of its hash.</summary>
        /// <param name="songDetail">The chart to mark as a favorite.</param>
        public static void AddToMyFavorites(ISongDetail songDetail)
        {
            s_myFavorites.Add(songDetail);
            var hash = songDetail.Hash;
            if (!string.IsNullOrEmpty(hash))
            {
                s_favoritesDatabase?.RunInTransactionAsync(conn => conn.InsertOrReplace(new FavoriteHashDB { Hash = hash }));
            }
        }
        /// <summary>Checks whether a chart belongs to the committed favorites view.</summary>
        /// <param name="songDetail">The chart whose identity is checked.</param>
        /// <returns>True when a favorite chart has the same hash.</returns>
        public static bool IsInMyFavorites(ISongDetail songDetail)
        {
            return s_myFavorites.Any(o => o.Hash == songDetail.Hash);
        }
        /// <summary>Removes a chart from the committed favorites view and schedules removal of its stored hash.</summary>
        /// <param name="songDetail">The chart to remove from favorites.</param>
        public static void RemoveFromMyFavorites(ISongDetail songDetail)
        {
            s_myFavorites.Remove(songDetail);
            var hash = songDetail.Hash;
            if (!string.IsNullOrEmpty(hash))
            {
                s_favoritesDatabase?.RunInTransactionAsync(conn => conn.Delete<FavoriteHashDB>(hash));
            }
        }
        /// <summary>Removes a chart identity from the committed favorites view and persistent storage.</summary>
        /// <param name="hashBase64Str">The chart identity to remove from favorites.</param>
        public static void RemoveFromMyFavorites(string hashBase64Str)
        {
            s_myFavorites.Remove(hashBase64Str);
            if (!string.IsNullOrEmpty(hashBase64Str))
            {
                s_favoritesDatabase?.RunInTransactionAsync(conn => conn.Delete<FavoriteHashDB>(hashBase64Str));
            }
        }
        /// <summary>Loads favorite hashes from the database and the optional portable JSON export.</summary>
        /// <returns>The loaded favorite hashes without changing the committed favorites view.</returns>
        /// <exception cref="IOException">Favorite import or backup storage could not be accessed.</exception>
        /// <exception cref="SQLiteException">The favorite database could not be opened or queried.</exception>
        private static async Task<HashSet<string>> LoadFavoriteHashesAsync()
        {
            // Init SQLite DB (once)
            if (s_favoritesDatabase is null)
            {
                var runtimeDirectory = MajEnv.RuntimeCacheDirectory;
                var databaseFile = runtimeDirectory.FindFile(FavoriteDatabaseFileName);
                // SQLite leaves a database empty until its first table is created, so an empty file is still a new database.
                var isDbExists = databaseFile is not null && databaseFile.Entry is { Length: > 0 };
                // SQLite needs a resolvable location, so the file is created through the directory before the connection opens it.
                databaseFile ??= runtimeDirectory.CreateFile(FavoriteDatabaseFileName, "application/vnd.sqlite3");
                var database = new SQLiteAsyncConnection(databaseFile.Location, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
                try
                {
                    await database.CreateTableAsync<FavoriteHashDB>().ConfigureAwait(false);
                    if (!isDbExists)
                    {
                        var legacyFile = runtimeDirectory.FindFile(MyFavoriteFileName);
                        if (legacyFile is not null)
                        {
                            await MigrateFavFromJsonAsync(database, runtimeDirectory, legacyFile).ConfigureAwait(false);
                        }
                    }
                }
                catch
                {
                    await database.CloseAsync().ConfigureAwait(false);
                    throw;
                }
                s_favoritesDatabase = database;
                GameManager.OnAppQuit += OnAppQuit;
            }

            // Load export JSON (user-facing, stays as JSON for portability).
            DanInfo? exportedFavorites = null;
            var exportFile = MajEnv.ChartDirectory.FindFile(MyFavoriteFileName);
            if (exportFile is not null)
            {
                bool result;
                Exception? exception;
                using var exportStream = exportFile.OpenRead();
                (result, exportedFavorites, exception) = await Serializer.Json.TryDeserializeAsync<DanInfo>(exportStream);
                if (!result)
                {
                    var exportDirectory = MajEnv.ChartDirectory;
                    var backupName = $"{MyFavoriteFileName}.bak";
                    while (exportDirectory.FindFile(backupName) is not null)
                    {
                        backupName = $"{backupName}.bak";
                    }
                    exportFile.CopyTo(exportDirectory, backupName);
                    MajDebug.LogError($"Failed to load favorites\nPath: {exportFile.Location}\nException: {exception}");
                }
            }

            // Load from SQLite
            var favoriteHashes = new HashSet<string>();
            var rows = await s_favoritesDatabase.Table<FavoriteHashDB>().ToListAsync();
            foreach (var row in rows)
            {
                if (!string.IsNullOrEmpty(row.Hash))
                {
                    favoriteHashes.Add(row.Hash);
                }
            }
            if (exportedFavorites is not null)
            {
                favoriteHashes.UnionWith(exportedFavorites.SongHashs);
            }
            return favoriteHashes;
        }
        /// <summary>Imports the legacy JSON favorites cache into SQLite and retains a backup.</summary>
        /// <param name="database">The successfully initialized connection which is not yet globally published.</param>
        /// <param name="storageDirectory">The original backend directory containing the legacy cache and backup.</param>
        /// <param name="storageFile">The existing legacy favorites file to import.</param>
        /// <returns>A task which completes after migration or logging a recoverable migration failure.</returns>
        private static async Task MigrateFavFromJsonAsync(SQLiteAsyncConnection database, StorageDirectory storageDirectory, StorageFile storageFile)
        {
            try
            {
                using var storageStream = storageFile.OpenRead();
                var (result, storageFav, exception) = await Serializer.Json.TryDeserializeAsync<HashSet<string>>(storageStream);
                if (result && storageFav is not null)
                {
                    await database.RunInTransactionAsync(conn =>
                    {
                        foreach (var hash in storageFav)
                        {
                            if (!string.IsNullOrEmpty(hash))
                            {
                                conn.Insert(new FavoriteHashDB { Hash = hash });
                            }
                        }
                    });
                }
                try
                {
                    var backupName = MyFavoriteFileName + ".bak";
                    storageDirectory.FindFile(backupName)?.Delete();
                    storageFile.Rename(backupName);
                    MajDebug.LogInfo("Migrated favorites from legacy JSON to SQLite, old file backed up.");
                }
                catch (Exception ex)
                {
                    MajDebug.LogError($"Failed to backup legacy favorite file: {ex.Message}");
                }
            }
            catch (Exception e)
            {
                MajDebug.LogError($"Failed to migrate favorites from JSON: {e.Message}");
            }
        }

        /// <summary>Unsubscribes the shutdown handler and closes the shared favorites connection.</summary>
        /// <param name="sender">The source of the shutdown event.</param>
        /// <param name="args">The shutdown event arguments.</param>
        private static void OnAppQuit(object? sender, EventArgs? args)
        {
            GameManager.OnAppQuit -= OnAppQuit;
            s_favoritesDatabase?.CloseAsync().Wait();
        }

        /// <summary>Represents a persistent favorite chart identity.</summary>
        [Table("FavoriteHashes")]
        private class FavoriteHashDB
        {
            /// <summary>Gets or sets the Base64 chart identity used as the database primary key.</summary>
            [PrimaryKey]
            public string Hash { get; set; } = string.Empty;
        }
    }
}
