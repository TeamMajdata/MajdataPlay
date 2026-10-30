using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MajdataPlay.Collections;
using MajdataPlay.IO.Storage;
using MajdataPlay.Net;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Misc.Support;
using MajdataPlay.Utils;
using NUnit.Framework;
using SQLite;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Base
{
    /// <summary>
    /// Covers the published song library of <see cref="SongStorage"/>: initialization, local refreshes, playlists,
    /// favorites, selection restoration and the recommendation folder helpers. Online services stay disabled.
    /// </summary>
    public class SongStorageTests
    {
        /// <summary>The static fields of <see cref="SongStorage"/> captured and restored around each test.</summary>
        private static readonly string[] s_fieldNames =
        {
            "s_collectionIndex",
            "s_totalChartCount",
            "s_allCharts",
            "s_localPlaylistDefinitions",
            "s_myFavorites",
            "s_isInitialized",
            "s_favoritesDatabase",
            "s_recommendCharts",
            "s_recommendGeneration",
        };

        /// <summary>Records progress messages synchronously.</summary>
        private sealed class ProgressRecorder : IProgress<string>
        {
            /// <summary>Gets the reported messages.</summary>
            public ConcurrentQueue<string> Messages { get; } = new();

            /// <summary>Records one message.</summary>
            /// <param name="value">The message.</param>
            public void Report(string value)
            {
                Messages.Enqueue(value);
            }
        }

        /// <summary>The captured static field values.</summary>
        private readonly Dictionary<string, object?> _savedFields = new();

        /// <summary>The captured published collections.</summary>
        private SongCollection[] _savedCollections = Array.Empty<SongCollection>();

        /// <summary>The temporary storage.</summary>
        private TemporaryDirectory _temporary = null!;

        /// <summary>The scope restoring the application directories.</summary>
        private MajEnvScope _environment = null!;

        /// <summary>The chart root of the temporary layout.</summary>
        private StorageDirectory _charts = null!;

        /// <summary>Gets the recommendation feed states.</summary>
        private static IDictionary RecommendStates => PrivateMember.GetStaticField<IDictionary>(typeof(SongStorage), "s_recommendStates");

        /// <summary>Points the library at an empty temporary chart root.</summary>
        [SetUp]
        public void SetUp()
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(MajEnv).TypeHandle);
            foreach (var name in s_fieldNames)
            {
                _savedFields[name] = PrivateMember.GetStaticField<object?>(typeof(SongStorage), name);
            }
            _savedCollections = SongStorage.Collections;
            _temporary = new TemporaryDirectory();
            _environment = new MajEnvScope();
            _environment.UseTemporaryLayout(_temporary);
            _charts = MajEnv.ChartDirectory;
            PrivateMember.SetStaticField(typeof(SongStorage), "s_isInitialized", false);
            PrivateMember.SetStaticField(typeof(SongStorage), "s_favoritesDatabase", null);
            PrivateMember.SetStaticField(typeof(SongStorage), "s_myFavorites", new MyFavoriteSongCollection());
            PrivateMember.SetStaticField(typeof(SongStorage), "s_allCharts", Array.Empty<ISongDetail>());
            PrivateMember.SetStaticField(typeof(SongStorage), "s_recommendCharts", Array.Empty<ISongDetail>());
            PrivateMember.SetStaticField(typeof(SongStorage), "s_collectionIndex", 0);
            PrivateMember.SetStaticProperty(typeof(SongStorage), nameof(SongStorage.Collections), Array.Empty<SongCollection>());
            RecommendStates.Clear();
        }

        /// <summary>Removes the event handler and restores the library; the scope closes the runtime database after its queued writes.</summary>
        [TearDown]
        public void TearDown()
        {
            RemoveHandler(nameof(GameManager.OnSave), "OnSave");
            foreach (var pair in _savedFields)
            {
                PrivateMember.SetStaticField(typeof(SongStorage), pair.Key, pair.Value);
            }
            PrivateMember.SetStaticProperty(typeof(SongStorage), nameof(SongStorage.Collections), _savedCollections);
            RecommendStates.Clear();
            _environment.Dispose();
            _temporary.Dispose();
        }

        /// <summary>Removes a private <see cref="SongStorage"/> handler from a <see cref="GameManager"/> event.</summary>
        /// <param name="eventName">The event name.</param>
        /// <param name="methodName">The handler method name.</param>
        private static void RemoveHandler(string eventName, string methodName)
        {
            var method = typeof(SongStorage).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!;
            var handler = (EventHandler<EventArgs?>)Delegate.CreateDelegate(typeof(EventHandler<EventArgs?>), method);
            var eventInfo = typeof(GameManager).GetEvent(eventName)!;
            eventInfo.RemoveEventHandler(null, handler);
        }

        /// <summary>Gets the runtime database once the library has prepared its favorites table, or null.</summary>
        private static MajDatabaseHandle? FavoritesDatabase => PrivateMember.GetStaticField<MajDatabaseHandle?>(typeof(SongStorage), "s_favoritesDatabase");

        /// <summary>Counts the stored rows of one favorite hash after the queued writes have finished.</summary>
        /// <param name="hash">The favorite hash.</param>
        /// <returns>The number of stored rows.</returns>
        private static async UniTask<int> CountStoredFavoriteAsync(string hash)
        {
            await SongStorage.WaitForFavoriteWritesAsync();
            return await FavoritesDatabase!.ReadAsync(connection => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM FavoriteHashes WHERE Hash = ?", hash));
        }

        /// <summary>Opens the favorites database file with a separate connection and counts its rows.</summary>
        /// <returns>The number of stored favorite rows.</returns>
        private static async UniTask<int> CountRowsInDatabaseFileAsync()
        {
            var file = MajEnv.RuntimeDirectory.FindFile("runtime.db")!;
            var connection = new SQLiteAsyncConnection(file.Location, SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex);
            try
            {
                return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM FavoriteHashes");
            }
            finally
            {
                await connection.CloseAsync();
            }
        }

        /// <summary>Creates two collections with two charts each.</summary>
        private void CreateLibrary()
        {
            var first = _charts.CreateDirectory("A-Collection");
            ChartFixture.CreateChart(first, "One", "One");
            ChartFixture.CreateChart(first, "Two", "Two");
            var second = _charts.CreateDirectory("B-Collection");
            ChartFixture.CreateChart(second, "Three", "Three");
            ChartFixture.CreateChart(second, "Four", "Four");
        }

        /// <summary>Gets a published collection by name.</summary>
        /// <param name="name">The collection name.</param>
        /// <returns>The collection.</returns>
        private static SongCollection Collection(string name)
        {
            return SongStorage.Collections.Single(collection => collection.Name == name);
        }

        /// <summary>Gets the hash of a published song by title.</summary>
        /// <param name="title">The song title.</param>
        /// <returns>The chart hash.</returns>
        private static string HashOf(string title)
        {
            return Collection("All").Single(song => song.Title == title).Hash;
        }

        /// <summary>Checks that refreshes are ignored until the library is initialized and that an empty library is reported.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RefreshLocal_BeforeInit_DoesNothing()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateLibrary();

                await SongStorage.RefreshLocal();

                Assert.That(SongStorage.Collections, Is.Empty);
                Assert.That(SongStorage.IsEmpty, Is.True);
                Assert.That(SongStorage.WorkingCollection.IsEmpty, Is.True);
                Assert.That(SongStorage.WorkingCollection.Name, Is.EqualTo("default"));
                SongStorage.CollectionIndex = 5;
                Assert.That(SongStorage.CollectionIndex, Is.EqualTo(0));
            });
        }

        /// <summary>Checks that initialization creates a missing chart root and the favorites database.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator InitAsync_CreatesStorage()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _charts.Delete(true);

                await SongStorage.InitAsync();
                await SongStorage.InitAsync();

                Assert.That(_charts.Exists, Is.True);
                Assert.That(_charts.FindDirectory("default"), Is.Not.Null);
                Assert.That(MajEnv.RuntimeDirectory.FindFile("runtime.db"), Is.Not.Null);
                Assert.That(PrivateMember.GetStaticField<bool>(typeof(SongStorage), "s_isInitialized"), Is.True);
            });
        }

        /// <summary>Checks that a local refresh publishes collections, the aggregate collection, favorites and playlists.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RefreshLocal_PublishesLibrary()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateLibrary();
                var progress = new ProgressRecorder();
                await SongStorage.InitAsync();

                await SongStorage.RefreshLocal(progress);

                Assert.That(SongStorage.Collections.Select(collection => collection.Name),
                            Is.EqualTo(new[] { "A-Collection", "B-Collection", "All", "MyFavorites" }));
                Assert.That(SongStorage.TotalChartCount, Is.EqualTo(4));
                Assert.That(SongStorage.IsEmpty, Is.False);
                Assert.That(Collection("All").Count, Is.EqualTo(4));
                Assert.That(Collection("A-Collection").Type, Is.EqualTo(ChartStorageType.List));
                Assert.That(Collection("MyFavorites").Type, Is.EqualTo(ChartStorageType.FavoriteList));
                Assert.That(progress.Messages, Is.Not.Empty);
                Assert.That(SongStorage.WorkingCollection.Name, Is.EqualTo("A-Collection"));
                Assert.That(MajEnv.RuntimeConfig!.List.SelectedDirGuid, Is.EqualTo(Guid.Empty));
                Assert.That(MajEnv.RuntimeConfig.List.SelectedSongId, Is.Empty);
                Assert.That(SongStorage.OrderBy, Is.SameAs(MajEnv.RuntimeConfig.List.OrderBy));
            });
        }

        /// <summary>Checks that playlists and courses resolve their charts in the requested order and invalid files are skipped.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RefreshLocal_ResolvesPlaylists()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateLibrary();
                await SongStorage.InitAsync();
                await SongStorage.RefreshLocal();
                var three = HashOf("Three");
                var one = HashOf("One");
                _charts.CreateFile("course.json").WriteAllText(Serializer.Json.Serialize(new DanInfo { Name = "Course", SongHashs = new[] { three, "missing", one } }));
                _charts.CreateFile("list.json").WriteAllText(Serializer.Json.Serialize(new DanInfo { Name = "Playlist", SongHashs = new[] { one }, IsPlayList = true }));
                _charts.CreateFile("unresolved.json").WriteAllText(Serializer.Json.Serialize(new DanInfo { Name = "Nothing", SongHashs = new[] { "missing" } }));
                _charts.CreateFile("nameless.json").WriteAllText("{\"SongHashs\":[\"x\"]}");
                _charts.CreateFile("broken.json").WriteAllText("{ broken");

                await SongStorage.RefreshLocal();

                var course = Collection("Course");
                Assert.That(course.Type, Is.EqualTo(ChartStorageType.Dan));
                Assert.That(course.DanInfo, Is.Not.Null);
                Assert.That(course.Select(song => song.Hash), Is.EqualTo(new[] { three, one }));
                Assert.That(course.Id, Is.Not.EqualTo(Guid.Empty));
                var playlist = Collection("Playlist");
                Assert.That(playlist.Type, Is.EqualTo(ChartStorageType.PlayList));
                Assert.That(playlist.DanInfo, Is.Null);
                Assert.That(SongStorage.Collections.Any(collection => collection.Name == "Nothing"), Is.False);
            });
        }

        /// <summary>Checks that the selection is restored by collection identity and chart identity after a refresh.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RefreshLocal_RestoresSelection()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateLibrary();
                await SongStorage.InitAsync();
                await SongStorage.RefreshLocal();
                var listConfig = MajEnv.RuntimeConfig!.List;
                SongStorage.CollectionIndex = 1;
                var selected = SongStorage.WorkingCollection;
                selected.SetCursor(selected.Single(song => song.Title == "Four"));
                listConfig.SelectedDirGuid = selected.Id;
                listConfig.SelectedSongId = selected.Current.Id;
                selected.Index = 0;
                SongStorage.CollectionIndex = 0;
                ChartFixture.CreateChart(_charts.CreateDirectory("0-First"), "Zero", "Zero");

                await SongStorage.RefreshLocal();

                Assert.That(SongStorage.WorkingCollection.Name, Is.EqualTo("B-Collection"));
                Assert.That(SongStorage.CollectionIndex, Is.EqualTo(2));
                Assert.That(SongStorage.WorkingCollection.Current.Title, Is.EqualTo("Four"));
                Assert.That(listConfig.SelectedDirGuid, Is.EqualTo(selected.Id));
                Assert.That(listConfig.SelectedSongId, Is.EqualTo(SongStorage.WorkingCollection.Current.Id));
            });
        }

        /// <summary>Checks that unavailable saved content leaves both persisted identities intact.</summary>
        /// <param name="collectionExists">Whether the saved collection is published.</param>
        /// <param name="collectionIsEmpty">Whether the saved collection contains no songs.</param>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void RestoreSelection_PreservesUnavailableIdentities(bool collectionExists, bool collectionIsEmpty)
        {
            var listConfig = MajEnv.RuntimeConfig!.List;
            var selectedId = Guid.NewGuid();
            var fallback = new SongCollection("Fallback", new ISongDetail[] { new FakeSongDetail("saved-song") })
            {
                Id = Guid.NewGuid()
            };
            var selected = new SongCollection("Selected", collectionIsEmpty
                ? Array.Empty<ISongDetail>()
                : new ISongDetail[] { new FakeSongDetail("other-song") })
            {
                Id = selectedId
            };
            var collections = collectionExists ? new[] { fallback, selected } : new[] { fallback };
            PrivateMember.SetStaticProperty(typeof(SongStorage), nameof(SongStorage.Collections), collections);
            listConfig.SelectedDirGuid = selectedId;
            listConfig.SelectedSongId = "saved-song";

            SongStorage.RestoreSelection();

            Assert.That(SongStorage.WorkingCollection, Is.SameAs(collectionExists ? selected : fallback));
            Assert.That(listConfig.SelectedDirGuid, Is.EqualTo(selectedId));
            Assert.That(listConfig.SelectedSongId, Is.EqualTo("saved-song"));
        }

        /// <summary>Checks that an empty library cannot replace the saved selection with the placeholder collection.</summary>
        [Test]
        public void RestoreSelection_EmptyLibraryPreservesSavedIdentities()
        {
            var listConfig = MajEnv.RuntimeConfig!.List;
            var selectedId = Guid.NewGuid();
            listConfig.SelectedDirGuid = selectedId;
            listConfig.SelectedSongId = "saved-song";

            SongStorage.RestoreSelection();

            Assert.That(SongStorage.WorkingCollection.IsEmpty, Is.True);
            Assert.That(listConfig.SelectedDirGuid, Is.EqualTo(selectedId));
            Assert.That(listConfig.SelectedSongId, Is.EqualTo("saved-song"));
        }

        /// <summary>Checks that recommendation publication retains missing identities and restores them when content returns.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PublishRecommendCollections_RestoresSavedSelectionWhenContentReturns()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var listConfig = MajEnv.RuntimeConfig!.List;
                var selectedId = Guid.NewGuid();
                var fallback = new SongCollection("Fallback", new ISongDetail[] { new FakeSongDetail("fallback-song") })
                {
                    Id = Guid.NewGuid()
                };
                listConfig.SelectedDirGuid = selectedId;
                listConfig.SelectedSongId = "saved-song";

                await PrivateMember.InvokeStatic<Task>(typeof(SongStorage), "PublishRecommendCollectionsAsync", (object)new[] { fallback });

                Assert.That(listConfig.SelectedDirGuid, Is.EqualTo(selectedId));
                Assert.That(listConfig.SelectedSongId, Is.EqualTo("saved-song"));
                var missingSong = new SongCollection("Recommendations", new ISongDetail[] { new FakeSongDetail("other-song") })
                {
                    Id = selectedId,
                    Type = ChartStorageType.Recommend
                };

                await PrivateMember.InvokeStatic<Task>(typeof(SongStorage), "PublishRecommendCollectionsAsync", (object)new[] { fallback, missingSong });

                Assert.That(SongStorage.WorkingCollection, Is.SameAs(missingSong));
                Assert.That(listConfig.SelectedDirGuid, Is.EqualTo(selectedId));
                Assert.That(listConfig.SelectedSongId, Is.EqualTo("saved-song"));
                var restored = new SongCollection("Recommendations", new ISongDetail[]
                {
                    new FakeSongDetail("other-song"),
                    new FakeSongDetail("saved-song")
                })
                {
                    Id = selectedId,
                    Type = ChartStorageType.Recommend
                };

                await PrivateMember.InvokeStatic<Task>(typeof(SongStorage), "PublishRecommendCollectionsAsync", (object)new[] { fallback, restored });

                Assert.That(SongStorage.WorkingCollection, Is.SameAs(restored));
                Assert.That(restored.Current.Hash, Is.EqualTo("saved-song"));
                Assert.That(listConfig.SelectedDirGuid, Is.EqualTo(selectedId));
                Assert.That(listConfig.SelectedSongId, Is.EqualTo("saved-song"));

                await PrivateMember.InvokeStatic<Task>(typeof(SongStorage), "PublishRecommendCollectionsAsync", (object)new[] { fallback });

                Assert.That(SongStorage.CollectionIndex, Is.EqualTo(0));
                Assert.That(SongStorage.WorkingCollection, Is.SameAs(fallback));
                Assert.That(listConfig.SelectedDirGuid, Is.EqualTo(selectedId));
                Assert.That(listConfig.SelectedSongId, Is.EqualTo("saved-song"));
            });
        }

        /// <summary>Checks that an online refresh with online functions disabled keeps the local library.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RefreshOnline_WhenDisabled_KeepsLocalCollections()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateLibrary();
                await SongStorage.InitAsync();
                await SongStorage.RefreshLocal();
                var before = Collection("A-Collection");

                await SongStorage.RefreshOnline();

                Assert.That(Collection("A-Collection"), Is.SameAs(before));
                Assert.That(SongStorage.TotalChartCount, Is.EqualTo(4));
                Assert.That(await SongStorage.RefreshUserOnlineFavAsync(), Is.True);
                await SongStorage.RefreshRecommendAsync();
                await SongStorage.RefreshStaleRecommendAsync();
                Assert.That(SongStorage.Collections.Any(collection => collection.Type == ChartStorageType.Recommend), Is.False);
            });
        }

        /// <summary>Checks that favorites can be added, queried, removed and exported.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Favorites_AreTrackedAndExported()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateLibrary();
                await SongStorage.InitAsync();
                await SongStorage.RefreshLocal();
                var one = Collection("All").Single(song => song.Title == "One");
                var two = Collection("All").Single(song => song.Title == "Two");

                SongStorage.AddToMyFavorites(one);
                SongStorage.AddToMyFavorites(two);
                SongStorage.RemoveFromMyFavorites(two);
                SongStorage.AddToMyFavorites(new FakeSongDetail(string.Empty));
                SongStorage.RemoveFromMyFavorites(string.Empty);
                Assert.That(await CountStoredFavoriteAsync(one.Hash), Is.EqualTo(1));

                Assert.That(SongStorage.IsInMyFavorites(one), Is.True);
                Assert.That(SongStorage.IsInMyFavorites(two), Is.False);

                PrivateMember.InvokeStatic(typeof(SongStorage), "OnSave", null, null);
                var exported = Serializer.Json.Deserialize<DanInfo>(_charts.FindFile("MyFavorites.json")!.ReadAllText())!;
                Assert.That(exported.Name, Is.EqualTo("My Favorites"));
                Assert.That(exported.IsPlayList, Is.True);
                Assert.That(exported.SongHashs, Does.Contain(one.Hash));
                Assert.That(exported.SongHashs, Does.Not.Contain(two.Hash));

                await SongStorage.RefreshLocal();
                Assert.That(Collection("MyFavorites").Select(song => song.Hash), Is.EqualTo(new[] { one.Hash }));

                SongStorage.RemoveFromMyFavorites(one.Hash);
                Assert.That(await CountStoredFavoriteAsync(one.Hash), Is.EqualTo(0));
                Assert.That(SongStorage.IsInMyFavorites(one), Is.False);
            });
        }

        /// <summary>Checks that favorite writes reach the database in call order, so the last toggle of a chart wins.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Favorites_WritesKeepCallOrder()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await SongStorage.InitAsync();
                var kept = new FakeSongDetail("kept-hash");
                var dropped = new FakeSongDetail("dropped-hash");

                for (var i = 0; i < 20; i++)
                {
                    SongStorage.AddToMyFavorites(kept);
                    SongStorage.RemoveFromMyFavorites(kept);
                    SongStorage.RemoveFromMyFavorites(dropped);
                    SongStorage.AddToMyFavorites(dropped);
                }
                SongStorage.AddToMyFavorites(kept);
                SongStorage.RemoveFromMyFavorites(dropped.Hash);

                Assert.That(await CountStoredFavoriteAsync(kept.Hash), Is.EqualTo(1));
                Assert.That(await CountStoredFavoriteAsync(dropped.Hash), Is.EqualTo(0));
            });
        }

        /// <summary>
        /// Checks that shutdown waits for every queued favorite write before closing the connection, instead of closing it
        /// while a transaction is still committing.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnAppQuit_WaitsForQueuedWritesBeforeClosing()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await SongStorage.InitAsync();
                for (var i = 0; i < 200; i++)
                {
                    SongStorage.AddToMyFavorites(new FakeSongDetail($"quit-hash-{i}"));
                }

                PrivateMember.InvokeStatic(typeof(MajDatabase), "OnAppQuit", null, null);

                Assert.That(FavoritesDatabase!.IsClosed, Is.True);
                Assert.That(SongStorage.WaitForFavoriteWritesAsync().IsCompleted, Is.True);
                Assert.That(await CountRowsInDatabaseFileAsync(), Is.EqualTo(200));
            });
        }

        /// <summary>Checks that favorite writes after shutdown are ignored instead of using the closed connection.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Favorites_AfterShutdown_AreNotWritten()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await SongStorage.InitAsync();
                PrivateMember.InvokeStatic(typeof(MajDatabase), "OnAppQuit", null, null);

                Assert.DoesNotThrow(() => SongStorage.AddToMyFavorites(new FakeSongDetail("late-hash")));
                await SongStorage.WaitForFavoriteWritesAsync();

                Assert.That(await CountRowsInDatabaseFileAsync(), Is.EqualTo(0));
            });
        }

        /// <summary>Checks that a failing favorite write is observed and does not stop the writes queued after it.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Favorites_FailedWriteDoesNotBlockLaterWrites()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // The failure is logged through MajDebug, which may forward it to the Unity log asynchronously when its
                // writer runs (after Play Mode was entered in the same Editor session); that error is expected here.
                LogAssert.ignoreFailingMessages = true;
                await SongStorage.InitAsync();
                Action<SQLiteConnection> failing = _ => throw new InvalidOperationException("write failed");

                PrivateMember.InvokeStatic(typeof(SongStorage), "QueueFavoriteWrite", failing);
                SongStorage.AddToMyFavorites(new FakeSongDetail("after-failure"));
                var pending = SongStorage.WaitForFavoriteWritesAsync();
                await pending;

                Assert.That(pending.IsFaulted, Is.False);
                Assert.That(await CountStoredFavoriteAsync("after-failure"), Is.EqualTo(1));
            });
        }

        /// <summary>Checks that favorites are loaded from the export file and the legacy cache, and that a corrupt export is backed up.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator InitAsync_LoadsFavoriteSources()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateLibrary();
                MajEnv.RuntimeDirectory.CreateFile("MyFavorites.json").WriteAllText("[\"legacy-hash\",\"\"]");
                _charts.CreateFile("MyFavorites.json").WriteAllText("{\"Name\":\"My Favorites\",\"SongHashs\":[\"export-hash\"],\"IsPlayList\":true}");

                await SongStorage.InitAsync();

                var favorites = PrivateMember.GetStaticField<MyFavoriteSongCollection>(typeof(SongStorage), "s_myFavorites");
                Assert.That(favorites.ExportHashSet(), Is.EquivalentTo(new[] { "legacy-hash", "export-hash" }));
                Assert.That(MajEnv.RuntimeDirectory.FindFile("MyFavorites.json"), Is.Null);
                Assert.That(MajEnv.RuntimeDirectory.FindFile("MyFavorites.json.bak"), Is.Not.Null);
            });
        }

        /// <summary>Creates a SQLite database file holding a favorites table with the given hashes.</summary>
        /// <param name="name">The database file name inside the runtime directory.</param>
        /// <param name="hashes">The favorite hashes to store.</param>
        private static void CreateFavoriteDatabase(string name, params string[] hashes)
        {
            var file = MajEnv.RuntimeDirectory.CreateFile(name);
            using var connection = new SQLiteConnection(file.Location, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex);
            connection.Execute("CREATE TABLE FavoriteHashes (Hash varchar PRIMARY KEY NOT NULL)");
            foreach (var hash in hashes)
            {
                connection.Execute("INSERT INTO FavoriteHashes (Hash) VALUES (?)", hash);
            }
        }

        /// <summary>Checks that the legacy favorites database is merged into the runtime database without duplicates and backed up.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator InitAsync_MergesLegacyFavoriteDatabase()
        {
            return UniTask.ToCoroutine(async () =>
            {
                CreateLibrary();
                CreateFavoriteDatabase("runtime.db", "current-hash", "shared-hash");
                CreateFavoriteDatabase("MajFavorites.db", "legacy-hash", "shared-hash");

                await SongStorage.InitAsync();

                var favorites = PrivateMember.GetStaticField<MyFavoriteSongCollection>(typeof(SongStorage), "s_myFavorites");
                Assert.That(favorites.ExportHashSet(), Is.EquivalentTo(new[] { "current-hash", "shared-hash", "legacy-hash" }));
                Assert.That(await CountStoredFavoriteAsync("shared-hash"), Is.EqualTo(1));
                Assert.That(MajEnv.RuntimeDirectory.FindFile("MajFavorites.db"), Is.Null);
                Assert.That(MajEnv.RuntimeDirectory.FindFile("MajFavorites.db.bak"), Is.Not.Null);
            });
        }

        /// <summary>Checks that the legacy JSON favorites are imported when runtime.db already exists but holds only the runtime configuration.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator InitAsync_ImportsLegacyJsonIntoRuntimeDatabaseWithoutFavoriteTable()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var file = MajEnv.RuntimeDirectory.CreateFile("runtime.db");
                using (var connection = new SQLiteConnection(file.Location, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex))
                {
                    connection.Execute("CREATE TABLE ListConfig (Id integer PRIMARY KEY NOT NULL, SelectedSongHash varchar)");
                }
                MajEnv.RuntimeDirectory.CreateFile("MyFavorites.json").WriteAllText("[\"legacy-hash\"]");

                await SongStorage.InitAsync();

                var favorites = PrivateMember.GetStaticField<MyFavoriteSongCollection>(typeof(SongStorage), "s_myFavorites");
                Assert.That(favorites.ExportHashSet(), Is.EquivalentTo(new[] { "legacy-hash" }));
                Assert.That(MajEnv.RuntimeDirectory.FindFile("MyFavorites.json.bak"), Is.Not.Null);
            });
        }

        /// <summary>Checks that an empty legacy favorites database file is backed up without changing the runtime database.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator InitAsync_BacksUpEmptyLegacyFavoriteDatabase()
        {
            return UniTask.ToCoroutine(async () =>
            {
                MajEnv.RuntimeDirectory.CreateFile("MajFavorites.db");

                await SongStorage.InitAsync();

                Assert.That(await CountRowsInDatabaseFileAsync(), Is.EqualTo(0));
                Assert.That(MajEnv.RuntimeDirectory.FindFile("MajFavorites.db"), Is.Null);
                Assert.That(MajEnv.RuntimeDirectory.FindFile("MajFavorites.db.bak"), Is.Not.Null);
            });
        }

        /// <summary>Checks that an unreadable favorites export is backed up without losing the stored favorites.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator InitAsync_BacksUpCorruptFavoritesExport()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _charts.CreateFile("MyFavorites.json").WriteAllText("{ broken");
                _charts.CreateFile("MyFavorites.json.bak").WriteAllText("older");

                await SongStorage.InitAsync();

                Assert.That(_charts.FindFile("MyFavorites.json.bak")!.ReadAllText(), Is.EqualTo("older"));
                Assert.That(_charts.FindFile("MyFavorites.json.bak.bak")!.ReadAllText(), Is.EqualTo("{ broken"));
            });
        }

        /// <summary>Checks the stable identity of recommendation folders.</summary>
        [Test]
        public void RecommendCollectionId_IsStablePerEndpoint()
        {
            var first = new ApiEndpoint { Name = "A", Url = new Uri("https://a.invalid/api/") };
            var same = new ApiEndpoint { Name = "Other name", Url = new Uri("https://a.invalid/api/") };
            var other = new ApiEndpoint { Name = "A", Url = new Uri("https://b.invalid/api/") };

            var id = PrivateMember.InvokeStatic<Guid>(typeof(SongStorage), "GetRecommendCollectionId", first);

            Assert.That(PrivateMember.InvokeStatic<Guid>(typeof(SongStorage), "GetRecommendCollectionId", same), Is.EqualTo(id));
            Assert.That(PrivateMember.InvokeStatic<Guid>(typeof(SongStorage), "GetRecommendCollectionId", other), Is.Not.EqualTo(id));
            Assert.That(SongStorage.GetRecommendGeneration(first), Is.EqualTo(0));
        }

        /// <summary>Checks that recommendation folders are placed after their endpoint's chart list or appended.</summary>
        [Test]
        public void InsertRecommendCollections_PlacesFoldersAfterLists()
        {
            var a = new ApiEndpoint { Name = "A", Url = new Uri("https://a.invalid/") };
            var b = new ApiEndpoint { Name = "B", Url = new Uri("https://b.invalid/") };
            var local = SongCollection.Empty("Local");
            var listA = new OnlineSongCollection(a, "A", Array.Empty<ISongDetail>());
            var all = SongCollection.Empty("All");
            var recommendA = new OnlineSongCollection(a, "RecA", Array.Empty<ISongDetail>()) { Type = ChartStorageType.Recommend };
            var recommendB = new OnlineSongCollection(b, "RecB", Array.Empty<ISongDetail>()) { Type = ChartStorageType.Recommend };
            var collections = new List<SongCollection> { local, listA, all };

            PrivateMember.InvokeStatic(typeof(SongStorage), "InsertRecommendCollections", collections, new[] { recommendA, recommendB });

            Assert.That(collections, Is.EqualTo(new SongCollection[] { local, listA, recommendA, all, recommendB }));
        }

        /// <summary>Checks that only charts owned by recommendation folders and missing from the base library are reported.</summary>
        [Test]
        public void GetRecommendOnlyCharts_FindsFolderOwnedCharts()
        {
            var endpoint = new ApiEndpoint { Name = "A", Url = new Uri("https://a.invalid/") };
            var baseSong = new FakeSongDetail("base");
            var extra = new FakeSongDetail("extra");
            var collections = new SongCollection[]
            {
                new SongCollection("List", new ISongDetail[] { baseSong }),
                new OnlineSongCollection(endpoint, "Rec", new ISongDetail[] { baseSong, extra, extra }) { Type = ChartStorageType.Recommend },
            };

            var owned = PrivateMember.InvokeStatic<ISongDetail[]>(typeof(SongStorage), "GetRecommendOnlyCharts", collections, new ISongDetail[] { baseSong });

            Assert.That(owned, Is.EqualTo(new ISongDetail[] { extra }));
        }

        /// <summary>Checks that a recommendation folder keeps server order, drops repeats and reuses known songs.</summary>
        [Test]
        public void CreateRecommendCollection_BuildsFolder()
        {
            var endpoint = new ApiEndpoint { Name = "Server", Url = new Uri("https://a.invalid/api/") };
            var hashA = Convert.ToBase64String(Encoding.UTF8.GetBytes("chart-a"));
            var hashB = Convert.ToBase64String(Encoding.UTF8.GetBytes("chart-b"));
            var knownDetail = new MajnetSongDetail
            {
                Id = Guid.NewGuid().ToString(),
                Title = "Known",
                Hash = hashA,
                Levels = Array.Empty<string>()
            };
            var known = new OnlineSongDetail(endpoint, knownDetail, OnlineChartCache.Shared.CreateTransientRecord(endpoint, knownDetail));
            var charts = new[]
            {
                new MajnetSongDetail { Id = Guid.NewGuid().ToString(), Title = "B", Hash = hashB, Levels = new[] { "1" } },
                new MajnetSongDetail { Id = Guid.NewGuid().ToString(), Title = "A", Hash = hashA, Levels = new[] { "2" } },
                new MajnetSongDetail { Id = Guid.NewGuid().ToString(), Title = "B again", Hash = hashB, Levels = new[] { "3" } },
            };

            var folder = PrivateMember.InvokeStatic<OnlineSongCollection>(typeof(SongStorage), "CreateRecommendCollection", endpoint, charts, new ISongDetail[] { known });

            Assert.That(folder.Type, Is.EqualTo(ChartStorageType.Recommend));
            Assert.That(folder.Source, Is.SameAs(endpoint));
            Assert.That(folder.Id, Is.EqualTo(PrivateMember.InvokeStatic<Guid>(typeof(SongStorage), "GetRecommendCollectionId", endpoint)));
            Assert.That(folder.Select(song => song.Hash), Is.EqualTo(new[] { hashB, hashA }));
            Assert.That(folder[1], Is.SameAs(known));
            Assert.That(folder[0].Title, Is.EqualTo("B"));
            Assert.That(((OnlineSongDetail)folder[0]).Record.ChartId, Is.EqualTo(charts[0].Id));
            Assert.That(CountOnlineChartRows(charts[0].Id), Is.EqualTo(1));
            ((IDisposable)folder[0]).Dispose();
            known.Dispose();
        }

        /// <summary>Counts the online chart cache rows of a chart.</summary>
        /// <param name="chartId">The chart identity.</param>
        /// <returns>The number of rows.</returns>
        private static int CountOnlineChartRows(string chartId)
        {
            return MajDatabase.Runtime.Read(connection => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM OnlineChartCache WHERE ChartId = ?", chartId));
        }

        /// <summary>Creates the chart list JSON of the stub endpoint.</summary>
        /// <param name="ids">The chart identities.</param>
        /// <returns>The JSON array.</returns>
        private static string ChartListJson(params string[] ids)
        {
            return "[" + string.Join(",", ids.Select(id => $"{{\"id\":\"{id}\",\"title\":\"Title {id}\",\"hash\":\"hash-{id}\",\"levels\":[\"1\"]}}")) + "]";
        }

        /// <summary>Creates the info JSON of the stub endpoint, giving every chart a chart file and a track.</summary>
        /// <param name="ids">The chart identities.</param>
        /// <returns>The JSON array.</returns>
        private static string ChartInfoJson(params string[] ids)
        {
            return "[" + string.Join(",", ids.Select(id =>
                $"{{\"metadata\":{{\"id\":\"{id}\"}},\"resources\":{{" +
                $"\"maidata\":{{\"url\":\"https://cdn.invalid/{id}/maidata.txt\",\"md5\":\"{new string('1', 32)}\",\"sha256\":\"{DigestOf(id, "maidata")}\",\"length\":1}}," +
                $"\"track\":{{\"url\":\"https://cdn.invalid/{id}/track.mp3\",\"md5\":\"{new string('2', 32)}\",\"sha256\":\"{DigestOf(id, "track")}\",\"length\":1}}," +
                "\"video\":null,\"coverArt\":null,\"thumbnail\":null}}")) + "]";
        }

        /// <summary>Derives a distinct digest for a chart's file.</summary>
        /// <param name="id">The chart identity.</param>
        /// <param name="file">The file name.</param>
        /// <returns>The lowercase hexadecimal SHA-256 digest of the chart identity and file name.</returns>
        private static string DigestOf(string id, string file)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            return ObjectStore.ToHex(sha256.ComputeHash(Encoding.UTF8.GetBytes(id + "/" + file)));
        }

        /// <summary>Loads one endpoint's online collection through the private loader.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <returns>The collection.</returns>
        private static async UniTask<SongCollection> GetOnlineCollectionAsync(ApiEndpoint endpoint)
        {
            return await PrivateMember.InvokeStatic<Task<SongCollection>>(typeof(SongStorage), "GetOnlineCollection", endpoint, null, default(System.Threading.CancellationToken))!;
        }

        /// <summary>Checks that an online refresh records the info API's digests and gives every song its record.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetOnlineCollection_SyncsChartInfo()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var http = new StubHttpScope();
                var endpoint = new ApiEndpoint { Name = "Server", Url = new Uri("https://api.invalid/api/") };
                var ids = new[] { Guid.NewGuid().ToString(), Guid.NewGuid().ToString() };
                http.Responder = request => request.RequestUri!.AbsolutePath.EndsWith("maichart/list")
                    ? StubHttpScope.Respond(System.Net.HttpStatusCode.OK, ChartListJson(ids))
                    : StubHttpScope.Respond(System.Net.HttpStatusCode.OK, ChartInfoJson(ids));

                var collection = await GetOnlineCollectionAsync(endpoint);

                var songs = collection.Cast<OnlineSongDetail>().ToArray();
                Assert.That(songs.Select(song => song.Id), Is.EqualTo(ids));
                Assert.That(songs.All(song => song.Record.IsResolved), Is.True);
                Assert.That(songs[1].Record.TryGetDigest(OnlineChartResourceKind.Track, out var track), Is.True);
                Assert.That(track.Sha256, Is.EqualTo(DigestOf(ids[1], "track")));
                Assert.That(songs[1].Record.GetUrl(OnlineChartResourceKind.Track), Is.EqualTo(new Uri($"https://cdn.invalid/{ids[1]}/track.mp3")));
                Assert.That(http.Requests.Single(request => request.Method == System.Net.Http.HttpMethod.Post).Uri.AbsolutePath, Does.EndWith("maichart/info"));
                Assert.That(CountOnlineChartRows(ids[0]), Is.EqualTo(1));
                foreach (var song in songs)
                {
                    song.Dispose();
                }
            });
        }

        /// <summary>Checks that songs of an endpoint without the info API still get records, which learn digests from downloads.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetOnlineCollection_WithoutChartInfo_CreatesUnresolvedRecords()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var http = new StubHttpScope();
                var endpoint = new ApiEndpoint { Name = "Server", Url = new Uri("https://api.invalid/api/") };
                var id = Guid.NewGuid().ToString();
                http.Responder = request => request.RequestUri!.AbsolutePath.EndsWith("maichart/list")
                    ? StubHttpScope.Respond(System.Net.HttpStatusCode.OK, ChartListJson(id))
                    : StubHttpScope.Respond(System.Net.HttpStatusCode.NotFound, string.Empty);

                var collection = await GetOnlineCollectionAsync(endpoint);

                var song = (OnlineSongDetail)collection.Single();
                Assert.That(song.Record.IsResolved, Is.False);
                Assert.That(song.Record.TryGetDigest(OnlineChartResourceKind.Maidata, out _), Is.False);
                Assert.That(song.Record.IsMissing(OnlineChartResourceKind.Video), Is.False);
                Assert.That(CountOnlineChartRows(id), Is.EqualTo(1));
                song.Dispose();
            });
        }

        /// <summary>Checks that the offline collection lists only the charts whose chart file and track are stored.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetCachedOnlineCollection_ListsStoredCharts()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var http = new StubHttpScope();
                var endpoint = new ApiEndpoint { Name = "Server", Url = new Uri("https://api.invalid/api/") };
                var ids = new[] { Guid.NewGuid().ToString(), Guid.NewGuid().ToString() };
                http.Responder = request => request.RequestUri!.AbsolutePath.EndsWith("maichart/list")
                    ? StubHttpScope.Respond(System.Net.HttpStatusCode.OK, ChartListJson(ids))
                    : StubHttpScope.Respond(System.Net.HttpStatusCode.OK, ChartInfoJson(ids));
                foreach (var song in (await GetOnlineCollectionAsync(endpoint)).Cast<OnlineSongDetail>())
                {
                    song.Dispose();
                }
                foreach (var digest in new[] { DigestOf(ids[1], "maidata"), DigestOf(ids[1], "track"), DigestOf(ids[0], "maidata") })
                {
                    ObjectStore.Shared.Directory.CreateDirectory(digest.Substring(0, 2)).CreateFile(digest).WriteAllText("object");
                }
                await OnlineChartCache.Shared.InitializeAsync();
                await ObjectStore.Shared.ScanAsync();
                var cacheDirectory = MajEnv.CacheDirectory.CreateDirectory("Net").CreateDirectory(endpoint.Name);

                var cached = await PrivateMember.InvokeStatic<Task<SongCollection>>(typeof(SongStorage), "GetCachedOnlineCollectionAsync", endpoint, cacheDirectory, default(System.Threading.CancellationToken))!;

                var offline = (OnlineSongDetail)cached.Single();
                Assert.That(cached.IsOnline, Is.True);
                Assert.That(offline.Id, Is.EqualTo(ids[1]));
                Assert.That(offline.Title, Is.EqualTo($"Title {ids[1]}"));
                Assert.That(offline.ServerInfo, Is.SameAs(endpoint));
                Assert.That(offline.Record.TryGetDigest(OnlineChartResourceKind.Track, out var track), Is.True);
                Assert.That(track.Sha256, Is.EqualTo(DigestOf(ids[1], "track")));
                offline.Dispose();
            });
        }

        /// <summary>Checks that playlist identities are derived from the file contents.</summary>
        [Test]
        public void GetGuidFromStream_HashesContents()
        {
            using var first = new MemoryStream(Encoding.UTF8.GetBytes("playlist"));
            using var same = new MemoryStream(Encoding.UTF8.GetBytes("playlist"));
            using var other = new MemoryStream(Encoding.UTF8.GetBytes("other"));

            var id = PrivateMember.InvokeStatic<Guid>(typeof(SongStorage), "GetGuidFromStream", first);

            Assert.That(PrivateMember.InvokeStatic<Guid>(typeof(SongStorage), "GetGuidFromStream", same), Is.EqualTo(id));
            Assert.That(PrivateMember.InvokeStatic<Guid>(typeof(SongStorage), "GetGuidFromStream", other), Is.Not.EqualTo(id));
        }

        /// <summary>Checks the supported resource file name tables.</summary>
        [Test]
        public void SupportedFileNames_CoverEveryFormat()
        {
            Assert.That(SongStorage.ChartFileName, Is.EqualTo("maidata.txt"));
            Assert.That(SongStorage.SupportedTrackFileNames, Is.EquivalentTo(MajEnv.SupportedAudioFormats.Select(extension => "track" + extension)));
            Assert.That(SongStorage.SupportedVideoFileNames, Does.Contain("bg.mp4").And.Contain("pv.webm").And.Contain("mv.mov"));
            Assert.That(SongStorage.SupportedVideoFileNames.Length, Is.EqualTo(MajEnv.SupportedVideoFormats.Length * 3));
            Assert.That(SongStorage.SupportedCoverFileNames, Is.EqualTo(new[] { "bg.png", "bg.jpg" }));
        }
    }
}
