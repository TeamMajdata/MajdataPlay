#if UNITY_EDITOR

using SkiaSharp.Unity;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.U2D;
using UnityEditor;
using UnityEditor.U2D;
using System.IO;

[CustomEditor(typeof(SkiaGraphic))]
[CanEditMultipleObjects]
public class SkiaGraphicEditor : Editor {
	// Shape
	private SerializedProperty shape;
	private SerializedProperty cornerRadii;

	// Fill
	private SerializedProperty fillType;
	private SerializedProperty fillColor;
	private SerializedProperty gradient;
	private SerializedProperty gradientAngle;
	private SerializedProperty fillImage;
	private SerializedProperty imageFit;

	// Stroke
	private SerializedProperty enableStroke;
	private SerializedProperty strokeColor;
	private SerializedProperty strokeWidth;
	private SerializedProperty enableDashedStroke;
	private SerializedProperty dashLength;
	private SerializedProperty dashGap;
	private SerializedProperty enableGradientStroke;
	private SerializedProperty strokeGradient;
	private SerializedProperty strokeGradientAngle;

	// Shadow
	private SerializedProperty enableShadow;
	private SerializedProperty shadowColor;
	private SerializedProperty shadowOffset;
	private SerializedProperty shadowBlur;

	// Inner Shadow
	private SerializedProperty enableInnerShadow;
	private SerializedProperty innerShadowColor;
	private SerializedProperty innerShadowOffset;
	private SerializedProperty innerShadowBlur;

	// Performance
	private SerializedProperty resolutionScale;

	// Bake
	private SerializedProperty bakedSprite;

	// Style Preset
	private SerializedProperty stylePreset;

	private GUIStyle headerStyle;

	private static readonly GUIContent k_RaycastTargetLabel = new GUIContent("Raycast Target");
	private static readonly GUIContent k_MaskableLabel = new GUIContent("Maskable");

	private GameObject[] _cachedGameObjects;

	private void OnDisable() {
		// Clean up hidden components when SkiaGraphic is removed from inspector
		if (_cachedGameObjects == null) return;
		foreach (var go in _cachedGameObjects) {
			if (go == null) continue;
			if (go.GetComponent<SkiaGraphic>() != null) continue; // component still exists
			var ri = go.GetComponent<RawImage>();
			if (ri != null && (ri.hideFlags & HideFlags.HideInInspector) != 0)
				Undo.DestroyObjectImmediate(ri);
			var img = go.GetComponent<Image>();
			if (img != null && (img.hideFlags & HideFlags.HideInInspector) != 0)
				Undo.DestroyObjectImmediate(img);
		}
	}

	private void OnEnable() {
		shape = serializedObject.FindProperty("shape");
		cornerRadii = serializedObject.FindProperty("cornerRadii");
		fillType = serializedObject.FindProperty("fillType");
		fillColor = serializedObject.FindProperty("fillColor");
		gradient = serializedObject.FindProperty("gradient");
		gradientAngle = serializedObject.FindProperty("gradientAngle");
		fillImage = serializedObject.FindProperty("fillImage");
		imageFit = serializedObject.FindProperty("imageFit");
		enableStroke = serializedObject.FindProperty("enableStroke");
		strokeColor = serializedObject.FindProperty("strokeColor");
		strokeWidth = serializedObject.FindProperty("strokeWidth");
		enableDashedStroke = serializedObject.FindProperty("enableDashedStroke");
		dashLength = serializedObject.FindProperty("dashLength");
		dashGap = serializedObject.FindProperty("dashGap");
		enableGradientStroke = serializedObject.FindProperty("enableGradientStroke");
		strokeGradient = serializedObject.FindProperty("strokeGradient");
		strokeGradientAngle = serializedObject.FindProperty("strokeGradientAngle");
		enableShadow = serializedObject.FindProperty("enableShadow");
		shadowColor = serializedObject.FindProperty("shadowColor");
		shadowOffset = serializedObject.FindProperty("shadowOffset");
		shadowBlur = serializedObject.FindProperty("shadowBlur");
		enableInnerShadow = serializedObject.FindProperty("enableInnerShadow");
		innerShadowColor = serializedObject.FindProperty("innerShadowColor");
		innerShadowOffset = serializedObject.FindProperty("innerShadowOffset");
		innerShadowBlur = serializedObject.FindProperty("innerShadowBlur");
		resolutionScale = serializedObject.FindProperty("resolutionScale");
		stylePreset = serializedObject.FindProperty("stylePreset");
		bakedSprite = serializedObject.FindProperty("bakedSprite");

		// Cache GameObjects so we can clean up hidden components in OnDisable
		_cachedGameObjects = new GameObject[targets.Length];
		for (int i = 0; i < targets.Length; i++)
			_cachedGameObjects[i] = ((Component)targets[i]).gameObject;

		// Hide RawImage and Image from inspector — SkiaGraphic manages them internally
		foreach (var t in targets) {
			var ri = ((Component)t).GetComponent<RawImage>();
			if (ri != null && (ri.hideFlags & HideFlags.HideInInspector) == 0)
				ri.hideFlags |= HideFlags.HideInInspector;
			var img = ((Component)t).GetComponent<Image>();
			if (img != null && (img.hideFlags & HideFlags.HideInInspector) == 0)
				img.hideFlags |= HideFlags.HideInInspector;
		}
	}

