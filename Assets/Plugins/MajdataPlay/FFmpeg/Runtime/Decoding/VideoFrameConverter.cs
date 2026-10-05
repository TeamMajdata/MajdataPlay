#nullable enable
using System;
using FFmpeg.AutoGen;
using MajdataPlay.Diagnostics;

namespace MajdataPlay.FFmpeg.Internal
{
    /// <summary>Owned by one decoder worker; no Unity API calls or pinned managed pixel arrays.</summary>
    internal sealed unsafe class VideoFrameConverter : IDisposable
    {
        /// <summary>Owns the cached FFmpeg scaling and color conversion context.</summary>
        private SwsContext* _scale;
        /// <summary>Owns reusable CPU frame storage for downloaded hardware frames.</summary>
        private AVFrame* _download;
        /// <summary>Owns aligned RGBA staging storage for rotation and vertical flipping.</summary>
        private AVFrame* _rgba;
        /// <summary>Provides reusable session output containers, or null for independent output frames.</summary>
        private readonly DecodedVideoFramePool? _framePool;
        /// <summary>Reuses source plane pointers when invoking libswscale.</summary>
        private readonly byte*[] _source = new byte*[8];
        /// <summary>Reuses source plane strides, in bytes, when invoking libswscale.</summary>
        private readonly int[] _sourceStride = new int[8];
        /// <summary>Reuses destination plane pointers when invoking libswscale.</summary>
        private readonly byte*[] _destination = new byte*[4];
        /// <summary>Reuses destination plane strides, in bytes, when invoking libswscale.</summary>
        private readonly int[] _destinationStride = new int[4];
        /// <summary>Initializes worker-local conversion storage with optional frame pooling.</summary>
        /// <param name="framePool">The private session frame pool, or null to allocate independent output containers.</param>
        internal VideoFrameConverter(DecodedVideoFramePool? framePool = null)
        {
            _framePool = framePool;
        }

