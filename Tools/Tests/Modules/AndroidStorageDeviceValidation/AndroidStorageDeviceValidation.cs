#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using CompressionLevel = System.IO.Compression.CompressionLevel;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.IO.Storage;
using MajdataPlay.Platform.Android.Runtime;
using MajdataPlay.Platform.Android.Storage;
using UnityEngine;
using Activity = MajdataPlay.Platform.Android.Runtime.Bindings.Android.App.Activity;
using AndroidKeyboard = MajdataPlay.Platform.Android.IO.AndroidKeyboard;
using AndroidKeyCode = MajdataPlay.Platform.Android.IO.KeyCode;
using DeviceJniProbe = MajdataPlay.Platform.Android.Runtime.Bindings.Net.Majdata.Validation.DeviceJniProbe;
using DocumentStream = MajdataPlay.Platform.Android.Runtime.Bindings.Net.Majdata.Majdataplay.StorageAccess.StreamHandle;
using Intent = MajdataPlay.Platform.Android.Runtime.Bindings.Android.Content.Intent;
using JavaObject = MajdataPlay.Platform.Android.Runtime.Bindings.Java.Lang.JavaObject;
using JavaIllegalStateException = MajdataPlay.Platform.Android.Runtime.Bindings.Java.Lang.IllegalStateException;
using JavaExceptionInInitializerError = MajdataPlay.Platform.Android.Runtime.Bindings.Java.Lang.ExceptionInInitializerError;
using JavaInteger = MajdataPlay.Platform.Android.Runtime.Bindings.Java.Lang.Integer;
using JavaNoClassDefFoundError = MajdataPlay.Platform.Android.Runtime.Bindings.Java.Lang.NoClassDefFoundError;
using JavaNumberFormatException = MajdataPlay.Platform.Android.Runtime.Bindings.Java.Lang.NumberFormatException;
using JavaString = MajdataPlay.Platform.Android.Runtime.Bindings.Java.Lang.String;
using JavaUri = MajdataPlay.Platform.Android.Runtime.Bindings.Android.Net.Uri;
using KeyEvent = MajdataPlay.Platform.Android.Runtime.Bindings.Android.View.KeyEvent;
using WrapperValue = MajdataPlay.Platform.Android.Runtime.Bindings.Net.Majdata.Validation.DeviceJniProbe.WrapperValue;
using StorageFileSystem = MajdataPlay.IO.Storage.FileSystem;
using UnityPlayer = MajdataPlay.Platform.Android.Runtime.Bindings.Com.Unity3d.Player.UnityPlayer;

namespace MajdataPlay.Platform.Android.Runtime.Validation
{
    /// <summary>Runs packaged-resource, generated JNI, and SAF checks in an isolated development Android player.</summary>
    /// <remarks>Only a newly created unique child is deleted. Saved grant URIs never appear in logs or results.</remarks>
    public sealed class AndroidStorageDeviceValidation : MonoBehaviour
    {
        /// <summary>Names the private grant state consumed after a force-stop.</summary>
        public const string StateFileName = "android-storage-device-state.json";

        /// <summary>Names the durable, URI-free machine-readable status.</summary>
        public const string ResultFileName = "android-storage-device-result.json";

        /// <summary>Names the durable, URI-free plain-text marker.</summary>
        public const string MarkerFileName = "android-storage-device-result.txt";

        /// <summary>Names the launch extra selecting full, replay, release, streams, jar, or wrappers.</summary>
        public const string ModeExtra = "validationMode";

        /// <summary>Names the optional fresh-run identifier supplied by the ADB runner.</summary>
        public const string RunIdExtra = "validationRunId";

        /// <summary>Owns cancellation for this component's lifetime.</summary>
        private CancellationTokenSource? _lifetime;

        /// <summary>Retains the exception-observing operation instead of launching an unobserved async void method.</summary>
        private Task? _runTask;

        /// <summary>Stores the app-private path captured on the Unity main thread.</summary>
        private string _persistentPath = string.Empty;

        /// <summary>Stores the APK resource root captured on the Unity main thread.</summary>
        private string _streamingAssetsPath = string.Empty;

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
            _streamingAssetsPath = Application.streamingAssetsPath;
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
                "MajdataPlay storage / generated JNI validation\n" + _result.Marker +
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
                _result.ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString();
#if ENABLE_IL2CPP
                _result.EnableIl2Cpp = true;
#endif
                Directory.CreateDirectory(_persistentPath);
                Mark("BOOT_READY", "Starting controlled validation.");
                Require(_result.EnableIl2Cpp, "ENABLE_IL2CPP must be defined by the real player build.");
                Mark("IL2CPP_PASSED", "The runtime was compiled with ENABLE_IL2CPP.");
                _mode = InvokeAttached(ReadMode);
                _result.Mode = _mode;
                Require(_mode == "full" || _mode == "replay" || _mode == "release" || _mode == "streams" || _mode == "jar" || _mode == "wrappers", "Unknown validationMode.");
                InvokeAttached(() =>
                {
                    _result.SdkInt = DeviceJniProbe.GetSdkInt();
                    _result.ProcessId = DeviceJniProbe.GetProcessId();
                    _result.RuntimeInitialized = AndroidRuntime.IsInitialized;
                    _result.RuntimeSdkVersion = AndroidRuntime.SdkVersion;
                    _result.RuntimeTargetSdkVersion = AndroidRuntime.TargetSdkVersion;
                    _result.RuntimeVersionCode = AndroidRuntime.VersionCode;
                    Require(DeviceJniProbe.Is64Bit() == (IntPtr.Size == 8), "Java and managed process bitness disagree.");
                    Require(_result.RuntimeInitialized, "Production AndroidRuntime.Init did not complete successfully.");
                    Require(_result.RuntimeSdkVersion == _result.SdkInt, "AndroidRuntime SDK version differs from the running device.");
                    Require(_result.RuntimeTargetSdkVersion == 35 &&
                        _result.RuntimeTargetSdkVersion == DeviceJniProbe.GetTargetSdkVersion(),
                        "AndroidRuntime target SDK differs from the installed validation package's target API 35.");
                    Require(_result.RuntimeVersionCode > 0 && _result.RuntimeVersionCode == DeviceJniProbe.GetVersionCode(),
                        "AndroidRuntime version code differs from the installed validation package.");
                    return true;
                });
                await CheckRuntimeCallbacksAsync(cancellationToken);
                _result.RuntimeCallbacksPassed = true;
                await CheckRuntimeKeyboardAsync(cancellationToken);
                _result.RuntimeKeyboardPassed = true;
                Mark("RUNTIME_INIT_PASSED", "Production AndroidRuntime initialization, package metadata, new-intent, nullable activity-result and keyboard down/up callbacks passed.");
                InvokeAttached(() =>
                {
                    CheckCheckedWrapperConversions();
                    CheckEnhancedWrapperContracts();
                    _result.WrapperContractsPassed = true;
                    return true;
                });
                Mark("JNI_WRAPPERS_PASSED", "Nested generic wrappers, typed Class, CLR equality/comparison/enumeration contracts, checked conversions, and typed invocation/class-initialization exceptions passed.");
                if (_mode == "jar")
                {
                    await CheckJarStorageAsync(cancellationToken);
                    Mark("JAR_PASSED", _result.JarCheckGroups + " packaged-resource groups passed; owned local extraction and deflate fixtures removed; no picker or grants used.", true);
                    return;
                }
                InvokeAttached(() =>
                {
                    CheckJni();
                    return true;
                });
                Mark("JNI_PASSED", "Typed constructors, references, signed bytes, UTF-16 and cleared Java exceptions passed.");
                var mainThreadId = Thread.CurrentThread.ManagedThreadId;
                await Task.Run(() => InvokeAttached(() =>
                {
                    Require(Thread.CurrentThread.ManagedThreadId != mainThreadId, "The JNI worker ran on the main thread.");
                    CheckJni();
                    CheckEnhancedWrapperContracts();
                    return true;
                }), cancellationToken);
                _result.BackgroundWrapperContractsPassed = true;
                Mark("BACKGROUND_JNI_PASSED", "JNI work and deterministic wrapper disposal ran on an attached worker.");
                if (_mode == "wrappers")
                {
                    Mark("WRAPPERS_PASSED", "Generated JNI and nested generic/CLR wrapper contracts passed on the main thread and an attached worker; no picker or grants used.", true);
                    return;
                }
                await Task.Run(() => InvokeAttached(() =>
                {
                    Require(DeviceJniProbe.ValidateDocumentStreams(), "Controlled descriptor stream regressions failed.");
                    CheckDescriptorAdapters();
                    return true;
                }), cancellationToken);
                Mark("DESCRIPTOR_STREAMS_PASSED", "Production Java handles passed real-file, slice, pipe, large-offset, write, append and close regressions.");
                if (_mode == "streams")
                {
                    Mark("STREAMS_PASSED", "Controlled Java descriptors and production managed stream adapters passed; no SAF picker or grants were used.", true);
                    return;
                }
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

