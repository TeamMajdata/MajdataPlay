#nullable enable
namespace MajdataPlay.Settings
{
    public class ButtonRingOptions
    {
        public bool Enable { get; init; } = SettingDefaults.ButtonRing.Enable;

        public ButtonRingDeviceOption? Type { get; set; } = SettingDefaults.ButtonRing.Type;

        public bool Debounce { get; set; } = SettingDefaults.ButtonRing.Debounce;

        public int PollingRateMs { get; set; } = SettingDefaults.ButtonRing.PollingRateMs;

        public int DebounceThresholdMs { get; set; } = SettingDefaults.ButtonRing.DebounceThresholdMs;

        public HidOptions HidOptions { get; set; } = new();
    }
}
