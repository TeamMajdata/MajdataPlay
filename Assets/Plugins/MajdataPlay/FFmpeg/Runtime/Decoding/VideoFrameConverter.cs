#nullable enable
using FFmpeg.AutoGen;
using MajdataPlay.Diagnostics;
using MajdataPlay.FFmpeg.Interop;
using System;

namespace MajdataPlay.FFmpeg.Internal
{
    /// <summary>Owned by one decoder worker; no Unity API calls or pinned managed pixel arrays.</summary>
    /// <remarks>
    /// With more than one conversion thread, libswscale converts slices on its own threads and the vertical flip or
    /// rotation copy runs in row bands on lazily created background threads. Neither kind of thread calls Unity APIs.
    /// The copy itself is the Burst-compiled <see cref="VideoPixelKernels.CopyRgbaLines"/>.
    /// </remarks>
    internal sealed unsafe class VideoFrameConverter : IDisposable
    {
        /// <summary>Sets the minimum number of source rows that justify one additional copy band.</summary>
        private const int MinimumBandRows = 64;
        /// <summary>Aligns copy band boundaries to multiples of this many source rows.</summary>
        private const int BandRowAlignment = 16;
        /// <summary>Owns the cached single-call scaling context used when slice conversion is unavailable.</summary>
        private SwsContext* _scale;
        /// <summary>Owns the explicitly initialized, optionally slice-threaded libswscale context.</summary>
        /// <remarks>Null before the first conversion, while recreation is pending, or after the current configuration was rejected.</remarks>
        private SwsContext* _sliceScale;
        /// <summary>Stores the source width, in pixels, of the current slice configuration.</summary>
        private int _sliceWidth;
        /// <summary>Stores the source height, in pixels, of the current slice configuration.</summary>
        private int _sliceHeight;
        /// <summary>Stores the source pixel format of the current slice configuration.</summary>
        private AVPixelFormat _sliceFormat = AVPixelFormat.AV_PIX_FMT_NONE;
        /// <summary>Stores the libswscale color matrix last applied to the slice context, or -1 when none has been applied.</summary>
        private int _sliceMatrix = -1;
        /// <summary>Stores the source range flag last applied to the slice context, or -1 when none has been applied.</summary>
        private int _sliceRange = -1;
        /// <summary>Indicates that the next conversion must recreate the slice context after a failure.</summary>
        private bool _sliceRecreate;
        /// <summary>Indicates that the current configuration failed after its last success, so another failure rejects it.</summary>
        private bool _sliceRetried;
        /// <summary>Stores the total number of threads, including the decoder worker, used for each conversion.</summary>
        private readonly int _threadCount;
        /// <summary>Distributes copy bands across threads, or null for single-threaded conversion.</summary>
        private readonly VideoBandExecutor? _bandExecutor;
        /// <summary>Describes the current banded copy without per-frame allocation, or null for single-threaded conversion.</summary>
        private readonly RgbaCopyJob? _copyJob;
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
        /// <summary>Initializes worker-local conversion storage with optional frame pooling and conversion threads.</summary>
        /// <param name="framePool">The private session frame pool, or null to allocate independent output containers.</param>
        /// <param name="threadCount">The total number of threads, including the calling worker, used by each conversion.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="threadCount"/> is less than one.</exception>
        internal VideoFrameConverter(DecodedVideoFramePool? framePool = null, int threadCount = 1)
        {
            if (threadCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(threadCount), "At least one conversion thread is required.");
            }

            _framePool = framePool;
            _threadCount = threadCount;
            if (threadCount > 1)
            {
                _bandExecutor = new VideoBandExecutor(threadCount);
                _copyJob = new RgbaCopyJob();
            }
        }

