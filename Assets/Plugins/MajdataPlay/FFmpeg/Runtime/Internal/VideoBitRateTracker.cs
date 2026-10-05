#nullable enable
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
        /// <summary>Limits retained packet samples to keep malformed input from growing memory without bound.</summary>
        private const int Capacity = 4096;
        /// <summary>Defines the media-time window used to estimate compressed video bit rate.</summary>
        private const double WindowSeconds = 1;
        /// <summary>Stores retained packet time spans and compressed byte counts.</summary>
        private readonly Sample[] _samples = new Sample[Capacity];
        /// <summary>Counts valid packet samples in the fixed storage array.</summary>
        private int _count;
        /// <summary>Records the earliest timestamp contributing to the current estimate.</summary>
        private double _firstTimestamp = double.PositiveInfinity;
        /// <summary>Marks the last timestamp affected by capacity overflow, before which estimates are incomplete.</summary>
        private double _incompleteThrough = double.NegativeInfinity;
        /// <summary>Stores a compressed packet's media time span and byte count.</summary>
        private struct Sample
        {
            /// <summary>Bound the packet's contribution on the media timeline, in seconds.</summary>
            public double Start, End;
            /// <summary>Stores the compressed video packet size in bytes.</summary>
            public int Bytes;
        }

        /// <summary>Records a valid packet time span and byte count for subsequent frame estimates.</summary>
        /// <param name="timestamp">The packet start time on the media timeline, in seconds.</param>
        /// <param name="duration">The frame or packet duration in seconds.</param>
        /// <param name="byteCount">The compressed video packet size in bytes.</param>
        public void Add(double timestamp, double duration, int byteCount)
        {
            if (!IsFinite(timestamp) || !IsFinite(duration) || duration <= 0 || byteCount <= 0)
            {
                return;
            }

            var end = timestamp + duration;
            if (!IsFinite(end) || end <= timestamp)
            {
                return;
            }

            if (_count == Capacity)
            {
                // A malformed source or unusually deep decoder buffering must not
                // grow memory indefinitely or report a partial window as a low rate.
                for (var i = 0; i < _count; i++)
                {
                    _incompleteThrough = Math.Max(_incompleteThrough, _samples[i].End);
                }

                _count = 0;
                _firstTimestamp = double.PositiveInfinity;
            }

            _firstTimestamp = Math.Min(_firstTimestamp, timestamp);
            _samples[_count++] = new Sample
            {
                Start = timestamp,
                End = end,
                Bytes = byteCount
            };
        }

        /// <summary>Returns bits per second at the given presentation-frame end, or zero when unavailable.</summary>
        /// <remarks>Measure frames in presentation order; call Reset before seeking backwards.</remarks>
        /// <param name="frameEnd">The displayed frame's end timestamp on the media timeline, in seconds.</param>
        /// <returns>The estimated bits per second, or zero for incomplete or unavailable history.</returns>
        public long Measure(double frameEnd)
        {
            if (!IsFinite(frameEnd))
            {
                return 0;
            }

            var windowStart = frameEnd - WindowSeconds;
            var coveredStart = Math.Max(windowStart, _firstTimestamp);
            var coveredSeconds = frameEnd - coveredStart;
            double bytes = 0;
            var retained = 0;
            for (var i = 0; i < _count; i++)
            {
                var sample = _samples[i];
                if (sample.End <= windowStart)
                {
                    continue;
                }

                _samples[retained++] = sample;
                var overlap = Math.Min(frameEnd, sample.End) - Math.Max(coveredStart, sample.Start);
                if (overlap > 0)
                {
                    bytes += sample.Bytes * (overlap / (sample.End - sample.Start));
                }
            }

            _count = retained;
            if (coveredSeconds <= 0 || windowStart < _incompleteThrough || bytes <= 0)
            {
                return 0;
            }

            var bitsPerSecond = bytes * 8 / coveredSeconds;
            return bitsPerSecond >= long.MaxValue ? long.MaxValue : (long)Math.Round(bitsPerSecond);
        }

        /// <summary>Discards packet history before seeking or restarting the media timeline.</summary>
        public void Reset()
        {
            _count = 0;
            _firstTimestamp = double.PositiveInfinity;
            _incompleteThrough = double.NegativeInfinity;
        }

        /// <summary>Checks whether a timestamp or duration is neither infinity nor NaN.</summary>
        /// <param name="value">The value to validate or assign.</param>
        /// <returns>True if the value is neither NaN nor infinity.</returns>
        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
