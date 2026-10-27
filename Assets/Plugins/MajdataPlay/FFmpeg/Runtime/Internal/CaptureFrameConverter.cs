#nullable enable
using FFmpeg.AutoGen;
using System;

namespace MajdataPlay.FFmpeg.Internal
{
    /// <summary>Converts packed sRGB RGBA8 capture frames to an encoder's limited-range BT.709 YUV420P or NV12 input frame.</summary>
    /// <remarks>
    /// One encoder worker owns each converter and calls <see cref="Convert"/> and <see cref="Dispose"/> sequentially. The
    /// conversion reads the capture buffer directly, including bottom-first rows, so no RGBA staging copy is made. With more
    /// than one thread, row-pair bands run on lazily created background threads through <see cref="VideoBandExecutor"/>;
    /// these threads never call Unity APIs. Steady-state conversions allocate no managed memory. Frames that
    /// <see cref="CaptureGpuConverter"/> already converted are instead copied by <see cref="CopyNv12"/>.
    /// </remarks>
    internal sealed unsafe class CaptureFrameConverter : IDisposable
    {
        /// <summary>Sets the minimum number of output rows that justify one conversion thread.</summary>
        internal const int MinimumRowsPerThread = 32;
        /// <summary>Stores the fixed frame width in pixels.</summary>
        private readonly int _width;
        /// <summary>Stores the fixed frame height in pixels.</summary>
        private readonly int _height;
        /// <summary>Stores the number of output row pairs in each band except possibly the last.</summary>
        private readonly int _bandPairs;
        /// <summary>Stores the number of row-pair bands that one conversion is split into.</summary>
        private readonly int _bandCount;
        /// <summary>Distributes row-pair bands across threads, or null for single-threaded conversion.</summary>
        private readonly VideoBandExecutor? _bandExecutor;
        /// <summary>Describes the current banded conversion without per-frame allocation, or null for single-threaded conversion.</summary>
        private readonly ConversionJob? _job;

        /// <summary>Initializes a converter for fixed even dimensions that uses up to the requested number of threads.</summary>
        /// <param name="width">The even frame width in pixels.</param>
        /// <param name="height">The even frame height in pixels.</param>
        /// <param name="maximumThreads">The maximum number of threads, including the calling worker, used by each conversion.</param>
        /// <remarks>Frames shorter than <see cref="MinimumRowsPerThread"/> rows per thread use fewer threads.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive and even, or <paramref name="maximumThreads"/> is less than one.</exception>
        internal CaptureFrameConverter(int width, int height, int maximumThreads)
        {
            if (width < 2 || (width & 1) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "The width must be positive and even.");
            }

