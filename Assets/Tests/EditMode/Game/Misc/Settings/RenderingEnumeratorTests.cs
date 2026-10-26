using MajdataPlay.Rendering;
using MajdataPlay.Settings;
using MajdataPlay.Settings.OptionEnumerators;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Settings
{
    /// <summary>
    /// Covers the option enumerators that apply rendering and engine settings immediately. Every engine setting they
    /// change is restored after each test.
    /// </summary>
    public class RenderingEnumeratorTests
    {
        /// <summary>The VSync count before the test.</summary>
        private int _vSyncCount;

        /// <summary>The target frame rate before the test.</summary>
        private int _targetFrameRate;

        /// <summary>The quality level before the test.</summary>
        private int _qualityLevel;

        /// <summary>The global texture mipmap limit before the test.</summary>
        private int _mipmapLimit;

        /// <summary>Captures the engine settings changed by the enumerators.</summary>
        [SetUp]
        public void SetUp()
        {
            _vSyncCount = QualitySettings.vSyncCount;
            _targetFrameRate = Application.targetFrameRate;
            _qualityLevel = QualitySettings.GetQualityLevel();
            _mipmapLimit = QualitySettings.globalTextureMipmapLimit;
        }

        /// <summary>Restores the engine settings and removes runtime rendering overrides.</summary>
        [TearDown]
        public void TearDown()
        {
            if (QualitySettings.GetQualityLevel() != _qualityLevel)
            {
                QualitySettings.SetQualityLevel(_qualityLevel, true);
            }
            QualitySettings.vSyncCount = _vSyncCount;
            Application.targetFrameRate = _targetFrameRate;
            QualitySettings.globalTextureMipmapLimit = _mipmapLimit;
            if (GraphicsSettings.currentRenderPipeline is MajRenderPipelineAsset pipeline)
            {
                pipeline.ResetRuntimeRenderingOverrides();
            }
        }

        /// <summary>Creates and initializes an enumerator for a rendering option.</summary>
        /// <typeparam name="T">The enumerator type.</typeparam>
        /// <param name="target">The options object.</param>
        /// <param name="name">The property name.</param>
        /// <returns>The initialized enumerator.</returns>
        private static T Create<T>(object target, string name) where T : IOptionEnumerator, new()
        {
            var enumerator = new T();
            enumerator.Init(target.GetType().GetProperty(name)!, target);
            return enumerator;
        }

        /// <summary>Checks that the preset enumerator cycles through presets and skips the hidden Custom entry.</summary>
        [Test]
        public void RenderQuality_SkipsCustom()
        {
            var options = new RenderingOptions { RenderQuality = RenderingQualityOption.Refined };
            var enumerator = Create<RenderingEnumSettingEnumerator>(options, nameof(RenderingOptions.RenderQuality));

            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(options.RenderQuality, Is.EqualTo(RenderingQualityOption.EnergySaving));
            Assert.That(enumerator.MovePrevious(), Is.True);
            Assert.That(options.RenderQuality, Is.EqualTo(RenderingQualityOption.Refined));
            Assert.That(enumerator.Current, Is.EqualTo(RenderingQualityOption.Refined));
        }

        /// <summary>Checks that a value rejected by the options model reports no change and keeps the label in sync.</summary>
        [Test]
        public void OutputMode_RejectedValueReportsNoChange()
        {
            var options = new RenderingOptions { RenderScale = 150 };
            var enumerator = Create<RenderingEnumSettingEnumerator>(options, nameof(RenderingOptions.OutputMode));

            Assert.That(enumerator.MoveNext(), Is.False);
            Assert.That(options.OutputMode, Is.EqualTo(RenderingOutputMode.Intermediate));
            Assert.That(enumerator.Current, Is.EqualTo(RenderingOutputMode.Intermediate));
        }

        /// <summary>Checks that changing anti-aliasing applies the rendering settings and switches to Custom.</summary>
        [Test]
        public void AntiAliasing_AppliesChange()
        {
            var options = new RenderingOptions();
            var enumerator = Create<RenderingEnumSettingEnumerator>(options, nameof(RenderingOptions.AntiAliasing));

            Assert.That(enumerator.MoveNext(), Is.True);

            Assert.That(options.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.MSAA2x));
            Assert.That(options.RenderQuality, Is.EqualTo(RenderingQualityOption.Custom));
            Assert.That(QualitySettings.vSyncCount, Is.EqualTo(MajEnv.Settings.Rendering.VSync ? 1 : 0));
        }

        /// <summary>Checks that the render scale enumerator shows percentages and applies changes.</summary>
        [Test]
        public void RenderScale_ShowsPercentAndSteps()
        {
            var options = new RenderingOptions();
            var enumerator = Create<RenderingNumberSettingEnumerator>(options, nameof(RenderingOptions.RenderScale));

            Assert.That(enumerator.LocalizedValueText, Is.EqualTo("100%"));
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(options.RenderScale, Is.EqualTo(105));
            Assert.That(enumerator.LocalizedValueText, Is.EqualTo("105%"));

            options.RenderScale = 120;
            enumerator.Refresh();
            Assert.That(enumerator.LocalizedValueText, Is.EqualTo("120%"));
            enumerator.RefreshLocalization();
            Assert.That(enumerator.LocalizedValueText, Is.EqualTo("120%"));
        }

        /// <summary>Checks that the render scale stops at its maximum.</summary>
        [Test]
        public void RenderScale_StopsAtMaximum()
        {
            var options = new RenderingOptions { RenderScale = 200 };
            var enumerator = Create<RenderingNumberSettingEnumerator>(options, nameof(RenderingOptions.RenderScale));

            Assert.That(enumerator.MoveNext(), Is.False);
            Assert.That(enumerator.MovePrevious(), Is.True);
            Assert.That(options.RenderScale, Is.EqualTo(195));
        }

        /// <summary>Checks that the system resolution scale cannot be edited outside Android players.</summary>
        [Test]
        public void SystemResolutionScaling_IsUnsupportedInEditor()
        {
            var options = new RenderingOptions();
            var enumerator = Create<RenderingNumberSettingEnumerator>(options, nameof(RenderingOptions.SystemResolutionScaling));

            Assert.That(enumerator.MoveNext(), Is.False);
            Assert.That(enumerator.MovePrevious(), Is.False);
            Assert.That(options.SystemResolutionScaling, Is.EqualTo(100));
            Assert.That(enumerator.LocalizedValueText, Does.Not.EndWith("%"));
        }

        /// <summary>Checks that toggling compatibility mode applies the rendering settings.</summary>
        [Test]
        public void CompatibilityMode_Toggles()
        {
            var options = new RenderingOptions();
            var enumerator = Create<RenderingBooleanSettingEnumerator>(options, nameof(RenderingOptions.CompatibilityMode));

            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(options.CompatibilityMode, Is.True);
            Assert.That(enumerator.MovePrevious(), Is.True);
            Assert.That(options.CompatibilityMode, Is.False);
        }

        /// <summary>Checks that refreshing the VSync option applies the engine VSync count only when it changed.</summary>
        [Test]
        public void VSync_RefreshAppliesEngineSetting()
        {
            var options = new RenderingOptions { VSync = true };
            var enumerator = Create<EngineBooleanSettingEnumerator>(options, nameof(RenderingOptions.VSync));
            QualitySettings.vSyncCount = 1;

            options.VSync = false;
            enumerator.Refresh();
            Assert.That(QualitySettings.vSyncCount, Is.EqualTo(0));

            QualitySettings.vSyncCount = 3;
            enumerator.Refresh();
            Assert.That(QualitySettings.vSyncCount, Is.EqualTo(3));

            options.VSync = true;
            enumerator.Refresh();
            Assert.That(QualitySettings.vSyncCount, Is.EqualTo(1));
        }

        /// <summary>Checks that refreshing the frame rate option applies the engine target frame rate only when it changed.</summary>
        [Test]
        public void FpsLimit_RefreshAppliesTargetFrameRate()
        {
            var options = new RenderingOptions { FPSLimit = 60 };
            var enumerator = Create<EngineNumberSettingEnumerator>(options, nameof(RenderingOptions.FPSLimit));
            Application.targetFrameRate = 30;

            enumerator.Refresh();
            Assert.That(Application.targetFrameRate, Is.EqualTo(30));

            options.FPSLimit = 75;
            enumerator.Refresh();
            Assert.That(Application.targetFrameRate, Is.EqualTo(75));
        }
    }
}
