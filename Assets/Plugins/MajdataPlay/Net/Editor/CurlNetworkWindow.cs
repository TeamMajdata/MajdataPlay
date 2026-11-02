#nullable enable
using MajdataPlay.Net.Curl.Diagnostics;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
#if UNITY_6000_2_OR_NEWER
using TreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;
#endif

namespace MajdataPlay.Net.Editor
{
    /// <summary>
    /// An Editor window in the style of a browser's network panel that lists the requests sent through
    /// <see cref="Curl.CurlHttpMessageHandler"/>, both running and finished, with their headers, bodies, cookies and timings.
    /// </summary>
    /// <remarks>
    /// Recording through <see cref="CurlNetworkMonitor"/> is on while the window is open and its Record toggle is on.
    /// The history lives in memory, so a domain reload (entering Play Mode with domain reload enabled) clears it.
    /// </remarks>
    public sealed class CurlNetworkWindow : EditorWindow
    {
        /// <summary>
        /// The window title.
        /// </summary>
        private const string WindowTitle = "Curl Network";

        /// <summary>
        /// The prefix of the window's EditorPrefs keys.
        /// </summary>
        private const string PreferencePrefix = "MajdataPlay.CurlNetwork.";

        /// <summary>
        /// The EditorPrefs key of <see cref="CurlNetworkMonitor.MaxRecordCount"/>.
        /// </summary>
        private const string MaxRecordCountPreference = PreferencePrefix + "MaxRecordCount";

        /// <summary>
        /// The EditorPrefs key of <see cref="CurlNetworkMonitor.MaxBodyCaptureBytes"/>.
        /// </summary>
        private const string MaxBodyCaptureBytesPreference = PreferencePrefix + "MaxBodyCaptureBytes";

        /// <summary>
        /// The time between checks for new data, in seconds.
        /// </summary>
        private const double RefreshInterval = 0.25;

        /// <summary>
        /// The height of the status bar.
        /// </summary>
        private const float StatusBarHeight = 20f;

        /// <summary>
        /// The width of the grip between the table and the details panel.
        /// </summary>
        private const float SplitterWidth = 4f;

        /// <summary>
        /// The narrowest the table gets while the details panel is open.
        /// </summary>
        private const float MinTableWidth = 220f;

        /// <summary>
        /// The narrowest the details panel gets.
        /// </summary>
        private const float MinDetailsWidth = 300f;

        /// <summary>
        /// The history sizes offered in the settings menu.
        /// </summary>
        private static readonly int[] s_recordCountChoices = { 100, 500, 1000, 5000 };

        /// <summary>
        /// The per-body capture limits offered in the settings menu.
        /// </summary>
        private static readonly int[] s_bodyCaptureChoices = { 0, 64 * 1024, 1024 * 1024, 8 * 1024 * 1024 };

        /// <summary>
        /// The state filter names, in <see cref="CurlNetworkStateFilter"/> order.
        /// </summary>
        private static readonly string[] s_stateFilterNames = { "All", "Pending", "Finished", "Errors" };

        /// <summary>
        /// The serialized table state: selection, scroll position and expanded items.
        /// </summary>
        [SerializeField]
        private TreeViewState _tableState = new();

        /// <summary>
        /// The serialized column layout: widths, visibility and sorting.
        /// </summary>
        [SerializeField]
        private MultiColumnHeaderState? _headerState;

        /// <summary>
        /// The details panel, serialized to keep its tab and display modes.
        /// </summary>
        [SerializeField]
        private CurlNetworkDetailsView _details = new();

        /// <summary>
        /// Whether new requests are recorded.
        /// </summary>
        [SerializeField]
        private bool _isRecording = true;

        /// <summary>
        /// The text filter.
        /// </summary>
        [SerializeField]
        private string _filter = string.Empty;

        /// <summary>
        /// The state filter.
        /// </summary>
        [SerializeField]
        private CurlNetworkStateFilter _stateFilter;

        /// <summary>
        /// The width of the details panel.
        /// </summary>
        [SerializeField]
        private float _detailsWidth = 480f;

        /// <summary>
        /// The request table.
        /// </summary>
        private CurlNetworkTable? _table;

