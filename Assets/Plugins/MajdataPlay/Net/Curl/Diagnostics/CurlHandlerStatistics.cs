#nullable enable
using System;
using System.Collections.Generic;

namespace MajdataPlay.Net.Curl.Diagnostics
{
    /// <summary>
    /// Request counters and the connection pool of one <see cref="CurlHttpMessageHandler"/>, for debugging tools.
    /// </summary>
    /// <remarks>
    /// The request counters are always kept. Connections are tracked only for requests sent while
    /// <see cref="CurlStatistics.IsConnectionTrackingEnabled"/> is set: libcurl reports which connection each such
    /// request uses and when it closes a connection. libcurl notices that a server closed an idle connection only when
    /// it next looks for a connection to that host, so such a connection stays listed until then, as it stays in
    /// libcurl's own pool. Every member is thread-safe.
    /// </remarks>
    public sealed class CurlHandlerStatistics
    {
        /// <summary>
        /// Gets the identifier of the handler, increasing in creation order.
        /// </summary>
        public long Id { get; }

        /// <summary>
        /// Gets the local time the handler was created.
        /// </summary>
        public DateTime CreatedAt { get; }

        /// <summary>
        /// Guards every mutable field.
        /// </summary>
        private readonly object _sync = new();

        /// <summary>
        /// The open connections by libcurl connection identifier.
        /// </summary>
        private readonly SortedDictionary<long, Connection> _connections = new();

        /// <summary>
        /// The local time the handler was disposed, or <see langword="null"/> while it is alive.
        /// </summary>
        private DateTime? _disposedAt;

        /// <summary>
        /// The number of requests ever queued.
        /// </summary>
        private long _totalRequests;

        /// <summary>
        /// The number of requests waiting for the worker.
        /// </summary>
        private long _queuedRequests;

        /// <summary>
        /// The number of requests attached to the multi handle.
        /// </summary>
        private long _runningRequests;

        /// <summary>
        /// The number of requests libcurl finished successfully.
        /// </summary>
        private long _completedRequests;

        /// <summary>
        /// The number of requests that failed.
        /// </summary>
        private long _failedRequests;

        /// <summary>
        /// The number of requests that were cancelled.
        /// </summary>
        private long _cancelledRequests;

        /// <summary>
        /// The <c>CURLMOPT_MAX_HOST_CONNECTIONS</c> value applied last.
        /// </summary>
        private int _maxConnectionsPerHost;

        /// <summary>
        /// The <c>CURLMOPT_MAXCONNECTS</c> value applied last, 0 when libcurl sizes the pool itself.
        /// </summary>
        private int _maxCachedConnections;

        /// <summary>
        /// Whether a request of this handler tracked its connection.
        /// </summary>
        private bool _isTrackingConnections;

        /// <summary>
        /// The number of connections opened since tracking started.
        /// </summary>
        private long _connectionsOpened;

        /// <summary>
        /// The number of requests that reused a pooled connection.
        /// </summary>
        private long _connectionReuses;

        /// <summary>
        /// The number of tracked connections that were closed.
        /// </summary>
        private long _connectionsClosed;

        /// <summary>
        /// Creates the statistics of a new handler.
        /// </summary>
        /// <param name="id">The identifier of the handler.</param>
        internal CurlHandlerStatistics(long id)
        {
            Id = id;
            CreatedAt = DateTime.Now;
        }

        /// <summary>
        /// Gets whether the handler has been disposed.
        /// </summary>
        public bool IsDisposed
        {
            get
            {
                lock (_sync)
                {
                    return _disposedAt.HasValue;
                }
            }
        }

