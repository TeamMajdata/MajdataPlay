#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.Collections;
using NativeDirectory = System.IO.Directory;
using NativeFile = System.IO.File;

namespace MajdataPlay.Tests.ChartStorageValidation
{
    /// <summary>Exercises the linked production scanner with fake parser I/O and an opaque storage backend.</summary>
    internal static partial class Program
    {
        /// <summary>Checks stable creation-time ordering, chart-local error isolation and hidden/incomplete skips.</summary>
        /// <param name="rootPath">The case-owned fixture root.</param>
        /// <returns>A task completing after repeated scans and counter assertions.</returns>
        private static async Task TestScannerIsolationAndOrder(string rootPath)
        {
            var path = CreateFixtureDirectory(rootPath, "ScannerOrder");
            var early = WriteChart(path, "Early", new ChartFixture { Hash = "early", Title = "Early", DelayMilliseconds = 40 }, "track.wav");
            var zeta = WriteChart(path, "Zeta", new ChartFixture { Hash = "tie-z", Title = "Zeta", DelayMilliseconds = 1 }, "TRACK.OPUS");
            var alpha = WriteChart(path, "Alpha", new ChartFixture { Hash = "tie-a", Title = "Alpha", DelayMilliseconds = 15 }, "track.aac");
            WriteChart(path, "BadChart", new ChartFixture { Hash = "bad", Title = "Rejected", Fail = true });
            var malformed = WriteChart(path, "Malformed", new ChartFixture { Hash = "malformed", Title = "Rejected" });
            NativeFile.WriteAllText(Path.Combine(malformed, "maidata.txt"), "{not a JSON fixture");
            WriteChart(path, "NoSupportedTrack", new ChartFixture { Hash = "no-track", Title = "Skipped" }, "track.unsupported");
            var noMetadata = CreateFixtureDirectory(path, "NoMetadata");
            NativeFile.WriteAllText(Path.Combine(noMetadata, "track.ogg"), "inert track");
            var hidden = WriteChart(path, "Hidden", new ChartFixture { Hash = "hidden", Title = "Skipped" });
            var dotHidden = WriteChart(path, ".HiddenByName", new ChartFixture { Hash = "dot-hidden", Title = "Skipped" });
            var denied = WriteChart(path, "DeniedChild", new ChartFixture { Hash = "denied", Title = "Skipped" });
            NativeFile.WriteAllText(Path.Combine(path, "not-a-chart-directory"), "ignore root file");
            var backend = new OpaqueFileSystem(path);
            var epoch = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            backend.CreationTimes[backend.Open(early).Location] = epoch;
            backend.CreationTimes[backend.Open(zeta).Location] = epoch.AddDays(1);
            backend.CreationTimes[backend.Open(alpha).Location] = epoch.AddDays(1);
            backend.Hidden[backend.Open(hidden).Location] = true;
            backend.Hidden[backend.Open(dotHidden).Location] = true;
            backend.EnumerationFailures[backend.Open(denied).Location] = new UnauthorizedAccessException("Injected chart-child access denial.");
            var probe = new ParseProbe();
            SongDetail.Probe = probe;
            using var scanner = new SongCollectionScanner();
            for (var iteration = 0; iteration < 4; iteration++)
            {
                backend.ReverseEnumeration = (iteration % 2) != 0;
                var songs = await scanner.ScanAsync(backend.Root);
                Require(songs.Select(song => song.Hash).SequenceEqual(new[] { "early", "tie-a", "tie-z" }), "Scanner order must be creation time then ordinal name, not provider or completion order: " + string.Join(",", songs.Select(song => song.Hash)));
                Require(songs.Cast<SongDetail>().All(song => ReferenceEquals(song.Directory!.FileSystem, backend)), "Every chart must retain its collection's original opaque backend.");
                Require(scanner.DiscoveredChartCount == (iteration + 1) * 5 && scanner.ProcessedChartCount == scanner.DiscoveredChartCount, "Counters must include valid candidates with parse failures, but not hidden, inaccessible or incomplete directories.");
                Require(scanner.LoadedChartCount == (iteration + 1) * 3, "The loaded count must include only successful charts and accumulate across scans.");
            }
            Require(probe.CompletedCount == 12 && probe.StartedCount == 20 && probe.Active == 0, "Repeated scans must observe all fake parser tasks without dropping healthy siblings.");
            Require(probe.Backends.All(item => ReferenceEquals(item, backend)), "Opaque locations must never be reopened through the facade or combined as native paths.");
            backend.EnumerationFailures[backend.Root.Location] = new UnauthorizedAccessException("Injected collection-level enumeration failure.");
            await ExpectExceptionAsync<UnauthorizedAccessException>(() => scanner.ScanAsync(backend.Root));
            Require(scanner.DiscoveredChartCount == 20 && scanner.LoadedChartCount == 12, "A root enumeration failure must not fabricate chart attempts.");
            backend.EnumerationFailures.TryRemove(backend.Root.Location, out _);
            Require((await scanner.ScanAsync(backend.Root)).Length == 3, "A collection enumeration failure must not poison a reusable scanner.");
            var dotVisible = WriteChart(path, ".VisibleChart", new ChartFixture { Hash = "dot-visible", Title = "Eligible dot-prefixed chart" });
            var linkVisible = WriteChart(path, "LinkedChart", new ChartFixture { Hash = "link-visible", Title = "Eligible linked chart" });
            backend.Hidden[backend.Open(dotVisible).Location] = false;
            backend.SymbolicLinks[backend.Open(linkVisible).Location] = true;
            var eligibleSongs = await scanner.ScanAsync(backend.Root);
            Require(eligibleSongs.Length == 5 && eligibleSongs.Any(song => song.Hash == "dot-visible") && eligibleSongs.Any(song => song.Hash == "link-visible"),
                "A non-hidden dot-prefixed or linked chart directory must retain its original scan eligibility.");
            scanner.Dispose();
            await ExpectExceptionAsync<ObjectDisposedException>(() => scanner.ScanAsync(backend.Root));
            Console.WriteLine("PASS scanner: stable provider-independent order, errors/skips, counters and lifetime");
        }

