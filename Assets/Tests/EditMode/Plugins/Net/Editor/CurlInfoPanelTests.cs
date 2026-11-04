#nullable enable
using System;
using System.Globalization;
using System.Linq;
using MajdataPlay.Net.Curl.Diagnostics;
using MajdataPlay.Net.Editor;
using MajdataPlay.Tests.Plugins.Net.Curl.Diagnostics;
using NUnit.Framework;

namespace MajdataPlay.Tests.Plugins.Net.Editor
{
    /// <summary>
    /// Covers the Info tab of the Curl Debugger (<see cref="CurlInfoPanel"/>): refreshing, the totals over every handler
    /// and the rows of its sections and tables.
    /// </summary>
    public class CurlInfoPanelTests
    {
        /// <summary>Restores the handler list after each test.</summary>
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

        /// <summary>
        /// Creates handler statistics with four requests (one queued, one running on connection #0, two finished on
        /// connection #1, the second reusing it) and a pool of two connections, #1 idle.
        /// </summary>
        /// <param name="maxPerHost">The applied <c>CURLMOPT_MAX_HOST_CONNECTIONS</c>.</param>
        /// <param name="maxCached">The applied <c>CURLMOPT_MAXCONNECTS</c>, 0 for libcurl's default.</param>
        /// <returns>The registered statistics.</returns>
        private static CurlHandlerStatistics CreateBusyHandler(int maxPerHost, int maxCached = 0)
        {
            var statistics = CurlStatistics.Register();
            statistics.OnPoolLimitsApplied(maxPerHost, maxCached);
            for (var i = 0; i < 4; i++)
            {
                statistics.OnRequestQueued();
            }
            for (var i = 0; i < 3; i++)
            {
                statistics.OnRequestDequeued();
                statistics.OnRequestAttached();
            }
            statistics.OnConnectionUsed(-1, 0, true, "10.0.0.1:443", "10.0.0.2:5000");
            statistics.OnConnectionUsed(-1, 1, true, "10.0.0.1:443", "10.0.0.2:5001");
            statistics.OnRequestDetached(1);
            statistics.OnRequestFinished(CurlNetworkRequestState.Completed);
            statistics.OnConnectionUsed(-1, 1, false, "10.0.0.1:443", "10.0.0.2:5001");
            statistics.OnRequestDetached(1);
            statistics.OnRequestFinished(CurlNetworkRequestState.Failed);
            return statistics;
        }

        /// <summary>Checks that a refresh takes snapshots of every handler and reads the library information.</summary>
        [Test]
        public void Refresh_LoadsHandlersAndLibrary()
        {
            var first = CurlStatistics.Register();
            var second = CreateBusyHandler(8);
            var panel = new CurlInfoPanel();

            panel.Refresh();

            Assert.That(panel.Handlers.Select(x => x.Id), Is.EqualTo(new[] { first.Id, second.Id }));
            Assert.That(panel.Library, Is.Not.Null);
            Assert.That(panel.Library!.Protocols, Does.Contain("https"));
        }

        /// <summary>Checks that the totals add up every handler and take the limit of live handlers only.</summary>
        [Test]
        public void GetTotals_AddsUpHandlers()
        {
            var busy = CreateBusyHandler(8);
            var disposed = CreateBusyHandler(16);
            CurlStatistics.OnHandlerDisposed(disposed);

            var totals = CurlInfoPanel.GetTotals(new[] { busy.GetSnapshot(), disposed.GetSnapshot() });

            Assert.That(totals.AliveHandlers, Is.EqualTo(1));
            Assert.That(totals.DisposedHandlers, Is.EqualTo(1));
            Assert.That(totals.TotalRequests, Is.EqualTo(8));
            Assert.That(totals.QueuedRequests, Is.EqualTo(1), "A disposed handler has nothing queued.");
            Assert.That(totals.RunningRequests, Is.EqualTo(1));
            Assert.That(totals.CompletedRequests, Is.EqualTo(2));
            Assert.That(totals.FailedRequests, Is.EqualTo(2));
            Assert.That(totals.OpenConnections, Is.EqualTo(2));
            Assert.That(totals.IdleConnections, Is.EqualTo(1));
            Assert.That(totals.ActiveConnections, Is.EqualTo(1));
            Assert.That(totals.ConnectionsOpened, Is.EqualTo(4));
            Assert.That(totals.ConnectionReuses, Is.EqualTo(2));
            Assert.That(totals.ConnectionsClosed, Is.EqualTo(2));
            Assert.That(totals.MaxConnectionsPerHost, Is.EqualTo(8));
        }

