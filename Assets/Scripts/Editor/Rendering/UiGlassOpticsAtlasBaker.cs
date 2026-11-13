using System;
using System.Collections.Generic;
using MajdataPlay.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Sprites;
#nullable enable

namespace MajdataPlay.Editor.Rendering
{
    /// <summary>Bakes authored source-space contours without modifying scenes, prefabs or their artwork.</summary>
    public static class UiGlassOpticsAtlasBaker
    {
        /// <summary>Reserves two distance-field guard texels outside every original Sprite rectangle.</summary>
        private const int Padding = 2;
        /// <summary>Bounds each contour's raster independently of its original source-space geometry and distance units.</summary>
        private const int MaximumRasterDimension = 1020;
        /// <summary>Samples the fixed bounded optical model, including both exact interval endpoints.</summary>
        private const int ProfileSamples = 256;
        /// <summary>Identifies the generated asset and its two native texture subassets.</summary>
        private const string AssetPath = "Assets/Resources/Rendering/UI/GlassOpticsAtlas.asset";

        /// <summary>Rebuilds distinct contours from the project's existing scene and prefab assets.</summary>
        /// <exception cref="InvalidOperationException">The editor is playing, no contours exist, or packing exceeds 4096 pixels.</exception>
        [MenuItem("Tools/MajdataPlay/Rendering/Bake Glass Optics Atlas")]
        public static void Bake()
        {
            if (Application.isPlaying)
            {
                throw new InvalidOperationException("Stop Play Mode before baking glass optics.");
            }
            var contours = CollectContours();
            if (contours.Count == 0)
            {
                throw new InvalidOperationException("No authored glass contours were found.");
            }
            contours.Sort(CompareContours);
            var rasterSizes = new Vector2Int[contours.Count];
            var area = 0;
            var widest = 0;
            for (var index = 0; index < contours.Count; index++)
            {
                var rasterSize = GetRasterSize(contours[index].Contour.Size);
                rasterSizes[index] = rasterSize;
                var width = rasterSize.x + Padding * 2;
                var height = rasterSize.y + Padding * 2;
                area += width * height;
                widest = Mathf.Max(widest, width);
            }
            var atlasWidth = Mathf.NextPowerOfTwo(Mathf.Max(widest, Mathf.CeilToInt(Mathf.Sqrt(area))));
            var positions = new Vector2Int[contours.Count];
            var x = 0;
            var y = 0;
            var rowHeight = 0;
            for (var index = 0; index < contours.Count; index++)
            {
                var width = rasterSizes[index].x + Padding * 2;
                var height = rasterSizes[index].y + Padding * 2;
                if (x + width > atlasWidth)
                {
                    x = 0;
                    y += rowHeight;
                    rowHeight = 0;
                }
                positions[index] = new Vector2Int(x, y);
                x += width;
                rowHeight = Mathf.Max(rowHeight, height);
            }
            var atlasHeight = Mathf.NextPowerOfTwo(y + rowHeight);
            if (atlasWidth > 4096 || atlasHeight > 4096)
            {
                throw new InvalidOperationException("The glass distance atlas exceeds its bounded 4096-pixel packing budget.");
            }
            var pixels = new Color32[atlasWidth * atlasHeight];
            var entries = new UiGlassOpticsAtlas.Entry[contours.Count];
            for (var index = 0; index < contours.Count; index++)
            {
                var source = contours[index];
                var contour = source.Contour;
                var innerWidth = rasterSizes[index].x;
                var innerHeight = rasterSizes[index].y;
                var position = positions[index];
                var distances = contour.AlphaContourThreshold > 0f
                    ? BakeArtworkDistances(source, innerWidth, innerHeight) : null;
                var paddedWidth = innerWidth + Padding * 2;
                var minimum = float.PositiveInfinity;
                var maximum = float.NegativeInfinity;
                for (var py = -Padding; py < innerHeight + Padding; py++)
                {
                    for (var px = -Padding; px < innerWidth + Padding; px++)
                    {
                        var value = distances != null ? distances[(py + Padding) * paddedWidth + px + Padding]
                            : EvaluateTexelDistance(in contour, px, py, innerWidth, innerHeight);
                        if (float.IsNaN(value) || float.IsInfinity(value))
                        {
                            throw new InvalidOperationException("The authored glass contour produced a non-finite source distance.");
                        }
                        minimum = Mathf.Min(minimum, value);
                        maximum = Mathf.Max(maximum, value);
                    }
                }
                var decode = new Vector2(Mathf.Max(maximum - minimum, 0.00001f), minimum);
                for (var py = -Padding; py < innerHeight + Padding; py++)
                {
                    for (var px = -Padding; px < innerWidth + Padding; px++)
                    {
                        var value = distances != null ? distances[(py + Padding) * paddedWidth + px + Padding]
                            : EvaluateTexelDistance(in contour, px, py, innerWidth, innerHeight);
                        pixels[(position.y + py + Padding) * atlasWidth + position.x + px + Padding] = EncodeDistance(value, decode);
                    }
                }
                var scale = new Vector2(innerWidth / (atlasWidth * contour.Size.x), innerHeight / (atlasHeight * contour.Size.y));
                var mapping = new Vector4(scale.x, scale.y, (position.x + Padding + innerWidth * 0.5f) / atlasWidth,
                    (position.y + Padding + innerHeight * 0.5f) / atlasHeight);
                var clamp = new Vector4((position.x + 0.5f) / atlasWidth, (position.y + 0.5f) / atlasHeight,
                    (position.x + innerWidth + Padding * 2 - 0.5f) / atlasWidth,
                    (position.y + innerHeight + Padding * 2 - 0.5f) / atlasHeight);
                entries[index] = new UiGlassOpticsAtlas.Entry(contour, mapping, clamp, decode);
            }
            var distance = CreateTexture(atlasWidth, atlasHeight, TextureFormat.RGBA32, "Glass Encoded Source Distances");
            distance.SetPixelData(pixels, 0);
            distance.Apply(false, true);
            var profile = CreateProfile();
            EnsureFolder("Assets/Resources/Rendering/UI");
            var atlas = AssetDatabase.LoadAssetAtPath<UiGlassOpticsAtlas>(AssetPath);
            if (atlas == null)
            {
                atlas = ScriptableObject.CreateInstance<UiGlassOpticsAtlas>();
                AssetDatabase.CreateAsset(atlas, AssetPath);
            }
            foreach (var previous in AssetDatabase.LoadAllAssetsAtPath(AssetPath))
            {
                if (previous is Texture2D)
                {
                    UnityEngine.Object.DestroyImmediate(previous, true);
                }
            }
            AssetDatabase.AddObjectToAsset(distance, atlas);
            AssetDatabase.AddObjectToAsset(profile, atlas);
            atlas.Initialize(distance, profile, entries);
            EditorUtility.SetDirty(atlas);
            AssetDatabase.SaveAssetIfDirty(atlas);
            UiGlassOpticsAtlas.ResetCache();
            Debug.Log($"[Glass Optics] Baked {contours.Count} contours into {atlasWidth}x{atlasHeight} bilinear RGBA8 UNORM v{UiGlassOpticsAtlas.CurrentEncodingVersion}; profile {ProfileSamples}x1 RGHalf.");
        }