        /// <summary>
        /// Takes a consistent view of the counters and the connection pool.
        /// </summary>
        /// <returns>The snapshot.</returns>
        public CurlHandlerStatisticsSnapshot GetSnapshot()
        {
            lock (_sync)
            {
                var connections = new CurlConnectionInfo[_connections.Count];
                var index = 0;
                foreach (var connection in _connections.Values)
                {
                    connections[index++] = connection.ToInfo();
                }

                return new CurlHandlerStatisticsSnapshot
                {
                    Id = Id,
                    CreatedAt = CreatedAt,
                    DisposedAt = _disposedAt,
                    TotalRequests = _totalRequests,
                    QueuedRequests = _queuedRequests,
                    RunningRequests = _runningRequests,
                    CompletedRequests = _completedRequests,
                    FailedRequests = _failedRequests,
                    CancelledRequests = _cancelledRequests,
                    MaxConnectionsPerHost = _maxConnectionsPerHost,
                    MaxCachedConnections = _maxCachedConnections,
                    IsTrackingConnections = _isTrackingConnections,
                    ConnectionsOpened = _connectionsOpened,
                    ConnectionReuses = _connectionReuses,
                    ConnectionsClosed = _connectionsClosed,
                    Connections = connections,
                };
            }
        }

        /// <summary>
        /// Records that a request was queued for the worker.
        /// </summary>
        internal void OnRequestQueued()
        {
            lock (_sync)
            {
                _totalRequests++;
                _queuedRequests++;
            }
        }

        /// <summary>
        /// Records that the worker took a request off its queue, whether it attaches it or not.
        /// </summary>
        internal void OnRequestDequeued()
        {
            lock (_sync)
            {
                _queuedRequests = Math.Max(0, _queuedRequests - 1);
            }
        }

        /// <summary>
        /// Records that a request was attached to the multi handle.
        /// </summary>
        internal void OnRequestAttached()
        {
            lock (_sync)
            {
                _runningRequests++;
            }
        }

        /// <summary>
        /// Records that a request left the multi handle, releasing the connection it used.
        /// </summary>
        /// <param name="connectionId">The connection the request used, or -1.</param>
        internal void OnRequestDetached(long connectionId)
        {
            lock (_sync)
            {
                _runningRequests = Math.Max(0, _runningRequests - 1);
                ReleaseConnectionLocked(connectionId);
            }
        }

        /// <summary>
        /// Records the final state of a request.
        /// </summary>
        /// <param name="state">
        /// <see cref="CurlNetworkRequestState.Completed"/>, <see cref="CurlNetworkRequestState.Failed"/> or
        /// <see cref="CurlNetworkRequestState.Cancelled"/>; other values are ignored.
        /// </param>
        internal void OnRequestFinished(CurlNetworkRequestState state)
        {
            lock (_sync)
            {
                switch (state)
                {
                    case CurlNetworkRequestState.Completed:
                        _completedRequests++;
                        break;
                    case CurlNetworkRequestState.Failed:
                        _failedRequests++;
                        break;
                    case CurlNetworkRequestState.Cancelled:
                        _cancelledRequests++;
                        break;
                }
            }
        }

        /// <summary>
        /// Records the connection pool limits applied to the multi handle.
        /// </summary>
        /// <param name="maxConnectionsPerHost">The <c>CURLMOPT_MAX_HOST_CONNECTIONS</c> value.</param>
        /// <param name="maxCachedConnections">The <c>CURLMOPT_MAXCONNECTS</c> value, 0 when libcurl sizes the pool itself.</param>
        internal void OnPoolLimitsApplied(int maxConnectionsPerHost, int maxCachedConnections)
        {
            lock (_sync)
            {
                _maxConnectionsPerHost = maxConnectionsPerHost;
                _maxCachedConnections = maxCachedConnections;
            }
        }

        /// <summary>
        /// Records that libcurl gave a request a connection, opened for it or taken from the pool.
        /// </summary>
        /// <param name="previousConnectionId">
        /// The connection the request used before, such as before following a redirect to another host, or -1.
        /// </param>
        /// <param name="connectionId">The connection the request uses now.</param>
        /// <param name="isNew">Whether libcurl opened the connection for this request.</param>
        /// <param name="remoteAddress">The remote endpoint.</param>
        /// <param name="localAddress">The local endpoint.</param>
        internal void OnConnectionUsed(long previousConnectionId, long connectionId, bool isNew, string remoteAddress, string localAddress)
        {
            var now = DateTime.Now;
            lock (_sync)
            {
                _isTrackingConnections = true;
                if (previousConnectionId == connectionId && _connections.TryGetValue(connectionId, out var current))
                {
                    // A redirect to the same host keeps the connection the request already holds.
                    current.RequestCount++;
                    current.LastUsedAt = now;
                    return;
                }
                ReleaseConnectionLocked(previousConnectionId);
                if (!_connections.TryGetValue(connectionId, out var connection))
                {
                    connection = new Connection(connectionId, remoteAddress, localAddress, isNew ? now : null);
                    _connections.Add(connectionId, connection);
                }
                if (isNew)
                {
                    _connectionsOpened++;
                }
                else
                {
                    _connectionReuses++;
                }
                connection.ActiveTransfers++;
                connection.RequestCount++;
                connection.LastUsedAt = now;
            }
        }

