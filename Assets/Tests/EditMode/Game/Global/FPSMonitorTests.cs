using System;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

#nullable enable
namespace MajdataPlay.Tests.Game.Global
{
    /// <summary>Verifies the frame time statistics of <see cref="FPSMonitor"/>.</summary>
    public class FPSMonitorTests
    {
        /// <summary>The created objects.</summary>
        private TestObjects _objects = null!;

        /// <summary>The monitor under test, created without its lifecycle.</summary>
        private FPSMonitor _monitor = null!;

        /// <summary>Creates the monitor.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            _monitor = _objects.Add<FPSMonitor>();
        }

        /// <summary>Destroys the monitor.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
        }

        /// <summary>Checks that a monitor without samples reports nothing.</summary>
        [Test]
        public void NoSamples_ReportsZero()
        {
            Assert.That(_monitor.AverageFPS, Is.EqualTo(0));
            Assert.That(_monitor.TryGetOnePercentLowFPS(out var low), Is.False);
            Assert.That(low, Is.EqualTo(0));
        }

        /// <summary>Checks that the average uses the mean frame time.</summary>
        [Test]
        public void AverageFPS_UsesTheMeanFrameTime()
        {
            AddSamples(30, TimeSpan.FromMilliseconds(10));
            AddSamples(30, TimeSpan.FromMilliseconds(30));

            Assert.That(_monitor.AverageFPS, Is.EqualTo(50).Within(0.01));
        }

        /// <summary>Checks that the average only covers the most recent 120 frames.</summary>
        [Test]
        public void AverageFPS_ForgetsOldFrames()
        {
            AddSamples(120, TimeSpan.FromMilliseconds(100));
            AddSamples(120, TimeSpan.FromMilliseconds(20));

            Assert.That(_monitor.AverageFPS, Is.EqualTo(50).Within(0.01));
        }

        /// <summary>Checks that zero-length frames do not produce an infinite rate.</summary>
        [Test]
        public void AverageFPS_ZeroFrameTimes_ReportZero()
        {
            AddSamples(5, TimeSpan.Zero);

            Assert.That(_monitor.AverageFPS, Is.EqualTo(0));
        }

        /// <summary>Checks that the 1% low rate needs 600 frames and averages the slowest six.</summary>
        [Test]
        public void OnePercentLow_AveragesTheSlowestFrames()
        {
            AddSamples(594, TimeSpan.FromMilliseconds(10));
            Assert.That(_monitor.TryGetOnePercentLowFPS(out _), Is.False);

            AddSamples(6, TimeSpan.FromMilliseconds(100));
            Assert.That(_monitor.TryGetOnePercentLowFPS(out var low), Is.True);
            Assert.That(low, Is.EqualTo(10).Within(0.01));

            AddSamples(600, TimeSpan.FromMilliseconds(20));
            Assert.That(_monitor.TryGetOnePercentLowFPS(out low), Is.True);
            Assert.That(low, Is.EqualTo(50).Within(0.01), "the window wrapped around");
        }

        /// <summary>Checks that different slow frame times are averaged before converting their duration to FPS.</summary>
        [Test]
        public void OnePercentLow_UsesTheMeanDurationOfMixedSlowFrames()
        {
            AddSamples(594, TimeSpan.FromMilliseconds(10));
            foreach (var milliseconds in new[] { 20, 30, 40, 50, 60, 100 })
            {
                AddSamples(1, TimeSpan.FromMilliseconds(milliseconds));
            }

            Assert.That(_monitor.TryGetOnePercentLowFPS(out var low), Is.True);
            Assert.That(low, Is.EqualTo(20).Within(0.000001));
        }

        /// <summary>Checks that averaging durations preserves fractions smaller than one TimeSpan tick.</summary>
        [Test]
        public void OnePercentLow_PreservesFractionalMeanTicks()
        {
            AddSamples(594, TimeSpan.Zero);
            AddSamples(5, TimeSpan.FromTicks(1));
            AddSamples(1, TimeSpan.FromTicks(2));

            Assert.That(_monitor.TryGetOnePercentLowFPS(out var low), Is.True);
            Assert.That(low, Is.EqualTo(60000000.0 / 7).Within(0.000001));
        }

        /// <summary>Checks ordered and duplicate durations against a sorted reference through partial window wraps.</summary>
        /// <param name="descending">Whether durations are supplied in descending order.</param>
        /// <param name="duplicates">Whether groups of durations have equal values.</param>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void OnePercentLow_OrderedSamples_MatchSortedReference(bool descending, bool duplicates)
        {
            var frameTimes = new long[1437];
            for (var i = 0; i < frameTimes.Length; i++)
            {
                var position = descending ? frameTimes.Length - i : i + 1;
                frameTimes[i] = (duplicates ? position / 13 : position) * 1000L;
            }

            AddSamplesAndCheckWindowWraps(frameTimes);
        }

        /// <summary>Checks a deterministic random sequence against a sorted reference through partial window wraps.</summary>
        [Test]
        public void OnePercentLow_RandomSamples_MatchSortedReference()
        {
            var random = new Random(379);
            var frameTimes = new long[1437];
            for (var i = 0; i < frameTimes.Length; i++)
            {
                frameTimes[i] = random.Next(0, 1000001);
            }

            AddSamplesAndCheckWindowWraps(frameTimes);
        }

        /// <summary>Checks that repeated queries preserve the oldest samples until new frames replace them.</summary>
        [Test]
        public void OnePercentLow_RepeatedQueries_PreserveTheRollingWindow()
        {
            AddSamples(6, TimeSpan.FromMilliseconds(100));
            AddSamples(594, TimeSpan.FromMilliseconds(10));
            for (var i = 0; i < 4; i++)
            {
                Assert.That(_monitor.TryGetOnePercentLowFPS(out var low), Is.True);
                Assert.That(low, Is.EqualTo(10).Within(0.000001));
            }

            AddSamples(37, TimeSpan.FromMilliseconds(20));

            Assert.That(_monitor.TryGetOnePercentLowFPS(out var updatedLow), Is.True);
            Assert.That(updatedLow, Is.EqualTo(50).Within(0.000001));
        }

        /// <summary>Checks warmed repeated queries for GC allocation with a calibrated current-thread profiler constraint.</summary>
        [Test]
        public void OnePercentLow_RepeatedQueries_DoNotAllocateGCMemory()
        {
            AddSamples(594, TimeSpan.FromMilliseconds(10));
            AddSamples(6, TimeSpan.FromMilliseconds(100));
            var allAvailable = true;
            var low = 0.0;
            TestDelegate query = () =>
            {
                for (var i = 0; i < 128; i++)
                {
                    allAvailable &= _monitor.TryGetOnePercentLowFPS(out low);
                }
            };
            query();

            Assert.That(() =>
            {
                GC.KeepAlive(new byte[128]);
            }, Is.AllocatingGCMemory(), "the profiler must detect a known managed allocation");
            Assert.That(query, Is.Not.AllocatingGCMemory());
            Assert.That(allAvailable, Is.True);
            Assert.That(low, Is.EqualTo(10).Within(0.000001));
        }

        /// <summary>Checks that the 1% low rate of zero-length frames is reported as zero.</summary>
        [Test]
        public void OnePercentLow_ZeroFrameTimes_ReportZero()
        {
            AddSamples(600, TimeSpan.Zero);

            Assert.That(_monitor.TryGetOnePercentLowFPS(out var low), Is.True);
            Assert.That(low, Is.EqualTo(0));
        }

        /// <summary>Checks that LateUpdate samples the time elapsed on the game timeline since the previous frame.</summary>
        [Test]
        public void LateUpdate_SamplesTimelineDeltas()
        {
            using var clock = new GlobalTestClock();
            UnityMessages.Send(_monitor, "Awake");

            clock.Advance(TimeSpan.FromMilliseconds(25));
            UnityMessages.Send(_monitor, "LateUpdate");
            clock.Advance(TimeSpan.FromMilliseconds(25));
            UnityMessages.Send(_monitor, "LateUpdate");

            Assert.That(_monitor.AverageFPS, Is.EqualTo(40).Within(0.01));
            Assert.That(_monitor.InstanceID, Is.EqualTo(_monitor.GetInstanceID()));
            Assert.That(_monitor.GameObject, Is.SameAs(_monitor.gameObject));
            Assert.That(_monitor.Transform, Is.SameAs(_monitor.transform));
        }

        /// <summary>Feeds a sequence and verifies complete and partially overwritten rolling windows.</summary>
        /// <param name="frameTimes">The frame durations in chronological order, in ticks.</param>
        private void AddSamplesAndCheckWindowWraps(long[] frameTimes)
        {
            for (var i = 0; i < frameTimes.Length; i++)
            {
                PrivateMember.Invoke(_monitor, "AddSample", frameTimes[i]);
                var sampleCount = i + 1;
                if (sampleCount == 599)
                {
                    Assert.That(_monitor.TryGetOnePercentLowFPS(out var low), Is.False);
                    Assert.That(low, Is.EqualTo(0));
                }
                if (sampleCount == 600 || sampleCount == 637 || sampleCount == 1200 || sampleCount == frameTimes.Length)
                {
                    AssertOnePercentLowMatchesReference(frameTimes, sampleCount);
                }
            }
        }

        /// <summary>Checks one percent low FPS using a separately sorted copy of the most recent 600 samples.</summary>
        /// <param name="frameTimes">The complete chronological frame duration sequence, in ticks.</param>
        /// <param name="sampleCount">The number of samples already supplied to the monitor.</param>
        private void AssertOnePercentLowMatchesReference(long[] frameTimes, int sampleCount)
        {
            var window = new long[600];
            Array.Copy(frameTimes, sampleCount - window.Length, window, 0, window.Length);
            Array.Sort(window);
            var totalSlowTicks = 0L;
            for (var i = window.Length - 6; i < window.Length; i++)
            {
                totalSlowTicks += window[i];
            }
            var expectedLow = totalSlowTicks == 0 ? 0 : (6.0 * TimeSpan.TicksPerSecond) / totalSlowTicks;

            Assert.That(_monitor.TryGetOnePercentLowFPS(out var low), Is.True);
            Assert.That(low, Is.EqualTo(expectedLow).Within(0.000001), $"after {sampleCount} frames");
        }

        /// <summary>Feeds identical frame times into the monitor.</summary>
        /// <param name="count">The number of frames.</param>
        /// <param name="frameTime">The frame time.</param>
        private void AddSamples(int count, TimeSpan frameTime)
        {
            for (var i = 0; i < count; i++)
            {
                PrivateMember.Invoke(_monitor, "AddSample", frameTime.Ticks);
            }
        }
    }
}
