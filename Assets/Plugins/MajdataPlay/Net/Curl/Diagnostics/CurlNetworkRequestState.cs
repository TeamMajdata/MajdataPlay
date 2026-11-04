#nullable enable
namespace MajdataPlay.Net.Curl.Diagnostics
{
    /// <summary>
    /// The lifecycle stage of a request recorded by <see cref="CurlNetworkMonitor"/>.
    /// </summary>
    public enum CurlNetworkRequestState
    {
        /// <summary>
        /// Waiting for the libcurl worker to start the transfer.
        /// </summary>
        Queued,

        /// <summary>
        /// The transfer has been handed to libcurl and has not finished yet.
        /// </summary>
        Running,

        /// <summary>
        /// libcurl finished the transfer successfully; the HTTP status may still be an error status.
        /// </summary>
        Completed,

        /// <summary>
        /// The transfer failed, for example because the connection could not be established.
        /// </summary>
        Failed,

        /// <summary>
        /// The transfer was cancelled, or its response or HTTP handler was disposed before it finished.
        /// </summary>
        Cancelled,
    }
}
