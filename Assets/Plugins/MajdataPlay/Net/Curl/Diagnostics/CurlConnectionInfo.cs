#nullable enable
using System;

namespace MajdataPlay.Net.Curl.Diagnostics
{
    /// <summary>
    /// One open connection in the connection pool of a <see cref="CurlHttpMessageHandler"/>, as
    /// <see cref="CurlHandlerStatistics"/> last saw it.
    /// </summary>
    public readonly struct CurlConnectionInfo
    {
        /// <summary>
        /// Gets libcurl's identifier of the connection, unique within its pool (<c>CURLINFO_CONN_ID</c>).
        /// </summary>
        public long Id { get; init; }

        /// <summary>
        /// Gets the remote endpoint, such as <c>127.0.0.1:80</c>.
        /// </summary>
        public string RemoteAddress { get; init; }

        /// <summary>
        /// Gets the local endpoint, such as <c>127.0.0.1:50123</c>.
        /// </summary>
        public string LocalAddress { get; init; }

        /// <summary>
        /// Gets the number of transfers currently using the connection; more than one with HTTP/2 multiplexing.
        /// </summary>
        public int ActiveTransfers { get; init; }

        /// <summary>
        /// Gets the number of requests sent over the connection since it was first seen.
        /// </summary>
        public long RequestCount { get; init; }

        /// <summary>
        /// Gets the local time the connection was opened, or <see langword="null"/> when it was opened before
        /// connection tracking started and first seen when it was reused.
        /// </summary>
        public DateTime? OpenedAt { get; init; }

        /// <summary>
        /// Gets the local time the connection was last given to a transfer or released by one.
        /// </summary>
        public DateTime LastUsedAt { get; init; }

        /// <summary>
        /// Gets whether no transfer uses the connection, so it waits in the pool to be reused.
        /// </summary>
        public bool IsIdle
        {
            get => ActiveTransfers == 0;
        }
    }
}
