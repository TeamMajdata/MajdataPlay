using System;
using System.Collections;
using System.Threading.Tasks;
using MajdataPlay.FFmpeg;
using MajdataPlay.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Global.PlayMode
{
    /// <summary>
    /// Verifies the global debug renderers, background video controller and frame updater in Play Mode, where Unity runs
    /// their Awake, Start, Update and LateUpdate messages.
    /// </summary>
    public class GlobalComponentsPlayModeTests
    {
        /// <summary>The created objects.</summary>
        private TestObjects _objects = null!;

        /// <summary>The installed default settings.</summary>
        private PlayModeSettingsScope _settings = null!;

        /// <summary>Creates the object tracker and settings.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            _settings = new PlayModeSettingsScope();
        }

        /// <summary>Destroys the objects, releases the singletons and restores the settings.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
            FreeSingleton<DummyLedRenderer>();
            FreeSingleton<DummyTouchPanelRenderer>();
            FreeSingleton<BackgroundVideoController>();
            FreeSingleton<GameUpdater>();
            _settings.Dispose();
        }

        /// <summary>Checks that the LED dummy renderer colours its child sprites with the published ring colours.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DummyLedRenderer_ColoursChildSprites()
        {
            var renderer = CreateLedRenderer(8);
            var colors = new Color[8];
            for (var i = 0; i < colors.Length; i++)
            {
                colors[i] = new Color(i / 8f, 1f - (i / 8f), 0.5f, 1f);
            }

            DummyLedRenderer.SetLedRingColorData(colors);
            renderer.OnLateUpdate();
            yield return null;

            Assert.That(MajdataPlay.Majdata<DummyLedRenderer>.Instance, Is.SameAs(renderer));
            var sprites = renderer.GetComponentsInChildren<SpriteRenderer>();
            for (var i = 0; i < sprites.Length; i++)
            {
                Assert.That(sprites[i].color, Is.EqualTo(colors[i]), $"LED {i}");
            }
        }

        /// <summary>Checks that the touch panel dummy renderer maps colliders, starts hidden and colours sensors when enabled.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DummyTouchPanelRenderer_ShowsSensorStates()
        {
            var renderer = CreateTouchPanelRenderer(34);
            var raw = PrivateMember.GetStaticField<Memory<bool>>(typeof(InputManager), "_sensorStates");
            var saved = raw.ToArray();
            try
            {
                yield return null;
                var renderers = renderer.GetComponentsInChildren<MeshRenderer>(true);
                Assert.That(renderer.InstanceID2SensorIndexMappingTable, Has.Count.EqualTo(34));
                Assert.That(renderers[0].enabled, Is.False, "sensors start hidden");
                Assert.That(renderers[0].sortingOrder, Is.EqualTo(short.MaxValue));

                _settings.Settings.Debug.DisplaySensor = true;
                _settings.Settings.Debug.SensorOpacity = 30;
                raw.Span.Clear();
                raw.Span[3] = true;
                renderer.OnPreUpdate();
                renderer.OnPreUpdate();

                Assert.That(renderers[0].enabled, Is.True);
                Assert.That(ReadColor(renderers[3]), Is.EqualTo(new Color(0f, 1f, 0f, 0.3f)));
                Assert.That(ReadColor(renderers[0]), Is.EqualTo(new Color(1f, 0f, 0f, 0.3f)));

                _settings.Settings.Debug.DisplaySensor = false;
                renderer.OnPreUpdate();
                Assert.That(renderers[3].enabled, Is.False);
            }
            finally
            {
                saved.CopyTo(raw);
            }
        }

        /// <summary>Checks that hiding and showing the background video moves its renderer between layers.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator BackgroundVideo_HideAndShowSwitchLayers()
        {
            var root = InactiveObjects.Create(_objects, "BackgroundVideo");
            var player = root.AddComponent<FFmpegVideoPlayer>();
            var spriteObject = _objects.Create("BackgroundSprite", root.transform);
            var sprite = spriteObject.AddComponent<SpriteRenderer>();
            var controller = root.AddComponent<BackgroundVideoController>();
            PrivateMember.SetField(controller, "_videoPlayer", player);
            PrivateMember.SetField(controller, "_videoRenderer", sprite);
            root.SetActive(true);
            yield return null;

            controller.Hide();
            Assert.That(spriteObject.layer, Is.EqualTo(MajEnv.HIDDEN_LAYER));
            controller.Pause();
            controller.Stop();
            var error = Assert.Throws<InvalidOperationException>(() => controller.Show(), "no video source is configured");
            Assert.That(error.Message, Does.Contain("Url"));
            Assert.That(spriteObject.layer, Is.EqualTo(MajEnv.DEFAULT_LAYER));
            Assert.Throws<InvalidOperationException>(() => controller.Play());
            Assert.That(MajdataPlay.Majdata<BackgroundVideoController>.Instance, Is.SameAs(controller));
        }

        /// <summary>Checks that the updater drives the timeline, the debug renderers and the LED output every frame.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GameUpdater_DrivesTheFrame()
        {
            var led = CreateLedRenderer(8);
            CreateTouchPanelRenderer(0);
            var root = InactiveObjects.Create(_objects, "GameUpdater");
            var updater = root.AddComponent<GameUpdater>();
            PrivateMember.SetField(updater, "_onlineHeartbeatTask", new TaskCompletionSource<bool>().Task);
            root.SetActive(true);
            var frames = MajTimeline.FrameCount;
            var scene = SceneSwitcher.CurrentScene;
            try
            {
                yield return null;
                yield return null;

                Assert.That(MajTimeline.FrameCount, Is.GreaterThan(frames));
                Assert.That(PrivateMember.GetField<DummyLedRenderer>(updater, "_dummyLedRenderer"), Is.SameAs(led));
                var ringColors = PrivateMember.GetStaticField<Color[]>(typeof(DummyLedRenderer), "_ledRingColors");
                Assert.That(led.GetComponentsInChildren<SpriteRenderer>()[0].color, Is.EqualTo(ringColors[0]));

                PrivateMember.SetStaticProperty(typeof(SceneSwitcher), nameof(SceneSwitcher.CurrentScene), MajScenes.Game);
                var heartbeatCts = PrivateMember.GetField<System.Threading.CancellationTokenSource>(updater, "_heartbeatCts");
                yield return null;
                yield return null;

                Assert.That(heartbeatCts.IsCancellationRequested, Is.True, "entering the game cancels a running heartbeat");
                Assert.That(PrivateMember.GetField<MajScenes>(updater, "_currentScene"), Is.EqualTo(MajScenes.Game));
            }
            finally
            {
                PrivateMember.SetStaticProperty(typeof(SceneSwitcher), nameof(SceneSwitcher.CurrentScene), scene);
            }
        }

        /// <summary>Creates an LED dummy renderer with sprite children and runs its Awake.</summary>
        /// <param name="lights">The number of child sprites.</param>
        /// <returns>The renderer.</returns>
        private DummyLedRenderer CreateLedRenderer(int lights)
        {
            var root = InactiveObjects.Create(_objects, "DummyLed");
            for (var i = 0; i < lights; i++)
            {
                _objects.Create($"Led{i}", root.transform).AddComponent<SpriteRenderer>();
            }
            var renderer = root.AddComponent<DummyLedRenderer>();
            root.SetActive(true);
            return renderer;
        }

        /// <summary>Creates a touch panel dummy renderer with sensor children and runs its Awake.</summary>
        /// <param name="sensors">The number of sensor children.</param>
        /// <returns>The renderer.</returns>
        private DummyTouchPanelRenderer CreateTouchPanelRenderer(int sensors)
        {
            var root = InactiveObjects.Create(_objects, "DummyTouchPanel");
            for (var i = 0; i < sensors; i++)
            {
                var child = _objects.Create($"Sensor{i}", root.transform);
                child.AddComponent<MeshFilter>();
                child.AddComponent<MeshRenderer>();
                child.AddComponent<MeshCollider>();
            }
            var renderer = root.AddComponent<DummyTouchPanelRenderer>();
            PrivateMember.SetField(renderer, "_sharedInstancedMaterial", _objects.Track(new Material(Shader.Find("Sprites/Default"))));
            root.SetActive(true);
            return renderer;
        }

        /// <summary>Reads the sensor colour written to a renderer's property block.</summary>
        /// <param name="renderer">The sensor renderer.</param>
        /// <returns>The colour.</returns>
        private static Color ReadColor(MeshRenderer renderer)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            return block.GetColor("_BaseColor");
        }

        /// <summary>Releases a singleton registered by a component under test.</summary>
        /// <typeparam name="T">The singleton type.</typeparam>
        private static void FreeSingleton<T>() where T : class
        {
            if (MajdataPlay.Majdata<T>.IsSingleton)
            {
                MajdataPlay.Majdata<T>.Free();
            }
        }
    }
}
