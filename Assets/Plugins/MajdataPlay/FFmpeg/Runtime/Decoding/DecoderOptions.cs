#nullable enable
using System;
using System.Threading;
using FFmpeg.AutoGen;

namespace MajdataPlay.FFmpeg.Internal
{
    /// <summary>Configures video decoding, resource limits, and hardware frame transport.</summary>
    /// <remarks>Set these options before creating the decoder and do not modify them while it is in use.</remarks>
    public sealed class DecoderOptions
    {
        /// <summary>Gets or sets the positive timeout, in milliseconds, for each blocking input operation.</summary>
        public int IOTimeoutMilliseconds { get; set; } = 15000;
        /// <summary>Gets or sets the maximum number of pixels in a decoded frame.</summary>
        /// <remarks>The value must be positive and no greater than <see cref="int.MaxValue"/> divided by four.</remarks>
        public int MaximumPixelCount { get; set; } = 4096 * 4096;
        /// <summary>Gets or sets the requested decoder thread count, which is clamped to the range 1 through 16.</summary>
        public int ThreadCount { get; set; } = Math.Min(Environment.ProcessorCount, 8);
        /// <summary>Gets or sets whether hardware frames should retain native GPU resources for presentation.</summary>
        /// <remarks>When disabled, hardware frames are downloaded or obtained as CPU pixels for RGBA upload.</remarks>
        public bool KeepNativeFrames { get; set; }
        /// <summary>Gets or sets whether hardware decoding may use CPU pixels for texture upload.</summary>
        /// <remarks>This also permits fallback from native frame transport when GPU-only playback is not required.</remarks>
        public bool AllowHardwareCpuUpload { get; set; } = true;
        /// <summary>Gets or sets a diagnostic description of the supplied hardware device.</summary>
        /// <remarks>The description is used for reporting and does not select a device.</remarks>
        public string? HardwareDeviceDescription { get; set; }
        /// <summary>Gets or sets whether decoding must produce native hardware frames without CPU pixel transfer.</summary>
        /// <remarks>When enabled, software decoding and hardware decoding with CPU upload fail instead of being used as fallbacks.</remarks>
        public bool RequireHardwareDecoding { get; set; }
        /// <summary>Gets or sets the requested FFmpeg hardware backend, or <see cref="AVHWDeviceType.AV_HWDEVICE_TYPE_NONE"/> for software decoding.</summary>
        public AVHWDeviceType HardwareDeviceType { get; set; } = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
        /// <summary>Gets or sets a worker callback that acquires a D3D11 decoding device compatible with the presentation adapter.</summary>
        /// <remarks>
        /// The callback returns an <c>ID3D11Device</c> pointer with an added reference, or <see cref="IntPtr.Zero"/> if unavailable.
        /// Ownership of a returned reference transfers to FFmpeg, including when device initialization fails.
        /// The player supplies an independent decoding device on Unity's adapter to isolate its immediate context.
        /// If omitted, hardware decoding may create an independent device when CPU upload is permitted.
        /// </remarks>
        public Func<IntPtr>? AcquireD3D11Device { get; set; }
        /// <summary>Gets or sets a worker callback that acquires a hardware decoding device compatible with Unity's GPU.</summary>
        /// <remarks>The callback transfers ownership of a new FFmpeg <c>AVBufferRef</c> to the decoder, or returns <see cref="IntPtr.Zero"/> if unavailable.</remarks>
        public Func<IntPtr>? AcquireHardwareDevice { get; set; }
        /// <summary>Gets or sets a worker callback that maps a borrowed hardware <c>AVFrame</c> to a native presentation frame.</summary>
        /// <remarks>
        /// The callback must not download pixels or consume the input frame. It transfers ownership of a new
        /// <c>AVFrame</c> to the caller; returning <see cref="IntPtr.Zero"/> indicates that mapping failed.
        /// </remarks>
        public Func<IntPtr, IntPtr>? MapHardwareFrame { get; set; }
        /// <summary>Gets or sets a worker callback that creates a native surface decoding session for the requested width and height, in pixels.</summary>
        /// <remarks>The decoder owns and disposes the returned session. A null result indicates that native surface decoding is unavailable.</remarks>
        public Func<int, int, IHardwareDecodeSession?>? CreateHardwareSession { get; set; }
        // A player-owned alternative for codecs or devices that cannot use the preferred native API.
        /// <summary>Gets or sets the player-owned alternative for codecs or devices that cannot use the preferred native API.</summary>
        internal DecoderOptions? FallbackHardwareOptions { get; set; }
        /// <summary>Creates worker-owned GPU admission control for a borrowed FFmpeg hardware device reference.</summary>
        /// <remarks>The callback must retain any native resources it needs; the decoder disposes the returned synchronizer.</remarks>
        internal Func<IntPtr, IHardwareDecodeSynchronization>? CreateHardwareSynchronization { get; set; }
        /// <summary>Creates worker-owned conversion of decoded surfaces into completed, independently retained GPU presentation frames.</summary>
        /// <remarks>The factory receives a borrowed hardware device reference. Its result is disposed on the decoder worker.</remarks>
        internal Func<IntPtr, IHardwareFrameMapper>? CreateHardwareFrameMapper { get; set; }
        /// <summary>Configures public hardware frame callbacks before a codec-owned frame pool is initialized.</summary>
        /// <remarks>The callback borrows an uninitialized FFmpeg AVBufferRef and returns zero on success or a negative FFmpeg error.</remarks>
        internal Func<IntPtr, int>? ConfigureHardwareFrames { get; set; }
        /// <summary>Waits on the decoding worker until a borrowed native frame is safe to publish.</summary>
        /// <remarks>The callback receives the frame, session cancellation token, and timeout in milliseconds. It must not call Unity APIs.</remarks>
        internal Action<IntPtr, CancellationToken, int>? WaitForHardwareFrame { get; set; }

