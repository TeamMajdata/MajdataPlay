using System;
using System.Collections.Generic;
using Topten.RichTextKit;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TextAlignment = Topten.RichTextKit.TextAlignment;

namespace SkiaSharp.Unity.HB {

	[AddComponentMenu("Skia UI (Canvas)/HB InputField")]
	public class HBInputField : Selectable,
		IUpdateSelectedHandler, IPointerClickHandler, ISubmitHandler,
		IBeginDragHandler, IDragHandler, IEndDragHandler,
		IPointerDownHandler, IPointerUpHandler {

		public enum ContentType { Standard, Integer, Decimal, Alphanumeric, Name, EmailAddress, Password }
		public enum LineType { SingleLine, MultiLine }

		[SerializeField] HB_TEXTBlock textComponent;
		[SerializeField] HB_TEXTBlock placeholder;
		[SerializeField] RectTransform textViewport;
		[SerializeField] Image caretImage;

		[SerializeField, TextArea] string m_Text = "";
		[SerializeField] int characterLimit;
		[SerializeField] ContentType contentType = ContentType.Standard;
		[SerializeField] LineType lineType = LineType.SingleLine;
		[SerializeField] bool readOnly;
		[SerializeField] Color caretColor = new Color(0.2f, 0.2f, 0.2f, 1f);
		[SerializeField] float caretBlinkRate = 0.85f;
		[SerializeField] int caretWidth = 2;
		[SerializeField] bool hideCaret;
		[SerializeField] Color selectionColor = new Color(0.24f, 0.48f, 0.9f, 0.4f);
		[SerializeField] char asteriskChar = '*';

		// Text settings
		[SerializeField] UnityEngine.Object fontAsset;
		[SerializeField] int fontSize = 20;
		[SerializeField] Color fontColor = Color.black;
		[SerializeField] bool bold;
		[SerializeField] bool italic;
		[SerializeField] bool richText;
		[SerializeField] TextAlignment textAlignment = TextAlignment.Left;
		[SerializeField] TextDirection textDirection = TextDirection.Auto;
		[SerializeField] string placeholderText = "Enter text...";
		[SerializeField] Color placeholderColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);

		// Behavior
		[SerializeField] bool selectAllOnFocus;
		[SerializeField] bool tabNavigation = true;

		// Read-only appearance
		[SerializeField] Color readOnlyColor = new Color(0.85f, 0.85f, 0.85f, 1f);
		[SerializeField] Color readOnlyFontColor = new Color(0.5f, 0.5f, 0.5f, 1f);

		// Focus appearance
		[SerializeField] bool useFocusColor;
		[SerializeField] Color focusBackgroundColor = new Color(1f, 1f, 1f, 1f);

		// Skia background (optional — used instead of Image when assigned)
		[SerializeField] SkiaGraphic skiaBackground;

		// Mobile
		[SerializeField] bool hideMobileInput = true;

		// Auto resize
		[SerializeField] bool autoResize;
		[SerializeField] float minHeight = 40f;
		[SerializeField] float maxAutoHeight = 300f;

		[Serializable] public class SubmitEvent : UnityEvent<string> { }
		[Serializable] public class ValueChangedEvent : UnityEvent<string> { }

		public ValueChangedEvent onValueChanged = new ValueChangedEvent();
		public SubmitEvent onSubmit = new SubmitEvent();
		public SubmitEvent onEndEdit = new SubmitEvent();
		public UnityEvent onFocus = new UnityEvent();
		public UnityEvent onUnfocus = new UnityEvent();

		// Runtime state
		private int m_CaretPosition;
		private int m_SelectionAnchor;
		private bool m_HasFocus;
		private float m_BlinkTimer;
		private bool m_CaretVisible;
		private bool m_DragSelecting;
		private RectTransform m_CaretRect;
		private Image m_SelectionHighlight; // kept for single-line fallback
		private readonly List<Image> m_SelectionRects = new List<Image>();
		private Transform m_SelectionContainer;
		private TouchScreenKeyboard m_Keyboard;
		private Image m_BackgroundImage;
		private Color m_OriginalBackgroundColor;
		private Vector2 m_ScrollOffset;
		private RectTransform m_TextRT;
		private RectTransform m_PlaceholderRT;
		private bool m_UpdatingDisplay;
		private string m_PrevKeyboardText;

		// Long-press & context menu
		private float m_PointerDownTime;
		private bool m_LongPressTriggered;
		private bool m_PointerIsDown;
		private Vector2 m_PointerDownPos;
		private GameObject m_ContextMenu;
		private const float LongPressDuration = 0.5f;
		private const float LongPressMoveThreshold = 10f;

		// Trackpad caret dragging
		private bool m_TrackpadMode;
		private float m_TrackpadAccumX;
		private float m_TrackpadAccumY;
		private Vector2 m_LastDragPos;
		[SerializeField] float trackpadSensitivity = 8f;

		// Public API
		public string text {
			get => m_Text;
			set {
				if (m_Text == value) return;
				m_Text = value ?? "";
				if (characterLimit > 0 && m_Text.Length > characterLimit)
					m_Text = m_Text.Substring(0, characterLimit);
				m_CaretPosition = SnapToClusterBoundary(m_Text, Mathf.Min(m_CaretPosition, m_Text.Length));
				m_SelectionAnchor = m_CaretPosition;
				UpdateDisplay();
				onValueChanged?.Invoke(m_Text);
			}
		}

		public int caretPosition {
			get => m_CaretPosition;
			set {
				m_CaretPosition = Mathf.Clamp(value, 0, m_Text.Length);
				m_SelectionAnchor = m_CaretPosition;
				RefreshCaretView();
			}
		}

		public bool isFocused => m_HasFocus;

		public HB_TEXTBlock TextComponent => textComponent;
		public HB_TEXTBlock Placeholder => placeholder;

		// ===================== Lifecycle =====================

		protected override void Awake() {
			base.Awake();

			if (caretImage != null) {
				m_CaretRect = caretImage.GetComponent<RectTransform>();
				caretImage.color = caretColor;
				caretImage.gameObject.SetActive(false);
			}

			m_BackgroundImage = GetComponent<Image>();
			if (skiaBackground != null)
				m_OriginalBackgroundColor = skiaBackground.FillColor;
			else if (m_BackgroundImage != null)
				m_OriginalBackgroundColor = m_BackgroundImage.color;

			if (textComponent != null)
				m_TextRT = textComponent.GetComponent<RectTransform>();
			if (placeholder != null)
				m_PlaceholderRT = placeholder.GetComponent<RectTransform>();

			CreateSelectionHighlight();
			ApplyReadOnlyAppearance();
		}

		protected override void Start() {
			base.Start();
			// Input field controls sizing — prevent text component from resizing itself
			if (textComponent != null) {
				textComponent.AutoFitVertical = false;
				textComponent.AutoFitHorizontal = false;
			}
			if (placeholder != null) {
				placeholder.AutoFitVertical = false;
				placeholder.AutoFitHorizontal = false;
			}
			SyncTextSettings();
			SyncMaxWidthToRect();
			UpdateDisplay();
		}

		protected override void OnEnable() {
			base.OnEnable();
			UpdateDisplay();
		}

		protected override void OnDisable() {
			base.OnDisable();
			Deactivate();
		}

#if UNITY_EDITOR
		private void OnValidate() {
			if (textComponent != null) {
				SyncTextSettings();
				UpdateDisplay();
			}
		}
#endif

		private void Update() {
			CheckLongPress();
		}

		protected override void OnRectTransformDimensionsChange() {
			base.OnRectTransformDimensionsChange();
			if (m_UpdatingDisplay) return;
			SyncMaxWidthToRect();
		}

