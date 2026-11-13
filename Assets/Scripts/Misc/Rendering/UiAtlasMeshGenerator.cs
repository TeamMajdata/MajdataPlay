using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;
#nullable enable

namespace MajdataPlay.Rendering
{
    /// <summary>Reuses native UGUI geometry while extending only its artwork sampling margins.</summary>
    [AddComponentMenu("")]
    public sealed class UiAtlasMeshGenerator : Image
    {
        /// <summary>Locates the original logical geometry without loading outlined artwork.</summary>
        private const string LayoutPath = "Rendering/UI/AtlasLayout";
        /// <summary>Identifies Images that use the paired Ribbon atlases.</summary>
        private const string AtlasName = "newui@3x";
        /// <summary>Allows for float roundoff when identifying native mesh edges.</summary>
        private const float Epsilon = 0.000001f;
        /// <summary>Allows for local-space Canvas float roundoff when classifying fill boundaries.</summary>
        private const float GeometryEpsilon = 0.0001f;
        /// <summary>Caches sprite-name lookups after the first logical layout request.</summary>
        private static readonly Dictionary<string, int> s_indices = new();
        /// <summary>Reuses verified imported-Sprite mappings without reading native name strings each frame.</summary>
        private static readonly Dictionary<Sprite, LegacyMapping> s_verifiedSprites = new();
        /// <summary>Bounds retention when callers create transient Sprite clones of the authored atlas.</summary>
        private const int MaximumVerifiedSprites = 256;
        /// <summary>Retains the logical layout and original imported Sprite subassets.</summary>
        private static UiAtlasLayout? s_layout;
        /// <summary>Provides one disabled, non-rendering native Image for all mesh rebuilds.</summary>
        private static UiAtlasMeshGenerator? s_generator;
        /// <summary>Reuses expanded triangle input for Filled Images.</summary>
        private static readonly List<UIVertex> s_triangles = new(36);
        /// <summary>Reuses one half-plane clipping polygon.</summary>
        private static readonly List<UIVertex> s_polygon = new(8);
        /// <summary>Reuses the other half-plane clipping polygon.</summary>
        private static readonly List<UIVertex> s_clipped = new(8);
        /// <summary>Retains the helper's Canvas solely for pixels-per-unit calculation.</summary>
        private Canvas? _geometryCanvas;

        /// <summary>Releases transient helpers before a new runtime session.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            if (s_generator != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(s_generator.gameObject);
                }
                else
                {
                    DestroyImmediate(s_generator.gameObject);
                }
            }
            s_generator = null;
            InvalidateSpriteMappings();
        }

        /// <summary>Forgets imported metadata when the atlas is reloaded or edited.</summary>
        internal static void InvalidateSpriteMappings()
        {
            s_layout = null;
            s_indices.Clear();
            s_verifiedSprites.Clear();
        }

#if UNITY_EDITOR
        /// <summary>Invalidates imported metadata after Editor asset changes, including Play without a domain reload.</summary>
        [UnityEditor.InitializeOnLoadMethod]
        private static void ObserveProjectChanges()
        {
            UnityEditor.EditorApplication.projectChanged -= InvalidateSpriteMappings;
            UnityEditor.EditorApplication.projectChanged += InvalidateSpriteMappings;
        }
