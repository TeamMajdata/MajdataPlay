#nullable enable
using System;
using System.Threading;
using FFmpeg.AutoGen;

namespace MajdataPlay.FFmpeg.Internal
{
    /// <summary>
    /// Owns a decoded presentation frame stored as CPU pixels or native GPU resources.
    /// </summary>
    /// <remarks>
    /// CPU pixels use tightly packed RGBA32, with the bottom row first and display rotation already applied.
    /// Keep this object alive until all consumers, including the render thread, have finished using its resources.
    /// </remarks>
    public sealed unsafe class DecodedVideoFrame : IDisposable
    {
        /// <summary>Owns an FFmpeg-allocated CPU pixel buffer, or zero when absent.</summary>
        private IntPtr _data;
        /// <summary>Owns an FFmpeg frame reference retained for native presentation.</summary>
        private IntPtr _nativeFrame;
        /// <summary>Owns a platform image retained until all presentation work has completed.</summary>
        private IntPtr _nativeImage;
        /// <summary>Releases the owned platform image; present only when native image ownership is installed.</summary>
        private Action<IntPtr>? _releaseImage;
        /// <summary>Identifies the private session pool to return to, or null for independent public frames.</summary>
        private readonly DecodedVideoFramePool? _pool;
        /// <summary>Atomically guards resource release and pool return for the current lease.</summary>
        private int _disposed;
        /// <summary>Gets the stored frame width, in pixels, before any remaining <see cref="RotationDegrees"/> is applied.</summary>
        public int Width { get; internal set; }
        /// <summary>Gets the stored frame height, in pixels, before any remaining <see cref="RotationDegrees"/> is applied.</summary>
        public int Height { get; internal set; }
        /// <summary>Gets the number of bytes per row for tightly packed RGBA32 CPU pixels.</summary>
        /// <remarks>This value does not describe the layout of a native hardware frame.</remarks>
        /// <exception cref="OverflowException">The frame width cannot be represented as an RGBA byte stride.</exception>
        public int Stride => checked(Width * 4);
        /// <summary>Gets the CPU pixel buffer size, in bytes, or zero when this frame has no CPU pixel buffer.</summary>
        /// <exception cref="OverflowException">The RGBA buffer size exceeds the signed 32-bit range.</exception>
        public int DataSize => _data == IntPtr.Zero ? 0 : checked(Stride * Height);
        /// <summary>Gets the borrowed pointer to the owned CPU pixel buffer, or <see cref="IntPtr.Zero"/> when absent or disposed.</summary>
        public IntPtr Data => _data;
        /// <summary>Gets the borrowed pointer to the owned FFmpeg <c>AVFrame</c>, or <see cref="IntPtr.Zero"/> when absent or disposed.</summary>
        public IntPtr NativeFrame => _nativeFrame;
        /// <summary>Gets the borrowed pointer to an owned platform image, or <see cref="IntPtr.Zero"/> when absent or disposed.</summary>
        /// <remarks>For Android, the image wraps an <c>AImage</c> and its <c>AHardwareBuffer</c>, with no CPU pixels.</remarks>
        public IntPtr NativeImage => _nativeImage;
        /// <summary>Gets the FFmpeg pixel format of the CPU buffer or native frame.</summary>
        public AVPixelFormat PixelFormat { get; internal set; }
        /// <summary>Gets the presentation timestamp, in seconds relative to the media's timeline origin.</summary>
        public double PresentationTime { get; internal set; }
        /// <summary>Gets the frame's presentation duration, in seconds.</summary>
        public double Duration { get; internal set; }
        /// <summary>Gets the estimated video bit rate over up to one second ending at this frame, in bits per second; zero if unavailable.</summary>
        public long CurrentBitRate { get; internal set; }
        /// <summary>Gets the clockwise display rotation, in degrees, still required when presenting a native frame.</summary>
        /// <remarks>CPU frames already have rotation applied and report zero.</remarks>
        public double RotationDegrees { get; internal set; }
        /// <summary>Gets the ratio of one stored pixel's display width to its display height.</summary>
        public double PixelAspectRatio { get; internal set; } = 1;
        /// <summary>Gets whether this frame currently owns a native hardware frame or platform image.</summary>
        public bool IsHardwareFrame => _nativeFrame != IntPtr.Zero || _nativeImage != IntPtr.Zero;
        /// <summary>Gets whether hardware decoded this frame, including frames downloaded for CPU upload.</summary>
        public bool HardwareDecoded { get; internal set; }
        /// <summary>Gets the frame's pixel transfer description, or null until a newly rented frame is populated.</summary>
        public string? TransferMode { get; internal set; }

