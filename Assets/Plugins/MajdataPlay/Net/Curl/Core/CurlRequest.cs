using AOT;
using MajdataPlay.Net.Curl.Core.PInvoke;
using MajdataPlay.Net.Curl.Diagnostics;
using MajdataPlay.Net.Curl.Lifecycle;
using MajdataPlay.Net.Curl.Utils;
using MajdataPlay.UnsafeKit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay.Net.Curl.Core
{
    internal class CurlRequest
    {
        public Uri RequestUri
        {
            get => Message.RequestUri;
        }
        public HttpRequestMessage Message { get; }
        public HttpContent? Content
        {
            get => Message.Content;
        }
        public HttpMethod Method
        {
            get => Message.Method;
        }

        /// <summary>
        /// Gets the network monitor's record of this request, which receives the uploaded body bytes, or
        /// <see langword="null"/> when recording was off as the request was sent.
        /// </summary>
        public CurlNetworkRecord? Record { get; init; }

        int _applyFlag = 0;

        Stream? _contentStream;

        GCHandle _handle;
        readonly static CurlReadOrWriteCallback _onReadCallback;  // Upload
        readonly static CurlSeekCallback _onSeekCallback;

        static CurlRequest()
        {
            _onReadCallback = OnReadCallback;
            _onSeekCallback = OnSeekCallback;
        }
        public CurlRequest(HttpRequestMessage httpRequest, Stream? contentStream)
        {
            if(httpRequest is null)
            {
                throw new ArgumentNullException(nameof(httpRequest));
            }
            Message = httpRequest;
            _contentStream = contentStream;    
        }
        ~CurlRequest()
        {
            if (_handle.IsAllocated)
            {
                _handle.Free();
            }
        }

        /// <summary>
        /// Frees the handle that libcurl's read and seek callbacks use to find this request, and skips finalization.
        /// Only call this once the easy handle has been cleaned up, because no callback can run after that.
        /// </summary>
        internal void ReleaseHandle()
        {
            if (_handle.IsAllocated)
            {
                _handle.Free();
            }
            GC.SuppressFinalize(this);
        }
        /// <summary>Configures the native request method, headers and upload callbacks for this transfer.</summary>
        /// <param name="curlEasy">The easy handle for this request.</param>
        /// <exception cref="InvalidOperationException">The options were already applied or libcurl rejected an option.</exception>
        /// <exception cref="ObjectDisposedException">The easy handle was disposed.</exception>
        /// <exception cref="NotSupportedException">A HEAD request specifies content with nonzero or unknown length.</exception>
        public void ApplyTo(CurlEasy curlEasy)
        {
            if (Interlocked.CompareExchange(ref _applyFlag, 1 , 0) == 1)
            {
                throw new InvalidOperationException("The curl options have already been applied and cannot be applied again.");
            }
            curlEasy.SetOption(CurlOption.Url, RequestUri.OriginalString);
            if (Method == HttpMethod.Head)
            {
                if (Content is not null && Content.Headers.ContentLength != 0)
                {
                    throw new NotSupportedException("A HEAD request can only have content with a known length of zero.");
                }
                // A custom verb alone leaves libcurl waiting for the representation's Content-Length bytes.
                curlEasy.SetOption(CurlOption.NoBody, 1L);
            }
            else
            {
                curlEasy.SetOption(CurlOption.CustomRequest, Method.Method);
                if (_contentStream is not null)
                {
                    curlEasy.SetOption(CurlOption.Upload, 1);
                    if (_contentStream.CanSeek)
                    {
                        curlEasy.SetOption(CurlOption.InFileSizeLarge, _contentStream.Length);
                    }
                }
            }
            curlEasy.SetHeaders(Message.Headers.Concat(Message.Content?.Headers?.AsEnumerable() ?? Array.Empty<KeyValuePair<string, IEnumerable<string>>>()));
            curlEasy.SetOption(CurlOption.ReadFunction, Marshal.GetFunctionPointerForDelegate(_onReadCallback));
            curlEasy.SetOption(CurlOption.SeekFunction, Marshal.GetFunctionPointerForDelegate(_onSeekCallback));

            _handle = GCHandle.Alloc(this, GCHandleType.Weak);
            var handlePtr = GCHandle.ToIntPtr(_handle);
            curlEasy.SetOption(CurlOption.ReadData, handlePtr);
            curlEasy.SetOption(CurlOption.SeekData, handlePtr);

            CurlUtility.ApplySystemCA(curlEasy);
        }

        [MonoPInvokeCallback(typeof(CurlReadOrWriteCallback))]
        static unsafe UIntPtr OnReadCallback(IntPtr ptr, UIntPtr size, UIntPtr nmemb, IntPtr userdata)
        {
            if(!UnsafeHelper.TryGetInstanceFromGCHandle<CurlRequest>(userdata, out var curlReq))
            {
                return UIntPtr.Zero;
            }
            var contentStream = curlReq._contentStream;
            if (contentStream is null)
            {
                return UIntPtr.Zero;
            }
            var length = (int)(size.ToUInt32() * nmemb.ToUInt32());
            var buffer = new Span<byte>((void*)ptr, length);
            try
            {
                var read = contentStream.Read(buffer);
                curlReq.Record?.OnRequestBodyRead(buffer.Slice(0, read));

                return (UIntPtr)read;
            }
            catch
            {
                // The content may have been disposed or may fail; an exception must never unwind through libcurl's
                // native frames, so the transfer is aborted instead.
                return CurlCallbackReturn.Read.Abort;
            }
        }
        [MonoPInvokeCallback(typeof(CurlSeekCallback))]
        static unsafe CurlSeekCallbackReturn OnSeekCallback(IntPtr userdata, long offset, SeekOrigin origin)
        {
            if (!UnsafeHelper.TryGetInstanceFromGCHandle<CurlRequest>(userdata, out var curlReq))
            {
                return CurlSeekCallbackReturn.Fail;
            }
            var contentStream = curlReq._contentStream;
            if (contentStream is null)
            {
                return CurlSeekCallbackReturn.Fail;
            }
            else if(!contentStream.CanSeek)
            {
                return CurlSeekCallbackReturn.CantSeek;
            }
            try
            {
                var position = contentStream.Seek(offset, origin);
                curlReq.Record?.OnRequestBodyRewound(position);
            }
            catch
            {
                return CurlSeekCallbackReturn.Fail;
            }
            return CurlSeekCallbackReturn.Ok;
        }

        public static async ValueTask<CurlRequest> CreateAsync(HttpRequestMessage requestMessage)
        {
            var contentStream = default(Stream?);
            var content = requestMessage.Content;
            if (content is not null && requestMessage.Method != HttpMethod.Head)
            {
                contentStream = await content.ReadAsStreamAsync();
            }
            return new(requestMessage, contentStream);
        }
    }
}
