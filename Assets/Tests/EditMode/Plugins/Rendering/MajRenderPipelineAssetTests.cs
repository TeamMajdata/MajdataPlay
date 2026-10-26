using MajdataPlay.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Rendering
{
    /// <summary>Verifies the pipeline asset's runtime overrides and the global state managed by pipeline instances.</summary>
    public class MajRenderPipelineAssetTests
    {
        /// <summary>Stores the asset created by the current test.</summary>
        private MajRenderPipelineAsset _asset = null!;

        /// <summary>Creates an unsaved pipeline asset with default serialized settings.</summary>
        [SetUp]
        public void SetUp()
        {
            _asset = ScriptableObject.CreateInstance<MajRenderPipelineAsset>();
        }

        /// <summary>Destroys the asset.</summary>
        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_asset);
        }

        /// <summary>Checks the defaults exposed before any runtime override.</summary>
        [Test]
        public void Defaults_FollowSerializedSettings()
        {
            Assert.That(_asset.DefaultRenderScale, Is.EqualTo(1f));
            Assert.That(_asset.RenderScale, Is.EqualTo(1f));
            Assert.That(_asset.MsaaMode, Is.EqualTo(MajMSAAMode.UseQualitySettings));
            Assert.That(_asset.ResolveToScreen, Is.True);
            Assert.That(_asset.ForceIntermediateOutput, Is.False);
            Assert.That(_asset.TextureMipmapBias, Is.EqualTo(0));
            Assert.That(_asset.renderPipelineShaderTag, Is.EqualTo("UniversalPipeline"));
        }

        /// <summary>Checks that the render scale override is clamped and can be reset alone.</summary>
        [Test]
        public void RenderScale_OverrideIsClampedAndResettable()
        {
            _asset.RenderScale = 5f;
            Assert.That(_asset.RenderScale, Is.EqualTo(2f));
            _asset.RenderScale = 0.5f;
            Assert.That(_asset.RenderScale, Is.EqualTo(0.5f));

            _asset.ResetRenderScaleOverride();

            Assert.That(_asset.RenderScale, Is.EqualTo(_asset.DefaultRenderScale));
        }

        /// <summary>Checks that runtime rendering overrides apply as a group and reset together.</summary>
        [Test]
        public void ConfigureRuntimeRendering_AppliesAndResetsOverrides()
        {
            _asset.ConfigureRuntimeRendering(0.75f, MajMSAAMode.MSAA4x, false, 7);

            Assert.That(_asset.RenderScale, Is.EqualTo(0.75f));
            Assert.That(_asset.MsaaMode, Is.EqualTo(MajMSAAMode.MSAA4x));
            Assert.That(_asset.ResolveToScreen, Is.False);
            Assert.That(_asset.ForceIntermediateOutput, Is.True);
            Assert.That(_asset.TextureMipmapBias, Is.EqualTo(2));

            _asset.ConfigureRuntimeRendering(9f, (MajMSAAMode)3, true, -4);

            Assert.That(_asset.RenderScale, Is.EqualTo(2f));
            Assert.That(_asset.MsaaMode, Is.EqualTo(MajMSAAMode.Disabled));
            Assert.That(_asset.ResolveToScreen, Is.True);
            Assert.That(_asset.ForceIntermediateOutput, Is.False);
            Assert.That(_asset.TextureMipmapBias, Is.EqualTo(0));

            foreach (var mode in new[] { MajMSAAMode.UseQualitySettings, MajMSAAMode.Disabled, MajMSAAMode.MSAA2x, MajMSAAMode.MSAA8x })
            {
                _asset.ConfigureRuntimeRendering(1f, mode, true);
                Assert.That(_asset.MsaaMode, Is.EqualTo(mode));
            }

            _asset.ResetRuntimeRenderingOverrides();

            Assert.That(_asset.RenderScale, Is.EqualTo(1f));
            Assert.That(_asset.MsaaMode, Is.EqualTo(MajMSAAMode.UseQualitySettings));
            Assert.That(_asset.ResolveToScreen, Is.True);
            Assert.That(_asset.ForceIntermediateOutput, Is.False);
            Assert.That(_asset.TextureMipmapBias, Is.EqualTo(0));
        }

        /// <summary>Checks that validation clamps the serialized scale and locates the final copy shader.</summary>
        [Test]
        public void OnValidate_ClampsScaleAndFindsShader()
        {
            PrivateMember.SetField(_asset, "_renderScale", 7f);
            PrivateMember.SetField(_asset, "_finalCopyShader", null);

            PrivateMember.Invoke(_asset, "OnValidate");

            Assert.That(_asset.DefaultRenderScale, Is.EqualTo(2f));
            Assert.That(PrivateMember.GetField<Shader>(_asset, "_finalCopyShader"), Is.EqualTo(Shader.Find("Hidden/Maj/FinalCopy")));
        }

        /// <summary>Checks that a pipeline instance overrides global rendering state and restores it when disposed.</summary>
        [Test]
        public void Pipeline_OverridesAndRestoresGlobalState()
        {
            var batching = GraphicsSettings.useScriptableRenderPipelineBatching;
            var anisotropic = QualitySettings.anisotropicFiltering;
            var features = SupportedRenderingFeatures.active;
            using var diagnostics = new DiagnosticsSnapshot();
            try
            {
                PrivateMember.SetField(_asset, "_useSRPBatcher", !batching);
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
                var pipeline = (MajRenderPipeline)PrivateMember.Invoke(_asset, "CreatePipeline")!;

                Assert.That(GraphicsSettings.useScriptableRenderPipelineBatching, Is.EqualTo(!batching));
                Assert.That(QualitySettings.anisotropicFiltering, Is.EqualTo(AnisotropicFiltering.Disable));
                Assert.That(SupportedRenderingFeatures.active.rendersUIOverlay, Is.False);
                Assert.That(SupportedRenderingFeatures.active.supportsHDR, Is.False);

                QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
                PrivateMember.Invoke(pipeline, "OnActiveQualityLevelChanged", 0, 1);
                Assert.That(QualitySettings.anisotropicFiltering, Is.EqualTo(AnisotropicFiltering.Disable));

                PrivateMember.Invoke(pipeline, "Dispose", true);

                Assert.That(GraphicsSettings.useScriptableRenderPipelineBatching, Is.EqualTo(batching));
                Assert.That(QualitySettings.anisotropicFiltering, Is.EqualTo(AnisotropicFiltering.ForceEnable));
                Assert.That(SupportedRenderingFeatures.active, Is.SameAs(features));
                Assert.That(MajRenderDiagnostics.OutputStatus, Is.EqualTo(MajRenderOutputStatus.Unavailable));
            }
            finally
            {
                GraphicsSettings.useScriptableRenderPipelineBatching = batching;
                QualitySettings.anisotropicFiltering = anisotropic;
                SupportedRenderingFeatures.active = features;
            }
        }
    }
}
