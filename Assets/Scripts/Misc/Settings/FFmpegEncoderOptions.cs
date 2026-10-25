using MajdataPlay.FFmpeg;
using System;
using System.Collections.Generic;
using System.Text;

namespace MajdataPlay.Settings
{
    public class FFmpegEncoderOptions
    {
        public FFmpegCodecPreference PreferredEncoder { get; set; } = FFmpegCodecPreference.Hardware;
        public VideoEncodingFormat EncodingFormat { get; set; } = VideoEncodingFormat.H264;
        [Step("30")]
        [Range("1", "240", HasMax = true, HasMin = true)]
        public int FrameRate { get; set; } = 60;
        public VideoRateControlMode RateControlMode { get; set; } = VideoRateControlMode.VBR;
        public long BitRate { get; set; } = 8000000;
        [ShowInSettingUIWhen(nameof(RateControlMode), VideoRateControlMode.VBR)]
        public long MaximumBitRate { get; set; } = 16000000;
        [HideInSettingUI]
        public MediaContainerFormat ContainerFormat { get; set; } = MediaContainerFormat.MP4;
    }
}