        /// <summary>Converts a borrowed decoded frame to an independently owned RGBA32 presentation frame.</summary>
        /// <param name="source">The source frame, which may be downloaded and have its CPU cropping metadata applied.</param>
        /// <param name="pts">The presentation timestamp, in seconds relative to the media's timeline origin.</param>
        /// <param name="duration">The frame's presentation duration, in seconds.</param>
        /// <param name="rotation">The clockwise display rotation, in degrees, rounded to the nearest quarter turn.</param>
        /// <param name="maximumPixels">The maximum permitted number of pixels in the source frame.</param>
        /// <returns>A frame owned by the caller, with rotation and Unity's vertical flip applied to its CPU pixels.</returns>
        /// <exception cref="InvalidOperationException">FFmpeg fails to transfer, crop, configure, or convert the source frame.</exception>
        /// <exception cref="NotSupportedException">Opaque MediaCodec output cannot be downloaded or the frame exceeds the configured allocation limit.</exception>
        /// <exception cref="OutOfMemoryException">FFmpeg cannot allocate the download frame, pixel buffer, or staging frame.</exception>
        public DecodedVideoFrame Convert(AVFrame* source, double pts, double duration, double rotation, int maximumPixels)
        {
            using var profile = UnityProfiler.Create("FFmpeg.Decoder.ConvertRGBA");
            var hardware = source->hw_frames_ctx != null;
            if (source->hw_frames_ctx != null)
            {
                using var downloadProfile = UnityProfiler.Create("FFmpeg.Decoder.DownloadHardwareFrame");
                if (_download == null)
                {
                    _download = ffmpeg.av_frame_alloc();
                }

                if (_download == null)
                {
                    throw new OutOfMemoryException("Cannot allocate hardware download frame.");
                }

                ffmpeg.av_frame_unref(_download);
                FFmpegVideoDecoder.Check(ffmpeg.av_hwframe_transfer_data(_download, source, 0), "Download hardware frame");
                FFmpegVideoDecoder.Check(ffmpeg.av_frame_copy_props(_download, source), "Copy hardware frame properties");
                source = _download;
            }
            else if ((AVPixelFormat)source->format == AVPixelFormat.AV_PIX_FMT_MEDIACODEC)
            {
                throw new NotSupportedException("Opaque MediaCodec surface frames cannot be downloaded; reopen the hardware decoder in byte-buffer mode.");
            }

            // Hardware frames can retain left/top crop metadata that the codec could not
            // apply by adjusting opaque texture pointers. It is safe after GPU download.
            if (source->crop_left != 0 || source->crop_top != 0 || source->crop_right != 0 || source->crop_bottom != 0)
            {
                FFmpegVideoDecoder.Check(ffmpeg.av_frame_apply_cropping(source, 0), "Crop video frame");
            }

            FFmpegVideoDecoder.ValidateDimensions(source->width, source->height, maximumPixels);
            var quarterTurns = ((int)Math.Round(rotation / 90.0) % 4 + 4) % 4;
            var width = source->width;
            var height = source->height;
            var outputWidth = (quarterTurns & 1) == 0 ? width : height;
            var outputHeight = (quarterTurns & 1) == 0 ? height : width;
            var byteCount = checked(width * height * 4);
            var result = (byte*)ffmpeg.av_malloc((nuint)byteCount);
            if (result == null)
            {
                throw new OutOfMemoryException("Cannot allocate RGBA video frame.");
            }

            try
            {
                _scale = ffmpeg.sws_getCachedContext(_scale, width, height, (AVPixelFormat)source->format,
                    width, height, AVPixelFormat.AV_PIX_FMT_RGBA, (int)SwsFlags.SWS_BILINEAR, null, null, null);
                if (_scale == null)
                {
                    throw new InvalidOperationException("FFmpeg cannot convert this pixel format to RGBA32.");
                }

                // Preserve explicitly signalled YUV matrix and full/limited range. Unspecified
                // matrices use the conventional SD/HD default; HDR tone mapping is not applied.
                var matrix = GetColorMatrix(source->colorspace, height);
                var coefficients = *(int_array4*)ffmpeg.sws_getCoefficients(matrix);
                FFmpegVideoDecoder.Check(ffmpeg.sws_setColorspaceDetails(_scale, in coefficients,
                    source->color_range == AVColorRange.AVCOL_RANGE_JPEG ? 1 : 0, in coefficients, 1, 0, 1 << 16,
                    1 << 16), "Configure video color conversion");
                for (var i = 0; i < 8; i++)
                {
                    _source[i] = source->data[(uint)i];
                    _sourceStride[i] = source->linesize[(uint)i];
                }

                // SIMD converters may write complete vector tails beyond the visible
                // row width. Never expose Unity's tightly packed buffer to swscale:
                // negative/tight strides can corrupt both adjacent rows and the heap.
                // av_frame_get_buffer reserves aligned rows and trailing plane padding.
                EnsureRgbaStaging(width, height);
                _destination[0] = _rgba->data[0];
                _destinationStride[0] = _rgba->linesize[0];
                int rows;
                using (UnityProfiler.Create("FFmpeg.Decoder.ScaleRGBA"))
                {
                    rows = ffmpeg.sws_scale(_scale, _source, _sourceStride, 0, height, _destination, _destinationStride);
                }

                FFmpegVideoDecoder.Check(rows, "Convert video frame");
                if (rows != height)
                {
                    throw new InvalidOperationException("FFmpeg produced an incomplete RGBA frame.");
                }

                using (UnityProfiler.Create("FFmpeg.Decoder.CopyRGBA"))
                {
                    if (quarterTurns == 0)
                    {
                        int rowBytes = checked(width * 4);
                        for (int y = 0; y < height; y++)
                        {
                            Buffer.MemoryCopy(_rgba->data[0] + y * _rgba->linesize[0], result + (height - 1 - y) * rowBytes, rowBytes, rowBytes);
                        }
                    }
                    else
                    {
                        Rotate(_rgba->data[0], _rgba->linesize[0], (uint*)result, width, height, quarterTurns);
                    }
                }

                var sar = source->sample_aspect_ratio;
                var aspect = sar.num > 0 && sar.den > 0 ? ffmpeg.av_q2d(sar) : 1.0;
                var output = _framePool?.Rent() ?? new DecodedVideoFrame(IntPtr.Zero, IntPtr.Zero);
                output.SetPixels((IntPtr)result);
                output.Width = outputWidth;
                output.Height = outputHeight;
                output.PixelFormat = AVPixelFormat.AV_PIX_FMT_RGBA;
                output.PresentationTime = pts;
                output.Duration = duration;
                output.RotationDegrees = 0;
                output.HardwareDecoded = hardware;
                output.TransferMode = hardware ? "Hardware decode + CPU RGBA upload" : "Software RGBA upload";
                output.PixelAspectRatio = (quarterTurns & 1) == 0 ? aspect : 1.0 / aspect;
                result = null;
                return output;
            }
            finally
            {
                if (result != null)
                {
                    ffmpeg.av_free(result);
                }
            }
        }

