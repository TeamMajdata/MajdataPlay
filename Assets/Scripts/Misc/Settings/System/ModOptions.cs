using MajdataPlay.Settings.OptionEnumerators;
#nullable enable
namespace MajdataPlay.Settings
{
    public class ModOptions
    {
        [Step("0.05")]
        [Range("0", null)]
        public float PlaybackSpeed { get; set; } = SettingDefaults.Mod.PlaybackSpeed;

        public AutoplayModeOption AutoPlay { get; set; } = SettingDefaults.Mod.AutoPlay;

        public JudgeStyleOption JudgeStyle { get; set; } = SettingDefaults.Mod.JudgeStyle;

        public bool SubdivideSlideJudgeGrade { get; set; } = SettingDefaults.Mod.SubdivideSlideJudgeGrade;

        public bool AllBreak { get; set; } = SettingDefaults.Mod.AllBreak;

        public bool AllEx { get; set; } = SettingDefaults.Mod.AllEx;

        public bool AllTouch { get; set; } = SettingDefaults.Mod.AllTouch;

        public bool SlideNoHead { get; set; } = SettingDefaults.Mod.SlideNoHead;

        public bool SlideNoTrack { get; set; } = SettingDefaults.Mod.SlideNoTrack;

        [OptionEnumerator(typeof(NoteMaskEnumerator))]
        public string NoteMask { get; set; } = SettingDefaults.Mod.NoteMask;

        public bool IsAnyModActive()
        {
            return !(PlaybackSpeed == 1f &&
                !AllBreak && !AllEx && !AllTouch && AutoPlay == AutoplayModeOption.Disable && JudgeStyle == JudgeStyleOption.DEFAULT);
        }
    }
}
