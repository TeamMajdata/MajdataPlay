#nullable enable
using System;
using System.Runtime.InteropServices;

namespace MajdataPlay.Net.Curl.Core.PInvoke
{
    /// <summary>
    /// libcurl <c>curl_debug_callback</c>, installed with <see cref="CurlOption.DebugFunction"/>. libcurl calls it while
    /// <see cref="CurlOption.Verbose"/> is set.
    /// </summary>
    /// <param name="handle">The easy handle the information belongs to.</param>
    /// <param name="type">The kind of information in <paramref name="data"/>.</param>
    /// <param name="data">The information; not NUL-terminated.</param>
    /// <param name="size">The length of <paramref name="data"/> in bytes.</param>
    /// <param name="userdata">The pointer set with <see cref="CurlOption.DebugData"/>.</param>
    /// <returns>Must return 0.</returns>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int CurlDebugCallback(IntPtr handle, CurlDebugInfoType type, IntPtr data, UIntPtr size, IntPtr userdata);
}
