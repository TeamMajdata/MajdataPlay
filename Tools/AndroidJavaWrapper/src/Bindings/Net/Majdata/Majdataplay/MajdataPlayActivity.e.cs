using System;
using UnityEngine;

#nullable enable

namespace MajdataPlay.Platform.Android.Runtime.Bindings.Net.Majdata.Majdataplay
{
    /// <summary>Connects Unity's managed callback proxies to the generated activity registration methods.</summary>
    public partial class MajdataPlayActivity
    {
        /// <summary>Registers a managed proxy for new intents through the generated Java interface binding.</summary>
        /// <param name="callback">The managed proxy implementing the Java new-intent callback interface.</param>
        /// <remarks>
        /// The caller must retain the managed proxy while Java uses it. Temporary JNI references
        /// are released after registration; Java retains the registered callback. Invoke this
        /// overload on Unity's Android main thread or a JVM-attached thread.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The callback is null.</exception>
        /// <exception cref="ArgumentException">The proxy does not implement the required Java interface.</exception>
        /// <exception cref="PlatformNotSupportedException">Execution is not in an Android player.</exception>
        /// <exception cref="AndroidJavaException">Unity cannot create the proxy or resolve its Java interface.</exception>
        /// <exception cref="JavaInvocationException">Java rejects creating or registering the callback.</exception>
        /// <exception cref="InvalidOperationException">Unity cannot create the proxy or JNI cannot allocate a reference or frame.</exception>
        public static void RegisterOnNewIntentCallback(AndroidJavaProxy callback)
        {
            if (callback is null)
            {
                throw new ArgumentNullException(nameof(callback));
            }
            AndroidJni.EnsureAndroid();
            var localReference = AndroidJNIHelper.CreateJavaProxy(callback);
            try
            {
                if (localReference == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Unity could not create an Android new-intent callback proxy.");
                }
                using var javaReference = new AndroidJavaObject(localReference);
                using var callbackReference = new CSharpOnNewIntentCallback(javaReference, ownsReference: false);
                RegisterOnNewIntentCallback(callbackReference);
            }
            finally
            {
                AndroidJNI.DeleteLocalRef(localReference);
            }
        }

        /// <summary>Registers a managed proxy for activity results through the generated Java interface binding.</summary>
        /// <param name="callback">The managed proxy implementing the Java activity-result callback interface.</param>
        /// <remarks>
        /// The caller must retain the managed proxy while Java uses it. Temporary JNI references
        /// are released after registration; Java retains the registered callback. Invoke this
        /// overload on Unity's Android main thread or a JVM-attached thread.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The callback is null.</exception>
        /// <exception cref="ArgumentException">The proxy does not implement the required Java interface.</exception>
        /// <exception cref="PlatformNotSupportedException">Execution is not in an Android player.</exception>
        /// <exception cref="AndroidJavaException">Unity cannot create the proxy or resolve its Java interface.</exception>
        /// <exception cref="JavaInvocationException">Java rejects creating or registering the callback.</exception>
        /// <exception cref="InvalidOperationException">Unity cannot create the proxy or JNI cannot allocate a reference or frame.</exception>
        public static void RegisterOnActivityResultCallback(AndroidJavaProxy callback)
        {
            if (callback is null)
            {
                throw new ArgumentNullException(nameof(callback));
            }
            AndroidJni.EnsureAndroid();
            var localReference = AndroidJNIHelper.CreateJavaProxy(callback);
            try
            {
                if (localReference == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Unity could not create an Android activity-result callback proxy.");
                }
                using var javaReference = new AndroidJavaObject(localReference);
                using var callbackReference = new CSharpOnActivityResultCallback(javaReference, ownsReference: false);
                RegisterOnActivityResultCallback(callbackReference);
            }
            finally
            {
                AndroidJNI.DeleteLocalRef(localReference);
            }
        }

        /// <summary>Registers a managed proxy for dispatched keys through the generated Java interface binding.</summary>
        /// <param name="callback">The managed proxy implementing the Java key-dispatch callback interface.</param>
        /// <remarks>
        /// The caller must retain the managed proxy while Java uses it. Temporary JNI references
        /// are released after registration; Java retains the registered callback. Invoke this
        /// overload on Unity's Android main thread or a JVM-attached thread.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The callback is null.</exception>
        /// <exception cref="ArgumentException">The proxy does not implement the required Java interface.</exception>
        /// <exception cref="PlatformNotSupportedException">Execution is not in an Android player.</exception>
        /// <exception cref="AndroidJavaException">Unity cannot create the proxy or resolve its Java interface.</exception>
        /// <exception cref="JavaInvocationException">Java rejects creating or registering the callback.</exception>
        /// <exception cref="InvalidOperationException">Unity cannot create the proxy or JNI cannot allocate a reference or frame.</exception>
        public static void RegisterDispatchKeyEventCallback(AndroidJavaProxy callback)
        {
            if (callback is null)
            {
                throw new ArgumentNullException(nameof(callback));
            }
            AndroidJni.EnsureAndroid();
            var localReference = AndroidJNIHelper.CreateJavaProxy(callback);
            try
            {
                if (localReference == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Unity could not create an Android key-dispatch callback proxy.");
                }
                using var javaReference = new AndroidJavaObject(localReference);
                using var callbackReference = new CSharpOnDispatchKeyEventCallback(javaReference, ownsReference: false);
                RegisterDispatchKeyEventCallback(callbackReference);
            }
            finally
            {
                AndroidJNI.DeleteLocalRef(localReference);
            }
        }
    }
}
