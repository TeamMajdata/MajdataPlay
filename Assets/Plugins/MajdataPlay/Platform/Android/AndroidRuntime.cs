#if UNITY_ANDROID && !UNITY_EDITOR
#define ANDROID_RUNTIME
#endif
using MajdataPlay.Diagnostics;
using MajdataPlay.Platform.Android.IO;
using MajdataPlay.Platform.Android.Runtime;
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

        /// <summary>Gets a fresh owned reference to the current production activity.</summary>
        /// <remarks>The caller must dispose the returned wrapper after use.</remarks>
        /// <exception cref="InvalidOperationException">The activity has not been created.</exception>
        /// <exception cref="PlatformNotSupportedException">Execution is outside an Android player.</exception>
        public static Activity CurrentActivity
        {
            get
            {
                return MajdataPlayActivity.GetCurrentActivity()
                    ?? throw new InvalidOperationException("The Android activity has not been created.");
            }
        }

        /// <summary>Gets whether metadata and all production activity callbacks initialized successfully.</summary>
        public static bool IsInitialized { get; private set; }
        /// <summary>Gets the installed application's Android version code after initialization.</summary>
        public static long VersionCode { get; private set; }
        /// <summary>Gets the running Android SDK level after initialization.</summary>
        public static int SdkVersion { get; private set; }
        /// <summary>Gets the installed application's target SDK level after initialization.</summary>
        public static int TargetSdkVersion { get; private set; }


        /// <summary>Keeps the registered new-intent proxy alive for the Java activity's lifetime.</summary>
        private static OnNewIntentCallbackProxy? s_onNewIntentCallbackProxy;
        /// <summary>Keeps the registered activity-result proxy alive for the Java activity's lifetime.</summary>
        private static OnActivityResultCallbackProxy? s_onActivityResultCallbackProxy;

        private static readonly AndroidLogger s_logger = new();

        /// <summary>Registers production callbacks and publishes metadata only after initialization succeeds.</summary>
