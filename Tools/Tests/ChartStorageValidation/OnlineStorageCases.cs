#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.Collections;
using MajdataPlay.IO.Storage;
using MajdataPlay.Net;

namespace MajdataPlay.Tests.ChartStorageValidation
{
    /// <summary>Checks offline online-collection control flow without issuing any network requests.</summary>
    internal static partial class Program
    {
        /// <summary>Checks that production online-list loading constructs legacy-named, authoritative per-song cache handles.</summary>
        /// <param name="rootPath">The owned fixture root for offline online-list responses.</param>
        /// <returns>A task completing after construction and cached-list fallback assertions.</returns>
        private static async Task TestOnlineCacheDirectories(string rootPath)
        {
            var originalCachePath = MajEnv.CacheDirectory;
            var originalHandler = Online.ChartListHandler;
            var path = CreateFixtureDirectory(rootPath, "OnlineCacheDirectories");
            var endpoint = new ApiEndpoint { Name = "Fixture endpoint" };
            var hashBytes = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 171, 255 };
            var metadata = new ChartFixture { Hash = Convert.ToBase64String(hashBytes), Title = "Remote metadata" };
            var method = typeof(SongStorage).GetMethod("GetOnlineCollection", BindingFlags.NonPublic | BindingFlags.Static)!;
            try
            {
                var opaquePath = CreateFixtureDirectory(path, "Opaque");
                var backend = new OpaqueFileSystem(opaquePath);
                foreach (var fixture in new[]
                {
                    (Directory: FileSystem.Directory.Open(path), NativePath: path),
                    (Directory: backend.Root, NativePath: opaquePath)
                })
                {
                    MajEnv.CacheDirectory = fixture.Directory;
                    Online.ChartListHandler = (source, token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        Require(ReferenceEquals(source, endpoint), "Online metadata must be fetched from the requested endpoint.");
                        return Task.FromResult<ChartFixture[]?>(new[] { metadata });
                    };
                    var collection = await (Task<SongCollection>)method.Invoke(null, new object?[] { endpoint, null, CancellationToken.None })!;
                    Require(collection.Count == 1 && collection.IsOnline, "A valid offline response must construct an online chart.");
                    var detail = (OnlineSongDetail)collection.Single();
                    var cacheName = BitConverter.ToString(hashBytes);
                    var expectedPath = Path.Combine(fixture.NativePath, "Net", endpoint.Name, cacheName);
                    var expectedDirectory = fixture.Directory.FindDirectory("Net")!.FindDirectory(endpoint.Name)!.FindDirectory(cacheName)!;
                    Require(detail.Directory is not null && detail.Directory.Exists && detail.Directory.Location == expectedDirectory.Location, "The constructor must receive an existing per-song directory preserving the legacy uppercase, hyphen-separated hash name.");
                    Require(ReferenceEquals(fixture.Directory.FileSystem, collection.Directory!.FileSystem)
                        && ReferenceEquals(collection.Directory.FileSystem, detail.Directory!.FileSystem), "Per-song cache construction must retain the collection cache's backend.");
                    WriteChart(Path.GetDirectoryName(expectedPath)!, cacheName, metadata);
                    Online.ChartListHandler = (source, token) => Task.FromResult<ChartFixture[]?>(null);
                    var cached = await (Task<SongCollection>)method.Invoke(null, new object?[] { endpoint, null, CancellationToken.None })!;
                    Require(cached.IsOnline && cached.Count == 1 && cached.Single().Hash == metadata.Hash, "An unavailable remote list must scan the same caller-owned per-song cache directories.");
                }
            }
            finally
            {
                Online.ChartListHandler = originalHandler;
                MajEnv.CacheDirectory = originalCachePath;
            }
            Console.WriteLine("PASS online cache: directory-based construction, legacy hash names, original backend and cache fallback");
        }

