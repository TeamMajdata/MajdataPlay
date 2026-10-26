using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MajdataPlay.IO;
using MajdataPlay.Timer;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>A manually advanced clock installed as <see cref="MajTimeline.TimeProvider"/>.</summary>
    internal sealed class ManualTimeProvider : ITimeProvider
    {
        /// <summary>Gets the provider kind reported to the timeline.</summary>
        public BuiltInTimeProvider Type => BuiltInTimeProvider.Stopwatch;

        /// <summary>Gets or sets the current time in ticks.</summary>
        public long Ticks { get; set; }

        /// <summary>Does nothing; the test advances <see cref="Ticks"/> itself.</summary>
        public void OnPreUpdate()
        {
        }

        /// <summary>Moves the clock forward.</summary>
        /// <param name="time">The amount to add.</param>
        public void Advance(TimeSpan time)
        {
            Ticks += time.Ticks;
        }
    }

    /// <summary>
    /// Installs scripted devices into the static <see cref="InputManager"/> and restores its devices, states,
    /// subscribers, debounce configuration and the timeline clock when disposed.
    /// </summary>
    internal sealed class InputManagerScope : IDisposable
    {
        /// <summary>The binding flags of static members.</summary>
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        /// <summary>The nested button ring facade type.</summary>
        private static readonly Type s_buttonRingType = typeof(InputManager).GetNestedType("ButtonRing", BindingFlags.NonPublic)!;

        /// <summary>The nested touch panel facade type.</summary>
        private static readonly Type s_touchPanelType = typeof(InputManager).GetNestedType("TouchPanel", BindingFlags.NonPublic)!;

        /// <summary>The button ring device before the scope.</summary>
        private readonly object? _originalButtonRing;

        /// <summary>The touch panel device before the scope.</summary>
        private readonly object? _originalTouchPanel;

        /// <summary>The time provider before the scope.</summary>
        private readonly ITimeProvider _originalTimeProvider;

        /// <summary>The instance id mapping before the scope.</summary>
        private readonly object? _originalMapping;

        /// <summary>Installs a fake device for both input capabilities and a manual clock.</summary>
        public InputManagerScope()
        {
            _originalButtonRing = PrivateMember.GetStaticField<object?>(s_buttonRingType, "_device");
            _originalTouchPanel = PrivateMember.GetStaticField<object?>(s_touchPanelType, "_device");
            _originalTimeProvider = MajTimeline.TimeProvider;
            _originalMapping = PrivateMember.GetStaticField<object?>(typeof(InputManager), "_instanceID2SensorIndexMappingTable");
            Device = new FakeInputDevice();
            Clock = new ManualTimeProvider { Ticks = TimeSpan.FromSeconds(100).Ticks };
            ResetState();
            PrivateMember.SetStaticField(s_buttonRingType, "_device", Device);
            PrivateMember.SetStaticField(s_touchPanelType, "_device", Device);
            MajTimeline.TimeProvider = Clock;
        }

        /// <summary>Gets the scripted device used for buttons and touch.</summary>
        public FakeInputDevice Device { get; }

        /// <summary>Gets the manual clock.</summary>
        public ManualTimeProvider Clock { get; }

        /// <summary>Gets the nested button ring facade type.</summary>
        public static Type ButtonRingType => s_buttonRingType;

        /// <summary>Gets the nested touch panel facade type.</summary>
        public static Type TouchPanelType => s_touchPanelType;

        /// <summary>Enables or disables debouncing with the given thresholds.</summary>
        /// <param name="enabled">Whether both debouncers are enabled.</param>
        /// <param name="threshold">The minimum interval between accepted transitions.</param>
        public static void SetDebounce(bool enabled, TimeSpan threshold)
        {
            PrivateMember.SetStaticField(typeof(InputManager), "_isBtnDebounceEnabled", enabled);
            PrivateMember.SetStaticField(typeof(InputManager), "_isSensorDebounceEnabled", enabled);
            PrivateMember.SetStaticField(typeof(InputManager), "_btnDebounceThresholdMs", threshold);
            PrivateMember.SetStaticField(typeof(InputManager), "_sensorDebounceThresholdMs", threshold);
        }

        /// <summary>Restores the devices, clock and mapping and clears every input state.</summary>
        public void Dispose()
        {
            ResetState();
            PrivateMember.SetStaticField(s_buttonRingType, "_device", _originalButtonRing);
            PrivateMember.SetStaticField(s_touchPanelType, "_device", _originalTouchPanel);
            PrivateMember.SetStaticField(typeof(InputManager), "_instanceID2SensorIndexMappingTable", _originalMapping);
            MajTimeline.TimeProvider = _originalTimeProvider;
        }

        /// <summary>Clears every button, sensor, frame snapshot, queue, subscriber and debounce setting.</summary>
        private static void ResetState()
        {
            InputManager.ClearAllSubscriber();
            SetDebounce(false, TimeSpan.Zero);
            foreach (var name in new[] { "_buttons", "_sensors" })
            {
                var memory = PrivateMember.GetStaticField<object>(typeof(InputManager), name);
                var array = (IEnumerable)memory.GetType().GetMethod("ToArray")!.Invoke(memory, null)!;
                foreach (var item in array)
                {
                    PrivateMember.SetProperty(item, "State", SwitchStatus.Off);
                }
            }
            foreach (var name in new[]
            {
                "_btnStatusInPreviousFrame", "_btnStatusInThisFrame", "_buttonControlStates", "_btnLastTriggerTimes",
                "_sensorStatusInPreviousFrame", "_sensorStatusInThisFrame", "_sensorControlStates", "_sensorLastTriggerTimes",
            })
            {
                var array = PrivateMember.GetStaticField<Array>(typeof(InputManager), name);
                Array.Clear(array, 0, array.Length);
            }
            foreach (var name in new[] { "_buttonStates", "_isBtnHadOn", "_isBtnHadOff", "_textInputSuppressedKeys" })
            {
                var array = PrivateMember.GetStaticField<Array>(s_buttonRingType, name);
                Array.Clear(array, 0, array.Length);
            }
            foreach (var name in new[] { "_sensorStates", "_isSensorHadOn", "_isSensorHadOff" })
            {
                var array = PrivateMember.GetStaticField<Array>(s_touchPanelType, name);
                Array.Clear(array, 0, array.Length);
            }
            PrivateMember.GetStaticField<Memory<bool>>(typeof(InputManager), "_sensorStates").Span.Clear();
            DrainQueue("_touchPanelInputBuffer");
            DrainQueue("_buttonRingInputBuffer");
        }

        /// <summary>Removes every pending report from an input queue.</summary>
        /// <param name="name">The queue field name.</param>
        private static void DrainQueue(string name)
        {
            var queue = PrivateMember.GetStaticField<System.Collections.Concurrent.ConcurrentQueue<InputDeviceReport>>(typeof(InputManager), name);
            while (queue.TryDequeue(out _))
            {
            }
        }
    }
}
