using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO;
using MajdataPlay.Net.Curl;
using ManagedBass;

/// <summary>
/// Streams audio from loopback HTTP servers into the real bundled Windows x64 BASS through <see cref="BassHttpFileSource"/>
/// and the project's libcurl HTTP handler.
/// </summary>
internal static class Program
{
    const int TimeoutMs = 5000;
    const int SampleRate = 44100;
    const int ChannelCount = 2;
    const int BytesPerFrame = ChannelCount * sizeof(short);
    const int WavHeaderLength = 44;

    /// <summary>
    /// Two seconds of 16-bit stereo PCM that every server serves inside a WAV container.
    /// </summary>
    static readonly byte[] s_pcm = CreatePcm(SampleRate * 2);

    /// <summary>
    /// <see cref="s_pcm"/> wrapped in a WAV header.
    /// </summary>
    static readonly byte[] s_wav = CreateWav(s_pcm);

    static int s_assertions;

    static readonly (string Name, Action Check)[] s_checks =
    {
        (nameof(KnownLengthBodyDecodesIntact), KnownLengthBodyDecodesIntact),
        (nameof(ChunkedBodyDecodesIntact), ChunkedBodyDecodesIntact),
        (nameof(CompressedBodyDecodesIntact), CompressedBodyDecodesIntact),
        (nameof(ErrorStatusThrowsHttpRequestException), ErrorStatusThrowsHttpRequestException),
        (nameof(UnsupportedFormatThrowsBassException), UnsupportedFormatThrowsBassException),
        (nameof(TruncatedBodyDuringCreationThrowsIOException), TruncatedBodyDuringCreationThrowsIOException),
        (nameof(CancelledTokenSendsNoRequest), CancelledTokenSendsNoRequest),
        (nameof(CancellationWhileWaitingForHeadersClosesConnection), CancellationWhileWaitingForHeadersClosesConnection),
        (nameof(CancellationWhileBassReadsHeaderClosesConnection), CancellationWhileBassReadsHeaderClosesConnection),
        (nameof(CancellationAfterCreationDoesNotAffectStream), CancellationAfterCreationDoesNotAffectStream),
        (nameof(FreeingStreamDuringStalledDownloadClosesConnection), FreeingStreamDuringStalledDownloadClosesConnection),
        (nameof(ConcurrentCreateAndFreeDoesNotLeak), ConcurrentCreateAndFreeDoesNotLeak),
    };