		private void SyncMaxWidthToRect() {
			if (textComponent == null) return;
			var rt = m_TextRT != null ? m_TextRT : textComponent.GetComponent<RectTransform>();
			if (rt == null) return;

			float viewportW = rt.rect.width;
			if (viewportW <= 0) return;

			if (lineType == LineType.SingleLine) {
				// Single-line: don't wrap — use large maxWidth so text extends freely
				float largeW = Mathf.Max(viewportW, 10000f);
				if (Mathf.Abs(textComponent.MaxWidth - largeW) > 0.5f)
					textComponent.MaxWidth = largeW;
			} else {
				// Multiline: wrap at viewport width
				if (Mathf.Abs(textComponent.MaxWidth - viewportW) > 0.5f)
					textComponent.MaxWidth = viewportW;
			}

			// Placeholder always wraps at viewport width
			if (placeholder != null) {
				var prt = m_PlaceholderRT != null ? m_PlaceholderRT : placeholder.GetComponent<RectTransform>();
				if (prt != null) {
					float pw = prt.rect.width;
					if (pw > 0 && Mathf.Abs(placeholder.MaxWidth - pw) > 0.5f)
						placeholder.MaxWidth = pw;
				}
			}
		}

		private void SyncTextSettings() {
			if (textComponent != null) {
				if (fontAsset != null) textComponent.Font = fontAsset;
				textComponent.FontSize = fontSize;
				textComponent.FontColor = readOnly ? readOnlyFontColor : fontColor;
				textComponent.Bold = bold;
				textComponent.Italic = italic;
				textComponent.TextAlignment = textAlignment;
				textComponent.TextDir = textDirection;
			}
			if (placeholder != null) {
				if (fontAsset != null) placeholder.Font = fontAsset;
				placeholder.FontSize = fontSize;
				placeholder.FontColor = placeholderColor;
				placeholder.Bold = false;
				placeholder.Italic = italic;
				placeholder.TextAlignment = textAlignment;
				placeholder.TextDir = textDirection;
				placeholder.text = placeholderText;
			}
			ApplyReadOnlyAppearance();
		}

		private void ApplyReadOnlyAppearance() {
			if (skiaBackground != null) {
				if (readOnly)
					skiaBackground.FillColor = readOnlyColor;
				else if (!m_HasFocus || !useFocusColor)
					skiaBackground.FillColor = m_OriginalBackgroundColor;
			} else if (m_BackgroundImage != null) {
				if (readOnly)
					m_BackgroundImage.color = readOnlyColor;
				else if (!m_HasFocus || !useFocusColor)
					m_BackgroundImage.color = m_OriginalBackgroundColor;
			}
		}

		private void AutoResizeHeight() {
			if (!autoResize || lineType != LineType.MultiLine) return;
			if (textComponent == null || textComponent.Info == null) return;

			var rootRT = GetComponent<RectTransform>();
			if (rootRT == null) return;

			// Get measured text height + viewport padding
			float textHeight = textComponent.Info.MeasuredHeight;
			// Account for viewport padding (offsetMin.y + abs(offsetMax.y))
			float padding = 12f; // default 6 top + 6 bottom from creation
			if (textViewport != null) {
				padding = textViewport.offsetMin.y + Mathf.Abs(textViewport.offsetMax.y);
			}

			float desiredHeight = Mathf.Clamp(textHeight + padding, minHeight, maxAutoHeight);
			if (Mathf.Abs(rootRT.sizeDelta.y - desiredHeight) > 0.5f) {
				rootRT.sizeDelta = new Vector2(rootRT.sizeDelta.x, desiredHeight);
			}
		}

		private void ScrollToCaret() {
			if (textComponent == null || textViewport == null) return;
			if (!textComponent.GetCaretLocalRect(m_CaretPosition, out Rect caretRect)) return;

			var textRT = m_TextRT != null ? m_TextRT : textComponent.GetComponent<RectTransform>();
			Rect viewRect = textViewport.rect;

			// Caret position in viewport space = caret local pos + current scroll offset
			float caretXInView = caretRect.x + m_ScrollOffset.x;
			float caretYInView = caretRect.y + m_ScrollOffset.y;
			float caretTop = caretYInView + caretRect.height;

			float margin = 4f;

			// Horizontal scrolling
			if (caretXInView < viewRect.xMin + margin) {
				m_ScrollOffset.x += (viewRect.xMin + margin) - caretXInView;
			} else if (caretXInView + caretWidth > viewRect.xMax - margin) {
				m_ScrollOffset.x -= (caretXInView + caretWidth) - (viewRect.xMax - margin);
			}

			// Vertical scrolling (multiline)
			if (lineType == LineType.MultiLine) {
				if (caretYInView < viewRect.yMin + margin) {
					m_ScrollOffset.y += (viewRect.yMin + margin) - caretYInView;
				} else if (caretTop > viewRect.yMax - margin) {
					m_ScrollOffset.y -= caretTop - (viewRect.yMax - margin);
				}
			}

			// Clamp: don't scroll past the beginning
			if (m_ScrollOffset.x > 0) m_ScrollOffset.x = 0;
			if (m_ScrollOffset.y < 0 && lineType == LineType.MultiLine) {
				// For multiline, don't scroll down past content
			}
			if (lineType == LineType.SingleLine) m_ScrollOffset.y = 0;

			ApplyScrollOffset();
		}

		private void ApplyScrollOffset() {
			if (m_TextRT == null) return;

			var newMin = new Vector2(m_ScrollOffset.x,
				lineType == LineType.MultiLine ? m_ScrollOffset.y : m_TextRT.offsetMin.y);
			var newMax = new Vector2(m_ScrollOffset.x,
				lineType == LineType.MultiLine ? m_ScrollOffset.y : m_TextRT.offsetMax.y);

			// Only set if changed to avoid triggering layout rebuild
			if (m_TextRT.offsetMin != newMin) m_TextRT.offsetMin = newMin;
			if (m_TextRT.offsetMax != newMax) m_TextRT.offsetMax = newMax;
		}

		private void ResetScroll() {
			m_ScrollOffset = Vector2.zero;
			if (m_TextRT != null) {
				m_TextRT.offsetMin = Vector2.zero;
				m_TextRT.offsetMax = Vector2.zero;
			}
		}

		// ===================== Focus =====================

		public override void OnSelect(BaseEventData eventData) {
			base.OnSelect(eventData);
			Activate();
		}

		public override void OnDeselect(BaseEventData eventData) {
			if (m_ContextMenu != null) {
				// Context menu is open — don't deactivate, re-select on next frame
				StartCoroutine(ReselectAfterContextMenu());
				return;
			}
			base.OnDeselect(eventData);
			Deactivate();
		}

		private System.Collections.IEnumerator ReselectAfterContextMenu() {
			yield return null;
			if (EventSystem.current != null)
				EventSystem.current.SetSelectedGameObject(gameObject);
		}

		private void Activate() {
			if (m_HasFocus) return;
			m_HasFocus = true;
			m_BlinkTimer = 0f;
			m_CaretVisible = true;

			if (caretImage != null && !hideCaret)
				caretImage.gameObject.SetActive(true);

			// Focus background color
			if (useFocusColor && !readOnly) {
				if (skiaBackground != null)
					skiaBackground.FillColor = focusBackgroundColor;
				else if (m_BackgroundImage != null)
					m_BackgroundImage.color = focusBackgroundColor;
			}

			// Open native keyboard on mobile platforms
			if (TouchScreenKeyboard.isSupported && !readOnly) {
				if (hideMobileInput)
					TouchScreenKeyboard.hideInput = true;

				TouchScreenKeyboardType keyboardType;
				switch (contentType) {
					case ContentType.Integer: keyboardType = TouchScreenKeyboardType.NumberPad; break;
					case ContentType.Decimal: keyboardType = TouchScreenKeyboardType.DecimalPad; break;
					case ContentType.EmailAddress: keyboardType = TouchScreenKeyboardType.EmailAddress; break;
					default: keyboardType = TouchScreenKeyboardType.Default; break;
				}
				bool secure = contentType == ContentType.Password;
				bool multiline = lineType == LineType.MultiLine;
				m_Keyboard = TouchScreenKeyboard.Open(m_Text, keyboardType, true, multiline, secure);
				m_PrevKeyboardText = m_Text;
			}

			if (selectAllOnFocus && m_Text.Length > 0) {
				m_SelectionAnchor = 0;
				m_CaretPosition = m_Text.Length;
			}

			RefreshCaretView();
			onFocus?.Invoke();
		}

