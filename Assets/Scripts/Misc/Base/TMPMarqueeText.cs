using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MajdataPlay.Misc.Base
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class TMPMarqueeText : MonoBehaviour
    {
        public float StartDelay = 2f;
        public float ScrollSpeed = 48f;
        public float EndRatio = 0.25f;
        private const float RefreshRate = 60f;

        private TextMeshProUGUI? _sourceText;
        private TextMeshProUGUI? _mainText;
        private TextMeshProUGUI? _copyText;
        private RectTransform? _viewportRect;
        private RectTransform? _mainRect;
        private RectTransform? _copyRect;
        private RectMask2D? _mask;
        private string _lastText = "";
        private Vector2 _lastViewportSize;
        private float _loopDistance;
        private float _offset;
        private float _delayTimer;
        private float _movementAccumulator;
        private bool _isScrolling;
        private bool _isDirty = true;
        private bool _sourcePrepared;
        private bool _sourceWasEnabled;
        private bool _sourceWasAutoSizing;
        private bool _sourceWasWordWrapping;
        private TextOverflowModes _sourceOverflowMode;

        private void Awake()
        {
            if (!CacheComponents())
            {
                return;
            }

            Refresh(true);
        }

        private void OnEnable()
        {
            _isDirty = true;
            _delayTimer = StartDelay;
            _movementAccumulator = 0f;
            Refresh(true);
        }

        private void OnDisable()
        {
            RestoreSourceText();
        }

        private void OnDestroy()
        {
            RestoreSourceText();

            if (_mainText is not null)
            {
                Destroy(_mainText.gameObject);
            }

            if (_copyText is not null)
            {
                Destroy(_copyText.gameObject);
            }
        }

        private void OnRectTransformDimensionsChange()
        {
            _isDirty = true;
        }

        private void Update()
        {
            if (!CacheComponents())
            {
                return;
            }

            var sourceText = _sourceText!;
            var viewportSize = GetViewportSize();
            if (_isDirty || _lastText != sourceText.text || _lastViewportSize != viewportSize)
            {
                Refresh(_lastText != sourceText.text);
            }

            var deltaTime = Time.deltaTime;
            if (!_isScrolling)
            {
                return;
            }

            if (_delayTimer > 0f)
            {
                _delayTimer -= deltaTime;
                _movementAccumulator = 0f;
                return;
            }

            _movementAccumulator += deltaTime;
            var refreshInterval = 1f / RefreshRate;
            if (_movementAccumulator < refreshInterval)
            {
                return;
            }

            _offset += Mathf.Max(1f, ScrollSpeed) * _movementAccumulator;
            _movementAccumulator = 0f;
            if (_offset >= _loopDistance)
            {
                _offset = 0f;
                _delayTimer = Mathf.Max(0f, StartDelay);
            }

            ApplyOffset();
        }

        public void SetText(string value)
        {
            if (!CacheComponents())
            {
                return;
            }

            _sourceText!.text = value;
            Refresh(true);
        }

        private void Refresh(bool resetPosition)
        {
            if (!CacheComponents())
            {
                return;
            }

            var sourceText = _sourceText!;
            var viewportSize = GetViewportSize();
            var viewportWidth = viewportSize.x;
            if (viewportWidth <= 0f)
            {
                return;
            }

            var preferredWidth = Mathf.Ceil(sourceText.GetPreferredValues(sourceText.text, Mathf.Infinity, Mathf.Infinity).x);
            _lastText = sourceText.text;
            _lastViewportSize = viewportSize;
            _isScrolling = !string.IsNullOrEmpty(_lastText) && preferredWidth > viewportWidth + 0.5f;
            _isDirty = false;

            if (resetPosition)
            {
                _offset = 0f;
                _delayTimer = Mathf.Max(0f, StartDelay);
                _movementAccumulator = 0f;
            }

            if (!_isScrolling)
            {
                RestoreSourceText();
                return;
            }

            EnsureMask();
            EnsureMarqueeTexts();
            PrepareSourceText();
            SyncTextStyle(_mainText!);
            SyncTextStyle(_copyText!);

            _loopDistance = preferredWidth + viewportWidth * EndRatio;
            _mainText!.gameObject.SetActive(true);
            _copyText!.gameObject.SetActive(true);
            _mainText.text = _lastText;
            _copyText.text = _lastText;
            SetTextRect(_mainRect!, preferredWidth, viewportSize.y, 0f);
            SetTextRect(_copyRect!, preferredWidth, viewportSize.y, _loopDistance);
            ApplyOffset();
        }

        private bool CacheComponents()
        {
            _sourceText ??= GetComponent<TextMeshProUGUI>();

            _viewportRect ??= transform as RectTransform;

            return _sourceText is not null && _viewportRect is not null;
        }

        private void EnsureMask()
        {
            if (_viewportRect is null)
            {
                return;
            }

            _mask ??= _viewportRect.GetComponent<RectMask2D>();
            if (_mask is null)
            {
                _mask = gameObject.AddComponent<RectMask2D>();
            }
            _mask.enabled = true;
        }

        private void EnsureMarqueeTexts()
        {
            if (_mainText is null)
            {
                _mainText = CreateChildText("Marquee Text");
                _mainRect = (RectTransform)_mainText.transform;
            }

            if (_copyText is not null)
            {
                return;
            }

            _copyText = CreateChildText("Marquee Copy");
            _copyRect = (RectTransform)_copyText.transform;
        }

        private TextMeshProUGUI CreateChildText(string objectName)
        {
            var textObj = new GameObject(objectName, typeof(RectTransform));
            textObj.transform.SetParent(transform, false);

            var text = textObj.AddComponent<TextMeshProUGUI>();
            text.raycastTarget = false;
            PrepareText(text);
            return text;
        }

        private void PrepareSourceText()
        {
            if (_sourceText is null)
            {
                return;
            }

            if (!_sourcePrepared)
            {
                _sourceWasEnabled = _sourceText.enabled;
                _sourceWasAutoSizing = _sourceText.enableAutoSizing;
                _sourceWasWordWrapping = _sourceText.enableWordWrapping;
                _sourceOverflowMode = _sourceText.overflowMode;
                _sourcePrepared = true;
            }

            _sourceText.enabled = false;
            _sourceText.enableAutoSizing = false;
            _sourceText.enableWordWrapping = false;
            _sourceText.overflowMode = TextOverflowModes.Overflow;
        }

        private void RestoreSourceText()
        {
            if (_sourceText is not null && _sourcePrepared)
            {
                _sourceText.enabled = _sourceWasEnabled;
                _sourceText.enableAutoSizing = _sourceWasAutoSizing;
                _sourceText.enableWordWrapping = _sourceWasWordWrapping;
                _sourceText.overflowMode = _sourceOverflowMode;
                _sourcePrepared = false;
            }

            if (_mainText is not null)
            {
                _mainText.gameObject.SetActive(false);
            }

            if (_copyText is not null)
            {
                _copyText.gameObject.SetActive(false);
            }
            if (_mask is not null)
            {
                _mask.enabled = false;
            }
        }

        private void SyncTextStyle(TextMeshProUGUI target)
        {
            if (_sourceText is null)
            {
                return;
            }

            target.text = _sourceText.text;
            target.font = _sourceText.font;
            target.fontSharedMaterial = _sourceText.fontSharedMaterial;
            target.spriteAsset = _sourceText.spriteAsset;
            target.styleSheet = _sourceText.styleSheet;
            target.color = _sourceText.color;
            target.fontSize = _sourceText.fontSize;
            target.fontStyle = _sourceText.fontStyle;
            target.alignment = _sourceText.alignment;
            target.characterSpacing = _sourceText.characterSpacing;
            target.wordSpacing = _sourceText.wordSpacing;
            target.lineSpacing = _sourceText.lineSpacing;
            target.paragraphSpacing = _sourceText.paragraphSpacing;
            target.margin = _sourceText.margin;
            target.richText = _sourceText.richText;
            target.parseCtrlCharacters = _sourceText.parseCtrlCharacters;
            target.isRightToLeftText = _sourceText.isRightToLeftText;
            target.enableAutoSizing = false;
            PrepareText(target);
        }

        private static void PrepareText(TMP_Text text)
        {
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
        }

        private void ApplyOffset()
        {
            if (_mainRect is null || _copyRect is null)
            {
                return;
            }

            SetTextAnchoredX(_mainRect, -_offset);
            SetTextAnchoredX(_copyRect, _loopDistance - _offset);
        }

        private Vector2 GetViewportSize()
        {
            if (_viewportRect is null)
            {
                return Vector2.zero;
            }

            var rect = _viewportRect.rect;
            return new Vector2(rect.width, rect.height);
        }

        private static void SetTextRect(RectTransform rectTransform, float width, float height, float anchoredX)
        {
            rectTransform.anchorMin = new Vector2(0f, 0f);
            rectTransform.anchorMax = new Vector2(0f, 1f);
            rectTransform.pivot = new Vector2(0f, 0.5f);
            rectTransform.anchoredPosition = new Vector2(anchoredX, 0f);
            rectTransform.sizeDelta = new Vector2(width, 0f);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }

        private static void SetTextAnchoredX(RectTransform rectTransform, float anchoredX)
        {
            var anchoredPosition = rectTransform.anchoredPosition;
            anchoredPosition.x = anchoredX;
            rectTransform.anchoredPosition = anchoredPosition;
        }

    }
}
