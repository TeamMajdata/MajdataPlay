using MajdataPlay.Settings.OptionEnumerators;
#nullable enable
namespace MajdataPlay.Settings
{
    public class ModOptions
    {
        [Step("0.05")]
        [Range("0", null)]
        public float PlaybackSpeed { get; set; } = 1f;

        public AutoplayModeOption AutoPlay { get; set; } = AutoplayModeOption.Disable;

        public JudgeStyleOption JudgeStyle { get; set; } = JudgeStyleOption.DEFAULT;

        public bool SubdivideSlideJudgeGrade { get; set; } = false;

        public bool AllBreak { get; set; } = false;

        public bool AllEx { get; set; } = false;

        public bool AllTouch { get; set; } = false;

        public bool SlideNoHead { get; set; } = false;

        public bool SlideNoTrack { get; set; } = false;

        [OptionEnumerator(typeof(NoteMaskEnumerator))]
        public string NoteMask { get; set; } = "Disable";

        public bool IsAnyModActive()
        {
            return !(PlaybackSpeed == 1f &&
                !AllBreak && !AllEx && !AllTouch && AutoPlay == AutoplayModeOption.Disable && JudgeStyle == JudgeStyleOption.DEFAULT);
        }
    }
}
