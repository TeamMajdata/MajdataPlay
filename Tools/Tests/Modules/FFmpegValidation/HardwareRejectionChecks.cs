#nullable enable

using System;
using System.Runtime.InteropServices;
using System.Threading;
using FFmpeg.AutoGen;
using MajdataPlay.FFmpeg.Internal;
using MajdataPlay.FFmpeg.Interop;

namespace MajdataPlay.FFmpeg.Validation
{
    /// <summary>Verifies that software decoding stays multi-threaded when a hardware decoder rejects the stream.</summary>
    /// <remarks>
    /// The fixture must be a seekable video that the D3D11VA decoder cannot accept, such as H.264 High 4:4:4 Predictive,
    /// so FFmpeg offers no hardware pixel format and the decoder falls back to software inside the same session.
    /// </remarks>
    internal static unsafe class HardwareRejectionChecks
    {
        /// <summary>Counts assertions completed by these checks.</summary>
        private static int s_checks;

        /// <summary>Runs software thread checks and the D3D11VA rejection checks against a stream the GPU cannot decode.</summary>
        /// <param name="media">A seekable fixture of at least two seconds whose codec profile has no D3D11VA decoder.</param>
        /// <returns>The number of assertions completed during this invocation.</returns>
        /// <exception cref="InvalidOperationException">The fixture is hardware decodable or an observed threading invariant is invalid.</exception>
        public static int RunNative(string media)
        {
            var before = s_checks;
            TestSoftwareThreads(media);
            TestSynchronizedCodecReopen(media, false);
            TestSynchronizedCodecReopen(media, true);
            TestSingleThreadConfigurationKeepsCodec(media);
            TestCpuUploadStaysMultiThreaded(media);
            TestStrictRejection(media);
            TestFallbackOptionsRejection(media);
            return s_checks - before;
        }

        /// <summary>Checks that a plain software decoder reports and uses the configured codec threads.</summary>
        /// <param name="media">The software-decodable fixture.</param>
        /// <exception cref="InvalidOperationException">The decoder reports unexpected threads or delivers an incomplete stream.</exception>
        private static void TestSoftwareThreads(string media)
        {
            using (var decoder = new FFmpegVideoDecoder(new DecoderOptions { ThreadCount = 4 }))
            {
                decoder.Open(media, CancellationToken.None);
                Check(!decoder.HardwareDecoding && decoder.DecoderThreadCount == 4 && decoder.DecoderThreadingMode == "frame",
                    "A software H.264 decoder uses the configured frame threads.");
                var frames = DecodeToEnd(decoder, out var first, out _);
                Check(frames > 0 && first < 0.05, "The software decoder delivers every frame from the stream start.");
                Check(frames == ExpectedFrames(decoder), "The software decoder delivers the complete frame count.");
            }

            using (var decoder = new FFmpegVideoDecoder(new DecoderOptions { ThreadCount = 1 }))
            {
                decoder.Open(media, CancellationToken.None);
                Check(decoder.DecoderThreadCount == 1 && decoder.DecoderThreadingMode == "none",
                    "A single-thread configuration reports one codec thread without a threading mode.");
                using var frame = decoder.ReadFrame();
                Check(frame != null && frame.Data != IntPtr.Zero, "A single-thread software decoder still produces CPU frames.");
            }
        }

