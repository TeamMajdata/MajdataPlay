using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Settings;
using MajdataPlay.Tests.Game.Notes.PlayMode.Doubles;
using MajdataPlay.Tests.Game.Notes.PlayMode.Support;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode
{
    /// <summary>
    /// Covers the deluxe and classic hold end judgments of
    /// <see cref="MajdataPlay.Scenes.Game.Notes.Behaviours.NoteLongDrop"/>.
    /// </summary>
    public class NoteLongDropTests
    {
        /// <summary>The hold length used by the deluxe cases.</summary>
        private const float HoldLength = 2f;

        /// <summary>The ignored head and tail length used by the deluxe cases.</summary>
        private const float IgnoreLength = 0.3f;

        /// <summary>The isolation scope of the current test.</summary>
        private NoteTestEnvironment _environment = null!;

        /// <summary>The note controller the probes read during Awake.</summary>
        private FakeNoteController _controller = null!;

        /// <summary>
        /// Gets the deluxe end judgment cases as release time, head grade, head offset sign and expected grade. With a
        /// two second hold and 0.3 second ignore length the effective length is 1.7 seconds, so releases of 0, 0.17,
        /// 0.85, 1.36 and 1.7 seconds give press ratios of 100%, 90%, 50%, 20% and 0%.
        /// </summary>
        private static object[] DeluxeCases => new object[]
        {
            new object[] { 0f, JudgeGrade.Perfect, 0f, JudgeGrade.Perfect },
            new object[] { 0f, JudgeGrade.LatePerfect3rd, 1f, JudgeGrade.LatePerfect3rd },
            new object[] { 0f, JudgeGrade.LateGood, 1f, JudgeGrade.LateGreat },
            new object[] { 0f, JudgeGrade.FastGood, -1f, JudgeGrade.FastGreat },
            new object[] { 0f, JudgeGrade.Miss, 0f, JudgeGrade.LateGood },
            new object[] { 0f, JudgeGrade.TooFast, 0f, JudgeGrade.FastGood },
            new object[] { 0.17f, JudgeGrade.Perfect, 1f, JudgeGrade.LatePerfect2nd },
            new object[] { 0.17f, JudgeGrade.Perfect, -1f, JudgeGrade.FastPerfect2nd },
            new object[] { 0.17f, JudgeGrade.FastPerfect3rd, -1f, JudgeGrade.FastPerfect3rd },
            new object[] { 0.17f, JudgeGrade.LateGreat3rd, 1f, JudgeGrade.LateGreat },
            new object[] { 0.17f, JudgeGrade.FastGreat2nd, -1f, JudgeGrade.FastGreat },
            new object[] { 0.17f, JudgeGrade.Miss, 0f, JudgeGrade.LateGood },
            new object[] { 0.17f, JudgeGrade.TooFast, 0f, JudgeGrade.FastGood },
            new object[] { 0.85f, JudgeGrade.Perfect, 1f, JudgeGrade.LateGreat2nd },
            new object[] { 0.85f, JudgeGrade.Perfect, -1f, JudgeGrade.FastGreat2nd },
            new object[] { 0.85f, JudgeGrade.LatePerfect2nd, 1f, JudgeGrade.LateGreat },
            new object[] { 0.85f, JudgeGrade.FastPerfect2nd, -1f, JudgeGrade.FastGreat },
            new object[] { 0.85f, JudgeGrade.Miss, 0f, JudgeGrade.LateGood },
            new object[] { 0.85f, JudgeGrade.TooFast, 0f, JudgeGrade.FastGood },
            new object[] { 1.36f, JudgeGrade.Perfect, 1f, JudgeGrade.LateGood },
            new object[] { 1.36f, JudgeGrade.Perfect, -1f, JudgeGrade.FastGood },
            new object[] { 1.36f, JudgeGrade.LateGreat, 1f, JudgeGrade.LateGood },
            new object[] { 1.36f, JudgeGrade.Miss, 0f, JudgeGrade.LateGood },
            new object[] { 1.36f, JudgeGrade.FastPerfect2nd, -1f, JudgeGrade.FastGood },
            new object[] { 1.36f, JudgeGrade.TooFast, 0f, JudgeGrade.FastGood },
            new object[] { 1.7f, JudgeGrade.Perfect, 1f, JudgeGrade.LateGood },
            new object[] { 1.7f, JudgeGrade.Perfect, -1f, JudgeGrade.FastGood },
            new object[] { 1.7f, JudgeGrade.LatePerfect3rd, 1f, JudgeGrade.LateGood },
            new object[] { 1.7f, JudgeGrade.FastGreat3rd, -1f, JudgeGrade.FastGood },
            new object[] { 1.7f, JudgeGrade.Miss, 0f, JudgeGrade.Miss },
            new object[] { 1.7f, JudgeGrade.TooFast, 0f, JudgeGrade.TooFast }
        };

        /// <summary>Installs a clean environment and a note controller.</summary>
        [SetUp]
        public void SetUp()
        {
            _environment = new NoteTestEnvironment();
            _controller = new FakeNoteController();
            Majdata<INoteController>.Instance = _controller;
        }

        /// <summary>Destroys the probes and restores the global state.</summary>
        [TearDown]
        public void TearDown()
        {
            _environment.Dispose();
        }

        /// <summary>Checks every press ratio bucket of the deluxe hold end judgment.</summary>
        /// <param name="releaseTime">The accumulated release time in seconds.</param>
        /// <param name="headGrade">The head grade.</param>
        /// <param name="headDiff">The head offset in milliseconds.</param>
        /// <param name="expected">The expected final grade.</param>
        [TestCaseSource(nameof(DeluxeCases))]
        public void HoldEndJudge_DowngradesByPressRatio(float releaseTime, JudgeGrade headGrade, float headDiff, JudgeGrade expected)
        {
            var hold = CreateHold();
            hold.Judged = true;
            hold.Diff = headDiff;
            hold.ReleaseTime = releaseTime;

            Assert.That(hold.EndJudge(headGrade, IgnoreLength), Is.EqualTo(expected));
        }

        /// <summary>Checks that an unknown head grade is rejected in every press ratio bucket.</summary>
        /// <param name="releaseTime">The accumulated release time in seconds.</param>
        [TestCase(0f)]
        [TestCase(0.17f)]
        [TestCase(0.85f)]
        [TestCase(1.36f)]
        [TestCase(1.7f)]
        public void HoldEndJudge_RejectsUnknownGrade(float releaseTime)
        {
            var hold = CreateHold();
            hold.Judged = true;
            hold.ReleaseTime = releaseTime;

            Assert.Throws<System.ArgumentOutOfRangeException>(() => hold.EndJudge((JudgeGrade)99, IgnoreLength));
        }

        /// <summary>Checks that an unjudged head grade is returned unchanged.</summary>
        [Test]
        public void HoldEndJudge_ReturnsHeadGradeWhenHeadIsNotJudged()
        {
            var hold = CreateHold();
            hold.ReleaseTime = 1.7f;

            Assert.That(hold.EndJudge(JudgeGrade.LatePerfect2nd, IgnoreLength), Is.EqualTo(JudgeGrade.LatePerfect2nd));
        }

        /// <summary>Checks that holds shorter than the ignored length keep their head grade.</summary>
        [Test]
        public void HoldEndJudge_KeepsHeadGradeForVeryShortHolds()
        {
            var hold = CreateHold();
            hold.Length = 0.2f;
            hold.Judged = true;
            hold.ReleaseTime = 0.2f;

            Assert.That(hold.EndJudge(JudgeGrade.FastGreat, IgnoreLength), Is.EqualTo(JudgeGrade.FastGreat));
        }

        /// <summary>Checks the classic end judgment when the hold is released exactly at its end.</summary>
        [Test]
        public void HoldClassicEndJudge_KeepsHeadGradeOnPerfectRelease()
        {
            var hold = CreateHold();
            hold.Judged = true;
            _controller.ThisFrameSec = 1f + HoldLength;

            Assert.That(hold.ClassicEndJudge(JudgeGrade.LateGreat, 0f), Is.EqualTo(JudgeGrade.LateGreat));
            Assert.That(hold.ClassicEndJudge(JudgeGrade.Perfect, 0f), Is.EqualTo(JudgeGrade.Perfect));
        }

        /// <summary>Checks that an early release beyond the perfect area gives a fast good.</summary>
        [Test]
        public void HoldClassicEndJudge_EarlyReleaseIsFastGood()
        {
            var hold = CreateHold();
            hold.Judged = true;
            _controller.ThisFrameSec = 1f + HoldLength - 0.5f;

            Assert.That(hold.ClassicEndJudge(JudgeGrade.Perfect, 0f), Is.EqualTo(JudgeGrade.FastGood));
        }

        /// <summary>Checks that a slightly early release is still perfect.</summary>
        [Test]
        public void HoldClassicEndJudge_SlightlyEarlyReleaseIsPerfect()
        {
            var hold = CreateHold();
            hold.Judged = true;
            _controller.ThisFrameSec = 1f + HoldLength - 0.1f;

            Assert.That(hold.ClassicEndJudge(JudgeGrade.FastPerfect2nd, 0f), Is.EqualTo(JudgeGrade.FastPerfect2nd));
        }

        /// <summary>Checks the late release areas of the classic end judgment, including the release offset.</summary>
        [Test]
        public void HoldClassicEndJudge_LateReleaseAreas()
        {
            var hold = CreateHold();
            hold.Judged = true;
            _controller.ThisFrameSec = 1f + HoldLength + 0.2f;

            Assert.That(hold.ClassicEndJudge(JudgeGrade.Perfect, 0f), Is.EqualTo(JudgeGrade.Perfect));

            _controller.ThisFrameSec = 1f + HoldLength + 0.3f;
            Assert.That(hold.ClassicEndJudge(JudgeGrade.Perfect, 0f), Is.EqualTo(JudgeGrade.LateGood));
            Assert.That(hold.ClassicEndJudge(JudgeGrade.Perfect, 0.25f), Is.EqualTo(JudgeGrade.Perfect));
        }

        /// <summary>Checks that the classic end judgment keeps unjudged, missed and too fast heads.</summary>
        [Test]
        public void HoldClassicEndJudge_KeepsUnjudgedAndMissedHeads()
        {
            var hold = CreateHold();
            _controller.ThisFrameSec = 1f + HoldLength - 0.5f;

            Assert.That(hold.ClassicEndJudge(JudgeGrade.Perfect, 0f), Is.EqualTo(JudgeGrade.Perfect));
            hold.Judged = true;
            Assert.That(hold.ClassicEndJudge(JudgeGrade.Miss, 0f), Is.EqualTo(JudgeGrade.Miss));
            Assert.That(hold.ClassicEndJudge(JudgeGrade.TooFast, 0f), Is.EqualTo(JudgeGrade.TooFast));
        }

        /// <summary>Checks the remaining time helpers measured from the answer and arrive timings.</summary>
        [Test]
        public void RemainingTime_IsClampedAtZero()
        {
            _environment.Settings.Debug.OffsetUnit = OffsetUnitOption.Second;
            _environment.Settings.Judge.JudgeOffset = 0.1f;
            var hold = CreateHold();
            _controller.ThisFrameSec = 1.5f;

            Assert.That(hold.RemainingTime, Is.EqualTo(1.6f).Within(1e-4f));
            Assert.That(hold.RemainingTimeWithoutOffset, Is.EqualTo(1.5f).Within(1e-4f));

            _controller.ThisFrameSec = 10f;
            Assert.That(hold.RemainingTime, Is.EqualTo(0f));
            Assert.That(hold.RemainingTimeWithoutOffset, Is.EqualTo(0f));
        }

        /// <summary>Creates a two second probe hold starting at one second.</summary>
        /// <returns>The probe.</returns>
        private ProbeLongDrop CreateHold()
        {
            var hold = _environment.Objects.Add<ProbeLongDrop>();
            hold.Timing = 1f;
            hold.AnswerTiming = 1f;
            hold.Length = HoldLength;
            return hold;
        }
    }
}
