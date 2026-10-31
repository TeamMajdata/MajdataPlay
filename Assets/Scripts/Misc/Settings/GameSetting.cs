using Newtonsoft.Json;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Contains every user setting stored in settings.json.
    /// </summary>
    /// <remarks>
    /// Each property is one setting category, and the declaration order is the category order of the setting UI.
    /// The classes only describe the current layout; documents written by older versions are upgraded by
    /// <see cref="GameSettingMigration"/> before they are deserialized.
    /// </remarks>
    public class GameSetting
    {
        /// <summary>
        /// Gets the gameplay options.
        /// </summary>
        public GameOptions Game { get; init; } = new();

        /// <summary>
        /// Gets the judgment timing options.
        /// </summary>
        public JudgeOptions Judge { get; init; } = new();

        /// <summary>
        /// Gets the display, judgment presentation and screen layout options.
        /// </summary>
        public DisplayOptions Display { get; init; } = new();

        /// <summary>
        /// Gets the rendering quality, frame pacing and output options.
        /// </summary>
        public RenderingOptions Rendering { get; init; } = new();

        /// <summary>
        /// Gets the audio volume and output backend options.
        /// </summary>
        public SoundOptions Audio { get; init; } = new();

        /// <summary>
        /// Gets the gameplay modifiers, which only last for the current session and are never saved.
        /// </summary>
        [JsonIgnore]
        public ModOptions Mod { get; init; } = new();

        [ShowInSettingUIWhen($"{nameof(Game)}.{nameof(GameOptions.RecordMode)}", RecordModeOption.FFmpeg)]
        public FFmpegEncodingOptions Encoding { get; init; } = new();

        public FFmpegDecodingOptions Decoding { get; init; } = new();

        /// <summary>
        /// Gets the diagnostics and debugging options.
        /// </summary>
        public DebugOptions Debug { get; init; } = new();

        /// <summary>
        /// Gets the online service options, which are only edited in settings.json.
        /// </summary>
        [HideInSettingUI]
        public OnlineOptions Online { get; init; } = new();

        /// <summary>
        /// Gets the input and output device options, which are only edited in settings.json.
        /// </summary>
        [HideInSettingUI]
        public IOOptions IO { get; init; } = new();
    }
}
