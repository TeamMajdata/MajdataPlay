#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MajdataPlay.Net.Curl.Diagnostics;
using MajdataPlay.Net.Editor;
using MajdataPlay.Tests.Plugins.Net.Curl.Diagnostics;
using NUnit.Framework;
using UnityEditor.IMGUI.Controls;

namespace MajdataPlay.Tests.Plugins.Net.Editor
{
    /// <summary>
    /// Covers the <see cref="CurlNetworkPanel"/> of the Curl Debugger: the recording switch, loading the history into the
    /// table, filtering, sorting, selection and the details panel, the status bar, the copy commands and refreshing.
    /// The monitor is isolated for each test and restored afterwards.
    /// </summary>
    public class CurlNetworkPanelTests
    {
        /// <summary>Restores the monitor after each test.</summary>
        private CurlNetworkMonitorIsolation _isolation = null!;

        /// <summary>The panel under test.</summary>
        private CurlNetworkPanel? _panel;

        /// <summary>The number of repaints the panel asked for.</summary>
        private int _repaints;

        /// <summary>Saves the monitor state.</summary>
        [SetUp]
        public void SetUp()
        {
            _isolation = new CurlNetworkMonitorIsolation();
            _repaints = 0;
        }

        /// <summary>Disables the panel and restores the monitor state.</summary>
        [TearDown]
        public void TearDown()
        {
            _panel?.Disable();
            _isolation.Dispose();
        }

        /// <summary>Creates and enables the panel, which loads the history.</summary>
        /// <returns>The panel.</returns>
        private CurlNetworkPanel CreatePanel()
        {
            _panel = new CurlNetworkPanel();
            _panel.Enable(() => _repaints++);
            return _panel;
        }

        /// <summary>Gets the panel's table.</summary>
        /// <param name="panel">The panel.</param>
        /// <returns>The table.</returns>
        private static CurlNetworkTable GetTable(CurlNetworkPanel panel)
        {
            return PrivateMember.GetField<CurlNetworkTable>(panel, "_table");
        }

        /// <summary>Gets the panel's details view.</summary>
        /// <param name="panel">The panel.</param>
        /// <returns>The details view.</returns>
        private static CurlNetworkDetailsView GetDetails(CurlNetworkPanel panel)
        {
            return PrivateMember.GetField<CurlNetworkDetailsView>(panel, "_details");
        }

        /// <summary>Gets the names of the listed requests in display order.</summary>
        /// <param name="panel">The panel.</param>
        /// <returns>The row names.</returns>
        private static string[] RowNames(CurlNetworkPanel panel)
        {
            return GetTable(panel).VisibleSnapshots.Select(x => x.Name).ToArray();
        }

        /// <summary>Selects a request the way a click does.</summary>
        /// <param name="panel">The panel.</param>
        /// <param name="record">The request to select.</param>
        internal static void Select(CurlNetworkPanel panel, CurlNetworkRecord record)
        {
            GetTable(panel).SetSelection(new List<int> { CurlNetworkTable.GetItemId(record.Id) }, TreeViewSelectionOptions.FireSelectionChanged);
        }

        /// <summary>Records one request of every kind: OK, client error, running and failed.</summary>
        /// <returns>The records in that order.</returns>
        private static CurlNetworkRecord[] RegisterMixedHistory()
        {
            var ok = CurlNetworkMonitorIsolation.Complete("http://example.test/ok", 200, "{\"ok\":true}", "Content-Type: application/json", "Content-Length: 11");
            var missing = CurlNetworkMonitorIsolation.Complete("http://example.test/missing", 404, "missing");
            var running = CurlNetworkMonitorIsolation.Register("GET", "http://example.test/running");
            running.OnSubmitted();
            var failed = CurlNetworkMonitorIsolation.Register("POST", "http://example.test/failed", "{}");
            failed.OnFailed(new InvalidOperationException("Curl Error 7 (CouldNotConnect)"));
            return new[] { ok, missing, running, failed };
        }

