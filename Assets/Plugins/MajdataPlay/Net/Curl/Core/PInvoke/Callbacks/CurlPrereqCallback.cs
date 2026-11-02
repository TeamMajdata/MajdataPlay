#nullable enable
using System;
using System.Runtime.InteropServices;

namespace MajdataPlay.Net.Curl.Core.PInvoke
{
    /// <summary>
    /// libcurl <c>curl_prereq_callback</c>, installed with <see cref="CurlOption.PrereqFunction"/>. libcurl calls it
    /// once the connection of a request is ready, whether new or reused, just before the request is sent.
    /// </summary>
    /// <param name="userdata">The pointer set with <see cref="CurlOption.PrereqData"/>.</param>
    /// <param name="primaryIp">The NUL-terminated remote IP address.</param>
    /// <param name="localIp">The NUL-terminated local IP address.</param>
    /// <param name="primaryPort">The remote port.</param>
    /// <param name="localPort">The local port.</param>
    /// <returns>0 (<c>CURL_PREREQFUNC_OK</c>) to send the request; any other value aborts it.</returns>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int CurlPrereqCallback(IntPtr userdata, IntPtr primaryIp, IntPtr localIp, int primaryPort, int localPort);
}
