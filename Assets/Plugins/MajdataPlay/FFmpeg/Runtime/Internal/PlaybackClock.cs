#nullable enable
using System;
using System.Diagnostics;

namespace MajdataPlay.FFmpeg.Internal
{
    // Monotonic, independent of Time.timeScale; injecting time makes discontinuities testable.
    /// <summary>Tracks a monotonic, rate-adjustable media timeline independently of Unity's time scale.</summary>
    internal sealed class PlaybackClock
    {
        /// <summary>Provides monotonic time in seconds, independently of Unity's time scale.</summary>
        private readonly Func<double> _now;
        /// <summary>Store the wall-clock anchor, anchored media position in seconds, and playback multiplier.</summary>
        private double _anchor, _position, _rate = 1;
        /// <summary>Gets whether the media clock is advancing.</summary>
        public bool Running { get; private set; }

        /// <summary>Initializes a playback clock using an injectable monotonic time source.</summary>
        /// <param name="now">An optional monotonic time provider returning seconds; null uses Stopwatch.</param>
        public PlaybackClock(Func<double>? now = null)
        {
            _now = now ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        }

        /// <summary>Gets the current media position in seconds.</summary>
        public double Position => _position + (Running ? (_now() - _anchor) * _rate : 0);

        /// <summary>Gets or sets the playback multiplier while preserving the current media position.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The rate is not finite or is outside the inclusive range 0.0625 to 16.</exception>
        public double Rate
        {
            get => _rate;
            set
            {
                ValidateRate(value);
                Set(Position);
                _rate = value;
            }
        }

        /// <summary>Validates a playback multiplier without requiring a clock instance.</summary>
        /// <param name="value">The requested finite multiplier in the inclusive range 0.0625 to 16.</param>
        /// <exception cref="ArgumentOutOfRangeException">The value is not finite or outside the supported range.</exception>
        public static void ValidateRate(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0625 || value > 16)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Playback rate must be in [0.0625, 16].");
            }
        }

        /// <summary>Reanchors the media timeline at the specified position.</summary>
        /// <param name="seconds">The media timeline position in seconds.</param>
        public void Set(double seconds)
        {
            _position = seconds;
            _anchor = _now();
        }

        /// <summary>Starts advancing the clock without changing its anchored media position.</summary>
        public void Start()
        {
            if (!Running)
            {
                _anchor = _now();
                Running = true;
            }
        }

        /// <summary>Freezes the clock at its current media position.</summary>
        public void Pause()
        {
            Set(Position);
            Running = false;
        }
    }
}