        /// <summary>Checks that a GPU-synchronized single-threaded codec is replaced by a multi-threaded software decoder when the GPU rejects the stream.</summary>
        /// <param name="media">The fixture rejected by the D3D11VA decoder.</param>
        /// <param name="mapper">True also installs the production D3D11 frame mapper, matching the Windows native-texture configuration.</param>
        /// <exception cref="InvalidOperationException">The bridge cannot force single-threaded decoding, the fixture is hardware decodable, frames are lost, or resources leak.</exception>
        /// <exception cref="NotSupportedException">The bridge cannot create the requested GPU synchronization or frame mapper.</exception>
        private static void TestSynchronizedCodecReopen(string media, bool mapper)
        {
            using var audit = new RejectionAudit();
            using var decoder = new FFmpegVideoDecoder(audit.CreateOptions(4, false, mapper));
            decoder.Open(media, CancellationToken.None);
            if (mapper && audit.MapperError != null)
            {
                // The decoder keeps hardware decoding with CPU upload when staging is unavailable,
                // so the single-thread precondition cannot be established on this machine.
                Console.WriteLine("SKIP: D3D11 frame mapper variant; independent GPU staging unavailable in this process: " + audit.MapperError.Message);
                return;
            }

            Check(audit.Synchronization != null && (!mapper || audit.Mapper != null),
                "The native D3D11 completion query and frame mapper must load from FFmpegUnityBridge; without them this check cannot force a single codec thread.");
            Check(decoder.HardwareDecoding && decoder.DecoderThreadCount == 1 && decoder.DecoderThreadingMode == "none"
                && decoder.TransferMode == "Native GPU frames",
                "Precondition: GPU-synchronized native decoding forces one codec thread before the first packet.");
            var expectedFrames = ExpectedFrames(decoder);
            double first;
            using (var frame = decoder.ReadFrame())
            {
                Check(frame != null && !frame.HardwareDecoded && frame.Data != IntPtr.Zero && decoder.HardwareFallbackReason != null
                    && decoder.HardwareFallbackReason.Contains("rejected the requested hardware pixel format", StringComparison.Ordinal),
                    "Precondition: the fixture must be rejected by the D3D11VA decoder; use H.264 High 4:4:4 Predictive as documented.");
                first = frame!.PresentationTime;
                AssertVisiblePixels(frame);
            }

            var frames = 1 + DecodeToEnd(decoder, out _, out _);
            Check(!decoder.HardwareDecoding && decoder.DecoderThreadCount == 4 && decoder.DecoderThreadingMode == "frame",
                "A rejected hardware pixel format reopens a software decoder with the configured frame threads.");
            Check(decoder.DecoderDevice == "Software" && decoder.TransferMode == "Software RGBA upload"
                && decoder.ActiveHardwareDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_NONE,
                "The replacement decoder reports software identity and CPU transport.");
            Check(decoder.HardwareFallbackReason != null && decoder.HardwareFallbackReason.Contains("Reopened", StringComparison.Ordinal),
                "The fallback reason records the software reopen.");
            Check(first < 0.05, "The rejected keyframe is re-sent, so decoding starts at the stream start without skipping a GOP.");
            Check(frames == expectedFrames, "Reopening loses no frames: decoded " + frames + ", expected " + expectedFrames + ".");
            Check(audit.Synchronization!.DisposeCount == 1 && (!mapper || audit.Mapper!.DisposeCount == 1),
                "The GPU synchronizer and frame mapper are disposed together with the replaced codec.");
            decoder.Seek(1);
            using (var frame = decoder.ReadFrame())
            {
                Check(frame != null && frame.PresentationTime + frame.Duration >= 0.95 && frame.PresentationTime < 1.2 && frame.Data != IntPtr.Zero,
                    "The software decoder remains seekable after the reopen.");
            }

            decoder.Seek(0);
            using (var frame = decoder.ReadFrame())
            {
                Check(frame != null && frame.PresentationTime < 0.05, "Backward seek after the reopen restarts at the stream start.");
            }

            decoder.Dispose();
            Check(audit.DeviceReferenceCount == 1, "Reopening releases every hardware device reference held by the codec and synchronizer.");
            Console.WriteLine("Hardware rejection reopen (mapper=" + mapper + "): " + decoder.DecoderName + " x" + decoder.DecoderThreadCount
                + " (" + decoder.DecoderThreadingMode + "); " + frames + " frames; " + decoder.HardwareFallbackReason);
        }

