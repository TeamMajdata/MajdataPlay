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
    /// This type mirrors <c>java.lang.Object</c>: it is directly constructible, and its parameterless
    /// constructor creates a new <c>java.lang.Object</c> instance.
    /// </remarks>
    public class JavaObject : IDisposable
    {
        /// <summary>
        /// Identifies the Java class created by the parameterless constructor.
        /// </summary>
        private const string ObjectClassName = "java.lang.Object";

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
        /// Creates a wrapper around a new Java <c>java.lang.Object</c> instance.
        /// </summary>
        /// <remarks>
        /// Equivalent to Java's <c>new Object()</c>, and to
        /// <see cref="JavaObject(string, string, object?[])"/> with the class name <c>java.lang.Object</c>
        /// and the constructor signature <c>()V</c>. Use it only when a plain Java object is required;
        /// a wrapper created this way is not an instance of any more specific Java type.
        /// </remarks>
        /// <exception cref="PlatformNotSupportedException">Execution is not in an Android player.</exception>
        /// <exception cref="AndroidJavaException">Java class or constructor resolution fails.</exception>
        /// <exception cref="JavaInvocationException">Java raises an exception during construction.</exception>
        /// <exception cref="InvalidOperationException">JNI cannot create a local frame or returns a null instance without an exception.</exception>
        public JavaObject()
            : this(ObjectClassName, "()V")
        {
        }

        /// <summary>
        /// Initializes a wrapper around an existing Unity Java reference.
        /// </summary>
        /// <param name="javaObject">The non-null Java reference to wrap.</param>
        /// <param name="ownsReference">Whether to adopt and dispose the reference, rather than borrow it.</param>
        /// <exception cref="ArgumentNullException">The Java reference is null.</exception>
        public JavaObject(AndroidJavaObject javaObject, bool ownsReference = true)
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
        public JavaObject(string className, string constructorSignature, params object?[] arguments)
        {
            _javaObject = AndroidJni.Construct(className, constructorSignature, arguments);
            _ownsReference = true;
        }

        /// <summary>
        /// Compares this wrapper with another wrapper using the Java object's virtual <c>equals</c> method.
        /// </summary>
        /// <param name="obj">The object to compare with this wrapper.</param>
        /// <returns>True for the same managed wrapper, or when Java considers two distinct wrappers equal; otherwise false.</returns>
        /// <remarks>
        /// Managed reference identity and null or non-wrapper comparisons do not invoke JNI, even after
        /// disposal. Comparing distinct wrappers requires both references to remain live on the Unity
        /// main thread or a JVM-attached thread.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">Either distinct wrapper has been disposed.</exception>
        /// <exception cref="PlatformNotSupportedException">A Java comparison is required outside an Android player.</exception>
        /// <exception cref="AndroidJavaException">Java method lookup or invocation fails.</exception>
        /// <exception cref="JavaInvocationException">Java raises an exception during the comparison.</exception>
        /// <exception cref="InvalidOperationException">JNI cannot create a local frame.</exception>
        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj is not JavaObject other)
            {
                return false;
            }

            var javaObject = JavaReference;
            var otherJavaObject = other.JavaReference;
            return AndroidJni.Call<bool>(javaObject, ObjectClassName, "equals", "(Ljava/lang/Object;)Z", otherJavaObject);
        }

        /// <summary>
        /// Gets the hash code returned by the Java object's virtual <c>hashCode</c> method.
        /// </summary>
        /// <returns>The wrapped Java object's hash code.</returns>
        /// <exception cref="ObjectDisposedException">This wrapper has been disposed.</exception>
        /// <exception cref="PlatformNotSupportedException">Execution is not in an Android player.</exception>
        /// <exception cref="AndroidJavaException">Java method lookup or invocation fails.</exception>
        /// <exception cref="JavaInvocationException">Java raises an exception while computing the hash code.</exception>
        /// <exception cref="InvalidOperationException">JNI cannot create a local frame.</exception>
        public override int GetHashCode()
        {
            return AndroidJni.Call<int>(JavaReference, ObjectClassName, "hashCode", "()I");
        }

        /// <summary>
        /// Gets the string returned by the Java object's virtual <c>toString</c> method.
        /// </summary>
        /// <returns>The wrapped Java object's string representation, or null if Java returns null.</returns>
        /// <exception cref="ObjectDisposedException">This wrapper has been disposed.</exception>
        /// <exception cref="PlatformNotSupportedException">Execution is not in an Android player.</exception>
        /// <exception cref="AndroidJavaException">Java method lookup or invocation fails.</exception>
        /// <exception cref="JavaInvocationException">Java raises an exception while creating the string representation.</exception>
        /// <exception cref="InvalidOperationException">JNI cannot create a local frame.</exception>
        public override string? ToString()
        {
            return AndroidJni.Call<string?>(JavaReference, ObjectClassName, "toString", "()Ljava/lang/String;");
        }

        /// <summary>
        /// Compares two nullable wrappers using managed identity, null checks, and virtual Java equality.
        /// </summary>
        /// <param name="left">The first wrapper, or null.</param>
        /// <param name="right">The second wrapper, or null.</param>
        /// <returns>True when both operands are null, are the same managed wrapper, or compare equal; otherwise false.</returns>
        /// <exception cref="ObjectDisposedException">Either distinct non-null wrapper has been disposed.</exception>
        /// <exception cref="PlatformNotSupportedException">A Java comparison is required outside an Android player.</exception>
        /// <exception cref="AndroidJavaException">Java method lookup or invocation fails.</exception>
        /// <exception cref="JavaInvocationException">Java raises an exception during the comparison.</exception>
        /// <exception cref="InvalidOperationException">JNI cannot create a local frame.</exception>
        public static bool operator ==(JavaObject? left, JavaObject? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            return left is not null && right is not null && left.Equals(right);
        }

        /// <summary>
        /// Determines whether two nullable wrappers differ according to the equality operator.
        /// </summary>
        /// <param name="left">The first wrapper, or null.</param>
        /// <param name="right">The second wrapper, or null.</param>
        /// <returns>True when the equality operator returns false; otherwise false.</returns>
        /// <exception cref="ObjectDisposedException">Either distinct non-null wrapper has been disposed.</exception>
        /// <exception cref="PlatformNotSupportedException">A Java comparison is required outside an Android player.</exception>
        /// <exception cref="AndroidJavaException">Java method lookup or invocation fails.</exception>
        /// <exception cref="JavaInvocationException">Java raises an exception during the comparison.</exception>
        /// <exception cref="InvalidOperationException">JNI cannot create a local frame.</exception>
        public static bool operator !=(JavaObject? left, JavaObject? right)
        {
            return !(left == right);
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
