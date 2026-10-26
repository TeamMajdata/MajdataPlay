
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SkiaSharp.Unity {

	[AddComponentMenu("Skia UI (Canvas)/Skia Dropdown")]
	public class SkiaDropdown : Selectable, IPointerClickHandler, ICancelHandler {

		public enum CaptionDisplayMode {
			Text,
			Icon,
			IconAndText
		}

		[Serializable]
		public class OptionData {
			public string text;
			public Texture2D icon;
			public OptionData() { }
			public OptionData(string text) { this.text = text; }
			public OptionData(string text, Texture2D icon) { this.text = text; this.icon = icon; }
		}

		[Header("References")]
		[SerializeField] SkiaGraphic background;
		[SerializeField] HB.HB_TEXTBlock captionText;
		[SerializeField] SkiaGraphic captionIcon;
		[SerializeField] RectTransform arrow;

		[Header("Template")]
		[SerializeField] float itemHeight = 120f;
		[SerializeField] float itemSpacing = 6f;
		[SerializeField] float itemCornerRadius = 16f;
		[SerializeField] float panelPadding = 12f;
		[SerializeField] float maxDropdownHeight = 600f;
		[SerializeField] float minDropdownWidth = 0f;
		[SerializeField] float iconSize = 80f;
		[SerializeField] Vector4 dropdownCornerRadii = new Vector4(24, 24, 24, 24);

		[Header("Display")]
		[SerializeField] CaptionDisplayMode displayMode = CaptionDisplayMode.Text;

		[Header("State")]
		[SerializeField] int m_Value;
		[SerializeField] List<OptionData> m_Options = new List<OptionData>();

		[Header("Colors")]
		[SerializeField] Color dropdownColor = Color.white;
		[SerializeField] Color panelColor = new Color(0.94f, 0.94f, 0.94f, 1f);
		[SerializeField] Color itemNormalColor = new Color(0.99f, 0.99f, 0.99f, 1f);
		[SerializeField] Color itemHoverColor = new Color(0.92f, 0.95f, 1f, 1f);
		[SerializeField] Color itemSelectedColor = new Color(0.24f, 0.48f, 0.9f, 1f);
		[SerializeField] Color itemTextColor = new Color(0.15f, 0.15f, 0.15f, 1f);
		[SerializeField] Color itemSelectedTextColor = Color.white;
		[SerializeField] Color disabledColor = new Color(0.65f, 0.65f, 0.65f, 1f);
		[SerializeField] Color overlayColor = new Color(0, 0, 0, 0.3f);

		[Serializable] public class DropdownEvent : UnityEvent<int> { }

		[Header("Events")]
		public DropdownEvent onValueChanged = new DropdownEvent();

		// Runtime
		private GameObject m_DropdownPanel;
		private Canvas m_RootCanvas;
		private bool m_IsOpen;
		private readonly List<SkiaDropdownItem> m_Items = new List<SkiaDropdownItem>();

		// --- Public API ---

		public int value {
			get => m_Value;
			set {
				int clamped = Mathf.Clamp(value, 0, Mathf.Max(0, m_Options.Count - 1));
				if (m_Value == clamped && m_Options.Count > 0) return;
				m_Value = clamped;
				RefreshCaption();
				onValueChanged?.Invoke(m_Value);
			}
		}

		public List<OptionData> options {
			get => m_Options;
			set {
				m_Options = value ?? new List<OptionData>();
				m_Value = Mathf.Clamp(m_Value, 0, Mathf.Max(0, m_Options.Count - 1));
				RefreshCaption();
			}
		}

		public bool isOpen => m_IsOpen;
		public SkiaGraphic Background => background;
		public HB.HB_TEXTBlock CaptionText => captionText;

		public CaptionDisplayMode DisplayMode {
			get => displayMode;
			set { displayMode = value; RefreshCaption(); }
		}

		public void SetOptions(params string[] items) {
			m_Options.Clear();
			foreach (var item in items)
				m_Options.Add(new OptionData(item));
			m_Value = Mathf.Clamp(m_Value, 0, Mathf.Max(0, m_Options.Count - 1));
			RefreshCaption();
		}

		public void SetOptions(Texture2D[] icons, string[] labels = null) {
			m_Options.Clear();
			for (int i = 0; i < icons.Length; i++) {
				string label = labels != null && i < labels.Length ? labels[i] : "";
				m_Options.Add(new OptionData(label, icons[i]));
			}
			m_Value = Mathf.Clamp(m_Value, 0, Mathf.Max(0, m_Options.Count - 1));
			RefreshCaption();
		}

		public void AddOption(string text) {
			m_Options.Add(new OptionData(text));
			if (m_Options.Count == 1) RefreshCaption();
		}

		public void AddOption(string text, Texture2D icon) {
			m_Options.Add(new OptionData(text, icon));
			if (m_Options.Count == 1) RefreshCaption();
		}

		public void ClearOptions() {
			m_Options.Clear();
			m_Value = 0;
			RefreshCaption();
		}

		// --- Lifecycle ---

		protected override void Awake() {
			base.Awake();
			if (background == null)
				background = GetComponent<SkiaGraphic>();
		}

		protected override void Start() {
			base.Start();
			RefreshCaption();
		}

		protected override void OnEnable() {
			base.OnEnable();
			RefreshCaption();
		}

		protected override void OnDisable() {
			base.OnDisable();
			CloseDropdown();
		}

#if UNITY_EDITOR
		protected override void OnValidate() {
			base.OnValidate();
			RefreshCaption();
		}
#endif

		private void RefreshCaption() {
			bool hasValidOption = m_Options.Count > 0 && m_Value >= 0 && m_Value < m_Options.Count;
			Texture2D optionIcon = hasValidOption ? m_Options[m_Value].icon : null;
			string optionText = hasValidOption ? m_Options[m_Value].text : "";

			bool showIcon = optionIcon != null && displayMode != CaptionDisplayMode.Text;
			bool showText = displayMode != CaptionDisplayMode.Icon || optionIcon == null;

			if (captionIcon != null) {
				if (showIcon) {
					captionIcon.FillType = SkiaFillType.Image;
					captionIcon.FillImage = optionIcon;
					captionIcon.gameObject.SetActive(true);
				} else {
					captionIcon.gameObject.SetActive(false);
				}
			}

			if (captionText != null) {
				captionText.text = showText ? optionText : "";
				captionText.gameObject.SetActive(showText);
			}

			if (background != null)
				background.FillColor = IsInteractable() ? dropdownColor : disabledColor;
		}

		// --- Click ---

		public void OnPointerClick(PointerEventData eventData) {
			if (eventData.button != PointerEventData.InputButton.Left) return;
			if (!IsActive() || !IsInteractable()) return;
			if (m_IsOpen) CloseDropdown(); else OpenDropdown();
		}

		public void OnCancel(BaseEventData eventData) {
			if (m_IsOpen) CloseDropdown();
		}

		// --- Open / Close ---

		public void OpenDropdown() {
			if (m_IsOpen || m_Options.Count == 0) return;
			m_IsOpen = true;

			m_RootCanvas = GetComponentInParent<Canvas>();
			if (m_RootCanvas == null) return;
			Canvas root = m_RootCanvas;
			while (root.transform.parent != null) {
				Canvas parent = root.transform.parent.GetComponentInParent<Canvas>();
				if (parent == null) break;
				root = parent;
			}
			m_RootCanvas = root;

			CreateDropdownPanel();

			if (arrow != null) {
				arrow.localRotation = Quaternion.Euler(0, 0, 180f);
			}
		}

		public void CloseDropdown() {
			if (!m_IsOpen) return;
			m_IsOpen = false;

			if (m_DropdownPanel != null) {
				if (Application.isPlaying) Destroy(m_DropdownPanel);
				else DestroyImmediate(m_DropdownPanel);
				m_DropdownPanel = null;
			}
			m_Items.Clear();

			if (arrow != null) {
				arrow.localRotation = Quaternion.identity;
			}
		}

		// --- Panel Creation ---

		private void CreateDropdownPanel() {
			RectTransform selfRT = GetComponent<RectTransform>();
			float contentHeight = m_Options.Count * itemHeight + (m_Options.Count - 1) * itemSpacing;
			float totalPanelHeight = Mathf.Min(contentHeight + panelPadding * 2, maxDropdownHeight);
			float width = selfRT.rect.width;

			// --- Overlay (dim background + catch outside clicks) ---
			var overlayGO = new GameObject("SkiaDropdownOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			overlayGO.transform.SetParent(m_RootCanvas.transform, false);

			var overlayRT = overlayGO.GetComponent<RectTransform>();
			overlayRT.anchorMin = Vector2.zero;
			overlayRT.anchorMax = Vector2.one;
			overlayRT.offsetMin = Vector2.zero;
			overlayRT.offsetMax = Vector2.zero;

			var overlayImg = overlayGO.GetComponent<Image>();
			overlayImg.color = overlayColor;

			var overlayBtn = overlayGO.AddComponent<Button>();
			overlayBtn.transition = Selectable.Transition.None;
			overlayBtn.onClick.AddListener(CloseDropdown);

			m_DropdownPanel = overlayGO;

			// --- Panel card ---
			var panelGO = new GameObject("SkiaDropdownPanel", typeof(RectTransform), typeof(CanvasRenderer));
			panelGO.transform.SetParent(overlayGO.transform, false);

			var panelRT = panelGO.GetComponent<RectTransform>();
			PositionPanel(panelRT, selfRT, totalPanelHeight, width);

			// Panel background
			var panelImg = panelGO.AddComponent<Image>();
			panelImg.color = panelColor;
			panelImg.raycastTarget = true;

			// Panel shadow
			var shadow = panelGO.AddComponent<Shadow>();
			shadow.effectColor = new Color(0, 0, 0, 0.15f);
			shadow.effectDistance = new Vector2(0, -4);

			// --- Scroll area (inside panel with padding) ---
			bool needsScroll = contentHeight + panelPadding * 2 > maxDropdownHeight;
			RectTransform contentParent;

			if (needsScroll) {
				var scrollGO = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
				scrollGO.transform.SetParent(panelGO.transform, false);
				var scrollRT = scrollGO.GetComponent<RectTransform>();
				scrollRT.anchorMin = Vector2.zero;
				scrollRT.anchorMax = Vector2.one;
				scrollRT.offsetMin = new Vector2(panelPadding, panelPadding);
				scrollRT.offsetMax = new Vector2(-panelPadding, -panelPadding);

				var contentGO = new GameObject("Content", typeof(RectTransform));
				contentGO.transform.SetParent(scrollGO.transform, false);
				var contentRT = contentGO.GetComponent<RectTransform>();
				contentRT.anchorMin = new Vector2(0, 1);
				contentRT.anchorMax = new Vector2(1, 1);
				contentRT.pivot = new Vector2(0.5f, 1);
				contentRT.offsetMin = Vector2.zero;
				contentRT.offsetMax = Vector2.zero;
				contentRT.sizeDelta = new Vector2(0, contentHeight);

				var scrollRect = scrollGO.GetComponent<ScrollRect>();
				scrollRect.content = contentRT;
				scrollRect.horizontal = false;
				scrollRect.vertical = true;
				scrollRect.movementType = ScrollRect.MovementType.Elastic;
				scrollRect.elasticity = 0.1f;
				scrollRect.scrollSensitivity = itemHeight;
				scrollRect.decelerationRate = 0.135f;

				var maskImg = scrollGO.AddComponent<Image>();
				maskImg.color = Color.white;
				scrollGO.AddComponent<Mask>().showMaskGraphic = false;

				contentParent = contentRT;
			} else {
				var contentGO = new GameObject("Content", typeof(RectTransform));
				contentGO.transform.SetParent(panelGO.transform, false);
				var contentRT = contentGO.GetComponent<RectTransform>();
				contentRT.anchorMin = Vector2.zero;
				contentRT.anchorMax = Vector2.one;
				contentRT.offsetMin = new Vector2(panelPadding, panelPadding);
				contentRT.offsetMax = new Vector2(-panelPadding, -panelPadding);
				contentParent = contentRT;
			}

			// --- Items ---
			m_Items.Clear();
			for (int i = 0; i < m_Options.Count; i++) {
				CreateItem(contentParent, i);
			}
		}

		private void PositionPanel(RectTransform panelRT, RectTransform selfRT, float totalHeight, float width) {
			Vector3[] worldCorners = new Vector3[4];
			selfRT.GetWorldCorners(worldCorners);

			RectTransform canvasRT = m_RootCanvas.GetComponent<RectTransform>();
			Vector2 localBL, localTR;
			RectTransformUtility.ScreenPointToLocalPointInRectangle(
				canvasRT, RectTransformUtility.WorldToScreenPoint(null, worldCorners[0]), null, out localBL);
			RectTransformUtility.ScreenPointToLocalPointInRectangle(
				canvasRT, RectTransformUtility.WorldToScreenPoint(null, worldCorners[2]), null, out localTR);

			float dropdownWidth = Mathf.Max(localTR.x - localBL.x, minDropdownWidth);

			// Canvas bounds
			float canvasLeft = -canvasRT.rect.width * canvasRT.pivot.x;
			float canvasRight = canvasRT.rect.width * (1f - canvasRT.pivot.x);
			float canvasBottom = -canvasRT.rect.height * canvasRT.pivot.y;
			float canvasTop = canvasRT.rect.height * (1f - canvasRT.pivot.y);

			panelRT.anchorMin = new Vector2(0.5f, 0.5f);
			panelRT.anchorMax = new Vector2(0.5f, 0.5f);
			panelRT.sizeDelta = new Vector2(dropdownWidth, totalHeight);

			// Try below first
			float centerX = (localBL.x + localTR.x) * 0.5f;
			float belowY = localBL.y - 8f;
			bool fitsBelow = belowY - totalHeight >= canvasBottom;
			bool fitsAbove = localTR.y + 8f + totalHeight <= canvasTop;

			if (fitsBelow) {
				panelRT.pivot = new Vector2(0.5f, 1f);
				panelRT.anchoredPosition = new Vector2(centerX, belowY);
			} else if (fitsAbove) {
				panelRT.pivot = new Vector2(0.5f, 0f);
				panelRT.anchoredPosition = new Vector2(centerX, localTR.y + 8f);
			} else {
				// Doesn't fit either way — clamp to canvas and shrink height
				float availableHeight = canvasTop - canvasBottom - 16f;
				panelRT.sizeDelta = new Vector2(dropdownWidth, Mathf.Min(totalHeight, availableHeight));
				panelRT.pivot = new Vector2(0.5f, 1f);
				panelRT.anchoredPosition = new Vector2(centerX, canvasTop - 8f);
			}

			// Clamp horizontal — keep menu fully on screen
			Vector2 pos = panelRT.anchoredPosition;
			float halfW = dropdownWidth * 0.5f;
			if (pos.x - halfW < canvasLeft) pos.x = canvasLeft + halfW;
			if (pos.x + halfW > canvasRight) pos.x = canvasRight - halfW;
			panelRT.anchoredPosition = pos;
		}

		private void CreateItem(RectTransform parent, int index) {
			bool selected = index == m_Value;
			bool hasIcon = m_Options[index].icon != null && displayMode != CaptionDisplayMode.Text;
			bool hasText = displayMode != CaptionDisplayMode.Icon || m_Options[index].icon == null;
			float yPos = index * (itemHeight + itemSpacing);

			var itemGO = new GameObject($"Item_{index}", typeof(RectTransform), typeof(CanvasRenderer));
			itemGO.transform.SetParent(parent, false);

			var itemRT = itemGO.GetComponent<RectTransform>();
			itemRT.anchorMin = new Vector2(0, 1);
			itemRT.anchorMax = new Vector2(1, 1);
			itemRT.pivot = new Vector2(0.5f, 1);
			itemRT.anchoredPosition = new Vector2(0, -yPos);
			itemRT.sizeDelta = new Vector2(0, itemHeight);

			// Rounded background
			var bgImg = itemGO.AddComponent<Image>();
			bgImg.color = selected ? itemSelectedColor : itemNormalColor;

			// Icon (SkiaGraphic with Image fill)
			float leftPad = 24f;
			if (hasIcon) {
				var iconGO = new GameObject("Icon", typeof(RectTransform));
				iconGO.transform.SetParent(itemGO.transform, false);

				var iconSG = iconGO.AddComponent<SkiaGraphic>();
				iconSG.Shape = SkiaShapeType.RoundedRect;
				iconSG.CornerRadii = new Vector4(8, 8, 8, 8);
				iconSG.FillType = SkiaFillType.Image;
				iconSG.FillImage = m_Options[index].icon;
				iconSG.ImageFit = SkiaImageFit.Fill;

				var iconRT = iconGO.GetComponent<RectTransform>();
				iconRT.anchorMin = new Vector2(0, 0.5f);
				iconRT.anchorMax = new Vector2(0, 0.5f);
				iconRT.pivot = new Vector2(0, 0.5f);
				iconRT.anchoredPosition = new Vector2(16, 0);
				iconRT.sizeDelta = new Vector2(iconSize, iconSize);

				leftPad = 16f + iconSize + 16f;
			}

			// Label — centered text, vertically centered
			var labelGO = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
			labelGO.transform.SetParent(itemGO.transform, false);
			// RawImage must be transparent so it doesn't cover the item background
			var labelRI = labelGO.GetComponent<RawImage>();
			labelRI.color = new Color(1, 1, 1, 0);
			labelRI.raycastTarget = false;
			var labelRT = labelGO.GetComponent<RectTransform>();
			labelRT.anchorMin = Vector2.zero;
			labelRT.anchorMax = Vector2.one;
			labelRT.offsetMin = new Vector2(leftPad, 0);
			labelRT.offsetMax = new Vector2(-24, 0);

			var textBlock = labelGO.AddComponent<HB.HB_TEXTBlock>();
			textBlock.text = hasText ? m_Options[index].text : "";
			textBlock.FontSize = captionText != null ? captionText.FontSize : 48;
			textBlock.FontColor = selected ? itemSelectedTextColor : itemTextColor;
			textBlock.TextAlignment = hasIcon
				? Topten.RichTextKit.TextAlignment.Left
				: Topten.RichTextKit.TextAlignment.Center;
			textBlock.VAlign = HB.HB_TEXTBlock.VerticalAlignment.Middle;
			if (captionText != null && captionText.Font != null)
				textBlock.Font = captionText.Font;
			if (!hasText) labelGO.SetActive(false);

			// Checkmark for selected item
			if (selected) {
				var checkGO = new GameObject("Check", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
				checkGO.transform.SetParent(itemGO.transform, false);
				var checkRI = checkGO.GetComponent<RawImage>();
				checkRI.color = new Color(1, 1, 1, 0);
				checkRI.raycastTarget = false;
				var checkRT = checkGO.GetComponent<RectTransform>();
				checkRT.anchorMin = new Vector2(1, 0.5f);
				checkRT.anchorMax = new Vector2(1, 0.5f);
				checkRT.pivot = new Vector2(1, 0.5f);
				checkRT.anchoredPosition = new Vector2(-16, 0);
				checkRT.sizeDelta = new Vector2(40, 40);

				var checkText = checkGO.AddComponent<HB.HB_TEXTBlock>();
				checkText.text = "\u2713";
				checkText.FontSize = 32;
				checkText.FontColor = itemSelectedTextColor;
				checkText.TextAlignment = Topten.RichTextKit.TextAlignment.Center;
				checkText.VAlign = HB.HB_TEXTBlock.VerticalAlignment.Middle;
			}

			// Separator line at bottom (except last item)
			if (index < m_Options.Count - 1 && !selected) {
				var sepGO = new GameObject("Sep", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
				sepGO.transform.SetParent(itemGO.transform, false);
				var sepRT = sepGO.GetComponent<RectTransform>();
				sepRT.anchorMin = new Vector2(0.1f, 0);
				sepRT.anchorMax = new Vector2(0.9f, 0);
				sepRT.pivot = new Vector2(0.5f, 0);
				sepRT.anchoredPosition = Vector2.zero;
				sepRT.sizeDelta = new Vector2(0, 1);
				var sepImg = sepGO.GetComponent<Image>();
				sepImg.color = new Color(0, 0, 0, 0.08f);
				sepImg.raycastTarget = false;
			}

			// Item click handler
			var item = itemGO.AddComponent<SkiaDropdownItem>();
			item.Init(this, index, bgImg, itemNormalColor, itemHoverColor, itemSelectedColor,
				itemTextColor, itemSelectedTextColor, textBlock, selected);
			m_Items.Add(item);
		}

		internal void SelectItem(int index) {
			m_Value = index;
			RefreshCaption();
			CloseDropdown();
			onValueChanged?.Invoke(m_Value);
		}
	}

	/// <summary>
	/// Internal helper for dropdown item interaction.
	/// </summary>
	internal class SkiaDropdownItem : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler {

		private SkiaDropdown dropdown;
		private int index;
		private Image bgImage;
		private HB.HB_TEXTBlock label;
		private Color normalColor;
		private Color hoverColor;
		private Color selectedColor;
		private Color normalTextColor;
		private Color selectedTextColor;
		private bool isSelected;

		public void Init(SkiaDropdown dropdown, int index, Image bgImage,
			Color normalColor, Color hoverColor, Color selectedColor,
			Color normalTextColor, Color selectedTextColor,
			HB.HB_TEXTBlock label, bool isSelected) {
			this.dropdown = dropdown;
			this.index = index;
			this.bgImage = bgImage;
			this.normalColor = normalColor;
			this.hoverColor = hoverColor;
			this.selectedColor = selectedColor;
			this.normalTextColor = normalTextColor;
			this.selectedTextColor = selectedTextColor;
			this.label = label;
			this.isSelected = isSelected;
		}

		public void OnPointerClick(PointerEventData eventData) {
			if (eventData.button != PointerEventData.InputButton.Left) return;
			dropdown.SelectItem(index);
		}

		public void OnPointerEnter(PointerEventData eventData) {
			if (bgImage != null && !isSelected) {
				bgImage.color = hoverColor;
				if (label != null) label.FontColor = normalTextColor;
			}
		}

		public void OnPointerExit(PointerEventData eventData) {
			if (bgImage != null) {
				bgImage.color = isSelected ? selectedColor : normalColor;
				if (label != null) label.FontColor = isSelected ? selectedTextColor : normalTextColor;
			}
		}
	}
}