        /// <summary>Creates a shallow options copy, retaining callback and fallback references.</summary>
        /// <returns>A shallow copy with the same option values and callback references.</returns>
        internal DecoderOptions Copy() => (DecoderOptions)MemberwiseClone();
    }

    /// <summary>Converts hardware frames on their owning worker so presentation does not enter the decoder context.</summary>
    internal interface IHardwareFrameMapper : IDisposable
    {
        /// <summary>Produces a completed GPU frame without downloading pixels or consuming the source.</summary>
        /// <param name="frame">The borrowed decoded AVFrame, retained throughout conversion.</param>
        /// <param name="cancellationToken">Cancels conversion or waits when the decode session closes.</param>
        /// <param name="timeoutMilliseconds">The positive maximum time allowed for GPU completion.</param>
        /// <returns>An owned AVFrame reference that the caller must free.</returns>
        /// <exception cref="OperationCanceledException">The session was canceled.</exception>
        /// <exception cref="TimeoutException">GPU completion exceeded the timeout.</exception>
        /// <exception cref="NotSupportedException">The frame cannot be safely converted or shared.</exception>
        IntPtr Map(IntPtr frame, CancellationToken cancellationToken, int timeoutMilliseconds);
    }

    /// <summary>Bounds hardware decode submissions without making Unity's render thread wait for the GPU.</summary>
    internal interface IHardwareDecodeSynchronization : IDisposable
    {
        /// <summary>Completes preceding codec work before publishing a frame or admitting another bounded packet batch.</summary>
        /// <param name="cancellationToken">Cancels the worker wait when the decode session closes.</param>
        /// <param name="timeoutMilliseconds">The maximum GPU wait time in milliseconds.</param>
        /// <exception cref="OperationCanceledException">The session was canceled.</exception>
        /// <exception cref="TimeoutException">GPU completion exceeded the timeout.</exception>
        /// <exception cref="NotSupportedException">The device cannot provide safe synchronization.</exception>
        void Wait(CancellationToken cancellationToken, int timeoutMilliseconds);
    }

    /// <summary>Owns a hardware decoding session that transports native images without mapping pixels into CPU memory.</summary>
    public interface IHardwareDecodeSession : IDisposable
    {
        /// <summary>Acquires the FFmpeg hardware device used by this session.</summary>
        /// <returns>A newly owned <c>AVBufferRef</c>, or <see cref="IntPtr.Zero"/> if unavailable. The caller must release the reference.</returns>
        IntPtr AcquireDevice();
        /// <summary>Renders a decoded hardware frame to a native image.</summary>
        /// <param name="frame">A borrowed FFmpeg <c>AVFrame</c> whose decoder output is rendered by this operation.</param>
        /// <returns>An owned native image reference to release with <see cref="ReleaseImage"/>, or <see cref="IntPtr.Zero"/> if capture failed.</returns>
        IntPtr CaptureFrame(IntPtr frame);
        /// <summary>Releases a native image previously returned by <see cref="CaptureFrame"/>.</summary>
        /// <param name="image">The owned image reference to release after presentation has finished.</param>
        void ReleaseImage(IntPtr image);
        /// <summary>Discards queued native images when flushing or seeking the decoder.</summary>
        void Flush();
    }
}
