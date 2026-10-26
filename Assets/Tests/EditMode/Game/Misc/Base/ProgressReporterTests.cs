using System;
using System.Collections.Generic;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Base
{
    /// <summary>Covers <see cref="ProgressReport"/> and the callback forwarding of <see cref="ProgressReporter"/>.</summary>
    public class ProgressReporterTests
    {
        /// <summary>Checks the counts, fraction and text of a report with countable items.</summary>
        [Test]
        public void ProgressReport_WithTotal_FormatsCountsAndPercentage()
        {
            var report = new ProgressReport("Extracting", 1, 3);

            Assert.That(report.HasTotal, Is.True);
            Assert.That(report.Fraction, Is.EqualTo(1f / 3f).Within(1e-6f));
            Assert.That(report.ToString(), Is.EqualTo("Extracting\n1/3 (33%)"));
        }

        /// <summary>Checks that a report without countable items, a default report and out-of-range counts stay well-formed.</summary>
        [Test]
        public void ProgressReport_WithoutTotal_ShowsOnlyTheMessage()
        {
            Assert.That(new ProgressReport("Installing").ToString(), Is.EqualTo("Installing"));
            Assert.That(new ProgressReport("Installing").Fraction, Is.EqualTo(0f));
            Assert.That(default(ProgressReport).Message, Is.EqualTo(string.Empty));
            Assert.That(default(ProgressReport).ToString(), Is.EqualTo(string.Empty));
            Assert.That(new ProgressReport(null, -1, -2).HasTotal, Is.False);
            Assert.That(new ProgressReport("Done", 5, 3).Fraction, Is.EqualTo(1f));
        }

        /// <summary>Checks that every report reaches the callback synchronously and in order.</summary>
        [Test]
        public void Report_ForwardsToTheCallback()
        {
            var received = new List<ProgressReport>();
            IProgress<ProgressReport> progress = new ProgressReporter(received.Add);

            progress.Report(new ProgressReport("A", 0, 2));
            progress.Report(new ProgressReport("A", 2, 2));

            Assert.That(received.Count, Is.EqualTo(2));
            Assert.That(received[1].Completed, Is.EqualTo(2));
        }

        /// <summary>Checks that a reporter needs a callback.</summary>
        [Test]
        public void Constructor_RejectsNullCallback()
        {
            Assert.Throws<ArgumentNullException>(() => new ProgressReporter(null!));
        }
    }
}
