#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace MajdataPlay.Net.Curl.Diagnostics
{
    /// <summary>
    /// Everything <see cref="CurlNetworkMonitor"/> records about one request sent through
    /// <see cref="CurlHttpMessageHandler"/>: its headers, the leading bytes of both bodies, its state and its timings.
    /// </summary>
    /// <remarks>
    /// The libcurl worker updates a record from its callbacks while any thread reads it, so every member is thread-safe.
    /// Reads go through <see cref="GetSnapshot"/>, which returns the same instance until the record changes.
    /// </remarks>
    public sealed class CurlNetworkRecord
    {
        /// <summary>
        /// The prefix of an HTTP status line.
        /// </summary>
        private const string StatusLinePrefix = "HTTP/";

        /// <summary>
        /// The <c>Cookie</c> request header name.
        /// </summary>
        private const string CookieHeader = "Cookie";

        /// <summary>
        /// The <c>Set-Cookie</c> response header name.
        /// </summary>
        private const string SetCookieHeader = "Set-Cookie";

        /// <summary>
        /// The <c>Content-Type</c> header name.
        /// </summary>
        private const string ContentTypeHeader = "Content-Type";

        /// <summary>
        /// Gets the identifier of the record, increasing in request order.
        /// </summary>
        public long Id { get; }

        /// <summary>
        /// Gets the HTTP method.
        /// </summary>
        public string Method { get; }

        /// <summary>
        /// Gets the requested URL.
        /// </summary>
        public string Url { get; }

        /// <summary>
        /// Gets the short display name: the last path segment and the query, or the host for the root path.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the local time at which the request was queued.
        /// </summary>
        public DateTime StartedAt { get; }

        /// <summary>
        /// Gets a number that changes whenever the record changes.
        /// </summary>
        public int Version
        {
            get => Volatile.Read(ref _version);
        }

        /// <summary>
        /// Guards every mutable field.
        /// </summary>
        private readonly object _sync = new();

        /// <summary>
        /// The <see cref="Stopwatch"/> timestamp at which the request was queued.
        /// </summary>
        private readonly long _queuedTimestamp;

        /// <summary>
        /// The request headers known before libcurl sends anything: the message and content headers, plus the
        /// <c>Cookie</c> header taken from the handler's cookie container.
        /// </summary>
        private readonly KeyValuePair<string, string>[] _provisionalRequestHeaders;

        /// <summary>
        /// The request body libcurl read.
        /// </summary>
        private readonly CurlNetworkBodyCapture _requestBody;

        /// <summary>
        /// The response body delivered to the reader.
        /// </summary>
        private readonly CurlNetworkBodyCapture _responseBody;

        /// <summary>
        /// The headers of the latest response header block.
        /// </summary>
        private readonly List<KeyValuePair<string, string>> _responseHeaders = new();

        /// <summary>
        /// A copy of <see cref="_responseHeaders"/> shared by snapshots; cleared when the headers change.
        /// </summary>
        private KeyValuePair<string, string>[]? _responseHeadersCopy;

        /// <summary>
        /// The headers of the latest request head libcurl sent, or <see langword="null"/> before one was sent.
        /// </summary>
        private KeyValuePair<string, string>[]? _sentRequestHeaders;

        /// <summary>
        /// The request line of the latest request head libcurl sent.
        /// </summary>
        private string? _requestLine;

        /// <summary>
        /// The latest status line.
        /// </summary>
        private string? _statusLine;

        /// <summary>
        /// The protocol token of <see cref="_statusLine"/>, such as <c>HTTP/1.1</c>.
        /// </summary>
        private string? _statusProtocol;

        /// <summary>
        /// The status code of <see cref="_statusLine"/>.
        /// </summary>
        private int _statusCode;

        /// <summary>
        /// The reason phrase of <see cref="_statusLine"/>.
        /// </summary>
        private string? _reasonPhrase;

        /// <summary>
        /// The number of response header bytes received, including every interim and redirect response.
        /// </summary>
        private long _receivedHeaderBytes;

        /// <summary>
        /// The details libcurl reported most recently.
        /// </summary>
        private CurlTransferInfo? _transferInfo;

        /// <summary>
        /// The lifecycle stage.
        /// </summary>
        private CurlNetworkRequestState _state = CurlNetworkRequestState.Queued;

        /// <summary>
        /// The reason the request failed or was cancelled.
        /// </summary>
        private string? _error;

        /// <summary>
        /// The <see cref="Stopwatch"/> timestamp at which libcurl started the transfer, or 0.
        /// </summary>
        private long _submittedTimestamp;

        /// <summary>
        /// The <see cref="Stopwatch"/> timestamp of the first response header byte, or 0.
        /// </summary>
        private long _firstResponseByteTimestamp;

        /// <summary>
        /// The <see cref="Stopwatch"/> timestamp at which the request finished, or 0.
        /// </summary>
        private long _endTimestamp;

        /// <summary>
        /// Backing field of <see cref="Version"/>.
        /// </summary>
        private int _version;

        /// <summary>
        /// The snapshot of the current version, or <see langword="null"/> once the record changed.
        /// </summary>
        private CurlNetworkSnapshot? _snapshot;

        /// <summary>
        /// Creates a record for a request that is about to be queued.
        /// </summary>
        /// <param name="id">The identifier of the record.</param>
        /// <param name="request">The request message.</param>
        /// <param name="containerCookies">The <c>Cookie</c> header the handler's cookie container adds, or <see langword="null"/>.</param>
        /// <param name="maxBodyBytes">The maximum number of bytes kept of each body.</param>
        internal CurlNetworkRecord(long id, HttpRequestMessage request, string? containerCookies, int maxBodyBytes)
        {
            Id = id;
            Method = request.Method.Method;
            Url = request.RequestUri?.OriginalString ?? string.Empty;
            Name = GetName(request.RequestUri, Url);
            StartedAt = DateTime.Now;
            _queuedTimestamp = Stopwatch.GetTimestamp();
            _provisionalRequestHeaders = CollectRequestHeaders(request, containerCookies);
            _requestBody = new CurlNetworkBodyCapture(maxBodyBytes);
            _responseBody = new CurlNetworkBodyCapture(maxBodyBytes);
        }

        /// <summary>
        /// Gets an immutable view of the record, reusing the previous one while the record has not changed.
        /// </summary>
        /// <returns>The snapshot of the current version.</returns>
        public CurlNetworkSnapshot GetSnapshot()
        {
            lock (_sync)
            {
                return _snapshot ??= CreateSnapshotLocked();
            }
        }

        /// <summary>
        /// Copies the captured request body.
        /// </summary>
        /// <returns>The kept request body bytes; see <see cref="CurlNetworkSnapshot.IsRequestBodyTruncated"/>.</returns>
        public byte[] GetRequestBody()
        {
            lock (_sync)
            {
                return _requestBody.ToArray();
            }
        }

        /// <summary>
        /// Copies the captured response body.
        /// </summary>
        /// <returns>The kept response body bytes; see <see cref="CurlNetworkSnapshot.IsResponseBodyTruncated"/>.</returns>
        public byte[] GetResponseBody()
        {
            lock (_sync)
            {
                return _responseBody.ToArray();
            }
        }

        /// <summary>
        /// Records that libcurl started the transfer.
        /// </summary>
        internal void OnSubmitted()
        {
            lock (_sync)
            {
                if (_state != CurlNetworkRequestState.Queued)
                {
                    return;
                }
                _state = CurlNetworkRequestState.Running;
                _submittedTimestamp = Stopwatch.GetTimestamp();
                MarkChangedLocked();
            }
        }

        /// <summary>
        /// Records a request head libcurl sent; a redirected request replaces the previous one.
        /// </summary>
        /// <param name="head">The request line and header lines, each ending with CRLF.</param>
        internal void OnRequestHeadersSent(ReadOnlySpan<byte> head)
        {
            var lines = Encoding.UTF8.GetString(head).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var headers = new List<KeyValuePair<string, string>>(lines.Length);
            for (var i = 1; i < lines.Length; i++)
            {
                if (TryParseHeader(lines[i], out var header))
                {
                    headers.Add(header);
                }
            }
            lock (_sync)
            {
                _requestLine = lines.Length > 0 ? lines[0] : null;
                _sentRequestHeaders = headers.ToArray();
                MarkChangedLocked();
            }
        }

        /// <summary>
        /// Records request body bytes libcurl read for upload.
        /// </summary>
        /// <param name="data">The bytes read.</param>
        internal void OnRequestBodyRead(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty)
            {
                return;
            }
            lock (_sync)
            {
                _requestBody.Append(data);
                MarkChangedLocked();
            }
        }

        /// <summary>
        /// Records that libcurl rewound the upload to send it again.
        /// </summary>
        /// <param name="position">The body position libcurl rewound to.</param>
        internal void OnRequestBodyRewound(long position)
        {
            lock (_sync)
            {
                _requestBody.Rewind(position);
                MarkChangedLocked();
            }
        }

        /// <summary>
        /// Records one response header line. A status line starts a new header block, so only the headers of the final
        /// response are kept, while the received header bytes of every block are counted.
        /// </summary>
        /// <param name="rawLine">The line as libcurl delivered it, including its line terminator.</param>
        internal void OnResponseHeaderLine(ReadOnlySpan<byte> rawLine)
        {
            var line = Encoding.UTF8.GetString(rawLine).TrimEnd('\r', '\n', ' ');
            var isStatusLine = line.StartsWith(StatusLinePrefix, StringComparison.Ordinal);
            var header = default(KeyValuePair<string, string>);
            var isHeader = !isStatusLine && TryParseHeader(line, out header);
            lock (_sync)
            {
                if (_firstResponseByteTimestamp == 0)
                {
                    _firstResponseByteTimestamp = Stopwatch.GetTimestamp();
                }
                _receivedHeaderBytes += rawLine.Length;
                if (isStatusLine)
                {
                    _responseHeaders.Clear();
                    _responseHeadersCopy = null;
                    _statusLine = line;
                    ParseStatusLine(line, out _statusProtocol, out _statusCode, out _reasonPhrase);
                }
                else if (isHeader)
                {
                    _responseHeaders.Add(header);
                    _responseHeadersCopy = null;
                }
                MarkChangedLocked();
            }
        }

        /// <summary>
        /// Records response body bytes delivered to the reader.
        /// </summary>
        /// <param name="data">The bytes delivered.</param>
        internal void OnResponseBodyReceived(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty)
            {
                return;
            }
            lock (_sync)
            {
                _responseBody.Append(data);
                MarkChangedLocked();
            }
        }

        /// <summary>
        /// Records the details libcurl reported; later reports replace earlier ones.
        /// </summary>
        /// <param name="info">The details read from the easy handle.</param>
        internal void OnTransferInfo(in CurlTransferInfo info)
        {
            lock (_sync)
            {
                _transferInfo = info;
                MarkChangedLocked();
            }
        }

        /// <summary>
        /// Records that libcurl finished the transfer successfully.
        /// </summary>
        internal void OnCompleted()
        {
            Finish(CurlNetworkRequestState.Completed, null);
        }

        /// <summary>
        /// Records that the transfer failed. A disposed response or handler counts as a cancellation.
        /// </summary>
        /// <param name="exception">The failure reported to the caller.</param>
        internal void OnFailed(Exception exception)
        {
            Finish(GetFailureState(exception), exception.Message);
        }

        /// <summary>
        /// Classifies the exception that ended a transfer early.
        /// </summary>
        /// <param name="exception">The failure reported to the caller.</param>
        /// <returns>
        /// <see cref="CurlNetworkRequestState.Cancelled"/> when the response or the handler was disposed first;
        /// otherwise <see cref="CurlNetworkRequestState.Failed"/>.
        /// </returns>
        internal static CurlNetworkRequestState GetFailureState(Exception exception)
        {
            return exception is ObjectDisposedException ? CurlNetworkRequestState.Cancelled : CurlNetworkRequestState.Failed;
        }

        /// <summary>
        /// Records that the caller cancelled the request.
        /// </summary>
        internal void OnCancelled()
        {
            Finish(CurlNetworkRequestState.Cancelled, "The request was cancelled.");
        }

        /// <summary>
        /// Frees the captured bodies and returns them to the shared budget, because the record left the history.
        /// </summary>
        internal void Release()
        {
            lock (_sync)
            {
                _requestBody.Release();
                _responseBody.Release();
            }
        }

        /// <summary>
        /// Moves the record to a final state once.
        /// </summary>
        /// <param name="state">The final state.</param>
        /// <param name="error">The reason for a failure or cancellation.</param>
        private void Finish(CurlNetworkRequestState state, string? error)
        {
            lock (_sync)
            {
                if (_state >= CurlNetworkRequestState.Completed)
                {
                    return;
                }
                _state = state;
                _error = error;
                _endTimestamp = Stopwatch.GetTimestamp();
                MarkChangedLocked();
            }
        }

        /// <summary>
        /// Invalidates the snapshot and announces the change. Must be called while holding <see cref="_sync"/>.
        /// </summary>
        private void MarkChangedLocked()
        {
            _snapshot = null;
            Volatile.Write(ref _version, _version + 1);
            CurlNetworkMonitor.NotifyChanged();
        }

        /// <summary>
        /// Creates the snapshot of the current version. Must be called while holding <see cref="_sync"/>.
        /// </summary>
        /// <returns>The new snapshot.</returns>
        private CurlNetworkSnapshot CreateSnapshotLocked()
        {
            var info = _transferInfo;
            var requestHeaders = _sentRequestHeaders ?? _provisionalRequestHeaders;
            var responseHeaders = _responseHeadersCopy ??= _responseHeaders.ToArray();

            return new CurlNetworkSnapshot
            {
                Id = Id,
                Version = _version,
                State = _state,
                StartedAt = StartedAt,
                Name = Name,
                Method = Method,
                Url = Url,
                EffectiveUrl = info?.EffectiveUrl,
                StatusCode = info?.ResponseCode > 0 ? info.Value.ResponseCode : _statusCode,
                ReasonPhrase = _reasonPhrase,
                StatusLine = _statusLine,
                Protocol = info?.Protocol ?? GetProtocolName(_statusProtocol),
                RemoteAddress = info?.RemoteAddress,
                ContentType = FindLast(responseHeaders, ContentTypeHeader),
                RequestLine = _requestLine,
                RequestHeaders = requestHeaders,
                AreRequestHeadersProvisional = _sentRequestHeaders is null,
                ResponseHeaders = responseHeaders,
                RequestCookies = JoinAll(requestHeaders, CookieHeader, "; "),
                ResponseCookies = FindAll(responseHeaders, SetCookieHeader),
                RequestBodySize = _requestBody.TotalBytes,
                CapturedRequestBodySize = _requestBody.CapturedBytes,
                IsRequestBodyTruncated = _requestBody.IsTruncated,
                ResponseBodySize = _responseBody.TotalBytes,
                CapturedResponseBodySize = _responseBody.CapturedBytes,
                IsResponseBodyTruncated = _responseBody.IsTruncated,
                ResponseHeaderSize = Math.Max(_receivedHeaderBytes, info?.HeaderSize ?? 0),
                RedirectCount = info?.RedirectCount ?? 0,
                ConnectionId = info?.ConnectionId,
                // A connect that failed opened nothing yet, so only a request that got a response was really reused.
                IsConnectionReused = info?.ConnectionId is not null && _firstResponseByteTimestamp != 0 ? info.Value.NewConnections == 0 : null,
                Error = _error,
                Timings = info?.Timings,
                QueuedTimestamp = _queuedTimestamp,
                SubmittedTimestamp = _submittedTimestamp,
                FirstResponseByteTimestamp = _firstResponseByteTimestamp,
                EndTimestamp = _endTimestamp,
            };
        }

        /// <summary>
        /// Gets the display name of a URL the way browsers show it in their network panels.
        /// </summary>
        /// <param name="uri">The request URI, or <see langword="null"/>.</param>
        /// <param name="url">The URL text used when <paramref name="uri"/> is not absolute.</param>
        /// <returns>The last path segment (with a trailing slash if the path has one) and the query, or the host for the root path.</returns>
        internal static string GetName(Uri? uri, string url)
        {
            if (uri is null || !uri.IsAbsoluteUri)
            {
                return url;
            }
            var path = uri.AbsolutePath;
            var hasTrailingSlash = path.Length > 1 && path.EndsWith("/", StringComparison.Ordinal);
            var trimmed = path.TrimEnd('/');
            var segment = trimmed.Substring(trimmed.LastIndexOf('/') + 1);
            if (segment.Length == 0)
            {
                return uri.Host + uri.Query;
            }

            return Uri.UnescapeDataString(segment) + (hasTrailingSlash ? "/" : string.Empty) + uri.Query;
        }

        /// <summary>
        /// Maps the protocol token of a status line to its ALPN name.
        /// </summary>
        /// <param name="statusProtocol">The token, such as <c>HTTP/1.1</c> or <c>HTTP/2</c>.</param>
        /// <returns>The ALPN name, the lower-cased token when it is unknown, or <see langword="null"/>.</returns>
        internal static string? GetProtocolName(string? statusProtocol)
        {
            switch (statusProtocol)
            {
                case null:
                case "":
                    return null;
                case "HTTP/1.0":
                    return "http/1.0";
                case "HTTP/1.1":
                    return "http/1.1";
                case "HTTP/2":
                case "HTTP/2.0":
                    return "h2";
                case "HTTP/3":
                    return "h3";
                default:
                    return statusProtocol.ToLowerInvariant();
            }
        }

        /// <summary>
        /// Splits a status line such as <c>HTTP/1.1 404 Not Found</c>.
        /// </summary>
        /// <param name="line">The status line.</param>
        /// <param name="protocol">Receives the protocol token.</param>
        /// <param name="statusCode">Receives the status code, or 0 when it is not a number.</param>
        /// <param name="reasonPhrase">Receives the reason phrase, or <see langword="null"/> when there is none.</param>
        internal static void ParseStatusLine(string line, out string? protocol, out int statusCode, out string? reasonPhrase)
        {
            var parts = line.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
            protocol = parts.Length > 0 ? parts[0] : null;
            statusCode = parts.Length > 1 && int.TryParse(parts[1], out var code) ? code : 0;
            reasonPhrase = parts.Length > 2 ? parts[2].Trim() : null;
        }

        /// <summary>
        /// Splits a <c>Name: value</c> header line.
        /// </summary>
        /// <param name="line">The header line without its terminator.</param>
        /// <param name="header">Receives the trimmed name and value.</param>
        /// <returns><see langword="true"/> when the line has a non-empty name followed by a colon.</returns>
        internal static bool TryParseHeader(string line, out KeyValuePair<string, string> header)
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                header = default;
                return false;
            }
            var name = line.Substring(0, colon).Trim();
            if (name.Length == 0)
            {
                header = default;
                return false;
            }
            header = new KeyValuePair<string, string>(name, line.Substring(colon + 1).Trim());

            return true;
        }

        /// <summary>
        /// Collects the request headers known before the request is sent.
        /// </summary>
        /// <param name="request">The request message.</param>
        /// <param name="containerCookies">The <c>Cookie</c> header the cookie container adds, or <see langword="null"/>.</param>
        /// <returns>The message headers, the content headers and the container's cookies.</returns>
        private static KeyValuePair<string, string>[] CollectRequestHeaders(HttpRequestMessage request, string? containerCookies)
        {
            var headers = new List<KeyValuePair<string, string>>();
            foreach (var header in request.Headers)
            {
                headers.Add(new KeyValuePair<string, string>(header.Key, string.Join(", ", header.Value)));
            }
            if (request.Content is not null)
            {
                foreach (var header in request.Content.Headers)
                {
                    headers.Add(new KeyValuePair<string, string>(header.Key, string.Join(", ", header.Value)));
                }
            }
            if (!string.IsNullOrEmpty(containerCookies))
            {
                headers.Add(new KeyValuePair<string, string>(CookieHeader, containerCookies!));
            }

            return headers.ToArray();
        }

        /// <summary>
        /// Finds the value of the last header with the given name.
        /// </summary>
        /// <param name="headers">The headers to search.</param>
        /// <param name="name">The header name, compared case-insensitively.</param>
        /// <returns>The value, or <see langword="null"/> when the header is missing.</returns>
        private static string? FindLast(KeyValuePair<string, string>[] headers, string name)
        {
            for (var i = headers.Length - 1; i >= 0; i--)
            {
                if (string.Equals(headers[i].Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return headers[i].Value;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the values of every header with the given name.
        /// </summary>
        /// <param name="headers">The headers to search.</param>
        /// <param name="name">The header name, compared case-insensitively.</param>
        /// <returns>The values in header order.</returns>
        private static string[] FindAll(KeyValuePair<string, string>[] headers, string name)
        {
            var values = default(List<string>);
            foreach (var header in headers)
            {
                if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    values ??= new List<string>();
                    values.Add(header.Value);
                }
            }

            return values?.ToArray() ?? Array.Empty<string>();
        }

        /// <summary>
        /// Joins the values of every header with the given name.
        /// </summary>
        /// <param name="headers">The headers to search.</param>
        /// <param name="name">The header name, compared case-insensitively.</param>
        /// <param name="separator">The separator placed between values.</param>
        /// <returns>The joined values, or <see langword="null"/> when the header is missing.</returns>
        private static string? JoinAll(KeyValuePair<string, string>[] headers, string name, string separator)
        {
            var values = FindAll(headers, name);

            return values.Length == 0 ? null : string.Join(separator, values);
        }
    }
}
