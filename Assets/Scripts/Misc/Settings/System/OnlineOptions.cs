using MajdataPlay.Net;
#nullable enable
namespace MajdataPlay.Settings
{
    public class OnlineOptions
    {
#if UNITY_IOS
        [JsonIgnore]
#endif
        public bool Enable { get; set; } = false;
#if UNITY_STANDALONE

        public bool UseProxy { get; init; } = true;

        public string Proxy { get; init; } = string.Empty;
#endif

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
