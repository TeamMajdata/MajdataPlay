#if UNITY_ANDROID && !UNITY_EDITOR
#define ANDROID_RUNTIME
#endif
using MajdataPlay.Diagnostics;
using MajdataPlay.Platform.Android.IO;
using MajdataPlay.Platform.Android.Runtime.Bindings.Android.App;
using MajdataPlay.Platform.Android.Runtime.Bindings.Android.Content;
using MajdataPlay.Platform.Android.Runtime.Bindings.Android.Os;
using MajdataPlay.Platform.Android.Runtime.Bindings.Com.Unity3d.Player;
using MajdataPlay.Platform.Android.Runtime.Bindings.Net.Majdata.Majdataplay;
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

        public static Activity CurrentActivity { get => MajdataPlayActivity.GetCurrentActivity()!; }

        public static long VersionCode { get; private set; }
        public static int SdkVersion { get; private set; }
        public static int TargetSdkVersion { get; private set; }


        private static readonly OnNewIntentCallbackProxy s_onNewIntentCallbackProxy = new();
        private static readonly OnActivityResultCallbackProxy s_onActivityResultCallbackProxy = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
#if !ANDROID_RUNTIME
            MajDebug.LogInfo(nameof(AndroidRuntime), "Init");
            try
            {
                var activityClass = MajdataPlayActivity.Class!;
                MajDebug.LogInfo(nameof(AndroidRuntime), "Setting onNewIntent callback proxy");
                activityClass.CallStatic("registerOnNewIntentCallback", s_onNewIntentCallbackProxy);
                MajDebug.LogInfo(nameof(AndroidRuntime), "Setting onActivityResult callback proxy");
                activityClass.CallStatic("registerOnActivityResultCallback", s_onActivityResultCallbackProxy);
                SdkVersion = Build_Version.SdkInt;
                var appInfo = CurrentActivity.GetApplicationInfo()!;
                var packageManager = CurrentActivity.GetPackageManager()!;
                var packageInfo = packageManager!.GetPackageInfo(CurrentActivity.GetPackageName(), 0)!;
                TargetSdkVersion = appInfo.TargetSdkVersion;

                if (SdkVersion >= 28)
                {
                    VersionCode = packageInfo.GetLongVersionCode();
                }
                else
                {
                    VersionCode = packageInfo.VersionCode;
                }

                MajDebug.LogInfo(nameof(AndroidRuntime), $"SdkVersion: {SdkVersion}");
                MajDebug.LogInfo(nameof(AndroidRuntime), $"TargetSdkVersion: {TargetSdkVersion}");
                MajDebug.LogInfo(nameof(AndroidRuntime), $"VersionCode: {VersionCode}");

                AndroidKeyboard.Init();
                MajDebug.LogInfo(nameof(AndroidRuntime), "Init finished");
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
