using System;
using System.Collections;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Scenes.Game.Notes.Slide;
using MajdataPlay.Settings;
using MajdataPlay.Tests.Game.Notes.PlayMode.Doubles;
using MajdataPlay.Tests.Game.Notes.PlayMode.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode
{
    /// <summary>
    /// Covers the shared slide logic of <see cref="SlideBase"/> through a probe slide: deluxe and classic judgment windows,
    /// grade correction, bar hiding and fading, sensor queue checks, mines, connection slides and the end report.
    /// </summary>
    public class SlideBaseTests
    {
        /// <summary>The answer timing used by the probe.</summary>
        private const float AnswerTiming = 2f;

        /// <summary>The gameplay objects of the current test.</summary>
        private GameplayRig _rig = null!;

        /// <summary>Gets the deluxe judgment cases as offset in milliseconds, wait time in seconds and expected grade.</summary>
        private static object[] DeluxeCases => new object[]
        {
            new object[] { 0f, 0f, JudgeGrade.Perfect },
            new object[] { 50f, 0f, JudgeGrade.Perfect },
            new object[] { -50f, 0f, JudgeGrade.Perfect },
            new object[] { 100f, 0f, JudgeGrade.LatePerfect2nd },
            new object[] { -100f, 0f, JudgeGrade.FastPerfect2nd },
            new object[] { 200f, 0f, JudgeGrade.LatePerfect3rd },
            new object[] { -200f, 0f, JudgeGrade.FastPerfect3rd },
            new object[] { 300f, 0f, JudgeGrade.LateGreat },
            new object[] { -300f, 0f, JudgeGrade.FastGreat },
            new object[] { 400f, 0f, JudgeGrade.LateGreat2nd },
            new object[] { -400f, 0f, JudgeGrade.FastGreat2nd },
            new object[] { 450f, 0f, JudgeGrade.LateGreat3rd },
            new object[] { -450f, 0f, JudgeGrade.FastGreat3rd },
            new object[] { 650f, 0f, JudgeGrade.LateGood },
            new object[] { -650f, 0f, JudgeGrade.FastGood },
            new object[] { 100f, 0.4f, JudgeGrade.Perfect },
            new object[] { 300f, 0.4f, JudgeGrade.LatePerfect3rd }
        };

        /// <summary>Gets the classic judgment cases as offset in milliseconds and expected grade.</summary>
        private static object[] ClassicCases => new object[]
        {
            new object[] { 0f, JudgeGrade.Perfect },
            new object[] { -60f, JudgeGrade.Perfect },
            new object[] { 60f, JudgeGrade.Perfect },
            new object[] { -100f, JudgeGrade.FastPerfect2nd },
            new object[] { 100f, JudgeGrade.LatePerfect2nd },
            new object[] { -180f, JudgeGrade.FastPerfect3rd },
            new object[] { 180f, JudgeGrade.LatePerfect3rd },
            new object[] { -250f, JudgeGrade.FastGreat },
            new object[] { 250f, JudgeGrade.LateGreat },
            new object[] { -300f, JudgeGrade.FastGreat2nd },
            new object[] { 300f, JudgeGrade.LateGreat2nd },
            new object[] { -380f, JudgeGrade.FastGreat3rd },
            new object[] { 380f, JudgeGrade.LateGreat3rd },
            new object[] { -450f, JudgeGrade.FastGood },
            new object[] { 450f, JudgeGrade.LateGood }
        };

        /// <summary>Builds the gameplay objects and lets Unity run their Start messages.</summary>
        /// <returns>The setup coroutine.</returns>
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _rig = new GameplayRig();
            yield return null;
        }

        /// <summary>Destroys the gameplay objects and restores the global state.</summary>
        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
        }

        /// <summary>Checks the deluxe slide windows, including the extension granted by the waiting time.</summary>
        /// <param name="offsetMs">The finish offset from the answer timing in milliseconds.</param>
        /// <param name="waitTime">The waiting time before the judgment in seconds.</param>
        /// <param name="expected">The expected grade.</param>
        [TestCaseSource(nameof(DeluxeCases))]
        public void Judge_MapsOffsetToSlideWindow(float offsetMs, float waitTime, JudgeGrade expected)
        {
            var slide = CreateSlide();
            slide.WaitTime = waitTime;
            _rig.Controller.ThisFrameSec = AnswerTiming;

            slide.RunJudge(AnswerTiming + (offsetMs / 1000f));

            Assert.That(slide.Judged, Is.True);
            Assert.That(slide.Result, Is.EqualTo(expected));
            Assert.That(slide.Diff, Is.EqualTo(offsetMs).Within(0.05f));
        }

        /// <summary>Checks that a slide finished before its group start waits half of the remaining time.</summary>
        [Test]
        public void Judge_EarlyFinishSetsWaitTimeFromGroupStart()
        {
            var slide = CreateSlide();
            slide.ConnectInfo = new ConnSlideInfo()
            {
                StartTiming = 3f
            };
            _rig.Controller.ThisFrameSec = 2f;

            slide.RunJudge(AnswerTiming);

            Assert.That(slide.WaitTime, Is.EqualTo(0.5f).Within(1e-5f));
        }

        /// <summary>Checks that a very late finish waits a fixed short time.</summary>
        [Test]
        public void Judge_LateGoodSetsShortWaitTime()
        {
            var slide = CreateSlide();
            slide.WaitTime = 1f;
            _rig.Controller.ThisFrameSec = 3f;

            slide.RunJudge(AnswerTiming + 0.9f);

            Assert.That(slide.Result, Is.EqualTo(JudgeGrade.LateGood));
            Assert.That(slide.WaitTime, Is.EqualTo(0.05f));
        }

        /// <summary>Checks that finishing a mine slide is too fast or a miss.</summary>
        [Test]
        public void Judge_MineFinishIsTooFastOrMiss()
        {
            var early = CreateSlide();
            early.IsMine = true;
            var late = CreateSlide();
            late.IsMine = true;
            _rig.Controller.ThisFrameSec = AnswerTiming;

            early.RunJudge(AnswerTiming);
            late.RunJudge(AnswerTiming + 0.2f);
            var classic = CreateSlide();
            classic.IsMine = true;
            classic.RunJudgeClassic(AnswerTiming + 0.2f);
            var classicEarly = CreateSlide();
            classicEarly.IsMine = true;
            classicEarly.RunJudgeClassic(AnswerTiming);

            Assert.That(early.Result, Is.EqualTo(JudgeGrade.TooFast));
            Assert.That(late.Result, Is.EqualTo(JudgeGrade.Miss));
            Assert.That(classic.Result, Is.EqualTo(JudgeGrade.Miss));
            Assert.That(classicEarly.Result, Is.EqualTo(JudgeGrade.TooFast));
        }

        /// <summary>Checks that only the last part of a connection slide and only unjudged slides are judged.</summary>
        [Test]
        public void Judge_SkipsConnectionPartsAndJudgedSlides()
        {
            var part = CreateSlide();
            part.ConnectInfo = new ConnSlideInfo()
            {
                IsGroupPart = true,
                IsGroupPartHead = true
            };
            var judged = CreateSlide();
            judged.Judged = true;
            judged.Result = JudgeGrade.LateGood;

            part.RunJudge(AnswerTiming);
            part.RunJudgeClassic(AnswerTiming);
            judged.RunJudge(AnswerTiming);
            judged.RunJudgeClassic(AnswerTiming);

            Assert.That(part.Judged, Is.False);
            Assert.That(judged.Result, Is.EqualTo(JudgeGrade.LateGood));
        }

        /// <summary>Checks the classic slide windows.</summary>
        /// <param name="offsetMs">The finish offset from the answer timing in milliseconds.</param>
        /// <param name="expected">The expected grade.</param>
        [TestCaseSource(nameof(ClassicCases))]
        public void JudgeClassic_MapsOffsetToClassicWindow(float offsetMs, JudgeGrade expected)
        {
            var slide = CreateSlide();
            _rig.Controller.ThisFrameSec = AnswerTiming;

            slide.RunJudgeClassic(AnswerTiming + (offsetMs / 1000f));

            Assert.That(slide.Result, Is.EqualTo(expected));
            Assert.That(slide.Diff, Is.EqualTo(offsetMs).Within(0.05f));
        }

        /// <summary>Checks that an early classic finish waits half of the time left before the group start.</summary>
        [Test]
        public void JudgeClassic_EarlyFinishSetsWaitTime()
        {
            var slide = CreateSlide();
            slide.ConnectInfo = new ConnSlideInfo()
            {
                StartTiming = 3f
            };
            _rig.Controller.ThisFrameSec = 2.5f;

            slide.RunJudgeClassic(AnswerTiming);

            Assert.That(slide.WaitTime, Is.EqualTo(0.25f).Within(1e-5f));
        }

        /// <summary>Checks that subdivided perfect grades are merged into a perfect and other grades are kept.</summary>
        [Test]
        public void JudgeGradeCorrection_MergesPerfects()
        {
            var slide = CreateSlide();

            Assert.That(slide.Correct(JudgeGrade.LatePerfect3rd), Is.EqualTo(JudgeGrade.Perfect));
            Assert.That(slide.Correct(JudgeGrade.LatePerfect2nd), Is.EqualTo(JudgeGrade.Perfect));
            Assert.That(slide.Correct(JudgeGrade.FastPerfect2nd), Is.EqualTo(JudgeGrade.Perfect));
            Assert.That(slide.Correct(JudgeGrade.FastPerfect3rd), Is.EqualTo(JudgeGrade.Perfect));
            Assert.That(slide.Correct(JudgeGrade.FastGreat), Is.EqualTo(JudgeGrade.FastGreat));
            Assert.That(slide.Correct(JudgeGrade.Miss), Is.EqualTo(JudgeGrade.Miss));
        }

        /// <summary>Checks that bars are hidden progressively and never shown again by a smaller progress.</summary>
        [Test]
        public void HideBar_HidesBarsUpToProgress()
        {
            var slide = CreateSlide();
            var bars = AddBars(slide, 5);

            slide.RunHideBar(3);
            Assert.That(Array.ConvertAll(bars, x => x.enabled), Is.EqualTo(new[] { false, false, false, true, true }));
            Assert.That(slide.HiddenBarCount, Is.EqualTo(3));

            slide.RunHideBar(1);
            Assert.That(slide.HiddenBarCount, Is.EqualTo(3));

            slide.RunHideAllBar();
            Assert.That(Array.TrueForAll(bars, x => !x.enabled), Is.True);
            Assert.That(slide.HiddenBarCount, Is.EqualTo(5));
        }

        /// <summary>Checks the alpha and material setters of the bars.</summary>
        [Test]
        public void SlideBarRender_SetsAlphaAndMaterial()
        {
            var slide = CreateSlide();
            var bars = AddBars(slide, 3);

            slide.RunSetSlideBarAlpha(0.5f);
            Assert.That(bars[1].enabled, Is.True);
            Assert.That(bars[1].color.a, Is.EqualTo(0.5f));

            slide.RunSetSlideBarAlpha(0f);
            Assert.That(bars[1].enabled, Is.False);

            slide.RunSetSlideBarMaterial(_rig.BreakMaterial);
            Assert.That(bars[2].sharedMaterial, Is.SameAs(_rig.BreakMaterial));

            slide.RunSetSlideBarAlphaAndMaterial(0.25f, _rig.HoldShineMaterial);
            Assert.That(bars[0].enabled, Is.True);
            Assert.That(bars[0].color.a, Is.EqualTo(0.25f));
            Assert.That(bars[0].sharedMaterial, Is.SameAs(_rig.HoldShineMaterial));

            slide.RunSetSlideBarAlphaAndMaterial(0f, _rig.DefaultMaterial);
            Assert.That(bars[0].enabled, Is.False);
            Assert.That(bars[0].sharedMaterial, Is.SameAs(_rig.DefaultMaterial));
        }

        /// <summary>Checks every phase of the bar fade-in, ending with the break material.</summary>
        [Test]
        public void SlideBarFadeIn_FollowsTimeline()
        {
            var slide = CreateSlide();
            slide.IsBreak = true;
            var bars = AddBars(slide, 2);
            slide.Init();
            slide.SetFadeIn(1f, 1.2f, 1.5f, 0.2f, 0.5f);
            slide.RunSetSlideBarAlpha(0f);

            _rig.Controller.ThisFrameSec = 0.9f;
            slide.RunSlideBarFadeIn();
            Assert.That(bars[0].enabled, Is.False);

            _rig.Controller.ThisFrameSec = 1.1f;
            slide.RunSlideBarFadeIn();
            Assert.That(bars[0].color.a, Is.EqualTo(0.25f).Within(1e-4f));

            _rig.Controller.ThisFrameSec = 1.3f;
            slide.RunSlideBarFadeIn();
            Assert.That(bars[0].color.a, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(slide.FadeInFlag, Is.EqualTo(0u));

            _rig.Controller.ThisFrameSec = 1.6f;
            slide.RunSlideBarFadeIn();
            Assert.That(bars[0].color.a, Is.EqualTo(1f));
            Assert.That(bars[0].sharedMaterial, Is.SameAs(_rig.BreakMaterial));
            Assert.That(slide.FadeInFlag, Is.EqualTo(1u));

            bars[0].color = Color.clear;
            slide.RunSlideBarFadeIn();
            Assert.That(bars[0].color.a, Is.EqualTo(0f));
        }

        /// <summary>Checks that fading is skipped for hidden tracks, uninitialized slides and ends with a plain alpha.</summary>
        [Test]
        public void SlideBarFadeIn_SkipsHiddenTracksAndUninitializedSlides()
        {
            var uninitialized = CreateSlide();
            var uninitializedBars = AddBars(uninitialized, 1);
            uninitialized.SetFadeIn(0f, 0f, 0f, 0f, 0.5f);
            var noTrack = CreateSlide();
            var noTrackBars = AddBars(noTrack, 1);
            noTrack.Init();
            noTrack.IsSlideNoTrack = true;
            noTrack.SetFadeIn(0f, 0f, 0f, 0f, 0.5f);
            var plain = CreateSlide();
            var plainBars = AddBars(plain, 1);
            plain.Init();
            plain.SetFadeIn(0f, 0f, 0f, 0f, 0.5f);
            _rig.Controller.ThisFrameSec = 1f;

            uninitialized.RunSlideBarFadeIn();
            noTrack.RunSlideBarFadeIn();
            plain.RunSlideBarFadeIn();

            Assert.That(uninitializedBars[0].color.a, Is.EqualTo(1f));
            Assert.That(uninitialized.FadeInFlag, Is.EqualTo(0u));
            Assert.That(noTrack.FadeInFlag, Is.EqualTo(0u));
            Assert.That(plain.FadeInFlag, Is.EqualTo(1u));
            Assert.That(plainBars[0].sharedMaterial, Is.Not.SameAs(_rig.BreakMaterial));
            Assert.That(noTrackBars[0].enabled, Is.True);
        }

        /// <summary>Checks that the sensor check walks the judge queue as areas are pressed and released.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SensorCheck_AdvancesQueue()
        {
            var slide = CreateSlide();
            slide.Init();
            slide.SetQueue(0, new[] { Area(SensorArea.A1, false, 1, 2), Area(SensorArea.B1, true, 3, 4) });
            yield return null;

            Assert.That(slide.RunSensorCheck(), Is.False);
            slide.Checkable = true;
            _rig.Step(1f);
            Assert.That(slide.RunSensorCheck(), Is.False);

            _rig.SetSensor(SensorArea.A1, SwitchStatus.Off, SwitchStatus.On);
            _rig.Step(1.1f);
            Assert.That(slide.RunSensorCheck(), Is.True);
            Assert.That(slide.QueueRemaining, Is.EqualTo(2));
            Assert.That(slide.IsFinished, Is.False);

            _rig.SetSensor(SensorArea.A1, SwitchStatus.On, SwitchStatus.Off);
            _rig.SetSensor(SensorArea.B1, SwitchStatus.Off, SwitchStatus.On);
            _rig.Step(1.2f);
            Assert.That(slide.RunSensorCheck(), Is.True);

            Assert.That(slide.IsFinished, Is.True);
            Assert.That(slide.RunSensorCheck(), Is.False);
        }

        /// <summary>Checks that a pressed skippable area lets the second area advance the queue by one.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SensorCheck_SecondAreaOnSkipsFirst()
        {
            var slide = CreateSlide();
            slide.Init();
            slide.Checkable = true;
            slide.SetQueue(1, new[] { Area(SensorArea.A1, false, 1, 2), Area(SensorArea.B1, false, 3, 4), Area(SensorArea.C, true, 5, 6) });
            yield return null;

            _rig.SetSensor(SensorArea.B1, SwitchStatus.Off, SwitchStatus.On);
            _rig.Step(1f);

            Assert.That(slide.RunSensorCheck(), Is.True);
            Assert.That(slide.QueueRemaining, Is.EqualTo(2));
            Assert.That(slide.IsPendingFinish, Is.False);

            _rig.SetSensor(SensorArea.B1, SwitchStatus.On, SwitchStatus.Off);
            _rig.Step(1.1f);
            Assert.That(slide.RunSensorCheck(), Is.True);
            Assert.That(slide.QueueRemaining, Is.EqualTo(1));
            Assert.That(slide.IsPendingFinish, Is.True);
        }

        /// <summary>Checks that sensor checks are ignored under autoplay and for ended slides.</summary>
        [Test]
        public void SensorCheck_IgnoredForAutoplayAndEndedSlides()
        {
            _rig.Controller.SetMods(AutoplayModeOption.Enable);
            var autoplay = CreateSlide();
            autoplay.Init();
            autoplay.Checkable = true;
            _rig.Controller.SetMods(AutoplayModeOption.Disable);
            var ended = CreateSlide();
            ended.SetState(NoteStatus.End);
            ended.Checkable = true;

            Assert.That(autoplay.RunSensorCheck(), Is.False);
            Assert.That(ended.RunSensorCheck(), Is.False);
        }

        /// <summary>Checks that the too-late judgment gives a late good with one area left and a miss otherwise.</summary>
        [Test]
        public void TooLateJudge_DependsOnRemainingAreas()
        {
            var nearlyDone = CreateSlide();
            nearlyDone.Init();
            nearlyDone.SetQueue(0, new[] { Area(SensorArea.A3, true, 1, 1) });
            var untouched = CreateSlide();
            untouched.Init();
            untouched.SetQueue(0, new[] { Area(SensorArea.A1, false, 1, 1), Area(SensorArea.A2, true, 2, 2) });

            nearlyDone.RunTooLateJudge();
            untouched.RunTooLateJudge();

            Assert.That(nearlyDone.State, Is.EqualTo(NoteStatus.End));
            Assert.That(nearlyDone.Result, Is.EqualTo(JudgeGrade.LateGood));
            Assert.That(untouched.Result, Is.EqualTo(JudgeGrade.Miss));
            Assert.That(_rig.Counter.JudgeStats.SlideGoodCount, Is.EqualTo(1));
            Assert.That(_rig.Counter.JudgeStats.SlideMissCount, Is.EqualTo(1));
        }

        /// <summary>Checks that the too-late judgment simply ends an already judged slide.</summary>
        [Test]
        public void TooLateJudge_EndsJudgedSlide()
        {
            var slide = CreateSlide();
            slide.Init();
            slide.Judged = true;
            slide.Result = JudgeGrade.FastGreat;

            slide.RunTooLateJudge();

            Assert.That(slide.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.SlideGreatCount, Is.EqualTo(1));
        }

        /// <summary>Checks the end report: grade correction, multiple counting, bar hiding and break sounds.</summary>
        [Test]
        public void End_ReportsCorrectedGradeWithMultiple()
        {
            var slide = CreateSlide();
            var bars = AddBars(slide, 2);
            slide.Init();
            slide.IsBreak = true;
            slide.Multiple = 2;
            slide.Result = JudgeGrade.LatePerfect2nd;
            slide.RunSetSlideBarAlpha(1f);

            slide.RunEnd();
            slide.RunEnd();

            Assert.That(slide.State, Is.EqualTo(NoteStatus.End));
            Assert.That(bars[0].enabled, Is.False);
            Assert.That(_rig.Counter.JudgeStats.BreakCriticalCount, Is.EqualTo(2));
            Assert.That(_rig.SoundRequests[GameplayRig.SfxBreakSlideJudge], Is.True);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxBreak], Is.True);
        }

        /// <summary>Checks that subdivided slide grades are kept when the modifier asks for it.</summary>
        [Test]
        public void End_KeepsSubdividedGradeWhenRequested()
        {
            _rig.Controller.SetMods(AutoplayModeOption.Disable, subdivideSlideJudgeGrade: true);
            var slide = CreateSlide();
            slide.Init();
            slide.Result = JudgeGrade.LatePerfect2nd;

            slide.RunEnd();

            Assert.That(_rig.Counter.JudgeStats.SlidePerfectCount, Is.EqualTo(1));
            Assert.That(_rig.SoundRequests[GameplayRig.SfxBreakSlideJudge], Is.False);
        }

        /// <summary>Checks that ending a connection part ends its parent without reporting a result.</summary>
        [Test]
        public void End_EndsParentWithoutReportingPart()
        {
            var parent = CreateSlide<ProbeConnectableSlide>();
            parent.Init();
            var part = CreateSlide();
            part.Init();
            part.ConnectInfo = new ConnSlideInfo()
            {
                IsGroupPart = true,
                IsGroupPartHead = true,
                Parent = parent
            };

            part.RunEnd();

            Assert.That(part.Parent, Is.SameAs(parent));
            Assert.That(parent.IsEnded, Is.True);
            Assert.That(_rig.Counter.JudgeStats.SlideJudgedCount, Is.EqualTo(1));
        }

        /// <summary>Checks that only unfinished connection parts can be force finished.</summary>
        [Test]
        public void ForceFinish_ClearsQueueOfConnectionPartsOnly()
        {
            var single = CreateSlide();
            single.SetQueue(0, new[] { Area(SensorArea.A1, true, 1, 1) });
            var part = CreateSlide();
            var bars = AddBars(part, 2);
            part.ConnectInfo = new ConnSlideInfo()
            {
                IsGroupPart = true,
                IsGroupPartHead = true
            };
            part.SetQueue(0, new[] { Area(SensorArea.A1, true, 1, 1) });
            part.RunSetSlideBarAlpha(1f);

            single.ForceFinish();
            part.ForceFinish();

            Assert.That(single.QueueRemaining, Is.EqualTo(1));
            Assert.That(part.QueueRemaining, Is.EqualTo(0));
            Assert.That(part.IsFinished, Is.True);
            Assert.That(bars[1].enabled, Is.False);
        }

        /// <summary>Checks the mine check of single slides and connection parts.</summary>
        [Test]
        public void MineCheck_JudgesMinesAtTheirTiming()
        {
            var single = CreateSlide();
            single.Init();
            single.IsMine = true;
            var part = CreateSlide();
            part.Init();
            part.IsMine = true;
            part.StartTiming = 1f;
            part.Length = 1f;
            part.SetQueue(0, new[] { Area(SensorArea.A1, true, 1, 1) });
            part.ConnectInfo = new ConnSlideInfo()
            {
                IsGroupPart = true,
                IsGroupPartHead = true
            };
            var normal = CreateSlide();
            normal.Init();

            _rig.Controller.ThisFrameSec = 1.5f;
            single.RunMineCheck();
            part.RunMineCheck();
            normal.RunMineCheck();
            Assert.That(single.Judged, Is.False);
            Assert.That(part.Judged, Is.False);

            _rig.Controller.ThisFrameSec = 2.1f;
            single.RunMineCheck();
            part.RunMineCheck();
            normal.RunMineCheck();

            Assert.That(single.Judged, Is.True);
            Assert.That(single.Result, Is.EqualTo(JudgeGrade.Perfect));
            Assert.That(part.Judged, Is.True);
            Assert.That(part.QueueRemaining, Is.EqualTo(0));
            Assert.That(normal.Judged, Is.False);
        }

        /// <summary>Checks that the slide sound is requested once and break slides use the break start sound.</summary>
        [Test]
        public void PlaySfx_RequestsSoundOnce()
        {
            var slide = CreateSlide();
            var breakSlide = CreateSlide();
            breakSlide.IsBreak = true;

            slide.RunPlaySfx();
            Assert.That(_rig.SoundRequests[GameplayRig.SfxSlide], Is.True);
            _rig.NoteAudio.OnPreUpdate();
            slide.RunPlaySfx();
            breakSlide.RunPlaySfx();

            Assert.That(slide.SoundPlayed, Is.True);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxSlide], Is.False);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxBreakSlide], Is.True);
        }

        /// <summary>Checks that only perfect break slides request the break end sounds.</summary>
        [Test]
        public void PlayJudgeSfx_OnlyForPerfectBreaks()
        {
            var slide = CreateSlide();

            slide.RunPlayJudgeSfx(JudgeGrade.LateGreat, true);
            slide.RunPlayJudgeSfx(JudgeGrade.Perfect, false);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxBreakSlideJudge], Is.False);

            slide.RunPlayJudgeSfx(JudgeGrade.Perfect, true);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxBreakSlideJudge], Is.True);
        }

        /// <summary>Checks that the slide result is only shown when it exists and the display setting allows the grade.</summary>
        [Test]
        public void PlaySlideOK_RespectsResultObjectAndSetting()
        {
            var slide = CreateSlide();
            Assert.That(slide.RunPlaySlideOK(JudgeGrade.Perfect), Is.False);

            slide.SetSlideOK(NoteTemplates.AddSlideOK(slide.transform));
            Assert.That(slide.RunPlaySlideOK(JudgeGrade.Perfect), Is.True);

            _rig.Settings.Display.SlideJudgeType = JudgeDisplayOption.MissOnly;
            Assert.That(slide.RunPlaySlideOK(JudgeGrade.Perfect), Is.False);
            Assert.That(slide.RunPlaySlideOK(JudgeGrade.Miss), Is.True);
        }

        /// <summary>Checks that stars follow the active state and can be forgotten.</summary>
        [Test]
        public void Stars_FollowActiveStateAndCanBeDestroyed()
        {
            var slide = CreateSlide();
            var bars = AddBars(slide, 1);
            var star = NoteTemplates.RendererChild("Star", slide.transform);
            slide.SetStar(1, star);

            slide.SetActive(true);
            Assert.That(star.enabled, Is.True);
            Assert.That(bars[0].enabled, Is.True);

            slide.SetActive(false);
            Assert.That(star.enabled, Is.False);
            Assert.That(bars[0].enabled, Is.False);

            slide.RunSetStarActive(true);
            slide.RunDestroyStars();
            Assert.That(star.enabled, Is.False);
            Assert.That(slide.GetStar(1), Is.Null);
        }

        /// <summary>Checks the timing, position and flag properties of slides.</summary>
        [Test]
        public void Properties_RoundTripAndValidate()
        {
            var slide = CreateSlide();
            slide.StartTiming = 1.5f;
            slide.Length = 2f;
            slide.IsJustR = true;
            slide.EndPos = 7;
            slide.Multiple = 3;
            slide.IsSlideNoHead = true;
            slide.IsSlideNoTrack = true;
            _rig.Controller.ThisFrameSec = 3f;

            Assert.That(slide.ArriveTiming, Is.EqualTo(3.5f));
            Assert.That(slide.IsJustR, Is.True);
            Assert.That(slide.EndPos, Is.EqualTo(7));
            Assert.That(slide.Multiple, Is.EqualTo(3));
            Assert.That(slide.IsSlideNoHead, Is.True);
            Assert.That(slide.IsSlideNoTrack, Is.True);
            Assert.That(slide.SlideType, Is.Empty);
            Assert.That(slide.SlideLength, Is.EqualTo(0f));
            Assert.That(slide.RemainingTime, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(slide.TimeSpanToArrive, Is.EqualTo(-0.5f).Within(1e-5f));
            Assert.That(slide.Parent, Is.Null);
            Assert.That(slide.IsFinished, Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => slide.EndPos = 9);
        }

        /// <summary>Creates a probe slide whose answer timing is two seconds.</summary>
        /// <returns>The probe.</returns>
        private ProbeSlide CreateSlide()
        {
            return CreateSlide<ProbeSlide>();
        }

        /// <summary>Creates a probe slide of the given type whose answer timing is two seconds.</summary>
        /// <typeparam name="T">The probe type.</typeparam>
        /// <returns>The probe.</returns>
        private T CreateSlide<T>() where T : SlideBase
        {
            var slide = _rig.Objects.Add<T>();
            PrivateMember.SetField(slide, "JudgeTiming", AnswerTiming);
            slide.Timing = AnswerTiming;
            return slide;
        }

        /// <summary>Adds sprite bars to a probe slide.</summary>
        /// <param name="slide">The slide.</param>
        /// <param name="count">The number of bars.</param>
        /// <returns>The bar renderers.</returns>
        private static SpriteRenderer[] AddBars(ProbeSlide slide, int count)
        {
            var bars = new SpriteRenderer[count];
            for (var i = 0; i < count; i++)
            {
                bars[i] = NoteTemplates.RendererChild($"Bar{i}", slide.transform);
                slide.AddBar(bars[i]);
            }
            return bars;
        }

        /// <summary>Creates a single-sensor judge area.</summary>
        /// <param name="area">The sensor.</param>
        /// <param name="isLast">Whether it is the last area of the slide.</param>
        /// <param name="progressWhenOn">The arrow progress when pressed.</param>
        /// <param name="progressWhenFinished">The arrow progress when finished.</param>
        /// <returns>The judge area.</returns>
        private static SlideArea Area(SensorArea area, bool isLast, int progressWhenOn, int progressWhenFinished)
        {
            return new SlideArea(new[] { (area, isLast) }, progressWhenOn, progressWhenFinished);
        }
    }
}
