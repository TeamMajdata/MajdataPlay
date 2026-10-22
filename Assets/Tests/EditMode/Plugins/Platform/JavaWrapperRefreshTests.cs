using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using MajdataPlay.Platform.Android.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Platform
{
    /// <summary>Verifies the Java wrapper refresh controller's queueing, cancellation and process handling without running the generator.</summary>
    public class JavaWrapperRefreshTests
    {
        /// <summary>Names the controller's private static fields saved and restored around each test.</summary>
        private static readonly string[] s_fields =
        {
            "s_process", "s_request", "s_refreshPending", "s_offlineDocumentation", "s_building", "s_autoRefreshDisallowed", "s_cancelRequested", "s_progressId"
        };

        /// <summary>Stores the controller state that existed before the test.</summary>
        private readonly System.Collections.Generic.Dictionary<string, object?> _saved = new System.Collections.Generic.Dictionary<string, object?>();
        /// <summary>Owns temporary process logs.</summary>
        private TemporaryDirectory _directory = null!;

        /// <summary>Saves the controller state.</summary>
        [SetUp]
        public void SetUp()
        {
            _directory = new TemporaryDirectory();
            foreach (var field in s_fields)
            {
                _saved[field] = PrivateMember.GetStaticField<object?>(typeof(JavaWrapperRefresh), field);
            }
        }

        /// <summary>Restores the controller state so no refresh is started by later Editor updates.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (var pair in _saved)
            {
                PrivateMember.SetStaticField(typeof(JavaWrapperRefresh), pair.Key, pair.Value);
            }

            SessionState.EraseBool("MajdataPlay.AndroidJavaWrappers.Pending");
            SessionState.EraseBool("MajdataPlay.AndroidJavaWrappers.OfflineDocumentation");
            _directory.Dispose();
        }

        /// <summary>Reads one private static controller field.</summary>
        /// <typeparam name="T">The field type.</typeparam>
        /// <param name="name">The field name.</param>
        /// <returns>The field value.</returns>
        private static T Field<T>(string name)
        {
            return PrivateMember.GetStaticField<T>(typeof(JavaWrapperRefresh), name);
        }

        /// <summary>Invokes one private static controller method.</summary>
        /// <param name="name">The method name.</param>
        /// <param name="arguments">The method arguments.</param>
        /// <returns>The method result.</returns>
        private static object? Call(string name, params object?[] arguments)
        {
            return PrivateMember.InvokeStatic(typeof(JavaWrapperRefresh), name, arguments);
        }

        /// <summary>Checks that only Java inputs under the project Android plugin folder trigger refreshes.</summary>
        [Test]
        public void ContainsJavaInput_FiltersPaths()
        {
            Assert.That(Call("ContainsJavaInput", (object)new[] { "Assets/Other/A.java", "Assets/Plugins/Android/A.java" }), Is.True);
            Assert.That(Call("ContainsJavaInput", (object)new[] { "assets/plugins/android/lib.AAR" }), Is.True);
            Assert.That(Call("ContainsJavaInput", (object)new[] { "Assets/Plugins/Android/A.cs", "Assets/Other/B.jar" }), Is.False);
            Assert.That(Call("ContainsJavaInput", (object)Array.Empty<string>()), Is.False);
        }

        /// <summary>Checks queueing, documentation-mode coalescing and cancellation of refreshes.</summary>
        [Test]
        public void QueueAndCancel_CoalesceRefreshes()
        {
            PrivateMember.SetStaticField(typeof(JavaWrapperRefresh), "s_refreshPending", false);
            PrivateMember.SetStaticField(typeof(JavaWrapperRefresh), "s_offlineDocumentation", true);
            PrivateMember.SetStaticField(typeof(JavaWrapperRefresh), "s_process", null);
            Assert.That(Call("CanCancelGeneration"), Is.False);

            Call("RegenerateOffline");
            Assert.That(Field<bool>("s_refreshPending"), Is.True);
            Assert.That(Field<bool>("s_offlineDocumentation"), Is.True);
            Assert.That(Call("CanCancelGeneration"), Is.True);
            Call("Regenerate");
            Assert.That(Field<bool>("s_offlineDocumentation"), Is.False);

            Call("CancelGeneration");
            Assert.That(Field<bool>("s_refreshPending"), Is.False);
            Assert.That(Field<bool>("s_offlineDocumentation"), Is.True);
            Assert.That(Field<bool>("s_cancelRequested"), Is.True);

            Call("OnPostprocessAllAssets", new[] { "Assets/Plugins/Android/A.java" }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            Assert.That(Field<bool>("s_refreshPending"), Is.True);
            PrivateMember.SetStaticField(typeof(JavaWrapperRefresh), "s_refreshPending", false);
            Call("OnPostprocessAllAssets", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), new[] { "Assets/Plugins/Android/B.jar" });
            Assert.That(Field<bool>("s_refreshPending"), Is.True);
            PrivateMember.SetStaticField(typeof(JavaWrapperRefresh), "s_refreshPending", false);
            Call("OnPostprocessAllAssets", new[] { "Assets/A.cs" }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            Assert.That(Field<bool>("s_refreshPending"), Is.False);
        }

        /// <summary>Checks that the reload and quit handlers persist or clear queued work and release the generation.</summary>
        [Test]
        public void ReloadAndQuit_PersistOrClearQueuedWork()
        {
            PrivateMember.SetStaticField(typeof(JavaWrapperRefresh), "s_process", null);
            PrivateMember.SetStaticField(typeof(JavaWrapperRefresh), "s_progressId", -1);
            PrivateMember.SetStaticField(typeof(JavaWrapperRefresh), "s_autoRefreshDisallowed", false);
            PrivateMember.SetStaticField(typeof(JavaWrapperRefresh), "s_refreshPending", true);
            PrivateMember.SetStaticField(typeof(JavaWrapperRefresh), "s_offlineDocumentation", false);

            Call("BeforeAssemblyReload");

            Assert.That(SessionState.GetBool("MajdataPlay.AndroidJavaWrappers.Pending", false), Is.True);
            Assert.That(SessionState.GetBool("MajdataPlay.AndroidJavaWrappers.OfflineDocumentation", true), Is.False);
            Assert.That(Field<object?>("s_request"), Is.Null);
            Assert.That(Field<bool>("s_cancelRequested"), Is.False);

            Call("BeforeEditorQuit");

            Assert.That(SessionState.GetBool("MajdataPlay.AndroidJavaWrappers.Pending", false), Is.False);
            Assert.That(Field<bool>("s_refreshPending"), Is.False);
        }

        /// <summary>Checks that an idle update does nothing and a failure is reported once.</summary>
        [Test]
        public void UpdateAndFailure_AreReported()
        {
            PrivateMember.SetStaticField(typeof(JavaWrapperRefresh), "s_process", null);
            PrivateMember.SetStaticField(typeof(JavaWrapperRefresh), "s_refreshPending", false);
            PrivateMember.SetStaticField(typeof(JavaWrapperRefresh), "s_progressId", -1);

            Call("Update");
            Assert.That(Field<object?>("s_process"), Is.Null);

            LogAssert.Expect(LogType.Error, new Regex(@"\[Android Java wrappers\] generator exploded"));
            Call("FailGeneration", "generator exploded");
            Assert.That(Field<object?>("s_request"), Is.Null);
        }

        /// <summary>Checks that the SDK lookup returns a usable directory.</summary>
        [Test]
        public void FindSdkDirectory_ReturnsConfiguredOrBundledPath()
        {
            var sdk = (string)Call("FindSdkDirectory", "contents")!;

            Assert.That(sdk, Is.Not.Empty);
        }

        /// <summary>Checks a directly owned process: output capture, diagnostic logging, draining and disposal.</summary>
        [Test]
        public void ProcessSession_CapturesOutputAndDrains()
        {
            var sessionType = typeof(JavaWrapperRefresh).GetNestedType("ProcessSession", BindingFlags.NonPublic)!;
            var logPath = Path.Combine(_directory.Location, "process.log");
            var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), "/d /c echo hello-out& echo hello-err 1>&2")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var session = Activator.CreateInstance(sessionType, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { info, logPath }, null)!;
            try
            {
                var watch = Stopwatch.StartNew();
                while (!PrivateMember.GetProperty<bool>(session, "IsFinished") && watch.ElapsedMilliseconds < 10000)
                {
                    System.Threading.Thread.Sleep(10);
                }

                Assert.That(PrivateMember.GetProperty<bool>(session, "IsFinished"), Is.True);
                Assert.That(PrivateMember.GetProperty<int>(session, "ExitCode"), Is.EqualTo(0));
                Assert.That(PrivateMember.GetProperty<bool>(session, "IsDrained"), Is.False);
                Assert.That(PrivateMember.GetProperty<string>(session, "LogPath"), Is.EqualTo(logPath));
                // Standard output and error are read on separate threads, so the two lines can be queued in either
                // order; record the drained logs instead of expecting a fixed sequence.
                var logs = new System.Collections.Generic.List<(LogType Type, string Message)>();
                Application.LogCallback record = (message, _, type) => logs.Add((type, message));
                Application.logMessageReceived += record;
                try
                {
                    PrivateMember.Invoke(session, "DrainMessages", -1, 64);
                }
                finally
                {
                    Application.logMessageReceived -= record;
                }

                Assert.That(PrivateMember.GetProperty<bool>(session, "IsDrained"), Is.True);
                Assert.That(logs, Has.Some.Matches<(LogType Type, string Message)>(x => x.Type == LogType.Log && x.Message.Contains("hello-out")));
                Assert.That(logs, Has.Some.Matches<(LogType Type, string Message)>(x => x.Type == LogType.Warning && x.Message.Contains("hello-err")));
                PrivateMember.Invoke(session, "Kill");
            }
            finally
            {
                PrivateMember.Invoke(session, "Dispose");
            }

            var log = MajdataPlay.IO.Storage.FileSystem.File.Open(logPath).ReadAllText();
            Assert.That(log, Does.Contain("stdout: hello-out"));
            Assert.That(log, Does.Contain("stderr: hello-err"));
        }

        /// <summary>Checks that killing a running process terminates it.</summary>
        [Test]
        public void ProcessSession_KillStopsRunningProcess()
        {
            var sessionType = typeof(JavaWrapperRefresh).GetNestedType("ProcessSession", BindingFlags.NonPublic)!;
            var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), "/d /c \"for /l %i in (0,1,2000000000) do @rem\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var session = Activator.CreateInstance(sessionType, BindingFlags.NonPublic | BindingFlags.Instance, null,
                new object[] { info, Path.Combine(_directory.Location, "kill.log") }, null)!;
            try
            {
                Assert.That(PrivateMember.GetProperty<bool>(session, "IsFinished"), Is.False);

                PrivateMember.Invoke(session, "Kill");

                var watch = Stopwatch.StartNew();
                while (!PrivateMember.GetProperty<bool>(session, "IsFinished") && watch.ElapsedMilliseconds < 10000)
                {
                    System.Threading.Thread.Sleep(10);
                }

                Assert.That(PrivateMember.GetProperty<bool>(session, "IsFinished"), Is.True);
            }
            finally
            {
                PrivateMember.Invoke(session, "Dispose");
            }
        }
    }
}
