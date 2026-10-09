using System;
using System.Threading;
using UnityEngine;

#nullable enable

namespace MajdataPlay.Platform.Android.Runtime.Java.Lang
{
    /// <summary>
    /// Owns or borrows a Unity Java-object reference used by generated JNI wrappers.
    /// </summary>
    /// <remarks>
    /// Dispose owned wrappers deterministically on the Unity main thread or a JVM-attached thread.
    /// Borrowed wrappers do not extend the lifetime of an externally disposed reference. Generated
    /// wrappers represent Java interfaces as well as classes; they do not implement Java callbacks.
    /// </remarks>
    public abstract class JavaObject : IDisposable
    {
        /// <summary>
        /// Stores the Java reference, or null after this wrapper has been disposed.
        /// </summary>
        private AndroidJavaObject? _javaObject;

        /// <summary>
        /// Indicates whether disposing the wrapper must dispose its Java reference.
        /// </summary>
        private readonly bool _ownsReference;

        /// <summary>
        /// Gets the live Unity Java reference used for JNI calls and argument marshaling.
        /// </summary>
        /// <exception cref="ObjectDisposedException">This wrapper has been disposed.</exception>
        public AndroidJavaObject JavaReference
        {
            get
            {
                return Volatile.Read(ref _javaObject)
                    ?? throw new ObjectDisposedException(GetType().FullName);
            }
        }

        /// <summary>
        /// Initializes a wrapper around an existing Unity Java reference.
        /// </summary>
        /// <param name="javaObject">The non-null Java reference to wrap.</param>
        /// <param name="ownsReference">Whether to adopt and dispose the reference, rather than borrow it.</param>
        /// <exception cref="ArgumentNullException">The Java reference is null.</exception>
        protected JavaObject(AndroidJavaObject javaObject, bool ownsReference = true)
        {
            _javaObject = javaObject ?? throw new ArgumentNullException(nameof(javaObject));
            _ownsReference = ownsReference;
        }

        /// <summary>
        /// Creates a Java instance using an exact constructor descriptor and adopts its reference.
        /// </summary>
        /// <param name="className">The fully qualified Java binary class name.</param>
        /// <param name="constructorSignature">The exact JVM constructor descriptor, ending in <c>V</c>.</param>
        /// <param name="arguments">The constructor arguments in their Java declaration order.</param>
        /// <exception cref="PlatformNotSupportedException">Execution is not in an Android player.</exception>
        /// <exception cref="ArgumentException">The descriptor or arguments are invalid.</exception>
        /// <exception cref="AndroidJavaException">Java class or constructor resolution fails.</exception>
        /// <exception cref="JavaInvocationException">Java raises an exception during construction.</exception>
        /// <exception cref="InvalidOperationException">JNI cannot create a local frame or returns a null instance without an exception.</exception>
        protected JavaObject(string className, string constructorSignature, params object?[] arguments)
        {
            _javaObject = AndroidJni.Construct(className, constructorSignature, arguments);
            _ownsReference = true;
        }

        /// <summary>
        /// Releases an owned reference once, or stops borrowing an externally owned reference.
        /// </summary>
        /// <remarks>
        /// Other code must not dispose an adopted <see cref="JavaReference"/> independently, and callers
        /// must not race JNI operations against disposal. A borrowed reference remains caller-owned.
        /// </remarks>
        public void Dispose()
        {
            var javaObject = Interlocked.Exchange(ref _javaObject, null);
            if (_ownsReference && javaObject is not null)
            {
                javaObject.Dispose();
            }

            GC.SuppressFinalize(this);
        }
    }
}
