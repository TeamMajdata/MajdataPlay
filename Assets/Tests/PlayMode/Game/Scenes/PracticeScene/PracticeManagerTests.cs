using System;
using System.Collections;
using System.Text.RegularExpressions;
using MajdataPlay.IO;
using MajdataPlay.Numerics;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Practice;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using MajSimai;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.PracticeScene
{
    /// <summary>Covers the practice range editor: loading, range and speed editing, looping and leaving.</summary>
    public class PracticeManagerTests : SceneTestBase
    {
        /// <summary>The manager under test.</summary>
        private PracticeManager _manager = null!;

        /// <summary>The fake song track.</summary>
        private FakeAudioSample _track = null!;

        /// <summary>The practiced song.</summary>
        private FakeSongDetail _song = null!;

        /// <summary>Creates the practice scene for a Master chart.</summary>
        [SetUp]
        public void SetUp()
        {
            CreateAudioManager("bgm_select.mp3");
            CreateSceneSwitcherStub();
            var canvas = CreateCanvas();
            _track = new FakeAudioSample("track", TimeSpan.FromSeconds(60));
            _song = new FakeSongDetail("majtest-practice", "Practice", "1", "2", "3", "4", "13") { AudioTrack = _track };
            var charts = new SimaiChart[7];
            for (var i = 0; i < charts.Length; i++)
            {
                charts[i] = i == (int)ChartLevel.Master ? SimaiParser.ParseChart("13", "Designer", "(120){4}1,2,3,4,E".AsSpan()) : SimaiChart.Empty;
            }
            _song.Maidata = new SimaiFile("Practice", "Artist", 0f, "hash", charts, null);

            _manager = AddInactiveUi<PracticeManager>("Practice", canvas);
            var visual = AddInactiveUi<ChartVisualDisplayer>("ChartVisual", canvas);
            visual.gameObject.AddComponent<RawImage>();
            ((RectTransform)visual.transform).sizeDelta = new Vector2(600, 100);
            Activate(visual);
            _manager.chartAnalyzer = visual;
            _manager.startTimeText = CreateText("Start", canvas);
            _manager.endTimeText = CreateText("End", canvas);
            _manager.timeText = CreateText("Time", canvas);
            _manager.rTimeText = CreateText("Remaining", canvas);
            _manager.selectionBox = (RectTransform)CreateRect("Selection", canvas).transform;
            _manager.progress = Objects.Add<Slider>("Progress", canvas);
            PrivateMember.SetField(_manager, "_playbackSpeedTitle", CreateText("SpeedTitle", canvas));
            PrivateMember.SetField(_manager, "_playbackSpeedValue", Objects.Add<TextMeshPro>("SpeedValue"));
        }

        /// <summary>Installs the game info and starts the scene.</summary>
        /// <param name="timeRange">An optional remembered practice range.</param>
        /// <returns>The coroutine.</returns>
        private IEnumerator StartScene(Range<double>? timeRange = null)
        {
            var info = new GameInfo(GameMode.Practice, new ISongDetail[] { _song }, new[] { ChartLevel.Master }, 114514)
            {
                TimeRange = timeRange
            };
            Majdata<GameInfo>.Free();
            Majdata<GameInfo>.Instance = info;
            Activate(_manager);
            yield return WaitUntil(() => PrivateMember.GetField<bool>(_manager, "_isInited"), 5f);
        }

        /// <summary>Gets the practice start time.</summary>
        private float StartTime => PrivateMember.GetField<float>(_manager, "_startTime");

        /// <summary>Gets the practice end time.</summary>
        private float EndTime => PrivateMember.GetField<float>(_manager, "_endTime");

        /// <summary>Touches a sensor for one update, then releases it for another.</summary>
        /// <param name="area">The sensor.</param>
        /// <returns>The coroutine.</returns>
        private static IEnumerator Touch(SensorArea area)
        {
            InputSimulator.SetSensor(area, true);
            InputSimulator.Step();
            yield return null;
            InputSimulator.SetSensor(area, false);
            InputSimulator.Step();
            yield return null;
        }

        /// <summary>Checks that initialization loads the chart and track, plays from the range start and shows the speed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_LoadsAndPlaysPracticeRange()
        {
            MajEnv.Settings.Debug.TouchSimulationRadius = 0.7f;
            Sfx("bgm_select.mp3").Playing = true;

            yield return StartScene();
            yield return null;

            Assert.That(PrivateMember.GetField<bool>(_manager, "_isInited"), Is.True);
            Assert.That(StartTime, Is.EqualTo(0f));
            Assert.That(EndTime, Is.EqualTo(60f).Within(1e-3f));
            Assert.That(_track.PlayCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(Sfx("bgm_select.mp3").StopCount, Is.EqualTo(1));
            Assert.That(Majdata<GameInfo>.Instance!.PracticeCount, Is.EqualTo(114514));
            Assert.That(PrivateMember.GetField<TextMeshPro>(_manager, "_playbackSpeedValue").text, Is.EqualTo((1f).ToString("F2")));
            Assert.That(PrivateMember.GetField<TextMeshProUGUI>(_manager, "_playbackSpeedTitle").text, Is.EqualTo("MAJSETTING_PROPERTY_PlaybackSpeed"));
            Assert.That(_manager.startTimeText.text, Is.EqualTo("0:00.000"));
            Assert.That(_manager.endTimeText.text, Is.EqualTo("1:00.000"));
            Assert.That(MajEnv.Settings.Debug.TouchSimulationRadius, Is.EqualTo(0f));
            Assert.That(InputManager.TouchButtonRingEdge, Is.EqualTo(4.8f).Within(1e-4f));
        }

        /// <summary>Checks that a remembered range is restored.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_RestoresRememberedRange()
        {
            yield return StartScene(new Range<double>(5d, 20d));

            Assert.That(StartTime, Is.EqualTo(5f));
            Assert.That(EndTime, Is.EqualTo(20f));
            Assert.That(_track.CurrentSec, Is.EqualTo(5d).Within(1e-4));
        }

        /// <summary>Checks that an empty chart returns to the list.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_EmptyChartReturnsToList()
        {
            _song.Maidata = new SimaiFile("Empty", "Artist", 0f, "hash", null, null);
            ExpectSceneSwitch();

            yield return StartScene();
            yield return new WaitForSecondsRealtime(0.2f);

            Assert.That(PrivateMember.GetField<bool>(_manager, "_isInited"), Is.False);
        }

        /// <summary>Checks that the range sensors nudge the start and end within the track.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RangeSensors_NudgeStartAndEnd()
        {
            yield return StartScene();

            yield return Touch(SensorArea.A6);
            Assert.That(StartTime, Is.EqualTo(0f));
            yield return Touch(SensorArea.B5);
            Assert.That(StartTime, Is.EqualTo(0.2f).Within(1e-4f));
            yield return Touch(SensorArea.B4);
            Assert.That(EndTime, Is.EqualTo(59.8f).Within(1e-3f));
            yield return Touch(SensorArea.A3);
            Assert.That(EndTime, Is.EqualTo(60f).Within(1e-3f));
        }

        /// <summary>Checks that the speed sensors change the playback speed in hundredths with a lower bound.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SpeedSensors_ChangePlaybackSpeed()
        {
            yield return StartScene();
            yield return null;

            yield return Touch(SensorArea.E2);
            Assert.That(PrivateMember.GetField<float>(_manager, "_playbackSpeed"), Is.EqualTo(1.01f).Within(1e-4f));
            Assert.That(PrivateMember.GetField<TextMeshPro>(_manager, "_playbackSpeedValue").text, Is.EqualTo((1.01f).ToString("F2")));

            yield return Touch(SensorArea.B7);
            Assert.That(PrivateMember.GetField<float>(_manager, "_playbackSpeed"), Is.EqualTo(1f).Within(1e-4f));
        }

        /// <summary>Checks that holding a range sensor accelerates the movement.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator HoldingRangeSensor_Accelerates()
        {
            yield return StartScene(new Range<double>(10d, 30d));
            SetDeltaTime(0.3f);

            InputSimulator.SetSensor(SensorArea.B4, true);
            for (var i = 0; i < 6; i++)
            {
                InputSimulator.Step(0.3f);
                yield return null;
            }
            InputSimulator.SetSensor(SensorArea.B4, false);
            InputSimulator.Step();
            yield return null;

            Assert.That(EndTime, Is.LessThan(29.5f));
            Assert.That(EndTime, Is.GreaterThanOrEqualTo(StartTime));
        }

        /// <summary>Checks that playback past the end jumps back to the start.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PlaybackPastEnd_LoopsToStart()
        {
            yield return StartScene(new Range<double>(5d, 20d));

            _track.CurrentSec = 25;
            yield return null;

            Assert.That(_track.CurrentSec, Is.EqualTo(5d).Within(1e-4));
        }

        /// <summary>Checks that the back button resets the speed and leaves for the list.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator BackButton_ResetsSpeedAndLeaves()
        {
            MajEnv.Settings.Mod.PlaybackSpeed = 1.5f;
            yield return StartScene();
            ExpectSceneSwitch();
            LogAssert.Expect(LogType.Exception, new Regex("OperationCanceledException"));

            InputSimulator.StepUntilButtonClickCompleted(ButtonZone.A5);
            yield return null;

            Assert.That(PrivateMember.GetField<bool>(_manager, "_isExited"), Is.True);
            Assert.That(MajEnv.Settings.Mod.PlaybackSpeed, Is.EqualTo(1f));
        }

        /// <summary>Checks that the start button stores the range and speed and starts the game.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator StartButton_StoresRangeAndStarts()
        {
            yield return StartScene(new Range<double>(3d, 9d));
            ExpectSceneSwitch();
            LogAssert.Expect(LogType.Exception, new Regex("OperationCanceledException"));

            InputSimulator.StepUntilButtonClickCompleted(ButtonZone.A4);
            yield return null;

            var range = Majdata<GameInfo>.Instance!.TimeRange!.Value;
            Assert.That(range.Start, Is.EqualTo(3d).Within(1e-4));
            Assert.That(range.End, Is.EqualTo(9d).Within(1e-4));
            Assert.That(MajEnv.Settings.Mod.PlaybackSpeed, Is.EqualTo(1f));
        }

        /// <summary>Checks that destroying the scene stops the track and restores the touch settings.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Destroy_RestoresSettings()
        {
            MajEnv.Settings.Debug.TouchSimulationRadius = 0.4f;
            yield return StartScene();

            UnityEngine.Object.DestroyImmediate(_manager.gameObject);

            Assert.That(MajEnv.Settings.Debug.TouchSimulationRadius, Is.EqualTo(0.4f));
            Assert.That(InputManager.TouchButtonRingEdge, Is.EqualTo(5.4f).Within(1e-4f));
            Assert.That(_track.StopCount, Is.GreaterThanOrEqualTo(1));
        }

        /// <summary>Checks the held duration helper.</summary>
        [Test]
        public void UpdateHeldDuration_AccumulatesWhilePressed()
        {
            SetDeltaTime(0.25f);
            var arguments = new object?[] { 1f, true };

            var held = PrivateMember.InvokeStatic<float>(typeof(PracticeManager), "UpdateHeldDuration", arguments);
            Assert.That(held, Is.EqualTo(1.25f));
            Assert.That(arguments[0], Is.EqualTo(1.25f));

            arguments[1] = false;
            Assert.That(PrivateMember.InvokeStatic<float>(typeof(PracticeManager), "UpdateHeldDuration", arguments), Is.EqualTo(0f));
        }
    }
}
