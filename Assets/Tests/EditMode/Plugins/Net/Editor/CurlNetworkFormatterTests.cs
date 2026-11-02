#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MajdataPlay.Net.Curl.Diagnostics;
using MajdataPlay.Net.Editor;
using MajdataPlay.Tests.Plugins.Net.Curl.Diagnostics;
using NUnit.Framework;

namespace MajdataPlay.Tests.Plugins.Net.Editor
{
    /// <summary>
    /// Covers the formatting and parsing helpers of the Curl Network window.
    /// </summary>
    public class CurlNetworkFormatterTests
    {
        /// <summary>Checks byte counts with binary units.</summary>
        /// <param name="bytes">The byte count.</param>
        /// <param name="expected">The expected text.</param>
        [TestCase(0L, "0 B")]
        [TestCase(1023L, "1023 B")]
        [TestCase(1536L, "1.5 KiB")]
        [TestCase(5L * 1024 * 1024, "5.0 MiB")]
        [TestCase(3L * 1024 * 1024 * 1024, "3.00 GiB")]
        public void FormatSize_UsesBinaryUnits(long bytes, string expected)
        {
            Assert.That(CurlNetworkFormatter.FormatSize(bytes), Is.EqualTo(expected));
        }

        /// <summary>Checks the precision of durations of every magnitude.</summary>
        /// <param name="milliseconds">The duration in milliseconds.</param>
        /// <param name="expected">The expected text.</param>
        [TestCase(0.25, "0.25 ms")]
        [TestCase(8.46, "8.5 ms")]
        [TestCase(120.4, "120 ms")]
        [TestCase(1250, "1.25 s")]
        [TestCase(125_000, "2 min 5 s")]
        [TestCase(-3, "0.00 ms")]
        public void FormatDuration_AdaptsPrecision(double milliseconds, string expected)
        {
            // Mono's TimeSpan.FromMilliseconds rounds to whole milliseconds, so the duration is built from ticks.
            var duration = TimeSpan.FromTicks((long)(milliseconds * TimeSpan.TicksPerMillisecond));

            Assert.That(CurlNetworkFormatter.FormatDuration(duration), Is.EqualTo(expected));
        }

        /// <summary>Checks that a missing duration is shown as a dash.</summary>
        [Test]
        public void FormatDuration_ShowsDashForMissing()
        {
            Assert.That(CurlNetworkFormatter.FormatDuration(null), Is.EqualTo("-"));
        }

        /// <summary>Checks the Status column for every stage, and the status with its reason phrase.</summary>
        [Test]
        public void FormatStatus_DescribesStage()
        {
            Assert.That(CurlNetworkFormatter.FormatStatus(new CurlNetworkSnapshot { State = CurlNetworkRequestState.Queued }), Is.EqualTo("(queued)"));
            Assert.That(CurlNetworkFormatter.FormatStatus(new CurlNetworkSnapshot { State = CurlNetworkRequestState.Running }), Is.EqualTo("(pending)"));
            Assert.That(CurlNetworkFormatter.FormatStatus(new CurlNetworkSnapshot { State = CurlNetworkRequestState.Running, StatusCode = 200 }), Is.EqualTo("200"));
            Assert.That(CurlNetworkFormatter.FormatStatus(new CurlNetworkSnapshot { State = CurlNetworkRequestState.Failed, StatusCode = 200 }), Is.EqualTo("(failed)"));
            Assert.That(CurlNetworkFormatter.FormatStatus(new CurlNetworkSnapshot { State = CurlNetworkRequestState.Cancelled }), Is.EqualTo("(canceled)"));
            Assert.That(CurlNetworkFormatter.FormatStatusWithReason(new CurlNetworkSnapshot { State = CurlNetworkRequestState.Completed, StatusCode = 404, ReasonPhrase = "Not Found" }), Is.EqualTo("404 Not Found"));
            Assert.That(CurlNetworkFormatter.FormatStatusWithReason(new CurlNetworkSnapshot { State = CurlNetworkRequestState.Completed, StatusCode = 204 }), Is.EqualTo("204"));
            Assert.That(CurlNetworkFormatter.FormatStatusWithReason(new CurlNetworkSnapshot { State = CurlNetworkRequestState.Queued }), Is.EqualTo("(queued)"));
        }

