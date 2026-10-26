#if UNITY_IOS
using System;
using MajdataPlay.Diagnostics;
using MajdataPlay.Platform.iOS;

namespace MajdataPlay.IO
{
    internal sealed class IOSKeyboardButtonRingDevice : IButtonRingDevice
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
                var initializationAttempted = false;
                for (var i = 0; i < bindings.Length; i++)
                {
                    var keyCode = KeyboardHelper.ToiOSGCKeyCode(bindings[i]);
                    var result = NativeKeyboard.IsPressed(keyCode, ref _buttonStates[i]);
                    if (result == ErrorCode.InvalidOperation && !initializationAttempted)
                    {
                        initializationAttempted = true;
                        result = NativeKeyboard.Init();
                        if (result == ErrorCode.NoError)
                        {
                            result = NativeKeyboard.IsPressed(keyCode, ref _buttonStates[i]);
                        }
                    }
                    if (result != ErrorCode.NoError)
                    {
                        ClearStates();
                        if (result is not (ErrorCode.NoDevice or ErrorCode.NotSupported))
                        {
                            MajDebug.LogError(nameof(IOSKeyboardButtonRingDevice),
                                $"Failed to read NativeKeyboard: {result}");
                        }
                        return;
                    }
                }
                IsConnected = true;
                _states.Publish(_buttonStates);
            }
            catch (Exception e)
            {
                ClearStates();
                MajDebug.LogError(nameof(IOSKeyboardButtonRingDevice), e);
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
