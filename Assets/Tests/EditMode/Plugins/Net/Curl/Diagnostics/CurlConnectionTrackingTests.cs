#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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
    /// Covers the request counters and the connection pool that <see cref="CurlHandlerStatistics"/> builds from real
    /// libcurl transfers against a keep-alive server on 127.0.0.1: reuse, server-side closes, concurrent connections,
    /// failures, cancellations, disposal and the link from recorded requests to their connection.
    /// </summary>
    public class CurlConnectionTrackingTests
    {
        /// <summary>The upper bound for every exchange, generous enough for a loaded machine.</summary>
        private const int TimeoutMs = 10000;

        /// <summary>Restores the handler list and the tracking switch after each test.</summary>
        private CurlStatisticsIsolation _statistics = null!;

        /// <summary>Restores the request history after each test.</summary>
        private CurlNetworkMonitorIsolation _monitor = null!;

        /// <summary>Saves the statistics and the history, and turns connection tracking and recording on.</summary>
        [SetUp]
        public void SetUp()
        {
            _statistics = new CurlStatisticsIsolation();
            _monitor = new CurlNetworkMonitorIsolation();
        }

        /// <summary>Restores the statistics and the history.</summary>
        [TearDown]
        public void TearDown()
        {
            _monitor.Dispose();
            _statistics.Dispose();
        }

        /// <summary>Sends a GET request and reads the whole response.</summary>
        /// <param name="client">The client.</param>
        /// <param name="url">The URL.</param>
        /// <param name="token">The cancellation token.</param>
        /// <returns>The response body.</returns>
        private static async Task<string> GetAsync(HttpClient client, string url, CancellationToken token = default)
        {
            var send = client.GetAsync(url, token);
            await NetAsyncAssert.CompletesWithin(send, TimeoutMs, "The request");
            using var response = await send;
            return await response.Content.ReadAsStringAsync();
        }

        /// <summary>Waits until a condition holds.</summary>
        /// <param name="condition">The condition.</param>
        /// <param name="what">What is awaited, for the failure message.</param>
        /// <returns>A task completing once the condition holds.</returns>
        private static async Task WaitUntilAsync(Func<bool> condition, string what)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(TimeoutMs);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail($"Timed out waiting for {what}.");
                }
                await Task.Delay(10);
            }
        }

        /// <summary>Checks that sequential requests share one keep-alive connection, which waits idle in the pool, and that recorded requests name it.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SequentialRequests_ReuseOneConnection() => UniTask.ToCoroutine(async () =>
        {
            using var server = new KeepAliveHttpServer();
            using var handler = new CurlHttpMessageHandler();
            using var client = new HttpClient(handler, false);

            Assert.That(await GetAsync(client, server.Url + "first"), Is.EqualTo("ok"));
            Assert.That(await GetAsync(client, server.Url + "second"), Is.EqualTo("ok"));
            var snapshot = handler.Statistics.GetSnapshot();
            var records = CurlNetworkMonitor.GetRecords().Select(x => x.GetSnapshot()).ToArray();

            Assert.That(server.ConnectionCount, Is.EqualTo(1));
            Assert.That(snapshot.TotalRequests, Is.EqualTo(2));
            Assert.That(snapshot.CompletedRequests, Is.EqualTo(2));
            Assert.That(snapshot.QueuedRequests, Is.Zero);
            Assert.That(snapshot.RunningRequests, Is.Zero);
            Assert.That(snapshot.MaxConnectionsPerHost, Is.EqualTo(handler.MaxConnectionsPerServer));
            Assert.That(snapshot.ConnectionsOpened, Is.EqualTo(1));
            Assert.That(snapshot.ConnectionReuses, Is.EqualTo(1));
            Assert.That(snapshot.OpenConnections, Is.EqualTo(1));
            Assert.That(snapshot.IdleConnections, Is.EqualTo(1));
            var connection = snapshot.Connections.Single();
            Assert.That(connection.RemoteAddress, Is.EqualTo($"127.0.0.1:{server.Port}"));
            Assert.That(connection.LocalAddress, Does.StartWith("127.0.0.1:"));
            Assert.That(connection.RequestCount, Is.EqualTo(2));
            Assert.That(connection.OpenedAt, Is.Not.Null);
            Assert.That(records.Select(x => x.ConnectionId), Is.EqualTo(new long?[] { connection.Id, connection.Id }));
            Assert.That(records.Select(x => x.IsConnectionReused), Is.EqualTo(new bool?[] { false, true }));
            Assert.That(CurlStatistics.GetHandlers(), Does.Contain(handler.Statistics));
            Assert.That(CurlStatistics.LibCurlReferenceCount, Is.GreaterThan(0));
        });

        /// <summary>Checks that a redirect followed on the same connection counts as one opened connection serving two requests.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Redirect_KeepsConnection() => UniTask.ToCoroutine(async () =>
        {
            using var server = new KeepAliveHttpServer();
            using var handler = new CurlHttpMessageHandler();
            using var client = new HttpClient(handler, false);

            Assert.That(await GetAsync(client, server.Url + "redirect"), Is.EqualTo("ok"));
            Assert.That(await GetAsync(client, server.Url + "again"), Is.EqualTo("ok"));
            var snapshot = handler.Statistics.GetSnapshot();

            Assert.That(server.RequestCount, Is.EqualTo(3));
            Assert.That(server.ConnectionCount, Is.EqualTo(1));
            Assert.That(snapshot.TotalRequests, Is.EqualTo(2));
            Assert.That(snapshot.ConnectionsOpened, Is.EqualTo(1));
            Assert.That(snapshot.ConnectionReuses, Is.EqualTo(1), "Only the second request took the connection from the pool.");
            Assert.That(snapshot.Connections.Single().RequestCount, Is.EqualTo(3));
            Assert.That(snapshot.IdleConnections, Is.EqualTo(1));
        });

        /// <summary>Checks that a response with <c>Connection: close</c> removes its connection from the pool.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ConnectionClose_RemovesConnection() => UniTask.ToCoroutine(async () =>
        {
            using var server = new KeepAliveHttpServer();
            using var handler = new CurlHttpMessageHandler();
            using var client = new HttpClient(handler, false);

            await GetAsync(client, server.Url + "close");
            var snapshot = handler.Statistics.GetSnapshot();

            Assert.That(snapshot.ConnectionsOpened, Is.EqualTo(1));
            Assert.That(snapshot.ConnectionsClosed, Is.EqualTo(1));
            Assert.That(snapshot.OpenConnections, Is.Zero);
        });

        /// <summary>Checks that an idle connection the server dropped stays pooled until the next request finds it dead.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DroppedIdleConnection_IsClosedByNextRequest() => UniTask.ToCoroutine(async () =>
        {
            using var server = new KeepAliveHttpServer();
            using var handler = new CurlHttpMessageHandler();
            using var client = new HttpClient(handler, false);

            await GetAsync(client, server.Url + "drop");
            var dropped = handler.Statistics.GetSnapshot();
            // Gives the server's close time to reach the client socket, so libcurl's liveness check sees it.
            await Task.Delay(300);
            await GetAsync(client, server.Url + "next");
            var snapshot = handler.Statistics.GetSnapshot();

            Assert.That(dropped.IdleConnections, Is.EqualTo(1), "libcurl keeps the connection until it next looks for one.");
            Assert.That(server.ConnectionCount, Is.EqualTo(2));
            Assert.That(snapshot.ConnectionsOpened, Is.EqualTo(2));
            Assert.That(snapshot.ConnectionsClosed, Is.EqualTo(1));
            Assert.That(snapshot.OpenConnections, Is.EqualTo(1));
            Assert.That(snapshot.Connections.Single().Id, Is.Not.EqualTo(dropped.Connections.Single().Id));
        });

        /// <summary>Checks that concurrent requests use one connection each, in use while they wait and idle afterwards.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ConcurrentRequests_UseSeparateConnections() => UniTask.ToCoroutine(async () =>
        {
            using var server = new KeepAliveHttpServer();
            using var handler = new CurlHttpMessageHandler();
            using var client = new HttpClient(handler, false);

            var first = GetAsync(client, server.Url + "hold");
            var second = GetAsync(client, server.Url + "hold");
            await WaitUntilAsync(() => server.HeldCount == 2, "both requests to reach the server");
            await WaitUntilAsync(() => handler.Statistics.GetSnapshot().ActiveConnections == 2, "both connections to be reported");
            var busy = handler.Statistics.GetSnapshot();
            server.ReleaseHeld();
            await Task.WhenAll(first, second);
            var idle = handler.Statistics.GetSnapshot();

            Assert.That(busy.RunningRequests, Is.EqualTo(2));
            Assert.That(busy.OpenConnections, Is.EqualTo(2));
            Assert.That(busy.IdleConnections, Is.Zero);
            Assert.That(busy.Connections.Select(x => x.ActiveTransfers), Is.EqualTo(new[] { 1, 1 }));
            Assert.That(idle.RunningRequests, Is.Zero);
            Assert.That(idle.IdleConnections, Is.EqualTo(2));
            Assert.That(idle.CompletedRequests, Is.EqualTo(2));
        });

        /// <summary>Checks the failure and cancellation counters, and that an aborted connection leaves the pool.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator FailedAndCancelledRequests_AreCounted() => UniTask.ToCoroutine(async () =>
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var closedPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            using var server = new KeepAliveHttpServer();
            using var handler = new CurlHttpMessageHandler();
            using var client = new HttpClient(handler, false);
            using var cancellation = new CancellationTokenSource();

            await NetAsyncAssert.ThrowsAsync<HttpRequestException>(() => GetAsync(client, $"http://127.0.0.1:{closedPort}/"));
            var held = client.GetAsync(server.Url + "hold", cancellation.Token);
            await WaitUntilAsync(() => server.HeldCount == 1, "the request to reach the server");
            await WaitUntilAsync(() => handler.Statistics.GetSnapshot().ActiveConnections == 1, "the connection to be reported");
            cancellation.Cancel();
            await NetAsyncAssert.CompletesWithin(held.ContinueWith(_ => { }), TimeoutMs, "The cancelled request");
            await WaitUntilAsync(() => handler.Statistics.GetSnapshot().RunningRequests == 0, "the request to leave libcurl");
            var snapshot = handler.Statistics.GetSnapshot();

            Assert.That(held.IsCanceled, Is.True);
            Assert.That(snapshot.TotalRequests, Is.EqualTo(2));
            Assert.That(snapshot.FailedRequests, Is.EqualTo(1));
            Assert.That(snapshot.CancelledRequests, Is.EqualTo(1));
            Assert.That(snapshot.CompletedRequests, Is.Zero);
            Assert.That(snapshot.ConnectionsOpened, Is.EqualTo(1), "The refused connect never opened a connection.");
            Assert.That(snapshot.ConnectionsClosed, Is.EqualTo(1), "libcurl closes the connection of an aborted transfer.");
            Assert.That(snapshot.OpenConnections, Is.Zero);
            server.ReleaseHeld();
        });

        /// <summary>Checks that disposing the handler closes its pool and keeps it listed as disposed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Dispose_ClosesPool() => UniTask.ToCoroutine(async () =>
        {
            using var server = new KeepAliveHttpServer();
            var handler = new CurlHttpMessageHandler();
            var statistics = handler.Statistics;
            using (var client = new HttpClient(handler, false))
            {
                await GetAsync(client, server.Url);
            }
            Assert.That(statistics.GetSnapshot().IdleConnections, Is.EqualTo(1));

            handler.Dispose();
            var snapshot = statistics.GetSnapshot();

            Assert.That(snapshot.IsDisposed, Is.True);
            Assert.That(snapshot.OpenConnections, Is.Zero);
            Assert.That(snapshot.ConnectionsClosed, Is.EqualTo(1));
            Assert.That(snapshot.CompletedRequests, Is.EqualTo(1));
            Assert.That(CurlStatistics.GetHandlers(), Does.Contain(statistics));
        });

        /// <summary>Checks that requests sent with tracking off are counted but report no connection.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TrackingOff_CountsRequestsOnly() => UniTask.ToCoroutine(async () =>
        {
            CurlStatistics.IsConnectionTrackingEnabled = false;
            CurlNetworkMonitor.IsEnabled = false;
            using var server = new KeepAliveHttpServer();
            using var handler = new CurlHttpMessageHandler();
            using var client = new HttpClient(handler, false);

            await GetAsync(client, server.Url);
            await GetAsync(client, server.Url);
            var snapshot = handler.Statistics.GetSnapshot();

            Assert.That(snapshot.TotalRequests, Is.EqualTo(2));
            Assert.That(snapshot.CompletedRequests, Is.EqualTo(2));
            Assert.That(snapshot.IsTrackingConnections, Is.False);
            Assert.That(snapshot.ConnectionsOpened, Is.Zero);
            Assert.That(snapshot.OpenConnections, Is.Zero);
            Assert.That(server.ConnectionCount, Is.EqualTo(1), "libcurl reuses the connection either way.");
        });
    }
}
