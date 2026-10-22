using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using MajdataPlay.Collections;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Misc.Support;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Collections
{
    /// <summary>Covers the selection cursor, ordering, filtering and identity rules of <see cref="SongCollection"/>.</summary>
    public class SongCollectionTests
    {
        /// <summary>Creates three songs with distinct metadata for ordering and filtering checks.</summary>
        /// <returns>The songs in their original order.</returns>
        private static FakeSongDetail[] CreateSongs()
        {
            var alpha = new FakeSongDetail("hash-a", title: "Bravo", artist: "Zed", designer: "Mia", level: "12", description: "tagged", timestamp: new DateTime(2020, 1, 1));
            var beta = new FakeSongDetail("hash-b", title: "Alpha", artist: "Yan", designer: "Kai", level: "14", timestamp: new DateTime(2022, 1, 1));
            var gamma = new FakeSongDetail("hash-c", title: "Charlie", artist: "Xia", designer: "Lou", level: "13+", timestamp: new DateTime(2021, 1, 1));
            return new[] { alpha, beta, gamma };
        }

        /// <summary>Gets the hashes of the visible songs in order.</summary>
        /// <param name="collection">The collection to read.</param>
        /// <returns>The visible hashes.</returns>
        private static string[] Hashes(SongCollection collection)
        {
            return collection.Select(song => song.Hash).ToArray();
        }

        /// <summary>Checks that null arguments are rejected.</summary>
        [Test]
        public void Constructor_RejectsNullArguments()
        {
            Assert.Throws<ArgumentNullException>(() => new SongCollection(null!, Array.Empty<ISongDetail>()));
            Assert.Throws<ArgumentNullException>(() => new SongCollection("name", null!));
            Assert.Throws<ArgumentNullException>(() => SongCollection.Empty(null!, "name"));
        }

        /// <summary>Checks the identity and flags of virtual collections, including the built-in identities.</summary>
        [Test]
        public void VirtualCollection_UsesBuiltInIdentities()
        {
            var all = new SongCollection("All", Array.Empty<ISongDetail>());
            var favorites = new SongCollection("MyFavorites", Array.Empty<ISongDetail>());
            var other = SongCollection.Empty("Other");
            var unnamed = new SongCollection();

            Assert.That(all.Id, Is.EqualTo(new Guid("00000000-0000-0000-0000-000000000001")));
            Assert.That(favorites.Id, Is.EqualTo(new Guid("00000000-0000-0000-0000-000000000002")));
            Assert.That(other.Id, Is.EqualTo(Guid.Empty));
            Assert.That(other.IsVirtual, Is.True);
            Assert.That(other.Directory, Is.Null);
            Assert.That(other.Path, Is.Empty);
            Assert.That(other.Name, Is.EqualTo("Other"));
            Assert.That(other.IsEmpty, Is.True);
            Assert.That(other.Type, Is.EqualTo(ChartStorageType.List));
            Assert.That(other.IsOnline, Is.False);
            Assert.That(unnamed.Name, Is.Empty);
            Assert.That(unnamed.Count, Is.EqualTo(0));
        }

        /// <summary>Checks that the collection copies the input array instead of sharing it.</summary>
        [Test]
        public void Constructor_CopiesSongs()
        {
            var songs = CreateSongs();
            var collection = new SongCollection("List", songs);

            songs[0] = new FakeSongDetail("replaced");

            Assert.That(collection[0].Hash, Is.EqualTo("hash-a"));
            Assert.That(collection.Count, Is.EqualTo(3));
        }

        /// <summary>Checks that an empty collection rejects index assignment and has no current song.</summary>
        [Test]
        public void EmptyCollection_HasNoSelection()
        {
            var collection = SongCollection.Empty("Empty");

            Assert.Throws<ArgumentOutOfRangeException>(() => collection.Index = 0);
            Assert.Throws<IndexOutOfRangeException>(() => _ = collection.Current);
            Assert.That(collection.MoveNext(), Is.False);
            collection.Move(3);
            Assert.That(collection.Index, Is.EqualTo(0));
            Assert.That(collection.ToArray(), Is.Empty);
            Assert.That(collection.GetOriginSnapshot(), Is.Empty);
        }

        /// <summary>Checks that index assignments are clamped to the visible songs.</summary>
        [Test]
        public void Index_IsClamped()
        {
            var collection = new SongCollection("List", CreateSongs());

            collection.Index = 99;
            Assert.That(collection.Index, Is.EqualTo(2));
            Assert.That(collection.Current.Hash, Is.EqualTo("hash-c"));

            collection.Index = -4;
            Assert.That(collection.Index, Is.EqualTo(0));
        }

        /// <summary>Checks that MoveNext advances until the last song.</summary>
        [Test]
        public void MoveNext_StopsAtLastSong()
        {
            var collection = new SongCollection("List", CreateSongs());

            Assert.That(collection.MoveNext(), Is.True);
            Assert.That(collection.MoveNext(), Is.True);
            Assert.That(collection.MoveNext(), Is.False);
            Assert.That(collection.Current.Hash, Is.EqualTo("hash-c"));
        }

        /// <summary>Checks that relative moves are clamped.</summary>
        [Test]
        public void Move_ClampsOffset()
        {
            var collection = new SongCollection("List", CreateSongs());

            collection.Move(10);
            Assert.That(collection.Index, Is.EqualTo(2));
            collection.Move(-1);
            Assert.That(collection.Index, Is.EqualTo(1));
            collection.Move(-10);
            Assert.That(collection.Index, Is.EqualTo(0));
        }

        /// <summary>Checks every sort mode.</summary>
        /// <param name="sortType">The requested order.</param>
        /// <param name="expected">The expected hash order.</param>
        [TestCase(SortType.Default, new[] { "hash-a", "hash-b", "hash-c" })]
        [TestCase(SortType.ByRank, new[] { "hash-a", "hash-b", "hash-c" })]
        [TestCase(SortType.ByTitle, new[] { "hash-b", "hash-a", "hash-c" })]
        [TestCase(SortType.ByTime, new[] { "hash-b", "hash-c", "hash-a" })]
        [TestCase(SortType.ByDiff, new[] { "hash-b", "hash-c", "hash-a" })]
        [TestCase(SortType.ByDes, new[] { "hash-b", "hash-c", "hash-a" })]
        public void SortAndFilter_SortsByMode(SortType sortType, string[] expected)
        {
            var collection = new SongCollection("List", CreateSongs());

            collection.SortAndFilter(new SongOrder { SortBy = sortType });

            Assert.That(Hashes(collection), Is.EqualTo(expected));
            Assert.That(collection.IsSorted, Is.True);
        }

        /// <summary>Checks the keyword filter against every searchable field.</summary>
        /// <param name="keyword">The search keyword.</param>
        /// <param name="expected">The expected hashes.</param>
        [TestCase("charlie", new[] { "hash-c" })]
        [TestCase("YAN", new[] { "hash-b" })]
        [TestCase("mi", new[] { "hash-a" })]
        [TestCase("13+", new[] { "hash-c" })]
        [TestCase("1", new string[0])]
        [TestCase("tag", new[] { "hash-a" })]
        [TestCase("", new[] { "hash-a", "hash-b", "hash-c" })]
        public void SortAndFilter_FiltersByKeyword(string keyword, string[] expected)
        {
            var collection = new SongCollection("List", CreateSongs());

            collection.SortAndFilter(new SongOrder { Keyword = keyword });

            Assert.That(Hashes(collection), Is.EqualTo(expected));
        }

        /// <summary>Checks that a null keyword is treated as no filter and null options are rejected.</summary>
        [Test]
        public void SortAndFilter_HandlesNullInputs()
        {
            var collection = new SongCollection("List", CreateSongs());

            collection.SortAndFilter(new SongOrder { Keyword = null! });

            Assert.That(collection.Count, Is.EqualTo(3));
            Assert.Throws<ArgumentNullException>(() => collection.SortAndFilter(null!));
            Assert.Throws<ArgumentNullException>(() => collection.SortAndFilterAsync(null!));
        }

        /// <summary>Checks that courses and recommendations keep their server order.</summary>
        /// <param name="type">The collection type that ignores ordering.</param>
        [TestCase(ChartStorageType.Dan)]
        [TestCase(ChartStorageType.Recommend)]
        public void SortAndFilter_IgnoresFixedOrderTypes(ChartStorageType type)
        {
            var collection = new SongCollection("Course", CreateSongs()) { Type = type };

            collection.SortAndFilter(new SongOrder { SortBy = SortType.ByTitle, Keyword = "charlie" });

            Assert.That(Hashes(collection), Is.EqualTo(new[] { "hash-a", "hash-b", "hash-c" }));
            Assert.That(collection.IsSorted, Is.False);
        }

        /// <summary>Checks that the selected song stays selected when the order changes.</summary>
        [Test]
        public void SortAndFilter_KeepsSelectedSong()
        {
            var collection = new SongCollection("List", CreateSongs());
            collection.Index = 2;

            collection.SortAndFilter(new SongOrder { SortBy = SortType.ByTime });

            Assert.That(collection.Current.Hash, Is.EqualTo("hash-c"));
            Assert.That(collection.Index, Is.EqualTo(1));
        }

        /// <summary>Checks that a selection survives a filter that hides every song and is restored by Reset.</summary>
        [Test]
        public void Reset_RestoresSelectionAfterEmptyFilter()
        {
            var collection = new SongCollection("List", CreateSongs());
            collection.Index = 1;

            collection.SortAndFilter(new SongOrder { Keyword = "nothing matches" });
            Assert.That(collection.IsEmpty, Is.True);

            collection.Reset();

            Assert.That(collection.IsSorted, Is.False);
            Assert.That(collection.Count, Is.EqualTo(3));
            Assert.That(collection.Current.Hash, Is.EqualTo("hash-b"));
        }

        /// <summary>Checks that Reset on an unsorted collection keeps the current state.</summary>
        [Test]
        public void Reset_WithoutSorting_KeepsState()
        {
            var collection = new SongCollection("List", CreateSongs());
            collection.Index = 2;

            collection.Reset();

            Assert.That(collection.Index, Is.EqualTo(2));
            Assert.That(collection.IsSorted, Is.False);
        }

        /// <summary>Checks that the cursor can be moved by song or hash and falls back to the first song.</summary>
        [Test]
        public void SetCursor_SelectsByHash()
        {
            var songs = CreateSongs();
            var collection = new SongCollection("List", songs);

            collection.SetCursor(songs[2]);
            Assert.That(collection.Index, Is.EqualTo(2));

            collection.SetCursor("unknown");
            Assert.That(collection.Index, Is.EqualTo(0));

            Assert.Throws<ArgumentNullException>(() => collection.SetCursor((ISongDetail)null!));
        }

        /// <summary>Checks that a hash set on an empty view is remembered until the songs become visible again.</summary>
        [Test]
        public void SetCursor_OnEmptyView_IsRemembered()
        {
            var collection = new SongCollection("List", CreateSongs());
            collection.SortAndFilter(new SongOrder { Keyword = "nothing matches" });

            collection.SetCursor("hash-c");
            collection.Reset();

            Assert.That(collection.Current.Hash, Is.EqualTo("hash-c"));
        }

        /// <summary>Checks that snapshots are independent copies of the visible and original songs.</summary>
        [Test]
        public void Snapshots_AreCopies()
        {
            var collection = new SongCollection("List", CreateSongs());
            collection.SortAndFilter(new SongOrder { Keyword = "alpha" });

            var visible = collection.ToArray();
            var origin = collection.GetOriginSnapshot();
            visible[0] = new FakeSongDetail("changed");

            Assert.That(visible.Length, Is.EqualTo(1));
            Assert.That(origin.Length, Is.EqualTo(3));
            Assert.That(collection[0].Hash, Is.EqualTo("hash-b"));
        }

        /// <summary>Checks the snapshot enumerator, including its reset and invalid-position behaviour.</summary>
        [Test]
        public void Enumerator_EnumeratesSnapshot()
        {
            var collection = new SongCollection("List", CreateSongs());
            using var enumerator = collection.GetEnumerator();

            Assert.Throws<InvalidOperationException>(() => _ = enumerator.Current);
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(enumerator.Current.Hash, Is.EqualTo("hash-a"));
            Assert.That(((IEnumerator)enumerator).Current, Is.SameAs(enumerator.Current));
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(enumerator.MoveNext(), Is.False);
            Assert.Throws<InvalidOperationException>(() => _ = enumerator.Current);

            enumerator.Reset();

            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(enumerator.Current.Hash, Is.EqualTo("hash-a"));
        }

        /// <summary>Checks that the non-generic enumerator also lists every song.</summary>
        [Test]
        public void NonGenericEnumerator_ListsSongs()
        {
            IEnumerable collection = new SongCollection("List", CreateSongs());
            var count = 0;

            foreach (var item in collection)
            {
                Assert.That(item, Is.InstanceOf<ISongDetail>());
                count++;
            }

            Assert.That(count, Is.EqualTo(3));
        }

        /// <summary>Checks that refreshing a virtual collection leaves it unchanged.</summary>
        [Test]
        public void Refresh_VirtualCollection_DoesNothing()
        {
            var collection = new SongCollection("List", CreateSongs());

            collection.Refresh();

            Assert.That(collection.RefreshAsync().IsCompleted, Is.True);
            Assert.That(collection.Count, Is.EqualTo(3));
        }

        /// <summary>Checks that sorting on a worker thread updates the visible songs.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SortAndFilterAsync_SortsOnWorker()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var collection = new SongCollection("List", CreateSongs());
                var order = new SongOrder { SortBy = SortType.ByTitle };

                var task = collection.SortAndFilterAsync(order);
                order.SortBy = SortType.Default;
                await task;

                Assert.That(Hashes(collection), Is.EqualTo(new[] { "hash-b", "hash-a", "hash-c" }));
            });
        }

        /// <summary>Checks that a directory-backed collection persists a stable identity in its hidden marker directory.</summary>
        [Test]
        public void DirectoryCollection_PersistsIdentity()
        {
            using var temporary = new TemporaryDirectory();
            var directory = temporary.Directory.CreateDirectory("Collection");

            var first = SongCollection.Empty(directory, "Collection");
            var second = new SongCollection(directory, "Collection", Array.Empty<ISongDetail>());

            Assert.That(first.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(second.Id, Is.EqualTo(first.Id));
            Assert.That(first.IsVirtual, Is.False);
            Assert.That(first.Path, Is.EqualTo(directory.Location));
            var idFile = directory.FindDirectory(".MajdataPlay")!.FindFile("id")!;
            Assert.That(Guid.Parse(idFile.ReadAllText()), Is.EqualTo(first.Id));
        }

        /// <summary>Checks that an unreadable identity marker is replaced by a new identity.</summary>
        [Test]
        public void DirectoryCollection_ReplacesInvalidIdentity()
        {
            using var temporary = new TemporaryDirectory();
            var directory = temporary.Directory.CreateDirectory("Collection");
            directory.CreateDirectory(".MajdataPlay").CreateFile("id").WriteAllText("not a guid");

            var collection = SongCollection.Empty(directory, "Collection");

            Assert.That(collection.Id, Is.Not.EqualTo(Guid.Empty));
            var idText = directory.FindDirectory(".MajdataPlay")!.FindFile("id")!.ReadAllText();
            Assert.That(Guid.Parse(idText), Is.EqualTo(collection.Id));
        }

        /// <summary>Checks that a collection cannot be bound to a missing directory.</summary>
        [Test]
        public void DirectoryCollection_MissingDirectory_Throws()
        {
            using var temporary = new TemporaryDirectory();
            var missing = new MajdataPlay.IO.Storage.StorageDirectory(temporary.Directory.FileSystem, System.IO.Path.Combine(temporary.Location, "missing"));

            Assert.Throws<ArgumentException>(() => SongCollection.Empty(missing, "Missing"));
        }

        /// <summary>Checks the online collection flags and that refreshing it never scans its cache.</summary>
        [Test]
        public void OnlineCollection_IsOnlineAndNeverRefreshed()
        {
            using var temporary = new TemporaryDirectory();
            var endpoint = new MajdataPlay.Net.ApiEndpoint { Name = "Server", Url = new Uri("https://example.invalid/api/") };
            var cache = temporary.Directory.CreateDirectory("Cache");
            ChartFixture.CreateChart(cache, "Chart", "Cached");
            var definition = new DanInfo { Name = "Def" };

            var virtualOnline = new OnlineSongCollection(endpoint, "Server", CreateSongs()) { Definition = definition };
            var cachedOnline = new OnlineSongCollection(endpoint, cache, "Server", Array.Empty<ISongDetail>());
            cachedOnline.Refresh();

            Assert.That(virtualOnline.IsOnline, Is.True);
            Assert.That(virtualOnline.Source, Is.SameAs(endpoint));
            Assert.That(virtualOnline.Definition, Is.SameAs(definition));
            Assert.That(virtualOnline.IsVirtual, Is.True);
            Assert.That(cachedOnline.IsOnline, Is.True);
            Assert.That(cachedOnline.IsVirtual, Is.False);
            Assert.That(cachedOnline.IsEmpty, Is.True);
        }
    }
}
