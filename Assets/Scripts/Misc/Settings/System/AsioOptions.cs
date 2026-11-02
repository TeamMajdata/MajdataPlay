#nullable enable
namespace MajdataPlay.Settings
{
    public class AsioOptions
    {
        public int DeviceIndex { get; set; } = SettingDefaults.Asio.DeviceIndex;

        public int SampleRate { get; set; } = SettingDefaults.Asio.SampleRate;
    }
}
