using Cysharp.Text;
using Cysharp.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.Net.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay.Net
{
    internal static partial class Online
    {
        /// <summary>
        /// Accesses the chart APIs of a server, <c>maichart/*</c>: the chart list and file metadata, and the votes,
        /// comments and rankings of a chart. Read responses are cached per chart and refreshed after this client changes
        /// them.
        /// </summary>
        public static class MaiChart
        {
            /// <summary>The route of the chart list.</summary>
            private const string API_GET_LIST = "maichart/list";
            /// <summary>The route of the metadata and files of several charts.</summary>
            private const string API_POST_INFO = "maichart/info";
            /// <summary>The route of the counts of a chart and the vote of the current user, formatted with the chart identifier.</summary>
            private const string API_GET_INTERACT_SUMMARY = "maichart/{0}/interactsum";
            /// <summary>The route that stores the vote of the current user on a chart, formatted with the chart identifier.</summary>
            private const string API_PUT_VOTE = "maichart/{0}/vote";
            /// <summary>The route of the comments of a chart, formatted with the chart identifier; also used to post a comment.</summary>
            private const string API_COMMENTS = "maichart/{0}/comments";
            /// <summary>The route of the rankings of a chart, formatted with its identifier; also used to send a score.</summary>
            private const string API_SCORE = "maichart/{0}/score";

            /// <summary>The number of chart identities per <c>POST maichart/info</c>; the server answers any number in two queries, batches only bound the size of one response.</summary>
            private const int INFO_BATCH_SIZE = 500;

            /// <summary>Fetches the charts of an endpoint from <c>GET maichart/list</c>.</summary>
            /// <param name="apiEndpoint">The endpoint.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>The charts, or null when the request failed.</returns>
            public static async ValueTask<MajnetSongDetail[]?> GetListAsync(ApiEndpoint apiEndpoint, CancellationToken token = default)
            {
                await UniTask.SwitchToThreadPool();
                var url = apiEndpoint.Url.Combine(API_GET_LIST);

                for (var i = 0; i < MajEnv.HTTP_REQUEST_MAX_RETRY; i++)
                {
                    var rsp = await GetAsync(url, token);
                    var e = default(Exception?);
                    if (rsp.IsSuccessfully && rsp.TryDeserialize<MajnetSongDetail[]>(out var chartList, out e) && chartList is not null)
                    {
                        return chartList;
                    }
                    else
                    {
                        MajDebug.LogError(LOG_TAG, $"[Online] Failed to get chart list: {e?.Message ?? "Unknown error"}");
                    }
                    if (rsp.ErrorCode == HttpErrorCode.Canceled)
                    {
                        break;
                    }
                    else if (!rsp.IsSuccessfully && rsp.StatusCode is not HttpStatusCode.OK)
                    {
                        break;
                    }
                }

                return null;
            }
            /// <summary>Fetches the metadata and file digests of charts from <c>POST maichart/info</c>, in batches.</summary>
            /// <param name="apiEndpoint">The endpoint serving the charts.</param>
            /// <param name="chartIds">The chart identities; blank identities are skipped.</param>
            /// <param name="token">The token canceling the requests.</param>
            /// <returns>
            /// The info of every chart the server knows, in request order; charts the server does not know are left out.
            /// Null when a batch failed, for example because the endpoint does not provide the API.
            /// </returns>
            public static async ValueTask<MajNetChartInfo[]?> GetInfosAsync(ApiEndpoint apiEndpoint, ReadOnlyMemory<string?> chartIds, CancellationToken token = default)
            {
                await UniTask.SwitchToThreadPool();
                var url = apiEndpoint.Url.Combine(API_POST_INFO);
                var infos = new List<MajNetChartInfo>(chartIds.Length);

                for (var offset = 0; offset < chartIds.Length; offset += INFO_BATCH_SIZE)
                {
                    var count = Math.Min(INFO_BATCH_SIZE, chartIds.Length - offset);
                    var json = SerializeChartIds(chartIds.Span.Slice(offset, count), out var idCount);
                    if (idCount == 0)
                    {
                        continue;
                    }
                    var batch = default(MajNetChartInfo[]);
                    for (var i = 0; i < MajEnv.HTTP_REQUEST_MAX_RETRY; i++)
                    {
                        var rsp = await PostAsync(url, CreateJsonContent(json), token);
                        var e = default(Exception?);
                        if (rsp.IsSuccessfully && rsp.TryDeserialize<MajNetChartInfo[]>(out batch, out e) && batch is not null)
                        {
                            break;
                        }
                        batch = null;
                        MajDebug.LogError(LOG_TAG, $"[Online] Failed to get chart info: {e?.Message ?? rsp.StatusCode?.ToString() ?? "Unknown error"}");
                        if (rsp.ErrorCode == HttpErrorCode.Canceled || rsp.StatusCode is not null)
                        {
                            break;
                        }
                    }
                    if (batch is null)
                    {
                        return null;
                    }
                    infos.AddRange(batch);
                }

                return infos.ToArray();
            }
            /// <summary>
            /// Fetches the counts of a chart and the vote of the current user from <c>GET maichart/{chartId}/interactsum</c>.
            /// A response fetched within <see cref="MajEnv.ONLINE_RESPONSE_CACHE_TTL_SEC"/> is reused.
            /// </summary>
            /// <param name="song">The online chart.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <param name="forceRefresh">Whether to bypass a live cached response and replace it.</param>
            /// <returns>The counts, or null when the request failed.</returns>
            public static async ValueTask<MajNetChartInteractSummary?> GetInteractSummaryAsync(OnlineSongDetail song, CancellationToken token = default, bool forceRefresh = false)
            {
                await UniTask.SwitchToThreadPool();
                var uri = BuildChartUri(song.ServerInfo, API_GET_INTERACT_SUMMARY, song.Id);
                return await GetCachedAsync(GetCachedResponse(song).InteractSummary, uri, "chart interactions", forceRefresh, token);
            }
            /// <summary>
            /// Stores the vote of the current user on a chart with <c>PUT maichart/{chartId}/vote</c> and invalidates the
            /// cached counts. The vote states the wanted result, so sending it again after a lost response is harmless.
            /// </summary>
            /// <param name="song">The online chart.</param>
            /// <param name="vote"><c>1</c> to vote the chart up, <c>-1</c> to vote it down and <c>0</c> to withdraw the vote.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>The response, deserializable to the votes of the chart when it succeeded; status 403 when the uploader disabled interactions.</returns>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="vote"/> is not <c>1</c>, <c>0</c> or <c>-1</c>.</exception>
            public static async ValueTask<EndpointResponse<MajNetVoteResult>> VoteAsync(OnlineSongDetail song, int vote, CancellationToken token = default)
            {
                if (vote is not (-1 or 0 or 1))
                {
                    throw new ArgumentOutOfRangeException(nameof(vote), vote, "A vote must be 1, 0 or -1.");
                }
                await UniTask.SwitchToThreadPool();
                var uri = BuildChartUri(song.ServerInfo, API_PUT_VOTE, song.Id);
                var json = await SerializeJsonAsync(new VoteRequest
                {
                    Value = vote
                });

                var rsp = await SendAsync(HttpMethod.Put, uri, CreateJsonContent(json), token);
                if (rsp.IsSuccessfully)
                {
                    await GetCachedResponse(song).InteractSummary.InvalidateAsync();
                }
                return new EndpointResponse<MajNetVoteResult>(rsp);
            }
            /// <summary>
            /// Fetches one page of the top-level comments of a chart, with their replies, from
            /// <c>GET maichart/{chartId}/comments</c>. A page fetched within <see cref="MajEnv.ONLINE_RESPONSE_CACHE_TTL_SEC"/>
            /// is reused.
            /// </summary>
            /// <param name="song">The online chart.</param>
            /// <param name="sort">The order of the comments.</param>
            /// <param name="page">The page number, starting at <c>1</c>.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>The page, or null when the request failed.</returns>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="sort"/> is not a defined order or <paramref name="page"/> is less than <c>1</c>.</exception>
            public static async ValueTask<MajNetCommentPage?> GetCommentsAsync(OnlineSongDetail song, CommentSort sort = CommentSort.Best, int page = 1, CancellationToken token = default)
            {
                if (page < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(page), page, "Comment pages start at 1.");
                }
                var uriBuilder = new UriBuilder(BuildChartUri(song.ServerInfo, API_COMMENTS, song.Id));
                uriBuilder.Query = $"sort={ToQueryValue(sort)}&page={page}";
                await UniTask.SwitchToThreadPool();
                var cache = GetCachedResponse(song).Comments.GetOrAdd((sort, page), _ => new CachedResponse<MajNetCommentPage>());
                return await GetCachedAsync(cache, uriBuilder.Uri, "chart comments", false, token);
            }
            /// <summary>
            /// Posts a top-level comment on a chart with <c>POST maichart/{chartId}/comments</c> and invalidates the cached
            /// counts and comments. A comment is not idempotent, so a failure is never retried.
            /// </summary>
            /// <param name="song">The online chart.</param>
            /// <param name="body">The text of the comment, 1 to 10000 characters without control characters.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>
            /// The response, deserializable to the new comment on status 201; status 403 when the uploader disabled
            /// interactions and 429 after too many comments within a minute.
            /// </returns>
            public static async ValueTask<EndpointResponse<MajNetComment>> PostCommentAsync(OnlineSongDetail song, string body, CancellationToken token = default)
            {
                await UniTask.SwitchToThreadPool();
                var uri = BuildChartUri(song.ServerInfo, API_COMMENTS, song.Id);
                var json = await SerializeJsonAsync(new CommentRequest
                {
                    Body = body
                });

                var rsp = await PostAsync(uri, CreateJsonContent(json), token);
                if (rsp.IsSuccessfully)
                {
                    var cachedResponse = GetCachedResponse(song);
                    await cachedResponse.InteractSummary.InvalidateAsync();
                    // A request still in flight stores its page in a dropped entry, so no stale page survives.
                    cachedResponse.Comments.Clear();
                }
                return new EndpointResponse<MajNetComment>(rsp);
            }
            /// <summary>
            /// Fetches the rankings of a chart from <c>GET maichart/{chartId}/score</c>. A response fetched within
            /// <see cref="MajEnv.ONLINE_RESPONSE_CACHE_TTL_SEC"/> is reused.
            /// </summary>
            /// <param name="song">The online chart.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>The rankings, or null when the request failed.</returns>
            public static async ValueTask<MajNetSongScoreInfo?> GetScoreInfoAsync(OnlineSongDetail song, CancellationToken token = default)
            {
                await UniTask.SwitchToThreadPool();
                var uri = BuildChartUri(song.ServerInfo, API_SCORE, song.Id);
                return await GetCachedAsync(GetCachedResponse(song).ScoreInfo, uri, "chart score info", false, token);
            }
            /// <summary>Sends a score of a chart to <c>POST maichart/{chartId}/score</c> and invalidates the cached rankings.</summary>
            /// <param name="song">The online chart.</param>
            /// <param name="score">The score.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>The response of the last attempt.</returns>
            public static async ValueTask<EndpointResponse> PostScoreAsync(OnlineSongDetail song, MaiScore score, CancellationToken token = default)
            {
                await UniTask.SwitchToThreadPool();
                var scoreUrl = BuildChartUri(song.ServerInfo, API_SCORE, song.Id);
                var json = await SerializeJsonAsync(score);
                var rsp = default(EndpointResponse);

                for (var i = 0; i < MajEnv.HTTP_REQUEST_MAX_RETRY; i++)
                {
                    rsp = await PostAsync(scoreUrl, CreateJsonContent(json), token);
                    if (rsp.IsSuccessfully)
                    {
                        MajDebug.LogDebug(LOG_TAG, rsp);
                        await GetCachedResponse(song).ScoreInfo.InvalidateAsync();
                        return rsp;
                    }
                    else
                    {
                        MajDebug.LogError(LOG_TAG, "[Online] Failed to post score");
                    }
                    if (!ShouldRetry(rsp))
                    {
                        break;
                    }
                }

                return rsp;
            }
            /// <summary>
            /// Returns a live cached response, or fetches it with GET and caches it; concurrent callers share one request.
            /// </summary>
            /// <typeparam name="T">The type of the response body.</typeparam>
            /// <param name="cache">The cached response.</param>
            /// <param name="uri">The requested URI.</param>
            /// <param name="description">What is fetched, for the log.</param>
            /// <param name="forceRefresh">Whether to bypass a live cached response and replace it.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>The response body, or null when every attempt failed.</returns>
            private static async ValueTask<T?> GetCachedAsync<T>(CachedResponse<T> cache, Uri uri, string description, bool forceRefresh, CancellationToken token) where T : struct
            {
                if (cache.IsAlive && !forceRefresh)
                {
                    return cache.Response;
                }
                using (await cache.RequestLock.LockAsync(token))
                {
                    if (cache.IsAlive && !forceRefresh)
                    {
                        return cache.Response;
                    }
                    for (var i = 0; i <= MajEnv.HTTP_REQUEST_MAX_RETRY; i++)
                    {
                        var e = default(Exception?);
                        var rsp = await GetAsync(uri, token);
                        if (rsp.IsSuccessfully && rsp.IsDeserializable && rsp.TryDeserialize<T?>(out var value, out e) && value is T response)
                        {
                            cache.Response = response;
                            cache.LastActive = DateTime.Now;
                            return response;
                        }
                        MajDebug.LogError(LOG_TAG, $"[Online] Failed to get {description}: {e?.Message ?? rsp.StatusCode?.ToString() ?? "Unknown error"}");
                        if (!ShouldRetry(rsp))
                        {
                            break;
                        }
                    }
                    return null;
                }
            }
            /// <summary>Gets whether a failed chart request is worth another attempt: it was not canceled and the server did not refuse it with a status code.</summary>
            /// <param name="rsp">The response that could not be used.</param>
            /// <returns>True for a timeout, an unreachable server or a successful response whose body could not be read.</returns>
            private static bool ShouldRetry(EndpointResponse rsp)
            {
                return rsp.ErrorCode != HttpErrorCode.Canceled && (rsp.IsSuccessfully || rsp.StatusCode is null);
            }
            /// <summary>Converts an order of comments to the value of the <c>sort</c> query.</summary>
            /// <param name="sort">The order.</param>
            /// <returns>The camel case name of the order.</returns>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="sort"/> is not a defined order.</exception>
            private static string ToQueryValue(CommentSort sort)
            {
                return sort switch
                {
                    CommentSort.Best => "best",
                    CommentSort.Top => "top",
                    CommentSort.New => "new",
                    CommentSort.Old => "old",
                    CommentSort.Controversial => "controversial",
                    _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, null)
                };
            }
            /// <summary>Writes chart identities as the JSON array body of <c>POST maichart/info</c>, skipping blank identities.</summary>
            /// <param name="chartIds">The chart identities of one batch.</param>
            /// <param name="idCount">Set to the number of identities written.</param>
            /// <returns>The JSON array.</returns>
            private static string SerializeChartIds(ReadOnlySpan<string?> chartIds, out int idCount)
            {
                idCount = 0;
                using var writer = new System.IO.StringWriter();
                using (var json = new JsonTextWriter(writer))
                {
                    json.WriteStartArray();
                    foreach (var id in chartIds)
                    {
                        if (string.IsNullOrWhiteSpace(id))
                        {
                            continue;
                        }
                        json.WriteValue(id);
                        idCount++;
                    }
                    json.WriteEndArray();
                }
                return writer.ToString();
            }
            /// <summary>Builds the URI of a chart API.</summary>
            /// <param name="endpoint">The endpoint serving the chart.</param>
            /// <param name="template">The route, formatted with the chart identifier.</param>
            /// <param name="chartId">The chart identifier, a GUID.</param>
            /// <returns>The URI.</returns>
            /// <exception cref="FormatException"><paramref name="chartId"/> is not a GUID.</exception>
            private static Uri BuildChartUri(ApiEndpoint endpoint, string template, string chartId)
            {
                using var sb = ZString.CreateStringBuilder(true);
                sb.AppendFormat(template, Guid.Parse(chartId));

                return new Uri(endpoint.Url, sb.ToString());
            }
        }
    }
}
