using System;
using System.Threading;
using FFmpeg.AutoGen;

namespace MajdataPlay.Video.Internal
{
    /// <summary>
    /// An independently owned presentation frame. Software pixels are tightly packed RGBA32,
    /// bottom row first, with the display rotation already applied. NativeFrame is an owned
    /// AVFrame reference; keep this object alive until the render thread finishes reading it.
    /// </summary>
    public sealed unsafe class DecodedVideoFrame : IDisposable
    {
        private IntPtr _data;
        private IntPtr _nativeFrame;
        private IntPtr _nativeImage;
        private readonly Action<IntPtr> _releaseImage;

        public int Width { get; internal set; }
        public int Height { get; internal set; }
        public int Stride => checked(Width * 4);
        public int DataSize => _data == IntPtr.Zero ? 0 : checked(Stride * Height);
        public IntPtr Data => _data;
        public IntPtr NativeFrame => _nativeFrame;
        /// <summary>Owned platform image (Android AImage/AHardwareBuffer), with no CPU pixels.</summary>
        public IntPtr NativeImage => _nativeImage;
        public AVPixelFormat PixelFormat { get; internal set; }
        public double PresentationTime { get; internal set; }
        public double Duration { get; internal set; }
        public double RotationDegrees { get; internal set; }
        public double PixelAspectRatio { get; internal set; } = 1;
        public bool IsHardwareFrame => _nativeFrame != IntPtr.Zero || _nativeImage != IntPtr.Zero;
        /// <summary>The decoder used hardware, including frames downloaded for CPU upload.</summary>
        public bool HardwareDecoded { get; internal set; }
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
        /// Worker-thread fallback for a hardware frame the graphics bridge cannot import.
        /// This GPU readback is deliberately explicit; it is not a zero-copy path.
        /// Returns a separate owned software frame, leaving this frame unchanged.
        /// </summary>
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
