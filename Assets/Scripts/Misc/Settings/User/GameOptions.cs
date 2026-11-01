using MajdataPlay.Settings.OptionEnumerators;
using MajdataPlay.Settings.Runtime;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Contains the gameplay options.
    /// </summary>
    public class GameOptions
    {
#if UNITY_ANDROID || UNITY_IOS
        public const GameplaySubScreenClickBehaviorOption DefaultGameplaySubScreenClickBehavior = GameplaySubScreenClickBehaviorOption.TrackSkip_1_Sec_Delay;
#else
        public const GameplaySubScreenClickBehaviorOption DefaultGameplaySubScreenClickBehavior = GameplaySubScreenClickBehaviorOption.TrackSkip;
#endif

        /// <summary>
        /// Gets or sets how fast notes approach the judgment ring.
        /// </summary>
        [Step("0.25")]
        public float TapSpeed { get; set; } = 7.5f;

        /// <summary>
        /// Gets or sets how fast touch notes appear and close in.
        /// </summary>
        [Step("0.25")]
        public float TouchSpeed { get; set; } = 7.5f;

        /// <summary>
        /// Gets or sets the appearance timing offset of slide paths in the selected offset unit; positive values are later.
        /// </summary>
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float SlideFadeInOffset { get; set; } = 0f;

        /// <summary>
        /// Gets or sets the background dimming during gameplay, from 0 to 1; higher values are darker.
        /// </summary>
        [Step("0.05")]
        [Range("0", "1")]
        public float BackgroundDim { get; set; } = 0.8f;

        public bool SkipVideoDownload { get; set; } = false;

        public bool StarRotation { get; set; } = true;

        public bool TrackSkip { get; set; } = true;

        public EnforceGameFailureCondition EnforceGameFailure { get; set; } = EnforceGameFailureCondition.Disabled;

        public bool FastRetry { get; set; } = true;

        public bool FastPractice { get; set; } = false;

        public GameplaySubScreenClickBehaviorOption GameplaySubScreenClickBehavior { get; set; } = DefaultGameplaySubScreenClickBehavior;

        public MirrorOption Mirror { get; set; } = MirrorOption.Off;

        [Step("1")]
        [Range("-7", "7")]
        public int Rotation { get; set; } = 0;

        public bool SlideSkipping { get; set; } = true;

        public RandomModeOption Random { get; set; } = RandomModeOption.Disabled;
#if UNITY_ANDROID || UNITY_IOS

        /// <summary>
        /// Gets or sets whether slides and touch notes can be played with the buttons.
        /// </summary>
        public bool ButtonRingForTouch { get; set; } = true;
#endif
        public RecordModeOption RecordMode { get; set; } = RecordModeOption.Disable;

        [Step("1")]
        [Range("1", "5")]
        [UseTranslation(TranslationTarget.OptionValue)]
        public int LeadInTime { get; set; } = 1;

        public bool ManualStartGame { get; set; } = false;

        [Step("0.001")]
        public float NoteAppearRate { get; set; } = 0.265f;
    }
}
