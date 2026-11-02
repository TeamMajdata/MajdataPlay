#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using MajdataPlay.Net.Curl.Diagnostics;
using NUnit.Framework;

namespace MajdataPlay.Tests.Plugins.Net.Curl.Diagnostics
{
    /// <summary>
    /// Covers <see cref="CurlNetworkRecord"/> without a transfer: what it collects from the request message, how it parses
    /// request heads and response header blocks, how it captures bodies within its limits, its states and its snapshots.
    /// </summary>
    public class CurlNetworkRecordTests
    {
        /// <summary>Restores the monitor after each test.</summary>
        private CurlNetworkMonitorIsolation _isolation = null!;

        /// <summary>Saves the monitor state.</summary>
        [SetUp]
        public void SetUp()
        {
            _isolation = new CurlNetworkMonitorIsolation();
        }

        /// <summary>Restores the monitor state.</summary>
        [TearDown]
        public void TearDown()
        {
            _isolation.Dispose();
        }

        /// <summary>Creates an unregistered record.</summary>
        /// <param name="request">The request message.</param>
        /// <param name="maxBodyBytes">The per-body capture limit.</param>
        /// <param name="cookies">The cookie header of the handler's cookie container.</param>
        /// <returns>The record.</returns>
        private static CurlNetworkRecord Create(HttpRequestMessage request, int maxBodyBytes = 1024, string? cookies = null)
        {
            return new CurlNetworkRecord(42, request, cookies, maxBodyBytes);
        }

        /// <summary>Creates an unregistered record of a GET request.</summary>
        /// <param name="maxBodyBytes">The per-body capture limit.</param>
        /// <returns>The record.</returns>
        private static CurlNetworkRecord CreateGet(int maxBodyBytes = 1024)
        {
            return Create(new HttpRequestMessage(HttpMethod.Get, "http://example.test/charts"), maxBodyBytes);
        }

        /// <summary>Checks that a new record takes the method, URL, name and provisional headers from the message.</summary>
        [Test]
        public void Constructor_CollectsRequestMessage()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "http://example.test/api/charts/list?page=2")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Client", "majdata");

            var snapshot = Create(request, cookies: "token=1").GetSnapshot();

