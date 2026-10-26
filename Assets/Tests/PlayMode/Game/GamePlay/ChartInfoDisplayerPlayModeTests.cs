using System.Collections;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.GamePlay.PlayMode.Support;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.PlayMode
{
    /// <summary>Checks the song metadata and cover display of <see cref="ChartInfoDisplayer"/> in Play Mode.</summary>
    public class ChartInfoDisplayerPlayModeTests
    {
        /// <summary>Tracks objects created by a test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Creates the object tracker.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
        }

        /// <summary>Destroys every object created by the test.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
        }

        /// <summary>Checks that the title, artist, designer and level of the selected chart are shown.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SetMetadata_ShowsTheSelectedChart()
        {
            var displayer = CreateDisplayer(out _);
            yield return null;
            var song = new GamePlayFakeSongDetail { Title = "Song", Artist = "Composer" };

            displayer.SetMetadata(song, ChartLevel.Master);

            Assert.That(displayer.title.text, Is.EqualTo("Song"));
            Assert.That(displayer.artist.text, Is.EqualTo("Composer"));
            Assert.That(displayer.designer.text, Is.EqualTo("D4"));
            Assert.That(displayer.level.text, Is.EqualTo("13+"));
        }

        /// <summary>Checks that setting the cover shows it and hides the loading indicator.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SetCover_ShowsTheCoverAndHidesLoading()
        {
            var displayer = CreateDisplayer(out var loading);
            var texture = _objects.Track(new Texture2D(4, 4));
            var cover = _objects.Track(Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.zero));
            yield return null;

            displayer.SetCover(cover);

            Assert.That(displayer.coverImg.sprite, Is.SameAs(cover));
            Assert.That(loading.activeSelf, Is.False);
        }

        /// <summary>Creates an active displayer with its texts, cover image and loading indicator.</summary>
        /// <param name="loading">Receives the loading indicator.</param>
        /// <returns>The displayer.</returns>
        private ChartInfoDisplayer CreateDisplayer(out GameObject loading)
        {
            var displayer = _objects.Add<ChartInfoDisplayer>();
            var parent = displayer.transform;
            displayer.title = _objects.Add<TextMeshProUGUI>("Title", parent);
            displayer.artist = _objects.Add<TextMeshProUGUI>("Artist", parent);
            displayer.designer = _objects.Add<TextMeshProUGUI>("Designer", parent);
            displayer.level = _objects.Add<TextMeshProUGUI>("Level", parent);
            displayer.coverImg = _objects.Add<Image>("Cover", parent);
            loading = _objects.Create("Loading", parent);
            PrivateMember.SetField(displayer, "_loadingObj", loading);
            return displayer;
        }
    }
}
