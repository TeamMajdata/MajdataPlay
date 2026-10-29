using System;
using MajdataPlay.Rendering;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Rendering
{
    /// <summary>Verifies the frame start planning, queue draining and refresh period re-measurement of <see cref="VSyncFramePacer"/>.</summary>
    public class VSyncFramePacerTests
    {
        /// <summary>A timestamp frequency of one tick per microsecond.</summary>
        private const long TicksPerSecond = 1_000_000;

        /// <summary>The refresh period of a 50 Hz display, in microseconds.</summary>
        private const long Period50Hz = 20_000;

        /// <summary>The 50 Hz period lengthened by the default margin and rounded up.</summary>
        private const long Interval50Hz = 20_020;

        /// <summary>Creates a pacer configured for 50 Hz whose initial queue drain has already happened at <paramref name="start"/>.</summary>
        /// <param name="start">The start timestamp of the first frame.</param>
        /// <returns>The pacer.</returns>
        private static VSyncFramePacer CreateRunningPacer(long start)
        {
            var pacer = new VSyncFramePacer(TicksPerSecond);
            pacer.SetRefreshRate(50);
            var drain = pacer.GetWaitTicks(start);
            pacer.OnFrameStarted(start, drain);
            return pacer;
        }

        /// <summary>Checks that invalid constructor arguments are rejected.</summary>
        [Test]
        public void Constructor_RejectsInvalidArguments()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new VSyncFramePacer(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VSyncFramePacer(TicksPerSecond, -0.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VSyncFramePacer(TicksPerSecond, double.NaN));
        }

        /// <summary>Checks that an unconfigured pacer never waits.</summary>
        [Test]
        public void Unconfigured_NeverWaits()
        {
            var pacer = new VSyncFramePacer(TicksPerSecond);

            Assert.That(pacer.IsConfigured, Is.False);
            Assert.That(pacer.GetWaitTicks(1_000), Is.EqualTo(0));
            Assert.That(pacer.OnFrameStarted(1_000, 0), Is.False);
            Assert.That(pacer.GetWaitTicks(2_000), Is.EqualTo(0));
        }

        /// <summary>Checks that only valid, changed refresh rates are applied and that the margin lengthens the interval.</summary>
        [Test]
        public void SetRefreshRate_AppliesMarginAndIgnoresInvalidOrUnchangedRates()
        {
            var pacer = new VSyncFramePacer(TicksPerSecond);

            Assert.That(pacer.SetRefreshRate(0), Is.False);
            Assert.That(pacer.SetRefreshRate(double.NaN), Is.False);
            Assert.That(pacer.SetRefreshRate(double.PositiveInfinity), Is.False);
            Assert.That(pacer.SetRefreshRate(50), Is.True);
            Assert.That(pacer.IntervalTicks, Is.EqualTo(Interval50Hz));
            Assert.That(pacer.SetRefreshRate(50), Is.False, "an unchanged rate keeps the running state");
            Assert.That(pacer.SetRefreshRate(100), Is.True);
            Assert.That(pacer.IntervalTicks, Is.EqualTo(10_010));
        }

        /// <summary>Checks that the first frame waits several intervals to empty an already filled queue, once.</summary>
        [Test]
        public void FirstFrame_DrainsTheQueueOnce()
        {
            var pacer = new VSyncFramePacer(TicksPerSecond);
            pacer.SetRefreshRate(50);

            Assert.That(pacer.GetWaitTicks(0), Is.EqualTo(Interval50Hz * VSyncFramePacer.DrainFrameCount));
            Assert.That(pacer.GetWaitTicks(0), Is.EqualTo(0), "without a planned start the next frame starts at once");
        }

        /// <summary>Checks that the wait fills the rest of the interval after the previous frame start.</summary>
        [Test]
        public void Wait_FillsTheRestOfTheInterval()
        {
            var pacer = CreateRunningPacer(100_000);

            Assert.That(pacer.GetWaitTicks(105_000), Is.EqualTo(Interval50Hz - 5_000));
            Assert.That(pacer.GetWaitTicks(100_000 + Interval50Hz + 1), Is.EqualTo(0), "a late frame starts at once");
        }

        /// <summary>Checks that a slightly late frame keeps the phase so the average rate stays on the interval.</summary>
        [Test]
        public void LateFrame_KeepsThePhase()
        {
            var pacer = CreateRunningPacer(100_000);

            pacer.OnFrameStarted(100_000 + Interval50Hz + 300, 0);

            Assert.That(pacer.GetWaitTicks(100_000 + Interval50Hz + 1_000), Is.EqualTo(Interval50Hz - 1_000));
        }

        /// <summary>Checks that after a long stall the next start is planned from the actual start without a catch-up burst.</summary>
        [Test]
        public void LongStall_PlansFromTheActualStart()
        {
            var pacer = CreateRunningPacer(100_000);
            var stalledStart = 100_000 + 5 * Interval50Hz;

            pacer.OnFrameStarted(stalledStart, 0);

            Assert.That(pacer.GetWaitTicks(stalledStart), Is.EqualTo(Interval50Hz));
        }

        /// <summary>Checks that restarting drains the queue again.</summary>
        [Test]
        public void Restart_DrainsAgain()
        {
            var pacer = CreateRunningPacer(100_000);

            pacer.Restart();

            Assert.That(pacer.GetWaitTicks(110_000), Is.EqualTo(Interval50Hz * VSyncFramePacer.DrainFrameCount));
        }

        /// <summary>
        /// Checks that frames which keep starting without a wait at a period slightly longer than the interval reveal
        /// a display slower than reported: the measured period is adopted and the queue drained.
        /// </summary>
        [Test]
        public void SlowerDisplay_RemeasuresThePeriodAndDrains()
        {
            const long displayPeriod = 20_030;
            var pacer = CreateRunningPacer(0);
            var remeasured = false;

            for (var i = 1; i <= VSyncFramePacer.CalibrationFrameCount; i++)
            {
                remeasured = pacer.OnFrameStarted(i * displayPeriod, 0);
            }

            Assert.That(remeasured, Is.True);
            Assert.That(pacer.IntervalTicks, Is.EqualTo(20_051), "20 030 plus 0.1 % rounded up");
            Assert.That(pacer.GetWaitTicks(0), Is.EqualTo(pacer.IntervalTicks * VSyncFramePacer.DrainFrameCount));
        }

        /// <summary>Checks that a game slower than the display does not change the interval.</summary>
        [Test]
        public void OverloadedGame_KeepsTheInterval()
        {
            var pacer = CreateRunningPacer(0);

            for (var i = 1; i <= VSyncFramePacer.CalibrationFrameCount * 2; i++)
            {
                Assert.That(pacer.OnFrameStarted(i * 2 * Period50Hz, 0), Is.False);
            }

            Assert.That(pacer.IntervalTicks, Is.EqualTo(Interval50Hz));
        }

        /// <summary>Checks that a frame that waited interrupts the run of frames used for re-measurement.</summary>
        [Test]
        public void WaitedFrame_ResetsTheMeasurementRun()
        {
            const long displayPeriod = 20_030;
            var pacer = CreateRunningPacer(0);
            var frame = 0L;

            for (var i = 0; i < VSyncFramePacer.CalibrationFrameCount - 1; i++)
            {
                Assert.That(pacer.OnFrameStarted(++frame * displayPeriod, 0), Is.False);
            }
            Assert.That(pacer.OnFrameStarted(++frame * displayPeriod, 10), Is.False);
            for (var i = 0; i < VSyncFramePacer.CalibrationFrameCount - 1; i++)
            {
                Assert.That(pacer.OnFrameStarted(++frame * displayPeriod, 0), Is.False);
            }

            Assert.That(pacer.IntervalTicks, Is.EqualTo(Interval50Hz));
        }
    }
}
