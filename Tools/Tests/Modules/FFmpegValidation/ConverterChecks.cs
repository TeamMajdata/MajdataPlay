using System;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using MajdataPlay.FFmpeg.Internal;

// Real libswscale output tests. No Unity graphics device is required.
static unsafe class ConverterChecks
{
    public static int Run()
    {
        int checks = 0;
        using (var converter = new VideoFrameConverter())
        {
            var frame = Allocate(3, 2, AVPixelFormat.AV_PIX_FMT_RGBA);
            try
            {
                uint[] colors = { 0xff0000ff, 0xff00ff00, 0xffff0000, 0xff00ffff, 0xffffff00, 0xffff00ff };
                for (int y = 0; y < 2; y++)
                    for (int x = 0; x < 3; x++)
                        ((uint*)(frame->data[0] + y * frame->linesize[0]))[x] = colors[y * 3 + x];
                int[][] expected = {
                    new[] {3, 4, 5, 0, 1, 2},
                    new[] {5, 2, 4, 1, 3, 0},
                    new[] {2, 1, 0, 5, 4, 3},
                    new[] {0, 3, 1, 4, 2, 5}
                };
                for (int turn = 0; turn < 4; turn++)
                    using (var result = converter.Convert(frame, 1.25, 0.04, turn * 90, 1024))
                    {
                        Assert(result.Width == (turn % 2 == 0 ? 3 : 2) && result.Height == (turn % 2 == 0 ? 2 : 3), "Rotated dimensions");
                        Assert(result.PresentationTime == 1.25 && result.Duration == 0.04 && result.RotationDegrees == 0, "Presentation metadata");
                        for (int pixel = 0; pixel < 6; pixel++)
                            Assert(unchecked((uint)Marshal.ReadInt32(result.Data, pixel * 4)) == colors[expected[turn][pixel]], "RGBA orientation at rotation " + turn * 90);
                        checks += 8;
                    }

                frame->width = int.MaxValue;
                bool rejected = false;
                try { using (converter.Convert(frame, 0, 0.04, 0, 1024)) { } }
                catch (NotSupportedException) { rejected = true; }
                finally { frame->width = 3; }
                Assert(rejected, "Dimension limit before output allocation");
                checks++;
            }
            finally { ffmpeg.av_frame_free(&frame); }

            // Changing format and dimensions exercises sws_getCachedContext replacement.
            frame = Allocate(4, 4, AVPixelFormat.AV_PIX_FMT_YUV420P);
            try
            {
                for (int plane = 1; plane < 3; plane++)
                    for (int y = 0; y < 2; y++)
                        for (int x = 0; x < 2; x++) frame->data[(uint)plane][y * frame->linesize[(uint)plane] + x] = 128;
                foreach (var sample in new[] { (16, AVColorRange.AVCOL_RANGE_MPEG, 0), (235, AVColorRange.AVCOL_RANGE_MPEG, 255), (16, AVColorRange.AVCOL_RANGE_JPEG, 16) })
                {
                    for (int y = 0; y < 4; y++)
                        for (int x = 0; x < 4; x++) frame->data[0][y * frame->linesize[0] + x] = (byte)sample.Item1;
                    frame->color_range = sample.Item2;
                    using (var result = converter.Convert(frame, 0, 0.04, 0, 1024))
                    {
                        for (int channel = 0; channel < 3; channel++)
                            Assert(Math.Abs(Marshal.ReadByte(result.Data, channel) - sample.Item3) <= 2, "YUV range conversion");
                        Assert(Marshal.ReadByte(result.Data, 3) == 255, "Opaque YUV alpha");
                        checks += 4;
                    }
                }
                frame->crop_top = 2;
                using (var result = converter.Convert(frame, 0, 0.04, 0, 1024))
                {
                    Assert(result.Width == 4 && result.Height == 2 && result.DataSize == 32, "Residual hardware crop after transfer");
                    checks++;
                }
            }
            finally { ffmpeg.av_frame_free(&frame); }

            // Exercise SIMD vector tails at every small/vector-adjacent row width.
            // Row-varying grayscale detects writes into neighboring tight output rows,
            // while repeated allocation/disposal exposes swscale heap-tail corruption.
            foreach (int width in new[] { 1, 2, 3, 4, 7, 8, 15, 16, 17, 31, 32, 33, 63, 64, 65 })
            foreach (int height in new[] { 1, 2, 3, 5 })
            {
                frame = Allocate(width, height, AVPixelFormat.AV_PIX_FMT_YUV420P);
                try
                {
                    frame->color_range = AVColorRange.AVCOL_RANGE_MPEG;
                    for (int plane = 1; plane < 3; plane++)
                        for (int y = 0; y < (height + 1) / 2; y++)
                            for (int x = 0; x < (width + 1) / 2; x++)
                                frame->data[(uint)plane][y * frame->linesize[(uint)plane] + x] = 128;
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                            frame->data[0][y * frame->linesize[0] + x] = (byte)(16 + (13 * x + 29 * y) % 220);
                    for (int repeat = 0; repeat < 3; repeat++)
                    using (var result = converter.Convert(frame, 0, 0.04, 0, 4096))
                    {
                        Assert(result.DataSize == width * height * 4, "SIMD staging preserves packed upload size");
                        for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                        {
                            double gray = (13 * x + 29 * y) % 220 * 255.0 / 219.0;
                            int offset = ((height - 1 - y) * width + x) * 4;
                            for (int channel = 0; channel < 3; channel++)
                                Assert(Math.Abs(Marshal.ReadByte(result.Data, offset + channel) - gray) <= 3,
                                    "SIMD tail/row corruption at " + width + "x" + height + " (" + x + "," + y + ")");
                            Assert(Marshal.ReadByte(result.Data, offset + 3) == 255, "SIMD alpha tail");
                        }
                        checks++;
                    }
                }
                finally { ffmpeg.av_frame_free(&frame); }
            }
        }
        return checks;
    }

    static AVFrame* Allocate(int width, int height, AVPixelFormat format)
    {
        var frame = ffmpeg.av_frame_alloc();
        if (frame == null) throw new OutOfMemoryException();
        frame->width = width; frame->height = height; frame->format = (int)format;
        int error = ffmpeg.av_frame_get_buffer(frame, 32);
        if (error < 0) { ffmpeg.av_frame_free(&frame); throw new Exception("Allocate test frame: " + error); }
        return frame;
    }
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
