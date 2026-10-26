using System;
using FFmpeg.AutoGen;
using MajdataPlay.FFmpeg.Internal;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Verifies capture RGBA to YUV conversion into real FFmpeg frames, its threading, and its agreement with libswscale.</summary>
    public unsafe class CaptureFrameConverterTests
    {
        /// <summary>Skips the fixture when the native FFmpeg libraries are unavailable.</summary>
        [SetUp]
        public void SetUp()
        {
            FFmpegTestMedia.RequireNativeLibraries();
        }

        /// <summary>Creates seeded random packed RGBA bytes.</summary>
        /// <param name="width">The frame width.</param>
        /// <param name="height">The frame height.</param>
        /// <param name="seed">The random seed.</param>
        /// <returns>The packed pixels.</returns>
        private static byte[] CreateRandomRgba(int width, int height, int seed)
        {
            var pixels = new byte[width * height * 4];
            new Random(seed).NextBytes(pixels);
            return pixels;
        }

        /// <summary>Creates packed RGBA bytes whose top and bottom halves have different flat colors.</summary>
        /// <param name="width">The frame width.</param>
        /// <param name="height">The frame height.</param>
        /// <param name="top">The RGB color of the top half.</param>
        /// <param name="bottom">The RGB color of the bottom half.</param>
        /// <returns>The packed top-first pixels.</returns>
        private static byte[] CreateHalves(int width, int height, (byte Red, byte Green, byte Blue) top,
            (byte Red, byte Green, byte Blue) bottom)
        {
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
            {
                var color = y < height / 2 ? top : bottom;
                for (var x = 0; x < width; x++)
                {
                    var pixel = ((y * width) + x) * 4;
                    pixels[pixel] = color.Red;
                    pixels[pixel + 1] = color.Green;
                    pixels[pixel + 2] = color.Blue;
                    pixels[pixel + 3] = 255;
                }
            }

            return pixels;
        }

        /// <summary>Reads one sample of a YUV420P or NV12 frame.</summary>
        /// <param name="frame">The frame to read.</param>
        /// <param name="component">Zero for Y, one for Cb, two for Cr.</param>
        /// <param name="x">The column in the component's plane.</param>
        /// <param name="y">The row in the component's plane.</param>
        /// <returns>The sample value.</returns>
        private static byte ReadSample(AVFrame* frame, int component, int x, int y)
        {
            if (component == 0)
            {
                return frame->data[0][(y * frame->linesize[0]) + x];
            }

            if ((AVPixelFormat)frame->format == AVPixelFormat.AV_PIX_FMT_NV12)
            {
                return frame->data[1][(y * frame->linesize[1]) + (2 * x) + component - 1];
            }

            return frame->data[(uint)component][(y * frame->linesize[(uint)component]) + x];
        }

        /// <summary>Asserts that two frames hold exactly the same visible samples.</summary>
        /// <param name="expected">The reference frame.</param>
        /// <param name="actual">The frame to check.</param>
        /// <param name="message">The assertion context.</param>
        private static void AssertSameSamples(AVFrame* expected, AVFrame* actual, string message)
        {
            for (var component = 0; component < 3; component++)
            {
                var width = component == 0 ? expected->width : expected->width / 2;
                var height = component == 0 ? expected->height : expected->height / 2;
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        if (ReadSample(expected, component, x, y) != ReadSample(actual, component, x, y))
                        {
                            Assert.Fail(message + ": component " + component + " differs at " + x + "," + y);
                        }
                    }
                }
            }
        }

        /// <summary>Packs the visible samples of a YUV420P or NV12 frame as a tightly packed NV12 image.</summary>
        /// <param name="frame">The frame to read.</param>
        /// <returns>The Y plane followed by the interleaved Cb, Cr rows.</returns>
        private static byte[] PackNv12(AVFrame* frame)
        {
            var width = frame->width;
            var height = frame->height;
            var packed = new byte[width * height * 3 / 2];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    packed[(y * width) + x] = ReadSample(frame, 0, x, y);
                }
            }

            for (var y = 0; y < height / 2; y++)
            {
                for (var x = 0; x < width / 2; x++)
                {
                    var sample = (width * height) + (y * width) + (2 * x);
                    packed[sample] = ReadSample(frame, 1, x, y);
                    packed[sample + 1] = ReadSample(frame, 2, x, y);
                }
            }

            return packed;
        }

        /// <summary>Checks the packed frame sizes of both capture layouts.</summary>
        [Test]
        public void GetFrameByteCount_ReturnsPackedSizes()
        {
            Assert.That(CapturePixelLayout.Rgba32.GetFrameByteCount(540, 1200), Is.EqualTo(540 * 1200 * 4));
            Assert.That(CapturePixelLayout.Nv12.GetFrameByteCount(540, 1200), Is.EqualTo(540 * 1200 * 3 / 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => ((CapturePixelLayout)99).GetFrameByteCount(2, 2));
        }

        /// <summary>Checks NV12 copy length, destination and format validation.</summary>
        [Test]
        public void CopyNv12_RejectsInvalidInput()
        {
            var nv12 = new byte[8 * 4 * 3 / 2];
            var rgba = NativeFrames.Allocate(8, 4, AVPixelFormat.AV_PIX_FMT_RGBA);
            var smaller = NativeFrames.Allocate(8, 2, AVPixelFormat.AV_PIX_FMT_NV12);
            var valid = NativeFrames.Allocate(8, 4, AVPixelFormat.AV_PIX_FMT_NV12);
            try
            {
                Assert.Throws<ArgumentException>(() => CaptureFrameConverter.CopyNv12(new byte[8 * 4 * 4], 8, 4, valid));
                Assert.Throws<ArgumentNullException>(() => CaptureFrameConverter.CopyNv12(nv12, 8, 4, null));
                Assert.Throws<NotSupportedException>(() => CaptureFrameConverter.CopyNv12(nv12, 8, 4, rgba));
                Assert.Throws<NotSupportedException>(() => CaptureFrameConverter.CopyNv12(nv12, 8, 4, smaller));
                Assert.DoesNotThrow(() => CaptureFrameConverter.CopyNv12(nv12, 8, 4, valid));
            }
            finally
            {
                NativeFrames.Free(rgba);
                NativeFrames.Free(smaller);
                NativeFrames.Free(valid);
            }
        }

        /// <summary>Checks that copying a converted NV12 image reproduces the converted samples in both destination formats.</summary>
        /// <param name="format">The destination format.</param>
        [TestCase(AVPixelFormat.AV_PIX_FMT_YUV420P)]
        [TestCase(AVPixelFormat.AV_PIX_FMT_NV12)]
        public void CopyNv12_ReproducesConvertedSamples(AVPixelFormat format)
        {
            const int width = 322;
            const int height = 200;
            var pixels = CreateRandomRgba(width, height, 11);
            var converted = NativeFrames.Allocate(width, height, AVPixelFormat.AV_PIX_FMT_NV12);
            var expected = NativeFrames.Allocate(width, height, format);
            var copied = NativeFrames.Allocate(width, height, format);
            try
            {
                using var converter = new CaptureFrameConverter(width, height, 1);
                converter.Convert(pixels, true, converted);
                converter.Convert(pixels, true, expected);
                CaptureFrameConverter.CopyNv12(PackNv12(converted), width, height, copied);

                AssertSameSamples(expected, copied, "format " + format);
            }
            finally
            {
                NativeFrames.Free(converted);
                NativeFrames.Free(expected);
                NativeFrames.Free(copied);
            }
        }

        /// <summary>Checks constructor validation.</summary>
        [Test]
        public void Constructor_RejectsInvalidArguments()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CaptureFrameConverter(0, 2, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CaptureFrameConverter(3, 2, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CaptureFrameConverter(2, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CaptureFrameConverter(2, 5, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CaptureFrameConverter(2, 2, 0));
        }

        /// <summary>Checks that each thread converts at least 32 rows and that bands cover every row pair.</summary>
        /// <param name="height">The frame height.</param>
        /// <param name="maximumThreads">The requested maximum.</param>
        /// <param name="threads">The expected thread count.</param>
        /// <param name="bands">The expected band count.</param>
        [TestCase(48, 4, 1, 1)]
        [TestCase(64, 1, 1, 1)]
        [TestCase(64, 2, 2, 2)]
        [TestCase(64, 8, 2, 2)]
        [TestCase(200, 3, 3, 3)]
        [TestCase(1080, 8, 8, 8)]
        public void ThreadCount_UsesAtLeast32RowsPerThread(int height, int maximumThreads, int threads, int bands)
        {
            using var converter = new CaptureFrameConverter(64, height, maximumThreads);

            Assert.That(converter.ThreadCount, Is.EqualTo(threads));
            Assert.That(converter.BandCount, Is.EqualTo(bands));
            Assert.That(converter.StartedThreadCount, Is.EqualTo(0));
        }

        /// <summary>Checks the encoder input formats the converter produces.</summary>
        [Test]
        public void Supports_OnlyYuv420PAndNv12()
        {
            Assert.That(CaptureFrameConverter.Supports(AVPixelFormat.AV_PIX_FMT_YUV420P), Is.True);
            Assert.That(CaptureFrameConverter.Supports(AVPixelFormat.AV_PIX_FMT_NV12), Is.True);
            Assert.That(CaptureFrameConverter.Supports(AVPixelFormat.AV_PIX_FMT_RGBA), Is.False);
            Assert.That(CaptureFrameConverter.Supports(AVPixelFormat.AV_PIX_FMT_YUV444P), Is.False);
        }

        /// <summary>Checks input length, destination and format validation.</summary>
        [Test]
        public void Convert_RejectsInvalidInput()
        {
            using var converter = new CaptureFrameConverter(8, 4, 1);
            var pixels = new byte[8 * 4 * 4];
            var rgba = NativeFrames.Allocate(8, 4, AVPixelFormat.AV_PIX_FMT_RGBA);
            var smaller = NativeFrames.Allocate(8, 2, AVPixelFormat.AV_PIX_FMT_YUV420P);
            var valid = NativeFrames.Allocate(8, 4, AVPixelFormat.AV_PIX_FMT_YUV420P);
            try
            {
                Assert.Throws<ArgumentException>(() => converter.Convert(new byte[10], false, valid));
                Assert.Throws<ArgumentNullException>(() => converter.Convert(pixels, false, null));
                Assert.Throws<NotSupportedException>(() => converter.Convert(pixels, false, rgba));
                Assert.Throws<NotSupportedException>(() => converter.Convert(pixels, false, smaller));
                Assert.DoesNotThrow(() => converter.Convert(pixels, false, valid));
            }
            finally
            {
                NativeFrames.Free(rgba);
                NativeFrames.Free(smaller);
                NativeFrames.Free(valid);
            }
        }

        /// <summary>Checks that banded multi-threaded conversion produces exactly the single-threaded samples.</summary>
        /// <param name="format">The destination format.</param>
        /// <param name="threads">The requested conversion threads.</param>
        [TestCase(AVPixelFormat.AV_PIX_FMT_YUV420P, 4)]
        [TestCase(AVPixelFormat.AV_PIX_FMT_YUV420P, 3)]
        [TestCase(AVPixelFormat.AV_PIX_FMT_NV12, 4)]
        public void Convert_MultipleThreadsMatchOneThread(AVPixelFormat format, int threads)
        {
            const int width = 322;
            const int height = 200;
            var pixels = CreateRandomRgba(width, height, threads);
            var expected = NativeFrames.Allocate(width, height, format);
            var actual = NativeFrames.Allocate(width, height, format);
            try
            {
                using var single = new CaptureFrameConverter(width, height, 1);
                using var banded = new CaptureFrameConverter(width, height, threads);
                foreach (var flip in new[] { false, true })
                {
                    single.Convert(pixels, flip, expected);
                    banded.Convert(pixels, flip, actual);

                    AssertSameSamples(expected, actual, "flip " + flip);
                }

                Assert.That(banded.BandCount, Is.EqualTo(threads));
                Assert.That(banded.StartedThreadCount, Is.EqualTo(threads - 1));
            }
            finally
            {
                NativeFrames.Free(expected);
                NativeFrames.Free(actual);
            }
        }

        /// <summary>Checks that flat colors convert to the same limited-range BT.709 samples as the replaced libswscale path.</summary>
        /// <param name="format">The destination format.</param>
        [TestCase(AVPixelFormat.AV_PIX_FMT_YUV420P)]
        [TestCase(AVPixelFormat.AV_PIX_FMT_NV12)]
        public void Convert_FlatColorsMatchLibswscale(AVPixelFormat format)
        {
            const int width = 24;
            const int height = 8;
            var colors = new (byte, byte, byte)[]
            {
                (0, 0, 0), (255, 255, 255), (128, 128, 128), (255, 0, 0), (0, 255, 0), (0, 0, 255),
                (0, 255, 255), (255, 0, 255), (255, 255, 0), (37, 201, 99), (250, 10, 130), (3, 64, 252)
            };
            var expected = NativeFrames.Allocate(width, height, format);
            var actual = NativeFrames.Allocate(width, height, format);
            try
            {
                using var reference = new LegacySwscaleCapture(width, height, format, 1);
                using var converter = new CaptureFrameConverter(width, height, 1);
                foreach (var color in colors)
                {
                    var pixels = CreateHalves(width, height, color, color);
                    reference.Convert(pixels, false, expected);
                    converter.Convert(pixels, false, actual);

                    for (var component = 0; component < 3; component++)
                    {
                        var planeWidth = component == 0 ? width : width / 2;
                        var planeHeight = component == 0 ? height : height / 2;
                        for (var y = 0; y < planeHeight; y++)
                        {
                            for (var x = 0; x < planeWidth; x++)
                            {
                                Assert.That(ReadSample(actual, component, x, y), Is.EqualTo(ReadSample(expected, component, x, y)).Within(1),
                                    "color " + color + " component " + component + " at " + x + "," + y);
                            }
                        }
                    }
                }
            }
            finally
            {
                NativeFrames.Free(expected);
                NativeFrames.Free(actual);
            }
        }

        /// <summary>Checks that bottom-first input lands in the same rows as the replaced libswscale path, away from the color edge.</summary>
        /// <param name="flipVertically">Whether the input rows are bottom-first.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void Convert_OrientationMatchesLibswscale(bool flipVertically)
        {
            const int width = 16;
            const int height = 32;
            var pixels = CreateHalves(width, height, (250, 200, 40), (20, 30, 200));
            var expected = NativeFrames.Allocate(width, height, AVPixelFormat.AV_PIX_FMT_YUV420P);
            var actual = NativeFrames.Allocate(width, height, AVPixelFormat.AV_PIX_FMT_YUV420P);
            try
            {
                using var reference = new LegacySwscaleCapture(width, height, AVPixelFormat.AV_PIX_FMT_YUV420P, 1);
                using var converter = new CaptureFrameConverter(width, height, 1);
                reference.Convert(pixels, flipVertically, expected);
                converter.Convert(pixels, flipVertically, actual);

                foreach (var row in new[] { 0, 5, 12, 19, 26, 31 })
                {
                    Assert.That(ReadSample(actual, 0, 3, row), Is.EqualTo(ReadSample(expected, 0, 3, row)).Within(1), "Y row " + row);
                }

                foreach (var row in new[] { 0, 3, 12, 15 })
                {
                    for (var component = 1; component < 3; component++)
                    {
                        Assert.That(ReadSample(actual, component, 2, row), Is.EqualTo(ReadSample(expected, component, 2, row)).Within(1),
                            "component " + component + " row " + row);
                    }
                }

                // The first output row holds the image's top color unless the input was bottom-first.
                var topLuma = ReadSample(actual, 0, 0, 0);
                if (flipVertically)
                {
                    Assert.That(topLuma, Is.LessThan(120));
                }
                else
                {
                    Assert.That(topLuma, Is.GreaterThan(120));
                }
            }
            finally
            {
                NativeFrames.Free(expected);
                NativeFrames.Free(actual);
            }
        }
    }
}