        /// <summary>Checks which requests count as errors.</summary>
        [Test]
        public void IsError_CoversFailuresAndErrorStatuses()
        {
            Assert.That(CurlNetworkFormatter.IsError(new CurlNetworkSnapshot { State = CurlNetworkRequestState.Completed, StatusCode = 200 }), Is.False);
            Assert.That(CurlNetworkFormatter.IsError(new CurlNetworkSnapshot { State = CurlNetworkRequestState.Completed, StatusCode = 404 }), Is.True);
            Assert.That(CurlNetworkFormatter.IsError(new CurlNetworkSnapshot { State = CurlNetworkRequestState.Failed }), Is.True);
            Assert.That(CurlNetworkFormatter.IsError(new CurlNetworkSnapshot { State = CurlNetworkRequestState.Cancelled }), Is.True);
            Assert.That(CurlNetworkFormatter.IsError(new CurlNetworkSnapshot { State = CurlNetworkRequestState.Running }), Is.False);
        }

        /// <summary>Checks the short types of common content types.</summary>
        /// <param name="contentType">The <c>Content-Type</c>.</param>
        /// <param name="expected">The expected type.</param>
        [TestCase("application/json; charset=utf-8", "json")]
        [TestCase("application/vnd.api+json", "json")]
        [TestCase("image/PNG", "png")]
        [TestCase("text/plain", "text")]
        [TestCase("application/octet-stream", "binary")]
        [TestCase("application/x-www-form-urlencoded", "www-form-urlencoded")]
        [TestCase(null, "")]
        [TestCase("  ", "")]
        public void GetShortType_UsesSubtype(string? contentType, string expected)
        {
            Assert.That(CurlNetworkFormatter.GetShortType(contentType), Is.EqualTo(expected));
        }

        /// <summary>Checks that the text filter matches URL, method, status, type, protocol and address case-insensitively.</summary>
        [Test]
        public void MatchesFilter_SearchesSeveralFields()
        {
            var snapshot = new CurlNetworkSnapshot
            {
                State = CurlNetworkRequestState.Completed,
                Url = "http://example.test/api/Charts",
                Method = "POST",
                StatusCode = 418,
                ContentType = "application/json",
                Protocol = "h2",
                RemoteAddress = "10.1.2.3:443",
            };

            Assert.That(CurlNetworkFormatter.MatchesFilter(snapshot, string.Empty), Is.True);
            Assert.That(CurlNetworkFormatter.MatchesFilter(snapshot, " charts "), Is.True);
            Assert.That(CurlNetworkFormatter.MatchesFilter(snapshot, "post"), Is.True);
            Assert.That(CurlNetworkFormatter.MatchesFilter(snapshot, "418"), Is.True);
            Assert.That(CurlNetworkFormatter.MatchesFilter(snapshot, "JSON"), Is.True);
            Assert.That(CurlNetworkFormatter.MatchesFilter(snapshot, "h2"), Is.True);
            Assert.That(CurlNetworkFormatter.MatchesFilter(snapshot, "10.1.2"), Is.True);
            Assert.That(CurlNetworkFormatter.MatchesFilter(snapshot, "missing"), Is.False);
        }

        /// <summary>Checks parsing of the <c>Cookie</c> request header.</summary>
        [Test]
        public void ParseRequestCookies_SplitsPairs()
        {
            var cookies = CurlNetworkFormatter.ParseRequestCookies("a=1; b = two=2 ;; flag");

            Assert.That(cookies.Select(x => x.Name), Is.EqualTo(new[] { "a", "b", "flag" }));
            Assert.That(cookies.Select(x => x.Value), Is.EqualTo(new[] { "1", "two=2", string.Empty }));
            Assert.That(CurlNetworkFormatter.ParseRequestCookies(null), Is.Empty);
            Assert.That(CurlNetworkFormatter.ParseRequestCookies(" "), Is.Empty);
        }

        /// <summary>Checks parsing of a <c>Set-Cookie</c> header with every known attribute.</summary>
        [Test]
        public void ParseSetCookie_ReadsAttributes()
        {
            var cookie = CurlNetworkFormatter.ParseSetCookie(
                "sid=a=b; Domain=example.test; path=/api; Expires=Wed, 01 Jan 2031 00:00:00 GMT; Max-Age=60; HttpOnly; secure; SameSite=Lax; Unknown=1");

            Assert.That(cookie.Name, Is.EqualTo("sid"));
            Assert.That(cookie.Value, Is.EqualTo("a=b"));
            Assert.That(cookie.Domain, Is.EqualTo("example.test"));
            Assert.That(cookie.Path, Is.EqualTo("/api"));
            Assert.That(cookie.Expires, Is.EqualTo("Wed, 01 Jan 2031 00:00:00 GMT"));
            Assert.That(cookie.MaxAge, Is.EqualTo("60"));
            Assert.That(cookie.HttpOnly, Is.True);
            Assert.That(cookie.Secure, Is.True);
            Assert.That(cookie.SameSite, Is.EqualTo("Lax"));
        }

