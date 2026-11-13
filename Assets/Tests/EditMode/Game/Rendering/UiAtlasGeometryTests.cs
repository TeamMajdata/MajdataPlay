using System;
using System.Collections.Generic;
using System.Reflection;
using MajdataPlay.Rendering;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;
#nullable enable

namespace MajdataPlay.Tests.Game.Rendering
{
    /// <summary>Compares compensated atlas meshes with Unity's unchanged native Image implementation.</summary>
    public sealed class UiAtlasGeometryTests
    {
        /// <summary>Calls the original generator only on an ordinary Unity Image used as the baseline.</summary>
        private static readonly MethodInfo s_populate = typeof(Image).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(VertexHelper) }, null)!;

        /// <summary>Tracks only temporary scene and mesh objects created by this fixture.</summary>
        private readonly List<UnityEngine.Object> _objects = new();

        /// <summary>Maps actual expanded authored sprites to their logical names.</summary>
        private readonly Dictionary<string, Sprite> _expanded = new(StringComparer.Ordinal);

        /// <summary>Retains the actual serialized legacy sprite bank.</summary>
        private UiAtlasLayout _layout = null!;

        /// <summary>Provides the same reference pixel density to the native and compensated Images.</summary>
        private Canvas _canvas = null!;

        /// <summary>Loads real production assets and creates a common world-space Canvas.</summary>
        [SetUp]
        public void SetUp()
        {
            _layout = Resources.Load<UiAtlasLayout>("Rendering/UI/AtlasLayout");
            Assert.That(_layout, Is.Not.Null);
            Assert.That(_layout.LegacySprites.Count, Is.EqualTo(34));
            Assert.That(_layout.OriginalOuterUVs.Count, Is.EqualTo(34));
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/Ribbon/newui@3x.png"))
            {
                if (asset is Sprite sprite)
                {
                    _expanded.Add(sprite.name, sprite);
                }
            }
            var root = Track(new GameObject("Atlas geometry validation", typeof(RectTransform), typeof(Canvas)));
            _canvas = root.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.referencePixelsPerUnit = 125f;
        }

        /// <summary>Destroys temporary objects and the production generator's hidden native helper.</summary>
        [TearDown]
        public void TearDown()
        {
            typeof(UiAtlasMeshGenerator).GetMethod("ResetCache", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
            for (var index = _objects.Count - 1; index >= 0; index--)
            {
                if (_objects[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_objects[index]);
                }
            }
            _objects.Clear();
            _expanded.Clear();
        }

        /// <summary>Checks offline baking covers every Sprite Swap state without changing a target's effective Sprite or source mapping.</summary>
        [Test]
        public void GlassBaker_CollectsSpriteSwapStatesAndRestoresTheTarget()
        {
            var buttonObject = Track(new GameObject("Sprite Swap contour owner", typeof(RectTransform)));
            buttonObject.SetActive(false);
            buttonObject.transform.SetParent(_canvas.transform, false);
            var button = buttonObject.AddComponent<Button>();
            var normal = _expanded["newui@3x_3"];
            var originalOverride = _expanded["newui@3x_8"];
            var image = CreateImage<QualityUiImage>(normal, Image.Type.Sliced, false, 440f, 88f);
            image.transform.SetParent(buttonObject.transform, false);
            image.gameObject.AddComponent<UiBackdropPanel>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            var states = new SpriteState
            {
                highlightedSprite = _expanded["newui@3x_4"],
                pressedSprite = _expanded["newui@3x_5"],
                selectedSprite = _expanded["newui@3x_6"],
                disabledSprite = _expanded["newui@3x_7"]
            };
            button.spriteState = states;
            image.overrideSprite = originalOverride;
            var rect = image.rectTransform.rect;
            var collector = typeof(MajdataPlay.Editor.Rendering.UiGlassOpticsAtlasBaker)
                .GetMethod("Collect", BindingFlags.Static | BindingFlags.NonPublic)!;
            var contours = (System.Collections.IList)Activator.CreateInstance(collector.GetParameters()[1].ParameterType)!;

            collector.Invoke(null, new object[] { _canvas.gameObject, contours });

            Assert.That(contours.Count, Is.EqualTo(6), "Keep the effective override, normal and all four non-null Sprite Swap states.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in contours)
            {
                var type = source.GetType();
                var contour = (UiGlassOpticsAtlas.Contour)type.GetProperty("Contour", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(source)!;
                var sprite = (Sprite)type.GetProperty("Sprite", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
                var outerUv = (Vector4)type.GetProperty("OuterUv", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
                var index = IndexOf(contour.SpriteName);
                Assert.That(sprite, Is.SameAs(_layout.LegacySprites[index]));
                Assert.That(outerUv, Is.EqualTo(_layout.OriginalOuterUVs[index]));
                Assert.That(names.Add(contour.SpriteName), Is.True);
            }
            Assert.That(names, Is.EquivalentTo(new[] { "newui@3x_3", "newui@3x_4", "newui@3x_5",
                "newui@3x_6", "newui@3x_7", "newui@3x_8" }));
            Assert.That(image.sprite, Is.SameAs(normal));
            Assert.That(image.overrideSprite, Is.SameAs(originalOverride));
            Assert.That(image.rectTransform.rect, Is.EqualTo(rect));
            Assert.That(button.spriteState, Is.EqualTo(states));

            states.pressedSprite = states.highlightedSprite;
            states.disabledSprite = null!;
            button.spriteState = states;
            image.overrideSprite = originalOverride;
            collector.Invoke(null, new object[] { _canvas.gameObject, contours });
            Assert.That(contours.Count, Is.EqualTo(6), "Duplicate and absent state sprites must not append descriptors.");
            Assert.That(image.overrideSprite, Is.SameAs(originalOverride));

            button.transition = Selectable.Transition.ColorTint;
            states.selectedSprite = _expanded["newui@3x_9"];
            button.spriteState = states;
            image.overrideSprite = originalOverride;
            collector.Invoke(null, new object[] { _canvas.gameObject, contours });
            Assert.That(contours.Count, Is.EqualTo(6), "Inactive Sprite Swap settings must not bake unrelated states.");
            Assert.That(image.overrideSprite, Is.SameAs(originalOverride));
        }

        /// <summary>Checks every original vertex, UV, channel and triangle index without outlined margins.</summary>
        /// <param name="type">The Unity Image geometry type.</param>
        /// <param name="preserveAspect">Whether native aspect preservation is enabled.</param>
        /// <param name="width">The logical control width.</param>
        /// <param name="height">The logical control height.</param>
        [TestCase(Image.Type.Simple, false, 447f, 319f)]
        [TestCase(Image.Type.Simple, true, 447f, 319f)]
        [TestCase(Image.Type.Sliced, false, 447f, 319f)]
        [TestCase(Image.Type.Sliced, false, 17f, 11f)]
        public void Unoutlined_AllSpritesMatchNativeMesh(Image.Type type, bool preserveAspect, float width, float height)
        {
            for (var index = 0; index < _layout.LegacySprites.Count; index++)
            {
                var legacy = _layout.LegacySprites[index];
                var native = CreateImage<Image>(legacy, type, preserveAspect, width, height);
                var actual = CreateImage<QualityUiImage>(_expanded[legacy.name], type, preserveAspect, width, height);
                using var expected = new VertexHelper();
                using var result = new VertexHelper();
                s_populate.Invoke(native, new object[] { expected });
                UiAtlasMeshGenerator.Populate(actual, legacy, _layout.OriginalOuterUVs[index], false, result);
                AssertIdentical(expected, result, legacy.name + " / " + type);
            }
        }

        /// <summary>Checks outline expansion preserves every native triangle's texel positions and reaches the full slice.</summary>
        /// <param name="type">The Unity Image geometry type.</param>
        /// <param name="preserveAspect">Whether the original sprite aspect ratio is retained.</param>
        /// <param name="width">The control width, including narrow nine-slice border compression cases.</param>
        /// <param name="height">The control height.</param>
        [TestCase(Image.Type.Simple, false, 447f, 319f)]
        [TestCase(Image.Type.Simple, true, 447f, 319f)]
        [TestCase(Image.Type.Sliced, false, 447f, 319f)]
        [TestCase(Image.Type.Sliced, false, 17f, 11f)]
        public void Outlined_AllSpritesPreserveNativeTexelPositions(Image.Type type, bool preserveAspect, float width, float height)
        {
            for (var index = 0; index < _layout.LegacySprites.Count; index++)
            {
                var legacy = _layout.LegacySprites[index];
                var native = CreateImage<Image>(legacy, type, preserveAspect, width, height);
                var actual = CreateImage<QualityUiImage>(_expanded[legacy.name], type, preserveAspect, width, height);
                using var expected = new VertexHelper();
                using var result = new VertexHelper();
                s_populate.Invoke(native, new object[] { expected });
                var rectBefore = actual.rectTransform.rect;
                var pivotBefore = actual.rectTransform.pivot;
                UiAtlasMeshGenerator.Populate(actual, legacy, _layout.OriginalOuterUVs[index], true, result);
                if (expected.currentVertCount == 0)
                {
                    Assert.That(result.currentVertCount, Is.Zero, legacy.name + ": native border/padding compression omits all quads.");
                    continue;
                }
                AssertOriginalTriangleSamples(expected, result, legacy.name + " / " + type);
                AssertSamplingBounds(expected, result, legacy, actual.sprite, _layout.OriginalOuterUVs[index], legacy.name + " / " + type);
                Assert.That(actual.rectTransform.rect, Is.EqualTo(rectBefore));
                Assert.That(actual.rectTransform.pivot, Is.EqualTo(pivotBefore));
                Assert.That(actual.sprite, Is.SameAs(_expanded[legacy.name]));
            }
        }

        /// <summary>Checks the real tiled menu artwork keeps its original tile period and border geometry.</summary>
        [Test]
        public void Tiled_RealMenuSpritePreservesNativeTileSamples()
        {
            var index = IndexOf("newui@3x_2");
            var legacy = _layout.LegacySprites[index];
            foreach (var width in new[] { 17f, 283.3504f, 613.7f })
            {
                var native = CreateImage<Image>(legacy, Image.Type.Tiled, false, width, 164f);
                var actual = CreateImage<QualityUiImage>(_expanded[legacy.name], Image.Type.Tiled, false, width, 164f);
                using var expected = new VertexHelper();
                using var low = new VertexHelper();
                using var high = new VertexHelper();
                s_populate.Invoke(native, new object[] { expected });
                UiAtlasMeshGenerator.Populate(actual, legacy, _layout.OriginalOuterUVs[index], false, low);
                UiAtlasMeshGenerator.Populate(actual, legacy, _layout.OriginalOuterUVs[index], true, high);
                AssertIdentical(expected, low, "Tiled width " + width);
                AssertOriginalTriangleSamples(expected, high, "Tiled width " + width);
                AssertSamplingBounds(expected, high, legacy, actual.sprite, _layout.OriginalOuterUVs[index], "Tiled width " + width);
            }
        }

        /// <summary>Checks radial progress geometry retains the native fill cut planes while margins grow.</summary>
        /// <param name="origin">The native Radial360 origin.</param>
        /// <param name="clockwise">The direction of native filling.</param>
        [TestCase(0, false)]
        [TestCase(0, true)]
        [TestCase(1, false)]
        [TestCase(1, true)]
        [TestCase(2, false)]
        [TestCase(2, true)]
        [TestCase(3, false)]
        [TestCase(3, true)]
        public void Filled_RealHoldCirclePreservesCutPlanes(int origin, bool clockwise)
        {
            var index = IndexOf("newui@3x_0");
            var legacy = _layout.LegacySprites[index];
            var native = CreateImage<Image>(legacy, Image.Type.Filled, false, 195f, 195f);
            var actual = CreateImage<QualityUiImage>(_expanded[legacy.name], Image.Type.Filled, false, 195f, 195f);
            native.fillMethod = actual.fillMethod = Image.FillMethod.Radial360;
            native.fillOrigin = actual.fillOrigin = origin;
            native.fillClockwise = actual.fillClockwise = clockwise;
            using var full = new VertexHelper();
            native.fillAmount = 1f;
            s_populate.Invoke(native, new object[] { full });
            var fullVertices = Vertices(full);
            var bounds = Bounds(fullVertices);
            foreach (var amount in new[] { 0f, 0.125f, 0.25f, 0.4f, 0.5f, 0.75f, 0.9f, 1f })
            {
                native.fillAmount = actual.fillAmount = amount;
                using var expected = new VertexHelper();
                using var low = new VertexHelper();
                using var high = new VertexHelper();
                s_populate.Invoke(native, new object[] { expected });
                UiAtlasMeshGenerator.Populate(actual, legacy, _layout.OriginalOuterUVs[index], false, low);
                UiAtlasMeshGenerator.Populate(actual, legacy, _layout.OriginalOuterUVs[index], true, high);
                var context = $"Radial360 origin {origin}, clockwise {clockwise}, fill {amount}";
                AssertIdentical(expected, low, context);
                AssertOriginalTriangleSamples(expected, high, context);
                AssertCoverageInsideOriginalBounds(expected, high, bounds, context);
                if (amount == 0f)
                {
                    Assert.That(high.currentVertCount, Is.Zero, context);
                }
                if (amount == 1f)
                {
                    AssertSamplingBounds(expected, high, legacy, actual.sprite, _layout.OriginalOuterUVs[index], context);
                }
            }
        }

        /// <summary>Checks a fully compressed border does not reveal previously cropped body artwork outside the control.</summary>
        [Test]
        public void CompressedSlicedInteriorCrop_DoesNotBecomeExtraBodyArtwork()
        {
            var index = IndexOf("newui@3x_81");
            var legacy = _layout.LegacySprites[index];
            var native = CreateImage<Image>(legacy, Image.Type.Sliced, false, 447f, 319f);
            var actual = CreateImage<QualityUiImage>(_expanded[legacy.name], Image.Type.Sliced, false, 447f, 319f);
            using var expected = new VertexHelper();
            using var result = new VertexHelper();
            s_populate.Invoke(native, new object[] { expected });
            UiAtlasMeshGenerator.Populate(actual, legacy, _layout.OriginalOuterUVs[index], true, result);

            var originalSampling = SamplingBounds(expected);
            var expandedSampling = SamplingBounds(result);
            var logicalBottom = legacy.rect.yMin / _layout.SourceTextureSize.y;
            Assert.That(originalSampling.y, Is.GreaterThan(logicalBottom + 0.01f), "This case must actually crop the old sprite's interior.");
            Assert.That(expandedSampling.y, Is.EqualTo(originalSampling.y).Within(0.000002f), "Preserve the native interior texture cut.");
            Assert.That(Bounds(Vertices(result)).yMin, Is.EqualTo(Bounds(Vertices(expected)).yMin).Within(0.0003f), "Do not restore formerly invisible body content below the widget.");
            AssertOriginalTriangleSamples(expected, result, "Compressed native body crop");
        }

        /// <summary>Checks logical layout and native sizing do not include added shadow margins.</summary>
        /// <param name="type">The native layout policy being compared.</param>
        [TestCase(Image.Type.Simple)]
        [TestCase(Image.Type.Sliced)]
        [TestCase(Image.Type.Tiled)]
        [TestCase(Image.Type.Filled)]
        public void LayoutAndNativeSize_AllSpritesMatchLegacy(Image.Type type)
        {
            foreach (var legacy in _layout.LegacySprites)
            {
                var native = CreateImage<Image>(legacy, type, false, 447f, 319f);
                var actual = CreateImage<QualityUiImage>(_expanded[legacy.name], type, false, 447f, 319f);
                Assert.That(actual.preferredWidth, Is.EqualTo(native.preferredWidth).Within(0.0001f), legacy.name + " width");
                Assert.That(actual.preferredHeight, Is.EqualTo(native.preferredHeight).Within(0.0001f), legacy.name + " height");
                native.SetNativeSize();
                actual.SetNativeSize();
                Assert.That(actual.rectTransform.sizeDelta.x, Is.EqualTo(native.rectTransform.sizeDelta.x).Within(0.0001f), legacy.name);
                Assert.That(actual.rectTransform.sizeDelta.y, Is.EqualTo(native.rectTransform.sizeDelta.y).Within(0.0001f), legacy.name);
                Assert.That(actual.rectTransform.anchorMin, Is.EqualTo(native.rectTransform.anchorMin), legacy.name);
                Assert.That(actual.rectTransform.anchorMax, Is.EqualTo(native.rectTransform.anchorMax), legacy.name);
            }
        }

        /// <summary>Creates a native or compensated Image with deliberately nondefault pivot, tint and pixel multiplier.</summary>
        /// <typeparam name="T">The Image implementation under test.</typeparam>
        /// <param name="sprite">The actual imported sprite to assign.</param>
        /// <param name="type">The native geometry type.</param>
        /// <param name="preserveAspect">Whether to preserve the sprite aspect ratio.</param>
        /// <param name="width">The control width.</param>
        /// <param name="height">The control height.</param>
        /// <returns>The configured active Image.</returns>
        private T CreateImage<T>(Sprite sprite, Image.Type type, bool preserveAspect, float width, float height) where T : Image
        {
            var gameObject = new GameObject("Native geometry comparison", typeof(RectTransform), typeof(CanvasRenderer));
            gameObject.transform.SetParent(_canvas.transform, false);
            var image = gameObject.AddComponent<T>();
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.3f, 0.6f);
            image.rectTransform.pivot = new Vector2(0.23f, 0.67f);
            image.rectTransform.sizeDelta = new Vector2(width, height);
            image.rectTransform.anchoredPosition = new Vector2(17f, -23f);
            image.sprite = sprite;
            image.type = type;
            image.preserveAspect = preserveAspect;
            image.pixelsPerUnitMultiplier = 3f;
            image.color = new Color(0.31f, 0.71f, 0.91f, 0.44f);
            return image;
        }

        /// <summary>Compares native mesh storage without reducing the comparison to only outer bounds.</summary>
        /// <param name="expected">The ordinary Image mesh.</param>
        /// <param name="actual">The compensated mesh.</param>
        /// <param name="context">The sprite and geometry configuration.</param>
        private void AssertIdentical(VertexHelper expected, VertexHelper actual, string context)
        {
            Assert.That(actual.currentVertCount, Is.EqualTo(expected.currentVertCount), context);
            for (var index = 0; index < expected.currentVertCount; index++)
            {
                var first = default(UIVertex);
                var second = default(UIVertex);
                expected.PopulateUIVertex(ref first, index);
                actual.PopulateUIVertex(ref second, index);
                Assert.That((first.position - second.position).sqrMagnitude, Is.LessThan(0.0000001f), context + " position " + index);
                Assert.That((first.uv0 - second.uv0).sqrMagnitude, Is.LessThan(0.0000000001f), context + " UV " + index);
                Assert.That(second.color, Is.EqualTo(first.color), context + " color " + index);
                Assert.That(second.normal, Is.EqualTo(first.normal), context + " normal " + index);
                Assert.That(second.tangent, Is.EqualTo(first.tangent), context + " tangent " + index);
            }
            var firstMesh = Track(new Mesh());
            var secondMesh = Track(new Mesh());
            expected.FillMesh(firstMesh);
            actual.FillMesh(secondMesh);
            Assert.That(secondMesh.triangles, Is.EqualTo(firstMesh.triangles), context + " triangle indices");
        }

        /// <summary>Checks each original triangle interior retains the same UV at the same local position.</summary>
        /// <param name="expected">The ordinary native mesh.</param>
        /// <param name="actual">The expanded compensated mesh.</param>
        /// <param name="context">The sprite and geometry configuration.</param>
        private static void AssertOriginalTriangleSamples(VertexHelper expected, VertexHelper actual, string context)
        {
            var original = new List<UIVertex>();
            var expanded = new List<UIVertex>();
            expected.GetUIVertexStream(original);
            actual.GetUIVertexStream(expanded);
            for (var index = 0; index + 2 < original.Count; index += 3)
            {
                var a = original[index];
                var b = original[index + 1];
                var c = original[index + 2];
                // Native border compression can leave effectively zero-width quads from float roundoff.
                // Their exact storage is already checked in the unoutlined comparison; barycentric
                // texel interpolation below is only meaningful for nondegenerate visible triangles.
                if (Mathf.Abs(Cross(b.position - a.position, c.position - a.position)) < 0.0001f)
                {
                    continue;
                }
                var position = (a.position + b.position + c.position) / 3f;
                var expectedUV = (a.uv0 + b.uv0 + c.uv0) / 3f;
                var matched = ContainsMatchingSample(expanded, position, expectedUV);
                if (!matched)
                {
                    Debug.Log($"GEOMETRY_SAMPLE_FAILURE {context} triangle {index / 3} area {Cross(b.position - a.position, c.position - a.position):R} a {a.position.ToString("F8")} uv {a.uv0.ToString("F8")} b {b.position.ToString("F8")} uv {b.uv0.ToString("F8")} c {c.position.ToString("F8")} uv {c.uv0.ToString("F8")}");
                }
                Assert.That(matched, Is.True, context + " original triangle " + index / 3);
            }
        }

        /// <summary>Checks expanded filled geometry does not move a fill boundary inside the old drawing area.</summary>
        /// <param name="expected">The original native filled mesh.</param>
        /// <param name="actual">The expanded filled mesh.</param>
        /// <param name="bounds">The full native drawing rectangle before filling.</param>
        /// <param name="context">The radial fill configuration.</param>
        private static void AssertCoverageInsideOriginalBounds(VertexHelper expected, VertexHelper actual, Rect bounds, string context)
        {
            var original = new List<UIVertex>();
            var expanded = new List<UIVertex>();
            expected.GetUIVertexStream(original);
            actual.GetUIVertexStream(expanded);
            for (var row = 0; row < 13; row++)
            {
                for (var column = 0; column < 13; column++)
                {
                    var point = new Vector3(bounds.xMin + bounds.width * ((column + 0.37f) / 13f), bounds.yMin + bounds.height * ((row + 0.61f) / 13f), 0f);
                    Assert.That(Contains(expanded, point), Is.EqualTo(Contains(original, point)), context + " local sample " + point);
                }
            }
        }

        /// <summary>Checks exposed slice edges grow to the shadow bounds while intentional native interior cuts remain fixed.</summary>
        /// <param name="native">The native baseline mesh before margin expansion.</param>
        /// <param name="expanded">The mesh with expanded artwork sampling margins.</param>
        /// <param name="legacy">The original logical sprite and its authored rectangle.</param>
        /// <param name="sprite">The expanded full-rectangle sprite.</param>
        /// <param name="originalOuter">The old sampling bounds, which may include imported extrusion outside the logical rect.</param>
        /// <param name="context">The sprite and geometry configuration.</param>
        private void AssertSamplingBounds(VertexHelper native, VertexHelper expanded, Sprite legacy, Sprite sprite, Vector4 originalOuter, string context)
        {
            var source = SamplingBounds(native);
            var actual = SamplingBounds(expanded);
            var artwork = DataUtility.GetOuterUV(sprite);
            var logical = new Vector4(legacy.rect.xMin / _layout.SourceTextureSize.x, legacy.rect.yMin / _layout.SourceTextureSize.y,
                legacy.rect.xMax / _layout.SourceTextureSize.x, legacy.rect.yMax / _layout.SourceTextureSize.y);
            const float tolerance = 0.000002f;
            for (var axis = 0; axis < 4; axis++)
            {
                var minimum = axis < 2;
                var atCapturedEdge = Mathf.Abs(source[axis] - originalOuter[axis]) <= tolerance;
                var atAuthoredEdge = minimum ? source[axis] <= logical[axis] + tolerance : source[axis] >= logical[axis] - tolerance;
                var expected = source[axis];
                if (atCapturedEdge || atAuthoredEdge)
                {
                    expected = minimum ? Mathf.Min(source[axis], originalOuter[axis], artwork[axis])
                        : Mathf.Max(source[axis], originalOuter[axis], artwork[axis]);
                }
                Assert.That(actual[axis], Is.EqualTo(expected).Within(tolerance), context + " sampling edge " + axis);
            }
        }

        /// <summary>Measures the native or compensated mesh's actual UV bounds.</summary>
        /// <param name="mesh">The mesh whose sampling bounds are inspected.</param>
        /// <returns>The minimum U/V and maximum U/V coordinates.</returns>
        private static Vector4 SamplingBounds(VertexHelper mesh)
        {
            var minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (var index = 0; index < mesh.currentVertCount; index++)
            {
                var vertex = default(UIVertex);
                mesh.PopulateUIVertex(ref vertex, index);
                minimum = Vector2.Min(minimum, vertex.uv0);
                maximum = Vector2.Max(maximum, vertex.uv0);
            }
            return new Vector4(minimum.x, minimum.y, maximum.x, maximum.y);
        }

        /// <summary>Finds a generated triangle with the expected interpolated UV at the original local point.</summary>
        /// <param name="triangles">The expanded triangle stream.</param>
        /// <param name="position">The original local position.</param>
        /// <param name="expectedUV">The original interpolated UV.</param>
        /// <returns>Whether the same content sample remains available.</returns>
        private static bool ContainsMatchingSample(List<UIVertex> triangles, Vector3 position, Vector4 expectedUV)
        {
            for (var index = 0; index + 2 < triangles.Count; index += 3)
            {
                if (TryWeights(triangles[index].position, triangles[index + 1].position, triangles[index + 2].position, position, out var weights))
                {
                    var actualUV = triangles[index].uv0 * weights.x + triangles[index + 1].uv0 * weights.y + triangles[index + 2].uv0 * weights.z;
                    if ((actualUV - expectedUV).sqrMagnitude < 0.0000000001f)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Checks whether any nondegenerate triangle covers a local point.</summary>
        /// <param name="triangles">The triangle stream.</param>
        /// <param name="position">The local sample point.</param>
        /// <returns>Whether the point is covered.</returns>
        private static bool Contains(List<UIVertex> triangles, Vector3 position)
        {
            for (var index = 0; index + 2 < triangles.Count; index += 3)
            {
                if (TryWeights(triangles[index].position, triangles[index + 1].position, triangles[index + 2].position, position, out _))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Computes barycentric weights using native local coordinates.</summary>
        /// <param name="a">The first triangle corner.</param>
        /// <param name="b">The second triangle corner.</param>
        /// <param name="c">The third triangle corner.</param>
        /// <param name="point">The point to classify.</param>
        /// <param name="weights">The barycentric coordinates when covered.</param>
        /// <returns>Whether the triangle covers the point.</returns>
        private static bool TryWeights(Vector3 a, Vector3 b, Vector3 c, Vector3 point, out Vector3 weights)
        {
            weights = default;
            var determinant = Cross(b - a, c - a);
            if (Mathf.Abs(determinant) < 0.00001f)
            {
                return false;
            }
            var second = Cross(point - a, c - a) / determinant;
            var third = Cross(b - a, point - a) / determinant;
            weights = new Vector3(1f - second - third, second, third);
            return weights.x >= -0.00002f && weights.y >= -0.00002f && weights.z >= -0.00002f;
        }

        /// <summary>Computes a signed two-dimensional cross product.</summary>
        /// <param name="a">The first direction.</param>
        /// <param name="b">The second direction.</param>
        /// <returns>The signed area scale.</returns>
        private static float Cross(Vector3 a, Vector3 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        /// <summary>Copies mesh vertices for reference-bound calculations.</summary>
        /// <param name="mesh">The native full mesh.</param>
        /// <returns>The native triangle vertex stream.</returns>
        private static List<UIVertex> Vertices(VertexHelper mesh)
        {
            var vertices = new List<UIVertex>();
            mesh.GetUIVertexStream(vertices);
            return vertices;
        }

        /// <summary>Measures the full native local drawing rectangle.</summary>
        /// <param name="vertices">The full native mesh vertices.</param>
        /// <returns>The axis-aligned native drawing bounds.</returns>
        private static Rect Bounds(List<UIVertex> vertices)
        {
            var minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var vertex in vertices)
            {
                minimum = Vector2.Min(minimum, vertex.position);
                maximum = Vector2.Max(maximum, vertex.position);
            }
            return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        }

        /// <summary>Finds a named legacy sprite and its corresponding normalized UV record.</summary>
        /// <param name="name">The authored logical sprite name.</param>
        /// <returns>The matching legacy-array index.</returns>
        private int IndexOf(string name)
        {
            for (var index = 0; index < _layout.LegacySprites.Count; index++)
            {
                if (_layout.LegacySprites[index].name == name)
                {
                    return index;
                }
            }
            throw new InvalidOperationException("Missing legacy sprite: " + name);
        }

        /// <summary>Tracks a temporary Unity object for deterministic cleanup.</summary>
        /// <typeparam name="T">The Unity object type.</typeparam>
        /// <param name="value">The temporary object.</param>
        /// <returns>The same tracked object.</returns>
        private T Track<T>(T value) where T : UnityEngine.Object
        {
            _objects.Add(value);
            return value;
        }
    }
}
