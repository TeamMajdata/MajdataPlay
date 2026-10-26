using System;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;

#nullable enable
namespace MajdataPlay
{
    /// <summary>Tracks rolling average FPS and the average FPS of the slowest one percent of frames.</summary>
    internal sealed class FPSMonitor : MajComponent
    {
        /// <summary>The number of recent frames used for average FPS.</summary>
        private const int AverageFpsSampleCount = 120;

        /// <summary>The number of recent frames used for one percent low FPS.</summary>
        private const int LowFpsSampleCount = 600;

        /// <summary>The number of slowest frames included in one percent low FPS.</summary>
        private const int OnePercentLowSampleCount = LowFpsSampleCount / 100;

        // --- Avg FPS ---
        /// <summary>The next average frame time slot to overwrite.</summary>
        private uint _avgFPSIndex;

        /// <summary>The number of populated average frame time slots.</summary>
        private uint _avgFPSSampleCount;

        /// <summary>The sum of the frame time ticks in the average window.</summary>
        private long _totalFrameTimeTicks;

        /// <summary>The rolling average frame time window.</summary>
        private readonly long[] _avgFPSData = new long[AverageFpsSampleCount];

        // --- 1% Low FPS ---
        /// <summary>The next low FPS frame time slot to overwrite.</summary>
        private uint _lowFPSIndex;

        /// <summary>The number of populated low FPS frame time slots.</summary>
        private uint _lowFPSSampleCount;

        /// <summary>The rolling frame time window used to select the slowest one percent.</summary>
        private readonly long[] _lowFPSData = new long[LowFpsSampleCount];

        /// <summary>The unscaled timeline time at the previous sample.</summary>
        private TimeSpan _lastUpdateTiming = TimeSpan.Zero;

        /// <summary>Gets the reciprocal of the mean frame time in the average window, or zero without positive elapsed time.</summary>
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

        /// <summary>Gets the reciprocal of the mean duration of the slowest six of the most recent 600 frames.</summary>
        /// <param name="fps">The one percent low FPS, or zero before the window is full or without positive elapsed time.</param>
        /// <returns>Whether all 600 frame times have been sampled.</returns>
        internal bool TryGetOnePercentLowFPS(out double fps)
        {
            if (_lowFPSSampleCount < LowFpsSampleCount)
            {
                fps = 0;
                return false;
            }

            // Keep only the six largest durations in descending order: O(n * 6) and no heap allocations.
            Span<long> slowestFrameTicks = stackalloc long[OnePercentLowSampleCount];
            var selectedCount = 0;
            foreach (var frameTicks in _lowFPSData)
            {
                var insertionIndex = selectedCount;
                if (selectedCount == slowestFrameTicks.Length)
                {
                    insertionIndex--;
                    if (frameTicks <= slowestFrameTicks[insertionIndex])
                    {
                        continue;
                    }
                }
                else
                {
                    selectedCount++;
                }

                while ((insertionIndex > 0) && (frameTicks > slowestFrameTicks[insertionIndex - 1]))
                {
                    slowestFrameTicks[insertionIndex] = slowestFrameTicks[insertionIndex - 1];
                    insertionIndex--;
                }
                slowestFrameTicks[insertionIndex] = frameTicks;
            }

            var totalLowFrameTime = 0L;
            foreach (var frameTicks in slowestFrameTicks)
            {
                totalLowFrameTime += frameTicks;
            }

            fps = totalLowFrameTime <= 0 ? 0 : (OnePercentLowSampleCount * (double)TimeSpan.TicksPerSecond) / totalLowFrameTime;
            return true;
        }

        /// <summary>Adds one frame duration to both rolling windows.</summary>
        /// <param name="frameTicks">The elapsed unscaled frame time in ticks.</param>
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddSample(in long frameTicks)
        {
            // ------ Refresh Average FPS Buffer ------
            if (_avgFPSIndex >= AverageFpsSampleCount)
            {
                _avgFPSIndex = 0;
            }
            if (_avgFPSSampleCount < AverageFpsSampleCount)
            {
                _avgFPSSampleCount++;
            }

            ref var lastAvgSample = ref _avgFPSData[_avgFPSIndex++];
            _totalFrameTimeTicks -= lastAvgSample;
            _totalFrameTimeTicks += frameTicks;
            lastAvgSample = frameTicks;

            // ------ Refresh 1% Low FPS Buffer ------
            if (_lowFPSIndex >= LowFpsSampleCount)
            {
                _lowFPSIndex = 0;
            }
            if (_lowFPSSampleCount < LowFpsSampleCount)
            {
                _lowFPSSampleCount++;
            }

            _lowFPSData[_lowFPSIndex++] = frameTicks;
        }
    }
}
