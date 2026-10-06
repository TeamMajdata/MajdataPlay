using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using MajdataPlay.Scenes.Game.Notes.Skins;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

#nullable enable
namespace CustomSkinAtlasValidation
{
    /// <summary>
    /// Exercises the production AtlasBuilder in an isolated Unity project.
    /// </summary>
    public static class Validation
    {
        /// <summary>
        /// Holds the height of the narrow texture used to force smaller-limit resizing.
        /// </summary>
        private const int SourceHeight = 32;

        /// <summary>
        /// Holds the transparent horizontal margin in the geometry fixture.
        /// </summary>
        private const int HorizontalMargin = 16;

        /// <summary>
        /// Holds the transparent vertical margin in the geometry fixture.
        /// </summary>
        private const int VerticalMargin = 8;

        /// <summary>
        /// Selects comparison with the committed implementation's existing Editor behavior.
        /// </summary>
        private static bool s_baseline;

        /// <summary>
        /// Checks the committed packing path against the same fixture and expected Editor errors.
        /// </summary>
        public static void RunBaseline()
        {
            s_baseline = true;
            Run();
        }

        /// <summary>
        /// Runs the checks and exits the isolated Editor with their result.
        /// </summary>
        public static async void Run()
        {
            try
            {
                Debug.Log($"CUSTOM_SKIN_ATLAS_DEVICE api={SystemInfo.graphicsDeviceType} maxTextureSize={SystemInfo.maxTextureSize}");
                var sourceWidth = Math.Min(8192, SystemInfo.maxTextureSize);
                var sourcePath = CreateSource(sourceWidth);
                await CheckEmptyInput();
                if (s_baseline)
                {
                    Check(SystemInfo.maxTextureSize >= 8192, "The committed baseline requires a host supporting its 8192-square allocation.");
                    await CheckPacking(sourcePath, sourceWidth, SystemInfo.maxTextureSize, false, useActualLimit: true);
                    Debug.Log("CUSTOM_SKIN_ATLAS_BASELINE_PASSED");
                    Debug.Log("CUSTOM_SKIN_ATLAS_VALIDATION_PASSED");
                    EditorApplication.Exit(0);
                    return;
                }
                foreach (var limit in new[] { 2048, 4096, 8192, 16384 })
                {
                    if (Math.Min(8192, limit) > SystemInfo.maxTextureSize)
                    {
                        Debug.Log($"CUSTOM_SKIN_ATLAS_SKIP limit={limit} actualMaximum={SystemInfo.maxTextureSize}");
                        continue;
                    }
                    await CheckPacking(sourcePath, sourceWidth, limit, false);
                    await CheckPacking(sourcePath, sourceWidth, limit, true);
                }
                DeviceCapabilities.MaximumTextureSize = 0;
                await CheckPacking(sourcePath, sourceWidth, SystemInfo.maxTextureSize, true, useActualLimit: true);
                Debug.Log("CUSTOM_SKIN_ATLAS_VALIDATION_PASSED");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Writes a source PNG with transparent margins around a rectangular alpha outline.
        /// </summary>
        /// <param name="width">The width supported by the actual host graphics device.</param>
        /// <returns>The generated PNG path inside the isolated project.</returns>
        /// <exception cref="IOException">The input directory or PNG cannot be written.</exception>
        /// <exception cref="UnauthorizedAccessException">The input directory cannot be accessed.</exception>
        private static string CreateSource(int width)
        {
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestInputs"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "solid.png");
            var texture = new Texture2D(width, SourceHeight, TextureFormat.RGBA32, false);
            try
            {
                var pixels = new Color32[width * SourceHeight];
                for (var y = VerticalMargin; y < SourceHeight - VerticalMargin; y++)
                {
                    for (var x = HorizontalMargin; x < width - HorizontalMargin; x++)
                    {
                        pixels[y * width + x] = new Color32(255, 255, 255, 255);
                    }
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(texture));
                return path;
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// Verifies empty and missing-file inputs in both public build paths.
        /// </summary>
        /// <returns>The asynchronous verification operation.</returns>
        /// <exception cref="InvalidOperationException">A check fails.</exception>
        private static async UniTask CheckEmptyInput()
        {
            var empty = new AtlasBuilder(Application.dataPath);
            Check(empty.BuildAndAssign() == null, "Synchronous empty input returns null.");
            Check(await empty.BuildAndAssignAsync() == null, "Asynchronous empty input returns null.");
            foreach (var asynchronous in new[] { false, true })
            {
                var assignments = 0;
                var missing = new AtlasBuilder(Application.dataPath);
                missing.Add("not-present.png", sprite =>
                {
                    Check(sprite == null, "Missing input assigns null.");
                    assignments++;
                });
                var atlas = asynchronous ? await missing.BuildAndAssignAsync() : missing.BuildAndAssign();
                Check(atlas == null && assignments == 1, "Missing input builds no atlas and invokes its assigner once.");
            }
        }

        /// <summary>
        /// Packs five sources through the unchanged production code and checks the result.
        /// </summary>
        /// <param name="sourcePath">The generated source PNG.</param>
        /// <param name="sourceWidth">The source PNG's width.</param>
        /// <param name="limit">The simulated or actual device maximum.</param>
        /// <param name="asynchronous">Whether to use the asynchronous public build path.</param>
        /// <param name="useActualLimit">Whether to read the real graphics device limit.</param>
        /// <returns>The asynchronous verification operation.</returns>
        /// <exception cref="InvalidOperationException">A check fails.</exception>
        /// <exception cref="IOException">The production builder cannot read a source PNG.</exception>
        private static async UniTask CheckPacking(string sourcePath, int sourceWidth, int limit, bool asynchronous, bool useActualLimit = false)
        {
            DeviceCapabilities.MaximumTextureSize = useActualLimit ? 0 : limit;
            DeviceCapabilities.QueryCount = 0;
            var sprites = new List<Sprite>();
            var border = new Vector4(8, 2, 32, 4);
            var builder = new AtlasBuilder(Path.GetDirectoryName(sourcePath)!, 1f, 1f, 30);
            for (var i = 0; i < 5; i++)
            {
                builder.Add(Path.GetFileName(sourcePath), sprite =>
                {
                    Check(sprite != null, "Existing input assigns a sprite.");
                    sprites.Add(sprite!);
                }, i == 0 ? border : Vector4.zero);
            }

            Texture2D? atlas = null;
            var expectedGeometryErrors = 0;
            string? unexpectedError = null;
            var expectedMessage = $"Not allowed to override geometry on sprite '{sourcePath}'";
            Application.LogCallback captureErrors = (message, stackTrace, type) =>
            {
                if (type == LogType.Error && message == expectedMessage)
                {
                    expectedGeometryErrors++;
                }
                else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    unexpectedError = message;
                }
            };
            Application.logMessageReceived += captureErrors;
            try
            {
                atlas = asynchronous ? await builder.BuildAndAssignAsync() : builder.BuildAndAssign();
                Check(unexpectedError == null, $"Unexpected Unity error: {unexpectedError}");
                Check(expectedGeometryErrors == 5, "Each sprite reproduces the existing Editor OverrideGeometry error.");
                Check(atlas != null, "Non-empty input returns an atlas.");
                if (!s_baseline)
                {
                    Check(DeviceCapabilities.QueryCount > 0, "Production packing queries the device texture limit.");
                }
                Check(atlas!.width <= limit && atlas.height <= limit, "Atlas dimensions fit the device limit.");
                Check(atlas.width <= 8192 && atlas.height <= 8192, "Atlas dimensions retain the existing 8192 ceiling.");
                Check(sprites.Count == 5, "Every source has an assigned sprite.");
                Check(!atlas.isReadable, "Completed atlas releases CPU pixels.");
                for (var i = 0; i < sprites.Count; i++)
                {
                    var sprite = sprites[i];
                    var sourceBorder = i == 0 ? border : Vector4.zero;
                    Check(sprite.texture == atlas, "Sprites share the completed atlas.");
                    var rect = sprite.rect;
                    Check(rect.xMin >= 0 && rect.yMin >= 0 && rect.xMax <= atlas.width && rect.yMax <= atlas.height,
                        "Sprite rectangles remain inside the atlas.");
                    Near(sprite.border.x, sourceBorder.x * rect.width / sourceWidth, "Left border follows packed width.");
                    Near(sprite.border.y, sourceBorder.y * rect.height / SourceHeight, "Bottom border follows packed height.");
                    Near(sprite.border.z, sourceBorder.z * rect.width / sourceWidth, "Right border follows packed width.");
                    Near(sprite.border.w, sourceBorder.w * rect.height / SourceHeight, "Top border follows packed height.");
                    Near(sprite.bounds.size.x, rect.width / sprite.pixelsPerUnit, "Existing Editor fallback retains the packed width.");
                    Near(sprite.bounds.size.y, rect.height / sprite.pixelsPerUnit, "Existing Editor fallback retains the packed height.");
                    Check(sprite.triangles.Length >= 6, "The sprite retains rectangle triangle indices.");
                    if (sourceWidth > limit)
                    {
                        Check(rect.width < sourceWidth, "Small-cap packing reduces the source's excessive width.");
                    }
                }
                Debug.Log($"CUSTOM_SKIN_ATLAS_CASE limit={limit} async={asynchronous} actual={useActualLimit} atlas={atlas.width}x{atlas.height} sprite={sprites[0].rect.width}x{sprites[0].rect.height} expectedGeometryErrors={expectedGeometryErrors}");
            }
            finally
            {
                Application.logMessageReceived -= captureErrors;
                foreach (var sprite in sprites)
                {
                    Object.DestroyImmediate(sprite);
                }
                if (atlas != null)
                {
                    Object.DestroyImmediate(atlas);
                }
            }
        }

        /// <summary>
        /// Requires two floating-point values to agree within a small tolerance.
        /// </summary>
        /// <param name="actual">The observed value.</param>
        /// <param name="expected">The required value.</param>
        /// <param name="message">The check's purpose.</param>
        /// <exception cref="InvalidOperationException">The values differ.</exception>
        private static void Near(float actual, float expected, string message)
        {
            Check(Mathf.Abs(actual - expected) < 0.001f, $"{message} Expected {expected}, got {actual}.");
        }

        /// <summary>
        /// Requires a validation condition to hold.
        /// </summary>
        /// <param name="condition">The condition to verify.</param>
        /// <param name="message">The failure explanation.</param>
        /// <exception cref="InvalidOperationException">The condition is false.</exception>
        private static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