        /// <summary>
        /// The records of the last refresh by id.
        /// </summary>
        private readonly Dictionary<long, CurlNetworkRecord> _records = new();

        /// <summary>
        /// The snapshots of the last refresh, oldest first.
        /// </summary>
        private CurlNetworkSnapshot[] _snapshots = Array.Empty<CurlNetworkSnapshot>();

        /// <summary>
        /// The <see cref="CurlNetworkMonitor.Version"/> of the last refresh.
        /// </summary>
        private int _lastVersion;

        /// <summary>
        /// The editor time of the next check for new data.
        /// </summary>
        private double _nextRefresh;

        /// <summary>
        /// Whether the splitter is being dragged.
        /// </summary>
        private bool _isResizing;

        /// <summary>
        /// The text a context menu command copies.
        /// </summary>
        internal enum CopyTarget
        {
            /// <summary>
            /// The requested URL.
            /// </summary>
            Url,

            /// <summary>
            /// A bash cURL command repeating the request.
            /// </summary>
            Curl,

            /// <summary>
            /// The request headers as they appear on the wire.
            /// </summary>
            RequestHeaders,

            /// <summary>
            /// The response headers as they appear on the wire.
            /// </summary>
            ResponseHeaders,

            /// <summary>
            /// The captured response body as text.
            /// </summary>
            ResponseBody,
        }

        /// <summary>
        /// Opens the window.
        /// </summary>
        [MenuItem("Window/Curl Network")]
        public static void OpenWindow()
        {
            GetWindow<CurlNetworkWindow>(WindowTitle).Show();
        }

        /// <summary>
        /// Builds the table, applies the saved settings, starts recording and loads the history.
        /// </summary>
        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            minSize = new Vector2(640, 320);
            ApplyPreferences();
            var headerState = CurlNetworkTable.CreateHeaderState();
            if (MultiColumnHeaderState.CanOverwriteSerializedFields(_headerState, headerState))
            {
                MultiColumnHeaderState.OverwriteSerializedFields(_headerState, headerState);
            }
            _headerState = headerState;
            _table = new CurlNetworkTable(_tableState, headerState)
            {
                Filter = _filter,
                StateFilter = _stateFilter,
            };
            _table.SelectedSnapshotChanged += OnSelectedSnapshotChanged;
            _table.SnapshotContextClicked += OnSnapshotContextClicked;
            _details.CloseRequested += OnDetailsCloseRequested;
            CurlNetworkMonitor.IsEnabled = _isRecording;
            Refresh();
        }

        /// <summary>
        /// Detaches from the table and the details panel and frees preview textures.
        /// </summary>
        private void OnDisable()
        {
            if (_table is not null)
            {
                _table.SelectedSnapshotChanged -= OnSelectedSnapshotChanged;
                _table.SnapshotContextClicked -= OnSnapshotContextClicked;
            }
            _details.CloseRequested -= OnDetailsCloseRequested;
            _details.Dispose();
        }

        /// <summary>
        /// Stops recording once the window is closed.
        /// </summary>
        private void OnDestroy()
        {
            CurlNetworkMonitor.IsEnabled = false;
        }

        /// <summary>
        /// Reloads when the history changed and keeps pending durations moving.
        /// </summary>
        private void Update()
        {
            if (EditorApplication.timeSinceStartup < _nextRefresh)
            {
                return;
            }
            _nextRefresh = EditorApplication.timeSinceStartup + RefreshInterval;
            if (CurlNetworkMonitor.Version != _lastVersion)
            {
                Refresh();
                Repaint();
            }
            else if (_table is not null && _table.HasPendingRows)
            {
                Repaint();
            }
        }

        /// <summary>
        /// Takes new snapshots of the history and rebuilds the table and the details panel.
        /// </summary>
        private void Refresh()
        {
            if (_table is null)
            {
                return;
            }
            // Read before the records, so a change made during the refresh triggers another one.
            _lastVersion = CurlNetworkMonitor.Version;
            var records = CurlNetworkMonitor.GetRecords();
            _records.Clear();
            _snapshots = new CurlNetworkSnapshot[records.Length];
            for (var i = 0; i < records.Length; i++)
            {
                _records[records[i].Id] = records[i];
                _snapshots[i] = records[i].GetSnapshot();
            }
            _table.Filter = _filter;
            _table.StateFilter = _stateFilter;
            _table.SetSnapshots(_snapshots);
            UpdateDetails();
        }

