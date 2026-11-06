using MajdataPlay.Rendering;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Rendering
{
    /// <summary>Verifies when <see cref="PacingFailureWindow"/> gives a pacing backend up.</summary>
    public class PacingFailureWindowTests
    {
        /// <summary>The window under test.</summary>
        private PacingFailureWindow _window = null!;

        /// <summary>Creates the window.</summary>
        [SetUp]
        public void SetUp()
        {
            _window = new PacingFailureWindow();
        }

        /// <summary>Checks that consecutive failures reach the limit.</summary>
        [Test]
        public void ConsecutiveFailures_ReachTheLimit()
        {
            RecordFailures(PacingFailureWindow.FailureLimit - 1);
            Assert.That(_window.IsExceeded, Is.False);

            _window.RecordFailure();

            Assert.That(_window.IsExceeded, Is.True);
            Assert.That(_window.Failures, Is.EqualTo(PacingFailureWindow.FailureLimit));
        }

        /// <summary>Checks that successes between failures do not reset the count.</summary>
        [Test]
        public void SuccessesBetweenFailures_DoNotResetTheCount()
        {
            for (var i = 0; i < PacingFailureWindow.FailureLimit; i++)
            {
                _window.RecordFailure();
                _window.RecordSuccess();
            }

            Assert.That(_window.IsExceeded, Is.True, "half of the recent frames failed");
        }

        /// <summary>Checks that failures older than the window are forgotten.</summary>
        [Test]
        public void FailuresOlderThanTheWindow_AreForgotten()
        {
            var failures = PacingFailureWindow.FailureLimit - 1;
            RecordFailures(failures);
            RecordSuccesses(PacingFailureWindow.WindowSize - failures);
            Assert.That(_window.Failures, Is.EqualTo(failures), "every failure is still in the window");

            _window.RecordSuccess();
            Assert.That(_window.Failures, Is.EqualTo(failures - 1), "the oldest failure left the window");

            _window.RecordFailure();
            Assert.That(_window.IsExceeded, Is.False);
        }

        /// <summary>Checks that occasional failures never reach the limit.</summary>
        [Test]
        public void OccasionalFailures_NeverReachTheLimit()
        {
            for (var i = 0; i < PacingFailureWindow.WindowSize * 10; i++)
            {
                if (i % 8 == 0)
                {
                    _window.RecordFailure();
                }
                else
                {
                    _window.RecordSuccess();
                }
            }

            Assert.That(_window.IsExceeded, Is.False);
            Assert.That(_window.Failures, Is.EqualTo(PacingFailureWindow.WindowSize / 8));
        }

        /// <summary>Records failed frames.</summary>
        /// <param name="count">The number of frames.</param>
        private void RecordFailures(int count)
        {
            for (var i = 0; i < count; i++)
            {
                _window.RecordFailure();
            }
        }

        /// <summary>Records successful frames.</summary>
        /// <param name="count">The number of frames.</param>
        private void RecordSuccesses(int count)
        {
            for (var i = 0; i < count; i++)
            {
                _window.RecordSuccess();
            }
        }
    }
}
