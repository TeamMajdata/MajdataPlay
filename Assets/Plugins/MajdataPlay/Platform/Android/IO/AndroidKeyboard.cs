using MajdataPlay.Diagnostics;
using MajdataPlay.Platform.Android.Runtime;
using MajdataPlay.Platform.Android.Runtime.Bindings.Net.Majdata.Majdataplay;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

#nullable enable
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
        /// <summary>Keeps the registered key-event proxy alive for the Java activity's lifetime.</summary>
        private static OnDispatchKeyEventCallbackProxy? s_onDispatchKeyEventCallbackProxy;
        readonly static bool[] _keyStates = new bool[1024];

        /// <summary>Registers the production Java key callback and propagates registration failures to runtime initialization.</summary>
        /// <exception cref="PlatformNotSupportedException">Execution is outside an Android player.</exception>
        /// <exception cref="AndroidJavaException">Unity cannot create the proxy.</exception>
        /// <exception cref="JavaInvocationException">Java rejects callback registration.</exception>
        /// <exception cref="InvalidOperationException">JNI cannot create the proxy or allocate a local frame.</exception>
        internal static void Init()
        {
            if (Application.platform != RuntimePlatform.Android || Application.isEditor)
            {
                throw new PlatformNotSupportedException("The Android keyboard is only available in an Android player.");
            }
            MajDebug.LogInfo($"[{nameof(AndroidKeyboard)}] Init");
            MajdataPlayActivity.RegisterDispatchKeyEventCallback(s_onDispatchKeyEventCallbackProxy ??= new OnDispatchKeyEventCallbackProxy());
            MajDebug.LogInfo($"[{nameof(AndroidKeyboard)}] Init finished");
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
            /// <summary>Creates a proxy for the production Java key callback interface.</summary>
            public OnDispatchKeyEventCallbackProxy() : base("net.majdata.majdataplay.CSharpOnDispatchKeyEventCallback")
            {
            }

            /// <summary>Updates managed key state when the production Java activity dispatches a key event.</summary>
            /// <param name="action">The Android key action; one releases the key.</param>
            /// <param name="keyCode">The Android key code used as an index into the key-state buffer.</param>
            [UnityEngine.Scripting.Preserve]
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
