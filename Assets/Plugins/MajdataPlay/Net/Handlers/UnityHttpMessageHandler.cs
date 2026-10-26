using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Networking;
#nullable enable
namespace MajdataPlay.Net.Handlers
{
    public class UnityHttpMessageHandler : HttpMessageHandler
    {
        public int MaxRedirects { get; set; } = 32;


        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await using (UniTask.ReturnToCurrentSynchronizationContext(true, cancellationToken))
            {
                await UniTask.SwitchToMainThread(cancellationToken);
                var uwr = await CreateWebRequest(request);

                var operation = uwr.SendWebRequest();
                try
                {
                    await operation.WithCancellation(cancellationToken, true);
                }catch(UnityWebRequestException ex)
                {
                    var response_err = new HttpResponseMessage((HttpStatusCode)ex.UnityWebRequest.responseCode);
                    response_err.RequestMessage = request;
                    response_err.ReasonPhrase = ex.UnityWebRequest.error;
                    return response_err;
                }
                var response = new HttpResponseMessage((HttpStatusCode)uwr.responseCode);

                if (uwr.downloadHandler != null)
                {
                    response.Content = new DownloadHandlerHttpContent(uwr, uwr.downloadHandler);
                }

                foreach (var kv in uwr.GetResponseHeaders())
                {
                    // Content headers such as Content-Type are rejected by the response headers and belong to the content.
                    if (!response.Headers.TryAddWithoutValidation(kv.Key, kv.Value))
                    {
                        response.Content?.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                    }
                }

                response.RequestMessage = request;
                response.ReasonPhrase = uwr.error;
                return response;
            }
        }


        async Task<UnityWebRequest> CreateWebRequest(HttpRequestMessage request)
        {
            var uwr = default(UnityWebRequest);
            var method = request.Method.Method;

            if (request.Content != null)
            {
                // Size the upload from the body itself: Content-Length is absent for content of unknown length.
                var body = await request.Content.ReadAsByteArrayAsync();
                var buffer = new NativeArray<byte>(body.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                buffer.CopyFrom(body);

                uwr = new UnityWebRequest(request.RequestUri, method)
                {
                    uploadHandler = new UploadHandlerRaw(buffer, true),
                    downloadHandler = new DownloadHandlerBuffer()
                };
                foreach (var header in request.Content.Headers)
                {
                    foreach (var value in header.Value)
                    {
                        uwr.SetRequestHeader(header.Key, value);
                    }
                }
            }
            else
            {
                uwr = new UnityWebRequest(request.RequestUri, method)
                {
                    downloadHandler = new DownloadHandlerBuffer()
                };
            }

            uwr.redirectLimit = MaxRedirects;
            //uwr.timeout = (int)UnityWebRequestFactory.Timeout.TotalMilliseconds / 1000;
            //uwr.SetRequestHeader("User-Agent", UnityWebRequestFactory.UserAgent);

            foreach (var header in request.Headers)
            {
                foreach (var value in header.Value)
                {
                    uwr.SetRequestHeader(header.Key, value);
                }
            }

            return uwr;
        }
        class DownloadHandlerHttpContent : HttpContent, IDisposable
        {
            NativeArray<byte>.ReadOnly _data;

            readonly UnityWebRequest _uwr;
            readonly DownloadHandler _handler;            

            public DownloadHandlerHttpContent(UnityWebRequest uwr, DownloadHandler handler)
            {
                _uwr = uwr;
                _handler = handler;
                _data = handler.nativeData;

                Headers.ContentLength = _data.Length;
            }

            ~DownloadHandlerHttpContent()
            {
                Dispose();
            }


            protected override unsafe Task SerializeToStreamAsync(
                Stream stream,
                TransportContext? context)
            {
                if (!_data.IsCreated || _data.Length == 0)
                {
                    return Task.CompletedTask;
                }


                var memory = new UnmanagedMemoryStream(
                    (byte*)_data.GetUnsafeReadOnlyPtr(),
                    _data.Length);

                return memory.CopyToAsync(stream, 81920);             
            }

            protected override bool TryComputeLength(out long length)
            {
                length = _data.Length;
                return true;
            }

            public new void Dispose()
            {
                base.Dispose();
                _handler.Dispose();
                _uwr.Dispose();
                _data = default;
            }
        }
    }
}
