using System;
using System.Collections.Generic;
using System.Text;

namespace MajdataPlay.Settings
{
    public enum FFmpegCodecPreference
    {
        /// <summary>
        /// Use the software decoder.
        /// </summary>
        Software,
        /// <summary>
        /// Use the hardware decoder.
        /// </summary>
        Hardware
    }
    public class FFmpegDecoderOptions
    {
        public FFmpegCodecPreference PreferredDecoder { get; set; } = FFmpegCodecPreference.Hardware;
        [Range("1", null)]
        public int IOTimeoutSeconds { get; set; } = 15;
    }
}