        /// <summary>Checks a <c>Set-Cookie</c> header without attributes.</summary>
        [Test]
        public void ParseSetCookie_WithoutAttributes()
        {
            var cookie = CurlNetworkFormatter.ParseSetCookie("theme=dark");

            Assert.That(cookie.Name, Is.EqualTo("theme"));
            Assert.That(cookie.Value, Is.EqualTo("dark"));
            Assert.That(cookie.Domain, Is.Null);
            Assert.That(cookie.HttpOnly, Is.False);
            Assert.That(cookie.Secure, Is.False);
        }

        /// <summary>Checks query string parsing, decoding and URLs without a query.</summary>
        [Test]
        public void ParseQuery_DecodesParameters()
        {
            var query = CurlNetworkFormatter.ParseQuery("http://example.test/search?q=hello+world&tag=%E6%97%A5&empty=&flag");

            Assert.That(query, Is.EqualTo(new[]
            {
                new KeyValuePair<string, string>("q", "hello world"),
                new KeyValuePair<string, string>("tag", "\u65E5"),
                new KeyValuePair<string, string>("empty", string.Empty),
                new KeyValuePair<string, string>("flag", string.Empty),
            }));
            Assert.That(CurlNetworkFormatter.ParseQuery("http://example.test/"), Is.Empty);
            Assert.That(CurlNetworkFormatter.ParseQuery("not a url"), Is.Empty);
        }

        /// <summary>Checks that invalid percent-encoding is kept as it is.</summary>
        [Test]
        public void ParseFormUrlEncoded_KeepsInvalidEscapes()
        {
            var fields = CurlNetworkFormatter.ParseFormUrlEncoded("a=%ZZ&&b=%41");

            Assert.That(fields, Is.EqualTo(new[]
            {
                new KeyValuePair<string, string>("a", "%ZZ"),
                new KeyValuePair<string, string>("b", "A"),
            }));
        }

        /// <summary>Checks the wire format of header blocks.</summary>
        [Test]
        public void FormatHeaderBlock_JoinsLines()
        {
            var headers = new[] { new KeyValuePair<string, string>("Host", "example.test"), new KeyValuePair<string, string>("Accept", "*/*") };

            Assert.That(CurlNetworkFormatter.FormatHeaderBlock("GET / HTTP/1.1", headers), Is.EqualTo("GET / HTTP/1.1\nHost: example.test\nAccept: */*"));
            Assert.That(CurlNetworkFormatter.FormatHeaderBlock(null, headers), Is.EqualTo("Host: example.test\nAccept: */*"));
            Assert.That(CurlNetworkFormatter.FormatHeaderBlock(null, Array.Empty<KeyValuePair<string, string>>()), Is.Empty);
        }

        /// <summary>Checks binary detection from the content type and from the bytes.</summary>
        [Test]
        public void IsBinary_UsesContentTypeThenBytes()
        {
            var text = Encoding.ASCII.GetBytes("plain text\r\n\twith tabs");
            var withNull = new byte[] { 65, 0, 66 };

            Assert.That(CurlNetworkFormatter.IsBinary(withNull, "application/json"), Is.False);
            Assert.That(CurlNetworkFormatter.IsBinary(text, "image/png"), Is.True);
            Assert.That(CurlNetworkFormatter.IsBinary(text, "application/octet-stream"), Is.True);
            Assert.That(CurlNetworkFormatter.IsBinary(text, null), Is.False);
            Assert.That(CurlNetworkFormatter.IsBinary(withNull, null), Is.True);
            Assert.That(CurlNetworkFormatter.IsBinary(new byte[] { 1, 2, 3, 4 }, "application/x-unknown"), Is.True);
            Assert.That(CurlNetworkFormatter.IsBinary(Array.Empty<byte>(), null), Is.False);
        }

