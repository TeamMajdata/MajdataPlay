#nullable enable
namespace MajdataPlay.Settings
{
    public static partial class SettingDefaults
    {
        public static class Wasapi
        {
            public const bool Exclusive = true;

            public const bool RawMode = true;

            public const bool AsyncMode = true;

            public const float BufferSize = 0.02f;

            public const float Period = 0.005f;
        }
    }
}
