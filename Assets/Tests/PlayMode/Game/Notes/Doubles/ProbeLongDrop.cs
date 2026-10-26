using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Behaviours;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode.Doubles
{
    /// <summary>
    /// A minimal concrete long note that exposes the hold end judgment helpers of <see cref="NoteLongDrop"/>.
    /// </summary>
    internal sealed class ProbeLongDrop : NoteLongDrop
    {
        /// <summary>Gets or sets a value indicating whether the head is judged.</summary>
        public bool Judged
        {
            get => IsJudged;
            set => IsJudged = value;
        }

        /// <summary>Gets or sets the head judgment offset in milliseconds.</summary>
        public float Diff
        {
            get => JudgeDiff;
            set => JudgeDiff = value;
        }

        /// <summary>Gets or sets the accumulated release time in seconds.</summary>
        public float ReleaseTime
        {
            get => PlayerReleaseTimeSec;
            set => PlayerReleaseTimeSec = value;
        }

        /// <summary>Gets or sets the answer timing in seconds.</summary>
        public float AnswerTiming
        {
            get => JudgeTiming;
            set => JudgeTiming = value;
        }

        /// <summary>Gets the remaining time measured from the answer timing.</summary>
        public float RemainingTime => GetRemainingTime();

        /// <summary>Gets the remaining time measured from the arrive timing.</summary>
        public float RemainingTimeWithoutOffset => GetRemainingTimeWithoutOffset();

        /// <summary>Runs the deluxe hold end judgment.</summary>
        /// <param name="headGrade">The head grade.</param>
        /// <param name="ignoreTimeSec">The ignored head and tail length in seconds.</param>
        /// <returns>The final grade.</returns>
        public JudgeGrade EndJudge(JudgeGrade headGrade, float ignoreTimeSec)
        {
            return HoldEndJudge(headGrade, ignoreTimeSec);
        }

        /// <summary>Runs the classic hold end judgment at the current frame.</summary>
        /// <param name="headGrade">The head grade.</param>
        /// <param name="offset">The release offset in seconds.</param>
        /// <returns>The final grade.</returns>
        public JudgeGrade ClassicEndJudge(JudgeGrade headGrade, float offset)
        {
            return HoldClassicEndJudge(headGrade, offset);
        }

        /// <summary>Does nothing; the probe has no sprites.</summary>
        protected override void LoadSkin()
        {
        }

        /// <summary>Does nothing; the probe has no sounds.</summary>
        protected override void PlaySFX()
        {
        }

        /// <summary>Does nothing; the probe has no sounds.</summary>
        /// <param name="judgeResult">The judgment result.</param>
        protected override void PlayJudgeSFX(in NoteJudgeResult judgeResult)
        {
        }
    }
}
