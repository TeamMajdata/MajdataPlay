using MajdataPlay.Rendering.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Rendering
{
    /// <summary>Verifies the surface, blend, keyword and queue state that the Maj Unlit material inspector derives.</summary>
    public class MajUnlitShaderGUITests
    {
        /// <summary>Stores the material under test.</summary>
        private Material _material = null!;
        /// <summary>Stores the inspector under test.</summary>
        private MajUnlitShaderGUI _gui = null!;

        /// <summary>Creates a material using the Maj Unlit shader.</summary>
        [SetUp]
        public void SetUp()
        {
            var shader = Shader.Find("Maj/Unlit");
            if (shader == null)
            {
                Assert.Ignore("The Maj/Unlit shader is unavailable.");
            }

            _material = new Material(shader);
            _gui = new MajUnlitShaderGUI();
        }

        /// <summary>Destroys the material.</summary>
        [TearDown]
        public void TearDown()
        {
            if (_material != null)
            {
                Object.DestroyImmediate(_material);
            }
        }

        /// <summary>Applies surface settings and validates the material.</summary>
        /// <param name="transparent">Whether the surface is transparent.</param>
        /// <param name="blend">The blend mode index.</param>
        /// <param name="alphaClip">Whether alpha clipping is enabled.</param>
        /// <param name="queueOffset">The render queue offset.</param>
        private void Validate(bool transparent, int blend, bool alphaClip, int queueOffset = 0)
        {
            _material.SetFloat("_Surface", transparent ? 1f : 0f);
            _material.SetFloat("_Blend", blend);
            _material.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
            _material.SetFloat("_QueueOffset", queueOffset);
            _gui.ValidateMaterial(_material);
        }

        /// <summary>Checks the opaque configuration.</summary>
        [Test]
        public void Opaque_WritesDepthWithoutBlending()
        {
            Validate(false, 0, false, 3);

            Assert.That(_material.GetFloat("_SrcBlend"), Is.EqualTo((float)BlendMode.One));
            Assert.That(_material.GetFloat("_DstBlend"), Is.EqualTo((float)BlendMode.Zero));
            Assert.That(_material.GetFloat("_ZWrite"), Is.EqualTo(1f));
            Assert.That(_material.GetFloat("_AlphaToMask"), Is.EqualTo(0f));
            Assert.That(_material.GetTag("RenderType", false), Is.EqualTo("Opaque"));
            Assert.That(_material.renderQueue, Is.EqualTo((int)RenderQueue.Geometry + 3));
            Assert.That(_material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), Is.False);
            Assert.That(_material.IsKeywordEnabled("_ALPHATEST_ON"), Is.False);
        }

        /// <summary>Checks the alpha-clipped opaque configuration with alpha to coverage.</summary>
        [Test]
        public void AlphaClip_UsesAlphaTestQueueAndCoverage()
        {
            Validate(false, 0, true);

            Assert.That(_material.IsKeywordEnabled("_ALPHATEST_ON"), Is.True);
            Assert.That(_material.GetFloat("_AlphaToMask"), Is.EqualTo(1f));
            Assert.That(_material.GetTag("RenderType", false), Is.EqualTo("TransparentCutout"));
            Assert.That(_material.renderQueue, Is.EqualTo((int)RenderQueue.AlphaTest));
        }

        /// <summary>Checks the four transparent blend modes.</summary>
        /// <param name="blend">The blend mode index.</param>
        /// <param name="source">The expected color source factor.</param>
        /// <param name="destination">The expected color destination factor.</param>
        /// <param name="sourceAlpha">The expected alpha source factor.</param>
        /// <param name="destinationAlpha">The expected alpha destination factor.</param>
        [TestCase(0, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, BlendMode.One, BlendMode.OneMinusSrcAlpha)]
        [TestCase(1, BlendMode.One, BlendMode.OneMinusSrcAlpha, BlendMode.One, BlendMode.OneMinusSrcAlpha)]
        [TestCase(2, BlendMode.SrcAlpha, BlendMode.One, BlendMode.One, BlendMode.One)]
        [TestCase(3, BlendMode.DstColor, BlendMode.Zero, BlendMode.Zero, BlendMode.One)]
        public void Transparent_ConfiguresBlendMode(int blend, BlendMode source, BlendMode destination, BlendMode sourceAlpha, BlendMode destinationAlpha)
        {
            Validate(true, blend, true, -2);

            Assert.That(_material.GetFloat("_SrcBlend"), Is.EqualTo((float)source));
            Assert.That(_material.GetFloat("_DstBlend"), Is.EqualTo((float)destination));
            Assert.That(_material.GetFloat("_SrcBlendAlpha"), Is.EqualTo((float)sourceAlpha));
            Assert.That(_material.GetFloat("_DstBlendAlpha"), Is.EqualTo((float)destinationAlpha));
            Assert.That(_material.GetFloat("_ZWrite"), Is.EqualTo(0f));
            Assert.That(_material.GetFloat("_AlphaToMask"), Is.EqualTo(0f));
            Assert.That(_material.GetTag("RenderType", false), Is.EqualTo("Transparent"));
            Assert.That(_material.renderQueue, Is.EqualTo((int)RenderQueue.Transparent - 2));
            Assert.That(_material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), Is.True);
            Assert.That(_material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"), Is.EqualTo(blend == 1));
            Assert.That(_material.IsKeywordEnabled("_ALPHAMODULATE_ON"), Is.EqualTo(blend == 3));
        }

        /// <summary>Checks that out-of-range blend values are clamped.</summary>
        [Test]
        public void Transparent_ClampsBlendIndex()
        {
            Validate(true, 9, false);

            Assert.That(_material.GetFloat("_SrcBlend"), Is.EqualTo((float)BlendMode.DstColor));
        }

        /// <summary>Checks that materials without the surface property are left unchanged.</summary>
        [Test]
        public void MaterialWithoutSurface_IsIgnored()
        {
            var shader = Shader.Find("Hidden/Maj/FinalCopy");
            var other = new Material(shader) { renderQueue = 1234 };
            try
            {
                _gui.ValidateMaterial(other);

                Assert.That(other.renderQueue, Is.EqualTo(1234));
            }
            finally
            {
                Object.DestroyImmediate(other);
            }
        }
    }
}
