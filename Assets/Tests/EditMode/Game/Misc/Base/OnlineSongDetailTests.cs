using System;
using System.Collections;
using System.Collections.Generic;
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
    /// Covers how <see cref="OnlineSongDetail"/> finds and downloads its files through its <see cref="OnlineChartRecord"/>
    /// and the <see cref="ObjectStore"/>: chart, video, track and cover downloads, digests from the info API, digests learned
    /// from downloads, files shared between charts, and failure handling. Every request is answered by a stub HTTP client,
    /// and the runtime database and object store live in temporary storage.
    /// </summary>
    public class OnlineSongDetailTests
    {
        /// <summary>The chart served by the stub endpoint.</summary>
        private static readonly byte[] s_chart = Encoding.UTF8.GetBytes(ChartFixture.CreateMaidata("Online"));

        /// <summary>The stub HTTP clients.</summary>
        private StubHttpScope _http = null!;

        /// <summary>The temporary storage.</summary>
        private TemporaryDirectory _temporary = null!;

        /// <summary>The scope restoring the application directories and closing the databases opened by a test.</summary>
        private MajEnvScope _environment = null!;

        /// <summary>The cache holding the song records, bound to the temporary layout.</summary>
        private OnlineChartCache _cache = null!;

        /// <summary>The endpoint serving the song.</summary>
        private ApiEndpoint _endpoint = null!;

        /// <summary>Installs the stub client and an empty runtime database and object store.</summary>
        [SetUp]
        public void SetUp()
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(MajEnv).TypeHandle);
            _http = new StubHttpScope();
            _temporary = new TemporaryDirectory();
            _environment = new MajEnvScope();
            _environment.UseTemporaryLayout(_temporary);
            _cache = OnlineChartCache.Shared;
            _endpoint = new ApiEndpoint { Name = "Server", Url = new Uri("https://api.invalid/api/") };
        }

        /// <summary>Restores the HTTP clients and the environment, and deletes the temporary storage.</summary>
        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            _http.Dispose();
            _environment.Dispose();
            _temporary.Dispose();
        }

        /// <summary>Gets the shared object store of the temporary layout.</summary>
        private static ObjectStore Objects => ObjectStore.Shared;

        /// <summary>Computes the Simai hash of chart bytes.</summary>
        /// <param name="bytes">The chart bytes.</param>
        /// <returns>The chart hash.</returns>
        private static string ChartHash(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes);
            return SimaiParser.ParseMetadata(stream).Hash;
        }

        /// <summary>Computes the lowercase hexadecimal SHA-256 digest of bytes.</summary>
        /// <param name="bytes">The bytes.</param>
        /// <returns>The digest.</returns>
        private static string Sha256(byte[] bytes)
        {
            using var sha256 = SHA256.Create();
            return ObjectStore.ToHex(sha256.ComputeHash(bytes));
        }

        /// <summary>Computes the Base64 SHA-256 digest the server sends in the hash header.</summary>
        /// <param name="bytes">The resource bytes.</param>
        /// <returns>The digest.</returns>
        private static string HashHeader(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(bytes));
        }

        /// <summary>Creates the info API's description of a file.</summary>
        /// <param name="bytes">The file content.</param>
        /// <param name="name">The file name in the direct URI.</param>
        /// <returns>The file description.</returns>
        private static MajNetChartResource Resource(byte[] bytes, string name)
        {
            using var md5 = MD5.Create();
            return new MajNetChartResource
            {
                Url = new Uri("https://cdn.invalid/MaiCharts/" + name),
                Sha256 = Sha256(bytes),
                Md5 = ObjectStore.ToHex(md5.ComputeHash(bytes)),
                Length = bytes.Length,
            };
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

        /// <summary>Creates song metadata.</summary>
        /// <param name="hash">The chart hash announced by the server, or null for the hash of the served chart.</param>
        /// <param name="id">The chart identity, or null for a new one.</param>
        /// <returns>The metadata.</returns>
        private static MajnetSongDetail Detail(string? hash = null, string? id = null)
        {
            return new MajnetSongDetail
            {
                Id = id ?? Guid.NewGuid().ToString(),
                Title = "Online",
                Artist = "Artist",
                Description = "Description",
                Designer = "designer",
                Uploader = "uploader",
                Hash = hash ?? ChartHash(s_chart),
                Levels = new[] { "1", "2", "3", "4", "5", "6", "7", "8" },
                Tags = new[] { "tag" },
                PublicTags = new[] { "public" },
                Timestamp = new DateTime(2024, 1, 2)
            };
        }

        /// <summary>Creates a song whose record only lives in memory and knows no digests.</summary>
        /// <param name="hash">The chart hash announced by the server, or null for the hash of the served chart.</param>
        /// <returns>The song.</returns>
        private OnlineSongDetail CreateSong(string? hash = null)
        {
            var detail = Detail(hash);
            return new OnlineSongDetail(_endpoint, detail, _cache.CreateTransientRecord(_endpoint, detail));
        }

        /// <summary>Creates a song whose record is synchronized into the runtime database, with the info API's files when given.</summary>
        /// <param name="resources">The files reported by the info API, or null when the info is not known.</param>
        /// <param name="id">The chart identity, or null for a new one.</param>
        /// <returns>The song.</returns>
        private async UniTask<OnlineSongDetail> CreateSyncedSongAsync(MajNetChartResources? resources, string? id = null)
        {
            var detail = Detail(id: id);
            var infos = resources is null
                ? null
                : new Dictionary<string, MajNetChartInfo> { [detail.Id] = new MajNetChartInfo { Metadata = detail, Resources = resources } };
            var records = await _cache.SyncAsync(_endpoint, new[] { detail }, infos);
            return new OnlineSongDetail(_endpoint, detail, records[detail.Id]);
        }

        /// <summary>Reads one column of a chart's row after the queued writes have finished.</summary>
        /// <param name="column">The column name.</param>
        /// <param name="chartId">The chart identity.</param>
        /// <returns>The column value.</returns>
        private static async UniTask<string?> ReadColumnAsync(string column, string chartId)
        {
            var database = MajDatabase.Runtime;
            await database.WaitForWritesAsync();
            return database.Read(connection => connection.ExecuteScalar<string?>($"SELECT {column} FROM OnlineChartCache WHERE ChartId = ?", chartId));
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
            Assert.That(song.Description, Is.EqualTo("Description"));
            Assert.That(song.Tags.ToArray(), Is.EqualTo(new[] { "tag", "public" }));
            Assert.That(song.Record.ChartId, Is.EqualTo(song.Id));
            Assert.That(song.IsMaidataLoaded, Is.False);
            Assert.That(song.IsVideoLoaded, Is.False);
            Assert.That(song.IsCoverLoaded, Is.False);
            Assert.That(song.IsCompressedCoverLoaded, Is.False);
            Assert.That(song.IsAudioTrackLoaded, Is.False);
            Assert.That(song.IsPreviewAudioTrackLoaded, Is.False);
            var detail = Detail();
            var record = _cache.CreateTransientRecord(_endpoint, detail);
            Assert.Throws<ArgumentNullException>(() => new OnlineSongDetail(null!, detail, record));
            Assert.Throws<ArgumentNullException>(() => new OnlineSongDetail(_endpoint, null!, record));
            Assert.Throws<ArgumentNullException>(() => new OnlineSongDetail(_endpoint, detail, null!));
        }

        /// <summary>
        /// Checks that a chart without a recorded digest is downloaded from the chart endpoint, stored under its digest,
        /// recorded in the runtime database, cached in memory and read again from the store.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetMaidataAsync_DownloadsAndRecordsChart()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.Responder = _ => Bytes(HttpStatusCode.OK, s_chart, HashHeader(s_chart));
                using var song = await CreateSyncedSongAsync(null);
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
                Assert.That(Objects.Find(Sha256(s_chart))!.ReadAllBytes(), Is.EqualTo(s_chart));
                Assert.That(song.Record.TryGetDigest(OnlineChartResourceKind.Maidata, out var digest), Is.True);
                Assert.That(digest.Sha256, Is.EqualTo(Sha256(s_chart)));
                Assert.That(await ReadColumnAsync("MaidataSha256", song.Id), Is.EqualTo(Sha256(s_chart)));
            });
        }

        /// <summary>Checks that a stored chart named by the recorded digest is used without a request.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetMaidataAsync_UsesStoredObject()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var maidata = Resource(s_chart, "1/maidata.txt");
                var digest = maidata.Sha256;
                Objects.Directory.CreateDirectory(digest.Substring(0, 2)).CreateFile(digest).WriteAllBytes(s_chart);
                using var song = await CreateSyncedSongAsync(new MajNetChartResources { Maidata = maidata });

                var chart = await song.GetMaidataAsync();

                Assert.That(chart.Title, Is.EqualTo("Online"));
                Assert.That(_http.Requests, Is.Empty);
            });
        }

        /// <summary>
        /// Checks that a resolved chart is still downloaded from the chart endpoint, which counts the play, and verified
        /// against the info API's digest.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetMaidataAsync_DownloadsResolvedChartFromChartEndpoint()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.Responder = _ => Bytes(HttpStatusCode.OK, s_chart);
                using var song = await CreateSyncedSongAsync(new MajNetChartResources { Maidata = Resource(s_chart, "1/maidata.txt") });

                var chart = await song.GetMaidataAsync();

                Assert.That(chart.Title, Is.EqualTo("Online"));
                Assert.That(_http.Requests.Single().Uri.AbsolutePath, Does.EndWith("/chart"));
                Assert.That(Objects.Contains(Sha256(s_chart)), Is.True);
            });
        }

        /// <summary>
        /// Checks that a recorded digest the server's file no longer matches is replaced by the digest of the endpoint's
        /// current file.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetMaidataAsync_ReplacesOutdatedDigest()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var outdated = Resource(Encoding.UTF8.GetBytes(ChartFixture.CreateMaidata("Outdated")), "1/maidata.txt");
                _http.Responder = _ => Bytes(HttpStatusCode.OK, s_chart);
                using var song = await CreateSyncedSongAsync(new MajNetChartResources { Maidata = outdated });

                var chart = await song.GetMaidataAsync();

                Assert.That(chart.Title, Is.EqualTo("Online"));
                Assert.That(_http.Requests.Length, Is.EqualTo(MajEnv.HTTP_REQUEST_MAX_RETRY + 2));
                Assert.That(song.Record.TryGetDigest(OnlineChartResourceKind.Maidata, out var digest), Is.True);
                Assert.That(digest.Sha256, Is.EqualTo(Sha256(s_chart)));
                Assert.That(await ReadColumnAsync("MaidataSha256", song.Id), Is.EqualTo(Sha256(s_chart)));
            });
        }

        /// <summary>Checks that a download failing its hash header is retried and finally reported.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetMaidataAsync_ReportsIntegrityFailure()
        {
            return UniTask.ToCoroutine(async () =>
            {
                LogAssert.ignoreFailingMessages = true;
                _http.Responder = _ => Bytes(HttpStatusCode.OK, s_chart, HashHeader(Encoding.UTF8.GetBytes("other")));
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
                LogAssert.ignoreFailingMessages = true;
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

        /// <summary>
        /// Checks that a video without a recorded digest is downloaded from the endpoint and stored, that a missing video
        /// is reported empty, and that a video the info API reports missing is not requested.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetVideoPathAsync_DownloadsOrReportsMissingVideo()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var video = new byte[] { 1, 2, 3, 4 };
                _http.Responder = _ => Bytes(HttpStatusCode.OK, video);
                using (var song = CreateSong())
                {
                    var path = await song.GetVideoPathAsync();
                    Assert.That(path, Is.EqualTo(Objects.Find(Sha256(video))!.Location));
                    Assert.That(await song.GetVideoPathAsync(), Is.EqualTo(path));
                    Assert.That(song.IsVideoLoaded, Is.True);
                    Assert.That(_http.Requests[0].Uri.AbsolutePath, Does.EndWith("/video"));
                }

                _http.Responder = _ => Bytes(HttpStatusCode.NotFound, Array.Empty<byte>());
                using (var song = CreateSong())
                {
                    Assert.That(await song.GetVideoPathAsync(), Is.Empty);
                }

                var before = _http.Requests.Length;
                using (var song = await CreateSyncedSongAsync(new MajNetChartResources { Maidata = Resource(s_chart, "1/maidata.txt") }))
                {
                    Assert.That(await song.GetVideoPathAsync(), Is.Empty);
                    Assert.That(_http.Requests.Length, Is.EqualTo(before));
                }
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

                var sample = await DownloadTrackAsync(song);

                Assert.That(sample, Is.SameAs(AudioSampleWrap.Empty));
                Assert.That(Objects.Find(Sha256(track))!.ReadAllBytes(), Is.EqualTo(track));
                Assert.That(_http.Requests[0].Uri.AbsolutePath, Does.EndWith("/track"));
            });
        }

        /// <summary>
        /// Checks that a resolved track is downloaded from the info API's direct URI, and that another chart or endpoint
        /// with the same track reuses the stored file without a request.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DownloadTrack_SharesStoredFileAcrossCharts()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var track = new byte[] { 5, 6, 7 };
                var resource = Resource(track, "1/track.mp3");
                _http.Responder = _ => Bytes(HttpStatusCode.OK, track);
                using var first = await CreateSyncedSongAsync(new MajNetChartResources { Track = resource });
                using var second = await CreateSyncedSongAsync(new MajNetChartResources { Track = resource });

                await DownloadTrackAsync(first);
                await DownloadTrackAsync(second);

                Assert.That(_http.Requests.Single().Uri, Is.EqualTo(resource.Url));
                Assert.That(Objects.Directory.FindDirectory(resource.Sha256.Substring(0, 2))!.EnumerateFiles().Count(), Is.EqualTo(1));
            });
        }

        /// <summary>Downloads a song's track without decoding it.</summary>
        /// <param name="song">The song.</param>
        /// <returns>The returned sample.</returns>
        private static async UniTask<AudioSampleWrap> DownloadTrackAsync(OnlineSongDetail song)
        {
            return await (System.Threading.Tasks.ValueTask<AudioSampleWrap>)PrivateMember.Invoke(song, "GetAudioTrackAsync", false, null, default(System.Threading.CancellationToken))!;
        }

        /// <summary>Checks that covers fall back to the runtime's empty cover when they are not loaded into memory or cannot be downloaded.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Covers_FallBackToEmptyCover()
        {
            return UniTask.ToCoroutine(async () =>
            {
                LogAssert.ignoreFailingMessages = true;
                var wasNull = Majdata<GameRuntime>.IsNull;
                GameRuntime.Init();
                try
                {
                    var thumbnail = new byte[] { 1, 2 };
                    _http.Responder = request => request.RequestUri!.AbsolutePath.EndsWith("/chart")
                        ? Bytes(HttpStatusCode.OK, s_chart)
                        : Bytes(HttpStatusCode.OK, thumbnail);
                    using var song = CreateSong();
                    await song.PreloadAsync();
                    await song.PreloadAsync();
                    await UniTask.SwitchToMainThread();
                    Assert.That(song.IsMaidataLoaded, Is.True);
                    Assert.That(Objects.Contains(Sha256(thumbnail)), Is.True);

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

        /// <summary>
        /// Checks that compressed and full-size covers are kept in their own caches and destroyed when the song is disposed.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Covers_AreCachedPerSizeUntilDisposed()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var png = CreatePng();
                _http.Responder = _ => Bytes(HttpStatusCode.OK, png);
                var song = CreateSong();

                var compressed = await song.GetCoverAsync(true);
                var compressedAgain = await song.GetCoverAsync(true);
                var fullSize = await song.GetCoverAsync(false);
                await UniTask.SwitchToMainThread();

                Assert.That(compressed != null, Is.True);
                Assert.That(compressedAgain, Is.SameAs(compressed));
                Assert.That(fullSize != null, Is.True);
                Assert.That(fullSize, Is.Not.SameAs(compressed));
                Assert.That(song.IsCompressedCoverLoaded, Is.True);
                Assert.That(song.IsCoverLoaded, Is.True);
                Assert.That(_http.Requests.Count, Is.EqualTo(2));

                song.Dispose();

                Assert.That(compressed == null, Is.True);
                Assert.That(fullSize == null, Is.True);
                Assert.That(song.IsCompressedCoverLoaded, Is.False);
                Assert.That(song.IsCoverLoaded, Is.False);
            });
        }

        /// <summary>Checks that the compressed cover falls back to the original cover when the info API reports no thumbnail.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Covers_UseOriginalCoverWithoutThumbnail()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var png = CreatePng();
                var coverArt = Resource(png, "1/bg.png");
                _http.Responder = _ => Bytes(HttpStatusCode.OK, png);
                using var song = await CreateSyncedSongAsync(new MajNetChartResources { CoverArt = coverArt });

                var cover = await song.GetCoverAsync(true);
                await UniTask.SwitchToMainThread();

                Assert.That(cover != null, Is.True);
                Assert.That(_http.Requests.Single().Uri, Is.EqualTo(coverArt.Url));
            });
        }

        /// <summary>Encodes a small PNG image.</summary>
        /// <returns>The PNG bytes.</returns>
        private static byte[] CreatePng()
        {
            var texture = new UnityEngine.Texture2D(4, 4);
            var png = UnityEngine.ImageConversion.EncodeToPNG(texture);
            UnityEngine.Object.DestroyImmediate(texture);
            return png;
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
