using System;

#nullable enable
namespace MajdataPlay.Rendering
{
    /// <summary>
    /// Computes how long the main thread should wait at the start of each frame so that frames are produced slightly
    /// slower than the display refreshes. With a FIFO (VSync) swapchain this keeps the presentation queue empty, so a
    /// frame is no longer rendered several refresh intervals before it is shown.
    /// </summary>
    /// <remarks>
    /// All times are <see cref="System.Diagnostics.Stopwatch"/> timestamps. Because frames are produced slower than the
    /// display, the display shows one frame twice about every 1 / (refresh rate × margin) frames.
    /// </remarks>
    internal sealed class VSyncFramePacer
    {
        /// <summary>The default fraction by which the frame interval exceeds the refresh period.</summary>
        internal const double DefaultMarginRatio = 0.001;

        /// <summary>The number of consecutive frames without a wait after which the refresh period is measured.</summary>
        internal const int CalibrationFrameCount = 120;

        /// <summary>The largest fraction by which a measured period may exceed the reported period to be adopted.</summary>
        internal const double CalibrationTolerance = 0.01;

        /// <summary>The number of frame intervals waited once to empty an already filled presentation queue.</summary>
        internal const int DrainFrameCount = 3;

        /// <summary>The timestamp frequency in ticks per second.</summary>
        private readonly long _ticksPerSecond;

        /// <summary>The fraction by which the frame interval exceeds the refresh period.</summary>
        private readonly double _marginRatio;

        /// <summary>The refresh rate set by <see cref="SetRefreshRate"/>, or zero before it is set.</summary>
        private double _refreshRate;

        /// <summary>The refresh period derived from <see cref="_refreshRate"/>.</summary>
        private long _reportedPeriodTicks;

        /// <summary>The planned start timestamp of the next frame; valid when <see cref="_hasDeadline"/> is set.</summary>
        private long _nextFrameStart;

        /// <summary>Whether <see cref="_nextFrameStart"/> holds a planned frame start.</summary>
        private bool _hasDeadline;

        /// <summary>Whether the next frame waits <see cref="DrainFrameCount"/> intervals to empty the queue.</summary>
        private bool _drainPending;

        /// <summary>The number of consecutive frames that started without waiting.</summary>
        private int _unthrottledFrames;

        /// <summary>The start timestamp of the first frame in the current run of frames without waiting.</summary>
        private long _unthrottledRunStart;

        /// <summary>Creates a pacer.</summary>
        /// <param name="ticksPerSecond">The timestamp frequency in ticks per second.</param>
        /// <param name="marginRatio">The fraction by which the frame interval exceeds the refresh period.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="ticksPerSecond"/> is not positive, or <paramref name="marginRatio"/> is negative or not finite.</exception>
        public VSyncFramePacer(long ticksPerSecond, double marginRatio = DefaultMarginRatio)
        {
            if (ticksPerSecond <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
            }
            if (marginRatio < 0 || double.IsNaN(marginRatio) || double.IsInfinity(marginRatio))
            {
                throw new ArgumentOutOfRangeException(nameof(marginRatio));
            }

            _ticksPerSecond = ticksPerSecond;
            _marginRatio = marginRatio;
        }

        /// <summary>Gets the target time between frame starts, or zero before a refresh rate is set.</summary>
        public long IntervalTicks { get; private set; }

        /// <summary>Gets whether a refresh rate has been set and frames are paced.</summary>
        public bool IsConfigured => IntervalTicks > 0;

        /// <summary>Sets the display refresh rate and restarts pacing when it changed.</summary>
        /// <param name="refreshRate">The refresh rate in hertz.</param>
        /// <returns>Whether the rate was valid and differed from the current one.</returns>
        public bool SetRefreshRate(double refreshRate)
        {
            if (refreshRate <= 0 || double.IsNaN(refreshRate) || double.IsInfinity(refreshRate)
                || refreshRate == _refreshRate)
            {
                return false;
            }

            _refreshRate = refreshRate;
            _reportedPeriodTicks = (long)Math.Round(_ticksPerSecond / refreshRate);
            IntervalTicks = WithMargin(_reportedPeriodTicks);
            Restart();
            return true;
        }

        /// <summary>Forgets the planned frame start and empties the presentation queue on the next frame.</summary>
        public void Restart()
        {
            _hasDeadline = false;
            _drainPending = true;
            _unthrottledFrames = 0;
        }

        /// <summary>Gets how long to wait before starting the frame.</summary>
        /// <param name="now">The current timestamp.</param>
        /// <returns>The ticks to wait, zero when the frame should start immediately or pacing is not configured.</returns>
        public long GetWaitTicks(long now)
        {
            if (!IsConfigured)
            {
                return 0;
            }
            if (_drainPending)
            {
                _drainPending = false;
                return IntervalTicks * DrainFrameCount;
            }
            if (!_hasDeadline)
            {
                return 0;
            }

            return Math.Max(0, _nextFrameStart - now);
        }

        /// <summary>Plans the next frame start after the current frame started.</summary>
        /// <param name="frameStart">The timestamp at which the frame started, after any wait.</param>
        /// <param name="waitedTicks">The ticks returned by <see cref="GetWaitTicks"/> for this frame.</param>
        /// <returns>Whether the refresh period was re-measured, which happens when frames keep starting late because the reported refresh rate is too high.</returns>
        public bool OnFrameStarted(long frameStart, long waitedTicks)
        {
            if (!IsConfigured)
            {
                return false;
            }

            // Keep the phase while frames are on time; after a long stall plan from the actual start instead of
            // producing a burst of frames to catch up.
            if (!_hasDeadline || frameStart - _nextFrameStart >= IntervalTicks)
            {
                _nextFrameStart = frameStart + IntervalTicks;
                _hasDeadline = true;
            }
            else
            {
                _nextFrameStart += IntervalTicks;
            }

            return TrackUnthrottledFrames(frameStart, waitedTicks);
        }

        /// <summary>
        /// Detects that the display, not the pacer, limits the frame rate. When the measured period is only slightly
        /// longer than the interval, the reported refresh rate is too high and the queue fills again; the measured
        /// period is then adopted and the queue emptied.
        /// </summary>
        /// <param name="frameStart">The timestamp at which the frame started.</param>
        /// <param name="waitedTicks">The ticks waited before the frame.</param>
        /// <returns>Whether the interval was re-measured.</returns>
        private bool TrackUnthrottledFrames(long frameStart, long waitedTicks)
        {
            if (waitedTicks > 0)
            {
                _unthrottledFrames = 0;
                return false;
            }
            if (_unthrottledFrames == 0)
            {
                _unthrottledRunStart = frameStart;
            }

            _unthrottledFrames++;
            if (_unthrottledFrames < CalibrationFrameCount)
            {
                return false;
            }

            var measuredPeriod = (frameStart - _unthrottledRunStart) / (_unthrottledFrames - 1);
            _unthrottledFrames = 0;

            // A much longer period means the game cannot keep up with the display; pacing would not help then.
            if (measuredPeriod <= IntervalTicks || measuredPeriod > _reportedPeriodTicks * (1 + CalibrationTolerance))
            {
                return false;
            }

            IntervalTicks = WithMargin(measuredPeriod);
            _hasDeadline = false;
            _drainPending = true;
            return true;
        }

        /// <summary>Adds the margin to a period.</summary>
        /// <param name="periodTicks">The period in ticks.</param>
        /// <returns>The period lengthened by the margin, rounded up.</returns>
        private long WithMargin(long periodTicks)
        {
            return periodTicks + (long)Math.Ceiling(periodTicks * _marginRatio);
        }
    }
}
