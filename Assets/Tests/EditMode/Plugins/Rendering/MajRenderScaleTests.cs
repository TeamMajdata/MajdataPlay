using MajdataPlay.Rendering;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Rendering
{
    /// <summary>Verifies the numeric render-scale and final-copy mapping helpers of the Maj Render Pipeline.</summary>
    public class MajRenderScaleTests
    {
        /// <summary>Checks clamping of finite and non-finite scales.</summary>
        /// <param name="scale">The requested scale.</param>
        /// <param name="expected">The clamped scale.</param>
        [TestCase(float.NaN, 1f)]
        [TestCase(float.PositiveInfinity, 1f)]
        [TestCase(float.NegativeInfinity, 1f)]
        [TestCase(0.05f, 0.1f)]
        [TestCase(3f, 2f)]
        [TestCase(0.75f, 0.75f)]
        public void Clamp_KeepsScaleInRange(float scale, float expected)
        {
            Assert.That(MajRenderScale.Clamp(scale), Is.EqualTo(expected));
            Assert.That(MajRenderScale.Effective(scale), Is.EqualTo(expected));
        }

        /// <summary>Checks that supersampling is limited by the device's maximum texture size.</summary>
        [Test]
        public void LimitToTextureSize_RespectsMaximumTextureSize()
        {
            var maximum = SystemInfo.maxTextureSize;

            Assert.That(MajRenderScale.LimitToTextureSize(2f, 100, 50), Is.EqualTo(2f));
            Assert.That(MajRenderScale.LimitToTextureSize(2f, maximum, 10), Is.EqualTo(1f));
            Assert.That(MajRenderScale.LimitToTextureSize(0.5f, 0, 0), Is.EqualTo(0.5f));
        }

        /// <summary>Checks that scaled dimensions truncate and stay within one pixel and the maximum texture size.</summary>
        [Test]
        public void ScaledDimension_TruncatesAndClamps()
        {
            Assert.That(MajRenderScale.ScaledDimension(1080, 0.5f), Is.EqualTo(540));
            Assert.That(MajRenderScale.ScaledDimension(1081, 0.5f), Is.EqualTo(540));
            Assert.That(MajRenderScale.ScaledDimension(1, 0.1f), Is.EqualTo(1));
            Assert.That(MajRenderScale.ScaledDimension(SystemInfo.maxTextureSize, 2f), Is.EqualTo(SystemInfo.maxTextureSize));
        }

        /// <summary>Checks the filter chosen for supersampling, integer upscaling and fractional upscaling.</summary>
        /// <param name="width">The output width.</param>
        /// <param name="height">The output height.</param>
        /// <param name="scale">The effective scale.</param>
        /// <param name="expected">The expected filter.</param>
        [TestCase(1920, 1080, 1.5f, FilterMode.Bilinear)]
        [TestCase(1920, 1080, 0.5f, FilterMode.Point)]
        [TestCase(1920, 1080, 1f, FilterMode.Point)]
        [TestCase(1920, 1080, 0.75f, FilterMode.Bilinear)]
        [TestCase(1921, 1080, 0.5f, FilterMode.Bilinear)]
        [TestCase(1920, 1081, 0.5f, FilterMode.Bilinear)]
        public void AutoFilter_SelectsFilter(int width, int height, float scale, FilterMode expected)
        {
            Assert.That(MajRenderScale.AutoFilter(width, height, scale), Is.EqualTo(expected));
        }

        /// <summary>Checks the mip bias compensation for downscaled scenes.</summary>
        [Test]
        public void MipBias_OnlyBiasesDownscaledScenes()
        {
            Assert.That(MajRenderScale.MipBias(1080, 1080), Is.EqualTo(new Vector4(0f, 1f, 0f, 0f)));
            Assert.That(MajRenderScale.MipBias(2160, 1080), Is.EqualTo(new Vector4(0f, 1f, 0f, 0f)));
            var half = MajRenderScale.MipBias(540, 1080);
            Assert.That(half.x, Is.EqualTo(-1f).Within(1e-5f));
            Assert.That(half.y, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(MajRenderScale.MipBias(0, 0).x, Is.EqualTo(0f));
        }

        /// <summary>Checks the normalized-to-surface viewport mapping.</summary>
        [Test]
        public void GetSurfaceViewport_ScalesNormalizedRect()
        {
            var viewport = MajFinalCopyMapping.GetSurfaceViewport(new Rect(0.25f, 0.5f, 0.5f, 0.5f), 800, 600);

            Assert.That(viewport, Is.EqualTo(new Rect(200, 300, 400, 300)));
        }

        /// <summary>Checks the clip-space scale and bias for full and offset viewports and both projection orientations.</summary>
        [Test]
        public void GetClipScaleBias_PlacesViewport()
        {
            Assert.That(MajFinalCopyMapping.GetClipScaleBias(new Rect(0, 0, 800, 600), 800, 600, 1f), Is.EqualTo(new Vector4(1f, 1f, 0f, 0f)));
            var offset = MajFinalCopyMapping.GetClipScaleBias(new Rect(400, 0, 400, 300), 800, 600, 1f);
            Assert.That(offset, Is.EqualTo(new Vector4(0.5f, 0.5f, 0.5f, -0.5f)));
            var flipped = MajFinalCopyMapping.GetClipScaleBias(new Rect(400, 0, 400, 300), 800, 600, -1f);
            Assert.That(flipped.w, Is.EqualTo(0.5f));
            var empty = MajFinalCopyMapping.GetClipScaleBias(new Rect(0, 0, 1, 1), 0, 0, 1f);
            Assert.That(empty.x, Is.EqualTo(1f));
        }

        /// <summary>Checks which viewports can use native screen targets and direct resolves.</summary>
        [Test]
        public void NativeScreenAndResolveChecks()
        {
            var full = new Rect(0f, 0f, 1f, 1f);
            var offset = new Rect(0.1f, 0f, 1f, 1f);

            Assert.That(MajFinalCopyMapping.SupportsNativeScreenTarget(full, 800, 600, 800, 600), Is.True);
            Assert.That(MajFinalCopyMapping.SupportsNativeScreenTarget(full, 800, 600, 640, 600), Is.False);
            Assert.That(MajFinalCopyMapping.SupportsNativeScreenTarget(new Rect(0f, 0f, 0.5f, 1f), 400, 600, 400, 600), Is.False);
            Assert.That(MajFinalCopyMapping.SupportsNativeScreenTarget(offset, 800, 600, 800, 600), Is.True);
            Assert.That(MajFinalCopyMapping.CanResolveViewportToScreen(full), Is.True);
            Assert.That(MajFinalCopyMapping.CanResolveViewportToScreen(offset), Is.False);
        }
    }
}
