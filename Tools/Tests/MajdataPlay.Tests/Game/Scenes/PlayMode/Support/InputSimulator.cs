using System;
using System.Reflection;
using MajdataPlay.IO;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.Support
{
    /// <summary>
    /// Drives the static <see cref="InputManager"/> frame state without devices, mirroring the per-frame update that
    /// the game updater normally performs.
    /// </summary>
    public static class InputSimulator
    {
        /// <summary>The number of physical buttons.</summary>
        private const int ButtonCount = 12;

        /// <summary>The number of touch sensors.</summary>
        private const int SensorCount = 33;

        /// <summary>The binding flags used to reach the input manager's private static state.</summary>
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.NonPublic;

        /// <summary>The desired button states applied on the next <see cref="Step"/>.</summary>
        private static readonly bool[] s_buttons = new bool[ButtonCount];

        /// <summary>The desired sensor states applied on the next <see cref="Step"/>.</summary>
        private static readonly bool[] s_sensors = new bool[SensorCount];

        /// <summary>Sets whether a button is held from the next step on.</summary>
        /// <param name="zone">The button.</param>
        /// <param name="isPressed">Whether it is held.</param>
        public static void SetButton(ButtonZone zone, bool isPressed)
        {
            s_buttons[(int)zone] = isPressed;
        }

        /// <summary>Sets whether a sensor is held from the next step on.</summary>
        /// <param name="area">The sensor.</param>
        /// <param name="isPressed">Whether it is held.</param>
        public static void SetSensor(SensorArea area, bool isPressed)
        {
            s_sensors[(int)area] = isPressed;
        }

        /// <summary>Advances one input frame: the current statuses become previous and the desired states become current.</summary>
        /// <param name="deltaTime">The frame duration used for held and click timing.</param>
        public static void Step(float deltaTime = 0.016f)
        {
            var buttonPrevious = GetArray<SwitchStatus>("_btnStatusInPreviousFrame");
            var buttonCurrent = GetArray<SwitchStatus>("_btnStatusInThisFrame");
            var sensorPrevious = GetArray<SwitchStatus>("_sensorStatusInPreviousFrame");
            var sensorCurrent = GetArray<SwitchStatus>("_sensorStatusInThisFrame");
            for (var i = 0; i < ButtonCount; i++)
            {
                buttonPrevious[i] = buttonCurrent[i];
                buttonCurrent[i] = s_buttons[i] ? SwitchStatus.On : SwitchStatus.Off;
            }
            for (var i = 0; i < SensorCount; i++)
            {
                sensorPrevious[i] = sensorCurrent[i];
                sensorCurrent[i] = s_sensors[i] ? SwitchStatus.On : SwitchStatus.Off;
            }
            typeof(InputManager).GetMethod("UpdateControlStates", StaticFlags)!.Invoke(null, new object[] { deltaTime });
        }

        /// <summary>Presses a button, then steps until its completed click is reported for exactly the current frame.</summary>
        /// <param name="zone">The button.</param>
        /// <param name="release">Whether to release the button immediately after the press frame.</param>
        public static void StepUntilButtonClickCompleted(ButtonZone zone, bool release = true)
        {
            SetButton(zone, true);
            Step();
            if (release)
            {
                SetButton(zone, false);
            }
            for (var i = 0; i < 64 && !InputManager.IsButtonClickCompletedInThisFrame(zone); i++)
            {
                Step(0.1f);
            }
        }

        /// <summary>Presses a sensor, then steps until its completed click is reported for exactly the current frame.</summary>
        /// <param name="area">The sensor.</param>
        public static void StepUntilSensorClickCompleted(SensorArea area)
        {
            SetSensor(area, true);
            Step();
            SetSensor(area, false);
            for (var i = 0; i < 64 && !InputManager.IsSensorClickCompletedInThisFrame(area); i++)
            {
                Step(0.1f);
            }
        }

        /// <summary>Holds a button for a duration, releases it and steps until the delayed release completion is reported.</summary>
        /// <param name="zone">The button.</param>
        /// <param name="heldSeconds">How long the button is held before release.</param>
        public static void StepUntilButtonReleaseCompleted(ButtonZone zone, float heldSeconds)
        {
            SetButton(zone, true);
            Step(0.016f);
            var remaining = heldSeconds;
            while (remaining > 0f)
            {
                var delta = Math.Min(0.1f, remaining);
                Step(delta);
                remaining -= delta;
            }
            SetButton(zone, false);
            Step(0.016f);
            for (var i = 0; i < 64 && !InputManager.GetButtonState(zone).ReleaseCompletedThisFrame; i++)
            {
                Step(0.05f);
            }
        }

        /// <summary>Releases every control and clears the statuses, control states and raw touch data.</summary>
        public static void Reset()
        {
            Array.Clear(s_buttons, 0, s_buttons.Length);
            Array.Clear(s_sensors, 0, s_sensors.Length);
            Array.Clear(GetArray<SwitchStatus>("_btnStatusInPreviousFrame"), 0, ButtonCount);
            Array.Clear(GetArray<SwitchStatus>("_btnStatusInThisFrame"), 0, ButtonCount);
            Array.Clear(GetArray<SwitchStatus>("_sensorStatusInPreviousFrame"), 0, SensorCount);
            Array.Clear(GetArray<SwitchStatus>("_sensorStatusInThisFrame"), 0, SensorCount);
            var buttonStates = (Array)typeof(InputManager).GetField("_buttonControlStates", StaticFlags)!.GetValue(null);
            var sensorStates = (Array)typeof(InputManager).GetField("_sensorControlStates", StaticFlags)!.GetValue(null);
            Array.Clear(buttonStates, 0, buttonStates.Length);
            Array.Clear(sensorStates, 0, sensorStates.Length);
            var raw = (Memory<bool>)typeof(InputManager).GetField("_sensorStates", StaticFlags)!.GetValue(null);
            raw.Span.Clear();
        }

        /// <summary>Sets one raw touch panel value as reported by <see cref="InputManager.GetTouchPanelRawData"/>.</summary>
        /// <param name="index">The raw sensor index.</param>
        /// <param name="value">The raw state.</param>
        public static void SetRawTouch(int index, bool value)
        {
            var raw = (Memory<bool>)typeof(InputManager).GetField("_sensorStates", StaticFlags)!.GetValue(null);
            raw.Span[index] = value;
        }

        /// <summary>Reads one of the input manager's static status arrays.</summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="name">The field name.</param>
        /// <returns>The live array.</returns>
        private static T[] GetArray<T>(string name)
        {
            return (T[])typeof(InputManager).GetField(name, StaticFlags)!.GetValue(null);
        }
    }
}
