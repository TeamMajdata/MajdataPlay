#if UNITY_EDITOR
using SkiaSharp.Unity.HB;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(HBTextAnimator))]
[CanEditMultipleObjects]
public class HBTextAnimatorEditor : Editor {

	// --- Labels ---
	static readonly GUIContent k_AutoPlay = new GUIContent("Auto Play", "Start animation on play mode.");
	static readonly GUIContent k_Loop = new GUIContent("Loop", "Repeat the full sequence.");
	static readonly GUIContent k_StepName = new GUIContent("Name", "Label for this step.");
	static readonly GUIContent k_Duration = new GUIContent("Duration", "Animation time in seconds.");
	static readonly GUIContent k_Delay = new GUIContent("Delay", "Wait time before this step starts.");
	static readonly GUIContent k_EaseCurve = new GUIContent("Ease", "Animation timing curve.");

	static readonly GUIContent k_Typewriter = new GUIContent("Typewriter", "Reveal text character by character.");
	static readonly GUIContent k_GradientAngle = new GUIContent("Gradient Angle", "Animate the gradient rotation.");
	static readonly GUIContent k_StartAngle = new GUIContent("Start", "Angle at t=0.");
	static readonly GUIContent k_EndAngle = new GUIContent("End", "Angle at t=1.");
	static readonly GUIContent k_Fade = new GUIContent("Fade", "Animate opacity.");
	static readonly GUIContent k_FadeFrom = new GUIContent("From", "Alpha at t=0.");
	static readonly GUIContent k_FadeTo = new GUIContent("To", "Alpha at t=1.");
	static readonly GUIContent k_ColorLerp = new GUIContent("Color Lerp", "Blend font color between two values.");
	static readonly GUIContent k_ColorFrom = new GUIContent("From");
	static readonly GUIContent k_ColorTo = new GUIContent("To");
	static readonly GUIContent k_Scale = new GUIContent("Scale", "Animate RectTransform scale.");
	static readonly GUIContent k_ScaleFrom = new GUIContent("From");
	static readonly GUIContent k_ScaleTo = new GUIContent("To");
	static readonly GUIContent k_Slide = new GUIContent("Slide In", "Slide from an offset to the current position.");
	static readonly GUIContent k_SlideOffset = new GUIContent("Offset", "Starting offset in pixels.");
	static readonly GUIContent k_Shake = new GUIContent("Shake", "Random position jitter that dampens over time.");
	static readonly GUIContent k_ShakeIntensity = new GUIContent("Intensity", "Max random offset in pixels.");
	static readonly GUIContent k_OutlinePulse = new GUIContent("Outline Pulse", "Animate outline width.");
	static readonly GUIContent k_OutlineFrom = new GUIContent("From");
	static readonly GUIContent k_OutlineTo = new GUIContent("To");
	static readonly GUIContent k_ShadowAnim = new GUIContent("Shadow Animate", "Animate shadow offset.");
	static readonly GUIContent k_ShadowFrom = new GUIContent("From");
	static readonly GUIContent k_ShadowTo = new GUIContent("To");
	static readonly GUIContent k_FontSize = new GUIContent("Font Size", "Animate font size.");
	static readonly GUIContent k_FontSizeFrom = new GUIContent("From");
	static readonly GUIContent k_FontSizeTo = new GUIContent("To");
	static readonly GUIContent k_LetterSpacing = new GUIContent("Letter Spacing", "Animate character spacing.");
	static readonly GUIContent k_LetterFrom = new GUIContent("From");
	static readonly GUIContent k_LetterTo = new GUIContent("To");

	// --- Properties ---
	SerializedProperty autoPlayProp, loopProp, stepsProp;
	SerializedProperty onCompleteProp, onStepCompleteProp;

	// --- State ---
	List<bool> stepFoldouts = new List<bool>();
	static GUIStyle s_boldFoldout;

	// Editor preview
	bool _editorPlaying;
	double _editorStartTime;
	int _previewStepIndex;
	double _previewStepStartTime;
	HBTextAnimator _previewTarget;

	static GUIStyle BoldFoldout {
		get {
			if (s_boldFoldout == null) {
				s_boldFoldout = new GUIStyle(EditorStyles.foldout) {
					fontStyle = FontStyle.Bold,
					fontSize = 12
				};
			}
			return s_boldFoldout;
		}
	}

	void OnEnable() {
		autoPlayProp = serializedObject.FindProperty("autoPlay");
		loopProp = serializedObject.FindProperty("loop");
		stepsProp = serializedObject.FindProperty("steps");
		onCompleteProp = serializedObject.FindProperty("onComplete");
		onStepCompleteProp = serializedObject.FindProperty("onStepComplete");

		stepFoldouts.Clear();
		for (int i = 0; i < stepsProp.arraySize; i++)
			stepFoldouts.Add(i == 0);
	}