        /// <summary>Validates the installed APK's jar resources and extracts only into a unique app-owned directory.</summary>
        /// <param name="cancellationToken">The component's lifetime token, checked before reads and transfers.</param>
        /// <returns>A task completing after every jar check and owned-directory cleanup.</returns>
        /// <exception cref="InvalidOperationException">A packaged-resource invariant fails.</exception>
        /// <exception cref="IOException">An APK entry, extraction, fixture, or cleanup cannot be accessed.</exception>
        /// <exception cref="OperationCanceledException">The component is destroyed during validation.</exception>
        private async Task CheckJarStorageAsync(CancellationToken cancellationToken)
        {
            const string Text = "MajdataPlay packaged UTF-8 你好\n";
            const string EscapedName = "space # 中文 %20.txt";
            var localPath = Path.Combine(_persistentPath, "MajdataPlay-jar-validation-" + _result.RunId);
            StorageDirectory? owned = null;
            try
            {
                Mark("JAR_LOOKUP_READY", "Opening the installed APK's real StreamingAssets jar root.");
                _result.StreamingAssetsIsJar = _streamingAssetsPath.StartsWith("jar:file:", StringComparison.OrdinalIgnoreCase);
                Require(_result.StreamingAssetsIsJar, "The real Android StreamingAssets root must use jar:file.");
                var fixtures = await Task.Run(() =>
                {
                    var assets = StorageFileSystem.Directory.Open(_streamingAssetsPath);
                    Require(assets.Exists, "The actual packaged asset root is missing.");
                    return assets.FindDirectory("JarValidation")
                        ?? throw new InvalidOperationException("The packaged JarValidation fixture directory is missing.");
                }, cancellationToken);
                var plain = fixtures.FindFile("plain.txt")
                    ?? throw new InvalidOperationException("The packaged UTF-8 fixture is missing.");
                var empty = fixtures.FindFile("empty.bin")
                    ?? throw new InvalidOperationException("The packaged empty fixture is missing.");
                var implicitDirectory = fixtures.FindDirectory("implicit")
                    ?? throw new InvalidOperationException("An implicit packaged directory was not indexed.");
                var nested = implicitDirectory.FindDirectory("nested")
                    ?? throw new InvalidOperationException("A nested implicit packaged directory was not indexed.");
                var pattern = nested.FindFile("pattern.bin")
                    ?? throw new InvalidOperationException("The packaged binary fixture is missing.");
                var names = fixtures.FindDirectory("names")
                    ?? throw new InvalidOperationException("The escaped-name fixture directory is missing.");
                var escaped = names.FindFile(EscapedName)
                    ?? throw new InvalidOperationException("The escaped-name fixture is missing.");
                await Task.Run(() =>
                {
                    var children = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var entry in fixtures.EnumerateEntries())
                    {
                        Require(children.Add(entry.Name), "Packaged enumeration duplicated an immediate child.");
                        Require(entry.Name.IndexOf('/') < 0, "Packaged enumeration returned a descendant as an immediate child.");
                    }
                    Require(children.SetEquals(new[] { "plain.txt", "empty.bin", "implicit", "names" }), "Packaged immediate enumeration returned unexpected children.");
                    Require(pattern.Entry?.Length == 65553 && empty.Entry?.Length == 0, "Packaged lengths differ from fixture lengths.");
                    Require(fixtures.FindFile("PLAIN.TXT") is null && fixtures.FindFile("missing.bin") is null, "Packaged lookup did not preserve case-sensitive absence.");
                    Require(fixtures.FindDirectory("plain.txt") is null && fixtures.FindFile("implicit") is null, "Packaged lookup confused files and directories.");
                }, cancellationToken);
                _result.JarCheckGroups++;
                Mark("JAR_LOOKUP_PASSED", "Actual APK lookup, lengths, case-sensitive absence and immediate implicit-directory enumeration passed.");

                Require(await Task.Run(() => plain.ReadAllText(), cancellationToken) == Text, "Synchronous APK UTF-8 read differs.");
                Require(await plain.ReadAllTextAsync(cancellationToken: cancellationToken) == Text, "Asynchronous APK UTF-8 read differs.");
                CheckJarPattern(await Task.Run(pattern.ReadAllBytes, cancellationToken));
                CheckJarPattern(await pattern.ReadAllBytesAsync(cancellationToken));
                Require((await empty.ReadAllBytesAsync(cancellationToken)).Length == 0, "The APK empty entry returned bytes.");
                _result.JarCheckGroups++;
                Mark("JAR_READS_PASSED", "Synchronous and asynchronous UTF-8 and 65553-byte pattern reads plus the empty APK entry passed.");

                Require(escaped.Location.IndexOf("%2520", StringComparison.Ordinal) >= 0 &&
                    escaped.Location.IndexOf("%23", StringComparison.Ordinal) >= 0 &&
                    escaped.Location.IndexOf("%20", StringComparison.Ordinal) >= 0 &&
                    escaped.Location.IndexOf("%E4%B8%AD", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Packaged child names were not escaped exactly once.");
                Require(await escaped.ReadAllTextAsync(cancellationToken: cancellationToken) == Text, "An escaped APK child returned different text.");
                Require(await StorageFileSystem.File.Open(escaped.Location).ReadAllTextAsync(cancellationToken: cancellationToken) == Text,
                    "Reopening an authoritative escaped APK URI decoded its literal percent name incorrectly.");
                _result.JarCheckGroups++;
                Mark("JAR_ESCAPING_PASSED", "APK names containing space, hash, CJK and literal percent-20 reopened through authoritative escaped jar locations.");

                await Task.Run(() =>
                {
                    using var first = pattern.OpenRead();
                    using var second = pattern.OpenRead();
                    Require(first.CanRead && second.CanRead && !first.CanWrite && !second.CanWrite, "APK stream capabilities are incorrect.");
                    Require(first.ReadByte() == 0 && second.ReadByte() == 0, "Independent APK streams did not start independently.");
                    first.Dispose();
                    first.Dispose();
                    Require(!first.CanRead && !first.CanWrite && !first.CanSeek, "A disposed APK stream retained capabilities.");
                    Require(second.ReadByte() == 1, "Closing one APK stream invalidated a separately owned archive stream.");
                    using var remainder = new MemoryStream();
                    second.CopyTo(remainder);
                    Require(remainder.Length == 65551 && second.ReadByte() == -1, "An independent APK stream did not reach the exact logical EOF.");
                    using var reopened = pattern.OpenRead();
                    Require(reopened.ReadByte() == 0, "The APK entry did not reopen after another stream was disposed.");
                }, cancellationToken);
                _result.JarCheckGroups++;
                Mark("JAR_OWNERSHIP_PASSED", "Independent APK streams, deterministic idempotent disposal, exact EOF and reopening passed.");

                owned = await Task.Run(() =>
                {
                    Require(!Directory.Exists(localPath) && !File.Exists(localPath), "The unique local scratch location was already occupied.");
                    return StorageFileSystem.Directory.Create(localPath);
                }, cancellationToken);
                var destination = owned;
                var copiedText = await Task.Run(() => plain.CopyTo(destination, "sync.txt", cancellationToken: cancellationToken), cancellationToken);
                var copiedPattern = await pattern.CopyToAsync(destination, "async.bin", cancellationToken: cancellationToken);
                var copiedEmpty = await empty.CopyToAsync(destination, "empty.bin", cancellationToken: cancellationToken);
                Require(await copiedText.ReadAllTextAsync(cancellationToken: cancellationToken) == Text, "Synchronous APK extraction changed UTF-8 text.");
                CheckJarPattern(await copiedPattern.ReadAllBytesAsync(cancellationToken));
                Require(copiedEmpty.Entry?.Length == 0, "Asynchronous APK extraction changed an empty file.");
                using (var canceled = new CancellationTokenSource())
                {
                    canceled.Cancel();
                    Expect<OperationCanceledException>(() => pattern.CopyToAsync(destination, "cancelled.bin", cancellationToken: canceled.Token).GetAwaiter().GetResult());
                    Require(destination.FindFile("cancelled.bin") is null, "Pre-cancelled APK extraction created a destination.");
                    Expect<OperationCanceledException>(() => pattern.ReadAllBytesAsync(canceled.Token).GetAwaiter().GetResult());
                }
                _result.JarCheckGroups++;
                Mark("JAR_EXTRACTION_PASSED", "CopyTo and CopyToAsync extracted verified local text, binary and empty files; pre-cancellation had no destination side effects.");

                await Task.Run(() =>
                {
                    Expect<NotSupportedException>(() => plain.OpenWrite());
                    Expect<NotSupportedException>(() => plain.WriteAllText("forbidden"));
                    Expect<NotSupportedException>(() => plain.Rename("forbidden.txt"));
                    Expect<NotSupportedException>(plain.Delete);
                    Expect<NotSupportedException>(() => plain.SetAttributes(FileAttributes.Hidden));
                    Expect<NotSupportedException>(() => plain.SetLastWriteTime(DateTime.UtcNow));
                    Expect<NotSupportedException>(() => fixtures.CreateFile("forbidden.bin"));
                    Expect<NotSupportedException>(() => fixtures.CreateDirectory("forbidden"));
                    Expect<NotSupportedException>(() => fixtures.Rename("forbidden"));
                    Expect<NotSupportedException>(() => fixtures.Delete(recursive: true));
                    Expect<NotSupportedException>(() => fixtures.SetAttributes(FileAttributes.Hidden));
                    Expect<NotSupportedException>(() => fixtures.SetLastWriteTime(DateTime.UtcNow));
                    Expect<NotSupportedException>(() => StorageFileSystem.File.Move(plain.Location, destination.Location, "forbidden.txt"));
                    Expect<NotSupportedException>(() => StorageFileSystem.Directory.Move(nested.Location, destination.Location, "forbidden"));
                    Require(plain.ReadAllText() == Text && pattern.Exists && fixtures.FindFile("forbidden.bin") is null,
                        "Rejected APK mutations changed the packaged source.");
                }, cancellationToken);
                _result.JarCheckGroups++;
                Mark("JAR_READONLY_PASSED", "APK write, create, rename, move, delete, attributes and timestamps were rejected with packaged resources intact.");

                await Task.Run(() =>
                {
                    var sentinel = destination.CreateFile("replace-sentinel.txt");
                    sentinel.WriteAllText("retained destination");
                    Expect<NotSupportedException>(() => StorageFileSystem.File.Replace(plain.Location, sentinel.Location));
                    Require(sentinel.ReadAllText() == "retained destination", "Rejected APK-source replacement changed its local destination.");
                    Expect<NotSupportedException>(() => StorageFileSystem.File.Replace(sentinel.Location, plain.Location));
                    Require(sentinel.ReadAllText() == "retained destination", "Rejected APK-destination replacement consumed its local source.");
                    Expect<NotSupportedException>(() => StorageFileSystem.File.Replace(sentinel.Location, copiedText.Location, plain.Location));
                    Require(sentinel.ReadAllText() == "retained destination" && copiedText.ReadAllText() == Text && plain.ReadAllText() == Text,
                        "Rejected APK-backup replacement mutated an operand.");
                }, cancellationToken);
                _result.JarCheckGroups++;
                Mark("JAR_REPLACE_PASSED", "APK source, destination and backup replacement operands were rejected before changing local sentinel files.");

                await Task.Run(() =>
                {
                    var archivePath = Path.Combine(localPath, "owned-deflate.zip");
                    var bytes = new byte[65553];
                    for (var i = 0; i < bytes.Length; i++)
                    {
                        bytes[i] = (byte)(i % 251);
                    }
                    using (var output = File.Create(archivePath))
                    using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
                    {
                        var entry = archive.CreateEntry("deflated/pattern.bin", CompressionLevel.Optimal);
                        using var stream = entry.Open();
                        stream.Write(bytes, 0, bytes.Length);
                    }
                    using (var input = File.OpenRead(archivePath))
                    using (var archive = new ZipArchive(input, ZipArchiveMode.Read))
                    {
                        var entry = archive.GetEntry("deflated/pattern.bin")
                            ?? throw new InvalidOperationException("The owned deflate fixture is missing.");
                        Require(entry.CompressedLength < entry.Length, "The owned deflate fixture was not actually compressed.");
                    }
                    var compressed = StorageFileSystem.File.Open("jar:" + new Uri(archivePath).AbsoluteUri + "!/deflated/pattern.bin");
                    CheckJarPattern(compressed.ReadAllBytes());
                    CheckJarPattern(compressed.ReadAllBytesAsync(cancellationToken).GetAwaiter().GetResult());
                    var extracted = compressed.CopyTo(destination, "deflated-extracted.bin", cancellationToken: cancellationToken);
                    CheckJarPattern(extracted.ReadAllBytes());
                    _result.JarDeflateChecked = true;
                }, cancellationToken);
                _result.JarCheckGroups++;
                Mark("JAR_DEFLATE_PASSED", "A verified compressed app-owned ZIP entry passed real IL2CPP synchronous, asynchronous and extracted-byte checks.");
            }
            finally
            {
                if (owned is not null)
                {
                    // The unique owned directory is the only recursive-delete target; the APK and app parent are never targets.
                    await Task.Run(() => owned.Delete(recursive: true));
                    Require(!Directory.Exists(localPath), "The unique jar validation scratch directory survived cleanup.");
                    _result.CleanupSucceeded = true;
                    Mark("JAR_CLEANUP_PASSED", "Only this invocation's unique app-local extraction and deflate fixture directory was removed.");
                }
            }
        }

        /// <summary>Checks every byte in the packaged binary fixture, including the transfer-buffer boundary.</summary>
        /// <param name="bytes">The complete bytes read or extracted through production storage APIs.</param>
        /// <exception cref="InvalidOperationException">The fixture length or any byte differs.</exception>
        private static void CheckJarPattern(byte[] bytes)
        {
            Require(bytes.Length == 65553, "The jar binary fixture length differs from 65553 bytes.");
            for (var i = 0; i < bytes.Length; i++)
            {
                Require(bytes[i] == (byte)(i % 251), "A jar binary fixture byte differs from its deterministic pattern.");
            }
        }

        /// <summary>Reads the launch extra through generated Activity and Intent wrappers.</summary>
        /// <returns>The requested mode, or full when no mode was supplied; also retains a validated runner run identifier.</returns>
        /// <exception cref="InvalidOperationException">Unity has no current activity or launch intent.</exception>
        /// <exception cref="JavaInvocationException">Java rejects an intent query.</exception>
        private string ReadMode()
        {
            using Activity activity = UnityPlayer.CurrentActivity
                ?? throw new InvalidOperationException("Unity has no current activity.");
            using var intent = activity.GetIntent()
                ?? throw new InvalidOperationException("The activity has no launch intent.");
            var runId = intent.GetStringExtra(RunIdExtra);
            if (!string.IsNullOrEmpty(runId))
            {
                Require(Guid.TryParseExact(runId, "N", out var parsedRunId), "The supplied validation run identifier is not a compact GUID.");
                _result.RunId = parsedRunId.ToString("N");
            }
            var mode = intent.GetStringExtra(ModeExtra);
            return string.IsNullOrEmpty(mode) || mode == "pick" ? "full" : mode;
        }

        /// <summary>Tests the production managed stream adapter against real controlled Android descriptors.</summary>
        /// <exception cref="InvalidOperationException">A capability, transfer, position, or lifetime assertion fails.</exception>
        /// <exception cref="IOException">A controlled descriptor cannot be opened or accessed.</exception>
        /// <exception cref="JavaInvocationException">The real Android descriptor fixture fails.</exception>
        private static void CheckDescriptorAdapters()
        {
            foreach (var kind in new[] { "read", "slice", "large", "pipe", "bounded-pipe" })
            {
                using var stream = OpenDescriptorFixture(kind, true);
                var seekable = kind != "pipe" && kind != "bounded-pipe";
                Require(stream.CanRead && !stream.CanWrite && stream.CanSeek == seekable, "The fixture advertised incorrect capabilities.");
                if (kind == "large")
                {
                    var offset = (1L << 32) + 17;
                    Require(stream.Length == offset + 2 && stream.Position == 0, "The large length lost its 64-bit value.");
                    stream.Position = offset;
                    Require(stream.Position == offset && stream.ReadByte() == 23, "The large Position setter lost its 64-bit value.");
                    Require(stream.Seek(-1, SeekOrigin.End) == offset + 1 && stream.ReadByte() == 24, "Large end-relative seek failed.");
                }
                else
                {
                    var expected = kind == "slice" ? new byte[] { 128, 255, 0, 127 } : new byte[] { 10, 20, 30, 40 };
                    if (seekable)
                    {
                        Require(stream.Length == 4 && stream.Position == 0, "A fixture has the wrong logical range.");
                        Require(stream.Seek(1, SeekOrigin.Begin) == 1 && stream.ReadByte() == expected[1], "Begin seek failed.");
                        Require(stream.Seek(-1, SeekOrigin.Current) == 1, "Current-relative seek failed.");
                        Require(stream.Seek(-1, SeekOrigin.End) == 3 && stream.ReadByte() == expected[3], "End-relative seek failed.");
                        Require(stream.ReadByte() == -1, "The fixture exposed bytes outside its logical end.");
                        stream.Position = 1;
                        Expect<ArgumentOutOfRangeException>(() => stream.Position = -1);
                        Expect<ArgumentException>(() => stream.Seek(0, (SeekOrigin)99));
                        Expect<IOException>(() => stream.Seek(-2, SeekOrigin.Current));
                        Expect<IOException>(() => stream.Seek(long.MaxValue, SeekOrigin.Current));
                        Require(stream.Position == 1, "A rejected seek changed the fixture position.");
                        stream.Position = 0;
                    }
                    else
                    {
                        Expect<NotSupportedException>(() =>
                        {
                            _ = stream.Length;
                        });
                        Expect<NotSupportedException>(() =>
                        {
                            _ = stream.Position;
                        });
                        Expect<NotSupportedException>(() => stream.Position = 0);
                        Expect<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
                    }
                    using var copy = new MemoryStream();
                    stream.CopyTo(copy);
                    var actual = copy.ToArray();
                    Require(actual.Length == expected.Length, "Default CopyTo returned the wrong logical length.");
                    for (var i = 0; i < expected.Length; i++)
                    {
                        Require(actual[i] == expected[i], "The fixture copied an incorrect signed-byte pattern.");
                    }
                    if (seekable)
                    {
                        stream.Position = 0;
                        using var asyncCopy = new MemoryStream();
                        stream.CopyToAsync(asyncCopy).GetAwaiter().GetResult();
                        Require(asyncCopy.Length == expected.Length, "Default CopyToAsync failed after rewinding from EOF.");
                        stream.Position = 8;
                        Require(stream.ReadByte() == -1, "Seeking past EOF exposed outside bytes.");
                    }
                }
                stream.Dispose();
                stream.Dispose();
                Require(!stream.CanRead && !stream.CanWrite && !stream.CanSeek, "A disposed fixture kept its capabilities.");
                Expect<ObjectDisposedException>(() =>
                {
                    _ = stream.Length;
                });
                Expect<ObjectDisposedException>(() => stream.Position = 0);
                Expect<ObjectDisposedException>(() => stream.Seek(0, SeekOrigin.Begin));
            }
            using (var stream = OpenDescriptorFixture("write", false))
            {
                Require(stream.CanWrite && !stream.CanRead && stream.CanSeek, "Regular output lost its seek capability.");
                stream.Write(new byte[] { 1, 2, 3 }, 0, 3);
                Require(stream.Length == 3 && stream.Position == 3, "Writing did not update descriptor length and position.");
                stream.Position = 1;
                stream.WriteByte(9);
                Require(stream.Position == 2 && stream.Length == 3, "Output positioning changed the wrong range.");
                var offset = (1L << 32) + 17;
                Require(stream.Seek(offset, SeekOrigin.Begin) == offset, "Large output seek lost its 64-bit value.");
                stream.WriteByte(7);
                Require(stream.Length == offset + 1 && stream.Position == offset + 1, "Large output length or position was truncated.");
                stream.Flush();
                Expect<NotSupportedException>(() => stream.SetLength(1));
            }
            using (var append = OpenDescriptorFixture("append", false))
            {
                Require(append.CanWrite && !append.CanSeek, "Append must keep sequential semantics.");
                Expect<NotSupportedException>(() => append.Position = 0);
                append.WriteByte(50);
            }
        }

        /// <summary>Adopts a controlled real descriptor using the same internal constructor as the production provider opener.</summary>
        /// <param name="kind">The Java descriptor scenario, without a URI or permission grant.</param>
        /// <param name="readable">Whether the scenario supplies an input rather than an output descriptor.</param>
        /// <returns>A caller-owned production managed stream adapter.</returns>
        /// <exception cref="InvalidOperationException">The fixture or retained production constructor is missing.</exception>
        /// <exception cref="TargetInvocationException">The production stream constructor fails.</exception>
        /// <exception cref="JavaInvocationException">The real descriptor fixture fails.</exception>
        private static AndroidDocumentStream OpenDescriptorFixture(string kind, bool readable)
        {
            var handle = DeviceJniProbe.CreateStreamFixture(kind)
                ?? throw new InvalidOperationException("The controlled Java stream handle is missing.");
            try
            {
                var constructor = typeof(AndroidDocumentStream).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new[] { typeof(DocumentStream), typeof(bool) }, null)
                    ?? throw new InvalidOperationException("The production stream constructor was stripped.");
                return (AndroidDocumentStream)constructor.Invoke(new object[] { handle, readable });
            }
            catch
            {
                try
                {
                    using var result = handle.Close();
                }
                finally
                {
                    handle.Dispose();
                }
                throw;
            }
        }

