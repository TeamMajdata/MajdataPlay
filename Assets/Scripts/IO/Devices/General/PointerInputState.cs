using System;
using System.Collections.Generic;
using UnityEngine;

namespace MajdataPlay.IO
{
    /// <summary>Aggregates pointer contacts without losing separate clicks from multiple fingers.</summary>
    internal sealed class PointerInputState
    {
        readonly InputStateBuffer _buttons = new(12);
        readonly InputStateBuffer _sensors = new(35);
        readonly bool[] _buttonStates = new bool[12];
        readonly bool[] _sensorStates = new bool[35];
        readonly int[] _buttonClickedCount = new int[8];
        readonly int[] _sensorClickedCount = new int[34];
        readonly Dictionary<int, ulong> _positions = new(32);
        readonly HashSet<int> _seenPointers = new();
        readonly List<int> _removedPointers = new(32);

        public ReadOnlySpan<int> ButtonClickedCount => _buttonClickedCount;
        public ReadOnlySpan<int> SensorClickedCount => _sensorClickedCount;

        public void BeginFrame()
        {
            _buttonStates.AsSpan().Clear();
            _sensorStates.AsSpan().Clear();
            _buttonClickedCount.AsSpan().Clear();
            _sensorClickedCount.AsSpan().Clear();
            _seenPointers.Clear();
        }

        public void AddPointer(int id, Vector3 position, float radius, bool ended, Camera camera)
        {
            _seenPointers.Add(id);
            _positions.TryGetValue(id, out var previous);
            var current = 0UL;
            var sensorOnly = (previous & (1UL << 63)) != 0;
            ScreenTouchMapper.PositionToSensorState(_sensorStates, _buttonStates, camera,
                position, radius, ref current, ref sensorOnly);
            CountClicks(previous, current, ScreenTouchMapper.UseOuterTouchAsSensor || sensorOnly);

            // Ended touches still contribute their final position for this frame.
            if (ended)
                _positions.Remove(id);
            else
                _positions[id] = current;
        }

        void CountClicks(ulong previous, ulong current, bool sensorOnly)
        {
            for (var i = 0; i < _sensorClickedCount.Length; i++)
            {
                var sensorBit = 1UL << (i + 12);
                var previousOn = (previous & sensorBit) != 0;
                var currentOn = (current & sensorBit) != 0;
                if (sensorOnly && i < 8)
                {
                    var buttonBit = 1UL << i;
                    previousOn |= (previous & buttonBit) != 0;
                    currentOn |= (current & buttonBit) != 0;
                }
                var clicked = !previousOn && currentOn;
                if (!sensorOnly && i < 8)
                    clicked &= (previous & (1UL << i)) == 0;
                if (clicked)
                    _sensorClickedCount[i]++;
            }
            if (sensorOnly) return;

            for (var i = 0; i < _buttonClickedCount.Length; i++)
            {
                var buttonBit = 1UL << i;
                if ((previous & buttonBit) == 0 && (current & buttonBit) != 0 &&
                    (previous & (1UL << (i + 12))) == 0)
                    _buttonClickedCount[i]++;
            }
        }

        public void EndFrame()
        {
            _removedPointers.Clear();
            foreach (var id in _positions.Keys)
                if (!_seenPointers.Contains(id)) _removedPointers.Add(id);
            foreach (var id in _removedPointers)
                _positions.Remove(id);

            _buttons.Publish(_buttonStates);
            _sensors.Publish(_sensorStates);
            _buttons.OnPreUpdate();
            _sensors.OnPreUpdate();
        }

        public void ReadButtons(Span<bool> states, Span<bool> hadOn, Span<bool> hadOff) =>
            _buttons.CopyTo(states, hadOn, hadOff);
        public void ReadTouchPanel(Span<bool> states, Span<bool> hadOn, Span<bool> hadOff) =>
            _sensors.CopyTo(states, hadOn, hadOff);
        public bool IsButtonCurrentlyOn(int index) => _buttons.IsCurrentlyOn(index);
        public bool IsSensorCurrentlyOn(int index) => _sensors.IsCurrentlyOn(index);
    }
}
