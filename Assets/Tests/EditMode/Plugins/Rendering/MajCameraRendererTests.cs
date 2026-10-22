using System.Text.RegularExpressions;
using MajdataPlay.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Rendering
{
    /// <summary>Renders offscreen cameras through temporary Maj Render Pipeline configurations and checks the rendered pixels.</summary>
    /// <remarks>
    /// Main-screen cameras are not rendered here: presenting to the backbuffer of a batch-mode Editor crashes the Vulkan driver
    /// ("Missing Vulkan framebuffer attachment image?"). Play Mode coverage of the scaled main-screen path lives in <see cref="MajRenderPipelinePlayModeTests"/>.
    /// </remarks>
    public class MajCameraRendererTests
    {
        /// <summary>The background color used by every test camera.</summary>
        private static readonly Color Background = new Color(0.8f, 0.2f, 0.4f, 1f);
        /// <summary>Tracks the objects created by the current test.</summary>
        private TestObjects _objects = null!;
        /// <summary>Isolates the static capture and extension registrations.</summary>
        private RenderRegistrationsSnapshot _registrations = null!;

        /// <summary>Saves static registrations and creates the object tracker.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            _registrations = new RenderRegistrationsSnapshot();
        }

        /// <summary>Restores static registrations and destroys created objects.</summary>
        [TearDown]
        public void TearDown()
        {
            _registrations.Dispose();
            _objects.Dispose();
        }

        /// <summary>Creates an orthographic camera that clears to the test background and renders into a texture.</summary>
        /// <param name="target">The explicit render target.</param>
        /// <returns>The camera.</returns>
        private Camera CreateCamera(RenderTexture target)
        {
            var camera = _objects.Add<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Background;
            camera.targetTexture = target;
            camera.enabled = false;
            return camera;
        }

        /// <summary>Creates a tracked 64x64 render texture with depth.</summary>
        /// <returns>The created texture.</returns>
        private RenderTexture CreateTarget()
        {
            var texture = _objects.Track(new RenderTexture(64, 64, 24));
            texture.Create();
            return texture;
        }

        /// <summary>Asserts that a color matches the background within readback precision.</summary>
        /// <param name="color">The color read back.</param>
        private static void AssertBackground(Color color)
        {
            Assert.That(color.r, Is.EqualTo(Background.r).Within(0.02f));
            Assert.That(color.g, Is.EqualTo(Background.g).Within(0.02f));
            Assert.That(color.b, Is.EqualTo(Background.b).Within(0.02f));
        }

        /// <summary>Checks that an offscreen camera renders the compatible path into its target texture.</summary>
        [Test]
        public void Offscreen_RendersIntoTargetTexture()
        {
            using var scope = new PipelineScope(MajMSAAMode.MSAA4x, 0.5f);
            var target = CreateTarget();
            var camera = CreateCamera(target);

            camera.Render();

            AssertBackground(PipelineScope.ReadCenter(target));
        }

        /// <summary>Checks that preview cameras also emit their geometry and render into their target.</summary>
        [Test]
        public void PreviewCamera_RendersIntoTargetTexture()
        {
            using var scope = new PipelineScope();
            var target = CreateTarget();
            var camera = CreateCamera(target);
            camera.cameraType = CameraType.Preview;

            camera.Render();

            AssertBackground(PipelineScope.ReadCenter(target));
        }

        /// <summary>Checks that cameras that cannot provide a capture target skip capture with one warning.</summary>
        [Test]
        public void Capture_UnsupportedCamera_IsSkipped()
        {
            using var scope = new PipelineScope();
            var offscreen = CreateCamera(CreateTarget());
            var calls = 0;
            MajCameraCaptureBridge.AddCaptureAction(offscreen, (_, _) => calls++);
            LogAssert.Expect(LogType.Warning, new Regex("camera capture requires"));

            offscreen.Render();
            offscreen.Render();

            Assert.That(calls, Is.EqualTo(0));
        }

        /// <summary>Checks that an extension that declines the camera is not drawn.</summary>
        [Test]
        public void Extension_Declined_IsNotDrawn()
        {
            using var scope = new PipelineScope();
            var extension = new RecordingRenderExtension(false) { Accept = false };
            MajCameraRenderExtensions.Register(() => extension);
            var target = CreateTarget();
            var camera = CreateCamera(target);

            camera.Render();

            Assert.That(extension.Prepared, Is.EqualTo(1));
            Assert.That(extension.Rendered, Is.EqualTo(0));
            AssertBackground(PipelineScope.ReadCenter(target));
        }

        /// <summary>Checks that an offscreen camera still calls an accepting extension on the compatible path.</summary>
        [Test]
        public void Extension_Offscreen_IsDrawnWithoutAttachments()
        {
            using var scope = new PipelineScope();
            var extension = new RecordingRenderExtension(false);
            MajCameraRenderExtensions.Register(() => extension);
            var target = CreateTarget();
            var camera = CreateCamera(target);

            camera.Render();

            Assert.That(extension.Prepared, Is.EqualTo(1));
            Assert.That(extension.Rendered, Is.EqualTo(1));
            Assert.That(extension.Ended, Is.EqualTo(1));
            AssertBackground(PipelineScope.ReadCenter(target));
        }
    }
}
