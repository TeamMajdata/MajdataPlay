#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using MajdataPlay.FFmpeg;
using MajdataPlay.FFmpeg.Internal;

namespace MajdataPlay.FFmpeg.Validation
{
    /// <summary>Checks GPU polling, cancellation, the allocation-free short-wait path, and transient playback underflow.</summary>
    internal static class GpuWaitChecks
    {
        /// <summary>Runs managed checks without requiring a GPU or invoking Unity native APIs.</summary>
        /// <returns>The number of successfully checked invariants.</returns>
        /// <exception cref="InvalidOperationException">A GPU wait invariant is not preserved.</exception>
        internal static int RunManaged()
        {
            var rejected = false;
            try
            {
                _ = new GpuCompletionWait(0);
            }
            catch (ArgumentOutOfRangeException)
            {
                rejected = true;
            }

            Check(rejected, "GPU polling rejects a nonpositive timeout.");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var canceled = false;
            var wait = new GpuCompletionWait(100);
            try
            {
                wait.WaitForNextPoll(cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }

            Check(canceled, "GPU polling observes cancellation before yielding or sleeping.");
            wait = new GpuCompletionWait(1);
            Thread.Sleep(10);
            var timedOut = false;
            try
            {
                wait.WaitForNextPoll(CancellationToken.None);
            }
            catch (TimeoutException)
            {
                timedOut = true;
            }

            Check(timedOut, "GPU polling stops at its monotonic deadline.");
            for (var index = 0; index < 256; index++)
            {
                wait = new GpuCompletionWait(100);
                wait.WaitForNextPoll(CancellationToken.None);
            }

            var watch = Stopwatch.StartNew();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 4096; index++)
            {
                wait = new GpuCompletionWait(100);
                wait.WaitForNextPoll(CancellationToken.None);
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            watch.Stop();
            Check(allocated == 0, "Short GPU polls do not allocate per-frame managed storage.");
            Check(watch.Elapsed.TotalSeconds < 1, "Short GPU polls do not impose a 1ms timer wait per query.");
            Console.WriteLine("GPU short polling: " + allocated + " managed bytes / 4096 waits; "
                + watch.Elapsed.TotalMilliseconds.ToString("F2") + " ms; cancellation and timeout passed.");
            Check(!FFmpegVideoPlayer.HasPlaybackUnderflow(1.02, 1, 1),
                "A short asynchronous decode gap does not freeze 1x playback.");
            Check(FFmpegVideoPlayer.HasPlaybackUnderflow(1.11, 1, 1),
                "A sustained decode gap still enters buffering at 1x.");
            Check(!FFmpegVideoPlayer.HasPlaybackUnderflow(1.25, 1, 3),
                "High-rate grace is measured in wall-clock time, not media time.");
            Check(FFmpegVideoPlayer.HasPlaybackUnderflow(1.34, 1, 3),
                "A sustained high-rate gap still enters buffering.");
            Check(FFmpegVideoPlayer.HasPlaybackUnderflow(1.03, 1, 0.25),
                "Slow-rate grace is also bounded in wall-clock time.");
            return 10;
        }

        /// <summary>Reports a failed managed wait invariant.</summary>
        /// <param name="condition">Whether the expected behavior was observed.</param>
        /// <param name="message">The diagnostic describing the expected behavior.</param>
        /// <exception cref="InvalidOperationException">The observed behavior is invalid.</exception>
        private static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