        /// <summary>Checks JSON detection from the content type and from the text.</summary>
        [Test]
        public void IsJson_UsesContentTypeOrText()
        {
            Assert.That(CurlNetworkFormatter.IsJson("application/problem+json", "x"), Is.True);
            Assert.That(CurlNetworkFormatter.IsJson("text/plain", "  [1]"), Is.True);
            Assert.That(CurlNetworkFormatter.IsJson(null, "{\"a\":1}"), Is.True);
            Assert.That(CurlNetworkFormatter.IsJson("text/plain", "hello"), Is.False);
        }

        /// <summary>Checks decoding with the declared charset, UTF-8 by default, and that a byte order mark is dropped.</summary>
        [Test]
        public void DecodeText_UsesCharset()
        {
            var latin1 = new byte[] { 0x63, 0x61, 0x66, 0xE9 };
            var utf8 = new byte[] { 0xEF, 0xBB, 0xBF, 0x68, 0x69 };

            Assert.That(CurlNetworkFormatter.DecodeText(latin1, "text/plain; charset=\"iso-8859-1\""), Is.EqualTo("caf\u00E9"));
            Assert.That(CurlNetworkFormatter.DecodeText(utf8, "text/plain"), Is.EqualTo("hi"));
            Assert.That(CurlNetworkFormatter.DecodeText(Encoding.UTF8.GetBytes("ok"), "text/plain; charset=no-such-charset"), Is.EqualTo("ok"));
        }

        /// <summary>Checks JSON re-indentation, including strings with structural characters and empty containers.</summary>
        [Test]
        public void PrettyPrintJson_Indents()
        {
            var json = "{\"a\":[1,2],\"b\":{},\"c\":\"x,{y}:\\\"z\\\"\",\"d\":[ ]}";

            var pretty = CurlNetworkFormatter.PrettyPrintJson(json);

            Assert.That(pretty, Is.EqualTo(
                "{\n" +
                "  \"a\": [\n" +
                "    1,\n" +
                "    2\n" +
                "  ],\n" +
                "  \"b\": {},\n" +
                "  \"c\": \"x,{y}:\\\"z\\\"\",\n" +
                "  \"d\": []\n" +
                "}"));
        }

        /// <summary>Checks that truncated or malformed JSON still formats without throwing.</summary>
        [Test]
        public void PrettyPrintJson_ToleratesTruncation()
        {
            Assert.That(CurlNetworkFormatter.PrettyPrintJson("{\"a\":[1,"), Is.EqualTo("{\n  \"a\": [\n    1,\n    "));
            Assert.That(CurlNetworkFormatter.PrettyPrintJson("]]}"), Is.EqualTo("\n]\n]\n}"));
            Assert.That(CurlNetworkFormatter.PrettyPrintJson(string.Empty), Is.Empty);
        }

        /// <summary>Checks hex dump lines, including a partial last line.</summary>
        [Test]
        public void FormatHexLine_ShowsOffsetBytesAndText()
        {
            var data = Encoding.ASCII.GetBytes("Hello, hex dump!\n\x01Z");

            Assert.That(CurlNetworkFormatter.GetHexLineCount(data.Length), Is.EqualTo(2));
            Assert.That(CurlNetworkFormatter.GetHexLineCount(0), Is.Zero);
            Assert.That(CurlNetworkFormatter.FormatHexLine(data, 0),
                Is.EqualTo("00000000  48 65 6C 6C 6F 2C 20 68  65 78 20 64 75 6D 70 21  |Hello, hex dump!|"));
            Assert.That(CurlNetworkFormatter.FormatHexLine(data, 1),
                Is.EqualTo("00000010  0A 01 5A" + new string(' ', 42) + "|..Z|"));
        }

