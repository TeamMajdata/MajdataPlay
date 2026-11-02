#nullable enable
namespace MajdataPlay.Settings
{
    public static partial class SettingDefaults
    {
        public static class Judge
        {
            public const float AudioOffset = 0f;

            public const float JudgeOffset = 0f;

            public const float AnswerOffset = 0f;

            public const float TouchPanelOffset = 0f;

            public const bool EnableJudgeTimingGauge = false;

            public const OffsetUnitOption OffsetUnit = OffsetUnitOption.Frame;

            public const JudgeModeOption Mode = JudgeModeOption.Modern;

            public const bool DisplayCriticalPerfect = false;

            public const bool DisplayBreakScore = true;

            public const JudgeDisplayOption FastLateType = JudgeDisplayOption.Disable;

            public const JudgeDisplayOption NoteJudgeType = JudgeDisplayOption.All;

            public const JudgeDisplayOption TouchJudgeType = JudgeDisplayOption.All;

            public const JudgeDisplayOption SlideJudgeType = JudgeDisplayOption.All;

            public const JudgeDisplayOption BreakJudgeType = JudgeDisplayOption.All;

            public const JudgeDisplayOption BreakFastLateType = JudgeDisplayOption.Disable;

            public const JudgeModeOption SlideSortOrder = JudgeModeOption.Modern;

            public const float OuterJudgeDistance = 1f;

            public const float InnerJudgeDistance = 1f;

            public const bool DisplayHoldHeadJudgeResult = false;
        }
    }
}
