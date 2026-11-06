using System;
using System.Collections.Generic;
using MajdataPlay.Rendering;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Rendering
{
    /// <summary>Verifies how <see cref="DriverLowLatencyPacer"/> maps driver results and when it gives the backend up.</summary>
    public class DriverLowLatencyPacerTests
    {
        /// <summary>A timestamp frequency of one tick per microsecond.</summary>
        private const long TicksPerSecond = 1_000_000;

        /// <summary>The refresh period of a 60 Hz display, in microseconds.</summary>
        private const long Period60Hz = 16_667;

        /// <summary>A scripted driver.</summary>
        private sealed class FakeDriver : ILowLatencyDriver
        {
            /// <summary>Gets or sets the result of the next frame starts.</summary>
            public int BeginResult { get; set; } = DriverLowLatencyPacer.FrameDone;

            /// <summary>Gets the timeouts passed to <see cref="BeginFrame"/>.</summary>
            public List<ulong> Timeouts { get; } = new();

            /// <summary>Gets the number of <see cref="EndFrame"/> calls.</summary>
            public int Ends { get; private set; }

            /// <inheritdoc/>
            public int BeginFrame(ulong timeoutNanoseconds)
            {
                Timeouts.Add(timeoutNanoseconds);
                return BeginResult;
            }

            /// <inheritdoc/>
            public int EndFrame()
            {
                Ends++;
                return DriverLowLatencyPacer.FrameDone;
            }
        }

        /// <summary>The driver.</summary>
        private FakeDriver _driver = null!;

        /// <summary>The pacer under test.</summary>
        private DriverLowLatencyPacer _pacer = null!;

        /// <summary>Creates the driver and pacer.</summary>
        [SetUp]
        public void SetUp()
        {
            _driver = new FakeDriver();
            _pacer = new DriverLowLatencyPacer(_driver);
        }

        /// <summary>Checks the result mapping and that frame ends reach the driver.</summary>
        [Test]
        public void Results_MapToOutcomes()
        {
            Assert.That(_pacer.BeginFrame(Period60Hz, TicksPerSecond), Is.EqualTo(DriverFrameOutcome.Done));
            _driver.BeginResult = DriverLowLatencyPacer.FrameSkipped;
            Assert.That(_pacer.BeginFrame(Period60Hz, TicksPerSecond), Is.EqualTo(DriverFrameOutcome.Skipped));
            _driver.BeginResult = DriverLowLatencyPacer.FrameTimedOut;
            Assert.That(_pacer.BeginFrame(Period60Hz, TicksPerSecond), Is.EqualTo(DriverFrameOutcome.TimedOut));
            // VK_ERROR_INITIALIZATION_FAILED, an error the backend may recover from.
            _driver.BeginResult = -3;
            Assert.That(_pacer.BeginFrame(Period60Hz, TicksPerSecond), Is.EqualTo(DriverFrameOutcome.Failed));
            Assert.That(_pacer.LastError, Is.EqualTo(-3));

            _pacer.EndFrame();
            Assert.That(_driver.Ends, Is.EqualTo(1));
            Assert.That(_pacer.HasGivenUp, Is.False);
        }

        /// <summary>Checks that the timeout is three refresh periods but at least 10 ms.</summary>
        [Test]
        public void Timeout_IsThreeRefreshPeriodsWithAMinimum()
        {
            _pacer.BeginFrame(Period60Hz, TicksPerSecond);
            _pacer.BeginFrame(1_000, TicksPerSecond);

            Assert.That((double)_driver.Timeouts[0], Is.EqualTo(Period60Hz * 3 * 1000.0).Within(1000));
            Assert.That(_driver.Timeouts[1], Is.EqualTo(10_000_000UL));
        }

        /// <summary>Checks that the backend is given up once failures reach the limit, and that skipped frames do not count.</summary>
        [Test]
        public void RepeatedFailures_GiveTheBackendUp()
        {
            _driver.BeginResult = DriverLowLatencyPacer.FrameSkipped;
            for (var i = 0; i < PacingFailureWindow.WindowSize; i++)
            {
                _pacer.BeginFrame(Period60Hz, TicksPerSecond);
            }
            Assert.That(_pacer.RecentFailures, Is.EqualTo(0), "skipped frames are not judged");

            _driver.BeginResult = DriverLowLatencyPacer.FrameTimedOut;
            for (var i = 0; i < PacingFailureWindow.FailureLimit - 1; i++)
            {
                _pacer.BeginFrame(Period60Hz, TicksPerSecond);
            }
            Assert.That(_pacer.HasGivenUp, Is.False);
            _pacer.BeginFrame(Period60Hz, TicksPerSecond);

            Assert.That(_pacer.HasGivenUp, Is.True);
            Assert.That(_pacer.LastError, Is.EqualTo(DriverLowLatencyPacer.FrameTimedOut));
            var calls = _driver.Timeouts.Count;
            Assert.That(_pacer.BeginFrame(Period60Hz, TicksPerSecond), Is.EqualTo(DriverFrameOutcome.Failed));
            Assert.That(_driver.Timeouts.Count, Is.EqualTo(calls), "a given-up backend is not called again");
            _pacer.EndFrame();
            Assert.That(_driver.Ends, Is.EqualTo(0));
        }

        /// <summary>
        /// Checks that started frames between timeouts no longer keep a mostly failing backend, and its stalls of up to
        /// three refresh periods, alive.
        /// </summary>
        [Test]
        public void TimeoutsBetweenStartedFrames_GiveTheBackendUp()
        {
            for (var i = 0; i < PacingFailureWindow.FailureLimit - 1; i++)
            {
                _driver.BeginResult = DriverLowLatencyPacer.FrameTimedOut;
                _pacer.BeginFrame(Period60Hz, TicksPerSecond);
                _driver.BeginResult = DriverLowLatencyPacer.FrameDone;
                _pacer.BeginFrame(Period60Hz, TicksPerSecond);
            }
            Assert.That(_pacer.HasGivenUp, Is.False);

            _driver.BeginResult = DriverLowLatencyPacer.FrameTimedOut;
            _pacer.BeginFrame(Period60Hz, TicksPerSecond);

            Assert.That(_pacer.HasGivenUp, Is.True);
            Assert.That(_pacer.RecentFailures, Is.EqualTo(PacingFailureWindow.FailureLimit));
        }

        /// <summary>Checks that an inactive backend is given up at once.</summary>
        [Test]
        public void InactiveBackend_IsGivenUpAtOnce()
        {
            _driver.BeginResult = DriverLowLatencyPacer.FrameInactive;

            Assert.That(_pacer.BeginFrame(Period60Hz, TicksPerSecond), Is.EqualTo(DriverFrameOutcome.Failed));
            Assert.That(_pacer.HasGivenUp, Is.True);
        }

        /// <summary>Checks that a lost device gives the backend up at once and is reported as such.</summary>
        [Test]
        public void DeviceLost_IsGivenUpAtOnce()
        {
            _driver.BeginResult = DriverLowLatencyPacer.ErrorDeviceLost;

            Assert.That(_pacer.BeginFrame(Period60Hz, TicksPerSecond), Is.EqualTo(DriverFrameOutcome.DeviceLost));
            Assert.That(_pacer.HasGivenUp, Is.True);
            Assert.That(_pacer.LastError, Is.EqualTo(DriverLowLatencyPacer.ErrorDeviceLost));
            var calls = _driver.Timeouts.Count;
            Assert.That(_pacer.BeginFrame(Period60Hz, TicksPerSecond), Is.EqualTo(DriverFrameOutcome.Failed));
            Assert.That(_driver.Timeouts.Count, Is.EqualTo(calls), "the lost device is not called again");
        }

        /// <summary>Checks that a non-positive timestamp frequency is rejected.</summary>
        [Test]
        public void InvalidFrequency_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _pacer.BeginFrame(Period60Hz, 0));
        }
    }
}
