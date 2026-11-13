using MajdataPlay.i18n;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Scenes.Setting
{
    public sealed class MenuTitleDisplayer : MonoBehaviour
    {
        /// <summary>Separates adjacent unselected category titles outside the central selected interval.</summary>
        private const float UnselectedTitleSpacing = 164f;

        /// <summary>Separates the selected title from its nearest neighboring category.</summary>
        private const float SelectedTitleSpacing = 218f;

        /// <summary>Reserves the first and last fifth of a category transition for its navigation arrows.</summary>
        private const float NavigationFadeFraction = 0.2f;

        [SerializeField]
        [FormerlySerializedAs("_unselectedBackground")]
        private Image _unselectedBackground = null!;

        [SerializeField]
        [FormerlySerializedAs("_selectedBackground")]
        private Image _selectedBackground = null!;

        [SerializeField]
        [FormerlySerializedAs("_titleDisplayer")]
        private TextMeshProUGUI _titleDisplayer = null!;

        [SerializeField]
        [FormerlySerializedAs("_selectedTextColor")]
        private Color _selectedTextColor = Color.white;

        [SerializeField]
        [FormerlySerializedAs("_selectedFontSizeMax")]
        private float _selectedFontSizeMax = 32f;

        private RectTransform _rectTransform = null!;
        private RectTransform _titleRectTransform = null!;
        private Vector2 _unselectedSize;
        private Vector2 _selectedSize;
        private Color _unselectedBackgroundColor;
        private Color _selectedBackgroundColor;
        private Color _unselectedTextColor;
        private float _selectedTitleScale = 1f;
        private Func<string>? _localizedTitleProvider;
        private string? _displayedTitle;
        private SettingTextTint _textTint = null!;
        private bool _isLocalizationSubscribed;

        /// <summary>The last selection blend applied to the backgrounds and title.</summary>
        private float _lastSelectedAmount = float.NaN;

        internal TMP_FontAsset Font => _titleDisplayer.font;

        private void Awake()
        {
            _rectTransform = (RectTransform)transform;
            _titleRectTransform = _titleDisplayer.rectTransform;
            _unselectedSize = _rectTransform.rect.size;
            _selectedSize = _selectedBackground.rectTransform.rect.size;
            _unselectedBackgroundColor = _unselectedBackground.color;
            _selectedBackgroundColor = _selectedBackground.color;
            _unselectedTextColor = _titleDisplayer.color;
            _selectedTitleScale = _selectedFontSizeMax / Mathf.Max(1f, _titleDisplayer.fontSizeMax);
            _textTint = new SettingTextTint(_titleDisplayer);
        }

        private void OnEnable()
        {
            if (!_isLocalizationSubscribed)
            {
                Localization.OnLanguageChanged += OnLanguageChanged;
                _isLocalizationSubscribed = true;
            }
            UpdateLocalizedText();
        }

        private void OnDisable()
        {
            UnsubscribeLocalization();
        }

        private void OnDestroy()
        {
            UnsubscribeLocalization();
            _textTint?.Dispose();
        }

        private void UnsubscribeLocalization()
        {
            if (_isLocalizationSubscribed)
            {
                Localization.OnLanguageChanged -= OnLanguageChanged;
                _isLocalizationSubscribed = false;
            }
        }

        /// <summary>
        /// Shows the translation of a fixed localization key and follows language changes.
        /// </summary>
        /// <param name="localizationKey">The localization key of the title; an empty key leaves the current text in place.</param>
        internal void Initialize(string localizationKey)
        {
            if (string.IsNullOrEmpty(localizationKey))
            {
                _localizedTitleProvider = null;
                return;
            }
            Initialize(() => localizationKey.i18n());
        }

        /// <summary>
        /// Shows a title computed for the current language and recomputes it on every language change.
        /// </summary>
        /// <param name="localizedTitleProvider">Returns the localized title for the current language.</param>
        /// <exception cref="ArgumentNullException"><paramref name="localizedTitleProvider"/> is null.</exception>
        internal void Initialize(Func<string> localizedTitleProvider)
        {
            _localizedTitleProvider = localizedTitleProvider ?? throw new ArgumentNullException(nameof(localizedTitleProvider));
            UpdateLocalizedText();
        }

        internal void SetDistance(float distance)
        {
            var absDistance = Mathf.Abs(distance);
            var selectedAmount = 1f - Mathf.Clamp01(absDistance);

            _rectTransform.anchoredPosition = new Vector2(GetHorizontalPosition(distance, absDistance), 0);
            if (_lastSelectedAmount == selectedAmount)
            {
                return;
            }
            _lastSelectedAmount = selectedAmount;
            _rectTransform.sizeDelta = Vector2.Lerp(_unselectedSize, _selectedSize, selectedAmount);

            _unselectedBackground.color = WithAlpha(
                _unselectedBackgroundColor,
                _unselectedBackgroundColor.a * (1f - selectedAmount));
            _selectedBackground.color = WithAlpha(
                _selectedBackgroundColor,
                _selectedBackgroundColor.a * selectedAmount);

            var textColor = Color.Lerp(_unselectedTextColor, _selectedTextColor, selectedAmount);
            _textTint.SetColor(textColor);
            var titleScale = Mathf.Lerp(1f, _selectedTitleScale, selectedAmount);
            _titleRectTransform.localScale = new Vector3(titleScale, titleScale, 1f);
        }

        internal void SetVisible(bool isVisible)
        {
            if (gameObject.activeSelf != isVisible)
            {
                gameObject.SetActive(isVisible);
            }
        }

        /// <summary>Fades shared category arrows out before the scroll and back in as the selected title settles.</summary>
        /// <param name="progress">The linear zero-to-one category transition progress.</param>
        /// <param name="initialArrowAlpha">The arrow opacity retained when a new transition interrupts the previous one.</param>
        /// <returns>The navigation-only opacity, without fading the moving category titles.</returns>
        internal static float GetNavigationAlpha(float progress, float initialArrowAlpha)
        {
            if (progress < NavigationFadeFraction)
            {
                return initialArrowAlpha * (1f - Mathf.SmoothStep(0f, 1f, progress / NavigationFadeFraction));
            }
            if (progress > 1f - NavigationFadeFraction)
            {
                return Mathf.SmoothStep(0f, 1f, (progress - (1f - NavigationFadeFraction)) / NavigationFadeFraction);
            }
            return 0f;
        }

        private void OnLanguageChanged(object? sender, Language language)
        {
            UpdateLocalizedText();
        }

        private void UpdateLocalizedText()
        {
            if (_localizedTitleProvider is not null)
            {
                var title = _localizedTitleProvider();
                if (_displayedTitle != title)
                {
                    _titleDisplayer.text = title;
                    _displayedTitle = title;
                }
            }
        }

        private static float GetHorizontalPosition(float distance, float absDistance)
        {
            if (absDistance <= 1f)
            {
                return SelectedTitleSpacing * distance;
            }

            return Mathf.Sign(distance) * (SelectedTitleSpacing + (UnselectedTitleSpacing * (absDistance - 1f)));
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
