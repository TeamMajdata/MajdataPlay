#nullable enable
using MajdataPlay.Net.Curl.Core.PInvoke;
using System;

namespace MajdataPlay.Net.Curl.Diagnostics
{
    /// <summary>
    /// The connection and timing details libcurl reports for a transfer through <c>curl_easy_getinfo</c>.
    /// </summary>
    internal readonly struct CurlTransferInfo
    {
        /// <summary>
        /// Gets the last HTTP status code received, or 0 when none was received.
        /// </summary>
        public int ResponseCode { get; init; }

        /// <summary>
        /// Gets the protocol of the last request in ALPN notation (<c>http/1.1</c>, <c>h2</c>, <c>h3</c>), or
        /// <see langword="null"/> before a connection was used.
        /// </summary>
        public string? Protocol { get; init; }

        /// <summary>
        /// Gets the URL of the last request, after redirects.
        /// </summary>
        public string? EffectiveUrl { get; init; }

        /// <summary>
        /// Gets the address and port of the last connection, such as <c>127.0.0.1:80</c> or <c>[::1]:443</c>, or
        /// <see langword="null"/> before a connection was made.
        /// </summary>
        public string? RemoteAddress { get; init; }

        /// <summary>
        /// Gets the number of header bytes received, including every interim and redirect response.
        /// </summary>
        public long HeaderSize { get; init; }

        /// <summary>
        /// Gets the number of redirects that were followed.
        /// </summary>
        public int RedirectCount { get; init; }

        /// <summary>
        /// Gets libcurl's identifier of the last connection used, or <see langword="null"/> before one was assigned.
        /// </summary>
        public long? ConnectionId { get; init; }

        /// <summary>
        /// Gets the number of connections the transfer had to open; 0 when it reused a pooled one.
        /// </summary>
        public int NewConnections { get; init; }

        /// <summary>
        /// Gets the timing points of the transfer.
        /// </summary>
        public CurlNetworkTimings Timings { get; init; }

        /// <summary>
        /// Reads the details of a transfer. Keys libcurl rejects are left at their defaults.
        /// </summary>
        /// <param name="easyHandle">
        /// The easy handle; it must stay allocated and must not be driven by another thread during the call.
        /// </param>
        /// <returns>The details of the transfer.</returns>
        public static CurlTransferInfo Read(IntPtr easyHandle)
        {
            return new CurlTransferInfo
            {
                ResponseCode = (int)ReadLong(easyHandle, CurlInfo.ResponseCode),
                Protocol = GetProtocolName(ReadLong(easyHandle, CurlInfo.HttpVersion)),
                EffectiveUrl = ReadString(easyHandle, CurlInfo.EffectiveUrl),
                RemoteAddress = FormatRemoteAddress(ReadString(easyHandle, CurlInfo.PrimaryIp), ReadLong(easyHandle, CurlInfo.PrimaryPort)),
                HeaderSize = ReadLong(easyHandle, CurlInfo.HeaderSize),
                RedirectCount = (int)ReadLong(easyHandle, CurlInfo.RedirectCount),
                ConnectionId = ReadConnectionId(easyHandle),
                NewConnections = (int)ReadLong(easyHandle, CurlInfo.NumConnects),
                Timings = new CurlNetworkTimings
                {
                    NameLookup = ReadMicroseconds(easyHandle, CurlInfo.NameLookupTimeT),
                    Connect = ReadMicroseconds(easyHandle, CurlInfo.ConnectTimeT),
                    AppConnect = ReadMicroseconds(easyHandle, CurlInfo.AppConnectTimeT),
                    PreTransfer = ReadMicroseconds(easyHandle, CurlInfo.PreTransferTimeT),
                    PostTransfer = ReadMicroseconds(easyHandle, CurlInfo.PostTransferTimeT),
                    StartTransfer = ReadMicroseconds(easyHandle, CurlInfo.StartTransferTimeT),
                    Total = ReadMicroseconds(easyHandle, CurlInfo.TotalTimeT),
                    Redirect = ReadMicroseconds(easyHandle, CurlInfo.RedirectTimeT),
                },
            };
        }

        /// <summary>
        /// Maps a <c>CURLINFO_HTTP_VERSION</c> value to its ALPN protocol name, as browsers show it.
        /// </summary>
        /// <param name="httpVersion">The <c>CURL_HTTP_VERSION_*</c> value.</param>
        /// <returns>The protocol name, or <see langword="null"/> for 0 and unknown values.</returns>
        internal static string? GetProtocolName(long httpVersion)
        {
            switch ((CurlHttpVersionMode)httpVersion)
            {
                case CurlHttpVersionMode.Http10:
                    return "http/1.0";
                case CurlHttpVersionMode.Http11:
                    return "http/1.1";
                case CurlHttpVersionMode.Http2:
                    return "h2";
                case CurlHttpVersionMode.Http3:
                    return "h3";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Formats an address and port, bracketing IPv6 addresses.
        /// </summary>
        /// <param name="ip">The numeric IP address, or <see langword="null"/>.</param>
        /// <param name="port">The port, or 0 when unknown.</param>
        /// <returns>The formatted endpoint, or <see langword="null"/> when <paramref name="ip"/> is empty.</returns>
        internal static string? FormatRemoteAddress(string? ip, long port)
        {
            if (string.IsNullOrEmpty(ip))
            {
                return null;
            }
            var host = ip!.IndexOf(':') >= 0 ? $"[{ip}]" : ip;

            return port > 0 ? $"{host}:{port}" : host;
        }

        /// <summary>
        /// Reads a <c>CURLINFO_LONG</c> value.
        /// </summary>
        /// <param name="easyHandle">The easy handle.</param>
        /// <param name="info">The info key.</param>
        /// <returns>The value, or 0 when libcurl rejected the key.</returns>
        private static long ReadLong(IntPtr easyHandle, CurlInfo info)
        {
            return LibCurl.Easy.GetInfo(easyHandle, info, out long value) == CurlCode.Ok ? value : 0;
        }

        /// <summary>
        /// Reads a <c>CURLINFO_STRING</c> value.
        /// </summary>
        /// <param name="easyHandle">The easy handle.</param>
        /// <param name="info">The info key.</param>
        /// <returns>The value, or <see langword="null"/> when it is unset or libcurl rejected the key.</returns>
        private static string? ReadString(IntPtr easyHandle, CurlInfo info)
        {
            return LibCurl.Easy.GetInfo(easyHandle, info, out string? value) == CurlCode.Ok ? value : null;
        }

        /// <summary>
        /// Reads <c>CURLINFO_CONN_ID</c>.
        /// </summary>
        /// <param name="easyHandle">The easy handle.</param>
        /// <returns>The connection identifier, or <see langword="null"/> when there is none or libcurl rejected the key.</returns>
        private static long? ReadConnectionId(IntPtr easyHandle)
        {
            if (LibCurl.Easy.GetInfoOffT(easyHandle, CurlInfo.ConnId, out var connectionId) != CurlCode.Ok || connectionId < 0)
            {
                return null;
            }

            return connectionId;
        }

        /// <summary>
        /// Reads a <c>CURLINFO_OFF_T</c> time in microseconds.
        /// </summary>
        /// <param name="easyHandle">The easy handle.</param>
        /// <param name="info">The info key.</param>
        /// <returns>The time, or <see cref="TimeSpan.Zero"/> when libcurl rejected the key.</returns>
        private static TimeSpan ReadMicroseconds(IntPtr easyHandle, CurlInfo info)
        {
            if (LibCurl.Easy.GetInfoOffT(easyHandle, info, out var microseconds) != CurlCode.Ok || microseconds <= 0)
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromTicks(microseconds * (TimeSpan.TicksPerMillisecond / 1000));
        }
    }
}