        /// <summary>Gets a bounded raster while retaining the original contour dimensions and source-distance units.</summary>
        /// <param name="sourceSize">The positive original Sprite dimensions.</param>
        /// <returns>A proportional raster with a longest side of at most 1020, before two guard texels on each side.</returns>
        private static Vector2Int GetRasterSize(Vector2 sourceSize)
        {
            var width = Mathf.Max(1, Mathf.CeilToInt(sourceSize.x));
            var height = Mathf.Max(1, Mathf.CeilToInt(sourceSize.y));
            if (width <= MaximumRasterDimension && height <= MaximumRasterDimension)
            {
                return new Vector2Int(width, height);
            }
            if (width >= height)
            {
                return new Vector2Int(MaximumRasterDimension,
                    Mathf.Max(1, (int)Math.Ceiling(height * (double)MaximumRasterDimension / width)));
            }
            return new Vector2Int(Mathf.Max(1, (int)Math.Ceiling(width * (double)MaximumRasterDimension / height)),
                MaximumRasterDimension);
        }

        /// <summary>Evaluates one raster texel in original Sprite-space distance units, including its analytic guard.</summary>
        /// <param name="contour">The original authored contour.</param>
        /// <param name="x">The source-grid horizontal texel coordinate, including negative guard coordinates.</param>
        /// <param name="y">The source-grid vertical texel coordinate, including negative guard coordinates.</param>
        /// <param name="width">The positive source-grid width.</param>
        /// <param name="height">The positive source-grid height.</param>
        /// <returns>The smooth source distance before the clipped ellipse's runtime crop.</returns>
        private static float EvaluateTexelDistance(in UiGlassOpticsAtlas.Contour contour, int x, int y, int width, int height)
        {
            var source = new Vector2((x + 0.5f) * contour.Size.x / width - contour.Size.x * 0.5f,
                (y + 0.5f) * contour.Size.y / height - contour.Size.y * 0.5f);
            return EvaluateDistance(in contour, source);
        }

