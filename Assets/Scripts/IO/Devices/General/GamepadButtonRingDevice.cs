#if UNITY_ANDROID || UNITY_IOS
using System;
using MajdataPlay.Diagnostics;
using UnityEngine.InputSystem;

namespace MajdataPlay.IO
{
    internal sealed class GamepadButtonRingDevice : IButtonRingDevice
    {
        public bool IsConnected { get; private set; }

        readonly InputStateBuffer _states = new(12);
        readonly bool[] _buttonStates = new bool[12];

        public void OnPreUpdate()
        {
            try
            {
                var gamepad = Gamepad.current;
                if (gamepad is null)
                {
                    ClearStates();
                    return;
                }
                _buttonStates.AsSpan().Clear();
                var bindings = KeyboardHelper.ButtonBindings.Slice(0, KeyboardHelper.GameButtonCount);
                for (var i = 0; i < bindings.Length; i++)
                {
                    var button = KeyboardHelper.ToGamepadButton(bindings[i]);
                    _buttonStates[i] = gamepad[button].isPressed;
                }
                IsConnected = true;
                _states.Publish(_buttonStates);
            }
            catch (Exception e)
            {
                ClearStates();
                MajDebug.LogError(nameof(GamepadButtonRingDevice), e);
            }
            finally
            {
                _states.OnPreUpdate();
            }
        }

        void ClearStates()
        {
            IsConnected = false;
            _buttonStates.AsSpan().Clear();
            _states.Clear();
        }

        public void ReadButtons(Span<bool> states, Span<bool> hadOn, Span<bool> hadOff) =>
            _states.CopyTo(states, hadOn, hadOff);
        public bool IsButtonCurrentlyOn(int index) => _states.IsCurrentlyOn(index);
    }
}
#endif
