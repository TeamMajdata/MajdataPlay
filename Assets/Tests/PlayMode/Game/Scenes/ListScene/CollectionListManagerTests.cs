using System;
using System.Collections;
using LitMotion;
using MajdataPlay.Collections;
using MajdataPlay.Scenes.List;
using MajdataPlay.Scenes.List.Models;
using MajdataPlay.Scenes.Setting;
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
        private string Name => PrivateMember.GetField<TextMeshProUGUI>(SelectedTab, "_nameDisplayer").text;

        /// <summary>Gets the collection icon.</summary>
        private Image Icon => PrivateMember.GetField<Image>(SelectedTab, "_iconDisplayer");

        /// <summary>Gets the actual pooled tab bound to the current selected collection.</summary>
        private CollectionDisplayer SelectedTab
        {
            get
            {
                var indices = PrivateMember.GetField<int[]>(_collections, "_collectionDisplayerIndices");
                var selectedIndex = PrivateMember.GetField<int>(_collections, "_listDesiredPos");
                var slot = Array.IndexOf(indices, selectedIndex);
                Assert.That(slot, Is.GreaterThanOrEqualTo(0), "The selected real tab must be bound before its presentation is inspected.");
                return PrivateMember.GetField<CollectionDisplayer[]>(_collections, "_collectionDisplayers")[slot];
            }
        }

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
            Assert.That(MajEnv.RuntimeConfig!.List.SelectedDirGuid, Is.EqualTo(Guid.Empty));
            Assert.That(MajEnv.RuntimeConfig.List.SelectedSongId, Is.Empty);
        }

        /// <summary>Checks that the remembered collection, song and difficulty are restored.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_RestoresRememberedContext()
        {
            var songs = CreateSongs("collection-restore", 4);
            var second = CreateCollection("Second", songs);
            MajEnv.RuntimeConfig!.List.SelectedDirGuid = second.Id;
            MajEnv.RuntimeConfig.List.SelectedSongId = songs[2].Id;
            MajEnv.RuntimeConfig.List.SelectedDiff = ChartLevel.Master;

            yield return Ready(CreateCollection("First", CreateSongs("collection-restore1", 1)), second);

            Assert.That(_collections.SelectedCollection.Name, Is.EqualTo("Second"));
            Assert.That(_covers.SelectedSong, Is.SameAs(songs[2]));
            Assert.That(MajEnv.RuntimeConfig.List.SelectedDirGuid, Is.EqualTo(second.Id));
            Assert.That(MajEnv.RuntimeConfig.List.SelectedSongId, Is.EqualTo(songs[2].Id));
            Assert.That(PrivateMember.GetField<TextMeshProUGUI>(BoundCover(_covers, 2), "_selectedLevelTitle").text, Is.EqualTo("Master"));
        }

        /// <summary>Checks that a missing collection survives initialization until an explicit selection replaces it.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_MissingCollectionPreservesSavedIdentities()
        {
            var listConfig = MajEnv.RuntimeConfig!.List;
            var savedId = Guid.NewGuid();
            var secondSongs = CreateSongs("collection-missing-next", 2);
            var second = CreateCollection("Second", secondSongs);
            listConfig.SelectedDirGuid = savedId;
            listConfig.SelectedSongId = "missing-song";

            yield return Ready(CreateCollection("First", CreateSongs("collection-missing", 2)), second);

            Assert.That(_collections.SelectedCollection.Name, Is.EqualTo("First"));
            Assert.That(listConfig.SelectedDirGuid, Is.EqualTo(savedId));
            Assert.That(listConfig.SelectedSongId, Is.EqualTo("missing-song"));

            _collections.NextCollection();

            Assert.That(listConfig.SelectedDirGuid, Is.EqualTo(second.Id));
            Assert.That(listConfig.SelectedSongId, Is.EqualTo(secondSongs[0].Id));
        }

        /// <summary>Checks that a missing song keeps its saved identity after the collection and difficulty are restored.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_MissingSongPreservesSavedIdentities()
        {
            var listConfig = MajEnv.RuntimeConfig!.List;
            var songs = CreateSongs("collection-missing-song", 3);
            var selected = CreateCollection("Selected", songs);
            listConfig.SelectedDirGuid = selected.Id;
            listConfig.SelectedSongId = "missing-song";
            listConfig.SelectedDiff = ChartLevel.Master;

            yield return Ready(CreateCollection("First", CreateSongs("collection-missing-song-first", 1)), selected);

            Assert.That(_collections.SelectedCollection.Name, Is.EqualTo("Selected"));
            Assert.That(_covers.SelectedSong, Is.SameAs(songs[0]));
            Assert.That(listConfig.SelectedDirGuid, Is.EqualTo(selected.Id));
            Assert.That(listConfig.SelectedSongId, Is.EqualTo("missing-song"));
        }

        /// <summary>Checks that restoring an empty collection leaves the saved song available for a later refresh.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_EmptyCollectionPreservesSavedIdentities()
        {
            var listConfig = MajEnv.RuntimeConfig!.List;
            var selected = CreateCollection("Empty", Array.Empty<FakeSongDetail>());
            listConfig.SelectedDirGuid = selected.Id;
            listConfig.SelectedSongId = "missing-song";

            yield return Ready(CreateCollection("First", CreateSongs("collection-empty-first", 1)), selected);

            Assert.That(_collections.SelectedCollection.Name, Is.EqualTo("Empty"));
            Assert.That(_covers.SelectedSong, Is.Null);
            Assert.That(listConfig.SelectedDirGuid, Is.EqualTo(selected.Id));
            Assert.That(listConfig.SelectedSongId, Is.EqualTo("missing-song"));
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

        /// <summary>Checks that each input moves one available difficulty, skipping missing charts and remembering the result.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SlideDifficulty_WrapsAndIsRemembered()
        {
            yield return Ready(CreateCollection("A", CreateSongs("collection-diff", 2)));

            _collections.SlideDifficulty(-1);
            Assert.That(MajEnv.RuntimeConfig!.List.SelectedDiff, Is.EqualTo(ChartLevel.ReMaster));
            Assert.That(PrivateMember.GetField<TextMeshProUGUI>(BoundCover(_covers, 0), "_selectedLevelTitle").text, Is.EqualTo("Re:Master"));

            _collections.SlideDifficulty(2);
            Assert.That(MajEnv.RuntimeConfig.List.SelectedDiff, Is.EqualTo(ChartLevel.Easy));
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
                PrivateMember.GetField<MotionHandle>(_collections, "_scrollMotion").TryComplete();
                Assert.That(Icon.enabled, Is.True, _collections.SelectedCollection.Name);
                Assert.That(Icon.sprite.name, Is.EqualTo(sprite), _collections.SelectedCollection.Name);
            }
        }

        /// <summary>Checks display snapshots preserve folder identity without giving the aggregate list a folder icon.</summary>
        /// <returns>The initialized list and category navigation coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_PreservesLocalDirectoryIconsWithoutAddingOneToAll()
        {
            using var temporary = new TemporaryDirectory();
            var directory = temporary.Directory.CreateDirectory("Folder");
            var songs = CreateSongs("collection-folder-icon", 1);
            var folder = new SongCollection(directory, "Folder", songs);
            yield return Ready(folder, new SongCollection("All", songs));

            Assert.That(_collections.SelectedCollection.IsVirtual, Is.False);
            Assert.That(Icon.enabled, Is.True);
            Assert.That(Icon.sprite.name, Is.EqualTo("Folder"));
            _collections.NextCollection();
            PrivateMember.GetField<MotionHandle>(_collections, "_scrollMotion").TryComplete();
            Assert.That(_collections.SelectedCollection.IsVirtual, Is.True);
            Assert.That(Icon.enabled, Is.False);
            _collections.PreviousCollection();
            PrivateMember.GetField<MotionHandle>(_collections, "_scrollMotion").TryComplete();
            Assert.That(Icon.enabled, Is.True);
            Assert.That(Icon.sprite.name, Is.EqualTo("Folder"));
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
            for (var step = 0; step < (int)ChartLevel.Master; step++)
            {
                _collections.SlideDifficulty(1);
            }
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
            MajEnv.UserSettings.Judge.Mode = JudgeModeOption.Classic;

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
            Assert.That(third.RectTransform.anchoredPosition.x, Is.EqualTo(546f).Within(1e-3f));
        }

        /// <summary>Checks selection belongs to the real moving tab rather than a duplicate fixed overlay or shader occlusion strip.</summary>
        /// <returns>The initialized list and native selection regression coroutine.</returns>
        [UnityTest]
        public IEnumerator Displayers_HighlightTheRealSelectedTabWithoutGlobalOcclusion()
        {
            yield return Ready(
                CreateCollection("A", CreateSongs("collection-real-tab-a", 1)),
                CreateCollection("B", CreateSongs("collection-real-tab-b", 1)),
                CreateCollection("C", CreateSongs("collection-real-tab-c", 1)));
            var selected = SelectedTab;
            var indices = PrivateMember.GetField<int[]>(_collections, "_collectionDisplayerIndices");
            var displayers = PrivateMember.GetField<CollectionDisplayer[]>(_collections, "_collectionDisplayers");
            var adjacent = displayers[Array.IndexOf(indices, 1)];
            AssertTab(selected, 0f, 1f);
            AssertTab(adjacent, 218f, 0f);
            Assert.That(_collections.transform.Find("CollectionName"), Is.Null);
            Assert.That(_collections.transform.Find("CollectionIcon"), Is.Null);
            var saved = Shader.GetGlobalVector("_MajCollectionClipRect");
            AddCleanup(() => Shader.SetGlobalVector("_MajCollectionClipRect", saved));
            var sentinel = new Vector4(0.13f, 0.87f, 0.07f, 0.42f);
            Shader.SetGlobalVector("_MajCollectionClipRect", sentinel);
            _collections.NextCollection();
            PrivateMember.GetField<MotionHandle>(_collections, "_scrollMotion").TryComplete();
            _collections.enabled = false;
            _collections.enabled = true;
            Assert.That(Shader.GetGlobalVector("_MajCollectionClipRect"), Is.EqualTo(sentinel),
                "Selection and lifecycle must not publish or clear a global pool-occlusion mask.");
        }

        /// <summary>Checks halfway motion highlights both real neighbors smoothly and reversing restores the same pooled objects.</summary>
        /// <returns>The initialized list, native highlighting and reversal regression coroutine.</returns>
        [UnityTest]
        public IEnumerator Displayers_InterpolateRealHighlightsAndRestoreThemAfterReversal()
        {
            yield return Ready(
                CreateCollection("A", CreateSongs("collection-tab-reversal-a", 1)),
                CreateCollection("B", CreateSongs("collection-tab-reversal-b", 1)),
                CreateCollection("C", CreateSongs("collection-tab-reversal-c", 1)));
            var indices = PrivateMember.GetField<int[]>(_collections, "_collectionDisplayerIndices");
            var displayers = PrivateMember.GetField<CollectionDisplayer[]>(_collections, "_collectionDisplayers");
            var first = displayers[Array.IndexOf(indices, 0)];
            var second = displayers[Array.IndexOf(indices, 1)];
            _collections.NextCollection();
            PrivateMember.SetField(_collections, "_listCursorPos", 0.5f);
            PrivateMember.Invoke(_collections, "UpdateDisplayerPosition");
            AssertTab(first, -109f, 0.5f);
            AssertTab(second, 109f, 0.5f);
            PrivateMember.GetField<MotionHandle>(_collections, "_scrollMotion").TryComplete();
            Assert.That(SelectedTab, Is.SameAs(second));
            AssertTab(first, -218f, 0f);
            AssertTab(second, 0f, 1f);
            _collections.PreviousCollection();
            PrivateMember.GetField<MotionHandle>(_collections, "_scrollMotion").TryComplete();
            Assert.That(SelectedTab, Is.SameAs(first));
            AssertTab(first, 0f, 1f);
            AssertTab(second, 218f, 0f);
        }

        /// <summary>Checks reversals and reused pool objects keep native positions, colors and authored opacity rather than fading whole tabs.</summary>
        /// <returns>The initialized list, reversal and reused-object regression coroutine.</returns>
        [UnityTest]
        public IEnumerator Displayers_ScrollAndReuseWithoutChangingWholeItemOpacity()
        {
            var collections = new SongCollection[9];
            for (var index = 0; index < collections.Length; index++)
            {
                collections[index] = CreateCollection("C" + index, CreateSongs("collection-tabs-reuse" + index, 1));
            }
            yield return Ready(collections);
            var indices = PrivateMember.GetField<int[]>(_collections, "_collectionDisplayerIndices");
            var displayers = PrivateMember.GetField<CollectionDisplayer[]>(_collections, "_collectionDisplayers");
            foreach (var displayer in displayers)
            {
                Assert.That(displayer.GetComponents<CanvasGroup>(), Is.Empty);
            }
            var slot = Array.IndexOf(indices, 0);
            var reused = displayers[slot];
            var group = reused.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0.42f;
            group.interactable = false;
            var icon = PrivateMember.GetField<Image>(reused, "_iconDisplayer");
            var name = PrivateMember.GetField<TextMeshProUGUI>(reused, "_nameDisplayer");
            var iconColor = icon.color;
            var nameColor = name.color;
            PrivateMember.SetField(_collections, "_listCursorPos", 0.5f);
            PrivateMember.Invoke(_collections, "UpdateDisplayerPosition");
            Assert.That(reused.RectTransform.anchoredPosition.x, Is.EqualTo(-109f).Within(0.001f));
            Assert.That(group.alpha, Is.EqualTo(0.42f));
            _collections.NextCollection();
            _collections.PreviousCollection();
            PrivateMember.GetField<MotionHandle>(_collections, "_scrollMotion").TryComplete();
            Assert.That(reused.RectTransform.anchoredPosition.x, Is.Zero);
            Assert.That(group.alpha, Is.EqualTo(0.42f));
            Assert.That(reused.gameObject.activeSelf, Is.True);
            PrivateMember.SetField(_collections, "_listCursorPos", 8f);
            PrivateMember.Invoke(_collections, "UpdateDisplayerPosition");
            Assert.That(indices[slot], Is.EqualTo(5));
            Assert.That(displayers[slot], Is.SameAs(reused));
            Assert.That(reused.RectTransform.anchoredPosition.x, Is.EqualTo(-546f).Within(0.001f));
            Assert.That(reused.GetComponent<CanvasGroup>(), Is.SameAs(group));
            Assert.That(group.alpha, Is.EqualTo(0.42f));
            Assert.That(group.interactable, Is.False);
            Assert.That(icon.color, Is.EqualTo(iconColor));
            Assert.That(name.color, Is.EqualTo(nameColor));
            Assert.That(reused.gameObject.activeSelf, Is.True);
        }

        /// <summary>Checks navigation opacity shares the scroll clock while preserving the original OutQuad cursor curve.</summary>
        /// <returns>The initialized navigation and exact transition-time regression coroutine.</returns>
        [UnityTest]
        public IEnumerator NavigationArrows_FadeWithTheCollectionMotionWithoutChangingItsEasing()
        {
            yield return ReadyNavigationCollections("navigation-timeline");
            var arrows = BindNavigationArrows();
            _collections.NextCollection();
            var motion = PrivateMember.GetField<MotionHandle>(_collections, "_scrollMotion");
            Assert.That(motion.Duration, Is.EqualTo(0.25f));
            foreach (var sample in new (double Time, float Alpha, float Cursor)[]
            {
                (0, 1f, 0f),
                (0.0125, 0.84375f, 0.0975f),
                (0.025, 0.5f, 0.19f),
                (0.05, 0f, 0.36f),
                (0.125, 0f, 0.75f),
                (0.2, 0f, 0.96f),
                (0.225, 0.5f, 0.99f),
                (0.25, 1f, 1f)
            })
            {
                motion.Time = sample.Time;
                Assert.That(arrows.alpha, Is.EqualTo(sample.Alpha).Within(0.0001f), "Arrow opacity at " + sample.Time);
                Assert.That(PrivateMember.GetField<float>(_collections, "_listCursorPos"),
                    Is.EqualTo(sample.Cursor).Within(0.0001f), "Collection cursor at " + sample.Time);
            }
            Assert.That(_collections.SelectedCollection.Name, Is.EqualTo("B"));
            AssertTab(SelectedTab, 0f, 1f);
        }

        /// <summary>Checks a rapid reversal retains the current arrow opacity and cursor rather than flashing fully visible.</summary>
        /// <returns>The initialized navigation interruption regression coroutine.</returns>
        [UnityTest]
        public IEnumerator NavigationArrows_ReverseWithoutRestartingFromFullOpacity()
        {
            yield return ReadyNavigationCollections("navigation-reverse");
            var arrows = BindNavigationArrows();
            _collections.NextCollection();
            var interrupted = PrivateMember.GetField<MotionHandle>(_collections, "_scrollMotion");
            interrupted.Time = 0.025;
            Assert.That(arrows.alpha, Is.EqualTo(0.5f).Within(0.0001f));
            _collections.PreviousCollection();
            var reversed = PrivateMember.GetField<MotionHandle>(_collections, "_scrollMotion");
            Assert.That(reversed, Is.Not.EqualTo(interrupted));
            Assert.That(interrupted.IsActive(), Is.False);
            Assert.That(arrows.alpha, Is.EqualTo(0.5f).Within(0.0001f));
            reversed.Time = 0;
            Assert.That(arrows.alpha, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(PrivateMember.GetField<float>(_collections, "_listCursorPos"), Is.EqualTo(0.19f).Within(0.0001f));
            reversed.Time = 0.025;
            Assert.That(arrows.alpha, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(PrivateMember.GetField<float>(_collections, "_listCursorPos"), Is.EqualTo(0.1539f).Within(0.0001f));
            reversed.Time = 0.125;
            Assert.That(arrows.alpha, Is.Zero);
            reversed.Time = 0.225;
            Assert.That(arrows.alpha, Is.EqualTo(0.5f).Within(0.0001f));
            reversed.TryComplete();
            Assert.That(arrows.alpha, Is.EqualTo(1f));
            Assert.That(_collections.SelectedCollection.Name, Is.EqualTo("A"));
            AssertTab(SelectedTab, 0f, 1f);
        }

        /// <summary>Checks disabling cancels the pending fade and re-enabling settles the actual selected tab layout.</summary>
        /// <returns>The initialized navigation lifecycle regression coroutine.</returns>
        [UnityTest]
        public IEnumerator NavigationArrows_DisableCancelsFadeAndRestoresTheSelectedLayoutOnEnable()
        {
            yield return ReadyNavigationCollections("navigation-disable");
            var arrows = BindNavigationArrows();
            _collections.NextCollection();
            var interrupted = PrivateMember.GetField<MotionHandle>(_collections, "_scrollMotion");
            interrupted.Time = 0.025;
            _collections.gameObject.SetActive(false);
            Assert.That(interrupted.IsActive(), Is.False);
            Assert.That(arrows.alpha, Is.EqualTo(1f));
            Assert.That(PrivateMember.GetField<float>(_collections, "_listCursorPos"), Is.EqualTo(1f));
            Assert.That(interrupted.TryComplete(), Is.False);
            _collections.gameObject.SetActive(true);
            Assert.That(arrows.alpha, Is.EqualTo(1f));
            Assert.That(_collections.SelectedCollection.Name, Is.EqualTo("B"));
            AssertTab(SelectedTab, 0f, 1f);
        }

        /// <summary>Checks immediate moves, both wrap directions and idle repeated selection leave navigation visible without an active fade.</summary>
        /// <returns>The initialized navigation nonanimated-path regression coroutine.</returns>
        [UnityTest]
        public IEnumerator NavigationArrows_ImmediateWrapAndUnchangedSelectionStayVisible()
        {
            yield return ReadyNavigationCollections("navigation-immediate");
            var arrows = BindNavigationArrows();
            _collections.NextCollection();
            var interrupted = PrivateMember.GetField<MotionHandle>(_collections, "_scrollMotion");
            interrupted.Time = 0.125;
            Assert.That(arrows.alpha, Is.Zero);
            PrivateMember.Invoke(_collections, "SlideListTo", 2, true, false);
            Assert.That(interrupted.IsActive(), Is.False);
            Assert.That(arrows.alpha, Is.EqualTo(1f));
            Assert.That(PrivateMember.GetField<float>(_collections, "_listCursorPos"), Is.EqualTo(2f));
            _collections.NextCollection();
            Assert.That(arrows.alpha, Is.EqualTo(1f));
            Assert.That(PrivateMember.GetField<float>(_collections, "_listCursorPos"), Is.Zero);
            var idleMotion = PrivateMember.GetField<MotionHandle>(_collections, "_scrollMotion");
            Assert.That(idleMotion.IsActive(), Is.False);
            PrivateMember.Invoke(_collections, "SlideListTo", 0, false, false);
            Assert.That(PrivateMember.GetField<MotionHandle>(_collections, "_scrollMotion"), Is.EqualTo(idleMotion));
            Assert.That(arrows.alpha, Is.EqualTo(1f));
            _collections.PreviousCollection();
            Assert.That(arrows.alpha, Is.EqualTo(1f));
            Assert.That(PrivateMember.GetField<float>(_collections, "_listCursorPos"), Is.EqualTo(2f));
            Assert.That(PrivateMember.GetField<MotionHandle>(_collections, "_scrollMotion").IsActive(), Is.False);
        }

        /// <summary>Checks a refreshed empty snapshot clears the old cover and pooled categories safely.</summary>
        /// <returns>The storage-refresh regression coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_EmptyRefreshClearsSelectionAndAcceptsNavigation()
        {
            yield return Ready(CreateCollection("A", CreateSongs("refresh-empty", 1)));
            PublishCollections(Array.Empty<SongCollection>());
            _collections.Init();
            Assert.That(_covers.SelectedSong, Is.Null);
            Assert.That(_collections.SelectedCollection.IsEmpty, Is.True);
            foreach (var tab in PrivateMember.GetField<CollectionDisplayer[]>(_collections, "_collectionDisplayers"))
            {
                Assert.That(tab.gameObject.activeSelf, Is.False);
            }
            Assert.DoesNotThrow(() =>
            {
                _collections.NextCollection();
                _collections.PreviousCollection();
                _collections.SlideDifficulty(1);
            });
        }

        /// <summary>Checks a replacement at the same selected index rebinds tabs and cancels the previous transition.</summary>
        /// <returns>The same-index refresh regression coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_SameIndexRefreshRebindsTheSelectedTab()
        {
            yield return Ready(CreateCollection("A", CreateSongs("refresh-old", 1)), CreateCollection("Other", CreateSongs("refresh-other", 1)));
            _collections.NextCollection();
            var first = PrivateMember.GetField<MotionHandle>(_collections, "_scrollMotion");
            first.Time = 0.025;
            _collections.PreviousCollection();
            var pending = PrivateMember.GetField<MotionHandle>(_collections, "_scrollMotion");
            Assert.That(pending.IsActive(), Is.True);
            PublishCollections(CreateCollection("B", CreateSongs("refresh-new", 1)));
            _collections.Init();
            Assert.That(pending.IsActive(), Is.False);
            Assert.That(pending.TryComplete(), Is.False);
            Assert.That(PrivateMember.GetField<int>(_collections, "_listDesiredPos"), Is.Zero);
            Assert.That(Name, Is.EqualTo("B"));
            Assert.That(_collections.SelectedCollection.Name, Is.EqualTo("B"));
        }

        /// <summary>Initializes three real collections for the focused navigation transition cases.</summary>
        /// <param name="prefix">The unique in-memory song identity prefix for this test.</param>
        /// <returns>The manager initialization coroutine.</returns>
        private IEnumerator ReadyNavigationCollections(string prefix)
        {
            yield return Ready(
                CreateCollection("A", CreateSongs(prefix + "-a", 1)),
                CreateCollection("B", CreateSongs(prefix + "-b", 1)),
                CreateCollection("C", CreateSongs(prefix + "-c", 1)));
        }

        /// <summary>Binds a group scoped to the independent navigation arrows without changing shared test setup.</summary>
        /// <returns>The newly bound navigation-only CanvasGroup.</returns>
        private CanvasGroup BindNavigationArrows()
        {
            var arrows = CreateRect("Navigation", _collections.transform).AddComponent<CanvasGroup>();
            arrows.alpha = 1f;
            PrivateMember.SetField(_collections, "_navigationArrows", arrows);
            return arrows;
        }
        /// <summary>Checks the native single-background presentation of an actual data-bearing pooled category.</summary>
        /// <param name="displayer">The real pooled collection tab.</param>
        /// <param name="position">The expected signed horizontal location.</param>
        /// <param name="selection">The zero-to-one selected presentation amount.</param>
        private static void AssertTab(CollectionDisplayer displayer, float position, float selection)
        {
            var tab = PrivateMember.GetField<MenuTitleDisplayer>(displayer, "_tabDisplayer");
            var background = PrivateMember.GetField<Image>(tab, "_background");
            Assert.That(displayer.RectTransform.anchoredPosition.x, Is.EqualTo(position).Within(0.001f));
            var expectedSize = new Vector2(Mathf.Lerp(147.83f, 254.69f, selection), 76.8f);
            Assert.That(displayer.RectTransform.sizeDelta.x, Is.EqualTo(expectedSize.x).Within(0.001f));
            Assert.That(displayer.RectTransform.sizeDelta.y, Is.EqualTo(expectedSize.y).Within(0.001f));
            var expectedColor = Color.Lerp(new Color(0.9058824f, 0.9176471f, 0.9176471f, 1f), new Color(159f / 255f, 176f / 255f, 188f / 255f, 1f), selection);
            Assert.That(background.color, Is.EqualTo(expectedColor));
            Assert.That(displayer.gameObject.activeSelf, Is.True);
        }

        /// <summary>Checks the collection tab layout curve.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LayoutCurve_InterpolatesBetweenSteps()
        {
            yield return Ready(CreateCollection("A", CreateSongs("collection-curve", 1)));
            var tab = SelectedTab;
            foreach (var entry in new (float Distance, float Position)[] { (0.5f, 109f), (-1.5f, -300f), (2.5f, 464f), (3.5f, 628f), (9f, 1530f) })
            {
                tab.SetDistance(entry.Distance);
                Assert.That(tab.RectTransform.anchoredPosition.x, Is.EqualTo(entry.Position).Within(0.001f));
            }
            Assert.That(PrivateMember.InvokeStatic<int>(typeof(CollectionListManager), "WrapIndex", -8, 7), Is.EqualTo(6));
        }
    }
}
