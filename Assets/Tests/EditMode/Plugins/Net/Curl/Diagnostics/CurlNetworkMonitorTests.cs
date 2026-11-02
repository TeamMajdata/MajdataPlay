#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using MajdataPlay.Net.Curl;
using MajdataPlay.Net.Curl.Diagnostics;
using NUnit.Framework;

namespace MajdataPlay.Tests.Plugins.Net.Curl.Diagnostics
{
    /// <summary>
    /// Covers the <see cref="CurlNetworkMonitor"/> history: the recording switch, registration order, the history size,
    /// clearing, the shared body budget and argument validation.
    /// </summary>
    public class CurlNetworkMonitorTests
    {
        /// <summary>Restores the monitor after each test.</summary>
        private CurlNetworkMonitorIsolation _isolation = null!;

        /// <summary>Saves the monitor state.</summary>
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

        /// <summary>Checks that no record is created while recording is off.</summary>
        [Test]
        public void TryCreateRecord_ReturnsNullWhileDisabled()
        {
            CurlNetworkMonitor.IsEnabled = false;
            using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.test/");

            Assert.That(CurlNetworkMonitor.TryCreateRecord(request, new CurlHttpConfig()), Is.Null);
        }

        /// <summary>Checks that records get increasing ids, pick up the container's cookies and join the history only when registered.</summary>
        [Test]
        public void TryCreateRecord_JoinsHistoryOnRegister()
        {
            var cookies = new CookieContainer();
            cookies.Add(new Uri("http://example.test/"), new Cookie("sid", "abc"));
            using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.test/list");

            var first = CurlNetworkMonitor.TryCreateRecord(request, new CurlHttpConfig { CookieContainer = cookies })!;
            var second = CurlNetworkMonitor.TryCreateRecord(request, new CurlHttpConfig())!;

            Assert.That(second.Id, Is.GreaterThan(first.Id));
            Assert.That(first.GetSnapshot().RequestCookies, Is.EqualTo("sid=abc"));
            Assert.That(second.GetSnapshot().RequestCookies, Is.Null);
            Assert.That(CurlNetworkMonitor.Count, Is.Zero);

            CurlNetworkMonitor.Register(first);

            Assert.That(CurlNetworkMonitor.GetRecords(), Is.EqualTo(new[] { first }));
        }

        /// <summary>Checks that the history keeps the newest records and frees the bodies of dropped ones.</summary>
        [Test]
        public void Register_DropsOldestBeyondLimit()
        {
            var baseline = CurlNetworkMonitor.CapturedBodyBytes;
            var oldest = CurlNetworkMonitorIsolation.Complete("http://example.test/1", 200, "first");
            CurlNetworkMonitor.MaxRecordCount = 2;
            var middle = CurlNetworkMonitorIsolation.Register("GET", "http://example.test/2");
            var newest = CurlNetworkMonitorIsolation.Register("GET", "http://example.test/3");

            Assert.That(CurlNetworkMonitor.GetRecords(), Is.EqualTo(new[] { middle, newest }));
            Assert.That(oldest.GetResponseBody(), Is.Empty);
            Assert.That(CurlNetworkMonitor.CapturedBodyBytes, Is.EqualTo(baseline));

            CurlNetworkMonitor.MaxRecordCount = 1;

            Assert.That(CurlNetworkMonitor.GetRecords(), Is.EqualTo(new[] { newest }));
        }

        /// <summary>Checks that clearing empties the history, frees every body and changes the version.</summary>
        [Test]
        public void Clear_EmptiesHistory()
        {
            var baseline = CurlNetworkMonitor.CapturedBodyBytes;
            var record = CurlNetworkMonitorIsolation.Complete("http://example.test/", 200, "body");
            Assert.That(CurlNetworkMonitor.CapturedBodyBytes, Is.EqualTo(baseline + 4));
            var version = CurlNetworkMonitor.Version;

            CurlNetworkMonitor.Clear();

            Assert.That(CurlNetworkMonitor.Count, Is.Zero);
            Assert.That(CurlNetworkMonitor.GetRecords(), Is.Empty);
            Assert.That(record.GetResponseBody(), Is.Empty);
            Assert.That(CurlNetworkMonitor.CapturedBodyBytes, Is.EqualTo(baseline));
            Assert.That(CurlNetworkMonitor.Version, Is.Not.EqualTo(version));
        }

        /// <summary>Checks that the body budget refuses reservations beyond it and that released bytes become available again.</summary>
        [Test]
        public void BodyBudget_RefusesBeyondLimit()
        {
            var baseline = CurlNetworkMonitor.CapturedBodyBytes;
            CurlNetworkMonitor.MaxTotalBodyCaptureBytes = baseline + 10;

            Assert.That(CurlNetworkMonitor.TryReserveBodyBytes(6), Is.True);
            Assert.That(CurlNetworkMonitor.TryReserveBodyBytes(5), Is.False);
            Assert.That(CurlNetworkMonitor.CapturedBodyBytes, Is.EqualTo(baseline + 6));
            CurlNetworkMonitor.ReleaseBodyBytes(6);
            Assert.That(CurlNetworkMonitor.TryReserveBodyBytes(10), Is.True);
            CurlNetworkMonitor.ReleaseBodyBytes(10);

            Assert.That(CurlNetworkMonitor.CapturedBodyBytes, Is.EqualTo(baseline));
        }

        /// <summary>Checks that out-of-range limits are rejected and leave the limits unchanged.</summary>
        [Test]
        public void Limits_RejectInvalidValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CurlNetworkMonitor.MaxRecordCount = 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => CurlNetworkMonitor.MaxBodyCaptureBytes = -1);
            Assert.Throws<ArgumentOutOfRangeException>(() => CurlNetworkMonitor.MaxTotalBodyCaptureBytes = -1);

            Assert.That(CurlNetworkMonitor.MaxRecordCount, Is.EqualTo(CurlNetworkMonitor.DefaultMaxRecordCount));
            Assert.That(CurlNetworkMonitor.MaxBodyCaptureBytes, Is.EqualTo(CurlNetworkMonitor.DefaultMaxBodyCaptureBytes));
            Assert.That(CurlNetworkMonitor.MaxTotalBodyCaptureBytes, Is.EqualTo(CurlNetworkMonitor.DefaultMaxTotalBodyCaptureBytes));
        }

        /// <summary>Checks that the per-body limit applies to records created afterwards.</summary>
        [Test]
        public void MaxBodyCaptureBytes_AppliesToNewRecords()
        {
            CurlNetworkMonitor.MaxBodyCaptureBytes = 3;
            var record = CurlNetworkMonitorIsolation.Complete("http://example.test/", 200, "abcdef");

            Assert.That(record.GetResponseBody(), Is.EqualTo(new byte[] { (byte)'a', (byte)'b', (byte)'c' }));
            Assert.That(record.GetSnapshot().IsResponseBodyTruncated, Is.True);
            Assert.That(CurlNetworkMonitor.GetRecords().Single(), Is.SameAs(record));
        }
    }
}