        /// <summary>Checks that a single-thread configuration keeps the existing in-place software fallback instead of reopening.</summary>
        /// <param name="media">The fixture rejected by the D3D11VA decoder.</param>
        /// <exception cref="InvalidOperationException">The codec was reopened, produced no software frame, or leaked GPU resources.</exception>
        /// <exception cref="NotSupportedException">The bridge cannot create the GPU synchronization.</exception>
        private static void TestSingleThreadConfigurationKeepsCodec(string media)
        {
            using var audit = new RejectionAudit();
            using var decoder = new FFmpegVideoDecoder(audit.CreateOptions(1, false));
            decoder.Open(media, CancellationToken.None);
            Check(audit.Synchronization != null && decoder.DecoderThreadCount == 1, "Precondition: the synchronized codec opened with one thread.");
            using (var frame = decoder.ReadFrame())
            {
                Check(frame != null && frame.Data != IntPtr.Zero && !frame.HardwareDecoded && !frame.IsHardwareFrame,
                    "A single-thread configuration decodes software frames on the existing codec.");
            }

            Check(!decoder.HardwareDecoding && decoder.DecoderThreadCount == 1
                && decoder.HardwareFallbackReason != null && !decoder.HardwareFallbackReason.Contains("Reopened", StringComparison.Ordinal),
                "A single-thread configuration gains nothing from a reopen and keeps the rejected hardware codec context.");
            Check(audit.Synchronization!.DisposeCount == 0, "The synchronizer stays alive while the original codec context is in use.");
            decoder.Dispose();
            Check(audit.Synchronization.DisposeCount == 1 && audit.DeviceReferenceCount == 1,
                "Disposing the decoder releases the synchronizer and the device reference.");
        }

        /// <summary>Checks that hardware decoding with CPU upload keeps its multi-threaded codec when the GPU rejects the stream.</summary>
        /// <param name="media">The fixture rejected by the D3D11VA decoder.</param>
        /// <exception cref="InvalidOperationException">The codec lost its threads, was reopened, or produced no software frame.</exception>
        private static void TestCpuUploadStaysMultiThreaded(string media)
        {
            using var decoder = new FFmpegVideoDecoder(new DecoderOptions
            {
                HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
                KeepNativeFrames = false,
                AllowHardwareCpuUpload = true,
                ThreadCount = 4
            });
            decoder.Open(media, CancellationToken.None);
            Check(decoder.HardwareDecoding && decoder.DecoderThreadCount == 4 && decoder.DecoderThreadingMode == "frame",
                "CPU upload never forces a single codec thread.");
            using (var frame = decoder.ReadFrame())
            {
                Check(frame != null && frame.Data != IntPtr.Zero && !frame.HardwareDecoded, "The rejected stream decodes in software with CPU pixels.");
            }

            Check(!decoder.HardwareDecoding && decoder.DecoderThreadCount == 4 && decoder.DecoderThreadingMode == "frame"
                && decoder.HardwareFallbackReason != null && !decoder.HardwareFallbackReason.Contains("Reopened", StringComparison.Ordinal),
                "The in-place software fallback keeps its frame threads without reopening the codec.");
        }

        /// <summary>Checks that strict native-only decoding fails with the rejection reason instead of decoding in software.</summary>
        /// <param name="media">The fixture rejected by the D3D11VA decoder.</param>
        /// <exception cref="InvalidOperationException">Strict decoding produced a frame, reopened in software, or leaked GPU resources.</exception>
        /// <exception cref="NotSupportedException">The bridge cannot create the GPU synchronization.</exception>
        private static void TestStrictRejection(string media)
        {
            using var audit = new RejectionAudit();
            using var decoder = new FFmpegVideoDecoder(audit.CreateOptions(4, true));
            decoder.Open(media, CancellationToken.None);
            Check(audit.Synchronization != null && decoder.DecoderThreadCount == 1, "Precondition: the strict synchronized codec opened with one thread.");
            var rejected = false;
            try
            {
                using var ignored = decoder.ReadFrame();
            }
            catch (NotSupportedException error)
            {
                rejected = error.Message.Contains("rejected the requested hardware pixel format", StringComparison.Ordinal);
            }

            Check(rejected, "Strict native decoding reports the hardware format rejection instead of decoding in software.");
            Check(decoder.HardwareFallbackReason != null && !decoder.HardwareFallbackReason.Contains("Reopened", StringComparison.Ordinal),
                "Strict native decoding never reopens a software decoder.");
            decoder.Dispose();
            Check(audit.Synchronization!.DisposeCount == 1 && audit.DeviceReferenceCount == 1,
                "A strict rejection releases the synchronizer and device reference on disposal.");
        }

