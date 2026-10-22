#nullable enable
using MajdataPlay.Collections;
using MajdataPlay.IO.Storage;
using MajdataPlay.Net;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Storage = MajdataPlay.IO.Storage.FileSystem;

namespace MajdataPlay.Tests
{
    /// <summary>Exercises the real online implementation using only pre-seeded cache files and offline DTOs.</summary>
    public static partial class SongStorageUnityValidation
    {
        /// <summary>Checks caller-created cache handles, genuine cached chart parsing, optional video absence and the storage caller.</summary>
        /// <param name="fixtures">The ignored fixture root, never the main project's cache.</param>
        /// <returns>A task completing after real online cache and caller assertions.</returns>
        /// <exception cref="InvalidOperationException">Production ignores a supplied backend or attempts networking.</exception>
        private static async Task ValidateOnlineCacheAsync(string fixtures)
        {
            MajEnv.CacheDirectory = FileSystem.Directory.Create(Path.Combine(fixtures, "OnlineCanonicalCache"));
            var endpoint = new ApiEndpoint { Name = "cached-fixture" };
            var localPath = WriteChart(Path.Combine(fixtures, "AlreadyCreatedOnlineCache"), 33000);
            File.WriteAllBytes(Path.Combine(localPath, "bg.mp4"), new byte[] { 0 });
            File.WriteAllBytes(Path.Combine(localPath, "bg.jpg"), new byte[] { 0 });
            var localDirectory = Storage.Directory.Open(localPath);
            var localSong = new OnlineSongDetail(localDirectory, endpoint, CreateRemoteMetadata(33000));
            s_fixtureCharts.Add(localSong);
            Require(ReferenceEquals(GetPrivateField<StorageDirectory>(localSong, "_cacheDirectory"), localDirectory),
                "OnlineSongDetail replaced the caller-created directory handle.");
            Require(ReferenceEquals(localSong.ServerInfo, endpoint), "OnlineSongDetail replaced its endpoint.");
            var canonicalSongPath = Path.Combine(MajEnv.CacheDirectory.Location, "Net", endpoint.Name,
                BitConverter.ToString(Convert.FromBase64String(localSong.Hash)));
            Require(!Directory.Exists(canonicalSongPath), "Direct online construction created a second canonical cache instead of using the supplied directory.");
            var localChart = await localSong.GetMaidataAsync().ConfigureAwait(false);
            Require(localChart.Title == "Fixture-33000" && localSong.IsMaidataLoaded,
                "The real online implementation did not parse pre-cached default-name maidata.");
            Require(ReferenceEquals(localChart, await localSong.GetMaidataAsync().ConfigureAwait(false)),
                "The real online implementation did not retain its in-memory chart cache.");
            Require((await localSong.GetMaidataAsync(ignoreCache: true).ConfigureAwait(false)).Title == localChart.Title,
                "The real online implementation failed to reread a valid pre-cached chart.");
            Require(await localSong.GetVideoPathAsync().ConfigureAwait(false) == Path.Combine(localPath, "bg.mp4"),
                "Online cache discovery lost the pre-cached default-name video path.");
            Require(FindOnlineCachedFile(localSong, SongStorage.SupportedTrackFileNames)?.Location == Path.Combine(localPath, "track.mp3"),
                "Online cache discovery lost the pre-cached default-name track.");
            Require(FindOnlineCachedFile(localSong, SongStorage.SupportedCoverFileNames)?.Location == Path.Combine(localPath, "bg.jpg"),
                "Online cache discovery lost the pre-cached default-name cover.");

            // All reads come from a backend that is not registered globally, with streams that cannot seek.
            Storage.RegisterContentProvider(new ValidationFileSystem());
            var backend = new ValidationFileSystem(nonSeekableReads: true);
            var fixture = SeedResourceSong(backend, backend.Root, 33001, "track.ogg", "mv.mov", "bg.png");
            var opaqueSong = new OnlineSongDetail(fixture.Directory, endpoint, CreateRemoteMetadata(fixture.Number));
            s_fixtureCharts.Add(opaqueSong);
            AssertOnlineCachedResources(opaqueSong, fixture);
            Require(backend.MutationAttemptCount == 0, "Online construction attempted to recreate the already-created provider directory.");
            var opaqueChart = await opaqueSong.GetMaidataAsync().ConfigureAwait(false);
            Require(opaqueChart.Title == fixture.Title && opaqueSong.Hash == ComputeFixtureHash(CreateChartContent(fixture.Number)),
                "Real online metadata parsing failed on pre-cached non-seekable provider bytes.");
            var reads = backend.GetReadCount(fixture.Maidata.Location);
            Require(reads > 0, "Real online metadata parsing bypassed the original backend.");
            Require(ReferenceEquals(opaqueChart, await opaqueSong.GetMaidataAsync().ConfigureAwait(false)) &&
                backend.GetReadCount(fixture.Maidata.Location) == reads,
                "The online in-memory metadata cache unexpectedly reopened the provider.");
            Require((await opaqueSong.GetMaidataAsync(ignoreCache: true).ConfigureAwait(false)).Title == fixture.Title &&
                backend.GetReadCount(fixture.Maidata.Location) > reads,
                "Forced online metadata parsing did not reread the original non-seekable backend.");
            Require(await opaqueSong.GetVideoPathAsync().ConfigureAwait(false) == fixture.VideoPath,
                "Online video cache selection lost the provider's authoritative legacy-mv path.");
            Require(backend.MutationAttemptCount == 0, "Pre-cached online reads or video path lookup attempted provider mutation.");
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var readsBeforeCancellation = backend.GetReadCount(fixture.Maidata.Location);
                await ExpectCancellationAsync(() => opaqueSong.GetMaidataAsync(ignoreCache: true, token: cancellation.Token).AsTask()).ConfigureAwait(false);
                Require(backend.GetReadCount(fixture.Maidata.Location) == readsBeforeCancellation,
                    "Pre-canceled online metadata refresh reopened the original provider.");
            }

