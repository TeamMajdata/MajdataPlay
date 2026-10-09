#nullable enable
using System;
using System.IO;
using System.Runtime.ExceptionServices;
using MajdataPlay.IO.Storage;
using MajdataPlay.Platform.Android.Runtime.Java.Lang;
using MajdataPlay.Platform.Android.Runtime.Storage;
using UnityEngine;

namespace MajdataPlay.Platform.Android.Storage
{
    /// <summary>
    /// Converts generated SAF wrapper results on scoped JVM-attached threads.
    /// </summary>
    internal static class AndroidDocumentBridge
    {
        /// <summary>Bounds managed transfers to the Java bridge's generated chunk-size constant.</summary>
        internal const int MaxTransferSize = StorageAccess.MaxTransferSize;

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

        /// <summary>Rejects a missing envelope without changing typed provider-error handling.</summary>
        /// <param name="result">The caller-owned envelope returned by a generated bridge method.</param>
        /// <returns>The same non-null envelope, retaining ownership with the caller.</returns>
        /// <exception cref="IOException">Java unexpectedly returned a null result.</exception>
        internal static StorageResult RequireResult(StorageResult? result)
        {
            return result ?? throw new IOException("The SAF bridge returned no result envelope.");
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
        internal static void CheckResult(StorageResult result, string? location = null, bool directoryOperation = false)
        {
            var code = result.ErrorCode;
            if (code == StorageAccess.Success)
            {
                return;
            }
            var message = result.ErrorMessage
                ?? "The SAF provider failed.";
            switch (code)
            {
                case StorageAccess.InvalidArgument:
                    throw new ArgumentException(message);
                case StorageAccess.NotFound:
                    if (directoryOperation)
                    {
                        throw new DirectoryNotFoundException(message);
                    }
                    throw new FileNotFoundException(message, location);
                case StorageAccess.DirectoryNotFound:
                    throw new DirectoryNotFoundException(message);
                case StorageAccess.AccessDenied:
                    throw new UnauthorizedAccessException(message);
                case StorageAccess.Unsupported:
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
        internal static FileSystemEntry? ReadEntry(StorageResult result)
        {
            using var entry = result.Entry;
            if (entry is null)
            {
                return null;
            }
            var uri = entry.Uri;
            var name = entry.Name;
            var resourceId = entry.ResourceId;
            if (string.IsNullOrEmpty(uri) || string.IsNullOrEmpty(resourceId) || name is null)
            {
                throw new IOException("The SAF provider omitted a document URI, resource identity, or display name.");
            }
            var directory = entry.Directory;
            long? length = null;
            if (!directory && entry.HasSize)
            {
                var size = entry.Size;
                if (size < 0)
                {
                    throw new IOException("The SAF provider returned a negative file size.");
                }
                length = size;
            }
            DateTime? modified = null;
            if (entry.HasModified)
            {
                var milliseconds = entry.Modified;
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
        internal static FileSystemEntry ReadRequiredEntry(StorageResult result)
        {
            return ReadEntry(result) ?? throw new IOException("The SAF operation returned no document metadata.");
        }

        /// <summary>Closes a provider cursor and always releases its owned Java reference.</summary>
        /// <param name="handle">The caller-owned cursor to close exactly once.</param>
        /// <exception cref="PlatformNotSupportedException">Execution is outside an Android player.</exception>
        /// <exception cref="UnauthorizedAccessException">The provider denied the close operation.</exception>
        /// <exception cref="IOException">Closing the provider cursor failed.</exception>
        internal static void CloseHandle(DocumentCursor handle)
        {
            CloseHandle(handle, handle.Close);
        }

        /// <summary>Closes a provider stream and always releases its owned Java reference.</summary>
        /// <param name="handle">The caller-owned stream to close exactly once.</param>
        /// <exception cref="PlatformNotSupportedException">Execution is outside an Android player.</exception>
        /// <exception cref="UnauthorizedAccessException">The provider denied the close operation.</exception>
        /// <exception cref="IOException">Closing or flushing the provider stream failed.</exception>
        internal static void CloseHandle(DocumentStream handle)
        {
            CloseHandle(handle, handle.Close);
        }

        /// <summary>Checks a typed close result and releases the wrapper even when closing fails.</summary>
        /// <param name="handle">The wrapper owning the Java resource reference.</param>
        /// <param name="close">The generated Java operation that closes the provider resource.</param>
        /// <exception cref="PlatformNotSupportedException">Execution is outside an Android player.</exception>
        /// <exception cref="UnauthorizedAccessException">The provider denied closing the resource.</exception>
        /// <exception cref="IOException">Closing the provider resource or invoking JNI failed.</exception>
        private static void CloseHandle(JavaObject handle, Func<StorageResult?> close)
        {
            Invoke(() =>
            {
                try
                {
                    using var result = RequireResult(close());
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
