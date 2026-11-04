#nullable enable
using System;

namespace MajdataPlay.Net.Curl.Diagnostics
{
    /// <summary>
    /// The timing points libcurl reports for a transfer. Every value is measured from the moment libcurl started the
    /// transfer (after queueing), so the values are cumulative rather than phase durations.
    /// </summary>
    /// <remarks>
    /// A zero value means the transfer did not reach that point, or that libcurl skipped it, such as the TLS handshake of a
    /// plain HTTP request. When redirects are followed, libcurl adds the phases of every hop together.
    /// </remarks>
    public readonly struct CurlNetworkTimings
    {
        /// <summary>
        /// Gets the time until name resolution completed (<c>CURLINFO_NAMELOOKUP_TIME_T</c>).
        /// </summary>
        public TimeSpan NameLookup { get; init; }

        /// <summary>
        /// Gets the time until the TCP connection to the remote host or proxy completed (<c>CURLINFO_CONNECT_TIME_T</c>).
        /// </summary>
        public TimeSpan Connect { get; init; }

        /// <summary>
        /// Gets the time until the SSL/TLS handshake completed (<c>CURLINFO_APPCONNECT_TIME_T</c>).
        /// </summary>
        public TimeSpan AppConnect { get; init; }

        /// <summary>
        /// Gets the time until the request was about to be sent (<c>CURLINFO_PRETRANSFER_TIME_T</c>).
        /// </summary>
        public TimeSpan PreTransfer { get; init; }

        /// <summary>
        /// Gets the time until the last request byte was sent (<c>CURLINFO_POSTTRANSFER_TIME_T</c>).
        /// </summary>
        public TimeSpan PostTransfer { get; init; }

        /// <summary>
        /// Gets the time until the first response byte arrived (<c>CURLINFO_STARTTRANSFER_TIME_T</c>).
        /// </summary>
        public TimeSpan StartTransfer { get; init; }

        /// <summary>
        /// Gets the total time of the transfer, or the time so far while it is still running (<c>CURLINFO_TOTAL_TIME_T</c>).
        /// </summary>
        public TimeSpan Total { get; init; }

        /// <summary>
        /// Gets the time taken by every redirect step before the final request started (<c>CURLINFO_REDIRECT_TIME_T</c>).
        /// </summary>
        public TimeSpan Redirect { get; init; }
    }
}
