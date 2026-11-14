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

        /// <summary>The same single-background presenter used by Setting category tabs.</summary>
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

        /// <summary>Checks that a virtual local list hides the icon and centers the name.</summary>
        [Test]
        public void VirtualLocalList_HidesIconAndCentersName()
        {
            _displayer.SetCollection(Collection(ChartStorageType.List));

            Assert.That(_name.text, Is.EqualTo("Name List"));
            Assert.That(_icon.enabled, Is.False);
            Assert.That(PrivateMember.GetField<RectTransform>(_tab, "_titleViewport").anchoredPosition, Is.EqualTo(Vector2.zero));
        }

        /// <summary>Checks local folders retain their icon through selection and pooled reuse after an aggregate list.</summary>
        [Test]
        public void LocalDirectory_ShowsFolderIconThroughSelectionAndReuse()
        {
            var folder = new SongCollection("Folder", Array.Empty<ISongDetail>()) { IsVirtual = false };
            foreach (var distance in new[] { 0f, 1f })
            {
                _displayer.SetCollection(Collection(ChartStorageType.List));
                Assert.That(_icon.enabled, Is.False);
                _displayer.SetCollection(folder);
                _displayer.SetDistance(distance);

                Assert.That(_icon.enabled, Is.True);
                Assert.That(_icon.sprite, Is.SameAs(_folder));
                Assert.That(_icon.color, Is.EqualTo(Color.yellow));
                Assert.That(_icon.rectTransform.anchoredPosition.y, Is.EqualTo(distance == 0f ? 62f : 16f));
                Assert.That(_icon.rectTransform.sizeDelta.x, Is.EqualTo(distance == 0f ? 48f : 24f));
            }
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
                Assert.That(PrivateMember.GetField<RectTransform>(_tab, "_titleViewport").anchoredPosition, Is.EqualTo(new Vector2(0f, -17f)), collection.Type.ToString());
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

        /// <summary>Checks one body changes width and color while the native icon rises outside it and the title centers.</summary>
        [Test]
        public void SetDistance_MovesAndHighlightsTheActualCollectionTab()
        {
            _displayer.SetCollection(Collection(ChartStorageType.FavoriteList));
            var iconColor = _icon.color;
            var background = PrivateMember.GetField<Image>(_tab, "_background");
            var viewport = PrivateMember.GetField<RectTransform>(_tab, "_titleViewport");
            _displayer.SetDistance(0f);
            Assert.That(_displayer.RectTransform.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(_displayer.RectTransform.sizeDelta, Is.EqualTo(new Vector2(254.69f, 76.8f)));
            Assert.That(background.color, Is.EqualTo(Color.blue));
            Assert.That(_name.fontSize, Is.EqualTo(32f));
            Assert.That(_name.rectTransform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(_name.canvasRenderer.GetColor(), Is.EqualTo(Color.white));
            Assert.That(_icon.rectTransform.sizeDelta, Is.EqualTo(new Vector2(48f, 48f)));
            Assert.That(viewport.rect.width, Is.EqualTo(156f).Within(0.001f));
            Assert.That(_name.rectTransform.rect.width, Is.EqualTo(156f).Within(0.001f));
            Assert.That(_icon.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(0f, 62f)));
            Assert.That(viewport.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(_name.rectTransform.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(_displayer.GetComponentsInChildren<Image>(true), Has.Length.EqualTo(2));

            _displayer.SetDistance(1f);
            Assert.That(_displayer.RectTransform.anchoredPosition.x, Is.EqualTo(218f));
            Assert.That(_displayer.RectTransform.sizeDelta, Is.EqualTo(new Vector2(147.83f, 76.8f)));
            Assert.That(background.color, Is.EqualTo(Color.white));
            Assert.That(_name.canvasRenderer.GetColor(), Is.EqualTo(Color.gray));
            Assert.That(_name.fontSize, Is.EqualTo(22f));
            Assert.That(_icon.rectTransform.sizeDelta, Is.EqualTo(new Vector2(24f, 24f)));
            Assert.That(viewport.rect.width, Is.EqualTo(111.83f).Within(0.001f));
            Assert.That(viewport.anchoredPosition, Is.EqualTo(new Vector2(0f, -17f)));
            Assert.That(_icon.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(0f, 16f)));
            Assert.That(_icon.color, Is.EqualTo(iconColor));
            Assert.That(_name.text, Is.EqualTo("Name FavoriteList"));
        }

        /// <summary>Checks either direction blends the same body and restores icon and title positions after reversal.</summary>
        [Test]
        public void SetDistance_InterpolatesSymmetricallyAndRestoresSelection()
        {
            _displayer.SetCollection(Collection(ChartStorageType.FavoriteList));
            var background = PrivateMember.GetField<Image>(_tab, "_background");
            var viewport = PrivateMember.GetField<RectTransform>(_tab, "_titleViewport");
            foreach (var distance in new[] { -0.5f, 0.5f })
            {
                _displayer.SetDistance(distance);
                Assert.That(_displayer.RectTransform.anchoredPosition.x, Is.EqualTo(218f * distance).Within(0.0001f));
                Assert.That(_displayer.RectTransform.sizeDelta.x, Is.EqualTo(201.26f).Within(0.0001f));
                Assert.That(_displayer.RectTransform.sizeDelta.y, Is.EqualTo(76.8f).Within(0.0001f));
                Assert.That(background.color, Is.EqualTo(Color.Lerp(Color.white, Color.blue, 0.5f)));
                Assert.That(_icon.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(0f, 39f)));
                Assert.That(_icon.rectTransform.sizeDelta, Is.EqualTo(new Vector2(36f, 36f)));
                Assert.That(viewport.rect.width, Is.EqualTo(133.915f).Within(0.001f));
                Assert.That(_name.fontSize, Is.EqualTo(27f));
                Assert.That(viewport.anchoredPosition, Is.EqualTo(new Vector2(0f, -8.5f)));
            }
            _displayer.SetDistance(0f);
            Assert.That(background.color, Is.EqualTo(Color.blue));
            Assert.That(_icon.rectTransform.anchoredPosition.y, Is.EqualTo(62f));
            Assert.That(_displayer.GetComponents<CanvasGroup>(), Is.Empty);
        }

        /// <summary>Checks a long unselected title starts at its beginning and retains the capsule's clipping viewport.</summary>
        [Test]
        public void LongUnselectedTitle_ClipsFromTheBeginningWithinTheInsetViewport()
        {
            _displayer.SetCollection(new SongCollection("CiRCLE PLUS A considerably longer collection name",
                Array.Empty<ISongDetail>()) { Type = ChartStorageType.PlayList });
            _displayer.SetDistance(0f);
            _displayer.SetDistance(1f);
            Canvas.ForceUpdateCanvases();
            _name.ForceMeshUpdate();

            Assert.That(_name.fontSize, Is.EqualTo(22f));
            Assert.That(_name.enableAutoSizing, Is.False);
            Assert.That(_name.overflowMode, Is.EqualTo(TextOverflowModes.Overflow));
            Assert.That(_name.horizontalAlignment, Is.EqualTo(HorizontalAlignmentOptions.Left));
            Assert.That(_name.isTextTruncated, Is.False);
            Assert.That(_name.canvasRenderer.hasRectClipping, Is.True);
            Assert.That(_name.rectTransform.rect.width, Is.EqualTo(111.83f).Within(0.001f));
            var bounds = _name.rectTransform.rect;
            Assert.That(_name.textInfo.characterInfo[0].origin, Is.EqualTo(bounds.xMin).Within(0.01f));
            Assert.That(_name.preferredWidth, Is.GreaterThan(bounds.width),
                "The complete title remains available; the existing RectMask2D clips its overflow.");
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
            var background = CreateImage("BG", displayer.transform);
            background.color = Color.white;
            var viewport = (RectTransform)CreateRect("TitleViewport", displayer.transform).transform;
            viewport.sizeDelta = new Vector2(111.83f, 40f);
            viewport.anchoredPosition = new Vector2(0f, -17f);
            viewport.gameObject.AddComponent<RectMask2D>();
            title = CreateText("Name", canvas);
            title.fontSize = 22f;
            title.color = Color.gray;
            title.rectTransform.sizeDelta = viewport.sizeDelta;
            title.transform.SetParent(viewport, false);
            icon = CreateImage("Icon", displayer.transform);
            icon.rectTransform.sizeDelta = new Vector2(24f, 24f);
            icon.rectTransform.anchoredPosition = new Vector2(0f, 16f);
            PrivateMember.SetField(tab, "_background", background);
            PrivateMember.SetField(tab, "_selectedBackgroundColor", Color.blue);
            PrivateMember.SetField(tab, "_selectedWidth", 254.69f);
            PrivateMember.SetField(tab, "_titleDisplayer", title);
            PrivateMember.SetField(tab, "_titleViewport", viewport);
            PrivateMember.SetField(tab, "_unselectedTitleInset", 18f);
            PrivateMember.SetField(tab, "_selectedTextColor", Color.white);
            PrivateMember.SetField(tab, "_selectedFontSize", 32f);
            PrivateMember.SetField(tab, "_iconTransform", icon.rectTransform);
            PrivateMember.SetField(tab, "_selectedIconPosition", new Vector2(0f, 62f));
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
