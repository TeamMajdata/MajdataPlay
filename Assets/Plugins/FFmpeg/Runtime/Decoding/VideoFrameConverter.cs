using System;
using FFmpeg.AutoGen;
using MajdataPlay.Diagnostics;

namespace MajdataPlay.Video.Internal
{
    /// <summary>Owned by one decoder worker; no Unity API calls or pinned managed pixel arrays.</summary>
    internal sealed unsafe class VideoFrameConverter : IDisposable
    {
        private SwsContext* _scale;
        private AVFrame* _download;
        private AVFrame* _rgba;
        private readonly byte*[] _source = new byte*[8];
        private readonly int[] _sourceStride = new int[8];
        private readonly byte*[] _destination = new byte*[4];
        private readonly int[] _destinationStride = new int[4];

        public DecodedVideoFrame Convert(AVFrame* source, double pts, double duration, double rotation, int maximumPixels)
        {
#if (UNITY_EDITOR || DEBUG) && ENABLE_PROFILER
            using var profile = UnityProfiler.Create("FFmpeg.Decoder.ConvertRGBA");
#endif
            var hardware = source->hw_frames_ctx != null;
            if (source->hw_frames_ctx != null)
            {
#if (UNITY_EDITOR || DEBUG) && ENABLE_PROFILER
                using var downloadProfile = UnityProfiler.Create("FFmpeg.Decoder.DownloadHardwareFrame");
#endif
                if (_download == null)
                    _download = ffmpeg.av_frame_alloc();
                if (_download == null)
                    throw new OutOfMemoryException("Cannot allocate hardware download frame.");
                ffmpeg.av_frame_unref(_download);
                FFmpegVideoDecoder.Check(ffmpeg.av_hwframe_transfer_data(_download, source, 0), "Download hardware frame");
                FFmpegVideoDecoder.Check(ffmpeg.av_frame_copy_props(_download, source), "Copy hardware frame properties");
                source = _download;
            }
            else if ((AVPixelFormat)source->format == AVPixelFormat.AV_PIX_FMT_MEDIACODEC)
                throw new NotSupportedException("Opaque MediaCodec surface frames cannot be downloaded; reopen the hardware decoder in byte-buffer mode.");

            // Hardware frames can retain left/top crop metadata that the codec could not
            // apply by adjusting opaque texture pointers. It is safe after GPU download.
            if (source->crop_left != 0 || source->crop_top != 0 || source->crop_right != 0 || source->crop_bottom != 0)
                FFmpegVideoDecoder.Check(ffmpeg.av_frame_apply_cropping(source, 0), "Crop video frame");

            FFmpegVideoDecoder.ValidateDimensions(source->width, source->height, maximumPixels);
            var quarterTurns = ((int)Math.Round(rotation / 90.0) % 4 + 4) % 4;
            var width = source->width;
            var height = source->height;
            var outputWidth = (quarterTurns & 1) == 0 ? width : height;
            var outputHeight = (quarterTurns & 1) == 0 ? height : width;
            var byteCount = checked(width * height * 4);
            var result = (byte*)ffmpeg.av_malloc((nuint)byteCount);
            if (result == null)
                throw new OutOfMemoryException("Cannot allocate RGBA video frame.");

            try
            {
                _scale = ffmpeg.sws_getCachedContext(_scale, width, height, (AVPixelFormat)source->format,
                    width, height, AVPixelFormat.AV_PIX_FMT_RGBA, (int)SwsFlags.SWS_BILINEAR, null, null, null);
                if (_scale == null)
                    throw new InvalidOperationException("FFmpeg cannot convert this pixel format to RGBA32.");

                // Preserve explicitly signalled YUV matrix and full/limited range. Unspecified
                // matrices use the conventional SD/HD default; HDR tone mapping is not applied.
                var matrix = GetColorMatrix(source->colorspace, height);
                var coefficients = *(int_array4*)ffmpeg.sws_getCoefficients(matrix);
                FFmpegVideoDecoder.Check(ffmpeg.sws_setColorspaceDetails(_scale, in coefficients,
                    source->color_range == AVColorRange.AVCOL_RANGE_JPEG ? 1 : 0,
                    in coefficients, 1, 0, 1 << 16, 1 << 16), "Configure video color conversion");
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
                var rows = ffmpeg.sws_scale(_scale, _source, _sourceStride, 0, height, _destination, _destinationStride);
                FFmpegVideoDecoder.Check(rows, "Convert video frame");
                if (rows != height)
                    throw new InvalidOperationException("FFmpeg produced an incomplete RGBA frame.");
                if (quarterTurns == 0)
                {
                    int rowBytes = checked(width * 4);
                    for (int y = 0; y < height; y++)
                        Buffer.MemoryCopy(_rgba->data[0] + y * _rgba->linesize[0],
                            result + (height - 1 - y) * rowBytes, rowBytes, rowBytes);
                }
                else
                    Rotate(_rgba->data[0], _rgba->linesize[0], (uint*)result, width, height, quarterTurns);

                var sar = source->sample_aspect_ratio;
                var aspect = sar.num > 0 && sar.den > 0 ? ffmpeg.av_q2d(sar) : 1.0;
                var output = new DecodedVideoFrame((IntPtr)result, IntPtr.Zero)
                {
                    Width = outputWidth,
                    Height = outputHeight,
                    PixelFormat = AVPixelFormat.AV_PIX_FMT_RGBA,
                    PresentationTime = pts,
                    Duration = duration,
                    RotationDegrees = 0,
                    HardwareDecoded = hardware,
                    TransferMode = hardware ? "Hardware decode + CPU RGBA upload" : "Software RGBA upload",
                    PixelAspectRatio = (quarterTurns & 1) == 0 ? aspect : 1.0 / aspect
                };
                result = null;
                return output;
            }
            finally
            {
                if (result != null)
                    ffmpeg.av_free(result);
            }
        }

