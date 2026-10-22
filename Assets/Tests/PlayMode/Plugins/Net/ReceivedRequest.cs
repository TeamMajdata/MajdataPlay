#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace MajdataPlay.Tests.Plugins.Net
{
    /// <summary>One HTTP request recorded by <see cref="LoopbackHttpServer"/>.</summary>
    public sealed class ReceivedRequest
    {
        /// <summary>Initializes a recorded request without a body.</summary>
        /// <param name="method">The request method.</param>
        /// <param name="target">The request target, either a path or an absolute URI for proxy requests.</param>
        /// <param name="headers">The header fields in arrival order.</param>
        public ReceivedRequest(string method, string target, IReadOnlyList<KeyValuePair<string, string>> headers)
        {
            Method = method;
            Target = target;
            Headers = headers;
        }

        /// <summary>Gets the request method.</summary>
        public string Method { get; }

        /// <summary>Gets the request target.</summary>
        public string Target { get; }

        /// <summary>Gets the header fields in arrival order.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Headers { get; }

        /// <summary>Gets or sets the decoded request body.</summary>
        public byte[] Body { get; set; } = Array.Empty<byte>();

        /// <summary>Gets the body decoded as UTF-8.</summary>
        public string BodyText => Encoding.UTF8.GetString(Body);

        /// <summary>Gets the first value of a header, compared case-insensitively.</summary>
        /// <param name="name">The header name.</param>
        /// <returns>The header value, or null when absent.</returns>
        public string? GetHeader(string name)
        {
            foreach (var header in Headers)
            {
                if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return header.Value;
                }
            }
            return null;
        }
    }
}
