#nullable enable
using MajdataPlay.Net.Curl.Diagnostics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
#if UNITY_6000_2_OR_NEWER
using TreeView = UnityEditor.IMGUI.Controls.TreeView<int>;
using TreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem<int>;
using TreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;
#endif

namespace MajdataPlay.Net.Editor
{
    /// <summary>
    /// The request table of the Curl Network window: one row per request with sortable, hideable columns and a waterfall.
    /// </summary>
    internal sealed class CurlNetworkTable : TreeView
    {
        /// <summary>
        /// The height of a row.
        /// </summary>
        private const float RowHeight = 20f;

        /// <summary>
        /// The color of the queueing part of a waterfall bar.
        /// </summary>
        private static readonly Color s_queueColor = new(0.62f, 0.62f, 0.62f, 0.9f);

        /// <summary>
        /// The color of the waiting-for-first-byte part of a waterfall bar.
        /// </summary>
        private static readonly Color s_waitColor = new(0.30f, 0.72f, 0.40f, 1f);

        /// <summary>
        /// The color of the content download part of a waterfall bar.
        /// </summary>
        private static readonly Color s_downloadColor = new(0.30f, 0.56f, 0.95f, 1f);

        /// <summary>
        /// The text color of error rows.
        /// </summary>
        private static readonly Color s_errorColor = new(0.95f, 0.36f, 0.36f, 1f);

        /// <summary>
        /// Every request, oldest first.
        /// </summary>
        private IReadOnlyList<CurlNetworkSnapshot> _snapshots = Array.Empty<CurlNetworkSnapshot>();

        /// <summary>
        /// The requests that pass the filters, in display order.
        /// </summary>
        private readonly List<CurlNetworkSnapshot> _visible = new();

        /// <summary>
        /// The visible requests by tree item id.
        /// </summary>
        private readonly Dictionary<int, CurlNetworkSnapshot> _byItemId = new();

        /// <summary>
        /// The <see cref="Stopwatch"/> timestamp at which the waterfall starts.
        /// </summary>
        private long _timelineStart;

        /// <summary>
        /// The length of the waterfall time line.
        /// </summary>
        private TimeSpan _timelineLength;

        /// <summary>
        /// The <see cref="Stopwatch"/> timestamp the rows are drawn at.
        /// </summary>
        private long _now;

        /// <summary>
        /// The style of ordinary cells, created on first draw.
        /// </summary>
        private GUIStyle? _cellStyle;

        /// <summary>
        /// The style of right-aligned numeric cells, created on first draw.
        /// </summary>
        private GUIStyle? _numberStyle;

        /// <summary>
        /// Creates the table.
        /// </summary>
        /// <param name="state">The serialized tree state, which keeps the selection and scroll position.</param>
        /// <param name="headerState">The serialized column state.</param>
        public CurlNetworkTable(TreeViewState state, MultiColumnHeaderState headerState)
            : base(state, new MultiColumnHeader(headerState))
        {
            rowHeight = RowHeight;
            showAlternatingRowBackgrounds = true;
            showBorder = true;
            multiColumnHeader.sortingChanged += OnSortingChanged;
            Reload();
        }

        /// <summary>
        /// Raised when the selected request changes; the argument is <see langword="null"/> when nothing is selected.
        /// </summary>
        public event Action<CurlNetworkSnapshot?>? SelectedSnapshotChanged;

        /// <summary>
        /// Raised when a row is right-clicked.
        /// </summary>
        public event Action<CurlNetworkSnapshot>? SnapshotContextClicked;

