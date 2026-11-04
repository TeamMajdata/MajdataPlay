using Cysharp.Threading.Tasks;
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
        /// Accesses the machine APIs of a server, <c>machine/*</c>: the registration of this machine and the QR code
        /// authorization, in which a user logged in on the website lets this machine use their account.
        /// </summary>
        public static class Machine
        {
            /// <summary>The route of the registration of this machine.</summary>
            private const string API_GET_INFO = "machine/info";
            /// <summary>The route that registers this machine.</summary>
            private const string API_POST_REGISTER = "machine/register";
            /// <summary>The route that starts an authorization.</summary>
            private const string API_POST_AUTH_REQUEST = "machine/auth/request";
            /// <summary>The route of the state of an authorization.</summary>
            private const string API_GET_AUTH_CHECK = "machine/auth/check";
            /// <summary>The route that withdraws an authorization.</summary>
            private const string API_POST_AUTH_REVOKE = "machine/auth/revoke";

            /// <summary>Gets whether this machine is known to be registered on an endpoint, without a request.</summary>
            /// <param name="apiEndpoint">The endpoint.</param>
            /// <returns>True when the last registration or check succeeded; false while it is unknown.</returns>
            public static bool IsRegistered(ApiEndpoint apiEndpoint)
            {
                return FindApiEndpointStatistic(apiEndpoint)?.IsMachineRegistered ?? false;
            }
            /// <summary>Checks with <c>GET machine/info</c> whether this machine is still registered on an endpoint.</summary>
            /// <param name="apiEndpoint">The endpoint.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>True when the server answered successfully.</returns>
            public static async ValueTask<bool> CheckRegistrationAsync(ApiEndpoint apiEndpoint, CancellationToken token = default)
            {
                await UniTask.SwitchToThreadPool();
                var uri = apiEndpoint.Url.Combine(API_GET_INFO);
                var rsp = await GetAsync(uri, token);

                return rsp.IsSuccessfully;
            }
            /// <summary>
            /// Registers this machine on an endpoint with <c>POST machine/register</c>. A server answering 404 does not
            /// provide machine registration, which is remembered so later calls return without a request.
            /// </summary>
            /// <param name="apiEndpoint">The endpoint.</param>
            /// <param name="machineInfo">The description of this machine.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>The response; <see cref="HttpErrorCode.NotSupported"/> when the server does not provide machine registration.</returns>
            public static async ValueTask<EndpointResponse> RegisterAsync(ApiEndpoint apiEndpoint, MachineInfo machineInfo, CancellationToken token = default)
            {
                var statistics = GetApiEndpointStatistic(apiEndpoint);
                if (statistics.IsMachineRegistrationSupported is false)
                {
                    return CreateRegistrationUnsupportedResponse(apiEndpoint);
                }
                using (await statistics.LockAsync())
                {
                    if (statistics.IsMachineRegistrationSupported is false)
                    {
                        return CreateRegistrationUnsupportedResponse(apiEndpoint);
                    }
                    var uri = apiEndpoint.Url.Combine(API_POST_REGISTER);
                    var json = await SerializeJsonAsync(machineInfo);

                    var rsp = await PostAsync(uri, CreateJsonContent(json), token);
                    if (!rsp.IsSuccessfully && rsp.StatusCode == HttpStatusCode.NotFound)
                    {
                        statistics.IsMachineRegistrationSupported = false;
                        statistics.IsMachineRegistered = false;
                        return CreateRegistrationUnsupportedResponse(apiEndpoint);
                    }
                    statistics.IsMachineRegistrationSupported = true;
                    statistics.IsMachineRegistered = rsp.IsSuccessfully;
                    return rsp;
                }
            }
            /// <summary>Starts an authorization with <c>POST machine/auth/request</c>.</summary>
            /// <param name="apiEndpoint">The endpoint this machine was registered on.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>
            /// The response, deserializable to the authorization on status 201; <see cref="HttpErrorCode.NotSupported"/>
            /// when the server does not provide machine registration.
            /// </returns>
            /// <exception cref="InvalidOperationException">This machine was not registered on the endpoint yet.</exception>
            public static async ValueTask<EndpointResponse> AuthRequestAsync(ApiEndpoint apiEndpoint, CancellationToken token = default)
            {
                if (!IsRegistrationSupported(apiEndpoint))
                {
                    return CreateRegistrationUnsupportedResponse(apiEndpoint);
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
            /// <summary>Reads the state of an authorization with <c>GET machine/auth/check</c>.</summary>
            /// <param name="apiEndpoint">The endpoint this machine was registered on.</param>
            /// <param name="authId">The identifier of the authorization returned by <see cref="AuthRequestAsync"/>.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>The response; <see cref="HttpErrorCode.NotSupported"/> when the server does not provide machine registration.</returns>
            /// <exception cref="InvalidOperationException">This machine was not registered on the endpoint yet.</exception>
            public static async ValueTask<EndpointResponse> AuthCheckAsync(ApiEndpoint apiEndpoint, string authId, CancellationToken token = default)
            {
                if (!IsRegistrationSupported(apiEndpoint))
                {
                    return CreateRegistrationUnsupportedResponse(apiEndpoint);
                }
                var uri = BuildAuthUri(apiEndpoint, API_GET_AUTH_CHECK, authId);
                return await GetAsync(uri, token);
            }
            /// <summary>Withdraws an authorization with <c>POST machine/auth/revoke</c>.</summary>
            /// <param name="apiEndpoint">The endpoint this machine was registered on.</param>
            /// <param name="authId">The identifier of the authorization returned by <see cref="AuthRequestAsync"/>.</param>
            /// <param name="token">The token canceling the request.</param>
            /// <returns>The response; <see cref="HttpErrorCode.NotSupported"/> when the server does not provide machine registration.</returns>
            /// <exception cref="InvalidOperationException">This machine was not registered on the endpoint yet.</exception>
            public static async ValueTask<EndpointResponse> AuthRevokeAsync(ApiEndpoint apiEndpoint, string authId, CancellationToken token = default)
            {
                if (!IsRegistrationSupported(apiEndpoint))
                {
                    return CreateRegistrationUnsupportedResponse(apiEndpoint);
                }
                var uri = BuildAuthUri(apiEndpoint, API_POST_AUTH_REVOKE, authId);
                return await PostAsync(uri, token);
            }
            /// <summary>Gets whether an endpoint provides machine registration, which the authorization APIs require.</summary>
            /// <param name="apiEndpoint">The endpoint.</param>
            /// <returns>True when the endpoint provides machine registration.</returns>
            /// <exception cref="InvalidOperationException">This machine was not registered on the endpoint yet, so the support is unknown.</exception>
            private static bool IsRegistrationSupported(ApiEndpoint apiEndpoint)
            {
                return GetApiEndpointStatistic(apiEndpoint).IsMachineRegistrationSupported
                    ?? throw new InvalidOperationException();
            }
            /// <summary>Builds the URI of an authorization API.</summary>
            /// <param name="apiEndpoint">The endpoint.</param>
            /// <param name="route">The route of the API.</param>
            /// <param name="authId">The identifier of the authorization.</param>
            /// <returns>The URI, with the identifier in the <c>auth-id</c> query.</returns>
            private static Uri BuildAuthUri(ApiEndpoint apiEndpoint, string route, string authId)
            {
                var uriBuilder = new UriBuilder(apiEndpoint.Url.Combine(route));
                uriBuilder.Query = $"auth-id={authId}";
                return uriBuilder.Uri;
            }
            /// <summary>Creates the response of an endpoint that does not provide machine registration.</summary>
            /// <param name="apiEndpoint">The endpoint.</param>
            /// <returns>An unsuccessful response with status 404 and <see cref="HttpErrorCode.NotSupported"/>.</returns>
            private static EndpointResponse CreateRegistrationUnsupportedResponse(ApiEndpoint apiEndpoint)
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
        }
    }
}
