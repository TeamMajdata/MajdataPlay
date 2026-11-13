using System;
using System.Reflection;
using MajdataPlay.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

#nullable enable
namespace MajdataPlay.Tests.Game.Rendering
{
    /// <summary>Checks actual scene-to-blur sizing and the allocation-free MRP backdrop registration lifecycle.</summary>
    public sealed class UiPvBackdropMappingTests
    {
        /// <summary>Owns only cameras and source attachment objects created by this fixture.</summary>
        private TestObjects _objects = null!;

        /// <summary>Retains any prior integration-owned registration without resetting its resources.</summary>
        private IMajUiBackdropCapture? _savedCapture;

        /// <summary>Isolates only the backdrop bridge while retaining an existing integration's registration.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            _savedCapture = (IMajUiBackdropCapture?)typeof(MajUiBackdropBridge)
                .GetField("s_capture", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
            MajUiBackdropBridge.Reset();
        }

        /// <summary>Restores the previous registration and destroys fixture-owned attachments and cameras.</summary>
        [TearDown]
        public void TearDown()
        {
            MajUiBackdropBridge.Reset();
            if (_savedCapture != null)
            {
                MajUiBackdropBridge.Register(_savedCapture);
            }
            _objects?.Dispose();
        }

        /// <summary>Checks Gaussian captures round each source dimension upward to a quarter-size target.</summary>
        /// <param name="width">The actual resolved scene width.</param>
        /// <param name="height">The actual resolved scene height.</param>
        /// <param name="expectedWidth">The quarter-resolution Gaussian target width.</param>
        /// <param name="expectedHeight">The quarter-resolution Gaussian target height.</param>
        [TestCase(1, 1, 1, 1)]
        [TestCase(1080, 1920, 270, 480)]
        [TestCase(1081, 1921, 271, 481)]
        [TestCase(1920, 1080, 480, 270)]
        [TestCase(2048, 2732, 512, 683)]
        [TestCase(8192, 4096, 2048, 1024)]
        [TestCase(4096, 8192, 1024, 2048)]
        [TestCase(16384, 1024, 4096, 256)]
        public void GaussianResolutionSize_RoundsActualSceneDimensionsUpward(int width, int height, int expectedWidth, int expectedHeight)
        {
            Assert.That(GetGaussianResolutionSize(width, height), Is.EqualTo(new Vector2Int(expectedWidth, expectedHeight)));
        }

        /// <summary>Checks the filter uses the actual post-scale single-sample attachment instead of camera or decoder dimensions.</summary>
        /// <param name="sceneWidth">The source attachment width for one real render-scale case.</param>
        /// <param name="sceneHeight">The source attachment height for that case.</param>
        /// <param name="expectedWidth">The half-resolution liquid snapshot width read from that attachment.</param>
        /// <param name="expectedHeight">The half-resolution liquid snapshot height.</param>
        [TestCase(1, 1, 1, 1)]
        [TestCase(540, 960, 270, 480)]
        [TestCase(810, 1440, 405, 720)]
        [TestCase(1080, 1920, 540, 960)]
        [TestCase(1081, 1921, 541, 961)]
        [TestCase(1620, 2880, 810, 1440)]
        [TestCase(8192, 4096, 4096, 2048)]
        public void WorkingSize_FollowsPostRenderScaleSourceAttachment(int sceneWidth, int sceneHeight, int expectedWidth, int expectedHeight)
        {
            // GPU allocation is unnecessary: these are the real Texture dimensions passed to the production callback.
            var source = _objects.Track(new RenderTexture(sceneWidth, sceneHeight, 0)
            {
                antiAliasing = 1
            });
            var full = (Vector2Int)typeof(UiPvBackdropBlur).GetMethod("GetSourceSize", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { source.width, source.height })!;
            var half = (Vector2Int)typeof(UiPvBackdropBlur).GetMethod("GetHalfResolutionSize", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { source.width, source.height })!;
            Assert.That(full, Is.EqualTo(new Vector2Int(sceneWidth, sceneHeight)), "The source attachment extent must retain its actual scene resolution.");
            Assert.That(half, Is.EqualTo(new Vector2Int(expectedWidth, expectedHeight)), "Liquid category and button snapshots retain their half-resolution reconstruction grid.");
            Assert.That(GetGaussianResolutionSize(source.width, source.height),
                Is.EqualTo(new Vector2Int((sceneWidth + 3) / 4, (sceneHeight + 3) / 4)),
                "Gaussian captures must derive their quarter-size grid from the actual scene attachment.");
        }

