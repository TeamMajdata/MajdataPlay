using Cysharp.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.Net.Models;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay.Net
{
    internal static partial class Online
    {
        /// <summary>
        /// Accesses the recommendation APIs of a server, <c>recommend/*</c>: the personal chart feed of the logged-in user
        /// and the report of the recommended charts the player has seen. Both use <see cref="MajEnv.BackgroundHttpClient"/>,
        /// so leaving the List scene does not cancel them.
        /// </summary>
        public static class Recommend
        {
            /// <summary>The route of one page of the feed, formatted with the source, the page and the page size.</summary>
            private const string API_GET_FEED = "recommend/feed?source={0}&page={1}&pageSize={2}";
            /// <summary>The route that reports seen charts.</summary>
            private const string API_POST_IMPRESSIONS = "recommend/impressions";

            /// <summary>The feed of MajdataPlay; the same value as <c>ImpressionSources.PlayFeed</c> on the server, which keeps its impressions apart from those of the website.</summary>
            private const string SOURCE = "play-feed";

            /// <summary>
            /// Fetches one page of the personal recommendations of the logged-in user from <c>GET recommend/feed</c>. The
            /// server pages a per-user snapshot, so pages are stable for its lifetime and a page shorter than
            /// <paramref name="pageSize"/> is the last one. No request is sent while the user is not logged in.
            /// </summary>
            /// <param name="apiEndpoint">The endpoint.</param>
            /// <param name="page">The page number.</param>
            /// <param name="pageSize">The number of charts per page.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>The charts of the page, or null when the user is not logged in or the request failed.</returns>
            public static async ValueTask<MajnetSongDetail[]?> GetFeedAsync(ApiEndpoint apiEndpoint, int page, int pageSize, CancellationToken token = default)
            {
                await UniTask.SwitchToThreadPool();
                var statistic = GetApiEndpointStatistic(apiEndpoint);
                if (statistic.IsUserLoggedIn is not true)
                {
                    return null;
                }
                var url = apiEndpoint.Url.Combine(string.Format(API_GET_FEED, SOURCE, page, pageSize));

                for (var i = 0; i <= MajEnv.HTTP_REQUEST_MAX_RETRY; i++)
                {
                    var rsp = await GetAsync(url, token, MajEnv.BackgroundHttpClient);
                    var e = default(Exception?);
                    if (rsp.IsSuccessfully && rsp.TryDeserialize<MajnetSongDetail[]>(out var charts, out e) && charts is not null)
                    {
                        return charts;
                    }
                    else if (rsp.ErrorCode == HttpErrorCode.Canceled)
                    {
                        break;
                    }
                    else if (rsp.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        MajDebug.LogError(LOG_TAG, "[Online] Failed to get recommend feed: user not logged in");
                        using (await statistic.LockAsync(token))
                        {
                            statistic.IsUserLoggedIn = false;
                        }
                        break;
                    }
                    else if (rsp.StatusCode is not null)
                    {
                        MajDebug.LogError(LOG_TAG, $"[Online] Failed to get recommend feed: {e?.Message ?? rsp.StatusCode.ToString()}");
                        break;
                    }
                }

                return null;
            }
            /// <summary>
            /// Reports recommended charts the player has seen to <c>POST recommend/impressions</c>. Statistics only:
            /// sent once and dropped on failure.
            /// </summary>
            /// <param name="apiEndpoint">The endpoint that recommended the charts.</param>
            /// <param name="chartHashes">The hashes of the charts that were seen.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>The response.</returns>
            public static async ValueTask<EndpointResponse> PostImpressionsAsync(ApiEndpoint apiEndpoint, string[] chartHashes, CancellationToken token = default)
            {
                await UniTask.SwitchToThreadPool();
                var uri = apiEndpoint.Url.Combine(API_POST_IMPRESSIONS);
                var report = new RecommendImpressionReport
                {
                    Source = SOURCE,
                    ChartHashes = chartHashes
                };
                var json = await SerializeJsonAsync(report);

                return await PostAsync(uri, CreateJsonContent(json), token, MajEnv.BackgroundHttpClient);
            }
        }
    }
}