        /// <summary>
        /// Gets or sets the text filter; see <see cref="CurlNetworkFormatter.MatchesFilter"/>. Applied by <see cref="TreeView.Reload"/>.
        /// </summary>
        public string Filter { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the state filter. Applied by <see cref="TreeView.Reload"/>.
        /// </summary>
        public CurlNetworkStateFilter StateFilter { get; set; }

        /// <summary>
        /// Gets the requests that pass the filters, in display order.
        /// </summary>
        public IReadOnlyList<CurlNetworkSnapshot> VisibleSnapshots
        {
            get => _visible;
        }

        /// <summary>
        /// Gets whether a visible request has not finished, so the table must keep repainting.
        /// </summary>
        public bool HasPendingRows { get; private set; }

        /// <summary>
        /// Gets the selected request, or <see langword="null"/>.
        /// </summary>
        public CurlNetworkSnapshot? SelectedSnapshot
        {
            get
            {
                var selection = GetSelection();
                return selection.Count > 0 && _byItemId.TryGetValue(selection[0], out var snapshot) ? snapshot : null;
            }
        }

        /// <summary>
        /// Creates the default column layout.
        /// </summary>
        /// <returns>The column state with every column visible.</returns>
        public static MultiColumnHeaderState CreateHeaderState()
        {
            var columns = new[]
            {
                CreateColumn("Name", 170, "The last path segment and query of the URL."),
                CreateColumn("URL", 260, "The requested URL."),
                CreateColumn("Method", 60, "The HTTP method."),
                CreateColumn("Status", 70, "The HTTP status code, or the stage of a request that has none yet."),
                CreateColumn("Protocol", 65, "The protocol of the final response."),
                CreateColumn("Remote Address", 130, "The address and port of the connection."),
                CreateColumn("Type", 60, "The media subtype of the response Content-Type."),
                CreateColumn("Size", 75, "Received response header and body bytes; bodies are counted after content decoding."),
                CreateColumn("Time", 75, "Total time from queueing until the transfer finished; it grows while the request is pending."),
                CreateColumn("TTFB", 70, "Time to first byte: from the start of the transfer (after queueing) until the first response byte."),
                CreateColumn("Transfer", 75, "Content download time: from the first response byte until the transfer finished."),
                CreateColumn("Waterfall", 220, "Queueing (gray), waiting for the first byte (green) and content download (blue) on a common time line."),
            };

            return new MultiColumnHeaderState(columns);
        }

        /// <summary>
        /// Gets the tree item id of a record.
        /// </summary>
        /// <param name="recordId">The record id.</param>
        /// <returns>The item id.</returns>
        public static int GetItemId(long recordId)
        {
            return unchecked((int)recordId);
        }

        /// <summary>
        /// Replaces the requests and rebuilds the rows.
        /// </summary>
        /// <param name="snapshots">Every request, oldest first.</param>
        public void SetSnapshots(IReadOnlyList<CurlNetworkSnapshot> snapshots)
        {
            _snapshots = snapshots;
            Reload();
        }

        /// <summary>
        /// Measures the waterfall time line at the current time; call it once before drawing.
        /// </summary>
        public void UpdateTimeline()
        {
            _now = Stopwatch.GetTimestamp();
            _timelineStart = long.MaxValue;
            foreach (var snapshot in _visible)
            {
                _timelineStart = Math.Min(_timelineStart, snapshot.QueuedTimestamp);
            }
            _timelineLength = TimeSpan.Zero;
            foreach (var snapshot in _visible)
            {
                var end = CurlNetworkSnapshot.ToTimeSpan(snapshot.QueuedTimestamp - _timelineStart) + snapshot.GetTotal(_now);
                if (end > _timelineLength)
                {
                    _timelineLength = end;
                }
            }
        }

        /// <summary>
        /// Builds the rows from the requests that pass the filters, sorted by the sorted column.
        /// </summary>
        /// <returns>The root item.</returns>
        protected override TreeViewItem BuildRoot()
        {
            var root = new TreeViewItem { id = -1, depth = -1, children = new List<TreeViewItem>() };
            _visible.Clear();
            _byItemId.Clear();
            HasPendingRows = false;
            foreach (var snapshot in _snapshots)
            {
                if (MatchesStateFilter(snapshot) && CurlNetworkFormatter.MatchesFilter(snapshot, Filter))
                {
                    _visible.Add(snapshot);
                }
            }
            SortVisible();
            foreach (var snapshot in _visible)
            {
                var id = GetItemId(snapshot.Id);
                _byItemId[id] = snapshot;
                HasPendingRows |= !snapshot.IsFinished;
                root.AddChild(new TreeViewItem { id = id, depth = 0, displayName = snapshot.Name });
            }
            UpdateTimeline();

            return root;
        }

        /// <summary>
        /// Allows selecting one request at a time.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <returns>Always <see langword="false"/>.</returns>
        protected override bool CanMultiSelect(TreeViewItem item)
        {
            return false;
        }

        /// <summary>
        /// Raises <see cref="SelectedSnapshotChanged"/>.
        /// </summary>
        /// <param name="selectedIds">The selected item ids.</param>
        protected override void SelectionChanged(IList<int> selectedIds)
        {
            SelectedSnapshotChanged?.Invoke(SelectedSnapshot);
        }

        /// <summary>
        /// Raises <see cref="SnapshotContextClicked"/>.
        /// </summary>
        /// <param name="id">The right-clicked item id.</param>
        protected override void ContextClickedItem(int id)
        {
            if (_byItemId.TryGetValue(id, out var snapshot))
            {
                SnapshotContextClicked?.Invoke(snapshot);
            }
        }

        /// <summary>
        /// Draws the cells of a row.
        /// </summary>
        /// <param name="args">The row to draw.</param>
        protected override void RowGUI(RowGUIArgs args)
        {
            if (!_byItemId.TryGetValue(args.item.id, out var snapshot))
            {
                return;
            }
            EnsureStyles();
            var textColor = GetTextColor(snapshot);
            for (var i = 0; i < args.GetNumVisibleColumns(); i++)
            {
                var column = (CurlNetworkColumn)args.GetColumn(i);
                var rect = args.GetCellRect(i);
                if (column == CurlNetworkColumn.Waterfall)
                {
                    DrawWaterfall(rect, snapshot);
                    continue;
                }
                CenterRectUsingSingleLineHeight(ref rect);
                var text = GetCellText(snapshot, column, _now);
                var tooltip = column == CurlNetworkColumn.Name ? snapshot.Url : text;
                var style = IsNumeric(column) ? _numberStyle! : _cellStyle!;
                var previousColor = style.normal.textColor;
                style.normal.textColor = textColor;
                GUI.Label(rect, new GUIContent(text, tooltip), style);
                style.normal.textColor = previousColor;
            }
        }

        /// <summary>
        /// Gets the text of a cell.
        /// </summary>
        /// <param name="snapshot">The request.</param>
        /// <param name="column">The column.</param>
        /// <param name="now">The current <see cref="Stopwatch"/> timestamp.</param>
        /// <returns>The cell text; empty for the waterfall.</returns>
        internal static string GetCellText(CurlNetworkSnapshot snapshot, CurlNetworkColumn column, long now)
        {
            switch (column)
            {
                case CurlNetworkColumn.Name:
                    return snapshot.Name;
                case CurlNetworkColumn.Url:
                    return snapshot.Url;
                case CurlNetworkColumn.Method:
                    return snapshot.Method;
                case CurlNetworkColumn.Status:
                    return CurlNetworkFormatter.FormatStatus(snapshot);
                case CurlNetworkColumn.Protocol:
                    return snapshot.Protocol ?? string.Empty;
                case CurlNetworkColumn.RemoteAddress:
                    return snapshot.RemoteAddress ?? string.Empty;
                case CurlNetworkColumn.Type:
                    return CurlNetworkFormatter.GetShortType(snapshot.ContentType);
                case CurlNetworkColumn.Size:
                    return CurlNetworkFormatter.FormatSize(snapshot.TransferredSize);
                case CurlNetworkColumn.Time:
                    return CurlNetworkFormatter.FormatDuration(snapshot.GetTotal(now));
                case CurlNetworkColumn.Ttfb:
                    return CurlNetworkFormatter.FormatDuration(snapshot.Ttfb);
                case CurlNetworkColumn.Transfer:
                    return CurlNetworkFormatter.FormatDuration(snapshot.GetTransfer(now));
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Creates a sortable, hideable column.
        /// </summary>
        /// <param name="name">The header text.</param>
        /// <param name="width">The initial width.</param>
        /// <param name="tooltip">The header tooltip.</param>
        /// <returns>The column.</returns>
        private static MultiColumnHeaderState.Column CreateColumn(string name, float width, string tooltip)
        {
            return new MultiColumnHeaderState.Column
            {
                headerContent = new GUIContent(name, tooltip),
                width = width,
                minWidth = 40,
                autoResize = false,
                canSort = true,
                allowToggleVisibility = true,
            };
        }

        /// <summary>
        /// Gets whether a column holds numbers that are right-aligned.
        /// </summary>
        /// <param name="column">The column.</param>
        /// <returns><see langword="true"/> for size and duration columns.</returns>
        private static bool IsNumeric(CurlNetworkColumn column)
        {
            return column == CurlNetworkColumn.Size
                || column == CurlNetworkColumn.Time
                || column == CurlNetworkColumn.Ttfb
                || column == CurlNetworkColumn.Transfer;
        }

        /// <summary>
        /// Gets the sort key of a request for a column.
        /// </summary>
        /// <param name="snapshot">The request.</param>
        /// <param name="column">The sorted column.</param>
        /// <param name="now">The <see cref="Stopwatch"/> timestamp every key is computed at, so keys stay consistent.</param>
        /// <returns>The key.</returns>
        private static IComparable GetSortKey(CurlNetworkSnapshot snapshot, CurlNetworkColumn column, long now)
        {
            switch (column)
            {
                case CurlNetworkColumn.Status:
                    return (snapshot.StatusCode * 10L) + (long)snapshot.State;
                case CurlNetworkColumn.Size:
                    return snapshot.TransferredSize;
                case CurlNetworkColumn.Time:
                    return snapshot.GetTotal(now);
                case CurlNetworkColumn.Ttfb:
                    return snapshot.Ttfb ?? TimeSpan.MaxValue;
                case CurlNetworkColumn.Transfer:
                    return snapshot.GetTransfer(now) ?? TimeSpan.MaxValue;
                case CurlNetworkColumn.Waterfall:
                    return snapshot.QueuedTimestamp;
                default:
                    return GetCellText(snapshot, column, now).ToLowerInvariant();
            }
        }

        /// <summary>
        /// Gets whether a request passes <see cref="StateFilter"/>.
        /// </summary>
        /// <param name="snapshot">The request.</param>
        /// <returns><see langword="true"/> when the request is listed.</returns>
        private bool MatchesStateFilter(CurlNetworkSnapshot snapshot)
        {
            switch (StateFilter)
            {
                case CurlNetworkStateFilter.Pending:
                    return !snapshot.IsFinished;
                case CurlNetworkStateFilter.Finished:
                    return snapshot.IsFinished;
                case CurlNetworkStateFilter.Errors:
                    return CurlNetworkFormatter.IsError(snapshot);
                default:
                    return true;
            }
        }

        /// <summary>
        /// Sorts the visible requests by the sorted column, keeping request order for equal keys and when nothing is sorted.
        /// </summary>
        private void SortVisible()
        {
            var columnIndex = multiColumnHeader.sortedColumnIndex;
            if (columnIndex < 0)
            {
                return;
            }
            var column = (CurlNetworkColumn)columnIndex;
            var ascending = multiColumnHeader.IsSortedAscending(columnIndex);
            var now = Stopwatch.GetTimestamp();
            var keys = new Dictionary<long, IComparable>(_visible.Count);
            foreach (var snapshot in _visible)
            {
                keys[snapshot.Id] = GetSortKey(snapshot, column, now);
            }
            _visible.Sort((left, right) =>
            {
                var result = keys[left.Id].CompareTo(keys[right.Id]);
                if (result == 0)
                {
                    return left.Id.CompareTo(right.Id);
                }
                return ascending ? result : -result;
            });
        }

        /// <summary>
        /// Rebuilds the rows in the new order.
        /// </summary>
        /// <param name="header">The header whose sorting changed.</param>
        private void OnSortingChanged(MultiColumnHeader header)
        {
            Reload();
        }

        /// <summary>
        /// Creates the cell styles.
        /// </summary>
        private void EnsureStyles()
        {
            _cellStyle ??= new GUIStyle(EditorStyles.label) { clipping = TextClipping.Clip };
            _numberStyle ??= new GUIStyle(_cellStyle) { alignment = TextAnchor.MiddleRight };
        }

        /// <summary>
        /// Gets the text color of a row: red for errors, dimmed for pending requests.
        /// </summary>
        /// <param name="snapshot">The request.</param>
        /// <returns>The color.</returns>
        private Color GetTextColor(CurlNetworkSnapshot snapshot)
        {
            var normal = _cellStyle!.normal.textColor;
            if (CurlNetworkFormatter.IsError(snapshot))
            {
                return s_errorColor;
            }

            return snapshot.IsFinished ? normal : new Color(normal.r, normal.g, normal.b, normal.a * 0.65f);
        }

        /// <summary>
        /// Draws the waterfall bar of a request: queueing, waiting for the first byte and content download.
        /// </summary>
        /// <param name="rect">The cell.</param>
        /// <param name="snapshot">The request.</param>
        private void DrawWaterfall(Rect rect, CurlNetworkSnapshot snapshot)
        {
            if (_timelineLength <= TimeSpan.Zero || rect.width <= 4)
            {
                return;
            }
            var area = new Rect(rect.x + 2, rect.y + 5, rect.width - 4, rect.height - 10);
            var scale = area.width / _timelineLength.TotalMilliseconds;
            var start = CurlNetworkSnapshot.ToTimeSpan(snapshot.QueuedTimestamp - _timelineStart);
            var queueing = snapshot.GetQueueing(_now);
            var total = snapshot.GetTotal(_now);
            var ttfb = snapshot.Ttfb;
            var transfer = snapshot.GetTransfer(_now);
            var waiting = ttfb ?? (total - queueing);
            var x = area.x + (float)(start.TotalMilliseconds * scale);
            x = DrawSegment(area, x, queueing, scale, s_queueColor);
            x = DrawSegment(area, x, waiting, scale, s_waitColor);
            if (transfer is { } download)
            {
                DrawSegment(area, x, download, scale, s_downloadColor);
            }
            GUI.Label(rect, new GUIContent(string.Empty, GetWaterfallTooltip(queueing, ttfb, transfer, total)));
        }

        /// <summary>
        /// Draws one segment of a waterfall bar, at least one pixel wide.
        /// </summary>
        /// <param name="area">The bar area.</param>
        /// <param name="x">The left edge of the segment.</param>
        /// <param name="duration">The segment duration.</param>
        /// <param name="scale">Pixels per millisecond.</param>
        /// <param name="color">The segment color.</param>
        /// <returns>The right edge of the segment.</returns>
        private static float DrawSegment(Rect area, float x, TimeSpan duration, double scale, Color color)
        {
            if (duration <= TimeSpan.Zero)
            {
                return x;
            }
            var width = Math.Max(1f, (float)(duration.TotalMilliseconds * scale));
            width = Math.Min(width, Math.Max(0f, area.xMax - x));
            EditorGUI.DrawRect(new Rect(x, area.y, width, area.height), color);

            return x + width;
        }

        /// <summary>
        /// Describes the parts of a waterfall bar.
        /// </summary>
        /// <param name="queueing">The queueing time.</param>
        /// <param name="ttfb">The time to first byte, or <see langword="null"/>.</param>
        /// <param name="transfer">The content download time, or <see langword="null"/>.</param>
        /// <param name="total">The total time.</param>
        /// <returns>The tooltip text.</returns>
        private static string GetWaterfallTooltip(TimeSpan queueing, TimeSpan? ttfb, TimeSpan? transfer, TimeSpan total)
        {
            var builder = new StringBuilder();
            builder.Append("Queueing: ").Append(CurlNetworkFormatter.FormatDuration(queueing)).Append('\n');
            builder.Append("TTFB: ").Append(CurlNetworkFormatter.FormatDuration(ttfb)).Append('\n');
            builder.Append("Content Download: ").Append(CurlNetworkFormatter.FormatDuration(transfer)).Append('\n');
            builder.Append("Total: ").Append(CurlNetworkFormatter.FormatDuration(total));

            return builder.ToString();
        }
    }
}