        /// <summary>Checks that enabling the panel applies its recording switch and lists the history in request order.</summary>
        [Test]
        public void Enable_AppliesRecordingAndLoadsHistory()
        {
            RegisterMixedHistory();
            CurlNetworkMonitor.IsEnabled = false;

            var panel = CreatePanel();

            Assert.That(panel.IsRecording, Is.True);
            Assert.That(CurlNetworkMonitor.IsEnabled, Is.True);
            Assert.That(RowNames(panel), Is.EqualTo(new[] { "ok", "missing", "running", "failed" }));
            Assert.That(GetTable(panel).HasPendingRows, Is.True);
        }

        /// <summary>Checks that the Record toggle turns recording off and on again.</summary>
        [Test]
        public void SetRecording_TogglesMonitor()
        {
            var panel = CreatePanel();

            panel.SetRecording(false);
            Assert.That(CurlNetworkMonitor.IsEnabled, Is.False);
            Assert.That(panel.IsRecording, Is.False);
            panel.SetRecording(true);

            Assert.That(CurlNetworkMonitor.IsEnabled, Is.True);
        }

        /// <summary>Checks the text filter and every state filter.</summary>
        [Test]
        public void Refresh_AppliesFilters()
        {
            RegisterMixedHistory();
            var panel = CreatePanel();

            PrivateMember.SetField(panel, "_filter", "MISS");
            panel.Refresh();
            Assert.That(RowNames(panel), Is.EqualTo(new[] { "missing" }));

            PrivateMember.SetField(panel, "_filter", string.Empty);
            PrivateMember.SetField(panel, "_stateFilter", CurlNetworkStateFilter.Pending);
            panel.Refresh();
            Assert.That(RowNames(panel), Is.EqualTo(new[] { "running" }));

            PrivateMember.SetField(panel, "_stateFilter", CurlNetworkStateFilter.Finished);
            panel.Refresh();
            Assert.That(RowNames(panel), Is.EqualTo(new[] { "ok", "missing", "failed" }));

            PrivateMember.SetField(panel, "_stateFilter", CurlNetworkStateFilter.Errors);
            panel.Refresh();
            Assert.That(RowNames(panel), Is.EqualTo(new[] { "missing", "failed" }));
            Assert.That(GetTable(panel).HasPendingRows, Is.False);
        }

        /// <summary>Checks sorting by name and size in both directions, and that every column can be sorted.</summary>
        [Test]
        public void Sorting_OrdersRowsByColumn()
        {
            CurlNetworkMonitorIsolation.Complete("http://example.test/b", 200, "12345");
            CurlNetworkMonitorIsolation.Complete("http://example.test/a", 200, "1");
            CurlNetworkMonitorIsolation.Complete("http://example.test/c", 200, "123");
            var panel = CreatePanel();
            var header = GetTable(panel).multiColumnHeader;

            header.SetSorting((int)CurlNetworkColumn.Name, true);
            Assert.That(RowNames(panel), Is.EqualTo(new[] { "a", "b", "c" }));
            header.SetSorting((int)CurlNetworkColumn.Name, false);
            Assert.That(RowNames(panel), Is.EqualTo(new[] { "c", "b", "a" }));
            header.SetSorting((int)CurlNetworkColumn.Size, true);
            Assert.That(RowNames(panel), Is.EqualTo(new[] { "a", "c", "b" }));

            foreach (CurlNetworkColumn column in Enum.GetValues(typeof(CurlNetworkColumn)))
            {
                header.SetSorting((int)column, (int)column % 2 == 0);
                Assert.That(RowNames(panel).Length, Is.EqualTo(3), $"Sorting by {column} lost rows.");
            }
        }

        /// <summary>Checks that selecting a request opens its details and that closing them clears the selection.</summary>
        [Test]
        public void Selection_OpensAndClosesDetails()
        {
            var records = RegisterMixedHistory();
            var panel = CreatePanel();

            Assert.That(GetDetails(panel).Record, Is.Null);
            Select(panel, records[1]);

            Assert.That(GetTable(panel).SelectedSnapshot!.Id, Is.EqualTo(records[1].Id));
            Assert.That(GetDetails(panel).Record, Is.SameAs(records[1]));
            Assert.That(_repaints, Is.GreaterThan(0));

            PrivateMember.Invoke(panel, "OnDetailsCloseRequested");

            Assert.That(GetTable(panel).GetSelection(), Is.Empty);
            Assert.That(GetDetails(panel).Record, Is.Null);
        }

