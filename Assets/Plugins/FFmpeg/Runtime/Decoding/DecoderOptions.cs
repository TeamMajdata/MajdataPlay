using System;
using FFmpeg.AutoGen;

namespace MajdataPlay.Video.Internal
{
    /// <summary>Snapshot these options before starting the decoder's owning worker.</summary>
    public sealed class DecoderOptions
    {
        public int IOTimeoutMilliseconds { get; set; } = 15000;
        public int MaximumPixelCount { get; set; } = 4096 * 4096;
        public int ThreadCount { get; set; } = Math.Min(Environment.ProcessorCount, 8);
        public bool KeepNativeFrames { get; set; }
        /// <summary>Fail before software conversion when GPU-only playback was requested.</summary>
        public bool RequireHardwareDecoding { get; set; }
        public AVHWDeviceType HardwareDeviceType { get; set; } = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;

        /// <summary>
        /// For D3D11VA, returns an AddRef'ed Unity ID3D11Device. Invoked on the worker.
        /// Ownership transfers to libavutil, including when device initialization fails.
        /// Do not return a device from another Unity graphics backend.
        /// </summary>
        public Func<IntPtr> AcquireD3D11Device { get; set; }

        /// <summary>Returns a newly owned AVBufferRef for a device matched to Unity's Vulkan GPU.</summary>
        public Func<IntPtr> AcquireHardwareDevice { get; set; }
        /// <summary>Worker-only direct GPU mapping; returns an owned AVFrame with no CPU pixels.</summary>
        public Func<IntPtr, IntPtr> MapHardwareFrame { get; set; }

        /// <summary>Creates the worker-owned Android surface decoder and image queue.</summary>
        public Func<int, int, IHardwareDecodeSession> CreateHardwareSession { get; set; }
    }

    /// <summary>Native image transport; no method may map decoded pixels into CPU memory.</summary>
    public interface IHardwareDecodeSession : IDisposable
    {
        /// <summary>Transfers a new AVBufferRef to the caller.</summary>
        IntPtr AcquireDevice();
        /// <summary>Renders an AVFrame to a native image and returns an owned image reference.</summary>
        IntPtr CaptureFrame(IntPtr frame);
        void ReleaseImage(IntPtr image);
        void Flush();
    }
}
