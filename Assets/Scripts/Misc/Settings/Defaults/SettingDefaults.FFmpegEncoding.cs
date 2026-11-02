using MajdataPlay.FFmpeg;
#nullable enable
namespace MajdataPlay.Settings
{
    public static partial class SettingDefaults
    {
        public static class FFmpegEncoding
        {
            public const FFmpegCodecPreference PreferredEncoder = FFmpegCodecPreference.Hardware;

            public const VideoEncodingFormat EncodingFormat = VideoEncodingFormat.H264;

            public const int FrameRate = 60;

            public const VideoRateControlMode RateControlMode = VideoRateControlMode.VBR;

            public const long BitRate = 4000000;

            public const long MaximumBitRate = 8000000;

            public const float ResolutionScale = 1f;

            public const MediaContainerFormat ContainerFormat = MediaContainerFormat.MP4;
        }
    }
}