        /// <summary>Checks the timing phases of a finished request with libcurl details.</summary>
        [Test]
        public void GetTimingPhases_SplitsLibcurlPoints()
        {
            var queued = 1_000_000L;
            var snapshot = new CurlNetworkSnapshot
            {
                State = CurlNetworkRequestState.Completed,
                QueuedTimestamp = queued,
                SubmittedTimestamp = queued + CurlNetworkRecordTests.Ticks(5),
                FirstResponseByteTimestamp = queued + CurlNetworkRecordTests.Ticks(45),
                EndTimestamp = queued + CurlNetworkRecordTests.Ticks(100),
                Timings = new CurlNetworkTimings
                {
                    NameLookup = TimeSpan.FromMilliseconds(2),
                    Connect = TimeSpan.FromMilliseconds(10),
                    AppConnect = TimeSpan.Zero,
                    PreTransfer = TimeSpan.FromMilliseconds(11),
                    PostTransfer = TimeSpan.FromMilliseconds(11),
                    StartTransfer = TimeSpan.FromMilliseconds(40),
                    Total = TimeSpan.FromMilliseconds(70),
                },
            };

            var phases = CurlNetworkFormatter.GetTimingPhases(snapshot, queued + CurlNetworkRecordTests.Ticks(1000));

            Assert.That(phases.Select(x => x.Name), Is.EqualTo(new[]
            {
                "Queueing", "DNS Lookup", "Initial connection", "Request setup", "Waiting for server response", "Content Download",
            }));
            Assert.That(phases.Select(x => Math.Round(x.Duration.TotalMilliseconds, 2)), Is.EqualTo(new[] { 5d, 2d, 8d, 1d, 29d, 30d }));
            Assert.That(phases.Select(x => Math.Round(x.Start.TotalMilliseconds, 2)), Is.EqualTo(new[] { 0d, 5d, 7d, 15d, 16d, 45d }));
        }

        /// <summary>Checks the phases of a running request that is still waiting for its response.</summary>
        [Test]
        public void GetTimingPhases_RunningWithoutResponse()
        {
            var queued = 1_000_000L;
            var snapshot = new CurlNetworkSnapshot
            {
                State = CurlNetworkRequestState.Running,
                QueuedTimestamp = queued,
                SubmittedTimestamp = queued + CurlNetworkRecordTests.Ticks(10),
            };

            var phases = CurlNetworkFormatter.GetTimingPhases(snapshot, queued + CurlNetworkRecordTests.Ticks(60));

            Assert.That(phases.Select(x => x.Name), Is.EqualTo(new[] { "Queueing", "Waiting for server response" }));
            Assert.That(phases[1].Duration.TotalMilliseconds, Is.EqualTo(50).Within(0.01));
        }

        /// <summary>Checks the cURL command of a POST request with a text body.</summary>
        [Test]
        public void BuildCurlCommand_RepeatsRequest()
        {
            var snapshot = new CurlNetworkSnapshot
            {
                Method = "POST",
                Url = "http://example.test/it's",
                RequestHeaders = new[]
                {
                    new KeyValuePair<string, string>("Host", "example.test"),
                    new KeyValuePair<string, string>("Content-Type", "application/json"),
                    new KeyValuePair<string, string>("Content-Length", "9"),
                    new KeyValuePair<string, string>("X-Note", "it's"),
                },
            };

            var command = CurlNetworkFormatter.BuildCurlCommand(snapshot, Encoding.UTF8.GetBytes("{\"a\":\"'\"}"));

            Assert.That(command, Is.EqualTo(
                "curl 'http://example.test/it'\\''s' \\\n" +
                "  -X 'POST' \\\n" +
                "  -H 'Content-Type: application/json' \\\n" +
                "  -H 'X-Note: it'\\''s' \\\n" +
                "  --data-raw '{\"a\":\"'\\''\"}'"));
        }

        /// <summary>Checks that GET and HEAD need no method option and that truncated or binary bodies are left out.</summary>
        [Test]
        public void BuildCurlCommand_OmitsDefaultsAndUnsafeBodies()
        {
            var get = new CurlNetworkSnapshot { Method = "GET", Url = "http://example.test/" };
            var head = new CurlNetworkSnapshot { Method = "HEAD", Url = "http://example.test/" };
            var truncated = new CurlNetworkSnapshot { Method = "PUT", Url = "http://example.test/", IsRequestBodyTruncated = true };
            var binary = new CurlNetworkSnapshot
            {
                Method = "PUT",
                Url = "http://example.test/",
                RequestHeaders = new[] { new KeyValuePair<string, string>("Content-Type", "application/octet-stream") },
            };

            Assert.That(CurlNetworkFormatter.BuildCurlCommand(get, Array.Empty<byte>()), Is.EqualTo("curl 'http://example.test/'"));
            Assert.That(CurlNetworkFormatter.BuildCurlCommand(head, Array.Empty<byte>()), Is.EqualTo("curl 'http://example.test/' \\\n  --head"));
            Assert.That(CurlNetworkFormatter.BuildCurlCommand(truncated, new byte[] { 65 }), Does.Not.Contain("--data-raw"));
            Assert.That(CurlNetworkFormatter.BuildCurlCommand(binary, new byte[] { 65 }), Does.Not.Contain("--data-raw"));
        }
    }
}
