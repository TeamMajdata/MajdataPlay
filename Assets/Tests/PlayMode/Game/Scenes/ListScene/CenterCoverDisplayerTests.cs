using System.Collections;
using System.Threading;
using LitMotion;
using MajdataPlay.Drawing;
using MajdataPlay.Scenes.List;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.ListScene
{
    /// <summary>Checks selected-song information and background loading without a second foreground cover owner.</summary>
    public class CenterCoverDisplayerTests : ListSceneTestBase
    {
        /// <summary>Owns only selected-song information, background art and preview services.</summary>
        private CenterCoverDisplayer _center = null!;

        /// <summary>Creates the information-only center using the existing shared scene fixture.</summary>
        [SetUp]
        public void SetUp()
        {
            _center = CreateCenterCoverDisplayer(Canvas);
        }

        /// <summary>Creates a song with a Master chart and a cover.</summary>
        /// <param name="hash">The song hash.</param>
        /// <returns>The fixture-owned song.</returns>
        private FakeSongDetail Song(string hash)
        {
            var song = new FakeSongDetail(hash, "Center " + hash, "1", "3", string.Empty, "10", "13", "14", string.Empty)
            {
                Cover = CreateSprite("Cover")
            };
            song.Maidata = Maidata(ChartLevel.Master, Chart("(120){4}1,2,E"));
            return song;
        }

        /// <summary>Checks selected-song information and preview updates no longer require foreground cover or level renderers.</summary>
        [Test]
        public void SetSongDetail_UpdatesInformationWithoutForegroundCover()
        {
            var song = Song("majtest-center-layout");
            _center.SetDifficulty((int)ChartLevel.Expert);
            _center.SetSongDetail(song, 0);

            var metadata = PrivateMember.GetField<ChartMetadataDisplayer>(_center, "_metadataDisplayer");
            Assert.That(PrivateMember.GetField<TextMeshProUGUI>(metadata, "_title").text, Is.EqualTo(song.Title));
            Assert.That(PrivateMember.GetField<PreviewSoundPlayer>(_center, "_previewPlayer").IsPreviewPending(song), Is.True);
            Assert.That(PrivateMember.GetField<FavoriteAdder>(_center, "_favoriteAdder").GetComponent<Image>().enabled, Is.True);
            Assert.That(PrivateMember.GetField<OnlineInfoDisplayer>(_center, "_onlineInfoDisplayer").gameObject.activeSelf, Is.False);
            Assert.That(_center.transform.Find("Embedded"), Is.Null);
            Assert.That(_center.transform.Find("Levels"), Is.Null);
        }

        /// <summary>Checks background loading and its existing fade remain independent of pooled foreground artwork.</summary>
        /// <returns>The asynchronous background-only regression.</returns>
        [UnityTest]
        public IEnumerator SetSongDetail_LoadsAndFadesBackground()
        {
            var song = Song("majtest-center-cover");
            var background = PrivateMember.GetField<Image>(_center, "_bgSongCoverDisplayer");
            _center.SetSongDetail(song, 0);
            yield return WaitUntil(() => background.sprite == song.Cover);
            PrivateMember.GetField<MotionHandle>(_center, "_bgSongCoverAnim").TryComplete();

            Assert.That(background.color.a, Is.EqualTo(0.3f).Within(1e-4f));
            Assert.That(background.rectTransform.anchoredPosition, Is.EqualTo(Vector2.zero));
        }

        /// <summary>Checks repeated selection of the same song does not restart its background request.</summary>
        /// <returns>The duplicate-request regression.</returns>
        [UnityTest]
        public IEnumerator SetSongDetail_SameSongDoesNotReloadBackground()
        {
            var song = Song("majtest-center-repeat");
            var background = PrivateMember.GetField<Image>(_center, "_bgSongCoverDisplayer");
            _center.SetSongDetail(song, 0);
            yield return WaitUntil(() => background.sprite == song.Cover);
            var requests = song.CoverRequestCount;
            Assert.That(requests, Is.GreaterThan(0));
            _center.SetSongDetail(song, 0);
            yield return null;
            Assert.That(song.CoverRequestCount, Is.EqualTo(requests));
            _center.SetSelectionDetailsVisible(false);
            _center.SetSongDetail(song, 0);
            yield return WaitUntil(() => song.CoverRequestCount > requests);
            Assert.That(song.CoverRequestCount, Is.EqualTo(requests + 1), "Returning to the same song after cancellation must restart its background request.");
        }

        /// <summary>Checks difficulty changes still refresh metadata without a fixed foreground level ring.</summary>
        [Test]
        public void SetDifficulty_WithSong_RefreshesMetadata()
        {
            var song = Song("majtest-center-difficulty");
            song.SetDesigner((int)ChartLevel.ReMaster, "ReMaster Designer");
            _center.SetSongDetail(song, 0);
            _center.SetDifficulty((int)ChartLevel.ReMaster);
            var metadata = PrivateMember.GetField<ChartMetadataDisplayer>(_center, "_metadataDisplayer");
            Assert.That(PrivateMember.GetField<TextMeshProUGUI>(metadata, "_charter").text, Is.EqualTo("ReMaster Designer"));
        }

        /// <summary>Checks hiding selection details toggles only the favorite control and cancels selection-bound requests.</summary>
        [Test]
        public void SetSelectionDetailsVisible_PreservesBackgroundAndCancelsPendingRequests()
        {
            var song = Song("majtest-center-hidden");
            song.IsCompressedCoverLoaded = false;
            _center.SetSongDetail(song, 10000);
            var pending = PrivateMember.GetField<CancellationTokenSource>(_center, "_cts").Token;
            var background = PrivateMember.GetField<Image>(_center, "_bgSongCoverDisplayer");
            var favorite = _center.transform.Find("Favorite").gameObject;
            _center.SetSelectionDetailsVisible(false);
            Assert.That(favorite.activeSelf, Is.False);
            Assert.That(background.gameObject.activeInHierarchy, Is.True);
            Assert.That(pending.IsCancellationRequested, Is.True);
            _center.SetSelectionDetailsVisible(true);
            Assert.That(favorite.activeSelf, Is.True);
        }

        /// <summary>Checks deactivating the information owner clears only its background cover.</summary>
        [Test]
        public void SetActive_FalseClearsBackground()
        {
            _center.SetActive(false);
            Assert.That(_center.gameObject.activeSelf, Is.False);
            Assert.That(PrivateMember.GetField<Image>(_center, "_bgSongCoverDisplayer").sprite, Is.SameAs(SpriteLoader.EmptySprite));
            _center.SetActive(true);
            Assert.That(_center.gameObject.activeSelf, Is.True);
        }
    }
}
