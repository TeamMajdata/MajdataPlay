#nullable enable
namespace MajdataPlay.Settings
{
    public static partial class SettingDefaults
    {
        public static class FFmpegDecoding
        {
            public const FFmpegCodecPreference PreferredDecoder = FFmpegCodecPreference.Hardware;

            public const int IOTimeoutSeconds = 15;
        }
    }
}