        /// <summary>Packs one normalized distance into monotone coarse channels and a fine residual for linear shader decoding.</summary>
        /// <param name="distance">The finite distance in original Sprite units.</param>
        /// <param name="decode">The positive normalization range and signed source-unit minimum.</param>
        /// <returns>Three coarse bytes and one residual byte, sampled in linear color space without compression.</returns>
        private static Color32 EncodeDistance(float distance, Vector2 decode)
        {
            var normalized = Math.Max(0d, Math.Min(1d, (distance - decode.y) / (double)decode.x));
            var scaled = normalized * 765d;
            var coarse = (int)Math.Floor(scaled);
            var residual = (byte)Math.Round((scaled - coarse) * 255d);
            return new Color32((byte)Math.Max(0, Math.Min(255, coarse)),
                (byte)Math.Max(0, Math.Min(255, coarse - 255)),
                (byte)Math.Max(0, Math.Min(255, coarse - 510)), residual);
        }

        /// <summary>Orders every geometry variant deterministically, independently of AssetDatabase enumeration.</summary>
        /// <param name="leftSource">The first authored contour and source.</param><param name="rightSource">The second authored contour and source.</param>
        /// <returns>The packing order, including a complete serialized-geometry tie break.</returns>
        private static int CompareContours(BakeSource leftSource, BakeSource rightSource)
        {
            var left = leftSource.Contour;
            var right = rightSource.Contour;
            var leftRaster = GetRasterSize(left.Size);
            var rightRaster = GetRasterSize(right.Size);
            var order = rightRaster.y.CompareTo(leftRaster.y);
            if (order == 0)
            {
                order = rightRaster.x.CompareTo(leftRaster.x);
            }
            if (order == 0)
            {
                order = StringComparer.Ordinal.Compare(left.SpriteName, right.SpriteName);
            }
            if (order == 0)
            {
                order = StringComparer.Ordinal.Compare(JsonUtility.ToJson(left), JsonUtility.ToJson(right));
            }
            return order;
        }

        /// <summary>Reads only assets depending on the production panel component, using disposable preview contents.</summary>
        /// <returns>The distinct descriptors supplied by the production geometry policy.</returns>
        private static List<BakeSource> CollectContours()
        {
            var contours = new List<BakeSource>();
            var scripts = AssetDatabase.FindAssets("UiBackdropPanel t:MonoScript", new[] { "Assets" });
            var panelPath = scripts.Length > 0 ? AssetDatabase.GUIDToAssetPath(scripts[0]) : string.Empty;
            foreach (var filter in new[] { "t:Prefab", "t:Scene" })
            {
                foreach (var guid in AssetDatabase.FindAssets(filter, new[] { "Assets" }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(panelPath) || Array.IndexOf(AssetDatabase.GetDependencies(path), panelPath) < 0)
                    {
                        continue;
                    }
                    if (filter == "t:Prefab")
                    {
                        var root = PrefabUtility.LoadPrefabContents(path);
                        try
                        {
                            Collect(root, contours);
                        }
                        finally
                        {
                            PrefabUtility.UnloadPrefabContents(root);
                        }
                    }
                    else
                    {
                        var scene = EditorSceneManager.OpenPreviewScene(path);
                        try
                        {
                            foreach (var root in scene.GetRootGameObjects())
                            {
                                Collect(root, contours);
                            }
                        }
                        finally
                        {
                            EditorSceneManager.ClosePreviewScene(scene);
                        }
                    }
                }
            }
            return contours;
        }

