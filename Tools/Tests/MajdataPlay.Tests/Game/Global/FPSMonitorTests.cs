using System;
using NUnit.Framework;

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
