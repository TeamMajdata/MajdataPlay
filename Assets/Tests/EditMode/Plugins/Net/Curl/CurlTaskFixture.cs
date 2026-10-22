#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using MajdataPlay.Net.Curl;
using MajdataPlay.Net.Curl.Core;

namespace MajdataPlay.Tests.Plugins.Net.Curl
{
    /// <summary>
    /// Builds a transfer around a real easy handle without a multi handle, recording the owner callbacks, and releases
    /// every native resource when disposed.
    /// </summary>
    internal sealed class CurlTaskFixture : IDisposable
    {
        /// <summary>The strong handle passed to the callbacks as user data.</summary>
        private GCHandle _handle;

        /// <summary>Creates a transfer for a GET request to a local URL.</summary>
        /// <param name="config">The handler settings snapshot.</param>
        /// <param name="token">The caller's cancellation token.</param>
        public CurlTaskFixture(CurlHttpConfig config, CancellationToken token = default)
        {
            Message = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:9/resource");
            Request = new CurlRequest(Message, null);
            Task = new CurlTask(new CurlEasy(), Request, config,
                _ => Interlocked.Increment(ref ResumeCount),
                _ => Interlocked.Increment(ref DisposeCount),
                _ => Interlocked.Increment(ref CancelCount),
                token);
            _handle = GCHandle.Alloc(Task);
        }

        /// <summary>Gets the request message.</summary>
        public HttpRequestMessage Message { get; }

        /// <summary>Gets the libcurl request wrapper.</summary>
        public CurlRequest Request { get; }

        /// <summary>Gets the transfer under test.</summary>
        public CurlTask Task { get; }

        /// <summary>Gets the user data pointer libcurl would pass to the callbacks.</summary>
        public IntPtr UserData => GCHandle.ToIntPtr(_handle);

        /// <summary>The number of resume requests forwarded to the owner.</summary>
        public int ResumeCount;

        /// <summary>The number of dispose requests forwarded to the owner.</summary>
        public int DisposeCount;

        /// <summary>The number of cancellation requests forwarded to the owner.</summary>
        public int CancelCount;

        /// <summary>Creates a settings snapshot with small, explicit limits.</summary>
        /// <param name="maxHeaders">The header block budget in bytes.</param>
        /// <param name="bodyBuffer">The body buffer size in bytes.</param>
        /// <param name="cookies">An optional cookie container.</param>
        /// <returns>The settings snapshot.</returns>
        public static CurlHttpConfig Config(int maxHeaders = 1024, int bodyBuffer = 1024, CookieContainer? cookies = null)
        {
            return new CurlHttpConfig
            {
                MaxResponseHeadersLength = maxHeaders,
                ResponseBodyBufferSize = bodyBuffer,
                CookieContainer = cookies
            };
        }

        /// <summary>Moves the transfer to the submitted state, as the multi worker does before the first callback.</summary>
        /// <returns>This fixture.</returns>
        public CurlTaskFixture Submit()
        {
            Task.TryEnterSubmittedState();
            return this;
        }

        /// <summary>Frees the callback handle and the transfer's native resources.</summary>
        public void Dispose()
        {
            if (_handle.IsAllocated)
            {
                _handle.Free();
            }
            Task.ReleaseCancellationRegistration();
            Task.ReleaseNativeResources();
            Message.Dispose();
        }
    }
}
