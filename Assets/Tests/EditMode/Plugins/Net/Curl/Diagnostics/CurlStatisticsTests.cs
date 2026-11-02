#nullable enable
using System;
using System.Linq;
using System.Text;
using MajdataPlay.Net.Curl.Diagnostics;
using NUnit.Framework;

namespace MajdataPlay.Tests.Plugins.Net.Curl.Diagnostics
{
    /// <summary>
    /// Covers <see cref="CurlHandlerStatistics"/>, the <see cref="CurlStatistics"/> handler list and the parsing of
    /// libcurl's connection messages, without a transfer.
    /// </summary>
    public class CurlStatisticsTests
    {
        /// <summary>Restores the handler list and the tracking switch after each test.</summary>
        private CurlStatisticsIsolation _isolation = null!;

        /// <summary>Saves the statistics state.</summary>
        [SetUp]
        public void SetUp()
        {
            _isolation = new CurlStatisticsIsolation();
        }

        /// <summary>Restores the statistics state.</summary>
        [TearDown]
        public void TearDown()
        {
            _isolation.Dispose();
        }

        /// <summary>Checks the request counters through a request's whole life and the final state counters.</summary>
        [Test]
        public void RequestCounters_FollowRequests()
        {
            var statistics = new CurlHandlerStatistics(1);

            statistics.OnRequestQueued();
            statistics.OnRequestQueued();
            Assert.That(statistics.GetSnapshot().QueuedRequests, Is.EqualTo(2));
            statistics.OnRequestDequeued();
            statistics.OnRequestAttached();
            var running = statistics.GetSnapshot();
            statistics.OnRequestDetached(-1);
            statistics.OnRequestFinished(CurlNetworkRequestState.Completed);
            statistics.OnRequestDequeued();
            statistics.OnRequestFinished(CurlNetworkRequestState.Cancelled);
            statistics.OnRequestFinished(CurlNetworkRequestState.Failed);
            statistics.OnRequestFinished(CurlNetworkRequestState.Running);
            statistics.OnRequestDequeued();
            var done = statistics.GetSnapshot();

            Assert.That(running.TotalRequests, Is.EqualTo(2));
            Assert.That(running.QueuedRequests, Is.EqualTo(1));
            Assert.That(running.RunningRequests, Is.EqualTo(1));
            Assert.That(done.QueuedRequests, Is.Zero, "The queued count never goes negative.");
            Assert.That(done.RunningRequests, Is.Zero);
            Assert.That(done.CompletedRequests, Is.EqualTo(1));
            Assert.That(done.CancelledRequests, Is.EqualTo(1));
            Assert.That(done.FailedRequests, Is.EqualTo(1));
            Assert.That(done.Id, Is.EqualTo(1));
            Assert.That(done.IsDisposed, Is.False);
        }

        /// <summary>Checks that a new connection is in use while its request runs and idle, so reusable, afterwards.</summary>
        [Test]
        public void Connections_TrackOpenUseAndRelease()
        {
            var statistics = new CurlHandlerStatistics(1);

            statistics.OnConnectionUsed(-1, 0, true, "127.0.0.1:80", "127.0.0.1:5000");
            var inUse = statistics.GetSnapshot();
            statistics.OnRequestDetached(0);
            var idle = statistics.GetSnapshot();

            Assert.That(inUse.IsTrackingConnections, Is.True);
            Assert.That(inUse.OpenConnections, Is.EqualTo(1));
            Assert.That(inUse.ActiveConnections, Is.EqualTo(1));
            Assert.That(inUse.IdleConnections, Is.Zero);
            Assert.That(inUse.Connections[0].IsIdle, Is.False);
            Assert.That(idle.IdleConnections, Is.EqualTo(1));
            Assert.That(idle.ConnectionsOpened, Is.EqualTo(1));
            var connection = idle.Connections.Single();
            Assert.That(connection.Id, Is.Zero);
            Assert.That(connection.RemoteAddress, Is.EqualTo("127.0.0.1:80"));
            Assert.That(connection.LocalAddress, Is.EqualTo("127.0.0.1:5000"));
            Assert.That(connection.RequestCount, Is.EqualTo(1));
            Assert.That(connection.ActiveTransfers, Is.Zero);
            Assert.That(connection.OpenedAt, Is.Not.Null);
        }

