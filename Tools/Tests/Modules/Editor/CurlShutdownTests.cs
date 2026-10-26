using MajdataPlay.Net.Curl;
using MajdataPlay.Net.Curl.Core;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MajdataPlay.Net.Tests
{
    public class CurlShutdownTests
    {
        const int TimeoutMs = 5000;

        [Test]
        public void DisposingHttpClientStopsIdleWorker()
        {
            var handler = new CurlHttpMessageHandler();
            using var client = new HttpClient(handler);
            var multi = GetMulti(handler);
            var worker = GetWorker(multi);

            var elapsed = Stopwatch.StartNew();
            client.Dispose();

            Assert.That(worker.IsCompleted, Is.True, "HTTP client must stop its owned worker.");
            Assert.That(elapsed.ElapsedMilliseconds, Is.LessThan(2000));
            Assert.That(multi.IsAllocated, Is.False);
            Assert.DoesNotThrow(client.Dispose);
            Assert.DoesNotThrow(handler.Dispose);
        }

        [Test]
        public void SyncAndAsyncDisposalCanRunTogether()
        {
            using var handler = new CurlHttpMessageHandler();
            var multi = GetMulti(handler);
            var syncDispose = Task.Run(() => multi.Dispose());
            var asyncDispose = multi.DisposeAsync().AsTask();

            Assert.That(Task.WaitAll(new[] { syncDispose, asyncDispose }, TimeoutMs), Is.True);
            Assert.That(GetWorker(multi).IsCompleted, Is.True);
            Assert.That(multi.IsAllocated, Is.False);
            Assert.DoesNotThrow(() => multi.DisposeAsync().AsTask().GetAwaiter().GetResult());
        }

        [Test]
        public void DisposingClientFailsRequestWaitingForHeaders()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var accepted = listener.AcceptTcpClientAsync();
                using var handler = new CurlHttpMessageHandler { UseProxy = false };
                using var client = new HttpClient(handler);
                var request = Task.Run(() => client.GetAsync(GetUrl(listener)));
                Assert.That(Task.WhenAny(accepted, request, Task.Delay(TimeoutMs)).Result,
                    Is.SameAs(accepted), request.Exception?.ToString() ?? "Request must reach the local server.");
                using var peer = accepted.Result; // Keep the connection open without returning headers.

                client.Dispose();

                Assert.That(GetWorker(GetMulti(handler)).IsCompleted, Is.True);
                Assert.That(Task.WhenAny(request, Task.Delay(TimeoutMs)).Result, Is.SameAs(request));
                Assert.That(request.IsFaulted || request.IsCanceled, Is.True);
                _ = request.Exception; // Observe the expected failure.
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public void ResponseCanBeDisposedAfterClientHasStopped()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var accepted = listener.AcceptTcpClientAsync();
                using var handler = new CurlHttpMessageHandler { UseProxy = false };
                using var client = new HttpClient(handler);
                var request = Task.Run(() => client.GetAsync(GetUrl(listener), HttpCompletionOption.ResponseHeadersRead));
                Assert.That(Task.WhenAny(accepted, request, Task.Delay(TimeoutMs)).Result,
                    Is.SameAs(accepted), request.Exception?.ToString() ?? "Request must reach the local server.");
                using var peer = accepted.Result;
                var responseBytes = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK");
                peer.GetStream().Write(responseBytes, 0, responseBytes.Length);
                Assert.That(request.Wait(TimeoutMs), Is.True);
                using var response = request.Result;
                var body = Task.Run(() => response.Content.ReadAsStringAsync());
                Assert.That(body.Wait(TimeoutMs), Is.True);
                Assert.That(body.Result, Is.EqualTo("OK"));

                client.Dispose();

                Assert.That(GetWorker(GetMulti(handler)).IsCompleted, Is.True);
                Assert.DoesNotThrow(response.Dispose);
                Assert.DoesNotThrow(response.Dispose);
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public void RequestsSubmittedDuringDisposalAlwaysFinish()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                for (var iteration = 0; iteration < 10; iteration++)
                {
                    using var handler = new CurlHttpMessageHandler { UseProxy = false };
                    using var client = new HttpClient(handler);
                    var url = GetUrl(listener);
                    var requests = new Task[8];
                    for (var i = 0; i < requests.Length; i++)
                    {
                        requests[i] = Task.Run(async () =>
                        {
                            try
                            {
                                using var response = await client.GetAsync(url).ConfigureAwait(false);
                            }
                            catch (Exception exception) when (
                                exception is ObjectDisposedException ||
                                exception is OperationCanceledException ||
                                exception is HttpRequestException)
                            {
                                // Closing a client is expected to abort or reject these requests.
                            }
                        });
                    }
                    client.Dispose();
                    Assert.That(Task.WaitAll(requests, TimeoutMs), Is.True);
                    Assert.That(GetWorker(GetMulti(handler)).IsCompleted, Is.True);
                }
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public void CancellationRacingWithDisposalDoesNotLoseRequests()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                for (var iteration = 0; iteration < 20; iteration++)
                {
                    using var handler = new CurlHttpMessageHandler { UseProxy = false };
                    using var client = new HttpClient(handler);
                    using var cancellation = new CancellationTokenSource();
                    var url = GetUrl(listener);
                    var requests = new Task<HttpResponseMessage>[8];
                    for (var i = 0; i < requests.Length; i++)
                    {
                        requests[i] = Task.Run(() => client.GetAsync(url, cancellation.Token));
                    }
                    var cancel = Task.Run(() => cancellation.Cancel());
                    var dispose = Task.Run(() => handler.Dispose());
                    Assert.That(Task.WaitAll(new[] { cancel, dispose }, TimeoutMs), Is.True);
                    foreach (var request in requests)
                    {
                        Assert.That(Task.WhenAny(request, Task.Delay(TimeoutMs)).Result, Is.SameAs(request));
                        _ = request.Exception;
                        if (request.Status == TaskStatus.RanToCompletion)
                        {
                            request.Result.Dispose();
                        }
                    }
                    Assert.That(GetWorker(GetMulti(handler)).IsCompleted, Is.True);
                }
            }
            finally
            {
                listener.Stop();
            }
        }

        static string GetUrl(TcpListener listener)
        {
            return $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
        }

        static CurlMulti GetMulti(CurlHttpMessageHandler handler)
        {
            return (CurlMulti)typeof(CurlHttpMessageHandler)
                .GetField("_curlMulti", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(handler);
        }

        static Task GetWorker(CurlMulti multi)
        {
            return (Task)typeof(CurlMulti)
                .GetField("_workerThread", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(multi);
        }
    }
}
