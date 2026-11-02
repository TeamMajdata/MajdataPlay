#nullable enable
using MajdataPlay.Net.Curl.Diagnostics;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace MajdataPlay.Net.Editor
{
    /// <summary>
    /// The Info tab of the Curl Debugger: what libcurl reports about itself, the request counters of every
    /// <see cref="Curl.CurlHttpMessageHandler"/> and their connection pools.
    /// </summary>
    /// <remarks>Serialized with the window, so the folded sections survive domain reloads.</remarks>
    [Serializable]
    internal sealed class CurlInfoPanel
    {
        /// <summary>
        /// The column headings of the handler table.
        /// </summary>
        private static readonly string[] s_handlerHeadings =
        {
            "#", "State", "Created", "Requests", "Queued", "Running", "Completed", "Failed", "Cancelled", "Open", "Idle", "Reused",
        };

        /// <summary>
        /// The column widths of the handler table, as shares of the panel width.
        /// </summary>
        private static readonly float[] s_handlerWeights =
        {
            0.04f, 0.09f, 0.10f, 0.09f, 0.08f, 0.08f, 0.10f, 0.08f, 0.09f, 0.07f, 0.07f, 0.08f,
        };

        /// <summary>
        /// The column headings of the connection table.
        /// </summary>
        private static readonly string[] s_connectionHeadings =
        {
            "Handler", "Connection", "Remote", "Local", "State", "Transfers", "Requests", "Opened", "Last used",
        };

        /// <summary>
        /// The column widths of the connection table, as shares of the panel width.
        /// </summary>
        private static readonly float[] s_connectionWeights =
        {
            0.08f, 0.10f, 0.17f, 0.17f, 0.08f, 0.09f, 0.09f, 0.10f, 0.11f,
        };

        /// <summary>
        /// Whether the libcurl section is expanded.
        /// </summary>
        [SerializeField]
        private bool _isLibraryExpanded = true;

        /// <summary>
        /// Whether the Requests section is expanded.
        /// </summary>
        [SerializeField]
        private bool _isRequestsExpanded = true;

        /// <summary>
        /// Whether the Connection Pool section is expanded.
        /// </summary>
        [SerializeField]
        private bool _isPoolExpanded = true;

        /// <summary>
        /// Whether the Handlers section is expanded.
        /// </summary>
        [SerializeField]
        private bool _isHandlersExpanded = true;

        /// <summary>
        /// Whether the Connections section is expanded.
        /// </summary>
        [SerializeField]
        private bool _isConnectionsExpanded = true;

        /// <summary>
        /// The scroll position.
        /// </summary>
        [NonSerialized]
        private Vector2 _scroll;

        /// <summary>
        /// What libcurl reports about itself, read once.
        /// </summary>
        [NonSerialized]
        private CurlLibraryInfo? _library;

        /// <summary>
        /// Why <see cref="_library"/> could not be read, or <see langword="null"/>.
        /// </summary>
        [NonSerialized]
        private string? _libraryError;

        /// <summary>
        /// The handler statistics of the last refresh, oldest handler first.
        /// </summary>
        [NonSerialized]
        private CurlHandlerStatisticsSnapshot[] _handlers = Array.Empty<CurlHandlerStatisticsSnapshot>();

        /// <summary>
        /// The libcurl global reference count of the last refresh.
        /// </summary>
        [NonSerialized]
        private int _libCurlReferences;

        /// <summary>
        /// The local time of the last refresh.
        /// </summary>
        [NonSerialized]
        private DateTime _refreshedAt;

        /// <summary>
        /// Gets the handler statistics of the last refresh, oldest handler first.
        /// </summary>
        public IReadOnlyList<CurlHandlerStatisticsSnapshot> Handlers
        {
            get => _handlers ?? Array.Empty<CurlHandlerStatisticsSnapshot>();
        }

        /// <summary>
        /// Gets what libcurl reports about itself, or <see langword="null"/> before the first refresh or when it could not be read.
        /// </summary>
        public CurlLibraryInfo? Library
        {
            get => _library;
        }

        /// <summary>
        /// Takes new snapshots of every handler and reads the library information once.
        /// </summary>
        public void Refresh()
        {
            var handlers = CurlStatistics.GetHandlers();
            _handlers = new CurlHandlerStatisticsSnapshot[handlers.Length];
            for (var i = 0; i < handlers.Length; i++)
            {
                _handlers[i] = handlers[i].GetSnapshot();
            }
            _libCurlReferences = CurlStatistics.LibCurlReferenceCount;
            _refreshedAt = DateTime.Now;
            if (_library is not null || _libraryError is not null)
            {
                return;
            }
            try
            {
                _library = CurlStatistics.GetLibraryInfo();
            }
            catch (Exception exception)
            {
                _libraryError = exception.Message;
            }
        }

        /// <summary>
        /// Draws the toolbar and the sections.
        /// </summary>
        /// <param name="rect">The area of the panel.</param>
        public void OnGUI(Rect rect)
        {
            var width = Math.Max(200f, rect.width - 24f);
            var handlers = Handlers;
            GUILayout.BeginArea(rect);
            DrawToolbar();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            var totals = GetTotals(handlers);
            _isLibraryExpanded = CurlEditorGUI.DrawSection(_isLibraryExpanded, "libcurl");
            if (_isLibraryExpanded)
            {
                if (_library is not null)
                {
                    CurlEditorGUI.DrawKeyValues(GetLibraryRows(_library, _libCurlReferences), width);
                }
                else
                {
                    EditorGUILayout.HelpBox(_libraryError ?? "Loading...", _libraryError is null ? MessageType.Info : MessageType.Error);
                }
            }
            _isRequestsExpanded = CurlEditorGUI.DrawSection(_isRequestsExpanded, "Requests");
            if (_isRequestsExpanded)
            {
                CurlEditorGUI.DrawKeyValues(GetRequestRows(totals), width);
            }
            _isPoolExpanded = CurlEditorGUI.DrawSection(_isPoolExpanded, "Connection Pool");
            if (_isPoolExpanded)
            {
                CurlEditorGUI.DrawKeyValues(GetPoolRows(totals, CurlStatistics.IsConnectionTrackingEnabled), width);
            }
            _isHandlersExpanded = CurlEditorGUI.DrawSection(_isHandlersExpanded, $"Handlers ({handlers.Count})");
            if (_isHandlersExpanded)
            {
                CurlEditorGUI.DrawTable(s_handlerHeadings, s_handlerWeights, GetHandlerRows(handlers), width);
            }
            _isConnectionsExpanded = CurlEditorGUI.DrawSection(_isConnectionsExpanded, $"Connections ({totals.OpenConnections})");
            if (_isConnectionsExpanded)
            {
                CurlEditorGUI.DrawTable(s_connectionHeadings, s_connectionWeights, GetConnectionRows(handlers, _refreshedAt), width);
            }
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Request counters cover every request. Connections are tracked only for requests sent while connection tracking is on: " +
                "libcurl reports the connection each request opens or reuses, and every connection it closes. An idle connection the " +
                "server closed stays listed until libcurl next looks for a connection to that host, as it stays in libcurl's own pool.",
                MessageType.None);
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>
        /// Adds up the statistics of every handler.
        /// </summary>
        /// <param name="handlers">The handler statistics.</param>
        /// <returns>The totals.</returns>
        internal static Totals GetTotals(IReadOnlyList<CurlHandlerStatisticsSnapshot> handlers)
        {
            var totals = new Totals();
            foreach (var handler in handlers)
            {
                if (handler.IsDisposed)
                {
                    totals.DisposedHandlers++;
                }
                else
                {
                    totals.AliveHandlers++;
                    totals.MaxConnectionsPerHost = Math.Max(totals.MaxConnectionsPerHost, handler.MaxConnectionsPerHost);
                }
                totals.TotalRequests += handler.TotalRequests;
                totals.QueuedRequests += handler.QueuedRequests;
                totals.RunningRequests += handler.RunningRequests;
                totals.CompletedRequests += handler.CompletedRequests;
                totals.FailedRequests += handler.FailedRequests;
                totals.CancelledRequests += handler.CancelledRequests;
                totals.OpenConnections += handler.OpenConnections;
                totals.IdleConnections += handler.IdleConnections;
                totals.ConnectionsOpened += handler.ConnectionsOpened;
                totals.ConnectionReuses += handler.ConnectionReuses;
                totals.ConnectionsClosed += handler.ConnectionsClosed;
            }

            return totals;
        }

        /// <summary>
        /// Gets the rows of the libcurl section.
        /// </summary>
        /// <param name="library">What libcurl reports about itself.</param>
        /// <param name="references">The number of live handles holding libcurl's global state.</param>
        /// <returns>The version, build target, TLS and zlib versions, protocols, features and global references.</returns>
        internal static List<KeyValuePair<string, string>> GetLibraryRows(CurlLibraryInfo library, int references)
        {
            return new List<KeyValuePair<string, string>>
            {
                new("Version", $"{library.Version} (0x{library.VersionNumber.ToString("X6", CultureInfo.InvariantCulture)})"),
                new("Build target", library.Host),
                new("TLS", library.SslVersion ?? "none"),
                new("zlib", library.LibzVersion ?? "none"),
                new("Protocols", string.Join(", ", library.Protocols)),
                new("Features", FormatFeatures(library.Features)),
                new("Global references", $"{references.ToString(CultureInfo.InvariantCulture)} live easy and multi handles"),
            };
        }

        /// <summary>
        /// Gets the rows of the Requests section.
        /// </summary>
        /// <param name="totals">The totals of every handler.</param>
        /// <returns>The handler count and the request counters.</returns>
        internal static List<KeyValuePair<string, string>> GetRequestRows(Totals totals)
        {
            return new List<KeyValuePair<string, string>>
            {
                new("Handlers", $"{Format(totals.AliveHandlers)} alive, {Format(totals.DisposedHandlers)} disposed"),
                new("Total", Format(totals.TotalRequests)),
                new("Queued", $"{Format(totals.QueuedRequests)} waiting for the libcurl worker"),
                new("Running", $"{Format(totals.RunningRequests)} in libcurl"),
                new("Completed", Format(totals.CompletedRequests)),
                new("Failed", Format(totals.FailedRequests)),
                new("Cancelled", Format(totals.CancelledRequests)),
            };
        }

        /// <summary>
        /// Gets the rows of the Connection Pool section.
        /// </summary>
        /// <param name="totals">The totals of every handler.</param>
        /// <param name="isTracking">Whether connection tracking is on for new requests.</param>
        /// <returns>The pool size, its idle and active connections, how many were opened, reused and closed, and the limits.</returns>
        internal static List<KeyValuePair<string, string>> GetPoolRows(Totals totals, bool isTracking)
        {
            var uses = totals.ConnectionsOpened + totals.ConnectionReuses;
            var reuseRate = uses == 0 ? "-" : (100d * totals.ConnectionReuses / uses).ToString("0", CultureInfo.InvariantCulture) + "%";
            var maxPerHost = totals.MaxConnectionsPerHost > 0
                ? $"{Format(totals.MaxConnectionsPerHost)} (CURLMOPT_MAX_HOST_CONNECTIONS)"
                : "-";

            return new List<KeyValuePair<string, string>>
            {
                new("Connection tracking", isTracking ? "On" : "Off: new requests do not report their connections"),
                new("Pool size", $"{Format(totals.OpenConnections)} open connections"),
                new("Reusable (idle)", Format(totals.IdleConnections)),
                new("In use", Format(totals.ActiveConnections)),
                new("Opened", Format(totals.ConnectionsOpened)),
                new("Reused", $"{Format(totals.ConnectionReuses)} requests on a pooled connection"),
                new("Reuse rate", reuseRate),
                new("Closed", Format(totals.ConnectionsClosed)),
                new("Max per host", maxPerHost),
                new("Idle limit", "libcurl default: 4 per transfer in progress (CURLMOPT_MAXCONNECTS not set)"),
            };
        }

        /// <summary>
        /// Gets the rows of the handler table.
        /// </summary>
        /// <param name="handlers">The handler statistics.</param>
        /// <returns>One row per handler, in <see cref="s_handlerHeadings"/> order.</returns>
        internal static List<string[]> GetHandlerRows(IReadOnlyList<CurlHandlerStatisticsSnapshot> handlers)
        {
            var rows = new List<string[]>(handlers.Count);
            foreach (var handler in handlers)
            {
                rows.Add(new[]
                {
                    Format(handler.Id),
                    handler.IsDisposed ? "Disposed" : "Alive",
                    handler.CreatedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                    Format(handler.TotalRequests),
                    Format(handler.QueuedRequests),
                    Format(handler.RunningRequests),
                    Format(handler.CompletedRequests),
                    Format(handler.FailedRequests),
                    Format(handler.CancelledRequests),
                    Format(handler.OpenConnections),
                    Format(handler.IdleConnections),
                    Format(handler.ConnectionReuses),
                });
            }

            return rows;
        }

        /// <summary>
        /// Gets the rows of the connection table.
        /// </summary>
        /// <param name="handlers">The handler statistics.</param>
        /// <param name="now">The local time the idle durations are measured to.</param>
        /// <returns>One row per open connection of every handler, in <see cref="s_connectionHeadings"/> order.</returns>
        internal static List<string[]> GetConnectionRows(IReadOnlyList<CurlHandlerStatisticsSnapshot> handlers, DateTime now)
        {
            var rows = new List<string[]>();
            foreach (var handler in handlers)
            {
                foreach (var connection in handler.Connections)
                {
                    rows.Add(new[]
                    {
                        "#" + Format(handler.Id),
                        "#" + Format(connection.Id),
                        connection.RemoteAddress,
                        connection.LocalAddress,
                        connection.IsIdle ? "Idle" : "In use",
                        Format(connection.ActiveTransfers),
                        Format(connection.RequestCount),
                        connection.OpenedAt?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "before tracking",
                        CurlNetworkFormatter.FormatDuration(now - connection.LastUsedAt) + " ago",
                    });
                }
            }

            return rows;
        }

        /// <summary>
        /// Lists the names of the features libcurl was built with.
        /// </summary>
        /// <param name="features">The feature bits.</param>
        /// <returns>The feature names joined with commas, followed by any unknown bits in hex, or <c>none</c>.</returns>
        internal static string FormatFeatures(CurlFeatures features)
        {
            var names = new List<string>();
            var known = CurlFeatures.None;
            foreach (CurlFeatures feature in Enum.GetValues(typeof(CurlFeatures)))
            {
                known |= feature;
                if (feature != CurlFeatures.None && (features & feature) == feature)
                {
                    names.Add(feature.ToString());
                }
            }
            var unknown = (int)(features & ~known);
            if (unknown != 0)
            {
                names.Add("0x" + unknown.ToString("X", CultureInfo.InvariantCulture));
            }

            return names.Count == 0 ? "none" : string.Join(", ", names);
        }

        /// <summary>
        /// Draws the toolbar: the connection tracking switch and the command that drops disposed handlers.
        /// </summary>
        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var tracking = new GUIContent("Track Connections", "Make requests sent from now on report the connections they open, reuse and close.");
                var isTracking = GUILayout.Toggle(CurlStatistics.IsConnectionTrackingEnabled, tracking, EditorStyles.toolbarButton, GUILayout.Width(120));
                if (isTracking != CurlStatistics.IsConnectionTrackingEnabled)
                {
                    CurlStatistics.IsConnectionTrackingEnabled = isTracking;
                }
                if (GUILayout.Button(new GUIContent("Clear Disposed", "Remove disposed handlers from the list."), EditorStyles.toolbarButton, GUILayout.Width(96)))
                {
                    CurlStatistics.ClearDisposed();
                    Refresh();
                }
                GUILayout.FlexibleSpace();
            }
        }

        /// <summary>
        /// Formats a count.
        /// </summary>
        /// <param name="value">The count.</param>
        /// <returns>The count in invariant digits.</returns>
        private static string Format(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The statistics of every listed handler added up.
        /// </summary>
        internal struct Totals
        {
            /// <summary>
            /// The number of live handlers.
            /// </summary>
            public int AliveHandlers;

            /// <summary>
            /// The number of listed disposed handlers.
            /// </summary>
            public int DisposedHandlers;

            /// <summary>
            /// The number of requests ever queued.
            /// </summary>
            public long TotalRequests;

            /// <summary>
            /// The number of requests waiting for a libcurl worker.
            /// </summary>
            public long QueuedRequests;

            /// <summary>
            /// The number of requests libcurl is transferring.
            /// </summary>
            public long RunningRequests;

            /// <summary>
            /// The number of requests libcurl finished successfully.
            /// </summary>
            public long CompletedRequests;

            /// <summary>
            /// The number of requests that failed.
            /// </summary>
            public long FailedRequests;

            /// <summary>
            /// The number of requests that were cancelled.
            /// </summary>
            public long CancelledRequests;

            /// <summary>
            /// The number of open pooled connections.
            /// </summary>
            public int OpenConnections;

            /// <summary>
            /// The number of open connections no transfer uses.
            /// </summary>
            public int IdleConnections;

            /// <summary>
            /// The number of connections opened since tracking started.
            /// </summary>
            public long ConnectionsOpened;

            /// <summary>
            /// The number of requests that reused a pooled connection.
            /// </summary>
            public long ConnectionReuses;

            /// <summary>
            /// The number of tracked connections that were closed.
            /// </summary>
            public long ConnectionsClosed;

            /// <summary>
            /// The largest <c>CURLMOPT_MAX_HOST_CONNECTIONS</c> of the live handlers.
            /// </summary>
            public int MaxConnectionsPerHost;

            /// <summary>
            /// Gets the number of open connections at least one transfer uses.
            /// </summary>
            public readonly int ActiveConnections
            {
                get => OpenConnections - IdleConnections;
            }
        }
    }
}