		private void Deactivate() {
			if (!m_HasFocus) return;
			m_HasFocus = false;
			DismissContextMenu();
			if (caretImage != null) caretImage.gameObject.SetActive(false);
			if (m_SelectionContainer != null) m_SelectionContainer.gameObject.SetActive(false);
			if (m_Keyboard != null) {
				m_Keyboard.active = false;
				m_Keyboard = null;
			}

			// Reset scroll and restore background
			ResetScroll();
			if (skiaBackground != null) {
				skiaBackground.FillColor = readOnly ? readOnlyColor : m_OriginalBackgroundColor;
			} else if (m_BackgroundImage != null) {
				m_BackgroundImage.color = readOnly ? readOnlyColor : m_OriginalBackgroundColor;
			}

			onEndEdit?.Invoke(m_Text);
			onUnfocus?.Invoke();
		}

		// ===================== Input Processing =====================

		public void OnUpdateSelected(BaseEventData eventData) {
			if (!m_HasFocus) return;

			// Blink caret (works for both desktop and mobile)
			if (!hideCaret) {
				m_BlinkTimer += Time.unscaledDeltaTime;
				if (caretBlinkRate > 0 && m_BlinkTimer >= caretBlinkRate) {
					m_BlinkTimer = 0f;
					m_CaretVisible = !m_CaretVisible;
					if (caretImage != null)
						caretImage.enabled = m_CaretVisible && !HasSelection;
				}
			}

			// Mobile keyboard
			if (m_Keyboard != null) {
				ProcessTouchKeyboard();
				eventData.Use();
				return;
			}

			// Process keyboard events using Event.PopEvent (works in EventSystem callbacks)
			bool changed = false;
			Event evt = new Event();
			while (Event.PopEvent(evt)) {
				if (evt.rawType == EventType.KeyDown) {
					changed |= ProcessKeyEvent(evt);

					// Handle character input from key events
					char c = evt.character;
					if (c != 0 && c != '\b' && c != 127 && c != '\t' && !readOnly) {
						if (c == '\n' || c == '\r') {
							if (lineType == LineType.MultiLine && !evt.control && !evt.command) {
								changed |= InsertChar('\n');
							}
						} else {
							changed |= InsertChar(c);
						}
					}
				}
			}

			if (changed) {
				UpdateDisplay();
				onValueChanged?.Invoke(m_Text);
			}

			eventData.Use();
		}

		private void ProcessTouchKeyboard() {
			if (m_Keyboard == null) return;

			string kbText = m_Keyboard.text;

			// Skip if keyboard text hasn't changed since we last processed it
			if (kbText == m_PrevKeyboardText) {
				if (m_Keyboard.status == TouchScreenKeyboard.Status.Done) {
					onSubmit?.Invoke(m_Text);
					Deactivate();
				} else if (m_Keyboard.status == TouchScreenKeyboard.Status.Canceled) {
					Deactivate();
				}
				return;
			}

			string prevKb = m_PrevKeyboardText ?? m_Text;
			m_PrevKeyboardText = kbText;

			// Keyboard text changed — figure out what it did and apply at OUR caret
			int prevLen = prevKb.Length;
			int newLen = kbText.Length;
			bool changed = false;

			if (newLen < prevLen) {
				// Deletion: keyboard removed characters (backspace)
				// Delete entire grapheme cluster, not just the chars the keyboard removed
				if (HasSelection) {
					DeleteSelection();
					changed = true;
				} else if (m_CaretPosition > 0) {
					int clusterStart = PrevCluster(m_Text, m_CaretPosition);
					int clusterLen = m_CaretPosition - clusterStart;
					if (clusterLen > 0) {
						m_Text = m_Text.Remove(clusterStart, clusterLen);
						m_CaretPosition = clusterStart;
						changed = true;
					}
				}
			} else if (newLen > prevLen) {
				// Insertion: keyboard added characters
				// Find what was inserted by diffing prevKb vs kbText
				int prefixLen = 0;
				int minLen = Mathf.Min(prevLen, newLen);
				while (prefixLen < minLen && prevKb[prefixLen] == kbText[prefixLen])
					prefixLen++;
				int insertedCount = newLen - prevLen;
				string inserted = kbText.Substring(prefixLen, insertedCount);

				// Validate inserted characters
				if (contentType != ContentType.Standard && contentType != ContentType.Password) {
					string valid = "";
					foreach (char c in inserted) {
						if (ValidateChar(c)) valid += c;
					}
					inserted = valid;
				}
				if (characterLimit > 0) {
					int space = characterLimit - m_Text.Length;
					if (inserted.Length > space)
						inserted = inserted.Substring(0, Mathf.Max(0, space));
				}

				if (inserted.Length > 0) {
					if (HasSelection) DeleteSelection();
					m_Text = m_Text.Insert(m_CaretPosition, inserted);
					m_CaretPosition += inserted.Length;
					changed = true;
				}
			} else {
				// Same length — replacement (e.g. autocorrect)
				m_CaretPosition = ComputeCaretFromDiff(m_Text, kbText);
				m_Text = kbText;
				changed = true;
			}

			if (changed) {
				m_SelectionAnchor = m_CaretPosition;
				// Sync our corrected text back to the keyboard
				if (m_Keyboard.text != m_Text) {
					m_Keyboard.text = m_Text;
					m_PrevKeyboardText = m_Text;
				}
				UpdateDisplay();
				onValueChanged?.Invoke(m_Text);
			}

			if (m_Keyboard.status == TouchScreenKeyboard.Status.Done) {
				onSubmit?.Invoke(m_Text);
				Deactivate();
			} else if (m_Keyboard.status == TouchScreenKeyboard.Status.Canceled) {
				Deactivate();
			}
		}

		private int ComputeCaretFromDiff(string oldText, string newText) {
			int oldLen = oldText.Length;
			int newLen = newText.Length;

			// Find common prefix
			int prefixLen = 0;
			int minLen = Mathf.Min(oldLen, newLen);
			while (prefixLen < minLen && oldText[prefixLen] == newText[prefixLen])
				prefixLen++;

			// Find common suffix
			int suffixLen = 0;
			while (suffixLen < (minLen - prefixLen)
				   && oldText[oldLen - 1 - suffixLen] == newText[newLen - 1 - suffixLen])
				suffixLen++;

			// Caret goes right after the inserted/changed region
			int insertedLen = newLen - prefixLen - suffixLen;
			return Mathf.Clamp(prefixLen + Mathf.Max(0, insertedLen), 0, newLen);
		}

		private void SyncKeyboardSelection() {
			if (m_Keyboard == null || !m_Keyboard.active) return;

			if (HasSelection) {
				int start = Mathf.Min(m_CaretPosition, m_SelectionAnchor);
				int length = Mathf.Abs(m_CaretPosition - m_SelectionAnchor);
				m_Keyboard.selection = new RangeInt(start, length);
			} else {
				m_Keyboard.selection = new RangeInt(m_CaretPosition, 0);
			}
		}

		private bool ProcessKeyEvent(Event evt) {
			bool ctrl = evt.control || evt.command;
			bool shift = evt.shift;
			KeyCode kc = evt.keyCode;

			// Backspace (keyCode or character '\b')
			if (kc == KeyCode.Backspace || evt.character == '\b') {
				if (readOnly) return false;
				return Backspace();
			}

			// Delete (keyCode or character 127)
			if (kc == KeyCode.Delete || evt.character == 127) {
				if (readOnly) return false;
				return Delete();
			}

			// Navigation
			if (kc == KeyCode.LeftArrow) { MoveCaretLeft(shift, ctrl); return false; }
			if (kc == KeyCode.RightArrow) { MoveCaretRight(shift, ctrl); return false; }
			if (kc == KeyCode.UpArrow && lineType == LineType.MultiLine) { MoveCaretUp(shift); return false; }
			if (kc == KeyCode.DownArrow && lineType == LineType.MultiLine) { MoveCaretDown(shift); return false; }
			if (kc == KeyCode.Home) { MoveToStart(shift); return false; }
			if (kc == KeyCode.End) { MoveToEnd(shift); return false; }

			// Submit / newline
			if (kc == KeyCode.Return || kc == KeyCode.KeypadEnter) {
				// Handled by character input for multiline, submit for single line
				if (lineType != LineType.MultiLine || ctrl) {
					onSubmit?.Invoke(m_Text);
					Deactivate();
				}
				return false; // char input handles the actual '\n' insertion
			}

			if (kc == KeyCode.Escape) { Deactivate(); return false; }

			// Tab navigation
			if (kc == KeyCode.Tab && tabNavigation) {
				Selectable next = shift ? FindSelectableOnUp() ?? FindSelectableOnLeft()
					: FindSelectableOnDown() ?? FindSelectableOnRight();
				if (next != null) {
					Deactivate();
					EventSystem.current.SetSelectedGameObject(next.gameObject);
				}
				return false;
			}

			// Shortcuts
			if (ctrl) {
				if (kc == KeyCode.A) { SelectAll(); return false; }
				if (kc == KeyCode.C) { CopyToClipboard(); return false; }
				if (kc == KeyCode.V && !readOnly) return Paste();
				if (kc == KeyCode.X && !readOnly) { CopyToClipboard(); return DeleteSelection(); }
			}

			return false;
		}

