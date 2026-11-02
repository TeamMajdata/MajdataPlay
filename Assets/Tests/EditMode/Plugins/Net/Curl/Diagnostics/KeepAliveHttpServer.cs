#nullable enable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MajdataPlay.Tests.Plugins.Net.Curl.Diagnostics
{
    /// <summary>
    /// A minimal HTTP/1.1 server bound to 127.0.0.1 that keeps connections open between requests, so connection reuse
    /// can be observed. The request path picks the behavior: <c>/close</c> answers with <c>Connection: close</c>,
    /// <c>/drop</c> answers as keep-alive and then closes the connection silently, <c>/hold</c> waits for
    /// <see cref="ReleaseHeld"/> before answering, <c>/redirect</c> redirects to <c>/redirected</c> on the same
    /// connection, and any other path answers <c>ok</c> at once.
    /// </summary>
    internal sealed class KeepAliveHttpServer : IDisposable
    {
        /// <summary>The listening socket.</summary>
        private readonly TcpListener _listener;

        /// <summary>Stops the accept loop and the connection loops.</summary>
        private readonly CancellationTokenSource _cancellation = new();

        /// <summary>The connections that are still open, closed on disposal.</summary>
        private readonly ConcurrentDictionary<TcpClient, bool> _clients = new();

        /// <summary>Completed to let every <c>/hold</c> request answer.</summary>
        private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The accept loop.</summary>
        private readonly Task _acceptLoop;

        /// <summary>Backing field of <see cref="ConnectionCount"/>.</summary>
        private int _connectionCount;

        /// <summary>Backing field of <see cref="RequestCount"/>.</summary>
        private int _requestCount;

        /// <summary>Backing field of <see cref="HeldCount"/>.</summary>
        private int _heldCount;

        /// <summary>Starts listening.</summary>
        public KeepAliveHttpServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        /// <summary>Gets the bound port.</summary>
        public int Port { get; }

        /// <summary>Gets the base URL of the server, ending with a slash.</summary>
        public string Url => $"http://127.0.0.1:{Port}/";

        /// <summary>Gets the number of connections accepted so far.</summary>
        public int ConnectionCount => Volatile.Read(ref _connectionCount);

        /// <summary>Gets the number of requests received so far.</summary>
        public int RequestCount => Volatile.Read(ref _requestCount);

        /// <summary>Gets the number of <c>/hold</c> requests waiting for <see cref="ReleaseHeld"/>.</summary>
        public int HeldCount => Volatile.Read(ref _heldCount);

        /// <summary>Lets every waiting and future <c>/hold</c> request answer.</summary>
        public void ReleaseHeld()
        {
            _release.TrySetResult(true);
        }

        /// <summary>Stops listening and closes every open connection.</summary>
        public void Dispose()
        {
            _cancellation.Cancel();
            _release.TrySetResult(true);
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
                Interlocked.Increment(ref _connectionCount);
                _clients.TryAdd(client, true);
                _ = Task.Run(() => ServeAsync(client));
            }
        }

        /// <summary>Answers requests on one connection until either side closes it.</summary>
        /// <param name="client">The accepted connection.</param>
        /// <returns>A task completing when the connection has been closed.</returns>
        private async Task ServeAsync(TcpClient client)
        {
            try
            {
                var stream = client.GetStream();
                while (!_cancellation.IsCancellationRequested)
                {
                    var target = await ReadRequestTargetAsync(stream).ConfigureAwait(false);
                    if (target is null)
                    {
                        return;
                    }
                    Interlocked.Increment(ref _requestCount);
                    if (target == "/hold")
                    {
                        Interlocked.Increment(ref _heldCount);
                        await _release.Task.ConfigureAwait(false);
                        Interlocked.Decrement(ref _heldCount);
                    }
                    var isClosing = target == "/close";
                    var head = target == "/redirect"
                        ? "HTTP/1.1 302 Found\r\nLocation: /redirected\r\nContent-Length: 0\r\n\r\n"
                        : "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n" + (isClosing ? "Connection: close\r\n" : string.Empty) + "\r\nok";
                    var bytes = Encoding.ASCII.GetBytes(head);
                    await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                    if (isClosing || target == "/drop")
                    {
                        return;
                    }
                }
            }
            catch (Exception)
            {
                // A connection closed by the client or by disposal ends this loop.
            }
            finally
            {
                _clients.TryRemove(client, out _);
                client.Close();
            }
        }

        /// <summary>Reads one request head and returns its target.</summary>
        /// <param name="stream">The connection stream.</param>
        /// <returns>The request target, such as <c>/close</c>, or <see langword="null"/> when the connection closed first.</returns>
        private static async Task<string?> ReadRequestTargetAsync(Stream stream)
        {
            var head = new StringBuilder();
            var buffer = new byte[1];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, 0, 1).ConfigureAwait(false);
                if (read == 0)
                {
                    return null;
                }
                head.Append((char)buffer[0]);
                if (head.Length >= 4 && head.ToString(head.Length - 4, 4) == "\r\n\r\n")
                {
                    break;
                }
            }
            var requestLine = head.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None)[0].Split(' ');

            return requestLine.Length > 1 ? requestLine[1] : "/";
        }
    }
}
