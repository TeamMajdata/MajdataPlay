#if UNITY_EDITOR

using SkiaSharp.Unity;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(SkiaDropdown))]
[CanEditMultipleObjects]
public class SkiaDropdownEditor : Editor {

	SerializedProperty background, captionText, captionIcon;
	SerializedProperty arrow;
	SerializedProperty displayMode;
	SerializedProperty itemHeight, itemSpacing, itemCornerRadius, panelPadding, maxDropdownHeight, minDropdownWidth, iconSize, dropdownCornerRadii;
	SerializedProperty m_Value, m_Options;
	SerializedProperty dropdownColor, panelColor, itemNormalColor, itemHoverColor, itemSelectedColor;
	SerializedProperty itemTextColor, itemSelectedTextColor, disabledColor, overlayColor;
	SerializedProperty onValueChanged;

	ReorderableList optionsList;

	void OnEnable() {
		background = serializedObject.FindProperty("background");
		captionText = serializedObject.FindProperty("captionText");
		captionIcon = serializedObject.FindProperty("captionIcon");
		arrow = serializedObject.FindProperty("arrow");
		displayMode = serializedObject.FindProperty("displayMode");
		itemHeight = serializedObject.FindProperty("itemHeight");
		itemSpacing = serializedObject.FindProperty("itemSpacing");
		itemCornerRadius = serializedObject.FindProperty("itemCornerRadius");
		panelPadding = serializedObject.FindProperty("panelPadding");
		maxDropdownHeight = serializedObject.FindProperty("maxDropdownHeight");
		minDropdownWidth = serializedObject.FindProperty("minDropdownWidth");
		iconSize = serializedObject.FindProperty("iconSize");
		dropdownCornerRadii = serializedObject.FindProperty("dropdownCornerRadii");
		m_Value = serializedObject.FindProperty("m_Value");
		m_Options = serializedObject.FindProperty("m_Options");
		dropdownColor = serializedObject.FindProperty("dropdownColor");
		panelColor = serializedObject.FindProperty("panelColor");
		itemNormalColor = serializedObject.FindProperty("itemNormalColor");
		itemHoverColor = serializedObject.FindProperty("itemHoverColor");
		itemSelectedColor = serializedObject.FindProperty("itemSelectedColor");
		itemTextColor = serializedObject.FindProperty("itemTextColor");
		itemSelectedTextColor = serializedObject.FindProperty("itemSelectedTextColor");
		disabledColor = serializedObject.FindProperty("disabledColor");
		overlayColor = serializedObject.FindProperty("overlayColor");
		onValueChanged = serializedObject.FindProperty("onValueChanged");

		optionsList = new ReorderableList(serializedObject, m_Options, true, true, true, true);
		optionsList.drawHeaderCallback = rect => {
			EditorGUI.LabelField(rect, "Options");
		};
		optionsList.elementHeight = EditorGUIUtility.singleLineHeight * 2 + 6;
		optionsList.drawElementCallback = (rect, index, isActive, isFocused) => {
			var element = m_Options.GetArrayElementAtIndex(index);
			var textProp = element.FindPropertyRelative("text");
			var iconProp = element.FindPropertyRelative("icon");
			rect.y += 2;
			float lineH = EditorGUIUtility.singleLineHeight;

			string label = index == m_Value.intValue ? $"[{index}] (selected)" : $"[{index}]";
			var textRect = new Rect(rect.x, rect.y, rect.width, lineH);
			EditorGUI.PropertyField(textRect, textProp, new GUIContent(label));

			var iconRect = new Rect(rect.x, rect.y + lineH + 2, rect.width, lineH);
			EditorGUI.PropertyField(iconRect, iconProp, new GUIContent("    Icon"));
		};
		optionsList.onAddCallback = list => {
			int index = list.serializedProperty.arraySize;
			list.serializedProperty.arraySize++;
			var element = list.serializedProperty.GetArrayElementAtIndex(index);
			element.FindPropertyRelative("text").stringValue = $"Option {index + 1}";
		};
	}

	public override void OnInspectorGUI() {
		serializedObject.Update();

		EditorGUILayout.LabelField("References", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(background);
		EditorGUILayout.PropertyField(captionText);
		EditorGUILayout.PropertyField(captionIcon, new GUIContent("Caption Icon (optional)"));
		EditorGUILayout.PropertyField(arrow);

		EditorGUILayout.Space();
		EditorGUILayout.LabelField("Display", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(displayMode, new GUIContent("Display Mode"));

		EditorGUILayout.Space();
		EditorGUILayout.LabelField("Template", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(itemHeight);
		EditorGUILayout.PropertyField(itemSpacing);
		EditorGUILayout.PropertyField(itemCornerRadius);
		EditorGUILayout.PropertyField(panelPadding);
		EditorGUILayout.PropertyField(maxDropdownHeight);
		EditorGUILayout.PropertyField(minDropdownWidth, new GUIContent("Min Menu Width"));
		EditorGUILayout.PropertyField(iconSize);
		EditorGUILayout.PropertyField(dropdownCornerRadii);

		EditorGUILayout.Space();
		EditorGUILayout.LabelField("State", EditorStyles.boldLabel);

		EditorGUI.BeginChangeCheck();
		EditorGUILayout.PropertyField(m_Value);
		bool valueChanged = EditorGUI.EndChangeCheck();

		EditorGUILayout.Space();
		optionsList.DoLayoutList();

		EditorGUILayout.Space();
		EditorGUILayout.LabelField("Colors", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(dropdownColor);
		EditorGUILayout.PropertyField(panelColor);
		EditorGUILayout.PropertyField(itemNormalColor);
		EditorGUILayout.PropertyField(itemHoverColor);
		EditorGUILayout.PropertyField(itemSelectedColor);
		EditorGUILayout.PropertyField(itemTextColor);
		EditorGUILayout.PropertyField(itemSelectedTextColor);
		EditorGUILayout.PropertyField(disabledColor);
		EditorGUILayout.PropertyField(overlayColor);

		EditorGUILayout.Space();
		EditorGUILayout.LabelField("Events", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(onValueChanged);

		serializedObject.ApplyModifiedProperties();

		if (valueChanged || GUI.changed) {
			foreach (var t in targets) {
				var dropdown = (SkiaDropdown)t;
				if (dropdown.CaptionText != null && dropdown.options.Count > 0) {
					int idx = Mathf.Clamp(dropdown.value, 0, dropdown.options.Count - 1);
					Undo.RecordObject(dropdown.CaptionText, "Update Dropdown Caption");
					dropdown.CaptionText.text = dropdown.options[idx].text;
					EditorUtility.SetDirty(dropdown.CaptionText);
				}
			}
		}
	}
}

#endif
