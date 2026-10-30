using Cysharp.Text;
using MajdataPlay.Diagnostics;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using MajdataPlay.Platform.Win32.Threading;
#endif
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using MajdataPlay.Settings;
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
    /// On Windows players the MajdataPlayVulkanPacing layer provides waiting until the previous frame has been shown
    /// (VK_KHR_present_wait, see <see cref="PresentWaitPacer"/>), which empties that queue, and then the driver's
    /// low-latency mode (VK_NV_low_latency2 or VK_AMD_anti_lag, see <see cref="DriverLowLatencyPacer"/>), which may
    /// delay the frame further. Each is used when available. The driver's mode alone does not remove the VSync
    /// queue: on an RTX 3060 Laptop GPU its sleep stayed near zero with FIFO presentation. Without either, frames
    /// are produced slightly slower than the display (see <see cref="VSyncFramePacer"/>), which also keeps the
    /// queue empty and samples input late.
    /// </summary>
    internal static class LowLatencyVSync
    {
        /// <summary>Identifies the frame start system in the player loop.</summary>
        private sealed class PlayerLoopMarker
        {
        }

        /// <summary>Identifies the frame end system in the player loop.</summary>
        private sealed class FrameEndMarker
        {
        }

        /// <summary>Names how frames are paced.</summary>
        private enum PacingMode
        {
            /// <summary>Frames are not paced.</summary>
            None,

            /// <summary>The pacing layer paces frames: present waiting, the driver's low-latency mode, or both.</summary>
            Layer,

            /// <summary>Frames are produced slightly slower than the display refreshes.</summary>
            Timer,
        }

        /// <summary>The remaining wait below which the thread spins instead of sleeping, one millisecond.</summary>
        private static readonly long SpinThresholdTicks = Stopwatch.Frequency / 1000;

        /// <summary>The pause below which the thread spins instead of sleeping, 0.2 milliseconds.</summary>
        private static readonly long MinSleepTicks = Stopwatch.Frequency / 5000;

        /// <summary>The delegate inserted at the start of the player loop.</summary>
        private static readonly PlayerLoopSystem.UpdateFunction FrameStartDelegate = OnFrameStart;

        /// <summary>The delegate inserted at the end of the player loop.</summary>
        private static readonly PlayerLoopSystem.UpdateFunction FrameEndDelegate = OnFrameEnd;

        /// <summary>The timer pacing state.</summary>
        private static readonly VSyncFramePacer Pacer = new(Stopwatch.Frequency);

        /// <summary>Whether the player loop systems have been inserted.</summary>
        private static bool _installed;

        /// <summary>How frames were paced during the previous frame.</summary>
        private static PacingMode _mode;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        /// <summary>The number of consecutive present waits that failed or timed out before switching to timer pacing.</summary>
        private const int PresentWaitFailureLimit = 30;

        /// <summary>The driver low-latency state, or null before it is created or after it was given up.</summary>
        private static DriverLowLatencyPacer? _driverPacer;

        /// <summary>The extension name of the driver low-latency backend, for logs.</summary>
        private static string _driverBackendName = string.Empty;

        /// <summary>Whether the driver low-latency mode was found unavailable or given up for this session.</summary>
        private static bool _driverUnavailable;

        /// <summary>The present wait state, or null before it is created or after it was given up.</summary>
        private static PresentWaitPacer? _presentWaitPacer;

        /// <summary>Whether present waiting was found unavailable or given up for this session.</summary>
        private static bool _presentWaitUnavailable;

        /// <summary>The number of consecutive present waits that failed or timed out.</summary>
        private static int _presentWaitFailures;

        /// <summary>The last swapchain generation seen, or zero before the first.</summary>
        private static ulong _swapchainGeneration;

        /// <summary>Whether the source of presentation ids has been logged.</summary>
        private static bool _presentIdSourceLogged;

        /// <summary>The layer backends used during the previous frame, for logging changes.</summary>
        private static string _layerBackends = string.Empty;
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

        /// <summary>
        /// Inserts the pacing step at the start of the player loop, before time and input are updated, and the frame
        /// end step at the end of the player loop.
        /// </summary>
        public static void Install()
        {
            if (_installed)
            {
                return;
            }

            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (!InsertIntoPhase(ref loop, typeof(TimeUpdate), typeof(PlayerLoopMarker), FrameStartDelegate, true) ||
                !InsertIntoPhase(ref loop, typeof(PostLateUpdate), typeof(FrameEndMarker), FrameEndDelegate, false))
            {
                MajDebug.LogError(nameof(LowLatencyVSync), "Failed to insert the pacing steps into the player loop");
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
            TrackSwapchain();
            if (TryPaceWithLayer(refreshRate))
            {
                return;
            }
            PaceWithTimer(refreshRate);
        }

        /// <summary>Tells the driver's low-latency mode that the main thread finished the frame.</summary>
        private static void OnFrameEnd()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (_mode == PacingMode.Layer)
            {
                _driverPacer?.EndFrame();
            }
#endif
        }

        /// <summary>Logs swapchain recreations, such as window resizes and fullscreen toggles, and the id source.</summary>
        private static void TrackSwapchain()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            var generation = VulkanPacingLayer.GetSwapchainGeneration();
            if (generation != _swapchainGeneration)
            {
                if (_swapchainGeneration != 0)
                {
                    MajDebug.LogInfo(nameof(LowLatencyVSync), ZString.Format(
                        "Swapchain changed (generation {0}); pacing follows the new swapchain", generation));
                }
                _swapchainGeneration = generation;
            }
            if (!_presentIdSourceLogged)
            {
                var source = VulkanPacingLayer.GetPresentIdSource();
                if (source != 0)
                {
                    _presentIdSourceLogged = true;
                    MajDebug.LogInfo(nameof(LowLatencyVSync), source switch
                    {
                        2 => "Presentation ids come from Unity",
                        3 => "Presentation ids come from Unity and are replaced with frame ids for the driver's latency markers",
                        _ => "Presentation ids come from the pacing layer",
                    });
                }
            }
#endif
        }

        /// <summary>
        /// Waits until the previous frame has been shown and then lets the driver's low-latency mode delay the frame,
        /// using whichever of the two the pacing layer offers.
        /// </summary>
        /// <param name="refreshRate">The display refresh rate in hertz.</param>
        /// <returns>Whether the layer paced this frame; false means the caller paces with the timer.</returns>
        private static bool TryPaceWithLayer(double refreshRate)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            var allowed = MajEnv.Settings.Debug.LowLatencyVSyncBackend;
            var presentWait = allowed != LowLatencyVSyncBackendOption.Timer && EnsurePresentWaitPacer();
            var driver = allowed == LowLatencyVSyncBackendOption.Auto && EnsureDriverPacer();
            if (!presentWait && !driver)
            {
                return false;
            }

            var backends = presentWait && driver
                ? "VK_KHR_present_wait, then " + _driverBackendName
                : presentWait ? "VK_KHR_present_wait" : _driverBackendName;
            if (_mode != PacingMode.Layer || backends != _layerBackends)
            {
                _mode = PacingMode.Layer;
                _layerBackends = backends;
                MajDebug.LogInfo(nameof(LowLatencyVSync), ZString.Format(
                    "Frame pacing started: each frame waits for the pacing layer ({0})", backends));
            }

            var waitStart = Stopwatch.GetTimestamp();
            var refreshPeriodTicks = RefreshPeriodTicks(refreshRate);
            if (presentWait)
            {
                WaitForPresentation(refreshPeriodTicks);
            }
            if (driver)
            {
                DriveLowLatency(refreshPeriodTicks);
            }
            LastWaitMilliseconds = TicksToMilliseconds(Stopwatch.GetTimestamp() - waitStart);
            return true;
