#if UNITY_EDITOR
using SkiaSharp.Unity.HB;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[CustomEditor(typeof(HBInputField))]
[CanEditMultipleObjects]
public class HBInputFieldEditor : Editor {

	// References
	static readonly GUIContent k_TextComponent = new GUIContent("Text Component", "HB_TEXTBlock for displaying input text.");
	static readonly GUIContent k_Placeholder = new GUIContent("Placeholder", "HB_TEXTBlock for placeholder text.");
	static readonly GUIContent k_TextViewport = new GUIContent("Text Viewport", "RectTransform with mask for clipping.");
	static readonly GUIContent k_CaretImage = new GUIContent("Caret Image", "Image used as the blinking cursor.");

	// Input
	static readonly GUIContent k_Text = new GUIContent("Text", "Current input text.");
	static readonly GUIContent k_CharLimit = new GUIContent("Character Limit", "Max characters (0 = unlimited).");
	static readonly GUIContent k_ContentType = new GUIContent("Content Type", "Validation and keyboard type.");
	static readonly GUIContent k_LineType = new GUIContent("Line Type", "Single or multi-line input.");
	static readonly GUIContent k_ReadOnly = new GUIContent("Read Only", "Prevents editing but allows selection/copy.");
	static readonly GUIContent k_AsteriskChar = new GUIContent("Asterisk Char", "Character used for password masking.");

	// Text settings
	static readonly GUIContent k_FontAsset = new GUIContent("Font", "Font asset (HBFontData or TextAsset .ttf/.otf).");
	static readonly GUIContent k_FontSize = new GUIContent("Font Size", "Font size in pixels.");
	static readonly GUIContent k_FontColor = new GUIContent("Font Color", "Text color.");
	static readonly GUIContent k_Alignment = new GUIContent("Alignment", "Text horizontal alignment.");
	static readonly GUIContent k_RichText = new GUIContent("Rich Text", "Enable rich text tags (<b>, <i>, <color>, etc.).");

	// Placeholder
	static readonly GUIContent k_PlaceholderText = new GUIContent("Placeholder Text", "Text shown when input is empty.");
	static readonly GUIContent k_PlaceholderColor = new GUIContent("Placeholder Color", "Color of the placeholder text.");

	// Behavior
	static readonly GUIContent k_SelectAllOnFocus = new GUIContent("Select All on Focus", "Select all text when the field gains focus.");
	static readonly GUIContent k_TabNavigation = new GUIContent("Tab Navigation", "Press Tab/Shift+Tab to move between fields.");

	// Read-only appearance
	static readonly GUIContent k_ReadOnlyColor = new GUIContent("Background", "Background color when read-only.");
	static readonly GUIContent k_ReadOnlyFontColor = new GUIContent("Font Color", "Text color when read-only.");

	// Focus appearance
	static readonly GUIContent k_UseFocusColor = new GUIContent("Highlight on Focus", "Change background color when focused.");
	static readonly GUIContent k_FocusBgColor = new GUIContent("Focus Color", "Background color when focused.");

	// Caret
	static readonly GUIContent k_HideCaret = new GUIContent("Hide Caret", "Completely hide the blinking caret.");
	static readonly GUIContent k_CaretColor = new GUIContent("Color");
	static readonly GUIContent k_CaretBlinkRate = new GUIContent("Blink Rate", "Seconds per blink cycle.");
	static readonly GUIContent k_CaretWidth = new GUIContent("Width", "Width in pixels.");
	static readonly GUIContent k_SelectionColor = new GUIContent("Selection Color", "Highlight color for selected text.");

	// Auto resize
	static readonly GUIContent k_AutoResize = new GUIContent("Auto Resize", "Automatically grow height to fit multiline text.");
	static readonly GUIContent k_MinHeight = new GUIContent("Min Height", "Minimum input field height.");
	static readonly GUIContent k_MaxAutoHeight = new GUIContent("Max Height", "Maximum height when auto-resizing.");

	// Mobile
	static readonly GUIContent k_HideMobileInput = new GUIContent("Hide Mobile Input", "Hide the native input field on mobile. Only uses the virtual keyboard.");

