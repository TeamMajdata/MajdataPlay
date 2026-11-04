#nullable enable
using MajdataPlay.Net.Curl.Diagnostics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace MajdataPlay.Net.Editor
{
    /// <summary>
    /// The request details panel of the Curl Debugger's Network tab, with Headers, Payload, Response, Cookies and Timing tabs.
    /// </summary>
    /// <remarks>Serialized with the window, so the chosen tab, modes and folded sections survive domain reloads.</remarks>
    [Serializable]
    internal sealed class CurlNetworkDetailsView : IDisposable
    {
        /// <summary>
        /// The width of the name column of the Timing tab.
        /// </summary>
        private const float PhaseNameWidth = 190f;

        /// <summary>
        /// The width of the duration column of the Timing tab.
        /// </summary>
        private const float PhaseDurationWidth = 80f;

        /// <summary>
        /// The tab names, in <see cref="CurlNetworkDetailsTab"/> order.
        /// </summary>
        private static readonly string[] s_tabNames = { "Headers", "Payload", "Response", "Cookies", "Timing" };

        /// <summary>
        /// The body modes offered for text bodies.
        /// </summary>
        private static readonly string[] s_textModeNames = { "Pretty", "Raw", "Hex" };

        /// <summary>
        /// The body modes offered for image bodies.
        /// </summary>
        private static readonly string[] s_imageModeNames = { "Pretty", "Raw", "Hex", "Preview" };

        /// <summary>
        /// The bar color of every timing phase.
        /// </summary>
        private static readonly Dictionary<string, Color> s_phaseColors = new()
        {
            ["Queueing"] = new Color(0.62f, 0.62f, 0.62f, 1f),
            ["DNS Lookup"] = new Color(0.20f, 0.66f, 0.66f, 1f),
            ["Initial connection"] = new Color(0.95f, 0.60f, 0.20f, 1f),
            ["SSL"] = new Color(0.70f, 0.45f, 0.90f, 1f),
            ["Request setup"] = new Color(0.55f, 0.55f, 0.70f, 1f),
            ["Request sent"] = new Color(0.45f, 0.60f, 0.80f, 1f),
            ["Waiting for server response"] = new Color(0.30f, 0.72f, 0.40f, 1f),
            ["Content Download"] = new Color(0.30f, 0.56f, 0.95f, 1f),
        };

        /// <summary>
        /// The selected tab.
        /// </summary>
        [SerializeField]
        private CurlNetworkDetailsTab _tab;

        /// <summary>
        /// How the request body is shown.
        /// </summary>
        [SerializeField]
        private CurlNetworkBodyMode _payloadMode;

        /// <summary>
        /// How the response body is shown.
        /// </summary>
        [SerializeField]
        private CurlNetworkBodyMode _responseMode;

        /// <summary>
        /// Whether the General section is expanded.
        /// </summary>
        [SerializeField]
        private bool _isGeneralExpanded = true;

        /// <summary>
        /// Whether the Response Headers section is expanded.
        /// </summary>
        [SerializeField]
        private bool _isResponseHeadersExpanded = true;

        /// <summary>
        /// Whether the Request Headers section is expanded.
        /// </summary>
        [SerializeField]
        private bool _isRequestHeadersExpanded = true;

        /// <summary>
        /// Whether the Query String Parameters section is expanded.
        /// </summary>
        [SerializeField]
        private bool _isQueryExpanded = true;

        /// <summary>
        /// Whether the response headers are shown as they appear on the wire.
        /// </summary>
        [SerializeField]
        private bool _showRawResponseHeaders;

        /// <summary>
        /// Whether the request headers are shown as they appear on the wire.
        /// </summary>
        [SerializeField]
        private bool _showRawRequestHeaders;

        /// <summary>
        /// The inspected record.
        /// </summary>
        [NonSerialized]
        private CurlNetworkRecord? _record;

        /// <summary>
        /// The latest snapshot of <see cref="_record"/>.
        /// </summary>
        [NonSerialized]
        private CurlNetworkSnapshot? _snapshot;

        /// <summary>
        /// The request body panel, created on first use.
        /// </summary>
        [NonSerialized]
        private CurlNetworkBodyPanel? _requestBody;

        /// <summary>
        /// The response body panel, created on first use.
        /// </summary>
        [NonSerialized]
        private CurlNetworkBodyPanel? _responseBody;

        /// <summary>
        /// The scroll position of the scrolling tabs.
        /// </summary>
        [NonSerialized]
        private Vector2 _scroll;

        /// <summary>
        /// The scroll position of the query parameters.
        /// </summary>
        [NonSerialized]
        private Vector2 _queryScroll;

        /// <summary>
        /// The width available to the content of the current frame.
        /// </summary>
        [NonSerialized]
        private float _viewWidth;

        /// <summary>
        /// Raised when the close button is clicked.
        /// </summary>
        public event Action? CloseRequested;

        /// <summary>
        /// Gets or sets the selected tab.
        /// </summary>
        public CurlNetworkDetailsTab Tab
        {
            get => _tab;
            set => _tab = value;
        }

        /// <summary>
        /// Gets the inspected record, or <see langword="null"/>.
        /// </summary>
        public CurlNetworkRecord? Record
        {
            get => _record;
        }

        /// <summary>
        /// Gets the snapshot being shown, or <see langword="null"/>.
        /// </summary>
        public CurlNetworkSnapshot? Snapshot
        {
            get => _snapshot;
        }

        /// <summary>
        /// Gets the request body panel.
        /// </summary>
        private CurlNetworkBodyPanel RequestBody
        {
            get => _requestBody ??= new CurlNetworkBodyPanel();
        }

        /// <summary>
        /// Gets the response body panel.
        /// </summary>
        private CurlNetworkBodyPanel ResponseBody
        {
            get => _responseBody ??= new CurlNetworkBodyPanel();
        }

        /// <summary>
        /// Shows a record, or nothing, and takes its latest snapshot.
        /// </summary>
        /// <param name="record">The record to inspect, or <see langword="null"/>.</param>
        public void SetRecord(CurlNetworkRecord? record)
        {
            if (!ReferenceEquals(record, _record))
            {
                _scroll = Vector2.zero;
                _queryScroll = Vector2.zero;
            }
            _record = record;
            _snapshot = record?.GetSnapshot();
            if (record is null)
            {
                RequestBody.Clear();
                ResponseBody.Clear();
            }
        }

        /// <summary>
        /// Draws the panel.
        /// </summary>
        /// <param name="rect">The area of the panel.</param>
        public void OnGUI(Rect rect)
        {
            _viewWidth = Math.Max(100f, rect.width - 24f);
            GUILayout.BeginArea(rect);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _tab = (CurlNetworkDetailsTab)GUILayout.Toolbar((int)_tab, s_tabNames, EditorStyles.toolbarButton);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("×", "Close the details panel."), EditorStyles.toolbarButton, GUILayout.Width(24)))
                {
                    CloseRequested?.Invoke();
                }
            }
            if (_record is null || _snapshot is null)
            {
                EditorGUILayout.HelpBox("Select a request to inspect it.", MessageType.Info);
            }
            else
            {
                DrawTab(_record, _snapshot);
            }
            GUILayout.EndArea();
        }

        /// <summary>
        /// Destroys the preview textures.
        /// </summary>
        public void Dispose()
        {
            _requestBody?.Dispose();
            _responseBody?.Dispose();
        }

        /// <summary>
        /// Gets the rows of the General section.
        /// </summary>
        /// <param name="snapshot">The request.</param>
        /// <returns>The URL, method, status, state, error, remote address, protocol, start time and redirects.</returns>
        internal static List<KeyValuePair<string, string>> GetGeneralRows(CurlNetworkSnapshot snapshot)
        {
            var rows = new List<KeyValuePair<string, string>>
            {
                new("Request URL", snapshot.Url),
            };
            if (!string.IsNullOrEmpty(snapshot.EffectiveUrl) && snapshot.EffectiveUrl != snapshot.Url)
            {
                rows.Add(new("Effective URL", snapshot.EffectiveUrl!));
            }
            rows.Add(new("Request Method", snapshot.Method));
            rows.Add(new("Status Code", CurlNetworkFormatter.FormatStatusWithReason(snapshot)));
            rows.Add(new("State", snapshot.State.ToString()));
            if (!string.IsNullOrEmpty(snapshot.Error))
            {
                rows.Add(new("Error", snapshot.Error!));
            }
            rows.Add(new("Remote Address", snapshot.RemoteAddress ?? "-"));
            rows.Add(new("Protocol", snapshot.Protocol ?? "-"));
            if (snapshot.ConnectionId is { } connectionId)
            {
                rows.Add(new("Connection", FormatConnection(connectionId, snapshot.IsConnectionReused)));
            }
            rows.Add(new("Started", snapshot.StartedAt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)));
            if (snapshot.RedirectCount > 0)
            {
                rows.Add(new("Redirects", snapshot.RedirectCount.ToString(CultureInfo.InvariantCulture)));
            }

            return rows;
        }

        /// <summary>
        /// Describes the connection a request used, as listed in the Info tab.
        /// </summary>
        /// <param name="connectionId">libcurl's connection identifier.</param>
        /// <param name="isReused">Whether the connection was reused from the pool, or <see langword="null"/> when unknown.</param>
        /// <returns>Such as <c>#3 (reused)</c>, <c>#4 (new)</c> or <c>#5</c>.</returns>
        internal static string FormatConnection(long connectionId, bool? isReused)
        {
            var id = "#" + connectionId.ToString(CultureInfo.InvariantCulture);
            switch (isReused)
            {
                case true:
                    return id + " (reused)";
                case false:
                    return id + " (new)";
                default:
                    return id;
            }
        }

        /// <summary>
        /// Describes the size of a body and how much of it was kept.
        /// </summary>
        /// <param name="totalBytes">The body size.</param>
        /// <param name="capturedBytes">The number of bytes kept.</param>
        /// <param name="isTruncated">Whether some bytes were not kept.</param>
        /// <returns>Such as <c>2.0 KiB</c>, <c>5.0 MiB (first 1.0 MiB kept)</c> or <c>1.2 KiB (not kept)</c>.</returns>
        internal static string GetBodyInfo(long totalBytes, int capturedBytes, bool isTruncated)
        {
            var size = CurlNetworkFormatter.FormatSize(totalBytes);
            if (!isTruncated)
            {
                return size;
            }
            if (capturedBytes == 0)
            {
                return $"{size} (not kept)";
            }

            return $"{size} (first {CurlNetworkFormatter.FormatSize(capturedBytes)} kept)";
        }

        /// <summary>
        /// Draws the selected tab.
        /// </summary>
        /// <param name="record">The inspected record.</param>
        /// <param name="snapshot">Its snapshot.</param>
        private void DrawTab(CurlNetworkRecord record, CurlNetworkSnapshot snapshot)
        {
            switch (_tab)
            {
                case CurlNetworkDetailsTab.Payload:
                    DrawPayload(record, snapshot);
                    break;
                case CurlNetworkDetailsTab.Response:
                    DrawResponse(record, snapshot);
                    break;
                case CurlNetworkDetailsTab.Cookies:
                    DrawCookies(snapshot);
                    break;
                case CurlNetworkDetailsTab.Timing:
                    DrawTiming(snapshot);
                    break;
                default:
                    DrawHeaders(snapshot);
                    break;
            }
        }

        /// <summary>
        /// Draws the Headers tab.
        /// </summary>
        /// <param name="snapshot">The request.</param>
        private void DrawHeaders(CurlNetworkSnapshot snapshot)
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            _isGeneralExpanded = CurlEditorGUI.DrawSection(_isGeneralExpanded, "General");
            if (_isGeneralExpanded)
            {
                CurlEditorGUI.DrawKeyValues(GetGeneralRows(snapshot), _viewWidth);
            }
            DrawHeaderSection("Response Headers", snapshot.StatusLine, snapshot.ResponseHeaders, ref _isResponseHeadersExpanded, ref _showRawResponseHeaders);
            DrawHeaderSection("Request Headers", snapshot.RequestLine, snapshot.RequestHeaders, ref _isRequestHeadersExpanded, ref _showRawRequestHeaders);
            if (_isRequestHeadersExpanded && snapshot.AreRequestHeadersProvisional)
            {
                EditorGUILayout.HelpBox("Provisional headers are shown: the request has not been sent yet, so headers libcurl adds itself (Host, Accept, Content-Length...) are missing.", MessageType.Warning);
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Draws a collapsible header section with a toggle between parsed and raw headers.
        /// </summary>
        /// <param name="title">The section title.</param>
        /// <param name="firstLine">The request or status line shown in raw mode.</param>
        /// <param name="headers">The headers.</param>
        /// <param name="isExpanded">Whether the section is expanded.</param>
        /// <param name="showRaw">Whether the headers are shown as they appear on the wire.</param>
        private void DrawHeaderSection(string title, string? firstLine, IReadOnlyList<KeyValuePair<string, string>> headers, ref bool isExpanded, ref bool showRaw)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                isExpanded = CurlEditorGUI.DrawSection(isExpanded, $"{title} ({headers.Count})");
                GUILayout.FlexibleSpace();
                showRaw = GUILayout.Toggle(showRaw, "Raw", EditorStyles.miniButton, GUILayout.Width(44));
            }
            if (!isExpanded)
            {
                return;
            }
            if (showRaw)
            {
                CurlEditorGUI.DrawSelectableBlock(CurlNetworkFormatter.FormatHeaderBlock(firstLine, headers), _viewWidth);
            }
            else
            {
                CurlEditorGUI.DrawKeyValues(headers, _viewWidth);
            }
        }

        /// <summary>
        /// Draws the Payload tab: the query parameters and the request body.
        /// </summary>
        /// <param name="record">The inspected record.</param>
        /// <param name="snapshot">The request.</param>
        private void DrawPayload(CurlNetworkRecord record, CurlNetworkSnapshot snapshot)
        {
            var query = CurlNetworkFormatter.ParseQuery(snapshot.Url);
            if (query.Count > 0)
            {
                _isQueryExpanded = CurlEditorGUI.DrawSection(_isQueryExpanded, $"Query String Parameters ({query.Count})");
                if (_isQueryExpanded)
                {
                    _queryScroll = EditorGUILayout.BeginScrollView(_queryScroll, GUILayout.MaxHeight(160));
                    CurlEditorGUI.DrawKeyValues(query, _viewWidth);
                    EditorGUILayout.EndScrollView();
                }
            }
            if (snapshot.RequestBodySize == 0)
            {
                if (query.Count == 0)
                {
                    EditorGUILayout.HelpBox("This request has no payload.", MessageType.Info);
                }
                return;
            }
            var contentType = FindHeader(snapshot.RequestHeaders, "Content-Type");
            RequestBody.Update(snapshot.Id, snapshot.Version, record.GetRequestBody, contentType);
            var info = GetBodyInfo(snapshot.RequestBodySize, snapshot.CapturedRequestBodySize, snapshot.IsRequestBodyTruncated);
            _payloadMode = DrawBodyToolbar("Request Payload", _payloadMode, RequestBody, info);
            if (_payloadMode == CurlNetworkBodyMode.Pretty && RequestBody.IsForm)
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                CurlEditorGUI.DrawKeyValues(RequestBody.GetFormFields(), _viewWidth);
                EditorGUILayout.EndScrollView();
                return;
            }
            RequestBody.OnGUI(GetRemainingRect(), _payloadMode);
        }

        /// <summary>
        /// Draws the Response tab.
        /// </summary>
        /// <param name="record">The inspected record.</param>
        /// <param name="snapshot">The request.</param>
        private void DrawResponse(CurlNetworkRecord record, CurlNetworkSnapshot snapshot)
        {
            if (snapshot.ResponseBodySize == 0)
            {
                var message = snapshot.IsFinished ? "This request has no response data available." : "Waiting for response data...";
                EditorGUILayout.HelpBox(message, MessageType.Info);
                return;
            }
            ResponseBody.Update(snapshot.Id, snapshot.Version, record.GetResponseBody, snapshot.ContentType);
            var info = GetBodyInfo(snapshot.ResponseBodySize, snapshot.CapturedResponseBodySize, snapshot.IsResponseBodyTruncated);
            _responseMode = DrawBodyToolbar("Response", _responseMode, ResponseBody, info);
            if (ResponseBody.Data.Length == 0)
            {
                EditorGUILayout.HelpBox("The body was not kept: the capture limit or the shared capture budget was reached.", MessageType.Info);
                return;
            }
            ResponseBody.OnGUI(GetRemainingRect(), _responseMode);
        }

        /// <summary>
        /// Draws the toolbar above a body: its title, the display modes, its size and a copy button.
        /// </summary>
        /// <param name="title">The body title.</param>
        /// <param name="mode">The current display mode.</param>
        /// <param name="panel">The body panel.</param>
        /// <param name="info">The size description.</param>
        /// <returns>The display mode chosen.</returns>
        private CurlNetworkBodyMode DrawBodyToolbar(string title, CurlNetworkBodyMode mode, CurlNetworkBodyPanel panel, string info)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(title, EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
                var names = panel.CanPreview ? s_imageModeNames : s_textModeNames;
                var index = Math.Min((int)mode, names.Length - 1);
                mode = (CurlNetworkBodyMode)GUILayout.Toolbar(index, names, EditorStyles.toolbarButton);
                GUILayout.FlexibleSpace();
                GUILayout.Label(info, EditorStyles.miniLabel);
                if (GUILayout.Button(new GUIContent("Copy", "Copy the body as shown."), EditorStyles.toolbarButton))
                {
                    EditorGUIUtility.systemCopyBuffer = panel.GetText(mode);
                }
            }

            return mode;
        }

        /// <summary>
        /// Draws the Cookies tab.
        /// </summary>
        /// <param name="snapshot">The request.</param>
        private void DrawCookies(CurlNetworkSnapshot snapshot)
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            var requestCookies = CurlNetworkFormatter.ParseRequestCookies(snapshot.RequestCookies);
            EditorGUILayout.LabelField($"Request Cookies ({requestCookies.Count})", EditorStyles.boldLabel);
            if (requestCookies.Count == 0)
            {
                EditorGUILayout.LabelField("This request sent no cookies.", EditorStyles.miniLabel);
            }
            else
            {
                var rows = new List<string[]>(requestCookies.Count);
                foreach (var cookie in requestCookies)
                {
                    rows.Add(new[] { cookie.Name, cookie.Value });
                }
                CurlEditorGUI.DrawTable(new[] { "Name", "Value" }, new[] { 0.3f, 0.7f }, rows, _viewWidth);
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Response Cookies ({snapshot.ResponseCookies.Count})", EditorStyles.boldLabel);
            if (snapshot.ResponseCookies.Count == 0)
            {
                EditorGUILayout.LabelField("This response set no cookies.", EditorStyles.miniLabel);
            }
            else
            {
                var rows = new List<string[]>(snapshot.ResponseCookies.Count);
                foreach (var header in snapshot.ResponseCookies)
                {
                    var cookie = CurlNetworkFormatter.ParseSetCookie(header);
                    rows.Add(new[]
                    {
                        cookie.Name,
                        cookie.Value,
                        cookie.Domain ?? string.Empty,
                        cookie.Path ?? string.Empty,
                        cookie.MaxAge is null ? cookie.Expires ?? "Session" : $"Max-Age={cookie.MaxAge}",
                        cookie.HttpOnly ? "Yes" : string.Empty,
                        cookie.Secure ? "Yes" : string.Empty,
                        cookie.SameSite ?? string.Empty,
                    });
                }
                var headers = new[] { "Name", "Value", "Domain", "Path", "Expires / Max-Age", "HttpOnly", "Secure", "SameSite" };
                CurlEditorGUI.DrawTable(headers, new[] { 0.14f, 0.26f, 0.13f, 0.08f, 0.17f, 0.07f, 0.06f, 0.09f }, rows, _viewWidth);
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Draws the Timing tab: one bar per phase on the request's own time line, and the summary durations.
        /// </summary>
        /// <param name="snapshot">The request.</param>
        private void DrawTiming(CurlNetworkSnapshot snapshot)
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            var now = Stopwatch.GetTimestamp();
            var total = snapshot.GetTotal(now);
            EditorGUILayout.LabelField("Queued at", snapshot.StartedAt.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
            EditorGUILayout.Space();
            foreach (var phase in CurlNetworkFormatter.GetTimingPhases(snapshot, now))
            {
                DrawPhase(phase, total);
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(new GUIContent("TTFB", "From the start of the transfer (after queueing) until the first response byte."), new GUIContent(CurlNetworkFormatter.FormatDuration(snapshot.Ttfb)));
            EditorGUILayout.LabelField(new GUIContent("Content Download", "From the first response byte until the transfer finished."), new GUIContent(CurlNetworkFormatter.FormatDuration(snapshot.GetTransfer(now))));
            EditorGUILayout.LabelField(new GUIContent("Total", "Queueing + TTFB + Content Download."), new GUIContent(CurlNetworkFormatter.FormatDuration(total)));
            if (snapshot.RedirectCount > 0 && snapshot.Timings is { } timings)
            {
                EditorGUILayout.LabelField("Redirects", $"{snapshot.RedirectCount} ({CurlNetworkFormatter.FormatDuration(timings.Redirect)})");
            }
            if (!snapshot.IsFinished)
            {
                EditorGUILayout.HelpBox("The request has not finished; the durations keep growing.", MessageType.Info);
            }
            EditorGUILayout.HelpBox(
                "Phases after queueing come from libcurl and are measured from the start of the transfer. " +
                "When redirects are followed, libcurl adds the phases of every hop together.",
                MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Draws one timing phase: its name, its bar and its duration.
        /// </summary>
        /// <param name="phase">The phase.</param>
        /// <param name="total">The total time of the request, which spans the bar area.</param>
        private void DrawPhase(CurlNetworkTimingPhase phase, TimeSpan total)
        {
            var row = GUILayoutUtility.GetRect(_viewWidth, EditorGUIUtility.singleLineHeight + 2, GUILayout.ExpandWidth(true));
            GUI.Label(new Rect(row.x, row.y, PhaseNameWidth, row.height), phase.Name);
            var bar = new Rect(row.x + PhaseNameWidth, row.y + 4, Math.Max(0, row.width - PhaseNameWidth - PhaseDurationWidth - 8), row.height - 8);
            if (total > TimeSpan.Zero && bar.width > 0)
            {
                var scale = bar.width / total.TotalMilliseconds;
                var x = bar.x + (float)(phase.Start.TotalMilliseconds * scale);
                var width = Math.Max(1f, (float)(phase.Duration.TotalMilliseconds * scale));
                var color = s_phaseColors.TryGetValue(phase.Name, out var phaseColor) ? phaseColor : Color.gray;
                EditorGUI.DrawRect(new Rect(x, bar.y, Math.Min(width, Math.Max(1f, bar.xMax - x)), bar.height), color);
            }
            var durationRect = new Rect(row.xMax - PhaseDurationWidth, row.y, PhaseDurationWidth, row.height);
            GUI.Label(durationRect, CurlNetworkFormatter.FormatDuration(phase.Duration), CurlEditorGUI.CellStyle);
        }

        /// <summary>
        /// Takes the rest of the panel.
        /// </summary>
        /// <returns>The remaining area.</returns>
        private static Rect GetRemainingRect()
        {
            return GUILayoutUtility.GetRect(0, 100000, 0, 100000, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        }

        /// <summary>
        /// Finds the value of the last header with the given name.
        /// </summary>
        /// <param name="headers">The headers.</param>
        /// <param name="name">The header name, compared case-insensitively.</param>
        /// <returns>The value, or <see langword="null"/>.</returns>
        private static string? FindHeader(IReadOnlyList<KeyValuePair<string, string>> headers, string name)
        {
            for (var i = headers.Count - 1; i >= 0; i--)
            {
                if (string.Equals(headers[i].Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return headers[i].Value;
                }
            }

            return null;
        }
    }
}
