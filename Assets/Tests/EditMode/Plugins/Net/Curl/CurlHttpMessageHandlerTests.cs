#nullable enable
using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MajdataPlay.Net.Curl;
using MajdataPlay.Net.Curl.Core;
using MajdataPlay.Net.Curl.Core.PInvoke;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MajdataPlay.Tests.Plugins.Net.Curl
{
    /// <summary>
    /// Covers complete HTTP exchanges through the libcurl handler against a server on 127.0.0.1 that the test starts
    /// itself; no request leaves the machine.
    /// </summary>
    public class CurlHttpMessageHandlerTests
    {
        /// <summary>The upper bound for every exchange, generous enough for a loaded machine.</summary>
        private const int TimeoutMs = 10000;

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

        /// <summary>Gets the libcurl features bitmask.</summary>
        /// <returns>The features reported by <c>curl_version_info</c>.</returns>
        private static int Features()
        {
            MajdataPlay.Net.Curl.Lifecycle.LibCurlLifecycle.Retain();
            try
            {
                return LibCurl.GetVersionInfo(0).Features;
            }
            finally
            {
                MajdataPlay.Net.Curl.Lifecycle.LibCurlLifecycle.Release();
            }
        }

        /// <summary>Checks a GET exchange: status, body, response and content headers, request headers and the result code.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Get_ReturnsStatusHeadersAndBody() => UniTask.ToCoroutine(async () =>
        {
            using var server = LoopbackHttpServer.Respond(200, "hello", "Content-Type: text/plain; charset=utf-8", "X-Test: one");
            using var handler = new CurlHttpMessageHandler();
            using var client = new HttpClient(handler);
            using var request = new HttpRequestMessage(HttpMethod.Get, server.Url + "path?q=1");
            request.Headers.Add("X-Request", "abc");

            using var response = await SendAsync(client, request);
            var body = await response.Content.ReadAsStringAsync();
            server.Requests.TryPeek(out var received);

            Assert.That(response, Is.InstanceOf<CurlResponse>());
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.EqualTo("hello"));
            Assert.That(response.Headers.GetValues("X-Test").Single(), Is.EqualTo("one"));
            Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("text/plain"));
            Assert.That(response.RequestMessage, Is.SameAs(request));
            Assert.That(((CurlResponse)response).ResultCode, Is.EqualTo(CurlCode.Ok));
            Assert.That(((CurlResponse)response).Config.MaxConnectionsPerServer, Is.EqualTo(8));
            Assert.That(received!.Method, Is.EqualTo("GET"));
            Assert.That(received.Target, Is.EqualTo("/path?q=1"));
            Assert.That(received.GetHeader("X-Request"), Is.EqualTo("abc"));
        });

        /// <summary>Checks that an error status is returned as a response rather than an exception.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Get_ReturnsErrorStatus() => UniTask.ToCoroutine(async () =>
        {
            using var server = LoopbackHttpServer.Respond(404, "missing");
            using var client = new HttpClient(new CurlHttpMessageHandler());

            using var response = await SendAsync(client, new HttpRequestMessage(HttpMethod.Get, server.Url));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(response.IsSuccessStatusCode, Is.False);
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("missing"));
        });

        /// <summary>Checks that an empty body publishes the response when the transfer completes.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Get_HandlesEmptyBody() => UniTask.ToCoroutine(async () =>
        {
            using var server = LoopbackHttpServer.Respond(204, string.Empty);
            using var client = new HttpClient(new CurlHttpMessageHandler());

            using var response = await SendAsync(client, new HttpRequestMessage(HttpMethod.Get, server.Url),
                HttpCompletionOption.ResponseHeadersRead);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(await response.Content.ReadAsByteArrayAsync(), Is.Empty);
        });

        /// <summary>Checks that a seekable request body is uploaded with its content headers and length.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Post_UploadsSeekableContent() => UniTask.ToCoroutine(async () =>
        {
            using var server = LoopbackHttpServer.Respond(201, "created");
            using var client = new HttpClient(new CurlHttpMessageHandler());
            using var request = new HttpRequestMessage(HttpMethod.Post, server.Url)
            {
                Content = new StringContent("{\"a\":1}", Encoding.UTF8, "application/json")
            };

            using var response = await SendAsync(client, request);
            server.Requests.TryPeek(out var received);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(received!.Method, Is.EqualTo("POST"));
            Assert.That(received.BodyText, Is.EqualTo("{\"a\":1}"));
            Assert.That(received.GetHeader("Content-Type"), Does.StartWith("application/json"));
            Assert.That(received.GetHeader("Content-Length"), Is.EqualTo("7"));
        });

        /// <summary>Checks that a body of unknown length is uploaded with chunked transfer encoding.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Put_UploadsNonSeekableContentChunked() => UniTask.ToCoroutine(async () =>
        {
            using var server = LoopbackHttpServer.Respond(200, "ok");
            using var client = new HttpClient(new CurlHttpMessageHandler());
            var payload = Encoding.ASCII.GetBytes(new string('p', 5000));
            using var request = new HttpRequestMessage(HttpMethod.Put, server.Url)
            {
                Content = new StreamContent(new UnseekableStream(payload))
            };

            using var response = await SendAsync(client, request);
            server.Requests.TryPeek(out var received);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(received!.Method, Is.EqualTo("PUT"));
            Assert.That(received.GetHeader("Transfer-Encoding"), Is.EqualTo("chunked"));
            Assert.That(received.Body, Is.EqualTo(payload));
        });

        /// <summary>Checks that redirects are followed by default and the final block's headers replace the redirect's.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Redirect_IsFollowedByDefault() => UniTask.ToCoroutine(async () =>
        {
            using var server = new LoopbackHttpServer((request, stream, token) => request.Target == "/start"
                ? LoopbackHttpServer.WriteResponseAsync(stream, 302, Array.Empty<byte>(), "Location: /final", "X-Redirect: yes")
                : LoopbackHttpServer.WriteResponseAsync(stream, 200, Encoding.ASCII.GetBytes("done")));
            using var client = new HttpClient(new CurlHttpMessageHandler());

            using var response = await SendAsync(client, new HttpRequestMessage(HttpMethod.Get, server.Url + "start"));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("done"));
            Assert.That(response.Headers.Contains("X-Redirect"), Is.False);
            Assert.That(server.Requests.Select(x => x.Target), Is.EqualTo(new[] { "/start", "/final" }));
        });

        /// <summary>Checks that disabling redirects returns the redirect response itself.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Redirect_CanBeDisabled() => UniTask.ToCoroutine(async () =>
        {
            using var server = new LoopbackHttpServer((request, stream, token) =>
                LoopbackHttpServer.WriteResponseAsync(stream, 302, Array.Empty<byte>(), "Location: /final"));
            using var client = new HttpClient(new CurlHttpMessageHandler { AllowAutoRedirect = false });

            using var response = await SendAsync(client, new HttpRequestMessage(HttpMethod.Get, server.Url + "start"));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(response.Headers.Location!.OriginalString, Is.EqualTo("/final"));
            Assert.That(server.RequestCount, Is.EqualTo(1));
        });

        /// <summary>Checks that exceeding the redirect limit fails the request with a curl error.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Redirect_LimitFailsRequest() => UniTask.ToCoroutine(async () =>
        {
            using var server = new LoopbackHttpServer((request, stream, token) =>
                LoopbackHttpServer.WriteResponseAsync(stream, 302, Array.Empty<byte>(), "Location: /again"));
            using var client = new HttpClient(new CurlHttpMessageHandler { MaxAutomaticRedirections = 1 });

            var exception = await NetAsyncAssert.ThrowsAsync<HttpRequestException>(
                () => SendAsync(client, new HttpRequestMessage(HttpMethod.Get, server.Url)));

            Assert.That(exception.InnerException, Is.InstanceOf<CurlException>());
            Assert.That(server.RequestCount, Is.EqualTo(2));
        });

        /// <summary>Checks that an oversized header block aborts the transfer.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator HeaderLimit_FailsRequest() => UniTask.ToCoroutine(async () =>
        {
            using var server = LoopbackHttpServer.Respond(200, "x", "X-Large: " + new string('a', 600));
            using var client = new HttpClient(new CurlHttpMessageHandler { MaxResponseHeadersLength = 256 });

            var exception = await NetAsyncAssert.ThrowsAsync<HttpRequestException>(
                () => SendAsync(client, new HttpRequestMessage(HttpMethod.Get, server.Url)));

            Assert.That(exception.InnerException, Is.InstanceOf<CurlException>());
        });

        /// <summary>Checks that a refused connection fails with a coded curl error that carries the OS error.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ConnectionRefused_FailsWithCurlError() => UniTask.ToCoroutine(async () =>
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            using var client = new HttpClient(new CurlHttpMessageHandler());

            var exception = await NetAsyncAssert.ThrowsAsync<HttpRequestException>(
                () => SendAsync(client, new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/")));
            var curl = (CurlException)exception.InnerException!;

            Assert.That(curl.ErrorCode, Is.EqualTo(CurlCode.CouldNotConnect));
            Assert.That(curl.Message, Does.Contain("OS Error"));
        });

        /// <summary>Checks that cancelling while waiting for headers cancels the request.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Cancellation_WhileWaitingForHeadersCancelsRequest() => UniTask.ToCoroutine(async () =>
        {
            using var server = new LoopbackHttpServer((request, stream, token) => Task.Delay(Timeout.Infinite, token));
            using var client = new HttpClient(new CurlHttpMessageHandler());
            using var cancellation = new CancellationTokenSource();

            var send = client.SendAsync(new HttpRequestMessage(HttpMethod.Get, server.Url), cancellation.Token);
            Assert.That(await server.WaitForRequestsAsync(1), Is.True);
            cancellation.Cancel();

            await NetAsyncAssert.CompletesWithin(send.ContinueWith(_ => { }), TimeoutMs, "The cancelled request");
            Assert.That(send.IsCanceled, Is.True);
        });

        /// <summary>Checks that an already cancelled token cancels the request before it is sent.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Cancellation_PreCancelledTokenCancelsRequest() => UniTask.ToCoroutine(async () =>
        {
            using var server = LoopbackHttpServer.Respond(200, "x");
            using var handler = new CurlHttpMessageHandler();
            using var invoker = new HttpMessageInvoker(handler);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var send = invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, server.Url), cancellation.Token);
            await NetAsyncAssert.CompletesWithin(send.ContinueWith(_ => { }), TimeoutMs, "The cancelled request");

            Assert.That(send.IsCanceled, Is.True);
        });

        /// <summary>Checks that disposing a response mid-body releases the transfer and keeps the handler usable.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ResponseDisposal_MidBodyReleasesTransfer() => UniTask.ToCoroutine(async () =>
        {
            var calls = 0;
            using var server = new LoopbackHttpServer(async (request, stream, token) =>
            {
                if (Interlocked.Increment(ref calls) > 1)
                {
                    await LoopbackHttpServer.WriteResponseAsync(stream, 200, Encoding.ASCII.GetBytes("second"));
                    return;
                }
                await LoopbackHttpServer.WriteAsync(stream, "HTTP/1.1 200 OK\r\nContent-Length: 100000\r\n\r\npartial");
                await stream.FlushAsync(token);
                await Task.Delay(Timeout.Infinite, token);
            });
            using var client = new HttpClient(new CurlHttpMessageHandler());

            var first = await SendAsync(client, new HttpRequestMessage(HttpMethod.Get, server.Url), HttpCompletionOption.ResponseHeadersRead);
            var stream = await first.Content.ReadAsStreamAsync();
            var buffer = new byte[7];
            var read = await Task.Run(() => stream.Read(buffer, 0, buffer.Length));
            first.Dispose();
            using var second = await SendAsync(client, new HttpRequestMessage(HttpMethod.Get, server.Url));

            Assert.That(Encoding.ASCII.GetString(buffer, 0, read), Is.EqualTo("partial".Substring(0, read)));
            Assert.That(await second.Content.ReadAsStringAsync(), Is.EqualTo("second"));
        });

        /// <summary>Checks that aborting a published response makes its body fail with cancellation.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Abort_FailsBodyReads() => UniTask.ToCoroutine(async () =>
        {
            using var server = new LoopbackHttpServer(async (request, stream, token) =>
            {
                await LoopbackHttpServer.WriteAsync(stream, "HTTP/1.1 200 OK\r\nContent-Length: 100000\r\n\r\npartial");
                await stream.FlushAsync(token);
                await Task.Delay(Timeout.Infinite, token);
            });
            using var client = new HttpClient(new CurlHttpMessageHandler());
            using var response = (CurlResponse)await SendAsync(client, new HttpRequestMessage(HttpMethod.Get, server.Url),
                HttpCompletionOption.ResponseHeadersRead);
            var stream = await response.Content.ReadAsStreamAsync();

            response.Abort();

            Assert.Throws<OperationCanceledException>(() => stream.Read(new byte[16], 0, 16));
        });

        /// <summary>Checks that a large body streams completely through a small buffer, pausing and resuming the transfer.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LargeBody_StreamsThroughSmallBuffer() => UniTask.ToCoroutine(async () =>
        {
            var payload = Enumerable.Range(0, 512 * 1024).Select(x => (byte)(x % 251)).ToArray();
            using var server = new LoopbackHttpServer((request, stream, token) => LoopbackHttpServer.WriteResponseAsync(stream, 200, payload));
            using var client = new HttpClient(new CurlHttpMessageHandler { ResponseBodyBufferSize = 4096 });

            using var response = await SendAsync(client, new HttpRequestMessage(HttpMethod.Get, server.Url), HttpCompletionOption.ResponseHeadersRead);
            var read = Task.Run(async () =>
            {
                using var body = await response.Content.ReadAsStreamAsync();
                using var copy = new MemoryStream();
                var buffer = new byte[1024];
                int count;
                while ((count = body.Read(buffer, 0, buffer.Length)) > 0)
                {
                    copy.Write(buffer, 0, count);
                }
                return copy.ToArray();
            });
            await NetAsyncAssert.CompletesWithin(read, TimeoutMs, "Reading the body");

            Assert.That(read.Result, Is.EqualTo(payload));
        });

        /// <summary>Checks that chunked responses with trailers are decoded and trailers stay out of the headers.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ChunkedResponse_IgnoresTrailers() => UniTask.ToCoroutine(async () =>
        {
            using var server = new LoopbackHttpServer((request, stream, token) => LoopbackHttpServer.WriteAsync(stream,
                "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nTrailer: X-Checksum\r\nConnection: close\r\n\r\n" +
                "5\r\nhello\r\n6\r\n world\r\n0\r\nX-Checksum: 42\r\n\r\n"));
            using var client = new HttpClient(new CurlHttpMessageHandler());

            using var response = await SendAsync(client, new HttpRequestMessage(HttpMethod.Get, server.Url));

            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("hello world"));
            Assert.That(response.Headers.Contains("X-Checksum"), Is.False);
        });

        /// <summary>Checks that request cookies are sent and response cookies are stored.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Cookies_AreSentAndStored() => UniTask.ToCoroutine(async () =>
        {
            using var server = LoopbackHttpServer.Respond(200, "ok", "Set-Cookie: token=xyz; Path=/");
            var cookies = new CookieContainer();
            var uri = new Uri(server.Url);
            cookies.Add(uri, new Cookie("session", "abc"));
            using var client = new HttpClient(new CurlHttpMessageHandler { CookieContainer = cookies });

            using var response = await SendAsync(client, new HttpRequestMessage(HttpMethod.Get, server.Url));
            server.Requests.TryPeek(out var received);

            Assert.That(received!.GetHeader("Cookie"), Is.EqualTo("session=abc"));
            Assert.That(cookies.GetCookies(uri)["token"]!.Value, Is.EqualTo("xyz"));
        });

        /// <summary>Checks that pre-authentication sends basic credentials with the first request.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Credentials_PreAuthenticateSendsBasicAuth() => UniTask.ToCoroutine(async () =>
        {
            using var server = LoopbackHttpServer.Respond(200, "ok");
            using var client = new HttpClient(new CurlHttpMessageHandler
            {
                Credentials = new NetworkCredential("user", "pass"),
                PreAuthenticate = true
            });

            using var response = await SendAsync(client, new HttpRequestMessage(HttpMethod.Get, server.Url));
            server.Requests.TryPeek(out var received);

            Assert.That(received!.GetHeader("Authorization"), Is.EqualTo("Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("user:pass"))));
        });

        /// <summary>Checks that without pre-authentication credentials are sent after the server's challenge.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Credentials_AnswerChallenge() => UniTask.ToCoroutine(async () =>
        {
            using var server = new LoopbackHttpServer((request, stream, token) => request.GetHeader("Authorization") is null
                ? LoopbackHttpServer.WriteResponseAsync(stream, 401, Array.Empty<byte>(), "WWW-Authenticate: Basic realm=\"test\"")
                : LoopbackHttpServer.WriteResponseAsync(stream, 200, Encoding.ASCII.GetBytes("authorized")));
            using var client = new HttpClient(new CurlHttpMessageHandler { Credentials = new NetworkCredential("user", "pass") });

            using var response = await SendAsync(client, new HttpRequestMessage(HttpMethod.Get, server.Url));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("authorized"));
            Assert.That(server.Requests.First().GetHeader("Authorization"), Is.Null);
        });

        /// <summary>Checks that default credentials request Windows authentication when libcurl supports it, or fail as not built in.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Credentials_DefaultCredentialsDependOnBuildFeatures() => UniTask.ToCoroutine(async () =>
        {
            const int ntlm = 1 << 4;
            const int spnego = 1 << 8;
            using var server = LoopbackHttpServer.Respond(200, "ok");
            using var client = new HttpClient(new CurlHttpMessageHandler { UseDefaultCredentials = true });
            var supported = (Features() & (ntlm | spnego)) != 0;

            if (supported)
            {
                using var response = await SendAsync(client, new HttpRequestMessage(HttpMethod.Get, server.Url));
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            }
            else
            {
                var exception = await NetAsyncAssert.ThrowsAsync<CurlException>(
                    () => SendAsync(client, new HttpRequestMessage(HttpMethod.Get, server.Url)));
                Assert.That(exception.ErrorCode, Is.EqualTo(CurlCode.NotBuiltIn));
            }
        });

        /// <summary>Checks that requests go through a configured proxy with proxy credentials.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Proxy_ForwardsAbsoluteRequestWithCredentials() => UniTask.ToCoroutine(async () =>
        {
            using var proxy = LoopbackHttpServer.Respond(200, "proxied");
            using var client = new HttpClient(new CurlHttpMessageHandler
            {
                UseProxy = true,
                Proxy = new WebProxy(proxy.Url),
                DefaultProxyCredentials = new NetworkCredential("proxy", "secret")
            });

            using var response = await SendAsync(client, new HttpRequestMessage(HttpMethod.Get, "http://target.invalid/resource"));
            proxy.Requests.TryPeek(out var received);

            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("proxied"));
            Assert.That(received!.Target, Is.EqualTo("http://target.invalid/resource"));
            Assert.That(received.GetHeader("Proxy-Authorization"), Is.EqualTo("Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("proxy:secret"))));
        });

        /// <summary>Checks that a proxy which bypasses local addresses lets loopback requests go directly.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Proxy_BypassedForLocalAddresses() => UniTask.ToCoroutine(async () =>
        {
            using var server = LoopbackHttpServer.Respond(200, "direct");
            using var client = new HttpClient(new CurlHttpMessageHandler
            {
                UseProxy = true,
                Proxy = new WebProxy("http://127.0.0.1:1", true)
            });

            using var response = await SendAsync(client, new HttpRequestMessage(HttpMethod.Get, server.Url));

            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("direct"));
            Assert.That(server.Requests.Single().Target, Is.EqualTo("/"));
        });

        /// <summary>Checks that gzip decompression is requested and applied when enabled.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Decompression_DecodesGzip() => UniTask.ToCoroutine(async () =>
        {
            using var compressed = new MemoryStream();
            using (var gzip = new GZipStream(compressed, CompressionMode.Compress, true))
            {
                var text = Encoding.ASCII.GetBytes("decompressed text");
                gzip.Write(text, 0, text.Length);
            }
            var bytes = compressed.ToArray();
            using var server = new LoopbackHttpServer((request, stream, token) =>
                LoopbackHttpServer.WriteResponseAsync(stream, 200, bytes, "Content-Encoding: gzip"));
            using var client = new HttpClient(new CurlHttpMessageHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });

            using var response = await SendAsync(client, new HttpRequestMessage(HttpMethod.Get, server.Url));
            server.Requests.TryPeek(out var received);

            Assert.That(received!.GetHeader("Accept-Encoding"), Is.EqualTo("gzip,deflate"));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("decompressed text"));
        });

        /// <summary>Checks that concurrent requests on one handler all complete.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ConcurrentRequests_AllComplete() => UniTask.ToCoroutine(async () =>
        {
            using var server = new LoopbackHttpServer((request, stream, token) =>
                LoopbackHttpServer.WriteResponseAsync(stream, 200, Encoding.ASCII.GetBytes(request.Target)));
            using var client = new HttpClient(new CurlHttpMessageHandler { MaxConnectionsPerServer = 2 });

            var requests = Enumerable.Range(0, 6).Select(i => client.GetStringAsync(server.Url + i)).ToArray();
            var all = Task.WhenAll(requests);
            await NetAsyncAssert.CompletesWithin(all, TimeoutMs, "The concurrent requests");

            Assert.That(all.Result, Is.EqualTo(Enumerable.Range(0, 6).Select(i => "/" + i).ToArray()));
        });

        /// <summary>Checks that disposing the handler fails a request that is still waiting for its response.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator HandlerDisposal_FailsPendingRequest() => UniTask.ToCoroutine(async () =>
        {
            using var server = new LoopbackHttpServer((request, stream, token) => Task.Delay(Timeout.Infinite, token));
            var handler = new CurlHttpMessageHandler();
            using var invoker = new HttpMessageInvoker(handler);

            var send = invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, server.Url), CancellationToken.None);
            Assert.That(await server.WaitForRequestsAsync(1), Is.True);
            handler.Dispose();
            await NetAsyncAssert.CompletesWithin(send.ContinueWith(_ => { }), TimeoutMs, "The abandoned request");

            Assert.That(send.IsFaulted, Is.True);
            Assert.That(send.Exception!.InnerException, Is.InstanceOf<HttpRequestException>());
            Assert.That(send.Exception.InnerException!.InnerException, Is.InstanceOf<ObjectDisposedException>());
            Assert.ThrowsAsync<ObjectDisposedException>(() => invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, server.Url), CancellationToken.None));
        });

        /// <summary>A readable stream that cannot seek, so uploads of it have no known length.</summary>
        private sealed class UnseekableStream : MemoryStream
        {
            /// <summary>Creates a stream over the given bytes.</summary>
            /// <param name="bytes">The bytes to read.</param>
            public UnseekableStream(byte[] bytes) : base(bytes)
            {
            }

            /// <inheritdoc />
            public override bool CanSeek => false;
        }
    }
}
