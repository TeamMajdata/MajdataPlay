using System;
using System.Collections.Generic;
using System.Linq;
using MajdataPlay.Collections;
using MajdataPlay.Tests.Game.Misc.Support;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Collections
{
    /// <summary>Covers adding, removing and cursor handling of <see cref="MyFavoriteSongCollection"/>.</summary>
    public class MyFavoriteSongCollectionTests
    {
        /// <summary>Gets the visible hashes in order.</summary>
        /// <param name="collection">The collection to read.</param>
        /// <returns>The visible hashes.</returns>
        private static string[] Hashes(SongCollection collection)
        {
            return collection.ToArray().Select(song => song.Hash).ToArray();
        }

        /// <summary>Checks the identity and type of a new favorites collection.</summary>
        [Test]
        public void NewCollection_IsEmptyFavoriteList()
        {
            var favorites = new MyFavoriteSongCollection();

            Assert.That(favorites.Type, Is.EqualTo(ChartStorageType.FavoriteList));
            Assert.That(favorites.Name, Is.EqualTo("MyFavorites"));
            Assert.That(favorites.Id, Is.EqualTo(new Guid("00000000-0000-0000-0000-000000000002")));
            Assert.That(favorites.IsReadOnly, Is.False);
            Assert.That(favorites.IsEmpty, Is.True);
        }

        /// <summary>Checks that songs are added once and found by song or hash.</summary>
        [Test]
        public void Add_IgnoresDuplicates()
        {
            var favorites = new MyFavoriteSongCollection();
            var song = new FakeSongDetail("a");

            favorites.Add(song);
            favorites.Add(new FakeSongDetail("a"));
            favorites.Add(new FakeSongDetail("b"));

            Assert.That(Hashes(favorites), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(favorites.Contains(song), Is.True);
            Assert.That(favorites.Contains("b"), Is.True);
            Assert.That(favorites.Contains("c"), Is.False);
            Assert.That(favorites.ExportHashSet(), Is.EquivalentTo(new[] { "a", "b" }));
        }

        /// <summary>Checks that adding a song keeps the current selection.</summary>
        [Test]
        public void Add_KeepsCurrentSelection()
        {
            var favorites = new MyFavoriteSongCollection();
            favorites.Add(new FakeSongDetail("a"));
            favorites.Add(new FakeSongDetail("b"));
            favorites.Index = 1;

            favorites.Add(new FakeSongDetail("c"));

            Assert.That(favorites.Current.Hash, Is.EqualTo("b"));
        }

        /// <summary>Checks that a song added while a sort is active is appended to the visible songs.</summary>
        [Test]
        public void Add_WhileSorted_AppendsToVisibleSongs()
        {
            var favorites = new MyFavoriteSongCollection();
            favorites.Add(new FakeSongDetail("b", title: "B"));
            favorites.Add(new FakeSongDetail("a", title: "A"));
            favorites.SortAndFilter(new SongOrder { SortBy = SortType.ByTitle });

            favorites.Add(new FakeSongDetail("c", title: "0"));

            Assert.That(Hashes(favorites), Is.EqualTo(new[] { "a", "b", "c" }));
            Assert.That(favorites.GetOriginSnapshot().Select(song => song.Hash), Is.EqualTo(new[] { "b", "a", "c" }));
        }

        /// <summary>Checks that removal reports whether the song was a favorite and keeps the selection on another song.</summary>
        [Test]
        public void Remove_UpdatesSongsAndSelection()
        {
            var favorites = new MyFavoriteSongCollection();
            var a = new FakeSongDetail("a");
            favorites.Add(a);
            favorites.Add(new FakeSongDetail("b"));
            favorites.Add(new FakeSongDetail("c"));
            favorites.Index = 2;

            Assert.That(favorites.Remove(a), Is.True);
            Assert.That(favorites.Remove("missing"), Is.False);

            Assert.That(Hashes(favorites), Is.EqualTo(new[] { "b", "c" }));
            Assert.That(favorites.Current.Hash, Is.EqualTo("c"));
        }

        /// <summary>Checks that removing the selected song while sorted keeps the visible songs consistent.</summary>
        [Test]
        public void Remove_WhileSorted_FiltersVisibleSongs()
        {
            var favorites = new MyFavoriteSongCollection();
            favorites.Add(new FakeSongDetail("b", title: "B"));
            favorites.Add(new FakeSongDetail("a", title: "A"));
            favorites.SortAndFilter(new SongOrder { SortBy = SortType.ByTitle });
            favorites.Index = 0;

            favorites.Remove("a");

            Assert.That(Hashes(favorites), Is.EqualTo(new[] { "b" }));
            Assert.That(favorites.Current.Hash, Is.EqualTo("b"));
        }

        /// <summary>Checks that a hash registered without a matching song is reported as an inconsistent state.</summary>
        [Test]
        public void Remove_HashWithoutSong_Throws()
        {
            var favorites = new MyFavoriteSongCollection(new List<ISongDetail>(), new HashSet<string> { "orphan" });

            Assert.Throws<KeyNotFoundException>(() => favorites.Remove("orphan"));
        }

        /// <summary>Checks that the constructor shares the supplied list and hash set.</summary>
        [Test]
        public void Constructor_UsesSuppliedState()
        {
            var hashes = new HashSet<string> { "a", "pending" };
            var favorites = new MyFavoriteSongCollection(new List<ISongDetail> { new FakeSongDetail("a") }, hashes);

            Assert.That(favorites.ExportHashSet(), Is.SameAs(hashes));
            Assert.That(favorites.Contains("pending"), Is.True);
            Assert.That(favorites.Count, Is.EqualTo(1));
        }

        /// <summary>Checks that Clear removes every song and hash.</summary>
        [Test]
        public void Clear_RemovesEverything()
        {
            var favorites = new MyFavoriteSongCollection();
            favorites.Add(new FakeSongDetail("a"));

            favorites.Clear();

            Assert.That(favorites.IsEmpty, Is.True);
            Assert.That(favorites.Contains("a"), Is.False);
            Assert.That(favorites.Index, Is.EqualTo(0));
        }

        /// <summary>Checks that the songs can be copied into an array at an offset.</summary>
        [Test]
        public void CopyTo_CopiesSongs()
        {
            var favorites = new MyFavoriteSongCollection();
            favorites.Add(new FakeSongDetail("a"));
            favorites.Add(new FakeSongDetail("b"));
            var target = new ISongDetail[3];

            favorites.CopyTo(target, 1);

            Assert.That(target[0], Is.Null);
            Assert.That(target[1].Hash, Is.EqualTo("a"));
            Assert.That(target[2].Hash, Is.EqualTo("b"));
        }
    }
}
