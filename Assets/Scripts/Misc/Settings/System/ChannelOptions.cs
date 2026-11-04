#nullable enable
namespace MajdataPlay.Settings
{
    public class ChannelOptions
    {
        public float FrontVolume { get; set; } = SettingDefaults.Channel.FrontVolume;

        public float CenterAndLFEVolume { get; set; } = SettingDefaults.Channel.CenterAndLFEVolume;

        public float SideVolume { get; set; } = SettingDefaults.Channel.SideVolume;

        public float RearVolume { get; set; } = SettingDefaults.Channel.RearVolume;
    }
}
