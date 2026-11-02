#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using MajdataPlay.Net.Curl.Diagnostics;
using MajdataPlay.Net.Editor;
using MajdataPlay.Tests.Plugins.Net.Curl.Diagnostics;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MajdataPlay.Tests.Plugins.Net.Editor
{
    /// <summary>
    /// Covers the <see cref="CurlNetworkWindow"/> editor window: the recording switch, loading the history into the table,
    /// filtering, sorting, selection and the details panel, the status bar, the copy commands, automatic refreshing and
    /// drawing every tab. The monitor is isolated for each test and restored afterwards.
    /// </summary>
    public class CurlNetworkWindowTests
    {
        /// <summary>Restores the monitor after each test.</summary>
        private CurlNetworkMonitorIsolation _isolation = null!;

        /// <summary>The window under test.</summary>
        private CurlNetworkWindow? _window;

        /// <summary>Saves the monitor state.</summary>
        [SetUp]
        public void SetUp()
        {
            _isolation = new CurlNetworkMonitorIsolation();
        }

        /// <summary>Closes the window and restores the monitor state.</summary>
        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                Object.DestroyImmediate(_window);
            }
            _isolation.Dispose();
        }

        /// <summary>Creates the window, which loads the history in OnEnable.</summary>
        /// <returns>The window.</returns>
        private CurlNetworkWindow CreateWindow()
        {
            _window = ScriptableObject.CreateInstance<CurlNetworkWindow>();
            return _window;
        }

        /// <summary>Gets the window's table.</summary>
        /// <param name="window">The window.</param>
        /// <returns>The table.</returns>
        private static CurlNetworkTable GetTable(CurlNetworkWindow window)
        {
            return PrivateMember.GetField<CurlNetworkTable>(window, "_table");
        }

        /// <summary>Gets the window's details panel.</summary>
        /// <param name="window">The window.</param>
        /// <returns>The details panel.</returns>
        private static CurlNetworkDetailsView GetDetails(CurlNetworkWindow window)
        {
            return PrivateMember.GetField<CurlNetworkDetailsView>(window, "_details");
        }

        /// <summary>Gets the names of the listed requests in display order.</summary>
        /// <param name="window">The window.</param>
        /// <returns>The row names.</returns>
        private static string[] RowNames(CurlNetworkWindow window)
        {
            return GetTable(window).VisibleSnapshots.Select(x => x.Name).ToArray();
        }

        /// <summary>Selects a request the way a click does.</summary>
        /// <param name="window">The window.</param>
        /// <param name="record">The request to select.</param>
        private static void Select(CurlNetworkWindow window, CurlNetworkRecord record)
        {
            GetTable(window).SetSelection(new List<int> { CurlNetworkTable.GetItemId(record.Id) }, TreeViewSelectionOptions.FireSelectionChanged);
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

        /// <summary>Checks that enabling the window turns recording on and lists the history in request order.</summary>
        [Test]
        public void OnEnable_StartsRecordingAndLoadsHistory()
        {
            RegisterMixedHistory();
            CurlNetworkMonitor.IsEnabled = false;

            var window = CreateWindow();

            Assert.That(CurlNetworkMonitor.IsEnabled, Is.True);
            Assert.That(window.titleContent.text, Is.EqualTo("Curl Network"));
            Assert.That(RowNames(window), Is.EqualTo(new[] { "ok", "missing", "running", "failed" }));
            Assert.That(GetTable(window).HasPendingRows, Is.True);
        }

        /// <summary>Checks that the Record toggle and closing the window turn recording off.</summary>
        [Test]
        public void Recording_StopsWhenToggledOffOrClosed()
        {
            var window = CreateWindow();

            PrivateMember.Invoke(window, "SetRecording", false);
            Assert.That(CurlNetworkMonitor.IsEnabled, Is.False);
            Assert.That(PrivateMember.GetField<bool>(window, "_isRecording"), Is.False);
            PrivateMember.Invoke(window, "SetRecording", true);
            Assert.That(CurlNetworkMonitor.IsEnabled, Is.True);

            Object.DestroyImmediate(window);
            _window = null;

            Assert.That(CurlNetworkMonitor.IsEnabled, Is.False);
        }

        /// <summary>Checks the text filter and every state filter.</summary>
        [Test]
        public void Refresh_AppliesFilters()
        {
            RegisterMixedHistory();
            var window = CreateWindow();

            PrivateMember.SetField(window, "_filter", "MISS");
            PrivateMember.Invoke(window, "Refresh");
            Assert.That(RowNames(window), Is.EqualTo(new[] { "missing" }));

            PrivateMember.SetField(window, "_filter", string.Empty);
            PrivateMember.SetField(window, "_stateFilter", CurlNetworkStateFilter.Pending);
            PrivateMember.Invoke(window, "Refresh");
            Assert.That(RowNames(window), Is.EqualTo(new[] { "running" }));

            PrivateMember.SetField(window, "_stateFilter", CurlNetworkStateFilter.Finished);
            PrivateMember.Invoke(window, "Refresh");
            Assert.That(RowNames(window), Is.EqualTo(new[] { "ok", "missing", "failed" }));

            PrivateMember.SetField(window, "_stateFilter", CurlNetworkStateFilter.Errors);
            PrivateMember.Invoke(window, "Refresh");
            Assert.That(RowNames(window), Is.EqualTo(new[] { "missing", "failed" }));
            Assert.That(GetTable(window).HasPendingRows, Is.False);
        }

        /// <summary>Checks sorting by name and size in both directions, and that every column can be sorted.</summary>
        [Test]
        public void Sorting_OrdersRowsByColumn()
        {
            CurlNetworkMonitorIsolation.Complete("http://example.test/b", 200, "12345");
            CurlNetworkMonitorIsolation.Complete("http://example.test/a", 200, "1");
            CurlNetworkMonitorIsolation.Complete("http://example.test/c", 200, "123");
            var window = CreateWindow();
            var header = GetTable(window).multiColumnHeader;

            header.SetSorting((int)CurlNetworkColumn.Name, true);
            Assert.That(RowNames(window), Is.EqualTo(new[] { "a", "b", "c" }));
            header.SetSorting((int)CurlNetworkColumn.Name, false);
            Assert.That(RowNames(window), Is.EqualTo(new[] { "c", "b", "a" }));
            header.SetSorting((int)CurlNetworkColumn.Size, true);
            Assert.That(RowNames(window), Is.EqualTo(new[] { "a", "c", "b" }));

            foreach (CurlNetworkColumn column in Enum.GetValues(typeof(CurlNetworkColumn)))
            {
                header.SetSorting((int)column, (int)column % 2 == 0);
                Assert.That(RowNames(window).Length, Is.EqualTo(3), $"Sorting by {column} lost rows.");
            }
        }

        /// <summary>Checks that selecting a request opens its details and that closing them clears the selection.</summary>
        [Test]
        public void Selection_OpensAndClosesDetails()
        {
            var records = RegisterMixedHistory();
            var window = CreateWindow();

            Assert.That(GetDetails(window).Record, Is.Null);
            Select(window, records[1]);

            Assert.That(GetTable(window).SelectedSnapshot!.Id, Is.EqualTo(records[1].Id));
            Assert.That(GetDetails(window).Record, Is.SameAs(records[1]));

            PrivateMember.Invoke(window, "OnDetailsCloseRequested");

            Assert.That(GetTable(window).GetSelection(), Is.Empty);
            Assert.That(GetDetails(window).Record, Is.Null);
        }

        /// <summary>Checks that a refresh keeps the details of the selected request up to date and closes them once it is cleared.</summary>
        [Test]
        public void Refresh_UpdatesSelectedDetails()
        {
            var record = CurlNetworkMonitorIsolation.Register("GET", "http://example.test/slow");
            record.OnSubmitted();
            var window = CreateWindow();
            Select(window, record);
            Assert.That(GetDetails(window).Snapshot!.State, Is.EqualTo(CurlNetworkRequestState.Running));

            record.OnCompleted();
            PrivateMember.Invoke(window, "Refresh");
            Assert.That(GetDetails(window).Snapshot!.State, Is.EqualTo(CurlNetworkRequestState.Completed));

            CurlNetworkMonitor.Clear();
            PrivateMember.Invoke(window, "Refresh");
            Assert.That(GetDetails(window).Record, Is.Null);
            Assert.That(RowNames(window), Is.Empty);
        }

        /// <summary>Checks that Update refreshes only when due and only when the history changed.</summary>
        [Test]
        public void Update_RefreshesWhenHistoryChanges()
        {
            var window = CreateWindow();
            CurlNetworkMonitorIsolation.Register("GET", "http://example.test/late");

            PrivateMember.SetField(window, "_nextRefresh", double.MaxValue);
            PrivateMember.Invoke(window, "Update");
            Assert.That(RowNames(window), Is.Empty);

            PrivateMember.SetField(window, "_nextRefresh", 0d);
            PrivateMember.Invoke(window, "Update");
            Assert.That(RowNames(window), Is.EqualTo(new[] { "late" }));
            Assert.That(PrivateMember.GetField<double>(window, "_nextRefresh"), Is.GreaterThan(EditorApplication.timeSinceStartup));
            Assert.That(PrivateMember.GetField<int>(window, "_lastVersion"), Is.EqualTo(CurlNetworkMonitor.Version));
        }

        /// <summary>Checks the status bar text with and without filters and while recording is paused.</summary>
        [Test]
        public void GetStatusText_SummarizesHistory()
        {
            var snapshots = RegisterMixedHistory().Select(x => x.GetSnapshot()).ToArray();
            var visible = snapshots.Take(3).ToArray();

            var all = CurlNetworkWindow.GetStatusText(snapshots, snapshots, true);
            var filtered = CurlNetworkWindow.GetStatusText(visible, snapshots, false);

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

            Assert.That(CurlNetworkWindow.GetCopyText(CurlNetworkWindow.CopyTarget.Url, snapshot, record), Is.EqualTo("http://example.test/data"));
            Assert.That(CurlNetworkWindow.GetCopyText(CurlNetworkWindow.CopyTarget.Curl, snapshot, record), Is.EqualTo("curl 'http://example.test/data' \\\n  -H 'Accept: */*'"));
            Assert.That(CurlNetworkWindow.GetCopyText(CurlNetworkWindow.CopyTarget.RequestHeaders, snapshot, record), Is.EqualTo("GET /data HTTP/1.1\nHost: example.test\nAccept: */*"));
            Assert.That(CurlNetworkWindow.GetCopyText(CurlNetworkWindow.CopyTarget.ResponseHeaders, snapshot, record), Is.EqualTo("HTTP/1.1 200 Status\nX-Server: test"));
            Assert.That(CurlNetworkWindow.GetCopyText(CurlNetworkWindow.CopyTarget.ResponseBody, snapshot, record), Is.EqualTo("payload"));
        }

        /// <summary>Checks the General rows of the details panel.</summary>
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
                StartedAt = new DateTime(2026, 11, 1, 12, 30, 45, 123),
            };

            var rows = CurlNetworkDetailsView.GetGeneralRows(snapshot).ToDictionary(x => x.Key, x => x.Value);

            Assert.That(rows["Request URL"], Is.EqualTo("http://example.test/start"));
            Assert.That(rows["Effective URL"], Is.EqualTo("http://example.test/final"));
            Assert.That(rows["Status Code"], Is.EqualTo("(failed)"));
            Assert.That(rows["State"], Is.EqualTo("Failed"));
            Assert.That(rows["Error"], Is.EqualTo("Curl Error 28"));
            Assert.That(rows["Remote Address"], Is.EqualTo("-"));
            Assert.That(rows["Started"], Is.EqualTo("2026-11-01 12:30:45.123"));
            Assert.That(rows["Redirects"], Is.EqualTo("1"));
            Assert.That(CurlNetworkDetailsView.GetGeneralRows(new CurlNetworkSnapshot { Url = "u", EffectiveUrl = "u" }).Any(x => x.Key == "Effective URL"), Is.False);
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

        /// <summary>Checks that the menu command opens the window and that it draws the table and every details tab and body mode without errors.</summary>
        [Test]
        public void OpenWindow_DrawsEveryTab()
        {
            var cookies = new CookieContainer();
            cookies.Add(new Uri("http://example.test/"), new Cookie("sid", "abc"));
            var record = CurlNetworkMonitorIsolation.Register("POST", "http://example.test/api/list?page=1&tag=a+b", "{\"query\":[1,2]}", cookies);
            record.OnSubmitted();
            record.OnRequestHeadersSent(Encoding.ASCII.GetBytes("POST /api/list?page=1&tag=a+b HTTP/1.1\r\nHost: example.test\r\nCookie: sid=abc\r\nContent-Type: application/json\r\n\r\n"));
            record.OnRequestBodyRead(Encoding.UTF8.GetBytes("{\"query\":[1,2]}"));
            CurlNetworkMonitorIsolation.ReceiveHeaders(record, "HTTP/1.1 200 OK", "Content-Type: application/json", "Set-Cookie: theme=dark; Path=/; HttpOnly; Secure; SameSite=Lax");
            record.OnResponseBodyReceived(Encoding.UTF8.GetBytes("{\"items\":[{\"id\":1},{\"id\":2}],\"long\":\"" + new string('z', 2000) + "\"}"));
            record.OnTransferInfo(new CurlTransferInfo
            {
                ResponseCode = 200,
                Protocol = "http/1.1",
                RemoteAddress = "127.0.0.1:80",
                Timings = new CurlNetworkTimings
                {
                    NameLookup = TimeSpan.FromMilliseconds(1),
                    Connect = TimeSpan.FromMilliseconds(2),
                    PreTransfer = TimeSpan.FromMilliseconds(3),
                    StartTransfer = TimeSpan.FromMilliseconds(10),
                    Total = TimeSpan.FromMilliseconds(12),
                },
            });
            record.OnCompleted();
            var image = CurlNetworkMonitorIsolation.Register("GET", "http://example.test/cover.png");
            var texture = new Texture2D(4, 4);
            var png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);
            CurlNetworkMonitorIsolation.ReceiveHeaders(image, "HTTP/1.1 200 OK", "Content-Type: image/png");
            image.OnResponseBodyReceived(png);
            image.OnCompleted();
            var pending = CurlNetworkMonitorIsolation.Register("GET", "http://example.test/pending");
            pending.OnSubmitted();

            CurlNetworkWindow.OpenWindow();
            _window = EditorWindow.GetWindow<CurlNetworkWindow>();
            Select(_window, record);
            var details = GetDetails(_window);
            foreach (CurlNetworkDetailsTab tab in Enum.GetValues(typeof(CurlNetworkDetailsTab)))
            {
                details.Tab = tab;
                PrivateMember.Invoke(_window, "RepaintImmediately");
            }
            details.Tab = CurlNetworkDetailsTab.Response;
            foreach (CurlNetworkBodyMode mode in Enum.GetValues(typeof(CurlNetworkBodyMode)))
            {
                PrivateMember.SetField(details, "_responseMode", mode);
                PrivateMember.Invoke(_window, "RepaintImmediately");
            }
            Select(_window, image);
            PrivateMember.SetField(details, "_responseMode", CurlNetworkBodyMode.Preview);
            PrivateMember.Invoke(_window, "RepaintImmediately");
            Select(_window, pending);
            foreach (CurlNetworkDetailsTab tab in Enum.GetValues(typeof(CurlNetworkDetailsTab)))
            {
                details.Tab = tab;
                PrivateMember.Invoke(_window, "RepaintImmediately");
            }

            Assert.That(_window.titleContent.text, Is.EqualTo("Curl Network"));
            Assert.That(RowNames(_window), Is.EqualTo(new[] { "list?page=1&tag=a+b", "cover.png", "pending" }));
            Assert.That(details.Record, Is.SameAs(pending));
            LogAssert.NoUnexpectedReceived();
            _window.Close();
            _window = null;
        }
    }
}
