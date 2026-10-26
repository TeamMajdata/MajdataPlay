using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace SkiaSharp.Unity.HB {

	[System.Serializable]
	public class AnimationStep {
		public string name = "Step";
		public float duration = 1f;
		public float delay;
		public AnimationCurve easeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

		// Text
		public bool typewriterEnabled;

		// Gradient Angle
		public bool gradientAnimEnabled;
		public float gradientStartAngle;
		public float gradientEndAngle = 360f;

		// Fade
		public bool fadeEnabled;
		public float fadeFrom;
		public float fadeTo = 1f;

		// Color
		public bool colorLerpEnabled;
		public Color colorFrom = Color.white;
		public Color colorTo = Color.red;

		// Scale
		public bool scaleEnabled;
		public Vector3 scaleFrom = Vector3.zero;
		public Vector3 scaleTo = Vector3.one;

		// Slide
		public bool slideEnabled;
		public Vector2 slideOffset = new Vector2(-100f, 0f);

		// Shake
		public bool shakeEnabled;
		public float shakeIntensity = 5f;

		// Outline Pulse
		public bool outlinePulseEnabled;
		public int outlineFrom;
		public int outlineTo = 5;

		// Shadow Animate
		public bool shadowAnimEnabled;
		public Vector2 shadowOffsetFrom;
		public Vector2 shadowOffsetTo = new Vector2(3f, 3f);

		// Font Size
		public bool fontSizeAnimEnabled;
		public int fontSizeFrom = 10;
		public int fontSizeTo = 40;

		// Letter Spacing
		public bool letterSpacingAnimEnabled;
		public int letterSpacingFrom;
		public int letterSpacingTo = 10;
	}

	[System.Serializable]
	public class StepCompleteEvent : UnityEvent<int> { }

	[RequireComponent(typeof(HB_TEXTBlock))]
	[AddComponentMenu("Skia UI (Canvas)/HB Text Animator")]
	public class HBTextAnimator : MonoBehaviour {

		// --- General ---
		[SerializeField] bool autoPlay = true;
		[SerializeField] bool loop;

		// --- Steps ---
		[SerializeField] List<AnimationStep> steps = new List<AnimationStep> { new AnimationStep() };

		// --- Events ---
		public UnityEvent onComplete = new UnityEvent();
		public StepCompleteEvent onStepComplete = new StepCompleteEvent();

		// --- Legacy fields for migration from v1 ---
		[SerializeField, HideInInspector] bool _migrated;
		[SerializeField, HideInInspector] float duration = 2f;
		[SerializeField, HideInInspector] AnimationCurve easeCurve;
		[SerializeField, HideInInspector] bool typewriterEnabled;
		[SerializeField, HideInInspector] bool gradientAnimEnabled;
		[SerializeField, HideInInspector] float gradientStartAngle;
		[SerializeField, HideInInspector] float gradientEndAngle = 360f;
		[SerializeField, HideInInspector] bool fadeEnabled;
		[SerializeField, HideInInspector] bool colorLerpEnabled;
		[SerializeField, HideInInspector] Color colorFrom = Color.white;
		[SerializeField, HideInInspector] Color colorTo = Color.red;
		[SerializeField, HideInInspector] bool scaleEnabled;
		[SerializeField, HideInInspector] Vector3 scaleFrom = Vector3.zero;
		[SerializeField, HideInInspector] Vector3 scaleTo = Vector3.one;
		[SerializeField, HideInInspector] bool slideEnabled;
		[SerializeField, HideInInspector] Vector2 slideOffset = new Vector2(-100f, 0f);
		[SerializeField, HideInInspector] bool shakeEnabled;
		[SerializeField, HideInInspector] float shakeIntensity = 5f;
		[SerializeField, HideInInspector] bool outlinePulseEnabled;
		[SerializeField, HideInInspector] int outlineFrom;
		[SerializeField, HideInInspector] int outlineTo = 5;
		[SerializeField, HideInInspector] bool shadowAnimEnabled;
		[SerializeField, HideInInspector] Vector2 shadowOffsetFrom;
		[SerializeField, HideInInspector] Vector2 shadowOffsetTo = new Vector2(3f, 3f);
		[SerializeField, HideInInspector] bool fontSizeAnimEnabled;
		[SerializeField, HideInInspector] int fontSizeFrom = 10;
		[SerializeField, HideInInspector] int fontSizeTo = 40;
		[SerializeField, HideInInspector] bool letterSpacingAnimEnabled;
		[SerializeField, HideInInspector] int letterSpacingFrom;
		[SerializeField, HideInInspector] int letterSpacingTo = 10;

		// --- Internals ---
		private HB_TEXTBlock _hbText;
		private Coroutine _activeCoroutine;
		private bool _isPlaying;
		private int _currentStepIndex = -1;

		private struct AnimSnapshot {
			public string text;
			public Color fontColor;
			public float alpha;
			public Vector3 localScale;
			public Vector2 anchoredPosition;
			public int outlineWidth;
			public float shadowOffsetX, shadowOffsetY;
			public int fontSize;
			public int letterSpacing;
			public float gradientAngle;
			public float maxWidth;
			public float maxHeight;
			public bool valid;
		}
		private AnimSnapshot _snapshot;
		private Vector2 _stepBasePosition;

		// Typewriter cache — avoid Substring allocation every frame
		private int _lastTypewriterCharCount = -1;
		private string _lastTypewriterText;

		// --- Public accessors ---
		public bool IsPlaying => _isPlaying;
		public int CurrentStepIndex => _currentStepIndex;
		public int StepCount => steps.Count;
		public bool Loop => loop;
		public List<AnimationStep> Steps => steps;

		public float TotalDuration {
			get {
				float total = 0f;
				foreach (var s in steps) total += s.delay + s.duration;
				return total;
			}
		}

		private HB_TEXTBlock HBText {
			get {
				if (_hbText == null) _hbText = GetComponent<HB_TEXTBlock>();
				return _hbText;
			}
		}

		private RectTransform RectTr => HBText.GetComponent<RectTransform>();

		private void Awake() {
			_hbText = GetComponent<HB_TEXTBlock>();
		}

		private void Start() {
			if (autoPlay) Play();
		}

		private void OnValidate() {
			MigrateFromV1();
		}

		// ===================== Public API =====================

		public void Play() {
			Stop();
			if (steps.Count == 0) return;
			TakeSnapshot();
			_isPlaying = true;
			_currentStepIndex = 0;
			_activeCoroutine = StartCoroutine(AnimationRoutine());
		}

		public void Stop() {
			if (_activeCoroutine != null) {
				StopCoroutine(_activeCoroutine);
				_activeCoroutine = null;
				RestoreSnapshot();
			}
			_isPlaying = false;
			_currentStepIndex = -1;
		}

		// ===================== Snapshot =====================

		public void TakeSnapshot() {
			_lastTypewriterCharCount = -1;
			_lastTypewriterText = null;
			var rt = RectTr;
			_snapshot = new AnimSnapshot {
				text = HBText.text,
				fontColor = HBText.FontColor,
				alpha = HBText.alpha,
				localScale = rt != null ? rt.localScale : Vector3.one,
				anchoredPosition = rt != null ? rt.anchoredPosition : Vector2.zero,
				outlineWidth = HBText.HaloWidth,
				shadowOffsetX = 0f,
				shadowOffsetY = 0f,
				fontSize = HBText.FontSize,
				letterSpacing = HBText.LetterSpacing,
				gradientAngle = HBText.GradientAngle,
				maxWidth = HBText.MaxWidth,
				maxHeight = HBText.MaxHeight,
				valid = true
			};
		}

		public void RestoreSnapshot() {
			if (!_snapshot.valid) return;

			HBText.ApplyAnimatedValues(
				displayText: _snapshot.text,
				fontColor: _snapshot.fontColor,
				haloWidth: _snapshot.outlineWidth,
				shadowOffX: _snapshot.shadowOffsetX,
				shadowOffY: _snapshot.shadowOffsetY,
				fontSize: _snapshot.fontSize,
				letterSpacing: _snapshot.letterSpacing,
				gradientAngle: _snapshot.gradientAngle,
				maxWidth: _snapshot.maxWidth,
				maxHeight: _snapshot.maxHeight
			);
			HBText.alpha = _snapshot.alpha;

			var rt = RectTr;
			if (rt != null) {
				rt.localScale = _snapshot.localScale;
				rt.anchoredPosition = _snapshot.anchoredPosition;
			}

			_snapshot.valid = false;
		}

		// ===================== Step Base Position =====================

		public void CaptureStepBasePosition() {
			var rt = RectTr;
			_stepBasePosition = rt != null ? rt.anchoredPosition : Vector2.zero;
		}

		// ===================== Frame Application =====================

		public void ApplyStepFrame(AnimationStep step, float rawT) {
			float t = step.easeCurve != null ? step.easeCurve.Evaluate(rawT) : rawT;

			// --- Collect HB property changes ---
			string displayText = null;
			Color? fontColor = null;
			int? haloWidth = null;
			float? shadowOffX = null;
			float? shadowOffY = null;
			int? fontSize = null;
			int? letterSpacing = null;
			float? gradientAngle = null;

			if (step.typewriterEnabled && _snapshot.valid) {
				int charCount = Mathf.Min(Mathf.CeilToInt(t * _snapshot.text.Length), _snapshot.text.Length);
				if (charCount != _lastTypewriterCharCount) {
					_lastTypewriterCharCount = charCount;
					_lastTypewriterText = _snapshot.text.Substring(0, charCount);
				}
				displayText = _lastTypewriterText;
			}

			if (step.gradientAnimEnabled)
				gradientAngle = Mathf.Lerp(step.gradientStartAngle, step.gradientEndAngle, t);

			if (step.colorLerpEnabled)
				fontColor = Color.Lerp(step.colorFrom, step.colorTo, t);

			if (step.outlinePulseEnabled)
				haloWidth = Mathf.RoundToInt(Mathf.Lerp(step.outlineFrom, step.outlineTo, t));

			if (step.shadowAnimEnabled) {
				shadowOffX = Mathf.Lerp(step.shadowOffsetFrom.x, step.shadowOffsetTo.x, t);
				shadowOffY = Mathf.Lerp(step.shadowOffsetFrom.y, step.shadowOffsetTo.y, t);
			}

			float? maxWidth = null;
			float? maxHeight = null;

			if (step.fontSizeAnimEnabled) {
				int newSize = Mathf.RoundToInt(Mathf.Lerp(step.fontSizeFrom, step.fontSizeTo, t));
				fontSize = newSize;
				if (_snapshot.valid && _snapshot.fontSize > 0) {
					float ratio = (float)newSize / _snapshot.fontSize;
					maxWidth = _snapshot.maxWidth * ratio;
					if (_snapshot.maxHeight > 0) maxHeight = _snapshot.maxHeight * ratio;
				}
			}

			if (step.letterSpacingAnimEnabled)
				letterSpacing = Mathf.RoundToInt(Mathf.Lerp(step.letterSpacingFrom, step.letterSpacingTo, t));

			// --- Apply all HB changes in one render ---
			bool hasHBChanges = displayText != null || fontColor.HasValue || haloWidth.HasValue
				|| shadowOffX.HasValue || fontSize.HasValue || letterSpacing.HasValue
				|| gradientAngle.HasValue || maxWidth.HasValue || maxHeight.HasValue;

			if (hasHBChanges) {
				HBText.ApplyAnimatedValues(displayText, fontColor, haloWidth, shadowOffX, shadowOffY,
					fontSize, letterSpacing, gradientAngle, maxWidth, maxHeight);
			}

			// --- Fade ---
			if (step.fadeEnabled)
				HBText.alpha = Mathf.Lerp(step.fadeFrom, step.fadeTo, t);

			// --- RectTransform animations ---
			var rt = RectTr;
			if (rt == null) return;

			if (step.scaleEnabled)
				rt.localScale = Vector3.Lerp(step.scaleFrom, step.scaleTo, t);

			Vector2 pos = _stepBasePosition;
			bool posChanged = false;

			if (step.slideEnabled) {
				pos = Vector2.Lerp(_stepBasePosition + step.slideOffset, _stepBasePosition, t);
				posChanged = true;
			}

			if (step.shakeEnabled) {
				float damping = 1f - t;
				pos += new Vector2(
					Random.Range(-step.shakeIntensity, step.shakeIntensity),
					Random.Range(-step.shakeIntensity, step.shakeIntensity)
				) * damping;
				posChanged = true;
			}

			if (posChanged)
				rt.anchoredPosition = pos;
		}

		// ===================== Runtime Coroutine =====================

		private IEnumerator AnimationRoutine() {
			do {
				for (int i = 0; i < steps.Count; i++) {
					_currentStepIndex = i;
					var step = steps[i];

					// Wait for delay
					if (step.delay > 0f)
						yield return new WaitForSeconds(step.delay);

					// Capture position at the start of this step
					CaptureStepBasePosition();

					float elapsed = 0f;
					while (elapsed < step.duration) {
						elapsed += Time.deltaTime;
						ApplyStepFrame(step, Mathf.Clamp01(elapsed / step.duration));
						yield return null;
					}

					// Final frame
					ApplyStepFrame(step, 1f);
					onStepComplete?.Invoke(i);
				}

				onComplete?.Invoke();

				// Reset for next loop iteration
				if (loop && _isPlaying) {
					RestoreSnapshot();
					TakeSnapshot();
				}
			} while (loop && _isPlaying);

			_snapshot.valid = false;
			_isPlaying = false;
			_currentStepIndex = -1;
			_activeCoroutine = null;
		}

		// ===================== Migration =====================

		private void MigrateFromV1() {
			if (_migrated) return;
			_migrated = true;

			bool hasOldData = typewriterEnabled || fadeEnabled || colorLerpEnabled
				|| scaleEnabled || slideEnabled || shakeEnabled || gradientAnimEnabled
				|| outlinePulseEnabled || shadowAnimEnabled || fontSizeAnimEnabled
				|| letterSpacingAnimEnabled;

			if (!hasOldData) return;

			if (steps == null) steps = new List<AnimationStep>();
			steps.Clear();
			steps.Add(new AnimationStep {
				name = "Step 1",
				duration = duration,
				easeCurve = easeCurve ?? AnimationCurve.EaseInOut(0f, 0f, 1f, 1f),
				typewriterEnabled = typewriterEnabled,
				gradientAnimEnabled = gradientAnimEnabled,
				gradientStartAngle = gradientStartAngle,
				gradientEndAngle = gradientEndAngle,
				fadeEnabled = fadeEnabled,
				colorLerpEnabled = colorLerpEnabled,
				colorFrom = colorFrom,
				colorTo = colorTo,
				scaleEnabled = scaleEnabled,
				scaleFrom = scaleFrom,
				scaleTo = scaleTo,
				slideEnabled = slideEnabled,
				slideOffset = slideOffset,
				shakeEnabled = shakeEnabled,
				shakeIntensity = shakeIntensity,
				outlinePulseEnabled = outlinePulseEnabled,
				outlineFrom = outlineFrom,
				outlineTo = outlineTo,
				shadowAnimEnabled = shadowAnimEnabled,
				shadowOffsetFrom = shadowOffsetFrom,
				shadowOffsetTo = shadowOffsetTo,
				fontSizeAnimEnabled = fontSizeAnimEnabled,
				fontSizeFrom = fontSizeFrom,
				fontSizeTo = fontSizeTo,
				letterSpacingAnimEnabled = letterSpacingAnimEnabled,
				letterSpacingFrom = letterSpacingFrom,
				letterSpacingTo = letterSpacingTo,
			});
		}
	}
}
