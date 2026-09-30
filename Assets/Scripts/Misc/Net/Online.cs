using Cysharp.Text;
using Cysharp.Threading.Tasks;
using MajdataPlay.Buffers;
using MajdataPlay.Drawing;
using MajdataPlay.Threading;
using MajdataPlay.UnsafeKit;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Printing;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.Utils;
using UnityEngine;
using UnityEngine.Networking;
using Nito.AsyncEx;
using MajdataPlay.Numerics;
using MajdataPlay.Diagnostics;
#nullable enable
namespace MajdataPlay.Net
{
    internal static class Online
    {
        readonly static HttpClient _client = MajEnv.SharedHttpClient;
        readonly static JsonSerializer _defaultJsonSerializer = JsonSerializer.Create(_defaultJsonSerializerSettings);
        readonly static JsonSerializerSettings _defaultJsonSerializerSettings = new()
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            },
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };

        public const string API_GET_USER_INFO = "account/info";
        public const string API_GET_USER_ICON = "account/icon?username={0}";
        public const string API_GET_USER_SCORES = "account/scores";
        public const string API_GET_USER_FAVCOLLECTION = "account/favorite/collection/list";
        public const string API_GET_MAICHART_LIST = "maichart/list";
        public const string API_GET_MAICHART_INTERACT = "maichart/{0}/interact";
        public const string API_GET_MAICHART_SCORE = "maichart/{0}/score";
        public const string API_GET_MACHINE_INFO = "machine/info";
        public const string API_GET_AUTH_CHECK = "machine/auth/check";

        public const string API_POST_USER_LOGIN = "account/login";
        public const string API_POST_USER_LOGOUT = "account/logout";
        public const string API_POST_MAICHART_INTERACT = "maichart/{0}/interact";
        public const string API_POST_MAICHART_SCORE = "maichart/{0}/score";
        public const string API_POST_MACHINE_REGISTER = "machine/register";
        public const string API_POST_AUTH_REQUEST = "machine/auth/request";
        public const string API_POST_AUTH_REVOKE = "machine/auth/revoke";

        static SpinLock _dictLock = new();
        static SpinLock _cachedResponseLock = new();

        readonly static Dictionary<OnlineSongDetail, CachedApiEndpointResponse> _cachedResponse = new();
        readonly static Dictionary<ApiEndpoint, ApiEndpointStatistics> _endpointStatistics = new();

        public static async ValueTask HeartbeatAsync(CancellationToken token = default)
        {
            using var rentedBuffer = new PooledList<ApiEndpointStatistics>();
            await UniTask.SwitchToThreadPool();
            MajDebug.LogDebug("Online heartbeat executing");
            GetAllApiEndpointStatistic(rentedBuffer);
            foreach (var statistics in rentedBuffer)
            {
                if (statistics.IsMachineRegistered is true)
                {
                    try
                    {
                        using (await statistics.LockAsync(token))
                        {
                            var isAlive = await CheckMachineRegisterAsync(statistics.Endpoint, token);
                            statistics.IsMachineRegistered = isAlive;
                        }
                    }
                    catch (Exception e)
                    {
                        MajDebug.LogException(e);
                    }
                }
                if (statistics.IsUserLoggedIn is true)
                {
                    try
                    {
                        using (await statistics.LockAsync(token))
                        {
                            var rsp = await GetUserInfoAsyncInternal(statistics.Endpoint, token);
                            var isAlive = rsp.StatusCode is not HttpStatusCode.Unauthorized;
                            statistics.IsUserLoggedIn = isAlive;
                        }
                    }
                    catch (Exception e)
                    {
                        MajDebug.LogException(e);
                    }
                }
                
            }
            MajDebug.LogDebug("Online heartbeat has been completed");
        }
        public static async ValueTask<EndpointResponse<UserSummary>> GetUserInfoAsync(ApiEndpoint apiEndpoint, CancellationToken token = default)
        {
            await UniTask.SwitchToThreadPool();
            var statistic = GetApiEndpointStatistic(apiEndpoint);
            var rsp = await GetUserInfoAsyncInternal(apiEndpoint, token);
            if (rsp.StatusCode is HttpStatusCode.Unauthorized)
            {
                using (await statistic.LockAsync())
                {
                    statistic.IsUserLoggedIn = false;
                }
            }
            else if (rsp.StatusCode is HttpStatusCode.OK)
            {
                using (await statistic.LockAsync())
                {
                    statistic.IsUserLoggedIn = true;
                }
            }
            return rsp;
        }
        public static async ValueTask<MajNetAccountSongScore[]> GetUserScoresAsync(ApiEndpoint apiEndpoint, CancellationToken token = default)
        {
            await UniTask.SwitchToThreadPool();
            try
            {
                var uri = apiEndpoint.Url.Combine(API_GET_USER_SCORES);
                var rsp = default(EndpointResponse);
                for (var i = 0; i <= MajEnv.HTTP_REQUEST_MAX_RETRY; i++)
                {
                    rsp = await GetAsync(uri, token);
                    if (rsp.StatusCode is HttpStatusCode.Unauthorized)
                    {
                        return Array.Empty<MajNetAccountSongScore>();
                    }
                    else if (!rsp.IsSuccessfully || !rsp.IsDeserializable)
                    {
                        MajDebug.LogError("Failed to get user scores");
                        MajDebug.LogError($"Url:{uri}\nStatusCode:{rsp.StatusCode}\nErrorCode:{rsp.ErrorCode}\nMessage:{rsp.Message}");
                        continue;
                    }
                    var userScores = await rsp.DeserializeAsync<MajNetAccountSongScore[]>() ?? Array.Empty<MajNetAccountSongScore>();
                    MajDebug.LogInfo($"Fetched {userScores.Length} scores for the user from endpoint");
                    return userScores;
                }
                return Array.Empty<MajNetAccountSongScore>();
            }
            catch (Exception e)
            {
                MajDebug.LogError("Get user scores failed: ");
                MajDebug.LogException(e);
                return Array.Empty<MajNetAccountSongScore>();
            }
        }
        public static async ValueTask<bool> CheckMachineRegisterAsync(ApiEndpoint apiEndpoint, CancellationToken token = default)
        {
            await UniTask.SwitchToThreadPool();
            var uri = apiEndpoint.Url.Combine(API_GET_MACHINE_INFO);
            var rsp = await GetAsync(uri, token);

            return rsp.IsSuccessfully;
        }
        public static async ValueTask<EndpointResponse> RegisterAsync(ApiEndpoint apiEndpoint, MachineInfo machineInfo, CancellationToken token = default)
        {
            var statistics = GetApiEndpointStatistic(apiEndpoint);
        FAST_RETURN:
            if (statistics.IsMachineRegistrationSupported is false)
            {
                return new()
                {
                    Endpoint = apiEndpoint.Url,
                    IsSuccessfully = false,
                    IsDeserializable = false,
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = HttpErrorCode.NotSupported,
                    Message = "MAJTEXT_ONLINE_MACHINE_REGISTRATION_UNSUPPORTED"
                };
            }
            using (await statistics.LockAsync())
            {
                if (statistics.IsMachineRegistrationSupported is false)
                {
                    goto FAST_RETURN;
                }
                var uri = apiEndpoint.Url.Combine(API_POST_MACHINE_REGISTER);
                var json = await Serializer.Json.SerializeAsync(machineInfo, _defaultJsonSerializer);

                var rsp = await PostAsync(uri, new StringContent(json, Encoding.UTF8, "application/json"), token);
                if (!rsp.IsSuccessfully && rsp.StatusCode == HttpStatusCode.NotFound)
                {
                    statistics.IsMachineRegistrationSupported = false;
                    statistics.IsMachineRegistered = false;
                    return new()
                    {
                        Endpoint = apiEndpoint.Url,
                        IsSuccessfully = false,
                        IsDeserializable = false,
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = HttpErrorCode.NotSupported,
                        Message = "MAJTEXT_ONLINE_MACHINE_REGISTRATION_UNSUPPORTED"
                    };
                }
                statistics.IsMachineRegistrationSupported = true;
                statistics.IsMachineRegistered = rsp.IsSuccessfully;
                return rsp;
            }
        }
        public static async ValueTask<EndpointResponse> AuthRequestAsync(ApiEndpoint apiEndpoint, CancellationToken token = default)
        {
            var statistics = GetApiEndpointStatistic(apiEndpoint);
            
            if (statistics.IsMachineRegistrationSupported is false)
            {
                return new()
                {
                    Endpoint = apiEndpoint.Url,
                    IsSuccessfully = false,
                    IsDeserializable = false,
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = HttpErrorCode.NotSupported,
                    Message = "MAJTEXT_ONLINE_MACHINE_REGISTRATION_UNSUPPORTED"
                };
            }
            else if (statistics.IsMachineRegistrationSupported is null)
            {
                throw new InvalidOperationException();
            }
            var uri = apiEndpoint.Url.Combine(API_POST_AUTH_REQUEST);
            var rsp = await PostAsync(uri, token);

            if (rsp.StatusCode == HttpStatusCode.Created)
            {
                return new(rsp.AsMemory(), _defaultJsonSerializer, _defaultJsonSerializerSettings)
                {
                    Endpoint = rsp.Endpoint,
                    IsSuccessfully = true,
                    IsDeserializable = true,
                    StatusCode = rsp.StatusCode,
                    ErrorCode = HttpErrorCode.NoError,
                    Headers = rsp.Headers,
                    Message = string.Empty
                };
            }
            else
            {
                return new()
                {
                    Endpoint = rsp.Endpoint,
                    IsSuccessfully = false,
                    IsDeserializable = false,
                    StatusCode = rsp.StatusCode,
                    ErrorCode = rsp.ErrorCode,
                    Headers = rsp.Headers,
                    Message = "MAJTEXT_ONLINE_MACHINE_AUTH_REQUEST_FAILED"
                };
            }
        }
        public static async ValueTask<EndpointResponse> AuthCheckAsync(ApiEndpoint apiEndpoint, string authId, CancellationToken token = default)
        {
            var statistics = GetApiEndpointStatistic(apiEndpoint);
            if (statistics.IsMachineRegistrationSupported is false)
            {
                return new()
                {
                    Endpoint = apiEndpoint.Url,
                    IsSuccessfully = false,
                    IsDeserializable = false,
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = HttpErrorCode.NotSupported,
                    Message = "MAJTEXT_ONLINE_MACHINE_REGISTRATION_UNSUPPORTED"
                };
            }
            else if (statistics.IsMachineRegistrationSupported is null)
            {
                throw new InvalidOperationException();
            }
            var uriBuilder = new UriBuilder(apiEndpoint.Url.Combine(API_GET_AUTH_CHECK));
            uriBuilder.Query = $"auth-id={authId}";
            var uri = uriBuilder.Uri;
            var rsp = await GetAsync(uri, token);
            return rsp;
        }
        public static async ValueTask<EndpointResponse> AuthRevokeAsync(ApiEndpoint apiEndpoint, string authId, CancellationToken token = default)
        {
            var statistics = GetApiEndpointStatistic(apiEndpoint);
            if (statistics.IsMachineRegistrationSupported is false)
            {
                return new()
                {
                    Endpoint = apiEndpoint.Url,
                    IsSuccessfully = false,
                    IsDeserializable = false,
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = HttpErrorCode.NotSupported,
                    Message = "MAJTEXT_ONLINE_MACHINE_REGISTRATION_UNSUPPORTED"
                };
            }
            else if (statistics.IsMachineRegistrationSupported is null)
            {
                throw new InvalidOperationException();
            }
            var uriBuilder = new UriBuilder(apiEndpoint.Url.Combine(API_POST_AUTH_REVOKE));
            uriBuilder.Query = $"auth-id={authId}";
            var uri = uriBuilder.Uri;
            var rsp = await PostAsync(uri, token);

            return rsp;
        }
        public static async ValueTask<EndpointResponse> LoginAsync(ApiEndpoint apiEndpoint, string username, string password, CancellationToken token = default)
        {
            await UniTask.SwitchToThreadPool();
            if (apiEndpoint == null)
            {
                throw new ArgumentNullException(nameof(apiEndpoint));
            }
            var uri = apiEndpoint.Url.Combine(API_POST_USER_LOGIN);
            var statistic = GetApiEndpointStatistic(apiEndpoint);
            if (statistic.IsUserLoggedIn is true)
            {
                return new()
                {
                    Endpoint = uri,
                    IsSuccessfully = true,
                    IsDeserializable = false,
                    StatusCode = HttpStatusCode.OK,
                    ErrorCode = HttpErrorCode.NoError,
                    Message = string.Empty
                };
            }
            if (username == "YourUsername" || password == "YourUsername" ||
                    string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                return new EndpointResponse()
                {
                    Endpoint = uri,
                    ErrorCode = HttpErrorCode.InvalidRequest,
                    IsSuccessfully = false,
                    IsDeserializable = false,
                    Message = "MAJTEXT_ONLINE_USERNAME_OR_PASSWORD_UNSET"
                };
            }

            var pwdHashStr = HashHelper.ToHexString(await HashHelper.ComputeHashAsync(Encoding.UTF8.GetBytes(password)));

            var formData = new MultipartFormDataContent
            {
                { new StringContent(username), "username" },
                { new StringContent(pwdHashStr.Replace("-", "").ToLower()), "password" }
            };

            var rsp = await PostAsync(uri, formData, token);
            if (rsp.StatusCode is HttpStatusCode.Unauthorized)
            {
                rsp = new(rsp.AsMemory(), _defaultJsonSerializer, _defaultJsonSerializerSettings)
                {
                    Endpoint = rsp.Endpoint,
                    StatusCode = rsp.StatusCode,
                    ErrorCode = rsp.ErrorCode,
                    IsSuccessfully = rsp.IsSuccessfully,
                    IsDeserializable = rsp.IsDeserializable,
                    Headers = rsp.Headers,
                    Message = "MAJTEXT_ONLINE_USERNAME_OR_PASSWORD_INCORRECT"
                };
            }
            else if (rsp.StatusCode is HttpStatusCode.OK)
            {
                using (await statistic.LockAsync())
                {
                    statistic.IsUserLoggedIn = true;
                }
            }
            return rsp;
        }
        public static async ValueTask LogoutAllAsync(CancellationToken token = default)
        {
            using var rentedBuffer = new PooledList<ApiEndpointStatistics>();
            await UniTask.SwitchToThreadPool();
            GetAllApiEndpointStatistic(rentedBuffer);
            foreach (var statistics in rentedBuffer)
            {
                using (await statistics.LockAsync())
                {
                    var apiEndpoint = statistics.Endpoint;
                    try
                    {
                        MajDebug.LogInfo("Logout");
                        var uri = apiEndpoint.Url.Combine(API_POST_USER_LOGOUT);
                        var rsp = await PostAsync(uri, token);
                    }
                    catch (Exception e)
                    {
                        MajDebug.LogException(e);
                    }
                    finally
                    {
                        statistics.IsUserLoggedIn = false;
                        apiEndpoint.RuntimeConfig.AuthMethod = NetAuthMethodOption.None;
                        apiEndpoint.RuntimeConfig.Avatar = null;
                        apiEndpoint.RuntimeConfig.Username = "???";
                        apiEndpoint.RuntimeConfig.AuthUsername = apiEndpoint.Username;
                        apiEndpoint.RuntimeConfig.AuthPassword = apiEndpoint.Password;
                    }
                }
            }
        }
        public static async ValueTask<MajNetSongInteract?> GetChartInteractAsync(OnlineSongDetail song, CancellationToken token = default)
        {
            await UniTask.SwitchToThreadPool();
            var cachedResponse = GetCachedResponse(song);
            var cachedInteract = cachedResponse.Interact;
            var isCacheAlive = (DateTime.Now - cachedInteract.LastActive).TotalSeconds < MajEnv.ONLINE_RESPONSE_CACHE_TTL_SEC;
            if (isCacheAlive)
            {
                return cachedInteract.Response;
            }
            using (await cachedInteract.RequestLock.LockAsync(token))
            {
                isCacheAlive = (DateTime.Now - cachedInteract.LastActive).TotalSeconds < MajEnv.ONLINE_RESPONSE_CACHE_TTL_SEC;
                if (isCacheAlive)
                {
                    return cachedInteract.Response;
                }
                var serverInfo = song.ServerInfo;
                var interactUrl = BuildMaiChartUri(song.ServerInfo, API_GET_MAICHART_INTERACT, song.Id);
                var rsp = default(EndpointResponse);

                for (var i = 0; i <= MajEnv.HTTP_REQUEST_MAX_RETRY; i++)
                {
                    var e = default(Exception?);
                    rsp = await GetAsync(interactUrl, token);
                    if (rsp.IsSuccessfully && rsp.IsDeserializable && rsp.TryDeserialize<MajNetSongInteract?>(out var intlist, out e) && intlist is not null)
                    {
                        MajDebug.LogDebug(rsp);
                        if (intlist is MajNetSongInteract interactRsp)
                        {
                            cachedResponse.Interact.Response = interactRsp;
                            cachedResponse.Interact.LastActive = DateTime.Now;
                        }
                        return intlist;
                    }
                    else
                    {
                        MajDebug.LogError(rsp);
                        MajDebug.LogError($"Failed to get chart interact: {e?.Message ?? "Unknown error"}");
                    }
                    if (rsp.ErrorCode == HttpErrorCode.Canceled)
                    {
                        break;
                    }
                    else if (rsp.StatusCode is HttpStatusCode.BadRequest
                        or HttpStatusCode.NotFound
                        or HttpStatusCode.Unauthorized
                        or HttpStatusCode.Forbidden)
                    {
                        return null;
                    }
                    else if (!rsp.IsSuccessfully && rsp.StatusCode is not null)
                    {
                        return null;
                    }
                }
                return null;
            }
        }
        public static async ValueTask<MajNetSongScoreInfo?> GetChartScoreInfoAsync(OnlineSongDetail song, CancellationToken token = default)
        {
            await UniTask.SwitchToThreadPool();
            var cachedResponse = GetCachedResponse(song);
            var cachedScoreInfo = cachedResponse.ScoreInfo;
            var isCacheAlive = (DateTime.Now - cachedScoreInfo.LastActive).TotalSeconds < MajEnv.ONLINE_RESPONSE_CACHE_TTL_SEC;
            if (isCacheAlive)
            {
                return cachedScoreInfo.Response;
            }
            using (await cachedScoreInfo.RequestLock.LockAsync(token))
            {
                isCacheAlive = (DateTime.Now - cachedScoreInfo.LastActive).TotalSeconds < MajEnv.ONLINE_RESPONSE_CACHE_TTL_SEC;
                if (isCacheAlive)
                {
                    return cachedScoreInfo.Response;
                }
                var serverInfo = song.ServerInfo;
                var interactUrl = BuildMaiChartUri(song.ServerInfo, API_GET_MAICHART_SCORE, song.Id);
                var rsp = default(EndpointResponse);

                for (var i = 0; i <= MajEnv.HTTP_REQUEST_MAX_RETRY; i++)
                {
                    var e = default(Exception?);
                    rsp = await GetAsync(interactUrl, token);
                    if (rsp.IsSuccessfully && rsp.IsDeserializable && rsp.TryDeserialize<MajNetSongScoreInfo?>(out var scoreInfo, out e) && scoreInfo is not null)
                    {
                        if (scoreInfo is MajNetSongScoreInfo scoreInfoRsp)
                        {
                            cachedScoreInfo.Response = scoreInfoRsp;
                            cachedScoreInfo.LastActive = DateTime.Now;
                        }
                        return scoreInfo;
                    }
                    else
                    {
                        MajDebug.LogError($"Failed to get chart interact: {e?.Message ?? "Unknown error"}");
                    }
                    if (rsp.ErrorCode == HttpErrorCode.Canceled)
                    {
                        break;
                    }
                    else if (rsp.StatusCode is HttpStatusCode.BadRequest
                        or HttpStatusCode.NotFound
                        or HttpStatusCode.Unauthorized
                        or HttpStatusCode.Forbidden)
                    {
                        return null;
                    }
                    else if (!rsp.IsSuccessfully && rsp.StatusCode is not null)
                    {
                        return null;
                    }
                }
                return null;
            }
        }

        public static async ValueTask<EndpointResponse> PostLikeAsync(OnlineSongDetail song, CancellationToken token = default)
        {
            await UniTask.SwitchToThreadPool();
            var serverInfo = song.ServerInfo;
            var interactUrl = BuildMaiChartUri(song.ServerInfo, API_POST_MAICHART_INTERACT, song.Id);
            var rsp = default(EndpointResponse);

            for (var i = 0; i <= MajEnv.HTTP_REQUEST_MAX_RETRY; i++)
            {
                var formData = new MultipartFormDataContent
                {
                    { new StringContent("like"), "type" },
                    { new StringContent("..."), "content" },
                };
                rsp = await PostAsync(interactUrl, formData, token);
                if (rsp.IsSuccessfully)
                {
                    MajDebug.LogDebug(rsp);
                    var cachedResponse = GetCachedResponse(song);
                    using (await cachedResponse.Interact.RequestLock.LockAsync())
                    {
                        cachedResponse.Interact.LastActive = DateTime.UnixEpoch;
                    }
                    return rsp;
                }
                else
                {
                    MajDebug.LogError("Failed to post like interact");
                }

                if (rsp.ErrorCode == HttpErrorCode.Canceled)
                {
                    break;
                }
                else if (rsp.StatusCode is HttpStatusCode.BadRequest
                    or HttpStatusCode.NotFound
                    or HttpStatusCode.Unauthorized
                    or HttpStatusCode.Forbidden)
                {
                    break;
                }
                else if (!rsp.IsSuccessfully && rsp.StatusCode is not null)
                {
                    break;
                }
            }
            return rsp;
        }
        public static async ValueTask<EndpointResponse> PostScoreAsync(OnlineSongDetail song, MaiScore score, CancellationToken token = default)
        {
            await UniTask.SwitchToThreadPool();
            var serverInfo = song.ServerInfo;
            var scoreUrl = BuildMaiChartUri(song.ServerInfo, API_POST_MAICHART_SCORE, song.Id);
            var json = await Serializer.Json.SerializeAsync(score, _defaultJsonSerializer);
            var rsp = default(EndpointResponse);


            for (var i = 0; i < MajEnv.HTTP_REQUEST_MAX_RETRY; i++)
            {
                rsp = await PostAsync(scoreUrl, new StringContent(json, Encoding.UTF8, "application/json"), token);
                if (rsp.IsSuccessfully)
                {
                    MajDebug.LogDebug(rsp);
                    var cachedResponse = GetCachedResponse(song);
                    using (await cachedResponse.ScoreInfo.RequestLock.LockAsync())
                    {
                        cachedResponse.ScoreInfo.LastActive = DateTime.UnixEpoch;
                    }
                    return rsp;
                }
                else
                {
                    MajDebug.LogError("Failed to post score");
                }
                if (rsp.ErrorCode == HttpErrorCode.Canceled)
                {
                    break;
                }
                else if (rsp.StatusCode is HttpStatusCode.BadRequest
                    or HttpStatusCode.NotFound
                    or HttpStatusCode.Unauthorized
                    or HttpStatusCode.Forbidden)
                {
                    break;
                }
                else if (!rsp.IsSuccessfully && rsp.StatusCode is not null)
                {
                    break;
                }
            }

            return rsp;
        }
        public static async ValueTask<MajnetSongDetail[]?> GetChartListAsync(ApiEndpoint apiEndpoint, CancellationToken token = default)
        {
            await UniTask.SwitchToThreadPool();
            var url = apiEndpoint.Url.Combine(API_GET_MAICHART_LIST);
            var rsp = default(EndpointResponse);

            for (var i = 0; i < MajEnv.HTTP_REQUEST_MAX_RETRY; i++)
            {
                rsp = await GetAsync(url, token);
                var e = default(Exception?);
                if (rsp.IsSuccessfully && rsp.TryDeserialize<MajnetSongDetail[]>(out var chartList, out e) && chartList is not null)
                {
                    return chartList;
                }
                else
                {
                    MajDebug.LogError($"Failed to get chart list: {e?.Message ?? "Unknown error"}");
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

        public static async ValueTask<MajNetOnlineDanInfo[]?> GetUserOnlineFavCollection(ApiEndpoint apiEndpoint, CancellationToken token = default)
        {
            await UniTask.SwitchToThreadPool();
            var statistic = GetApiEndpointStatistic(apiEndpoint);
            if (statistic.IsUserLoggedIn is not true)
            {
                return null;
            }
            var url = apiEndpoint.Url.Combine(API_GET_USER_FAVCOLLECTION);
            var rsp = default(EndpointResponse);

            for (var i = 0; i < MajEnv.HTTP_REQUEST_MAX_RETRY; i++)
            {
                rsp = await GetAsync(url, token);
                var e = default(Exception?);
                if (rsp.IsSuccessfully && rsp.TryDeserialize<MajNetOnlineDanInfo[]>(out var danList, out e) && danList is not null)
                {
                    MajDebug.LogDebug(rsp);
                    return danList;
                }
                else
                {
                    MajDebug.LogError($"Failed to get dan list: {e?.Message ?? "Unknown error"}");
                }
                if (rsp.ErrorCode == HttpErrorCode.Canceled)
                {
                    break;
                }
                else if (rsp.StatusCode == HttpStatusCode.Unauthorized)
                {
                    MajDebug.LogError($"Failed to get online dan list: user not logged in");
                    using (await statistic.LockAsync(token))
                    {
                        statistic.IsUserLoggedIn = false;
                    }
                    break;
                }
                else if (!rsp.IsSuccessfully && rsp.StatusCode is not HttpStatusCode.OK)
                {
                    break;
                }
            }

            return null;
        }

        public static async ValueTask<Sprite?> GetUserIconAsync(ApiEndpoint apiEndpoint,string username, CancellationToken token = default, Action<string, bool>? onError = null)
        {
            if(string.IsNullOrEmpty(username))
            {
                return null;
            }
            await UniTask.SwitchToThreadPool();
            var url = apiEndpoint.Url.Combine(string.Format(API_GET_USER_ICON, username));
            var statistic = GetApiEndpointStatistic(apiEndpoint);
            for (var i = 0; i <= MajEnv.HTTP_REQUEST_MAX_RETRY; i++)
            {
                try
                {
                    var rsp = await GetAsync(url, token);
                    if (!rsp.IsSuccessfully)
                    {
                        MajDebug.LogError("Failed to download user icon");
                        if (rsp.StatusCode is HttpStatusCode.Unauthorized)
                        {
                            using (await statistic.LockAsync(token))
                            {
                                statistic.IsUserLoggedIn = false;
                            }
                            return default;
                        }
                        await UniTask.SwitchToMainThread();
                        onError?.Invoke("MAJTEXT_LOGIN_DOWNLOAD_AVATAR_FAILED_RETRIED_TIMES_{0}/{1}".i18n(i+1, MajEnv.HTTP_REQUEST_MAX_RETRY+1), true);
                        await UniTask.SwitchToThreadPool();
                        continue;
                    }
                    using (await statistic.LockAsync(token))
                    {
                        statistic.IsUserLoggedIn = true;
                    }
                    var avatar = await SpriteLoader.LoadFromMemoryAsync(rsp.AsMemory());
                    return avatar;
                }
                catch (Exception e)
                {
                    MajDebug.LogError("Failed to download user icon");
                    MajDebug.LogError(e);
                }
            }
            return default;
        }
        public static bool IsMachineRegistered(ApiEndpoint apiEndpoint, CancellationToken token = default)
        {
            var stats = _endpointStatistics.TryGetValue(apiEndpoint, out var s) ? s : null;

            return stats?.IsMachineRegistered ?? false;
        }
        public static void ClearResponseCache()
        {
            ref var @lock = ref _cachedResponseLock;
            var isLocked = false;
            try
            {
                @lock.Enter(ref isLocked);
                _cachedResponse.Clear();
            }
            finally
            {
                if (isLocked)
                {
                    @lock.Exit();
                }
            }
        }
        #region Internal method
        static async ValueTask<EndpointResponse<UserSummary>> GetUserInfoAsyncInternal(ApiEndpoint apiEndpoint, CancellationToken token = default)
        {
            var uri = apiEndpoint.Url.Combine(API_GET_USER_INFO);
            var rsp = default(EndpointResponse);
            for (var i = 0; i <= MajEnv.HTTP_REQUEST_MAX_RETRY; i++)
            {
                rsp = await GetAsync(uri, token);
                if (rsp.StatusCode is HttpStatusCode.Unauthorized)
                {
                    return new EndpointResponse<UserSummary>(rsp);
                }
                else if (!rsp.IsSuccessfully || !rsp.IsDeserializable)
                {
                    MajDebug.LogError("Failed to get user info");
                    continue;
                }
                return new EndpointResponse<UserSummary>(rsp);
            }
            return new EndpointResponse<UserSummary>(rsp);
        }
        static async ValueTask<EndpointResponse> GetAsync(Uri uri, CancellationToken token = default)
        {
            try
            {
                var client = MajEnv.SharedHttpClient;
                var rsp = await client.GetAsync(uri, token);
                var epRsp = default(EndpointResponse);
                var buffer = await rsp.Content.ReadAsByteArrayAsync();
                if (!((int)rsp.StatusCode).InRange(200, 299))
                {
                    epRsp = new EndpointResponse(buffer, _defaultJsonSerializer, _defaultJsonSerializerSettings)
                    {
                        Endpoint = uri,
                        IsSuccessfully = false,
                        IsDeserializable = false,
                        ErrorCode = HttpErrorCode.Unsuccessful,
                        StatusCode = rsp.StatusCode,
                        Headers = rsp.Headers.ToDictionary(kv => kv.Key.ToLower(), kv => kv.Value),
                        Message = ""
                    };
                    MajDebug.LogError($"[Online][GET] {uri} - Unsuccessful\n{epRsp}");
                    return epRsp;
                }                
                epRsp = new EndpointResponse(buffer, _defaultJsonSerializer, _defaultJsonSerializerSettings)
                {
                    Endpoint = uri,
                    IsSuccessfully = true,
                    IsDeserializable = true,
                    ErrorCode = HttpErrorCode.NoError,
                    StatusCode = rsp.StatusCode,
                    Headers = rsp.Headers.ToDictionary(kv => kv.Key.ToLower(), kv => kv.Value),
                    Message = "Ok"
                };
                MajDebug.LogDebug($"[Online][GET] {uri}\n{epRsp}");
                return epRsp;
            }
            catch (OperationCanceledException)
            {
                var errorCode = HttpErrorCode.Timeout;
                if (token.IsCancellationRequested)
                {
                    errorCode = HttpErrorCode.Canceled;
                    MajDebug.LogError($"[Online][GET] {uri} - Canceled");
                }
                else
                {
                    MajDebug.LogError($"[Online][GET] {uri} - Timeout");
                }
                return new EndpointResponse(Array.Empty<byte>(), _defaultJsonSerializer, _defaultJsonSerializerSettings)
                {
                    Endpoint = uri,
                    IsSuccessfully = false,
                    IsDeserializable = false,
                    ErrorCode = errorCode,
                    StatusCode = null,
                    Message = ""
                };
            }
            catch (Exception e)
            {
                MajDebug.LogError($"[Online][GET] {uri} - Unknown Error\n{e}");
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
        }
        static ValueTask<EndpointResponse> PostAsync(Uri uri, CancellationToken token = default)
        {
            return PostAsync(uri, (HttpContent?)null, token);
        }
        static ValueTask<EndpointResponse> PostAsync(Uri uri, string content, CancellationToken token = default)
        {
            return PostAsync(uri, new StringContent(content), token);
        }
        static ValueTask<EndpointResponse> PostAsync(Uri uri, byte[] content, CancellationToken token = default)
        {
            return PostAsync(uri, new ByteArrayContent(content), token);
        }
        static ValueTask<EndpointResponse> PostAsync(Uri uri, ArraySegment<byte> content, CancellationToken token = default)
        {
            return PostAsync(uri, new ByteArrayContent(content.Array, content.Offset, content.Count), token);
        }
        static async ValueTask<EndpointResponse> PostAsync(Uri uri, HttpContent? content, CancellationToken token = default)
        {
            try
            {
                var client = MajEnv.SharedHttpClient;
                var rsp = await (content is null ? client.PostAsync(uri, new StringContent(string.Empty, Encoding.UTF8, "application/json"), token) : client.PostAsync(uri, content, token));
                var buffer = await rsp.Content.ReadAsByteArrayAsync();
                var epRsp = default(EndpointResponse);
                if (!((int)rsp.StatusCode).InRange(200, 299))
                {
                    epRsp = new EndpointResponse(buffer, _defaultJsonSerializer, _defaultJsonSerializerSettings)
                    {
                        Endpoint = uri,
                        IsSuccessfully = false,
                        IsDeserializable = false,
                        ErrorCode = HttpErrorCode.Unsuccessful,
                        StatusCode = rsp.StatusCode,
                        Headers = rsp.Headers.ToDictionary(kv => kv.Key.ToLower(), kv => kv.Value),
                        Message = ""
                    };
                    MajDebug.LogError($"[Online][POST] {uri} - Unsuccessful\n{epRsp}");
                    return epRsp;
                }
                epRsp = new EndpointResponse(buffer, _defaultJsonSerializer, _defaultJsonSerializerSettings)
                {
                    Endpoint = uri,
                    IsSuccessfully = true,
                    IsDeserializable = true,
                    ErrorCode = HttpErrorCode.NoError,
                    StatusCode = rsp.StatusCode,
                    Headers = rsp.Headers.ToDictionary(kv => kv.Key.ToLower(), kv => kv.Value),
                    Message = "Ok"
                };
                MajDebug.LogDebug($"[Online][POST] {uri}\n{epRsp}");
                return epRsp;
            }
            catch (OperationCanceledException)
            {
                var errorCode = HttpErrorCode.Timeout;
                if (token.IsCancellationRequested)
                {
                    errorCode = HttpErrorCode.Canceled;
                    MajDebug.LogError($"[Online][POST] {uri} - Canceled");
                }
                else
                {
                    MajDebug.LogError($"[Online][POST] {uri} - Timeout");
                }
                return new EndpointResponse(Array.Empty<byte>(), _defaultJsonSerializer, _defaultJsonSerializerSettings)
                {
                    Endpoint = uri,
                    IsSuccessfully = false,
                    IsDeserializable = false,
                    ErrorCode = errorCode,
                    StatusCode = null,
                    Message = ""
                };
            }
            catch (Exception e)
            {
                MajDebug.LogError($"[Online][POST] {uri} - Unknown Error\n{e}");
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
        }

        static Uri BuildMaiChartUri(ApiEndpoint endpoint, string template, string chartId)
        {
            return BuildMaiChartUri(endpoint, template, Guid.Parse(chartId));
        }
        static Uri BuildMaiChartUri(ApiEndpoint endpoint, string template, Guid chartId)
        {
            using var sb = ZString.CreateStringBuilder(true);
            sb.AppendFormat(template, chartId);

            return new Uri(endpoint.Url, sb.ToString());
        }

        static ApiEndpointStatistics GetApiEndpointStatistic(ApiEndpoint endpoint)
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
        static CachedApiEndpointResponse GetCachedResponse(OnlineSongDetail songDetail)
        {
            ref var @lock = ref _cachedResponseLock;
            var isLocked = false;
            try
            {
                @lock.Enter(ref isLocked);
                if (!_cachedResponse.TryGetValue(songDetail, out var cachedResponse))
                {
                    cachedResponse = new CachedApiEndpointResponse();
                    _cachedResponse[songDetail] = cachedResponse;
                }
                return cachedResponse;
            }
            finally
            {
                if (isLocked)
                {
                    @lock.Exit();
                }
            }
        }
        static int GetAllApiEndpointStatistic(IList<ApiEndpointStatistics> buffer)
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
        class ReadOnlyApiEndpointStatistics
        {
            public bool IsMachineRegistered
            {
                get
                {
                    return _endpointStatistics.IsMachineRegistered ?? false;
                }
            }
            public bool IsUserLoggedIn
            {
                get
                {
                    return _endpointStatistics.IsUserLoggedIn ?? false;
                }
            }
            public bool IsMachineRegistrationSupported
            {
                get
                {
                    return _endpointStatistics.IsMachineRegistrationSupported ?? false;
                }
            }

            readonly ApiEndpointStatistics _endpointStatistics;

            ReadOnlyApiEndpointStatistics(ApiEndpointStatistics statistics)
            {
                _endpointStatistics = statistics;
            }
        }
        class ApiEndpointStatistics
        {
            public required ApiEndpoint Endpoint { get; init; }

            public bool? IsMachineRegistered { get; set; }
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
                    }
                    Endpoint.RuntimeConfig.IsLoggedIn = value ?? false;
                    _isUserLoggedIn = value;
                }
            }
            public bool? IsMachineRegistrationSupported { get; set; }

            bool? _isUserLoggedIn;

            readonly AsyncLock _lock = new ();

            public IDisposable Lock()
            {
                return _lock.Lock();
            }
            public AwaitableDisposable<IDisposable> LockAsync(CancellationToken token = default)
            {
                return _lock.LockAsync(token);
            }
        }
        readonly struct NetMachineInfo
        {
            public string Id { get; init; }
            public string Name { get; init; }
            public string Description { get; init; }
            public string Place { get; init; }
        }
        class CachedApiEndpointResponse
        {
            public CachedInteractResponse Interact { get; init; } = new();
            public CachedScoreInfoResponse ScoreInfo { get; init; } = new();
        }
        class CachedInteractResponse
        {
            public MajNetSongInteract Response { get; set; }
            public AsyncLock RequestLock { get; init; } = new ();
            public DateTime LastActive { get; set; }
        }
        class CachedScoreInfoResponse
        {
            public MajNetSongScoreInfo Response { get; set; }
            public AsyncLock RequestLock { get; init; } = new();
            public DateTime LastActive { get; set; }
        }
    }
}