        /// <summary>Checks that a configured fallback backend defers the rejection to the owner instead of reopening in software.</summary>
        /// <param name="media">The fixture rejected by the D3D11VA decoder.</param>
        /// <exception cref="InvalidOperationException">The decoder produced a frame or reopened in software despite the configured fallback.</exception>
        /// <exception cref="NotSupportedException">The bridge cannot create the GPU synchronization.</exception>
        private static void TestFallbackOptionsRejection(string media)
        {
            using var audit = new RejectionAudit();
            var options = audit.CreateOptions(4, false);
            options.FallbackHardwareOptions = new DecoderOptions
            {
                HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
                KeepNativeFrames = false,
                AllowHardwareCpuUpload = true,
                ThreadCount = 4
            };
            using var decoder = new FFmpegVideoDecoder(options);
            decoder.Open(media, CancellationToken.None);
            Check(audit.Synchronization != null && decoder.DecoderThreadCount == 1, "Precondition: the synchronized codec with a fallback opened with one thread.");
            var rejected = false;
            try
            {
                using var ignored = decoder.ReadFrame();
            }
            catch (NotSupportedException error)
            {
                rejected = error.Message.Contains("rejected the requested hardware pixel format", StringComparison.Ordinal);
            }

            Check(rejected && decoder.HardwareFallbackReason != null && !decoder.HardwareFallbackReason.Contains("Reopened", StringComparison.Ordinal),
                "A configured fallback backend lets the owner select the next transport instead of reopening in software.");
        }

        /// <summary>Decodes every remaining frame, checking ordering, CPU pixels, and visible content.</summary>
        /// <param name="decoder">The open decoder to drain.</param>
        /// <param name="firstTimestamp">Receives the first presentation timestamp in seconds, or NaN when no frame was decoded.</param>
        /// <param name="lastTimestamp">Receives the last presentation timestamp in seconds, or NaN when no frame was decoded.</param>
        /// <returns>The number of frames decoded before the end of the input.</returns>
        /// <exception cref="InvalidOperationException">A frame lacks CPU pixels, breaks timestamp order, or shows no visible content.</exception>
        private static int DecodeToEnd(FFmpegVideoDecoder decoder, out double firstTimestamp, out double lastTimestamp)
        {
            var frames = 0;
            firstTimestamp = double.NaN;
            lastTimestamp = double.NaN;
            var previous = double.NegativeInfinity;
            while (true)
            {
                using var frame = decoder.ReadFrame();
                if (frame == null)
                {
                    return frames;
                }

                Check(frame.Data != IntPtr.Zero && frame.DataSize == frame.Width * frame.Height * 4 && !frame.IsHardwareFrame,
                    "Software frames carry packed CPU RGBA pixels.");
                Check(frame.PresentationTime >= previous, "Software frames keep monotonic presentation timestamps.");
                if (frames == 0)
                {
                    firstTimestamp = frame.PresentationTime;
                    AssertVisiblePixels(frame);
                }

                previous = frame.PresentationTime;
                lastTimestamp = frame.PresentationTime;
                frames++;
            }
        }

        /// <summary>Computes the fixture's frame count from its duration and frame rate.</summary>
        /// <param name="decoder">The open decoder describing the fixture.</param>
        /// <returns>The expected number of decodable frames.</returns>
        private static int ExpectedFrames(FFmpegVideoDecoder decoder)
        {
            return (int)Math.Round(decoder.Duration * decoder.FrameRate);
        }

        /// <summary>Checks that a CPU frame contains non-uniform pixels, which excludes blank or corrupted output.</summary>
        /// <param name="frame">The CPU frame to inspect.</param>
        /// <exception cref="InvalidOperationException">The frame is uniform.</exception>
        private static void AssertVisiblePixels(DecodedVideoFrame frame)
        {
            var pixels = new byte[frame.DataSize];
            Marshal.Copy(frame.Data, pixels, 0, pixels.Length);
            int low = 765, high = 0;
            for (var offset = 0; offset < pixels.Length; offset += 4)
            {
                var brightness = pixels[offset] + pixels[offset + 1] + pixels[offset + 2];
                low = Math.Min(low, brightness);
                high = Math.Max(high, brightness);
            }

            Check(high - low > 15, "Decoded software pixels contain visible content.");
        }

