using System;
using System.Collections;
using LitMotion;
using MajdataPlay.Collections;
using MajdataPlay.Scenes.List;
using MajdataPlay.Scenes.List.Models;
using MajdataPlay.Settings;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.ListScene
{
    /// <summary>Covers the collection tabs of the song list: storage snapshot, restore, navigation, difficulty and sorting.</summary>
    public class CollectionListManagerTests : ListSceneTestBase
    {
        /// <summary>The cover list.</summary>
        private CoverListManager _covers = null!;

        /// <summary>The collection list under test.</summary>
        private CollectionListManager _collections = null!;

        /// <summary>The center cover.</summary>
        private CenterCoverDisplayer _center = null!;

        /// <summary>Creates and activates the managers.</summary>
        [SetUp]
        public void SetUp()
        {
            (_covers, _collections, _center) = CreateListManagers(covers: 7, thumbnails: 13, collectionDisplayers: 7);
            ActivateManagers(_covers, _collections);
        }

        /// <summary>Lets the managers start, then initializes the collections.</summary>
        /// <param name="collections">The collections published by the storage.</param>
        /// <returns>The coroutine.</returns>
        private IEnumerator Ready(params SongCollection[] collections)
        {
            yield return null;
            PublishCollections(collections);
            _collections.Init();
        }

        /// <summary>Gets the collection name text.</summary>
        private string Name => PrivateMember.GetField<TextMeshProUGUI>(_collections, "_nameDisplayer").text;

        /// <summary>Gets the collection icon.</summary>
        private Image Icon => PrivateMember.GetField<Image>(_collections, "_iconDisplayer");

        /// <summary>Checks that initialization selects the first collection and its first song when nothing is remembered.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_SelectsFirstCollectionAndSong()
        {
            var songs = CreateSongs("collection-init", 3);
            var first = CreateCollection("First", songs);

            yield return Ready(first, CreateCollection("Second", CreateSongs("collection-init2", 2)));

            Assert.That(_collections.SelectedCollection.Name, Is.EqualTo("First"));
            Assert.That(_collections.SelectedCollection, Is.Not.SameAs(first));
            Assert.That(Name, Is.EqualTo("First"));
            Assert.That(_covers.SelectedSong, Is.SameAs(songs[0]));
            Assert.That(MajEnv.RuntimeConfig!.List.SelectedDirGuid, Is.EqualTo(first.Id));
            Assert.That(MajEnv.RuntimeConfig.List.SelectedDir, Is.EqualTo(0));
        }

        /// <summary>Checks that the remembered collection, song and difficulty are restored.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_RestoresRememberedContext()
        {
            var songs = CreateSongs("collection-restore", 4);
            var second = CreateCollection("Second", songs);
            MajEnv.RuntimeConfig!.List.SelectedDirGuid = second.Id;
            MajEnv.RuntimeConfig.List.SelectedSongHash = songs[2].Hash;
            MajEnv.RuntimeConfig.List.SelectedDiff = ChartLevel.Master;

            yield return Ready(CreateCollection("First", CreateSongs("collection-restore1", 1)), second);

            Assert.That(_collections.SelectedCollection.Name, Is.EqualTo("Second"));
            Assert.That(_covers.SelectedSong, Is.SameAs(songs[2]));
            Assert.That(PrivateMember.GetField<TextMeshProUGUI>(_center, "_selectedLevelTitle").text, Is.EqualTo("Master"));
        }

        /// <summary>Checks that collection navigation wraps around in both directions.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator NextAndPrevious_WrapAround()
        {
            yield return Ready(
                CreateCollection("A", CreateSongs("collection-a", 2)),
                CreateCollection("B", CreateSongs("collection-b", 2)),
                CreateCollection("C", CreateSongs("collection-c", 2)));

            _collections.NextCollection();
            Assert.That(_collections.SelectedCollection.Name, Is.EqualTo("B"));
            PrivateMember.GetField<MotionHandle>(_collections, "_scrollMotion").TryComplete();
            Assert.That(PrivateMember.GetField<float>(_collections, "_listCursorPos"), Is.EqualTo(1f));

            _collections.PreviousCollection();
            _collections.PreviousCollection();
            Assert.That(_collections.SelectedCollection.Name, Is.EqualTo("C"));
            Assert.That(PrivateMember.GetField<float>(_collections, "_listCursorPos"), Is.EqualTo(2f));
            Assert.That(_covers.SelectedSong!.Hash, Does.StartWith("majtest-collection-c"));

            _collections.NextCollection();
            Assert.That(_collections.SelectedCollection.Name, Is.EqualTo("A"));
        }

        /// <summary>Checks that the difficulty wraps through all seven levels and is remembered.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SlideDifficulty_WrapsAndIsRemembered()
        {
            yield return Ready(CreateCollection("A", CreateSongs("collection-diff", 2)));

            _collections.SlideDifficulty(-1);
            Assert.That(MajEnv.RuntimeConfig!.List.SelectedDiff, Is.EqualTo(ChartLevel.UTAGE));
            Assert.That(PrivateMember.GetField<TextMeshProUGUI>(_center, "_selectedLevelTitle").text, Is.EqualTo("UTAGE"));

            _collections.SlideDifficulty(2);
            Assert.That(MajEnv.RuntimeConfig.List.SelectedDiff, Is.EqualTo(ChartLevel.Basic));
        }

        /// <summary>Checks the collection icon of each collection type.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Icons_FollowCollectionType()
        {
            var songs = CreateSongs("collection-icon", 1);
            yield return Ready(
                new SongCollection("List", songs) { Id = Guid.NewGuid() },
                new OnlineSongCollection(CreateEndpoint(), "Online", songs) { Id = Guid.NewGuid() },
                new SongCollection("Recommend", songs) { Id = Guid.NewGuid(), Type = ChartStorageType.Recommend },
                new SongCollection("Dan", songs) { Id = Guid.NewGuid(), Type = ChartStorageType.Dan },
                new SongCollection("PlayList", songs) { Id = Guid.NewGuid(), Type = ChartStorageType.PlayList },
                new SongCollection("Favorite", songs) { Id = Guid.NewGuid(), Type = ChartStorageType.FavoriteList });

            Assert.That(Icon.enabled, Is.False);
            var expected = new[] { "Online", "Online", "Dan", "Folder", "Favorite" };
            foreach (var sprite in expected)
            {
                _collections.NextCollection();
                Assert.That(Icon.enabled, Is.True, _collections.SelectedCollection.Name);
                Assert.That(Icon.sprite.name, Is.EqualTo(sprite), _collections.SelectedCollection.Name);
            }
        }

        /// <summary>Checks that favorites keep their instance and online collections keep their source.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_CopiesCollectionsByKind()
        {
            var songs = CreateSongs("collection-kind", 2);
            var favorites = new SongCollection("Favorites", songs) { Id = Guid.NewGuid(), Type = ChartStorageType.FavoriteList };
            var endpoint = CreateEndpoint();
            var online = new OnlineSongCollection(endpoint, "Online", songs) { Id = Guid.NewGuid() };

            yield return Ready(favorites, online);

            Assert.That(_collections.SelectedCollection, Is.SameAs(favorites));
            _collections.NextCollection();
            Assert.That(_collections.SelectedCollection, Is.InstanceOf<OnlineSongCollection>());
            Assert.That(((OnlineSongCollection)_collections.SelectedCollection).Source, Is.SameAs(endpoint));
            Assert.That(_collections.SelectedCollection.Id, Is.EqualTo(online.Id));
        }

        /// <summary>Checks that ranking order sorts each difficulty by the player's achievement in DX mode.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SortByRank_SortsEachDifficultyByAchievement()
        {
            var songs = CreateSongs("collection-rank", 3);
            ScoreManager.GetScore(songs[0], ChartLevel.Easy).Acc = new Accurate { DX = 90 };
            ScoreManager.GetScore(songs[1], ChartLevel.Easy).Acc = new Accurate { DX = 100 };
            ScoreManager.GetScore(songs[2], ChartLevel.Easy).Acc = new Accurate { DX = 95 };
            ScoreManager.GetScore(songs[2], ChartLevel.Master).Acc = new Accurate { DX = 99 };
            MajEnv.RuntimeConfig!.List.OrderBy.SortBy = SortType.ByRank;
            var recommend = new SongCollection("Recommend", songs) { Id = Guid.NewGuid(), Type = ChartStorageType.Recommend };

            yield return Ready(CreateCollection("Ranked", songs), recommend);

            Assert.That(_covers.SelectedSong, Is.SameAs(songs[1]));
            _collections.SlideDifficulty((int)ChartLevel.Master);
            _covers.SlideListToHead();
            Assert.That(_covers.SelectedSong, Is.SameAs(songs[2]));

            _collections.NextCollection();
            Assert.That(_covers.SelectedSong, Is.SameAs(songs[0]));
        }

        /// <summary>Checks that ranking order uses the classic achievement in classic judge mode.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SortByRank_ClassicModeUsesClassicAchievement()
        {
            var songs = CreateSongs("collection-classic", 2);
            ScoreManager.GetScore(songs[0], ChartLevel.Easy).Acc = new Accurate { DX = 100, Classic = 90 };
            ScoreManager.GetScore(songs[1], ChartLevel.Easy).Acc = new Accurate { DX = 90, Classic = 100 };
            MajEnv.RuntimeConfig!.List.OrderBy.SortBy = SortType.ByRank;
            MajEnv.Settings.Judge.Mode = JudgeModeOption.Classic;

            yield return Ready(CreateCollection("Classic", songs));

            Assert.That(_covers.SelectedSong, Is.SameAs(songs[1]));
        }

        /// <summary>Checks that only collections near the cursor are bound to displayers and positioned by distance.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Displayers_BindNearbyCollections()
        {
            var collections = new SongCollection[9];
            for (var i = 0; i < collections.Length; i++)
            {
                collections[i] = CreateCollection($"C{i}", CreateSongs($"collection-near{i}", 1));
            }

            yield return Ready(collections);

            var indices = PrivateMember.GetField<int[]>(_collections, "_collectionDisplayerIndices");
            Assert.That(indices, Is.EquivalentTo(new[] { 0, 1, 2, 3, -1, -1, -1 }));
            var displayers = PrivateMember.GetField<CollectionDisplayer[]>(_collections, "_collectionDisplayers");
            var third = displayers[Array.IndexOf(indices, 3)];
            Assert.That(third.RectTransform.anchoredPosition.x, Is.EqualTo(540f).Within(1e-3f));
        }

        /// <summary>Checks the collection tab layout curve.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LayoutCurve_InterpolatesBetweenSteps()
        {
            yield return Ready(CreateCollection("A", CreateSongs("collection-curve", 1)));

            Assert.That(PrivateMember.Invoke<Vector2>(_collections, "GetDisplayerPositionFromDelta", 0.5f).x, Is.EqualTo(120f).Within(1e-3f));
            Assert.That(PrivateMember.Invoke<Vector2>(_collections, "GetDisplayerPositionFromDelta", -1.5f).x, Is.EqualTo(-315f).Within(1e-3f));
            Assert.That(PrivateMember.Invoke<Vector2>(_collections, "GetDisplayerPositionFromDelta", 2.5f).x, Is.EqualTo(465f).Within(1e-3f));
            Assert.That(PrivateMember.Invoke<Vector2>(_collections, "GetDisplayerPositionFromDelta", 3.5f).x, Is.EqualTo(615f).Within(1e-3f));
            Assert.That(PrivateMember.Invoke<Vector2>(_collections, "GetDisplayerPositionFromDelta", 9f).x, Is.EqualTo(690f).Within(1e-3f));
            Assert.That(PrivateMember.InvokeStatic<int>(typeof(CollectionListManager), "WrapIndex", -8, 7), Is.EqualTo(6));
        }
    }
}
