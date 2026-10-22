#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MajdataPlay.Tests.Plugins.Net
{
    /// <summary>An in-process HTTP handler that records requests and answers them with a test-supplied function.</summary>
    internal sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        /// <summary>Creates the response for each request.</summary>
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        /// <summary>Creates a handler that answers with the given function.</summary>
        /// <param name="respond">Creates the response for each request.</param>
        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        /// <summary>Gets the range header text of every request, or null when a request had none.</summary>
        public List<string?> Ranges { get; } = new();

        /// <summary>Gets the URI of every request.</summary>
        public List<Uri?> Uris { get; } = new();

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Ranges.Add(request.Headers.Range?.ToString());
            Uris.Add(request.RequestUri);
            var response = _respond(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
