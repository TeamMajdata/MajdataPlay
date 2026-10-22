#if UNITY_EDITOR

using SkiaSharp.Unity;
using SkiaSharp.Unity.HB;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

public static class SkiaUICreators {

	// ===================== Helpers =====================

	static (Canvas canvas, Transform parent) EnsureCanvas(MenuCommand menuCommand) {
		GameObject parentObj = menuCommand.context as GameObject;
		Canvas canvas = parentObj != null ? parentObj.GetComponentInParent<Canvas>() : null;

		if (canvas == null)
			canvas = Object.FindObjectOfType<Canvas>();

		if (canvas == null) {
			var canvasGO = new GameObject("Canvas");
			canvas = canvasGO.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvasGO.AddComponent<CanvasScaler>();
			canvasGO.AddComponent<GraphicRaycaster>();
			Undo.RegisterCreatedObjectUndo(canvasGO, "Create Canvas");

			if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null) {
				var esGO = new GameObject("EventSystem");
				esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
				esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
				Undo.RegisterCreatedObjectUndo(esGO, "Create EventSystem");
			}
		}

		Transform parentTransform = parentObj != null && parentObj.GetComponentInParent<Canvas>() != null
			? parentObj.transform
			: canvas.transform;

		return (canvas, parentTransform);
	}

	// ===================== Skia Button =====================

	[MenuItem("GameObject/Skia UI (Canvas)/Skia Button", false, 12)]
	static void CreateSkiaButton(MenuCommand menuCommand) {
		var (canvas, parentTransform) = EnsureCanvas(menuCommand);

		// Root
		var root = new GameObject("Skia Button");
		var rootRT = root.AddComponent<RectTransform>();
		GameObjectUtility.SetParentAndAlign(root, parentTransform.gameObject);
		rootRT.anchorMin = new Vector2(0.5f, 0.5f);
		rootRT.anchorMax = new Vector2(0.5f, 0.5f);
		rootRT.anchoredPosition = Vector2.zero;
		rootRT.sizeDelta = new Vector2(300, 100);

		// Background (SkiaGraphic — render child auto-created)
		var bg = root.AddComponent<SkiaGraphic>();

		// Label (HB_TEXTBlock needs RawImage for its own rendering)
		var labelGO = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
		labelGO.transform.SetParent(root.transform, false);
		var label = labelGO.AddComponent<HB_TEXTBlock>();
		var labelRT = labelGO.GetComponent<RectTransform>();
		labelRT.anchorMin = Vector2.zero;
		labelRT.anchorMax = Vector2.one;
		labelRT.offsetMin = new Vector2(8, 4);
		labelRT.offsetMax = new Vector2(-8, -4);

		// SkiaButton component
		var button = root.AddComponent<SkiaButton>();
		Undo.RegisterCreatedObjectUndo(root, "Create Skia Button");

		// Wire references
		var so = new SerializedObject(button);
		so.FindProperty("background").objectReferenceValue = bg;
		so.FindProperty("label").objectReferenceValue = label;
		so.ApplyModifiedPropertiesWithoutUndo();

		// Configure background
		var bgSO = new SerializedObject(bg);
		bgSO.FindProperty("shape").enumValueIndex = 1; // RoundedRect
		bgSO.FindProperty("cornerRadii").vector4Value = new Vector4(12, 12, 12, 12);
		bgSO.FindProperty("fillType").enumValueIndex = 1; // Solid
		bgSO.FindProperty("fillColor").colorValue = new Color(0.24f, 0.48f, 0.9f, 1f);
		bgSO.ApplyModifiedPropertiesWithoutUndo();

		// Configure label
		var labelSO = new SerializedObject(label);
		labelSO.FindProperty("Text").stringValue = "Button";
		labelSO.FindProperty("fontColor").colorValue = Color.white;
		labelSO.FindProperty("fontSize").intValue = 28;
		labelSO.FindProperty("textAlignment").enumValueIndex = 2; // Center
		labelSO.FindProperty("verticalAlignment").enumValueIndex = 1; // Middle
		labelSO.FindProperty("maxWidth").floatValue = 284f;
		labelSO.ApplyModifiedPropertiesWithoutUndo();

		Selection.activeGameObject = root;
	}

	// ===================== Skia Toggle =====================

	[MenuItem("GameObject/Skia UI (Canvas)/Skia Toggle", false, 13)]
	static void CreateSkiaToggle(MenuCommand menuCommand) {
		var (canvas, parentTransform) = EnsureCanvas(menuCommand);

		// Root (wide enough for switch + label)
		var root = new GameObject("Skia Toggle");
		var rootRT = root.AddComponent<RectTransform>();
		GameObjectUtility.SetParentAndAlign(root, parentTransform.gameObject);
		rootRT.anchorMin = new Vector2(0.5f, 0.5f);
		rootRT.anchorMax = new Vector2(0.5f, 0.5f);
		rootRT.anchoredPosition = Vector2.zero;
		rootRT.sizeDelta = new Vector2(350, 96);

		// Track background — iOS pill shape (180x96)
		var trackGO = new GameObject("Track", typeof(RectTransform));
		trackGO.transform.SetParent(root.transform, false);
		var trackBG = trackGO.AddComponent<SkiaGraphic>();
		var trackRT = trackGO.GetComponent<RectTransform>();
		trackRT.anchorMin = new Vector2(0, 0.5f);
		trackRT.anchorMax = new Vector2(0, 0.5f);
		trackRT.pivot = new Vector2(0, 0.5f);
		trackRT.anchoredPosition = new Vector2(0, 0);
		trackRT.sizeDelta = new Vector2(180, 96);

		// Status text (ON/OFF) inside the track
		var statusGO = new GameObject("StatusText", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
		statusGO.transform.SetParent(trackGO.transform, false);
		var statusTxt = statusGO.AddComponent<HB_TEXTBlock>();
		var statusRT = statusGO.GetComponent<RectTransform>();
		statusRT.anchorMin = new Vector2(0, 0.5f);
		statusRT.anchorMax = new Vector2(0, 0.5f);
		statusRT.pivot = new Vector2(0.5f, 0.5f);
		statusRT.anchoredPosition = new Vector2(135, 0); // right side (off state, opposite of knob)
		statusRT.sizeDelta = new Vector2(70, 60);

		// Knob — white circle inside the track
		var knobGO = new GameObject("Knob", typeof(RectTransform));
		knobGO.transform.SetParent(trackGO.transform, false);
		var knobBG = knobGO.AddComponent<SkiaGraphic>();
		var knobRT = knobGO.GetComponent<RectTransform>();
		knobRT.anchorMin = new Vector2(0, 0.5f);
		knobRT.anchorMax = new Vector2(0, 0.5f);
		knobRT.pivot = new Vector2(0.5f, 0.5f);
		// Start at off position (left side)
		knobRT.anchoredPosition = new Vector2(48, 0);
		knobRT.sizeDelta = new Vector2(78, 78);

		// Label
		var labelGO = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
		labelGO.transform.SetParent(root.transform, false);
		var label = labelGO.AddComponent<HB_TEXTBlock>();
		var labelRT = labelGO.GetComponent<RectTransform>();
		labelRT.anchorMin = new Vector2(0, 0);
		labelRT.anchorMax = new Vector2(1, 1);
		labelRT.offsetMin = new Vector2(192, 0);
		labelRT.offsetMax = new Vector2(0, 0);

		// SkiaToggle component
		var toggle = root.AddComponent<SkiaToggle>();
		Undo.RegisterCreatedObjectUndo(root, "Create Skia Toggle");

		// Wire references
		var so = new SerializedObject(toggle);
		so.FindProperty("background").objectReferenceValue = trackBG;
		so.FindProperty("knob").objectReferenceValue = knobBG;
		so.FindProperty("label").objectReferenceValue = label;
		so.FindProperty("statusText").objectReferenceValue = statusTxt;
		so.FindProperty("offColor").colorValue = new Color(0.85f, 0.25f, 0.22f, 1f);
		so.FindProperty("onColor").colorValue = new Color(0.20f, 0.78f, 0.35f, 1f);
		so.FindProperty("onText").stringValue = "ON";
		so.FindProperty("offText").stringValue = "OFF";
		so.FindProperty("statusTextColor").colorValue = Color.white;
		so.ApplyModifiedPropertiesWithoutUndo();

		// Configure track — pill shape, red (off), glassy with gradient + inner shadow + stroke
		var trackSO = new SerializedObject(trackBG);
		trackSO.FindProperty("shape").enumValueIndex = 1; // RoundedRect
		trackSO.FindProperty("cornerRadii").vector4Value = new Vector4(48, 48, 48, 48);
		trackSO.FindProperty("fillType").enumValueIndex = 2; // LinearGradient
		// Gradient: darker at bottom, lighter at top for glass
		var trackGrad = trackSO.FindProperty("gradient");
		// We'll set solid color; gradient configured at runtime by SkiaToggle
		trackSO.FindProperty("fillType").enumValueIndex = 1; // Solid
		trackSO.FindProperty("fillColor").colorValue = new Color(0.85f, 0.25f, 0.22f, 1f);
		// Inner shadow — top highlight for glass
		trackSO.FindProperty("enableInnerShadow").boolValue = true;
		trackSO.FindProperty("innerShadowColor").colorValue = new Color(1f, 1f, 1f, 0.35f);
		trackSO.FindProperty("innerShadowOffset").vector2Value = new Vector2(0, 4);
		trackSO.FindProperty("innerShadowBlur").floatValue = 8f;
		// Outer shadow for depth
		trackSO.FindProperty("enableShadow").boolValue = true;
		trackSO.FindProperty("shadowColor").colorValue = new Color(0, 0, 0, 0.25f);
		trackSO.FindProperty("shadowOffset").vector2Value = new Vector2(0, 3);
		trackSO.FindProperty("shadowBlur").floatValue = 6f;
		// Subtle stroke for glass edge
		trackSO.FindProperty("enableStroke").boolValue = true;
		trackSO.FindProperty("strokeColor").colorValue = new Color(1f, 1f, 1f, 0.15f);
		trackSO.FindProperty("strokeWidth").floatValue = 1.5f;
		trackSO.ApplyModifiedPropertiesWithoutUndo();

		// Configure knob — white glass circle
		var knobSO = new SerializedObject(knobBG);
		knobSO.FindProperty("shape").enumValueIndex = 2; // Circle
		knobSO.FindProperty("fillType").enumValueIndex = 1; // Solid
		knobSO.FindProperty("fillColor").colorValue = Color.white;
		// Drop shadow
		knobSO.FindProperty("enableShadow").boolValue = true;
		knobSO.FindProperty("shadowColor").colorValue = new Color(0, 0, 0, 0.3f);
		knobSO.FindProperty("shadowOffset").vector2Value = new Vector2(0, 3);
		knobSO.FindProperty("shadowBlur").floatValue = 6f;
		// Glass highlight — bright inner shadow at top
		knobSO.FindProperty("enableInnerShadow").boolValue = true;
		knobSO.FindProperty("innerShadowColor").colorValue = new Color(1f, 1f, 1f, 0.6f);
		knobSO.FindProperty("innerShadowOffset").vector2Value = new Vector2(0, 3);
		knobSO.FindProperty("innerShadowBlur").floatValue = 4f;
		// Glass edge stroke
		knobSO.FindProperty("enableStroke").boolValue = true;
		knobSO.FindProperty("strokeColor").colorValue = new Color(0.9f, 0.9f, 0.9f, 0.5f);
		knobSO.FindProperty("strokeWidth").floatValue = 1f;
		knobSO.ApplyModifiedPropertiesWithoutUndo();

		// Configure status text (ON/OFF)
		var statusSO = new SerializedObject(statusTxt);
		statusSO.FindProperty("Text").stringValue = "OFF";
		statusSO.FindProperty("fontColor").colorValue = Color.white;
		statusSO.FindProperty("fontSize").intValue = 24;
		statusSO.FindProperty("bold").boolValue = true;
		statusSO.FindProperty("textAlignment").enumValueIndex = 2; // Center
		statusSO.FindProperty("verticalAlignment").enumValueIndex = 1; // Middle
		statusSO.FindProperty("maxWidth").floatValue = 70f;
		statusSO.ApplyModifiedPropertiesWithoutUndo();

		// Configure label
		var labelSO = new SerializedObject(label);
		labelSO.FindProperty("Text").stringValue = "Toggle";
		labelSO.FindProperty("fontColor").colorValue = Color.black;
		labelSO.FindProperty("fontSize").intValue = 18;
		labelSO.FindProperty("textAlignment").enumValueIndex = 1; // Left
		labelSO.FindProperty("verticalAlignment").enumValueIndex = 1; // Middle
		labelSO.FindProperty("maxWidth").floatValue = 132f;
		labelSO.ApplyModifiedPropertiesWithoutUndo();

		Selection.activeGameObject = root;
	}

	// ===================== Skia Dropdown =====================

	[MenuItem("GameObject/Skia UI (Canvas)/Skia Dropdown", false, 14)]
	static void CreateSkiaDropdown(MenuCommand menuCommand) {
		var (canvas, parentTransform) = EnsureCanvas(menuCommand);

		// Root
		var root = new GameObject("Skia Dropdown");
		var rootRT = root.AddComponent<RectTransform>();
		GameObjectUtility.SetParentAndAlign(root, parentTransform.gameObject);
		rootRT.anchorMin = new Vector2(0.5f, 0.5f);
		rootRT.anchorMax = new Vector2(0.5f, 0.5f);
		rootRT.anchoredPosition = Vector2.zero;
		rootRT.sizeDelta = new Vector2(600, 120);

		// Background (SkiaGraphic)
		var bg = root.AddComponent<SkiaGraphic>();

		// Caption label
		var captionGO = new GameObject("Caption", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
		captionGO.transform.SetParent(root.transform, false);
		var caption = captionGO.AddComponent<HB_TEXTBlock>();
		var captionRT = captionGO.GetComponent<RectTransform>();
		captionRT.anchorMin = Vector2.zero;
		captionRT.anchorMax = Vector2.one;
		captionRT.offsetMin = new Vector2(36, 12);
		captionRT.offsetMax = new Vector2(-108, -12);

		// Arrow indicator (▼ text)
		var arrowGO = new GameObject("Arrow", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
		arrowGO.transform.SetParent(root.transform, false);
		var arrowText = arrowGO.AddComponent<HB_TEXTBlock>();
		var arrowRT = arrowGO.GetComponent<RectTransform>();
		arrowRT.anchorMin = new Vector2(1, 0.5f);
		arrowRT.anchorMax = new Vector2(1, 0.5f);
		arrowRT.pivot = new Vector2(0.5f, 0.5f);
		arrowRT.anchoredPosition = new Vector2(-50, 0);
		arrowRT.sizeDelta = new Vector2(60, 60);

		// SkiaDropdown component
		var dropdown = root.AddComponent<SkiaDropdown>();
		Undo.RegisterCreatedObjectUndo(root, "Create Skia Dropdown");

		// Wire references
		var so = new SerializedObject(dropdown);
		so.FindProperty("background").objectReferenceValue = bg;
		so.FindProperty("captionText").objectReferenceValue = caption;
		so.FindProperty("arrow").objectReferenceValue = arrowRT;

		// Add default options
		var optionsProp = so.FindProperty("m_Options");
		optionsProp.arraySize = 3;
		optionsProp.GetArrayElementAtIndex(0).FindPropertyRelative("text").stringValue = "Option A";
		optionsProp.GetArrayElementAtIndex(1).FindPropertyRelative("text").stringValue = "Option B";
		optionsProp.GetArrayElementAtIndex(2).FindPropertyRelative("text").stringValue = "Option C";
		so.ApplyModifiedPropertiesWithoutUndo();

		// Configure background
		var bgSO = new SerializedObject(bg);
		bgSO.FindProperty("shape").enumValueIndex = 1; // RoundedRect
		bgSO.FindProperty("cornerRadii").vector4Value = new Vector4(30, 30, 30, 30);
		bgSO.FindProperty("fillType").enumValueIndex = 1; // Solid
		bgSO.FindProperty("fillColor").colorValue = Color.white;
		bgSO.FindProperty("enableStroke").boolValue = true;
		bgSO.FindProperty("strokeColor").colorValue = new Color(0.8f, 0.8f, 0.8f, 1f);
		bgSO.FindProperty("strokeWidth").floatValue = 3f;
		bgSO.ApplyModifiedPropertiesWithoutUndo();

		// Configure dropdown template sizes
		so = new SerializedObject(dropdown);
		so.FindProperty("itemHeight").floatValue = 120f;
		so.FindProperty("maxDropdownHeight").floatValue = 600f;
		so.FindProperty("dropdownCornerRadii").vector4Value = new Vector4(24, 24, 24, 24);
		so.ApplyModifiedPropertiesWithoutUndo();

		// Configure caption
		var captionSO = new SerializedObject(caption);
		captionSO.FindProperty("Text").stringValue = "Option A";
		captionSO.FindProperty("fontColor").colorValue = Color.black;
		captionSO.FindProperty("fontSize").intValue = 48;
		captionSO.FindProperty("textAlignment").enumValueIndex = 2; // Center
		captionSO.FindProperty("verticalAlignment").enumValueIndex = 1; // Middle
		captionSO.FindProperty("maxWidth").floatValue = 456f;
		captionSO.ApplyModifiedPropertiesWithoutUndo();

		// Configure arrow text
		var arrowSO = new SerializedObject(arrowText);
		arrowSO.FindProperty("Text").stringValue = "\u25BC"; // ▼
		arrowSO.FindProperty("fontColor").colorValue = new Color(0.4f, 0.4f, 0.4f, 1f);
		arrowSO.FindProperty("fontSize").intValue = 30;
		arrowSO.FindProperty("textAlignment").enumValueIndex = 2; // Center
		arrowSO.FindProperty("verticalAlignment").enumValueIndex = 1; // Middle
		arrowSO.FindProperty("maxWidth").floatValue = 60f;
		arrowSO.ApplyModifiedPropertiesWithoutUndo();

		Selection.activeGameObject = root;
	}
}

#endif
