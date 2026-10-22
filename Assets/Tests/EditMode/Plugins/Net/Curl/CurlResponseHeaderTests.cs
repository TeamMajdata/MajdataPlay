#nullable enable
using System;
using System.Linq;
using System.Net;
using MajdataPlay.Net.Curl.Core;
using MajdataPlay.Net.Curl.Core.PInvoke;
using NUnit.Framework;

namespace MajdataPlay.Tests.Plugins.Net.Curl
{
    /// <summary>Covers how the libcurl header callback parses status lines, header fields, cookies and header budgets.</summary>
    public class CurlResponseHeaderTests
    {
        /// <summary>The transfer whose header callback is exercised.</summary>
        private CurlTaskFixture _fixture = null!;

        /// <summary>The cookie container attached to the transfer settings.</summary>
        private CookieContainer _cookies = null!;

        /// <summary>Creates a submitted transfer with a 256-byte header budget and a cookie container.</summary>
        [SetUp]
        public void SetUp()
        {
            _cookies = new CookieContainer();
            _fixture = new CurlTaskFixture(CurlTaskFixture.Config(maxHeaders: 256, cookies: _cookies)).Submit();
        }

        /// <summary>Releases the transfer.</summary>
        [TearDown]
        public void TearDown()
        {
            _fixture.Dispose();
        }

        /// <summary>Gets the response under test.</summary>
        private CurlResponse Response => _fixture.Task.Response;

        /// <summary>Delivers a header line and asserts that libcurl is told to continue.</summary>
        /// <param name="line">The raw header line.</param>
        private void Accept(string line)
        {
            Assert.That(CurlCallbackInvoker.Header(_fixture.UserData, line), Is.EqualTo((UIntPtr)(uint)line.Length), line);
        }

        /// <summary>Checks that status lines of every HTTP version set the status code.</summary>
        /// <param name="line">The status line.</param>
        /// <param name="expected">The expected status code.</param>
        [TestCase("HTTP/1.1 404 Not Found\r\n", 404)]
        [TestCase("HTTP/2 204\r\n", 204)]
        [TestCase("HTTP/1.0   301 Moved\r\n", 301)]
        public void StatusLine_SetsStatusCode(string line, int expected)
        {
            Accept(line);

            Assert.That((int)Response.StatusCode, Is.EqualTo(expected));
        }

