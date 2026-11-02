#nullable enable
namespace MajdataPlay.Net.Curl.Core.PInvoke
{
    /// <summary>
    /// libcurl <c>curl_infotype</c>: the kind of information passed to a <see cref="CurlDebugCallback"/>.
    /// </summary>
    public enum CurlDebugInfoType
    {
        /// <summary>
        /// Informational text.
        /// </summary>
        Text = 0,

        /// <summary>
        /// Header data received from the peer.
        /// </summary>
        HeaderIn = 1,

        /// <summary>
        /// Header data sent to the peer: one complete request head per call.
        /// </summary>
        HeaderOut = 2,

        /// <summary>
        /// Unprocessed protocol data received from the peer.
        /// </summary>
        DataIn = 3,

        /// <summary>
        /// Protocol data sent to the peer.
        /// </summary>
        DataOut = 4,

        /// <summary>
        /// SSL/TLS data received from the peer.
        /// </summary>
        SslDataIn = 5,

        /// <summary>
        /// SSL/TLS data sent to the peer.
        /// </summary>
        SslDataOut = 6,
    }
}
