using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using MajdataPlay.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

#nullable enable
namespace MajdataPlay.Tests.Game.Rendering
{
    /// <summary>Checks actual baked resources, native procedural glass, shader equivalence and material bindings.</summary>
    public sealed class UiGlassOpticsAtlasTests
    {
        /// <summary>Owns fixture objects while keeping imported assets borrowed.</summary>
        private TestObjects _objects = null!;

        /// <summary>Restores the previous native readback target.</summary>
        private RenderTexture? _previousTarget;

        /// <summary>Starts a clean domain-reload-independent glass service.</summary>
        [SetUp]
        public void SetUp()
        {
            ResetService();
            typeof(UiOutlineTheme).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
            _objects = new TestObjects();
            _previousTarget = RenderTexture.active;
        }

        /// <summary>Releases only fixture-owned objects and runtime materials.</summary>
        [TearDown]
        public void TearDown()
        {
            RenderTexture.active = _previousTarget;
            _objects.Dispose();
            ResetService();
            typeof(UiOutlineTheme).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
        }

        /// <summary>Checks encoded RGBA8 contours, the half-float profile and safe packed source-coordinate mappings.</summary>
        [Test]
        [Category("GPU")]
        public void Resources_ProfileAndMappingsUseEncodedDistanceAndHalfResponse()
        {
            var atlas = LoadAtlas();
            var distance = atlas.DistanceTexture!;
            var profile = atlas.ProfileTexture!;
            Assert.That(distance.format, Is.EqualTo(TextureFormat.RGBA32), "Encoded distances must use filterable RGBA8 UNORM storage.");
            Assert.That(distance.filterMode, Is.EqualTo(FilterMode.Bilinear));
            Assert.That(UiGlassOpticsAtlas.CurrentEncodingVersion, Is.EqualTo(7));
            Assert.That(profile.format, Is.EqualTo(TextureFormat.RGHalf));
            Assert.That(profile.width, Is.EqualTo(256));
            Assert.That(profile.height, Is.EqualTo(1));
            foreach (var texture in new[] { distance, profile })
            {
                Assert.That(texture.mipmapCount, Is.EqualTo(1));
                Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Bilinear));
                Assert.That(texture.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
                Assert.That(GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat), Is.False);
                Assert.That(texture.width, Is.InRange(1, SystemInfo.maxTextureSize));
                Assert.That(texture.height, Is.InRange(1, SystemInfo.maxTextureSize));
                TestContext.WriteLine($"OPTICS_TEXTURE name={texture.name} size={texture.width}x{texture.height} format={texture.format} gpu={texture.graphicsFormat}");
            }
            var values = ReadFloatTexture(profile);
            var previousSource = -1f;
            var maximumError = 0f;
            for (var index = 0; index < profile.width; index++)
            {
                var t = index / (double)(profile.width - 1);
                var edge = 1.0 - t;
                var slope = 3.6 * edge * edge;
                var normalY = 1.0 / Math.Sqrt(1.0 + slope * slope);
                var normalX = slope * normalY;
                const double eta = 1.0 / 1.45;
                var transmittedCosine = Math.Sqrt(1.0 - eta * eta * normalX * normalX);
                var rayTerm = transmittedCosine - eta * normalY;
                var height = 1.2 * (1.0 - edge * edge * edge);
                var displacement = rayTerm * normalX / (eta + rayTerm * normalY) * height * 1.15;
                maximumError = Mathf.Max(maximumError, Mathf.Abs(values[index].r - (float)displacement));
                maximumError = Mathf.Max(maximumError, Mathf.Abs(values[index].g - (float)(1.0 - normalY)));
                Assert.That(values[index].r, Is.GreaterThanOrEqualTo(0f));
                var source = (float)t + values[index].r;
                Assert.That(source, Is.GreaterThanOrEqualTo(previousSource - 0.00001f), "Half-float quantization must not fold the source mapping.");
                Assert.That(source, Is.InRange(-0.00001f, 1.00001f));
                previousSource = source;
            }
            Assert.That(maximumError, Is.LessThan(0.001f), "The stored profile must preserve the fixed Snell/cubic-cap response within half precision.");
            Assert.That(values[0].r, Is.Zero);
            Assert.That(values[values.Length - 1].r, Is.Zero);
            Assert.That(values[values.Length - 1].g, Is.Zero);
            var entries = (UiGlassOpticsAtlas.Entry[])typeof(UiGlassOpticsAtlas).GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(atlas)!;
            Assert.That(entries.Length, Is.GreaterThanOrEqualTo(4), "The actual menu consumers must not produce an empty or trivial atlas.");
            var hasOption = false;
            var hasUserInfo = false;
            foreach (var entry in entries)
            {
                var contour = entry.Contour;
                hasOption |= contour.SpriteName == "newui@3x_16";
                hasUserInfo |= contour.SpriteName == "newui@3x_26";
                var sourceUvToLocal = new Vector4(333f, 777f, -12f, 34f);
                Assert.That(atlas.TryResolve(in contour, sourceUvToLocal, out var mapping, out var clamp, out var decode), Is.True);
                Assert.That(decode, Is.EqualTo(entry.Decode));
                Assert.That(decode.x, Is.GreaterThan(0f));
                Assert.That(float.IsNaN(decode.y) || float.IsInfinity(decode.y), Is.False);
                Assert.That(clamp.x, Is.GreaterThan(0f).And.LessThan(clamp.z));
                Assert.That(clamp.y, Is.GreaterThan(0f).And.LessThan(clamp.w));
                Assert.That(clamp.z, Is.LessThan(1f));
                Assert.That(clamp.w, Is.LessThan(1f));
                var sourceWidth = Mathf.Max(1, Mathf.CeilToInt(contour.Size.x));
                var sourceHeight = Mathf.Max(1, Mathf.CeilToInt(contour.Size.y));
                var expectedWidth = sourceWidth;
                var expectedHeight = sourceHeight;
                const int maximumRasterDimension = 1020;
                if (Mathf.Max(sourceWidth, sourceHeight) > maximumRasterDimension)
                {
                    if (sourceWidth >= sourceHeight)
                    {
                        expectedWidth = maximumRasterDimension;
                        expectedHeight = Math.Max(1, (int)Math.Ceiling(sourceHeight * (double)maximumRasterDimension / sourceWidth));
                    }
                    else
                    {
                        expectedWidth = Math.Max(1, (int)Math.Ceiling(sourceWidth * (double)maximumRasterDimension / sourceHeight));
                        expectedHeight = maximumRasterDimension;
                    }
                }
                var rasterWidth = entry.Mapping.x * contour.Size.x * distance.width;
                var rasterHeight = entry.Mapping.y * contour.Size.y * distance.height;
                Assert.That(rasterWidth, Is.EqualTo(expectedWidth).Within(0.001f), "The encoded grid must preserve the source aspect ratio within upward rounding and its long-edge cap.");
                Assert.That(rasterHeight, Is.EqualTo(expectedHeight).Within(0.001f));
                Assert.That((clamp.z - clamp.x) * distance.width,
                    Is.EqualTo(rasterWidth + 3f).Within(0.001f), "Two guard texels must surround the actual capped raster width.");
                Assert.That((clamp.w - clamp.y) * distance.height,
                    Is.EqualTo(rasterHeight + 3f).Within(0.001f));
                var sourceCenter = new Vector2(-sourceUvToLocal.z / sourceUvToLocal.x, -sourceUvToLocal.w / sourceUvToLocal.y);
                var atlasCenter = new Vector2(sourceCenter.x * mapping.x + mapping.z, sourceCenter.y * mapping.y + mapping.w);
                Assert.That(atlasCenter.x, Is.InRange(clamp.x, clamp.z));
                Assert.That(atlasCenter.y, Is.InRange(clamp.y, clamp.w));
                var changed = new UiGlassOpticsAtlas.Contour(contour.SpriteName, contour.Size, contour.Rect + new Vector4(0f, 0f, 1f, 0f),
                    contour.Corners, contour.Shape, contour.Circle, contour.ClipSmoothness);
                Assert.That(atlas.TryResolve(in changed, sourceUvToLocal, out _, out _, out _), Is.False, "An unknown geometry must disable refraction rather than use an unrelated tile.");
            }
            Assert.That(hasOption && hasUserInfo, Is.True);
            TestContext.WriteLine($"OPTICS_PROFILE maxError={maximumError:F7} entries={entries.Length}");
        }

        /// <summary>Checks the peripheral tail's independent continuous model preserves peak strength and ordered source depth.</summary>
        [Test]
        public void PeripheralTail_IndependentModelJoinsBevelAndCenterWithoutFoldsOrStrongerPeak()
        {
            const int samples = 100000;
            const double peakBound = 0.397008;
            foreach (var ratio in new[] { 0.025, 0.05, 0.1, 0.2, 0.4, 0.8 })
            {
                var maximum = 0.0;
                var previousSource = 0.0;
                var minimumDerivative = double.PositiveInfinity;
                for (var index = 0; index <= samples; index++)
                {
                    var depth = index / (double)samples;
                    var displacement = PeripheralTailModel(depth, ratio);
                    maximum = Math.Max(maximum, displacement / ratio);
                    if (index > 0)
                    {
                        minimumDerivative = Math.Min(minimumDerivative, (depth + displacement - previousSource) * samples);
                    }
                    previousSource = depth + displacement;
                }
                Assert.That(maximum, Is.LessThanOrEqualTo(peakBound + 0.00000001));
                Assert.That(minimumDerivative, Is.GreaterThan(0.0), "The one-dimensional source-depth mapping must remain ordered.");
                const double epsilon = 0.000001;
                var join = PeripheralTailModel(ratio, ratio);
                var leftSlope = (join - PeripheralTailModel(ratio - epsilon, ratio)) / epsilon;
                var rightSlope = (PeripheralTailModel(ratio + epsilon, ratio) - join) / epsilon;
                Assert.That(Math.Abs(leftSlope - rightSlope), Is.LessThan(0.001), "The tail must not introduce a slope step at the old bevel endpoint.");
                Assert.That(PeripheralTailModel(0.0, ratio), Is.Zero);
                Assert.That(PeripheralTailModel(1.0, ratio), Is.Zero);
                Assert.That(PeripheralTailModel(1.0 - epsilon, ratio) / epsilon, Is.LessThan(0.0001),
                    "Both displacement and its slope must vanish smoothly at the center.");
                Assert.That(PeripheralTailModel((1.0 + ratio) * 0.5, ratio), Is.GreaterThan(0.0),
                    "The old flat interior must retain a positive displacement before the center.");
                TestContext.WriteLine($"TAIL_MODEL widthRatio={ratio:F3} maxNormalized={maximum:F10} minSourceDerivative={minimumDerivative:F8} joinSlopeDelta={Math.Abs(leftSlope - rightSlope):F10}");
            }
        }

        /// <summary>Evaluates a unit-radius cap with the proposed peripheral continuation using Bernstein onset weights.</summary>
        /// <param name="depth">The nonnegative inward source depth with the circle center at one.</param>
        /// <param name="width">The normalized existing reflection-band width.</param>
        /// <returns>The independently evaluated source displacement in unit-radius coordinates.</returns>
        private static double PeripheralTailModel(double depth, double width)
        {
            var t = Math.Min(1.0, Math.Max(0.0, depth / width));
            var edge = 1.0 - t;
            var slope = 3.6 * edge * edge;
            var normalY = 1.0 / Math.Sqrt(1.0 + slope * slope);
            var normalX = slope * normalY;
            const double eta = 1.0 / 1.45;
            var transmitted = Math.Sqrt(1.0 - eta * eta * normalX * normalX);
            var ray = transmitted - eta * normalY;
            var legacy = ray * normalX / (eta + ray * normalY) * 1.2 * (1.0 - edge * edge * edge) * 1.15;
            // The Bernstein basis is independently equivalent to quintic smoothstep,
            // retaining zero first/second derivatives at each onset endpoint.
            var onset = 10.0 * t * t * t * edge * edge + 5.0 * t * t * t * t * edge + t * t * t * t * t;
            var remaining = Math.Max(0.0, 1.0 - depth);
            var headroom = Math.Max(0.0, 1.0 - legacy / 0.397008);
            return (legacy + 0.22 * onset * remaining * remaining * headroom) * width;
        }

        /// <summary>Checks hardware-filtering eligibility without an operating-system or graphics-API whitelist.</summary>
        /// <param name="sample">The RGBA8 sampling capability.</param>
        /// <param name="linear">The same format's hardware-linear capability.</param>
        /// <param name="level">The reported shader capability level.</param>
        /// <param name="expected">Whether the ordinary optical program may read its contour data.</param>
        [TestCase(true, true, 30, true)]
        [TestCase(true, true, 35, true)]
        [TestCase(true, true, 45, true)]
        [TestCase(true, true, 50, true)]
        [TestCase(false, true, 50, false)]
        [TestCase(true, false, 50, false)]
        [TestCase(false, false, 50, false)]
        [TestCase(true, true, 29, false)]
        public void FilteringEligibility_RequiresRgbaSamplingLinearFilteringAndBaselineShaderLevel(bool sample, bool linear, int level, bool expected)
        {
            Assert.That(UiGlassOpticsAtlas.SupportsFiltering(sample, linear, level), Is.EqualTo(expected));
        }
        /// <summary>Rejects incomplete or corrupt per-tile decode metadata rather than applying unrelated optical distances.</summary>
        [Test]
        public void DistanceMapping_RejectsInvalidDecodeMetadata()
        {
            var atlas = LoadAtlas();
            var entriesField = typeof(UiGlassOpticsAtlas).GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var entry = ((UiGlassOpticsAtlas.Entry[])entriesField.GetValue(atlas)!)[0];
            var contour = entry.Contour;
            var probe = _objects.Track(ScriptableObject.CreateInstance<UiGlassOpticsAtlas>());
            foreach (var decode in new[] { Vector2.zero, new Vector2(-1f, 0f),
                new Vector2(float.NaN, 0f), new Vector2(float.PositiveInfinity, 0f),
                new Vector2(1f, float.NaN), new Vector2(1f, float.NegativeInfinity) })
            {
                probe.Initialize(atlas.DistanceTexture!, atlas.ProfileTexture!,
                    new[] { new UiGlassOpticsAtlas.Entry(contour, entry.Mapping, entry.Clamp, decode) });
                Assert.That(probe.TryResolve(in contour, Vector4.one, out _, out _, out _), Is.False,
                    "Invalid generated decode metadata must disable refraction: " + decode);
            }
        }

        /// <summary>Checks all currently authored source components against the generated resource without loading incomplete scene dependencies.</summary>
        [Test]
        public void AuthoredPanels_AllCurrentSourceDescriptorsResolve()
        {
#if UNITY_EDITOR
            var atlas = LoadAtlas();
            var sprites = new System.Collections.Generic.Dictionary<string, Sprite>();
            var total = 0;
            var matched = 0;
            var procedural = 0;
            foreach (var path in new[]
            {
                "Assets/Scenes/Calibrator.unity", "Assets/Scenes/Login.unity", "Assets/Scenes/Practice.unity",
                "Assets/Scenes/SortFind.unity", "Assets/Scenes/Setting.unity", "Assets/Scenes/Title.unity",
                "Assets/Scenes/Result.unity", "Assets/Scenes/TotalResult.unity",
                "Assets/Prefabs/UI/Buttons.prefab", "Assets/Prefabs/UI/UserInfo.prefab", "Assets/Prefabs/UI/ChartVisualDisplayer.prefab",
                "Assets/Prefabs/Setting/MenuTitleDisplayer.prefab", "Assets/Prefabs/List/Model/CollectionDisplayer.prefab",
                "Assets/Prefabs/List/Main_Display_ribbon.prefab"
            })
            {
                var text = MajdataPlay.IO.Storage.FileSystem.File.Open(path).ReadAllText();
                var components = Regex.Matches(text, @"^--- !u!114 &-?\d+\r?\n(?<body>.*?)(?=^--- |\z)", RegexOptions.Multiline | RegexOptions.Singleline);
                var sceneDescriptors = 0;
                foreach (Match component in components)
                {
                    var body = component.Groups["body"].Value;
                    if (!body.Contains("guid: ef06d39c7c35439cafc6637c942934d4"))
                    {
                        continue;
                    }
                    total++;
                    sceneDescriptors++;
                    var owner = Regex.Match(body, @"m_GameObject: \{fileID: (?<id>-?\d+)\}").Groups["id"].Value;
                    var imageBody = string.Empty;
                    foreach (Match candidate in components)
                    {
                        var candidateBody = candidate.Groups["body"].Value;
                        if (candidateBody.Contains("guid: 7c51cc521e1747f0ba99ae9b65785bd5") && candidateBody.Contains("m_GameObject: {fileID: " + owner + "}"))
                        {
                            imageBody = candidateBody;
                            break;
                        }
                    }
                    Assert.That(imageBody, Is.Not.Empty, path + ": every authored panel must retain its actual quality-aware Image.");
                    var spriteReference = Regex.Match(imageBody, @"m_Sprite: \{fileID: (?<id>-?\d+)(?:, guid: (?<guid>[0-9a-f]{32}))?");
                    Assert.That(spriteReference.Success, Is.True, path + ": the authored Image must retain its serialized Sprite reference.");
                    var spriteId = long.Parse(spriteReference.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture);
                    var child = _objects.Create("Descriptor " + total);
                    child.SetActive(false);
                    var image = child.AddComponent<QualityUiImage>();
                    var panel = child.AddComponent<UiBackdropPanel>();
                    ApplyAuthoredFields(panel, body);
                    image.type = (Image.Type)int.Parse(Regex.Match(imageBody, @"^  m_Type: (?<v>\d+)", RegexOptions.Multiline).Groups["v"].Value,
                        System.Globalization.CultureInfo.InvariantCulture);
                    if (spriteId == 0)
                    {
                        Assert.That(panel.TryGetOpticalContour(out _), Is.False);
                        procedural++;
                        continue;
                    }
                    var spriteGuid = spriteReference.Groups["guid"].Value;
                    var spriteKey = spriteGuid + "/" + spriteId;
                    if (!sprites.TryGetValue(spriteKey, out var sourceSprite))
                    {
                        var sourcePath = AssetDatabase.GUIDToAssetPath(spriteGuid);
                        Assert.That(sourcePath, Is.Not.Empty, path + ": the exact authored Sprite asset must be available.");
                        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(sourcePath))
                        {
                            if (asset is Sprite candidate && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate,
                                out _, out long candidateId) && candidateId == spriteId)
                            {
                                sourceSprite = candidate;
                                break;
                            }
                        }
                        Assert.That(sourceSprite, Is.Not.Null, path + ": the real authored source Sprite must match both its GUID and fileID.");
                        sprites.Add(spriteKey, sourceSprite!);
                    }
                    image.sprite = sourceSprite;
                    Assert.That(panel.TryGetOpticalContour(out var contour), Is.True);
                    Assert.That(atlas.TryResolve(in contour, Vector4.one, out _, out _, out _), Is.True,
                        path + ": generated data must cover actual source component " + owner + " / " + sourceSprite!.name);
                    matched++;
                }
                if (path == "Assets/Scenes/Result.unity" || path == "Assets/Scenes/TotalResult.unity")
                {
                    Assert.That(sceneDescriptors, Is.EqualTo(path == "Assets/Scenes/Result.unity" ? 4 : 1),
                        path + ": every explicitly opted-in settlement panel must participate in baked-contour coverage.");
                }
            }
            Assert.That(total, Is.EqualTo(43));
            Assert.That(matched, Is.EqualTo(41));
            Assert.That(procedural, Is.EqualTo(2));
            TestContext.WriteLine($"OPTICS_AUTHORED total={total} matched={matched} proceduralFallback={procedural}");
#else
            Assert.Ignore("This source-coverage check requires the Editor-imported project assets.");
