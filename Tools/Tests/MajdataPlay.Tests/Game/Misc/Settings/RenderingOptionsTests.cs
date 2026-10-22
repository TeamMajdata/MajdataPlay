using MajdataPlay.Settings;
using MajdataPlay.Utils;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Settings
{
    /// <summary>Covers presets, manual customization, deserialization repair and legacy migration of <see cref="RenderingOptions"/>.</summary>
    public class RenderingOptionsTests
    {
        /// <summary>Deserializes rendering options with the settings.json reader options.</summary>
        /// <param name="json">The JSON text.</param>
        /// <returns>The options.</returns>
        private static RenderingOptions Read(string json)
        {
            return Serializer.Json.Deserialize<RenderingOptions>(json, MajEnv.UserJsonReaderOption)!;
        }

        /// <summary>Checks the default rendering options.</summary>
        [Test]
        public void Defaults_UsePerformancePreset()
        {
            var options = new RenderingOptions();

            Assert.That(options.RenderQuality, Is.EqualTo(RenderingQualityOption.Performance));
            Assert.That(options.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.Disabled));
            Assert.That(options.TextureQuality, Is.EqualTo(RenderingTextureQualityOption.High));
            Assert.That(options.OutputMode, Is.EqualTo(RenderingOutputMode.Direct));
            Assert.That(options.RenderScale, Is.EqualTo(100));
            Assert.That(options.FPSLimit, Is.EqualTo(120));
            Assert.That(options.VSync, Is.True);
            Assert.That(options.CompatibilityMode, Is.False);
            Assert.That(options.SystemResolutionScaling, Is.EqualTo(100));
        }

        /// <summary>Checks the parameters applied by each preset.</summary>
        /// <param name="quality">The preset.</param>
        /// <param name="scale">The expected render scale.</param>
        /// <param name="antiAliasing">The expected anti-aliasing.</param>
        /// <param name="texture">The expected texture quality.</param>
        /// <param name="output">The expected output mode.</param>
        [TestCase(RenderingQualityOption.EnergySaving, 100, RenderingAntialiasingOption.Disabled, RenderingTextureQualityOption.Medium, RenderingOutputMode.Direct)]
        [TestCase(RenderingQualityOption.Performance, 100, RenderingAntialiasingOption.Disabled, RenderingTextureQualityOption.High, RenderingOutputMode.Direct)]
        [TestCase(RenderingQualityOption.Beautiful, 100, RenderingAntialiasingOption.MSAA4x, RenderingTextureQualityOption.High, RenderingOutputMode.Direct)]
        [TestCase(RenderingQualityOption.Refined, 150, RenderingAntialiasingOption.MSAA4x, RenderingTextureQualityOption.High, RenderingOutputMode.Intermediate)]
        public void RenderQuality_AppliesPreset(RenderingQualityOption quality, int scale, RenderingAntialiasingOption antiAliasing,
                                                RenderingTextureQualityOption texture, RenderingOutputMode output)
        {
            var options = new RenderingOptions();

            options.RenderQuality = quality;

            Assert.That(options.RenderQuality, Is.EqualTo(quality));
            Assert.That(options.RenderScale, Is.EqualTo(scale));
            Assert.That(options.AntiAliasing, Is.EqualTo(antiAliasing));
            Assert.That(options.TextureQuality, Is.EqualTo(texture));
            Assert.That(options.OutputMode, Is.EqualTo(output));
        }

        /// <summary>Checks that selecting Custom keeps the current detailed parameters.</summary>
        [Test]
        public void RenderQuality_Custom_KeepsParameters()
        {
            var options = new RenderingOptions { RenderQuality = RenderingQualityOption.Refined };

            options.RenderQuality = RenderingQualityOption.Custom;

            Assert.That(options.RenderScale, Is.EqualTo(150));
            Assert.That(options.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.MSAA4x));
        }

        /// <summary>Checks that changing a detailed parameter switches to Custom while repeating the value does not.</summary>
        [Test]
        public void DetailedParameters_SwitchToCustom()
        {
            var options = new RenderingOptions();

            options.AntiAliasing = RenderingAntialiasingOption.Disabled;
            options.TextureQuality = RenderingTextureQualityOption.High;
            options.OutputMode = RenderingOutputMode.Direct;
            Assert.That(options.RenderQuality, Is.EqualTo(RenderingQualityOption.Performance));

            options.AntiAliasing = RenderingAntialiasingOption.MSAA8x;
            Assert.That(options.RenderQuality, Is.EqualTo(RenderingQualityOption.Custom));

            options.RenderQuality = RenderingQualityOption.Performance;
            options.TextureQuality = RenderingTextureQualityOption.Low;
            Assert.That(options.RenderQuality, Is.EqualTo(RenderingQualityOption.Custom));

            options.RenderQuality = RenderingQualityOption.Performance;
            options.OutputMode = RenderingOutputMode.Intermediate;
            Assert.That(options.RenderQuality, Is.EqualTo(RenderingQualityOption.Custom));
        }

        /// <summary>Checks that the render scale is clamped and forces intermediate output away from 100%.</summary>
        [Test]
        public void RenderScale_ClampsAndForcesIntermediateOutput()
        {
            var options = new RenderingOptions();

            options.RenderScale = 5;
            Assert.That(options.RenderScale, Is.EqualTo(10));
            Assert.That(options.OutputMode, Is.EqualTo(RenderingOutputMode.Intermediate));
            Assert.That(options.RenderQuality, Is.EqualTo(RenderingQualityOption.Custom));

            options.RenderScale = 300;
            Assert.That(options.RenderScale, Is.EqualTo(200));

            options.OutputMode = RenderingOutputMode.Direct;
            Assert.That(options.OutputMode, Is.EqualTo(RenderingOutputMode.Intermediate));

            options.RenderScale = 100;
            options.OutputMode = RenderingOutputMode.Direct;
            Assert.That(options.OutputMode, Is.EqualTo(RenderingOutputMode.Direct));
        }

        /// <summary>Checks that the frame rate limit cannot go below -1 and that VSync can be changed.</summary>
        [Test]
        public void FramePacing_IsValidated()
        {
            var options = new RenderingOptions();

            options.FPSLimit = -20;
            options.VSync = false;

            Assert.That(options.FPSLimit, Is.EqualTo(-1));
            Assert.That(options.VSync, Is.False);
        }

        /// <summary>Checks that legacy frame pacing only fills values that were not set explicitly.</summary>
        [Test]
        public void MigrateFramePacing_DoesNotOverrideExplicitValues()
        {
            var fresh = new RenderingOptions();
            var explicitValues = new RenderingOptions { FPSLimit = 30, VSync = true };

            fresh.MigrateFramePacing(60, false);
            fresh.MigrateFramePacing(null, null);
            explicitValues.MigrateFramePacing(60, false);

            Assert.That(fresh.FPSLimit, Is.EqualTo(60));
            Assert.That(fresh.VSync, Is.False);
            Assert.That(explicitValues.FPSLimit, Is.EqualTo(30));
            Assert.That(explicitValues.VSync, Is.True);
        }

        /// <summary>Checks that entering custom mode only changes the mode once.</summary>
        [Test]
        public void EnterCustomMode_IsIdempotent()
        {
            var options = new RenderingOptions { RenderQuality = RenderingQualityOption.Beautiful };

            Assert.That(options.EnterCustomMode(), Is.True);
            Assert.That(options.EnterCustomMode(), Is.False);
            Assert.That(options.RenderQuality, Is.EqualTo(RenderingQualityOption.Custom));
            Assert.That(options.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.MSAA4x));
        }

        /// <summary>Checks the effective parameters, including the temporary compatibility override.</summary>
        [Test]
        public void GetEffectiveParameters_ReflectsModeAndCompatibility()
        {
            var options = new RenderingOptions { RenderQuality = RenderingQualityOption.EnergySaving };

            var normal = options.GetEffectiveParameters();
            options.RenderScale = 120;
            var scaled = options.GetEffectiveParameters();
            options.CompatibilityMode = true;
            var compatible = options.GetEffectiveParameters();

            Assert.That(normal.RenderScale, Is.EqualTo(100));
            Assert.That(normal.MSAASamples, Is.EqualTo(1));
            Assert.That(normal.MipmapBias, Is.EqualTo(1));
            Assert.That(normal.ResolveToScreen, Is.True);
            Assert.That(scaled.RenderScale, Is.EqualTo(120));
            Assert.That(scaled.ResolveToScreen, Is.False);
            Assert.That(compatible.RenderScale, Is.EqualTo(50));
            Assert.That(compatible.MSAASamples, Is.EqualTo(1));
            Assert.That(compatible.MipmapBias, Is.EqualTo(2));
            Assert.That(compatible.ResolveToScreen, Is.False);
            Assert.That(options.RenderScale, Is.EqualTo(120));
        }

        /// <summary>Checks that a stored preset wins over stored detailed values regardless of JSON property order.</summary>
        [Test]
        public void Deserialize_PresetWinsOverStoredDetails()
        {
            var options = Read("{\"RenderScale\":100,\"AntiAliasing\":\"Disabled\",\"RenderQuality\":\"Refined\"}");

            Assert.That(options.RenderQuality, Is.EqualTo(RenderingQualityOption.Refined));
            Assert.That(options.RenderScale, Is.EqualTo(150));
            Assert.That(options.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.MSAA4x));
            Assert.That(options.OutputMode, Is.EqualTo(RenderingOutputMode.Intermediate));
        }

        /// <summary>Checks that a custom configuration keeps its details and repairs inconsistent output.</summary>
        [Test]
        public void Deserialize_CustomKeepsDetails()
        {
            var options = Read("{\"RenderQuality\":\"Custom\",\"RenderScale\":50,\"OutputMode\":\"Direct\",\"AntiAliasing\":\"MSAA2x\",\"TextureQuality\":\"Low\"}");

            Assert.That(options.RenderQuality, Is.EqualTo(RenderingQualityOption.Custom));
            Assert.That(options.RenderScale, Is.EqualTo(50));
            Assert.That(options.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.MSAA2x));
            Assert.That(options.TextureQuality, Is.EqualTo(RenderingTextureQualityOption.Low));
            Assert.That(options.OutputMode, Is.EqualTo(RenderingOutputMode.Intermediate));
        }

        /// <summary>Checks that out-of-range numeric values are repaired after deserialization.</summary>
        [Test]
        public void Deserialize_RepairsInvalidValues()
        {
            var options = Read("{\"RenderQuality\":99,\"AntiAliasing\":3,\"TextureQuality\":9,\"OutputMode\":7,\"RenderScale\":900,\"SystemResolutionScaling\":500}");
            var lowScaling = Read("{\"RenderQuality\":\"Custom\",\"SystemResolutionScaling\":1,\"RenderScale\":1,\"AntiAliasing\":3}");

            Assert.That(options.RenderQuality, Is.EqualTo(RenderingQualityOption.Performance));
            Assert.That(options.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.Disabled));
            Assert.That(options.TextureQuality, Is.EqualTo(RenderingTextureQualityOption.High));
            Assert.That(options.RenderScale, Is.EqualTo(100));
            Assert.That(options.OutputMode, Is.EqualTo(RenderingOutputMode.Direct));
            Assert.That(options.SystemResolutionScaling, Is.EqualTo(100));
            Assert.That(lowScaling.SystemResolutionScaling, Is.EqualTo(10));
            Assert.That(lowScaling.RenderScale, Is.EqualTo(10));
            Assert.That(lowScaling.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.Disabled));
            Assert.That(lowScaling.OutputMode, Is.EqualTo(RenderingOutputMode.Intermediate));
        }

        /// <summary>Checks the mapping of the six legacy quality levels and render scales.</summary>
        /// <param name="quality">The legacy quality, or -1 for none.</param>
        /// <param name="scale">The legacy render scale, or -1 for none.</param>
        /// <param name="expectedQuality">The expected preset.</param>
        /// <param name="expectedAntiAliasing">The expected anti-aliasing.</param>
        /// <param name="expectedTexture">The expected texture quality.</param>
        /// <param name="expectedScale">The expected render scale.</param>
        /// <param name="expectedOutput">The expected output mode.</param>
        [TestCase(-1, -1, RenderingQualityOption.Performance, RenderingAntialiasingOption.Disabled, RenderingTextureQualityOption.High, 100, RenderingOutputMode.Direct)]
        [TestCase(0, 100, RenderingQualityOption.EnergySaving, RenderingAntialiasingOption.Disabled, RenderingTextureQualityOption.Medium, 100, RenderingOutputMode.Direct)]
        [TestCase(1, 100, RenderingQualityOption.Performance, RenderingAntialiasingOption.Disabled, RenderingTextureQualityOption.High, 100, RenderingOutputMode.Direct)]
        [TestCase(2, -1, RenderingQualityOption.Custom, RenderingAntialiasingOption.MSAA2x, RenderingTextureQualityOption.High, 100, RenderingOutputMode.Direct)]
        [TestCase(3, 100, RenderingQualityOption.Beautiful, RenderingAntialiasingOption.MSAA4x, RenderingTextureQualityOption.High, 100, RenderingOutputMode.Direct)]
        [TestCase(4, 100, RenderingQualityOption.Custom, RenderingAntialiasingOption.MSAA8x, RenderingTextureQualityOption.High, 100, RenderingOutputMode.Direct)]
        [TestCase(5, 150, RenderingQualityOption.Custom, RenderingAntialiasingOption.MSAA8x, RenderingTextureQualityOption.High, 150, RenderingOutputMode.Intermediate)]
        [TestCase(-1, 500, RenderingQualityOption.Custom, RenderingAntialiasingOption.MSAA2x, RenderingTextureQualityOption.High, 200, RenderingOutputMode.Intermediate)]
        [TestCase(3, 50, RenderingQualityOption.Custom, RenderingAntialiasingOption.MSAA4x, RenderingTextureQualityOption.High, 50, RenderingOutputMode.Intermediate)]
        public void FromLegacy_MapsQualityLevels(int quality, int scale, RenderingQualityOption expectedQuality, RenderingAntialiasingOption expectedAntiAliasing,
                                                 RenderingTextureQualityOption expectedTexture, int expectedScale, RenderingOutputMode expectedOutput)
        {
            RenderQualityOption? legacyQuality = quality < 0 ? null : (RenderQualityOption)quality;
            int? legacyScale = scale < 0 ? null : scale;

            var options = RenderingOptions.FromLegacy(legacyQuality, legacyScale);

            Assert.That(options.RenderQuality, Is.EqualTo(expectedQuality));
            Assert.That(options.AntiAliasing, Is.EqualTo(expectedAntiAliasing));
            Assert.That(options.TextureQuality, Is.EqualTo(expectedTexture));
            Assert.That(options.RenderScale, Is.EqualTo(expectedScale));
            Assert.That(options.OutputMode, Is.EqualTo(expectedOutput));
        }

        /// <summary>Checks that the rendering parameter value keeps its fields.</summary>
        [Test]
        public void RenderingParameters_StoresValues()
        {
            var parameters = new RenderingParameters(80, 4, 1, true);

            Assert.That(parameters.RenderScale, Is.EqualTo(80));
            Assert.That(parameters.MSAASamples, Is.EqualTo(4));
            Assert.That(parameters.MipmapBias, Is.EqualTo(1));
            Assert.That(parameters.ResolveToScreen, Is.True);
        }
    }
}
