using MajdataPlay.Settings.OptionEnumerators;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Contains the gameplay modifiers of the current session.
    /// </summary>
    public class ModOptions
    {
        /// <summary>
        /// Gets or sets the playback speed multiplier of the track.
        /// </summary>
        [Step("0.05")]
        [Range("0", null)]
        public float PlaybackSpeed { get; set; } = 1f;

        /// <summary>
        /// Gets or sets how the chart is played automatically.
        /// </summary>
        public AutoplayModeOption AutoPlay { get; set; } = AutoplayModeOption.Disable;

        /// <summary>
        /// Gets or sets how strict the judgment windows are.
        /// </summary>
        public JudgeStyleOption JudgeStyle { get; set; } = JudgeStyleOption.DEFAULT;

        /// <summary>
        /// Gets or sets whether slides can receive judgments below critical perfect.
        /// </summary>
        public bool SubdivideSlideJudgeGrade { get; set; } = false;

        /// <summary>
        /// Gets or sets whether every note becomes a break note.
        /// </summary>
        public bool AllBreak { get; set; } = false;

        /// <summary>
        /// Gets or sets whether every note becomes an EX note.
        /// </summary>
        public bool AllEx { get; set; } = false;

        /// <summary>
        /// Gets or sets whether every note becomes a touch note.
        /// </summary>
        public bool AllTouch { get; set; } = false;

        /// <summary>
        /// Gets or sets whether the head tap of every slide is removed.
        /// </summary>
        public bool SlideNoHead { get; set; } = false;

        /// <summary>
        /// Gets or sets whether the body of every slide is removed.
        /// </summary>
        public bool SlideNoTrack { get; set; } = false;
#if !(UNITY_ANDROID || UNITY_IOS)

        /// <summary>
        /// Gets or sets whether slides and touch notes can be played with the buttons.
        /// </summary>
        public bool ButtonRingForTouch { get; set; } = false;
#endif

        /// <summary>
        /// Gets or sets the name of the mask covering part of the gameplay screen, or <c>Disable</c> for none.
        /// </summary>
        [OptionEnumerator(typeof(NoteMaskEnumerator))]
        public string NoteMask { get; set; } = "Disable";

        /// <summary>
        /// Determines whether a modifier that changes the playback, the notes or the judgment is active.
        /// </summary>
        /// <returns>True when the playback speed is not 1 or any of the all-break, all-EX, all-touch, autoplay or judgment style modifiers is set.</returns>
        public bool IsAnyModActive()
        {
            return !(PlaybackSpeed == 1f &&
                !AllBreak && !AllEx && !AllTouch && AutoPlay == AutoplayModeOption.Disable && JudgeStyle == JudgeStyleOption.DEFAULT);
        }
    }
}
