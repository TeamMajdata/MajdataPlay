using Cysharp.Text;
using Cysharp.Threading.Tasks;
using MajdataPlay.Buffers;
using MajdataPlay.Collections;
using MajdataPlay.Diagnostics;
using MajdataPlay.Net.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay.Net
{
    internal static partial class Online
    {
        /// <summary>
        /// Accesses the chart APIs of a server, <c>maichart/*</c>: the chart list, its changes and file metadata, and the votes,
        /// comments and scores of a chart, plus the rankings of a chart from <c>maiscore/leaderboard/*</c>. Read responses are
        /// cached by request URI and dropped after this client changes them.
        /// </summary>
        public static class MaiChart
        {
            /// <summary>The route of the chart list.</summary>
            private const string API_GET_LIST = "maichart/list";
            /// <summary>The route of the metadata and files of several charts.</summary>
            private const string API_POST_INFO = "maichart/info";
            /// <summary>The route of the metadata, files and level hashes of one chart.</summary>
            private const string API_GET_INFO = "maichart/{0}/info";
            /// <summary>The route of the charts that changed within a time range.</summary>
            private const string API_GET_CHANGES = "maichart/changes";
            /// <summary>The format of the moments in the query of <c>GET maichart/changes</c>: UTC with milliseconds, the precision the server stores.</summary>
            private const string CHANGES_QUERY_TIME_FORMAT = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
            /// <summary>The route of the counts of a chart and the vote of the current user, formatted with the chart identifier.</summary>
            private const string API_GET_INTERACT_SUMMARY = "maichart/{0}/interactsum";
            /// <summary>The route that stores the vote of the current user on a chart, formatted with the chart identifier.</summary>
            private const string API_PUT_VOTE = "maichart/{0}/vote";
            /// <summary>The route of the comments of a chart, formatted with the chart identifier; also used to post a comment.</summary>
            private const string API_COMMENTS = "maichart/{0}/comments";
            /// <summary>The route of the rankings of a chart, formatted with the chart identifier.</summary>
            private const string API_GET_LEADERBOARD = "maiscore/leaderboard/chart/by-id/{0}";
            /// <summary>The route that stores a score by level hash.</summary>
            private const string API_POST_SCORE = "maiscore/level/{0}";
            /// <summary>The legacy route that stores a score by chart identifier and difficulty.</summary>
            private const string API_POST_LEGACY_SCORE = "maichart/{0}/score";

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

                for (var i = 0; i < MajEnv.HttpRequestMaxRetry; i++)
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
                using var infos = new PooledList<MajNetChartInfo>(chartIds.Length);
                var taskCount = (int)Math.Ceiling(chartIds.Length / (float)INFO_BATCH_SIZE);
                var tasks = new Task<MajNetChartInfo[]>[taskCount];
                var taskIdCounter = 0;
                var gate = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
                Parallel.For(0, taskCount, i =>
                {
                    var taskId = Interlocked.Increment(ref taskIdCounter) - 1;
                    tasks[taskId] = Task.Run(async () =>
                    {
                        gate.Wait(token);
                        try
                        {
                            var offset = taskId * INFO_BATCH_SIZE;
                            var count = Math.Min(INFO_BATCH_SIZE, chartIds.Length - offset);
                            var json = SerializeChartIds(chartIds.Span.Slice(offset, count), out var idCount);
                            if (idCount == 0)
                            {
                                return Array.Empty<MajNetChartInfo>();
                            }
                            var batch = default(MajNetChartInfo[]);
                            for (var i = 0; i < MajEnv.HttpRequestMaxRetry; i++)
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
                                return Array.Empty<MajNetChartInfo>();
                            }
                            return batch;
                        }
                        finally
                        {
                            gate.Release();
                        }
                    });
                });
                await Task.WhenAll(tasks);
                token.ThrowIfCancellationRequested();
                for (var i = 0; i < tasks.Length; i++)
                {
                    var batch = tasks[i].Result;
                    if (batch.Length == 0)
                    {
                        return null;
                    }
                    infos.AddRange(batch.AsSpan());
                }

                return infos.ToArray();
            }
            /// <summary>Fetches the metadata, files and level hashes of one chart from <c>GET maichart/{chartId}/info</c>.</summary>
            /// <param name="apiEndpoint">The endpoint serving the chart.</param>
            /// <param name="chartId">The chart identifier.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>The chart info, or null when the endpoint does not provide the chart or the request failed.</returns>
            public static async ValueTask<MajNetChartInfo?> GetInfoAsync(ApiEndpoint apiEndpoint, string chartId, CancellationToken token = default)
            {
                await UniTask.SwitchToThreadPool();
                var uri = BuildChartUri(apiEndpoint, API_GET_INFO, chartId);
                for (var i = 0; i < MajEnv.HttpRequestMaxRetry; i++)
                {
                    var rsp = await GetAsync(uri, token);
                    var exception = default(Exception?);
                    if (rsp.IsSuccessfully && rsp.TryDeserialize<MajNetChartInfo>(out var info, out exception) && info is not null)
                    {
                        return info;
                    }
                    MajDebug.LogError(LOG_TAG, $"[Online] Failed to get chart info: {exception?.Message ?? rsp.StatusCode?.ToString() ?? "Unknown error"}");
                    if (!ShouldRetry(rsp))
                    {
                        break;
                    }
                }
                return null;
            }
            /// <summary>
            /// Fetches the charts that were uploaded, modified or deleted within a time range from
            /// <c>GET maichart/changes</c>, so a cached chart list is updated without reading the whole list again.
            /// </summary>
            /// <param name="apiEndpoint">The endpoint serving the charts.</param>
            /// <param name="from">
            /// The start of the range, inclusive, normally the <see cref="MajNetChartChanges.To"/> of the previous query; a
            /// moment of unspecified kind is read as UTC, as the server does.
            /// </param>
            /// <param name="to">The end of the range, exclusive, or null for the current time of the server.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>
            /// The response of the last attempt, deserializable to the changes when it succeeded. A server without the API
            /// answers 404, and one whose current time is earlier than <paramref name="from"/> answers 400; an unreachable
            /// server gives no status code.
            /// </returns>
            public static async ValueTask<EndpointResponse<MajNetChartChanges>> GetChangesAsync(ApiEndpoint apiEndpoint, DateTime from, DateTime? to = null, CancellationToken token = default)
            {
                await UniTask.SwitchToThreadPool();
                var uriBuilder = new UriBuilder(apiEndpoint.Url.Combine(API_GET_CHANGES));
                uriBuilder.Query = to is { } end
                    ? $"from={FormatChangesQueryTime(from)}&to={FormatChangesQueryTime(end)}"
                    : $"from={FormatChangesQueryTime(from)}";
                var uri = uriBuilder.Uri;
                var rsp = default(EndpointResponse);

                for (var i = 0; i < MajEnv.HttpRequestMaxRetry; i++)
                {
                    rsp = await GetAsync(uri, token);
                    var e = default(Exception?);
                    if (rsp.IsSuccessfully && rsp.TryDeserialize<MajNetChartChanges>(out var changes, out e) && changes is not null)
                    {
                        break;
                    }
                    MajDebug.LogError(LOG_TAG, $"[Online] Failed to get chart changes: {e?.Message ?? rsp.StatusCode?.ToString() ?? "Unknown error"}");
                    if (!ShouldRetry(rsp))
                    {
                        break;
                    }
                }

                return new EndpointResponse<MajNetChartChanges>(rsp);
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
                return await GetCachedAsync(s_interactSummaries, uri, "chart interactions", forceRefresh, token);
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
                    s_interactSummaries.Remove(BuildChartUri(song.ServerInfo, API_GET_INTERACT_SUMMARY, song.Id));
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
                return await GetCachedAsync(s_commentPages, uriBuilder.Uri, "chart comments", false, token);
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
                    s_interactSummaries.Remove(BuildChartUri(song.ServerInfo, API_GET_INTERACT_SUMMARY, song.Id));
                    // The pages of every order share the path of the comments and differ only in the query.
                    var comments = uri.GetLeftPart(UriPartial.Path);
                    s_commentPages.RemoveWhere(pageUri => pageUri.GetLeftPart(UriPartial.Path) == comments);
                }
                return new EndpointResponse<MajNetComment>(rsp);
            }
            /// <summary>
            /// Fetches the rankings of a chart from <c>GET maiscore/leaderboard/chart/by-id/{chartId}</c>. A response fetched within
            /// <see cref="MajEnv.ONLINE_RESPONSE_CACHE_TTL_SEC"/> is reused.
            /// </summary>
            /// <param name="song">The online chart.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>The rankings, or null when the request failed.</returns>
            public static async ValueTask<MajNetSongScoreInfo?> GetScoreInfoAsync(OnlineSongDetail song, CancellationToken token = default)
            {
                await UniTask.SwitchToThreadPool();
                var uri = BuildChartUri(song.ServerInfo, API_GET_LEADERBOARD, song.Id);
                return await GetCachedAsync(s_scoreInfos, uri, "chart score info", false, token);
            }
            /// <summary>Sends a score by level hash, falling back to the legacy chart route only when the new route is unsupported.</summary>
            /// <param name="song">The online chart.</param>
            /// <param name="score">The online submission and its level routing information.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>The response of the last attempt.</returns>
            public static async ValueTask<EndpointResponse> PostScoreAsync(OnlineSongDetail song, OnlineMaiScore score, CancellationToken token = default)
            {
                await UniTask.SwitchToThreadPool();
                var rsp = default(EndpointResponse);
                if (!string.IsNullOrEmpty(score.LevelHash))
                {
                    var uri = BuildLevelScoreUri(song.ServerInfo, score.LevelHash);
                    var json = await SerializeJsonAsync(score);
                    rsp = await SendScoreAsync(uri, json, token);
                    if (!IsScoreRouteUnsupported(rsp))
                    {
                        InvalidateScoreInfo(song, rsp);
                        return rsp;
                    }
                }
                if (token.IsCancellationRequested)
                {
                    return CreateCanceledResponse(HttpMethod.Post, BuildChartUri(song.ServerInfo, API_POST_LEGACY_SCORE, song.Id), token);
                }

                MajDebug.LogWarning(LOG_TAG, $"[Online] Submitting score through the legacy API at {song.ServerInfo.Url}: the level hash or level score route is unavailable.");
                var legacyScore = new OnlineLegacyMaiScore
                {
                    Hash = song.Hash,
                    ChartLevel = score.ChartLevel,
                    Acc = score.Acc,
                    DXScore = score.DXScore,
                    ComboState = score.ComboState
                };
                var legacyUri = BuildChartUri(song.ServerInfo, API_POST_LEGACY_SCORE, song.Id);
                var legacyJson = await SerializeJsonAsync(legacyScore);
                rsp = await SendScoreAsync(legacyUri, legacyJson, token);
                InvalidateScoreInfo(song, rsp);
                return rsp;
            }
            /// <summary>Sends a serialized score, retrying only unanswered requests.</summary>
            /// <param name="uri">The submission route.</param>
            /// <param name="json">The serialized online submission.</param>
            /// <param name="token">The token canceling the submission.</param>
            /// <returns>The response of the last attempt.</returns>
            private static async ValueTask<EndpointResponse> SendScoreAsync(Uri uri, string json, CancellationToken token)
            {
                var rsp = default(EndpointResponse);

                for (var i = 0; i < MajEnv.HttpRequestMaxRetry; i++)
                {
                    rsp = await PostAsync(uri, CreateJsonContent(json), token);
                    if (rsp.IsSuccessfully)
                    {
                        MajDebug.LogDebug(LOG_TAG, rsp);
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
            /// <summary>Determines whether a level score response selects the legacy compatibility route.</summary>
            /// <param name="rsp">The response of the level score route.</param>
            /// <returns>True when the legacy route should be tried; false for other responses.</returns>
            private static bool IsScoreRouteUnsupported(EndpointResponse rsp)
            {
                if (rsp.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented)
                {
                    return true;
                }
                if (rsp.StatusCode is not HttpStatusCode.NotFound)
                {
                    return false;
                }
                try
                {
                    // A missing code or NoSuchItem selects the compatibility route.
                    var body = JObject.Parse(Encoding.UTF8.GetString(rsp.AsSpan()));
                    var returnCode = body["code"];
                    return returnCode is null || returnCode.ToString() == ((int)ApiReturnCode.ApiNotSupported).ToString(CultureInfo.InvariantCulture);
                }
                catch (JsonException)
                {
                    return true;
                }
            }
            /// <summary>Drops the chart rankings after a score was accepted.</summary>
            /// <param name="song">The chart on which the play was counted.</param>
            /// <param name="rsp">The submission response.</param>
            private static void InvalidateScoreInfo(OnlineSongDetail song, EndpointResponse rsp)
            {
                if (rsp.IsSuccessfully)
                {
                    s_scoreInfos.Remove(BuildChartUri(song.ServerInfo, API_GET_LEADERBOARD, song.Id));
                }
            }
            /// <summary>
            /// Returns a live cached response, or fetches it with GET and caches it. Callers asking for the same URI while it
            /// is fetched share that request, and a failed request is not cached.
            /// </summary>
            /// <typeparam name="T">The type of the response body.</typeparam>
            /// <param name="cache">The cache of the responses of the API, by request URI.</param>
            /// <param name="uri">The requested URI.</param>
            /// <param name="description">What is fetched, for the log.</param>
            /// <param name="forceRefresh">Whether to bypass a live cached response and replace it.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>The response body, or null when every attempt failed.</returns>
            /// <exception cref="OperationCanceledException"><paramref name="token"/> was canceled while waiting for another caller's request.</exception>
            private static async ValueTask<T?> GetCachedAsync<T>(TtlCache<Uri, T> cache, Uri uri, string description, bool forceRefresh, CancellationToken token) where T : struct
            {
                Func<Uri, CancellationToken, ValueTask<T>> fetch = (requestUri, requestToken) => FetchAsync<T>(requestUri, description, requestToken);
                try
                {
                    return forceRefresh
                        ? await cache.RefreshAsync(uri, fetch, token)
                        : await cache.GetOrAddAsync(uri, fetch, token);
                }
                catch (HttpException)
                {
                    // FetchAsync logged every failed attempt.
                    return null;
                }
            }
            /// <summary>Reads a response body with GET, retrying the failures worth another attempt.</summary>
            /// <typeparam name="T">The type of the response body.</typeparam>
            /// <param name="uri">The requested URI.</param>
            /// <param name="description">What is fetched, for the log.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>A task returning the response body.</returns>
            /// <exception cref="HttpException">Every attempt failed; the exception describes the last one.</exception>
            private static async ValueTask<T> FetchAsync<T>(Uri uri, string description, CancellationToken token) where T : struct
            {
                var rsp = default(EndpointResponse);
                for (var i = 0; i <= MajEnv.HttpRequestMaxRetry; i++)
                {
                    var e = default(Exception?);
                    rsp = await GetAsync(uri, token);
                    if (rsp.IsSuccessfully && rsp.IsDeserializable && rsp.TryDeserialize<T?>(out var value, out e) && value is T response)
                    {
                        return response;
                    }
                    MajDebug.LogError(LOG_TAG, $"[Online] Failed to get {description}: {e?.Message ?? rsp.StatusCode?.ToString() ?? "Unknown error"}");
                    if (!ShouldRetry(rsp))
                    {
                        break;
                    }
                }
                throw new HttpException(uri.ToString(), rsp.ErrorCode, rsp.StatusCode, $"Failed to get {description}");
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
            /// <summary>Formats a moment for the query of <c>GET maichart/changes</c>.</summary>
            /// <param name="moment">The moment; one of unspecified kind is taken to be UTC already.</param>
            /// <returns>The moment in UTC as ISO 8601 with milliseconds, which needs no escaping in a query.</returns>
            private static string FormatChangesQueryTime(DateTime moment)
            {
                var utc = moment.Kind == DateTimeKind.Local ? moment.ToUniversalTime() : moment;
                return utc.ToString(CHANGES_QUERY_TIME_FORMAT, CultureInfo.InvariantCulture);
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
            /// <summary>Builds the submission URI from the level's hexadecimal hash.</summary>
            /// <param name="endpoint">The endpoint accepting the score.</param>
            /// <param name="levelHash">The level hash used as one path segment.</param>
            /// <returns>The URI of the level score submission.</returns>
            private static Uri BuildLevelScoreUri(ApiEndpoint endpoint, string levelHash)
            {
                using var sb = ZString.CreateStringBuilder(true);
                sb.AppendFormat(API_POST_SCORE, Uri.EscapeDataString(levelHash));

                return new Uri(endpoint.Url, sb.ToString());
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
