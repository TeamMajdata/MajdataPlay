#nullable enable
using System;
using System.Diagnostics;
using System.Threading;

namespace MajdataPlay.FFmpeg.Internal
{
    /// <summary>Polls short GPU operations without imposing a millisecond timer delay on every decoded frame.</summary>
    internal struct GpuCompletionWait
    {
        /// <summary>Bounds yielding polls before backing off for a slow or unresponsive GPU.</summary>
        // A bounded decode submission can cover several reference pictures plus
        // a snapshot copy. Avoid a coarse OS timer before one 60 Hz interval;
        // genuinely slow or hung work still backs off cancellably afterward.
        private const int FastPollMilliseconds = 16;
        /// <summary>Stores the monotonic deadline for the complete GPU operation.</summary>
        private readonly long _deadline;
        /// <summary>Stores the monotonic deadline for short spin/yield polling.</summary>
        private readonly long _fastPollDeadline;
        /// <summary>Progressively yields execution without using a positive-duration timer in the fast path.</summary>
        private SpinWait _spinWait;

        /// <summary>Starts a bounded worker-side wait for a single GPU completion operation.</summary>
        /// <param name="timeoutMilliseconds">The positive maximum wait duration in milliseconds.</param>
        /// <exception cref="ArgumentOutOfRangeException">The timeout is not positive.</exception>
        internal GpuCompletionWait(int timeoutMilliseconds)
        {
            if (timeoutMilliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            }

            var now = Stopwatch.GetTimestamp();
            _deadline = now + (long)(timeoutMilliseconds * (double)Stopwatch.Frequency / 1000);
            _fastPollDeadline = now + (long)(FastPollMilliseconds * (double)Stopwatch.Frequency / 1000);
            _spinWait = new SpinWait();
        }

        /// <summary>Yields before the next completion query, backing off only after the short-poll budget expires.</summary>
        /// <param name="cancellationToken">Cancels the wait when the owning decode session closes.</param>
        /// <exception cref="OperationCanceledException">The decode session was canceled.</exception>
        /// <exception cref="TimeoutException">The GPU operation exceeded its deadline.</exception>
        internal void WaitForNextPoll(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = Stopwatch.GetTimestamp();
            if (now >= _deadline)
            {
                throw new TimeoutException("Timed out waiting for GPU video decoding on the background worker.");
            }

            if (now < _fastPollDeadline)
            {
                // Only use the non-yielding SpinOnce path, then yield explicitly
                // and reset before SpinWait can escalate to a timer-based Sleep(1).
                if (_spinWait.NextSpinWillYield)
                {
                    Thread.Yield();
                    _spinWait.Reset();
                }
                else
                {
                    _spinWait.SpinOnce();
                }
            }
            else if (cancellationToken.WaitHandle.WaitOne(1))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}
