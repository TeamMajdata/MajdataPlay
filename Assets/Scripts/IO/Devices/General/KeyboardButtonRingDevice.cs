using System;
using System.Threading;
using MajdataPlay.Diagnostics;
using MajdataPlay.Settings;
using UnityEngine.InputSystem;
#if UNITY_ANDROID
using MajdataPlay.Platform.Android;
using MajdataPlay.Platform.Android.IO;
#endif
#if UNITY_IOS
using MajdataPlay.Platform.iOS;
#endif
namespace MajdataPlay.IO
{
    internal static partial class InputManager
    {
        internal static void ReadFunctionButtons(Span<bool> states)
        {
            var buttons = _buttons.Span;
            for (var i = 8; i < buttons.Length; i++)
                states[i] |= KeyboardHelper.IsKeyDown(buttons[i].BindingKey);
        }

        internal sealed class KeyboardButtonRingDevice : IODevice, IButtonRingDevice
        {
            readonly InputStateBuffer _states = new InputStateBuffer(12);
            readonly bool[] _buttonRealTimeStates = new bool[12];
            protected override string DaemonThreadName => "IO/ButtonRing Thread";
            protected override TimeSpan PollingInterval => _btnPollingRateMs;
            protected override bool Connect(CancellationToken token) => true;
            protected override void Parse(ReadOnlySpan<byte> data) { }
            protected override void Update(CancellationToken token)
            {
                var buttons = _buttons.Span;
                for (var i = 0; i < buttons.Length; i++)
                    _buttonRealTimeStates[i] = KeyboardHelper.IsKeyDown(buttons[i].BindingKey);
                _states.Publish(_buttonRealTimeStates);
            }
            protected override void OnDisconnected() => _states.Clear();
            public override void OnPreUpdate()
            {
#if UNITY_ANDROID || UNITY_IOS
                switch (MajEnv.Settings.IO.InputDevice.ExternalButtonRing)
                {
                    case MobileExternalButtonRingOption.Keyboard: KeyboardPreUpdate(); break;
                    case MobileExternalButtonRingOption.Gamepad: GamepadPreUpdate(); break;
                }
#endif
                _states.OnPreUpdate();
            }
            public void ReadButtons(Span<bool> states, Span<bool> hadOn, Span<bool> hadOff) => _states.CopyTo(states, hadOn, hadOff);
            public bool IsButtonCurrentlyOn(int index) => _states.IsCurrentlyOn(index);
#if UNITY_ANDROID || UNITY_IOS
            void KeyboardPreUpdate()
            {
                var gameButtons = _buttons.Slice(0, 8);
                try
                {
#if UNITY_ANDROID
                    IsConnected = true;
                    for (var i = 0; i < gameButtons.Length; i++)
                    {
                        var button = gameButtons.Span[i];
                        var keyCode = button.BindingKey;
                        var androidKeyCode = (keyCode switch
                        {
                            KeyCode.B1 => MajdataPlay.Platform.Android.IO.KeyCode.W,
                            KeyCode.B2 => MajdataPlay.Platform.Android.IO.KeyCode.E,
                            KeyCode.B3 => MajdataPlay.Platform.Android.IO.KeyCode.D,
                            KeyCode.B4 => MajdataPlay.Platform.Android.IO.KeyCode.C,
                            KeyCode.B5 => MajdataPlay.Platform.Android.IO.KeyCode.X,
                            KeyCode.B6 => MajdataPlay.Platform.Android.IO.KeyCode.Z,
                            KeyCode.B7 => MajdataPlay.Platform.Android.IO.KeyCode.A,
                            KeyCode.B8 => MajdataPlay.Platform.Android.IO.KeyCode.Q,

                            KeyCode.Test => MajdataPlay.Platform.Android.IO.KeyCode.Numpad9,
                            KeyCode.SelectP1 => MajdataPlay.Platform.Android.IO.KeyCode.NumpadMultiply,
                            KeyCode.Service => MajdataPlay.Platform.Android.IO.KeyCode.Numpad7,
                            KeyCode.SelectP2 => MajdataPlay.Platform.Android.IO.KeyCode.Numpad3,

                            _ => MajdataPlay.Platform.Android.IO.KeyCode.Unknown
                        });
                        _buttonRealTimeStates[i] = AndroidKeyboard.IsPreesedUnsafe(androidKeyCode);
                    }
#elif UNITY_IOS
                    for (var i = 0; i < gameButtons.Length; i++)
                    {
                        var button = gameButtons.Span[i];
                        var keyCode = KeyboardHelper.ToiOSGCKeyCode(button.BindingKey);
                        var @return = NativeKeyboard.IsPressed(keyCode, ref _buttonRealTimeStates[i]);
                        switch(@return)
                        {
                            case ErrorCode.NoError:
                                IsConnected = true;
                                break;
                            case ErrorCode.NoDevice:
                            case ErrorCode.NotSupported:
                                IsConnected = false;
                                break;
                            case ErrorCode.InvalidOperation: // Not init
                                @return = NativeKeyboard.Init();
                                if(@return == ErrorCode.NoError)
                                {
                                    i--;
                                    IsConnected = true;
                                    continue;
                                }
                                IsConnected = false;
                                MajDebug.LogError(nameof(KeyboardButtonRingDevice), $"Failed to initialize NativeKeyboard: {@return}");
                                return;
                            default:
                                IsConnected = false;
                                MajDebug.LogError(nameof(KeyboardButtonRingDevice), $"Error occurred while reading key states from NativeKeyboard: {@return}");
                                break;
                        }
                    }
#endif
                    _states.Publish(_buttonRealTimeStates);
                }
                catch (Exception e)
                {
                    IsConnected = false;
                    MajDebug.LogError($"From Keyboard listener: \n{e}");
                }
            }
            void GamepadPreUpdate()
            {
                var gameButtons = _buttons.Slice(0, 8);
                try
                {
                    var gamepad = Gamepad.current;
                    

                    for (var i = 0; i < gameButtons.Length; i++)
                    {
                        var button = gameButtons.Span[i];
                        var keyCode = button.BindingKey;
                        var state = (keyCode switch
                        {
                            KeyCode.B1 => gamepad?.buttonNorth,
                            KeyCode.B2 => gamepad?.buttonEast,
                            KeyCode.B3 => gamepad?.buttonSouth,
                            KeyCode.B4 => gamepad?.buttonWest,

                            KeyCode.B5 => gamepad?.dpad.up,
                            KeyCode.B6 => gamepad?.dpad.right,
                            KeyCode.B7 => gamepad?.dpad.down,
                            KeyCode.B8 => gamepad?.dpad.left,

                            KeyCode.Test => gamepad?.leftShoulder,
                            KeyCode.SelectP1 => gamepad?.rightShoulder,
                            KeyCode.Service => gamepad?.leftStickButton,
                            KeyCode.SelectP2 => gamepad?.rightStickButton,

                            _ => null
                        })?.isPressed ?? false;
                        _buttonRealTimeStates[i] = state;
                    }
                    IsConnected = gamepad != null;

                    _states.Publish(_buttonRealTimeStates);
                }
                catch (Exception e)
                {
                    IsConnected = false;
                    MajDebug.LogError($"From Gamepad listener: \n{e}");
                }
            }
#endif
        }
    }
}
