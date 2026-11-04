#nullable enable
namespace MajdataPlay.Settings
{
    public static partial class SettingDefaults
    {
        public static class Mod
        {
            public const float PlaybackSpeed = 1f;

            public const AutoplayModeOption AutoPlay = AutoplayModeOption.Disable;

            public const JudgeStyleOption JudgeStyle = JudgeStyleOption.DEFAULT;

            public const bool SubdivideSlideJudgeGrade = false;

            public const bool AllBreak = false;

            public const bool AllEx = false;

            public const bool AllTouch = false;

            public const bool SlideNoHead = false;

            public const bool SlideNoTrack = false;

            public const string NoteMask = "Disable";
        }
    }
}
