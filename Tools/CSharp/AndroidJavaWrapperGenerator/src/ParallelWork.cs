using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Runs indexed CPU work with bounded concurrency and ordered exception propagation.</summary>
    internal static class ParallelWork
    {
        /// <summary>Processes each input index and rethrows the earliest input's original failure.</summary>
        /// <remarks>
        /// A single worker executes directly on the caller's thread. Parallel actions must own
        /// their results independently; shared collections should be committed after this call.
        /// All workers finish before an exception is rethrown, preserving its type and stack trace.
        /// </remarks>
        /// <param name="count">The number of input indices, starting at zero.</param>
        /// <param name="jobs">The maximum worker count; one selects serial execution.</param>
        /// <param name="action">The independent action to execute for each input index.</param>
        /// <param name="ct">The cancellation token checked before each action.</param>
        /// <exception cref="ArgumentOutOfRangeException">The count is negative or jobs is not positive.</exception>
        /// <exception cref="ArgumentNullException">The action is null.</exception>
        /// <exception cref="OperationCanceledException">Cancellation is requested or an action is canceled.</exception>
        /// <exception cref="Exception">An action fails; its original exception is rethrown in input order.</exception>
        internal static void For(int count, int jobs, Action<int> action, CancellationToken ct)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            if (jobs < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(jobs));
            }
            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }
            ct.ThrowIfCancellationRequested();
            if (count == 0)
            {
                return;
            }
            if (jobs == 1 || count == 1)
            {
                for (var index = 0; index < count; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    action(index);
                }
                ct.ThrowIfCancellationRequested();
                return;
            }
            var failures = new ExceptionDispatchInfo?[count];
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(count, jobs)
            };
            Parallel.For(0, count, options, index =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    action(index);
                }
                catch (Exception exception)
                {
                    failures[index] = ExceptionDispatchInfo.Capture(exception);
                }
            });
            for (var index = 0; index < failures.Length; index++)
            {
                failures[index]?.Throw();
            }
            ct.ThrowIfCancellationRequested();
        }
    }
}
