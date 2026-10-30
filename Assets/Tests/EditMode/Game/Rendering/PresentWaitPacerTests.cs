using System;
using System.Collections.Generic;
using MajdataPlay.Rendering;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Rendering
{
    /// <summary>Verifies how <see cref="PresentWaitPacer"/> picks the frame to wait for and how its waits end.</summary>
    public class PresentWaitPacerTests
    {
        /// <summary>A timestamp frequency of one tick per microsecond.</summary>
        private const long TicksPerSecond = 1_000_000;

        /// <summary>The refresh period of a 60 Hz display, in microseconds.</summary>
        private const long Period60Hz = 16_667;

        /// <summary>The longest brief wait for an in-flight submission at 60 Hz: 2 ms.</summary>
        private const long SubmissionWait = 2_000;

        /// <summary>A scripted presentation source driven by a fake clock.</summary>
        private sealed class FakeSource : IPresentWaitSource
        {
            /// <summary>Gets or sets whether present waiting is available.</summary>
            public bool Available { get; set; } = true;

            /// <summary>Gets or sets the swapchain generation.</summary>
            public ulong Generation { get; set; } = 1;

            /// <summary>Gets or sets the latest presentation id.</summary>
            public ulong Latest { get; set; }

            /// <summary>Gets or sets the result of polls; receives the polled id.</summary>
            public Func<ulong, int> PollResult { get; set; } = _ => PresentWaitPacer.PollPresented;

            /// <summary>Gets the ids passed to <see cref="Poll"/>.</summary>
            public List<ulong> PolledIds { get; } = new();

            /// <inheritdoc/>
            public bool TryQuery(out ulong generation, out ulong presentId)
            {
                generation = Generation;
                presentId = Latest;
                return Available;
            }

            /// <inheritdoc/>
            public int Poll(ulong generation, ulong presentId)
            {
                PolledIds.Add(presentId);
                return generation == Generation ? PollResult(presentId) : PresentWaitPacer.PollSwapchainChanged;
            }
        }

        /// <summary>The fake source.</summary>
        private FakeSource _source = null!;

        /// <summary>The fake clock in microseconds.</summary>
        private long _now;

        /// <summary>Runs on every pause, after the clock advanced; simulates the render thread.</summary>
        private Action? _onPause;

        /// <summary>The pacer under test.</summary>
        private PresentWaitPacer _pacer = null!;

        /// <summary>Creates the source, clock and pacer.</summary>
        [SetUp]
        public void SetUp()
        {
            _source = new FakeSource();
            _now = 1_000_000;
            _onPause = null;
            _pacer = new PresentWaitPacer(_source, () => _now, ticks =>
            {
                _now += ticks;
                _onPause?.Invoke();
            }, TicksPerSecond);
        }

        /// <summary>Checks that a non-positive timestamp frequency is rejected.</summary>
        [Test]
        public void Constructor_RejectsInvalidFrequency()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PresentWaitPacer(_source, () => 0, _ => { }, 0));
        }

        /// <summary>Checks that an unavailable source fails without waiting.</summary>
        [Test]
        public void UnavailableSource_Fails()
        {
            _source.Available = false;

            Assert.That(_pacer.WaitForPreviousFrame(Period60Hz), Is.EqualTo(PresentWaitOutcome.Failed));
            Assert.That(_pacer.LastError, Is.EqualTo(PresentWaitPacer.ErrorUnavailable));
        }

        /// <summary>Checks that the in-flight submission of the previous frame becomes the frame to wait for.</summary>
        [Test]
        public void InFlightSubmission_IsTheTarget()
        {
            _source.Latest = 4;
            var pauses = 0;
            _onPause = () =>
            {
                // The render thread presents the previous frame 0.3 ms after the frame started.
                if (++pauses == 3)
                {
                    _source.Latest = 5;
                }
            };
            var polls = 0;
            _source.PollResult = _ => ++polls < 3 ? PresentWaitPacer.PollPending : PresentWaitPacer.PollPresented;

            Assert.That(_pacer.WaitForPreviousFrame(Period60Hz), Is.EqualTo(PresentWaitOutcome.Presented));
            Assert.That(_source.PolledIds, Is.All.EqualTo(5UL));
            Assert.That(_pacer.ExpectsInFlightSubmission, Is.True);
        }

        /// <summary>Checks that without an in-flight submission the latest presentation is the target after the brief wait.</summary>
        [Test]
        public void NoSubmission_WaitsForTheLatestAfterTheBriefWait()
        {
            _source.Latest = 4;
            var start = _now;

            Assert.That(_pacer.WaitForPreviousFrame(Period60Hz), Is.EqualTo(PresentWaitOutcome.Presented));
            Assert.That(_source.PolledIds, Is.EqualTo(new[] { 4UL }));
            Assert.That(_now - start, Is.InRange(SubmissionWait, SubmissionWait + 200));
        }

        /// <summary>Checks that the brief wait never exceeds a quarter of the refresh period.</summary>
        [Test]
        public void BriefWait_IsCappedByTheRefreshPeriod()
        {
            _source.Latest = 4;
            var start = _now;

            _pacer.WaitForPreviousFrame(4_000);

            Assert.That(_now - start, Is.InRange(1_000, 1_200));
        }

        /// <summary>Checks that repeated misses skip the brief wait and that a later probe restores it.</summary>
        [Test]
        public void RepeatedMisses_SkipTheBriefWaitUntilAProbeFindsASubmission()
        {
            _source.Latest = 4;
            for (var i = 0; i < PresentWaitPacer.InFlightMissLimit; i++)
            {
                _pacer.WaitForPreviousFrame(Period60Hz);
            }
            Assert.That(_pacer.ExpectsInFlightSubmission, Is.False);

            var start = _now;
            _pacer.WaitForPreviousFrame(Period60Hz);
            Assert.That(_now, Is.EqualTo(start), "no brief wait while submissions are not expected");

            for (var i = 2; i < PresentWaitPacer.InFlightProbeInterval; i++)
            {
                _pacer.WaitForPreviousFrame(Period60Hz);
            }
            _onPause = () => _source.Latest = 5;
            _pacer.WaitForPreviousFrame(Period60Hz);

            Assert.That(_pacer.ExpectsInFlightSubmission, Is.True, "the probe found an in-flight submission");
            Assert.That(_source.PolledIds[_source.PolledIds.Count - 1], Is.EqualTo(5UL));
        }

        /// <summary>Checks that nothing is waited for before the first presentation.</summary>
        [Test]
        public void BeforeTheFirstPresentation_NothingIsWaitedFor()
        {
            Assert.That(_pacer.WaitForPreviousFrame(Period60Hz), Is.EqualTo(PresentWaitOutcome.NoPresentation));
            Assert.That(_source.PolledIds, Is.Empty);
        }

        /// <summary>Checks that a swapchain recreated during the brief wait or reported by a poll ends the wait.</summary>
        [Test]
        public void SwapchainChange_EndsTheWait()
        {
            _source.Latest = 4;
            _onPause = () => _source.Generation = 2;
            Assert.That(_pacer.WaitForPreviousFrame(Period60Hz), Is.EqualTo(PresentWaitOutcome.SwapchainChanged));

            _onPause = null;
            _source.PollResult = _ => PresentWaitPacer.PollSwapchainChanged;
            Assert.That(_pacer.WaitForPreviousFrame(Period60Hz), Is.EqualTo(PresentWaitOutcome.SwapchainChanged));
        }

        /// <summary>Checks that a poll error fails the wait and is recorded.</summary>
        [Test]
        public void PollError_Fails()
        {
            _source.Latest = 4;
            _source.PollResult = _ => -4;

            Assert.That(_pacer.WaitForPreviousFrame(Period60Hz), Is.EqualTo(PresentWaitOutcome.Failed));
            Assert.That(_pacer.LastError, Is.EqualTo(-4));
        }

        /// <summary>
        /// Checks that a presentation that is never shown times out after three refresh periods, is not waited for
        /// again, and that waiting resumes once a new presentation arrives.
        /// </summary>
        [Test]
        public void Timeout_IsNotRepeatedUntilANewPresentation()
        {
            _source.Latest = 4;
            _source.PollResult = _ => PresentWaitPacer.PollPending;
            var start = _now;

            Assert.That(_pacer.WaitForPreviousFrame(Period60Hz), Is.EqualTo(PresentWaitOutcome.TimedOut));
            Assert.That(_now - start, Is.InRange(Period60Hz * 3, Period60Hz * 3 + 1_000));

            var polls = _source.PolledIds.Count;
            start = _now;
            Assert.That(_pacer.WaitForPreviousFrame(Period60Hz), Is.EqualTo(PresentWaitOutcome.NoPresentation));
            Assert.That(_source.PolledIds.Count, Is.EqualTo(polls));
            Assert.That(_now, Is.EqualTo(start));

            _source.Latest = 5;
            _source.PollResult = _ => PresentWaitPacer.PollPresented;
            Assert.That(_pacer.WaitForPreviousFrame(Period60Hz), Is.EqualTo(PresentWaitOutcome.Presented));
        }

        /// <summary>
        /// Checks the waits around a window resize or fullscreen toggle: the swapchain generation changes while a
        /// frame waits, nothing is waited for until the new swapchain presents, and a timeout remembered for the old
        /// swapchain does not suppress waits on the new one even when its ids are lower.
        /// </summary>
        [Test]
        public void SwapchainRecreation_WaitsOnTheNewSwapchain()
        {
            _source.Generation = 1;
            _source.Latest = 10;
            _source.PollResult = _ => PresentWaitPacer.PollPending;
            Assert.That(_pacer.WaitForPreviousFrame(Period60Hz), Is.EqualTo(PresentWaitOutcome.TimedOut));

            // Unity recreates the swapchain while the next frame waits.
            _source.PollResult = _ =>
            {
                _source.Generation = 2;
                _source.Latest = 0;
                return PresentWaitPacer.PollSwapchainChanged;
            };
            _source.Latest = 11;
            Assert.That(_pacer.WaitForPreviousFrame(Period60Hz), Is.EqualTo(PresentWaitOutcome.SwapchainChanged));

            _source.PollResult = _ => PresentWaitPacer.PollPresented;
            Assert.That(_pacer.WaitForPreviousFrame(Period60Hz), Is.EqualTo(PresentWaitOutcome.NoPresentation),
                "the new swapchain has not presented yet");

            _source.PolledIds.Clear();
            _source.Latest = 3;
            Assert.That(_pacer.WaitForPreviousFrame(Period60Hz), Is.EqualTo(PresentWaitOutcome.Presented));
            Assert.That(_source.PolledIds, Is.EqualTo(new[] { 3UL }), "waits on the new swapchain's id");
        }
    }
}
