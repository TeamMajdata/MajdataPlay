using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MajdataPlay.Threading;
using MajdataPlay.Threading.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Threading
{
    /// <summary>
    /// Covers the <see cref="TaskTrackerWindow"/> editor window: loading snapshots into its tree, filtering, searching,
    /// sorting, selection, the details text, automatic reloading and drawing. The tracker history is cleared for each test
    /// and restored afterwards.
    /// </summary>
    public class TaskTrackerWindowTests
    {
        /// <summary>Restores the tracker state after each test.</summary>
        private TaskTrackerIsolation _isolation = null!;

        /// <summary>The sources of the tasks registered by the test, completed in TearDown.</summary>
        private List<TaskCompletionSource<int>> _sources = null!;

        /// <summary>The window under test.</summary>
        private TaskTrackerWindow? _window;

        /// <summary>Saves the tracker state and clears its history.</summary>
        [SetUp]
        public void SetUp()
        {
            _isolation = new TaskTrackerIsolation();
            TaskTracker.EnableTracking = true;
            TaskTracker.Clear();
            _sources = new List<TaskCompletionSource<int>>();
        }

        /// <summary>Closes the window, completes the test tasks and restores the tracker state.</summary>
        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                Object.DestroyImmediate(_window);
            }
            foreach (var source in _sources)
            {
                source.TrySetResult(0);
            }
            _isolation.Dispose();
        }

        /// <summary>Registers a running task.</summary>
        /// <param name="name">The registration name.</param>
        /// <param name="isWorker">Whether the task is a worker.</param>
        /// <returns>The source that completes the task.</returns>
        private TaskCompletionSource<int> RegisterRunning(string name, bool isWorker = false)
        {
            var source = new TaskCompletionSource<int>();
            _sources.Add(source);
            TaskTracker.Register(source.Task, name, "WindowScene", isWorker);
            return source;
        }

        /// <summary>Creates the window, which loads the snapshots in OnEnable.</summary>
        /// <returns>The window.</returns>
        private TaskTrackerWindow CreateWindow()
        {
            _window = ScriptableObject.CreateInstance<TaskTrackerWindow>();
            return _window;
        }

        /// <summary>Gets the window's tree view.</summary>
        /// <param name="window">The window.</param>
        /// <returns>The tree view.</returns>
        private static TreeView<int> GetTree(TaskTrackerWindow window)
        {
            return PrivateMember.GetField<TreeView<int>>(window, "_tree");
        }

        /// <summary>Gets the names of the visible rows in display order.</summary>
        /// <param name="window">The window.</param>
        /// <returns>The row names.</returns>
        private static string[] RowNames(TaskTrackerWindow window)
        {
            return GetTree(window).GetRows().Select(x => PrivateMember.GetField<TaskTrackerSnapshot>(x, "Snapshot").Name).ToArray();
        }

        /// <summary>Checks that enabling the window builds the tree from the current snapshots.</summary>
        [Test]
        public void OnEnable_LoadsSnapshots()
        {
            RegisterRunning("alpha");
            TaskTracker.Register(Task.FromResult(1), "beta", "WindowScene");

            var window = CreateWindow();

            Assert.That(window.minSize, Is.EqualTo(new Vector2(780, 420)));
            Assert.That(RowNames(window), Is.EquivalentTo(new[] { "alpha", "beta" }));
        }

        /// <summary>Checks that the completed filter hides finished tasks.</summary>
        [Test]
        public void Refresh_HideCompleted_HidesFinishedTasks()
        {
            RegisterRunning("running");
            TaskTracker.Register(Task.FromResult(1), "finished", "WindowScene");
            var window = CreateWindow();

            PrivateMember.SetField(window, "_hideCompleted", true);
            PrivateMember.Invoke(window, "Refresh");

            Assert.That(RowNames(window), Is.EqualTo(new[] { "running" }));
        }

        /// <summary>Checks that the worker filter hides worker tasks.</summary>
        [Test]
        public void Refresh_HideWorkers_HidesWorkerTasks()
        {
            RegisterRunning("foreground");
            RegisterRunning("background", true);
            var window = CreateWindow();

            PrivateMember.SetField(window, "_hideWorkers", true);
            PrivateMember.Invoke(window, "Refresh");

            Assert.That(RowNames(window), Is.EqualTo(new[] { "foreground" }));
        }

        /// <summary>Checks that the search matches names case-insensitively and can match nothing.</summary>
        [Test]
        public void Refresh_Search_FiltersRows()
        {
            RegisterRunning("LoadChart");
            RegisterRunning("SaveScore");
            var window = CreateWindow();

            PrivateMember.SetField(window, "_search", "loadchart");
            PrivateMember.Invoke(window, "Refresh");
            Assert.That(RowNames(window), Is.EqualTo(new[] { "LoadChart" }));

            PrivateMember.SetField(window, "_search", "WaitingForActivation");
            PrivateMember.Invoke(window, "Refresh");
            Assert.That(RowNames(window).Length, Is.EqualTo(2));

            PrivateMember.SetField(window, "_search", "no-such-task");
            PrivateMember.Invoke(window, "Refresh");
            Assert.That(RowNames(window), Is.Empty);
        }

        /// <summary>Checks sorting by name in both directions and that every column can be sorted.</summary>
        [Test]
        public void Sorting_OrdersRowsByColumn()
        {
            RegisterRunning("b-task");
            RegisterRunning("a-task", true);
            RegisterRunning("c-task");
            var window = CreateWindow();
            var header = GetTree(window).multiColumnHeader;

            header.SetSorting(1, true);
            Assert.That(RowNames(window), Is.EqualTo(new[] { "a-task", "b-task", "c-task" }));

            header.SetSorting(1, false);
            Assert.That(RowNames(window), Is.EqualTo(new[] { "c-task", "b-task", "a-task" }));

            for (var column = 0; column < 8; column++)
            {
                header.SetSorting(column, column % 2 == 0);
                Assert.That(RowNames(window).Length, Is.EqualTo(3), $"Sorting by column {column} lost rows.");
            }
        }

        /// <summary>Checks that the selected snapshot follows the tree selection.</summary>
        [Test]
        public void SelectedSnapshot_FollowsSelection()
        {
            var source = RegisterRunning("selected");
            var window = CreateWindow();
            var tree = GetTree(window);

            Assert.That(PrivateMember.GetProperty<TaskTrackerSnapshot?>(tree, "SelectedSnapshot"), Is.Null);

            tree.SetSelection(new List<int> { source.Task.Id });

            Assert.That(PrivateMember.GetProperty<TaskTrackerSnapshot?>(tree, "SelectedSnapshot")!.Name, Is.EqualTo("selected"));
        }

        /// <summary>Checks that the tree allows selecting only one task at a time.</summary>
        [Test]
        public void Tree_DisallowsMultiSelection()
        {
            RegisterRunning("single");
            var window = CreateWindow();
            var tree = GetTree(window);

            Assert.That(PrivateMember.Invoke<bool>(tree, "CanMultiSelect", tree.GetRows()[0]), Is.False);
        }

        /// <summary>Checks the details text for snapshots registered on the main thread and on a worker thread.</summary>
        [Test]
        public void GetDetails_DescribesSnapshot()
        {
            var registeredAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, 6, TimeSpan.Zero);
            var cached = new TaskTrackerSnapshot(7, "Load", TaskStatus.Running, registeredAt, TimeSpan.FromSeconds(1.5),
                "Scenes/Game.unity", true, "at Stack.Frame()", true, "UniTask");
            var direct = new TaskTrackerSnapshot(8, "Save", TaskStatus.RanToCompletion, registeredAt, TimeSpan.Zero,
                "Scenes/List.unity", false, "stack", false, "Task");

            var cachedDetails = PrivateMember.InvokeStatic<string>(typeof(TaskTrackerWindow), "GetDetails", cached);
            var directDetails = PrivateMember.InvokeStatic<string>(typeof(TaskTrackerWindow), "GetDetails", direct);

            Assert.That(cachedDetails, Does.StartWith("Task #7: Load\nStatus: Running    Elapsed: "));
            Assert.That(cachedDetails, Does.Contain("Type: UniTask    Worker: True"));
            Assert.That(cachedDetails, Does.Contain("Registered: 2026-01-02 03:04:05.006 +00:00"));
            Assert.That(cachedDetails, Does.Contain("Scene: Scenes/Game.unity (cached active scene; registered on a worker thread)"));
            Assert.That(cachedDetails, Does.EndWith("\n\nat Stack.Frame()"));
            Assert.That(directDetails, Does.Contain("Scene: Scenes/List.unity\n\nstack"));
        }

        /// <summary>Checks that Update reloads once the refresh interval has passed and only while auto reload is on.</summary>
        [Test]
        public void Update_ReloadsOnlyWhenDueAndEnabled()
        {
            var window = CreateWindow();
            RegisterRunning("late");

            PrivateMember.SetField(window, "_autoReload", false);
            PrivateMember.SetField(window, "_nextRefresh", 0d);
            PrivateMember.Invoke(window, "Update");
            Assert.That(RowNames(window), Is.Empty);

            PrivateMember.SetField(window, "_autoReload", true);
            PrivateMember.SetField(window, "_nextRefresh", double.MaxValue);
            PrivateMember.Invoke(window, "Update");
            Assert.That(RowNames(window), Is.Empty);

            PrivateMember.SetField(window, "_nextRefresh", 0d);
            PrivateMember.Invoke(window, "Update");
            Assert.That(RowNames(window), Is.EqualTo(new[] { "late" }));
            Assert.That(PrivateMember.GetField<double>(window, "_nextRefresh"), Is.GreaterThan(EditorApplication.timeSinceStartup));
        }

        /// <summary>Checks that refreshing before the tree exists is ignored.</summary>
        [Test]
        public void Refresh_WithoutTree_DoesNothing()
        {
            var window = CreateWindow();
            PrivateMember.SetField(window, "_tree", null);

            Assert.DoesNotThrow(() => PrivateMember.Invoke(window, "Refresh"));
        }

        /// <summary>Checks that the menu command opens the window and that it draws its rows and details without errors.</summary>
        [Test]
        public void OpenWindow_ShowsAndDrawsWindow()
        {
            var source = RegisterRunning("drawn");
            RegisterRunning("drawn-worker", true);
            TaskTracker.Register(Task.FromResult(1), "drawn-done", "WindowScene");

            TaskTrackerWindow.OpenWindow();
            _window = EditorWindow.GetWindow<TaskTrackerWindow>();
            GetTree(_window).SetSelection(new List<int> { source.Task.Id });
            PrivateMember.Invoke(_window, "RepaintImmediately");

            Assert.That(_window.titleContent.text, Is.EqualTo("Task Tracker"));
            Assert.That(RowNames(_window).Length, Is.EqualTo(3));
            _window.Close();
            _window = null;
        }
    }
}
