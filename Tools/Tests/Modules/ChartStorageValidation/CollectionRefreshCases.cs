#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.Collections;
using MajdataPlay.IO.Storage;
using MajdataPlay.Net;
using NativeDirectory = System.IO.Directory;
using NativeFile = System.IO.File;

namespace MajdataPlay.Tests.ChartStorageValidation
{
    /// <summary>Validates refresh publication, selection and backend retention against production collections.</summary>
    internal static partial class Program
    {
        /// <summary>Checks add, remove and metadata-update refreshes while preserving ID, sort/filter and cursor hash.</summary>
        /// <param name="rootPath">The case-owned fixture root.</param>
        /// <returns>A task completing after synchronous and asynchronous refresh assertions.</returns>
        private static async Task TestCollectionRefresh(string rootPath)
        {
            var path = CreateFixtureDirectory(rootPath, "Refresh");
            var directory = FileSystem.Directory.Open(path);
            WriteChart(path, "Alpha", new ChartFixture { Hash = "keep-a", Title = "Keep zebra" });
            WriteChart(path, "Beta", new ChartFixture { Hash = "keep-b", Title = "Keep beta" });
            WriteChart(path, "Gamma", new ChartFixture { Hash = "remove", Title = "Keep remove" });
            WriteChart(path, "Delta", new ChartFixture { Hash = "other", Title = "Other" });
            var collection = SongCollection.Empty(directory, "Refresh");
            var id = collection.Id;
            Require(ReferenceEquals(collection.Directory, directory), "The constructor must retain the original StorageDirectory handle.");
            Require(collection.Path == directory.Location && !collection.IsVirtual, "Path must remain compatible with the supplied local directory.");
            var previousContext = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new RejectingSynchronizationContext());
                collection.Refresh();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
            Require(collection.Count == 4, "Synchronous refresh must populate an initially empty local collection.");
            collection.SetCursor("keep-b");
            var order = new SongOrder { Keyword = "keep", SortBy = SortType.ByTitle };
            collection.SortAndFilter(order);
            RequireHashes(collection, "keep-b", "remove", "keep-a");
            var oldSongs = collection.ToArray().Cast<SongDetail>().ToArray();
            var sharedView = new SongCollection("All", collection.ToArray());
            var sharedFavorites = new MyFavoriteSongCollection();
            sharedFavorites.Add(oldSongs[0]);
            using var previousEnumerator = collection.GetEnumerator();
            order.Keyword = "unapplied mutation";
            order.SortBy = SortType.Default;
            WriteChart(path, "Alpha", new ChartFixture { Hash = "keep-a", Title = "Keep zzz" });
            WriteChart(path, "Beta", new ChartFixture { Hash = "keep-b", Title = "Keep aardvark" });
            NativeFile.Delete(Path.Combine(path, "Gamma", "maidata.txt"));
            WriteChart(path, "Epsilon", new ChartFixture { Hash = "keep-c", Title = "Keep omega" });
            await collection.RefreshAsync();
            RequireHashes(collection, "keep-b", "keep-c", "keep-a");
            Require(collection.Current.Hash == "keep-b" && collection.Current.Title == "Keep aardvark", "Refresh must retain the cursor hash while replacing updated metadata.");
            Require(collection.IsSorted && oldSongs.All(song => !song.IsDisposed), "A collection refresh must not dispose song details potentially shared by storage aggregates or playlists.");
            Require(sharedView.Current.Title == "Keep beta" && sharedView.ToArray().SequenceEqual(oldSongs), "Refreshing one collection must leave another collection sharing the previous details usable and unchanged.");
            Require(ReferenceEquals(sharedFavorites.Current, oldSongs[0]) && !((SongDetail)sharedFavorites.Current).IsDisposed,
                "A favorite retaining the previous song must remain usable after another collection is refreshed.");
            var enumerated = new System.Collections.Generic.List<ISongDetail>();
            while (previousEnumerator.MoveNext())
            {
                enumerated.Add(previousEnumerator.Current);
            }
            Require(enumerated.SequenceEqual(oldSongs), "An enumerator captured before refresh must retain its original immutable song snapshot.");
            Require(collection.Id == id && Guid.Parse(directory.FindDirectory(".MajdataPlay")!.FindFile("id")!.ReadAllText()) == id, "Refresh must not create a new collection ID.");

