#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Reflection;

namespace SkiaSharp.Unity.HB.Editor {
    [InitializeOnLoad]
    static class HBIconAssigner {
        static readonly string IconFolder = "Assets/SkiaSharp/Editor/Icons";

        static readonly (string scriptName, string label, Color bg, Color fg)[] Scripts = {
            ("HB_TEXTBlock", "HB", new Color(0.16f, 0.50f, 0.73f, 1f), Color.white),
            ("HBTextAnimator", "A", new Color(0.20f, 0.60f, 0.36f, 1f), Color.white),
            ("HBInputField", "IF", new Color(0.80f, 0.45f, 0.10f, 1f), Color.white),
            ("SkiaGraphic", "SG", new Color(0.55f, 0.10f, 0.70f, 1f), Color.white),
        };

        static HBIconAssigner() {
            // Delay to avoid running during import
            EditorApplication.delayCall += AssignIcons;
        }

        static void AssignIcons() {
            foreach (var (scriptName, label, bg, fg) in Scripts) {
                var script = FindMonoScript(scriptName);
                if (script == null) continue;

                var existing = EditorGUIUtility.GetIconForObject(script);
                if (existing != null) continue;

                string iconPath = $"{IconFolder}/{scriptName} Icon.png";
                var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
                if (icon == null) {
                    icon = GenerateIcon(label, bg, fg);
                    if (!Directory.Exists(IconFolder))
                        Directory.CreateDirectory(IconFolder);
                    File.WriteAllBytes(iconPath, icon.EncodeToPNG());
                    Object.DestroyImmediate(icon);
                    AssetDatabase.ImportAsset(iconPath, ImportAssetOptions.ForceSynchronousImport);
                    // Set texture import settings for icon
                    var importer = (TextureImporter)AssetImporter.GetAtPath(iconPath);
                    if (importer != null) {
                        importer.textureType = TextureImporterType.GUI;
                        importer.npotScale = TextureImporterNPOTScale.None;
                        importer.textureCompression = TextureImporterCompression.Uncompressed;
                        importer.maxTextureSize = 64;
                        importer.SaveAndReimport();
                    }
                    icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
                }

                if (icon != null) {
                    EditorGUIUtility.SetIconForObject(script, icon);
                    EditorUtility.SetDirty(script);
                    // Disable the scene-view gizmo icon so it only shows in Inspector/Hierarchy
                    DisableSceneGizmo(script.GetClass());
                }
            }
        }

        static void DisableSceneGizmo(System.Type componentType) {
            if (componentType == null) return;
            try {
                var asm = typeof(EditorWindow).Assembly;
                var annotationUtility = asm.GetType("UnityEditor.AnnotationUtility");
                if (annotationUtility == null) return;

                var setIconEnabled = annotationUtility.GetMethod(
                    "SetIconEnabled",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                    null,
                    new[] { typeof(int), typeof(string), typeof(int) },
                    null);
                if (setIconEnabled != null) {
                    // classID 114 = MonoBehaviour
                    setIconEnabled.Invoke(null, new object[] { 114, componentType.Name, 0 });
                    return;
                }
            } catch {
                // Silently fail — gizmo will just remain visible
            }
        }

        static MonoScript FindMonoScript(string className) {
            foreach (var guid in AssetDatabase.FindAssets($"t:MonoScript {className}")) {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (ms != null && ms.GetClass()?.Name == className) return ms;
            }
            return null;
        }

        static Texture2D GenerateIcon(string text, Color bgColor, Color fgColor) {
            int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];

            float center = size / 2f;
            float radius = size / 2f - 1f;

            // Draw filled circle with soft edge
            for (int y = 0; y < size; y++) {
                for (int x = 0; x < size; x++) {
                    float dx = x - center + 0.5f;
                    float dy = y - center + 0.5f;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist <= radius - 1f)
                        pixels[y * size + x] = bgColor;
                    else if (dist <= radius)
                        pixels[y * size + x] = Color.Lerp(bgColor, Color.clear, dist - (radius - 1f));
                    else
                        pixels[y * size + x] = Color.clear;
                }
            }

            // Draw text using a simple bitmap font
            DrawTextOnIcon(pixels, size, text, fgColor);

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        static void DrawTextOnIcon(Color[] pixels, int size, string text, Color color) {
            // Simple 5x7 pixel font glyphs for uppercase + digits
            var glyphs = new System.Collections.Generic.Dictionary<char, byte[]> {
                ['H'] = new byte[] {
                    0b10001_000,
                    0b10001_000,
                    0b11111_000,
                    0b10001_000,
                    0b10001_000,
                    0b10001_000,
                    0b10001_000,
                },
                ['B'] = new byte[] {
                    0b11110_000,
                    0b10001_000,
                    0b10001_000,
                    0b11110_000,
                    0b10001_000,
                    0b10001_000,
                    0b11110_000,
                },
                ['A'] = new byte[] {
                    0b01110_000,
                    0b10001_000,
                    0b10001_000,
                    0b11111_000,
                    0b10001_000,
                    0b10001_000,
                    0b10001_000,
                },
                ['T'] = new byte[] {
                    0b11111_000,
                    0b00100_000,
                    0b00100_000,
                    0b00100_000,
                    0b00100_000,
                    0b00100_000,
                    0b00100_000,
                },
                ['I'] = new byte[] {
                    0b11111_000,
                    0b00100_000,
                    0b00100_000,
                    0b00100_000,
                    0b00100_000,
                    0b00100_000,
                    0b11111_000,
                },
                ['F'] = new byte[] {
                    0b11111_000,
                    0b10000_000,
                    0b10000_000,
                    0b11110_000,
                    0b10000_000,
                    0b10000_000,
                    0b10000_000,
                },
                ['S'] = new byte[] {
                    0b01111_000,
                    0b10000_000,
                    0b10000_000,
                    0b01110_000,
                    0b00001_000,
                    0b00001_000,
                    0b11110_000,
                },
                ['G'] = new byte[] {
                    0b01110_000,
                    0b10001_000,
                    0b10000_000,
                    0b10011_000,
                    0b10001_000,
                    0b10001_000,
                    0b01110_000,
                },
            };

            int glyphW = 5, glyphH = 7;
            int scale = 4; // Each pixel becomes 4x4
            int totalW = text.Length * glyphW * scale + (text.Length - 1) * scale;
            int totalH = glyphH * scale;
            int startX = (size - totalW) / 2;
            int startY = (size - totalH) / 2;

            int cursorX = startX;
            foreach (char c in text) {
                if (!glyphs.TryGetValue(c, out var glyph)) { cursorX += (glyphW + 1) * scale; continue; }
                for (int row = 0; row < glyphH; row++) {
                    byte bits = glyph[row];
                    for (int col = 0; col < glyphW; col++) {
                        bool on = (bits & (1 << (7 - col))) != 0;
                        if (!on) continue;
                        for (int sy = 0; sy < scale; sy++) {
                            for (int sx = 0; sx < scale; sx++) {
                                int px = cursorX + col * scale + sx;
                                int py = startY + (glyphH - 1 - row) * scale + sy; // Flip Y
                                if (px >= 0 && px < size && py >= 0 && py < size)
                                    pixels[py * size + px] = color;
                            }
                        }
                    }
                }
                cursorX += (glyphW + 1) * scale;
            }
        }
    }
}
#endif
