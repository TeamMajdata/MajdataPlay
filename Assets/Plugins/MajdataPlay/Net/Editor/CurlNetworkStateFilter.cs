#nullable enable
namespace MajdataPlay.Net.Editor
{
    /// <summary>
    /// Which requests the Curl Debugger's Network tab lists.
    /// </summary>
    internal enum CurlNetworkStateFilter
    {
        /// <summary>
        /// Every request.
        /// </summary>
        All,

        /// <summary>
        /// Requests that are queued or running.
        /// </summary>
        Pending,

        /// <summary>
        /// Requests that completed, failed or were cancelled.
        /// </summary>
        Finished,

        /// <summary>
        /// Requests that failed, were cancelled or got a 4xx/5xx status.
        /// </summary>
        Errors,
    }
}