        /// <summary>Checks reuse counting, several transfers on one connection and redirects that keep or change the connection.</summary>
        [Test]
        public void Connections_CountReuseAndSharing()
        {
            var statistics = new CurlHandlerStatistics(1);
            statistics.OnConnectionUsed(-1, 0, true, "a:443", "l:1");
            statistics.OnRequestDetached(0);

            statistics.OnConnectionUsed(-1, 0, false, "a:443", "l:1");
            statistics.OnConnectionUsed(-1, 0, false, "a:443", "l:1");
            var shared = statistics.GetSnapshot();
            statistics.OnConnectionUsed(0, 0, false, "a:443", "l:1");
            statistics.OnConnectionUsed(0, 1, true, "b:443", "l:2");
            var redirected = statistics.GetSnapshot();

            Assert.That(shared.ConnectionReuses, Is.EqualTo(2));
            Assert.That(shared.Connections.Single().ActiveTransfers, Is.EqualTo(2), "HTTP/2 multiplexing shares one connection.");
            Assert.That(redirected.Connections.Select(x => x.ActiveTransfers), Is.EqualTo(new[] { 1, 1 }), "A redirect to another host moves the request.");
            Assert.That(redirected.Connections[0].RequestCount, Is.EqualTo(4), "A redirect on the same connection counts as one more request.");
            Assert.That(redirected.ConnectionReuses, Is.EqualTo(2), "Keeping the connection across a redirect is not a reuse from the pool.");
            Assert.That(redirected.ConnectionsOpened, Is.EqualTo(2));
        }

        /// <summary>Checks that a connection opened before tracking started is discovered when it is reused.</summary>
        [Test]
        public void Connections_DiscoverUntrackedConnection()
        {
            var statistics = new CurlHandlerStatistics(1);

            statistics.OnConnectionUsed(-1, 7, false, "a:80", "l:1");
            var snapshot = statistics.GetSnapshot();

            Assert.That(snapshot.Connections.Single().Id, Is.EqualTo(7));
            Assert.That(snapshot.Connections.Single().OpenedAt, Is.Null);
            Assert.That(snapshot.ConnectionsOpened, Is.Zero);
            Assert.That(snapshot.ConnectionReuses, Is.EqualTo(1));
        }

        /// <summary>Checks that closing removes a connection once, and that unknown connections and late releases are ignored.</summary>
        [Test]
        public void Connections_CloseOnce()
        {
            var statistics = new CurlHandlerStatistics(1);
            statistics.OnConnectionUsed(-1, 3, true, "a:80", "l:1");

            statistics.OnConnectionClosed(3);
            statistics.OnConnectionClosed(3);
            statistics.OnConnectionClosed(99);
            statistics.OnRequestDetached(3);
            var snapshot = statistics.GetSnapshot();

            Assert.That(snapshot.OpenConnections, Is.Zero);
            Assert.That(snapshot.ConnectionsClosed, Is.EqualTo(1));
        }

        /// <summary>Checks that disposal closes every connection, empties the queues and happens once.</summary>
        [Test]
        public void OnDisposed_ClosesEverything()
        {
            var statistics = new CurlHandlerStatistics(1);
            statistics.OnRequestQueued();
            statistics.OnRequestAttached();
            statistics.OnConnectionUsed(-1, 0, true, "a:80", "l:1");
            statistics.OnConnectionUsed(-1, 1, true, "a:80", "l:2");
            statistics.OnMaxConnectionsPerHostApplied(8);

            statistics.OnDisposed();
            var disposedAt = statistics.GetSnapshot().DisposedAt;
            statistics.OnDisposed();
            var snapshot = statistics.GetSnapshot();

            Assert.That(statistics.IsDisposed, Is.True);
            Assert.That(snapshot.IsDisposed, Is.True);
            Assert.That(snapshot.DisposedAt, Is.EqualTo(disposedAt));
            Assert.That(snapshot.OpenConnections, Is.Zero);
            Assert.That(snapshot.ConnectionsClosed, Is.EqualTo(2));
            Assert.That(snapshot.QueuedRequests, Is.Zero);
            Assert.That(snapshot.RunningRequests, Is.Zero);
            Assert.That(snapshot.TotalRequests, Is.EqualTo(1));
            Assert.That(snapshot.MaxConnectionsPerHost, Is.EqualTo(8));
        }

