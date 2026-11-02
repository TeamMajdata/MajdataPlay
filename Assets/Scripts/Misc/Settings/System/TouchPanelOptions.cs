#nullable enable
namespace MajdataPlay.Settings
{
    public class TouchPanelOptions
    {
        public bool Enable { get; init; } = SettingDefaults.TouchPanel.Enable;

        public bool Debounce { get; set; } = SettingDefaults.TouchPanel.Debounce;

        public TouchPanelSensitivityConfig Sensitivities { get; set; } = SettingDefaults.TouchPanel.Sensitivities;

        public int PollingRateMs { get; set; } = SettingDefaults.TouchPanel.PollingRateMs;

        public int DebounceThresholdMs { get; set; } = SettingDefaults.TouchPanel.DebounceThresholdMs;

        public SerialPortOptions SerialPortOptions { get; set; } = new();

        public UsbOptions UsbOptions { get; set; } = new();

        public CapacitiveTouchPanelOptions CapacitivePanelOptions { get; set; } = new();
    }
}
