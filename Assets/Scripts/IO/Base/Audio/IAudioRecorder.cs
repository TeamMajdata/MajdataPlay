#nullable enable
using System;

namespace MajdataPlay.IO
{
    /// <summary>Captures the final audio output as interleaved 32-bit float PCM for a consumer that drains it while recording.</summary>
    /// <remarks>
    /// Start, Stop, and Read may be called from different threads, but only one consumer thread may call Read.
    /// Samples are buffered in fixed storage; a consumer that falls behind loses the newest samples and increases <see cref="DroppedFrames"/>.
    /// </remarks>
    public interface IAudioRecorder : IDisposable
    {
        /// <summary>Gets the sample rate of the captured audio in hertz.</summary>
        int SampleRate { get; }
        /// <summary>Gets the number of interleaved channels in every frame returned by <see cref="Read"/>.</summary>
        int Channels { get; }
        /// <summary>Gets whether output audio is currently being captured.</summary>
        bool IsRecording { get; }
        /// <summary>Gets the number of frames discarded since the last start because the buffer was full.</summary>
        long DroppedFrames { get; }
        /// <summary>Gets how long after the latest <see cref="Start"/> the output device played the first captured frame.</summary>
        /// <remarks>
        /// Captured audio is taken from the output mix before the device buffers it, so this includes both the wait for the first
        /// captured block and the measured output latency. Delaying the samples by this amount places them where they were heard.
        /// It is zero before the first block of a recording is captured, and covers only that wait when the backend cannot
        /// report its output latency.
        /// </remarks>
        TimeSpan PlaybackDelay { get; }

        /// <summary>Discards buffered samples and begins capturing output audio.</summary>
        /// <remarks>Calling this while recording has no effect.</remarks>
        /// <exception cref="ObjectDisposedException">The recorder has been disposed.</exception>
        void Start();

        /// <summary>Stops capturing output audio while keeping already captured samples readable.</summary>
        /// <remarks>Calling this while stopped has no effect.</remarks>
        /// <exception cref="ObjectDisposedException">The recorder has been disposed.</exception>
        void Stop();

        /// <summary>Moves captured samples into the destination in capture order.</summary>
        /// <param name="destination">The buffer receiving interleaved float samples; only whole frames are written.</param>
        /// <returns>The number of float samples written, always a multiple of <see cref="Channels"/>; zero when nothing is buffered.</returns>
        /// <exception cref="ObjectDisposedException">The recorder has been disposed.</exception>
        int Read(Span<float> destination);
    }
}