		// ===================== Text Editing =====================

		private bool InsertChar(char c) {
			if (!ValidateChar(c)) return false;
			DismissContextMenu();

			DeleteSelection();

			if (characterLimit > 0 && m_Text.Length >= characterLimit) return false;

			m_Text = m_Text.Insert(m_CaretPosition, c.ToString());
			m_CaretPosition++;
			m_SelectionAnchor = m_CaretPosition;
			ResetBlink();
			return true;
		}

		private bool Backspace() {
			if (HasSelection) return DeleteSelection();
			if (m_CaretPosition <= 0) return false;

			int clusterStart = PrevCluster(m_Text, m_CaretPosition);
			int removeCount = m_CaretPosition - clusterStart;
			m_Text = m_Text.Remove(clusterStart, removeCount);
			m_CaretPosition = clusterStart;
			m_SelectionAnchor = m_CaretPosition;
			ResetBlink();
			return true;
		}

		private bool Delete() {
			if (HasSelection) return DeleteSelection();
			if (m_CaretPosition >= m_Text.Length) return false;

			int clusterEnd = NextCluster(m_Text, m_CaretPosition);
			int removeCount = clusterEnd - m_CaretPosition;
			m_Text = m_Text.Remove(m_CaretPosition, removeCount);
			ResetBlink();
			return true;
		}

		private bool DeleteSelection() {
			if (!HasSelection) return false;

			int start = Mathf.Min(m_CaretPosition, m_SelectionAnchor);
			int end = Mathf.Max(m_CaretPosition, m_SelectionAnchor);
			m_Text = m_Text.Remove(start, end - start);
			m_CaretPosition = start;
			m_SelectionAnchor = start;
			ResetBlink();
			return true;
		}

		private bool Paste() {
			string clipboard = GetClipboard();
			if (string.IsNullOrEmpty(clipboard)) return false;

			DeleteSelection();

			if (lineType == LineType.SingleLine)
				clipboard = clipboard.Replace("\n", "").Replace("\r", "");

			// Validate each character
			string validated = "";
			foreach (char c in clipboard) {
				if (ValidateChar(c)) validated += c;
			}

			if (characterLimit > 0) {
				int space = characterLimit - m_Text.Length;
				if (space <= 0) return false;
				if (validated.Length > space) validated = validated.Substring(0, space);
			}

			m_Text = m_Text.Insert(m_CaretPosition, validated);
			m_CaretPosition += validated.Length;
			m_SelectionAnchor = m_CaretPosition;
			ResetBlink();
			return true;
		}

		// ===================== Navigation =====================

		/// <summary>
		/// Moves the caret visually left. In RTL text this moves forward in the string,
		/// in LTR text this moves backward. Uses hit-testing for correct bidi behavior.
		/// </summary>
		private void MoveCaretLeft(bool shift, bool ctrl) {
			int newPos = m_CaretPosition;
			if (ctrl) {
				newPos = FindPreviousWordStart(m_CaretPosition);
			} else if (!shift && HasSelection) {
				newPos = Mathf.Min(m_CaretPosition, m_SelectionAnchor);
			} else {
				newPos = GetVisualNeighbor(m_CaretPosition, -1);
			}
			m_CaretPosition = newPos;
			if (!shift) m_SelectionAnchor = m_CaretPosition;
			ResetBlink();
			RefreshCaretView();
		}

		/// <summary>
		/// Moves the caret visually right. Uses hit-testing for correct bidi behavior.
		/// </summary>
		private void MoveCaretRight(bool shift, bool ctrl) {
			int newPos = m_CaretPosition;
			if (ctrl) {
				newPos = FindNextWordEnd(m_CaretPosition);
			} else if (!shift && HasSelection) {
				newPos = Mathf.Max(m_CaretPosition, m_SelectionAnchor);
			} else {
				newPos = GetVisualNeighbor(m_CaretPosition, 1);
			}
			m_CaretPosition = newPos;
			if (!shift) m_SelectionAnchor = m_CaretPosition;
			ResetBlink();
			RefreshCaretView();
		}

		/// <summary>
		/// Returns the code point index of the character visually to the left (dir=-1) or right (dir=1).
		/// Checks both logical neighbors (index±1) and picks the one whose caret X is in the desired
		/// visual direction. Works correctly for RTL, LTR, and mixed bidi text.
		/// </summary>
		private int GetVisualNeighbor(int fromIndex, int visualDir) {
			if (textComponent == null || textComponent.Info == null || m_Text.Length == 0)
				return Mathf.Clamp(fromIndex + visualDir, 0, m_Text.Length);

			var info = textComponent.Info;
			var current = info.GetCaretInfo(new CaretPosition(fromIndex));
			if (current.IsNone)
				return Mathf.Clamp(fromIndex + visualDir, 0, m_Text.Length);

			float currentX = current.CaretXCoord;
			int bestIndex = -1;
			float bestDist = float.MaxValue;

			// Check previous grapheme cluster boundary
			int prevIdx = PrevCluster(m_Text, fromIndex);
			if (prevIdx != fromIndex) {
				var prev = info.GetCaretInfo(new CaretPosition(prevIdx));
				if (!prev.IsNone) {
					float dx = prev.CaretXCoord - currentX;
					if ((visualDir > 0 && dx > 0.1f) || (visualDir < 0 && dx < -0.1f)) {
						float dist = Mathf.Abs(dx);
						if (dist < bestDist) { bestDist = dist; bestIndex = prevIdx; }
					}
				}
			}

			// Check next grapheme cluster boundary
			int nextIdx = NextCluster(m_Text, fromIndex);
			if (nextIdx != fromIndex) {
				var next = info.GetCaretInfo(new CaretPosition(nextIdx));
				if (!next.IsNone) {
					float dx = next.CaretXCoord - currentX;
					if ((visualDir > 0 && dx > 0.1f) || (visualDir < 0 && dx < -0.1f)) {
						float dist = Mathf.Abs(dx);
						if (dist < bestDist) { bestDist = dist; bestIndex = nextIdx; }
					}
				}
			}

			// If found a visual neighbor, use it
			if (bestIndex >= 0) return bestIndex;

			// Fallback: at line boundaries, move by cluster
			if (visualDir > 0 && fromIndex < m_Text.Length) return NextCluster(m_Text, fromIndex);
			if (visualDir < 0 && fromIndex > 0) return PrevCluster(m_Text, fromIndex);
			return fromIndex;
		}

		private void MoveCaretUp(bool shift) {
			if (textComponent == null || textComponent.Info == null) return;
			var info = textComponent.Info;
			var current = info.GetCaretInfo(new Topten.RichTextKit.CaretPosition(m_CaretPosition));
			if (current.LineIndex <= 0) {
				m_CaretPosition = 0;
			} else {
				var result = info.HitTestLine(current.LineIndex - 1, current.CaretXCoord);
				m_CaretPosition = Mathf.Clamp(result.ClosestCodePointIndex, 0, m_Text.Length);
			}
			if (!shift) m_SelectionAnchor = m_CaretPosition;
			ResetBlink();
			RefreshCaretView();
		}

