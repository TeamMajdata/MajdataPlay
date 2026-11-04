#nullable enable
namespace MajdataPlay.Settings
{
    public static partial class SettingDefaults
    {
        public static class ButtonRing
        {
            public const bool Enable = true;

            public static ButtonRingDeviceOption? Type
            {
                get => null;
            }

            public const bool Debounce = false;

            public const int PollingRateMs = 0;

            public const int DebounceThresholdMs = 0;
        }
    }
}
