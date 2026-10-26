using System;
using System.Collections.Generic;
using System.Reflection;
using MajdataPlay.IO;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>
    /// Verifies the frame pipeline of the static <see cref="InputManager"/>: device sampling, state transitions,
    /// subscriptions, debouncing and the per-frame queries, using a scripted device instead of hardware.
    /// </summary>
    public class InputManagerTests
    {
        /// <summary>The installed fake devices and restored statics.</summary>
        private InputManagerScope _scope = null!;

        /// <summary>The installed default settings.</summary>
        private SettingsScope _settings = null!;

        /// <summary>The Unity objects created by the current test.</summary>
        private TestObjects _objects = null!;

        /// <summary>The event system selected before the current test.</summary>
        private EventSystem? _originalEventSystem;

        /// <summary>The event system manually enabled for synchronous EditMode checks, if any.</summary>
        private EventSystem? _manuallyEnabledEventSystem;

        /// <summary>The events received through subscriptions.</summary>
        private readonly List<InputEventArgs> _events = new();

        /// <summary>Installs the fake devices.</summary>
        [SetUp]
        public void SetUp()
        {
            _settings = new SettingsScope();
            _scope = new InputManagerScope();
            _objects = new TestObjects();
            _originalEventSystem = EventSystem.current;
            _manuallyEnabledEventSystem = null;
            _events.Clear();
        }

        /// <summary>Restores the input manager.</summary>
        [TearDown]
        public void TearDown()
        {
            var eventSystems = PrivateMember.GetStaticField<List<EventSystem>>(typeof(EventSystem), "m_EventSystems");
            if (_manuallyEnabledEventSystem != null && eventSystems.Contains(_manuallyEnabledEventSystem))
            {
                UnityMessages.Send(_manuallyEnabledEventSystem, "OnDisable");
            }
            _objects.Dispose();
            if (_originalEventSystem != null && eventSystems.Contains(_originalEventSystem))
            {
                EventSystem.current = _originalEventSystem;
            }
            _scope.Dispose();
            _settings.Dispose();
        }

        /// <summary>Checks that a pressed button is published, raises events and is reported as clicked once.</summary>
        [Test]
        public void ButtonPress_UpdatesFramesAndRaisesEvents()
        {
            InputManager.BindAnyArea(Record);
            InputManager.BindButton(Record, ButtonZone.A2);
            _scope.Device.Buttons[1] = true;

            InputManager.OnPreUpdate();

            Assert.That(_events, Has.Count.EqualTo(2), "button subscriber and any-area subscriber");
            Assert.That(_events[0].IsButton && _events[0].IsDown && _events[0].BZone == ButtonZone.A2, Is.True);
            Assert.That(InputManager.CheckButtonStatus(ButtonZone.A2, SwitchStatus.On), Is.True);
            Assert.That(InputManager.IsButtonClickedInThisFrame(ButtonZone.A2), Is.True);
            Assert.That(InputManager.IsButtonPressedInThisFrame(ButtonZone.A2), Is.True);
            Assert.That(InputManager.GetButtonStatusInThisFrame(ButtonZone.A2), Is.EqualTo(SwitchStatus.On));
            Assert.That(InputManager.GetButtonStatusInPreviousFrame(ButtonZone.A2), Is.EqualTo(SwitchStatus.Off));
            Assert.That(InputManager.CheckButtonStatusInThisFrame(ButtonZone.A2, SwitchStatus.On), Is.True);
            Assert.That(InputManager.CheckButtonStatusInPreviousFrame(ButtonZone.A2, SwitchStatus.Off), Is.True);
            Assert.That(InputManager.ButtonStatusInThisFrame[1], Is.EqualTo(SwitchStatus.On));
            Assert.That(InputManager.ButtonStatusInPreviousFrame[1], Is.EqualTo(SwitchStatus.Off));
            Assert.That(InputManager.CheckAreaStatus(ButtonZone.A2, SwitchStatus.On), Is.True);

            InputManager.OnPreUpdate();
            Assert.That(InputManager.IsButtonClickedInThisFrame(ButtonZone.A2), Is.False, "held, not clicked again");
            Assert.That(_events, Has.Count.EqualTo(2), "no event without a transition");

            _scope.Device.Buttons[1] = false;
            InputManager.OnPreUpdate();
            Assert.That(_events[^1].IsUp, Is.True);
            Assert.That(InputManager.GetButtonState(ButtonZone.A2).ReleasedThisFrame, Is.True);
        }

        /// <summary>Checks that a press edge without a held state still turns the button on for the frame.</summary>
        [Test]
        public void ButtonPulse_IsSeenThroughHadOn()
        {
            _scope.Device.ButtonHadOn[9] = true;

            InputManager.OnPreUpdate();

            Assert.That(InputManager.CheckButtonStatus(ButtonZone.P1, SwitchStatus.On), Is.True);
        }

        /// <summary>Checks that the combined click queries for several buttons.</summary>
        [Test]
        public void ButtonClickQueries_CombineSeveralButtons()
        {
            _scope.Device.Buttons[0] = true;
            _scope.Device.Buttons[1] = true;

            InputManager.OnPreUpdate();

            Assert.That(InputManager.IsButtonClickedInThisFrame_OR(ButtonZone.A3, ButtonZone.A1), Is.True);
            Assert.That(InputManager.IsButtonClickedInThisFrame_OR(ButtonZone.A3, ButtonZone.A4), Is.False);
            Assert.That(InputManager.IsButtonClickedInThisFrame_AND(ButtonZone.A1, ButtonZone.A2), Is.True);
            Assert.That(InputManager.IsButtonClickedInThisFrame_AND(ButtonZone.A1, ButtonZone.A3), Is.False);
            Assert.That(InputManager.IsButtonClickedInThisFrame_OR(Array.Empty<ButtonZone>()), Is.False);
            Assert.That(InputManager.IsButtonClickedInThisFrame_AND(Array.Empty<ButtonZone>()), Is.True);
        }

        /// <summary>Checks that the two C sensor slots merge and later slots shift down by one.</summary>
        [Test]
        public void SensorSlots_MergeCAndShiftLaterSlots()
        {
            InputManager.BindSensor(Record, SensorArea.C);
            InputManager.BindSensor(Record, SensorArea.D1);
            _scope.Device.Sensors[17] = true;
            _scope.Device.Sensors[18] = true;
            _scope.Device.Sensors[34] = true;

            InputManager.OnPreUpdate();

            Assert.That(InputManager.CheckSensorStatus(SensorArea.C, SwitchStatus.On), Is.True);
            Assert.That(InputManager.CheckSensorStatus(SensorArea.D1, SwitchStatus.On), Is.True);
            Assert.That(InputManager.CheckSensorStatus(SensorArea.E8, SwitchStatus.Off), Is.True, "slot 34 is unused");
            Assert.That(_events, Has.Count.EqualTo(2));
            Assert.That(_events.TrueForAll(x => !x.IsButton && x.IsDown), Is.True);
            Assert.That(InputManager.TouchPanelRawData[17], Is.True);
            Assert.That(InputManager.GetTouchPanelRawData().Span[18], Is.True);
            Assert.That(InputManager.SensorStatusInThisFrame[(int)SensorArea.C], Is.EqualTo(SwitchStatus.On));
            Assert.That(InputManager.SensorStatusInPreviousFrame[(int)SensorArea.C], Is.EqualTo(SwitchStatus.Off));
        }

        /// <summary>Checks the sensor frame queries for press and release transitions.</summary>
        [Test]
        public void SensorFrameQueries_FollowTransitions()
        {
            _scope.Device.Sensors[0] = true;
            _scope.Device.Sensors[8] = true;
            InputManager.OnPreUpdate();

            Assert.That(InputManager.IsSensorClickedInThisFrame(SensorArea.A1), Is.True);
            Assert.That(InputManager.IsSensorPressedInThisFrame(SensorArea.A1), Is.True);
            Assert.That(InputManager.IsSensorClickedInThisFrame_OR(SensorArea.C, SensorArea.B1), Is.True);
            Assert.That(InputManager.IsSensorClickedInThisFrame_OR(SensorArea.C, SensorArea.B2), Is.False);
            Assert.That(InputManager.IsSensorClickedInThisFrame_AND(SensorArea.A1, SensorArea.B1), Is.True);
            Assert.That(InputManager.IsSensorClickedInThisFrame_AND(SensorArea.A1, SensorArea.B2), Is.False);
            Assert.That(InputManager.GetSensorStatusInThisFrame(SensorArea.A1), Is.EqualTo(SwitchStatus.On));
            Assert.That(InputManager.CheckSensorStatusInThisFrame(SensorArea.A1, SwitchStatus.On), Is.True);

            _scope.Device.Sensors[0] = false;
            InputManager.OnPreUpdate();

            Assert.That(InputManager.IsSensorClickedUpInThisFrame(SensorArea.A1), Is.True);
            Assert.That(InputManager.GetSensorStatusInPreviousFrame(SensorArea.A1), Is.EqualTo(SwitchStatus.On));
            Assert.That(InputManager.CheckSensorStatusInPreviousFrame(SensorArea.A1, SwitchStatus.On), Is.True);
            Assert.That(InputManager.GetSensorState(SensorArea.A1).ReleasedThisFrame, Is.True);
            Assert.That(InputManager.IsSensorClickCompletedInThisFrame(SensorArea.A1), Is.False);
            Assert.That(InputManager.IsButtonClickCompletedInThisFrame(ButtonZone.A1), Is.False);
        }

        /// <summary>Checks that area subscriptions receive both the sensor and the button of an area until unbound.</summary>
        [Test]
        public void BindArea_ReceivesSensorAndButtonUntilUnbound()
        {
            InputManager.BindArea(Record, ButtonZone.A5);
            _scope.Device.Buttons[4] = true;
            _scope.Device.Sensors[4] = true;
            InputManager.OnPreUpdate();
            Assert.That(_events, Has.Count.EqualTo(2));

            InputManager.UnbindArea(Record, ButtonZone.A5);
            _scope.Device.Buttons[4] = false;
            _scope.Device.Sensors[4] = false;
            InputManager.OnPreUpdate();

            Assert.That(_events, Has.Count.EqualTo(2));
        }

        /// <summary>Checks that every unbind method stops delivery and clearing removes every subscriber.</summary>
        [Test]
        public void UnbindAndClear_StopDelivery()
        {
            InputManager.BindButton(Record, ButtonZone.A1);
            InputManager.BindSensor(Record, SensorArea.A1);
            InputManager.BindAnyArea(Record);
            InputManager.UnbindButton(Record, ButtonZone.A1);
            InputManager.UnbindSensor(Record, SensorArea.A1);
            InputManager.UnbindAnyArea(Record);
            InputManager.UnbindButton(Record, ButtonZone.A1);
            InputManager.UnbindSensor(Record, SensorArea.A1);
            _scope.Device.Buttons[0] = true;
            _scope.Device.Sensors[0] = true;
            InputManager.OnPreUpdate();
            Assert.That(_events, Is.Empty);

            InputManager.BindAnyArea(Record);
            InputManager.BindArea(Record, ButtonZone.A1);
            InputManager.ClearAllSubscriber();
            _scope.Device.Buttons[0] = false;
            InputManager.OnPreUpdate();
            Assert.That(_events, Is.Empty);
        }

        /// <summary>Checks that transitions inside the debounce threshold are ignored.</summary>
        [Test]
        public void Debounce_IgnoresTransitionsInsideTheThreshold()
        {
            InputManagerScope.SetDebounce(true, TimeSpan.FromSeconds(1));
            _scope.Device.Buttons[0] = true;
            _scope.Device.Sensors[0] = true;
            InputManager.OnPreUpdate();
            Assert.That(InputManager.CheckButtonStatus(ButtonZone.A1, SwitchStatus.On), Is.True);
            Assert.That(InputManager.CheckSensorStatus(SensorArea.A1, SwitchStatus.On), Is.True);

            _scope.Device.Buttons[0] = false;
            _scope.Device.Sensors[0] = false;
            _scope.Clock.Advance(TimeSpan.FromMilliseconds(100));
            InputManager.OnPreUpdate();
            Assert.That(InputManager.CheckButtonStatus(ButtonZone.A1, SwitchStatus.On), Is.True, "jitter is ignored");
            Assert.That(InputManager.CheckSensorStatus(SensorArea.A1, SwitchStatus.On), Is.True, "jitter is ignored");

            _scope.Clock.Advance(TimeSpan.FromSeconds(2));
            InputManager.OnPreUpdate();
            Assert.That(InputManager.CheckButtonStatus(ButtonZone.A1, SwitchStatus.Off), Is.True);
            Assert.That(InputManager.CheckSensorStatus(SensorArea.A1, SwitchStatus.Off), Is.True);
        }

        /// <summary>Checks that a scene change suppresses held controls until they are released.</summary>
        [Test]
        public void ResetUIInputForSceneChange_SuppressesHeldControls()
        {
            _scope.Device.Buttons[2] = true;
            InputManager.OnPreUpdate();

            InputManager.ResetUIInputForSceneChange();
            InputManager.OnPreUpdate();

            Assert.That(InputManager.GetButtonState(ButtonZone.A3).IsPressed, Is.False);
        }

        /// <summary>Checks the connection state reported from the installed devices.</summary>
        [Test]
        public void ConnectionState_FollowsTheDevices()
        {
            Assert.That(InputManager.IsButtonRingConnected, Is.False);
            Assert.That(InputManager.IsTouchPanelConnected, Is.False);

            _scope.Device.IsConnected = true;

            Assert.That(InputManager.IsButtonRingConnected, Is.True);
            Assert.That(InputManager.IsTouchPanelConnected, Is.True);
        }

        /// <summary>Checks that out of range zones and areas are rejected by every query.</summary>
        [Test]
        public void Queries_RejectOutOfRangeValues()
        {
            var badZone = (ButtonZone)12;
            var badArea = (SensorArea)33;

            Assert.Throws<ArgumentOutOfRangeException>(() => InputManager.GetButtonState(badZone));
            Assert.Throws<ArgumentOutOfRangeException>(() => InputManager.GetSensorState(badArea));
            Assert.Throws<ArgumentOutOfRangeException>(() => InputManager.CheckButtonStatus(badZone, SwitchStatus.On));
            Assert.Throws<ArgumentOutOfRangeException>(() => InputManager.CheckSensorStatus(badArea, SwitchStatus.On));
            Assert.Throws<ArgumentOutOfRangeException>(() => InputManager.CheckButtonStatusInThisFrame(badZone, SwitchStatus.On));
            Assert.Throws<ArgumentOutOfRangeException>(() => InputManager.CheckButtonStatusInPreviousFrame(badZone, SwitchStatus.On));
            Assert.Throws<ArgumentOutOfRangeException>(() => InputManager.GetButtonStatusInThisFrame(badZone));
            Assert.Throws<ArgumentOutOfRangeException>(() => InputManager.GetButtonStatusInPreviousFrame(badZone));
            Assert.Throws<ArgumentOutOfRangeException>(() => InputManager.IsButtonClickedInThisFrame(badZone));
            Assert.Throws<ArgumentOutOfRangeException>(() => InputManager.IsButtonClickedInThisFrame_OR(badZone));
            Assert.Throws<ArgumentOutOfRangeException>(() => InputManager.IsButtonClickedInThisFrame_AND(badZone));
            Assert.Throws<ArgumentOutOfRangeException>(() => InputManager.CheckSensorStatusInThisFrame(badArea, SwitchStatus.On));
            Assert.Throws<ArgumentOutOfRangeException>(() => InputManager.CheckSensorStatusInPreviousFrame(badArea, SwitchStatus.On));
            Assert.Throws<ArgumentOutOfRangeException>(() => InputManager.GetSensorStatusInThisFrame(badArea));
            Assert.Throws<ArgumentOutOfRangeException>(() => InputManager.GetSensorStatusInPreviousFrame(badArea));
            Assert.Throws<ArgumentOutOfRangeException>(() => InputManager.IsSensorClickedUpInThisFrame(badArea));
            Assert.Throws<ArgumentOutOfRangeException>(() => InputManager.IsSensorClickedInThisFrame(badArea));
            Assert.Throws<ArgumentOutOfRangeException>(() => InputManager.IsSensorClickedInThisFrame_OR(badArea));
            Assert.Throws<ArgumentOutOfRangeException>(() => InputManager.IsSensorClickedInThisFrame_AND(badArea));
            Assert.Throws<ArgumentOutOfRangeException>(() => InputManager.BindButton(Record, badZone));
        }

        /// <summary>Checks the mapping from collider instance ids to sensor areas.</summary>
        [Test]
        public void GetSensorAreaFromInstanceID_MapsColliderIndices()
        {
            var mapping = new Dictionary<int, int> { [10] = 3, [11] = 16, [12] = 17, [13] = 20, [14] = 40 };
            PrivateMember.SetStaticField(typeof(InputManager), "_instanceID2SensorIndexMappingTable", mapping);

            Assert.That(InputManager.GetSensorAreaFromInstanceID(10), Is.EqualTo(SensorArea.A4));
            Assert.That(InputManager.GetSensorAreaFromInstanceID(11), Is.EqualTo(SensorArea.C));
            Assert.That(InputManager.GetSensorAreaFromInstanceID(12), Is.EqualTo(SensorArea.C));
            Assert.That(InputManager.GetSensorAreaFromInstanceID(13), Is.EqualTo(SensorArea.D3));
            Assert.Throws<ArgumentOutOfRangeException>(() => InputManager.GetSensorAreaFromInstanceID(14));
            Assert.Throws<InvalidOperationException>(() => InputManager.GetSensorAreaFromInstanceID(99));
        }

        /// <summary>Checks that the private sensor setter only raises events for real transitions.</summary>
        [Test]
        public void SetSensorState_RaisesEventsOnlyForTransitions()
        {
            InputManager.BindAnyArea(Record);

            PrivateMember.InvokeStatic(typeof(InputManager), "SetSensorState", SensorArea.B3, SwitchStatus.On);
            PrivateMember.InvokeStatic(typeof(InputManager), "SetSensorState", SensorArea.B3, SwitchStatus.On);

            Assert.That(_events, Has.Count.EqualTo(1));
            Assert.That(_events[0].SArea, Is.EqualTo(SensorArea.B3));
            Assert.That(InputManager.CheckSensorStatus(SensorArea.B3, SwitchStatus.On), Is.True);
        }

        /// <summary>Checks the per-index and per-zone queries of the button ring facade.</summary>
        [Test]
        public void ButtonRingFacade_Queries()
        {
            var type = InputManagerScope.ButtonRingType;
            _scope.Device.Buttons[3] = true;
            PrivateMember.InvokeStatic(type, "OnPreUpdate");

            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsOn", 3), Is.True);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsOn", ButtonZone.A4), Is.True);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsOff", ButtonZone.A4), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsOff", 2), Is.True);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsHadOn", ButtonZone.A4), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsHadOff", ButtonZone.A4), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsCurrentlyOn", ButtonZone.A4), Is.True);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsCurrentlyOff", ButtonZone.A4), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsCurrentlyOff", 0), Is.True);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsOn", 12), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsHadOn", -1), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsHadOff", 12), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsCurrentlyOn", 12), Is.False);
            var error = Assert.Throws<TargetInvocationException>(() => PrivateMember.InvokeStatic(type, "IsOn", (ButtonZone)20));
            Assert.That(error.InnerException, Is.TypeOf<ArgumentOutOfRangeException>());
        }

        /// <summary>Checks that a typing key stays blocked after focus loss until release and a new press.</summary>
        [Test]
        public void KeyboardTextInput_HeldKeyRemainsSuppressedUntilRelease()
        {
            var input = FocusTextInput();
            var type = InputManagerScope.ButtonRingType;
            _scope.Device.Buttons[0] = true;
            _scope.Device.ButtonHadOn[0] = true;

            ReadKeyboardButtons();

            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsOn", 0), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsHadOn", 0), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsHadOff", 0), Is.True);
            input.DeactivateInputField();
            Assert.That(InputManager.SuppressKeyboardGameButtons, Is.False);

            ReadKeyboardButtons();

            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsOn", 0), Is.False, "held after focus loss");
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsHadOn", 0), Is.False);
            _scope.Device.Buttons[0] = false;
            _scope.Device.ButtonHadOn[0] = false;
            ReadKeyboardButtons();
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsHadOff", 0), Is.True);

            _scope.Device.Buttons[0] = true;
            _scope.Device.ButtonHadOn[0] = true;
            ReadKeyboardButtons();

            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsOn", 0), Is.True, "new press after release");
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsHadOn", 0), Is.True);
        }

        /// <summary>Checks that a cabinet device keeps publishing game buttons while a text field is focused.</summary>
        [Test]
        public void KeyboardTextInput_CabinetDeviceRemainsAvailable()
        {
            FocusTextInput();
            _scope.Device.Buttons[0] = true;
            _scope.Device.ButtonHadOn[7] = true;

            InputManager.OnPreUpdate();

            Assert.That(InputManager.CheckButtonStatus(ButtonZone.A1, SwitchStatus.On), Is.True);
            Assert.That(InputManager.CheckButtonStatus(ButtonZone.A8, SwitchStatus.On), Is.True);
        }

        /// <summary>Checks that text input leaves all four keyboard function buttons available.</summary>
        [Test]
        public void KeyboardTextInput_FunctionKeysRemainAvailable()
        {
            FocusTextInput();
            var type = InputManagerScope.ButtonRingType;
            for (var i = 8; i < 12; i++)
            {
                _scope.Device.Buttons[i] = true;
                _scope.Device.ButtonHadOn[i] = true;
            }

            ReadKeyboardButtons();

            for (var i = 8; i < 12; i++)
            {
                Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsOn", i), Is.True, $"function button {i}");
                Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsHadOn", i), Is.True, $"function edge {i}");
            }
        }

        /// <summary>Checks that a typing pulse between snapshots is suppressed and later pulses recover.</summary>
        [Test]
        public void KeyboardTextInput_ShortPressIsSuppressedThroughHadOn()
        {
            var input = FocusTextInput();
            var type = InputManagerScope.ButtonRingType;
            _scope.Device.ButtonHadOn[2] = true;

            ReadKeyboardButtons();

            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsOn", 2), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsHadOn", 2), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsHadOff", 2), Is.True);

            input.DeactivateInputField();
            _scope.Device.ButtonHadOn[2] = false;
            ReadKeyboardButtons();
            _scope.Device.ButtonHadOn[2] = true;
            ReadKeyboardButtons();

            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsHadOn", 2), Is.True, "new pulse after release");
        }

        /// <summary>Checks the per-index and per-area queries of the touch panel facade, including the merged C area.</summary>
        [Test]
        public void TouchPanelFacade_Queries()
        {
            var type = InputManagerScope.TouchPanelType;
            _scope.Device.Sensors[17] = true;
            _scope.Device.Sensors[19] = true;
            _scope.Device.SensorHadOn[16] = true;
            PrivateMember.InvokeStatic(type, "OnPreUpdate");

            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsOn", SensorArea.C), Is.True);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsOn", SensorArea.D2), Is.True);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsOn", SensorArea.A1), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsOff", SensorArea.A1), Is.True);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsOn", (SensorArea)40), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsHadOn", SensorArea.C), Is.True);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsHadOn", SensorArea.A2), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsHadOn", SensorArea.E1), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsHadOn", (SensorArea)40), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsHadOff", SensorArea.C), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsHadOff", SensorArea.A2), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsHadOff", SensorArea.E1), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsHadOff", (SensorArea)40), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsCurrentlyOn", SensorArea.C), Is.True);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsCurrentlyOn", SensorArea.A1), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsCurrentlyOn", SensorArea.D2), Is.True);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsCurrentlyOn", (SensorArea)40), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsCurrentlyOff", SensorArea.D2), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsCurrentlyOff", 0), Is.True);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsCurrentlyOn", 40), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsOn", 40), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsOff", 17), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsHadOn", 40), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "IsHadOff", 40), Is.False);
        }

        /// <summary>Checks that the touch tuning properties forward to the screen touch mapper.</summary>
        [Test]
        public void TouchTuningProperties_ForwardToTheMapper()
        {
            var saved = (InputManager.Override_TouchSimulationRadius, InputManager.Override_TouchAAreaExtraRadius,
                InputManager.Override_TouchBAreaExtraRadius, InputManager.Override_TouchCAreaExtraRadius,
                InputManager.Override_TouchDAreaExtraRadius, InputManager.Override_TouchEAreaExtraRadius,
                InputManager.UseOuterTouchAsSensor, InputManager.UseGameplayTouchEnhancementFeatures,
                InputManager.TouchButtonRingEdge, InputManager.SubScreenEdge);
            try
            {
                InputManager.Override_TouchSimulationRadius = 0.7f;
                InputManager.Override_TouchAAreaExtraRadius = 0.1f;
                InputManager.Override_TouchBAreaExtraRadius = 0.2f;
                InputManager.Override_TouchCAreaExtraRadius = 0.3f;
                InputManager.Override_TouchDAreaExtraRadius = 0.4f;
                InputManager.Override_TouchEAreaExtraRadius = null;
                InputManager.UseOuterTouchAsSensor = true;
                InputManager.UseGameplayTouchEnhancementFeatures = true;
                InputManager.TouchButtonRingEdge = 4.5f;
                InputManager.SubScreenEdge = new Vector4(1, 2, 3, 4);
                _settings.Settings.Debug.TouchSimulationRadius = 0.9f;

                Assert.That(ScreenTouchMapper.Override_TouchSimulationRadius, Is.EqualTo(0.7f));
                Assert.That(InputManager.Override_TouchAAreaExtraRadius, Is.EqualTo(0.1f));
                Assert.That(InputManager.Override_TouchBAreaExtraRadius, Is.EqualTo(0.2f));
                Assert.That(InputManager.Override_TouchCAreaExtraRadius, Is.EqualTo(0.3f));
                Assert.That(InputManager.Override_TouchDAreaExtraRadius, Is.EqualTo(0.4f));
                Assert.That(InputManager.Override_TouchEAreaExtraRadius, Is.Null);
                Assert.That(ScreenTouchMapper.UseOuterTouchAsSensor, Is.True);
                Assert.That(ScreenTouchMapper.UseGameplayTouchEnhancementFeatures, Is.True);
                Assert.That(ScreenTouchMapper.TouchButtonRingEdge, Is.EqualTo(4.5f));
                Assert.That(ScreenTouchMapper.SubScreenEdge, Is.EqualTo(new Vector4(1, 2, 3, 4)));
                Assert.That(InputManager.FingerRadius, Is.EqualTo(0.9f));
                Assert.That(InputManager.TOUCH_ANGLE_SMAPLE_COUNT, Is.EqualTo(ScreenTouchMapper.TOUCH_ANGLE_SMAPLE_COUNT));
                Assert.That(InputManager.FINGER_RADIUS_SEGMENT_LENGTH, Is.EqualTo(ScreenTouchMapper.FINGER_RADIUS_SEGMENT_LENGTH));
                Assert.That(InputManager.UnitCircle.Length, Is.EqualTo(ScreenTouchMapper.UnitCircle.Length));
                Assert.That(InputManager.TouchPanelPositionSamples.Length, Is.EqualTo(ScreenTouchMapper.TouchPanelPositionSamples.Length));
            }
            finally
            {
                InputManager.Override_TouchSimulationRadius = saved.Item1;
                InputManager.Override_TouchAAreaExtraRadius = saved.Item2;
                InputManager.Override_TouchBAreaExtraRadius = saved.Item3;
                InputManager.Override_TouchCAreaExtraRadius = saved.Item4;
                InputManager.Override_TouchDAreaExtraRadius = saved.Item5;
                InputManager.Override_TouchEAreaExtraRadius = saved.Item6;
                InputManager.UseOuterTouchAsSensor = saved.Item7;
                InputManager.UseGameplayTouchEnhancementFeatures = saved.Item8;
                InputManager.TouchButtonRingEdge = saved.Item9;
                InputManager.SubScreenEdge = saved.Item10;
            }
        }

        /// <summary>Creates and selects an active text field with its focused state set for synchronous EditMode checks.</summary>
        /// <returns>The focused input field.</returns>
        private TMP_InputField FocusTextInput()
        {
            var eventSystem = _objects.Add<EventSystem>();
            var eventSystems = PrivateMember.GetStaticField<List<EventSystem>>(typeof(EventSystem), "m_EventSystems");
            if (!eventSystems.Contains(eventSystem))
            {
                _manuallyEnabledEventSystem = eventSystem;
                UnityMessages.Send(eventSystem, "OnEnable");
            }
            EventSystem.current = eventSystem;
            var inputObject = _objects.Create("Focused text input");
            inputObject.AddComponent<RectTransform>();
            var input = inputObject.AddComponent<TMP_InputField>();
            eventSystem.SetSelectedGameObject(inputObject);
            // TMP normally sets this during its next player update, which EditMode does not run.
            PrivateMember.SetField(input, "m_AllowInput", true);
            Assert.That(InputManager.SuppressKeyboardGameButtons, Is.True);
            return input;
        }

        /// <summary>Reads the scripted snapshot and applies the keyboard filter without a native keyboard reader.</summary>
        private static void ReadKeyboardButtons()
        {
            var type = InputManagerScope.ButtonRingType;
            PrivateMember.InvokeStatic(type, "OnPreUpdate");
            PrivateMember.InvokeStatic(type, "FilterTextInputKeys", true);
        }

        /// <summary>Records an input event.</summary>
        /// <param name="sender">The publisher.</param>
        /// <param name="args">The event.</param>
        private void Record(object? sender, InputEventArgs args)
        {
            _events.Add(args);
        }
    }
}