        /// <summary>Gets the libswscale thread count that converted the latest frame, or zero when the single-call fallback converted it.</summary>
        internal int ScaleThreadCount { get; private set; }
        /// <summary>Gets the number of row bands used by the latest vertical flip or rotation copy.</summary>
        internal int CopyBandCount { get; private set; }
        /// <summary>Gets the number of background copy threads that have been started.</summary>
        internal int StartedCopyThreadCount => _bandExecutor?.StartedThreadCount ?? 0;

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
            if (source->hw_frames_ctx == null && (AVPixelFormat)source->format == AVPixelFormat.AV_PIX_FMT_D3D11)
            {
                // Staged RGBA surfaces retain their original decodable NV12 frame.
                // FFmpeg's D3D11 transfer API does not accept the shared RGBA format.
                var original = VulkanVideoInterop.GetD3D11StageSource((IntPtr)source);
                if (original == IntPtr.Zero)
                {
                    throw new NotSupportedException("The staged D3D11 frame has no downloadable decoder surface.");
                }

                source = (AVFrame*)original;
            }

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
            var quarterTurns = (((int)Math.Round(rotation / 90.0) % 4) + 4) % 4;
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
                // Preserve explicitly signalled YUV matrix and full/limited range. Unspecified
                // matrices use the conventional SD/HD default; HDR tone mapping is not applied.
                var matrix = GetColorMatrix(source->colorspace, height);
                var sourceRange = source->color_range == AVColorRange.AVCOL_RANGE_JPEG ? 1 : 0;

                // SIMD converters may write complete vector tails beyond the visible
                // row width. Never expose Unity's tightly packed buffer to swscale:
                // negative/tight strides can corrupt both adjacent rows and the heap.
                // av_frame_get_buffer reserves aligned rows and trailing plane padding.
                EnsureRgbaStaging(width, height);
                using (UnityProfiler.Create("FFmpeg.Decoder.ScaleRGBA"))
                {
                    if (!TryScaleSlices(source, matrix, sourceRange))
                    {
                        ScaleWholeFrame(source, matrix, sourceRange);
                    }
                }

