using System;
using System.Collections.Generic;
using MajdataPlay.Rendering;
using MajdataPlay.Settings;
using MajdataPlay.Utils;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Settings
{
    /// <summary>Covers presets, manual customization, deserialization repair and setting UI visibility of <see cref="RenderingOptions"/>.</summary>
    public class RenderingOptionsTests
    {
        /// <summary>Deserializes rendering options with the settings.json reader options.</summary>
        /// <param name="json">The JSON text.</param>
        /// <returns>The options.</returns>
        private static RenderingOptions Read(string json)
        {
            return Serializer.Json.Deserialize<RenderingOptions>(json, MajEnv.UserJsonReaderOption)!;
        }

        /// <summary>Gets the names of the rendering options the setting UI lists, in declaration order.</summary>
        /// <returns>The option names.</returns>
        private static List<string> UiOptionNames()
        {
            var names = new List<string>();
            foreach (var property in SettingReflectionCache.GetVisibleProperties(typeof(RenderingOptions)))
            {
                names.Add(property.Name);
            }
            return names;
        }

        /// <summary>Gets the names of the options a preset shows outside compatibility mode, in declaration order.</summary>
        /// <returns>The option names.</returns>
        private static List<string> PresetOptionNames()
        {
            var common = new[]
            {
                nameof(RenderingOptions.RenderQuality),
                nameof(RenderingOptions.FPSLimit),
                nameof(RenderingOptions.VSync),
                nameof(RenderingOptions.CompatibilityMode)
            };
            var names = UiOptionNames().FindAll(name => Array.IndexOf(common, name) >= 0);
            names.AddRange(s_screenOptionNames);
            return names;
        }

        /// <summary>The runtime statistics and screen layout options listed after the rendering parameters, which are always shown.</summary>
        private static readonly string[] s_screenOptionNames =
        {
            nameof(RenderingOptions.DisplayRuntimeInfo),
            nameof(RenderingOptions.MainScreenTransform),
            nameof(RenderingOptions.MainScreenScale),
            nameof(RenderingOptions.MainScreenOffset),
            nameof(RenderingOptions.SubDisplayOffset),
            nameof(RenderingOptions.SubDisplayScale),
            nameof(RenderingOptions.GameplayScreenRotationAngle)
        };

        /// <summary>
        /// Checks that the rendering preset stays the first row and the screen layout options moved from the Display menu
        /// follow the rendering parameters, while the window options and the cached layout value stay hidden.
        /// </summary>
        [Test]
        public void UiOptions_ListScreenLayoutAfterRenderingParameters()
        {
            var names = UiOptionNames();

            Assert.That(names[0], Is.EqualTo(nameof(RenderingOptions.RenderQuality)));
            Assert.That(names.GetRange(names.Count - s_screenOptionNames.Length, s_screenOptionNames.Length), Is.EqualTo(s_screenOptionNames));
            Assert.That(names, Has.No.Member(nameof(RenderingOptions.MainScreenCachedScreenCenterY)));
            Assert.That(names, Has.Member(nameof(RenderingOptions.UIOutlineMode)));
            Assert.That(names, Has.Member(nameof(RenderingOptions.UIBackdropStyle)));
            Assert.That(names, Has.No.Member("UIBackdropBlur"));
#if UNITY_STANDALONE
            Assert.That(names, Has.No.Member(nameof(RenderingOptions.Resolution)));
            Assert.That(names, Has.No.Member(nameof(RenderingOptions.FullScreen)));
            Assert.That(names, Has.No.Member(nameof(RenderingOptions.Topmost)));
#endif
        }

        /// <summary>Gets the names of the listed options whose <see cref="ShowInSettingUIWhenAttribute"/> conditions hold.</summary>
        /// <param name="options">The options the conditions are evaluated against.</param>
        /// <returns>The option names in declaration order.</returns>
        private static List<string> VisibleOptionNames(RenderingOptions options)
        {
            var names = new List<string>();
            foreach (var property in SettingReflectionCache.GetVisibleProperties(typeof(RenderingOptions)))
            {
                if (SettingReflectionCache.GetVisibility(property).IsVisible(options))
                {
                    names.Add(property.Name);
                }
            }
            return names;
        }

        /// <summary>Checks that new and empty saved settings use the complete Beautiful preset.</summary>
        /// <param name="deserializeEmpty">Whether to read an empty document instead of constructing new options.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void Defaults_UseBeautifulPreset(bool deserializeEmpty)
        {
            var options = deserializeEmpty ? Read("{}") : new RenderingOptions();

            Assert.That(options.RenderQuality, Is.EqualTo(RenderingQualityOption.Beautiful));
            Assert.That(options.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.Disabled));
            Assert.That(options.TextureQuality, Is.EqualTo(RenderingTextureQualityOption.High));
            Assert.That(options.OutputMode, Is.EqualTo(RenderingOutputMode.Direct));
            Assert.That(options.RenderScale, Is.EqualTo(100));
            Assert.That(options.FPSLimit, Is.EqualTo(120));
            Assert.That(options.VSync, Is.True);
            Assert.That(options.CompatibilityMode, Is.False);
            Assert.That(options.UIOutlineMode, Is.EqualTo(UiOutlineMode.All));
            Assert.That(options.GetEffectiveUIOutlineMode(), Is.EqualTo(UiOutlineMode.All));
            Assert.That(options.UIBackdropStyle, Is.EqualTo(UiBackdropStyle.GaussianBlur));


            Assert.That(options.SystemResolutionScaling, Is.EqualTo(100));
        }

        /// <summary>Checks that changing the default does not upgrade a saved Performance selection.</summary>
        [Test]
        public void Deserialize_SavedPerformancePreservesItsRenderingParameters()
        {
            var options = Read("{\"RenderQuality\":\"Performance\"}");

            Assert.That(options.RenderQuality, Is.EqualTo(RenderingQualityOption.Performance));
            Assert.That(options.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.Disabled));
            Assert.That(options.UIOutlineMode, Is.EqualTo(UiOutlineMode.Performance));

            Assert.That(options.RenderScale, Is.EqualTo(100));
            Assert.That(options.OutputMode, Is.EqualTo(RenderingOutputMode.Direct));
        }

        /// <summary>Checks the parameters applied by each preset.</summary>
        /// <param name="quality">The preset.</param>
        /// <param name="scale">The expected render scale.</param>
        /// <param name="antiAliasing">The expected anti-aliasing.</param>
        /// <param name="texture">The expected texture quality.</param>
        /// <param name="output">The expected output mode.</param>
        [TestCase(RenderingQualityOption.EnergySaving, 100, RenderingAntialiasingOption.Disabled, RenderingTextureQualityOption.Medium, RenderingOutputMode.Direct)]
        [TestCase(RenderingQualityOption.Performance, 100, RenderingAntialiasingOption.Disabled, RenderingTextureQualityOption.High, RenderingOutputMode.Direct)]
        [TestCase(RenderingQualityOption.Beautiful, 100, RenderingAntialiasingOption.Disabled, RenderingTextureQualityOption.High, RenderingOutputMode.Direct)]
        [TestCase(RenderingQualityOption.Refined, 100, RenderingAntialiasingOption.Disabled, RenderingTextureQualityOption.High, RenderingOutputMode.Direct)]
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
            Assert.That(options.UIOutlineMode, Is.EqualTo(quality == RenderingQualityOption.EnergySaving ? UiOutlineMode.None
                : quality == RenderingQualityOption.Performance ? UiOutlineMode.Performance
                : quality == RenderingQualityOption.Refined ? UiOutlineMode.GaussianBlur : UiOutlineMode.All));
        }

        /// <summary>Checks named presets replace their historical MSAA value in either JSON property order.</summary>
        /// <param name="quality">The saved named preset whose current definition disables MSAA.</param>
        [TestCase(RenderingQualityOption.Beautiful)]
        [TestCase(RenderingQualityOption.Refined)]
        public void Deserialize_NamedPresetDisablesHistoricalMsaa(RenderingQualityOption quality)
        {
            foreach (var json in new[]
            {
                "{\"RenderQuality\":\"" + quality + "\",\"AntiAliasing\":\"MSAA4x\"}",
                "{\"AntiAliasing\":\"MSAA8x\",\"RenderQuality\":\"" + quality + "\"}"
            })
            {
                var options = Read(json);
                Assert.That(options.RenderQuality, Is.EqualTo(quality));
                Assert.That(options.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.Disabled));
                Assert.That(options.GetEffectiveParameters().MSAASamples, Is.EqualTo(1));
            }
        }

        /// <summary>Checks an explicit manual sample count and saved Custom configuration survive the new preset defaults.</summary>
        /// <param name="antiAliasing">The explicitly selected sample count.</param>
        [TestCase(RenderingAntialiasingOption.MSAA2x)]
        [TestCase(RenderingAntialiasingOption.MSAA4x)]
        [TestCase(RenderingAntialiasingOption.MSAA8x)]
        public void Custom_ExplicitMsaaSurvivesDefaultsAndRoundTrip(RenderingAntialiasingOption antiAliasing)
        {
            var manual = new RenderingOptions();
            manual.AntiAliasing = antiAliasing;
            Assert.That(manual.RenderQuality, Is.EqualTo(RenderingQualityOption.Custom));
            Assert.That(manual.GetEffectiveParameters().MSAASamples, Is.EqualTo((int)antiAliasing));
            foreach (var json in new[]
            {
                "{\"RenderQuality\":\"Custom\",\"AntiAliasing\":\"" + antiAliasing + "\"}",
                "{\"AntiAliasing\":\"" + antiAliasing + "\",\"RenderQuality\":\"Custom\"}"
            })
            {
                var restored = Read(Serializer.Json.Serialize(Read(json), MajEnv.UserJsonReaderOption));
                Assert.That(restored.RenderQuality, Is.EqualTo(RenderingQualityOption.Custom));
                Assert.That(restored.AntiAliasing, Is.EqualTo(antiAliasing));
                Assert.That(restored.GetEffectiveParameters().MSAASamples, Is.EqualTo((int)antiAliasing));
            }
        }

        /// <summary>Checks Custom inherits the unified UI level independently of MSAA and saves it explicitly.</summary>
        /// <param name="quality">The preset selected before entering Custom.</param>
        /// <param name="mode">The inherited UI level.</param>
        [TestCase(RenderingQualityOption.EnergySaving, UiOutlineMode.None)]
        [TestCase(RenderingQualityOption.Performance, UiOutlineMode.Performance)]
        [TestCase(RenderingQualityOption.Beautiful, UiOutlineMode.All)]
        [TestCase(RenderingQualityOption.Refined, UiOutlineMode.GaussianBlur)]
        public void UIOutlineMode_CustomInheritsPresetAndRoundTrips(RenderingQualityOption quality, UiOutlineMode mode)
        {
            var options = new RenderingOptions { RenderQuality = quality };

            Assert.That(options.EnterCustomMode(), Is.True);
            options.AntiAliasing = RenderingAntialiasingOption.Disabled;
            var restored = Read(Serializer.Json.Serialize(options, MajEnv.UserJsonReaderOption));

            Assert.That(options.UIOutlineMode, Is.EqualTo(mode));
            Assert.That(restored.RenderQuality, Is.EqualTo(RenderingQualityOption.Custom));
            Assert.That(restored.UIOutlineMode, Is.EqualTo(mode));
            Assert.That(restored.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.Disabled));
        }

        /// <summary>Checks an actual UI-level change enters Custom while the same value preserves its preset.</summary>
        [Test]
        public void UIOutlineMode_ChangesEnterCustomOnlyWhenValueChanges()
        {
            var options = new RenderingOptions();

            options.UIOutlineMode = UiOutlineMode.All;
            Assert.That(options.RenderQuality, Is.EqualTo(RenderingQualityOption.Beautiful));
            options.UIOutlineMode = UiOutlineMode.Performance;
            Assert.That(options.RenderQuality, Is.EqualTo(RenderingQualityOption.Custom));
            Assert.That(options.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.Disabled));
            options.UIOutlineMode = UiOutlineMode.None;
            Assert.That(options.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.Disabled));
        }

        /// <summary>Checks current and legacy UI levels are resolved once, independently of JSON key order.</summary>
        /// <param name="json">The saved rendering configuration.</param>
        /// <param name="mode">The expected effective UI level.</param>
        [TestCase("{\"RenderQuality\":\"Custom\"}", UiOutlineMode.None)]
        [TestCase("{\"RenderQuality\":\"Custom\",\"UIOutline\":true}", UiOutlineMode.All)]
        [TestCase("{\"UIOutline\":false,\"UIBackgroundMode\":\"OutlinedAlpha\",\"RenderQuality\":\"Custom\"}", UiOutlineMode.Performance)]
        [TestCase("{\"RenderQuality\":\"Custom\",\"UIBackgroundMode\":1,\"UIOutline\":false}", UiOutlineMode.Performance)]
        [TestCase("{\"RenderQuality\":\"Custom\",\"UIOutline\":false,\"UIBackgroundMode\":\"Cutoff\"}", UiOutlineMode.None)]
        [TestCase("{\"RenderQuality\":\"Custom\",\"UIOutline\":false}", UiOutlineMode.None)]
        [TestCase("{\"RenderQuality\":\"Custom\",\"UIOutline\":true,\"UIBackgroundMode\":\"Cutoff\"}", UiOutlineMode.All)]
        [TestCase("{\"UIOutlineMode\":\"None\",\"UIOutline\":true,\"UIBackgroundMode\":\"OutlinedAlpha\",\"RenderQuality\":\"Custom\"}", UiOutlineMode.None)]
        [TestCase("{\"RenderQuality\":\"Custom\",\"UIOutline\":true,\"UIBackgroundMode\":\"Cutoff\",\"UIOutlineMode\":\"Performance\"}", UiOutlineMode.Performance)]
        [TestCase("{\"UIOutlineMode\":2,\"UIOutline\":false,\"RenderQuality\":\"Custom\"}", UiOutlineMode.All)]
        [TestCase("{\"RenderQuality\":\"Custom\",\"UIOutlineMode\":999,\"UIOutline\":true}", UiOutlineMode.None)]
        [TestCase("{\"RenderQuality\":\"EnergySaving\",\"UIOutlineMode\":\"All\"}", UiOutlineMode.None)]
        [TestCase("{\"UIOutline\":true,\"UIOutlineMode\":\"All\",\"RenderQuality\":\"Performance\"}", UiOutlineMode.Performance)]
        [TestCase("{\"RenderQuality\":\"Beautiful\",\"UIOutlineMode\":\"None\"}", UiOutlineMode.All)]
        [TestCase("{\"UIOutlineMode\":999,\"RenderQuality\":\"Refined\"}", UiOutlineMode.GaussianBlur)]
        [TestCase("{\"RenderQuality\":\"Custom\",\"UIOutlineMode\":3}", UiOutlineMode.GaussianBlur)]
        [TestCase("{\"RenderQuality\":\"Custom\",\"UIOutlineMode\":\"LiquidGlass\"}", UiOutlineMode.LiquidGlass)]
        [TestCase("{\"RenderQuality\":\"Custom\",\"UIOutline\":true,\"UIBackdropBlur\":true}", UiOutlineMode.GaussianBlur)]
        [TestCase("{\"UIBackdropBlur\":true,\"UIBackdropStyle\":\"LiquidGlass\",\"UIOutline\":true,\"RenderQuality\":\"Custom\"}", UiOutlineMode.LiquidGlass)]
        public void Deserialize_UIOutlineModeMigratesLegacyAndPreservesCanonicalAuthority(string json, UiOutlineMode mode)
        {
            var options = Read(json);
            var written = Serializer.Json.Serialize(options, MajEnv.UserJsonReaderOption);
            var parsed = JObject.Parse(written);
            var restored = Read(written);

            Assert.That(options.UIOutlineMode, Is.EqualTo(mode));
            Assert.That(options.GetEffectiveUIOutlineMode(), Is.EqualTo(mode == UiOutlineMode.GaussianBlur || mode == UiOutlineMode.LiquidGlass
                ? UiOutlineMode.All : mode));
            Assert.That(parsed.ContainsKey(nameof(RenderingOptions.UIOutlineMode)), Is.True);
            Assert.That(parsed.ContainsKey("UIOutline"), Is.False);
            Assert.That(parsed.ContainsKey("UIBackgroundMode"), Is.False);
            Assert.That(parsed.ContainsKey("UIBackdropBlur"), Is.False);
            Assert.That(restored.UIOutlineMode, Is.EqualTo(mode));
            Assert.That(restored.RenderQuality, Is.EqualTo(options.RenderQuality));
            Assert.That(restored.AntiAliasing, Is.EqualTo(options.AntiAliasing));
        }

        /// <summary>Checks compatibility changes only the effective UI level and restores the saved Custom choice.</summary>
        /// <param name="mode">The saved Custom UI level.</param>
        [TestCase(UiOutlineMode.None)]
        [TestCase(UiOutlineMode.Performance)]
        [TestCase(UiOutlineMode.All)]
        [TestCase(UiOutlineMode.GaussianBlur)]
        [TestCase(UiOutlineMode.LiquidGlass)]
        public void UIOutlineMode_CompatibilityOverridePreservesSavedSelection(UiOutlineMode mode)
        {
            var options = new RenderingOptions { RenderQuality = RenderingQualityOption.Custom, UIOutlineMode = mode };

            options.CompatibilityMode = true;
            Assert.That(options.UIOutlineMode, Is.EqualTo(mode));
            Assert.That(options.GetEffectiveUIOutlineMode(), Is.EqualTo(UiOutlineMode.None));
            var restored = Read(Serializer.Json.Serialize(options, MajEnv.UserJsonReaderOption));
            Assert.That(restored.UIOutlineMode, Is.EqualTo(mode));
            restored.CompatibilityMode = false;
            Assert.That(restored.GetEffectiveUIOutlineMode(), Is.EqualTo(mode == UiOutlineMode.GaussianBlur || mode == UiOutlineMode.LiquidGlass
                ? UiOutlineMode.All : mode));
        }

        /// <summary>Checks historical missing Custom details without changing explicitly stored MSAA.</summary>
        /// <param name="json">The saved rendering configuration.</param>
        /// <param name="antiAliasing">The expected saved or historical anti-aliasing mode.</param>
        /// <param name="mode">The expected migrated UI level.</param>
        [TestCase("{\"RenderQuality\":\"Custom\"}", RenderingAntialiasingOption.Disabled, UiOutlineMode.None)]
        [TestCase("{\"RenderQuality\":\"Custom\",\"UIOutline\":true}", RenderingAntialiasingOption.Disabled, UiOutlineMode.All)]
        [TestCase("{\"RenderQuality\":\"Custom\",\"AntiAliasing\":\"MSAA8x\"}", RenderingAntialiasingOption.MSAA8x, UiOutlineMode.None)]
        [TestCase("{\"AntiAliasing\":\"MSAA2x\",\"UIOutline\":true,\"RenderQuality\":\"Custom\"}", RenderingAntialiasingOption.MSAA2x, UiOutlineMode.All)]
        public void Deserialize_CustomMissingDetailsRetainHistoricalDefaults(string json, RenderingAntialiasingOption antiAliasing, UiOutlineMode mode)
        {
            var options = Read(json);

            Assert.That(options.RenderQuality, Is.EqualTo(RenderingQualityOption.Custom));
            Assert.That(options.AntiAliasing, Is.EqualTo(antiAliasing));
            Assert.That(options.UIOutlineMode, Is.EqualTo(mode));
            Assert.That(options.RenderScale, Is.EqualTo(100));
            Assert.That(options.OutputMode, Is.EqualTo(RenderingOutputMode.Direct));
        }

        /// <summary>Checks the unified UI selector is exposed only in non-compatible Custom mode.</summary>
        [Test]
        public void UIOutlineMode_VisibilityIsCustomOnly()
        {
            var options = new RenderingOptions();
            var visibility = SettingReflectionCache.GetVisibility(typeof(RenderingOptions).GetProperty(nameof(RenderingOptions.UIOutlineMode))!);

            Assert.That(visibility.IsVisible(options), Is.False);
            options.EnterCustomMode();
            Assert.That(visibility.IsVisible(options), Is.True);
            options.CompatibilityMode = true;
            Assert.That(visibility.IsVisible(options), Is.False);
            options.CompatibilityMode = false;
            options.RenderQuality = RenderingQualityOption.Beautiful;
            Assert.That(visibility.IsVisible(options), Is.False);
        }

        /// <summary>Checks that changing a detailed parameter switches to Custom while repeating the value does not.</summary>
        [Test]
        public void DetailedParameters_SwitchToCustom()
        {
            var options = new RenderingOptions { RenderQuality = RenderingQualityOption.Performance };

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

        /// <summary>Checks that entering custom mode only changes the mode once.</summary>
        [Test]
        public void EnterCustomMode_IsIdempotent()
        {
            var options = new RenderingOptions { RenderQuality = RenderingQualityOption.Beautiful };

            Assert.That(options.EnterCustomMode(), Is.True);
            Assert.That(options.EnterCustomMode(), Is.False);
            Assert.That(options.RenderQuality, Is.EqualTo(RenderingQualityOption.Custom));
            Assert.That(options.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.Disabled));
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

        /// <summary>Checks Refined reapplies its current full-resolution preset over old supersampling details.</summary>
        /// <param name="json">A current or historical Refined configuration in either property order.</param>
        [TestCase("{\"RenderScale\":100,\"AntiAliasing\":\"Disabled\",\"RenderQuality\":\"Refined\"}")]
        [TestCase("{\"RenderQuality\":\"Refined\",\"RenderScale\":150,\"OutputMode\":\"Intermediate\"}")]
        [TestCase("{\"OutputMode\":\"Intermediate\",\"RenderScale\":150,\"RenderQuality\":\"Refined\"}")]
        public void Deserialize_PresetWinsOverStoredDetails(string json)
        {
            var options = Read(json);

            Assert.That(options.RenderQuality, Is.EqualTo(RenderingQualityOption.Refined));
            Assert.That(options.RenderScale, Is.EqualTo(100));
            Assert.That(options.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.Disabled));
            Assert.That(options.TextureQuality, Is.EqualTo(RenderingTextureQualityOption.High));
            Assert.That(options.UIOutlineMode, Is.EqualTo(UiOutlineMode.GaussianBlur));
            Assert.That(options.OutputMode, Is.EqualTo(RenderingOutputMode.Direct));
            Assert.That(options.GetEffectiveParameters().ResolveToScreen, Is.True);
        }

        /// <summary>Checks the Refined preset change does not replace explicit Custom supersampling or output preferences.</summary>
        /// <param name="scale">The saved Custom resolution scale.</param>
        [TestCase(100)]
        [TestCase(150)]
        public void Deserialize_CustomRetainsExplicitScaleAndIntermediateOutput(int scale)
        {
            var options = Read("{\"RenderQuality\":\"Custom\",\"RenderScale\":" + scale + ",\"OutputMode\":\"Intermediate\",\"AntiAliasing\":\"MSAA4x\",\"UIOutlineMode\":\"All\"}");
            var restored = Read(Serializer.Json.Serialize(options, MajEnv.UserJsonReaderOption));

            Assert.That(restored.RenderQuality, Is.EqualTo(RenderingQualityOption.Custom));
            Assert.That(restored.RenderScale, Is.EqualTo(scale));
            Assert.That(restored.OutputMode, Is.EqualTo(RenderingOutputMode.Intermediate));
            Assert.That(restored.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.MSAA4x));
            Assert.That(restored.UIOutlineMode, Is.EqualTo(UiOutlineMode.All));
            Assert.That(restored.GetEffectiveParameters().ResolveToScreen, Is.False);
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

            Assert.That(options.RenderQuality, Is.EqualTo(RenderingQualityOption.Beautiful));
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

        /// <summary>Checks that a preset shows only the preset selector, the frame pacing options, the compatibility switch and the screen options.</summary>
        [Test]
        public void Visibility_PresetShowsCommonOptions()
        {
            var options = new RenderingOptions { RenderQuality = RenderingQualityOption.Refined };
            var expected = PresetOptionNames();
            expected.Insert(expected.IndexOf(nameof(RenderingOptions.CompatibilityMode)), nameof(RenderingOptions.UIBackdropStyle));

            Assert.That(VisibleOptionNames(options), Is.EqualTo(expected));
        }

        /// <summary>Checks that the custom preset additionally shows every detailed parameter and diagnostic.</summary>
        [Test]
        public void Visibility_CustomShowsEveryOption()
        {
            var options = new RenderingOptions();
            options.EnterCustomMode();
            var expected = UiOptionNames();
            expected.Remove(nameof(RenderingOptions.UIBackdropStyle));

            Assert.That(VisibleOptionNames(options), Is.EqualTo(expected));
        }

        /// <summary>Checks that compatibility mode hides the preset selector and the detailed parameters, even for the custom preset.</summary>
        [Test]
        public void Visibility_CompatibilityShowsFramePacingAndSwitch()
        {
            var preset = new RenderingOptions { CompatibilityMode = true };
            var custom = new RenderingOptions { CompatibilityMode = true };
            custom.EnterCustomMode();
            var expected = PresetOptionNames();
            expected.Remove(nameof(RenderingOptions.RenderQuality));

            Assert.That(VisibleOptionNames(preset), Is.EqualTo(expected));
            Assert.That(VisibleOptionNames(custom), Is.EqualTo(expected));
        }

        /// <summary>Checks that the visibility follows the options as they change, without rebuilding the metadata.</summary>
        [Test]
        public void Visibility_FollowsPresetAndCompatibilityChanges()
        {
            var options = new RenderingOptions();
            var antiAliasing = SettingReflectionCache.GetVisibility(typeof(RenderingOptions).GetProperty(nameof(RenderingOptions.AntiAliasing))!);

            Assert.That(antiAliasing.IsVisible(options), Is.False);
            options.EnterCustomMode();
            Assert.That(antiAliasing.IsVisible(options), Is.True);
            options.CompatibilityMode = true;
            Assert.That(antiAliasing.IsVisible(options), Is.False);
            options.CompatibilityMode = false;
            options.RenderQuality = RenderingQualityOption.Beautiful;
            Assert.That(antiAliasing.IsVisible(options), Is.False);
        }

        /// <summary>Checks named presets use one unified UI style and Custom retains that style.</summary>
        /// <param name="quality">The named preset selected before Custom.</param>
        /// <param name="blur">Whether that preset enables the effect.</param>
        [TestCase(RenderingQualityOption.EnergySaving, false)]
        [TestCase(RenderingQualityOption.Performance, false)]
        [TestCase(RenderingQualityOption.Beautiful, false)]
        [TestCase(RenderingQualityOption.Refined, true)]
        public void UIBackdropBlur_PresetAndCustomInheritance(RenderingQualityOption quality, bool blur)
        {
            var options = new RenderingOptions { RenderQuality = quality };

            Assert.That(options.GetEffectiveUIBackdropBlur(), Is.EqualTo(blur));
            var mode = options.UIOutlineMode;
            options.EnterCustomMode();
            var restored = Read(Serializer.Json.Serialize(options, MajEnv.UserJsonReaderOption));
            Assert.That(restored.RenderQuality, Is.EqualTo(RenderingQualityOption.Custom));
            Assert.That(restored.UIOutlineMode, Is.EqualTo(mode));
            Assert.That(restored.GetEffectiveUIBackdropBlur(), Is.EqualTo(blur));
        }

        /// <summary>Checks only explicit glass styles capture backgrounds, while compatibility preserves the saved style.</summary>
        [Test]
        public void UIBackdropBlur_OverridesPreserveStoredChoice()
        {
            var options = new RenderingOptions { RenderQuality = RenderingQualityOption.Refined };
            options.EnterCustomMode();
            options.UIOutlineMode = UiOutlineMode.None;
            Assert.That(options.GetEffectiveUIBackdropBlur(), Is.False);
            options.UIOutlineMode = UiOutlineMode.All;
            Assert.That(options.GetEffectiveUIBackdropBlur(), Is.False, "Translucent is ordinary alpha blending, not inherited glass.");
            options.UIOutlineMode = UiOutlineMode.LiquidGlass;
            options.CompatibilityMode = true;
            Assert.That(options.GetEffectiveUIBackdropBlur(), Is.False);
            options.CompatibilityMode = false;
            Assert.That(options.GetEffectiveUIBackdropBlur(), Is.True);
            Assert.That(options.GetEffectiveUIBackdropStyle(), Is.EqualTo(UiBackdropStyle.LiquidGlass));
            options.UIBackdropStyle = UiBackdropStyle.GaussianBlur;
            Assert.That(options.GetEffectiveUIBackdropStyle(), Is.EqualTo(UiBackdropStyle.LiquidGlass), "Custom uses only its unified UI style.");
            Assert.That(options.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.Disabled));
            Assert.That(options.RenderScale, Is.EqualTo(100));
            Assert.That(options.OutputMode, Is.EqualTo(RenderingOutputMode.Direct));
        }

        /// <summary>Checks missing old Custom fields stay disabled while a saved Refined preset reapplies its effect.</summary>
        /// <param name="json">The rendering document in either JSON property order.</param>
        /// <param name="blur">The expected effective effect.</param>
        [TestCase("{\"RenderQuality\":\"Custom\",\"UIOutlineMode\":\"All\"}", false)]
        [TestCase("{\"UIBackdropBlur\":true,\"RenderQuality\":\"Beautiful\"}", false)]
        [TestCase("{\"RenderQuality\":\"Refined\",\"UIBackdropBlur\":false}", true)]
        [TestCase("{\"UIBackdropBlur\":true,\"RenderQuality\":\"Custom\",\"UIOutlineMode\":\"All\"}", false)]
        [TestCase("{\"UIOutlineMode\":\"All\",\"UIBackdropStyle\":\"LiquidGlass\",\"RenderQuality\":\"Custom\",\"UIBackdropBlur\":true}", false)]
        [TestCase("{\"RenderQuality\":\"Custom\",\"UIOutlineMode\":\"Performance\",\"UIBackdropBlur\":true}", false)]
        [TestCase("{\"RenderQuality\":\"Custom\",\"UIOutlineMode\":\"GaussianBlur\",\"UIBackdropBlur\":false,\"UIBackdropStyle\":\"LiquidGlass\"}", true)]
        [TestCase("{\"UIBackdropStyle\":\"GaussianBlur\",\"UIBackdropBlur\":false,\"UIOutlineMode\":\"LiquidGlass\",\"RenderQuality\":\"Custom\"}", true)]
        public void Deserialize_UIBackdropBlurPreservesPresetAndCustomSemantics(string json, bool blur)
        {
            var options = Read(json);
            var written = Serializer.Json.Serialize(options, MajEnv.UserJsonReaderOption);
            var restored = Read(written);

            Assert.That(options.GetEffectiveUIBackdropBlur(), Is.EqualTo(blur));
            Assert.That(JObject.Parse(written).ContainsKey("UIBackdropBlur"), Is.False);
            Assert.That(restored.UIOutlineMode, Is.EqualTo(options.UIOutlineMode));
            Assert.That(restored.GetEffectiveUIBackdropBlur(), Is.EqualTo(blur));
            Assert.That(restored.GetEffectiveUIBackdropStyle(), Is.EqualTo(options.GetEffectiveUIBackdropStyle()));
        }

        /// <summary>Checks only Refined outside compatibility exposes the independent panel-style control.</summary>
        [Test]
        public void UIBackdropStyle_VisibilityRequiresRefinedOutsideCompatibility()
        {
            var visibility = SettingReflectionCache.GetVisibility(typeof(RenderingOptions).GetProperty(nameof(RenderingOptions.UIBackdropStyle))!);
            foreach (RenderingQualityOption quality in Enum.GetValues(typeof(RenderingQualityOption)))
            {
                var options = new RenderingOptions { RenderQuality = quality };
                Assert.That(visibility.IsVisible(options), Is.EqualTo(quality == RenderingQualityOption.Refined));
                options.CompatibilityMode = true;
                Assert.That(visibility.IsVisible(options), Is.False);
            }
            Assert.That(UiOptionNames(), Has.No.Member("UIBackdropBlur"));
        }

        /// <summary>Checks panel-style changes retain Refined and remain saved through quality switches.</summary>
        [Test]
        public void UIBackdropStyle_SwitchingPreservesQualityAndSavedPreference()
        {
            var options = new RenderingOptions { RenderQuality = RenderingQualityOption.Refined };
            options.UIBackdropStyle = UiBackdropStyle.LiquidGlass;

            Assert.That(options.RenderQuality, Is.EqualTo(RenderingQualityOption.Refined));
            Assert.That(options.GetEffectiveUIBackdropBlur(), Is.True);
            Assert.That(options.RenderScale, Is.EqualTo(100));
            Assert.That(options.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.Disabled));
            foreach (RenderingQualityOption quality in Enum.GetValues(typeof(RenderingQualityOption)))
            {
                options.RenderQuality = quality;
                var restored = Read(Serializer.Json.Serialize(options, MajEnv.UserJsonReaderOption));
                Assert.That(options.UIBackdropStyle, Is.EqualTo(UiBackdropStyle.LiquidGlass));
                Assert.That(restored.UIBackdropStyle, Is.EqualTo(UiBackdropStyle.LiquidGlass));
                Assert.That(restored.RenderQuality, Is.EqualTo(quality));
            }
        }

        /// <summary>Checks missing, invalid and explicit style values remain compatible with existing saved blur flags.</summary>
        /// <param name="json">The rendering document to deserialize.</param>
        /// <param name="style">The expected repaired or explicit style.</param>
        [TestCase("{\"RenderQuality\":\"Refined\"}", UiBackdropStyle.GaussianBlur)]
        [TestCase("{\"RenderQuality\":\"Custom\",\"UIBackdropBlur\":true,\"UIOutlineMode\":\"All\"}", UiBackdropStyle.GaussianBlur)]
        [TestCase("{\"UIBackdropStyle\":99,\"RenderQuality\":\"Refined\"}", UiBackdropStyle.GaussianBlur)]
        [TestCase("{\"UIBackdropStyle\":\"LiquidGlass\",\"RenderQuality\":\"Refined\"}", UiBackdropStyle.LiquidGlass)]
        [TestCase("{\"RenderQuality\":\"Refined\",\"UIBackdropStyle\":\"LiquidGlass\"}", UiBackdropStyle.LiquidGlass)]
        public void Deserialize_UIBackdropStyleKeepsLegacyDefaults(string json, UiBackdropStyle style)
        {
            var options = Read(json);
            var restored = Read(Serializer.Json.Serialize(options, MajEnv.UserJsonReaderOption));

            Assert.That(options.UIBackdropStyle, Is.EqualTo(style));
            Assert.That(restored.UIBackdropStyle, Is.EqualTo(style));
            Assert.That(restored.GetEffectiveUIBackdropBlur(), Is.EqualTo(options.GetEffectiveUIBackdropBlur()));
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
