using System;
using System.Collections;
using System.Diagnostics;
using MajdataPlay.Diagnostics;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Provides frame-based waits and log handling for Play Mode tests whose work completes on later frames.</summary>
    public static class PlayModeWait
    {
        /// <summary>Yields frames until a condition holds or the time limit elapses; the caller asserts the outcome.</summary>
        /// <param name="condition">The condition to wait for.</param>
        /// <param name="seconds">The wall-clock limit in seconds.</param>
        /// <returns>The waiting coroutine.</returns>
        public static IEnumerator Until(Func<bool> condition, double seconds = 10)
        {
            var watch = Stopwatch.StartNew();
            while (!condition() && watch.Elapsed.TotalSeconds < seconds)
            {
                yield return null;
            }
        }

        /// <summary>Yields a fixed number of frames.</summary>
        /// <param name="count">The number of frames.</param>
        /// <returns>The waiting coroutine.</returns>
        public static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++)
            {
                yield return null;
            }
        }

        /// <summary>Tolerates error logs that the code under test reports for an expected failure.</summary>
        /// <remarks>MajDebug forwards queued messages from a background writer, so their timing cannot be matched with LogAssert.Expect.</remarks>
        public static void ExpectErrorLogs()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        /// <summary>Waits briefly for worker threads to queue their diagnostics and then flushes them within the current test.</summary>
        /// <returns>The waiting coroutine.</returns>
        public static IEnumerator FlushLogs()
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < 0.3)
            {
                yield return null;
            }

            MajDebug.FlushLog();
            yield return null;
        }
    }
}
