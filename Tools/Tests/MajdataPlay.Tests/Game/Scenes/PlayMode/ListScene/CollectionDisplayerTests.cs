using System;
using MajdataPlay.Collections;
using MajdataPlay.Scenes.List.Models;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.ListScene
{
    /// <summary>Covers the collection tab of the song list: its name and the icon chosen per collection type.</summary>
    public class CollectionDisplayerTests : SceneTestBase
    {
        /// <summary>The displayer under test.</summary>
        private CollectionDisplayer _displayer = null!;

        /// <summary>The icon image.</summary>
        private Image _icon = null!;

        /// <summary>The collection name text.</summary>
        private TextMeshProUGUI _name = null!;

        /// <summary>The icon sprites by role.</summary>
        private Sprite _online = null!;

        /// <summary>The folder icon sprite.</summary>
        private Sprite _folder = null!;

        /// <summary>The favorite icon sprite.</summary>
        private Sprite _favorite = null!;

        /// <summary>The dan icon sprite.</summary>
        private Sprite _dan = null!;

        /// <summary>Creates the displayer with distinct sprites and colors per icon.</summary>
        [SetUp]
        public void SetUp()
        {
            _displayer = AddInactiveUi<CollectionDisplayer>();
            _icon = CreateImage("Icon", _displayer.transform);
            _name = CreateText("Name", _displayer.transform);
            _online = CreateSprite("Online");
            _folder = CreateSprite("Folder");
            _favorite = CreateSprite("Favorite");
            _dan = CreateSprite("Dan");
            PrivateMember.SetField(_displayer, "_iconDisplayer", _icon);
            PrivateMember.SetField(_displayer, "_nameDisplayer", _name);
            PrivateMember.SetField(_displayer, "_onlineCollectionIcon", _online);
            PrivateMember.SetField(_displayer, "_onlineCollectionIconColor", Color.cyan);
            PrivateMember.SetField(_displayer, "_folderCollectionIcon", _folder);
            PrivateMember.SetField(_displayer, "_folderCollectionIconColor", Color.yellow);
            PrivateMember.SetField(_displayer, "_favoriteCollectionIcon", _favorite);
            PrivateMember.SetField(_displayer, "_favoriteCollectionIconColor", Color.red);
            PrivateMember.SetField(_displayer, "_danCollectionIcon", _dan);
            PrivateMember.SetField(_displayer, "_danCollectionIconColor", Color.magenta);
            Activate(_displayer);
        }

        /// <summary>Creates an empty collection of a type.</summary>
        /// <param name="type">The collection type.</param>
        /// <param name="isOnline">Whether the collection comes from a server.</param>
        /// <returns>The collection.</returns>
        private static SongCollection Collection(ChartStorageType type, bool isOnline = false)
        {
            return new SongCollection("Name " + type, Array.Empty<ISongDetail>())
            {
                Type = type,
                IsOnline = isOnline
            };
        }

        /// <summary>Checks that Awake exposes the rect transform.</summary>
        [Test]
        public void Awake_ExposesRectTransform()
        {
            Assert.That(_displayer.RectTransform, Is.SameAs(_displayer.transform));
        }

        /// <summary>Checks that a local list hides the icon and centers the name.</summary>
        [Test]
        public void LocalList_HidesIconAndCentersName()
        {
            _displayer.SetCollection(Collection(ChartStorageType.List));

            Assert.That(_name.text, Is.EqualTo("Name List"));
            Assert.That(_icon.enabled, Is.False);
            Assert.That(_name.rectTransform.anchoredPosition, Is.EqualTo(Vector2.zero));
        }

        /// <summary>Checks the icon, color and name offset of each icon-bearing collection type.</summary>
        [Test]
        public void IconTypes_UseTheirSpriteAndColor()
        {
            var cases = new (SongCollection Collection, Sprite Sprite, Color Color)[]
            {
                (Collection(ChartStorageType.List, true), _online, Color.cyan),
                (Collection(ChartStorageType.Recommend), _online, Color.cyan),
                (Collection(ChartStorageType.Dan), _dan, Color.magenta),
                (Collection(ChartStorageType.PlayList), _folder, Color.yellow),
                (Collection(ChartStorageType.FavoriteList), _favorite, Color.red),
            };
            foreach (var (collection, sprite, color) in cases)
            {
                _displayer.SetCollection(collection);

                Assert.That(_icon.enabled, Is.True, collection.Type.ToString());
                Assert.That(_icon.sprite, Is.SameAs(sprite), collection.Type.ToString());
                Assert.That(_icon.color, Is.EqualTo(color), collection.Type.ToString());
                Assert.That(_name.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(0, -25)), collection.Type.ToString());
            }
        }

        /// <summary>Checks argument validation and activation.</summary>
        [Test]
        public void SetCollectionAndActive_ValidateAndToggle()
        {
            Assert.That(() => _displayer.SetCollection(null!), Throws.ArgumentNullException);

            _displayer.SetActive(false);
            Assert.That(_displayer.gameObject.activeSelf, Is.False);
            _displayer.SetActive(true);
            Assert.That(_displayer.gameObject.activeSelf, Is.True);
        }

        /// <summary>Checks that the small cover base class caches its rect transform and online flag.</summary>
        [Test]
        public void CoverSmallDisplayer_CachesRectTransform()
        {
            var cover = AddInactiveUi<CoverSmallDisplayer>();
            Activate(cover);
            cover.IsOnline = true;

            Assert.That(cover.RectTransform, Is.SameAs(cover.transform));
            Assert.That(cover.IsOnline, Is.True);
        }
    }
}
