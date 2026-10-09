using System;
using UnityEngine;
using Runnable = MajdataPlay.Platform.Android.Runtime.Java.Lang.Runnable;

#nullable enable

namespace MajdataPlay.Platform.Android.Runtime.App
{
    /// <summary>Launches Android activities and exposes the inherited content resolver API.</summary>
    [JavaClass("android.app.Activity", ApiLevel = 36, IncludeInheritedMembers = true)]
    public partial class Activity
    {
        /// <summary>Schedules a managed callback through Unity's Java Runnable proxy.</summary>
        /// <param name="callback">The callback to execute on Android's UI thread.</param>
        /// <remarks>
        /// Unity's proxy retains the callback while Java holds the queued Runnable. The generated
        /// interface wrapper only borrows a temporary global reference; it is not the callback proxy.
        /// Invoke this overload on Unity's Android main thread or a JVM-attached thread.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The callback is null.</exception>
        /// <exception cref="PlatformNotSupportedException">Execution is not in an Android player.</exception>
        /// <exception cref="ObjectDisposedException">The activity wrapper has been disposed.</exception>
        /// <exception cref="AndroidJavaException">Unity cannot create the proxy or resolve the method.</exception>
        /// <exception cref="JavaInvocationException">Java rejects scheduling the callback.</exception>
        /// <exception cref="InvalidOperationException">Unity cannot create the proxy or JNI cannot allocate a local frame.</exception>
        public void RunOnUiThread(AndroidJavaRunnable callback)
        {
            if (callback is null)
            {
                throw new ArgumentNullException(nameof(callback));
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            var localReference = AndroidJNIHelper.CreateJavaRunnable(callback);
            try
            {
                if (localReference == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Unity could not create an Android Runnable proxy.");
                }
                using var javaReference = new AndroidJavaObject(localReference);
                using var runnable = new Runnable(javaReference, ownsReference: false);
                RunOnUiThread(runnable);
            }
            finally
            {
                AndroidJNI.DeleteLocalRef(localReference);
            }
#else
            throw new PlatformNotSupportedException("Scheduling Android UI callbacks requires an Android player.");
#endif
        }
    }
}
