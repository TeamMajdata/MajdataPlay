using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace MajdataPlay.Settings.System
{
    internal class SystemSetting
    {
        public RenderingOptions Rendering { get; init; } = new();

        public SoundOptions Audio { get; init; } = new();

        [JsonIgnore]
        public ModOptions Mod { get; init; } = new();

        [ShowInSettingUIWhen($"{nameof(Game)}.{nameof(GameOptions.RecordMode)}", RecordModeOption.FFmpeg)]
        public FFmpegEncodingOptions Encoding { get; init; } = new();

        public FFmpegDecodingOptions Decoding { get; init; } = new();

        public DebugOptions Debug { get; init; } = new();

        [HideInSettingUI]
        public OnlineOptions Online { get; init; } = new();

        [HideInSettingUI]
        public IOOptions IO { get; init; } = new();
    }
}
