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
        public FFmpegCodecPreference PreferredDecoder { get; set; } = FFmpegCodecPreference.Hardware;
        [Range("1", null)]
        public int IOTimeoutSeconds { get; set; } = 15;
    }
}
