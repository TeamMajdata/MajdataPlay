#if UNITY_EDITOR

using SkiaSharp.Unity;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SkiaButton))]
[CanEditMultipleObjects]
public class SkiaButtonEditor : Editor {

	SerializedProperty background;
	SerializedProperty label;
	SerializedProperty text;
	SerializedProperty normalColor;
	SerializedProperty hoverColor;
	SerializedProperty pressedColor;
	SerializedProperty disabledColor;
	SerializedProperty normalTextColor;
	SerializedProperty disabledTextColor;
	SerializedProperty onClick;

	void OnEnable() {
		background = serializedObject.FindProperty("background");
		label = serializedObject.FindProperty("label");
		text = serializedObject.FindProperty("text");
		normalColor = serializedObject.FindProperty("normalColor");
		hoverColor = serializedObject.FindProperty("hoverColor");
		pressedColor = serializedObject.FindProperty("pressedColor");
		disabledColor = serializedObject.FindProperty("disabledColor");
		normalTextColor = serializedObject.FindProperty("normalTextColor");
		disabledTextColor = serializedObject.FindProperty("disabledTextColor");
		onClick = serializedObject.FindProperty("onClick");
	}

	public override void OnInspectorGUI() {
		serializedObject.Update();

		EditorGUILayout.LabelField("References", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(background);
		EditorGUILayout.PropertyField(label);

		EditorGUILayout.Space();
		EditorGUILayout.LabelField("Label", EditorStyles.boldLabel);

		EditorGUI.BeginChangeCheck();
		EditorGUILayout.PropertyField(text);
		bool textChanged = EditorGUI.EndChangeCheck();

		EditorGUILayout.Space();
		EditorGUILayout.LabelField("Colors", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(normalColor);
		EditorGUILayout.PropertyField(hoverColor);
		EditorGUILayout.PropertyField(pressedColor);
		EditorGUILayout.PropertyField(disabledColor);
		EditorGUILayout.PropertyField(normalTextColor);
		EditorGUILayout.PropertyField(disabledTextColor);

		EditorGUILayout.Space();
		EditorGUILayout.LabelField("Events", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(onClick);

		serializedObject.ApplyModifiedProperties();

		// Sync text to label immediately when changed in inspector
		if (textChanged) {
			foreach (var t in targets) {
				var button = (SkiaButton)t;
				if (button.Label != null) {
					Undo.RecordObject(button.Label, "Change Button Text");
					button.Label.text = button.Text;
					EditorUtility.SetDirty(button.Label);
				}
			}
		}
	}
}

#endif
