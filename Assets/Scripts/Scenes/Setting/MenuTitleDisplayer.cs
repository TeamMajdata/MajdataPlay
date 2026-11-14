using MajdataPlay.i18n;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Scenes.Setting
{
    /// <summary>Animates one category body, its title and optional icon without creating runtime UI.</summary>
    public sealed class MenuTitleDisplayer : MonoBehaviour
    {
        /// <summary>Separates adjacent unselected category titles outside the central selected interval.</summary>
        private const float UnselectedTitleSpacing = 164f;

        /// <summary>Separates the selected title from its nearest neighboring category.</summary>
        private const float SelectedTitleSpacing = 218f;

        /// <summary>Reserves the first and last fifth of a category transition for its navigation arrows.</summary>
        private const float NavigationFadeFraction = 0.2f;

        /// <summary>Pauses a selected overflowing title before scrolling and at either end.</summary>
        private const float TitleScrollPauseSeconds = 0.8f;

        /// <summary>Moves overflowing selected titles in authored UI units per second.</summary>
        private const float TitleScrollSpeed = 36f;

        /// <summary>Colors the same fixed-cap body throughout selection changes.</summary>
        [SerializeField]
        [FormerlySerializedAs("Background")]
        private Image _background = null!;

        /// <summary>Defines the selected tint of the same background image.</summary>
        [SerializeField]
        [FormerlySerializedAs("SelectedBackgroundColor")]
        private Color _selectedBackgroundColor = Color.white;

        /// <summary>Defines the selected body width while preserving its authored height and end caps.</summary>
        [SerializeField]
        [FormerlySerializedAs("SelectedWidth")]
        private float _selectedWidth = 254.69f;

        /// <summary>Displays the localized setting title or bound collection name.</summary>
        [SerializeField]
        [FormerlySerializedAs("TitleDisplayer")]
        private TextMeshProUGUI _titleDisplayer = null!;

        /// <summary>Clips the authored title child independently of its scrolling and selection movement.</summary>
        [SerializeField]
        [FormerlySerializedAs("TitleViewport")]
        private RectTransform _titleViewport = null!;

        /// <summary>Leaves equal text padding on both sides of an unselected body.</summary>
        [SerializeField]
        [FormerlySerializedAs("UnselectedTitleInset")]
        private float _unselectedTitleInset = 4f;

        /// <summary>Reserves space for the navigation arrows on both sides of a selected title.</summary>
        [SerializeField]
        [FormerlySerializedAs("SelectedTitleInset")]
        private float _selectedTitleInset = 49.345f;

        /// <summary>Defines the title tint when this category is selected.</summary>
        [SerializeField]
        [FormerlySerializedAs("SelectedTextColor")]
        private Color _selectedTextColor = Color.white;

        /// <summary>Defines the selected font size while retaining the authored text transform.</summary>
        [SerializeField]
        [FormerlySerializedAs("SelectedFontSize")]
        private float _selectedFontSize = 32f;

        /// <summary>Optionally moves the List collection icon outside the selected body.</summary>
        [SerializeField]
        [FormerlySerializedAs("IconTransform")]
        private RectTransform? _iconTransform;

        /// <summary>Defines the icon's selected position in its authored parent coordinates.</summary>
        [SerializeField]
        [FormerlySerializedAs("SelectedIconPosition")]
        private Vector2 _selectedIconPosition = new(0f, 62f);

        /// <summary>Enlarges the existing icon dimensions as it rises above the selected body.</summary>
        [SerializeField]
        [FormerlySerializedAs("SelectedIconSizeMultiplier")]
        private float _selectedIconSizeMultiplier = 2f;

        /// <summary>Caches the category's authored root.</summary>
        private RectTransform _rectTransform = null!;

        /// <summary>Caches the single title child that moves inside the clipping viewport.</summary>
        private RectTransform _titleRectTransform = null!;

        /// <summary>Retains the unselected width and constant body height.</summary>
        private Vector2 _unselectedSize;

        /// <summary>Retains the unselected title viewport position for categories with icons.</summary>
        private Vector2 _unselectedViewportPosition;

        /// <summary>Retains the title's authored position before any scrolling.</summary>
        private Vector2 _titleOriginPosition;

        /// <summary>Retains the icon's inside-body position before selection.</summary>
        private Vector2 _unselectedIconPosition;

        /// <summary>Retains the icon's unselected dimensions for reversible resizing.</summary>
        private Vector2 _unselectedIconSize;

        /// <summary>Retains the unselected body tint.</summary>
        private Color _unselectedBackgroundColor;

        /// <summary>Retains the unselected title tint.</summary>
        private Color _unselectedTextColor;

        /// <summary>Retains the common unselected font size before animating selection.</summary>
        private float _unselectedFontSize;

        /// <summary>Provides the setting title for the current language, when applicable.</summary>
        private Func<string>? _localizedTitleProvider;

        /// <summary>Avoids reassigning unchanged localized text.</summary>
        private string? _displayedTitle;

        /// <summary>Applies title colors to its main and fallback renderers without recoloring glyph meshes.</summary>
        private SettingTextTint _textTint = null!;

        /// <summary>Tracks the language subscription across visibility and destruction.</summary>
        private bool _isLocalizationSubscribed;

        /// <summary>Tracks whether the current collection actually displays its optional icon.</summary>
        private bool _hasIcon;

        /// <summary>Defers layout refreshes caused by external RectTransform dimension changes.</summary>
        private bool _titleLayoutDirty = true;

        /// <summary>Caches how far an overflowing title must move to reveal its end.</summary>
        private float _titleOverflowWidth;

        /// <summary>Stores the title's current horizontal scrolling offset.</summary>
        private float _titleScrollOffset;

        /// <summary>Tracks the pause remaining before the next scrolling segment.</summary>
        private float _titleScrollPause = TitleScrollPauseSeconds;

        /// <summary>Chooses whether the next scrolling segment reveals the end or the beginning of the title.</summary>
        private bool _titleScrollTowardsEnd = true;

        /// <summary>The last selection blend applied to the body, title and icon.</summary>
        private float _lastSelectedAmount = float.NaN;

        /// <summary>Gets the authored title font for prewarming localized glyphs.</summary>
        internal TMP_FontAsset Font => _titleDisplayer.font;

        private void Awake()
        {
            _rectTransform = (RectTransform)transform;
            _titleRectTransform = _titleDisplayer.rectTransform;
            _unselectedSize = _rectTransform.rect.size;
            _unselectedViewportPosition = _titleViewport.anchoredPosition;
            _titleOriginPosition = _titleRectTransform.anchoredPosition;
            _unselectedBackgroundColor = _background.color;
            _unselectedTextColor = _titleDisplayer.color;
            _unselectedFontSize = _titleDisplayer.fontSize;
            _titleDisplayer.enableAutoSizing = false;
            _titleDisplayer.textWrappingMode = TextWrappingModes.NoWrap;
            _titleDisplayer.overflowMode = TextOverflowModes.Overflow;
            if (_iconTransform != null)
            {
                _unselectedIconPosition = _iconTransform.anchoredPosition;
                _unselectedIconSize = _iconTransform.sizeDelta;
            }
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
            RefreshTitle();
        }

        private void OnDisable()
        {
            UnsubscribeLocalization();
            ResetTitleScroll();
        }

        private void OnDestroy()
        {
            UnsubscribeLocalization();
            _textTint?.Dispose();
        }

        private void OnRectTransformDimensionsChange()
        {
            _titleLayoutDirty = true;
        }

        private void Update()
        {
            if (_titleLayoutDirty)
            {
                RefreshTitle();
            }
            if (_lastSelectedAmount < 1f || float.IsNaN(_lastSelectedAmount) || _titleOverflowWidth <= 0f)
            {
                return;
            }
            if (_titleScrollPause > 0f)
            {
                _titleScrollPause -= Time.unscaledDeltaTime;
                return;
            }
            var target = _titleScrollTowardsEnd ? -_titleOverflowWidth : 0f;
            _titleScrollOffset = Mathf.MoveTowards(_titleScrollOffset, target, TitleScrollSpeed * Time.unscaledDeltaTime);
            _titleRectTransform.anchoredPosition = _titleOriginPosition + new Vector2(_titleScrollOffset, 0f);
            if (_titleScrollOffset == target)
            {
                _titleScrollTowardsEnd = !_titleScrollTowardsEnd;
                _titleScrollPause = TitleScrollPauseSeconds;
            }
        }

        /// <summary>Shows the translation of a fixed localization key and follows language changes.</summary>
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

        /// <summary>Shows a computed localized title and recomputes it on language changes.</summary>
        /// <param name="localizedTitleProvider">Returns the localized title for the current language.</param>
        /// <exception cref="ArgumentNullException"><paramref name="localizedTitleProvider"/> is null.</exception>
        internal void Initialize(Func<string> localizedTitleProvider)
        {
            _localizedTitleProvider = localizedTitleProvider ?? throw new ArgumentNullException(nameof(localizedTitleProvider));
            UpdateLocalizedText();
        }

        /// <summary>Fits short titles centrally and restarts clipped long titles at their beginning after content or layout changes.</summary>
        internal void RefreshTitle()
        {
            var titleWidth = _titleDisplayer.GetPreferredValues(_titleDisplayer.text, Mathf.Infinity, Mathf.Infinity).x;
            _titleOverflowWidth = Mathf.Max(0f, titleWidth - _titleViewport.rect.width);
            _titleDisplayer.horizontalAlignment = _titleOverflowWidth > 0f
                ? HorizontalAlignmentOptions.Left : HorizontalAlignmentOptions.Center;
            _titleLayoutDirty = false;
            ResetTitleScroll();
        }

        /// <summary>Reserves the inside-body icon row only for collections that actually show an icon.</summary>
        /// <param name="hasIcon">Whether the collection's native icon image is enabled.</param>
        internal void SetHasIcon(bool hasIcon)
        {
            _hasIcon = hasIcon;
            ApplyTitleViewportLayout(float.IsNaN(_lastSelectedAmount) ? 0f : _lastSelectedAmount);
        }

        /// <summary>Moves and resizes the same body while blending its title and optional icon presentation.</summary>
        /// <param name="distance">The category index relative to the animated selection cursor.</param>
        internal void SetDistance(float distance)
        {
            var absDistance = Mathf.Abs(distance);
            var selectedAmount = 1f - Mathf.Clamp01(absDistance);
            _rectTransform.anchoredPosition = new Vector2(GetHorizontalPosition(distance, absDistance), 0f);
            if (_lastSelectedAmount == selectedAmount)
            {
                return;
            }
            _lastSelectedAmount = selectedAmount;
            _rectTransform.sizeDelta = new Vector2(Mathf.Lerp(_unselectedSize.x, _selectedWidth, selectedAmount), _unselectedSize.y);
            _background.color = Color.Lerp(_unselectedBackgroundColor, _selectedBackgroundColor, selectedAmount);
            _textTint.SetColor(Color.Lerp(_unselectedTextColor, _selectedTextColor, selectedAmount));
            _titleDisplayer.fontSize = Mathf.Lerp(_unselectedFontSize, _selectedFontSize, selectedAmount);
            ApplyTitleViewportLayout(selectedAmount);
            if (_iconTransform != null)
            {
                _iconTransform.anchoredPosition = Vector2.Lerp(_unselectedIconPosition, _selectedIconPosition, selectedAmount);
                _iconTransform.sizeDelta = _unselectedIconSize * Mathf.Lerp(1f, _selectedIconSizeMultiplier, selectedAmount);
            }
            RefreshTitle();
        }

        /// <summary>Activates or hides this authored category without creating display objects.</summary>
        /// <param name="isVisible">Whether this category belongs to the current visible window.</param>
        internal void SetVisible(bool isVisible)
        {
            if (gameObject.activeSelf != isVisible)
            {
                gameObject.SetActive(isVisible);
            }
        }

        /// <summary>Fades shared category arrows out before scrolling and back in as the selected title settles.</summary>
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

        /// <summary>Fits the authored title to the current body width while keeping scrolling and vertical movement independent.</summary>
        /// <param name="selectedAmount">The zero-to-one selection blend.</param>
        private void ApplyTitleViewportLayout(float selectedAmount)
        {
            var unselectedPosition = _hasIcon ? _unselectedViewportPosition : Vector2.zero;
            _titleViewport.anchoredPosition = Vector2.Lerp(unselectedPosition, Vector2.zero, selectedAmount);
            var inset = Mathf.Lerp(_unselectedTitleInset, _selectedTitleInset, selectedAmount);
            var width = Mathf.Max(0f, _rectTransform.rect.width - 2f * inset);
            _titleViewport.sizeDelta = new Vector2(width, _titleViewport.sizeDelta.y);
            _titleRectTransform.sizeDelta = new Vector2(width, _titleRectTransform.sizeDelta.y);
        }

        /// <summary>Restores the title's beginning and initial scrolling pause without changing typography or alignment.</summary>
        private void ResetTitleScroll()
        {
            _titleScrollOffset = 0f;
            _titleScrollTowardsEnd = true;
            _titleScrollPause = TitleScrollPauseSeconds;
            if (_titleRectTransform != null)
            {
                _titleRectTransform.anchoredPosition = _titleOriginPosition;
            }
        }

        /// <summary>Stops receiving language changes after this category is hidden or destroyed.</summary>
        private void UnsubscribeLocalization()
        {
            if (_isLocalizationSubscribed)
            {
                Localization.OnLanguageChanged -= OnLanguageChanged;
                _isLocalizationSubscribed = false;
            }
        }

        /// <summary>Refreshes the localized title when the active language changes.</summary>
        /// <param name="sender">The localization service publishing the change.</param>
        /// <param name="language">The newly selected language.</param>
        private void OnLanguageChanged(object? sender, Language language)
        {
            UpdateLocalizedText();
        }

        /// <summary>Publishes changed localized text, refreshes its layout and restarts scrolling at the beginning.</summary>
        private void UpdateLocalizedText()
        {
            if (_localizedTitleProvider is not null)
            {
                var title = _localizedTitleProvider();
                if (_displayedTitle != title)
                {
                    _titleDisplayer.text = title;
                    _displayedTitle = title;
                    RefreshTitle();
                }
            }
        }

        /// <summary>Maps the animated category distance onto the shared selected and neighboring spacing.</summary>
        /// <param name="distance">The signed category distance from the selection cursor.</param>
        /// <param name="absDistance">The absolute category distance.</param>
        /// <returns>The category's horizontal position in its authored parent.</returns>
        private static float GetHorizontalPosition(float distance, float absDistance)
        {
            if (absDistance <= 1f)
            {
                return SelectedTitleSpacing * distance;
            }
            return Mathf.Sign(distance) * (SelectedTitleSpacing + (UnselectedTitleSpacing * (absDistance - 1f)));
        }
    }
}
