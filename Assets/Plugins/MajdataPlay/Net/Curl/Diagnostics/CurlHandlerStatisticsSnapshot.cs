#nullable enable
using System;
using System.Collections.Generic;

namespace MajdataPlay.Net.Curl.Diagnostics
{
    /// <summary>
    /// A consistent view of a <see cref="CurlHandlerStatistics"/> at one moment.
    /// </summary>
    public sealed class CurlHandlerStatisticsSnapshot
    {
        /// <summary>
        /// Gets the identifier of the handler, increasing in creation order.
        /// </summary>
        public long Id { get; internal init; }

        /// <summary>
        /// Gets the local time the handler was created.
        /// </summary>
        public DateTime CreatedAt { get; internal init; }

        /// <summary>
        /// Gets the local time the handler was disposed, or <see langword="null"/> while it is alive.
        /// </summary>
        public DateTime? DisposedAt { get; internal init; }

        /// <summary>
        /// Gets whether the handler has been disposed.
        /// </summary>
        public bool IsDisposed
        {
            get => DisposedAt.HasValue;
        }

        /// <summary>
        /// Gets the number of requests ever queued.
        /// </summary>
        public long TotalRequests { get; internal init; }

        /// <summary>
        /// Gets the number of requests waiting for the libcurl worker.
        /// </summary>
        public long QueuedRequests { get; internal init; }

        /// <summary>
        /// Gets the number of requests libcurl is transferring, including those waiting for a free connection.
        /// </summary>
        public long RunningRequests { get; internal init; }

        /// <summary>
        /// Gets the number of requests libcurl finished successfully, whatever their HTTP status.
        /// </summary>
        public long CompletedRequests { get; internal init; }

        /// <summary>
        /// Gets the number of requests that failed.
        /// </summary>
        public long FailedRequests { get; internal init; }

        /// <summary>
        /// Gets the number of requests that were cancelled, or whose response or handler was disposed first.
        /// </summary>
        public long CancelledRequests { get; internal init; }

        /// <summary>
        /// Gets the value of <c>CURLMOPT_MAX_HOST_CONNECTIONS</c> applied last, or 0 before the first request.
        /// </summary>
        public int MaxConnectionsPerHost { get; internal init; }

        /// <summary>
        /// Gets whether connection tracking was on for at least one request of this handler.
        /// </summary>
        public bool IsTrackingConnections { get; internal init; }

        /// <summary>
        /// Gets the number of connections opened since tracking started.
        /// </summary>
        public long ConnectionsOpened { get; internal init; }

        /// <summary>
        /// Gets the number of requests that reused a connection from the pool instead of opening one.
        /// </summary>
        public long ConnectionReuses { get; internal init; }

        /// <summary>
        /// Gets the number of tracked connections that were closed.
        /// </summary>
        public long ConnectionsClosed { get; internal init; }

        /// <summary>
        /// Gets the open connections of the pool, in identifier order.
        /// </summary>
        public IReadOnlyList<CurlConnectionInfo> Connections { get; internal init; } = Array.Empty<CurlConnectionInfo>();

        /// <summary>
        /// Gets the number of open connections in the pool.
        /// </summary>
        public int OpenConnections
        {
            get => Connections.Count;
        }

        /// <summary>
        /// Gets the number of open connections no transfer uses, which the next request to their host can reuse.
        /// </summary>
        public int IdleConnections
        {
            get
            {
                var count = 0;
                foreach (var connection in Connections)
                {
                    if (connection.IsIdle)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// Gets the number of open connections at least one transfer uses.
        /// </summary>
        public int ActiveConnections
        {
            get => OpenConnections - IdleConnections;
        }
    }
}