        /// <summary>
        /// Checks that the idle limit row shows the limit shared by the live handlers, libcurl's default when none is set,
        /// and a note when the live handlers differ, ignoring disposed handlers.
        /// </summary>
        [Test]
        public void IdleLimit_DescribesLiveHandlerLimits()
        {
            var four = CreateBusyHandler(8, 4);
            var otherFour = CreateBusyHandler(8, 4);
            var unset = CreateBusyHandler(8);
            var disposed = CreateBusyHandler(8, 16);
            CurlStatistics.OnHandlerDisposed(disposed);

            var shared = CurlInfoPanel.GetTotals(new[] { four.GetSnapshot(), otherFour.GetSnapshot(), disposed.GetSnapshot() });
            var mixed = CurlInfoPanel.GetTotals(new[] { four.GetSnapshot(), unset.GetSnapshot() });
            var handlerRows = CurlInfoPanel.GetHandlerRows(new[] { four.GetSnapshot(), unset.GetSnapshot() });

            Assert.That(shared.HasMixedMaxCachedConnections, Is.False);
            Assert.That(CurlInfoPanel.FormatIdleLimit(shared), Is.EqualTo("4 connections per handler (CURLMOPT_MAXCONNECTS)"));
            Assert.That(mixed.HasMixedMaxCachedConnections, Is.True);
            Assert.That(CurlInfoPanel.FormatIdleLimit(mixed), Does.StartWith("Differs between handlers"));
            Assert.That(CurlInfoPanel.FormatIdleLimit(CurlInfoPanel.GetTotals(new[] { unset.GetSnapshot() })), Does.StartWith("libcurl default"));
            Assert.That(CurlInfoPanel.FormatIdleLimit(CurlInfoPanel.GetTotals(new[] { disposed.GetSnapshot() })), Is.EqualTo("-"));
            Assert.That(handlerRows.Select(x => x[x.Length - 1]), Is.EqualTo(new[] { "4", "default" }));
        }

        /// <summary>Checks the rows of the Requests and Connection Pool sections.</summary>
        [Test]
        public void SectionRows_DescribeTotals()
        {
            var totals = CurlInfoPanel.GetTotals(new[] { CreateBusyHandler(8).GetSnapshot() });

            var requests = CurlInfoPanel.GetRequestRows(totals).ToDictionary(x => x.Key, x => x.Value);
            var pool = CurlInfoPanel.GetPoolRows(totals, true).ToDictionary(x => x.Key, x => x.Value);
            var untracked = CurlInfoPanel.GetPoolRows(new CurlInfoPanel.Totals(), false).ToDictionary(x => x.Key, x => x.Value);

            Assert.That(requests["Handlers"], Is.EqualTo("1 alive, 0 disposed"));
            Assert.That(requests["Total"], Is.EqualTo("4"));
            Assert.That(requests["Queued"], Does.StartWith("1 "));
            Assert.That(requests["Running"], Does.StartWith("1 "));
            Assert.That(requests["Completed"], Is.EqualTo("1"));
            Assert.That(requests["Failed"], Is.EqualTo("1"));
            Assert.That(requests["Cancelled"], Is.EqualTo("0"));
            Assert.That(pool["Connection tracking"], Is.EqualTo("On"));
            Assert.That(pool["Pool size"], Is.EqualTo("2 open connections"));
            Assert.That(pool["Reusable (idle)"], Is.EqualTo("1"));
            Assert.That(pool["In use"], Is.EqualTo("1"));
            Assert.That(pool["Opened"], Is.EqualTo("2"));
            Assert.That(pool["Reused"], Does.StartWith("1 "));
            Assert.That(pool["Reuse rate"], Is.EqualTo("33%"));
            Assert.That(pool["Max per host"], Does.StartWith("8 "));
            Assert.That(pool["Idle limit"], Does.StartWith("libcurl default"));
            Assert.That(untracked["Connection tracking"], Does.StartWith("Off"));
            Assert.That(untracked["Reuse rate"], Is.EqualTo("-"));
            Assert.That(untracked["Max per host"], Is.EqualTo("-"));
            Assert.That(untracked["Idle limit"], Is.EqualTo("-"));
        }

