using System;
using System.Collections;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Settings;
using MajdataPlay.Tests.Game.Notes.PlayMode.Doubles;
using MajdataPlay.Tests.Game.Notes.PlayMode.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode
{
    /// <summary>
    /// Covers the judgment windows, judge style conversion, autoplay and settings handling shared by every note through
    /// <see cref="MajdataPlay.Scenes.Game.Notes.Behaviours.NoteDrop"/>.
    /// </summary>
    public class NoteDropTests
    {
        /// <summary>The answer timing used by the probes.</summary>
        private const float AnswerTiming = 1f;

        /// <summary>The isolation scope of the current test.</summary>
        private NoteTestEnvironment _environment = null!;

        /// <summary>The note controller the probes read during Awake.</summary>
        private FakeNoteController _controller = null!;

        /// <summary>Gets the tap judgment window cases as offset in milliseconds and expected grade.</summary>
        private static object[] TapWindowCases => new object[]
        {
            new object[] { 0f, JudgeGrade.Perfect },
            new object[] { 10f, JudgeGrade.Perfect },
            new object[] { -10f, JudgeGrade.Perfect },
            new object[] { 25f, JudgeGrade.LatePerfect2nd },
            new object[] { -25f, JudgeGrade.FastPerfect2nd },
            new object[] { 40f, JudgeGrade.LatePerfect3rd },
            new object[] { -40f, JudgeGrade.FastPerfect3rd },
            new object[] { 60f, JudgeGrade.LateGreat },
            new object[] { -60f, JudgeGrade.FastGreat },
            new object[] { 75f, JudgeGrade.LateGreat2nd },
            new object[] { -75f, JudgeGrade.FastGreat2nd },
            new object[] { 90f, JudgeGrade.LateGreat3rd },
            new object[] { -90f, JudgeGrade.FastGreat3rd },
            new object[] { 120f, JudgeGrade.LateGood },
            new object[] { -120f, JudgeGrade.FastGood }
        };

        /// <summary>Gets the judge style conversion cases as style, input grade and expected grade.</summary>
        private static object[] ConversionCases => new object[]
        {
            new object[] { JudgeStyleOption.DEFAULT, JudgeGrade.LateGreat2nd, JudgeGrade.LateGreat2nd },
            new object[] { JudgeStyleOption.DEFAULT, JudgeGrade.FastGood, JudgeGrade.FastGood },
            new object[] { JudgeStyleOption.MAJI, JudgeGrade.Miss, JudgeGrade.Miss },
            new object[] { JudgeStyleOption.MAJI, JudgeGrade.LateGood, JudgeGrade.Miss },
            new object[] { JudgeStyleOption.MAJI, JudgeGrade.LateGreat3rd, JudgeGrade.LateGood },
            new object[] { JudgeStyleOption.MAJI, JudgeGrade.LateGreat2nd, JudgeGrade.LateGood },
            new object[] { JudgeStyleOption.MAJI, JudgeGrade.LateGreat, JudgeGrade.LateGood },
            new object[] { JudgeStyleOption.MAJI, JudgeGrade.LatePerfect3rd, JudgeGrade.LateGreat },
            new object[] { JudgeStyleOption.MAJI, JudgeGrade.LatePerfect2nd, JudgeGrade.LatePerfect2nd },
            new object[] { JudgeStyleOption.MAJI, JudgeGrade.Perfect, JudgeGrade.Perfect },
            new object[] { JudgeStyleOption.MAJI, JudgeGrade.FastPerfect2nd, JudgeGrade.FastPerfect2nd },
            new object[] { JudgeStyleOption.MAJI, JudgeGrade.FastPerfect3rd, JudgeGrade.FastGreat },
            new object[] { JudgeStyleOption.MAJI, JudgeGrade.FastGreat, JudgeGrade.FastGood },
            new object[] { JudgeStyleOption.MAJI, JudgeGrade.FastGreat2nd, JudgeGrade.FastGood },
            new object[] { JudgeStyleOption.MAJI, JudgeGrade.FastGreat3rd, JudgeGrade.FastGood },
            new object[] { JudgeStyleOption.MAJI, JudgeGrade.FastGood, JudgeGrade.TooFast },
            new object[] { JudgeStyleOption.MAJI, JudgeGrade.TooFast, JudgeGrade.TooFast },
            new object[] { JudgeStyleOption.GACHI, JudgeGrade.Miss, JudgeGrade.Miss },
            new object[] { JudgeStyleOption.GACHI, JudgeGrade.LateGood, JudgeGrade.Miss },
            new object[] { JudgeStyleOption.GACHI, JudgeGrade.LateGreat, JudgeGrade.Miss },
            new object[] { JudgeStyleOption.GACHI, JudgeGrade.LatePerfect3rd, JudgeGrade.LateGood },
            new object[] { JudgeStyleOption.GACHI, JudgeGrade.LatePerfect2nd, JudgeGrade.LateGreat },
            new object[] { JudgeStyleOption.GACHI, JudgeGrade.Perfect, JudgeGrade.Perfect },
            new object[] { JudgeStyleOption.GACHI, JudgeGrade.FastPerfect2nd, JudgeGrade.FastGreat },
            new object[] { JudgeStyleOption.GACHI, JudgeGrade.FastPerfect3rd, JudgeGrade.FastGood },
            new object[] { JudgeStyleOption.GACHI, JudgeGrade.FastGreat3rd, JudgeGrade.TooFast },
            new object[] { JudgeStyleOption.GACHI, JudgeGrade.TooFast, JudgeGrade.TooFast },
            new object[] { JudgeStyleOption.GORI, JudgeGrade.Miss, JudgeGrade.Miss },
            new object[] { JudgeStyleOption.GORI, JudgeGrade.LatePerfect2nd, JudgeGrade.Miss },
            new object[] { JudgeStyleOption.GORI, JudgeGrade.LateGood, JudgeGrade.Miss },
            new object[] { JudgeStyleOption.GORI, JudgeGrade.Perfect, JudgeGrade.Perfect },
            new object[] { JudgeStyleOption.GORI, JudgeGrade.FastPerfect2nd, JudgeGrade.TooFast },
            new object[] { JudgeStyleOption.GORI, JudgeGrade.TooFast, JudgeGrade.TooFast }
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

        /// <summary>Checks every tap judgment window boundary and the recorded offset.</summary>
        /// <param name="offsetMs">The hit offset from the answer timing in milliseconds.</param>
        /// <param name="expected">The expected grade.</param>
        [TestCaseSource(nameof(TapWindowCases))]
        public void Judge_MapsOffsetToTapWindow(float offsetMs, JudgeGrade expected)
        {
            var note = CreateNote();

            note.RunJudge(AnswerTiming + (offsetMs / 1000f));

            Assert.That(note.Judged, Is.True);
            Assert.That(note.Result, Is.EqualTo(expected));
            Assert.That(note.Diff, Is.EqualTo(offsetMs).Within(0.01f));
        }

        /// <summary>Checks that EX notes are always judged perfect while keeping the real offset.</summary>
        [Test]
        public void Judge_ExNoteIsAlwaysPerfect()
        {
            var note = CreateNote();
            note.IsEX = true;

            note.RunJudge(AnswerTiming + 0.12f);

            Assert.That(note.Result, Is.EqualTo(JudgeGrade.Perfect));
            Assert.That(note.Diff, Is.EqualTo(120f).Within(0.01f));
        }

        /// <summary>Checks that a judged note ignores further judgments.</summary>
        [Test]
        public void Judge_IgnoresSecondJudgment()
        {
            var note = CreateNote();
            note.RunJudge(AnswerTiming);

            note.RunJudge(AnswerTiming + 0.12f);

            Assert.That(note.Result, Is.EqualTo(JudgeGrade.Perfect));
            Assert.That(note.Diff, Is.EqualTo(0f).Within(0.01f));
        }

        /// <summary>Checks that the judgment offset setting shifts the windows.</summary>
        [Test]
        public void Judge_AppliesJudgeOffset()
        {
            _environment.Settings.Debug.OffsetUnit = OffsetUnitOption.Second;
            _environment.Settings.Judge.JudgeOffset = 0.1f;
            var note = CreateNote();

            note.RunJudge(AnswerTiming + 0.1f);

            Assert.That(note.JudgeTimingWithOffset, Is.EqualTo(AnswerTiming + 0.1f).Within(1e-5f));
            Assert.That(note.Result, Is.EqualTo(JudgeGrade.Perfect));
        }

        /// <summary>Checks the conversion of grades for every judge style.</summary>
        /// <param name="style">The judge style.</param>
        /// <param name="input">The original grade.</param>
        /// <param name="expected">The converted grade.</param>
        [TestCaseSource(nameof(ConversionCases))]
        public void ConvertJudgeGrade_FollowsJudgeStyle(JudgeStyleOption style, JudgeGrade input, JudgeGrade expected)
        {
            _controller.SetMods(AutoplayModeOption.Disable, style);
            var note = CreateNote();

            Assert.That(note.Convert(input), Is.EqualTo(expected));
        }

        /// <summary>Checks that a judgment is converted with the judge style before it is stored.</summary>
        [Test]
        public void Judge_ConvertsWithJudgeStyle()
        {
            _controller.SetMods(AutoplayModeOption.Disable, JudgeStyleOption.GORI);
            var note = CreateNote();

            note.RunJudge(AnswerTiming + 0.025f);

            Assert.That(note.Result, Is.EqualTo(JudgeGrade.Miss));
        }

        /// <summary>Checks that the start position only accepts keys from 1 to 8.</summary>
        [Test]
        public void StartPos_RejectsPositionsOutsideTheRing()
        {
            var note = CreateNote();

            note.StartPos = 8;

            Assert.That(note.StartPos, Is.EqualTo(8));
            Assert.Throws<ArgumentOutOfRangeException>(() => note.StartPos = 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => note.StartPos = 9);
        }

        /// <summary>Checks the state helpers and the clock exposed through the note controller.</summary>
        [Test]
        public void StateAndClock_AreReported()
        {
            var note = CreateNote();
            _controller.ThisFrameSec = 1.5f;

            Assert.That(note.IsInited, Is.False);
            note.SetState(NoteStatus.Running);
            Assert.That(note.IsInited, Is.True);
            Assert.That(note.IsEnded, Is.False);
            note.SetState(NoteStatus.End);
            Assert.That(note.IsEnded, Is.True);
            Assert.That(note.State, Is.EqualTo(NoteStatus.End));
            Assert.That(note.ThisFrameSec, Is.EqualTo(1.5f));
            Assert.That(note.TimeSpanToArrive, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(note.TimeSpanToJudge, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(note.TimeSpanToJudgeFrom(1.25f), Is.EqualTo(0.25f).Within(1e-5f));
        }

        /// <summary>Checks the simple note properties.</summary>
        [Test]
        public void Properties_RoundTrip()
        {
            var note = CreateNote();

            note.SortOrder = 12;
            note.Speed = 8.5f;
            note.IsEach = true;
            note.IsBreak = true;
            note.IsMine = true;

            Assert.That(note.SortOrder, Is.EqualTo(12));
            Assert.That(note.Speed, Is.EqualTo(8.5f));
            Assert.That(note.IsEach, Is.True);
            Assert.That(note.IsBreak, Is.True);
            Assert.That(note.IsMine, Is.True);
            Assert.That(note.IsClassic, Is.False);
        }

        /// <summary>Checks that Awake reads second based offsets, scales and judge options from the settings.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Awake_ReadsSettingsInSeconds()
        {
            var settings = _environment.Settings;
            settings.Debug.OffsetUnit = OffsetUnitOption.Second;
            settings.Judge.JudgeOffset = 0.5f;
            settings.Debug.DisplayOffset = 0.25f;
            settings.Judge.TouchPanelOffset = 0.125f;
            settings.Display.TapScale = 1.5f;
            settings.Game.SlideSkipping = false;
            settings.Judge.Mode = JudgeModeOption.Classic;
            settings.Debug.DJAutoPolicy = DJAutoPolicyOption.Permissive;
            _controller.SetMods(AutoplayModeOption.Enable, buttonRingForTouch: true);

            var note = CreateNote();
            yield return null;

            Assert.That(note.JudgeOffsetSec, Is.EqualTo(0.75f).Within(1e-5f));
            Assert.That(note.TouchPanelOffsetSec, Is.EqualTo(0.125f).Within(1e-5f));
            Assert.That(note.TapScale, Is.EqualTo(1.5f));
            Assert.That(note.SlideSkipping, Is.False);
            Assert.That(note.IsClassic, Is.True);
            Assert.That(note.DjAutoPolicy, Is.EqualTo(DJAutoPolicyOption.Permissive));
            Assert.That(note.Mode, Is.EqualTo(AutoplayModeOption.Enable));
            Assert.That(note.Autoplaying, Is.True);
            Assert.That(note.ButtonRingForTouch, Is.True);
            Assert.That(note.GameObject, Is.SameAs(note.gameObject));
            Assert.That(note.Transform, Is.SameAs(note.transform));
        }

        /// <summary>Checks that frame based offsets are converted to seconds.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Awake_ConvertsFrameOffsets()
        {
            var settings = _environment.Settings;
            settings.Debug.OffsetUnit = OffsetUnitOption.Frame;
            settings.Judge.JudgeOffset = 3f;
            settings.Judge.TouchPanelOffset = 6f;

            var note = CreateNote();
            yield return null;

            Assert.That(note.JudgeOffsetSec, Is.EqualTo(3f / 60f).Within(1e-5f));
            Assert.That(note.TouchPanelOffsetSec, Is.EqualTo(6f / 60f).Within(1e-5f));
            Assert.That(note.Autoplaying, Is.False);
        }

        /// <summary>Checks that destroying a note marks it inactive.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnDestroy_DeactivatesTheNote()
        {
            var note = CreateNote();
            note.SetActive(true);
            yield return null;

            Assert.That(note.Active, Is.True);
            Assert.That(note.gameObject.layer, Is.EqualTo(MajEnv.DEFAULT_LAYER));
            Object.Destroy(note.gameObject);
            yield return null;

            Assert.That(note.Active, Is.False);
        }

        /// <summary>Checks that hiding a component moves it to the hidden layer.</summary>
        [Test]
        public void SetActive_SwitchesLayer()
        {
            var note = CreateNote();

            note.SetActive(false);

            Assert.That(note.Active, Is.False);
            Assert.That(note.gameObject.layer, Is.EqualTo(MajEnv.HIDDEN_LAYER));
            Assert.That(note.InstanceID, Is.EqualTo(note.GetInstanceID()));
            Assert.That(note.Tag, Is.EqualTo("Untagged"));
        }

        /// <summary>Checks that autoplay does nothing while autoplay is disabled.</summary>
        [Test]
        public void Autoplay_DoesNothingWhenDisabled()
        {
            var note = CreateNote();
            _controller.ThisFrameSec = AnswerTiming;

            note.RunAutoplay();

            Assert.That(note.Judged, Is.False);
        }

        /// <summary>Checks that autoplay waits until one frame before the arrive timing.</summary>
        [Test]
        public void Autoplay_WaitsForArriveTiming()
        {
            _controller.SetMods(AutoplayModeOption.Enable);
            var note = CreateNote();
            _controller.ThisFrameSec = AnswerTiming - 0.05f;

            note.RunAutoplay();

            Assert.That(note.Judged, Is.False);
        }

        /// <summary>Checks the grade and offset sign assigned by autoplay.</summary>
        /// <param name="grade">The autoplay grade.</param>
        /// <param name="expectedDiff">The expected offset sign.</param>
        [TestCase(JudgeGrade.Perfect, 0f)]
        [TestCase(JudgeGrade.LateGreat, 1f)]
        [TestCase(JudgeGrade.FastGood, -1f)]
        public void Autoplay_UsesConfiguredGrade(JudgeGrade grade, float expectedDiff)
        {
            _controller.SetMods(AutoplayModeOption.Enable);
            _controller.AutoplayGrade = grade;
            var note = CreateNote();
            _controller.ThisFrameSec = AnswerTiming;

            note.RunAutoplay();

            Assert.That(note.Judged, Is.True);
            Assert.That(note.Result, Is.EqualTo(grade));
            Assert.That(note.Diff, Is.EqualTo(expectedDiff));
        }

        /// <summary>Checks that an out-of-range autoplay grade produces a random valid grade.</summary>
        [Test]
        public void Autoplay_PicksRandomGradeForUnknownValue()
        {
            _controller.SetMods(AutoplayModeOption.Enable);
            _controller.AutoplayGrade = (JudgeGrade)99;
            var note = CreateNote();
            _controller.ThisFrameSec = AnswerTiming;

            note.RunAutoplay();

            Assert.That(note.Judged, Is.True);
            Assert.That((int)note.Result, Is.InRange(0, 14));
        }

        /// <summary>Checks that autoplay leaves an already judged note alone.</summary>
        [Test]
        public void Autoplay_SkipsJudgedNote()
        {
            _controller.SetMods(AutoplayModeOption.Enable);
            _controller.AutoplayGrade = JudgeGrade.LateGood;
            var note = CreateNote();
            note.Judged = true;
            note.Result = JudgeGrade.Perfect;
            _controller.ThisFrameSec = AnswerTiming;

            note.RunAutoplay();

            Assert.That(note.Result, Is.EqualTo(JudgeGrade.Perfect));
        }

        /// <summary>Creates a probe note whose answer and arrive timing are one second.</summary>
        /// <returns>The probe.</returns>
        private ProbeNoteDrop CreateNote()
        {
            var note = _environment.Objects.Add<ProbeNoteDrop>();
            note.Timing = AnswerTiming;
            note.AnswerTiming = AnswerTiming;
            return note;
        }
    }
}
