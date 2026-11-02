using MajdataPlay.Net.Curl;
using MajdataPlay.Net.Curl.Core;
using MajdataPlay.Net.Curl.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay.Net.Curl
{
    public class CurlHttpMessageHandler : HttpMessageHandler
    {
        public SslProtocols SslProtocols { get; set; } = SslProtocols.Tls12 | SslProtocols.Tls13;
        public IWebProxy? Proxy { get; set; }
        public bool PreAuthenticate { get; set; }
        /// <summary>
        /// Gets or sets the maximum length, in bytes, of one block of response headers (an interim or redirect
        /// response, the final response, or the trailers). Defaults to 64 KiB.
        /// </summary>
        /// <remarks>
        /// <c>HttpClientHandler.MaxResponseHeadersLength</c> is in kilobytes; this property is in bytes.
        /// </remarks>
        public int MaxResponseHeadersLength { get; set; } = 64 * 1024;
        /// <summary>
        /// Gets or sets the number of response body bytes that may be buffered ahead of the reader before the transfer
        /// is paused. Defaults to 64 KiB.
        /// </summary>
        public int ResponseBodyBufferSize { get; set; } = 64 * 1024;
        public long MaxRequestContentBufferSize { get; set; } = int.MaxValue;
        /// <summary>
        /// Gets or sets the limit on simultaneous connections to one host, applied as
        /// <c>CURLMOPT_MAX_HOST_CONNECTIONS</c>. Requests beyond it wait for a free connection. Defaults to 8.
        /// </summary>
        /// <remarks>
        /// The limit is shared by every request of this handler; the value of the latest submitted request applies.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
        public int MaxConnectionsPerServer
        {
            get => _maxConnectionsPerServer;
            set
            {
                if (value < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(MaxConnectionsPerServer), value, "At least one connection per server must be allowed.");
                }
                _maxConnectionsPerServer = value;
            }
        }
        /// <summary>
        /// Gets or sets how many connections libcurl may keep open in its connection pool, applied as
        /// <c>CURLMOPT_MAXCONNECTS</c>. When a finished transfer would exceed it, libcurl closes the oldest idle
        /// connection. Defaults to 0, which lets libcurl size the pool from the number of running transfers.
        /// </summary>
        /// <remarks>
        /// The pool is shared by every request of this handler; the value of the latest submitted request applies.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
        public int MaxCachedConnections
        {
            get => _maxCachedConnections;
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(MaxCachedConnections), value, "The connection pool size cannot be negative.");
                }
                _maxCachedConnections = value;
            }
        }
        /// <summary>
        /// Gets or sets how long a pooled connection may have been idle and still be reused, applied as
        /// <c>CURLOPT_MAXAGE_CONN</c>. An older idle connection is closed instead of being reused. Defaults to 118
        /// seconds, libcurl's own default; <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> means no limit.
        /// </summary>
        /// <remarks>
        /// libcurl measures this in whole seconds, so the value is rounded up to whole seconds and capped at
        /// <see cref="int.MaxValue"/> seconds. It is checked when a request looks for a connection to reuse.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The value is neither positive nor <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>.
        /// </exception>
        public TimeSpan PooledConnectionIdleTimeout
        {
            get => _pooledConnectionIdleTimeout;
            set
            {
                EnsureValidTimeout(value, nameof(PooledConnectionIdleTimeout));
                _pooledConnectionIdleTimeout = value;
            }
        }
        /// <summary>
        /// Gets or sets how long after it was opened a pooled connection may still be reused, applied as
        /// <c>CURLOPT_MAXLIFETIME_CONN</c>. An older connection is closed instead of being reused, so that, for example,
        /// DNS changes are picked up. Defaults to <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>, which means
        /// no limit.
        /// </summary>
        /// <remarks>
        /// libcurl measures this in whole seconds, so the value is rounded up to whole seconds and capped at
        /// <see cref="int.MaxValue"/> seconds. A transfer already using the connection is not interrupted.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The value is neither positive nor <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>.
        /// </exception>
        public TimeSpan PooledConnectionLifetime
        {
            get => _pooledConnectionLifetime;
            set
            {
                EnsureValidTimeout(value, nameof(PooledConnectionLifetime));
                _pooledConnectionLifetime = value;
            }
        }
        public int MaxAutomaticRedirections { get; set; } = 50;
        public ICredentials? DefaultProxyCredentials { get; set; }
        public ICredentials? Credentials { get; set; }
        public CookieContainer? CookieContainer { get; set; }
        public bool CheckCertificateRevocationList { get; set; }
        public DecompressionMethods AutomaticDecompression { get; set; } = DecompressionMethods.None;
        public bool AllowAutoRedirect { get; set; } = true;
        public bool UseDefaultCredentials { get; set; }
        public bool UseProxy { get; set; } = false;
        /// <summary>
        /// Gets or sets the limit for the whole transfer, from name resolution until the last response body byte has
        /// been received. Defaults to <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>, which means no limit.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="HttpClient.Timeout"/>, this limit keeps running while the response body is read, including
        /// while the transfer is paused because the reader has not drained <see cref="ResponseBodyBufferSize"/>. It is
        /// rounded up to whole milliseconds and capped at <see cref="int.MaxValue"/> milliseconds. An expired limit fails
        /// the request, or the pending body read, with the timeout exception described on
        /// <see cref="SendAsync(HttpRequestMessage, CancellationToken)"/>.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The value is neither positive nor <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>.
        /// </exception>
        public TimeSpan Timeout
        {
            get => _timeout;
            set
            {
                EnsureValidTimeout(value, nameof(Timeout));
                _timeout = value;
            }
        }
        /// <summary>
        /// Gets or sets the limit for resolving the host and completing the TCP, proxy and TLS handshakes. Defaults to
        /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>, which leaves libcurl's built-in 300-second limit in
        /// place.
        /// </summary>
        /// <remarks>
        /// The value is rounded up to whole milliseconds and capped at <see cref="int.MaxValue"/> milliseconds. An expired
        /// limit fails the request with the timeout exception described on
        /// <see cref="SendAsync(HttpRequestMessage, CancellationToken)"/>.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The value is neither positive nor <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>.
        /// </exception>
        public TimeSpan ConnectTimeout
        {
            get => _connectTimeout;
            set
            {
                EnsureValidTimeout(value, nameof(ConnectTimeout));
                _connectTimeout = value;
            }
        }
        /// <summary>
        /// Gets or sets how long the average transfer speed may stay below <see cref="LowSpeedLimit"/> before the
        /// transfer is aborted. Defaults to <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>, which disables the
        /// low-speed check.
        /// </summary>
        /// <remarks>
        /// libcurl measures this in whole seconds, so the value is rounded up to whole seconds. The speed is averaged over
        /// roughly the last five seconds, so a transfer that stalls right after a burst is aborted up to about five
        /// seconds later than this value. Time spent paused because
        /// the reader has not drained <see cref="ResponseBodyBufferSize"/> is not counted. An expired limit fails the
        /// request, or the pending body read, with the timeout exception described on
        /// <see cref="SendAsync(HttpRequestMessage, CancellationToken)"/>.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The value is neither positive nor <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>.
        /// </exception>
        public TimeSpan LowSpeedTimeout
        {
            get => _lowSpeedTimeout;
            set
            {
                EnsureValidTimeout(value, nameof(LowSpeedTimeout));
                _lowSpeedTimeout = value;
            }
        }
        /// <summary>
        /// Gets or sets the average transfer speed, in bytes per second, below which <see cref="LowSpeedTimeout"/>
        /// starts counting. Defaults to 1, so that only a stalled transfer is aborted. It has no effect while
        /// <see cref="LowSpeedTimeout"/> is <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
        public int LowSpeedLimit
        {
            get => _lowSpeedLimit;
            set
            {
                if (value < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(LowSpeedLimit), value, "The low-speed limit must be at least 1 byte per second.");
                }
                _lowSpeedLimit = value;
            }
        }

        /// <summary>
        /// Gets the request counters and connection pool of this handler, also listed in <see cref="CurlStatistics"/>.
        /// </summary>
        public CurlHandlerStatistics Statistics
        {
            get => _curlMulti.Statistics;
        }

        readonly CurlMulti _curlMulti;
        private TimeSpan _timeout = System.Threading.Timeout.InfiniteTimeSpan;
        private TimeSpan _connectTimeout = System.Threading.Timeout.InfiniteTimeSpan;
        private TimeSpan _lowSpeedTimeout = System.Threading.Timeout.InfiniteTimeSpan;
        private int _lowSpeedLimit = 1;
        private int _maxConnectionsPerServer = 8;
        private int _maxCachedConnections;
        private TimeSpan _pooledConnectionIdleTimeout = TimeSpan.FromSeconds(118);
        private TimeSpan _pooledConnectionLifetime = System.Threading.Timeout.InfiniteTimeSpan;

        public CurlHttpMessageHandler()
        {
            _curlMulti = new();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _curlMulti.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Sends a request through libcurl and publishes the response once its headers have arrived.
        /// </summary>
        /// <param name="request">The request to send.</param>
        /// <param name="cancellationToken">The token cancelling the request and the reads of its response body.</param>
        /// <returns>The response, whose body streams from the running transfer.</returns>
        /// <exception cref="TaskCanceledException">
        /// <see cref="Timeout"/>, <see cref="ConnectTimeout"/> or <see cref="LowSpeedTimeout"/> expired. As with
        /// <see cref="HttpClient.Timeout"/>, the exception's token is not cancelled and its inner exception is a
        /// <see cref="TimeoutException"/>, which wraps the <see cref="CurlException"/> with
        /// <see cref="Core.PInvoke.CurlCode.OperationTimedOut"/>. Body reads that hit one of these limits throw the same exception.
        /// </exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        /// <exception cref="HttpRequestException">libcurl failed the request for another reason; the inner exception is
        /// the <see cref="CurlException"/>.</exception>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var config = CopyCurrentConfig();
            var content = request.Content;
            var contentStream = default(Stream?);
            if(content is not null)
            {
                contentStream = await content.ReadAsStreamAsync().ConfigureAwait(false);
            }
            var curlTask = _curlMulti.AddToQueue(request, contentStream, config, cancellationToken);

            try
            {
                var rsp = await curlTask.ConfigureAwait(false);

                return rsp;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new HttpRequestException(e.Message, e);
            }           
        }

        CurlHttpConfig CopyCurrentConfig()
        {
            var config = new CurlHttpConfig()
            {
                SslProtocols = SslProtocols,
                Proxy = Proxy,
                PreAuthenticate = PreAuthenticate,
                MaxResponseHeadersLength = MaxResponseHeadersLength,
                ResponseBodyBufferSize = ResponseBodyBufferSize,
                MaxRequestContentBufferSize = MaxRequestContentBufferSize,
                MaxConnectionsPerServer = MaxConnectionsPerServer,
                MaxAutomaticRedirections = MaxAutomaticRedirections,
                DefaultProxyCredentials = DefaultProxyCredentials,
                Credentials = Credentials,
                CookieContainer = CookieContainer,
                CheckCertificateRevocationList = CheckCertificateRevocationList,
                AutomaticDecompression = AutomaticDecompression,
                AllowAutoRedirect = AllowAutoRedirect,
                UseDefaultCredentials = UseDefaultCredentials,
                UseProxy = UseProxy,
                Timeout = Timeout,
                ConnectTimeout = ConnectTimeout,
                LowSpeedTimeout = LowSpeedTimeout,
                LowSpeedLimit = LowSpeedLimit,
                MaxCachedConnections = MaxCachedConnections,
                PooledConnectionIdleTimeout = PooledConnectionIdleTimeout,
                PooledConnectionLifetime = PooledConnectionLifetime
            };

            return config;
        }

        /// <summary>
        /// Checks a timeout setting the same way <see cref="HttpClient.Timeout"/> does.
        /// </summary>
        /// <param name="value">The timeout to check.</param>
        /// <param name="paramName">The name of the property being set.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="value"/> is neither positive nor <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>.
        /// </exception>
        private static void EnsureValidTimeout(TimeSpan value, string paramName)
        {
            if (value <= TimeSpan.Zero && value != System.Threading.Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(paramName, value, "The timeout must be positive or Timeout.InfiniteTimeSpan.");
            }
        }
    }
}
