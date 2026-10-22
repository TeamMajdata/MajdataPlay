using MajdataPlay.i18n;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Scenes.Setting
{
    public sealed class MenuTitleDisplayer : MonoBehaviour
    {
        private const float X_POS_STEP = 164f;
        private const float X_POS_WITH_DELTA_1 = 218f;

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
        private float _unselectedFontSizeMax;
        private float _selectedTitleScale = 1f;
        private string _localizationKey = string.Empty;
        private string? _displayedTitle;
        private SettingTextTint _textTint = null!;
        private bool _isLocalizationSubscribed;

        internal TMP_FontAsset Font => _titleDisplayer.font;

        private void Awake()
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

        private void OnLanguageChanged(object? sender, Language language)
        {
            UpdateLocalizedText();
        }

        private void UpdateLocalizedText()
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

        private static float GetHorizontalPosition(float distance, float absDistance)
        {
            if (absDistance <= 1f)
            {
                return X_POS_WITH_DELTA_1 * distance;
            }

            return Mathf.Sign(distance) * (X_POS_WITH_DELTA_1 + (X_POS_STEP * (absDistance - 1f)));
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