		private void MoveCaretDown(bool shift) {
			if (textComponent == null || textComponent.Info == null) return;
			var info = textComponent.Info;
			var current = info.GetCaretInfo(new Topten.RichTextKit.CaretPosition(m_CaretPosition));
			if (current.LineIndex >= info.LineCount - 1) {
				m_CaretPosition = m_Text.Length;
			} else {
				var result = info.HitTestLine(current.LineIndex + 1, current.CaretXCoord);
				m_CaretPosition = Mathf.Clamp(result.ClosestCodePointIndex, 0, m_Text.Length);
			}
			if (!shift) m_SelectionAnchor = m_CaretPosition;
			ResetBlink();
			RefreshCaretView();
		}

		private void MoveToStart(bool shift) {
			m_CaretPosition = 0;
			if (!shift) m_SelectionAnchor = 0;
			ResetBlink();
			RefreshCaretView();
		}

		private void MoveToEnd(bool shift) {
			m_CaretPosition = m_Text.Length;
			if (!shift) m_SelectionAnchor = m_Text.Length;
			ResetBlink();
			RefreshCaretView();
		}

		private void SelectAll() {
			m_SelectionAnchor = 0;
			m_CaretPosition = m_Text.Length;
			RefreshCaretView();
		}

		// ===================== Selection =====================

		private bool HasSelection => m_CaretPosition != m_SelectionAnchor;

		private static string s_InternalClipboard = "";

		private void CopyToClipboard() {
			if (!HasSelection) return;
			if (contentType == ContentType.Password) return;
			int start = Mathf.Min(m_CaretPosition, m_SelectionAnchor);
			int end = Mathf.Max(m_CaretPosition, m_SelectionAnchor);
			string selected = m_Text.Substring(start, end - start);
			s_InternalClipboard = selected;
			GUIUtility.systemCopyBuffer = selected;
		}

		private static string GetClipboard() {
			string sys = GUIUtility.systemCopyBuffer;
			return !string.IsNullOrEmpty(sys) ? sys : s_InternalClipboard;
		}

		// ===================== Click / Drag =====================

		private bool m_WasDragging;

		public void OnPointerClick(PointerEventData eventData) {
			if (eventData.button != PointerEventData.InputButton.Left) return;

			// Skip if we just finished a drag selection
			if (m_WasDragging) {
				m_WasDragging = false;
				return;
			}

			if (!m_HasFocus) {
				EventSystem.current.SetSelectedGameObject(gameObject);
			}

			// Position caret at click location
			PositionCaretFromPointer(eventData);

			if (eventData.clickCount == 2) {
				SelectWordAtCaret();
				ShowContextMenu(eventData.position);
			} else if (eventData.clickCount == 3) {
				SelectAll();
				ShowContextMenu(eventData.position);
			} else {
				// Single tap dismisses context menu
				DismissContextMenu();
			}
		}

		public void OnBeginDrag(PointerEventData eventData) {
			if (!m_HasFocus) return;

			DismissContextMenu();

			// If long-press already triggered → selection drag mode
			// Otherwise → trackpad caret mode
			if (m_LongPressTriggered) {
				m_DragSelecting = true;
				m_TrackpadMode = false;
				PositionCaretFromPointer(eventData);
				m_SelectionAnchor = m_CaretPosition;
			} else {
				m_TrackpadMode = true;
				m_DragSelecting = false;
				m_TrackpadAccumX = 0f;
				m_TrackpadAccumY = 0f;
				m_LastDragPos = eventData.position;
				// Clear any existing selection
				m_SelectionAnchor = m_CaretPosition;
				RefreshCaretView();
			}
		}

		public void OnDrag(PointerEventData eventData) {
			if (m_DragSelecting) {
				PositionCaretFromPointer(eventData, keepAnchor: true);
			} else if (m_TrackpadMode) {
				// Trackpad mode: move caret to finger position, no selection
				PositionCaretFromPointer(eventData, keepAnchor: false);
			}
		}

		public void OnEndDrag(PointerEventData eventData) {
			if (m_DragSelecting && HasSelection) {
				ShowContextMenu(eventData.position);
			}
			m_DragSelecting = false;
			m_TrackpadMode = false;
			m_WasDragging = true;
		}

		private void TrackpadDrag(PointerEventData eventData) {
			Vector2 delta = eventData.position - m_LastDragPos;
			m_LastDragPos = eventData.position;

			m_TrackpadAccumX += delta.x;
			m_TrackpadAccumY += delta.y;

			// Horizontal: move caret visually left/right (bidi-aware)
			while (Mathf.Abs(m_TrackpadAccumX) >= trackpadSensitivity) {
				if (m_TrackpadAccumX > 0) {
					m_CaretPosition = GetVisualNeighbor(m_CaretPosition, 1); // visually right
					m_TrackpadAccumX -= trackpadSensitivity;
				} else {
					m_CaretPosition = GetVisualNeighbor(m_CaretPosition, -1); // visually left
					m_TrackpadAccumX += trackpadSensitivity;
				}
			}

			// Vertical: move caret up/down lines (multiline only)
			if (lineType == LineType.MultiLine) {
				while (Mathf.Abs(m_TrackpadAccumY) >= trackpadSensitivity * 3f) {
					if (m_TrackpadAccumY < 0) {
						MoveCaretDown(false);
						m_TrackpadAccumY += trackpadSensitivity * 3f;
					} else {
						MoveCaretUp(false);
						m_TrackpadAccumY -= trackpadSensitivity * 3f;
					}
				}
			}

			m_SelectionAnchor = m_CaretPosition;
			ResetBlink();
			RefreshCaretView();
		}

		public void OnSubmit(BaseEventData eventData) {
			if (!m_HasFocus) {
				EventSystem.current.SetSelectedGameObject(gameObject);
			}
		}

		// ===================== Long Press & Context Menu =====================

		public override void OnPointerDown(PointerEventData eventData) {
			base.OnPointerDown(eventData);
			m_PointerIsDown = true;
			m_PointerDownTime = Time.unscaledTime;
			m_PointerDownPos = eventData.position;
			m_LongPressTriggered = false;
			DismissContextMenu();
		}

		public override void OnPointerUp(PointerEventData eventData) {
			base.OnPointerUp(eventData);
			m_PointerIsDown = false;
		}

		private void CheckLongPress() {
			if (!m_PointerIsDown || m_LongPressTriggered || m_DragSelecting) return;
			if (Time.unscaledTime - m_PointerDownTime < LongPressDuration) return;

			// Check finger hasn't moved too far
			Vector2 currentPos = Input.touchCount > 0
				? (Vector2)Input.GetTouch(0).position
				: (Vector2)Input.mousePosition;
			if (Vector2.Distance(currentPos, m_PointerDownPos) > LongPressMoveThreshold) return;

			m_LongPressTriggered = true;

			// Select word at touch position if no selection
			if (!HasSelection) {
				SelectWordAtCaret();
			}

			ShowContextMenu(currentPos);
		}

		private void ShowContextMenu(Vector2 screenPos) {
			DismissContextMenu();

			// Snapshot selection state NOW before anything can change it
			int savedCaretPos = m_CaretPosition;
			int savedAnchor = m_SelectionAnchor;
			string savedText = m_Text;
			bool hasSelection = savedCaretPos != savedAnchor;
			string selectedText = "";
			if (hasSelection) {
				int s = Mathf.Min(savedCaretPos, savedAnchor);
				int e = Mathf.Max(savedCaretPos, savedAnchor);
				selectedText = savedText.Substring(s, e - s);
			}

			Canvas rootCanvas = GetComponentInParent<Canvas>();
			if (rootCanvas == null) return;
			Canvas root = rootCanvas;
			while (root.transform.parent != null) {
				Canvas p = root.transform.parent.GetComponentInParent<Canvas>();
				if (p == null) break;
				root = p;
			}

			Camera cam = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;

			// Blocker to dismiss on outside tap
			var blockerGO = new GameObject("InputFieldContextMenu", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			blockerGO.transform.SetParent(root.transform, false);
			var blockerRT = blockerGO.GetComponent<RectTransform>();
			blockerRT.anchorMin = Vector2.zero;
			blockerRT.anchorMax = Vector2.one;
			blockerRT.offsetMin = Vector2.zero;
			blockerRT.offsetMax = Vector2.zero;
			blockerGO.GetComponent<Image>().color = Color.clear;
			AddClickTrigger(blockerGO, () => DismissContextMenu());
			m_ContextMenu = blockerGO;

			// Menu panel
			var panelGO = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			panelGO.transform.SetParent(blockerGO.transform, false);
			panelGO.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 0.95f);
			panelGO.GetComponent<Image>().raycastTarget = true;

			// Position above the input field
			var panelRT = panelGO.GetComponent<RectTransform>();
			RectTransform canvasRT = root.GetComponent<RectTransform>();

			Vector3[] corners = new Vector3[4];
			GetComponent<RectTransform>().GetWorldCorners(corners);
			Vector2 topCenterScreen = RectTransformUtility.WorldToScreenPoint(cam, (corners[1] + corners[2]) * 0.5f);
			RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, topCenterScreen, cam, out Vector2 localPos);