        /// <summary>Checks centralized filenames, case-insensitive track discovery and original-backend retention.</summary>
        /// <param name="rootPath">The owned fixture root for supported-resource charts.</param>
        /// <returns>A task completing after both the standard and modified filename-table scans.</returns>
        private static async Task TestSupportedResourceNames(string rootPath)
        {
            Require(SongStorage.SupportedCoverFileNames.SequenceEqual(new[] { "bg.png", "bg.jpg" }), "Cover support must be published centrally without adding unrequested formats.");
            Require(SongStorage.SupportedVideoFileNames.Contains("mv.mp4"), "Centralizing video names must retain legacy mv video support.");
            var path = CreateFixtureDirectory(rootPath, "SupportedResourceNames");
            for (var index = 0; index < SongStorage.SupportedTrackFileNames.Length; index++)
            {
                WriteChart(path, "Track-" + index, new ChartFixture { Hash = "supported-" + index, Title = "Supported resource" }, SongStorage.SupportedTrackFileNames[index].ToUpperInvariant());
            }
            WriteChart(path, "Unsupported", new ChartFixture { Hash = "unsupported", Title = "Unsupported resource" }, "track.flac");
            var directoryTrack = CreateFixtureDirectory(path, "DirectoryTrack");
            NativeFile.WriteAllText(Path.Combine(directoryTrack, SongStorage.ChartFileName), "{}");
            CreateFixtureDirectory(directoryTrack, SongStorage.SupportedTrackFileNames[0]);
            var noMetadata = CreateFixtureDirectory(path, "MissingMetadata");
            NativeFile.WriteAllBytes(Path.Combine(noMetadata, SongStorage.SupportedTrackFileNames[0]), new byte[] { 1 });
            var backend = new OpaqueFileSystem(path);
            SongDetail.Probe = new ParseProbe();
            using var scanner = new SongCollectionScanner();
            var songs = await scanner.ScanAsync(backend.Root);
            Require(songs.Length == SongStorage.SupportedTrackFileNames.Length && songs.All(song => song.Hash.StartsWith("supported-", StringComparison.Ordinal)), "Every centralized track name must be accepted without treating directory names or incomplete charts as resources.");
            Require(songs.Cast<SongDetail>().All(song => ReferenceEquals(song.Directory!.FileSystem, backend)), "Filename matching must not reopen original provider locations through the facade.");
            var originalName = SongStorage.SupportedTrackFileNames[0];
            try
            {
                SongStorage.SupportedTrackFileNames[0] = "track.validation";
                WriteChart(path, "CentralTableOnly", new ChartFixture { Hash = "central-table", Title = "Added only to the centralized table" }, "TRACK.VALIDATION");
                songs = await scanner.ScanAsync(backend.Root);
                Require(songs.Length == SongStorage.SupportedTrackFileNames.Length && songs.Any(song => song.Hash == "central-table") && songs.All(song => song.Hash != "supported-0"), "The production scanner must consult the shared filename table, not a duplicate audio-extension whitelist.");
            }
            finally
            {
                SongStorage.SupportedTrackFileNames[0] = originalName;
            }
            Console.WriteLine("PASS scanner: centralized filenames, uppercase tracks, required resources and retained backend");
        }

