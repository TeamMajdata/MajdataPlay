using System;
using System.Collections.Generic;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Settings;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.GamePlay.PlayMode.Support;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Counting
{
    /// <summary>
    /// Checks the scoring, combo, fast/late and result bookkeeping of <see cref="ObjectCounter"/> without its user
    /// interface, by reporting judgements of real note components that were never awakened.
    /// </summary>
    public class ObjectCounterScoringTests
    {
        /// <summary>Tracks objects created by a test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Restores the settings.</summary>
        private GamePlaySettingsScope _settings = null!;

        /// <summary>The counter under test.</summary>
        private ObjectCounter _counter = null!;

        /// <summary>A tap note.</summary>
        private TapDrop _tap = null!;

        /// <summary>A hold note.</summary>
        private HoldDrop _hold = null!;

        /// <summary>A touch note.</summary>
        private TouchDrop _touch = null!;

        /// <summary>A touch hold note.</summary>
        private TouchHoldDrop _touchHold = null!;

        /// <summary>A slide note.</summary>
        private SlideDrop _slide = null!;

        /// <summary>Creates a cleared counter in a normal session and one note of every kind.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            _settings = new GamePlaySettingsScope();
            _counter = _objects.Add<ObjectCounter>();
            PrivateMember.SetField(_counter, "_gameInfo", new GameInfo(GameMode.Normal, null!, null!));
            _counter.Clear();
            _tap = _objects.Add<TapDrop>();
            _hold = _objects.Add<HoldDrop>();
            _touch = _objects.Add<TouchDrop>();
            _touchHold = _objects.Add<TouchHoldDrop>();
            _slide = _objects.Add<SlideDrop>();
        }

        /// <summary>Clears the shared judgement tables and destroys every object.</summary>
        [TearDown]
        public void TearDown()
        {
            _counter.Clear();
            _objects.Dispose();
            _settings.Dispose();
        }

        /// <summary>Checks the base score of a note kind for every grade band.</summary>
        /// <param name="kind">The note kind: 0 tap, 1 hold, 2 touch, 3 touch hold, 4 slide.</param>
        /// <param name="baseScore">The base score of the note kind.</param>
        [TestCase(0, 500)]
        [TestCase(1, 1000)]
        [TestCase(2, 500)]
        [TestCase(3, 1000)]
        [TestCase(4, 1500)]
        public void ReportResult_ScoresEveryGradeBand(int kind, int baseScore)
        {
            Report(kind, JudgeGrade.Perfect);
            Report(kind, JudgeGrade.LatePerfect3rd);
            Report(kind, JudgeGrade.FastGreat2nd);
            Report(kind, JudgeGrade.LateGood);
            Report(kind, JudgeGrade.Miss);

            var expectedCurrent = baseScore + baseScore + (long)(baseScore * 0.8) + (long)(baseScore * 0.5);
            var expectedLost = (long)(baseScore * 0.2) + (long)(baseScore * 0.5) + baseScore;
            Assert.That(_counter.CurrentNoteBaseScore, Is.EqualTo(expectedCurrent));
            Assert.That(_counter.LostNoteBaseScore, Is.EqualTo(expectedLost));
            Assert.That(_counter.CurrentNoteExtraScore, Is.EqualTo(0));
            Assert.That(_counter.LostNoteExtraScore, Is.EqualTo(0));
        }

        /// <summary>Checks that the multiple of a folded note multiplies the score and the counts.</summary>
        [Test]
        public void ReportResult_AppliesTheMultiple()
        {
            _counter.ReportResult(_slide, new NoteJudgeResult { Grade = JudgeGrade.Perfect }, 3);

            Assert.That(_counter.CurrentNoteBaseScore, Is.EqualTo(4500));
            Assert.That(_counter.JudgeStats.SlideCriticalCount, Is.EqualTo(3));
            Assert.That(_counter.JudgeStats.SlideJudgedCount, Is.EqualTo(3));
            Assert.That(PrivateMember.GetField<long>(_counter, "_combo"), Is.EqualTo(3));
        }

        /// <summary>Checks the break score table for every grade.</summary>
        /// <param name="grade">The judge grade.</param>
        /// <param name="current">The expected current base score.</param>
        /// <param name="currentExtra">The expected current extra score.</param>
        /// <param name="currentClassic">The expected current classic extra score.</param>
        /// <param name="lost">The expected lost base score.</param>
        /// <param name="lostExtra">The expected lost extra score.</param>
        /// <param name="lostClassic">The expected lost classic extra score.</param>
        [TestCase(JudgeGrade.Perfect, 2500, 100, 100, 0, 0, 0)]
        [TestCase(JudgeGrade.FastPerfect2nd, 2500, 75, 50, 0, 25, 50)]
        [TestCase(JudgeGrade.LatePerfect3rd, 2500, 50, 0, 0, 50, 100)]
        [TestCase(JudgeGrade.LateGreat, 2000, 40, 0, 500, 60, 100)]
        [TestCase(JudgeGrade.FastGreat2nd, 1500, 40, 0, 1000, 60, 100)]
        [TestCase(JudgeGrade.FastGreat3rd, 1250, 40, 0, 1250, 60, 100)]
        [TestCase(JudgeGrade.FastGood, 1000, 30, 0, 1500, 70, 100)]
        [TestCase(JudgeGrade.TooFast, 0, 0, 0, 2500, 100, 100)]
        public void ReportResult_ScoresBreaks(JudgeGrade grade, int current, int currentExtra, int currentClassic, int lost, int lostExtra, int lostClassic)
        {
            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = grade, IsBreak = true, Diff = -1f });

            Assert.That(_counter.CurrentNoteBaseScore, Is.EqualTo(current));
            Assert.That(_counter.CurrentNoteExtraScore, Is.EqualTo(currentExtra));
            Assert.That(_counter.CurrentNoteExtraScoreClassic, Is.EqualTo(currentClassic));
            Assert.That(_counter.LostNoteBaseScore, Is.EqualTo(lost));
            Assert.That(_counter.LostNoteExtraScore, Is.EqualTo(lostExtra));
            Assert.That(_counter.LostNoteExtraScoreClassic, Is.EqualTo(lostClassic));
            Assert.That(_counter.CurrentNoteScore, Is.EqualTo(current + currentExtra));
            Assert.That(_counter.CurrentNoteScoreClassic, Is.EqualTo(current + currentClassic));
            Assert.That(_counter.TotalLostNoteScore, Is.EqualTo(lost + lostExtra));
            Assert.That(_counter.TotalLostNoteScoreClassic, Is.EqualTo(lost + lostClassic));
            Assert.That(_counter.JudgeStats.BreakJudgedCount, Is.EqualTo(1));
            Assert.That(_counter.JudgeStats.TapJudgedCount, Is.EqualTo(0), "A break is not counted as a tap");
        }

        /// <summary>Checks that every grade lands in the critical, perfect, great, good or miss column of its note kind.</summary>
        [Test]
        public void ReportResult_CountsGradesPerKind()
        {
            Report(0, JudgeGrade.Perfect);
            Report(0, JudgeGrade.FastPerfect2nd);
            Report(1, JudgeGrade.LateGreat3rd);
            Report(2, JudgeGrade.FastGood);
            Report(3, JudgeGrade.TooFast);
            Report(4, JudgeGrade.LatePerfect2nd);

            ref readonly var stats = ref _counter.JudgeStats;
            Assert.That(stats.TapCriticalCount, Is.EqualTo(1));
            Assert.That(stats.TapPerfectCount, Is.EqualTo(1));
            Assert.That(stats.TapJudgedCount, Is.EqualTo(2));
            Assert.That(stats.HoldGreatCount, Is.EqualTo(1));
            Assert.That(stats.HoldMissCount, Is.EqualTo(1), "Touch holds are counted as holds");
            Assert.That(stats.HoldJudgedCount, Is.EqualTo(2));
            Assert.That(stats.TouchGoodCount, Is.EqualTo(1));
            Assert.That(stats.TouchJudgedCount, Is.EqualTo(1));
            Assert.That(stats.SlidePerfectCount, Is.EqualTo(1));
            Assert.That(stats.SlideJudgedCount, Is.EqualTo(1));
            Assert.That(stats.TotalCriticalCount, Is.EqualTo(1));
            Assert.That(stats.TotalPerfectCount, Is.EqualTo(2));
            Assert.That(stats.TotalGreatCount, Is.EqualTo(1));
            Assert.That(stats.TotalGoodCount, Is.EqualTo(1));
            Assert.That(stats.TotalMissCount, Is.EqualTo(1));
            Assert.That(stats.TotalJudgedCount, Is.EqualTo(6));
            Assert.That(stats.BreakJudgedCount, Is.EqualTo(0));
        }

        /// <summary>Checks the combo, perfect combo, critical combo and lost DX score for each grade band.</summary>
        [Test]
        public void ReportResult_TracksCombosAndDxScore()
        {
            Report(0, JudgeGrade.Perfect);
            Report(0, JudgeGrade.Perfect);
            AssertCombos(2, 2, 2, 0);

            Report(0, JudgeGrade.FastPerfect3rd);
            AssertCombos(3, 3, 0, -1);

            Report(0, JudgeGrade.LateGreat2nd);
            AssertCombos(4, 0, 0, -3);

            Report(0, JudgeGrade.FastGood);
            AssertCombos(5, 0, 0, -6);

            Report(0, JudgeGrade.Miss);
            AssertCombos(0, 0, 0, -9);

            Report(0, JudgeGrade.TooFast);
            AssertCombos(0, 0, 0, -12);
        }

        /// <summary>Checks the last judge difference, the outline request and the timing samples of taps and holds.</summary>
        [Test]
        public void ReportResult_RecordsTimingOfTapsAndHolds()
        {
            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = JudgeGrade.FastGreat, Diff = -42f });
            _counter.ReportResult(_hold, new NoteJudgeResult { Grade = JudgeGrade.LateGreat, Diff = 37f });
            _counter.ReportResult(_slide, new NoteJudgeResult { Grade = JudgeGrade.LateGreat, Diff = 99f });
            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = JudgeGrade.Perfect, Diff = 5f, IsMine = true });
            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = JudgeGrade.Miss, Diff = 150f });

            var diffs = PrivateMember.GetField<List<float>>(_counter, "_noteJudgeDiffList");
            Assert.That(diffs, Is.EqualTo(new[] { -42f, 37f }));
            Assert.That(PrivateMember.GetField<float>(_counter, "_lastJudgeDiff"), Is.EqualTo(5f), "Slides and misses do not change the last difference");
            Assert.That(PrivateMember.GetField<bool>(_counter, "_isOutlinePlayRequested"), Is.True);
        }

        /// <summary>Checks fast/late counting for every display option.</summary>
        /// <param name="option">The fast/late display option of notes.</param>
        /// <param name="expectedFast">The expected fast count.</param>
        /// <param name="expectedLate">The expected late count.</param>
        [TestCase(JudgeDisplayOption.All, 3, 2)]
        [TestCase(JudgeDisplayOption.BelowCP, 2, 1)]
        [TestCase(JudgeDisplayOption.BelowP, 1, 1)]
        [TestCase(JudgeDisplayOption.BelowGR, 1, 1)]
        [TestCase(JudgeDisplayOption.Disable, 1, 1)]
        [TestCase(JudgeDisplayOption.MissOnly, 0, 0)]
        public void ReportResult_CountsFastAndLateByDisplayOption(JudgeDisplayOption option, int expectedFast, int expectedLate)
        {
            PrivateMember.SetField(_counter, "_noteFastLateDisplayOption", option);

            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = JudgeGrade.Perfect, Diff = -3f });
            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = JudgeGrade.Perfect, Diff = 2f });
            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = JudgeGrade.Perfect, Diff = 0f });
            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = JudgeGrade.FastPerfect2nd, Diff = -20f });
            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = JudgeGrade.FastGreat, Diff = -60f });
            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = JudgeGrade.LateGood, Diff = 120f });
            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = JudgeGrade.Miss, Diff = 200f });

            Assert.That(_counter.JudgeStats.TotalFastCount, Is.EqualTo(expectedFast));
            Assert.That(_counter.JudgeStats.TotalLateCount, Is.EqualTo(expectedLate));
        }

        /// <summary>Checks that breaks use their own fast/late display option.</summary>
        [Test]
        public void ReportResult_BreaksUseTheBreakFastLateOption()
        {
            PrivateMember.SetField(_counter, "_noteFastLateDisplayOption", JudgeDisplayOption.Disable);
            PrivateMember.SetField(_counter, "_breakFastLateDisplayOption", JudgeDisplayOption.All);

            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = JudgeGrade.Perfect, Diff = -3f, IsBreak = true });
            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = JudgeGrade.Perfect, Diff = -3f });

            Assert.That(_counter.JudgeStats.TotalFastCount, Is.EqualTo(1));
        }

        /// <summary>Checks that every note kind must be fully judged before the chart is finished.</summary>
        [Test]
        public void AllFinished_ComparesJudgedCountsWithSums()
        {
            Assert.That(_counter.AllFinished, Is.True, "An empty chart is finished");

            PrivateMember.SetProperty(_counter, nameof(ObjectCounter.TapSum), 1);
            PrivateMember.SetProperty(_counter, nameof(ObjectCounter.BreakSum), 1);
            Assert.That(_counter.AllFinished, Is.False);

            Report(0, JudgeGrade.Perfect);
            Assert.That(_counter.AllFinished, Is.False);

            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = JudgeGrade.Perfect, IsBreak = true });
            Assert.That(_counter.AllFinished, Is.True);
        }

        /// <summary>Checks the achievement rates of a chart without breaks.</summary>
        [Test]
        public void UpdateAccRate_WithoutBreaks()
        {
            PrivateMember.SetProperty(_counter, nameof(ObjectCounter.TotalNoteBaseScore), 1000L);
            Report(0, JudgeGrade.Perfect);
            Report(0, JudgeGrade.FastGreat);

            PrivateMember.Invoke(_counter, "UpdateAccRate");

            var acc = _counter.AccurateStats;
            Assert.That(acc.ClassicAchievement_A, Is.EqualTo(90).Within(1e-9));
            Assert.That(acc.ClassicAchievement_B, Is.EqualTo(90).Within(1e-9));
            Assert.That(acc.Achievement_A, Is.EqualTo(90).Within(1e-9));
            Assert.That(acc.Achievement_B, Is.EqualTo(90).Within(1e-9));
            Assert.That(acc.Achievement_C, Is.EqualTo(90).Within(1e-9));
        }

        /// <summary>Checks the achievement rates of a perfect break.</summary>
        [Test]
        public void UpdateAccRate_WithAPerfectBreak()
        {
            PrivateMember.SetProperty(_counter, nameof(ObjectCounter.TotalNoteBaseScore), 2500L);
            PrivateMember.SetProperty(_counter, nameof(ObjectCounter.TotalNoteExtraScore), 100L);
            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = JudgeGrade.Perfect, IsBreak = true });

            PrivateMember.Invoke(_counter, "UpdateAccRate");

            var acc = _counter.AccurateStats;
            Assert.That(acc.ClassicAchievement_A, Is.EqualTo(104).Within(1e-9));
            Assert.That(acc.ClassicAchievement_B, Is.EqualTo(104).Within(1e-9));
            Assert.That(acc.Achievement_A, Is.EqualTo(101).Within(1e-9));
            Assert.That(acc.Achievement_B, Is.EqualTo(101).Within(1e-9));
            Assert.That(acc.Achievement_C, Is.EqualTo(101).Within(1e-9));
            Assert.That(_counter.TotalNoteScore, Is.EqualTo(2600));
        }

        /// <summary>Checks that clearing restores the initial achievement rates and zeroes every counter.</summary>
        [Test]
        public void Clear_ResetsEveryCounter()
        {
            PrivateMember.SetProperty(_counter, nameof(ObjectCounter.TapSum), 4);
            Report(0, JudgeGrade.FastGood);

            _counter.Clear();

            var acc = _counter.AccurateStats;
            Assert.That((acc.ClassicAchievement_A, acc.ClassicAchievement_B, acc.Achievement_A, acc.Achievement_B, acc.Achievement_C), Is.EqualTo((0d, 100d, 101d, 100d, 0d)));
            Assert.That(_counter.TapSum, Is.EqualTo(0));
            Assert.That(_counter.JudgeStats.TotalJudgedCount, Is.EqualTo(0));
            Assert.That(PrivateMember.GetField<long>(_counter, "_combo"), Is.EqualTo(0));
            Assert.That(PrivateMember.GetField<long>(_counter, "_lostDXScore"), Is.EqualTo(0));
            Assert.That(PrivateMember.GetField<float>(_counter, "_diffTimer"), Is.EqualTo(3f));
            Assert.That(_counter.GetCurrentTotalJudgeInfo().All, Is.EqualTo(0));
        }

        /// <summary>Checks the totals returned for the judged notes so far.</summary>
        [Test]
        public void GetCurrentTotalJudgeInfo_UnpacksTheTotals()
        {
            Report(0, JudgeGrade.Perfect);
            Report(1, JudgeGrade.FastPerfect2nd);
            Report(2, JudgeGrade.LateGreat);
            Report(4, JudgeGrade.FastGood);
            Report(3, JudgeGrade.Miss);

            var info = _counter.GetCurrentTotalJudgeInfo();

            Assert.That((info.CriticalPerfect, info.Perfect, info.Great, info.Good, info.Miss), Is.EqualTo((1L, 1L, 1L, 1L, 1L)));
            Assert.That(info.Fast, Is.EqualTo(2));
            Assert.That(info.Late, Is.EqualTo(1));
        }

        /// <summary>Checks the combo state of the result for every outcome.</summary>
        /// <param name="tapGrade">The grade of the tap.</param>
        /// <param name="breakGrade">The grade of the break.</param>
        /// <param name="expected">The expected combo state.</param>
        [TestCase(JudgeGrade.Perfect, JudgeGrade.Perfect, ComboState.APPlus)]
        [TestCase(JudgeGrade.Perfect, JudgeGrade.LatePerfect2nd, ComboState.AP)]
        [TestCase(JudgeGrade.FastGreat, JudgeGrade.Perfect, ComboState.FCPlus)]
        [TestCase(JudgeGrade.Perfect, JudgeGrade.LateGood, ComboState.FC)]
        [TestCase(JudgeGrade.Miss, JudgeGrade.Perfect, ComboState.None)]
        public void GetPlayRecord_DerivesTheComboState(JudgeGrade tapGrade, JudgeGrade breakGrade, ComboState expected)
        {
            Report(0, tapGrade);
            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = breakGrade, IsBreak = true });

            var result = _counter.GetPlayRecord(new GamePlayFakeSongDetail(), ChartLevel.Expert);

            Assert.That(result.ComboState, Is.EqualTo(expected));
        }

        /// <summary>Checks the judge record, DX score, fast/late and samples of a result.</summary>
        [Test]
        public void GetPlayRecord_ReportsTheJudgeRecordAndScores()
        {
            var song = new GamePlayFakeSongDetail();
            PrivateMember.SetField(_counter, "_totalDXScore", 15L);
            PrivateMember.SetField(_counter, "_noteFastLateDisplayOption", JudgeDisplayOption.All);
            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = JudgeGrade.Perfect, Diff = -1f });
            _counter.ReportResult(_hold, new NoteJudgeResult { Grade = JudgeGrade.Perfect, Diff = 2f });
            _counter.ReportResult(_touchHold, new NoteJudgeResult { Grade = JudgeGrade.Perfect, Diff = 1f });
            _counter.ReportResult(_touch, new NoteJudgeResult { Grade = JudgeGrade.LateGreat, Diff = 50f });
            _counter.ReportResult(_slide, new NoteJudgeResult { Grade = JudgeGrade.FastPerfect2nd, Diff = -20f });

            var result = _counter.GetPlayRecord(song, ChartLevel.Master);

            Assert.That(result.SongDetail, Is.SameAs(song));
            Assert.That(result.Level, Is.EqualTo(ChartLevel.Master));
            Assert.That(result.JudgeRecord[ScoreNoteType.Tap][JudgeGrade.Perfect], Is.EqualTo(1));
            Assert.That(result.JudgeRecord[ScoreNoteType.Hold][JudgeGrade.Perfect], Is.EqualTo(2), "Touch holds join the hold record");
            Assert.That(result.JudgeRecord[ScoreNoteType.Touch][JudgeGrade.LateGreat], Is.EqualTo(1));
            Assert.That(result.JudgeRecord[ScoreNoteType.Slide][JudgeGrade.FastPerfect2nd], Is.EqualTo(1));
            Assert.That(result.TotalDXScore, Is.EqualTo(15));
            Assert.That(result.DXScore, Is.EqualTo(15 - 2 - 1));
            Assert.That(result.Fast, Is.EqualTo(2));
            Assert.That(result.Late, Is.EqualTo(3));
            Assert.That(result.NoteJudgeDiffs.ToArray(), Is.EqualTo(new[] { -1f, 2f }));
            Assert.That(result.Acc.DX, Is.EqualTo(0d));
            Assert.That(result.ComboState, Is.EqualTo(ComboState.FCPlus));
        }

        /// <summary>Checks that a recorded result keeps its judge record after the counter is cleared for the next chart.</summary>
        [Test]
        [Ignore("Known issue: ObjectCounter.GetPlayRecord wraps its static per-type dictionaries in JudgeInfo without copying, so Clear and later records overwrite the judge records of earlier results.")]
        public void GetPlayRecord_ResultSurvivesClear()
        {
            Report(0, JudgeGrade.Perfect);
            var result = _counter.GetPlayRecord(new GamePlayFakeSongDetail(), ChartLevel.Basic);

            _counter.Clear();

            Assert.That(result.JudgeRecord[ScoreNoteType.Tap][JudgeGrade.Perfect], Is.EqualTo(1));
        }

        /// <summary>Checks that a chart without breaks reaches AP+ when every note is a critical perfect.</summary>
        [Test]
        [Ignore("Known issue: ObjectCounter.GetPlayRecord requires the break record to be theoretical, which needs at least one judged break, so charts without breaks can never reach AP+.")]
        public void GetPlayRecord_AllCriticalWithoutBreaksIsAPPlus()
        {
            Report(0, JudgeGrade.Perfect);

            var result = _counter.GetPlayRecord(new GamePlayFakeSongDetail(), ChartLevel.Basic);

            Assert.That(result.ComboState, Is.EqualTo(ComboState.APPlus));
        }

        /// <summary>Checks that dan life loses health on judgements and a forced game over is requested at zero life.</summary>
        [Test]
        public void ReportResult_UpdatesDanLife()
        {
            var info = new GameInfo(GameMode.Dan, new ISongDetail[] { new GamePlayFakeSongDetail() }, new[] { ChartLevel.Easy })
            {
                DanInfo = new DanInfo { StartHP = 5, IsForceGameover = true },
            };
            info.MaxHP = 5;
            info.CurrentHP = 5;
            var manager = _objects.Add<GamePlayManager>();
            PrivateMember.SetProperty(manager, nameof(GamePlayManager.State), GamePlayStatus.Ended);
            PrivateMember.SetField(_counter, "_gameInfo", info);
            PrivateMember.SetField(_counter, "_gpManager", manager);

            Report(0, JudgeGrade.LateGood);
            Assert.That(info.CurrentHP, Is.EqualTo(2));

            Report(0, JudgeGrade.Miss);
            Assert.That(info.CurrentHP, Is.EqualTo(0));
            Assert.That(manager.State, Is.EqualTo(GamePlayStatus.Ended));
        }

        /// <summary>Checks that dan life is not touched in view mode.</summary>
        [Test]
        public void ReportResult_IgnoresDanLifeInViewMode()
        {
            var info = new GameInfo(GameMode.Dan, new ISongDetail[] { new GamePlayFakeSongDetail() }, new[] { ChartLevel.Easy })
            {
                DanInfo = new DanInfo { StartHP = 5 },
            };
            info.MaxHP = 5;
            info.CurrentHP = 5;
            PrivateMember.SetField(_counter, "_gameInfo", info);
            MajEnv.Mode = RunningMode.View;

            Report(0, JudgeGrade.Miss);

            Assert.That(info.CurrentHP, Is.EqualTo(5));
        }

        /// <summary>Reports a judgement for one of the note kinds.</summary>
        /// <param name="kind">The note kind: 0 tap, 1 hold, 2 touch, 3 touch hold, 4 slide.</param>
        /// <param name="grade">The judge grade.</param>
        private void Report(int kind, JudgeGrade grade)
        {
            var result = new NoteJudgeResult { Grade = grade, Diff = grade > JudgeGrade.Perfect ? -10f : 10f };
            switch (kind)
            {
                case 0:
                    _counter.ReportResult(_tap, result);
                    break;
                case 1:
                    _counter.ReportResult(_hold, result);
                    break;
                case 2:
                    _counter.ReportResult(_touch, result);
                    break;
                case 3:
                    _counter.ReportResult(_touchHold, result);
                    break;
                case 4:
                    _counter.ReportResult(_slide, result);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        /// <summary>Asserts the combo counters and the lost DX score.</summary>
        /// <param name="combo">The expected combo.</param>
        /// <param name="perfectCombo">The expected perfect combo.</param>
        /// <param name="criticalCombo">The expected critical perfect combo.</param>
        /// <param name="lostDxScore">The expected lost DX score.</param>
        private void AssertCombos(long combo, long perfectCombo, long criticalCombo, long lostDxScore)
        {
            Assert.That(PrivateMember.GetField<long>(_counter, "_combo"), Is.EqualTo(combo), "Combo");
            Assert.That(PrivateMember.GetField<long>(_counter, "_pCombo"), Is.EqualTo(perfectCombo), "Perfect combo");
            Assert.That(PrivateMember.GetField<long>(_counter, "_cPCombo"), Is.EqualTo(criticalCombo), "Critical combo");
            Assert.That(PrivateMember.GetField<long>(_counter, "_lostDXScore"), Is.EqualTo(lostDxScore), "Lost DX score");
        }
    }
}
