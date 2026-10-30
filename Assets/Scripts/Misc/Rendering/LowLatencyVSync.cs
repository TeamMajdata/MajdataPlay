using Cysharp.Text;
using MajdataPlay.Diagnostics;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using MajdataPlay.Platform.Win32.Threading;
#endif
using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.Rendering;

#nullable enable
namespace MajdataPlay.Rendering
{
    /// <summary>
    /// Paces the main thread at the very start of each frame when VSync is on with the Vulkan backend on desktop.
    /// Unity's Vulkan FIFO swapchain lets the game run until every swapchain image is queued, so each frame is
    /// rendered about three refresh intervals before it is shown; D3D11 limits that queue with a waitable swapchain.
    /// On Windows players whose driver supports VK_KHR_present_wait, each frame waits until the previous frame has
    /// been shown (see <see cref="PresentWaitPacer"/>). Otherwise frames are produced slightly slower than the
    /// display (see <see cref="VSyncFramePacer"/>), which also keeps the queue empty.
    /// </summary>
    internal static class LowLatencyVSync
    {
        /// <summary>Identifies the inserted player loop system.</summary>
        private sealed class PlayerLoopMarker
        {
        }

        /// <summary>Names how frames are paced.</summary>
        private enum PacingMode
        {
            /// <summary>Frames are not paced.</summary>
            None,

            /// <summary>Frames wait until the previous frame has been shown.</summary>
            PresentWait,

            /// <summary>Frames are produced slightly slower than the display refreshes.</summary>
            Timer,
        }

        /// <summary>The remaining wait below which the thread spins instead of sleeping, one millisecond.</summary>
        private static readonly long SpinThresholdTicks = Stopwatch.Frequency / 1000;

        /// <summary>The pause below which the thread spins instead of sleeping, 0.2 milliseconds.</summary>
        private static readonly long MinSleepTicks = Stopwatch.Frequency / 5000;

        /// <summary>The delegate inserted into the player loop.</summary>
        private static readonly PlayerLoopSystem.UpdateFunction FrameStartDelegate = OnFrameStart;

        /// <summary>The timer pacing state.</summary>
        private static readonly VSyncFramePacer Pacer = new(Stopwatch.Frequency);

        /// <summary>Whether the player loop system has been inserted.</summary>
        private static bool _installed;

        /// <summary>How frames were paced during the previous frame.</summary>
        private static PacingMode _mode;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        /// <summary>The number of consecutive present waits that failed or timed out before switching to timer pacing.</summary>
        private const int PresentWaitFailureLimit = 30;

        /// <summary>The present wait state, or null before it is created or after it was given up.</summary>
        private static PresentWaitPacer? _presentWaitPacer;

        /// <summary>Whether present waiting was found unavailable or given up for this session.</summary>
        private static bool _presentWaitUnavailable;

        /// <summary>The number of consecutive present waits that failed or timed out.</summary>
        private static int _presentWaitFailures;
#endif

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        /// <summary>The high-resolution timer used for sleeping, or null when unavailable.</summary>
        private static HighResolutionWaitableTimer? _timer;

        /// <summary>Whether creating <see cref="_timer"/> has been attempted.</summary>
        private static bool _timerCreationAttempted;
#endif

        /// <summary>Gets whether frames are currently paced.</summary>
        public static bool IsActive => _mode != PacingMode.None;

        /// <summary>Gets the time the main thread waited at the start of the current frame, in milliseconds.</summary>
        public static double LastWaitMilliseconds { get; private set; }

        /// <summary>Inserts the pacing step at the start of the player loop, before time and input are updated.</summary>
        public static void Install()
        {
            if (_installed)
            {
                return;
            }

            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (!InsertAtStartOfPhase(ref loop, typeof(TimeUpdate)))
            {
                MajDebug.LogError(nameof(LowLatencyVSync), "Failed to insert the pacing step into the TimeUpdate phase");
                return;
            }

            PlayerLoop.SetPlayerLoop(loop);
            _installed = true;
        }

        /// <summary>Waits until the planned frame start when pacing applies.</summary>
        private static void OnFrameStart()
        {
            if (!ShouldPace())
            {
                if (_mode != PacingMode.None)
                {
                    _mode = PacingMode.None;
                    LastWaitMilliseconds = 0;
                    MajDebug.LogInfo(nameof(LowLatencyVSync), "Frame pacing stopped");
                }
                return;
            }

            var refreshRate = Screen.currentResolution.refreshRateRatio.value;
            if (TryWaitForPresentation(refreshRate))
            {
                return;
            }
            PaceWithTimer(refreshRate);
        }

