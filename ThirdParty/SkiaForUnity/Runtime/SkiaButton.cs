using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SkiaSharp.Unity {

	[AddComponentMenu("Skia UI (Canvas)/Skia Button")]
	public class SkiaButton : Selectable, IPointerClickHandler {

		[Header("References")]
		[SerializeField] SkiaGraphic background;
		[SerializeField] HB.HB_TEXTBlock label;

		[Header("Label")]
		[SerializeField] string text = "Button";

		[Header("Colors")]
		[SerializeField] Color normalColor = new Color(0.24f, 0.48f, 0.9f, 1f);
		[SerializeField] Color hoverColor = new Color(0.30f, 0.55f, 0.95f, 1f);
		[SerializeField] Color pressedColor = new Color(0.18f, 0.40f, 0.78f, 1f);
		[SerializeField] Color disabledColor = new Color(0.65f, 0.65f, 0.65f, 1f);
		[SerializeField] Color normalTextColor = Color.white;
		[SerializeField] Color disabledTextColor = new Color(0.85f, 0.85f, 0.85f, 1f);

		[Header("Events")]
		public UnityEvent onClick = new UnityEvent();

		private SelectionState lastState;

		// --- Public API ---

		public string Text {
			get => text;
			set {
				text = value;
				if (label != null) label.text = text;
			}
		}

		public SkiaGraphic Background => background;
		public HB.HB_TEXTBlock Label => label;

		public Color NormalColor {
			get => normalColor;
			set { normalColor = value; UpdateVisuals(); }
		}

		public Color HoverColor {
			get => hoverColor;
			set { hoverColor = value; UpdateVisuals(); }
		}

		public Color PressedColor {
			get => pressedColor;
			set { pressedColor = value; UpdateVisuals(); }
		}

		public Color DisabledColor {
			get => disabledColor;
			set { disabledColor = value; UpdateVisuals(); }
		}

		// --- Lifecycle ---

		protected override void Awake() {
			base.Awake();
			if (background == null)
				background = GetComponent<SkiaGraphic>();
		}

		protected override void Start() {
			base.Start();
			if (label != null) label.text = text;
			UpdateVisuals();
		}

		protected override void OnEnable() {
			base.OnEnable();
			UpdateVisuals();
		}

#if UNITY_EDITOR
		protected override void OnValidate() {
			base.OnValidate();
			if (label != null) label.text = text;
			UpdateVisuals();
		}
#endif

		// --- State Changes ---

		protected override void DoStateTransition(SelectionState state, bool instant) {
			base.DoStateTransition(state, instant);
			lastState = state;
			UpdateVisuals();
		}

		private void UpdateVisuals() {
			if (background == null) return;

			Color targetColor;
			Color targetTextColor = normalTextColor;

			switch (lastState) {
				case SelectionState.Highlighted:
					targetColor = hoverColor;
					break;
				case SelectionState.Pressed:
					targetColor = pressedColor;
					break;
				case SelectionState.Disabled:
					targetColor = disabledColor;
					targetTextColor = disabledTextColor;
					break;
				case SelectionState.Selected:
					targetColor = hoverColor;
					break;
				default:
					targetColor = normalColor;
					break;
			}

			background.FillColor = targetColor;

			if (label != null)
				label.FontColor = targetTextColor;
		}

		// --- Click ---

		public void OnPointerClick(PointerEventData eventData) {
			if (eventData.button != PointerEventData.InputButton.Left) return;
			if (!IsActive() || !IsInteractable()) return;
			onClick?.Invoke();
		}
	}
}
