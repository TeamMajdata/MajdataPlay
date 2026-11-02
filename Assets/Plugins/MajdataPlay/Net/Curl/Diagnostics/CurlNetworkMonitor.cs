#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;

namespace MajdataPlay.Net.Curl.Diagnostics
{
    /// <summary>
    /// Records the requests sent through <see cref="CurlHttpMessageHandler"/> while <see cref="IsEnabled"/> is set, for
    /// debugging tools such as the Editor's Curl Network window.
    /// </summary>
    /// <remarks>
    /// Recording is off by default and costs one flag check per request while it stays off. A request is recorded only
    /// when recording was on as it was sent, and its history entry keeps updating until the request finishes. The
    /// history keeps the newest <see cref="MaxRecordCount"/> requests, and every body keeps at most
    /// <see cref="MaxBodyCaptureBytes"/> bytes within a shared budget of <see cref="MaxTotalBodyCaptureBytes"/> bytes.
    /// Every member is thread-safe.
    /// </remarks>
    public static class CurlNetworkMonitor
    {
        /// <summary>
        /// The default value of <see cref="MaxRecordCount"/>.
        /// </summary>
        public const int DefaultMaxRecordCount = 1000;

        /// <summary>
        /// The default value of <see cref="MaxBodyCaptureBytes"/>: 1 MiB.
        /// </summary>
        public const int DefaultMaxBodyCaptureBytes = 1024 * 1024;

        /// <summary>
        /// The default value of <see cref="MaxTotalBodyCaptureBytes"/>: 128 MiB.
        /// </summary>
        public const long DefaultMaxTotalBodyCaptureBytes = 128L * 1024 * 1024;

        /// <summary>
        /// Guards <see cref="s_records"/>. Records never take this lock, so it may be held while calling into them.
        /// </summary>
        private static readonly object s_sync = new();

        /// <summary>
        /// The history, oldest first.
        /// </summary>
        private static readonly Queue<CurlNetworkRecord> s_records = new();

        /// <summary>
        /// Backing field of <see cref="IsEnabled"/>.
        /// </summary>
        private static volatile bool s_isEnabled;

        /// <summary>
        /// Backing field of <see cref="MaxRecordCount"/>.
        /// </summary>
        private static int s_maxRecordCount = DefaultMaxRecordCount;

        /// <summary>
        /// Backing field of <see cref="MaxBodyCaptureBytes"/>.
        /// </summary>
        private static int s_maxBodyCaptureBytes = DefaultMaxBodyCaptureBytes;

        /// <summary>
        /// Backing field of <see cref="MaxTotalBodyCaptureBytes"/>.
        /// </summary>
        private static long s_maxTotalBodyCaptureBytes = DefaultMaxTotalBodyCaptureBytes;

        /// <summary>
        /// Backing field of <see cref="CapturedBodyBytes"/>.
        /// </summary>
        private static long s_capturedBodyBytes;

        /// <summary>
        /// The identifier of the last record created.
        /// </summary>
        private static long s_lastId;

        /// <summary>
        /// Backing field of <see cref="Version"/>.
        /// </summary>
        private static int s_version;

        /// <summary>
        /// Gets or sets whether new requests are recorded. Requests already recorded keep updating either way.
        /// </summary>
        public static bool IsEnabled
        {
            get => s_isEnabled;
            set => s_isEnabled = value;
        }

        /// <summary>
        /// Gets or sets the number of requests the history keeps; the oldest are dropped first, even while running.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
        public static int MaxRecordCount
        {
            get => Volatile.Read(ref s_maxRecordCount);
            set
            {
                if (value < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "At least one record must be kept.");
                }
                lock (s_sync)
                {
                    Volatile.Write(ref s_maxRecordCount, value);
                    TrimLocked();
                }
            }
        }

