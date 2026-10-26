using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Settings;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode.Doubles
{
    /// <summary>
    /// A minimal concrete note that exposes the protected judgment, conversion and timing helpers of
    /// <see cref="NoteDrop"/> so the shared note logic can be tested without a real note type.
    /// </summary>
    internal sealed class ProbeNoteDrop : NoteDrop
    {
        /// <summary>Gets the number of times the skin was requested.</summary>
        public int LoadSkinCount { get; private set; }

        /// <summary>Gets the number of times the note sound was requested.</summary>
        public int PlaySfxCount { get; private set; }

        /// <summary>Gets the grade of the last judgment sound request, or null when none was made.</summary>
        public JudgeGrade? LastJudgeSfxGrade { get; private set; }

        /// <summary>Gets or sets the stored judgment result.</summary>
        public JudgeGrade Result
        {
            get => JudgeResult;
            set => JudgeResult = value;
        }

        /// <summary>Gets or sets the stored judgment offset in milliseconds.</summary>
        public float Diff
        {
            get => JudgeDiff;
            set => JudgeDiff = value;
        }

        /// <summary>Gets or sets a value indicating whether the note is judged.</summary>
        public bool Judged
        {
            get => IsJudged;
            set => IsJudged = value;
        }

        /// <summary>Gets or sets the answer timing in seconds.</summary>
        public float AnswerTiming
        {
            get => JudgeTiming;
            set => JudgeTiming = value;
        }

        /// <summary>Gets the configured autoplay mode.</summary>
        public AutoplayModeOption Mode => AutoplayMode;

        /// <summary>Gets a value indicating whether any autoplay mode is active.</summary>
        public bool Autoplaying => IsAutoplay;

        /// <summary>Gets a value indicating whether the button ring may judge touches.</summary>
        public bool ButtonRingForTouch => IsUseButtonRingForTouch;

        /// <summary>Gets the judgment offset read from the settings, in seconds.</summary>
        public float JudgeOffsetSec => USERSETTING_JUDGE_OFFSET_SEC;

        /// <summary>Gets the touch panel offset read from the settings, in seconds.</summary>
        public float TouchPanelOffsetSec => USERSETTING_TOUCHPANEL_OFFSET_SEC;

        /// <summary>Gets the tap scale read from the settings.</summary>
        public float TapScale => USERSETTING_TAP_SCALE;

        /// <summary>Gets the slide skipping option read from the settings.</summary>
        public bool SlideSkipping => USERSETTING_SLIDE_SKIPPING;

        /// <summary>Gets the DJ autoplay policy read from the settings.</summary>
        public DJAutoPolicyOption DjAutoPolicy => USERSETTING_DJAUTO_POLICY;

        /// <summary>Gets the time from the arrive timing to the current frame.</summary>
        public float TimeSpanToArrive => GetTimeSpanToArriveTiming();

        /// <summary>Gets the time from the answer timing to the current frame.</summary>
        public float TimeSpanToJudge => GetTimeSpanToJudgeTiming();

        /// <summary>Judges the note as if it were hit at the given time.</summary>
        /// <param name="currentSec">The hit time in seconds.</param>
        public void RunJudge(float currentSec)
        {
            Judge(currentSec);
        }

        /// <summary>Runs the shared autoplay logic.</summary>
        public void RunAutoplay()
        {
            Autoplay();
        }

        /// <summary>Converts a grade with the active judge style.</summary>
        /// <param name="grade">The grade to convert.</param>
        /// <returns>The converted grade.</returns>
        public JudgeGrade Convert(JudgeGrade grade)
        {
            ConvertJudgeGrade(ref grade);
            return grade;
        }

        /// <summary>Gets the time from the answer timing to a base timing.</summary>
        /// <param name="baseTiming">The base timing in seconds.</param>
        /// <returns>The base timing minus the answer timing with offset.</returns>
        public float TimeSpanToJudgeFrom(float baseTiming)
        {
            return GetTimeSpanToJudgeTiming(baseTiming);
        }

        /// <summary>Sets the note state.</summary>
        /// <param name="state">The new state.</param>
        public void SetState(NoteStatus state)
        {
            State = state;
        }

        /// <summary>Counts the skin request.</summary>
        protected override void LoadSkin()
        {
            LoadSkinCount++;
        }

        /// <summary>Counts the sound request.</summary>
        protected override void PlaySFX()
        {
            PlaySfxCount++;
        }

        /// <summary>Records the judgment sound request.</summary>
        /// <param name="judgeResult">The judgment result.</param>
        protected override void PlayJudgeSFX(in NoteJudgeResult judgeResult)
        {
            LastJudgeSfxGrade = judgeResult.Grade;
        }
    }
}
