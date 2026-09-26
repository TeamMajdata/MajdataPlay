using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MajdataPlay.Threading
{
    /// <summary>
    /// Tracks explicitly registered Tasks. Register immediately after creation to capture
    /// the creation site. Entries (including completed Tasks) are retained until cleared.
    /// </summary>
    public static class TaskTracker
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<Task, Entry> _entries = new Dictionary<Task, Entry>(new TaskComparer());
        private static readonly Dictionary<Task, Entry> _activeEntries = new Dictionary<Task, Entry>(new TaskComparer());
        private static int _mainThreadId;
        private static string _activeScene = "Unknown (main thread has not initialized)";
        private static volatile bool _enableTracking = true;
        private static volatile bool _enableStackTrace = true;

        /// <summary>Controls diagnostic history only. Running-task queries always remain enabled.</summary>
        public static bool EnableTracking
        {
            get => _enableTracking;
            set => _enableTracking = value;
        }

        /// <summary>Capturing a stack has a performance cost. Changes affect new registrations only.</summary>
        public static bool EnableStackTrace
        {
            get => _enableStackTrace;
            set => _enableStackTrace = value;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void InitializeRuntime()
        {
            lock (Gate)
            {
                _entries.Clear();
                _activeEntries.Clear();
            }
            InitializeSceneTracking();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void InitializeEditor()
        {
            EnableTracking = UnityEditor.EditorPrefs.GetBool("MajdataPlay.TaskTracker.Tracking", true);
            EnableStackTrace = UnityEditor.EditorPrefs.GetBool("MajdataPlay.TaskTracker.StackTrace", true);
            InitializeSceneTracking();
            UnityEditor.EditorApplication.update -= UpdateActiveScene;
            UnityEditor.EditorApplication.update += UpdateActiveScene;
        }
#endif

        static void InitializeSceneTracking()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            UpdateActiveScene();
        }

        static void OnActiveSceneChanged(Scene previous, Scene current) => UpdateActiveScene();

        static void UpdateActiveScene()
        {
            var scene = SceneManager.GetActiveScene();
            var name = !scene.IsValid() ? "No active scene" :
                string.IsNullOrEmpty(scene.path) ? scene.name + " (unsaved)" : scene.path;
            Volatile.Write(ref _activeScene, name);
        }

        /// <summary>
        /// Registers once per Task instance and returns the original Task without changing its
        /// result, cancellation or exception behavior. Timing and stack capture start here.
        /// On worker threads, sceneName defaults to the latest cached active scene.
        /// Pass an owning object's scene explicitly when using additive scenes.
        /// isWorker is an explicit category, not a thread check. The first registration wins.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static Task Register(Task task, string name = null, string sceneName = null, bool isWorker = false)
        {
            RegisterCore(task, name, sceneName, isWorker);
            return task;
        }

        /// <summary>Registers a Task while preserving its result type.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static Task<T> Register<T>(Task<T> task, string name = null, string sceneName = null, bool isWorker = false)
        {
            RegisterCore(task, name, sceneName, isWorker);
            return task;
        }

        /// <summary>
        /// Consumes the original once. Always use the returned ValueTask; do not await or
        /// register the original again. Conversion also occurs when diagnostic tracking is disabled.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ValueTask Register(ValueTask task, string name = null, string sceneName = null, bool isWorker = false)
        {
            var backingTask = task.AsTask();
            RegisterCore(backingTask, name, sceneName, isWorker, "ValueTask");
            return new ValueTask(backingTask);
        }

        /// <summary>Consumes the original once. Always use the returned ValueTask instead of the original.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ValueTask<T> Register<T>(ValueTask<T> task, string name = null, string sceneName = null, bool isWorker = false)
        {
            var backingTask = task.AsTask();
            RegisterCore(backingTask, name, sceneName, isWorker, "ValueTask<" + typeof(T).Name + ">");
            return new ValueTask<T>(backingTask);
        }

        /// <summary>
        /// Consumes the original once. Always use the returned UniTask; do not await or
        /// register the original again. Completion is tracked even before the returned value is awaited.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static UniTask Register(UniTask task, string name = null, string sceneName = null, bool isWorker = false)
        {
            var backing = new TaskCompletionSource<bool>();
            var result = new UniTaskCompletionSource();
            RegisterCore(backing.Task, name, sceneName, isWorker, "UniTask");
            ObserveUniTask(task, backing, result).Forget();
            return result.Task;
        }

        /// <summary>Consumes the original once. Always use the returned UniTask instead of the original.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static UniTask<T> Register<T>(UniTask<T> task, string name = null, string sceneName = null, bool isWorker = false)
        {
            var backing = new TaskCompletionSource<T>();
            var result = new UniTaskCompletionSource<T>();
            RegisterCore(backing.Task, name, sceneName, isWorker, "UniTask<" + typeof(T).Name + ">");
            ObserveUniTask(task, backing, result).Forget();
            return result.Task;
        }

        // Complete the returned promise on the source's continuation thread, preserving UniTask's
        // thread behavior. AsTask() in the bundled UniTask maps cancellation to a faulted Task,
        // so explicitly preserve cancellation here instead of using that conversion.
        static async UniTaskVoid ObserveUniTask(UniTask source, TaskCompletionSource<bool> backing,
            UniTaskCompletionSource result)
        {
            try
            {
                await source;
                backing.TrySetResult(true);
                result.TrySetResult();
            }
            catch (OperationCanceledException ex)
            {
                backing.TrySetCanceled(ex.CancellationToken);
                result.TrySetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                backing.TrySetException(ex);
                // The returned promise owns exception reporting; the bookkeeping Task does not.
                _ = backing.Task.Exception;
                result.TrySetException(ex);
            }
        }

        static async UniTaskVoid ObserveUniTask<T>(UniTask<T> source, TaskCompletionSource<T> backing,
            UniTaskCompletionSource<T> result)
        {
            try
            {
                var value = await source;
                backing.TrySetResult(value);
                result.TrySetResult(value);
            }
            catch (OperationCanceledException ex)
            {
                backing.TrySetCanceled(ex.CancellationToken);
                result.TrySetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                backing.TrySetException(ex);
                _ = backing.Task.Exception;
                result.TrySetException(ex);
            }
        }

        /// <summary>True while any registered non-worker task is incomplete, including tasks not yet started.</summary>
        public static bool HasRunningNonWorkerTasks => HasRunningTasks(includeWorkers: false);
        public static int RunningNonWorkerTaskCount => GetRunningTaskCount(includeWorkers: false);

        /// <summary>
        /// Queries incomplete registrations. Null sceneName matches all scenes; otherwise it must
        /// exactly match the recorded string (normally Scene.path). This is a point-in-time query,
        /// not a barrier preventing new registrations. Canceled/faulted tasks do not block.
        /// Worker classification is explicit and independent of the executing thread.
        /// </summary>
        public static bool HasRunningTasks(bool includeWorkers = true, string sceneName = null)
        {
            lock (Gate)
            {
                foreach (var entry in _activeEntries.Values)
                    if (MatchesRunning(entry, includeWorkers, sceneName))
                        return true;
                return false;
            }
        }

        /// <summary>Counts incomplete registrations with the same filters as HasRunningTasks.</summary>
        public static int GetRunningTaskCount(bool includeWorkers = true, string sceneName = null)
        {
            lock (Gate)
            {
                int count = 0;
                foreach (var entry in _activeEntries.Values)
                    if (MatchesRunning(entry, includeWorkers, sceneName))
                        count++;
                return count;
            }
        }

        static bool MatchesRunning(Entry entry, bool includeWorkers, string sceneName)
        {
            return !entry.Task.IsCompleted && (includeWorkers || !entry.IsWorker) &&
                (sceneName == null || string.Equals(entry.SceneName, sceneName, StringComparison.Ordinal));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void RegisterCore(Task task, string name, string sceneName, bool isWorker, string taskType = null)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));
            lock (Gate)
            {
                if (_entries.ContainsKey(task) || _activeEntries.ContainsKey(task))
                    return;
                bool captureHistory = EnableTracking;
                if (!captureHistory && task.IsCompleted)
                    return;

                bool cachedScene = sceneName == null && Thread.CurrentThread.ManagedThreadId != _mainThreadId;
                if (sceneName == null && !cachedScene)
                    UpdateActiveScene();

                var entry = new Entry(task, name, sceneName ?? Volatile.Read(ref _activeScene), cachedScene,
                    captureHistory && EnableStackTrace ? new StackTrace(2, true).ToString() : "Stack trace capture was disabled.",
                    isWorker, taskType ?? task.GetType().ToString());
                if (captureHistory)
                    _entries.Add(task, entry);
                if (task.IsCompleted)
                {
                    // Its actual duration before registration is unavailable.
                    entry.CompletedTimestamp = entry.StartTimestamp;
                }
                else
                {
                    _activeEntries.Add(task, entry);
                    task.ContinueWith((_, state) =>
                    {
                        var tracked = (Entry)state;
                        Interlocked.CompareExchange(ref tracked.CompletedTimestamp, Stopwatch.GetTimestamp(), 0);
                        lock (Gate)
                        {
                            // A new play session may have registered the same Task again.
                            if (_activeEntries.TryGetValue(tracked.Task, out var active) && ReferenceEquals(active, tracked))
                                _activeEntries.Remove(tracked.Task);
                        }
                    }, entry, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
            }
        }

        /// <summary>Returns immutable snapshots; safe to call concurrently with registration.</summary>
        public static TaskTrackerSnapshot[] GetSnapshots()
        {
            lock (Gate)
            {
                var snapshots = new TaskTrackerSnapshot[_entries.Count];
                int index = 0;
                foreach (var entry in _entries.Values)
                {
                    var status = entry.Task.Status;
                    var completed = Interlocked.Read(ref entry.CompletedTimestamp);
                    var end = completed == 0 ? Stopwatch.GetTimestamp() : completed;
                    snapshots[index++] = new TaskTrackerSnapshot(entry.Task.Id, entry.Name, status,
                        entry.RegisteredAt, TimeSpan.FromSeconds((end - entry.StartTimestamp) / (double)Stopwatch.Frequency),
                        entry.SceneName, entry.IsSceneCached, entry.StackTrace, entry.IsWorker, entry.TaskType);
                }
                return snapshots;
            }
        }

        /// <summary>Removes diagnostic history only; running tasks still participate in queries.</summary>
        public static bool Unregister(Task task)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));
            lock (Gate)
                return _entries.Remove(task);
        }

        public static int ClearCompleted()
        {
            lock (Gate)
            {
                var completed = new List<Task>();
                foreach (var task in _entries.Keys)
                    if (task.IsCompleted)
                        completed.Add(task);
                foreach (var task in completed)
                    _entries.Remove(task);
                return completed.Count;
            }
        }

        /// <summary>Clears diagnostic history without hiding running tasks from queries.</summary>
        public static void Clear()
        {
            lock (Gate)
                _entries.Clear();
        }

        sealed class TaskComparer : IEqualityComparer<Task>
        {
            public bool Equals(Task x, Task y) => ReferenceEquals(x, y);
            public int GetHashCode(Task task) => RuntimeHelpers.GetHashCode(task);
        }

        sealed class Entry
        {
            public readonly Task Task;
            public readonly string Name;
            public readonly string SceneName;
            public readonly bool IsSceneCached;
            public readonly string StackTrace;
            public readonly bool IsWorker;
            public readonly string TaskType;
            public readonly DateTimeOffset RegisteredAt = DateTimeOffset.Now;
            public readonly long StartTimestamp = Stopwatch.GetTimestamp();
            public long CompletedTimestamp;

            public Entry(Task task, string name, string sceneName, bool isSceneCached, string stackTrace,
                bool isWorker, string taskType)
            {
                Task = task;
                Name = string.IsNullOrEmpty(name) ? taskType : name;
                IsWorker = isWorker;
                TaskType = taskType;
                SceneName = sceneName;
                IsSceneCached = isSceneCached;
                StackTrace = stackTrace;
            }
        }
    }

    /// <summary>A point-in-time view of a registered Task, without exposing the Task itself.</summary>
    public sealed class TaskTrackerSnapshot
    {
        public int Id { get; }
        public string Name { get; }
        public TaskStatus Status { get; }
        public DateTimeOffset RegisteredAt { get; }
        public TimeSpan Elapsed { get; }
        public string SceneName { get; }
        public bool IsSceneCached { get; }
        public string StackTrace { get; }
        public bool IsWorker { get; }
        public string TaskType { get; }
        public bool IsCompleted => Status == TaskStatus.RanToCompletion || Status == TaskStatus.Canceled || Status == TaskStatus.Faulted;

        internal TaskTrackerSnapshot(int id, string name, TaskStatus status, DateTimeOffset registeredAt,
            TimeSpan elapsed, string sceneName, bool isSceneCached, string stackTrace, bool isWorker, string taskType)
        {
            Id = id;
            IsWorker = isWorker;
            TaskType = taskType;
            Name = name;
            Status = status;
            RegisteredAt = registeredAt;
            Elapsed = elapsed;
            SceneName = sceneName;
            IsSceneCached = isSceneCached;
            StackTrace = stackTrace;
        }
    }
}
