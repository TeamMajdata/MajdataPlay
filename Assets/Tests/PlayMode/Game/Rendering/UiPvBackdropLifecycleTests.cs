using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MajdataPlay.Rendering;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

#nullable enable
namespace MajdataPlay.Tests.Game.Rendering
{
    /// <summary>Checks opt-in PV panels through native UGUI visibility, pooling and shutdown.</summary>
    public sealed class UiPvBackdropLifecycleTests
    {
        /// <summary>Owns the test's Canvas and child components.</summary>
        private TestObjects _objects = null!;

        /// <summary>Creates a clean domain-reload-independent service state.</summary>
        [SetUp]
        public void SetUp()
        {
            ResetService();
            typeof(UiOutlineTheme).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
            _objects = new TestObjects();
        }

        /// <summary>Destroys registered components before clearing the remaining service state.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects?.Dispose();
            ResetService();
            typeof(UiOutlineTheme).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
        }

        /// <summary>Checks startup pools persist through style, visibility and gameplay changes and resize atomically.</summary>
        /// <returns>The native six-target ownership regression.</returns>
        [UnityTest]
        [Category("GPU")]
        public IEnumerator BackdropSources_PersistentPoolSurvivesStyleDisableAndGame()
        {
            var initialize = typeof(UiPvBackdropBlur).GetMethod("Initialize", BindingFlags.Static | BindingFlags.NonPublic)!;
            var ensure = typeof(UiPvBackdropBlur).GetMethod("EnsureTargets", BindingFlags.Static | BindingFlags.NonPublic)!;
            var begin = typeof(UiPvBackdropBlur).GetMethod("OnBeginCameraRendering", BindingFlags.Static | BindingFlags.NonPublic)!;
            initialize.Invoke(null, null);
            var names = new[] { "s_liquidCategoryTexture", "s_liquidButtonTexture", "s_boardBlurTexture", "s_categoryBlurTexture", "s_buttonBlurTexture", "s_blurScratchTexture" };
            foreach (var name in names)
            {
                Assert.That(Field<RenderTexture?>(name), Is.Not.Null, "BeforeSceneLoad must prepare both styles before any owner appears.");
            }
            Assert.That((bool)ensure.Invoke(null, new object[] { new Vector2Int(128, 96) })!, Is.True);
            var retained = new RenderTexture[names.Length];
            for (var index = 0; index < names.Length; index++)
            {
                retained[index] = Field<RenderTexture>(names[index]);
                Assert.That(retained[index].width, Is.EqualTo(index < 2 ? 64 : 32));
                Assert.That(retained[index].height, Is.EqualTo(index < 2 ? 48 : 24));
                Assert.That(retained[index].antiAliasing, Is.EqualTo(1));
                Assert.That(retained[index].depth, Is.Zero);
                Assert.That(retained[index].useMipMap, Is.False);
            }
            Assert.That(new HashSet<RenderTexture>(retained).Count, Is.EqualTo(6));
            var oldCamera = SceneSwitcher.MainCamera;
            var oldScene = SceneSwitcher.CurrentScene;
            var scene = typeof(SceneSwitcher).GetProperty(nameof(SceneSwitcher.CurrentScene), BindingFlags.Static | BindingFlags.Public)!;
            try
            {
                var camera = _objects.Add<Camera>();
                camera.enabled = false;
                SceneSwitcher.MainCamera = camera;
                var panel = CreatePanel();
                var image = panel.GetComponent<QualityUiImage>();
                image.canvas.renderMode = RenderMode.ScreenSpaceCamera;
                image.canvas.worldCamera = camera;
                foreach (var style in new[] { UiBackdropStyle.GaussianBlur, UiBackdropStyle.LiquidGlass })
                {
                    UiPvBackdropBlur.Configure(true, style);
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    Assert.That(MajUiBackdropBridge.GetActive(camera), Is.Not.Null);
                    scene.SetValue(null, MajScenes.Game);
                    foreach (var ready in new[] { "_MajPvBlurReady", "_MajUiPanelBlurReady", "_MajUiButtonBlurReady" })
                    {
                        Shader.SetGlobalFloat(ready, 1f);
                    }
                    begin.Invoke(null, new object[] { default(UnityEngine.Rendering.ScriptableRenderContext), camera });
                    Assert.That(MajUiBackdropBridge.GetActive(camera), Is.Null, "Game must never request menu capture even when its top cards survive.");
                    foreach (var ready in new[] { "_MajPvBlurReady", "_MajUiPanelBlurReady", "_MajUiButtonBlurReady" })
                    {
                        Assert.That(Shader.GetGlobalFloat(ready), Is.Zero);
                    }
                    scene.SetValue(null, oldScene);
                    UiPvBackdropBlur.Configure(false, style);
                    panel.enabled = false;
                    for (var index = 0; index < names.Length; index++)
                    {
                        Assert.That(Field<RenderTexture>(names[index]), Is.SameAs(retained[index]), "Disable, Game and unregistration must retain the preallocated pools.");
                    }
                    panel.enabled = true;
                }
                Assert.That((bool)ensure.Invoke(null, new object[] { new Vector2Int(97, 65) })!, Is.True);
                for (var index = 0; index < names.Length; index++)
                {
                    var current = Field<RenderTexture>(names[index]);
                    Assert.That(current, Is.Not.SameAs(retained[index]));
                    Assert.That(current.width, Is.EqualTo(index < 2 ? 49 : 25));
                    Assert.That(current.height, Is.EqualTo(index < 2 ? 33 : 17));
                }
                Assert.That(Field<Vector2Int>("s_sourceSize"), Is.EqualTo(new Vector2Int(97, 65)), "The pool key must retain the full source extent.");
                Assert.That(Field<Vector2Int>("s_halfResolutionSize"), Is.EqualTo(new Vector2Int(49, 33)));
                Assert.That(Field<Vector2Int>("s_gaussianResolutionSize"), Is.EqualTo(new Vector2Int(25, 17)));
                var resized = new RenderTexture[names.Length];
                for (var index = 0; index < names.Length; index++)
                {
                    resized[index] = Field<RenderTexture>(names[index]);
                }
                Assert.That((bool)ensure.Invoke(null, new object[] { new Vector2Int(97, 65) })!, Is.True);
                for (var index = 0; index < names.Length; index++)
                {
                    Assert.That(Field<RenderTexture>(names[index]), Is.SameAs(resized[index]), "Repeated source descriptors must reuse all six targets.");
                }
                yield return null;
                foreach (var old in retained)
                {
                    Assert.That(old == null, Is.True, "Only descriptor changes retire native pool targets.");
                }
                var final = Field<RenderTexture>("s_boardBlurTexture");
                ResetService();
                Assert.That(Field<RenderTexture?>("s_boardBlurTexture"), Is.Null);
                yield return null;
                Assert.That(final == null, Is.True, "Runtime reset/quit owns final destruction.");
            }
            finally
            {
                scene.SetValue(null, oldScene);
                SceneSwitcher.MainCamera = oldCamera;
            }
        }

