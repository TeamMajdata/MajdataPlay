using System;
using FFmpeg.AutoGen;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Allocates and fills native FFmpeg frames for converter and frame-ownership tests.</summary>
    internal static unsafe class NativeFrames
    {
        /// <summary>Allocates a CPU frame with aligned pixel storage.</summary>
        /// <param name="width">The frame width in pixels.</param>
        /// <param name="height">The frame height in pixels.</param>
        /// <param name="format">The CPU pixel format.</param>
        /// <returns>An owned frame that the caller releases with <see cref="Free"/>.</returns>
        /// <exception cref="InvalidOperationException">FFmpeg cannot allocate the frame storage.</exception>
        internal static AVFrame* Allocate(int width, int height, AVPixelFormat format)
        {
            var frame = ffmpeg.av_frame_alloc();
            if (frame == null)
            {
                throw new OutOfMemoryException("Cannot allocate a test frame.");
            }

            frame->width = width;
            frame->height = height;
            frame->format = (int)format;
            var result = ffmpeg.av_frame_get_buffer(frame, 32);
            if (result < 0)
            {
                ffmpeg.av_frame_free(&frame);
                throw new InvalidOperationException("Cannot allocate test frame pixels: " + result);
            }

            return frame;
        }

        /// <summary>Releases a frame allocated by <see cref="Allocate"/>.</summary>
        /// <param name="frame">The owned frame, which may be null.</param>
        internal static void Free(AVFrame* frame)
        {
            if (frame != null)
            {
                ffmpeg.av_frame_free(&frame);
            }
        }

        /// <summary>Computes the packed RGBA value of a source pixel in the test pattern.</summary>
        /// <param name="x">The source column.</param>
        /// <param name="y">The source row.</param>
        /// <returns>The pixel as a little-endian RGBA word.</returns>
        internal static uint Pattern(int x, int y)
        {
            return (uint)((x & 0xFF) | ((y & 0xFF) << 8) | (((x + y) & 0xFF) << 16) | (0xFF << 24));
        }

        /// <summary>Fills an RGBA frame with <see cref="Pattern"/>.</summary>
        /// <param name="frame">The frame to fill.</param>
        internal static void FillRgba(AVFrame* frame)
        {
            for (var y = 0; y < frame->height; y++)
            {
                var row = (uint*)(frame->data[0] + ((long)y * frame->linesize[0]));
                for (var x = 0; x < frame->width; x++)
                {
                    row[x] = Pattern(x, y);
                }
            }
        }

        /// <summary>Fills a YUV 4:2:0 frame with constant luma and neutral chroma.</summary>
        /// <param name="frame">The frame to fill.</param>
        /// <param name="luma">The luma sample value.</param>
        internal static void FillYuv420(AVFrame* frame, byte luma)
        {
            for (var y = 0; y < frame->height; y++)
            {
                for (var x = 0; x < frame->width; x++)
                {
                    frame->data[0][(y * frame->linesize[0]) + x] = luma;
                }
            }

            for (uint plane = 1; plane < 3; plane++)
            {
                for (var y = 0; y < (frame->height + 1) / 2; y++)
                {
                    for (var x = 0; x < (frame->width + 1) / 2; x++)
                    {
                        frame->data[plane][(y * frame->linesize[plane]) + x] = 128;
                    }
                }
            }
        }

        /// <summary>Reads one packed RGBA pixel of a converted output frame.</summary>
        /// <param name="data">The output pixel buffer.</param>
        /// <param name="width">The output width in pixels.</param>
        /// <param name="row">The output row, counted from the first stored row.</param>
        /// <param name="column">The output column.</param>
        /// <returns>The pixel as a little-endian RGBA word.</returns>
        internal static uint ReadPixel(IntPtr data, int width, int row, int column)
        {
            return ((uint*)data)[((long)row * width) + column];
        }
    }
}
