#nullable enable
namespace MajdataPlay.Settings
{
    public static partial class SettingDefaults
    {
        public static class Led
        {
            public const bool Enable = true;

            public const float Brightness = 1.0f;

            public const int RefreshRateMs = 16;

            public const bool Throttler = true;
        }
    }
}
