using MajdataPlay.Diagnostics;
using MajdataPlay.Platform.Android.Runtime.Bindings.Net.Majdata.Majdataplay;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace MajdataPlay.Platform.Android.IO
{
    public static class AndroidKeyboard
    {
        public static ReadOnlySpan<bool> KeyStates
        {
            get
            {
                return _keyStates;
            }
        }
        static OnDispatchKeyEventCallbackProxy _onDispatchKeyEventCallbackProxy;
        readonly static bool[] _keyStates = new bool[1024];

        internal static void Init()
        {
            MajDebug.LogInfo($"[{nameof(AndroidKeyboard)}] Init");
            try
            {
                _onDispatchKeyEventCallbackProxy = new OnDispatchKeyEventCallbackProxy();
                MajdataPlayActivity.Class.CallStatic("registerDispatchKeyEventCallback", _onDispatchKeyEventCallbackProxy);
                MajDebug.LogInfo($"[{nameof(AndroidKeyboard)}] Init finished");
            }
            catch (Exception ex)
            {
                MajDebug.LogError($"[{nameof(AndroidKeyboard)}] Init failed: {ex}");
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPressed(KeyCode keyCode)
        {
            return _keyStates[(int)keyCode];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static bool IsPreesedUnsafe(KeyCode keyCode)
        {
            fixed(bool* ptr = _keyStates)
            {
                return ptr[(int)keyCode];
            }
        }
        unsafe class OnDispatchKeyEventCallbackProxy : AndroidJavaProxy
        {
            public OnDispatchKeyEventCallbackProxy() : base("net.majdata.majdataplay.CSharpOnDispatchKeyEventCallback") { }

            public void OnDispatchKeyEvent(int action, int keyCode)
            {
                fixed(bool* ptr = AndroidKeyboard._keyStates)
                {
                    ptr[keyCode] = action != 1;
                }
            }
        }
    }
}
