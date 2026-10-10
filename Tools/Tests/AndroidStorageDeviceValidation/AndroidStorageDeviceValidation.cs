#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.IO.Storage;
using MajdataPlay.Platform.Android.Runtime.Java.Lang;
using MajdataPlay.Platform.Android.Storage;
using UnityEngine;
using Activity = MajdataPlay.Platform.Android.Runtime.App.Activity;
using Intent = MajdataPlay.Platform.Android.Runtime.Content.Intent;
using KeyEvent = MajdataPlay.Platform.Android.Runtime.View.KeyEvent;
using StorageFileSystem = MajdataPlay.IO.Storage.FileSystem;
using UnityPlayer = MajdataPlay.Platform.Android.Runtime.Unity.UnityPlayer;

namespace MajdataPlay.Platform.Android.Runtime.Validation
{
    /// <summary>Runs real generated JNI and SAF checks in an isolated development Android player.</summary>
    /// <remarks>Only a newly created unique child is deleted. Saved grant URIs never appear in logs or results.</remarks>
    public sealed class AndroidStorageDeviceValidation : MonoBehaviour
    {
        /// <summary>Names the private grant state consumed after a force-stop.</summary>
        public const string StateFileName = "android-storage-device-state.json";

        /// <summary>Names the durable, URI-free machine-readable status.</summary>
        public const string ResultFileName = "android-storage-device-result.json";

        /// <summary>Names the durable, URI-free plain-text marker.</summary>
        public const string MarkerFileName = "android-storage-device-result.txt";

        /// <summary>Names the launch extra selecting full, replay, or release.</summary>
        public const string ModeExtra = "validationMode";

        /// <summary>Owns cancellation for this component's lifetime.</summary>
        private CancellationTokenSource? _lifetime;

        /// <summary>Retains the exception-observing operation instead of launching an unobserved async void method.</summary>
        private Task? _runTask;

        /// <summary>Stores the app-private path captured on the Unity main thread.</summary>
        private string _persistentPath = string.Empty;

        /// <summary>Stores the current URI-free visible and durable status.</summary>
        private readonly DeviceResult _result = new DeviceResult();

        /// <summary>Stores the selected launch mode.</summary>
        private string _mode = "full";

        /// <summary>Creates the runtime component without requiring a serialized scene reference.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Bootstrap()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (UnityEngine.Object.FindFirstObjectByType<AndroidStorageDeviceValidation>() != null)
            {
                return;
            }
            var owner = new GameObject(nameof(AndroidStorageDeviceValidation));
            UnityEngine.Object.DontDestroyOnLoad(owner);
            owner.AddComponent<AndroidStorageDeviceValidation>();
#endif
        }

        private void Start()
        {
            _persistentPath = Application.persistentDataPath;
            _lifetime = new CancellationTokenSource();
            _runTask = RunObservedAsync(_lifetime.Token);
        }

        private void OnDestroy()
        {
            _lifetime?.Cancel();
        }

        private void OnGUI()
        {
            GUI.Label(new Rect(20, 20, Screen.width - 40, Screen.height - 40),
                "MajdataPlay SAF / generated JNI validation\n" + _result.Marker +
                "\nMode: " + _mode + "  IL2CPP: " + _result.EnableIl2Cpp +
                "  Pointer bytes: " + _result.PointerSize + "\n" + _result.Detail);
        }

