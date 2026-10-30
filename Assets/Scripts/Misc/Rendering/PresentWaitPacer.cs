using System;

#nullable enable
namespace MajdataPlay.Rendering
{
    /// <summary>Reports the presentation progress of the swapchain Unity renders to.</summary>
    internal interface IPresentWaitSource
    {
        /// <summary>Reads the swapchain generation and the id of its latest successful presentation.</summary>
        /// <param name="generation">The generation; it changes whenever the swapchain is created or destroyed.</param>
        /// <param name="presentId">The latest presentation id, or zero before the first presentation.</param>
        /// <returns>Whether present waiting is available.</returns>
        bool TryQuery(out ulong generation, out ulong presentId);

        /// <summary>Checks without blocking whether a presentation has been shown.</summary>
        /// <param name="generation">The generation the id belongs to.</param>
        /// <param name="presentId">The presentation id to check.</param>
        /// <returns><see cref="PresentWaitPacer.PollPresented"/>, <see cref="PresentWaitPacer.PollPending"/>, <see cref="PresentWaitPacer.PollSwapchainChanged"/>, or another negative error code.</returns>
        int Poll(ulong generation, ulong presentId);
    }

    /// <summary>Describes how a frame start wait ended.</summary>
    internal enum PresentWaitOutcome
    {
        /// <summary>The previous frame was shown.</summary>
        Presented,

        /// <summary>There was no new presentation to wait for, such as before the first frame or while minimized.</summary>
        NoPresentation,

        /// <summary>The swapchain was recreated; the next frame waits on the new one.</summary>
        SwapchainChanged,

        /// <summary>The previous frame was not shown within the timeout.</summary>
        TimedOut,

        /// <summary>Present waiting reported an error.</summary>
        Failed,
    }

    /// <summary>
    /// Waits at the start of a frame until the frame rendered before it has been shown, which caps the number of
    /// queued frames the way D3D11's waitable swapchain does.
    /// </summary>
    /// <remarks>
    /// With multithreaded rendering the previous frame is usually still being submitted on the render thread when
    /// the next frame starts, so its presentation id does not exist yet. The pacer first waits briefly for that
    /// submission; when none arrives, the latest presentation is the previous frame. If submissions keep missing,
    /// the brief wait is skipped and only probed occasionally.
    /// </remarks>
    internal sealed class PresentWaitPacer
    {
        /// <summary>The poll result for a presentation that has been shown.</summary>
        internal const int PollPresented = 1;

        /// <summary>The poll result for a presentation that is still pending.</summary>
        internal const int PollPending = 0;

        /// <summary>The poll result for a swapchain that was recreated or destroyed.</summary>
        internal const int PollSwapchainChanged = -1;

        /// <summary>The error recorded when the source stops reporting presentations.</summary>
        internal const int ErrorUnavailable = -2;

        /// <summary>The number of consecutive missed submissions after which the brief submission wait is skipped.</summary>
        internal const int InFlightMissLimit = 8;

        /// <summary>The number of frames between probes for an in-flight submission while it is skipped.</summary>
        internal const int InFlightProbeInterval = 240;

        /// <summary>The source of presentation progress.</summary>
        private readonly IPresentWaitSource _source;

        /// <summary>Reads the current timestamp.</summary>
        private readonly Func<long> _getTimestamp;

        /// <summary>Pauses the calling thread for about the given number of ticks.</summary>
        private readonly Action<long> _pause;

        /// <summary>The longest wait for the previous frame's submission.</summary>
        private readonly long _maxSubmissionWaitTicks;

        /// <summary>The pause between submission checks.</summary>
        private readonly long _submissionPollTicks;

        /// <summary>The pause between presentation polls.</summary>
        private readonly long _presentationPollTicks;

        /// <summary>The shortest presentation timeout.</summary>
        private readonly long _minPresentationTimeoutTicks;

        /// <summary>The number of consecutive frames whose previous frame was not submitted in time.</summary>
        private int _inFlightMisses;

        /// <summary>The number of frames since the last submission probe while submissions are not expected.</summary>
        private int _framesSinceProbe;

        /// <summary>The generation of <see cref="_stalledPresentId"/>.</summary>
        private ulong _stalledGeneration;

        /// <summary>A presentation id that timed out; it is not waited for again.</summary>
        private ulong _stalledPresentId;