	public override void OnInspectorGUI() {
		if (headerStyle == null) {
			headerStyle = new GUIStyle(EditorStyles.boldLabel) {
				fontSize = 12
			};
		}

		serializedObject.Update();

		bool isBaked = bakedSprite.objectReferenceValue != null;

		// Grey out all rendering options when baked
		EditorGUI.BeginDisabledGroup(isBaked);

		// Shape
		EditorGUILayout.LabelField("Shape", headerStyle);
		EditorGUI.indentLevel++;
		EditorGUILayout.PropertyField(shape);
		if ((SkiaShapeType)shape.enumValueIndex == SkiaShapeType.RoundedRect) {
			EditorGUILayout.PropertyField(cornerRadii, new GUIContent("Corner Radii", "X=TopLeft, Y=TopRight, Z=BottomRight, W=BottomLeft"));
		}
		EditorGUI.indentLevel--;

		EditorGUILayout.Space(4);

		// Fill
		EditorGUILayout.LabelField("Fill", headerStyle);
		EditorGUI.indentLevel++;
		EditorGUILayout.PropertyField(fillType);
		var ft = (SkiaFillType)fillType.enumValueIndex;
		if (ft == SkiaFillType.Solid) {
			EditorGUILayout.PropertyField(fillColor);
		} else if (ft == SkiaFillType.Image) {
			EditorGUILayout.PropertyField(fillImage, new GUIContent("Image", "Texture must have Read/Write enabled in import settings."));
			EditorGUILayout.PropertyField(imageFit, new GUIContent("Fit Mode"));
		} else if (ft != SkiaFillType.None) {
			EditorGUILayout.PropertyField(gradient);
			if (ft == SkiaFillType.LinearGradient) {
				EditorGUILayout.PropertyField(gradientAngle, new GUIContent("Angle"));
			}
		}
		EditorGUI.indentLevel--;

		EditorGUILayout.Space(4);

		// Stroke
		EditorGUILayout.LabelField("Stroke", headerStyle);
		EditorGUI.indentLevel++;
		EditorGUILayout.PropertyField(enableStroke, new GUIContent("Enable"));
		if (enableStroke.boolValue) {
			EditorGUILayout.PropertyField(enableGradientStroke, new GUIContent("Gradient Stroke"));
			if (enableGradientStroke.boolValue) {
				EditorGUILayout.PropertyField(strokeGradient, new GUIContent("Gradient"));
				EditorGUILayout.PropertyField(strokeGradientAngle, new GUIContent("Angle"));
			} else {
				EditorGUILayout.PropertyField(strokeColor, new GUIContent("Color"));
			}
			EditorGUILayout.PropertyField(strokeWidth, new GUIContent("Width"));
			EditorGUILayout.PropertyField(enableDashedStroke, new GUIContent("Dashed"));
			if (enableDashedStroke.boolValue) {
				EditorGUILayout.PropertyField(dashLength, new GUIContent("Dash Length"));
				EditorGUILayout.PropertyField(dashGap, new GUIContent("Dash Gap"));
			}
		}
		EditorGUI.indentLevel--;

		EditorGUILayout.Space(4);

		// Shadow
		EditorGUILayout.LabelField("Shadow", headerStyle);
		EditorGUI.indentLevel++;
		EditorGUILayout.PropertyField(enableShadow, new GUIContent("Enable"));
		if (enableShadow.boolValue) {
			EditorGUILayout.PropertyField(shadowColor, new GUIContent("Color"));
			EditorGUILayout.PropertyField(shadowOffset, new GUIContent("Offset"));
			EditorGUILayout.PropertyField(shadowBlur, new GUIContent("Blur"));
		}
		EditorGUI.indentLevel--;

		EditorGUILayout.Space(4);

		// Inner Shadow
		EditorGUILayout.LabelField("Inner Shadow", headerStyle);
		EditorGUI.indentLevel++;
		EditorGUILayout.PropertyField(enableInnerShadow, new GUIContent("Enable"));
		if (enableInnerShadow.boolValue) {
			EditorGUILayout.PropertyField(innerShadowColor, new GUIContent("Color"));
			EditorGUILayout.PropertyField(innerShadowOffset, new GUIContent("Offset"));
			EditorGUILayout.PropertyField(innerShadowBlur, new GUIContent("Blur"));
		}
		EditorGUI.indentLevel--;

		EditorGUILayout.Space(4);

		// Performance
		EditorGUILayout.LabelField("Performance", headerStyle);
		EditorGUI.indentLevel++;
		EditorGUILayout.PropertyField(resolutionScale, new GUIContent("Resolution Scale", "Lower for better mobile performance. 0.5 = half resolution."));
		EditorGUI.indentLevel--;

		EditorGUI.EndDisabledGroup();

		EditorGUILayout.Space(4);

		// Bake
		EditorGUILayout.LabelField("Bake", headerStyle);
		EditorGUI.indentLevel++;
		var prevBaked = bakedSprite.objectReferenceValue;
		EditorGUILayout.PropertyField(bakedSprite, new GUIContent("Baked Sprite", "When assigned, uses this sprite via Image component + SpriteAtlas for batching. Zero SkiaSharp cost at runtime."));
		var newBaked = bakedSprite.objectReferenceValue;

		// Handle manual assignment/removal of baked sprite via the field
		if (prevBaked != newBaked) {
			serializedObject.ApplyModifiedProperties();
			if (newBaked != null) {
				// Manually assigned a sprite — swap RawImage → Image
				foreach (var t in targets) {
					var graphic = (SkiaGraphic)t;
					var rawImg = graphic.GetComponent<RawImage>();
					bool raycast = rawImg != null && rawImg.raycastTarget;
					bool maskable = rawImg != null && rawImg.maskable;
					if (rawImg != null) {
						if (rawImg.texture != null) {
							DestroyImmediate(rawImg.texture);
							rawImg.texture = null;
						}
						Undo.DestroyObjectImmediate(rawImg);
					}
					var img = graphic.GetComponent<Image>();
					if (img == null) {
						img = graphic.gameObject.AddComponent<Image>();
						Undo.RegisterCreatedObjectUndo(img, "Assign Baked Sprite");
					}
					img.sprite = (Sprite)newBaked;
					img.type = img.sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
					img.preserveAspect = false;
					img.color = Color.white;
					img.raycastTarget = raycast;
					img.maskable = maskable;
					img.hideFlags |= HideFlags.HideInInspector;
				}
			} else {
				// Cleared baked sprite — swap Image → RawImage (unbake)
				UnbakeSelectedGraphics();
			}
			GUIUtility.ExitGUI();
		}

		if (bakedSprite.objectReferenceValue != null) {
			EditorGUILayout.HelpBox("Baked. Using Image + SpriteAtlas for draw call batching. Zero SkiaSharp cost at runtime.", MessageType.Info);
			if (GUILayout.Button("Unbake (Return to Live Rendering)")) {
				serializedObject.ApplyModifiedProperties();
				UnbakeSelectedGraphics();
				GUIUtility.ExitGUI();
			}
		} else {
			if (GUILayout.Button("Bake to PNG (Auto Atlas)")) {
				serializedObject.ApplyModifiedProperties();
				BakeSelectedGraphics();
				GUIUtility.ExitGUI();
			}
		}
		EditorGUI.indentLevel--;

		EditorGUILayout.Space(4);

		// Style Preset
		EditorGUILayout.LabelField("Style Preset", headerStyle);
		EditorGUI.indentLevel++;
		EditorGUILayout.PropertyField(stylePreset, new GUIContent("Preset"));
		var currentStyle = (SkiaGraphicStyle)stylePreset.objectReferenceValue;
		if (currentStyle != null) {
			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("Apply Style")) {
				serializedObject.ApplyModifiedProperties();
				foreach (var t in targets) {
					Undo.RecordObject((SkiaGraphic)t, "Apply Style Preset");
					ApplyStyleToGraphic((SkiaGraphic)t, currentStyle);
					EditorUtility.SetDirty((SkiaGraphic)t);
				}
				serializedObject.Update();
				SceneView.RepaintAll();
				UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
				GUIUtility.ExitGUI();
			}
			if (GUILayout.Button("Save to Style")) {
				Undo.RecordObject(currentStyle, "Save Style Preset");
				SaveGraphicToStyle((SkiaGraphic)target, currentStyle);
				EditorUtility.SetDirty(currentStyle);
				AssetDatabase.SaveAssets();
				GUIUtility.ExitGUI();
			}
			EditorGUILayout.EndHorizontal();
		}
		if (GUILayout.Button("Create New Style from Current")) {
			string absPath = EditorUtility.SaveFilePanel("Save Style Preset", Application.dataPath, "NewStyle", "asset");
			if (!string.IsNullOrEmpty(absPath)) {
				// Convert absolute path to project-relative Assets/ path
				string projectPath = Application.dataPath; // ends with /Assets
				if (!absPath.StartsWith(projectPath)) {
					Debug.LogError("Style must be saved inside the Assets folder.");
				} else {
					string path = "Assets" + absPath.Substring(projectPath.Length);
					var newStyle = ScriptableObject.CreateInstance<SkiaGraphicStyle>();
					SaveGraphicToStyle((SkiaGraphic)target, newStyle);
					AssetDatabase.CreateAsset(newStyle, path);
					AssetDatabase.SaveAssets();
					foreach (var t in targets) {
						var so = new SerializedObject(t);
						so.FindProperty("stylePreset").objectReferenceValue = newStyle;
						so.ApplyModifiedProperties();
					}
					EditorGUIUtility.PingObject(newStyle);
					GUIUtility.ExitGUI();
				}
			}
		}
		EditorGUI.indentLevel--;

