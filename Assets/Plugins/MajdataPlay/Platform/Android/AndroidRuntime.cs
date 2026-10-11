using MajdataPlay.Platform.Android.Runtime.App;
using MajdataPlay.Platform.Android.Runtime.Content;
using MajdataPlay.Platform.Android.Runtime.Unity;
using System;
using UnityEngine;
#nullable enable
namespace MajdataPlay.Platform.Android
{
    public delegate void OnActivityResultCallback(object? sender, int requestCode, int resultCode, Intent? intent);
    public static class AndroidRuntime
    {
        public static event EventHandler<Intent?>? OnNewIntent;
        public static event OnActivityResultCallback? OnActivityResult;

        public static AndroidJavaClass MajdataPlayActivityClass { get; private set; } = null!;
        public static Activity CurrentActivity { get => UnityPlayer.CurrentActivity!; }



        private static readonly OnNewIntentCallbackProxy s_onNewIntentCallbackProxy = new();
        private static readonly OnActivityResultCallbackProxy s_onActivityResultCallbackProxy = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            MajDebug.LogInfo($"[{nameof(AndroidRuntime)}] Init");
            try
            {
                MajdataPlayActivityClass = new AndroidJavaClass("net.majdata.majdataplay.MajdataPlayActivity");
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
        private static void Android_OnNewIntent(AndroidJavaObject intent)
        {
            if (OnNewIntent is not null)
            {
                OnNewIntent(null, new Intent(intent, false));
            }
        }
        private static void Android_OnActivityResult(int requestCode, int resultCode, AndroidJavaObject? intent)
        {
            if (OnActivityResult is not null)
            {
                OnActivityResult(null, requestCode, resultCode, intent != null ? new Intent(intent, false) : null);
            }
        }
        private class OnNewIntentCallbackProxy : AndroidJavaProxy
        {
            public OnNewIntentCallbackProxy() : base("net.majdata.majdataplay.CSharpOnNewIntentCallback") { }

            public void OnNewIntent(AndroidJavaObject intent)
            {
                Android_OnNewIntent(intent);
            }
        }
        private class OnActivityResultCallbackProxy : AndroidJavaProxy
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