        /// <summary>
        /// Records that libcurl closed a connection. Unknown connections, such as ones that never finished connecting,
        /// are ignored.
        /// </summary>
        /// <param name="connectionId">The connection identifier.</param>
        internal void OnConnectionClosed(long connectionId)
        {
            lock (_sync)
            {
                if (_connections.Remove(connectionId))
                {
                    _connectionsClosed++;
                }
            }
        }

        /// <summary>
        /// Records that the handler was disposed: libcurl closed every pooled connection and dropped every request.
        /// </summary>
        internal void OnDisposed()
        {
            lock (_sync)
            {
                if (_disposedAt.HasValue)
                {
                    return;
                }
                _disposedAt = DateTime.Now;
                _connectionsClosed += _connections.Count;
                _connections.Clear();
                _queuedRequests = 0;
                _runningRequests = 0;
            }
        }

        /// <summary>
        /// Releases a request's hold on a connection. Must be called while holding <see cref="_sync"/>.
        /// </summary>
        /// <param name="connectionId">The connection, or -1.</param>
        private void ReleaseConnectionLocked(long connectionId)
        {
            if (connectionId < 0 || !_connections.TryGetValue(connectionId, out var connection))
            {
                return;
            }
            connection.ActiveTransfers = Math.Max(0, connection.ActiveTransfers - 1);
            connection.LastUsedAt = DateTime.Now;
        }

        /// <summary>
        /// The mutable state of one tracked connection.
        /// </summary>
        private sealed class Connection
        {
            /// <summary>
            /// Creates the state of a connection.
            /// </summary>
            /// <param name="id">The libcurl connection identifier.</param>
            /// <param name="remoteAddress">The remote endpoint.</param>
            /// <param name="localAddress">The local endpoint.</param>
            /// <param name="openedAt">The time it was opened, or <see langword="null"/> when unknown.</param>
            public Connection(long id, string remoteAddress, string localAddress, DateTime? openedAt)
            {
                Id = id;
                RemoteAddress = remoteAddress;
                LocalAddress = localAddress;
                OpenedAt = openedAt;
            }

            /// <summary>
            /// Gets the libcurl connection identifier.
            /// </summary>
            public long Id { get; }

            /// <summary>
            /// Gets the remote endpoint.
            /// </summary>
            public string RemoteAddress { get; }

            /// <summary>
            /// Gets the local endpoint.
            /// </summary>
            public string LocalAddress { get; }

            /// <summary>
            /// Gets the time the connection was opened, or <see langword="null"/> when unknown.
            /// </summary>
            public DateTime? OpenedAt { get; }

            /// <summary>
            /// Gets or sets the number of transfers using the connection.
            /// </summary>
            public int ActiveTransfers { get; set; }

            /// <summary>
            /// Gets or sets the number of requests sent over the connection.
            /// </summary>
            public long RequestCount { get; set; }

            /// <summary>
            /// Gets or sets the time the connection was last given to or released by a transfer.
            /// </summary>
            public DateTime LastUsedAt { get; set; }

            /// <summary>
            /// Copies the state into an immutable value.
            /// </summary>
            /// <returns>The connection info.</returns>
            public CurlConnectionInfo ToInfo()
            {
                return new CurlConnectionInfo
                {
                    Id = Id,
                    RemoteAddress = RemoteAddress,
                    LocalAddress = LocalAddress,
                    ActiveTransfers = ActiveTransfers,
                    RequestCount = RequestCount,
                    OpenedAt = OpenedAt,
                    LastUsedAt = LastUsedAt,
                };
            }
        }
    }
}
