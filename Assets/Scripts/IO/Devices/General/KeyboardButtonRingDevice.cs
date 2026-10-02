using System;
using System.Threading;
using MajdataPlay.Diagnostics;
using MajdataPlay.Settings;
using UnityEngine.InputSystem;
#if UNITY_ANDROID
using MajdataPlay.Platform.Android.IO;
#endif
#if UNITY_IOS
using MajdataPlay.Platform.iOS;
#endif
namespace MajdataPlay.IO
{
    internal sealed class KeyboardButtonRingDevice : IODevice, IButtonRingDevice
    {
        readonly InputStateBuffer _states = new InputStateBuffer(12);
        readonly bool[] _buttonRealTimeStates = new bool[12];
        protected override string DaemonThreadName => "IO/ButtonRing Thread";
        protected override TimeSpan PollingInterval
        {
            get
            {
#if UNITY_STANDALONE
                return TimeSpan.FromMilliseconds(MajEnv.Settings.IO.InputDevice.ButtonRing.PollingRateMs);
#else
                return TimeSpan.Zero;
#endif
            }
        }
        protected override bool Connect(CancellationToken token) => true;
        protected override void Parse(ReadOnlySpan<byte> data) { }
        protected override void Update(CancellationToken token)
        {
            var bindings = KeyboardHelper.ButtonBindings;
            for (var i = 0; i < bindings.Length; i++)
                _buttonRealTimeStates[i] = KeyboardHelper.IsKeyDown(bindings[i]);
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
            var bindings = KeyboardHelper.ButtonBindings.Slice(0, KeyboardHelper.GameButtonCount);
            try
            {
#if UNITY_ANDROID
                IsConnected = true;
                for (var i = 0; i < bindings.Length; i++)
                {
                    var keyCode = bindings[i];
                    var androidKeyCode = KeyboardHelper.ToAndroidKeyCode(keyCode);
                    _buttonRealTimeStates[i] = AndroidKeyboard.IsPreesedUnsafe(androidKeyCode);
                }
#elif UNITY_IOS
                for (var i = 0; i < bindings.Length; i++)
                {
                    var keyCode = KeyboardHelper.ToiOSGCKeyCode(bindings[i]);
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
            var bindings = KeyboardHelper.ButtonBindings.Slice(0, KeyboardHelper.GameButtonCount);
            try
            {
                var gamepad = Gamepad.current;
                for (var i = 0; i < bindings.Length; i++)
                {
                    var keyCode = bindings[i];
                    var state = gamepad?[KeyboardHelper.ToGamepadButton(keyCode)].isPressed ?? false;
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
