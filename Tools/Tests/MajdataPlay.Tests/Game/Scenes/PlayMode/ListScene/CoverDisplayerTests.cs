using System.Collections;
using MajdataPlay.Scenes.List;
using MajdataPlay.Scenes.List.Models;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.ListScene
{
    /// <summary>Covers the song cover and thumbnail displayers of the song list.</summary>
    public class CoverDisplayerTests : ListSceneTestBase
    {
        /// <summary>Gets the image showing a displayer's cover.</summary>
        /// <param name="displayer">The displayer.</param>
        /// <returns>The image.</returns>
        private static Image CoverOf(Component displayer)
        {
            return PrivateMember.GetField<Image>(displayer, "_coverDisplayer");
        }

        /// <summary>Creates and activates a song cover displayer.</summary>
        /// <returns>The displayer.</returns>
        private SongCoverDisplayer CreateActiveCover()
        {
            var displayer = CreateSongCoverDisplayer(Canvas);
            Activate(displayer);
            return displayer;
        }

        /// <summary>Checks that Awake starts in the small style with visible levels.</summary>
        [Test]
        public void SongCover_Awake_StartsSmall()
        {
            var displayer = CreateActiveCover();
            var levels = PrivateMember.GetField<GameObject>(displayer, "_levelDisplayerListRoot");

            Assert.That(levels.activeSelf, Is.True);
            Assert.That(PrivateMember.GetField<float>(displayer, "_selectedProgress"), Is.EqualTo(0f));
            Assert.That(displayer.RectTransform, Is.SameAs(displayer.transform));
        }

        /// <summary>Checks that a level displayer without a Text child is rejected.</summary>
        [Test]
        public void SongCover_Awake_RequiresLevelText()
        {
            var displayer = CreateSongCoverDisplayer(Canvas);
            var level = PrivateMember.GetField<GameObject>(displayer, "_levelDisplayerListRoot").transform.GetChild(3);
            Object.DestroyImmediate(level.Find("Text").gameObject);
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("Level displayer 3 does not have a child named 'Text'"));

            Activate(displayer);
        }

        /// <summary>Checks that the selected style enlarges background and cover and hides the small levels.</summary>
        [Test]
        public void SongCover_SelectedProgress_ScalesTowardsCenterStyle()
        {
            var displayer = CreateActiveCover();
            var background = (RectTransform)displayer.transform.Find("BG");
            var cover = CoverOf(displayer).rectTransform;
            var levels = PrivateMember.GetField<GameObject>(displayer, "_levelDisplayerListRoot");

            displayer.SetSelectedProgress(2f);

            Assert.That(background.anchoredPosition, Is.EqualTo(new Vector2(0, -4f)));
            Assert.That(background.sizeDelta.x, Is.EqualTo(356.32f).Within(1e-3f));
            Assert.That(cover.sizeDelta.x, Is.EqualTo(285.22f).Within(1e-3f));
            Assert.That(levels.activeSelf, Is.False);

            displayer.SetSelectedProgress(0f);
            Assert.That(background.anchoredPosition, Is.EqualTo(new Vector2(0, 20)));
            Assert.That(levels.activeSelf, Is.True);
        }

        /// <summary>Checks that deactivating resets the selected style.</summary>
        [Test]
        public void SongCover_SetActiveFalse_ResetsStyle()
        {
            var displayer = CreateActiveCover();
            displayer.SetSelectedProgress(1f);

            displayer.SetActive(false);

            Assert.That(displayer.gameObject.activeSelf, Is.False);
            Assert.That(PrivateMember.GetField<float>(displayer, "_selectedProgress"), Is.EqualTo(0f));
            displayer.SetActive(true);
            Assert.That(displayer.gameObject.activeSelf, Is.True);
        }

        /// <summary>Checks that level labels are shown right-aligned and empty levels are hidden.</summary>
        [Test]
        public void SongCover_SetSongDetail_ShowsLevels()
        {
            var displayer = CreateActiveCover();
            var levels = PrivateMember.GetField<GameObject>(displayer, "_levelDisplayerListRoot").transform;
            var song = new FakeSongDetail("majtest-cover-levels", "Song", "1", string.Empty, "7", "10", "13", "14+", string.Empty)
            {
                Cover = CreateSprite("Cover")
            };

            displayer.SetSongDetail(song);

            Assert.That(levels.GetChild(6).gameObject.activeSelf, Is.False);
            Assert.That(levels.GetChild(1).gameObject.activeSelf, Is.False);
            Assert.That(levels.GetChild(5).Find("Text").GetComponent<TextMeshProUGUI>().text, Is.EqualTo("14+"));
            Assert.That(levels.GetChild(5).localPosition, Is.EqualTo(new Vector3(60, 0, 0)));
            Assert.That(levels.GetChild(0).localPosition, Is.EqualTo(new Vector3(20, 0, 0)));
        }

        /// <summary>Checks that the cover is loaded asynchronously and exposed once it is a real sprite.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SongCover_SetSongDetail_LoadsCover()
        {
            var displayer = CreateActiveCover();
            var cover = CreateSprite("Cover");
            var song = new FakeSongDetail("majtest-cover-load", "Song", "1") { Cover = cover, IsCompressedCoverLoaded = false };
            var loading = PrivateMember.GetField<GameObject>(displayer, "_loadingComponent");

            displayer.SetSongDetail(song, 50);
            Assert.That(displayer.CurrentCoverSprite, Is.Null);
            Assert.That(loading.activeSelf, Is.True);
            yield return WaitUntil(() => CoverOf(displayer).sprite == cover);

            Assert.That(displayer.CurrentCoverSprite, Is.SameAs(cover));
            Assert.That(loading.activeSelf, Is.False);
            Assert.That(song.CoverRequestCount, Is.EqualTo(1));
        }

        /// <summary>Checks that a newer song cancels the pending cover of an older one.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SongCover_NewSong_CancelsPreviousLoad()
        {
            var displayer = CreateActiveCover();
            var first = new FakeSongDetail("majtest-cover-first", "First", "1") { Cover = CreateSprite("First"), IsCompressedCoverLoaded = false };
            var second = new FakeSongDetail("majtest-cover-second", "Second", "1") { Cover = CreateSprite("Second") };

            displayer.SetSongDetail(first, 10000);
            displayer.SetSongDetail(second, 0);
            yield return WaitUntil(() => CoverOf(displayer).sprite == second.Cover);
            yield return null;

            Assert.That(CoverOf(displayer).sprite, Is.SameAs(second.Cover));
            Assert.That(first.CoverRequestCount, Is.EqualTo(0));
        }

        /// <summary>Checks that a thumbnail loads its cover and can be toggled.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Thumbnail_LoadsCoverAndToggles()
        {
            var displayer = CreateThumbnailDisplayer(Canvas);
            Activate(displayer);
            var cover = CreateSprite("Thumb");
            var song = new FakeSongDetail("majtest-thumb", "Thumb", "1") { Cover = cover, IsCompressedCoverLoaded = false };

            displayer.SetSongDetail(song, 0);
            yield return WaitUntil(() => CoverOf(displayer).sprite == cover);

            Assert.That(CoverOf(displayer).sprite, Is.SameAs(cover));
            Assert.That(PrivateMember.GetField<GameObject>(displayer, "_loadingComponent").activeSelf, Is.False);
            Assert.That(ListManager.AllBackgroundTasks, Is.Not.Empty);

            displayer.SetActive(false);
            Assert.That(displayer.gameObject.activeSelf, Is.False);
        }

        /// <summary>Checks that a newer thumbnail request cancels the older one.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Thumbnail_NewSong_CancelsPreviousLoad()
        {
            var displayer = CreateThumbnailDisplayer(Canvas);
            Activate(displayer);
            var first = new FakeSongDetail("majtest-thumb-first", "First", "1") { Cover = CreateSprite("First"), IsCompressedCoverLoaded = false };
            var second = new FakeSongDetail("majtest-thumb-second", "Second", "1") { Cover = CreateSprite("Second") };

            displayer.SetSongDetail(first, 10000);
            displayer.SetSongDetail(second, 0);
            yield return WaitUntil(() => CoverOf(displayer).sprite == second.Cover);
            yield return null;

            Assert.That(CoverOf(displayer).sprite, Is.SameAs(second.Cover));
            Assert.That(first.CoverRequestCount, Is.EqualTo(0));
        }
    }
}