        /// <summary>Selects the libswscale color matrix, using SD or HD defaults for unspecified color spaces.</summary>
        /// <param name="colorspace">The color space signaled by the decoded frame.</param>
        /// <param name="height">The requested frame or texture height in pixels.</param>
        /// <returns>The libswscale color-matrix identifier.</returns>
        private static int GetColorMatrix(AVColorSpace colorspace, int height)
        {
            switch (colorspace)
            {
                case AVColorSpace.AVCOL_SPC_BT709:
                    return ffmpeg.SWS_CS_ITU709;
                case AVColorSpace.AVCOL_SPC_FCC:
                    return ffmpeg.SWS_CS_FCC;
                case AVColorSpace.AVCOL_SPC_BT470BG:
                case AVColorSpace.AVCOL_SPC_SMPTE170M:
                    return ffmpeg.SWS_CS_ITU601;
                case AVColorSpace.AVCOL_SPC_SMPTE240M:
                    return ffmpeg.SWS_CS_SMPTE240M;
                case AVColorSpace.AVCOL_SPC_BT2020_NCL:
                case AVColorSpace.AVCOL_SPC_BT2020_CL:
                    return ffmpeg.SWS_CS_BT2020;
                default:
                    return height > 576 ? ffmpeg.SWS_CS_ITU709 : ffmpeg.SWS_CS_ITU601;
            }
        }

        /// <summary>Allocates or reuses aligned RGBA staging storage for the requested dimensions.</summary>
        /// <param name="width">The requested frame or texture width in pixels.</param>
        /// <param name="height">The requested frame or texture height in pixels.</param>
        /// <exception cref="OutOfMemoryException">FFmpeg cannot allocate an RGBA staging frame.</exception>
        /// <exception cref="InvalidOperationException">FFmpeg cannot allocate aligned pixel storage for the staging frame.</exception>
        private void EnsureRgbaStaging(int width, int height)
        {
            if (_rgba != null && _rgba->data[0] != null && _rgba->width == width && _rgba->height == height)
            {
                return;
            }

            if (_rgba == null)
            {
                _rgba = ffmpeg.av_frame_alloc();
            }

            if (_rgba == null)
            {
                throw new OutOfMemoryException("Cannot allocate RGBA staging frame.");
            }

            ffmpeg.av_frame_unref(_rgba);
            _rgba->width = width;
            _rgba->height = height;
            _rgba->format = (int)AVPixelFormat.AV_PIX_FMT_RGBA;
            FFmpegVideoDecoder.Check(ffmpeg.av_frame_get_buffer(_rgba, 64), "Allocate aligned RGBA staging");
        }

        // Source coordinates are top-down; destination indexing incorporates Unity's Y flip.
        /// <summary>Copies RGBA pixels through a quarter-turn rotation and Unity's vertical coordinate flip.</summary>
        /// <param name="source">The borrowed source pixel buffer or frame to convert.</param>
        /// <param name="sourceStride">The source row stride in bytes.</param>
        /// <param name="destination">The writable RGBA output buffer owned by the caller.</param>
        /// <param name="width">The requested frame or texture width in pixels.</param>
        /// <param name="height">The requested frame or texture height in pixels.</param>
        /// <param name="turns">The clockwise rotation in quarter turns.</param>
        private static void Rotate(byte* source, int sourceStride, uint* destination, int width, int height, int turns)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    int dx, dy, outputWidth, outputHeight;
                    if (turns == 1)
                    {
                        dx = height - 1 - y;
                        dy = x;
                        outputWidth = height;
                        outputHeight = width;
                    }
                    else if (turns == 2)
                    {
                        dx = width - 1 - x;
                        dy = height - 1 - y;
                        outputWidth = width;
                        outputHeight = height;
                    }
                    else
                    {
                        dx = y;
                        dy = width - 1 - x;
                        outputWidth = height;
                        outputHeight = width;
                    }

                    destination[(outputHeight - 1 - dy) * outputWidth + dx] = ((uint*)(source + y * sourceStride))[x];
                }
            }
        }

        /// <summary>Releases cached conversion contexts and staging frames.</summary>
        public void Dispose()
        {
            if (_scale != null)
            {
                ffmpeg.sws_freeContext(_scale);
                _scale = null;
            }

            if (_download != null)
            {
                var frame = _download;
                ffmpeg.av_frame_free(&frame);
                _download = null;
            }

            if (_rgba != null)
            {
                var frame = _rgba;
                ffmpeg.av_frame_free(&frame);
                _rgba = null;
            }
        }
    }
}
