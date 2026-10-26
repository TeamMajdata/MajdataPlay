using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Settings;
using MajdataPlay.Tests.Game.GamePlay.PlayMode.Support;
using MajdataPlay.Tests.Game.Notes.PlayMode.Doubles;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Counting
{
    /// <summary>
    /// Checks the state <see cref="ObjectCounter.CaptureDebugInfo"/> captures for the object counter inspector and the
    /// cross-check of the counters in <see cref="ObjectCounterDebugInfo.FindInconsistencies"/>.
    /// </summary>
    public class ObjectCounterDebugInfoTests
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

        /// <summary>A touch hold note.</summary>
        private TouchHoldDrop _touchHold = null!;

        /// <summary>Creates a cleared counter in a normal session and the notes the tests report.</summary>
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
            _touchHold = _objects.Add<TouchHoldDrop>();
        }

        /// <summary>Clears the shared judgement tables and destroys every object.</summary>
        [TearDown]
        public void TearDown()
        {
            _counter.Clear();
            _objects.Dispose();
            _settings.Dispose();
        }

        /// <summary>Checks that the captured state holds the private combos, DX score, timing and options of the counter.</summary>
        [Test]
        public void CaptureDebugInfo_CapturesThePrivateState()
        {
            PrivateMember.SetField(_counter, "_totalDXScore", 9L);
            PrivateMember.SetField(_counter, "_noteFastLateDisplayOption", JudgeDisplayOption.All);
            PrivateMember.SetField(_counter, "_breakFastLateDisplayOption", JudgeDisplayOption.BelowCP);
            PrivateMember.SetField(_counter, "_centerInfoDisplayOption", BGInfoOption.Combo);
            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = JudgeGrade.Perfect, Diff = -4f });
            _counter.ReportResult(_hold, new NoteJudgeResult { Grade = JudgeGrade.FastGreat, Diff = -60f });

            var info = _counter.CaptureDebugInfo();

            Assert.That((info.Combo, info.PerfectCombo, info.CriticalPerfectCombo), Is.EqualTo((2L, 0L, 0L)));
            Assert.That(info.TotalDXScore, Is.EqualTo(9));
            Assert.That(info.LostDXScore, Is.EqualTo(-2));
            Assert.That(info.DXScore, Is.EqualTo(7));
            Assert.That(info.LastJudgeDiffMSec, Is.EqualTo(-60f));
            Assert.That(info.DiffDisplayTimer, Is.EqualTo(3f));
            Assert.That(info.NoteJudgeDiffsMSec, Is.EqualTo(new[] { -4f, -60f }));
            Assert.That(info.NoteFastLateDisplayOption, Is.EqualTo(JudgeDisplayOption.All));
            Assert.That(info.BreakFastLateDisplayOption, Is.EqualTo(JudgeDisplayOption.BelowCP));
            Assert.That(info.CenterInfoOption, Is.EqualTo(BGInfoOption.Combo));
            Assert.That(info.TopInfoOption, Is.EqualTo(MajEnv.Settings.Game.TopInfo));
            Assert.That(info.IsJudgeTimingGaugeEnabled, Is.False);
            Assert.That(info.JudgeTimingGaugeAverageMSec, Is.EqualTo(0f), "Without gauge scales the average stays centred");
            Assert.That(info.IsDanLifeEnabled, Is.False);
        }

        /// <summary>Checks that the captured state holds the public sums, statistics and scores of the counter.</summary>
        [Test]
        public void CaptureDebugInfo_CapturesTheSumsAndScores()
        {
            PrivateMember.SetProperty(_counter, nameof(ObjectCounter.TapSum), 2);
            PrivateMember.SetProperty(_counter, nameof(ObjectCounter.NoteSum), 2);
            PrivateMember.SetProperty(_counter, nameof(ObjectCounter.TotalNoteBaseScore), 1000L);
            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = JudgeGrade.LateGood, Diff = 120f });

            var info = _counter.CaptureDebugInfo();

            Assert.That((info.TapSum, info.NoteSum), Is.EqualTo((2, 2)));
            Assert.That(info.AllFinished, Is.False);
            Assert.That(info.JudgeStats.TapGoodCount, Is.EqualTo(1));
            Assert.That(info.JudgeStats.TotalJudgedCount, Is.EqualTo(1));
            Assert.That(info.AccurateStats.Achievement_B, Is.EqualTo(_counter.AccurateStats.Achievement_B));
            Assert.That(info.TotalNoteBaseScore, Is.EqualTo(1000));
            Assert.That(info.CurrentNoteBaseScore, Is.EqualTo(250));
            Assert.That(info.LostNoteBaseScore, Is.EqualTo(250));
        }

        /// <summary>Checks that the per-grade counts keep holds and touch holds apart, and breaks apart from their note kind.</summary>
        [Test]
        public void CaptureDebugInfo_CapturesThePerGradeCounts()
        {
            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = JudgeGrade.Perfect });
            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = JudgeGrade.FastGood, IsBreak = true });
            _counter.ReportResult(_hold, new NoteJudgeResult { Grade = JudgeGrade.LateGreat2nd });
            _counter.ReportResult(_touchHold, new NoteJudgeResult { Grade = JudgeGrade.Miss }, 2);

            var info = _counter.CaptureDebugInfo();

            Assert.That(info.TapGradeCounts[(int)JudgeGrade.Perfect], Is.EqualTo(1));
            Assert.That(info.TapGradeCounts[(int)JudgeGrade.FastGood], Is.EqualTo(0), "A break is not counted as a tap");
            Assert.That(info.BreakGradeCounts[(int)JudgeGrade.FastGood], Is.EqualTo(1));
            Assert.That(info.HoldGradeCounts[(int)JudgeGrade.LateGreat2nd], Is.EqualTo(1));
            Assert.That(info.HoldGradeCounts[(int)JudgeGrade.Miss], Is.EqualTo(0));
            Assert.That(info.TouchHoldGradeCounts[(int)JudgeGrade.Miss], Is.EqualTo(2));
            Assert.That(info.TotalGradeCounts[(int)JudgeGrade.Miss], Is.EqualTo(2));
            Assert.That(info.TotalGradeCounts.Length, Is.EqualTo(15));
        }

        /// <summary>Checks that a snapshot does not change when the counter goes on counting or is cleared.</summary>
        [Test]
        public void CaptureDebugInfo_IsASnapshot()
        {
            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = JudgeGrade.Perfect, Diff = 1f });
            var info = _counter.CaptureDebugInfo();

            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = JudgeGrade.Perfect, Diff = 2f });
            _counter.Clear();

            Assert.That(info.TapGradeCounts[(int)JudgeGrade.Perfect], Is.EqualTo(1));
            Assert.That(info.NoteJudgeDiffsMSec, Is.EqualTo(new[] { 1f }));
            Assert.That(info.JudgeStats.TapJudgedCount, Is.EqualTo(1));
            Assert.That(info.Combo, Is.EqualTo(1));
        }

        /// <summary>Checks that the dan life is captured only when it is enabled.</summary>
        [Test]
        public void CaptureDebugInfo_CapturesTheDanLife()
        {
            var info = new GameInfo(GameMode.Dan, new ISongDetail[] { new GamePlayFakeSongDetail() }, new[] { ChartLevel.Easy })
            {
                DanInfo = new DanInfo { StartHP = 5 },
            };
            info.MaxHP = 5;
            info.CurrentHP = 4;
            PrivateMember.SetField(_counter, "_gameInfo", info);

            var debugInfo = _counter.CaptureDebugInfo();

            Assert.That(debugInfo.IsDanLifeEnabled, Is.True);
            Assert.That((debugInfo.CurrentHP, debugInfo.MaxHP), Is.EqualTo((4, 5)));
        }

        /// <summary>Checks that counting notes of every kind within the note sums keeps the counters consistent.</summary>
        [Test]
        public void FindInconsistencies_AcceptsConsistentCounters()
        {
            PrivateMember.SetProperty(_counter, nameof(ObjectCounter.TapSum), 1);
            PrivateMember.SetProperty(_counter, nameof(ObjectCounter.HoldSum), 3);
            PrivateMember.SetProperty(_counter, nameof(ObjectCounter.BreakSum), 1);
            PrivateMember.SetProperty(_counter, nameof(ObjectCounter.NoteSum), 5);
            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = JudgeGrade.FastPerfect2nd });
            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = JudgeGrade.TooFast, IsBreak = true });
            _counter.ReportResult(_hold, new NoteJudgeResult { Grade = JudgeGrade.LateGood });
            _counter.ReportResult(_touchHold, new NoteJudgeResult { Grade = JudgeGrade.Perfect }, 2);

            Assert.That(_counter.CaptureDebugInfo().FindInconsistencies(), Is.Empty);
        }

        /// <summary>Checks that judging more notes of a kind than the chart has is reported for the kind and the total.</summary>
        [Test]
        public void FindInconsistencies_ReportsMoreJudgmentsThanNotes()
        {
            PrivateMember.SetProperty(_counter, nameof(ObjectCounter.TapSum), 1);
            PrivateMember.SetProperty(_counter, nameof(ObjectCounter.NoteSum), 1);
            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = JudgeGrade.Perfect });
            _counter.ReportResult(_tap, new NoteJudgeResult { Grade = JudgeGrade.Perfect });

            var problems = _counter.CaptureDebugInfo().FindInconsistencies();

            Assert.That(problems, Is.EqualTo(new[]
            {
                "Tap: 2 judged, but the chart has 1.",
                "Total: 2 judged, but the chart has 1."
            }));
        }

        /// <summary>Checks that per-grade counts that disagree with the judged count are reported.</summary>
        [Test]
        public void FindInconsistencies_ReportsPerGradeCountsThatDisagree()
        {
            var gradeCounts = new int[15];
            gradeCounts[(int)JudgeGrade.Perfect] = 1;
            var info = new ObjectCounterDebugInfo { SlideSum = 1, NoteSum = 1, SlideGradeCounts = gradeCounts };

            Assert.That(info.FindInconsistencies(), Is.EqualTo(new[] { "Slide: 0 judged, but the per-grade counts add up to 1." }));
        }

        /// <summary>
        /// Checks that a note of a kind the counter does not know, which only reaches the total judged count, is reported
        /// by the total cross-checks.
        /// </summary>
        [Test]
        public void FindInconsistencies_ReportsAJudgmentOfAnUnknownNoteKind()
        {
            PrivateMember.SetProperty(_counter, nameof(ObjectCounter.NoteSum), 1);
            var unknown = _objects.Add<ProbeNoteDrop>();
            _counter.ReportResult(unknown, new NoteJudgeResult { Grade = JudgeGrade.Perfect });

            var problems = _counter.CaptureDebugInfo().FindInconsistencies();

            Assert.That(problems, Is.EqualTo(new[]
            {
                "Total: 1 judged, but the critical to miss counts add up to 0.",
                "Total: 1 judged, but the note kinds add up to 0."
            }));
        }
    }
}
