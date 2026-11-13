using Cysharp.Threading.Tasks;
using MajdataPlay.Buffers;
using MajdataPlay.Diagnostics;
using MajdataPlay.Drawing;
using MajdataPlay.Net.Models;
using MajdataPlay.Utils;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
#nullable enable
namespace MajdataPlay.Net
{
    internal static partial class Online
    {
        /// <summary>
        /// Accesses the account APIs of a server, <c>account/*</c> and <c>avatar/*</c>: the login session and the profile,
        /// scores, favorite collections and avatar of the logged-in user.
        /// </summary>
        public static class Account
        {
            /// <summary>The route of the profile of the logged-in user.</summary>
            private const string API_GET_PROFILE = "account/profile";
            /// <summary>The route of the best scores of the logged-in user.</summary>
            private const string API_GET_SCORES = "account/scores";
            /// <summary>The route of the favorite collections of the logged-in user.</summary>
            private const string API_GET_FAVORITE_COLLECTIONS = "account/favorite/collection/list";
            /// <summary>The route of an avatar image, formatted with the escaped avatar identifier.</summary>
            private const string API_GET_AVATAR = "avatar/{0}";
            /// <summary>The route that logs a user in.</summary>
            private const string API_POST_LOGIN = "account/login";
            /// <summary>The route that ends the login session.</summary>
            private const string API_POST_LOGOUT = "account/logout";

            /// <summary>The <see cref="NetStatus.Code"/> of a 403 login response whose session cookie is still valid.</summary>
            private const int CODE_ALREADY_LOGGED_IN = 0;
            /// <summary>The <see cref="NetStatus.Code"/> of a 403 login response for an account whose email is not verified.</summary>
            private const int CODE_LOGIN_FAILED_PENDING_VERIFICATION = 11;
            /// <summary>The <see cref="NetStatus.Code"/> of a 403 login response for a banned account.</summary>
            private const int CODE_LOGIN_FAILED_USER_BANNED = 12;

