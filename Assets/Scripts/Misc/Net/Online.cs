using Cysharp.Threading.Tasks;
using MajdataPlay.Buffers;
using MajdataPlay.Collections;
using MajdataPlay.Diagnostics;
using MajdataPlay.Net.Models;
using MajdataPlay.Numerics;
using MajdataPlay.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay.Net
{
    /// <summary>
    /// Accesses the APIs of the online servers. The APIs are grouped like the routes of the server:
    /// <see cref="Account"/> for <c>account/*</c> and <c>avatar/*</c>, <see cref="Machine"/> for <c>machine/*</c>,
    /// <see cref="MaiChart"/> for <c>maichart/*</c> and <see cref="Recommend"/> for <c>recommend/*</c>.
    /// The login and machine registration state of every endpoint and the cached responses are shared by the groups.
    /// </summary>
    internal static partial class Online
    {
        /// <summary>The tag of the log messages of the online APIs.</summary>
        private const string LOG_TAG = "Network";

        /// <summary>The route of the server identity.</summary>
        private const string API_GET_SERVER_INFO = "info";

        // Declared before the serializer: static initializers run in textual order.
        /// <summary>The JSON settings of request and response bodies: camel case names, replacing default collections.</summary>
        private static readonly JsonSerializerSettings _defaultJsonSerializerSettings = new()
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            },
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };
        /// <summary>The JSON serializer created from <see cref="_defaultJsonSerializerSettings"/>.</summary>
        private static readonly JsonSerializer _defaultJsonSerializer = JsonSerializer.Create(_defaultJsonSerializerSettings);

        /// <summary>Guards <see cref="_endpointStatistics"/>.</summary>
        private static SpinLock _dictLock = new();

        /// <summary>The login and machine registration state of every endpoint that was accessed.</summary>
        private static readonly Dictionary<ApiEndpoint, ApiEndpointStatistics> _endpointStatistics = new();

        // Declared before the caches: static initializers run in textual order.
        /// <summary>How long a response read from a server is reused.</summary>
        private static readonly TimeSpan s_responseTimeToLive = TimeSpan.FromSeconds(MajEnv.ONLINE_RESPONSE_CACHE_TTL_SEC);
        /// <summary>The responses of <c>GET maichart/{chartId}/interactsum</c>, by request URI.</summary>
        private static readonly TtlCache<Uri, MajNetChartInteractSummary> s_interactSummaries = new(s_responseTimeToLive);
        /// <summary>The responses of <c>GET maichart/{chartId}/comments</c>, by request URI, which holds the order and the page.</summary>
        private static readonly TtlCache<Uri, MajNetCommentPage> s_commentPages = new(s_responseTimeToLive);
        /// <summary>The responses of <c>GET maichart/{chartId}/score</c>, by request URI.</summary>
        private static readonly TtlCache<Uri, MajNetSongScoreInfo> s_scoreInfos = new(s_responseTimeToLive);

        /// <summary>
        /// Revalidates the machine registration and the login of every endpoint that reports them, so a session that expired
        /// on the server is no longer treated as alive.
        /// </summary>
        /// <param name="token">The token canceling the requests.</param>
        /// <returns>A task that completes when every endpoint was checked.</returns>
        public static async ValueTask HeartbeatAsync(CancellationToken token = default)
        {
            using var rentedBuffer = new PooledList<ApiEndpointStatistics>();
            await UniTask.SwitchToThreadPool();
            MajDebug.LogDebug(LOG_TAG, "[Online] Heartbeat executing");
            GetAllApiEndpointStatistic(rentedBuffer);
            foreach (var statistics in rentedBuffer)
            {
                if (statistics.IsMachineRegistered is true)
                {
                    try
                    {
                        using (await statistics.LockAsync(token))
                        {
                            var isAlive = await Machine.CheckRegistrationAsync(statistics.Endpoint, token);
                            statistics.IsMachineRegistered = isAlive;
                        }
                    }
                    catch (Exception e)
                    {
                        MajDebug.LogException(LOG_TAG, e);
                    }
                }
                if (statistics.IsUserLoggedIn is true)
                {
                    try
                    {
                        using (await statistics.LockAsync(token))
                        {
                            var rsp = await Account.RequestProfileAsync(statistics.Endpoint, token);
                            var isAlive = rsp.StatusCode is not HttpStatusCode.Unauthorized;
                            statistics.IsUserLoggedIn = isAlive;
                        }
                    }
                    catch (Exception e)
                    {
                        MajDebug.LogException(LOG_TAG, e);
                    }
                }
            }
            MajDebug.LogDebug(LOG_TAG, "[Online] Heartbeat has been completed");
        }
        /// <summary>Fetches the identity of the server behind an endpoint from <c>GET info</c>.</summary>
        /// <param name="apiEndpoint">The endpoint, which may be one of several addresses of the server, such as a reverse proxy.</param>
        /// <param name="token">The token canceling the request.</param>
        /// <returns>The server identity, or null when the request failed, for example because the server does not provide the API.</returns>
        public static async ValueTask<MajNetServerInfo?> GetServerInfoAsync(ApiEndpoint apiEndpoint, CancellationToken token = default)
        {
            await UniTask.SwitchToThreadPool();
            var url = apiEndpoint.Url.Combine(API_GET_SERVER_INFO);

            for (var i = 0; i < MajEnv.HTTP_REQUEST_MAX_RETRY; i++)
            {
                var rsp = await GetAsync(url, token);
                var e = default(Exception?);
                if (rsp.IsSuccessfully && rsp.TryDeserialize<MajNetServerInfo>(out var serverInfo, out e) && serverInfo is not null)
                {
                    return serverInfo;
                }
                MajDebug.LogError(LOG_TAG, $"[Online] Failed to get server info: {e?.Message ?? rsp.StatusCode?.ToString() ?? "Unknown error"}");
                if (rsp.ErrorCode == HttpErrorCode.Canceled || rsp.StatusCode is not null)
                {
                    break;
                }
            }

            return null;
        }
        /// <summary>
        /// Drops every cached response, so the next requests reach the server. Requests still running are not cached when
        /// they complete.
        /// </summary>
        public static void ClearResponseCache()
        {
            s_interactSummaries.Clear();
            s_commentPages.Clear();
            s_scoreInfos.Clear();
        }

        #region Internal method
        /// <summary>Sends a GET request and wraps its outcome; transport failures, timeouts and cancellations become unsuccessful responses.</summary>
        /// <param name="uri">The requested URI.</param>
        /// <param name="token">The token canceling the request.</param>
        /// <param name="client">The client sending the request; <see cref="MajEnv.SharedHttpClient"/> when null.</param>
        /// <returns>The response; never throws.</returns>
        private static async ValueTask<EndpointResponse> GetAsync(Uri uri, CancellationToken token = default, HttpClient? client = null)
        {
            try
            {
                client ??= MajEnv.SharedHttpClient;
                var rsp = await client.GetAsync(uri, token);
                var buffer = await rsp.Content.ReadAsByteArrayAsync();
                return CreateResponse(HttpMethod.Get, uri, rsp, buffer);
            }
            catch (OperationCanceledException)
            {
                return CreateCanceledResponse(HttpMethod.Get, uri, token);
            }
            catch (Exception e)
            {
                return CreateUnreachableResponse(HttpMethod.Get, uri, e);
            }
        }
        /// <summary>Sends a POST request without a body.</summary>
        /// <param name="uri">The requested URI.</param>
        /// <param name="token">The token canceling the request.</param>
        /// <returns>The response; never throws.</returns>
        private static ValueTask<EndpointResponse> PostAsync(Uri uri, CancellationToken token = default)
        {
            return PostAsync(uri, null, token);
        }
        /// <summary>Sends a POST request.</summary>
        /// <param name="uri">The requested URI.</param>
        /// <param name="content">The body, which the request disposes; an empty JSON body when null.</param>
        /// <param name="token">The token canceling the request.</param>
        /// <param name="client">The client sending the request; <see cref="MajEnv.SharedHttpClient"/> when null.</param>
        /// <returns>The response; never throws.</returns>
        private static ValueTask<EndpointResponse> PostAsync(Uri uri, HttpContent? content, CancellationToken token = default, HttpClient? client = null)
        {
            return SendAsync(HttpMethod.Post, uri, content, token, client);
        }
        /// <summary>Sends a request with a body and wraps its outcome; transport failures, timeouts and cancellations become unsuccessful responses.</summary>
        /// <param name="method">The HTTP method.</param>
        /// <param name="uri">The requested URI.</param>
        /// <param name="content">The body, which the request disposes; an empty JSON body when null.</param>
        /// <param name="token">The token canceling the request.</param>
        /// <param name="client">The client sending the request; <see cref="MajEnv.SharedHttpClient"/> when null.</param>
        /// <returns>The response; never throws.</returns>
        private static async ValueTask<EndpointResponse> SendAsync(HttpMethod method, Uri uri, HttpContent? content, CancellationToken token = default, HttpClient? client = null)
        {
            try
            {
                client ??= MajEnv.SharedHttpClient;
                using var request = new HttpRequestMessage(method, uri)
                {
                    Content = content ?? new StringContent(string.Empty, Encoding.UTF8, "application/json")
                };
                using var rsp = await client.SendAsync(request, token);
                var buffer = await rsp.Content.ReadAsByteArrayAsync();
                return CreateResponse(method, uri, rsp, buffer);
            }
            catch (OperationCanceledException)
            {
                return CreateCanceledResponse(method, uri, token);
            }
            catch (Exception e)
            {
                return CreateUnreachableResponse(method, uri, e);
            }
        }
        /// <summary>Wraps a response that reached the client, successful for a status code from 200 to 299.</summary>
        /// <param name="method">The HTTP method, for the log.</param>
        /// <param name="uri">The requested URI.</param>
        /// <param name="rsp">The HTTP response.</param>
        /// <param name="buffer">The body of the response.</param>
        /// <returns>The wrapped response.</returns>
        private static EndpointResponse CreateResponse(HttpMethod method, Uri uri, HttpResponseMessage rsp, byte[] buffer)
        {
            var isSuccessful = ((int)rsp.StatusCode).InRange(200, 299);
            var epRsp = new EndpointResponse(buffer, _defaultJsonSerializer, _defaultJsonSerializerSettings)
            {
                Endpoint = uri,
                IsSuccessfully = isSuccessful,
                IsDeserializable = isSuccessful,
                ErrorCode = isSuccessful ? HttpErrorCode.NoError : HttpErrorCode.Unsuccessful,
                StatusCode = rsp.StatusCode,
                Headers = rsp.Headers.ToDictionary(kv => kv.Key.ToLower(), kv => kv.Value),
                Message = isSuccessful ? "Ok" : string.Empty
            };
            if (isSuccessful)
            {
                MajDebug.LogDebug(LOG_TAG, $"[Online][{method}] {uri}\n{epRsp}");
            }
            else
            {
                MajDebug.LogError(LOG_TAG, $"[Online][{method}] {uri} - Unsuccessful\n{epRsp}");
            }
            return epRsp;
        }
        /// <summary>Wraps a request that was canceled by its token or timed out.</summary>
        /// <param name="method">The HTTP method, for the log.</param>
        /// <param name="uri">The requested URI.</param>
        /// <param name="token">The token of the request, which tells a cancellation from a timeout.</param>
        /// <returns>An unsuccessful response with <see cref="HttpErrorCode.Canceled"/> or <see cref="HttpErrorCode.Timeout"/>.</returns>
        private static EndpointResponse CreateCanceledResponse(HttpMethod method, Uri uri, CancellationToken token)
        {
            var errorCode = HttpErrorCode.Timeout;
            if (token.IsCancellationRequested)
            {
                errorCode = HttpErrorCode.Canceled;
                MajDebug.LogError(LOG_TAG, $"[Online][{method}] {uri} - Canceled");
            }
            else
            {
                MajDebug.LogError(LOG_TAG, $"[Online][{method}] {uri} - Timeout");
            }
            return new EndpointResponse(Array.Empty<byte>(), _defaultJsonSerializer, _defaultJsonSerializerSettings)
            {
                Endpoint = uri,
                IsSuccessfully = false,
                IsDeserializable = false,
                ErrorCode = errorCode,
                StatusCode = null,
                Message = string.Empty
            };
        }
        /// <summary>Wraps a request that failed before a response arrived.</summary>
        /// <param name="method">The HTTP method, for the log.</param>
        /// <param name="uri">The requested URI.</param>
        /// <param name="e">The failure.</param>
        /// <returns>An unsuccessful response with <see cref="HttpErrorCode.Unreachable"/>.</returns>
        private static EndpointResponse CreateUnreachableResponse(HttpMethod method, Uri uri, Exception e)
        {
            MajDebug.LogError(LOG_TAG, $"[Online][{method}] {uri} - Unknown Error\n{e}");
            return new EndpointResponse(Array.Empty<byte>(), _defaultJsonSerializer, _defaultJsonSerializerSettings)
            {
                Endpoint = uri,
                IsSuccessfully = false,
                IsDeserializable = false,
                ErrorCode = HttpErrorCode.Unreachable,
                StatusCode = null,
                Message = e.ToString()
            };
        }
        /// <summary>Serializes a request body with the camel case names the server expects.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The value to serialize.</param>
        /// <returns>A task returning the JSON text.</returns>
        private static Task<string> SerializeJsonAsync<T>(T value)
        {
            return Serializer.Json.SerializeAsync(value, _defaultJsonSerializer);
        }
        /// <summary>Wraps JSON text as a request body; a request disposes its body, so every attempt needs a new one.</summary>
        /// <param name="json">The JSON text.</param>
        /// <returns>The body.</returns>
        private static StringContent CreateJsonContent(string json)
        {
            return new StringContent(json, Encoding.UTF8, "application/json");
        }
        /// <summary>Gets the state of an endpoint, creating it on first access.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <returns>The state of the endpoint.</returns>
        private static ApiEndpointStatistics GetApiEndpointStatistic(ApiEndpoint endpoint)
        {
            ref var @lock = ref _dictLock;
            var isLocked = false;
            try
            {
                @lock.Enter(ref isLocked);
                if (!_endpointStatistics.TryGetValue(endpoint, out var stats))
                {
                    stats = new ApiEndpointStatistics
                    {
                        Endpoint = endpoint
                    };
                    _endpointStatistics[endpoint] = stats;
                }
                return stats;
            }
            finally
            {
                if (isLocked)
                {
                    @lock.Exit();
                }
            }
        }
        /// <summary>Gets the state of an endpoint without creating it.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <returns>The state of the endpoint, or null when it was never accessed.</returns>
        private static ApiEndpointStatistics? FindApiEndpointStatistic(ApiEndpoint endpoint)
        {
            ref var @lock = ref _dictLock;
            var isLocked = false;
            try
            {
                @lock.Enter(ref isLocked);
                return _endpointStatistics.TryGetValue(endpoint, out var stats) ? stats : null;
            }
            finally
            {
                if (isLocked)
                {
                    @lock.Exit();
                }
            }
        }
        /// <summary>Copies the state of every endpoint that was accessed.</summary>
        /// <param name="buffer">The list receiving the states.</param>
        /// <returns>The number of states added.</returns>
        private static int GetAllApiEndpointStatistic(IList<ApiEndpointStatistics> buffer)
        {
            ref var @lock = ref _dictLock;
            var isLocked = false;
            try
            {
                @lock.Enter(ref isLocked);
                var i = 0;
                foreach (var (_, statistics) in _endpointStatistics)
                {
                    i++;
                    buffer.Add(statistics);
                }
                return i;
            }
            finally
            {
                if (isLocked)
                {
                    @lock.Exit();
                }
            }
        }
        #endregion

        /// <summary>The login and machine registration state of an endpoint; null while unknown.</summary>
        private class ApiEndpointStatistics
        {
            /// <summary>Gets the endpoint.</summary>
            public required ApiEndpoint Endpoint { get; init; }

            /// <summary>Gets or sets whether the machine is registered on the endpoint.</summary>
            public bool? IsMachineRegistered { get; set; }
            /// <summary>
            /// Gets or sets whether the user is logged in; logging out also clears the user shown for the endpoint, and any
            /// change drops the cached responses.
            /// </summary>
            public bool? IsUserLoggedIn
            {
                get
                {
                    return _isUserLoggedIn;
                }
                set
                {
                    if (value is false or null)
                    {
                        Endpoint.RuntimeConfig.Avatar = null;
                        Endpoint.RuntimeConfig.Username = string.Empty;
                        Endpoint.RuntimeConfig.Nickname = string.Empty;
                    }
                    Endpoint.RuntimeConfig.IsLoggedIn = value ?? false;
                    if (_isUserLoggedIn != value)
                    {
                        // Cached counts and comments carry the votes of the user who read them.
                        ClearResponseCache();
                    }
                    _isUserLoggedIn = value;
                }
            }
            /// <summary>Gets or sets whether the endpoint provides machine registration.</summary>
            public bool? IsMachineRegistrationSupported { get; set; }

            /// <summary>The backing field of <see cref="IsUserLoggedIn"/>.</summary>
            private bool? _isUserLoggedIn;

            /// <summary>Serializes the requests that change the state.</summary>
            private readonly AsyncLock _lock = new();

            /// <summary>Waits for exclusive access to the state.</summary>
            /// <param name="token">The token canceling the wait.</param>
            /// <returns>A task returning the lease, which releases the access when disposed.</returns>
            public AwaitableDisposable<IDisposable> LockAsync(CancellationToken token = default)
            {
                return _lock.LockAsync(token);
            }
        }
    }
}
