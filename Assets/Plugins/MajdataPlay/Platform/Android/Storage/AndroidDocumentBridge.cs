#nullable enable
using System;
using System.IO;
using System.Runtime.ExceptionServices;
using MajdataPlay.IO.Storage;
using UnityEngine;

namespace MajdataPlay.Platform.Android.Storage
{
    /// <summary>
    /// Marshals the independent SAF bridge's exact JNI protocol on scoped JVM-attached threads.
    /// </summary>
    internal static class AndroidDocumentBridge
    {
        /// <summary>Names the Java class without depending on AndroidRuntime initialization.</summary>
        internal const string ClassName = "net.majdata.majdataplay.StorageAccess";

        /// <summary>Names the typed Java result envelope.</summary>
        internal const string ResultClass = ClassName + "$Result";

        /// <summary>Names the immutable Java metadata record.</summary>
        internal const string EntryClass = ClassName + "$Entry";

        /// <summary>Names the caller-owned Java cursor handle.</summary>
        internal const string CursorClass = ClassName + "$CursorHandle";

        /// <summary>Names the caller-owned Java stream handle.</summary>
        internal const string StreamClass = ClassName + "$StreamHandle";

        /// <summary>Specifies the exact JVM result descriptor used by all bridge operations.</summary>
        internal const string ResultDescriptor = "Lnet/majdata/majdataplay/StorageAccess$Result;";

        /// <summary>Bounds managed and Java byte transfers to the same 32 KiB chunk size.</summary>
        internal const int MaxTransferSize = 32768;

        /// <summary>Rejects execution outside Android players without initializing Java.</summary>
        /// <exception cref="PlatformNotSupportedException">Execution is in the Editor or on another platform.</exception>
        internal static void EnsureAndroid()
        {
#if !UNITY_ANDROID || UNITY_EDITOR
            throw new PlatformNotSupportedException("The SAF storage backend requires an Android player.");
#endif
        }

        /// <summary>Attaches only when needed and preserves managed exceptions across the native callback.</summary>
        /// <typeparam name="T">The managed result type.</typeparam>
        /// <param name="operation">The synchronous JNI operation, including disposal of temporary Java references.</param>
        /// <returns>The operation's managed result after owned thread attachment has been released.</returns>
        /// <exception cref="PlatformNotSupportedException">Execution is outside an Android player.</exception>
        /// <exception cref="IOException">JNI fails outside the Java bridge's typed error protocol.</exception>
        /// <remarks>Typed storage and argument exceptions raised by the operation are propagated unchanged.</remarks>
        internal static T Invoke<T>(Func<T> operation)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var value = default(T)!;
            ExceptionDispatchInfo? failure = null;
            AndroidJNI.InvokeAttached(() =>
            {
                try
                {
                    value = operation();
                }
                catch (AndroidJavaException exception)
                {
                    failure = ExceptionDispatchInfo.Capture(new IOException("The SAF JNI bridge failed.", exception));
                }
                catch (JavaInvocationException exception)
                {
                    failure = ExceptionDispatchInfo.Capture(new IOException("The SAF JNI bridge failed outside its error protocol.", exception));
                }
                catch (Exception exception)
                {
                    failure = ExceptionDispatchInfo.Capture(exception);
                }
            });
            failure?.Throw();
            return value;
#else
            throw new PlatformNotSupportedException("The SAF storage backend requires an Android player.");
#endif
        }

        /// <summary>Calls a bridge method using an exact return and argument descriptor.</summary>
        /// <param name="instance">The cursor or stream handle, or null for a static operation.</param>
        /// <param name="method">The case-sensitive Java method name.</param>
        /// <param name="parameters">The JVM parameter descriptors without parentheses.</param>
        /// <param name="arguments">The arguments in declaration order; Java byte arrays must be sbyte arrays.</param>
        /// <returns>A caller-owned result envelope, to be disposed while attached.</returns>
        /// <exception cref="IOException">Java unexpectedly returns a null result.</exception>
        /// <exception cref="PlatformNotSupportedException">Execution is outside Android players.</exception>
        /// <exception cref="AndroidJavaException">The bridge method cannot be resolved.</exception>
        /// <exception cref="JavaInvocationException">JNI raises a Java exception outside the error protocol.</exception>
        internal static AndroidJavaObject Call(AndroidJavaObject? instance, string method, string parameters, params object?[] arguments)
        {
            return AndroidJni.Call<AndroidJavaObject?>(instance, ClassName, method,
                "(" + parameters + ")" + ResultDescriptor, arguments)
                ?? throw new IOException("The SAF bridge returned no result envelope.");
        }

        /// <summary>Reads a protocol field without inferring an object or array's JVM type.</summary>
        /// <typeparam name="T">The corresponding managed primitive, string, signed array, or Java reference type.</typeparam>
        /// <param name="instance">The Java record containing the field.</param>
        /// <param name="declaringClass">The exact binary name of the declaring Java class.</param>
        /// <param name="name">The case-sensitive field name.</param>
        /// <param name="descriptor">The exact JVM field descriptor.</param>
        /// <returns>The field value, preserving null object references.</returns>
        /// <exception cref="PlatformNotSupportedException">Execution is outside Android players.</exception>
        /// <exception cref="AndroidJavaException">JNI cannot resolve the field.</exception>
        /// <exception cref="JavaInvocationException">JNI raises a Java exception.</exception>
        internal static T Field<T>(AndroidJavaObject instance, string declaringClass, string name, string descriptor)
        {
            return AndroidJni.GetField<T>(instance, declaringClass, name, descriptor);
        }