#if ANDROID_RUNTIME
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#endif
        private static void Init()
        {
            IsInitialized = false;
            MajDebug.AddLogger(s_logger);
            VersionCode = 0;
            SdkVersion = 0;
            TargetSdkVersion = 0;
            MajDebug.LogInfo(nameof(AndroidRuntime), "Init");
            try
            {
                MajDebug.LogInfo(nameof(AndroidRuntime), "Setting onNewIntent callback proxy");
                MajdataPlayActivity.RegisterOnNewIntentCallback(s_onNewIntentCallbackProxy ??= new OnNewIntentCallbackProxy());
                MajDebug.LogInfo(nameof(AndroidRuntime), "Setting onActivityResult callback proxy");
                MajdataPlayActivity.RegisterOnActivityResultCallback(s_onActivityResultCallbackProxy ??= new OnActivityResultCallbackProxy());
                var sdkVersion = Build.Version.SdkInt;
                using var activity = CurrentActivity;
                using var appInfo = activity.GetApplicationInfo()
                    ?? throw new InvalidOperationException("The Android application metadata is unavailable.");
                using var packageManager = activity.GetPackageManager()
                    ?? throw new InvalidOperationException("The Android package manager is unavailable.");
                using var packageInfo = packageManager.GetPackageInfo(activity.GetPackageName(), 0)
                    ?? throw new InvalidOperationException("The Android package metadata is unavailable.");
                var targetSdkVersion = appInfo.TargetSdkVersion;
                var versionCode = sdkVersion >= 28 ? packageInfo.GetLongVersionCode() : packageInfo.VersionCode;
                AndroidKeyboard.Init();

                SdkVersion = sdkVersion;
                TargetSdkVersion = targetSdkVersion;
                VersionCode = versionCode;
                IsInitialized = true;

                MajDebug.LogInfo(nameof(AndroidRuntime), $"SdkVersion: {SdkVersion}");
                MajDebug.LogInfo(nameof(AndroidRuntime), $"TargetSdkVersion: {TargetSdkVersion}");
                MajDebug.LogInfo(nameof(AndroidRuntime), $"VersionCode: {VersionCode}");

                MajDebug.LogInfo(nameof(AndroidRuntime), "Init finished");
            }
            catch (Exception ex)
            {
                MajDebug.LogError($"[{nameof(AndroidRuntime)}] Init failed: {ex}");
            }
        }

        /// <summary>Asks Android to use a display refresh rate that can present the requested frame rate.</summary>
        /// <param name="frameRate">
        /// The display refresh rate to request in frames per second. A value of zero or less asks for
        /// the fastest refresh rate the display offers.
        /// </param>
        /// <remarks>
        /// The request keeps the current display resolution and applies to the current activity's window
        /// and surface. It is posted to Android's UI thread and remains best effort: the system may refuse
        /// it for power, thermal, or vendor-specific reasons. This method does not change Unity's
        /// <see cref="Application.targetFrameRate"/>.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="frameRate"/> is NaN or infinite.</exception>
        /// <exception cref="InvalidOperationException">The Android activity has not been created, or JNI cannot allocate a reference or frame.</exception>
        /// <exception cref="PlatformNotSupportedException">Execution is outside an Android player.</exception>
        /// <exception cref="AndroidJavaException">Unity cannot resolve the Java helper class or its method.</exception>
        /// <exception cref="JavaInvocationException">Java rejects the request.</exception>
        public static void SetFrameRate(float frameRate)
        {
            if (float.IsNaN(frameRate) || float.IsInfinity(frameRate))
            {
                throw new ArgumentOutOfRangeException(nameof(frameRate), frameRate, "The frame rate must be a finite number.");
            }
            using var activity = CurrentActivity;
            MajDebug.LogInfo(nameof(AndroidRuntime), $"Requesting display refresh rate: {frameRate} Hz");
            SurfaceRefreshRate.Request(activity, frameRate);
            // OEM special
            RequestXiaomiRefreshRate((int)frameRate);
        }

        /// <summary>Requests a package-scoped refresh preference on Xiaomi, Redmi, and POCO devices.</summary>
        /// <param name="targetFrameRate">The Unity frame limit; nonpositive values use its mobile default of 30 FPS.</param>
        private static void RequestXiaomiRefreshRate(int targetFrameRate)
        {
            try
            {
                var manufacturer = Build.Manufacturer;
                if (!string.Equals(manufacturer, "Xiaomi", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(manufacturer, "Redmi", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(manufacturer, "POCO", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                using var activity = CurrentActivity;
                activity.RunOnUiThread(new AndroidJavaRunnable(() => ApplyXiaomiRefreshRate(targetFrameRate)));
            }
            catch (Exception ex)
            {
                using var javaException = ex as JavaInvocationException;
                MajDebug.LogWarning(nameof(AndroidRuntime), $"Xiaomi refresh rate preference unavailable: {ex.Message}");
            }
        }
        /// <summary>Applies the display mode and PowerKeeper override on Android's UI thread.</summary>
        /// <param name="targetFrameRate">The current Unity frame limit.</param>
        private static void ApplyXiaomiRefreshRate(int targetFrameRate)
        {
            try
            {
                using var activity = CurrentActivity;
                using var window = activity.GetWindow();
                using var windowManager = activity.GetWindowManager();
                using var display = windowManager?.GetDefaultDisplay();
                using var currentMode = display?.GetMode();
                if (window is null || display is null || currentMode is null)
                {
                    return;
                }

                var requestedRate = targetFrameRate > 0 ? targetFrameRate : 30;
                var width = currentMode.GetPhysicalWidth();
                var height = currentMode.GetPhysicalHeight();
                var bestModeId = currentMode.GetModeId();
                var bestRate = currentMode.GetRefreshRate();
                var bestDistance = Math.Abs(bestRate - requestedRate);
                var modes = display.GetSupportedModes();
                if (modes is not null)
                {
                    try
                    {
                        foreach (var mode in modes)
                        {
                            if (mode is null || mode.GetPhysicalWidth() != width || mode.GetPhysicalHeight() != height)
                            {
                                continue;
                            }

                            var rate = mode.GetRefreshRate();
                            var distance = Math.Abs(rate - requestedRate);
                            if (distance < bestDistance - 0.001f ||
                                (Math.Abs(distance - bestDistance) < 0.001f && rate > bestRate))
                            {
                                bestModeId = mode.GetModeId();
                                bestRate = rate;
                                bestDistance = distance;
                            }
                        }
                    }
                    finally
                    {
                        foreach (var mode in modes)
                        {
                            mode?.Dispose();
                        }
                    }
                }

                using var attributes = window.GetAttributes();
                if (attributes is null)
                {
                    return;
                }
                attributes.JavaReference.Set("preferredDisplayModeId", bestModeId);
                attributes.JavaReference.Set("preferredRefreshRate", (float)requestedRate);
                window.SetAttributes(attributes);

                // PowerKeeper's default 60 Hz game policy can override the window request.
                // Send the selected mode's rate so 144 Hz displays are not capped at 120 Hz.
                using var request = new Intent("com.xiaomi.joyose.OVERRIDE_GAME_FRESHRATE");
                request.SetPackage("com.miui.powerkeeper")?.Dispose();
                request.PutExtra("override_pkg_name", activity.GetPackageName())?.Dispose();
                request.PutExtra("override_freshrate", (int)Math.Round(bestRate))?.Dispose();
                activity.SendBroadcast(request);
            }
            catch (Exception ex)
            {
                using var javaException = ex as JavaInvocationException;
                MajDebug.LogWarning(nameof(AndroidRuntime), $"Xiaomi refresh rate preference unavailable: {ex.Message}");
            }
        }
        /// <summary>Forwards the borrowed Java intent to managed subscribers.</summary>
        /// <param name="intent">The intent borrowed for the callback duration.</param>
        private static void Android_OnNewIntent(AndroidJavaObject intent)
        {
            if (OnNewIntent is not null)
            {
                OnNewIntent(null, intent.ToWrapper<Intent>(ownsReference: false));
            }
        }
        /// <summary>Forwards an activity result to managed subscribers.</summary>
        /// <param name="requestCode">The request identifier supplied when launching the activity.</param>
        /// <param name="resultCode">The Android activity result status.</param>
        /// <param name="intent">The optional intent borrowed for the callback duration.</param>
        private static void Android_OnActivityResult(int requestCode, int resultCode, AndroidJavaObject? intent)
        {
            if (OnActivityResult is not null)
            {
                OnActivityResult(null, requestCode, resultCode, intent?.ToWrapper<Intent>(ownsReference: false));
            }
        }
        /// <summary>Adapts the production new-intent Java callback to the managed event.</summary>
        private sealed class OnNewIntentCallbackProxy : AndroidJavaProxy
        {
            /// <summary>Creates a proxy for the production Java callback interface.</summary>
            public OnNewIntentCallbackProxy() : base("net.majdata.majdataplay.CSharpOnNewIntentCallback")
            {
            }

            /// <summary>Receives new intents from Java without losing the method to managed stripping.</summary>
            /// <param name="intent">The intent borrowed for the callback duration.</param>
            [UnityEngine.Scripting.Preserve]
            public void OnNewIntent(AndroidJavaObject intent)
            {
                Android_OnNewIntent(intent);
            }
        }
        /// <summary>Adapts the production activity-result Java callback to the managed event.</summary>
        private sealed class OnActivityResultCallbackProxy : AndroidJavaProxy
        {
            /// <summary>Creates a proxy for the production Java callback interface.</summary>
            public OnActivityResultCallbackProxy() : base("net.majdata.majdataplay.CSharpOnActivityResultCallback")
            {
            }

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
