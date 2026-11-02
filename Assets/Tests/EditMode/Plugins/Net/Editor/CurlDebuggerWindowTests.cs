#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Text;
using MajdataPlay.Net.Curl.Diagnostics;
using MajdataPlay.Net.Editor;
using MajdataPlay.Tests.Plugins.Net.Curl.Diagnostics;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MajdataPlay.Tests.Plugins.Net.Editor
{
    /// <summary>
    /// Covers the <see cref="CurlDebuggerWindow"/> shell: the recording lifecycle, switching between the Network and
    /// Info tabs, refreshing the visible tab and drawing every tab without errors. The monitor and the handler list
    /// are isolated for each test and restored afterwards.
    /// </summary>
    public class CurlDebuggerWindowTests
    {
        /// <summary>Restores the request history after each test.</summary>
        private CurlNetworkMonitorIsolation _monitor = null!;

        /// <summary>Restores the handler list after each test.</summary>
        private CurlStatisticsIsolation _statistics = null!;

        /// <summary>The window under test.</summary>
        private CurlDebuggerWindow? _window;

        /// <summary>Saves the monitor and statistics state.</summary>
        [SetUp]
        public void SetUp()
        {
            _monitor = new CurlNetworkMonitorIsolation();
            _statistics = new CurlStatisticsIsolation();
        }

        /// <summary>Closes the window and restores the saved state.</summary>
        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                Object.DestroyImmediate(_window);
            }
            _statistics.Dispose();
            _monitor.Dispose();
        }

        /// <summary>Creates the window, which enables its panels.</summary>
        /// <returns>The window.</returns>
        private CurlDebuggerWindow CreateWindow()
        {
            _window = ScriptableObject.CreateInstance<CurlDebuggerWindow>();
            return _window;
        }

        /// <summary>Gets the window's Network panel.</summary>
        /// <param name="window">The window.</param>
        /// <returns>The panel.</returns>
        private static CurlNetworkPanel GetNetwork(CurlDebuggerWindow window)
        {
            return PrivateMember.GetField<CurlNetworkPanel>(window, "_network");
        }

        /// <summary>Gets the window's Info panel.</summary>
        /// <param name="window">The window.</param>
        /// <returns>The panel.</returns>
        private static CurlInfoPanel GetInfo(CurlDebuggerWindow window)
        {
            return PrivateMember.GetField<CurlInfoPanel>(window, "_info");
        }

        /// <summary>Gets the names of the requests the Network panel lists.</summary>
        /// <param name="window">The window.</param>
        /// <returns>The row names.</returns>
        private static string[] RowNames(CurlDebuggerWindow window)
        {
            return PrivateMember.GetField<CurlNetworkTable>(GetNetwork(window), "_table").VisibleSnapshots.Select(x => x.Name).ToArray();
        }

        /// <summary>Checks that the window opens on the Network tab with recording on and the handler list loaded.</summary>
        [Test]
        public void OnEnable_StartsRecordingOnNetworkTab()
        {
            CurlNetworkMonitorIsolation.Register("GET", "http://example.test/early");
            var handler = CurlStatistics.Register();
            CurlNetworkMonitor.IsEnabled = false;

            var window = CreateWindow();

            Assert.That(window.titleContent.text, Is.EqualTo("Curl Debugger"));
            Assert.That(window.minSize, Is.EqualTo(new Vector2(640, 320)));
            Assert.That(window.Tab, Is.EqualTo(CurlDebuggerTab.Network));
            Assert.That(CurlNetworkMonitor.IsEnabled, Is.True);
            Assert.That(RowNames(window), Is.EqualTo(new[] { "early" }));
            Assert.That(GetInfo(window).Handlers.Single().Id, Is.EqualTo(handler.Id));
        }

        /// <summary>Checks that closing the window turns recording off.</summary>
        [Test]
        public void OnDestroy_StopsRecording()
        {
            var window = CreateWindow();

            Object.DestroyImmediate(window);
            _window = null;

            Assert.That(CurlNetworkMonitor.IsEnabled, Is.False);
        }

        /// <summary>Checks that selecting a tab loads its latest data at once.</summary>
        [Test]
        public void SelectTab_RefreshesTab()
        {
            var window = CreateWindow();
            var handler = CurlStatistics.Register();
            CurlNetworkMonitorIsolation.Register("GET", "http://example.test/late");

            window.SelectTab(CurlDebuggerTab.Info);
            Assert.That(window.Tab, Is.EqualTo(CurlDebuggerTab.Info));
            Assert.That(GetInfo(window).Handlers.Select(x => x.Id), Does.Contain(handler.Id));
            Assert.That(RowNames(window), Is.Empty, "The hidden Network tab is not refreshed.");

            window.SelectTab(CurlDebuggerTab.Network);

            Assert.That(RowNames(window), Is.EqualTo(new[] { "late" }));
        }

        /// <summary>Checks that Update refreshes only the visible tab, and only when due.</summary>
        [Test]
        public void Update_RefreshesVisibleTab()
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

            PrivateMember.SetField(window, "_tab", CurlDebuggerTab.Info);
            var handler = CurlStatistics.Register();
            PrivateMember.SetField(window, "_nextRefresh", 0d);
            PrivateMember.Invoke(window, "Update");

            Assert.That(GetInfo(window).Handlers.Select(x => x.Id), Does.Contain(handler.Id));
        }

        /// <summary>Checks that the menu command opens the window and that it draws both tabs, every details tab and every body mode without errors.</summary>
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
                ConnectionId = 0,
                NewConnections = 1,
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
            var handler = CurlStatistics.Register();
            handler.OnRequestQueued();
            handler.OnConnectionUsed(-1, 0, true, "127.0.0.1:80", "127.0.0.1:5000");
            handler.OnConnectionUsed(-1, 5, false, "127.0.0.1:80", "127.0.0.1:5001");
            handler.OnRequestDetached(5);
            CurlStatistics.OnHandlerDisposed(CurlStatistics.Register());

            CurlDebuggerWindow.OpenWindow();
            _window = EditorWindow.GetWindow<CurlDebuggerWindow>();
            var network = GetNetwork(_window);
            var details = PrivateMember.GetField<CurlNetworkDetailsView>(network, "_details");
            _window.SelectTab(CurlDebuggerTab.Network);
            CurlNetworkPanelTests.Select(network, record);
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
            CurlNetworkPanelTests.Select(network, image);
            PrivateMember.SetField(details, "_responseMode", CurlNetworkBodyMode.Preview);
            PrivateMember.Invoke(_window, "RepaintImmediately");
            CurlNetworkPanelTests.Select(network, pending);
            foreach (CurlNetworkDetailsTab tab in Enum.GetValues(typeof(CurlNetworkDetailsTab)))
            {
                details.Tab = tab;
                PrivateMember.Invoke(_window, "RepaintImmediately");
            }
            _window.SelectTab(CurlDebuggerTab.Info);
            PrivateMember.Invoke(_window, "RepaintImmediately");

            Assert.That(_window.titleContent.text, Is.EqualTo("Curl Debugger"));
            Assert.That(RowNames(_window), Is.EqualTo(new[] { "list?page=1&tag=a+b", "cover.png", "pending" }));
            Assert.That(details.Record, Is.SameAs(pending));
            Assert.That(GetInfo(_window).Handlers.Count, Is.EqualTo(2));
            Assert.That(GetInfo(_window).Handlers[0].Connections.Count, Is.EqualTo(2));
            LogAssert.NoUnexpectedReceived();
            _window.Close();
            _window = null;
        }
    }
}