        /// <summary>Observes every failure, including cancellation and cleanup, and writes a terminal marker.</summary>
        /// <param name="cancellationToken">The component's lifetime token.</param>
        /// <returns>A task that completes after recording success or an observed failure.</returns>
        private async Task RunObservedAsync(CancellationToken cancellationToken)
        {
            try
            {
                _result.RunId = Guid.NewGuid().ToString("N");
                _result.PointerSize = IntPtr.Size;
#if ENABLE_IL2CPP
                _result.EnableIl2Cpp = true;
#endif
                Directory.CreateDirectory(_persistentPath);
                Mark("BOOT_READY", "Starting controlled validation.");
                Require(_result.EnableIl2Cpp, "ENABLE_IL2CPP must be defined by the real player build.");
                Mark("IL2CPP_PASSED", "The runtime was compiled with ENABLE_IL2CPP.");
                _mode = InvokeAttached(ReadMode);
                _result.Mode = _mode;
                Require(_mode == "full" || _mode == "replay" || _mode == "release", "Unknown validationMode.");
                InvokeAttached(() =>
                {
                    CheckJni();
                    _result.SdkInt = DeviceJniProbe.GetSdkInt();
                    _result.ProcessId = DeviceJniProbe.GetProcessId();
                    Require(DeviceJniProbe.Is64Bit() == (IntPtr.Size == 8), "Java and managed process bitness disagree.");
                    return true;
                });
                Mark("JNI_PASSED", "Typed constructors, references, signed bytes, UTF-16 and cleared Java exceptions passed.");
                var mainThreadId = Thread.CurrentThread.ManagedThreadId;
                await Task.Run(() => InvokeAttached(() =>
                {
                    Require(Thread.CurrentThread.ManagedThreadId != mainThreadId, "The JNI worker ran on the main thread.");
                    CheckJni();
                    return true;
                }), cancellationToken);
                Mark("BACKGROUND_JNI_PASSED", "JNI work and deterministic wrapper disposal ran on an attached worker.");
                await CheckUiCallbackAsync(cancellationToken);
                Mark("UI_CALLBACK_PASSED", "The generated Activity Runnable adapter completed a managed callback.");

                var statePath = Path.Combine(_persistentPath, StateFileName);
                DeviceState state;
                StorageDirectory parent;
                if (_mode == "replay" || _mode == "release")
                {
                    state = JsonUtility.FromJson<DeviceState>(File.ReadAllText(statePath))
                        ?? throw new InvalidOperationException("Saved validation state is missing.");
                    Require(state.Version == 1 && !string.IsNullOrEmpty(state.Location) && !state.Finished,
                        "No active saved grant is available.");
                    if (_mode == "release")
                    {
                        await Task.Run(() =>
                        {
                            if (!state.WasPreexisting)
                            {
                                AndroidStorageAccess.ReleasePermission(state.Location);
                                Require(FindPermission(state.Location) is null, "The newly acquired grant was not released.");
                            }
                        }, cancellationToken);
                        state.Finished = true;
                        SaveState(state);
                        _result.GrantReleased = !state.WasPreexisting;
                        Mark("RELEASE_PASSED", state.WasPreexisting ? "Preexisting grant retained." : "Test-acquired grant released.", true);
                        return;
                    }
                    Require(state.FullPassed && state.PickProcessId != _result.ProcessId,
                        "Replay requires a completed full run and a different process after force-stop.");
                    Require(string.IsNullOrEmpty(state.ActiveChildLocation), "A previous interrupted run left an owned child; inspect private state first.");
                    parent = StorageFileSystem.Directory.Open(state.Location);
                }
                else
                {
                    if (File.Exists(statePath))
                    {
                        var old = JsonUtility.FromJson<DeviceState>(File.ReadAllText(statePath));
                        Require(old is null || old.Finished || string.IsNullOrEmpty(old.Location),
                            "An active grant state exists; use replay or release before starting a new full run.");
                    }
                    var before = await Task.Run(AndroidStorageAccess.GetPersistedPermissions, cancellationToken);
                    state = new DeviceState { RunId = _result.RunId, PickProcessId = _result.ProcessId };
                    state.ExistingGrantLocations = new string[before.Count];
                    for (var i = 0; i < before.Count; i++)
                    {
                        state.ExistingGrantLocations[i] = before[i].Location;
                    }
                    SaveState(state);
                    var picker = AndroidStorageAccess.PickDirectoryAsync(writable: true, persistPermission: true,
                        cancellationToken: cancellationToken);
                    Mark("PICKER_READY", "Select a disposable scratch directory in the real system picker.");
                    parent = await picker ?? throw new InvalidOperationException("The picker was dismissed without a directory.");
                    state.Location = parent.Location;
                    state.WasPreexisting = Array.IndexOf(state.ExistingGrantLocations, state.Location) >= 0;
                    SaveState(state);
                }
                _result.GrantWasPreexisting = state.WasPreexisting;
                await Task.Run(() =>
                {
                    var permission = FindPermission(state.Location);
                    Require(permission is not null && permission.CanRead && permission.CanWrite,
                        "The selected URI has no persisted read/write grant.");
                    Require(parent.Exists, "The persisted directory could not be reopened.");
                }, cancellationToken);
                Mark("PERSISTED_GRANT_PASSED", "The selected location has a persisted read/write grant and can be reopened.");
                await CheckStorageAsync(parent, state, cancellationToken);
                if (_mode == "full")
                {
                    state.FullPassed = true;
                    SaveState(state);
                }
                Mark(_mode == "replay" ? "REPLAY_PASSED" : "FULL_PASSED",
                    "Owned scratch child removed; selected parent retained. " +
                    (_mode == "full" ? "Force-stop and launch validationMode=replay next." : "Persisted access survived process restart."), true);
            }
            catch (Exception exception)
            {
                _result.ErrorType = exception.GetType().FullName ?? exception.GetType().Name;
                // Provider exception messages and stack traces can contain private document URIs.
                try
                {
                    Mark(_mode.ToUpperInvariant() + "_FAILED",
                        "Observed " + _result.ErrorType + "; last phase: " + _result.Marker + ". Provider details suppressed.", true);
                }
                catch (Exception markerException)
                {
                    Debug.LogError("FILE_SYSTEM_DEVICE_RESULT_FAILED: Could not persist failure marker (" + markerException.GetType().Name + ").");
                }
            }
            finally
            {
                _lifetime?.Dispose();
                _lifetime = null;
            }
        }

