#nullable enable
using Unity.Burst;
using Unity.Mathematics;

namespace MajdataPlay.FFmpeg.Internal
{
    /// <summary>Provides the Burst-compiled sample loops that prepare recorded float PCM for the muxer's audio stream.</summary>
    /// <remarks>
    /// Every entry point is a Burst direct call over borrowed raw pointers and may run on any thread. Without Burst the same
    /// code runs as managed code and produces identical samples.
    /// </remarks>
    [BurstCompile]
    internal static unsafe class AudioSampleKernels
    {
        /// <summary>Scales nominal float samples from -1 to 1 onto the symmetric signed 16-bit range.</summary>
        internal const float Pcm16Scale = short.MaxValue;

        /// <summary>Converts float samples to signed 16-bit PCM, clamping values outside the nominal range.</summary>
        /// <param name="source">The float samples.</param>
        /// <param name="destination">The 16-bit samples, which do not overlap the source.</param>
        /// <param name="count">The number of samples to convert.</param>
        /// <remarks>Scaled samples round to the nearest integer with ties to even; NaN becomes silence.</remarks>
        [BurstCompile]
        public static void ConvertToPcm16([NoAlias] float* source, [NoAlias] short* destination, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var scaled = source[i] * Pcm16Scale;
                var rounded = (int)math.round(math.clamp(scaled, short.MinValue, short.MaxValue));
                destination[i] = math.isnan(scaled) ? (short)0 : (short)rounded;
            }
        }

        /// <summary>Copies one channel of interleaved float samples into its own plane.</summary>
        /// <param name="interleaved">The interleaved samples, with one sample of every channel per frame.</param>
        /// <param name="channels">The number of interleaved channels.</param>
        /// <param name="channel">The zero-based channel to copy.</param>
        /// <param name="plane">The destination plane, which does not overlap the interleaved samples.</param>
        /// <param name="frames">The number of frames to copy.</param>
        [BurstCompile]
        public static void ExtractChannel([NoAlias] float* interleaved, int channels, int channel, [NoAlias] float* plane,
            int frames)
        {
            for (var frame = 0; frame < frames; frame++)
            {
                plane[frame] = interleaved[((long)frame * channels) + channel];
            }
        }
    }
}
