using MajdataPlay.FFmpeg;
using MajdataPlay.Settings.Runtime;
using System;
using System.Collections.Generic;
using System.Text;

namespace MajdataPlay.Settings
{
    public class FFmpegEncoderOptions
    {
        public FFmpegCodecPreference PreferredEncoder { get; set; } = FFmpegCodecPreference.Hardware;
        [NoTranslation(TranslationTarget.OptionValue)]
        public VideoEncodingFormat EncodingFormat { get; set; } = VideoEncodingFormat.H264;
        [Step("30")]
        [Range("30", "240")]
        public int FrameRate { get; set; } = 60;
        [NoTranslation(TranslationTarget.OptionValue)]
        public VideoRateControlMode RateControlMode { get; set; } = VideoRateControlMode.VBR;
        [FoldNumber]
        [Step("50000")] // 50K
        [Range("100000", null)] // 100K
        public long BitRate { get; set; } = 4000000;
        [FoldNumber]
        [Step("50000")] // 50K
        [Range("100000", null)] // 100K
        [ShowInSettingUIWhen(nameof(RateControlMode), VideoRateControlMode.VBR)]
        public long MaximumBitRate { get; set; } = 8000000;
        [Step("0.25")]
        [Range("0.25", "1")]
        public float ResolutionScale { get; set; } = 1f;
        [HideInSettingUI]
        [NoTranslation(TranslationTarget.OptionValue)]
        public MediaContainerFormat ContainerFormat { get; set; } = MediaContainerFormat.MP4;
    }
}
