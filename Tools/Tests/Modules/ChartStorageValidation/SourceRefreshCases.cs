#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.Collections;
using MajdataPlay.IO.Storage;
using MajdataPlay.Net;
using NativeDirectory = System.IO.Directory;
using NativeFile = System.IO.File;

namespace MajdataPlay.Tests.ChartStorageValidation
{
    /// <summary>Checks independently loaded sources, cached playlist rebinding and their shared publication lock.</summary>
    internal static partial class Program
    {
        /// <summary>Runs the online-first scenario in a fresh instance of this executable rather than resetting private storage state.</summary>
        /// <returns>A task completing after the isolated scenario passes.</returns>
        /// <exception cref="InvalidOperationException">The child executable cannot start or its assertions fail.</exception>
        /// <exception cref="TimeoutException">The child scenario does not finish within its deadline.</exception>
        private static async Task RunOnlineFirstProcessAsync()
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The validation executable path is unavailable.");
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = NativeDirectory.GetCurrentDirectory()
            };
            if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            }
            startInfo.ArgumentList.Add("--online-first");
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The online-first validation process did not start.");
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
                Console.Write(await output);
                Console.Error.Write(await errors);
                Require(process.ExitCode == 0, "The independently initialized online-first scenario failed.");
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
        }

        /// <summary>Proves initialization does not scan and the first online refresh needs neither a local refresh nor local filesystem access.</summary>
        /// <param name="rootPath">The fresh child process's owned fixture root.</param>
        /// <returns>A task completing after initial online publication and saved-selection assertions.</returns>
        private static async Task TestOnlineFirstRefresh(string rootPath)
        {
            var path = CreateFixtureDirectory(rootPath, "OnlineFirst");
            var nativeChartRoot = CreateFixtureDirectory(path, "Charts");
            var localCollection = CreateFixtureDirectory(nativeChartRoot, "MustNotBeScanned");
            WriteChart(localCollection, "Local", new ChartFixture { Hash = "initial-local", Title = "Unloaded local chart" });
            var remoteHash = Convert.ToBase64String(new byte[16]);
            var selectedHash = Convert.ToBase64String(Enumerable.Repeat((byte)1, 16).ToArray());
            NativeFile.WriteAllText(Path.Combine(nativeChartRoot, "MyFavorites.json"), JsonSerializer.Serialize(new DanInfo
            {
                Name = "Initial favorites",
                SongHashs = new[] { selectedHash },
                IsPlayList = true
            }));
            var backend = new OpaqueFileSystem(nativeChartRoot);
            MajEnv.ChartDirectory = backend.Root;
            MajEnv.CacheDirectory = FileSystem.Directory.Open(CreateFixtureDirectory(path, "Cache"));
            SongDetail.Probe = new ParseProbe();
            var beforeInitialization = SongStorage.Collections;
            SQLite.SQLiteAsyncConnection.QueryException = new IOException("Injected initialization favorites-query failure.");
            try
            {
                await ExpectExceptionAsync<IOException>(() => SongStorage.InitAsync());
                Require(ReferenceEquals(SongStorage.Collections, beforeInitialization) && SongStorage.Collections.Length == 0
                    && SongStorage.TotalChartCount == 0 && SongDetail.Probe.StartedCount == 0,
                    "A favorites-query failure during initialization must not publish collections, counts or parsed charts.");
            }
            finally
            {
                SQLite.SQLiteAsyncConnection.QueryException = null;
            }
            await SongStorage.InitAsync();
            Require(ReferenceEquals(SongStorage.Collections, beforeInitialization) && SongStorage.Collections.Length == 0
                && SongStorage.TotalChartCount == 0 && SongDetail.Probe.StartedCount == 0,
                "Initialization with existing charts and exported favorites must publish no collections or parsed charts.");

            var initializationAccessCount = backend.AccessCount;
            backend.RejectAccess = true;
            var endpoint = new ApiEndpoint { Name = "First online endpoint" };
            var identity = Guid.NewGuid();
            var marker = CreateFixtureDirectory(Path.Combine(MajEnv.CacheDirectory.Location, "Net", endpoint.Name), ".MajdataPlay");
            NativeFile.WriteAllText(Path.Combine(marker, "id"), identity.ToString());
            MajEnv.RuntimeConfig.List.SelectedDir = 100;
            MajEnv.RuntimeConfig.List.SelectedSongIndex = 0;
            MajEnv.RuntimeConfig.List.SelectedDirGuid = identity;
            MajEnv.RuntimeConfig.List.SelectedSongHash = selectedHash;
            MajEnv.ApiEndpoints = new[] { endpoint };
            MajEnv.Settings.Online.Enable = true;
            Online.ChartListHandler = (source, token) => Task.FromResult<ChartFixture[]?>(new[]
            {
                new ChartFixture { Hash = remoteHash, Title = "Remote first" },
                new ChartFixture { Hash = selectedHash, Title = "Saved remote selection" }
            });
            await SongStorage.RefreshOnline();
            Require(SongStorage.TotalChartCount == 2 && SongStorage.Collections.Single(collection => collection.Name == "All").Count == 2
                && SongStorage.Collections.Single(collection => collection.Name == "MyFavorites").Single().Hash == selectedHash,
                "A first online refresh must construct online base, aggregate and favorites without a local refresh.");
            Require(SongStorage.Collections.All(collection => collection.IsVirtual || collection.IsOnline)
                && backend.AccessCount == initializationAccessCount && SongDetail.Probe.StartedCount == 0,
                "A first online refresh must not enumerate the local root, parse a local chart or publish a local base collection.");
            Require(SongStorage.WorkingCollection.Id == identity && SongStorage.WorkingCollection.Current.Hash == selectedHash,
                "The first source refresh must restore the saved collection GUID and chart hash instead of clamping to an empty-library cursor.");
            SongStorage.RemoveFromMyFavorites(selectedHash);
            SQLite.SQLiteAsyncConnection.QueryException = new IOException("Refresh must not query initialized favorites storage.");
            try
            {
                await SongStorage.RefreshOnline();
                Require(SongStorage.Collections.Single(collection => collection.Name == "MyFavorites").IsEmpty,
                    "An online refresh must not resurrect a runtime removal by rereading the unchanged favorites export or database.");
            }
            finally
            {
                SQLite.SQLiteAsyncConnection.QueryException = null;
            }
            var published = SongStorage.Collections;
            await SongStorage.InitAsync();
            Require(ReferenceEquals(SongStorage.Collections, published) && SongStorage.TotalChartCount == 2 && backend.AccessCount == initializationAccessCount,
                "Repeated initialization must not refresh or replace a published library.");
            MajEnv.ChartDirectory = FileSystem.Directory.Open(nativeChartRoot);
            Console.WriteLine("PASS online first: non-scanning initialization, no local I/O, online-only base/favorites and saved GUID/hash selection");
        }

        /// <summary>Exercises both source boundaries, cached mixed-source definitions, failed publication and mixed-source lock ordering.</summary>
        /// <param name="rootPath">The parent runner's owned fixture root.</param>
        /// <returns>A task completing after independent source refresh assertions.</returns>
        private static async Task TestIndependentSourceRefresh(string rootPath)
        {
            var originalChartPath = MajEnv.ChartDirectory;
            var originalCachePath = MajEnv.CacheDirectory;
            var originalEndpoints = MajEnv.ApiEndpoints;
            var originalChartHandler = Online.ChartListHandler;
            var originalFavoritesHandler = Online.FavoritesHandler;
            var originalQueryException = SQLite.SQLiteAsyncConnection.QueryException;
            var path = CreateFixtureDirectory(rootPath, "IndependentSources");
            var nativeChartRoot = CreateFixtureDirectory(path, "Charts");
            var localPath = CreateFixtureDirectory(nativeChartRoot, "Independent local");
            var localChartPath = WriteChart(localPath, "Local", new ChartFixture { Hash = "split-local", Title = "Local original" });
            var remoteHash = Convert.ToBase64String(Enumerable.Repeat((byte)2, 16).ToArray());
            var mixedDefinition = new DanInfo { Name = "Cached mixed playlist", SongHashs = new[] { "split-local", remoteHash }, IsPlayList = true };
            var mixedPath = Path.Combine(nativeChartRoot, "Mixed.json");
            var remoteOnlyPath = Path.Combine(nativeChartRoot, "RemoteOnly.json");
            NativeFile.WriteAllText(mixedPath, JsonSerializer.Serialize(mixedDefinition));
            NativeFile.WriteAllText(remoteOnlyPath, JsonSerializer.Serialize(new DanInfo { Name = "Initially unmatched playlist", SongHashs = new[] { remoteHash }, IsPlayList = true }));
            NativeFile.WriteAllText(Path.Combine(nativeChartRoot, "MissingHashes.json"), "{\"Name\":\"Invalid missing hashes\"}");
            NativeFile.WriteAllText(Path.Combine(nativeChartRoot, "MissingName.json"), "{\"SongHashs\":[\"split-local\"]}");
            var backend = new OpaqueFileSystem(nativeChartRoot, useContentUris: true);
            var endpoint = new ApiEndpoint { Name = "Independent endpoint" };
            var requestCount = 0;
            try
            {
                SQLite.SQLiteAsyncConnection.QueryException = new IOException("Source refreshes must not requery favorites storage.");
                MajEnv.Settings.Online.Enable = false;
                await SongStorage.RefreshOnline();
                FileSystem.RegisterContentProvider(backend);
                MajEnv.ChartDirectory = backend.Root;
                MajEnv.CacheDirectory = FileSystem.Directory.Open(CreateFixtureDirectory(path, "Cache"));
                MajEnv.ApiEndpoints = new[] { endpoint };
                MajEnv.Settings.Online.Enable = true;
                Online.ChartListHandler = (source, token) =>
                {
                    Interlocked.Increment(ref requestCount);
                    throw new InvalidOperationException("A local refresh entered the online chart-list boundary.");
                };
                Online.FavoritesHandler = (source, token) =>
                {
                    Interlocked.Increment(ref requestCount);
                    throw new InvalidOperationException("A source refresh entered the online favorites boundary.");
                };
                SongDetail.Probe = new ParseProbe();
                await SongStorage.RefreshLocal();
                var local = SongStorage.Collections.Single(collection => collection.Name == "Independent local");
                var localSong = (SongDetail)local.Single();
                var mixed = SongStorage.Collections.Single(collection => collection.Name == mixedDefinition.Name);
                var mixedId = mixed.Id;
                Require(requestCount == 0 && SongDetail.Probe.CompletedCount == 1 && SongStorage.TotalChartCount == 1
                    && mixed.Single().Hash == localSong.Hash && !SongStorage.Collections.Any(collection => collection.Name == "Initially unmatched playlist"),
                    "A local refresh must load only local charts, isolate incomplete JSON and cache currently unmatched definitions without online requests.");
                SongStorage.AddToMyFavorites(localSong);

                backend.RejectAccess = true;
                var accessCount = backend.AccessCount;
                SongDetail.Probe = new ParseProbe();
                Online.ChartListHandler = (source, token) =>
                {
                    Interlocked.Increment(ref requestCount);
                    return Task.FromResult<ChartFixture[]?>(new[] { new ChartFixture { Hash = remoteHash, Title = "Remote original" } });
                };
                await SongStorage.RefreshOnline();
                var remote = SongStorage.Collections.OfType<OnlineSongCollection>().Single(collection => collection.Type == ChartStorageType.List);
                var remoteSong = (OnlineSongDetail)remote.Single();
                var remoteOnlyId = SongStorage.Collections.Single(collection => collection.Name == "Initially unmatched playlist").Id;
                Require(ReferenceEquals(SongStorage.Collections.Single(collection => collection.Id == local.Id), local)
                    && ReferenceEquals(local.Single(), localSong) && !localSong.IsDisposed
                    && backend.AccessCount == accessCount && SongDetail.Probe.StartedCount == 0,
                    "An online refresh must retain local collection/chart references and perform zero local filesystem operations or parses.");
                SongStorage.AddToMyFavorites(remoteSong);
                var remotePlaylistId = Guid.NewGuid();
                var remotePlaylist = new OnlineSongCollection(endpoint, "Remote mixed playlist", new ISongDetail[] { localSong, remoteSong })
                {
                    Id = remotePlaylistId,
                    Type = ChartStorageType.PlayList,
                    Definition = mixedDefinition
                };
                typeof(SongStorage).GetProperty(nameof(SongStorage.Collections), BindingFlags.Public | BindingFlags.Static)!
                    .SetValue(null, SongStorage.Collections.Append(remotePlaylist).ToArray());
                SelectStorageSong(SongStorage.Collections.Single(collection => collection.Id == mixedId), remoteHash);

                backend.RejectAccess = false;
                NativeFile.WriteAllText(Path.Combine(localChartPath, "maidata.txt"), JsonSerializer.Serialize(new ChartFixture { Hash = "split-local", Title = "Local replacement" }));
                MajEnv.Settings.Online.Enable = false;
                var requestsBeforeLocalRefresh = requestCount;
                await SongStorage.RefreshLocal();
                local = SongStorage.Collections.Single(collection => collection.Id == local.Id);
                var replacementLocal = (SongDetail)local.Single();
                Require(requestCount == requestsBeforeLocalRefresh && ReferenceEquals(SongStorage.Collections.Single(collection => collection.Id == remote.Id), remote)
                    && ReferenceEquals(remote.Single(), remoteSong) && !remoteSong.IsDisposed && localSong.IsDisposed,
                    "A local refresh must retain the online base and its charts without requests even after online settings are disabled.");
                RequireSplitViews(replacementLocal, remoteSong, mixedId, remotePlaylistId);
                Require(SongStorage.WorkingCollection.Id == mixedId && SongStorage.WorkingCollection.Current.Hash == remoteHash,
                    "A local refresh must retain a selected online hash inside a mixed local playlist.");
                localSong = replacementLocal;

                NativeFile.WriteAllText(mixedPath, "{\"Name\":\"Must not be reloaded\"}");
                NativeFile.Delete(remoteOnlyPath);
                backend.RejectAccess = true;
                accessCount = backend.AccessCount;
                MajEnv.Settings.Online.Enable = true;
                SelectStorageSong(remote, remoteHash);
                SongDetail.Probe = new ParseProbe();
                await SongStorage.RefreshOnline();
                var replacementRemote = (OnlineSongDetail)SongStorage.Collections.Single(collection => collection.Id == remote.Id).Single();
                Require(ReferenceEquals(SongStorage.Collections.Single(collection => collection.Id == local.Id), local)
                    && ReferenceEquals(local.Single(), localSong) && !localSong.IsDisposed && remoteSong.IsDisposed
                    && backend.AccessCount == accessCount && SongDetail.Probe.StartedCount == 0,
                    "Online replacement must not reload modified/deleted playlist JSON or reparse/replace the retained local base.");
                RequireSplitViews(localSong, replacementRemote, mixedId, remotePlaylistId);
                Require(SongStorage.WorkingCollection.Id == remote.Id && ReferenceEquals(SongStorage.WorkingCollection.Current, replacementRemote),
                    "Online replacement must restore the selected online base ID/hash to its new chart instance.");
                remoteSong = replacementRemote;

                var previous = SongStorage.Collections;
                var previousAll = previous.Single(collection => collection.Name == "All").ToArray();
                var previousFavorites = previous.Single(collection => collection.Name == "MyFavorites");
                var createdBeforeFailure = OnlineSongDetail.CreatedDetails.Count;
                await ExpectExceptionAsync<IOException>(() => SongStorage.RefreshOnline(new ThrowOnCleanupProgress()));
                RequireUnchangedStorage(previous, previousAll, previousFavorites, 2);
                Require(OnlineSongDetail.CreatedDetails.Skip(createdBeforeFailure).Any()
                    && OnlineSongDetail.CreatedDetails.Skip(createdBeforeFailure).All(song => song.IsDisposed)
                    && !localSong.IsDisposed && !remoteSong.IsDisposed,
                    "A failed online publication must release only unpublished online charts and leave both committed sources alive.");
                NativeFile.WriteAllText(mixedPath, JsonSerializer.Serialize(mixedDefinition));
                NativeFile.WriteAllText(remoteOnlyPath, JsonSerializer.Serialize(new DanInfo { Name = "Initially unmatched playlist", SongHashs = new[] { remoteHash }, IsPlayList = true }));
                await CheckMixedSourceLockAsync(backend, endpoint, remoteHash);
                NativeFile.WriteAllText(mixedPath, "{\"Name\":\"Must not be reloaded\"}");
                NativeFile.Delete(remoteOnlyPath);

                backend.RejectAccess = true;
                accessCount = backend.AccessCount;
                var localBeforeDisable = SongStorage.Collections.Single(collection => collection.Id == local.Id);
                var localSongBeforeDisable = (SongDetail)localBeforeDisable.Single();
                var requestsBeforeDisable = requestCount;
                MajEnv.Settings.Online.Enable = false;
                await SongStorage.RefreshOnline();
                Require(SongStorage.Collections.All(collection => !collection.IsOnline) && SongStorage.TotalChartCount == 1
                    && ReferenceEquals(SongStorage.Collections.Single(collection => collection.Id == local.Id), localBeforeDisable)
                    && !localSongBeforeDisable.IsDisposed && requestCount == requestsBeforeDisable && backend.AccessCount == accessCount,
                    "Only an explicit disabled-online refresh may remove online bases/derived views; it must retain local references without I/O or requests.");
                Require(!SongStorage.Collections.Any(collection => collection.Id == remoteOnlyId)
                    && SongStorage.Collections.Single(collection => collection.Id == mixedId).Single().Hash == "split-local",
                    "Disabled-online rebinding must keep the local portion of cached playlists and hide only unmatched remote-only views.");
                MajEnv.Settings.Online.Enable = true;
                await SongStorage.RefreshOnline();
                Require(SongStorage.Collections.Single(collection => collection.Id == remoteOnlyId).Single().Hash == remoteHash
                    && backend.AccessCount == accessCount,
                    "A previously unmatched cached local definition must recover its original GUID when online songs return without reading its deleted JSON.");
                Console.WriteLine("PASS independent sources: preserved references, zero cross-source I/O/requests, cached definitions, selection, rollback and shared lock");
            }
            finally
            {
                backend.RejectAccess = false;
                MajEnv.ChartDirectory = originalChartPath;
                MajEnv.CacheDirectory = originalCachePath;
                MajEnv.ApiEndpoints = originalEndpoints;
                MajEnv.Settings.Online.Enable = false;
                Online.ChartListHandler = originalChartHandler;
                Online.FavoritesHandler = originalFavoritesHandler;
                SQLite.SQLiteAsyncConnection.QueryException = originalQueryException;
            }
        }

        /// <summary>Checks current aggregate, favorites and both kinds of mixed-source playlists against exact base chart references.</summary>
        /// <param name="local">The current retained or replacement local chart.</param>
        /// <param name="remote">The current retained or replacement remote chart.</param>
        /// <param name="localPlaylistId">The stable cached local playlist identity.</param>
        /// <param name="onlinePlaylistId">The stable online playlist identity.</param>
        private static void RequireSplitViews(ISongDetail local, ISongDetail remote, Guid localPlaylistId, Guid onlinePlaylistId)
        {
            var charts = new[] { local, remote };
            Require(SongStorage.TotalChartCount == 2 && SongStorage.Collections.Single(collection => collection.Name == "All").GetOriginSnapshot().SequenceEqual(charts)
                && SongStorage.Collections.Single(collection => collection.Name == "MyFavorites").GetOriginSnapshot().SequenceEqual(charts),
                "Aggregate and favorites must rebind to the exact current base charts without duplicate ownership counts.");
            Require(SongStorage.Collections.Single(collection => collection.Id == localPlaylistId).GetOriginSnapshot().SequenceEqual(charts)
                && SongStorage.Collections.Single(collection => collection.Id == onlinePlaylistId).GetOriginSnapshot().SequenceEqual(charts),
                "Local and online playlist definitions must both rebind to the current charts while preserving their identities.");
        }

        /// <summary>Selects a chart through the same public collection and runtime indices consumed by production refresh.</summary>
        /// <param name="collection">The published collection to select.</param>
        /// <param name="hash">The selected chart identity.</param>
        private static void SelectStorageSong(SongCollection collection, string hash)
        {
            collection.SetCursor(hash);
            MajEnv.RuntimeConfig.List.SelectedDir = Array.IndexOf(SongStorage.Collections, collection);
            MajEnv.RuntimeConfig.List.SelectedSongIndex = collection.Index;
            SongStorage.CollectionIndex = MajEnv.RuntimeConfig.List.SelectedDir;
        }

        /// <summary>Checks local/online lock ordering in both directions and cancellation during a request or queued admission.</summary>
        /// <param name="backend">The retained local fixture backend whose access is counted.</param>
        /// <param name="endpoint">The offline endpoint being refreshed.</param>
        /// <param name="remoteHash">The remote chart identity returned by the fixture.</param>
        /// <returns>A task completing after blocked requests and serialized retries finish.</returns>
        private static async Task CheckMixedSourceLockAsync(OpaqueFileSystem backend, ApiEndpoint endpoint, string remoteHash)
        {
            var handler = Online.ChartListHandler;
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Online.ChartListHandler = async (source, token) =>
            {
                Require(ReferenceEquals(source, endpoint), "The mixed-lock fixture received the wrong endpoint.");
                started.TrySetResult();
                await gate.Task.WaitAsync(token);
                return new[] { new ChartFixture { Hash = remoteHash, Title = "Serialized remote" } };
            };
            var previous = SongStorage.Collections;
            var previousAll = previous.Single(collection => collection.Name == "All").ToArray();
            var previousFavorites = previous.Single(collection => collection.Name == "MyFavorites");
            var accessCount = backend.AccessCount;
            using var cancellation = new CancellationTokenSource();
            var onlineRefresh = SongStorage.RefreshOnline(cancellationToken: cancellation.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var queuedCancellation = new CancellationTokenSource();
            var queuedLocal = SongStorage.RefreshLocal(cancellationToken: queuedCancellation.Token);
            await Task.Delay(100);
            Require(!queuedLocal.IsCompleted && backend.AccessCount == accessCount, "A local refresh queued behind online work must not enter the local filesystem before acquiring the shared lock.");
            queuedCancellation.Cancel();
            await ExpectExceptionAsync<OperationCanceledException>(() => queuedLocal);
            cancellation.Cancel();
            await ExpectExceptionAsync<OperationCanceledException>(() => onlineRefresh);
            RequireUnchangedStorage(previous, previousAll, previousFavorites, 2);
            Require(previousAll.Cast<SongDetail>().All(song => !song.IsDisposed), "Active online and queued-local cancellation must preserve both sources' chart instances.");

            backend.RejectAccess = false;
            SongDetail.Probe = new ParseProbe(block: true);
            var localRefresh = SongStorage.RefreshLocal();
            try
            {
                await SongDetail.Probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var requests = 0;
                Online.ChartListHandler = (source, token) =>
                {
                    Interlocked.Increment(ref requests);
                    return Task.FromResult<ChartFixture[]?>(new[] { new ChartFixture { Hash = remoteHash, Title = "Serialized remote" } });
                };
                var queuedOnline = SongStorage.RefreshOnline();
                await Task.Delay(100);
                Require(!queuedOnline.IsCompleted && requests == 0, "An online refresh queued behind local scanning must not enter its request boundary before acquiring the same lock.");
                RequireUnchangedStorage(previous, previousAll, previousFavorites, 2);
                SongDetail.Probe.Gate!.TrySetResult();
                await Task.WhenAll(localRefresh, queuedOnline).WaitAsync(TimeSpan.FromSeconds(20));
                Require(requests == 1 && SongDetail.Probe.CompletedCount == 1 && SongStorage.TotalChartCount == 2,
                    "Serialized source refreshes must each load their own source exactly once and publish a coherent combined library.");
            }
            finally
            {
                SongDetail.Probe.Gate!.TrySetResult();
                Online.ChartListHandler = handler;
            }
        }
    }
}
