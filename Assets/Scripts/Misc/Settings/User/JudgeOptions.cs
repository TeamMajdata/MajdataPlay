using MajdataPlay.Settings.OptionEnumerators;
using MajdataPlay.Settings.Runtime;
#nullable enable
namespace MajdataPlay.Settings
{
    public class JudgeOptions
    {
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float AudioOffset { get; set; } = 0f;

        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float JudgeOffset { get; set; } = 0f;

        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float AnswerOffset { get; set; } = 0f;

        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float TouchPanelOffset { get; set; } = 0f;

        public bool EnableJudgeTimingGauge { get; set; } = false;

        public OffsetUnitOption OffsetUnit { get; set; } = OffsetUnitOption.Frame;

        public JudgeModeOption Mode { get; set; } = JudgeModeOption.Modern;

        public bool DisplayCriticalPerfect { get; set; } = false;

        public bool DisplayBreakScore { get; set; } = true;

        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public JudgeDisplayOption FastLateType { get; set; } = JudgeDisplayOption.Disable;

        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public JudgeDisplayOption NoteJudgeType { get; set; } = JudgeDisplayOption.All;

        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public JudgeDisplayOption TouchJudgeType { get; set; } = JudgeDisplayOption.All;

        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public JudgeDisplayOption SlideJudgeType { get; set; } = JudgeDisplayOption.All;

        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public JudgeDisplayOption BreakJudgeType { get; set; } = JudgeDisplayOption.All;

        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public JudgeDisplayOption BreakFastLateType { get; set; } = JudgeDisplayOption.Disable;

        public JudgeModeOption SlideSortOrder { get; set; } = JudgeModeOption.Modern;

        [Step("0.05")]
        [Range("0", "1")]
        [TranslateValues("0")]
        public float OuterJudgeDistance { get; set; } = 1f;

        [Step("0.05")]
        [Range("0", "1")]
        [TranslateValues("0")]
        public float InnerJudgeDistance { get; set; } = 1f;

        public bool DisplayHoldHeadJudgeResult { get; set; } = false;
    }
}
