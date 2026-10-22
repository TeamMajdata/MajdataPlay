using System.Collections;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.GamePlay.PlayMode.Support;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.PlayMode
{
    /// <summary>Checks the elapsed, remaining and progress display of <see cref="TimeDisplayer"/> in Play Mode.</summary>
    public class TimeDisplayerPlayModeTests
    {
        /// <summary>Tracks objects created by a test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Restores the registered note controller.</summary>
        private SingletonScope<INoteController> _controllerSlot = null!;

        /// <summary>Restores the registered time displayer.</summary>
        private SingletonScope<TimeDisplayer> _displayerSlot = null!;

        /// <summary>The note controller seen by the displayer.</summary>
        private GamePlayFakeNoteController _controller = null!;

        /// <summary>Registers a fake note controller.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            _controller = new GamePlayFakeNoteController { ThisFrameSec = 0f, AudioLength = 120f };
            _controllerSlot = new SingletonScope<INoteController>(_controller);
            _displayerSlot = new SingletonScope<TimeDisplayer>();
        }

        /// <summary>Destroys the displayer and restores the registrations.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
            _displayerSlot.Dispose();
            _controllerSlot.Dispose();
        }

        /// <summary>Checks that Awake registers the displayer.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Awake_RegistersTheDisplayer()
        {
            var displayer = CreateDisplayer();

            yield return null;

            Assert.That(Majdata<TimeDisplayer>.Instance, Is.SameAs(displayer));
        }

        /// <summary>Checks the elapsed time, remaining time and progress during playback.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnPreUpdate_ShowsElapsedAndRemainingTime()
        {
            var displayer = CreateDisplayer();
            yield return null;

            _controller.ThisFrameSec = 61.5f;
            displayer.OnPreUpdate();

            Assert.That(displayer.timeText.text, Is.EqualTo("1:01:500"));
            Assert.That(displayer.rTimeText.text, Is.EqualTo("0:58:500"));
            Assert.That(displayer.progress.value, Is.EqualTo(61.5f / 120f).Within(1e-4f));
        }

        /// <summary>Checks the negative elapsed time shown during the lead-in.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnPreUpdate_ShowsNegativeTimeBeforeTheStart()
        {
            var displayer = CreateDisplayer();
            yield return null;

            _controller.ThisFrameSec = -2.25f;
            displayer.OnPreUpdate();

            Assert.That(displayer.timeText.text, Is.EqualTo("-0:02:250"));
            Assert.That(displayer.rTimeText.text, Is.EqualTo("2:02:250"));
            Assert.That(displayer.progress.value, Is.EqualTo(0f));
        }

        /// <summary>Checks that the texts are only rewritten when the time changes and that the progress is clamped.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnPreUpdate_SkipsUnchangedTimeAndClampsProgress()
        {
            _controller.ThisFrameSec = 5f;
            var displayer = CreateDisplayer();
            yield return null;
            displayer.timeText.text = "unchanged";

            displayer.OnPreUpdate();
            Assert.That(displayer.timeText.text, Is.EqualTo("unchanged"), "The time did not change since Start");

            _controller.ThisFrameSec = 150f;
            displayer.OnPreUpdate();

            Assert.That(displayer.timeText.text, Is.EqualTo("2:30:000"));
            Assert.That(displayer.progress.value, Is.EqualTo(1f));
        }

        /// <summary>Creates an active displayer with its texts and progress bar.</summary>
        /// <returns>The displayer, awakened by Unity.</returns>
        private TimeDisplayer CreateDisplayer()
        {
            var root = _objects.Create("TimeDisplayer");
            root.SetActive(false);
            var displayer = root.AddComponent<TimeDisplayer>();
            displayer.timeText = _objects.Add<TextMeshProUGUI>("Time", root.transform);
            displayer.rTimeText = _objects.Add<TextMeshProUGUI>("Remaining", root.transform);
            displayer.progress = _objects.Add<Slider>("Progress", root.transform);
            root.SetActive(true);
            return displayer;
        }
    }
}