        /// <summary>Counts an assertion and reports a failed invariant.</summary>
        /// <param name="condition">The invariant that must hold.</param>
        /// <param name="message">The failure description.</param>
        /// <exception cref="InvalidOperationException">The invariant does not hold.</exception>
        private static void Check(bool condition, string message)
        {
            s_checks++;
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        /// <summary>Supplies a real D3D11VA device and production GPU synchronization while tracking their lifetimes.</summary>
        private sealed class RejectionAudit : IDisposable
        {
            /// <summary>Owns one independent device reference used only to check release after decoder cleanup.</summary>
            private IntPtr _device;
            /// <summary>Gets the wrapper around the real native synchronizer created on the decoding worker, or null before it exists.</summary>
            internal TrackedSynchronization? Synchronization { get; private set; }
            /// <summary>Gets the wrapper around the real native frame mapper created on the decoding worker, or null when not requested or not yet created.</summary>
            internal TrackedFrameMapper? Mapper { get; private set; }
            /// <summary>Gets the error raised while creating the native frame mapper, or null when creation succeeded or was not attempted.</summary>
            internal Exception? MapperError { get; private set; }
            /// <summary>Gets the remaining FFmpeg device references while the audit reference keeps the device alive.</summary>
            internal int DeviceReferenceCount => _device == IntPtr.Zero ? 0 : ffmpeg.av_buffer_get_ref_count((AVBufferRef*)_device);

            /// <summary>Creates native D3D11VA options that force a single codec thread through production admission synchronization.</summary>
            /// <param name="threadCount">The codec thread count requested for software decoding.</param>
            /// <param name="strict">True requires native hardware frames; false permits software and CPU fallbacks.</param>
            /// <param name="mapper">True also installs the production D3D11 frame mapper used by the Windows native-texture path.</param>
            /// <returns>The options to pass to one decoder.</returns>
            internal DecoderOptions CreateOptions(int threadCount, bool strict, bool mapper = false)
            {
                return new DecoderOptions
                {
                    HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
                    KeepNativeFrames = true,
                    RequireHardwareDecoding = strict,
                    AllowHardwareCpuUpload = !strict,
                    ThreadCount = threadCount,
                    AcquireHardwareDevice = AcquireDevice,
                    CreateHardwareSynchronization = CreateSynchronization,
                    CreateHardwareFrameMapper = mapper ? CreateMapper : null
                };
            }

            /// <summary>Wraps the production D3D11 frame mapper so its disposal can be observed.</summary>
            /// <param name="device">The borrowed FFmpeg D3D11VA device supplied to the native bridge.</param>
            /// <returns>A worker-owned mapper with lifecycle counters.</returns>
            /// <exception cref="NotSupportedException">The bridge cannot create the staging mapper.</exception>
            private IHardwareFrameMapper CreateMapper(IntPtr device)
            {
                try
                {
                    Mapper = new TrackedFrameMapper(VulkanVideoInterop.CreateD3D11FrameMapper(device));
                    return Mapper;
                }
                catch (Exception error)
                {
                    MapperError = error;
                    throw;
                }
            }

            /// <summary>Creates a real independent D3D11VA device and retains a separate reference for cleanup assertions.</summary>
            /// <returns>An owned FFmpeg device reference transferred to the decoder.</returns>
            /// <exception cref="InvalidOperationException">A device was already created or FFmpeg cannot create D3D11VA.</exception>
            /// <exception cref="OutOfMemoryException">FFmpeg cannot allocate the audit reference.</exception>
            private IntPtr AcquireDevice()
            {
                if (_device != IntPtr.Zero)
                {
                    throw new InvalidOperationException("The rejection audit expects one hardware device per decoder.");
                }

                AVBufferRef* device = null;
                FFmpegVideoDecoder.Check(ffmpeg.av_hwdevice_ctx_create(&device,
                    AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, null, null, 0), "Create rejection test device");
                var retained = ffmpeg.av_buffer_ref(device);
                if (retained == null)
                {
                    ffmpeg.av_buffer_unref(&device);
                    throw new OutOfMemoryException("Cannot retain the rejection audit device.");
                }

                _device = (IntPtr)retained;
                return (IntPtr)device;
            }

            /// <summary>Wraps the production admission synchronizer so its disposal can be observed.</summary>
            /// <param name="device">The borrowed FFmpeg D3D11VA device supplied to the native bridge.</param>
            /// <returns>A worker-owned synchronizer with lifecycle counters.</returns>
            /// <exception cref="NotSupportedException">The bridge cannot create the real completion query.</exception>
            private IHardwareDecodeSynchronization CreateSynchronization(IntPtr device)
            {
                Synchronization = new TrackedSynchronization(VulkanVideoInterop.CreateD3D11Synchronization(device));
                return Synchronization;
            }

            /// <summary>Releases the audit's final FFmpeg device reference after all ownership assertions finish.</summary>
            public void Dispose()
            {
                var device = (AVBufferRef*)_device;
                _device = IntPtr.Zero;
                ffmpeg.av_buffer_unref(&device);
            }
        }

        /// <summary>Forwards every operation to the production synchronizer while counting disposals.</summary>
        private sealed class TrackedSynchronization : IHardwareDecodeSynchronization
        {
            /// <summary>Owns the production synchronizer and its retained native query/device resources.</summary>
            private readonly IHardwareDecodeSynchronization _inner;
            /// <summary>Counts decoder-owned disposal calls.</summary>
            private int _disposeCount;
            /// <summary>Gets the number of disposal calls forwarded to the native synchronizer.</summary>
            internal int DisposeCount => Volatile.Read(ref _disposeCount);

            /// <summary>Transfers ownership of the production synchronizer into the observing wrapper.</summary>
            /// <param name="inner">The production synchronizer whose native behavior is exercised.</param>
            internal TrackedSynchronization(IHardwareDecodeSynchronization inner)
            {
                _inner = inner;
            }

            /// <summary>Waits using the real completion query.</summary>
            /// <param name="cancellationToken">Cancels the production GPU wait.</param>
            /// <param name="timeoutMilliseconds">Bounds the production GPU wait in milliseconds.</param>
            /// <exception cref="OperationCanceledException">The session was canceled.</exception>
            /// <exception cref="TimeoutException">GPU completion exceeded the configured timeout.</exception>
            /// <exception cref="NotSupportedException">The native completion query failed.</exception>
            public void Wait(CancellationToken cancellationToken, int timeoutMilliseconds)
            {
                _inner.Wait(cancellationToken, timeoutMilliseconds);
            }

            /// <summary>Releases the real native query and records its disposal.</summary>
            public void Dispose()
            {
                _inner.Dispose();
                Interlocked.Increment(ref _disposeCount);
            }
        }

        /// <summary>Forwards every operation to the production D3D11 frame mapper while counting disposals.</summary>
        private sealed class TrackedFrameMapper : IHardwareFrameMapper
        {
            /// <summary>Owns the production mapper and its retained native staging resources.</summary>
            private readonly IHardwareFrameMapper _inner;
            /// <summary>Counts decoder-owned disposal calls.</summary>
            private int _disposeCount;
            /// <summary>Gets the number of disposal calls forwarded to the native mapper.</summary>
            internal int DisposeCount => Volatile.Read(ref _disposeCount);

            /// <summary>Transfers ownership of the production mapper into the observing wrapper.</summary>
            /// <param name="inner">The production mapper whose native behavior is exercised.</param>
            internal TrackedFrameMapper(IHardwareFrameMapper inner)
            {
                _inner = inner;
            }

            /// <summary>Maps a decoded frame using the real staging pool.</summary>
            /// <param name="frame">The borrowed decoded AVFrame.</param>
            /// <param name="cancellationToken">Cancels the production GPU wait.</param>
            /// <param name="timeoutMilliseconds">Bounds the production GPU wait in milliseconds.</param>
            /// <returns>An owned AVFrame reference from the production mapper.</returns>
            /// <exception cref="OperationCanceledException">The session was canceled.</exception>
            /// <exception cref="TimeoutException">GPU completion exceeded the timeout.</exception>
            /// <exception cref="NotSupportedException">The frame cannot be converted or shared.</exception>
            public IntPtr Map(IntPtr frame, CancellationToken cancellationToken, int timeoutMilliseconds)
            {
                return _inner.Map(frame, cancellationToken, timeoutMilliseconds);
            }

            /// <summary>Releases the real staging pool and records its disposal.</summary>
            public void Dispose()
            {
                _inner.Dispose();
                Interlocked.Increment(ref _disposeCount);
            }
        }
    }
}
