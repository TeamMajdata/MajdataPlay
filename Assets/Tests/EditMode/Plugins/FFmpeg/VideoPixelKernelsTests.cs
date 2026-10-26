using System;
using MajdataPlay.FFmpeg.Internal;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Verifies the Burst pixel kernels against the replaced managed loops and the BT.709 limited-range definition.</summary>
    public unsafe class VideoPixelKernelsTests
    {
        /// <summary>Marks source row padding and untouched output pixels.</summary>
        private const uint PixelSentinel = 0x5EED5EED;
        /// <summary>Marks plane padding bytes that the YUV conversion must not write.</summary>
        private const byte PlaneSentinel = 0xA5;
        /// <summary>Stores the BT.709 red luma weight.</summary>
        private const double Bt709Red = 0.2126;
        /// <summary>Stores the BT.709 blue luma weight.</summary>
        private const double Bt709Blue = 0.0722;

        /// <summary>Creates a source image whose pixels encode their own coordinates, with sentinel row padding.</summary>
        /// <param name="width">The image width in pixels.</param>
        /// <param name="height">The image height in pixels.</param>
        /// <param name="stride">The row stride in pixels, at least the width.</param>
        /// <returns>The source pixels.</returns>
        private static uint[] CreateCoordinateImage(int width, int height, int stride)
        {
            var pixels = new uint[stride * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < stride; x++)
                {
                    pixels[(y * stride) + x] = x < width ? 0x80000000u | ((uint)y << 16) | (uint)x : PixelSentinel;
                }
            }

            return pixels;
        }

        /// <summary>Creates an output buffer filled with <see cref="PixelSentinel"/>.</summary>
        /// <param name="length">The number of pixels.</param>
        /// <returns>The output buffer.</returns>
        private static uint[] CreateOutput(int length)
        {
            var output = new uint[length];
            output.AsSpan().Fill(PixelSentinel);
            return output;
        }

        /// <summary>Checks whether every value of a span equals a sentinel.</summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="values">The values to check.</param>
        /// <param name="sentinel">The expected value.</param>
        /// <returns><see langword="true"/> when no value differs from the sentinel; otherwise, <see langword="false"/>.</returns>
        private static bool IsFilledWith<T>(ReadOnlySpan<T> values, T sentinel) where T : IEquatable<T>
        {
            foreach (var value in values)
            {
                if (!value.Equals(sentinel))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Copies source lines with the Burst kernel.</summary>
        /// <param name="source">The source pixels.</param>
        /// <param name="stride">The source row stride in pixels.</param>
        /// <param name="output">The output pixels.</param>
        /// <param name="width">The source width.</param>
        /// <param name="height">The source height.</param>
        /// <param name="turns">The clockwise quarter turns.</param>
        /// <param name="firstLine">The first source line.</param>
        /// <param name="endLine">The source line after the last one.</param>
        private static void CopyWithKernel(uint[] source, int stride, uint[] output, int width, int height, int turns,
            int firstLine, int endLine)
        {
            fixed (uint* input = source)
            fixed (uint* destination = output)
            {
                VideoPixelKernels.CopyRgbaLines((byte*)input, stride * 4, destination, width, height, turns, firstLine, endLine);
            }
        }

        /// <summary>Copies source lines with the replaced managed loop.</summary>
        /// <param name="source">The source pixels.</param>
        /// <param name="stride">The source row stride in pixels.</param>
        /// <param name="output">The output pixels.</param>
        /// <param name="width">The source width.</param>
        /// <param name="height">The source height.</param>
        /// <param name="turns">The clockwise quarter turns.</param>
        private static void CopyWithLegacyLoop(uint[] source, int stride, uint[] output, int width, int height, int turns)
        {
            fixed (uint* input = source)
            fixed (uint* destination = output)
            {
                LegacyManagedLoops.CopyRgbaLines((byte*)input, stride * 4, destination, width, height, turns, 0,
                    (turns & 1) == 0 ? height : width);
            }
        }

        /// <summary>Checks every rotation of whole images, including sizes that are not multiples of the 4x4 block or 32-pixel tile.</summary>
        /// <param name="width">The source width.</param>
        /// <param name="height">The source height.</param>
        [TestCase(1, 1)]
        [TestCase(1, 7)]
        [TestCase(7, 1)]
        [TestCase(3, 5)]
        [TestCase(4, 4)]
        [TestCase(5, 3)]
        [TestCase(31, 33)]
        [TestCase(33, 31)]
        [TestCase(64, 48)]
        [TestCase(67, 35)]
        [TestCase(130, 70)]
        public void CopyRgbaLines_MatchesReplacedLoop(int width, int height)
        {
            var stride = width + 3;
            var source = CreateCoordinateImage(width, height, stride);
            for (var turns = 0; turns < 4; turns++)
            {
                var expected = CreateOutput(width * height);
                var actual = CreateOutput(width * height);
                CopyWithLegacyLoop(source, stride, expected, width, height, turns);

                CopyWithKernel(source, stride, actual, width, height, turns, 0, (turns & 1) == 0 ? height : width);

                Assert.That(actual, Is.EqualTo(expected), "turns " + turns);
                Assert.That(Array.IndexOf(actual, PixelSentinel), Is.EqualTo(-1), "every output pixel is written for turns " + turns);
            }
        }

        /// <summary>Checks that consecutive line ranges, as copy bands use them, compose the whole image.</summary>
        /// <param name="turns">The clockwise quarter turns.</param>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void CopyRgbaLines_ConsecutiveRangesComposeTheWholeImage(int turns)
        {
            const int width = 75;
            const int height = 54;
            var stride = width + 1;
            var source = CreateCoordinateImage(width, height, stride);
            var lines = (turns & 1) == 0 ? height : width;
            var expected = CreateOutput(width * height);
            var actual = CreateOutput(width * height);
            CopyWithLegacyLoop(source, stride, expected, width, height, turns);

            var first = 0;
            foreach (var end in new[] { 5, 16, 17, 34, lines })
            {
                CopyWithKernel(source, stride, actual, width, height, turns, first, end);
                first = end;
            }

            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>Checks that one line range writes exactly its own whole output rows, so concurrent bands never share a row.</summary>
        /// <param name="turns">The clockwise quarter turns.</param>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void CopyRgbaLines_RangeWritesOnlyItsOutputRows(int turns)
        {
            const int width = 70;
            const int height = 45;
            const int firstLine = 9;
            const int endLine = 30;
            var source = CreateCoordinateImage(width, height, width);
            var outputWidth = (turns & 1) == 0 ? width : height;
            var expected = CreateOutput(width * height);
            var actual = CreateOutput(width * height);
            CopyWithLegacyLoop(source, width, expected, width, height, turns);

            CopyWithKernel(source, width, actual, width, height, turns, firstLine, endLine);

            var writtenRows = 0;
            for (var row = 0; row < actual.Length / outputWidth; row++)
            {
                var rowPixels = actual.AsSpan(row * outputWidth, outputWidth);
                if (rowPixels[0] == PixelSentinel)
                {
                    Assert.That(IsFilledWith<uint>(rowPixels, PixelSentinel), Is.True, "row " + row + " is untouched");
                    continue;
                }

                writtenRows++;
                Assert.That(rowPixels.ToArray(), Is.EqualTo(expected.AsSpan(row * outputWidth, outputWidth).ToArray()), "row " + row);
            }

            Assert.That(writtenRows, Is.EqualTo(endLine - firstLine));
        }

        /// <summary>Creates seeded random RGBA bytes, including any row padding.</summary>
        /// <param name="width">The image width in pixels.</param>
        /// <param name="height">The image height in pixels.</param>
        /// <param name="stride">The row stride in bytes.</param>
        /// <param name="seed">The random seed.</param>
        /// <returns>The packed source bytes.</returns>
        private static byte[] CreateRandomRgba(int width, int height, int stride, int seed)
        {
            var random = new Random(seed);
            var pixels = new byte[stride * height];
            random.NextBytes(pixels);
            return pixels;
        }

        /// <summary>Allocates sentinel-filled planes and describes them for the conversion kernel.</summary>
        /// <param name="width">The image width in pixels.</param>
        /// <param name="height">The image height in pixels.</param>
        /// <param name="interleaved">Whether to describe NV12 instead of YUV420P.</param>
        /// <param name="luma">Receives the luma buffer.</param>
        /// <param name="chroma">Receives the chroma buffer: Cb rows then Cr rows for planar, CbCr rows for NV12.</param>
        /// <param name="lumaStride">Receives the luma row stride, which includes padding.</param>
        /// <param name="chromaStride">Receives the chroma row stride, which includes padding.</param>
        private static void CreatePlanes(int width, int height, bool interleaved, out byte[] luma, out byte[] chroma,
            out int lumaStride, out int chromaStride)
        {
            lumaStride = width + 5;
            chromaStride = interleaved ? width + 4 : (width / 2) + 3;
            luma = new byte[lumaStride * height];
            chroma = new byte[chromaStride * (height / 2) * (interleaved ? 1 : 2)];
            luma.AsSpan().Fill(PlaneSentinel);
            chroma.AsSpan().Fill(PlaneSentinel);
        }

        /// <summary>Converts row pairs with the Burst kernel into managed planes.</summary>
        /// <param name="source">The packed source bytes.</param>
        /// <param name="sourceOffset">The byte offset of output row 0 in the source.</param>
        /// <param name="sourceStride">The signed source row stride in bytes.</param>
        /// <param name="width">The image width.</param>
        /// <param name="height">The image height.</param>
        /// <param name="interleaved">Whether the planes are NV12.</param>
        /// <param name="luma">The luma buffer.</param>
        /// <param name="chroma">The chroma buffer.</param>
        /// <param name="lumaStride">The luma row stride.</param>
        /// <param name="chromaStride">The chroma row stride.</param>
        /// <param name="firstPair">The first row pair.</param>
        /// <param name="endPair">The row pair after the last one.</param>
        private static void ConvertWithKernel(byte[] source, long sourceOffset, long sourceStride, int width, int height,
            bool interleaved, byte[] luma, byte[] chroma, int lumaStride, int chromaStride, int firstPair, int endPair)
        {
            fixed (byte* input = source)
            fixed (byte* lumaPlane = luma)
            fixed (byte* chromaPlane = chroma)
            {
                var planes = new Yuv420Planes
                {
                    Luma = lumaPlane,
                    LumaStride = lumaStride,
                    BlueDifference = chromaPlane,
                    BlueDifferenceStride = chromaStride,
                    RedDifference = interleaved ? chromaPlane + 1 : chromaPlane + (chromaStride * (height / 2)),
                    RedDifferenceStride = chromaStride,
                    ChromaStep = interleaved ? 2 : 1
                };
                VideoPixelKernels.ConvertRgbaToYuv420(input + sourceOffset, sourceStride, width, in planes, firstPair, endPair);
            }
        }

        /// <summary>Computes the ideal limited-range BT.709 code for full-range RGB.</summary>
        /// <param name="red">The red value from 0 to 255.</param>
        /// <param name="green">The green value from 0 to 255.</param>
        /// <param name="blue">The blue value from 0 to 255.</param>
        /// <param name="component">Zero for Y, one for Cb, two for Cr.</param>
        /// <returns>The unrounded code.</returns>
        private static double IdealCode(double red, double green, double blue, int component)
        {
            var luma = (Bt709Red * red) + ((1.0 - Bt709Red - Bt709Blue) * green) + (Bt709Blue * blue);
            switch (component)
            {
                case 0:
                    return 16.0 + (219.0 / 255.0 * luma);
                case 1:
                    return 128.0 + (112.0 / 255.0 * (blue - luma) / (1.0 - Bt709Blue));
                default:
                    return 128.0 + (112.0 / 255.0 * (red - luma) / (1.0 - Bt709Red));
            }
        }

        /// <summary>Checks luma and 2x2-mean chroma against the BT.709 definition, the plane layout, and that padding is never written.</summary>
        /// <param name="width">The even image width.</param>
        /// <param name="height">The even image height.</param>
        /// <param name="interleaved">Whether to write NV12 instead of YUV420P.</param>
        [TestCase(2, 2, false)]
        [TestCase(6, 4, false)]
        [TestCase(8, 2, true)]
        [TestCase(14, 6, true)]
        [TestCase(64, 18, false)]
        [TestCase(70, 18, true)]
        public void ConvertRgbaToYuv420_MatchesBt709LimitedRange(int width, int height, bool interleaved)
        {
            var sourceStride = (width * 4) + 8;
            var source = CreateRandomRgba(width, height, sourceStride, (width * 31) + height);
            CreatePlanes(width, height, interleaved, out var luma, out var chroma, out var lumaStride, out var chromaStride);

            ConvertWithKernel(source, 0, sourceStride, width, height, interleaved, luma, chroma, lumaStride, chromaStride, 0, height / 2);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var pixel = (y * sourceStride) + (x * 4);
                    var ideal = IdealCode(source[pixel], source[pixel + 1], source[pixel + 2], 0);
                    Assert.That(luma[(y * lumaStride) + x], Is.EqualTo(ideal).Within(1.0), "Y at " + x + "," + y);
                }

                Assert.That(IsFilledWith<byte>(luma.AsSpan((y * lumaStride) + width, lumaStride - width), PlaneSentinel), Is.True,
                    "luma row padding " + y);
            }

            for (var row = 0; row < height / 2; row++)
            {
                for (var column = 0; column < width / 2; column++)
                {
                    var means = new double[3];
                    for (var channel = 0; channel < 3; channel++)
                    {
                        for (var block = 0; block < 4; block++)
                        {
                            means[channel] += source[(((2 * row) + (block >> 1)) * sourceStride) + (((2 * column) + (block & 1)) * 4) + channel] / 4.0;
                        }
                    }

                    var blue = interleaved ? chroma[(row * chromaStride) + (2 * column)] : chroma[(row * chromaStride) + column];
                    var red = interleaved
                        ? chroma[(row * chromaStride) + (2 * column) + 1]
                        : chroma[((row + (height / 2)) * chromaStride) + column];
                    Assert.That(blue, Is.EqualTo(IdealCode(means[0], means[1], means[2], 1)).Within(1.0), "Cb at " + column + "," + row);
                    Assert.That(red, Is.EqualTo(IdealCode(means[0], means[1], means[2], 2)).Within(1.0), "Cr at " + column + "," + row);
                }
            }

            var chromaRows = chroma.Length / chromaStride;
            var usedChromaBytes = interleaved ? width : width / 2;
            for (var row = 0; row < chromaRows; row++)
            {
                Assert.That(IsFilledWith<byte>(chroma.AsSpan((row * chromaStride) + usedChromaBytes, chromaStride - usedChromaBytes), PlaneSentinel),
                    Is.True, "chroma row padding " + row);
            }
        }

        /// <summary>Checks that every gray level maps exactly to its limited-range luma code and neutral chroma.</summary>
        [Test]
        public void ConvertRgbaToYuv420_GrayIsExactAndNeutral()
        {
            const int width = 512;
            const int height = 2;
            var source = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    // Each 2x2 block holds one gray level, so its chroma is that level's chroma.
                    var level = (byte)(x / 2);
                    var pixel = ((y * width) + x) * 4;
                    source[pixel] = level;
                    source[pixel + 1] = level;
                    source[pixel + 2] = level;
                    source[pixel + 3] = (byte)(255 - level);
                }
            }

            CreatePlanes(width, height, false, out var luma, out var chroma, out var lumaStride, out var chromaStride);

            ConvertWithKernel(source, 0, width * 4, width, height, false, luma, chroma, lumaStride, chromaStride, 0, 1);

            for (var level = 0; level < 256; level++)
            {
                var expected = (byte)Math.Round(16.0 + (219.0 * level / 255.0));
                Assert.That(luma[2 * level], Is.EqualTo(expected), "Y of gray " + level);
                Assert.That(luma[lumaStride + (2 * level) + 1], Is.EqualTo(expected), "Y of gray " + level + " in the lower row");
                Assert.That(chroma[level], Is.EqualTo(128), "Cb of gray " + level);
                Assert.That(chroma[chromaStride + level], Is.EqualTo(128), "Cr of gray " + level);
            }
        }

        /// <summary>Checks that NV12 holds exactly the planar samples, interleaved.</summary>
        [Test]
        public void ConvertRgbaToYuv420_Nv12InterleavesThePlanarSamples()
        {
            const int width = 46;
            const int height = 10;
            var source = CreateRandomRgba(width, height, width * 4, 7);
            CreatePlanes(width, height, false, out var planarLuma, out var planar, out var lumaStride, out var planarStride);
            CreatePlanes(width, height, true, out var nv12Luma, out var nv12, out _, out var nv12Stride);

            ConvertWithKernel(source, 0, width * 4, width, height, false, planarLuma, planar, lumaStride, planarStride, 0, height / 2);
            ConvertWithKernel(source, 0, width * 4, width, height, true, nv12Luma, nv12, lumaStride, nv12Stride, 0, height / 2);

            Assert.That(nv12Luma, Is.EqualTo(planarLuma));
            for (var row = 0; row < height / 2; row++)
            {
                for (var column = 0; column < width / 2; column++)
                {
                    Assert.That(nv12[(row * nv12Stride) + (2 * column)], Is.EqualTo(planar[(row * planarStride) + column]));
                    Assert.That(nv12[(row * nv12Stride) + (2 * column) + 1],
                        Is.EqualTo(planar[((row + (height / 2)) * planarStride) + column]));
                }
            }
        }

        /// <summary>Checks that a negative stride reads bottom-first input exactly like the same image stored top-first.</summary>
        /// <param name="interleaved">Whether to write NV12 instead of YUV420P.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void ConvertRgbaToYuv420_NegativeStrideReadsBottomFirstRows(bool interleaved)
        {
            const int width = 38;
            const int height = 12;
            var rowBytes = width * 4;
            var topFirst = CreateRandomRgba(width, height, rowBytes, 11);
            var bottomFirst = new byte[topFirst.Length];
            for (var y = 0; y < height; y++)
            {
                topFirst.AsSpan(y * rowBytes, rowBytes).CopyTo(bottomFirst.AsSpan((height - 1 - y) * rowBytes, rowBytes));
            }

            CreatePlanes(width, height, interleaved, out var expectedLuma, out var expectedChroma, out var lumaStride, out var chromaStride);
            CreatePlanes(width, height, interleaved, out var actualLuma, out var actualChroma, out _, out _);

            ConvertWithKernel(topFirst, 0, rowBytes, width, height, interleaved, expectedLuma, expectedChroma, lumaStride, chromaStride, 0, height / 2);
            ConvertWithKernel(bottomFirst, (height - 1L) * rowBytes, -rowBytes, width, height, interleaved, actualLuma, actualChroma,
                lumaStride, chromaStride, 0, height / 2);

            Assert.That(actualLuma, Is.EqualTo(expectedLuma));
            Assert.That(actualChroma, Is.EqualTo(expectedChroma));
        }

        /// <summary>Checks that consecutive row-pair ranges, as conversion bands use them, compose the whole frame.</summary>
        [Test]
        public void ConvertRgbaToYuv420_ConsecutivePairRangesComposeTheWholeFrame()
        {
            const int width = 54;
            const int height = 26;
            var source = CreateRandomRgba(width, height, width * 4, 3);
            CreatePlanes(width, height, false, out var expectedLuma, out var expectedChroma, out var lumaStride, out var chromaStride);
            CreatePlanes(width, height, false, out var actualLuma, out var actualChroma, out _, out _);
            ConvertWithKernel(source, 0, width * 4, width, height, false, expectedLuma, expectedChroma, lumaStride, chromaStride, 0, height / 2);

            var first = 0;
            foreach (var end in new[] { 1, 6, 7, height / 2 })
            {
                ConvertWithKernel(source, 0, width * 4, width, height, false, actualLuma, actualChroma, lumaStride, chromaStride, first, end);
                first = end;
            }

            Assert.That(actualLuma, Is.EqualTo(expectedLuma));
            Assert.That(actualChroma, Is.EqualTo(expectedChroma));
        }
    }
}
