using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;

#nullable enable
namespace MajdataPlay
{
    internal sealed class FPSMonitor : MajComponent
    {
        private const int AVG_FPS_SAMPLE_COUNT = 120;
        private const int LOW_FPS_SAMPLE_COUNT = 600;

        // --- Avg FPS ---
        private uint _avgFPSIndex;
        private uint _avgFPSSampleCount;
        private long _totalFrameTimeTicks;
        private readonly long[] _avgFPSData = new long[AVG_FPS_SAMPLE_COUNT];

        // --- 1% Low FPS ---
        private uint _lowFPSIndex;
        private uint _lowFPSSampleCount;
        private readonly long[] _lowFPSData = new long[LOW_FPS_SAMPLE_COUNT];
        private readonly long[] _lowSortBuffer = new long[LOW_FPS_SAMPLE_COUNT];

        private TimeSpan _lastUpdateTiming = TimeSpan.Zero;
        private IComparer<long> _lowFrameComparer = new LowFrameComparer();

        internal double AverageFPS
        {
            get
            {
                if (_avgFPSSampleCount == 0 || _totalFrameTimeTicks <= 0)
                {
                    return 0;
                }

                var averageFrameTime = (TimeSpan.FromTicks(_totalFrameTimeTicks) / _avgFPSSampleCount).TotalSeconds;
                return averageFrameTime <= 0 ? 0 : 1 / averageFrameTime;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            _lastUpdateTiming = MajTimeline.UnscaledTime;
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        private void LateUpdate()
        {
            var currentTime = MajTimeline.UnscaledTime;
            var delta = currentTime - _lastUpdateTiming;
            AddSample(delta.Ticks);
            _lastUpdateTiming = currentTime;
        }

        internal bool TryGetOnePercentLowFPS(out double fps)
        {
            if (_lowFPSSampleCount < LOW_FPS_SAMPLE_COUNT)
            {
                fps = 0;
                return false;
            }

            Array.Copy(_lowFPSData, _lowSortBuffer, LOW_FPS_SAMPLE_COUNT);
            Array.Sort(_lowSortBuffer, _lowFrameComparer);

            var totalLowFrameTime = 0L;
            var sampleCount = Math.Max(1, (int)(LOW_FPS_SAMPLE_COUNT * 0.01));

            for (var i = 0; i < sampleCount; i++)
            {
                totalLowFrameTime += _lowSortBuffer[i];
            }

            var averageLowFrameTime = (TimeSpan.FromTicks(totalLowFrameTime) / sampleCount).TotalSeconds;
            fps = averageLowFrameTime <= 0 ? 0 : 1 / averageLowFrameTime;
            return true;
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddSample(in long frameTicks)
        {
            // ------ Refresh Average FPS Buffer ------
            if (_avgFPSIndex >= AVG_FPS_SAMPLE_COUNT)
            {
                _avgFPSIndex = 0;
            }
            if (_avgFPSSampleCount < AVG_FPS_SAMPLE_COUNT)
            {
                _avgFPSSampleCount++;
            }

            ref var lastAvgSample = ref _avgFPSData[_avgFPSIndex++];
            _totalFrameTimeTicks -= lastAvgSample;
            _totalFrameTimeTicks += frameTicks;
            lastAvgSample = frameTicks;

            // ------ Refresh 1% Low FPS Buffer ------
            if (_lowFPSIndex >= LOW_FPS_SAMPLE_COUNT)
            {
                _lowFPSIndex = 0;
            }
            if (_lowFPSSampleCount < LOW_FPS_SAMPLE_COUNT)
            {
                _lowFPSSampleCount++;
            }

            _lowFPSData[_lowFPSIndex++] = frameTicks;
        }

        private class LowFrameComparer : IComparer<long>
        {
            public int Compare(long x, long y)
            {
                return y.CompareTo(x);
            }
        }
    }
}
