using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

#nullable enable

namespace MajdataPlay.Platform.Android.Editor
{
    /// <summary>Refreshes standalone Java bindings after Java asset changes or a manual request.</summary>
    /// <remarks>Java analysis runs in the independent CLI. Unity asset APIs are used only on the Editor thread.</remarks>
    [InitializeOnLoad]
    internal sealed class JavaWrapperRefresh : AssetPostprocessor
    {
        /// <summary>Identifies a refresh that must survive a script domain reload.</summary>
        private const string PendingRefreshKey = "MajdataPlay.AndroidJavaWrappers.Pending";

        /// <summary>Identifies the documentation mode of a saved refresh request.</summary>
        private const string OfflineDocumentationKey = "MajdataPlay.AndroidJavaWrappers.OfflineDocumentation";

        /// <summary>Identifies the project Java asset directory watched by the importer.</summary>
        private const string JavaAssetsPrefix = "Assets/Plugins/Android/";

        /// <summary>Contains the single running build or generator process.</summary>
        private static ProcessSession? s_process;

        /// <summary>Contains the captured inputs of the current generation.</summary>
        private static JavaWrapperGenerationRequest? s_request;

        /// <summary>Indicates that a coalesced refresh is waiting for the Editor to become idle.</summary>
        private static bool s_refreshPending;

        /// <summary>Uses cached documentation unless any pending request asks for online enrichment.</summary>
        private static bool s_offlineDocumentation = true;

        /// <summary>Indicates that the current process is compiling the standalone tool.</summary>
        private static bool s_building;

        /// <summary>Defers DLL importing until the directly owned project pipeline exits.</summary>
        private static bool s_autoRefreshDisallowed;

        /// <summary>Indicates that the user has canceled the running generation.</summary>
        private static bool s_cancelRequested;

        /// <summary>Identifies the Editor progress item, or minus one when no item is active.</summary>
        private static int s_progressId = -1;

        /// <summary>Restores queued work and registers Editor lifecycle callbacks.</summary>
        static JavaWrapperRefresh()
        {
            s_refreshPending = SessionState.GetBool(PendingRefreshKey, false);
            s_offlineDocumentation = SessionState.GetBool(OfflineDocumentationKey, true);
            SessionState.EraseBool(PendingRefreshKey);
            SessionState.EraseBool(OfflineDocumentationKey);
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
            EditorApplication.quitting += BeforeEditorQuit;
        }

        /// <summary>Regenerates the complete SDK, Java BCL, project Java, and Unity Java bindings with official documentation.</summary>
        [MenuItem("Tools/MajdataPlay/Android/Regenerate Java wrappers")]
        private static void Regenerate()
        {
            QueueRefresh(false);
        }

        /// <summary>Regenerates bindings using cached official documentation without HTTP requests.</summary>
        [MenuItem("Tools/MajdataPlay/Android/Regenerate Java wrappers (cached documentation)")]
        private static void RegenerateOffline()
        {
            QueueRefresh(true);
        }

        /// <summary>Cancels the current generation and clears any queued follow-up refresh.</summary>
        [MenuItem("Tools/MajdataPlay/Android/Cancel Java wrapper generation")]
        private static void CancelGeneration()
        {
            s_refreshPending = false;
            s_offlineDocumentation = true;
            s_cancelRequested = true;
            s_process?.Kill();
        }

        /// <summary>Enables cancellation only while a refresh is running or queued.</summary>
        /// <returns>True when there is generation work to cancel.</returns>
        [MenuItem("Tools/MajdataPlay/Android/Cancel Java wrapper generation", true)]
        private static bool CanCancelGeneration()
        {
            return s_process != null || s_refreshPending;
        }

        /// <summary>Coalesces changes to project Java sources and archives into one background refresh.</summary>
        /// <param name="importedAssets">The imported asset paths.</param>
        /// <param name="deletedAssets">The removed asset paths.</param>
        /// <param name="movedAssets">The destination paths of moved assets.</param>
        /// <param name="movedFromAssetPaths">The source paths of moved assets.</param>
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (ContainsJavaInput(importedAssets) || ContainsJavaInput(deletedAssets)
                || ContainsJavaInput(movedAssets) || ContainsJavaInput(movedFromAssetPaths))
            {
                QueueRefresh(false);
            }
        }

