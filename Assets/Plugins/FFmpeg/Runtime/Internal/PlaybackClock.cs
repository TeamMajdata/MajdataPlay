using System;
using System.Diagnostics;

namespace MajdataPlay.Video.Internal
{
    // Monotonic, independent of Time.timeScale; injecting time makes discontinuities testable.
    internal sealed class PlaybackClock
    {
        readonly Func<double> _now;
        double _anchor, _position, _rate = 1;
        public bool Running { get; private set; }
        public PlaybackClock(Func<double> now = null)
        {
            _now = now ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        }
        public double Position => _position + (Running ? (_now() - _anchor) * _rate : 0);
        public double Rate
        {
            get => _rate;
            set
            {
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0625 || value > 16)
                    throw new ArgumentOutOfRangeException(nameof(value), "Playback rate must be in [0.0625, 16].");
                Set(Position);
                _rate = value;
            }
        }
        public void Set(double seconds) { _position = seconds; _anchor = _now(); }
        public void Start() { if (!Running) { _anchor = _now(); Running = true; } }
        public void Pause() { Set(Position); Running = false; }
    }
}