			panelRT.anchorMin = panelRT.anchorMax = new Vector2(0.5f, 0.5f);
			panelRT.pivot = new Vector2(0.5f, 0f);
			panelRT.anchoredPosition = new Vector2(localPos.x, localPos.y + 12f);

			// Shadow
			var shadow = panelGO.AddComponent<Shadow>();
			shadow.effectColor = new Color(0, 0, 0, 0.3f);
			shadow.effectDistance = new Vector2(0, -2);

			// Build actions using snapshot
			bool isPassword = contentType == ContentType.Password;
			var items = new List<(string label, System.Action action)>();

			Debug.Log($"[ContextMenu] hasSelection={hasSelection}, selectedText='{selectedText}', readOnly={readOnly}, clipboard='{GetClipboard()}'");

			if (hasSelection && !isPassword)
				items.Add(("Copy", () => {
					Debug.Log($"[ContextMenu] COPY: '{selectedText}'");
					s_InternalClipboard = selectedText;
					GUIUtility.systemCopyBuffer = selectedText;
					DismissContextMenu();
				}));

			if (hasSelection && !readOnly)
				items.Add(("Cut", () => {
					Debug.Log($"[ContextMenu] CUT: '{selectedText}', removing from pos {Mathf.Min(savedCaretPos, savedAnchor)} to {Mathf.Max(savedCaretPos, savedAnchor)}");
					s_InternalClipboard = selectedText;
					GUIUtility.systemCopyBuffer = selectedText;
					int s = Mathf.Min(savedCaretPos, savedAnchor);
					int e = Mathf.Max(savedCaretPos, savedAnchor);
					m_Text = m_Text.Remove(s, e - s);
					m_CaretPosition = s;
					m_SelectionAnchor = s;
					Debug.Log($"[ContextMenu] CUT result: '{m_Text}'");
					DismissContextMenu();
					UpdateDisplay();
					onValueChanged?.Invoke(m_Text);
				}));

			string clipContent = GetClipboard();
			if (!readOnly && !string.IsNullOrEmpty(clipContent))
				items.Add(("Paste", () => {
					string clip = GetClipboard();
					Debug.Log($"[ContextMenu] PASTE: clipboard='{clip}', caretPos={m_CaretPosition}");
					if (string.IsNullOrEmpty(clip)) { DismissContextMenu(); return; }
					if (hasSelection) {
						int s = Mathf.Min(m_CaretPosition, m_SelectionAnchor);
						int e = Mathf.Max(m_CaretPosition, m_SelectionAnchor);
						m_Text = m_Text.Remove(s, e - s);
						m_CaretPosition = s;
						m_SelectionAnchor = s;
					}
					if (lineType == LineType.SingleLine)
						clip = clip.Replace("\n", "").Replace("\r", "");
					if (characterLimit > 0) {
						int space = characterLimit - m_Text.Length;
						if (clip.Length > space) clip = clip.Substring(0, Mathf.Max(0, space));
					}
					m_Text = m_Text.Insert(m_CaretPosition, clip);
					m_CaretPosition += clip.Length;
					m_SelectionAnchor = m_CaretPosition;
					Debug.Log($"[ContextMenu] PASTE result: '{m_Text}'");
					DismissContextMenu();
					UpdateDisplay();
					onValueChanged?.Invoke(m_Text);
				}));

			if (!hasSelection && m_Text.Length > 0)
				items.Add(("Select All", () => {
					Debug.Log("[ContextMenu] SELECT ALL");
					DismissContextMenu();
					SelectAll();
					RefreshCaretView();
				}));

			Debug.Log($"[ContextMenu] Showing {items.Count} items: {string.Join(", ", items.ConvertAll(x => x.label))}");
			if (items.Count == 0) { DismissContextMenu(); return; }

			float btnWidth = 120f;
			float btnHeight = 60f;
			panelRT.sizeDelta = new Vector2(items.Count * btnWidth + 16, btnHeight + 16);

			for (int i = 0; i < items.Count; i++) {
				var item = items[i];
				var btnGO = new GameObject(item.label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
				btnGO.transform.SetParent(panelGO.transform, false);

				var btnRT = btnGO.GetComponent<RectTransform>();
				btnRT.anchorMin = new Vector2(0, 0.5f);
				btnRT.anchorMax = new Vector2(0, 0.5f);
				btnRT.pivot = new Vector2(0, 0.5f);
				btnRT.anchoredPosition = new Vector2(8 + i * btnWidth, 0);
				btnRT.sizeDelta = new Vector2(btnWidth, btnHeight);

				var btnImg = btnGO.GetComponent<Image>();
				btnImg.color = new Color(1, 1, 1, 0);
				btnImg.alphaHitTestMinimumThreshold = 0f;

				AddClickTrigger(btnGO, item.action);

				// Separator
				if (i > 0) {
					var sepGO = new GameObject("Sep", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
					sepGO.transform.SetParent(btnGO.transform, false);
					var sepRT = sepGO.GetComponent<RectTransform>();
					sepRT.anchorMin = new Vector2(0, 0.1f);
					sepRT.anchorMax = new Vector2(0, 0.9f);
					sepRT.pivot = new Vector2(0, 0.5f);
					sepRT.anchoredPosition = Vector2.zero;
					sepRT.sizeDelta = new Vector2(1, 0);
					sepGO.GetComponent<Image>().color = new Color(1, 1, 1, 0.2f);
					sepGO.GetComponent<Image>().raycastTarget = false;
				}

				// Label
				var labelGO = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
				labelGO.transform.SetParent(btnGO.transform, false);
				var labelRT = labelGO.GetComponent<RectTransform>();
				labelRT.anchorMin = Vector2.zero;
				labelRT.anchorMax = Vector2.one;
				labelRT.offsetMin = Vector2.zero;
				labelRT.offsetMax = Vector2.zero;
				labelGO.GetComponent<RawImage>().color = new Color(1, 1, 1, 0);
				labelGO.GetComponent<RawImage>().raycastTarget = false;

				var lbl = labelGO.AddComponent<HB_TEXTBlock>();
				lbl.text = item.label;
				lbl.FontSize = 22;
				lbl.FontColor = Color.white;
				lbl.TextAlignment = Topten.RichTextKit.TextAlignment.Center;
				lbl.VAlign = HB_TEXTBlock.VerticalAlignment.Middle;
			}

			// Flip below if no room above
			float canvasTop = canvasRT.rect.height * (1f - canvasRT.pivot.y);
			if (localPos.y + 12f + btnHeight + 16 > canvasTop) {
				Vector2 botScreen = RectTransformUtility.WorldToScreenPoint(cam, (corners[0] + corners[3]) * 0.5f);
				RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, botScreen, cam, out Vector2 localBot);
				panelRT.pivot = new Vector2(0.5f, 1f);
				panelRT.anchoredPosition = new Vector2(localBot.x, localBot.y - 12f);
			}
		}

		private static void AddClickTrigger(GameObject go, System.Action action) {
			var handler = go.AddComponent<SimpleClickHandler>();
			handler.action = action;
		}

		private void DismissContextMenu() {
			if (m_ContextMenu != null) {
				if (Application.isPlaying)
					Destroy(m_ContextMenu);
				else
					DestroyImmediate(m_ContextMenu);
				m_ContextMenu = null;

				// Re-select the input field so it stays focused
				if (m_HasFocus && EventSystem.current != null)
					EventSystem.current.SetSelectedGameObject(gameObject);
			}
		}

