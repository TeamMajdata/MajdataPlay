#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MajdataPlay.Net.Curl;
using MajdataPlay.Net.Curl.Diagnostics;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MajdataPlay.Tests.Plugins.Net.Curl.Diagnostics
{
    /// <summary>
    /// Covers what <see cref="CurlNetworkMonitor"/> records of real exchanges through <see cref="CurlHttpMessageHandler"/>
    /// with a server on 127.0.0.1 that the test starts itself: headers on the wire, bodies, cookies, redirects, timings,
    /// failures, cancellations and requests that are still running.
    /// </summary>
    public class CurlNetworkCaptureTests
    {
        /// <summary>The upper bound for every exchange, generous enough for a loaded machine.</summary>
        private const int TimeoutMs = 10000;

        /// <summary>Restores the monitor after each test.</summary>
        private CurlNetworkMonitorIsolation _isolation = null!;

        /// <summary>Saves the monitor state and turns recording on.</summary>
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

        /// <summary>Sends a request with a timeout so a broken exchange fails instead of hanging the run.</summary>
        /// <param name="client">The client.</param>
        /// <param name="request">The request.</param>
        /// <param name="option">When the response is published.</param>
        /// <param name="token">The cancellation token.</param>
        /// <returns>The response.</returns>
        private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request,
            HttpCompletionOption option = HttpCompletionOption.ResponseContentRead, CancellationToken token = default)
        {
            var send = client.SendAsync(request, option, token);
            await NetAsyncAssert.CompletesWithin(send, TimeoutMs, "The request");
            return await send;
        }

        /// <summary>Gets the only recorded request.</summary>
        /// <returns>Its record.</returns>
        private static CurlNetworkRecord SingleRecord()
        {
            var records = CurlNetworkMonitor.GetRecords();
            Assert.That(records.Length, Is.EqualTo(1), "Exactly one request should have been recorded.");
            return records[0];
        }

        /// <summary>Waits until a record satisfies a condition.</summary>
        /// <param name="record">The record.</param>
        /// <param name="condition">The condition on its snapshot.</param>
        /// <param name="what">What is awaited, for the failure message.</param>
        /// <returns>The snapshot that satisfied the condition.</returns>
        private static async Task<CurlNetworkSnapshot> WaitForAsync(CurlNetworkRecord record, Func<CurlNetworkSnapshot, bool> condition, string what)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(TimeoutMs);
            while (true)
            {
                var snapshot = record.GetSnapshot();
                if (condition(snapshot))
                {
                    return snapshot;
                }
                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail($"Timed out waiting for {what}; the record is {snapshot.State}.");
                }
                await Task.Delay(10);
            }
        }

        /// <summary>Checks a GET exchange: the request head on the wire, the response, the connection details and the timings.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Get_RecordsExchange() => UniTask.ToCoroutine(async () =>
        {
            using var server = LoopbackHttpServer.Respond(200, "hello", "Content-Type: text/plain; charset=utf-8", "X-Test: one");
            var cookies = new CookieContainer();
            cookies.Add(new Uri(server.Url), new Cookie("sid", "abc"));
            using var client = new HttpClient(new CurlHttpMessageHandler { CookieContainer = cookies });
            using var request = new HttpRequestMessage(HttpMethod.Get, server.Url + "path?q=1");
            request.Headers.Add("X-Request", "abc");

            using var response = await SendAsync(client, request);
            var record = SingleRecord();
            var snapshot = record.GetSnapshot();

            Assert.That(snapshot.State, Is.EqualTo(CurlNetworkRequestState.Completed));
            Assert.That(snapshot.Method, Is.EqualTo("GET"));
            Assert.That(snapshot.Url, Is.EqualTo(server.Url + "path?q=1"));
            Assert.That(snapshot.Name, Is.EqualTo("path?q=1"));
            Assert.That(snapshot.StatusCode, Is.EqualTo(200));
            Assert.That(snapshot.Protocol, Is.EqualTo("http/1.1"));
            Assert.That(snapshot.RemoteAddress, Is.EqualTo($"127.0.0.1:{server.Port}"));
            Assert.That(snapshot.RequestLine, Is.EqualTo("GET /path?q=1 HTTP/1.1"));
            Assert.That(snapshot.AreRequestHeadersProvisional, Is.False);
            Assert.That(snapshot.RequestHeaders.Select(x => x.Key), Does.Contain("Host").And.Contain("X-Request"));
            Assert.That(snapshot.RequestCookies, Is.EqualTo("sid=abc"));
            Assert.That(snapshot.ResponseHeaders.Single(x => x.Key == "X-Test").Value, Is.EqualTo("one"));
            Assert.That(snapshot.ContentType, Is.EqualTo("text/plain; charset=utf-8"));
            Assert.That(Encoding.UTF8.GetString(record.GetResponseBody()), Is.EqualTo("hello"));
            Assert.That(snapshot.ResponseBodySize, Is.EqualTo(5));
            Assert.That(snapshot.ResponseHeaderSize, Is.GreaterThan(0));
            Assert.That(snapshot.Timings, Is.Not.Null);
            Assert.That(snapshot.Timings!.Value.Total, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(snapshot.Ttfb, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(snapshot.Transfer, Is.Not.Null);
            Assert.That(snapshot.Total, Is.GreaterThanOrEqualTo(snapshot.Ttfb!.Value));
            Assert.That(snapshot.Error, Is.Null);
        });

        /// <summary>Checks that the uploaded body and its headers are recorded.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Post_RecordsPayload() => UniTask.ToCoroutine(async () =>
        {
            using var server = LoopbackHttpServer.Respond(201, "created");
            using var client = new HttpClient(new CurlHttpMessageHandler());
            using var request = new HttpRequestMessage(HttpMethod.Post, server.Url)
            {
                Content = new StringContent("{\"a\":1}", Encoding.UTF8, "application/json")
            };

            using var response = await SendAsync(client, request);
            var record = SingleRecord();
            var snapshot = record.GetSnapshot();

            Assert.That(snapshot.Method, Is.EqualTo("POST"));
            Assert.That(snapshot.StatusCode, Is.EqualTo(201));
            Assert.That(Encoding.UTF8.GetString(record.GetRequestBody()), Is.EqualTo("{\"a\":1}"));
            Assert.That(snapshot.RequestBodySize, Is.EqualTo(7));
            Assert.That(snapshot.IsRequestBodyTruncated, Is.False);
            Assert.That(snapshot.RequestHeaders.Single(x => x.Key == "Content-Length").Value, Is.EqualTo("7"));
            Assert.That(snapshot.RequestHeaders.Single(x => x.Key == "Content-Type").Value, Does.StartWith("application/json"));
            Assert.That(Encoding.UTF8.GetString(record.GetResponseBody()), Is.EqualTo("created"));
        });

        /// <summary>Checks that a followed redirect records the final request, its URL and the redirect count.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Redirect_RecordsFinalHop() => UniTask.ToCoroutine(async () =>
        {
            using var server = new LoopbackHttpServer((request, stream, token) => request.Target == "/start"
                ? LoopbackHttpServer.WriteResponseAsync(stream, 302, Array.Empty<byte>(), "Location: /final", "X-Redirect: yes")
                : LoopbackHttpServer.WriteResponseAsync(stream, 200, Encoding.ASCII.GetBytes("done"), "Set-Cookie: sid=1; Path=/; HttpOnly"));
            using var client = new HttpClient(new CurlHttpMessageHandler());

            using var response = await SendAsync(client, new HttpRequestMessage(HttpMethod.Get, server.Url + "start"));
            var snapshot = SingleRecord().GetSnapshot();

            Assert.That(snapshot.StatusCode, Is.EqualTo(200));
            Assert.That(snapshot.Url, Is.EqualTo(server.Url + "start"));
            Assert.That(snapshot.EffectiveUrl, Is.EqualTo(server.Url + "final"));
            Assert.That(snapshot.RedirectCount, Is.EqualTo(1));
            Assert.That(snapshot.RequestLine, Is.EqualTo("GET /final HTTP/1.1"));
            Assert.That(snapshot.ResponseHeaders.Any(x => x.Key == "X-Redirect"), Is.False);
            Assert.That(snapshot.ResponseCookies, Is.EqualTo(new[] { "sid=1; Path=/; HttpOnly" }));
        });

        /// <summary>Checks that an error status is recorded as a completed request with that status.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ErrorStatus_IsCompletedWithStatus() => UniTask.ToCoroutine(async () =>
        {
            using var server = LoopbackHttpServer.Respond(404, "missing");
            using var client = new HttpClient(new CurlHttpMessageHandler());

            using var response = await SendAsync(client, new HttpRequestMessage(HttpMethod.Get, server.Url));
            var snapshot = SingleRecord().GetSnapshot();

            Assert.That(snapshot.State, Is.EqualTo(CurlNetworkRequestState.Completed));
            Assert.That(snapshot.StatusCode, Is.EqualTo(404));
            Assert.That(snapshot.ReasonPhrase, Is.EqualTo("Status"));
        });

        /// <summary>Checks that a refused connection is recorded as failed with libcurl's error.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ConnectionRefused_RecordsFailure() => UniTask.ToCoroutine(async () =>
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            using var client = new HttpClient(new CurlHttpMessageHandler());

            await NetAsyncAssert.ThrowsAsync<HttpRequestException>(
                () => SendAsync(client, new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/")));
            var snapshot = SingleRecord().GetSnapshot();

            Assert.That(snapshot.State, Is.EqualTo(CurlNetworkRequestState.Failed));
            Assert.That(snapshot.Error, Does.Contain("Curl Error"));
            Assert.That(snapshot.StatusCode, Is.Zero);
            Assert.That(snapshot.Ttfb, Is.Null);
        });

        /// <summary>Checks that a request waiting for its response is listed as running, and as cancelled once cancelled.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PendingRequest_IsVisibleUntilCancelled() => UniTask.ToCoroutine(async () =>
        {
            using var server = new LoopbackHttpServer((request, stream, token) => Task.Delay(Timeout.Infinite, token));
            using var client = new HttpClient(new CurlHttpMessageHandler());
            using var cancellation = new CancellationTokenSource();

            var send = client.SendAsync(new HttpRequestMessage(HttpMethod.Get, server.Url), cancellation.Token);
            Assert.That(await server.WaitForRequestsAsync(1), Is.True);
            var record = SingleRecord();
            var running = await WaitForAsync(record, x => x.RequestLine is not null, "the request head");

            Assert.That(running.State, Is.EqualTo(CurlNetworkRequestState.Running));
            Assert.That(running.IsFinished, Is.False);
            Assert.That(running.Ttfb, Is.Null);
            var before = running.Total;
            await Task.Delay(30);
            Assert.That(running.Total, Is.GreaterThan(before), "A pending request's total keeps growing.");

            cancellation.Cancel();
            await NetAsyncAssert.CompletesWithin(send.ContinueWith(_ => { }), TimeoutMs, "The cancelled request");
            var cancelled = record.GetSnapshot();

            Assert.That(cancelled.State, Is.EqualTo(CurlNetworkRequestState.Cancelled));
            Assert.That(cancelled.IsFinished, Is.True);
        });

        /// <summary>Checks that a response whose body is still streaming already shows its status, connection and first bytes.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator StreamingResponse_ShowsProgress() => UniTask.ToCoroutine(async () =>
        {
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var server = new LoopbackHttpServer(async (request, stream, token) =>
            {
                await LoopbackHttpServer.WriteAsync(stream, "HTTP/1.1 200 OK\r\nContent-Length: 10\r\nConnection: close\r\n\r\n12345");
                await stream.FlushAsync(token);
                // Bounded, so a failed assertion cannot leave the connection open forever.
                await Task.WhenAny(release.Task, Task.Delay(TimeoutMs, token));
                await LoopbackHttpServer.WriteAsync(stream, "67890");
            });
            using var client = new HttpClient(new CurlHttpMessageHandler());

            using var response = await SendAsync(client, new HttpRequestMessage(HttpMethod.Get, server.Url), HttpCompletionOption.ResponseHeadersRead);
            var record = SingleRecord();
            var streaming = await WaitForAsync(record, x => x.ResponseBodySize == 5, "the first body bytes");

            Assert.That(streaming.State, Is.EqualTo(CurlNetworkRequestState.Running));
            Assert.That(streaming.StatusCode, Is.EqualTo(200));
            Assert.That(streaming.RemoteAddress, Is.EqualTo($"127.0.0.1:{server.Port}"));
            Assert.That(streaming.Ttfb, Is.Not.Null);
            Assert.That(streaming.Transfer, Is.Not.Null);

            release.SetResult(true);
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("1234567890"));
            var completed = record.GetSnapshot();

            Assert.That(completed.State, Is.EqualTo(CurlNetworkRequestState.Completed));
            Assert.That(Encoding.ASCII.GetString(record.GetResponseBody()), Is.EqualTo("1234567890"));
        });

        /// <summary>Checks that nothing is recorded while recording is off.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Disabled_RecordsNothing() => UniTask.ToCoroutine(async () =>
        {
            CurlNetworkMonitor.IsEnabled = false;
            using var server = LoopbackHttpServer.Respond(200, "quiet");
            using var client = new HttpClient(new CurlHttpMessageHandler());

            using var response = await SendAsync(client, new HttpRequestMessage(HttpMethod.Get, server.Url));

            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("quiet"));
            Assert.That(CurlNetworkMonitor.Count, Is.Zero);
        });
    }
}
