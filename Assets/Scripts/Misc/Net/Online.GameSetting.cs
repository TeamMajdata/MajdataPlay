using Cysharp.Threading.Tasks;
using MajdataPlay.Diagnostics;
using System;
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
        /// <summary>Accesses the user settings document of the logged-in account, <c>account/gamesetting</c>.</summary>
        /// <remarks>
        /// The server stores one document per account and never interprets it, so the text is exchanged exactly as the
        /// game writes it into settings.json. The endpoint answers with the document text as <c>text/plain</c>, and with
        /// an empty object when the account has no copy yet.
        /// </remarks>
        public static class GameSetting
        {
            /// <summary>The route of the user settings document of the logged-in user.</summary>
            private const string API_ACCOUNT_GAME_SETTING = "account/gamesetting";

            /// <summary>Reads the settings document of the account, retrying requests that may succeed later.</summary>
            /// <param name="apiEndpoint">The endpoint owning the account.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>
            /// The document text, an empty document when the account has no copy yet, or null when the user is not logged
            /// in or every attempt failed. The request never throws.
            /// </returns>
            public static async ValueTask<string?> GetRawAsync(ApiEndpoint apiEndpoint, CancellationToken token = default)
            {
                await UniTask.SwitchToThreadPool();
                var statistic = GetApiEndpointStatistic(apiEndpoint);
                if (statistic.IsUserLoggedIn is not true)
                {
                    MajDebug.LogWarning(LOG_TAG, "[Online] No user settings are fetched while the user is not logged in");
                    return null;
                }
                var url = apiEndpoint.Url.Combine(API_ACCOUNT_GAME_SETTING);

                for (var i = 0; i < MajEnv.HttpRequestMaxRetry; i++)
                {
                    var rsp = await GetAsync(url, token);
                    if (rsp.StatusCode is HttpStatusCode.OK)
                    {
                        var json = Encoding.UTF8.GetString(rsp.AsSpan());
                        MajDebug.LogDebug(LOG_TAG, $"[Online] Fetched the user settings ({json.Length} chars) from {url}");
                        return json;
                    }
                    if (rsp.StatusCode is HttpStatusCode.Unauthorized)
                    {
                        using (await statistic.LockAsync(token))
                        {
                            statistic.IsUserLoggedIn = false;
                        }
                        MajDebug.LogError(LOG_TAG, $"[Online] Failed to get the user settings: the user is not logged in ({url})");
                        return null;
                    }
                    MajDebug.LogError(LOG_TAG, $"[Online] Failed to get the user settings of {url}: {rsp.StatusCode} - {rsp.ErrorCode}");
                    if (rsp.ErrorCode == HttpErrorCode.Canceled || IsClientError(rsp.StatusCode))
                    {
                        break;
                    }
                }

                return null;
            }

            /// <summary>Stores the settings document of the account, retrying requests that may succeed later.</summary>
            /// <param name="apiEndpoint">The endpoint owning the account.</param>
            /// <param name="json">The document text, which the server does not interpret.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>Whether the server accepted the document. The request never throws.</returns>
            public static async ValueTask<bool> PostRawAsync(ApiEndpoint apiEndpoint, string json, CancellationToken token = default)
            {
                await UniTask.SwitchToThreadPool();
                if (string.IsNullOrEmpty(json))
                {
                    MajDebug.LogError(LOG_TAG, "[Online] An empty user settings document is not stored");
                    return false;
                }
                var statistic = GetApiEndpointStatistic(apiEndpoint);
                if (statistic.IsUserLoggedIn is not true)
                {
                    MajDebug.LogWarning(LOG_TAG, "[Online] The user settings are not stored while the user is not logged in");
                    return false;
                }
                var url = apiEndpoint.Url.Combine(API_ACCOUNT_GAME_SETTING);

                for (var i = 0; i < MajEnv.HttpRequestMaxRetry; i++)
                {
                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var rsp = await PostAsync(url, content, token);
                    if (rsp.StatusCode is HttpStatusCode.OK)
                    {
                        MajDebug.LogInfo(LOG_TAG, $"[Online] Stored the user settings ({json.Length} chars) to {url}");
                        return true;
                    }
                    if (rsp.StatusCode is HttpStatusCode.Unauthorized)
                    {
                        using (await statistic.LockAsync(token))
                        {
                            statistic.IsUserLoggedIn = false;
                        }
                        MajDebug.LogError(LOG_TAG, $"[Online] Failed to store the user settings: the user is not logged in ({url})");
                        return false;
                    }
                    MajDebug.LogError(LOG_TAG, $"[Online] Failed to store the user settings to {url}: {rsp.StatusCode} - {rsp.ErrorCode}");
                    if (rsp.ErrorCode == HttpErrorCode.Canceled || IsClientError(rsp.StatusCode))
                    {
                        break;
                    }
                }

                return false;
            }

            /// <summary>Checks whether a status code reports a request the server refuses whatever the retry.</summary>
            /// <param name="statusCode">The status code of a response, or null for a transport failure.</param>
            /// <returns>True for a status code from 400 to 499.</returns>
            private static bool IsClientError(HttpStatusCode? statusCode)
            {
                return statusCode is not null && (int)statusCode.Value >= 400 && (int)statusCode.Value <= 499;
            }
        }
    }
}
