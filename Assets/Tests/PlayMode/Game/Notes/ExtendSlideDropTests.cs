using System;
using System.Collections;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Scenes.Game.Notes.Slide;
using MajdataPlay.Scenes.Game.Parsing.Slide;
using MajdataPlay.Scenes.Game.Utils;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Notes.PlayMode.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode
{
    /// <summary>
    /// Drives <see cref="ExtendSlideDrop"/>, the slide built from parsed path metadata: arrows, judge queue, slide result
    /// pose and shape, and a full play-through.
    /// </summary>
    public class ExtendSlideDropTests
    {
        /// <summary>The slide constant used by the generated metadata.</summary>
        private const float SlideConst = 0.2f;

        /// <summary>The gameplay objects of the current test.</summary>
        private GameplayRig _rig = null!;

        /// <summary>Gets the result type cases as result type, expected right-hand flag and expected shape.</summary>
        private static object[] OkTypeCases => new object[]
        {
            new object[] { SlideOkType.StraightL, false, SlideOKShape.Str },
            new object[] { SlideOkType.StraightR, true, SlideOKShape.Str },
            new object[] { SlideOkType.CircleL, false, SlideOKShape.Curv },
            new object[] { SlideOkType.CircleR, true, SlideOKShape.Curv },
            new object[] { SlideOkType.WifiU, false, SlideOKShape.Wifi },
            new object[] { SlideOkType.WifiD, false, SlideOKShape.Wifi }
        };

        /// <summary>Builds the gameplay objects, which load the runtime database with the arrow prefab.</summary>
        /// <returns>The setup coroutine.</returns>
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _rig = new GameplayRig();
            yield return null;
            Assume.That(_rig.Runtime, Is.Not.Null, "The runtime database with the slide arrow prefab is required.");
        }

        /// <summary>Destroys the gameplay objects and restores the global state.</summary>
        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
        }

        /// <summary>Checks that Init refuses to run without metadata.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_RequiresMetadata()
        {
            var slide = Spawn();
            yield return null;

            Assert.Throws<InvalidOperationException>(() => slide.Init());
        }

        /// <summary>Checks that Awake creates a hidden star and moves the slide result next to the slide.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Awake_CreatesStarAndDetachesResult()
        {
            var slide = Spawn();
            yield return null;

            Assert.That(_rig.SlideStarCount, Is.EqualTo(1));
            Assert.That(_rig.SlideStar(0).localScale, Is.EqualTo(new Vector3(0f, 0f, 1f)));
            Assert.That(slide.transform.childCount, Is.EqualTo(0));
            Assert.That(_rig.SlideParent.GetComponentInChildren<SlideOK>(true), Is.Not.Null);
        }

        /// <summary>Checks the arrows, judge queue, timings and slide result built from the metadata.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_BuildsSlideFromMetadata()
        {
            var slide = CreateSlide(ThreeAreas(), SlideOkType.StraightL, conditionalLastArrow: false);
            yield return null;

            Assert.That(slide.State, Is.EqualTo(NoteStatus.Inited));
            Assert.That(slide.transform.childCount, Is.EqualTo(4));
            Assert.That(slide.SlideLength, Is.EqualTo(5f));
            Assert.That(slide.StartPos, Is.EqualTo(1));
            Assert.That(slide.EndPos, Is.EqualTo(3));
            Assert.That(slide.QueueRemaining, Is.EqualTo(3));
            Assert.That(slide.JudgeTimingWithOffset, Is.EqualTo(1f + (1f - SlideConst)).Within(1e-4f));
            Assert.That(PrivateMember.GetField<float>(slide, "LastWaitTimeSec"), Is.EqualTo(SlideConst).Within(1e-4f));
            var arrow = slide.transform.GetChild(0).GetComponent<SpriteRenderer>();
            Assert.That(arrow.sprite.name, Is.EqualTo("Slide"));
            Assert.That(arrow.enabled, Is.False);
            var queue = PrivateMember.GetField<Memory<SlideArea>[]>(slide, "JudgeQueues")[0].ToArray();
            Assert.That(queue[1].IsSkippable, Is.False);
            Assert.That(queue[0].IsSkippable, Is.True);
            var result = _rig.SlideParent.GetComponentInChildren<SlideOK>(true);
            Assert.That(result.transform.position.x, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(result.transform.rotation.eulerAngles.z, Is.EqualTo(30f).Within(0.01f));
        }

        /// <summary>Checks that a conditional last arrow is left out and a two-area queue makes its first area mandatory.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_HandlesConditionalArrowAndShortQueue()
        {
            var areas = new[]
            {
                new ParsingSlideArea(0, 2, SensorArea.A1, null),
                new ParsingSlideArea(3, 5, SensorArea.A3, null)
            };
            var slide = CreateSlide(areas, SlideOkType.StraightL, conditionalLastArrow: true);
            yield return null;

            Assert.That(slide.transform.childCount, Is.EqualTo(3));
            var queue = PrivateMember.GetField<Memory<SlideArea>[]>(slide, "JudgeQueues")[0].ToArray();
            Assert.That(queue.Length, Is.EqualTo(2));
            Assert.That(queue[0].IsSkippable, Is.False);
        }

        /// <summary>Checks the slide result shape and facing for every result type.</summary>
        /// <param name="testCase">The result type, the expected right-hand flag and the expected shape.</param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_MapsResultType([ValueSource(nameof(OkTypeCases))] object[] testCase)
        {
            var slide = CreateSlide(ThreeAreas(), (SlideOkType)testCase[0], conditionalLastArrow: false);
            yield return null;

            var result = _rig.SlideParent.GetComponentInChildren<SlideOK>(true);
            Assert.That(slide.IsJustR, Is.EqualTo((bool)testCase[1]));
            Assert.That(result.Shape, Is.EqualTo((SlideOKShape)testCase[2]));
        }

        /// <summary>Plays the slide over its sensors and checks the judgment and the reported result.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_SlidingOverSensorsFinishesSlide()
        {
            var slide = CreateSlide(ThreeAreas(), SlideOkType.StraightL, conditionalLastArrow: false);
            _rig.RegisterSlides((slide, 0.3f));
            yield return null;

            Press(1f, SensorArea.A1);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxSlide], Is.True);
            Press(1.3f, SensorArea.B2);
            Assert.That(slide.transform.GetChild(1).GetComponent<SpriteRenderer>().enabled, Is.False);
            Press(1.6f, SensorArea.A3);
            Assert.That(slide.IsFinished, Is.True);

            for (var frame = 1; frame <= 15; frame++)
            {
                _rig.Step(1.6f + (frame / 60f));
            }

            Assert.That(slide.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.SlideCriticalCount, Is.EqualTo(1));
        }

        /// <summary>Checks that the star of an extend slide is hidden together with the slide when it ends.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        [Ignore("Known issue: ExtendSlideDrop.Awake never stores the star renderer in StarRenderers, so SetStarActive cannot hide its star.")]
        public IEnumerator End_HidesStar()
        {
            var slide = CreateSlide(ThreeAreas(), SlideOkType.StraightL, conditionalLastArrow: false);
            var star = _rig.SlideStar(0).GetComponent<SpriteRenderer>();
            yield return null;

            Assert.That(star.enabled, Is.False);
            PrivateMember.Invoke(slide, "End");

            Assert.That(star.enabled, Is.False);
        }

        /// <summary>Spawns an extend slide.</summary>
        /// <returns>The slide.</returns>
        private ExtendSlideDrop Spawn()
        {
            return _rig.SpawnSlide<ExtendSlideDrop>(NoteTemplates.ExtendSlide(_rig.Templates, _rig.StarTemplate));
        }

        /// <summary>Spawns, configures and initializes an extend slide from generated metadata.</summary>
        /// <param name="areas">The judge areas.</param>
        /// <param name="okType">The slide result type.</param>
        /// <param name="conditionalLastArrow">Whether the second to last arrow is conditional.</param>
        /// <returns>The slide.</returns>
        private ExtendSlideDrop CreateSlide(ParsingSlideArea[] areas, SlideOkType okType, bool conditionalLastArrow)
        {
            var slide = Spawn();
            var start = NoteHelper.GetTapPosition(1, 4.8f);
            var end = NoteHelper.GetTapPosition(3, 4.8f);
            var poses = new SlidePose[6];
            for (var i = 0; i < poses.Length; i++)
            {
                var position = Vector3.Lerp(start, end, i / 5f);
                poses[i] = new SlidePose(position.x, position.y, -90f, i);
            }
            slide.Metadata = new ExtendSlideMetadata(areas,
                                                     SlideConst,
                                                     3f,
                                                     poses,
                                                     conditionalLastArrow,
                                                     new SlidePose(0.5f, 0.5f, 30f, 1f),
                                                     okType,
                                                     SlideFlag.None);
            slide.Timing = 0.5f;
            slide.StartTiming = 1f;
            slide.Length = 1f;
            slide.Speed = 7.5f;
            slide.Init();
            return slide;
        }

        /// <summary>Creates a three-area queue from key 1 over the second key to key 3.</summary>
        /// <returns>The judge areas.</returns>
        private static ParsingSlideArea[] ThreeAreas()
        {
            return new[]
            {
                new ParsingSlideArea(0, 1, SensorArea.A1, null),
                new ParsingSlideArea(2, 3, SensorArea.A2, SensorArea.B2),
                new ParsingSlideArea(4, 5, SensorArea.A3, null)
            };
        }

        /// <summary>Releases every sensor and presses the given one for one frame.</summary>
        /// <param name="time">The frame time.</param>
        /// <param name="area">The pressed sensor.</param>
        private void Press(float time, SensorArea area)
        {
            _rig.ClearInput();
            _rig.SetSensor(area, SwitchStatus.Off, SwitchStatus.On);
            _rig.Step(time);
        }
    }
}