            if (height < 2 || (height & 1) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height), "The height must be positive and even.");
            }

            if (maximumThreads < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumThreads), "At least one conversion thread is required.");
            }

            _width = width;
            _height = height;
            ThreadCount = Math.Max(1, Math.Min(maximumThreads, height / MinimumRowsPerThread));
            var pairs = height / 2;
            _bandPairs = (pairs + ThreadCount - 1) / ThreadCount;
            _bandCount = (pairs + _bandPairs - 1) / _bandPairs;
            if (_bandCount > 1)
            {
                _bandExecutor = new VideoBandExecutor(ThreadCount);
                _job = new ConversionJob();
            }
        }

        /// <summary>Gets the number of threads, including the calling worker, that convert each frame.</summary>
        internal int ThreadCount { get; }
        /// <summary>Gets the number of row-pair bands that each conversion is split into.</summary>
        internal int BandCount => _bandCount;
        /// <summary>Gets the number of background conversion threads that have been started.</summary>
        internal int StartedThreadCount => _bandExecutor?.StartedThreadCount ?? 0;

        /// <summary>Gets whether frames of a pixel format can be produced by this converter.</summary>
        /// <param name="format">The encoder input pixel format.</param>
        /// <returns><see langword="true"/> for YUV420P and NV12; otherwise, <see langword="false"/>.</returns>
        internal static bool Supports(AVPixelFormat format)
        {
            return format == AVPixelFormat.AV_PIX_FMT_YUV420P || format == AVPixelFormat.AV_PIX_FMT_NV12;
        }

        /// <summary>Converts one packed capture frame into the planes of a writable encoder input frame.</summary>
        /// <param name="packedRgba">Exactly width times height times four sRGB bytes; alpha is ignored.</param>
        /// <param name="flipVertically">Whether the first input row is the bottom row of the image.</param>
        /// <param name="destination">The writable YUV420P or NV12 frame with this converter's dimensions.</param>
        /// <exception cref="ArgumentException">The input has the wrong length.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="destination"/> is null.</exception>
        /// <exception cref="NotSupportedException">The destination format is not YUV420P or NV12, or its dimensions differ.</exception>
        /// <exception cref="Exception">A conversion band failed; it is rethrown after every band has completed.</exception>
        internal void Convert(ReadOnlySpan<byte> packedRgba, bool flipVertically, AVFrame* destination)
        {
            if (packedRgba.Length != checked(_width * _height * 4))
            {
                throw new ArgumentException("The input must contain exactly one tightly packed RGBA8 frame.", nameof(packedRgba));
            }

            var planes = GetPlanes(destination, _width, _height);
            var rowBytes = _width * 4L;
            fixed (byte* input = packedRgba)
            {
                // A negative stride walks bottom-first input upward, so the flip costs no extra pass.
                var top = flipVertically ? input + ((_height - 1) * rowBytes) : input;
                var stride = flipVertically ? -rowBytes : rowBytes;
                if (_bandExecutor == null || _job == null)
                {
                    VideoPixelKernels.ConvertRgbaToYuv420(top, stride, _width, in planes, 0, _height / 2);
                    return;
                }

                _job.Configure(top, stride, _width, _height / 2, _bandPairs, planes);
                try
                {
                    _bandExecutor.Run(_job, _bandCount);
                }
                finally
                {
                    _job.Clear();
                }
            }
        }

        /// <summary>Copies one GPU-converted NV12 capture frame into the planes of a writable encoder input frame.</summary>
        /// <param name="nv12">Exactly width times height times 1.5 bytes: the Y plane, then the interleaved Cb, Cr rows, top row first.</param>
        /// <param name="width">The even frame width in pixels.</param>
        /// <param name="height">The even frame height in pixels.</param>
        /// <param name="destination">The writable YUV420P or NV12 frame with these dimensions.</param>
        /// <remarks>The copy is a few row copies, plus a chroma split for planar frames, so it runs on the calling thread.</remarks>
        /// <exception cref="ArgumentException">The input has the wrong length.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="destination"/> is null.</exception>
        /// <exception cref="NotSupportedException">The destination format is not YUV420P or NV12, or its dimensions differ.</exception>
        internal static void CopyNv12(ReadOnlySpan<byte> nv12, int width, int height, AVFrame* destination)
        {
            if (nv12.Length != CapturePixelLayout.Nv12.GetFrameByteCount(width, height))
            {
                throw new ArgumentException("The input must contain exactly one tightly packed NV12 frame.", nameof(nv12));
            }

            var planes = GetPlanes(destination, width, height);
            fixed (byte* input = nv12)
            {
                VideoPixelKernels.CopyNv12ToYuv420(input, width, height, in planes);
            }
        }

        /// <summary>Signals idle conversion threads to exit.</summary>
        public void Dispose()
        {
            _bandExecutor?.Dispose();
        }

        /// <summary>Describes the planes of a writable YUV420P or NV12 frame with the given dimensions.</summary>
        /// <param name="frame">The borrowed destination frame.</param>
        /// <param name="width">The expected frame width in pixels.</param>
        /// <param name="height">The expected frame height in pixels.</param>
        /// <returns>The plane layout passed to the conversion kernel.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
        /// <exception cref="NotSupportedException">The frame format is not YUV420P or NV12, or its dimensions differ.</exception>
        private static Yuv420Planes GetPlanes(AVFrame* frame, int width, int height)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            var format = (AVPixelFormat)frame->format;
            if (!Supports(format) || frame->width != width || frame->height != height)
            {
                throw new NotSupportedException("The encoder input must be a " + width + "x" + height
                    + " YUV420P or NV12 frame, not " + frame->width + "x" + frame->height + " " + format + ".");
            }

            var planes = new Yuv420Planes
            {
                Luma = frame->data[0],
                LumaStride = frame->linesize[0],
                BlueDifference = frame->data[1],
                BlueDifferenceStride = frame->linesize[1]
            };

            if (format == AVPixelFormat.AV_PIX_FMT_NV12)
            {
                // NV12 interleaves Cb and Cr in its second plane.
                planes.RedDifference = frame->data[1] + 1;
                planes.RedDifferenceStride = frame->linesize[1];
                planes.ChromaStep = 2;
            }
            else
            {
                planes.RedDifference = frame->data[2];
                planes.RedDifferenceStride = frame->linesize[2];
                planes.ChromaStep = 1;
            }

            return planes;
        }

        /// <summary>Describes one banded conversion so band threads can run it without per-frame allocation.</summary>
        /// <remarks>The owner configures the job before publishing it to the band executor and clears it afterwards.</remarks>
        private sealed class ConversionJob : IVideoBandJob
        {
            /// <summary>Stores the borrowed source pixels of output row 0.</summary>
            private byte* _source;
            /// <summary>Stores the signed byte distance between the source pixels of consecutive output rows.</summary>
            private long _sourceStride;
            /// <summary>Stores the frame width in pixels.</summary>
            private int _width;
            /// <summary>Stores the total number of output row pairs.</summary>
            private int _pairs;
            /// <summary>Stores the number of output row pairs in each band except possibly the last.</summary>
            private int _bandPairs;
            /// <summary>Stores the borrowed destination planes.</summary>
            private Yuv420Planes _planes;

            /// <summary>Configures the conversion that subsequent band executions perform.</summary>
            /// <param name="source">The borrowed source pixels of output row 0.</param>
            /// <param name="sourceStride">The signed byte distance between the source pixels of consecutive output rows.</param>
            /// <param name="width">The frame width in pixels.</param>
            /// <param name="pairs">The total number of output row pairs.</param>
            /// <param name="bandPairs">The number of output row pairs in each band except possibly the last.</param>
            /// <param name="planes">The borrowed destination planes.</param>
            internal void Configure(byte* source, long sourceStride, int width, int pairs, int bandPairs, Yuv420Planes planes)
            {
                _source = source;
                _sourceStride = sourceStride;
                _width = width;
                _pairs = pairs;
                _bandPairs = bandPairs;
                _planes = planes;
            }

            /// <summary>Forgets the borrowed buffers after the conversion has completed.</summary>
            internal void Clear()
            {
                _source = null;
                _planes = default;
            }

            /// <summary>Converts the output row pairs of one band.</summary>
            /// <param name="band">The zero-based band index.</param>
            public void Execute(int band)
            {
                var firstPair = band * _bandPairs;
                var endPair = Math.Min(_pairs, firstPair + _bandPairs);
                VideoPixelKernels.ConvertRgbaToYuv420(_source, _sourceStride, _width, in _planes, firstPair, endPair);
            }
        }
    }
}
