#nullable enable
namespace MajdataPlay.Settings
{
    public class LedOptions
    {
        public bool Enable { get; init; } = true;

        public float Brightness { get; set; } = 1.0f;

        public int RefreshRateMs { get; set; } = 16;

        public bool Throttler { get; set; } = true;

        public SerialPortOptions SerialPortOptions { get; set; } = new();

        public HidOptions HidOptions { get; set; } = new();
    }
}
