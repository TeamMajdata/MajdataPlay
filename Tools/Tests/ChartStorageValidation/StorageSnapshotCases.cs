#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.Collections;
using MajdataPlay.IO.Storage;
using NativeDirectory = System.IO.Directory;
using NativeFile = System.IO.File;

namespace MajdataPlay.Tests.ChartStorageValidation
{
    /// <summary>Exercises production root scheduling and full storage snapshot commits without private-field coupling.</summary>
    internal static partial class Program
    {
        /// <summary>Repeats 96-collection scans through the real root scheduler and checks exact sibling coverage.</summary>
        /// <param name="rootPath">The case-owned fixture root.</param>
        /// <returns>A task completing after repeated root scans, cancellation and provider failure checks.</returns>
        private static async Task TestLocalRootScheduling(string rootPath)
        {
            var path = CreateFixtureDirectory(rootPath, "RootScheduling");
            var expected = new Dictionary<string, int>(StringComparer.Ordinal);
            var backend = new OpaqueFileSystem(path);
            for (var index = 95; index >= 0; index--)
            {
                var name = "Collection-" + index.ToString("D3");
                var collectionPath = CreateFixtureDirectory(path, name);
                var count = (index % 7) == 0 ? 0 : (index % 3) + 1;
                expected.Add(name, count);
                backend.Hidden[backend.Open(collectionPath).Location] = (index % 5) == 0;
                for (var chartIndex = 0; chartIndex < count; chartIndex++)
                {
                    WriteChart(collectionPath, "Chart-" + chartIndex.ToString("D2"), new ChartFixture { Hash = name + ":" + chartIndex, Title = name, DelayMilliseconds = (index % 3) + 1 });
                }
            }
            WriteChart(Path.Combine(path, "Collection-010"), "RejectedChart", new ChartFixture { Hash = "bad-root-chart", Title = "Rejected", Fail = true });
            var identities = new Dictionary<string, Guid>(StringComparer.Ordinal);
            var expectedCount = expected.Values.Sum();
            for (var iteration = 0; iteration < 8; iteration++)
            {
                backend.ReverseEnumeration = (iteration % 2) != 0;
                var probe = new ParseProbe();
                SongDetail.Probe = probe;
                var progress = new ProgressRecorder();
                var collections = await ScanLocalCollectionsAsync(backend.Root, progress);
                Require(collections.Count == expected.Count && collections.Select(collection => collection.Name).Distinct(StringComparer.Ordinal).Count() == expected.Count, "The production root scheduler must retain every healthy collection exactly once, including empty folders.");
                Require(collections.Select(collection => collection.Name).SequenceEqual(expected.Keys.OrderBy(name => name)), "Root results must be name-sorted independently of concurrent completion or provider enumeration order.");
                foreach (var collection in collections)
                {
                    Require(collection.Count == expected[collection.Name], "Root scan produced an incorrect successful chart count for " + collection.Name + ".");
                    Require(ReferenceEquals(collection.Directory!.FileSystem, backend), "The root scheduler must forward the root's exact backend into each collection.");
                    if (iteration == 0)
                    {
                        identities.Add(collection.Name, collection.Id);
                    }
                    Require(collection.Id == identities[collection.Name], "Repeated concurrent root scans must preserve each persisted collection identity.");
                }
                Require(collections.Sum(collection => collection.Count) == expectedCount && probe.CompletedCount == expectedCount && probe.StartedCount == expectedCount + 1, "Only successful charts may appear in returned collections, while a bad chart must not discard its siblings.");
                Require(probe.Active == 0 && probe.MaximumActive <= Math.Max(1, Environment.ProcessorCount), "The root workers must share one bounded production scanner and observe every parse.");
                Require(probe.Backends.All(item => ReferenceEquals(item, backend)), "Root scans must never reselect a backend for an opaque provider location.");
                RequireCompletedProgress(progress);
            }
            var emptyPath = CreateFixtureDirectory(rootPath, "EmptyRootScheduling");
            var emptyProgress = new ProgressRecorder();
            Require((await ScanLocalCollectionsAsync(FileSystem.Directory.Open(emptyPath), emptyProgress)).Count == 0, "A chart root with no collection directories must produce an empty base result.");
            RequireCompletedProgress(emptyProgress);

            var throwingProbe = new ParseProbe(block: true);
            SongDetail.Probe = throwingProbe;
            var throwingProgress = new ProgressRecorder(_ => throw new IOException("Injected root-scan progress failure."));
            var rejectedScan = ScanLocalCollectionsAsync(backend.Root, throwingProgress);
            try
            {
                await throwingProbe.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                throwingProbe.Gate!.TrySetResult();
            }
            await ExpectExceptionAsync<IOException>(() => rejectedScan);
            Require(throwingProbe.Active == 0 && throwingProbe.Songs.Count > 0 && throwingProbe.Songs.All(song => song.IsDisposed)
                && !throwingProgress.Messages.Any(IsCompletedProgress), "A throwing scan-progress callback must drain all admitted root workers and release unpublished charts without claiming completion.");
            var terminalProbe = new ParseProbe();
            SongDetail.Probe = terminalProbe;
            var terminalProgress = new ProgressRecorder(message =>
            {
                if (IsCompletedProgress(message))
                {
                    throw new IOException("Injected terminal progress failure.");
                }
            });
            await ExpectExceptionAsync<IOException>(() => ScanLocalCollectionsAsync(backend.Root, terminalProgress));
            Require(terminalProbe.Active == 0 && terminalProbe.CompletedCount == expectedCount && terminalProbe.Songs.All(song => song.IsDisposed),
                "A terminal reporter failure must release every completed unpublished chart only after all workers have finished.");
            var cancelProbe = new ParseProbe(block: true);
            SongDetail.Probe = cancelProbe;
            var canceledProgress = new ProgressRecorder();
            using var cancellation = new CancellationTokenSource();
            var scan = ScanLocalCollectionsAsync(backend.Root, canceledProgress, cancellation.Token);
            await cancelProbe.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            await ExpectExceptionAsync<OperationCanceledException>(() => scan);
            Require(cancelProbe.Active == 0 && cancelProbe.CanceledCount > 0 && !canceledProgress.Messages.Any(IsCompletedProgress), "Canceled root scans must propagate cancellation, drain workers and never falsely report 100 percent.");
            SongDetail.Probe = new ParseProbe();
            var deniedPath = CreateFixtureDirectory(path, "BrokenEnumeration");
            var deniedLocation = backend.Open(deniedPath).Location;
            backend.EnumerationFailures[deniedLocation] = new UnauthorizedAccessException("Injected root-child collection access denial.");
            var deniedProgress = new ProgressRecorder();
            await ExpectExceptionAsync<UnauthorizedAccessException>(() => ScanLocalCollectionsAsync(backend.Root, deniedProgress));
            Require(SongDetail.Probe.Active == 0 && SongDetail.Probe.Songs.All(song => song.IsDisposed) && !deniedProgress.Messages.Any(IsCompletedProgress), "A collection enumeration error must fail the entire root scan, drain siblings and release unpublished songs without reporting success.");
            backend.EnumerationFailures.TryRemove(deniedLocation, out _);
            var collisionPath = CreateFixtureDirectory(path, "BrokenMarker");
            WriteChart(collisionPath, "ParsedBeforeIdFailure", new ChartFixture { Hash = "marker-collision", Title = "Must be released" });
            NativeFile.WriteAllText(Path.Combine(collisionPath, ".MajdataPlay"), "preserve this marker collision");
            SongDetail.Probe = new ParseProbe();
            await ExpectExceptionAsync<IOException>(() => ScanLocalCollectionsAsync(backend.Root, new ProgressRecorder()));
            Require(SongDetail.Probe.Active == 0 && SongDetail.Probe.Songs.All(song => song.IsDisposed), "A collection ID failure must fail the root scan and release both sibling and constructor-rejected unpublished songs.");
            Require(NativeFile.ReadAllText(Path.Combine(collisionPath, ".MajdataPlay")) == "preserve this marker collision", "Failing marker creation must not alter the sibling fixture file.");
            backend.EnumerationFailures[backend.Root.Location] = new IOException("Injected inaccessible chart root.");
            await ExpectExceptionAsync<IOException>(() => ScanLocalCollectionsAsync(backend.Root, new ProgressRecorder()));
            backend.EnumerationFailures.TryRemove(backend.Root.Location, out _);
            Console.WriteLine("PASS storage root: 8 x 96 exact collections, empties, sorted names, success totals, progress and cancellation");
        }

