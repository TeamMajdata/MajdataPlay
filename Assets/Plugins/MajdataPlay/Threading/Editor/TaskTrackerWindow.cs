using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
#if UNITY_6000_2_OR_NEWER
using TreeView = UnityEditor.IMGUI.Controls.TreeView<int>;
using TreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem<int>;
using TreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;
#endif

namespace MajdataPlay.Threading.Editor
{
    public sealed class TaskTrackerWindow : EditorWindow
    {
        const string PreferencePrefix = "MajdataPlay.TaskTracker.";
        [SerializeField] TreeViewState _treeState = new TreeViewState();
        [SerializeField] bool _autoReload = true;
        [SerializeField] bool _hideCompleted;
        [SerializeField] bool _hideWorkers;
        [SerializeField] string _search = "";
        TaskTreeView _tree;
        Vector2 _detailsScroll;
        double _nextRefresh;
        GUIStyle _detailsStyle;

        [MenuItem("Window/Task Tracker")]
        public static void OpenWindow() => GetWindow<TaskTrackerWindow>("Task Tracker").Show();

        void OnEnable()
        {
            minSize = new Vector2(780, 420);
            _tree = new TaskTreeView(_treeState);
            Refresh();
        }

        void Update()
        {
            if (!_autoReload || EditorApplication.timeSinceStartup < _nextRefresh)
                return;
            _nextRefresh = EditorApplication.timeSinceStartup + 0.2;
            Refresh();
            Repaint();
        }

        void Refresh()
        {
            if (_tree == null)
                return;
            _tree.Snapshots = TaskTracker.GetSnapshots();
            _tree.HideCompleted = _hideCompleted;
            _tree.HideWorkers = _hideWorkers;
            _tree.searchString = _search;
            _tree.Reload();
        }

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var tracking = GUILayout.Toggle(TaskTracker.EnableTracking, "Enable Tracking", EditorStyles.toolbarButton);
                if (tracking != TaskTracker.EnableTracking)
                {
                    TaskTracker.EnableTracking = tracking;
                    EditorPrefs.SetBool(PreferencePrefix + "Tracking", tracking);
                }
                var stacks = GUILayout.Toggle(TaskTracker.EnableStackTrace, "Capture Stack Trace", EditorStyles.toolbarButton);
                if (stacks != TaskTracker.EnableStackTrace)
                {
                    TaskTracker.EnableStackTrace = stacks;
                    EditorPrefs.SetBool(PreferencePrefix + "StackTrace", stacks);
                }
                _autoReload = GUILayout.Toggle(_autoReload, "Auto Reload", EditorStyles.toolbarButton);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Reload", EditorStyles.toolbarButton))
                    Refresh();
                if (GUILayout.Button("Clear Completed", EditorStyles.toolbarButton))
                {
                    TaskTracker.ClearCompleted();
                    Refresh();
                }
                if (GUILayout.Button("Clear All", EditorStyles.toolbarButton))
                {
                    TaskTracker.Clear();
                    Refresh();
                }
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                _hideCompleted = GUILayout.Toggle(_hideCompleted, "Hide Completed", EditorStyles.toolbarButton);
                _hideWorkers = GUILayout.Toggle(_hideWorkers, "Hide Workers", EditorStyles.toolbarButton);
                GUILayout.Label("Search", GUILayout.Width(45));
                _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
                if (EditorGUI.EndChangeCheck())
                    Refresh();
                GUILayout.Label($"Tracked: {_tree.Snapshots.Length}", GUILayout.Width(110));
            }

