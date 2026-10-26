using System;
using FFmpeg.AutoGen;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Reproduces the replaced capture conversion: a row copy into aligned RGBA staging, then libswscale's BT.709 conversion.</summary>
    /// <remarks>Serves as the color reference and benchmark baseline for <c>CaptureFrameConverter</c>.</remarks>
    internal sealed unsafe class LegacySwscaleCapture : IDisposable
    {
        /// <summary>Stores the frame width in pixels.</summary>
        private readonly int _width;
        /// <summary>Stores the frame height in pixels.</summary>
        private readonly int _height;
        /// <summary>Owns the aligned RGBA staging frame.</summary>
        private AVFrame* _rgba;
        /// <summary>Owns the configured libswscale context.</summary>
        private SwsContext* _scale;

        /// <summary>Configures the conversion exactly as the encoder did before the Burst converter replaced it.</summary>
        /// <param name="width">The frame width in pixels.</param>
        /// <param name="height">The frame height in pixels.</param>
        /// <param name="format">The destination YUV format.</param>
        /// <param name="threads">The libswscale worker count.</param>
        /// <exception cref="InvalidOperationException">FFmpeg cannot allocate or configure the conversion.</exception>
        internal LegacySwscaleCapture(int width, int height, AVPixelFormat format, int threads)
        {
            _width = width;
            _height = height;
            _rgba = NativeFrames.Allocate(width, height, AVPixelFormat.AV_PIX_FMT_RGBA);
            try
            {
                _scale = ffmpeg.sws_alloc_context();
                if (_scale == null)
                {
                    throw new InvalidOperationException("Cannot allocate the reference color converter.");
                }

                SetOption("srcw", width);
                SetOption("srch", height);
                SetOption("src_format", (long)AVPixelFormat.AV_PIX_FMT_RGBA);
                SetOption("dstw", width);
                SetOption("dsth", height);
                SetOption("dst_format", (long)format);
                SetOption("sws_flags", (long)SwsFlags.SWS_BILINEAR);
                SetOption("threads", threads);
                Check(ffmpeg.sws_init_context(_scale, null, null), "initialize");
                var coefficients = *(int_array4*)ffmpeg.sws_getCoefficients(ffmpeg.SWS_CS_ITU709);
                Check(ffmpeg.sws_setColorspaceDetails(_scale, in coefficients, 1, in coefficients, 0, 0, 1 << 16, 1 << 16),
                    "configure BT.709");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>Copies the packed frame into staging, flipping it if requested, and converts it into the destination.</summary>
        /// <param name="packedRgba">Exactly width times height times four bytes.</param>
        /// <param name="flipVertically">Whether the first input row is the bottom row.</param>
        /// <param name="destination">The writable destination frame.</param>
        /// <exception cref="InvalidOperationException">FFmpeg fails to convert the frame.</exception>
        internal void Convert(ReadOnlySpan<byte> packedRgba, bool flipVertically, AVFrame* destination)
        {
            var rowBytes = _width * 4;
            fixed (byte* input = packedRgba)
            {
                for (var y = 0; y < _height; y++)
                {
                    var sourceRow = flipVertically ? _height - 1 - y : y;
                    Buffer.MemoryCopy(input + ((long)sourceRow * rowBytes), _rgba->data[0] + ((long)y * _rgba->linesize[0]), rowBytes,
                        rowBytes);
                }
            }

            Check(ffmpeg.sws_scale_frame(_scale, destination, _rgba), "convert");
        }

        /// <summary>Releases the staging frame and the libswscale context.</summary>
        public void Dispose()
        {
            if (_scale != null)
            {
                ffmpeg.sws_freeContext(_scale);
                _scale = null;
            }

            NativeFrames.Free(_rgba);
            _rgba = null;
        }

        /// <summary>Sets one integer option of the libswscale context.</summary>
        /// <param name="name">The option name.</param>
        /// <param name="value">The option value.</param>
        /// <exception cref="InvalidOperationException">FFmpeg rejects the option.</exception>
        private void SetOption(string name, long value)
        {
            Check(ffmpeg.av_opt_set_int(_scale, name, value, 0), "set " + name);
        }

        /// <summary>Throws when an FFmpeg call reports an error.</summary>
        /// <param name="result">The FFmpeg result code.</param>
        /// <param name="operation">The operation that produced the result.</param>
        /// <exception cref="InvalidOperationException"><paramref name="result"/> is negative.</exception>
        private static void Check(int result, string operation)
        {
            if (result < 0)
            {
                throw new InvalidOperationException("The reference color converter cannot " + operation + ": " + result);
            }
        }
    }
}
