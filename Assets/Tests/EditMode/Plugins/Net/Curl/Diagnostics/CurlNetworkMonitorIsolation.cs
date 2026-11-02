#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using MajdataPlay.Net.Curl;
using MajdataPlay.Net.Curl.Diagnostics;

namespace MajdataPlay.Tests.Plugins.Net.Curl.Diagnostics
{
    /// <summary>
    /// Saves the static state of <see cref="CurlNetworkMonitor"/> (history, switch and limits), empties the history for
    /// the test and restores everything when disposed, so a Curl Debugger open during the run keeps its history.
    /// </summary>
    internal sealed class CurlNetworkMonitorIsolation : IDisposable
    {
        /// <summary>The saved history, oldest first.</summary>
        private readonly CurlNetworkRecord[] _records;

        /// <summary>The saved recording switch.</summary>
        private readonly bool _isEnabled;

        /// <summary>The saved history size.</summary>
        private readonly int _maxRecordCount;

        /// <summary>The saved per-body capture limit.</summary>
        private readonly int _maxBodyCaptureBytes;

        /// <summary>The saved shared capture budget.</summary>
        private readonly long _maxTotalBodyCaptureBytes;

        /// <summary>Saves the current state, empties the history without freeing its bodies and turns recording on.</summary>
        public CurlNetworkMonitorIsolation()
        {
            _isEnabled = CurlNetworkMonitor.IsEnabled;
            _maxRecordCount = CurlNetworkMonitor.MaxRecordCount;
            _maxBodyCaptureBytes = CurlNetworkMonitor.MaxBodyCaptureBytes;
            _maxTotalBodyCaptureBytes = CurlNetworkMonitor.MaxTotalBodyCaptureBytes;
            lock (Gate)
            {
                _records = History.ToArray();
                History.Clear();
            }
            CurlNetworkMonitor.MaxRecordCount = CurlNetworkMonitor.DefaultMaxRecordCount;
            CurlNetworkMonitor.MaxBodyCaptureBytes = CurlNetworkMonitor.DefaultMaxBodyCaptureBytes;
            CurlNetworkMonitor.MaxTotalBodyCaptureBytes = CurlNetworkMonitor.DefaultMaxTotalBodyCaptureBytes;
            CurlNetworkMonitor.IsEnabled = true;
        }

        /// <summary>Gets the lock that guards the history.</summary>
        public static object Gate => PrivateMember.GetStaticField<object>(typeof(CurlNetworkMonitor), "s_sync");

        /// <summary>Gets the history queue.</summary>
        public static Queue<CurlNetworkRecord> History => PrivateMember.GetStaticField<Queue<CurlNetworkRecord>>(typeof(CurlNetworkMonitor), "s_records");

        /// <summary>Creates and registers a record of a request without sending it.</summary>
        /// <param name="method">The HTTP method.</param>
        /// <param name="url">The request URL.</param>
        /// <param name="body">An optional UTF-8 JSON request body.</param>
        /// <param name="cookies">An optional cookie container whose cookies are recorded as the <c>Cookie</c> header.</param>
        /// <returns>The registered record in the <see cref="CurlNetworkRequestState.Queued"/> state.</returns>
        public static CurlNetworkRecord Register(string method, string url, string? body = null, CookieContainer? cookies = null)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            if (body is not null)
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }
            var record = CurlNetworkMonitor.TryCreateRecord(request, new CurlHttpConfig { CookieContainer = cookies })!;
            CurlNetworkMonitor.Register(record);
            return record;
        }

        /// <summary>Delivers response header lines to a record, each terminated with CRLF.</summary>
        /// <param name="record">The record.</param>
        /// <param name="lines">The status and header lines.</param>
        public static void ReceiveHeaders(CurlNetworkRecord record, params string[] lines)
        {
            foreach (var line in lines)
            {
                record.OnResponseHeaderLine(Encoding.ASCII.GetBytes(line + "\r\n"));
            }
            record.OnResponseHeaderLine(Encoding.ASCII.GetBytes("\r\n"));
        }

        /// <summary>Creates, registers and completes a GET exchange with the given response.</summary>
        /// <param name="url">The request URL.</param>
        /// <param name="status">The status code.</param>
        /// <param name="body">The UTF-8 response body.</param>
        /// <param name="headers">Additional response header lines.</param>
        /// <returns>The completed record.</returns>
        public static CurlNetworkRecord Complete(string url, int status, string body, params string[] headers)
        {
            var record = Register("GET", url);
            record.OnSubmitted();
            var lines = new List<string> { $"HTTP/1.1 {status} Status" };
            lines.AddRange(headers);
            ReceiveHeaders(record, lines.ToArray());
            record.OnResponseBodyReceived(Encoding.UTF8.GetBytes(body));
            record.OnCompleted();
            return record;
        }

        /// <summary>Empties the test history and restores the saved state.</summary>
        public void Dispose()
        {
            CurlNetworkMonitor.Clear();
            CurlNetworkMonitor.MaxRecordCount = _maxRecordCount;
            CurlNetworkMonitor.MaxBodyCaptureBytes = _maxBodyCaptureBytes;
            CurlNetworkMonitor.MaxTotalBodyCaptureBytes = _maxTotalBodyCaptureBytes;
            lock (Gate)
            {
                foreach (var record in _records)
                {
                    History.Enqueue(record);
                }
            }
            CurlNetworkMonitor.NotifyChanged();
            CurlNetworkMonitor.IsEnabled = _isEnabled;
        }
    }
}
