#nullable enable
namespace MajdataPlay.Net.Curl.Core.PInvoke
{
    /// <summary>Native <c>CURL_HTTP_VERSION_*</c> modes accepted by <c>CURLOPT_HTTP_VERSION</c>.</summary>
    internal enum CurlHttpVersionMode : long
    {
        /// <summary>Leave negotiation to libcurl.</summary>
        Default = 0,
        /// <summary>Send HTTP/1.0 requests.</summary>
        Http10 = 1,
        /// <summary>Send HTTP/1.1 requests.</summary>
        Http11 = 2,
        /// <summary>Attempt HTTP/2 with HTTP/1.1 fallback.</summary>
        Http2 = 3,
        /// <summary>Use HTTP/2 prior knowledge, offering only h2 over TLS.</summary>
        Http2PriorKnowledge = 5,
        /// <summary>Attempt HTTP/3 with HTTP/2 and HTTP/1.1 fallback.</summary>
        Http3 = 30,
        /// <summary>Attempt only HTTP/3.</summary>
        Http3Only = 31
    }
}
