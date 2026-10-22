using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Controllers;
using MajdataPlay.Settings;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Notes.PlayMode.Doubles;
using MajdataPlay.Tests.Game.Notes.PlayMode.Support;
using MajSimai;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode
{
    /// <summary>
    /// Covers <see cref="NoteAudioManager"/>: sound requests for every judgment, the touch hold riser, playback of the
    /// requested samples and the answer sound schedule generated from a chart.
    /// </summary>
    public class NoteAudioManagerTests
    {
        /// <summary>The gameplay objects of the current test.</summary>
        private GameplayRig _rig = null!;

        /// <summary>Gets the tap sound cases as grade, break, EX and mine flags with the expected request indexes.</summary>
        private static object[] TapSoundCases => new object[]
        {
            new object[] { JudgeGrade.Perfect, false, false, false, new[] { GameplayRig.SfxTapPerfect } },
            new object[] { JudgeGrade.LatePerfect3rd, false, false, false, new[] { GameplayRig.SfxTapPerfect } },
            new object[] { JudgeGrade.FastGreat2nd, false, false, false, new[] { GameplayRig.SfxTapGreat } },
            new object[] { JudgeGrade.LateGood, false, false, false, new[] { GameplayRig.SfxTapGood } },
            new object[] { JudgeGrade.LateGood, false, true, false, new[] { GameplayRig.SfxTapEx } },
            new object[] { JudgeGrade.Perfect, true, true, false, new[] { GameplayRig.SfxBreakJudge, GameplayRig.SfxBreak } },
            new object[] { JudgeGrade.FastGood, true, false, false, new[] { GameplayRig.SfxBreakJudge } },
            new object[] { JudgeGrade.Miss, false, false, false, Array.Empty<int>() },
            new object[] { JudgeGrade.TooFast, true, false, false, Array.Empty<int>() },
            new object[] { JudgeGrade.Perfect, false, false, true, Array.Empty<int>() },
            new object[] { JudgeGrade.TooFast, false, false, true, new[] { GameplayRig.SfxMiss } },
            new object[] { JudgeGrade.Miss, true, false, true, new[] { GameplayRig.SfxMiss } }
        };

        /// <summary>Builds the gameplay objects.</summary>
        /// <returns>The setup coroutine.</returns>
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _rig = new GameplayRig(initializePools: false, initializeEffects: false);
            yield return null;
        }

        /// <summary>Destroys the gameplay objects and restores the global state.</summary>
        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
        }

        /// <summary>Checks the sounds requested for tap, hold and star judgments.</summary>
        /// <param name="grade">The grade.</param>
        /// <param name="isBreak">Whether the note is a break.</param>
        /// <param name="isEx">Whether the note is an EX note.</param>
        /// <param name="isMine">Whether the note is a mine.</param>
        /// <param name="expected">The indexes of the requested sounds.</param>
        [TestCaseSource(nameof(TapSoundCases))]
        public void PlayTapSound_RequestsMatchingSounds(JudgeGrade grade, bool isBreak, bool isEx, bool isMine, int[] expected)
        {
            _rig.NoteAudio.OnPreUpdate();

            _rig.NoteAudio.PlayTapSound(new NoteJudgeResult() { Grade = grade, IsBreak = isBreak, IsEX = isEx, IsMine = isMine });

            AssertRequests(expected);
        }

        /// <summary>Checks the touch, firework, slide and break slide sound requests.</summary>
        [Test]
        public void OtherSounds_RequestTheirSlots()
        {
            var audio = _rig.NoteAudio;
            audio.OnPreUpdate();
            audio.PlayTouchSound();
            audio.PlayHanabiSound();
            audio.PlaySlideSound(false);
            AssertRequests(GameplayRig.SfxTouch, GameplayRig.SfxFirework, GameplayRig.SfxSlide);

            audio.OnPreUpdate();
            audio.PlaySlideSound(true);
            audio.PlayBreakSlideEndSound();
            AssertRequests(GameplayRig.SfxBreakSlide, GameplayRig.SfxBreakSlideJudge, GameplayRig.SfxBreak);
        }

        /// <summary>Checks that the touch hold riser starts once, keeps playing while requested and stops when released.</summary>
        [Test]
        public void TouchHoldRiser_StartsAndStops()
        {
            var audio = _rig.NoteAudio;
            audio.PlayTouchHoldSound();
            audio.OnLateUpdate();
            Assert.That(PrivateMember.GetField<bool>(audio, "_isTouchHoldRiserPlaying"), Is.True);
            audio.OnLateUpdate();
            Assert.That(PrivateMember.GetField<bool>(audio, "_isTouchHoldRiserPlaying"), Is.True);

            audio.StopTouchHoldSound();
            audio.OnLateUpdate();
            Assert.That(PrivateMember.GetField<bool>(audio, "_isTouchHoldRiserPlaying"), Is.False);
            audio.OnLateUpdate();
            Assert.That(PrivateMember.GetField<bool>(audio, "_isTouchHoldRiserPlaying"), Is.False);
        }

        /// <summary>Checks that requested samples are played once per frame, muted samples are skipped and Clear resets.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnLateUpdate_PlaysRequestedSamples()
        {
            var samples = PrivateMember.GetField<List<AudioSampleWrap>>(_rig.AudioOutput, "SFXSamples");
            var touch = new FakeAudioSample("touch.wav", 1f);
            var riser = new FakeAudioSample("touch_Hold_riser.wav", 1f);
            var muted = new FakeAudioSample("tap_perfect.wav", 0f);
            samples.Add(touch);
            samples.Add(riser);
            samples.Add(muted);
            var audio = _rig.NoteAudio;
            UnityMessages.Send(audio, "Awake");
            yield return null;

            var requests = _rig.SoundRequests;
            for (var i = 0; i < requests.Length; i++)
            {
                requests[i] = true;
            }
            audio.OnLateUpdate();

            Assert.That(touch.PlayOneShotCount, Is.EqualTo(1));
            Assert.That(riser.PlayOneShotCount, Is.EqualTo(1));
            Assert.That(muted.PlayOneShotCount, Is.EqualTo(0));

            audio.Clear();
            Assert.That(Array.TrueForAll(requests, x => !x), Is.True);
            Assert.That(PrivateMember.GetField<bool>(audio, "_isTouchHoldRiserPlaying"), Is.False);
            Assert.That(riser.StopCount, Is.EqualTo(1));
        }

        /// <summary>Checks that the answer offset is read in seconds or in frames.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Awake_ReadsAnswerOffset()
        {
            _rig.Settings.Debug.OffsetUnit = OffsetUnitOption.Second;
            _rig.Settings.Judge.AnswerOffset = 0.1f;
            _rig.Settings.Debug.DisplayOffset = 0.05f;
            var audio = _rig.NoteAudio;
            UnityMessages.Send(audio, "Awake");
            var seconds = PrivateMember.GetField<float>(audio, "_answerOffsetSec");
            _rig.Settings.Debug.OffsetUnit = OffsetUnitOption.Frame;
            _rig.Settings.Judge.AnswerOffset = 6f;
            _rig.Settings.Debug.DisplayOffset = 0f;
            UnityMessages.Send(audio, "Awake");
            var frames = PrivateMember.GetField<float>(audio, "_answerOffsetSec");
            yield return null;

            Assert.That(seconds, Is.EqualTo(0.15f).Within(1e-5f));
            Assert.That(frames, Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(Majdata<NoteAudioManager>.Instance, Is.SameAs(audio));
        }

        /// <summary>Checks that practice charts get answer sounds for notes, hold ends and mines but none for headless slides.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GenerateAnswerSfx_SchedulesNoteAnswers() => UniTask.ToCoroutine(async () =>
        {
            var audio = _rig.NoteAudio;
            var chart = Chart(
                Point(1.0, Note(SimaiNoteType.Tap)),
                Point(2.0, Note(SimaiNoteType.Hold, holdTime: 0.5)),
                Point(3.0, Note(SimaiNoteType.Tap, isMine: true)),
                Point(4.0, Note(SimaiNoteType.Slide, isSlideNoHead: true)));

            await audio.GenerateAnswerSFX(chart, true);

            Assert.That(audio.FirstClockTiming, Is.EqualTo(1f).Within(1e-4f));
            AnswerAt(audio, 0.9f);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxAnswer], Is.False);
            AnswerAt(audio, 1.0f);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxAnswer], Is.True);
            AnswerAt(audio, 2.0f);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxAnswer], Is.True);
            AnswerAt(audio, 2.5f);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxAnswer], Is.True);
            AnswerAt(audio, 3.0f);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxMine], Is.True);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxAnswer], Is.False);
        });

        /// <summary>Checks that clock sounds precede the chart when it starts within the first measure.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GenerateAnswerSfx_AddsClocksBeforeEarlyChart() => UniTask.ToCoroutine(async () =>
        {
            var audio = _rig.NoteAudio;
            var xxlb = CreateXxlb();
            PrivateMember.SetField(audio, "_xxlbController", xxlb);

            await audio.GenerateAnswerSFX(Chart(Point(1.0, Note(SimaiNoteType.Tap))), false);

            Assert.That(audio.FirstClockTiming, Is.EqualTo(-2f).Within(1e-4f));
            AnswerAt(audio, -1.99f);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxAnswerClock], Is.True);
            Assert.That(xxlb.GetComponent<Image>().sprite, Is.SameAs(xxlb.sprites[1]));
        });

        /// <summary>Checks that clock sounds start with the music when the first measure is empty.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GenerateAnswerSfx_AddsClocksWithLateChart() => UniTask.ToCoroutine(async () =>
        {
            var audio = _rig.NoteAudio;

            await audio.GenerateAnswerSFX(Chart(Point(5.0, Note(SimaiNoteType.Tap))), false, 2);

            Assert.That(audio.FirstClockTiming, Is.EqualTo(0f).Within(1e-4f));
        });

        /// <summary>Checks that notes closer than one frame share a single answer sound.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GenerateAnswerSfx_MergesCloseAnswers() => UniTask.ToCoroutine(async () =>
        {
            var audio = _rig.NoteAudio;

            await audio.GenerateAnswerSFX(Chart(Point(1.0, Note(SimaiNoteType.Tap)), Point(1.005, Note(SimaiNoteType.Tap, isMine: true)), Point(2.0, Note(SimaiNoteType.Tap))), true);

            AnswerAt(audio, 1.01f);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxAnswer], Is.True);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxMine], Is.True);
            AnswerAt(audio, 1.5f);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxAnswer], Is.False);
            AnswerAt(audio, 2.0f);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxAnswer], Is.True);
        });

        /// <summary>Checks that the schedule only holds the generated answer points.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        [Ignore("Known issue: NoteAudioManager.GenerateAnswerSFX sizes the schedule with PooledList.Capacity instead of Count, appending default answer points.")]
        public IEnumerator GenerateAnswerSfx_ScheduleHasOnePointPerAnswer() => UniTask.ToCoroutine(async () =>
        {
            var audio = _rig.NoteAudio;

            await audio.GenerateAnswerSFX(Chart(Point(1.0, Note(SimaiNoteType.Tap))), true);

            var schedule = PrivateMember.GetField<object>(audio, "_answerTimingPoints");
            Assert.That(schedule.GetType().GetProperty("Length")!.GetValue(schedule), Is.EqualTo(1));
        });

        /// <summary>Checks that an empty chart produces no answer sounds.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GenerateAnswerSfx_EmptyChartSchedulesNothing() => UniTask.ToCoroutine(async () =>
        {
            var audio = _rig.NoteAudio;

            await audio.GenerateAnswerSFX(SimaiChart.Empty, false);

            AnswerAt(audio, 10f);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxAnswer], Is.False);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxAnswerClock], Is.False);
        });

        /// <summary>Checks that destroying the manager frees its singleton.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnDestroy_FreesSingleton()
        {
            UnityEngine.Object.Destroy(_rig.NoteAudio.gameObject);
            yield return null;

            Assert.That(Majdata<NoteAudioManager>.IsNull, Is.True);
        }

        /// <summary>Runs the answer schedule for one frame at a chart time.</summary>
        /// <param name="audio">The audio manager.</param>
        /// <param name="time">The chart time.</param>
        private void AnswerAt(NoteAudioManager audio, float time)
        {
            _rig.Controller.ThisFrameSec = time;
            audio.OnPreUpdate();
            audio.OnLateUpdate();
        }

        /// <summary>Asserts that exactly the given sound slots are requested.</summary>
        /// <param name="expected">The requested slot indexes.</param>
        private void AssertRequests(params int[] expected)
        {
            var requests = _rig.SoundRequests;
            for (var i = 0; i < requests.Length; i++)
            {
                Assert.That(requests[i], Is.EqualTo(Array.IndexOf(expected, i) >= 0), $"Sound slot {i}");
            }
        }

        /// <summary>Creates the dancing mascot controller that answer clocks step.</summary>
        /// <returns>The controller.</returns>
        private XxlbAnimationController CreateXxlb()
        {
            var gameObject = _rig.Objects.Create("Xxlb");
            gameObject.SetActive(false);
            gameObject.AddComponent<Image>();
            gameObject.AddComponent<Animator>();
            var controller = gameObject.AddComponent<XxlbAnimationController>();
            controller.sprites = new[]
            {
                TestSkinFactory.CreateSprite(_rig.Objects, "Left"),
                TestSkinFactory.CreateSprite(_rig.Objects, "Center"),
                TestSkinFactory.CreateSprite(_rig.Objects, "Right")
            };
            gameObject.SetActive(true);
            return controller;
        }

        /// <summary>Creates a chart from timing points.</summary>
        /// <param name="points">The timing points.</param>
        /// <returns>The chart.</returns>
        private static SimaiChart Chart(params SimaiTimingPoint[] points)
        {
            return new SimaiChart("1", "Tester", "1,", points);
        }

        /// <summary>Creates a timing point at 120 BPM.</summary>
        /// <param name="timing">The timing in seconds.</param>
        /// <param name="note">The note at that timing.</param>
        /// <returns>The timing point.</returns>
        private static SimaiTimingPoint Point(double timing, SimaiNote note)
        {
            return new SimaiTimingPoint(timing, new[] { note }, "1".AsSpan(), bpm: 120f);
        }

        /// <summary>Creates a note.</summary>
        /// <param name="type">The note type.</param>
        /// <param name="holdTime">The hold time in seconds.</param>
        /// <param name="isMine">Whether the note is a mine.</param>
        /// <param name="isSlideNoHead">Whether the note is a slide without a head.</param>
        /// <returns>The note.</returns>
        private static SimaiNote Note(SimaiNoteType type, double holdTime = 0, bool isMine = false, bool isSlideNoHead = false)
        {
            return new SimaiNote()
            {
                Type = type,
                HoldTime = holdTime,
                IsMine = isMine,
                IsSlideNoHead = isSlideNoHead
            };
        }
    }
}
