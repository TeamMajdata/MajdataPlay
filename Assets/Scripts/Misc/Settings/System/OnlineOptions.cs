using MajdataPlay.Net;
#if UNITY_IOS
using Newtonsoft.Json;
#endif
#nullable enable
namespace MajdataPlay.Settings
{
    public class OnlineOptions
    {
#if UNITY_IOS
        [JsonIgnore]
#endif
        public bool Enable { get; set; } = false;

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

        public MachineInfo Machine { get; set; } = new()
        {
            Name = "MajdataPlay Client",
            Description = "MajdataPlay Client QR Code Authentication"
        };
    }
}