            Assert.That(snapshot.Id, Is.EqualTo(42));
            Assert.That(snapshot.Method, Is.EqualTo("POST"));
            Assert.That(snapshot.Url, Is.EqualTo("http://example.test/api/charts/list?page=2"));
            Assert.That(snapshot.Name, Is.EqualTo("list?page=2"));
            Assert.That(snapshot.State, Is.EqualTo(CurlNetworkRequestState.Queued));
            Assert.That(snapshot.IsFinished, Is.False);
            Assert.That(snapshot.AreRequestHeadersProvisional, Is.True);
            var names = snapshot.RequestHeaders.Select(x => x.Key).ToArray();
            Assert.That(names.First(), Is.EqualTo("X-Client"), "Message headers come first.");
            Assert.That(names, Does.Contain("Content-Type"), "Content headers follow.");
            Assert.That(names.Last(), Is.EqualTo("Cookie"), "The container's cookies come last.");
            Assert.That(snapshot.RequestCookies, Is.EqualTo("token=1"));
            Assert.That(snapshot.StatusCode, Is.Zero);
            Assert.That(snapshot.Timings, Is.Null);
        }

        /// <summary>Checks the display names derived from URLs.</summary>
        /// <param name="url">The request URL.</param>
        /// <param name="expected">The expected name.</param>
        [TestCase("http://example.test/", "example.test")]
        [TestCase("http://example.test/?q=1", "example.test?q=1")]
        [TestCase("http://example.test/a/b", "b")]
        [TestCase("http://example.test/a/b/", "b/")]
        [TestCase("http://example.test/a/song%20name.mp3?x=1", "song name.mp3?x=1")]
        public void GetName_FollowsBrowserConventions(string url, string expected)
        {
            Assert.That(CurlNetworkRecord.GetName(new Uri(url), url), Is.EqualTo(expected));
        }

        /// <summary>Checks that relative and missing URIs fall back to the URL text.</summary>
        [Test]
        public void GetName_UsesUrlTextForRelativeUris()
        {
            Assert.That(CurlNetworkRecord.GetName(new Uri("/relative", UriKind.Relative), "/relative"), Is.EqualTo("/relative"));
            Assert.That(CurlNetworkRecord.GetName(null, string.Empty), Is.Empty);
        }

        /// <summary>Checks that the last request head libcurl sent replaces the provisional headers.</summary>
        [Test]
        public void OnRequestHeadersSent_KeepsLastHead()
        {
            var record = CreateGet();

            record.OnRequestHeadersSent(Encoding.ASCII.GetBytes("GET /start HTTP/1.1\r\nHost: example.test\r\nAccept: */*\r\n\r\n"));
            record.OnRequestHeadersSent(Encoding.ASCII.GetBytes("GET /final HTTP/1.1\r\nHost: example.test\r\nCookie: a=1; b=2\r\nbroken line\r\n\r\n"));
            var snapshot = record.GetSnapshot();

            Assert.That(snapshot.AreRequestHeadersProvisional, Is.False);
            Assert.That(snapshot.RequestLine, Is.EqualTo("GET /final HTTP/1.1"));
            Assert.That(snapshot.RequestHeaders, Is.EqualTo(new[]
            {
                new KeyValuePair<string, string>("Host", "example.test"),
                new KeyValuePair<string, string>("Cookie", "a=1; b=2"),
            }));
            Assert.That(snapshot.RequestCookies, Is.EqualTo("a=1; b=2"));
        }

        /// <summary>Checks that only the final header block is kept while every received header byte is counted.</summary>
        [Test]
        public void OnResponseHeaderLine_KeepsFinalBlock()
        {
            var record = CreateGet();
            var lines = new[]
            {
                "HTTP/1.1 307 Temporary Redirect\r\n", "Location: /final\r\n", "\r\n",
                "HTTP/1.1 200 OK\r\n", "Content-Type: application/json; charset=utf-8\r\n",
                "Set-Cookie: a=1; Path=/\r\n", "set-cookie: b=2\r\n", "\r\n",
            };

            foreach (var line in lines)
            {
                record.OnResponseHeaderLine(Encoding.ASCII.GetBytes(line));
            }
            var snapshot = record.GetSnapshot();

            Assert.That(snapshot.StatusCode, Is.EqualTo(200));
            Assert.That(snapshot.ReasonPhrase, Is.EqualTo("OK"));
            Assert.That(snapshot.StatusLine, Is.EqualTo("HTTP/1.1 200 OK"));
            Assert.That(snapshot.Protocol, Is.EqualTo("http/1.1"));
            Assert.That(snapshot.ResponseHeaders.Select(x => x.Key), Is.EqualTo(new[] { "Content-Type", "Set-Cookie", "set-cookie" }));
            Assert.That(snapshot.ContentType, Is.EqualTo("application/json; charset=utf-8"));
            Assert.That(snapshot.ResponseCookies, Is.EqualTo(new[] { "a=1; Path=/", "b=2" }));
            Assert.That(snapshot.ResponseHeaderSize, Is.EqualTo(lines.Sum(x => x.Length)));
            Assert.That(snapshot.TransferredSize, Is.EqualTo(snapshot.ResponseHeaderSize));
        }

        /// <summary>Checks status lines of HTTP/2, which have no reason phrase.</summary>
        [Test]
        public void OnResponseHeaderLine_ParsesHttp2StatusLine()
        {
            var record = CreateGet();

            CurlNetworkMonitorIsolation.ReceiveHeaders(record, "HTTP/2 204");
            var snapshot = record.GetSnapshot();

            Assert.That(snapshot.StatusCode, Is.EqualTo(204));
            Assert.That(snapshot.ReasonPhrase, Is.Null);
            Assert.That(snapshot.Protocol, Is.EqualTo("h2"));
        }

        /// <summary>Checks status line splitting, including malformed codes.</summary>
        /// <param name="line">The status line.</param>
        /// <param name="protocol">The expected protocol token.</param>
        /// <param name="code">The expected status code.</param>
        /// <param name="reason">The expected reason phrase.</param>
        [TestCase("HTTP/1.1 404 Not Found", "HTTP/1.1", 404, "Not Found")]
        [TestCase("HTTP/1.0   301   Moved", "HTTP/1.0", 301, "Moved")]
        [TestCase("HTTP/1.1 2x0 Bad", "HTTP/1.1", 0, "Bad")]
        [TestCase("HTTP/1.1", "HTTP/1.1", 0, null)]
        public void ParseStatusLine_SplitsParts(string line, string protocol, int code, string? reason)
        {
            CurlNetworkRecord.ParseStatusLine(line, out var parsedProtocol, out var parsedCode, out var parsedReason);

            Assert.That(parsedProtocol, Is.EqualTo(protocol));
            Assert.That(parsedCode, Is.EqualTo(code));
            Assert.That(parsedReason, Is.EqualTo(reason));
        }

        /// <summary>Checks header line splitting and rejection of lines without a name.</summary>
        [Test]
        public void TryParseHeader_SplitsAtFirstColon()
        {
            Assert.That(CurlNetworkRecord.TryParseHeader(" X-Time :  12:00 ", out var header), Is.True);
            Assert.That(header, Is.EqualTo(new KeyValuePair<string, string>("X-Time", "12:00")));
            Assert.That(CurlNetworkRecord.TryParseHeader(": value", out _), Is.False);
            Assert.That(CurlNetworkRecord.TryParseHeader("  : value", out _), Is.False);
            Assert.That(CurlNetworkRecord.TryParseHeader("no colon", out _), Is.False);
        }

        /// <summary>Checks the ALPN names of status line protocol tokens.</summary>
        [Test]
        public void GetProtocolName_MapsStatusLineTokens()
        {
            Assert.That(CurlNetworkRecord.GetProtocolName(null), Is.Null);
            Assert.That(CurlNetworkRecord.GetProtocolName(string.Empty), Is.Null);
            Assert.That(CurlNetworkRecord.GetProtocolName("HTTP/1.0"), Is.EqualTo("http/1.0"));
            Assert.That(CurlNetworkRecord.GetProtocolName("HTTP/2.0"), Is.EqualTo("h2"));
            Assert.That(CurlNetworkRecord.GetProtocolName("HTTP/3"), Is.EqualTo("h3"));
            Assert.That(CurlNetworkRecord.GetProtocolName("RTSP/1.0"), Is.EqualTo("rtsp/1.0"));
        }

        /// <summary>Checks that a body is kept up to the per-body limit while every byte is counted.</summary>
        [Test]
        public void ResponseBody_IsKeptUpToLimit()
        {
            var record = CreateGet(maxBodyBytes: 8);

            record.OnResponseBodyReceived(Encoding.ASCII.GetBytes("01234"));
            record.OnResponseBodyReceived(Encoding.ASCII.GetBytes("56789"));
            record.OnResponseBodyReceived(ReadOnlySpan<byte>.Empty);
            var snapshot = record.GetSnapshot();

            Assert.That(snapshot.ResponseBodySize, Is.EqualTo(10));
            Assert.That(snapshot.CapturedResponseBodySize, Is.EqualTo(8));
            Assert.That(snapshot.IsResponseBodyTruncated, Is.True);
            Assert.That(Encoding.ASCII.GetString(record.GetResponseBody()), Is.EqualTo("01234567"));
        }

        /// <summary>Checks that a zero limit keeps nothing but still counts the body.</summary>
        [Test]
        public void ResponseBody_ZeroLimitOnlyCounts()
        {
            var record = CreateGet(maxBodyBytes: 0);

            record.OnResponseBodyReceived(new byte[100]);
            var snapshot = record.GetSnapshot();

            Assert.That(snapshot.ResponseBodySize, Is.EqualTo(100));
            Assert.That(snapshot.CapturedResponseBodySize, Is.Zero);
            Assert.That(record.GetResponseBody(), Is.Empty);
        }

        /// <summary>Checks that a rewound upload replaces the bytes sent before the rewind.</summary>
        [Test]
        public void RequestBody_RewindDropsResentBytes()
        {
            var record = CreateGet();

            record.OnRequestBodyRead(Encoding.ASCII.GetBytes("abcd"));
            record.OnRequestBodyRewound(1);
            Assert.That(Encoding.ASCII.GetString(record.GetRequestBody()), Is.EqualTo("a"));
            record.OnRequestBodyRewound(0);
            record.OnRequestBodyRead(Encoding.ASCII.GetBytes("abcd"));
            var snapshot = record.GetSnapshot();

            Assert.That(Encoding.ASCII.GetString(record.GetRequestBody()), Is.EqualTo("abcd"));
            Assert.That(snapshot.RequestBodySize, Is.EqualTo(4));
            Assert.That(snapshot.IsRequestBodyTruncated, Is.False);
        }

        /// <summary>Checks that the shared budget caps every capture and that releasing a record returns its bytes.</summary>
        [Test]
        public void BodyCapture_RespectsSharedBudget()
        {
            var baseline = CurlNetworkMonitor.CapturedBodyBytes;
            CurlNetworkMonitor.MaxTotalBodyCaptureBytes = baseline + 6;
            var first = CreateGet();
            var second = CreateGet();

            first.OnResponseBodyReceived(new byte[4]);
            second.OnResponseBodyReceived(new byte[4]);
            second.OnResponseBodyReceived(new byte[1]);

            Assert.That(first.GetSnapshot().CapturedResponseBodySize, Is.EqualTo(4));
            Assert.That(second.GetSnapshot().CapturedResponseBodySize, Is.Zero, "A capture stops once the budget refuses it.");
            Assert.That(second.GetSnapshot().ResponseBodySize, Is.EqualTo(5));
            Assert.That(CurlNetworkMonitor.CapturedBodyBytes, Is.EqualTo(baseline + 4));

            first.Release();

            Assert.That(CurlNetworkMonitor.CapturedBodyBytes, Is.EqualTo(baseline));
            Assert.That(first.GetResponseBody(), Is.Empty);
            first.OnResponseBodyReceived(new byte[2]);
            Assert.That(CurlNetworkMonitor.CapturedBodyBytes, Is.EqualTo(baseline), "A released record keeps nothing.");
        }

        /// <summary>Checks the state transitions and that the first final state wins.</summary>
        [Test]
        public void States_FollowTransfer()
        {
            var record = CreateGet();

            record.OnSubmitted();
            Assert.That(record.GetSnapshot().State, Is.EqualTo(CurlNetworkRequestState.Running));
            record.OnSubmitted();
            record.OnCompleted();
            record.OnFailed(new InvalidOperationException("late"));
            record.OnCancelled();
            var snapshot = record.GetSnapshot();

            Assert.That(snapshot.State, Is.EqualTo(CurlNetworkRequestState.Completed));
            Assert.That(snapshot.IsFinished, Is.True);
            Assert.That(snapshot.Error, Is.Null);
        }

        /// <summary>Checks that failures keep their message and that disposal counts as cancellation.</summary>
        [Test]
        public void OnFailed_ClassifiesExceptions()
        {
            var failed = CreateGet();
            var disposed = CreateGet();
            var cancelled = CreateGet();

            failed.OnFailed(new InvalidOperationException("Curl Error 7"));
            disposed.OnFailed(new ObjectDisposedException("CurlMulti", "The HTTP handler was disposed."));
            cancelled.OnCancelled();

            Assert.That(failed.GetSnapshot().State, Is.EqualTo(CurlNetworkRequestState.Failed));
            Assert.That(failed.GetSnapshot().Error, Is.EqualTo("Curl Error 7"));
            Assert.That(disposed.GetSnapshot().State, Is.EqualTo(CurlNetworkRequestState.Cancelled));
            Assert.That(disposed.GetSnapshot().Error, Does.Contain("The HTTP handler was disposed."));
            Assert.That(cancelled.GetSnapshot().State, Is.EqualTo(CurlNetworkRequestState.Cancelled));
            Assert.That(cancelled.GetSnapshot().Error, Is.Not.Empty);
        }

        /// <summary>Checks that the snapshot is reused until the record changes, and that changes bump both versions.</summary>
        [Test]
        public void GetSnapshot_IsCachedUntilChange()
        {
            var record = CreateGet();
            var first = record.GetSnapshot();
            var monitorVersion = CurlNetworkMonitor.Version;

            Assert.That(record.GetSnapshot(), Is.SameAs(first));

            record.OnSubmitted();
            var second = record.GetSnapshot();

            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.Version, Is.GreaterThan(first.Version));
            Assert.That(record.Version, Is.EqualTo(second.Version));
            Assert.That(CurlNetworkMonitor.Version, Is.Not.EqualTo(monitorVersion));
        }

        /// <summary>Checks that libcurl's details take precedence over the parsed status line.</summary>
        [Test]
        public void OnTransferInfo_AddsConnectionDetails()
        {
            var record = CreateGet();
            CurlNetworkMonitorIsolation.ReceiveHeaders(record, "HTTP/1.1 200 OK");
            var timings = new CurlNetworkTimings { StartTransfer = TimeSpan.FromMilliseconds(30), Total = TimeSpan.FromMilliseconds(50) };

            record.OnTransferInfo(new CurlTransferInfo
            {
                ResponseCode = 201,
                Protocol = "h2",
                EffectiveUrl = "http://example.test/final",
                RemoteAddress = "[::1]:8443",
                HeaderSize = 4096,
                RedirectCount = 2,
                Timings = timings,
            });
            var snapshot = record.GetSnapshot();

            Assert.That(snapshot.StatusCode, Is.EqualTo(201));
            Assert.That(snapshot.Protocol, Is.EqualTo("h2"));
            Assert.That(snapshot.EffectiveUrl, Is.EqualTo("http://example.test/final"));
            Assert.That(snapshot.RemoteAddress, Is.EqualTo("[::1]:8443"));
            Assert.That(snapshot.ResponseHeaderSize, Is.EqualTo(4096));
            Assert.That(snapshot.RedirectCount, Is.EqualTo(2));
            Assert.That(snapshot.Timings!.Value.StartTransfer, Is.EqualTo(TimeSpan.FromMilliseconds(30)));
            Assert.That(snapshot.Ttfb, Is.EqualTo(TimeSpan.FromMilliseconds(30)));
        }

        /// <summary>Checks the protocol names of <c>CURLINFO_HTTP_VERSION</c> values.</summary>
        [Test]
        public void TransferInfo_MapsHttpVersions()
        {
            Assert.That(CurlTransferInfo.GetProtocolName(0), Is.Null);
            Assert.That(CurlTransferInfo.GetProtocolName(1), Is.EqualTo("http/1.0"));
            Assert.That(CurlTransferInfo.GetProtocolName(2), Is.EqualTo("http/1.1"));
            Assert.That(CurlTransferInfo.GetProtocolName(3), Is.EqualTo("h2"));
            Assert.That(CurlTransferInfo.GetProtocolName(30), Is.EqualTo("h3"));
            Assert.That(CurlTransferInfo.GetProtocolName(99), Is.Null);
        }

        /// <summary>Checks remote address formatting for IPv4, IPv6, unknown ports and missing addresses.</summary>
        [Test]
        public void TransferInfo_FormatsRemoteAddress()
        {
            Assert.That(CurlTransferInfo.FormatRemoteAddress("127.0.0.1", 80), Is.EqualTo("127.0.0.1:80"));
            Assert.That(CurlTransferInfo.FormatRemoteAddress("::1", 443), Is.EqualTo("[::1]:443"));
            Assert.That(CurlTransferInfo.FormatRemoteAddress("10.0.0.1", 0), Is.EqualTo("10.0.0.1"));
            Assert.That(CurlTransferInfo.FormatRemoteAddress(null, 80), Is.Null);
            Assert.That(CurlTransferInfo.FormatRemoteAddress(string.Empty, 80), Is.Null);
        }

        /// <summary>Checks that queueing, time to first byte and content download add up to the total of a finished request.</summary>
        [Test]
        public void Snapshot_FinishedTimingsAddUp()
        {
            var queued = 1_000_000L;
            var snapshot = new CurlNetworkSnapshot
            {
                State = CurlNetworkRequestState.Completed,
                QueuedTimestamp = queued,
                SubmittedTimestamp = queued + Ticks(100),
                FirstResponseByteTimestamp = queued + Ticks(200),
                EndTimestamp = queued + Ticks(500),
                Timings = new CurlNetworkTimings
                {
                    StartTransfer = TimeSpan.FromMilliseconds(120),
                    Total = TimeSpan.FromMilliseconds(300),
                },
            };
            var later = queued + Ticks(10_000);

            Assert.That(snapshot.GetQueueing(later).TotalMilliseconds, Is.EqualTo(100).Within(0.01));
            Assert.That(snapshot.Ttfb, Is.EqualTo(TimeSpan.FromMilliseconds(120)));
            Assert.That(snapshot.GetTotal(later).TotalMilliseconds, Is.EqualTo(400).Within(0.01), "libcurl's total plus queueing");
            Assert.That(snapshot.GetTransfer(later)!.Value.TotalMilliseconds, Is.EqualTo(180).Within(0.01));
        }

        /// <summary>Checks the managed fallback timings of a running request without libcurl details.</summary>
        [Test]
        public void Snapshot_RunningTimingsGrowWithTime()
        {
            var queued = 1_000_000L;
            var snapshot = new CurlNetworkSnapshot
            {
                State = CurlNetworkRequestState.Running,
                QueuedTimestamp = queued,
                SubmittedTimestamp = queued + Ticks(10),
                FirstResponseByteTimestamp = queued + Ticks(40),
            };

            Assert.That(snapshot.Ttfb!.Value.TotalMilliseconds, Is.EqualTo(30).Within(0.01));
            Assert.That(snapshot.GetTotal(queued + Ticks(100)).TotalMilliseconds, Is.EqualTo(100).Within(0.01));
            Assert.That(snapshot.GetTransfer(queued + Ticks(100))!.Value.TotalMilliseconds, Is.EqualTo(60).Within(0.01));
            Assert.That(snapshot.GetTransfer(queued + Ticks(200))!.Value.TotalMilliseconds, Is.EqualTo(160).Within(0.01));
        }

        /// <summary>Checks the timings of a request that is still queued, and of one that failed without libcurl details.</summary>
        [Test]
        public void Snapshot_TimingsBeforeFirstByte()
        {
            var queued = 1_000_000L;
            var waiting = new CurlNetworkSnapshot { State = CurlNetworkRequestState.Queued, QueuedTimestamp = queued };
            // libcurl reports a start-transfer time even for a refused connection, which received no byte at all.
            var failed = new CurlNetworkSnapshot
            {
                State = CurlNetworkRequestState.Failed,
                QueuedTimestamp = queued,
                EndTimestamp = queued + Ticks(70),
                Timings = new CurlNetworkTimings { StartTransfer = TimeSpan.FromMilliseconds(60) },
            };

            Assert.That(waiting.GetQueueing(queued + Ticks(25)).TotalMilliseconds, Is.EqualTo(25).Within(0.01));
            Assert.That(waiting.GetTotal(queued + Ticks(25)).TotalMilliseconds, Is.EqualTo(25).Within(0.01));
            Assert.That(waiting.Ttfb, Is.Null);
            Assert.That(waiting.GetTransfer(queued + Ticks(25)), Is.Null);
            Assert.That(failed.GetQueueing(queued + Ticks(1000)).TotalMilliseconds, Is.EqualTo(70).Within(0.01));
            Assert.That(failed.GetTotal(queued + Ticks(1000)).TotalMilliseconds, Is.EqualTo(70).Within(0.01));
            Assert.That(failed.Ttfb, Is.Null);
            Assert.That(failed.GetTransfer(queued + Ticks(1000)), Is.Null);
        }

        /// <summary>Checks the conversion of <see cref="Stopwatch"/> ticks.</summary>
        [Test]
        public void ToTimeSpan_ConvertsStopwatchTicks()
        {
            Assert.That(CurlNetworkSnapshot.ToTimeSpan(Stopwatch.Frequency).TotalSeconds, Is.EqualTo(1).Within(1e-6));
            Assert.That(CurlNetworkSnapshot.ToTimeSpan(-5), Is.EqualTo(TimeSpan.Zero));
        }

        /// <summary>Converts milliseconds to <see cref="Stopwatch"/> ticks.</summary>
        /// <param name="milliseconds">The duration in milliseconds.</param>
        /// <returns>The tick count.</returns>
        internal static long Ticks(double milliseconds)
        {
            return (long)(milliseconds * Stopwatch.Frequency / 1000d);
        }
    }
}