        /// <summary>Creates a pacer.</summary>
        /// <param name="source">The source of presentation progress.</param>
        /// <param name="getTimestamp">Reads the current timestamp.</param>
        /// <param name="pause">Pauses the calling thread for about the given number of ticks.</param>
        /// <param name="ticksPerSecond">The timestamp frequency in ticks per second.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="ticksPerSecond"/> is not positive.</exception>
        public PresentWaitPacer(IPresentWaitSource source, Func<long> getTimestamp, Action<long> pause, long ticksPerSecond)
        {
            if (ticksPerSecond <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
            }

            _source = source;
            _getTimestamp = getTimestamp;
            _pause = pause;
            _maxSubmissionWaitTicks = ticksPerSecond / 500;
            _submissionPollTicks = ticksPerSecond / 10000;
            _presentationPollTicks = ticksPerSecond / 2000;
            _minPresentationTimeoutTicks = ticksPerSecond / 100;
        }

        /// <summary>Gets whether the previous frame's submission is expected to be in flight at frame start.</summary>
        public bool ExpectsInFlightSubmission { get; private set; } = true;

        /// <summary>Gets the error code of the last <see cref="PresentWaitOutcome.Failed"/> outcome.</summary>
        public int LastError { get; private set; }

        /// <summary>Waits until the frame rendered before the current one has been shown.</summary>
        /// <param name="refreshPeriodTicks">The display refresh period in ticks, used for the timeouts.</param>
        /// <returns>How the wait ended.</returns>
        public PresentWaitOutcome WaitForPreviousFrame(long refreshPeriodTicks)
        {
            var start = _getTimestamp();
            if (!_source.TryQuery(out var generation, out var latest))
            {
                LastError = ErrorUnavailable;
                return PresentWaitOutcome.Failed;
            }
            if (generation == _stalledGeneration && _stalledPresentId != 0 && latest <= _stalledPresentId)
            {
                // Nothing new was presented since a timeout, for example while the window is minimized.
                return PresentWaitOutcome.NoPresentation;
            }

            var target = latest;
            if (ShouldWaitForSubmission())
            {
                var submitted = false;
                var deadline = start + Math.Min(_maxSubmissionWaitTicks, Math.Max(0, refreshPeriodTicks / 4));
                while (true)
                {
                    if (!_source.TryQuery(out var currentGeneration, out var current))
                    {
                        LastError = ErrorUnavailable;
                        return PresentWaitOutcome.Failed;
                    }
                    if (currentGeneration != generation)
                    {
                        return PresentWaitOutcome.SwapchainChanged;
                    }
                    if (current > latest)
                    {
                        target = current;
                        submitted = true;
                        break;
                    }
                    if (_getTimestamp() >= deadline)
                    {
                        break;
                    }
                    _pause(_submissionPollTicks);
                }
                RecordSubmission(submitted);
            }
            if (target == 0)
            {
                return PresentWaitOutcome.NoPresentation;
            }

            var timeout = start + Math.Max(_minPresentationTimeoutTicks, refreshPeriodTicks * 3);
            while (true)
            {
                var result = _source.Poll(generation, target);
                if (result == PollPresented)
                {
                    return PresentWaitOutcome.Presented;
                }
                if (result == PollSwapchainChanged)
                {
                    return PresentWaitOutcome.SwapchainChanged;
                }
                if (result < 0)
                {
                    LastError = result;
                    return PresentWaitOutcome.Failed;
                }
                if (_getTimestamp() >= timeout)
                {
                    _stalledGeneration = generation;
                    _stalledPresentId = target;
                    return PresentWaitOutcome.TimedOut;
                }
                _pause(_presentationPollTicks);
            }
        }

        /// <summary>Decides whether this frame waits briefly for the previous frame's submission.</summary>
        /// <returns>Whether to wait for the submission.</returns>
        private bool ShouldWaitForSubmission()
        {
            if (ExpectsInFlightSubmission)
            {
                return true;
            }
            if (++_framesSinceProbe < InFlightProbeInterval)
            {
                return false;
            }

            _framesSinceProbe = 0;
            return true;
        }

        /// <summary>Updates the expectation of an in-flight submission.</summary>
        /// <param name="submitted">Whether the previous frame's submission arrived during the brief wait.</param>
        private void RecordSubmission(bool submitted)
        {
            if (submitted)
            {
                _inFlightMisses = 0;
                ExpectsInFlightSubmission = true;
                return;
            }
            if (ExpectsInFlightSubmission && ++_inFlightMisses >= InFlightMissLimit)
            {
                ExpectsInFlightSubmission = false;
                _framesSinceProbe = 0;
            }
        }
    }
}
