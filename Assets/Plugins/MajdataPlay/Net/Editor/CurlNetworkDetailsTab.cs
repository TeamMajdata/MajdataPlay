#nullable enable
namespace MajdataPlay.Net.Editor
{
    /// <summary>
    /// The tabs of the request details panel.
    /// </summary>
    internal enum CurlNetworkDetailsTab
    {
        /// <summary>
        /// General information and the request and response headers.
        /// </summary>
        Headers,

        /// <summary>
        /// The query string and the request body.
        /// </summary>
        Payload,

        /// <summary>
        /// The response body.
        /// </summary>
        Response,

        /// <summary>
        /// The cookies sent and set.
        /// </summary>
        Cookies,

        /// <summary>
        /// The timing breakdown.
        /// </summary>
        Timing,
    }
}