        /// <summary>Deduplicates panel descriptors while retaining disabled authored states.</summary>
        /// <param name="root">The disposable prefab or scene root.</param><param name="contours">The accumulated distinct shapes.</param>
        private static void Collect(GameObject root, List<BakeSource> contours)
        {
            foreach (var panel in root.GetComponentsInChildren<UiBackdropPanel>(true))
            {
                if (!panel.TryGetOpticalContour(out var contour))
                {
                    continue;
                }
                var sprite = panel.GetComponent<QualityUiImage>().overrideSprite;
                var outerUv = DataUtility.GetOuterUV(sprite);
                if (UiAtlasMeshGenerator.TryGetLegacy(sprite, out var legacy, out var originalUv))
                {
                    sprite = legacy;
                    outerUv = originalUv;
                }
                var existing = contours.Find(item => item.Contour.Matches(contour));
                if (existing != null)
                {
                    if (contour.AlphaContourThreshold > 0f &&
                        (existing.Sprite.texture != sprite.texture || existing.OuterUv != outerUv))
                    {
                        throw new InvalidOperationException($"Ambiguous artwork contour source: {contour.SpriteName}.");
                    }
                    continue;
                }
                contours.Add(new BakeSource(contour, sprite, outerUv));
            }
        }

        /// <summary>Retains imported source artwork after its disposable scene or prefab preview closes.</summary>
        private sealed class BakeSource
        {
            /// <summary>Gets the production descriptor used unchanged by runtime matching.</summary>
            internal UiGlassOpticsAtlas.Contour Contour { get; }
            /// <summary>Gets the actual original logical Sprite asset.</summary>
            internal Sprite Sprite { get; }
            /// <summary>Gets its original tight sampling rectangle before restoring Sprite padding.</summary>
            internal Vector4 OuterUv { get; }

            /// <summary>Stores the production geometry and its actual imported artwork source.</summary>
            /// <param name="contour">The exact production contour.</param>
            /// <param name="sprite">The resolved original Sprite.</param>
            /// <param name="outerUv">The original tight source sampling bounds.</param>
            internal BakeSource(UiGlassOpticsAtlas.Contour contour, Sprite sprite, Vector4 outerUv)
            {
                Contour = contour;
                Sprite = sprite;
                OuterUv = outerUv;
            }
        }

