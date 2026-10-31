using MajdataPlay.Settings.OptionEnumerators;
using MajdataPlay.Settings.Runtime;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Contains the judgment timing and judgment presentation options.
    /// </summary>
    public class JudgeOptions
    {
        /// <summary>
        /// Gets or sets the offset that aligns the chart to the music in the selected offset unit; negative values are earlier.
        /// </summary>
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float AudioOffset { get; set; } = 0f;

        /// <summary>
        /// Gets or sets the offset of the judgment window in the selected offset unit; negative values are earlier.
        /// </summary>
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float JudgeOffset { get; set; } = 0f;

        /// <summary>
        /// Gets or sets the offset of the guide sound in the selected offset unit, which does not affect judgment.
        /// </summary>
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float AnswerOffset { get; set; } = 0f;

        /// <summary>
        /// Gets or sets the offset compensating for the input latency of the touch panel in the selected offset unit.
        /// </summary>
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float TouchPanelOffset { get; set; } = 0f;

        /// <summary>
        /// Gets or sets whether the real-time timing offset gauge and the average judgment position are shown.
        /// </summary>
        public bool EnableJudgeTimingGauge { get; set; } = false;

        /// <summary>Gets or sets the unit used for gameplay timing offsets.</summary>
        public OffsetUnitOption OffsetUnit { get; set; } = OffsetUnitOption.Frame;

        /// <summary>
        /// Gets or sets how hold and slide judgments behave.
        /// </summary>
        public JudgeModeOption Mode { get; set; } = JudgeModeOption.Modern;

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
    }
}
