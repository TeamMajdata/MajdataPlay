#nullable enable
using MajdataPlay.Net.Curl.Core.PInvoke;
using MajdataPlay.Net.Curl.Lifecycle;
using System;
using System.Collections.Generic;
using System.Threading;

namespace MajdataPlay.Net.Curl.Diagnostics
{
    /// <summary>
    /// The statistics of every <see cref="CurlHttpMessageHandler"/> and of libcurl itself, for debugging tools such as
    /// the Editor's Curl Debugger.
    /// </summary>
    /// <remarks>
    /// Every handler registers its <see cref="CurlHandlerStatistics"/> when it is created. Disposed handlers stay listed
    /// until more than <see cref="MaxDisposedHandlers"/> have accumulated or <see cref="ClearDisposed"/> is called.
    /// Every member is thread-safe.
    /// </remarks>
    public static class CurlStatistics
    {
        /// <summary>
        /// The number of disposed handlers kept in the list; older ones are dropped first.
        /// </summary>
        public const int MaxDisposedHandlers = 16;

#if UNITY_EDITOR
        /// <summary>
        /// Connection tracking is on by default in the Editor, where the Curl Debugger shows it.
        /// </summary>
        private const bool DefaultConnectionTracking = true;
#else
        /// <summary>
        /// Connection tracking is off by default in players, which have no tool that shows it.
        /// </summary>
        private const bool DefaultConnectionTracking = false;
#endif

        /// <summary>
        /// Guards <see cref="s_handlers"/>.
        /// </summary>
        private static readonly object s_sync = new();

        /// <summary>
        /// Every live handler and the most recently disposed ones, oldest first.
        /// </summary>
        private static readonly List<CurlHandlerStatistics> s_handlers = new();

        /// <summary>
        /// Backing field of <see cref="IsConnectionTrackingEnabled"/>.
        /// </summary>
        private static volatile bool s_isConnectionTrackingEnabled = DefaultConnectionTracking;

        /// <summary>
        /// The identifier of the last handler registered.
        /// </summary>
        private static long s_lastId;

        /// <summary>
        /// Gets or sets whether requests sent from now on report their connections, which keeps the connection pools
        /// of <see cref="CurlHandlerStatistics"/> up to date.
        /// </summary>
        /// <remarks>
        /// Tracking turns on libcurl's verbose output and a pre-request callback for each request, a few extra native
        /// callbacks per request. It is on by default in the Editor and off in players. Connections opened while it was
        /// off are only discovered once a tracked request reuses them, and their closing is missed when an untracked
        /// request triggers it.
        /// </remarks>
        public static bool IsConnectionTrackingEnabled
        {
            get => s_isConnectionTrackingEnabled;
            set => s_isConnectionTrackingEnabled = value;
        }

        /// <summary>
        /// Gets the number of live easy and multi handles that hold libcurl's global state.
        /// </summary>
        public static int LibCurlReferenceCount
        {
            get => LibCurlLifecycle.ReferenceCount;
        }

        /// <summary>
        /// Gets the statistics of every live handler and of the most recently disposed ones.
        /// </summary>
        /// <returns>The statistics, oldest handler first.</returns>
        public static CurlHandlerStatistics[] GetHandlers()
        {
            lock (s_sync)
            {
                return s_handlers.ToArray();
            }
        }

        /// <summary>
        /// Removes every disposed handler from the list.
        /// </summary>
        public static void ClearDisposed()
        {
            lock (s_sync)
            {
                s_handlers.RemoveAll(static handler => handler.IsDisposed);
            }
        }

        /// <summary>
        /// Reads what the loaded libcurl reports about itself, initializing libcurl's global state if needed.
        /// </summary>
        /// <returns>The version, build target, TLS and zlib versions, protocols and features.</returns>
        /// <exception cref="CurlException"><c>curl_global_init</c> failed.</exception>
        public static CurlLibraryInfo GetLibraryInfo()
        {
            LibCurlLifecycle.Retain();
            try
            {
                var info = LibCurl.GetVersionInfo(0);

                return new CurlLibraryInfo
                {
                    Version = info.Version ?? string.Empty,
                    VersionNumber = info.VersionNum,
                    Host = info.Host ?? string.Empty,
                    SslVersion = info.SslVersion,
                    LibzVersion = info.LibzVersion,
                    Protocols = info.Protocols ?? Array.Empty<string>(),
                    Features = (CurlFeatures)info.Features,
                };
            }
            finally
            {
                LibCurlLifecycle.Release();
            }
        }

        /// <summary>
        /// Creates and lists the statistics of a new handler.
        /// </summary>
        /// <returns>The statistics.</returns>
        internal static CurlHandlerStatistics Register()
        {
            var statistics = new CurlHandlerStatistics(Interlocked.Increment(ref s_lastId));
            lock (s_sync)
            {
                s_handlers.Add(statistics);
            }

            return statistics;
        }

        /// <summary>
        /// Marks a handler as disposed and drops the oldest disposed handlers beyond <see cref="MaxDisposedHandlers"/>.
        /// </summary>
        /// <param name="statistics">The statistics of the disposed handler.</param>
        internal static void OnHandlerDisposed(CurlHandlerStatistics statistics)
        {
            statistics.OnDisposed();
            lock (s_sync)
            {
                var disposed = 0;
                for (var i = s_handlers.Count - 1; i >= 0; i--)
                {
                    if (!s_handlers[i].IsDisposed)
                    {
                        continue;
                    }
                    disposed++;
                    if (disposed > MaxDisposedHandlers)
                    {
                        s_handlers.RemoveAt(i);
                    }
                }
            }
        }
    }
}