        /// <summary>Reads the launch extra through generated Activity and Intent wrappers.</summary>
        /// <returns>The requested mode, or full when no mode was supplied.</returns>
        /// <exception cref="InvalidOperationException">Unity has no current activity or launch intent.</exception>
        /// <exception cref="JavaInvocationException">Java rejects an intent query.</exception>
        private static string ReadMode()
        {
            using Activity activity = UnityPlayer.CurrentActivity
                ?? throw new InvalidOperationException("Unity has no current activity.");
            using var intent = activity.GetIntent()
                ?? throw new InvalidOperationException("The activity has no launch intent.");
            var mode = intent.GetStringExtra(ModeExtra);
            return string.IsNullOrEmpty(mode) || mode == "pick" ? "full" : mode;
        }

        /// <summary>Tests generated class dispatch without handwritten JNI class or method dispatch.</summary>
        /// <exception cref="InvalidOperationException">An actual JNI result violates the expected contract.</exception>
        /// <exception cref="JavaInvocationException">An unexpected Java exception occurs.</exception>
        private static void CheckJni()
        {
            using var key = new KeyEvent(KeyEvent.ActionDown, KeyEvent.KeycodeA);
            Require(key.GetAction() == KeyEvent.ActionDown && key.GetKeyCode() == KeyEvent.KeycodeA, "KeyEvent construction failed.");
            using (var borrowed = new KeyEvent(key.JavaReference, ownsReference: false))
            {
                Require(borrowed.GetKeyCode() == KeyEvent.KeycodeA, "Borrowed reference dispatch failed.");
                borrowed.Dispose();
                Expect<ObjectDisposedException>(() => borrowed.GetAction());
            }
            Require(key.GetAction() == KeyEvent.ActionDown, "Disposing a borrowed wrapper disposed the owner.");
            using (var echoedKey = DeviceJniProbe.EchoKeyEvent(key))
            {
                Require(echoedKey is not null && echoedKey.GetKeyCode() == KeyEvent.KeycodeA, "Typed KeyEvent return failed.");
            }
            var raw = DeviceJniProbe.CreateAdoptableKeyEvent()
                ?? throw new InvalidOperationException("The adoption probe returned null.");
            using (var adopted = new KeyEvent(raw, ownsReference: true))
            {
                Require(adopted.GetKeyCode() == KeyEvent.KeycodeA, "Adopted reference dispatch failed.");
                adopted.Dispose();
                adopted.Dispose();
                Expect<ObjectDisposedException>(() => adopted.GetAction());
            }
            using var intent = new Intent("net.majdata.validation.PROBE");
            using (var flags = intent.AddFlags(Intent.FlagGrantReadUriPermission))
            using (var typed = DeviceJniProbe.EchoIntent(intent))
            {
                Require(flags is not null && typed is not null && typed.GetAction() == "net.majdata.validation.PROBE", "Intent fluent result failed.");
            }
            Require(intent.GetAction() == "net.majdata.validation.PROBE", "Disposing fluent results disposed their input.");
            Require(DeviceJniProbe.NullIntent() is null && DeviceJniProbe.EchoIntent(null) is null, "Null reference conversion failed.");
            var input = new sbyte[] { 0, 127, unchecked((sbyte)128), unchecked((sbyte)255) };
            var output = DeviceJniProbe.EchoBytes(input);
            Require(output is not null && output.Length == input.Length && !ReferenceEquals(input, output), "Owned signed byte-array conversion failed.");
            for (var i = 0; i < input.Length; i++)
            {
                Require(input[i] == output![i], "A signed-byte boundary changed its bit pattern.");
            }
            output![0] = 10;
            Require(input[0] == 0 && DeviceJniProbe.EchoBytes(null) is null, "Byte arrays were aliased or null was lost.");
            const string Text = "JNI\0\u4E2D\uD83D\uDE00";
            Require(DeviceJniProbe.EchoString(Text) == Text && DeviceJniProbe.EchoString(null) is null, "UTF-16 or embedded NUL conversion failed.");
            var translated = false;
            try
            {
                DeviceJniProbe.ThrowExpected();
            }
            catch (JavaInvocationException exception)
            {
                translated = exception.Message.Contains("MAJDATA_VALIDATION_EXPECTED_JAVA_EXCEPTION") &&
                    exception.JavaStackTrace.Contains("IllegalStateException");
            }
            Require(translated, "The Java exception was not translated with its stack trace.");
            var pending = AndroidJNI.ExceptionOccurred();
            if (pending != IntPtr.Zero)
            {
                AndroidJNI.ExceptionClear();
                AndroidJNI.DeleteLocalRef(pending);
                throw new InvalidOperationException("Generated dispatch left a Java exception pending.");
            }
            Require(DeviceJniProbe.EchoString("after-exception") == "after-exception", "JNI did not recover after the Java exception.");
            CheckJavaObjectEquality(key);
        }

