using MajdataPlay.Diagnostics;
using MajdataPlay.Platform.Android.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
#nullable enable
namespace MajdataPlay.Platform.Android
{
    public delegate void OnActivityResultCallback(object? sender, int requestCode, int resultCode, AndroidJavaObject? intent);
    public static class AndroidRuntime
    {
        public static event EventHandler<AndroidJavaObject?>? OnNewIntent;
        public static event OnActivityResultCallback? OnActivityResult;

        public static AndroidJavaClass MajdataPlayActivityClass { get; private set; } = null!;
        public static AndroidJavaObject CurrentActivity { get; private set; } = null!;

        

        readonly static OnNewIntentCallbackProxy _onNewIntentCallbackProxy = new();
        readonly static OnActivityResultCallbackProxy _onActivityResultCallbackProxy = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            MajDebug.LogInfo($"[{nameof(AndroidRuntime)}] Init");
            try
            {
                MajdataPlayActivityClass = new AndroidJavaClass("net.majdata.majdataplay.MajdataPlayActivity");
                MajDebug.LogInfo($"[{nameof(AndroidRuntime)}] Get current activity");
                CurrentActivity = MajdataPlayActivityClass.GetStatic<AndroidJavaObject>("currentActivity");
                MajDebug.LogInfo($"[{nameof(AndroidRuntime)}] Setting onNewIntent callback proxy");
                MajdataPlayActivityClass.CallStatic("registerOnNewIntentCallback", _onNewIntentCallbackProxy);
                MajDebug.LogInfo($"[{nameof(AndroidRuntime)}] Setting onActivityResult callback proxy");
                MajdataPlayActivityClass.CallStatic("registerOnActivityResultCallback", _onActivityResultCallbackProxy);

            
                AndroidKeyboard.Init();
                MajDebug.LogInfo($"[{nameof(AndroidRuntime)}] Init finished");
            }
            catch (Exception ex)
            {
                MajDebug.LogError($"[{nameof(AndroidRuntime)}] Init failed: {ex}");
            }
#endif
        }
        static void Android_OnNewIntent(AndroidJavaObject intent)
        {
            if (OnNewIntent is not null)
            {
                OnNewIntent(null, intent);
            }
        }
        static void Android_OnActivityResult(int requestCode, int resultCode, AndroidJavaObject? intent)
        {
            if (OnActivityResult is not null)
            {
                OnActivityResult(null, requestCode, resultCode, intent);
            }
        }
        class OnNewIntentCallbackProxy : AndroidJavaProxy
        {
            public OnNewIntentCallbackProxy() : base("net.majdata.majdataplay.CSharpOnNewIntentCallback") { }

            public void OnNewIntent(AndroidJavaObject intent)
            {
                Android_OnNewIntent(intent);
            }
        }
        class OnActivityResultCallbackProxy : AndroidJavaProxy
        {
            public OnActivityResultCallbackProxy() : base("net.majdata.majdataplay.CSharpOnActivityResultCallback") { }

            /// <summary>Forwards Java activity results to managed subscribers, including SAF picker requests.</summary>
            /// <param name="requestCode">The request identifier supplied when launching the activity.</param>
            /// <param name="resultCode">Android's activity result status.</param>
            /// <param name="intent">The nullable result intent borrowed for the callback duration.</param>
            [UnityEngine.Scripting.Preserve]
            public void OnActivityResult(int requestCode, int resultCode, AndroidJavaObject? intent)
            {
                Android_OnActivityResult(requestCode, resultCode, intent);
            }
        }
    }
}
