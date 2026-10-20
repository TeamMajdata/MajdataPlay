using System;
using System.Collections.Generic;
#nullable enable
namespace MajdataPlay.Net
{
    /// <summary>
    /// Impression rules for a list with a single focus position (the center of the song list).
    /// A chart counts once it stays focused for <see cref="DWELL_SECONDS"/>, or as soon as the
    /// player commits to it. Hashes are reported once per batch of recommendations and handed to
    /// the sender in groups.
    /// <para>
    /// There is no timer: <see cref="Focus"/> records when the dwell started and the dwell is
    /// settled when the focus leaves. Delayed sending is up to the owner, which is told through
    /// the onQueued callback when the queue stops being empty. Not thread safe; use it from one thread.
    /// </para>
    /// </summary>
    public sealed class ImpressionTracker
    {
        // Same as the website (IMPRESSION_DWELL_MS)
        public const double DWELL_SECONDS = 1;
        // Well below the server's limit of 100 hashes per report
        public const int FLUSH_SIZE = 20;

        public int PendingCount => _queue.Count;

        readonly Func<double> _clock;
        readonly Action<string[]> _send;
        readonly Action? _onQueued;
        readonly HashSet<string> _reported = new(StringComparer.Ordinal);
        readonly List<string> _queue = new();

        string? _focusedHash;
        double _focusedAt;
        bool _isSuspended;

        /// <param name="clock">Monotonic time in seconds</param>
        /// <param name="send">Receives each batch of newly impressed hashes</param>
        /// <param name="onQueued">Called when a hash enters an empty queue</param>
        public ImpressionTracker(Func<double> clock, Action<string[]> send, Action? onQueued = null)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _send = send ?? throw new ArgumentNullException(nameof(send));
            _onQueued = onQueued;
        }
        /// <summary>
        /// The chart has settled in the focus position. Focusing the same chart again keeps the original start time.
        /// </summary>
        public void Focus(string hash)
        {
            if (hash == _focusedHash && !_isSuspended)
            {
                return;
            }
            Blur();
            _focusedHash = hash;
            _focusedAt = _clock();
        }
        /// <summary>
        /// The focused chart has left the focus position.
        /// </summary>
        public void Blur()
        {
            Settle();
            _focusedHash = null;
            _isSuspended = false;
        }
        /// <summary>
        /// The app went to the background. The time spent there does not count towards the dwell.
        /// </summary>
        public void Suspend()
        {
            if (_isSuspended)
            {
                return;
            }
            Settle();
            _isSuspended = true;
        }
        /// <summary>
        /// The app is back; the focused chart starts a new dwell.
        /// </summary>
        public void Resume()
        {
            if (!_isSuspended)
            {
                return;
            }
            _isSuspended = false;
            _focusedAt = _clock();
        }
        /// <summary>
        /// The player started the chart, so it was seen no matter how long it was focused. Sends at once.
        /// </summary>
        public void Commit(string hash)
        {
            Blur();
            Record(hash);
            Flush();
        }
        /// <summary>
        /// A new batch of recommendations was served: sends what is pending and lets every chart be reported again.
        /// </summary>
        public void Reset()
        {
            Blur();
            Flush();
            _reported.Clear();
        }
        public void Flush()
        {
            if (_queue.Count == 0)
            {
                return;
            }
            var batch = _queue.ToArray();
            _queue.Clear();
            _send(batch);
        }
        void Settle()
        {
            if (_focusedHash is null || _isSuspended)
            {
                return;
            }
            if (_clock() - _focusedAt >= DWELL_SECONDS)
            {
                Record(_focusedHash);
            }
        }
        void Record(string hash)
        {
            if (!_reported.Add(hash))
            {
                return;
            }
            _queue.Add(hash);
            if (_queue.Count >= FLUSH_SIZE)
            {
                Flush();
            }
            else if (_queue.Count == 1)
            {
                _onQueued?.Invoke();
            }
        }
    }
}
