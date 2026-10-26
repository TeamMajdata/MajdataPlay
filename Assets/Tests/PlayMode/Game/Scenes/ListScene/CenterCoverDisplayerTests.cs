using System.Collections;
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
    /// <summary>Covers the center cover of the song list: difficulty ring, level layout, cover loading and child displayers.</summary>
    public class CenterCoverDisplayerTests : ListSceneTestBase
    {
        /// <summary>The displayer under test.</summary>
        private CenterCoverDisplayer _center = null!;

        /// <summary>Creates the center cover with all child displayers.</summary>
        [SetUp]
        public void SetUp()
        {
            _center = CreateCenterCoverDisplayer(Canvas);
        }

        /// <summary>Gets a text field of the center cover.</summary>
        /// <param name="field">The field name.</param>
        /// <returns>The text.</returns>
        private string TextOf(string field)
        {
            return PrivateMember.GetField<TextMeshProUGUI>(_center, field).text;
        }

        /// <summary>Gets the transform of a level displayer.</summary>
        /// <param name="index">The level index.</param>
        /// <returns>The transform.</returns>
        private Transform Level(int index)
        {
            return PrivateMember.GetField<GameObject>(_center, "_levelDisplayerListRoot").transform.GetChild(index);
        }

        /// <summary>Creates a song with a Master chart and a cover.</summary>
        /// <param name="hash">The song hash.</param>
        /// <returns>The song.</returns>
        private FakeSongDetail Song(string hash)
        {
            var song = new FakeSongDetail(hash, "Center " + hash, "1", "3", string.Empty, "10", "13", "14", string.Empty)
            {
                Cover = CreateSprite("Cover")
            };
            song.Maidata = Maidata(ChartLevel.Master, Chart("(120){4}1,2,E"));
            return song;
        }

        /// <summary>Checks that Awake applies the remembered difficulty without a song.</summary>
        [Test]
        public void Awake_AppliesInitialDifficulty()
        {
            Assert.That(TextOf("_selectedLevelTitle"), Is.EqualTo("Easy"));
            Assert.That(TextOf("_selectedLevelText"), Is.EqualTo("-"));
            Assert.That(PrivateMember.GetField<Image>(_center, "_levelRingDisplayer").color, Is.EqualTo(DifficultyColors()[0]));
        }

        /// <summary>Checks the title and colors of every difficulty.</summary>
        [Test]
        public void SetDifficulty_ShowsTitleAndColorOfEachLevel()
        {
            var titles = new[] { "Easy", "Basic", "Advance", "Expert", "Master", "Re:Master", "UTAGE" };
            for (var i = 0; i < titles.Length; i++)
            {
                _center.SetDifficulty(i);

                Assert.That(TextOf("_selectedLevelTitle"), Is.EqualTo(titles[i]));
                Assert.That(PrivateMember.GetField<Image>(_center, "_selectedLevelColor").color, Is.EqualTo(DifficultyColors()[i]));
            }
        }

        /// <summary>Checks that a song shows its levels, rotates them around the selected one and updates its child displayers.</summary>
        [Test]
        public void SetSongDetail_LaysOutLevelsAndUpdatesChildren()
        {
            var song = Song("majtest-center-layout");
            _center.SetDifficulty((int)ChartLevel.Expert);

            _center.SetSongDetail(song, 0);

            Assert.That(Level(2).gameObject.activeSelf, Is.False);
            Assert.That(Level(6).gameObject.activeSelf, Is.False);
            Assert.That(Level(3).Find("Text").GetComponent<TextMeshProUGUI>().text, Is.EqualTo("10"));
            Assert.That(TextOf("_selectedLevelText"), Is.EqualTo("10"));
            Assert.That(Level(3).localRotation.eulerAngles.z, Is.EqualTo(50f).Within(1e-3f));
            Assert.That(Level(1).localRotation.eulerAngles.z, Is.EqualTo(67.5f).Within(1e-3f));
            Assert.That(Level(0).localRotation.eulerAngles.z, Is.EqualTo(77.5f).Within(1e-3f));
            Assert.That(Level(4).localRotation.eulerAngles.z, Is.EqualTo(32.5f).Within(1e-3f));
            Assert.That(Level(5).localRotation.eulerAngles.z, Is.EqualTo(22.5f).Within(1e-3f));

            var metadata = PrivateMember.GetField<ChartMetadataDisplayer>(_center, "_metadataDisplayer");
            Assert.That(PrivateMember.GetField<TextMeshProUGUI>(metadata, "_title").text, Is.EqualTo(song.Title));
            Assert.That(PrivateMember.GetField<PreviewSoundPlayer>(_center, "_previewPlayer").IsPreviewPending(song), Is.True);
            Assert.That(PrivateMember.GetField<FavoriteAdder>(_center, "_favoriteAdder").GetComponent<Image>().enabled, Is.True);
            Assert.That(PrivateMember.GetField<OnlineInfoDisplayer>(_center, "_onlineInfoDisplayer").gameObject.activeSelf, Is.False);
        }

        /// <summary>Checks that the cover is loaded into both cover images and the background fades in.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SetSongDetail_LoadsCoverAndFadesBackground()
        {
            var song = Song("majtest-center-cover");
            var cover = PrivateMember.GetField<Image>(_center, "_songCoverDisplayer");
            var background = PrivateMember.GetField<Image>(_center, "_bgSongCoverDisplayer");

            _center.SetSongDetail(song, 0);
            yield return WaitUntil(() => cover.sprite == song.Cover);
            PrivateMember.GetField<MotionHandle>(_center, "_bgSongCoverAnim").TryComplete();

            Assert.That(background.sprite, Is.SameAs(song.Cover));
            Assert.That(background.color.a, Is.EqualTo(0.3f).Within(1e-4f));
            Assert.That(background.rectTransform.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(PrivateMember.GetField<GameObject>(_center, "_loadingObj").activeSelf, Is.False);
        }

        /// <summary>Checks that an immediate cover is shown before loading and the same song is not reloaded.</summary>
        [Test]
        public void SetSongDetail_UsesImmediateCoverAndIgnoresSameSong()
        {
            var song = Song("majtest-center-immediate");
            song.IsCompressedCoverLoaded = false;
            var immediate = CreateSprite("Immediate");

            _center.SetSongDetail(song, 100000, immediate);
            _center.SetSongDetail(song, 0);

            Assert.That(PrivateMember.GetField<Image>(_center, "_songCoverDisplayer").sprite, Is.SameAs(immediate));
            Assert.That(PrivateMember.GetField<GameObject>(_center, "_loadingObj").activeSelf, Is.False);
            Assert.That(song.CoverRequestCount, Is.EqualTo(0));
        }

        /// <summary>Checks that changing difficulty after a song refreshes the designer of the new level.</summary>
        [Test]
        public void SetDifficulty_WithSong_RefreshesMetadata()
        {
            var song = Song("majtest-center-difficulty");
            song.SetDesigner((int)ChartLevel.ReMaster, "ReMaster Designer");
            _center.SetSongDetail(song, 0);

            _center.SetDifficulty((int)ChartLevel.ReMaster);

            var metadata = PrivateMember.GetField<ChartMetadataDisplayer>(_center, "_metadataDisplayer");
            Assert.That(PrivateMember.GetField<TextMeshProUGUI>(metadata, "_charter").text, Is.EqualTo("ReMaster Designer"));
            Assert.That(TextOf("_selectedLevelText"), Is.EqualTo("14"));
        }

        /// <summary>Checks that hiding the embedded cover hides its root and the favorite toggle.</summary>
        [Test]
        public void SetEmbeddedCoverVisible_TogglesRoots()
        {
            var embedded = PrivateMember.GetField<Image>(_center, "_songCoverDisplayer").transform.parent.gameObject;
            var favorite = _center.transform.Find("Favorite").gameObject;

            _center.SetEmbeddedCoverVisible(false);
            Assert.That(embedded.activeSelf, Is.False);
            Assert.That(favorite.activeSelf, Is.False);

            _center.SetEmbeddedCoverVisible(true);
            Assert.That(embedded.activeSelf, Is.True);
            Assert.That(favorite.activeSelf, Is.True);
        }

        /// <summary>Checks that deactivating clears the background cover.</summary>
        [Test]
        public void SetActive_FalseClearsBackground()
        {
            _center.SetActive(false);

            Assert.That(_center.gameObject.activeSelf, Is.False);
            Assert.That(PrivateMember.GetField<Image>(_center, "_bgSongCoverDisplayer").sprite, Is.SameAs(SpriteLoader.EmptySprite));

            _center.SetActive(true);
            Assert.That(_center.gameObject.activeSelf, Is.True);
        }

        /// <summary>Checks that a level displayer without a Text child is rejected.</summary>
        [Test]
        public void Awake_RequiresLevelText()
        {
            var center = AddInactiveUi<CenterCoverDisplayer>("Broken", Canvas);
            var root = CreateLevelRoot(center.transform);
            Object.DestroyImmediate(root.transform.GetChild(0).Find("Text").gameObject);
            PrivateMember.SetField(center, "_levelDisplayerListRoot", root);
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("Level displayer 0 does not have a child named 'Text'"));

            Activate(center);
        }
    }
}
