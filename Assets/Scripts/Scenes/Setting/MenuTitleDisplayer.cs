using MajdataPlay.Extensions;
using MajdataPlay.i18n;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Scenes.Setting
{
    public sealed class MenuTitleDisplayer : MonoBehaviour
    {
        const float X_POS_STEP = 164f;
        const float X_POS_WITH_DELTA_1 = 218f;

        [SerializeField]
        Image _unselectedBackground = null!;

        [SerializeField]
        Image _selectedBackground = null!;

        [SerializeField]
        TextMeshProUGUI _titleDisplayer = null!;

        [SerializeField]
        Color _selectedTextColor = Color.white;

        [SerializeField]
        float _selectedFontSizeMax = 32f;

        RectTransform _rectTransform = null!;
        RectTransform _titleRectTransform = null!;
        Vector2 _unselectedSize;
        Vector2 _selectedSize;
        Color _unselectedBackgroundColor;
        Color _selectedBackgroundColor;
        Color _unselectedTextColor;
        float _unselectedFontSizeMax;
        float _selectedTitleScale = 1f;
        string _localizationKey = string.Empty;
        string? _displayedTitle;
        SettingTextTint _textTint = null!;
        bool _isLocalizationSubscribed;

        internal TMP_FontAsset Font => _titleDisplayer.font;

        void Awake()
        {
            _rectTransform = (RectTransform)transform;
            _titleRectTransform = _titleDisplayer.rectTransform;
            _unselectedSize = _rectTransform.rect.size;
            _selectedSize = _selectedBackground.rectTransform.rect.size;
            // A stretch-anchored title changes its width whenever the background
            // expands, causing TMP to parse and auto-size it on every motion frame.
            var titleSize = _titleRectTransform.rect.size;
            var titlePosition = _titleRectTransform.localPosition;
            _titleRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _titleRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _titleRectTransform.sizeDelta = titleSize;
            _titleRectTransform.localPosition = titlePosition;
            _unselectedBackgroundColor = _unselectedBackground.color;
            _selectedBackgroundColor = _selectedBackground.color;
            _unselectedTextColor = _titleDisplayer.color;
            _unselectedFontSizeMax = _titleDisplayer.fontSizeMax;
            _selectedTitleScale = _selectedFontSizeMax / Mathf.Max(1f, _unselectedFontSizeMax);
            _textTint = new SettingTextTint(_titleDisplayer);
        }

        void OnEnable()
        {
            if (!_isLocalizationSubscribed)
            {
                Localization.OnLanguageChanged += OnLanguageChanged;
                _isLocalizationSubscribed = true;
            }
            UpdateLocalizedText();
        }

        void OnDisable()
        {
            UnsubscribeLocalization();
        }

        void OnDestroy()
        {
            UnsubscribeLocalization();
            _textTint?.Dispose();
        }

        void UnsubscribeLocalization()
        {
            if (_isLocalizationSubscribed)
            {
                Localization.OnLanguageChanged -= OnLanguageChanged;
                _isLocalizationSubscribed = false;
            }
        }

        internal void Initialize(string localizationKey)
        {
            _localizationKey = localizationKey;
            UpdateLocalizedText();
        }

        internal void SetDistance(float distance)
        {
            var absDistance = Mathf.Abs(distance);
            var selectedAmount = 1f - Mathf.Clamp01(absDistance);

            _rectTransform.anchoredPosition = new Vector2(GetHorizontalPosition(distance, absDistance), 0);
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

        void OnLanguageChanged(object? sender, Language language)
        {
            UpdateLocalizedText();
        }

        void UpdateLocalizedText()
        {
            if (!string.IsNullOrEmpty(_localizationKey))
            {
                var title = _localizationKey.i18n();
                if (_displayedTitle != title)
                {
                    _titleDisplayer.text = title;
                    _displayedTitle = title;
                }
            }
        }

        static float GetHorizontalPosition(float distance, float absDistance)
        {
            if (absDistance <= 1f)
            {
                return X_POS_WITH_DELTA_1 * distance;
            }

            return Mathf.Sign(distance) * (X_POS_WITH_DELTA_1 + X_POS_STEP * (absDistance - 1f));
        }

        static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
