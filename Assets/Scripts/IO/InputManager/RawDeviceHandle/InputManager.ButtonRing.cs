using System;
using System.Runtime.CompilerServices;
using MajdataPlay.Numerics;
using MajdataPlay.Utils;
#nullable enable
namespace MajdataPlay.IO
{
    internal static partial class InputManager
    {
        static class ButtonRing
        {
            static IButtonRingDevice? _device;
            public static bool IsConnected => _device?.IsConnected ?? false;
            static readonly bool[] _buttonStates = new bool[12];
            static readonly bool[] _isBtnHadOn = new bool[12];
            static readonly bool[] _isBtnHadOff = new bool[12];

            /// <summary>Tracks game keys suppressed by a text field until their release.</summary>
            static readonly bool[] _textInputSuppressedKeys = new bool[8];

            public static void Init()
            {
                GameDeviceManager.Init();
                _device = GameDeviceManager.ButtonRing;
            }

            /// <summary>Reads the frame snapshot and suppresses keyboard game keys consumed by text input.</summary>
            public static void OnPreUpdate()
            {
                _device?.ReadButtons(_buttonStates, _isBtnHadOn, _isBtnHadOff);
#if UNITY_STANDALONE
                FilterTextInputKeys(_device is KeyboardButtonRingDevice);
#elif UNITY_ANDROID
                FilterTextInputKeys(_device is AndroidKeyboardButtonRingDevice);
#elif UNITY_IOS
                FilterTextInputKeys(_device is IOSKeyboardButtonRingDevice);
#endif
            }
            /// <summary>Keeps keyboard game keys inactive until release after a text field consumes them.</summary>
            /// <param name="isKeyboard">Whether the current device supplies keyboard input.</param>
            static void FilterTextInputKeys(bool isKeyboard)
            {
                if (!isKeyboard)
                {
                    return;
                }
                var isEditingText = SuppressKeyboardGameButtons;
                for (var i = 0; i < _textInputSuppressedKeys.Length; i++)
                {
                    var active = _buttonStates[i] || _isBtnHadOn[i];
                    if (isEditingText && active)
                    {
                        _textInputSuppressedKeys[i] = true;
                    }
                    if (!_textInputSuppressedKeys[i])
                    {
                        continue;
                    }
                    // Keep a key suppressed until release even if the text field loses focus.
                    _textInputSuppressedKeys[i] = active;
                    _buttonStates[i] = false;
                    _isBtnHadOn[i] = false;
                    _isBtnHadOff[i] = true;
                }
            }
            #region Queries
            /// <summary>
            /// Determines whether the button at the given index was ever ON
            /// during the interval between the two most recent OnPreUpdate calls.
            /// </summary>
            /// <param name="index">
            /// Zero‑based button index (valid range 0–11).
            /// </param>
            /// <returns>
            /// True if the button was ON at any point during that interval; otherwise, false.
            /// </returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsHadOn(int index)
            {
                if (!index.InRange(0, 11))
                    return false;

                return _isBtnHadOn[index];
            }
            /// <summary>
            /// Determines whether the button at the given index is ON in the this frame.
            /// </summary>
            /// <param name="index">
            /// Zero‑based button index (valid range 0–11).
            /// </param>
            /// <returns>
            /// True if the button state is ON in this frame; otherwise, false.
            /// </returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsOn(int index)
            {
                if (!index.InRange(0, 11))
                    return false;

                return _buttonStates[index];
            }
            /// <summary>
            /// Determines whether the button at the given index was ever OFF
            /// during the interval between the two most recent OnPreUpdate calls.
            /// </summary>
            /// <param name="index">
            /// Zero‑based button index (valid range 0–11).
            /// </param>
            /// <returns>
            /// True if the button was OFF at any point during that interval; otherwise, false.
            /// </returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsHadOff(int index)
            {
                if (!index.InRange(0, 11))
                    return false;

                return _isBtnHadOff[index];
            }
            /// <summary>
            /// Determines whether the button at the given index is OFF in the this frame.
            /// </summary>
            /// <param name="index">
            /// Zero‑based button index (valid range 0–11).
            /// </param>
            /// <returns>
            /// True if the button state is OFF in this frame; otherwise, false.
            /// </returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsOff(int index)
            {
                return !IsOn(index);
            }
            /// <summary>
            /// Retrieves the real‑time state of the button at the given index
            /// as read from the IO thread, indicating whether it is currently ON.
            /// </summary>
            /// <param name="index">
            /// Zero‑based button index (valid range 0–11).
            /// </param>
            /// <returns>
            /// True if the button is ON according to the latest IO thread reading; otherwise, false.
            /// </returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsCurrentlyOn(int index)
            {
                if (!index.InRange(0, 11))
                    return false;

                return _device?.IsButtonCurrentlyOn(index) ?? false;
            }
            /// <summary>
            /// Retrieves the real‑time state of the button at the given index
            /// as read from the IO thread, indicating whether it is currently OFF.
            /// </summary>
            /// <param name="index">
            /// Zero‑based button index (valid range 0–11).
            /// </param>
            /// <returns>
            /// True if the button is OFF according to the latest IO thread reading; otherwise, false.
            /// </returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsCurrentlyOff(int index)
            {
                return !IsCurrentlyOn(index);
            }


            /// <summary>
            /// See also <seealso cref="IsHadOn(int)"/>
            /// </summary>
            /// <param name="area"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsHadOn(ButtonZone area)
            {
                return IsHadOn(GetIndexFromArea(area));
            }
            /// <summary>
            /// See also <seealso cref="IsOn(int)"/>
            /// </summary>
            /// <param name="area"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsOn(ButtonZone area)
            {
                return IsOn(GetIndexFromArea(area));
            }
            /// <summary>
            /// See also <seealso cref="IsHadOff(int)"/>
            /// </summary>
            /// <param name="area"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsHadOff(ButtonZone area)
            {
                return IsHadOff(GetIndexFromArea(area));
            }
            /// <summary>
            /// See also <seealso cref="IsOff(int)"/>
            /// </summary>
            /// <param name="area"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsOff(ButtonZone area)
            {
                return IsOff(GetIndexFromArea(area));
            }
            /// <summary>
            /// See also <seealso cref="IsCurrentlyOn(int)"/>
            /// </summary>
            /// <param name="area"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsCurrentlyOn(ButtonZone area)
            {
                return IsCurrentlyOn(GetIndexFromArea(area));
            }
            /// <summary>
            /// See also <seealso cref="IsCurrentlyOff(int)"/>
            /// </summary>
            /// <param name="area"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsCurrentlyOff(ButtonZone area)
            {
                return IsCurrentlyOff(GetIndexFromArea(area));
            }
            #endregion

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static int GetIndexFromArea(ButtonZone area)
            {
                if (area < ButtonZone.A1 || area > ButtonZone.P2)
                {
                    ThrowHelper.OutOfRange(nameof(area));
                }
                return (int)area;
            }
        }
    }
}
