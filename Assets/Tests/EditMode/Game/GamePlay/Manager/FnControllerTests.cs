using System;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Settings;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.GamePlay.PlayMode.Support;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Manager
{
    /// <summary>Checks rotated shortcut mappings, input arming, hold thresholds and action priority.</summary>
    public class FnControllerTests
    {
        /// <summary>Tracks the gameplay objects created by each test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Restores game settings and timeline values after each test.</summary>
        private GamePlaySettingsScope _settings = null!;

        /// <summary>The existing published button controls, whose state is changed without polling devices.</summary>
        private object[] _buttons = Array.Empty<object>();

        /// <summary>The existing published sensor controls, whose state is changed without polling devices.</summary>
        private object[] _sensors = Array.Empty<object>();

        /// <summary>The published button states restored after each test.</summary>
        private SwitchStatus[] _previousButtonStates = Array.Empty<SwitchStatus>();

        /// <summary>The published sensor states restored after each test.</summary>
        private SwitchStatus[] _previousSensorStates = Array.Empty<SwitchStatus>();

        /// <summary>The gameplay state observed by the controller.</summary>
        private GamePlayManager _manager = null!;

        /// <summary>The shortcut controller under test.</summary>
        private FnController _controller = null!;

        /// <summary>Creates a controller with every combination enabled and a quarter-second frame duration.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            _settings = new GamePlaySettingsScope();
            _buttons = GetInputControls("_buttons");
            _sensors = GetInputControls("_sensors");
            _previousButtonStates = CaptureControlStates(_buttons);
            _previousSensorStates = CaptureControlStates(_sensors);
            foreach (var control in _buttons)
            {
                PrivateMember.SetProperty(control, "State", SwitchStatus.Off);
            }
            foreach (var control in _sensors)
            {
                PrivateMember.SetProperty(control, "State", SwitchStatus.Off);
            }
            _settings.Settings.Game.TrackSkip = true;
            _settings.Settings.Game.FastRetry = true;
            _settings.Settings.Game.FastPractice = true;
            _settings.Settings.Game.GameplaySubScreenClickBehavior = GameplaySubScreenClickBehaviorOption.TrackSkip_1_Sec_Delay;
            _settings.SetDeltaTime(0.25f);
            _manager = _objects.Add<GamePlayManager>();
            _controller = _manager.gameObject.AddComponent<FnController>();
            _controller.Init(_manager);
        }

        /// <summary>Destroys the gameplay objects and restores input, settings and timeline values.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects?.Dispose();
            RestoreControlStates(_buttons, _previousButtonStates);
            RestoreControlStates(_sensors, _previousSensorStates);
            _settings?.Dispose();
        }

        /// <summary>Checks the outer and touch mappings for all supported screen rotations.</summary>
        /// <param name="rotation">The gameplay screen rotation.</param>
        /// <param name="trackSkip">The expected track skip keys.</param>
        /// <param name="fastRetry">The expected fast retry keys.</param>
        /// <param name="fastPractice">The expected fast practice keys.</param>
        [TestCase(GameplayScreenRotationAngleOption.Zero, "2367", "3456", "1278")]
        [TestCase(GameplayScreenRotationAngleOption._90, "4581", "5678", "1234")]
        [TestCase(GameplayScreenRotationAngleOption._180, "6723", "7812", "3456")]
        [TestCase(GameplayScreenRotationAngleOption._270, "8145", "1234", "5678")]
        public void Initialize_MapsButtonsAndSensorsForScreenRotation(
            GameplayScreenRotationAngleOption rotation, string trackSkip, string fastRetry, string fastPractice)
        {
            _settings.Settings.Display.GameplayScreenRotationAngle = rotation;
            _controller.Init(_manager);

            AssertKeyMapping("2367", trackSkip);
            AssertKeyMapping("3456", fastRetry);
            AssertKeyMapping("1278", fastPractice);
        }

        /// <summary>Checks that ended gameplay clears every timer and requires another release before arming.</summary>
        [Test]
        public void UpdateInputState_ResetsAfterGameplayEnded()
        {
            SetState(GamePlayStatus.Ended);
            PrivateMember.SetField(_controller, "_2367PressTime", 1f);
            PrivateMember.SetField(_controller, "_3456PressTime", 1f);
            PrivateMember.SetField(_controller, "_1278PressTime", 1f);
            PrivateMember.SetField(_controller, "_p1PressTime", 1f);
            PrivateMember.SetField(_controller, "_isFnKeyInputArmed", true);

            Assert.That(_controller.UpdateInputState(), Is.EqualTo(FnController.FnAction.None));

            AssertPressTimesAreZero();
            Assert.That(PrivateMember.GetField<bool>(_controller, "_isFnKeyInputArmed"), Is.False);
        }

        /// <summary>Checks that gameplay before the end arms shortcuts after a released frame without accumulating holds.</summary>
        /// <param name="state">The gameplay state before the end.</param>
        [TestCase(GamePlayStatus.Start)]
        [TestCase(GamePlayStatus.Loading)]
        [TestCase(GamePlayStatus.Running)]
        [TestCase(GamePlayStatus.Blocking)]
        [TestCase(GamePlayStatus.WaitForEnd)]
        public void UpdateInputState_ArmsAfterTheKeysAreReleased(GamePlayStatus state)
        {
            SetState(state);
            PrivateMember.SetField(_controller, "_2367PressTime", 1f);

            Assert.That(_controller.UpdateInputState(), Is.EqualTo(FnController.FnAction.None));
            Assert.That(PrivateMember.GetField<bool>(_controller, "_isFnKeyInputArmed"), Is.True);
            AssertPressTimesAreZero();

            PrivateMember.SetField(_controller, "_3456PressTime", 0.25f);
            Assert.That(_controller.UpdateInputState(), Is.EqualTo(FnController.FnAction.None));
            AssertPressTimesAreZero();
            Assert.That(_manager.State, Is.EqualTo(state));
        }

        /// <summary>Checks that an inherited hold of any shortcut button prevents arming until it is released.</summary>
        /// <param name="button">The button held while entering active gameplay.</param>
        [TestCase(ButtonZone.A1)]
        [TestCase(ButtonZone.A2)]
        [TestCase(ButtonZone.A3)]
        [TestCase(ButtonZone.A4)]
        [TestCase(ButtonZone.A5)]
        [TestCase(ButtonZone.A6)]
        [TestCase(ButtonZone.A7)]
        [TestCase(ButtonZone.A8)]
        [TestCase(ButtonZone.P1)]
        public void UpdateInputState_WaitsForInheritedButtonHoldToBeReleased(ButtonZone button)
        {
            SetPressedButtons(button);
            _controller.UpdateInputState();
            SetState(GamePlayStatus.Running);

            Assert.That(_controller.UpdateInputState(), Is.EqualTo(FnController.FnAction.None));
            Assert.That(_controller.UpdateInputState(), Is.EqualTo(FnController.FnAction.None));
            Assert.That(PrivateMember.GetField<bool>(_controller, "_isFnKeyInputArmed"), Is.False);
            AssertPressTimesAreZero();

            SetPressedButtons();
            Assert.That(_controller.UpdateInputState(), Is.EqualTo(FnController.FnAction.None));
            Assert.That(PrivateMember.GetField<bool>(_controller, "_isFnKeyInputArmed"), Is.True);
        }

        /// <summary>Checks that all three combinations trigger at half a second and do not execute a scene transition.</summary>
        /// <param name="combination">The logical combination whose mapped buttons are pressed.</param>
        /// <param name="expectedAction">The action selected when the hold threshold is reached.</param>
        [TestCase("2367", FnController.FnAction.TrackSkip)]
        [TestCase("3456", FnController.FnAction.FastRetry)]
        [TestCase("1278", FnController.FnAction.FastPractice)]
        public void UpdateInputState_RequiresHalfSecondForCombinations(string combination, object expectedAction)
        {
            ArmInput();
            SetPressedCombination(combination);

            Assert.That(_controller.UpdateInputState(), Is.EqualTo(FnController.FnAction.None));
            Assert.That(_controller.UpdateInputState(), Is.EqualTo(expectedAction));
            Assert.That(_manager.State, Is.EqualTo(GamePlayStatus.Running));
        }

        /// <summary>Checks that releasing a combination restarts its hold threshold.</summary>
        [Test]
        public void UpdateInputState_ResetsCombinationHoldOnRelease()
        {
            ArmInput();
            SetPressedCombination("2367");
            Assert.That(_controller.UpdateInputState(), Is.EqualTo(FnController.FnAction.None));

            SetPressedButtons();
            Assert.That(_controller.UpdateInputState(), Is.EqualTo(FnController.FnAction.None));
            AssertPressTimesAreZero();

            SetPressedCombination("2367");
            Assert.That(_controller.UpdateInputState(), Is.EqualTo(FnController.FnAction.None));
            Assert.That(_controller.UpdateInputState(), Is.EqualTo(FnController.FnAction.TrackSkip));
        }

        /// <summary>Checks that switching combinations cannot inherit another combination's hold duration.</summary>
        [Test]
        public void UpdateInputState_ResetsPreviousCombinationWhenSwitching()
        {
            ArmInput();
            SetPressedCombination("2367");
            _controller.UpdateInputState();

            SetPressedCombination("3456");
            Assert.That(_controller.UpdateInputState(), Is.EqualTo(FnController.FnAction.None));
            Assert.That(PrivateMember.GetField<float>(_controller, "_2367PressTime"), Is.Zero);
            Assert.That(_controller.UpdateInputState(), Is.EqualTo(FnController.FnAction.FastRetry));
        }

        /// <summary>Checks that settings disable the corresponding combination.</summary>
        /// <param name="combination">The disabled combination whose mapped buttons are pressed.</param>
        [TestCase("2367")]
        [TestCase("3456")]
        [TestCase("1278")]
        public void UpdateInputState_IgnoresDisabledCombinations(string combination)
        {
            _settings.Settings.Game.TrackSkip = false;
            _settings.Settings.Game.FastRetry = false;
            _settings.Settings.Game.FastPractice = false;
            _controller.Init(_manager);
            ArmInput();
            SetPressedCombination(combination);

            for (var i = 0; i < 4; i++)
            {
                Assert.That(_controller.UpdateInputState(), Is.EqualTo(FnController.FnAction.None));
            }
            AssertPressTimesAreZero();
        }

        /// <summary>Checks combination priority when all outer buttons are pressed together.</summary>
        /// <param name="trackSkip">Whether track skip is enabled.</param>
        /// <param name="fastRetry">Whether fast retry is enabled.</param>
        /// <param name="expectedAction">The highest priority enabled action.</param>
        [TestCase(true, true, FnController.FnAction.TrackSkip)]
        [TestCase(false, true, FnController.FnAction.FastRetry)]
        [TestCase(false, false, FnController.FnAction.FastPractice)]
        public void UpdateInputState_PrioritizesEnabledCombinations(bool trackSkip, bool fastRetry, object expectedAction)
        {
            _settings.Settings.Game.TrackSkip = trackSkip;
            _settings.Settings.Game.FastRetry = fastRetry;
            _controller.Init(_manager);
            ArmInput();
            SetPressedButtons(ButtonZone.A1, ButtonZone.A2, ButtonZone.A3, ButtonZone.A4,
                ButtonZone.A5, ButtonZone.A6, ButtonZone.A7, ButtonZone.A8);

            Assert.That(_controller.UpdateInputState(), Is.EqualTo(FnController.FnAction.None));
            Assert.That(_controller.UpdateInputState(), Is.EqualTo(expectedAction));
        }

        /// <summary>Checks that immediate sub-screen actions trigger on the first pressed frame.</summary>
        /// <param name="behavior">The configured immediate sub-screen behavior.</param>
        /// <param name="expectedAction">The selected gameplay action.</param>
        [TestCase(GameplaySubScreenClickBehaviorOption.TrackSkip, FnController.FnAction.QuickTrackSkip)]
        [TestCase(GameplaySubScreenClickBehaviorOption.FastRetry, FnController.FnAction.FastRetry)]
        public void UpdateInputState_TriggersImmediateSubScreenAction(GameplaySubScreenClickBehaviorOption behavior, object expectedAction)
        {
            _settings.Settings.Game.TrackSkip = false;
            _settings.Settings.Game.FastRetry = false;
            _settings.Settings.Game.FastPractice = false;
            _settings.Settings.Game.GameplaySubScreenClickBehavior = behavior;
            _controller.Init(_manager);
            ArmInput();
            SetPressedButtons(ButtonZone.P1);

            Assert.That(_controller.UpdateInputState(), Is.EqualTo(expectedAction));
        }

        /// <summary>Checks the one-second sub-screen threshold and its priority over held combinations.</summary>
        /// <param name="behavior">The configured delayed sub-screen behavior.</param>
        /// <param name="expectedAction">The selected action after one second.</param>
        [TestCase(GameplaySubScreenClickBehaviorOption.TrackSkip_1_Sec_Delay, FnController.FnAction.QuickTrackSkip)]
        [TestCase(GameplaySubScreenClickBehaviorOption.FastRetry_1_Sec_Delay, FnController.FnAction.FastRetry)]
        public void UpdateInputState_PrioritizesDelayedSubScreenAction(GameplaySubScreenClickBehaviorOption behavior, object expectedAction)
        {
            _settings.Settings.Game.GameplaySubScreenClickBehavior = behavior;
            _controller.Init(_manager);
            ArmInput();
            SetPressedButtons(ButtonZone.P1, ButtonZone.A1, ButtonZone.A2, ButtonZone.A3, ButtonZone.A4,
                ButtonZone.A5, ButtonZone.A6, ButtonZone.A7, ButtonZone.A8);

            for (var i = 0; i < 3; i++)
            {
                Assert.That(_controller.UpdateInputState(), Is.EqualTo(FnController.FnAction.None));
            }
            Assert.That(_controller.UpdateInputState(), Is.EqualTo(expectedAction));
        }

        /// <summary>Checks that disabled sub-screen input cannot trigger a combination held alongside it.</summary>
        [Test]
        public void UpdateInputState_DisabledSubScreenDoesNotFallThroughToCombinations()
        {
            _settings.Settings.Game.GameplaySubScreenClickBehavior = GameplaySubScreenClickBehaviorOption.None;
            _controller.Init(_manager);
            ArmInput();
            SetPressedButtons(ButtonZone.P1, ButtonZone.A2, ButtonZone.A3, ButtonZone.A6, ButtonZone.A7);

            for (var i = 0; i < 4; i++)
            {
                Assert.That(_controller.UpdateInputState(), Is.EqualTo(FnController.FnAction.None));
            }
        }

        /// <summary>Checks that touch-only shortcuts follow the same mobile platform restriction as before extraction.</summary>
        [Test]
        public void UpdateInputState_UsesTouchCombinationsOnlyOnMobile()
        {
            ArmInput();
            PrivateMember.SetProperty(_sensors[(int)SensorArea.A2], "State", SwitchStatus.On);
            PrivateMember.SetProperty(_sensors[(int)SensorArea.A3], "State", SwitchStatus.On);
            PrivateMember.SetProperty(_sensors[(int)SensorArea.A6], "State", SwitchStatus.On);
            PrivateMember.SetProperty(_sensors[(int)SensorArea.A7], "State", SwitchStatus.On);

            Assert.That(_controller.UpdateInputState(), Is.EqualTo(FnController.FnAction.None));
#if UNITY_ANDROID || UNITY_IOS
            Assert.That(_controller.UpdateInputState(), Is.EqualTo(FnController.FnAction.TrackSkip));
#else
            Assert.That(_controller.UpdateInputState(), Is.EqualTo(FnController.FnAction.None));
#endif
        }

        /// <summary>Checks that a partial button combination and a partial touch combination are not combined.</summary>
        [Test]
        public void UpdateInputState_RequiresOneCompleteInputSource()
        {
            ArmInput();
            PrivateMember.SetProperty(_sensors[(int)SensorArea.A6], "State", SwitchStatus.On);
            PrivateMember.SetProperty(_sensors[(int)SensorArea.A7], "State", SwitchStatus.On);
            SetPressedButtons(ButtonZone.A2, ButtonZone.A3);

            Assert.That(_controller.UpdateInputState(), Is.EqualTo(FnController.FnAction.None));
            Assert.That(_controller.UpdateInputState(), Is.EqualTo(FnController.FnAction.None));
            AssertPressTimesAreZero();
        }

        /// <summary>Checks that the gameplay update entry point advances its shortcut controller after the one-second start-up wait.</summary>
        [Test]
        public void GamePlayManagerOnUpdate_UpdatesTheShortcutControllerAfterStartupWait()
        {
            PrivateMember.SetField(_manager, "_fnController", _controller);
            SetState(GamePlayStatus.Ended);
            PrivateMember.SetField(_controller, "_2367PressTime", 1f);
            PrivateMember.SetField(_controller, "_isFnKeyInputArmed", true);

            // Four quarter-second frames only fill the one-second wait.
            for (var i = 0; i < 4; i++)
            {
                _manager.OnUpdate();
            }
            Assert.That(PrivateMember.GetField<float>(_controller, "_2367PressTime"), Is.EqualTo(1f));
            Assert.That(PrivateMember.GetField<bool>(_controller, "_isFnKeyInputArmed"), Is.True);

            _manager.OnUpdate();

            AssertPressTimesAreZero();
            Assert.That(PrivateMember.GetField<bool>(_controller, "_isFnKeyInputArmed"), Is.False);
        }

        /// <summary>Checks the expected physical button and sensor numbers for one logical combination.</summary>
        /// <param name="combination">The logical combination suffix used by the controller's mappings.</param>
        /// <param name="keys">The expected physical key numbers, in mapping order.</param>
        private void AssertKeyMapping(string combination, string keys)
        {
            var buttons = PrivateMember.GetField<ButtonZone[]>(_controller, "_buttonKeyFor" + combination);
            var sensors = PrivateMember.GetField<SensorArea[]>(_controller, "_sensorAreaFor" + combination);
            for (var i = 0; i < keys.Length; i++)
            {
                var index = keys[i] - '1';
                Assert.That(buttons[i], Is.EqualTo((ButtonZone)index));
                Assert.That(sensors[i], Is.EqualTo((SensorArea)index));
            }
        }

        /// <summary>Enters running gameplay and supplies the released frame required to arm shortcut input.</summary>
        private void ArmInput()
        {
            SetState(GamePlayStatus.Running);
            Assert.That(_controller.UpdateInputState(), Is.EqualTo(FnController.FnAction.None));
            Assert.That(PrivateMember.GetField<bool>(_controller, "_isFnKeyInputArmed"), Is.True);
        }

        /// <summary>Sets the gameplay state without invoking scene lifecycle methods.</summary>
        /// <param name="state">The state to report to the controller.</param>
        private void SetState(GamePlayStatus state)
        {
            PrivateMember.SetProperty(_manager, nameof(GamePlayManager.State), state);
        }

        /// <summary>Publishes a new frame with only the specified outer buttons held.</summary>
        /// <param name="buttons">The buttons to hold for the next input frame.</param>
        private void SetPressedButtons(params ButtonZone[] buttons)
        {
            foreach (var control in _buttons)
            {
                PrivateMember.SetProperty(control, "State", SwitchStatus.Off);
            }
            foreach (var button in buttons)
            {
                PrivateMember.SetProperty(_buttons[(int)button], "State", SwitchStatus.On);
            }
        }

        /// <summary>Publishes a frame holding the outer buttons mapped to one logical combination.</summary>
        /// <param name="combination">The logical combination suffix used by the controller's mappings.</param>
        private void SetPressedCombination(string combination)
        {
            SetPressedButtons(PrivateMember.GetField<ButtonZone[]>(_controller, "_buttonKeyFor" + combination));
        }

        /// <summary>Gets the existing control objects stored in an input manager memory field.</summary>
        /// <param name="fieldName">The input manager field that contains the controls.</param>
        /// <returns>An array referring to the existing controls without replacing the readonly field.</returns>
        private static object[] GetInputControls(string fieldName)
        {
            var memory = PrivateMember.GetStaticField<object>(typeof(InputManager), fieldName);
            return (object[])memory.GetType().GetMethod("ToArray")!.Invoke(memory, null)!;
        }

        /// <summary>Captures the current published states of the specified controls.</summary>
        /// <param name="controls">The button or sensor controls whose states are captured.</param>
        /// <returns>The original published state of each control.</returns>
        private static SwitchStatus[] CaptureControlStates(object[] controls)
        {
            var states = new SwitchStatus[controls.Length];
            for (var i = 0; i < controls.Length; i++)
            {
                states[i] = PrivateMember.GetProperty<SwitchStatus>(controls[i], "State");
            }
            return states;
        }

        /// <summary>Restores published control states without raising events or changing device state.</summary>
        /// <param name="controls">The existing button or sensor controls.</param>
        /// <param name="states">The original published state of each control.</param>
        private static void RestoreControlStates(object[] controls, SwitchStatus[] states)
        {
            for (var i = 0; i < states.Length; i++)
            {
                PrivateMember.SetProperty(controls[i], "State", states[i]);
            }
        }

        /// <summary>Asserts that all combination and sub-screen hold timers are zero.</summary>
        private void AssertPressTimesAreZero()
        {
            Assert.That(PrivateMember.GetField<float>(_controller, "_2367PressTime"), Is.Zero);
            Assert.That(PrivateMember.GetField<float>(_controller, "_3456PressTime"), Is.Zero);
            Assert.That(PrivateMember.GetField<float>(_controller, "_1278PressTime"), Is.Zero);
            Assert.That(PrivateMember.GetField<float>(_controller, "_p1PressTime"), Is.Zero);
        }
    }
}
