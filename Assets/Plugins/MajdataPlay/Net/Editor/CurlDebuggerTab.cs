#nullable enable
namespace MajdataPlay.Net.Editor
{
    /// <summary>
    /// The tabs of the Curl Debugger window.
    /// </summary>
    internal enum CurlDebuggerTab
    {
        /// <summary>
        /// The requests sent through the libcurl HTTP handler.
        /// </summary>
        Network,

        /// <summary>
        /// libcurl itself, the request counters and the connection pools.
        /// </summary>
        Info,
    }
}
