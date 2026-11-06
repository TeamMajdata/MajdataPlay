using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using MajdataPlay.Collections;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Misc.Support;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Collections
{
    /// <summary>Covers chart discovery and parsing by <see cref="SongCollectionScanner"/> and directory collection refreshes.</summary>
    public class SongCollectionScannerTests
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

        /// <summary>Checks that valid and leniently parsed charts are loaded while incomplete and loose entries are skipped and counted correctly.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ScanAsync_LoadsValidCharts()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var collection = _temporary.Directory.CreateDirectory("Collection");
                ChartFixture.CreateChart(collection, "A", "Alpha");
                ChartFixture.CreateChart(collection, "B", "Beta", trackName: "TRACK.OGG");
                ChartFixture.CreateChart(collection, "NoTrack", "NoTrack", trackName: null);
                ChartFixture.CreateChart(collection, "NoChart", "NoChart", withMaidata: false);
                collection.CreateDirectory("Broken").CreateFile("maidata.txt").WriteAllText("&title\n");
                collection.FindDirectory("Broken")!.CreateFile("track.mp3").WriteAllBytes(new byte[] { 1 });
                collection.CreateFile("loose.txt").WriteAllText("x");
                using var scanner = new SongCollectionScanner();

                var songs = await scanner.ScanAsync(collection);

                // Since MajSimai 3.0 the lenient parser ignores an '&' line without '=' (with a warning) instead of
                // throwing, so the "Broken" entry loads as a song without a title.
                Assert.That(songs.Select(song => song.Title), Is.EquivalentTo(new[] { "Alpha", "Beta", string.Empty }));
                Assert.That(songs.All(song => song is SongDetail), Is.True);
                Assert.That(scanner.DiscoveredChartCount, Is.EqualTo(3));
                Assert.That(scanner.ProcessedChartCount, Is.EqualTo(3));
                Assert.That(scanner.LoadedChartCount, Is.EqualTo(3));
                // Every child directory counts towards progress, including the ones without a chart.
                Assert.That(scanner.DirectoryCount, Is.EqualTo(5));
                Assert.That(scanner.ScannedDirectoryCount, Is.EqualTo(5));
                // Scanning only reads; an identity marker is written only when something stores the identity.
                Assert.That(collection.FindDirectory("A")!.FindEntry(DirectoryIdentity.MarkerDirectoryName), Is.Null);
            });
        }

        /// <summary>
        /// Checks that charts load and progress is reported on the scanner's own threads rather than thread-pool threads, and
        /// that the reported fraction stays within range and never decreases.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ScanAsync_ReportsProgressFromDedicatedThreads()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var collection = _temporary.Directory.CreateDirectory("Collection");
                const int chartCount = 24;
                for (var i = 0; i < chartCount; i++)
                {
                    ChartFixture.CreateChart(collection, $"Chart{i:D2}", $"Title{i:D2}");
                }
                var reports = new ConcurrentQueue<(double Progress, bool IsThreadPoolThread)>();
                using var scanner = new SongCollectionScanner(progressChanged: progress =>
                {
                    reports.Enqueue((progress, Thread.CurrentThread.IsThreadPoolThread));
                });

                var songs = await scanner.ScanAsync(collection);

                Assert.That(songs.Length, Is.EqualTo(chartCount));
                Assert.That(scanner.DirectoryCount, Is.EqualTo(chartCount));
                Assert.That(scanner.ScannedDirectoryCount, Is.EqualTo(chartCount));
                var reported = reports.ToArray();
                Assert.That(reported, Is.Not.Empty);
                Assert.That(reported.Any(report => report.IsThreadPoolThread), Is.False);
                Assert.That(reported.Select(report => report.Progress), Is.Ordered);
                Assert.That(reported.All(report => report.Progress >= 0d && report.Progress <= 1d), Is.True);
            });
        }

        /// <summary>Checks that one scanner scans several collections concurrently, keeping each collection's charts apart.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ScanAsync_ScansCollectionsConcurrently()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var first = _temporary.Directory.CreateDirectory("First");
                var second = _temporary.Directory.CreateDirectory("Second");
                ChartFixture.CreateChart(first, "A", "Alpha");
                ChartFixture.CreateChart(first, "B", "Beta");
                ChartFixture.CreateChart(second, "C", "Gamma");
                second.CreateDirectory("Empty");
                using var scanner = new SongCollectionScanner();

                var scans = new[] { scanner.ScanAsync(first), scanner.ScanAsync(second), scanner.ScanAsync(_temporary.Directory.CreateDirectory("None")) };
                var results = await System.Threading.Tasks.Task.WhenAll(scans);

                Assert.That(results[0].Select(song => song.Title), Is.EquivalentTo(new[] { "Alpha", "Beta" }));
                Assert.That(results[1].Select(song => song.Title), Is.EquivalentTo(new[] { "Gamma" }));
                Assert.That(results[2], Is.Empty);
                Assert.That(scanner.DirectoryCount, Is.EqualTo(4));
                Assert.That(scanner.ScannedDirectoryCount, Is.EqualTo(4));
                Assert.That(scanner.LoadedChartCount, Is.EqualTo(3));
            });
        }

        /// <summary>Checks that an empty collection yields no charts.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ScanAsync_EmptyCollection()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var scanner = new SongCollectionScanner();

                var songs = await scanner.ScanAsync(_temporary.Directory);

                Assert.That(songs, Is.Empty);
                Assert.That(scanner.DiscoveredChartCount, Is.EqualTo(0));
            });
        }

        /// <summary>Checks argument validation and that a disposed scanner rejects new scans.</summary>
        [Test]
        public void ScanAsync_ValidatesStateAndArguments()
        {
            var scanner = new SongCollectionScanner();

            Assert.Throws<ArgumentNullException>(() => scanner.ScanAsync(null!));
            scanner.Dispose();
            scanner.Dispose();
            Assert.Throws<ObjectDisposedException>(() => scanner.ScanAsync(_temporary.Directory));
        }

        /// <summary>Checks that a canceled scanner fails with cancellation.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ScanAsync_Canceled_Throws()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ChartFixture.CreateChart(_temporary.Directory, "A", "Alpha");
                using var source = new CancellationTokenSource();
                source.Cancel();
                using var scanner = new SongCollectionScanner(source.Token);
                var canceled = false;

                try
                {
                    await scanner.ScanAsync(_temporary.Directory);
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                }

                Assert.That(canceled, Is.True);
            });
        }

        /// <summary>Checks that a missing collection directory fails the scan.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ScanAsync_MissingDirectory_Throws()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var missing = new MajdataPlay.IO.Storage.StorageDirectory(_temporary.Directory.FileSystem, System.IO.Path.Combine(_temporary.Location, "missing"));
                using var scanner = new SongCollectionScanner();
                var failed = false;

                try
                {
                    await scanner.ScanAsync(missing);
                }
                catch (System.IO.IOException)
                {
                    failed = true;
                }

                Assert.That(failed, Is.True);
            });
        }

        /// <summary>Checks that refreshing a directory collection picks up new charts while keeping the sort and selection.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RefreshAsync_PublishesNewCharts()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var directory = _temporary.Directory.CreateDirectory("Collection");
                ChartFixture.CreateChart(directory, "B", "Bravo");
                var collection = SongCollection.Empty(directory, "Collection");
                await collection.RefreshAsync();
                Assert.That(collection.Count, Is.EqualTo(1));
                collection.SortAndFilter(new SongOrder { SortBy = SortType.ByTitle });

                ChartFixture.CreateChart(directory, "A", "Alpha");
                await collection.RefreshAsync();

                Assert.That(collection.Select(song => song.Title), Is.EqualTo(new[] { "Alpha", "Bravo" }));
                Assert.That(collection.Current.Title, Is.EqualTo("Bravo"));
                Assert.That(collection.IsSorted, Is.True);
            });
        }

        /// <summary>Checks the synchronous refresh wrapper and that a canceled refresh keeps the previous songs.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Refresh_SynchronousAndCanceled()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var directory = _temporary.Directory.CreateDirectory("Collection");
                ChartFixture.CreateChart(directory, "A", "Alpha");
                var collection = SongCollection.Empty(directory, "Collection");

                await UniTask.RunOnThreadPool(() => collection.Refresh());
                Assert.That(collection.Count, Is.EqualTo(1));

                ChartFixture.CreateChart(directory, "B", "Bravo");
                using var source = new CancellationTokenSource();
                source.Cancel();
                var canceled = false;
                try
                {
                    await collection.RefreshAsync(source.Token);
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                }

                Assert.That(canceled, Is.True);
                Assert.That(collection.Count, Is.EqualTo(1));
            });
        }
    }
}