		EditorGUILayout.Space(4);

		// UI Settings (RawImage proxy properties)
		EditorGUILayout.LabelField("UI Settings", headerStyle);
		EditorGUI.indentLevel++;
		DrawRawImageProperties();
		EditorGUI.indentLevel--;

		serializedObject.ApplyModifiedProperties();
	}

	private void BakeSelectedGraphics() {
		string dir = "Assets/BakedSkiaGraphics";
		if (!AssetDatabase.IsValidFolder(dir))
			AssetDatabase.CreateFolder("Assets", "BakedSkiaGraphics");

		// Load or create SpriteAtlas
		string atlasPath = $"{dir}/SkiaGraphicAtlas.spriteatlas";
		SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
		if (atlas == null) {
			atlas = new SpriteAtlas();
			atlas.SetPackingSettings(new SpriteAtlasPackingSettings {
				enableRotation = false,
				enableTightPacking = false,
				padding = 4
			});
			atlas.SetTextureSettings(new SpriteAtlasTextureSettings {
				readable = false,
				generateMipMaps = false,
				sRGB = true,
				filterMode = FilterMode.Bilinear
			});
			AssetDatabase.CreateAsset(atlas, atlasPath);
		}

		foreach (var t in targets) {
			var graphic = (SkiaGraphic)t;
			byte[] png = graphic.BakeToTexture();
			if (png == null || png.Length == 0) {
				Debug.LogWarning($"SkiaGraphic: Bake failed for {graphic.name}");
				continue;
			}

			string assetPath = GetUniqueBakePath(dir, graphic.gameObject.name, "_baked");

			File.WriteAllBytes(assetPath, png);
			AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

			// Import as Sprite with compression + nine-slice border
			Vector4 border = graphic.GetNineSliceBorder();
			TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
			if (importer != null) {
				importer.textureType = TextureImporterType.Sprite;
				importer.spriteImportMode = SpriteImportMode.Single;
				importer.spriteBorder = border;
				importer.textureCompression = TextureImporterCompression.Compressed;
				importer.alphaIsTransparency = true;
				importer.npotScale = TextureImporterNPOTScale.None;
				importer.mipmapEnabled = false;
				importer.sRGBTexture = true;
				importer.SaveAndReimport();
			}

			Sprite spriteAsset = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
			if (spriteAsset == null) {
				Debug.LogWarning($"SkiaGraphic: Could not load baked sprite at {assetPath}");
				continue;
			}

			// Add sprite to atlas
			atlas.Add(new Object[] { AssetDatabase.LoadAssetAtPath<Object>(assetPath) });

			// Swap RawImage → Image (destroy texture first to prevent leak)
			var rawImg = graphic.GetComponent<RawImage>();
			bool raycast = rawImg != null && rawImg.raycastTarget;
			bool maskable = rawImg != null && rawImg.maskable;
			if (rawImg != null) {
				if (rawImg.texture != null) {
					DestroyImmediate(rawImg.texture);
					rawImg.texture = null;
				}
				Undo.DestroyObjectImmediate(rawImg);
			}

			var img = graphic.GetComponent<Image>();
			if (img == null) {
				img = graphic.gameObject.AddComponent<Image>();
				Undo.RegisterCreatedObjectUndo(img, "Bake SkiaGraphic");
			}
			img.sprite = spriteAsset;
			if (border != Vector4.zero) {
				img.type = Image.Type.Sliced;
				img.fillCenter = true;
			} else {
				img.type = Image.Type.Simple;
			}
			img.preserveAspect = false;
			img.color = Color.white;
			img.raycastTarget = raycast;
			img.maskable = maskable;
			img.hideFlags |= HideFlags.HideInInspector;

			// Assign bakedSprite
			var so = new SerializedObject(graphic);
			so.FindProperty("bakedSprite").objectReferenceValue = spriteAsset;
			so.ApplyModifiedProperties();
			EditorUtility.SetDirty(graphic);
		}

		EditorUtility.SetDirty(atlas);
		AssetDatabase.SaveAssets();
	}

	private void UnbakeSelectedGraphics() {
		foreach (var t in targets) {
			var graphic = (SkiaGraphic)t;

			// Remove Image, re-add RawImage (only one Graphic per GO)
			var img = graphic.GetComponent<Image>();
			if (img != null) Undo.DestroyObjectImmediate(img);

			var rawImg = graphic.GetComponent<RawImage>();
			if (rawImg == null) {
				rawImg = graphic.gameObject.AddComponent<RawImage>();
				Undo.RegisterCreatedObjectUndo(rawImg, "Unbake SkiaGraphic");
			}
			rawImg.enabled = true;
			rawImg.hideFlags |= HideFlags.HideInInspector;

			// Clear bakedSprite
			var so = new SerializedObject(graphic);
			so.FindProperty("bakedSprite").objectReferenceValue = null;
			so.ApplyModifiedProperties();

			// Re-trigger OnDisable → OnEnable to reinitialize rawImage + paint
			graphic.enabled = false;
			graphic.enabled = true;

			EditorUtility.SetDirty(graphic);
		}
	}

	private static string GetUniqueBakePath(string dir, string objectName, string suffix) {
		// Sanitize object name for filesystem
		foreach (char c in Path.GetInvalidFileNameChars())
			objectName = objectName.Replace(c, '_');

		string baseName = $"{objectName}{suffix}";
		string path = $"{dir}/{baseName}.png";
		if (!File.Exists(path)) return path;

		int counter = 1;
		while (File.Exists($"{dir}/{baseName}_{counter}.png"))
			counter++;
		return $"{dir}/{baseName}_{counter}.png";
	}

	[DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
	static void DrawSkiaGizmo(SkiaGraphic graphic, GizmoType gizmoType) {
		RectTransform rt = graphic.GetComponent<RectTransform>();
		if (rt == null) return;

		// Cyan when selected, semi-transparent when not
		bool selected = (gizmoType & GizmoType.Selected) != 0;
		Gizmos.color = selected ? new Color(0f, 0.8f, 1f, 1f) : new Color(0f, 0.8f, 1f, 0.3f);

		Vector3[] corners = new Vector3[4];
		rt.GetWorldCorners(corners);

		// Draw wireframe rect outline
		Gizmos.DrawLine(corners[0], corners[1]);
		Gizmos.DrawLine(corners[1], corners[2]);
		Gizmos.DrawLine(corners[2], corners[3]);
		Gizmos.DrawLine(corners[3], corners[0]);

		// Draw bake indicator (filled center dot) when baked
		if (graphic.IsBaked && selected) {
			Vector3 center = (corners[0] + corners[2]) * 0.5f;
			Handles.color = new Color(0.2f, 1f, 0.3f, 0.8f);
			float size = Vector3.Distance(corners[0], corners[2]) * 0.02f;
			Handles.DrawSolidDisc(center, rt.forward, Mathf.Max(size, 2f));
		}
	}

	private static void ApplyStyleToGraphic(SkiaGraphic graphic, SkiaGraphicStyle style) {
		graphic.Shape = style.shape;
		graphic.CornerRadii = style.cornerRadii;
		graphic.FillType = style.fillType;
		graphic.FillColor = style.fillColor;
		graphic.GradientAngle = style.gradientAngle;
		graphic.FillImage = style.fillImage;
		graphic.ImageFit = style.imageFit;
		graphic.EnableStroke = style.enableStroke;
		graphic.StrokeColor = style.strokeColor;
		graphic.StrokeWidth = style.strokeWidth;
		graphic.EnableDashedStroke = style.enableDashedStroke;
		graphic.DashLength = style.dashLength;
		graphic.DashGap = style.dashGap;
		graphic.EnableGradientStroke = style.enableGradientStroke;
		graphic.StrokeGradientAngle = style.strokeGradientAngle;
		graphic.EnableShadow = style.enableShadow;
		graphic.ShadowColor = style.shadowColor;
		graphic.ShadowOffset = style.shadowOffset;
		graphic.ShadowBlur = style.shadowBlur;
		graphic.EnableInnerShadow = style.enableInnerShadow;
		graphic.InnerShadowColor = style.innerShadowColor;
		graphic.InnerShadowOffset = style.innerShadowOffset;
		graphic.InnerShadowBlur = style.innerShadowBlur;
		graphic.ResolutionScale = style.resolutionScale;

		// Copy gradients via serialized properties
		var soGraphic = new SerializedObject(graphic);
		var soStyle = new SerializedObject(style);
		soGraphic.CopyFromSerializedPropertyIfDifferent(soStyle.FindProperty("gradient"));
		soGraphic.CopyFromSerializedPropertyIfDifferent(soStyle.FindProperty("strokeGradient"));
		soGraphic.ApplyModifiedProperties();

		// Handle baked sprite swap (RawImage ↔ Image)
		bool wasBaked = graphic.IsBaked;
		bool willBake = style.bakedSprite != null;

		soGraphic = new SerializedObject(graphic);
		soGraphic.FindProperty("bakedSprite").objectReferenceValue = style.bakedSprite;
		soGraphic.ApplyModifiedProperties();

		if (willBake) {
			// Swap RawImage → Image
			var rawImg = graphic.GetComponent<RawImage>();
			bool raycast = rawImg != null && rawImg.raycastTarget;
			bool maskable = rawImg != null && rawImg.maskable;
			if (rawImg != null) {
				if (rawImg.texture != null) {
					DestroyImmediate(rawImg.texture);
					rawImg.texture = null;
				}
				Undo.DestroyObjectImmediate(rawImg);
			}
			var img = graphic.GetComponent<Image>();
			if (img == null) {
				img = graphic.gameObject.AddComponent<Image>();
				Undo.RegisterCreatedObjectUndo(img, "Apply Style Baked Sprite");
			}
			img.sprite = style.bakedSprite;
			img.type = img.sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
			img.preserveAspect = false;
			img.color = Color.white;
			img.raycastTarget = raycast;
			img.maskable = maskable;
			img.hideFlags |= HideFlags.HideInInspector;
		} else if (wasBaked && !willBake) {
			// Swap Image → RawImage (unbake)
			var img = graphic.GetComponent<Image>();
			if (img != null) Undo.DestroyObjectImmediate(img);
			var rawImg = graphic.GetComponent<RawImage>();
			if (rawImg == null) {
				rawImg = graphic.gameObject.AddComponent<RawImage>();
				Undo.RegisterCreatedObjectUndo(rawImg, "Apply Style Unbake");
			}
			rawImg.enabled = true;
			rawImg.hideFlags |= HideFlags.HideInInspector;
			graphic.enabled = false;
			graphic.enabled = true;
		}
	}

	private static void SaveGraphicToStyle(SkiaGraphic graphic, SkiaGraphicStyle style) {
		// Direct assignments first
		style.shape = graphic.Shape;
		style.cornerRadii = graphic.CornerRadii;
		style.fillType = graphic.FillType;
		style.fillColor = graphic.FillColor;
		style.gradientAngle = graphic.GradientAngle;
		style.fillImage = graphic.FillImage;
		style.imageFit = graphic.ImageFit;
		style.enableStroke = graphic.EnableStroke;
		style.strokeColor = graphic.StrokeColor;
		style.strokeWidth = graphic.StrokeWidth;
		style.enableDashedStroke = graphic.EnableDashedStroke;
		style.dashLength = graphic.DashLength;
		style.dashGap = graphic.DashGap;
		style.enableGradientStroke = graphic.EnableGradientStroke;
		style.strokeGradientAngle = graphic.StrokeGradientAngle;
		style.enableShadow = graphic.EnableShadow;
		style.shadowColor = graphic.ShadowColor;
		style.shadowOffset = graphic.ShadowOffset;
		style.shadowBlur = graphic.ShadowBlur;
		style.enableInnerShadow = graphic.EnableInnerShadow;
		style.innerShadowColor = graphic.InnerShadowColor;
		style.innerShadowOffset = graphic.InnerShadowOffset;
		style.innerShadowBlur = graphic.InnerShadowBlur;
		style.resolutionScale = graphic.ResolutionScale;

		// Read baked sprite
		var soGraphic = new SerializedObject(graphic);
		style.bakedSprite = soGraphic.FindProperty("bakedSprite").objectReferenceValue as Sprite;

		// Create SerializedObject AFTER direct assignments so buffer has correct values
		var soStyle = new SerializedObject(style);
		soStyle.CopyFromSerializedPropertyIfDifferent(soGraphic.FindProperty("gradient"));
		soStyle.CopyFromSerializedPropertyIfDifferent(soGraphic.FindProperty("strokeGradient"));
		soStyle.ApplyModifiedProperties();
	}

	private void DrawRawImageProperties() {
		bool isBaked = bakedSprite.objectReferenceValue != null;
		var uiTargets = new Object[targets.Length];
		for (int i = 0; i < targets.Length; i++) {
			if (isBaked)
				uiTargets[i] = ((Component)targets[i]).GetComponent<Image>();
			else
				uiTargets[i] = ((Component)targets[i]).GetComponent<RawImage>();
		}

		if (uiTargets[0] == null) return;

		var uiSo = new SerializedObject(uiTargets);
		uiSo.Update();

		var raycastProp = uiSo.FindProperty("m_RaycastTarget");
		var maskableProp = uiSo.FindProperty("m_Maskable");

		EditorGUILayout.PropertyField(raycastProp, k_RaycastTargetLabel);
		EditorGUILayout.PropertyField(maskableProp, k_MaskableLabel);

		uiSo.ApplyModifiedProperties();
	}
}

