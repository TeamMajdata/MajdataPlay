using Newtonsoft.Json;
#nullable enable
namespace MajdataPlay.Settings
{
    public class SystemSetting
    {
        public RenderingOptions Rendering { get; init; } = new();

        [HideInSettingUI]
        public SoundOptions Audio { get; init; } = new();

        [JsonIgnore]
        public ModOptions Mod { get; init; } = new();

        [ShowInSettingUIWhen($"{nameof(SettingRoot.User)}.{nameof(UserSetting.Game)}.{nameof(GameOptions.RecordMode)}", RecordModeOption.FFmpeg)]
        public FFmpegEncodingOptions Encoding { get; init; } = new();

        public FFmpegDecodingOptions Decoding { get; init; } = new();

        public DebugOptions Debug { get; init; } = new();

        [HideInSettingUI]
        public OnlineOptions Online { get; init; } = new();

        [HideInSettingUI]
        public IOOptions IO { get; init; } = new();
    }
}