        /// <summary>Checks that a refresh keeps the details of the selected request up to date and closes them once it is cleared.</summary>
        [Test]
        public void Refresh_UpdatesSelectedDetails()
        {
            var record = CurlNetworkMonitorIsolation.Register("GET", "http://example.test/slow");
            record.OnSubmitted();
            var panel = CreatePanel();
            Select(panel, record);
            Assert.That(GetDetails(panel).Snapshot!.State, Is.EqualTo(CurlNetworkRequestState.Running));

            record.OnCompleted();
            panel.Refresh();
            Assert.That(GetDetails(panel).Snapshot!.State, Is.EqualTo(CurlNetworkRequestState.Completed));

            CurlNetworkMonitor.Clear();
            panel.Refresh();
            Assert.That(GetDetails(panel).Record, Is.Null);
            Assert.That(RowNames(panel), Is.Empty);
        }

        /// <summary>Checks that Update refreshes only when the history changed and asks for repaints while requests run.</summary>
        [Test]
        public void Update_RefreshesWhenHistoryChanges()
        {
            var panel = CreatePanel();

            Assert.That(panel.Update(), Is.False, "Nothing changed and nothing is running.");
            var record = CurlNetworkMonitorIsolation.Register("GET", "http://example.test/late");

            Assert.That(panel.Update(), Is.True);
            Assert.That(RowNames(panel), Is.EqualTo(new[] { "late" }));
            Assert.That(PrivateMember.GetField<int>(panel, "_lastVersion"), Is.EqualTo(CurlNetworkMonitor.Version));
            Assert.That(panel.Update(), Is.True, "A pending request keeps its durations moving.");

            record.OnCompleted();
            panel.Update();

            Assert.That(panel.Update(), Is.False);
        }

        /// <summary>Checks the status bar text with and without filters and while recording is paused.</summary>
        [Test]
        public void GetStatusText_SummarizesHistory()
        {
            var snapshots = RegisterMixedHistory().Select(x => x.GetSnapshot()).ToArray();
            var visible = snapshots.Take(3).ToArray();

            var all = CurlNetworkPanel.GetStatusText(snapshots, snapshots, true);
            var filtered = CurlNetworkPanel.GetStatusText(visible, snapshots, false);

            Assert.That(all, Does.StartWith("4 requests | "));
            Assert.That(all, Does.Contain("1 pending"));
            Assert.That(all, Does.Contain("bodies kept"));
            Assert.That(all, Does.Not.Contain("paused"));
            Assert.That(filtered, Does.StartWith("3 / 4 requests | "));
            Assert.That(filtered, Does.EndWith("Recording paused"));
        }

        /// <summary>Checks the text of every copy command.</summary>
        [Test]
        public void GetCopyText_CoversEveryTarget()
        {
            var record = CurlNetworkMonitorIsolation.Complete("http://example.test/data", 200, "payload", "X-Server: test");
            record.OnRequestHeadersSent(Encoding.ASCII.GetBytes("GET /data HTTP/1.1\r\nHost: example.test\r\nAccept: */*\r\n\r\n"));
            var snapshot = record.GetSnapshot();

            Assert.That(CurlNetworkPanel.GetCopyText(CurlNetworkPanel.CopyTarget.Url, snapshot, record), Is.EqualTo("http://example.test/data"));
            Assert.That(CurlNetworkPanel.GetCopyText(CurlNetworkPanel.CopyTarget.Curl, snapshot, record), Is.EqualTo("curl 'http://example.test/data' \\\n  -H 'Accept: */*'"));
            Assert.That(CurlNetworkPanel.GetCopyText(CurlNetworkPanel.CopyTarget.RequestHeaders, snapshot, record), Is.EqualTo("GET /data HTTP/1.1\nHost: example.test\nAccept: */*"));
            Assert.That(CurlNetworkPanel.GetCopyText(CurlNetworkPanel.CopyTarget.ResponseHeaders, snapshot, record), Is.EqualTo("HTTP/1.1 200 Status\nX-Server: test"));
            Assert.That(CurlNetworkPanel.GetCopyText(CurlNetworkPanel.CopyTarget.ResponseBody, snapshot, record), Is.EqualTo("payload"));
        }

