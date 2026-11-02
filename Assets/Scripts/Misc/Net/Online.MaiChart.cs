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
        /// Accesses the chart APIs of a server, <c>maichart/*</c>: the chart list and file metadata, and the
        /// interactions and rankings of a chart. Interactions and rankings are cached per chart and refreshed after this
        /// client changes them.
        /// </summary>
        public static class MaiChart
        {
            /// <summary>The route of the chart list.</summary>
            private const string API_GET_LIST = "maichart/list";
            /// <summary>The route of the metadata and files of several charts.</summary>
            private const string API_POST_INFO = "maichart/info";
            /// <summary>The route of the interactions of a chart, formatted with its identifier; also used to send interactions.</summary>
            private const string API_INTERACT = "maichart/{0}/interact";
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
            /// Fetches the votes, plays and comments of a chart from <c>GET maichart/{chartId}/interact</c>. A response
            /// fetched within <see cref="MajEnv.ONLINE_RESPONSE_CACHE_TTL_SEC"/> is reused.
            /// </summary>
            /// <param name="song">The online chart.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <param name="forceRefresh">Whether to bypass a live cached response and replace it.</param>
            /// <returns>The interactions, or null when the request failed.</returns>
            public static async ValueTask<MajNetSongInteract?> GetInteractAsync(OnlineSongDetail song, CancellationToken token = default, bool forceRefresh = false)
            {
                await UniTask.SwitchToThreadPool();
                var cachedInteract = GetCachedResponse(song).Interact;
                if (cachedInteract.IsAlive && !forceRefresh)
                {
                    return cachedInteract.Response;
                }
                using (await cachedInteract.RequestLock.LockAsync(token))
                {
                    if (cachedInteract.IsAlive && !forceRefresh)
                    {
                        return cachedInteract.Response;
                    }
                    var interactUrl = BuildChartUri(song.ServerInfo, API_INTERACT, song.Id);

                    for (var i = 0; i <= MajEnv.HTTP_REQUEST_MAX_RETRY; i++)
                    {
                        var e = default(Exception?);
                        var rsp = await GetAsync(interactUrl, token);
                        if (rsp.IsSuccessfully && rsp.IsDeserializable && rsp.TryDeserialize<MajNetSongInteract?>(out var interact, out e) && interact is MajNetSongInteract interactRsp)
                        {
                            MajDebug.LogDebug(LOG_TAG, rsp);
                            cachedInteract.Response = interactRsp;
                            cachedInteract.LastActive = DateTime.Now;
                            return interactRsp;
                        }
                        else
                        {
                            MajDebug.LogError(LOG_TAG, rsp);
                            MajDebug.LogError(LOG_TAG, $"[Online] Failed to get chart interact: {e?.Message ?? "Unknown error"}");
                        }
                        if (!ShouldRetry(rsp))
                        {
                            break;
                        }
                    }
                    return null;
                }
            }
            /// <summary>Votes a chart up with <c>POST maichart/{chartId}/interact</c>.</summary>
            /// <param name="song">The online chart.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>The response.</returns>
            public static ValueTask<EndpointResponse> PostLikeAsync(OnlineSongDetail song, CancellationToken token = default)
            {
                return SendInteractionAsync(song, HttpMethod.Post, "like", "like", token);
            }
            /// <summary>Votes a chart down with <c>POST maichart/{chartId}/interact</c>.</summary>
            /// <param name="song">The online chart.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>The response.</returns>
            public static ValueTask<EndpointResponse> PostDislikeAsync(OnlineSongDetail song, CancellationToken token = default)
            {
                return SendInteractionAsync(song, HttpMethod.Post, "dislike", "dislike", token);
            }
            /// <summary>Comments on a chart with <c>POST maichart/{chartId}/interact</c>; the comment is sent once, never retried.</summary>
            /// <param name="song">The online chart.</param>
            /// <param name="comment">The text of the comment.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>The response.</returns>
            public static ValueTask<EndpointResponse> PostCommentAsync(OnlineSongDetail song, string comment, CancellationToken token = default)
            {
                return SendInteractionAsync(song, HttpMethod.Post, "comment", comment, token);
            }
            /// <summary>Withdraws the upvote of a chart with <c>DELETE maichart/{chartId}/interact</c>; a downvote is kept.</summary>
            /// <param name="song">The online chart.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>The response.</returns>
            public static ValueTask<EndpointResponse> DeleteLikeAsync(OnlineSongDetail song, CancellationToken token = default)
            {
                return SendInteractionAsync(song, HttpMethod.Delete, "like", string.Empty, token);
            }
            /// <summary>Withdraws the downvote of a chart with <c>DELETE maichart/{chartId}/interact</c>; an upvote is kept.</summary>
            /// <param name="song">The online chart.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>The response.</returns>
            public static ValueTask<EndpointResponse> DeleteDislikeAsync(OnlineSongDetail song, CancellationToken token = default)
            {
                return SendInteractionAsync(song, HttpMethod.Delete, "dislike", string.Empty, token);
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
                var cachedScoreInfo = GetCachedResponse(song).ScoreInfo;
                if (cachedScoreInfo.IsAlive)
                {
                    return cachedScoreInfo.Response;
                }
                using (await cachedScoreInfo.RequestLock.LockAsync(token))
                {
                    if (cachedScoreInfo.IsAlive)
                    {
                        return cachedScoreInfo.Response;
                    }
                    var scoreUrl = BuildChartUri(song.ServerInfo, API_SCORE, song.Id);

                    for (var i = 0; i <= MajEnv.HTTP_REQUEST_MAX_RETRY; i++)
                    {
                        var e = default(Exception?);
                        var rsp = await GetAsync(scoreUrl, token);
                        if (rsp.IsSuccessfully && rsp.IsDeserializable && rsp.TryDeserialize<MajNetSongScoreInfo?>(out var scoreInfo, out e) && scoreInfo is MajNetSongScoreInfo scoreInfoRsp)
                        {
                            cachedScoreInfo.Response = scoreInfoRsp;
                            cachedScoreInfo.LastActive = DateTime.Now;
                            return scoreInfoRsp;
                        }
                        else
                        {
                            MajDebug.LogError(LOG_TAG, $"[Online] Failed to get chart score info: {e?.Message ?? "Unknown error"}");
                        }
                        if (!ShouldRetry(rsp))
                        {
                            break;
                        }
                    }
                    return null;
                }
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
            /// Sends an interaction to <c>maichart/{chartId}/interact</c> as a form and invalidates the cached interactions
            /// when it succeeds. Interactions are not idempotent, so a failure is never retried.
            /// </summary>
            /// <param name="song">The online chart.</param>
            /// <param name="method">POST to add the interaction, DELETE to withdraw it.</param>
            /// <param name="type">The <c>type</c> field: <c>like</c>, <c>dislike</c> or <c>comment</c>.</param>
            /// <param name="content">The <c>content</c> field.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>The response.</returns>
            private static async ValueTask<EndpointResponse> SendInteractionAsync(OnlineSongDetail song, HttpMethod method, string type, string content, CancellationToken token)
            {
                await UniTask.SwitchToThreadPool();
                var uri = BuildChartUri(song.ServerInfo, API_INTERACT, song.Id);
                // SendAsync's HttpRequestMessage owns and disposes the form, including on failure.
                // Disposing it again here throws in Unity's MultipartContent implementation on iOS.
                var formData = new MultipartFormDataContent
                {
                    { new StringContent(type), "type" },
                    { new StringContent(content), "content" },
                };
                // Comments are not idempotent; a lost response must not trigger an automatic repost.
                var response = await SendAsync(method, uri, formData, token);
                if (response.IsSuccessfully)
                {
                    await GetCachedResponse(song).Interact.InvalidateAsync();
                }
                return response;
            }
            /// <summary>Gets whether a failed chart request is worth another attempt: it was not canceled and the server did not refuse it with a status code.</summary>
            /// <param name="rsp">The response that could not be used.</param>
            /// <returns>True for a timeout, an unreachable server or a successful response whose body could not be read.</returns>
            private static bool ShouldRetry(EndpointResponse rsp)
            {
                return rsp.ErrorCode != HttpErrorCode.Canceled && (rsp.IsSuccessfully || rsp.StatusCode is null);
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