        /// <summary>Checks Java virtual object methods, wrapper equality, and managed collection lookup on a real JVM.</summary>
        /// <param name="key">A live generated KeyEvent wrapper whose reference can be borrowed.</param>
        /// <exception cref="InvalidOperationException">An object method or lifecycle check violates its expected contract.</exception>
        /// <exception cref="AndroidJavaException">Java class or method resolution fails.</exception>
        /// <exception cref="JavaInvocationException">An unexpected Java exception occurs.</exception>
        private static void CheckJavaObjectEquality(KeyEvent key)
        {
            const string Text = "polygenelubricants";
            using var first = new JavaObject("java.lang.String", "(Ljava/lang/String;)V", Text);
            using var equal = new JavaObject("java.lang.String", "(Ljava/lang/String;)V", Text);
            using var different = new JavaObject("java.lang.String", "(Ljava/lang/String;)V", "different");
            Require(!AndroidJNI.IsSameObject(first.JavaReference.GetRawObject(), equal.JavaReference.GetRawObject()),
                "The String equality probes must be distinct Java objects.");
            Require(first.Equals(equal) && equal.Equals(first) && first == equal && !(first != equal),
                "String value equality did not dispatch to Java.");
            Require(!first.Equals(different) && first != different && !(first == different),
                "Different Java String values compared equal.");
            Require(first.GetHashCode() == int.MinValue && equal.GetHashCode() == int.MinValue,
                "String.hashCode did not preserve its signed 32-bit Java result.");
            Require(first.ToString() == Text, "String.toString did not dispatch to Java.");
            JavaObject? missing = null;
            var firstAlias = first;
            Require(first == firstAlias && missing == null && !(missing != null) &&
                first != missing && missing != first && !firstAlias.Equals(missing) && !firstAlias.Equals(Text),
                "Wrapper identity, null, or non-wrapper equality failed.");

            var dictionary = new Dictionary<JavaObject, string> { { firstAlias, "found" } };
            Require(dictionary.TryGetValue(equal, out var value) && value == "found" && !dictionary.ContainsKey(different),
                "Dictionary lookup did not use Java equality and hash codes.");
            var set = new HashSet<JavaObject> { firstAlias };
            Require(!set.Add(equal) && set.Contains(equal) && !set.Contains(different) && set.Count == 1,
                "HashSet did not deduplicate Java-equal wrappers.");

            using var identity = new JavaObject();
            using var otherIdentity = new JavaObject();
            using var borrowedIdentity = new JavaObject(identity.JavaReference, ownsReference: false);
            Require(identity == borrowedIdentity && borrowedIdentity == identity && identity != otherIdentity,
                "Object identity equality did not use the underlying Java object.");
            Require(identity.GetHashCode() == borrowedIdentity.GetHashCode() &&
                identity.ToString() == "java.lang.Object@" + identity.GetHashCode().ToString("x", CultureInfo.InvariantCulture),
                "Object hashCode or toString did not dispatch to Java.");
            using var borrowedKey = new JavaObject(key.JavaReference, ownsReference: false);
            Require(key.Equals(borrowedKey) && borrowedKey.Equals(key) && key == borrowedKey && borrowedKey == key &&
                key.GetHashCode() == borrowedKey.GetHashCode(),
                "Generated and base wrappers around the same Java object compared differently.");

            borrowedKey.Dispose();
            var disposedAlias = borrowedKey;
            Require(borrowedKey.Equals(disposedAlias) && borrowedKey == disposedAlias && !(borrowedKey != disposedAlias) &&
                borrowedKey != missing && missing != borrowedKey && !disposedAlias.Equals(missing) && !disposedAlias.Equals(Text),
                "Disposed wrapper identity, null, or non-wrapper comparisons failed.");
            Expect<ObjectDisposedException>(() => disposedAlias.GetHashCode());
            Expect<ObjectDisposedException>(() => disposedAlias.ToString());
            Expect<ObjectDisposedException>(() => disposedAlias.Equals(key));
            Expect<ObjectDisposedException>(() => key.Equals(borrowedKey));
            Expect<ObjectDisposedException>(() =>
            {
                _ = borrowedKey == key;
            });
            Expect<ObjectDisposedException>(() =>
            {
                _ = key != borrowedKey;
            });
            Require(key.GetAction() == KeyEvent.ActionDown, "Object equality checks disposed a borrowed reference's owner.");
        }

