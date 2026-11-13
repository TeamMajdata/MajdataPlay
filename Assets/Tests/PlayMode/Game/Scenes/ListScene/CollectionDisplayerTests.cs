using System;
using MajdataPlay.Collections;
using MajdataPlay.Scenes.List.Models;
using MajdataPlay.Scenes.Setting;
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

        /// <summary>The same two-background presenter used by Setting category tabs.</summary>
        private MenuTitleDisplayer _tab = null!;

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
            _displayer = CreateInactiveDisplayer("Collection", out _icon, out _name, out _tab);
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
                Assert.That(_name.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(0, -17)), collection.Type.ToString());
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

        /// <summary>Checks a moving category does not add a whole-item opacity group or alter authored graphics.</summary>
        [Test]
        public void Awake_DoesNotAddImplicitOpacityGroupOrChangeGraphicState()
        {
            _displayer.SetCollection(Collection(ChartStorageType.FavoriteList));
            _name.color = new Color(0.2f, 0.4f, 0.6f, 0.7f);
            var iconColor = _icon.color;
            var nameColor = _name.color;
            Assert.That(_displayer.GetComponents<CanvasGroup>(), Is.Empty);
            _displayer.SetActive(false);
            _displayer.SetActive(true);
            Assert.That(_displayer.GetComponents<CanvasGroup>(), Is.Empty);
            Assert.That(_displayer.gameObject.activeSelf, Is.True);
            Assert.That(_icon.enabled, Is.True);
            Assert.That(_icon.color, Is.EqualTo(iconColor));
            Assert.That(_name.color, Is.EqualTo(nameColor));
        }

        /// <summary>Checks preexisting root opacity and interaction settings are preserved through initialization and collection binding.</summary>
        [Test]
        public void Awake_PreservesAuthoredCanvasGroupWithoutOverridingIt()
        {
            var displayer = CreateInactiveDisplayer("Preconfigured collection", out _, out _, out _);
            var group = displayer.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0.7f;
            group.interactable = false;
            group.blocksRaycasts = false;
            Activate(displayer);
            displayer.SetCollection(Collection(ChartStorageType.List));
            displayer.SetActive(false);
            displayer.SetActive(true);

            Assert.That(displayer.GetComponent<CanvasGroup>(), Is.SameAs(group));
            Assert.That(displayer.GetComponents<CanvasGroup>().Length, Is.EqualTo(1));
            Assert.That(group.alpha, Is.EqualTo(0.7f));
            Assert.That(group.interactable, Is.False);
            Assert.That(group.blocksRaycasts, Is.False);
            Assert.That(displayer.gameObject.activeSelf, Is.True);
        }

        /// <summary>Checks the real data-bearing tab moves and changes its selected appearance without duplicating its name or icon.</summary>
        [Test]
        public void SetDistance_MovesAndHighlightsTheActualCollectionTab()
        {
            _displayer.SetCollection(Collection(ChartStorageType.FavoriteList));
            var iconColor = _icon.color;
            var unselected = PrivateMember.GetField<Image>(_tab, "_unselectedBackground");
            var selected = PrivateMember.GetField<Image>(_tab, "_selectedBackground");
            _displayer.SetDistance(0f);
            Assert.That(_displayer.RectTransform.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(_displayer.RectTransform.sizeDelta, Is.EqualTo(new Vector2(254.69f, 82.3f)));
            Assert.That(unselected.color.a, Is.Zero);
            Assert.That(selected.color.a, Is.EqualTo(1f));
            Assert.That(_name.rectTransform.localScale.x, Is.EqualTo(32f / 28f).Within(0.0001f));
            Assert.That(_name.canvasRenderer.GetColor(), Is.EqualTo(Color.white));
            Assert.That(_icon.rectTransform.sizeDelta, Is.EqualTo(new Vector2(24f, 24f)));
            Assert.That(_icon.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(0f, 16f)));
            Assert.That(_name.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(0f, -17f)));
            _displayer.SetDistance(1f);
            Assert.That(_displayer.RectTransform.anchoredPosition.x, Is.EqualTo(218f));
            Assert.That(_displayer.RectTransform.sizeDelta, Is.EqualTo(new Vector2(147.83f, 76.8f)));
            Assert.That(unselected.color.a, Is.EqualTo(1f));
            Assert.That(selected.color.a, Is.Zero);
            Assert.That(_name.canvasRenderer.GetColor(), Is.EqualTo(Color.gray));
            Assert.That(_icon.color, Is.EqualTo(iconColor));
            Assert.That(_name.text, Is.EqualTo("Name FavoriteList"));
        }

        /// <summary>Checks both directions interpolate the same two backgrounds and restore the selected appearance after reversal.</summary>
        [Test]
        public void SetDistance_InterpolatesSymmetricallyAndRestoresSelection()
        {
            var unselected = PrivateMember.GetField<Image>(_tab, "_unselectedBackground");
            var selected = PrivateMember.GetField<Image>(_tab, "_selectedBackground");
            foreach (var distance in new[] { -0.5f, 0.5f })
            {
                _displayer.SetDistance(distance);
                Assert.That(_displayer.RectTransform.anchoredPosition.x, Is.EqualTo(218f * distance).Within(0.0001f));
                Assert.That(_displayer.RectTransform.sizeDelta.x, Is.EqualTo(201.26f).Within(0.0001f));
                Assert.That(_displayer.RectTransform.sizeDelta.y, Is.EqualTo(79.55f).Within(0.0001f));
                Assert.That(unselected.color.a, Is.EqualTo(0.5f));
                Assert.That(selected.color.a, Is.EqualTo(0.5f));
                Assert.That(_displayer.gameObject.activeSelf, Is.True);
            }
            _displayer.SetDistance(0f);
            Assert.That(_displayer.RectTransform.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(selected.color.a, Is.EqualTo(1f));
            Assert.That(_displayer.GetComponents<CanvasGroup>(), Is.Empty);
        }

        /// <summary>Builds an inactive collection with the real Setting presenter initialized after its text defaults.</summary>
        /// <param name="name">The stable fixture object name.</param>
        /// <param name="icon">Receives the tab's native icon.</param>
        /// <param name="title">Receives the data-bearing collection name.</param>
        /// <param name="tab">Receives the shared layout and highlighting component.</param>
        /// <returns>The inactive collection displayer ready for its remaining serialized fixture fields.</returns>
        private CollectionDisplayer CreateInactiveDisplayer(string name, out Image icon, out TextMeshProUGUI title,
            out MenuTitleDisplayer tab)
        {
            var canvas = CreateCanvas(name + " Canvas");
            var displayer = AddInactiveUi<CollectionDisplayer>(name, canvas);
            ((RectTransform)displayer.transform).sizeDelta = new Vector2(147.83f, 76.8f);
            tab = displayer.gameObject.AddComponent<MenuTitleDisplayer>();
            var unselected = CreateImage("BG", displayer.transform);
            unselected.color = Color.white;
            var selected = CreateImage("SelectedBackground", displayer.transform);
            selected.rectTransform.sizeDelta = new Vector2(254.69f, 82.3f);
            selected.color = Color.blue;
            title = CreateText("Name", canvas);
            title.fontSizeMax = 28f;
            title.color = Color.gray;
            title.transform.SetParent(displayer.transform, false);
            icon = CreateImage("Icon", displayer.transform);
            icon.rectTransform.sizeDelta = new Vector2(24f, 24f);
            icon.rectTransform.anchoredPosition = new Vector2(0f, 16f);
            PrivateMember.SetField(tab, "_unselectedBackground", unselected);
            PrivateMember.SetField(tab, "_selectedBackground", selected);
            PrivateMember.SetField(tab, "_titleDisplayer", title);
            PrivateMember.SetField(tab, "_selectedTextColor", Color.white);
            PrivateMember.SetField(tab, "_selectedFontSizeMax", 32f);
            PrivateMember.SetField(displayer, "_tabDisplayer", tab);
            PrivateMember.SetField(displayer, "_iconDisplayer", icon);
            PrivateMember.SetField(displayer, "_nameDisplayer", title);
            return displayer;
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
