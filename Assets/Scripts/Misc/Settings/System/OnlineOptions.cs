using MajdataPlay.Net;
using MajdataPlay.Net.Models;
#nullable enable
namespace MajdataPlay.Settings
{
    public class OnlineOptions
    {
        [JsonIgnoreOnPlatform(SettingPlatform.IOS)]
        public bool Enable { get; set; } = SettingDefaults.Online.Enable;

        public ApiEndpoint[] ApiEndpoints { get; set; } = SettingDefaults.Online.ApiEndpoints;

        public MachineInfo Machine { get; set; } = SettingDefaults.Online.Machine;
    }
}