#endif

        /// <summary>Finds the preserved logical Sprite independently of expanded import rectangles.</summary>
        /// <param name="source">The authored active Sprite, including Sprite Swap.</param>
        /// <param name="legacy">The original native Sprite geometry.</param>
        /// <param name="outerUV">The original normalized sampling rectangle.</param>
        /// <returns>Whether this Sprite belongs to the preserved atlas layout.</returns>
        internal static bool TryGetLegacy(Sprite? source, out Sprite legacy, out Vector4 outerUV)
        {
            legacy = null!;
            outerUV = default;
            if (source == null)
            {
                return false;
            }
            var texture = source.texture;
            if (texture == null)
            {
                return false;
            }
            if (s_layout != null && s_verifiedSprites.TryGetValue(source, out var verified) &&
                verified.Texture == texture && verified.Legacy != null)
            {
                legacy = verified.Legacy;
                outerUV = verified.OuterUV;
                return true;
            }
            if (texture.name != AtlasName)
            {
                return false;
            }
            if (s_layout == null)
            {
                s_layout = Resources.Load<UiAtlasLayout>(LayoutPath);
                s_indices.Clear();
                if (s_layout != null)
                {
                    for (var index = 0; index < s_layout.LegacySprites.Count; index++)
                    {
                        s_indices.Add(s_layout.LegacySprites[index].name, index);
                    }
                }
            }
            if (s_layout == null || !s_indices.TryGetValue(source.name, out var entry) ||
                entry >= s_layout.OriginalOuterUVs.Count)
            {
                return false;
            }
            legacy = s_layout.LegacySprites[entry];
            outerUV = s_layout.OriginalOuterUVs[entry];
            if (legacy == null)
            {
                return false;
            }
            if (s_verifiedSprites.Count >= MaximumVerifiedSprites)
            {
                s_verifiedSprites.Clear();
            }
            s_verifiedSprites[source] = new LegacyMapping(texture, legacy, outerUV);
            return true;
        }

        /// <summary>Retains only immutable imported geometry and its unchanged texture identity.</summary>
        private readonly struct LegacyMapping
        {
            /// <summary>Gets the source texture whose authored naming contract was verified.</summary>
            internal Texture2D Texture { get; }
            /// <summary>Gets the preserved logical Sprite.</summary>
            internal Sprite Legacy { get; }
            /// <summary>Gets the original normalized texture rectangle.</summary>
            internal Vector4 OuterUV { get; }

            /// <summary>Stores one successful mapping; failed requests are never retained.</summary>
            /// <param name="texture">The verified source atlas.</param>
            /// <param name="legacy">The preserved logical Sprite.</param>
            /// <param name="outerUV">The unchanged original texture coordinates.</param>
            internal LegacyMapping(Texture2D texture, Sprite legacy, Vector4 outerUV)
            {
                Texture = texture;
                Legacy = legacy;
                OuterUV = outerUV;
            }
        }

        /// <summary>Generates the unchanged logical mesh and optionally extends its sampling margins.</summary>
        /// <param name="source">The actual UI control whose color and native Image settings are retained.</param>
        /// <param name="legacy">The preserved pre-expansion native Sprite.</param>
        /// <param name="outerUV">The captured original sampling rectangle.</param>
        /// <param name="expanded">Whether to include the full outlined slice beyond its old Tight bounds.</param>
        /// <param name="target">The destination Canvas mesh.</param>
        internal static void Populate(Image source, Sprite legacy, Vector4 outerUV, bool expanded, VertexHelper target)
        {
            if (s_generator == null)
            {
                // Recover the disabled editor helper after a script-domain reload rather than
                // leaving another hidden Canvas behind. This scan occurs only at cache creation.
                foreach (var existing in Resources.FindObjectsOfTypeAll<UiAtlasMeshGenerator>())
                {
                    if (existing.gameObject.hideFlags == HideFlags.HideAndDontSave)
                    {
                        s_generator = existing;
                        s_generator._geometryCanvas = existing.GetComponent<Canvas>();
                        break;
                    }
                }
                if (s_generator == null)
                {
                    var helper = new GameObject("UI Atlas Geometry", typeof(RectTransform));
                    helper.hideFlags = HideFlags.HideAndDontSave;
                    helper.SetActive(false);
                    var canvas = helper.AddComponent<Canvas>();
                    canvas.renderMode = RenderMode.WorldSpace;
                    s_generator = helper.AddComponent<UiAtlasMeshGenerator>();
                    s_generator.enabled = false;
                    s_generator._geometryCanvas = canvas;
                    helper.SetActive(true);
                }
                if (Application.isPlaying)
                {
                    DontDestroyOnLoad(s_generator.gameObject);
                }
            }
            var generator = s_generator;
            var rect = source.GetPixelAdjustedRect();
            generator._geometryCanvas!.referencePixelsPerUnit = source.canvas != null ? source.canvas.referencePixelsPerUnit : 100f;
            generator.rectTransform.pivot = new Vector2(rect.width != 0f ? -rect.x / rect.width : 0.5f,
                rect.height != 0f ? -rect.y / rect.height : 0.5f);
            generator.rectTransform.sizeDelta = rect.size;
            generator.sprite = legacy;
            generator.overrideSprite = null;
            generator.type = source.type;
            generator.color = source.color;
            generator.preserveAspect = source.preserveAspect;
            generator.useSpriteMesh = source.useSpriteMesh;
            generator.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
            generator.fillCenter = source.fillCenter;
            generator.fillMethod = source.fillMethod;
            generator.fillOrigin = source.fillOrigin;
            generator.fillClockwise = source.fillClockwise;
            generator.fillAmount = source.fillAmount;
            generator.Generate(target);

            // Sprite subassets keep their logical geometry. Normalize their UVs explicitly in case
            // the shared texture has since been imported at a different platform resolution.
            var current = DataUtility.GetOuterUV(legacy);
            for (var index = 0; index < target.currentVertCount; index++)
            {
                var vertex = default(UIVertex);
                target.PopulateUIVertex(ref vertex, index);
                vertex.uv0.x = Remap(vertex.uv0.x, current.x, current.z, outerUV.x, outerUV.z);
                vertex.uv0.y = Remap(vertex.uv0.y, current.y, current.w, outerUV.y, outerUV.w);
                target.SetUIVertex(vertex, index);
            }
            if (!expanded || target.currentVertCount == 0)
            {
                return;
            }
            var full = DataUtility.GetOuterUV(source.overrideSprite);
            // Native Tight meshes can include a subpixel extrusion beyond the authored rectangle.
            // Keep that already-visible range as well as the newly available shadow margins.
            full = new Vector4(Mathf.Min(full.x, outerUV.x), Mathf.Min(full.y, outerUV.y),
                Mathf.Max(full.z, outerUV.z), Mathf.Max(full.w, outerUV.w));
            if (source.type == Type.Filled)
            {
                ExpandFilled(target, generator.GetLogicalDrawingRect(legacy), outerUV, full);
            }
            else
            {
                ExpandQuads(target, legacy, outerUV, full);
            }
        }

        /// <summary>Calls Unity's native Image mesh implementation without enabling another renderer.</summary>
        /// <param name="target">The destination mesh.</param>
        private void Generate(VertexHelper target)
        {
            base.OnPopulateMesh(target);
        }

        /// <summary>Reproduces Image.GetDrawingDimensions for the preserved Filled Sprite.</summary>
        /// <param name="legacy">The original native Sprite.</param>
        /// <returns>The original Tight sampling rectangle in local UI units.</returns>
        private Rect GetLogicalDrawingRect(Sprite legacy)
        {
            var rect = GetPixelAdjustedRect();
            var size = legacy.rect.size;
            if (preserveAspect && size.sqrMagnitude > 0f)
            {
                var aspect = size.x / size.y;
                if (aspect > rect.width / rect.height)
                {
                    var height = rect.height;
                    rect.height = rect.width / aspect;
                    rect.y += (height - rect.height) * rectTransform.pivot.y;
                }
                else
                {
                    var width = rect.width;
                    rect.width = rect.height * aspect;
                    rect.x += (width - rect.width) * rectTransform.pivot.x;
                }
            }
            var padding = DataUtility.GetPadding(legacy);
            var widthPixels = Mathf.RoundToInt(size.x);
            var heightPixels = Mathf.RoundToInt(size.y);
            return Rect.MinMaxRect(rect.x + rect.width * (padding.x / widthPixels),
                rect.y + rect.height * (padding.y / heightPixels),
                rect.x + rect.width * ((widthPixels - padding.z) / widthPixels),
                rect.y + rect.height * ((heightPixels - padding.w) / heightPixels));
        }

        /// <summary>Extends outer quad edges by affine extrapolation, retaining every original texel's position.</summary>
        /// <param name="target">The native Simple, Sliced or Tiled mesh.</param>
        /// <param name="legacy">The original logical Sprite whose border cropping is retained.</param>
        /// <param name="capturedOuter">The original normalized Tight outer UVs.</param>
        /// <param name="full">The expanded full-rectangle outer UVs.</param>
        private static void ExpandQuads(VertexHelper target, Sprite legacy, Vector4 capturedOuter, Vector4 full)
        {
            // Native Sliced generation can omit an outer row when its old border is zero or
            // compressed below Tight padding. Extend the actual surviving mesh boundary too.
            var edge = default(UIVertex);
            target.PopulateUIVertex(ref edge, 0);
            var outer = new Vector4(edge.uv0.x, edge.uv0.y, edge.uv0.x, edge.uv0.y);
            for (var index = 1; index < target.currentVertCount; index++)
            {
                target.PopulateUIVertex(ref edge, index);
                outer.x = Mathf.Min(outer.x, edge.uv0.x);
                outer.y = Mathf.Min(outer.y, edge.uv0.y);
                outer.z = Mathf.Max(outer.z, edge.uv0.x);
                outer.w = Mathf.Max(outer.w, edge.uv0.y);
            }
            full = new Vector4(Mathf.Min(full.x, outer.x), Mathf.Min(full.y, outer.y),
                Mathf.Max(full.z, outer.z), Mathf.Max(full.w, outer.w));
            var padding = DataUtility.GetPadding(legacy);
            var size = legacy.rect.size;
            var pixelU = (capturedOuter.z - capturedOuter.x) / (size.x - padding.x - padding.z);
            var pixelV = (capturedOuter.w - capturedOuter.y) / (size.y - padding.y - padding.w);
            var logicalOuter = new Vector4(capturedOuter.x - padding.x * pixelU,
                capturedOuter.y - padding.y * pixelV, capturedOuter.z + padding.z * pixelU,
                capturedOuter.w + padding.w * pixelV);
            // A zero border may omit a row but still expose the full logical slice: its shadow
            // may expand. A compressed border that shows only an interior texture region is a
            // deliberate native crop; do not add previously invisible body content there.
            if (Mathf.Abs(outer.x - capturedOuter.x) > Epsilon && outer.x > logicalOuter.x + Epsilon)
            {
                full.x = outer.x;
            }
            if (Mathf.Abs(outer.y - capturedOuter.y) > Epsilon && outer.y > logicalOuter.y + Epsilon)
            {
                full.y = outer.y;
            }
            if (Mathf.Abs(outer.z - capturedOuter.z) > Epsilon && outer.z < logicalOuter.z - Epsilon)
            {
                full.z = outer.z;
            }
            if (Mathf.Abs(outer.w - capturedOuter.w) > Epsilon && outer.w < logicalOuter.w - Epsilon)
            {
                full.w = outer.w;
            }
            for (var start = 0; start + 3 < target.currentVertCount; start += 4)
            {
                var first = default(UIVertex);
                var opposite = default(UIVertex);
                target.PopulateUIVertex(ref first, start);
                target.PopulateUIVertex(ref opposite, start + 2);
                for (var offset = 0; offset < 4; offset++)
                {
                    var vertex = default(UIVertex);
                    target.PopulateUIVertex(ref vertex, start + offset);
                    var u = ExtendCoordinate(vertex.uv0.x, outer.x, outer.z, full.x, full.z);
                    var v = ExtendCoordinate(vertex.uv0.y, outer.y, outer.w, full.y, full.w);
                    if (u != vertex.uv0.x)
                    {
                        vertex.position.x = Remap(u, first.uv0.x, opposite.uv0.x, first.position.x, opposite.position.x);
                    }
                    if (v != vertex.uv0.y)
                    {
                        vertex.position.y = Remap(v, first.uv0.y, opposite.uv0.y, first.position.y, opposite.position.y);
                    }
                    vertex.uv0.x = u;
                    vertex.uv0.y = v;
                    target.SetUIVertex(vertex, start + offset);
                }
            }
        }

        /// <summary>Extends Filled geometry while keeping its original radial or linear cut planes fixed.</summary>
        /// <param name="target">The native Filled mesh.</param>
        /// <param name="drawing">The original full Tight drawing bounds before the fill cut.</param>
        /// <param name="outer">The original outer UVs.</param>
        /// <param name="full">The expanded outer UVs.</param>
        private static void ExpandFilled(VertexHelper target, Rect drawing, Vector4 outer, Vector4 full)
        {
            s_triangles.Clear();
            target.GetUIVertexStream(s_triangles);
            target.Clear();
            var bounds = Rect.MinMaxRect(Remap(full.x, outer.x, outer.z, drawing.xMin, drawing.xMax),
                Remap(full.y, outer.y, outer.w, drawing.yMin, drawing.yMax),
                Remap(full.z, outer.x, outer.z, drawing.xMin, drawing.xMax),
                Remap(full.w, outer.y, outer.w, drawing.yMin, drawing.yMax));
            for (var triangle = 0; triangle + 2 < s_triangles.Count; triangle += 3)
            {
                var a = s_triangles[triangle];
                var b = s_triangles[triangle + 1];
                var c = s_triangles[triangle + 2];
                var orientation = Cross(b.position - a.position, c.position - a.position);
                if (Mathf.Abs(orientation) <= Epsilon)
                {
                    continue;
                }
                s_polygon.Clear();
                AddCorner(a, bounds.xMin, bounds.yMin, full.x, full.y);
                AddCorner(a, bounds.xMin, bounds.yMax, full.x, full.w);
                AddCorner(a, bounds.xMax, bounds.yMax, full.z, full.w);
                AddCorner(a, bounds.xMax, bounds.yMin, full.z, full.y);
                Clip(a.position, b.position, drawing, orientation);
                Clip(b.position, c.position, drawing, orientation);
                Clip(c.position, a.position, drawing, orientation);
                var first = target.currentVertCount;
                foreach (var vertex in s_polygon)
                {
                    target.AddVert(vertex);
                }
                for (var index = 1; index + 1 < s_polygon.Count; index++)
                {
                    target.AddTriangle(first, first + index, first + index + 1);
                }
            }
        }

        /// <summary>Adds a full-slice corner while retaining the native vertex's color and channels.</summary>
        /// <param name="template">The native vertex attributes.</param>
        /// <param name="x">The local X coordinate.</param>
        /// <param name="y">The local Y coordinate.</param>
        /// <param name="u">The normalized horizontal texture coordinate.</param>
        /// <param name="v">The normalized vertical texture coordinate.</param>
        private static void AddCorner(UIVertex template, float x, float y, float u, float v)
        {
            template.position.x = x;
            template.position.y = y;
            template.uv0.x = u;
            template.uv0.y = v;
            s_polygon.Add(template);
        }

        /// <summary>Clips against a fill/triangulation edge, excluding obsolete slice-boundary clipping.</summary>
        /// <param name="a">The edge start.</param>
        /// <param name="b">The edge end.</param>
        /// <param name="drawing">The old outer drawing bounds whose edges may expand.</param>
        /// <param name="orientation">The native triangle's signed winding.</param>
        private static void Clip(Vector3 a, Vector3 b, Rect drawing, float orientation)
        {
            if (s_polygon.Count == 0 || IsBoundary(a.x, b.x, drawing.xMin, drawing.xMax) ||
                IsBoundary(a.y, b.y, drawing.yMin, drawing.yMax))
            {
                return;
            }
            s_clipped.Clear();
            var sign = Mathf.Sign(orientation);
            var previous = s_polygon[s_polygon.Count - 1];
            var previousDistance = sign * Cross(b - a, previous.position - a);
            foreach (var current in s_polygon)
            {
                var distance = sign * Cross(b - a, current.position - a);
                if ((distance >= 0f) != (previousDistance >= 0f))
                {
                    var t = previousDistance / (previousDistance - distance);
                    var intersection = previous;
                    intersection.position = Vector3.LerpUnclamped(previous.position, current.position, t);
                    intersection.uv0 = Vector4.LerpUnclamped(previous.uv0, current.uv0, t);
                    s_clipped.Add(intersection);
                }
                if (distance >= 0f)
                {
                    s_clipped.Add(current);
                }
                previous = current;
                previousDistance = distance;
            }
            s_polygon.Clear();
            s_polygon.AddRange(s_clipped);
        }

        /// <summary>Tests whether both edge endpoints lie on the same original slice boundary.</summary>
        /// <param name="a">The first coordinate.</param>
        /// <param name="b">The second coordinate.</param>
        /// <param name="minimum">The lower boundary.</param>
        /// <param name="maximum">The upper boundary.</param>
        /// <returns>Whether this is an expandable outer edge.</returns>
        private static bool IsBoundary(float a, float b, float minimum, float maximum)
        {
            return (Mathf.Abs(a - minimum) < GeometryEpsilon && Mathf.Abs(b - minimum) < GeometryEpsilon) ||
                (Mathf.Abs(a - maximum) < GeometryEpsilon && Mathf.Abs(b - maximum) < GeometryEpsilon);
        }

        /// <summary>Computes the signed two-dimensional cross product.</summary>
        /// <param name="a">The first vector.</param>
        /// <param name="b">The second vector.</param>
        /// <returns>The signed area scale.</returns>
        private static float Cross(Vector3 a, Vector3 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        /// <summary>Replaces only original outer UV endpoints with expanded endpoints.</summary>
        /// <param name="value">The native coordinate.</param>
        /// <param name="minimum">The old minimum.</param>
        /// <param name="maximum">The old maximum.</param>
        /// <param name="expandedMinimum">The new minimum.</param>
        /// <param name="expandedMaximum">The new maximum.</param>
        /// <returns>The unchanged interior coordinate or expanded outer coordinate.</returns>
        private static float ExtendCoordinate(float value, float minimum, float maximum, float expandedMinimum, float expandedMaximum)
        {
            if (Mathf.Abs(value - minimum) < Epsilon)
            {
                return expandedMinimum;
            }
            if (Mathf.Abs(value - maximum) < Epsilon)
            {
                return expandedMaximum;
            }
            return value;
        }

        /// <summary>Affinely maps coordinates, retaining the destination minimum for degenerate axes.</summary>
        /// <param name="value">The input coordinate.</param>
        /// <param name="minimum">The source minimum.</param>
        /// <param name="maximum">The source maximum.</param>
        /// <param name="resultMinimum">The destination minimum.</param>
        /// <param name="resultMaximum">The destination maximum.</param>
        /// <returns>The mapped coordinate.</returns>
        private static float Remap(float value, float minimum, float maximum, float resultMinimum, float resultMaximum)
        {
            return Mathf.Abs(maximum - minimum) > Epsilon
                ? resultMinimum + (value - minimum) * (resultMaximum - resultMinimum) / (maximum - minimum)
                : resultMinimum;
        }
    }
}