#else
            return false;
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        /// <summary>Waits until the previous frame has been shown, and gives present waiting up after repeated failures.</summary>
        /// <param name="refreshPeriodTicks">The display refresh period in timestamp ticks.</param>
        private static void WaitForPresentation(long refreshPeriodTicks)
        {
            var outcome = _presentWaitPacer!.WaitForPreviousFrame(refreshPeriodTicks);
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
                            "Present waiting {0} {1} times in a row (last error {2}); giving it up",
                            outcome == PresentWaitOutcome.TimedOut ? "timed out" : "failed", _presentWaitFailures,
                            _presentWaitPacer.LastError));
                        _presentWaitPacer = null;
                        _presentWaitUnavailable = true;
                    }
                    break;
            }
        }

        /// <summary>Lets the driver's low-latency mode delay the frame, and gives it up after repeated failures.</summary>
        /// <param name="refreshPeriodTicks">The display refresh period in timestamp ticks.</param>
        private static void DriveLowLatency(long refreshPeriodTicks)
        {
            _driverPacer!.BeginFrame(refreshPeriodTicks, Stopwatch.Frequency);
            if (_driverPacer.HasGivenUp)
            {
                MajDebug.LogWarning(nameof(LowLatencyVSync), ZString.Format(
                    "{0} failed repeatedly (last result {1}); giving it up", _driverBackendName, _driverPacer.LastError));
                _driverPacer = null;
                _driverUnavailable = true;
            }
        }
