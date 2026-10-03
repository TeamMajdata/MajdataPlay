#if UNITY_STANDALONE_WIN
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MajdataPlay.IO
{
    /// <summary>Maps exclusive USB touch reports to cabinet sensor states.</summary>
    internal sealed class TouchPanelState
    {
        internal readonly struct TouchUpdate
        {
            public readonly ushort X;
            public readonly ushort Y;
            public readonly int FingerId;
            public readonly bool IsPressed;

            public TouchUpdate(ushort x, ushort y, int fingerId, bool isPressed)
            {
                X = x;
                Y = y;
                FingerId = fingerId;
                IsPressed = isPressed;
            }
        }

        sealed class TouchPoint
        {
            public ulong Mask;
            public long LastUpdateTick;
            public bool IsActive;
        }

        readonly TouchSensorMapper _touchSensorMapper;
        readonly TouchPoint[] _allFingerPoints = new TouchPoint[256];
        readonly InputStateBuffer _states = new(35);
        readonly object _touchLock = new();
        readonly long _touchTimeoutTicks;

        public TouchPanelState(int minX, int minY, int maxX, int maxY, bool flip, int radius,
            float aExtraRadius = 0, float bExtraRadius = 0, float cExtraRadius = 0,
            float dExtraRadius = 0, float eExtraRadius = 0, int timeoutMilliseconds = 20)
        {
            _touchTimeoutTicks = Stopwatch.Frequency * timeoutMilliseconds / 1000;
            _touchSensorMapper = new TouchSensorMapper(minX, minY, maxX, maxY, radius, flip,
                aExtraRadius, bExtraRadius, cExtraRadius, dExtraRadius, eExtraRadius);
            for (var i = 0; i < _allFingerPoints.Length; i++)
                _allFingerPoints[i] = new TouchPoint();
        }

        public void Clear()
        {
            lock (_touchLock)
            {
                foreach (var point in _allFingerPoints)
                    point.IsActive = false;
                _states.Clear();
            }
        }

        public void OnPreUpdate()
        {
            lock (_touchLock)
            {
                var now = Stopwatch.GetTimestamp();
                foreach (var point in _allFingerPoints)
                {
                    if (point.IsActive && now - point.LastUpdateTick > _touchTimeoutTicks)
                        point.IsActive = false;
                }
                PublishState();
                _states.OnPreUpdate();
            }
        }

        public void ReadTouchPanel(Span<bool> states, Span<bool> hadOn, Span<bool> hadOff) =>
            _states.CopyTo(states, hadOn, hadOff);
        public bool IsSensorCurrentlyOn(int index) => _states.IsCurrentlyOn(index);

        void PublishState()
        {
            var mask = ComputeActiveMask();
            Span<bool> states = stackalloc bool[35];
            for (var i = 0; i < states.Length; i++)
                states[i] = (mask & (1UL << i)) != 0;
            _states.Publish(states);
        }

        private void ApplyFinger(TouchUpdate update, long timestamp)
        {
            if (update.FingerId < 0 || update.FingerId >= 256) return;

            var point = _allFingerPoints[update.FingerId];
            if (update.IsPressed)
            {
                point.Mask = _touchSensorMapper.ParseTouchPoint(update.X, update.Y);
                point.IsActive = true;
                point.LastUpdateTick = timestamp;
            }
            else
            {
                point.IsActive = false;
            }
        }

        public void HandleFinger(ushort x, ushort y, int fingerId, bool isPressed)
        {
            if (fingerId < 0 || fingerId >= 256) return;

            lock (_touchLock)
            {
                ApplyFinger(new TouchUpdate(x, y, fingerId, isPressed), Stopwatch.GetTimestamp());
                PublishState();
            }
        }

        private void HandleUpdates(List<TouchUpdate> updates, bool replaceState)
        {
            lock (_touchLock)
            {
                var now = Stopwatch.GetTimestamp();
                if (replaceState)
                {
                    for (var i = 0; i < _allFingerPoints.Length; i++)
                    {
                        _allFingerPoints[i].IsActive = false;
                    }
                }

                for (var i = 0; i < updates.Count; i++)
                {
                    ApplyFinger(updates[i], now);
                }

                PublishState();
            }
        }

        /// <summary>提交一帧完整快照：帧内未出现的触点全部释放。</summary>
        public void HandleFrame(List<TouchUpdate> updates)
            => HandleUpdates(updates, replaceState: true);

        /// <summary>仅提交帧内的释放，保留仍在按下的触点。</summary>
        public void HandleReleases(List<TouchUpdate> updates)
            => HandleUpdates(updates, replaceState: false);

        private ulong ComputeActiveMask()
        {
            var mask = 0UL;
            for (var i = 0; i < _allFingerPoints.Length; i++)
            {
                if (_allFingerPoints[i].IsActive)
                {
                    mask |= _allFingerPoints[i].Mask;
                }
            }
            return mask;
        }

    }
}
#endif
