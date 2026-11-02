#nullable enable
namespace MajdataPlay.Net.Editor
{
    /// <summary>
    /// The columns of the request table in the Curl Debugger's Network tab, in display order.
    /// </summary>
    internal enum CurlNetworkColumn
    {
        /// <summary>
        /// The last path segment and query.
        /// </summary>
        Name,

        /// <summary>
        /// The requested URL.
        /// </summary>
        Url,

        /// <summary>
        /// The HTTP method.
        /// </summary>
        Method,

        /// <summary>
        /// The status code or lifecycle stage.
        /// </summary>
        Status,

        /// <summary>
        /// The HTTP protocol version.
        /// </summary>
        Protocol,

        /// <summary>
        /// The remote endpoint.
        /// </summary>
        RemoteAddress,

        /// <summary>
        /// The short resource type of the response.
        /// </summary>
        Type,

        /// <summary>
        /// The received header and body bytes.
        /// </summary>
        Size,

        /// <summary>
        /// The total time.
        /// </summary>
        Time,

        /// <summary>
        /// The time to first byte.
        /// </summary>
        Ttfb,

        /// <summary>
        /// The content download time.
        /// </summary>
        Transfer,

        /// <summary>
        /// The phases of every request on a common time line.
        /// </summary>
        Waterfall,
    }
}
