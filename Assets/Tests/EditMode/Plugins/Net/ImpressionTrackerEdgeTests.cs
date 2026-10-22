#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using MajdataPlay.Net;
using NUnit.Framework;

namespace MajdataPlay.Tests.Plugins.Net
{
    /// <summary>Covers argument validation and the idempotent edges of the impression tracker.</summary>
    public class ImpressionTrackerEdgeTests
    {
        /// <summary>The fake monotonic clock in seconds.</summary>
        private double _now;

        /// <summary>The batches handed to the sender.</summary>
        private List<string[]> _sent = null!;

        /// <summary>Resets the clock and the recorded batches.</summary>
        [SetUp]
        public void SetUp()
        {
            _now = 10;
            _sent = new List<string[]>();
        }

        /// <summary>Creates a tracker without an onQueued callback.</summary>
        /// <returns>The tracker.</returns>
        private ImpressionTracker CreateTracker()
        {
            return new ImpressionTracker(() => _now, _sent.Add);
        }

        /// <summary>Checks that the clock and sender are required while the queue callback is optional.</summary>
        [Test]
        public void Constructor_RequiresClockAndSender()
        {
            Assert.Throws<ArgumentNullException>(() => new ImpressionTracker(null!, _ => { }));
            Assert.Throws<ArgumentNullException>(() => new ImpressionTracker(() => 0, null!));
            Assert.DoesNotThrow(() => CreateTracker().Commit("a"));
        }

        /// <summary>Checks the published constants that mirror the website and server limits.</summary>
        [Test]
        public void Constants_MatchServerLimits()
        {
            Assert.That(ImpressionTracker.DWELL_SECONDS, Is.EqualTo(1));
            Assert.That(ImpressionTracker.FLUSH_SIZE, Is.EqualTo(20));
        }

        /// <summary>Checks that flushing an empty queue never calls the sender.</summary>
        [Test]
        public void Flush_EmptyQueueSendsNothing()
        {
            var tracker = CreateTracker();

            tracker.Flush();
            tracker.Reset();

            Assert.That(_sent, Is.Empty);
            Assert.That(tracker.PendingCount, Is.Zero);
        }

        /// <summary>Checks that a dwell exactly at the threshold counts.</summary>
        [Test]
        public void Blur_CountsDwellAtThreshold()
        {
            var tracker = CreateTracker();

            tracker.Focus("a");
            _now += ImpressionTracker.DWELL_SECONDS;
            tracker.Blur();
            tracker.Blur();

            Assert.That(tracker.PendingCount, Is.EqualTo(1));
        }

        /// <summary>Checks that suspending twice does not settle twice and resuming without suspension is ignored.</summary>
        [Test]
        public void SuspendAndResume_AreIdempotent()
        {
            var tracker = CreateTracker();
            tracker.Focus("a");

            tracker.Resume();
            _now += 0.5;
            tracker.Suspend();
            tracker.Suspend();
            _now += 10;
            tracker.Resume();
            tracker.Resume();
            _now += ImpressionTracker.DWELL_SECONDS;
            tracker.Blur();

            Assert.That(tracker.PendingCount, Is.EqualTo(1));
        }

        /// <summary>Checks that focusing the same chart while suspended restarts its dwell instead of being ignored.</summary>
        [Test]
        public void Focus_SameChartWhileSuspendedRestartsDwell()
        {
            var tracker = CreateTracker();
            tracker.Focus("a");
            tracker.Suspend();
            _now += 5;

            tracker.Focus("a");
            _now += ImpressionTracker.DWELL_SECONDS;
            tracker.Blur();

            Assert.That(tracker.PendingCount, Is.EqualTo(1));
        }

        /// <summary>Checks that a committed chart is not reported again in the same batch, but is after a reset.</summary>
        [Test]
        public void Commit_ReportsOncePerBatch()
        {
            var tracker = CreateTracker();

            tracker.Commit("a");
            tracker.Commit("a");
            tracker.Reset();
            tracker.Commit("a");

            Assert.That(_sent.SelectMany(x => x), Is.EqualTo(new[] { "a", "a" }));
            Assert.That(_sent, Has.Count.EqualTo(2));
        }

        /// <summary>Checks that the sender receives a copy of the queue, which keeps working after the batch is sent.</summary>
        [Test]
        public void Flush_SendsSnapshotAndClearsQueue()
        {
            var tracker = new ImpressionTracker(() => _now, batch => _sent.Add(batch), () => { });
            tracker.Focus("a");
            _now += 2;
            tracker.Focus("b");
            _now += 2;
            tracker.Blur();

            tracker.Flush();
            tracker.Commit("c");

            Assert.That(_sent[0], Is.EqualTo(new[] { "a", "b" }));
            Assert.That(_sent[1], Is.EqualTo(new[] { "c" }));
            Assert.That(tracker.PendingCount, Is.Zero);
        }
    }
}
