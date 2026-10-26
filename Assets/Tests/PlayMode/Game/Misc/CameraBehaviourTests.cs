using System;
using System.Collections;
using MajdataPlay.Behaviours.UI;
using MajdataPlay.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.PlayMode
{
    /// <summary>Covers the camera related behaviours in Play Mode, restoring the shared main camera and display settings.</summary>
    public class CameraBehaviourTests
    {
        /// <summary>The objects created by the test.</summary>
        private TestObjects _objects = null!;

        /// <summary>The main camera registered before the test.</summary>
        private Camera? _savedMainCamera;

        /// <summary>The display transform flag before the test.</summary>
        private bool _savedTransform;

        /// <summary>The main screen offset before the test.</summary>
        private float _savedOffset;

        /// <summary>The main screen scale before the test.</summary>
        private float _savedScale;

        /// <summary>Captures the shared camera and display settings.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            _savedMainCamera = SceneSwitcher.MainCamera;
            var display = MajEnv.Settings.Display;
            _savedTransform = display.MainScreenTransform;
            _savedOffset = display.MainScreenOffset;
            _savedScale = display.MainScreenScale;
        }

        /// <summary>Restores the shared camera, the singleton slot and the display settings.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
            Majdata<GlobalCamera>.Free();
            SceneSwitcher.MainCamera = _savedMainCamera!;
            var display = MajEnv.Settings.Display;
            display.MainScreenTransform = _savedTransform;
            display.MainScreenOffset = _savedOffset;
            display.MainScreenScale = _savedScale;
        }

        /// <summary>Checks that the global camera registers itself as the main camera and survives scene loads.</summary>
        [Test]
        public void GlobalCamera_RegistersMainCamera()
        {
            var globalCamera = _objects.Add<GlobalCamera>();

            Assert.That(globalCamera.Camera, Is.SameAs(globalCamera.GetComponent<Camera>()));
            Assert.That(SceneSwitcher.MainCamera, Is.SameAs(globalCamera.Camera));
            Assert.That(Majdata<GlobalCamera>.Instance, Is.SameAs(globalCamera));
            Assert.That(Majdata<GlobalCamera>.IsSingleton, Is.True);
            Assert.That(globalCamera.gameObject.scene.name, Is.EqualTo("DontDestroyOnLoad"));
        }

        /// <summary>Checks that a preview-only camera removes itself in Play Mode.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PreviewOnlyCamera_DestroysItself()
        {
            var camera = _objects.Add<PreviewOnlyCamera>();
            var gameObject = camera.gameObject;

            yield return null;

            Assert.That(gameObject == null, Is.True);
        }

        /// <summary>Checks that a canvas follows the main camera on start and on every scene change until destroyed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CanvasConfigurator_FollowsMainCamera()
        {
            var first = _objects.Add<Camera>("First");
            var second = _objects.Add<Camera>("Second");
            SceneSwitcher.MainCamera = first;
            var configurator = _objects.Add<CanvasConfigurator>();
            var canvas = configurator.GetComponent<Canvas>();

            yield return null;
            Assert.That(canvas.worldCamera, Is.SameAs(first));

            SceneSwitcher.MainCamera = second;
            RaiseSceneChanged();
            Assert.That(canvas.worldCamera, Is.SameAs(second));

            UnityEngine.Object.DestroyImmediate(configurator.gameObject);
            Assert.DoesNotThrow(RaiseSceneChanged);
        }

        /// <summary>Raises the scene change event of the scene switcher.</summary>
        private static void RaiseSceneChanged()
        {
            var handler = PrivateMember.GetStaticField<EventHandler<(MajScenes NewScene, MajScenes OldScene)>?>(typeof(SceneSwitcher), nameof(SceneSwitcher.OnSceneChanged));
            handler?.Invoke(null, (MajScenes.List, MajScenes.Title));
        }

        /// <summary>Computes the orthographic size and vertical adjustment the camera mover derives from the screen size.</summary>
        /// <param name="baseSize">The camera's original orthographic size.</param>
        /// <returns>The screen orthographic size and the vertical adjustment.</returns>
        private static (float Size, float Adjustment) ExpectedScreenLayout(float baseSize)
        {
            float width = Screen.width;
            float height = Screen.height;
            var canvasScale = Mathf.Min(width / 1080f, height / 1920f);
            var size = baseSize * (height / 1920f) / canvasScale;
            return (size, size - baseSize);
        }

        /// <summary>Checks that the camera mover keeps the original camera while the display transform is disabled and moves and scales it when enabled.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ScreenPosCameraMover_AppliesDisplayTransform()
        {
            Assume.That(Screen.width > 0 && Screen.height > 0, "A screen size is required.");
            var display = MajEnv.Settings.Display;
            display.MainScreenTransform = false;
            var camera = _objects.Add<Camera>("Camera");
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.transform.position = new Vector3(0f, 2f, -10f);
            camera.rect = new Rect(0.1f, 0.1f, 0.5f, 0.5f);
            camera.gameObject.AddComponent<ScreenPosCameraMover>();
            var (size, adjustment) = ExpectedScreenLayout(5f);

            yield return null;
            Assert.That(camera.rect, Is.EqualTo(new Rect(0f, 0f, 1f, 1f)));
            Assert.That(camera.transform.position.y, Is.EqualTo(2f + adjustment).Within(1e-3f));
            Assert.That(camera.orthographicSize, Is.EqualTo(size).Within(1e-3f));

            display.MainScreenTransform = true;
            display.MainScreenOffset = 1f;
            display.MainScreenScale = 2f;
            yield return null;
            Assert.That(camera.transform.position.y, Is.EqualTo(1.5f + 2.7f + adjustment).Within(1e-3f));
            Assert.That(camera.orthographicSize, Is.EqualTo(size / 2f).Within(1e-3f));

            display.MainScreenOffset = 0f;
            yield return null;
            Assert.That(camera.transform.position.y, Is.EqualTo(1.5f + adjustment).Within(1e-3f));

            display.MainScreenTransform = false;
            yield return null;
            Assert.That(camera.transform.position.y, Is.EqualTo(2f + adjustment).Within(1e-3f));
            Assert.That(camera.orthographicSize, Is.EqualTo(size).Within(1e-3f));
        }

        /// <summary>Checks that a perspective camera only moves and keeps its size.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ScreenPosCameraMover_PerspectiveCameraOnlyMoves()
        {
            Assume.That(Screen.width > 0 && Screen.height > 0, "A screen size is required.");
            var display = MajEnv.Settings.Display;
            display.MainScreenTransform = true;
            display.MainScreenOffset = 0.5f;
            display.MainScreenScale = 0f;
            var camera = _objects.Add<Camera>("Camera");
            camera.orthographic = false;
            camera.gameObject.AddComponent<ScreenPosCameraMover>();

            yield return null;

            Assert.That(camera.transform.position.y, Is.EqualTo(1.5f + (0.5f * 2.7f)).Within(1e-3f));
            Assert.That(camera.transform.position.z, Is.EqualTo(-10f));
        }
    }
}
