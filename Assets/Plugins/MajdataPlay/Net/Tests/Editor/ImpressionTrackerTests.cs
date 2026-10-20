using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace MajdataPlay.Net.Tests
{
    public class ImpressionTrackerTests
    {
        double _now;
        List<string[]> _sent = null!;
        int _queuedCount;
        ImpressionTracker _tracker = null!;

        [SetUp]
        public void SetUp()
        {
            _now = 100;
            _sent = new List<string[]>();
            _queuedCount = 0;
            _tracker = new ImpressionTracker(() => _now, _sent.Add, () => _queuedCount++);
        }

        [Test]
        public void ShortDwellIsNotAnImpression()
        {
            _tracker.Focus("a");
            _now += ImpressionTracker.DWELL_SECONDS - 0.01;
            _tracker.Blur();
            _tracker.Flush();

            Assert.That(_sent, Is.Empty);
            Assert.That(_queuedCount, Is.Zero);
        }

        [Test]
        public void DwellIsSettledWhenTheFocusLeaves()
        {
            _tracker.Focus("a");
            _now += ImpressionTracker.DWELL_SECONDS;

            Assert.That(_tracker.PendingCount, Is.Zero, "Nothing is recorded while the chart stays focused.");
            _tracker.Blur();
            Assert.That(_tracker.PendingCount, Is.EqualTo(1));
            Assert.That(_queuedCount, Is.EqualTo(1));
            _tracker.Flush();
            Assert.That(_sent.Single(), Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void FocusingAnotherChartSettlesThePreviousOne()
        {
            _tracker.Focus("a");
            _now += 1;
            _tracker.Focus("b");
            _now += 0.1;
            _tracker.Focus("c");
            _tracker.Flush();

            Assert.That(_sent.Single(), Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void RefocusingTheSameChartKeepsItsStartTime()
        {
            // Each part is below the dwell, so the chart only counts if the refocus kept the start time
            _tracker.Focus("a");
            _now += ImpressionTracker.DWELL_SECONDS * 0.6;
            _tracker.Focus("a");
            _now += ImpressionTracker.DWELL_SECONDS * 0.6;
            _tracker.Blur();

            Assert.That(_tracker.PendingCount, Is.EqualTo(1));
        }

        [Test]
        public void EachChartIsReportedOncePerBatch()
        {
            for (var i = 0; i < 3; i++)
            {
                _tracker.Focus("a");
                _now += 1;
                _tracker.Blur();
            }
            _tracker.Commit("a");
            _tracker.Flush();

            Assert.That(_sent.SelectMany(x => x), Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void ResetSendsPendingAndAllowsReportingAgain()
        {
            _tracker.Focus("a");
            _now += 1;
            _tracker.Focus("b");
            _now += 1;

            _tracker.Reset();
            Assert.That(_sent.Single(), Is.EqualTo(new[] { "a", "b" }), "The focused chart is settled before the batch changes.");

            _tracker.Focus("a");
            _now += 1;
            _tracker.Blur();
            _tracker.Flush();
            Assert.That(_sent.Last(), Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void CommitCountsWithoutDwellAndSendsAtOnce()
        {
            _tracker.Focus("x");
            _now += 1;
            _tracker.Focus("a");

            _tracker.Commit("a");

            Assert.That(_sent.Single(), Is.EqualTo(new[] { "x", "a" }));
            Assert.That(_tracker.PendingCount, Is.Zero);
            _now += 1;
            _tracker.Blur();
            Assert.That(_tracker.PendingCount, Is.Zero, "Commit clears the focus, so leaving later does not count it again.");
        }

        [Test]
        public void FullBatchIsSentWithoutWaiting()
        {
            for (var i = 0; i < ImpressionTracker.FLUSH_SIZE + 1; i++)
            {
                _tracker.Focus($"chart-{i}");
                _now += 1;
            }
            _tracker.Blur();

            Assert.That(_sent.Single(), Has.Length.EqualTo(ImpressionTracker.FLUSH_SIZE));
            Assert.That(_tracker.PendingCount, Is.EqualTo(1));
            Assert.That(_queuedCount, Is.EqualTo(2), "The owner is told again once the queue refills after a send.");
        }

        [Test]
        public void OwnerIsToldOnlyWhenTheQueueStopsBeingEmpty()
        {
            foreach (var hash in new[] { "a", "b", "c" })
            {
                _tracker.Focus(hash);
                _now += 1;
            }
            _tracker.Blur();
            Assert.That(_queuedCount, Is.EqualTo(1));

            _tracker.Flush();
            _tracker.Commit("d");
            Assert.That(_queuedCount, Is.EqualTo(2));
        }

        [Test]
        public void TimeInTheBackgroundDoesNotCount()
        {
            _tracker.Focus("a");
            _now += ImpressionTracker.DWELL_SECONDS * 0.6;
            _tracker.Suspend();
            _now += 60;
            _tracker.Resume();
            _now += ImpressionTracker.DWELL_SECONDS * 0.6;
            _tracker.Blur();
            Assert.That(_tracker.PendingCount, Is.Zero, "Dwell before and after the background is not added up.");

            _tracker.Focus("b");
            _now += ImpressionTracker.DWELL_SECONDS + 0.1;
            _tracker.Suspend();
            Assert.That(_tracker.PendingCount, Is.EqualTo(1), "Dwell finished before the background counts right away.");
        }

        [Test]
        public void SuspendedChartCountsAgainAfterResume()
        {
            _tracker.Focus("a");
            _tracker.Suspend();
            _now += 60;
            _tracker.Blur();
            Assert.That(_tracker.PendingCount, Is.Zero);

            _tracker.Focus("a");
            _tracker.Suspend();
            _tracker.Resume();
            _now += ImpressionTracker.DWELL_SECONDS;
            _tracker.Blur();
            Assert.That(_tracker.PendingCount, Is.EqualTo(1));
        }
    }
}