            var absentVideoDirectory = backend.AddDirectory(backend.Root, "VideoNotAvailable");
            backend.AddFile(absentVideoDirectory, SongStorage.ChartFileName, CreateChartContent(33002));
            backend.AddFile(absentVideoDirectory, "bg.mp4.cache", string.Empty);
            // A matching directory must not override the pre-existing negative-cache marker.
            backend.AddDirectory(absentVideoDirectory, "BG.MP4");
            var absentVideoSong = new OnlineSongDetail(absentVideoDirectory, endpoint, CreateRemoteMetadata(33002));
            s_fixtureCharts.Add(absentVideoSong);
            Require((await absentVideoSong.GetMaidataAsync().ConfigureAwait(false)).Title == "Fixture-33002",
                "Online metadata parsing unexpectedly required optional cached media.");
            Require(await absentVideoSong.GetVideoPathAsync().ConfigureAwait(false) == string.Empty,
                "The cached video-not-found marker triggered a download or accepted a directory as media.");
            Require(FindOnlineCachedFile(absentVideoSong, SongStorage.SupportedTrackFileNames) is null &&
                FindOnlineCachedFile(absentVideoSong, SongStorage.SupportedCoverFileNames) is null,
                "Online discovery synthesized absent optional cache files.");
            Require(backend.MutationAttemptCount == 0, "Negative video-cache discovery attempted mutation.");
            await ValidateOnlineStorageCallerAsync().ConfigureAwait(false);
            Require(ValidationHttpHandler.RequestCount == 0, "An online cache assertion attempted a real HTTP send.");
            Debug.Log("SONG_STORAGE_ONLINE_CACHE_PASSED: real OnlineSongDetail, caller-created handle, default/variant cache names, non-seekable original-backend parsing, cancellation and existing video-not-found marker; zero HTTP sends or media decoding.");
        }

