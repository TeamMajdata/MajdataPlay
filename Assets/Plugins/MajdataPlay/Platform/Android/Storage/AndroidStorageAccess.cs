#nullable enable
using MajdataPlay.IO.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MajdataPlay.Platform.Android.Storage
{
    /// <summary>Launches Storage Access Framework pickers and manages persisted URI grants.</summary>
    /// <remarks>
    /// Start pickers on Unity's main thread and propagate an owner-lifetime cancellation token.
    /// Cancellation is cooperative: result commitment can win concurrent cancellation to avoid hiding a persisted grant.
    /// Cancellation cannot close Android's picker; dismiss it before starting another.
    /// Grant queries and file operations may run on worker threads. No broad storage permission is requested.
    /// </remarks>
    public static class AndroidStorageAccess
    {
        /// <summary>Protects the request shared by Unity, Android's UI thread, and cancellation callbacks.</summary>
        private static readonly object s_gate = new object();

        /// <summary>Stores the open picker, including a cancelled but not yet dismissed picker.</summary>
        private static PickerRequest? s_pending;

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>Stores Unity's main thread ID after player initialization.</summary>
        private static int s_mainThreadId;

        /// <summary>Allocates request codes reserved for this component.</summary>
        private static int s_nextRequestCode = 0x4D00;
#endif

        /// <summary>Requests read access to a URI.</summary>
        private const int GrantRead = 1;

        /// <summary>Requests write access to a URI.</summary>
        private const int GrantWrite = 2;

        /// <summary>Requests a grant persistable across process restarts.</summary>
        private const int GrantPersistable = 64;

        /// <summary>Requests access to descendants of a selected tree.</summary>
        private const int GrantPrefix = 128;

        /// <summary>Lets the user choose a document tree.</summary>
        /// <param name="writable">Whether read and write access are required.</param>
        /// <param name="persistPermission">Whether the grant must be persisted before completing.</param>
        /// <param name="cancellationToken">Cancels the request when its owner is destroyed or closes.</param>
        /// <returns>The selected directory, or null on user dismissal.</returns>
        /// <exception cref="PlatformNotSupportedException">Execution is not in an Android player.</exception>
        /// <exception cref="InvalidOperationException">The caller is not on the main thread or a picker is already open.</exception>
        /// <exception cref="OperationCanceledException">The request is cancelled or the application quits.</exception>
        /// <exception cref="UnauthorizedAccessException">The required grant is unavailable.</exception>
        /// <exception cref="NotSupportedException">The picker or persistable access is unavailable.</exception>
        /// <exception cref="IOException">The result or picker launch fails.</exception>
        public static async Task<StorageDirectory?> PickDirectoryAsync(bool writable = true,
            bool persistPermission = true, CancellationToken cancellationToken = default)
        {
            var location = await StartPicker("android.intent.action.OPEN_DOCUMENT_TREE", null, null,
                writable, persistPermission, true, cancellationToken).ConfigureAwait(false);
            if (location is null)
            {
                return null;
            }
            return new StorageDirectory(new AndroidDocumentFileSystem(), location);
        }

        /// <summary>Lets the user choose a single binary document.</summary>
        /// <param name="mimeType">The MIME filter, such as text/plain, application/zip, or */*.</param>
        /// <param name="writable">Whether read and write access are required.</param>
        /// <param name="persistPermission">Whether the grant must be persisted before completing.</param>
        /// <param name="cancellationToken">Cancels the request with its owner's lifetime.</param>
        /// <returns>The selected file, or null on user dismissal.</returns>
        /// <exception cref="ArgumentException">The MIME type is malformed.</exception>
        /// <exception cref="PlatformNotSupportedException">Execution is not in an Android player.</exception>
        /// <exception cref="InvalidOperationException">The caller is not on the main thread or a picker is already open.</exception>
        /// <exception cref="OperationCanceledException">The request is cancelled.</exception>
        /// <exception cref="UnauthorizedAccessException">The required grant is unavailable.</exception>
        /// <exception cref="NotSupportedException">The picker or persistable access is unavailable.</exception>
        /// <exception cref="IOException">The result or launch fails.</exception>
        public static async Task<StorageFile?> PickFileAsync(string mimeType = "*/*", bool writable = false,
            bool persistPermission = true, CancellationToken cancellationToken = default)
        {
            ValidateMimeType(mimeType);
            var location = await StartPicker("android.intent.action.OPEN_DOCUMENT", mimeType, null,
                writable, persistPermission, false, cancellationToken).ConfigureAwait(false);
            if (location is null)
            {
                return null;
            }
            return new StorageFile(new AndroidDocumentFileSystem(), location);
        }

        /// <summary>Lets the user choose where to create a new document with Android's save picker.</summary>
        /// <param name="name">The proposed single filename; Android may choose another name.</param>
        /// <param name="mimeType">The content MIME type.</param>
        /// <param name="persistPermission">Whether the returned grant must be persisted.</param>
        /// <param name="cancellationToken">Cancels the request with its owner's lifetime.</param>
        /// <returns>The created file, or null on user dismissal.</returns>
        /// <exception cref="ArgumentException">The name or MIME type is invalid.</exception>
        /// <exception cref="PlatformNotSupportedException">Execution is not in an Android player.</exception>
        /// <exception cref="InvalidOperationException">The caller is not on the main thread or a picker is already open.</exception>
        /// <exception cref="OperationCanceledException">The request is cancelled.</exception>
        /// <exception cref="UnauthorizedAccessException">The required grant is unavailable.</exception>
        /// <exception cref="NotSupportedException">The picker or persistable access is unavailable.</exception>
        /// <exception cref="IOException">The result or launch fails.</exception>
        public static async Task<StorageFile?> CreateFileAsync(string name,
            string mimeType = "application/octet-stream", bool persistPermission = true,
            CancellationToken cancellationToken = default)
        {
            StorageName.Validate(name);
            ValidateMimeType(mimeType);
            var location = await StartPicker("android.intent.action.CREATE_DOCUMENT", mimeType, name,
                true, persistPermission, false, cancellationToken).ConfigureAwait(false);
            if (location is null)
            {
                return null;
            }
            return new StorageFile(new AndroidDocumentFileSystem(), location);
        }

        /// <summary>Lists grants currently persisted by Android for this application.</summary>
        /// <returns>A snapshot of permissions, not a guarantee that the documents still exist.</returns>
        /// <exception cref="PlatformNotSupportedException">Execution is not in an Android player.</exception>
        /// <exception cref="UnauthorizedAccessException">Android denies the query.</exception>
        /// <exception cref="InvalidOperationException">The Unity Android activity is unavailable.</exception>
        /// <exception cref="IOException">The query fails.</exception>
        public static IReadOnlyList<AndroidStoragePermission> GetPersistedPermissions()
        {
            return InvokeJava(() =>
            {
                using var activity = GetActivity();
                using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");
                using var permissions = resolver.Call<AndroidJavaObject>("getPersistedUriPermissions");
                var count = permissions.Call<int>("size");
                var result = new AndroidStoragePermission[count];
                for (var i = 0; i < count; i++)
                {
                    using var permission = permissions.Call<AndroidJavaObject>("get", i);
                    using var uri = permission.Call<AndroidJavaObject>("getUri");
                    result[i] = new AndroidStoragePermission(uri.Call<string>("toString"),
                        permission.Call<bool>("isReadPermission"), permission.Call<bool>("isWritePermission"),
                        DateTimeOffset.FromUnixTimeMilliseconds(permission.Call<long>("getPersistedTime")).UtcDateTime);
                }
                return (IReadOnlyList<AndroidStoragePermission>)result;
            });
        }

        /// <summary>Releases selected parts of an existing persisted grant; an absent grant is a no-op.</summary>
        /// <param name="location">The original granted URI, not a descendant URI.</param>
        /// <param name="read">Whether to release read access.</param>
        /// <param name="write">Whether to release write access.</param>
        /// <exception cref="ArgumentException">The location is not an absolute content URI.</exception>
        /// <exception cref="PlatformNotSupportedException">Execution is not in an Android player.</exception>
        /// <exception cref="UnauthorizedAccessException">Android denies release.</exception>
        /// <exception cref="InvalidOperationException">The Unity Android activity is unavailable.</exception>
        /// <exception cref="IOException">The grant cannot be released.</exception>
        public static void ReleasePermission(string location, bool read = true, bool write = true)
        {
            ValidateContentUri(location);
            var flags = 0;
            foreach (var permission in GetPersistedPermissions())
            {
                if (!string.Equals(permission.Location, location, StringComparison.Ordinal))
                {
                    continue;
                }
                if (read && permission.CanRead)
                {
                    flags |= GrantRead;
                }
                if (write && permission.CanWrite)
                {
                    flags |= GrantWrite;
                }
            }
            if (flags == 0)
            {
                return;
            }
            InvokeJava(() =>
            {
                using var activity = GetActivity();
                using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");
                using var uriClass = new AndroidJavaClass("android.net.Uri");
                using var uri = uriClass.CallStatic<AndroidJavaObject>("parse", location);
                resolver.Call("releasePersistableUriPermission", uri, flags);
                return true;
            });
        }

        /// <summary>Hooks activity results after AndroidRuntime has initialized.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            s_mainThreadId = Thread.CurrentThread.ManagedThreadId;
            AndroidRuntime.OnActivityResult -= OnActivityResult;
            AndroidRuntime.OnActivityResult += OnActivityResult;
            Application.quitting -= Shutdown;
            Application.quitting += Shutdown;
#endif
        }

        /// <summary>Cancels outstanding work and removes callbacks during shutdown.</summary>
        private static void Shutdown()
        {
            PickerRequest? pending;
            lock (s_gate)
            {
                pending = s_pending;
                s_pending = null;
#if UNITY_ANDROID && !UNITY_EDITOR
                s_mainThreadId = 0;
#endif
                pending?.Cancel();
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidRuntime.OnActivityResult -= OnActivityResult;
            Application.quitting -= Shutdown;
#endif
            // A cancellation callback can wait for s_gate, so never dispose its registration under the gate.
            pending?.Dispose();
        }

        /// <summary>Reserves one request and schedules launch on Android's UI thread.</summary>
        /// <param name="action">The Android intent action.</param>
        /// <param name="mimeType">The optional file filter.</param>
        /// <param name="name">The optional proposed filename.</param>
        /// <param name="writable">Whether write access is required.</param>
        /// <param name="persistPermission">Whether to require a persisted grant.</param>
        /// <param name="tree">Whether the result must be a tree.</param>
        /// <param name="cancellationToken">The owner's token.</param>
        /// <returns>The selected raw URI, or null on dismissal.</returns>
        /// <exception cref="PlatformNotSupportedException">Execution is not in an Android player.</exception>
        /// <exception cref="InvalidOperationException">The thread or concurrent request is invalid.</exception>
        /// <exception cref="OperationCanceledException">The token was already cancelled.</exception>
        private static Task<string?> StartPicker(string action, string? mimeType, string? name,
            bool writable, bool persistPermission, bool tree, CancellationToken cancellationToken)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Thread.CurrentThread.ManagedThreadId != s_mainThreadId || s_mainThreadId == 0)
            {
                throw new InvalidOperationException("Start the document picker on Unity's main thread after initialization.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            PickerRequest pending;
            lock (s_gate)
            {
                if (s_pending is not null)
                {
                    throw new InvalidOperationException("Dismiss the active Android document picker before opening another.");
                }
                if (s_nextRequestCode > 0x7FFF)
                {
                    s_nextRequestCode = 0x4D00;
                }
                pending = new PickerRequest(s_nextRequestCode++, writable, persistPermission, tree, cancellationToken);
                s_pending = pending;
            }
            try
            {
                pending.RegisterCancellation();
                InvokeJava(() =>
                {
                    using var activity = GetActivity();
                    activity.Call("runOnUiThread", new AndroidJavaRunnable(() => LaunchPicker(pending, action, mimeType, name)));
                    return true;
                });
            }
            catch (Exception exception)
            {
                FailRequest(pending, exception);
            }
            return pending.Completion.Task;
#else
            throw new PlatformNotSupportedException("Storage Access Framework pickers require an Android player.");
#endif
        }

        /// <summary>Builds and launches the intent on Android's UI thread.</summary>
        /// <param name="pending">The reserved request.</param>
        /// <param name="action">The intent action.</param>
        /// <param name="mimeType">The optional MIME filter.</param>
        /// <param name="name">The optional proposed filename.</param>
        private static void LaunchPicker(PickerRequest pending, string action, string? mimeType, string? name)
        {
            try
            {
                bool active;
                lock (s_gate)
                {
                    active = ReferenceEquals(s_pending, pending) && !pending.Completion.Task.IsCompleted;
                }
                if (!active)
                {
                    FailRequest(pending, new OperationCanceledException());
                    return;
                }
                var launched = InvokeJava(() =>
                {
                    using var activity = GetActivity();
                    using var intent = new AndroidJavaObject("android.content.Intent", action);
                    var flags = GrantRead | (pending.Writable ? GrantWrite : 0);
                    if (pending.PersistPermission)
                    {
                        flags |= GrantPersistable;
                    }
                    if (pending.Tree)
                    {
                        flags |= GrantPrefix;
                    }
                    intent.Call<AndroidJavaObject>("addFlags", flags).Dispose();
                    if (mimeType is not null)
                    {
                        intent.Call<AndroidJavaObject>("setType", mimeType).Dispose();
                        intent.Call<AndroidJavaObject>("addCategory", "android.intent.category.OPENABLE").Dispose();
                    }
                    if (name is not null)
                    {
                        intent.Call<AndroidJavaObject>("putExtra", "android.intent.extra.TITLE", name).Dispose();
                    }
                    return TryLaunch(pending, () =>
                    {
                        activity.Call("startActivityForResult", intent, pending.RequestCode);
                    });
                });
                if (!launched)
                {
                    FailRequest(pending, new OperationCanceledException());
                }
            }
            catch (Exception exception)
            {
                FailRequest(pending, exception);
            }
        }

        /// <summary>Authorizes the final native launch atomically with cancellation and shutdown.</summary>
        /// <param name="pending">The request whose UI launch is being authorized.</param>
        /// <param name="launch">The native launch action, executed while request ownership is held.</param>
        /// <returns>Whether the action was authorized and executed.</returns>
        /// <exception cref="Exception">The native launch action failed; the caller completes the request with that failure.</exception>
        private static bool TryLaunch(PickerRequest pending, Action launch)
        {
            lock (s_gate)
            {
                if (!ReferenceEquals(s_pending, pending) || pending.Completion.Task.IsCompleted || pending.IsCancellationRequested)
                {
                    return false;
                }
                launch();
                return true;
            }
        }

        /// <summary>Routes only this component's result into a serialized result commitment.</summary>
        /// <param name="sender">The unused AndroidRuntime event sender.</param>
        /// <param name="requestCode">The activity request ID.</param>
        /// <param name="resultCode">Android's result status.</param>
        /// <param name="intent">The borrowed intent, owned by AndroidRuntime.</param>
        private static void OnActivityResult(object? sender, int requestCode, int resultCode, AndroidJavaObject? intent)
        {
            PickerRequest? pending;
            lock (s_gate)
            {
                pending = s_pending;
                if (pending is null || pending.RequestCode != requestCode)
                {
                    return;
                }
            }
            CommitResult(pending, () =>
            {
                if (resultCode == 0)
                {
                    return null;
                }
                if (resultCode != -1 || intent is null)
                {
                    throw new IOException("Android returned an invalid document picker result.");
                }
                return InvokeJava(() => ReadPickerResult(pending, intent));
            });
        }

        /// <summary>Arbitrates grant persistence/result completion against owner cancellation and shutdown.</summary>
        /// <param name="pending">The request whose result is ready.</param>
        /// <param name="readResult">The result validation/persistence operation, which runs only after commitment wins.</param>
        /// <remarks>
        /// Cancellation requested before commitment wins without invoking readResult. Once commitment begins,
        /// it completes successfully or with its operation error; cancellation cannot hide a persisted grant.
        /// Registration disposal is always outside the gate because a cancellation callback can wait for that gate.
        /// </remarks>
        private static void CommitResult(PickerRequest pending, Func<string?> readResult)
        {
            try
            {
                lock (s_gate)
                {
                    if (!ReferenceEquals(s_pending, pending))
                    {
                        return;
                    }
                    try
                    {
                        if (pending.TryBeginCommit())
                        {
                            var location = readResult();
                            pending.Completion.TrySetResult(location);
                        }
                    }
                    catch (Exception exception)
                    {
                        pending.Completion.TrySetException(exception);
                    }
                    finally
                    {
                        s_pending = null;
                    }
                }
            }
            finally
            {
                pending.Dispose();
            }
        }

        /// <summary>Validates the URI and persists only the read/write flags actually granted.</summary>
        /// <param name="pending">The requested permissions.</param>
        /// <param name="intent">The borrowed result intent.</param>
        /// <returns>The selected raw URI.</returns>
        /// <exception cref="IOException">The result has no valid URI.</exception>
        /// <exception cref="UnauthorizedAccessException">Required access was not granted.</exception>
        /// <exception cref="NotSupportedException">The provider did not offer persistable access.</exception>
        private static string ReadPickerResult(PickerRequest pending, AndroidJavaObject intent)
        {
            using var uri = intent.Call<AndroidJavaObject>("getData");
            if (uri is null)
            {
                throw new IOException("The document picker returned no URI.");
            }
            var location = uri.Call<string>("toString");
            ValidateContentUri(location);
            if (pending.Tree)
            {
                using var documents = new AndroidJavaClass("android.provider.DocumentsContract");
                if (!documents.CallStatic<bool>("isTreeUri", uri))
                {
                    throw new IOException("The document picker did not return a document tree.");
                }
            }
            var resultFlags = intent.Call<int>("getFlags");
            var grants = resultFlags & (GrantRead | GrantWrite);
            var requiredGrants = GrantRead | (pending.Writable ? GrantWrite : 0);
            if ((grants & requiredGrants) != requiredGrants)
            {
                throw new UnauthorizedAccessException("The document picker did not grant the required access.");
            }
            if (pending.PersistPermission)
            {
                if ((resultFlags & GrantPersistable) == 0)
                {
                    throw new NotSupportedException("This document provider did not offer persistable access.");
                }
                using var activity = GetActivity();
                using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");
                resolver.Call("takePersistableUriPermission", uri, grants);
            }
            return location;
        }

        /// <summary>Releases a failed launch without overwriting an already completed cancellation.</summary>
        /// <param name="pending">The failed request.</param>
        /// <param name="exception">The launch or cancellation failure.</param>
        private static void FailRequest(PickerRequest pending, Exception exception)
        {
            lock (s_gate)
            {
                if (ReferenceEquals(s_pending, pending))
                {
                    s_pending = null;
                }
                if (exception is OperationCanceledException)
                {
                    pending.Cancel();
                }
                else
                {
                    pending.Completion.TrySetException(exception);
                }
            }
            pending.Dispose();
        }

        /// <summary>Gets an owned activity reference without depending on AndroidRuntime initialization order.</summary>
        /// <returns>The current Unity Android activity.</returns>
        /// <exception cref="InvalidOperationException">The activity is unavailable.</exception>
        private static AndroidJavaObject GetActivity()
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity")
                ?? throw new InvalidOperationException("The Unity Android activity is unavailable.");
        }

        /// <summary>Runs JNI with scoped thread attachment and translates platform failures.</summary>
        /// <typeparam name="T">The managed result type.</typeparam>
        /// <param name="operation">The JNI operation, responsible for disposing its owned references.</param>
        /// <returns>The result.</returns>
        /// <exception cref="PlatformNotSupportedException">Execution is not in an Android player.</exception>
        /// <exception cref="UnauthorizedAccessException">Java reports denied access.</exception>
        /// <exception cref="NotSupportedException">No document picker is installed.</exception>
        /// <exception cref="IOException">JNI or Android fails the operation.</exception>
        private static T InvokeJava<T>(Func<T> operation)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var result = default(T)!;
            ExceptionDispatchInfo? failure = null;
            AndroidJNI.InvokeAttached(() =>
            {
                try
                {
                    result = operation();
                }
                catch (AndroidJavaException exception)
                {
                    Exception translated;
                    if (exception.Message.IndexOf("SecurityException", StringComparison.Ordinal) >= 0)
                    {
                        translated = new UnauthorizedAccessException("Android denied storage access.", exception);
                    }
                    else if (exception.Message.IndexOf("ActivityNotFoundException", StringComparison.Ordinal) >= 0)
                    {
                        translated = new NotSupportedException("No Android document picker is available.", exception);
                    }
                    else
                    {
                        translated = new IOException("Android storage access failed.", exception);
                    }
                    failure = ExceptionDispatchInfo.Capture(translated);
                }
                catch (Exception exception)
                {
                    failure = ExceptionDispatchInfo.Capture(exception);
                }
            });
            failure?.Throw();
            return result;
#else
            throw new PlatformNotSupportedException("Storage Access Framework requires an Android player.");
#endif
        }

        /// <summary>Validates a content URI without changing its escaping.</summary>
        /// <param name="location">The original URI.</param>
        /// <exception cref="ArgumentException">The URI is not absolute content with an authority.</exception>
        private static void ValidateContentUri(string location)
        {
            if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, "content", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(uri.Authority))
            {
                throw new ArgumentException("An absolute content URI is required.", nameof(location));
            }
        }

        /// <summary>Checks the MIME filter before showing UI.</summary>
        /// <param name="mimeType">The requested filter.</param>
        /// <exception cref="ArgumentException">The type is empty or has no type/subtype separator.</exception>
        private static void ValidateMimeType(string mimeType)
        {
            if (string.IsNullOrWhiteSpace(mimeType) || mimeType.IndexOf('/') <= 0 ||
                mimeType.EndsWith("/", StringComparison.Ordinal) || mimeType.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("A MIME type with a type and subtype is required.", nameof(mimeType));
            }
        }

        /// <summary>Owns a request completion and cancellation registration.</summary>
        private sealed class PickerRequest : IDisposable
        {
            /// <summary>Stores the owner's token.</summary>
            private readonly CancellationToken _cancellationToken;

            /// <summary>Records that result commitment owns completion, including reentrant cancellation callbacks.</summary>
            private bool _committing;

            /// <summary>Stores the registration released after a result or launch failure.</summary>
            private CancellationTokenRegistration _registration;

            /// <summary>Gets the activity request code.</summary>
            public int RequestCode { get; }

            /// <summary>Gets whether write access is required.</summary>
            public bool Writable { get; }

            /// <summary>Gets whether the grant must be persisted.</summary>
            public bool PersistPermission { get; }

            /// <summary>Gets whether a tree URI is required.</summary>
            public bool Tree { get; }

            /// <summary>Gets whether the owner's token is signalled, even if its callback is still waiting for the gate.</summary>
            public bool IsCancellationRequested
            {
                get
                {
                    return _cancellationToken.IsCancellationRequested;
                }
            }

            /// <summary>Gets a completion whose continuations never run inline on Android's UI thread.</summary>
            public TaskCompletionSource<string?> Completion { get; }

            /// <summary>Initializes a reserved request.</summary>
            /// <param name="requestCode">The activity request code.</param>
            /// <param name="writable">Whether write access is required.</param>
            /// <param name="persistPermission">Whether to persist the grant.</param>
            /// <param name="tree">Whether a tree is required.</param>
            /// <param name="cancellationToken">The owner's token.</param>
            public PickerRequest(int requestCode, bool writable, bool persistPermission,
                bool tree, CancellationToken cancellationToken)
            {
                RequestCode = requestCode;
                Writable = writable;
                PersistPermission = persistPermission;
                Tree = tree;
                _cancellationToken = cancellationToken;
                Completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            /// <summary>Registers cancellation without freeing a still-open native picker.</summary>
            public void RegisterCancellation()
            {
                _registration = _cancellationToken.Register(Cancel);
            }

            /// <summary>Reserves result completion before any grant-persistence side effect.</summary>
            /// <returns>Whether result commitment wins over a prior cancellation or completion.</returns>
            public bool TryBeginCommit()
            {
                lock (s_gate)
                {
                    if (_committing || Completion.Task.IsCompleted)
                    {
                        return false;
                    }
                    if (_cancellationToken.IsCancellationRequested)
                    {
                        Completion.TrySetCanceled(_cancellationToken);
                        return false;
                    }
                    _committing = true;
                    return true;
                }
            }

            /// <summary>Completes cancellation only when it wins the gate before result commitment.</summary>
            public void Cancel()
            {
                lock (s_gate)
                {
                    if (!_committing)
                    {
                        Completion.TrySetCanceled(_cancellationToken);
                    }
                }
            }

            /// <summary>Stops observing cancellation.</summary>
            public void Dispose()
            {
                _registration.Dispose();
            }
        }
    }
}