        /// <summary>Checks both Gaussian axes share the fixed sigma-four, radius-eight kernel with normalized energy.</summary>
        [Test]
        public void GaussianKernel_UsesFourNormalizedBilinearPairs()
        {
            typeof(UiPvBackdropBlur).GetMethod("BuildPanelBlurKernel", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, null);
            var centerWeight = (float)typeof(UiPvBackdropBlur)
                .GetField("s_gaussianCenterWeight", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var pairs = (Vector4[])typeof(UiPvBackdropBlur)
                .GetField("s_gaussianKernelPairs", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            Assert.That(pairs.Length, Is.EqualTo(4));
            var sigma = 4.0;
            var radius = 8;
            var normalization = 1.0;
            for (var tap = 1; tap <= radius; tap++)
            {
                normalization += 2.0 * Math.Exp(-tap * tap / (2.0 * sigma * sigma));
            }
            Assert.That(centerWeight, Is.EqualTo(1.0 / normalization).Within(0.000001));
            var energy = (double)centerWeight;
            var moment = 0.0;
            for (var pair = 0; pair < pairs.Length; pair++)
            {
                var first = pair * 2 + 1;
                var firstWeight = Math.Exp(-first * first / (2.0 * sigma * sigma));
                var secondWeight = Math.Exp(-(first + 1) * (first + 1) / (2.0 * sigma * sigma));
                var weight = pairs[pair].x;
                var offset = pairs[pair].y;
                Assert.That(weight, Is.EqualTo((firstWeight + secondWeight) / normalization).Within(0.000001));
                Assert.That(offset, Is.EqualTo(first + secondWeight / (firstWeight + secondWeight)).Within(0.00001));
                Assert.That(pairs[pair].z, Is.Zero);
                Assert.That(pairs[pair].w, Is.Zero);
                var fraction = offset - first;
                energy += 2.0 * weight;
                moment += 2.0 * weight * ((1.0 - fraction) * first * first + fraction * (first + 1.0) * (first + 1.0));
            }
            Assert.That(energy, Is.EqualTo(1.0).Within(0.000001));
            TestContext.WriteLine($"QUARTER_GAUSSIAN_KERNEL sigma={sigma:F1} radius={radius} pairs={pairs.Length} energy={energy:F8} variance={moment / energy:F6}");
        }

        /// <summary>Checks the circle mapping follows the authored transform without applying the output viewport twice.</summary>
        /// <param name="rotation">The main display's local rotation in degrees.</param>
        /// <param name="scaleX">The signed horizontal scale, including mirrored layouts.</param>
        /// <param name="scaleY">The signed vertical scale independent of its horizontal peer.</param>
        [TestCase(0f, 1f, 1f)]
        [TestCase(33f, -1.5f, 0.75f)]
        [TestCase(-71f, 0.6f, -1.8f)]
        public void CircleUvMapping_MatchesTransformedViewportCoordinates(float rotation, float scaleX, float scaleY)
        {
            var camera = _objects.Add<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 8f;
            camera.targetTexture = _objects.Track(new RenderTexture(1200, 800, 0));
            camera.rect = new Rect(0.17f, 0.13f, 0.61f, 0.72f);
            camera.transform.SetPositionAndRotation(new Vector3(3f, -2f, -20f), Quaternion.Euler(0f, 0f, 13f));
            var display = _objects.Add<RectTransform>();
            display.pivot = new Vector2(0.3f, 0.65f);
            display.sizeDelta = new Vector2(8f, 6f);
            display.SetPositionAndRotation(new Vector3(-1.5f, 2f, 0f), Quaternion.Euler(0f, 0f, rotation));
            display.localScale = new Vector3(scaleX, scaleY, 1f);
            Assert.That(UiPvBackdropBlur.TryGetCircleUvMapping(camera, display, out var uvX, out var uvY), Is.True);
            Assert.That(uvX.w, Is.EqualTo(1f));
            foreach (var expected in new[] { Vector2.zero, Vector2.right, Vector2.up, new Vector2(-0.6f, 0.4f) })
            {
                var local = display.rect.center + Vector2.Scale(display.rect.size * 0.5f, expected);
                var projected = camera.WorldToViewportPoint(display.TransformPoint(local));
                var uv = new Vector3(projected.x, projected.y, 1f);
                Assert.That(Vector3.Dot(uv, new Vector3(uvX.x, uvX.y, uvX.z)), Is.EqualTo(expected.x).Within(0.00002f));
                Assert.That(Vector3.Dot(uv, new Vector3(uvY.x, uvY.y, uvY.z)), Is.EqualTo(expected.y).Within(0.00002f));
            }
            display.localScale = new Vector3(0f, scaleY, 1f);
            Assert.That(UiPvBackdropBlur.TryGetCircleUvMapping(camera, display, out uvX, out uvY), Is.False);
            Assert.That(uvX, Is.EqualTo(Vector4.zero));
            Assert.That(uvY, Is.EqualTo(Vector4.zero));
        }

        /// <summary>Checks null registration does not replace a live consumer and inactive cameras do not request a capture.</summary>
        [Test]
        public void Bridge_RejectsNullWithoutChangingTheLiveRegistration()
        {
            var camera = _objects.Add<Camera>();
            var otherCamera = _objects.Add<Camera>();
            var capture = new CameraCapture(camera);
            MajUiBackdropBridge.Register(capture);
            Assert.Throws<ArgumentNullException>(() => MajUiBackdropBridge.Register(null!));
            Assert.That(MajUiBackdropBridge.GetActive(camera), Is.SameAs(capture));
            Assert.That(MajUiBackdropBridge.GetActive(otherCamera), Is.Null);
            capture.Enabled = false;
            Assert.That(MajUiBackdropBridge.GetActive(camera), Is.Null);
            capture.Enabled = true;
            Assert.That(MajUiBackdropBridge.GetActive(camera), Is.SameAs(capture));
        }

        /// <summary>Checks a stale owner's unregister cannot remove its replacement while the matching owner can unregister normally.</summary>
        [Test]
        public void Bridge_ReplacementSurvivesAnUnmatchedUnregister()
        {
            var camera = _objects.Add<Camera>();
            var first = new CameraCapture(camera);
            var replacement = new CameraCapture(camera);
            MajUiBackdropBridge.Register(first);
            MajUiBackdropBridge.Register(replacement);
            MajUiBackdropBridge.Unregister(first);
            Assert.That(MajUiBackdropBridge.GetActive(camera), Is.SameAs(replacement));
            MajUiBackdropBridge.Unregister(replacement);
            Assert.That(MajUiBackdropBridge.GetActive(camera), Is.Null);
        }

        /// <summary>Checks a new session forgets the callback without recording or disposing integration-owned resources.</summary>
        [Test]
        public void Bridge_ResetForgetsRegistrationWithoutOwningTheConsumer()
        {
            var camera = _objects.Add<Camera>();
            var capture = new CameraCapture(camera);
            MajUiBackdropBridge.Register(capture);
            Assert.That(MajUiBackdropBridge.GetActive(camera), Is.SameAs(capture));
            MajUiBackdropBridge.Reset();
            Assert.That(MajUiBackdropBridge.GetActive(camera), Is.Null);
            Assert.That(capture.RecordCalls, Is.Zero);
            Assert.That(capture.DisposeCalls, Is.Zero);
            Assert.That(MajUiBackdropBridge.BackgroundQueueUpperBound, Is.EqualTo(2899));
        }

        /// <summary>Checks single-stage providers remain valid and optional button preparation never repeats the first-stage query.</summary>
        [Test]
        public void Bridge_OptionalButtonStagePreservesLegacyProvidersWithoutRepeatingPreparation()
        {
            var camera = _objects.Add<Camera>();
            var otherCamera = _objects.Add<Camera>();
            var single = new CameraCapture(camera);
            MajUiBackdropBridge.Register(single);
            var active = MajUiBackdropBridge.GetActive(camera);
            Assert.That(active, Is.SameAs(single));
            Assert.That(MajUiBackdropBridge.GetActiveButtons(active, camera), Is.Null);

            var buttons = new ButtonCapture(camera);
            MajUiBackdropBridge.Register(buttons);
            active = MajUiBackdropBridge.GetActive(camera);
            Assert.That(MajUiBackdropBridge.GetActiveButtons(active, camera), Is.SameAs(buttons));
            buttons.ButtonsEnabled = false;
            Assert.That(MajUiBackdropBridge.GetActiveButtons(active, camera), Is.Null);
            buttons.ButtonsEnabled = true;
            Assert.That(MajUiBackdropBridge.GetActiveButtons(active, otherCamera), Is.Null);
            Assert.That(MajUiBackdropBridge.GetActiveButtons(active, camera), Is.SameAs(buttons));
            Assert.That(buttons.ActiveChecks, Is.EqualTo(1), "The optional button query must not repeat general capture preparation.");
            Assert.That(MajUiBackdropBridge.BackgroundQueueUpperBound, Is.LessThan(MajUiBackdropBridge.CategoryQueue));
            Assert.That(MajUiBackdropBridge.CategoryQueue, Is.LessThan(MajUiBackdropBridge.ButtonBodyQueue));
        }
        /// <summary>Models an optional button-stage provider using the unchanged two-stage bridge contract.</summary>
        private class ButtonCapture : IMajUiButtonBackdropCapture
        {
            /// <summary>Identifies the only camera accepted by this provider.</summary>
            protected Camera Camera { get; }

            /// <summary>Gets or sets the independent button-stage requirement.</summary>
            internal bool ButtonsEnabled { get; set; } = true;

            /// <summary>Gets the number of general preparation calls.</summary>
            internal int ActiveChecks { get; private set; }

            /// <summary>Retains a camera without taking ownership of it.</summary>
            /// <param name="camera">The camera accepted by the fixture provider.</param>
            internal ButtonCapture(Camera camera)
            {
                Camera = camera;
            }

            /// <summary>Counts general preparation independently of optional-stage queries.</summary>
            /// <param name="camera">The renderer's current camera.</param>
            /// <returns>Whether this is the configured camera.</returns>
            public bool IsActive(Camera camera)
            {
                ActiveChecks++;
                return camera == Camera;
            }

            /// <summary>Reports only the legacy button requirement.</summary>
            /// <param name="camera">The renderer's current camera.</param>
            /// <returns>Whether buttons need a capture for this camera.</returns>
            public bool RequiresButtonCapture(Camera camera)
            {
                return camera == Camera && ButtonsEnabled;
            }

            /// <summary>Accepts a first-stage callback without submitting GPU commands.</summary>
            /// <param name="buffer">The renderer-owned command buffer.</param>
            /// <param name="camera">The renderer's current camera.</param>
            /// <param name="resolvedColor">The renderer-owned resolved source.</param>
            public void Record(CommandBuffer buffer, Camera camera, RenderTexture resolvedColor)
            {
            }

            /// <summary>Accepts a legacy button-stage callback without submitting GPU commands.</summary>
            /// <param name="buffer">The renderer-owned command buffer.</param>
            /// <param name="camera">The renderer's current camera.</param>
            /// <param name="resolvedColor">The renderer-owned resolved source.</param>
            public void RecordButtons(CommandBuffer buffer, Camera camera, RenderTexture resolvedColor)
            {
            }
        }

        /// <summary>Calls the production sizing helper for quarter-resolution Gaussian targets.</summary>
        /// <param name="width">The actual source attachment width.</param>
        /// <param name="height">The actual source attachment height.</param>
        /// <returns>The production filter's working dimensions.</returns>
        private static Vector2Int GetGaussianResolutionSize(int width, int height)
        {
            return (Vector2Int)typeof(UiPvBackdropBlur)
                .GetMethod("GetGaussianResolutionSize", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { width, height })!;
        }

        /// <summary>Models an integration-owned consumer that opts in only one real fixture camera.</summary>
        private sealed class CameraCapture : IMajUiBackdropCapture, IDisposable
        {
            /// <summary>Identifies the only camera allowed to request this consumer.</summary>
            private readonly Camera _camera;

            /// <summary>Gets or sets whether the consumer currently has visible content.</summary>
            internal bool Enabled { get; set; } = true;

            /// <summary>Gets the number of explicit record calls, which registration must not perform.</summary>
            internal int RecordCalls { get; private set; }

            /// <summary>Gets the number of disposal calls, which the bridge must not own.</summary>
            internal int DisposeCalls { get; private set; }

            /// <summary>Retains a fixture camera without taking ownership of it.</summary>
            /// <param name="camera">The camera that has visible backdrop consumers.</param>
            internal CameraCapture(Camera camera)
            {
                _camera = camera;
            }

            /// <summary>Prepares capture only for the configured camera while the consumer is enabled.</summary>
            /// <param name="camera">The camera currently being prepared by MRP.</param>
            /// <returns>True only for the enabled fixture camera.</returns>
            public bool IsActive(Camera camera)
            {
                return Enabled && camera == _camera;
            }

            /// <summary>Counts an explicit capture without submitting GPU commands.</summary>
            /// <param name="buffer">The renderer-owned ordered command buffer.</param>
            /// <param name="camera">The camera whose background completed drawing.</param>
            /// <param name="resolvedColor">The renderer-owned single-sample background attachment.</param>
            public void Record(CommandBuffer buffer, Camera camera, RenderTexture resolvedColor)
            {
                RecordCalls++;
            }

            /// <summary>Counts disposal so the fixture can detect accidental ownership transfer.</summary>
            public void Dispose()
            {
                DisposeCalls++;
            }
        }
    }
}
