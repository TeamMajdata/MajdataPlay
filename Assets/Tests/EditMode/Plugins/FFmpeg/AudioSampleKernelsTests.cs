using System;
using MajdataPlay.FFmpeg;
using MajdataPlay.FFmpeg.Internal;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Verifies the Burst audio sample kernels against the replaced managed loops.</summary>
    public unsafe class AudioSampleKernelsTests
    {
        /// <summary>Creates special and random float samples, including rounding ties, clamping limits and non-finite values.</summary>
        /// <returns>The samples.</returns>
        private static float[] CreateSamples()
        {
            var special = new[]
            {
                0f, -0f, 1f, -1f, 2f, -2f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.Epsilon,
                -float.Epsilon, float.MaxValue, float.MinValue, 0.5f, -0.5f, 1.0000001f, -1.0000001f, 0.99999994f,
                -0.99999994f, -1.0000305f, 1.5f / 32767f, 2.5f / 32767f, -1.5f / 32767f, -2.5f / 32767f,
                32766.5f / 32767f, -32767.5f / 32767f
            };
            var random = new Random(12345);
            var samples = new float[special.Length + 100000];
            special.CopyTo(samples, 0);
            for (var i = special.Length; i < samples.Length; i++)
            {
                samples[i] = (float)((random.NextDouble() * 3.0) - 1.5);
            }

            return samples;
        }

        /// <summary>Checks that the kernel produces exactly the samples of the replaced branching loop.</summary>
        [Test]
        public void ConvertToPcm16_MatchesReplacedLoop()
        {
            var source = CreateSamples();
            var expected = new short[source.Length];
            var actual = new short[source.Length];
            LegacyManagedLoops.ConvertToPcm16(source, expected);

            fixed (float* input = source)
            fixed (short* output = actual)
            {
                AudioSampleKernels.ConvertToPcm16(input, output, source.Length);
            }

            for (var i = 0; i < source.Length; i++)
            {
                if (actual[i] != expected[i])
                {
                    Assert.Fail("sample " + i + " (" + source[i].ToString("R") + ") converts to " + actual[i] + " instead of " + expected[i]);
                }
            }
        }

        /// <summary>Checks that the muxer wrapper rejects a destination that cannot hold every sample.</summary>
        [Test]
        public void MuxerConvertToPcm16_RejectsShortDestination()
        {
            Assert.Throws<ArgumentException>(() => FFmpegMediaMuxer.ConvertToPcm16(new float[4], new short[3]));
            Assert.DoesNotThrow(() => FFmpegMediaMuxer.ConvertToPcm16(new float[3], new short[4]));
        }

        /// <summary>Checks that every channel of interleaved samples is copied to its own plane.</summary>
        /// <param name="channels">The number of interleaved channels.</param>
        /// <param name="frames">The number of frames.</param>
        [TestCase(1, 37)]
        [TestCase(2, 1024)]
        [TestCase(6, 5)]
        [TestCase(8, 0)]
        public void ExtractChannel_CopiesEveryChannel(int channels, int frames)
        {
            var interleaved = new float[channels * frames];
            for (var i = 0; i < interleaved.Length; i++)
            {
                interleaved[i] = i + 0.25f;
            }

            for (var channel = 0; channel < channels; channel++)
            {
                var plane = new float[frames + 1];
                plane[frames] = -1f;
                fixed (float* input = interleaved)
                fixed (float* output = plane)
                {
                    AudioSampleKernels.ExtractChannel(input, channels, channel, output, frames);
                }

                for (var frame = 0; frame < frames; frame++)
                {
                    Assert.That(plane[frame], Is.EqualTo(interleaved[(frame * channels) + channel]), "channel " + channel + " frame " + frame);
                }

                Assert.That(plane[frames], Is.EqualTo(-1f), "the plane is not written past its frames");
            }
        }
    }
}
