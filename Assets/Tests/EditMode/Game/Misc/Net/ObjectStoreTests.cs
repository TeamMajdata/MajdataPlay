using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using MajdataPlay.IO.Storage;
using MajdataPlay.Net;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Misc.Support;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Net
{
    /// <summary>
    /// Covers <see cref="ObjectStore"/>: the content-addressed layout, verification against known and announced digests,
    /// partial downloads resumed across attempts, sharing concurrent downloads, and scanning and pruning the store. Every
    /// request is answered by a stub HTTP client.
    /// </summary>
    public class ObjectStoreTests
    {
        /// <summary>The content served by the stub endpoint.</summary>
        private static readonly byte[] s_content = Encoding.UTF8.GetBytes("MajdataPlay object store content");

        /// <summary>The URI serving <see cref="s_content"/>.</summary>
        private static readonly Uri s_uri = new("https://cdn.invalid/MaiCharts/1/track.mp3");

        /// <summary>The longest time a test waits for a blocked stub response.</summary>
        private static readonly TimeSpan s_gateTimeout = TimeSpan.FromSeconds(10);

        /// <summary>A reference source answering with a fixed function.</summary>
        private sealed class ReferenceSource : IObjectReferenceSource
        {
            /// <summary>The function answering <see cref="GetReferencedDigests"/>.</summary>
            private readonly Func<IEnumerable<string>> _digests;

            /// <summary>Creates the source.</summary>
            /// <param name="digests">The function answering <see cref="GetReferencedDigests"/>.</param>
            public ReferenceSource(Func<IEnumerable<string>> digests)
            {
                _digests = digests;
            }

            /// <summary>Gets the referenced digests.</summary>
            /// <returns>The digests.</returns>
            public IEnumerable<string> GetReferencedDigests()
            {
                return _digests();
            }
        }

        /// <summary>A response body that delivers some bytes and then fails, like a dropped connection.</summary>
        private sealed class InterruptedStream : MemoryStream
        {
            /// <summary>Creates the body.</summary>
            /// <param name="delivered">The bytes delivered before the failure.</param>
            public InterruptedStream(byte[] delivered) : base(delivered, false)
            {
            }

            /// <summary>Reads the delivered bytes, then fails.</summary>
            /// <param name="buffer">The destination buffer.</param>
            /// <param name="offset">The offset in the buffer.</param>
            /// <param name="count">The maximum number of bytes to read.</param>
            /// <returns>The number of bytes read.</returns>
            /// <exception cref="IOException">Every delivered byte has been read.</exception>
            public override int Read(byte[] buffer, int offset, int count)
            {
                var read = base.Read(buffer, offset, count);
                if (read == 0)
                {
                    throw new IOException("The connection was reset.");
                }
                return read;
            }
        }

        /// <summary>The stub HTTP clients.</summary>
        private StubHttpScope _http = null!;

        /// <summary>The temporary storage.</summary>
        private TemporaryDirectory _temporary = null!;

        /// <summary>The directory receiving partial downloads.</summary>
        private StorageDirectory _partials = null!;

        /// <summary>The store under test.</summary>
        private ObjectStore _store = null!;

        /// <summary>Installs the stub client and an empty store.</summary>
        [SetUp]
        public void SetUp()
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(MajEnv).TypeHandle);
            _http = new StubHttpScope();
            _temporary = new TemporaryDirectory();
            var cache = _temporary.Directory.CreateDirectory("Cache");
            _partials = cache.CreateDirectory("Temp").CreateDirectory(ObjectStore.DirectoryName);
            _store = new ObjectStore(cache.CreateDirectory(ObjectStore.DirectoryName), _partials);
        }

        /// <summary>Restores the HTTP clients and deletes the store.</summary>
        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            _http.Dispose();
            _temporary.Dispose();
        }

        /// <summary>Computes the lowercase hexadecimal SHA-256 digest of bytes.</summary>
        /// <param name="bytes">The bytes.</param>
        /// <returns>The digest.</returns>
        private static string Sha256(byte[] bytes)
        {
            using var sha256 = SHA256.Create();
            return ObjectStore.ToHex(sha256.ComputeHash(bytes));
        }

        /// <summary>Computes the lowercase hexadecimal MD5 digest of bytes.</summary>
        /// <param name="bytes">The bytes.</param>
        /// <returns>The digest.</returns>
        private static string Md5(byte[] bytes)
        {
            using var md5 = MD5.Create();
            return ObjectStore.ToHex(md5.ComputeHash(bytes));
        }

        /// <summary>Computes the Base64 SHA-256 digest a MajdataNET endpoint sends in its hash header.</summary>
        /// <param name="bytes">The bytes.</param>
        /// <returns>The digest.</returns>
        private static string HashHeader(byte[] bytes)
        {
            using var sha256 = SHA256.Create();
            return Convert.ToBase64String(sha256.ComputeHash(bytes));
        }

        /// <summary>Gets the name of the partial download of a URI whose digest is not known.</summary>
        /// <param name="uri">The URI.</param>
        /// <returns>The partial download name.</returns>
        private static string UriChunkName(Uri uri)
        {
            return "uri-" + Sha256(Encoding.UTF8.GetBytes(uri.AbsoluteUri)) + ".chunk";
        }

        /// <summary>Creates a response with binary content and an optional hash header.</summary>
        /// <param name="status">The status code.</param>
        /// <param name="bytes">The body.</param>
        /// <param name="hashHeader">The hash header, or null.</param>
        /// <returns>The response.</returns>
        private static HttpResponseMessage Bytes(HttpStatusCode status, byte[] bytes, string? hashHeader = null)
        {
            var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(bytes) };
            if (hashHeader is not null)
            {
                response.Headers.Add("hash", hashHeader);
            }
            return response;
        }

        /// <summary>Writes a file into the directory of a digest in the object directory.</summary>
        /// <param name="sha256">The digest naming the file's parent directory.</param>
        /// <param name="name">The file name.</param>
        /// <param name="bytes">The content.</param>
        /// <returns>The file.</returns>
        private StorageFile WriteObjectFile(string sha256, string name, byte[] bytes)
        {
            var file = _store.Directory.CreateDirectory(sha256.Substring(0, 2)).CreateFile(name);
            file.WriteAllBytes(bytes);
            return file;
        }

        /// <summary>Finds a file in the directory of a digest in the object directory.</summary>
        /// <param name="sha256">The digest naming the file's parent directory.</param>
        /// <param name="name">The file name.</param>
        /// <returns>The file, or null.</returns>
        private StorageFile? FindObjectFile(string sha256, string name)
        {
            return _store.Directory.FindDirectory(sha256.Substring(0, 2))?.FindFile(name);
        }

        /// <summary>Writes a partial download.</summary>
        /// <param name="name">The partial download name.</param>
        /// <param name="bytes">The content.</param>
        /// <returns>The file.</returns>
        private StorageFile WritePartial(string name, byte[] bytes)
        {
            var file = _partials.CreateFile(name);
            file.WriteAllBytes(bytes);
            return file;
        }

        /// <summary>Counts the partial downloads.</summary>
        /// <returns>The number of partial downloads.</returns>
        private int CountPartials()
        {
            return _partials.EnumerateFiles().Count();
        }

        /// <summary>Awaits an operation and returns the exception it failed with.</summary>
        /// <param name="operation">The operation.</param>
        /// <returns>The exception, or null.</returns>
        private static async UniTask<Exception?> CatchAsync(Func<UniTask> operation)
        {
            try
            {
                await operation();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        /// <summary>Checks the digest format accepted by the store.</summary>
        [Test]
        public void IsDigest_AcceptsLowercaseSha256Only()
        {
            var digest = Sha256(s_content);

            Assert.That(ObjectStore.IsDigest(digest), Is.True);
            Assert.That(ObjectStore.IsDigest(digest.ToUpperInvariant()), Is.False);
            Assert.That(ObjectStore.IsDigest(digest.Substring(1)), Is.False);
            Assert.That(ObjectStore.IsDigest(null), Is.False);
            Assert.Throws<ArgumentException>(() => _store.GetOrDownloadAsync(s_uri, "not-a-digest", null, default).GetAwaiter().GetResult());
        }

        /// <summary>Checks lowercase hexadecimal encoding on the stack and, for long input, in a pooled buffer.</summary>
        [Test]
        public void ToHex_EncodesLowercaseDigits()
        {
            var longInput = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray();

            Assert.That(ObjectStore.ToHex(new byte[] { 0x00, 0x0f, 0xa5, 0xff }), Is.EqualTo("000fa5ff"));
            Assert.That(ObjectStore.ToHex(ReadOnlySpan<byte>.Empty), Is.Empty);
            Assert.That(ObjectStore.ToHex(longInput), Is.EqualTo(BitConverter.ToString(longInput).Replace("-", string.Empty).ToLowerInvariant()));
        }

        /// <summary>Checks that a download of a known digest is stored at its two-digit directory under its digest and reports its progress.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetOrDownloadAsync_StoresObjectUnderDigest()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var digest = Sha256(s_content);
                _http.Responder = _ => Bytes(HttpStatusCode.OK, s_content);
                var progress = new NetProgress();

                var result = await _store.GetOrDownloadAsync(s_uri, digest, progress, default);

                Assert.That(result.File!.ReadAllBytes(), Is.EqualTo(s_content));
                Assert.That(result.Sha256, Is.EqualTo(digest));
                Assert.That(result.Md5, Is.EqualTo(Md5(s_content)));
                Assert.That(FindObjectFile(digest, digest), Is.Not.Null);
                Assert.That(CountPartials(), Is.Zero);
                Assert.That(_store.Contains(digest), Is.True);
                Assert.That(progress.TotalBytes, Is.EqualTo(s_content.Length));
                Assert.That(progress.Percent, Is.EqualTo(1f));
                Assert.That(_http.Requests.Single().Uri, Is.EqualTo(s_uri));
            });
        }

        /// <summary>Checks that a stored object is returned without a request.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetOrDownloadAsync_ReusesStoredObject()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var digest = Sha256(s_content);
                var stored = WriteObjectFile(digest, digest, s_content);

                var result = await _store.GetOrDownloadAsync(s_uri, digest, null, default);

                Assert.That(result.File!.Location, Is.EqualTo(stored.Location));
                Assert.That(result.Md5, Is.Null);
                Assert.That(_http.Requests, Is.Empty);
            });
        }

        /// <summary>
        /// Checks that content of an unknown digest is verified against the hash header, stored under its own digest,
        /// and that the same content from another URI shares the stored object.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetOrDownloadAsync_UnknownDigest_StoresByContent()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var digest = Sha256(s_content);
                _http.Responder = _ => Bytes(HttpStatusCode.OK, s_content, HashHeader(s_content));

                var first = await _store.GetOrDownloadAsync(s_uri, null, null, default);
                var second = await _store.GetOrDownloadAsync(new Uri("https://api.invalid/api/maichart/2/track"), null, null, default);

                Assert.That(first.Sha256, Is.EqualTo(digest));
                Assert.That(first.Md5, Is.EqualTo(Md5(s_content)));
                Assert.That(second.File!.Location, Is.EqualTo(first.File!.Location));
                Assert.That(_store.Directory.FindDirectory(digest.Substring(0, 2))!.EnumerateFiles().Count(), Is.EqualTo(1));
                Assert.That(CountPartials(), Is.Zero);
            });
        }

        /// <summary>Checks that content contradicting its hash header is retried, reported and not kept.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetOrDownloadAsync_UnknownDigest_RejectsMismatchingHashHeader()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.Responder = _ => Bytes(HttpStatusCode.OK, s_content, HashHeader(Encoding.UTF8.GetBytes("other")));

                var failure = await CatchAsync(async () => await _store.GetOrDownloadAsync(s_uri, null, null, default));

                Assert.That(((HttpException)failure!).ErrorCode, Is.EqualTo(HttpErrorCode.IntegrityCheckFailed));
                Assert.That(_http.Requests.Length, Is.EqualTo(MajEnv.HTTP_REQUEST_MAX_RETRY + 1));
                Assert.That(_store.Contains(Sha256(s_content)), Is.False);
                Assert.That(CountPartials(), Is.Zero);
            });
        }

        /// <summary>Checks that content contradicting the known digest is retried, reported and not kept.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetOrDownloadAsync_KnownDigest_ReportsIntegrityFailure()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var digest = Sha256(Encoding.UTF8.GetBytes("expected"));
                _http.Responder = _ => Bytes(HttpStatusCode.OK, s_content);

                var failure = await CatchAsync(async () => await _store.GetOrDownloadAsync(s_uri, digest, null, default));

                Assert.That(((HttpException)failure!).ErrorCode, Is.EqualTo(HttpErrorCode.IntegrityCheckFailed));
                Assert.That(_http.Requests.Length, Is.EqualTo(MajEnv.HTTP_REQUEST_MAX_RETRY + 1));
                Assert.That(FindObjectFile(digest, digest), Is.Null);
                Assert.That(CountPartials(), Is.Zero);
            });
        }

        /// <summary>Checks that a partial download of a known digest is resumed from the partial directory with a range request.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetOrDownloadAsync_ResumesPartialOfDigest()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var digest = Sha256(s_content);
                WritePartial(digest + ".chunk", s_content.Take(10).ToArray());
                var range = default(string);
                _http.Responder = request =>
                {
                    range = request.Headers.Range?.ToString();
                    return Bytes(HttpStatusCode.PartialContent, s_content.Skip(10).ToArray());
                };

                var result = await _store.GetOrDownloadAsync(s_uri, digest, null, default);

                Assert.That(range, Is.EqualTo("bytes=10-"));
                Assert.That(result.File!.ReadAllBytes(), Is.EqualTo(s_content));
                Assert.That(FindObjectFile(digest, digest), Is.Not.Null);
                Assert.That(CountPartials(), Is.Zero);
            });
        }

        /// <summary>Checks that a partial download of an unknown digest is resumed by the next download of the same URI.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetOrDownloadAsync_ResumesPartialOfUri()
        {
            return UniTask.ToCoroutine(async () =>
            {
                WritePartial(UriChunkName(s_uri), s_content.Take(10).ToArray());
                var range = default(string);
                _http.Responder = request =>
                {
                    range = request.Headers.Range?.ToString();
                    return Bytes(HttpStatusCode.PartialContent, s_content.Skip(10).ToArray(), HashHeader(s_content));
                };

                var result = await _store.GetOrDownloadAsync(s_uri, null, null, default);

                Assert.That(range, Is.EqualTo("bytes=10-"));
                Assert.That(result.Sha256, Is.EqualTo(Sha256(s_content)));
                Assert.That(result.File!.ReadAllBytes(), Is.EqualTo(s_content));
                Assert.That(CountPartials(), Is.Zero);
            });
        }

        /// <summary>Checks that an interrupted download keeps its partial download, which the next download resumes.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetOrDownloadAsync_KeepsInterruptedPartialForNextDownload()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // The final failure is logged through MajDebug, which may forward it to the Unity log.
                LogAssert.ignoreFailingMessages = true;
                var digest = Sha256(s_content);
                _http.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new InterruptedStream(s_content.Take(10).ToArray()))
                };

                var failure = await CatchAsync(async () => await _store.GetOrDownloadAsync(s_uri, digest, null, default));

                Assert.That(((HttpException)failure!).ErrorCode, Is.EqualTo(HttpErrorCode.Unreachable));
                Assert.That(_partials.FindFile(digest + ".chunk")!.ReadAllBytes(), Is.EqualTo(s_content.Take(10).ToArray()));

                var range = default(string);
                _http.Responder = request =>
                {
                    range = request.Headers.Range?.ToString();
                    return Bytes(HttpStatusCode.PartialContent, s_content.Skip(10).ToArray());
                };
                var result = await _store.GetOrDownloadAsync(s_uri, digest, null, default);

                Assert.That(range, Is.EqualTo("bytes=10-"));
                Assert.That(result.File!.ReadAllBytes(), Is.EqualTo(s_content));
                Assert.That(CountPartials(), Is.Zero);
            });
        }

        /// <summary>Checks that a complete partial download is placed when the server reports its range as unsatisfiable.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetOrDownloadAsync_PlacesCompletePartial()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var digest = Sha256(s_content);
                WritePartial(digest + ".chunk", s_content);
                _http.Responder = _ => Bytes(HttpStatusCode.RequestedRangeNotSatisfiable, Array.Empty<byte>());

                var result = await _store.GetOrDownloadAsync(s_uri, digest, null, default);

                Assert.That(result.File!.ReadAllBytes(), Is.EqualTo(s_content));
                Assert.That(_http.Requests.Length, Is.EqualTo(1));
                Assert.That(CountPartials(), Is.Zero);
            });
        }

        /// <summary>Checks that missing content is reported without a file and drops its partial download, and that unsuccessful responses are HTTP errors.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetOrDownloadAsync_ReportsMissingAndFailedContent()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var digest = Sha256(s_content);
                WritePartial(digest + ".chunk", s_content.Take(10).ToArray());

                var missing = await _store.GetOrDownloadAsync(s_uri, digest, null, default);
                Assert.That(missing.File, Is.Null);
                Assert.That(missing.Sha256, Is.Null);
                Assert.That(CountPartials(), Is.Zero);

                _http.Responder = _ => Bytes(HttpStatusCode.InternalServerError, Array.Empty<byte>());
                var failure = await CatchAsync(async () => await _store.GetOrDownloadAsync(s_uri, null, null, default));
                Assert.That(((HttpException)failure!).ErrorCode, Is.EqualTo(HttpErrorCode.Unsuccessful));
            });
        }

        /// <summary>Checks that concurrent requests for the same digest share one download and its progress.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetOrDownloadAsync_SharesConcurrentDownloads()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var digest = Sha256(s_content);
                using var gate = new ManualResetEventSlim(false);
                _http.Responder = _ =>
                {
                    gate.Wait(s_gateTimeout);
                    return Bytes(HttpStatusCode.OK, s_content);
                };
                var secondProgress = new NetProgress();

                var first = _store.GetOrDownloadAsync(s_uri, digest, null, default);
                var second = _store.GetOrDownloadAsync(new Uri("https://cdn.invalid/MaiCharts/2/track.mp3"), digest, secondProgress, default);
                gate.Set();
                var results = await System.Threading.Tasks.Task.WhenAll(first, second);

                Assert.That(_http.Requests.Length, Is.EqualTo(1));
                Assert.That(results[1].File!.Location, Is.EqualTo(results[0].File!.Location));
                Assert.That(secondProgress.TotalBytes, Is.EqualTo(s_content.Length));
            });
        }

        /// <summary>Checks that canceling one of two requests leaves the shared download running for the other.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetOrDownloadAsync_CancelingOneRequestKeepsSharedDownload()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var digest = Sha256(s_content);
                using var gate = new ManualResetEventSlim(false);
                _http.Responder = _ =>
                {
                    gate.Wait(s_gateTimeout);
                    return Bytes(HttpStatusCode.OK, s_content);
                };
                using var canceled = new CancellationTokenSource();

                var first = _store.GetOrDownloadAsync(s_uri, digest, null, canceled.Token);
                var second = _store.GetOrDownloadAsync(s_uri, digest, null, default);
                canceled.Cancel();
                var failure = await CatchAsync(async () => await first);
                gate.Set();
                var result = await second;

                Assert.That(failure, Is.InstanceOf<OperationCanceledException>());
                Assert.That(result.File!.ReadAllBytes(), Is.EqualTo(s_content));
                Assert.That(_http.Requests.Length, Is.EqualTo(1));
            });
        }

        /// <summary>Checks that canceling every request stops the shared download, so the next request downloads again.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetOrDownloadAsync_CancelingEveryRequestStopsDownload()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var digest = Sha256(s_content);
                using var gate = new ManualResetEventSlim(false);
                _http.Responder = _ =>
                {
                    gate.Wait(s_gateTimeout);
                    return Bytes(HttpStatusCode.OK, s_content);
                };
                using var canceled = new CancellationTokenSource();

                var abandoned = _store.GetOrDownloadAsync(s_uri, digest, null, canceled.Token);
                canceled.Cancel();
                var failure = await CatchAsync(async () => await abandoned);
                var next = _store.GetOrDownloadAsync(s_uri, digest, null, default);
                gate.Set();
                var result = await next;

                Assert.That(failure, Is.InstanceOf<OperationCanceledException>());
                Assert.That(result.File!.ReadAllBytes(), Is.EqualTo(s_content));
                Assert.That(_http.Requests.Length, Is.EqualTo(2));
            });
        }

        /// <summary>Checks that a scan indexes the stored objects only and leaves partial downloads in place.</summary>
        [Test]
        public void Scan_IndexesObjects()
        {
            var digest = Sha256(s_content);
            var partialDigest = Sha256(Encoding.UTF8.GetBytes("partial"));
            var stored = WriteObjectFile(digest, digest, s_content);
            WriteObjectFile(digest, "unrelated.txt", s_content);
            WritePartial(partialDigest + ".chunk", s_content);

            var count = _store.Scan();

            Assert.That(count, Is.EqualTo(1));
            Assert.That(_store.IsScanned, Is.True);
            Assert.That(_store.Contains(digest), Is.True);
            Assert.That(_store.Contains(partialDigest), Is.False);
            Assert.That(_partials.FindFile(partialDigest + ".chunk"), Is.Not.Null);

            stored.Delete();
            Assert.That(_store.Find(digest), Is.Null);
            Assert.That(_store.Contains(digest), Is.False);
        }

        /// <summary>
        /// Checks that pruning deletes unreferenced objects and partial downloads of unreferenced digests, and partial
        /// downloads of URIs only once they are stale.
        /// </summary>
        [Test]
        public void Prune_DeletesUnreferencedFiles()
        {
            var kept = Sha256(s_content);
            var dropped = Sha256(Encoding.UTF8.GetBytes("dropped"));
            var partial = Sha256(Encoding.UTF8.GetBytes("partial"));
            WriteObjectFile(kept, kept, s_content);
            WriteObjectFile(dropped, dropped, s_content);
            WritePartial(kept + ".chunk", s_content);
            WritePartial(partial + ".chunk", s_content);
            var freshUri = WritePartial(UriChunkName(s_uri), s_content);
            var staleUri = WritePartial(UriChunkName(new Uri("https://api.invalid/api/maichart/3/video")), s_content);
            staleUri.SetLastWriteTime(DateTime.UtcNow - TimeSpan.FromDays(8));
            _store.Scan();

            var deleted = _store.Prune(new HashSet<string> { kept });

            Assert.That(deleted, Is.EqualTo(3));
            Assert.That(_store.Contains(kept), Is.True);
            Assert.That(_store.Contains(dropped), Is.False);
            Assert.That(FindObjectFile(dropped, dropped), Is.Null);
            Assert.That(_partials.FindFile(kept + ".chunk"), Is.Not.Null);
            Assert.That(_partials.FindFile(partial + ".chunk"), Is.Null);
            Assert.That(freshUri.Exists, Is.True);
            Assert.That(staleUri.Exists, Is.False);
        }

        /// <summary>
        /// Checks that the startup scan prunes only once a reference source is registered, keeps what any source
        /// references, and prunes only once per store.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ScanAsync_PrunesOnceWithRegisteredSources()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var first = Sha256(Encoding.UTF8.GetBytes("first"));
                var second = Sha256(Encoding.UTF8.GetBytes("second"));
                var orphan = Sha256(Encoding.UTF8.GetBytes("orphan"));
                WriteObjectFile(first, first, s_content);
                WriteObjectFile(second, second, s_content);
                WriteObjectFile(orphan, orphan, s_content);

                await _store.ScanAsync();
                Assert.That(_store.Contains(orphan), Is.True);

                _store.AddReferenceSource(new ReferenceSource(() => new[] { first }));
                _store.AddReferenceSource(new ReferenceSource(() => new[] { second }));
                await _store.ScanAsync();
                Assert.That(_store.Contains(first), Is.True);
                Assert.That(_store.Contains(second), Is.True);
                Assert.That(_store.Contains(orphan), Is.False);

                WriteObjectFile(orphan, orphan, s_content);
                await _store.ScanAsync();
                Assert.That(_store.Contains(orphan), Is.True);
            });
        }

        /// <summary>Checks that a reference source failing to report its references keeps every object.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ScanAsync_FailingSourceSkipsPruning()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // The skipped pruning is logged through MajDebug, which may forward it to the Unity log.
                LogAssert.ignoreFailingMessages = true;
                var orphan = Sha256(s_content);
                WriteObjectFile(orphan, orphan, s_content);
                _store.AddReferenceSource(new ReferenceSource(() => Array.Empty<string>()));
                _store.AddReferenceSource(new ReferenceSource(() => throw new IOException("unreadable")));

                await _store.ScanAsync();

                Assert.That(_store.Contains(orphan), Is.True);
            });
        }
    }
}