#endif
        }

        /// <summary>Checks matching real controls render with hardware-filtered contours, while missing data keeps the same unshifted glass composition.</summary>
        /// <returns>The actual Simple/Sliced/clipped-ellipse and missing-resource pixel regressions.</returns>
        [UnityTest]
        [Category("GPU")]
        public IEnumerator FilteredOptics_RealControlsAndUnavailableResourcesPreserveUnrefractedPixels()
        {
            LoadAtlas();
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var capture = CreateProbeCapture(64, false);
            UiOutlineTheme.Configure(UiOutlineMode.All);
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            foreach (var spriteName in new[] { "newui@3x_26", "newui@3x_16" })
            {
                var panel = CreateAuthoredPanel(canvas, spriteName);
                var image = panel.GetComponent<QualityUiImage>();
                foreach (var type in spriteName == "newui@3x_26" ? new[] { Image.Type.Simple, Image.Type.Sliced } : new[] { Image.Type.Simple })
                {
                    image.type = type;
                    image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 17f);
                    image.rectTransform.localScale = new Vector3(0.9f, 1.15f, 1f);
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    CheckUnrefractedFallbacks(image, capture, 128, false, spriteName + " " + type);
                }
                panel.gameObject.SetActive(false);
            }
        }

        /// <summary>Checks the transparent cover overlay refracts only RT3 without repainting its full-resolution cover or difficulty artwork.</summary>
        /// <returns>The authored frame's source, rim coverage and genuine displaced-sampling regression.</returns>
        [UnityTest]
        [Category("GPU")]
        public IEnumerator CoverFrame_UsesOnlyRt3AndLeavesCoverInteriorTransparent()
        {
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            UiOutlineTheme.Configure(UiOutlineMode.All);
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            const int size = 512;
            var target = _objects.Track(new RenderTexture(size, size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var resolved = _objects.Track(new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var reader = _objects.Track(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
            var ramp = CreateProbeCapture(128, false);
            var solid = _objects.Track(new Texture2D(1, 1, TextureFormat.RGBA32, false, true));
            Assert.That(target.Create() && resolved.Create(), Is.True);
            var image = CreateAuthoredDecoration(canvas, "Assets/Prefabs/List/Model/SongCoverDisplayer.prefab", 5322621527391851573L);
            yield return null;
            Canvas.ForceUpdateCanvases();
            var material = _objects.Track(new Material(image.materialForRendering));
            Assert.That(material.shader.name, Is.EqualTo("Maj/UI/List/Cover Decoration Glass"));
            Assert.That(material.renderQueue, Is.EqualTo(MajUiBackdropBridge.ButtonBodyQueue));
            material.SetInt("_StencilComp", (int)CompareFunction.Always);
            // The offscreen pixel matrix uses local Z zero; the actual authored depth is checked separately.
            material.SetFloat("_ListDepthOffset", 0f);
            var mesh = BuildMesh(image);
            SetCapture(ramp);
            Shader.SetGlobalVector("_ScreenParams", new Vector4(size, size, 1f + 1f / size, 1f + 1f / size));
            var reference = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
            Shader.SetGlobalTexture("_MajUiPanelBlurTexture", Texture2D.redTexture);
            Shader.SetGlobalTexture("_MajPvBlurTexture", Texture2D.blackTexture);
            var otherStages = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
            Assert.That(MaximumPixelDifference(reference, otherStages, 4), Is.Zero, "The overlay cannot read RT1 or RT2, which exclude its cover and difficulty artwork.");
            solid.SetPixel(0, 0, Color.green);
            solid.Apply(false, false);
            Shader.SetGlobalTexture("_MajUiButtonBlurTexture", solid);
            var green = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
            solid.SetPixel(0, 0, Color.blue);
            solid.Apply(false, false);
            var blue = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
            Assert.That(MaximumPixelDifference(green, blue, 3), Is.GreaterThan(0.05f), "The rim must transmit existing RT3 without requiring the later full-resolution cover.");
            AssertAlphaEqual(green, blue, "Changing the captured scene cannot change overlay coverage or shadows.");
            var innerRadius = image.rectTransform.rect.width * size / 256f * 0.3f;
            var maximumInnerAlpha = 0f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    if (Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), Vector2.one * (size * 0.5f)) < innerRadius)
                    {
                        maximumInnerAlpha = Mathf.Max(maximumInnerAlpha, green[y * size + x].a);
                    }
                }
            }
            Assert.That(maximumInnerAlpha, Is.Zero, "The overlay center must not reconstruct, whiten or duplicate the original full-resolution cover.");
            Shader.SetGlobalTexture("_MajUiButtonBlurTexture", ramp);
            var refraction = material.GetFloat("_Refraction");
            material.SetFloat("_Refraction", 0f);
            var unwarped = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
            material.SetFloat("_Refraction", refraction);
            var warped = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
            Assert.That(MaximumPixelDifference(unwarped, warped, 3), Is.GreaterThan(0.001f), "Frame refraction must move completed RT3 pixels rather than recolor a white ring.");
            AssertAlphaEqual(unwarped, warped, "Refraction cannot alter overlay coverage.");
            Assert.That(material.GetFloat("_Transmission"), Is.EqualTo(0.75f));
            material.SetFloat("_HighlightStrength", 0f);
            material.SetFloat("_Refraction", 0f);
            material.SetColor("_ShadowColor", Color.clear);
            foreach (var backgroundColor in new[] { new Color(0.4f, 0.6f, 0.8f, 1f), new Color(0.8f, 0.4f, 0.2f, 1f) })
            {
                solid.SetPixel(0, 0, backgroundColor);
                solid.Apply(false, false);
                Shader.SetGlobalTexture("_MajUiButtonBlurTexture", solid);
                var pixels = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
                // This point is inside the opaque annulus, close to its inner edge where the cosmetic edge shade is negligible.
                var x = Mathf.RoundToInt(size * 0.5f + image.rectTransform.rect.width * size / 256f * 0.38f);
                var actual = pixels[size / 2 * size + x];
                Assert.That(actual.a, Is.GreaterThan(0.99f), "The white glass rim must be opaque rather than revealing the removed solid ring.");
                for (var channel = 0; channel < 3; channel++)
                {
                    var expected = 0.25f + 0.75f * backgroundColor[channel];
                    Assert.That(actual[channel], Is.EqualTo(expected).Within(0.003f), "White glass must retain 25% white and 75% captured color without difficulty tint.");
                }
            }
        }

        /// <summary>Checks half-size RT3 refraction cannot create dark specks on a subpixel cover-frame silhouette without MSAA.</summary>
        /// <returns>The real capture layout, fractional-scale fringe, opaque refraction and unchanged-alpha regression.</returns>
        [UnityTest]
        [Category("GPU")]
        public IEnumerator CoverFrame_HalfRt3KeepsFractionalFringesClearWithoutMsaa()
        {
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            UiOutlineTheme.Configure(UiOutlineMode.All);
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            var image = CreateAuthoredDecoration(canvas, "Assets/Prefabs/List/Model/SongCoverDisplayer.prefab", 5322621527391851573L);
            var ring = CreateAuthoredDecoration(canvas, "Assets/Prefabs/List/Model/SongCoverDisplayer.prefab", 900000000000000303L);
            ring.color = Color.white;
            typeof(QualityUiImage).GetField("_glassInlaySource", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(image, ring);
            const int size = 512;
            var target = _objects.Track(new RenderTexture(size, size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { antiAliasing = 1 });
            var resolved = _objects.Track(new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var reader = _objects.Track(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
            var source = _objects.Track(new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            });
            var descriptor = (RenderTextureDescriptor)typeof(UiPvBackdropBlur).GetMethod("CreateDescriptor", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { new Vector2Int(size / 2, size / 2) })!;
            var capture = _objects.Track(new RenderTexture(descriptor) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp });
            var captureReader = _objects.Track(new Texture2D(size / 2, size / 2, TextureFormat.RGBAFloat, false, true));
            var copy = _objects.Track(new Material(Shader.Find("Hidden/Maj/UI PV Gaussian")));
            copy.SetVector("_MajUiCaptureClipUvX", Vector4.zero);
            Assert.That(target.Create() && resolved.Create() && capture.Create(), Is.True);
            Assert.That(target.antiAliasing, Is.EqualTo(1), "The regression must exercise the actual MSAA-off path.");
            Assert.That(capture.useMipMap, Is.False);
            var background = new Color(0.94f, 0.93f, 0.9f, 1f);
            var backgroundDetail = new Color(0.90f, 0.88f, 0.85f, 1f);
            var difficulty = new Color(0.65f, 0.04f, 0.95f, 1f);
            var prefix = new Color[size * size];
            foreach (var extent in new[] { 175f, 365f })
            {
                image.rectTransform.sizeDelta = Vector2.one * extent;
                ring.rectTransform.sizeDelta = Vector2.one * (extent * 310f / 365f);
                yield return null;
                Canvas.ForceUpdateCanvases();
                var material = _objects.Track(new Material(image.materialForRendering));
                material.SetInt("_StencilComp", (int)CompareFunction.Always);
                material.SetFloat("_ListDepthOffset", 0f);
                material.SetColor("_ShadowColor", Color.clear);
                var refraction = material.GetFloat("_Refraction");
                Assert.That(refraction, Is.GreaterThan(0f));
                foreach (var phase in new[] { new Vector3(1f, 0f, 0f), new Vector3(0.9975f, 0.25f, 0.5f), new Vector3(1.003f, 0.5f, 0.25f) })
                {
                    var center = Vector2.one * (size * 0.5f) + new Vector2(phase.y, phase.z);
                    var frameRadius = material.GetFloat("_Radius") * extent * phase.x;
                    var ringRadius = extent * phase.x * 310f / 365f * 0.5f;
                    var coverRadius = extent * phase.x * 285f / 365f * 0.5f;
                    // Stress the captured ring with a black center and a pale textured background.
                    // The white frame is still absent from this half-copy, just as in production.
                    for (var y = 0; y < size; y++)
                    {
                        for (var x = 0; x < size; x++)
                        {
                            var radius = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                            var ringAlpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ringRadius - 0.5f, ringRadius + 0.5f, radius));
                            var coverAlpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(coverRadius - 0.5f, coverRadius + 0.5f, radius));
                            var underlay = x % 6 < 3 ? backgroundDetail : background;
                            prefix[y * size + x] = Color.Lerp(Color.Lerp(underlay, difficulty, ringAlpha), Color.black, coverAlpha);
                        }
                    }
                    source.SetPixels(prefix);
                    source.Apply(false, false);
                    Graphics.Blit(source, capture, copy, 3);
                    RenderTexture.active = capture;
                    captureReader.ReadPixels(new Rect(0, 0, capture.width, capture.height), 0, 0);
                    captureReader.Apply(false, false);
                    Assert.That(captureReader.GetPixel(capture.width / 2, capture.height / 2).grayscale, Is.LessThan(0.001f), "The captured cover must really be black.");
                    var shoulder = (center + Vector2.right * (frameRadius - 0.25f)) * 0.5f;
                    Assert.That(captureReader.GetPixel(Mathf.RoundToInt(shoulder.x), Mathf.RoundToInt(shoulder.y)).grayscale, Is.GreaterThan(0.85f), "The unfinished RT3 shoulder must contain the ordinary background, not a prepainted white frame.");
                    SetCapture(capture);
                    Shader.SetGlobalVector("_ScreenParams", new Vector4(size, size, 1f + 1f / size, 1f + 1f / size));
                    var mesh = BuildMesh(image);
                    var vertices = mesh.vertices;
                    for (var index = 0; index < vertices.Length; index++)
                    {
                        vertices[index] = vertices[index] * phase.x + new Vector3(phase.y, phase.z, 0f);
                    }
                    mesh.vertices = vertices;
                    mesh.RecalculateBounds();
                    material.SetFloat("_Refraction", 0f);
                    var unwarped = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard: true, axisAligned: true);
                    material.SetFloat("_Refraction", refraction);
                    var warped = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard: true, axisAligned: true);
                    AssertAlphaEqual(unwarped, warped, "Capture displacement must not alter the frame silhouette.");
                    var fringeDifference = 0f;
                    var interiorDifference = 0f;
                    var minimumShoulder = 1f;
                    var fringeSamples = 0;
                    var previewFlat = new Color[prefix.Length];
                    var previewWarped = new Color[prefix.Length];
                    for (var y = 0; y < size; y++)
                    {
                        for (var x = 0; x < size; x++)
                        {
                            var index = y * size + x;
                            var radius = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                            var difference = warped[index] - unwarped[index];
                            var rgbDifference = Mathf.Max(Mathf.Abs(difference.r), Mathf.Max(Mathf.Abs(difference.g), Mathf.Abs(difference.b)));
                            if (Mathf.Abs(radius - frameRadius) < 1.5f && warped[index].a > 0f && warped[index].a < 1f)
                            {
                                fringeDifference = Mathf.Max(fringeDifference, rgbDifference);
                                var composed = warped[index] + prefix[index] * (1f - warped[index].a);
                                minimumShoulder = Mathf.Min(minimumShoulder, Mathf.Min(composed.r, Mathf.Min(composed.g, composed.b)));
                                fringeSamples++;
                            }
                            else if (radius < frameRadius - 1f && warped[index].a > 0.999f)
                            {
                                interiorDifference = Mathf.Max(interiorDifference, rgbDifference);
                            }
                            previewFlat[index] = unwarped[index] + prefix[index] * (1f - unwarped[index].a);
                            previewWarped[index] = warped[index] + prefix[index] * (1f - warped[index].a);
                        }
                    }
                    WriteWhiteEdgePreview($"CoverHalfRt3-{extent:F0}-{phase.x:F4}-{phase.y:F2}-{phase.z:F2}", previewFlat, previewWarped, size);
                    TestContext.WriteLine($"COVER_RT3_FRINGE extent={extent:F0} scale={phase.x:F4} phase={phase.y:F2},{phase.z:F2} samples={target.antiAliasing} fringe={fringeSamples} fringeDifference={fringeDifference:F7} minimumShoulder={minimumShoulder:F5} interiorDifference={interiorDifference:F6}");
                    Assert.That(fringeSamples, Is.GreaterThan(64));
                    Assert.That(fringeDifference, Is.LessThan(0.00001f), "The partially covered outer contour must retain the stable unshifted capture sample.");
                    Assert.That(minimumShoulder, Is.GreaterThan(0.75f), "The pale outer shoulder must not acquire isolated black capture specks.");
                    Assert.That(interiorDifference, Is.GreaterThan(0.005f), "The fully covered annulus must still refract the real colored-ring and black-cover prefix.");
                }
            }
        }

        /// <summary>Checks the visible cover frame has a softly shaded face, white reflections and an independent exterior shadow.</summary>
        /// <returns>The real white annulus, captured inlay, broad highlight and composited-shadow regressions.</returns>
        [UnityTest]
        [Category("GPU")]
        public IEnumerator CoverRimLighting_VisibleBandReflectsOnBothBackgroundsAndCastsShadow()
        {
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            UiOutlineTheme.Configure(UiOutlineMode.All);
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            var image = CreateAuthoredDecoration(canvas, "Assets/Prefabs/List/Model/SongCoverDisplayer.prefab", 5322621527391851573L);
            var ring = CreateAuthoredDecoration(canvas, "Assets/Prefabs/List/Model/SongCoverDisplayer.prefab", 900000000000000303L);
            ring.rectTransform.sizeDelta = image.rectTransform.rect.size * (310f / 365f);
            ring.color = Color.white;
            typeof(QualityUiImage).GetField("_glassInlaySource", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(image, ring);
            yield return null;
            Canvas.ForceUpdateCanvases();
            var material = _objects.Track(new Material(image.materialForRendering));
            material.SetFloat("_ListDepthOffset", 0f);
            material.SetFloat("_Refraction", 0f);
            material.SetInt("_StencilComp", (int)CompareFunction.Always);
            var mesh = BuildMesh(image);
            const int size = 512;
            var target = _objects.Track(new RenderTexture(size, size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var resolved = _objects.Track(new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var reader = _objects.Track(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
            Assert.That(target.Create() && resolved.Create(), Is.True);
            var capture = _objects.Track(new Texture2D(size / 2, size / 2, TextureFormat.RGBA32, false, true)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            });
            var sourcePixels = new Color[capture.width * capture.height];
            var pixelScale = image.rectTransform.rect.width * size / 256f;
            var frameRadius = material.GetFloat("_Radius") * pixelScale;
            var coverRadius = 0.5f * 285f / 365f * pixelScale;
            var ringRadius = 0.5f * 310f / 365f * pixelScale;
            var strength = material.GetFloat("_HighlightStrength");
            var shadow = material.GetColor("_ShadowColor");
            var whiteCaptureMiddle = 0f;
            foreach (var background in new[] { Color.white, new Color(0.12f, 0.12f, 0.12f, 1f) })
            {
                for (var y = 0; y < capture.height; y++)
                {
                    for (var x = 0; x < capture.width; x++)
                    {
                        var radius = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), Vector2.one * (capture.width * 0.5f)) * 2f;
                        sourcePixels[y * capture.width + x] = radius <= ringRadius ? new Color(0.6f, 0.2f, 1f, 1f) : background;
                    }
                }
                capture.SetPixels(sourcePixels);
                capture.Apply(false, false);
                SetCapture(capture);
                material.SetFloat("_HighlightStrength", 0f);
                var flat = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
                material.SetFloat("_HighlightStrength", strength);
                var lit = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
                AssertAlphaEqual(flat, lit, "Face lighting must not change the silhouette, coverage or shadow footprint.");
                var faceRadius = (ringRadius + frameRadius) * 0.5f;
                Assert.That(faceRadius, Is.GreaterThan(coverRadius + 2f));
                var face = new Color[3];
                var flatFace = new Color[3];
                for (var band = 0; band < face.Length; band++)
                {
                    var angle = (band - 1) * Mathf.PI * 0.25f;
                    var point = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * faceRadius + Vector2.one * (size * 0.5f);
                    face[band] = AverageInteriorRgb(lit, size, point, 1, 1);
                    flatFace[band] = AverageInteriorRgb(flat, size, point, 1, 1);
                }
                Assert.That(face[2].grayscale, Is.GreaterThan(face[1].grayscale + 0.003f), "The visible upper white annulus must have a soft face highlight.");
                Assert.That(face[0].grayscale, Is.GreaterThan(face[1].grayscale + 0.002f), "The white annulus needs a lower soft reflection rather than a hard dark bevel.");
                var centerDrop = flatFace[1].grayscale - face[1].grayscale;
                if (background.r > 0.5f)
                {
                    Assert.That(centerDrop / flatFace[1].grayscale, Is.InRange(0.03f, 0.08f), "The white frame's middle face must have restrained relative absorption over a white capture.");
                    whiteCaptureMiddle = face[1].grayscale;
                }
                else
                {
                    Assert.That(whiteCaptureMiddle - face[1].grayscale, Is.GreaterThan(0.25f),
                        "Dark-capture reflections may lift the middle, but the frame must retain a clear transmitted response instead of becoming a white veil.");
                }
                foreach (var side in new[] { -1f, 1f })
                {
                    var point = new Vector2(size * 0.5f, size * 0.5f + side * (frameRadius - 2f));
                    var edge = AverageInteriorRgb(lit, size, point, 1, 0);
                    Assert.That(edge.grayscale, Is.GreaterThan(face[side < 0 ? 0 : 2].grayscale + 0.003f),
                        "Both visible arcs need a white contour reflection rather than a black outline.");
                }
                var purplePoint = Vector2.up * ((coverRadius + ringRadius) * 0.5f) + Vector2.one * (size * 0.5f);
                var purpleIndex = Mathf.RoundToInt(purplePoint.y) * size + Mathf.RoundToInt(purplePoint.x);
                Assert.That(lit[purpleIndex].g - flat[purpleIndex].g, Is.GreaterThan(0.005f), "A saturated colored inlay must retain an independent visible reflection.");
                material.SetFloat("_HighlightStrength", 0f);
                material.SetColor("_ShadowColor", Color.clear);
                var unshadowed = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
                material.SetColor("_ShadowColor", shadow);
                var maximumShadowDrop = 0f;
                var totalShadowDrop = 0f;
                var shadowSamples = 0;
                var broadShadowAngles = 0;
                var maximumAlpha = 0f;
                var shadowExtent = Mathf.CeilToInt((material.GetFloat("_ShadowSoftness") + ((Vector2)material.GetVector("_ShadowOffset")).magnitude) * pixelScale) + 2;
                for (var angle = 0; angle < 48; angle++)
                {
                    var radians = angle * Mathf.PI * 2f / 48f;
                    var normal = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                    var significantSamples = 0;
                    for (var offset = 1; offset <= shadowExtent; offset++)
                    {
                        var point = normal * (frameRadius + offset) + Vector2.one * (size * 0.5f);
                        var index = Mathf.RoundToInt(point.y) * size + Mathf.RoundToInt(point.x);
                        var withShadow = flat[index].r + background.r * (1f - flat[index].a);
                        var withoutShadow = unshadowed[index].r + background.r * (1f - unshadowed[index].a);
                        var drop = Mathf.Max(0f, withoutShadow - withShadow);
                        maximumShadowDrop = Mathf.Max(maximumShadowDrop, drop);
                        totalShadowDrop += drop;
                        shadowSamples++;
                        maximumAlpha = Mathf.Max(maximumAlpha, flat[index].a);
                        if (drop > background.r * 0.02f)
                        {
                            significantSamples++;
                        }
                    }
                    if (significantSamples >= 3)
                    {
                        broadShadowAngles++;
                    }
                }
                Assert.That(maximumAlpha, Is.LessThanOrEqualTo(1.0001f), "Single exterior suppression must not over-accumulate shadow alpha.");
                Assert.That(broadShadowAngles, Is.GreaterThanOrEqualTo(16), "A soft cast shadow must cover several radial pixels across a broad arc rather than a single bright sample.");
                var meanShadowDrop = totalShadowDrop / shadowSamples;
                Assert.That(meanShadowDrop, Is.GreaterThan(background.r * 0.025f), "The distributed shadow must survive actual RGB composition, not only an isolated peak.");
                Assert.That(maximumShadowDrop, Is.GreaterThan(background.r > 0.5f ? 0.02f : 0.002f), "The authored shadow must measurably darken the actual composited background outside the glass.");
                WriteWhiteEdgePreview($"CoverFace-{(background.r > 0.5f ? "White" : "Dark")}", flat, lit, size);
                TestContext.WriteLine($"COVER_FACE background={background.r:F2} bottom={face[0].grayscale:F5} middle={face[1].grayscale:F5} top={face[2].grayscale:F5} shade={centerDrop:F5} shadow={maximumShadowDrop:F5} shadowMean={meanShadowDrop:F5} shadowArcs={broadShadowAngles}");
            }
        }

        /// <summary>Checks the actual small and center cover frames cast a detached soft shadow instead of a dark rail at their outer contour.</summary>
        /// <returns>The native-size, white/dark-background radial-shadow and unchanged-cover-coverage regression.</returns>
        [UnityTest]
        [Category("GPU")]
        public IEnumerator CoverFrame_OuterShadowStartsSoftlyAtAuthoredSizes()
        {
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            UiOutlineTheme.Configure(UiOutlineMode.All);
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            var image = CreateAuthoredDecoration(canvas, "Assets/Prefabs/List/Model/SongCoverDisplayer.prefab", 5322621527391851573L);
            var ring = CreateAuthoredDecoration(canvas, "Assets/Prefabs/List/Model/SongCoverDisplayer.prefab", 900000000000000303L);
            ring.color = Color.white;
            typeof(QualityUiImage).GetField("_glassInlaySource", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(image, ring);
            const int size = 512;
            var target = _objects.Track(new RenderTexture(size, size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var resolved = _objects.Track(new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var reader = _objects.Track(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
            var capture = _objects.Track(new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true));
            Assert.That(target.Create() && resolved.Create(), Is.True);

            // Reconstruct physical positions from the resolved readback; no lighting equation is reproduced here.
            Color CompositeAt(Color[] pixels, Vector2 point, Color background)
            {
                var coordinate = point - Vector2.one * 0.5f;
                var x = Mathf.FloorToInt(coordinate.x);
                var y = Mathf.FloorToInt(coordinate.y);
                var color = Color.Lerp(Color.Lerp(pixels[y * size + x], pixels[y * size + x + 1], coordinate.x - x),
                    Color.Lerp(pixels[(y + 1) * size + x], pixels[(y + 1) * size + x + 1], coordinate.x - x), coordinate.y - y);
                return new Color(color.r + background.r * (1f - color.a), color.g + background.g * (1f - color.a),
                    color.b + background.b * (1f - color.a), 1f);
            }

            // The authored side frame is 175 UI units; the selected cover uses 365 without changing its shape.
            foreach (var extent in new[] { 175f, 365f })
            {
                image.rectTransform.sizeDelta = Vector2.one * extent;
                ring.rectTransform.sizeDelta = Vector2.one * (extent * 310f / 365f);
                yield return null;
                Canvas.ForceUpdateCanvases();
                var material = _objects.Track(new Material(image.materialForRendering));
                material.SetInt("_StencilComp", (int)CompareFunction.Always);
                material.SetFloat("_ListDepthOffset", 0f);
                material.SetFloat("_Refraction", 0f);
                var strength = material.GetFloat("_HighlightStrength");
                var shadow = material.GetColor("_ShadowColor");
                var radius = material.GetFloat("_Radius") * extent;
                var shadowExtent = Mathf.CeilToInt((material.GetFloat("_ShadowSoftness") + ((Vector2)material.GetVector("_ShadowOffset")).magnitude) * extent) + 2;
                var mesh = BuildMesh(image);
                foreach (var background in new[] { Color.white, new Color(0.12f, 0.12f, 0.12f, 1f) })
                {
                    capture.SetPixel(0, 0, background);
                    capture.Apply(false, false);
                    SetCapture(capture);
                    Shader.SetGlobalVector("_ScreenParams", new Vector4(size, size, 1f + 1f / size, 1f + 1f / size));
                    material.SetFloat("_HighlightStrength", 0f);
                    material.SetColor("_ShadowColor", shadow);
                    var shadowed = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard: true, axisAligned: true);
                    material.SetColor("_ShadowColor", Color.clear);
                    var unshadowed = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard: true, axisAligned: true);
                    material.SetColor("_ShadowColor", shadow);
                    material.SetFloat("_HighlightStrength", strength);
                    var lit = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard: true, axisAligned: true);
                    AssertAlphaEqual(shadowed, lit, "Soft frame lighting cannot alter the cover or shadow coverage.");
                    Assert.That(lit[size / 2 * size + size / 2].a, Is.Zero, "The frame must leave the later full-resolution cover interior clear.");
                    WriteWhiteEdgePreview($"CoverOuterProfile-{Mathf.RoundToInt(extent)}-{(background.r > 0.5f ? "White" : "Dark")}", shadowed, lit, size);
                    var contactSum = 0f;
                    var peakSum = 0f;
                    var maximumPeak = 0f;
                    var maximumStep = 0f;
                    var totalSpan = 0f;
                    var softArcs = 0;
                    for (var angle = 0; angle < 32; angle++)
                    {
                        var radians = angle * Mathf.PI * 2f / 32f;
                        var normal = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                        var profile = new float[shadowExtent * 4 + 1];
                        var peak = 0f;
                        for (var step = 0; step < profile.Length; step++)
                        {
                            var point = Vector2.one * (size * 0.5f) + normal * (radius + step * 0.25f);
                            var withShadow = CompositeAt(shadowed, point, background);
                            var withoutShadow = CompositeAt(unshadowed, point, background);
                            profile[step] = Mathf.Max(0f, (withoutShadow.grayscale - withShadow.grayscale) / background.grayscale);
                            peak = Mathf.Max(peak, profile[step]);
                            if (step > 0)
                            {
                                maximumStep = Mathf.Max(maximumStep, Mathf.Abs(profile[step] - profile[step - 1]));
                            }
                        }
                        contactSum += profile[0];
                        peakSum += peak;
                        maximumPeak = Mathf.Max(maximumPeak, peak);
                        if (peak > 0.005f)
                        {
                            var first = -1;
                            var last = -1;
                            for (var step = 0; step < profile.Length; step++)
                            {
                                if (profile[step] >= peak * 0.25f)
                                {
                                    if (first < 0)
                                    {
                                        first = step;
                                    }
                                    last = step;
                                }
                            }
                            totalSpan += (last - first) * 0.25f;
                            softArcs++;
                        }
                    }
                    TestContext.WriteLine($"COVER_OUTER_PROFILE extent={extent:F0} capture={background.r:F2} contact={contactSum / 32f:F6} peak={peakSum / 32f:F6} ratio={contactSum / peakSum:F6} maximumStep={maximumStep:F6} softArcs={softArcs} meanSpan={totalSpan / Mathf.Max(softArcs, 1):F3}");
                    Assert.That(peakSum / 32f, Is.GreaterThan(0.015f), "The frame must retain a visible exterior soft shadow.");
                    Assert.That(contactSum, Is.LessThan(peakSum * 0.25f), "The actual silhouette cannot abruptly switch on a dark shadow rail.");
                    Assert.That(maximumPeak, Is.LessThanOrEqualTo(shadow.a + 0.002f), "The soft shadow cannot accumulate extra opacity at the contour.");
                    Assert.That(maximumStep, Is.LessThan(0.05f), "The native-size outer profile must change smoothly across subpixel positions.");
                    Assert.That(softArcs, Is.GreaterThanOrEqualTo(16));
                    Assert.That(totalSpan / softArcs, Is.GreaterThan(1f), "The exterior shadow must occupy a soft band rather than a narrow dark line.");
                }
            }
        }

        /// <summary>Checks native CanvasRenderer forwards the frame's full UV0 metadata while clearcoat preserves captured difficulty RGB.</summary>
        /// <returns>The actual UGUI camera readback, half-size source and visible-opacity regression.</returns>
        [UnityTest]
        [Category("GPU")]
        public IEnumerator CoverClearcoat_NativeCanvasPreservesInlayUvAndCapturedColor()
        {
            var oldPipeline = GraphicsSettings.defaultRenderPipeline;
            var oldQualityPipeline = QualitySettings.renderPipeline;
            var oldCamera = SceneSwitcher.MainCamera;
            Camera? nativeCamera = null;
            Action<RenderTargetIdentifier, CommandBuffer>? finalCapture = null;
            try
            {
                var pipeline = _objects.Track(ScriptableObject.CreateInstance<MajRenderPipelineAsset>());
                typeof(MajRenderPipelineAsset).GetField("_finalCopyShader", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(pipeline, Shader.Find("Hidden/Maj/FinalCopy"));
                pipeline.ConfigureRuntimeRendering(1f, MajMSAAMode.Disabled, false);
                GraphicsSettings.defaultRenderPipeline = pipeline;
                QualitySettings.renderPipeline = pipeline;
                var camera = _objects.Add<Camera>();
                nativeCamera = camera;
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = 128f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.white;
                SceneSwitcher.MainCamera = camera;
                const int size = 512;
                var target = _objects.Track(new RenderTexture(size, size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
                Assert.That(target.Create(), Is.True);
                camera.pixelRect = new Rect(0f, 0f, size, size);
                finalCapture = (source, commands) => commands.Blit(source, target);
                MajCameraCaptureBridge.AddCaptureAction(camera, finalCapture);
                var canvas = _objects.Add<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 10f;
                var channels = canvas.additionalShaderChannels;
                UiOutlineTheme.Configure(UiOutlineMode.All);
                UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
                var image = CreateAuthoredDecoration(canvas, "Assets/Prefabs/List/Model/SongCoverDisplayer.prefab", 5322621527391851573L);
                var ring = CreateAuthoredDecoration(canvas, "Assets/Prefabs/List/Model/SongCoverDisplayer.prefab", 900000000000000303L);
                ring.rectTransform.sizeDelta = new Vector2(168f, 168f);
                ring.transform.SetAsFirstSibling();
                typeof(QualityUiImage).GetField("_glassInlaySource", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(image, ring);
                var flat = _objects.Track(new Material(image.material));
                flat.SetFloat("_ListDepthOffset", 0f);
                flat.SetFloat("_HighlightStrength", 0f);
                flat.SetFloat("_Refraction", 0f);
                flat.SetColor("_ShadowColor", Color.clear);
                typeof(QualityUiImage).GetField("_liquidGlassMaterial", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(image, flat);
                image.RefreshMaterialSelection();
                var source = new Color(0.2f, 0.4f, 0.8f, 1f);

                var reader = _objects.Track(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
                camera.enabled = true;
                yield return null;
                yield return null;
                var nativeUvs = new List<Vector4>();
                foreach (var opacity in new[] { 1f, 0.5f, 0f })
                {
                    ring.color = new Color(source.r, source.g, source.b, opacity);
                    image.SetVerticesDirty();
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    var nativeMesh = image.canvasRenderer.GetMesh();
                    nativeMesh.GetUVs(0, nativeUvs);
                    var before = MajUiBackdropBridge.GetActive(camera) != null;
                    camera.Render();
                    TestContext.WriteLine($"NATIVE_CANVAS opacity={opacity} captureBefore={before} ready={Shader.GetGlobalFloat("_MajUiButtonBlurReady")} cull={image.canvasRenderer.cull} materials={image.canvasRenderer.materialCount} vertices={nativeMesh.vertexCount} uv0={(nativeUvs.Count > 0 ? nativeUvs[0] : Vector4.zero)} queue={image.materialForRendering.renderQueue} rect={image.rectTransform.rect} position={image.transform.position} camera={camera.transform.position} viewport={camera.pixelRect}");
                    RenderTexture.active = target;
                    reader.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                    reader.Apply(false, false);
                    var viewport = camera.WorldToViewportPoint(image.transform.TransformPoint(new Vector3(image.rectTransform.rect.width * 0.4f, 0f, 0f)));
                    var pixel = reader.GetPixel(Mathf.Clamp((int)(viewport.x * size), 0, size - 1), Mathf.Clamp((int)(viewport.y * size), 0, size - 1));
                    var capture = (RenderTexture)typeof(UiPvBackdropBlur).GetField("s_liquidButtonTexture", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
                    var captureReader = _objects.Track(new Texture2D(capture.width, capture.height, TextureFormat.RGBAFloat, false, true));
                    Assert.That(capture.filterMode, Is.EqualTo(FilterMode.Bilinear));
                    RenderTexture.active = capture;
                    captureReader.ReadPixels(new Rect(0, 0, capture.width, capture.height), 0, 0);
                    captureReader.Apply(false, false);
                    var captured = captureReader.GetPixel(Mathf.Clamp((int)(viewport.x * capture.width), 0, capture.width - 1),
                        Mathf.Clamp((int)(viewport.y * capture.height), 0, capture.height - 1));
                    TestContext.WriteLine($"NATIVE_PIXELS viewport={viewport} target={pixel} capture={captured} pool={capture.width}x{capture.height} pipeline={RenderPipelineManager.currentPipeline?.GetType().Name} status={MajRenderDiagnostics.OutputStatus}");
                    var transmission = Mathf.Lerp(0.75f, 1f, opacity);
                    for (var channel = 0; channel < 3; channel++)
                    {
                        Assert.That(captured[channel], Is.EqualTo(Mathf.Lerp(1f, source[channel], opacity)).Within(0.012f), "The real native ring must be present in the half-size RT3 prefix.");
                        Assert.That(pixel[channel], Is.EqualTo(1f - transmission + transmission * captured[channel]).Within(0.008f),
                            "Native Canvas must preserve UV0 Z/W so captured ring RGB is polished directly instead of being whitened or difficulty-tinted.");
                    }
                    Assert.That(canvas.additionalShaderChannels, Is.EqualTo(channels), "Inlay data must use UV0 without adding Canvas channels.");
                }
                ring.color = new Color(source.r, source.g, source.b, 1f);
                var fallbackMesh = BuildMesh(image);
                Shader.SetGlobalFloat("_MajUiButtonBlurReady", 0f);
                var fallbackResolved = _objects.Track(new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
                Assert.That(fallbackResolved.Create(), Is.True);
                var fallback = Draw(image, flat, fallbackMesh, target, fallbackResolved, reader, 1, axisAligned: true);
                var inside = size / 2 * size + Mathf.RoundToInt(size * 0.5f + image.rectTransform.rect.width * size / 256f * 0.4f);
                var outside = size / 2 * size + Mathf.RoundToInt(size * 0.5f + image.rectTransform.rect.width * size / 256f * 0.44f);
                Assert.That(fallback[inside].a, Is.Zero, "Unavailable RT3 must make clearcoat transparent above the original colored ring instead of erasing it with white.");
                Assert.That(fallback[outside].a, Is.GreaterThan(0.99f), "Outside the ring, unavailable capture must retain the ordinary opaque white frame.");
            }
            finally
            {
                if (nativeCamera != null)
                {
                    nativeCamera.enabled = false;
                    if (finalCapture != null)
                    {
                        MajCameraCaptureBridge.RemoveCaptureAction(nativeCamera, finalCapture);
                    }
                }
                GraphicsSettings.defaultRenderPipeline = oldPipeline;
                QualitySettings.renderPipeline = oldQualityPipeline;
                SceneSwitcher.MainCamera = oldCamera;
            }
        }

        /// <summary>Checks both extents of the single native category retain optics, masking and animated coverage.</summary>
        /// <returns>The two-prefab, two-state native/stencil and real GPU regression.</returns>
        [UnityTest]
        [Category("GPU")]
        public IEnumerator NativeCategories_ActualSelectedAndUnselectedControlsRetainLiquidOptics()
        {
            var atlas = LoadAtlas();
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var mask = _objects.Create("Actual category stencil parent", canvas.transform);
            mask.AddComponent<Image>();
            mask.AddComponent<Mask>().showMaskGraphic = false;
            UiOutlineTheme.Configure(UiOutlineMode.All);
            var capture = CreateProbeCapture(128, false);
            var profile = atlas.ProfileTexture!;
            var response = ReadFloatTexture(profile);
            var zeroData = new ushort[profile.width * 2];
            for (var index = 0; index < profile.width; index++)
            {
                zeroData[index * 2 + 1] = Mathf.FloatToHalf(response[index].g);
            }
            var zeroDisplacement = _objects.Track(new Texture2D(profile.width, 1, TextureFormat.RGHalf, false, true)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            });
            zeroDisplacement.SetPixelData(zeroData, 0);
            zeroDisplacement.Apply(false, true);
            var solid = _objects.Track(new Texture2D(1, 1, TextureFormat.RGBA32, false, true));
            solid.SetPixel(0, 0, new Color(0.17f, 0.31f, 0.47f, 1f));
            solid.Apply(false, false);
            const int size = 512;
            var target = _objects.Track(new RenderTexture(size, size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var resolved = _objects.Track(new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var reader = _objects.Track(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
            Assert.That(target.Create() && resolved.Create(), Is.True);
            Shader.SetGlobalVector("_ScreenParams", new Vector4(size, size, 1f + 1f / size, 1f + 1f / size));
            foreach (var setting in new[] { false, true })
            {
                foreach (var selected in new[] { false, true })
                {
                    UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
                    var panel = CreateAuthoredCategory(canvas, setting, selected);
                    var image = panel.GetComponent<QualityUiImage>();
                    image.transform.SetParent(mask.transform, false);
                    var originalColor = image.color;
                    var sourceMaterial = image.SourceMaterial;
                    var originalSize = image.rectTransform.sizeDelta;
                    foreach (var extent in new[] { originalSize, new Vector2(180f, 52f) })
                    {
                        image.rectTransform.sizeDelta = extent;
                        yield return null;
                        Canvas.ForceUpdateCanvases();
                        var owned = image.material;
                        var rendered = image.canvasRenderer.GetMaterial(0);
                        Assert.That(rendered, Is.Not.SameAs(owned));
                        Assert.That(owned.GetFloat("_BackdropIsPanel"), Is.Zero, "Native category capsules must retain their small-control lighting.");
                        Assert.That(rendered.GetFloat("_BackdropIsPanel"), Is.Zero, "The stencil variant must retain the same small-control policy.");
                        Assert.That(image.SourceMaterial, Is.SameAs(sourceMaterial));
                        Assert.That(image.color, Is.EqualTo(originalColor));
                        Assert.That(panel.RefractionReady, Is.True);
                        foreach (var material in new[] { owned, rendered })
                        {
                            Assert.That(material.GetFloat("_LiquidNativeContourReady"), Is.EqualTo(1f));
                            Assert.That(material.GetFloat("_LiquidContourReady"), Is.Zero);
                            Assert.That(material.GetFloat("_LiquidRefractionReady"), Is.EqualTo(1f));
                            Assert.That(material.IsKeywordEnabled(UiBackdropPanel.LiquidStyleKeyword), Is.True);
                        }
                        Assert.That(image.sprite, Is.Null);
                        var expectedRadius = Mathf.Min(40f, Mathf.Min(extent.x, extent.y) * 0.5f);
                        Assert.That(owned.GetVector("_LiquidGlassCorners"), Is.EqualTo(Vector4.one * expectedRadius));
                        foreach (var radius in new[] { "_RadiusTL", "_RadiusTR", "_RadiusBL", "_RadiusBR" })
                        {
                            Assert.That(owned.GetFloat(radius), Is.EqualTo(40f), "Authored corner values stay unchanged while the shader clamps them to the live extent.");
                        }
                        var probe = _objects.Track(new Material(rendered));
                        probe.SetInt("_StencilComp", (int)CompareFunction.Always);
                        var mesh = BuildMesh(image);
                        SetCapture(capture);
                        var refracted = Draw(image, probe, mesh, target, resolved, reader, 1, axisAligned: true);
                        probe.SetTexture("_LiquidProfileTex", zeroDisplacement);
                        var unmoved = Draw(image, probe, mesh, target, resolved, reader, 1, axisAligned: true);
                        var displacementDifference = MaximumPixelDifference(refracted, unmoved, 3);
                        Assert.That(displacementDifference, Is.GreaterThan(0.001f), "Both actual category states must sample displaced background pixels, independently of profile lighting.");
                        AssertAlphaEqual(refracted, unmoved, "Refraction cannot change native or Sprite coverage.");
                        probe.SetTexture("_LiquidProfileTex", profile);
                        SetCapture(solid);
                        var highlighted = Draw(image, probe, mesh, target, resolved, reader, 1, axisAligned: true);
                        var highlight = probe.GetFloat("_GlassHighlight");
                        probe.SetFloat("_GlassHighlight", 0f);
                        var noHighlight = Draw(image, probe, mesh, target, resolved, reader, 1, axisAligned: true);
                        var highlightDifference = MaximumPixelDifference(highlighted, noHighlight, 3);
                        Assert.That(highlightDifference, Is.GreaterThan(0.001f), "A flat background must still reveal the category's independent neutral edge reflection.");
                        AssertAlphaEqual(highlighted, noHighlight, "Highlight intensity cannot change coverage.");
                        probe.SetFloat("_GlassHighlight", highlight);
                        probe.SetFloat("_LiquidRefractionReady", 0f);
                        var fallback = Draw(image, probe, mesh, target, resolved, reader, 1, axisAligned: true);
                        AssertAlphaEqual(highlighted, fallback, "The optical branch cannot change the native clipping silhouette.");
                        probe.SetFloat("_LiquidRefractionReady", 1f);
                        const string requiredContour = "_LiquidNativeContourReady";
                        foreach (var disabled in new[] { requiredContour, "_LiquidProfileReady" })
                        {
                            probe.SetFloat(disabled, 0f);
                            var unavailable = Draw(image, probe, mesh, target, resolved, reader, 1, axisAligned: true);
                            Assert.That(MaximumPixelDifference(unavailable, fallback, 4), Is.Zero, disabled + " must retain the ordinary unshifted glass fallback.");
                            probe.SetFloat(disabled, 1f);
                        }
                        TestContext.WriteLine($"NATIVE_CATEGORY setting={setting} selected={selected} extent={extent} displacement={displacementDifference:F8} highlight={highlightDifference:F8}");
                    }
                    SetCapture(solid);
                    var fadeMaterial = _objects.Track(new Material(image.materialForRendering));
                    fadeMaterial.SetInt("_StencilComp", (int)CompareFunction.Always);
                    var resting = Draw(image, fadeMaterial, BuildMesh(image), target, resolved, reader, 1, axisAligned: true);
                    image.color = new Color(originalColor.r, originalColor.g, originalColor.b, originalColor.a * 0.5f);
                    Canvas.ForceUpdateCanvases();
                    var faded = Draw(image, fadeMaterial, BuildMesh(image), target, resolved, reader, 1, axisAligned: true);
                    var fadeRatio = Mathf.Round(originalColor.a * 0.5f * 255f) / Mathf.Max(Mathf.Round(originalColor.a * 255f), 1f);
                    var maximumFadeError = 0f;
                    for (var index = 0; index < faded.Length; index++)
                    {
                        for (var channel = 0; channel < 4; channel++)
                        {
                            maximumFadeError = Mathf.Max(maximumFadeError, Mathf.Abs(faded[index][channel] - resting[index][channel] * fadeRatio));
                        }
                    }
                    Assert.That(maximumFadeError, Is.LessThan(0.001f), "Native state/fade animation must still scale finished premultiplied color and coverage together.");
                    image.color = originalColor;
                    UiPvBackdropBlur.Configure(true, UiBackdropStyle.GaussianBlur);
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    var gaussian = _objects.Track(new Material(image.canvasRenderer.GetMaterial(0)));
                    gaussian.SetInt("_StencilComp", (int)CompareFunction.Always);
                    Assert.That(gaussian.IsKeywordEnabled(UiBackdropPanel.LiquidStyleKeyword), Is.False);
                    var gaussianMesh = BuildMesh(image);
                    SetCapture(capture);
                    var gaussianPixels = Draw(image, gaussian, gaussianMesh, target, resolved, reader, 1, axisAligned: true);
                    gaussian.SetFloat("_LiquidNativeContourReady", 0f);
                    gaussian.SetFloat("_LiquidContourReady", 0f);
                    gaussian.SetFloat("_LiquidProfileReady", 0f);
                    var gaussianWithoutOptics = Draw(image, gaussian, gaussianMesh, target, resolved, reader, 1, axisAligned: true);
                    Assert.That(MaximumPixelDifference(gaussianPixels, gaussianWithoutOptics, 4), Is.Zero, "Gaussian categories cannot consume either optical-contour path.");
                    panel.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>Checks real category faces retain soft gradients and white reflections across selection and background changes.</summary>
        /// <returns>The Setting/List face, split-background, reflection and coverage regression.</returns>
        [UnityTest]
        [Category("GPU")]
        public IEnumerator NativeCategories_FaceGradientsAndReflectionsSurviveSelectionAndCaptures()
        {
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            UiOutlineTheme.Configure(UiOutlineMode.All);
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            const int size = 512;
            var target = _objects.Track(new RenderTexture(size, size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var resolved = _objects.Track(new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var reader = _objects.Track(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
            Assert.That(target.Create() && resolved.Create(), Is.True);
            var capture = _objects.Track(new Texture2D(64, 1, TextureFormat.RGBA32, false, true)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            });
            var sourcePixels = new Color[capture.width];
            var captures = new[] { Color.white, new Color(0.03f, 0.03f, 0.03f, 1f),
                new Color(0.02f, 0.05f, 1f, 1f), new Color(1f, 0.9f, 0.05f, 1f), Color.green, Color.red };
            foreach (var setting in new[] { true, false })
            {
                var panel = CreateAuthoredCategory(canvas, setting, selected: false);
                var image = panel.GetComponent<QualityUiImage>();
                var selected = CreateAuthoredCategory(canvas, setting, selected: true).GetComponent<QualityUiImage>();
                var unselectedColor = image.color;
                var unselectedSize = image.rectTransform.rect.size;
                var selectedColor = selected.color;
                var selectedSize = selected.rectTransform.rect.size;
                selected.gameObject.SetActive(false);
                foreach (var amount in new[] { 0f, 0.5f, 1f })
                {
                    image.color = Color.Lerp(unselectedColor, selectedColor, amount);
                    image.rectTransform.sizeDelta = Vector2.Lerp(unselectedSize, selectedSize, amount);
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    Assert.That(panel.RefractionReady, Is.True);
                    var material = _objects.Track(new Material(image.materialForRendering));
                    material.SetInt("_StencilComp", (int)CompareFunction.Always);
                    material.SetFloat("_GlassRefraction", 0f);
                    var strength = material.GetFloat("_GlassHighlight");
                    var mesh = BuildMesh(image);
                    var halfHeight = image.rectTransform.rect.height * size / 512f;
                    var flatReferences = new List<Color[]>(2);
                    var litReferences = new List<Color[]>(2);
                    var whiteBackgroundReflection = new float[2, 2];
                    for (var captureIndex = 0; captureIndex <= captures.Length; captureIndex++)
                    {
                        var split = captureIndex == captures.Length;
                        for (var x = 0; x < sourcePixels.Length; x++)
                        {
                            sourcePixels[x] = split ? captures[x < capture.width / 2 ? 0 : 1] : captures[captureIndex];
                        }
                        capture.SetPixels(sourcePixels);
                        capture.Apply(false, false);
                        SetCapture(capture);
                        Shader.SetGlobalVector("_ScreenParams", new Vector4(size, size, 1f + 1f / size, 1f + 1f / size));
                        material.SetFloat("_GlassHighlight", 0f);
                        var flat = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
                        material.SetFloat("_GlassHighlight", strength);
                        var lit = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
                        AssertAlphaEqual(flat, lit, "Face lighting must preserve opacity and the animated silhouette.");
                        Assert.That(MaximumPixelDifference(flat, lit, 3), Is.GreaterThan(0.003f), "Every capture must retain finite, visible glass lighting.");
                        if (captureIndex < 2)
                        {
                            flatReferences.Add(flat);
                            litReferences.Add(lit);
                        }
                        var center = Vector2.one * (size * 0.5f);
                        if (split)
                        {
                            var left = AverageInteriorRgb(flat, size, center + Vector2.left * 24f, 2, 1);
                            var right = AverageInteriorRgb(flat, size, center + Vector2.right * 24f, 2, 1);
                            Assert.That(Mathf.Abs(left.r - right.r), Is.GreaterThan(0.1f), "The same category must visibly span both bright and dark background halves.");
                        }
                        foreach (var half in new[] { 0, 1 })
                        {
                            var x = size * 0.5f + (half == 0 ? -16f : 16f);
                            var face = new Color[3];
                            var flatFace = new Color[3];
                            for (var band = 0; band < face.Length; band++)
                            {
                                var point = new Vector2(x, size * 0.5f + (band - 1) * halfHeight * 0.65f);
                                face[band] = AverageInteriorRgb(lit, size, point, 4, 1);
                                flatFace[band] = AverageInteriorRgb(flat, size, point, 4, 1);
                                if (split)
                                {
                                    var expectedFlat = AverageInteriorRgb(flatReferences[half], size, point, 4, 1);
                                    var expectedLit = AverageInteriorRgb(litReferences[half], size, point, 4, 1);
                                    Assert.That(Vector4.Distance(face[band], expectedLit), Is.LessThan(0.002f), "A background boundary must not change the authored face gradient.");
                                    Assert.That(Vector4.Distance(flatFace[band], expectedFlat), Is.LessThan(0.002f));
                                }
                            }
                            if (captureIndex == 0)
                            {
                                Assert.That(face[2].grayscale, Is.GreaterThan(face[1].grayscale + 0.003f), "The category needs a soft upper face highlight.");
                                var centerAbsorption = (flatFace[1].grayscale - face[1].grayscale) / flatFace[1].grayscale;
                                Assert.That(centerAbsorption, Is.InRange(0.03f, 0.08f), "The middle face needs restrained relative absorption throughout its tint transition.");
                                if (amount == 1f)
                                {
                                    Assert.That(face[1].grayscale, Is.GreaterThan(face[0].grayscale + 0.001f), "The dark selected face should shade downward, independently of its lower white contour.");
                                }
                            }
                            foreach (var side in new[] { 0, 1 })
                            {
                                var sign = side == 0 ? -1f : 1f;
                                var point = new Vector2(x, size * 0.5f + sign * (halfHeight - 2f));
                                var edge = AverageInteriorRgb(lit, size, point, 4, 0);
                                var flatEdge = AverageInteriorRgb(flat, size, point, 4, 0);
                                var reflection = edge.grayscale - flatEdge.grayscale;
                                if (captureIndex == 0)
                                {
                                    whiteBackgroundReflection[half, side] = reflection;
                                    var faceReflection = face[side == 0 ? 0 : 2].grayscale - flatFace[side == 0 ? 0 : 2].grayscale;
                                    Assert.That(reflection, Is.GreaterThan(faceReflection + 0.003f), "Both contours need a white reflection distinct from the face gradient.");
                                }
                                else if (captureIndex == 1)
                                {
                                    Assert.That(reflection, Is.GreaterThan(whiteBackgroundReflection[half, side] + 0.005f), "White reflections must become more visible naturally over a dark captured background.");
                                }
                                if (split)
                                {
                                    var expected = AverageInteriorRgb(litReferences[half], size, point, 4, 0);
                                    Assert.That(Vector4.Distance(edge, expected), Is.LessThan(0.002f), "The same contour must retain its lighting on both sides of a background boundary.");
                                }
                            }
                            TestContext.WriteLine($"CATEGORY_FACE setting={setting} selected={amount:F1} capture={captureIndex} half={half} bottom={face[0].grayscale:F5} middle={face[1].grayscale:F5} top={face[2].grayscale:F5}");
                        }
                        if (captureIndex < 2 || split)
                        {
                            WriteWhiteEdgePreview($"CategoryFace-{(setting ? "Setting" : "List")}-{Mathf.RoundToInt(amount * 100)}-{captureIndex}", flat, lit, size);
                        }
                    }
                }
                panel.gameObject.SetActive(false);
            }
        }

        /// <summary>Checks a real white capsule has a continuous softly shaded face with upper and lower white reflections.</summary>
        /// <returns>The production face-gradient, neutral-color, size, pixel-density, multisampling and coverage regression.</returns>
        [UnityTest]
        [Category("GPU")]
        public IEnumerator NativeWhiteGlass_FaceGradientAndReflectionsPreserveCoverage()
        {
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            UiOutlineTheme.Configure(UiOutlineMode.All);
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            var panel = CreateAuthoredCategory(canvas, setting: true, selected: false);
            var image = panel.GetComponent<QualityUiImage>();
            image.color = Color.white;
            const int size = 512;
            var resolved = _objects.Track(new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var reader = _objects.Track(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
            Assert.That(resolved.Create(), Is.True);
            foreach (var extent in new[] { image.rectTransform.rect.size, new Vector2(180f, 52f) })
            {
                image.rectTransform.sizeDelta = extent;
                yield return null;
                Canvas.ForceUpdateCanvases();
                Assert.That(panel.RefractionReady, Is.True);
                var material = _objects.Track(new Material(image.materialForRendering));
                material.SetInt("_StencilComp", (int)CompareFunction.Always);
                material.SetFloat("_GlassRefraction", 0f);
                var mesh = BuildMesh(image);
                var strength = material.GetFloat("_GlassHighlight");
                foreach (var samples in new[] { 1, 4 })
                {
                    var target = _objects.Track(new RenderTexture(size, size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
                    {
                        antiAliasing = samples
                    });
                    Assert.That(SystemInfo.GetRenderTextureSupportedMSAASampleCount(target.descriptor), Is.EqualTo(samples));
                    Assert.That(target.Create(), Is.True);
                    foreach (var pixelsPerUnit in new[] { 1, 2 })
                    {
                        SetCapture(Texture2D.whiteTexture);
                        Shader.SetGlobalVector("_ScreenParams", new Vector4(size, size, 1f + 1f / size, 1f + 1f / size));
                        material.SetFloat("_GlassHighlight", 0f);
                        var flat = Draw(image, material, mesh, target, resolved, reader, samples, largeBoard: pixelsPerUnit == 1, axisAligned: true);
                        material.SetFloat("_GlassHighlight", strength);
                        var lit = Draw(image, material, mesh, target, resolved, reader, samples, largeBoard: pixelsPerUnit == 1, axisAligned: true);
                        AssertAlphaEqual(flat, lit, "Face lighting must preserve the native capsule silhouette and opacity.");
                        var center = Vector2.one * (size * 0.5f);
                        var flatCenter = AverageInteriorRgb(flat, size, center, 8, 1);
                        // Native UGUI's fast Gamma-to-Linear approximation maps white slightly below one.
                        Assert.That(flatCenter.r, Is.EqualTo(1f).Within(0.005f), "The disabled-lighting reference must remain white.");
                        var halfHeight = image.rectTransform.rect.height * pixelsPerUnit * 0.5f;
                        var face = new Color[3];
                        for (var band = 0; band < face.Length; band++)
                        {
                            var point = center + Vector2.up * ((band - 1) * halfHeight * 0.65f);
                            face[band] = AverageInteriorRgb(lit, size, point, 8, 1);
                            Assert.That(face[band].r, Is.EqualTo(face[band].g).Within(0.001f));
                            Assert.That(face[band].r, Is.EqualTo(face[band].b).Within(0.001f));
                        }
                        var centerDrop = flatCenter.r - face[1].r;
                        Assert.That(centerDrop, Is.InRange(0.025f, 0.06f), "A pure white face needs a restrained middle shade rather than a flat clear center.");
                        Assert.That(face[2].r, Is.GreaterThan(face[1].r + 0.004f), "The upper white face must brighten softly toward the contour.");
                        Assert.That(face[0].r, Is.GreaterThan(face[1].r + 0.004f), "The lower white face must also retain a soft reflection.");
                        // Inspect the full face rather than accepting a single bright edge pixel as a gradient.
                        var firstY = Mathf.CeilToInt(center.y - halfHeight * 0.72f);
                        var lastY = Mathf.FloorToInt(center.y + halfHeight * 0.72f);
                        var previous = AverageInteriorRgb(lit, size, new Vector2(center.x, firstY), 8, 0).r;
                        var maximumStep = 0f;
                        for (var y = firstY + 1; y <= lastY; y++)
                        {
                            var current = AverageInteriorRgb(lit, size, new Vector2(center.x, y), 8, 0).r;
                            maximumStep = Mathf.Max(maximumStep, Mathf.Abs(current - previous));
                            previous = current;
                        }
                        Assert.That(maximumStep, Is.LessThan(0.006f), "The face must transition continuously without a hard internal boundary.");
                        foreach (var side in new[] { -1f, 1f })
                        {
                            var point = center + Vector2.up * (side * (halfHeight - 2f));
                            var edge = AverageInteriorRgb(lit, size, point, 8, 0);
                            Assert.That(edge.r, Is.GreaterThan(face[1].r + 0.01f), "Both contours must retain a white reflection above the middle shade.");
                            Assert.That(edge.r, Is.LessThanOrEqualTo(flatCenter.r + 0.002f), "White reflections must remain within the existing color headroom.");
                        }
                        TestContext.WriteLine($"WHITE_CAPSULE_FACE extent={extent} samples={samples} pixelsPerUnit={pixelsPerUnit} bottom={face[0].r:F5} middle={face[1].r:F5} top={face[2].r:F5} shade={centerDrop:F5} maximumStep={maximumStep:F5}");
                        WriteWhiteEdgePreview($"NativeWhiteFace-{Mathf.RoundToInt(extent.x)}x{Mathf.RoundToInt(extent.y)}-{samples}x-{pixelsPerUnit}px", flat, lit, size);
                    }
                }
            }
        }

        /// <summary>Checks the actual adjustment controls use category-sized lenses without changing their capture role or Gaussian output.</summary>
        /// <returns>The authored plus/minus material and GPU comparison.</returns>
        [UnityTest]
        [Category("GPU")]
        public IEnumerator SettingAdjustments_MatchCategoryRefractionAndPreserveGaussianCoverage()
        {
            LoadAtlas();
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            UiOutlineTheme.Configure(UiOutlineMode.All);
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            var selected = CreateAuthoredCategory(canvas, setting: false, selected: true);
            yield return null;
            Canvas.ForceUpdateCanvases();
            var categoryWidth = selected.GetComponent<QualityUiImage>().material.GetVector("_LiquidGlassOptics").x;
            selected.gameObject.SetActive(false);
            var capture = CreateProbeCapture(128, false);
            const int size = 512;
            var target = _objects.Track(new RenderTexture(size, size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var resolved = _objects.Track(new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var reader = _objects.Track(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
            Assert.That(target.Create() && resolved.Create(), Is.True);
            foreach (var spriteName in new[] { "newui@3x_17", "newui@3x_18" })
            {
                UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
                var panel = CreateAuthoredPanel(canvas, spriteName);
                var image = panel.GetComponent<QualityUiImage>();
                yield return null;
                Canvas.ForceUpdateCanvases();
                Assert.That(panel.IsButton, Is.False);
                Assert.That(panel.SourceRole, Is.EqualTo(UiBackdropSourceRole.Peripheral));
                Assert.That(panel.RefractionReady, Is.True);
                var material = _objects.Track(new Material(image.materialForRendering));
                Assert.That(material.GetFloat("_BackdropIsPanel"), Is.Zero, "Adjustment controls cannot inherit the large-plate recipe.");
                material.SetInt("_StencilComp", (int)CompareFunction.Always);
                var optics = material.GetVector("_LiquidGlassOptics");
                Assert.That(optics.x, Is.InRange(categoryWidth * 0.95f, categoryWidth * 1.05f),
                    "Adjustment lenses remain comparable to the category while retaining their independently authored body height.");
                Assert.That(material.GetFloat("_LiquidRefractionTail"), Is.Zero, "Sharing RT3 cannot opt adjustment capsules into the outer-key lens tail.");
                var previousOptics = optics;
                previousOptics.x /= 0.75f;
                previousOptics.y /= 0.75f;
                var mesh = BuildMesh(image);
                SetCapture(capture);
                var tuned = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
                material.SetVector("_LiquidGlassOptics", previousOptics);
                var previous = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
                var difference = MaximumPixelDifference(tuned, previous, 3);
                Assert.That(difference, Is.GreaterThan(0.001f), "The smaller lens must actually change displaced background pixels.");
                AssertAlphaEqual(tuned, previous, "Lens tuning cannot change the authored silhouette or opacity.");
                material.DisableKeyword(UiBackdropPanel.LiquidStyleKeyword);
                var gaussianPrevious = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
                material.SetVector("_LiquidGlassOptics", optics);
                var gaussianTuned = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
                Assert.That(MaximumPixelDifference(gaussianPrevious, gaussianTuned, 4), Is.Zero, "The liquid-only tuning must not alter Gaussian appearance.");
                TestContext.WriteLine($"ADJUSTMENT_LENS sprite={spriteName} band={optics.x:F6} categoryBand={categoryWidth:F6} changedRGB={difference:F8}");
                panel.gameObject.SetActive(false);
            }
        }

        /// <summary>Checks the shared RT2/RT3 capture passes encode geometric coverage without classifying source RGB.</summary>
        [Test]
        [Category("GPU")]
        public void ControlCapture_CopyAndDownsampleEncodeCircleCoverage()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null));
            var shader = Shader.Find("Hidden/Maj/UI PV Gaussian");
            Assert.That(shader, Is.Not.Null);
            var material = _objects.Track(new Material(shader));
            var source = _objects.Track(new Texture2D(1, 1, TextureFormat.RGBA32, false, true)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            });
            const int size = 64;
            var target = _objects.Track(new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var reader = _objects.Track(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
            Assert.That(target.Create(), Is.True);
            var savedX = Shader.GetGlobalVector("_MajUiCaptureClipUvX");
            var savedY = Shader.GetGlobalVector("_MajUiCaptureClipUvY");
            var savedStep = Shader.GetGlobalVector("_MajPvDownsampleStep");
            var savedManual = Shader.GetGlobalFloat("_MajUiSourceNeedsManualLinear");
            try
            {
                Shader.SetGlobalFloat("_MajUiSourceNeedsManualLinear", 0f);
                Shader.SetGlobalVector("_MajPvDownsampleStep", new Vector4(1f / size, 1f / size, 0f, 0f));
                Shader.SetGlobalVector("_MajUiCaptureClipUvY", new Vector4(0f, 6.25f, -0.69f * 6.25f, 0f));
                foreach (var pass in new[] { 3, 0 })
                {
                    foreach (var enabled in new[] { true, false })
                    {
                        source.SetPixel(0, 0, new Color(0.2f, 0.65f, 0.4f, enabled ? 1f : 0.37f));
                        source.Apply(false, false);
                        var expected = source.GetPixel(0, 0);
                        Shader.SetGlobalVector("_MajUiCaptureClipUvX", new Vector4(6.25f, 0f, -0.31f * 6.25f, enabled ? 1f : 0f));
                        Graphics.Blit(source, target, material, pass);
                        RenderTexture.active = target;
                        reader.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                        reader.Apply(false, false);
                        Assert.That(Vector4.Distance(reader.GetPixel(20, 44), expected), Is.LessThan(0.002f),
                            "The off-center circle interior must retain source RGB and full valid coverage.");
                        foreach (var point in new[] { new Vector2Int(20, 19), new Vector2Int(50, 8) })
                        {
                            var outside = reader.GetPixel(point.x, point.y);
                            Assert.That(Vector4.Distance(outside, enabled ? Color.clear : expected), Is.LessThan(0.002f),
                                "Enabled capture must zero nonblack exterior RGBA; disabled capture must preserve original alpha.");
                        }
                    }
                }
            }
            finally
            {
                Shader.SetGlobalVector("_MajUiCaptureClipUvX", savedX);
                Shader.SetGlobalVector("_MajUiCaptureClipUvY", savedY);
                Shader.SetGlobalVector("_MajPvDownsampleStep", savedStep);
                Shader.SetGlobalFloat("_MajUiSourceNeedsManualLinear", savedManual);
            }
        }

        /// <summary>Checks category consumers retain their own circle when later button metadata changes, without clipping unrelated roles.</summary>
        /// <returns>The authored selected, procedural, value-plate, board and adjustment GPU regressions.</returns>
        [UnityTest]
        [Category("GPU")]
        public IEnumerator CategorySampling_UsesIndependentBoundsAndPreservesOtherRoles()
        {
            LoadAtlas();
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            UiOutlineTheme.Configure(UiOutlineMode.All);
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            var panels = new[]
            {
                CreateAuthoredCategory(canvas, setting: false, selected: true),
                CreateAuthoredCategory(canvas, setting: true, selected: false),
                CreateAuthoredPanel(canvas, "newui@3x_19"),
                CreateAuthoredPanel(canvas, "newui@3x_22"),
                CreateAuthoredPanel(canvas, "newui@3x_17")
            };
            yield return null;
            Canvas.ForceUpdateCanvases();
            const int size = 512;
            var fill = new Color(0.15f, 0.8f, 0.35f, 1f);
            var spatial = CreateCircleCapture(128, new Vector2(0.1f, 0.5f), 0.2f, fill);
            var solid = _objects.Track(new Texture2D(1, 1, TextureFormat.RGBA32, false, true));
            solid.SetPixel(0, 0, fill);
            solid.Apply(false, false);
            var target = _objects.Track(new RenderTexture(size, size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var resolved = _objects.Track(new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var reader = _objects.Track(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
            Assert.That(target.Create() && resolved.Create(), Is.True);
            var saved = new System.Collections.Generic.Dictionary<string, Vector4>();
            foreach (var name in new[] { "_MajUiCategoryClipUvX", "_MajUiCategoryClipUvY", "_MajUiCategoryClipSampleBounds",
                "_MajUiButtonClipUvX", "_MajUiButtonClipUvY", "_MajUiButtonClipSampleBounds" })
            {
                saved.Add(name, Shader.GetGlobalVector(name));
            }
            try
            {
                foreach (var panel in panels)
                {
                    var image = panel.GetComponent<QualityUiImage>();
                    var material = _objects.Track(new Material(image.materialForRendering));
                    material.SetInt("_StencilComp", (int)CompareFunction.Always);
                    material.SetFloat("_GlassHighlight", 0f);
                    material.SetFloat("_GlassRefraction", 0.008f);
                    var mesh = BuildMesh(image);
                    foreach (var liquid in new[] { false, true })
                    {
                        if (liquid)
                        {
                            material.EnableKeyword(UiBackdropPanel.LiquidStyleKeyword);
                        }
                        else
                        {
                            material.DisableKeyword(UiBackdropPanel.LiquidStyleKeyword);
                        }
                        var bounds = new Vector4(0.1f, 0.5f, 1f - 2f / (spatial.width * 0.2f), 0f);
                        Shader.SetGlobalVector("_MajUiCategoryClipUvX", new Vector4(5f, 0f, -0.5f, 1f));
                        Shader.SetGlobalVector("_MajUiCategoryClipUvY", new Vector4(0f, 5f, -2.5f, 0f));
                        Shader.SetGlobalVector("_MajUiCategoryClipSampleBounds", bounds);
                        Shader.SetGlobalVector("_MajUiButtonClipUvX", new Vector4(10f, 0f, -9f, 1f));
                        Shader.SetGlobalVector("_MajUiButtonClipUvY", new Vector4(0f, 10f, -5f, 0f));
                        Shader.SetGlobalVector("_MajUiButtonClipSampleBounds", new Vector4(0.9f, 0.5f, 0.8f, 0f));
                        SetCapture(solid);
                        Shader.SetGlobalVector("_ScreenParams", new Vector4(size, size, 1f + 1f / size, 1f + 1f / size));
                        var validReference = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
                        SetCapture(spatial);
                        Shader.SetGlobalVector("_ScreenParams", new Vector4(size, size, 1f + 1f / size, 1f + 1f / size));
                        var bounded = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
                        var disabledBounds = bounds;
                        disabledBounds.z = 1000f;
                        Shader.SetGlobalVector("_MajUiCategoryClipSampleBounds", disabledBounds);
                        Shader.SetGlobalVector("_MajUiButtonClipUvX", Vector4.zero);
                        var unbounded = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
                        var category = panel.SourceRole == UiBackdropSourceRole.Category;
                        Assert.That(MaximumPixelDifference(bounded, category ? validReference : unbounded, 4), Is.LessThan(0.005f),
                            "RT2 consumers must use their own valid circle; Board and RT3 adjustments must ignore both circle mappings.");
                        if (category)
                        {
                            Assert.That(MaximumPixelDifference(bounded, unbounded, 3), Is.GreaterThan(0.02f), "The spatial fixture must expose invalid samples without circle bounds.");
                            Shader.SetGlobalVector("_MajUiCategoryClipSampleBounds", bounds);
                            var afterButtons = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
                            Assert.That(MaximumPixelDifference(bounded, afterButtons, 4), Is.Zero, "Later RT3 metadata changes cannot move RT2 sampling bounds.");
                        }
                    }
                    panel.gameObject.SetActive(false);
                }
            }
            finally
            {
                foreach (var pair in saved)
                {
                    Shader.SetGlobalVector(pair.Key, pair.Value);
                }
            }
        }

        /// <summary>Checks outer-key sampling stays inside the captured circle without changing valid black or shared native controls.</summary>
        /// <returns>The authored outer-key and native adjustment GPU regressions in both rendering styles.</returns>
        [UnityTest]
        [Category("GPU")]
        public IEnumerator PeripheralSampling_ClampsCaptureUvAndPreservesNativeControlsInBothStyles()
        {
            LoadAtlas();
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            UiOutlineTheme.Configure(UiOutlineMode.All);
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            var outer = CreateAuthoredPeripheral(canvas);
            var adjustment = CreateAuthoredPanel(canvas, "newui@3x_17");
            yield return null;
            Canvas.ForceUpdateCanvases();
            const int size = 512;
            var capture = _objects.Track(new Texture2D(1, 1, TextureFormat.RGBA32, false, true));
            var target = _objects.Track(new RenderTexture(size, size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var resolved = _objects.Track(new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var reader = _objects.Track(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
            Assert.That(target.Create() && resolved.Create(), Is.True);
            var savedX = Shader.GetGlobalVector("_MajUiButtonClipUvX");
            var savedY = Shader.GetGlobalVector("_MajUiButtonClipUvY");
            var savedBounds = Shader.GetGlobalVector("_MajUiButtonClipSampleBounds");
            var circleX = new Vector4(4f, 0f, -1f, 1f);
            var circleY = new Vector4(0f, 4f, -2f, 0f);
            try
            {
                foreach (var panel in new[] { outer, adjustment })
                {
                    var image = panel.GetComponent<QualityUiImage>();
                    var material = _objects.Track(new Material(image.materialForRendering));
                    material.SetInt("_StencilComp", (int)CompareFunction.Always);
                    material.SetFloat("_GlassHighlight", 0f);
                    material.SetFloat("_GlassRefraction", 0f);
                    material.SetFloat("_LiquidArtworkBlend", 0f);
                    material.SetFloat("_LiquidTintStrength", 0f);
                    material.SetFloat("_GaussianButtonTintStrength", 0f);
                    Assert.That(material.GetFloat("_BackdropIsNativeControl"), Is.EqualTo(panel == outer ? 0f : 1f));
                    var mesh = BuildMesh(image);
                    foreach (var liquid in new[] { false, true })
                    {
                        if (liquid)
                        {
                            material.EnableKeyword(UiBackdropPanel.LiquidStyleKeyword);
                        }
                        else
                        {
                            material.DisableKeyword(UiBackdropPanel.LiquidStyleKeyword);
                        }
                        foreach (var capturedColor in new[] { Color.black, new Color(0.15f, 0.8f, 0.35f, 1f) })
                        {
                            capture.SetPixel(0, 0, capturedColor);
                            capture.Apply(false, false);
                            SetCapture(capture);
                            Shader.SetGlobalVector("_MajUiButtonClipSampleBounds", new Vector4(0.25f, 0.5f, 0.98f, 0f));
                            Shader.SetGlobalVector("_ScreenParams", new Vector4(size, size, 1f + 1f / size, 1f + 1f / size));
                            Shader.SetGlobalVector("_MajUiButtonClipUvX", Vector4.zero);
                            var baseline = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
                            Shader.SetGlobalVector("_MajUiButtonClipUvY", circleY);
                            Shader.SetGlobalVector("_MajUiButtonClipUvX", circleX);
                            var guarded = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
                            AssertAlphaEqual(baseline, guarded, "Constraining sampled UVs cannot alter authored Sprite coverage.");
                            Assert.That(MaximumPixelDifference(guarded, baseline, 4), Is.LessThan(0.002f),
                                "A constant valid capture, including real black, must not become original artwork outside the circle.");
                            if (panel == adjustment)
                            {
                                continue;
                            }
                            var inside = size / 2 * size + size / 2 - 10;
                            Assert.That(Vector4.Distance(guarded[inside], capture.GetPixel(0, 0)), Is.LessThan(0.01f),
                                "The covered button body must retain the actual captured color rather than outputting transparent or artwork-only pixels.");
                            capture.SetPixel(0, 0, capturedColor * 0.25f);
                            capture.Apply(false, false);
                            var filtered = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
                            Assert.That(Vector4.Distance(filtered[inside], guarded[inside]), Is.LessThan(0.015f),
                                "Filtered RGBA must restore valid RGB without a dark fringe from partial circle coverage.");
                        }
                        if (liquid)
                        {
                            // The screen edge crosses the left lens, whose inward refraction can leave RT3's valid circle.
                            const int captureSize = 128;
                            var fill = new Color(0.15f, 0.8f, 0.35f, 1f);
                            var spatial = CreateCircleCapture(captureSize, new Vector2(0.1f, 0.5f), 0.1f, fill);
                            capture.SetPixel(0, 0, fill);
                            capture.Apply(false, false);
                            SetCapture(capture);
                            Shader.SetGlobalVector("_ScreenParams", new Vector4(size, size, 1f + 1f / size, 1f + 1f / size));
                            Shader.SetGlobalVector("_MajUiButtonClipUvX", new Vector4(10f, 0f, -1f, 1f));
                            Shader.SetGlobalVector("_MajUiButtonClipUvY", new Vector4(0f, 10f, -5f, 0f));
                            var bounds = new Vector4(0.1f, 0.5f, 1f - 2f / (captureSize * 0.1f), 0f);
                            Shader.SetGlobalVector("_MajUiButtonClipSampleBounds", bounds);
                            material.SetFloat("_GlassRefraction", 0.008f);
                            var validReference = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
                            SetCapture(spatial);
                            Shader.SetGlobalVector("_ScreenParams", new Vector4(size, size, 1f + 1f / size, 1f + 1f / size));
                            var bounded = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
                            bounds.z = 1000f;
                            Shader.SetGlobalVector("_MajUiButtonClipSampleBounds", bounds);
                            var unbounded = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
                            Assert.That(MaximumPixelDifference(bounded, panel == outer ? validReference : unbounded, 4), Is.LessThan(0.005f),
                                "Only outer keys constrain UVs; valid interior color must match a fully valid capture without black or artwork patches.");
                            if (panel == outer)
                            {
                                var crossingDifference = 0f;
                                var coveredPixels = 0;
                                for (var x = Mathf.FloorToInt(size * 0.2f) - 16; x <= Mathf.FloorToInt(size * 0.2f) - 3; x++)
                                {
                                    var index = size / 2 * size + x;
                                    if (validReference[index].a < 0.9f)
                                    {
                                        continue;
                                    }
                                    coveredPixels++;
                                    var difference = unbounded[index] - validReference[index];
                                    crossingDifference = Mathf.Max(crossingDifference,
                                        Mathf.Max(Mathf.Abs(difference.r), Mathf.Max(Mathf.Abs(difference.g), Mathf.Abs(difference.b))));
                                }
                                Assert.That(coveredPixels, Is.GreaterThan(3), "The negative control must inspect covered button-body pixels, not transparent edges.");
                                Assert.That(crossingDifference, Is.GreaterThan(0.02f), "The negative control must really refract valid screen pixels beyond the captured circle.");
                            }
                        }
                    }
                    panel.gameObject.SetActive(false);
                }
            }
            finally
            {
                Shader.SetGlobalVector("_MajUiButtonClipUvX", savedX);
                Shader.SetGlobalVector("_MajUiButtonClipUvY", savedY);
                Shader.SetGlobalVector("_MajUiButtonClipSampleBounds", savedBounds);
            }
        }

        /// <summary>Checks the physical outer key softens a thin board shadow already present in RT3.</summary>
        /// <returns>The half-resolution, real-outline transmission regression.</returns>
        [UnityTest]
        [Category("GPU")]
        public IEnumerator PeripheralLens_SoftensTransmittedBoardEdgeWithoutChangingCoverage()
        {
            const int size = 512;
            const int captureSize = size / 2;
            const string captureInvSize = "_MajUiButtonCaptureInvSize";
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            UiOutlineTheme.Configure(UiOutlineMode.All);
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            var panel = CreateSerializedSurface(canvas, "Assets/Prefabs/UI/Buttons.prefab", 5208406972733417819L);
            var image = panel.GetComponent<QualityUiImage>();
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.That(panel.RefractionReady && panel.IsButton, Is.True);
            var material = _objects.Track(new Material(image.materialForRendering));
            material.SetInt("_StencilComp", (int)CompareFunction.Always);
            var target = _objects.Track(new RenderTexture(size, size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var resolved = _objects.Track(new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var reader = _objects.Track(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
            Assert.That(target.Create() && resolved.Create(), Is.True);
            var capture = _objects.Track(new Texture2D(captureSize, captureSize, TextureFormat.RGBA32, false, true)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            });
            var pixels = new Color[captureSize * captureSize];
            var flatFill = new Color(0.86f, 0.84f, 0.80f, 1f);
            var shadow = new Color(0.36f, 0.34f, 0.30f, 1f);
            for (var y = 0; y < captureSize; y++)
            {
                for (var x = 0; x < captureSize; x++)
                {
                    // The white plate's thin shadow runs continuously through the key body.
                    var curve = captureSize / 2 + (y - captureSize / 2) * (y - captureSize / 2) / 170;
                    pixels[y * captureSize + x] = x == curve ? shadow : flatFill;
                }
            }
            capture.SetPixels(pixels);
            capture.Apply(false, false);
            SetCapture(capture);
            Shader.SetGlobalVector("_ScreenParams", new Vector4(size, size, 1f + 1f / size, 1f + 1f / size));
            var mesh = BuildMesh(image);
            Shader.SetGlobalVector(captureInvSize, Vector4.zero);
            var sharp = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard: true);
            Shader.SetGlobalVector(captureInvSize, new Vector4(1f / captureSize, 1f / captureSize, 0f, 0f));
            var filtered = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard: true);
            AssertAlphaEqual(sharp, filtered, "Softening the captured board edge cannot modify the button's artwork coverage.");
            var row = size / 2 * size;
            var peakRecovery = 0f;
            for (var x = size / 2 - 8; x < size / 2 + 12; x++)
            {
                peakRecovery = Mathf.Max(peakRecovery, filtered[row + x].grayscale - sharp[row + x].grayscale);
            }
            Assert.That(peakRecovery, Is.GreaterThan(0.06f), "RT3's narrow shadow should visibly lose contrast in the key.");
            Assert.That(filtered[row + size / 2 - 20], Is.EqualTo(sharp[row + size / 2 - 20]),
                "A uniform surrounding transmission must retain its authored color.");
            var lineCenter = row + size / 2 + 1;
            Assert.That(filtered[lineCenter].grayscale,
                Is.LessThanOrEqualTo(Mathf.Min(filtered[lineCenter - 2].grayscale, filtered[lineCenter + 2].grayscale) + 0.001f),
                "Overlapping bilinear footprints must not split the shadow into two darker rails.");
            material.DisableKeyword(UiBackdropPanel.LiquidStyleKeyword);
            var gaussian = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard: true);
            Shader.SetGlobalVector(captureInvSize, Vector4.zero);
            var gaussianWithoutFilter = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard: true);
            Assert.That(MaximumPixelDifference(gaussian, gaussianWithoutFilter, 4), Is.Zero,
                "Gaussian keys must not apply the extra liquid transmission filter.");
            WriteWhiteEdgePreview("PeripheralBoardShadow", sharp, filtered, size);
        }

        /// <summary>Checks a real peripheral body refracts beyond its reflection band while retaining center, color and fallback behavior.</summary>
        /// <returns>The actual authored peripheral profile and unchanged-lighting GPU regression.</returns>
        [UnityTest]
        [Category("GPU")]
        public IEnumerator PeripheralTail_RealButtonContinuesThroughBodyWithoutChangingFlatSourceLighting()
        {
            var atlas = LoadAtlas();
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            UiOutlineTheme.Configure(UiOutlineMode.All);
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            var panel = CreateAuthoredPeripheral(canvas);
            var image = panel.GetComponent<QualityUiImage>();
            var mask = _objects.Create("Actual peripheral stencil parent", canvas.transform);
            mask.AddComponent<Image>();
            mask.AddComponent<Mask>().showMaskGraphic = false;
            image.transform.SetParent(mask.transform, false);
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.That(panel.IsButton, Is.True);
            Assert.That(image.material.GetFloat("_LiquidRefractionTail"), Is.EqualTo(0.22f));
            var native = image.canvasRenderer.GetMaterial(0);
            Assert.That(native, Is.Not.SameAs(image.material));
            Assert.That(native.GetFloat("_LiquidRefractionTail"), Is.EqualTo(0.22f));
            Assert.That(native.GetVector("_LiquidGlassOptics"), Is.EqualTo(image.material.GetVector("_LiquidGlassOptics")));
            var material = _objects.Track(new Material(native));
            material.SetInt("_StencilComp", (int)CompareFunction.Always);
            Assert.That(material.GetFloat("_LiquidRefractionReady"), Is.EqualTo(1f));
            var profile = atlas.ProfileTexture!;
            var values = ReadFloatTexture(profile);
            var zeroData = new ushort[profile.width * 2];
            for (var index = 0; index < profile.width; index++)
            {
                zeroData[index * 2 + 1] = Mathf.FloatToHalf(values[index].g);
            }
            var zeroDisplacement = _objects.Track(new Texture2D(profile.width, 1, TextureFormat.RGHalf, false, true)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            });
            zeroDisplacement.SetPixelData(zeroData, 0);
            zeroDisplacement.Apply(false, true);
            const int size = 512;
            var capture = _objects.Track(new Texture2D(size, size, TextureFormat.RGBA32, false, true));
            var probe = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var u = (x + 0.5f) / size;
                    var v = (y + 0.5f) / size;
                    probe[y * size + x] = new Color(0.5f + 0.45f * Mathf.Sin(u * Mathf.PI * 20f),
                        0.5f + 0.45f * Mathf.Sin(v * Mathf.PI * 16f), 0.5f + 0.3f * Mathf.Sin((u + v) * Mathf.PI * 12f), 1f);
                }
            }
            capture.SetPixels(probe);
            capture.Apply(false, false);
            capture.filterMode = FilterMode.Bilinear;
            capture.wrapMode = TextureWrapMode.Clamp;
            SetCapture(capture);
            Shader.SetGlobalVector("_ScreenParams", new Vector4(size, size, 1f + 1f / size, 1f + 1f / size));
            var mesh = BuildMesh(image);
            var target = _objects.Track(new RenderTexture(size, size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var resolved = _objects.Track(new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var reader = _objects.Track(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
            Assert.That(target.Create() && resolved.Create(), Is.True);
            material.SetFloat("_LiquidRefractionTail", 0f);
            var oldPixels = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
            material.SetTexture("_LiquidProfileTex", zeroDisplacement);
            var zeroPixels = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
            material.SetTexture("_LiquidProfileTex", profile);
            material.SetFloat("_LiquidRefractionTail", 0.22f);
            var newPixels = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
            var radius = Mathf.Min(image.GetPixelAdjustedRect().width, image.GetPixelAdjustedRect().height) * 0.5f;
            var optics = material.GetVector("_LiquidGlassOptics");
            Assert.That(optics.x, Is.LessThan(radius * 0.45f));
            var middleDifference = 0f;
            var oldFlatDifference = 0f;
            var centerDifference = 0f;
            var centerIndex = -1;
            var alphaDifference = 0f;
            var middlePixels = 0;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var index = y * size + x;
                    var local = new Vector2((x + 0.5f) * 256f / size - 128f, (y + 0.5f) * 256f / size - 128f);
                    alphaDifference = Mathf.Max(alphaDifference, Mathf.Abs(newPixels[index].a - oldPixels[index].a));
                    if (local.magnitude >= radius * 0.25f && local.magnitude <= radius * 0.55f && oldPixels[index].a > 0.99f)
                    {
                        for (var channel = 0; channel < 3; channel++)
                        {
                            middleDifference = Mathf.Max(middleDifference, Mathf.Abs(newPixels[index][channel] - oldPixels[index][channel]));
                            oldFlatDifference = Mathf.Max(oldFlatDifference, Mathf.Abs(oldPixels[index][channel] - zeroPixels[index][channel]));
                        }
                        middlePixels++;
                    }
                    if (local.magnitude <= radius * 0.02f)
                    {
                        for (var channel = 0; channel < 3; channel++)
                        {
                            var difference = Mathf.Abs(newPixels[index][channel] - oldPixels[index][channel]);
                            if (difference > centerDifference)
                            {
                                centerDifference = difference;
                                centerIndex = index;
                            }
                        }
                    }
                }
            }
            Assert.That(middlePixels, Is.GreaterThan(100));
            Assert.That(oldFlatDifference, Is.LessThanOrEqualTo(0.000001f),
                "The middle-region reference must be outside the old displacement band; its unchanged G channel retains reflection and shading.");
            Assert.That(middleDifference, Is.GreaterThan(0.001f), "The actual authored button must retain visible refraction in its previously flat middle.");
            Assert.That(alphaDifference, Is.Zero, "Extending displacement must not change original Sprite coverage.");
            var solid = _objects.Track(new Texture2D(1, 1, TextureFormat.RGBA32, false, true));
            solid.SetPixel(0, 0, new Color(0.28f, 0.47f, 0.71f, 1f));
            solid.Apply(false, false);
            SetCapture(solid);
            material.SetFloat("_LiquidRefractionTail", 0f);
            var oldSolid = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
            material.SetFloat("_LiquidRefractionTail", 0.22f);
            var newSolid = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
            var flatDifference = 0f;
            for (var index = 0; index < oldSolid.Length; index++)
            {
                for (var channel = 0; channel < 4; channel++)
                {
                    flatDifference = Mathf.Max(flatDifference, Mathf.Abs(oldSolid[index][channel] - newSolid[index][channel]));
                }
            }
            Assert.That(flatDifference, Is.Zero, "An unchanged flat capture must retain the exact original tint, inner shading, highlight and alpha.");
            SetCapture(capture);
            material.SetFloat("_LiquidRefractionReady", 0f);
            material.SetFloat("_LiquidRefractionTail", 0f);
            var oldFallback = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
            material.SetFloat("_LiquidRefractionTail", 0.22f);
            var newFallback = Draw(image, material, mesh, target, resolved, reader, 1, axisAligned: true);
            var fallbackDifference = 0f;
            for (var index = 0; index < oldFallback.Length; index++)
            {
                for (var channel = 0; channel < 4; channel++)
                {
                    fallbackDifference = Mathf.Max(fallbackDifference, Mathf.Abs(oldFallback[index][channel] - newFallback[index][channel]));
                }
            }
            Assert.That(fallbackDifference, Is.Zero, "The tail cannot bypass missing-data refraction readiness.");
            TestContext.WriteLine($"PERIPHERAL_TAIL_GPU sprite={image.sprite.name} width={optics.x:F6} radius={radius:F6} middlePixels={middlePixels} middleMax={middleDifference:F8} legacyFlat={oldFlatDifference:F8} centerMax={centerDifference:F8} centerPixel={centerIndex % size},{centerIndex / size} rect={material.GetVector("_LiquidGlassRect")} sourceRect={material.GetVector("_LiquidGlassSpriteRect")} shape={material.GetVector("_LiquidGlassShape")} alphaMax={alphaDifference:F8} flatSource={flatDifference:F8} fallback={fallbackDifference:F8}");
            Assert.That(centerDifference, Is.LessThan(0.001f), "The tail must settle smoothly to an unchanged center.");
        }

        /// <summary>Checks plates keep fifteen percent authored color and transmit eighty-five percent capture through a sixty-five percent tint.</summary>
        /// <returns>The authored base, reduced transmission tint, flat-center lighting and Gaussian-isolation regression.</returns>
        [UnityTest]
        [Category("GPU")]
        public IEnumerator LiquidPanels_FixedTransmissionAndEdgeOnlyLightingUseAuthoredBase()
        {
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            UiOutlineTheme.Configure(UiOutlineMode.All);
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            const int size = 1200;
            var target = _objects.Track(new RenderTexture(size, size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var resolved = _objects.Track(new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var reader = _objects.Track(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
            var capture = _objects.Track(new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true));
            Assert.That(target.Create() && resolved.Create(), Is.True);
            foreach (var source in new[]
            {
                (Name: "List", Path: "Assets/Prefabs/List/Main_Display_ribbon.prefab", Panel: 5019321880153334813L),
                (Name: "Login", Path: "Assets/Scenes/Login.unity", Panel: 900000000000000021L),
                (Name: "InheritedSetting", Path: "Assets/Scenes/Setting.unity", Panel: 900000000000000016L),
                (Name: "UserInfo", Path: "Assets/Prefabs/UI/UserInfo.prefab", Panel: 4988950831640157352L),
                (Name: "PracticeChart", Path: "Assets/Scenes/Practice.unity", Panel: 900000000000000022L)
            })
            {
                var panel = CreateSerializedSurface(canvas, source.Path, source.Panel);
                var image = panel.GetComponent<QualityUiImage>();
                if (source.Name == "List")
                {
                    image.color = new Color(0.8f, 0.9f, 1f, image.color.a);
                }
                yield return null;
                Canvas.ForceUpdateCanvases();
                Assert.That(panel.RefractionReady, Is.True);
                var material = _objects.Track(new Material(image.materialForRendering));
                Assert.That(material.GetFloat("_BackdropIsPanel"), Is.EqualTo(1f), source.Name + " must use plate treatment.");
                material.SetInt("_StencilComp", (int)CompareFunction.Always);
                material.SetFloat("_GlassRefraction", 0f);
                var strength = material.GetFloat("_GlassHighlight");
                material.SetFloat("_GlassHighlight", 0f);
                material.SetVector("_Color", new Vector4(0.9f, 0.85f, 0.8f, 1f));
                var mesh = BuildMesh(image);
                var graphic = (Color)mesh.colors32[0];
                if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                {
                    graphic = graphic.linear;
                }
                var uv = (Vector2)material.GetVector("_FillSampleUV");
                uv = Vector2.Scale(uv, material.GetTextureScale("_MainTex")) + material.GetTextureOffset("_MainTex");
                var fill = SampleWorkingTexture(image.mainTexture, uv);
                var sourceColor = material.GetVector("_LiquidTintSourceColor");
                var inherited = Color.white;
                if (sourceColor.w > 0f)
                {
                    var sourceFill = SampleWorkingTexture(material.GetTexture("_LiquidTintSourceTex"), (Vector2)material.GetVector("_LiquidTintSourceUV"));
                    inherited = Color.Lerp(Color.white, sourceFill * (Color)sourceColor, sourceColor.w);
                }
                var materialColor = (Color)material.GetVector("_Color");
                if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                {
                    // ShaderLab Color properties are converted on upload; the inherited tint is
                    // already a working-space Vector and must not receive this conversion again.
                    materialColor = materialColor.linear;
                }
                var baseColor = fill * graphic * materialColor * inherited;
                var transmissionFilter = Color.Lerp(Color.white, baseColor, 0.65f);
                var rect = material.GetVector("_LiquidGlassRect");
                var center = Vector2.one * (size * 0.5f) + (Vector2)rect;
                foreach (var color in new[] { new Color(0.2f, 0.4f, 0.8f, 1f), new Color(0.8f, 0.3f, 0.1f, 1f) })
                {
                    capture.SetPixel(0, 0, color);
                    capture.Apply(false, false);
                    SetCapture(capture);
                    Shader.SetGlobalVector("_ScreenParams", new Vector4(size, size, 1f + 1f / size, 1f + 1f / size));
                    var pixels = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard: true, axisAligned: true);
                    var actual = AverageInteriorRgb(pixels, size, center, 1, 1);
                    for (var channel = 0; channel < 3; channel++)
                    {
                        var expected = baseColor[channel] * 0.15f + color[channel] * transmissionFilter[channel] * 0.85f;
                        Assert.That(actual[channel], Is.EqualTo(expected).Within(0.005f),
                            source.Name + " must retain full authored color in the solid share and apply only sixty-five percent tint to the captured share.");
                    }
                    material.SetFloat("_LiquidArtworkBlend", 1f);
                    material.SetFloat("_LiquidTintStrength", 0f);
                    material.SetFloat("_TintStrength", 0f);
                    var legacyChanged = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard: true, axisAligned: true);
                    Assert.That(MaximumPixelDifference(pixels, legacyChanged, 4), Is.LessThan(0.0001f), "Legacy tint/share controls cannot alter the fixed liquid-plate mixture.");
                }
                capture.SetPixel(0, 0, new Color(0.5f, 0.5f, 0.5f, 1f));
                capture.Apply(false, false);
                var flat = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard: true, axisAligned: true);
                material.SetFloat("_GlassHighlight", strength);
                var lit = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard: true, axisAligned: true);
                AssertAlphaEqual(flat, lit, "Plate lighting must preserve artwork coverage.");
                var middle = AverageInteriorRgb(lit, size, center, 1, 1);
                var flatMiddle = AverageInteriorRgb(flat, size, center, 1, 1);
                foreach (var height in new[] { -0.65f, 0.65f })
                {
                    var body = AverageInteriorRgb(lit, size, center + Vector2.up * (rect.w * height), 1, 1);
                    Assert.That(Vector4.Distance(body, middle), Is.LessThan(0.002f), "The central eighty percent must retain one flat plate treatment.");
                }
                foreach (var side in new[] { -1f, 1f })
                {
                    var peak = float.MinValue;
                    for (var step = 86; step <= 97; step++)
                    {
                        var point = center + Vector2.up * (rect.w * side * step * 0.01f);
                        var index = Mathf.RoundToInt(point.y) * size + Mathf.RoundToInt(point.x);
                        if (flat[index].a < 0.99f)
                        {
                            continue;
                        }
                        var baseline = flat[index] / flat[index].a;
                        if (Vector4.Distance(baseline, flatMiddle) > 0.001f)
                        {
                            continue;
                        }
                        peak = Mathf.Max(peak, ((lit[index] - flat[index]) / flat[index].a).grayscale);
                    }
                    Assert.That(peak, Is.GreaterThan(middle.grayscale - flatMiddle.grayscale + 0.004f), "Each outer height band must carry a visible reflection above the flat middle.");
                }
                WriteWhiteEdgePreview("FixedPlate-" + source.Name, flat, lit, size);
                material.DisableKeyword(UiBackdropPanel.LiquidStyleKeyword);
                var gaussian = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard: true, axisAligned: true);
                material.SetFloat("_BackdropIsPanel", 0f);
                var gaussianWithoutPlate = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard: true, axisAligned: true);
                Assert.That(MaximumPixelDifference(gaussian, gaussianWithoutPlate, 4), Is.Zero, "Gaussian rendering must ignore the liquid-only plate policy.");
                panel.gameObject.SetActive(false);
            }
        }

        /// <summary>Checks all eight peripheral surfaces keep their radial color transition near the rim over bright and dark captures.</summary>
        /// <returns>The actual eight-key center, interior gradient, isotropic rim and unchanged-coverage regression.</returns>
        [UnityTest]
        [Category("GPU")]
        public IEnumerator PeripheralButtons_RadialLightingIsDirectionIndependent()
        {
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            UiOutlineTheme.Configure(UiOutlineMode.All);
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            const int size = 512;
            var target = _objects.Track(new RenderTexture(size, size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var resolved = _objects.Track(new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var reader = _objects.Track(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
            var capture = _objects.Track(new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true));
            Assert.That(target.Create() && resolved.Create(), Is.True);
            foreach (var id in new[] { 1589196009147436767L, 3061586214258133881L, 8655079017024877620L, 8983832068243600665L,
                7024678684790401055L, 5208406972733417819L, 4854234884495665087L, 6210024136466343178L })
            {
                var panel = CreateSerializedSurface(canvas, "Assets/Prefabs/UI/Buttons.prefab", id);
                var image = panel.GetComponent<QualityUiImage>();
                yield return null;
                Canvas.ForceUpdateCanvases();
                Assert.That(panel.RefractionReady, Is.True);
                var material = _objects.Track(new Material(image.materialForRendering));
                Assert.That(material.GetFloat("_BackdropIsButton"), Is.EqualTo(1f));
                Assert.That(material.GetFloat("_BackdropIsNativeControl"), Is.Zero);
                Assert.That(material.GetFloat("_BackdropIsPanel"), Is.Zero);
                material.SetInt("_StencilComp", (int)CompareFunction.Always);
                material.SetFloat("_GlassRefraction", 0f);
                var strength = material.GetFloat("_GlassHighlight");
                var mesh = BuildMesh(image);
                var rect = material.GetVector("_LiquidGlassRect");
                var center = Vector2.one * (size * 0.5f) + (Vector2)rect;
                foreach (var background in new[] { Color.white, new Color(0.05f, 0.05f, 0.05f, 1f) })
                {
                    capture.SetPixel(0, 0, background);
                    capture.Apply(false, false);
                    SetCapture(capture);
                    Shader.SetGlobalVector("_ScreenParams", new Vector4(size, size, 1f + 1f / size, 1f + 1f / size));
                    material.SetFloat("_GlassHighlight", 0f);
                    var flat = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard: true, axisAligned: true);
                    material.SetFloat("_GlassHighlight", strength);
                    var lit = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard: true, axisAligned: true);
                    AssertAlphaEqual(flat, lit, "Radial lighting cannot change the actual key coverage.");
                    var baseline = SampleInteriorRgb(flat, size, center);
                    var shaded = SampleInteriorRgb(lit, size, center);
                    var absorption = (baseline.grayscale - shaded.grayscale) / baseline.grayscale;
                    Assert.That(absorption, Is.InRange(0.15f, 0.22f), $"Key {id}: the visible face needs the strengthened radial center depth.");
                    var weakest = float.MaxValue;
                    var strongest = float.MinValue;
                    var weakestEdge = float.MaxValue;
                    var minimumInnerGain = float.MaxValue;
                    var minimumMiddleGain = float.MaxValue;
                    for (var angle = 0; angle < 16; angle++)
                    {
                        var radians = angle * Mathf.PI * 2f / 16f;
                        var ray = new Vector2(Mathf.Cos(radians) * rect.z, Mathf.Sin(radians) * rect.w);
                        var peak = float.MinValue;
                        // Visibility may use the clean outer band; directionality below always compares one depth.
                        for (var step = 87; step <= 98; step++)
                        {
                            var point = center + ray * (step * 0.01f);
                            var index = Mathf.RoundToInt(point.y) * size + Mathf.RoundToInt(point.x);
                            if (flat[index].a < 0.99f || Vector4.Distance(flat[index] / flat[index].a, baseline) > 0.001f)
                            {
                                continue;
                            }
                            peak = Mathf.Max(peak, ((lit[index] - flat[index]) / flat[index].a).grayscale / (1f - baseline.grayscale));
                        }
                        Assert.That(peak, Is.GreaterThan(0.08f), $"Key {id}, direction {angle}: both light and dark surfaces need a visible soft white rim.");
                        weakestEdge = Mathf.Min(weakestEdge, peak);
                        var innerFace = SampleInteriorRgb(lit, size, center + ray * 0.5f);
                        Assert.That(Vector4.Distance(innerFace, shaded), Is.LessThan(0.001f),
                            $"Key {id}, direction {angle}: the color transition must end before the inner half of the face.");
                        var inner = SampleInteriorRgb(lit, size, center + ray * 0.68f);
                        var middle = SampleInteriorRgb(lit, size, center + ray * 0.8f);
                        var innerGain = (inner.grayscale - shaded.grayscale) / baseline.grayscale;
                        var middleGain = (middle.grayscale - shaded.grayscale) / baseline.grayscale;
                        minimumInnerGain = Mathf.Min(minimumInnerGain, innerGain);
                        minimumMiddleGain = Mathf.Min(minimumMiddleGain, middleGain);
                        Assert.That(innerGain, Is.GreaterThan(0.01f), $"Key {id}, direction {angle}: the narrower color band must still brighten inward from the rim.");
                        Assert.That(middleGain, Is.GreaterThan(innerGain + 0.025f), $"Key {id}, direction {angle}: the radial face must brighten gradually toward the rim.");
                        var facePoint = center + ray * 0.8f;
                        var flatFace = SampleInteriorRgb(flat, size, facePoint);
                        Assert.That(Vector4.Distance(flatFace, baseline), Is.LessThan(0.001f), $"Key {id}, direction {angle}: the fixed-depth probe must exclude original ink.");
                        var response = (middle.grayscale - flatFace.grayscale) / (1f - baseline.grayscale);
                        weakest = Mathf.Min(weakest, response);
                        strongest = Mathf.Max(strongest, response);
                    }
                    TestContext.WriteLine($"PERIPHERAL_RADIAL id={id} capture={background.r:F2} absorption={absorption:F5} innerGain={minimumInnerGain:F5} middleGain={minimumMiddleGain:F5} weakest={weakest:F5} strongest={strongest:F5} weakestEdge={weakestEdge:F5}");
                    Assert.That(strongest - weakest, Is.LessThan(0.03f), $"Key {id}: reflections at the same depth cannot depend on vertical height or illumination direction.");
                    WriteWhiteEdgePreview($"RadialKey-{id}-{(background.r > 0.5f ? "White" : "Dark")}", flat, lit, size);
                }
                panel.gameObject.SetActive(false);
            }
        }

        /// <summary>Checks complete authored surfaces do not expose distance-field or nine-slice seams through their face lighting.</summary>
        /// <returns>The real board, sliced-card and peripheral GPU continuity regression with full-surface previews.</returns>
        [UnityTest]
        [Category("GPU")]
        public IEnumerator AuthoredSurfaces_LightingIsContinuousAcrossMedialAndSliceBoundaries()
        {
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            UiOutlineTheme.Configure(UiOutlineMode.All);
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            const int size = 1200;
            var target = _objects.Track(new RenderTexture(size, size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var resolved = _objects.Track(new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var reader = _objects.Track(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
            var capture = _objects.Track(new Texture2D(1, 1, TextureFormat.RGBA32, false, true));
            capture.SetPixel(0, 0, new Color(0.5f, 0.5f, 0.5f, 1f));
            capture.Apply(false, false);
            Assert.That(target.Create() && resolved.Create(), Is.True);
            var failures = new List<string>();
            foreach (var source in new[]
            {
                (Name: "ListBoard", Path: "Assets/Prefabs/List/Main_Display_ribbon.prefab", Panel: 5019321880153334813L),
                (Name: "SettingBoard", Path: "Assets/Scenes/Setting.unity", Panel: 900000000000000017L),
                (Name: "SettingValue", Path: "Assets/Scenes/Setting.unity", Panel: 900000000000000016L),
                (Name: "LoginBoard", Path: "Assets/Scenes/Login.unity", Panel: 900000000000000021L),
                (Name: "LoginGuest", Path: "Assets/Scenes/Login.unity", Panel: 900000000000000003L),
                (Name: "LoginConfirm", Path: "Assets/Scenes/Login.unity", Panel: 900000000000000004L),
                (Name: "UserInfoSliced", Path: "Assets/Prefabs/UI/UserInfo.prefab", Panel: 4988950831640157352L),
                (Name: "PracticeChartSliced", Path: "Assets/Scenes/Practice.unity", Panel: 900000000000000022L),
                (Name: "Peripheral", Path: "Assets/Prefabs/UI/Buttons.prefab", Panel: 1589196009147436767L)
            })
            {
                var panel = CreateSerializedSurface(canvas, source.Path, source.Panel);
                var image = panel.GetComponent<QualityUiImage>();
                yield return null;
                Canvas.ForceUpdateCanvases();
                Assert.That(panel.RefractionReady, Is.True, source.Name + " must exercise supported enabled optics.");
                var material = _objects.Track(new Material(image.materialForRendering));
                if (source.Path == "Assets/Scenes/Login.unity")
                {
                    var optics = material.GetVector("_LiquidGlassOptics");
                    Assert.That(optics.z, Is.Zero, "Login's plates and keys must not redraw a second, fine contour.");
                    Assert.That(optics.w, Is.Zero);
                    Assert.That(optics.x, Is.Positive, "Removing a fine glint must preserve the refraction width.");
                }
                material.SetInt("_StencilComp", (int)CompareFunction.Always);
                material.SetFloat("_GlassRefraction", 0f);
                var strength = material.GetFloat("_GlassHighlight");
                var mesh = BuildMesh(image);
                SetCapture(capture);
                Shader.SetGlobalVector("_ScreenParams", new Vector4(size, size, 1f + 1f / size, 1f + 1f / size));
                material.SetFloat("_GlassHighlight", 0f);
                var flat = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard: true, axisAligned: true);
                capture.SetPixel(0, 0, new Color(0.15f, 0.15f, 0.15f, 1f));
                capture.Apply(false, false);
                var alternateFlat = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard: true, axisAligned: true);
                capture.SetPixel(0, 0, new Color(0.5f, 0.5f, 0.5f, 1f));
                capture.Apply(false, false);
                material.SetFloat("_GlassHighlight", strength);
                var lit = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard: true, axisAligned: true);
                AssertAlphaEqual(flat, alternateFlat, "Changing the test exposure cannot change surface coverage: " + source.Name);
                AssertAlphaEqual(flat, lit, "Surface lighting cannot change the authored coverage: " + source.Name);
                MaximumPixelDifference(flat, lit, 3);
                WriteWhiteEdgePreview("Continuity-" + source.Name, flat, lit, size);
                var center = size / 2 * size + size / 2;
                Assert.That(flat[center].a, Is.GreaterThan(0.99f), source.Name + " must retain an ordinary interior reference.");
                var reference = flat[center] / flat[center].a;
                var alternateReference = alternateFlat[center] / alternateFlat[center].a;
                var referenceTransmission = reference - alternateReference;
                var transmissionMagnitude = Mathf.Max(Mathf.Abs(referenceTransmission.r), Mathf.Max(Mathf.Abs(referenceTransmission.g), Mathf.Abs(referenceTransmission.b)));
                Assert.That(transmissionMagnitude, Is.GreaterThan(0.001f), source.Name + " must transmit a measurable exposure contrast.");
                var body = new bool[flat.Length];
                var interior = new bool[flat.Length];
                var lighting = new Color[flat.Length];
                for (var index = 0; index < flat.Length; index++)
                {
                    if (flat[index].a < 0.99f || alternateFlat[index].a < 0.99f)
                    {
                        continue;
                    }
                    var color = flat[index] / flat[index].a;
                    var alternateColor = alternateFlat[index] / alternateFlat[index].a;
                    var bodyDifference = color - reference;
                    var alternateDifference = alternateColor - alternateReference;
                    var transmissionDifference = color - alternateColor - referenceTransmission;
                    var bodyError = Mathf.Max(Mathf.Abs(bodyDifference.r), Mathf.Max(Mathf.Abs(bodyDifference.g), Mathf.Abs(bodyDifference.b)));
                    var alternateError = Mathf.Max(Mathf.Abs(alternateDifference.r), Mathf.Max(Mathf.Abs(alternateDifference.g), Mathf.Abs(alternateDifference.b)));
                    var transmissionError = Mathf.Max(Mathf.Abs(transmissionDifference.r), Mathf.Max(Mathf.Abs(transmissionDifference.g), Mathf.Abs(transmissionDifference.b)));
                    // Compare both flat colors and their transmitted response. Ink that happens to match one
                    // exposure still loses contrast, and cannot masquerade as a continuous undecorated face.
                    body[index] = Mathf.Max(bodyError, Mathf.Max(alternateError, transmissionError)) < transmissionMagnitude * 0.002f;
                    lighting[index] = (lit[index] - flat[index]) / flat[index].a;
                }
                // Inset the uniform-artwork mask in both axes, excluding original ink, the silhouette and the narrow glint.
                const int margin = 8;
                for (var y = margin; y < size - margin; y++)
                {
                    for (var x = margin; x < size - margin; x++)
                    {
                        var valid = true;
                        for (var dy = -margin; dy <= margin; dy += margin)
                        {
                            for (var dx = -margin; dx <= margin; dx += margin)
                            {
                                valid &= body[(y + dy) * size + x + dx];
                            }
                        }
                        interior[y * size + x] = valid;
                    }
                }
                var maximumJump = 0f;
                var maximumSlopeChange = 0f;
                var maximumPosition = Vector2Int.zero;
                var comparisons = 0;
                var neighborOffsets = new[] { 1, size };
                for (var y = margin; y < size - margin - 1; y++)
                {
                    for (var x = margin; x < size - margin - 1; x++)
                    {
                        var index = y * size + x;
                        foreach (var offset in neighborOffsets)
                        {
                            if (!interior[index] || !interior[index + offset])
                            {
                                continue;
                            }
                            var delta = lighting[index] - lighting[index + offset];
                            var jump = Mathf.Max(Mathf.Abs(delta.r), Mathf.Max(Mathf.Abs(delta.g), Mathf.Abs(delta.b)));
                            if (interior[index - offset])
                            {
                                var slopeChange = lighting[index - offset] - lighting[index] - delta;
                                maximumSlopeChange = Mathf.Max(maximumSlopeChange,
                                    Mathf.Max(Mathf.Abs(slopeChange.r), Mathf.Abs(slopeChange.g), Mathf.Abs(slopeChange.b)));
                            }
                            comparisons++;
                            if (jump > maximumJump)
                            {
                                maximumJump = jump;
                                maximumPosition = new Vector2Int(x, y);
                            }
                        }
                    }
                }
                var sliceJump = 0f;
                var sliceComparisons = 0;
                if (image.type == Image.Type.Sliced)
                {
                    foreach (var axis in new[] { 0, 1 })
                    {
                        var seams = new HashSet<float>();
                        foreach (var vertex in mesh.vertices)
                        {
                            seams.Add(vertex[axis]);
                        }
                        foreach (var seam in seams)
                        {
                            if (seam <= mesh.bounds.min[axis] + margin || seam >= mesh.bounds.max[axis] - margin)
                            {
                                continue;
                            }
                            var coordinate = Mathf.RoundToInt(size * 0.5f + seam);
                            for (var along = margin; along < size - margin; along++)
                            {
                                var index = axis == 0 ? along * size + coordinate : coordinate * size + along;
                                var previous = index - (axis == 0 ? 1 : size);
                                if (!interior[index] || !interior[previous])
                                {
                                    continue;
                                }
                                var delta = lighting[index] - lighting[previous];
                                sliceJump = Mathf.Max(sliceJump, Mathf.Max(Mathf.Abs(delta.r), Mathf.Max(Mathf.Abs(delta.g), Mathf.Abs(delta.b))));
                                sliceComparisons++;
                            }
                        }
                    }
                    Assert.That(sliceComparisons, Is.GreaterThan(0), source.Name + " must sample actual internal slice interfaces.");
                }
                Assert.That(comparisons, Is.GreaterThan(250), source.Name + " must scan a two-dimensional body region.");
                var detail = $"SURFACE_CONTINUITY name={source.Name} sprite={image.sprite.name} extent={image.rectTransform.rect.size} aspect={image.preserveAspect} type={image.type} ppu={image.pixelsPerUnitMultiplier:F3} highlight={strength:F5} keyword={material.IsKeywordEnabled(UiBackdropPanel.LiquidStyleKeyword)} native={material.GetFloat("_LiquidNativeContourReady")} profile={material.GetFloat("_LiquidProfileReady")} maxJump={maximumJump:F6} position={maximumPosition} comparisons={comparisons} sliceJump={sliceJump:F6} sliceComparisons={sliceComparisons}";
                TestContext.WriteLine(detail);
                // At half width the circular face may have a steeper slope. Check its local
                // change in slope separately, so a discontinuity still fails the original limit.
                var isPeripheral = material.GetFloat("_BackdropIsButton") > 0.5f
                    && material.GetFloat("_BackdropIsNativeControl") < 0.5f;
                if (maximumJump > (isPeripheral ? 0.025f : 0.01f) || maximumSlopeChange > 0.01f || sliceJump > 0.01f)
                {
                    failures.Add($"{detail} slopeChange={maximumSlopeChange:F6}");
                }
                panel.gameObject.SetActive(false);
            }
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        /// <summary>Checks a missing optical resource does not move or erase a thin line on the real large board.</summary>
        /// <returns>The actual full/half-source blue-ring regression.</returns>
        [UnityTest]
        [Category("GPU")]
        public IEnumerator FilteredOptics_LargeBoardMissingResourcesDoNotDistortBlueRing()
        {
            LoadAtlas();
            var previousCamera = SceneSwitcher.MainCamera;
            try
            {
                const int size = 1200;
                var camera = _objects.Add<Camera>();
                camera.enabled = false;
                SceneSwitcher.MainCamera = camera;
                var canvas = _objects.Add<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                var image = CreateAuthoredPanel(canvas, "newui@3x_22").GetComponent<QualityUiImage>();
                image.color = new Color(1f, 1f, 1f, 0.85f);
                UiOutlineTheme.Configure(UiOutlineMode.All);
                UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
                yield return null;
                Canvas.ForceUpdateCanvases();
                var source = CreateProbeCapture(size, true);
                var half = _objects.Track(new RenderTexture(size / 2, size / 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
                Assert.That(half.Create(), Is.True);
                half.filterMode = FilterMode.Bilinear;
                Graphics.Blit(source, half);
                foreach (var capture in new Texture[] { source, half })
                {
                    CheckUnrefractedFallbacks(image, capture, size, true, "BLUE_RING source=" + capture.width);
                }
            }
            finally
            {
                SceneSwitcher.MainCamera = previousCamera;
            }
        }

        /// <summary>Creates a deterministic valid-color circle with zero coverage outside the capture boundary.</summary>
        /// <param name="size">The square capture resolution.</param>
        /// <param name="center">The circle center in capture UV coordinates.</param>
        /// <param name="radius">The circle radius in capture UV units.</param>
        /// <param name="fill">The valid interior color and coverage.</param>
        /// <returns>The fixture-owned linear, bilinear-filtered capture.</returns>
        private Texture2D CreateCircleCapture(int size, Vector2 center, float radius, Color fill)
        {
            var capture = _objects.Track(new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            });
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var delta = new Vector2((x + 0.5f) / size, (y + 0.5f) / size) - center;
                    pixels[y * size + x] = delta.sqrMagnitude <= radius * radius ? fill : Color.clear;
                }
            }
            capture.SetPixels(pixels);
            capture.Apply(false, false);
            return capture;
        }

        /// <summary>Creates a deterministic rich source or the actual thin-line stress source without running scene capture.</summary>
        /// <param name="size">The source dimension.</param><param name="ring">Whether to draw the thin blue ring.</param>
        /// <returns>The fixture-owned linear source texture.</returns>
        private Texture2D CreateProbeCapture(int size, bool ring)
        {
            var capture = _objects.Track(new Texture2D(size, size, TextureFormat.RGBA32, false, true));
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    if (ring)
                    {
                        var delta = new Vector2(x + 0.5f - size * 0.5f, y + 0.5f - size * 0.5f);
                        var coverage = Mathf.Clamp01(1.15f - Mathf.Abs(delta.magnitude - 470f));
                        pixels[y * size + x] = Color.Lerp(Color.white, new Color(0.1f, 0.35f, 0.75f, 1f), coverage);
                    }
                    else
                    {
                        var u = x / (float)(size - 1);
                        var v = y / (float)(size - 1);
                        pixels[y * size + x] = new Color(0.2f + 0.6f * u, 0.15f + 0.6f * v, 0.2f + 0.4f * u * v, 1f);
                    }
                }
            }
            capture.SetPixels(pixels);
            capture.Apply(false, false);
            capture.filterMode = FilterMode.Bilinear;
            capture.wrapMode = TextureWrapMode.Clamp;
            return capture;
        }

        /// <summary>Compares missing capabilities/resources against an explicit unshifted glass draw rather than an analytic substitute.</summary>
        /// <param name="image">The actual control.</param><param name="capture">The sampled scene source.</param>
        /// <param name="size">The target dimension.</param><param name="largeBoard">Whether to preserve large-board coordinates.</param>
        /// <param name="context">The contour and capture context.</param>
        private void CheckUnrefractedFallbacks(QualityUiImage image, Texture capture, int size, bool largeBoard, string context)
        {
            SetCapture(capture);
            Shader.SetGlobalVector("_ScreenParams", new Vector4(size, size, 1f + 1f / size, 1f + 1f / size));
            // The isolated mesh draw does not execute the parent Mask's stencil-writing pass.
            // Preserve its actual optical state in an owned copy, but make only this probe's
            // stencil comparison unconditional; native stencil synchronization is checked separately.
            var material = _objects.Track(new Material(image.materialForRendering));
            material.SetInt("_StencilComp", (int)CompareFunction.Always);
            var contourReady = material.GetFloat("_LiquidContourReady");
            var nativeReady = material.GetFloat("_LiquidNativeContourReady");
            var profileReady = material.GetFloat("_LiquidProfileReady");
            var refractionReady = material.GetFloat("_LiquidRefractionReady");
            var filtered = UiGlassOpticsAtlas.SupportsContourFiltering;
            Assert.That(refractionReady > 0.5f, Is.EqualTo(filtered && (contourReady > 0.5f || nativeReady > 0.5f) && profileReady > 0.5f));
            if (contourReady > 0.5f)
            {
                Assert.That(material.GetTexture("_LiquidContourTex").graphicsFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
            }
            var mesh = BuildMesh(image);
            var target = _objects.Track(new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
            var resolved = _objects.Track(new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
            Assert.That(target.Create() && resolved.Create(), Is.True);
            var reader = _objects.Track(new Texture2D(size, size, TextureFormat.RGBA32, false, true));
            material.SetFloat("_LiquidRefractionReady", 0f);
            var baseline = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard);
            var visible = 0;
            foreach (var pixel in baseline)
            {
                visible += pixel.a > 0.25f && Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b)) > 0.05f ? 1 : 0;
            }
            Assert.That(visible, Is.GreaterThan(100), "Disabling unavailable refraction must retain the control and its background colors.");
            material.SetFloat("_LiquidRefractionReady", refractionReady);
            var enabled = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard);
            var enabledDifference = 0f;
            for (var pixel = 0; pixel < enabled.Length; pixel++)
            {
                Assert.That(enabled[pixel].a, Is.EqualTo(baseline[pixel].a).Within(1f / 255f));
                for (var channel = 0; channel < 3; channel++)
                {
                    enabledDifference = Mathf.Max(enabledDifference, Mathf.Abs(enabled[pixel][channel] - baseline[pixel][channel]));
                }
            }
            TestContext.WriteLine($"FILTERED_ACTIVE context={context} supported={filtered} ready={refractionReady} format={material.GetTexture("_LiquidContourTex").graphicsFormat} maxVersusUnrefracted={enabledDifference:F8}");
            if (refractionReady > 0.5f)
            {
                Assert.That(enabledDifference, Is.GreaterThan(0.5f / 255f), "The supported branch must actually render its contour/profile optics.");
            }
            else
            {
                Assert.That(enabledDifference, Is.LessThanOrEqualTo(1f / 255f));
            }
            try
            {
                var requiredContour = material.GetFloat("_BackdropIsRoundedRect") > 0.5f ? "_LiquidNativeContourReady" : "_LiquidContourReady";
                foreach (var disabled in new[] { requiredContour, "_LiquidProfileReady", "_LiquidRefractionReady" })
                {
                    material.SetFloat("_LiquidContourReady", contourReady);
                    material.SetFloat("_LiquidNativeContourReady", nativeReady);
                    material.SetFloat("_LiquidProfileReady", profileReady);
                    material.SetFloat("_LiquidRefractionReady", refractionReady);
                    material.SetFloat(disabled, 0f);
                    var actual = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard);
                    var maximum = 0f;
                    for (var pixel = 0; pixel < actual.Length; pixel++)
                    {
                        for (var channel = 0; channel < 4; channel++)
                        {
                            maximum = Mathf.Max(maximum, Mathf.Abs(actual[pixel][channel] - baseline[pixel][channel]));
                        }
                    }
                    TestContext.WriteLine($"NO_REFRACTION context={context} disabled={disabled} filtered={filtered} contour={contourReady} profile={profileReady} refraction={refractionReady} max={maximum:F8} visible={visible}");
                    Assert.That(maximum, Is.LessThanOrEqualTo(1f / 255f), "Unavailable refraction must produce unshifted glass composition, not four-point or analytic optics.");
                }
            }
            finally
            {
                material.SetFloat("_LiquidContourReady", contourReady);
                material.SetFloat("_LiquidNativeContourReady", nativeReady);
                material.SetFloat("_LiquidProfileReady", profileReady);
                material.SetFloat("_LiquidRefractionReady", refractionReady);

            }
        }

        /// <summary>Checks actual sliced UserInfo binding rings retain original premultiplied artwork while the card still transmits its backdrop.</summary>
        /// <returns>The native mesh, foreground-artwork and relative-fade regression.</returns>
        [UnityTest]
        [Category("GPU")]
        public IEnumerator UserInfoSliced_ProtectsBindingRingArtworkAndKeepsBodyTransmission()
        {
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var image = CreateAuthoredPanel(canvas, "newui@3x_26").GetComponent<QualityUiImage>();
            UiOutlineTheme.Configure(UiOutlineMode.All);
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.That(image.type, Is.EqualTo(Image.Type.Sliced));
            var material = image.material;
            Assert.That(material.GetVector("_LiquidArtworkProtection").z, Is.EqualTo(1f));
            var reference = _objects.Track(new Material(Shader.Find("Maj/UI/Atlas Alpha")));
            var capture = _objects.Track(new Texture2D(1, 1, TextureFormat.RGBA32, false, true));
            var target = _objects.Track(new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
            var resolved = _objects.Track(new RenderTexture(512, 512, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
            var reader = _objects.Track(new Texture2D(512, 512, TextureFormat.RGBA32, false, true));
            Assert.That(target.Create() && resolved.Create(), Is.True);
            Assert.That(UiAtlasMeshGenerator.TryGetLegacy(image.sprite, out var legacy, out _), Is.True);
            var protectionEnd = (400f / 3058f - 0.5f) * legacy.rect.width;
            var maximumProtectedDifference = 0f;
            var protectedInkPixels = 0;
            Color[]? bodyFirstCapture = null;
            foreach (var fade in new[] { 1f, 0.5f })
            {
                image.color = new Color(0.8f, 0.9f, 1f, fade);
                Canvas.ForceUpdateCanvases();
                var mesh = BuildMesh(image);
                var expected = Draw(image, reference, mesh, target, resolved, reader, 1, true);
                var positions = mesh.vertices;
                var uvs = mesh.uv;
                var indices = mesh.triangles;
                foreach (var background in new[] { Color.red, Color.blue })
                {
                    capture.SetPixel(0, 0, background);
                    capture.Apply(false, false);
                    SetCapture(capture);
                    Shader.SetGlobalVector("_ScreenParams", new Vector4(512f, 512f, 1f + 1f / 512f, 1f + 1f / 512f));
                    var actual = Draw(image, material, mesh, target, resolved, reader, 1, true);
                    var uvToSource = material.GetVector("_LiquidGlassUvToLocal");
                    for (var y = 190; y < 322; y++)
                    {
                        for (var x = 50; x < 155; x++)
                        {
                            var index = y * 512 + x;
                            if (!TryGetMeshUv(new Vector2(x + 0.5f - 256f, y + 0.5f - 256f), positions, uvs, indices, out var uv)
                                || uv.x * uvToSource.x + uvToSource.z > protectionEnd || expected[index].a <= 0.02f)
                            {
                                continue;
                            }
                            protectedInkPixels += expected[index].r < expected[index].a * 0.85f ? 1 : 0;
                            for (var channel = 0; channel < 4; channel++)
                            {
                                maximumProtectedDifference = Mathf.Max(maximumProtectedDifference,
                                    Mathf.Abs(actual[index][channel] - expected[index][channel]));
                            }
                        }
                    }
                    if (fade == 1f)
                    {
                        if (bodyFirstCapture == null)
                        {
                            bodyFirstCapture = actual;
                        }
                        else
                        {
                            Assert.That(Mathf.Abs(bodyFirstCapture[256 * 512 + 320].r - actual[256 * 512 + 320].r), Is.GreaterThan(0.1f),
                                "Protecting the rings must not disable the card body's backdrop transmission.");
                        }
                    }
                }
            }
            TestContext.WriteLine($"USERINFO_IRON protectedInkSamples={protectedInkPixels} maxRgbaDifference={maximumProtectedDifference:F7}");
            Assert.That(protectedInkPixels, Is.GreaterThan(20), "The fixture must actually cover the original gray binding rings.");
            Assert.That(maximumProtectedDifference, Is.LessThanOrEqualTo(1f / 255f), "Protected artwork must retain original RGB, alpha and Graphic fade.");
        }

        /// <summary>Interpolates actual native mesh UVs to identify the protected source strip without assuming nine-slice proportions.</summary>
        /// <param name="position">The logical pixel center.</param>
        /// <param name="vertices">The actual generated positions.</param>
        /// <param name="uvs">The actual generated atlas coordinates.</param>
        /// <param name="indices">The native triangle indices.</param>
        /// <param name="uv">The interpolated atlas coordinate when a triangle contains the point.</param>
        /// <returns>Whether the point falls within the native rendered mesh.</returns>
        private static bool TryGetMeshUv(Vector2 position, Vector3[] vertices, Vector2[] uvs, int[] indices, out Vector2 uv)
        {
            for (var triangle = 0; triangle < indices.Length; triangle += 3)
            {
                var a = indices[triangle];
                var b = indices[triangle + 1];
                var c = indices[triangle + 2];
                var ab = (Vector2)(vertices[b] - vertices[a]);
                var ac = (Vector2)(vertices[c] - vertices[a]);
                var ap = position - (Vector2)vertices[a];
                var determinant = ab.x * ac.y - ab.y * ac.x;
                if (Mathf.Abs(determinant) < 0.000001f)
                {
                    continue;
                }
                var u = (ap.x * ac.y - ap.y * ac.x) / determinant;
                var v = (ab.x * ap.y - ab.y * ap.x) / determinant;
                if (u >= 0f && v >= 0f && u + v <= 1f)
                {
                    uv = uvs[a] + (uvs[b] - uvs[a]) * u + (uvs[c] - uvs[a]) * v;
                    return true;
                }
            }
            uv = default;
            return false;
        }

        /// <summary>Checks actual native stencil variants receive baked bindings and invalidate them when source geometry stops matching.</summary>
        /// <returns>The UGUI material ownership and geometry-mismatch regression.</returns>
        [UnityTest]
        public IEnumerator PanelBinding_TracksStencilAndRejectsChangedGeometry()
        {
            var atlas = LoadAtlas();
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var mask = _objects.Create("Baked contour stencil parent", canvas.transform);
            mask.AddComponent<Image>();
            mask.AddComponent<Mask>().showMaskGraphic = false;
            var panel = CreateAuthoredPanel(canvas, "newui@3x_26");
            var image = panel.GetComponent<QualityUiImage>();
            image.transform.SetParent(mask.transform, false);
            UiOutlineTheme.Configure(UiOutlineMode.All);
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            yield return null;
            Canvas.ForceUpdateCanvases();
            var owned = image.material;
            var rendered = image.canvasRenderer.GetMaterial(0);
            Assert.That(rendered, Is.Not.SameAs(owned));
            Assert.That(panel.RefractionReady, Is.EqualTo(UiGlassOpticsAtlas.SupportsContourFiltering));
            foreach (var material in new[] { owned, rendered })
            {
                Assert.That(material.GetFloat("_LiquidContourReady"), Is.EqualTo(1f));
                Assert.That(material.GetFloat("_LiquidProfileReady"), Is.EqualTo(1f));
                Assert.That(material.IsKeywordEnabled(UiBackdropPanel.LiquidStyleKeyword), Is.True);
                Assert.That(material.GetVector("_LiquidContourDecode").x, Is.GreaterThan(0f));
                Assert.That(material.GetFloat("_LiquidRefractionReady"), Is.EqualTo(UiGlassOpticsAtlas.SupportsContourFiltering ? 1f : 0f));
                Assert.That(material.GetTexture("_LiquidContourTex"), Is.SameAs(atlas.DistanceTexture));
                Assert.That(material.GetTexture("_LiquidProfileTex"), Is.SameAs(atlas.ProfileTexture));
                Assert.That(material.GetVector("_LiquidProfileUV"), Is.EqualTo(new Vector4((atlas.ProfileTexture!.width - 1f) / atlas.ProfileTexture.width, 0.5f / atlas.ProfileTexture.width, 0f, 0f)));
            }
            Assert.That(panel.TryGetOpticalContour(out var sourceContour), Is.True);
            Assert.That(atlas.TryResolve(in sourceContour, owned.GetVector("_LiquidGlassUvToLocal"),
                out _, out _, out var sourceDecode), Is.True);
            var expectedDecode = new Vector4(sourceDecode.x, sourceDecode.y, 0f, 0f);
            Assert.That(owned.GetVector("_LiquidContourDecode"), Is.EqualTo(expectedDecode));
            Assert.That(rendered.GetVector("_LiquidContourDecode"), Is.EqualTo(expectedDecode));
            var insets = typeof(UiBackdropPanel).GetField("_liquidBodyInsets", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var original = (Vector4)insets.GetValue(panel)!;
            insets.SetValue(panel, original + new Vector4(0.013f, 0f, 0f, 0f));
            Canvas.ForceUpdateCanvases();
            Assert.That(image.material, Is.SameAs(owned));
            Assert.That(image.canvasRenderer.GetMaterial(0), Is.SameAs(rendered));
            Assert.That(owned.GetFloat("_LiquidContourReady"), Is.Zero);
            Assert.That(rendered.GetFloat("_LiquidContourReady"), Is.Zero);
            Assert.That(owned.GetFloat("_LiquidRefractionReady"), Is.Zero);
            Assert.That(rendered.GetFloat("_LiquidRefractionReady"), Is.Zero);
            Assert.That(panel.RefractionReady, Is.False);
            CheckUnrefractedFallbacks(image, CreateProbeCapture(64, false), 128, false, "unbaked contour edit");
            insets.SetValue(panel, original);
            Canvas.ForceUpdateCanvases();
            Assert.That(owned.GetFloat("_LiquidContourReady"), Is.EqualTo(1f));
            Assert.That(rendered.GetFloat("_LiquidContourReady"), Is.EqualTo(1f));
            image.sprite = null;
            image.material = _objects.Track(new Material(Shader.Find("Maj/UI/Rounded Rect")));
            Canvas.ForceUpdateCanvases();
            Assert.That(image.material.GetFloat("_LiquidContourReady"), Is.Zero, "A dynamic procedural rectangle must not reuse the last Sprite's baked tile.");
            Assert.That(image.material.GetFloat("_LiquidNativeContourReady"), Is.Zero, "The selected Sprite's old Sliced mode must not opt into the exact Simple native-contour path.");
            image.type = Image.Type.Simple;
            Canvas.ForceUpdateCanvases();
            Assert.That(image.material.GetFloat("_LiquidNativeContourReady"), Is.EqualTo(1f), "The exact native Rounded Rect can use its existing coverage contour independently of the old Sprite tile.");
            Assert.That(panel.RefractionReady, Is.True);
            image.material = _objects.Track(new Material(Shader.Find("Maj/UI/Atlas Alpha")));
            Canvas.ForceUpdateCanvases();
            Assert.That(panel.RefractionReady, Is.False, "An unknown sprite-less shader cannot opt into native refraction.");
        }

        /// <summary>Checks unsupported sampled data, linear filtering or profiles disable refraction while retaining visible glass composition.</summary>
        /// <returns>The independent capability and explicit data-sampler binding regression.</returns>
        [UnityTest]
        public IEnumerator Capabilities_UnavailableFilteredContourOrProfileDisablesRefraction()
        {
            var atlas = LoadAtlas();
            var distance = atlas.DistanceTexture!;
            var oldFilter = distance.filterMode;
            var encoding = typeof(UiGlassOpticsAtlas).GetField("_encodingVersion", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var originalEncoding = (int)encoding.GetValue(atlas)!;
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var mask = _objects.Create("Capability stencil parent", canvas.transform);
            mask.AddComponent<Image>();
            mask.AddComponent<Mask>().showMaskGraphic = false;
            UiOutlineTheme.Configure(UiOutlineMode.All);
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            try
            {
                foreach (var capability in new[] { new Vector3Int(1, 1, 1), new Vector3Int(1, 0, 1),
                    new Vector3Int(0, 0, 1), new Vector3Int(1, 1, 0), Vector3Int.zero })
                {
                    var contourSupported = capability.x > 0;
                    var linearSupported = capability.y > 0;
                    var profileSupported = capability.z > 0;
                    const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
                    var type = typeof(UiGlassOpticsAtlas);
                    type.GetField("s_capabilitiesInitialized", flags)!.SetValue(null, true);
                    type.GetField("s_contourSampleSupported", flags)!.SetValue(null, contourSupported);
                    type.GetField("s_contourLinearSupported", flags)!.SetValue(null, linearSupported);
                    type.GetField("s_profileSupported", flags)!.SetValue(null, profileSupported);
                    type.GetField("s_maximumTextureSize", flags)!.SetValue(null, SystemInfo.maxTextureSize);
                    Assert.That(UiGlassOpticsAtlas.Load(), Is.SameAs((contourSupported && linearSupported) || profileSupported ? atlas : null));
                    Assert.That(atlas.ProfileTexture != null, Is.EqualTo(profileSupported));
                    Assert.That(atlas.DistanceTexture != null, Is.EqualTo(contourSupported && linearSupported));
                    var panel = CreateAuthoredPanel(canvas, "newui@3x_26");
                    var image = panel.GetComponent<QualityUiImage>();
                    image.transform.SetParent(mask.transform, false);
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    var owned = image.material;
                    var rendered = image.canvasRenderer.GetMaterial(0);
                    Assert.That(rendered, Is.Not.SameAs(owned));
                    foreach (var material in new[] { owned, rendered })
                    {
                        Assert.That(material.GetFloat("_LiquidContourReady"), Is.EqualTo(contourSupported && linearSupported ? 1f : 0f));
                        Assert.That(material.GetFloat("_LiquidNativeContourReady"), Is.Zero, "Sprite consumers must not bypass missing baked data through the procedural path.");
                        Assert.That(material.GetFloat("_LiquidProfileReady"), Is.EqualTo(profileSupported ? 1f : 0f));
                        Assert.That(material.GetFloat("_LiquidRefractionReady"), Is.EqualTo(contourSupported && linearSupported && profileSupported ? 1f : 0f));
                        Assert.That(material.IsKeywordEnabled(UiBackdropPanel.LiquidStyleKeyword), Is.True);
                        if (contourSupported && linearSupported)
                        {
                            Assert.That(material.GetTexture("_LiquidContourTex"), Is.SameAs(distance));
                            Assert.That(distance.filterMode, Is.EqualTo(FilterMode.Bilinear));
                            Assert.That(material.GetVector("_LiquidContourDecode").x, Is.GreaterThan(0f));

                        }
                    }
                    CheckUnrefractedFallbacks(image, CreateProbeCapture(64, false), 128, false, "caps=" + capability);
                    panel.gameObject.SetActive(false);
                    var nativePanel = CreateAuthoredCategory(canvas, setting: true, selected: false);
                    var nativeImage = nativePanel.GetComponent<QualityUiImage>();
                    nativeImage.transform.SetParent(mask.transform, false);
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    var expectedNativeReady = contourSupported && linearSupported && profileSupported ? 1f : 0f;
                    foreach (var nativeMaterial in new[] { nativeImage.material, nativeImage.canvasRenderer.GetMaterial(0) })
                    {
                        Assert.That(nativeMaterial.GetFloat("_LiquidContourReady"), Is.Zero);
                        Assert.That(nativeMaterial.GetFloat("_LiquidNativeContourReady"), Is.EqualTo(expectedNativeReady));
                        Assert.That(nativeMaterial.GetFloat("_LiquidRefractionReady"), Is.EqualTo(expectedNativeReady));
                    }
                    CheckUnrefractedFallbacks(nativeImage, CreateProbeCapture(64, false), 128, false, "native caps=" + capability);
                    nativePanel.gameObject.SetActive(false);
                }
                typeof(UiGlassOpticsAtlas).GetField("s_contourSampleSupported", BindingFlags.NonPublic | BindingFlags.Static)!
                    .SetValue(null, true);
                typeof(UiGlassOpticsAtlas).GetField("s_profileSupported", BindingFlags.NonPublic | BindingFlags.Static)!
                    .SetValue(null, true);
                typeof(UiGlassOpticsAtlas).GetField("s_contourLinearSupported", BindingFlags.NonPublic | BindingFlags.Static)!
                    .SetValue(null, true);
                encoding.SetValue(atlas, UiGlassOpticsAtlas.CurrentEncodingVersion - 1);
                Assert.That(atlas.DistanceTexture, Is.Null, "Old contour encodings must not supply refraction.");
                Assert.That(atlas.ProfileTexture, Is.Null, "Independent profile support must not accept incompatible resource encodings.");
                Assert.That(UiGlassOpticsAtlas.Load(), Is.Null, "An incompatible generated resource must not be published.");
            }
            finally
            {
                encoding.SetValue(atlas, originalEncoding);
                UiGlassOpticsAtlas.ResetCache();
                distance.filterMode = oldFilter;
            }
        }

        /// <summary>Loads the intentional runtime resource rather than substituting generated test data.</summary>
        /// <returns>The generated source-distance and optical-profile asset.</returns>
        private static UiGlassOpticsAtlas LoadAtlas()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null));
            var atlas = UiGlassOpticsAtlas.Load();
            Assert.That(atlas, Is.Not.Null, "Run the isolated production baker before this resource-dependent scope.");
            Assert.That(atlas!.DistanceTexture, Is.Not.Null);
            Assert.That(atlas.ProfileTexture, Is.Not.Null);
            return atlas;
        }

        /// <summary>Reads the real uncompressed data through GPU sampling without requiring a CPU-readable runtime copy.</summary>
        /// <param name="texture">The imported linear data source.</param>
        /// <param name="width">The output width, or zero to sample native texel centers.</param>
        /// <param name="height">The output height, or zero to sample native texel centers.</param>
        /// <returns>The actual GPU-filtered values.</returns>
        private Color[] ReadFloatTexture(Texture2D texture, int width = 0, int height = 0)
        {
            width = width > 0 ? width : texture.width;
            height = height > 0 ? height : texture.height;
            var target = _objects.Track(new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            Assert.That(target.Create(), Is.True);
            Graphics.Blit(texture, target);
            RenderTexture.active = target;
            var reader = _objects.Track(new Texture2D(width, height, TextureFormat.RGBAFloat, false, true));
            reader.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            reader.Apply(false, false);
            return reader.GetPixels();
        }

        /// <summary>Reads one artwork sample through the current graphics color space without requiring readable source textures.</summary>
        /// <param name="texture">The actual bound artwork or inherited-tint texture.</param>
        /// <param name="uv">The same normalized coordinate consumed by the production shader.</param>
        /// <returns>The GPU-sampled working-space color.</returns>
        private Color SampleWorkingTexture(Texture texture, Vector2 uv)
        {
            var target = _objects.Track(new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            Assert.That(target.Create(), Is.True);
            Graphics.Blit(texture, target, Vector2.zero, uv);
            RenderTexture.active = target;
            var reader = _objects.Track(new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true));
            reader.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            reader.Apply(false, false);
            return reader.GetPixel(0, 0);
        }

        /// <summary>Loads an authored surface's static Image, geometry and optical fields without running its scene controllers.</summary>
        /// <param name="canvas">The isolated test Canvas.</param>
        /// <param name="path">The original scene or prefab asset.</param>
        /// <param name="panelId">The serialized UiBackdropPanel component identifier.</param>
        /// <returns>The active surface with its actual artwork, sizing policy and optional source tint.</returns>
        private UiBackdropPanel CreateSerializedSurface(Canvas canvas, string path, long panelId)
        {
            var child = _objects.Create("Serialized surface " + panelId, canvas.transform);
            child.SetActive(false);
            var image = child.AddComponent<QualityUiImage>();
            var panel = child.AddComponent<UiBackdropPanel>();
#if UNITY_EDITOR
            var yaml = MajdataPlay.IO.Storage.FileSystem.File.Open(path).ReadAllText();
            var panelBody = Regex.Match(yaml, @"^--- !u!114 &" + panelId + @"\r?\n(?<body>.*?)(?=^--- |\z)", RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
            Assert.That(panelBody, Is.Not.Empty, path + ": " + panelId);
            ApplyAuthoredFields(panel, panelBody);
            var owner = Regex.Match(panelBody, @"m_GameObject: \{fileID: (?<id>-?\d+)\}").Groups["id"].Value;
            var imageBody = string.Empty;
            var rectBody = string.Empty;
            foreach (Match component in Regex.Matches(yaml, @"^--- !u!(?<type>\d+) &-?\d+\r?\n(?<body>.*?)(?=^--- |\z)", RegexOptions.Multiline | RegexOptions.Singleline))
            {
                var body = component.Groups["body"].Value;
                if (!body.Contains("m_GameObject: {fileID: " + owner + "}"))
                {
                    continue;
                }
                if (component.Groups["type"].Value == "224")
                {
                    rectBody = body;
                }
                else if (body.Contains("guid: 7c51cc521e1747f0ba99ae9b65785bd5"))
                {
                    imageBody = body;
                }
            }
            Assert.That(imageBody, Is.Not.Empty, path + ": surface Image");
            Assert.That(rectBody, Is.Not.Empty, path + ": surface RectTransform");
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var spriteReference = Regex.Match(imageBody, @"m_Sprite: \{fileID: (?<id>-?\d+), guid: (?<guid>[0-9a-f]{32})");
            Assert.That(spriteReference.Success, Is.True);
            var spriteId = long.Parse(spriteReference.Groups["id"].Value, culture);
            var spritePath = AssetDatabase.GUIDToAssetPath(spriteReference.Groups["guid"].Value);
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(spritePath))
            {
                if (asset is Sprite sprite && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out _, out long id) && id == spriteId)
                {
                    image.sprite = sprite;
                    break;
                }
            }
            Assert.That(image.sprite, Is.Not.Null, path + ": exact authored Sprite");
            var materialReference = Regex.Match(imageBody, @"m_Material: \{fileID: -?\d+, guid: (?<guid>[0-9a-f]{32})");
            Assert.That(materialReference.Success, Is.True);
            image.material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(materialReference.Groups["guid"].Value));
            var color = Regex.Match(imageBody, @"m_Color: \{r: (?<r>[^,]+), g: (?<g>[^,]+), b: (?<b>[^,]+), a: (?<a>[^}]+)\}");
            Assert.That(color.Success, Is.True);
            image.color = new Color(float.Parse(color.Groups["r"].Value, culture), float.Parse(color.Groups["g"].Value, culture),
                float.Parse(color.Groups["b"].Value, culture), float.Parse(color.Groups["a"].Value, culture));
            foreach (Match field in Regex.Matches(imageBody, @"^  (?<name>m_Type|m_PreserveAspect|m_UseSpriteMesh|m_FillCenter|m_PixelsPerUnitMultiplier): (?<v>[^\r\n]+)", RegexOptions.Multiline))
            {
                var value = float.Parse(field.Groups["v"].Value, culture);
                switch (field.Groups["name"].Value)
                {
                    case "m_Type":
                        image.type = (Image.Type)(int)value;
                        break;
                    case "m_PreserveAspect":
                        image.preserveAspect = value != 0f;
                        break;
                    case "m_UseSpriteMesh":
                        image.useSpriteMesh = value != 0f;
                        break;
                    case "m_FillCenter":
                        image.fillCenter = value != 0f;
                        break;
                    case "m_PixelsPerUnitMultiplier":
                        image.pixelsPerUnitMultiplier = value;
                        break;
                }
            }
            var extent = Regex.Match(rectBody, @"m_SizeDelta: \{x: (?<x>[^,]+), y: (?<y>[^}]+)\}");
            var pivot = Regex.Match(rectBody, @"m_Pivot: \{x: (?<x>[^,]+), y: (?<y>[^}]+)\}");
            Assert.That(extent.Success && pivot.Success, Is.True);
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = Vector2.one * 0.5f;
            image.rectTransform.pivot = new Vector2(float.Parse(pivot.Groups["x"].Value, culture), float.Parse(pivot.Groups["y"].Value, culture));
            var resolvedSize = new Vector2(float.Parse(extent.Groups["x"].Value, culture), float.Parse(extent.Groups["y"].Value, culture));
            if (path.EndsWith(".prefab", StringComparison.Ordinal))
            {
                // Stretched children serialize only an offset from their parent, often a negative sizeDelta.
                // Read the authored hierarchy's resolved rectangle before isolating it under centered anchors.
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var found = false;
                foreach (var candidate in prefab.GetComponentsInChildren<UiBackdropPanel>(true))
                {
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate, out _, out long id) && id == panelId)
                    {
                        resolvedSize = ((RectTransform)candidate.transform).rect.size;
                        found = true;
                        break;
                    }
                }
                Assert.That(found, Is.True, path + ": resolve the actual prefab surface rectangle.");
            }
            Assert.That(resolvedSize.x > 0f && resolvedSize.y > 0f, Is.True, path + ": the isolated surface must retain its authored size.");
            image.rectTransform.sizeDelta = resolvedSize;
            var tintSource = Regex.Match(panelBody, @"_liquidTintSource: \{fileID: (?<id>-?\d+)\}");
            if (tintSource.Success && tintSource.Groups["id"].Value != "0")
            {
                var source = CreateSerializedSurface(canvas, path, long.Parse(tintSource.Groups["id"].Value, culture));
                typeof(UiBackdropPanel).GetField("_liquidTintSource", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(panel, source);
            }
#else
            Assert.Ignore("This continuity fixture requires the actual Editor-imported scene and prefab artwork.");
#endif
            child.SetActive(true);
            return panel;
        }

        /// <summary>Copies one actual outer circular body without instantiating its sensor bindings or foreground controls.</summary>
        /// <param name="canvas">The isolated native UGUI root.</param>
        /// <returns>The authored peripheral panel with its original artwork and optical policy.</returns>
        private UiBackdropPanel CreateAuthoredPeripheral(Canvas canvas)
        {
            var child = _objects.Create("Authored outer circular body", canvas.transform);
            child.SetActive(false);
            var image = child.AddComponent<QualityUiImage>();
            var panel = child.AddComponent<UiBackdropPanel>();
#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Buttons.prefab");
            Assert.That(prefab, Is.Not.Null);
            UiBackdropPanel? source = null;
            foreach (var candidate in prefab.GetComponentsInChildren<UiBackdropPanel>(true))
            {
                if (candidate.IsButton)
                {
                    source = candidate;
                    break;
                }
            }
            Assert.That(source, Is.Not.Null);
            EditorUtility.CopySerialized(source!.GetComponent<QualityUiImage>(), image);
            EditorUtility.CopySerialized(source, panel);
            typeof(UiBackdropPanel).GetField("_buttonForegroundRoot", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(panel, null);
#else
            Assert.Ignore("This focused fixture requires the actual Editor-imported peripheral prefab.");
#endif
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = Vector2.one * 0.5f;
            image.rectTransform.pivot = Vector2.one * 0.5f;
            image.rectTransform.sizeDelta = new Vector2(200f, 200f);
            image.preserveAspect = false;
            child.SetActive(true);
            return panel;
        }

        /// <summary>Copies one explicitly opted-in decoration without running cover loading or list controllers.</summary>
        /// <param name="canvas">The isolated UGUI root.</param>
        /// <param name="path">The authored prefab path.</param>
        /// <param name="fileId">The original decoration Image identifier.</param>
        /// <returns>The active quality-aware Image retaining its authored Sprite and liquid material.</returns>
        private QualityUiImage CreateAuthoredDecoration(Canvas canvas, string path, long fileId)
        {
            var child = _objects.Create("Authored cover decoration", canvas.transform);
            child.SetActive(false);
            var image = child.AddComponent<QualityUiImage>();
#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null);
            QualityUiImage? source = null;
            foreach (var candidate in prefab.GetComponentsInChildren<QualityUiImage>(true))
            {
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate, out _, out long id) && id == fileId)
                {
                    source = candidate;
                    break;
                }
            }
            Assert.That(source, Is.Not.Null, path + ": " + fileId);
            EditorUtility.CopySerialized(source, image);
#else
            Assert.Ignore("This focused fixture requires the authored Editor-imported decorations.");
#endif
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = Vector2.one * 0.5f;
            image.rectTransform.sizeDelta = new Vector2(200f, 200f);
            child.SetActive(true);
            return image;
        }

        /// <summary>Copies an actual category body without instantiating its unrelated scene controllers.</summary>
        /// <param name="canvas">The isolated native UGUI root.</param>
        /// <param name="setting">Whether to use the Setting menu rather than the List collection prefab.</param>
        /// <param name="selected">Whether to apply the selected width and tint to the same authored native body.</param>
        /// <returns>The active copied production panel, preserving authored Image and optical settings.</returns>
        private UiBackdropPanel CreateAuthoredCategory(Canvas canvas, bool setting, bool selected)
        {
            var child = _objects.Create((setting ? "Setting" : "List") + (selected ? " selected" : " unselected"), canvas.transform);
            child.SetActive(false);
            var image = child.AddComponent<QualityUiImage>();
            var panel = child.AddComponent<UiBackdropPanel>();
#if UNITY_EDITOR
            var path = setting ? "Assets/Prefabs/Setting/MenuTitleDisplayer.prefab" : "Assets/Prefabs/List/Model/CollectionDisplayer.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null);
            var source = setting ? prefab.transform : prefab.transform.Find("BG");
            Assert.That(source, Is.Not.Null);
            EditorUtility.CopySerialized(source.GetComponent<QualityUiImage>(), image);
            EditorUtility.CopySerialized(source.GetComponent<UiBackdropPanel>(), panel);
            typeof(UiBackdropPanel).GetField("_buttonForegroundRoot", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(panel, null);
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = Vector2.one * 0.5f;
            image.rectTransform.pivot = Vector2.one * 0.5f;
            image.rectTransform.sizeDelta = ((RectTransform)source).rect.size;
            if (selected)
            {
                // The narrow rendering runner omits menu controllers, so read their authored state
                // without instantiating scene services or substituting a second selected body.
                var yaml = MajdataPlay.IO.Storage.FileSystem.File.Open(path).ReadAllText();
                var width = Regex.Match(yaml, @"_selectedWidth: (?<v>[^\r\n]+)");
                var tint = Regex.Match(yaml, @"_selectedBackgroundColor: \{r: (?<r>[^,]+), g: (?<g>[^,]+), b: (?<b>[^,]+), a: (?<a>[^}]+)\}");
                Assert.That(width.Success && tint.Success, Is.True);
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                image.rectTransform.sizeDelta = new Vector2(float.Parse(width.Groups["v"].Value, culture), image.rectTransform.sizeDelta.y);
                image.color = new Color(float.Parse(tint.Groups["r"].Value, culture), float.Parse(tint.Groups["g"].Value, culture),
                    float.Parse(tint.Groups["b"].Value, culture), float.Parse(tint.Groups["a"].Value, culture));
            }
            Assert.That(image.rectTransform.sizeDelta.x, Is.GreaterThan(1f));
            Assert.That(image.rectTransform.sizeDelta.y, Is.GreaterThan(1f));
#else
            Assert.Ignore("This focused fixture requires the actual Editor-imported category prefabs.");
#endif
            child.SetActive(true);
            return panel;
        }

        /// <summary>Samples a covered face at its physical pixel-center coordinate, independent of integer raster phase.</summary>
        /// <param name="pixels">The premultiplied GPU readback.</param>
        /// <param name="size">The square readback dimensions.</param>
        /// <param name="point">The physical output coordinate, whose texel centers lie at half-integers.</param>
        /// <returns>The bilinearly reconstructed unpremultiplied interior color.</returns>
        private static Color SampleInteriorRgb(Color[] pixels, int size, Vector2 point)
        {
            var coordinate = point - Vector2.one * 0.5f;
            var x = Mathf.FloorToInt(coordinate.x);
            var y = Mathf.FloorToInt(coordinate.y);
            var lowerLeft = pixels[y * size + x];
            var lowerRight = pixels[y * size + x + 1];
            var upperLeft = pixels[(y + 1) * size + x];
            var upperRight = pixels[(y + 1) * size + x + 1];
            foreach (var pixel in new[] { lowerLeft, lowerRight, upperLeft, upperRight })
            {
                Assert.That(pixel.a, Is.GreaterThan(0.99f), "A reconstructed face probe must remain inside the fully covered surface.");
            }
            var color = Color.Lerp(Color.Lerp(lowerLeft, lowerRight, coordinate.x - x),
                Color.Lerp(upperLeft, upperRight, coordinate.x - x), coordinate.y - y);
            return color / color.a;
        }

        /// <summary>Averages an interior RGB probe without conflating the surface color with coverage.</summary>
        /// <param name="pixels">The premultiplied GPU readback.</param>
        /// <param name="size">The square readback dimensions.</param>
        /// <param name="center">The probe center in output pixels.</param>
        /// <param name="halfWidth">The horizontal probe radius in pixels.</param>
        /// <param name="halfHeight">The vertical probe radius in pixels.</param>
        /// <returns>The mean unpremultiplied color of the fully covered interior.</returns>
        private static Color AverageInteriorRgb(Color[] pixels, int size, Vector2 center, int halfWidth, int halfHeight)
        {
            var sum = Color.clear;
            var centerX = Mathf.RoundToInt(center.x);
            var centerY = Mathf.RoundToInt(center.y);
            for (var y = centerY - halfHeight; y <= centerY + halfHeight; y++)
            {
                for (var x = centerX - halfWidth; x <= centerX + halfWidth; x++)
                {
                    var pixel = pixels[y * size + x];
                    Assert.That(pixel.a, Is.GreaterThan(0.99f), "A face probe must remain inside the fully covered surface.");
                    sum += pixel / pixel.a;
                }
            }
            return sum / ((halfWidth * 2 + 1) * (halfHeight * 2 + 1));
        }

        /// <summary>Measures the largest finite difference between equally sized GPU readbacks.</summary>
        /// <param name="left">The first GPU image.</param>
        /// <param name="right">The comparison GPU image.</param>
        /// <param name="channels">Three for RGB or four for RGBA.</param>
        /// <returns>The maximum absolute channel difference across the images.</returns>
        private static float MaximumPixelDifference(Color[] left, Color[] right, int channels)
        {
            Assert.That(left.Length, Is.EqualTo(right.Length));
            var maximum = 0f;
            var finite = true;
            for (var index = 0; index < left.Length; index++)
            {
                for (var channel = 0; channel < channels; channel++)
                {
                    var difference = Mathf.Abs(left[index][channel] - right[index][channel]);
                    finite &= !float.IsNaN(difference) && !float.IsInfinity(difference);
                    maximum = Mathf.Max(maximum, difference);
                }
            }
            Assert.That(finite, Is.True, "GPU comparisons must not hide nonfinite pixels.");
            return maximum;
        }

        /// <summary>Checks optical changes preserve the exact finished coverage rather than making controls more transparent.</summary>
        /// <param name="left">The enabled optical draw.</param>
        /// <param name="right">The comparison draw.</param>
        /// <param name="message">The expected coverage contract.</param>
        private static void AssertAlphaEqual(Color[] left, Color[] right, string message)
        {
            Assert.That(left.Length, Is.EqualTo(right.Length));
            var maximum = 0f;
            for (var index = 0; index < left.Length; index++)
            {
                maximum = Mathf.Max(maximum, Mathf.Abs(left[index].a - right[index].a));
            }
            Assert.That(maximum, Is.Zero, message);
        }

        /// <summary>Creates a native control from the current user-info prefab or Setting value-plate data.</summary>
        /// <param name="canvas">The isolated native UGUI root.</param>
        /// <param name="spriteName">The authored Sprite whose real policy should be used.</param>
        /// <returns>The active production panel component.</returns>
        private UiBackdropPanel CreateAuthoredPanel(Canvas canvas, string spriteName)
        {
            var child = _objects.Create(spriteName, canvas.transform);
            child.SetActive(false);
            var image = child.AddComponent<QualityUiImage>();
            var panel = child.AddComponent<UiBackdropPanel>();
            var authoredAdjustmentSize = Vector2.zero;
#if UNITY_EDITOR
            if (spriteName == "newui@3x_26")
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/UserInfo.prefab");
                var source = prefab.transform;
                EditorUtility.CopySerialized(source.GetComponent<QualityUiImage>(), image);
                EditorUtility.CopySerialized(source.GetComponent<UiBackdropPanel>(), panel);
                typeof(UiBackdropPanel).GetField("_buttonForegroundRoot", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(panel, null);
            }
            else
            {
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/Ribbon/newui@3x.png"))
                {
                    if (asset is Sprite sprite && sprite.name == spriteName)
                    {
                        image.sprite = sprite;
                    }
                }
                image.material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Materials/UI/[UI]Opaque.mat");
                var scene = MajdataPlay.IO.Storage.FileSystem.File.Open("Assets/Scenes/Setting.unity").ReadAllText();
                var adjustment = spriteName == "newui@3x_17" || spriteName == "newui@3x_18";
                var componentId = spriteName == "newui@3x_22" ? "1053645856" :
                    spriteName == "newui@3x_17" ? "900000000000000015" :
                    spriteName == "newui@3x_18" ? "900000000000000014" : "900000000000000016";
                var body = Regex.Match(scene, @"^--- !u!114 &" + componentId + @"\r?\n(?<body>.*?)(?=^--- |\z)", RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
                ApplyAuthoredFields(panel, body);
                if (adjustment)
                {
                    var rectId = spriteName == "newui@3x_17" ? "1623762597" : "2074874835";
                    var rect = Regex.Match(scene, @"^--- !u!224 &" + rectId + @"\r?\n(?<body>.*?)(?=^--- |\z)", RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
                    var extent = Regex.Match(rect, @"m_SizeDelta: \{x: (?<x>[^,]+), y: (?<y>[^}]+)\}");
                    Assert.That(extent.Success, Is.True, "Read the real authored adjustment extent without running scene managers.");
                    authoredAdjustmentSize = new Vector2(
                        float.Parse(extent.Groups["x"].Value, System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(extent.Groups["y"].Value, System.Globalization.CultureInfo.InvariantCulture));
                }
            }
#else
            Assert.Ignore("This focused fixture requires the actual Editor-imported source artwork.");
#endif
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = Vector2.one * 0.5f;
            image.rectTransform.pivot = Vector2.one * 0.5f;
            image.rectTransform.sizeDelta = authoredAdjustmentSize != Vector2.zero ? authoredAdjustmentSize :
                spriteName == "newui@3x_22" ? new Vector2(1028f, 590f) :
                spriteName == "newui@3x_26" ? new Vector2(395.22f, 106.13f) : new Vector2(200f, 140f);
            child.SetActive(true);
            return panel;
        }

        /// <summary>Copies only explicitly authored primitive geometry and color policy without loading scene managers.</summary>
        /// <param name="panel">The isolated native component.</param>
        /// <param name="source">The exact current scene component YAML.</param>
        private static void ApplyAuthoredFields(UiBackdropPanel panel, string source)
        {
            foreach (var field in typeof(UiBackdropPanel).GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var match = Regex.Match(source, @"^  " + field.Name + @": (?<v>[^\r\n]+)", RegexOptions.Multiline);
                if (!match.Success)
                {
                    continue;
                }
                var value = match.Groups["v"].Value;
                if (field.FieldType == typeof(bool))
                {
                    field.SetValue(panel, value == "1");
                }
                else if (field.FieldType == typeof(float))
                {
                    field.SetValue(panel, float.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
                }
                else if (field.FieldType == typeof(Vector2) || field.FieldType == typeof(Vector4))
                {
                    var components = Regex.Matches(value, @": (?<v>[^,}]+)");
                    var vector = Vector4.zero;
                    for (var axis = 0; axis < components.Count; axis++)
                    {
                        vector[axis] = float.Parse(components[axis].Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);
                    }
                    field.SetValue(panel, field.FieldType == typeof(Vector2) ? (object)new Vector2(vector.x, vector.y) : vector);
                }
            }
        }

        /// <summary>Uses the production generator, including its actual paired-atlas padding and sliced geometry.</summary>
        /// <param name="image">The real quality-aware control.</param>
        /// <returns>An owned native UI mesh.</returns>
        private Mesh BuildMesh(QualityUiImage image)
        {
            var mesh = _objects.Track(new Mesh());
            using (var vertices = new VertexHelper())
            {
                typeof(QualityUiImage).GetMethod("OnPopulateMesh", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(VertexHelper) }, null)!
                    .Invoke(image, new object[] { vertices });
                vertices.FillMesh(mesh);
            }
            return mesh;
        }

        /// <summary>Assigns one deterministic captured ramp to every role without running scene capture or modifying resources.</summary>
        /// <param name="capture">The test-owned sampled background.</param>
        private static void SetCapture(Texture capture)
        {
            foreach (var name in new[] { "_MajPvBlurTexture", "_MajUiPanelBlurTexture", "_MajUiButtonBlurTexture" })
            {
                Shader.SetGlobalTexture(name, capture);
                Shader.SetGlobalVector(name + "_TexelSize", new Vector4(1f / capture.width, 1f / capture.height, capture.width, capture.height));
            }
            Shader.SetGlobalFloat("_MajPvBlurReady", 1f);
            Shader.SetGlobalFloat("_MajUiPanelBlurReady", 1f);
            Shader.SetGlobalFloat("_MajUiButtonBlurReady", 1f);
            Shader.SetGlobalFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
            Shader.SetGlobalVector("_ScreenParams", new Vector4(128f, 128f, 1f + 1f / 128f, 1f + 1f / 128f));
        }

        /// <summary>Exports flat and lit GPU readbacks over white for visual review without writing project assets.</summary>
        /// <param name="name">The diagnostic PNG name without its extension.</param>
        /// <param name="flat">The premultiplied reference pixels shown on the left.</param>
        /// <param name="lit">The premultiplied optical pixels shown on the right.</param>
        /// <param name="size">The square readback dimensions.</param>
        private void WriteWhiteEdgePreview(string name, Color[] flat, Color[] lit, int size)
        {
            var preview = _objects.Track(new Texture2D(size * 2, size, TextureFormat.RGBA32, false, true));
            var pixels = new Color[size * size * 2];
            var linearReadback = QualitySettings.activeColorSpace == ColorSpace.Linear;
            foreach (var side in new[] { 0, 1 })
            {
                var source = side == 0 ? flat : lit;
                for (var y = 0; y < size; y++)
                {
                    for (var x = 0; x < size; x++)
                    {
                        var color = source[y * size + x];
                        color += Color.white * (1f - color.a);
                        if (linearReadback)
                        {
                            color = color.gamma;
                        }
                        color.a = 1f;
                        pixels[y * size * 2 + x + side * size] = color;
                    }
                }
            }
            preview.SetPixels(pixels);
            preview.Apply(false, false);
            var directory = MajdataPlay.IO.Storage.FileSystem.Directory.Create(Application.temporaryCachePath)
                .CreateDirectory("MajdataPlay.Tests").CreateDirectory("GlassEdgePreview");
            var file = directory.FindFile(name + ".png") ?? directory.CreateFile(name + ".png");
            file.WriteAllBytes(preview.EncodeToPNG());
            TestContext.WriteLine($"GLASS_EDGE_PREVIEW {directory.Location}/{name}.png");
        }

        /// <summary>Draws the native UI mesh using the same logical transform while comparing only the optical implementation.</summary>
        /// <param name="image">The control whose actual texture is bound per renderer.</param>
        /// <param name="material">The production-owned glass variant.</param>
        /// <param name="mesh">The production-generated UI mesh.</param>
        /// <param name="target">The requested native MSAA attachment.</param>
        /// <param name="resolved">The explicit single-sample readback target.</param>
        /// <param name="reader">The reusable CPU readback buffer.</param>
        /// <param name="samples">The actual attachment sample count.</param>
        /// <param name="largeBoard">Whether to draw the actual large board at one logical unit per target pixel.</param>
        /// <param name="axisAligned">Whether a focused probe uses identity placement to inspect exact radial regions.</param>
        /// <returns>The finished premultiplied RGBA pixels.</returns>
        private static Color[] Draw(QualityUiImage image, Material material, Mesh mesh, RenderTexture target,
            RenderTexture resolved, Texture2D reader, int samples, bool largeBoard = false, bool axisAligned = false)
        {
            RenderTexture.active = target;
            GL.Clear(true, true, Color.clear);
            var savedVertexColorGamma = Shader.GetGlobalInt("_UIVertexColorAlwaysGammaSpace");
            GL.PushMatrix();
            try
            {
                // Raw UI meshes bypass Canvas conversion and still contain Gamma vertex colors.
                Shader.SetGlobalInt("_UIVertexColorAlwaysGammaSpace", 1);
                var halfSize = largeBoard ? target.width * 0.5f : 128f;
                GL.LoadPixelMatrix(-halfSize, halfSize, -halfSize, halfSize);
                var model = largeBoard || axisAligned ? Matrix4x4.identity : Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 0f, 17f), new Vector3(0.9f, 1.15f, 1f));
                material.SetTexture("_MainTex", image.mainTexture);
                if (material.shader.name == "Maj/UI/PV Glass")
                {
                    material.SetMatrix("_GlassWorldToLocal", model.inverse);
                }
                material.DisableKeyword("UNITY_UI_CLIP_RECT");
                Assert.That(material.SetPass(0), Is.True);
                Graphics.DrawMeshNow(mesh, model);
            }
            finally
            {
                Shader.SetGlobalInt("_UIVertexColorAlwaysGammaSpace", savedVertexColorGamma);
                GL.PopMatrix();
            }
            if (samples > 1)
            {
                target.ResolveAntiAliasedSurface(resolved);
            }
            else
            {
                Graphics.Blit(target, resolved);
            }
            RenderTexture.active = resolved;
            reader.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            reader.Apply(false, false);
            return reader.GetPixels();
        }

        /// <summary>Checks the real Login board does not filter the black outer mask into its lateral edge.</summary>
        /// <returns>The authored RT1 coverage integration and final glass pixel regression.</returns>
        [UnityTest]
        [Category("GPU")]
        public IEnumerator LoginBoard_ExcludesScreenMaskFromGlassBlur()
        {
            const int size = 1200;
            const float radius = 532f / size;
            var oldCamera = SceneSwitcher.MainCamera;
            var camera = _objects.Add<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            var source = _objects.Track(new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
            var target = _objects.Track(new RenderTexture(size, size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var resolved = _objects.Track(new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var reader = _objects.Track(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
            Assert.That(source.Create() && target.Create() && resolved.Create(), Is.True);
            camera.targetTexture = source;
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            var display = _objects.Track(new GameObject("Main_Display", typeof(RectTransform)));
            var displayRect = (RectTransform)display.transform;
            displayRect.SetParent(canvas.transform, false);
            displayRect.sizeDelta = Vector2.one * (2f * radius * size);
            try
            {
                SceneSwitcher.MainCamera = camera;
                UiOutlineTheme.Configure(UiOutlineMode.All);
                UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
                var board = CreateSerializedSurface(canvas, "Assets/Scenes/Login.unity", 900000000000000021L);
                board.gameObject.SetActive(false);
                board.transform.SetParent(displayRect, false);
                board.gameObject.SetActive(true);
                yield return null;
                Canvas.ForceUpdateCanvases();
                Assert.That(board.MainDisplayRect, Is.SameAs(displayRect));
                var capture = (IMajUiBackdropCapturePlan)MajUiBackdropBridge.GetActive(camera)!;
                Assert.That(capture.Stages, Is.EqualTo(MajUiBackdropStages.Board));
                var image = board.GetComponent<QualityUiImage>();
                var material = _objects.Track(new Material(image.materialForRendering));
                material.SetInt("_StencilComp", (int)CompareFunction.Always);
                material.SetFloat("_GlassRefraction", 0f);
                Assert.That(material.GetFloat("_BackdropUsesPanelBlur"), Is.EqualTo(1f));
                var mesh = BuildMesh(image);
                var circle = CreateCircleCapture(size, Vector2.one * 0.5f, radius, Color.white);
                using var commands = new CommandBuffer();
                capture.BeginCamera(commands, camera);
                commands.Blit(circle, source);
                capture.Record(commands, camera, source);
                Graphics.ExecuteCommandBuffer(commands);
                const string coverage = "_MajUiPanelCaptureHasCoverage";
                Assert.That(Shader.GetGlobalFloat(coverage), Is.EqualTo(1f));
                Shader.SetGlobalVector("_ScreenParams", new Vector4(size, size, 1f + 1f / size, 1f + 1f / size));
                var covered = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard: true);
                // The same blurred RGBA without normalization reproduces the black-contaminated edge.
                Shader.SetGlobalFloat(coverage, 0f);
                var unnormalized = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard: true);
                AssertAlphaEqual(covered, unnormalized, "Background validity must not change the board's silhouette.");
                var middle = size / 2 * size + size / 2;
                Assert.That(covered[middle], Is.EqualTo(unnormalized[middle]));
                var left = middle - 524;
                Assert.That(covered[left].a, Is.GreaterThan(0.99f));
                TestContext.WriteLine($"BOARD_APERTURE center={covered[middle]} edge={covered[left]} raw={unnormalized[left]} maxDelta={MaximumPixelDifference(covered, unnormalized, 3):F5}");
                Assert.That(covered[left].grayscale, Is.GreaterThan(unnormalized[left].grayscale + 0.015f),
                    "At the solid board edge, filtered RGB must be reconstructed from the circle's valid area.");
                WriteWhiteEdgePreview("LoginBoard-Mask", unnormalized, covered, size);
            }
            finally
            {
                SceneSwitcher.MainCamera = oldCamera;
            }
        }

        /// <summary>Checks the login board follows its actual clipped-arc artwork instead of a generic rounded rectangle.</summary>
        [Test]
        [Category("GPU")]
        public void LoginWindow_UsesBakedArtworkContour()
        {
            var atlas = LoadAtlas();
            var entries = (UiGlassOpticsAtlas.Entry[])typeof(UiGlassOpticsAtlas)
                .GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(atlas)!;
            var matches = 0;
            foreach (var entry in entries)
            {
                if (entry.Contour.SpriteName != "newui@3x_23")
                {
                    continue;
                }
                matches++;
                Assert.That(entry.Contour.AlphaContourThreshold, Is.EqualTo(0.5f),
                    "Precompute the original image silhouette, including the long side arcs and flat caps.");
            }
            Assert.That(matches, Is.EqualTo(1));
        }

        /// <summary>Checks login input focus preserves the baked normal and selected rims in both glass styles.</summary>
        /// <returns>The real quality-image material, focus-sprite and style-switch lifecycle regression.</returns>
        [UnityTest]
        public IEnumerator LoginInputs_FocusSpriteRetainsBothGlassStyles()
        {
#if UNITY_EDITOR
            var atlas = LoadAtlas();
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var child = _objects.Create("Authored login input", canvas.transform);
            child.SetActive(false);
            var image = child.AddComponent<QualityUiImage>();
            var panel = child.AddComponent<UiBackdropPanel>();
            var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Materials/UI/Default.mat");
            image.material = source;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 3f;
            image.rectTransform.sizeDelta = new Vector2(400f, 80f);
            Sprite? focused = null;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/Ribbon/newui@3x.png"))
            {
                if (asset is Sprite sprite)
                {
                    if (sprite.name == "newui@3x_3")
                    {
                        image.sprite = sprite;
                    }
                    else if (sprite.name == "newui@3x_4")
                    {
                        focused = sprite;
                    }
                }
            }
            Assert.That(image.sprite, Is.Not.Null);
            Assert.That(focused, Is.Not.Null);
            var yaml = MajdataPlay.IO.Storage.FileSystem.File.Open("Assets/Scenes/Login.unity").ReadAllText();
            var body = Regex.Match(yaml, @"^--- !u!114 &900000000000000101\r?\n(?<body>.*?)(?=^--- |\z)",
                RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
            Assert.That(body, Is.Not.Empty);
            ApplyAuthoredFields(panel, body);
            UiOutlineTheme.Configure(UiOutlineMode.All);
            child.SetActive(true);
            const int size = 512;
            var target = _objects.Track(new RenderTexture(size, size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var resolved = _objects.Track(new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            var reader = _objects.Track(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
            var capture = _objects.Track(new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true));
            capture.SetPixel(0, 0, new Color(0.31f, 0.33f, 0.37f, 1f));
            capture.Apply(false, false);
            Assert.That(target.Create() && resolved.Create(), Is.True);
            foreach (var style in new[] { UiBackdropStyle.GaussianBlur, UiBackdropStyle.LiquidGlass })
            {
                UiPvBackdropBlur.Configure(true, style);
                foreach (var state in new[] { focused!, image.sprite })
                {
                    image.overrideSprite = state;
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    Assert.That(panel.SourceRole, Is.EqualTo(UiBackdropSourceRole.Category));
                    Assert.That(image.material.renderQueue, Is.EqualTo(source.renderQueue));
                    Assert.That(image.material.shader.name, Is.EqualTo("Maj/UI/PV Glass"));
                    Assert.That(image.material.GetFloat("_BackdropIsNativeControl"), Is.EqualTo(1f));
                    Assert.That(panel.TryGetOpticalContour(out var contour), Is.True);
                    Assert.That(atlas.TryResolve(in contour, Vector4.one, out _, out _, out _), Is.True,
                        "The selected input background needs its own baked source identity.");
                    Assert.That(panel.RefractionReady, Is.EqualTo(style == UiBackdropStyle.LiquidGlass && UiGlassOpticsAtlas.SupportsContourFiltering));
                    Assert.That(image.SourceMaterial, Is.SameAs(source));
                    Assert.That(image.rectTransform.sizeDelta, Is.EqualTo(new Vector2(400f, 80f)));
                    var material = _objects.Track(new Material(image.materialForRendering));
                    material.SetInt("_StencilComp", (int)CompareFunction.Always);
                    var optics = material.GetVector("_LiquidGlassOptics");
                    if (style == UiBackdropStyle.LiquidGlass)
                    {
                        Assert.That(optics.z, Is.Zero);
                        Assert.That(optics.w, Is.Zero, "Both focus states must use only their authored narrow rim.");
                        Assert.That(optics.x, Is.GreaterThan(0f));
                    }
                    var mesh = BuildMesh(image);
                    SetCapture(capture);
                    Shader.SetGlobalVector("_ScreenParams", new Vector4(size, size, 1f + 1f / size, 1f + 1f / size));
                    var authoredRim = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard: true, axisAligned: true);
                    // Reinstate the narrow synthetic glint to isolate the change from the broad face lighting.
                    material.SetVector("_LiquidGlassOptics", new Vector4(optics.x, optics.y, 2.4f, 1.4f));
                    var extraRim = Draw(image, material, mesh, target, resolved, reader, 1, largeBoard: true, axisAligned: true);
                    AssertAlphaEqual(authoredRim, extraRim, "Removing the extra highlight must retain the input silhouette.");
                    var rgbChange = MaximumPixelDifference(authoredRim, extraRim, 3);
                    if (panel.RefractionReady)
                    {
                        Assert.That(rgbChange, Is.GreaterThan(0.02f));
                        var center = size / 2 * size + size / 2;
                        Assert.That(authoredRim[center], Is.EqualTo(extraRim[center]), "The correction must leave the body color unchanged.");
                        WriteWhiteEdgePreview("LoginInput-" + state.name, extraRim, authoredRim, size);
                    }
                    else
                    {
                        Assert.That(rgbChange, Is.Zero, "Gaussian and the fallback must ignore the optional contour reflection.");
                    }
                }
            }
            UiPvBackdropBlur.Configure(false);
            Assert.That(image.material, Is.SameAs(source));
#else
            Assert.Ignore("This regression loads the authored login input and Ribbon sprites.");
            yield break;
#endif
        }

        /// <summary>Resets runtime-only resources and baked lookup caches without changing imported assets.</summary>
        private static void ResetService()
        {
            typeof(UiPvBackdropBlur).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
            UiGlassOpticsAtlas.ResetCache();
        }
    }
}