        /// <summary>Checks public wrapper conversions and catchable typed exceptions using real Java objects.</summary>
        /// <exception cref="InvalidOperationException">Java type, ownership, exception metadata, or pending-exception checks fail.</exception>
        /// <exception cref="JavaInvocationException">An unexpected Java failure interrupts the checks.</exception>
        /// <exception cref="AndroidJavaException">Unity cannot create or dispatch the independent Java reference.</exception>
        private static void CheckCheckedWrapperConversions()
        {
            const string Text = "checked-wrapper-conversion";
            using var reference = new AndroidJavaObject("java.lang.String", Text);
            using (var borrowed = reference.ToWrapper<JavaString>(ownsReference: false)
                ?? throw new InvalidOperationException("The non-null Java String was not wrapped."))
            {
                Require(borrowed.ToString() == Text, "Checked Java String conversion lost its Java value.");
            }
            Require(reference.Call<int>("length") == Text.Length,
                "Disposing the borrowed wrapper released its externally owned Java reference.");
            using (var source = reference.ToWrapper<JavaObject>(ownsReference: false)
                ?? throw new InvalidOperationException("The Java object conversion probe was not wrapped."))
            {
                using var converted = source.As<JavaString>();
                Require(converted.ToString() == Text && !ReferenceEquals(source.JavaReference, converted.JavaReference),
                    "JavaObject.As did not create an independently owned typed reference.");
                Expect<InvalidCastException>(() => source.As<JavaUri>());
                Require(source.ToString() == Text,
                    "A rejected JavaObject.As conversion invalidated the source wrapper.");
                source.Dispose();
                Require(converted.ToString() == Text,
                    "Disposing the source wrapper invalidated an independently owned As result.");
            }
            Require(reference.Call<int>("length") == Text.Length,
                "Disposing the As result released the original externally owned Java reference.");
            Expect<InvalidCastException>(() => reference.ToWrapper<JavaUri>(ownsReference: false));
            Require(reference.Call<string>("toString") == Text,
                "A rejected Java type conversion released or invalidated its borrowed reference.");
            using (var copied = JavaWrapperConverter.FromHandle<JavaString>(reference.GetRawObject())
                ?? throw new InvalidOperationException("The real Java String handle was not wrapped."))
            {
                Require(copied.ToString() == Text, "JNI handle conversion returned the wrong Java value.");
            }
            Require(reference.Call<string>("toString") == Text,
                "Disposing an independently owned handle wrapper released the original Java reference.");

            var caught = false;
            try
            {
                JavaInteger.ParseInt("not-an-integer");
            }
            catch (JavaNumberFormatException exception)
            {
                using (exception)
                {
                    Require(exception.JavaReference.GetRawObject() != IntPtr.Zero
                        && exception.JavaClassName == "java.lang.NumberFormatException"
                        && exception.JavaStackTrace.Contains("NumberFormatException"),
                        "The typed Java exception did not retain its throwable reference and diagnostics.");
                    caught = true;
                }
            }
            Require(caught, "Integer.parseInt did not throw the matching NumberFormatException wrapper.");
            CheckClassInitializationFailures();
            var pending = AndroidJNI.ExceptionOccurred();
            if (pending != IntPtr.Zero)
            {
                AndroidJNI.ExceptionClear();
                AndroidJNI.DeleteLocalRef(pending);
                throw new InvalidOperationException("Typed Java exception conversion left a pending JVM exception.");
            }
            Require(reference.Call<string>("toString") == Text, "JNI dispatch failed after handling the typed Java exception.");
        }