static class SkiaGraphicMenuItems {
	[MenuItem("GameObject/Skia UI (Canvas)/Skia Graphic", false, 9)]
	static void CreateSkiaGraphic(MenuCommand menuCommand) {
		GameObject parent = menuCommand.context as GameObject;
		Canvas canvas = parent != null ? parent.GetComponentInParent<Canvas>() : null;

		if (canvas == null)
			canvas = Object.FindObjectOfType<Canvas>();

		if (canvas == null) {
			var canvasGO = new GameObject("Canvas");
			canvas = canvasGO.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
			canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();
			Undo.RegisterCreatedObjectUndo(canvasGO, "Create Canvas");

			if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null) {
				var esGO = new GameObject("EventSystem");
				esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
				esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
				Undo.RegisterCreatedObjectUndo(esGO, "Create EventSystem");
			}
		}

		var go = new GameObject("Skia Graphic");
		var rt = go.AddComponent<RectTransform>();
		Undo.RegisterCreatedObjectUndo(go, "Create Skia Graphic");

		Transform parentTransform = parent != null && parent.GetComponentInParent<Canvas>() != null
			? parent.transform
			: canvas.transform;
		GameObjectUtility.SetParentAndAlign(go, parentTransform.gameObject);

		go.AddComponent<SkiaGraphic>();

		rt.sizeDelta = new Vector2(200, 200);

		Selection.activeGameObject = go;
	}
}
#endif