            collection.SortAndFilter(new SongOrder { Keyword = "no-match", SortBy = SortType.ByTitle });
            Require(collection.IsEmpty, "An unmatched filter must produce an empty visible snapshot.");
            collection.SortAndFilter(new SongOrder { Keyword = "keep", SortBy = SortType.ByTitle });
            RequireHashes(collection, "keep-b", "keep-c", "keep-a");
            Require(collection.Current.Hash == "keep-b", "Re-filtering an empty result must recover the retained cursor hash.");
            collection.SortAndFilter(new SongOrder { Keyword = "future-token", SortBy = SortType.ByTitle });
            await collection.RefreshAsync();
            Require(collection.IsEmpty && collection.IsSorted, "Refresh must keep an active filter even while no chart matches it.");
            WriteChart(path, "Beta", new ChartFixture { Hash = "keep-b", Title = "future-token beta" });
            WriteChart(path, "Future", new ChartFixture { Hash = "future-a", Title = "future-token alpha" });
            await collection.RefreshAsync();
            RequireHashes(collection, "future-a", "keep-b");
            Require(collection.Current.Hash == "keep-b" && collection.Index == 1, "A filter that becomes nonempty must recover its previous cursor by hash, not index.");
            collection.Reset();
            Require(collection.Count == 5 && !collection.IsSorted && collection.Current.Hash == "keep-b", "Reset must reveal the freshly scanned origin while preserving selection.");
            NativeFile.Delete(Path.Combine(path, "Beta", "maidata.txt"));
            collection.Refresh();
            Require(collection.Count == 4 && collection.Index == 0 && collection.All(song => song.Hash != "keep-b"), "Removing the selected chart must fall back to a valid first cursor.");
            Require(collection.Id == id, "All successful refreshes must preserve collection identity.");
            Console.WriteLine("PASS collection: sync/async add/remove/update, ID, filter recovery and cursor hash");
        }

        /// <summary>Checks that directory failures and cancellation retain the complete previous collection snapshot.</summary>
        /// <param name="rootPath">The case-owned fixture root.</param>
        /// <returns>A task completing after failure, active cancellation and lock-reuse checks.</returns>
        private static async Task TestCollectionRefreshRollback(string rootPath)
        {
            var path = CreateFixtureDirectory(rootPath, "Rollback");
            WriteChart(path, "One", new ChartFixture { Hash = "one", Title = "First" });
            WriteChart(path, "Two", new ChartFixture { Hash = "two", Title = "Second" });
            var backend = new OpaqueFileSystem(path);
            var collection = SongCollection.Empty(backend.Root, "Rollback");
            await collection.RefreshAsync();
            collection.SortAndFilter(new SongOrder { SortBy = SortType.ByTitle });
            collection.SetCursor("two");
            var original = collection.ToArray();
            var id = collection.Id;
            backend.EnumerationFailures[backend.Root.Location] = new UnauthorizedAccessException("Injected collection access denial.");
            await ExpectExceptionAsync<UnauthorizedAccessException>(() => collection.RefreshAsync());
            RequireUnchangedCollection(collection, original, id, "two");
            backend.EnumerationFailures.TryRemove(backend.Root.Location, out _);
            WriteChart(path, "One", new ChartFixture { Hash = "one", Title = "Uncommitted update" });
            var probe = new ParseProbe(block: true);
            SongDetail.Probe = probe;
            using var cancellation = new CancellationTokenSource();
            var refresh = collection.RefreshAsync(cancellation.Token);
            await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            await ExpectExceptionAsync<OperationCanceledException>(() => refresh);
            Require(probe.Active == 0 && probe.CanceledCount > 0, "Refresh cancellation must reach the parser and observe every worker before returning.");
            RequireUnchangedCollection(collection, original, id, "two");
            using var alreadyCanceled = new CancellationTokenSource();
            alreadyCanceled.Cancel();
            await ExpectExceptionAsync<OperationCanceledException>(() => collection.RefreshAsync(alreadyCanceled.Token));
            RequireUnchangedCollection(collection, original, id, "two");
            SongDetail.Probe = new ParseProbe();
            await collection.RefreshAsync();
            Require(collection.Any(song => song.Title == "Uncommitted update") && collection.Current.Hash == "two", "A failed or canceled refresh must release its lock for the next successful refresh.");
            Require(ReferenceEquals(collection.Directory, backend.Root) && SongDetail.Probe.Backends.All(item => ReferenceEquals(item, backend)), "Refresh must never reopen an opaque directory through the global facade.");
            Console.WriteLine("PASS collection: failure/cancellation rollback, original backend and retry");
        }