        /// <summary>Checks that malformed status lines leave the status unchanged.</summary>
        /// <param name="line">A malformed status line.</param>
        [TestCase("HTTP/1.1 2x0 Bad\r\n")]
        [TestCase("HTTP/1.1 20\r\n")]
        [TestCase("HTTP/1.1\r\n")]
        public void StatusLine_IgnoresMalformedCodes(string line)
        {
            Accept("HTTP/1.1 202 Accepted\r\n");

            Accept(line);

            Assert.That(Response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        }

        /// <summary>Checks that header names are matched case-insensitively and values are trimmed.</summary>
        [Test]
        public void HeaderFields_AreParsedAndNormalized()
        {
            Accept("HTTP/1.1 200 OK\r\n");
            Accept("server:  test-server  \r\n");
            Accept("DATE: Tue, 01 Jan 2030 00:00:00 GMT\r\n");
            Accept("cache-control: no-cache\r\n");
            Accept("CONNECTION: close\r\n");
            Accept("location: /next\r\n");
            Accept("Transfer-Encoding: chunked\r\n");
            Accept("X-Custom: custom value\r\n");

            Assert.That(Response.Headers.Server.ToString(), Is.EqualTo("test-server"));
            Assert.That(Response.Headers.Date, Is.Not.Null);
            Assert.That(Response.Headers.CacheControl!.NoCache, Is.True);
            Assert.That(Response.Headers.Connection, Does.Contain("close"));
            Assert.That(Response.Headers.Location!.OriginalString, Is.EqualTo("/next"));
            Assert.That(Response.Headers.TransferEncodingChunked, Is.True);
            Assert.That(Response.Headers.GetValues("X-Custom").Single(), Is.EqualTo("custom value"));
        }

        /// <summary>Checks that content headers fall back to the content header collection.</summary>
        [Test]
        public void ContentHeaders_AreStoredOnContent()
        {
            Accept("HTTP/1.1 200 OK\r\n");
            Accept("content-type: text/plain; charset=utf-8\r\n");
            Accept("Content-Length: 5\r\n");
            Accept("Content-Language: ja\r\n");

            Assert.That(Response.Content.Headers.ContentType!.MediaType, Is.EqualTo("text/plain"));
            Assert.That(Response.Content.Headers.ContentLength, Is.EqualTo(5));
            Assert.That(Response.Content.Headers.ContentLanguage, Does.Contain("ja"));
        }

        /// <summary>Checks that lines without a usable name are accepted but ignored.</summary>
        /// <param name="line">A header line without a name.</param>
        [TestCase("NoColonHere\r\n")]
        [TestCase(": value\r\n")]
        [TestCase("   : value\r\n")]
        public void InvalidFields_AreIgnored(string line)
        {
            Accept("HTTP/1.1 200 OK\r\n");

            Accept(line);

            Assert.That(Response.Headers, Is.Empty);
            Assert.That(Response.Content.Headers, Is.Empty);
        }

        /// <summary>Checks that a new status line, such as after a redirect, clears the headers of the previous block.</summary>
        [Test]
        public void NewStatusLine_ClearsPreviousHeaders()
        {
            Accept("HTTP/1.1 302 Found\r\n");
            Accept("Location: /final\r\n");
            Accept("Content-Type: text/html\r\n");
            Accept("\r\n");

            Accept("HTTP/1.1 200 OK\r\n");

            Assert.That(Response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Response.Headers.Location, Is.Null);
            Assert.That(Response.Content.Headers.ContentType, Is.Null);
        }

        /// <summary>Checks that cookies are stored in the container and invalid cookies are ignored.</summary>
        [Test]
        public void SetCookie_UpdatesCookieContainer()
        {
            var uri = new Uri("http://127.0.0.1:9/resource");
            Accept("HTTP/1.1 200 OK\r\n");

            Accept("Set-Cookie: session=abc; Path=/\r\n");
            Accept("set-cookie: bad=1; Domain=other.invalid\r\n");

            Assert.That(_cookies.GetCookies(uri)["session"]!.Value, Is.EqualTo("abc"));
            Assert.That(_cookies.GetCookies(uri)["bad"], Is.Null);
            Assert.That(Response.Headers.GetValues("Set-Cookie").Count(), Is.EqualTo(2));
        }

        /// <summary>Checks that one header block may not exceed the budget, which resets for each new block.</summary>
        [Test]
        public void HeaderBudget_AppliesPerBlock()
        {
            var filler = "X-Fill: " + new string('a', 150) + "\r\n";
            Accept("HTTP/1.1 100 Continue\r\n");
            Accept(filler);
            Accept("\r\n");
            Accept("HTTP/1.1 200 OK\r\n");
            Accept(filler);

            var rejected = CurlCallbackInvoker.Header(_fixture.UserData, filler);

            Assert.That(rejected, Is.EqualTo(UIntPtr.Zero));
        }

        /// <summary>Checks that trailers after the body started are accepted but never added to the published headers.</summary>
        [Test]
        public void Trailers_AreCountedButNotParsed()
        {
            Accept("HTTP/1.1 200 OK\r\n");
            Accept("\r\n");
            _fixture.Task.TryEnterHeaderReadState();

            Accept("X-Checksum: 1234\r\n");

            Assert.That(Response.Headers.Contains("X-Checksum"), Is.False);
        }

        /// <summary>Checks that headers are refused for unknown user data and transfers that are no longer running.</summary>
        [Test]
        public void HeaderCallback_RejectsUnknownOrFinishedTransfers()
        {
            using var created = new CurlTaskFixture(CurlTaskFixture.Config());
            _fixture.Task.TryEnterHeaderReadState();
            _fixture.Task.TryEnterCompletedState(CurlCode.Ok);

            Assert.That(CurlCallbackInvoker.Header(IntPtr.Zero, "X: y\r\n"), Is.EqualTo(UIntPtr.Zero));
            Assert.That(CurlCallbackInvoker.Header(created.UserData, "X: y\r\n"), Is.EqualTo(UIntPtr.Zero));
            Assert.That(CurlCallbackInvoker.Header(_fixture.UserData, "X: y\r\n"), Is.EqualTo(UIntPtr.Zero));
        }
    }
}
