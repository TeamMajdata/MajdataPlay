using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MajdataPlay.Threading;
using NUnit.Framework;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using TaskTracker = MajdataPlay.Threading.TaskTracker;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Threading
{
    /// <summary>
    /// Covers registration, running-task queries, snapshots and history management of <see cref="TaskTracker"/> for Task,
    /// ValueTask and UniTask sources. The tracker's static state is saved before and restored after every test.
    /// </summary>
    public class TaskTrackerTests
    {
        /// <summary>Restores the tracker state after each test.</summary>
        private TaskTrackerIsolation _isolation = null!;

        /// <summary>Saves the tracker state and enables history and stack capture.</summary>
        [SetUp]
        public void SetUp()
        {
            _isolation = new TaskTrackerIsolation();
            TaskTracker.EnableTracking = true;
            TaskTracker.EnableStackTrace = true;
        }

        /// <summary>Restores the tracker state.</summary>
        [TearDown]
        public void TearDown()
        {
            _isolation.Dispose();
        }

        /// <summary>Checks that the switches can be read back after being changed.</summary>
        [Test]
        public void Switches_RoundTrip()
        {
            TaskTracker.EnableTracking = false;
            TaskTracker.EnableStackTrace = false;

            Assert.That(TaskTracker.EnableTracking, Is.False);
            Assert.That(TaskTracker.EnableStackTrace, Is.False);
        }

        /// <summary>Checks that registering a running task returns it unchanged and records a complete snapshot.</summary>
        [Test]
        public void Register_RunningTask_RecordsSnapshot()
        {
            var name = TaskTrackerIsolation.UniqueName("running");
            var source = new TaskCompletionSource<int>();
            var before = DateTimeOffset.Now;

            var returned = TaskTracker.Register(source.Task, name, "SceneA");

            Assert.That(returned, Is.SameAs(source.Task));
            var snapshot = TaskTrackerIsolation.Single(name);
            Assert.That(snapshot.Id, Is.EqualTo(source.Task.Id));
            Assert.That(snapshot.Status, Is.EqualTo(TaskStatus.WaitingForActivation));
            Assert.That(snapshot.IsCompleted, Is.False);
            Assert.That(snapshot.SceneName, Is.EqualTo("SceneA"));
            Assert.That(snapshot.IsSceneCached, Is.False);
            Assert.That(snapshot.IsWorker, Is.False);
            Assert.That(snapshot.TaskType, Is.EqualTo(source.Task.GetType().ToString()));
            Assert.That(snapshot.RegisteredAt, Is.GreaterThanOrEqualTo(before.AddSeconds(-1)));
            Assert.That(snapshot.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.Zero));
            Assert.That(snapshot.StackTrace, Does.Contain(nameof(Register_RunningTask_RecordsSnapshot)));
            source.SetResult(1);
        }

        /// <summary>Checks that the non-generic overload returns the task and that a missing name defaults to the task type.</summary>
        [Test]
        public void Register_NonGenericTaskWithoutName_UsesTaskType()
        {
            var scene = TaskTrackerIsolation.UniqueName("scene");
            var source = new TaskCompletionSource<bool>();
            Task task = source.Task;

            var returned = TaskTracker.Register(task, null!, scene, true);

            Assert.That(returned, Is.SameAs(task));
            var snapshot = TaskTracker.GetSnapshots().Single(x => x.SceneName == scene);
            Assert.That(snapshot.Name, Is.EqualTo(task.GetType().ToString()));
            Assert.That(snapshot.IsWorker, Is.True);
            source.SetResult(true);
        }

        /// <summary>Checks that null tasks are rejected.</summary>
        [Test]
        public void Register_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => TaskTracker.Register((Task)null!));
            Assert.Throws<ArgumentNullException>(() => TaskTracker.Register((Task<int>)null!));
        }

        /// <summary>Checks that the first registration of a task wins over later ones.</summary>
        [Test]
        public void Register_Twice_FirstRegistrationWins()
        {
            var name = TaskTrackerIsolation.UniqueName("first");
            var source = new TaskCompletionSource<int>();

            TaskTracker.Register(source.Task, name, "S1");
            TaskTracker.Register(source.Task, name + "-second", "S2", true);

            Assert.That(TaskTrackerIsolation.Single(name).SceneName, Is.EqualTo("S1"));
            Assert.That(TaskTrackerIsolation.Find(name + "-second"), Is.Empty);
            source.SetResult(1);
        }

        /// <summary>Checks that a task completed before registration is recorded with zero elapsed time and is not running.</summary>
        [Test]
        public void Register_CompletedTask_RecordsZeroElapsed()
        {
            var name = TaskTrackerIsolation.UniqueName("completed");
            var scene = TaskTrackerIsolation.UniqueName("scene");

            TaskTracker.Register(Task.FromResult(5), name, scene);

            var snapshot = TaskTrackerIsolation.Single(name);
            Assert.That(snapshot.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(snapshot.IsCompleted, Is.True);
            Assert.That(snapshot.Elapsed, Is.EqualTo(TimeSpan.Zero));
            Assert.That(TaskTracker.HasRunningTasks(true, scene), Is.False);
        }

        /// <summary>Checks that with history disabled a completed task is ignored entirely.</summary>
        [Test]
        public void Register_CompletedTaskWithoutTracking_IsIgnored()
        {
            var name = TaskTrackerIsolation.UniqueName("ignored");
            TaskTracker.EnableTracking = false;

            TaskTracker.Register(Task.FromResult(1), name, "S");

            Assert.That(TaskTrackerIsolation.Find(name), Is.Empty);
        }

        /// <summary>Checks that with history disabled a running task is counted but leaves no snapshot.</summary>
        [Test]
        public void Register_RunningTaskWithoutTracking_IsCountedOnly()
        {
            var name = TaskTrackerIsolation.UniqueName("untracked");
            var scene = TaskTrackerIsolation.UniqueName("scene");
            var source = new TaskCompletionSource<int>();
            TaskTracker.EnableTracking = false;

            TaskTracker.Register(source.Task, name, scene);

            Assert.That(TaskTrackerIsolation.Find(name), Is.Empty);
            Assert.That(TaskTracker.GetRunningTaskCount(true, scene), Is.EqualTo(1));

            source.SetResult(1);

            Assert.That(TaskTracker.GetRunningTaskCount(true, scene), Is.EqualTo(0));
        }

        /// <summary>Checks that disabling stack capture records a placeholder instead of a stack.</summary>
        [Test]
        public void Register_WithoutStackTrace_RecordsPlaceholder()
        {
            var name = TaskTrackerIsolation.UniqueName("nostack");
            var source = new TaskCompletionSource<int>();
            TaskTracker.EnableStackTrace = false;

            TaskTracker.Register(source.Task, name, "S");

            Assert.That(TaskTrackerIsolation.Single(name).StackTrace, Is.EqualTo("Stack trace capture was disabled."));
            source.SetResult(1);
        }

        /// <summary>Checks that the main thread records the current active scene when no scene is given.</summary>
        [Test]
        public void Register_MainThreadWithoutScene_RecordsActiveScene()
        {
            var name = TaskTrackerIsolation.UniqueName("main");
            var source = new TaskCompletionSource<int>();

            TaskTracker.Register(source.Task, name);

            var snapshot = TaskTrackerIsolation.Single(name);
            Assert.That(snapshot.SceneName, Is.EqualTo(TaskTrackerIsolation.DescribeActiveScene()));
            Assert.That(snapshot.IsSceneCached, Is.False);
            source.SetResult(1);
        }

        /// <summary>Checks that a worker thread records the cached active scene and marks it as cached.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Register_WorkerThreadWithoutScene_RecordsCachedScene() => UniTask.ToCoroutine(async () =>
        {
            var name = TaskTrackerIsolation.UniqueName("worker");
            var source = new TaskCompletionSource<int>();
            PrivateMember.SetStaticField(typeof(TaskTracker), "_activeScene", "Cached/Scene.unity");

            await Task.Run(() =>
            {
                TaskTracker.Register(source.Task, name);
            });

            var snapshot = TaskTrackerIsolation.Single(name);
            Assert.That(snapshot.SceneName, Is.EqualTo("Cached/Scene.unity"));
            Assert.That(snapshot.IsSceneCached, Is.True);
            source.SetResult(1);
        });

        /// <summary>Checks the running-task queries with worker and scene filters as tasks complete.</summary>
        [Test]
        public void RunningQueries_FilterByWorkerAndScene()
        {
            var scene = TaskTrackerIsolation.UniqueName("scene");
            var other = TaskTrackerIsolation.UniqueName("other");
            var foreground = new TaskCompletionSource<int>();
            var worker = new TaskCompletionSource<int>();
            var elsewhere = new TaskCompletionSource<int>();
            TaskTracker.Register(foreground.Task, "foreground", scene);
            TaskTracker.Register(worker.Task, "worker", scene, true);
            TaskTracker.Register(elsewhere.Task, "elsewhere", other);

            Assert.That(TaskTracker.GetRunningTaskCount(true, scene), Is.EqualTo(2));
            Assert.That(TaskTracker.GetRunningTaskCount(false, scene), Is.EqualTo(1));
            Assert.That(TaskTracker.HasRunningTasks(false, scene), Is.True);

            foreground.SetResult(1);

            Assert.That(TaskTracker.HasRunningTasks(false, scene), Is.False);
            Assert.That(TaskTracker.HasRunningTasks(true, scene), Is.True);
            Assert.That(TaskTracker.GetRunningTaskCount(true, other), Is.EqualTo(1));

            worker.SetResult(1);
            elsewhere.SetResult(1);

            Assert.That(TaskTracker.HasRunningTasks(true, scene), Is.False);
            Assert.That(TaskTracker.GetRunningTaskCount(true, scene), Is.EqualTo(0));
        }

        /// <summary>Checks the unfiltered non-worker queries as a running task is added and completed.</summary>
        [Test]
        public void NonWorkerQueries_CountAllScenes()
        {
            var before = TaskTracker.RunningNonWorkerTaskCount;
            var allBefore = TaskTracker.GetRunningTaskCount();
            var source = new TaskCompletionSource<int>();

            TaskTracker.Register(source.Task, "nonworker", TaskTrackerIsolation.UniqueName("scene"));

            Assert.That(TaskTracker.RunningNonWorkerTaskCount, Is.EqualTo(before + 1));
            Assert.That(TaskTracker.GetRunningTaskCount(), Is.EqualTo(allBefore + 1));
            Assert.That(TaskTracker.HasRunningNonWorkerTasks, Is.True);
            Assert.That(TaskTracker.HasRunningTasks(), Is.True);

            source.SetResult(1);

            Assert.That(TaskTracker.RunningNonWorkerTaskCount, Is.EqualTo(before));
        }

        /// <summary>Checks that canceled and faulted tasks stop counting as running and report their status.</summary>
        [Test]
        public void CanceledAndFaultedTasks_DoNotBlock()
        {
            var scene = TaskTrackerIsolation.UniqueName("scene");
            var canceled = new TaskCompletionSource<int>();
            var faulted = new TaskCompletionSource<int>();
            var canceledName = TaskTrackerIsolation.UniqueName("canceled");
            var faultedName = TaskTrackerIsolation.UniqueName("faulted");
            TaskTracker.Register(canceled.Task, canceledName, scene);
            TaskTracker.Register(faulted.Task, faultedName, scene);

            canceled.SetCanceled();
            faulted.SetException(new InvalidOperationException("faulted"));
            _ = faulted.Task.Exception;

            Assert.That(TaskTracker.HasRunningTasks(true, scene), Is.False);
            Assert.That(TaskTrackerIsolation.Single(canceledName).Status, Is.EqualTo(TaskStatus.Canceled));
            Assert.That(TaskTrackerIsolation.Single(canceledName).IsCompleted, Is.True);
            Assert.That(TaskTrackerIsolation.Single(faultedName).Status, Is.EqualTo(TaskStatus.Faulted));
            Assert.That(TaskTrackerIsolation.Single(faultedName).IsCompleted, Is.True);
        }

        /// <summary>Checks that the elapsed time stops growing once the task completes.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Snapshot_ElapsedFreezesOnCompletion() => UniTask.ToCoroutine(async () =>
        {
            var name = TaskTrackerIsolation.UniqueName("elapsed");
            var source = new TaskCompletionSource<int>();
            TaskTracker.Register(source.Task, name, "S");

            await UniTask.Delay(20, DelayType.Realtime);
            var running = TaskTrackerIsolation.Single(name).Elapsed;
            source.SetResult(1);
            await UniTask.Yield();
            var completed = TaskTrackerIsolation.Single(name).Elapsed;
            await UniTask.Delay(20, DelayType.Realtime);
            var later = TaskTrackerIsolation.Single(name).Elapsed;

            Assert.That(running, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(completed, Is.GreaterThanOrEqualTo(running));
            Assert.That(later, Is.EqualTo(completed));
        });

        /// <summary>Checks that the ValueTask overload records the backing task and keeps the outcome.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Register_ValueTask_TracksBackingTask() => UniTask.ToCoroutine(async () =>
        {
            var name = TaskTrackerIsolation.UniqueName("valuetask");
            var source = new TaskCompletionSource<bool>();

            var returned = TaskTracker.Register(new ValueTask(source.Task), name, "S");

            Assert.That(TaskTrackerIsolation.Single(name).TaskType, Is.EqualTo("ValueTask"));
            Assert.That(returned.AsTask(), Is.SameAs(source.Task));
            source.SetResult(true);
            await returned;
            Assert.That(TaskTrackerIsolation.Single(name).Status, Is.EqualTo(TaskStatus.RanToCompletion));
        });

        /// <summary>Checks that the generic ValueTask overload names the result type and keeps the result.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Register_ValueTaskOfT_KeepsResult() => UniTask.ToCoroutine(async () =>
        {
            var name = TaskTrackerIsolation.UniqueName("valuetaskT");
            var source = new TaskCompletionSource<int>();

            var returned = TaskTracker.Register(new ValueTask<int>(source.Task), name, "S", true);
            source.SetResult(42);

            Assert.That(await returned, Is.EqualTo(42));
            var snapshot = TaskTrackerIsolation.Single(name);
            Assert.That(snapshot.TaskType, Is.EqualTo("ValueTask<Int32>"));
            Assert.That(snapshot.IsWorker, Is.True);
        });

        /// <summary>Checks that a registered UniTask completes the returned task and the snapshot when the source completes.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Register_UniTask_TracksCompletion() => UniTask.ToCoroutine(async () =>
        {
            var name = TaskTrackerIsolation.UniqueName("unitask");
            var source = new UniTaskCompletionSource();

            var returned = TaskTracker.Register(source.Task, name, "S");

            var snapshot = TaskTrackerIsolation.Single(name);
            Assert.That(snapshot.TaskType, Is.EqualTo("UniTask"));
            Assert.That(snapshot.IsCompleted, Is.False);
            Assert.That(TaskTracker.HasRunningTasks(true, "S"), Is.True);

            source.TrySetResult();
            await returned;

            Assert.That(TaskTrackerIsolation.Single(name).Status, Is.EqualTo(TaskStatus.RanToCompletion));
        });

        /// <summary>Checks that canceling the source UniTask cancels the returned task with the same token and marks the snapshot.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Register_UniTask_PreservesCancellation() => UniTask.ToCoroutine(async () =>
        {
            var name = TaskTrackerIsolation.UniqueName("unitask-canceled");
            var source = new UniTaskCompletionSource();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var returned = TaskTracker.Register(source.Task, name, "S");

            source.TrySetCanceled(cancellation.Token);
            OperationCanceledException? caught = null;
            try
            {
                await returned;
            }
            catch (OperationCanceledException exception)
            {
                caught = exception;
            }

            Assert.That(caught, Is.Not.Null);
            Assert.That(caught!.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(TaskTrackerIsolation.Single(name).Status, Is.EqualTo(TaskStatus.Canceled));
        });

        /// <summary>Checks that a failing source UniTask fails the returned task with the same exception and marks the snapshot.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Register_UniTask_PreservesException() => UniTask.ToCoroutine(async () =>
        {
            var name = TaskTrackerIsolation.UniqueName("unitask-faulted");
            var source = new UniTaskCompletionSource();
            var failure = new InvalidOperationException("source failed");
            var returned = TaskTracker.Register(source.Task, name, "S");

            source.TrySetException(failure);
            Exception? caught = null;
            try
            {
                await returned;
            }
            catch (Exception exception)
            {
                caught = exception;
            }

            Assert.That(caught, Is.SameAs(failure));
            Assert.That(TaskTrackerIsolation.Single(name).Status, Is.EqualTo(TaskStatus.Faulted));
        });

        /// <summary>Checks that a registered UniTask with a result passes the result through and names the result type.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Register_UniTaskOfT_KeepsResult() => UniTask.ToCoroutine(async () =>
        {
            var name = TaskTrackerIsolation.UniqueName("unitaskT");
            var source = new UniTaskCompletionSource<string>();

            var returned = TaskTracker.Register(source.Task, name, "S", true);
            source.TrySetResult("done");

            Assert.That(await returned, Is.EqualTo("done"));
            var snapshot = TaskTrackerIsolation.Single(name);
            Assert.That(snapshot.TaskType, Is.EqualTo("UniTask<String>"));
            Assert.That(snapshot.IsWorker, Is.True);
            Assert.That(snapshot.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        });

        /// <summary>Checks that canceling the source UniTask with a result cancels the returned task.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Register_UniTaskOfT_PreservesCancellation() => UniTask.ToCoroutine(async () =>
        {
            var name = TaskTrackerIsolation.UniqueName("unitaskT-canceled");
            var source = new UniTaskCompletionSource<int>();
            var returned = TaskTracker.Register(source.Task, name, "S");

            source.TrySetCanceled();
            var canceled = false;
            try
            {
                await returned;
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }

            Assert.That(canceled, Is.True);
            Assert.That(TaskTrackerIsolation.Single(name).Status, Is.EqualTo(TaskStatus.Canceled));
        });

        /// <summary>Checks that a failing source UniTask with a result fails the returned task.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Register_UniTaskOfT_PreservesException() => UniTask.ToCoroutine(async () =>
        {
            var name = TaskTrackerIsolation.UniqueName("unitaskT-faulted");
            var source = new UniTaskCompletionSource<int>();
            var failure = new FormatException("bad result");
            var returned = TaskTracker.Register(source.Task, name, "S");

            source.TrySetException(failure);
            Exception? caught = null;
            try
            {
                await returned;
            }
            catch (Exception exception)
            {
                caught = exception;
            }

            Assert.That(caught, Is.SameAs(failure));
            Assert.That(TaskTrackerIsolation.Single(name).Status, Is.EqualTo(TaskStatus.Faulted));
        });

        /// <summary>Checks that Unregister removes history only and validates its argument.</summary>
        [Test]
        public void Unregister_RemovesHistoryOnly()
        {
            var name = TaskTrackerIsolation.UniqueName("unregister");
            var scene = TaskTrackerIsolation.UniqueName("scene");
            var source = new TaskCompletionSource<int>();
            TaskTracker.Register(source.Task, name, scene);

            Assert.That(TaskTracker.Unregister(source.Task), Is.True);
            Assert.That(TaskTracker.Unregister(source.Task), Is.False);
            Assert.That(TaskTrackerIsolation.Find(name), Is.Empty);
            Assert.That(TaskTracker.HasRunningTasks(true, scene), Is.True);
            Assert.Throws<ArgumentNullException>(() => TaskTracker.Unregister(null!));
            source.SetResult(1);
        }

        /// <summary>Checks that ClearCompleted removes only completed history entries and reports how many.</summary>
        [Test]
        public void ClearCompleted_RemovesCompletedEntriesOnly()
        {
            var completedName = TaskTrackerIsolation.UniqueName("done");
            var runningName = TaskTrackerIsolation.UniqueName("running");
            var source = new TaskCompletionSource<int>();
            TaskTracker.Register(Task.FromResult(1), completedName, "S");
            TaskTracker.Register(source.Task, runningName, "S");

            var removed = TaskTracker.ClearCompleted();

            Assert.That(removed, Is.GreaterThanOrEqualTo(1));
            Assert.That(TaskTrackerIsolation.Find(completedName), Is.Empty);
            Assert.That(TaskTrackerIsolation.Find(runningName), Has.Length.EqualTo(1));
            Assert.That(TaskTracker.GetSnapshots().All(x => !x.IsCompleted), Is.True);
            source.SetResult(1);
        }

        /// <summary>Checks that Clear removes all history but keeps running tasks visible to queries.</summary>
        [Test]
        public void Clear_RemovesHistoryButNotRunningTasks()
        {
            var scene = TaskTrackerIsolation.UniqueName("scene");
            var source = new TaskCompletionSource<int>();
            TaskTracker.Register(source.Task, "clear", scene);

            TaskTracker.Clear();

            Assert.That(TaskTracker.GetSnapshots(), Is.Empty);
            Assert.That(TaskTracker.HasRunningTasks(true, scene), Is.True);
            source.SetResult(1);
        }

        /// <summary>Checks that concurrent registrations from many threads are all counted and released.</summary>
        [Test]
        public void Register_Concurrently_CountsEveryTask()
        {
            var scene = TaskTrackerIsolation.UniqueName("scene");
            var sources = Enumerable.Range(0, 100).Select(_ => new TaskCompletionSource<int>()).ToArray();

            Parallel.For(0, sources.Length, i => TaskTracker.Register(sources[i].Task, "parallel", scene, (i & 1) == 0));

            Assert.That(TaskTracker.GetRunningTaskCount(true, scene), Is.EqualTo(100));
            Assert.That(TaskTracker.GetRunningTaskCount(false, scene), Is.EqualTo(50));
            foreach (var source in sources)
            {
                source.SetResult(0);
            }
            Assert.That(TaskTracker.GetRunningTaskCount(true, scene), Is.EqualTo(0));
        }

        /// <summary>Checks that runtime initialization clears history and running registrations and refreshes the scene.</summary>
        [Test]
        public void InitializeRuntime_ClearsEverything()
        {
            var scene = TaskTrackerIsolation.UniqueName("scene");
            var source = new TaskCompletionSource<int>();
            TaskTracker.Register(source.Task, "init", scene);
            PrivateMember.SetStaticField(typeof(TaskTracker), "_activeScene", "stale");

            PrivateMember.InvokeStatic(typeof(TaskTracker), "InitializeRuntime");

            Assert.That(TaskTracker.GetSnapshots(), Is.Empty);
            Assert.That(TaskTracker.HasRunningTasks(true, scene), Is.False);
            Assert.That(PrivateMember.GetStaticField<string>(typeof(TaskTracker), "_activeScene"), Is.EqualTo(TaskTrackerIsolation.DescribeActiveScene()));
            source.SetResult(1);
        }

        /// <summary>Checks that editor initialization reads the switches from the editor preferences.</summary>
        [Test]
        public void InitializeEditor_ReadsPreferences()
        {
            TaskTracker.EnableTracking = !UnityEditor.EditorPrefs.GetBool("MajdataPlay.TaskTracker.Tracking", true);

            PrivateMember.InvokeStatic(typeof(TaskTracker), "InitializeEditor");

            Assert.That(TaskTracker.EnableTracking, Is.EqualTo(UnityEditor.EditorPrefs.GetBool("MajdataPlay.TaskTracker.Tracking", true)));
            Assert.That(TaskTracker.EnableStackTrace, Is.EqualTo(UnityEditor.EditorPrefs.GetBool("MajdataPlay.TaskTracker.StackTrace", true)));
        }

        /// <summary>Checks that an active scene change refreshes the cached scene name.</summary>
        [Test]
        public void OnActiveSceneChanged_RefreshesCachedScene()
        {
            PrivateMember.SetStaticField(typeof(TaskTracker), "_activeScene", "stale");

            PrivateMember.InvokeStatic(typeof(TaskTracker), "OnActiveSceneChanged", default(Scene), default(Scene));

            Assert.That(PrivateMember.GetStaticField<string>(typeof(TaskTracker), "_activeScene"), Is.EqualTo(TaskTrackerIsolation.DescribeActiveScene()));
        }

        /// <summary>Checks which task statuses a snapshot reports as completed.</summary>
        /// <param name="status">The task status.</param>
        /// <param name="expected">Whether the status counts as completed.</param>
        [TestCase(TaskStatus.Created, false)]
        [TestCase(TaskStatus.WaitingForActivation, false)]
        [TestCase(TaskStatus.Running, false)]
        [TestCase(TaskStatus.RanToCompletion, true)]
        [TestCase(TaskStatus.Canceled, true)]
        [TestCase(TaskStatus.Faulted, true)]
        public void Snapshot_IsCompleted_FollowsStatus(TaskStatus status, bool expected)
        {
            var snapshot = new TaskTrackerSnapshot(1, "n", status, DateTimeOffset.Now, TimeSpan.Zero, "s", false, "stack", false, "type");

            Assert.That(snapshot.IsCompleted, Is.EqualTo(expected));
        }
    }
}