        /// <summary>Exercises nested generic wrappers and CLR contracts against the actual packaged JVM classes.</summary>
        /// <exception cref="InvalidOperationException">An observed Java or CLR contract differs from its expected behavior.</exception>
        /// <exception cref="JavaInvocationException">An unexpected Java failure interrupts the checks.</exception>
        /// <exception cref="AndroidJavaException">Unity cannot resolve a Java type used by the fixtures.</exception>
        private static void CheckEnhancedWrapperContracts()
        {
            using var value = new WrapperValue(7);
            using var equal = new WrapperValue(7);
            using var greater = new WrapperValue(9);
            object managedValue = value;
            Require(managedValue.Equals(equal) && !managedValue.Equals(greater)
                && managedValue.GetHashCode() == 217 && managedValue.ToString() == "WrapperValue:7",
                "Nested Java equals/hashCode/toString overrides did not dispatch through CLR object methods.");
            var equatable = (IEquatable<WrapperValue>)value;
            Require(equatable.Equals(equal) && !equatable.Equals(greater) && !equatable.Equals(null),
                "The generated IEquatable contract differs from Java equality.");
            var dictionary = new Dictionary<WrapperValue, string> { { value, "matched" } };
            Require(dictionary.TryGetValue(equal, out var match) && match == "matched"
                && !dictionary.ContainsKey(greater), "CLR collections did not use the generated Java value contracts.");
            Require(((IComparable<WrapperValue>)value).CompareTo(greater) < 0
                && ((IComparable<WrapperValue>)value).CompareTo(equal) == 0
                && ((IComparable)value).CompareTo(greater) < 0 && ((IComparable)value).CompareTo(null) == 1,
                "Typed or untyped CLR comparison differs from Java Comparable.");
            Expect<ArgumentException>(() => ((IComparable)value).CompareTo(new object()));
            using var order = new DeviceJniProbe.WrapperOrder();
            Require(((IComparer<WrapperValue>)order).Compare(value, greater) > 0,
                "The CLR comparer did not invoke the reverse-order Java Comparator.");

            using var reflectionClass = WrapperValue.Class;
            Require(reflectionClass.GetName() == "net.majdata.validation.DeviceJniProbe$WrapperValue",
                "The typed Class wrapper did not retain the Java nested binary name.");
            using var classCast = reflectionClass.Cast(value);
            Require(classCast is not null && classCast.Value == 7,
                "Class<T>.cast did not return its typed nested Java wrapper.");
            using var box = new DeviceJniProbe.WrapperBox<WrapperValue>(value);
            using var field = box.Value;
            using var echoed = box.Echo(greater);
            Require(field is not null && field.Value == 7 && echoed is not null && echoed.Value == 9,
                "Generic class-bound fields or methods did not retain the concrete wrapper argument.");
            using var boxClass = DeviceJniProbe.WrapperBox<WrapperValue>.Class;
            Require(boxClass.GetName() == "net.majdata.validation.DeviceJniProbe$WrapperBox",
                "The generic Class wrapper used a CLR name instead of the Java erased binary name.");
            using var castBox = boxClass.Cast(box);
            Require(castBox is not null, "Class<T>.cast did not preserve the generic wrapper.");
            using var castValue = castBox!.Value;
            Require(castValue is not null && castValue.Value == 7, "The cast generic box lost its typed field.");
            var array = box.EchoArray(new WrapperValue?[] { value, null, greater });
            try
            {
                Require(array is not null && array.Length == 3 && array[0]?.Value == 7
                    && array[1] is null && array[2]?.Value == 9,
                    "Generic arrays lost their concrete wrapper argument or null element.");
            }
            finally
            {
                if (array is not null)
                {
                    foreach (var element in array)
                    {
                        element?.Dispose();
                    }
                }
            }
            using (var iterator = ((IEnumerable<WrapperValue>)box).GetEnumerator())
            {
                Require(iterator.MoveNext(), "The CLR generic enumerator did not advance the Java iterator.");
                using var item = iterator.Current;
                Require(item is not null && item.Value == 7 && !iterator.MoveNext(),
                    "The CLR generic enumerator did not preserve the single typed Java value.");
            }
            using var inner = new DeviceJniProbe.WrapperBox<WrapperValue>.Inner<WrapperValue>(box, greater);
            using var innerValue = inner.GetValue();
            Require(innerValue is not null && innerValue.Value == 9,
                "The nonstatic nested generic constructor or its transitive outer bound failed.");
            using var innerClass = DeviceJniProbe.WrapperBox<WrapperValue>.Inner<WrapperValue>.Class;
            Require(innerClass.GetName() == "net.majdata.validation.DeviceJniProbe$WrapperBox$Inner",
                "The multilevel generic Class wrapper lost its enclosing Java structure.");
            using var selfBox = new DeviceJniProbe.SelfBox<WrapperValue>();
            using var selfValue = selfBox.Echo(value);
            Require(selfValue is not null && selfValue.Value == 7,
                "The recursive Comparable constraint did not preserve the concrete Java wrapper.");

            using (var borrowed = value.JavaReference.ToWrapper<WrapperValue>(ownsReference: false))
            {
                Require(borrowed is not null && borrowed.Value == 7, "Nested type conversion returned the wrong value.");
            }
            using (var copied = JavaWrapperConverter.FromHandle<WrapperValue>(value.JavaReference.GetRawObject()))
            {
                Require(copied is not null && copied.Value == 7, "Nested handle conversion returned the wrong value.");
            }
            using (var copied = value.As<WrapperValue>())
            {
                Require(copied.Value == 7 && !ReferenceEquals(value.JavaReference, copied.JavaReference),
                    "Nested As conversion did not create an independent reference.");
            }
            Expect<InvalidCastException>(() => value.JavaReference.ToWrapper<JavaUri>(ownsReference: false));
            Expect<InvalidCastException>(() => JavaWrapperConverter.FromHandle<JavaUri>(value.JavaReference.GetRawObject()));
            Require(value.Value == 7, "A rejected conversion or independently disposed wrapper invalidated its source.");
            using (var typedBox = box.JavaReference.ToWrapper<DeviceJniProbe.WrapperBox<WrapperValue>>(ownsReference: false))
            {
                Require(typedBox is not null, "The closed generic wrapper factory was not initialized under IL2CPP.");
            }
            CheckWrapperFailure(DeviceJniProbe.ThrowWrapperFailure,
                "net.majdata.validation.DeviceJniProbe$WrapperFailure");
            CheckWrapperFailure(DeviceJniProbe.ThrowPrivateWrapperFailure,
                "net.majdata.validation.DeviceJniProbe$PrivateWrapperFailure");
            Require(value.Value == 7, "JNI dispatch did not recover after exported and private Java throwables.");
        }