                using (UnityProfiler.Create("FFmpeg.Decoder.CopyRGBA"))
                {
                    CopyToOutput((uint*)result, width, height, quarterTurns);
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

        /// <summary>Converts the source frame into the RGBA staging frame through the stateful slice API.</summary>
        /// <param name="source">The borrowed CPU source frame, whose dimensions have been validated.</param>
        /// <param name="matrix">The libswscale color-matrix identifier for the source frame.</param>
        /// <param name="sourceRange">One for full-range source samples; zero for limited range.</param>
        /// <returns><see langword="true"/> when the staging frame holds the converted pixels; otherwise, <see langword="false"/>.</returns>
        /// <remarks>
        /// A failed conversion releases the context so the next frame recreates it. A configuration that fails again
        /// before succeeding, or that libswscale cannot initialize, uses the single-call fallback until the size or
        /// pixel format changes.
        /// </remarks>
        private bool TryScaleSlices(AVFrame* source, int matrix, int sourceRange)
        {
            var width = source->width;
            var height = source->height;
            var format = (AVPixelFormat)source->format;
            var configured = width == _sliceWidth && height == _sliceHeight && format == _sliceFormat;
            if (!configured)
            {
                _sliceRetried = false;
            }

            if (!configured || _sliceRecreate)
            {
                ReleaseSliceScale();
                _sliceWidth = width;
                _sliceHeight = height;
                _sliceFormat = format;
                _sliceRecreate = false;
                _sliceScale = CreateSliceScale(width, height, format, sourceRange);
            }

            if (_sliceScale == null)
            {
                return false;
            }

            var status = 0;
            if (matrix != _sliceMatrix || sourceRange != _sliceRange)
            {
                var coefficients = *(int_array4*)ffmpeg.sws_getCoefficients(matrix);
                status = ffmpeg.sws_setColorspaceDetails(_sliceScale, in coefficients, sourceRange, in coefficients, 1, 0,
                    1 << 16, 1 << 16);
                if (status >= 0)
                {
                    _sliceMatrix = matrix;
                    _sliceRange = sourceRange;
                }
            }

            if (status >= 0)
            {
                // libswscale references both frames until sws_frame_end; the aligned staging
                // frame remains the only destination it writes.
                status = ffmpeg.sws_frame_start(_sliceScale, _rgba, source);
                if (status >= 0)
                {
                    status = ffmpeg.sws_send_slice(_sliceScale, 0, (uint)height);
                    if (status >= 0)
                    {
                        status = ffmpeg.sws_receive_slice(_sliceScale, 0, (uint)height);
                    }

                    ffmpeg.sws_frame_end(_sliceScale);
                }
            }

            if (status < 0)
            {
                MajDebug.LogWarning("FFmpeg", "[Decoder] libswscale slice conversion of " + width + "x" + height + " "
                    + format + " failed: " + FFmpegVideoDecoder.ErrorText(status) + " (" + status + "); "
                    + (_sliceRetried ? "using single-call conversion until the format changes." : "recreating the context."));
                ReleaseSliceScale();
                _sliceRecreate = !_sliceRetried;
                _sliceRetried = true;
                return false;
            }

            _sliceRetried = false;
            ScaleThreadCount = _sliceScale->threads;
            return true;
        }

        /// <summary>Allocates and initializes a libswscale context for whole-frame RGBA conversion.</summary>
        /// <param name="width">The source and destination width in pixels.</param>
        /// <param name="height">The source and destination height in pixels.</param>
        /// <param name="format">The source pixel format.</param>
        /// <param name="sourceRange">One for full-range source samples; zero for limited range.</param>
        /// <returns>The initialized context, or null when libswscale cannot allocate or initialize it.</returns>
        private SwsContext* CreateSliceScale(int width, int height, AVPixelFormat format, int sourceRange)
        {
            var context = ffmpeg.sws_alloc_context();
            if (context == null)
            {
                return null;
            }

            context->src_w = width;
            context->src_h = height;
            context->src_format = (int)format;
            context->dst_w = width;
            context->dst_h = height;
            context->dst_format = (int)AVPixelFormat.AV_PIX_FMT_RGBA;
            context->flags = (uint)SwsFlags.SWS_BILINEAR;
            context->threads = _threadCount;
            context->src_range = sourceRange;
            context->dst_range = 1;
            var status = ffmpeg.sws_init_context(context, null, null);
            if (status < 0)
            {
                ffmpeg.sws_freeContext(context);
                MajDebug.LogWarning("FFmpeg", "[Decoder] libswscale cannot initialize slice conversion of " + width + "x"
                    + height + " " + format + ": " + FFmpegVideoDecoder.ErrorText(status) + " (" + status
                    + "); using single-call conversion.");
                return null;
            }

            return context;
        }

        /// <summary>Frees the slice context and forgets its applied color details.</summary>
        private void ReleaseSliceScale()
        {
            if (_sliceScale != null)
            {
                ffmpeg.sws_freeContext(_sliceScale);
                _sliceScale = null;
            }

            _sliceMatrix = -1;
            _sliceRange = -1;
        }

        /// <summary>Converts the source frame into the RGBA staging frame with one single-threaded sws_scale call.</summary>
        /// <param name="source">The borrowed CPU source frame, whose dimensions have been validated.</param>
        /// <param name="matrix">The libswscale color-matrix identifier for the source frame.</param>
        /// <param name="sourceRange">One for full-range source samples; zero for limited range.</param>
        /// <exception cref="InvalidOperationException">FFmpeg cannot configure the conversion or convert the complete frame.</exception>
        private void ScaleWholeFrame(AVFrame* source, int matrix, int sourceRange)
        {
            var width = source->width;
            var height = source->height;
            _scale = ffmpeg.sws_getCachedContext(_scale, width, height, (AVPixelFormat)source->format,
                width, height, AVPixelFormat.AV_PIX_FMT_RGBA, (int)SwsFlags.SWS_BILINEAR, null, null, null);
            if (_scale == null)
            {
                throw new InvalidOperationException("FFmpeg cannot convert this pixel format to RGBA32.");
            }

            var coefficients = *(int_array4*)ffmpeg.sws_getCoefficients(matrix);
            FFmpegVideoDecoder.Check(ffmpeg.sws_setColorspaceDetails(_scale, in coefficients, sourceRange,
                in coefficients, 1, 0, 1 << 16, 1 << 16), "Configure video color conversion");
            for (var i = 0; i < 8; i++)
            {
                _source[i] = source->data[(uint)i];
                _sourceStride[i] = source->linesize[(uint)i];
            }

            _destination[0] = _rgba->data[0];
            _destinationStride[0] = _rgba->linesize[0];
            var rows = ffmpeg.sws_scale(_scale, _source, _sourceStride, 0, height, _destination, _destinationStride);
            FFmpegVideoDecoder.Check(rows, "Convert video frame");
            if (rows != height)
            {
                throw new InvalidOperationException("FFmpeg produced an incomplete RGBA frame.");
            }

            ScaleThreadCount = 0;
        }

        /// <summary>Copies the RGBA staging frame into tightly packed output, in output-row bands when threads are available.</summary>
        /// <param name="destination">The writable RGBA output buffer owned by the caller.</param>
        /// <param name="width">The source frame width in pixels.</param>
        /// <param name="height">The source frame height in pixels.</param>
        /// <param name="turns">The clockwise rotation in quarter turns.</param>
        /// <remarks>
        /// Bands partition output rows, so each thread writes whole rows: source rows for 0 and 180 degrees, and source
        /// columns for 90 and 270 degrees. Writing whole rows avoids sharing output cache lines between threads.
        /// </remarks>
        /// <exception cref="Exception">A copy band failed; it is rethrown after every band has completed.</exception>
        private void CopyToOutput(uint* destination, int width, int height, int turns)
        {
            var source = _rgba->data[0];
            var sourceStride = _rgba->linesize[0];
            var outputRows = (turns & 1) == 0 ? height : width;
            var bandCount = Math.Min(_threadCount, outputRows / MinimumBandRows);
            if (bandCount <= 1 || _bandExecutor == null || _copyJob == null)
            {
                CopyBandCount = 1;
                VideoPixelKernels.CopyRgbaLines(source, sourceStride, destination, width, height, turns, 0, outputRows);
                return;
            }

            var bandRows = (((outputRows + bandCount - 1) / bandCount) + BandRowAlignment - 1) / BandRowAlignment * BandRowAlignment;
            bandCount = (outputRows + bandRows - 1) / bandRows;
            CopyBandCount = bandCount;
            _copyJob.Configure(source, sourceStride, destination, width, height, turns, bandRows);
            try
            {
                _bandExecutor.Run(_copyJob, bandCount);
            }
            finally
            {
                _copyJob.Clear();
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

        /// <summary>Describes one banded RGBA copy so band threads can run it without per-frame allocation.</summary>
        /// <remarks>The owner configures the job before publishing it to the band executor and clears it afterwards.</remarks>
        private sealed class RgbaCopyJob : IVideoBandJob
        {
            /// <summary>Stores the borrowed top-down RGBA staging pixels.</summary>
            private byte* _source;
            /// <summary>Stores the source row stride in bytes.</summary>
            private int _sourceStride;
            /// <summary>Stores the tightly packed RGBA output buffer.</summary>
            private uint* _destination;
            /// <summary>Stores the source frame width in pixels.</summary>
            private int _width;
            /// <summary>Stores the source frame height in pixels.</summary>
            private int _height;
            /// <summary>Stores the clockwise rotation in quarter turns.</summary>
            private int _turns;
            /// <summary>Stores the number of output rows in each band except possibly the last.</summary>
            private int _bandRows;

            /// <summary>Configures the copy that subsequent band executions perform.</summary>
            /// <param name="source">The borrowed top-down RGBA staging pixels.</param>
            /// <param name="sourceStride">The source row stride in bytes.</param>
            /// <param name="destination">The writable, tightly packed RGBA output buffer.</param>
            /// <param name="width">The source frame width in pixels.</param>
            /// <param name="height">The source frame height in pixels.</param>
            /// <param name="turns">The clockwise rotation in quarter turns.</param>
            /// <param name="bandRows">The number of output rows in each band, a multiple of 16.</param>
            internal void Configure(byte* source, int sourceStride, uint* destination, int width, int height, int turns,
                int bandRows)
            {
                _source = source;
                _sourceStride = sourceStride;
                _destination = destination;
                _width = width;
                _height = height;
                _turns = turns;
                _bandRows = bandRows;
            }

            /// <summary>Forgets the borrowed buffers after the copy has completed.</summary>
            internal void Clear()
            {
                _source = null;
                _destination = null;
            }

            /// <summary>Copies the source lines that produce the output rows of one band.</summary>
            /// <param name="band">The zero-based band index.</param>
            public void Execute(int band)
            {
                var firstLine = band * _bandRows;
                var endLine = Math.Min((_turns & 1) == 0 ? _height : _width, firstLine + _bandRows);
                VideoPixelKernels.CopyRgbaLines(_source, _sourceStride, _destination, _width, _height, _turns, firstLine, endLine);
            }
        }

        /// <summary>Releases cached conversion contexts, staging frames, and copy threads.</summary>
        public void Dispose()
        {
            if (_scale != null)
            {
                ffmpeg.sws_freeContext(_scale);
                _scale = null;
            }

            ReleaseSliceScale();
            _bandExecutor?.Dispose();

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