            GUILayout.Label($"Running non-worker tasks: {TaskTracker.RunningNonWorkerTaskCount}");
            EditorGUILayout.HelpBox("Stack and elapsed time start at registration. Completed entries are retained until cleared. " +
                "Scene is the active scene at registration; background threads use its latest cached value (*). " +
                "Worker is an explicit category, independent of the thread. Disabling tracking or clearing history does not affect running-task queries.", MessageType.Info);
            var tableRect = GUILayoutUtility.GetRect(0, 100000, 100, 100000,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            _tree.OnGUI(tableRect);

            var selected = _tree.SelectedSnapshot;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Registration / Creation Stack");
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(selected == null))
                {
                    if (GUILayout.Button("Copy Details", EditorStyles.toolbarButton))
                        EditorGUIUtility.systemCopyBuffer = GetDetails(selected);
                }
            }

            if (_detailsStyle == null)
                _detailsStyle = new GUIStyle(EditorStyles.label) { wordWrap = false, richText = false };
            using (var scroll = new EditorGUILayout.ScrollViewScope(_detailsScroll, GUILayout.Height(position.height * 0.3f)))
            {
                _detailsScroll = scroll.scrollPosition;
                var details = selected == null ? "Select a Task to inspect its registration stack." : GetDetails(selected);
                var size = _detailsStyle.CalcSize(new GUIContent(details));
                EditorGUILayout.SelectableLabel(details, _detailsStyle,
                    GUILayout.MinWidth(size.x + 15), GUILayout.MinHeight(size.y + 10), GUILayout.ExpandHeight(true));
            }
        }

        static string GetDetails(TaskTrackerSnapshot task)
        {
            return $"Task #{task.Id}: {task.Name}\nStatus: {task.Status}    Elapsed: {task.Elapsed.TotalSeconds:F3} s\n" +
                $"Type: {task.TaskType}    Worker: {task.IsWorker}\n" +
                $"Registered: {task.RegisteredAt:yyyy-MM-dd HH:mm:ss.fff zzz}\nScene: {task.SceneName}" +
                (task.IsSceneCached ? " (cached active scene; registered on a worker thread)" : "") +
                "\n\n" + task.StackTrace;
        }

        sealed class TaskTreeView : TreeView
        {
            public TaskTrackerSnapshot[] Snapshots = Array.Empty<TaskTrackerSnapshot>();
            public bool HideCompleted;
            public bool HideWorkers;
            public TaskTrackerSnapshot SelectedSnapshot
            {
                get
                {
                    var selected = GetSelection();
                    return selected.Count == 0 ? null : Snapshots.FirstOrDefault(task => task.Id == selected[0]);
                }
            }

            public TaskTreeView(TreeViewState state) : base(state, new MultiColumnHeader(new MultiColumnHeaderState(new[]
            {
                Column("ID", 65), Column("Task / Name", 240), Column("Elapsed (s)", 100),
                Column("Status", 150), Column("Scene", 280), Column("Registered", 170),
                Column("Worker", 75), Column("Type", 140)
            })))
            {
                rowHeight = 22;
                showAlternatingRowBackgrounds = true;
                showBorder = true;
                multiColumnHeader.sortingChanged += _ => Reload();
                Reload();
            }

            static MultiColumnHeaderState.Column Column(string name, float width) => new MultiColumnHeaderState.Column
            {
                headerContent = new GUIContent(name),
                width = width,
                minWidth = 50,
                autoResize = true,
                canSort = true,
                allowToggleVisibility = true
            };

            protected override TreeViewItem BuildRoot()
            {
                var root = new TreeViewItem { id = -1, depth = -1, children = new List<TreeViewItem>() };
                IEnumerable<TaskTrackerSnapshot> tasks = Snapshots;
                if (HideCompleted)
                    tasks = tasks.Where(task => !task.IsCompleted);
                if (HideWorkers)
                    tasks = tasks.Where(task => !task.IsWorker);
                var column = multiColumnHeader.sortedColumnIndex;
                Func<TaskTrackerSnapshot, IComparable> key;
                switch (column)
                {
                    case 1: key = task => task.Name; break;
                    case 2: key = task => task.Elapsed; break;
                    case 3: key = task => task.Status; break;
                    case 4: key = task => task.SceneName; break;
                    case 5: key = task => task.RegisteredAt; break;
                    case 6: key = task => task.IsWorker; break;
                    case 7: key = task => task.TaskType; break;
                    default: key = task => task.Id; break;
                }
                tasks = column < 0 || multiColumnHeader.IsSortedAscending(column)
                    ? tasks.OrderBy(key) : tasks.OrderByDescending(key);
                foreach (var task in tasks)
                    root.AddChild(new TaskItem(task));
                return root;
            }

            protected override bool DoesItemMatchSearch(TreeViewItem item, string search)
            {
                var task = ((TaskItem)item).Snapshot;
                return ($"{task.Id} {task.Name} {task.Status} {task.SceneName} {task.TaskType}").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            protected override bool CanMultiSelect(TreeViewItem item) => false;

            protected override void RowGUI(RowGUIArgs args)
            {
                var task = ((TaskItem)args.item).Snapshot;
                for (int i = 0; i < args.GetNumVisibleColumns(); i++)
                {
                    string text;
                    switch (args.GetColumn(i))
                    {
                        case 0: text = task.Id.ToString(); break;
                        case 1: text = task.Name; break;
                        case 2: text = task.Elapsed.TotalSeconds.ToString("F3"); break;
                        case 3: text = task.Status.ToString(); break;
                        case 4: text = task.SceneName + (task.IsSceneCached ? " *" : ""); break;
                        case 6: text = task.IsWorker ? "Yes" : "No"; break;
                        case 7: text = task.TaskType; break;
                        default: text = task.RegisteredAt.ToString("HH:mm:ss.fff"); break;
                    }
                    var rect = args.GetCellRect(i);
                    CenterRectUsingSingleLineHeight(ref rect);
                    EditorGUI.LabelField(rect, new GUIContent(text, text));
                }
            }

            sealed class TaskItem : TreeViewItem
            {
                public readonly TaskTrackerSnapshot Snapshot;
                public TaskItem(TaskTrackerSnapshot task) : base(task.Id, 0, task.Name) => Snapshot = task;
            }
        }
    }
}
