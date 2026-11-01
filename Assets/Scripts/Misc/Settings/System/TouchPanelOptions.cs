#nullable enable
namespace MajdataPlay.Settings
{
    public class TouchPanelOptions
    {
        public bool Enable { get; init; } = true;

        public bool Debounce { get; set; } = false;

        public TouchPanelSensitivityConfig Sensitivities { get; set; } = default;

        public int PollingRateMs { get; set; } = 0;

        public int DebounceThresholdMs { get; set; } = 0;

        public SerialPortOptions SerialPortOptions { get; set; } = new();

        public UsbOptions UsbOptions { get; set; } = new();

        public CapacitiveTouchPanelOptions CapacitivePanelOptions { get; set; } = new();
    }
}
