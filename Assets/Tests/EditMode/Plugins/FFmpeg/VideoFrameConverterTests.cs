using System;
using FFmpeg.AutoGen;
using MajdataPlay.FFmpeg.Internal;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Verifies RGBA conversion, rotation, cropping and banded copying with the real libswscale.</summary>
    public unsafe class VideoFrameConverterTests
    {
        /// <summary>Skips the fixture when the native FFmpeg libraries are unavailable.</summary>
        [SetUp]
        public void SetUp()
        {
            FFmpegTestMedia.RequireNativeLibraries();
        }

        /// <summary>Computes the source pixel that a rotated, vertically flipped output pixel must contain.</summary>
        /// <param name="turns">The clockwise quarter turns.</param>
        /// <param name="width">The source width.</param>
        /// <param name="height">The source height.</param>
        /// <param name="row">The output row.</param>
        /// <param name="column">The output column.</param>
        /// <returns>The expected pattern value.</returns>
        private static uint Expected(int turns, int width, int height, int row, int column)
        {
            switch (turns)
            {
                case 0:
                    return NativeFrames.Pattern(column, height - 1 - row);
                case 1:
                    return NativeFrames.Pattern(width - 1 - row, height - 1 - column);
                case 2:
                    return NativeFrames.Pattern(width - 1 - column, row);
                default:
                    return NativeFrames.Pattern(row, column);
            }
        }

        /// <summary>Checks that the converter requires at least one thread.</summary>
        [Test]
        public void Constructor_RejectsZeroThreads()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new VideoFrameConverter(null, 0));
        }

        /// <summary>Checks pixel placement and metadata for every quarter-turn rotation of an RGBA source.</summary>
        /// <param name="rotation">The requested clockwise rotation in degrees.</param>
        /// <param name="turns">The equivalent normalized quarter turns.</param>
        [TestCase(0.0, 0)]
        [TestCase(90.0, 1)]
        [TestCase(180.0, 2)]
        [TestCase(270.0, 3)]
        [TestCase(-90.0, 3)]
        [TestCase(450.0, 1)]
        [TestCase(44.0, 0)]
        public void Convert_Rgba_RotatesAndFlipsPixels(double rotation, int turns)
        {
            const int width = 5;
            const int height = 3;
            var source = NativeFrames.Allocate(width, height, AVPixelFormat.AV_PIX_FMT_RGBA);
            try
            {
                NativeFrames.FillRgba(source);
                using var converter = new VideoFrameConverter();
                using var result = converter.Convert(source, 1.5, 0.25, rotation, 1024);
                var outputWidth = (turns & 1) == 0 ? width : height;
                var outputHeight = (turns & 1) == 0 ? height : width;

                Assert.That(result.Width, Is.EqualTo(outputWidth));
                Assert.That(result.Height, Is.EqualTo(outputHeight));
                Assert.That(result.Stride, Is.EqualTo(outputWidth * 4));
                Assert.That(result.DataSize, Is.EqualTo(width * height * 4));
                Assert.That(result.PixelFormat, Is.EqualTo(AVPixelFormat.AV_PIX_FMT_RGBA));
                Assert.That(result.PresentationTime, Is.EqualTo(1.5));
                Assert.That(result.Duration, Is.EqualTo(0.25));
                Assert.That(result.RotationDegrees, Is.EqualTo(0));
                Assert.That(result.HardwareDecoded, Is.False);
                Assert.That(result.IsHardwareFrame, Is.False);
                Assert.That(result.TransferMode, Is.EqualTo("Software RGBA upload"));
                Assert.That(converter.CopyBandCount, Is.EqualTo(1));
                for (var row = 0; row < outputHeight; row++)
                {
                    for (var column = 0; column < outputWidth; column++)
                    {
                        Assert.That(NativeFrames.ReadPixel(result.Data, outputWidth, row, column),
                            Is.EqualTo(Expected(turns, width, height, row, column)), "pixel " + row + "," + column);
                    }
                }
            }
            finally
            {
                NativeFrames.Free(source);
            }
        }

        /// <summary>Checks that the pixel aspect ratio is reported and inverted for quarter turns.</summary>
        [Test]
        public void Convert_ReportsPixelAspectRatio()
        {
            var source = NativeFrames.Allocate(4, 2, AVPixelFormat.AV_PIX_FMT_RGBA);
            try
            {
                NativeFrames.FillRgba(source);
                using var converter = new VideoFrameConverter();
                using (var unset = converter.Convert(source, 0, 0, 0, 1024))
                {
                    Assert.That(unset.PixelAspectRatio, Is.EqualTo(1));
                }

                source->sample_aspect_ratio = new AVRational { num = 2, den = 1 };
                using (var straight = converter.Convert(source, 0, 0, 0, 1024))
                {
                    Assert.That(straight.PixelAspectRatio, Is.EqualTo(2));
                }

                using (var rotated = converter.Convert(source, 0, 0, 90, 1024))
                {
                    Assert.That(rotated.PixelAspectRatio, Is.EqualTo(0.5));
                }
            }
            finally
            {
                NativeFrames.Free(source);
            }
        }

        /// <summary>Checks limited and full range YUV conversion for each supported color matrix.</summary>
        /// <param name="colorspace">The signalled color space.</param>
        /// <param name="height">The frame height, which selects the default matrix for unspecified spaces.</param>
        [TestCase(AVColorSpace.AVCOL_SPC_BT709, 4)]
        [TestCase(AVColorSpace.AVCOL_SPC_FCC, 4)]
        [TestCase(AVColorSpace.AVCOL_SPC_BT470BG, 4)]
        [TestCase(AVColorSpace.AVCOL_SPC_SMPTE170M, 4)]
        [TestCase(AVColorSpace.AVCOL_SPC_SMPTE240M, 4)]
        [TestCase(AVColorSpace.AVCOL_SPC_BT2020_NCL, 4)]
        [TestCase(AVColorSpace.AVCOL_SPC_BT2020_CL, 4)]
        [TestCase(AVColorSpace.AVCOL_SPC_UNSPECIFIED, 4)]
        [TestCase(AVColorSpace.AVCOL_SPC_UNSPECIFIED, 578)]
        public void Convert_Yuv_ProducesNeutralGray(AVColorSpace colorspace, int height)
        {
            var source = NativeFrames.Allocate(8, height, AVPixelFormat.AV_PIX_FMT_YUV420P);
            try
            {
                using var converter = new VideoFrameConverter();
                source->colorspace = colorspace;
                foreach (var sample in new[] { (16, AVColorRange.AVCOL_RANGE_MPEG, 0), (235, AVColorRange.AVCOL_RANGE_MPEG, 255), (128, AVColorRange.AVCOL_RANGE_JPEG, 128) })
                {
                    NativeFrames.FillYuv420(source, (byte)sample.Item1);
                    source->color_range = sample.Item2;
                    using var result = converter.Convert(source, 0, 0, 0, 1 << 20);
                    var pixel = NativeFrames.ReadPixel(result.Data, 8, 0, 0);

                    for (var channel = 0; channel < 3; channel++)
                    {
                        var value = (int)((pixel >> (channel * 8)) & 0xFF);
                        Assert.That(value, Is.EqualTo(sample.Item3).Within(3), colorspace + " channel " + channel);
                    }

                    Assert.That(pixel >> 24, Is.EqualTo(255u));
                }
            }
            finally
            {
                NativeFrames.Free(source);
            }
        }

        /// <summary>Checks that crop metadata is applied before conversion.</summary>
        [Test]
        public void Convert_AppliesCropping()
        {
            var source = NativeFrames.Allocate(6, 4, AVPixelFormat.AV_PIX_FMT_RGBA);
            try
            {
                NativeFrames.FillRgba(source);
                source->crop_top = 1;
                source->crop_bottom = 1;
                source->crop_right = 2;
                using var converter = new VideoFrameConverter();
                using var result = converter.Convert(source, 0, 0, 0, 1024);

                Assert.That(result.Width, Is.EqualTo(4));
                Assert.That(result.Height, Is.EqualTo(2));
                Assert.That(NativeFrames.ReadPixel(result.Data, 4, 1, 3), Is.EqualTo(NativeFrames.Pattern(3, 1)));
                Assert.That(NativeFrames.ReadPixel(result.Data, 4, 0, 0), Is.EqualTo(NativeFrames.Pattern(0, 2)));
            }
            finally
            {
                NativeFrames.Free(source);
            }
        }

        /// <summary>Checks that frames exceeding the pixel limit are rejected before allocation.</summary>
        [Test]
        public void Convert_RejectsFramesOverPixelLimit()
        {
            var source = NativeFrames.Allocate(8, 8, AVPixelFormat.AV_PIX_FMT_RGBA);
            try
            {
                using var converter = new VideoFrameConverter();

                Assert.Throws<NotSupportedException>(() => converter.Convert(source, 0, 0, 0, 63));
            }
            finally
            {
                NativeFrames.Free(source);
            }
        }

        /// <summary>Checks that opaque MediaCodec surfaces are rejected because they cannot be downloaded.</summary>
        [Test]
        public void Convert_RejectsOpaqueMediaCodecFrames()
        {
            var source = ffmpeg.av_frame_alloc();
            try
            {
                source->width = 4;
                source->height = 4;
                source->format = (int)AVPixelFormat.AV_PIX_FMT_MEDIACODEC;
                using var converter = new VideoFrameConverter();

                var error = Assert.Throws<NotSupportedException>(() => converter.Convert(source, 0, 0, 0, 1024));

                Assert.That(error!.Message, Does.Contain("MediaCodec"));
            }
            finally
            {
                NativeFrames.Free(source);
            }
        }

        /// <summary>Checks that a D3D11 frame without a staged decoder surface is rejected through the native bridge.</summary>
        [Test]
        public void Convert_RejectsForeignD3D11Frames()
        {
            var source = ffmpeg.av_frame_alloc();
            try
            {
                source->width = 4;
                source->height = 4;
                source->format = (int)AVPixelFormat.AV_PIX_FMT_D3D11;
                using var converter = new VideoFrameConverter();

                try
                {
                    var error = Assert.Throws<NotSupportedException>(() => converter.Convert(source, 0, 0, 0, 1024));
                    Assert.That(error!.Message, Does.Contain("staged D3D11"));
                }
                catch (DllNotFoundException error)
                {
                    Assert.Ignore("The FFmpeg graphics bridge is unavailable: " + error.Message);
                }
            }
            finally
            {
                NativeFrames.Free(source);
            }
        }

        /// <summary>Checks that a pixel format that libswscale cannot read is reported as a conversion failure.</summary>
        [Test]
        public void Convert_RejectsUnconvertibleFormat()
        {
            var source = ffmpeg.av_frame_alloc();
            try
            {
                source->width = 4;
                source->height = 4;
                source->format = (int)AVPixelFormat.AV_PIX_FMT_VAAPI;
                using var converter = new VideoFrameConverter();

                var error = Assert.Throws<InvalidOperationException>(() => converter.Convert(source, 0, 0, 0, 1024));

                Assert.That(error!.Message, Does.Contain("RGBA32"));
                Assert.That(converter.ScaleThreadCount, Is.EqualTo(0));
            }
            finally
            {
                NativeFrames.Free(source);
            }
        }

        /// <summary>Checks that banded multi-threaded copies produce the same bytes as a single-threaded copy for every rotation.</summary>
        /// <param name="turns">The clockwise quarter turns.</param>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void Convert_MultiThreaded_MatchesSingleThread(int turns)
        {
            var width = (turns & 1) == 0 ? 72 : 256;
            var height = (turns & 1) == 0 ? 256 : 72;
            var source = NativeFrames.Allocate(width, height, AVPixelFormat.AV_PIX_FMT_RGBA);
            try
            {
                NativeFrames.FillRgba(source);
                using var single = new VideoFrameConverter();
                using var threaded = new VideoFrameConverter(null, 4);
                using var expected = single.Convert(source, 0, 0, turns * 90, 1 << 20);
                using var actual = threaded.Convert(source, 0, 0, turns * 90, 1 << 20);

                Assert.That(threaded.CopyBandCount, Is.EqualTo(4));
                Assert.That(threaded.StartedCopyThreadCount, Is.EqualTo(3));
                Assert.That(single.StartedCopyThreadCount, Is.EqualTo(0));
                Assert.That(actual.DataSize, Is.EqualTo(expected.DataSize));
                var expectedPixels = (uint*)expected.Data;
                var actualPixels = (uint*)actual.Data;
                for (var i = 0; i < width * height; i++)
                {
                    if (expectedPixels[i] != actualPixels[i])
                    {
                        Assert.Fail("Pixel " + i + " differs: " + expectedPixels[i] + " != " + actualPixels[i]);
                    }
                }
            }
            finally
            {
                NativeFrames.Free(source);
            }
        }

        /// <summary>Checks that small frames stay on one copy band even with several conversion threads.</summary>
        [Test]
        public void Convert_SmallFrame_UsesOneBand()
        {
            var source = NativeFrames.Allocate(16, 16, AVPixelFormat.AV_PIX_FMT_RGBA);
            try
            {
                NativeFrames.FillRgba(source);
                using var converter = new VideoFrameConverter(null, 4);
                using var result = converter.Convert(source, 0, 0, 0, 1024);

                Assert.That(converter.CopyBandCount, Is.EqualTo(1));
                Assert.That(converter.StartedCopyThreadCount, Is.EqualTo(0));
                Assert.That(converter.ScaleThreadCount, Is.GreaterThanOrEqualTo(0));
            }
            finally
            {
                NativeFrames.Free(source);
            }
        }

        /// <summary>Checks that size and format changes reconfigure the cached conversion contexts.</summary>
        [Test]
        public void Convert_FormatAndSizeChanges_Reconfigure()
        {
            var rgba = NativeFrames.Allocate(4, 4, AVPixelFormat.AV_PIX_FMT_RGBA);
            var yuv = NativeFrames.Allocate(6, 2, AVPixelFormat.AV_PIX_FMT_YUV420P);
            try
            {
                NativeFrames.FillRgba(rgba);
                NativeFrames.FillYuv420(yuv, 128);
                using var converter = new VideoFrameConverter(null, 2);
                for (var i = 0; i < 2; i++)
                {
                    using (var first = converter.Convert(rgba, 0, 0, 0, 1024))
                    {
                        Assert.That(NativeFrames.ReadPixel(first.Data, 4, 3, 2), Is.EqualTo(NativeFrames.Pattern(2, 0)));
                    }

                    using (var second = converter.Convert(yuv, 0, 0, 0, 1024))
                    {
                        Assert.That(second.Width, Is.EqualTo(6));
                        Assert.That(second.Height, Is.EqualTo(2));
                    }
                }
            }
            finally
            {
                NativeFrames.Free(rgba);
                NativeFrames.Free(yuv);
            }
        }

        /// <summary>Checks that converted output can be rented from a private session pool.</summary>
        [Test]
        public void Convert_WithPool_RentsPooledFrames()
        {
            var source = NativeFrames.Allocate(2, 2, AVPixelFormat.AV_PIX_FMT_RGBA);
            try
            {
                NativeFrames.FillRgba(source);
                var pool = new DecodedVideoFramePool(1);
                using var converter = new VideoFrameConverter(pool);
                var first = converter.Convert(source, 1, 0.5, 0, 1024);

                Assert.Throws<InvalidOperationException>(() => converter.Convert(source, 2, 0.5, 0, 1024));
                first.Dispose();
                using var second = converter.Convert(source, 3, 0.5, 0, 1024);

                Assert.That(second, Is.SameAs(first));
                Assert.That(second.PresentationTime, Is.EqualTo(3));
                Assert.That(second.Data, Is.Not.EqualTo(IntPtr.Zero));
            }
            finally
            {
                NativeFrames.Free(source);
            }
        }

        /// <summary>Checks that disposing a converter twice is harmless.</summary>
        [Test]
        public void Dispose_IsIdempotent()
        {
            var converter = new VideoFrameConverter(null, 2);

            converter.Dispose();

            Assert.DoesNotThrow(() => converter.Dispose());
        }
    }
}
