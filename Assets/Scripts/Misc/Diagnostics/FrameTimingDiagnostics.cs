using Cysharp.Text;
using System;
using System.Globalization;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Diagnostics
{
    /// <summary>
    /// Captures <see cref="FrameTimingManager"/> data every frame, averages it over fixed windows and logs each window
    /// together with the graphics API and presentation settings, so render queue depth can be compared across APIs.
    /// </summary>
    internal sealed class FrameTimingDiagnostics
    {
        /// <summary>The length of one measurement window.</summary>
        internal static readonly TimeSpan WindowLength = TimeSpan.FromSeconds(5);

        /// <summary>The overlay text shown before the first window completes.</summary>
        internal const string WaitingOverlaySummary = "frame timing: measuring...";

        /// <summary>The overlay text shown when the build does not report frame timings.</summary>
        internal const string UnsupportedOverlaySummary = "frame timing: unavailable (Frame Timing Stats disabled)";

        /// <summary>The buffer that receives the latest frame timing.</summary>
        private readonly FrameTiming[] _timings = new FrameTiming[1];

        /// <summary>The frames of the current window.</summary>
        private readonly FrameLatencyStatistics _statistics = new();

        /// <summary>The start timestamp of the last accepted frame, used to skip frames that were already counted.</summary>
        private ulong _lastFrameStartTimestamp;

        /// <summary>The unscaled time at which the current window started, or null before the first frame.</summary>
        private TimeSpan? _windowStart;

        /// <summary>Gets the averages of the last completed window.</summary>
        public FrameLatencySnapshot LastSnapshot { get; private set; }

        /// <summary>Gets whether a window has been completed since the diagnostics were enabled.</summary>
        public bool HasSnapshot => LastSnapshot.SampleCount > 0;

        /// <summary>Gets the one-line overlay summary of the last completed window, or a placeholder before the first one.</summary>
        public string OverlaySummary { get; private set; } = WaitingOverlaySummary;

        /// <summary>Gets whether the engine reports frame timings in this build.</summary>
        public static bool IsSupported => FrameTimingManager.IsFeatureEnabled();

        /// <summary>Captures the frame timing of this frame and completes the window when it is long enough.</summary>
        /// <param name="now">The current unscaled time.</param>
        /// <returns>Whether a window was completed and logged during this call. Always false when the build does not report frame timings.</returns>
        public bool Tick(TimeSpan now)
        {
            if (!IsSupported)
            {
                OverlaySummary = UnsupportedOverlaySummary;
                return false;
            }

            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _timings) > 0)
            {
                AddTiming(_timings[0], FrameTimingManager.GetCpuTimerFrequency());
            }

            return TryCompleteWindow(now);
        }

        /// <summary>Adds one frame timing unless the same frame was already added.</summary>
        /// <param name="timing">The frame timing.</param>
        /// <param name="cpuTimerFrequency">The frequency of the CPU timestamps in ticks per second.</param>
        /// <returns>Whether the frame was added.</returns>
        internal bool AddTiming(in FrameTiming timing, ulong cpuTimerFrequency)
        {
            // GetLatestTimings reports the same frame again when the engine has no newer complete frame yet.
            if (timing.frameStartTimestamp == _lastFrameStartTimestamp)
            {
                return false;
            }
            if (!_statistics.AddSample(timing, cpuTimerFrequency))
            {
                return false;
            }

            _lastFrameStartTimestamp = timing.frameStartTimestamp;
            return true;
        }

        /// <summary>Publishes and logs the current window once it covers <see cref="WindowLength"/>.</summary>
        /// <param name="now">The current unscaled time.</param>
        /// <returns>Whether a window was completed.</returns>
        internal bool TryCompleteWindow(TimeSpan now)
        {
            if (_windowStart is not TimeSpan windowStart)
            {
                _windowStart = now;
                return false;
            }
            if (now - windowStart < WindowLength)
            {
                return false;
            }

            _windowStart = now;
            if (_statistics.SampleCount == 0)
            {
                return false;
            }

            LastSnapshot = _statistics.GetSnapshot();
            OverlaySummary = FormatOverlaySummary(LastSnapshot);
            _statistics.Reset();
            MajDebug.LogInfo(nameof(FrameTimingDiagnostics), FormatLogMessage(LastSnapshot));
            return true;
        }

        /// <summary>Discards the current window and the last snapshot.</summary>
        public void Reset()
        {
            _statistics.Reset();
            _windowStart = null;
            _lastFrameStartTimestamp = 0;
            LastSnapshot = default;
            OverlaySummary = WaitingOverlaySummary;
        }

        /// <summary>Formats the compact one-line summary of a window for the runtime info overlay.</summary>
        /// <param name="snapshot">The averages of the window.</param>
        /// <returns>The summary, starting with the graphics API.</returns>
        internal static string FormatOverlaySummary(in FrameLatencySnapshot snapshot)
        {
            return ZString.Format("{0}   start->present {1:F1}ms   start->GPU {2}   wait {3:F1}ms   GPU {4:F1}ms",
                SystemInfo.graphicsDeviceType, snapshot.StartToPresentMs, FormatOptionalMs(snapshot.StartToGpuCompleteMs),
                snapshot.PresentWaitMs, snapshot.GpuMs);
        }

        /// <summary>Formats the log message of one window.</summary>
        /// <param name="snapshot">The averages of the window.</param>
        /// <returns>The message, including the graphics API and the presentation settings.</returns>
        internal static string FormatLogMessage(in FrameLatencySnapshot snapshot)
        {
            return ZString.Format(
                "API={0} VSync={1} MaxQueuedFrames={2} TargetFPS={3} Mode={4} Refresh={5:F2}Hz Frames={6} | " +
                "main={7:F2}ms presentWait={8:F2}ms render={9:F2}ms GPU={10:F2}ms | " +
                "start->present={11:F2}ms start->GPUComplete={12} (max {13})",
                SystemInfo.graphicsDeviceType, QualitySettings.vSyncCount, QualitySettings.maxQueuedFrames,
                Application.targetFrameRate, Screen.fullScreenMode, Screen.currentResolution.refreshRateRatio.value,
                snapshot.SampleCount, snapshot.MainThreadMs, snapshot.PresentWaitMs, snapshot.RenderThreadMs,
                snapshot.GpuMs, snapshot.StartToPresentMs, FormatOptionalMs(snapshot.StartToGpuCompleteMs),
                FormatOptionalMs(snapshot.MaxStartToGpuCompleteMs));
        }

        /// <summary>Formats a millisecond value that may be unavailable.</summary>
        /// <param name="milliseconds">The value, or a negative number when unavailable.</param>
        /// <returns>The value with two decimals and the "ms" unit, or "n/a".</returns>
        private static string FormatOptionalMs(double milliseconds)
        {
            return milliseconds < 0 ? "n/a" : milliseconds.ToString("F2", CultureInfo.InvariantCulture) + "ms";
        }
    }
}
