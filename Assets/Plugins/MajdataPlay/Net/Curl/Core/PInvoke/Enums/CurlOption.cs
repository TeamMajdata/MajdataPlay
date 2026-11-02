using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MajdataPlay.Net.Curl.Core.PInvoke
{
    /// <summary>
    /// Defines the complete and commonly used libcurl options (corresponding to CURLoption in curl/curl.h).
    /// </summary>
    /// <remarks>
    /// Value calculation rules based on libcurl data types:
    /// <list type="bullet">
    /// <item><description><c>LONG</c> = 0 + offset</description></item>
    /// <item><description><c>OBJECTPOINT</c> (Pointers / Strings) = 10000 + offset</description></item>
    /// <item><description><c>FUNCTIONPOINT</c> (Callback Delegates) = 20000 + offset</description></item>
    /// <item><description><c>OFF_T</c> (Large Integers / 64-bit) = 30000 + offset</description></item>
    /// </list>
    /// </remarks>
    public enum CurlOption
    {
        #region LONG options
        File = 10000 - 1, // placeholder, never used

        /// <summary>Enable verbose output.</summary>
        Verbose = 41,

        /// <summary>Enable progress meter.</summary>
        NoProgress = 43,

        /// <summary>Specify timeout in seconds.</summary>
        Timeout = 13,

        /// <summary>Connection timeout in seconds.</summary>
        ConnectTimeout = 78,

        /// <summary>Specify timeout in milliseconds (<c>CURLOPT_TIMEOUT_MS</c>).</summary>
        TimeoutMs = 155,

        /// <summary>Connection timeout in milliseconds (<c>CURLOPT_CONNECTTIMEOUT_MS</c>).</summary>
        ConnectTimeoutMs = 156,

        /// <summary>Maximum number of redirects.</summary>
        MaxRedirs = 68,

        /// <summary>Follow HTTP redirects.</summary>
        FollowLocation = 52,

        /// <summary>HTTP version.</summary>
        HttpVersion = 84,

        /// <summary>Fail on HTTP errors.</summary>
        FailOnError = 45,

        /// <summary>Use TCP keepalive.</summary>
        TcpKeepAlive = 213,

        /// <summary>TCP keepalive idle.</summary>
        TcpKeepIdle = 214,

        /// <summary>TCP keepalive interval.</summary>
        TcpKeepIntvl = 215,

        /// <summary>IPv4 only.</summary>
        IpResolve = 113,

        /// <summary>Transfer direction.</summary>
        Upload = 46,

        /// <summary>HTTP POST.</summary>
        Post = 47,

        /// <summary>Buffer size.</summary>
        BufferSize = 98,

        /// <summary>DNS cache timeout.</summary>
        DnsCacheTimeout = 92,

        /// <summary>DNS servers.</summary>
        DnsServers = 102,

        /// <summary>Low speed limit.</summary>
        LowSpeedLimit = 19,

        /// <summary>Low speed time.</summary>
        LowSpeedTime = 20,

        /// <summary>Transfer encoding.</summary>
        TransferEncoding = 207,

        /// <summary>HTTP authentication.</summary>
        HttpAuth = 107,

        /// <summary>Proxy authentication.</summary>
        ProxyAuth = 111,

        /// <summary>Resume offset.</summary>
        ResumeFrom = 21,

        /// <summary>SSL verify peer.</summary>
        SslVerifyPeer = 64,

        /// <summary>
        /// SSL version
        /// </summary>
        SslVersion = 32,

        /// <summary>
        /// SSL options
        /// </summary>
        SslOptions = 216,

        /// <summary>SSL verify host.</summary>
        SslVerifyHost = 81,

        /// <summary>HTTP proxy tunnel.</summary>
        HttpProxyTunnel = 61,

        /// <summary>Disable signals.</summary>
        Nosignal = 99,

        /// <summary>HTTP/3 support.</summary>
        Http3 = 264,

        /// <summary>Enable HTTP/2 prior knowledge.</summary>
        Http2PriorKnowledge = 150,

        /// <summary>
        /// Last option marker.
        /// </summary>
        Last = 314,

        #endregion


        #region OBJECTPOINT options (10000+)

        /// <summary>Request URL.</summary>
        Url = CurlOptionType.ObjectPointer + 2,

        /// <summary>HTTP user agent.</summary>
        UserAgent = CurlOptionType.ObjectPointer + 18,

        /// <summary>HTTP headers list.</summary>
        HttpHeader = CurlOptionType.ObjectPointer + 23,

        /// <summary>Cookie string.</summary>
        Cookie = CurlOptionType.ObjectPointer + 22,

        /// <summary>Cookie file.</summary>
        CookieFile = CurlOptionType.ObjectPointer + 31,

        /// <summary>Cookie save file.</summary>
        CookieJar = CurlOptionType.ObjectPointer + 82,

        /// <summary>Custom HTTP method.</summary>
        CustomRequest = CurlOptionType.ObjectPointer + 36,

        /// <summary>POST body.</summary>
        PostFields = CurlOptionType.ObjectPointer + 15,

        /// <summary>Proxy URL.</summary>
        Proxy = CurlOptionType.ObjectPointer + 4,

        /// <summary>
        /// Basic Authentication
        /// </summary>
        UserPassword = CurlOptionType.ObjectPointer + 5,

        /// <summary>Proxy username/password.</summary>
        ProxyUserPwd = CurlOptionType.ObjectPointer + 6,

        /// <summary>SSL CA certificate file.</summary>
        CaInfo = CurlOptionType.ObjectPointer + 65,

        /// <summary>SSL CA directory.</summary>
        CaPath = CurlOptionType.ObjectPointer + 97,

        /// <summary>Client certificate.</summary>
        SslCert = CurlOptionType.ObjectPointer + 25,

        /// <summary>Client private key.</summary>
        Key = CurlOptionType.ObjectPointer + 87,

        /// <summary>Accept gzip/br compression.</summary>
        AcceptEncoding = CurlOptionType.ObjectPointer + 102,

        /// <summary>Referer header.</summary>
        Referer = CurlOptionType.ObjectPointer + 16,

        /// <summary>Range request.</summary>
        Range = CurlOptionType.ObjectPointer + 7,

        /// <summary>SSL cipher list.</summary>
        SslCipherList = CurlOptionType.ObjectPointer + 83,

        /// <summary>Unix socket path.</summary>
        UnixSocketPath = CurlOptionType.ObjectPointer + 231,

        /// <summary>
        /// Represents a private handle within libcurl.
        /// </summary>
        Private = CurlOptionType.ObjectPointer + 103,

        /// <summary>
        /// Pass a pointer to the write callback userdata.
        /// </summary>
        WriteData = CurlOptionType.ObjectPointer + 1,

        /// <summary>
        /// Pass a pointer to the read callback userdata.
        /// </summary>
        ReadData = CurlOptionType.ObjectPointer + 9,

        /// <summary>
        /// Pass a pointer to the header callback userdata.
        /// </summary>
        HeaderData = CurlOptionType.ObjectPointer + 29,

        /// <summary>
        /// Pass a pointer to the seek callback userdata.
        /// </summary>
        SeekData = CurlOptionType.ObjectPointer + 168,

        /// <summary>
        /// Pass a pointer to the debug callback userdata.
        /// </summary>
        DebugData = CurlOptionType.ObjectPointer + 95,

        /// <summary>
        /// Pass a pointer to the pre-request callback userdata.
        /// </summary>
        PrereqData = CurlOptionType.ObjectPointer + 313,

        #endregion


        #region FUNCTIONPOINT options (20000+)

        /// <summary>Write callback.</summary>
        WriteFunction = CurlOptionType.FunctionPointer + 11,

        /// <summary>Read callback.</summary>
        ReadFunction = CurlOptionType.FunctionPointer + 12,

        /// <summary>Progress callback.</summary>
        ProgressFunction = CurlOptionType.FunctionPointer + 56,

        /// <summary>Header callback.</summary>
        HeaderFunction = CurlOptionType.FunctionPointer + 79,        

        /// <summary>Debug callback.</summary>
        DebugFunction = CurlOptionType.FunctionPointer + 94,

        /// <summary>
        /// Set the SSL context callback function, currently only for OpenSSL or
        /// wolfSSL ssl_ctx, or mbedTLS mbedtls_ssl_config in the second argument.
        /// The function must match the curl_ssl_ctx_callback prototype.
        /// </summary>
        SslContextFunction = CurlOptionType.FunctionPointer + 108,

        /// <summary>
        /// callback function for setting socket options
        /// </summary>
        SocketOptFunction = CurlOptionType.FunctionPointer + 148,

        /// <summary>
        /// Callback function for opening socket (instead of socket(2)). Optionally,
        /// callback is able change the address or refuse to connect returning
        /// CURL_SOCKET_BAD. The callback should have type
        /// curl_opensocket_callback
        /// </summary>
        OpenSocketFunction = CurlOptionType.FunctionPointer + 163,

        /// <summary>
        /// Callback function for seeking in the input stream
        /// </summary>
        SeekFunction = CurlOptionType.FunctionPointer + 167,

        /// <summary>
        /// Pre-request callback, called once a connection is ready and just before each request is sent.
        /// </summary>
        PrereqFunction = CurlOptionType.FunctionPointer + 312,

        #endregion


        #region OFF_T options (30000+)

        /// <summary>Upload size.</summary>
        InFileSizeLarge = CurlOptionType.Off_T + 115,

        /// <summary>POST size.</summary>
        PostFieldSizeLarge = CurlOptionType.Off_T + 120,

        /// <summary>Maximum download speed.</summary>
        MaxRecvSpeedLarge = CurlOptionType.Off_T + 146,

        /// <summary>Maximum upload speed.</summary>
        MaxSendSpeedLarge = CurlOptionType.Off_T + 145,

        #endregion
    }
}
