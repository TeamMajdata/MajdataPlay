#nullable enable
using System;
#if UNITY_ANDROID && !UNITY_EDITOR
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using MajdataPlay.Platform.Android.Runtime.Bindings.Com.Unity3d.Player;
using UnityEngine;
#endif

namespace MajdataPlay.FFmpeg.Interop
{
    /// <summary>Registers the Android application context required by FFmpeg's native content protocol.</summary>
    internal static class AndroidContentInterop
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>Serializes application-context registration across concurrent decoder sessions.</summary>
        private static readonly object s_initializationLock = new object();
#endif

        /// <summary>Initializes FFmpeg's Java VM and application context without requiring a graphics bridge.</summary>
        /// <remarks>
        /// Reuses the native context after managed domain reloads. A newly registered JNI global reference
        /// deliberately survives until process exit because FFmpeg only borrows it. JNI work runs within
        /// scoped thread attachment; generated JavaInvocationException failures are propagated unchanged.
        /// </remarks>
        /// <exception cref="PlatformNotSupportedException">Execution is outside an Android player.</exception>
        /// <exception cref="NotSupportedException">The Java VM is unavailable or FFmpeg rejects registration.</exception>
        /// <exception cref="InvalidOperationException">The activity, application context, or JNI global reference is unavailable.</exception>
        /// <exception cref="UnityEngine.AndroidJavaException">Java lookup or JNI reference creation fails.</exception>
        /// <exception cref="DllNotFoundException">The native avcodec library is unavailable.</exception>
        /// <exception cref="EntryPointNotFoundException">The native avcodec library lacks a required JNI entry point.</exception>
        /// <exception cref="BadImageFormatException">The native avcodec library has an incompatible architecture.</exception>
        internal static void Initialize()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            lock (s_initializationLock)
            {
                VulkanVideoInterop.InitializeAndroid();
                if (Native.AvJniGetAndroidAppContext() != IntPtr.Zero)
                {
                    return;
                }

                ExceptionDispatchInfo? failure = null;
                AndroidJNI.InvokeAttached(() =>
                {
                    try
                    {
                        RegisterApplicationContext();
                    }
                    catch (Exception exception)
                    {
                        // Do not allow managed exceptions to cross the native attachment callback.
                        failure = ExceptionDispatchInfo.Capture(exception);
                    }
                });
                failure?.Throw();
            }
#else
            throw new PlatformNotSupportedException("FFmpeg content initialization requires an Android player.");
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>Registers an independently retained application context on a JVM-attached thread.</summary>
        /// <remarks>The caller holds the initialization lock and has already registered FFmpeg's Java VM.</remarks>
        /// <exception cref="InvalidOperationException">The activity, application context, or JNI global reference is unavailable.</exception>
        /// <exception cref="AndroidJavaException">Java lookup or JNI reference creation fails.</exception>
        /// <exception cref="MajdataPlay.Platform.Android.Runtime.JavaInvocationException">A generated Java wrapper invocation fails.</exception>
        /// <exception cref="NotSupportedException">FFmpeg rejects the application-context global reference.</exception>
        /// <exception cref="DllNotFoundException">The native avcodec library is unavailable.</exception>
        /// <exception cref="EntryPointNotFoundException">The native avcodec library lacks the context setter.</exception>
        /// <exception cref="BadImageFormatException">The native avcodec library has an incompatible architecture.</exception>
        private static void RegisterApplicationContext()
        {
            using var activity = UnityPlayer.CurrentActivity
                ?? throw new InvalidOperationException("The Unity Android activity is unavailable.");
            using var applicationContext = activity.GetApplicationContext()
                ?? throw new InvalidOperationException("The Android application context is unavailable.");

            var globalReference = AndroidJNI.NewGlobalRef(applicationContext.JavaReference.GetRawObject());
            if (globalReference == IntPtr.Zero)
            {
                throw new InvalidOperationException("Could not retain the Android application context.");
            }

            try
            {
                var result = Native.AvJniSetAndroidAppContext(globalReference, IntPtr.Zero);
                if (result < 0)
                {
                    throw new NotSupportedException("Could not initialize FFmpeg's Android application context (" + result + ").");
                }

                // FFmpeg stores a borrowed pointer, not its own global reference. Retain this reference
                // for the process lifetime; subsequent initialization reuses it through the native getter.
                globalReference = IntPtr.Zero;
            }
            finally
            {
                if (globalReference != IntPtr.Zero)
                {
                    AndroidJNI.DeleteGlobalRef(globalReference);
                }
            }
        }

        /// <summary>Imports libavcodec's Android application-context registration API independently of graphics interop.</summary>
        private static class Native
        {
            /// <summary>Registers a borrowed JNI global reference to the Android application context.</summary>
            /// <param name="applicationContext">The global reference retained by the caller for the process lifetime.</param>
            /// <param name="logContext">An optional native logging context, or zero when none is supplied.</param>
            /// <returns>Zero on success, or a negative FFmpeg error code.</returns>
            /// <exception cref="DllNotFoundException">The native avcodec library is unavailable.</exception>
            /// <exception cref="EntryPointNotFoundException">The native avcodec library lacks the context setter.</exception>
            /// <exception cref="BadImageFormatException">The native avcodec library has an incompatible architecture.</exception>
            [DllImport("avcodec", CallingConvention = CallingConvention.Cdecl, EntryPoint = "av_jni_set_android_app_ctx")]
            internal static extern int AvJniSetAndroidAppContext(IntPtr applicationContext, IntPtr logContext);

            /// <summary>Gets the borrowed JNI global reference already registered with FFmpeg.</summary>
            /// <returns>The registered application-context reference, or zero when no context has been registered.</returns>
            /// <exception cref="DllNotFoundException">The native avcodec library is unavailable.</exception>
            /// <exception cref="EntryPointNotFoundException">The native avcodec library lacks the context getter.</exception>
            /// <exception cref="BadImageFormatException">The native avcodec library has an incompatible architecture.</exception>
            [DllImport("avcodec", CallingConvention = CallingConvention.Cdecl, EntryPoint = "av_jni_get_android_app_ctx")]
            internal static extern IntPtr AvJniGetAndroidAppContext();
        }
#endif
    }
}
