#nullable enable
namespace MajdataPlay.Settings
{
    public static partial class SettingDefaults
    {
        public static class TouchPanel
        {
            public const bool Enable = true;

            public const bool Debounce = false;

            public static TouchPanelSensitivityConfig Sensitivities
            {
                get => default;
            }

            public const int PollingRateMs = 0;

            public const int DebounceThresholdMs = 0;
        }
    }
}
