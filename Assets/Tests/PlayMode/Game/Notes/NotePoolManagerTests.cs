using System.Collections;
using MajdataPlay.IO;
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
    /// Covers <see cref="NotePoolManager"/>: pool sizing from the chart key flags, timed activation of pooled notes,
    /// recycling of ended notes, clearing and destruction.
    /// </summary>
    public class NotePoolManagerTests
    {
        /// <summary>The gameplay objects of the current test.</summary>
        private GameplayRig _rig = null!;

        /// <summary>Builds the gameplay objects without initializing the pools.</summary>
        /// <returns>The setup coroutine.</returns>
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _rig = new GameplayRig(initializePools: false);
            yield return null;
        }

        /// <summary>Destroys the gameplay objects and restores the global state.</summary>
        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
        }

        /// <summary>Checks that the manager stays idle until it is initialized.</summary>
        [Test]
        public void OnPreUpdate_IdleBeforeInit()
        {
            Assert.That(_rig.PoolManager.State, Is.EqualTo(ComponentState.Idle));
            Assert.DoesNotThrow(() => _rig.PoolManager.OnPreUpdate());
            Assert.That(Majdata<NotePoolManager>.Instance, Is.SameAs(_rig.PoolManager));
        }

        /// <summary>Checks that Init creates as many pooled notes as configured for every used note type.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_CreatesConfiguredCapacity()
        {
            SetCapacities(2);

            _rig.PoolManager.Init();
            yield return null;

            Assert.That(_rig.PoolManager.State, Is.EqualTo(ComponentState.Running));
            Assert.That(_rig.TapParent.childCount, Is.EqualTo(2));
            Assert.That(_rig.HoldParent.childCount, Is.EqualTo(2));
            Assert.That(_rig.TouchParent.childCount, Is.EqualTo(2));
            Assert.That(_rig.TouchHoldParent.childCount, Is.EqualTo(2));
            Assert.That(_rig.EachLineParent.childCount, Is.EqualTo(2));
            Assert.That(_rig.TapParent.GetChild(0).GetComponent<TapDrop>().State, Is.EqualTo(NoteStatus.Start));
        }

        /// <summary>Checks that unused note types get no pooled notes whatever the configured capacity.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_SkipsUnusedNoteTypes()
        {
            SetCapacities(2);
            _rig.SetLoaderFlags(false);

            _rig.PoolManager.Init();
            yield return null;

            Assert.That(_rig.TapParent.childCount, Is.EqualTo(0));
            Assert.That(_rig.TouchHoldParent.childCount, Is.EqualTo(0));
            Assert.That(_rig.EachLineParent.childCount, Is.EqualTo(0));
        }

        /// <summary>Checks that pooled notes are activated shortly before they appear and recycled after they end.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnPreUpdate_ActivatesAndRecyclesTaps()
        {
            SetCapacities(1);
            var first = GameplayRig.TapInfo(timing: 1f);
            var second = GameplayRig.TapInfo(timing: 3f, index: 1);
            _rig.PoolManager.AddTap(first);
            _rig.PoolManager.AddTap(second);
            _rig.PoolManager.Init();
            _rig.TapUpdater.Init();
            var tap = _rig.TapParent.GetChild(0).GetComponent<TapDrop>();
            yield return null;

            _rig.Step(-0.2f);
            Assert.That(tap.State, Is.EqualTo(NoteStatus.Start));
            _rig.Step(-0.1f);
            Assert.That(tap.State, Is.EqualTo(NoteStatus.Inited));
            Assert.That(first.Instance, Is.SameAs(tap));

            _rig.Step(1.2f);
            Assert.That(tap.State, Is.EqualTo(NoteStatus.End));
            _rig.Step(1.9f);

            Assert.That(tap.State, Is.EqualTo(NoteStatus.Inited));
            Assert.That(tap.Timing, Is.EqualTo(3f));
            Assert.That(second.Instance, Is.SameAs(tap));
        }

        /// <summary>Checks that notes waiting for a free pooled note are activated once one is recycled.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnPreUpdate_WaitsForFreeNote()
        {
            SetCapacities(1);
            var first = GameplayRig.TouchInfo(SensorArea.B1, timing: 1f);
            var second = GameplayRig.TouchInfo(SensorArea.B2, timing: 1f);
            _rig.PoolManager.AddTouch(first);
            _rig.PoolManager.AddTouch(second);
            _rig.PoolManager.Init();
            _rig.TouchUpdater.Init();
            yield return null;

            _rig.Step(0.5f);
            Assert.That(first.Instance, Is.Not.Null);
            Assert.That(second.Instance, Is.Null);

            _rig.Step(1.4f);

            Assert.That(second.Instance, Is.SameAs(first.Instance));
            Assert.That(second.Instance!.State, Is.InRange(NoteStatus.Inited, NoteStatus.Arrived));
        }

        /// <summary>Checks that holds, touch holds and each lines are pooled and activated too.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnPreUpdate_ActivatesOtherNoteTypes()
        {
            SetCapacities(1);
            var hold = GameplayRig.HoldInfo(timing: 1f);
            var touchHold = GameplayRig.TouchHoldInfo(timing: 1f);
            var eachLine = new EachLinePoolingInfo()
            {
                StartPos = 1,
                Timing = 1f,
                AppearTiming = 0f,
                CurvLength = 1,
                DistanceProvider = new FakeDistanceProvider()
            };
            _rig.PoolManager.AddHold(hold);
            _rig.PoolManager.AddTouchHold(touchHold);
            _rig.PoolManager.AddEachLine(eachLine);
            _rig.PoolManager.Init();
            yield return null;

            _rig.Step(0f);

            Assert.That(hold.Instance!.State, Is.EqualTo(NoteStatus.Inited));
            Assert.That(touchHold.Instance!.State, Is.EqualTo(NoteStatus.Inited));
            Assert.That(_rig.EachLineParent.GetChild(0).GetComponent<EachLineDrop>().State, Is.EqualTo(NoteStatus.Inited));
        }

        /// <summary>Checks that collecting every pooled note type is accepted, including types that are not pooled.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Collect_AcceptsEveryNoteType()
        {
            SetCapacities(1);
            _rig.PoolManager.Init();
            yield return null;

            Assert.DoesNotThrow(() =>
            {
                _rig.PoolManager.Collect(_rig.TapParent.GetChild(0).GetComponent<TapDrop>());
                _rig.PoolManager.Collect(_rig.HoldParent.GetChild(0).GetComponent<HoldDrop>());
                _rig.PoolManager.Collect(_rig.TouchParent.GetChild(0).GetComponent<TouchDrop>());
                _rig.PoolManager.Collect(_rig.TouchHoldParent.GetChild(0).GetComponent<TouchHoldDrop>());
                _rig.PoolManager.Collect(_rig.EachLineParent.GetChild(0).GetComponent<EachLineDrop>());
                _rig.PoolManager.Collect("not a note");
            });
        }

        /// <summary>Checks that Clear destroys the pooled notes.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Clear_DestroysPooledNotes()
        {
            SetCapacities(1);
            _rig.PoolManager.AddTap(GameplayRig.TapInfo());
            _rig.PoolManager.Init();
            yield return null;

            _rig.PoolManager.Clear();
            yield return null;

            Assert.That(_rig.TapParent.childCount, Is.EqualTo(0));
            Assert.That(_rig.HoldParent.childCount, Is.EqualTo(0));
        }

        /// <summary>Checks that destroying the manager disposes its pools and frees the singleton.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnDestroy_FreesSingleton()
        {
            SetCapacities(1);
            _rig.PoolManager.Init();
            yield return null;

            Object.Destroy(_rig.PoolManager.gameObject);
            yield return null;

            Assert.That(Majdata<NotePoolManager>.IsNull, Is.True);
        }

        /// <summary>Sets the same pool capacity for every note type.</summary>
        /// <param name="capacity">The capacity.</param>
        private void SetCapacities(int capacity)
        {
            var debug = _rig.Settings.Debug;
            debug.TapPoolCapacity = capacity;
            debug.HoldPoolCapacity = capacity;
            debug.TouchPoolCapacity = capacity;
            debug.TouchHoldPoolCapacity = capacity;
            debug.EachLinePoolCapacity = capacity;
        }
    }
}