#endif

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        /// <summary>Creates the driver low-latency state once the native pacing layer reports an active backend.</summary>
        /// <returns>Whether the driver's low-latency mode is available.</returns>
        private static bool EnsureDriverPacer()
        {
            if (_driverPacer is not null)
            {
                return true;
            }
            if (_driverUnavailable)
            {
                return false;
            }

            var backend = VulkanPacingLayer.GetLowLatencyBackend(out var status);
            if (backend == 0)
            {
                _driverUnavailable = true;
                MajDebug.LogInfo(nameof(LowLatencyVSync), ZString.Format(
                    "Driver low-latency mode unavailable ({0})", VulkanPacingLayer.DescribeLowLatencyStatus(status)));
                return false;
            }

            _driverBackendName = VulkanPacingLayer.DescribeBackend(backend);
            _driverPacer = new DriverLowLatencyPacer(new VulkanPacingLayer.Driver());
            return true;
        }

        /// <summary>Creates the present wait state once the native pacing layer reports that present waiting is enabled.</summary>
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

            var status = VulkanPacingLayer.GetPresentWaitStatus();
            if (status != 0)
            {
                _presentWaitUnavailable = true;
                MajDebug.LogInfo(nameof(LowLatencyVSync), ZString.Format(
                    "Present waiting unavailable ({0})", VulkanPacingLayer.DescribePresentWaitStatus(status)));
                return false;
            }

            _presentWaitPacer = new PresentWaitPacer(new VulkanPresentWaitSource(), Stopwatch.GetTimestamp, Pause,
                Stopwatch.Frequency);
            return true;
        }

        /// <summary>Converts a refresh rate into a refresh period.</summary>
        /// <param name="refreshRate">The display refresh rate in hertz.</param>
        /// <returns>The refresh period in timestamp ticks; 60 Hz when the rate is unknown.</returns>
        private static long RefreshPeriodTicks(double refreshRate)
        {
            return refreshRate > 0 ? (long)(Stopwatch.Frequency / refreshRate) : Stopwatch.Frequency / 60;
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

        /// <summary>Inserts a pacing step as the first or last system of a top-level player loop phase.</summary>
        /// <param name="root">The player loop root.</param>
        /// <param name="phaseType">The phase type.</param>
        /// <param name="markerType">The type that identifies the step.</param>
        /// <param name="update">The step's update function.</param>
        /// <param name="atStart">Whether the step runs first in the phase; otherwise it runs last.</param>
        /// <returns>Whether the phase was found; true also when the step is already present.</returns>
        private static bool InsertIntoPhase(ref PlayerLoopSystem root, Type phaseType, Type markerType,
            PlayerLoopSystem.UpdateFunction update, bool atStart)
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
                    if (system.type == markerType)
                    {
                        return true;
                    }
                }

                var newSystems = new PlayerLoopSystem[oldSystems.Length + 1];
                var step = new PlayerLoopSystem
                {
                    type = markerType,
                    updateDelegate = update
                };
                if (atStart)
                {
                    newSystems[0] = step;
                    Array.Copy(oldSystems, 0, newSystems, 1, oldSystems.Length);
                }
                else
                {
                    Array.Copy(oldSystems, 0, newSystems, 0, oldSystems.Length);
                    newSystems[oldSystems.Length] = step;
                }
                phase.subSystemList = newSystems;
                phases[i] = phase;
                root.subSystemList = phases;
                return true;
            }

            return false;
        }
    }
}
