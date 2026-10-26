using System.Collections;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Calibrator;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.CalibratorScene
{
    /// <summary>Covers the audio latency calibrator: offset measurement, averaging and background music handling.</summary>
    public class CalibratorManagerTests : SceneTestBase
    {
        /// <summary>The temporary assets directory without calibration audio.</summary>
        private TemporaryDirectory _assets = null!;

        /// <summary>The manager under test.</summary>
        private CalibratorManager _manager = null!;

        /// <summary>The fake calibration track.</summary>
        private FakeAudioSample _track = null!;

        /// <summary>Creates the manager with an assets directory that lacks the calibration audio.</summary>
        [SetUp]
        public void SetUp()
        {
            _assets = new TemporaryDirectory();
            var assets = _assets;
            AddCleanup(() => assets.Dispose());
            PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.AssetsDirectory), _assets.Directory);
            CreateAudioManager("bgm_select.mp3");
            CreateSceneSwitcherStub();
            _manager = AddInactive<CalibratorManager>();
            foreach (var field in new[] { "_avgOffset", "_offset1", "_offset2", "_offset3", "_offset4" })
            {
                var text = CreateText(field);
                text.text = "stale";
                PrivateMember.SetField(_manager, field, text);
            }
            Activate(_manager);
            _track = new FakeAudioSample("calibrate.ogg", System.TimeSpan.FromSeconds(9)) { KeepsPlaying = true };
        }

        /// <summary>Gets one of the manager's labels.</summary>
        /// <param name="field">The label field.</param>
        /// <returns>The label text.</returns>
        private string Label(string field)
        {
            return PrivateMember.GetField<TextMeshProUGUI>(_manager, field).text;
        }

        /// <summary>Sends an A4 press or release to the manager.</summary>
        /// <param name="isDown">Whether the event is a press.</param>
        private void SendA4(bool isDown)
        {
            var args = new InputEventArgs
            {
                BZone = ButtonZone.A4,
                IsButton = true,
                OldStatus = isDown ? SwitchStatus.Off : SwitchStatus.On,
                Status = isDown ? SwitchStatus.On : SwitchStatus.Off,
            };
            PrivateMember.Invoke(_manager, "OnCalibrationAreaDown", null, args);
        }

        /// <summary>Marks the calibration audio as loaded.</summary>
        private void MakeAudioReady()
        {
            PrivateMember.SetField(_manager, "_calibrationAudio", _track);
            PrivateMember.SetField(_manager, "_isAudioReady", true);
        }

        /// <summary>Checks that Awake clears every result label and missing audio keeps calibration unavailable.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Awake_ResetsLabelsAndMissingAudioIsNotReady()
        {
            yield return null;

            Assert.That(Label("_offset1"), Is.EqualTo("--"));
            Assert.That(Label("_offset4"), Is.EqualTo("--"));
            Assert.That(Label("_avgOffset"), Is.Empty);
            Assert.That(PrivateMember.GetField<bool>(_manager, "_isAudioReady"), Is.False);
        }

        /// <summary>Checks that a press before the audio is ready only queues the start, and releases are ignored.</summary>
        [Test]
        public void PressBeforeAudioReady_QueuesStart()
        {
            SendA4(false);
            Assert.That(PrivateMember.GetField<bool>(_manager, "_isStartPending"), Is.False);

            SendA4(true);

            Assert.That(PrivateMember.GetField<bool>(_manager, "_isStartPending"), Is.True);
            Assert.That(PrivateMember.GetField<bool>(_manager, "_isCalibrating"), Is.False);
        }

        /// <summary>Checks that four hits are measured against the beats, averaged, and the list music is paused then resumed.</summary>
        [Test]
        public void FourHits_MeasureAndAverageOffsets()
        {
            var music = Sfx("bgm_select.mp3");
            music.Playing = true;
            MakeAudioReady();

            SendA4(true);
            Assert.That(PrivateMember.GetField<bool>(_manager, "_isCalibrating"), Is.True);
            Assert.That(_track.PlayOneShotCount, Is.EqualTo(1));
            Assert.That(music.PauseCount, Is.EqualTo(1));

            Clock.AdvanceMilliseconds(400);
            SendA4(true);
            Assert.That(Label("_offset1"), Is.EqualTo("--"));

            Clock.AdvanceMilliseconds(620);
            SendA4(true);
            Clock.AdvanceMilliseconds(1970);
            SendA4(true);
            Clock.AdvanceMilliseconds(2010);
            SendA4(true);
            Clock.AdvanceMilliseconds(2010);
            SendA4(true);

            Assert.That(Label("_offset1"), Is.EqualTo("+20"));
            Assert.That(Label("_offset2"), Is.EqualTo("-10"));
            Assert.That(Label("_offset3"), Is.EqualTo("+0"));
            Assert.That(Label("_offset4"), Is.EqualTo("+10"));
            Assert.That(Label("_avgOffset"), Is.EqualTo("平均延迟: +5ms"));
            Assert.That(PrivateMember.GetField<bool>(_manager, "_isCalibrating"), Is.False);
            Assert.That(music.PlayCount, Is.EqualTo(1));
        }

        /// <summary>Checks that calibration stops when the track ends, without resuming music that was not paused.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TrackEnd_StopsCalibration()
        {
            MakeAudioReady();
            SendA4(true);

            _track.Playing = false;
            yield return null;

            Assert.That(PrivateMember.GetField<bool>(_manager, "_isCalibrating"), Is.False);
            Assert.That(Sfx("bgm_select.mp3").PlayCount, Is.EqualTo(0));
        }

        /// <summary>Checks that the exit button stops the track and leaves for the setting scene.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ExitButton_StopsAndLeaves()
        {
            MakeAudioReady();
            Sfx("bgm_select.mp3").Playing = true;
            SendA4(true);
            ExpectSceneSwitch();

            InputSimulator.StepUntilButtonClickCompleted(ButtonZone.A5);
            yield return null;
            yield return null;

            Assert.That(PrivateMember.GetField<bool>(_manager, "_isExited"), Is.True);
            Assert.That(_track.StopCount, Is.EqualTo(1));
            Assert.That(Sfx("bgm_select.mp3").PlayCount, Is.EqualTo(1));
        }

        /// <summary>Checks that destroying the manager releases the calibration track.</summary>
        [Test]
        public void Destroy_DisposesTrack()
        {
            MakeAudioReady();

            Object.DestroyImmediate(_manager.gameObject);

            Assert.That(_track.IsDisposed, Is.True);
        }

        /// <summary>Checks the signed millisecond format.</summary>
        [Test]
        public void FormatSignedInteger_RoundsAwayFromZero()
        {
            Assert.That(PrivateMember.InvokeStatic<string>(typeof(CalibratorManager), "FormatSignedInteger", 0d), Is.EqualTo("+0"));
            Assert.That(PrivateMember.InvokeStatic<string>(typeof(CalibratorManager), "FormatSignedInteger", -2.5d), Is.EqualTo("-3"));
            Assert.That(PrivateMember.InvokeStatic<string>(typeof(CalibratorManager), "FormatSignedInteger", 2.4d), Is.EqualTo("+2"));
        }
    }
}