        /// <summary>Checks first initialization, full snapshot counters, playlists and failure/cancellation rollback.</summary>
        /// <param name="rootPath">The case-owned fixture root.</param>
        /// <returns>A task completing after storage initialization and serialized refresh cases.</returns>
        private static async Task TestStorageSnapshotCommit(string rootPath)
        {
            Require(SongStorage.SupportedTrackFileNames.Contains("track.mp3") && !SongStorage.SupportedTrackFileNames.Contains("track..mp3"), "Supported audio extensions already contain their leading period and must produce exactly one filename separator.");
            Require(SongStorage.SupportedVideoFileNames.Contains("bg.mp4") && SongStorage.SupportedVideoFileNames.Contains("pv.mp4") && SongStorage.SupportedVideoFileNames.Contains("mv.mp4") && !SongStorage.SupportedVideoFileNames.Contains("bg..mp4"), "Supported video filenames must retain exactly one extension separator.");
            SongDetail.Probe = new ParseProbe();
            var storagePath = CreateFixtureDirectory(rootPath, "StorageSnapshot");
            MajEnv.ChartDirectory = FileSystem.Directory.Open(Path.Combine(storagePath, "Charts"));
            MajEnv.CacheDirectory = FileSystem.Directory.Open(CreateFixtureDirectory(storagePath, "Cache"));
            MajEnv.Settings.Online.Enable = false;
            var beforeInitialization = SongStorage.Collections;
            var countBeforeInitialization = SongStorage.TotalChartCount;
            await SongStorage.InitAsync();
            Require(NativeDirectory.Exists(Path.Combine(MajEnv.ChartDirectory.Location, "default")), "First initialization must create a missing root and its default directory.");
            Require(ReferenceEquals(SongStorage.Collections, beforeInitialization) && SongStorage.Collections.Length == 0
                && SongStorage.TotalChartCount == countBeforeInitialization && SongDetail.Probe.StartedCount == 0,
                "Initialization must not publish any collection, aggregate or chart, or invoke the chart parser.");
            var initialProgress = new ProgressRecorder();
            await SongStorage.RefreshLocal(initialProgress);
            Require(SongStorage.Collections.Any(collection => collection.Name == "default") && SongStorage.Collections.Any(collection => collection.Name == "All") && SongStorage.TotalChartCount == 0, "The first explicit local refresh must publish a usable local snapshot without an online refresh.");
            RequireCompletedProgress(initialProgress);
            var defaultId = SongStorage.Collections.Single(collection => collection.Name == "default").Id;
            var collectionPath = CreateFixtureDirectory(MajEnv.ChartDirectory.Location, "Fresh");
            var selectedPath = WriteChart(collectionPath, "Beta", new ChartFixture { Hash = "selected", Title = "Selected original" });
            WriteChart(collectionPath, "Other", new ChartFixture { Hash = "other", Title = "Other original" });
            WriteChart(collectionPath, "Bad", new ChartFixture { Hash = "bad", Title = "Rejected", Fail = true });
            NativeFile.WriteAllText(Path.Combine(MajEnv.ChartDirectory.Location, "InvalidDan.json"), "{not valid playlist JSON");
            NativeFile.WriteAllText(Path.Combine(MajEnv.ChartDirectory.Location, "EmptyDan.json"), JsonSerializer.Serialize(new DanInfo { Name = "Unmatched", SongHashs = new[] { "missing" } }));
            NativeFile.WriteAllText(Path.Combine(MajEnv.ChartDirectory.Location, "ValidDan.json"), JsonSerializer.Serialize(new DanInfo { Name = "Fixture playlist", SongHashs = new[] { "selected" }, IsPlayList = true }));
            NativeFile.WriteAllText(Path.Combine(MajEnv.ChartDirectory.Location, "MyFavorites.json"), JsonSerializer.Serialize(new DanInfo { Name = "Exported favorites", SongHashs = new[] { "selected" }, IsPlayList = true }));
            var refreshProgress = new ProgressRecorder();
            await RefreshBothSourcesAsync(refreshProgress);
            Require(SongStorage.TotalChartCount == 2 && SongStorage.Collections.Single(collection => collection.Name == "All").Count == 2, "Committed chart totals must count successful base charts only, not aggregate, favorite or playlist duplicates.");
            Require(SongStorage.Collections.Single(collection => collection.Name == "default").Id == defaultId, "Refreshing after first-time root creation must retain the default collection's identity.");
            Require(SongStorage.Collections.Single(collection => collection.Name == "MyFavorites").IsEmpty && SongStorage.Collections.Single(collection => collection.Name == "Fixture playlist").Single().Hash == "selected", "Valid playlists must resolve against the committed snapshot; refresh must not reload a favorites export written after initialization.");
            Require(!SongStorage.Collections.Any(collection => collection.Name == "Unmatched"), "An unmatched or malformed playlist must not leave a null task slot or prevent healthy playlists loading.");
            RequireCompletedProgress(refreshProgress);
            var local = SongStorage.Collections.Single(collection => collection.Name == "Fresh");
            var localId = local.Id;
            local.SetCursor("selected");
            MajEnv.RuntimeConfig.List.SelectedDir = Array.IndexOf(SongStorage.Collections, local);
            MajEnv.RuntimeConfig.List.SelectedSongIndex = local.Index;
            SongStorage.CollectionIndex = MajEnv.RuntimeConfig.List.SelectedDir;
            Require(SongStorage.WorkingCollection.Current.Hash == "selected", "The snapshot fixture must begin with its selected local chart.");
            SongStorage.AddToMyFavorites(local.Current);
            var previous = SongStorage.Collections;
            var previousAll = previous.Single(collection => collection.Name == "All").ToArray();
            var previousFavorites = previous.Single(collection => collection.Name == "MyFavorites");
            var previousCount = SongStorage.TotalChartCount;
            NativeFile.Delete(Path.Combine(selectedPath, "maidata.txt"));
            var movedPath = WriteChart(collectionPath, "Aardvark", new ChartFixture { Hash = "selected", Title = "Selected updated" });
            var addedPath = WriteChart(collectionPath, "Added", new ChartFixture { Hash = "added", Title = "Added chart" });

            var parseProbe = new ParseProbe(block: true);
            SongDetail.Probe = parseProbe;
            using var cancellation = new CancellationTokenSource();
            var canceledProgress = new ProgressRecorder();
            var canceledRefresh = SongStorage.RefreshLocal(canceledProgress, cancellation.Token);
            await parseProbe.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            RequireUnchangedStorage(previous, previousAll, previousFavorites, previousCount);
            using var queuedCancellation = new CancellationTokenSource();
            var queuedRefresh = RefreshBothSourcesAsync(cancellationToken: queuedCancellation.Token);
            queuedCancellation.Cancel();
            await ExpectExceptionAsync<OperationCanceledException>(() => queuedRefresh);
            RequireUnchangedStorage(previous, previousAll, previousFavorites, previousCount);
            cancellation.Cancel();
            await ExpectExceptionAsync<OperationCanceledException>(() => canceledRefresh);
            Require(parseProbe.Active == 0 && parseProbe.CanceledCount > 0 && !canceledProgress.Messages.Any(IsCompletedProgress), "Canceled storage work must drain the shared scanner without claiming a complete scan.");
            RequireUnchangedStorage(previous, previousAll, previousFavorites, previousCount);
            using var alreadyCanceled = new CancellationTokenSource();
            alreadyCanceled.Cancel();
            await ExpectExceptionAsync<OperationCanceledException>(() => RefreshBothSourcesAsync(cancellationToken: alreadyCanceled.Token));
            RequireUnchangedStorage(previous, previousAll, previousFavorites, previousCount);

            var validRoot = MajEnv.ChartDirectory;
            var notDirectory = Path.Combine(storagePath, "NotADirectory");
            NativeFile.WriteAllText(notDirectory, "root scan must fail");
            MajEnv.ChartDirectory = FileSystem.Directory.Open(notDirectory);
            await ExpectExceptionAsync<IOException>(() => SongStorage.RefreshLocal());
            RequireUnchangedStorage(previous, previousAll, previousFavorites, previousCount);
            MajEnv.ChartDirectory = validRoot;
            SongDetail.Probe = new ParseProbe();
            await ExpectExceptionAsync<IOException>(() => SongStorage.RefreshLocal(new ThrowOnCleanupProgress()));
            RequireUnchangedStorage(previous, previousAll, previousFavorites, previousCount);
            Require(SongDetail.Probe.CompletedCount == 3, "The pre-commit failure fixture must finish scanning before rejecting publication.");

            var favoritesExport = Path.Combine(MajEnv.ChartDirectory.Location, "MyFavorites.json");
            NativeFile.WriteAllText(favoritesExport, JsonSerializer.Serialize(new DanInfo { Name = "Uncommitted favorites", SongHashs = new[] { "other" }, IsPlayList = true }));
            await ExpectExceptionAsync<IOException>(() => SongStorage.RefreshLocal(new ThrowOnCleanupProgress()));
            RequireUnchangedStorage(previous, previousAll, previousFavorites, previousCount);
            var rejectedPath = CreateFixtureDirectory(MajEnv.ChartDirectory.Location, "RejectedCollection");
            var rejectedMarker = Path.Combine(rejectedPath, ".MajdataPlay");
            NativeFile.WriteAllText(rejectedMarker, "preserve marker collision");
            SongDetail.Probe = new ParseProbe();
            await ExpectExceptionAsync<IOException>(() => RefreshBothSourcesAsync());
            RequireUnchangedStorage(previous, previousAll, previousFavorites, previousCount);
            Require(SongDetail.Probe.Active == 0 && SongDetail.Probe.Songs.All(song => song.IsDisposed), "A collection-level refresh failure must drain sibling workers and release every unpublished chart instead of committing a library with a missing folder.");
            NativeFile.Delete(rejectedMarker);
            var goodProgress = new ProgressRecorder();
            await SongStorage.RefreshLocal(goodProgress);
            Require(SongStorage.TotalChartCount == 3 && SongStorage.Collections.Single(collection => collection.Name == "All").Count == 3, "A successful retry must publish the complete new chart total.");
            Require(SongStorage.WorkingCollection.Id == localId && SongStorage.WorkingCollection.Current.Hash == "selected" && SongStorage.WorkingCollection.Current.Title == "Selected updated", "Storage refresh must restore the selection by collection ID and chart hash after chart reordering.");
            Require(previousAll.Cast<SongDetail>().All(song => song.IsDisposed), "The owning storage snapshot may release obsolete chart instances after successful publication.");
            Require(SongStorage.IsInMyFavorites(SongStorage.WorkingCollection.Current), "Canceled/failed refreshes must not lose favorite membership before the successful retry.");
            Require(!SongStorage.IsInMyFavorites(SongStorage.Collections.Single(collection => collection.Name == "All").Single(song => song.Hash == "other")), "A failed refresh or later root scan must not import post-initialization export hashes into a local refresh.");
            NativeFile.WriteAllText(favoritesExport, JsonSerializer.Serialize(new DanInfo { Name = "Exported favorites", SongHashs = new[] { "selected" }, IsPlayList = true }));
            RequireCompletedProgress(goodProgress);

            var beforeConcurrent = SongStorage.Collections;
            var beforeConcurrentAll = beforeConcurrent.Single(collection => collection.Name == "All").ToArray();
            var beforeConcurrentFavorites = beforeConcurrent.Single(collection => collection.Name == "MyFavorites");
            var serialProbe = new ParseProbe(block: true);
            SongDetail.Probe = serialProbe;
            var firstRefresh = SongStorage.RefreshLocal();
            await serialProbe.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await WaitUntilAsync(() => serialProbe.Active >= Math.Min(Math.Max(1, Environment.ProcessorCount), 4));
            var admitted = serialProbe.StartedCount;
            var secondRefresh = RefreshBothSourcesAsync();
            await Task.Delay(100);
            var secondWasWaiting = !secondRefresh.IsCompleted && serialProbe.StartedCount == admitted;
            RequireUnchangedStorage(beforeConcurrent, beforeConcurrentAll, beforeConcurrentFavorites, 3);
            serialProbe.Gate!.TrySetResult();
            await Task.WhenAll(firstRefresh, secondRefresh).WaitAsync(TimeSpan.FromSeconds(20));
            Require(secondWasWaiting && serialProbe.Active == 0 && serialProbe.CompletedCount == 6, "Explicit source refreshes must serialize admission through the shared refresh lock, then both complete.");
            Require(SongStorage.TotalChartCount == 3 && SongStorage.WorkingCollection.Current.Hash == "selected", "Serialized refresh commits must leave a coherent total and selected chart. Total=" + SongStorage.TotalChartCount + ", selected=" + SongStorage.WorkingCollection.Current.Hash + ", runtimeIndex=" + MajEnv.RuntimeConfig.List.SelectedSongIndex);
            NativeFile.Delete(Path.Combine(addedPath, "maidata.txt"));
            NativeFile.Delete(Path.Combine(movedPath, "maidata.txt"));
            SongDetail.Probe = new ParseProbe();
            await RefreshBothSourcesAsync();
            Require(SongStorage.TotalChartCount == 1 && SongStorage.Collections.Single(collection => collection.Name == "All").Single().Hash == "other", "A later explicit two-source refresh must lower the committed total when charts are deleted.");
            Require(await SongStorage.RefreshUserOnlineFavAsync(), "Disabled online favorites refresh must not invoke the rejected network stub.");
            Console.WriteLine("PASS storage snapshot: non-scanning init, explicit local creation, totals/playlists, selection, rollback and serialized retries");
        }