        /// <summary>Checks the handler and connection table rows.</summary>
        [Test]
        public void TableRows_DescribeHandlersAndConnections()
        {
            var busy = CreateBusyHandler(8);
            busy.OnConnectionUsed(-1, 9, false, "10.0.0.3:80", "10.0.0.2:6000");
            var snapshot = busy.GetSnapshot();
            var now = snapshot.Connections.Max(x => x.LastUsedAt).AddSeconds(2);

            var handlers = CurlInfoPanel.GetHandlerRows(new[] { snapshot });
            var connections = CurlInfoPanel.GetConnectionRows(new[] { snapshot }, now);

            Assert.That(handlers.Single(), Is.EqualTo(new[]
            {
                busy.Id.ToString(CultureInfo.InvariantCulture), "Alive", snapshot.CreatedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                "4", "1", "1", "1", "1", "0", "3", "1", "2", "default",
            }));
            Assert.That(connections.Select(x => x[1]), Is.EqualTo(new[] { "#0", "#1", "#9" }));
            Assert.That(connections[0], Is.EqualTo(new[]
            {
                "#" + busy.Id.ToString(CultureInfo.InvariantCulture), "#0", "10.0.0.1:443", "10.0.0.2:5000", "In use", "1", "1",
                snapshot.Connections[0].OpenedAt!.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                CurlNetworkFormatter.FormatDuration(now - snapshot.Connections[0].LastUsedAt) + " ago",
            }));
            Assert.That(connections[1][4], Is.EqualTo("Idle"));
            Assert.That(connections[1][6], Is.EqualTo("2"));
            Assert.That(connections[2][7], Is.EqualTo("before tracking"));
        }

        /// <summary>Checks the feature list, including unknown bits and no features.</summary>
        [Test]
        public void FormatFeatures_ListsNames()
        {
            Assert.That(CurlInfoPanel.FormatFeatures(CurlFeatures.Http2 | CurlFeatures.Ssl | CurlFeatures.Libz), Is.EqualTo("Ssl, Libz, Http2"));
            Assert.That(CurlInfoPanel.FormatFeatures(CurlFeatures.IPv6 | (CurlFeatures)int.MinValue), Is.EqualTo("IPv6, 0x80000000"));
            Assert.That(CurlInfoPanel.FormatFeatures(CurlFeatures.None), Is.EqualTo("none"));
        }

        /// <summary>Checks the rows of the libcurl section.</summary>
        [Test]
        public void GetLibraryRows_DescribeLibrary()
        {
            var library = new CurlLibraryInfo
            {
                Version = "8.21.0-DEV",
                VersionNumber = 0x081500,
                Host = "x86_64-pc-win32",
                SslVersion = "OpenSSL/3.6.2",
                Protocols = new[] { "http", "https" },
                Features = CurlFeatures.Ssl,
            };

            var rows = CurlInfoPanel.GetLibraryRows(library, 3).ToDictionary(x => x.Key, x => x.Value);

            Assert.That(rows["Version"], Is.EqualTo("8.21.0-DEV (0x081500)"));
            Assert.That(rows["Build target"], Is.EqualTo("x86_64-pc-win32"));
            Assert.That(rows["TLS"], Is.EqualTo("OpenSSL/3.6.2"));
            Assert.That(rows["zlib"], Is.EqualTo("none"));
            Assert.That(rows["Protocols"], Is.EqualTo("http, https"));
            Assert.That(rows["Features"], Is.EqualTo("Ssl"));
            Assert.That(rows["Global references"], Does.StartWith("3 "));
        }
    }
}
