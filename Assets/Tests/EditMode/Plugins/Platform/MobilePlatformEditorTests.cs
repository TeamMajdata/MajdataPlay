using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using MajdataPlay.Platform.Android;
using MajdataPlay.Platform.Android.IO;
using MajdataPlay.Platform.Android.Storage;
using MajdataPlay.Platform.iOS;
using MajdataPlay.Platform.MacOS;
using NUnit.Framework;
using UnityEngine;
using AndroidKeyCode = MajdataPlay.Platform.Android.IO.KeyCode;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Platform
{
    /// <summary>Verifies the Editor-compiled behaviour of the Android, iOS and macOS platform layers on a Windows Editor.</summary>
    public class MobilePlatformEditorTests
    {
        /// <summary>Checks the Android keyboard state buffer and its Java callback adapter.</summary>
        [Test]
        public void AndroidKeyboard_TracksDispatchedKeys()
        {
            var states = PrivateMember.GetStaticField<bool[]>(typeof(AndroidKeyboard), "_keyStates");
            var saved = (bool[])states.Clone();
            try
            {
                var proxyType = typeof(AndroidKeyboard).GetNestedType("OnDispatchKeyEventCallbackProxy", System.Reflection.BindingFlags.NonPublic)!;
                var proxy = Activator.CreateInstance(proxyType, true)!;
                var key = (AndroidKeyCode)42;

                PrivateMember.Invoke(proxy, "OnDispatchKeyEvent", 0, 42);
                Assert.That(AndroidKeyboard.IsPressed(key), Is.True);
                Assert.That(AndroidKeyboard.IsPreesedUnsafe(key), Is.True);
                Assert.That(AndroidKeyboard.KeyStates[42], Is.True);
                Assert.That(AndroidKeyboard.KeyStates.Length, Is.EqualTo(1024));

                PrivateMember.Invoke(proxy, "OnDispatchKeyEvent", 1, 42);
                Assert.That(AndroidKeyboard.IsPressed(key), Is.False);
                Assert.That(AndroidKeyboard.IsPreesedUnsafe(key), Is.False);
            }
            finally
            {
                Array.Copy(saved, states, saved.Length);
            }
        }

        /// <summary>Checks that the Android keyboard and runtime refuse Java access outside an Android player.</summary>
        [Test]
        public void AndroidRuntime_IsUnavailableInEditor()
        {
            Assert.Throws<PlatformNotSupportedException>(() => AndroidKeyboard.Init());
            Assert.That(AndroidRuntime.IsInitialized, Is.False);
            PrivateMember.InvokeStatic(typeof(AndroidRuntime), "Init");
            Assert.That(AndroidRuntime.IsInitialized, Is.False);
            Assert.That(AndroidRuntime.SdkVersion, Is.EqualTo(0));
            Assert.That(AndroidRuntime.TargetSdkVersion, Is.EqualTo(0));
            Assert.That(AndroidRuntime.VersionCode, Is.EqualTo(0));
            Assert.That(() => _ = AndroidRuntime.CurrentActivity, Throws.Exception);
        }

        /// <summary>Checks that activity results and new intents are forwarded to subscribers.</summary>
        [Test]
        public void AndroidRuntime_ForwardsActivityResults()
        {
            var results = new List<(int, int)>();
            OnActivityResultCallback handler = (_, request, result, intent) =>
            {
                Assert.That(intent, Is.Null);
                results.Add((request, result));
            };
            PrivateMember.InvokeStatic(typeof(AndroidRuntime), "Android_OnActivityResult", 7, -1, null);
            AndroidRuntime.OnActivityResult += handler;
            try
            {
                PrivateMember.InvokeStatic(typeof(AndroidRuntime), "Android_OnActivityResult", 7, -1, null);
            }
            finally
            {
                AndroidRuntime.OnActivityResult -= handler;
            }

            Assert.That(results, Is.EqualTo(new[] { (7, -1) }));
            Assert.DoesNotThrow(() => PrivateMember.InvokeStatic(typeof(AndroidRuntime), "Android_OnNewIntent", new object?[] { null }));
        }

        /// <summary>Checks the immutable persisted permission snapshot.</summary>
        [Test]
        public void AndroidStoragePermission_StoresGrant()
        {
            var time = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var permission = new AndroidStoragePermission("content://provider/tree/1", true, false, time);

            Assert.That(permission.Location, Is.EqualTo("content://provider/tree/1"));
            Assert.That(permission.CanRead, Is.True);
            Assert.That(permission.CanWrite, Is.False);
            Assert.That(permission.PersistedAtUtc, Is.EqualTo(time));
        }

        /// <summary>Checks argument validation and the Editor refusal of the storage access framework entry points.</summary>
        [Test]
        public void AndroidStorageAccess_ValidatesAndRefusesInEditor()
        {
            Assert.That(PickFile("bad"), Is.InstanceOf<ArgumentException>());
            Assert.That(PickFile("text/"), Is.InstanceOf<ArgumentException>());
            Assert.That(PickFile(" "), Is.InstanceOf<ArgumentException>());
            Assert.That(PickFile("text/plain\0"), Is.InstanceOf<ArgumentException>());
            Assert.That(PickFile("text/plain"), Is.InstanceOf<PlatformNotSupportedException>());
            Assert.That(AndroidStorageAccess.PickDirectoryAsync().Exception!.InnerException, Is.InstanceOf<PlatformNotSupportedException>());
            Assert.That(AndroidStorageAccess.CreateFileAsync("a.txt", "text/plain").Exception!.InnerException, Is.InstanceOf<PlatformNotSupportedException>());
            Assert.That(AndroidStorageAccess.CreateFileAsync("a.txt", "plain").Exception!.InnerException, Is.InstanceOf<ArgumentException>());
            Assert.That(AndroidStorageAccess.CreateFileAsync("a/b", "text/plain").Exception!.InnerException, Is.InstanceOf<ArgumentException>());
            Assert.Throws<PlatformNotSupportedException>(() => AndroidStorageAccess.GetPersistedPermissions());
            Assert.Throws<ArgumentException>(() => AndroidStorageAccess.ReleasePermission("file:///tmp/a"));
            Assert.Throws<ArgumentException>(() => AndroidStorageAccess.ReleasePermission("not a uri"));
            Assert.Throws<PlatformNotSupportedException>(() => AndroidStorageAccess.ReleasePermission("content://provider/tree/1"));
        }

        /// <summary>Starts a file picker and returns the exception it fails with.</summary>
        /// <param name="mimeType">The requested MIME type.</param>
        /// <returns>The failure.</returns>
        private static Exception? PickFile(string mimeType)
        {
            return AndroidStorageAccess.PickFileAsync(mimeType).Exception?.InnerException;
        }

        /// <summary>Gets the completion task of a private picker request.</summary>
        /// <param name="request">The picker request.</param>
        /// <returns>The request's completion task.</returns>
        private static System.Threading.Tasks.Task<string?> Completion(object request)
        {
            return PrivateMember.GetProperty<System.Threading.Tasks.TaskCompletionSource<string?>>(request, "Completion").Task;
        }

        /// <summary>Checks the picker request arbitration between result commitment, cancellation and shutdown.</summary>
        [Test]
        public void AndroidStorageAccess_PickerRequestArbitration()
        {
            var accessType = typeof(AndroidStorageAccess);
            var requestType = accessType.GetNestedType("PickerRequest", System.Reflection.BindingFlags.NonPublic)!;
            var saved = PrivateMember.GetStaticField<object?>(accessType, "s_pending");
            try
            {
                using var source = new System.Threading.CancellationTokenSource();
                var committed = Activator.CreateInstance(requestType, 0x4D00, true, true, false, source.Token)!;
                PrivateMember.Invoke(committed, "RegisterCancellation");
                PrivateMember.SetStaticField(accessType, "s_pending", committed);
                Assert.That(PrivateMember.GetProperty<int>(committed, "RequestCode"), Is.EqualTo(0x4D00));
                Assert.That(PrivateMember.GetProperty<bool>(committed, "Writable"), Is.True);
                Assert.That(PrivateMember.GetProperty<bool>(committed, "PersistPermission"), Is.True);
                Assert.That(PrivateMember.GetProperty<bool>(committed, "Tree"), Is.False);
                Assert.That(PrivateMember.GetProperty<bool>(committed, "IsCancellationRequested"), Is.False);

                Assert.That(PrivateMember.InvokeStatic<bool>(accessType, "TryLaunch", committed, (Action)(() => { })), Is.True);
                PrivateMember.InvokeStatic(accessType, "OnActivityResult", null, 0x4D01, -1, null);
                Assert.That(PrivateMember.GetStaticField<object?>(accessType, "s_pending"), Is.SameAs(committed));
                PrivateMember.InvokeStatic(accessType, "OnActivityResult", null, 0x4D00, 0, null);
                var completion = Completion(committed);
                Assert.That(completion.Result, Is.Null);
                Assert.That(PrivateMember.GetStaticField<object?>(accessType, "s_pending"), Is.Null);
                Assert.That(PrivateMember.InvokeStatic<bool>(accessType, "TryLaunch", committed, (Action)(() => Assert.Fail("launched"))), Is.False);

                var invalid = Activator.CreateInstance(requestType, 0x4D02, false, false, true, System.Threading.CancellationToken.None)!;
                PrivateMember.SetStaticField(accessType, "s_pending", invalid);
                PrivateMember.InvokeStatic(accessType, "OnActivityResult", null, 0x4D02, -1, null);
                var invalidCompletion = Completion(invalid);
                Assert.That(invalidCompletion.Exception!.InnerException, Is.InstanceOf<IOException>());

                using var cancelSource = new System.Threading.CancellationTokenSource();
                var canceled = Activator.CreateInstance(requestType, 0x4D03, false, true, false, cancelSource.Token)!;
                PrivateMember.Invoke(canceled, "RegisterCancellation");
                PrivateMember.SetStaticField(accessType, "s_pending", canceled);
                cancelSource.Cancel();
                Assert.That(PrivateMember.GetProperty<bool>(canceled, "IsCancellationRequested"), Is.True);
                Assert.That(Completion(canceled).IsCanceled, Is.True);
                Assert.That(PrivateMember.InvokeStatic<bool>(accessType, "TryLaunch", canceled, (Action)(() => Assert.Fail("launched"))), Is.False);
                PrivateMember.InvokeStatic(accessType, "CommitResult", canceled, (Func<string?>)(() => "content://late"));
                Assert.That(PrivateMember.GetStaticField<object?>(accessType, "s_pending"), Is.Null);

                var failed = Activator.CreateInstance(requestType, 0x4D04, false, false, false, System.Threading.CancellationToken.None)!;
                PrivateMember.SetStaticField(accessType, "s_pending", failed);
                PrivateMember.InvokeStatic(accessType, "FailRequest", failed, new IOException("launch failed"));
                Assert.That(Completion(failed).Exception!.InnerException!.Message, Is.EqualTo("launch failed"));
                Assert.That(PrivateMember.GetStaticField<object?>(accessType, "s_pending"), Is.Null);

                var aborted = Activator.CreateInstance(requestType, 0x4D05, false, false, false, System.Threading.CancellationToken.None)!;
                PrivateMember.SetStaticField(accessType, "s_pending", aborted);
                PrivateMember.InvokeStatic(accessType, "FailRequest", aborted, new OperationCanceledException());
                Assert.That(Completion(aborted).IsCanceled, Is.True);

                var shutdown = Activator.CreateInstance(requestType, 0x4D06, false, false, false, System.Threading.CancellationToken.None)!;
                PrivateMember.SetStaticField(accessType, "s_pending", shutdown);
                PrivateMember.InvokeStatic(accessType, "Shutdown");
                Assert.That(Completion(shutdown).IsCanceled, Is.True);
                Assert.That(PrivateMember.GetStaticField<object?>(accessType, "s_pending"), Is.Null);

                var resolved = Activator.CreateInstance(requestType, 0x4D07, false, false, false, System.Threading.CancellationToken.None)!;
                PrivateMember.SetStaticField(accessType, "s_pending", resolved);
                PrivateMember.InvokeStatic(accessType, "CommitResult", resolved, (Func<string?>)(() => "content://picked"));
                Assert.That(Completion(resolved).Result, Is.EqualTo("content://picked"));
                Assert.That(PrivateMember.InvokeStatic<bool>(typeof(AndroidStorageAccess), "TryLaunch", resolved, (Action)(() => { })), Is.False);
                PrivateMember.InvokeStatic(accessType, "LaunchPicker", resolved, "action", null, null);
                PrivateMember.InvokeStatic(accessType, "Initialize");
            }
            finally
            {
                PrivateMember.SetStaticField(accessType, "s_pending", saved);
            }
        }

        /// <summary>Checks that every SAF document operation refuses to run outside an Android player.</summary>
        [Test]
        public void AndroidDocumentFileSystem_RefusesInEditor()
        {
            var fileSystem = new AndroidDocumentFileSystem();
            const string location = "content://provider/document/1";

            Assert.Throws<PlatformNotSupportedException>(() => fileSystem.GetEntry(location));
            Assert.Throws<PlatformNotSupportedException>(() => fileSystem.GetChildEntry(location, "a.txt"));
            Assert.Throws<PlatformNotSupportedException>(() => fileSystem.EnumerateEntries(location));
            Assert.Throws<PlatformNotSupportedException>(() => fileSystem.OpenRead(location));
            Assert.Throws<PlatformNotSupportedException>(() => fileSystem.OpenWrite(location, true));
            Assert.Throws<PlatformNotSupportedException>(() => fileSystem.CreateFile(location, "a.txt"));
            Assert.Throws<PlatformNotSupportedException>(() => fileSystem.CreateDirectory(location, "dir"));
            Assert.Throws<PlatformNotSupportedException>(() => fileSystem.Rename(location, "b.txt"));
            Assert.Throws<PlatformNotSupportedException>(() => fileSystem.Move(location, location, "c.txt"));
            Assert.Throws<PlatformNotSupportedException>(() => fileSystem.SetAttributes(location, FileAttributes.Hidden));
            Assert.Throws<PlatformNotSupportedException>(() => fileSystem.SetLastWriteTime(location, DateTime.UtcNow));
            Assert.Throws<PlatformNotSupportedException>(() => fileSystem.DeleteFile(location));
            Assert.Throws<PlatformNotSupportedException>(() => fileSystem.DeleteDirectory(location, true));
            PrivateMember.InvokeStatic(typeof(AndroidDocumentFileSystem), "Register");
        }

        /// <summary>Checks the SAF bridge guards and its missing-result handling.</summary>
        [Test]
        public void AndroidDocumentBridge_GuardsAndConstants()
        {
            Assert.Throws<PlatformNotSupportedException>(() => AndroidDocumentBridge.EnsureAndroid());
            Assert.Throws<PlatformNotSupportedException>(() => AndroidDocumentBridge.Invoke(() => 1));
            Assert.Throws<IOException>(() => AndroidDocumentBridge.RequireResult(null));
            Assert.That(AndroidDocumentBridge.MaxTransferSize, Is.EqualTo(32768));
            Assert.That(AndroidDocumentBridge.NotFound, Is.EqualTo(2));
        }

        /// <summary>Checks the SAF stream's argument validation and disposed-state behaviour without a Java handle.</summary>
        [Test]
        public void AndroidDocumentStream_ValidatesWithoutJava()
        {
            Assert.Throws<PlatformNotSupportedException>(() => new AndroidDocumentStream(null!, true));
            var stream = (AndroidDocumentStream)RuntimeHelpers.GetUninitializedObject(typeof(AndroidDocumentStream));
            PrivateMember.SetField(stream, "_gate", new object());

            Assert.That(stream.CanRead, Is.False);
            Assert.That(stream.CanSeek, Is.False);
            Assert.That(stream.CanWrite, Is.False);
            Assert.Throws<ArgumentNullException>(() => stream.Read(null!, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Read(new byte[4], -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Write(new byte[4], 0, -1));
            Assert.Throws<ArgumentException>(() => stream.Read(new byte[4], 3, 2));
            Assert.Throws<ArgumentException>(() => stream.Write(new byte[4], 5, 0));
            Assert.Throws<ObjectDisposedException>(() => stream.Read(new byte[4], 0, 4));
            Assert.Throws<ObjectDisposedException>(() => stream.Write(new byte[4], 0, 4));
            Assert.Throws<ObjectDisposedException>(() => stream.Flush());
            Assert.Throws<ObjectDisposedException>(() => _ = stream.Length);
            Assert.Throws<ObjectDisposedException>(() => _ = stream.Position);
            Assert.Throws<ObjectDisposedException>(() => stream.Position = 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = -1);
            Assert.Throws<ObjectDisposedException>(() => stream.Seek(0, SeekOrigin.End));
            Assert.Throws<ArgumentException>(() => stream.Seek(0, (SeekOrigin)7));
            Assert.Throws<NotSupportedException>(() => stream.SetLength(0));
            Assert.DoesNotThrow(() => stream.Dispose());
        }

        /// <summary>Checks the iOS runtime state outside an iOS player and its native callbacks.</summary>
        [Test]
        public void IOSRuntime_ReportsFocusAndForwardsCallbacks()
        {
            Assert.That(IOSRuntime.IsAppFocused, Is.EqualTo(Application.isFocused));
            Assert.That(IOSRuntime.IsAppInForeground, Is.EqualTo(Application.isFocused));
            var focus = new List<bool>();
            var foreground = new List<bool>();
            EventHandler<bool> onFocus = (_, value) => focus.Add(value);
            EventHandler<bool> onForeground = (_, value) => foreground.Add(value);
            PrivateMember.InvokeStatic(typeof(IOSRuntime), "NativeFocusCallback", true);
            PrivateMember.InvokeStatic(typeof(IOSRuntime), "NativeForegroundCallback", true);
            IOSRuntime.OnAppFocusChanged += onFocus;
            IOSRuntime.OnAppForegroundChanged += onForeground;
            try
            {
                PrivateMember.InvokeStatic(typeof(IOSRuntime), "NativeFocusCallback", false);
                PrivateMember.InvokeStatic(typeof(IOSRuntime), "NativeForegroundCallback", true);
                PrivateMember.InvokeStatic(typeof(IOSRuntime), "Init");
            }
            finally
            {
                IOSRuntime.OnAppFocusChanged -= onFocus;
                IOSRuntime.OnAppForegroundChanged -= onForeground;
            }

            Assert.That(focus, Is.EqualTo(new[] { false }));
            Assert.That(foreground, Is.EqualTo(new[] { true }));
        }

        /// <summary>Checks that the iOS settings bundle returns the documented defaults in the Editor.</summary>
        [Test]
        public void IOSNativeSettings_UseDefaultsInEditor()
        {
            var names = new[]
            {
                "Inited", "AppVersion", "Online", "DebugLogLevel", "DebugNoCache", "DebugNoteFolding", "MajnetEnabled", "MajnetApi",
                "MajnetUsername", "MajnetPassword", "MajnetAutoLogin", "CustomEnabled", "CustomName", "CustomApi", "CustomUsername",
                "CustomPassword", "CustomAutoLogin"
            };
            var saved = new Dictionary<string, object?>();
            foreach (var name in names)
            {
                saved[name] = PrivateMember.GetStaticProperty<object?>(typeof(IOSNativeSettings), name);
            }

            try
            {
                IOSNativeSettings.Reload();

                Assert.That(IOSNativeSettings.Inited, Is.True);
                Assert.That(IOSNativeSettings.AppVersion, Is.EqualTo("0.1.50"));
                Assert.That(IOSNativeSettings.Online, Is.False);
                Assert.That(IOSNativeSettings.DebugLogLevel, Is.EqualTo("Info"));
                Assert.That(IOSNativeSettings.DebugNoCache, Is.False);
                Assert.That(IOSNativeSettings.DebugNoteFolding, Is.False);
                Assert.That(IOSNativeSettings.MajnetEnabled, Is.False);
                Assert.That(IOSNativeSettings.MajnetApi, Is.EqualTo("https://majdata.net/api/api3/"));
                Assert.That(IOSNativeSettings.MajnetUsername, Is.Empty);
                Assert.That(IOSNativeSettings.MajnetPassword, Is.Empty);
                Assert.That(IOSNativeSettings.MajnetAutoLogin, Is.False);
                Assert.That(IOSNativeSettings.CustomEnabled, Is.False);
                Assert.That(IOSNativeSettings.CustomName, Is.Empty);
                Assert.That(IOSNativeSettings.CustomApi, Is.Empty);
                Assert.That(IOSNativeSettings.CustomUsername, Is.Empty);
                Assert.That(IOSNativeSettings.CustomPassword, Is.Empty);
                Assert.That(IOSNativeSettings.CustomAutoLogin, Is.False);
                Assert.That(IOSNativeSettings.GetInt("any", 17), Is.EqualTo(17));
                Assert.That(IOSNativeSettings.GetBool("any", true), Is.True);
                Assert.That(IOSNativeSettings.GetString("any", "fallback"), Is.EqualTo("fallback"));
            }
            finally
            {
                foreach (var pair in saved)
                {
                    PrivateMember.SetStaticProperty(typeof(IOSNativeSettings), pair.Key, pair.Value);
                }
            }
        }

        /// <summary>Checks that the native keyboard and macOS presentation report no support in a Windows Editor.</summary>
        [Test]
        public void AppleNativeLayers_AreUnsupportedOnWindows()
        {
            var pressed = true;

            Assert.That(NativeKeyboard.Init(), Is.EqualTo(ErrorCode.NotSupported));
            Assert.That(NativeKeyboard.GetKeyboardHandle(), Is.EqualTo((nint)0));
            Assert.That(NativeKeyboard.IsPressed(GCKeyCode.KeyA, ref pressed), Is.EqualTo(ErrorCode.NotSupported));
            Assert.That(pressed, Is.False);
            pressed = true;
            Assert.That(NativeKeyboard.IsPressed(1, GCKeyCode.Spacebar, ref pressed), Is.EqualTo(ErrorCode.NotSupported));
            Assert.That(pressed, Is.False);
            Assert.That(NativeKeyboard.Free(), Is.EqualTo(ErrorCode.NotSupported));
            Assert.That(NativePresentation.EnterNativeFullscreenIfNeeded(), Is.False);
        }
    }
}