        /// <summary>Checks active and pre-start cancellation, worker observation and partial-result release.</summary>
        /// <param name="rootPath">The case-owned fixture root.</param>
        /// <returns>A task completing after cancellation and cleanup assertions.</returns>
        private static async Task TestScannerCancellation(string rootPath)
        {
            var path = CreateFixtureDirectory(rootPath, "ScannerCancellation");
            for (var index = 0; index < 32; index++)
            {
                WriteChart(path, "Chart-" + index.ToString("D3"), new ChartFixture { Hash = "cancel-" + index, Title = "Cancel fixture" });
            }
            var backend = new OpaqueFileSystem(path);
            using var canceledBeforeStart = new CancellationTokenSource();
            canceledBeforeStart.Cancel();
            using (var scanner = new SongCollectionScanner(canceledBeforeStart.Token))
            {
                await ExpectExceptionAsync<OperationCanceledException>(() => scanner.ScanAsync(backend.Root));
                Require(scanner.DiscoveredChartCount == 0 && scanner.ProcessedChartCount == 0 && scanner.LoadedChartCount == 0, "A pre-canceled scan must perform no discovery or parsing.");
            }
            var probe = new ParseProbe(block: true);
            SongDetail.Probe = probe;
            using var cancellation = new CancellationTokenSource();
            using (var scanner = new SongCollectionScanner(cancellation.Token))
            {
                var scan = scanner.ScanAsync(backend.Root);
                await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                cancellation.Cancel();
                await ExpectExceptionAsync<OperationCanceledException>(() => scan);
                Require(probe.Active == 0 && probe.CanceledCount > 0, "Cancellation must propagate into parsing and wait for active workers to finish.");
                Require(scanner.DiscoveredChartCount > 0 && scanner.ProcessedChartCount == scanner.DiscoveredChartCount && scanner.LoadedChartCount == 0, "Canceled admitted candidates must be processed without publishing loaded songs.");
            }

            var partialPath = CreateFixtureDirectory(rootPath, "ScannerPartialCancellation");
            var first = WriteChart(partialPath, "First", new ChartFixture { Hash = "partial-success", Title = "Loaded before cancellation" });
            var blocked = WriteChart(partialPath, "Second", new ChartFixture { Hash = "partial-blocked", Title = "Canceled later", DelayMilliseconds = 30000 });
            var partialBackend = new OpaqueFileSystem(partialPath);
            partialBackend.CreationTimes[partialBackend.Open(first).Location] = DateTime.UnixEpoch;
            partialBackend.CreationTimes[partialBackend.Open(blocked).Location] = DateTime.UnixEpoch.AddDays(1);
            var partialProbe = new ParseProbe();
            SongDetail.Probe = partialProbe;
            using var partialCancellation = new CancellationTokenSource();
            using (var scanner = new SongCollectionScanner(partialCancellation.Token))
            {
                var scan = scanner.ScanAsync(partialBackend.Root);
                await WaitUntilAsync(() => partialProbe.CompletedCount == 1 && partialProbe.Active > 0);
                partialCancellation.Cancel();
                await ExpectExceptionAsync<OperationCanceledException>(() => scan);
                Require(partialProbe.Songs.Count == 1 && partialProbe.Songs.All(song => song.IsDisposed), "A canceled scan must dispose already parsed unpublished songs after observing its workers.");
                Require(partialProbe.Active == 0 && scanner.LoadedChartCount == 1, "The loaded counter records completed attempts even when the enclosing scan is canceled.");
            }
            Console.WriteLine("PASS scanner: pre/active cancellation, token propagation and partial-result cleanup");
        }

