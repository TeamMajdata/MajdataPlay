using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SkiaSharp.Unity {

	[AddComponentMenu("Skia UI (Canvas)/Skia Toggle")]
	public class SkiaToggle : Selectable, IPointerClickHandler {

		[Header("References")]
		[SerializeField] SkiaGraphic background;
		[SerializeField] SkiaGraphic knob;
		[SerializeField] HB.HB_TEXTBlock label;
		[SerializeField] HB.HB_TEXTBlock statusText;

		[Header("State")]
		[SerializeField] bool m_IsOn;

		[Header("Label")]
		[SerializeField] string text = "Toggle";
		[SerializeField] string onText = "ON";
		[SerializeField] string offText = "OFF";

		[Header("Layout")]
		[SerializeField] float knobPadding = 6f;
		[SerializeField] Vector2 statusTextOffsetOn = Vector2.zero;
		[SerializeField] Vector2 statusTextOffsetOff = Vector2.zero;

		[Header("Colors")]
		[SerializeField] Color offColor = new Color(0.85f, 0.25f, 0.22f, 1f);
		[SerializeField] Color onColor = new Color(0.20f, 0.78f, 0.35f, 1f);
		[SerializeField] Color knobColor = Color.white;
		[SerializeField] Color disabledColor = new Color(0.65f, 0.65f, 0.65f, 1f);
		[SerializeField] Color statusTextColor = Color.white;

		[Serializable] public class ToggleEvent : UnityEvent<bool> { }

		[Header("Events")]
		public ToggleEvent onValueChanged = new ToggleEvent();

		// Runtime
		private RectTransform knobRT;
		private RectTransform bgRT;
		private RectTransform statusRT;
		private float animTarget;
		private float animCurrent;
		private bool animating;

		// --- Public API ---

		public bool isOn {
			get => m_IsOn;
			set {
				if (m_IsOn == value) return;
				m_IsOn = value;
				if (Application.isPlaying)
					StartKnobAnimation();
				else
					UpdateVisuals();
				onValueChanged?.Invoke(m_IsOn);
			}
		}

		public string Text {
			get => text;
			set {
				text = value;
				if (label != null) label.text = text;
			}
		}

		public string OnText {
			get => onText;
			set { onText = value; UpdateStatusText(); }
		}

		public string OffText {
			get => offText;
			set { offText = value; UpdateStatusText(); }
		}

		public SkiaGraphic Background => background;
		public SkiaGraphic Knob => knob;
		public HB.HB_TEXTBlock Label => label;
		public HB.HB_TEXTBlock StatusText => statusText;

		// --- Lifecycle ---

		protected override void Awake() {
			base.Awake();
			if (background == null)
				background = GetComponent<SkiaGraphic>();
			CacheRects();
		}

		protected override void Start() {
			base.Start();
			if (label != null) label.text = text;
			CacheRects();
			UpdateVisuals();
			animCurrent = m_IsOn ? 1f : 0f;
		}

		protected override void OnEnable() {
			base.OnEnable();
			CacheRects();
			UpdateVisuals();
		}

#if UNITY_EDITOR
		protected override void OnValidate() {
			base.OnValidate();
			if (label != null) label.text = text;
			CacheRects();
			UpdateVisuals();
		}
#endif

		private void CacheRects() {
			if (knob != null && knobRT == null)
				knobRT = knob.GetComponent<RectTransform>();
			if (background != null && bgRT == null)
				bgRT = background.GetComponent<RectTransform>();
			if (statusText != null && statusRT == null)
				statusRT = statusText.GetComponent<RectTransform>();
		}

		// --- Animation ---

		private void StartKnobAnimation() {
			animTarget = m_IsOn ? 1f : 0f;
			animating = true;
			UpdateBackgroundColor();
			UpdateStatusText();
		}

		private void Update() {
			if (!animating || knobRT == null || bgRT == null) return;

			animCurrent = Mathf.MoveTowards(animCurrent, animTarget, Time.unscaledDeltaTime * 8f);
			PositionKnob(animCurrent);

			if (Mathf.Approximately(animCurrent, animTarget))
				animating = false;
		}

		// --- State Changes ---

		protected override void DoStateTransition(SelectionState state, bool instant) {
			base.DoStateTransition(state, instant);
			UpdateBackgroundColor();
		}

		private void UpdateVisuals() {
			UpdateBackgroundColor();
			UpdateKnobPosition();
			UpdateStatusText();
		}

		private void UpdateStatusText() {
			if (statusText != null) {
				statusText.text = m_IsOn ? onText : offText;
				statusText.FontColor = statusTextColor;
			}
		}

		private void UpdateBackgroundColor() {
			if (background == null) return;

			bool disabled = !IsInteractable();
			if (disabled) {
				background.FillColor = disabledColor;
			} else {
				background.FillColor = m_IsOn ? onColor : offColor;
			}
		}

		private void UpdateKnobPosition() {
			CacheRects();
			if (knobRT == null || bgRT == null) return;

			float t = m_IsOn ? 1f : 0f;
			animCurrent = t;
			PositionKnob(t);
		}

		private void PositionKnob(float t) {
			if (knobRT == null || bgRT == null) return;

			float bgWidth = bgRT.rect.width;
			float knobSize = knobRT.sizeDelta.x;

			float offX = knobPadding + knobSize * 0.5f;
			float onX = bgWidth - knobPadding - knobSize * 0.5f;
			float x = Mathf.Lerp(offX, onX, t);

			knobRT.anchoredPosition = new Vector2(x, 0);

			if (statusRT != null) {
				Vector2 offset = m_IsOn ? statusTextOffsetOn : statusTextOffsetOff;
				float textX = (m_IsOn ? offX : onX) + offset.x;
				statusRT.anchoredPosition = new Vector2(textX, offset.y);
			}
		}

		// --- Click ---

		public void OnPointerClick(PointerEventData eventData) {
			if (eventData.button != PointerEventData.InputButton.Left) return;
			if (!IsActive() || !IsInteractable()) return;
			isOn = !m_IsOn;
		}
	}
}
