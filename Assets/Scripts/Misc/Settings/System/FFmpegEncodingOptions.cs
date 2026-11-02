using MajdataPlay.FFmpeg;
using MajdataPlay.Settings.Runtime;
using System;
using System.Collections.Generic;
using System.Text;

namespace MajdataPlay.Settings
{
    public class FFmpegEncodingOptions
    {
        public FFmpegCodecPreference PreferredEncoder { get; set; } = SettingDefaults.FFmpegEncoding.PreferredEncoder;
        [NoTranslation(TranslationTarget.OptionValue)]
        public VideoEncodingFormat EncodingFormat { get; set; } = SettingDefaults.FFmpegEncoding.EncodingFormat;
        [Step("30")]
        [Range("30", "240")]
        public int FrameRate { get; set; } = SettingDefaults.FFmpegEncoding.FrameRate;
        [NoTranslation(TranslationTarget.OptionValue)]
        public VideoRateControlMode RateControlMode { get; set; } = SettingDefaults.FFmpegEncoding.RateControlMode;
        [FoldNumber]
        [Step("50000")]
        [Range("100000", null)]
        public long BitRate { get; set; } = SettingDefaults.FFmpegEncoding.BitRate;
        [FoldNumber]
        [Step("50000")]
        [Range("100000", null)]
        [ShowInSettingUIWhen(nameof(RateControlMode), VideoRateControlMode.VBR)]
        public long MaximumBitRate { get; set; } = SettingDefaults.FFmpegEncoding.MaximumBitRate;
        [Step("0.25")]
        [Range("0.25", "1")]
        public float ResolutionScale { get; set; } = SettingDefaults.FFmpegEncoding.ResolutionScale;
        [HideInSettingUI]
        [NoTranslation(TranslationTarget.OptionValue)]
        public MediaContainerFormat ContainerFormat { get; set; } = SettingDefaults.FFmpegEncoding.ContainerFormat;
    }
}
