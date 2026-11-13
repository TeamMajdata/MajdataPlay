using System.Reflection;
using MajdataPlay.Rendering;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

#nullable enable
namespace MajdataPlay.Tests.Game.Rendering
{
    /// <summary>Checks real opaque-to-alpha material conversion and its visible GPU output.</summary>
    public sealed class AtlasAlphaMaterialTests
    {
        /// <summary>Uses a small target while keeping samples away from geometry edges.</summary>
        private const int TargetSize = 32;

        /// <summary>Selects the local glass keyword for manually drawn fixture materials without publishing production globals.</summary>
        private static bool s_liquidStyle;

        /// <summary>Stores a nonblack background to expose a material that fails to write color.</summary>
        private static readonly Color s_background = new Color(0.1f, 0.3f, 0.6f, 0.4f);

        /// <summary>Clears only the theme's runtime cache so each test builds a fresh variant.</summary>
        [SetUp]
        public void SetUp()
        {
            s_liquidStyle = false;
            ResetTheme();
        }

        /// <summary>Releases generated material variants without destroying imported assets.</summary>
        [TearDown]
        public void TearDown()
        {
            s_liquidStyle = false;
            ResetTheme();
        }

        /// <summary>Checks real cutout sources preserve the alpha template's render state and continuous visual alpha.</summary>
        /// <param name="samples">The target sample count whose alpha output is verified.</param>
        /// <param name="circle">Whether the source uses the List procedural circle instead of a plain cutout.</param>
        [TestCase(1, false)]
        [TestCase(4, false)]
        [TestCase(1, true)]
        [TestCase(4, true)]
        [Category("GPU")]
        public void RealCutoutVariant_PreservesTemplateStateAndRendersContinuousAlpha(int samples, bool circle)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("This test requires a real graphics device.");
            }
            var descriptor = new RenderTextureDescriptor(TargetSize, TargetSize, RenderTextureFormat.ARGB32, 24)
            {
                msaaSamples = samples,
                sRGB = false
            };
            if (SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor) != samples)
            {
                Assert.Ignore($"The current GPU does not support this target at {samples} samples.");
            }

            var originalPath = circle ? "Assets/Rendering/Materials/UI/List/[List]Cover.mat" : "Assets/Rendering/Materials/UI/[UI]Opaque.mat";
            var original = AssetDatabase.LoadAssetAtPath<Material>(originalPath);
            var template = AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Materials/UI/AtlasAlpha.mat");
            Assert.That(original, Is.Not.Null);
            Assert.That(template, Is.Not.Null);
            Assert.That(original.shader.name, Is.EqualTo(circle ? "Maj/UI/List/CoverShader" : "Maj/Common/Opaque"));
            Assert.That(original.HasProperty("_StencilComp"), Is.EqualTo(circle));
            Assert.That(original.HasProperty("_ColorMask"), Is.EqualTo(circle));

            UiOutlineTheme.Configure(UiOutlineMode.All);
            // Resource bindings and Image lifecycle are covered separately. This fixture isolates material conversion.
            typeof(UiOutlineTheme).GetField("s_alphaTemplate", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, template);
            var variant = UiOutlineTheme.GetAlphaMaterial(original);
            Assert.That(variant, Is.Not.SameAs(original));
            Assert.That(variant.shader.name, Is.EqualTo(circle ? "Maj/UI/List/Cover Alpha" : "Maj/UI/Atlas Alpha"));
            Assert.That(variant.GetFloat("_ColorMask"), Is.EqualTo(15f));
            Assert.That(variant.GetFloat("_StencilComp"), Is.EqualTo((float)CompareFunction.Always));
            Assert.That(variant.GetFloat("_StencilWriteMask"), Is.EqualTo(255f));
            Assert.That(variant.GetFloat("_StencilReadMask"), Is.EqualTo(255f));
            Assert.That(variant.GetFloat("_StencilOp"), Is.EqualTo((float)StencilOp.Keep));
            Assert.That(variant.GetFloat("_UseUIAlphaClip"), Is.Zero);
            Assert.That(variant.IsKeywordEnabled("UNITY_UI_ALPHACLIP"), Is.False);

            var savedTarget = RenderTexture.active;
            var savedScreenParams = Shader.GetGlobalVector("_ScreenParams");
            var savedZTest = Shader.GetGlobalFloat("unity_GUIZTestMode");
            var savedMipBias = Shader.GetGlobalFloat("_MajTextureMipBias");
            var savedGamma = Shader.GetGlobalInt("_UIVertexColorAlwaysGammaSpace");
            var material = new Material(variant);
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            var target = new RenderTexture(descriptor);
            var resolved = new RenderTexture(TargetSize, TargetSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(TargetSize, TargetSize, TextureFormat.RGBA32, false, true);
            try
            {
                Assert.That(target.Create(), Is.True);
                Assert.That(resolved.Create(), Is.True);
                material.SetTexture("_MainTex", texture);
                material.SetTextureScale("_MainTex", Vector2.one);
                material.SetTextureOffset("_MainTex", Vector2.zero);
                material.SetColor("_Color", Color.white);
                material.SetFloat("_ListDepthOffset", 0f);
                Shader.SetGlobalVector("_ScreenParams", new Vector4(TargetSize, TargetSize, 1f + 1f / TargetSize, 1f + 1f / TargetSize));
                Shader.SetGlobalFloat("unity_GUIZTestMode", (float)CompareFunction.LessEqual);
                Shader.SetGlobalFloat("_MajTextureMipBias", 0f);
                Shader.SetGlobalInt("_UIVertexColorAlwaysGammaSpace", 0);

                foreach (var alpha in new[] { 0.25f, 0.75f })
                {
                    texture.SetPixel(0, 0, new Color(0.8f, 0.2f, 0.4f, alpha));
                    texture.Apply(false, false);
                    var sampled = texture.GetPixel(0, 0);
                    RenderTexture.active = target;
                    GL.Clear(true, true, s_background);
                    Draw(material);
                    if (samples > 1)
                    {
                        target.ResolveAntiAliasedSurface(resolved);
                    }
                    else
                    {
                        Graphics.Blit(target, resolved);
                    }
                    RenderTexture.active = resolved;
                    readback.ReadPixels(new Rect(0, 0, TargetSize, TargetSize), 0, 0);
                    readback.Apply(false, false);
                    var actual = readback.GetPixel(TargetSize / 2, TargetSize / 2);
                    var expected = sampled * sampled.a + s_background * (1f - sampled.a);
                    expected.a = sampled.a + s_background.a * (1f - sampled.a);
                    AssertColor(actual, expected, $"{SystemInfo.graphicsDeviceType}, {samples}x, alpha {sampled.a}");
                }
                Assert.That(ShaderUtil.ShaderHasError(material.shader), Is.False);
            }
            finally
            {
                RenderTexture.active = savedTarget;
                Shader.SetGlobalVector("_ScreenParams", savedScreenParams);
                Shader.SetGlobalFloat("unity_GUIZTestMode", savedZTest);
                Shader.SetGlobalFloat("_MajTextureMipBias", savedMipBias);
                Shader.SetGlobalInt("_UIVertexColorAlwaysGammaSpace", savedGamma);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(resolved);
                Object.DestroyImmediate(readback);
            }
        }

        /// <summary>Checks lighter menu vertex opacity does not make the None-mode half-alpha artwork disappear or blend.</summary>
        [Test]
        [Category("GPU")]
        public void PlateVertexFade_KeepsNoneModeOpaqueAndFullyHiddenVerticesDiscarded()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("Opaque plate fragment output requires a real graphics device.");
            }
            var shader = Shader.Find("Maj/UI/Atlas Cutoff");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            var target = new RenderTexture(TargetSize, TargetSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(TargetSize, TargetSize, TextureFormat.RGBA32, false, true);
            var savedTarget = RenderTexture.active;
            var savedZTest = Shader.GetGlobalFloat("unity_GUIZTestMode");
            var savedGamma = Shader.GetGlobalInt("_UIVertexColorAlwaysGammaSpace");
            try
            {
                Assert.That(target.Create(), Is.True);
                texture.SetPixel(0, 0, new Color(0.8f, 0.6f, 0.4f, 128f / 255f));
                texture.Apply(false, false);
                material.SetTexture("_MainTex", texture);
                material.SetColor("_Color", new Color(1f, 1f, 1f, 0.85f));
                Shader.SetGlobalFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
                Shader.SetGlobalInt("_UIVertexColorAlwaysGammaSpace", 0);
                RenderTexture.active = target;
                GL.Clear(true, true, s_background);
                Draw(material);
                readback.ReadPixels(new Rect(0, 0, TargetSize, TargetSize), 0, 0);
                readback.Apply(false, false);
                AssertColor(readback.GetPixel(TargetSize / 2, TargetSize / 2),
                    new Color(0.8f, 0.6f, 0.4f, 1f), "None-mode plate at vertex alpha 0.85");
                material.SetColor("_Color", new Color(1f, 1f, 1f, 0f));
                GL.Clear(true, true, s_background);
                Draw(material);
                readback.ReadPixels(new Rect(0, 0, TargetSize, TargetSize), 0, 0);
                readback.Apply(false, false);
                AssertColor(readback.GetPixel(TargetSize / 2, TargetSize / 2), s_background,
                    "Fully hidden plate must not write opaque color");
            }
            finally
            {
                RenderTexture.active = savedTarget;
                Shader.SetGlobalFloat("unity_GUIZTestMode", savedZTest);
                Shader.SetGlobalInt("_UIVertexColorAlwaysGammaSpace", savedGamma);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(target);
            }
        }

        /// <summary>Checks artwork recoloring drives multiplicative glass tint, preserving blur, routes and relative fades.</summary>
        /// <param name="samples">The actual target sample count used for every composition check.</param>
        /// <param name="liquid">Whether the shared sharp-capture liquid branch replaces the Gaussian surface branch.</param>
        [TestCase(1, false)]
        [TestCase(4, false)]
        [TestCase(1, true)]
        [TestCase(4, true)]
        [Category("GPU")]
        public void GlassPanelTint_SamplesArtworkAndMultipliesBlurWithoutBakingThePalette(int samples, bool liquid)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("Glass panel fragment output requires a real graphics device.");
            }
            var descriptor = new RenderTextureDescriptor(TargetSize, TargetSize, RenderTextureFormat.ARGB32, 24)
            {
                msaaSamples = samples,
                sRGB = false
            };
            if (SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor) != samples)
            {
                Assert.Ignore($"The current GPU does not support this target at {samples} samples.");
            }
            var shader = Shader.Find("Maj/UI/PV Glass");
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True);
            var savedTarget = RenderTexture.active;
            var savedScreenParams = Shader.GetGlobalVector("_ScreenParams");
            var savedZTest = Shader.GetGlobalFloat("unity_GUIZTestMode");
            var savedMipBias = Shader.GetGlobalFloat("_MajTextureMipBias");
            var savedGamma = Shader.GetGlobalInt("_UIVertexColorAlwaysGammaSpace");
            var savedTextureAdd = Shader.GetGlobalVector("_TextureSampleAdd");
            var savedBlur = Shader.GetGlobalTexture("_MajPvBlurTexture");
            var savedButtonBlur = Shader.GetGlobalTexture("_MajUiButtonBlurTexture");
            var savedButtonBlurSize = Shader.GetGlobalVector("_MajUiButtonBlurTexture_TexelSize");
            var savedBlurSize = Shader.GetGlobalVector("_MajPvBlurTexture_TexelSize");
            var savedPanelReady = Shader.GetGlobalFloat("_MajPvBlurReady");
            var savedButtonReady = Shader.GetGlobalFloat("_MajUiButtonBlurReady");
            var savedLiquidStyle = s_liquidStyle;
            var material = new Material(shader);
            // This fixture isolates transmission from the separately tested artwork share.
            material.SetFloat("_GaussianButtonTintStrength", 1f);
            material.SetFloat("_LiquidArtworkBlend", 0f);
            material.SetFloat("_LiquidTintStrength", 1f);
            var artworkTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            var inkTexture = new Texture2D(TargetSize, TargetSize, TextureFormat.RGBA32, false, true)
            {
                filterMode = FilterMode.Point
            };
            var coverageTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            var blurTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            var target = new RenderTexture(descriptor);
            var resolved = new RenderTexture(TargetSize, TargetSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(TargetSize, TargetSize, TextureFormat.RGBA32, false, true);
            try
            {
                Assert.That(target.Create(), Is.True);
                Assert.That(resolved.Create(), Is.True);
                Assert.That(target.antiAliasing, Is.EqualTo(samples));
                material.SetTexture("_MainTex", artworkTexture);
                material.SetTexture("_PanelBaseTex", Texture2D.whiteTexture);
                material.SetTextureScale("_MainTex", Vector2.one);
                material.SetTextureOffset("_MainTex", Vector2.zero);
                material.SetFloat("_FillAlpha", 1f);
                material.SetFloat("_ReferenceOpacity", 0.85f);
                material.SetVector("_FillSampleUV", new Vector4(0.5f, 0.5f, 0f, 0f));
                material.SetFloat("_TintStrength", 0.4f);
                material.SetFloat("_FrostStrength", 0.08f);
                material.SetFloat("_ListDepthOffset", 0f);
                material.SetFloat("_GlassHighlight", 0f);
                material.SetFloat("_GlassRefraction", 0f);
                material.SetVector("_GlassShape", Vector4.zero);
                material.SetMatrix("_GlassWorldToLocal", Matrix4x4.identity);
                material.SetVector("_LiquidGlassRect", new Vector4(0.5f, 0.5f, 0.5f, 0.5f));
                material.SetVector("_LiquidGlassShape", new Vector4(0.5f, 0f, 0f, 0f));
                material.SetVector("_LiquidGlassCorners", Vector4.one * 0.5f);
                material.SetVector("_LiquidGlassOptics", new Vector4(0.1f, 0.1f, 0.04f, 0f));
                material.SetFloat("_BackdropIsRoundedRect", 0f);
                material.DisableKeyword("UNITY_UI_CLIP_RECT");
                material.DisableKeyword("UNITY_UI_ALPHACLIP");
                Shader.SetGlobalVector("_ScreenParams", new Vector4(TargetSize, TargetSize, 1f + 1f / TargetSize, 1f + 1f / TargetSize));
                Shader.SetGlobalFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
                Shader.SetGlobalFloat("_MajTextureMipBias", 0f);
                Shader.SetGlobalInt("_UIVertexColorAlwaysGammaSpace", 0);
                Shader.SetGlobalVector("_TextureSampleAdd", Vector4.zero);
                Shader.SetGlobalTexture("_MajPvBlurTexture", blurTexture);
                Shader.SetGlobalTexture("_MajUiButtonBlurTexture", blurTexture);
                Shader.SetGlobalVector("_MajUiButtonBlurTexture_TexelSize", Vector4.one);
                Shader.SetGlobalVector("_MajPvBlurTexture_TexelSize", Vector4.one);
                s_liquidStyle = liquid;

                foreach (var background in new[] { Color.black, Color.white, new Color(0.2f, 0.4f, 0.6f, 1f) })
                {
                    blurTexture.SetPixel(0, 0, background);
                    blurTexture.Apply(false, false);
                    var sampled = blurTexture.GetPixel(0, 0);
                    Shader.SetGlobalFloat("_MajPvBlurReady", 1f);
                    Shader.SetGlobalFloat("_MajUiButtonBlurReady", 0f);
                    // Mutate the same texture after material creation: no C# palette property or
                    // material rebuild can explain a successful color change in these draws.
                    foreach (var fill in new[] { new Color(1f, 249f / 255f, 231f / 255f, 1f),
                        new Color(0.3f, 0.8f, 0.5f, 1f), Color.white, Color.black })
                    {
                        artworkTexture.SetPixel(0, 0, fill);
                        artworkTexture.Apply(false, false);
                        var sampledFill = artworkTexture.GetPixel(0, 0);
                        material.SetFloat("_BackdropIsCategory", 0f);
                        material.SetFloat("_BackdropIsButton", 0f);
                        material.SetFloat("_TintStrength", 0.4f);
                        material.SetFloat("_FrostStrength", 0.08f);
                        Shader.SetGlobalFloat("_MajPvBlurReady", 1f);
                        var graphic = new Color(0.8f, 0.9f, 1f, 0.85f);
                        material.SetColor("_Color", graphic);
                        var multiplied = (liquid ? sampled : Color.Lerp(sampled, Color.white, 0.08f))
                            * (liquid ? sampledFill * graphic : Color.Lerp(Color.white, sampledFill * graphic, 0.4f));
                        multiplied.a = 1f;
                        AssertColor(DrawAndReadCenter(material, target, resolved, readback, samples), multiplied,
                            $"{samples}x artwork {sampledFill} and Graphic RGB multiply the blurred source {sampled}");
                        foreach (var fade in new[] { 0f, 0.25f, 0.5f })
                        {
                            graphic.a = 0.85f * fade;
                            material.SetColor("_Color", graphic);
                            var opacity = Mathf.Clamp01(Mathf.Round(graphic.a * 255f) / 255f / 0.85f);
                            var expected = multiplied * opacity + s_background * (1f - opacity);
                            expected.a = opacity + s_background.a * (1f - opacity);
                            AssertColor(DrawAndReadCenter(material, target, resolved, readback, samples), expected,
                                $"{samples}x relative Image fade {fade} retains premultiplied source-over composition");
                        }
                    }

                    artworkTexture.SetPixel(0, 0, Color.white);
                    artworkTexture.Apply(false, false);
                    material.SetColor("_Color", new Color(1f, 1f, 1f, 0.85f));
                    material.SetFloat("_TintStrength", 1f);
                    material.SetFloat("_FrostStrength", 0.02f);
                    var whitePlate = liquid ? sampled : Color.Lerp(sampled, Color.white, 0.02f);
                    whitePlate.a = 1f;
                    AssertColor(DrawAndReadCenter(material, target, resolved, readback, samples), whitePlate,
                        $"{samples}x submonitor white plate retains its style's captured-background policy");

                    foreach (var category in new[] { true, false })
                    {
                        var fill = category ? new Color(159f / 255f, 176f / 255f, 188f / 255f, 1f)
                            : new Color(79f / 255f, 87f / 255f, 99f / 255f, 1f);
                        artworkTexture.SetPixel(0, 0, fill);
                        artworkTexture.Apply(false, false);
                        material.SetFloat("_FrostStrength", 0f);
                        material.SetFloat("_BackdropIsCategory", category ? 1f : 0f);
                        material.SetFloat("_BackdropIsButton", category ? 0f : 1f);
                        Shader.SetGlobalFloat("_MajPvBlurReady", category ? 1f : 0f);
                        Shader.SetGlobalFloat("_MajUiButtonBlurReady", category ? 0f : 1f);
                        var expected = sampled * artworkTexture.GetPixel(0, 0);
                        expected.a = 1f;
                        AssertColor(DrawAndReadCenter(material, target, resolved, readback, samples), expected,
                            $"{samples}x {(category ? "category" : "button")} multiplies actual artwork and retains its Ready route");
                    }
                }
                material.SetFloat("_BackdropIsCategory", 0f);
                material.SetFloat("_BackdropIsButton", 0f);
                material.SetTexture("_PanelBaseTex", coverageTexture);
                material.SetFloat("_FillAlpha", 128f / 255f);
                Shader.SetGlobalFloat("_MajPvBlurReady", 1f);
                foreach (var alpha in new[] { 0f, 64f / 255f, 128f / 255f })
                {
                    var fill = new Color(0.3f, 0.8f, 0.5f, alpha);
                    artworkTexture.SetPixel(0, 0, fill);
                    artworkTexture.Apply(false, false);
                    coverageTexture.SetPixel(0, 0, fill);
                    coverageTexture.Apply(false, false);
                    var coverage = Mathf.Clamp01(coverageTexture.GetPixel(0, 0).a / (128f / 255f));
                    var background = blurTexture.GetPixel(0, 0);
                    var surface = background * artworkTexture.GetPixel(0, 0);
                    var expected = surface * coverage + s_background * (1f - coverage);
                    expected.a = coverage + s_background.a * (1f - coverage);
                    AssertColor(DrawAndReadCenter(material, target, resolved, readback, samples), expected,
                        $"{samples}x authored alpha {alpha} preserves normalized body and feather coverage");
                }
                if (liquid)
                {
                    // Keep a white fill anchor separate from fully opaque black ink. Place
                    // that ink in the wide curved bevel, beyond the narrow white rim/lip.
                    var inkPixels = new Color[TargetSize * TargetSize];
                    for (var y = 0; y < TargetSize; y++)
                    {
                        for (var x = 0; x < TargetSize; x++)
                        {
                            inkPixels[y * TargetSize + x] = y >= 12 && y <= 19 && (x <= 3 || x >= 28)
                                ? Color.black : Color.white;
                        }
                    }
                    inkTexture.SetPixels(inkPixels);
                    inkTexture.Apply(false, false);
                    blurTexture.SetPixel(0, 0, new Color(0.8f, 0.8f, 0.8f, 1f));
                    blurTexture.Apply(false, false);
                    material.SetTexture("_MainTex", inkTexture);
                    material.SetTexture("_PanelBaseTex", Texture2D.whiteTexture);
                    material.SetColor("_Color", new Color(0.3f, 0.4f, 0.5f, 1f));
                    material.SetFloat("_FillAlpha", 1f);
                    material.SetFloat("_ReferenceOpacity", 1f);
                    material.SetVector("_LiquidGlassOptics", new Vector4(0.2f, 0.24f, 0.003f, 0.002f));
                    material.SetFloat("_GlassHighlight", 0f);
                    material.SetFloat("_GlassRefraction", 0f);
                    DrawAndReadCenter(material, target, resolved, readback, samples);
                    var noSheen = readback.GetPixels();
                    material.SetFloat("_GlassRefraction", 0.008f);
                    DrawAndReadCenter(material, target, resolved, readback, samples);
                    foreach (var x in new[] { 2, 29 })
                    {
                        var before = noSheen[15 * TargetSize + x];
                        AssertColor(before, Color.black, "The authored ink fixture must be fully opaque before enabling the curved surface");
                        AssertColor(readback.GetPixel(x, 15), before,
                            "Angular reflection must shade the transmitted background, not brighten opaque foreground ink");
                    }
                    AssertColor(readback.GetPixel(2, 20), noSheen[20 * TargetSize + 2],
                        "Without matched baked optics, changing refraction strength must not create substitute bevel sheen");
                }
                Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
            }
            finally
            {
                RenderTexture.active = savedTarget;
                Shader.SetGlobalVector("_ScreenParams", savedScreenParams);
                Shader.SetGlobalFloat("unity_GUIZTestMode", savedZTest);
                Shader.SetGlobalFloat("_MajTextureMipBias", savedMipBias);
                Shader.SetGlobalInt("_UIVertexColorAlwaysGammaSpace", savedGamma);
                Shader.SetGlobalVector("_TextureSampleAdd", savedTextureAdd);
                Shader.SetGlobalTexture("_MajPvBlurTexture", savedBlur);
                Shader.SetGlobalTexture("_MajUiButtonBlurTexture", savedButtonBlur);
                Shader.SetGlobalVector("_MajUiButtonBlurTexture_TexelSize", savedButtonBlurSize);
                Shader.SetGlobalVector("_MajPvBlurTexture_TexelSize", savedBlurSize);
                Shader.SetGlobalFloat("_MajPvBlurReady", savedPanelReady);
                Shader.SetGlobalFloat("_MajUiButtonBlurReady", savedButtonReady);
                s_liquidStyle = savedLiquidStyle;
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(artworkTexture);
                Object.DestroyImmediate(inkTexture);
                Object.DestroyImmediate(coverageTexture);
                Object.DestroyImmediate(blurTexture);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(resolved);
                Object.DestroyImmediate(readback);
            }
        }

        /// <summary>Checks Liquid own and inherited filters follow mutated artwork while retaining each child's base color.</summary>
        /// <param name="samples">The actual scene-target sample count.</param>
        [TestCase(1)]
        [TestCase(4)]
        [Category("GPU")]
        public void LiquidLayerTint_SamplesChangedParentArtworkAndPreservesOwnDarkBase(int samples)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("Layered artwork filtering requires a real graphics device.");
            }
            var descriptor = new RenderTextureDescriptor(TargetSize, TargetSize, RenderTextureFormat.ARGB32, 24)
            {
                msaaSamples = samples,
                sRGB = false
            };
            Assert.That(SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor), Is.EqualTo(samples));
            var shader = Shader.Find("Maj/UI/PV Glass");
            Assert.That(shader, Is.Not.Null);
            var savedTarget = RenderTexture.active;
            var savedBlur = Shader.GetGlobalTexture("_MajPvBlurTexture");
            var savedPanelBlur = Shader.GetGlobalTexture("_MajUiPanelBlurTexture");
            var savedButtonBlur = Shader.GetGlobalTexture("_MajUiButtonBlurTexture");
            var savedGamma = Shader.GetGlobalInt("_UIVertexColorAlwaysGammaSpace");
            var savedFloats = new System.Collections.Generic.Dictionary<string, float>();
            foreach (var name in new[] { "unity_GUIZTestMode", "_MajTextureMipBias", "_MajPvBlurReady", "_MajUiButtonBlurReady", "_MajUiPanelBlurReady" })
            {
                savedFloats.Add(name, Shader.GetGlobalFloat(name));
            }
            var savedVectors = new System.Collections.Generic.Dictionary<string, Vector4>();
            foreach (var name in new[] { "_ScreenParams", "_TextureSampleAdd", "_MajPvBlurTexture_TexelSize", "_MajUiPanelBlurTexture_TexelSize", "_MajUiButtonBlurTexture_TexelSize" })
            {
                savedVectors.Add(name, Shader.GetGlobalVector(name));
            }
            var material = new Material(shader);
            Assert.That(material.GetFloat("_LiquidArtworkBlend"), Is.EqualTo(0.25f));
            Assert.That(material.GetFloat("_LiquidTintStrength"), Is.EqualTo(0.9f));
            Assert.That(material.GetFloat("_GaussianButtonTintStrength"), Is.EqualTo(0.65f));
            var artwork = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            var parentArtwork = new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            var capture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            var target = new RenderTexture(descriptor);
            var resolved = new RenderTexture(TargetSize, TargetSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(TargetSize, TargetSize, TextureFormat.RGBA32, false, true);
            try
            {
                Assert.That(target.Create(), Is.True);
                Assert.That(resolved.Create(), Is.True);
                material.SetTexture("_MainTex", artwork);
                material.SetTexture("_PanelBaseTex", Texture2D.whiteTexture);
                material.SetColor("_Color", Color.white);
                material.SetFloat("_FillAlpha", 1f);
                material.SetFloat("_ReferenceOpacity", 1f);
                material.SetVector("_FillSampleUV", new Vector4(0.5f, 0.5f, 0f, 0f));
                material.SetFloat("_GlassHighlight", 0f);
                material.SetFloat("_GlassRefraction", 0f);
                material.SetMatrix("_GlassWorldToLocal", Matrix4x4.identity);
                material.SetVector("_LiquidGlassRect", new Vector4(0.5f, 0.5f, 0.5f, 0.5f));
                material.SetVector("_LiquidGlassCorners", Vector4.one * 0.5f);
                material.SetVector("_LiquidGlassOptics", new Vector4(0.1f, 0.12f, 0f, 0f));
                material.SetTexture("_LiquidTintSourceTex", parentArtwork);
                material.SetVector("_LiquidTintSourceUV", new Vector4(0.375f, 0.875f, 0f, 0f));
                material.DisableKeyword("UNITY_UI_CLIP_RECT");
                material.DisableKeyword("UNITY_UI_ALPHACLIP");
                Shader.SetGlobalVector("_ScreenParams", new Vector4(TargetSize, TargetSize, 1f + 1f / TargetSize, 1f + 1f / TargetSize));
                Shader.SetGlobalFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
                Shader.SetGlobalFloat("_MajTextureMipBias", 0f);
                Shader.SetGlobalInt("_UIVertexColorAlwaysGammaSpace", 0);
                Shader.SetGlobalVector("_TextureSampleAdd", Vector4.zero);
                Shader.SetGlobalTexture("_MajPvBlurTexture", capture);
                Shader.SetGlobalTexture("_MajUiButtonBlurTexture", capture);
                Shader.SetGlobalVector("_MajUiButtonBlurTexture_TexelSize", Vector4.one);
                Shader.SetGlobalVector("_MajPvBlurTexture_TexelSize", Vector4.one);
                Shader.SetGlobalFloat("_MajPvBlurReady", 1f);
                s_liquidStyle = true;
                foreach (var background in new[] { Color.white, new Color(0.2f, 0.4f, 0.6f, 1f) })
                {
                    capture.SetPixel(0, 0, background);
                    capture.Apply(false, false);
                    var sampledBackground = capture.GetPixel(0, 0);
                    foreach (var parentColor in new[] { new Color(1f, 0.9f, 0.6f, 1f), new Color(0.25f, 0.85f, 0.35f, 1f),
                        Color.white, new Color(1f, 249f / 255f, 231f / 255f, 1f) })
                    {
                        artwork.SetPixel(0, 0, parentColor);
                        artwork.Apply(false, false);
                        material.SetFloat("_BackdropIsButton", 0f);
                        material.SetFloat("_BackdropIsCategory", 0f);
                        s_liquidStyle = true;
                        material.SetFloat("_LiquidTintStrength", 0.23f);
                        material.SetVector("_LiquidTintSourceColor", Vector4.zero);
                        var ownFill = artwork.GetPixel(0, 0);
                        var largePanel = Color.Lerp(sampledBackground * Color.Lerp(Color.white, ownFill, 0.23f), ownFill, 0.25f);
                        largePanel.a = 1f;
                        var renderedLargePanel = DrawAndReadCenter(material, target, resolved, readback, samples);
                        AssertColor(renderedLargePanel, largePanel,
                            "The large panel mixes 25 percent raw artwork with 75 percent texture-tinted transmission");
                        if (background == Color.white && parentColor == new Color(1f, 249f / 255f, 231f / 255f, 1f)
                            && QualitySettings.activeColorSpace == ColorSpace.Gamma)
                        {
                            const float byteTolerance = 1f / 255f + 0.00001f;
                            Assert.That(renderedLargePanel.r, Is.EqualTo(1f).Within(byteTolerance));
                            Assert.That(renderedLargePanel.g, Is.EqualTo(252f / 255f).Within(byteTolerance));
                            Assert.That(renderedLargePanel.b, Is.EqualTo(245f / 255f).Within(byteTolerance),
                                "White behind the authored cream fill must remain within one sRGB step of Beautiful's #FFFCF5");
                        }

                        for (var y = 0; y < 4; y++)
                        {
                            for (var x = 0; x < 4; x++)
                            {
                                parentArtwork.SetPixel(x, y, Color.magenta);
                            }
                        }
                        parentArtwork.SetPixel(1, 3, parentColor);
                        parentArtwork.Apply(false, false);
                        var parentGraphic = new Color(0.8f, 0.9f, 1f, 1f);
                        var parentFilter = Color.Lerp(Color.white, parentArtwork.GetPixel(1, 3) * parentGraphic, 0.14f);
                        material.SetFloat("_LiquidTintStrength", 0.9f);
                        material.SetVector("_LiquidTintSourceColor", new Vector4(parentGraphic.r, parentGraphic.g, parentGraphic.b, 0.14f));
                        foreach (var childColor in new[] { new Color(0.3f, 0.35f, 0.4f, 1f), Color.white })
                        {
                            artwork.SetPixel(0, 0, childColor);
                            artwork.Apply(false, false);
                            var childFill = artwork.GetPixel(0, 0);
                            var expected = Color.Lerp(sampledBackground * Color.Lerp(Color.white, childFill, 0.9f) * parentFilter,
                                childFill, 0.25f);
                            expected.a = 1f;
                            AssertColor(DrawAndReadCenter(material, target, resolved, readback, samples), expected,
                                "Liquid parent tint follows actual source artwork and affects only the child's transmission share.");
                            Assert.That(material.GetTexture("_LiquidTintSourceTex"), Is.SameAs(parentArtwork),
                                "Mutating the same parent texture must not require a CPU color palette or material replacement");
                        }
                    }
                }
                material.SetVector("_LiquidTintSourceColor", Vector4.zero);
                capture.SetPixel(0, 0, new Color(0.2f, 0.4f, 0.6f, 1f));
                capture.Apply(false, false);
                foreach (var childColor in new[] { new Color(0.3f, 0.35f, 0.4f, 1f), Color.white })
                {
                    artwork.SetPixel(0, 0, childColor);
                    artwork.Apply(false, false);
                    var expected = Color.Lerp(capture.GetPixel(0, 0) * Color.Lerp(Color.white, artwork.GetPixel(0, 0), 0.9f),
                        artwork.GetPixel(0, 0), 0.25f);
                    expected.a = 1f;
                    foreach (var isButton in new[] { false, true })
                    {
                        material.SetFloat("_BackdropIsButton", isButton ? 1f : 0f);
                        material.SetFloat("_BackdropIsCategory", isButton ? 0f : 1f);
                        Shader.SetGlobalFloat("_MajUiButtonBlurReady", 1f);
                        s_liquidStyle = false;
                        var gaussianStrength = isButton ? 0.65f : 0.9f;
                        var gaussianExpected = Color.Lerp(capture.GetPixel(0, 0) * Color.Lerp(Color.white, artwork.GetPixel(0, 0), gaussianStrength),
                            artwork.GetPixel(0, 0), 0.25f);
                        gaussianExpected.a = 1f;
                        var gaussian = DrawAndReadCenter(material, target, resolved, readback, samples);
                        AssertColor(gaussian, gaussianExpected, "Only Gaussian peripheral controls use the independent lighter tint cap.");
                        s_liquidStyle = true;
                        var liquid = DrawAndReadCenter(material, target, resolved, readback, samples);
                        AssertColor(liquid, expected, "Liquid retains its original 0.9 tint and 0.25 artwork share.");
                        var originalLiquidPixels = readback.GetPixels();
                        material.SetFloat("_GaussianButtonTintStrength", 0.2f);
                        DrawAndReadCenter(material, target, resolved, readback, samples);
                        Assert.That(readback.GetPixels(), Is.EqualTo(originalLiquidPixels),
                            "Changing the Gaussian-only cap must leave every Liquid RGBA pixel unchanged.");
                        s_liquidStyle = false;
                        var changedGaussian = DrawAndReadCenter(material, target, resolved, readback, samples);
                        if (!isButton)
                        {
                            AssertColor(changedGaussian, gaussian, "The peripheral-only cap must not alter Gaussian categories.");
                        }
                        else if (childColor != Color.white)
                        {
                            Assert.That(gaussian.r, Is.GreaterThan(liquid.r), "The new Gaussian default must visibly lighten a dark peripheral fill.");
                            Assert.That(changedGaussian.r, Is.GreaterThan(gaussian.r));
                        }
                        material.SetFloat("_GaussianButtonTintStrength", 0.65f);
                    }
                }
                artwork.SetPixel(0, 0, Color.white);
                artwork.Apply(false, false);
                capture.SetPixel(0, 0, Color.red);
                capture.Apply(false, false);
                var blue = new Color[16];
                for (var index = 0; index < blue.Length; index++)
                {
                    blue[index] = Color.blue;
                }
                parentArtwork.SetPixels(blue);
                parentArtwork.Apply(false, false);
                Shader.SetGlobalTexture("_MajUiPanelBlurTexture", parentArtwork);
                Shader.SetGlobalVector("_MajUiPanelBlurTexture_TexelSize", new Vector4(0.25f, 0.25f, 4f, 4f));
                material.SetColor("_Color", Color.white);
                material.SetFloat("_TintStrength", 1f);
                material.SetFloat("_FrostStrength", 0f);
                material.SetFloat("_LiquidTintStrength", 1f);
                material.SetFloat("_LiquidArtworkBlend", 0f);
                material.SetVector("_LiquidTintSourceColor", Vector4.zero);
                foreach (var liquid in new[] { false, true })
                {
                    s_liquidStyle = liquid;
                    material.SetFloat("_BackdropIsButton", 0f);
                    material.SetFloat("_BackdropIsCategory", 0f);
                    material.SetFloat("_BackdropUsesPanelBlur", 1f);
                    Shader.SetGlobalFloat("_MajPvBlurReady", 0f);
                    Shader.SetGlobalFloat("_MajUiPanelBlurReady", 1f);
                    AssertColor(DrawAndReadCenter(material, target, resolved, readback, samples), Color.blue,
                        "Strong panels must sample the blue half-size snapshot independently of the red full-size snapshot and its Ready flag.");
                    Shader.SetGlobalFloat("_MajUiPanelBlurReady", 0f);
                    Shader.SetGlobalFloat("_MajPvBlurReady", 1f);
                    AssertColor(DrawAndReadCenter(material, target, resolved, readback, samples), Color.white,
                        "An unavailable strong source must fall back to original Alpha artwork, never the other ready snapshot.");
                    material.SetFloat("_BackdropUsesPanelBlur", 0f);
                    AssertColor(DrawAndReadCenter(material, target, resolved, readback, samples), Color.red,
                        "Full-source controls must keep reading the red small-blur snapshot.");
                    material.SetFloat("_BackdropIsButton", 1f);
                    Shader.SetGlobalTexture("_MajUiButtonBlurTexture", Texture2D.blackTexture);
                    Shader.SetGlobalFloat("_MajUiButtonBlurReady", 1f);
                    Shader.SetGlobalFloat("_MajPvBlurReady", 0f);
                    AssertColor(DrawAndReadCenter(material, target, resolved, readback, samples), Color.black,
                        "RT3 uses its own texture and Ready flag independently of the category result.");
                }
                Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
            }
            finally
            {
                RenderTexture.active = savedTarget;
                Shader.SetGlobalTexture("_MajPvBlurTexture", savedBlur);
                Shader.SetGlobalTexture("_MajUiPanelBlurTexture", savedPanelBlur);
                Shader.SetGlobalTexture("_MajUiButtonBlurTexture", savedButtonBlur);
                Shader.SetGlobalInt("_UIVertexColorAlwaysGammaSpace", savedGamma);
                foreach (var pair in savedFloats)
                {
                    Shader.SetGlobalFloat(pair.Key, pair.Value);
                }
                foreach (var pair in savedVectors)
                {
                    Shader.SetGlobalVector(pair.Key, pair.Value);
                }
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(artwork);
                Object.DestroyImmediate(parentArtwork);
                Object.DestroyImmediate(capture);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(resolved);
                Object.DestroyImmediate(readback);
            }
        }

        /// <summary>Checks missing baked optics keep the source unshifted while artwork mixing preserves ink, fades and coverage.</summary>
        /// <param name="samples">The actual single- or multisample scene-target count.</param>
        [TestCase(1)]
        [TestCase(4)]
        [Category("GPU")]
        public void LiquidWithoutBakedOptics_DoesNotRefractAndArtworkMixPreservesComposition(int samples)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("Wide-band refraction and artwork mixing require a real graphics device.");
            }
            var descriptor = new RenderTextureDescriptor(TargetSize, TargetSize, RenderTextureFormat.ARGB32, 24)
            {
                msaaSamples = samples,
                sRGB = false
            };
            Assert.That(SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor), Is.EqualTo(samples));
            var shader = Shader.Find("Maj/UI/PV Glass");
            Assert.That(shader, Is.Not.Null);
            var savedTarget = RenderTexture.active;
            var savedBlur = Shader.GetGlobalTexture("_MajPvBlurTexture");
            var savedGamma = Shader.GetGlobalInt("_UIVertexColorAlwaysGammaSpace");
            var savedFloats = new System.Collections.Generic.Dictionary<string, float>();
            foreach (var name in new[] { "unity_GUIZTestMode", "_MajTextureMipBias", "_MajPvBlurReady" })
            {
                savedFloats.Add(name, Shader.GetGlobalFloat(name));
            }
            var savedVectors = new System.Collections.Generic.Dictionary<string, Vector4>();
            foreach (var name in new[] { "_ScreenParams", "_TextureSampleAdd", "_MajPvBlurTexture_TexelSize" })
            {
                savedVectors.Add(name, Shader.GetGlobalVector(name));
            }
            var material = new Material(shader);
            Assert.That(material.GetFloat("_LiquidArtworkBlend"), Is.EqualTo(0.25f));
            Assert.That(material.GetFloat("_LiquidTintStrength"), Is.EqualTo(0.9f));
            var ramp = new Texture2D(128, 128, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Bilinear };
            var capture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            var ink = new Texture2D(TargetSize, TargetSize, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            var target = new RenderTexture(descriptor);
            var resolved = new RenderTexture(TargetSize, TargetSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(TargetSize, TargetSize, TextureFormat.RGBA32, false, true);
            try
            {
                Assert.That(target.Create(), Is.True);
                Assert.That(resolved.Create(), Is.True);
                var rampPixels = new Color[128 * 128];
                for (var y = 0; y < 128; y++)
                {
                    for (var x = 0; x < 128; x++)
                    {
                        var value = 0.1f + 0.8f * (x + 0.5f) / 128f;
                        rampPixels[y * 128 + x] = new Color(value, value, value, 1f);
                    }
                }
                ramp.SetPixels(rampPixels);
                ramp.Apply(false, false);
                material.SetTexture("_MainTex", Texture2D.whiteTexture);
                material.SetTexture("_PanelBaseTex", Texture2D.whiteTexture);
                material.SetColor("_Color", Color.white);
                material.SetFloat("_FillAlpha", 1f);
                material.SetFloat("_ReferenceOpacity", 1f);
                material.SetVector("_FillSampleUV", new Vector4(0.1f, 0.1f, 0f, 0f));
                material.SetFloat("_LiquidTintStrength", 1f);
                material.SetFloat("_LiquidArtworkBlend", 0f);
                material.SetVector("_LiquidTintSourceColor", Vector4.zero);
                material.SetFloat("_GlassHighlight", 0f);
                material.SetMatrix("_GlassWorldToLocal", Matrix4x4.identity);
                const float radius = 0.4f;
                material.SetVector("_LiquidGlassRect", new Vector4(0.5f, 0.5f, radius, radius));
                material.SetVector("_LiquidGlassShape", new Vector4(radius, 1f, 0f, 0f));
                material.SetVector("_LiquidGlassCorners", Vector4.one * radius);
                material.SetVector("_LiquidGlassOptics", new Vector4(radius * 0.9f, radius * 0.9f * 1.2f, 0f, 0f));
                material.DisableKeyword("UNITY_UI_CLIP_RECT");
                material.DisableKeyword("UNITY_UI_ALPHACLIP");
                Shader.SetGlobalVector("_ScreenParams", new Vector4(TargetSize, TargetSize, 1f + 1f / TargetSize, 1f + 1f / TargetSize));
                Shader.SetGlobalFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
                Shader.SetGlobalFloat("_MajTextureMipBias", 0f);
                Shader.SetGlobalInt("_UIVertexColorAlwaysGammaSpace", 0);
                Shader.SetGlobalVector("_TextureSampleAdd", Vector4.zero);
                Shader.SetGlobalTexture("_MajPvBlurTexture", ramp);
                Shader.SetGlobalVector("_MajPvBlurTexture_TexelSize", new Vector4(1f / 128f, 1f / 128f, 128f, 128f));
                Shader.SetGlobalFloat("_MajPvBlurReady", 1f);
                s_liquidStyle = true;
                material.SetFloat("_GlassRefraction", 0f);
                DrawAndReadCenter(material, target, resolved, readback, samples);
                var straight = readback.GetPixels();
                material.SetFloat("_GlassRefraction", 0.008f);
                DrawAndReadCenter(material, target, resolved, readback, samples);
                var wide = readback.GetPixel(20, 16);
                var normalizedDepth = (radius - new Vector2(20.5f / TargetSize - 0.5f,
                    16.5f / TargetSize - 0.5f).magnitude) / radius;
                var measuredSourceShift = (straight[16 * TargetSize + 20].r - wide.r) / 0.8f;
                TestContext.WriteLine($"WIDE_LENS samples={samples} depthOverRadius={normalizedDepth:F6} rampDerivedSourceUvShift={measuredSourceShift:F6}");
                Assert.That(normalizedDepth, Is.InRange(0.6f, 0.73f), "Probe about two-thirds of the radius inward, one-third of the full height.");
                AssertColor(wide, straight[16 * TargetSize + 20],
                    "An unbaked synthetic contour must not silently run analytic wide-band refraction.");
                AssertColor(readback.GetPixel(16, 16), straight[16 * TargetSize + 16], "The central region beyond 0.9R remains flat");
                material.SetVector("_LiquidGlassOptics", new Vector4(0.12f, 0.144f, 0f, 0f));
                DrawAndReadCenter(material, target, resolved, readback, samples);
                AssertColor(readback.GetPixel(20, 16), straight[16 * TargetSize + 20], "The narrow-band control must leave this deep probe unchanged");

                Shader.SetGlobalTexture("_MajPvBlurTexture", capture);
                Shader.SetGlobalVector("_MajPvBlurTexture_TexelSize", Vector4.one);
                material.SetFloat("_GlassRefraction", 0f);
                material.SetFloat("_ReferenceOpacity", 0.85f);
                foreach (var artworkShare in new[] { 0.25f, 0.5f })
                {
                    material.SetFloat("_LiquidArtworkBlend", artworkShare);
                    var tintStrength = artworkShare == 0.5f ? 0.42f : 0.9f;
                    material.SetFloat("_LiquidTintStrength", tintStrength);
                    foreach (var background in new[] { Color.black, new Color(0.5f, 0.5f, 0.5f, 1f),
                        new Color(0.2f, 0.4f, 0.6f, 1f), Color.white })
                    {
                        capture.SetPixel(0, 0, background);
                        capture.Apply(false, false);
                        foreach (var authoredColor in new[] { Color.white, new Color(79f / 255f, 87f / 255f, 99f / 255f, 1f),
                            new Color(1f, 249f / 255f, 231f / 255f, 1f) })
                        {
                            var transmission = capture.GetPixel(0, 0) * Color.Lerp(Color.white, authoredColor, tintStrength);
                            var mixed = Color.Lerp(transmission, authoredColor, artworkShare);
                            foreach (var fade in new[] { 1f, 0.5f, 0f })
                            {
                                var alpha = 0.85f * fade;
                                material.SetColor("_Color", new Color(authoredColor.r, authoredColor.g, authoredColor.b, alpha));
                                var opacity = Mathf.Clamp01(Mathf.Round(alpha * 255f) / 255f / 0.85f);
                                var expected = mixed * opacity + s_background * (1f - opacity);
                                expected.a = opacity + s_background.a * (1f - opacity);
                                var actual = DrawAndReadCenter(material, target, resolved, readback, samples);
                                AssertColor(actual, expected,
                                    "Actual RGB artwork share mixes after transmission tint, without a hidden neutral-white correction, and preserves relative fades");
                                if (background == Color.white && authoredColor.r == 79f / 255f && artworkShare == 0.25f && fade == 1f)
                                {
                                    Assert.That(actual.r, Is.GreaterThan(authoredColor.r));
                                    Assert.That(actual.r - authoredColor.r, Is.LessThan(0.075f),
                                        "Default dark controls may be slightly lighter than their Beautiful artwork, not washed toward white.");
                                }
                            }
                        }
                    }
                }
                material.SetColor("_Color", new Color(1f, 1f, 1f, 0.5f));
                Shader.SetGlobalFloat("_MajPvBlurReady", 0f);
                var fallback = Color.white * 0.5f + s_background * 0.5f;
                fallback.a = 0.5f + s_background.a * 0.5f;
                AssertColor(DrawAndReadCenter(material, target, resolved, readback, samples), fallback,
                    "An unavailable snapshot must retain original Alpha composition without applying the artwork mix");
                Shader.SetGlobalFloat("_MajPvBlurReady", 1f);
                var inkPixels = new Color[TargetSize * TargetSize];
                for (var y = 0; y < TargetSize; y++)
                {
                    for (var x = 0; x < TargetSize; x++)
                    {
                        inkPixels[y * TargetSize + x] = x >= 12 && x <= 19 && y >= 12 && y <= 19 ? Color.black : Color.white;
                    }
                }
                ink.SetPixels(inkPixels);
                ink.Apply(false, false);
                material.SetTexture("_MainTex", ink);
                material.SetColor("_Color", Color.white);
                material.SetFloat("_ReferenceOpacity", 1f);
                AssertColor(DrawAndReadCenter(material, target, resolved, readback, samples), Color.black,
                    "The RGB artwork mix belongs below foreground decoration and must not whiten authored opaque ink");
                Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
            }
            finally
            {
                RenderTexture.active = savedTarget;
                Shader.SetGlobalTexture("_MajPvBlurTexture", savedBlur);
                Shader.SetGlobalInt("_UIVertexColorAlwaysGammaSpace", savedGamma);
                foreach (var pair in savedFloats)
                {
                    Shader.SetGlobalFloat(pair.Key, pair.Value);
                }
                foreach (var pair in savedVectors)
                {
                    Shader.SetGlobalVector(pair.Key, pair.Value);
                }
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(ramp);
                Object.DestroyImmediate(capture);
                Object.DestroyImmediate(ink);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(resolved);
                Object.DestroyImmediate(readback);
            }
        }

        /// <summary>Checks the three calibrated board bodies match actual Beautiful alpha rendering over solid captures.</summary>
        /// <param name="samples">The actual scene-target sample count.</param>
        [TestCase(1)]
        [TestCase(4)]
        [Category("GPU")]
        public void LiquidBoardCalibration_MatchesBeautifulOverWhiteBlackAndColoredCaptures(int samples)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("Board calibration requires a real graphics device.");
            }
            Assert.That(QualitySettings.activeColorSpace, Is.EqualTo(ColorSpace.Gamma), "This calibration compares current Gamma-project sRGB output steps.");
            var descriptor = new RenderTextureDescriptor(TargetSize, TargetSize, RenderTextureFormat.ARGB32, 24)
            {
                msaaSamples = samples,
                sRGB = false
            };
            Assert.That(SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor), Is.EqualTo(samples));
            Sprite? sprite = null;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/Ribbon/newui@3x.png"))
            {
                if (asset is Sprite candidate && candidate.name == "newui@3x_22")
                {
                    sprite = candidate;
                    break;
                }
            }
            Assert.That(sprite, Is.Not.Null);
            var shader = Shader.Find("Maj/UI/PV Glass");
            var alphaShader = Shader.Find("Maj/UI/Atlas Alpha");
            Assert.That(shader, Is.Not.Null);
            Assert.That(alphaShader, Is.Not.Null);
            var savedTarget = RenderTexture.active;
            var savedBlur = Shader.GetGlobalTexture("_MajPvBlurTexture");
            var savedGamma = Shader.GetGlobalInt("_UIVertexColorAlwaysGammaSpace");
            var savedFloats = new System.Collections.Generic.Dictionary<string, float>();
            foreach (var name in new[] { "unity_GUIZTestMode", "_MajTextureMipBias", "_MajPvBlurReady" })
            {
                savedFloats.Add(name, Shader.GetGlobalFloat(name));
            }
            var savedVectors = new System.Collections.Generic.Dictionary<string, Vector4>();
            foreach (var name in new[] { "_ScreenParams", "_TextureSampleAdd", "_MajPvBlurTexture_TexelSize" })
            {
                savedVectors.Add(name, Shader.GetGlobalVector(name));
            }
            var material = new Material(shader);
            var alpha = new Material(alphaShader);
            var capture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            var target = new RenderTexture(descriptor);
            var resolved = new RenderTexture(TargetSize, TargetSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(TargetSize, TargetSize, TextureFormat.RGBA32, false, true);
            try
            {
                Assert.That(target.Create(), Is.True);
                Assert.That(resolved.Create(), Is.True);
                var uv = UnityEngine.Sprites.DataUtility.GetOuterUV(sprite);
                var anchor = new Vector2((uv.x + uv.z) * 0.5f, (uv.y + uv.w) * 0.5f);
                foreach (var current in new[] { material, alpha })
                {
                    current.SetTexture("_MainTex", sprite!.texture);
                    current.SetTextureScale("_MainTex", Vector2.zero);
                    current.SetTextureOffset("_MainTex", anchor);
                    current.SetColor("_Color", new Color(1f, 1f, 1f, 0.85f));
                    current.DisableKeyword("UNITY_UI_CLIP_RECT");
                    current.DisableKeyword("UNITY_UI_ALPHACLIP");
                }
                material.SetTexture("_PanelBaseTex", sprite!.texture);
                material.SetVector("_FillSampleUV", new Vector4(anchor.x, anchor.y, 0f, 0f));
                material.SetFloat("_FillAlpha", 127f / 255f);
                material.SetFloat("_ReferenceOpacity", 0.85f);
                material.SetFloat("_LiquidTintStrength", 0f);
                material.SetFloat("_LiquidArtworkBlend", 0.4233333f);
                material.SetVector("_LiquidTintSourceColor", Vector4.zero);
                material.SetFloat("_GlassRefraction", 0f);
                material.SetFloat("_GlassHighlight", 0f);
                material.SetMatrix("_GlassWorldToLocal", Matrix4x4.identity);
                material.SetVector("_LiquidGlassRect", new Vector4(0.5f, 0.5f, 1f, 1f));
                material.SetVector("_LiquidGlassCorners", Vector4.one * 0.1f);
                material.SetVector("_LiquidGlassOptics", new Vector4(0.1f, 0.12f, 0f, 0f));
                Shader.SetGlobalVector("_ScreenParams", new Vector4(TargetSize, TargetSize, 1f + 1f / TargetSize, 1f + 1f / TargetSize));
                Shader.SetGlobalFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
                Shader.SetGlobalFloat("_MajTextureMipBias", 0f);
                Shader.SetGlobalInt("_UIVertexColorAlwaysGammaSpace", 0);
                Shader.SetGlobalVector("_TextureSampleAdd", Vector4.zero);
                Shader.SetGlobalTexture("_MajPvBlurTexture", capture);
                Shader.SetGlobalVector("_MajPvBlurTexture_TexelSize", Vector4.one);
                Shader.SetGlobalFloat("_MajPvBlurReady", 1f);
                s_liquidStyle = true;
                alpha.SetColor("_Color", Color.white);
                var rawPremultiplied = DrawAndReadCenter(alpha, target, resolved, readback, samples, Color.clear);
                Assert.That(rawPremultiplied.a, Is.EqualTo(127f / 255f).Within(1f / 255f + 0.00001f));
                var raw = rawPremultiplied / rawPremultiplied.a;
                raw.a = 1f;
                alpha.SetColor("_Color", new Color(1f, 1f, 1f, 0.85f));
                const float byteTolerance = 1f / 255f + 0.00001f;
                foreach (var background in new[] { Color.white, Color.black, new Color(0.15f, 0.45f, 0.75f, 1f),
                    new Color(0.8f, 0.2f, 0.4f, 1f) })
                {
                    capture.SetPixel(0, 0, background);
                    capture.Apply(false, false);
                    var sampled = capture.GetPixel(0, 0);
                    var reference = DrawAndReadCenter(alpha, target, resolved, readback, samples, sampled);
                    var actual = DrawAndReadCenter(material, target, resolved, readback, samples, sampled);
                    var expected = Color.Lerp(sampled, raw, 127f / 255f * 0.85f);
                    for (var channel = 0; channel < 3; channel++)
                    {
                        Assert.That(actual[channel], Is.EqualTo(reference[channel]).Within(byteTolerance),
                            "The liquid board's body must match actual Beautiful Atlas Alpha output within one sRGB step on the same capture.");
                        Assert.That(actual[channel], Is.EqualTo(expected[channel]).Within(byteTolerance),
                            "Calibration uses original fill opacity, without multiplying the yellow tint a second time.");
                    }
                    Assert.That(actual.a, Is.EqualTo(1f).Within(byteTolerance));
                    TestContext.WriteLine($"BOARD_CALIBRATION samples={samples} background={ColorUtility.ToHtmlStringRGB(sampled)} liquid={ColorUtility.ToHtmlStringRGB(actual)} beautiful={ColorUtility.ToHtmlStringRGB(reference)}");
                }
                Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
                Assert.That(ShaderUtil.ShaderHasError(alphaShader), Is.False);
            }
            finally
            {
                RenderTexture.active = savedTarget;
                Shader.SetGlobalTexture("_MajPvBlurTexture", savedBlur);
                Shader.SetGlobalInt("_UIVertexColorAlwaysGammaSpace", savedGamma);
                foreach (var pair in savedFloats)
                {
                    Shader.SetGlobalFloat(pair.Key, pair.Value);
                }
                foreach (var pair in savedVectors)
                {
                    Shader.SetGlobalVector(pair.Key, pair.Value);
                }
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(alpha);
                Object.DestroyImmediate(capture);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(resolved);
                Object.DestroyImmediate(readback);
            }
        }

        /// <summary>Checks missing baked optics retain original artwork, unshifted coordinates and the circular screen boundary.</summary>
        [Test]
        [Category("GPU")]
        public void LiquidWithoutBakedOptics_PreservesArtworkAndUnshiftedCoordinatesUnderTransforms()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("The projected lens reference requires a real graphics device.");
            }
            var shader = Shader.Find("Maj/UI/PV Glass");
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True);
            var savedTarget = RenderTexture.active;
            var savedBlur = Shader.GetGlobalTexture("_MajPvBlurTexture");
            var savedGamma = Shader.GetGlobalInt("_UIVertexColorAlwaysGammaSpace");
            var savedFloats = new System.Collections.Generic.Dictionary<string, float>();
            foreach (var name in new[] { "unity_GUIZTestMode", "_MajTextureMipBias", "_MajPvBlurReady" })
            {
                savedFloats.Add(name, Shader.GetGlobalFloat(name));
            }
            var savedVectors = new System.Collections.Generic.Dictionary<string, Vector4>();
            foreach (var name in new[] { "_ScreenParams", "_TextureSampleAdd", "_MajPvBlurTexture_TexelSize" })
            {
                savedVectors.Add(name, Shader.GetGlobalVector(name));
            }
            var material = new Material(shader);
            // This fixture isolates transmission from the separately tested artwork share.
            material.SetFloat("_LiquidArtworkBlend", 0f);
            material.SetFloat("_LiquidTintStrength", 1f);
            var source = new Texture2D(128, 128, TextureFormat.RGBA32, false, true)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var target = new RenderTexture(TargetSize, TargetSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var resolved = new RenderTexture(TargetSize, TargetSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(TargetSize, TargetSize, TextureFormat.RGBA32, false, true);
            try
            {
                Assert.That(target.Create(), Is.True);
                Assert.That(resolved.Create(), Is.True);
                var pixels = new Color[128 * 128];
                for (var y = 0; y < 128; y++)
                {
                    for (var x = 0; x < 128; x++)
                    {
                        var value = 0.1f + 0.8f * (x + 0.5f) / 128f;
                        pixels[y * 128 + x] = new Color(value, value, value, 1f);
                    }
                }
                source.SetPixels(pixels);
                source.Apply(false, false);
                material.SetTexture("_MainTex", Texture2D.whiteTexture);
                material.SetTexture("_PanelBaseTex", Texture2D.whiteTexture);
                material.SetTextureScale("_MainTex", Vector2.one);
                material.SetTextureOffset("_MainTex", Vector2.zero);
                material.SetColor("_Color", Color.white);
                material.SetFloat("_FillAlpha", 1f);
                material.SetFloat("_ReferenceOpacity", 1f);
                material.SetFloat("_TintStrength", 1f);
                material.SetFloat("_FrostStrength", 0f);
                material.SetFloat("_ListDepthOffset", 0f);
                material.SetFloat("_GlassHighlight", 0f);
                material.SetFloat("_GlassRefraction", 0.008f);
                material.SetFloat("_BackdropIsButton", 0f);
                material.SetFloat("_BackdropIsCategory", 1f);
                material.SetFloat("_BackdropIsRoundedRect", 0f);
                material.SetVector("_GlassShape", Vector4.zero);
                material.SetVector("_FillSampleUV", new Vector4(0.5f, 0.5f, 0f, 0f));
                var halfSize = new Vector2(0.3f, 0.22f);
                var corners = Vector4.one * 0.1f;
                const float height = 0.15f;
                const float amount = height * 1.2f;
                material.SetVector("_LiquidGlassRect", new Vector4(0f, 0f, halfSize.x, halfSize.y));
                material.SetVector("_LiquidGlassShape", new Vector4(0.1f, 0f, 0f, 0f));
                material.SetVector("_LiquidGlassCorners", corners);
                material.SetVector("_LiquidGlassOptics", new Vector4(height, amount, 0f, 0f));
                material.DisableKeyword("UNITY_UI_CLIP_RECT");
                material.DisableKeyword("UNITY_UI_ALPHACLIP");
                Shader.SetGlobalVector("_ScreenParams", new Vector4(TargetSize, TargetSize, 1f + 1f / TargetSize, 1f + 1f / TargetSize));
                Shader.SetGlobalFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
                Shader.SetGlobalFloat("_MajTextureMipBias", 0f);
                Shader.SetGlobalInt("_UIVertexColorAlwaysGammaSpace", 0);
                Shader.SetGlobalVector("_TextureSampleAdd", Vector4.zero);
                Shader.SetGlobalTexture("_MajPvBlurTexture", source);
                Shader.SetGlobalVector("_MajPvBlurTexture_TexelSize", new Vector4(1f / 128f, 1f / 128f, 128f, 128f));
                Shader.SetGlobalFloat("_MajPvBlurReady", 1f);
                s_liquidStyle = true;
                foreach (var localToWorld in new[] {
                    Matrix4x4.TRS(new Vector3(0.5f, 0.5f, 0f), Quaternion.identity, Vector3.one),
                    Matrix4x4.TRS(new Vector3(0.5f, 0.5f, 0f), Quaternion.Euler(0f, 0f, 21f), new Vector3(1.25f, 0.8f, 1f)) })
                {
                    material.SetMatrix("_GlassWorldToLocal", localToWorld.inverse);
                    material.SetFloat("_GlassRefraction", 0f);
                    DrawAndReadCenter(material, target, resolved, readback, 1);
                    var unwarped = readback.GetPixels();
                    material.SetFloat("_GlassRefraction", 0.008f);
                    DrawAndReadCenter(material, target, resolved, readback, 1);
                    var refracted = readback.GetPixels();
                    DrawAndReadCenter(material, target, resolved, readback, 1);
                    var repeated = readback.GetPixels();
                    var largestEffect = 0f;
                    foreach (var pixel in new[] { new Vector2Int(6, 12), new Vector2Int(25, 19),
                        new Vector2Int(10, 22), new Vector2Int(21, 9) })
                    {
                        var index = pixel.y * TargetSize + pixel.x;
                        var mirror = (TargetSize - 1 - pixel.y) * TargetSize + TargetSize - 1 - pixel.x;
                        var delta = refracted[index].r - unwarped[index].r;
                        var opposite = refracted[mirror].r - unwarped[mirror].r;
                        Assert.That(delta + opposite, Is.EqualTo(0f).Within(0.015f), "A centered symmetric contour must have antisymmetric displacements.");
                    }
                    var center = TargetSize / 2 * TargetSize + TargetSize / 2;
                    AssertColor(refracted[center], unwarped[center], "The flat control center must remain stable");
                    for (var index = 0; index < refracted.Length; index++)
                    {
                        AssertColor(refracted[index], repeated[index], "Repeated draws with unchanged inputs must be deterministic");
                        var uv = new Vector3((index % TargetSize + 0.5f) / TargetSize,
                            (index / TargetSize + 0.5f) / TargetSize, 0f);
                        var local = localToWorld.inverse.MultiplyPoint3x4(uv);
                        var corner = new Vector2(Mathf.Abs(local.x), Mathf.Abs(local.y)) - halfSize + Vector2.one * 0.1f;
                        var distance = new Vector2(Mathf.Max(corner.x, 0f), Mathf.Max(corner.y, 0f)).magnitude
                            + Mathf.Min(Mathf.Max(corner.x, corner.y), 0f) - 0.1f;
                        if (distance < -0.08f)
                        {
                            Assert.That(refracted[index].r, Is.InRange(0.09f, 0.91f), "The body samples the bounded captured gradient.");
                        }
                        else if (distance > 0.08f)
                        {
                            AssertColor(refracted[index], Color.white, "Artwork outside the optical body retains its original color");
                        }
                        else
                        {
                            Assert.That(refracted[index].r, Is.InRange(0.09f, 1f), "The feather joins the captured body and original artwork continuously.");
                        }
                        if (distance < -0.03f)
                        {
                            largestEffect = Mathf.Max(largestEffect, Mathf.Abs(refracted[index].r - unwarped[index].r));
                        }
                        Assert.That(refracted[index].a, Is.EqualTo(1f).Within(0.012f));
                    }
                    Assert.That(largestEffect, Is.LessThanOrEqualTo(1f / 255f), "Missing baked optics must not generate substitute refraction under either transform.");
                }
                // Put the right straight edge on a pixel center. Decode the sampled gradient
                // through its actual AA coverage instead of comparing a copy of the lens formula.
                const float edge = 24.5f / TargetSize;
                material.SetMatrix("_GlassWorldToLocal", Matrix4x4.Translate(new Vector3(-0.5f, -0.5f, 0f)));
                material.SetVector("_LiquidGlassRect", new Vector4(0f, 0f, edge - 0.5f, 0.22f));
                material.SetVector("_LiquidGlassOptics", new Vector4(0.2f, 0.24f, 0f, 0f));
                DrawAndReadCenter(material, target, resolved, readback, 1);
                var coordinateRamp = readback.GetPixels();
                for (var index = 0; index < pixels.Length; index++)
                {
                    pixels[index] = Color.black;
                }
                source.SetPixels(pixels);
                source.Apply(false, false);
                DrawAndReadCenter(material, target, resolved, readback, 1);
                var blackCoverage = readback.GetPixels();
                var previousCoordinate = 0f;
                var minimumIncrement = float.PositiveInfinity;
                var maximumIncrement = 0f;
                var inwardDisplacement = 0f;
                for (var x = 18; x <= 24; x++)
                {
                    var index = TargetSize / 2 * TargetSize + x;
                    var coverage = 1f - blackCoverage[index].r;
                    Assert.That(coverage, Is.GreaterThan(0.25f));
                    var coordinate = ((coordinateRamp[index].r - blackCoverage[index].r) / coverage - 0.1f) / 0.8f;
                    var outputCoordinate = (x + 0.5f) / TargetSize;
                    Assert.That(coordinate, Is.LessThanOrEqualTo(edge + 0.006f),
                        "A curved edge must sample inside its contour rather than pull the screen's exterior into the glass.");
                    Assert.That(coordinate, Is.EqualTo(outputCoordinate).Within(0.006f),
                        "The no-data path must not compress or offset the source, including its half-covered boundary.");
                    inwardDisplacement = Mathf.Max(inwardDisplacement, outputCoordinate - coordinate);
                    if (x > 18)
                    {
                        var increment = coordinate - previousCoordinate;
                        Assert.That(increment, Is.GreaterThanOrEqualTo(-0.006f),
                            "Recovered source UV must not fold back within the curved band or its coverage feather.");
                        minimumIncrement = Mathf.Min(minimumIncrement, increment);
                        maximumIncrement = Mathf.Max(maximumIncrement, increment);
                    }
                    previousCoordinate = coordinate;
                }
                Assert.That(inwardDisplacement, Is.LessThanOrEqualTo(0.006f), "Missing baked optics must retain the unshifted ramp within the existing coordinate-readback tolerance.");

                TestContext.WriteLine($"LENS_RAMP inward={inwardDisplacement:F6} minStep={minimumIncrement:F6} maxStep={maximumIncrement:F6}");

                // Maj's main screen is circular, with black outside it. A constant bright
                // interior makes any accidental sample beyond that boundary directly visible.
                var circleRadius = edge - 0.5f;
                var insideColor = new Color(0.75f, 0.85f, 0.95f, 1f);
                for (var y = 0; y < 128; y++)
                {
                    for (var x = 0; x < 128; x++)
                    {
                        var position = new Vector2((x + 0.5f) / 128f - 0.5f, (y + 0.5f) / 128f - 0.5f);
                        pixels[y * 128 + x] = position.magnitude <= circleRadius ? insideColor : Color.black;
                    }
                }
                source.SetPixels(pixels);
                source.Apply(false, false);
                material.SetVector("_LiquidGlassRect", new Vector4(0f, 0f, circleRadius, circleRadius));
                material.SetVector("_LiquidGlassShape", new Vector4(circleRadius, 1f, 0f, 0f));
                material.SetFloat("_GlassRefraction", 0f);
                DrawAndReadCenter(material, target, resolved, readback, 1);
                var circleUnwarped = readback.GetPixels();
                material.SetFloat("_GlassRefraction", 0.008f);
                DrawAndReadCenter(material, target, resolved, readback, 1);
                var checkedBoundaryPixels = 0;
                for (var y = 0; y < TargetSize; y++)
                {
                    for (var x = 0; x < TargetSize; x++)
                    {
                        var position = new Vector2((x + 0.5f) / TargetSize - 0.5f, (y + 0.5f) / TargetSize - 0.5f);
                        var insideDepth = circleRadius - position.magnitude;
                        if (insideDepth < 0.03f || insideDepth > 0.14f)
                        {
                            continue;
                        }
                        AssertColor(readback.GetPixel(x, y), circleUnwarped[y * TargetSize + x],
                            "Black beyond the circular screen must not leak into its refracting inner band");
                        checkedBoundaryPixels++;
                    }
                }
                Assert.That(checkedBoundaryPixels, Is.GreaterThan(20), "The circular fixture must exercise a substantial fully covered perimeter.");
                AssertColor(readback.GetPixel(TargetSize / 2, TargetSize / 2), insideColor,
                    "The circular glass center must keep the captured color unchanged");
                Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
            }
            finally
            {
                RenderTexture.active = savedTarget;
                Shader.SetGlobalTexture("_MajPvBlurTexture", savedBlur);
                Shader.SetGlobalInt("_UIVertexColorAlwaysGammaSpace", savedGamma);
                foreach (var pair in savedFloats)
                {
                    Shader.SetGlobalFloat(pair.Key, pair.Value);
                }
                foreach (var pair in savedVectors)
                {
                    Shader.SetGlobalVector(pair.Key, pair.Value);
                }
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(resolved);
                Object.DestroyImmediate(readback);
            }
        }

        /// <summary>Checks unbaked real buttons and clipped-circle cards preserve original colors, alpha and decorations without synthetic highlights.</summary>
        /// <param name="renderSize">The output size representing a small or medium peripheral button.</param>
        [TestCase(128)]
        [TestCase(256)]
        [Category("GPU")]
        public void LiquidWithoutBakedOptics_ActualAtlasButtonsKeepColorsAlphaAndDecorations(int renderSize)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("Atlas highlight visibility requires a real graphics device.");
            }
            var shader = Shader.Find("Maj/UI/PV Glass");
            Assert.That(shader, Is.Not.Null);
            var savedTarget = RenderTexture.active;
            var savedBlur = Shader.GetGlobalTexture("_MajPvBlurTexture");
            var savedButtonBlur = Shader.GetGlobalTexture("_MajUiButtonBlurTexture");
            var savedGamma = Shader.GetGlobalInt("_UIVertexColorAlwaysGammaSpace");
            var savedFloats = new System.Collections.Generic.Dictionary<string, float>();
            foreach (var name in new[] { "unity_GUIZTestMode", "_MajTextureMipBias", "_MajPvBlurReady", "_MajUiButtonBlurReady" })
            {
                savedFloats.Add(name, Shader.GetGlobalFloat(name));
            }
            var savedVectors = new System.Collections.Generic.Dictionary<string, Vector4>();
            foreach (var name in new[] { "_ScreenParams", "_TextureSampleAdd", "_MajPvBlurTexture_TexelSize", "_MajUiButtonBlurTexture_TexelSize" })
            {
                savedVectors.Add(name, Shader.GetGlobalVector(name));
            }
            var canvasObject = new GameObject("Actual atlas contour test", typeof(RectTransform), typeof(Canvas));
            var imageObject = new GameObject("Actual outlined button geometry", typeof(RectTransform), typeof(CanvasRenderer));
            imageObject.transform.SetParent(canvasObject.transform, false);
            var image = imageObject.AddComponent<QualityUiImage>();
            image.rectTransform.sizeDelta = new Vector2(232f, 232f);
            image.rectTransform.pivot = Vector2.one * 0.5f;
            image.preserveAspect = true;
            image.color = Color.white;
            var material = new Material(shader);
            // This fixture isolates transmission from the separately tested artwork share.
            material.SetFloat("_LiquidArtworkBlend", 0f);
            material.SetFloat("_LiquidTintStrength", 1f);
            var artworkMaterial = new Material(Shader.Find("Maj/UI/Atlas Alpha"));
            var blur = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            var mesh = new Mesh();
            var target = new RenderTexture(renderSize, renderSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(renderSize, renderSize, TextureFormat.RGBA32, false, true);
            try
            {
                Assert.That(target.Create(), Is.True);
                blur.SetPixel(0, 0, new Color(0.2f, 0.2f, 0.2f, 1f));
                blur.Apply(false, false);
                material.SetTextureScale("_MainTex", Vector2.one);
                material.SetTextureOffset("_MainTex", Vector2.zero);
                material.SetColor("_Color", Color.white);
                material.SetFloat("_FillAlpha", 1f);
                material.SetFloat("_ReferenceOpacity", 1f);
                material.SetFloat("_TintStrength", 1f);
                material.SetFloat("_FrostStrength", 0f);
                material.SetFloat("_ListDepthOffset", 0f);
                material.SetFloat("_GlassRefraction", 0f);
                material.SetFloat("_BackdropIsButton", 1f);
                material.SetFloat("_BackdropIsCategory", 0f);
                material.SetFloat("_BackdropIsRoundedRect", 0f);
                material.SetMatrix("_GlassWorldToLocal", Matrix4x4.identity);
                material.SetVector("_LiquidGlassRect", new Vector4(0f, 0f, 116f, 116f));
                material.SetVector("_LiquidGlassShape", new Vector4(116f, 1f, 0f, 0f));
                material.SetVector("_LiquidGlassCorners", Vector4.one * 116f);
                material.SetVector("_LiquidGlassOptics", new Vector4(9.6f, 9.6f, 3.2f, 2f));
                material.DisableKeyword("UNITY_UI_CLIP_RECT");
                material.DisableKeyword("UNITY_UI_ALPHACLIP");
                Shader.SetGlobalVector("_ScreenParams", new Vector4(renderSize, renderSize, 1f + 1f / renderSize, 1f + 1f / renderSize));
                Shader.SetGlobalFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
                Shader.SetGlobalFloat("_MajTextureMipBias", 0f);
                Shader.SetGlobalInt("_UIVertexColorAlwaysGammaSpace", 0);
                Shader.SetGlobalVector("_TextureSampleAdd", Vector4.zero);
                Shader.SetGlobalTexture("_MajPvBlurTexture", blur);
                Shader.SetGlobalTexture("_MajUiButtonBlurTexture", blur);
                Shader.SetGlobalVector("_MajUiButtonBlurTexture_TexelSize", Vector4.one);
                Shader.SetGlobalVector("_MajPvBlurTexture_TexelSize", Vector4.one);
                Shader.SetGlobalFloat("_MajPvBlurReady", 1f);
                Shader.SetGlobalFloat("_MajUiButtonBlurReady", 1f);
                s_liquidStyle = true;
                UiOutlineTheme.Configure(UiOutlineMode.All);
                var setting = MajdataPlay.IO.Storage.FileSystem.File.Open("Assets/Scenes/Setting.unity").ReadAllText();
                var optionBody = System.Text.RegularExpressions.Regex.Match(setting, @"^--- !u!114 &900000000000000016\r?\n(?<body>.*?)(?=^--- |\z)",
                    System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Singleline).Groups["body"].Value;
                Assert.That(optionBody, Does.Contain("_liquidClippedCircle: 1"), "The actual OptionBG must use its circular sides and flat horizontal crop.");
                var optionInsets = System.Text.RegularExpressions.Regex.Match(optionBody,
                    @"_liquidBodyInsets: \{x: (?<x>[^,]+), y: (?<y>[^,]+), z: (?<z>[^,]+), w: (?<w>[^}]+)\}");
                Assert.That(optionInsets.Success, Is.True, "Use the actual authored source-body alignment rather than an invented UI rounded rectangle.");
                var optionBodyInsets = new Vector4(
                    float.Parse(optionInsets.Groups["x"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(optionInsets.Groups["y"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(optionInsets.Groups["z"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(optionInsets.Groups["w"].Value, System.Globalization.CultureInfo.InvariantCulture));
                var optionCenter = System.Text.RegularExpressions.Regex.Match(optionBody,
                    @"_liquidCircleCenter: \{x: (?<x>[^,]+), y: (?<y>[^}]+)\}");
                var optionRadius = System.Text.RegularExpressions.Regex.Match(optionBody, @"_liquidCircleRadius: (?<r>[^\r\n]+)");
                Assert.That(optionCenter.Success && optionRadius.Success, Is.True, "The real circle offset and radius must be authored independently of the horizontal crop.");
                var optionCircleCenter = new Vector2(
                    float.Parse(optionCenter.Groups["x"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(optionCenter.Groups["y"].Value, System.Globalization.CultureInfo.InvariantCulture));
                var optionCircleRadius = float.Parse(optionRadius.Groups["r"].Value, System.Globalization.CultureInfo.InvariantCulture);
                var aspect = float.Parse(System.Text.RegularExpressions.Regex.Match(optionBody,
                    @"_liquidCircleAspect: (?<v>[^\r\n]+)").Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);
                var smoothness = float.Parse(System.Text.RegularExpressions.Regex.Match(optionBody,
                    @"_liquidClipSmoothness: (?<v>[^\r\n]+)").Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);
                for (var role = 0; role < 5; role++)
                {
                    var spriteName = role == 0 ? "newui@3x_20" : role == 1 ? "newui@3x_21" : role == 2 ? "newui@3x_22" : role == 3 ? "newui@3x_8" : "newui@3x_16";
                    Sprite? sprite = null;
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/Ribbon/newui@3x.png"))
                    {
                        if (asset is Sprite candidate && candidate.name == spriteName)
                        {
                            sprite = candidate;
                            break;
                        }
                    }
                    Assert.That(sprite, Is.Not.Null);
                    image.sprite = sprite;
                    image.preserveAspect = role < 2;
                    image.type = role == 3 ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
                    Assert.That(UiAtlasMeshGenerator.TryGetLegacy(sprite, out var legacy, out var originalUv), Is.True);
                    var halfHeight = role < 2 ? 116f : role == 2 || role == 4 ? 116f * legacy.rect.height / legacy.rect.width : 75f;
                    image.rectTransform.sizeDelta = new Vector2(232f, halfHeight * 2f);
                    Assert.That(UiOutlineTheme.TryGetOutlinedSprite(sprite, out var outlined), Is.True);
                    using (var vertices = new UnityEngine.UI.VertexHelper())
                    {
                        UiAtlasMeshGenerator.Populate(image, legacy, originalUv, true, vertices);
                        vertices.FillMesh(mesh);
                    }
                    material.SetTexture("_MainTex", outlined.texture);
                    material.SetTexture("_PanelBaseTex", sprite!.texture);
                    material.SetFloat("_FillAlpha", role == 2 ? 128f / 255f : 1f);
                    material.SetFloat("_FrostStrength", role == 3 ? 0.02f : 0f);
                    material.SetFloat("_BackdropIsButton", role < 2 ? 1f : 0f);
                    material.SetVector("_LiquidGlassRect", new Vector4(0f, 0f, 116f, halfHeight));
                    material.SetVector("_LiquidGlassShape", role < 2 ? new Vector4(116f, 1f, 0f, 0f)
                        : role == 2 ? new Vector4(0f, 0f, 1f, 0f) : role == 4 ? new Vector4(0f, 0f, 1f, 1f) : new Vector4(0f, 0f, 0f, 1f));
                    material.SetVector("_LiquidGlassCircle", Vector4.zero);
                    material.SetFloat("_LiquidGlassClipSmoothness", 0f);
                    if (role == 3 || role == 4)
                    {
                        // Exercise the actual non-affine nine-slice UV path, including authored
                        // transparent padding, rather than approximating it by the Image's outer rect.
                        var size = legacy.rect.size;
                        var padding = UnityEngine.Sprites.DataUtility.GetPadding(legacy);
                        var pixelUv = new Vector2((originalUv.z - originalUv.x) / (size.x - padding.x - padding.z),
                            (originalUv.w - originalUv.y) / (size.y - padding.y - padding.w));
                        var centerUv = new Vector2(originalUv.x - padding.x * pixelUv.x + size.x * pixelUv.x * 0.5f,
                            originalUv.y - padding.y * pixelUv.y + size.y * pixelUv.y * 0.5f);
                        material.SetVector("_LiquidGlassUvToLocal", new Vector4(1f / pixelUv.x, 1f / pixelUv.y,
                            -centerUv.x / pixelUv.x, -centerUv.y / pixelUv.y));
                        var insets = role == 4 ? optionBodyInsets : new Vector4(0.13793103f, 0.19047619f, 0.13793103f, 0.0952381f);
                        material.SetVector("_LiquidGlassSpriteRect", new Vector4(size.x * (insets.x - insets.z) * 0.5f,
                            size.y * (insets.y - insets.w) * 0.5f,
                            size.x * (1f - insets.x - insets.z) * 0.5f, size.y * (1f - insets.y - insets.w) * 0.5f));
                        material.SetVector("_LiquidGlassSpriteCorners", Vector4.one * Mathf.Min(size.x, size.y) * 0.24206349f);
                        if (role == 4)
                        {
                            var bodyRect = material.GetVector("_LiquidGlassSpriteRect");
                            material.SetVector("_LiquidGlassCircle", new Vector4(optionCircleCenter.x * size.x - bodyRect.x,
                                optionCircleCenter.y * size.y - bodyRect.y, optionCircleRadius * size.x, optionCircleRadius * size.x * aspect));
                            material.SetFloat("_LiquidGlassClipSmoothness", smoothness * size.x);
                        }
                    }
                    var uv = UnityEngine.Sprites.DataUtility.GetOuterUV(sprite);
                    material.SetVector("_FillSampleUV", new Vector4((uv.x + uv.z) * 0.5f, (uv.y + uv.w) * 0.5f, 0f, 0f));
                    material.SetFloat("_GlassHighlight", 0f);
                    var without = DrawActualButtonMesh(material, mesh, target, readback);
                    material.SetFloat("_GlassHighlight", 0.045f);
                    var withHighlight = DrawActualButtonMesh(material, mesh, target, readback);
                    var visiblePixels = 0;
                    for (var pixel = 0; pixel < without.Length; pixel++)
                    {
                        visiblePixels += without[pixel].a > 0.25f ? 1 : 0;
                        for (var channel = 0; channel < 4; channel++)
                        {
                            Assert.That(withHighlight[pixel][channel], Is.EqualTo(without[pixel][channel]).Within(1f / 255f),
                                "Unbaked actual atlas artwork must retain its colors, alpha and decorations when highlight strength changes.");
                        }
                    }
                    Assert.That(visiblePixels, Is.GreaterThan(100), "No optical data must not hide the actual atlas control.");
                    Assert.That(material.GetFloat("_LiquidRefractionReady"), Is.Zero);
                    Assert.That(material.GetFloat("_LiquidContourReady"), Is.Zero);
                    Assert.That(material.GetFloat("_LiquidProfileReady"), Is.Zero);
                    // Repeat over a bright capture so preserving colors cannot be a black-output false pass.
                    blur.SetPixel(0, 0, new Color(0.9f, 0.9f, 0.9f, 1f));
                    blur.Apply(false, false);
                    material.SetFloat("_GlassHighlight", 0f);
                    var brightWithout = DrawActualButtonMesh(material, mesh, target, readback);
                    material.SetFloat("_GlassHighlight", 0.045f);
                    var brightWith = DrawActualButtonMesh(material, mesh, target, readback);
                    for (var pixel = 0; pixel < brightWithout.Length; pixel++)
                    {
                        for (var channel = 0; channel < 4; channel++)
                        {
                            Assert.That(brightWith[pixel][channel], Is.EqualTo(brightWithout[pixel][channel]).Within(1f / 255f),
                                "No-data glass must preserve the same original image and alpha over bright captured backgrounds.");
                        }
                    }
                    TestContext.WriteLine($"NO_BAKED_ATLAS_OPTICS size={renderSize} sprite={spriteName} visiblePixels={visiblePixels}");
                    blur.SetPixel(0, 0, new Color(0.2f, 0.2f, 0.2f, 1f));
                    blur.Apply(false, false);
                }
                Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
            }
            finally
            {
                RenderTexture.active = savedTarget;
                Shader.SetGlobalTexture("_MajPvBlurTexture", savedBlur);
                Shader.SetGlobalTexture("_MajUiButtonBlurTexture", savedButtonBlur);
                Shader.SetGlobalInt("_UIVertexColorAlwaysGammaSpace", savedGamma);
                foreach (var pair in savedFloats)
                {
                    Shader.SetGlobalFloat(pair.Key, pair.Value);
                }
                foreach (var pair in savedVectors)
                {
                    Shader.SetGlobalVector(pair.Key, pair.Value);
                }
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(artworkMaterial);
                Object.DestroyImmediate(blur);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(canvasObject);
            }
        }

        /// <summary>Draws the actual compensated atlas mesh in logical UI units and reads its premultiplied output.</summary>
        /// <param name="material">The current production glass material.</param>
        /// <param name="mesh">The production-generated outlined button geometry.</param>
        /// <param name="target">The single-sample output at the tested button size.</param>
        /// <param name="readback">The reusable readback buffer.</param>
        /// <param name="scale">The fractional geometry scale whose inverse keeps the contour in authored UI units.</param>
        /// <returns>The complete resolved RGBA framebuffer.</returns>
        private static Color[] DrawActualButtonMesh(Material material, Mesh mesh, RenderTexture target, Texture2D readback, float scale = 1f)
        {
            RenderTexture.active = target;
            GL.Clear(true, true, Color.clear);
            GL.PushMatrix();
            try
            {
                GL.LoadPixelMatrix(-120f, 120f, -120f, 120f);
                var transform = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
                material.SetMatrix("_GlassWorldToLocal", transform.inverse);
                MatchExplicitGlassStyle(material);
                Assert.That(material.SetPass(0), Is.True);
                Graphics.DrawMeshNow(mesh, transform);
            }
            finally
            {
                GL.PopMatrix();
            }
            readback.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
            readback.Apply(false, false);
            return readback.GetPixels();
        }

        /// <summary>Resolves one production material draw and reads a fragment safely away from the quad's edges.</summary>
        /// <param name="material">The native shader material to draw.</param>
        /// <param name="target">The actual single- or multisample scene target.</param>
        /// <param name="resolved">The single-sample destination used for CPU readback.</param>
        /// <param name="readback">The reusable texture receiving resolved pixels.</param>
        /// <param name="samples">The verified sample count selecting explicit MSAA resolve.</param>
        /// <param name="background">The optional reference capture used behind the draw instead of the fixture's usual background.</param>
        /// <returns>The resolved center pixel including target alpha.</returns>
        private static Color DrawAndReadCenter(Material material, RenderTexture target, RenderTexture resolved,
            Texture2D readback, int samples, Color? background = null)
        {
            RenderTexture.active = target;
            GL.Clear(true, true, background ?? s_background);
            Draw(material);
            if (samples > 1)
            {
                target.ResolveAntiAliasedSurface(resolved);
            }
            else
            {
                Graphics.Blit(target, resolved);
            }
            RenderTexture.active = resolved;
            readback.ReadPixels(new Rect(0, 0, TargetSize, TargetSize), 0, 0);
            readback.Apply(false, false);
            return readback.GetPixel(TargetSize / 2, TargetSize / 2);
        }

        /// <summary>Draws a full-target quad while retaining the caller's GPU matrix state.</summary>
        /// <param name="material">The production-derived material whose fragment output is tested.</param>
        private static void Draw(Material material)
        {
            GL.PushMatrix();
            try
            {
                GL.LoadOrtho();
                MatchExplicitGlassStyle(material);
                Assert.That(material.SetPass(0), Is.True);
                GL.Begin(GL.QUADS);
                GL.Color(Color.white);
                GL.TexCoord2(0f, 0f);
                GL.Vertex3(0f, 0f, 0f);
                GL.TexCoord2(0f, 1f);
                GL.Vertex3(0f, 1f, 0f);
                GL.TexCoord2(1f, 1f);
                GL.Vertex3(1f, 1f, 0f);
                GL.TexCoord2(1f, 0f);
                GL.Vertex3(1f, 0f, 0f);
                GL.End();
            }
            finally
            {
                GL.PopMatrix();
            }
        }

        /// <summary>Sets the compiled glass style for manually created materials from the isolated fixture choice.</summary>
        /// <param name="material">The material about to draw, including shaders without a glass-style keyword.</param>
        private static void MatchExplicitGlassStyle(Material material)
        {
            var keyword = material.shader.keywordSpace.FindKeyword(UiBackdropPanel.LiquidStyleKeyword);
            if (keyword.isValid)
            {
                material.SetKeyword(keyword, s_liquidStyle);
            }
        }

        /// <summary>Checks every color channel including the blended target alpha.</summary>
        /// <param name="actual">The resolved GPU pixel.</param>
        /// <param name="expected">The continuous premultiplied-alpha result.</param>
        /// <param name="context">The graphics API, sample count, and input alpha.</param>
        private static void AssertColor(Color actual, Color expected, string context)
        {
            const float tolerance = 0.012f;
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(tolerance), context + " red");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(tolerance), context + " green");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(tolerance), context + " blue");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(tolerance), context + " alpha");
        }

        /// <summary>Resets the private runtime cache without adding a production test-only API.</summary>
        private static void ResetTheme()
        {
            typeof(UiOutlineTheme).GetMethod("Reset", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
        }
    }
}