        /// <summary>Records a refresh without starting a process from inside an import callback.</summary>
        /// <param name="offlineDocumentation">Whether only cached official documentation should be used.</param>
        private static void QueueRefresh(bool offlineDocumentation)
        {
            s_refreshPending = true;
            s_offlineDocumentation &= offlineDocumentation;
        }

        /// <summary>Starts queued work and consumes process completion on the Editor thread.</summary>
        private static void Update()
        {
            if (s_process == null)
            {
                if (s_refreshPending && !EditorApplication.isCompiling && !EditorApplication.isUpdating
                    && !EditorApplication.isPlayingOrWillChangePlaymode && !BuildPipeline.isBuildingPlayer)
                {
                    StartGeneration();
                }
                return;
            }

            try
            {
                s_process.DrainMessages(s_progressId);
                if (!s_process.IsFinished || !s_process.IsDrained)
                {
                    return;
                }

                var exitCode = s_process.ExitCode;
                var logPath = s_process.LogPath;
                s_process.Dispose();
                s_process = null;
                if (s_cancelRequested)
                {
                    FinishGeneration(Progress.Status.Canceled);
                    Debug.Log("[Android Java wrappers] Generation canceled.");
                }
                else if (exitCode != 0)
                {
                    FailGeneration("The " + (s_building ? "tool build" : "wrapper library pipeline") + " exited with code " + exitCode + ". Complete diagnostics: " + logPath);
                }
                else if (s_building)
                {
                    s_building = false;
                    AssetDatabase.DisallowAutoRefresh();
                    s_autoRefreshDisallowed = true;
                    StartProcess(s_request!.Arguments, "Generating bindings and compiling the Android runtime library...");
                }
                else
                {
                    CompleteGeneration();
                }
            }
            catch (Exception exception)
            {
                FailGeneration(exception.Message);
            }
        }

        /// <summary>Captures the current Editor SDK and prepares the standalone tool build.</summary>
        private static void StartGeneration()
        {
            var offlineDocumentation = s_offlineDocumentation;
            s_refreshPending = false;
            s_offlineDocumentation = true;
            s_cancelRequested = false;
            try
            {
                var projectRoot = Path.GetDirectoryName(Application.dataPath)!;
                var unityContentsPath = EditorApplication.applicationContentsPath;
                s_request = JavaWrapperGenerationRequest.Create(projectRoot, unityContentsPath, FindSdkDirectory(unityContentsPath), offlineDocumentation);
                foreach (var warning in s_request.Warnings)
                {
                    Debug.LogWarning("[Android Java wrappers] " + warning);
                }
                s_progressId = Progress.Start("Android Java wrappers", "Building AndroidJavaWrapperGenerator...", Progress.Options.Indefinite);
                Progress.RegisterCancelCallback(s_progressId, () =>
                {
                    CancelGeneration();
                    return true;
                });
                s_building = true;
                Debug.Log("[Android Java wrappers] Refreshing with AndroidJavaWrapperGenerator; " + s_request.Inputs.Count + " additional inputs, "
                    + (offlineDocumentation ? "cached" : "online") + " official documentation. Sources: " + s_request.OutputDirectory
                    + ". Runtime library: " + s_request.RuntimeAssemblyPath);
                StartProcess(s_request.BuildArguments, "Building the independent .NET 9 tool...");
            }
            catch (Exception exception)
            {
                FailGeneration(exception.Message + " Ensure the .NET 9 SDK and Unity Android Build Support are installed.");
            }
        }

