using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

#nullable enable
namespace MajdataPlay.ResourceUpdate
{
    /// <summary>Splits synchronous main-thread work across frames so that the loading screen keeps rendering progress.</summary>
    /// <remarks>
    /// Resource work stays on the main thread because storage backends such as Android SAF and the APK archive are not
    /// guaranteed to be usable from worker threads. Call <see cref="YieldIfExceededAsync"/> between work items.
    /// </remarks>
    internal sealed class FrameBudget
    {
        /// <summary>The default work time per frame, in milliseconds.</summary>
        public const long DefaultBudgetMilliseconds = 8;

        /// <summary>Measures the work time since the last yield.</summary>
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        /// <summary>The work time per frame, in milliseconds.</summary>
        private readonly long _budgetMilliseconds;

        /// <summary>Initializes a budget that starts measuring immediately.</summary>
        /// <param name="budgetMilliseconds">The work time per frame, in milliseconds; 0 yields between every item.</param>
        public FrameBudget(long budgetMilliseconds = DefaultBudgetMilliseconds)
        {
            _budgetMilliseconds = budgetMilliseconds < 0 ? 0 : budgetMilliseconds;
        }

        /// <summary>Yields to the next frame once the work time since the last yield exceeds the budget.</summary>
        /// <param name="cancellationToken">Cancels the work, for example when its owner is destroyed.</param>
        /// <returns>An operation that completes immediately within the budget, or on the next frame after it.</returns>
        /// <exception cref="System.OperationCanceledException">The token was cancelled.</exception>
        public ValueTask YieldIfExceededAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_stopwatch.ElapsedMilliseconds < _budgetMilliseconds)
            {
                return default;
            }
            return YieldAsync(cancellationToken);
        }

        /// <summary>Yields to the next frame and starts measuring the next frame's work.</summary>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns>An operation that completes on the next frame.</returns>
        /// <exception cref="System.OperationCanceledException">The token was cancelled.</exception>
        private async ValueTask YieldAsync(CancellationToken cancellationToken)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            _stopwatch.Restart();
        }
    }
}
