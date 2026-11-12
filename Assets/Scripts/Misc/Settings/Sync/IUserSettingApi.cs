using MajdataPlay.Net;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay.Settings.Sync
{
    /// <summary>Exchanges the user settings document of an account with a server.</summary>
    /// <remarks>
    /// The server stores the document without interpreting it, so this interface exchanges the text itself and never a
    /// parsed object graph.
    /// </remarks>
    internal interface IUserSettingApi
    {
        /// <summary>Fetches the settings document of the user logged in on an endpoint.</summary>
        /// <param name="endpoint">The endpoint owning the account.</param>
        /// <param name="token">The token canceling the request.</param>
        /// <returns>
        /// The document text, an empty document when the account has no copy yet, or null when the request failed or the
        /// user is not logged in.
        /// </returns>
        ValueTask<string?> FetchAsync(ApiEndpoint endpoint, CancellationToken token);

        /// <summary>Stores the settings document of the user logged in on an endpoint.</summary>
        /// <param name="endpoint">The endpoint owning the account.</param>
        /// <param name="json">The document text to store, which the server does not interpret.</param>
        /// <param name="token">The token canceling the request.</param>
        /// <returns>Whether the server accepted the document.</returns>
        ValueTask<bool> StoreAsync(ApiEndpoint endpoint, string json, CancellationToken token);
    }
}
