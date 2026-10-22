using System;
using System.Collections;
using MajdataPlay.Behaviours.UI;
using MajdataPlay.IO;
using MajdataPlay.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.PlayMode
{
    /// <summary>
    /// Covers the press, hold and release animations of <see cref="ButtonAnimation"/> in Play Mode. Input is simulated by
    /// writing the input manager's per-frame control states, which nothing else updates in the test scene.
    /// </summary>
    public class ButtonAnimationTests
    {
        /// <summary>The objects created by the test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Creates the object tracker.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            SetState("_buttonControlStates", (int)ButtonZone.A1, false, false, false);
            SetState("_sensorControlStates", (int)SensorArea.A1, false, false, false);
        }

        /// <summary>Destroys every object created by the test and clears the simulated input.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
            SetState("_buttonControlStates", (int)ButtonZone.A1, false, false, false);
            SetState("_sensorControlStates", (int)SensorArea.A1, false, false, false);
        }

        /// <summary>Writes one simulated control state.</summary>
        /// <param name="field">The input manager state array name.</param>
        /// <param name="index">The button or sensor index.</param>
        /// <param name="isPressed">Whether the control is held.</param>
        /// <param name="pressedThisFrame">Whether the control was pressed in this frame.</param>
        /// <param name="releasedThisFrame">Whether the control was released in this frame.</param>
        private static void SetState(string field, int index, bool isPressed, bool pressedThisFrame, bool releasedThisFrame)
        {
            var states = PrivateMember.GetStaticField<Array>(typeof(InputManager), field);
            var state = Activator.CreateInstance(states.GetType().GetElementType()!)!;
            PrivateMember.SetProperty(state, "IsPressed", isPressed);
            PrivateMember.SetProperty(state, "PressedThisFrame", pressedThisFrame);
            PrivateMember.SetProperty(state, "ReleasedThisFrame", releasedThisFrame);
            states.SetValue(state, index);
        }

        /// <summary>Simulates the A1 button.</summary>
        /// <param name="isPressed">Whether the button is held.</param>
        /// <param name="pressedThisFrame">Whether it was pressed in this frame.</param>
        /// <param name="releasedThisFrame">Whether it was released in this frame.</param>
        private static void SetButton(bool isPressed, bool pressedThisFrame, bool releasedThisFrame)
        {
            SetState("_buttonControlStates", (int)ButtonZone.A1, isPressed, pressedThisFrame, releasedThisFrame);
        }

        /// <summary>Simulates the A1 sensor.</summary>
        /// <param name="isPressed">Whether the sensor is held.</param>
        /// <param name="pressedThisFrame">Whether it was pressed in this frame.</param>
        /// <param name="releasedThisFrame">Whether it was released in this frame.</param>
        private static void SetSensor(bool isPressed, bool pressedThisFrame, bool releasedThisFrame)
        {
            SetState("_sensorControlStates", (int)SensorArea.A1, isPressed, pressedThisFrame, releasedThisFrame);
        }

        /// <summary>Creates an animated button bound to the A1 button and sensor with a hold circle.</summary>
        /// <param name="holdTriggerTime">The hold time, or zero for a click.</param>
        /// <returns>The animation component.</returns>
        private ButtonAnimation CreateButton(float holdTriggerTime)
        {
            var circle = _objects.Add<Image>("Circle");
            var animation = _objects.Add<ButtonAnimation>("Button");
            animation.BindButton = new[] { ButtonZone.A1 };
            animation.BindSensor = new[] { SensorArea.A1 };
            animation.HoldCircle = circle;
            animation.HoldTriggerTime = holdTriggerTime;
            animation.AnimationDuration = 0.05f;
            animation.StartColor = Color.white;
            animation.EndColor = Color.green;
            return animation;
        }

        /// <summary>Checks that the hold circle is configured as an empty radial fill on start.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_ConfiguresHoldCircle()
        {
            var animation = CreateButton(1f);
            animation.HoldCircle.fillAmount = 0.7f;

            yield return null;

            Assert.That(animation.HoldCircle.type, Is.EqualTo(Image.Type.Filled));
            Assert.That(animation.HoldCircle.fillMethod, Is.EqualTo(Image.FillMethod.Radial360));
            Assert.That(animation.HoldCircle.fillAmount, Is.EqualTo(0f));
            Assert.That(animation.HoldCircle.color, Is.EqualTo(Color.white));
        }

        /// <summary>Checks that a click shrinks the button and releases it automatically.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Click_ShrinksAndRestores()
        {
            var animation = CreateButton(0f);
            yield return null;

            SetButton(true, true, false);
            yield return null;
            SetButton(false, false, false);
            yield return new WaitForSeconds(0.04f);
            Assert.That(animation.transform.localScale.x, Is.LessThan(1f));

            yield return new WaitForSeconds(0.3f);
            Assert.That(animation.transform.localScale.x, Is.EqualTo(1f).Within(1e-3f));
        }

        /// <summary>Checks that holding long enough fills the circle completely and releasing restores the scale.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Hold_CompletesCircle()
        {
            var animation = CreateButton(0.1f);
            yield return null;

            SetSensor(true, true, false);
            yield return null;
            SetSensor(true, false, false);
            yield return new WaitForSeconds(0.25f);

            Assert.That(animation.HoldCircle.fillAmount, Is.EqualTo(1f));
            Assert.That(animation.transform.localScale.x, Is.LessThan(1f));

            SetSensor(false, false, true);
            yield return null;
            SetSensor(false, false, false);
            yield return new WaitForSeconds(0.3f);

            Assert.That(animation.transform.localScale.x, Is.EqualTo(1f).Within(1e-3f));
        }

        /// <summary>Checks that releasing before the hold time drains the circle again.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Hold_ReleasedEarlyDrainsCircle()
        {
            var animation = CreateButton(2f);
            yield return null;

            SetButton(true, true, false);
            yield return null;
            SetButton(true, false, false);
            yield return new WaitForSeconds(0.3f);
            Assert.That(animation.HoldCircle.fillAmount, Is.GreaterThan(0f));

            SetButton(false, false, true);
            yield return null;
            SetButton(false, false, false);
            yield return new WaitForSeconds(0.4f);

            Assert.That(animation.HoldCircle.fillAmount, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(animation.transform.localScale.x, Is.EqualTo(1f).Within(1e-3f));
        }

        /// <summary>Checks that disabling the component resets the circle and the scale.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnDisable_ResetsState()
        {
            var animation = CreateButton(2f);
            animation.ReleasedScale = 1.2f;
            yield return null;
            SetButton(true, true, false);
            yield return null;
            SetButton(true, false, false);
            yield return new WaitForSeconds(0.1f);

            animation.enabled = false;

            Assert.That(animation.HoldCircle.fillAmount, Is.EqualTo(0f));
            Assert.That(animation.HoldCircle.color, Is.EqualTo(Color.white));
            Assert.That(animation.transform.localScale, Is.EqualTo(new Vector3(1.2f, 1.2f, 1.2f)));
        }

        /// <summary>Checks the editor preview of the click animation and its cancellation.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Preview_PlaysAndStops()
        {
            var animation = CreateButton(0f);
            animation.HoldCircle = null!;
            yield return null;

            animation.PlayClickAnimationPreview();
            yield return new WaitForSeconds(0.03f);
            Assert.That(animation.transform.localScale.x, Is.LessThan(1f));

            animation.StopAnimationPreview(new Vector3(2f, 2f, 2f));
            yield return new WaitForSeconds(0.1f);
            Assert.That(animation.transform.localScale, Is.EqualTo(new Vector3(2f, 2f, 2f)));
        }
    }
}