        /// <summary>Checks exact exported throwable factories and nearest-exported-ancestor fallback under IL2CPP.</summary>
        /// <param name="action">The generated JNI call that throws the controlled Java failure.</param>
        /// <param name="className">The actual Java binary name expected in the retained throwable diagnostics.</param>
        /// <exception cref="InvalidOperationException">The expected typed failure or its retained Java reference is missing.</exception>
        /// <exception cref="JavaInvocationException">An unexpected Java throwable propagates.</exception>
        private static void CheckWrapperFailure(Action action, string className)
        {
            var caught = false;
            try
            {
                action();
            }
            catch (DeviceJniProbe.WrapperFailure exception)
            {
                using (exception)
                {
                    caught = exception.JavaClassName == className
                        && exception.JavaReference.GetRawObject() != IntPtr.Zero
                        && exception.JavaStackTrace.Contains("WrapperFailure")
                        && exception.Message.Contains("MAJDATA_VALIDATION_EXPECTED");
                }
            }
            Require(caught, "The controlled Java failure did not become its closest exported exception wrapper.");
        }

        /// <summary>Checks original throwable types from first and repeated access to an erroneous Java class.</summary>
        /// <exception cref="InvalidOperationException">A class-initialization failure loses its Java throwable type or diagnostics.</exception>
        /// <exception cref="JavaInvocationException">An unexpected Java throwable interrupts the checks.</exception>
        private static void CheckClassInitializationFailures()
        {
            var initialized = false;
            try
            {
                _ = DeviceJniProbe.InitializerFailure.Value;
            }
            catch (JavaExceptionInInitializerError exception)
            {
                using (exception)
                {
                    initialized = exception.JavaClassName == "java.lang.ExceptionInInitializerError"
                        && exception.JavaStackTrace.Contains("MAJDATA_VALIDATION_EXPECTED_INITIALIZER_FAILURE");
                }
            }
            Require(initialized, "First access to an erroneous Java class did not preserve ExceptionInInitializerError.");
            var repeated = false;
            try
            {
                _ = DeviceJniProbe.InitializerFailure.Value;
            }
            catch (JavaNoClassDefFoundError exception)
            {
                using (exception)
                {
                    repeated = exception.JavaClassName == "java.lang.NoClassDefFoundError";
                }
            }
            Require(repeated, "Repeated access to an erroneous Java class did not preserve NoClassDefFoundError.");
        }

