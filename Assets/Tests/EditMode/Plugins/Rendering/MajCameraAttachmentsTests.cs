using System;
using MajdataPlay.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Rendering
{
    /// <summary>Verifies scene attachment allocation, reuse and render pass recording.</summary>
    public class MajCameraAttachmentsTests
    {
        /// <summary>Owns the attachments under test.</summary>
        private MajCameraAttachments _attachments = null!;
        /// <summary>Records commands issued by the attachments.</summary>
        private CommandBuffer _buffer = null!;

        /// <summary>Creates the attachments and a command buffer.</summary>
        [SetUp]
        public void SetUp()
        {
            _attachments = new MajCameraAttachments();
            _buffer = new CommandBuffer { name = "MajCameraAttachmentsTests" };
        }

        /// <summary>Releases the attachments, their native descriptor arrays and the command buffer.</summary>
        [TearDown]
        public void TearDown()
        {
            _attachments.Dispose();
            _buffer.Release();
        }

        /// <summary>Gets the MSAA sample count the device supports for a small scene target.</summary>
        /// <param name="requested">The requested sample count.</param>
        /// <returns>The supported sample count.</returns>
        private static int SupportedSamples(int requested)
        {
            var format = SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);
            format = QualitySettings.activeColorSpace == ColorSpace.Linear ? GraphicsFormatUtility.GetSRGBFormat(format) : GraphicsFormatUtility.GetLinearFormat(format);
            var descriptor = new RenderTextureDescriptor(64, 32)
            {
                graphicsFormat = format,
                depthStencilFormat = SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil),
                msaaSamples = requested
            };
            return Mathf.Max(1, SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor));
        }

        /// <summary>Checks that empty sizes are rejected without allocating.</summary>
        [Test]
        public void TryPrepare_RejectsEmptySize()
        {
            Assert.That(_attachments.TryPrepare(0, 32, 1, true, true, false, FilterMode.Point), Is.False);
            Assert.That(_attachments.TryPrepare(32, -1, 1, true, true, false, FilterMode.Point), Is.False);
        }

        /// <summary>Checks a single-sample intermediate target and its reuse with a changed copy filter.</summary>
        [Test]
        public void TryPrepare_SingleSampleIntermediate_ReusesTextures()
        {
            Assert.That(_attachments.TryPrepare(64, 32, 1, true, true, false, FilterMode.Point), Is.True);

            Assert.That(_attachments.Samples, Is.EqualTo(1));
            Assert.That(_attachments.UsesIntermediateColor, Is.True);
            Assert.That(_attachments.ResolvesToScreen, Is.False);
            var color = _attachments.ResolvedColor;
            Assert.That(color.width, Is.EqualTo(64));
            Assert.That(color.height, Is.EqualTo(32));
            Assert.That(color.IsCreated(), Is.True);
            Assert.That(_attachments.SceneColor, Is.SameAs(color));
            Assert.That(_attachments.DepthStencil.width, Is.EqualTo(64));

            Assert.That(_attachments.TryPrepare(64, 32, 1, true, true, false, FilterMode.Bilinear), Is.True);
            Assert.That(_attachments.ResolvedColor, Is.SameAs(color));
            Assert.That(color.filterMode, Is.EqualTo(FilterMode.Bilinear));

            Assert.That(_attachments.TryPrepare(32, 32, 1, true, true, false, FilterMode.Bilinear), Is.True);
            Assert.That(_attachments.ResolvedColor.width, Is.EqualTo(32));
            Assert.That(color == null, Is.True);
        }

        /// <summary>Checks that an intermediate target is refused when the final copy is unavailable.</summary>
        [Test]
        public void TryPrepare_WithoutFinalCopy_RefusesIntermediate()
        {
            Assert.That(_attachments.TryPrepare(64, 32, 1, false, true, false, FilterMode.Point), Is.False);
            Assert.That(_attachments.TryPrepare(64, 32, 1, true, true, false, FilterMode.Point), Is.True);
            Assert.That(_attachments.TryPrepare(64, 32, 1, false, true, false, FilterMode.Point), Is.False);
        }

        /// <summary>Checks a depth-only native target that renders directly to the camera target.</summary>
        [Test]
        public void TryPrepare_DirectSingleSample_AllocatesDepthOnly()
        {
            Assert.That(_attachments.TryPrepare(64, 32, 1, true, false, true, FilterMode.Point), Is.True);

            Assert.That(_attachments.UsesIntermediateColor, Is.False);
            Assert.That(_attachments.ResolvedColor, Is.Null);
            Assert.That(_attachments.DepthStencil, Is.Not.Null);

            _attachments.BeginScene(_buffer, Color.black);
            _buffer.EndRenderPass();
            Assert.That(_buffer.sizeInBytes, Is.GreaterThan(0));
        }

        /// <summary>Checks multisampled attachments, both with a resolve texture and resolving directly to the screen.</summary>
        [Test]
        public void TryPrepare_Multisampled_SelectsResolveTarget()
        {
            var samples = SupportedSamples(4);
            if (samples == 1)
            {
                Assert.Ignore("The graphics device has no multisampled render textures.");
            }

            Assert.That(_attachments.TryPrepare(64, 32, 4, true, false, false, FilterMode.Point), Is.True);
            Assert.That(_attachments.Samples, Is.EqualTo(samples));
            Assert.That(_attachments.UsesIntermediateColor, Is.True);
            Assert.That(_attachments.SceneColor, Is.Not.SameAs(_attachments.ResolvedColor));
            Assert.That(_attachments.SceneColor.antiAliasing, Is.EqualTo(samples));
            _attachments.BeginScene(_buffer, Color.black);
            _buffer.EndRenderPass();
            _attachments.SetCompatibleSceneTarget(_buffer);
            _attachments.SetCompatibleSceneTarget(_buffer, true);
            _attachments.ResolveCompatible(_buffer);

            Assert.That(_attachments.TryPrepare(64, 32, 4, true, false, true, FilterMode.Point, true), Is.True);
            Assert.That(_attachments.ResolvesToScreen, Is.True);
            Assert.That(_attachments.UsesIntermediateColor, Is.False);
            Assert.That(_attachments.ResolvedColor, Is.Null);
            _attachments.BeginScene(_buffer, Color.black);
            _buffer.EndRenderPass();
            Assert.That(_buffer.sizeInBytes, Is.GreaterThan(0));
        }

        /// <summary>Checks the single-sample compatible path commands and the final copy pass for full and offset viewports.</summary>
        [Test]
        public void Commands_RecordCompatibleAndFinalCopyPasses()
        {
            Assert.That(_attachments.TryPrepare(64, 32, 1, true, true, true, FilterMode.Point), Is.True);

            _attachments.BeginScene(_buffer, Color.red);
            _buffer.EndRenderPass();
            _attachments.SetCompatibleSceneTarget(_buffer);
            _attachments.ResolveCompatible(_buffer);
            _attachments.BeginFinalCopy(_buffer, 64, 32, new Rect(0, 0, 64, 32), Color.blue);
            _buffer.EndRenderPass();
            _attachments.BeginFinalCopy(_buffer, 64, 32, new Rect(16, 0, 32, 32), Color.blue);
            _buffer.EndRenderPass();

            Assert.That(_buffer.sizeInBytes, Is.GreaterThan(0));
        }

        /// <summary>Checks that releasing temporary targets resets the attachment state.</summary>
        [Test]
        public void ReleaseTargets_ResetsState()
        {
            _attachments.TryPrepare(64, 32, 1, true, true, false, FilterMode.Point);
            var color = _attachments.ResolvedColor;

            _attachments.ReleaseTargets();

            Assert.That(color == null, Is.True);
            Assert.That(_attachments.Samples, Is.EqualTo(1));
            Assert.That(_attachments.UsesIntermediateColor, Is.False);
            Assert.That(_attachments.ResolvesToScreen, Is.False);
            Assert.That(_attachments.TryPrepare(64, 32, 1, true, true, false, FilterMode.Point), Is.True);
        }

        /// <summary>Checks that disposal can be repeated.</summary>
        [Test]
        public void Dispose_IsIdempotent()
        {
            _attachments.TryPrepare(16, 16, 1, true, true, false, FilterMode.Point);
            _attachments.Dispose();

            Assert.DoesNotThrow(() => _attachments.Dispose());
        }

        /// <summary>Checks MSAA color creation: rejected candidates, a memoryless retry and an accepted texture.</summary>
        [Test]
        public void CreateMSAAColor_RetriesWithoutMemoryless()
        {
            var descriptor = new RenderTextureDescriptor(16, 16, RenderTextureFormat.ARGB32, 0) { msaaSamples = 2, bindMS = true };
            var calls = 0;

            var none = MajCameraAttachments.CreateMSAAColor(descriptor, false, out var rejected, (_, _) =>
            {
                calls++;
                return null;
            });

            Assert.That(none, Is.Null);
            Assert.That(rejected, Is.False);
            Assert.That(calls, Is.EqualTo(1));

            calls = 0;
            var retried = MajCameraAttachments.CreateMSAAColor(descriptor, true, out rejected, (_, _) =>
            {
                calls++;
                return null;
            });

            Assert.That(retried, Is.Null);
            Assert.That(rejected, Is.True);
            Assert.That(calls, Is.EqualTo(2));
        }

        /// <summary>Checks that a texture matching the requested descriptor is accepted and a mismatching one is released.</summary>
        [Test]
        public void CreateMSAAColor_AcceptsMatchingTexture()
        {
            var samples = SupportedSamples(2);
            if (samples == 1)
            {
                Assert.Ignore("The graphics device has no multisampled render textures.");
            }

            var descriptor = new RenderTextureDescriptor(16, 16, RenderTextureFormat.ARGB32, 0) { msaaSamples = samples, bindMS = true };
            RenderTexture? mismatch = null;
            var accepted = MajCameraAttachments.CreateMSAAColor(descriptor, true, out var rejected, (requested, name) =>
            {
                if (requested.memoryless != RenderTextureMemoryless.None)
                {
                    mismatch = new RenderTexture(16, 16, 0);
                    mismatch.Create();
                    return mismatch;
                }

                var texture = new RenderTexture(requested) { name = name };
                texture.Create();
                return texture;
            });
            try
            {
                Assert.That(rejected, Is.True);
                Assert.That(accepted, Is.Not.Null);
                Assert.That(accepted!.antiAliasing, Is.EqualTo(samples));
                Assert.That(accepted.name, Is.EqualTo("MRP Scene MSAA Color"));
                Assert.That(mismatch == null, Is.True);
            }
            finally
            {
                if (accepted != null)
                {
                    accepted.Release();
                    Object.DestroyImmediate(accepted);
                }
            }
        }
    }
}
