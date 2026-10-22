using System.Collections;
using MajdataPlay.IO;
using MajdataPlay.Scenes.SortFind;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.SortFindScene
{
    /// <summary>Covers the sort and search scene: sort selection, keyword editing and applying the order.</summary>
    public class SortFindManagerTests : SceneTestBase
    {
        /// <summary>The manager under test.</summary>
        private SortFindManager _manager = null!;

        /// <summary>The search bar.</summary>
        private TMP_InputField _search = null!;

        /// <summary>The clear button.</summary>
        private GameObject _clear = null!;

        /// <summary>The sort label.</summary>
        private TextMeshProUGUI _slot = null!;

        /// <summary>Creates the manager; it starts on the first frame of each test.</summary>
        [SetUp]
        public void SetUp()
        {
            CreateSceneSwitcherStub();
            var canvas = CreateCanvas();
            _search = CreateInputField("Search", canvas);
            _clear = CreateRect("Clear", canvas);
            _slot = CreateText("Slot", canvas);
            _manager = AddInactive<SortFindManager>("SortFind", canvas);
            PrivateMember.SetField(_manager, "_slotText", _slot);
            PrivateMember.SetField(_manager, "_searchBar", _search);
            PrivateMember.SetField(_manager, "_clearButton", _clear);
        }

        /// <summary>Starts the manager.</summary>
        /// <returns>The coroutine.</returns>
        private IEnumerator StartManager()
        {
            Activate(_manager);
            yield return null;
        }

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

        /// <summary>Gets the selected sort type.</summary>
        private SortType Selected => PrivateMember.GetField<SortType>(_manager, "_sortType");

        /// <summary>Checks that Start shows the stored keyword and sort order.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_ShowsStoredOrder()
        {
            MajEnv.RuntimeConfig!.List.OrderBy.Keyword = "abc";
            MajEnv.RuntimeConfig.List.OrderBy.SortBy = SortType.ByTitle;

            yield return StartManager();

            Assert.That(_search.text, Is.EqualTo("abc"));
            Assert.That(_clear.activeSelf, Is.True);
            Assert.That(Selected, Is.EqualTo(SortType.ByTitle));
            Assert.That(_slot.text, Is.EqualTo("MAJTEXT_SORTFIND_SortbyTitle"));
        }

        /// <summary>Checks that the arrow sensors cycle the sort order in both directions with wrap-around.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Sensors_CycleSortOrder()
        {
            yield return StartManager();
            Assert.That(_clear.activeSelf, Is.False);

            yield return Touch(SensorArea.E6);
            Assert.That(Selected, Is.EqualTo(SortType.ByRank));
            yield return Touch(SensorArea.B4);
            Assert.That(Selected, Is.EqualTo(SortType.Default));
            yield return Touch(SensorArea.E4);
            Assert.That(Selected, Is.EqualTo(SortType.ByTime));
            yield return Touch(SensorArea.B5);
            Assert.That(Selected, Is.EqualTo(SortType.Default));
            Assert.That(_slot.text, Is.EqualTo("MAJTEXT_SORTFIND_SortbyDefault"));
        }

        /// <summary>Checks that E3 clears the keyword and the clear button follows the text.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator E3_ClearsKeyword()
        {
            yield return StartManager();
            _search.text = "keyword";
            Assert.That(_clear.activeSelf, Is.True);

            yield return Touch(SensorArea.E3);

            Assert.That(_search.text, Is.Empty);
            Assert.That(_clear.activeSelf, Is.False);
        }

        /// <summary>Checks that the focus sensors keep the scene running.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator FocusSensors_KeepScene()
        {
            yield return StartManager();

            yield return Touch(SensorArea.C);
            yield return Touch(SensorArea.B7);

            Assert.That(PrivateMember.GetField<bool>(_manager, "_isExited"), Is.False);
        }

        /// <summary>Checks that the confirm button stores the keyword and sort order and returns to the list.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ConfirmButton_AppliesOrderAndLeaves()
        {
            yield return StartManager();
            yield return Touch(SensorArea.E4);
            _search.text = "song";
            ExpectSceneSwitch();

            InputSimulator.StepUntilButtonClickCompleted(ButtonZone.P1);
            yield return null;
            yield return null;

            Assert.That(MajEnv.RuntimeConfig!.List.OrderBy.Keyword, Is.EqualTo("song"));
            Assert.That(MajEnv.RuntimeConfig.List.OrderBy.SortBy, Is.EqualTo(SortType.ByTime));
            Assert.That(PrivateMember.GetField<bool>(_manager, "_isExited"), Is.True);
        }

        /// <summary>Checks that submitting the search applies the order once.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Submit_AppliesOrderOnce()
        {
            yield return StartManager();
            _search.text = "submitted";
            ExpectSceneSwitch();

            _search.onSubmit.Invoke(_search.text);
            yield return null;

            Assert.That(MajEnv.RuntimeConfig!.List.OrderBy.Keyword, Is.EqualTo("submitted"));
            Assert.That(PrivateMember.GetField<bool>(_manager, "_isExited"), Is.True);
        }
    }
}