        /// <summary>Checks retained filtered origins, derived-list rebinding and endpoint-specific replacement semantics.</summary>
        /// <param name="rootPath">The owned fixture root containing the already initialized library.</param>
        /// <returns>A task completing after local-only and fake endpoint refresh assertions.</returns>
        private static async Task TestRetainedOnlineOrigins(string rootPath)
        {
            var baseChartCount = SongStorage.TotalChartCount;
            var expectedChartCount = baseChartCount + 2;
            var originalEndpoints = MajEnv.ApiEndpoints;
            var endpoint = new ApiEndpoint { Name = "Successful endpoint" };
            var failingEndpoint = new ApiEndpoint { Name = "Failed endpoint" };
            endpoint.RuntimeConfig.IsLoggedIn = true;
            failingEndpoint.RuntimeConfig.IsLoggedIn = true;
            var cache = MajEnv.CacheDirectory.CreateDirectory("RetainedEndpoint");
            var remoteVisible = new OnlineSongDetail(cache.CreateDirectory("Visible"), endpoint, new ChartFixture { Hash = "remote-visible", Title = "Visible remote" });
            var remoteHidden = new OnlineSongDetail(cache.CreateDirectory("Hidden"), endpoint, new ChartFixture { Hash = "remote-hidden", Title = "Hidden remote" });
            var retained = new OnlineSongCollection(endpoint, cache, "Retained endpoint", new ISongDetail[] { remoteVisible, remoteHidden });
            retained.SortAndFilter(new SongOrder { Keyword = "no-match", SortBy = SortType.ByTitle });
            Require(retained.IsEmpty && retained.GetOriginSnapshot().Length == 2, "The retained-online fixture must expose an empty filtered view with two original charts.");
            var oldLocal = SongStorage.Collections.Single(collection => collection.Name == "All").Single(song => song.Hash == "other");
            var playlistId = Guid.NewGuid();
            var courseId = Guid.NewGuid();
            var failedId = Guid.NewGuid();
            var oldPlaylist = new OnlineSongCollection(endpoint, "Remote playlist", new ISongDetail[] { oldLocal, remoteVisible })
            {
                Id = playlistId,
                Type = ChartStorageType.PlayList,
                Definition = new DanInfo { Name = "Remote playlist", SongHashs = new[] { "other", "remote-visible" }, IsPlayList = true }
            };
            oldPlaylist.SortAndFilter(new SongOrder { Keyword = "no-match" });
            var courseInfo = new DanInfo { Name = "Remote course", SongHashs = new[] { "other", "remote-hidden" }, Description = "Preserve course metadata" };
            var oldCourse = new OnlineSongCollection(endpoint, courseInfo.Name, new ISongDetail[] { oldLocal, remoteHidden })
            {
                Id = courseId,
                Type = ChartStorageType.Dan,
                DanInfo = courseInfo,
                Definition = courseInfo
            };
            var failedCourse = new OnlineSongCollection(failingEndpoint, "Failed endpoint course", new ISongDetail[] { remoteHidden })
            {
                Id = failedId,
                Type = ChartStorageType.Dan,
                DanInfo = new DanInfo { Name = "Failed endpoint course", SongHashs = new[] { "remote-hidden" } }
            };
            var injected = SongStorage.Collections.Concat(new SongCollection[] { retained, oldPlaylist, oldCourse, failedCourse }).ToArray();
            var collectionsProperty = typeof(SongStorage).GetProperty(nameof(SongStorage.Collections), BindingFlags.Public | BindingFlags.Static)!;
            collectionsProperty.SetValue(null, injected);
            MajEnv.Settings.Online.Enable = true;
            MajEnv.ApiEndpoints = new[] { endpoint, failingEndpoint };
            try
            {
                SongDetail.Probe = new ParseProbe();
                await SongStorage.RefreshLocal();
                var all = SongStorage.Collections.Single(collection => collection.Name == "All");
                var newLocal = all.Single(song => song.Hash == "other");
                var reboundPlaylist = SongStorage.Collections.OfType<OnlineSongCollection>().Single(collection => collection.Id == playlistId);
                var reboundCourse = SongStorage.Collections.OfType<OnlineSongCollection>().Single(collection => collection.Id == courseId);
                var currentFailedCourse = SongStorage.Collections.OfType<OnlineSongCollection>().Single(collection => collection.Id == failedId);
                Require(SongStorage.TotalChartCount == expectedChartCount && all.Count == expectedChartCount, "A retained base list's filter must not remove original charts from the aggregate or committed count, and derived duplicates must not inflate it.");
                Require(ReferenceEquals(SongStorage.Collections.Single(collection => collection.Name == retained.Name), retained) && retained.IsEmpty, "Local-only refresh must retain the base online collection instance and its active empty filter.");
                Require(all.Any(song => ReferenceEquals(song, remoteVisible)) && all.Any(song => ReferenceEquals(song, remoteHidden)) && !remoteVisible.IsDisposed && !remoteHidden.IsDisposed, "Origin-only remote charts must remain reachable and undisposed even when their online list is filtered to zero.");
                Require(!ReferenceEquals(oldLocal, newLocal) && ((SongDetail)oldLocal).IsDisposed, "Local-only refresh must replace obsolete local chart instances.");
                Require(!ReferenceEquals(oldPlaylist, reboundPlaylist) && reboundPlaylist.GetOriginSnapshot().SequenceEqual(new ISongDetail[] { newLocal, remoteVisible }), "A filtered virtual online playlist must be rebound from its unfiltered origin to the replacement local and retained online charts.");
                Require(!ReferenceEquals(oldCourse, reboundCourse) && reboundCourse.GetOriginSnapshot().SequenceEqual(new ISongDetail[] { newLocal, remoteHidden }) && ReferenceEquals(reboundCourse.DanInfo, courseInfo), "Online courses must retain identity and course metadata while rebinding songs to the committed base snapshot.");
                Require(reboundPlaylist.IsVirtual && reboundPlaylist.IsOnline && reboundCourse.Type == ChartStorageType.Dan, "Retained derived online collections must keep virtual playlist/course semantics.");
                Require(SongStorage.IsInMyFavorites(newLocal) == false, "Deleting a favorite chart from the prior fixture must not invent membership for an unrelated chart.");

                var localDirectory = ((SongDetail)newLocal).Directory!;
                var metadataFile = localDirectory.FindFile("maidata.txt")!;
                var savedMetadata = metadataFile.ReadAllText();
                metadataFile.Delete();
                SongDetail.Probe = new ParseProbe();
                await SongStorage.RefreshLocal();
                var partialPlaylist = SongStorage.Collections.OfType<OnlineSongCollection>().Single(collection => collection.Id == playlistId);
                Require(partialPlaylist.GetOriginSnapshot().Select(song => song.Hash).SequenceEqual(new[] { "remote-visible" })
                    && partialPlaylist.Definition!.SongHashs.SequenceEqual(new[] { "other", "remote-visible" }) && partialPlaylist.DanInfo is null,
                    "Partial missing songs must not shrink the full online playlist definition or expose playlist DanInfo.");
                localDirectory.CreateFile("maidata.txt", "text/plain").WriteAllText(savedMetadata);
                await SongStorage.RefreshLocal();
                var restoredPlaylist = SongStorage.Collections.OfType<OnlineSongCollection>().Single(collection => collection.Id == playlistId);
                Require(restoredPlaylist.GetOriginSnapshot().Select(song => song.Hash).SequenceEqual(new[] { "other", "remote-visible" }),
                    "Reimporting a previously missing local chart must restore all requested online playlist hashes.");

                localDirectory.FindFile("maidata.txt")!.Delete();
                var emptyBase = new OnlineSongCollection(endpoint, cache, retained.Name, Array.Empty<ISongDetail>()) { Id = retained.Id };
                collectionsProperty.SetValue(null, SongStorage.Collections.Select(collection => ReferenceEquals(collection, retained) ? emptyBase : collection).ToArray());
                await SongStorage.RefreshLocal();
                var emptyPlaylist = SongStorage.Collections.OfType<OnlineSongCollection>().Single(collection => collection.Id == playlistId);
                var emptyCourse = SongStorage.Collections.OfType<OnlineSongCollection>().Single(collection => collection.Id == courseId);
                Require(emptyPlaylist.IsEmpty && emptyPlaylist.Definition!.SongHashs.SequenceEqual(new[] { "other", "remote-visible" }) && emptyPlaylist.DanInfo is null,
                    "When all requested songs are missing, keep the empty virtual playlist and its original definition for later restoration.");
                Require(emptyCourse.IsEmpty && emptyCourse.Definition!.SongHashs.SequenceEqual(new[] { "other", "remote-hidden" }) && ReferenceEquals(emptyCourse.DanInfo, courseInfo),
                    "An all-missing online course must retain its full definition and public course metadata.");
                Require(remoteVisible.IsDisposed && remoteHidden.IsDisposed, "Removing the remote base origin permits the owning storage snapshot to release its obsolete remote instances.");

                localDirectory.CreateFile("maidata.txt", "text/plain").WriteAllText(savedMetadata);
                remoteVisible = new OnlineSongDetail(cache.CreateDirectory("Visible"), endpoint, new ChartFixture { Hash = "remote-visible", Title = "Visible remote restored" });
                remoteHidden = new OnlineSongDetail(cache.CreateDirectory("Hidden"), endpoint, new ChartFixture { Hash = "remote-hidden", Title = "Hidden remote restored" });
                retained = new OnlineSongCollection(endpoint, cache, retained.Name, new ISongDetail[] { remoteVisible, remoteHidden }) { Id = retained.Id };
                retained.SortAndFilter(new SongOrder { Keyword = "no-match" });
                collectionsProperty.SetValue(null, SongStorage.Collections.Select(collection => ReferenceEquals(collection, emptyBase) ? retained : collection).ToArray());
                await SongStorage.RefreshLocal();
                all = SongStorage.Collections.Single(collection => collection.Name == "All");
                newLocal = all.Single(song => song.Hash == "other");
                var fullyRestoredPlaylist = SongStorage.Collections.OfType<OnlineSongCollection>().Single(collection => collection.Id == playlistId);
                var fullyRestoredCourse = SongStorage.Collections.OfType<OnlineSongCollection>().Single(collection => collection.Id == courseId);
                currentFailedCourse = SongStorage.Collections.OfType<OnlineSongCollection>().Single(collection => collection.Id == failedId);
                Require(fullyRestoredPlaylist.GetOriginSnapshot().SequenceEqual(new ISongDetail[] { newLocal, remoteVisible })
                    && fullyRestoredCourse.GetOriginSnapshot().SequenceEqual(new ISongDetail[] { newLocal, remoteHidden }) && SongStorage.TotalChartCount == expectedChartCount,
                    "Restoring an all-missing definition must rebind playlist and course hashes to the new base chart instances without duplicate counts.");
                var responses = new MajNetOnlineDanInfo[]
                {
                    new MajNetOnlineDanInfo { Id = playlistId.ToString(), Name = "Remote playlist", SongHashs = new[] { "other", "remote-visible" }, IsPlayList = true },
                    new MajNetOnlineDanInfo { Id = courseId.ToString(), Name = "Remote course", SongHashs = new[] { "other", "remote-hidden" }, IsPlayList = false }
                };
                Online.FavoritesHandler = (source, token) => ReferenceEquals(source, endpoint)
                    ? Task.FromResult<MajNetOnlineDanInfo[]?>(responses)
                    : Task.FromException<MajNetOnlineDanInfo[]?>(new IOException("Injected endpoint favorites failure."));
                for (var repetition = 0; repetition < 3; repetition++)
                {
                    Require(!await SongStorage.RefreshUserOnlineFavAsync(), "One failed endpoint must make the result unsuccessful without discarding its existing derived collections.");
                    var derived = SongStorage.Collections.OfType<OnlineSongCollection>().Where(collection => collection.Type is ChartStorageType.PlayList or ChartStorageType.Dan).ToArray();
                    Require(derived.Count(collection => ReferenceEquals(collection.Source, endpoint)) == 2 && derived.Count(collection => collection.Id == courseId) == 1, "Successful endpoint refreshes must replace playlists and courses rather than accumulate duplicate courses.");
                    Require(ReferenceEquals(derived.Single(collection => collection.Id == failedId), currentFailedCourse), "A failed endpoint must retain its exact previous course object.");
                    Require(SongStorage.TotalChartCount == expectedChartCount && derived.Where(collection => ReferenceEquals(collection.Source, endpoint)).SelectMany(collection => collection.GetOriginSnapshot()).All(song => all.Any(chart => ReferenceEquals(chart, song))), "Endpoint-derived replacements must use the committed base charts and never alter the base chart total.");
                }
                Online.FavoritesHandler = (source, token) => Task.FromResult<MajNetOnlineDanInfo[]?>(ReferenceEquals(source, endpoint)
                    ? Array.Empty<MajNetOnlineDanInfo>()
                    : new[] { new MajNetOnlineDanInfo { Id = failedId.ToString(), Name = "Failed endpoint course", SongHashs = new[] { "remote-hidden" } } });
                Require(await SongStorage.RefreshUserOnlineFavAsync(), "All fake endpoint successes must produce a successful favorites refresh.");
                Require(!SongStorage.Collections.OfType<OnlineSongCollection>().Any(collection => ReferenceEquals(collection.Source, endpoint) && collection.Type is ChartStorageType.PlayList or ChartStorageType.Dan), "An empty successful endpoint response must remove only that endpoint's obsolete derived collections.");
                Require(ReferenceEquals(SongStorage.Collections.Single(collection => collection.Name == retained.Name), retained) && !remoteHidden.IsDisposed && SongStorage.TotalChartCount == expectedChartCount, "Endpoint refresh must not replace base online lists or dispose shared chart objects.");

                var beforeCancellation = SongStorage.Collections;
                var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var requestGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Online.FavoritesHandler = async (source, token) =>
                {
                    requestStarted.TrySetResult();
                    await requestGate.Task.WaitAsync(token).ConfigureAwait(false);
                    return responses;
                };
                using var cancellation = new CancellationTokenSource();
                var refresh = SongStorage.RefreshUserOnlineFavAsync(cancellationToken: cancellation.Token);
                await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
                cancellation.Cancel();
                await ExpectExceptionAsync<OperationCanceledException>(() => refresh);
                Require(ReferenceEquals(SongStorage.Collections, beforeCancellation) && SongStorage.TotalChartCount == expectedChartCount && !remoteVisible.IsDisposed, "Canceled endpoint refresh must leave the entire committed collection array and shared chart instances unchanged.");
                Console.WriteLine("PASS storage online: filtered origins/counts, playlist/course rebinding and endpoint replace/fail/cancel");
            }
            finally
            {
                Online.FavoritesHandler = null;
                MajEnv.ApiEndpoints = originalEndpoints;
                MajEnv.Settings.Online.Enable = false;
            }
        }
    }
}