        /// <summary>Initializes ownership storage for a decoded presentation frame.</summary>
        /// <param name="data">The owned FFmpeg-allocated CPU buffer, or zero when absent.</param>
        /// <param name="nativeFrame">The owned FFmpeg frame reference, or zero when absent.</param>
        internal DecodedVideoFrame(IntPtr data, IntPtr nativeFrame)
        {
            _data = data;
            _nativeFrame = nativeFrame;
        }

        /// <summary>Initializes ownership storage for a decoded presentation frame.</summary>
        /// <param name="nativeImage">The owned platform image reference.</param>
        /// <param name="releaseImage">The callback that releases the owned platform image after presentation completes.</param>
        /// <exception cref="ArgumentNullException">A required reference argument is null.</exception>
        internal DecodedVideoFrame(IntPtr nativeImage, Action<IntPtr> releaseImage)
        {
            _nativeImage = nativeImage;
            _releaseImage = releaseImage ?? throw new ArgumentNullException(nameof(releaseImage));
        }

        /// <summary>Initializes ownership storage for a decoded presentation frame.</summary>
        /// <param name="pool">The private pool that owns this reusable frame container.</param>
        internal DecodedVideoFrame(DecodedVideoFramePool pool)
        {
            _pool = pool;
            _disposed = 1;
        }

        /// <summary>Resets presentation metadata and starts a new exclusive pool lease.</summary>
        internal void Reuse()
        {
            Width = Height = 0;
            PixelFormat = AVPixelFormat.AV_PIX_FMT_NONE;
            PresentationTime = Duration = RotationDegrees = 0;
            CurrentBitRate = 0;
            PixelAspectRatio = 1;
            HardwareDecoded = false;
            TransferMode = null;
            _disposed = 0;
        }

        /// <summary>Transfers ownership of an FFmpeg-allocated CPU pixel buffer to this frame.</summary>
        /// <param name="pixels">The owned FFmpeg-allocated RGBA pixel buffer.</param>
        internal void SetPixels(IntPtr pixels) => _data = pixels;
        /// <summary>Transfers ownership of an FFmpeg frame reference to this frame.</summary>
        /// <param name="frame">The owned FFmpeg AVFrame reference transferred to this frame.</param>
        internal void SetNativeFrame(IntPtr frame) => _nativeFrame = frame;
        /// <summary>Transfers ownership of a native image and its release callback to this frame.</summary>
        /// <param name="image">The owned native image reference to release.</param>
        /// <param name="releaseImage">The callback that releases the owned platform image after presentation completes.</param>
        internal void SetNativeImage(IntPtr image, Action<IntPtr> releaseImage)
        {
            _releaseImage = releaseImage;
            _nativeImage = image;
        }

