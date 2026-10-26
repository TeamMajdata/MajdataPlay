#nullable enable
#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEditor;
using System.Reflection;
using MajdataPlay.IO.Storage;

namespace SkiaSharp.Unity.HB.Editor
{
    /// <summary>Assigns package icons to Skia components without creating project assets.</summary>
    [InitializeOnLoad]
    internal static class HBIconAssigner
    {
        /// <summary>The physical directory in the local SkiaForUnity package used for generated PNG files.</summary>
        private const string IconFolder = "ThirdParty/SkiaForUnity/Editor/Icons";

        /// <summary>The virtual package directory used by Unity to load and import icons.</summary>
        private const string IconAssetFolder = "Packages/com.ammariqais.skiaforunity/Editor/Icons";

        /// <summary>The component names, labels, and colors used by generated icons.</summary>
        private static readonly (string scriptName, string label, Color bg, Color fg)[] s_scripts = {
            ("HB_TEXTBlock", "HB", new Color(0.16f, 0.50f, 0.73f, 1f), Color.white),
            ("HBTextAnimator", "A", new Color(0.20f, 0.60f, 0.36f, 1f), Color.white),
            ("HBInputField", "IF", new Color(0.80f, 0.45f, 0.10f, 1f), Color.white),
            ("SkiaGraphic", "SG", new Color(0.55f, 0.10f, 0.70f, 1f), Color.white),
        };

        /// <summary>Schedules icon assignment after the editor finishes importing scripts.</summary>
        static HBIconAssigner()
        {
            // Delay to avoid running during import
            EditorApplication.delayCall += AssignIcons;
        }

        /// <summary>Reuses or generates package icons and replaces icons from the obsolete Assets directory.</summary>
        /// <exception cref="UnauthorizedAccessException">The package icon directory cannot be written.</exception>
        /// <exception cref="System.IO.IOException">Creating or writing a package icon fails.</exception>
        private static void AssignIcons()
        {
            foreach (var (scriptName, label, bg, fg) in s_scripts)
            {
                var script = FindMonoScript(scriptName);
                if (script == null)
                {
                    continue;
                }

                var existing = EditorGUIUtility.GetIconForObject(script);
                if (existing != null && !AssetDatabase.GetAssetPath(existing).StartsWith("Assets/SkiaSharp/", StringComparison.Ordinal))
                {
                    continue;
                }

                var iconName = $"{scriptName} Icon.png";
                var iconPath = $"{IconAssetFolder}/{iconName}";
                var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
                if (icon == null)
                {
                    var generatedIcon = GenerateIcon(label, bg, fg);
                    try
                    {
                        var folder = FileSystem.Directory.Create(IconFolder);
                        var file = FileSystem.File.Create(folder.Location, iconName, overwrite: true);
                        file.WriteAllBytes(generatedIcon.EncodeToPNG());
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(generatedIcon);
                    }
                    AssetDatabase.ImportAsset(iconPath, ImportAssetOptions.ForceSynchronousImport);
                    // Set texture import settings for icon
                    var importer = (TextureImporter)AssetImporter.GetAtPath(iconPath);
                    if (importer != null)
                    {
                        importer.textureType = TextureImporterType.GUI;
                        importer.npotScale = TextureImporterNPOTScale.None;
                        importer.textureCompression = TextureImporterCompression.Uncompressed;
                        importer.maxTextureSize = 64;
                        importer.SaveAndReimport();
                    }
                    icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
                }

                if (icon != null)
                {
                    EditorGUIUtility.SetIconForObject(script, icon);
                    EditorUtility.SetDirty(script);
                    // Disable the scene-view gizmo icon so it only shows in Inspector/Hierarchy
                    DisableSceneGizmo(script.GetClass());
                }
            }
        }

        /// <summary>Disables the scene-view gizmo for a component while preserving its editor icon.</summary>
        /// <param name="componentType">The component type, or null when the script has no loaded class.</param>
        private static void DisableSceneGizmo(Type? componentType)
        {
            if (componentType == null)
            {
                return;
            }
            try
            {
                var asm = typeof(EditorWindow).Assembly;
                var annotationUtility = asm.GetType("UnityEditor.AnnotationUtility");
                if (annotationUtility == null)
                {
                    return;
                }

                var setIconEnabled = annotationUtility.GetMethod(
                    "SetIconEnabled",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                    null,
                    new[] { typeof(int), typeof(string), typeof(int) },
                    null);
                if (setIconEnabled != null)
                {
                    // classID 114 = MonoBehaviour
                    setIconEnabled.Invoke(null, new object[] { 114, componentType.Name, 0 });
                    return;
                }
            }
            catch
            {
                // Silently fail — gizmo will just remain visible
            }
        }

        /// <summary>Finds the imported script whose loaded class matches a component name.</summary>
        /// <param name="className">The component class name to find.</param>
        /// <returns>The matching script, or null when no loaded class matches.</returns>
        private static MonoScript? FindMonoScript(string className)
        {
            foreach (var guid in AssetDatabase.FindAssets($"t:MonoScript {className}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (ms != null && ms.GetClass()?.Name == className)
                {
                    return ms;
                }
            }
            return null;
        }

        /// <summary>Creates a circular component icon with a bitmap label.</summary>
        /// <param name="text">The label to draw on the icon.</param>
        /// <param name="bgColor">The circle background color.</param>
        /// <param name="fgColor">The label foreground color.</param>
        /// <returns>A new readable texture that the caller must destroy.</returns>
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

        /// <summary>Draws a centered bitmap label into a square pixel buffer.</summary>
        /// <param name="pixels">The target pixel buffer.</param>
        /// <param name="size">The width and height of the buffer.</param>
        /// <param name="text">The label whose supported glyphs are drawn.</param>
        /// <param name="color">The glyph color.</param>
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
