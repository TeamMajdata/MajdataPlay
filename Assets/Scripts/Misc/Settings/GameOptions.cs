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
        /// <summary>
        /// The default action of a tap on the sub-screen during gameplay on the current platform.
        /// </summary>
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

        /// <summary>
        /// Gets or sets whether stars rotate while approaching the judgment ring.
        /// </summary>
        public bool StarRotation { get; set; } = true;

        /// <summary>
        /// Gets or sets the information displayed at the center of the screen during gameplay.
        /// </summary>
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public BGInfoOption BGInfo { get; set; } = BGInfoOption.Combo;

        /// <summary>
        /// Gets or sets the information displayed below the center of the screen during gameplay.
        /// </summary>
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public BGInfoOption SecondaryBGInfo { get; set; } = BGInfoOption.None;

        /// <summary>
        /// Gets or sets the information displayed on the sub-screen during gameplay.
        /// </summary>
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public BGInfoOption SubScreenBGInfo { get; set; } = BGInfoOption.Achievement;

        /// <summary>
        /// Gets or sets the information displayed on the bar between buttons 1 and 8.
        /// </summary>
        public TopInfoDisplayOption TopInfo { get; set; } = TopInfoDisplayOption.None;

        /// <summary>
        /// Gets or sets whether the real-time timing offset gauge and the average judgment position are shown.
        /// </summary>
        public bool EnableJudgeTimingGauge { get; set; } = false;

        /// <summary>
        /// Gets or sets whether holding buttons 2, 3, 6 and 7 skips the track.
        /// </summary>
        public bool TrackSkip { get; set; } = true;

        /// <summary>
        /// Gets or sets when the track is skipped or retried automatically because the target achievement is no longer reachable.
        /// </summary>
        public EnforceGameFailureCondition EnforceGameFailure { get; set; } = EnforceGameFailureCondition.Disabled;

        /// <summary>
        /// Gets or sets whether holding buttons 3, 4, 5 and 6 retries the track.
        /// </summary>
        public bool FastRetry { get; set; } = true;

        /// <summary>
        /// Gets or sets whether pressing buttons 1, 2, 7 and 8 during gameplay opens the practice mode menu.
        /// </summary>
        public bool FastPractice { get; set; } = false;

        /// <summary>
        /// Gets or sets what a tap on the sub-screen does during gameplay.
        /// </summary>
        public GameplaySubScreenClickBehaviorOption GameplaySubScreenClickBehavior { get; set; } = DefaultGameplaySubScreenClickBehavior;

        /// <summary>
        /// Gets or sets the axis the chart is mirrored by.
        /// </summary>
        public MirrorOption Mirror { get; set; } = MirrorOption.Off;

        /// <summary>
        /// Gets or sets the chart rotation in buttons, 45 degrees each; positive values are clockwise.
        /// </summary>
        [Step("1")]
        [Range("-7", "7")]
        public int Rotation { get; set; } = 0;

        /// <summary>
        /// Gets or sets whether slide sections can be skipped.
        /// </summary>
        public bool SlideSkipping { get; set; } = true;

        /// <summary>
        /// Gets or sets how note positions are randomized.
        /// </summary>
        public RandomModeOption Random { get; set; } = RandomModeOption.Disabled;
#if UNITY_ANDROID || UNITY_IOS

        /// <summary>
        /// Gets or sets whether slides and touch notes can be played with the buttons.
        /// </summary>
        public bool ButtonRingForTouch { get; set; } = true;
#endif
        /// <summary>
        /// Gets or sets how gameplay is recorded.
        /// </summary>
        public RecordModeOption RecordMode { get; set; } = RecordModeOption.Disable;

        /// <summary>
        /// Gets or sets the wait in seconds between entering the gameplay screen and the start of the chart.
        /// </summary>
        [Step("1")]
        [Range("1", "5")]
        [UseTranslation(TranslationTarget.OptionValue)]
        public int LeadInTime { get; set; } = 1;

        /// <summary>
        /// Gets or sets whether gameplay waits for a button press after loading, as used in tournaments.
        /// </summary>
        public bool ManualStartGame { get; set; } = false;
    }
}
