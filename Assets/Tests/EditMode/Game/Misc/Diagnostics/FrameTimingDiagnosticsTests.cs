using System;
using MajdataPlay.Diagnostics;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Diagnostics
{
    /// <summary>Verifies the averaging of <see cref="FrameLatencyStatistics"/> and the windowing of <see cref="FrameTimingDiagnostics"/>.</summary>
    public class FrameTimingDiagnosticsTests
    {
        /// <summary>A CPU timer frequency of one tick per microsecond, so one millisecond is 1000 ticks.</summary>
        private const ulong TicksPerSecond = 1_000_000;

        /// <summary>Creates a frame timing with timestamps relative to <paramref name="start"/>.</summary>
        /// <param name="start">The frame start timestamp in ticks.</param>
        /// <param name="presentAfterMs">The delay from the frame start to the Present call.</param>
        /// <param name="completeAfterMs">The delay from the frame start to GPU completion, or a negative value for no completion timestamp.</param>
        /// <returns>The frame timing.</returns>
        private static FrameTiming CreateTiming(ulong start, double presentAfterMs, double completeAfterMs)
        {
            return new FrameTiming
            {
                frameStartTimestamp = start,
                cpuTimePresentCalled = start + (ulong)(presentAfterMs * 1000),
                cpuTimeFrameComplete = completeAfterMs < 0 ? 0 : start + (ulong)(completeAfterMs * 1000),
                cpuMainThreadFrameTime = 2,
                cpuMainThreadPresentWaitTime = 10,
                cpuRenderThreadFrameTime = 3,
                gpuFrameTime = 4,
            };
        }

        /// <summary>Checks that empty statistics report no samples and unavailable GPU completion.</summary>
        [Test]
        public void Statistics_Empty_ReportsNothing()
        {
            var snapshot = new FrameLatencyStatistics().GetSnapshot();

            Assert.That(snapshot.SampleCount, Is.EqualTo(0));
            Assert.That(snapshot.StartToGpuCompleteMs, Is.LessThan(0));
        }

        /// <summary>Checks that the thread times are averaged and the timestamps are converted to milliseconds.</summary>
        [Test]
        public void Statistics_AveragesTimesAndTimestampDifferences()
        {
            var statistics = new FrameLatencyStatistics();

            Assert.That(statistics.AddSample(CreateTiming(1_000, 20, 30), TicksPerSecond), Is.True);
            Assert.That(statistics.AddSample(CreateTiming(100_000, 40, 70), TicksPerSecond), Is.True);
            var snapshot = statistics.GetSnapshot();

            Assert.That(snapshot.SampleCount, Is.EqualTo(2));
            Assert.That(snapshot.MainThreadMs, Is.EqualTo(2).Within(1e-9));
            Assert.That(snapshot.PresentWaitMs, Is.EqualTo(10).Within(1e-9));
            Assert.That(snapshot.RenderThreadMs, Is.EqualTo(3).Within(1e-9));
            Assert.That(snapshot.GpuMs, Is.EqualTo(4).Within(1e-9));
            Assert.That(snapshot.StartToPresentMs, Is.EqualTo(30).Within(1e-9));
            Assert.That(snapshot.StartToGpuCompleteMs, Is.EqualTo(50).Within(1e-9));
            Assert.That(snapshot.MaxStartToGpuCompleteMs, Is.EqualTo(70).Within(1e-9));
        }

        /// <summary>Checks that frames without a GPU completion timestamp count everywhere except the GPU completion average.</summary>
        [Test]
        public void Statistics_MissingGpuCompletion_IsExcludedFromCompletionAverage()
        {
            var statistics = new FrameLatencyStatistics();

            statistics.AddSample(CreateTiming(1_000, 20, -1), TicksPerSecond);
            var withoutCompletion = statistics.GetSnapshot();
            statistics.AddSample(CreateTiming(100_000, 20, 40), TicksPerSecond);
            var withCompletion = statistics.GetSnapshot();

            Assert.That(withoutCompletion.SampleCount, Is.EqualTo(1));
            Assert.That(withoutCompletion.StartToGpuCompleteMs, Is.LessThan(0));
            Assert.That(withCompletion.SampleCount, Is.EqualTo(2));
            Assert.That(withCompletion.StartToGpuCompleteMs, Is.EqualTo(40).Within(1e-9));
        }

        /// <summary>Checks that invalid frames are rejected and that reset discards everything.</summary>
        [Test]
        public void Statistics_RejectsInvalidFramesAndResets()
        {
            var statistics = new FrameLatencyStatistics();

            Assert.That(statistics.AddSample(CreateTiming(1_000, 20, 30), 0), Is.False, "zero timer frequency");
            Assert.That(statistics.AddSample(CreateTiming(0, 20, 30), TicksPerSecond), Is.False, "no start timestamp");
            var presentBeforeStart = CreateTiming(10_000, 0, 30);
            presentBeforeStart.cpuTimePresentCalled = 5_000;
            Assert.That(statistics.AddSample(presentBeforeStart, TicksPerSecond), Is.False, "Present before start");
            Assert.That(statistics.SampleCount, Is.EqualTo(0));

            statistics.AddSample(CreateTiming(1_000, 20, 30), TicksPerSecond);
            statistics.Reset();

            Assert.That(statistics.SampleCount, Is.EqualTo(0));
            Assert.That(statistics.GetSnapshot().StartToGpuCompleteMs, Is.LessThan(0));
        }

        /// <summary>Checks that the same frame reported twice is only counted once.</summary>
        [Test]
        public void Diagnostics_SkipsRepeatedFrames()
        {
            var diagnostics = new FrameTimingDiagnostics();
            var timing = CreateTiming(1_000, 20, 30);

            Assert.That(diagnostics.AddTiming(timing, TicksPerSecond), Is.True);
            Assert.That(diagnostics.AddTiming(timing, TicksPerSecond), Is.False);
            Assert.That(diagnostics.AddTiming(CreateTiming(50_000, 20, 30), TicksPerSecond), Is.True);
        }

        /// <summary>Checks that a snapshot is published only after a full window with samples, and that reset clears it.</summary>
        [Test]
        public void Diagnostics_PublishesSnapshotPerWindow()
        {
            var diagnostics = new FrameTimingDiagnostics();
            var start = TimeSpan.FromSeconds(100);

            Assert.That(diagnostics.TryCompleteWindow(start), Is.False, "the first call starts the window");
            Assert.That(diagnostics.TryCompleteWindow(start + FrameTimingDiagnostics.WindowLength), Is.False,
                "a window without samples publishes nothing");

            diagnostics.AddTiming(CreateTiming(1_000, 20, 30), TicksPerSecond);
            diagnostics.AddTiming(CreateTiming(100_000, 40, 50), TicksPerSecond);
            var windowStart = start + FrameTimingDiagnostics.WindowLength;
            Assert.That(diagnostics.TryCompleteWindow(windowStart + TimeSpan.FromSeconds(1)), Is.False);
            Assert.That(diagnostics.HasSnapshot, Is.False);
            Assert.That(diagnostics.OverlaySummary, Is.EqualTo(FrameTimingDiagnostics.WaitingOverlaySummary));

            Assert.That(diagnostics.TryCompleteWindow(windowStart + FrameTimingDiagnostics.WindowLength), Is.True);
            Assert.That(diagnostics.HasSnapshot, Is.True);
            Assert.That(diagnostics.LastSnapshot.SampleCount, Is.EqualTo(2));
            Assert.That(diagnostics.LastSnapshot.StartToPresentMs, Is.EqualTo(30).Within(1e-9));
            Assert.That(diagnostics.OverlaySummary, Does.Contain("start->present 30.0ms"));

            diagnostics.Reset();
            Assert.That(diagnostics.HasSnapshot, Is.False);
            Assert.That(diagnostics.OverlaySummary, Is.EqualTo(FrameTimingDiagnostics.WaitingOverlaySummary));
        }

        /// <summary>Checks that the log message names the API settings and marks unavailable GPU completion.</summary>
        [Test]
        public void Diagnostics_LogMessage_ContainsSettingsAndValues()
        {
            var snapshot = new FrameLatencySnapshot(3, 1, 2, 3, 4, 25.5, -1, -1);

            var message = FrameTimingDiagnostics.FormatLogMessage(snapshot);

            Assert.That(message, Does.Contain("API=").And.Contain("VSync=").And.Contain("MaxQueuedFrames=")
                .And.Contain("Frames=3").And.Contain("start->GPUComplete=n/a"));
        }

        /// <summary>Checks that the overlay summary shows the averages and marks unavailable GPU completion.</summary>
        [Test]
        public void Diagnostics_OverlaySummary_ContainsValues()
        {
            var snapshot = new FrameLatencySnapshot(3, 1, 2.25, 3, 4.5, 25.5, -1, -1);

            var summary = FrameTimingDiagnostics.FormatOverlaySummary(snapshot);

            Assert.That(summary, Does.Contain(SystemInfo.graphicsDeviceType.ToString())
                .And.Contain("start->present 25.5ms").And.Contain("start->GPU n/a").And.Contain("GPU 4.5ms"));
        }

        /// <summary>Checks that capturing frame timings through the engine does not throw in the Editor.</summary>
        [Test]
        public void Diagnostics_Tick_RunsAgainstTheEngine()
        {
            var diagnostics = new FrameTimingDiagnostics();

            Assert.DoesNotThrow(() => diagnostics.Tick(TimeSpan.Zero));
            Assert.DoesNotThrow(() => diagnostics.Tick(FrameTimingDiagnostics.WindowLength));
        }
    }
}
