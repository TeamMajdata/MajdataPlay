#nullable enable
using System;

namespace MajdataPlay.FFmpeg.Internal
{
    /// <summary>Assigns frame timestamps to camera renders so that render timing jitter does not drop frames.</summary>
    /// <remarks>
    /// Quantizing each render time independently drops frames whenever renders arrive near a frame boundary: jitter places one
    /// render just before the boundary, where it repeats the previous frame and is discarded, and the next render just after the
    /// following boundary, which skips a frame. This timeline instead records a render as the next frame while it is within
    /// <see cref="Tolerance"/> frame periods of that frame's time, so consecutive renders stay on consecutive frames, and
    /// resynchronizes to the nearest frame only after a render arrives more than the tolerance late. Timestamps therefore stay
    /// within the tolerance of the clock. Use one instance from one thread.
    /// </remarks>
    internal sealed class CaptureFrameTimeline
    {
        /// <summary>Sets how many frame periods a render may be away from the next frame time and still be recorded as that frame.</summary>
        /// <remarks>It exceeds one half so that render jitter around a resynchronized timeline does not reach the opposite limit.</remarks>
        public const double Tolerance = 0.75;
        /// <summary>Sets the weight of the newest interval in the smoothed render interval.</summary>
        private const double RenderIntervalSmoothing = 0.125;
        /// <summary>Stores the last assigned frame timestamp, or -1 before the first render.</summary>
        private long _lastFrameIndex = -1;
        /// <summary>Stores the time of the previous render in frame periods, or NaN before the first render.</summary>
        private double _lastRenderPosition = double.NaN;
        /// <summary>Stores the smoothed time between renders in frame periods, or NaN before two renders.</summary>
        private double _renderInterval = double.NaN;

        /// <summary>Gets the number of frame periods in which no render was recorded because the camera rendered slower than the frame rate.</summary>
        public long SourceGapFrames { get; private set; }
        /// <summary>Gets the last assigned frame timestamp, or -1 before the first render.</summary>
        public long LastFrameIndex => _lastFrameIndex;

        /// <summary>Forgets all renders so that the next render starts a new recording.</summary>
        public void Reset()
        {
            _lastFrameIndex = -1;
            _lastRenderPosition = double.NaN;
            _renderInterval = double.NaN;
            SourceGapFrames = 0;
        }

        /// <summary>Chooses the frame timestamp of a render, or rejects it when a render closer to the next frame time is expected.</summary>
        /// <remarks>
        /// When renders are faster than the frame rate, the tolerance for early renders shrinks with the smoothed render interval so that
        /// the render closest to each frame time is kept; smoothing keeps a single short interval caused by jitter from rejecting a
        /// render. The first render of a recording is timestamped to the nearest frame.
        /// </remarks>
        /// <param name="position">The render time in frame periods since the recording clock started; it must not decrease.</param>
        /// <param name="frameIndex">Receives the strictly increasing, nonnegative frame timestamp when the render is recorded.</param>
        /// <returns>True when the render is recorded as <paramref name="frameIndex"/>; false when it should be skipped.</returns>
        public bool TryAssign(double position, out long frameIndex)
        {
            if (!double.IsNaN(_lastRenderPosition))
            {
                var interval = Math.Max(0, position - _lastRenderPosition);
                _renderInterval = double.IsNaN(_renderInterval) ? interval : _renderInterval + ((interval - _renderInterval) * RenderIntervalSmoothing);
            }
            _lastRenderPosition = position;
            var next = _lastFrameIndex + 1;
            var lead = position - next;
            if (_lastFrameIndex >= 0)
            {
                var earlyTolerance = Tolerance * (double.IsNaN(_renderInterval) ? 1 : Math.Min(1, _renderInterval));
                if (lead < -earlyTolerance)
                {
                    frameIndex = 0;
                    return false;
                }
            }

            frameIndex = _lastFrameIndex >= 0 && lead < Tolerance
                ? next
                : Math.Max(next, (long)Math.Floor(position + 0.5));
            SourceGapFrames += frameIndex - next;
            _lastFrameIndex = frameIndex;
            return true;
        }
    }
}