        /// <summary>Bakes a thresholded artwork body using exact squared Euclidean transforms on its source-unit grid.</summary>
        /// <param name="source">The original artwork and production threshold.</param>
        /// <param name="width">The bounded interior raster width.</param>
        /// <param name="height">The bounded interior raster height.</param>
        /// <returns>Negative-inside source-unit distances, including a transparent guard border.</returns>
        /// <exception cref="InvalidOperationException">The source cannot be sampled or contains no body at its threshold.</exception>
        private static float[] BakeArtworkDistances(BakeSource source, int width, int height)
        {
            var pixels = ReadArtwork(source, width, height);
            var paddedWidth = width + Padding * 2;
            var paddedHeight = height + Padding * 2;
            var body = new bool[paddedWidth * paddedHeight];
            var anyInside = false;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var inside = pixels[y * width + x].a / 255f >= source.Contour.AlphaContourThreshold;
                    body[(y + Padding) * paddedWidth + x + Padding] = inside;
                    anyInside |= inside;
                }
            }
            if (!anyInside)
            {
                throw new InvalidOperationException($"Artwork contour has no body at its alpha threshold: {source.Contour.SpriteName}.");
            }
            var stepX = source.Contour.Size.x / (double)width;
            var stepY = source.Contour.Size.y / (double)height;
            var toInside = EuclideanDistanceTransform(body, paddedWidth, paddedHeight, true, stepX, stepY);
            var toOutside = EuclideanDistanceTransform(body, paddedWidth, paddedHeight, false, stepX, stepY);
            var distances = new float[body.Length];
            for (var index = 0; index < distances.Length; index++)
            {
                distances[index] = body[index] ? -(float)Math.Sqrt(toOutside[index]) : (float)Math.Sqrt(toInside[index]);
            }
            return distances;
        }

        /// <summary>Reads only the selected original Sprite rectangle at LOD0 with the existing linear-copy program.</summary>
        /// <param name="source">The original logical Sprite, tight UVs and padding.</param>
        /// <param name="width">The bounded raster width.</param>
        /// <param name="height">The bounded raster height.</param>
        /// <returns>The bounded source image in bottom-left-origin raster order.</returns>
        /// <exception cref="InvalidOperationException">A graphics device, valid source UVs or copy program is unavailable.</exception>
        private static Color32[] ReadArtwork(BakeSource source, int width, int height)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                throw new InvalidOperationException("Artwork contour baking requires a graphics device; omit -nographics.");
            }
            var shader = Shader.Find("Hidden/Maj/UI PV Gaussian");
            if (shader == null || !shader.isSupported)
            {
                throw new InvalidOperationException("The production linear-copy shader is unavailable for artwork contour baking.");
            }
            var padding = DataUtility.GetPadding(source.Sprite);
            var size = source.Contour.Size;
            var content = size - new Vector2(padding.x + padding.z, padding.y + padding.w);
            if (content.x <= 0f || content.y <= 0f)
            {
                throw new InvalidOperationException($"Invalid original Sprite padding: {source.Contour.SpriteName}.");
            }
            var pixelUv = new Vector2((source.OuterUv.z - source.OuterUv.x) / content.x,
                (source.OuterUv.w - source.OuterUv.y) / content.y);
            var minimumUv = new Vector2(source.OuterUv.x - padding.x * pixelUv.x,
                source.OuterUv.y - padding.y * pixelUv.y);
            var maximumUv = minimumUv + Vector2.Scale(size, pixelUv);
            if (!float.IsFinite(pixelUv.x) || !float.IsFinite(pixelUv.y) || pixelUv.x <= 0f || pixelUv.y <= 0f)
            {
                throw new InvalidOperationException($"Invalid original Sprite sampling bounds: {source.Contour.SpriteName}.");
            }
            var texture = source.Sprite.texture;
            var previousFilter = texture.filterMode;
            var previous = RenderTexture.active;
            RenderTexture? target = null;
            Material? material = null;
            Texture2D? reader = null;
            try
            {
                target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                material = new Material(shader);
                reader = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                texture.filterMode = FilterMode.Bilinear;
                RenderTexture.active = target;
                GL.Clear(false, true, Color.clear);
                material.SetTexture("_MainTex", texture);
                material.SetVector("_MainTex_TexelSize", new Vector4(1f / texture.width,
                    1f / texture.height, texture.width, texture.height));
                material.SetFloat("_MajUiSourceNeedsManualLinear", 0f);
                GL.PushMatrix();
                try
                {
                    GL.LoadOrtho();
                    if (!material.SetPass(3))
                    {
                        throw new InvalidOperationException("The production linear-copy pass could not be selected.");
                    }
                    GL.Begin(GL.QUADS);
                    try
                    {
                        GL.TexCoord2(minimumUv.x, minimumUv.y);
                        GL.Vertex3(0f, 0f, 0f);
                        GL.TexCoord2(minimumUv.x, maximumUv.y);
                        GL.Vertex3(0f, 1f, 0f);
                        GL.TexCoord2(maximumUv.x, maximumUv.y);
                        GL.Vertex3(1f, 1f, 0f);
                        GL.TexCoord2(maximumUv.x, minimumUv.y);
                        GL.Vertex3(1f, 0f, 0f);
                    }
                    finally
                    {
                        GL.End();
                    }
                }
                finally
                {
                    GL.PopMatrix();
                }
                reader.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                reader.Apply(false, false);
                return reader.GetPixels32();
            }
            finally
            {
                texture.filterMode = previousFilter;
                RenderTexture.active = previous;
                if (target != null)
                {
                    RenderTexture.ReleaseTemporary(target);
                }
                if (reader != null)
                {
                    UnityEngine.Object.DestroyImmediate(reader);
                }
                if (material != null)
                {
                    UnityEngine.Object.DestroyImmediate(material);
                }
            }
        }

        /// <summary>Applies weighted one-dimensional lower envelopes along both axes without finite-seed assumptions.</summary>
        /// <param name="body">The padded body mask.</param><param name="width">Its width.</param><param name="height">Its height.</param>
        /// <param name="targetInside">Selects inside or outside pixels as zero-distance seeds.</param>
        /// <param name="stepX">The original source units per horizontal raster texel.</param>
        /// <param name="stepY">The original source units per vertical raster texel.</param>
        /// <returns>Exact squared distances to the selected raster pixel centers in source units.</returns>
        private static double[] EuclideanDistanceTransform(bool[] body, int width, int height, bool targetInside, double stepX, double stepY)
        {
            var field = new double[body.Length];
            var maximum = Math.Max(width, height);
            var input = new double[maximum];
            var output = new double[maximum];
            var sites = new int[maximum];
            var boundaries = new double[maximum + 1];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    input[x] = body[y * width + x] == targetInside ? 0d : double.PositiveInfinity;
                }
                TransformDistanceLine(input, output, width, stepX * stepX, sites, boundaries);
                Array.Copy(output, 0, field, y * width, width);
            }
            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++)
                {
                    input[y] = field[y * width + x];
                }
                TransformDistanceLine(input, output, height, stepY * stepY, sites, boundaries);
                for (var y = 0; y < height; y++)
                {
                    field[y * width + x] = output[y];
                }
            }
            return field;
        }

        /// <summary>Finds the lower envelope of finite quadratic seeds, including rows or columns with no seeds.</summary>
        /// <param name="input">The squared seed costs.</param><param name="output">The receiving squared distances.</param>
        /// <param name="length">The active line length.</param><param name="weight">The positive squared source-unit texel spacing.</param>
        /// <param name="sites">The reusable parabola-index buffer.</param><param name="boundaries">The reusable envelope-boundary buffer.</param>
        private static void TransformDistanceLine(double[] input, double[] output, int length, double weight, int[] sites, double[] boundaries)
        {
            var last = -1;
            for (var q = 0; q < length; q++)
            {
                if (double.IsPositiveInfinity(input[q]))
                {
                    continue;
                }
                var intersection = double.NegativeInfinity;
                while (last >= 0)
                {
                    var previous = sites[last];
                    intersection = ((input[q] - input[previous]) / weight + (double)q * q - (double)previous * previous)
                        / (2d * (q - previous));
                    if (intersection > boundaries[last])
                    {
                        break;
                    }
                    last--;
                }
                last++;
                sites[last] = q;
                boundaries[last] = last == 0 ? double.NegativeInfinity : intersection;
                boundaries[last + 1] = double.PositiveInfinity;
            }
            if (last < 0)
            {
                for (var q = 0; q < length; q++)
                {
                    output[q] = double.PositiveInfinity;
                }
                return;
            }
            var active = 0;
            for (var q = 0; q < length; q++)
            {
                while (boundaries[active + 1] < q)
                {
                    active++;
                }
                var delta = q - sites[active];
                output[q] = input[sites[active]] + weight * delta * delta;
            }
        }

        /// <summary>Stores smooth source fields without duplicating the production layout policy or interpolating a clipping crease.</summary>
        /// <param name="contour">The exact panel-provided geometry.</param><param name="source">The original centered Sprite position.</param>
        /// <returns>The source-unit distance, or the unclipped ellipse field whose slab intersection is applied after sampling.</returns>
        private static float EvaluateDistance(in UiGlassOpticsAtlas.Contour contour, Vector2 source)
        {
            var p = source - new Vector2(contour.Rect.x, contour.Rect.y);
            var half = new Vector2(Mathf.Max(contour.Rect.z, 0.0001f), Mathf.Max(contour.Rect.w, 0.0001f));
            if (contour.Shape.y > 0.5f)
            {
                var radius = contour.Circle.z > 0f ? contour.Circle.z : half.x;
                var radiusY = contour.Circle.w > 0f ? contour.Circle.w : radius;
                var q = new Vector2((p.x - contour.Circle.x) / Mathf.Max(radius, 0.0001f),
                    (p.y - contour.Circle.y) / Mathf.Max(radiusY, 0.0001f));
                return (q.magnitude - 1f) * Mathf.Min(radius, radiusY);
            }
            if (contour.Shape.x > 0.5f)
            {
                return (new Vector2(p.x / half.x, p.y / half.y).magnitude - 1f) * Mathf.Min(half.x, half.y);
            }
            var corners = contour.Corners;
            var radiusAtY = p.y >= 0f ? (p.x < 0f ? corners.x : corners.y) : (p.x < 0f ? corners.w : corners.z);
            var r = Mathf.Clamp(radiusAtY, 0f, Mathf.Min(half.x, half.y));
            var corner = new Vector2(Mathf.Abs(p.x) - half.x + r, Mathf.Abs(p.y) - half.y + r);
            return new Vector2(Mathf.Max(corner.x, 0f), Mathf.Max(corner.y, 0f)).magnitude + Mathf.Min(Mathf.Max(corner.x, corner.y), 0f) - r;
        }

        /// <summary>Creates the exact normalized cap and Snell model shared by all current panels.</summary>
        /// <returns>The mip-free, endpoint-centered displacement and edge-weight profile.</returns>
        private static Texture2D CreateProfile()
        {
            var data = new ushort[ProfileSamples * 2];
            for (var index = 0; index < ProfileSamples; index++)
            {
                var t = index / (float)(ProfileSamples - 1);
                var edge = 1f - t;
                var height = 1.2f * (1f - edge * edge * edge);
                var normal = new Vector2(3.6f * edge * edge, 1f).normalized;
                const float eta = 1f / 1.45f;
                var transmitted = Mathf.Sqrt(Mathf.Max(1f - eta * eta * normal.x * normal.x, 0f));
                var ray = transmitted - eta * normal.y;
                var displacement = ray * normal.x / Mathf.Max(eta + ray * normal.y, 0.0001f) * height * 1.15f;
                data[index * 2] = Mathf.FloatToHalf(displacement);
                data[index * 2 + 1] = Mathf.FloatToHalf(1f - normal.y);
            }
            var texture = CreateTexture(ProfileSamples, 1, TextureFormat.RGHalf, "Glass Bounded Lens Profile");
            texture.SetPixelData(data, 0);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>Creates data textures with no color conversion, mip chain or anisotropic filtering.</summary>
        /// <param name="width">The texture width.</param><param name="height">The texture height.</param>
        /// <param name="format">The uncompressed data format.</param><param name="name">The generated subasset name.</param>
        /// <returns>The explicitly linear, bilinear-clamped data texture.</returns>
        private static Texture2D CreateTexture(int width, int height, TextureFormat format, string name)
        {
            return new Texture2D(width, height, format, false, true)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
                mipMapBias = 0f,
                ignoreMipmapLimit = true
            };
        }

        /// <summary>Creates only the generated resource's missing asset folders.</summary>
        /// <param name="path">The project-relative target folder.</param>
        private static void EnsureFolder(string path)
        {
            var parts = path.Split('/');
            var current = parts[0];
            for (var index = 1; index < parts.Length; index++)
            {
                var next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[index]);
                }
                current = next;
            }
        }
    }
}
