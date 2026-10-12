#nullable enable
using System;
using System.Threading;

namespace MajdataPlay.Tests.ChartStorageValidation
{
    /// <summary>Supplies the non-pumping caller context used by synchronous refresh validation.</summary>
    internal static partial class Program
    {
        /// <summary>Detects synchronous refresh implementations that capture a non-pumping context.</summary>
        private sealed class RejectingSynchronizationContext : SynchronizationContext
        {
            /// <summary>Rejects attempts to resume work on the thread blocked by Refresh.</summary>
            /// <param name="callback">The attempted continuation.</param>
            /// <param name="state">The continuation state.</param>
            /// <exception cref="InvalidOperationException">A synchronous refresh captured this context.</exception>
            public override void Post(SendOrPostCallback callback, object? state)
            {
                throw new InvalidOperationException("Synchronous Refresh must not capture the caller's synchronization context.");
            }
        }
    }
}
