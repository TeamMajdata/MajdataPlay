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
    /// <summary>Covers the single category body, selection typography, clipping and localized title scrolling.</summary>
    public class MenuTitleDisplayerTests : SceneTestBase
    {
        /// <summary>The displayer under test.</summary>
        private MenuTitleDisplayer _displayer = null!;

        /// <summary>The same background retained in every selection state.</summary>
        private Image _background = null!;

        /// <summary>The authored clipping viewport independent of horizontal title scrolling.</summary>
        private RectTransform _viewport = null!;

        /// <summary>The single title text.</summary>
        private TextMeshProUGUI _title = null!;

        /// <summary>Creates one background and one statically authored clipping viewport and title.</summary>
        [SetUp]
        public void SetUp()
        {
            var canvas = CreateCanvas();
            _displayer = AddInactiveUi<MenuTitleDisplayer>("Title", canvas);
            ((RectTransform)_displayer.transform).sizeDelta = new Vector2(200f, 60f);
            _background = CreateImage("Background", _displayer.transform);
            _background.color = new Color(1f, 1f, 1f, 0.8f);
            _viewport = (RectTransform)CreateRect("Viewport", _displayer.transform).transform;
            _viewport.sizeDelta = new Vector2(160f, 40f);
            _viewport.gameObject.AddComponent<RectMask2D>();
            _title = CreateText("TitleText", canvas);
            _title.color = Color.gray;
            _title.fontSize = 22f;
            _title.rectTransform.sizeDelta = _viewport.sizeDelta;
            _title.transform.SetParent(_viewport, false);
            PrivateMember.SetField(_displayer, "_background", _background);
            PrivateMember.SetField(_displayer, "_selectedBackgroundColor", new Color(0f, 0f, 1f, 0.5f));
            PrivateMember.SetField(_displayer, "_selectedWidth", 300f);
            PrivateMember.SetField(_displayer, "_titleDisplayer", _title);
            PrivateMember.SetField(_displayer, "_titleViewport", _viewport);
            PrivateMember.SetField(_displayer, "_selectedTextColor", Color.white);
            PrivateMember.SetField(_displayer, "_selectedFontSize", 32f);
            Activate(_displayer);
        }

        /// <summary>Checks the title keeps its authored transform and uses explicit typography inside its authored mask.</summary>
        [Test]
        public void Awake_PreservesTitleAnchorsAndExposesFont()
        {
            Assert.That(_title.rectTransform.anchorMin, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(_title.rectTransform.anchorMax, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(_displayer.Font, Is.SameAs(_title.font));
            Assert.That(_title.color, Is.EqualTo(Color.white));
            Assert.That(_title.enableAutoSizing, Is.False);
            Assert.That(_title.textWrappingMode, Is.EqualTo(TextWrappingModes.NoWrap));
            Assert.That(_viewport.GetComponent<RectMask2D>(), Is.Not.Null);
        }

        /// <summary>Checks selection widens the same body without changing height or scaling its text transform.</summary>
        [Test]
        public void SetDistance_Zero_ShowsSelectedStyle()
        {
            _displayer.SetDistance(0f);

            var rect = (RectTransform)_displayer.transform;
            Assert.That(rect.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(rect.sizeDelta, Is.EqualTo(new Vector2(300f, 60f)));
            Assert.That(_background.color, Is.EqualTo(new Color(0f, 0f, 1f, 0.5f)));
            Assert.That(_title.fontSize, Is.EqualTo(32f));
            Assert.That(_viewport.rect.width, Is.EqualTo(201.31f).Within(0.001f));
            Assert.That(_title.rectTransform.rect.width, Is.EqualTo(_viewport.rect.width));
            Assert.That(_title.rectTransform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(_title.canvasRenderer.GetColor(), Is.EqualTo(Color.white));
            Assert.That(_displayer.GetComponentsInChildren<Image>(true), Has.Length.EqualTo(1));
        }

        /// <summary>Checks an unselected category restores fixed-size clipped typography and the original body tint.</summary>
        [Test]
        public void SetDistance_One_ShowsUnselectedStyle()
        {
            _displayer.SetDistance(1f);

            var rect = (RectTransform)_displayer.transform;
            Assert.That(rect.anchoredPosition.x, Is.EqualTo(218f).Within(1e-3f));
            Assert.That(rect.sizeDelta, Is.EqualTo(new Vector2(200f, 60f)));
            Assert.That(_background.color, Is.EqualTo(new Color(1f, 1f, 1f, 0.8f)));
            Assert.That(_title.fontSize, Is.EqualTo(22f));
            Assert.That(_viewport.rect.width, Is.EqualTo(192f).Within(0.001f));
            Assert.That(_title.overflowMode, Is.EqualTo(TextOverflowModes.Overflow));
            Assert.That(_title.rectTransform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(_title.canvasRenderer.GetColor(), Is.EqualTo(Color.gray));
        }

        /// <summary>Checks distant positions retain their spacing and intermediate selection changes width alone.</summary>
        [Test]
        public void SetDistance_FarPositions_UseFixedSteps()
        {
            var rect = (RectTransform)_displayer.transform;
            _displayer.SetDistance(-2.5f);
            Assert.That(rect.anchoredPosition.x, Is.EqualTo(-(218f + 164f * 1.5f)).Within(1e-3f));
            _displayer.SetDistance(0.5f);
            Assert.That(rect.anchoredPosition.x, Is.EqualTo(109f).Within(1e-3f));
            Assert.That(rect.sizeDelta, Is.EqualTo(new Vector2(250f, 60f)));
        }

        /// <summary>Checks unchanged selection amounts do not dirty style and a changed amount restores the shared appearance.</summary>
        [Test]
        public void SetDistance_UnchangedSelectionBlend_OnlyUpdatesPosition()
        {
            _displayer.SetDistance(2f);
            var rect = (RectTransform)_displayer.transform;
            var retainedSize = new Vector2(210f, 65f);
            rect.sizeDelta = retainedSize;
            _background.color = Color.red;
            _title.fontSize = 23f;
            _displayer.SetDistance(3f);
            Assert.That(rect.anchoredPosition.x, Is.EqualTo(218f + 164f * 2f));
            Assert.That(rect.sizeDelta, Is.EqualTo(retainedSize));
            Assert.That(_background.color, Is.EqualTo(Color.red));
            Assert.That(_title.fontSize, Is.EqualTo(23f));

            _displayer.SetDistance(0.5f);
            Assert.That(rect.sizeDelta, Is.EqualTo(new Vector2(250f, 60f)));
            Assert.That(_background.color, Is.EqualTo(Color.Lerp(new Color(1f, 1f, 1f, 0.8f), new Color(0f, 0f, 1f, 0.5f), 0.5f)));
            Assert.That(_title.fontSize, Is.EqualTo(27f));
            Assert.That(_viewport.rect.width, Is.EqualTo(196.655f).Within(0.001f));
            Assert.That(_title.rectTransform.localScale, Is.EqualTo(Vector3.one));
        }

        /// <summary>Checks long titles retain their beginning through enlargement without switching overflow modes.</summary>
        [Test]
        public void TransitionOverflow_KeepsTheBeginningWithoutEllipsis()
        {
            _title.text = "A considerably longer collection name than this title viewport can display";
            _displayer.RefreshTitle();
            foreach (var distance in new[] { 1f, 0.5f, 0.001f, 0f })
            {
                _displayer.SetDistance(distance);
                Canvas.ForceUpdateCanvases();
                _title.ForceMeshUpdate();

                Assert.That(_title.overflowMode, Is.EqualTo(TextOverflowModes.Overflow));
                Assert.That(_title.horizontalAlignment, Is.EqualTo(HorizontalAlignmentOptions.Left));
                Assert.That(_title.isTextTruncated, Is.False);
                Assert.That(_title.rectTransform.anchoredPosition, Is.EqualTo(Vector2.zero));
                Assert.That(_title.textInfo.characterInfo[0].origin,
                    Is.EqualTo(_title.rectTransform.rect.xMin).Within(0.01f));
                Assert.That(_title.canvasRenderer.hasRectClipping, Is.True);
            }
        }

        /// <summary>Checks titles that fit retain centered alignment throughout selection changes.</summary>
        [Test]
        public void ShortTitle_StaysCenteredThroughoutSelection()
        {
            _title.text = "Short";
            _displayer.RefreshTitle();
            foreach (var distance in new[] { 1f, 0.5f, 0.001f, 0f })
            {
                _displayer.SetDistance(distance);

                Assert.That(_title.horizontalAlignment, Is.EqualTo(HorizontalAlignmentOptions.Center));
                Assert.That(_title.rectTransform.anchoredPosition, Is.EqualTo(Vector2.zero));
            }
        }

        /// <summary>Checks selected scrolling pauses at the beginning and resets when leaving, reselecting or renaming.</summary>
        /// <returns>The title-scrolling coroutine.</returns>
        [UnityTest]
        public IEnumerator SelectedOverflow_ScrollsWithoutCreatingUiAndResetsOnLeave()
        {
            _title.text = "A considerably longer collection name than this title viewport can display";
            _displayer.RefreshTitle();
            var count = _displayer.GetComponentsInChildren<RectTransform>(true).Length;
            _displayer.SetDistance(0f);
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(_title.rectTransform.anchoredPosition, Is.EqualTo(Vector2.zero));
            yield return new WaitForSecondsRealtime(0.9f);
            Assert.That(_title.rectTransform.anchoredPosition.x, Is.LessThan(0f));
            Assert.That(_title.overflowMode, Is.EqualTo(TextOverflowModes.Overflow));
            Assert.That(_title.rectTransform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(_displayer.GetComponentsInChildren<RectTransform>(true), Has.Length.EqualTo(count));
            Assert.That(_viewport.anchoredPosition, Is.EqualTo(Vector2.zero));

            _displayer.SetDistance(1f);
            Assert.That(_title.rectTransform.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(_title.overflowMode, Is.EqualTo(TextOverflowModes.Overflow));
            Assert.That(_title.fontSize, Is.EqualTo(22f));
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(_title.rectTransform.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(_title.rectTransform.localScale, Is.EqualTo(Vector3.one));

            _displayer.SetDistance(0f);
            Assert.That(_title.rectTransform.anchoredPosition, Is.EqualTo(Vector2.zero));
            _title.text = "A renamed collection whose title is still longer than the selected viewport";
            _displayer.RefreshTitle();
            _title.ForceMeshUpdate();
            Assert.That(_title.horizontalAlignment, Is.EqualTo(HorizontalAlignmentOptions.Left));
            Assert.That(_title.textInfo.characterInfo[0].origin,
                Is.EqualTo(_title.rectTransform.rect.xMin).Within(0.01f));
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(_title.rectTransform.anchoredPosition, Is.EqualTo(Vector2.zero));
        }

        /// <summary>Checks scrolling reaches the last characters and returns to the first characters.</summary>
        /// <returns>The two endpoint transitions without waiting through the complete long title.</returns>
        [UnityTest]
        public IEnumerator SelectedOverflow_ReachesTheEndAndReturnsToTheBeginning()
        {
            _title.text = "A considerably longer collection name than this title viewport can display";
            _displayer.RefreshTitle();
            _displayer.SetDistance(0f);
            Canvas.ForceUpdateCanvases();
            _title.ForceMeshUpdate();
            yield return null;

            var overflow = _title.preferredWidth - _viewport.rect.width;
            PrivateMember.SetField(_displayer, "_titleScrollOffset", -overflow + 0.0001f);
            PrivateMember.SetField(_displayer, "_titleScrollPause", 0f);
            yield return null;

            var lastCharacter = _title.textInfo.characterInfo[_title.textInfo.characterCount - 1];
            Assert.That(lastCharacter.xAdvance + _title.rectTransform.anchoredPosition.x,
                Is.EqualTo(_viewport.rect.xMax).Within(0.05f));
            var endPosition = _title.rectTransform.anchoredPosition;
            yield return null;
            Assert.That(_title.rectTransform.anchoredPosition, Is.EqualTo(endPosition));

            PrivateMember.SetField(_displayer, "_titleScrollOffset", -0.0001f);
            PrivateMember.SetField(_displayer, "_titleScrollPause", 0f);
            yield return null;

            Assert.That(_title.rectTransform.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(_title.textInfo.characterInfo[0].origin,
                Is.EqualTo(_viewport.rect.xMin).Within(0.01f));
        }

        /// <summary>Checks the shared navigation-only fade, including a transition interrupted at partial opacity.</summary>
        /// <param name="progress">The normalized category transition time.</param>
        /// <param name="initialAlpha">The arrow opacity inherited from the preceding transition.</param>
        /// <param name="expectedAlpha">The expected arrow opacity without fading category contents.</param>
        [TestCase(0f, 1f, 1f)]
        [TestCase(0.1f, 1f, 0.5f)]
        [TestCase(0.2f, 1f, 0f)]
        [TestCase(0.5f, 1f, 0f)]
        [TestCase(0.8f, 1f, 0f)]
        [TestCase(0.9f, 1f, 0.5f)]
        [TestCase(1f, 1f, 1f)]
        [TestCase(0f, 0.4f, 0.4f)]
        [TestCase(0.1f, 0.4f, 0.2f)]
        [TestCase(0.9f, 0.4f, 0.5f)]
        public void NavigationAlpha_KeepsTheSharedFadeAndInterruptedOpacity(float progress, float initialAlpha, float expectedAlpha)
        {
            Assert.That(MenuTitleDisplayer.GetNavigationAlpha(progress, initialAlpha), Is.EqualTo(expectedAlpha).Within(0.0001f));
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
