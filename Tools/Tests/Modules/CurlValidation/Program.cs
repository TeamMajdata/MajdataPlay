using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.Buffers;
using MajdataPlay.Diagnostics;
using MajdataPlay.Net.Curl;
using MajdataPlay.Net.Curl.Core;
using MajdataPlay.Net.Curl.Core.PInvoke;
using MajdataPlay.Net.Curl.Lifecycle;

/// <summary>
/// Runs the libcurl HTTP handler against loopback servers with the real bundled Windows x64 libcurl.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Byte written over every array returned to the tracking pool; never produced by <see cref="PatternByte"/>.
    /// </summary>
    public const byte PoisonByte = 0xFF;

    const int TimeoutMs = 5000;

    static int _assertions;

    static readonly (string Name, Action Check)[] s_checks =
    {
        (nameof(DisposingHttpClientStopsIdleWorker), DisposingHttpClientStopsIdleWorker),
        (nameof(SyncAndAsyncDisposalCanRunTogether), SyncAndAsyncDisposalCanRunTogether),
        (nameof(DisposingClientFailsRequestWaitingForHeaders), DisposingClientFailsRequestWaitingForHeaders),
        (nameof(ResponseCanBeDisposedAfterClientHasStopped), ResponseCanBeDisposedAfterClientHasStopped),
        (nameof(RequestsSubmittedDuringDisposalAlwaysFinish), RequestsSubmittedDuringDisposalAlwaysFinish),
        (nameof(CancellationRacingWithDisposalDoesNotLoseRequests), CancellationRacingWithDisposalDoesNotLoseRequests),
        (nameof(LargeBodyIsDeliveredIntact), LargeBodyIsDeliveredIntact),
        (nameof(SmallResponseBufferDoesNotStallBody), SmallResponseBufferDoesNotStallBody),
        (nameof(DisposingResponseDuringReadKeepsBuffersConsistent), DisposingResponseDuringReadKeepsBuffersConsistent),
        (nameof(DisposingClientDuringReadKeepsBuffersConsistent), DisposingClientDuringReadKeepsBuffersConsistent),
        (nameof(CompletedResponseReleasesNativeHandleOnDispose), CompletedResponseReleasesNativeHandleOnDispose),
        (nameof(CancelledRequestReleasesNativeHandle), CancelledRequestReleasesNativeHandle),
        (nameof(LongLivedTokenDoesNotRetainResponse), LongLivedTokenDoesNotRetainResponse),
        (nameof(ThrowingUploadStreamFailsRequest), ThrowingUploadStreamFailsRequest),
        (nameof(SslOptionsKeepNativeCaStore), SslOptionsKeepNativeCaStore),
        (nameof(BodyBufferIsSizedIndependentlyOfHeaderLimit), BodyBufferIsSizedIndependentlyOfHeaderLimit),
        (nameof(ChunkedTrailersDoNotAbortTransfer), ChunkedTrailersDoNotAbortTransfer),
        (nameof(HeaderLineIsCountedOnce), HeaderLineIsCountedOnce),
        (nameof(HeaderLimitResetsAfterRedirect), HeaderLimitResetsAfterRedirect),
        (nameof(HeaderLimitResetsAfterInterimResponse), HeaderLimitResetsAfterInterimResponse),
        (nameof(OversizedHeadersFailRequest), OversizedHeadersFailRequest),
    };

    /// <summary>
    /// Header limit used by the header checks; each response they serve has one 600-byte header.
    /// </summary>
    const int SmallHeaderLimit = 1024;

    /// <summary>
    /// Runs every check, or only the checks named on the command line.
    /// </summary>
    /// <param name="args">Optional check names to run.</param>
    /// <returns>0 when every selected check passed; otherwise 1.</returns>
    static int Main(string[] args)
    {
        var failures = 0;
        foreach (var (name, check) in s_checks)
        {
            if (args.Length != 0 && !args.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            var errorsBefore = MajDebug.ErrorCount;
            var elapsed = Stopwatch.StartNew();
            try
            {
                check();
                Check(MajDebug.ErrorCount == errorsBefore, $"The curl worker logged an error: {MajDebug.LastError}");
                Check(TrackingArrayPool<byte>.Instance.DoubleReturns == 0, "A pooled buffer was returned twice.");
                Console.WriteLine($"PASS {name} ({elapsed.ElapsedMilliseconds} ms)");
            }
            catch (Exception exception)
            {
                failures++;
                Console.WriteLine($"FAIL {name} ({elapsed.ElapsedMilliseconds} ms): {exception}");
            }
        }
        if (failures != 0)
        {
            Console.WriteLine($"CURL_VALIDATION_FAILED ({failures} failed, {_assertions} assertions)");
            return 1;
        }
        Console.WriteLine($"CURL_VALIDATION_PASSED ({_assertions} assertions)");
        return 0;
    }

    static void DisposingHttpClientStopsIdleWorker()
    {
        var handler = new CurlHttpMessageHandler();
        using var client = new HttpClient(handler);
        var multi = GetMulti(handler);
        var worker = GetWorker(multi);

        var elapsed = Stopwatch.StartNew();
        client.Dispose();

        Check(worker.IsCompleted, "HTTP client must stop its owned worker.");
        Check(elapsed.ElapsedMilliseconds < 2000, "Stopping an idle worker must not wait for the poll timeout.");
        Check(!multi.IsAllocated, "The multi handle must be released.");
        client.Dispose();
        handler.Dispose();
    }

    static void SyncAndAsyncDisposalCanRunTogether()
    {
        using var handler = new CurlHttpMessageHandler();
        var multi = GetMulti(handler);
        var syncDispose = Task.Run(() => multi.Dispose());
        var asyncDispose = multi.DisposeAsync().AsTask();

        Check(Task.WaitAll(new[] { syncDispose, asyncDispose }, TimeoutMs), "Concurrent disposal must finish.");
        Check(GetWorker(multi).IsCompleted, "The worker must stop.");
        Check(!multi.IsAllocated, "The multi handle must be released.");
        multi.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    static void DisposingClientFailsRequestWaitingForHeaders()
    {
        using var server = LoopbackServer.HoldOpen(out var accepted);
        using var handler = new CurlHttpMessageHandler();
        using var client = new HttpClient(handler);
        var request = Task.Run(() => client.GetAsync(server.Url));
        WaitFor(accepted.Task, request, "Request must reach the local server.");

        client.Dispose();

        Check(GetWorker(GetMulti(handler)).IsCompleted, "The worker must stop.");
        Check(Task.WhenAny(request, Task.Delay(TimeoutMs)).Result == request, "The pending request must finish.");
        Check(request.IsFaulted || request.IsCanceled, "The pending request must fail.");
        _ = request.Exception;
    }

    static void ResponseCanBeDisposedAfterClientHasStopped()
    {
        using var server = LoopbackServer.FixedBody(Encoding.ASCII.GetBytes("OK"));
        using var handler = new CurlHttpMessageHandler();
        using var client = new HttpClient(handler);
        var request = Task.Run(() => client.GetAsync(server.Url, HttpCompletionOption.ResponseHeadersRead));
        Check(request.Wait(TimeoutMs), "The response headers must arrive.");
        using var response = request.Result;
        var body = Task.Run(() => response.Content.ReadAsStringAsync());
        Check(body.Wait(TimeoutMs), "The body must arrive.");
        Check(body.Result == "OK", $"Unexpected body '{body.Result}'.");

        client.Dispose();

        Check(GetWorker(GetMulti(handler)).IsCompleted, "The worker must stop.");
        response.Dispose();
        response.Dispose();
    }

    static void RequestsSubmittedDuringDisposalAlwaysFinish()
    {
        using var server = LoopbackServer.HoldOpen(out _);
        for (var iteration = 0; iteration < 10; iteration++)
        {
            using var handler = new CurlHttpMessageHandler();
            using var client = new HttpClient(handler);
            var requests = new Task[8];
            for (var i = 0; i < requests.Length; i++)
            {
                requests[i] = Task.Run(async () =>
                {
                    try
                    {
                        using var response = await client.GetAsync(server.Url).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is ObjectDisposedException
                        or OperationCanceledException
                        or HttpRequestException)
                    {
                        // Closing a client is expected to abort or reject these requests.
                    }
                });
            }
            client.Dispose();
            Check(Task.WaitAll(requests, TimeoutMs), "Every request must finish after disposal.");
            Check(GetWorker(GetMulti(handler)).IsCompleted, "The worker must stop.");
        }
    }

    static void CancellationRacingWithDisposalDoesNotLoseRequests()
    {
        using var server = LoopbackServer.HoldOpen(out _);
        for (var iteration = 0; iteration < 20; iteration++)
        {
            using var handler = new CurlHttpMessageHandler();
            using var client = new HttpClient(handler);
            using var cancellation = new CancellationTokenSource();
            var requests = new Task<HttpResponseMessage>[8];
            for (var i = 0; i < requests.Length; i++)
            {
                requests[i] = Task.Run(() => client.GetAsync(server.Url, cancellation.Token));
            }
            var cancel = Task.Run(() => cancellation.Cancel());
            var dispose = Task.Run(() => handler.Dispose());
            Check(Task.WaitAll(new[] { cancel, dispose }, TimeoutMs), "Cancellation and disposal must finish.");
            foreach (var request in requests)
            {
                Check(Task.WhenAny(request, Task.Delay(TimeoutMs)).Result == request, "Every request must finish.");
                _ = request.Exception;
                if (request.Status == TaskStatus.RanToCompletion)
                {
                    request.Result.Dispose();
                }
            }
            Check(GetWorker(GetMulti(handler)).IsCompleted, "The worker must stop.");
        }
    }

    static void LargeBodyIsDeliveredIntact()
    {
        const int Length = 8 * 1024 * 1024;
        using var server = LoopbackServer.PatternBody(Length);
        using var handler = new CurlHttpMessageHandler();
        using var client = new HttpClient(handler);
        var body = Task.Run(() => client.GetByteArrayAsync(server.Url));
        Check(body.Wait(TimeoutMs * 2), "The large body must arrive.");
        CheckPattern(body.Result, 0, Length);
    }

    static void SmallResponseBufferDoesNotStallBody()
    {
        // A single libcurl write larger than the high-water mark must not pause a transfer whose buffer is empty, or no
        // read would ever resume it.
        const int Length = 256 * 1024;
        using var server = LoopbackServer.PatternBody(Length);
        using var handler = new CurlHttpMessageHandler { ResponseBodyBufferSize = 512 };
        using var client = new HttpClient(handler);
        var body = Task.Run(() => client.GetByteArrayAsync(server.Url));
        Check(Task.WhenAny(body, Task.Delay(TimeoutMs)).Result == body, "The transfer stalled with an empty buffer.");
        CheckPattern(body.Result, 0, Length);
    }

    static void DisposingResponseDuringReadKeepsBuffersConsistent()
    {
        RunReadDisposalRace(disposeClient: false);
    }

    static void DisposingClientDuringReadKeepsBuffersConsistent()
    {
        RunReadDisposalRace(disposeClient: true);
    }

    static void RunReadDisposalRace(bool disposeClient)
    {
        using var server = LoopbackServer.PatternBody(long.MaxValue);
        var random = new Random(1234);
        for (var iteration = 0; iteration < 40; iteration++)
        {
            var handler = new CurlHttpMessageHandler();
            var client = new HttpClient(handler);
            var request = Task.Run(() => client.GetAsync(server.Url, HttpCompletionOption.ResponseHeadersRead));
            Check(request.Wait(TimeoutMs), "The response headers must arrive.");
            var response = request.Result;
            var stream = response.Content.ReadAsStream();
            using var reading = new ManualResetEventSlim();
            var reader = Task.Run(() =>
            {
                var buffer = new byte[8 * 1024];
                var position = 0L;
                reading.Set();
                while (true)
                {
                    var read = stream.Read(buffer, 0, buffer.Length);
                    if (read == 0)
                    {
                        return;
                    }
                    CheckPattern(buffer, position, read);
                    position += read;
                }
            });
            Check(reading.Wait(TimeoutMs), "The reader must start.");
            Thread.Sleep(random.Next(0, 15));
            if (disposeClient)
            {
                client.Dispose();
            }
            else
            {
                response.Dispose();
            }
            Check(Task.WhenAny(reader, Task.Delay(TimeoutMs)).Result == reader, "A blocked reader must be released.");
            if (reader.IsFaulted)
            {
                var error = reader.Exception!.GetBaseException();
                Check(error is ObjectDisposedException or IOException or OperationCanceledException,
                    $"Unexpected reader failure: {error}");
            }
            response.Dispose();
            client.Dispose();
        }
    }

    static void CompletedResponseReleasesNativeHandleOnDispose()
    {
        using var server = LoopbackServer.FixedBody(Encoding.ASCII.GetBytes("OK"));
        using var handler = new CurlHttpMessageHandler();
        using var client = new HttpClient(handler);
        var baseline = GetSettledLibCurlReferenceCount();
        // ResponseContentRead would let HttpClient dispose the body stream (and so the handle) after buffering it.
        var request = Task.Run(() => client.GetAsync(server.Url, HttpCompletionOption.ResponseHeadersRead));
        Check(request.Wait(TimeoutMs), "The response must arrive.");
        var response = request.Result;
        var body = response.Content.ReadAsStream();
        var buffer = new byte[16];
        var length = 0;
        for (var read = body.Read(buffer, 0, buffer.Length); read != 0; read = body.Read(buffer, length, buffer.Length - length))
        {
            length += read;
        }
        Check(Encoding.ASCII.GetString(buffer, 0, length) == "OK", "The body must arrive.");
        Check(GetLibCurlReferenceCount() == baseline + 1, "A live response must own its easy handle.");

        response.Dispose();

        Check(GetLibCurlReferenceCount() == baseline, "Disposing a completed response must release its easy handle at once.");
    }

    static void CancelledRequestReleasesNativeHandle()
    {
        using var server = LoopbackServer.HoldOpen(out var accepted);
        using var handler = new CurlHttpMessageHandler();
        using var client = new HttpClient(handler);
        var baseline = GetSettledLibCurlReferenceCount();
        using var cancellation = new CancellationTokenSource();
        var request = Task.Run(() => client.GetAsync(server.Url, cancellation.Token));
        WaitFor(accepted.Task, request, "Request must reach the local server.");

        cancellation.Cancel();

        Check(Task.WhenAny(request, Task.Delay(TimeoutMs)).Result == request, "The cancelled request must finish.");
        Check(request.IsCanceled, $"The request must be cancelled, but was {request.Status}.");
        // The response was never handed out, so nobody else can release the easy handle.
        Check(SpinWait.SpinUntil(() => GetLibCurlReferenceCount() == baseline, TimeoutMs),
            "A cancelled request must release its easy handle without waiting for the finalizer.");
    }

    static void LongLivedTokenDoesNotRetainResponse()
    {
        using var server = LoopbackServer.FixedBody(Encoding.ASCII.GetBytes("OK"));
        using var handler = new CurlHttpMessageHandler();
        // HttpClient links the caller's token into a short-lived source, which would hide the leak; call the handler directly.
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        using var applicationLifetime = new CancellationTokenSource();

        var response = SendAndDispose(invoker, server.Url, applicationLifetime.Token);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Check(!response.IsAlive, "A completed request must not stay registered on a long-lived cancellation token.");
        GC.KeepAlive(applicationLifetime);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference SendAndDispose(HttpMessageInvoker invoker, string url, CancellationToken token)
    {
        var request = Task.Run(() => invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), token));
        Check(request.Wait(TimeoutMs), "The response must arrive.");
        var response = request.Result;
        response.Dispose();
        return new WeakReference(response);
    }

    static void ThrowingUploadStreamFailsRequest()
    {
        using var server = LoopbackServer.FixedBody(Encoding.ASCII.GetBytes("OK"));
        using var handler = new CurlHttpMessageHandler();
        using var client = new HttpClient(handler);
        using var content = new StreamContent(new ThrowingStream());
        content.Headers.ContentLength = 16;
        var request = Task.Run(() => client.PutAsync(server.Url, content));

        Check(Task.WhenAny(request, Task.Delay(TimeoutMs)).Result == request, "The upload must finish.");
        Check(request.IsFaulted, $"A failing upload stream must fail the request, but it was {request.Status}.");
        _ = request.Exception;
    }

    static void SslOptionsKeepNativeCaStore()
    {
        // CURLOPT_SSL_OPTIONS cannot be read back from libcurl, and telling the two values apart over the wire needs an
        // HTTPS server whose root is in the Windows certificate store. The check therefore covers the bitmask the
        // handler sets in its single SetOption call.
        var withoutRevocation = new CurlHttpConfig { CheckCertificateRevocationList = false }.GetSslOptions();
        var withRevocation = new CurlHttpConfig { CheckCertificateRevocationList = true }.GetSslOptions();

        Check(withoutRevocation == (CurlSslOption.NativeCa | CurlSslOption.NoRevoke), $"Unexpected SSL options {withoutRevocation}.");
        Check(withRevocation == CurlSslOption.NativeCa, $"Unexpected SSL options {withRevocation}.");
    }

    static void BodyBufferIsSizedIndependentlyOfHeaderLimit()
    {
        const int BufferSize = 1024 * 1024;
        using var server = LoopbackServer.FixedBody(Encoding.ASCII.GetBytes("OK"));
        using var handler = new CurlHttpMessageHandler
        {
            MaxResponseHeadersLength = SmallHeaderLimit,
            ResponseBodyBufferSize = BufferSize,
        };
        using var client = new HttpClient(handler);
        var request = Task.Run(() => client.GetAsync(server.Url, HttpCompletionOption.ResponseHeadersRead));
        Check(request.Wait(TimeoutMs), "The response must arrive.");
        using var response = request.Result;

        var stream = typeof(CurlResponse)
            .GetField("_responseStream", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(response)!;
        var highWaterMark = (int)stream.GetType()
            .GetField("_hwmBytes", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(stream)!;
        Check(highWaterMark == (int)(BufferSize * 0.8), $"The body buffer must follow ResponseBodyBufferSize, but pauses at {highWaterMark} bytes.");
    }

    static void ChunkedTrailersDoNotAbortTransfer()
    {
        using var server = LoopbackServer.Scripted(async (stream, target, stop) =>
        {
            var head = "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nTrailer: X-Checksum\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head + "5\r\nhello\r\n"), stop).ConfigureAwait(false);
            // Let libcurl deliver the body first, so the trailers arrive after the response has been published.
            await Task.Delay(100, stop).ConfigureAwait(false);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("0\r\nX-Checksum: 5d41402a\r\n\r\n"), stop).ConfigureAwait(false);
        });
        using var handler = new CurlHttpMessageHandler();
        using var client = new HttpClient(handler);

        var body = Task.Run(() => client.GetStringAsync(server.Url));

        Check(Task.WhenAny(body, Task.Delay(TimeoutMs)).Result == body, "The chunked response must finish.");
        Check(body.Status == TaskStatus.RanToCompletion, $"Trailers must not abort the transfer: {body.Exception?.GetBaseException()}");
        Check(body.Result == "hello", $"Unexpected body '{body.Result}'.");
    }

    static void HeaderLineIsCountedOnce()
    {
        // About 660 header bytes in total, but counting the 600-byte line twice would exceed the 1024-byte limit.
        using var server = LoopbackServer.Scripted((stream, target, stop) => WriteAsync(stream, stop, OkResponse()));

        var response = GetWithHeaderLimit(server.Url);

        Check(response.Status == TaskStatus.RanToCompletion, $"Headers within the limit must be accepted: {response.Exception?.GetBaseException()}");
        Check(response.Result == "OK", $"Unexpected body '{response.Result}'.");
    }

    static void HeaderLimitResetsAfterRedirect()
    {
        // Each response is within the limit; together they are not.
        using var server = LoopbackServer.Scripted((stream, target, stop) =>
        {
            if (target == "/final")
            {
                return WriteAsync(stream, stop, OkResponse());
            }
            return WriteAsync(stream, stop, $"HTTP/1.1 302 Found\r\nLocation: /final\r\n{PadHeader()}Content-Length: 0\r\nConnection: close\r\n\r\n");
        });

        var response = GetWithHeaderLimit(server.Url);

        Check(response.Status == TaskStatus.RanToCompletion, $"Each redirect response must get its own header budget: {response.Exception?.GetBaseException()}");
        Check(response.Result == "OK", $"Unexpected body '{response.Result}'.");
    }

    static void HeaderLimitResetsAfterInterimResponse()
    {
        using var server = LoopbackServer.Scripted((stream, target, stop) =>
            WriteAsync(stream, stop, $"HTTP/1.1 100 Continue\r\n{PadHeader()}\r\n" + OkResponse()));

        var response = GetWithHeaderLimit(server.Url);

        Check(response.Status == TaskStatus.RanToCompletion, $"A 1xx response must not use up the final response's header budget: {response.Exception?.GetBaseException()}");
        Check(response.Result == "OK", $"Unexpected body '{response.Result}'.");
    }

    static void OversizedHeadersFailRequest()
    {
        using var server = LoopbackServer.Scripted((stream, target, stop) =>
            WriteAsync(stream, stop, $"HTTP/1.1 200 OK\r\n{PadHeader(SmallHeaderLimit + 1)}Content-Length: 2\r\nConnection: close\r\n\r\nOK"));

        var response = GetWithHeaderLimit(server.Url);

        Check(response.IsFaulted, $"Headers over the limit must fail the request, but it was {response.Status}.");
        _ = response.Exception;
    }

    /// <summary>
    /// Requests <paramref name="url"/> with a <see cref="SmallHeaderLimit"/> header limit and waits for the body.
    /// </summary>
    /// <param name="url">The loopback URL to request.</param>
    /// <returns>The finished body task.</returns>
    static Task<string> GetWithHeaderLimit(string url)
    {
        using var handler = new CurlHttpMessageHandler { MaxResponseHeadersLength = SmallHeaderLimit };
        using var client = new HttpClient(handler);
        var body = Task.Run(() => client.GetStringAsync(url));
        Check(Task.WhenAny(body, Task.Delay(TimeoutMs)).Result == body, "The request must finish.");
        return body;
    }

    /// <summary>
    /// Builds a 200 response with a body of "OK" and one <see cref="PadHeader"/>.
    /// </summary>
    static string OkResponse()
    {
        return $"HTTP/1.1 200 OK\r\n{PadHeader()}Content-Length: 2\r\nConnection: close\r\n\r\nOK";
    }

    /// <summary>
    /// Builds a filler header line of exactly <paramref name="length"/> bytes, including its CRLF.
    /// </summary>
    /// <param name="length">The total length of the line.</param>
    static string PadHeader(int length = 600)
    {
        const string Prefix = "X-Pad: ";
        return Prefix + new string('a', length - Prefix.Length - 2) + "\r\n";
    }

    static async Task WriteAsync(NetworkStream stream, CancellationToken stop, string response)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes(response), stop).ConfigureAwait(false);
    }

    static void CheckPattern(byte[] buffer, long position, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (buffer[i] != PatternByte(position + i))
            {
                throw new InvalidOperationException($"Corrupted body byte at {position + i}: {buffer[i]:X2}.");
            }
        }
    }

    /// <summary>
    /// Gets the body byte served at <paramref name="position"/>; never equal to <see cref="PoisonByte"/>.
    /// </summary>
    /// <param name="position">The absolute body offset.</param>
    /// <returns>The expected byte.</returns>
    public static byte PatternByte(long position)
    {
        return (byte)(position % 251);
    }

    static void WaitFor(Task expected, Task request, string message)
    {
        var first = Task.WhenAny(expected, request, Task.Delay(TimeoutMs)).Result;
        Check(first == expected, request.Exception?.ToString() ?? message);
    }

    static void Check(bool condition, string message)
    {
        Interlocked.Increment(ref _assertions);
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>
    /// Gets the libcurl reference count after finalizers of handles leaked by earlier checks have run.
    /// </summary>
    static int GetSettledLibCurlReferenceCount()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GetLibCurlReferenceCount();
    }

    static int GetLibCurlReferenceCount()
    {
        return (int)typeof(LibCurlLifecycle)
            .GetField("s_refCount", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
    }

    static CurlMulti GetMulti(CurlHttpMessageHandler handler)
    {
        return (CurlMulti)typeof(CurlHttpMessageHandler)
            .GetField("_curlMulti", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(handler)!;
    }

    static Task GetWorker(CurlMulti multi)
    {
        return (Task)typeof(CurlMulti)
            .GetField("_workerThread", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(multi)!;
    }

    /// <summary>
    /// Upload body whose reads always fail, as a disposed or broken request stream would.
    /// </summary>
    sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush()
        {
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new IOException("The upload source failed.");
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Minimal HTTP/1.1 loopback server; each accepted connection serves one response and then closes.
    /// </summary>
    sealed class LoopbackServer : IDisposable
    {
        readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        readonly CancellationTokenSource _stop = new();
        readonly Func<NetworkStream, string, CancellationToken, Task> _respond;

        LoopbackServer(Func<NetworkStream, string, CancellationToken, Task> respond)
        {
            _respond = respond;
            _listener.Start();
            _ = AcceptLoopAsync();
        }

        public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";

        /// <summary>
        /// Accepts connections, signals the first one, and never answers.
        /// </summary>
        public static LoopbackServer HoldOpen(out TaskCompletionSource accepted)
        {
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            accepted = signal;
            return new LoopbackServer(async (stream, target, stop) =>
            {
                signal.TrySetResult();
                await Task.Delay(Timeout.Infinite, stop).ConfigureAwait(false);
            });
        }

        /// <summary>
        /// Answers every request with a fixed body.
        /// </summary>
        public static LoopbackServer FixedBody(byte[] body)
        {
            return new LoopbackServer(async (stream, target, stop) =>
            {
                var head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head, stop).ConfigureAwait(false);
                await stream.WriteAsync(body, stop).ConfigureAwait(false);
            });
        }

        /// <summary>
        /// Answers every request with <paramref name="length"/> bytes of <see cref="PatternByte"/>, streamed until the
        /// client goes away.
        /// </summary>
        public static LoopbackServer PatternBody(long length)
        {
            return new LoopbackServer(async (stream, target, stop) =>
            {
                var lengthHeader = length == long.MaxValue ? string.Empty : $"Content-Length: {length}\r\n";
                var head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\n{lengthHeader}Connection: close\r\n\r\n");
                await stream.WriteAsync(head, stop).ConfigureAwait(false);
                var block = new byte[251 * 64];
                for (var i = 0; i < block.Length; i++)
                {
                    block[i] = PatternByte(i);
                }
                for (var sent = 0L; sent < length;)
                {
                    var count = (int)Math.Min(block.Length, length - sent);
                    await stream.WriteAsync(block.AsMemory(0, count), stop).ConfigureAwait(false);
                    sent += count;
                }
            });
        }

        /// <summary>
        /// Answers every request with <paramref name="respond"/>, which receives the request target.
        /// </summary>
        public static LoopbackServer Scripted(Func<NetworkStream, string, CancellationToken, Task> respond)
        {
            return new LoopbackServer(respond);
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
        }

        async Task AcceptLoopAsync()
        {
            while (true)
            {
                TcpClient connection;
                try
                {
                    connection = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }
                _ = ServeAsync(connection);
            }
        }

        async Task ServeAsync(TcpClient connection)
        {
            using (connection)
            {
                try
                {
                    var stream = connection.GetStream();
                    var target = await ReadRequestAsync(stream, _stop.Token).ConfigureAwait(false);
                    await _respond(stream, target, _stop.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Clients routinely abort; the server only has to keep running.
                }
            }
        }

        /// <summary>
        /// Reads one request, including its body, and returns its request target (for example "/final").
        /// </summary>
        static async Task<string> ReadRequestAsync(NetworkStream stream, CancellationToken stop)
        {
            var head = new List<byte>();
            var buffer = new byte[1];
            while (head.Count < 4 || head[^4] != '\r' || head[^3] != '\n' || head[^2] != '\r' || head[^1] != '\n')
            {
                if (await stream.ReadAsync(buffer, stop).ConfigureAwait(false) == 0)
                {
                    return string.Empty;
                }
                head.Add(buffer[0]);
            }
            var text = Encoding.ASCII.GetString(head.ToArray());
            var requestLine = text.Substring(0, text.IndexOf("\r\n", StringComparison.Ordinal)).Split(' ');
            var target = requestLine.Length > 1 ? requestLine[1] : string.Empty;
            var lengthLine = text.Split("\r\n").FirstOrDefault(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
            if (lengthLine is not null)
            {
                var remaining = long.Parse(lengthLine.Substring("Content-Length:".Length).Trim());
                var body = new byte[4096];
                while (remaining > 0)
                {
                    var read = await stream.ReadAsync(body.AsMemory(0, (int)Math.Min(body.Length, remaining)), stop).ConfigureAwait(false);
                    if (read == 0)
                    {
                        return target;
                    }
                    remaining -= read;
                }
            }
            return target;
        }
    }
}
