using System.Collections;
using System.Text.RegularExpressions;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Scenes.Game.Notes.Slide;
using MajdataPlay.Settings;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Notes.PlayMode.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode
{
    /// <summary>
    /// Drives <see cref="SlideDrop"/> through the slide updater: table lookup and end positions, initialization, skins,
    /// fade-in, the star animation, sensor play, too-late misses, autoplay, mines and connection slides.
    /// </summary>
    public class SlideDropTests
    {
        /// <summary>The line3 slide constant.</summary>
        private const float Line3Const = 0.182f;

        /// <summary>The gameplay objects of the current test.</summary>
        private GameplayRig _rig = null!;

        /// <summary>Gets the slide types with their expected end key and slide result shape.</summary>
        private static object[] TypeCases => new object[]
        {
            new object[] { "line3", 3, SlideOKShape.Str },
            new object[] { "line4", 4, SlideOKShape.Str },
            new object[] { "line5", 5, SlideOKShape.Str },
            new object[] { "line6", 6, SlideOKShape.Str },
            new object[] { "line7", 7, SlideOKShape.Str },
            new object[] { "circle1", 2, SlideOKShape.Curv },
            new object[] { "circle2", 3, SlideOKShape.Curv },
            new object[] { "circle3", 4, SlideOKShape.Curv },
            new object[] { "circle4", 5, SlideOKShape.Curv },
            new object[] { "circle5", 6, SlideOKShape.Curv },
            new object[] { "circle6", 7, SlideOKShape.Curv },
            new object[] { "circle7", 8, SlideOKShape.Curv },
            new object[] { "circle8", 1, SlideOKShape.Curv },
            new object[] { "v1", 1, SlideOKShape.Str },
            new object[] { "v2", 2, SlideOKShape.Str },
            new object[] { "v3", 3, SlideOKShape.Str },
            new object[] { "v4", 4, SlideOKShape.Str },
            new object[] { "v6", 6, SlideOKShape.Str },
            new object[] { "v7", 7, SlideOKShape.Str },
            new object[] { "v8", 8, SlideOKShape.Str },
            new object[] { "ppqq1", 1, SlideOKShape.Str },
            new object[] { "ppqq2", 2, SlideOKShape.Str },
            new object[] { "ppqq3", 3, SlideOKShape.Str },
            new object[] { "ppqq4", 4, SlideOKShape.Str },
            new object[] { "ppqq5", 5, SlideOKShape.Str },
            new object[] { "ppqq6", 6, SlideOKShape.Str },
            new object[] { "ppqq7", 7, SlideOKShape.Str },
            new object[] { "ppqq8", 8, SlideOKShape.Str },
            new object[] { "pq1", 1, SlideOKShape.Str },
            new object[] { "pq2", 2, SlideOKShape.Str },
            new object[] { "pq3", 3, SlideOKShape.Str },
            new object[] { "pq4", 4, SlideOKShape.Str },
            new object[] { "pq5", 5, SlideOKShape.Str },
            new object[] { "pq6", 6, SlideOKShape.Str },
            new object[] { "pq7", 7, SlideOKShape.Str },
            new object[] { "pq8", 8, SlideOKShape.Str },
            new object[] { "s", 5, SlideOKShape.Str },
            new object[] { "L2", 2, SlideOKShape.Str },
            new object[] { "L3", 3, SlideOKShape.Str },
            new object[] { "L4", 4, SlideOKShape.Str },
            new object[] { "L5", 5, SlideOKShape.Str }
        };

        /// <summary>Gets the skin cases as break, each and mine flags with the expected bar and star sprite names.</summary>
        private static object[] SkinCases => new object[]
        {
            new object[] { false, false, false, "Slide", "Star", false },
            new object[] { false, true, false, "Slide_Each", "Star_Each", false },
            new object[] { true, false, false, "Slide_Break", "Star_Break", true },
            new object[] { true, true, false, "Slide_Break", "Star_Break", true },
            new object[] { false, false, true, "Slide_Mine", "Star_Mine", false },
            new object[] { true, false, true, "Slide_Break_Mine", "Star_Break_Mine", true }
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

        /// <summary>Checks that Awake loads the slide table, the end key and the slide result shape of every slide type.</summary>
        /// <param name="testCase">The slide type, the expected end key and the expected result shape.</param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Awake_ResolvesTypeInformation([ValueSource(nameof(TypeCases))] object[] testCase)
        {
            var slide = _rig.SpawnSlide<SlideDrop>(_rig.CreateSlideTemplate((string)testCase[0]));
            yield return null;

            Assert.That(slide.EndPos, Is.EqualTo((int)testCase[1]));
            Assert.That(slide.transform.GetComponentInChildren<SlideOK>(true).Shape, Is.EqualTo((SlideOKShape)testCase[2]));
            Assert.That(slide.SlideType, Is.EqualTo((string)testCase[0]));
        }

        /// <summary>Checks that an unknown slide type fails during Awake.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Awake_RejectsUnknownSlideType()
        {
            LogAssert.Expect(LogType.Exception, new Regex("Slide table of \"unknown\" is not found"));

            _rig.SpawnSlide<SlideDrop>(_rig.CreateSlideTemplate("unknown"));
            yield return null;
        }

        /// <summary>Checks that Awake creates a hidden star, hides the bars and finds the slide result.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Awake_PreparesStarBarsAndResult()
        {
            var slide = _rig.SpawnSlide<SlideDrop>(_rig.CreateSlideTemplate("line3"));
            yield return null;

            Assert.That(_rig.SlideStarCount, Is.EqualTo(1));
            Assert.That(_rig.SlideStar(0).localScale, Is.EqualTo(new Vector3(0f, 0f, 1f)));
            Assert.That(_rig.SlideStar(0).GetComponent<SpriteRenderer>().enabled, Is.False);
            Assert.That(slide.SlideLength, Is.EqualTo(13f));
            Assert.That(slide.transform.GetChild(0).GetComponent<SpriteRenderer>().enabled, Is.False);
            Assert.That(slide.Active, Is.False);
            Assert.That(slide.State, Is.EqualTo(NoteStatus.Start));
        }

        /// <summary>Checks the timings, rotation and slide result placement set up by Init.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_ComputesTimingsAndRotation()
        {
            var slide = CreateSlide(startPos: 3);
            yield return null;

            Assert.That(slide.State, Is.EqualTo(NoteStatus.Inited));
            Assert.That(slide.transform.rotation.eulerAngles.z, Is.EqualTo(270f).Within(0.01f));
            Assert.That(slide.JudgeTimingWithOffset, Is.EqualTo(1f + (1f - Line3Const)).Within(1e-4f));
            Assert.That(PrivateMember.GetField<float>(slide, "LastWaitTimeSec"), Is.EqualTo(Line3Const).Within(1e-4f));
            Assert.That(PrivateMember.GetField<float>(slide, "FadeInTiming"), Is.EqualTo(0.5f - (3.926913f / 7.5f)).Within(1e-4f));
            Assert.That(PrivateMember.GetField<float>(slide, "FadeInCompletedTiming"), Is.EqualTo(0.45f).Within(1e-4f));
            Assert.That(slide.ArriveTiming, Is.EqualTo(2f));
            Assert.That(slide.transform.GetComponentInChildren<SlideOK>(true), Is.Null);
            Assert.That(slide.QueueRemaining, Is.EqualTo(3));
            var resultSprite = _rig.SlideParent.GetComponentInChildren<SlideOK>(true).GetComponent<SpriteRenderer>().sprite;
            Assert.That(resultSprite.name, Is.EqualTo("Just[4]"));

            slide.Init();
            Assert.That(slide.State, Is.EqualTo(NoteStatus.Inited));
        }

        /// <summary>Checks the mirrored layout, the right-hand result sprite and the classic constant.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_MirroredClassicSlide()
        {
            _rig.Settings.Judge.Mode = JudgeModeOption.Classic;
            _rig.Settings.Debug.OffsetUnit = OffsetUnitOption.Second;
            _rig.Settings.Game.SlideFadeInOffset = 0.1f;
            var slide = CreateSlide(startPos: 2, isMirror: true, isJustR: true);
            yield return null;

            Assert.That(slide.transform.localScale, Is.EqualTo(new Vector3(-1f, 1f, 1f)));
            Assert.That(slide.transform.rotation.eulerAngles.z, Is.EqualTo(270f).Within(0.01f));
            Assert.That(slide.JudgeTimingWithOffset, Is.EqualTo(1f + (1f - 0.2106f)).Within(1e-4f));
            Assert.That(PrivateMember.GetField<float>(slide, "FadeInTiming"), Is.EqualTo(0.6f - (3.926913f / 7.5f)).Within(1e-4f));
            var result = _rig.SlideParent.GetComponentInChildren<SlideOK>(true);
            Assert.That(result.IsClassic, Is.True);
            Assert.That(result.GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo("Just[1]"));
        }

        /// <summary>Checks the frame based fade-in offset.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_ConvertsFrameFadeInOffset()
        {
            _rig.Settings.Game.SlideFadeInOffset = 6f;
            var slide = CreateSlide();
            yield return null;

            Assert.That(PrivateMember.GetField<float>(slide, "FadeInTiming"), Is.EqualTo(0.6f - (3.926913f / 7.5f)).Within(1e-4f));
        }

        /// <summary>Checks the bar, star and material choices for every slide variant.</summary>
        /// <param name="testCase">
        /// The break, each and mine flags followed by the expected bar and star sprite names and whether the star uses the
        /// break material.
        /// </param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LoadSkin_PicksSpritesForVariant([ValueSource(nameof(SkinCases))] object[] testCase)
        {
            var slide = CreateSlide(isBreak: (bool)testCase[0], isEach: (bool)testCase[1], isMine: (bool)testCase[2]);
            yield return null;

            var bar = slide.transform.GetChild(0).GetComponent<SpriteRenderer>();
            var star = _rig.SlideStar(0).GetComponent<SpriteRenderer>();
            Assert.That(bar.sprite.name, Is.EqualTo((string)testCase[3]));
            Assert.That(bar.color.a, Is.EqualTo(0f));
            Assert.That(star.sprite.name, Is.EqualTo((string)testCase[4]));
            Assert.That(star.sharedMaterial == _rig.BreakMaterial, Is.EqualTo((bool)testCase[5]));
        }

        /// <summary>Checks that disabling slide skipping makes every judge area mandatory.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_DisablesSkippingWhenRequested()
        {
            _rig.Settings.Game.SlideSkipping = false;
            var slide = CreateSlide();
            yield return null;

            var queue = PrivateMember.GetField<System.Memory<SlideArea>[]>(slide, "JudgeQueues")[0];
            foreach (var area in queue.ToArray())
            {
                Assert.That(area.IsSkippable, Is.False);
            }
        }

        /// <summary>Checks the star fade-in, scaling, travel and arrival.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnUpdate_AnimatesStar()
        {
            var slide = CreateSlide();
            var star = _rig.SlideStar(0);
            var starRenderer = star.GetComponent<SpriteRenderer>();
            yield return null;

            UpdateAt(slide, 0.4f);
            Assert.That(slide.State, Is.EqualTo(NoteStatus.Inited));
            Assert.That(starRenderer.enabled, Is.False);

            UpdateAt(slide, 0.75f);
            Assert.That(slide.State, Is.EqualTo(NoteStatus.Scaling));
            Assert.That(starRenderer.enabled, Is.True);
            Assert.That(starRenderer.color.a, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(star.localScale.x, Is.EqualTo(1f).Within(1e-4f));

            UpdateAt(slide, 1.5f);
            Assert.That(slide.State, Is.EqualTo(NoteStatus.Running));
            Assert.That(star.localScale.x, Is.EqualTo(1.5f));
            Assert.That(starRenderer.color.a, Is.EqualTo(1f));

            UpdateAt(slide, 2.1f);
            Assert.That(slide.State, Is.EqualTo(NoteStatus.Arrived));
            var end = MajdataPlay.Scenes.Game.Utils.NoteHelper.GetTapPosition(3, 4.8f);
            Assert.That(star.position.x, Is.EqualTo(end.x).Within(1e-3f));
            Assert.That(star.position.y, Is.EqualTo(end.y).Within(1e-3f));
        }

        /// <summary>Checks that classic slides grow the star from its normal size while fading in.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnUpdate_ClassicStarScaling()
        {
            _rig.Settings.Judge.Mode = JudgeModeOption.Classic;
            var slide = CreateSlide();
            var star = _rig.SlideStar(0);
            yield return null;

            UpdateAt(slide, 0.75f);

            Assert.That(star.localScale.x, Is.EqualTo(1.25f).Within(1e-4f));
        }

        /// <summary>Plays a whole slide by sliding over its sensors and checks the bars, sounds, judgment and result.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_SlidingOverSensorsFinishesSlide()
        {
            var slide = CreateSlide();
            var bars = slide.GetComponentsInChildren<SpriteRenderer>(true);
            _rig.RegisterSlides((slide, 0.3f));
            yield return null;

            _rig.Step(0.1f);
            Assert.That(slide.Active, Is.False);
            _rig.Step(0.2f);
            Assert.That(slide.Active, Is.True);
            Assert.That(bars[0].color.a, Is.EqualTo(0.5f).Within(1e-4f));
            _rig.Step(0.6f);
            Assert.That(bars[0].color.a, Is.EqualTo(1f));

            _rig.SetSensor(SensorArea.A1, SwitchStatus.Off, SwitchStatus.On);
            _rig.Step(1f);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxSlide], Is.True);
            Assert.That(bars[0].color.a, Is.EqualTo(1f));

            _rig.SetSensor(SensorArea.A1, SwitchStatus.On, SwitchStatus.Off);
            _rig.SetSensor(SensorArea.A2, SwitchStatus.Off, SwitchStatus.On);
            _rig.Step(1.3f);
            Assert.That(bars[5].enabled, Is.False);
            Assert.That(bars[6].enabled, Is.True);

            _rig.SetSensor(SensorArea.A2, SwitchStatus.On, SwitchStatus.Off);
            _rig.SetSensor(SensorArea.A3, SwitchStatus.Off, SwitchStatus.On);
            _rig.Step(1.6f);
            Assert.That(slide.IsFinished, Is.True);

            _rig.Step(1.62f);
            Assert.That(PrivateMember.GetField<bool>(slide, "IsJudged"), Is.True);
            for (var frame = 1; frame <= 15; frame++)
            {
                _rig.Step(1.62f + (frame / 60f));
            }

            Assert.That(slide.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.SlideCriticalCount, Is.EqualTo(1));
            var result = _rig.SlideParent.GetComponentInChildren<SlideOK>(true);
            Assert.That(result.State, Is.EqualTo(NoteStatus.Running));
            Assert.That(result.GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo("Just[10]"));

            for (var frame = 0; frame < 40; frame++)
            {
                _rig.Step(2f + (frame / 60f));
            }
            Assert.That(result.State, Is.EqualTo(NoteStatus.End));
        }

        /// <summary>Checks that classic mode judges a finished slide with the classic windows.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_ClassicSlideUsesClassicJudgment()
        {
            _rig.Settings.Judge.Mode = JudgeModeOption.Classic;
            var slide = CreateSlide();
            _rig.RegisterSlides((slide, 0.3f));
            yield return null;

            PlaySensors(1f, SensorArea.A1, SensorArea.A2, SensorArea.A3);
            _rig.Step(1.32f);
            Assert.That(PrivateMember.GetField<float>(slide, "JudgeDiff"), Is.LessThan(0f));
            for (var frame = 1; frame <= 20; frame++)
            {
                _rig.Step(1.32f + (frame / 60f));
            }

            Assert.That(slide.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.SlideJudgedCount, Is.EqualTo(1));
        }

        /// <summary>Checks that a mirrored slide turns its star half a revolution.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnUpdate_MirroredStarIsFlipped()
        {
            var slide = CreateSlide(isMirror: true);
            yield return null;

            UpdateAt(slide, 0.75f);

            var rotations = PrivateMember.GetField<MajdataPlay.Buffers.PooledList<Quaternion>>(slide, "StarRotations");
            var expected = rotations[0] * Quaternion.Euler(0f, 0f, 180f);
            Assert.That(Quaternion.Angle(_rig.SlideStar(0).rotation, expected), Is.LessThan(0.01f));
        }

        /// <summary>Checks that autoplay picks a valid random grade when the configured grade is invalid.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_AutoplayRandomGrade()
        {
            _rig.Controller.SetMods(AutoplayModeOption.Enable);
            _rig.Controller.AutoplayGrade = (JudgeGrade)99;
            var slide = CreateSlide();
            _rig.RegisterSlides((slide, 0.3f));
            yield return null;

            _rig.Step(1.1f);
            _rig.Step(2f);
            _rig.Step(2.02f);

            Assert.That(slide.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.SlideJudgedCount, Is.EqualTo(1));
        }

        /// <summary>Checks that an untouched slide becomes a miss after its good window.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_UntouchedSlideIsMiss()
        {
            var slide = CreateSlide();
            _rig.RegisterSlides((slide, 0.3f));
            yield return null;

            _rig.Step(1f);
            _rig.Step(2.55f);
            Assert.That(slide.State, Is.Not.EqualTo(NoteStatus.End));
            _rig.Step(2.61f);

            Assert.That(slide.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.SlideMissCount, Is.EqualTo(1));
            var result = _rig.SlideParent.GetComponentInChildren<SlideOK>(true);
            Assert.That(result.GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo("Just[52]"));
        }

        /// <summary>Checks that autoplay hides the bars as the star travels and judges the slide at its end.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_AutoplayFinishesSlide()
        {
            _rig.Controller.SetMods(AutoplayModeOption.Enable);
            var slide = CreateSlide();
            var bars = slide.GetComponentsInChildren<SpriteRenderer>(true);
            _rig.RegisterSlides((slide, 0.3f));
            yield return null;

            _rig.Step(0.6f);
            _rig.Step(1.1f);
            Assert.That(slide.State, Is.EqualTo(NoteStatus.Running));
            _rig.Step(1.5f);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxSlide], Is.True);
            Assert.That(bars[2].enabled, Is.False);
            Assert.That(bars[3].enabled, Is.True);

            _rig.Step(2f);
            Assert.That(PrivateMember.GetField<bool>(slide, "IsJudged"), Is.True);
            _rig.Step(2.02f);

            Assert.That(slide.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.SlideCriticalCount, Is.EqualTo(1));
        }

        /// <summary>Checks that an untouched mine slide is a perfect once its answer timing passes.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_AvoidedMineSlideIsPerfect()
        {
            var slide = CreateSlide(isMine: true);
            _rig.RegisterSlides((slide, 0.3f));
            yield return null;

            _rig.Step(1.1f);
            _rig.Step(1.5f);
            Assert.That(PrivateMember.GetField<bool>(slide, "IsJudged"), Is.False);
            _rig.Step(1.85f);
            Assert.That(PrivateMember.GetField<bool>(slide, "IsJudged"), Is.True);
            for (var frame = 1; frame <= 15; frame++)
            {
                _rig.Step(1.85f + (frame / 60f));
            }

            Assert.That(slide.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.SlideCriticalCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Plays a two-part connection slide: the second part starts when the first is nearly done, forces the first to
        /// finish, and reports one result for the group when it ends.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_ConnectionSlideReportsOnce()
        {
            var head = CreateSlide(initialize: false);
            var tail = CreateSlide(startPos: 3, initialize: false);
            head.ConnectInfo = new ConnSlideInfo()
            {
                IsGroupPart = true,
                IsGroupPartHead = true,
                TotalJudgeQueueLen = 6,
                StartTiming = 1f
            };
            tail.ConnectInfo = new ConnSlideInfo()
            {
                IsGroupPart = true,
                IsGroupPartEnd = true,
                TotalJudgeQueueLen = 6,
                StartTiming = 1f,
                Parent = head
            };
            head.Init();
            tail.Init();
            _rig.RegisterSlides((head, 0.3f), (tail, 0.31f));
            yield return null;

            Assert.That(tail.StartTiming, Is.EqualTo(2f));
            Assert.That(tail.Parent, Is.SameAs(head));
            Assert.That(head.FinalStarAngle, Is.Not.EqualTo(default(Quaternion)));

            PlaySensors(1f, SensorArea.A1, SensorArea.A2, SensorArea.A3);
            Assert.That(head.IsFinished, Is.True);
            PlaySensors(1.4f, SensorArea.A3, SensorArea.A4, SensorArea.A5);
            Assert.That(tail.IsFinished, Is.True);

            for (var frame = 1; frame <= 20; frame++)
            {
                _rig.Step(1.8f + (frame / 60f));
            }

            Assert.That(tail.State, Is.EqualTo(NoteStatus.End));
            Assert.That(head.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.SlideJudgedCount, Is.EqualTo(1));
        }

        /// <summary>Creates, configures and optionally initializes a line3 slide.</summary>
        /// <param name="startPos">The start key.</param>
        /// <param name="isBreak">Whether the slide is a break.</param>
        /// <param name="isEach">Whether the slide is an each slide.</param>
        /// <param name="isMine">Whether the slide is a mine.</param>
        /// <param name="isMirror">Whether the slide is mirrored.</param>
        /// <param name="isJustR">Whether the slide result faces right.</param>
        /// <param name="initialize">Whether to call Init.</param>
        /// <returns>The slide.</returns>
        private SlideDrop CreateSlide(int startPos = 1,
                                      bool isBreak = false,
                                      bool isEach = false,
                                      bool isMine = false,
                                      bool isMirror = false,
                                      bool isJustR = false,
                                      bool initialize = true)
        {
            var slide = _rig.SpawnSlide<SlideDrop>(_rig.CreateSlideTemplate("line3"));
            slide.StartPos = startPos;
            slide.Timing = 0.5f;
            slide.StartTiming = 1f;
            slide.Length = 1f;
            slide.Speed = 7.5f;
            slide.IsBreak = isBreak;
            slide.IsEach = isEach;
            slide.IsMine = isMine;
            slide.IsMirror = isMirror;
            slide.IsJustR = isJustR;
            slide.SortOrder = 50;
            if (initialize)
            {
                slide.Init();
            }
            return slide;
        }

        /// <summary>Slides a finger over three sensors in three steps a tenth of a second apart.</summary>
        /// <param name="start">The time of the first step.</param>
        /// <param name="first">The first sensor.</param>
        /// <param name="second">The second sensor.</param>
        /// <param name="third">The third sensor.</param>
        private void PlaySensors(float start, SensorArea first, SensorArea second, SensorArea third)
        {
            _rig.ClearInput();
            _rig.SetSensor(first, SwitchStatus.Off, SwitchStatus.On);
            _rig.Step(start);
            _rig.SetSensor(first, SwitchStatus.On, SwitchStatus.Off);
            _rig.SetSensor(second, SwitchStatus.Off, SwitchStatus.On);
            _rig.Step(start + 0.1f);
            _rig.SetSensor(second, SwitchStatus.On, SwitchStatus.Off);
            _rig.Step(start + 0.2f);
            _rig.SetSensor(third, SwitchStatus.Off, SwitchStatus.On);
            _rig.Step(start + 0.3f);
        }

        /// <summary>Runs only the animation update of a slide at a given time.</summary>
        /// <param name="slide">The slide.</param>
        /// <param name="time">The chart time.</param>
        private void UpdateAt(SlideDrop slide, float time)
        {
            _rig.Controller.ThisFrameSec = time;
            slide.OnUpdate();
        }
    }
}
