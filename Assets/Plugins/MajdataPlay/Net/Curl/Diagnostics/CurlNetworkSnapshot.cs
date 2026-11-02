#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MajdataPlay.Net.Curl.Diagnostics
{
    /// <summary>
    /// An immutable view of a <see cref="CurlNetworkRecord"/> at one <see cref="Version"/>. Bodies are not copied into
    /// snapshots; read them with <see cref="CurlNetworkRecord.GetRequestBody"/> and <see cref="CurlNetworkRecord.GetResponseBody"/>.
    /// </summary>
    /// <remarks>
    /// The durations of a request that has not finished grow with the current time, so <see cref="Queueing"/>,
    /// <see cref="Total"/> and <see cref="Transfer"/> are evaluated on every access.
    /// </remarks>
    public sealed class CurlNetworkSnapshot
    {
        /// <summary>
        /// Gets the identifier of the record, increasing in request order.
        /// </summary>
        public long Id { get; internal init; }

        /// <summary>
        /// Gets the record version this snapshot was taken at.
        /// </summary>
        public int Version { get; internal init; }

        /// <summary>
        /// Gets the lifecycle stage of the request.
        /// </summary>
        public CurlNetworkRequestState State { get; internal init; }

        /// <summary>
        /// Gets whether the request completed, failed or was cancelled.
        /// </summary>
        public bool IsFinished
        {
            get => State >= CurlNetworkRequestState.Completed;
        }

        /// <summary>
        /// Gets the local time at which the request was queued.
        /// </summary>
        public DateTime StartedAt { get; internal init; }

        /// <summary>
        /// Gets the short display name: the last path segment and the query, or the host for the root path.
        /// </summary>
        public string Name { get; internal init; } = string.Empty;

        /// <summary>
        /// Gets the HTTP method.
        /// </summary>
        public string Method { get; internal init; } = string.Empty;

        /// <summary>
        /// Gets the requested URL.
        /// </summary>
        public string Url { get; internal init; } = string.Empty;

        /// <summary>
        /// Gets the URL of the last request after redirects, or <see langword="null"/> before libcurl reported it.
        /// </summary>
        public string? EffectiveUrl { get; internal init; }

        /// <summary>
        /// Gets the HTTP status code of the final response, or 0 before a status line arrived.
        /// </summary>
        public int StatusCode { get; internal init; }

        /// <summary>
        /// Gets the reason phrase of the final status line, such as <c>Not Found</c>; HTTP/2 and HTTP/3 have none.
        /// </summary>
        public string? ReasonPhrase { get; internal init; }

        /// <summary>
        /// Gets the raw final status line, such as <c>HTTP/1.1 200 OK</c>.
        /// </summary>
        public string? StatusLine { get; internal init; }

        /// <summary>
        /// Gets the protocol in ALPN notation (<c>http/1.1</c>, <c>h2</c>, <c>h3</c>), or <see langword="null"/> before a response arrived.
        /// </summary>
        public string? Protocol { get; internal init; }

        /// <summary>
        /// Gets the remote endpoint, such as <c>127.0.0.1:80</c>, or <see langword="null"/> before libcurl reported it.
        /// </summary>
        public string? RemoteAddress { get; internal init; }

        /// <summary>
        /// Gets the <c>Content-Type</c> of the final response.
        /// </summary>
        public string? ContentType { get; internal init; }

        /// <summary>
        /// Gets the request line libcurl sent, such as <c>GET /path HTTP/1.1</c>, or <see langword="null"/> before it was sent.
        /// </summary>
        public string? RequestLine { get; internal init; }

        /// <summary>
        /// Gets the request headers: the ones libcurl sent for the last request, or the provisional ones taken from the
        /// request message until then (see <see cref="AreRequestHeadersProvisional"/>).
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, string>> RequestHeaders { get; internal init; } = Array.Empty<KeyValuePair<string, string>>();

        /// <summary>
        /// Gets whether <see cref="RequestHeaders"/> come from the request message rather than from the wire, so headers
        /// libcurl adds itself (<c>Host</c>, <c>Accept</c>, <c>Content-Length</c>...) are missing.
        /// </summary>
        public bool AreRequestHeadersProvisional { get; internal init; }

        /// <summary>
        /// Gets the headers of the final response, in arrival order. Interim and redirect responses are not included.
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, string>> ResponseHeaders { get; internal init; } = Array.Empty<KeyValuePair<string, string>>();

        /// <summary>
        /// Gets the value of the <c>Cookie</c> request header, or <see langword="null"/> when no cookie is sent.
        /// </summary>
        public string? RequestCookies { get; internal init; }

        /// <summary>
        /// Gets the values of every <c>Set-Cookie</c> header of the final response.
        /// </summary>
        public IReadOnlyList<string> ResponseCookies { get; internal init; } = Array.Empty<string>();

        /// <summary>
        /// Gets the number of request body bytes libcurl read for the last attempt.
        /// </summary>
        public long RequestBodySize { get; internal init; }

        /// <summary>
        /// Gets the number of request body bytes kept by the record.
        /// </summary>
        public int CapturedRequestBodySize { get; internal init; }

        /// <summary>
        /// Gets whether only part of the request body was kept.
        /// </summary>
        public bool IsRequestBodyTruncated { get; internal init; }

        /// <summary>
        /// Gets the number of response body bytes delivered so far, after content decoding.
        /// </summary>
        public long ResponseBodySize { get; internal init; }

        /// <summary>
        /// Gets the number of response body bytes kept by the record.
        /// </summary>
        public int CapturedResponseBodySize { get; internal init; }

        /// <summary>
        /// Gets whether only part of the response body was kept.
        /// </summary>
        public bool IsResponseBodyTruncated { get; internal init; }

        /// <summary>
        /// Gets the number of response header bytes received, including interim and redirect responses.
        /// </summary>
        public long ResponseHeaderSize { get; internal init; }

        /// <summary>
        /// Gets the number of response bytes received: headers plus body.
        /// </summary>
        public long TransferredSize
        {
            get => ResponseHeaderSize + ResponseBodySize;
        }

        /// <summary>
        /// Gets the number of redirects libcurl followed.
        /// </summary>
        public int RedirectCount { get; internal init; }

        /// <summary>
        /// Gets the reason the request failed or was cancelled, or <see langword="null"/>.
        /// </summary>
        public string? Error { get; internal init; }

        /// <summary>
        /// Gets the timing points libcurl reported, or <see langword="null"/> before the first response byte arrived.
        /// </summary>
        public CurlNetworkTimings? Timings { get; internal init; }

        /// <summary>
        /// Gets the time to first byte: from the start of the transfer (after queueing) until the first response byte,
        /// covering name lookup, connecting, the TLS handshake, sending the request and the server's processing.
        /// <see langword="null"/> until the first response byte arrived.
        /// </summary>
        public TimeSpan? Ttfb
        {
            get
            {
                // libcurl also reports a start-transfer time for transfers that failed before any response byte, such as
                // a refused connection, so only a received byte makes it meaningful.
                if (FirstResponseByteTimestamp == 0)
                {
                    return null;
                }
                if (Timings is { } timings && timings.StartTransfer > TimeSpan.Zero)
                {
                    return timings.StartTransfer;
                }
                if (SubmittedTimestamp != 0)
                {
                    return ToTimeSpan(FirstResponseByteTimestamp - SubmittedTimestamp);
                }

                return null;
            }
        }

        /// <summary>
        /// Gets the time spent waiting for the libcurl worker before the transfer started.
        /// </summary>
        public TimeSpan Queueing
        {
            get => GetQueueing(Stopwatch.GetTimestamp());
        }

        /// <summary>
        /// Gets the total time from queueing until the transfer finished, or until now while it is running.
        /// <see cref="Queueing"/>, <see cref="Ttfb"/> and <see cref="Transfer"/> add up to it.
        /// </summary>
        public TimeSpan Total
        {
            get => GetTotal(Stopwatch.GetTimestamp());
        }

        /// <summary>
        /// Gets the content download time: from the first response byte until the transfer finished, or until now while
        /// it is running. <see langword="null"/> until the first response byte arrived.
        /// </summary>
        public TimeSpan? Transfer
        {
            get => GetTransfer(Stopwatch.GetTimestamp());
        }

        /// <summary>
        /// Gets the <see cref="Stopwatch"/> timestamp at which the request was queued; used to place requests on a
        /// common time line.
        /// </summary>
        public long QueuedTimestamp { get; internal init; }

        /// <summary>
        /// Gets the <see cref="Stopwatch"/> timestamp at which libcurl started the transfer, or 0.
        /// </summary>
        internal long SubmittedTimestamp { get; init; }

        /// <summary>
        /// Gets the <see cref="Stopwatch"/> timestamp at which the first response header byte arrived, or 0.
        /// </summary>
        internal long FirstResponseByteTimestamp { get; init; }

        /// <summary>
        /// Gets the <see cref="Stopwatch"/> timestamp at which the request finished, or 0.
        /// </summary>
        internal long EndTimestamp { get; init; }

        /// <summary>
        /// Converts a <see cref="Stopwatch"/> tick count to a time span.
        /// </summary>
        /// <param name="stopwatchTicks">The tick count; negative values count as zero.</param>
        /// <returns>The duration.</returns>
        public static TimeSpan ToTimeSpan(long stopwatchTicks)
        {
            if (stopwatchTicks <= 0)
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromTicks((long)(stopwatchTicks * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency)));
        }

        /// <summary>
        /// Gets the queueing time as of <paramref name="now"/>.
        /// </summary>
        /// <param name="now">The current <see cref="Stopwatch"/> timestamp.</param>
        /// <returns>The time between queueing and the start of the transfer, its end, or <paramref name="now"/>.</returns>
        public TimeSpan GetQueueing(long now)
        {
            var until = SubmittedTimestamp != 0 ? SubmittedTimestamp : (EndTimestamp != 0 ? EndTimestamp : now);

            return ToTimeSpan(until - QueuedTimestamp);
        }

        /// <summary>
        /// Gets the total time as of <paramref name="now"/>, preferring libcurl's total for a finished transfer.
        /// </summary>
        /// <param name="now">The current <see cref="Stopwatch"/> timestamp.</param>
        /// <returns>The total time.</returns>
        public TimeSpan GetTotal(long now)
        {
            if (!IsFinished)
            {
                return ToTimeSpan(now - QueuedTimestamp);
            }
            if (SubmittedTimestamp != 0 && Timings is { } timings && timings.Total > TimeSpan.Zero)
            {
                return GetQueueing(now) + timings.Total;
            }

            return ToTimeSpan(EndTimestamp - QueuedTimestamp);
        }

        /// <summary>
        /// Gets the content download time as of <paramref name="now"/>.
        /// </summary>
        /// <param name="now">The current <see cref="Stopwatch"/> timestamp.</param>
        /// <returns>What remains of the total after queueing and the time to first byte, or <see langword="null"/>.</returns>
        public TimeSpan? GetTransfer(long now)
        {
            if (Ttfb is not { } ttfb)
            {
                return null;
            }
            var transfer = GetTotal(now) - GetQueueing(now) - ttfb;

            return transfer > TimeSpan.Zero ? transfer : TimeSpan.Zero;
        }
    }
}
