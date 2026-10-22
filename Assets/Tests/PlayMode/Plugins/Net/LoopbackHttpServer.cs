#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MajdataPlay.Tests.Plugins.Net
{
    /// <summary>
    /// A minimal HTTP/1.1 server bound to 127.0.0.1 that records every request and answers it with a test-supplied
    /// handler, so HTTP clients can be exercised without any network access.
    /// </summary>
    /// <remarks>Each connection serves exactly one request and is closed afterwards.</remarks>
    public sealed class LoopbackHttpServer : IDisposable
    {
        /// <summary>The listening socket.</summary>
        private readonly TcpListener _listener;

        /// <summary>Stops the accept loop and pending handlers.</summary>
        private readonly CancellationTokenSource _cancellation = new();

        /// <summary>The connections that are still open, closed on disposal to unblock handlers.</summary>
        private readonly ConcurrentDictionary<TcpClient, bool> _clients = new();

        /// <summary>The handler that writes the response for each received request.</summary>
        private readonly Func<ReceivedRequest, Stream, CancellationToken, Task> _handler;

        /// <summary>The accept loop.</summary>
        private readonly Task _acceptLoop;

        /// <summary>Starts a server that answers with the given handler.</summary>
        /// <param name="handler">Writes the raw response for a request to the connection stream.</param>
        public LoopbackHttpServer(Func<ReceivedRequest, Stream, CancellationToken, Task> handler)
        {
            _handler = handler;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        /// <summary>Gets the bound port.</summary>
        public int Port { get; }

        /// <summary>Gets the base URL of the server, ending with a slash.</summary>
        public string Url => $"http://127.0.0.1:{Port}/";

        /// <summary>Gets every request received so far, in arrival order.</summary>
        public ConcurrentQueue<ReceivedRequest> Requests { get; } = new();

        /// <summary>Gets the number of requests received so far.</summary>
        public int RequestCount => Requests.Count;

        /// <summary>Gets an event that is set whenever a request has been received.</summary>
        public SemaphoreSlim RequestReceived { get; } = new(0);

        /// <summary>Creates a server that always answers with the same response.</summary>
        /// <param name="status">The status code.</param>
        /// <param name="body">The UTF-8 body.</param>
        /// <param name="headers">Additional raw header lines.</param>
        /// <returns>The started server.</returns>
        public static LoopbackHttpServer Respond(int status, string body, params string[] headers)
        {
            return new LoopbackHttpServer((request, stream, token) => WriteResponseAsync(stream, status, Encoding.UTF8.GetBytes(body), headers));
        }

        /// <summary>Writes a complete response with a content length and <c>Connection: close</c>.</summary>
        /// <param name="stream">The connection stream.</param>
        /// <param name="status">The status code.</param>
        /// <param name="body">The body bytes.</param>
        /// <param name="headers">Additional raw header lines without line terminators.</param>
        /// <returns>A task completing when the response has been written.</returns>
        public static async Task WriteResponseAsync(Stream stream, int status, byte[] body, params string[] headers)
        {
            var head = new StringBuilder();
            head.Append("HTTP/1.1 ").Append(status).Append(" Status\r\n");
            foreach (var header in headers)
            {
                head.Append(header).Append("\r\n");
            }
            head.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            head.Append("Connection: close\r\n\r\n");
            await WriteAsync(stream, head.ToString()).ConfigureAwait(false);
            await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        /// <summary>Writes ASCII text to a connection stream.</summary>
        /// <param name="stream">The connection stream.</param>
        /// <param name="text">The text to write.</param>
        /// <returns>A task completing when the text has been written.</returns>
        public static Task WriteAsync(Stream stream, string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            return stream.WriteAsync(bytes, 0, bytes.Length);
        }

        /// <summary>Waits until the given number of requests has been received.</summary>
        /// <param name="count">The number of requests to wait for.</param>
        /// <param name="timeoutMs">The maximum wait in milliseconds.</param>
        /// <returns>True when the requests arrived in time.</returns>
        public async Task<bool> WaitForRequestsAsync(int count, int timeoutMs = 5000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (RequestCount < count)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    return false;
                }
                await RequestReceived.WaitAsync(remaining).ConfigureAwait(false);
            }
            return true;
        }

        /// <summary>Stops listening and closes every open connection.</summary>
        public void Dispose()
        {
            _cancellation.Cancel();
            _listener.Stop();
            foreach (var client in _clients.Keys)
            {
                client.Close();
            }
            _acceptLoop.Wait(2000);
        }

        /// <summary>Accepts connections until disposal and serves each one on its own task.</summary>
        /// <returns>A task completing when the listener stops.</returns>
        private async Task AcceptLoopAsync()
        {
            while (!_cancellation.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }
                _clients.TryAdd(client, true);
                _ = Task.Run(() => ServeAsync(client));
            }
        }

        /// <summary>Reads one request, records it, runs the handler and closes the connection.</summary>
        /// <param name="client">The accepted connection.</param>
        /// <returns>A task completing when the connection has been closed.</returns>
        private async Task ServeAsync(TcpClient client)
        {
            try
            {
                var stream = client.GetStream();
                var request = await ReadRequestAsync(stream).ConfigureAwait(false);
                if (request is null)
                {
                    return;
                }
                Requests.Enqueue(request);
                RequestReceived.Release();
                await _handler(request, stream, _cancellation.Token).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A closed connection or a cancelled handler ends this exchange.
            }
            finally
            {
                _clients.TryRemove(client, out _);
                client.Close();
            }
        }

        /// <summary>Reads the request head and body, answering <c>Expect: 100-continue</c> when asked.</summary>
        /// <param name="stream">The connection stream.</param>
        /// <returns>The parsed request, or null when the connection closed first.</returns>
        private static async Task<ReceivedRequest?> ReadRequestAsync(Stream stream)
        {
            var head = await ReadHeadAsync(stream).ConfigureAwait(false);
            if (head is null)
            {
                return null;
            }
            var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var requestLine = lines[0].Split(' ');
            var headers = new List<KeyValuePair<string, string>>();
            for (var i = 1; i < lines.Length; i++)
            {
                var colon = lines[i].IndexOf(':');
                if (colon > 0)
                {
                    headers.Add(new KeyValuePair<string, string>(lines[i].Substring(0, colon).Trim(), lines[i].Substring(colon + 1).Trim()));
                }
            }
            var request = new ReceivedRequest(requestLine[0], requestLine.Length > 1 ? requestLine[1] : string.Empty, headers);
            if (string.Equals(request.GetHeader("Expect"), "100-continue", StringComparison.OrdinalIgnoreCase))
            {
                await WriteAsync(stream, "HTTP/1.1 100 Continue\r\n\r\n").ConfigureAwait(false);
            }
            if (string.Equals(request.GetHeader("Transfer-Encoding"), "chunked", StringComparison.OrdinalIgnoreCase))
            {
                request.Body = await ReadChunkedBodyAsync(stream).ConfigureAwait(false);
            }
            else if (long.TryParse(request.GetHeader("Content-Length"), out var length) && length > 0)
            {
                request.Body = await ReadExactAsync(stream, (int)length).ConfigureAwait(false);
            }
            return request;
        }

        /// <summary>Reads bytes up to and excluding the blank line that ends the request head.</summary>
        /// <param name="stream">The connection stream.</param>
        /// <returns>The ASCII head, or null when the connection closed first.</returns>
        private static async Task<string?> ReadHeadAsync(Stream stream)
        {
            var bytes = new List<byte>();
            var single = new byte[1];
            while (true)
            {
                var read = await stream.ReadAsync(single, 0, 1).ConfigureAwait(false);
                if (read == 0)
                {
                    return null;
                }
                bytes.Add(single[0]);
                var count = bytes.Count;
                if (count >= 4 && bytes[count - 4] == '\r' && bytes[count - 3] == '\n' && bytes[count - 2] == '\r' && bytes[count - 1] == '\n')
                {
                    return Encoding.ASCII.GetString(bytes.ToArray(), 0, count - 4);
                }
            }
        }

        /// <summary>Reads one CRLF-terminated line.</summary>
        /// <param name="stream">The connection stream.</param>
        /// <returns>The line without its terminator.</returns>
        /// <exception cref="EndOfStreamException">The connection closed first.</exception>
        private static async Task<string> ReadLineAsync(Stream stream)
        {
            var bytes = new List<byte>();
            var single = new byte[1];
            while (true)
            {
                var read = await stream.ReadAsync(single, 0, 1).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }
                if (single[0] == '\n' && bytes.Count > 0 && bytes[bytes.Count - 1] == '\r')
                {
                    return Encoding.ASCII.GetString(bytes.ToArray(), 0, bytes.Count - 1);
                }
                bytes.Add(single[0]);
            }
        }

        /// <summary>Reads a chunked request body including its terminating chunk and trailers.</summary>
        /// <param name="stream">The connection stream.</param>
        /// <returns>The decoded body.</returns>
        private static async Task<byte[]> ReadChunkedBodyAsync(Stream stream)
        {
            var body = new MemoryStream();
            while (true)
            {
                var sizeLine = await ReadLineAsync(stream).ConfigureAwait(false);
                var size = Convert.ToInt32(sizeLine.Split(';')[0].Trim(), 16);
                if (size == 0)
                {
                    while ((await ReadLineAsync(stream).ConfigureAwait(false)).Length != 0)
                    {
                    }
                    return body.ToArray();
                }
                var chunk = await ReadExactAsync(stream, size).ConfigureAwait(false);
                body.Write(chunk, 0, chunk.Length);
                await ReadLineAsync(stream).ConfigureAwait(false);
            }
        }

        /// <summary>Reads exactly the given number of bytes.</summary>
        /// <param name="stream">The connection stream.</param>
        /// <param name="length">The number of bytes.</param>
        /// <returns>The bytes read.</returns>
        /// <exception cref="EndOfStreamException">The connection closed first.</exception>
        private static async Task<byte[]> ReadExactAsync(Stream stream, int length)
        {
            var buffer = new byte[length];
            var offset = 0;
            while (offset < length)
            {
                var read = await stream.ReadAsync(buffer, offset, length - offset).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }
                offset += read;
            }
            return buffer;
        }
    }
}
