using System;
using System.Collections;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using MajdataPlay.Databases;
using MajdataPlay.Drawing;
using MajdataPlay.IO.Storage;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Misc.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Base
{
    /// <summary>Covers metadata parsing, chart caching and lifetime rules of the local song <see cref="SongDetail"/>.</summary>
    public class SongDetailTests
    {
        /// <summary>The temporary chart storage.</summary>
        private TemporaryDirectory _temporary = null!;

        /// <summary>Creates the temporary storage.</summary>
        [SetUp]
        public void SetUp()
        {
            // Initialize the environment on the main thread before workers touch it, so its main thread is recorded correctly.
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(MajEnv).TypeHandle);
            _temporary = new TemporaryDirectory();
        }

        /// <summary>Deletes the temporary storage.</summary>
        [TearDown]
        public void TearDown()
        {
            _temporary.Dispose();
        }

        /// <summary>Checks that the metadata of a parsed chart is exposed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ParseAsync_ReadsMetadata()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var directory = ChartFixture.CreateChart(_temporary.Directory, "Chart", "Title", videoName: "PV.MP4");

                using var song = await SongDetail.ParseAsync(directory);

                Assert.That(song.Title, Is.EqualTo("Title"));
                Assert.That(song.Artist, Is.EqualTo("Artist"));
                Assert.That(song.Levels[4], Is.EqualTo("13"));
                Assert.That(song.Designers[4], Is.EqualTo("Designer"));
                Assert.That(song.Hash, Is.Not.Empty);
                Assert.That(song.Location, Is.EqualTo(ChartStorageLocation.Local));
                Assert.That(((ISongDetail)song).IsOnline, Is.False);
                Assert.That(song.Description, Is.Empty);
                Assert.That(song.Timestamp, Is.GreaterThan(DateTime.UnixEpoch));
                Assert.That(song.IsMaidataLoaded, Is.False);
                Assert.That(song.IsCoverLoaded, Is.False);
                Assert.That(song.IsCompressedCoverLoaded, Is.False);
                Assert.That(song.IsAudioTrackLoaded, Is.False);
                Assert.That(song.IsPreviewAudioTrackLoaded, Is.False);
                Assert.That(song.IsVideoLoaded, Is.True);
                var video = await song.GetVideoPathAsync();
                Assert.That(Path.GetFileName(video), Is.EqualTo("PV.MP4"));
            });
        }

        /// <summary>Checks that a chart's identity is generated into its hidden marker file and reused by later scans.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ParseAsync_PersistsIdentity()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var directory = ChartFixture.CreateChart(_temporary.Directory, "Chart", "Title");

                using var first = await SongDetail.ParseAsync(directory);
                using var second = await SongDetail.ParseAsync(directory);

                Assert.That(Guid.TryParse(first.Id, out _), Is.True);
                Assert.That(second.Id, Is.EqualTo(first.Id));
                var idFile = directory.FindFileAt(".MajdataPlay/id")!;
                Assert.That(idFile.ReadAllText(), Is.EqualTo(first.Id));
                Assert.That(directory.FindEntry(".MajdataPlay")!.IsHidden, Is.True);
            });
        }

        /// <summary>Checks that the path overload opens the directory through the storage facade.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ParseAsync_ByPath()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var directory = ChartFixture.CreateChart(_temporary.Directory, "Chart", "ByPath");

                using var song = await SongDetail.ParseAsync(directory.Location);

                Assert.That(song.Title, Is.EqualTo("ByPath"));
                Assert.That(await song.GetVideoPathAsync(), Is.Empty);
            });
        }

        /// <summary>Checks that the chart and its track are required and that cancellation is observed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ParseAsync_RejectsIncompleteCharts()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var noChart = ChartFixture.CreateChart(_temporary.Directory, "NoChart", "x", withMaidata: false);
                var noTrack = ChartFixture.CreateChart(_temporary.Directory, "NoTrack", "y", trackName: null);
                var valid = ChartFixture.CreateChart(_temporary.Directory, "Valid", "z");
                using var canceled = new CancellationTokenSource();
                canceled.Cancel();

                await AssertThrowsAsync<FileNotFoundException>(() => SongDetail.ParseAsync(noChart));
                await AssertThrowsAsync<FileNotFoundException>(() => SongDetail.ParseAsync(noTrack));
                await AssertThrowsAsync<OperationCanceledException>(() => SongDetail.ParseAsync(valid, canceled.Token));
                Assert.Throws<OperationCanceledException>(() => SongDetail.ParseAsync(valid.Location, canceled.Token));
                Assert.Throws<ArgumentNullException>(() => new SongDetail(null!, default));
                await AssertThrowsAsync<ArgumentNullException>(() => SongDetail.ParseAsync((MajdataPlay.IO.Storage.StorageDirectory)null!));
            });
        }

        /// <summary>Checks that the parsed chart is cached and re-read when the file changes.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetMaidataAsync_CachesAndRevalidates()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var directory = ChartFixture.CreateChart(_temporary.Directory, "Chart", "Before");
                using var song = await SongDetail.ParseAsync(directory);

                var first = await song.GetMaidataAsync();
                var cached = await song.GetMaidataAsync();
                var unchanged = await song.GetMaidataAsync(ignoreCache: true);
                Assert.That(cached, Is.SameAs(first));
                Assert.That(unchanged, Is.SameAs(first));
                Assert.That(first.Title, Is.EqualTo("Before"));
                Assert.That(song.IsMaidataLoaded, Is.True);
                var oldHash = song.Hash;

                directory.FindFile("maidata.txt")!.WriteAllText(ChartFixture.CreateMaidata("After"));
                var reread = await song.GetMaidataAsync(ignoreCache: true);

                Assert.That(reread, Is.Not.SameAs(first));
                Assert.That(reread.Title, Is.EqualTo("After"));
                Assert.That(song.Hash, Is.Not.EqualTo(oldHash));
            });
        }

        /// <summary>Checks that a missing cover falls back to the runtime's empty song cover and that preloading succeeds.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetCoverAsync_WithoutCover_UsesEmptyCover()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var wasNull = Majdata<GameRuntime>.IsNull;
                GameRuntime.Init();
                try
                {
                    var directory = ChartFixture.CreateChart(_temporary.Directory, "Chart", "NoCover");
                    using var song = await SongDetail.ParseAsync(directory);

                    var cover = await song.GetCoverAsync(true);
                    await song.PreloadAsync();
                    await song.PreloadAsync();
                    await UniTask.SwitchToMainThread();

                    Assert.That(cover, Is.SameAs(GameRuntime.Instance.Sprite.EmptySongCover));
                    Assert.That(song.IsMaidataLoaded, Is.True);
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

        /// <summary>Writes a decodable PNG cover into a chart directory.</summary>
        /// <param name="directory">The chart directory.</param>
        private static void WriteCover(StorageDirectory directory)
        {
            var texture = new Texture2D(4, 4);
            try
            {
                directory.CreateFile("bg.png").WriteAllBytes(texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>Checks that a loaded cover is kept in <see cref="Cache.Sprite"/> and destroyed when the song is disposed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetCoverAsync_CachesCoverUntilDisposed()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var directory = ChartFixture.CreateChart(_temporary.Directory, "Chart", "Cover");
                WriteCover(directory);
                var song = await SongDetail.ParseAsync(directory);

                var cover = await song.GetCoverAsync(true);
                var cached = await song.GetCoverAsync(false);
                await UniTask.SwitchToMainThread();

                Assert.That(cover != null, Is.True);
                Assert.That(cover, Is.Not.SameAs(SpriteLoader.EmptySprite));
                Assert.That(cached, Is.SameAs(cover));
                Assert.That(song.IsCompressedCoverLoaded, Is.True);
                Assert.That(song.IsCoverLoaded, Is.True);
                var texture = cover.texture;

                song.Dispose();

                Assert.That(cover == null, Is.True);
                Assert.That(texture == null, Is.True);
                Assert.That(song.IsCompressedCoverLoaded, Is.False);
            });
        }

        /// <summary>Checks that asynchronous disposal destroys the cover before it completes.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DisposeAsync_DestroysCover()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var directory = ChartFixture.CreateChart(_temporary.Directory, "Chart", "Cover");
                WriteCover(directory);
                var song = await SongDetail.ParseAsync(directory);
                var cover = await song.GetCoverAsync(true);

                await song.DisposeAsync();
                await UniTask.SwitchToMainThread();

                Assert.That(cover == null, Is.True);
                Assert.That(song.IsCompressedCoverLoaded, Is.False);
            });
        }

        /// <summary>Checks that a cover pushed out of the cache by newer sprites is destroyed and loaded again on demand.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetCoverAsync_ReloadsEvictedCover()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var directory = ChartFixture.CreateChart(_temporary.Directory, "Chart", "Cover");
                WriteCover(directory);
                using var song = await SongDetail.ParseAsync(directory);
                var fillerScope = Cache.CreateKeyScope(nameof(SongDetailTests));
                try
                {
                    var evicted = await song.GetCoverAsync(true);
                    await UniTask.SwitchToMainThread();

                    for (var i = 0; i < Cache.SpriteCapacity; i++)
                    {
                        var texture = new Texture2D(2, 2);
                        Cache.Sprite.Set($"{fillerScope}/{i}", Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero));
                    }

                    Assert.That(evicted == null, Is.True);
                    Assert.That(song.IsCompressedCoverLoaded, Is.False);

                    var reloaded = await song.GetCoverAsync(true);
                    await UniTask.SwitchToMainThread();

                    Assert.That(reloaded != null, Is.True);
                    Assert.That(song.IsCompressedCoverLoaded, Is.True);
                }
                finally
                {
                    await UniTask.SwitchToMainThread();
                    for (var i = 0; i < Cache.SpriteCapacity; i++)
                    {
                        Cache.Sprite.Remove($"{fillerScope}/{i}");
                    }
                }
            });
        }

        /// <summary>Checks that the shared empty sprite returned for an undecodable cover is not cached, so it is never destroyed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetCoverAsync_UndecodableCover_IsNotCached()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // The loader logs the decoding failure.
                LogAssert.ignoreFailingMessages = true;
                try
                {
                    var directory = ChartFixture.CreateChart(_temporary.Directory, "Chart", "BrokenCover", coverName: "bg.png");
                    var song = await SongDetail.ParseAsync(directory);

                    var cover = await song.GetCoverAsync(true);
                    await UniTask.SwitchToMainThread();

                    Assert.That(cover, Is.SameAs(SpriteLoader.EmptySprite));
                    Assert.That(song.IsCompressedCoverLoaded, Is.False);
                    song.Dispose();
                    Assert.That(SpriteLoader.EmptySprite != null, Is.True);
                }
                finally
                {
                    LogAssert.ignoreFailingMessages = false;
                }
            });
        }

        /// <summary>Checks that every resource accessor rejects a disposed song and that disposal is idempotent.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Dispose_RejectsFurtherUse()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var directory = ChartFixture.CreateChart(_temporary.Directory, "Chart", "Disposed");
                var song = await SongDetail.ParseAsync(directory);
                await song.GetMaidataAsync();
                var asyncSong = await SongDetail.ParseAsync(directory);

                song.Dispose();
                song.Dispose();
                await asyncSong.DisposeAsync();
                await asyncSong.DisposeAsync();

                Assert.That(song.IsMaidataLoaded, Is.False);
                Assert.Throws<ObjectDisposedException>(() => song.GetVideoPathAsync());
                await AssertThrowsAsync<ObjectDisposedException>(() => song.GetMaidataAsync().AsTask());
                await AssertThrowsAsync<ObjectDisposedException>(() => song.GetCoverAsync(true).AsTask());
                await AssertThrowsAsync<ObjectDisposedException>(() => song.GetAudioTrackAsync().AsTask());
                await AssertThrowsAsync<ObjectDisposedException>(() => song.GetPreviewAudioTrackAsync().AsTask());
                await AssertThrowsAsync<ObjectDisposedException>(() => song.PreloadAsync().AsTask());
                await AssertThrowsAsync<ObjectDisposedException>(() => asyncSong.GetMaidataAsync().AsTask());
            });
        }

        /// <summary>Awaits an operation and asserts that it fails with the expected exception type.</summary>
        /// <typeparam name="TException">The expected exception type, or a base type of it.</typeparam>
        /// <param name="operation">The operation to run.</param>
        /// <returns>A task completing after the assertion.</returns>
        private static async UniTask AssertThrowsAsync<TException>(Func<System.Threading.Tasks.Task> operation) where TException : Exception
        {
            var failure = default(Exception);
            try
            {
                await operation();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            Assert.That(failure, Is.InstanceOf<TException>());
        }
    }
}
