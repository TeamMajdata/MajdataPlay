using System;
#nullable enable
namespace MajdataPlay
{
    /// <summary>Describes one step of a long-running operation.</summary>
    public readonly struct ProgressReport
    {
        /// <summary>Stores the message, which is null in a default report.</summary>
        private readonly string? _message;

        /// <summary>Gets the message describing the current step, or an empty string for a default report.</summary>
        public string Message
        {
            get => _message ?? string.Empty;
        }

        /// <summary>Gets the number of finished items in the current step.</summary>
        public int Completed { get; }

        /// <summary>Gets the total number of items in the current step, or 0 when the step has no countable items.</summary>
        public int Total { get; }

        /// <summary>Gets whether the step has countable items.</summary>
        public bool HasTotal
        {
            get => Total > 0;
        }

        /// <summary>Gets the finished fraction of the current step between 0 and 1, or 0 when the step has no countable items.</summary>
        public float Fraction
        {
            get
            {
                if (!HasTotal)
                {
                    return 0f;
                }
                return Math.Clamp((float)Completed / Total, 0f, 1f);
            }
        }

        /// <summary>Initializes one progress report.</summary>
        /// <param name="message">The message describing the current step; null is treated as an empty string.</param>
        /// <param name="completed">The number of finished items in the current step.</param>
        /// <param name="total">The total number of items in the current step, or 0 when the step has no countable items.</param>
        public ProgressReport(string? message, int completed = 0, int total = 0)
        {
            _message = message;
            Completed = Math.Max(completed, 0);
            Total = Math.Max(total, 0);
        }

        /// <summary>Formats the report as text for a loading screen.</summary>
        /// <returns>The message, followed by the finished and total item counts and the percentage when the step has countable items.</returns>
        public override string ToString()
        {
            if (!HasTotal)
            {
                return Message;
            }
            return $"{Message}\n{Completed}/{Total} ({(int)(Fraction * 100f)}%)";
        }
    }

    /// <summary>Forwards each progress report of a long-running operation to a callback.</summary>
    /// <remarks>
    /// Unlike <see cref="Progress{T}"/>, the callback runs synchronously on the reporting thread instead of being posted
    /// to a synchronization context, so a main-thread operation updates its UI before it continues.
    /// </remarks>
    public sealed class ProgressReporter : IProgress<ProgressReport>
    {
        /// <summary>The callback receiving each report.</summary>
        private readonly Action<ProgressReport> _handler;

        /// <summary>Initializes a reporter that forwards reports to a callback.</summary>
        /// <param name="handler">The callback receiving each report on the reporting thread.</param>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> is null.</exception>
        public ProgressReporter(Action<ProgressReport> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        /// <summary>Forwards a report to the callback.</summary>
        /// <param name="value">The report to forward.</param>
        public void Report(ProgressReport value)
        {
            _handler(value);
        }
    }
}