        /// <summary>
        /// Draws the toolbar, the table, the details panel of the selected request and the status bar.
        /// </summary>
        private void OnGUI()
        {
            if (_table is null)
            {
                return;
            }
            var toolbarHeight = EditorStyles.toolbar.fixedHeight > 0 ? EditorStyles.toolbar.fixedHeight : 21f;
            DrawToolbar(new Rect(0, 0, position.width, toolbarHeight));
            var content = new Rect(0, toolbarHeight, position.width, Math.Max(0, position.height - toolbarHeight - StatusBarHeight));
            if (_details.Record is null)
            {
                _table.UpdateTimeline();
                _table.OnGUI(content);
            }
            else
            {
                var detailsWidth = Mathf.Clamp(_detailsWidth, MinDetailsWidth, Math.Max(MinDetailsWidth, content.width - MinTableWidth));
                var tableRect = new Rect(content.x, content.y, Math.Max(0, content.width - detailsWidth - SplitterWidth), content.height);
                var splitter = new Rect(tableRect.xMax, content.y, SplitterWidth, content.height);
                var detailsRect = new Rect(splitter.xMax, content.y, Math.Max(0, content.xMax - splitter.xMax), content.height);
                HandleSplitter(splitter, content);
                _table.UpdateTimeline();
                _table.OnGUI(tableRect);
                _details.OnGUI(detailsRect);
            }
            DrawStatusBar(new Rect(0, position.height - StatusBarHeight, position.width, StatusBarHeight));
        }

        /// <summary>
        /// Gets the status bar text.
        /// </summary>
        /// <param name="visible">The listed requests.</param>
        /// <param name="all">Every recorded request.</param>
        /// <param name="isRecording">Whether recording is on.</param>
        /// <returns>The request counts, the received bytes, the pending count and the body capture use.</returns>
        internal static string GetStatusText(IReadOnlyList<CurlNetworkSnapshot> visible, IReadOnlyList<CurlNetworkSnapshot> all, bool isRecording)
        {
            var transferred = 0L;
            var pending = 0;
            foreach (var snapshot in visible)
            {
                transferred += snapshot.TransferredSize;
                if (!snapshot.IsFinished)
                {
                    pending++;
                }
            }
            var requests = visible.Count == all.Count
                ? $"{all.Count} requests"
                : $"{visible.Count} / {all.Count} requests";
            var bodies = $"{CurlNetworkFormatter.FormatSize(CurlNetworkMonitor.CapturedBodyBytes)} / {CurlNetworkFormatter.FormatSize(CurlNetworkMonitor.MaxTotalBodyCaptureBytes)} bodies kept";
            var status = $"{requests} | {CurlNetworkFormatter.FormatSize(transferred)} received | {pending} pending | {bodies}";

            return isRecording ? status : status + " | Recording paused";
        }

        /// <summary>
        /// Gets the text a context menu command copies.
        /// </summary>
        /// <param name="target">What to copy.</param>
        /// <param name="snapshot">The request.</param>
        /// <param name="record">Its record, which holds the bodies.</param>
        /// <returns>The text for the clipboard.</returns>
        internal static string GetCopyText(CopyTarget target, CurlNetworkSnapshot snapshot, CurlNetworkRecord record)
        {
            switch (target)
            {
                case CopyTarget.Curl:
                    return CurlNetworkFormatter.BuildCurlCommand(snapshot, record.GetRequestBody());
                case CopyTarget.RequestHeaders:
                    return CurlNetworkFormatter.FormatHeaderBlock(snapshot.RequestLine, snapshot.RequestHeaders);
                case CopyTarget.ResponseHeaders:
                    return CurlNetworkFormatter.FormatHeaderBlock(snapshot.StatusLine, snapshot.ResponseHeaders);
                case CopyTarget.ResponseBody:
                    return CurlNetworkFormatter.DecodeText(record.GetResponseBody(), snapshot.ContentType);
                default:
                    return snapshot.Url;
            }
        }

