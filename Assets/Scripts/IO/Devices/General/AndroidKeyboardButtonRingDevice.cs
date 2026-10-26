#if UNITY_ANDROID
using System;
using MajdataPlay.Diagnostics;
using MajdataPlay.Platform.Android.IO;

namespace MajdataPlay.IO
{
    internal sealed class AndroidKeyboardButtonRingDevice : IButtonRingDevice
    {
        public bool IsConnected { get; private set; }

        readonly InputStateBuffer _states = new(12);
        readonly bool[] _buttonStates = new bool[12];

        public void OnPreUpdate()
        {
            try
            {
                _buttonStates.AsSpan().Clear();
                var bindings = KeyboardHelper.ButtonBindings.Slice(0, KeyboardHelper.GameButtonCount);
                for (var i = 0; i < bindings.Length; i++)
                {
                    var keyCode = KeyboardHelper.ToAndroidKeyCode(bindings[i]);
                    _buttonStates[i] = AndroidKeyboard.IsPreesedUnsafe(keyCode);
                }
                IsConnected = true;
                _states.Publish(_buttonStates);
            }
            catch (Exception e)
            {
                IsConnected = false;
                _buttonStates.AsSpan().Clear();
                _states.Clear();
                MajDebug.LogError(nameof(AndroidKeyboardButtonRingDevice), e);
            }
            finally
            {
                _states.OnPreUpdate();
            }
        }

        public void ReadButtons(Span<bool> states, Span<bool> hadOn, Span<bool> hadOff) =>
            _states.CopyTo(states, hadOn, hadOff);
        public bool IsButtonCurrentlyOn(int index) => _states.IsCurrentlyOn(index);
    }
}
#endif