        /// <summary>Checks all three ordered captures use distinct persistent outputs and style-specific real GPU filters.</summary>
        /// <returns>The independent-source and borrowed-attachment regression.</returns>
        [UnityTest]
        [Category("GPU")]
        public IEnumerator BackdropSources_ThreeStagesKeepIndependentSnapshotsAndStyleFilters()
        {
            var oldCamera = SceneSwitcher.MainCamera;
            var previousTarget = RenderTexture.active;
            try
            {
                var camera = _objects.Add<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                SceneSwitcher.MainCamera = camera;
                var board = CreatePanel();
                typeof(UiBackdropPanel).GetField("_useStrongBlur", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(board, true);
                var category = CreatePanel();
                typeof(UiBackdropPanel).GetField("_isCategory", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(category, true);
                var resources = Resources.Load<UiOutlineResources>("Rendering/UI/OutlineTheme")!;
                var button = CreateButtonPanel(resources.AlphaMaterial!, out _, out _, out _);
                foreach (var panel in new[] { board, category, button })
                {
                    var canvas = panel.GetComponent<QualityUiImage>().canvas.rootCanvas;
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                }
                var source = _objects.Track(new RenderTexture(64, 48, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point });
                Assert.That(source.Create(), Is.True);
                var pattern = _objects.Track(new Texture2D(64, 48, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point });
                var pixels = new Color[64 * 48];
                for (var y = 0; y < 48; y++)
                {
                    for (var x = 0; x < 64; x++)
                    {
                        pixels[y * 64 + x] = (x >= 32) ^ (y >= 24) ? Color.white : Color.black;
                    }
                }
                pattern.SetPixels(pixels);
                pattern.Apply(false, false);
                var reader = _objects.Track(new Texture2D(1, 1, TextureFormat.RGBA32, false, true));
                var boardReader = _objects.Track(new Texture2D(16, 12, TextureFormat.RGBA32, false, true));
                var rawReader = _objects.Track(new Texture2D(32, 24, TextureFormat.RGBA32, false, true));
                var interpolated = _objects.Track(new RenderTexture(1, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
                Assert.That(interpolated.Create(), Is.True);
                Color[]? gaussianBoard = null;
                foreach (var style in new[] { UiBackdropStyle.GaussianBlur, UiBackdropStyle.LiquidGlass })
                {
                    UiPvBackdropBlur.Configure(true, style);
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    var capture = MajUiBackdropBridge.GetActive(camera);
                    Assert.That(capture, Is.Not.Null);
                    var categories = MajUiBackdropBridge.GetActiveCategories(capture, camera);
                    var buttons = MajUiBackdropBridge.GetActiveButtons(capture, camera);
                    Assert.That(categories, Is.Not.Null);
                    Assert.That(buttons, Is.Not.Null);
                    using (var commands = new UnityEngine.Rendering.CommandBuffer())
                    {
                        commands.Blit(pattern, source);
                        capture!.Record(commands, camera, source);
                        categories!.RecordCategories(commands, camera, source);
                        buttons!.RecordButtons(commands, camera, source);
                        Graphics.ExecuteCommandBuffer(commands);
                        var first = (RenderTexture)Shader.GetGlobalTexture("_MajUiPanelBlurTexture");
                        var middle = (RenderTexture)Shader.GetGlobalTexture("_MajPvBlurTexture");
                        var last = (RenderTexture)Shader.GetGlobalTexture("_MajUiButtonBlurTexture");
                        Assert.That(first, Is.SameAs(Field<RenderTexture>("s_boardBlurTexture")));
                        Assert.That(middle, Is.SameAs(Field<RenderTexture>(style == UiBackdropStyle.GaussianBlur ? "s_categoryBlurTexture" : "s_liquidCategoryTexture")));
                        Assert.That(last, Is.SameAs(Field<RenderTexture>(style == UiBackdropStyle.GaussianBlur ? "s_buttonBlurTexture" : "s_liquidButtonTexture")));
                        Assert.That(new HashSet<RenderTexture> { first, middle, last }.Count, Is.EqualTo(3));
                        Assert.That(first.width, Is.EqualTo(16));
                        Assert.That(middle.width, Is.EqualTo(style == UiBackdropStyle.GaussianBlur ? 16 : 32));
                        Assert.That(first.height, Is.EqualTo(12));
                        Assert.That(middle.height, Is.EqualTo(style == UiBackdropStyle.GaussianBlur ? 12 : 24));
                        Assert.That(last.width, Is.EqualTo(middle.width));
                        Assert.That(last.height, Is.EqualTo(middle.height));
                        var firstEdge = ReadBlurTexel(first, reader, first.width / 2 - 1, first.height / 8).r;
                        var middleEdge = ReadBlurTexel(middle, reader, middle.width / 2 - 1, middle.height / 8).r;
                        var lastEdge = ReadBlurTexel(last, reader, last.width / 2 - 1, last.height / 8).r;
                        Assert.That(firstEdge, Is.InRange(0.3f, 0.7f), "Both board styles must retain the same broad screen-space Gaussian.");
                        var beforeReadback = RenderTexture.active;
                        RenderTexture.active = first;
                        boardReader.ReadPixels(new Rect(0, 0, first.width, first.height), 0, 0);
                        boardReader.Apply(false, false);
                        RenderTexture.active = beforeReadback;
                        var boardPixels = boardReader.GetPixels();
                        if (style == UiBackdropStyle.GaussianBlur)
                        {
                            gaussianBoard = boardPixels;
                            Assert.That(middleEdge, Is.EqualTo(firstEdge).Within(1f / 255f));
                            Assert.That(lastEdge, Is.EqualTo(firstEdge).Within(1f / 255f));
                        }
                        else
                        {
                            Assert.That(gaussianBoard, Is.Not.Null);
                            var maximumBoardDifference = 0f;
                            for (var pixel = 0; pixel < boardPixels.Length; pixel++)
                            {
                                for (var channel = 0; channel < 4; channel++)
                                {
                                    maximumBoardDifference = Mathf.Max(maximumBoardDifference,
                                        Mathf.Abs(boardPixels[pixel][channel] - gaussianBoard![pixel][channel]));
                                }
                            }
                            Assert.That(maximumBoardDifference, Is.LessThanOrEqualTo(1f / 255f),
                                "Liquid boards must use the identical Gaussian capture kernel, rather than only a bilinear resize.");
                            TestContext.WriteLine($"BOARD_STYLE_CAPTURE maxDifference={maximumBoardDifference:F7} pixels={boardPixels.Length}");
                            Assert.That(middleEdge, Is.LessThan(0.05f), "Liquid categories use only a half-size linear resize without a broad Gaussian.");
                            Assert.That(lastEdge, Is.EqualTo(middleEdge).Within(1f / 255f));
                            Assert.That(middle.filterMode, Is.EqualTo(FilterMode.Bilinear));
                            Assert.That(last.filterMode, Is.EqualTo(FilterMode.Bilinear));
                            foreach (var raw in new[] { middle, last })
                            {
                                RenderTexture.active = raw;
                                rawReader.ReadPixels(new Rect(0, 0, 32, 24), 0, 0);
                                rawReader.Apply(false, false);
                                var actualPixels = rawReader.GetPixels();
                                for (var y = 0; y < raw.height; y++)
                                {
                                    for (var x = 0; x < raw.width; x++)
                                    {
                                        var sourcePixel = y * 2 * 64 + x * 2;
                                        var expected = (pixels[sourcePixel] + pixels[sourcePixel + 1] +
                                            pixels[sourcePixel + 64] + pixels[sourcePixel + 65]) * 0.25f;
                                        Assert.That(actualPixels[y * raw.width + x], Is.EqualTo(expected),
                                            "Exact 2:1 linear reduction must average each source 2x2 footprint, including orientation and borders.");
                                    }
                                }
                            }
                            commands.Clear();
                            commands.Blit(middle, interpolated);
                            Graphics.ExecuteCommandBuffer(commands);
                            Assert.That(ReadBlurTexel(interpolated, reader, 0, 0).r, Is.EqualTo(0.5f).Within(1f / 255f),
                                "The published raw snapshot must interpolate the four checker quadrants even when its borrowed source is Point filtered.");
                        }
                        TestContext.WriteLine($"THREE_SOURCE style={style} sizes={first.width}x{first.height}/{middle.width}x{middle.height}/{last.width}x{last.height} edge={firstEdge:F6},{middleEdge:F6},{lastEdge:F6}");
                        var preserved = ReadBlurTexel(first, reader, first.width / 2 - 1, first.height / 8);
                        commands.Clear();
                        commands.SetRenderTarget(source);
                        commands.ClearRenderTarget(false, true, Color.green);
                        categories.RecordCategories(commands, camera, source);
                        Graphics.ExecuteCommandBuffer(commands);
                        Assert.That(ReadBlurTexel(first, reader, first.width / 2 - 1, first.height / 8), Is.EqualTo(preserved));
                        commands.Clear();
                        commands.SetRenderTarget(source);
                        commands.ClearRenderTarget(false, true, Color.blue);
                        buttons.RecordButtons(commands, camera, source);
                        Graphics.ExecuteCommandBuffer(commands);
                        Assert.That(ReadBlurTexel(middle, reader, middle.width / 2, middle.height / 2), Is.EqualTo(Color.green));
                        Assert.That(ReadBlurTexel(last, reader, last.width / 2, last.height / 2), Is.EqualTo(Color.blue));
                        Assert.That(ReadBlurTexel(first, reader, first.width / 2 - 1, first.height / 8), Is.EqualTo(preserved));
                        foreach (var ready in new[] { "_MajPvBlurReady", "_MajUiPanelBlurReady", "_MajUiButtonBlurReady" })
                        {
                            Assert.That(Shader.GetGlobalFloat(ready), Is.EqualTo(1f), "Later stages cannot invalidate an independent earlier snapshot.");
                        }
                        foreach (var textureName in new[] { "_MajUiPanelBlurTexture", "_MajPvBlurTexture", "_MajUiButtonBlurTexture" })
                        {
                            var published = (RenderTexture)Shader.GetGlobalTexture(textureName);
                            Assert.That(Shader.GetGlobalVector(textureName + "_TexelSize"), Is.EqualTo(new Vector4(1f / published.width, 1f / published.height, published.width, published.height)));
                        }
                        Assert.That(Shader.GetGlobalFloat("_MajUiSourceNeedsManualLinear"), Is.Zero);
                        Assert.That(source.IsCreated(), Is.True);
                    }
                }
            }
            finally
            {
                RenderTexture.active = previousTarget;
                SceneSwitcher.MainCamera = oldCamera;
            }
        }

        /// <summary>Checks liquid half-size snapshots against independent bilinear footprints for even and odd source extents.</summary>
        /// <returns>The real category/peripheral capture and published-size regression.</returns>
        [UnityTest]
        [Category("GPU")]
        public IEnumerator BackdropSources_LiquidHalfSnapshotsMatchBilinearSourceFootprints()
        {
            var previousCamera = SceneSwitcher.MainCamera;
            var previousTarget = RenderTexture.active;
            try
            {
                var camera = _objects.Add<Camera>();
                camera.enabled = false;
                SceneSwitcher.MainCamera = camera;
                var category = CreatePanel();
                typeof(UiBackdropPanel).GetField("_isCategory", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(category, true);
                var resources = Resources.Load<UiOutlineResources>("Rendering/UI/OutlineTheme")!;
                var button = CreateButtonPanel(resources.AlphaMaterial!, out _, out _, out _);
                foreach (var panel in new[] { category, button })
                {
                    var canvas = panel.GetComponent<QualityUiImage>().canvas.rootCanvas;
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                }
                UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
                yield return null;
                Canvas.ForceUpdateCanvases();
                var capture = MajUiBackdropBridge.GetActive(camera);
                Assert.That(capture, Is.Not.Null);
                var categories = MajUiBackdropBridge.GetActiveCategories(capture, camera)!;
                var buttons = MajUiBackdropBridge.GetActiveButtons(capture, camera)!;
                foreach (var size in new[] { new Vector2Int(64, 48), new Vector2Int(97, 65) })
                {
                    var half = new Vector2Int((size.x + 1) / 2, (size.y + 1) / 2);
                    var source = _objects.Track(new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                    {
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp
                    });
                    var sourceLinear = _objects.Track(new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
                    var resultLinear = _objects.Track(new RenderTexture(half.x, half.y, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
                    Assert.That(source.Create() && sourceLinear.Create() && resultLinear.Create(), Is.True);
                    var pattern = _objects.Track(new Texture2D(size.x, size.y, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point });
                    var pixels = new Color[size.x * size.y];
                    for (var y = 0; y < size.y; y++)
                    {
                        for (var x = 0; x < size.x; x++)
                        {
                            pixels[y * size.x + x] = new Color((x % 3) * 0.5f, (y % 5) * 0.25f,
                                ((x ^ y) & 1), 0.25f + (x + y) % 4 * 0.25f);
                        }
                    }
                    pattern.SetPixels(pixels);
                    pattern.Apply(false, false);
                    var sourceReader = _objects.Track(new Texture2D(size.x, size.y, TextureFormat.RGBAFloat, false, true));
                    var resultReader = _objects.Track(new Texture2D(half.x, half.y, TextureFormat.RGBAFloat, false, true));
                    using (var commands = new UnityEngine.Rendering.CommandBuffer())
                    {
                        commands.Blit(pattern, source);
                        capture!.Record(commands, camera, source);
                        categories.RecordCategories(commands, camera, source);
                        buttons.RecordButtons(commands, camera, source);
                        Graphics.ExecuteCommandBuffer(commands);
                    }
                    Graphics.Blit(source, sourceLinear);
                    RenderTexture.active = sourceLinear;
                    sourceReader.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                    sourceReader.Apply(false, false);
                    var actualSource = sourceReader.GetPixels();
                    var maximumDifference = 0f;
                    foreach (var name in new[] { "_MajPvBlurTexture", "_MajUiButtonBlurTexture" })
                    {
                        var snapshot = (RenderTexture)Shader.GetGlobalTexture(name);
                        Assert.That(snapshot.width, Is.EqualTo(half.x));
                        Assert.That(snapshot.height, Is.EqualTo(half.y));
                        Assert.That(Shader.GetGlobalVector(name + "_TexelSize"), Is.EqualTo(new Vector4(1f / half.x, 1f / half.y, half.x, half.y)));
                        Graphics.Blit(snapshot, resultLinear);
                        RenderTexture.active = resultLinear;
                        resultReader.ReadPixels(new Rect(0, 0, half.x, half.y), 0, 0);
                        resultReader.Apply(false, false);
                        var actual = resultReader.GetPixels();
                        for (var y = 0; y < half.y; y++)
                        {
                            for (var x = 0; x < half.x; x++)
                            {
                                var sourceX = (x + 0.5f) * size.x / half.x - 0.5f;
                                var sourceY = (y + 0.5f) * size.y / half.y - 0.5f;
                                var expected = BilinearFootprint(actualSource, size, sourceX, sourceY);
                                for (var channel = 0; channel < 4; channel++)
                                {
                                    maximumDifference = Mathf.Max(maximumDifference, Mathf.Abs(actual[y * half.x + x][channel] - expected[channel]));
                                }
                            }
                        }
                    }
                    Assert.That(maximumDifference, Is.LessThanOrEqualTo(2f / 255f), "The half-size raw pass must match bilinear source footprints within LDR storage precision, without another Gaussian.");
                    Assert.That(source.filterMode, Is.EqualTo(FilterMode.Point), "Capture cannot mutate the borrowed attachment's sampler.");
                    Assert.That(Field<Vector2Int>("s_sourceSize"), Is.EqualTo(size));
                    TestContext.WriteLine($"LIQUID_HALF source={size.x}x{size.y} target={half.x}x{half.y} maxBilinearError={maximumDifference:F8} exact2x2={(size.x % 2 == 0 && size.y % 2 == 0)} colorSpace={QualitySettings.activeColorSpace}");
                }
            }
            finally
            {
                RenderTexture.active = previousTarget;
                SceneSwitcher.MainCamera = previousCamera;
            }
        }

        /// <summary>Evaluates a bilinear clamp footprint from actual source readback rather than reproducing the capture shader.</summary>
        /// <param name="pixels">The source's linear GPU color readback.</param>
        /// <param name="size">The positive source dimensions.</param>
        /// <param name="x">The continuous horizontal source texel-center coordinate.</param>
        /// <param name="y">The continuous vertical source texel-center coordinate.</param>
        /// <returns>The independently interpolated color, including alpha.</returns>
        private static Color BilinearFootprint(Color[] pixels, Vector2Int size, float x, float y)
        {
            var left = Mathf.FloorToInt(x);
            var bottom = Mathf.FloorToInt(y);
            var x0 = Mathf.Clamp(left, 0, size.x - 1);
            var x1 = Mathf.Clamp(left + 1, 0, size.x - 1);
            var y0 = Mathf.Clamp(bottom, 0, size.y - 1);
            var y1 = Mathf.Clamp(bottom + 1, 0, size.y - 1);
            return Color.Lerp(Color.Lerp(pixels[y0 * size.x + x0], pixels[y0 * size.x + x1], x - left),
                Color.Lerp(pixels[y1 * size.x + x0], pixels[y1 * size.x + x1], x - left), y - bottom);
        }

        /// <summary>Checks peripheral captures preserve actual black pixels and reuse the existing six-target pool in both styles.</summary>
        /// <returns>The real ordered capture, source-color and retained-pool regression.</returns>
        [UnityTest]
        [Category("GPU")]
        public IEnumerator PeripheralCapture_PreservesCapturedBlackAndSixTargetPool()
        {
            var previousCamera = SceneSwitcher.MainCamera;
            var previousTarget = RenderTexture.active;
            try
            {
                var camera = _objects.Add<Camera>();
                camera.enabled = false;
                SceneSwitcher.MainCamera = camera;
                var resources = Resources.Load<UiOutlineResources>("Rendering/UI/OutlineTheme")!;
                CreateButtonPanel(resources.AlphaMaterial!, out var body, out _, out _);
                body.canvas.renderMode = RenderMode.ScreenSpaceCamera;
                body.canvas.worldCamera = camera;
                var source = _objects.Track(new RenderTexture(128, 96, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
                Assert.That(source.Create(), Is.True);
                var reader = _objects.Track(new Texture2D(1, 1, TextureFormat.RGBA32, false, true));
                RenderTexture[]? retained = null;
                foreach (var style in new[] { UiBackdropStyle.GaussianBlur, UiBackdropStyle.LiquidGlass })
                {
                    UiPvBackdropBlur.Configure(true, style);
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    var capture = MajUiBackdropBridge.GetActive(camera);
                    Assert.That(capture, Is.Not.Null);
                    var buttons = MajUiBackdropBridge.GetActiveButtons(capture, camera);
                    Assert.That(buttons, Is.Not.Null);
                    using (var commands = new UnityEngine.Rendering.CommandBuffer())
                    {
                        commands.SetRenderTarget(source);
                        commands.ClearRenderTarget(false, true, Color.black);
                        capture!.Record(commands, camera, source);
                        buttons!.RecordButtons(commands, camera, source);
                        Graphics.ExecuteCommandBuffer(commands);
                        Assert.That(Shader.GetGlobalFloat("_MajUiButtonBlurReady"), Is.EqualTo(1f));
                        var actual = (RenderTexture)Shader.GetGlobalTexture("_MajUiButtonBlurTexture");
                        Assert.That(ReadBlurTexel(actual, reader, actual.width / 2, actual.height / 2), Is.EqualTo(Color.black),
                            "The early mask's black capture must remain available to peripheral consumers without replacement.");
                        var names = new[] { "s_liquidCategoryTexture", "s_liquidButtonTexture", "s_boardBlurTexture", "s_categoryBlurTexture", "s_buttonBlurTexture", "s_blurScratchTexture" };
                        var current = new RenderTexture[names.Length];
                        for (var index = 0; index < names.Length; index++)
                        {
                            current[index] = Field<RenderTexture>(names[index]);
                            if (retained != null)
                            {
                                Assert.That(current[index], Is.SameAs(retained[index]), "Source color policy must not rebuild the six existing capture targets.");
                            }
                        }
                        retained = current;
                        Assert.That(new HashSet<RenderTexture>(current).Count, Is.EqualTo(6));
                        commands.Clear();
                        buttons.RecordButtons(commands, camera, source);
                        Graphics.ExecuteCommandBuffer(commands);
                        Assert.That(Shader.GetGlobalTexture("_MajUiButtonBlurTexture"), Is.SameAs(actual));
                        Assert.That(source.IsCreated(), Is.True);
                    }
                }
                UiPvBackdropBlur.Configure(false);
                Assert.That(Shader.GetGlobalFloat("_MajUiButtonBlurReady"), Is.Zero);
                ResetService();
                Assert.That(Shader.GetGlobalFloat("_MajUiButtonBlurReady"), Is.Zero);
            }
            finally
            {
                SceneSwitcher.MainCamera = previousCamera;
                RenderTexture.active = previousTarget;
            }
        }

        /// <summary>Checks black transmission stays black rather than being replaced by authored fill for any consumer role.</summary>
        /// <param name="liquid">Whether the existing local liquid-glass program is selected.</param>
        [TestCase(false)]
        [TestCase(true)]
        [Category("GPU")]
        public void PeripheralCapture_ConsumersSampleBlackWithoutArtworkSubstitution(bool liquid)
        {
            var oldTarget = RenderTexture.active;
            var oldGuiZTest = Shader.GetGlobalInt("unity_GUIZTestMode");
            try
            {
                var target = _objects.Track(new RenderTexture(48, 48, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
                Assert.That(target.Create(), Is.True);
                var reader = _objects.Track(new Texture2D(48, 48, TextureFormat.RGBA32, false, true));
                var fill = new Color(0.4f, 0.6f, 0.8f, 1f);
                var tint = new Color(0.8f, 0.6f, 0.4f, 1f);
                var artwork = _objects.Track(new Texture2D(1, 1, TextureFormat.RGBA32, false, true));
                artwork.SetPixel(0, 0, fill);
                artwork.Apply(false, false);
                var material = _objects.Track(new Material(Shader.Find("Maj/UI/PV Glass")));
                material.SetTexture("_MainTex", artwork);
                material.SetTexture("_PanelBaseTex", artwork);
                material.SetFloat("_FillAlpha", 1f);
                material.SetFloat("_ReferenceOpacity", 1f);
                material.SetFloat("_BackdropIsNativeControl", 1f);
                material.SetFloat("_TintStrength", 0f);
                material.SetFloat("_LiquidTintStrength", 0f);
                material.SetFloat("_GaussianButtonTintStrength", 0f);
                material.SetFloat("_FrostStrength", 0f);
                material.SetFloat("_GlassHighlight", 0f);
                material.SetMatrix("_GlassWorldToLocal", Matrix4x4.identity);
                material.SetVector("_LiquidGlassRect", new Vector4(0.5f, 0.5f, 2f, 2f));
                if (liquid)
                {
                    material.EnableKeyword("MAJ_UI_LIQUID_GLASS");
                }
                else
                {
                    material.DisableKeyword("MAJ_UI_LIQUID_GLASS");
                }
                Shader.SetGlobalInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
                foreach (var name in new[] { "_MajPvBlurTexture", "_MajUiPanelBlurTexture", "_MajUiButtonBlurTexture" })
                {
                    Shader.SetGlobalTexture(name, Texture2D.blackTexture);
                }
                foreach (var name in new[] { "_MajPvBlurReady", "_MajUiPanelBlurReady", "_MajUiButtonBlurReady" })
                {
                    Shader.SetGlobalFloat(name, 1f);
                }
                var mesh = _objects.Track(new Mesh());
                mesh.vertices = new[] { Vector3.zero, Vector3.up, new Vector3(1f, 1f, 0f), Vector3.right };
                mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
                mesh.colors = new[] { tint, tint, tint, tint };
                mesh.triangles = new[] { 0, 1, 2, 2, 3, 0 };
                mesh.RecalculateBounds();
                for (var role = 0; role < 3; role++)
                {
                    material.SetFloat("_BackdropIsButton", role == 0 ? 1f : 0f);
                    material.SetFloat("_BackdropIsCategory", role == 1 ? 1f : 0f);
                    material.SetFloat("_BackdropUsesPanelBlur", role == 2 ? 1f : 0f);
                    foreach (var share in new[] { 0f, 0.25f })
                    {
                        material.SetFloat("_LiquidArtworkBlend", share);
                        var actual = DrawCapturedBlackConsumer(material, mesh, target, reader);
                        var expected = fill * tint * share;
                        expected.a = 1f;
                        var maximum = 0f;
                        foreach (var pixel in actual)
                        {
                            for (var channel = 0; channel < 4; channel++)
                            {
                                maximum = Mathf.Max(maximum, Mathf.Abs(pixel[channel] - expected[channel]));
                            }
                        }
                        Assert.That(maximum, Is.LessThanOrEqualTo(1f / 255f + 0.00001f),
                            "Every source coordinate must retain black transmission plus only the explicitly configured artwork share.");
                        TestContext.WriteLine($"BLACK_CAPTURE liquid={liquid} role={role} artworkShare={share} pixels={actual.Length} maxError={maximum:F8}");
                    }
                }
            }
            finally
            {
                Shader.SetGlobalInt("unity_GUIZTestMode", oldGuiZTest);
                RenderTexture.active = oldTarget;
            }
        }
        /// <summary>Draws the actual production glass pass without adding a scene capture or substitute optical shader.</summary>
        /// <param name="material">The production glass material for one role and style.</param>
        /// <param name="mesh">The colored native quad used to test tint composition.</param>
        /// <param name="target">The owned single-sample output.</param>
        /// <param name="reader">The reused full-size readback texture.</param>
        /// <returns>The actual premultiplied output pixels.</returns>
        private static Color[] DrawCapturedBlackConsumer(Material material, Mesh mesh, RenderTexture target, Texture2D reader)
        {
            RenderTexture.active = target;
            GL.Clear(true, true, Color.clear);
            GL.PushMatrix();
            try
            {
                GL.LoadOrtho();
                Assert.That(material.SetPass(0), Is.True);
                Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
            }
            finally
            {
                GL.PopMatrix();
            }
            reader.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            reader.Apply(false, false);
            return reader.GetPixels();
        }

        /// <summary>Reads one actual filtered texel without changing the surrounding capture fixture's render target.</summary>
        /// <param name="source">The real owned filtered snapshot.</param>
        /// <param name="reader">The reusable one-pixel readback texture.</param>
        /// <param name="x">The source texel's horizontal coordinate.</param>
        /// <param name="y">The source texel's vertical coordinate.</param>
        /// <returns>The GPU-filtered color at the requested location.</returns>
        private static Color ReadBlurTexel(RenderTexture source, Texture2D reader, int x, int y)
        {
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = source;
                reader.ReadPixels(new Rect(x, y, 1, 1), 0, 0);
                reader.Apply(false, false);
                return reader.GetPixel(0, 0);
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }
        /// <summary>Checks liquid contours follow moving native UI without changing Gaussian mapping or source artwork.</summary>
        /// <returns>The Simple/Sliced panel, category and peripheral-button geometry coroutine without drawing pixels.</returns>
        [UnityTest]
        public IEnumerator BackdropStyle_LiquidGeometryTracksMovingSimpleSlicedAndPeripheralControls()
        {
            var setting = MajdataPlay.IO.Storage.FileSystem.File.Open("Assets/Scenes/Setting.unity").ReadAllText();
            var actualOption = System.Text.RegularExpressions.Regex.Match(setting, @"^--- !u!114 &900000000000000016\r?\n(?<body>.*?)(?=^--- |\z)",
                System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Singleline).Groups["body"].Value;
            var centerData = System.Text.RegularExpressions.Regex.Match(actualOption, @"_liquidCircleCenter: \{x: (?<x>[^,]+), y: (?<y>[^}]+)\}");
            var optionCenter = new Vector2(float.Parse(centerData.Groups["x"].Value, System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(centerData.Groups["y"].Value, System.Globalization.CultureInfo.InvariantCulture));
            var optionRadius = ReadAuthoredScalar(actualOption, "_liquidCircleRadius");
            var optionAspect = ReadAuthoredScalar(actualOption, "_liquidCircleAspect");
            var optionSmoothness = ReadAuthoredScalar(actualOption, "_liquidClipSmoothness");
            foreach (var role in new[] { 0, 1, 2, 3, 4, 5, 6 })
            {
                foreach (var type in new[] { UnityEngine.UI.Image.Type.Simple, UnityEngine.UI.Image.Type.Sliced })
                {
                    var panel = CreatePanel();
                    var image = panel.GetComponent<QualityUiImage>();
                    image.sprite = LoadImportedSprite(role == 0 ? "newui@3x_22" : role == 1 ? "newui@3x_6" : role == 6 ? "newui@3x_16" : "newui@3x_21");
                    image.type = type;
                    image.preserveAspect = false;
                    image.rectTransform.sizeDelta = role == 0 ? new Vector2(512f, 256f) : new Vector2(128f, 64f);
                    image.rectTransform.pivot = new Vector2(0.3f, 0.6f);
                    image.rectTransform.localScale = new Vector3(1.1f, 0.8f, 1f);
                    typeof(UiBackdropPanel).GetField("_isCategory", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .SetValue(panel, role == 1);
                    typeof(UiBackdropPanel).GetField("_isButton", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .SetValue(panel, role == 2);
                    typeof(UiBackdropPanel).GetField("_liquidEllipse", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .SetValue(panel, role == 3);
                    typeof(UiBackdropPanel).GetField("_liquidCapsule", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .SetValue(panel, role == 4 || role == 5);
                    typeof(UiBackdropPanel).GetField("_usePeripheralBackdrop", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .SetValue(panel, role == 5);
                    if (role == 6)
                    {
                        typeof(UiBackdropPanel).GetField("_liquidClippedCircle", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(panel, true);
                        typeof(UiBackdropPanel).GetField("_liquidBodyInsets", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(panel,
                            new Vector4(0f, 0f, 0f, 1.5f / 958.5f));
                        typeof(UiBackdropPanel).GetField("_liquidCircleCenter", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(panel,
                            optionCenter);
                        typeof(UiBackdropPanel).GetField("_liquidCircleRadius", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(panel,
                            optionRadius);
                        typeof(UiBackdropPanel).GetField("_liquidCircleAspect", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(panel, optionAspect);
                        typeof(UiBackdropPanel).GetField("_liquidClipSmoothness", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(panel, optionSmoothness);
                    }
                    if (role == 2 || role == 6)
                    {
                        var maskObject = _objects.Create("Moving contour stencil mask", image.transform.parent);
                        maskObject.AddComponent<UnityEngine.UI.Image>();
                        maskObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
                        image.transform.SetParent(maskObject.transform, false);
                    }
                    UiPvBackdropBlur.Configure(true);
                    var gaussian = image.material;
                    if (role == 5)
                    {
                        Assert.That(gaussian.shader.name, Is.EqualTo("Maj/UI/PV Glass"));
                        Assert.That(gaussian.renderQueue, Is.EqualTo(MajUiBackdropBridge.ButtonBodyQueue));
                        Assert.That(gaussian.GetFloat("_BackdropIsButton"), Is.EqualTo(1f));
                    }
                    var oldMapping = gaussian.GetVector("_GlassUvToLocal");
                    var oldShape = gaussian.GetVector("_GlassShape");
                    var source = image.SourceMaterial;
                    var sprite = image.sprite;
                    UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
                    var liquid = image.material;
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    // Native UGUI resolves the themed source during its material rebuild. Read the
                    // current owned variant after that rebuild rather than retaining a replaced clone.
                    liquid = image.material;
                    Assert.That(liquid.GetMatrix("_GlassWorldToLocal"), Is.EqualTo(image.rectTransform.worldToLocalMatrix));
                    var beforeMove = liquid.GetMatrix("_GlassWorldToLocal");
                    var originalSize = image.rectTransform.sizeDelta;
                    image.rectTransform.anchoredPosition += new Vector2(37f, -21f);
                    image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 17f);
                    image.rectTransform.sizeDelta += new Vector2(20f, 12f);
                    Canvas.ForceUpdateCanvases();
                    var actualMatrix = liquid.GetMatrix("_GlassWorldToLocal");
                    Assert.That(actualMatrix, Is.EqualTo(image.rectTransform.worldToLocalMatrix));
                    Assert.That(actualMatrix, Is.Not.EqualTo(beforeMove), "Canvas callbacks must update the contour after animation.");
                    var rect = image.GetPixelAdjustedRect();
                    Assert.That(liquid.GetVector("_LiquidGlassRect"),
                        Is.EqualTo(new Vector4(rect.center.x, rect.center.y, rect.width * 0.5f, rect.height * 0.5f)));
                    var shape = liquid.GetVector("_LiquidGlassShape");
                    AssertFinite(shape);
                    Assert.That(shape.x, Is.GreaterThan(0f));
                    Assert.That(shape.y, Is.EqualTo(role == 2 || role == 3 ? 1f : 0f));
                    if (role == 5)
                    {
                        Assert.That(panel.IsButton, Is.False, "A peripheral source must not change an adjustment capsule into an outer circular button.");
                        Assert.That(panel.SourceRole, Is.EqualTo(UiBackdropSourceRole.Peripheral));
                        Assert.That(liquid.renderQueue, Is.EqualTo(MajUiBackdropBridge.ButtonBodyQueue));
                        Assert.That(liquid.GetFloat("_BackdropIsButton"), Is.EqualTo(1f), "The shader must actually select RT3 while keeping capsule geometry.");
                    }
                    if (role == 6)
                    {
                        Assert.That(panel.SourceRole, Is.EqualTo(UiBackdropSourceRole.Category));
                        Assert.That(liquid.GetFloat("_BackdropIsButton"), Is.Zero);
                        Assert.That(shape.z, Is.EqualTo(1f));
                        Assert.That(shape.w, Is.EqualTo(1f), "The real padded OptionBG follows original Sprite coordinates for Simple and Sliced UI.");
                        Assert.That(UiAtlasMeshGenerator.TryGetLegacy(image.sprite, out var legacy, out _), Is.True);
                        var size = legacy.rect.size;
                        var sourceRect = liquid.GetVector("_LiquidGlassSpriteRect");
                        var circle = liquid.GetVector("_LiquidGlassCircle");
                        Assert.That(circle.x, Is.EqualTo(optionCenter.x * size.x - sourceRect.x).Within(0.0001f));
                        Assert.That(circle.y, Is.EqualTo(optionCenter.y * size.y - sourceRect.y).Within(0.0001f));
                        Assert.That(circle.z, Is.EqualTo(optionRadius * size.x).Within(0.0001f));
                        Assert.That(circle.w, Is.EqualTo(circle.z * optionAspect).Within(0.0001f));
                        Assert.That(liquid.GetFloat("_LiquidGlassClipSmoothness"), Is.EqualTo(optionSmoothness * size.x).Within(0.0001f));
                        Assert.That(circle.y, Is.GreaterThan(0f), "The circle's measured upward offset must remain independent of its flat crop planes.");
                    }
                    var shortRadius = Mathf.Min(rect.width, rect.height) * 0.5f;
                    var rootCanvas = image.canvas.rootCanvas;
                    var toCanvas = rootCanvas.transform.worldToLocalMatrix * image.rectTransform.localToWorldMatrix;
                    var smallerScale = Mathf.Min(toCanvas.MultiplyVector(Vector3.right).magnitude,
                        toCanvas.MultiplyVector(Vector3.up).magnitude);
                    var units = Mathf.Min(1.6f / Mathf.Max(smallerScale, 0.0001f), shortRadius * 2f / 48f);
                    var expectedWidth = role == 0 || role == 2 || role == 6
                        ? Mathf.Min(20f * units, shortRadius * 0.4f) : shortRadius * 0.9f;
                    var optics = liquid.GetVector("_LiquidGlassOptics");
                    Assert.That(optics.x, Is.EqualTo(expectedWidth).Within(0.0001f),
                        "Ordinary boards and peripheral buttons retain the same narrow-cap policy; only non-peripheral categories and explicit ellipse/capsule controls use 0.9R.");
                    Assert.That(optics.y, Is.EqualTo(expectedWidth * 1.2f).Within(0.0001f));
                    Assert.That(optics.z, Is.EqualTo(1.5f * units).Within(0.0001f), "The wider lens must not enlarge the fine white reflection.");
                    var expectedTail = role == 2 ? 0.22f : 0f;
                    Assert.That(liquid.GetFloat("_LiquidRefractionTail"), Is.EqualTo(expectedTail),
                        "Only outer circular buttons extend displacement through the body; shared RT3 alone does not enable a tail.");
                    Assert.That(image.canvasRenderer.materialCount, Is.GreaterThan(0));
                    var rendered = image.canvasRenderer.GetMaterial(0);
                    Assert.That(rendered.GetMatrix("_GlassWorldToLocal"), Is.EqualTo(actualMatrix));
                    Assert.That(rendered.GetVector("_LiquidGlassOptics"), Is.EqualTo(optics));
                    Assert.That(rendered.GetFloat("_LiquidRefractionTail"), Is.EqualTo(expectedTail),
                        "The actual stencil material must receive the same peripheral-only tail strength.");
                    if (role == 2 || role == 6)
                    {
                        Assert.That(rendered, Is.Not.SameAs(liquid), "Resize must reach the actual Canvas stencil variant too.");
                    }
                    if (role == 6)
                    {
                        var circle = liquid.GetVector("_LiquidGlassCircle");
                        var join = liquid.GetFloat("_LiquidGlassClipSmoothness");
                        Assert.That(rendered.GetVector("_LiquidGlassCircle"), Is.EqualTo(circle));
                        Assert.That(rendered.GetFloat("_LiquidGlassClipSmoothness"), Is.EqualTo(join));
                        typeof(UiBackdropPanel).GetField("_liquidCircleAspect", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(panel, optionAspect * 0.99f);
                        typeof(UiBackdropPanel).GetField("_liquidClipSmoothness", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(panel, optionSmoothness * 0.75f);
                        Canvas.ForceUpdateCanvases();
                        Assert.That(image.material, Is.SameAs(liquid));
                        Assert.That(image.canvasRenderer.GetMaterial(0), Is.SameAs(rendered));
                        Assert.That(rendered.GetVector("_LiquidGlassCircle").w, Is.EqualTo(circle.w * 0.99f).Within(0.0001f));
                        Assert.That(rendered.GetFloat("_LiquidGlassClipSmoothness"), Is.EqualTo(join * 0.75f).Within(0.0001f));
                        typeof(UiBackdropPanel).GetField("_liquidCircleAspect", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(panel, optionAspect);
                        typeof(UiBackdropPanel).GetField("_liquidClipSmoothness", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(panel, optionSmoothness);
                        Canvas.ForceUpdateCanvases();
                    }
                    var widthScaleField = typeof(UiBackdropPanel).GetField("_liquidRefractionWidthScale", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    Assert.That((float)widthScaleField.GetValue(panel)!, Is.EqualTo(1f), "Unconfigured controls must preserve their established width.");
                    foreach (var widthScale in new[] { 1f, 1.5f, 2f })
                    {
                        widthScaleField.SetValue(panel, widthScale);
                        Canvas.ForceUpdateCanvases();
                        Assert.That(image.material, Is.SameAs(liquid), "Width changes update geometry uniforms without rebuilding the owned material.");
                        var scaled = liquid.GetVector("_LiquidGlassOptics");
                        Assert.That(scaled.x, Is.EqualTo(expectedWidth * widthScale).Within(0.0001f));
                        Assert.That(scaled.y, Is.EqualTo(expectedWidth * widthScale * 1.2f).Within(0.0001f));
                        Assert.That(scaled.z, Is.EqualTo(optics.z), "Card width scaling must not widen the fine white reflection.");
                        Assert.That(scaled.w, Is.EqualTo(optics.w), "Card width scaling must not change reflection softness.");
                        var scaledRendered = image.canvasRenderer.GetMaterial(0);
                        Assert.That(scaledRendered, Is.SameAs(rendered));
                        Assert.That(scaledRendered.GetVector("_LiquidGlassOptics"), Is.EqualTo(scaled),
                            "The actual rendered stencil variant must receive the same width and thickness updates.");
                    }
                    widthScaleField.SetValue(panel, 1f);
                    Assert.That(image.SourceMaterial, Is.SameAs(source));
                    Assert.That(image.sprite, Is.SameAs(sprite));
                    image.rectTransform.sizeDelta = originalSize;
                    UiPvBackdropBlur.Configure(true);
                    var restored = image.material;
                    Assert.That(restored.GetVector("_GlassUvToLocal"), Is.EqualTo(oldMapping));
                    Assert.That(restored.GetVector("_GlassShape"), Is.EqualTo(oldShape));
                    panel.enabled = false;
                }
            }
        }

        /// <summary>Reads one explicitly authored numeric contour parameter without instantiating scene managers.</summary>
        /// <param name="component">The exact source YAML component.</param>
        /// <param name="name">The serialized scalar field to verify.</param>
        /// <returns>The source's invariant-culture scalar value.</returns>
        private static float ReadAuthoredScalar(string component, string name)
        {
            var value = System.Text.RegularExpressions.Regex.Match(component, @"^  " + name + @": (?<v>[^\r\n]+)", System.Text.RegularExpressions.RegexOptions.Multiline);
            Assert.That(value.Success, Is.True, "The current contour must explicitly serialize " + name);
            return float.Parse(value.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        /// <summary>Checks liquid-only artwork keeps Gaussian rendering unchanged and samples its explicitly chosen interior.</summary>
        /// <returns>The style-gating and authored glyph-anchor lifecycle coroutine.</returns>
        [UnityTest]
        public IEnumerator BackdropStyle_LiquidOnlyGlyphsPreserveGaussianAndUseTheirAuthoredFillAnchor()
        {
            var panel = CreatePanel();
            var image = panel.GetComponent<QualityUiImage>();
            image.sprite = LoadImportedSprite("newui@3x_6");
            image.color = new Color(0.8f, 0.9f, 1f, 0.85f);
            typeof(UiBackdropPanel).GetField("_liquidOnly", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(panel, true);
            typeof(UiBackdropPanel).GetField("_liquidEllipse", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(panel, true);
            typeof(UiBackdropPanel).GetField("_fillSamplePosition", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(panel, new Vector2(0.73f, 0.29f));
            var source = image.SourceMaterial;
            UiPvBackdropBlur.Configure(true);
            var themed = UiOutlineTheme.GetMaterial(source);
            Assert.That(panel.SupportsCurrentStyle, Is.False);
            Assert.That(panel.ResolveMaterial(themed), Is.SameAs(themed));
            Assert.That(panel.ResolveBackgroundMaterial(source), Is.SameAs(source),
                "A liquid-only owner must not move its bound background before the Gaussian capture.");
            Assert.That(Field<bool>("s_subscribed"), Is.False,
                "A liquid-only surface must not request a capture in the existing Gaussian style.");
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            Assert.That(panel.SupportsCurrentStyle, Is.True);
            Assert.That(Field<bool>("s_subscribed"), Is.True);
            var background = panel.ResolveBackgroundMaterial(source);
            Assert.That(background, Is.Not.SameAs(source));
            Assert.That(background.renderQueue, Is.EqualTo(MajUiBackdropBridge.BackgroundQueueUpperBound));
            var material = image.material;
            Assert.That(material.shader.name, Is.EqualTo("Maj/UI/PV Glass"));
            var uv = UnityEngine.Sprites.DataUtility.GetOuterUV(image.sprite);
            var expected = new Vector4(Mathf.Lerp(uv.x, uv.z, 0.73f), Mathf.Lerp(uv.y, uv.w, 0.29f), 0f, 0f);
            Assert.That(material.GetVector("_FillSampleUV"), Is.EqualTo(expected));
            Assert.That(material.GetVector("_LiquidGlassShape").y, Is.EqualTo(1f));
            Assert.That(image.SourceMaterial, Is.SameAs(source));
            Assert.That(image.color, Is.EqualTo(new Color(0.8f, 0.9f, 1f, 0.85f)));
            UiPvBackdropBlur.Configure(true);
            Assert.That(image.material.shader.name, Is.Not.EqualTo("Maj/UI/PV Glass"));
            Assert.That(panel.SupportsCurrentStyle, Is.False);
            Assert.That(panel.ResolveBackgroundMaterial(source), Is.SameAs(source),
                "Returning to Gaussian must restore the authored bound-background order.");
            yield return null;
        }

        /// <summary>Checks secondary top cards may own liquid capture without changing the Gaussian style activation policy.</summary>
        /// <returns>The camera-bound reuse-consumer style transition coroutine without rendering or texture readback.</returns>
        [UnityTest]
        public IEnumerator BackdropStyle_ReuseOnlyTopCardsCanOwnLiquidCaptureWithoutChangingGaussian()
        {
            var originalCamera = SceneSwitcher.MainCamera;
            var camera = _objects.Add<Camera>();
            camera.enabled = false;
            try
            {
                SceneSwitcher.MainCamera = camera;
                var panel = CreatePanel();
                var image = panel.GetComponent<QualityUiImage>();
                image.sprite = LoadImportedSprite("newui@3x_26");
                image.type = UnityEngine.UI.Image.Type.Sliced;
                typeof(UiBackdropPanel).GetField("_reusePanelCaptureOnly", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(panel, true);
                var canvas = image.canvas.rootCanvas;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                UiPvBackdropBlur.Configure(true);
                yield return null;
                Canvas.ForceUpdateCanvases();
                Assert.That(panel.IsVisible, Is.True);
                Assert.That(panel.ReusesPanelCaptureOnly, Is.True);
                Assert.That(MajUiBackdropBridge.GetActive(camera), Is.Null);
                Assert.That(Field<RenderTexture?>("s_liquidCategoryTexture"), Is.Not.Null);
                UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
                Canvas.ForceUpdateCanvases();
                Assert.That(panel.ReusesPanelCaptureOnly, Is.False);
                var capture = MajUiBackdropBridge.GetActive(camera);
                Assert.That(capture, Is.Not.Null, "Liquid top cards can request their PV-only board snapshot.");
                Assert.That(MajUiBackdropBridge.GetActiveButtons(capture, camera), Is.Null);
                Assert.That(Field<RenderTexture?>("s_liquidCategoryTexture"), Is.Not.Null,
                    "The persistent pool exists before owner selection or capture.");
                UiPvBackdropBlur.Configure(true);
                Assert.That(MajUiBackdropBridge.GetActive(camera), Is.Null);
                Assert.That(panel.ReusesPanelCaptureOnly, Is.True);
                Assert.That(Field<bool>("s_subscribed"), Is.False);
            }
            finally
            {
                SceneSwitcher.MainCamera = originalCamera;
            }
        }

        /// <summary>Checks pooled panels register exactly once and leave no camera subscription after their last disable.</summary>
        /// <returns>The Play Mode lifecycle coroutine.</returns>
        [UnityTest]
        public IEnumerator Pooling_RegistersOnceAndUnregistersOnDisable()
        {
            var panel = CreatePanel();
            yield return null;
            Assert.That(Panels.Count, Is.EqualTo(1));
            Assert.That(Panels[0], Is.SameAs(panel));
            UiPvBackdropBlur.Register(panel);
            Assert.That(Panels.Count, Is.EqualTo(1));
            panel.gameObject.SetActive(false);
            yield return null;
            Assert.That(Panels, Is.Empty);
            Assert.That(Field<bool>("s_subscribed"), Is.False);
            Assert.That(Shader.GetGlobalFloat("_MajPvBlurReady"), Is.Zero);
            panel.gameObject.SetActive(true);
            yield return null;
            Assert.That(Panels.Count, Is.EqualTo(1));
            Assert.That(Panels[0], Is.SameAs(panel));
            panel.enabled = false;
            yield return null;
            Assert.That(Panels, Is.Empty);
            for (var cycle = 0; cycle < 2; cycle++)
            {
                panel.enabled = true;
                yield return null;
                Assert.That(Panels.Count, Is.EqualTo(1));
                Assert.That(Panels[0], Is.SameAs(panel));
                panel.enabled = false;
                yield return null;
                Assert.That(Panels, Is.Empty);
            }
        }

        /// <summary>Checks transparent Images, hidden CanvasGroups and disabled Canvases cannot keep filtering active.</summary>
        /// <returns>The Play Mode lifecycle coroutine.</returns>
        [UnityTest]
        public IEnumerator Visibility_FollowsNativeCanvasAndImageOpacity()
        {
            var panel = CreatePanel();
            var image = panel.GetComponent<QualityUiImage>();
            var canvas = image.canvas;
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.That(panel.IsVisible, Is.True);
            image.color = new Color(1f, 1f, 1f, 0f);
            Assert.That(panel.IsVisible, Is.False);
            image.color = Color.white;
            var group = canvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.That(panel.IsVisible, Is.False);
            group.alpha = 1f;
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.That(panel.IsVisible, Is.True);
            canvas.enabled = false;
            Assert.That(panel.IsVisible, Is.False);
            canvas.enabled = true;
            image.enabled = false;
            Assert.That(panel.IsVisible, Is.False);
        }

        /// <summary>Checks disabling the optional service leaves source material selection and borrowed textures untouched.</summary>
        /// <returns>The Play Mode lifecycle coroutine.</returns>
        [UnityTest]
        public IEnumerator DisabledService_LeavesAuthoredMaterialAndReleasesOwnedState()
        {
            var panel = CreatePanel();
            var image = panel.GetComponent<QualityUiImage>();
            var source = image.SourceMaterial;
            yield return null;
            Assert.That(panel.ResolveMaterial(source), Is.SameAs(source));
            UiPvBackdropBlur.Configure(true);
            Assert.That(UiPvBackdropBlur.Enabled, Is.True);
            Assert.That(Field<bool>("s_subscribed"), Is.True);
            UiPvBackdropBlur.Configure(false);
            Assert.That(UiPvBackdropBlur.Enabled, Is.False);
            Assert.That(Field<bool>("s_subscribed"), Is.False);
            Assert.That(Field<RenderTexture?>("s_liquidCategoryTexture"), Is.Not.Null);
            Assert.That(Field<RenderTexture?>("s_liquidButtonTexture"), Is.Not.Null);
            Assert.That(Field<Material?>("s_blurMaterial"), Is.Null);
            Assert.That(panel.ResolveMaterial(source), Is.SameAs(source));
            Assert.That(image.SourceMaterial, Is.SameAs(source));
            Assert.That(Shader.GetGlobalFloat("_MajPvBlurReady"), Is.Zero);
        }

        /// <summary>Checks the real panel slices produce finite logical shapes and cannot retain another slice's cached variant.</summary>
        /// <returns>The Play Mode material and Sprite Swap regression coroutine.</returns>
        [UnityTest]
        public IEnumerator AuthoredShapes_RebuildMaterialWhenPanelSpriteChanges()
        {
            var panel = CreatePanel();
            var image = panel.GetComponent<QualityUiImage>();
            var firstSprite = LoadAuthoredSprite("newui@3x_22");
            var secondSprite = LoadAuthoredSprite("newui@3x_19");
            image.sprite = firstSprite;
            UiPvBackdropBlur.Configure(true);
            var first = panel.ResolveMaterial(image.SourceMaterial);
            Assert.That(first.shader.name, Is.EqualTo("Maj/UI/PV Glass"));
            var firstShape = first.GetVector("_GlassShape");
            AssertFinite(firstShape);
            AssertFinite(first.GetVector("_GlassUvToLocal"));
            Assert.That(firstShape.w, Is.EqualTo(1f));
            Assert.That(firstShape.x, Is.EqualTo(firstSprite.rect.width * 0.5f).Within(0.001f));
            Assert.That(firstShape.y, Is.EqualTo(firstSprite.rect.height * 0.5f).Within(0.001f));
            Assert.That(firstShape.z, Is.GreaterThan(0f).And.LessThan(Mathf.Min(firstShape.x, firstShape.y)));

            image.sprite = secondSprite;
            var second = panel.ResolveMaterial(image.SourceMaterial);
            Assert.That(second, Is.Not.SameAs(first), "A new base material also invalidates UGUI's cached stencil variants.");
            var secondShape = second.GetVector("_GlassShape");
            AssertFinite(secondShape);
            AssertFinite(second.GetVector("_GlassUvToLocal"));
            Assert.That(secondShape.w, Is.EqualTo(1f));
            Assert.That(secondShape.x, Is.EqualTo(secondSprite.rect.width * 0.5f).Within(0.001f));
            Assert.That(secondShape.y, Is.EqualTo(secondSprite.rect.height * 0.5f).Within(0.001f));
            Assert.That(secondShape, Is.Not.EqualTo(firstShape));
            Assert.That(second.GetTexture("_PanelBaseTex"), Is.SameAs(firstSprite.texture));
            yield return null;
        }

        /// <summary>Checks piecewise atlas mappings disable only the analytic approximation, retaining the glass sprite composition.</summary>
        /// <returns>The Play Mode Image geometry regression coroutine.</returns>
        [UnityTest]
        public IEnumerator UnsupportedGeometry_DisablesAnalyticShapeWithoutChangingArtworkMaterial()
        {
            var panel = CreatePanel();
            var image = panel.GetComponent<QualityUiImage>();
            image.sprite = LoadAuthoredSprite("newui@3x_22");
            UiPvBackdropBlur.Configure(true);
            var simple = panel.ResolveMaterial(image.SourceMaterial);
            Assert.That(simple.GetVector("_GlassShape").w, Is.EqualTo(1f));

            image.type = UnityEngine.UI.Image.Type.Sliced;
            var sliced = panel.ResolveMaterial(image.SourceMaterial);
            Assert.That(sliced.GetVector("_GlassShape").w, Is.Zero);
            Assert.That(sliced.shader.name, Is.EqualTo("Maj/UI/PV Glass"));
            image.type = UnityEngine.UI.Image.Type.Tiled;
            var tiled = panel.ResolveMaterial(image.SourceMaterial);
            Assert.That(tiled.GetVector("_GlassShape").w, Is.Zero);
            Assert.That(tiled.shader.name, Is.EqualTo("Maj/UI/PV Glass"));

            image.type = UnityEngine.UI.Image.Type.Simple;
            image.useSpriteMesh = true;
            var tightMesh = panel.ResolveMaterial(image.SourceMaterial);
            Assert.That(tightMesh.GetVector("_GlassShape").w, Is.Zero);
            Assert.That(tightMesh.shader.name, Is.EqualTo("Maj/UI/PV Glass"));
            image.useSpriteMesh = false;
            Assert.That(panel.ResolveMaterial(image.SourceMaterial).GetVector("_GlassShape").w, Is.EqualTo(1f));
            yield return null;
        }

        /// <summary>Checks real Sliced artwork supplies its own interior palette without changing opacity or color.</summary>
        /// <returns>The Play Mode material-policy and atlas-anchor regression coroutine.</returns>
        [UnityTest]
        public IEnumerator GlassTint_UsesSpriteInteriorUvWithoutBakingAColorPalette()
        {
            var panel = CreatePanel();
            var image = panel.GetComponent<QualityUiImage>();
            image.sprite = LoadAuthoredSprite("newui@3x_22");
            image.type = UnityEngine.UI.Image.Type.Sliced;
            image.color = new Color(0.8f, 0.9f, 1f, 0.85f);
            UiPvBackdropBlur.Configure(true);
            var material = panel.ResolveMaterial(image.SourceMaterial);
            var uv = UnityEngine.Sprites.DataUtility.GetOuterUV(image.sprite);
            var anchor = material.GetVector("_FillSampleUV");
            Assert.That(anchor.x, Is.EqualTo((uv.x + uv.z) * 0.5f).Within(0.00001f));
            Assert.That(anchor.y, Is.EqualTo((uv.y + uv.w) * 0.5f).Within(0.00001f));
            Assert.That(material.GetFloat("_TintStrength"), Is.EqualTo(0.4f));
            Assert.That(material.GetFloat("_FrostStrength"), Is.EqualTo(0.08f));
            Assert.That(material.HasProperty("_GlassTint"), Is.False);
            Assert.That(material.HasProperty("_FillColor"), Is.False);
            Assert.That(material.GetFloat("_ReferenceOpacity"), Is.EqualTo(1f));
            Assert.That(image.color, Is.EqualTo(new Color(0.8f, 0.9f, 1f, 0.85f)));
            Assert.That(image.SourceMaterial, Is.Not.SameAs(material));
            image.type = UnityEngine.UI.Image.Type.Simple;
            var simple = panel.ResolveMaterial(image.SourceMaterial);
            Assert.That(simple.GetVector("_FillSampleUV"), Is.EqualTo(anchor),
                "Interior sampling is independent of Sliced versus Simple surface geometry.");
            yield return null;
        }

        /// <summary>Checks inherited artwork follows source UV/color changes on both native materials without rebuilding them.</summary>
        /// <returns>The actual Canvas callback and stencil-material binding regression coroutine.</returns>
        [UnityTest]
        public IEnumerator LiquidLayerTint_SourceBindingRefreshesOwnedAndStencilWithoutReplacingMaterials()
        {
            var modeField = typeof(UiOutlineTheme).GetField("s_mode", BindingFlags.NonPublic | BindingFlags.Static)!;
            var previousMode = (UiOutlineMode)modeField.GetValue(null)!;
            try
            {
                UiOutlineTheme.Configure(UiOutlineMode.All);
                var parent = CreatePanel();
                var parentImage = parent.GetComponent<QualityUiImage>();
                parentImage.sprite = LoadImportedSprite("newui@3x_22");
                parentImage.color = new Color(0.8f, 0.9f, 1f, 0.85f);
                var parentSource = _objects.Track(new Material(parentImage.SourceMaterial));
                parentSource.SetVector("_Color", new Vector4(0.7f, 0.8f, 0.9f, 1f));
                parentImage.material = parentSource;
                var anchorField = typeof(UiBackdropPanel).GetField("_fillSamplePosition", BindingFlags.NonPublic | BindingFlags.Instance)!;
                anchorField.SetValue(parent, new Vector2(0.25f, 0.75f));
                var child = CreatePanel();
                var childImage = child.GetComponent<QualityUiImage>();
                childImage.sprite = LoadImportedSprite("newui@3x_21");
                childImage.color = new Color(0.3f, 0.35f, 0.4f, 0.85f);
                var sourceField = typeof(UiBackdropPanel).GetField("_liquidTintSource", BindingFlags.NonPublic | BindingFlags.Instance)!;
                sourceField.SetValue(child, parent);
                var maskObject = _objects.Create("Layered tint native mask", childImage.transform.parent);
                maskObject.AddComponent<UnityEngine.UI.Image>();
                maskObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
                childImage.transform.SetParent(maskObject.transform, false);
                UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
                yield return null;
                Canvas.ForceUpdateCanvases();
                var material = childImage.material;
                var stencil = childImage.materialForRendering;
                Assert.That(stencil, Is.Not.SameAs(material), "The fixture must exercise a real Canvas-owned stencil variant.");
                Assert.That(material.GetFloat("_LiquidTintStrength"), Is.EqualTo(0.9f));
                Assert.That(material.GetFloat("_LiquidArtworkBlend"), Is.EqualTo(0.25f));
                foreach (var current in new[] { material, stencil })
                {
                    Assert.That(current.GetTexture("_LiquidTintSourceTex"), Is.SameAs(parentImage.mainTexture));
                    var uv = UnityEngine.Sprites.DataUtility.GetOuterUV(parentImage.overrideSprite);
                    Assert.That(current.GetVector("_LiquidTintSourceUV"), Is.EqualTo(new Vector4(
                        Mathf.Lerp(uv.x, uv.z, 0.25f), Mathf.Lerp(uv.y, uv.w, 0.75f), 0f, 0f)));
                    var graphic = QualitySettings.activeColorSpace == ColorSpace.Linear ? parentImage.color.linear : parentImage.color;
                    var tint = parentImage.material.GetVector("_Color");
                    Assert.That(current.GetVector("_LiquidTintSourceColor"), Is.EqualTo(new Vector4(
                        graphic.r * tint.x, graphic.g * tint.y, graphic.b * tint.z, 0.14f)));
                }
                var previousAnchor = material.GetVector("_LiquidTintSourceUV");
                parentImage.sprite = LoadImportedSprite("newui@3x_21");
                parentImage.color = new Color(0.25f, 0.85f, 0.35f, 0.2f);
                anchorField.SetValue(parent, new Vector2(0.75f, 0.25f));
                Canvas.ForceUpdateCanvases();
                yield return null;
                Canvas.ForceUpdateCanvases();
                Assert.That(childImage.material, Is.SameAs(material));
                Assert.That(childImage.materialForRendering, Is.SameAs(stencil));
                Assert.That(material.GetVector("_LiquidTintSourceUV"), Is.Not.EqualTo(previousAnchor));
                foreach (var current in new[] { material, stencil })
                {
                    Assert.That(current.GetTexture("_LiquidTintSourceTex"), Is.SameAs(parentImage.mainTexture));
                    var graphic = QualitySettings.activeColorSpace == ColorSpace.Linear ? parentImage.color.linear : parentImage.color;
                    var tint = parentImage.material.GetVector("_Color");
                    Assert.That(current.GetVector("_LiquidTintSourceColor"), Is.EqualTo(new Vector4(
                        graphic.r * tint.x, graphic.g * tint.y, graphic.b * tint.z, 0.14f)),
                        "Changing the parent's alpha must not alter its inherited RGB weight.");
                }
                foreach (var invalidSource in new UiBackdropPanel?[] { parent, child, null })
                {
                    parent.enabled = false;
                    sourceField.SetValue(child, invalidSource);
                    Canvas.ForceUpdateCanvases();
                    foreach (var current in new[] { material, stencil })
                    {
                        Assert.That(current.GetTexture("_LiquidTintSourceTex"), Is.SameAs(Texture2D.whiteTexture));
                        Assert.That(current.GetVector("_LiquidTintSourceColor"), Is.EqualTo(new Vector4(1f, 1f, 1f, 0f)),
                            "Disabled, self and missing sources must safely preserve the child's own filter.");
                    }
                    Assert.That(childImage.material, Is.SameAs(material));
                    Assert.That(childImage.materialForRendering, Is.SameAs(stencil));
                }
                Assert.That(childImage.color, Is.EqualTo(new Color(0.3f, 0.35f, 0.4f, 0.85f)));
            }
            finally
            {
                UiOutlineTheme.Configure(previousMode);
            }
        }

        /// <summary>Checks scalar changes refresh native variants without baking or retaining an earlier palette.</summary>
        /// <returns>The Play Mode material-cache refresh regression coroutine.</returns>
        [UnityTest]
        public IEnumerator GlassTint_RefreshMaterialReplacesAnExistingVariantAfterPolicyChanges()
        {
            var panel = CreatePanel();
            var image = panel.GetComponent<QualityUiImage>();
            image.sprite = LoadImportedSprite("newui@3x_22");
            image.color = new Color(1f, 1f, 1f, 0.85f);
            var tintField = typeof(UiBackdropPanel).GetField("_tintStrength", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var frostField = typeof(UiBackdropPanel).GetField("_frostStrength", BindingFlags.NonPublic | BindingFlags.Instance)!;
            UiPvBackdropBlur.Configure(true);
            var source = image.SourceMaterial;
            var previous = panel.ResolveMaterial(source);
            var previousAnchor = previous.GetVector("_FillSampleUV");
            var previousReference = previous.GetFloat("_ReferenceOpacity");
            tintField.SetValue(panel, 1f);
            panel.RefreshMaterial();
            var updated = panel.ResolveMaterial(source);
            Assert.That(updated, Is.Not.SameAs(previous), "Tint strength refreshes the native material and its stencil variants.");
            Assert.That(updated.GetFloat("_TintStrength"), Is.EqualTo(1f));
            frostField.SetValue(panel, 0.02f);
            panel.RefreshMaterial();
            var frosted = panel.ResolveMaterial(source);
            Assert.That(frosted, Is.Not.SameAs(updated), "Frost strength is also part of the material cache key.");
            Assert.That(frosted.GetFloat("_FrostStrength"), Is.EqualTo(0.02f));
            Assert.That(image.SourceMaterial, Is.SameAs(source));
            Assert.That(frosted.GetVector("_FillSampleUV"), Is.EqualTo(previousAnchor));
            Assert.That(frosted.GetFloat("_ReferenceOpacity"), Is.EqualTo(previousReference));
            Assert.That(image.color.a, Is.EqualTo(0.85f));
            panel.RefreshMaterial();
            Assert.That(panel.ResolveMaterial(source), Is.SameAs(frosted), "Unchanged scalars keep the reusable variant.");
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            var liquid = panel.ResolveMaterial(source);
            Assert.That(liquid.GetFloat("_LiquidArtworkBlend"), Is.EqualTo(0.25f));
            Assert.That(liquid.GetFloat("_LiquidTintStrength"), Is.EqualTo(0.9f));
            Assert.That(liquid.HasProperty("_LiquidFrostStrength"), Is.False);
            var artworkBlendField = typeof(UiBackdropPanel).GetField("_liquidArtworkBlend", BindingFlags.NonPublic | BindingFlags.Instance)!;
            artworkBlendField.SetValue(panel, 0.5f);
            panel.RefreshMaterial();
            var artworkMixed = panel.ResolveMaterial(source);
            Assert.That(artworkMixed, Is.Not.SameAs(liquid), "The RGB artwork share participates in material-cache invalidation.");
            Assert.That(artworkMixed.GetFloat("_LiquidArtworkBlend"), Is.EqualTo(0.5f));
            Assert.That(artworkMixed.GetFloat("_FrostStrength"), Is.EqualTo(0.02f), "Liquid artwork mixing must not overwrite the Gaussian veil policy.");
            Assert.That(artworkMixed.GetFloat("_ReferenceOpacity"), Is.EqualTo(previousReference));
            panel.RefreshMaterial();
            Assert.That(panel.ResolveMaterial(source), Is.SameAs(artworkMixed), "An unchanged artwork share reuses its owned material.");
            Assert.That(image.SourceMaterial, Is.SameAs(source));
            yield return null;
            Assert.That(previous == null && updated == null && liquid == null, Is.True, "Replaced runtime variants are released after the frame.");
        }

        /// <summary>Checks the authored resting opacity remains the stable fade reference rather than a permanent glass transparency.</summary>
        /// <returns>The Play Mode material-reference and native opacity coroutine.</returns>
        [UnityTest]
        public IEnumerator AuthoredRestOpacity_NormalizesGlassWithoutNormalizingAnimationFades()
        {
            var panel = CreatePanel();
            var image = panel.GetComponent<QualityUiImage>();
            image.sprite = LoadAuthoredSprite("newui@3x_22");
            image.color = new Color(1f, 1f, 1f, 0.85f);
            typeof(UiBackdropPanel).GetField("_referenceOpacity", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(panel, 0.85f);
            UiPvBackdropBlur.Configure(true);
            var themed = UiOutlineTheme.GetMaterial(image.SourceMaterial);
            var material = panel.ResolveMaterial(themed);
            Assert.That(material.GetFloat("_ReferenceOpacity"), Is.EqualTo(0.85f).Within(0.0001f));
            Assert.That(material.GetFloat("_FillAlpha"), Is.EqualTo(128f / 255f).Within(0.0001f));
            Assert.That(image.color.a, Is.EqualTo(0.85f));

            image.color = new Color(1f, 1f, 1f, 0.425f);
            var group = image.canvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0.5f;
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.That(panel.ResolveMaterial(themed), Is.SameAs(material));
            Assert.That(material.GetFloat("_ReferenceOpacity"), Is.EqualTo(0.85f).Within(0.0001f),
                "A later Image or CanvasGroup fade must not become the new resting reference.");
            Assert.That(image.color.a, Is.EqualTo(0.425f));
            Assert.That(image.canvasRenderer.GetInheritedAlpha(), Is.EqualTo(0.5f).Within(0.0001f));

            group.alpha = 1f;
            image.color = new Color(1f, 1f, 1f, 0.85f);
            UiPvBackdropBlur.Configure(false);
            Assert.That(panel.ResolveMaterial(themed), Is.SameAs(themed));
            Assert.That(image.color.a, Is.EqualTo(0.85f), "Disabling glass preserves the authored ordinary Alpha composition.");
        }

        /// <summary>Checks only the explicitly bound background changes queue and restores its source through toggles and destruction.</summary>
        /// <returns>The Play Mode material-ownership and queue regression coroutine.</returns>
        [UnityTest]
        public IEnumerator BoundBackground_ClonesOnlyItsQueueAndRestoresSourceAcrossLifecycle()
        {
            Sprite? panelSprite = null;
            Sprite? ringSprite = null;
#if UNITY_EDITOR
            // Canvas artwork needs actual expanded slices; legacy Sprite subassets describe layout only.
            foreach (var asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/Ribbon/newui@3x.png"))
            {
                if (asset is Sprite sprite)
                {
                    if (sprite.name == "newui@3x_22")
                    {
                        panelSprite = sprite;
                    }
                    else if (sprite.name == "newui@3x_10")
                    {
                        ringSprite = sprite;
                    }
                }
            }
#else
            Assert.Ignore("This regression requires the actual imported panel and ring artwork.");
            yield break;
#endif
            Assert.That(panelSprite, Is.Not.Null);
            Assert.That(ringSprite, Is.Not.Null);
            var originalMode = (UiOutlineMode)typeof(UiOutlineTheme)
                .GetField("s_mode", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            try
            {
                UiOutlineTheme.Configure(UiOutlineMode.All);
                var resources = Resources.Load<UiOutlineResources>("Rendering/UI/OutlineTheme");
                Assert.That(resources, Is.Not.Null);
                Assert.That(resources!.AlphaMaterial, Is.Not.Null);
                var source = _objects.Track(new Material(resources.AlphaMaterial!)
                {
                    name = "Authored backdrop queue source",
                    renderQueue = 3027
                });
                var sourceTint = new Color(0.25f, 0.5f, 0.75f, 0.8f);
                source.SetColor("_Color", sourceTint);
                source.SetFloat("_ListDepthOffset", 7.25f);
                source.SetFloat("_Stencil", 3f);
                source.SetFloat("_UseUIAlphaClip", 1f);
                source.EnableKeyword("UNITY_UI_ALPHACLIP");

                var panel = CreatePanel();
                var image = panel.GetComponent<QualityUiImage>();
                image.sprite = panelSprite;
                var ring = _objects.Create("Explicit backdrop ring", image.transform.parent)
                    .AddComponent<QualityUiImage>();
                var unrelated = _objects.Create("Unrelated UI using the same material", image.transform.parent)
                    .AddComponent<QualityUiImage>();
                ring.material = source;
                unrelated.material = source;
                ring.sprite = ringSprite;
                unrelated.sprite = ringSprite;
                panel.enabled = false;
                typeof(UiBackdropPanel).GetField("_backgroundImage", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(panel, ring);
                panel.enabled = true;
                yield return null;
                Assert.That(ring.material, Is.SameAs(source));
                Assert.That(source.IsKeywordEnabled("UNITY_UI_ALPHACLIP"), Is.True,
                    "The authored source keyword must remain enabled before its queue-only clone is created.");

                UiPvBackdropBlur.Configure(true);
                var variant = ring.material;
                var bodyVariant = image.material;
                Assert.That(variant, Is.Not.SameAs(source));
                Assert.That(variant.renderQueue, Is.EqualTo(MajUiBackdropBridge.BackgroundQueueUpperBound));
                Assert.That(variant.shader, Is.SameAs(source.shader));
                Assert.That(variant.GetColor("_Color"), Is.EqualTo(sourceTint));
                Assert.That(variant.GetFloat("_ListDepthOffset"), Is.EqualTo(7.25f));
                Assert.That(variant.GetFloat("_Stencil"), Is.EqualTo(3f));
                Assert.That(variant.IsKeywordEnabled("UNITY_UI_ALPHACLIP"), Is.True);
                Assert.That(ring.material, Is.SameAs(variant));
                Assert.That(unrelated.material, Is.SameAs(source));
                Assert.That(source.renderQueue, Is.EqualTo(3027));
                Assert.That(ring.SourceMaterial, Is.SameAs(source));

                UiPvBackdropBlur.Configure(false);
                Assert.That(ring.material, Is.SameAs(source));
                Assert.That(source.renderQueue, Is.EqualTo(3027));
                UiPvBackdropBlur.Configure(true);
                Assert.That(ring.material, Is.SameAs(variant));
                panel.enabled = false;
                yield return null;
                Assert.That(ring.material, Is.SameAs(source));
                Assert.That(variant == null, Is.True, "Disabling the owner releases its queue-only clone.");
                Assert.That(bodyVariant == null, Is.True, "Disabling the owner releases its glass clone as well.");

                panel.enabled = true;
                var replacement = ring.material;
                Assert.That(replacement, Is.Not.SameAs(variant));
                Assert.That(replacement.renderQueue, Is.EqualTo(MajUiBackdropBridge.BackgroundQueueUpperBound));
                Object.Destroy(panel);
                yield return null;
                Assert.That(ring.material, Is.SameAs(source));
                Assert.That(replacement == null, Is.True);
                Assert.That(unrelated.material, Is.SameAs(source));
                Assert.That(source.GetColor("_Color"), Is.EqualTo(sourceTint));
                Assert.That(source.IsKeywordEnabled("UNITY_UI_ALPHACLIP"), Is.True);
            }
            finally
            {
                UiOutlineTheme.Configure(originalMode);
            }
        }

        /// <summary>Checks button bodies use the later blurred snapshot while symbols and native hold progress keep their original sharp shaders.</summary>
        /// <returns>The Play Mode second-stage material-ownership regression coroutine.</returns>
        [UnityTest]
        public IEnumerator ButtonBodyAndForeground_KeepSeparateQueuesAndRestoreAuthoredMaterials()
        {
            var originalMode = (UiOutlineMode)typeof(UiOutlineTheme)
                .GetField("s_mode", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            try
            {
                UiOutlineTheme.Configure(UiOutlineMode.All);
                var resources = Resources.Load<UiOutlineResources>("Rendering/UI/OutlineTheme");
                Assert.That(resources, Is.Not.Null);
                var source = _objects.Track(new Material(resources!.AlphaMaterial!)
                {
                    name = "Authored sharp button foreground",
                    renderQueue = 3027
                });
                var materialTint = new Color(0.3f, 0.5f, 0.7f, 0.9f);
                source.SetColor("_Color", materialTint);
                source.SetFloat("_Stencil", 3f);
                source.SetFloat("_ListDepthOffset", 7.25f);
                source.SetFloat("_UseUIAlphaClip", 1f);
                source.EnableKeyword("UNITY_UI_ALPHACLIP");
                var panel = CreateButtonPanel(source, out var body, out var glyph, out var hold);
                var glyphTint = new Color(0.85f, 0.75f, 0.52f, 0.8f);
                glyph.color = glyphTint;
                yield return null;
                Assert.That(panel.IsButton, Is.True);
                Assert.That(glyph.materialForRendering, Is.SameAs(source));
                Assert.That(hold.materialForRendering, Is.SameAs(source));

                UiPvBackdropBlur.Configure(true);
                Canvas.ForceUpdateCanvases();
                var bodyVariant = body.materialForRendering;
                var glyphVariant = glyph.materialForRendering;
                var holdVariant = hold.materialForRendering;
                Assert.That(bodyVariant.shader.name, Is.EqualTo("Maj/UI/PV Glass"));
                Assert.That(bodyVariant.renderQueue, Is.EqualTo(MajUiBackdropBridge.ButtonBodyQueue));
                Assert.That(bodyVariant.GetFloat("_BackdropIsButton"), Is.EqualTo(1f));
                Assert.That(glyphVariant.renderQueue, Is.EqualTo(MajUiBackdropBridge.ButtonGlyphQueueLowerBound));
                Assert.That(holdVariant.renderQueue, Is.EqualTo(MajUiBackdropBridge.ButtonGlyphQueueLowerBound));
                foreach (var foreground in new[] { glyphVariant, holdVariant })
                {
                    Assert.That(foreground.shader, Is.SameAs(source.shader));
                    Assert.That(foreground.GetColor("_Color"), Is.EqualTo(materialTint));
                    Assert.That(foreground.GetFloat("_Stencil"), Is.EqualTo(3f));
                    Assert.That(foreground.GetFloat("_ListDepthOffset"), Is.EqualTo(7.25f));
                    Assert.That(foreground.IsKeywordEnabled("UNITY_UI_ALPHACLIP"), Is.True);
                }
                Assert.That(glyph.color, Is.EqualTo(glyphTint));
                Assert.That(hold.fillAmount, Is.EqualTo(0.37f));
                Assert.That(glyph.SourceMaterial, Is.SameAs(source));
                Assert.That(hold.material, Is.SameAs(source));
                Assert.That(source.renderQueue, Is.EqualTo(3027));

                UiPvBackdropBlur.Configure(false);
                Assert.That(body.materialForRendering, Is.SameAs(source));
                Assert.That(glyph.materialForRendering, Is.SameAs(source));
                Assert.That(hold.materialForRendering, Is.SameAs(source));
                UiPvBackdropBlur.Configure(true);
                Assert.That(glyph.materialForRendering.renderQueue, Is.EqualTo(MajUiBackdropBridge.ButtonGlyphQueueLowerBound));
                panel.enabled = false;
                yield return null;
                Assert.That(body.materialForRendering, Is.SameAs(source));
                Assert.That(glyph.materialForRendering, Is.SameAs(source));
                Assert.That(hold.materialForRendering, Is.SameAs(source));
                Assert.That(bodyVariant == null, Is.True);
                Assert.That(glyphVariant == null, Is.True);
                Assert.That(holdVariant == null, Is.True);
                Assert.That(glyph.color, Is.EqualTo(glyphTint));
                Assert.That(hold.fillAmount, Is.EqualTo(0.37f));
            }
            finally
            {
                UiOutlineTheme.Configure(originalMode);
            }
        }

        /// <summary>Checks a category with procedural corners reuses the panel snapshot without a Sprite or source-material changes.</summary>
        /// <returns>The native material-selection and lifecycle regression coroutine.</returns>
        [UnityTest]
        public IEnumerator RoundedCategory_ReusesPanelSnapshotAndPreservesAuthoredCornersWithoutSprite()
        {
            var originalMode = (UiOutlineMode)typeof(UiOutlineTheme)
                .GetField("s_mode", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            try
            {
                UiOutlineTheme.Configure(UiOutlineMode.All);
                var shader = Shader.Find("Maj/UI/Rounded Rect");
                Assert.That(shader, Is.Not.Null);
                var source = _objects.Track(new Material(shader));
                source.SetFloat("_RadiusTL", 7f);
                source.SetFloat("_RadiusTR", 11f);
                source.SetFloat("_RadiusBL", 13f);
                source.SetFloat("_RadiusBR", 17f);
                source.SetFloat("_Stencil", 3f);
                var panel = CreatePanel();
                panel.enabled = false;
                var image = panel.GetComponent<QualityUiImage>();
                image.material = source;
                image.sprite = null;
                var tint = new Color(0.7f, 0.8f, 0.9f, 0.6f);
                image.color = tint;
                typeof(UiBackdropPanel).GetField("_isCategory", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(panel, true);
                panel.enabled = true;
                UiPvBackdropBlur.Configure(true);
                var variant = image.material;
                Assert.That(variant.shader.name, Is.EqualTo("Maj/UI/PV Glass"));
                Assert.That(variant.renderQueue, Is.EqualTo(MajUiBackdropBridge.CategoryQueue));
                Assert.That(variant.GetFloat("_BackdropIsButton"), Is.Zero);
                Assert.That(variant.GetFloat("_BackdropIsCategory"), Is.EqualTo(1f));
                Assert.That(variant.GetFloat("_BackdropIsRoundedRect"), Is.EqualTo(1f));
                Assert.That(variant.GetVector("_FillSampleUV"), Is.EqualTo(new Vector4(0.5f, 0.5f, 0f, 0f)));
                foreach (var property in new[] { "_RadiusTL", "_RadiusTR", "_RadiusBL", "_RadiusBR", "_Stencil" })
                {
                    Assert.That(variant.GetFloat(property), Is.EqualTo(source.GetFloat(property)));
                }
                Assert.That(variant.GetTexture("_PanelBaseTex"), Is.SameAs(Texture2D.whiteTexture));
                Assert.That(image.color, Is.EqualTo(tint));
                Assert.That(image.sprite, Is.Null);
                Assert.That(image.SourceMaterial, Is.SameAs(source));
                UiPvBackdropBlur.Configure(false);
                Assert.That(image.material, Is.SameAs(source));
                UiOutlineTheme.Configure(UiOutlineMode.None);
                var originalCutout = image.material;
                Assert.That(originalCutout.shader, Is.SameAs(source.shader));
                Assert.That(originalCutout.GetFloat("_MajUIOpaque"), Is.EqualTo(1f));
                Assert.That(source.GetFloat("_MajUIOpaque"), Is.Zero);
                Assert.That(image.color, Is.EqualTo(tint));
                yield return null;
            }
            finally
            {
                UiOutlineTheme.Configure(originalMode);
            }
        }

        /// <summary>Checks dynamic TMP fallback submeshes follow their owner's active style and restore native materials together.</summary>
        /// <returns>The TMP event, style refresh, material-ownership and cleanup regression coroutine.</returns>
        [UnityTest]
        public IEnumerator CategoryText_BindsNewNativeSubMeshesAndRestoresThemWithItsOwner()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Assert.Ignore("Native TMP materials require a real graphics device.");
            }
            var originalMode = (UiOutlineMode)typeof(UiOutlineTheme)
                .GetField("s_mode", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            try
            {
                UiOutlineTheme.Configure(UiOutlineMode.All);
                var resources = Resources.Load<UiOutlineResources>("Rendering/UI/OutlineTheme");
                var panel = CreateButtonPanel(resources!.AlphaMaterial!, out var body, out _, out _);
                panel.enabled = false;
                typeof(UiBackdropPanel).GetField("_isCategory", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(panel, true);
                var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                Assert.That(font, Is.Not.Null);
                var source = _objects.Track(new Material(font!.material));
                var fallbackSource = _objects.Track(new Material(source));
                var textObject = _objects.Create("Category label", body.transform.parent);
                textObject.SetActive(false);
                var text = textObject.AddComponent<TextMeshProUGUI>();
                text.font = font;
                text.fontSharedMaterial = source;
                text.text = "Category";
                text.rectTransform.sizeDelta = new Vector2(200f, 80f);
                textObject.SetActive(true);
                panel.enabled = true;
                UiPvBackdropBlur.Configure(true);
                text.ForceMeshUpdate();
                var variant = text.materialForRendering;
                Assert.That(variant.renderQueue, Is.EqualTo(MajUiBackdropBridge.CategoryQueue));
                Assert.That(variant.shader, Is.SameAs(source.shader));
                var submesh = TMP_SubMeshUI.AddSubTextObject(text, new MaterialReference(1, font, null!, fallbackSource, 0f));
                _objects.Track(submesh.gameObject);
                TMPro_EventManager.ON_TEXT_CHANGED(text);
                var fallbackVariant = submesh.materialForRendering;
                Assert.That(fallbackVariant.renderQueue, Is.EqualTo(MajUiBackdropBridge.CategoryQueue));
                Assert.That(fallbackVariant.shader, Is.SameAs(fallbackSource.shader));
                Assert.That(submesh.sharedMaterial, Is.SameAs(fallbackSource));
                Assert.That(text.fontSharedMaterial, Is.SameAs(source));
                var fallbackMaterialDirty = 0;
                submesh.RegisterDirtyMaterialCallback(() => fallbackMaterialDirty++);
                typeof(UiBackdropPanel).GetField("_liquidOnly", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(panel, true);
                UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
                Assert.That(fallbackMaterialDirty, Is.GreaterThan(0), "A style change must dirty submeshes created after the owner's initial Graphic scan.");
                Assert.That(text.materialForRendering.renderQueue, Is.EqualTo(MajUiBackdropBridge.CategoryQueue));
                Assert.That(submesh.materialForRendering.renderQueue, Is.EqualTo(MajUiBackdropBridge.CategoryQueue));
                fallbackMaterialDirty = 0;
                UiPvBackdropBlur.Configure(true, UiBackdropStyle.GaussianBlur);
                Assert.That(fallbackMaterialDirty, Is.GreaterThan(0), "Unsupported styles must also refresh the already-bound native fallback materials.");
                Assert.That(text.materialForRendering, Is.SameAs(source));
                Assert.That(submesh.materialForRendering, Is.SameAs(fallbackSource), "Liquid-only foregrounds must retain the same native ordering as their Gaussian body.");
                UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
                variant = text.materialForRendering;
                fallbackVariant = submesh.materialForRendering;
                Assert.That(variant.renderQueue, Is.EqualTo(MajUiBackdropBridge.CategoryQueue));
                Assert.That(fallbackVariant.renderQueue, Is.EqualTo(MajUiBackdropBridge.CategoryQueue));
                panel.enabled = false;
                yield return null;
                Assert.That(text.materialForRendering, Is.SameAs(source));
                Assert.That(submesh.materialForRendering, Is.SameAs(fallbackSource));
                Assert.That(variant == null, Is.True);
                Assert.That(fallbackVariant == null, Is.True);
            }
            finally
            {
                UiOutlineTheme.Configure(originalMode);
            }
        }

        /// <summary>Checks an authored frame alone owns only the existing RT3 stage and finds the main-display circle without peripheral buttons.</summary>
        /// <returns>The frame-only registration, role selection, circle mapping and style lifecycle regression.</returns>
        [UnityTest]
        [Category("GPU")]
        public IEnumerator CoverFrameOnly_RequestsRt3AndReusesPoolsWithoutChangingGaussian()
        {
#if UNITY_EDITOR
            var previousCamera = SceneSwitcher.MainCamera;
            try
            {
                var camera = _objects.Add<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                SceneSwitcher.MainCamera = camera;
                var source = _objects.Track(new RenderTexture(128, 96, 0));
                Assert.That(source.Create(), Is.True);
                camera.targetTexture = source;
                var canvas = _objects.Add<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                var display = _objects.Track(new GameObject("Main_Display", typeof(RectTransform)));
                display.transform.SetParent(canvas.transform, false);
                var displayRect = (RectTransform)display.transform;
                displayRect.sizeDelta = new Vector2(80f, 80f);
                var child = _objects.Track(new GameObject("Frame only", typeof(RectTransform), typeof(CanvasRenderer)));
                child.SetActive(false);
                child.transform.SetParent(display.transform, false);
                var frame = child.AddComponent<QualityUiImage>();
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/List/Model/SongCoverDisplayer.prefab");
                EditorUtility.CopySerialized(prefab.transform.Find("BG").GetComponent<QualityUiImage>(), frame);
                frame.rectTransform.sizeDelta = new Vector2(64f, 64f);
                frame.color = Color.white;
                UiOutlineTheme.Configure(UiOutlineMode.All);
                UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
                child.SetActive(true);
                yield return null;
                Canvas.ForceUpdateCanvases();
                frame.canvasRenderer.cull = false;
                var capture = MajUiBackdropBridge.GetActive(camera);
                Assert.That(capture, Is.Not.Null);
                Assert.That(Field<List<UiBackdropPanel>>("s_panels"), Is.Empty);
                Assert.That(Field<List<QualityUiImage>>("s_liquidFrameConsumers"), Is.EquivalentTo(new[] { frame }));
                Assert.That(MajUiBackdropBridge.GetActiveCategories(capture, camera), Is.Null);
                var buttons = MajUiBackdropBridge.GetActiveButtons(capture, camera);
                Assert.That(buttons, Is.Not.Null, "A frame-only menu cannot rely on the existence of peripheral buttons to refresh its background.");
                using (var commands = new UnityEngine.Rendering.CommandBuffer())
                {
                    commands.SetRenderTarget(source);
                    commands.ClearRenderTarget(false, true, Color.gray);
                    capture!.Record(commands, camera, source);
                    buttons!.RecordButtons(commands, camera, source);
                    Graphics.ExecuteCommandBuffer(commands);
                }
                Assert.That(Shader.GetGlobalFloat("_MajUiPanelBlurReady"), Is.Zero);
                Assert.That(Shader.GetGlobalFloat("_MajPvBlurReady"), Is.Zero);
                Assert.That(Shader.GetGlobalFloat("_MajUiButtonBlurReady"), Is.EqualTo(1f));
                Assert.That(UiPvBackdropBlur.TryGetCircleUvMapping(camera, displayRect, out var circleX, out var circleY), Is.True);
                Assert.That(Shader.GetGlobalVector("_MajUiButtonClipUvX"), Is.EqualTo(circleX));
                Assert.That(Shader.GetGlobalVector("_MajUiButtonClipUvY"), Is.EqualTo(circleY));
                var names = new[] { "s_liquidCategoryTexture", "s_liquidButtonTexture", "s_boardBlurTexture", "s_categoryBlurTexture", "s_buttonBlurTexture", "s_blurScratchTexture" };
                var retained = new RenderTexture[names.Length];
                for (var index = 0; index < names.Length; index++)
                {
                    retained[index] = Field<RenderTexture>(names[index]);
                }
                frame.color = Color.clear;
                Assert.That(MajUiBackdropBridge.GetActive(camera), Is.Null);
                frame.color = Color.white;
                UiPvBackdropBlur.Configure(true, UiBackdropStyle.GaussianBlur);
                Assert.That(MajUiBackdropBridge.GetActive(camera), Is.Null, "The ordinary Gaussian frame must not request any new capture cut.");
                UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
                Assert.That(MajUiBackdropBridge.GetActiveButtons(MajUiBackdropBridge.GetActive(camera), camera), Is.Not.Null);
                frame.enabled = false;
                Assert.That(Field<List<QualityUiImage>>("s_liquidFrameConsumers"), Is.Empty);
                Assert.That(MajUiBackdropBridge.GetActive(camera), Is.Null);
                Assert.That(Shader.GetGlobalFloat("_MajUiButtonBlurReady"), Is.Zero);
                for (var index = 0; index < names.Length; index++)
                {
                    Assert.That(Field<RenderTexture>(names[index]), Is.SameAs(retained[index]), "The frame must reuse the six existing persistent targets across styles and disable.");
                }
            }
            finally
            {
                SceneSwitcher.MainCamera = previousCamera;
            }
#else
            Assert.Ignore("This regression requires the authored frame material and prefab.");
            yield break;
#endif
        }

        /// <summary>Checks single peripheral consumers request only RT3 while preserving the startup pools on disable.</summary>
        /// <returns>The native role ownership regression.</returns>
        [UnityTest]
        public IEnumerator ButtonOnlyConsumer_RequestsLaterStageAndCleansUpOnDisable()
        {
            return VerifySingleRoleOwnership(false);
        }

        /// <summary>Checks single category consumers request only RT2 while preserving the startup pools on disable.</summary>
        /// <returns>The native category ownership regression.</returns>
        [UnityTest]
        public IEnumerator CategoryOnlyConsumer_UsesFirstCaptureAndClearsReadinessOnDisable()
        {
            return VerifySingleRoleOwnership(true);
        }

        /// <summary>Checks optional stages are independent from first-stage ownership and native target retention.</summary>
        /// <param name="category">Whether this fixture owns RT2 rather than peripheral RT3.</param>
        /// <returns>The visibility, capture selection and pool-retention coroutine.</returns>
        private IEnumerator VerifySingleRoleOwnership(bool category)
        {
            var originalCamera = SceneSwitcher.MainCamera;
            try
            {
                var camera = _objects.Add<Camera>();
                camera.enabled = false;
                SceneSwitcher.MainCamera = camera;
                var resources = Resources.Load<UiOutlineResources>("Rendering/UI/OutlineTheme")!;
                var panel = CreateButtonPanel(resources.AlphaMaterial!, out var body, out _, out _);
                panel.enabled = false;
                typeof(UiBackdropPanel).GetField("_isCategory", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(panel, category);
                panel.enabled = true;
                body.canvas.rootCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                body.canvas.rootCanvas.worldCamera = camera;
                UiPvBackdropBlur.Configure(true);
                yield return null;
                Canvas.ForceUpdateCanvases();
                var capture = MajUiBackdropBridge.GetActive(camera);
                Assert.That(capture, Is.Not.Null);
                Assert.That(MajUiBackdropBridge.GetActiveCategories(capture, camera) != null, Is.EqualTo(category));
                Assert.That(MajUiBackdropBridge.GetActiveButtons(capture, camera) != null, Is.EqualTo(!category));
                body.color = Color.clear;
                Assert.That(MajUiBackdropBridge.GetActive(camera), Is.Null);
                body.color = Color.white;
                var source = _objects.Track(new RenderTexture(8, 8, 0));
                Assert.That(source.Create(), Is.True);
                using (var commands = new UnityEngine.Rendering.CommandBuffer())
                {
                    commands.SetRenderTarget(source);
                    commands.ClearRenderTarget(false, true, Color.gray);
                    capture!.Record(commands, camera, source);
                    Graphics.ExecuteCommandBuffer(commands);
                    Assert.That(Shader.GetGlobalFloat("_MajUiPanelBlurReady"), Is.Zero, "Absent boards cannot publish RT1, despite its preallocated native storage.");
                    commands.Clear();
                    if (category)
                    {
                        MajUiBackdropBridge.GetActiveCategories(capture, camera)!.RecordCategories(commands, camera, source);
                    }
                    else
                    {
                        MajUiBackdropBridge.GetActiveButtons(capture, camera)!.RecordButtons(commands, camera, source);
                    }
                    Graphics.ExecuteCommandBuffer(commands);
                }
                var result = Field<RenderTexture>(category ? "s_categoryBlurTexture" : "s_buttonBlurTexture");
                Assert.That(Shader.GetGlobalTexture(category ? "_MajPvBlurTexture" : "_MajUiButtonBlurTexture"), Is.SameAs(result));
                Assert.That(Shader.GetGlobalFloat(category ? "_MajPvBlurReady" : "_MajUiButtonBlurReady"), Is.EqualTo(1f));
                Assert.That(result.width, Is.EqualTo(2));
                panel.enabled = false;
                yield return null;
                Assert.That(MajUiBackdropBridge.GetActive(camera), Is.Null);
                Assert.That(Field<RenderTexture>(category ? "s_categoryBlurTexture" : "s_buttonBlurTexture"), Is.SameAs(result));
                Assert.That(Shader.GetGlobalFloat("_MajPvBlurReady"), Is.Zero);
                Assert.That(Shader.GetGlobalFloat("_MajUiButtonBlurReady"), Is.Zero);
                Assert.That(source.IsCreated(), Is.True);
            }
            finally
            {
                SceneSwitcher.MainCamera = originalCamera;
            }
        }

        /// <summary>Checks secondary plates never activate capture alone and keep preallocated storage after an owner leaves.</summary>
        /// <returns>The owner registration and persistent-resource coroutine.</returns>
        [UnityTest]
        public IEnumerator ReuseOnlyPlates_DoNotActivateOrCreateFirstCaptureWithoutAPrimaryPanel()
        {
            var originalCamera = SceneSwitcher.MainCamera;
            try
            {
                var camera = _objects.Add<Camera>();
                camera.enabled = false;
                SceneSwitcher.MainCamera = camera;
                var secondary = CreatePanel();
                typeof(UiBackdropPanel).GetField("_reusePanelCaptureOnly", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(secondary, true);
                secondary.GetComponent<QualityUiImage>().canvas.renderMode = RenderMode.ScreenSpaceCamera;
                secondary.GetComponent<QualityUiImage>().canvas.worldCamera = camera;
                UiPvBackdropBlur.Configure(true);
                yield return null;
                Canvas.ForceUpdateCanvases();
                var pool = Field<RenderTexture>("s_boardBlurTexture");
                Assert.That(pool, Is.Not.Null);
                Assert.That(MajUiBackdropBridge.GetActive(camera), Is.Null);
                var primary = CreatePanel();
                primary.GetComponent<QualityUiImage>().canvas.renderMode = RenderMode.ScreenSpaceCamera;
                primary.GetComponent<QualityUiImage>().canvas.worldCamera = camera;
                yield return null;
                Canvas.ForceUpdateCanvases();
                Assert.That(MajUiBackdropBridge.GetActive(camera), Is.Not.Null);
                primary.enabled = false;
                Assert.That(MajUiBackdropBridge.GetActive(camera), Is.Null);
                Assert.That(Field<RenderTexture>("s_boardBlurTexture"), Is.SameAs(pool));
                Assert.That(Shader.GetGlobalFloat("_MajUiPanelBlurReady"), Is.Zero);
            }
            finally
            {
                SceneSwitcher.MainCamera = originalCamera;
            }
        }

        /// <summary>Creates only native graphics for a real button hierarchy, excluding input scripts and scene managers.</summary>
        /// <param name="source">The shared authored material that all test graphics must retain.</param>
        /// <param name="body">The exact quality-aware body receiving the later background snapshot.</param>
        /// <param name="glyph">A quality-aware symbol that must remain a sharp foreground.</param>
        /// <param name="hold">A native Filled Image representing the independent radial hold-progress contract.</param>
        /// <returns>The registered button-role panel beneath the shared foreground root.</returns>
        private UiBackdropPanel CreateButtonPanel(Material source, out QualityUiImage body,
            out QualityUiImage glyph, out UnityEngine.UI.Image hold)
        {
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var root = _objects.Create("Button foreground root", canvas.transform);
            root.SetActive(false);
            var background = _objects.Create("BG", root.transform);
            body = background.AddComponent<QualityUiImage>();
            body.sprite = LoadImportedSprite("newui@3x_20");
            body.material = source;
            var panel = background.AddComponent<UiBackdropPanel>();
            typeof(UiBackdropPanel).GetField("_isButton", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(panel, true);
            typeof(UiBackdropPanel).GetField("_buttonForegroundRoot", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(panel, root.transform);
            glyph = _objects.Create("Sharp symbol", root.transform).AddComponent<QualityUiImage>();
            glyph.sprite = LoadImportedSprite("newui@3x_0");
            glyph.material = source;
            hold = _objects.Create("Sharp radial progress", root.transform).AddComponent<UnityEngine.UI.Image>();
            hold.sprite = LoadImportedSprite("newui@3x_0");
            hold.material = source;
            hold.type = UnityEngine.UI.Image.Type.Filled;
            hold.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
            hold.fillOrigin = 2;
            hold.fillClockwise = true;
            hold.fillAmount = 0.37f;
            root.SetActive(true);
            return panel;
        }

        /// <summary>Finds a real imported expanded slice suitable for native Canvas rendering and outline pairing.</summary>
        /// <param name="name">The exact source-atlas Sprite name.</param>
        /// <returns>The imported source Sprite with matching outlined layout.</returns>
        /// <exception cref="AssertionException">The imported Sprite is unavailable.</exception>
        private static Sprite LoadImportedSprite(string name)
        {
#if UNITY_EDITOR
            foreach (var asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/Ribbon/newui@3x.png"))
            {
                if (asset is Sprite sprite && sprite.name == name)
                {
                    return sprite;
                }
            }
            Assert.Fail("Imported Ribbon Sprite is missing: " + name);
#else
            Assert.Ignore("This regression requires actual imported Ribbon artwork.");
#endif
            return null!;
        }

        /// <summary>Gets one original imported panel slice from the production logical-layout resource.</summary>
        /// <param name="name">The actual Ribbon Sprite name, including its atlas prefix.</param>
        /// <returns>The preserved original Sprite used by the production mesh generator.</returns>
        /// <exception cref="AssertionException">The production layout or requested authored slice is missing.</exception>
        private static Sprite LoadAuthoredSprite(string name)
        {
            var layout = Resources.Load<UiAtlasLayout>("Rendering/UI/AtlasLayout");
            Assert.That(layout, Is.Not.Null);
            foreach (var sprite in layout!.LegacySprites)
            {
                if (sprite != null && sprite.name == name)
                {
                    return sprite;
                }
            }
            Assert.Fail("Production Ribbon slice is missing: " + name);
            return null!;
        }

        /// <summary>Rejects invalid shape parameters before they can enter fragment-stage distance or derivative calculations.</summary>
        /// <param name="value">The actual vector read back from the production panel material.</param>
        private static void AssertFinite(Vector4 value)
        {
            for (var component = 0; component < 4; component++)
            {
                Assert.That(float.IsNaN(value[component]) || float.IsInfinity(value[component]), Is.False);
            }
        }

        /// <summary>Creates a real quality-aware Image beneath a native overlay Canvas.</summary>
        /// <returns>The enabled opt-in panel component.</returns>
        private UiBackdropPanel CreatePanel()
        {
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var child = _objects.Create("PV lifecycle panel", canvas.transform);
            child.SetActive(false);
            var image = child.AddComponent<QualityUiImage>();
            image.rectTransform.sizeDelta = new Vector2(64f, 64f);
            var panel = child.AddComponent<UiBackdropPanel>();
            child.SetActive(true);
            return panel;
        }

        /// <summary>Gets the service's actual opt-in registry without adding production debug APIs.</summary>
        private static List<UiBackdropPanel> Panels => Field<List<UiBackdropPanel>>("s_panels");

        /// <summary>Reads a private service field used to verify resource ownership and paired subscriptions.</summary>
        /// <typeparam name="T">The declared field type.</typeparam>
        /// <param name="name">The production field name.</param>
        /// <returns>The current field value.</returns>
        private static T Field<T>(string name)
        {
            return (T)typeof(UiPvBackdropBlur).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        }

        /// <summary>Clears service state as Unity does at the beginning of a new Play session.</summary>
        private static void ResetService()
        {
            typeof(UiPvBackdropBlur).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
        }
    }
}
