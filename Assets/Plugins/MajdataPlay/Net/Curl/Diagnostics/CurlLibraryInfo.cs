#nullable enable
using System;
using System.Collections.Generic;

namespace MajdataPlay.Net.Curl.Diagnostics
{
    /// <summary>
    /// What the loaded libcurl reports about itself through <c>curl_version_info</c>.
    /// </summary>
    public sealed class CurlLibraryInfo
    {
        /// <summary>
        /// Gets the libcurl version, such as <c>8.21.0-DEV</c>.
        /// </summary>
        public string Version { get; internal init; } = string.Empty;

        /// <summary>
        /// Gets the numeric version, <c>0xMMmmpp</c>.
        /// </summary>
        public uint VersionNumber { get; internal init; }

        /// <summary>
        /// Gets the target the library was built for, such as <c>x86_64-pc-win32</c>.
        /// </summary>
        public string Host { get; internal init; } = string.Empty;

        /// <summary>
        /// Gets the TLS backend and its version, or <see langword="null"/> without TLS support.
        /// </summary>
        public string? SslVersion { get; internal init; }

        /// <summary>
        /// Gets the zlib version, or <see langword="null"/> without zlib.
        /// </summary>
        public string? LibzVersion { get; internal init; }

        /// <summary>
        /// Gets the supported protocols.
        /// </summary>
        public IReadOnlyList<string> Protocols { get; internal init; } = Array.Empty<string>();

        /// <summary>
        /// Gets the compiled-in features.
        /// </summary>
        public CurlFeatures Features { get; internal init; }
    }
}
