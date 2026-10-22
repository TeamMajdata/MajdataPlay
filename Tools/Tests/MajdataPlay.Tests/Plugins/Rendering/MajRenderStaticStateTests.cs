using System;
using System.Collections.Generic;
using MajdataPlay.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Rendering
{
    /// <summary>Verifies the static registries and diagnostics of the Maj Render Pipeline, restoring their state after each test.</summary>
    public class MajRenderStaticStateTests
    {
        /// <summary>Stores the capture registrations that existed before the test.</summary>
        private Dictionary<Camera, Action<RenderTargetIdentifier, CommandBuffer>[]> _savedActions = null!;
        /// <summary>Stores the extension factory that existed before the test.</summary>
        private object? _savedFactory;
        /// <summary>Stores the extension version that existed before the test.</summary>
        private int _savedVersion;
        /// <summary>Stores the diagnostics fields that existed before the test.</summary>
        private readonly Dictionary<string, object?> _savedDiagnostics = new Dictionary<string, object?>();
        /// <summary>Stores the diagnostics output status that existed before the test.</summary>
        private MajRenderOutputStatus _savedStatus;
        /// <summary>Tracks the objects created by the current test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Gets the live capture registration dictionary.</summary>
        private static Dictionary<Camera, Action<RenderTargetIdentifier, CommandBuffer>[]> Actions =>
            PrivateMember.GetStaticField<Dictionary<Camera, Action<RenderTargetIdentifier, CommandBuffer>[]>>(typeof(MajCameraCaptureBridge), "s_actions");

        /// <summary>Saves every static value the tests modify.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            _savedActions = new Dictionary<Camera, Action<RenderTargetIdentifier, CommandBuffer>[]>(Actions);
            _savedFactory = PrivateMember.GetStaticField<object?>(typeof(MajCameraRenderExtensions), "_factory");
            _savedVersion = MajCameraRenderExtensions.Version;
            foreach (var name in new[] { "_displayWidth", "_displayHeight", "_sceneWidth", "_sceneHeight", "_displayResolution", "_sceneResolution", "_frameId" })
            {
                _savedDiagnostics[name] = PrivateMember.GetStaticField<object?>(typeof(MajRenderDiagnostics), name);
            }

            _savedStatus = MajRenderDiagnostics.OutputStatus;
        }

        /// <summary>Restores every static value and destroys created objects.</summary>
        [TearDown]
        public void TearDown()
        {
            var actions = Actions;
            actions.Clear();
            foreach (var pair in _savedActions)
            {
                actions[pair.Key] = pair.Value;
            }

            PrivateMember.SetStaticField(typeof(MajCameraRenderExtensions), "_factory", _savedFactory);
            PrivateMember.SetStaticProperty(typeof(MajCameraRenderExtensions), nameof(MajCameraRenderExtensions.Version), _savedVersion);
            foreach (var pair in _savedDiagnostics)
            {
                PrivateMember.SetStaticField(typeof(MajRenderDiagnostics), pair.Key, pair.Value);
            }

            PrivateMember.SetStaticProperty(typeof(MajRenderDiagnostics), nameof(MajRenderDiagnostics.OutputStatus), _savedStatus);
            _objects.Dispose();
        }

        /// <summary>Checks argument validation of capture registration.</summary>
        [Test]
        public void AddCaptureAction_ValidatesArguments()
        {
            var camera = _objects.Add<Camera>();
            Action<RenderTargetIdentifier, CommandBuffer> action = (_, _) => { };

            Assert.Throws<ArgumentNullException>(() => MajCameraCaptureBridge.AddCaptureAction(null!, action));
            Assert.Throws<ArgumentNullException>(() => MajCameraCaptureBridge.AddCaptureAction(camera, null!));
            Object.DestroyImmediate(camera.gameObject);
            Assert.Throws<ArgumentNullException>(() => MajCameraCaptureBridge.AddCaptureAction(camera, action));
        }

        /// <summary>Checks that registrations are unique, ordered snapshots and that removal restores the empty snapshot.</summary>
        [Test]
        public void CaptureActions_AreUniqueOrderedSnapshots()
        {
            var camera = _objects.Add<Camera>();
            Action<RenderTargetIdentifier, CommandBuffer> first = (_, _) => { };
            Action<RenderTargetIdentifier, CommandBuffer> second = (_, _) => { };
            Action<RenderTargetIdentifier, CommandBuffer> third = (_, _) => { };

            Assert.That(MajCameraCaptureBridge.GetCaptureActions(camera), Is.SameAs(Array.Empty<Action<RenderTargetIdentifier, CommandBuffer>>()));
            MajCameraCaptureBridge.AddCaptureAction(camera, first);
            MajCameraCaptureBridge.AddCaptureAction(camera, second);
            MajCameraCaptureBridge.AddCaptureAction(camera, first);
            MajCameraCaptureBridge.AddCaptureAction(camera, third);
            var snapshot = MajCameraCaptureBridge.GetCaptureActions(camera);

            Assert.That(snapshot, Is.EqualTo(new[] { first, second, third }));
            Assert.That(MajCameraCaptureBridge.GetCaptureActions(camera), Is.SameAs(snapshot));

            MajCameraCaptureBridge.RemoveCaptureAction(camera, second);
            Assert.That(MajCameraCaptureBridge.GetCaptureActions(camera), Is.EqualTo(new[] { first, third }));
            Assert.That(snapshot, Is.EqualTo(new[] { first, second, third }));
            MajCameraCaptureBridge.RemoveCaptureAction(camera, second);
            MajCameraCaptureBridge.RemoveCaptureAction(camera, null!);
            MajCameraCaptureBridge.RemoveCaptureAction(null!, first);
            MajCameraCaptureBridge.RemoveCaptureAction(camera, first);
            MajCameraCaptureBridge.RemoveCaptureAction(camera, third);

            Assert.That(MajCameraCaptureBridge.GetCaptureActions(camera), Is.Empty);
            Assert.That(Actions.ContainsKey(camera), Is.False);
            MajCameraCaptureBridge.RemoveCaptureAction(camera, third);
        }

        /// <summary>Checks that a destroyed camera is hidden from lookups but can still be unregistered.</summary>
        [Test]
        public void CaptureActions_DestroyedCameraCanBeRemoved()
        {
            var camera = _objects.Add<Camera>();
            Action<RenderTargetIdentifier, CommandBuffer> action = (_, _) => { };
            MajCameraCaptureBridge.AddCaptureAction(camera, action);
            Object.DestroyImmediate(camera.gameObject);

            Assert.That(MajCameraCaptureBridge.GetCaptureActions(camera), Is.Empty);
            Assert.That(Actions.ContainsKey(camera), Is.True);
            MajCameraCaptureBridge.RemoveCaptureAction(camera, action);
            Assert.That(Actions.ContainsKey(camera), Is.False);
        }

        /// <summary>Checks that Reset forgets every registration.</summary>
        [Test]
        public void Reset_ClearsRegistrations()
        {
            var camera = _objects.Add<Camera>();
            MajCameraCaptureBridge.AddCaptureAction(camera, (_, _) => { });

            MajCameraCaptureBridge.Reset();

            Assert.That(Actions, Is.Empty);
        }

        /// <summary>Checks registration, versioning and creation of camera render extensions.</summary>
        [Test]
        public void Extensions_RegisterVersionAndCreate()
        {
            var created = 0;
            Func<IMajCameraRenderExtension> factory = () =>
            {
                created++;
                return new RecordingExtension();
            };
            Func<IMajCameraRenderExtension> other = () => new RecordingExtension();
            Assert.Throws<ArgumentNullException>(() => MajCameraRenderExtensions.Register(null!));

            var version = MajCameraRenderExtensions.Version;
            MajCameraRenderExtensions.Register(factory);
            Assert.That(MajCameraRenderExtensions.Version, Is.EqualTo(version + 1));
            MajCameraRenderExtensions.Register(factory);
            Assert.That(MajCameraRenderExtensions.Version, Is.EqualTo(version + 1));
            Assert.That(MajCameraRenderExtensions.Create(), Is.InstanceOf<RecordingExtension>());
            Assert.That(created, Is.EqualTo(1));

            MajCameraRenderExtensions.Unregister(other);
            Assert.That(MajCameraRenderExtensions.Version, Is.EqualTo(version + 1));
            MajCameraRenderExtensions.Unregister(factory);
            Assert.That(MajCameraRenderExtensions.Version, Is.EqualTo(version + 2));
            Assert.That(MajCameraRenderExtensions.Create(), Is.Null);
        }

        /// <summary>Checks the frame, camera and publication lifecycle of the output diagnostics.</summary>
        [Test]
        public void Diagnostics_TrackFrameCameraAndPublication()
        {
            MajRenderDiagnostics.Invalidate();
            Assert.That(MajRenderDiagnostics.OutputStatus, Is.EqualTo(MajRenderOutputStatus.Unavailable));
            Assert.That(MajRenderDiagnostics.DisplayResolution, Is.EqualTo("--"));

            MajRenderDiagnostics.BeginFrame(12345);
            MajRenderDiagnostics.BeginCamera();
            Assert.That(MajRenderDiagnostics.OutputStatus, Is.EqualTo(MajRenderOutputStatus.Pending));
            MajRenderDiagnostics.PublishDimensions(1920, 1080, 960, 540);
            Assert.That(MajRenderDiagnostics.SceneResolution, Is.EqualTo("--"));
            MajRenderDiagnostics.Publish(MajRenderOutputStatus.IntermediateScaled);

            Assert.That(MajRenderDiagnostics.OutputStatus, Is.EqualTo(MajRenderOutputStatus.IntermediateScaled));
            Assert.That(MajRenderDiagnostics.DisplayResolution, Is.EqualTo("1920 x 1080"));
            Assert.That(MajRenderDiagnostics.SceneResolution, Is.EqualTo("960 x 540"));

            MajRenderDiagnostics.BeginFrame(12345);
            Assert.That(MajRenderDiagnostics.OutputStatus, Is.EqualTo(MajRenderOutputStatus.IntermediateScaled));
            MajRenderDiagnostics.PublishDimensions(1920, 1080, 960, 540);
            Assert.That(MajRenderDiagnostics.DisplayResolution, Is.EqualTo("1920 x 1080"));
            MajRenderDiagnostics.BeginFrame(12346);
            Assert.That(MajRenderDiagnostics.OutputStatus, Is.EqualTo(MajRenderOutputStatus.Unavailable));
            Assert.That(MajRenderDiagnostics.SceneResolution, Is.EqualTo("--"));

            MajRenderDiagnostics.Publish(MajRenderOutputStatus.Unavailable);
            MajRenderDiagnostics.Publish(MajRenderOutputStatus.Direct);
            Assert.That(MajRenderDiagnostics.DisplayResolution, Is.EqualTo("0 x 0").Or.EqualTo("--"));
        }

        /// <summary>Records the calls made by the pipeline to a camera render extension.</summary>
        private sealed class RecordingExtension : IMajCameraRenderExtension
        {
            /// <summary>Gets a value indicating whether the extension requires an intermediate target.</summary>
            public bool RequiresIntermediateTarget => false;

            /// <summary>Accepts every camera.</summary>
            /// <param name="camera">The camera about to render.</param>
            /// <returns>Always true.</returns>
            public bool Prepare(Camera camera)
            {
                return true;
            }

            /// <summary>Draws nothing.</summary>
            /// <param name="buffer">The pipeline command buffer.</param>
            /// <param name="camera">The rendering camera.</param>
            /// <param name="colorTarget">The optional color target.</param>
            /// <param name="depthTarget">The optional depth target.</param>
            /// <param name="width">The target width.</param>
            /// <param name="height">The target height.</param>
            public void Render(CommandBuffer buffer, Camera camera, RenderTexture? colorTarget, RenderTexture? depthTarget, int width, int height)
            {
            }

            /// <summary>Does nothing after the camera.</summary>
            public void EndCamera()
            {
            }

            /// <summary>Does nothing on disposal.</summary>
            public void Dispose()
            {
            }
        }
    }
}
