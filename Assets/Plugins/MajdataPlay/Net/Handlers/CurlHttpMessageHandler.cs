using MajdataPlay.Net.Curl;
using MajdataPlay.Net.Curl.Core;
using MajdataPlay.Net.Curl.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay.Net.Curl
{
    public class CurlHttpMessageHandler : HttpMessageHandler
    {
        public SslProtocols SslProtocols { get; set; } = SslProtocols.Tls12 | SslProtocols.Tls13;
        public IWebProxy? Proxy { get; set; }
        public bool PreAuthenticate { get; set; }
        /// <summary>
        /// Gets or sets the maximum length, in bytes, of one block of response headers (an interim or redirect
        /// response, the final response, or the trailers). Defaults to 64 KiB.
        /// </summary>
        /// <remarks>
        /// <c>HttpClientHandler.MaxResponseHeadersLength</c> is in kilobytes; this property is in bytes.
        /// </remarks>
        public int MaxResponseHeadersLength { get; set; } = 64 * 1024;
        /// <summary>
        /// Gets or sets the number of response body bytes that may be buffered ahead of the reader before the transfer
        /// is paused. Defaults to 64 KiB.
        /// </summary>
        public int ResponseBodyBufferSize { get; set; } = 64 * 1024;
        public long MaxRequestContentBufferSize { get; set; } = int.MaxValue;
        public int MaxConnectionsPerServer { get; set; } = 8;
        public int MaxAutomaticRedirections { get; set; } = 50;
        public ICredentials? DefaultProxyCredentials { get; set; }
        public ICredentials? Credentials { get; set; }
        public CookieContainer? CookieContainer { get; set; }
        public bool CheckCertificateRevocationList { get; set; }
        public DecompressionMethods AutomaticDecompression { get; set; } = DecompressionMethods.None;
        public bool AllowAutoRedirect { get; set; } = true;
        public bool UseDefaultCredentials { get; set; }
        public bool UseProxy { get; set; } = false;

        /// <summary>
        /// Gets the request counters and connection pool of this handler, also listed in <see cref="CurlStatistics"/>.
        /// </summary>
        public CurlHandlerStatistics Statistics
        {
            get => _curlMulti.Statistics;
        }

        readonly CurlMulti _curlMulti;

        public CurlHttpMessageHandler()
        {
            _curlMulti = new();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _curlMulti.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var config = CopyCurrentConfig();
            var content = request.Content;
            var contentStream = default(Stream?);
            if(content is not null)
            {
                contentStream = await content.ReadAsStreamAsync().ConfigureAwait(false);
            }
            var curlTask = _curlMulti.AddToQueue(request, contentStream, config, cancellationToken);

            try
            {
                var rsp = await curlTask.ConfigureAwait(false);

                return rsp;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new HttpRequestException(e.Message, e);
            }           
        }

        CurlHttpConfig CopyCurrentConfig()
        {
            var config = new CurlHttpConfig()
            {
                SslProtocols = SslProtocols,
                Proxy = Proxy,
                PreAuthenticate = PreAuthenticate,
                MaxResponseHeadersLength = MaxResponseHeadersLength,
                ResponseBodyBufferSize = ResponseBodyBufferSize,
                MaxRequestContentBufferSize = MaxRequestContentBufferSize,
                MaxConnectionsPerServer = MaxConnectionsPerServer,
                MaxAutomaticRedirections = MaxAutomaticRedirections,
                DefaultProxyCredentials = DefaultProxyCredentials,
                Credentials = Credentials,
                CookieContainer = CookieContainer,
                CheckCertificateRevocationList = CheckCertificateRevocationList,
                AutomaticDecompression = AutomaticDecompression,
                AllowAutoRedirect = AllowAutoRedirect,
                UseDefaultCredentials = UseDefaultCredentials,
                UseProxy = UseProxy
            };

            return config;
        }
    }
}