        /// <summary>Checks registration order, the cap on disposed handlers and clearing them.</summary>
        [Test]
        public void HandlerList_KeepsRecentDisposedHandlers()
        {
            var alive = CurlStatistics.Register();
            var disposed = Enumerable.Range(0, CurlStatistics.MaxDisposedHandlers + 2).Select(_ => CurlStatistics.Register()).ToArray();
            var newest = CurlStatistics.Register();

            Assert.That(CurlStatistics.GetHandlers().First(), Is.SameAs(alive));
            Assert.That(newest.Id, Is.GreaterThan(alive.Id));
            foreach (var handler in disposed)
            {
                CurlStatistics.OnHandlerDisposed(handler);
            }

            var listed = CurlStatistics.GetHandlers();
            Assert.That(listed.Count(x => x.IsDisposed), Is.EqualTo(CurlStatistics.MaxDisposedHandlers));
            Assert.That(listed, Has.No.Member(disposed[0]).And.No.Member(disposed[1]), "The oldest disposed handlers are dropped.");
            Assert.That(listed, Has.Member(alive).And.Member(newest));

            CurlStatistics.ClearDisposed();

            Assert.That(CurlStatistics.GetHandlers(), Is.EqualTo(new[] { alive, newest }));
        }

        /// <summary>Checks that connection tracking is on by default in the Editor.</summary>
        [Test]
        public void ConnectionTracking_IsOnByDefaultInEditor()
        {
            Assert.That(PrivateMember.GetStaticField<bool>(typeof(CurlStatistics), "DefaultConnectionTracking"), Is.True);
        }

        /// <summary>Checks the messages libcurl writes when it closes a connection.</summary>
        /// <param name="message">The informational message.</param>
        /// <param name="expected">The closed connection, or -1 for messages that close nothing.</param>
        [TestCase("shutting down connection #0\n", 0)]
        [TestCase("closing connection #12", 12)]
        [TestCase("[SHUTDOWN] closing connection #7\n", 7)]
        [TestCase("Discarding connection #3 from 5 to reach total limit of 4\n", 3)]
        [TestCase("Connection #0 to host 127.0.0.1:80 left intact\n", -1)]
        [TestCase("Connection 1 seems to be dead\n", -1)]
        [TestCase("Connection #1 is not open enough, cannot reuse\n", -1)]
        [TestCase("shutting down connection #\n", -1)]
        [TestCase("Reusing existing http: connection with host 127.0.0.1\n", -1)]
        [TestCase("", -1)]
        public void TryParseClosedConnection_RecognizesCloseMessages(string message, long expected)
        {
            var isClosed = CurlNetworkCapture.TryParseClosedConnection(Encoding.ASCII.GetBytes(message), out var connectionId);

            Assert.That(isClosed, Is.EqualTo(expected >= 0));
            Assert.That(connectionId, Is.EqualTo(expected));
        }

        /// <summary>Checks what the bundled libcurl reports about itself and the global reference count.</summary>
        [Test]
        public void GetLibraryInfo_DescribesBundledLibcurl()
        {
            var info = CurlStatistics.GetLibraryInfo();

            Assert.That(info.Version, Does.StartWith("8."));
            Assert.That(info.VersionNumber, Is.GreaterThanOrEqualTo(0x080000u));
            Assert.That(info.Host, Is.Not.Empty);
            Assert.That(info.Protocols, Does.Contain("http").And.Contain("https"));
            Assert.That(info.Features.HasFlag(CurlFeatures.Ssl), Is.True);
            Assert.That(info.SslVersion, Is.Not.Null.And.Not.Empty);
            Assert.That(CurlStatistics.LibCurlReferenceCount, Is.GreaterThanOrEqualTo(0));
        }
    }
}