        /// <summary>Tests generated class dispatch without handwritten JNI class or method dispatch.</summary>
        /// <exception cref="InvalidOperationException">An actual JNI result violates the expected contract.</exception>
        /// <exception cref="JavaInvocationException">An unexpected Java exception occurs.</exception>
        private static void CheckJni()
        {
            using var key = new KeyEvent(KeyEvent.ActionDown, KeyEvent.KeycodeA);
            Require(key.GetAction() == KeyEvent.ActionDown && key.GetKeyCode() == KeyEvent.KeycodeA, "KeyEvent construction failed.");
            using (var javaClass = KeyEvent.Class)
            {
                Require(javaClass.GetName() == "android.view.KeyEvent", "The typed Class wrapper represents the wrong Java class.");
            }
            Expect<InvalidCastException>(() => key.JavaReference.ToWrapper<Intent>(ownsReference: false));
            using (var fromHandle = JavaWrapperConverter.FromHandle<KeyEvent>(key.JavaReference.GetRawObject()))
            {
                Require(fromHandle is not null && fromHandle.GetKeyCode() == KeyEvent.KeycodeA,
                    "Checked JNI handle conversion failed to retain the Java object.");
            }
            using (var borrowed = key.JavaReference.ToWrapper<KeyEvent>(ownsReference: false))
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
            using var adoptable = DeviceJniProbe.CreateAdoptableKeyEvent()
                ?? throw new InvalidOperationException("The adoption probe returned null.");
            var raw = new AndroidJavaObject(adoptable.JavaReference.GetRawObject());
            using (var adopted = raw.ToWrapper<KeyEvent>())
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
            foreach (var value in new[] { 0L, -1L, (1L << 32) + 17, long.MinValue, long.MaxValue })
            {
                Require(DeviceJniProbe.EchoLong(value) == value, "A Java long changed its 64-bit value across JNI.");
            }
            const string Text = "JNI\0\u4E2D\uD83D\uDE00";
            Require(DeviceJniProbe.EchoString(Text) == Text && DeviceJniProbe.EchoString(null) is null, "UTF-16 or embedded NUL conversion failed.");
            var translated = false;
            try
            {
                DeviceJniProbe.ThrowExpected();
            }
            catch (JavaIllegalStateException exception)
            {
                using (exception)
                {
                    translated = exception.Message.Contains("MAJDATA_VALIDATION_EXPECTED_JAVA_EXCEPTION") &&
                        exception.JavaStackTrace.Contains("IllegalStateException");
                }
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
            using var first = AndroidJni.Construct("java.lang.String", "(Ljava/lang/String;)V", Text).ToWrapper<JavaObject>();
            using var equal = AndroidJni.Construct("java.lang.String", "(Ljava/lang/String;)V", Text).ToWrapper<JavaObject>();
            using var different = AndroidJni.Construct("java.lang.String", "(Ljava/lang/String;)V", "different").ToWrapper<JavaObject>();
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
            using var borrowedIdentity = identity.JavaReference.ToWrapper<JavaObject>(ownsReference: false);
            Require(identity == borrowedIdentity && borrowedIdentity == identity && identity != otherIdentity,
                "Object identity equality did not use the underlying Java object.");
            Require(identity.GetHashCode() == borrowedIdentity.GetHashCode() &&
                identity.ToString() == "java.lang.Object@" + identity.GetHashCode().ToString("x", CultureInfo.InvariantCulture),
                "Object hashCode or toString did not dispatch to Java.");
            using var borrowedKey = key.JavaReference.ToWrapper<JavaObject>(ownsReference: false);
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

        /// <summary>Dispatches inert events through the real activity and verifies the production runtime's registered proxies.</summary>
        /// <param name="cancellationToken">The owner-lifetime token.</param>
        /// <returns>A task completing after new-intent, non-null result and null-result callbacks reach managed subscribers.</returns>
        /// <exception cref="TimeoutException">Android does not execute the callbacks within ten seconds.</exception>
        /// <exception cref="OperationCanceledException">The component is destroyed.</exception>
        /// <exception cref="InvalidOperationException">The activity or registered runtime callbacks do not produce the expected events.</exception>
        /// <exception cref="JavaInvocationException">Java rejects activity dispatch or a validation callback fails.</exception>
        /// <exception cref="AndroidJavaException">Unity cannot create or schedule the UI callback proxy.</exception>
        private async Task CheckRuntimeCallbacksAsync(CancellationToken cancellationToken)
        {
            const string extraName = "runtimeValidationSentinel";
            const int requestCode = 0x4D4A;
            var sentinel = "runtime-" + _result.RunId;
            var newIntentCount = 0;
            var resultCount = 0;
            var nullResultCount = 0;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<Intent?> onNewIntent = (sender, intent) =>
            {
                try
                {
                    if (intent?.GetStringExtra(extraName) == sentinel)
                    {
                        newIntentCount++;
                    }
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            };
            OnActivityResultCallback onActivityResult = (sender, receivedRequestCode, resultCode, intent) =>
            {
                if (receivedRequestCode != requestCode && receivedRequestCode != requestCode + 1)
                {
                    return;
                }
                try
                {
                    if (receivedRequestCode == requestCode)
                    {
                        Require(resultCode == Activity.ResultOk && intent?.GetStringExtra(extraName) == sentinel,
                            "The production activity-result callback did not preserve the non-null intent.");
                        resultCount++;
                    }
                    else
                    {
                        Require(resultCode == Activity.ResultCanceled && intent is null,
                            "The production activity-result callback did not preserve the null intent.");
                        nullResultCount++;
                    }
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            };
            AndroidRuntime.OnNewIntent += onNewIntent;
            AndroidRuntime.OnActivityResult += onActivityResult;
            try
            {
                using var activity = UnityPlayer.CurrentActivity
                    ?? throw new InvalidOperationException("Unity has no current activity.");
                activity.RunOnUiThread(() =>
                {
                    try
                    {
                        DeviceJniProbe.DispatchRuntimeCallbacks(extraName, sentinel, requestCode);
                        Require(newIntentCount == 1 && resultCount == 1 && nullResultCount == 1,
                            "The real activity did not invoke each registered AndroidRuntime callback exactly once.");
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
                    throw new TimeoutException("The production AndroidRuntime callbacks did not complete.");
                }
                await completion.Task;
            }
            finally
            {
                AndroidRuntime.OnNewIntent -= onNewIntent;
                AndroidRuntime.OnActivityResult -= onActivityResult;
            }
        }

        /// <summary>Verifies that the production keyboard proxy changes state for real activity key-down and key-up dispatches.</summary>
        /// <param name="cancellationToken">The owner-lifetime token.</param>
        /// <returns>A task completing after the A key transitions from released to pressed and back to released.</returns>
        /// <exception cref="InvalidOperationException">The test key was already pressed before validation.</exception>
        /// <exception cref="TimeoutException">The registered keyboard proxy does not produce the expected state within ten seconds.</exception>
        /// <exception cref="OperationCanceledException">The component is destroyed.</exception>
        /// <exception cref="JavaInvocationException">Java rejects scheduling the key event.</exception>
        private static async Task CheckRuntimeKeyboardAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(!AndroidKeyboard.IsPressed(AndroidKeyCode.A), "The validation key must initially be released.");
            try
            {
                InvokeAttached(() =>
                {
                    DeviceJniProbe.DispatchRuntimeKeyEvent(KeyEvent.ActionDown, (int)AndroidKeyCode.A);
                    return true;
                });
                await WaitForRuntimeKeyboardStateAsync(true, cancellationToken);
            }
            finally
            {
                InvokeAttached(() =>
                {
                    DeviceJniProbe.DispatchRuntimeKeyEvent(KeyEvent.ActionUp, (int)AndroidKeyCode.A);
                    return true;
                });
                await WaitForRuntimeKeyboardStateAsync(false, cancellationToken);
            }
        }

        /// <summary>Waits for the Android UI callback to update the production A-key state.</summary>
        /// <param name="expectedPressed">Whether the production key state must become pressed.</param>
        /// <param name="cancellationToken">The owner-lifetime token.</param>
        /// <returns>A task completing when the expected state becomes visible.</returns>
        /// <exception cref="TimeoutException">The expected state is absent after ten seconds.</exception>
        /// <exception cref="OperationCanceledException">The component is destroyed.</exception>
        private static async Task WaitForRuntimeKeyboardStateAsync(bool expectedPressed, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            cancellationToken.ThrowIfCancellationRequested();
            while (AndroidKeyboard.IsPressed(AndroidKeyCode.A) != expectedPressed)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException("The production keyboard callback did not update the expected key state.");
                }
                await Task.Delay(16, cancellationToken);
            }
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
                    Require(stream.CanRead && !stream.CanWrite, "The provider read stream reported unexpected capabilities.");
                    _result.ProviderReadCanSeek = stream.CanSeek;
                    if (stream.CanSeek)
                    {
                        Require(stream.Length == bytes.Length && stream.Position == 0, "The opened stream has the wrong logical range.");
                        Require(stream.Seek(17, SeekOrigin.Begin) == 17 && stream.ReadByte() == bytes[17], "Begin seek read the wrong byte.");
                        Require(stream.Seek(-1, SeekOrigin.Current) == 17 && stream.ReadByte() == bytes[17], "Current seek read the wrong byte.");
                        Require(stream.Seek(-1, SeekOrigin.End) == bytes.Length - 1 && stream.ReadByte() == bytes[bytes.Length - 1],
                            "End seek read the wrong byte.");
                        Require(stream.ReadByte() == -1, "The stream did not reach EOF.");
                        stream.Position = 0;
                        using var streamCopy = new MemoryStream();
                        await stream.CopyToAsync(streamCopy, cancellationToken);
                        Require(streamCopy.Length == bytes.Length, "Default CopyToAsync failed on the seekable provider stream.");
                        var streamCopyBytes = streamCopy.ToArray();
                        for (var i = 0; i < bytes.Length; i++)
                        {
                            Require(streamCopyBytes[i] == bytes[i], "Seeking or copying changed a provider byte.");
                        }
                        stream.Position = 1;
                        Expect<ArgumentOutOfRangeException>(() => stream.Position = -1);
                        Expect<ArgumentException>(() => stream.Seek(0, (SeekOrigin)99));
                        Expect<IOException>(() => stream.Seek(-2, SeekOrigin.Current));
                        Expect<IOException>(() => stream.Seek(long.MaxValue, SeekOrigin.Current));
                        Require(stream.Position == 1, "A rejected seek changed the stream position.");
                        stream.Position = bytes.Length + 1;
                        Require(stream.ReadByte() == -1, "Reading past EOF must not expose unrelated bytes.");
                        Mark("STREAM_SEEK_PASSED", "The actual provider read stream supported logical length, all seek origins and Position changes.");
                    }
                    else
                    {
                        Expect<NotSupportedException>(() =>
                        {
                            _ = stream.Length;
                        });
                        Expect<NotSupportedException>(() =>
                        {
                            _ = stream.Position;
                        });
                        Expect<NotSupportedException>(() => stream.Position = 0);
                        Expect<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
                        Require(stream.ReadByte() == bytes[0], "Capability queries consumed sequential provider data.");
                        Mark("STREAM_SEQUENTIAL_PASSED", "The actual provider read stream retained sequential fallback without consuming probe data.");
                    }
                    stream.Dispose();
                    stream.Dispose();
                    Require(!stream.CanRead && !stream.CanWrite && !stream.CanSeek, "A disposed stream retained a live capability.");
                    Expect<ObjectDisposedException>(() =>
                    {
                        _ = stream.Length;
                    });
                    Expect<ObjectDisposedException>(() => stream.Position = 0);
                    Expect<ObjectDisposedException>(() => stream.Seek(0, SeekOrigin.Begin));
                }
                Mark("STREAM_PASSED", "Provider streams exposed only actual capabilities and kept correct seek, copy, EOF and disposal semantics.");
                using (var truncate = await Task.Run(() => renamed.OpenWrite(append: false), cancellationToken))
                {
                    await truncate.WriteAsync(new byte[] { 1, 2, 3 }, 0, 3, cancellationToken);
                    _result.ProviderWriteCanSeek = truncate.CanSeek;
                    if (truncate.CanSeek)
                    {
                        Require(truncate.Length == 3 && truncate.Position == 3, "Writing did not update stream length and position.");
                        truncate.Position = 1;
                        truncate.WriteByte(2);
                        Require(truncate.Seek(0, SeekOrigin.End) == 3, "Seeking a write stream used stale length.");
                        Mark("STREAM_WRITE_SEEK_PASSED", "The actual provider truncate stream supported positioning and live output lengths.");
                    }
                }
                using (var append = await Task.Run(() => renamed.OpenWrite(append: true), cancellationToken))
                {
                    Require(!append.CanSeek, "Append must retain the provider's sequential wa semantics.");
                    Expect<NotSupportedException>(() => append.Position = 0);
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
            /// <summary>Records the architecture reported by the actual managed process.</summary>
            public string ProcessArchitecture = string.Empty;
            /// <summary>Records the running device SDK version.</summary>
            public int SdkInt;
            /// <summary>Records the current process ID.</summary>
            public int ProcessId;
            /// <summary>Records that production initialization reached its successful terminal state.</summary>
            public bool RuntimeInitialized;
            /// <summary>Records the device SDK reported by the initialized production runtime.</summary>
            public int RuntimeSdkVersion;
            /// <summary>Records the installed package target SDK reported by the initialized production runtime.</summary>
            public int RuntimeTargetSdkVersion;
            /// <summary>Records the installed package version code reported by the initialized production runtime.</summary>
            public long RuntimeVersionCode;
            /// <summary>Records successful real-activity dispatch through the production runtime's registered callback proxies.</summary>
            public bool RuntimeCallbacksPassed;
            /// <summary>Records real activity key-down and key-up dispatch through the production keyboard's registered callback proxy.</summary>
            public bool RuntimeKeyboardPassed;
            /// <summary>Records nested generic, reflection, CLR contract, conversion and exception checks on the main thread.</summary>
            public bool WrapperContractsPassed;
            /// <summary>Records the same generated wrapper contracts on a JVM-attached background thread.</summary>
            public bool BackgroundWrapperContractsPassed;
            /// <summary>Records that the captured real Android StreamingAssets root used jar:file.</summary>
            public bool StreamingAssetsIsJar;
            /// <summary>Counts completed jar validation groups independently of intermediate phase markers.</summary>
            public int JarCheckGroups;
            /// <summary>Records successful reads and extraction from a verified compressed app-owned ZIP entry.</summary>
            public bool JarDeflateChecked;
            /// <summary>Records whether the actual full/replay provider read stream supported seeking.</summary>
            public bool ProviderReadCanSeek;
            /// <summary>Records whether the actual full/replay provider truncate output supported seeking.</summary>
            public bool ProviderWriteCanSeek;
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