            /// <summary>
            /// Logs a user in with <c>POST account/login</c>, sending the MD5 digest of the password. A user already
            /// logged in on the endpoint is not logged in again.
            /// </summary>
            /// <param name="apiEndpoint">The endpoint to log in to.</param>
            /// <param name="username">The username.</param>
            /// <param name="password">The password in plain text; only its digest leaves the machine.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>
            /// The response. <see cref="EndpointResponse.Message"/> holds the i18n key of the failure: unset credentials,
            /// a wrong password, an account pending verification, a banned account or another refusal.
            /// </returns>
            /// <exception cref="ArgumentNullException"><paramref name="apiEndpoint"/> is null.</exception>
            public static async ValueTask<EndpointResponse> LoginAsync(ApiEndpoint apiEndpoint, string username, string password, CancellationToken token = default)
            {
                await UniTask.SwitchToThreadPool();
                if (apiEndpoint == null)
                {
                    throw new ArgumentNullException(nameof(apiEndpoint));
                }
                var uri = apiEndpoint.Url.Combine(API_POST_LOGIN);
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
                    rsp = WithMessage(rsp, "MAJTEXT_ONLINE_USERNAME_OR_PASSWORD_INCORRECT");
                }
                else if (rsp.StatusCode is HttpStatusCode.Forbidden)
                {
                    var code = Serializer.Json.TryDeserialize<NetStatus>(Encoding.UTF8.GetString(rsp.AsSpan()), out var status, out _, _defaultJsonSerializerSettings)
                        ? status.Code
                        : null;
                    if (code == CODE_ALREADY_LOGGED_IN)
                    {
                        // The session cookie is still valid on the server
                        using (await statistic.LockAsync())
                        {
                            statistic.IsUserLoggedIn = true;
                        }
                        return new()
                        {
                            Endpoint = uri,
                            IsSuccessfully = true,
                            IsDeserializable = false,
                            StatusCode = HttpStatusCode.OK,
                            ErrorCode = HttpErrorCode.NoError,
                            Headers = rsp.Headers,
                            Message = string.Empty
                        };
                    }
                    rsp = WithMessage(rsp, code switch
                    {
                        CODE_LOGIN_FAILED_PENDING_VERIFICATION => "MAJTEXT_ONLINE_ACCOUNT_PENDING_VERIFICATION",
                        CODE_LOGIN_FAILED_USER_BANNED => "MAJTEXT_ONLINE_ACCOUNT_BANNED",
                        _ => "MAJTEXT_ONLINE_ACCESS_FORBIDDEN"
                    });
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
            /// <summary>
            /// Ends the login session on every endpoint that was accessed with <c>POST account/logout</c>, and restores the
            /// configured credentials of the endpoints. The local state is reset even when a request fails.
            /// </summary>
            /// <param name="token">The token canceling the requests.</param>
            /// <returns>A task that completes when every endpoint was logged out.</returns>
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
                            MajDebug.LogInfo(LOG_TAG, "[Online] Logout");
                            var uri = apiEndpoint.Url.Combine(API_POST_LOGOUT);
                            await PostAsync(uri, token);
                        }
                        catch (Exception e)
                        {
                            MajDebug.LogException(LOG_TAG, e);
                        }
                        finally
                        {
                            statistics.IsUserLoggedIn = false;
                            apiEndpoint.RuntimeConfig.AuthMethod = NetAuthMethodOption.None;
                            apiEndpoint.RuntimeConfig.Avatar = null;
                            apiEndpoint.RuntimeConfig.Username = "???";
                            apiEndpoint.RuntimeConfig.Nickname = "???";
                            apiEndpoint.RuntimeConfig.AuthUsername = apiEndpoint.Username;
                            apiEndpoint.RuntimeConfig.AuthPassword = apiEndpoint.Password;
                        }
                    }
                }
            }
            /// <summary>Fetches the profile of the logged-in user from <c>GET account/profile</c> and records whether the user is still logged in.</summary>
            /// <param name="apiEndpoint">The endpoint.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>The response; status 401 means the user is not logged in.</returns>
            public static async ValueTask<EndpointResponse<UserProfile>> GetProfileAsync(ApiEndpoint apiEndpoint, CancellationToken token = default)
            {
                await UniTask.SwitchToThreadPool();
                var statistic = GetApiEndpointStatistic(apiEndpoint);
                var rsp = await RequestProfileAsync(apiEndpoint, token);
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
            /// <summary>Fetches the best scores of the logged-in user from <c>GET account/scores</c>.</summary>
            /// <param name="apiEndpoint">The endpoint.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>The scores; empty when the user is not logged in or every attempt failed.</returns>
            public static async ValueTask<MajNetAccountSongScore[]> GetScoresAsync(ApiEndpoint apiEndpoint, CancellationToken token = default)
            {
                await UniTask.SwitchToThreadPool();
                try
                {
                    var uri = apiEndpoint.Url.Combine(API_GET_SCORES);
                    for (var i = 0; i <= MajEnv.HttpRequestMaxRetry; i++)
                    {
                        var rsp = await GetAsync(uri, token);
                        if (rsp.StatusCode is HttpStatusCode.Unauthorized)
                        {
                            return Array.Empty<MajNetAccountSongScore>();
                        }
                        else if (!rsp.IsSuccessfully || !rsp.IsDeserializable)
                        {
                            MajDebug.LogError(LOG_TAG, $"[Online] Failed to get user scores\nUrl:{uri}\nStatusCode:{rsp.StatusCode}\nErrorCode:{rsp.ErrorCode}\nMessage:{rsp.Message}");
                            continue;
                        }
                        var userScores = await rsp.DeserializeAsync<MajNetAccountSongScore[]>() ?? Array.Empty<MajNetAccountSongScore>();
                        MajDebug.LogInfo(LOG_TAG, $"[Online] Fetched {userScores.Length} scores for the user from endpoint");
                        return userScores;
                    }
                    return Array.Empty<MajNetAccountSongScore>();
                }
                catch (Exception e)
                {
                    MajDebug.LogError(LOG_TAG, "[Online] Get user scores failed: ");
                    MajDebug.LogException(LOG_TAG, e);
                    return Array.Empty<MajNetAccountSongScore>();
                }
            }
            /// <summary>
            /// Fetches the favorite collections of the logged-in user from <c>GET account/favorite/collection/list</c>.
            /// No request is sent while the user is not logged in.
            /// </summary>
            /// <param name="apiEndpoint">The endpoint.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>The collections, or null when the user is not logged in or the request failed.</returns>
            public static async ValueTask<MajNetOnlineDanInfo[]?> GetFavoriteCollectionsAsync(ApiEndpoint apiEndpoint, CancellationToken token = default)
            {
                await UniTask.SwitchToThreadPool();
                var statistic = GetApiEndpointStatistic(apiEndpoint);
                if (statistic.IsUserLoggedIn is not true)
                {
                    return null;
                }
                var url = apiEndpoint.Url.Combine(API_GET_FAVORITE_COLLECTIONS);

                for (var i = 0; i < MajEnv.HttpRequestMaxRetry; i++)
                {
                    var rsp = await GetAsync(url, token);
                    var e = default(Exception?);
                    if (rsp.IsSuccessfully && rsp.TryDeserialize<MajNetOnlineDanInfo[]>(out var danList, out e) && danList is not null)
                    {
                        MajDebug.LogDebug(LOG_TAG, rsp);
                        return danList;
                    }
                    else
                    {
                        MajDebug.LogError(LOG_TAG, $"[Online] Failed to get favorite collections: {e?.Message ?? "Unknown error"}");
                    }
                    if (rsp.ErrorCode == HttpErrorCode.Canceled)
                    {
                        break;
                    }
                    else if (rsp.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        MajDebug.LogError(LOG_TAG, "[Online] Failed to get favorite collections: user not logged in");
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
            /// <summary>Downloads an avatar image from <c>GET avatar/{avatarId}</c>.</summary>
            /// <param name="apiEndpoint">The endpoint.</param>
            /// <param name="avatarId">The <see cref="UserProfile.AvatarId"/> of the user; null or empty when the user has no avatar.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <param name="onError">Called on the main thread after a failed attempt, with the i18n text of the failure and whether it is shown as an error.</param>
            /// <returns>The avatar, or null when the user has no avatar or every attempt failed.</returns>
            public static async ValueTask<Sprite?> GetAvatarAsync(ApiEndpoint apiEndpoint, string? avatarId, CancellationToken token = default, Action<string, bool>? onError = null)
            {
                if (string.IsNullOrEmpty(avatarId))
                {
                    return null;
                }
                await UniTask.SwitchToThreadPool();
                var url = apiEndpoint.Url.Combine(string.Format(API_GET_AVATAR, Uri.EscapeDataString(avatarId)));
                for (var i = 0; i <= MajEnv.HttpRequestMaxRetry; i++)
                {
                    try
                    {
                        var rsp = await GetAsync(url, token);
                        if (!rsp.IsSuccessfully)
                        {
                            MajDebug.LogError(LOG_TAG, "[Online] Failed to download user avatar");
                            if (rsp.StatusCode is HttpStatusCode.NotFound || rsp.ErrorCode is HttpErrorCode.Canceled)
                            {
                                return default;
                            }
                            await UniTask.SwitchToMainThread();
                            onError?.Invoke("MAJTEXT_LOGIN_DOWNLOAD_AVATAR_FAILED_RETRIED_TIMES_{0}/{1}".i18n(i + 1, MajEnv.HttpRequestMaxRetry + 1), true);
                            await UniTask.SwitchToThreadPool();
                            continue;
                        }
                        var avatar = await SpriteLoader.LoadFromMemoryAsync(rsp.AsMemory());
                        return avatar;
                    }
                    catch (Exception e)
                    {
                        MajDebug.LogError(LOG_TAG, "[Online] Failed to download user avatar");
                        MajDebug.LogException(LOG_TAG, e);
                    }
                }
                return default;
            }
            /// <summary>
            /// Fetches the profile of the logged-in user from <c>GET account/profile</c>, retrying failures other than
            /// status 401, without recording the login state; the caller holds or updates it.
            /// </summary>
            /// <param name="apiEndpoint">The endpoint.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>The response of the last attempt.</returns>
            internal static async ValueTask<EndpointResponse<UserProfile>> RequestProfileAsync(ApiEndpoint apiEndpoint, CancellationToken token = default)
            {
                var uri = apiEndpoint.Url.Combine(API_GET_PROFILE);
                var rsp = default(EndpointResponse);
                for (var i = 0; i <= MajEnv.HttpRequestMaxRetry; i++)
                {
                    rsp = await GetAsync(uri, token);
                    if (rsp.StatusCode is HttpStatusCode.Unauthorized)
                    {
                        return new EndpointResponse<UserProfile>(rsp);
                    }
                    else if (!rsp.IsSuccessfully || !rsp.IsDeserializable)
                    {
                        MajDebug.LogError(LOG_TAG, "[Online] Failed to get user profile");
                        continue;
                    }
                    return new EndpointResponse<UserProfile>(rsp);
                }
                return new EndpointResponse<UserProfile>(rsp);
            }
            /// <summary>Copies a response with another message, keeping its body readable.</summary>
            /// <param name="rsp">The response.</param>
            /// <param name="message">The i18n key of the message.</param>
            /// <returns>The copy.</returns>
            private static EndpointResponse WithMessage(EndpointResponse rsp, string message)
            {
                return new(rsp.AsMemory(), _defaultJsonSerializer, _defaultJsonSerializerSettings)
                {
                    Endpoint = rsp.Endpoint,
                    StatusCode = rsp.StatusCode,
                    ErrorCode = rsp.ErrorCode,
                    IsSuccessfully = rsp.IsSuccessfully,
                    IsDeserializable = rsp.IsDeserializable,
                    Headers = rsp.Headers,
                    Message = message
                };
            }
        }
    }
}