        private static int GetColorMatrix(AVColorSpace colorspace, int height)
        {
            switch (colorspace)
            {
                case AVColorSpace.AVCOL_SPC_BT709: return ffmpeg.SWS_CS_ITU709;
                case AVColorSpace.AVCOL_SPC_FCC: return ffmpeg.SWS_CS_FCC;
                case AVColorSpace.AVCOL_SPC_BT470BG:
                case AVColorSpace.AVCOL_SPC_SMPTE170M: return ffmpeg.SWS_CS_ITU601;
                case AVColorSpace.AVCOL_SPC_SMPTE240M: return ffmpeg.SWS_CS_SMPTE240M;
                case AVColorSpace.AVCOL_SPC_BT2020_NCL:
                case AVColorSpace.AVCOL_SPC_BT2020_CL: return ffmpeg.SWS_CS_BT2020;
                default: return height > 576 ? ffmpeg.SWS_CS_ITU709 : ffmpeg.SWS_CS_ITU601;
            }
        }

        private void EnsureRgbaStaging(int width, int height)
        {
            if (_rgba != null && _rgba->data[0] != null && _rgba->width == width && _rgba->height == height) return;
            if (_rgba == null) _rgba = ffmpeg.av_frame_alloc();
            if (_rgba == null) throw new OutOfMemoryException("Cannot allocate RGBA staging frame.");
            ffmpeg.av_frame_unref(_rgba);
            _rgba->width = width;
            _rgba->height = height;
            _rgba->format = (int)AVPixelFormat.AV_PIX_FMT_RGBA;
            FFmpegVideoDecoder.Check(ffmpeg.av_frame_get_buffer(_rgba, 64), "Allocate aligned RGBA staging");
        }

        // Source coordinates are top-down; destination indexing incorporates Unity's Y flip.
        private static void Rotate(byte* source, int sourceStride, uint* destination, int width, int height, int turns)
        {
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                int dx, dy, outputWidth, outputHeight;
                if (turns == 1) { dx = height - 1 - y; dy = x; outputWidth = height; outputHeight = width; }
                else if (turns == 2) { dx = width - 1 - x; dy = height - 1 - y; outputWidth = width; outputHeight = height; }
                else { dx = y; dy = width - 1 - x; outputWidth = height; outputHeight = width; }
                destination[(outputHeight - 1 - dy) * outputWidth + dx] = ((uint*)(source + y * sourceStride))[x];
            }
        }

        public void Dispose()
        {
            if (_scale != null) { ffmpeg.sws_freeContext(_scale); _scale = null; }
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
