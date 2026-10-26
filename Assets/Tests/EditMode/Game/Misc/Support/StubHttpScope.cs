using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.Net;
using MajdataPlay.Tests;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Support
{
    /// <summary>One request received by <see cref="StubHttpScope"/>.</summary>
    internal sealed class RecordedRequest
    {
        /// <summary>Creates a request record.</summary>
        /// <param name="method">The HTTP method.</param>
        /// <param name="uri">The request URI.</param>
        /// <param name="body">The request body, or an empty string.</param>
        public RecordedRequest(HttpMethod method, Uri uri, string body)
        {
            Method = method;
            Uri = uri;
            Body = body;
        }

        /// <summary>Gets the HTTP method.</summary>
        public HttpMethod Method { get; }

        /// <summary>Gets the request URI.</summary>
        public Uri Uri { get; }

        /// <summary>Gets the request body.</summary>
        public string Body { get; }
    }

    /// <summary>
    /// Routes the shared HTTP clients of <see cref="MajEnv"/> to a handler answered by a test responder, so online code
    /// runs without network access. Disposing restores the original handler and clears the online response caches.
    /// </summary>
    internal sealed class StubHttpScope : IDisposable
    {
        /// <summary>The handler answering every request.</summary>
        private sealed class StubHandler : HttpMessageHandler
        {
            /// <summary>The owning scope.</summary>
            private readonly StubHttpScope _owner;

            /// <summary>Creates the handler.</summary>
            /// <param name="owner">The owning scope.</param>
            public StubHandler(StubHttpScope owner)
            {
                _owner = owner;
            }

            /// <summary>Records the request and returns the responder's answer.</summary>
            /// <param name="request">The request.</param>
            /// <param name="cancellationToken">The request cancellation token.</param>
            /// <returns>The response.</returns>
            /// <exception cref="OperationCanceledException">The request was canceled.</exception>
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync();
                _owner._requests.Enqueue(new RecordedRequest(request.Method, request.RequestUri!, body));
                return _owner.Responder(request);
            }
        }

        /// <summary>The recorded requests.</summary>
        private readonly ConcurrentQueue<RecordedRequest> _requests = new();

        /// <summary>The handler field of <see cref="HttpMessageInvoker"/>, which every shared client sends through.</summary>
        private static readonly FieldInfo s_handlerField = FindHandlerField();

        /// <summary>The handler the shared clients used before the scope.</summary>
        private readonly object? _savedSharedHandler;

        /// <summary>The handler the background client used before the scope.</summary>
        private readonly object? _savedBackgroundHandler;

        /// <summary>The stub handler installed by the scope.</summary>
        private readonly StubHandler _handler;

        /// <summary>Installs the stub handler, answering with 404 until <see cref="Responder"/> is replaced.</summary>
        public StubHttpScope()
        {
            // The client properties are read-only statics whose values the JIT may fold, so the handler inside the
            // existing clients is replaced instead of the clients themselves.
            _handler = new StubHandler(this);
            Responder = _ => Respond(HttpStatusCode.NotFound, string.Empty);
            _savedSharedHandler = s_handlerField.GetValue(MajEnv.SharedHttpClient);
            _savedBackgroundHandler = s_handlerField.GetValue(MajEnv.BackgroundHttpClient);
            s_handlerField.SetValue(MajEnv.SharedHttpClient, _handler);
            s_handlerField.SetValue(MajEnv.BackgroundHttpClient, _handler);
            ClearOnlineState();
        }

        /// <summary>Gets or sets the function answering requests.</summary>
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; }

        /// <summary>Gets the requests received so far.</summary>
        public RecordedRequest[] Requests => _requests.ToArray();

        /// <summary>Creates a response with a JSON or text body.</summary>
        /// <param name="status">The status code.</param>
        /// <param name="body">The body.</param>
        /// <returns>The response.</returns>
        public static HttpResponseMessage Respond(HttpStatusCode status, string body)
        {
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }

        /// <summary>Answers every request with the same status and body.</summary>
        /// <param name="status">The status code.</param>
        /// <param name="body">The body.</param>
        public void RespondWith(HttpStatusCode status, string body = "")
        {
            Responder = _ => Respond(status, body);
        }

        /// <summary>Finds the private field of <see cref="HttpMessageInvoker"/> holding its handler.</summary>
        /// <returns>The handler field.</returns>
        /// <exception cref="MissingFieldException">The runtime has no such field.</exception>
        private static FieldInfo FindHandlerField()
        {
            foreach (var field in typeof(HttpMessageInvoker).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (field.FieldType == typeof(HttpMessageHandler))
                {
                    return field;
                }
            }
            throw new MissingFieldException(typeof(HttpMessageInvoker).FullName, "handler");
        }

        /// <summary>Clears the per-endpoint statistics and cached responses kept by <see cref="Online"/>.</summary>
        public static void ClearOnlineState()
        {
            PrivateMember.GetStaticField<IDictionary>(typeof(Online), "_endpointStatistics").Clear();
            Online.ClearResponseCache();
        }

        /// <summary>Restores the original clients and clears the online state.</summary>
        public void Dispose()
        {
            s_handlerField.SetValue(MajEnv.SharedHttpClient, _savedSharedHandler);
            s_handlerField.SetValue(MajEnv.BackgroundHttpClient, _savedBackgroundHandler);
            ClearOnlineState();
            _handler.Dispose();
        }
    }
}
