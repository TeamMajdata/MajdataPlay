using System.Collections;
using MajdataPlay.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Rendering
{
    /// <summary>Renders cameras through temporary pipeline configurations in Play Mode, where resources are released with deferred destruction.</summary>
    public class MajRenderPipelinePlayModeTests
    {
        /// <summary>The background color used by every test camera.</summary>
        private static readonly Color Background = new Color(0.1f, 0.6f, 0.3f, 1f);
        /// <summary>Tracks the objects created by the current test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Creates the object tracker.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
        }

        /// <summary>Destroys created objects.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
        }

        /// <summary>Creates a disabled main-screen camera that clears to the test background.</summary>
        /// <returns>The camera.</returns>
        private Camera CreateCamera()
        {
            var camera = _objects.Add<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Background;
            camera.enabled = false;
            return camera;
        }

        /// <summary>Checks that changing scene scale between frames recreates the attachments and that capture sees the scene color.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ScaleChanges_RecreateAttachmentsAcrossFrames()
        {
            Assert.That(Application.isPlaying, Is.True);
            using var scope = new PipelineScope(MajMSAAMode.Disabled, 0.5f);
            var camera = CreateCamera();
            var captured = _objects.Track(new RenderTexture(16, 16, 0));
            captured.Create();
            void Capture(RenderTargetIdentifier source, CommandBuffer commands)
            {
                commands.Blit(source, captured);
            }

            camera.Render();
            Assert.That(MajRenderDiagnostics.OutputStatus, Is.EqualTo(MajRenderOutputStatus.IntermediateScaled));
            yield return null;

            scope.Asset.RenderScale = 0.75f;
            MajCameraCaptureBridge.AddCaptureAction(camera, Capture);
            try
            {
                camera.Render();
                Assert.That(MajRenderDiagnostics.OutputStatus, Is.EqualTo(MajRenderOutputStatus.IntermediateCapture));
                var color = PipelineScope.ReadCenter(captured);
                Assert.That(color.g, Is.EqualTo(Background.g).Within(0.02f));
            }
            finally
            {
                MajCameraCaptureBridge.RemoveCaptureAction(camera, Capture);
            }

            yield return null;
            scope.Asset.ResetRuntimeRenderingOverrides();
            camera.Render();
            Assert.That(MajRenderDiagnostics.OutputStatus, Is.EqualTo(MajRenderOutputStatus.IntermediateScaled));
            yield return null;
        }

        /// <summary>Checks that switching back to the project pipeline disposes the temporary pipeline while playing.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PipelineSwitch_DisposesTemporaryPipeline()
        {
            var camera = CreateCamera();
            var target = _objects.Track(new RenderTexture(32, 32, 24));
            camera.targetTexture = target;
            using (new PipelineScope(MajMSAAMode.MSAA2x, 0.5f))
            {
                camera.Render();
                yield return null;
            }

            camera.Render();
            yield return null;

            Assert.That(GraphicsSettings.currentRenderPipeline, Is.Not.Null);
            Assert.That(PipelineScope.ReadCenter(target).g, Is.EqualTo(Background.g).Within(0.02f));
        }
    }
}