        /// <summary>
        /// Gets or sets the number of bytes kept of each request and response body of requests recorded from now on.
        /// 0 keeps no body but still counts body sizes.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
        public static int MaxBodyCaptureBytes
        {
            get => Volatile.Read(ref s_maxBodyCaptureBytes);
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "The limit cannot be negative.");
                }
                Volatile.Write(ref s_maxBodyCaptureBytes, value);
            }
        }

        /// <summary>
        /// Gets or sets the number of body bytes all recorded requests may keep together. Once it is used up, bodies are
        /// only counted until records leave the history.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
        public static long MaxTotalBodyCaptureBytes
        {
            get => Interlocked.Read(ref s_maxTotalBodyCaptureBytes);
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "The budget cannot be negative.");
                }
                Interlocked.Exchange(ref s_maxTotalBodyCaptureBytes, value);
            }
        }

        /// <summary>
        /// Gets the number of body bytes the recorded requests keep.
        /// </summary>
        public static long CapturedBodyBytes
        {
            get => Interlocked.Read(ref s_capturedBodyBytes);
        }

        /// <summary>
        /// Gets a number that changes whenever the history or one of its records changes.
        /// </summary>
        public static int Version
        {
            get => Volatile.Read(ref s_version);
        }

        /// <summary>
        /// Gets the number of requests in the history.
        /// </summary>
        public static int Count
        {
            get
            {
                lock (s_sync)
                {
                    return s_records.Count;
                }
            }
        }

        /// <summary>
        /// Gets the history.
        /// </summary>
        /// <returns>The recorded requests, oldest first.</returns>
        public static CurlNetworkRecord[] GetRecords()
        {
            lock (s_sync)
            {
                return s_records.ToArray();
            }
        }

        /// <summary>
        /// Empties the history and frees the captured bodies. Running requests are dropped from the history too.
        /// </summary>
        public static void Clear()
        {
            lock (s_sync)
            {
                while (s_records.Count > 0)
                {
                    s_records.Dequeue().Release();
                }
                NotifyChanged();
            }
        }

        /// <summary>
        /// Creates a record for a request that is about to be queued, if recording is on. The record joins the history
        /// only once <see cref="Register"/> is called, so a request that is never queued leaves no entry behind.
        /// </summary>
        /// <param name="request">The request message.</param>
        /// <param name="config">The handler settings, whose cookie container provides the <c>Cookie</c> header.</param>
        /// <returns>The new record, or <see langword="null"/> when recording is off.</returns>
        internal static CurlNetworkRecord? TryCreateRecord(HttpRequestMessage request, in CurlHttpConfig config)
        {
            if (!s_isEnabled)
            {
                return null;
            }
            var uri = request.RequestUri;
            var containerCookies = default(string?);
            if (config.CookieContainer is not null && uri is not null && uri.IsAbsoluteUri)
            {
                containerCookies = config.CookieContainer.GetCookieHeader(uri);
            }

            return new CurlNetworkRecord(Interlocked.Increment(ref s_lastId), request, containerCookies, MaxBodyCaptureBytes);
        }

        /// <summary>
        /// Adds a record to the history, dropping the oldest records beyond <see cref="MaxRecordCount"/>.
        /// </summary>
        /// <param name="record">The record of a queued request.</param>
        internal static void Register(CurlNetworkRecord record)
        {
            lock (s_sync)
            {
                s_records.Enqueue(record);
                TrimLocked();
                NotifyChanged();
            }
        }

        /// <summary>
        /// Takes body bytes from the shared budget.
        /// </summary>
        /// <param name="count">The number of bytes to keep.</param>
        /// <returns><see langword="true"/> when the budget allowed it; the bytes must then be returned with <see cref="ReleaseBodyBytes"/>.</returns>
        internal static bool TryReserveBodyBytes(int count)
        {
            var total = Interlocked.Add(ref s_capturedBodyBytes, count);
            if (total > MaxTotalBodyCaptureBytes)
            {
                Interlocked.Add(ref s_capturedBodyBytes, -count);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns body bytes to the shared budget.
        /// </summary>
        /// <param name="count">The number of bytes no longer kept.</param>
        internal static void ReleaseBodyBytes(long count)
        {
            Interlocked.Add(ref s_capturedBodyBytes, -count);
        }

        /// <summary>
        /// Changes <see cref="Version"/>.
        /// </summary>
        internal static void NotifyChanged()
        {
            Interlocked.Increment(ref s_version);
        }

        /// <summary>
        /// Drops the oldest records beyond <see cref="MaxRecordCount"/>. Must be called while holding <see cref="s_sync"/>.
        /// </summary>
        private static void TrimLocked()
        {
            var removed = false;
            while (s_records.Count > s_maxRecordCount)
            {
                s_records.Dequeue().Release();
                removed = true;
            }
            if (removed)
            {
                NotifyChanged();
            }
        }
    }
}
