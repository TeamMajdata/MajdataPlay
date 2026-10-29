using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MajdataPlay.Platform.Win32.Threading;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Platform
{
    /// <summary>Verifies <see cref="HighResolutionWaitableTimer"/> against the real Windows kernel timer.</summary>
    public class HighResolutionWaitableTimerTests
    {
        /// <summary>The timer under test.</summary>
        private HighResolutionWaitableTimer? _timer;

        /// <summary>Creates the timer, or skips the test outside Windows.</summary>
        [SetUp]
        public void SetUp()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("The waitable timer is a Windows API.");
            }

            Assert.That(HighResolutionWaitableTimer.TryCreate(out _timer), Is.True,
                "Windows 10 1803 or later supports high-resolution waitable timers");
        }

        /// <summary>Closes the timer.</summary>
        [TearDown]
        public void TearDown()
        {
            _timer?.Dispose();
        }

        /// <summary>Checks that a short wait lasts at least the requested time and far less than the default timer granularity allows.</summary>
        [Test]
        public void Wait_SleepsForAboutTheRequestedTime()
        {
            var requested = TimeSpan.FromMilliseconds(3);
            var shortest = TimeSpan.MaxValue;

            // The shortest of several waits filters out scheduling noise on a busy test machine.
            for (var i = 0; i < 5; i++)
            {
                var started = Stopwatch.GetTimestamp();
                Assert.That(_timer!.Wait(requested), Is.True);
                var elapsed = TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency);

                Assert.That(elapsed, Is.GreaterThanOrEqualTo(requested - TimeSpan.FromMilliseconds(0.5)));
                if (elapsed < shortest)
                {
                    shortest = elapsed;
                }
            }

            Assert.That(shortest, Is.LessThan(TimeSpan.FromMilliseconds(10)));
        }

        /// <summary>Checks that zero and negative durations return at once.</summary>
        [Test]
        public void Wait_NonPositiveDuration_ReturnsImmediately()
        {
            Assert.That(_timer!.Wait(TimeSpan.Zero), Is.True);
            Assert.That(_timer.Wait(TimeSpan.FromMilliseconds(-5)), Is.True);
        }

        /// <summary>Checks that a disposed timer refuses to wait and tolerates repeated disposal.</summary>
        [Test]
        public void Wait_AfterDispose_Throws()
        {
            _timer!.Dispose();
            _timer.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _timer.Wait(TimeSpan.FromMilliseconds(1)));
        }
    }
}
