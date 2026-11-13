using MajdataPlay.Net;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay.Settings.Sync
{
    /// <summary>Exchanges the user settings document through the online APIs of a MajdataNET endpoint.</summary>
    /// <remarks>The document travels as the text itself, because the server stores and returns it without interpreting it.</remarks>
    internal sealed class OnlineUserSettingApi : IUserSettingApi
    {
        /// <summary>Gets the instance used by the game; tests replace <see cref="UserSettingSync.Api"/> instead.</summary>
        internal static OnlineUserSettingApi Shared { get; } = new OnlineUserSettingApi();

        /// <inheritdoc />
        public ValueTask<string?> FetchAsync(ApiEndpoint endpoint, CancellationToken token)
        {
            return Online.GameSetting.GetRawAsync(endpoint, token);
        }

        /// <inheritdoc />
        public ValueTask<bool> StoreAsync(ApiEndpoint endpoint, string json, CancellationToken token)
        {
            return Online.GameSetting.PostRawAsync(endpoint, json, token);
        }
    }
}
