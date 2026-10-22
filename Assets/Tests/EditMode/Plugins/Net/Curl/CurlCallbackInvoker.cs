#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MajdataPlay.Net.Curl.Core;
using MajdataPlay.Net.Curl.Core.PInvoke;

namespace MajdataPlay.Tests.Plugins.Net.Curl
{
    /// <summary>
    /// Calls the private static libcurl callbacks the way libcurl would, with pinned native buffers and GC handle user
    /// data, so header parsing, body hand-off and upload reads can be tested without a transfer.
    /// </summary>
    internal static class CurlCallbackInvoker
    {
        /// <summary>Calls the response header callback with one header line.</summary>
        /// <param name="userdata">The GC handle pointer of the owning transfer, or zero.</param>
        /// <param name="line">The raw header line including its terminator.</param>
        /// <returns>The value the callback returned to libcurl.</returns>
        public static UIntPtr Header(IntPtr userdata, string line)
        {
            return InvokeWithBuffer(typeof(CurlResponse), "OnHeaderReceived", Encoding.UTF8.GetBytes(line), userdata);
        }

        /// <summary>Calls the response body callback with one chunk.</summary>
        /// <param name="userdata">The GC handle pointer of the owning transfer, or zero.</param>
        /// <param name="chunk">The body bytes.</param>
        /// <returns>The value the callback returned to libcurl.</returns>
        public static UIntPtr Write(IntPtr userdata, byte[] chunk)
        {
            return InvokeWithBuffer(typeof(CurlResponse), "OnWriteCallback", chunk, userdata);
        }

        /// <summary>Calls the upload read callback with a native buffer of the given size.</summary>
        /// <param name="userdata">The GC handle pointer of the request, or zero.</param>
        /// <param name="buffer">The buffer libcurl offers; it receives the uploaded bytes.</param>
        /// <returns>The value the callback returned to libcurl.</returns>
        public static UIntPtr Read(IntPtr userdata, byte[] buffer)
        {
            return InvokeWithBuffer(typeof(CurlRequest), "OnReadCallback", buffer, userdata);
        }

        /// <summary>Calls the upload seek callback.</summary>
        /// <param name="userdata">The GC handle pointer of the request, or zero.</param>
        /// <param name="offset">The requested offset.</param>
        /// <param name="origin">The seek origin.</param>
        /// <returns>The value the callback returned to libcurl.</returns>
        public static CurlSeekCallbackReturn Seek(IntPtr userdata, long offset, SeekOrigin origin)
        {
            return PrivateMember.InvokeStatic<CurlSeekCallbackReturn>(typeof(CurlRequest), "OnSeekCallback", userdata, offset, origin);
        }

        /// <summary>Pins a buffer and passes it to a read/write style callback as one block of <c>size * nmemb</c> bytes.</summary>
        /// <param name="type">The type declaring the callback.</param>
        /// <param name="name">The callback method name.</param>
        /// <param name="buffer">The buffer to pin.</param>
        /// <param name="userdata">The user data pointer.</param>
        /// <returns>The callback result.</returns>
        private static UIntPtr InvokeWithBuffer(Type type, string name, byte[] buffer, IntPtr userdata)
        {
            var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                return PrivateMember.InvokeStatic<UIntPtr>(type, name, pin.AddrOfPinnedObject(), (UIntPtr)1u,
                    (UIntPtr)(uint)buffer.Length, userdata);
            }
            finally
            {
                pin.Free();
            }
        }
    }
}