        /// <summary>Starts one directly owned dotnet process without a shell or a visible console window.</summary>
        /// <param name="arguments">The quoted dotnet arguments.</param>
        /// <param name="description">The progress description.</param>
        /// <exception cref="Win32Exception">The dotnet executable cannot be launched.</exception>
        /// <exception cref="InvalidOperationException">The process cannot start.</exception>
        private static void StartProcess(string arguments, string description)
        {
            var info = new ProcessStartInfo("dotnet", arguments)
            {
                WorkingDirectory = s_request!.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var logsDirectory = Path.Combine(s_request.WorkingDirectory, ".work", "editor-logs");
            Directory.CreateDirectory(JavaWrapperOutput.GetFileSystemPath(logsDirectory));
            s_process = new ProcessSession(info, Path.Combine(logsDirectory, Guid.NewGuid().ToString("N") + ".log"));
            Progress.Report(s_progressId, 0f, description);
        }

        /// <summary>Imports the successfully compiled library after the independent pipeline has completed.</summary>
        /// <exception cref="FileNotFoundException">The successful pipeline did not publish its runtime DLL.</exception>
        private static void CompleteGeneration()
        {
            var request = s_request!;
            if (!File.Exists(request.RuntimeAssemblyPath))
            {
                throw new FileNotFoundException("The wrapper pipeline completed without publishing its runtime library: "
                    + request.RuntimeAssemblyPath, request.RuntimeAssemblyPath);
            }

            FinishGeneration(Progress.Status.Succeeded);
            Debug.Log("[Android Java wrappers] Compiled " + request.RuntimeAssemblyPath + ". Extend generated bindings in "
                + request.OutputDirectory + " with partial XXX.e.cs classes and regenerate to rebuild the library.");
            AssetDatabase.Refresh();
        }

        /// <summary>Reports a failed refresh and releases its process and progress resources.</summary>
        /// <param name="message">The actionable build, generation, or publication failure.</param>
        private static void FailGeneration(string message)
        {
            FinishGeneration(Progress.Status.Failed);
            Debug.LogError("[Android Java wrappers] " + message);
        }

        /// <summary>Releases one generation without discarding a refresh queued during its execution.</summary>
        /// <param name="status">The terminal progress state.</param>
        private static void FinishGeneration(Progress.Status status)
        {
            if (s_process != null)
            {
                s_process.Kill();
                s_process.Dispose();
                s_process = null;
            }
            if (s_autoRefreshDisallowed)
            {
                AssetDatabase.AllowAutoRefresh();
                s_autoRefreshDisallowed = false;
            }
            if (s_progressId >= 0)
            {
                Progress.Finish(s_progressId, status);
                s_progressId = -1;
            }
            s_request = null;
            s_cancelRequested = false;
        }

        /// <summary>Saves interrupted or queued work before a script domain reload.</summary>
        private static void BeforeAssemblyReload()
        {
            if (s_request != null && !s_cancelRequested)
            {
                s_refreshPending = true;
                s_offlineDocumentation &= s_request.OfflineDocumentation;
            }
            SessionState.SetBool(PendingRefreshKey, s_refreshPending);
            SessionState.SetBool(OfflineDocumentationKey, s_offlineDocumentation);
            FinishGeneration(Progress.Status.Canceled);
        }

        /// <summary>Stops the directly owned process when the Editor closes.</summary>
        private static void BeforeEditorQuit()
        {
            SessionState.EraseBool(PendingRefreshKey);
            SessionState.EraseBool(OfflineDocumentationKey);
            s_refreshPending = false;
            FinishGeneration(Progress.Status.Canceled);
        }

        /// <summary>Reads the current Android External Tools SDK setting, falling back to this Editor's bundled SDK.</summary>
        /// <param name="unityContentsPath">The current Editor contents directory.</param>
        /// <returns>The configured SDK directory or the bundled SDK directory.</returns>
        private static string FindSdkDirectory(string unityContentsPath)
        {
            var settingsType = Type.GetType("UnityEditor.Android.AndroidExternalToolsSettings, UnityEditor.Android.Extensions", false);
            if (settingsType != null)
            {
                var property = settingsType.GetProperty("sdkRootPath", BindingFlags.Public | BindingFlags.Static);
                var configured = property?.GetValue(null) as string;
                if (!string.IsNullOrWhiteSpace(configured))
                {
                    return configured!;
                }
            }
            return Path.Combine(unityContentsPath, "PlaybackEngines", "AndroidPlayer", "SDK");
        }

        /// <summary>Finds relevant project Java inputs without reacting to generated C# imports.</summary>
        /// <param name="paths">The imported, deleted, or moved asset paths.</param>
        /// <returns>True when an input under Assets/Plugins/Android changed.</returns>
        private static bool ContainsJavaInput(string[] paths)
        {
            foreach (var path in paths)
            {
                if (path.StartsWith(JavaAssetsPrefix, StringComparison.OrdinalIgnoreCase)
                    && JavaWrapperGenerationRequest.IsJavaInput(path))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Owns a directly launched process and marshals its asynchronous pipe messages to the Editor thread.</summary>
        private sealed class ProcessSession : IDisposable
        {
            /// <summary>Bounds pipe messages retained for display on the Editor thread.</summary>
            private const int MaxQueuedMessages = 2048;

            /// <summary>Serializes complete diagnostic-log writes and disposal from pipe-reader threads.</summary>
            private readonly object _logLock = new object();

            /// <summary>Records complete process diagnostics independently of the bounded Console queue.</summary>
            private readonly StreamWriter _log;

            /// <summary>Counts messages waiting for a budgeted Editor update.</summary>
            private int _queuedMessages;

            /// <summary>Counts Console messages suppressed during unusually large diagnostic bursts.</summary>
            private int _suppressedMessages;

            /// <summary>Prevents callbacks from writing after diagnostic resources have been released.</summary>
            private bool _disposed;

            /// <summary>Stores a diagnostic-log failure without throwing on a background reader thread.</summary>
            private string? _logFailure;

            /// <summary>Contains the owned CLI or build process.</summary>
            private readonly Process _process;

            /// <summary>Contains output received on background pipe-reader threads.</summary>
            private readonly ConcurrentQueue<ProcessMessage> _messages = new ConcurrentQueue<ProcessMessage>();

            /// <summary>Counts closed redirected streams before completion is observed.</summary>
            private int _closedStreams;

            /// <summary>Launches a process and begins draining both pipes without blocking the Editor.</summary>
            /// <param name="info">The executable, arguments, and working directory.</param>
            /// <param name="logPath">The tool-local file receiving every process diagnostic.</param>
            /// <exception cref="Win32Exception">The executable cannot be launched.</exception>
            /// <exception cref="InvalidOperationException">The process cannot start.</exception>
            internal ProcessSession(ProcessStartInfo info, string logPath)
            {
                LogPath = logPath;
                _log = new StreamWriter(JavaWrapperOutput.GetFileSystemPath(logPath), false);
                _process = new Process { StartInfo = info };
                _process.OutputDataReceived += (sender, args) => Receive(args.Data, false);
                _process.ErrorDataReceived += (sender, args) => Receive(args.Data, true);
                try
                {
                    if (!_process.Start())
                    {
                        throw new InvalidOperationException("Could not start dotnet.");
                    }
                    _process.BeginOutputReadLine();
                    _process.BeginErrorReadLine();
                }
                catch
                {
                    Kill();
                    Dispose();
                    throw;
                }
            }

            /// <summary>Gets whether the process exited and both redirected streams have reached their ends.</summary>
            internal bool IsFinished => _process.HasExited && Volatile.Read(ref _closedStreams) == 2;

            /// <summary>Gets the path of the complete diagnostic log for this process.</summary>
            internal string LogPath { get; }

            /// <summary>Gets whether all queued diagnostics have been consumed within the Editor update budget.</summary>
            internal bool IsDrained => _messages.IsEmpty;

            /// <summary>Gets the exit code of the completed process.</summary>
            internal int ExitCode => _process.ExitCode;

            /// <summary>Queues one pipe message or records the end of its stream.</summary>
            /// <param name="text">The received line, or null when the stream closed.</param>
            /// <param name="error">Whether the line came from standard error.</param>
            private void Receive(string? text, bool error)
            {
                if (text == null)
                {
                    Interlocked.Increment(ref _closedStreams);
                }
                else if (text.Length > 0)
                {
                    lock (_logLock)
                    {
                        if (_disposed)
                        {
                            return;
                        }
                        if (_logFailure == null)
                        {
                            try
                            {
                                _log.WriteLine((error ? "stderr: " : "stdout: ") + text);
                            }
                            catch (IOException exception)
                            {
                                _logFailure = exception.Message;
                            }
                        }
                    }
                    if (Interlocked.Increment(ref _queuedMessages) <= MaxQueuedMessages)
                    {
                        _messages.Enqueue(new ProcessMessage(text, error));
                    }
                    else
                    {
                        Interlocked.Decrement(ref _queuedMessages);
                        Interlocked.Increment(ref _suppressedMessages);
                    }
                }
            }

            /// <summary>Logs pending output and updates progress from the Editor thread.</summary>
            /// <param name="progressId">The current Editor progress item.</param>
            /// <param name="limit">The maximum messages to consume during this Editor update.</param>
            internal void DrainMessages(int progressId, int limit = 64)
            {
                while (limit-- > 0 && _messages.TryDequeue(out var message))
                {
                    Interlocked.Decrement(ref _queuedMessages);
                    if (message.Error)
                    {
                        Debug.LogWarning("[Android Java wrappers] " + message.Text);
                    }
                    else
                    {
                        Debug.Log("[Android Java wrappers] " + message.Text);
                        if (progressId >= 0)
                        {
                            Progress.Report(progressId, 0f, message.Text);
                        }
                    }
                }
            }

            /// <summary>Stops the directly owned process if it is still running.</summary>
            internal void Kill()
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        var killTree = typeof(Process).GetMethod("Kill", new[] { typeof(bool) });
                        var treeTerminationRequested = false;
                        if (killTree != null)
                        {
                            try
                            {
                                killTree.Invoke(_process, new object[] { true });
                                treeTerminationRequested = true;
                            }
                            catch (TargetInvocationException)
                            {
                                // Some Unity Mono versions expose this overload without implementing it.
                            }
                        }
                        if (!treeTerminationRequested)
                        {
                            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                            {
                                KillWindowsProcessTree();
                            }
                            if (!_process.HasExited)
                            {
                                _process.Kill();
                            }
                        }
                        _process.WaitForExit(1000);
                    }
                }
                catch (InvalidOperationException)
                {
                    // A process may exit between the state check and the kill request.
                }
                catch (Win32Exception)
                {
                    // The operating system may already have removed the exiting process.
                }
            }

            /// <summary>Stops the CLI and its child compiler on Windows when Unity Mono cannot terminate process trees.</summary>
            private void KillWindowsProcessTree()
            {
                try
                {
                    var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "taskkill.exe"),
                        "/PID " + _process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + " /T /F")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var termination = new Process { StartInfo = info };
                    if (!termination.Start())
                    {
                        return;
                    }
                    termination.BeginOutputReadLine();
                    termination.BeginErrorReadLine();
                    if (!termination.WaitForExit(1000))
                    {
                        termination.Kill();
                    }
                }
                catch (InvalidOperationException)
                {
                    // The CLI or bounded taskkill helper may already have exited.
                }
                catch (Win32Exception)
                {
                    // Fall back to direct termination if the operating system cannot launch taskkill.
                }
            }

            /// <summary>Releases redirected stream and process handles.</summary>
            public void Dispose()
            {
                _process.Dispose();
                lock (_logLock)
                {
                    _disposed = true;
                    try
                    {
                        _log.Dispose();
                    }
                    catch (IOException exception)
                    {
                        _logFailure = exception.Message;
                    }
                }
                if (_logFailure != null)
                {
                    Debug.LogWarning("[Android Java wrappers] Process diagnostic log could not be completed: " + _logFailure);
                }
                var suppressed = Interlocked.Exchange(ref _suppressedMessages, 0);
                if (suppressed > 0)
                {
                    Debug.LogWarning("[Android Java wrappers] " + suppressed + " messages omitted from the Console; complete diagnostics: " + LogPath);
                }
            }
        }

        /// <summary>Stores an immutable line received from an external process.</summary>
        private sealed class ProcessMessage
        {
            /// <summary>Initializes one output message.</summary>
            /// <param name="text">The received line.</param>
            /// <param name="error">Whether the line came from standard error.</param>
            internal ProcessMessage(string text, bool error)
            {
                Text = text;
                Error = error;
            }

            /// <summary>Gets the received output line.</summary>
            internal string Text { get; }

            /// <summary>Gets whether the line came from standard error.</summary>
            internal bool Error { get; }
        }
    }
}
