using System;

namespace MajdataPlay.FFmpeg.Internal
{
    /// <summary>Estimates compressed video bit rate over one second of the media timeline.</summary>
    /// <remarks>
    /// Used only by the decoder worker. Packet timestamps may arrive out of presentation order.
    /// Bytes are distributed uniformly over a packet's duration when it crosses a window boundary.
    /// </remarks>
    internal sealed class VideoBitRateTracker
    {
        private const int Capacity = 4096;
        private const double WindowSeconds = 1;
        private readonly Sample[] _samples = new Sample[Capacity];
        private int _count;
        private double _firstTimestamp = double.PositiveInfinity;
        private double _incompleteThrough = double.NegativeInfinity;

        private struct Sample
        {
            public double Start, End;
            public int Bytes;
        }

        public void Add(double timestamp, double duration, int byteCount)
        {
            if (!IsFinite(timestamp) || !IsFinite(duration) || duration <= 0 || byteCount <= 0) return;
            var end = timestamp + duration;
            if (!IsFinite(end) || end <= timestamp) return;
            if (_count == Capacity)
            {
                // A malformed source or unusually deep decoder buffering must not
                // grow memory indefinitely or report a partial window as a low rate.
                for (var i = 0; i < _count; i++)
                    _incompleteThrough = Math.Max(_incompleteThrough, _samples[i].End);
                _count = 0;
                _firstTimestamp = double.PositiveInfinity;
            }
            _firstTimestamp = Math.Min(_firstTimestamp, timestamp);
            _samples[_count++] = new Sample { Start = timestamp, End = end, Bytes = byteCount };
        }

        /// <summary>Returns bits per second at the given presentation-frame end, or zero when unavailable.</summary>
        /// <remarks>Measure frames in presentation order; call Reset before seeking backwards.</remarks>
        public long Measure(double frameEnd)
        {
            if (!IsFinite(frameEnd)) return 0;
            var windowStart = frameEnd - WindowSeconds;
            var coveredStart = Math.Max(windowStart, _firstTimestamp);
            var coveredSeconds = frameEnd - coveredStart;
            double bytes = 0;
            var retained = 0;
            for (var i = 0; i < _count; i++)
            {
                var sample = _samples[i];
                if (sample.End <= windowStart) continue;
                _samples[retained++] = sample;
                var overlap = Math.Min(frameEnd, sample.End) - Math.Max(coveredStart, sample.Start);
                if (overlap > 0)
                    bytes += sample.Bytes * (overlap / (sample.End - sample.Start));
            }
            _count = retained;
            if (coveredSeconds <= 0 || windowStart < _incompleteThrough || bytes <= 0) return 0;
            var bitsPerSecond = bytes * 8 / coveredSeconds;
            return bitsPerSecond >= long.MaxValue ? long.MaxValue : (long)Math.Round(bitsPerSecond);
        }

        public void Reset()
        {
            _count = 0;
            _firstTimestamp = double.PositiveInfinity;
            _incompleteThrough = double.NegativeInfinity;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
