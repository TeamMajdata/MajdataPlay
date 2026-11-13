using Cysharp.Threading.Tasks;
using MajdataPlay.Collections;
using MajdataPlay.Diagnostics;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
#nullable enable
namespace MajdataPlay.Net
{
    /// <summary>
    /// Reports the recommended charts the player has seen in the List scene. Only the song in the center of the
    /// list counts, by the rules of <see cref="ImpressionTracker"/>. There is one tracker per endpoint, and it
    /// starts a new batch whenever the endpoint's feed is fetched again. Main thread only.
    /// </summary>
    internal static class RecommendImpressions
    {
        // Same as the website: the server only attributes conversions to impressions it has already recorded.
        // Starting a game sends at once anyway.
        private const int FlushDelayMs = 1000;

        private static readonly Dictionary<ApiEndpoint, TrackerEntry> s_entries = new();
        private static TrackerEntry? s_focused;
        // The shared HTTP handler is disposed on quit
        private static bool s_isQuitting = false;

        static RecommendImpressions()
        {
            GameManager.OnAppQuit += (_, _) => s_isQuitting = true;
        }
        /// <summary>
        /// The song has settled in the center of the list
        /// </summary>
        public static void Focus(SongCollection collection, ISongDetail? song)
        {
            var entry = GetEntry(collection, song);
            if (entry != s_focused)
            {
                s_focused?.Tracker.Blur();
                s_focused = entry;
            }
            entry?.Tracker.Focus(song!.Hash);
        }
        /// <summary>
        /// The centered song is leaving: the list scrolls, the collection changes or the scene ends
        /// </summary>
        public static void Blur()
        {
            s_focused?.Tracker.Blur();
            s_focused = null;
        }
        /// <summary>
        /// The player starts the song. It counts without dwell and is sent at once, or once
        /// <paramref name="sendAfter"/> completes.
        /// </summary>
        /// <param name="sendAfter">
        /// The refresh of stale feeds started for this game. The server only marks charts served in the last
        /// 30 minutes, so an impression racing the refetch of an expired feed would be dropped.
        /// </param>
        public static void Commit(SongCollection collection, ISongDetail? song, Task? sendAfter = null)
        {
            Blur();
            var entry = GetEntry(collection, song);
            if (entry is null)
            {
                return;
            }
            entry.SendAfter = sendAfter;
            try
            {
                entry.Tracker.Commit(song!.Hash);
            }
            finally
            {
                entry.SendAfter = null;
            }
        }
        /// <summary>
        /// The app went to the background and may not come back
        /// </summary>
        public static void Suspend()
        {
            s_focused?.Tracker.Suspend();
            FlushAll();
        }
        public static void Resume()
        {
            s_focused?.Tracker.Resume();
        }
        public static void FlushAll()
        {
            foreach (var entry in s_entries.Values)
            {
                entry.Tracker.Flush();
            }
        }
        private static TrackerEntry? GetEntry(SongCollection collection, ISongDetail? song)
        {
            if (song is null || collection is not OnlineSongCollection { Type: ChartStorageType.Recommend } recommendCollection)
            {
                return null;
            }
            var endpoint = recommendCollection.Source;
            var generation = SongStorage.GetRecommendGeneration(endpoint);
            if (!s_entries.TryGetValue(endpoint, out var entry))
            {
                entry = new TrackerEntry(endpoint, generation);
                s_entries[endpoint] = entry;
            }
            else if (entry.Generation != generation)
            {
                // The feed was fetched again, so the server holds new served records to mark
                entry.Tracker.Reset();
                entry.Generation = generation;
            }
            return entry;
        }
        private static async UniTaskVoid SendAsync(ApiEndpoint endpoint, string[] chartHashes, Task? sendAfter)
        {
            try
            {
                if (sendAfter is not null)
                {
                    try
                    {
                        await sendAfter;
                    }
                    catch (Exception e)
                    {
                        // A failed refresh leaves the old served records; send anyway
                        MajDebug.LogException(e);
                    }
                }
                await Online.Recommend.PostImpressionsAsync(endpoint, chartHashes);
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
            }
        }
        private sealed class TrackerEntry
        {
            public ImpressionTracker Tracker { get; }
            public int Generation { get; set; }
            /// <summary>
            /// Set by <see cref="Commit"/> for the batch it sends
            /// </summary>
            public Task? SendAfter { get; set; }

            private readonly ApiEndpoint _endpoint;

            public TrackerEntry(ApiEndpoint endpoint, int generation)
            {
                _endpoint = endpoint;
                Generation = generation;
                Tracker = new ImpressionTracker(() => Time.realtimeSinceStartupAsDouble, Send, () => FlushLaterAsync().Forget());
            }
            private void Send(string[] chartHashes)
            {
                // Impressions belong to the logged-in user; after logout the server would reject them anyway
                if (s_isQuitting || !_endpoint.RuntimeConfig.IsLoggedIn)
                {
                    return;
                }
                SendAsync(_endpoint, chartHashes, SendAfter).Forget();
            }
            private async UniTaskVoid FlushLaterAsync()
            {
                await UniTask.Delay(FlushDelayMs, DelayType.Realtime);
                Tracker.Flush();
            }
        }
    }
}
