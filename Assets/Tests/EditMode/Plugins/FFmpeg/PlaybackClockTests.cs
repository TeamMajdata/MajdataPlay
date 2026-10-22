using System;
using MajdataPlay.FFmpeg.Internal;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Verifies the monotonic, rate-adjustable playback clock with an injected time source.</summary>
    public class PlaybackClockTests
    {
        /// <summary>Stores the injected monotonic time in seconds.</summary>
        private double _now;

        /// <summary>Creates a clock that reads the injected time.</summary>
        /// <returns>The clock under test.</returns>
        private PlaybackClock CreateClock()
        {
            return new PlaybackClock(() => _now);
        }

        /// <summary>Resets the injected time before each test.</summary>
        [SetUp]
        public void SetUp()
        {
            _now = 100;
        }

        /// <summary>Checks that a new clock is stopped at zero with unit rate.</summary>
        [Test]
        public void NewClock_IsStoppedAtZero()
        {
            var clock = CreateClock();
            _now += 5;

            Assert.That(clock.Running, Is.False);
            Assert.That(clock.Position, Is.EqualTo(0));
            Assert.That(clock.Rate, Is.EqualTo(1));
        }

        /// <summary>Checks that the running clock advances with elapsed time and freezes when paused.</summary>
        [Test]
        public void StartPause_AdvanceAndFreeze()
        {
            var clock = CreateClock();
            clock.Set(3);
            clock.Start();
            _now += 2;

            Assert.That(clock.Running, Is.True);
            Assert.That(clock.Position, Is.EqualTo(5).Within(1e-9));

            clock.Pause();
            _now += 50;

            Assert.That(clock.Running, Is.False);
            Assert.That(clock.Position, Is.EqualTo(5).Within(1e-9));
        }

        /// <summary>Checks that starting an already running clock does not reset its anchor.</summary>
        [Test]
        public void Start_WhenRunning_KeepsAnchor()
        {
            var clock = CreateClock();
            clock.Start();
            _now += 1;
            clock.Start();
            _now += 1;

            Assert.That(clock.Position, Is.EqualTo(2).Within(1e-9));
        }

        /// <summary>Checks that changing the rate keeps the position continuous and scales later progress.</summary>
        [Test]
        public void Rate_ChangePreservesPositionAndScalesProgress()
        {
            var clock = CreateClock();
            clock.Start();
            _now += 2;
            clock.Rate = 4;

            Assert.That(clock.Position, Is.EqualTo(2).Within(1e-9));

            _now += 0.5;

            Assert.That(clock.Rate, Is.EqualTo(4));
            Assert.That(clock.Position, Is.EqualTo(4).Within(1e-9));
        }

        /// <summary>Checks that Set while running reanchors the timeline at the new position.</summary>
        [Test]
        public void Set_WhileRunning_ReanchorsTimeline()
        {
            var clock = CreateClock();
            clock.Start();
            _now += 10;
            clock.Set(1);
            _now += 0.25;

            Assert.That(clock.Position, Is.EqualTo(1.25).Within(1e-9));
        }

        /// <summary>Checks the inclusive rate bounds and rejection of non-finite values.</summary>
        /// <param name="rate">The rate to validate.</param>
        /// <param name="valid">Whether the rate is accepted.</param>
        [TestCase(0.0625, true)]
        [TestCase(16.0, true)]
        [TestCase(1.0, true)]
        [TestCase(0.06, false)]
        [TestCase(16.01, false)]
        [TestCase(double.NaN, false)]
        [TestCase(double.PositiveInfinity, false)]
        [TestCase(double.NegativeInfinity, false)]
        public void ValidateRate_ChecksBounds(double rate, bool valid)
        {
            if (valid)
            {
                Assert.DoesNotThrow(() => PlaybackClock.ValidateRate(rate));
            }
            else
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => PlaybackClock.ValidateRate(rate));
            }
        }

        /// <summary>Checks that an invalid rate assignment leaves the clock unchanged.</summary>
        [Test]
        public void Rate_InvalidValueLeavesClockUnchanged()
        {
            var clock = CreateClock();
            clock.Rate = 2;

            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Rate = 0);
            Assert.That(clock.Rate, Is.EqualTo(2));
        }

        /// <summary>Checks that the default clock uses the system stopwatch and advances in real time.</summary>
        [Test]
        public void DefaultTimeSource_AdvancesMonotonically()
        {
            var clock = new PlaybackClock();
            clock.Start();
            var first = clock.Position;
            var spin = System.Diagnostics.Stopwatch.StartNew();
            while (spin.ElapsedMilliseconds < 5)
            {
            }

            Assert.That(clock.Position, Is.GreaterThan(first));
        }
    }
}
