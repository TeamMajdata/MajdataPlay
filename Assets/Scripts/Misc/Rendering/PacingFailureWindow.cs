#nullable enable
namespace MajdataPlay.Rendering
{
    /// <summary>
    /// Decides when a frame pacing backend should be given up: when failed frames make up too large a share of the
    /// most recent frames that judged the backend. Unlike a count of consecutive failures, which any success resets,
    /// an occasional success between failures does not keep a mostly failing backend, and its stalls, alive.
    /// </summary>
    internal sealed class PacingFailureWindow
    {
        /// <summary>The number of most recent judged frames that are kept.</summary>
        internal const int WindowSize = 64;

        /// <summary>The number of failed frames within the window at which the backend is given up.</summary>
        internal const int FailureLimit = 16;

        /// <summary>The bit of the oldest frame in <see cref="_history"/>.</summary>
        private const ulong OldestFrameBit = 1UL << (WindowSize - 1);

        /// <summary>One bit per judged frame, the newest in the lowest bit; a set bit marks a failed frame.</summary>
        private ulong _history;

        /// <summary>Gets the number of failed frames among the most recent <see cref="WindowSize"/> judged frames.</summary>
        public int Failures { get; private set; }

        /// <summary>Gets whether <see cref="Failures"/> reached <see cref="FailureLimit"/>.</summary>
        public bool IsExceeded => Failures >= FailureLimit;

        /// <summary>Records a frame the backend paced as intended.</summary>
        public void RecordSuccess()
        {
            Record(false);
        }

        /// <summary>Records a frame the backend failed to pace, such as a timeout or an error.</summary>
        public void RecordFailure()
        {
            Record(true);
        }

        /// <summary>Adds a judged frame to the window and drops the oldest one.</summary>
        /// <param name="failed">Whether the frame failed.</param>
        private void Record(bool failed)
        {
            if ((_history & OldestFrameBit) != 0)
            {
                Failures--;
            }

            _history <<= 1;
            if (failed)
            {
                _history |= 1;
                Failures++;
            }
        }
    }
}