        /// <summary>Invokes the real private root scheduler using its explicitly agreed signature.</summary>
        /// <param name="directory">The root directory and its original backend.</param>
        /// <param name="progress">The synchronous progress observer.</param>
        /// <param name="cancellationToken">Cancels the production root scan.</param>
        /// <returns>The collections produced by the actual production implementation.</returns>
        /// <exception cref="InvalidOperationException">The agreed entry point is absent or returns the wrong task type.</exception>
        private static Task<List<SongCollection>> ScanLocalCollectionsAsync(StorageDirectory directory, IProgress<string> progress, CancellationToken cancellationToken = default)
        {
            var method = typeof(SongStorage).GetMethod("GetLocalCollections", BindingFlags.NonPublic | BindingFlags.Static,
                binder: null, types: new[] { typeof(StorageDirectory), typeof(IProgress<string>), typeof(CancellationToken) }, modifiers: null)
                ?? throw new InvalidOperationException("The production StorageDirectory root scheduler signature is missing.");
            return method.Invoke(null, new object?[] { directory, progress, cancellationToken }) as Task<List<SongCollection>>
                ?? throw new InvalidOperationException("The production root scheduler must return Task<List<SongCollection>>.");
        }

        /// <summary>Checks that a complete scan emits finite percentages with exactly one final 100-percent message.</summary>
        /// <param name="progress">The synchronous scan progress observer.</param>
        private static void RequireCompletedProgress(ProgressRecorder progress)
        {
            var percentages = progress.Messages.Select(ParseProgressPercent).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
            Require(percentages.Length > 0 && percentages[^1] == 100f && percentages.Count(value => value == 100f) == 1, "A completed scan must report exactly one terminal 100-percent progress message.");
            Require(percentages.All(value => float.IsFinite(value) && value >= 0f && value <= 100f), "Scan progress percentages must be finite and remain within their valid range.");
        }

