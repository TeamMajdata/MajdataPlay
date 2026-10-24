using Cysharp.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO;
using MajdataPlay.Settings;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Scenes.Game
{
    /// <summary>Recognizes gameplay shortcuts and requests their actions from the gameplay manager.</summary>
    [DisallowMultipleComponent]
    internal sealed class FnController : MajBehaviour
    {
        private const int InitWaitTimeSec = 1;

        /// <summary>Whether the track skip shortcut is enabled.</summary>
        private bool _isTrackSkipAvailable;

        /// <summary>Whether the fast retry shortcut is enabled.</summary>
        private bool _isFastRetryAvailable;

        /// <summary>Whether the fast practice shortcut is enabled.</summary>
        private bool _isFastPracticeAvailable;

        /// <summary>The accumulated hold time of the track skip combination.</summary>
        private float _2367PressTime;

        /// <summary>The accumulated hold time of the fast retry combination.</summary>
        private float _3456PressTime;

        /// <summary>The accumulated hold time of the fast practice combination.</summary>
        private float _1278PressTime;

        /// <summary>The accumulated hold time of the sub-screen button.</summary>
        private float _p1PressTime;

        /// <summary>Whether every shortcut input has been released since gameplay became active.</summary>
        private bool _isFnKeyInputArmed;

        private float _initWaitTimer = 0;

        /// <summary>The rotated outer buttons for the track skip combination.</summary>
        private readonly ButtonZone[] _buttonKeyFor2367 = new ButtonZone[4];

        /// <summary>The rotated outer buttons for the fast retry combination.</summary>
        private readonly ButtonZone[] _buttonKeyFor3456 = new ButtonZone[4];

        /// <summary>The rotated outer buttons for the fast practice combination.</summary>
        private readonly ButtonZone[] _buttonKeyFor1278 = new ButtonZone[4];

        /// <summary>The rotated touch sensors for the track skip combination.</summary>
        private readonly SensorArea[] _sensorAreaFor2367 = new SensorArea[4];

        /// <summary>The rotated touch sensors for the fast retry combination.</summary>
        private readonly SensorArea[] _sensorAreaFor3456 = new SensorArea[4];

        /// <summary>The rotated touch sensors for the fast practice combination.</summary>
        private readonly SensorArea[] _sensorAreaFor1278 = new SensorArea[4];

        /// <summary>The action and hold delay configured for the sub-screen button.</summary>
        private GameplaySubScreenClickBehaviorOption _gameplaySubScreenClickBehavior;

        /// <summary>The manager that executes gameplay transitions requested by shortcuts.</summary>
        private GamePlayManager _gpManager = null!;

        /// <summary>Loads shortcut settings and maps inputs to the current screen rotation.</summary>
        /// <param name="gpManager">The manager that owns this controller and executes gameplay transitions.</param>
        internal void Init(GamePlayManager gpManager)
        {
            _gpManager = gpManager;
            var settings = MajEnv.Settings;
            _isTrackSkipAvailable = settings.Game.TrackSkip;
            _isFastRetryAvailable = settings.Game.FastRetry;
            _isFastPracticeAvailable = settings.Game.FastPractice;
            _gameplaySubScreenClickBehavior = settings.Game.GameplaySubScreenClickBehavior;
            _isFnKeyInputArmed = false;
            ResetFnKeyPressTimes();

            switch (settings.Display.GameplayScreenRotationAngle)
            {
                case GameplayScreenRotationAngleOption._90:
                    _buttonKeyFor2367[0] = ButtonZone.A4;
                    _buttonKeyFor2367[1] = ButtonZone.A5;
                    _buttonKeyFor2367[2] = ButtonZone.A8;
                    _buttonKeyFor2367[3] = ButtonZone.A1;

                    _buttonKeyFor3456[0] = ButtonZone.A5;
                    _buttonKeyFor3456[1] = ButtonZone.A6;
                    _buttonKeyFor3456[2] = ButtonZone.A7;
                    _buttonKeyFor3456[3] = ButtonZone.A8;

                    _sensorAreaFor2367[0] = SensorArea.A4;
                    _sensorAreaFor2367[1] = SensorArea.A5;
                    _sensorAreaFor2367[2] = SensorArea.A8;
                    _sensorAreaFor2367[3] = SensorArea.A1;

                    _sensorAreaFor3456[0] = SensorArea.A5;
                    _sensorAreaFor3456[1] = SensorArea.A6;
                    _sensorAreaFor3456[2] = SensorArea.A7;
                    _sensorAreaFor3456[3] = SensorArea.A8;

                    _buttonKeyFor1278[0] = ButtonZone.A1;
                    _buttonKeyFor1278[1] = ButtonZone.A2;
                    _buttonKeyFor1278[2] = ButtonZone.A3;
                    _buttonKeyFor1278[3] = ButtonZone.A4;

                    _sensorAreaFor1278[0] = SensorArea.A1;
                    _sensorAreaFor1278[1] = SensorArea.A2;
                    _sensorAreaFor1278[2] = SensorArea.A3;
                    _sensorAreaFor1278[3] = SensorArea.A4;
                    break;
                case GameplayScreenRotationAngleOption._180:
                    _buttonKeyFor2367[0] = ButtonZone.A6;
                    _buttonKeyFor2367[1] = ButtonZone.A7;
                    _buttonKeyFor2367[2] = ButtonZone.A2;
                    _buttonKeyFor2367[3] = ButtonZone.A3;

                    _buttonKeyFor3456[0] = ButtonZone.A7;
                    _buttonKeyFor3456[1] = ButtonZone.A8;
                    _buttonKeyFor3456[2] = ButtonZone.A1;
                    _buttonKeyFor3456[3] = ButtonZone.A2;

                    _sensorAreaFor2367[0] = SensorArea.A6;
                    _sensorAreaFor2367[1] = SensorArea.A7;
                    _sensorAreaFor2367[2] = SensorArea.A2;
                    _sensorAreaFor2367[3] = SensorArea.A3;

                    _sensorAreaFor3456[0] = SensorArea.A7;
                    _sensorAreaFor3456[1] = SensorArea.A8;
                    _sensorAreaFor3456[2] = SensorArea.A1;
                    _sensorAreaFor3456[3] = SensorArea.A2;

                    _buttonKeyFor1278[0] = ButtonZone.A3;
                    _buttonKeyFor1278[1] = ButtonZone.A4;
                    _buttonKeyFor1278[2] = ButtonZone.A5;
                    _buttonKeyFor1278[3] = ButtonZone.A6;

                    _sensorAreaFor1278[0] = SensorArea.A3;
                    _sensorAreaFor1278[1] = SensorArea.A4;
                    _sensorAreaFor1278[2] = SensorArea.A5;
                    _sensorAreaFor1278[3] = SensorArea.A6;
                    break;
                case GameplayScreenRotationAngleOption._270:
                    _buttonKeyFor2367[0] = ButtonZone.A8;
                    _buttonKeyFor2367[1] = ButtonZone.A1;
                    _buttonKeyFor2367[2] = ButtonZone.A4;
                    _buttonKeyFor2367[3] = ButtonZone.A5;

                    _buttonKeyFor3456[0] = ButtonZone.A1;
                    _buttonKeyFor3456[1] = ButtonZone.A2;
                    _buttonKeyFor3456[2] = ButtonZone.A3;
                    _buttonKeyFor3456[3] = ButtonZone.A4;

                    _sensorAreaFor2367[0] = SensorArea.A8;
                    _sensorAreaFor2367[1] = SensorArea.A1;
                    _sensorAreaFor2367[2] = SensorArea.A4;
                    _sensorAreaFor2367[3] = SensorArea.A5;

                    _sensorAreaFor3456[0] = SensorArea.A1;
                    _sensorAreaFor3456[1] = SensorArea.A2;
                    _sensorAreaFor3456[2] = SensorArea.A3;
                    _sensorAreaFor3456[3] = SensorArea.A4;

                    _buttonKeyFor1278[0] = ButtonZone.A5;
                    _buttonKeyFor1278[1] = ButtonZone.A6;
                    _buttonKeyFor1278[2] = ButtonZone.A7;
                    _buttonKeyFor1278[3] = ButtonZone.A8;

                    _sensorAreaFor1278[0] = SensorArea.A5;
                    _sensorAreaFor1278[1] = SensorArea.A6;
                    _sensorAreaFor1278[2] = SensorArea.A7;
                    _sensorAreaFor1278[3] = SensorArea.A8;
                    break;
                default:
                    _buttonKeyFor2367[0] = ButtonZone.A2;
                    _buttonKeyFor2367[1] = ButtonZone.A3;
                    _buttonKeyFor2367[2] = ButtonZone.A6;
                    _buttonKeyFor2367[3] = ButtonZone.A7;

                    _buttonKeyFor3456[0] = ButtonZone.A3;
                    _buttonKeyFor3456[1] = ButtonZone.A4;
                    _buttonKeyFor3456[2] = ButtonZone.A5;
                    _buttonKeyFor3456[3] = ButtonZone.A6;

                    _sensorAreaFor2367[0] = SensorArea.A2;
                    _sensorAreaFor2367[1] = SensorArea.A3;
                    _sensorAreaFor2367[2] = SensorArea.A6;
                    _sensorAreaFor2367[3] = SensorArea.A7;

                    _sensorAreaFor3456[0] = SensorArea.A3;
                    _sensorAreaFor3456[1] = SensorArea.A4;
                    _sensorAreaFor3456[2] = SensorArea.A5;
                    _sensorAreaFor3456[3] = SensorArea.A6;

                    _buttonKeyFor1278[0] = ButtonZone.A1;
                    _buttonKeyFor1278[1] = ButtonZone.A2;
                    _buttonKeyFor1278[2] = ButtonZone.A7;
                    _buttonKeyFor1278[3] = ButtonZone.A8;

                    _sensorAreaFor1278[0] = SensorArea.A1;
                    _sensorAreaFor1278[1] = SensorArea.A2;
                    _sensorAreaFor1278[2] = SensorArea.A7;
                    _sensorAreaFor1278[3] = SensorArea.A8;
                    break;
            }
        }

        /// <summary>The gameplay action selected by the current shortcut input.</summary>
        internal enum FnAction
        {
            /// <summary>No shortcut has reached its hold threshold.</summary>
            None,

            /// <summary>Skips the track using the standard practice return delay.</summary>
            TrackSkip,

            /// <summary>Skips the track using the shorter practice return transition.</summary>
            QuickTrackSkip,

            /// <summary>Restarts the current track.</summary>
            FastRetry,

            /// <summary>Opens practice selection around the current playback time.</summary>
            FastPractice,
        }

        /// <summary>Updates shortcut input after the notes and executes the selected gameplay action.</summary>
        internal void OnUpdate()
        {
            using (UnityProfiler.Create("FnController.OnUpdate"))
            {
                if (_initWaitTimer < InitWaitTimeSec)
                {
                    _initWaitTimer += MajTimeline.DeltaTime;
                    return;
                }
                switch (UpdateInputState())
                {
                    case FnAction.TrackSkip:
                        _gpManager.SkipTrack().Forget();
                        break;
                    case FnAction.QuickTrackSkip:
                        _gpManager.SkipTrack(quickReturn: true).Forget();
                        break;
                    case FnAction.FastRetry:
                        _gpManager.FastRetry().Forget();
                        break;
                    case FnAction.FastPractice:
                        _gpManager.FastPractice();
                        break;
                }
            }
        }

        /// <summary>Updates shortcut holds and selects an action without performing a gameplay transition.</summary>
        /// <returns>The action selected by the current inputs, settings and hold times.</returns>
        internal FnAction UpdateInputState()
        {
            if (_gpManager.State is GamePlayStatus.Ended)
            {
                _isFnKeyInputArmed = false;
                ResetFnKeyPressTimes();
                return FnAction.None;
            }
            if (!_isFnKeyInputArmed)
            {
                // Do not inherit a shortcut that was pressed while entering this Game scene.
                ResetFnKeyPressTimes();
                if (IsFnKeyInputReleased())
                {
                    _isFnKeyInputArmed = true;
                }
                return FnAction.None;
            }

            UpdatePressTimes();
            return GetAction();
        }

        /// <summary>Accumulates the highest priority pressed shortcut and resets the other combination timers.</summary>
        private void UpdatePressTimes()
        {
            var isTrackSkipPressed = _isTrackSkipAvailable && IsShortcutPressed(_buttonKeyFor2367, _sensorAreaFor2367);
            var isFastRetryPressed = _isFastRetryAvailable && IsShortcutPressed(_buttonKeyFor3456, _sensorAreaFor3456);
            var isFastPracticePressed = _isFastPracticeAvailable && IsShortcutPressed(_buttonKeyFor1278, _sensorAreaFor1278);
            var isSubScreenPressed = InputManager.CheckButtonStatus(ButtonZone.P1, SwitchStatus.On);

            if (isSubScreenPressed)
            {
                _p1PressTime += MajTimeline.DeltaTime;
            }
            else if (isTrackSkipPressed)
            {
                _2367PressTime += MajTimeline.DeltaTime;
                _3456PressTime = 0;
                _1278PressTime = 0;
            }
            else if (isFastRetryPressed)
            {
                _3456PressTime += MajTimeline.DeltaTime;
                _2367PressTime = 0;
                _1278PressTime = 0;
            }
            else if (isFastPracticePressed)
            {
                _1278PressTime += MajTimeline.DeltaTime;
                _2367PressTime = 0;
                _3456PressTime = 0;
            }
            else
            {
                ResetFnKeyPressTimes();
            }
        }

        /// <summary>Selects the configured action when its hold threshold is reached.</summary>
        /// <returns>The highest priority eligible action, or no action while its hold threshold is unmet.</returns>
        private FnAction GetAction()
        {
            if (_p1PressTime != 0)
            {
                switch (_gameplaySubScreenClickBehavior)
                {
                    case GameplaySubScreenClickBehaviorOption.TrackSkip:
                    case GameplaySubScreenClickBehaviorOption.TrackSkip_1_Sec_Delay when _p1PressTime >= 1f:
                        return FnAction.QuickTrackSkip;
                    case GameplaySubScreenClickBehaviorOption.FastRetry:
                    case GameplaySubScreenClickBehaviorOption.FastRetry_1_Sec_Delay when _p1PressTime >= 1f:
                        return FnAction.FastRetry;
                    default:
                        return FnAction.None;
                }
            }
            else if (_2367PressTime >= 0.5f && _isTrackSkipAvailable)
            {
                return FnAction.TrackSkip;
            }
            else if (_3456PressTime >= 0.5f && _isFastRetryAvailable)
            {
                return FnAction.FastRetry;
            }
            else if (_1278PressTime >= 0.5f && _isFastPracticeAvailable)
            {
                return FnAction.FastPractice;
            }
            return FnAction.None;
        }

        /// <summary>Checks whether a full outer button or mobile touch combination is held.</summary>
        /// <param name="buttons">The outer buttons that must all be pressed.</param>
        /// <param name="sensors">The mobile touch sensors that must all be pressed.</param>
        /// <returns>Whether either supported input source reports the entire combination as pressed.</returns>
        private static bool IsShortcutPressed(ButtonZone[] buttons, SensorArea[] sensors)
        {
            var areButtonsPressed = true;
            for (var i = 0; i < buttons.Length; i++)
            {
                if (!InputManager.CheckButtonStatus(buttons[i], SwitchStatus.On))
                {
                    areButtonsPressed = false;
                    break;
                }
            }
#if UNITY_ANDROID || UNITY_IOS
            if (areButtonsPressed)
            {
                return true;
            }
            for (var i = 0; i < sensors.Length; i++)
            {
                if (!InputManager.CheckSensorStatus(sensors[i], SwitchStatus.On))
                {
                    return false;
                }
            }
            return true;
#else
            return areButtonsPressed;
#endif
        }

        /// <summary>Clears the accumulated hold time of every shortcut.</summary>
        private void ResetFnKeyPressTimes()
        {
            _3456PressTime = 0;
            _2367PressTime = 0;
            _1278PressTime = 0;
            _p1PressTime = 0;
        }

        /// <summary>Checks that every shortcut input is released before accepting a new hold.</summary>
        /// <returns>Whether P1 and all mapped buttons and supported touch sensors are released.</returns>
        private bool IsFnKeyInputReleased()
        {
            if (InputManager.CheckButtonStatus(ButtonZone.P1, SwitchStatus.On))
            {
                return false;
            }
            for (var i = 0; i < _buttonKeyFor2367.Length; i++)
            {
                if (InputManager.CheckButtonStatus(_buttonKeyFor2367[i], SwitchStatus.On) ||
                    InputManager.CheckButtonStatus(_buttonKeyFor3456[i], SwitchStatus.On) ||
                    InputManager.CheckButtonStatus(_buttonKeyFor1278[i], SwitchStatus.On))
                {
                    return false;
                }
#if UNITY_ANDROID || UNITY_IOS
                if (InputManager.CheckSensorStatus(_sensorAreaFor2367[i], SwitchStatus.On) ||
                    InputManager.CheckSensorStatus(_sensorAreaFor3456[i], SwitchStatus.On) ||
                    InputManager.CheckSensorStatus(_sensorAreaFor1278[i], SwitchStatus.On))
                {
                    return false;
                }
#endif
            }
            return true;
        }
    }
}
