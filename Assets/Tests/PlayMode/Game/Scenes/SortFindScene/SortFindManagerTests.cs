using System.Collections;
using System.Collections.Generic;
using MajdataPlay.IO;
using MajdataPlay.Scenes.SortFind;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

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

        /// <summary>Checks search uses the authored menu button-ring reach and restores the default on exit.</summary>
        /// <returns>The scene-bound touch-range regression coroutine.</returns>
        [UnityTest]
        public IEnumerator ButtonRingRange_IsRestoredWhenTheManagerIsDestroyed()
        {
            InputManager.TouchButtonRingEdge = 5.4f;
            yield return StartManager();
            Assert.That(InputManager.TouchButtonRingEdge, Is.EqualTo(4.8f));
            Object.DestroyImmediate(_manager.gameObject);
            Assert.That(InputManager.TouchButtonRingEdge, Is.EqualTo(5.4f));
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

            InputSimulator.StepUntilButtonClickCompleted(ButtonZone.A4);
            yield return null;
            yield return null;

            Assert.That(MajEnv.RuntimeConfig!.List.OrderBy.Keyword, Is.EqualTo("song"));
            Assert.That(MajEnv.RuntimeConfig.List.OrderBy.SortBy, Is.EqualTo(SortType.ByTime));
            Assert.That(PrivateMember.GetField<bool>(_manager, "_isExited"), Is.True);
        }

        /// <summary>Checks the A4 touch sensor commits the pending edits like the physical confirm button.</summary>
        /// <returns>The touch confirmation coroutine.</returns>
        [UnityTest]
        public IEnumerator ConfirmSensor_AppliesOrderAndLeaves()
        {
            yield return StartManager();
            yield return Touch(SensorArea.E4);
            _search.text = "touch submitted";
            ExpectSceneSwitch();
            InputSimulator.StepUntilSensorClickCompleted(SensorArea.A4);
            yield return null;
            Assert.That(MajEnv.RuntimeConfig!.List.OrderBy.Keyword, Is.EqualTo("touch submitted"));
            Assert.That(MajEnv.RuntimeConfig.List.OrderBy.SortBy, Is.EqualTo(SortType.ByTime));
            Assert.That(PrivateMember.GetField<bool>(_manager, "_isExited"), Is.True);
        }

        /// <summary>Checks the physical A5 cancel action discards edits and cannot later submit them.</summary>
        /// <returns>The physical cancellation coroutine.</returns>
        [UnityTest]
        public IEnumerator CancelButton_DiscardsEditsAndLeaves()
        {
            yield return CancelEdits(false);
        }

        /// <summary>Checks the A5 touch cancel action discards edits and cannot later submit them.</summary>
        /// <returns>The touch cancellation coroutine.</returns>
        [UnityTest]
        public IEnumerator CancelSensor_DiscardsEditsAndLeaves()
        {
            yield return CancelEdits(true);
        }

        /// <summary>Edits both pending values, cancels and checks the shared ordering object and its values remain unchanged.</summary>
        /// <param name="useSensor">Whether to cancel with the touch sensor instead of the physical button.</param>
        /// <returns>The cancellation and duplicate-submit regression coroutine.</returns>
        private IEnumerator CancelEdits(bool useSensor)
        {
            var order = SongStorage.OrderBy;
            order.Keyword = "saved keyword";
            order.SortBy = SortType.ByTitle;
            yield return StartManager();
            _search.text = "discard this keyword";
            yield return Touch(SensorArea.E4);
            Assert.That(Selected, Is.EqualTo(SortType.ByRank));
            ExpectSceneSwitch();
            if (useSensor)
            {
                InputSimulator.StepUntilSensorClickCompleted(SensorArea.A5);
            }
            else
            {
                InputSimulator.StepUntilButtonClickCompleted(ButtonZone.A5);
            }
            yield return null;
            Assert.That(PrivateMember.GetField<bool>(_manager, "_isExited"), Is.True);
            _search.onSubmit.Invoke(_search.text);
            InputSimulator.StepUntilButtonClickCompleted(ButtonZone.A4);
            yield return null;
            Assert.That(SongStorage.OrderBy, Is.SameAs(order));
            Assert.That(order.Keyword, Is.EqualTo("saved keyword"));
            Assert.That(order.SortBy, Is.EqualTo(SortType.ByTitle));
        }

        /// <summary>Checks the removed D5 and P1 actions no longer submit pending edits.</summary>
        /// <returns>The obsolete-input regression coroutine.</returns>
        [UnityTest]
        public IEnumerator LegacyConfirmInputs_DoNotApplyOrLeave()
        {
            yield return StartManager();
            _search.text = "pending";
            InputSimulator.StepUntilSensorClickCompleted(SensorArea.D5);
            yield return null;
            InputSimulator.StepUntilButtonClickCompleted(ButtonZone.P1);
            yield return null;
            Assert.That(PrivateMember.GetField<bool>(_manager, "_isExited"), Is.False);
            Assert.That(SongStorage.OrderBy.Keyword, Is.Empty);
        }

        /// <summary>Checks native EventSystem pointer dispatch focuses the field and a simultaneous focus sensor preserves its caret.</summary>
        /// <returns>The real UGUI raycast, pointer and focus-preservation regression coroutine.</returns>
        [UnityTest]
        public IEnumerator PointerClick_FocusesSearchAndFocusSensorPreservesTheCaret()
        {
            var canvas = _search.GetComponentInParent<Canvas>();
            var raycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();
            var target = _search.gameObject.AddComponent<Image>();
            _search.targetGraphic = target;
            _search.textComponent.raycastTarget = false;
            var events = EventSystem.current != null ? EventSystem.current : Objects.Add<EventSystem>("Search event system");
            yield return StartManager();
            _search.text = "editable keyword";
            _search.DeactivateInputField();
            events.SetSelectedGameObject(null);
            yield return null;
            Canvas.ForceUpdateCanvases();
            var pointer = new PointerEventData(events)
            {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, _search.transform.position)
            };
            var hits = new List<RaycastResult>();
            raycaster.Raycast(pointer, hits);
            var hit = hits.Find(result => result.gameObject == _search.gameObject);
            Assert.That(hit.gameObject, Is.SameAs(_search.gameObject));
            pointer.pointerCurrentRaycast = hit;
            ExecuteEvents.Execute(_search.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(_search.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(_search.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            yield return null;
            Assert.That(events.currentSelectedGameObject, Is.SameAs(_search.gameObject));
            Assert.That(_search.isFocused, Is.True);
            _search.caretPosition = 2;
            yield return Touch(SensorArea.C);
            Assert.That(_search.isFocused, Is.True);
            Assert.That(_search.caretPosition, Is.EqualTo(2));
            Assert.That(PrivateMember.GetField<bool>(_manager, "_isExited"), Is.False);
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
            _search.text = "ignored after submission";
            _search.onSubmit.Invoke(_search.text);
            yield return null;

            Assert.That(MajEnv.RuntimeConfig!.List.OrderBy.Keyword, Is.EqualTo("submitted"));
            Assert.That(PrivateMember.GetField<bool>(_manager, "_isExited"), Is.True);
        }
    }
}