        /// <summary>
        /// Downloads an owned native hardware frame and converts it to a separate RGBA32 CPU frame.
        /// </summary>
        /// <returns>A new frame owned by the caller, which must be disposed after use.</returns>
        /// <remarks>Call this on the decoder worker. It performs GPU readback and leaves this frame's ownership unchanged.</remarks>
        /// <exception cref="InvalidOperationException">This object has no native FFmpeg frame, or FFmpeg cannot transfer or convert its pixels.</exception>
        /// <exception cref="NotSupportedException">The hardware frame cannot be downloaded or its dimensions exceed the allocation limit.</exception>
        /// <exception cref="OutOfMemoryException">FFmpeg cannot allocate the download or conversion resources.</exception>
        public DecodedVideoFrame CopyToSoftware()
        {
            if (_nativeFrame == IntPtr.Zero)
            {
                throw new InvalidOperationException("This frame does not own a native hardware frame.");
            }

            using (var converter = new VideoFrameConverter())
            {
                var result = converter.Convert((AVFrame*)_nativeFrame, PresentationTime, Duration, RotationDegrees, int.MaxValue / 4);
                result.HardwareDecoded = true;
                result.CurrentBitRate = CurrentBitRate;
                result.TransferMode = "Hardware decode + CPU RGBA upload";
                return result;
            }
        }

        /// <summary>Releases the owned CPU pixels, native frame, and platform image.</summary>
        /// <remarks>Call only after all consumers have finished using the frame. Repeated calls have no effect.</remarks>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            var image = Interlocked.Exchange(ref _nativeImage, IntPtr.Zero);
            var releaseImage = _releaseImage;
            _releaseImage = null;
            try
            {
                if (image != IntPtr.Zero)
                {
                    // A native image is stored only together with its release callback.
                    releaseImage!(image);
                }
            }
            finally
            {
                var pixels = Interlocked.Exchange(ref _data, IntPtr.Zero);
                if (pixels != IntPtr.Zero)
                {
                    ffmpeg.av_free((void*)pixels);
                }

                var frame = (AVFrame*)Interlocked.Exchange(ref _nativeFrame, IntPtr.Zero);
                if (frame != null)
                {
                    ffmpeg.av_frame_free(&frame);
                }

                _pool?.Return(this);
            }
        }
    }

    // Only VideoDecodeSession's private frames may be reused. Public decoder/copy
    // results remain independent objects so a stale Dispose cannot release a new frame.
    // Returning a frame ends its internal lease: no caller may access it afterwards.
    /// <summary>Pools private session frame containers with exclusive leases that end on disposal.</summary>
    internal sealed class DecodedVideoFramePool
    {
        /// <summary>Serializes frame rentals and returns across the worker and presentation threads.</summary>
        private readonly object _gate = new object();
        /// <summary>Stores available frame containers; rented slots are null.</summary>
        private readonly DecodedVideoFrame?[] _frames;
        /// <summary>Counts occupied slots available for rental.</summary>
        private int _count;
        /// <summary>Preallocates a fixed number of presentation frame containers.</summary>
        /// <param name="capacity">The number of frame containers or queued frames to retain.</param>
        /// <exception cref="ArgumentOutOfRangeException">The pool capacity is not positive.</exception>
        internal DecodedVideoFramePool(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _frames = new DecodedVideoFrame?[capacity];
            for (var i = 0; i < capacity; i++)
            {
                _frames[i] = new DecodedVideoFrame(this);
            }

            _count = capacity;
        }

        /// <summary>Starts an exclusive lease on an available frame container.</summary>
        /// <returns>An exclusively leased frame that must be disposed to return it to this pool.</returns>
        /// <exception cref="InvalidOperationException">All preallocated frame containers are currently leased.</exception>
        internal DecodedVideoFrame Rent()
        {
            lock (_gate)
            {
                if (_count == 0)
                {
                    throw new InvalidOperationException("The presentation frame pool is exhausted.");
                }

                // Occupied slots below _count always contain a returned frame.
                var frame = _frames[--_count]!;
                _frames[_count] = null;
                frame.Reuse();
                return frame;
            }
        }

        /// <summary>Returns a released frame container to the pool under the synchronization lock.</summary>
        /// <param name="frame">The released frame container whose exclusive lease has ended.</param>
        internal void Return(DecodedVideoFrame frame)
        {
            lock (_gate)
            {
                _frames[_count++] = frame;
            }
        }
    }
}
