using System;
using System.Diagnostics;
using System.Threading;
using MajdataPlay.FFmpeg.Internal;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Verifies the bounded worker-side GPU polling helper.</summary>
    public class GpuCompletionWaitTests
    {
        /// <summary>Checks that a nonpositive timeout is rejected.</summary>
        /// <param name="timeout">The invalid timeout.</param>
        [TestCase(0)]
        [TestCase(-1)]
        public void Constructor_RejectsNonPositiveTimeout(int timeout)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GpuCompletionWait(timeout));
        }

        /// <summary>Checks that a canceled token stops the wait immediately.</summary>
        [Test]
        public void WaitForNextPoll_CanceledToken_Throws()
        {
            var wait = new GpuCompletionWait(1000);
            using var source = new CancellationTokenSource();
            source.Cancel();

            Assert.Throws<OperationCanceledException>(() => wait.WaitForNextPoll(source.Token));
        }

        /// <summary>Checks that polling eventually reports a timeout after the deadline, using both the fast and slow paths.</summary>
        [Test]
        public void WaitForNextPoll_PastDeadline_ThrowsTimeout()
        {
            var wait = new GpuCompletionWait(40);
            var watch = Stopwatch.StartNew();
            var polls = 0;
            TimeoutException? timeout = null;
            while (watch.ElapsedMilliseconds < 5000)
            {
                try
                {
                    wait.WaitForNextPoll(CancellationToken.None);
                    polls++;
                }
                catch (TimeoutException error)
                {
                    timeout = error;
                    break;
                }
            }

            Assert.That(timeout, Is.Not.Null);
            Assert.That(polls, Is.GreaterThan(10));
            Assert.That(watch.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(35));
        }

        /// <summary>Checks that cancellation during the slow back-off path is observed.</summary>
        [Test]
        public void WaitForNextPoll_CancelDuringBackoff_Throws()
        {
            var wait = new GpuCompletionWait(5000);
            using var source = new CancellationTokenSource();
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 30)
            {
                wait.WaitForNextPoll(source.Token);
            }

            source.CancelAfter(5);
            var canceled = false;
            while (watch.ElapsedMilliseconds < 3000)
            {
                try
                {
                    wait.WaitForNextPoll(source.Token);
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                    break;
                }
            }

            Assert.That(canceled, Is.True);
        }
    }
}