	// Properties
	SerializedProperty textComponentProp, placeholderProp, textViewportProp, caretImageProp;
	SerializedProperty textProp, charLimitProp, contentTypeProp, lineTypeProp, readOnlyProp, asteriskProp;
	SerializedProperty fontAssetProp, fontSizeProp, fontColorProp, boldProp, italicProp, alignmentProp, textDirectionProp, richTextProp;
	SerializedProperty placeholderTextProp, placeholderColorProp;
	SerializedProperty selectAllOnFocusProp, tabNavigationProp;
	SerializedProperty readOnlyColorProp, readOnlyFontColorProp;
	SerializedProperty useFocusColorProp, focusBgColorProp;
	SerializedProperty hideMobileInputProp;
	SerializedProperty hideCaretProp, caretColorProp, caretBlinkProp, caretWidthProp, selectionColorProp;
	SerializedProperty autoResizeProp, minHeightProp, maxAutoHeightProp;
	SerializedProperty skiaBackgroundProp, trackpadSensitivityProp;
	SerializedProperty onValueChangedProp, onSubmitProp, onEndEditProp, onFocusProp, onUnfocusProp;

	void OnEnable() {
		textComponentProp = serializedObject.FindProperty("textComponent");
		placeholderProp = serializedObject.FindProperty("placeholder");
		textViewportProp = serializedObject.FindProperty("textViewport");
		caretImageProp = serializedObject.FindProperty("caretImage");

		textProp = serializedObject.FindProperty("m_Text");
		charLimitProp = serializedObject.FindProperty("characterLimit");
		contentTypeProp = serializedObject.FindProperty("contentType");
		lineTypeProp = serializedObject.FindProperty("lineType");
		readOnlyProp = serializedObject.FindProperty("readOnly");
		asteriskProp = serializedObject.FindProperty("asteriskChar");

		fontAssetProp = serializedObject.FindProperty("fontAsset");
		fontSizeProp = serializedObject.FindProperty("fontSize");
		fontColorProp = serializedObject.FindProperty("fontColor");
		boldProp = serializedObject.FindProperty("bold");
		italicProp = serializedObject.FindProperty("italic");
		alignmentProp = serializedObject.FindProperty("textAlignment");
		textDirectionProp = serializedObject.FindProperty("textDirection");
		richTextProp = serializedObject.FindProperty("richText");

		placeholderTextProp = serializedObject.FindProperty("placeholderText");
		placeholderColorProp = serializedObject.FindProperty("placeholderColor");

		selectAllOnFocusProp = serializedObject.FindProperty("selectAllOnFocus");
		tabNavigationProp = serializedObject.FindProperty("tabNavigation");

		readOnlyColorProp = serializedObject.FindProperty("readOnlyColor");
		readOnlyFontColorProp = serializedObject.FindProperty("readOnlyFontColor");

		useFocusColorProp = serializedObject.FindProperty("useFocusColor");
		focusBgColorProp = serializedObject.FindProperty("focusBackgroundColor");

		hideMobileInputProp = serializedObject.FindProperty("hideMobileInput");

		hideCaretProp = serializedObject.FindProperty("hideCaret");
		caretColorProp = serializedObject.FindProperty("caretColor");
		caretBlinkProp = serializedObject.FindProperty("caretBlinkRate");
		caretWidthProp = serializedObject.FindProperty("caretWidth");
		selectionColorProp = serializedObject.FindProperty("selectionColor");

		autoResizeProp = serializedObject.FindProperty("autoResize");
		minHeightProp = serializedObject.FindProperty("minHeight");
		maxAutoHeightProp = serializedObject.FindProperty("maxAutoHeight");

		skiaBackgroundProp = serializedObject.FindProperty("skiaBackground");
		trackpadSensitivityProp = serializedObject.FindProperty("trackpadSensitivity");

		onValueChangedProp = serializedObject.FindProperty("onValueChanged");
		onSubmitProp = serializedObject.FindProperty("onSubmit");
		onEndEditProp = serializedObject.FindProperty("onEndEdit");
		onFocusProp = serializedObject.FindProperty("onFocus");
		onUnfocusProp = serializedObject.FindProperty("onUnfocus");
	}

