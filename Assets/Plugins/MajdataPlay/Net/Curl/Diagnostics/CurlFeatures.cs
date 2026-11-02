#nullable enable
using System;

namespace MajdataPlay.Net.Curl.Diagnostics
{
    /// <summary>
    /// The <c>CURL_VERSION_*</c> feature bits reported by <c>curl_version_info</c>.
    /// </summary>
    [Flags]
    public enum CurlFeatures
    {
        /// <summary>
        /// No feature.
        /// </summary>
        None = 0,

        /// <summary>
        /// IPv6 support.
        /// </summary>
        IPv6 = 1 << 0,

        /// <summary>
        /// Kerberos V4 authentication (removed from libcurl).
        /// </summary>
        Kerberos4 = 1 << 1,

        /// <summary>
        /// SSL/TLS support.
        /// </summary>
        Ssl = 1 << 2,

        /// <summary>
        /// zlib content decoding.
        /// </summary>
        Libz = 1 << 3,

        /// <summary>
        /// NTLM authentication.
        /// </summary>
        Ntlm = 1 << 4,

        /// <summary>
        /// GSS-Negotiate authentication (superseded by <see cref="Spnego"/>).
        /// </summary>
        GssNegotiate = 1 << 5,

        /// <summary>
        /// A debug build of libcurl.
        /// </summary>
        Debug = 1 << 6,

        /// <summary>
        /// Asynchronous name resolution.
        /// </summary>
        AsynchDns = 1 << 7,

        /// <summary>
        /// SPNEGO authentication.
        /// </summary>
        Spnego = 1 << 8,

        /// <summary>
        /// Files larger than 2 GB.
        /// </summary>
        LargeFile = 1 << 9,

        /// <summary>
        /// Internationalized domain names.
        /// </summary>
        Idn = 1 << 10,

        /// <summary>
        /// Windows SSPI.
        /// </summary>
        Sspi = 1 << 11,

        /// <summary>
        /// Character conversions (removed from libcurl).
        /// </summary>
        Conv = 1 << 12,

        /// <summary>
        /// libcurl's memory debugging.
        /// </summary>
        CurlDebug = 1 << 13,

        /// <summary>
        /// TLS-SRP authentication.
        /// </summary>
        TlsAuthSrp = 1 << 14,

        /// <summary>
        /// NTLM delegation to winbind (removed from libcurl).
        /// </summary>
        NtlmWb = 1 << 15,

        /// <summary>
        /// HTTP/2.
        /// </summary>
        Http2 = 1 << 16,

        /// <summary>
        /// GSS-API.
        /// </summary>
        GssApi = 1 << 17,

        /// <summary>
        /// Kerberos V5 authentication.
        /// </summary>
        Kerberos5 = 1 << 18,

        /// <summary>
        /// Unix domain sockets.
        /// </summary>
        UnixSockets = 1 << 19,

        /// <summary>
        /// The Public Suffix List for cookies.
        /// </summary>
        Psl = 1 << 20,

        /// <summary>
        /// HTTPS proxies.
        /// </summary>
        HttpsProxy = 1 << 21,

        /// <summary>
        /// Several TLS backends selectable at run time.
        /// </summary>
        MultiSsl = 1 << 22,

        /// <summary>
        /// Brotli content decoding.
        /// </summary>
        Brotli = 1 << 23,

        /// <summary>
        /// Alt-Svc handling.
        /// </summary>
        AltSvc = 1 << 24,

        /// <summary>
        /// HTTP/3.
        /// </summary>
        Http3 = 1 << 25,

        /// <summary>
        /// Zstandard content decoding.
        /// </summary>
        Zstd = 1 << 26,

        /// <summary>
        /// Unicode file names on Windows.
        /// </summary>
        Unicode = 1 << 27,

        /// <summary>
        /// HTTP Strict Transport Security.
        /// </summary>
        Hsts = 1 << 28,

        /// <summary>
        /// SASL through libgsasl.
        /// </summary>
        Gsasl = 1 << 29,

        /// <summary>
        /// Thread-safe global initialization.
        /// </summary>
        ThreadSafe = 1 << 30,
    }
}
