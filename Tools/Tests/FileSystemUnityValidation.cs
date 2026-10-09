#nullable enable
using MajdataPlay.IO.Storage;
using MajdataPlay.Platform.Android.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEngine;

namespace MajdataPlay.Tests
{
    /// <summary>Runs isolated Editor smoke checks and compiles actual storage assemblies for requested players.</summary>
    public static class FileSystemUnityValidation
    {
        /// <summary>Validates the storage layer without scenes, game services, or Android JNI execution.</summary>
        public static void Run()
        {
            try
            {
                CheckEditorOperations();
                CheckPickerCompletion();
                CheckPickerConcurrency();
                var targets = GetRequestedTargets();
                foreach (var targetName in targets)
                {
                    var target = (BuildTarget)Enum.Parse(typeof(BuildTarget), targetName);
                    var group = BuildPipeline.GetBuildTargetGroup(target);
                    if (!BuildPipeline.IsBuildTargetSupported(group, target))
                    {
                        throw new InvalidOperationException("Install the Unity build module for requested target " + targetName + ".");
                    }
                    var output = Path.GetFullPath(Path.Combine("Temp", "StoragePlayerScripts", targetName,
                        Guid.NewGuid().ToString("N")));
                    System.IO.Directory.CreateDirectory(output);
                    var result = PlayerBuildInterface.CompilePlayerScripts(new ScriptCompilationSettings
                    {
                        target = target,
                        group = group,
                        options = ScriptCompilationOptions.None,
                        extraScriptingDefines = Array.Empty<string>()
                    }, output);
                    Require(result.assemblies is not null && result.assemblies.Count > 0,
                        "Player script compilation produced no assemblies for " + targetName + ".");
                    Require(ContainsAssembly(result.assemblies!, "MajdataPlay.IO.dll"), "The IO assembly was not compiled.");
                    if (target == BuildTarget.Android)
                    {
                        Require(ContainsAssembly(result.assemblies!, "MajdataPlay.Platform.Android.dll"),
                            "The Android player branch was not compiled.");
                    }
                    Debug.Log("FILE_SYSTEM_PLAYER_COMPILE_PASSED: " + targetName + " (compile only, not runtime).");
                }
                Debug.Log("FILE_SYSTEM_UNITY_PASSED: Editor local smoke, Editor JNI guards, requested player script compilation.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("FILE_SYSTEM_UNITY_FAILED: " + exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Checks basic local operations and that Android-only APIs remain safe in the Editor.</summary>
        /// <exception cref="InvalidOperationException">An expectation fails.</exception>
        private static void CheckEditorOperations()
        {
            var path = Path.GetFullPath(Path.Combine("Temp", "StorageEditorSmoke", Guid.NewGuid().ToString("N")));
            var directory = FileSystem.CreateLocalDirectory(path);
            try
            {
                var file = directory.CreateFile("storage-中文.txt", "text/plain");
                file.WriteAllText("portable storage 中文");
                Require(file.ReadAllText() == "portable storage 中文", "UTF-8 round-trip failed.");
                var copy = file.CopyTo(directory, "copy.txt");
                Require(copy.ReadAllText() == file.ReadAllText(), "Local copy failed.");
                var renamed = copy.Rename("renamed.txt");
                Require(renamed.Exists && !copy.Exists, "Rename did not return the authoritative new location.");
                Require(directory.FindFile("renamed.txt") is not null, "Child lookup failed.");
                RequireThrowsPlatform(() => new AndroidDocumentFileSystem().GetEntry("content://test/tree/root"));
                RequireThrowsPlatform(() => AndroidStorageAccess.GetPersistedPermissions());
                RequireThrowsPlatform(() => AndroidStorageAccess.PickDirectoryAsync().GetAwaiter().GetResult());
                Debug.Log("FILE_SYSTEM_EDITOR_SMOKE_PASSED");
            }
            finally
            {
                directory.Delete(true);
            }
        }

        /// <summary>Checks picker cancellation, unrelated results, and late-result cleanup without invoking JNI.</summary>
        /// <exception cref="InvalidOperationException">The picker completion or reservation is incorrect.</exception>
        private static void CheckPickerCompletion()
        {
            var pickerType = typeof(AndroidStorageAccess);
            var requestType = pickerType.GetNestedType("PickerRequest", BindingFlags.NonPublic)!;
            var pendingField = pickerType.GetField("s_pending", BindingFlags.NonPublic | BindingFlags.Static)!;
            var onResult = pickerType.GetMethod("OnActivityResult", BindingFlags.NonPublic | BindingFlags.Static)!;
            var completionProperty = requestType.GetProperty("Completion")!;
            var register = requestType.GetMethod("RegisterCancellation")!;
            using var cancellation = new CancellationTokenSource();
            var request = Activator.CreateInstance(requestType, 0x4D10, true, true, true, cancellation.Token)!;
            var completion = (TaskCompletionSource<string?>)completionProperty.GetValue(request)!;
            pendingField.SetValue(null, request);
            try
            {
                register.Invoke(request, null);
                cancellation.Cancel();
                Require(completion.Task.IsCanceled, "Owner cancellation did not complete the picker task.");
                Require(ReferenceEquals(pendingField.GetValue(null), request), "Cancellation freed a still-open picker.");
                onResult.Invoke(null, new object?[] { null, 0x4D11, 0, null });
                Require(ReferenceEquals(pendingField.GetValue(null), request), "An unrelated result consumed the request.");
                onResult.Invoke(null, new object?[] { null, 0x4D10, -1, null });
                Require(pendingField.GetValue(null) is null, "The cancelled picker's late result did not release its reservation.");
                Require(completion.Task.IsCanceled, "The late result replaced cancellation.");
            }
            finally
            {
                pendingField.SetValue(null, null);
                ((IDisposable)request).Dispose();
            }
            var dismissed = Activator.CreateInstance(requestType, 0x4D12, false, false, false, CancellationToken.None)!;
            var dismissal = (TaskCompletionSource<string?>)completionProperty.GetValue(dismissed)!;
            pendingField.SetValue(null, dismissed);
            try
            {
                register.Invoke(dismissed, null);
                onResult.Invoke(null, new object?[] { null, 0x4D12, 0, null });
                Require(dismissal.Task.IsCompletedSuccessfully && dismissal.Task.Result is null,
                    "User dismissal did not return null.");
                Require(pendingField.GetValue(null) is null, "User dismissal retained the reservation.");
            }
            finally
            {
                pendingField.SetValue(null, null);
                ((IDisposable)dismissed).Dispose();
            }
            Debug.Log("FILE_SYSTEM_PICKER_COMPLETION_PASSED: cancellation, unrelated/late results, dismissal (no JNI).");
        }

        /// <summary>Exercises actual commit/launch gates with deterministic no-JNI thread interleavings.</summary>
        /// <exception cref="InvalidOperationException">A cancellation, shutdown, or ownership assertion fails.</exception>
        /// <exception cref="TargetInvocationException">An invoked picker helper unexpectedly fails.</exception>
        private static void CheckPickerConcurrency()
        {
            var pickerType = typeof(AndroidStorageAccess);
            var requestType = pickerType.GetNestedType("PickerRequest", BindingFlags.NonPublic)!;
            var pendingField = pickerType.GetField("s_pending", BindingFlags.NonPublic | BindingFlags.Static)!;
            var completionProperty = requestType.GetProperty("Completion")!;
            var register = requestType.GetMethod("RegisterCancellation")!;
            var commit = pickerType.GetMethod("CommitResult", BindingFlags.NonPublic | BindingFlags.Static)!;
            var launch = pickerType.GetMethod("TryLaunch", BindingFlags.NonPublic | BindingFlags.Static)!;
            var shutdown = pickerType.GetMethod("Shutdown", BindingFlags.NonPublic | BindingFlags.Static)!;
            const string ResultLocation = "content://test/document/committed";

            using (var cancelled = new CancellationTokenSource())
            {
                var request = Activator.CreateInstance(requestType, 0x4D20, true, true, true, cancelled.Token)!;
                var completion = (TaskCompletionSource<string?>)completionProperty.GetValue(request)!;
                pendingField.SetValue(null, request);
                register.Invoke(request, null);
                cancelled.Cancel();
                var sideEffects = 0;
                var launched = (bool)launch.Invoke(null, new object[] { request, new Action(() =>
                {
                    sideEffects++;
                }) })!;
                commit.Invoke(null, new object[] { request, new Func<string?>(() =>
                {
                    sideEffects++;
                    return ResultLocation;
                }) });
                Require(!launched && sideEffects == 0 && completion.Task.IsCanceled,
                    "Prior cancellation allowed native launch or permission persistence.");
                Require(pendingField.GetValue(null) is null, "Cancelled commitment retained the reservation.");
            }

            using (var reentrant = new CancellationTokenSource())
            {
                var request = Activator.CreateInstance(requestType, 0x4D21, true, true, true, reentrant.Token)!;
                var completion = (TaskCompletionSource<string?>)completionProperty.GetValue(request)!;
                pendingField.SetValue(null, request);
                register.Invoke(request, null);
                commit.Invoke(null, new object[] { request, new Func<string?>(() =>
                {
                    reentrant.Cancel();
                    return ResultLocation;
                }) });
                Require(reentrant.IsCancellationRequested && completion.Task.IsCompletedSuccessfully &&
                    completion.Task.Result == ResultLocation,
                    "Reentrant cancellation hid a result after commitment began.");
                Require(pendingField.GetValue(null) is null, "Committed result retained the reservation.");
            }

            using (var concurrent = new CancellationTokenSource())
            using (var commitStarted = new ManualResetEventSlim())
            using (var releaseCommit = new ManualResetEventSlim())
            {
                var request = Activator.CreateInstance(requestType, 0x4D22, true, true, true, concurrent.Token)!;
                var completion = (TaskCompletionSource<string?>)completionProperty.GetValue(request)!;
                pendingField.SetValue(null, request);
                register.Invoke(request, null);
                var commitWork = Task.Run(() => commit.Invoke(null, new object[] { request, new Func<string?>(() =>
                {
                    commitStarted.Set();
                    Require(releaseCommit.Wait(5000), "The test did not release result commitment.");
                    return ResultLocation;
                }) }));
                Task? cancelWork = null;
                try
                {
                    Require(commitStarted.Wait(5000), "Result commitment did not start.");
                    cancelWork = Task.Run(() => concurrent.Cancel());
                    Require(SpinWait.SpinUntil(() => concurrent.IsCancellationRequested, 5000), "Cancellation did not signal.");
                    Require(!completion.Task.IsCompleted, "Cancellation replaced an in-flight committed result.");
                    Require(!cancelWork.IsCompleted, "Cancellation bypassed the result commitment gate.");
                }
                finally
                {
                    releaseCommit.Set();
                    var work = cancelWork is null ? new Task[] { commitWork } : new Task[] { commitWork, cancelWork };
                    Require(Task.WaitAll(work, 5000), "Result commitment/cancellation deadlocked during registration disposal.");
                }
                Require(completion.Task.IsCompletedSuccessfully && completion.Task.Result == ResultLocation,
                    "Concurrent cancellation hid the committed URI.");
                Require(pendingField.GetValue(null) is null, "Concurrent commitment retained the reservation.");
            }

            using (var launchStarted = new ManualResetEventSlim())
            using (var releaseLaunch = new ManualResetEventSlim())
            using (var shutdownStarted = new ManualResetEventSlim())
            {
                var request = Activator.CreateInstance(requestType, 0x4D23, true, true, true, CancellationToken.None)!;
                var completion = (TaskCompletionSource<string?>)completionProperty.GetValue(request)!;
                pendingField.SetValue(null, request);
                register.Invoke(request, null);
                var launches = 0;
                var launchWork = Task.Run(() => launch.Invoke(null, new object[] { request, new Action(() =>
                {
                    launches++;
                    launchStarted.Set();
                    Require(releaseLaunch.Wait(5000), "The test did not release the native launch boundary.");
                }) }));
                Task? shutdownWork = null;
                try
                {
                    Require(launchStarted.Wait(5000), "The authorized launch did not start.");
                    shutdownWork = Task.Run(() =>
                    {
                        shutdownStarted.Set();
                        shutdown.Invoke(null, null);
                    });
                    Require(shutdownStarted.Wait(5000), "Shutdown did not start.");
                    Require(!completion.Task.IsCompleted && ReferenceEquals(pendingField.GetValue(null), request),
                        "Shutdown invalidated a request inside its native launch boundary.");
                }
                finally
                {
                    releaseLaunch.Set();
                    var work = shutdownWork is null ? new Task[] { launchWork } : new Task[] { launchWork, shutdownWork };
                    Require(Task.WaitAll(work, 5000), "Launch/shutdown arbitration deadlocked.");
                }
                Require((bool)launchWork.Result! && launches == 1 && completion.Task.IsCanceled,
                    "Launch-before-shutdown did not preserve ordering or cancel outstanding completion.");
                Require(pendingField.GetValue(null) is null, "Shutdown retained the request.");
                var launchedAfterShutdown = (bool)launch.Invoke(null, new object[] { request, new Action(() =>
                {
                    launches++;
                }) })!;
                var resultReads = 0;
                commit.Invoke(null, new object[] { request, new Func<string?>(() =>
                {
                    resultReads++;
                    return ResultLocation;
                }) });
                Require(!launchedAfterShutdown && launches == 1 && resultReads == 0,
                    "A stale request launched or persisted a result after shutdown.");
            }
            Debug.Log("FILE_SYSTEM_PICKER_CONCURRENCY_PASSED: commit vs cancellation, reentrancy, launch vs shutdown (no JNI).");
        }

        /// <summary>Finds a compiled assembly by filename without relying on output path formatting.</summary>
        /// <param name="assemblies">The paths returned by Unity's player compiler.</param>
        /// <param name="name">The expected assembly filename.</param>
        /// <returns>Whether Unity reported the assembly.</returns>
        private static bool ContainsAssembly(IEnumerable<string> assemblies, string name)
        {
            foreach (var assembly in assemblies)
            {
                if (string.Equals(Path.GetFileName(assembly), name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Reads the optional comma-delimited player targets passed by the staging script.</summary>
        /// <returns>The requested target names, or Windows64 and Android by default.</returns>
        private static string[] GetRequestedTargets()
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var i = 0; i < arguments.Length - 1; i++)
            {
                if (arguments[i] == "-storageValidationTargets")
                {
                    return arguments[i + 1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                }
            }
            return new[] { "StandaloneWindows64", "Android" };
        }

        /// <summary>Requires an Android API to throw before executing JNI in the Editor.</summary>
        /// <param name="operation">The Android-only operation.</param>
        /// <exception cref="InvalidOperationException">The operation does not throw the platform guard.</exception>
        private static void RequireThrowsPlatform(Action operation)
        {
            try
            {
                operation();
            }
            catch (PlatformNotSupportedException)
            {
                return;
            }
            throw new InvalidOperationException("Android JNI was not guarded in the Editor.");
        }

        /// <summary>Fails an unexpected validation condition.</summary>
        /// <param name="condition">Whether the expectation holds.</param>
        /// <param name="message">The failure explanation.</param>
        /// <exception cref="InvalidOperationException">The condition is false.</exception>
        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
