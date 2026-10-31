using MajdataPlay.Net;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Contains the online service options.
    /// </summary>
    public class OnlineOptions
    {
        /// <summary>
        /// Gets or sets whether the online services are enabled; on iOS the native settings decide and the value is not saved.
        /// </summary>
#if UNITY_IOS
        [JsonIgnore]
#endif
        public bool Enable { get; set; } = false;
#if UNITY_STANDALONE

        /// <summary>
        /// Gets whether HTTP requests use a proxy: <see cref="Proxy"/> when it is set, otherwise the system proxy.
        /// </summary>
        public bool UseProxy { get; init; } = true;

        /// <summary>
        /// Gets the absolute HTTP or HTTPS proxy URL, optionally with <c>user:password</c> credentials; empty uses the system proxy.
        /// </summary>
        public string Proxy { get; init; } = string.Empty;
#endif

        /// <summary>
        /// Gets or sets the online service endpoints.
        /// </summary>
        public ApiEndpoint[] ApiEndpoints { get; set; } = new ApiEndpoint[]
        {
#if !UNITY_IOS
            new ApiEndpoint()
            {
                Name = "MajdataNET",
                Url = new("https://majdata.net/api3/api/"),
                Username = "YourUsername",
                Password = "YourPassword"
            }
#endif
        };
    }
}
