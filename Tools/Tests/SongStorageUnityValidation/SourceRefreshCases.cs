#nullable enable
using MajdataPlay.Collections;
using MajdataPlay.IO.Storage;
using MajdataPlay.Net;
using MajdataPlay.Utils;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace MajdataPlay.Tests
{
    /// <summary>Checks independent source replacement with the real Unity song implementations and cached local definitions.</summary>
    public static partial class SongStorageUnityValidation
    {
        /// <summary>Checks reference retention, disabled-online local refresh and online rebinding without local provider access.</summary>
        /// <param name="fixtures">The isolated run's disposable fixture root.</param>
        /// <returns>A task completing after the two independent sources and disabled-online removal are checked.</returns>
        /// <exception cref="InvalidOperationException">An independent-source assertion fails.</exception>
        private static async Task ValidateIndependentSourcesAsync(string fixtures)
        {
            var nativeChartRoot = MajEnv.ChartDirectory;
            var originalEndpoints = MajEnv.ApiEndpoints;
            var endpoint = new ApiEndpoint { Name = "Independent offline endpoint" };
            var metadata = CreateRemoteMetadata(35001);
            var playlistPath = Path.Combine(nativeChartRoot.Location, "CachedRemotePlaylist.json");
            var playlistName = "Cached remote-only local playlist";
            File.WriteAllText(playlistPath, Serializer.Json.Serialize(new DanInfo
            {
                Name = playlistName,
                SongHashs = new[] { metadata.Hash },
                IsPlayList = true
            }), new UTF8Encoding(false));
            var inaccessibleRoot = new ValidationFileSystem { RejectAccess = true };
            FileSystem.RegisterContentProvider(inaccessibleRoot);
            try
            {
                await SongStorage.RefreshLocal().ConfigureAwait(false);
                Require(!SongStorage.Collections.Any(collection => collection.Name == playlistName),
                    "An unmatched local playlist must remain unpublished until its online hash becomes available.");
                var localCollections = SongStorage.Collections.Where(collection => !collection.IsVirtual && !collection.IsOnline).ToArray();
                var localCharts = localCollections.SelectMany(collection => collection.GetOriginSnapshot()).ToArray();
                var localCount = SongStorage.TotalChartCount;
                var selectedLocal = localCollections.First(collection => !collection.IsEmpty);
                MajEnv.RuntimeConfig.List.SelectedDir = Array.IndexOf(SongStorage.Collections, selectedLocal);
                MajEnv.RuntimeConfig.List.SelectedSongIndex = 1;
                var selectedId = selectedLocal.Id;
                var selectedHash = selectedLocal[1].Hash;
                File.Delete(playlistPath);
                MajEnv.ChartDirectory = inaccessibleRoot.Root;
                MajEnv.ApiEndpoints = new[] { endpoint };
                MajEnv.Settings.Online.Enable = true;
                Online.SupplyChartList(endpoint, new[] { metadata });
                await SongStorage.RefreshOnline().ConfigureAwait(false);
                Require(localCollections.All(collection => ReferenceEquals(SongStorage.Collections.Single(current => current.Id == collection.Id), collection))
                    && SongStorage.Collections.Single(collection => collection.Name == "All").GetOriginSnapshot().Take(localCharts.Length).SequenceEqual(localCharts)
                    && SongStorage.TotalChartCount == localCount + 1,
                    "Online refresh replaced a real local base/chart or produced an incorrect aggregate total.");
                var localPlaylist = SongStorage.Collections.Single(collection => collection.Name == playlistName);
                var playlistId = localPlaylist.Id;
                var remote = SongStorage.Collections.OfType<OnlineSongCollection>().Single(collection => collection.Type == ChartStorageType.List);
                var remoteChart = remote.Single();
                Require(ReferenceEquals(localPlaylist.Single(), remoteChart) && inaccessibleRoot.AccessAttemptCount == 0
                    && inaccessibleRoot.MutationAttemptCount == 0,
                    "Online refresh must rebind the cached local definition after its JSON was deleted, with zero local provider access.");
                Require(SongStorage.WorkingCollection.Id == selectedId && SongStorage.WorkingCollection.Current.Hash == selectedHash,
                    "Online refresh lost the selected local collection or chart hash.");
                SongStorage.AddToMyFavorites(remoteChart);

                File.WriteAllText(playlistPath, Serializer.Json.Serialize(new DanInfo
                {
                    Name = playlistName,
                    SongHashs = new[] { metadata.Hash },
                    IsPlayList = true
                }), new UTF8Encoding(false));
                MajEnv.ChartDirectory = nativeChartRoot;
                MajEnv.Settings.Online.Enable = false;
                var requestsBeforeLocal = Online.ChartListRequestCount;
                await SongStorage.RefreshLocal().ConfigureAwait(false);
                Require(ReferenceEquals(SongStorage.Collections.Single(collection => collection.Id == remote.Id), remote)
                    && ReferenceEquals(SongStorage.Collections.Single(collection => collection.Id == playlistId).Single(), remoteChart)
                    && SongStorage.IsInMyFavorites(remoteChart) && Online.ChartListRequestCount == requestsBeforeLocal,
                    "Disabled online settings must not make a local refresh remove, refetch or replace online charts or their cached local playlist/favorite bindings.");

                localCollections = SongStorage.Collections.Where(collection => !collection.IsVirtual && !collection.IsOnline).ToArray();
                File.Delete(playlistPath);
                MajEnv.ChartDirectory = inaccessibleRoot.Root;
                await SongStorage.RefreshOnline().ConfigureAwait(false);
                Require(SongStorage.Collections.All(collection => !collection.IsOnline) && SongStorage.TotalChartCount == localCount
                    && localCollections.All(collection => ReferenceEquals(SongStorage.Collections.Single(current => current.Id == collection.Id), collection))
                    && !SongStorage.Collections.Any(collection => collection.Id == playlistId) && inaccessibleRoot.AccessAttemptCount == 0,
                    "Only an explicit disabled-online refresh may remove remote content, without replacing local bases or rereading deleted definitions.");
                MajEnv.Settings.Online.Enable = true;
                Online.SupplyChartList(endpoint, new[] { metadata });
                await SongStorage.RefreshOnline().ConfigureAwait(false);
                Require(SongStorage.Collections.Single(collection => collection.Id == playlistId).Single().Hash == metadata.Hash
                    && SongStorage.Collections.Single(collection => collection.Type == ChartStorageType.FavoriteList).Any(chart => chart.Hash == metadata.Hash)
                    && inaccessibleRoot.AccessAttemptCount == 0 && inaccessibleRoot.MutationAttemptCount == 0,
                    "Returning online charts must restore cached local playlist identity and favorite membership without local I/O.");
                Debug.Log("SONG_STORAGE_INDEPENDENT_SOURCES_PASSED: real local/online references, cached unmatched definitions, selection, disabled-online removal and zero local provider access/HTTP sends.");
            }
            finally
            {
                MajEnv.ChartDirectory = nativeChartRoot;
                MajEnv.ApiEndpoints = originalEndpoints;
                MajEnv.Settings.Online.Enable = false;
                await SongStorage.RefreshOnline().ConfigureAwait(false);
            }
        }
    }
}
