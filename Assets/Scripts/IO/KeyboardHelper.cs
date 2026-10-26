using System;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#if UNITY_STANDALONE_WIN
using MajdataPlay.Syscall.Win32;
#endif
#if (UNITY_IOS || UNITY_EDITOR_OSX) && !UNITY_STANDALONE_WIN
using MajdataPlay.Platform.iOS;
#endif
#if UNITY_ANDROID
using AndroidKeyCode = MajdataPlay.Platform.Android.IO.KeyCode;
#endif

namespace MajdataPlay.IO
{
    internal static class KeyboardHelper
    {
        internal const int GameButtonCount = 8;

        static readonly KeyCode[] _buttonBindings =
        {
            KeyCode.B1, KeyCode.B2, KeyCode.B3, KeyCode.B4,
            KeyCode.B5, KeyCode.B6, KeyCode.B7, KeyCode.B8,
            KeyCode.Test, KeyCode.SelectP1, KeyCode.Service, KeyCode.SelectP2,
        };

        /// <summary>Logical bindings in ButtonZone order; device samplers share this mapping.</summary>
        internal static ReadOnlySpan<KeyCode> ButtonBindings => _buttonBindings;

        internal static KeyCode GetBindingKey(ButtonZone zone)
        {
            var index = (int)zone;
            if ((uint)index >= (uint)_buttonBindings.Length)
                throw new ArgumentOutOfRangeException(nameof(zone));
            return _buttonBindings[index];
        }

        internal static bool IsKeyDown(KeyCode keyCode)
        {
#if UNITY_STANDALONE_WIN
            var result = Win32API.GetAsyncKeyState((int)ToWinKeyCode(keyCode));
            return (result & 0x8000) != 0;
#elif UNITY_STANDALONE
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard[ToUnityKeyCode(keyCode)].isPressed;
#else
            return false;
#endif
        }

        internal static void ReadFunctionButtons(Span<bool> states)
        {
            var bindings = ButtonBindings;
            for (var i = GameButtonCount; i < bindings.Length; i++)
                states[i] |= IsKeyDown(bindings[i]);
        }

#if UNITY_STANDALONE_WIN
        internal static Win32API.RawKey ToWinKeyCode(KeyCode keyCode)
        {
            return keyCode switch
            {
                KeyCode.B1 => Win32API.RawKey.W,
                KeyCode.B2 => Win32API.RawKey.E,
                KeyCode.B3 => Win32API.RawKey.D,
                KeyCode.B4 => Win32API.RawKey.C,
                KeyCode.B5 => Win32API.RawKey.X,
                KeyCode.B6 => Win32API.RawKey.Z,
                KeyCode.B7 => Win32API.RawKey.A,
                KeyCode.B8 => Win32API.RawKey.Q,
                KeyCode.Test => Win32API.RawKey.Numpad9,
                KeyCode.SelectP1 => Win32API.RawKey.Multiply,
                KeyCode.Service => Win32API.RawKey.Numpad7,
                KeyCode.SelectP2 => Win32API.RawKey.Numpad3,
                _ => throw new ArgumentOutOfRangeException(nameof(keyCode)),
            };
        }
#endif

        internal static Key ToUnityKeyCode(KeyCode keyCode)
        {
            return keyCode switch
            {
                KeyCode.B1 => Key.W,
                KeyCode.B2 => Key.E,
                KeyCode.B3 => Key.D,
                KeyCode.B4 => Key.C,
                KeyCode.B5 => Key.X,
                KeyCode.B6 => Key.Z,
                KeyCode.B7 => Key.A,
                KeyCode.B8 => Key.Q,
                KeyCode.Test => Key.Numpad9,
                KeyCode.SelectP1 => Key.NumpadMultiply,
                KeyCode.Service => Key.Numpad7,
                KeyCode.SelectP2 => Key.Numpad3,
                _ => throw new ArgumentOutOfRangeException(nameof(keyCode)),
            };
        }

#if (UNITY_IOS || UNITY_EDITOR_OSX) && !UNITY_STANDALONE_WIN
        internal static GCKeyCode ToiOSGCKeyCode(KeyCode keyCode)
        {
            return keyCode switch
            {
                KeyCode.B1 => GCKeyCode.KeyW,
                KeyCode.B2 => GCKeyCode.KeyE,
                KeyCode.B3 => GCKeyCode.KeyD,
                KeyCode.B4 => GCKeyCode.KeyC,
                KeyCode.B5 => GCKeyCode.KeyX,
                KeyCode.B6 => GCKeyCode.KeyZ,
                KeyCode.B7 => GCKeyCode.KeyA,
                KeyCode.B8 => GCKeyCode.KeyQ,
                KeyCode.Test => GCKeyCode.Keypad9,
                KeyCode.SelectP1 => GCKeyCode.KeypadAsterisk,
                KeyCode.Service => GCKeyCode.Keypad7,
                KeyCode.SelectP2 => GCKeyCode.Keypad3,
                _ => throw new ArgumentOutOfRangeException(nameof(keyCode)),
            };
        }
#endif

#if UNITY_ANDROID
        internal static AndroidKeyCode ToAndroidKeyCode(KeyCode keyCode)
        {
            return keyCode switch
            {
                KeyCode.B1 => AndroidKeyCode.W,
                KeyCode.B2 => AndroidKeyCode.E,
                KeyCode.B3 => AndroidKeyCode.D,
                KeyCode.B4 => AndroidKeyCode.C,
                KeyCode.B5 => AndroidKeyCode.X,
                KeyCode.B6 => AndroidKeyCode.Z,
                KeyCode.B7 => AndroidKeyCode.A,
                KeyCode.B8 => AndroidKeyCode.Q,
                KeyCode.Test => AndroidKeyCode.Numpad9,
                KeyCode.SelectP1 => AndroidKeyCode.NumpadMultiply,
                KeyCode.Service => AndroidKeyCode.Numpad7,
                KeyCode.SelectP2 => AndroidKeyCode.Numpad3,
                _ => AndroidKeyCode.Unknown,
            };
        }
#endif

        internal static GamepadButton ToGamepadButton(KeyCode keyCode)
        {
            return keyCode switch
            {
                KeyCode.B1 => GamepadButton.North,
                KeyCode.B2 => GamepadButton.East,
                KeyCode.B3 => GamepadButton.South,
                KeyCode.B4 => GamepadButton.West,
                KeyCode.B5 => GamepadButton.DpadUp,
                KeyCode.B6 => GamepadButton.DpadRight,
                KeyCode.B7 => GamepadButton.DpadDown,
                KeyCode.B8 => GamepadButton.DpadLeft,
                KeyCode.Test => GamepadButton.LeftShoulder,
                KeyCode.SelectP1 => GamepadButton.RightShoulder,
                KeyCode.Service => GamepadButton.LeftStick,
                KeyCode.SelectP2 => GamepadButton.RightStick,
                _ => throw new ArgumentOutOfRangeException(nameof(keyCode)),
            };
        }
    }
}
