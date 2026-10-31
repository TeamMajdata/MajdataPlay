using MajdataPlay.Settings.OptionEnumerators;
using MajdataPlay.Settings.Runtime;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Contains the display, judgment presentation and screen layout options.
    /// </summary>
    public class DisplayOptions
    {
        /// <summary>
        /// Gets or sets the game language; empty selects the language of the system.
        /// </summary>
        [OptionEnumerator(typeof(LanguageEnumerator))]
        public string Language { get; set; } = "";

        /// <summary>
        /// Gets or sets the name of the note skin used during gameplay.
        /// </summary>
        [OptionEnumerator(typeof(SkinEnumerator))]
        public string Skin { get; set; } = "default";

        /// <summary>
        /// Gets or sets whether critical perfect judgments are displayed during gameplay.
        /// </summary>
        public bool DisplayCriticalPerfect { get; set; } = false;

        /// <summary>
        /// Gets or sets whether break notes display their score instead of the judgment.
        /// </summary>
        public bool DisplayBreakScore { get; set; } = true;

        /// <summary>
        /// Gets or sets the judgment levels that show fast or late; higher judgments are hidden.
        /// </summary>
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public JudgeDisplayOption FastLateType { get; set; } = JudgeDisplayOption.Disable;

        /// <summary>
        /// Gets or sets the judgment levels shown for tap and hold notes; higher judgments are hidden.
        /// </summary>
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public JudgeDisplayOption NoteJudgeType { get; set; } = JudgeDisplayOption.All;

        /// <summary>
        /// Gets or sets the judgment levels shown for touch notes; higher judgments are hidden.
        /// </summary>
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public JudgeDisplayOption TouchJudgeType { get; set; } = JudgeDisplayOption.All;

        /// <summary>
        /// Gets or sets the judgment levels shown for slides; higher judgments are hidden.
        /// </summary>
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public JudgeDisplayOption SlideJudgeType { get; set; } = JudgeDisplayOption.All;

        /// <summary>
        /// Gets or sets the judgment levels shown for break notes; higher judgments are hidden.
        /// </summary>
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public JudgeDisplayOption BreakJudgeType { get; set; } = JudgeDisplayOption.All;

        /// <summary>
        /// Gets or sets the judgment levels of break notes that show fast or late; higher judgments are hidden.
        /// </summary>
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public JudgeDisplayOption BreakFastLateType { get; set; } = JudgeDisplayOption.Disable;

        /// <summary>
        /// Gets or sets whether newer slides are drawn below (modern) or above (classic) older slides.
        /// </summary>
        public JudgeModeOption SlideSortOrder { get; set; } = JudgeModeOption.Modern;

        /// <summary>
        /// Gets or sets how the arrows of connected slides such as <c>1-4q7</c> are placed along their segments.
        /// </summary>
        public SlidePathStyle SlidePathStyle { get; set; } = SlidePathStyle.Classic;

        /// <summary>
        /// Gets or sets where judgments of ring notes such as taps, stars, holds and breaks appear, from 0 at the center to 1 at the ring.
        /// </summary>
        [Step("0.05")]
        [Range("0", "1")]
        [TranslateValues("0")]
        public float OuterJudgeDistance { get; set; } = 1f;

        /// <summary>
        /// Gets or sets where judgments of touch and touch hold notes appear, from 0 at the center to 1 at the ring.
        /// </summary>
        [Step("0.05")]
        [Range("0", "1")]
        [TranslateValues("0")]
        public float InnerJudgeDistance { get; set; } = 1f;

        /// <summary>
        /// Gets or sets whether the judgment of a hold head is shown.
        /// </summary>
        public bool DisplayHoldHeadJudgeResult { get; set; } = false;

        /// <summary>
        /// Gets or sets the size multiplier of tap notes during gameplay.
        /// </summary>
        [Step("0.01")]
        [Range("0", "2")]
        public float TapScale { get; set; } = 1f;

        /// <summary>
        /// Gets or sets the size multiplier of hold notes during gameplay.
        /// </summary>
        [Step("0.01")]
        [Range("0", "2")]
        public float HoldScale { get; set; } = 1f;

        /// <summary>
        /// Gets or sets the size multiplier of touch notes during gameplay.
        /// </summary>
        [Step("0.01")]
        [Range("0", "2")]
        public float TouchScale { get; set; } = 1f;

        /// <summary>
        /// Gets or sets the size multiplier of slides during gameplay, which does not affect Wi-Fi slides.
        /// </summary>
        [Step("0.01")]
        [Range("0", "2")]
        public float SlideScale { get; set; } = 1f;

        /// <summary>
        /// Gets or sets where the reaction effect of a touch on the screen is displayed.
        /// </summary>
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public TouchFeedbackLevel TouchFeedback { get; set; } = TouchFeedbackLevel.Outer_Only;
#if UNITY_STANDALONE

        /// <summary>
        /// Gets or sets the window resolution as <c>width</c>x<c>height</c>.
        /// </summary>
        [HideInSettingUI]
        public string Resolution { get; set; } = "1080x1920";
#endif

        /// <summary>
        /// Gets or sets whether the ratio and position of the main screen are adjusted manually instead of using the default layout.
        /// </summary>
#if UNITY_ANDROID || UNITY_IOS
        public bool MainScreenTransform { get; set; } = true;
#else
        public bool MainScreenTransform { get; set; } = false;
#endif

        /// <summary>
        /// Gets or sets the size multiplier of the main gameplay screen while <see cref="MainScreenTransform"/> is enabled.
        /// </summary>
        [Step("0.01")]
        [Range("0.05", "1.5")]
        public float MainScreenScale { get; set; } = 1f;

        /// <summary>
        /// Gets or sets the position of the main gameplay screen on the display while <see cref="MainScreenTransform"/> is enabled.
        /// </summary>
        [Step("0.01")]
        [Range("-1", "1")]
        public float MainScreenOffset { get; set; } = 1f;

        /// <summary>
        /// Gets or sets the vertical screen center of the last applied main screen layout, cached by the layout provider.
        /// </summary>
        [HideInSettingUI]
        public float MainScreenCachedScreenCenterY { get; set; } = 960f;

        /// <summary>
        /// Gets or sets the position of the secondary display area.
        /// </summary>
        [Step("0.01")]
        [Range("-5", "5")]
        public float SubDisplayOffset { get; set; } = 0f;

        /// <summary>
        /// Gets or sets the size multiplier of the secondary display area.
        /// </summary>
        [Step("0.01")]
        public float SubDisplayScale { get; set; } = 1f;

        /// <summary>
        /// Gets or sets the screen orientation during gameplay.
        /// </summary>
        public GameplayScreenRotationAngleOption GameplayScreenRotationAngle { get; set; } = GameplayScreenRotationAngleOption.Zero;
#if UNITY_STANDALONE

        /// <summary>
        /// Gets or sets whether the game window stays on top of other windows.
        /// </summary>
        [HideInSettingUI]
        public bool Topmost { get; set; } = false;
#endif

        /// <summary>
        /// Gets or sets whether the video of an online chart is skipped while it is downloaded.
        /// </summary>
        public bool SkipVideoDownload { get; set; } = false;
    }
}
