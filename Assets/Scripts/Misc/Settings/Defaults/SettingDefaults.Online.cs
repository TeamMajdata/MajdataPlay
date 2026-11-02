using MajdataPlay.Net;
using MajdataPlay.Net.Models;
#nullable enable
namespace MajdataPlay.Settings
{
    public static partial class SettingDefaults
    {
        public static class Online
        {
            public const bool Enable = false;

            public static ApiEndpoint[] ApiEndpoints
            {
                get
                {
                    return new ApiEndpoint[]
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

            public static MachineInfo Machine
            {
                get
                {
                    return new()
                    {
                        Name = "MajdataPlay Client",
                        Description = "MajdataPlay Client QR Code Authentication"
                    };
                }
            }
        }
    }
}
