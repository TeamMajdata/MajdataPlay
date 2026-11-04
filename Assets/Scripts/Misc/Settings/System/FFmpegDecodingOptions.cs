using System;
using System.Collections.Generic;
using System.Text;

namespace MajdataPlay.Settings
{
    public enum FFmpegCodecPreference
    {
        Software,
        Hardware
    }
    public class FFmpegDecodingOptions
    {
        public FFmpegCodecPreference PreferredDecoder { get; set; } = SettingDefaults.FFmpegDecoding.PreferredDecoder;
        [Range("1", null)]
        public int IOTimeoutSeconds { get; set; } = SettingDefaults.FFmpegDecoding.IOTimeoutSeconds;
    }
}