        /// <summary>Reads a stable error code without parsing provider-dependent Java exception messages.</summary>
        /// <param name="result">The non-null result envelope.</param>
        /// <returns>Zero for success, or one of the bridge's documented storage failure codes.</returns>
        /// <exception cref="AndroidJavaException">JNI cannot resolve the error field.</exception>
        /// <exception cref="JavaInvocationException">JNI raises a Java exception.</exception>
        internal static int ErrorCode(AndroidJavaObject result)
        {
            return Field<int>(result, ResultClass, "errorCode", "I");
        }

        /// <summary>Translates typed Java errors into the corresponding .NET storage exceptions.</summary>
        /// <param name="result">The result envelope returned by one operation.</param>
        /// <param name="location">The opaque URI associated with the operation, if applicable.</param>
        /// <param name="directoryOperation">Whether a missing document should be reported as a missing directory.</param>
        /// <exception cref="ArgumentException">The Java bridge rejected an argument.</exception>
        /// <exception cref="FileNotFoundException">A required file is absent.</exception>
        /// <exception cref="DirectoryNotFoundException">A required directory is absent.</exception>
        /// <exception cref="UnauthorizedAccessException">Permission was denied or revoked.</exception>
        /// <exception cref="NotSupportedException">A provider capability or stream mode is unavailable.</exception>
        /// <exception cref="IOException">The provider failed or returned an unknown error code.</exception>
        internal static void CheckResult(AndroidJavaObject result, string? location = null, bool directoryOperation = false)
        {
            var code = ErrorCode(result);
            if (code == 0)
            {
                return;
            }
            var message = Field<string?>(result, ResultClass, "errorMessage", "Ljava/lang/String;")
                ?? "The SAF provider failed.";
            switch (code)
            {
                case 1:
                    throw new ArgumentException(message);
                case 2:
                    if (directoryOperation)
                    {
                        throw new DirectoryNotFoundException(message);
                    }
                    throw new FileNotFoundException(message, location);
                case 3:
                    throw new DirectoryNotFoundException(message);
                case 4:
                    throw new UnauthorizedAccessException(message);
                case 5:
                    throw new NotSupportedException(message);
                default:
                    throw new IOException(message);
            }
        }

        /// <summary>Copies nullable provider metadata into a managed, immutable entry.</summary>
        /// <param name="result">The successful result envelope containing metadata or cursor EOF.</param>
        /// <returns>The authoritative entry, or null for a missing lookup or cursor EOF.</returns>
        /// <exception cref="IOException">Required metadata is absent or a timestamp is out of range.</exception>
        /// <exception cref="AndroidJavaException">JNI cannot resolve metadata fields.</exception>
        /// <exception cref="JavaInvocationException">JNI raises a Java exception.</exception>
        internal static FileSystemEntry? ReadEntry(AndroidJavaObject result)
        {
            using var entry = Field<AndroidJavaObject?>(result, ResultClass, "entry",
                "Lnet/majdata/majdataplay/StorageAccess$Entry;");
            if (entry is null)
            {
                return null;
            }
            var uri = Field<string?>(entry, EntryClass, "uri", "Ljava/lang/String;");
            var name = Field<string?>(entry, EntryClass, "name", "Ljava/lang/String;");
            var resourceId = Field<string?>(entry, EntryClass, "resourceId", "Ljava/lang/String;");
            if (string.IsNullOrEmpty(uri) || string.IsNullOrEmpty(resourceId) || name is null)
            {
                throw new IOException("The SAF provider omitted a document URI, resource identity, or display name.");
            }
            var directory = Field<bool>(entry, EntryClass, "directory", "Z");
            long? length = null;
            if (!directory && Field<bool>(entry, EntryClass, "hasSize", "Z"))
            {
                var size = Field<long>(entry, EntryClass, "size", "J");
                if (size < 0)
                {
                    throw new IOException("The SAF provider returned a negative file size.");
                }
                length = size;
            }
            DateTime? modified = null;
            if (Field<bool>(entry, EntryClass, "hasModified", "Z"))
            {
                var milliseconds = Field<long>(entry, EntryClass, "modified", "J");
                try
                {
                    modified = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
                }
                catch (ArgumentOutOfRangeException exception)
                {
                    throw new IOException("The SAF provider returned an out-of-range modification time.", exception);
                }
            }
            return new FileSystemEntry(uri, name, directory, length, modified, resourceId: resourceId);
        }

        /// <summary>Reads metadata for an operation which must return an existing document.</summary>
        /// <param name="result">The successful result envelope.</param>
        /// <returns>The authoritative managed metadata.</returns>
        /// <exception cref="IOException">The result omitted an entry or contains invalid metadata.</exception>
        internal static FileSystemEntry ReadRequiredEntry(AndroidJavaObject result)
        {
            return ReadEntry(result) ?? throw new IOException("The SAF operation returned no document metadata.");
        }

        /// <summary>Closes a cursor or stream and always releases its managed global Java reference.</summary>
        /// <param name="handle">The caller-owned handle to close exactly once.</param>
        /// <exception cref="PlatformNotSupportedException">Execution is outside an Android player.</exception>
        /// <exception cref="UnauthorizedAccessException">The provider denied the close operation.</exception>
        /// <exception cref="IOException">Closing or flushing the provider resource failed.</exception>
        internal static void CloseHandle(AndroidJavaObject handle)
        {
            Invoke(() =>
            {
                try
                {
                    using var result = Call(handle, "close", "");
                    CheckResult(result);
                }
                finally
                {
                    handle.Dispose();
                }
                return true;
            });
        }
    }
}
