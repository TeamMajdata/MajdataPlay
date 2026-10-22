using System;
using System.Collections;
using MajdataPlay.Scenes.Game.Buffers;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Scenes.Game.Notes.Controllers;
using MajdataPlay.Tests;
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
    /// Covers the per-type note updaters: collecting notes, dispatching frame phases only to notes in play, slide
    /// activation and the singleton registration of each updater.
    /// </summary>
    public class NoteUpdaterTests
    {
        /// <summary>The gameplay objects of the current test.</summary>
        private GameplayRig _rig = null!;

        /// <summary>Builds the gameplay objects.</summary>
        /// <returns>The setup coroutine.</returns>
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _rig = new GameplayRig(initializeEffects: false);
            yield return null;
        }

        /// <summary>Destroys the gameplay objects and restores the global state.</summary>
        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
        }

        /// <summary>Checks that Init collects matching notes and skips children without the note component.</summary>
        [Test]
        public void Init_CollectsOnlyMatchingNotes()
        {
            var tap = _rig.Spawn<TapDrop>(_rig.TapTemplate, _rig.TapParent);
            NoteTemplates.Child("NotANote", _rig.TapParent);

            _rig.TapUpdater.Init();

            Assert.That(PrivateMember.GetField<TapDrop[]>(_rig.TapUpdater, "NoteInstances"), Is.EqualTo(new[] { tap }));
        }

        /// <summary>Checks that notes are only updated between initialization and their end.</summary>
        [Test]
        public void Dispatch_SkipsNotesOutOfPlay()
        {
            var waiting = _rig.Spawn<TapDrop>(_rig.TapTemplate, _rig.TapParent);
            var playing = _rig.SpawnTap(GameplayRig.TapInfo(timing: 1f));
            _rig.Controller.ThisFrameSec = 0.6f;

            _rig.TapUpdater.OnPreUpdate();
            _rig.TapUpdater.OnUpdate();
            _rig.TapUpdater.OnLateUpdate();
            _rig.TapUpdater.OnFixedUpdate();

            Assert.That(waiting.State, Is.EqualTo(NoteStatus.Start));
            Assert.That(playing.State, Is.EqualTo(NoteStatus.Running));
            Assert.That(_rig.TapUpdater.PreUpdateElapsedMs, Is.EqualTo(0d));
            Assert.That(_rig.TapUpdater.UpdateElapsedMs, Is.EqualTo(0d));
            Assert.That(_rig.TapUpdater.FixedUpdateElapsedMs, Is.EqualTo(0d));
            Assert.That(_rig.TapUpdater.LateUpdateElapsedMs, Is.EqualTo(0d));
        }

        /// <summary>Checks that the hold, touch and touch hold updaters drive their notes and skip finished ones.</summary>
        [Test]
        public void Dispatch_DrivesHoldTouchAndTouchHoldNotes()
        {
            var hold = _rig.SpawnHold(GameplayRig.HoldInfo(timing: 1f));
            var touch = _rig.SpawnTouch(GameplayRig.TouchInfo(timing: 1f));
            var touchHold = _rig.SpawnTouchHold(GameplayRig.TouchHoldInfo(timing: 1f));
            _rig.Controller.ThisFrameSec = 0.7f;

            foreach (var phase in new Action[]
            {
                _rig.HoldUpdater.OnPreUpdate,
                _rig.HoldUpdater.OnUpdate,
                _rig.HoldUpdater.OnLateUpdate,
                _rig.HoldUpdater.OnFixedUpdate,
                _rig.TouchUpdater.OnPreUpdate,
                _rig.TouchUpdater.OnUpdate,
                _rig.TouchUpdater.OnLateUpdate,
                _rig.TouchUpdater.OnFixedUpdate,
                _rig.TouchHoldUpdater.OnPreUpdate,
                _rig.TouchHoldUpdater.OnUpdate,
                _rig.TouchHoldUpdater.OnLateUpdate,
                _rig.TouchHoldUpdater.OnFixedUpdate,
                _rig.EachLineUpdater.OnPreUpdate,
                _rig.EachLineUpdater.OnUpdate,
                _rig.EachLineUpdater.OnFixedUpdate,
                _rig.SlideUpdater.OnFixedUpdate,
                _rig.SlideOKUpdater.OnFixedUpdate
            })
            {
                phase();
            }

            Assert.That(hold.State, Is.EqualTo(NoteStatus.Running));
            Assert.That(touch.State, Is.EqualTo(NoteStatus.Running));
            Assert.That(touchHold.State, Is.EqualTo(NoteStatus.Running));
        }

        /// <summary>Checks that slide queue information is validated, filtered and sorted.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SlideUpdater_SortsQueueAndActivatesInOrder()
        {
            var late = CreateSlide();
            var early = CreateSlide();
            yield return null;

            Assert.Throws<ArgumentNullException>(() => _rig.SlideUpdater.AddSlideQueueInfos(null!));
            _rig.SlideUpdater.AddSlideQueueInfos(new SlideQueueInfo?[]
            {
                new SlideQueueInfo() { AppearTiming = 2f, SlideObject = late },
                null,
                new SlideQueueInfo() { AppearTiming = 1f, SlideObject = early }
            }!);
            _rig.SlideUpdater.Init();

            _rig.Step(0.9f);
            Assert.That(early.Active, Is.True);
            Assert.That(late.Active, Is.False);
            _rig.Step(1.9f);
            Assert.That(late.Active, Is.True);
        }

        /// <summary>Checks that the slide updater dispatches wifi and extend slides and ignores unknown slide types.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SlideUpdater_DispatchesEverySlideKind()
        {
            var wifi = _rig.SpawnSlide<WifiDrop>(NoteTemplates.Wifi(_rig.Templates, _rig.StarTemplate));
            wifi.Timing = 0.5f;
            wifi.StartTiming = 1f;
            wifi.Length = 1f;
            wifi.Init();
            var probe = _rig.Objects.Add<ProbeSlide>("Probe");
            probe.Init();
            ExtendSlideDrop? extend = null;
            if (_rig.Runtime != null)
            {
                extend = _rig.SpawnSlide<ExtendSlideDrop>(NoteTemplates.ExtendSlide(_rig.Templates, _rig.StarTemplate));
                extend.Metadata = new MajdataPlay.Scenes.Game.Parsing.Slide.ExtendSlideMetadata(
                    new[]
                    {
                        new MajdataPlay.Scenes.Game.Parsing.Slide.ParsingSlideArea(0, 1, MajdataPlay.IO.SensorArea.A1, null),
                        new MajdataPlay.Scenes.Game.Parsing.Slide.ParsingSlideArea(2, 3, MajdataPlay.IO.SensorArea.A2, null)
                    },
                    0.2f,
                    1f,
                    new[]
                    {
                        new MajdataPlay.Scenes.Game.Parsing.Slide.SlidePose(0f, 4f, 0f, 0f),
                        new MajdataPlay.Scenes.Game.Parsing.Slide.SlidePose(1f, 4f, 0f, 0f),
                        new MajdataPlay.Scenes.Game.Parsing.Slide.SlidePose(2f, 4f, 0f, 0f)
                    },
                    false,
                    new MajdataPlay.Scenes.Game.Parsing.Slide.SlidePose(0f, 0f, 0f, 0f),
                    MajdataPlay.Scenes.Game.Parsing.Slide.SlideOkType.CircleL,
                    MajdataPlay.Scenes.Game.Parsing.Slide.SlideFlag.None);
                extend.Timing = 0.5f;
                extend.StartTiming = 1f;
                extend.Length = 1f;
                extend.Init();
            }
            var slides = extend is null
                ? new (SlideBase, float)[] { (wifi, 0.2f), (probe, 0.2f) }
                : new (SlideBase, float)[] { (wifi, 0.2f), (probe, 0.2f), (extend, 0.2f) };
            _rig.RegisterSlides(slides);
            yield return null;

            _rig.Step(0.1f);
            _rig.Step(0.75f);
            _rig.Step(2.7f);

            Assert.That(wifi.State, Is.EqualTo(NoteStatus.End));
            Assert.That(probe.State, Is.EqualTo(NoteStatus.Inited));
            if (extend is not null)
            {
                Assert.That(extend.State, Is.EqualTo(NoteStatus.End));
            }
            _rig.Step(2.8f);
            Assert.That(_rig.Counter.JudgeStats.SlideMissCount, Is.EqualTo(extend is null ? 1 : 2));
        }

        /// <summary>Checks that every updater registers itself as singleton and releases it when destroyed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Updaters_RegisterAndFreeSingletons()
        {
            Assert.That(Majdata<TapUpdater>.Instance, Is.SameAs(_rig.TapUpdater));
            Assert.That(Majdata<HoldUpdater>.Instance, Is.SameAs(_rig.HoldUpdater));
            Assert.That(Majdata<TouchUpdater>.Instance, Is.SameAs(_rig.TouchUpdater));
            Assert.That(Majdata<TouchHoldUpdater>.Instance, Is.SameAs(_rig.TouchHoldUpdater));
            Assert.That(Majdata<EachLineUpdater>.Instance, Is.SameAs(_rig.EachLineUpdater));
            Assert.That(Majdata<SlideUpdater>.Instance, Is.SameAs(_rig.SlideUpdater));
            Assert.That(Majdata<SlideOKUpdater>.Instance, Is.SameAs(_rig.SlideOKUpdater));

            foreach (var updater in new Component[] { _rig.TapUpdater, _rig.HoldUpdater, _rig.TouchUpdater, _rig.TouchHoldUpdater, _rig.EachLineUpdater, _rig.SlideUpdater, _rig.SlideOKUpdater })
            {
                Object.Destroy(updater.gameObject);
            }
            yield return null;

            Assert.That(Majdata<TapUpdater>.IsNull, Is.True);
            Assert.That(Majdata<HoldUpdater>.IsNull, Is.True);
            Assert.That(Majdata<TouchUpdater>.IsNull, Is.True);
            Assert.That(Majdata<TouchHoldUpdater>.IsNull, Is.True);
            Assert.That(Majdata<EachLineUpdater>.IsNull, Is.True);
            Assert.That(Majdata<SlideUpdater>.IsNull, Is.True);
            Assert.That(Majdata<SlideOKUpdater>.IsNull, Is.True);
        }

        /// <summary>Creates an initialized line3 slide.</summary>
        /// <returns>The slide.</returns>
        private SlideDrop CreateSlide()
        {
            var slide = _rig.SpawnSlide<SlideDrop>(_rig.CreateSlideTemplate("line3"));
            slide.Timing = 0.5f;
            slide.StartTiming = 1f;
            slide.Length = 1f;
            slide.Init();
            return slide;
        }
    }
}