    /// <summary>
    /// Runs every check, or only the checks named on the command line.
    /// </summary>
    /// <param name="args">Optional check names to run.</param>
    /// <returns>0 when every selected check passed; otherwise 1.</returns>
    static int Main(string[] args)
    {
        // Device 0 is BASS's "no sound" device, so only decoding channels are created.
        if (!Bass.Init(0, SampleRate))
        {
            Console.WriteLine($"BASS_HTTP_VALIDATION_FAILED (Bass.Init failed: {Bass.LastError})");
            return 1;
        }
        Console.WriteLine($"BASS {Bass.Version}");
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
                Check(WaitUntil(() => BassHttpFileSource.ActiveSourceCount == 0),
                    $"{BassHttpFileSource.ActiveSourceCount} sources are still registered after the check.");
                Check(MajDebug.ErrorCount == errorsBefore, $"An error was logged: {MajDebug.LastError}");
                Console.WriteLine($"PASS {name} ({elapsed.ElapsedMilliseconds} ms)");
            }
            catch (Exception exception)
            {
                failures++;
                Console.WriteLine($"FAIL {name} ({elapsed.ElapsedMilliseconds} ms): {exception}");
            }
        }
        // A failed check can leave a stream whose download thread never returns; do not let it hang the process.
        if (!Task.Run(Bass.Free).Wait(TimeoutMs))
        {
            failures++;
            Console.WriteLine("FAIL Bass.Free did not return.");
        }
        if (failures != 0)
        {
            Console.WriteLine($"BASS_HTTP_VALIDATION_FAILED ({failures} failed, {s_assertions} assertions)");
            return 1;
        }
        Console.WriteLine($"BASS_HTTP_VALIDATION_PASSED ({s_assertions} assertions)");
        return 0;
    }

    static void KnownLengthBodyDecodesIntact()
    {
        using var server = new LoopbackServer(async (stream, token) =>
        {
            await WriteHeadAsync(stream, "200 OK", token, "Content-Type: audio/wav", $"Content-Length: {s_wav.Length}");
            await stream.WriteAsync(s_wav, token);
        });
        using var client = CreateClient();

        var handle = CreateStream(client, server.Uri, CancellationToken.None);
        try
        {
            Check(Bass.ChannelGetLength(handle) == s_pcm.Length, "The stream length must come from the WAV header.");
            Check(DecodeAll(handle).AsSpan().SequenceEqual(s_pcm), "The decoded PCM must match the served PCM.");
            Check(WaitUntil(() => BassHttpFileSource.ActiveSourceCount == 0),
                "BASS must close the source once the download reaches the end of the body.");
        }
        finally
        {
            Bass.StreamFree(handle);
        }
    }

    static void ChunkedBodyDecodesIntact()
    {
        using var server = new LoopbackServer(async (stream, token) =>
        {
            await WriteHeadAsync(stream, "200 OK", token, "Content-Type: audio/wav", "Transfer-Encoding: chunked");
            for (var offset = 0; offset < s_wav.Length; offset += 4096)
            {
                var length = Math.Min(4096, s_wav.Length - offset);
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"{length:X}\r\n"), token);
                await stream.WriteAsync(s_wav.AsMemory(offset, length), token);
                await stream.WriteAsync("\r\n"u8.ToArray(), token);
            }
            await stream.WriteAsync("0\r\n\r\n"u8.ToArray(), token);
        });
        using var client = CreateClient();

        var handle = CreateStream(client, server.Uri, CancellationToken.None);
        try
        {
            Check(DecodeAll(handle).AsSpan().SequenceEqual(s_pcm), "A body of unknown length must decode intact.");
        }
        finally
        {
            Bass.StreamFree(handle);
        }
    }

    static void CompressedBodyDecodesIntact()
    {
        var compressed = Gzip(s_wav);
        Check(compressed.Length < s_wav.Length, "The fixture must shrink when compressed.");
        using var server = new LoopbackServer(async (stream, token) =>
        {
            await WriteHeadAsync(stream, "200 OK", token, "Content-Type: audio/wav", "Content-Encoding: gzip",
                $"Content-Length: {compressed.Length}");
            await stream.WriteAsync(compressed, token);
        });
        using var client = CreateClient();

        // The handler decompresses, so the encoded Content-Length must not be reported to BASS as the file length.
        var handle = CreateStream(client, server.Uri, CancellationToken.None);
        try
        {
            Check(DecodeAll(handle).AsSpan().SequenceEqual(s_pcm), "A decompressed body must decode intact.");
        }
        finally
        {
            Bass.StreamFree(handle);
        }
    }

    static void ErrorStatusThrowsHttpRequestException()
    {
        using var server = new LoopbackServer(async (stream, token) =>
        {
            await WriteHeadAsync(stream, "404 Not Found", token, "Content-Length: 0");
        });
        using var client = CreateClient();

        var error = CaptureCreateFailure(client, server.Uri, CancellationToken.None);
        Check(error is HttpRequestException, $"An error status must throw HttpRequestException, not {error.GetType()}.");
    }

    static void UnsupportedFormatThrowsBassException()
    {
        var garbage = new byte[64 * 1024];
        garbage.AsSpan().Fill(0x5A);
        using var server = new LoopbackServer(async (stream, token) =>
        {
            await WriteHeadAsync(stream, "200 OK", token, $"Content-Length: {garbage.Length}");
            await stream.WriteAsync(garbage, token);
        });
        using var client = CreateClient();

        var error = CaptureCreateFailure(client, server.Uri, CancellationToken.None);
        Check(error is BassException { ErrorCode: Errors.FileFormat },
            $"An unsupported body must throw BassException(FileFormat), not {error}.");
    }

    static void TruncatedBodyDuringCreationThrowsIOException()
    {
        using var server = new LoopbackServer(async (stream, token) =>
        {
            await WriteHeadAsync(stream, "200 OK", token, $"Content-Length: {s_wav.Length}");
            await stream.WriteAsync(s_wav.AsMemory(0, 16), token);
            // Returning closes the connection while the client still expects most of the body.
        });
        using var client = CreateClient();
        var warningsBefore = MajDebug.WarningCount;

        var error = CaptureCreateFailure(client, server.Uri, CancellationToken.None);
        Check(error is IOException { InnerException: not null },
            $"A body that fails before BASS identifies the format must throw IOException, not {error}.");
        Check(MajDebug.WarningCount > warningsBefore, "The body failure must be logged as a warning.");
    }

    static void CancelledTokenSendsNoRequest()
    {
        using var server = new LoopbackServer(async (stream, token) =>
        {
            await WriteHeadAsync(stream, "200 OK", token, $"Content-Length: {s_wav.Length}");
            await stream.WriteAsync(s_wav, token);
        });
        using var client = CreateClient();

        var error = CaptureCreateFailure(client, server.Uri, new CancellationToken(true));
        Check(error is OperationCanceledException, $"A cancelled token must throw OperationCanceledException, not {error}.");
        Thread.Sleep(100);
        Check(server.RequestCount == 0, "A cancelled token must not send a request.");
    }

    static void CancellationWhileWaitingForHeadersClosesConnection()
    {
        var closed = NewSignal();
        using var server = new LoopbackServer(async (stream, token) =>
        {
            await WaitForClientCloseAsync(stream, token);
            closed.TrySetResult();
        });
        using var client = CreateClient();
        using var cts = new CancellationTokenSource();

        var creation = StartCreate(client, server.Uri, cts.Token);
        Check(WaitUntil(() => server.RequestCount == 1), "The request must reach the server.");
        cts.Cancel();
        var error = WaitForFailure(creation);
        Check(error is OperationCanceledException, $"Cancelling before the headers must throw OperationCanceledException, not {error}.");
        Check(closed.Task.Wait(TimeoutMs), "Cancelling before the headers must close the connection.");
    }

    static void CancellationWhileBassReadsHeaderClosesConnection()
    {
        var bodyStarted = NewSignal();
        var closed = NewSignal();
        using var server = new LoopbackServer(async (stream, token) =>
        {
            await WriteHeadAsync(stream, "200 OK", token, $"Content-Length: {s_wav.Length}");
            // Too little for BASS to identify the format, so creation blocks in the read callback.
            await stream.WriteAsync(s_wav.AsMemory(0, 16), token);
            bodyStarted.TrySetResult();
            await WaitForClientCloseAsync(stream, token);
            closed.TrySetResult();
        });
        using var client = CreateClient();
        using var cts = new CancellationTokenSource();

        var creation = StartCreate(client, server.Uri, cts.Token);
        Check(bodyStarted.Task.Wait(TimeoutMs), "The server must start the body.");
        Thread.Sleep(200);
        Check(!creation.IsCompleted, $"Creation must still be waiting for the body, but it finished: {creation.Status}.");
        cts.Cancel();
        var error = WaitForFailure(creation);
        Check(error is OperationCanceledException, $"Cancelling during creation must throw OperationCanceledException, not {error}.");
        Check(closed.Task.Wait(TimeoutMs), "Cancelling during creation must close the connection.");
    }

    static void CancellationAfterCreationDoesNotAffectStream()
    {
        using var server = new LoopbackServer(async (stream, token) =>
        {
            await WriteHeadAsync(stream, "200 OK", token, $"Content-Length: {s_wav.Length}");
            await stream.WriteAsync(s_wav, token);
        });
        using var client = CreateClient();
        using var cts = new CancellationTokenSource();

        var handle = CreateStream(client, server.Uri, cts.Token);
        try
        {
            cts.Cancel();
            Check(DecodeAll(handle).AsSpan().SequenceEqual(s_pcm), "Cancelling after creation must not cut the stream short.");
        }
        finally
        {
            Bass.StreamFree(handle);
        }
    }

    static void FreeingStreamDuringStalledDownloadClosesConnection()
    {
        var closed = NewSignal();
        using var server = new LoopbackServer(async (stream, token) =>
        {
            await WriteHeadAsync(stream, "200 OK", token, $"Content-Length: {s_wav.Length}");
            await stream.WriteAsync(s_wav.AsMemory(0, WavHeaderLength + 32 * 1024), token);
            // Stall with most of the body outstanding, so BASS's download thread blocks in the read callback.
            await WaitForClientCloseAsync(stream, token);
            closed.TrySetResult();
        });
        using var client = CreateClient();

        var handle = WithoutPreBuffering(() => CreateStream(client, server.Uri, CancellationToken.None));
        Thread.Sleep(200);
        Check(BassHttpFileSource.ActiveSourceCount == 1, "The source must stay open while the download is incomplete.");
        // BASS waits for its download thread, so a read that the close callback cannot wake would hang StreamFree.
        var free = Task.Run(() => Bass.StreamFree(handle));
        Check(free.Wait(2000), "StreamFree must not wait for the stalled download.");
        Check(free.Result, $"StreamFree failed: {Bass.LastError}");
        Check(BassHttpFileSource.ActiveSourceCount == 0, "Freeing the stream must close the source.");
        Check(closed.Task.Wait(TimeoutMs), "Freeing the stream must close the connection.");
    }

    static void ConcurrentCreateAndFreeDoesNotLeak()
    {
        using var server = new LoopbackServer(async (stream, token) =>
        {
            await WriteHeadAsync(stream, "200 OK", token, $"Content-Length: {s_wav.Length}");
            for (var offset = 0; offset < s_wav.Length; offset += 8192)
            {
                await stream.WriteAsync(s_wav.AsMemory(offset, Math.Min(8192, s_wav.Length - offset)), token);
                await Task.Delay(1, token);
            }
        });
        using var client = CreateClient();

        WithoutPreBuffering(() =>
        {
            var workers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                var buffer = new byte[4096];
                for (var i = 0; i < 8; i++)
                {
                    var handle = CreateStream(client, server.Uri, CancellationToken.None);
                    Bass.ChannelGetData(handle, buffer, buffer.Length);
                    // Free while the download thread is still running.
                    Check(Bass.StreamFree(handle), $"StreamFree failed: {Bass.LastError}");
                }
            })).ToArray();
            Check(Task.WaitAll(workers, TimeoutMs * 4), "Every create/free cycle must finish.");
            return 0;
        });
    }

    /// <summary>
    /// Creates a client configured like <c>MajEnv.SharedHttpClient</c>.
    /// </summary>
    /// <returns>A client backed by the project's libcurl handler.</returns>
    static HttpClient CreateClient()
    {
        var handler = new CurlHttpMessageHandler()
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(TimeoutMs * 2),
        };
    }

    /// <summary>
    /// Starts creating a decoding stream on the thread pool, as the game does.
    /// </summary>
    static Task<int> StartCreate(HttpClient client, Uri uri, CancellationToken token)
    {
        return Task.Run(() => BassHttpFileSource.CreateStreamAsync(client, uri, BassFlags.Decode, token).AsTask());
    }

    /// <summary>
    /// Creates a decoding stream and fails the check if creation fails or takes too long.
    /// </summary>
    static int CreateStream(HttpClient client, Uri uri, CancellationToken token)
    {
        var creation = StartCreate(client, uri, token);
        Check(creation.Wait(TimeoutMs), "Stream creation timed out.");
        var handle = creation.Result;
        Check(handle != 0, "Stream creation returned no handle.");
        return handle;
    }

    /// <summary>
    /// Creates a decoding stream that is expected to fail, and returns the failure.
    /// </summary>
    static Exception CaptureCreateFailure(HttpClient client, Uri uri, CancellationToken token)
    {
        return WaitForFailure(StartCreate(client, uri, token));
    }

    /// <summary>
    /// Waits for a creation that is expected to fail, freeing the stream if it unexpectedly succeeds.
    /// </summary>
    static Exception WaitForFailure(Task<int> creation)
    {
        try
        {
            Check(creation.Wait(TimeoutMs), "Stream creation did not finish.");
        }
        catch (AggregateException error)
        {
            return error.InnerException!;
        }
        Bass.StreamFree(creation.Result);
        throw new InvalidOperationException("Stream creation was expected to fail but succeeded.");
    }

    /// <summary>
    /// Runs <paramref name="action"/> with pre-buffering disabled, so stream creation does not wait for most of the body.
    /// </summary>
    static T WithoutPreBuffering<T>(Func<T> action)
    {
        var preBuffer = Bass.NetPreBuffer;
        Bass.NetPreBuffer = 0;
        try
        {
            return action();
        }
        finally
        {
            Bass.NetPreBuffer = preBuffer;
        }
    }

    /// <summary>
    /// Decodes a stream to its end.
    /// </summary>
    static byte[] DecodeAll(int handle)
    {
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            var read = Bass.ChannelGetData(handle, buffer, buffer.Length);
            if (read == -1)
            {
                Check(Bass.LastError == Errors.Ended, $"Decoding failed: {Bass.LastError}");
                break;
            }
            output.Write(buffer, 0, read);
            if (read == 0)
            {
                Thread.Sleep(1);
            }
            Check(elapsed.ElapsedMilliseconds < TimeoutMs, "Decoding timed out.");
        }
        return output.ToArray();
    }

    static TaskCompletionSource NewSignal()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    static bool WaitUntil(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.ElapsedMilliseconds > TimeoutMs)
            {
                return false;
            }
            Thread.Sleep(5);
        }
        return true;
    }

    static void Check(bool condition, string message)
    {
        Interlocked.Increment(ref s_assertions);
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    static byte[] CreatePcm(int frames)
    {
        var pcm = new byte[frames * BytesPerFrame];
        for (var i = 0; i < frames; i++)
        {
            var left = (short)(Math.Sin(i * 2 * Math.PI * 440 / SampleRate) * 12000);
            var right = (short)(Math.Sin(i * 2 * Math.PI * 660 / SampleRate) * 9000);
            BitConverter.TryWriteBytes(pcm.AsSpan(i * BytesPerFrame), left);
            BitConverter.TryWriteBytes(pcm.AsSpan(i * BytesPerFrame + sizeof(short)), right);
        }
        return pcm;
    }

    static byte[] CreateWav(byte[] pcm)
    {
        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + pcm.Length);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)ChannelCount);
            writer.Write(SampleRate);
            writer.Write(SampleRate * BytesPerFrame);
            writer.Write((short)BytesPerFrame);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(pcm.Length);
            writer.Write(pcm);
        }
        return output.ToArray();
    }

    static byte[] Gzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(data);
        }
        return output.ToArray();
    }

    static async Task WriteHeadAsync(Stream stream, string status, CancellationToken token, params string[] headers)
    {
        var head = new StringBuilder();
        head.Append("HTTP/1.1 ").Append(status).Append("\r\nConnection: close\r\n");
        foreach (var header in headers)
        {
            head.Append(header).Append("\r\n");
        }
        head.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), token);
        await stream.FlushAsync(token);
    }

    /// <summary>
    /// Returns once the client closes the connection or the server stops.
    /// </summary>
    static async Task WaitForClientCloseAsync(Stream stream, CancellationToken token)
    {
        var buffer = new byte[256];
        try
        {
            while (await stream.ReadAsync(buffer, token) > 0)
            {
            }
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// One-connection-per-request HTTP/1.1 server on the loopback interface.
    /// </summary>
    sealed class LoopbackServer : IDisposable
    {
        readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        readonly CancellationTokenSource _stopping = new();
        readonly Func<NetworkStream, CancellationToken, Task> _respond;
        readonly Task _acceptLoop;
        int _requestCount;

        /// <summary>
        /// Starts listening.
        /// </summary>
        /// <param name="respond">Writes the response after the request headers have been read.</param>
        public LoopbackServer(Func<NetworkStream, CancellationToken, Task> respond)
        {
            _respond = respond;
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            // The query stands in for a signed URL; the production code must not log it.
            Uri = new Uri($"http://127.0.0.1:{port}/track.wav?token=secret");
            _acceptLoop = AcceptLoopAsync();
        }

        /// <summary>
        /// Gets the address served by this server.
        /// </summary>
        public Uri Uri { get; }

        /// <summary>
        /// Gets how many requests have been received.
        /// </summary>
        public int RequestCount => Volatile.Read(ref _requestCount);

        public void Dispose()
        {
            _stopping.Cancel();
            _listener.Stop();
            _acceptLoop.Wait(TimeoutMs);
            _stopping.Dispose();
        }

        async Task AcceptLoopAsync()
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                }
                catch (Exception)
                {
                    return;
                }
                _ = ServeAsync(client);
            }
        }

        async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                var stream = client.GetStream();
                try
                {
                    await ReadRequestHeadAsync(stream, _stopping.Token);
                    Interlocked.Increment(ref _requestCount);
                    await _respond(stream, _stopping.Token);
                }
                catch (Exception)
                {
                    // The client went away or the server is stopping.
                }
            }
        }

        static async Task ReadRequestHeadAsync(Stream stream, CancellationToken token)
        {
            var buffer = new byte[1];
            var matched = 0;
            var terminator = "\r\n\r\n"u8.ToArray();
            while (matched < terminator.Length)
            {
                if (await stream.ReadAsync(buffer, token) == 0)
                {
                    throw new EndOfStreamException();
                }
                matched = buffer[0] == terminator[matched] ? matched + 1 : (buffer[0] == terminator[0] ? 1 : 0);
            }
        }
    }
}