        /// <summary>
        /// Draws the toolbar: recording, clearing, the filters and the settings menu.
        /// </summary>
        /// <param name="rect">The toolbar area.</param>
        private void DrawToolbar(Rect rect)
        {
            GUILayout.BeginArea(rect);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var recordContent = new GUIContent(_isRecording ? "Recording" : "Record", "Record the requests sent through the libcurl HTTP handler.");
                var isRecording = GUILayout.Toggle(_isRecording, recordContent, EditorStyles.toolbarButton, GUILayout.Width(76));
                if (isRecording != _isRecording)
                {
                    SetRecording(isRecording);
                }
                if (GUILayout.Button(new GUIContent("Clear", "Remove every request from the history."), EditorStyles.toolbarButton, GUILayout.Width(44)))
                {
                    CurlNetworkMonitor.Clear();
                    Refresh();
                }
                GUILayout.Space(6);
                EditorGUI.BeginChangeCheck();
                _filter = EditorGUILayout.TextField(_filter, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120), GUILayout.MaxWidth(280));
                _stateFilter = (CurlNetworkStateFilter)GUILayout.Toolbar((int)_stateFilter, s_stateFilterNames, EditorStyles.toolbarButton);
                if (EditorGUI.EndChangeCheck())
                {
                    Refresh();
                }
                GUILayout.FlexibleSpace();
                if (EditorGUILayout.DropdownButton(new GUIContent("Settings"), FocusType.Passive, EditorStyles.toolbarDropDown))
                {
                    CreateSettingsMenu().ShowAsContext();
                }
            }
            GUILayout.EndArea();
        }

        /// <summary>
        /// Draws the status bar.
        /// </summary>
        /// <param name="rect">The status bar area.</param>
        private void DrawStatusBar(Rect rect)
        {
            if (Event.current.type == EventType.Repaint)
            {
                EditorStyles.toolbar.Draw(rect, false, false, false, false);
            }
            var visible = _table?.VisibleSnapshots ?? Array.Empty<CurlNetworkSnapshot>();
            GUI.Label(new Rect(rect.x + 6, rect.y + 2, rect.width - 12, rect.height - 2), GetStatusText(visible, _snapshots, _isRecording), EditorStyles.miniLabel);
        }

        /// <summary>
        /// Lets the splitter between the table and the details panel be dragged.
        /// </summary>
        /// <param name="splitter">The splitter area.</param>
        /// <param name="content">The area shared by the table and the details panel.</param>
        private void HandleSplitter(Rect splitter, Rect content)
        {
            EditorGUIUtility.AddCursorRect(splitter, MouseCursor.ResizeHorizontal);
            var current = Event.current;
            switch (current.type)
            {
                case EventType.MouseDown:
                    if (splitter.Contains(current.mousePosition))
                    {
                        _isResizing = true;
                        current.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (_isResizing)
                    {
                        _detailsWidth = Mathf.Clamp(content.xMax - current.mousePosition.x, MinDetailsWidth, Math.Max(MinDetailsWidth, content.width - MinTableWidth));
                        current.Use();
                        Repaint();
                    }
                    break;
                case EventType.MouseUp:
                    _isResizing = false;
                    break;
                case EventType.Repaint:
                    EditorGUI.DrawRect(new Rect(splitter.x + 1, splitter.y, 1, splitter.height), new Color(0f, 0f, 0f, 0.35f));
                    break;
            }
        }

        /// <summary>
        /// Turns recording on or off.
        /// </summary>
        /// <param name="isRecording">Whether new requests are recorded.</param>
        private void SetRecording(bool isRecording)
        {
            _isRecording = isRecording;
            CurlNetworkMonitor.IsEnabled = isRecording;
        }

        /// <summary>
        /// Shows the record of the selected request in the details panel, or closes the panel.
        /// </summary>
        private void UpdateDetails()
        {
            var selected = _table?.SelectedSnapshot;
            var record = selected is not null && _records.TryGetValue(selected.Id, out var found) ? found : null;
            _details.SetRecord(record);
        }

        /// <summary>
        /// Opens the details of the newly selected request.
        /// </summary>
        /// <param name="snapshot">The selected request, or <see langword="null"/>.</param>
        private void OnSelectedSnapshotChanged(CurlNetworkSnapshot? snapshot)
        {
            UpdateDetails();
            Repaint();
        }

        /// <summary>
        /// Closes the details panel by clearing the selection.
        /// </summary>
        private void OnDetailsCloseRequested()
        {
            _table?.SetSelection(new List<int>());
            UpdateDetails();
            Repaint();
        }

        /// <summary>
        /// Shows the copy commands of a right-clicked request.
        /// </summary>
        /// <param name="snapshot">The right-clicked request.</param>
        private void OnSnapshotContextClicked(CurlNetworkSnapshot snapshot)
        {
            if (!_records.TryGetValue(snapshot.Id, out var record))
            {
                return;
            }
            var menu = new GenericMenu();
            AddCopyItem(menu, "Copy URL", CopyTarget.Url, snapshot, record);
            AddCopyItem(menu, "Copy as cURL (bash)", CopyTarget.Curl, snapshot, record);
            AddCopyItem(menu, "Copy Request Headers", CopyTarget.RequestHeaders, snapshot, record);
            AddCopyItem(menu, "Copy Response Headers", CopyTarget.ResponseHeaders, snapshot, record);
            AddCopyItem(menu, "Copy Response", CopyTarget.ResponseBody, snapshot, record);
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Clear History"), false, () =>
            {
                CurlNetworkMonitor.Clear();
                Refresh();
            });
            menu.ShowAsContext();
        }

        /// <summary>
        /// Adds a command that copies text about a request to the clipboard.
        /// </summary>
        /// <param name="menu">The menu.</param>
        /// <param name="label">The command label.</param>
        /// <param name="target">What to copy.</param>
        /// <param name="snapshot">The request.</param>
        /// <param name="record">Its record.</param>
        private static void AddCopyItem(GenericMenu menu, string label, CopyTarget target, CurlNetworkSnapshot snapshot, CurlNetworkRecord record)
        {
            menu.AddItem(new GUIContent(label), false, () => EditorGUIUtility.systemCopyBuffer = GetCopyText(target, snapshot, record));
        }

        /// <summary>
        /// Creates the settings menu: the history size and the per-body capture limit.
        /// </summary>
        /// <returns>The menu.</returns>
        private GenericMenu CreateSettingsMenu()
        {
            var menu = new GenericMenu();
            foreach (var count in s_recordCountChoices)
            {
                var label = new GUIContent($"History Size/{count.ToString(CultureInfo.InvariantCulture)} requests");
                menu.AddItem(label, CurlNetworkMonitor.MaxRecordCount == count, () =>
                {
                    EditorPrefs.SetInt(MaxRecordCountPreference, count);
                    ApplyPreferences();
                    Refresh();
                });
            }
            foreach (var bytes in s_bodyCaptureChoices)
            {
                var name = bytes == 0 ? "Off (sizes only)" : CurlNetworkFormatter.FormatSize(bytes);
                menu.AddItem(new GUIContent($"Body Capture Per Request/{name}"), CurlNetworkMonitor.MaxBodyCaptureBytes == bytes, () =>
                {
                    EditorPrefs.SetInt(MaxBodyCaptureBytesPreference, bytes);
                    ApplyPreferences();
                });
            }

            return menu;
        }

        /// <summary>
        /// Applies the saved history size and body capture limit to <see cref="CurlNetworkMonitor"/>.
        /// </summary>
        private static void ApplyPreferences()
        {
            CurlNetworkMonitor.MaxRecordCount = Math.Max(1, EditorPrefs.GetInt(MaxRecordCountPreference, CurlNetworkMonitor.DefaultMaxRecordCount));
            CurlNetworkMonitor.MaxBodyCaptureBytes = Math.Max(0, EditorPrefs.GetInt(MaxBodyCaptureBytesPreference, CurlNetworkMonitor.DefaultMaxBodyCaptureBytes));
        }
    }
}
