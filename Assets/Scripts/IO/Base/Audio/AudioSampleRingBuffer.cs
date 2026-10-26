#nullable enable
using System;

namespace MajdataPlay.IO
{
    /// <summary>Stores a bounded window of interleaved float audio frames in capture order.</summary>
    /// <remarks>
    /// Writes keep only the first <see cref="Channels"/> channels of each source frame, so a multichannel
    /// device mix is reduced to its front pair. Writes that do not fit are dropped instead of overwriting
    /// unread audio. This type is not thread-safe; callers synchronize writers and readers.
    /// </remarks>
    internal sealed class AudioSampleRingBuffer
    {
        /// <summary>Scales signed 16-bit PCM into the nominal float range.</summary>
        private const float Pcm16Scale = 1f / 32768f;
        /// <summary>Stores <see cref="CapacityFrames"/> frames of interleaved samples.</summary>
        private readonly float[] _samples;
        /// <summary>Stores the frame index of the oldest unread frame.</summary>
        private int _readFrame;
        /// <summary>Stores the number of unread frames.</summary>
        private int _availableFrames;

        /// <summary>Allocates fixed storage for the given layout and duration.</summary>
        /// <param name="channels">The number of interleaved channels kept per frame, at least one.</param>
        /// <param name="capacityFrames">The maximum number of unread frames, at least one.</param>
        /// <exception cref="ArgumentOutOfRangeException">A count is less than one or the storage would exceed the array size limit.</exception>
        public AudioSampleRingBuffer(int channels, int capacityFrames)
        {
            if (channels < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(channels));
            }
            if (capacityFrames < 1 || (long)capacityFrames * channels > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(capacityFrames));
            }
            Channels = channels;
            CapacityFrames = capacityFrames;
            _samples = new float[capacityFrames * channels];
        }

        /// <summary>Gets the number of interleaved channels kept per frame.</summary>
        public int Channels { get; }
        /// <summary>Gets the maximum number of unread frames.</summary>
        public int CapacityFrames { get; }
        /// <summary>Gets the number of unread frames.</summary>
        public int AvailableFrames => _availableFrames;
        /// <summary>Gets the number of frames discarded because the buffer was full since the last clear.</summary>
        public long DroppedFrames { get; private set; }

        /// <summary>Discards unread frames and resets the dropped-frame count.</summary>
        public void Clear()
        {
            _readFrame = 0;
            _availableFrames = 0;
            DroppedFrames = 0;
        }

        /// <summary>Appends interleaved float frames, keeping the leading channels of each frame.</summary>
        /// <param name="source">The interleaved source samples; a trailing partial frame is ignored.</param>
        /// <param name="sourceChannels">The number of interleaved channels in the source, at least <see cref="Channels"/>.</param>
        /// <returns>The number of frames stored; frames that do not fit are counted in <see cref="DroppedFrames"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The source has fewer channels than this buffer keeps.</exception>
        public int Write(ReadOnlySpan<float> source, int sourceChannels)
        {
            var frames = PrepareWrite(source.Length, sourceChannels);
            var writeFrame = (_readFrame + _availableFrames) % CapacityFrames;
            for (var frame = 0; frame < frames; frame++)
            {
                var sourceOffset = frame * sourceChannels;
                var targetOffset = writeFrame * Channels;
                for (var channel = 0; channel < Channels; channel++)
                {
                    _samples[targetOffset + channel] = source[sourceOffset + channel];
                }
                writeFrame = writeFrame + 1 == CapacityFrames ? 0 : writeFrame + 1;
            }
            _availableFrames += frames;
            return frames;
        }

        /// <summary>Appends interleaved signed 16-bit frames converted to float, keeping the leading channels of each frame.</summary>
        /// <param name="source">The interleaved source samples; a trailing partial frame is ignored.</param>
        /// <param name="sourceChannels">The number of interleaved channels in the source, at least <see cref="Channels"/>.</param>
        /// <returns>The number of frames stored; frames that do not fit are counted in <see cref="DroppedFrames"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The source has fewer channels than this buffer keeps.</exception>
        public int Write(ReadOnlySpan<short> source, int sourceChannels)
        {
            var frames = PrepareWrite(source.Length, sourceChannels);
            var writeFrame = (_readFrame + _availableFrames) % CapacityFrames;
            for (var frame = 0; frame < frames; frame++)
            {
                var sourceOffset = frame * sourceChannels;
                var targetOffset = writeFrame * Channels;
                for (var channel = 0; channel < Channels; channel++)
                {
                    _samples[targetOffset + channel] = source[sourceOffset + channel] * Pcm16Scale;
                }
                writeFrame = writeFrame + 1 == CapacityFrames ? 0 : writeFrame + 1;
            }
            _availableFrames += frames;
            return frames;
        }

        /// <summary>Appends silent frames, for example to fill time in which the source delivered no audio.</summary>
        /// <param name="frames">The number of silent frames to append.</param>
        /// <returns>The number of frames stored; frames that do not fit are counted in <see cref="DroppedFrames"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="frames"/> is negative.</exception>
        public int WriteSilence(int frames)
        {
            if (frames < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frames));
            }
            var writable = Math.Min(frames, CapacityFrames - _availableFrames);
            DroppedFrames += frames - writable;
            // The free region wraps at most once, so it is cleared as at most two contiguous parts.
            var writeFrame = (_readFrame + _availableFrames) % CapacityFrames;
            var firstFrames = Math.Min(writable, CapacityFrames - writeFrame);
            _samples.AsSpan(writeFrame * Channels, firstFrames * Channels).Clear();
            var secondFrames = writable - firstFrames;
            if (secondFrames > 0)
            {
                _samples.AsSpan(0, secondFrames * Channels).Clear();
            }
            _availableFrames += writable;
            return writable;
        }

        /// <summary>Moves the oldest unread frames into the destination.</summary>
        /// <param name="destination">The buffer receiving interleaved samples; only whole frames are written.</param>
        /// <returns>The number of float samples written, always a multiple of <see cref="Channels"/>.</returns>
        public int Read(Span<float> destination)
        {
            var frames = Math.Min(_availableFrames, destination.Length / Channels);
            if (frames == 0)
            {
                return 0;
            }
            // The unread region wraps at most once, so it is copied as at most two contiguous parts.
            var firstFrames = Math.Min(frames, CapacityFrames - _readFrame);
            _samples.AsSpan(_readFrame * Channels, firstFrames * Channels).CopyTo(destination);
            var secondFrames = frames - firstFrames;
            if (secondFrames > 0)
            {
                _samples.AsSpan(0, secondFrames * Channels).CopyTo(destination.Slice(firstFrames * Channels));
            }
            _readFrame = (_readFrame + frames) % CapacityFrames;
            _availableFrames -= frames;
            return frames * Channels;
        }

        /// <summary>Validates a write and records frames that do not fit.</summary>
        /// <param name="sampleCount">The number of interleaved source samples.</param>
        /// <param name="sourceChannels">The number of interleaved channels in the source.</param>
        /// <returns>The number of whole frames that fit in the free space.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The source has fewer channels than this buffer keeps.</exception>
        private int PrepareWrite(int sampleCount, int sourceChannels)
        {
            if (sourceChannels < Channels)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceChannels));
            }
            var frames = sampleCount / sourceChannels;
            var writable = Math.Min(frames, CapacityFrames - _availableFrames);
            DroppedFrames += frames - writable;
            return writable;
        }
    }
}