	public override void OnInspectorGUI() {
		serializedObject.Update();

		// --- References ---
		EditorGUILayout.LabelField("References", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(textComponentProp, k_TextComponent);
		EditorGUILayout.PropertyField(placeholderProp, k_Placeholder);
		EditorGUILayout.PropertyField(textViewportProp, k_TextViewport);
		EditorGUILayout.PropertyField(caretImageProp, k_CaretImage);
		EditorGUILayout.PropertyField(skiaBackgroundProp, new GUIContent("Skia Background", "Optional SkiaGraphic for background (replaces Image)."));
		EditorGUILayout.Space();

		// --- Input Settings ---
		EditorGUILayout.LabelField("Input Settings", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(textProp, k_Text);
		EditorGUILayout.PropertyField(charLimitProp, k_CharLimit);
		EditorGUILayout.PropertyField(contentTypeProp, k_ContentType);
		EditorGUILayout.PropertyField(lineTypeProp, k_LineType);
		EditorGUILayout.PropertyField(readOnlyProp, k_ReadOnly);
		if (contentTypeProp.enumValueIndex == (int)HBInputField.ContentType.Password)
			EditorGUILayout.PropertyField(asteriskProp, k_AsteriskChar);
		EditorGUILayout.Space();

		// --- Text Settings ---
		EditorGUILayout.LabelField("Text Settings", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(fontAssetProp, k_FontAsset);
		EditorGUILayout.PropertyField(fontSizeProp, k_FontSize);
		EditorGUILayout.PropertyField(fontColorProp, k_FontColor);

		EditorGUILayout.BeginHorizontal();
		EditorGUILayout.PrefixLabel("Style");
		boldProp.boolValue = GUILayout.Toggle(boldProp.boolValue, "B", EditorStyles.miniButtonLeft, GUILayout.Width(30));
		italicProp.boolValue = GUILayout.Toggle(italicProp.boolValue, "I", EditorStyles.miniButtonRight, GUILayout.Width(30));
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.PropertyField(alignmentProp, k_Alignment);
		EditorGUILayout.PropertyField(textDirectionProp, new GUIContent("Text Direction", "Auto, LTR, or RTL. Use RTL for Arabic/Hebrew."));
		EditorGUILayout.PropertyField(richTextProp, k_RichText);
		EditorGUILayout.Space();

		// --- Placeholder ---
		EditorGUILayout.LabelField("Placeholder", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(placeholderTextProp, k_PlaceholderText);
		EditorGUILayout.PropertyField(placeholderColorProp, k_PlaceholderColor);
		EditorGUILayout.Space();

		// --- Behavior ---
		EditorGUILayout.LabelField("Behavior", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(selectAllOnFocusProp, k_SelectAllOnFocus);
		EditorGUILayout.PropertyField(tabNavigationProp, k_TabNavigation);
		EditorGUILayout.Space();

		// --- Appearance ---
		EditorGUILayout.LabelField("Appearance", EditorStyles.boldLabel);

		if (readOnlyProp.boolValue) {
			EditorGUILayout.LabelField("Read-Only Colors", EditorStyles.miniLabel);
			EditorGUI.indentLevel++;
			EditorGUILayout.PropertyField(readOnlyColorProp, k_ReadOnlyColor);
			EditorGUILayout.PropertyField(readOnlyFontColorProp, k_ReadOnlyFontColor);
			EditorGUI.indentLevel--;
		}

		EditorGUILayout.PropertyField(useFocusColorProp, k_UseFocusColor);
		if (useFocusColorProp.boolValue) {
			EditorGUI.indentLevel++;
			EditorGUILayout.PropertyField(focusBgColorProp, k_FocusBgColor);
			EditorGUI.indentLevel--;
		}
		EditorGUILayout.Space();

		// --- Auto Resize (multiline only) ---
		if (lineTypeProp.enumValueIndex == (int)HBInputField.LineType.MultiLine) {
			EditorGUILayout.LabelField("Auto Resize", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(autoResizeProp, k_AutoResize);
			if (autoResizeProp.boolValue) {
				EditorGUI.indentLevel++;
				EditorGUILayout.PropertyField(minHeightProp, k_MinHeight);
				EditorGUILayout.PropertyField(maxAutoHeightProp, k_MaxAutoHeight);
				EditorGUI.indentLevel--;
			}
			EditorGUILayout.Space();
		}

		// --- Mobile ---
		EditorGUILayout.LabelField("Mobile", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(hideMobileInputProp, k_HideMobileInput);
		EditorGUILayout.PropertyField(trackpadSensitivityProp, new GUIContent("Trackpad Sensitivity", "Pixels per character when dragging to move caret."));
		EditorGUILayout.Space();

		// --- Caret ---
		EditorGUILayout.LabelField("Caret", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(hideCaretProp, k_HideCaret);
		if (!hideCaretProp.boolValue) {
			EditorGUI.indentLevel++;
			EditorGUILayout.PropertyField(caretColorProp, k_CaretColor);
			EditorGUILayout.PropertyField(caretBlinkProp, k_CaretBlinkRate);
			EditorGUILayout.PropertyField(caretWidthProp, k_CaretWidth);
			EditorGUI.indentLevel--;
		}
		EditorGUILayout.PropertyField(selectionColorProp, k_SelectionColor);
		EditorGUILayout.Space();

		// --- Events ---
		EditorGUILayout.LabelField("Events", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(onValueChangedProp);
		EditorGUILayout.PropertyField(onSubmitProp);
		EditorGUILayout.PropertyField(onEndEditProp);
		EditorGUILayout.PropertyField(onFocusProp);
		EditorGUILayout.PropertyField(onUnfocusProp);

		serializedObject.ApplyModifiedProperties();
	}
}
#endif