	void OnDisable() {
		StopEditorPreview();
	}

	public override void OnInspectorGUI() {
		serializedObject.Update();

		// --- General ---
		EditorGUILayout.PropertyField(autoPlayProp, k_AutoPlay);
		EditorGUILayout.PropertyField(loopProp, k_Loop);
		EditorGUILayout.Space(8);

		// --- Steps ---
		for (int i = 0; i < stepsProp.arraySize; i++) {
			if (DrawStep(i)) {
				i--;
				continue;
			}
		}

		// Add Step button
		EditorGUILayout.Space(4);
		if (GUILayout.Button("+ Add Step", GUILayout.Height(26))) {
			AddNewStep();
		}

		EditorGUILayout.Space(8);

		// --- Events ---
		EditorGUILayout.PropertyField(onCompleteProp);
		EditorGUILayout.PropertyField(onStepCompleteProp);
		EditorGUILayout.Space();

		// --- Playback ---
		DrawPlaybackControls();

		serializedObject.ApplyModifiedProperties();
	}

	// ===================== Step Drawing =====================

	// Returns true if step was deleted (caller should adjust index)
	bool DrawStep(int index) {
		while (stepFoldouts.Count <= index) stepFoldouts.Add(true);

		var stepProp = stepsProp.GetArrayElementAtIndex(index);
		var nameProp = stepProp.FindPropertyRelative("name");

		// --- Header row ---
		EditorGUILayout.BeginHorizontal();

		string header = string.Format("Step {0}: {1}", index + 1, nameProp.stringValue);
		stepFoldouts[index] = EditorGUILayout.Foldout(stepFoldouts[index], header, true, BoldFoldout);

		// Reorder buttons
		EditorGUI.BeginDisabledGroup(index == 0);
		if (GUILayout.Button("\u25B2", EditorStyles.miniButtonLeft, GUILayout.Width(22))) {
			stepsProp.MoveArrayElement(index, index - 1);
			SwapFoldouts(index, index - 1);
		}
		EditorGUI.EndDisabledGroup();

		EditorGUI.BeginDisabledGroup(index >= stepsProp.arraySize - 1);
		if (GUILayout.Button("\u25BC", EditorStyles.miniButtonMid, GUILayout.Width(22))) {
			stepsProp.MoveArrayElement(index, index + 1);
			SwapFoldouts(index, index + 1);
		}
		EditorGUI.EndDisabledGroup();

		// Delete button
		EditorGUI.BeginDisabledGroup(stepsProp.arraySize <= 1);
		if (GUILayout.Button("\u2715", EditorStyles.miniButtonRight, GUILayout.Width(22))) {
			stepsProp.DeleteArrayElementAtIndex(index);
			if (index < stepFoldouts.Count) stepFoldouts.RemoveAt(index);
			EditorGUILayout.EndHorizontal();
			return true;
		}
		EditorGUI.EndDisabledGroup();

		EditorGUILayout.EndHorizontal();

		if (!stepFoldouts[index]) return false;

		// --- Step body ---
		EditorGUI.indentLevel++;

		EditorGUILayout.PropertyField(nameProp, k_StepName);
		EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("duration"), k_Duration);
		EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("delay"), k_Delay);
		EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("easeCurve"), k_EaseCurve);
		EditorGUILayout.Space(4);

		// --- Effect toggles ---
		DrawStepToggle(stepProp, "typewriterEnabled", k_Typewriter);
		DrawStepToggleWithFields(stepProp, "gradientAnimEnabled", k_GradientAngle,
			("gradientStartAngle", k_StartAngle), ("gradientEndAngle", k_EndAngle));
		DrawStepToggleWithFields(stepProp, "fadeEnabled", k_Fade,
			("fadeFrom", k_FadeFrom), ("fadeTo", k_FadeTo));
		DrawStepToggleWithFields(stepProp, "colorLerpEnabled", k_ColorLerp,
			("colorFrom", k_ColorFrom), ("colorTo", k_ColorTo));
		DrawStepToggleWithFields(stepProp, "scaleEnabled", k_Scale,
			("scaleFrom", k_ScaleFrom), ("scaleTo", k_ScaleTo));
		DrawStepToggleWithFields(stepProp, "slideEnabled", k_Slide,
			("slideOffset", k_SlideOffset));
		DrawStepToggleWithFields(stepProp, "shakeEnabled", k_Shake,
			("shakeIntensity", k_ShakeIntensity));
		DrawStepToggleWithFields(stepProp, "outlinePulseEnabled", k_OutlinePulse,
			("outlineFrom", k_OutlineFrom), ("outlineTo", k_OutlineTo));
		DrawStepToggleWithFields(stepProp, "shadowAnimEnabled", k_ShadowAnim,
			("shadowOffsetFrom", k_ShadowFrom), ("shadowOffsetTo", k_ShadowTo));
		DrawStepToggleWithFields(stepProp, "fontSizeAnimEnabled", k_FontSize,
			("fontSizeFrom", k_FontSizeFrom), ("fontSizeTo", k_FontSizeTo));
		DrawStepToggleWithFields(stepProp, "letterSpacingAnimEnabled", k_LetterSpacing,
			("letterSpacingFrom", k_LetterFrom), ("letterSpacingTo", k_LetterTo));

		EditorGUI.indentLevel--;

		// Separator line
		EditorGUILayout.Space(4);
		var lineRect = EditorGUILayout.GetControlRect(false, 1);
		EditorGUI.DrawRect(lineRect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
		EditorGUILayout.Space(4);

		return false;
	}

	void DrawStepToggle(SerializedProperty stepProp, string fieldName, GUIContent label) {
		EditorGUILayout.PropertyField(stepProp.FindPropertyRelative(fieldName), label);
	}

	void DrawStepToggleWithFields(SerializedProperty stepProp, string enabledField, GUIContent label,
		params (string field, GUIContent label)[] subFields) {
		var enabledProp = stepProp.FindPropertyRelative(enabledField);
		EditorGUILayout.PropertyField(enabledProp, label);
		if (enabledProp.boolValue) {
			EditorGUI.indentLevel++;
			foreach (var (f, l) in subFields)
				EditorGUILayout.PropertyField(stepProp.FindPropertyRelative(f), l);
			EditorGUI.indentLevel--;
		}
	}

	void SwapFoldouts(int a, int b) {
		while (stepFoldouts.Count <= Mathf.Max(a, b)) stepFoldouts.Add(true);
		bool tmp = stepFoldouts[a];
		stepFoldouts[a] = stepFoldouts[b];
		stepFoldouts[b] = tmp;
	}

	void AddNewStep() {
		int newIndex = stepsProp.arraySize;
		stepsProp.InsertArrayElementAtIndex(newIndex);
		var s = stepsProp.GetArrayElementAtIndex(newIndex);

		s.FindPropertyRelative("name").stringValue = string.Format("Step {0}", newIndex + 1);
		s.FindPropertyRelative("duration").floatValue = 1f;
		s.FindPropertyRelative("delay").floatValue = 0f;
		s.FindPropertyRelative("easeCurve").animationCurveValue = AnimationCurve.EaseInOut(0, 0, 1, 1);

		// Reset all effect toggles
		string[] boolFields = {
			"typewriterEnabled", "gradientAnimEnabled", "fadeEnabled",
			"colorLerpEnabled", "scaleEnabled", "slideEnabled", "shakeEnabled",
			"outlinePulseEnabled", "shadowAnimEnabled", "fontSizeAnimEnabled",
			"letterSpacingAnimEnabled"
		};
		foreach (var f in boolFields)
			s.FindPropertyRelative(f).boolValue = false;

		// Set sensible defaults for value fields
		s.FindPropertyRelative("gradientStartAngle").floatValue = 0f;
		s.FindPropertyRelative("gradientEndAngle").floatValue = 360f;
		s.FindPropertyRelative("fadeFrom").floatValue = 0f;
		s.FindPropertyRelative("fadeTo").floatValue = 1f;
		s.FindPropertyRelative("colorFrom").colorValue = Color.white;
		s.FindPropertyRelative("colorTo").colorValue = Color.red;
		s.FindPropertyRelative("scaleFrom").vector3Value = Vector3.zero;
		s.FindPropertyRelative("scaleTo").vector3Value = Vector3.one;
		s.FindPropertyRelative("slideOffset").vector2Value = new Vector2(-100, 0);
		s.FindPropertyRelative("shakeIntensity").floatValue = 5f;
		s.FindPropertyRelative("outlineFrom").intValue = 0;
		s.FindPropertyRelative("outlineTo").intValue = 5;
		s.FindPropertyRelative("shadowOffsetFrom").vector2Value = Vector2.zero;
		s.FindPropertyRelative("shadowOffsetTo").vector2Value = new Vector2(3, 3);
		s.FindPropertyRelative("fontSizeFrom").intValue = 10;
		s.FindPropertyRelative("fontSizeTo").intValue = 40;
		s.FindPropertyRelative("letterSpacingFrom").intValue = 0;
		s.FindPropertyRelative("letterSpacingTo").intValue = 10;

		stepFoldouts.Add(true);
	}

	// ===================== Playback Controls =====================

	void DrawPlaybackControls() {
		if (targets.Length != 1) return;
		var animator = (HBTextAnimator)target;

		if (Application.isPlaying) {
			EditorGUILayout.BeginHorizontal();
			EditorGUI.BeginDisabledGroup(animator.IsPlaying);
			if (GUILayout.Button("Play", GUILayout.Height(28))) animator.Play();
			EditorGUI.EndDisabledGroup();
			EditorGUI.BeginDisabledGroup(!animator.IsPlaying);
			if (GUILayout.Button("Stop", GUILayout.Height(28))) animator.Stop();
			EditorGUI.EndDisabledGroup();
			EditorGUILayout.EndHorizontal();
		} else {
			EditorGUILayout.BeginHorizontal();
			EditorGUI.BeginDisabledGroup(_editorPlaying);
			if (GUILayout.Button("Preview", GUILayout.Height(28))) StartEditorPreview(animator);
			EditorGUI.EndDisabledGroup();
			EditorGUI.BeginDisabledGroup(!_editorPlaying);
			if (GUILayout.Button("Stop", GUILayout.Height(28))) StopEditorPreview();
			EditorGUI.EndDisabledGroup();
			EditorGUILayout.EndHorizontal();

			if (_editorPlaying) {
				float totalElapsed = (float)(EditorApplication.timeSinceStartup - _editorStartTime);
				float totalDur = animator.TotalDuration;
				float t = totalDur > 0 ? Mathf.Clamp01(totalElapsed / totalDur) : 1f;
				Rect r = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
				string label = string.Format("Step {0}/{1}  {2:F1}s / {3:F1}s",
					_previewStepIndex + 1, animator.StepCount, totalElapsed, totalDur);
				EditorGUI.ProgressBar(r, t, label);
			}
		}
	}

	// ===================== Editor Preview =====================

	void StartEditorPreview(HBTextAnimator animator) {
		if (_editorPlaying) StopEditorPreview();

		_previewTarget = animator;
		_editorPlaying = true;
		_editorStartTime = EditorApplication.timeSinceStartup;
		_previewStepIndex = 0;
		_previewStepStartTime = EditorApplication.timeSinceStartup;

		_previewTarget.TakeSnapshot();
		_previewTarget.CaptureStepBasePosition();
		EditorApplication.update += EditorUpdate;
	}

	void StopEditorPreview(bool restore = true) {
		if (!_editorPlaying) return;

		EditorApplication.update -= EditorUpdate;
		_editorPlaying = false;

		if (_previewTarget != null && restore)
			_previewTarget.RestoreSnapshot();
		_previewTarget = null;

		SceneView.RepaintAll();
		Repaint();
	}

	void EditorUpdate() {
		if (_previewTarget == null || !_editorPlaying) {
			StopEditorPreview();
			return;
		}

		var stps = _previewTarget.Steps;
		if (stps == null || stps.Count == 0) {
			StopEditorPreview();
			return;
		}

		// All steps completed
		if (_previewStepIndex >= stps.Count) {
			if (_previewTarget.Loop) {
				_previewTarget.RestoreSnapshot();
				_previewTarget.TakeSnapshot();
				_previewStepIndex = 0;
				_previewStepStartTime = EditorApplication.timeSinceStartup;
				_editorStartTime = EditorApplication.timeSinceStartup;
				_previewTarget.CaptureStepBasePosition();
			} else {
				StopEditorPreview(restore: false);
				return;
			}
		}

		var step = stps[_previewStepIndex];
		float stepElapsed = (float)(EditorApplication.timeSinceStartup - _previewStepStartTime);

		// During delay phase — don't apply anything yet
		if (stepElapsed < step.delay) {
			SceneView.RepaintAll();
			Repaint();
			return;
		}

		float animElapsed = stepElapsed - step.delay;
		float t = step.duration > 0 ? Mathf.Clamp01(animElapsed / step.duration) : 1f;

		_previewTarget.ApplyStepFrame(step, t);

		// Step completed
		if (animElapsed >= step.duration) {
			_previewTarget.ApplyStepFrame(step, 1f);
			_previewStepIndex++;
			_previewStepStartTime = EditorApplication.timeSinceStartup;
			if (_previewStepIndex < stps.Count)
				_previewTarget.CaptureStepBasePosition();
		}

		SceneView.RepaintAll();
		Repaint();
	}
}
#endif
