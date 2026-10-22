using System;
using System.Collections;
using System.Collections.Generic;
using LitMotion;
using MajdataPlay.Collections;
using MajdataPlay.i18n;
using MajdataPlay.Scenes.List;
using MajdataPlay.Scenes.List.Models;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.ListScene
{
    /// <summary>Covers the scrolling song cover list: binding of pooled displayers, selection, navigation and layout.</summary>
    public class CoverListManagerTests : ListSceneTestBase
    {
        /// <summary>The cover list under test.</summary>
        private CoverListManager _covers = null!;

        /// <summary>The collection list the cover list delegates boundary navigation to.</summary>
        private CollectionListManager _collections = null!;

        /// <summary>The center cover.</summary>
        private CenterCoverDisplayer _center = null!;

        /// <summary>Creates the managers and runs their Awake; Start runs on the first frame of each test.</summary>
        [SetUp]
        public void SetUp()
        {
            (_covers, _collections, _center) = CreateListManagers(covers: 7, thumbnails: 13, collectionDisplayers: 3);
            PublishCollections(CreateCollection("Only", CreateSongs("cover-only", 2)));
            ActivateManagers(_covers, _collections);
        }

        /// <summary>Lets the managers start and initializes the collections.</summary>
        /// <returns>The coroutine.</returns>
        private IEnumerator Ready()
        {
            yield return null;
            _collections.Init();
        }

        /// <summary>Gets the progress text.</summary>
        private string Progress => PrivateMember.GetField<TextMeshProUGUI>(_covers, "_progressDisplayer").text;

        /// <summary>Gets the number of active cover displayers.</summary>
        /// <returns>The count.</returns>
        private int ActiveCovers()
        {
            var count = 0;
            foreach (var displayer in PrivateMember.GetField<SongCoverDisplayer[]>(_covers, "_songCoverDisplayers"))
            {
                if (displayer.gameObject.activeSelf)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>Checks that setting a collection binds displayers around the head of the list.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SetCollection_BindsVisibleDisplayers()
        {
            yield return Ready();
            var songs = CreateSongs("cover-bind", 10);

            _covers.SetCollection(CreateCollection("Ten", songs), false);

            Assert.That(ActiveCovers(), Is.EqualTo(4));
            Assert.That(PrivateMember.GetField<int>(_covers, "_lastVisibleThumbnailIndex"), Is.EqualTo(6));
            Assert.That(PrivateMember.GetField<GameObject>(_covers, "_emptyCollectionNotice").activeSelf, Is.False);
        }

        /// <summary>Checks that sliding without animation selects the song, updates progress, configuration and center cover.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SlideList_WithoutAnimation_SelectsSong()
        {
            yield return Ready();
            var songs = CreateSongs("cover-slide", 10);
            _covers.SetCollection(CreateCollection("Ten", songs), false);

            _covers.SlideList(5, disableAnimation: true);

            Assert.That(_covers.SelectedSong, Is.SameAs(songs[5]));
            Assert.That(Progress, Is.EqualTo("6/<size=70%>10"));
            Assert.That(MajEnv.RuntimeConfig!.List.SelectedSongIndex, Is.EqualTo(5));
            Assert.That(MajEnv.RuntimeConfig.List.SelectedSongHash, Is.EqualTo(songs[5].Hash));
            Assert.That(ActiveCovers(), Is.EqualTo(7));
            var metadata = PrivateMember.GetField<ChartMetadataDisplayer>(_center, "_metadataDisplayer");
            Assert.That(PrivateMember.GetField<TextMeshProUGUI>(metadata, "_title").text, Is.EqualTo(songs[5].Title));
        }

        /// <summary>Checks that an animated slide settles on the target and then updates the center cover.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SlideList_Animated_SettlesOnTarget()
        {
            yield return Ready();
            var songs = CreateSongs("cover-anim", 10);
            _covers.SetCollection(CreateCollection("Ten", songs), false);
            _covers.SlideList(1, disableAnimation: true);

            _covers.SlideList(2);
            yield return null;
            PrivateMember.GetField<MotionHandle>(_covers, "_scrollMotion").TryComplete();

            Assert.That(_covers.SelectedSong, Is.SameAs(songs[3]));
            Assert.That(PrivateMember.GetField<float>(_covers, "_listCursorPos"), Is.EqualTo(3f));
            var metadata = PrivateMember.GetField<ChartMetadataDisplayer>(_center, "_metadataDisplayer");
            Assert.That(PrivateMember.GetField<TextMeshProUGUI>(metadata, "_title").text, Is.EqualTo(songs[3].Title));
        }

        /// <summary>Checks that the head, tail, cursor and random selection move within the collection.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Navigation_HeadTailCursorAndRandom()
        {
            yield return Ready();
            var songs = CreateSongs("cover-nav", 6);
            _covers.SetCollection(CreateCollection("Six", songs), false);

            _covers.SlideListToTail();
            Assert.That(_covers.SelectedSong, Is.SameAs(songs[5]));
            _covers.SlideListToHead();
            Assert.That(_covers.SelectedSong, Is.SameAs(songs[0]));
            _covers.SetCursor(songs[4].Hash, true);
            Assert.That(_covers.SelectedSong, Is.SameAs(songs[4]));
            _covers.SetCursor(songs[2], true);
            Assert.That(_covers.SelectedSong, Is.SameAs(songs[2]));
            _covers.RandomSelect();
            Assert.That(Array.IndexOf(songs, _covers.SelectedSong), Is.GreaterThanOrEqualTo(0));
        }

        /// <summary>Checks that sliding past either end moves to the neighbouring collection.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SlideList_PastEnds_ChangesCollection()
        {
            yield return Ready();
            var first = CreateCollection("First", CreateSongs("cover-first", 2));
            var second = CreateCollection("Second", CreateSongs("cover-second", 3));
            PublishCollections(first, second);
            _collections.Init();
            Assert.That(_collections.SelectedCollection.Name, Is.EqualTo("First"));

            _covers.SlideListToTail();
            _covers.SlideList(1, disableAnimation: true);
            Assert.That(_collections.SelectedCollection.Name, Is.EqualTo("Second"));

            _covers.SlideListToHead();
            _covers.SlideList(-1, disableAnimation: true);
            Assert.That(_collections.SelectedCollection.Name, Is.EqualTo("First"));
        }

        /// <summary>Checks that an empty collection shows the notice and delegates sliding to the collection list.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator EmptyCollection_ShowsNoticeAndDelegatesSliding()
        {
            yield return Ready();
            var empty = CreateCollection("Empty");
            var other = CreateCollection("Other", CreateSongs("cover-other", 2));
            PublishCollections(empty, other);
            _collections.Init();

            Assert.That(_covers.SelectedSong, Is.Null);
            Assert.That(Progress, Is.EqualTo("Ciallo～(∠・ω< )⌒★"));
            Assert.That(PrivateMember.GetField<GameObject>(_covers, "_emptyCollectionNotice").activeSelf, Is.True);
            Assert.That(_center.gameObject.activeSelf, Is.False);
            _covers.RandomSelect();
            _covers.SlideListToHead();
            _covers.SlideListToTail();
            _covers.SetCursor("missing");
            Assert.That(_covers.SelectedSong, Is.Null);

            _covers.SlideList(1);
            Assert.That(_collections.SelectedCollection.Name, Is.EqualTo("Other"));
            _covers.SlideList(-1);
            Assert.That(_collections.SelectedCollection.Name, Is.EqualTo("Empty"));
            _covers.SlideList(0);
            Assert.That(_collections.SelectedCollection.Name, Is.EqualTo("Empty"));
        }

        /// <summary>Checks that keeping the cursor reselects the same song in a new collection and repeated collections are ignored.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SetCollection_KeepCursorReselectsSong()
        {
            yield return Ready();
            var songs = CreateSongs("cover-keep", 5);
            var first = CreateCollection("First", songs);
            _covers.SetCollection(first, false);
            _covers.SlideList(3, disableAnimation: true);

            _covers.SetCollection(first, true);
            Assert.That(_covers.SelectedSong, Is.SameAs(songs[3]));

            var reordered = CreateCollection("Reordered", songs[4], songs[3], songs[0]);
            _covers.SetCollection(reordered, true);
            Assert.That(_covers.SelectedSong, Is.SameAs(songs[3]));
            Assert.That(Progress, Is.EqualTo("2/<size=70%>3"));
            Assert.That(() => _covers.SetCollection(null!, false), Throws.ArgumentNullException);
        }

        /// <summary>Checks that a pool smaller than the visible window leaves bindings pending.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SmallPool_LeavesPendingBindings()
        {
            yield return Ready();
            var (covers, collections, _) = CreateListManagers(covers: 2, thumbnails: 2, collectionDisplayers: 1);
            ActivateManagers(covers, collections);
            yield return null;

            covers.SetCollection(CreateCollection("Many", CreateSongs("cover-pool", 8)), false);

            Assert.That(PrivateMember.GetField<bool>(covers, "_hasPendingBindings"), Is.True);
        }

        /// <summary>Checks the cover and thumbnail layout curves.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LayoutCurves_FollowSteps()
        {
            yield return Ready();
            Assert.That(PrivateMember.Invoke<Vector2>(_covers, "GetCoverDisplayerPositionFromDelta", 0f), Is.EqualTo(Vector2.zero));
            Assert.That(PrivateMember.Invoke<Vector2>(_covers, "GetCoverDisplayerPositionFromDelta", 0.5f).x, Is.EqualTo(132f).Within(1e-3f));
            Assert.That(PrivateMember.Invoke<Vector2>(_covers, "GetCoverDisplayerPositionFromDelta", -2.5f).x, Is.EqualTo(-517.5f).Within(1e-3f));
            Assert.That(PrivateMember.Invoke<Vector2>(_covers, "GetThumbnailDisplayerPositionFromDelta", 0f), Is.EqualTo(Vector2.zero));
            Assert.That(PrivateMember.Invoke<Vector2>(_covers, "GetThumbnailDisplayerPositionFromDelta", 0.5f).x, Is.EqualTo(101.5f).Within(1e-3f));
            Assert.That(PrivateMember.Invoke<Vector2>(_covers, "GetThumbnailDisplayerPositionFromDelta", 3f).x, Is.EqualTo(363f).Within(1e-3f));
            Assert.That(PrivateMember.Invoke<Vector3>(_covers, "GetCoverDisplayerScaleFromDelta", 0f).x, Is.EqualTo(1f));
            Assert.That(PrivateMember.Invoke<Vector3>(_covers, "GetCoverDisplayerScaleFromDelta", -4f).x, Is.EqualTo(0.86f).Within(1e-4f));
            Assert.That(PrivateMember.Invoke<float>(_covers, "GetCoverDisplayerSelectedProgressFromDelta", 0f), Is.EqualTo(1f));
            Assert.That(PrivateMember.Invoke<float>(_covers, "GetCoverDisplayerSelectedProgressFromDelta", 2f), Is.EqualTo(0f));
        }

        /// <summary>Checks that the empty notice follows language changes and pausing the app does not fail.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LanguageAndPause_AreHandled()
        {
            yield return Ready();
            Localization.Current = new Language
            {
                Translations = new Dictionary<string, string> { { "MAJTEXT_LIST_EMPTY_COLLECTION", "Nothing here" } }
            };
            PrivateMember.Invoke(_covers, "OnApplicationPause", true);
            PrivateMember.Invoke(_covers, "OnApplicationPause", false);

            var notice = PrivateMember.GetField<GameObject>(_covers, "_emptyCollectionNotice");
            Assert.That(notice.GetComponentInChildren<TextMeshProUGUI>(true).text, Is.EqualTo("Nothing here"));
        }

        /// <summary>Checks that the singleton is registered while alive and released on destroy.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Destroy_ReleasesSingleton()
        {
            yield return Ready();
            Assert.That(Majdata<CoverListManager>.Instance, Is.SameAs(_covers));

            UnityEngine.Object.DestroyImmediate(_covers.gameObject);

            Assert.That(Majdata<CoverListManager>.IsNull, Is.True);
        }
    }
}
