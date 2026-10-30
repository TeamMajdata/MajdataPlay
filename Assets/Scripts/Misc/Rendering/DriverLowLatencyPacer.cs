using System;

#nullable enable
namespace MajdataPlay.Rendering
{
    /// <summary>Lets the GPU driver's low-latency mode delay the start of each frame.</summary>
    internal interface ILowLatencyDriver
    {
        /// <summary>Starts a frame before input is sampled; the driver may block until the frame should start.</summary>
        /// <param name="timeoutNanoseconds">The longest time to block.</param>
        /// <returns>One of the <see cref="DriverLowLatencyPacer"/> frame results, or a negative error code.</returns>
        int BeginFrame(ulong timeoutNanoseconds);

        /// <summary>Ends the main-thread part of the frame started last.</summary>
        /// <returns>One of the <see cref="DriverLowLatencyPacer"/> frame results.</returns>
        int EndFrame();
    }

    /// <summary>Describes how a driver frame start ended.</summary>
    internal enum DriverFrameOutcome
    {
        /// <summary>The driver started the frame, possibly after delaying it.</summary>
        Done,

        /// <summary>The driver had nothing to do, for example while no swapchain exists.</summary>
        Skipped,

        /// <summary>The driver did not release the frame within the timeout.</summary>
        TimedOut,

        /// <summary>The driver reported an error or the backend is no longer active.</summary>
        Failed,
    }

    /// <summary>
    /// Drives the GPU driver's low-latency mode (VK_NV_low_latency2 or VK_AMD_anti_lag) at the start and end of each
    /// frame, and gives it up after repeated failures so that the caller can fall back to another backend.
    /// </summary>
    internal sealed class DriverLowLatencyPacer
    {
        /// <summary>The result for a frame the driver started.</summary>
        internal const int FrameDone = 1;

        /// <summary>The result for a frame the driver had nothing to do for.</summary>
        internal const int FrameSkipped = 0;

        /// <summary>The result for a frame the driver did not release within the timeout.</summary>
        internal const int FrameTimedOut = 2;

        /// <summary>The result when no driver low-latency backend is active.</summary>
        internal const int FrameInactive = -2;

        /// <summary>The number of consecutive failed or timed-out frames after which the backend is given up.</summary>
        internal const int FailureLimit = 30;

        /// <summary>The driver.</summary>
        private readonly ILowLatencyDriver _driver;

        /// <summary>The number of consecutive failed or timed-out frames.</summary>
        private int _failures;

        /// <summary>Creates a pacer.</summary>
        /// <param name="driver">The driver.</param>
        public DriverLowLatencyPacer(ILowLatencyDriver driver)
        {
            _driver = driver;
        }

        /// <summary>Gets whether the backend was given up; the caller should use another backend.</summary>
        public bool HasGivenUp { get; private set; }

        /// <summary>Gets the result code of the last failed frame.</summary>
        public int LastError { get; private set; }

        /// <summary>Starts a frame through the driver.</summary>
        /// <param name="refreshPeriodTicks">The display refresh period in timestamp ticks, used for the timeout.</param>
        /// <param name="ticksPerSecond">The timestamp frequency in ticks per second.</param>
        /// <returns>How the frame start ended.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="ticksPerSecond"/> is not positive.</exception>
        public DriverFrameOutcome BeginFrame(long refreshPeriodTicks, long ticksPerSecond)
        {
            if (ticksPerSecond <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
            }
            if (HasGivenUp)
            {
                return DriverFrameOutcome.Failed;
            }

            // Three refresh periods, but at least 10 ms.
            var timeoutSeconds = Math.Max(0.01, Math.Max(0, refreshPeriodTicks) * 3.0 / ticksPerSecond);
            var result = _driver.BeginFrame((ulong)(timeoutSeconds * 1e9));
            switch (result)
            {
                case FrameDone:
                    _failures = 0;
                    return DriverFrameOutcome.Done;
                case FrameSkipped:
                    return DriverFrameOutcome.Skipped;
                case FrameTimedOut:
                    RecordFailure(result);
                    return DriverFrameOutcome.TimedOut;
                default:
                    RecordFailure(result);
                    if (result == FrameInactive)
                    {
                        HasGivenUp = true;
                    }
                    return DriverFrameOutcome.Failed;
            }
        }

        /// <summary>Ends the main-thread part of the frame started last, unless the backend was given up.</summary>
        public void EndFrame()
        {
            if (!HasGivenUp)
            {
                _driver.EndFrame();
            }
        }

        /// <summary>Counts a failed frame and gives the backend up at <see cref="FailureLimit"/>.</summary>
        /// <param name="result">The frame result.</param>
        private void RecordFailure(int result)
        {
            LastError = result;
            if (++_failures >= FailureLimit)
            {
                HasGivenUp = true;
            }
        }
    }
}
