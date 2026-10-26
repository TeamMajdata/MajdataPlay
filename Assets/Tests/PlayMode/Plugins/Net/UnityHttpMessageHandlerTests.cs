#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MajdataPlay.Net.Handlers;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MajdataPlay.Tests.Plugins.Net.PlayMode
{
    /// <summary>
    /// Covers the HttpClient adapter over UnityWebRequest in Play Mode, against a server on 127.0.0.1 that the test starts
    /// itself.
    /// </summary>
    public class UnityHttpMessageHandlerTests
    {
        /// <summary>The upper bound for every exchange.</summary>
        private const int TimeoutMs = 10000;

        /// <summary>Content whose length is unknown until it has been serialized.</summary>
        private sealed class UnknownLengthContent : HttpContent
        {
            /// <summary>The bytes written when serialized.</summary>
            private readonly byte[] _bytes;

            /// <summary>Creates content over the given bytes.</summary>
            /// <param name="bytes">The bytes to serialize.</param>
            public UnknownLengthContent(byte[] bytes)
            {
                _bytes = bytes;
            }

            /// <inheritdoc />
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            {
                return stream.WriteAsync(_bytes, 0, _bytes.Length);
            }

            /// <inheritdoc />
            protected override bool TryComputeLength(out long length)
            {
                length = 0;
                return false;
            }
        }

        /// <summary>Sends a request with a timeout so a broken exchange fails instead of hanging the run.</summary>
        /// <param name="client">The client.</param>
        /// <param name="request">The request.</param>
        /// <param name="token">The cancellation token.</param>
        /// <returns>The response.</returns>
        private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken token = default)
        {
            var send = client.SendAsync(request, token);
            await NetAsyncAssert.CompletesWithin(send, TimeoutMs, "The request");
            return await send;
        }

        /// <summary>Checks the default redirect limit.</summary>
        [Test]
        public void MaxRedirects_DefaultsTo32()
        {
            using var handler = new UnityHttpMessageHandler();

            Assert.That(handler.MaxRedirects, Is.EqualTo(32));
        }

        /// <summary>Checks a GET exchange: status, body, response headers, request headers and the request message.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Get_ReturnsStatusHeadersAndBody() => UniTask.ToCoroutine(async () =>
        {
            using var server = LoopbackHttpServer.Respond(200, "hello", "X-Test: one");
            using var client = new HttpClient(new UnityHttpMessageHandler());
            using var request = new HttpRequestMessage(HttpMethod.Get, server.Url + "path");
            request.Headers.Add("X-Request", "abc");

            using var response = await SendAsync(client, request);
            var body = await response.Content.ReadAsStringAsync();
            server.Requests.TryPeek(out var received);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.EqualTo("hello"));
            Assert.That(response.Content.Headers.ContentLength, Is.EqualTo(5));
            Assert.That(response.Headers.GetValues("X-Test").Single(), Is.EqualTo("one"));
            Assert.That(response.RequestMessage, Is.SameAs(request));
            Assert.That(response.ReasonPhrase, Is.EqualTo("OK"), "No error text leaves the default reason phrase.");
            Assert.That(received!.Target, Is.EqualTo("/path"));
            Assert.That(received.GetHeader("X-Request"), Is.EqualTo("abc"));
        });

        /// <summary>Checks that content headers of the response, such as its type, are exposed on the content.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Get_ExposesContentHeaders() => UniTask.ToCoroutine(async () =>
        {
            using var server = LoopbackHttpServer.Respond(200, "{}", "Content-Type: application/json");
            using var client = new HttpClient(new UnityHttpMessageHandler());

            using var response = await SendAsync(client, new HttpRequestMessage(HttpMethod.Get, server.Url));

            Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/json"));
        });

        /// <summary>Checks that an empty body yields empty content with a zero length.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Get_HandlesEmptyBody() => UniTask.ToCoroutine(async () =>
        {
            using var server = LoopbackHttpServer.Respond(200, string.Empty);
            using var client = new HttpClient(new UnityHttpMessageHandler());

            using var response = await SendAsync(client, new HttpRequestMessage(HttpMethod.Get, server.Url));

            Assert.That(await response.Content.ReadAsByteArrayAsync(), Is.Empty);
            Assert.That(response.Content.Headers.ContentLength, Is.EqualTo(0));
        });

        /// <summary>Checks that an error status is returned as a response carrying the UnityWebRequest error text.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Get_ReturnsErrorStatus() => UniTask.ToCoroutine(async () =>
        {
            using var server = LoopbackHttpServer.Respond(404, "missing");
            using var client = new HttpClient(new UnityHttpMessageHandler());
            using var request = new HttpRequestMessage(HttpMethod.Get, server.Url);

            using var response = await SendAsync(client, request);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(response.ReasonPhrase, Is.Not.Empty);
            Assert.That(response.RequestMessage, Is.SameAs(request));
        });

        /// <summary>Checks that a request body and its content type are uploaded.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Post_UploadsContent() => UniTask.ToCoroutine(async () =>
        {
            using var server = LoopbackHttpServer.Respond(201, "created");
            using var client = new HttpClient(new UnityHttpMessageHandler());
            using var request = new HttpRequestMessage(HttpMethod.Post, server.Url)
            {
                Content = new StringContent("payload", Encoding.UTF8, "text/plain")
            };

            using var response = await SendAsync(client, request);
            server.Requests.TryPeek(out var received);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(received!.Method, Is.EqualTo("POST"));
            Assert.That(received.BodyText, Is.EqualTo("payload"));
            Assert.That(received.GetHeader("Content-Type"), Does.StartWith("text/plain"));
        });

        /// <summary>Checks that a request body whose length is not known in advance is uploaded completely.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Post_UploadsContentOfUnknownLength() => UniTask.ToCoroutine(async () =>
        {
            using var server = LoopbackHttpServer.Respond(200, "ok");
            using var client = new HttpClient(new UnityHttpMessageHandler());
            using var request = new HttpRequestMessage(HttpMethod.Post, server.Url)
            {
                Content = new UnknownLengthContent(Encoding.ASCII.GetBytes("streamed"))
            };

            using var response = await SendAsync(client, request);
            server.Requests.TryPeek(out var received);

            Assert.That(received!.BodyText, Is.EqualTo("streamed"));
        });

        /// <summary>Checks that cancelling a pending request cancels it.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Cancellation_CancelsPendingRequest() => UniTask.ToCoroutine(async () =>
        {
            using var server = new LoopbackHttpServer((received, stream, token) => Task.Delay(Timeout.Infinite, token));
            using var client = new HttpClient(new UnityHttpMessageHandler());
            using var cancellation = new CancellationTokenSource();

            var send = client.SendAsync(new HttpRequestMessage(HttpMethod.Get, server.Url), cancellation.Token);
            Assert.That(await server.WaitForRequestsAsync(1), Is.True);
            cancellation.Cancel();
            await NetAsyncAssert.CompletesWithin(send.ContinueWith(_ => { }), TimeoutMs, "The cancelled request");

            Assert.That(send.IsCanceled || send.Exception?.InnerException is OperationCanceledException, Is.True);
        });

        /// <summary>Checks that disposing the response content through its interface releases the request.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ContentDisposal_ReleasesRequest() => UniTask.ToCoroutine(async () =>
        {
            using var server = LoopbackHttpServer.Respond(200, "bytes");
            using var client = new HttpClient(new UnityHttpMessageHandler());
            var response = await SendAsync(client, new HttpRequestMessage(HttpMethod.Get, server.Url));
            var body = await response.Content.ReadAsByteArrayAsync();

            Assert.DoesNotThrow(((IDisposable)response.Content).Dispose);
            Assert.DoesNotThrow(response.Dispose);
            Assert.That(Encoding.ASCII.GetString(body), Is.EqualTo("bytes"));
        });
    }
}