        /// <summary>Checks that virtual and online refreshes are no-ops, including with an already canceled token.</summary>
        /// <param name="rootPath">The case-owned fixture root.</param>
        /// <returns>A task completing after all no-op assertions.</returns>
        private static async Task TestVirtualAndOnlineRefresh(string rootPath)
        {
            var song = new SongDetail(new ChartFixture { Hash = "virtual", Title = "Remote original" }, null);
            var virtualCollection = new SongCollection("All", new ISongDetail[] { song });
            var emptyVirtual = SongCollection.Empty("default");
            var path = CreateFixtureDirectory(rootPath, "OnlineCache");
            WriteChart(path, "NeverRead", new ChartFixture { Hash = "cache", Title = "Local cache must not replace remote metadata" });
            var backend = new OpaqueFileSystem(path);
            var endpoint = new ApiEndpoint();
            var cachedOnline = new OnlineSongCollection(endpoint, backend.Root, "Cached online", new ISongDetail[] { song });
            var online = new OnlineSongCollection(endpoint, "Online", new ISongDetail[] { song });
            var explicitVirtual = new SongCollection(backend.Root, "Flagged virtual", new ISongDetail[] { song }) { IsVirtual = true };
            backend.EnumerationFailures[backend.Root.Location] = new UnauthorizedAccessException("No-op refresh must never enumerate this cache.");
            var probe = new ParseProbe();
            SongDetail.Probe = probe;
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            foreach (var collection in new SongCollection[] { virtualCollection, emptyVirtual, cachedOnline, online, explicitVirtual })
            {
                var id = collection.Id;
                var original = collection.ToArray();
                collection.Refresh(canceled.Token);
                await collection.RefreshAsync(canceled.Token);
                Require(collection.Id == id && collection.ToArray().SequenceEqual(original), "Virtual/online refresh must not change ID, contents or cursor.");
            }
            Require(probe.StartedCount == 0 && !song.IsDisposed, "Virtual and online refreshes must perform neither parsing nor disposal.");
            Require(virtualCollection.Directory is null && virtualCollection.IsVirtual && virtualCollection.Path == string.Empty, "Directory-less collections must retain virtual path semantics.");
            Require(cachedOnline.IsOnline && ReferenceEquals(cachedOnline.Directory, backend.Root) && cachedOnline.Path == backend.Root.Location, "An online cache must retain its provider handle without becoming locally refreshable.");
            Console.WriteLine("PASS collection: virtual/online no-op refresh and cached opaque paths");
        }

        /// <summary>Creates one fixture directory inside the case-owned root.</summary>
        /// <param name="rootPath">The case-owned fixture root.</param>
        /// <param name="name">The immediate fixture directory name.</param>
        /// <returns>The absolute fixture directory path.</returns>
        private static string CreateFixtureDirectory(string rootPath, string name)
        {
            var path = Path.Combine(rootPath, name);
            NativeDirectory.CreateDirectory(path);
            return path;
        }

        /// <summary>Creates or updates fake maidata and a small inert track file inside an ignored fixture.</summary>
        /// <param name="collectionPath">The case-owned collection path.</param>
        /// <param name="name">The immediate fake chart-directory name.</param>
        /// <param name="metadata">The fake metadata, never interpreted by Simai.</param>
        /// <param name="trackName">The track filename tested by production candidate detection.</param>
        /// <returns>The chart directory's absolute fixture path.</returns>
        private static string WriteChart(string collectionPath, string name, ChartFixture metadata, string trackName = "track.mp3")
        {
            var path = CreateFixtureDirectory(collectionPath, name);
            NativeFile.WriteAllText(Path.Combine(path, "maidata.txt"), JsonSerializer.Serialize(metadata));
            NativeFile.WriteAllBytes(Path.Combine(path, trackName), new byte[] { 1 });
            return path;
        }

        /// <summary>Checks visible chart order using stable fixture hashes.</summary>
        /// <param name="collection">The collection snapshot under test.</param>
        /// <param name="hashes">The complete expected sequence of hashes.</param>
        private static void RequireHashes(SongCollection collection, params string[] hashes)
        {
            Require(collection.Select(song => song.Hash).SequenceEqual(hashes), "Unexpected visible chart sequence: " + string.Join(", ", collection.Select(song => song.Hash)));
        }

        /// <summary>Checks that a failed refresh did not mutate, replace or dispose the previous snapshot.</summary>
        /// <param name="collection">The local collection under test.</param>
        /// <param name="original">The exact previous song references.</param>
        /// <param name="id">The previous collection identity.</param>
        /// <param name="cursorHash">The previously selected chart identity.</param>
        private static void RequireUnchangedCollection(SongCollection collection, ISongDetail[] original, Guid id, string cursorHash)
        {
            Require(collection.Id == id && collection.IsSorted && collection.Current.Hash == cursorHash, "Failure/cancellation must preserve collection ID, order mode and cursor.");
            Require(collection.ToArray().SequenceEqual(original) && original.Cast<SongDetail>().All(song => !song.IsDisposed), "Failure/cancellation must preserve the original undisposed song instances.");
        }

        /// <summary>Asserts that an asynchronous production operation propagates a specific exception type.</summary>
        /// <typeparam name="TException">The required exception type.</typeparam>
        /// <param name="action">The operation under test.</param>
        /// <returns>A task completing when the required exception has been observed.</returns>
        /// <exception cref="InvalidOperationException">The operation completes without the required exception.</exception>
        private static async Task ExpectExceptionAsync<TException>(Func<Task> action) where TException : Exception
        {
            try
            {
                await action().WaitAsync(TimeSpan.FromSeconds(20));
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException("Expected " + typeof(TException).Name + " was not propagated.");
        }
    }
}