        /// <summary>Inspects the real lookup for track/video/cover handles without calling media loaders.</summary>
        /// <param name="song">The real online song retaining the supplied backend.</param>
        /// <param name="fixture">The original seeded resource expectations.</param>
        /// <exception cref="InvalidOperationException">A directory, wrong file or re-resolved backend was selected.</exception>
        private static void AssertOnlineCachedResources(OnlineSongDetail song, ResourceFixture fixture)
        {
            Require(ReferenceEquals(GetPrivateField<StorageDirectory>(song, "_cacheDirectory"), fixture.Directory),
                "The real online song lost the original cache-directory object.");
            foreach (var resource in new (string[] Names, string Location)[]
            {
                (SongStorage.SupportedTrackFileNames, fixture.TrackPath),
                (SongStorage.SupportedVideoFileNames, fixture.VideoPath),
                (SongStorage.SupportedCoverFileNames, fixture.CoverPath)
            })
            {
                var file = FindOnlineCachedFile(song, resource.Names);
                Require(file is not null && file.Location == resource.Location && ReferenceEquals(file.FileSystem, fixture.Directory.FileSystem),
                    "Online cache lookup did not preserve the expected file and original backend: " + fixture.Title);
            }
        }

        /// <summary>Invokes only the real production cached-file lookup through a narrow reflection inspection seam.</summary>
        /// <param name="song">The production online song.</param>
        /// <param name="names">The central filename array, not a copied selection algorithm.</param>
        /// <returns>The actual production-selected file, or null for absent cached resources.</returns>
        /// <exception cref="InvalidOperationException">The production lookup inspection seam is unavailable.</exception>
        private static StorageFile? FindOnlineCachedFile(OnlineSongDetail song, string[] names)
        {
            var method = typeof(OnlineSongDetail).GetMethod("FindCachedFile", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(method is not null && method.ReturnType == typeof(StorageFile), "The real online cached-file inspection seam is missing.");
            var parameters = method!.GetParameters();
            Require(parameters.Length == 1, "The online cached-file inspection seam has unexpected parameters.");
            Require(parameters[0].ParameterType.IsAssignableFrom(typeof(string[])),
                "The online cached-file inspection seam does not accept a central filename table.");
            return (StorageFile?)method.Invoke(song, new object[] { names });
        }

        /// <summary>Creates the real remote DTO with a hash matching the pre-seeded Simai bytes.</summary>
        /// <param name="number">The deterministic fixture chart identity.</param>
        /// <returns>A production DTO requiring no network response or media download.</returns>
        private static MajnetSongDetail CreateRemoteMetadata(int number)
        {
            return new MajnetSongDetail
            {
                Id = number.ToString("D24"),
                Title = "Fixture-" + number.ToString("D4"),
                Artist = "Fixture Artist",
                Designer = "Fixture Designer",
                Uploader = "Fixture Uploader",
                Description = "Offline fixture",
                Levels = new string[] { string.Empty, string.Empty, string.Empty, string.Empty, "12+", string.Empty, string.Empty },
                Hash = ComputeFixtureHash(CreateChartContent(number)),
                Timestamp = DateTime.UnixEpoch,
                Tags = new string[] { "cached" },
                PublicTags = Array.Empty<string>()
            };
        }

        /// <summary>Exercises real SongStorage per-song construction and real cached fallback with only an offline chart-list boundary.</summary>
        /// <returns>A task completing after directory naming, handle ownership and cached scanner assertions.</returns>
        /// <exception cref="InvalidOperationException">The caller omits hyphens/case or cached fallback bypasses real parsing.</exception>
        private static async Task ValidateOnlineStorageCallerAsync()
        {
            var endpoint = new ApiEndpoint { Name = "storage-caller-fixture" };
            var metadata = CreateRemoteMetadata(33003);
            metadata.Hash = Convert.ToBase64String(new byte[]
            {
                0x00, 0xAB, 0xFF, 0x10, 0x7F, 0x80, 0x01, 0xFE,
                0x02, 0xCD, 0x03, 0xEF, 0x04, 0xAA, 0x05, 0xBB
            });
            var expectedCacheName = "00-AB-FF-10-7F-80-01-FE-02-CD-03-EF-04-AA-05-BB";
            try
            {
                Online.SupplyChartList(endpoint, new MajnetSongDetail[] { metadata });
                var collection = await InvokeOnlineStorageCallerAsync(endpoint).ConfigureAwait(false);
                s_fixtureCharts.AddRange(collection.GetOriginSnapshot());
                Require(collection.IsOnline && collection.Count == 1 && collection[0] is OnlineSongDetail,
                    "The real storage caller did not construct the real online song from offline DTOs.");
                var cacheDirectory = collection.Directory;
                Require(cacheDirectory is not null, "The real storage caller did not retain its endpoint cache directory.");
                var songDirectory = GetPrivateField<StorageDirectory>(collection[0], "_cacheDirectory");
                var expectedDirectory = cacheDirectory!.FindDirectory(expectedCacheName);
                Require(expectedDirectory is not null && songDirectory.Location == expectedDirectory.Location &&
                    ReferenceEquals(songDirectory.FileSystem, cacheDirectory.FileSystem),
                    "The real storage caller did not pass its created per-song directory and backend.");
                Require(songDirectory.Entry?.Name == expectedCacheName,
                    "The cache directory must retain uppercase byte pairs separated by hyphens, not contiguous hexadecimal.");
                Require(songDirectory.Location == Path.Combine(MajEnv.CacheDirectory.Location, "Net", endpoint.Name, expectedCacheName),
                    "The real online storage caller created a per-song directory at a different cache location.");

                // Null means an explicitly supplied unavailable list, not a real remote failure.
                var maidata = songDirectory.CreateFile(SongStorage.ChartFileName);
                await maidata.WriteAllTextAsync(CreateChartContent(33004)).ConfigureAwait(false);
                var track = songDirectory.CreateFile("TRACK.WAV");
                await track.WriteAllBytesAsync(new byte[] { 0 }).ConfigureAwait(false);
                Online.SupplyChartList(endpoint, null);
                var fallback = await InvokeOnlineStorageCallerAsync(endpoint).ConfigureAwait(false);
                s_fixtureCharts.AddRange(fallback.GetOriginSnapshot());
                Require(fallback.IsOnline && fallback.Count == 1 && fallback[0] is SongDetail && fallback[0].Title == "Fixture-33004",
                    "Unavailable online-list fallback did not use the real local scanner and real MajSimai parser.");
                Require(fallback.Directory?.Location == cacheDirectory.Location,
                    "The real cached fallback changed the endpoint cache directory.");
                Require((await fallback[0].GetMaidataAsync().ConfigureAwait(false)).Title == "Fixture-33004",
                    "The real cached fallback failed to read its original metadata for full chart parsing.");
            }
            finally
            {
                Online.ClearResponseCache();
            }
            Debug.Log("SONG_STORAGE_ONLINE_CALLER_PASSED: real per-song directory creation, uppercase hyphenated hash name and original handle; unavailable-list cached fallback uses real scanner/metadata.");
        }

        /// <summary>Invokes the real private storage entry point without reproducing construction or fallback logic.</summary>
        /// <param name="endpoint">The endpoint explicitly supplied with an offline DTO response.</param>
        /// <returns>The production collection-loading task.</returns>
        /// <exception cref="InvalidOperationException">The real storage entry point cannot be inspected.</exception>
        private static Task<SongCollection> InvokeOnlineStorageCallerAsync(ApiEndpoint endpoint)
        {
            var method = typeof(SongStorage).GetMethod("GetOnlineCollection", BindingFlags.Static | BindingFlags.NonPublic);
            Require(method is not null, "The production online storage caller inspection seam is missing.");
            var result = method!.Invoke(null, new object?[] { endpoint, null, CancellationToken.None });
            Require(result is Task<SongCollection>, "The production online storage caller returned an unexpected operation type.");
            return (Task<SongCollection>)result!;
        }
    }
}
