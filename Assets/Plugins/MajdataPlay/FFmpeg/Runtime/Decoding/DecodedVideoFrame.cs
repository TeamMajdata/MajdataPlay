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
        private IntPtr _data;
        private IntPtr _nativeFrame;
        private IntPtr _nativeImage;
        private readonly Action<IntPtr> _releaseImage;

        /// <summary>Gets the stored frame width, in pixels, before any remaining <see cref="RotationDegrees"/> is applied.</summary>
        public int Width { get; internal set; }
        /// <summary>Gets the stored frame height, in pixels, before any remaining <see cref="RotationDegrees"/> is applied.</summary>
        public int Height { get; internal set; }
        /// <summary>Gets the number of bytes per row for tightly packed RGBA32 CPU pixels.</summary>
        /// <remarks>This value does not describe the layout of a native hardware frame.</remarks>
        public int Stride => checked(Width * 4);
        /// <summary>Gets the CPU pixel buffer size, in bytes, or zero when this frame has no CPU pixel buffer.</summary>
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
        /// <summary>Gets the clockwise display rotation, in degrees, still required when presenting a native frame.</summary>
        /// <remarks>CPU frames already have rotation applied and report zero.</remarks>
        public double RotationDegrees { get; internal set; }
        /// <summary>Gets the ratio of one stored pixel's display width to its display height.</summary>
        public double PixelAspectRatio { get; internal set; } = 1;
        /// <summary>Gets whether this frame currently owns a native hardware frame or platform image.</summary>
        public bool IsHardwareFrame => _nativeFrame != IntPtr.Zero || _nativeImage != IntPtr.Zero;
        /// <summary>Gets whether hardware decoded this frame, including frames downloaded for CPU upload.</summary>
        public bool HardwareDecoded { get; internal set; }
        /// <summary>Gets a diagnostic description of the frame's pixel transfer path.</summary>
        public string TransferMode { get; internal set; }

        internal DecodedVideoFrame(IntPtr data, IntPtr nativeFrame)
        {
            _data = data;
            _nativeFrame = nativeFrame;
        }

        internal DecodedVideoFrame(IntPtr nativeImage, Action<IntPtr> releaseImage)
        {
            _nativeImage = nativeImage;
            _releaseImage = releaseImage ?? throw new ArgumentNullException(nameof(releaseImage));
        }

        /// <summary>
        /// Downloads an owned native hardware frame and converts it to a separate RGBA32 CPU frame.
        /// </summary>
        /// <returns>A new frame owned by the caller, which must be disposed after use.</returns>
        /// <remarks>Call this on the decoder worker. It performs GPU readback and leaves this frame's ownership unchanged.</remarks>
        /// <exception cref="InvalidOperationException">This object has no native FFmpeg frame, or FFmpeg cannot transfer or convert its pixels.</exception>
        /// <exception cref="NotSupportedException">The hardware frame cannot be downloaded or its dimensions exceed the allocation limit.</exception>
        public DecodedVideoFrame CopyToSoftware()
        {
            if (_nativeFrame == IntPtr.Zero)
                throw new InvalidOperationException("This frame does not own a native hardware frame.");
            using (var converter = new VideoFrameConverter())
            {
                var result = converter.Convert((AVFrame*)_nativeFrame, PresentationTime, Duration, RotationDegrees, int.MaxValue / 4);
                result.HardwareDecoded = true;
                result.TransferMode = "Hardware decode + CPU RGBA upload";
                return result;
            }
        }

        /// <summary>Releases the owned CPU pixels, native frame, and platform image.</summary>
        /// <remarks>Call only after all consumers have finished using the frame. Repeated calls have no effect.</remarks>
        public void Dispose()
        {
            var image = Interlocked.Exchange(ref _nativeImage, IntPtr.Zero);
            if (image != IntPtr.Zero) _releaseImage(image);
            var pixels = Interlocked.Exchange(ref _data, IntPtr.Zero);
            if (pixels != IntPtr.Zero)
                ffmpeg.av_free((void*)pixels);
            var frame = (AVFrame*)Interlocked.Exchange(ref _nativeFrame, IntPtr.Zero);
            if (frame != null)
                ffmpeg.av_frame_free(&frame);
        }
    }
}
