using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using MajdataPlay.Databases;
using MajdataPlay.IO;
using MajdataPlay.IO.Storage;
using MajdataPlay.Net;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Misc.Support;
using MajSimai;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Base
{
    /// <summary>
    /// Covers the cache and download logic of <see cref="OnlineSongDetail"/>: chart, video, track and cover downloads,
    /// integrity checks, resumable chunks and failure handling. Every request is answered by a stub HTTP client.
    /// </summary>
    public class OnlineSongDetailTests
    {
        /// <summary>The chart served by the stub endpoint.</summary>
        private static readonly byte[] s_chart = Encoding.UTF8.GetBytes(ChartFixture.CreateMaidata("Online"));

        /// <summary>The stub HTTP clients.</summary>
        private StubHttpScope _http = null!;

        /// <summary>The temporary storage.</summary>
        private TemporaryDirectory _temporary = null!;

        /// <summary>The song cache directory.</summary>
        private StorageDirectory _cache = null!;

        /// <summary>The endpoint serving the song.</summary>
        private ApiEndpoint _endpoint = null!;

        /// <summary>Installs the stub client and an empty song cache.</summary>
        [SetUp]
        public void SetUp()
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(MajEnv).TypeHandle);
            _http = new StubHttpScope();
            _temporary = new TemporaryDirectory();
            _cache = _temporary.Directory.CreateDirectory("Song");
            _endpoint = new ApiEndpoint { Name = "Server", Url = new Uri("https://api.invalid/api/") };
        }

        /// <summary>Restores the HTTP clients and deletes the cache.</summary>
        [TearDown]
        public void TearDown()
        {
            _http.Dispose();
            _temporary.Dispose();
        }

        /// <summary>Computes the Simai hash of chart bytes.</summary>
        /// <param name="bytes">The chart bytes.</param>
        /// <returns>The chart hash.</returns>
        private static string ChartHash(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes);
            return SimaiParser.ParseMetadata(stream).Hash;
        }

        /// <summary>Computes the Base64 SHA-256 digest the server sends in the hash header.</summary>
        /// <param name="bytes">The resource bytes.</param>
        /// <returns>The digest.</returns>
        private static string Digest(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(bytes));
        }

        /// <summary>Creates a response with binary content and an optional digest header.</summary>
        /// <param name="status">The status code.</param>
        /// <param name="bytes">The body.</param>
        /// <param name="digest">The digest header, or null.</param>
        /// <returns>The response.</returns>
        private static HttpResponseMessage Bytes(HttpStatusCode status, byte[] bytes, string? digest = null)
        {
            var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(bytes) };
            if (digest is not null)
            {
                response.Headers.Add("hash", digest);
            }
            return response;
        }

        /// <summary>Creates the song served by the stub endpoint.</summary>
        /// <param name="hash">The chart hash announced by the server, or null for the hash of the served chart.</param>
        /// <returns>The song.</returns>
        private OnlineSongDetail CreateSong(string? hash = null)
        {
            return new OnlineSongDetail(_cache, _endpoint, new MajnetSongDetail
            {
                Id = Guid.NewGuid().ToString(),
                Title = "Online",
                Artist = "Artist",
                Designer = "designer",
                Uploader = "uploader",
                Hash = hash ?? ChartHash(s_chart),
                Levels = new[] { "1", "2", "3", "4", "5", "6", "7", "8" },
                Tags = new[] { "tag" },
                PublicTags = new[] { "public" },
                Timestamp = new DateTime(2024, 1, 2)
            });
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

        /// <summary>Checks the metadata taken from the server song description.</summary>
        [Test]
        public void Constructor_ReadsMetadata()
        {
            using var song = CreateSong("server-hash");

            Assert.That(song.Title, Is.EqualTo("Online"));
            Assert.That(song.Artist, Is.EqualTo("Artist"));
            Assert.That(song.Hash, Is.EqualTo("server-hash"));
            Assert.That(song.Location, Is.EqualTo(ChartStorageLocation.Online));
            Assert.That(song.ServerInfo, Is.SameAs(_endpoint));
            Assert.That(song.Timestamp, Is.EqualTo(new DateTime(2024, 1, 2)));
            Assert.That(song.Levels.Length, Is.EqualTo(7));
            Assert.That(song.Levels[6], Is.EqualTo("7"));
            Assert.That(song.Designers[0], Is.EqualTo("uploader@designer"));
            Assert.That(song.Description, Does.Contain("tag").And.Contain("public"));
            Assert.That(song.IsMaidataLoaded, Is.False);
            Assert.That(song.IsVideoLoaded, Is.False);
            Assert.That(song.IsCoverLoaded, Is.False);
            Assert.That(song.IsCompressedCoverLoaded, Is.False);
            Assert.That(song.IsAudioTrackLoaded, Is.False);
            Assert.That(song.IsPreviewAudioTrackLoaded, Is.False);
            Assert.Throws<ArgumentNullException>(() => new OnlineSongDetail(null!, _endpoint, new MajnetSongDetail()));
            Assert.Throws<ArgumentNullException>(() => new OnlineSongDetail(_cache, null!, new MajnetSongDetail()));
            Assert.Throws<ArgumentNullException>(() => new OnlineSongDetail(_cache, _endpoint, null!));
        }

        /// <summary>Checks that the chart is downloaded with its digest, cached on disk and in memory, and reused.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetMaidataAsync_DownloadsAndCaches()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.Responder = _ => Bytes(HttpStatusCode.OK, s_chart, Digest(s_chart));
                using var song = CreateSong();
                var progress = new NetProgress();

                var chart = await song.GetMaidataAsync(progress: progress);
                var cached = await song.GetMaidataAsync();
                var reread = await song.GetMaidataAsync(ignoreCache: true);

                Assert.That(chart.Title, Is.EqualTo("Online"));
                Assert.That(cached, Is.SameAs(chart));
                Assert.That(reread.Title, Is.EqualTo("Online"));
                Assert.That(song.IsMaidataLoaded, Is.True);
                Assert.That(progress.Percent, Is.EqualTo(1f));
                Assert.That(progress.TotalBytes, Is.EqualTo(s_chart.Length));
                Assert.That(_http.Requests.Length, Is.EqualTo(1));
                Assert.That(_http.Requests[0].Uri.AbsolutePath, Does.EndWith("/chart"));
                Assert.That(_cache.FindFile("maidata.txt")!.ReadAllBytes(), Is.EqualTo(s_chart));
                Assert.That(_cache.FindFile("maidata.txt.sha256")!.ReadAllText(), Is.EqualTo(Digest(s_chart)));
                Assert.That(_cache.FindFile("maidata.txt.chunk"), Is.Null);
            });
        }

        /// <summary>Checks that a cached chart of another hash is downloaded again.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetMaidataAsync_RedownloadsOutdatedChart()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _cache.CreateFile("maidata.txt").WriteAllText(ChartFixture.CreateMaidata("Outdated"));
                _cache.CreateFile("maidata.txt.sha256").WriteAllText("stale");
                _http.Responder = _ => Bytes(HttpStatusCode.OK, s_chart);
                using var song = CreateSong();

                var chart = await song.GetMaidataAsync();

                Assert.That(chart.Title, Is.EqualTo("Online"));
                Assert.That(_http.Requests.Length, Is.EqualTo(1));
                Assert.That(_cache.FindFile("maidata.txt.sha256"), Is.Null);
            });
        }

        /// <summary>Checks that a cached chart with a matching digest is used without a request.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetMaidataAsync_UsesVerifiedCache()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _cache.CreateFile("maidata.txt").WriteAllBytes(s_chart);
                using var song = CreateSong();

                var chart = await song.GetMaidataAsync();

                Assert.That(chart.Title, Is.EqualTo("Online"));
                Assert.That(_http.Requests, Is.Empty);
            });
        }

        /// <summary>Checks that a complete chunk verified by its stored digest is promoted without a request.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetMaidataAsync_PromotesVerifiedChunk()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _cache.CreateFile("maidata.txt.chunk").WriteAllBytes(s_chart);
                _cache.CreateFile("maidata.txt.sha256").WriteAllText(Digest(s_chart));
                using var song = CreateSong();

                var chart = await song.GetMaidataAsync();

                Assert.That(chart.Title, Is.EqualTo("Online"));
                Assert.That(_http.Requests, Is.Empty);
                Assert.That(_cache.FindFile("maidata.txt"), Is.Not.Null);
            });
        }

        /// <summary>Checks that a partial chunk is resumed with a range request.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetMaidataAsync_ResumesPartialChunk()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var head = s_chart.Take(10).ToArray();
                var tail = s_chart.Skip(10).ToArray();
                _cache.CreateFile("maidata.txt.chunk").WriteAllBytes(head);
                _cache.CreateFile("maidata.txt.sha256").WriteAllText(Digest(s_chart));
                var range = default(string);
                _http.Responder = request =>
                {
                    range = request.Headers.Range?.ToString();
                    return Bytes(HttpStatusCode.PartialContent, tail);
                };
                using var song = CreateSong();

                var chart = await song.GetMaidataAsync();

                Assert.That(chart.Title, Is.EqualTo("Online"));
                Assert.That(range, Is.EqualTo("bytes=10-"));
                Assert.That(_cache.FindFile("maidata.txt")!.ReadAllBytes(), Is.EqualTo(s_chart));
            });
        }

        /// <summary>Checks that an unsatisfiable range resets the chunk and that a server ignoring ranges restarts the download.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetMaidataAsync_RestartsInvalidChunks()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _cache.CreateFile("maidata.txt.chunk").WriteAllBytes(Encoding.UTF8.GetBytes("garbage"));
                var calls = 0;
                _http.Responder = request =>
                {
                    calls++;
                    return calls == 1 ? Bytes(HttpStatusCode.RequestedRangeNotSatisfiable, Array.Empty<byte>()) : Bytes(HttpStatusCode.OK, s_chart);
                };
                using var first = CreateSong();
                Assert.That((await first.GetMaidataAsync()).Title, Is.EqualTo("Online"));
                Assert.That(calls, Is.EqualTo(2));

                _cache.FindFile("maidata.txt")!.Delete();
                _cache.CreateFile("maidata.txt.chunk").WriteAllBytes(Encoding.UTF8.GetBytes("garbage"));
                _http.Responder = _ => Bytes(HttpStatusCode.OK, s_chart);
                using var second = CreateSong();
                Assert.That((await second.GetMaidataAsync()).Title, Is.EqualTo("Online"));
                Assert.That(_cache.FindFile("maidata.txt")!.ReadAllBytes(), Is.EqualTo(s_chart));
            });
        }

        /// <summary>Checks that a download failing its integrity check is retried and finally reported.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetMaidataAsync_ReportsIntegrityFailure()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.Responder = _ => Bytes(HttpStatusCode.OK, s_chart, Digest(Encoding.UTF8.GetBytes("other")));
                using var song = CreateSong();

                var failure = await CatchAsync(async () => await song.GetMaidataAsync());

                Assert.That(failure, Is.InstanceOf<HttpException>());
                Assert.That(((HttpException)failure!).ErrorCode, Is.EqualTo(HttpErrorCode.IntegrityCheckFailed));
                Assert.That(_http.Requests.Length, Is.EqualTo(MajEnv.HTTP_REQUEST_MAX_RETRY + 1));
                Assert.That(song.IsMaidataLoaded, Is.False);
            });
        }

        /// <summary>Checks that unsuccessful responses and transport failures are reported as HTTP errors.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetMaidataAsync_ReportsHttpFailures()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var song = CreateSong();
                _http.Responder = _ => Bytes(HttpStatusCode.InternalServerError, Array.Empty<byte>());
                var unsuccessful = await CatchAsync(async () => await song.GetMaidataAsync());
                Assert.That(((HttpException)unsuccessful!).ErrorCode, Is.EqualTo(HttpErrorCode.Unsuccessful));
                Assert.That(_http.Requests.Length, Is.EqualTo(1));

                _http.Responder = _ => throw new HttpRequestException("offline");
                var unreachable = await CatchAsync(async () => await song.GetMaidataAsync());
                Assert.That(((HttpException)unreachable!).ErrorCode, Is.EqualTo(HttpErrorCode.Unreachable));

                _http.Responder = _ => throw new InvalidOperationException("bad request");
                var invalid = await CatchAsync(async () => await song.GetMaidataAsync());
                Assert.That(((HttpException)invalid!).ErrorCode, Is.EqualTo(HttpErrorCode.InvalidRequest));

                _http.Responder = _ => Bytes(HttpStatusCode.NotFound, Array.Empty<byte>());
                var missing = await CatchAsync(async () => await song.GetMaidataAsync());
                Assert.That(missing, Is.InstanceOf<Exception>());
                Assert.That(missing!.Message, Is.EqualTo("Maidata Load Failed"));
            });
        }

        /// <summary>Checks that a canceled chart request is reported as cancellation.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetMaidataAsync_Canceled()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var song = CreateSong();
                using var canceled = new System.Threading.CancellationTokenSource();
                canceled.Cancel();

                var failure = await CatchAsync(async () => await song.GetMaidataAsync(token: canceled.Token));

                Assert.That(failure, Is.InstanceOf<OperationCanceledException>());
            });
        }

        /// <summary>Checks that a downloaded video is cached and that a missing video is remembered with a flag file.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetVideoPathAsync_DownloadsOrRemembersMissingVideo()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var video = new byte[] { 1, 2, 3, 4 };
                _http.Responder = _ => Bytes(HttpStatusCode.OK, video);
                using (var song = CreateSong())
                {
                    var path = await song.GetVideoPathAsync();
                    Assert.That(Path.GetFileName(path), Is.EqualTo("bg.mp4"));
                    Assert.That(await song.GetVideoPathAsync(), Is.EqualTo(path));
                    Assert.That(song.IsVideoLoaded, Is.True);
                    Assert.That(_http.Requests[0].Uri.AbsolutePath, Does.EndWith("/video"));
                }

                _cache.FindFile("bg.mp4")!.Delete();
                _http.Responder = _ => Bytes(HttpStatusCode.NotFound, Array.Empty<byte>());
                using (var song = CreateSong())
                {
                    Assert.That(await song.GetVideoPathAsync(), Is.Empty);
                    Assert.That(_cache.FindFile("bg.mp4.cache"), Is.Not.Null);
                }

                var before = _http.Requests.Length;
                using (var song = CreateSong())
                {
                    Assert.That(await song.GetVideoPathAsync(), Is.Empty);
                    Assert.That(_http.Requests.Length, Is.EqualTo(before));
                }
            });
        }

        /// <summary>Checks that a cached supported video is used without a request.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetVideoPathAsync_UsesCachedVideo()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _cache.CreateFile("PV.MP4").WriteAllBytes(new byte[] { 9 });
                using var song = CreateSong();

                var path = await song.GetVideoPathAsync();

                Assert.That(Path.GetFileName(path), Is.EqualTo("PV.MP4"));
                Assert.That(_http.Requests, Is.Empty);
            });
        }

        /// <summary>Checks that downloading the track without decoding stores it and returns the empty sample.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DownloadTrack_StoresTrack()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var track = new byte[] { 5, 6, 7 };
                _http.Responder = _ => Bytes(HttpStatusCode.OK, track);
                using var song = CreateSong();

                var sample = await (System.Threading.Tasks.ValueTask<AudioSampleWrap>)PrivateMember.Invoke(song, "GetAudioTrackAsync", false, null, default(System.Threading.CancellationToken))!;

                Assert.That(sample, Is.SameAs(AudioSampleWrap.Empty));
                Assert.That(_cache.FindFile("track.mp3")!.ReadAllBytes(), Is.EqualTo(track));
                Assert.That(_http.Requests[0].Uri.AbsolutePath, Does.EndWith("/track"));
            });
        }

        /// <summary>Checks that covers fall back to the runtime's empty cover when they are not loaded into memory or cannot be downloaded.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Covers_FallBackToEmptyCover()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var wasNull = Majdata<GameRuntime>.IsNull;
                GameRuntime.Init();
                try
                {
                    _http.Responder = request => request.RequestUri!.AbsolutePath.EndsWith("/chart")
                        ? Bytes(HttpStatusCode.OK, s_chart)
                        : Bytes(HttpStatusCode.OK, new byte[] { 1, 2 });
                    using var song = CreateSong();
                    await song.PreloadAsync();
                    await song.PreloadAsync();
                    await UniTask.SwitchToMainThread();
                    Assert.That(song.IsMaidataLoaded, Is.True);
                    Assert.That(_cache.FindFile("bg.jpg"), Is.Not.Null);

                    _http.Responder = _ => Bytes(HttpStatusCode.NotFound, Array.Empty<byte>());
                    var fullSize = await song.GetCoverAsync(false);
                    await UniTask.SwitchToMainThread();
                    Assert.That(fullSize, Is.SameAs(GameRuntime.Instance.Sprite.EmptySongCover));

                    _http.Responder = _ => Bytes(HttpStatusCode.BadGateway, Array.Empty<byte>());
                    var failed = await song.GetCoverAsync(false);
                    await UniTask.SwitchToMainThread();
                    Assert.That(failed, Is.SameAs(GameRuntime.Instance.Sprite.EmptySongCover));
                }
                finally
                {
                    await UniTask.SwitchToMainThread();
                    if (wasNull)
                    {
                        Majdata<GameRuntime>.Free();
                    }
                }
            });
        }

        /// <summary>Checks that a disposed song rejects every resource request and that disposal is idempotent.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Dispose_RejectsFurtherUse()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var song = CreateSong();
                var asyncSong = CreateSong();

                song.Dispose();
                song.Dispose();
                await asyncSong.DisposeAsync();
                await asyncSong.DisposeAsync();

                Assert.Throws<ObjectDisposedException>(() => song.GetCoverAsync(true));
                Assert.That(await CatchAsync(async () => await song.PreloadAsync()), Is.InstanceOf<ObjectDisposedException>());
                Assert.That(await CatchAsync(async () => await song.GetMaidataAsync()), Is.InstanceOf<ObjectDisposedException>());
                Assert.That(await CatchAsync(async () => await song.GetVideoPathAsync()), Is.InstanceOf<ObjectDisposedException>());
                Assert.That(await CatchAsync(async () => await song.GetAudioTrackAsync()), Is.InstanceOf<ObjectDisposedException>());
                Assert.That(await CatchAsync(async () => await song.GetPreviewAudioTrackAsync()), Is.InstanceOf<ObjectDisposedException>());
                Assert.That(await CatchAsync(async () => await asyncSong.GetMaidataAsync()), Is.InstanceOf<ObjectDisposedException>());
            });
        }
    }
}
