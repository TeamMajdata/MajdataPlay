using System.Collections;
using System.Collections.Generic;
using MajdataPlay.i18n;
using MajdataPlay.Scenes.Setting;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.SettingScene
{
    /// <summary>Covers the category title of the setting scene: layout by distance, localization and visibility.</summary>
    public class MenuTitleDisplayerTests : SceneTestBase
    {
        /// <summary>The displayer under test.</summary>
        private MenuTitleDisplayer _displayer = null!;

        /// <summary>The background shown while unselected.</summary>
        private Image _unselected = null!;

        /// <summary>The background shown while selected.</summary>
        private Image _selected = null!;

        /// <summary>The title text.</summary>
        private TextMeshProUGUI _title = null!;

        /// <summary>Creates the displayer with its backgrounds and title.</summary>
        [SetUp]
        public void SetUp()
        {
            var canvas = CreateCanvas();
            _displayer = AddInactiveUi<MenuTitleDisplayer>("Title", canvas);
            ((RectTransform)_displayer.transform).sizeDelta = new Vector2(200, 60);
            _unselected = CreateImage("Unselected", _displayer.transform);
            _unselected.color = new Color(1f, 1f, 1f, 0.8f);
            _selected = CreateImage("Selected", _displayer.transform);
            _selected.color = new Color(0f, 0f, 1f, 0.5f);
            _selected.rectTransform.sizeDelta = new Vector2(300, 80);
            // TMP loads its default sizes in its own Awake, so the title is configured while active and then moved.
            _title = CreateText("TitleText", canvas);
            _title.color = Color.gray;
            _title.fontSizeMax = 16f;
            _title.transform.SetParent(_displayer.transform, false);
            PrivateMember.SetField(_displayer, "_unselectedBackground", _unselected);
            PrivateMember.SetField(_displayer, "_selectedBackground", _selected);
            PrivateMember.SetField(_displayer, "_titleDisplayer", _title);
            PrivateMember.SetField(_displayer, "_selectedTextColor", Color.white);
            PrivateMember.SetField(_displayer, "_selectedFontSizeMax", 32f);
            Activate(_displayer);
        }

        /// <summary>Checks that Awake centers the title anchors and exposes the title font.</summary>
        [Test]
        public void Awake_CentersTitleAndExposesFont()
        {
            Assert.That(_title.rectTransform.anchorMin, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(_title.rectTransform.anchorMax, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(_displayer.Font, Is.SameAs(_title.font));
            Assert.That(_title.color, Is.EqualTo(Color.white));
        }

        /// <summary>Checks that the selected position centers and enlarges the title.</summary>
        [Test]
        public void SetDistance_Zero_ShowsSelectedStyle()
        {
            _displayer.SetDistance(0f);

            var rect = (RectTransform)_displayer.transform;
            Assert.That(rect.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(rect.sizeDelta, Is.EqualTo(new Vector2(300, 80)));
            Assert.That(_unselected.color.a, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(_selected.color.a, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(_title.rectTransform.localScale.x, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(_title.canvasRenderer.GetColor(), Is.EqualTo(Color.white));
        }

        /// <summary>Checks that a neighbouring position uses the unselected style at the first step.</summary>
        [Test]
        public void SetDistance_One_ShowsUnselectedStyle()
        {
            _displayer.SetDistance(1f);

            var rect = (RectTransform)_displayer.transform;
            Assert.That(rect.anchoredPosition.x, Is.EqualTo(218f).Within(1e-3f));
            Assert.That(rect.sizeDelta, Is.EqualTo(new Vector2(200, 60)));
            Assert.That(_unselected.color.a, Is.EqualTo(0.8f).Within(1e-4f));
            Assert.That(_selected.color.a, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(_title.rectTransform.localScale.x, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(_title.canvasRenderer.GetColor(), Is.EqualTo(Color.gray));
        }

        /// <summary>Checks that distant positions continue in fixed steps on both sides.</summary>
        [Test]
        public void SetDistance_FarPositions_UseFixedSteps()
        {
            var rect = (RectTransform)_displayer.transform;

            _displayer.SetDistance(-2.5f);
            Assert.That(rect.anchoredPosition.x, Is.EqualTo(-(218f + 164f * 1.5f)).Within(1e-3f));

            _displayer.SetDistance(0.5f);
            Assert.That(rect.anchoredPosition.x, Is.EqualTo(109f).Within(1e-3f));
            Assert.That(rect.sizeDelta, Is.EqualTo(new Vector2(250, 70)));
        }

        /// <summary>Checks that the title text follows its localization key and language changes while enabled.</summary>
        [Test]
        public void Initialize_LocalizesAndFollowsLanguageChanges()
        {
            _displayer.Initialize("FIXTURE_TITLE");
            Assert.That(_title.text, Is.EqualTo("FIXTURE_TITLE"));

            Localization.Current = new Language
            {
                Code = "fx-FX",
                Translations = new Dictionary<string, string> { { "FIXTURE_TITLE", "Localized" } }
            };

            Assert.That(_title.text, Is.EqualTo("Localized"));
        }

        /// <summary>Checks that a computed title is re-evaluated on language changes and that a null provider is rejected.</summary>
        [Test]
        public void Initialize_Provider_FollowsLanguageChanges()
        {
            _displayer.Initialize(() => Localization.Current.Code == "fx-FX" ? "Computed" : "Raw");
            Assert.That(_title.text, Is.EqualTo("Raw"));

            Localization.Current = new Language { Code = "fx-FX" };

            Assert.That(_title.text, Is.EqualTo("Computed"));
            Assert.Throws<System.ArgumentNullException>(() => _displayer.Initialize((System.Func<string>)null!));
        }

        /// <summary>Checks that a hidden title ignores language changes until it is shown again.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SetVisible_UnsubscribesWhileHidden()
        {
            _displayer.Initialize("FIXTURE_TITLE");
            _displayer.SetVisible(false);
            Assert.That(_displayer.gameObject.activeSelf, Is.False);

            Localization.Current = new Language
            {
                Translations = new Dictionary<string, string> { { "FIXTURE_TITLE", "Hidden" } }
            };
            Assert.That(_title.text, Is.EqualTo("FIXTURE_TITLE"));

            _displayer.SetVisible(true);
            _displayer.SetVisible(true);
            yield return null;

            Assert.That(_displayer.gameObject.activeSelf, Is.True);
            Assert.That(_title.text, Is.EqualTo("Hidden"));
        }

        /// <summary>Checks that an uninitialized title keeps its text when enabled or when the language changes.</summary>
        [Test]
        public void WithoutKey_KeepsText()
        {
            _title.text = "Original";

            Localization.Current = new Language();

            Assert.That(_title.text, Is.EqualTo("Original"));
        }

        /// <summary>Checks that destroying the title unsubscribes from language changes.</summary>
        [Test]
        public void Destroy_UnsubscribesFromLanguageChanges()
        {
            _displayer.Initialize("FIXTURE_TITLE");

            Object.DestroyImmediate(_displayer.gameObject);

            Assert.DoesNotThrow(() => Localization.Current = new Language());
        }
    }
}
