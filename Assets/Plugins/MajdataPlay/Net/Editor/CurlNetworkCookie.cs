#nullable enable
namespace MajdataPlay.Net.Editor
{
    /// <summary>
    /// One cookie of a <c>Cookie</c> request header or a <c>Set-Cookie</c> response header, as the Curl Debugger shows it.
    /// </summary>
    internal readonly struct CurlNetworkCookie
    {
        /// <summary>
        /// Gets the cookie name.
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// Gets the cookie value.
        /// </summary>
        public string Value { get; init; }

        /// <summary>
        /// Gets the <c>Domain</c> attribute, or <see langword="null"/>.
        /// </summary>
        public string? Domain { get; init; }

        /// <summary>
        /// Gets the <c>Path</c> attribute, or <see langword="null"/>.
        /// </summary>
        public string? Path { get; init; }

        /// <summary>
        /// Gets the <c>Expires</c> attribute, or <see langword="null"/>.
        /// </summary>
        public string? Expires { get; init; }

        /// <summary>
        /// Gets the <c>Max-Age</c> attribute, or <see langword="null"/>.
        /// </summary>
        public string? MaxAge { get; init; }

        /// <summary>
        /// Gets the <c>SameSite</c> attribute, or <see langword="null"/>.
        /// </summary>
        public string? SameSite { get; init; }

        /// <summary>
        /// Gets whether the <c>HttpOnly</c> attribute is present.
        /// </summary>
        public bool HttpOnly { get; init; }

        /// <summary>
        /// Gets whether the <c>Secure</c> attribute is present.
        /// </summary>
        public bool Secure { get; init; }
    }
}