        /// <summary>Schedules a managed Runnable using the production adapter and generated Activity dispatch.</summary>
        /// <param name="cancellationToken">The owner-lifetime token.</param>
        /// <returns>A task completing after the callback or failing after ten seconds.</returns>
        /// <exception cref="TimeoutException">Android does not execute the callback in time.</exception>
        /// <exception cref="OperationCanceledException">The component is destroyed.</exception>
        private static async Task CheckUiCallbackAsync(CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var activity = UnityPlayer.CurrentActivity
                ?? throw new InvalidOperationException("Unity has no current activity.");
            activity.RunOnUiThread(() =>
            {
                try
                {
                    Require(DeviceJniProbe.EchoString("ui-callback") == "ui-callback", "UI callback JNI failed.");
                    completion.TrySetResult(true);
                }
                catch (Exception exception)
                {
                    // Never allow a managed exception to escape the native reverse-JNI callback.
                    completion.TrySetException(exception);
                }
            });
            var winner = await Task.WhenAny(completion.Task, Task.Delay(10000, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            if (winner != completion.Task)
            {
                throw new TimeoutException("The Android UI callback did not complete.");
            }
            await completion.Task;
        }

        /// <summary>Round-trips more than three JNI chunks through production SAF helpers and deletes only the owned child.</summary>
        /// <param name="parent">The selected directory, which must never be deleted.</param>
        /// <param name="state">The private state recording ownership if the process is interrupted.</param>
        /// <param name="cancellationToken">The owner-lifetime token.</param>
        /// <returns>A task completing after stream disposal and owned-child cleanup.</returns>
        /// <exception cref="IOException">The provider rejects an operation or cleanup fails.</exception>
        /// <exception cref="OperationCanceledException">Cancellation is requested.</exception>
        /// <exception cref="InvalidOperationException">A round-trip result is incorrect.</exception>
        private async Task CheckStorageAsync(StorageDirectory parent, DeviceState state, CancellationToken cancellationToken)
        {
            StorageDirectory? child = null;
            try
            {
                child = await Task.Run(() => parent.CreateDirectory("MajdataPlay-validation-" + Guid.NewGuid().ToString("N")), cancellationToken);
                state.ActiveChildLocation = child.Location;
                SaveState(state);
                var file = await Task.Run(() => child.CreateFile("roundtrip.bin"), cancellationToken);
                var bytes = new byte[(3 * 65536) + 513];
                for (var i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = unchecked((byte)i);
                }
                await file.WriteAllBytesAsync(bytes, cancellationToken);
                var read = await file.ReadAllBytesAsync(cancellationToken);
                Require(read.Length == bytes.Length, "The SAF round-trip length differs.");
                for (var i = 0; i < bytes.Length; i++)
                {
                    Require(read[i] == bytes[i], "The SAF round-trip changed a byte.");
                }
                Mark("BINARY_PASSED", "197121 bytes containing all 256 byte values round-tripped across multiple bounded JNI transfers.");
                var nested = await Task.Run(() => child.CreateDirectory("moved"), cancellationToken);
                var moved = await Task.Run(() => StorageFileSystem.File.Move(file.Location, nested.Location, "moved.bin"), cancellationToken);
                Require(moved.Location != file.Location && moved.Exists, "The SAF move did not return a new authoritative document.");
                var movedBytes = await moved.ReadAllBytesAsync(cancellationToken);
                Require(movedBytes.Length == bytes.Length, "The SAF move changed the document length.");
                for (var i = 0; i < bytes.Length; i++)
                {
                    Require(movedBytes[i] == bytes[i], "The SAF move changed a byte.");
                }
                Mark("MOVE_PASSED", "A document moved into a child directory of the same grant kept its bytes under a new URI.");
                var timestamp = new DateTime(2020, 2, 3, 4, 5, 6, DateTimeKind.Utc);
                try
                {
                    await Task.Run(() => moved.SetLastWriteTime(timestamp), cancellationToken);
                    var updated = moved.Entry;
                    Mark("TIMESTAMP_PASSED", updated?.LastWriteTimeUtc is null
                        ? "The provider accepted a modification-time update without reporting a modification time."
                        : "The provider reported " + updated.LastWriteTimeUtc.Value.ToString("O", CultureInfo.InvariantCulture) + " after a modification-time update.");
                }
                catch (NotSupportedException exception)
                {
                    // Providers may reject modification-time updates; record the capability instead of failing the run.
                    Mark("TIMESTAMP_SKIPPED", exception.Message);
                }
                var children = await Task.Run(() =>
                {
                    var names = new List<string>();
                    foreach (var entry in nested.EnumerateEntries())
                    {
                        names.Add(entry.Name);
                    }
                    return names;
                }, cancellationToken);
                Require(children.Count == 1 && children[0] == "moved.bin",
                    "The provider enumeration did not report exactly the moved child.");
                Mark("ENUMERATION_PASSED", "Immediate children were enumerated through repeated provider cursor steps.");
                var renamed = await Task.Run(() => moved.Rename("renamed.bin"), cancellationToken);
                var previousNameGone = await Task.Run(() => nested.FindFile("moved.bin"), cancellationToken) is null;
                Require(renamed.Exists && (renamed.Location != moved.Location || previousNameGone),
                    "The provider rename did not take effect on its new name.");
                var renamedBytes = await renamed.ReadAllBytesAsync(cancellationToken);
                Require(renamedBytes.Length == bytes.Length, "The provider rename changed the document length.");
                for (var i = 0; i < bytes.Length; i++)
                {
                    Require(renamedBytes[i] == bytes[i], "The provider rename changed a byte.");
                }
                Mark("RENAME_PASSED", "A same-parent rename kept the document bytes under its new name.");
                using (var stream = await Task.Run(() => renamed.OpenRead(), cancellationToken))
                {
                    Require(stream.CanRead && !stream.CanSeek, "The provider read stream reported unexpected capabilities.");
                    Expect<NotSupportedException>(() =>
                    {
                        _ = stream.Length;
                    });
                    Expect<NotSupportedException>(() =>
                    {
                        _ = stream.Position;
                    });
                    Expect<NotSupportedException>(() =>
                    {
                        stream.Seek(0, SeekOrigin.Begin);
                    });
                }
                Mark("STREAM_PASSED", "A provider stream stayed readable while rejecting length, position and seek queries.");
                using (var truncate = await Task.Run(() => renamed.OpenWrite(append: false), cancellationToken))
                {
                    await truncate.WriteAsync(new byte[] { 1, 2, 3 }, 0, 3, cancellationToken);
                }
                using (var append = await Task.Run(() => renamed.OpenWrite(append: true), cancellationToken))
                {
                    await append.WriteAsync(new byte[] { 4, 5 }, 0, 2, cancellationToken);
                }
                var appended = await renamed.ReadAllBytesAsync(cancellationToken);
                Require(appended.Length == 5 && appended[0] == 1 && appended[4] == 5,
                    "Explicit truncate and append modes produced unexpected bytes.");
                Mark("APPEND_PASSED", "Explicit truncate produced 3 bytes and append added 2 more without seeking.");
                using (var canceled = new CancellationTokenSource())
                {
                    canceled.Cancel();
                    try
                    {
                        await renamed.ReadAllBytesAsync(canceled.Token);
                        throw new InvalidOperationException("A pre-cancelled provider read completed instead of cancelling.");
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected: the pre-cancelled read must not transfer any byte.
                    }
                }
                Mark("CANCELLATION_PASSED", "A pre-cancelled provider read was rejected before transferring bytes.");
                var occupied = await Task.Run(() => nested.CreateFile("occupied.bin"), cancellationToken);
                await occupied.WriteAllTextAsync("occupied", cancellationToken: cancellationToken);
                Expect<IOException>(() => Task.Run(() =>
                    StorageFileSystem.File.Move(renamed.Location, nested.Location, "occupied.bin")).GetAwaiter().GetResult());
                Expect<IOException>(() => Task.Run(() => renamed.Rename("occupied.bin")).GetAwaiter().GetResult());
                var survivor = await renamed.ReadAllBytesAsync(cancellationToken);
                Require(survivor.Length == 5 && survivor[4] == 5, "The rejected collision changed the source document.");
                Require(await occupied.ReadAllTextAsync(cancellationToken: cancellationToken) == "occupied",
                    "The rejected collision changed the occupied sibling.");
                Mark("COLLISION_PASSED", "Move and rename onto an occupied name were rejected without changing either document.");
                Expect<ArgumentException>(() => nested.CreateFile(".."));
                Expect<ArgumentException>(() => nested.FindFile("parent/child"));
                Expect<ArgumentException>(() => StorageFileSystem.File.Create(nested.Location, "a\\b"));
                Mark("NAME_REJECTION_PASSED", "Traversal and separator child names were rejected before touching the provider.");
                var disposable = await Task.Run(() => nested.CreateFile("delete-me.bin"), cancellationToken);
                await disposable.WriteAllTextAsync("delete me", cancellationToken: cancellationToken);
                await Task.Run(() => disposable.Delete(), cancellationToken);
                Require(await Task.Run(() => nested.FindFile("delete-me.bin"), cancellationToken) is null,
                    "The provider still lists the deleted file as an immediate child.");
                var repeatedDeleteFailed = false;
                try
                {
                    await Task.Run(() => disposable.Delete(), cancellationToken);
                }
                catch (IOException)
                {
                    // A deleted tree document cannot be resolved again, so a repeated delete can surface a provider query failure.
                    repeatedDeleteFailed = true;
                }
                var staleUriResolved = false;
                try
                {
                    staleUriResolved = disposable.Exists;
                }
                catch (IOException)
                {
                    // The same provider limitation applies to metadata queries for the previous URI.
                    staleUriResolved = false;
                }
                Require(!staleUriResolved, "The deleted file is still reachable through its previous URI.");
                var retained = await Task.Run(() => nested.FindFile("occupied.bin"), cancellationToken);
                Require(retained is not null, "Deleting one file removed an unrelated sibling.");
                Mark("DELETE_PASSED", repeatedDeleteFailed
                    ? "An owned file was deleted and its sibling was retained; the provider reported the deleted URI as unresolvable."
                    : "An owned file was deleted, a repeated delete stayed harmless, and its sibling was retained.");
                Expect<IOException>(() => Task.Run(() => nested.Delete(recursive: false)).GetAwaiter().GetResult());
                Require(nested.Exists, "The rejected nonrecursive deletion removed the directory.");
                Mark("NONRECURSIVE_DELETE_PASSED", "A nonrecursive deletion of a nonempty provider directory was rejected.");
                var localRoot = Path.Combine(_persistentPath, "storage-validation-local");
                Directory.CreateDirectory(localRoot);
                var localSource = Path.Combine(localRoot, "local-source.bin");
                File.WriteAllBytes(localSource, new byte[] { 9, 8, 7 });
                var copied = await Task.Run(() => StorageFileSystem.File.Copy(localSource, nested.Location, "copied.bin"), cancellationToken);
                var copiedBytes = await copied.ReadAllBytesAsync(cancellationToken);
                Require(copiedBytes.Length == 3 && copiedBytes[0] == 9 && copiedBytes[2] == 7,
                    "The local-to-provider copy produced unexpected bytes.");
                var localTarget = Path.Combine(localRoot, "provider-to-local.bin");
                var backCopy = await Task.Run(() => StorageFileSystem.File.Copy(copied.Location, localTarget), cancellationToken);
                var backBytes = File.ReadAllBytes(localTarget);
                Require(backCopy.Exists && backBytes.Length == 3 && backBytes[1] == 8,
                    "The provider-to-local copy produced unexpected bytes.");
                File.Delete(localSource);
                File.Delete(localTarget);
                Directory.Delete(localRoot, false);
                Mark("COPY_PASSED", "Local-to-provider and provider-to-local copies transferred the expected bytes.");
                await Task.Run(() => renamed.SetAttributes(FileAttributes.Hidden | FileAttributes.System), cancellationToken);
                var attributeEntry = await Task.Run(() => renamed.Entry, cancellationToken);
                Require(attributeEntry is not null && !attributeEntry.IsHidden && !attributeEntry.IsSystem,
                    "The provider reported attribute flags that no SAF provider stores.");
                Mark("ATTRIBUTES_PASSED", "A provider attribute request was accepted without reporting stored hidden or system flags.");
                try
                {
                    var renamedDirectory = await Task.Run(() => nested.Rename("moved-renamed"), cancellationToken);
                    Require(renamedDirectory.Exists, "The renamed provider directory is not accessible.");
                    Mark("DIRECTORY_RENAME_PASSED", "A provider directory rename returned an accessible directory.");
                }
                catch (NotSupportedException exception)
                {
                    // Providers may refuse directory renames; record the capability instead of failing the run.
                    Mark("DIRECTORY_RENAME_SKIPPED", exception.Message);
                }
            }
            finally
            {
                if (child is not null)
                {
                    // Do not use a cancelled token for cleanup, and never substitute the selected parent here.
                    await Task.Run(() => child.Delete(recursive: true));
                    state.ActiveChildLocation = string.Empty;
                    SaveState(state);
                    _result.CleanupSucceeded = true;
                    Mark("CLEANUP_PASSED", "Only the unique child created by this invocation was removed.");
                }
            }
        }

        /// <summary>Finds the exact original persisted grant without logging its URI.</summary>
        /// <param name="location">The private original picker URI.</param>
        /// <returns>The matching permission, or null when it is absent.</returns>
        /// <exception cref="IOException">Android cannot return the persisted permissions.</exception>
        private static AndroidStoragePermission? FindPermission(string location)
        {
            foreach (var permission in AndroidStorageAccess.GetPersistedPermissions())
            {
                if (string.Equals(permission.Location, location, StringComparison.Ordinal))
                {
                    return permission;
                }
            }
            return null;
        }

        /// <summary>Attaches this thread and captures managed failures before leaving the native callback.</summary>
        /// <typeparam name="T">The callback result type.</typeparam>
        /// <param name="callback">Work that only accesses Java and ordinary managed objects.</param>
        /// <returns>The result after rethrowing any captured failure outside the attachment callback.</returns>
        /// <exception cref="Exception">The callback's original exception is rethrown with its original stack.</exception>
        private static T InvokeAttached<T>(Func<T> callback)
        {
            var value = default(T)!;
            ExceptionDispatchInfo? failure = null;
            AndroidJNI.InvokeAttached(() =>
            {
                try
                {
                    value = callback();
                }
                catch (Exception exception)
                {
                    failure = ExceptionDispatchInfo.Capture(exception);
                }
            });
            failure?.Throw();
            return value;
        }

        /// <summary>Checks an invariant without including private provider data in the message.</summary>
        /// <param name="condition">Whether the invariant holds.</param>
        /// <param name="message">A fixed non-sensitive failure explanation.</param>
        /// <exception cref="InvalidOperationException">The invariant does not hold.</exception>
        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        /// <summary>Requires a specific managed exception for a negative lifecycle check.</summary>
        /// <typeparam name="TException">The expected exception type.</typeparam>
        /// <param name="action">The operation expected to fail.</param>
        /// <exception cref="InvalidOperationException">The expected exception does not occur.</exception>
        /// <exception cref="Exception">An unexpected exception propagates unchanged.</exception>
        private static void Expect<TException>(Action action) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException("An expected " + typeof(TException).Name + " was not thrown.");
        }

        /// <summary>Flushes selected-grant state to the app-private filesystem on the Unity thread.</summary>
        /// <param name="state">State containing the private URI and conservative grant ownership snapshot.</param>
        /// <exception cref="IOException">Writing or flushing the private state fails.</exception>
        private void SaveState(DeviceState state)
        {
            WriteDurable(Path.Combine(_persistentPath, StateFileName), JsonUtility.ToJson(state, true));
        }

        /// <summary>Flushes a URI-free phase marker before reporting it to the screen and logcat.</summary>
        /// <param name="suffix">The fixed phase and READY, PASSED, or FAILED suffix.</param>
        /// <param name="detail">A fixed non-sensitive explanation.</param>
        /// <param name="terminal">Whether this is the invocation's terminal result.</param>
        /// <exception cref="IOException">Writing or flushing the result fails.</exception>
        private void Mark(string suffix, string detail, bool terminal = false)
        {
            _result.Marker = "FILE_SYSTEM_DEVICE_" + suffix;
            _result.Detail = detail;
            _result.Terminal = terminal;
            _result.Mode = _mode;
            _result.UpdatedUtc = DateTime.UtcNow.ToString("O");
            WriteDurable(Path.Combine(_persistentPath, ResultFileName), JsonUtility.ToJson(_result, true));
            WriteDurable(Path.Combine(_persistentPath, MarkerFileName), _result.Marker + "\n" + detail + "\n");
            if (suffix.EndsWith("_FAILED", StringComparison.Ordinal))
            {
                Debug.LogError(_result.Marker + ": " + detail);
            }
            else
            {
                Debug.Log(_result.Marker + ": " + detail);
            }
        }

        /// <summary>Writes UTF-8 app-local state and explicitly flushes it to durable storage.</summary>
        /// <param name="path">An app-private result or state path.</param>
        /// <param name="text">The serialized content.</param>
        /// <exception cref="IOException">Writing or flushing the file fails.</exception>
        private static void WriteDurable(string path, string text)
        {
            var bytes = new UTF8Encoding(false).GetBytes(text);
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }

        /// <summary>Stores grant ownership only in app-private data, never in public validation output.</summary>
        [Serializable]
        private sealed class DeviceState
        {
            /// <summary>Identifies this state protocol version.</summary>
            public int Version = 1;
            /// <summary>Identifies the full invocation that selected the grant.</summary>
            public string RunId = string.Empty;
            /// <summary>Stores the original authoritative selected grant URI.</summary>
            public string Location = string.Empty;
            /// <summary>Records the original process ID for force-stop replay validation.</summary>
            public int PickProcessId;
            /// <summary>Snapshots all original grant URIs so none of them can be released.</summary>
            public string[] ExistingGrantLocations = Array.Empty<string>();
            /// <summary>Records whether the selected grant existed before opening the picker.</summary>
            public bool WasPreexisting;
            /// <summary>Records successful completion and cleanup of the full phase.</summary>
            public bool FullPassed;
            /// <summary>Records completion of the optional release/retain phase.</summary>
            public bool Finished;
            /// <summary>Stores only the currently owned child URI for interrupted-run diagnosis.</summary>
            public string ActiveChildLocation = string.Empty;
        }

        /// <summary>Exposes only non-sensitive status that an ADB runner can safely retain.</summary>
        [Serializable]
        private sealed class DeviceResult
        {
            /// <summary>Stores the latest phase or terminal marker.</summary>
            public string Marker = "FILE_SYSTEM_DEVICE_BOOT_READY";
            /// <summary>Stores the launch mode.</summary>
            public string Mode = "full";
            /// <summary>Identifies this invocation independently of previous saved grants.</summary>
            public string RunId = string.Empty;
            /// <summary>Indicates whether Marker is a terminal result.</summary>
            public bool Terminal;
            /// <summary>Records the actual ENABLE_IL2CPP compile flag.</summary>
            public bool EnableIl2Cpp;
            /// <summary>Records actual managed pointer width in bytes.</summary>
            public int PointerSize;
            /// <summary>Records the running device SDK version.</summary>
            public int SdkInt;
            /// <summary>Records the current process ID.</summary>
            public int ProcessId;
            /// <summary>Records that the owned child was successfully removed.</summary>
            public bool CleanupSucceeded;
            /// <summary>Records whether the selected grant predates this test.</summary>
            public bool GrantWasPreexisting;
            /// <summary>Records an actual release, not a preexisting-grant retain operation.</summary>
            public bool GrantReleased;
            /// <summary>Stores an exception type without its possibly private message or stack trace.</summary>
            public string ErrorType = string.Empty;
            /// <summary>Stores a fixed non-sensitive status explanation.</summary>
            public string Detail = string.Empty;
            /// <summary>Records when the latest marker was flushed.</summary>
            public string UpdatedUtc = string.Empty;
        }
    }
}