        /// <summary>Waits until the previous frame has been shown when present waiting is available.</summary>
        /// <param name="refreshRate">The display refresh rate in hertz.</param>
        /// <returns>Whether present waiting paced this frame; false means the caller paces with the timer.</returns>
        private static bool TryWaitForPresentation(double refreshRate)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (!MajEnv.Settings.Debug.LowLatencyVSyncPresentWait || !EnsurePresentWaitPacer())
            {
                return false;
            }
            if (_mode != PacingMode.PresentWait)
            {
                _mode = PacingMode.PresentWait;
                MajDebug.LogInfo(nameof(LowLatencyVSync),
                    "Frame pacing started: each frame waits until the previous frame is shown (VK_KHR_present_wait)");
            }

            var waitStart = Stopwatch.GetTimestamp();
            var refreshPeriodTicks = refreshRate > 0 ? (long)(Stopwatch.Frequency / refreshRate) : Stopwatch.Frequency / 60;
            var outcome = _presentWaitPacer!.WaitForPreviousFrame(refreshPeriodTicks);
            LastWaitMilliseconds = TicksToMilliseconds(Stopwatch.GetTimestamp() - waitStart);
            switch (outcome)
            {
                case PresentWaitOutcome.Presented:
                    _presentWaitFailures = 0;
                    break;
                case PresentWaitOutcome.TimedOut:
                case PresentWaitOutcome.Failed:
                    if (++_presentWaitFailures >= PresentWaitFailureLimit)
                    {
                        MajDebug.LogWarning(nameof(LowLatencyVSync), ZString.Format(
                            "Present waiting {0} {1} times in a row (last error {2}); switching to timer pacing",
                            outcome == PresentWaitOutcome.TimedOut ? "timed out" : "failed", _presentWaitFailures,
                            _presentWaitPacer.LastError));
                        _presentWaitPacer = null;
                        _presentWaitUnavailable = true;
                        _mode = PacingMode.None;
                    }
                    break;
            }
            return true;
#else
            return false;
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        /// <summary>Creates the present wait state once the bridge reports that present waiting is enabled.</summary>
        /// <returns>Whether present waiting is available.</returns>
        private static bool EnsurePresentWaitPacer()
        {
            if (_presentWaitPacer is not null)
            {
                return true;
            }
            if (_presentWaitUnavailable)
            {
                return false;
            }

            var status = VulkanPresentWaitSource.GetStatus();
            if (status != VulkanPresentWaitSource.StatusEnabled)
            {
                _presentWaitUnavailable = true;
                MajDebug.LogInfo(nameof(LowLatencyVSync), ZString.Format(
                    "Present waiting unavailable ({0}); using timer pacing", VulkanPresentWaitSource.DescribeStatus(status)));
                return false;
            }

            _presentWaitPacer = new PresentWaitPacer(new VulkanPresentWaitSource(), Stopwatch.GetTimestamp, Pause,
                Stopwatch.Frequency);
            return true;
        }
#endif

        /// <summary>Produces frames slightly slower than the display refreshes.</summary>
        /// <param name="refreshRate">The display refresh rate in hertz.</param>
        private static void PaceWithTimer(double refreshRate)
        {
            if (Pacer.SetRefreshRate(refreshRate) || _mode != PacingMode.Timer)
            {
                Pacer.Restart();
                MajDebug.LogInfo(nameof(LowLatencyVSync), ZString.Format(
                    "Frame pacing started: refresh {0:F3}Hz, frame interval {1:F3}ms",
                    refreshRate, TicksToMilliseconds(Pacer.IntervalTicks)));
            }
            _mode = PacingMode.Timer;

            var waitStart = Stopwatch.GetTimestamp();
            var waitTicks = Pacer.GetWaitTicks(waitStart);
            if (waitTicks > 0)
            {
                WaitUntil(waitStart + waitTicks);
            }

            var frameStart = Stopwatch.GetTimestamp();
            LastWaitMilliseconds = TicksToMilliseconds(frameStart - waitStart);
            if (Pacer.OnFrameStarted(frameStart, waitTicks))
            {
                MajDebug.LogInfo(nameof(LowLatencyVSync), ZString.Format(
                    "The display refreshes slower than reported; frame interval re-measured as {0:F3}ms",
                    TicksToMilliseconds(Pacer.IntervalTicks)));
            }
        }

