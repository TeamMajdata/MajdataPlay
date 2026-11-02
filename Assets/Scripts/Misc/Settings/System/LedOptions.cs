#nullable enable
namespace MajdataPlay.Settings
{
    public class LedOptions
    {
        public bool Enable { get; init; } = SettingDefaults.Led.Enable;

        public float Brightness { get; set; } = SettingDefaults.Led.Brightness;

        public int RefreshRateMs { get; set; } = SettingDefaults.Led.RefreshRateMs;

        public bool Throttler { get; set; } = SettingDefaults.Led.Throttler;

        public SerialPortOptions SerialPortOptions { get; set; } = new();

        public HidOptions HidOptions { get; set; } = new();
    }
}
