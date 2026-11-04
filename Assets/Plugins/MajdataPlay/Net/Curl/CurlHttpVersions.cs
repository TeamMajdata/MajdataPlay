#nullable enable
using System;

namespace MajdataPlay.Net.Curl
{
    /// <summary>HTTP request versions that a curl handler can negotiate.</summary>
    [Flags]
    public enum CurlHttpVersions
    {
        /// <summary>No preferred version.</summary>
        None = 0,
        /// <summary>HTTP/1.0.</summary>
        Http10 = 1,
        /// <summary>HTTP/1.1.</summary>
        Http11 = 1 << 1,
        /// <summary>HTTP/2.</summary>
        Http2 = 1 << 2,
        /// <summary>HTTP/3 over QUIC.</summary>
        Http3 = 1 << 3,
        /// <summary>All HTTP versions known to the handler.</summary>
        All = Http10 | Http11 | Http2 | Http3
    }
}
