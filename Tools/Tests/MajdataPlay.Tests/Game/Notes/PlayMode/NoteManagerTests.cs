using System.Collections;
using Cysharp.Threading.Tasks;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game.Buffers;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Scenes.Game.Notes.Controllers;
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
    /// Covers the per-frame input snapshot of <see cref="NoteManager"/>: hardware and simulated input, click
    /// consumption, status queries with invalid arguments, note queue indexes and the updater dispatch.
    /// </summary>
    public class NoteManagerTests
    {
        /// <summary>The gameplay objects of the current test.</summary>
        private GameplayRig _rig = null!;

        /// <summary>Gets the note manager under test.</summary>
        private NoteManager Manager => _rig.NoteManager;

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

        /// <summary>Checks that the manager registers itself and releases the singleton when destroyed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Lifecycle_RegistersAndFreesSingleton()
        {
            Assert.That(Majdata<NoteManager>.Instance, Is.SameAs(Manager));

            Object.Destroy(Manager.gameObject);
            yield return null;

            Assert.That(Majdata<NoteManager>.IsNull, Is.True);
        }

        /// <summary>Checks that a raw button press edge becomes a click and a held button does not.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GameInput_DetectsButtonEdges()
        {
            _rig.SetButton(ButtonZone.A3, SwitchStatus.Off, SwitchStatus.On);
            _rig.Step(0f);

            Assert.That(Manager.IsButtonClickedInThisFrame(ButtonZone.A3), Is.True);
            Assert.That(Manager.CheckButtonStatusInThisFrame(ButtonZone.A3, SwitchStatus.On), Is.True);
            Assert.That(Manager.CheckButtonStatusInPreviousFrame(ButtonZone.A3, SwitchStatus.Off), Is.True);
            Assert.That(Manager.GetButtonStatusInThisFrame(ButtonZone.A3), Is.EqualTo(SwitchStatus.On));
            Assert.That(Manager.GetButtonStatusInPreviousFrame(ButtonZone.A3), Is.EqualTo(SwitchStatus.Off));

            _rig.SetButton(ButtonZone.A3, SwitchStatus.On, SwitchStatus.On);
            _rig.Step(0.1f);
            yield return null;

            Assert.That(Manager.IsButtonClickedInThisFrame(ButtonZone.A3), Is.False);
            Assert.That(Manager.CheckButtonStatusInPreviousFrame(ButtonZone.A3, SwitchStatus.On), Is.True);
        }

        /// <summary>Checks that a raw sensor press edge becomes a click.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GameInput_DetectsSensorEdges()
        {
            _rig.SetSensor(SensorArea.E5, SwitchStatus.Off, SwitchStatus.On);
            _rig.Step(0f);
            yield return null;

            Assert.That(Manager.IsSensorClickedInThisFrame(SensorArea.E5), Is.True);
            Assert.That(Manager.CheckSensorStatusInThisFrame(SensorArea.E5, SwitchStatus.On), Is.True);
            Assert.That(Manager.CheckSensorStatusInPreviousFrame(SensorArea.E5, SwitchStatus.Off), Is.True);
            Assert.That(Manager.GetSensorStatusInThisFrame(SensorArea.E5), Is.EqualTo(SwitchStatus.On));
            Assert.That(Manager.GetSensorStatusInPreviousFrame(SensorArea.E5), Is.EqualTo(SwitchStatus.Off));
            Assert.That(Manager.IsSensorClickedInThisFrame(SensorArea.E6), Is.False);
        }

        /// <summary>Checks that the button ring also drives the outer sensors when the modifier asks for it.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GameInput_ButtonRingDrivesOuterSensorsWhenEnabled()
        {
            _rig.Controller.SetMods(AutoplayModeOption.Disable, buttonRingForTouch: true);
            UnityMessages.Send(Manager, "Start");
            _rig.SetButton(ButtonZone.A2, SwitchStatus.Off, SwitchStatus.On);

            _rig.Step(0f);
            yield return null;

            Assert.That(Manager.IsSensorClickedInThisFrame(SensorArea.A2), Is.True);
            Assert.That(PrivateMember.GetStaticField<bool>(typeof(NoteManager), "_isUseButtonRingForTouch"), Is.True);
        }

        /// <summary>Checks that a click can be consumed only once per frame and becomes available again next frame.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TryUseClickEvent_ConsumesOncePerFrame()
        {
            _rig.SetButton(ButtonZone.A1, SwitchStatus.Off, SwitchStatus.On);
            _rig.SetSensor(SensorArea.B1, SwitchStatus.Off, SwitchStatus.On);
            _rig.Step(0f);

            Assert.That(Manager.TryUseButtonClickEvent(ButtonZone.A1), Is.True);
            Assert.That(Manager.TryUseButtonClickEvent(ButtonZone.A1), Is.False);
            Assert.That(Manager.TryUseSensorClickEvent(SensorArea.B1), Is.True);
            Assert.That(Manager.TryUseSensorClickEvent(SensorArea.B1), Is.False);

            _rig.Step(0.1f);
            yield return null;

            Assert.That(Manager.TryUseButtonClickEvent(ButtonZone.A1), Is.True);
            Assert.That(Manager.TryUseSensorClickEvent(SensorArea.B1), Is.True);
        }

        /// <summary>Checks that every query rejects missing and out-of-range buttons with a default result.</summary>
        [Test]
        public void ButtonQueries_RejectNullAndOutOfRangeZones()
        {
            foreach (var zone in new ButtonZone?[] { null, ButtonZone.Test, (ButtonZone)(-1) })
            {
                Assert.That(Manager.TryUseButtonClickEvent(zone), Is.False);
                Assert.That(Manager.CheckButtonStatusInThisFrame(zone, SwitchStatus.Off), Is.False);
                Assert.That(Manager.CheckButtonStatusInPreviousFrame(zone, SwitchStatus.Off), Is.False);
                Assert.That(Manager.GetButtonStatusInThisFrame(zone), Is.EqualTo(SwitchStatus.Off));
                Assert.That(Manager.GetButtonStatusInPreviousFrame(zone), Is.EqualTo(SwitchStatus.Off));
                Assert.That(Manager.IsButtonClickedInThisFrame(zone), Is.False);
                Assert.That(Manager.SimulateButtonPress(zone), Is.False);
                Assert.That(Manager.SimulateButtonClick(zone), Is.False);
            }
        }

        /// <summary>Checks that every query rejects missing and out-of-range sensors with a default result.</summary>
        [Test]
        public void SensorQueries_RejectNullAndOutOfRangeAreas()
        {
            foreach (var area in new SensorArea?[] { null, (SensorArea)33, (SensorArea)(-1) })
            {
                Assert.That(Manager.TryUseSensorClickEvent(area), Is.False);
                Assert.That(Manager.CheckSensorStatusInThisFrame(area, SwitchStatus.Off), Is.False);
                Assert.That(Manager.CheckSensorStatusInPreviousFrame(area, SwitchStatus.Off), Is.False);
                Assert.That(Manager.GetSensorStatusInThisFrame(area), Is.EqualTo(SwitchStatus.Off));
                Assert.That(Manager.GetSensorStatusInPreviousFrame(area), Is.EqualTo(SwitchStatus.Off));
                Assert.That(Manager.IsSensorClickedInThisFrame(area), Is.False);
                Assert.That(Manager.SimulateSensorPress(area), Is.False);
                Assert.That(Manager.SimulateSensorClick(area), Is.False);
            }
        }

        /// <summary>Checks the return values of simulated presses and clicks.</summary>
        [Test]
        public void Simulate_ReportsWhetherStateChanged()
        {
            Assert.That(Manager.SimulateButtonPress(ButtonZone.A4), Is.True);
            Assert.That(Manager.SimulateButtonPress(ButtonZone.A4), Is.False);
            Assert.That(Manager.SimulateButtonClick(ButtonZone.A5), Is.True);
            Assert.That(Manager.SimulateButtonClick(ButtonZone.A5), Is.False);
            Assert.That(Manager.SimulateSensorPress(SensorArea.C), Is.True);
            Assert.That(Manager.SimulateSensorPress(SensorArea.C), Is.False);
            Assert.That(Manager.SimulateSensorClick(SensorArea.D3), Is.True);
            Assert.That(Manager.SimulateSensorClick(SensorArea.D3), Is.False);
        }

        /// <summary>Checks that autoplay input moves simulated presses into the next frame and releases them after.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator AutoplayInput_AppliesSimulatedStateOnNextFrame()
        {
            PrivateMember.SetField(Manager, "USERSETTING_IS_AUTOPLAY", true);
            Manager.SimulateButtonClick(ButtonZone.A7);
            Manager.SimulateSensorClick(SensorArea.B7);

            _rig.Step(0f);

            Assert.That(Manager.IsButtonClickedInThisFrame(ButtonZone.A7), Is.True);
            Assert.That(Manager.IsSensorClickedInThisFrame(SensorArea.B7), Is.True);
            Assert.That(Manager.SimulateButtonClick(ButtonZone.A7), Is.False);

            _rig.Step(0.1f);
            Assert.That(Manager.GetButtonStatusInThisFrame(ButtonZone.A7), Is.EqualTo(SwitchStatus.On));
            Assert.That(Manager.IsButtonClickedInThisFrame(ButtonZone.A7), Is.False);

            _rig.Step(0.2f);
            yield return null;

            Assert.That(Manager.GetButtonStatusInThisFrame(ButtonZone.A7), Is.EqualTo(SwitchStatus.Off));
            Assert.That(Manager.GetSensorStatusInThisFrame(SensorArea.B7), Is.EqualTo(SwitchStatus.Off));
            Assert.That(Manager.GetSensorStatusInPreviousFrame(SensorArea.B7), Is.EqualTo(SwitchStatus.Off));
        }

        /// <summary>Checks that autoplay input lets the button ring drive the outer sensors when enabled.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator AutoplayInput_ButtonRingDrivesOuterSensorsWhenEnabled()
        {
            PrivateMember.SetField(Manager, "USERSETTING_IS_AUTOPLAY", true);
            PrivateMember.SetStaticField(typeof(NoteManager), "_isUseButtonRingForTouch", true);
            Manager.SimulateButtonPress(ButtonZone.A1);

            _rig.Step(0f);
            yield return null;

            Assert.That(Manager.GetSensorStatusInThisFrame(SensorArea.A1), Is.EqualTo(SwitchStatus.On));
            Assert.That(Manager.IsSensorClickedInThisFrame(SensorArea.A1), Is.True);
            Assert.That(Manager.GetSensorStatusInThisFrame(SensorArea.A2), Is.EqualTo(SwitchStatus.Off));
        }

        /// <summary>Checks the tap queue index bookkeeping, including keys outside the ring.</summary>
        [Test]
        public void TapQueue_AdvancesPerKey()
        {
            var first = new TapQueueInfo() { Index = 0, KeyIndex = 2 };
            var second = new TapQueueInfo() { Index = 1, KeyIndex = 2 };
            var otherKey = new TapQueueInfo() { Index = 1, KeyIndex = 3 };

            Assert.That(Manager.IsCurrentNoteJudgeable(first), Is.True);
            Assert.That(Manager.IsCurrentNoteJudgeable(second), Is.False);

            Manager.NextNote(first);

            Assert.That(Manager.IsCurrentNoteJudgeable(second), Is.True);
            Assert.That(Manager.IsCurrentNoteJudgeable(otherKey), Is.False);
            Assert.That(Manager.IsCurrentNoteJudgeable(new TapQueueInfo() { Index = 0, KeyIndex = 0 }), Is.False);
            Assert.That(Manager.IsCurrentNoteJudgeable(new TapQueueInfo() { Index = 0, KeyIndex = 9 }), Is.False);
            Assert.DoesNotThrow(() => Manager.NextNote(new TapQueueInfo() { Index = 0, KeyIndex = 9 }));
        }

        /// <summary>Checks the touch queue index bookkeeping, including sensors outside the panel.</summary>
        [Test]
        public void TouchQueue_AdvancesPerSensor()
        {
            var first = new TouchQueueInfo() { Index = 0, SensorPos = SensorArea.E1 };
            var second = new TouchQueueInfo() { Index = 1, SensorPos = SensorArea.E1 };
            var invalid = new TouchQueueInfo() { Index = 0, SensorPos = (SensorArea)40 };

            Assert.That(Manager.IsCurrentNoteJudgeable(second), Is.False);
            Manager.NextTouch(first);

            Assert.That(Manager.IsCurrentNoteJudgeable(second), Is.True);
            Assert.That(Manager.IsCurrentNoteJudgeable(invalid), Is.False);
            Assert.DoesNotThrow(() => Manager.NextTouch(invalid));
        }

        /// <summary>Checks that resetting the counters and clearing the manager rewind every queue and input state.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ResetCounterAndClear_RewindState()
        {
            var tap = new TapQueueInfo() { Index = 1, KeyIndex = 1 };
            var touch = new TouchQueueInfo() { Index = 1, SensorPos = SensorArea.C };
            Manager.NextNote(new TapQueueInfo() { Index = 0, KeyIndex = 1 });
            Manager.NextTouch(new TouchQueueInfo() { Index = 0, SensorPos = SensorArea.C });
            Manager.ResetCounter();
            Assert.That(Manager.IsCurrentNoteJudgeable(tap), Is.False);
            Assert.That(Manager.IsCurrentNoteJudgeable(touch), Is.False);

            _rig.SetButton(ButtonZone.A1, SwitchStatus.On, SwitchStatus.On);
            _rig.Step(0f);
            Manager.NextNote(new TapQueueInfo() { Index = 0, KeyIndex = 1 });
            Assert.That(Manager.TryUseButtonClickEvent(ButtonZone.A1), Is.True);
            Manager.Clear();
            yield return null;

            Assert.That(Manager.GetButtonStatusInThisFrame(ButtonZone.A1), Is.EqualTo(SwitchStatus.Off));
            Assert.That(Manager.IsCurrentNoteJudgeable(tap), Is.False);
            Assert.That(Manager.TryUseButtonClickEvent(ButtonZone.A1), Is.True);
        }

        /// <summary>Checks that InitAsync initializes every updater so they pick up the notes under their roots.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator InitAsync_InitializesUpdaters() => UniTask.ToCoroutine(async () =>
        {
            var tap = _rig.Spawn<TapDrop>(_rig.TapTemplate, _rig.TapParent);
            tap.Init(GameplayRig.TapInfo(timing: 1f));

            await Manager.InitAsync();

            var instances = PrivateMember.GetField<TapDrop[]>(_rig.TapUpdater, "NoteInstances");
            Assert.That(instances, Is.EquivalentTo(new[] { tap }));
        });

        /// <summary>Checks that the frame phases sum the elapsed time reported by the updaters.</summary>
        [Test]
        public void FramePhases_RecordElapsedTime()
        {
            Manager.OnPreUpdate();
            Manager.OnUpdate();
            Manager.OnLateUpdate();
            Manager.OnFixedUpdate();

            Assert.That(PrivateMember.GetField<double>(Manager, "_fixedUpdateElapsedMs"), Is.EqualTo(0d));
            Assert.That(PrivateMember.GetField<double>(Manager, "_preUpdateElapsedMs"), Is.GreaterThanOrEqualTo(0d));
            Assert.That(PrivateMember.GetField<double>(Manager, "_updateElapsedMs"), Is.GreaterThanOrEqualTo(0d));
            Assert.That(PrivateMember.GetField<double>(Manager, "_lateUpdateElapsedMs"), Is.GreaterThanOrEqualTo(0d));
        }
    }
}
