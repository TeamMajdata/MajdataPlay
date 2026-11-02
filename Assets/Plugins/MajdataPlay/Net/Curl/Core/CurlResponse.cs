using AOT;
using MajdataPlay.Net.Curl.Core.PInvoke;
using MajdataPlay.UnsafeKit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay.Net.Curl.Core
{
    public class CurlResponse : HttpResponseMessage
    {
        public CurlCode? ResultCode { get; set; }
        public CurlHttpConfig Config { get; }
        internal CurlRequest Request { get; }

        int _disposeFlag = 0;
        /// <summary>
        /// Bytes received so far in the current header block, checked against <see cref="CurlHttpConfig.MaxResponseHeadersLength"/>.
        /// </summary>
        long _currentHeadersLength = 0;

        readonly Action _onResume;
        readonly Action _onDispose;
        readonly CurlResponseStream _responseStream;
        readonly static CurlReadOrWriteCallback _onWriteCallback; // Download
        readonly static CurlReadOrWriteCallback _onHeaderReceivedCallback;

        static CurlResponse()
        {
            _onWriteCallback = OnWriteCallback;
            _onHeaderReceivedCallback = OnHeaderReceived;
        }

        /// <summary>
        /// Creates the response for a transfer and installs its body and header callbacks on <paramref name="handle"/>.
        /// The easy handle stays owned by the <see cref="CurlTask"/>.
        /// </summary>
        /// <param name="handle">The easy handle that performs the transfer.</param>
        /// <param name="request">The request being answered.</param>
        /// <param name="onResume">Asks the owner to resume a transfer paused by body backpressure.</param>
        /// <param name="onDispose">Tells the owner that the response or its body stream has been disposed.</param>
        /// <param name="config">The handler settings captured when the request was sent.</param>
        internal CurlResponse(CurlEasy handle, CurlRequest request, Action onResume, Action onDispose, CurlHttpConfig config)
        {
            Request = request;
            Config = config;
            _onResume = onResume;
            _onDispose = onDispose;
            _responseStream = new CurlResponseStream(config.ResponseBodyBufferSize, _onResume, _onDispose);
            
            Content = new StreamContent(_responseStream);
            RequestMessage = Request.Message;

            handle.SetOption(CurlOption.WriteFunction, Marshal.GetFunctionPointerForDelegate(_onWriteCallback));
            handle.SetOption(CurlOption.HeaderFunction, Marshal.GetFunctionPointerForDelegate(_onHeaderReceivedCallback));
        }

        internal void Complete()
        {
            _responseStream.CompleteWriting();
        }
        public void Abort()
        {
            Abort(new OperationCanceledException("Request was aborted."));
        }
        internal void Abort(Exception abortException)
        {
            _responseStream.Abort(abortException);
        }


        [MonoPInvokeCallback(typeof(CurlReadOrWriteCallback))]
        static unsafe UIntPtr OnWriteCallback(IntPtr ptr, UIntPtr size, UIntPtr nmemb, IntPtr userdata)
        {
            if (!UnsafeHelper.TryGetInstanceFromGCHandle<CurlTask>(userdata, out var curlTask))
            {
                return UIntPtr.Zero;
            }
            else if(curlTask.State != CurlTaskState.HeaderRead && curlTask.TryEnterHeaderReadState())
            {
                // libcurl knows the connection and the time to first byte by now, so a long download shows them early.
                curlTask.CaptureTransferInfo();
            }
            var curlRsp = curlTask.Response;
            var responseStream = curlRsp._responseStream;
            var length = (int)(size.ToUInt32() * nmemb.ToUInt32());
            var buffer = new ReadOnlySpan<byte>((void*)ptr, length);
            var result = responseStream.WriteChunk(buffer);
            // A paused or rejected chunk is delivered again or not at all, so only accepted bytes are recorded.
            if (result == (UIntPtr)length)
            {
                curlTask.Record?.OnResponseBodyReceived(buffer);
            }

            return result;
        }
        /// <summary>
        /// libcurl header callback. Called on the multi worker thread once per header line, including status lines,
        /// the blank line that ends each header block, and the trailers of a chunked body.
        /// </summary>
        /// <param name="ptr">The header line; not NUL-terminated.</param>
        /// <param name="size">Always 1.</param>
        /// <param name="nmemb">The length of the line in bytes.</param>
        /// <param name="userdata">The GC handle of the owning <see cref="CurlTask"/>.</param>
        /// <returns>The line length to accept it, or 0 to abort the transfer.</returns>
        [MonoPInvokeCallback(typeof(CurlReadOrWriteCallback))]
        static unsafe UIntPtr OnHeaderReceived(IntPtr ptr, UIntPtr size, UIntPtr nmemb, IntPtr userdata)
        {
            if (!UnsafeHelper.TryGetInstanceFromGCHandle<CurlTask>(userdata, out var curlTask))
            {
                return UIntPtr.Zero;
            }
            var state = curlTask.State;
            // Trailers arrive after the first body byte has published the response, so HeaderRead is accepted too.
            if (state != CurlTaskState.Submitted && state != CurlTaskState.HeaderRead)
            {
                return UIntPtr.Zero;
            }
            var curlResponse = curlTask.Response;
            var length = (int)(size.ToUInt32() * nmemb.ToUInt32());
            // Only the worker thread runs this callback, so the counter needs no synchronization.
            var headersLength = curlResponse._currentHeadersLength + length;
            if (headersLength > curlTask.Config.MaxResponseHeadersLength)
            {
                return UIntPtr.Zero;
            }
            var line = new ReadOnlySpan<byte>((void*)ptr, length);
            if (state == CurlTaskState.Submitted)
            {
                curlTask.Record?.OnResponseHeaderLine(line);
            }
            TrimHttpHeader(ref line);
            if (line.Length == 0)
            {
                // A blank line ends a header block (an interim or redirect response, or the final response), and the
                // next block gets its own budget.
                curlResponse._currentHeadersLength = 0;
                return (UIntPtr)length;
            }
            curlResponse._currentHeadersLength = headersLength;
            // Trailers are only counted: Headers already belongs to the caller, and Unity's .NET profile has no TrailingHeaders.
            if (state == CurlTaskState.Submitted)
            {
                curlResponse.ParseHttpHeader(line);
            }

            return (UIntPtr)length;
        }
        static void TrimHttpHeader(ref ReadOnlySpan<byte> line)
        {
            while (line.Length > 0)
            {
                var last = line[line.Length - 1];
                if (last == '\r' || last == '\n' || last == ' ')
                {
                    line = line.Slice(0, line.Length - 1);
                }
                else
                {
                    break;
                }
            }
        }
        void ParseHttpHeader(ReadOnlySpan<byte> line)
        {
            if (line.Length == 0)
            {
                return;
            }
            var isHTTPHeader = line.Length >= 5
                && line[0] == 'H'
                && line[1] == 'T'
                && line[2] == 'T'
                && line[3] == 'P'
                && line[4] == '/';

            // HTTP status code parse
            if (isHTTPHeader)
            {
                var code = ParseStatusCode(line);
                if (code > 0)
                {
                    StatusCode = (HttpStatusCode)code;
                    Headers.Clear();
                    Content.Headers.Clear();
                }
                return;
            }

            // find ':'
            var colonIndex = -1;
            for (var i = 0; i < line.Length; i++)
            {
                if (line[i] == ':')
                {
                    colonIndex = i;
                    break;
                }
            }

            // Colon not found or at the start of the line, invalid header format
            if (colonIndex <= 0)
            {
                return;
            }

            // Header key and value extraction
            var keyBytes = TrimSpan(line.Slice(0, colonIndex));
            var valueBytes = TrimSpan(line.Slice(colonIndex + 1));

            // If the key is empty, skip this header
            if (keyBytes.Length == 0)
            {
                return;
            }

            var keyStr = GetKeyString(keyBytes);
            var valueStr = Encoding.UTF8.GetString(valueBytes);

            if (!Headers.TryAddWithoutValidation(keyStr, valueStr))
            {
                Content.Headers.TryAddWithoutValidation(keyStr, valueStr);
            }

            var cookieContainer = Config.CookieContainer;
            var requestUri = Request.RequestUri;
            if (cookieContainer != null)
            {
                if (string.Equals(keyStr, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        cookieContainer.SetCookies(requestUri, valueStr);
                    }
                    catch (CookieException)
                    {

                    }
                }
            }

            return;
        }
        static int ParseStatusCode(ReadOnlySpan<byte> line)
        {
            int firstSpace = -1;
            for (int i = 5; i < line.Length; i++)
            {
                if (line[i] == ' ')
                {
                    firstSpace = i;
                    break;
                }
            }

            if (firstSpace > 0 && firstSpace + 3 < line.Length)
            {
                var codeStart = firstSpace + 1;
                while (codeStart < line.Length && line[codeStart] == 32)
                {
                    codeStart++;
                }

                if (codeStart + 2 < line.Length)
                {
                    var d1 = line[codeStart];
                    var d2 = line[codeStart + 1];
                    var d3 = line[codeStart + 2];

                    if (d1 >= '0' && d1 <= '9' &&
                        d2 >= '0' && d2 <= '9' &&
                        d3 >= '0' && d3 <= '9')
                    {
                        return ((d1 - '0') * 100) + ((d2 - '0') * 10) + (d3 - '0');
                    }
                }
            }
            return -1;
        }
        static string GetKeyString(ReadOnlySpan<byte> keyBytes)
        {
            if (IsMatch(keyBytes, "Content-Type"))
            {
                return "Content-Type";
            }
            if (IsMatch(keyBytes, "Content-Length"))
            {
                return "Content-Length";
            }
            if (IsMatch(keyBytes, "Server"))
            {
                return "Server";
            }
            if (IsMatch(keyBytes, "Date"))
            {
                return "Date";
            }
            if (IsMatch(keyBytes, "Set-Cookie"))
            {
                return "Set-Cookie";
            }
            if (IsMatch(keyBytes, "Cache-Control"))
            {
                return "Cache-Control";
            }
            if (IsMatch(keyBytes, "Connection"))
            {
                return "Connection";
            }
            if (IsMatch(keyBytes, "Location"))
            {
                return "Location";
            }
            if (IsMatch(keyBytes, "Transfer-Encoding"))
            {
                return "Transfer-Encoding";
            }

            return Encoding.UTF8.GetString(keyBytes);
        }
        static bool IsMatch(ReadOnlySpan<byte> span, string knownKey)
        {
            if (span.Length != knownKey.Length)
            {
                return false;
            }

            for (var i = 0; i < span.Length; i++)
            {
                var b = span[i];
                var c = knownKey[i];

                if (b == (byte)c)
                {
                    continue;
                }
                if (b >= 'A' && b <= 'Z' && (b + 32) == c)
                {
                    continue;
                }
                if (b >= 'a' && b <= 'z' && (b - 32) == c)
                {
                    continue;
                }

                return false;
            }
            return true;
        }
        static ReadOnlySpan<byte> TrimSpan(ReadOnlySpan<byte> span)
        {
            var start = 0;
            while (start < span.Length && span[start] == 32)
            {
                start++;
            }

            var end = span.Length - 1;
            while (end >= start && span[end] == 32)
            {
                end--;
            }

            var length = end - start + 1;
            if (length <= 0)
            {
                return ReadOnlySpan<byte>.Empty;
            }
            return span.Slice(start, length);
        }
        /// <summary>
        /// Releases the body buffers at once and asks the owner to release the native transfer.
        /// </summary>
        /// <param name="disposing">Whether the call comes from <see cref="HttpResponseMessage.Dispose()"/>.</param>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                // Content may have been replaced by the caller, so the body stream this response owns is disposed explicitly.
                _responseStream.Dispose();
                _onDispose();
            }
        }
    }
}