        /// <summary>Checks whether frames should be paced in the current frame.</summary>
        /// <returns>Whether the option is on, VSync is on and the Vulkan backend renders on a desktop platform.</returns>
        private static bool ShouldPace()
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            return false;
#else
            return MajEnv.Settings.Debug.LowLatencyVSync
                && QualitySettings.vSyncCount > 0
                && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan;
#endif
        }

        /// <summary>Blocks the main thread until a timestamp, sleeping first and spinning for the last millisecond.</summary>
        /// <param name="deadline">The timestamp to wait for.</param>
        private static void WaitUntil(long deadline)
        {
            while (true)
            {
                var remaining = deadline - Stopwatch.GetTimestamp();
                if (remaining <= 0)
                {
                    return;
                }
                if (remaining <= SpinThresholdTicks || !TrySleep(remaining - SpinThresholdTicks))
                {
                    Thread.SpinWait(64);
                }
            }
        }

        /// <summary>Pauses the main thread for about the given time, spinning for pauses too short to sleep.</summary>
        /// <param name="ticks">The time to pause in timestamp ticks.</param>
        private static void Pause(long ticks)
        {
            var deadline = Stopwatch.GetTimestamp() + ticks;
            if (ticks >= MinSleepTicks && TrySleep(ticks))
            {
                return;
            }
            while (Stopwatch.GetTimestamp() < deadline)
            {
                Thread.SpinWait(32);
            }
        }

        /// <summary>Sleeps for about the given time when the platform offers sub-millisecond sleep precision.</summary>
        /// <param name="ticks">The time to sleep in timestamp ticks.</param>
        /// <returns>Whether the thread slept; false means the caller should spin instead.</returns>
        private static bool TrySleep(long ticks)
        {
            var duration = TimeSpan.FromSeconds(ticks / (double)Stopwatch.Frequency);
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (!_timerCreationAttempted)
            {
                _timerCreationAttempted = true;
                if (!HighResolutionWaitableTimer.TryCreate(out _timer))
                {
                    MajDebug.LogWarning(nameof(LowLatencyVSync),
                        "High-resolution waitable timer unavailable; frame pacing spins instead of sleeping");
                }
            }

            // Thread.Sleep follows the 15.6 ms system timer on Windows, too coarse for pacing.
            return _timer is not null && _timer.Wait(duration);
#else
            Thread.Sleep(duration);
            return true;
#endif
        }

        /// <summary>Converts timestamp ticks to milliseconds.</summary>
        /// <param name="ticks">The ticks.</param>
        /// <returns>The duration in milliseconds.</returns>
        private static double TicksToMilliseconds(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }

        /// <summary>Inserts the pacing step as the first system of a top-level player loop phase.</summary>
        /// <param name="root">The player loop root.</param>
        /// <param name="phaseType">The phase type.</param>
        /// <returns>Whether the phase was found; true also when the step is already present.</returns>
        private static bool InsertAtStartOfPhase(ref PlayerLoopSystem root, Type phaseType)
        {
            var phases = root.subSystemList;
            if (phases is null)
            {
                return false;
            }

            for (var i = 0; i < phases.Length; i++)
            {
                var phase = phases[i];
                if (phase.type != phaseType)
                {
                    continue;
                }

                var oldSystems = phase.subSystemList ?? Array.Empty<PlayerLoopSystem>();
                foreach (var system in oldSystems)
                {
                    if (system.type == typeof(PlayerLoopMarker))
                    {
                        return true;
                    }
                }

                var newSystems = new PlayerLoopSystem[oldSystems.Length + 1];
                newSystems[0] = new PlayerLoopSystem
                {
                    type = typeof(PlayerLoopMarker),
                    updateDelegate = FrameStartDelegate
                };
                Array.Copy(oldSystems, 0, newSystems, 1, oldSystems.Length);
                phase.subSystemList = newSystems;
                phases[i] = phase;
                root.subSystemList = phases;
                return true;
            }

            return false;
        }
    }
}
