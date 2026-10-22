#nullable enable
using System;
using System.Collections;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.Net;
using NUnit.Framework;
using UnityEngine.Networking;
using UnityEngine.TestTools;

namespace MajdataPlay.Tests.Plugins.Net.PlayMode
{
    /// <summary>
    /// Covers the success checks of completed UnityWebRequests in Play Mode, against a server on 127.0.0.1 that the test
    /// starts itself.
    /// </summary>
    public class UnityWebRequestExtensionsTests
    {
        /// <summary>The server of the current test.</summary>
        private LoopbackHttpServer? _server;

        /// <summary>Stops the server of the current test.</summary>
        [TearDown]
        public void TearDown()
        {
            _server?.Dispose();
            _server = null;
        }

        /// <summary>Checks that a completed 200 response passes both success checks.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Ok_IsSuccessful()
        {
            _server = LoopbackHttpServer.Respond(200, "ok");
            using var request = UnityWebRequest.Get(_server.Url);

            yield return request.SendWebRequest();

            Assert.That(request.IsSuccessStatusCode(), Is.True);
            Assert.That(request.EnsureSuccessStatusCode(), Is.SameAs(request));
        }

        /// <summary>Checks that a 2xx status other than 200 passes the range check but not the strict 200 check.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator NoContent_PassesRangeCheckOnly()
        {
            _server = LoopbackHttpServer.Respond(204, string.Empty);
            using var request = UnityWebRequest.Get(_server.Url);

            yield return request.SendWebRequest();

            Assert.That(request.IsSuccessStatusCode(), Is.False);
            Assert.That(request.EnsureSuccessStatusCode(), Is.SameAs(request));
        }

        /// <summary>Checks that an error status is reported as an unsuccessful request with its status code.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator NotFound_IsUnsuccessful()
        {
            _server = LoopbackHttpServer.Respond(404, "missing");
            using var request = UnityWebRequest.Get(_server.Url);

            yield return request.SendWebRequest();

            var exception = Assert.Throws<HttpException>(() => request.EnsureSuccessStatusCode());
            Assert.That(exception.ErrorCode, Is.EqualTo(HttpErrorCode.Unsuccessful));
            Assert.That(exception.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(exception.OriginalUrl, Is.EqualTo(_server.Url));
            Assert.That(request.IsSuccessStatusCode(), Is.False);
        }

        /// <summary>Checks that a connection closed without a response is reported as unreachable.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator EmptyReply_IsUnreachable()
        {
            _server = new LoopbackHttpServer((received, stream, token) => Task.CompletedTask);
            using var request = UnityWebRequest.Get(_server.Url);

            yield return request.SendWebRequest();

            Assert.That(request.result, Is.EqualTo(UnityWebRequest.Result.ConnectionError));
            var exception = Assert.Throws<HttpException>(() => request.EnsureSuccessStatusCode());
            Assert.That(exception.ErrorCode, Is.EqualTo(HttpErrorCode.Unreachable));
        }

        /// <summary>Checks that a request that runs into its timeout is reported as a timeout.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Timeout_IsReportedAsTimeout()
        {
            _server = new LoopbackHttpServer((received, stream, token) => Task.Delay(Timeout.Infinite, token));
            using var request = UnityWebRequest.Get(_server.Url);
            request.timeout = 1;

            yield return request.SendWebRequest();

            var exception = Assert.Throws<HttpException>(() => request.EnsureSuccessStatusCode());
            Assert.That(exception.ErrorCode, Is.EqualTo(HttpErrorCode.Timeout));
        }
    }
}