        /// <summary>Checks one parser budget shared across concurrent collections and safe in-flight scanner disposal.</summary>
        /// <param name="rootPath">The case-owned fixture root.</param>
        /// <returns>A task completing after shared concurrency and scanner-lifetime assertions.</returns>
        private static async Task TestScannerSharedBudget(string rootPath)
        {
            var path = CreateFixtureDirectory(rootPath, "ScannerBudget");
            var budget = Math.Max(1, Environment.ProcessorCount);
            var collectionCount = 8;
            var chartsPerCollection = Math.Max(8, (budget / collectionCount) + 2);
            var backend = new OpaqueFileSystem(path);
            var directories = new List<MajdataPlay.IO.Storage.StorageDirectory>();
            for (var collectionIndex = 0; collectionIndex < collectionCount; collectionIndex++)
            {
                var collectionPath = CreateFixtureDirectory(path, "Collection-" + collectionIndex.ToString("D2"));
                directories.Add(backend.Open(collectionPath));
                for (var chartIndex = 0; chartIndex < chartsPerCollection; chartIndex++)
                {
                    WriteChart(collectionPath, "Chart-" + chartIndex.ToString("D3"), new ChartFixture { Hash = collectionIndex + ":" + chartIndex, Title = "Budget fixture" });
                }
            }
            var probe = new ParseProbe(block: true);
            SongDetail.Probe = probe;
            using (var scanner = new SongCollectionScanner())
            {
                var scans = directories.Select(directory => scanner.ScanAsync(directory)).ToArray();
                await WaitUntilAsync(() => probe.Active >= budget || scans.Any(scan => scan.IsFaulted));
                // Give every competing collection an admission opportunity while all admitted parses remain held.
                await Task.Delay(100);
                var observedPeak = probe.MaximumActive;
                var incomplete = scans.All(scan => !scan.IsCompleted);
                probe.Gate!.TrySetResult();
                var results = await Task.WhenAll(scans).WaitAsync(TimeSpan.FromSeconds(20));
                Require(incomplete && observedPeak > 0 && observedPeak <= budget && probe.MaximumActive <= budget, "Parallel collection scans must share one processor-count parser ceiling rather than multiply a per-collection budget.");
                var expectedCount = collectionCount * chartsPerCollection;
                Require(results.All(songs => songs.Length == chartsPerCollection) && results.Sum(songs => songs.Length) == expectedCount, "Bounded workers must still visit every chart across concurrent collections.");
                Require(scanner.DiscoveredChartCount == expectedCount && scanner.ProcessedChartCount == expectedCount && scanner.LoadedChartCount == expectedCount && probe.Active == 0, "Global counters must match all attempts after every concurrent scan completes.");
            }
            var lifetimeProbe = new ParseProbe(block: true);
            SongDetail.Probe = lifetimeProbe;
            using (var scanner = new SongCollectionScanner())
            {
                var scan = scanner.ScanAsync(directories[0]);
                await lifetimeProbe.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                scanner.Dispose();
                await ExpectExceptionAsync<ObjectDisposedException>(() => scanner.ScanAsync(directories[1]));
                lifetimeProbe.Gate!.TrySetResult();
                Require((await scan.WaitAsync(TimeSpan.FromSeconds(20))).Length == chartsPerCollection && lifetimeProbe.Active == 0, "Disposal must reject new scans but defer releasing the semaphore until admitted scans finish.");
            }
            Console.WriteLine("PASS scanner: global concurrency <= " + budget + ", exact coverage and deferred disposal");
        }

        /// <summary>Waits for a deterministic fixture signal with a bounded failure timeout.</summary>
        /// <param name="condition">The observable condition to wait for.</param>
        /// <returns>A task completing once the condition holds.</returns>
        /// <exception cref="TimeoutException">The fixture condition never became true.</exception>
        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!condition())
            {
                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException("The deterministic chart-storage fixture signal did not arrive.");
                }
                await Task.Delay(5);
            }
        }
    }
}
