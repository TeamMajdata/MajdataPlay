#nullable enable
using AOT;
using MajdataPlay.Net.Curl.Core;
using MajdataPlay.Net.Curl.Core.PInvoke;
using MajdataPlay.UnsafeKit;
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MajdataPlay.Net.Curl.Diagnostics
{
    /// <summary>
    /// Installs libcurl's debug and pre-request callbacks on transfers that are recorded or track their connection, so
    /// the request heads libcurl sends reach their <see cref="CurlNetworkRecord"/> and the connections it opens, reuses
    /// and closes reach the handler's <see cref="CurlHandlerStatistics"/>.
    /// </summary>
    internal static class CurlNetworkCapture
    {
        /// <summary>
        /// The text that precedes a connection identifier in libcurl's connection messages.
        /// </summary>
        private static readonly byte[] s_connectionMarker = Encoding.ASCII.GetBytes("connection #");

        /// <summary>
        /// The end of the prefix of a gracefully closed connection: <c>shutting down connection #N</c>.
        /// </summary>
        private static readonly byte[] s_shuttingDown = Encoding.ASCII.GetBytes("shutting down");

        /// <summary>
        /// The end of the prefix of an immediately closed connection: <c>closing connection #N</c>.
        /// </summary>
        private static readonly byte[] s_closing = Encoding.ASCII.GetBytes("closing");

        /// <summary>
        /// The prefix of a connection dropped to keep the pool within its limits: <c>Discarding connection #N ...</c>.
        /// </summary>
        private static readonly byte[] s_discarding = Encoding.ASCII.GetBytes("Discarding");

        /// <summary>
        /// Keeps the debug callback delegate alive for as long as libcurl may call it.
        /// </summary>
        private static readonly CurlDebugCallback s_onDebug = OnDebug;

        /// <summary>
        /// Keeps the pre-request callback delegate alive for as long as libcurl may call it.
        /// </summary>
        private static readonly CurlPrereqCallback s_onPrereq = OnPrereq;

        /// <summary>
        /// Turns on verbose output for a transfer and routes it to <see cref="OnDebug"/>, and, when the transfer
        /// tracks its connection, routes the pre-request callback to <see cref="OnPrereq"/>.
        /// </summary>
        /// <param name="easy">The easy handle of the transfer.</param>
        /// <param name="taskHandle">The GC handle pointer of the owning <see cref="CurlTask"/>.</param>
        /// <param name="trackConnection">Whether the transfer reports the connections it uses.</param>
        /// <exception cref="InvalidOperationException">libcurl rejected one of the options.</exception>
        public static void Attach(CurlEasy easy, IntPtr taskHandle, bool trackConnection)
        {
            easy.SetOption(CurlOption.DebugFunction, Marshal.GetFunctionPointerForDelegate(s_onDebug));
            easy.SetOption(CurlOption.DebugData, taskHandle);
            easy.SetOption(CurlOption.Verbose, 1);
            if (trackConnection)
            {
                easy.SetOption(CurlOption.PrereqFunction, Marshal.GetFunctionPointerForDelegate(s_onPrereq));
                easy.SetOption(CurlOption.PrereqData, taskHandle);
            }
        }

        /// <summary>
        /// Recognizes libcurl's message for a connection it closes: <c>shutting down connection #N</c>,
        /// <c>closing connection #N</c>, or <c>Discarding connection #N ...</c> when the pool is over its limit.
        /// </summary>
        /// <param name="text">One informational message.</param>
        /// <param name="connectionId">Receives the identifier of the closed connection.</param>
        /// <returns><see langword="true"/> when the message reports a closed connection.</returns>
        internal static bool TryParseClosedConnection(ReadOnlySpan<byte> text, out long connectionId)
        {
            connectionId = -1;
            var marker = text.IndexOf(s_connectionMarker);
            if (marker < 0)
            {
                return false;
            }
            var prefix = text.Slice(0, marker);
            while (prefix.Length > 0 && prefix[prefix.Length - 1] == (byte)' ')
            {
                prefix = prefix.Slice(0, prefix.Length - 1);
            }
            var isClosing = prefix.EndsWith(s_shuttingDown)
                || prefix.EndsWith(s_closing)
                || prefix.SequenceEqual(s_discarding);
            if (!isClosing)
            {
                return false;
            }
            var digits = text.Slice(marker + s_connectionMarker.Length);
            var value = 0L;
            var length = 0;
            while (length < digits.Length && digits[length] >= '0' && digits[length] <= '9' && length < 18)
            {
                value = (value * 10) + (digits[length] - '0');
                length++;
            }
            if (length == 0)
            {
                return false;
            }
            connectionId = value;

            return true;
        }

        /// <summary>
        /// libcurl debug callback. Forwards request heads to the transfer's record and closed connections to its
        /// handler's statistics, and ignores everything else.
        /// </summary>
        /// <param name="handle">The easy handle.</param>
        /// <param name="type">The kind of information.</param>
        /// <param name="data">The information; not NUL-terminated.</param>
        /// <param name="size">The length of <paramref name="data"/> in bytes.</param>
        /// <param name="userdata">The GC handle pointer of the owning <see cref="CurlTask"/>.</param>
        /// <returns>Always 0, as libcurl requires.</returns>
        [MonoPInvokeCallback(typeof(CurlDebugCallback))]
        private static unsafe int OnDebug(IntPtr handle, CurlDebugInfoType type, IntPtr data, UIntPtr size, IntPtr userdata)
        {
            // Body and TLS data arrive for every chunk, so they are filtered out before the GC handle is touched.
            if ((type != CurlDebugInfoType.HeaderOut && type != CurlDebugInfoType.Text) || data == IntPtr.Zero)
            {
                return 0;
            }
            try
            {
                if (!UnsafeHelper.TryGetInstanceFromGCHandle<CurlTask>(userdata, out var task))
                {
                    return 0;
                }
                var span = new ReadOnlySpan<byte>((void*)data, checked((int)size.ToUInt64()));
                if (type == CurlDebugInfoType.HeaderOut)
                {
                    task.Record?.OnRequestHeadersSent(span);
                }
                else if (task.IsTrackingConnection && TryParseClosedConnection(span, out var connectionId))
                {
                    task.Statistics?.OnConnectionClosed(connectionId);
                }
            }
            catch
            {
                // An exception must never unwind through libcurl's native frames; the message is simply not recorded.
            }

            return 0;
        }

        /// <summary>
        /// libcurl pre-request callback. Tells the transfer which connection it got, opened or reused.
        /// </summary>
        /// <param name="userdata">The GC handle pointer of the owning <see cref="CurlTask"/>.</param>
        /// <param name="primaryIp">The NUL-terminated remote IP address.</param>
        /// <param name="localIp">The NUL-terminated local IP address.</param>
        /// <param name="primaryPort">The remote port.</param>
        /// <param name="localPort">The local port.</param>
        /// <returns>Always 0 (<c>CURL_PREREQFUNC_OK</c>); any other value would abort the request.</returns>
        [MonoPInvokeCallback(typeof(CurlPrereqCallback))]
        private static int OnPrereq(IntPtr userdata, IntPtr primaryIp, IntPtr localIp, int primaryPort, int localPort)
        {
            try
            {
                if (UnsafeHelper.TryGetInstanceFromGCHandle<CurlTask>(userdata, out var task))
                {
                    var remoteAddress = CurlTransferInfo.FormatRemoteAddress(Marshal.PtrToStringAnsi(primaryIp), primaryPort);
                    var localAddress = CurlTransferInfo.FormatRemoteAddress(Marshal.PtrToStringAnsi(localIp), localPort);
                    task.OnConnectionReady(remoteAddress ?? string.Empty, localAddress ?? string.Empty);
                }
            }
            catch
            {
                // An exception must never unwind through libcurl's native frames, and the request must still be sent.
            }

            return 0;
        }
    }
}