        /// <summary>Checks the General rows of the details panel, including the connection a request used.</summary>
        [Test]
        public void GetGeneralRows_DescribesRequest()
        {
            var snapshot = new CurlNetworkSnapshot
            {
                State = CurlNetworkRequestState.Failed,
                Url = "http://example.test/start",
                EffectiveUrl = "http://example.test/final",
                Method = "GET",
                Error = "Curl Error 28",
                RedirectCount = 1,
                ConnectionId = 3,
                IsConnectionReused = true,
                StartedAt = new DateTime(2026, 11, 1, 12, 30, 45, 123),
            };

            var rows = CurlNetworkDetailsView.GetGeneralRows(snapshot).ToDictionary(x => x.Key, x => x.Value);

            Assert.That(rows["Request URL"], Is.EqualTo("http://example.test/start"));
            Assert.That(rows["Effective URL"], Is.EqualTo("http://example.test/final"));
            Assert.That(rows["Status Code"], Is.EqualTo("(failed)"));
            Assert.That(rows["State"], Is.EqualTo("Failed"));
            Assert.That(rows["Error"], Is.EqualTo("Curl Error 28"));
            Assert.That(rows["Remote Address"], Is.EqualTo("-"));
            Assert.That(rows["Connection"], Is.EqualTo("#3 (reused)"));
            Assert.That(rows["Started"], Is.EqualTo("2026-11-01 12:30:45.123"));
            Assert.That(rows["Redirects"], Is.EqualTo("1"));
            var minimal = CurlNetworkDetailsView.GetGeneralRows(new CurlNetworkSnapshot { Url = "u", EffectiveUrl = "u" });
            Assert.That(minimal.Any(x => x.Key == "Effective URL" || x.Key == "Connection"), Is.False);
        }

        /// <summary>Checks the connection descriptions.</summary>
        [Test]
        public void FormatConnection_ShowsReuse()
        {
            Assert.That(CurlNetworkDetailsView.FormatConnection(3, true), Is.EqualTo("#3 (reused)"));
            Assert.That(CurlNetworkDetailsView.FormatConnection(4, false), Is.EqualTo("#4 (new)"));
            Assert.That(CurlNetworkDetailsView.FormatConnection(5, null), Is.EqualTo("#5"));
        }

        /// <summary>Checks the body size descriptions.</summary>
        [Test]
        public void GetBodyInfo_DescribesCapture()
        {
            Assert.That(CurlNetworkDetailsView.GetBodyInfo(2048, 2048, false), Is.EqualTo("2.0 KiB"));
            Assert.That(CurlNetworkDetailsView.GetBodyInfo(5 * 1024 * 1024, 1024 * 1024, true), Is.EqualTo("5.0 MiB (first 1.0 MiB kept)"));
            Assert.That(CurlNetworkDetailsView.GetBodyInfo(1200, 0, true), Is.EqualTo("1.2 KiB (not kept)"));
        }

        /// <summary>Checks that wrapping long lines and splitting line breaks keeps every character.</summary>
        [Test]
        public void TextView_SplitLinesWrapsLongLines()
        {
            var longLine = new string('x', CurlNetworkTextView.MaxLineLength + 5);

            var lines = CurlNetworkTextView.SplitLines("a\r\n\n" + longLine + "\nb");

            Assert.That(lines.Select(x => x.Length), Is.EqualTo(new[] { 1, 0, CurlNetworkTextView.MaxLineLength, 5, 1 }));
            Assert.That(CurlNetworkTextView.SplitLines(string.Empty), Is.EqualTo(new[] { string.Empty }));
        }
    }
}
