using System.Collections;
using MajdataPlay.Recording;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Settings;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.GamePlay.PlayMode.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.PlayMode
{
    /// <summary>Checks the recorder status icon of <see cref="RecorderStatusDisplayer"/> in Play Mode.</summary>
    public class RecorderStatusDisplayerPlayModeTests
    {
        /// <summary>Tracks objects created by a test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Restores the settings.</summary>
        private GamePlaySettingsScope _settings = null!;

        /// <summary>Restores the registered displayer.</summary>
        private SingletonScope<RecorderStatusDisplayer> _slot = null!;

        /// <summary>The recorder installed in <see cref="Recorder"/> before the test.</summary>
        private object? _previousRecorder;

        /// <summary>The not-ready, ready and recording sprites.</summary>
        private Sprite[] _sprites = null!;

        /// <summary>Installs default settings and creates the status sprites.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            _settings = new GamePlaySettingsScope();
            _slot = new SingletonScope<RecorderStatusDisplayer>();
            _previousRecorder = PrivateMember.GetStaticField<object?>(typeof(Recorder), "s_recorder");
            var texture = _objects.Track(new Texture2D(3, 1));
            _sprites = new Sprite[3];
            for (var i = 0; i < _sprites.Length; i++)
            {
                _sprites[i] = _objects.Track(Sprite.Create(texture, new Rect(i, 0, 1, 1), Vector2.zero));
            }
        }

        /// <summary>Restores the recorder, the registration and the settings.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
            PrivateMember.SetStaticField(typeof(Recorder), "s_recorder", _previousRecorder);
            _slot.Dispose();
            _settings.Dispose();
        }

        /// <summary>Checks that the icon is hidden and never updated when recording is disabled.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Awake_HidesTheIconWhenRecordingIsDisabled()
        {
#if UNITY_STANDALONE
            _settings.Settings.Game.RecordMode = RecordModeOption.Disable;
#endif

            var displayer = CreateDisplayer(out var renderer);
            yield return null;
            displayer.OnLateUpdate();

            Assert.That(Majdata<RecorderStatusDisplayer>.Instance, Is.SameAs(displayer));
            Assert.That(displayer.Active, Is.False);
            Assert.That(displayer.gameObject.layer, Is.EqualTo(MajEnv.HIDDEN_LAYER));
            Assert.That(renderer.sprite, Is.Null);
        }

#if UNITY_STANDALONE
        /// <summary>Checks the icon for a disconnected, connected and recording OBS recorder.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnLateUpdate_ShowsTheRecorderState()
        {
            _settings.Settings.Game.RecordMode = RecordModeOption.OBSTrigger;
            var recorder = new GamePlayFakeRecorder();
            PrivateMember.SetStaticField(typeof(Recorder), "s_recorder", recorder);

            var displayer = CreateDisplayer(out var renderer);
            yield return null;

            displayer.OnLateUpdate();
            Assert.That(renderer.sprite, Is.SameAs(_sprites[0]), "Not ready");

            recorder.IsConnected = true;
            displayer.OnLateUpdate();
            Assert.That(renderer.sprite, Is.SameAs(_sprites[1]), "Ready");

            recorder.IsRecording = true;
            displayer.OnLateUpdate();
            Assert.That(renderer.sprite, Is.SameAs(_sprites[2]), "Recording");
            Assert.That(displayer.gameObject.layer, Is.EqualTo(MajEnv.DEFAULT_LAYER));
        }
#endif

        /// <summary>Checks that destroying the displayer unregisters it.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnDestroy_UnregistersTheDisplayer()
        {
            var displayer = CreateDisplayer(out _);
            yield return null;

            Object.Destroy(displayer.gameObject);
            yield return null;

            Assert.That(Majdata<RecorderStatusDisplayer>.Instance, Is.Null);
        }

        /// <summary>Creates an active displayer with a sprite renderer and its status sprites.</summary>
        /// <param name="renderer">Receives the sprite renderer.</param>
        /// <returns>The displayer.</returns>
        private RecorderStatusDisplayer CreateDisplayer(out SpriteRenderer renderer)
        {
            var root = _objects.Create("RecorderStatus");
            root.SetActive(false);
            renderer = root.AddComponent<SpriteRenderer>();
            var displayer = root.AddComponent<RecorderStatusDisplayer>();
            PrivateMember.SetField(displayer, "_notReady", _sprites[0]);
            PrivateMember.SetField(displayer, "_ready", _sprites[1]);
            PrivateMember.SetField(displayer, "_recording", _sprites[2]);
            root.SetActive(true);
            return displayer;
        }
    }
}
