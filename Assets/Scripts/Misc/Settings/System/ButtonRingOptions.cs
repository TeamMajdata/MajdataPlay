#nullable enable
namespace MajdataPlay.Settings
{
    public class ButtonRingOptions
    {
        public bool Enable { get; init; } = true;

        public ButtonRingDeviceOption? Type { get; set; } = null;

        public bool Debounce { get; set; } = false;

        public int PollingRateMs { get; set; } = 0;

        public int DebounceThresholdMs { get; set; } = 0;

        public HidOptions HidOptions { get; set; } = new();
    }
}
