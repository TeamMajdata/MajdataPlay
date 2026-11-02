using MajdataPlay.Net.Curl.Core.PInvoke;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Authentication;
using System.Text;
using System.Threading.Tasks;
using MajdataPlay.Net.Curl.Core;
using MajdataPlay.Net.Curl.Utils;

#nullable enable
namespace MajdataPlay.Net.Curl
{
    public readonly struct CurlHttpConfig
    {
        public SslProtocols SslProtocols { get; init; }
        public IWebProxy? Proxy { get; init; }
        public bool PreAuthenticate { get; init; }
        /// <summary>
        /// Gets the maximum length, in bytes, of one block of response headers: the status line and headers of an
        /// interim or redirect response, of the final response, or the trailers. Unlike
        /// <c>HttpClientHandler.MaxResponseHeadersLength</c>, which is in kilobytes, this value is in bytes.
        /// </summary>
        public int MaxResponseHeadersLength { get; init; }
        /// <summary>
        /// Gets the number of response body bytes that may be buffered ahead of the reader. The transfer pauses when
        /// the buffer would exceed 80% of this size and resumes once the reader has drained it to 40%.
        /// </summary>
        public int ResponseBodyBufferSize { get; init; }
        public long MaxRequestContentBufferSize { get; init; }
        /// <summary>
        /// Gets the limit on simultaneous connections to one host, applied as <c>CURLMOPT_MAX_HOST_CONNECTIONS</c>. Zero
        /// means no limit.
        /// </summary>
        public int MaxConnectionsPerServer { get; init; }
        /// <summary>
        /// Gets the number of connections libcurl may keep open in its connection pool, applied as
        /// <c>CURLMOPT_MAXCONNECTS</c>. Zero lets libcurl size the pool itself.
        /// </summary>
        public int MaxCachedConnections { get; init; }
        /// <summary>
        /// Gets how long a pooled connection may have been idle and still be reused, applied as
        /// <c>CURLOPT_MAXAGE_CONN</c>. Zero keeps libcurl's default of 118 seconds; a negative value means no limit.
        /// </summary>
        public TimeSpan PooledConnectionIdleTimeout { get; init; }
        /// <summary>
        /// Gets how long after it was opened a pooled connection may still be reused, applied as
        /// <c>CURLOPT_MAXLIFETIME_CONN</c>. Zero or a negative value means no limit.
        /// </summary>
        public TimeSpan PooledConnectionLifetime { get; init; }
        public int MaxAutomaticRedirections { get; init; }
        public ICredentials? DefaultProxyCredentials { get; init; }
        public ICredentials? Credentials { get; init; }
        public CookieContainer? CookieContainer { get; init; }
        public bool CheckCertificateRevocationList { get; init; }
        public DecompressionMethods AutomaticDecompression { get; init; }
        public bool AllowAutoRedirect { get; init; }
        public bool UseDefaultCredentials { get; init; }
        public bool UseProxy { get; init; }
        /// <summary>
        /// Gets the limit for the whole transfer, from name resolution to the last body byte. Zero or a negative value
        /// means no limit.
        /// </summary>
        public TimeSpan Timeout { get; init; }
        /// <summary>
        /// Gets the limit for resolving the host and completing the connection and TLS handshakes. Zero or a negative
        /// value uses libcurl's built-in 300-second limit.
        /// </summary>
        public TimeSpan ConnectTimeout { get; init; }
        /// <summary>
        /// Gets how long the transfer may stay below <see cref="LowSpeedLimit"/> before it is aborted. Zero or a
        /// negative value disables the low-speed check.
        /// </summary>
        public TimeSpan LowSpeedTimeout { get; init; }
        /// <summary>
        /// Gets the average transfer speed, in bytes per second, below which <see cref="LowSpeedTimeout"/> starts
        /// counting.
        /// </summary>
        public int LowSpeedLimit { get; init; }

        /// <summary>
        /// Applies the connection pool limits to the multi handle shared by every request of the handler.
        /// </summary>
        /// <param name="multi">The multi handle the request is about to be added to.</param>
        /// <returns><see cref="CurlMCode.Ok"/>, or the code of the first option libcurl rejected.</returns>
        internal readonly CurlMCode ApplyToMulti(CurlMulti multi)
        {
            var multiHandle = multi.Handle;

            var returnCode = LibCurl.Multi.SetOption(multiHandle, CurlMOption.MaxHostConnections, (long)Math.Max(MaxConnectionsPerServer, 0));
            if (returnCode != CurlMCode.Ok)
            {
                return returnCode;
            }
            return LibCurl.Multi.SetOption(multiHandle, CurlMOption.MaxConnections, (long)Math.Max(MaxCachedConnections, 0));
        }

        internal readonly void ApplyToEasy(CurlEasy curlEasy, Uri requestUri)
        {
            var easyHandle = curlEasy.Handle;
            var returnCode = default(CurlCode);

            returnCode = LibCurl.Easy.SetOption(easyHandle, CurlOption.TimeoutMs, ToCurlMilliseconds(Timeout));
            CurlUtility.EnsureSuccess(easyHandle, returnCode);
            returnCode = LibCurl.Easy.SetOption(easyHandle, CurlOption.ConnectTimeoutMs, ToCurlMilliseconds(ConnectTimeout));
            CurlUtility.EnsureSuccess(easyHandle, returnCode);
            var lowSpeedTime = ToCurlSeconds(LowSpeedTimeout);
            returnCode = LibCurl.Easy.SetOption(easyHandle, CurlOption.LowSpeedTime, lowSpeedTime);
            CurlUtility.EnsureSuccess(easyHandle, returnCode);
            returnCode = LibCurl.Easy.SetOption(easyHandle, CurlOption.LowSpeedLimit, lowSpeedTime > 0 ? Math.Max(LowSpeedLimit, 0) : 0);
            CurlUtility.EnsureSuccess(easyHandle, returnCode);

            if (PooledConnectionIdleTimeout != TimeSpan.Zero)
            {
                returnCode = LibCurl.Easy.SetOption(easyHandle, CurlOption.MaxAgeConn, ToCurlMaxAgeSeconds(PooledConnectionIdleTimeout));
                CurlUtility.EnsureSuccess(easyHandle, returnCode);
            }
            returnCode = LibCurl.Easy.SetOption(easyHandle, CurlOption.MaxLifetimeConn, ToCurlSeconds(PooledConnectionLifetime));
            CurlUtility.EnsureSuccess(easyHandle, returnCode);

            returnCode = LibCurl.Easy.SetOption(easyHandle, CurlOption.FollowLocation, AllowAutoRedirect ? 1 : 0);
            CurlUtility.EnsureSuccess(easyHandle, returnCode);
            if (AllowAutoRedirect)
            {
                returnCode = LibCurl.Easy.SetOption(easyHandle, CurlOption.MaxRedirs, MaxAutomaticRedirections);
                CurlUtility.EnsureSuccess(easyHandle, returnCode);
            }

            if (AutomaticDecompression != DecompressionMethods.None)
            {
                var encodings = GetEncodingString(AutomaticDecompression);
                returnCode = LibCurl.Easy.SetOption(easyHandle, CurlOption.AcceptEncoding, encodings);
                CurlUtility.EnsureSuccess(easyHandle, returnCode);
            }

            if (UseProxy && Proxy != null && !Proxy.IsBypassed(requestUri))
            {
                var proxyUri = Proxy.GetProxy(requestUri);
                returnCode = LibCurl.Easy.SetOption(easyHandle, CurlOption.Proxy, proxyUri.ToString());
                CurlUtility.EnsureSuccess(easyHandle, returnCode);

                var proxyCred = (DefaultProxyCredentials ?? Proxy.Credentials)?.GetCredential(proxyUri, "Basic");
                if (proxyCred != null)
                {
                    var userPwd = $"{proxyCred.UserName}:{proxyCred.Password}";
                    returnCode = LibCurl.Easy.SetOption(easyHandle, CurlOption.ProxyUserPwd, userPwd);
                    CurlUtility.EnsureSuccess(easyHandle, returnCode);
                }
            }
            else if (!UseProxy)
            {
                returnCode = LibCurl.Easy.SetOption(easyHandle, CurlOption.Proxy, "");
                CurlUtility.EnsureSuccess(easyHandle, returnCode);
            }

            if (UseDefaultCredentials)
            {
                returnCode = LibCurl.Easy.SetOption(easyHandle, CurlOption.UserPassword, ":");
                CurlUtility.EnsureSuccess(easyHandle, returnCode);
                returnCode = LibCurl.Easy.SetOption(easyHandle, CurlOption.HttpAuth, (long)(CurlAuth.NTLM | CurlAuth.GSSNEGOTIATE));
                CurlUtility.EnsureSuccess(easyHandle, returnCode);
            }
            else if (Credentials != null)
            {
                var cred = Credentials.GetCredential(requestUri, "Basic");
                if (cred != null)
                {
                    returnCode = LibCurl.Easy.SetOption(easyHandle, CurlOption.UserPassword, $"{cred.UserName}:{cred.Password}");
                    CurlUtility.EnsureSuccess(easyHandle, returnCode);

                    var authType = PreAuthenticate ? (long)CurlAuth.Basic : (long)CurlAuth.Any;
                    returnCode = LibCurl.Easy.SetOption(easyHandle, CurlOption.HttpAuth, authType);
                    CurlUtility.EnsureSuccess(easyHandle, returnCode);
                }
            }

            // SSL/TLS version
            var sslVersion = MapSslProtocols(SslProtocols);
            returnCode = LibCurl.Easy.SetOption(easyHandle, CurlOption.SslVersion, sslVersion);
            CurlUtility.EnsureSuccess(easyHandle, returnCode);

            returnCode = LibCurl.Easy.SetOption(easyHandle, CurlOption.SslOptions, (long)GetSslOptions());
            CurlUtility.EnsureSuccess(easyHandle, returnCode);

            if(CookieContainer is not null)
            {
                var cookieHeader = CookieContainer.GetCookieHeader(requestUri);

                if (!string.IsNullOrEmpty(cookieHeader))
                {
                    returnCode = LibCurl.Easy.SetOption(easyHandle, CurlOption.Cookie, cookieHeader);
                    CurlUtility.EnsureSuccess(easyHandle, returnCode);
                }
            }

        }
        /// <summary>
        /// Combines every <c>CURLOPT_SSL_OPTIONS</c> bit for a request. libcurl stores the option as one bitmask, so
        /// setting it more than once keeps only the last value.
        /// </summary>
        /// <returns>The platform's CA store bits, plus <see cref="CurlSslOption.NoRevoke"/> unless revocation checks are on.</returns>
        internal readonly CurlSslOption GetSslOptions()
        {
            var options = CurlUtility.GetPlatformSslOptions();
            if (!CheckCertificateRevocationList)
            {
                // Only the Schannel backend checks revocation; OpenSSL ignores this bit.
                options |= CurlSslOption.NoRevoke;
            }
            return options;
        }
        /// <summary>
        /// Converts a timeout to the millisecond value of <c>CURLOPT_TIMEOUT_MS</c> or <c>CURLOPT_CONNECTTIMEOUT_MS</c>.
        /// </summary>
        /// <param name="timeout">The timeout to convert; zero or a negative value means no timeout.</param>
        /// <returns>
        /// The timeout rounded up to whole milliseconds and capped at <see cref="int.MaxValue"/>, because the options are
        /// a C <c>long</c> that is 32 bits wide on Windows and ARMv7; 0 when there is no timeout.
        /// </returns>
        internal static long ToCurlMilliseconds(TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero)
            {
                return 0;
            }
            var milliseconds = DivideRoundingUp(timeout.Ticks, TimeSpan.TicksPerMillisecond);
            return Math.Min(milliseconds, int.MaxValue);
        }
        /// <summary>
        /// Converts a timeout to the second value of <c>CURLOPT_LOW_SPEED_TIME</c>, which has no millisecond variant.
        /// </summary>
        /// <param name="timeout">The timeout to convert; zero or a negative value disables the check.</param>
        /// <returns>
        /// The timeout rounded up to whole seconds and capped at <see cref="int.MaxValue"/>; 0 when the check is disabled.
        /// </returns>
        internal static long ToCurlSeconds(TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero)
            {
                return 0;
            }
            var seconds = DivideRoundingUp(timeout.Ticks, TimeSpan.TicksPerSecond);
            return Math.Min(seconds, int.MaxValue);
        }
        /// <summary>
        /// Converts an idle timeout to the second value of <c>CURLOPT_MAXAGE_CONN</c>, where 0 would make almost every
        /// pooled connection too old to reuse rather than lift the limit.
        /// </summary>
        /// <param name="idleTimeout">The idle timeout to convert; a negative value means no limit.</param>
        /// <returns>
        /// The timeout rounded up to whole seconds and capped at <see cref="int.MaxValue"/>; <see cref="int.MaxValue"/>
        /// when there is no limit.
        /// </returns>
        internal static long ToCurlMaxAgeSeconds(TimeSpan idleTimeout)
        {
            if (idleTimeout < TimeSpan.Zero)
            {
                return int.MaxValue;
            }
            return ToCurlSeconds(idleTimeout);
        }
        /// <summary>
        /// Divides a non-negative tick count, rounding up without overflowing near <see cref="long.MaxValue"/>.
        /// </summary>
        /// <param name="ticks">The non-negative tick count.</param>
        /// <param name="ticksPerUnit">The number of ticks in one target unit.</param>
        /// <returns>The number of whole target units covering <paramref name="ticks"/>.</returns>
        private static long DivideRoundingUp(long ticks, long ticksPerUnit)
        {
            var units = ticks / ticksPerUnit;
            if ((ticks % ticksPerUnit) != 0)
            {
                units++;
            }
            return units;
        }
        readonly string GetEncodingString(DecompressionMethods methods)
        {
            var sb = new StringBuilder();
            if (methods.HasFlag(DecompressionMethods.GZip))
            {
                sb.Append("gzip");
            }
            if (methods.HasFlag(DecompressionMethods.Deflate))
            {
                if (sb.Length > 0)
                {
                    sb.Append(',');
                }
                sb.Append("deflate");
            }
            //if (methods.HasFlag(DecompressionMethods.Brotli))
            //{
            //    list.Add("br");
            //}
            return sb.ToString();
        }
        readonly long MapSslProtocols(SslProtocols protocols)
        {
            // CURL_SSLVERSION_TLSv1_2 = 6, CURL_SSLVERSION_TLSv1_3 = 7
            if (protocols.HasFlag(SslProtocols.Tls13))
            {
                return 7;
            }
            if (protocols.HasFlag(SslProtocols.Tls12))
            {
                return 6;
            }
            return 0; // CURL_SSLVERSION_DEFAULT
        }
    }
}