        /// <summary>Identifies a terminal progress percentage independently of the current culture's decimal separator.</summary>
        /// <param name="message">The reported production progress text.</param>
        /// <returns>Whether the message reports 100 percent.</returns>
        private static bool IsCompletedProgress(string message)
        {
            return ParseProgressPercent(message) == 100f;
        }

        /// <summary>Extracts a parenthesized percent from a production progress string.</summary>
        /// <param name="message">The reported production message.</param>
        /// <returns>The numeric percentage, or null for non-percent progress messages.</returns>
        private static float? ParseProgressPercent(string message)
        {
            var start = message.LastIndexOf('(');
            var end = message.LastIndexOf('%');
            if (start < 0 || end <= start)
            {
                return null;
            }
            return float.TryParse(message.AsSpan(start + 1, end - start - 1), NumberStyles.Float, CultureInfo.CurrentCulture, out var percentage) ? percentage : null;
        }

        /// <summary>Checks the public aggregate/favorites/count state without coupling to production private fields.</summary>
        /// <param name="collections">The exact previously published collection array.</param>
        /// <param name="allCharts">The previous aggregate's exact song instances.</param>
        /// <param name="favorites">The previously published favorites collection.</param>
        /// <param name="count">The previously committed successful base chart count.</param>
        private static void RequireUnchangedStorage(SongCollection[] collections, ISongDetail[] allCharts, SongCollection favorites, long count)
        {
            Require(ReferenceEquals(SongStorage.Collections, collections) && SongStorage.TotalChartCount == count, "Failed, canceled or incomplete scans must not publish Collections or TotalChartCount.");
            Require(SongStorage.Collections.Single(collection => collection.Name == "All").ToArray().SequenceEqual(allCharts), "Failed/canceled scans must leave the aggregate's exact song references unchanged.");
            Require(ReferenceEquals(SongStorage.Collections.Single(collection => collection.Name == "MyFavorites"), favorites) && allCharts.Cast<SongDetail>().All(song => !song.IsDisposed), "Failed/canceled scans must preserve the favorites instance and undisposed old charts.");
        }

