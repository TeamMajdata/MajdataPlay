using System.Collections;
using MajdataPlay.Rendering;
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

        /// <summary>Checks the same pooled cover, frame and seven badges morph continuously into the selected presentation.</summary>
        [Test]
        public void SongCover_SelectedProgress_ScalesTowardsCenterStyle()
        {
            var displayer = CreateActiveCover();
            var background = (RectTransform)displayer.transform.Find("BG");
            var cover = CoverOf(displayer).rectTransform;
            var levels = PrivateMember.GetField<GameObject>(displayer, "_levelDisplayerListRoot");
            var ring = PrivateMember.GetField<Image>(displayer, "_levelRingDisplayer");
            var selected = PrivateMember.GetField<CanvasGroup>(displayer, "_selectedLevelGroup");
            displayer.SetDifficulty(3);
            displayer.SetSongDetail(new FakeSongDetail("majtest-morph", "Song", "1", "3", "7", "10", "13", "14", "15"));
            var originalBadge = levels.transform.GetChild(3);
            var originalPosition = ((RectTransform)originalBadge).anchoredPosition;
            var originalPivot = ((RectTransform)originalBadge).pivot;
            var sprite = CoverOf(displayer).sprite;

            displayer.SetSelectedProgress(2f);

            Assert.That(background.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(background.sizeDelta.x, Is.EqualTo(365f).Within(1e-3f));
            Assert.That(cover.sizeDelta.x, Is.EqualTo(285f).Within(1e-3f));
            Assert.That(ring.rectTransform.sizeDelta.x, Is.EqualTo(310f).Within(1e-3f));
            Assert.That(selected.alpha, Is.EqualTo(1f));
            Assert.That(levels.activeSelf, Is.True);
            Assert.That(levels.transform.childCount, Is.EqualTo(7));
            var finalPosition = ((RectTransform)originalBadge).anchoredPosition;
            var finalPivot = ((RectTransform)originalBadge).pivot;
            var finalRotation = originalBadge.localRotation;
            Assert.That(finalPivot, Is.EqualTo(new Vector2(0.5f, -1.2f)));
            Assert.That(originalBadge.GetComponent<CanvasGroup>().alpha, Is.Zero);
            Assert.That(levels.transform.GetChild(2).GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f));

            displayer.SetSelectedProgress(0.5f);
            Assert.That(background.anchoredPosition.y, Is.EqualTo(10f).Within(1e-3f));
            Assert.That(background.sizeDelta.x, Is.EqualTo(282.5f).Within(1e-3f));
            Assert.That(cover.sizeDelta.x, Is.EqualTo(222.5f).Within(1e-3f));
            Assert.That(((RectTransform)originalBadge).anchoredPosition, Is.EqualTo(Vector2.Lerp(originalPosition, finalPosition, 0.5f)));
            Assert.That(((RectTransform)originalBadge).pivot, Is.EqualTo(Vector2.Lerp(originalPivot, finalPivot, 0.5f)));
            Assert.That(Quaternion.Angle(originalBadge.localRotation, Quaternion.Lerp(Quaternion.identity, finalRotation, 0.5f)), Is.LessThan(0.001f));
            Assert.That(originalBadge.GetComponent<CanvasGroup>().alpha, Is.EqualTo(0.5f));
            Assert.That(selected.alpha, Is.EqualTo(0.5f));
            Assert.That(CoverOf(displayer).enabled, Is.True);
            Assert.That(background.GetComponent<Image>().enabled, Is.True);
            Assert.That(CoverOf(displayer).sprite, Is.SameAs(sprite));
            Assert.That(levels.transform.GetChild(3), Is.SameAs(originalBadge));

            displayer.SetSelectedProgress(0f);
            Assert.That(background.anchoredPosition, Is.EqualTo(new Vector2(0, 20)));
            Assert.That(levels.activeSelf, Is.True);
            Assert.That(originalBadge.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f));
            Assert.That(selected.alpha, Is.Zero);
        }

        /// <summary>Checks pooled visual difficulty labels and colors update before and after binding a song.</summary>
        [Test]
        public void SongCover_SetDifficulty_UpdatesItsOwnedRingAndSelectedBadge()
        {
            var displayer = CreateSongCoverDisplayer(Canvas);
            var backgroundObject = displayer.transform.Find("BG").gameObject;
            Object.DestroyImmediate(backgroundObject.GetComponent<Image>());
            var background = backgroundObject.AddComponent<QualityUiImage>();
            var sourceColor = background.color;
            Activate(displayer);
            var sourceMaterial = background.SourceMaterial;
            var displayedMaterial = background.material;
            Assert.That(sourceColor, Is.EqualTo(Color.white));
            var titles = new[] { "Easy", "Basic", "Advance", "Expert", "Master", "Re:Master", "UTAGE" };
            for (var index = 0; index < titles.Length; index++)
            {
                displayer.SetDifficulty(index);
                Assert.That(background.color, Is.EqualTo(sourceColor), "Difficulty changes must leave the glass frame white.");
                Assert.That(background.SourceMaterial, Is.SameAs(sourceMaterial));
                Assert.That(background.material, Is.SameAs(displayedMaterial));
                Assert.That(PrivateMember.GetField<TextMeshProUGUI>(displayer, "_selectedLevelTitle").text, Is.EqualTo(titles[index]));
                Assert.That(PrivateMember.GetField<Image>(displayer, "_selectedLevelColor").color, Is.EqualTo(DifficultyColors()[index]));
            }
            displayer.SetSongDetail(new FakeSongDetail("majtest-pool-diff", "Song", "1", "3", "7", "10", "13", "14+", "15"));
            displayer.SetDifficulty(5);
            displayer.SetSelectedProgress(1f);
            Assert.That(PrivateMember.GetField<TextMeshProUGUI>(displayer, "_selectedLevelText").text, Is.EqualTo("14+"));
            Assert.That(PrivateMember.GetField<Image>(displayer, "_levelRingDisplayer").color, Is.EqualTo(DifficultyColors()[5]));
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
            Assert.That(CoverOf(displayer).sprite, Is.Null);
            Assert.That(loading.activeSelf, Is.True);
            yield return WaitUntil(() => CoverOf(displayer).sprite == cover);

            Assert.That(CoverOf(displayer).sprite, Is.SameAs(cover));
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
