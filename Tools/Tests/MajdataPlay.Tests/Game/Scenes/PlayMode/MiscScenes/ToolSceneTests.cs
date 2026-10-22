using System.Collections;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Other;
using MajdataPlay.Scenes.Test;
using MajdataPlay.Scenes.View;
using MajdataPlay.Scenes.View.Types;
using MajdataPlay.Settings;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.MiscScenes
{
    /// <summary>Covers the sensor test page, the view mode helpers and the HTTP diagnostics page without devices or network.</summary>
    public class ToolSceneTests : SceneTestBase
    {
        /// <summary>Creates an IO listener whose page objects are wired without running Awake, which needs the background video.</summary>
        /// <returns>The listener.</returns>
        private IOListener CreateListener()
        {
            var listener = AddInactive<IOListener>("IOListener");
            var sensors = CreateRect("Sensors", listener.transform);
            var sensorTexts = CreateRect("SensorTexts", listener.transform);
            var buttons = CreateRect("Buttons", listener.transform);
            var buttonStates = CreateRect("ButtonStates", listener.transform);
            buttons.SetActive(false);
            buttonStates.SetActive(false);
            PrivateMember.SetField(listener, "_sensorParent", sensors);
            PrivateMember.SetField(listener, "_sensorTextParent", sensorTexts);
            PrivateMember.SetField(listener, "_btnParent", buttons);
            PrivateMember.SetField(listener, "_btnStateTextParent", buttonStates);
            var materials = PrivateMember.GetField<Material[]>(listener, "_materials");
            for (var i = 0; i < materials.Length; i++)
            {
                materials[i] = Objects.Track(new Material(Shader.Find("Sprites/Default")));
            }
            var states = PrivateMember.GetField<TextMeshPro[]>(listener, "_btnStateTexts");
            for (var i = 0; i < states.Length; i++)
            {
                states[i] = Objects.Add<TextMeshPro>($"State{i}", buttonStates.transform);
            }
            var nextScene = IOListener.NextScene;
            var mode = MajEnv.Mode;
            AddCleanup(() =>
            {
                IOListener.NextScene = nextScene;
                MajEnv.Mode = mode;
            });
            return listener;
        }

        /// <summary>Checks that the sensor page colors touched sensors and the test button switches pages.</summary>
        [Test]
        public void IOListener_SensorPageColorsTouchesAndSwitchesPage()
        {
            var listener = CreateListener();
            IOListener.NextScene = string.Empty;
            var materials = PrivateMember.GetField<Material[]>(listener, "_materials");
            InputSimulator.SetRawTouch(3, true);

            UnityMessages.Send(listener, "Update");
            Assert.That(materials[3].color, Is.EqualTo(Color.red));
            Assert.That(materials[4].color, Is.EqualTo(Color.blue));

            InputSimulator.SetButton(ButtonZone.Test, true);
            InputSimulator.Step();
            UnityMessages.Send(listener, "Update");

            Assert.That(PrivateMember.GetField<object>(listener, "_currentPage").ToString(), Is.EqualTo("Button"));
            Assert.That(PrivateMember.GetField<GameObject>(listener, "_btnParent").activeSelf, Is.True);
            Assert.That(PrivateMember.GetField<GameObject>(listener, "_sensorParent").activeSelf, Is.False);
        }

        /// <summary>Checks that the button page shows button states and returns to the sensor page after a long test press.</summary>
        [Test]
        public void IOListener_ButtonPageShowsStatesAndReturns()
        {
            var listener = CreateListener();
            IOListener.NextScene = string.Empty;
            PrivateMember.Invoke(listener, "NextPage");
            var states = PrivateMember.GetField<TextMeshPro[]>(listener, "_btnStateTexts");
            SetDeltaTime(2f);

            InputSimulator.SetButton(ButtonZone.A3, true);
            InputSimulator.SetButton(ButtonZone.Test, true);
            InputSimulator.Step();
            for (var i = 0; i < 3; i++)
            {
                UnityMessages.Send(listener, "Update");
            }
            Assert.That(states[2].text, Is.EqualTo("ON"));
            Assert.That(states[0].text, Is.EqualTo("OFF"));
            Assert.That(PrivateMember.GetField<object>(listener, "_currentPage").ToString(), Is.EqualTo("Button"));

            UnityMessages.Send(listener, "Update");
            Assert.That(PrivateMember.GetField<object>(listener, "_currentPage").ToString(), Is.EqualTo("Sensor"));

            InputSimulator.SetButton(ButtonZone.Test, false);
            InputSimulator.Step();
            PrivateMember.Invoke(listener, "NextPage");
            UnityMessages.Send(listener, "Update");
            Assert.That(PrivateMember.GetField<float>(listener, "_testBtnPressTime"), Is.EqualTo(0f));
        }

        /// <summary>Checks that the exit button leaves for the next scene and returns to play mode.</summary>
        [Test]
        public void IOListener_ExitLeavesForNextScene()
        {
            CreateSceneSwitcherStub();
            var listener = CreateListener();
            IOListener.NextScene = "List";
            MajEnv.Mode = RunningMode.Test;
            InputSimulator.StepUntilButtonClickCompleted(ButtonZone.A5);
            ExpectSceneSwitch();

            UnityMessages.Send(listener, "Update");
            UnityMessages.Send(listener, "Update");

            Assert.That(PrivateMember.GetField<bool>(listener, "_exitFlag"), Is.True);
            Assert.That(MajEnv.Mode, Is.EqualTo(RunningMode.Play));
        }

        /// <summary>Checks the view summary and the view manager's state-dependent properties.</summary>
        [Test]
        public void ViewManager_SummaryAndProperties()
        {
            var state = PrivateMember.GetStaticField<ViewStatus>(typeof(ViewManager), "_state");
            var error = PrivateMember.GetStaticField<string>(typeof(ViewManager), "_errMsg");
            AddCleanup(() =>
            {
                PrivateMember.SetStaticField(typeof(ViewManager), "_state", state);
                PrivateMember.SetStaticField(typeof(ViewManager), "_errMsg", error);
            });
            var manager = AddInactive<ViewManager>("View");

            PrivateMember.SetStaticField(typeof(ViewManager), "_state", ViewStatus.Playing);
            PrivateMember.SetStaticField(typeof(ViewManager), "_errMsg", "oops");
            MajEnv.Settings.Mod.AutoPlay = AutoplayModeOption.Enable;

            Assert.That(ViewManager.Summary.State, Is.EqualTo(ViewStatus.Playing));
            Assert.That(ViewManager.Summary.ErrMsg, Is.EqualTo("oops"));
            Assert.That(manager.IsStart, Is.True);
            Assert.That(manager.IsAutoplay, Is.True);
            Assert.That(manager.AutoplayMode, Is.EqualTo(AutoplayModeOption.Enable));

            PrivateMember.SetStaticField(typeof(ViewManager), "_state", ViewStatus.Paused);
            MajEnv.Settings.Mod.AutoPlay = AutoplayModeOption.Disable;
            Assert.That(manager.IsStart, Is.False);
            Assert.That(manager.IsAutoplay, Is.False);
            Assert.That(manager.ThisFrameSec, Is.EqualTo(manager.ThisFixedUpdateSec));
        }

        /// <summary>Checks that the heartbeat JSON reports the summary with enum names.</summary>
        [Test]
        public void WsService_SummaryJsonIsHeartbeat()
        {
            var json = PrivateMember.InvokeStatic<string>(typeof(MajdataWsService), "GetSummaryJson");

            Assert.That(json, Does.Contain("Heartbeat"));
            Assert.That(json, Does.Contain("\"State\""));
        }

        /// <summary>Checks that the view settings button opens the setting scene.</summary>
        [Test]
        public void SettingButton_OpensSettings()
        {
            CreateSceneSwitcherStub();
            var button = AddInactive<SettingButton>("SettingButton");
            ExpectSceneSwitch();

            button.OnClick();

            Assert.That(button, Is.Not.Null);
        }

        /// <summary>Checks that the HTTP diagnostics page starts, logs, resets and cancels without sending requests.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator MultiHttpTest_LogsResetsAndCancels()
        {
            var page = AddInactive<MultiHttpTest>("Http");
            var log = Objects.Add<Text>("Log");
            PrivateMember.SetField(page, "logText", log);
            Activate(page);
            yield return null;

            PrivateMember.Invoke(page, "Log", "first");
            PrivateMember.Invoke(page, "Log", "second");
            Assert.That(log.text, Does.Contain("first").And.Contain("second"));

            PrivateMember.Invoke(page, "CancelRunning");
            Assert.That(log.text, Is.Empty);
            Assert.That(PrivateMember.GetField<System.Threading.CancellationTokenSource>(page, "_cts"), Is.Not.Null);

            PrivateMember.Invoke(page, "Log", "third");
            PrivateMember.Invoke(page, "ResetLog");
            Assert.That(log.text, Is.Empty);
        }
    }
}