		private void PositionCaretFromPointer(PointerEventData eventData, bool keepAnchor = false) {
			if (textComponent == null) return;

			var rt = m_TextRT != null ? m_TextRT : textComponent.GetComponent<RectTransform>();
			if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
				rt, eventData.position, eventData.pressEventCamera, out Vector2 localPoint)) {
				int index = textComponent.HitTestLocal(localPoint);
				m_CaretPosition = SnapToClusterBoundary(m_Text, Mathf.Clamp(index, 0, m_Text.Length));
				if (!keepAnchor) m_SelectionAnchor = m_CaretPosition;
				ResetBlink();
				RefreshCaretView();
			}
		}

		private void SelectWordAtCaret() {
			if (m_Text.Length == 0) return;

			int start = m_CaretPosition;
			int end = m_CaretPosition;

			// Expand backward
			while (start > 0 && !char.IsWhiteSpace(m_Text[start - 1])) start--;
			// Expand forward
			while (end < m_Text.Length && !char.IsWhiteSpace(m_Text[end])) end++;

			m_SelectionAnchor = start;
			m_CaretPosition = end;
			RefreshCaretView();
		}

		// ===================== Display =====================

		private void UpdateDisplay() {
			if (m_UpdatingDisplay) return;
			m_UpdatingDisplay = true;

			try {
				string displayText = m_Text;
				if (contentType == ContentType.Password && !string.IsNullOrEmpty(displayText)) {
					displayText = new string(asteriskChar, displayText.Length);
				}

				if (textComponent != null) {
					if (richText)
						textComponent.SetRichText(displayText);
					else
						textComponent.text = displayText;
					// Flush layout so caret queries use updated text
					textComponent.FlushLayout();
				}

				if (placeholder != null) {
					placeholder.gameObject.SetActive(string.IsNullOrEmpty(m_Text));
				}

				AutoResizeHeight();
				UpdateCaretVisual();
				ScrollToCaret();
				UpdateSelectionVisual();
			} finally {
				m_UpdatingDisplay = false;
			}
		}

		private void UpdateCaretVisual() {
			if (caretImage == null || m_CaretRect == null || textComponent == null) return;
			if (!m_HasFocus || hideCaret) return;

			var textRT = textComponent.GetComponent<RectTransform>();
			Rect parentRect = textRT.rect;

			if (textComponent.GetCaretLocalRect(m_CaretPosition, out Rect localRect)) {
				m_CaretRect.SetParent(textRT, false);
				m_CaretRect.anchorMin = new Vector2(0, 0);
				m_CaretRect.anchorMax = new Vector2(0, 0);
				m_CaretRect.pivot = new Vector2(0, 0);
				// Convert from pivot-relative local coords to bottom-left anchor coords
				m_CaretRect.anchoredPosition = new Vector2(
					localRect.x - parentRect.xMin,
					localRect.y - parentRect.yMin
				);
				m_CaretRect.sizeDelta = new Vector2(caretWidth, localRect.height);
				caretImage.color = caretColor;
			} else if (m_Text.Length == 0) {
				m_CaretRect.SetParent(textRT, false);
				m_CaretRect.anchorMin = new Vector2(0, 0);
				m_CaretRect.anchorMax = new Vector2(0, 0);
				m_CaretRect.pivot = new Vector2(0, 0);
				// Place at top-left of text area
				m_CaretRect.anchoredPosition = new Vector2(4f, parentRect.height - 4f - textComponent.FontSize);
				m_CaretRect.sizeDelta = new Vector2(caretWidth, textComponent.FontSize);
				caretImage.color = caretColor;
			}
		}

		private void CreateSelectionHighlight() {
			if (m_SelectionContainer != null) return;
			if (textComponent == null) return;

			var containerGO = new GameObject("SelectionContainer", typeof(RectTransform));
			containerGO.transform.SetParent(textComponent.transform, false);
			containerGO.transform.SetAsFirstSibling();
			m_SelectionContainer = containerGO.transform;
			var containerRT = containerGO.GetComponent<RectTransform>();
			containerRT.anchorMin = Vector2.zero;
			containerRT.anchorMax = Vector2.one;
			containerRT.offsetMin = Vector2.zero;
			containerRT.offsetMax = Vector2.zero;
			containerGO.SetActive(false);

			// Keep legacy reference for compatibility
			m_SelectionHighlight = null;
		}

		private Image GetOrCreateSelectionRect(int index) {
			while (m_SelectionRects.Count <= index) {
				var go = new GameObject($"SelRect_{m_SelectionRects.Count}",
					typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
				go.transform.SetParent(m_SelectionContainer, false);
				var img = go.GetComponent<Image>();
				img.color = selectionColor;
				img.raycastTarget = false;
				m_SelectionRects.Add(img);
			}
			return m_SelectionRects[index];
		}

		private void UpdateSelectionVisual() {
			if (m_SelectionContainer == null) return;

			if (!HasSelection || !m_HasFocus) {
				m_SelectionContainer.gameObject.SetActive(false);
				return;
			}

			int start = Mathf.Min(m_CaretPosition, m_SelectionAnchor);
			int end = Mathf.Max(m_CaretPosition, m_SelectionAnchor);

			var textRT = textComponent.GetComponent<RectTransform>();
			Rect parentRect = textRT.rect;
			var info = textComponent.Info;

			if (info == null || info.LineCount == 0) {
				m_SelectionContainer.gameObject.SetActive(false);
				return;
			}

			// Find which lines the selection spans
			var startCaret = info.GetCaretInfo(new CaretPosition(start));
			var endCaret = info.GetCaretInfo(new CaretPosition(end));

			if (startCaret.IsNone || endCaret.IsNone) {
				m_SelectionContainer.gameObject.SetActive(false);
				return;
			}

			m_SelectionContainer.gameObject.SetActive(true);

			int startLine = startCaret.LineIndex;
			int endLine = endCaret.LineIndex;
			int rectIndex = 0;

				for (int line = startLine; line <= endLine; line++) {

				if (line == startLine && line == endLine) {
					// Single line selection — use min/max X for bidi support
					if (!textComponent.GetCaretLocalRect(start, out Rect sRect) ||
						!textComponent.GetCaretLocalRect(end, out Rect eRect))
						continue;
					float left = Mathf.Min(sRect.x, eRect.x);
					float right = Mathf.Max(sRect.x, eRect.x);
					PlaceSelectionRect(rectIndex++, left, sRect.y, right - left, sRect.height, parentRect);
				} else if (line == startLine) {
					// First line: from start caret to line boundary
					if (!textComponent.GetCaretLocalRect(start, out Rect sRect))
						continue;
					// Get end-of-line caret position
					var lineInfo = info.Lines[line];
					int lineEndIdx = Mathf.Min(lineInfo.Start + lineInfo.Length, m_Text.Length);
					float lineEdgeX;
					if (textComponent.GetCaretLocalRect(lineEndIdx, out Rect leRect))
						lineEdgeX = leRect.x;
					else
						lineEdgeX = parentRect.xMax;
					float left = Mathf.Min(sRect.x, lineEdgeX);
					float right = Mathf.Max(sRect.x, lineEdgeX);
					PlaceSelectionRect(rectIndex++, left, sRect.y, right - left, sRect.height, parentRect);
				} else if (line == endLine) {
					// Last line: from line boundary to end caret
					if (!textComponent.GetCaretLocalRect(end, out Rect eRect))
						continue;
					// Get start-of-line caret position
					var lineInfo = info.Lines[line];
					float lineEdgeX;
					if (textComponent.GetCaretLocalRect(lineInfo.Start, out Rect lsRect))
						lineEdgeX = lsRect.x;
					else
						lineEdgeX = parentRect.xMin;
					float left = Mathf.Min(eRect.x, lineEdgeX);
					float right = Mathf.Max(eRect.x, lineEdgeX);
					PlaceSelectionRect(rectIndex++, left, eRect.y, right - left, eRect.height, parentRect);
				} else {
					// Middle line: full width
					var lineInfo = info.Lines[line];
					int midIndex = lineInfo.Start;
					if (!textComponent.GetCaretLocalRect(midIndex, out Rect mRect))
						continue;
					PlaceSelectionRect(rectIndex++, parentRect.xMin, mRect.y, parentRect.width, mRect.height, parentRect);
				}
			}

			// Hide unused rects
			for (int i = rectIndex; i < m_SelectionRects.Count; i++) {
				m_SelectionRects[i].gameObject.SetActive(false);
			}
		}

		private void PlaceSelectionRect(int index, float localX, float localY, float width, float height, Rect parentRect) {
			var img = GetOrCreateSelectionRect(index);
			img.gameObject.SetActive(true);
			var rt = img.GetComponent<RectTransform>();
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.zero;
			rt.pivot = Vector2.zero;
			rt.anchoredPosition = new Vector2(localX - parentRect.xMin, localY - parentRect.yMin);
			rt.sizeDelta = new Vector2(Mathf.Max(4f, width), height);
		}

		private void RefreshCaretView() {
			UpdateCaretVisual();
			ScrollToCaret();
			UpdateSelectionVisual();
			SyncKeyboardSelection();
		}

		private void ResetBlink() {
			m_BlinkTimer = 0f;
			m_CaretVisible = true;
			if (caretImage != null)
				caretImage.enabled = true;
		}

		// ===================== Validation =====================

		private bool ValidateChar(char c) {
			if (c < 32 && c != '\n') return false;

			switch (contentType) {
				case ContentType.Integer:
					return char.IsDigit(c) || (c == '-' && m_CaretPosition == 0 && !m_Text.Contains("-"));
				case ContentType.Decimal:
					return char.IsDigit(c) || (c == '.' && !m_Text.Contains("."))
						|| (c == '-' && m_CaretPosition == 0 && !m_Text.Contains("-"));
				case ContentType.Alphanumeric:
					return char.IsLetterOrDigit(c);
				case ContentType.Name:
					return char.IsLetter(c) || c == ' ' || c == '\'' || c == '-';
				case ContentType.EmailAddress:
					return char.IsLetterOrDigit(c) || c == '@' || c == '.' || c == '_' || c == '-' || c == '+';
				case ContentType.Password:
				case ContentType.Standard:
				default:
					return true;
			}
		}

		// ===================== Word Navigation =====================

		// ===================== Grapheme Cluster Helpers =====================
		// Unicode-based implementation that handles ZWJ sequences, surrogate pairs,
		// combining marks, variation selectors, skin tone modifiers, and flag emojis.
		// Font-independent — works even when the font doesn't have combined glyphs.

		private static int CPLen(string t, int i) {
			return (i < t.Length && char.IsHighSurrogate(t[i]) && i + 1 < t.Length && char.IsLowSurrogate(t[i + 1])) ? 2 : 1;
		}

		private static int CP(string t, int i) {
			if (i >= t.Length) return -1;
			return (char.IsHighSurrogate(t[i]) && i + 1 < t.Length && char.IsLowSurrogate(t[i + 1]))
				? char.ConvertToUtf32(t[i], t[i + 1]) : t[i];
		}

		private static bool IsExtender(int cp) =>
			cp == 0x200D ||                                       // ZWJ
			(cp >= 0xFE00 && cp <= 0xFE0F) ||                    // Variation selectors
			(cp >= 0xE0100 && cp <= 0xE01EF) ||                  // Variation selectors supplement
			(cp >= 0x1F3FB && cp <= 0x1F3FF) ||                   // Skin tone modifiers
			(cp >= 0x0300 && cp <= 0x036F) ||                     // Combining diacritical marks
			(cp >= 0x1AB0 && cp <= 0x1AFF) ||                     // Combining marks extended
			(cp >= 0x1DC0 && cp <= 0x1DFF) ||                     // Combining marks supplement
			(cp >= 0x20D0 && cp <= 0x20FF) ||                     // Combining marks for symbols
			(cp >= 0xFE20 && cp <= 0xFE2F) ||                     // Combining half marks
			cp == 0x20E3;                                          // Combining enclosing keycap

		private static bool IsRegional(int cp) => cp >= 0x1F1E6 && cp <= 0x1F1FF;
		private static bool IsTag(int cp) => cp >= 0xE0020 && cp <= 0xE007F;

		/// <summary>
		/// Returns the char index after the next grapheme cluster.
		/// Handles all emoji sequences: ZWJ families, flags, skin tones, keycaps, tag sequences.
		/// </summary>
		private static int NextCluster(string text, int charIndex) {
			if (string.IsNullOrEmpty(text) || charIndex >= text.Length) return text?.Length ?? 0;

			int i = charIndex;
			int cp = CP(text, i);

			// Regional indicator pair (flags 🇯🇴)
			if (IsRegional(cp)) {
				i += CPLen(text, i);
				if (i < text.Length && IsRegional(CP(text, i)))
					i += CPLen(text, i);
				// Consume trailing VS
				while (i < text.Length && IsExtender(CP(text, i)))
					i += CPLen(text, i);
				return i;
			}

			// Tag sequences (🏴󠁧󠁢󠁥󠁮󠁧󠁿)
			if (cp == 0x1F3F4) {
				i += CPLen(text, i);
				while (i < text.Length && IsTag(CP(text, i)))
					i += CPLen(text, i);
				if (i < text.Length && CP(text, i) == 0xE007F) // cancel tag
					i += CPLen(text, i);
				return i;
			}

			// Skip base code point
			i += CPLen(text, i);

			// Consume all extenders (ZWJ + next CP, variation selectors, skin tones, combining marks)
			while (i < text.Length) {
				cp = CP(text, i);

				if (cp == 0x200D) {
					// ZWJ: consume ZWJ + next code point
					int zjLen = CPLen(text, i);
					if (i + zjLen < text.Length) {
						i += zjLen; // skip ZWJ
						i += CPLen(text, i); // skip next code point
						continue; // loop to consume that code point's modifiers too
					}
					break;
				}

				if (IsExtender(cp)) {
					i += CPLen(text, i);
					continue;
				}

				break;
			}

			return i;
		}

		/// <summary>
		/// Returns the start of the grapheme cluster before charIndex.
		/// </summary>
		private static int PrevCluster(string text, int charIndex) {
			if (string.IsNullOrEmpty(text) || charIndex <= 0) return 0;

			// Build cluster boundaries forward, find the one before charIndex
			int prev = 0;
			int cur = 0;
			while (cur < text.Length) {
				int next = NextCluster(text, cur);
				if (next >= charIndex) return cur;
				prev = cur;
				cur = next;
			}
			return cur;
		}

		/// <summary>
		/// Snaps to the nearest cluster boundary (start of the cluster containing charIndex).
		/// </summary>
		private static int SnapToClusterBoundary(string text, int charIndex) {
			if (string.IsNullOrEmpty(text) || charIndex <= 0) return 0;
			if (charIndex >= text.Length) return text.Length;

			int cur = 0;
			while (cur < text.Length) {
				int next = NextCluster(text, cur);
				if (cur == charIndex) return charIndex;
				if (next > charIndex) return cur;
				cur = next;
			}
			return text.Length;
		}

		private int FindPreviousWordStart(int from) {
			if (from <= 0) return 0;
			int i = from - 1;
			// Skip whitespace
			while (i > 0 && char.IsWhiteSpace(m_Text[i])) i--;
			// Skip word
			while (i > 0 && !char.IsWhiteSpace(m_Text[i - 1])) i--;
			return i;
		}

		private int FindNextWordEnd(int from) {
			if (from >= m_Text.Length) return m_Text.Length;
			int i = from;
			// Skip word
			while (i < m_Text.Length && !char.IsWhiteSpace(m_Text[i])) i++;
			// Skip whitespace
			while (i < m_Text.Length && char.IsWhiteSpace(m_Text[i])) i++;
			return i;
		}
	}

	internal class SimpleClickHandler : MonoBehaviour, IPointerDownHandler, IPointerClickHandler {
		public System.Action action;
		public void OnPointerDown(PointerEventData e) {
			Debug.Log($"[ContextMenu] OnPointerDown on {gameObject.name}");
		}
		public void OnPointerClick(PointerEventData e) {
			Debug.Log($"[ContextMenu] OnPointerClick on {gameObject.name}, action null? {action == null}");
			action?.Invoke();
			Debug.Log($"[ContextMenu] Action executed on {gameObject.name}");
		}
	}
}