        /// <summary>Collects progress synchronously so terminal assertions do not race a synchronization-context callback.</summary>
        private sealed class ProgressRecorder : IProgress<string>
        {
            /// <summary>Owns an optional synchronous progress fault or cancellation injection.</summary>
            private readonly Action<string>? _callback;

            /// <summary>Creates a recorder with an optional inline injection callback.</summary>
            /// <param name="callback">The injection invoked after each recorded progress message.</param>
            public ProgressRecorder(Action<string>? callback = null)
            {
                _callback = callback;
            }

            /// <summary>Gets all reported progress messages in their observation order.</summary>
            public ConcurrentQueue<string> Messages { get; } = new ConcurrentQueue<string>();

            /// <summary>Records a production progress message immediately.</summary>
            /// <param name="value">The production progress text.</param>
            public void Report(string value)
            {
                Messages.Enqueue(value);
                _callback?.Invoke(value);
            }
        }

        /// <summary>Injects a deterministic failure after scanning but before snapshot publication.</summary>
        private sealed class ThrowOnCleanupProgress : IProgress<string>
        {
            /// <summary>Fails only at the agreed pre-publication cleanup-report boundary.</summary>
            /// <param name="value">The production progress text.</param>
            /// <exception cref="IOException">The completed snapshot is about to be published.</exception>
            public void Report(string value)
            {
                if (value == "MAJTEXT_CLEANING_UP")
                {
                    throw new IOException("Injected failure immediately before storage snapshot publication.");
                }
            }
        }
    }
}
