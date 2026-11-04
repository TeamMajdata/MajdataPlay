using MajdataPlay.Settings.OptionEnumerators;
using MajdataPlay.Settings.Runtime;
#nullable enable
namespace MajdataPlay.Settings
{
    public class JudgeOptions
    {
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float AudioOffset { get; set; } = SettingDefaults.Judge.AudioOffset;

        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float JudgeOffset { get; set; } = SettingDefaults.Judge.JudgeOffset;

        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float AnswerOffset { get; set; } = SettingDefaults.Judge.AnswerOffset;

        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float TouchPanelOffset { get; set; } = SettingDefaults.Judge.TouchPanelOffset;

        public bool EnableJudgeTimingGauge { get; set; } = SettingDefaults.Judge.EnableJudgeTimingGauge;

        public OffsetUnitOption OffsetUnit { get; set; } = SettingDefaults.Judge.OffsetUnit;

        public JudgeModeOption Mode { get; set; } = SettingDefaults.Judge.Mode;

        public bool DisplayCriticalPerfect { get; set; } = SettingDefaults.Judge.DisplayCriticalPerfect;

        public bool DisplayBreakScore { get; set; } = SettingDefaults.Judge.DisplayBreakScore;

        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public JudgeDisplayOption FastLateType { get; set; } = SettingDefaults.Judge.FastLateType;

        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public JudgeDisplayOption NoteJudgeType { get; set; } = SettingDefaults.Judge.NoteJudgeType;

        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public JudgeDisplayOption TouchJudgeType { get; set; } = SettingDefaults.Judge.TouchJudgeType;

        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public JudgeDisplayOption SlideJudgeType { get; set; } = SettingDefaults.Judge.SlideJudgeType;

        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public JudgeDisplayOption BreakJudgeType { get; set; } = SettingDefaults.Judge.BreakJudgeType;

        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public JudgeDisplayOption BreakFastLateType { get; set; } = SettingDefaults.Judge.BreakFastLateType;

        public JudgeModeOption SlideSortOrder { get; set; } = SettingDefaults.Judge.SlideSortOrder;

        [Step("0.05")]
        [Range("0", "1")]
        [TranslateValues("0")]
        public float OuterJudgeDistance { get; set; } = SettingDefaults.Judge.OuterJudgeDistance;

        [Step("0.05")]
        [Range("0", "1")]
        [TranslateValues("0")]
        public float InnerJudgeDistance { get; set; } = SettingDefaults.Judge.InnerJudgeDistance;

        public bool DisplayHoldHeadJudgeResult { get; set; } = SettingDefaults.Judge.DisplayHoldHeadJudgeResult;
    }
}
