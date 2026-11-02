#nullable enable
using AOT;
using MajdataPlay.Net.Curl.Core;
using MajdataPlay.Net.Curl.Core.PInvoke;
using MajdataPlay.UnsafeKit;
using System;
using System.Runtime.InteropServices;

namespace MajdataPlay.Net.Curl.Diagnostics
{
    /// <summary>
    /// Installs libcurl's debug callback on recorded transfers, so the request heads libcurl actually sends reach their
    /// <see cref="CurlNetworkRecord"/>.
    /// </summary>
    internal static class CurlNetworkCapture
    {
        /// <summary>
        /// Keeps the callback delegate alive for as long as libcurl may call it.
        /// </summary>
        private static readonly CurlDebugCallback s_onDebug = OnDebug;

        /// <summary>
        /// Turns on verbose output for a transfer and routes it to <see cref="OnDebug"/>.
        /// </summary>
        /// <param name="easy">The easy handle of the transfer.</param>
        /// <param name="taskHandle">The GC handle pointer of the owning <see cref="CurlTask"/>.</param>
        /// <exception cref="InvalidOperationException">libcurl rejected one of the options.</exception>
        public static void Attach(CurlEasy easy, IntPtr taskHandle)
        {
            easy.SetOption(CurlOption.DebugFunction, Marshal.GetFunctionPointerForDelegate(s_onDebug));
            easy.SetOption(CurlOption.DebugData, taskHandle);
            easy.SetOption(CurlOption.Verbose, 1);
        }

        /// <summary>
        /// libcurl debug callback. Forwards every request head to the transfer's record and ignores everything else.
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
            // Text messages also arrive while the easy handle is being cleaned up, so they are filtered out before the
            // GC handle is touched.
            if (type != CurlDebugInfoType.HeaderOut || data == IntPtr.Zero)
            {
                return 0;
            }
            try
            {
                if (UnsafeHelper.TryGetInstanceFromGCHandle<CurlTask>(userdata, out var task))
                {
                    task.Record?.OnRequestHeadersSent(new ReadOnlySpan<byte>((void*)data, checked((int)size.ToUInt64())));
                }
            }
            catch
            {
                // An exception must never unwind through libcurl's native frames; the head is simply not recorded.
            }

            return 0;
        }
    }
}
