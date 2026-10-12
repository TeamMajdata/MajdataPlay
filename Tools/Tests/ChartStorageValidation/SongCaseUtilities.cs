#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MajdataPlay.Tests.ChartStorageValidation
{
    /// <summary>Supplies the non-pumping caller context used by synchronous refresh validation.</summary>
    internal static partial class Program
    {
        /// <summary>Explicitly refreshes both sources for legacy tests without introducing a production full-refresh API.</summary>
        /// <param name="progressReporter">Receives each source's progress in sequence.</param>
        /// <param name="cancellationToken">Cancels either source or waiting for its shared lock.</param>
        /// <returns>A task completing after the local and then the online refresh.</returns>
        /// <exception cref="OperationCanceledException">Either source refresh is canceled.</exception>
        private static async Task RefreshBothSourcesAsync(IProgress<string>? progressReporter = null, CancellationToken cancellationToken = default)
        {
            await SongStorage.RefreshLocal(progressReporter, cancellationToken);
            await SongStorage.RefreshOnline(progressReporter, cancellationToken);
        }

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
