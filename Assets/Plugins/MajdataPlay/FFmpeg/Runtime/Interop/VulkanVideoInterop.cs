#nullable enable
using System;
using System.Runtime.InteropServices;
using MajdataPlay.Diagnostics;
using MajdataPlay.FFmpeg.Internal;

namespace MajdataPlay.FFmpeg.Interop
{
    /// <summary>Vulkan Video, Linux DMA-BUF, and Android AHardwareBuffer transport without CPU image mapping.</summary>
    internal static class VulkanVideoInterop
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>Serializes one-time FFmpeg Java VM registration across decoder sessions.</summary>
        private static readonly object s_androidInitializationLock = new object ();
        /// <summary>Tracks whether FFmpeg has received the Android Java VM.</summary>
        private static bool s_androidInitialized;
#endif
        /// <summary>Converts a native transport error into an actionable diagnostic with its numeric code.</summary>
        /// <param name="error">The native status code to inspect or describe.</param>
        /// <returns>A diagnostic description containing the native status code.</returns>
        internal static string DescribeError(int error)
        {
            string detail;
            switch (Math.Abs((long)error))
            {
                case 102:
                    detail = "Too many GPU frames remain in flight.";
                    break;
                case 103:
                    detail = "Unity's output texture has an incompatible Vulkan format or size.";
                    break;
                case 104:
                    detail = "The graphics device changed while a frame was pending.";
                    break;
                case 203:
                    detail = "The decoder did not provide a valid hardware surface.";
                    break;
                case 204:
                    detail = "The VAAPI frame is not a supported 8-bit NV12 SDR image.";
                    break;
                case 205:
                    detail = "The Vulkan driver lacks a required external-image function.";
                    break;
                case 206:
                    detail = "The DRM image has an unsupported plane or allocation layout.";
                    break;
                case 207:
                    detail = "The Vulkan driver cannot import this decoder's DRM format modifier.";
                    break;
                case 208:
                    detail = "The DMA-BUF allocation cannot be bound to the Vulkan image.";
                    break;
                case 2001:
                    detail = "Android API 26 image-reader functions are unavailable.";
                    break;
                case 2002:
                    detail = "FFmpeg has no Android Java VM.";
                    break;
                case 2003:
                    detail = "Android could not create the decoder's GPU output surface.";
                    break;
                case 2004:
                    detail = "FFmpeg could not initialize the MediaCodec hardware device.";
                    break;
                case 2006:
                    detail = "MediaCodec did not return the requested hardware image before the deadline.";
                    break;
                case 2010:
                    detail = "This hardware image uses an unsupported color space or HDR transfer function.";
                    break;
                case 400:
                    detail = "Vulkan Video was not negotiated before Unity device creation; enable plugin Preload and restart Unity.";
                    break;
                case 401:
                    detail = "Vulkan Video requires Vulkan 1.3 on both the instance and physical device.";
                    break;
                case 402:
                    detail = "The Vulkan driver could not report video decoding capabilities.";
                    break;
                case 403:
                    detail = "The Vulkan driver does not expose video queue and video decode queue extensions.";
                    break;
                case 404:
                    detail = "Vulkan Video requires timeline semaphores, synchronization2, and sampler YCbCr conversion.";
                    break;
                case 405:
                    detail = "Unity's Vulkan feature chain could not be safely extended for video decoding.";
                    break;
                case 406:
                    detail = "No independent graphics/compute queue is available for FFmpeg Vulkan operations.";
                    break;
                case 407:
                    detail = "No compatible Vulkan video decode queue or codec extension is available.";
                    break;
                case 408:
                    detail = "The driver rejected Vulkan Video device creation; Unity retained its original device configuration.";
                    break;
                case 409:
                    detail = "The loaded FFmpeg library cannot allocate a Vulkan device context.";
                    break;
                case 410:
                    detail = "The Vulkan video frame could not be retained.";
                    break;
                case 411:
                    detail = "The decoded Vulkan image belongs to a different device than Unity.";
                    break;
                case 412:
                    detail = "The decoded Vulkan frame has an unsupported image layout, layer count, or pixel format.";
                    break;
                case 413:
                    detail = "A required Vulkan Video device function is unavailable.";
                    break;
                case 414:
                    detail = "The Vulkan driver cannot sample the decoded format with YCbCr conversion.";
                    break;
                case 415:
                    detail = "The Vulkan decoded frame has invalid dimensions.";
                    break;
                case 416:
                    detail = "The Vulkan decoded frame has unsupported queue ownership or synchronization state.";
                    break;
                default:
                    detail = "The native hardware image operation failed.";
                    break;
            }

            return detail + " (native code " + error + ")";
        }

        /// <summary>Acquires an FFmpeg Vulkan Video device compatible with Unity's device.</summary>
        /// <returns>A newly owned FFmpeg AVBufferRef for the shared Vulkan Video device.</returns>
        /// <exception cref="NotSupportedException">The native bridge cannot acquire the negotiated Vulkan Video device.</exception>
        /// <exception cref="PlatformNotSupportedException">Vulkan Video interop is not implemented on the current platform.</exception>
        internal static IntPtr AcquireVideoDevice()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX || (UNITY_ANDROID && !UNITY_EDITOR)
            var device = Native.FfuVulkanVideoAcquireDevice();
            if (device == IntPtr.Zero)
            {
                throw new NotSupportedException("Vulkan Video device acquisition failed (native code " + Native.FfuVulkanVideoStatus() + ").");
            }

            return device;
#else
            throw new PlatformNotSupportedException();
#endif
        }

        /// <summary>Reports the native Vulkan Video initialization status.</summary>
        /// <returns>The native Vulkan Video status, or -1 on unsupported platforms.</returns>
        internal static int VideoStatus()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX || (UNITY_ANDROID && !UNITY_EDITOR)
            return Native.FfuVulkanVideoStatus();
#else
            return -1;
#endif
        }

        /// <summary>Acquires a VAAPI device matched to Unity's Vulkan DRM render node.</summary>
        /// <returns>An owned FFmpeg VAAPI device reference, or zero when unavailable.</returns>
        /// <exception cref="PlatformNotSupportedException">The operation is unavailable on the current platform.</exception>
        internal static IntPtr AcquireLinuxDevice()
        {
#if UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
            var device = Native.FfuVulkanAcquireDecodeDevice(0, 0);
            if (device != IntPtr.Zero)
            {
                MajDebug.LogDebug("FFmpeg", "[Interop] Acquired a VAAPI device matched to Unity's Vulkan render node.");
            }

            return device;
#else
            throw new PlatformNotSupportedException();
#endif
        }

        /// <summary>Registers Android's Java VM directly with FFmpeg once per process.</summary>
        /// <exception cref="NotSupportedException">The Java VM pointer is unavailable or libavcodec rejects Java VM registration.</exception>
        /// <exception cref="PlatformNotSupportedException">The caller is not running in an Android Player.</exception>
        internal static void InitializeAndroid()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            lock (s_androidInitializationLock)
            {
                if (s_androidInitialized)
                {
                    return;
                }

                var javaVm = UnityEngine.AndroidJNI.GetJavaVM();
                if (javaVm == IntPtr.Zero)
                {
                    throw new NotSupportedException("Unity's Android Java VM is unavailable.");
                }

                // ByteBuffer decoding needs libavcodec's JVM registration even when
                // the optional graphics bridge is absent or cannot share textures.
                int result = Native.AvJniSetJavaVm(javaVm, IntPtr.Zero);
                if (result < 0)
                {
                    throw new NotSupportedException("Could not initialize FFmpeg's Android Java VM (" + result + ").");
                }

                s_androidInitialized = true;
                MajDebug.LogDebug("FFmpeg", "[Interop] Registered Android's Java VM directly with libavcodec.");
            }
#else
            throw new PlatformNotSupportedException();
#endif
        }

        /// <summary>Maps a borrowed VAAPI frame to an owned DRM PRIME frame without downloading pixels.</summary>
        /// <param name="frame">The borrowed decoded frame to process without consuming its ownership.</param>
        /// <returns>An owned DRM PRIME AVFrame, which the caller must release.</returns>
        /// <exception cref="NotSupportedException">The bridge cannot map the VAAPI surface into a shareable DRM PRIME frame.</exception>
        /// <exception cref="PlatformNotSupportedException">The caller is not running on Linux.</exception>
        internal static IntPtr MapLinuxFrame(IntPtr frame)
        {
#if UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
            using var profile = UnityProfiler.Create("FFmpeg.Interop.MapVaapiToDrm");
            int result = Native.FfuVulkanMapFrame(frame, out var mapped);
            if (result < 0 || mapped == IntPtr.Zero)
            {
                throw new NotSupportedException(DescribeError(result));
            }

            return mapped;
#else
            throw new PlatformNotSupportedException();
#endif
        }

        /// <summary>Creates an owned MediaCodec surface session for native image transport.</summary>
        /// <param name="width">The requested frame or texture width in pixels.</param>
        /// <param name="height">The requested frame or texture height in pixels.</param>
        /// <returns>An owned hardware session that the decoder must dispose.</returns>
        /// <exception cref="PlatformNotSupportedException">The operation is unavailable on the current platform.</exception>
        internal static IHardwareDecodeSession CreateAndroidSession(int width, int height)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidSession(width, height);
#else
            throw new PlatformNotSupportedException();
#endif
        }

        /// <summary>Creates a native Vulkan presenter on supported platforms.</summary>
        /// <returns>An owned Vulkan presenter handle, or zero when unsupported or unavailable.</returns>
        internal static IntPtr Create()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX || (UNITY_ANDROID && !UNITY_EDITOR)
            return Native.FfuVulkanCreate();
#else
            return IntPtr.Zero;
#endif
        }

        /// <summary>Reads the latest native Vulkan presentation error.</summary>
        /// <param name="presenter">The native presentation context handle.</param>
        /// <returns>The native presentation error, or -1 on unsupported platforms.</returns>
        internal static int Error(IntPtr presenter)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX || (UNITY_ANDROID && !UNITY_EDITOR)
            return Native.FfuVulkanError(presenter);
#else
            return -1;
#endif
        }

        /// <summary>Retains a native frame or image and prepares GPU work targeting Unity's texture.</summary>
        /// <param name="presenter">The native presentation context handle.</param>
        /// <param name="frame">The borrowed decoded frame to process without consuming its ownership.</param>
        /// <param name="target">The native destination graphics resource or requested build target.</param>
        /// <returns>An owned presentation packet, or zero on failure; submit it to transfer ownership or cancel it.</returns>
        internal static IntPtr Prepare(IntPtr presenter, DecodedVideoFrame frame, IntPtr target)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX || (UNITY_ANDROID && !UNITY_EDITOR)
            if (frame.PixelFormat == global::FFmpeg.AutoGen.AVPixelFormat.AV_PIX_FMT_VULKAN)
            {
                return Native.FfuVulkanVideoPrepare(presenter, frame.NativeFrame, target);
            }

#endif
#if UNITY_ANDROID && !UNITY_EDITOR
            return Native.FfuAndroidPrepare(presenter, frame.NativeImage, target);
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
            return Native.FfuVulkanPrepare(presenter, frame.NativeFrame, target);
#else
            return IntPtr.Zero;
#endif
        }

        /// <summary>Releases an unsubmitted native Vulkan presentation packet.</summary>
        /// <param name="packet">The owned, unsubmitted native presentation packet to release.</param>
        internal static void Cancel(IntPtr packet)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX || (UNITY_ANDROID && !UNITY_EDITOR)
            Native.FfuVulkanCancel(packet);
#endif
        }

        /// <summary>Releases a Vulkan presenter while native frame retirement waits for outstanding GPU work.</summary>
        /// <param name="presenter">The native presentation context handle.</param>
        internal static void Release(IntPtr presenter)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX || (UNITY_ANDROID && !UNITY_EDITOR)
            Native.FfuVulkanRelease(presenter);
#endif
        }

        /// <summary>Polls released Vulkan frames until their GPU work has completed.</summary>
        /// <returns>Zero when retirement is complete; nonzero while frames remain pending.</returns>
        internal static int PollRetiredFrames()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX || (UNITY_ANDROID && !UNITY_EDITOR)
            return Native.FfuVulkanPoll();
#else
            return 0;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>Owns a MediaCodec output surface and transfers captured image references to presentation frames.</summary>
        private sealed class AndroidSession : IHardwareDecodeSession
        {
            /// <summary>Owns the native Android surface session, or zero after disposal.</summary>
            private IntPtr _session;
            /// <summary>Creates an Android GPU image reader and decoder surface of the requested dimensions.</summary>
            /// <param name="width">The requested frame or texture width in pixels.</param>
            /// <param name="height">The requested frame or texture height in pixels.</param>
            /// <exception cref="NotSupportedException">The native image reader or MediaCodec output surface cannot be created.</exception>
            internal AndroidSession(int width, int height)
            {
                _session = Native.FfuAndroidCreate(width, height);
                if (_session == IntPtr.Zero)
                {
                    throw new NotSupportedException(DescribeError(Native.FfuAndroidError(IntPtr.Zero)));
                }

                MajDebug.LogDebug("FFmpeg", "[Interop] Created Android GPU image session " + width + "x" + height + ".");
            }

            /// <summary>Acquires an owned FFmpeg device reference associated with this output surface.</summary>
            /// <returns>An owned FFmpeg device reference, or zero when unavailable.</returns>
            public IntPtr AcquireDevice() => Native.FfuAndroidDevice(_session);
            /// <summary>Consumes a MediaCodec output buffer once and captures its owned native image.</summary>
            /// <param name="frame">The borrowed AVFrame whose MediaCodec output buffer is consumed exactly once.</param>
            /// <returns>An owned native image to release after presentation completes.</returns>
            /// <exception cref="NotSupportedException">The MediaCodec output buffer cannot produce a shareable image within 500 milliseconds.</exception>
            public IntPtr CaptureFrame(IntPtr frame)
            {
                using var profile = UnityProfiler.Create("FFmpeg.Interop.CaptureAndroidImage");
                // Rendering a MediaCodec buffer consumes it exactly once. A timeout
                // fails this session; retrying the same AVFrame could reorder images.
                IntPtr image = Native.FfuAndroidCapture(_session, frame, 500);
                if (image == IntPtr.Zero)
                {
                    throw new NotSupportedException(DescribeError(Native.FfuAndroidError(_session)));
                }

                return image;
            }

            // Native image references retain their reader even after session disposal.
            /// <summary>Releases an image reference that can outlive its originating session.</summary>
            /// <param name="image">The owned native image reference to release.</param>
            public void ReleaseImage(IntPtr image) => Native.FfuAndroidImageRelease(image);
            /// <summary>Discards queued native images before seeking or flushing the decoder.</summary>
            public void Flush() => Native.FfuAndroidFlush(_session);
            /// <summary>Releases the surface session while outstanding images retain their own native references.</summary>
            public void Dispose()
            {
                if (_session == IntPtr.Zero)
                {
                    return;
                }

                Native.FfuAndroidRelease(_session);
                _session = IntPtr.Zero;
                MajDebug.LogDebug("FFmpeg", "[Interop] Released Android GPU image session; pending images retain their own native references.");
            }
        }

#endif
        /// <summary>Declares Cdecl bridge entry points for Vulkan, VAAPI, and Android native frame transport.</summary>
        private static class Native
        {
            /// <summary>Names the shared native FFmpeg graphics bridge.</summary>
            private const string Library = "FFmpegUnityBridge";
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX || (UNITY_ANDROID && !UNITY_EDITOR)
            /// <summary>Acquires an owned FFmpeg Vulkan device reference sharing Unity's negotiated video queues.</summary>
            /// <returns>An owned FFmpeg AVBufferRef to release with av_buffer_unref, or zero when unavailable.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_vulkan_video_acquire_device")]
            internal static extern IntPtr FfuVulkanVideoAcquireDevice();
            /// <summary>Reads the native Vulkan Video initialization status.</summary>
            /// <returns>Zero on success or no error; otherwise a native status or error code.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_vulkan_video_status")]
            internal static extern int FfuVulkanVideoStatus();
            /// <summary>Retains a Vulkan Video frame and prepares GPU color conversion into the target.</summary>
            /// <param name="presenter">The native presentation context handle.</param>
            /// <param name="frame">The borrowed decoded frame to process without consuming its ownership.</param>
            /// <param name="target">The borrowed native destination graphics resource owned by Unity.</param>
            /// <returns>An owned presentation packet, or zero on failure; submit it to transfer ownership to the render callback, or cancel it before submission.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_vulkan_video_prepare")]
            internal static extern IntPtr FfuVulkanVideoPrepare(IntPtr presenter, IntPtr frame, IntPtr target);
            /// <summary>Creates an owned Vulkan presentation context.</summary>
            /// <returns>An owned presenter handle to release with FfuVulkanRelease, or zero on failure.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_vulkan_create")]
            internal static extern IntPtr FfuVulkanCreate();
            /// <summary>Reads the last Vulkan presentation error code.</summary>
            /// <param name="presenter">The native presentation context handle.</param>
            /// <returns>Zero on success or no error; otherwise a native status or error code.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_vulkan_error")]
            internal static extern int FfuVulkanError(IntPtr presenter);
            /// <summary>Releases a Vulkan presenter and defers in-flight frame retirement until GPU completion.</summary>
            /// <param name="presenter">The native presentation context handle.</param>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_vulkan_release")]
            internal static extern void FfuVulkanRelease(IntPtr presenter);
            /// <summary>Releases an unsubmitted Vulkan presentation packet.</summary>
            /// <param name="packet">The owned, unsubmitted native presentation packet to release.</param>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_vulkan_cancel")]
            internal static extern void FfuVulkanCancel(IntPtr packet);
            /// <summary>Polls outstanding Vulkan frame retirements.</summary>
            /// <returns>The number of retired frames still pending GPU completion; zero when all are released.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_vulkan_poll")]
            internal static extern int FfuVulkanPoll();
#endif
#if UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
            /// <summary>Acquires an owned VAAPI device reference matching Unity's Vulkan DRM render node.</summary>
            /// <param name="width">The requested frame or texture width in pixels.</param>
            /// <param name="height">The requested frame or texture height in pixels.</param>
            /// <returns>An owned FFmpeg AVBufferRef to release with av_buffer_unref, or zero when no compatible VAAPI device is available.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_vulkan_acquire_decode_device")]
            internal static extern IntPtr FfuVulkanAcquireDecodeDevice(int width, int height);
            /// <summary>Maps a borrowed VAAPI frame to an owned DRM PRIME frame for DMA-BUF sharing.</summary>
            /// <param name="frame">The borrowed decoded frame to process without consuming its ownership.</param>
            /// <param name="mapped">Receives an owned mapped DRM PRIME frame; release it with av_frame_free.</param>
            /// <returns>Zero on success, or a negative native error code.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_vulkan_map_frame")]
            internal static extern int FfuVulkanMapFrame(IntPtr frame, out IntPtr mapped);
            /// <summary>Retains a mapped DRM PRIME frame for Vulkan GPU conversion.</summary>
            /// <param name="presenter">The native presentation context handle.</param>
            /// <param name="frame">The borrowed decoded frame to process without consuming its ownership.</param>
            /// <param name="target">The borrowed native destination graphics resource owned by Unity.</param>
            /// <returns>An owned presentation packet, or zero on failure; submit it to transfer ownership to the render callback, or cancel it before submission.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_vulkan_prepare")]
            internal static extern IntPtr FfuVulkanPrepare(IntPtr presenter, IntPtr frame, IntPtr target);
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
            /// <summary>Registers the process Java VM with libavcodec for MediaCodec decoding.</summary>
            /// <param name="javaVm">The process Java VM pointer supplied by AndroidJNI.</param>
            /// <param name="logContext">An optional native logging context, or zero when none is supplied.</param>
            /// <returns>Zero on success, or a negative FFmpeg error code.</returns>
            [DllImport("avcodec", CallingConvention = CallingConvention.Cdecl, EntryPoint = "av_jni_set_java_vm")]
            internal static extern int AvJniSetJavaVm(IntPtr javaVm, IntPtr logContext);
            /// <summary>Creates an owned Android image-reader and MediaCodec surface session.</summary>
            /// <param name="width">The requested frame or texture width in pixels.</param>
            /// <param name="height">The requested frame or texture height in pixels.</param>
            /// <returns>An owned surface session to release with FfuAndroidRelease, or zero on failure.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_android_create")]
            internal static extern IntPtr FfuAndroidCreate(int width, int height);
            /// <summary>Acquires an owned FFmpeg hardware device reference for an Android surface session.</summary>
            /// <param name="session">The native Android surface session handle.</param>
            /// <returns>An owned FFmpeg AVBufferRef to release with av_buffer_unref, or zero on failure.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_android_device")]
            internal static extern IntPtr FfuAndroidDevice(IntPtr session);
            /// <summary>Consumes a MediaCodec output buffer and captures an owned native image before the timeout.</summary>
            /// <param name="session">The native Android surface session handle.</param>
            /// <param name="frame">The borrowed AVFrame whose MediaCodec output buffer is consumed exactly once by this call.</param>
            /// <param name="timeoutMs">The maximum time to wait for a hardware image, in milliseconds.</param>
            /// <returns>An owned native image to release with FfuAndroidImageRelease, or zero on failure or timeout.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_android_capture")]
            internal static extern IntPtr FfuAndroidCapture(IntPtr session, IntPtr frame, int timeoutMs);
            /// <summary>Reads the Android session error or the last session creation error.</summary>
            /// <param name="session">The session handle, or zero to query the last session creation failure.</param>
            /// <returns>Zero on success or no error; otherwise a native status or error code.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_android_error")]
            internal static extern int FfuAndroidError(IntPtr session);
            /// <summary>Releases an owned Android image and its retained reader reference.</summary>
            /// <param name="image">The owned native image reference to release.</param>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_android_image_release")]
            internal static extern void FfuAndroidImageRelease(IntPtr image);
            /// <summary>Discards images queued by an Android surface session.</summary>
            /// <param name="session">The native Android surface session handle.</param>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_android_flush")]
            internal static extern void FfuAndroidFlush(IntPtr session);
            /// <summary>Releases an Android session while captured images retain their reader references.</summary>
            /// <param name="session">The native Android surface session handle.</param>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_android_release")]
            internal static extern void FfuAndroidRelease(IntPtr session);
            /// <summary>Retains an Android hardware image for Vulkan GPU conversion.</summary>
            /// <param name="presenter">The native presentation context handle.</param>
            /// <param name="image">The borrowed native image to retain for GPU presentation.</param>
            /// <param name="target">The borrowed native destination graphics resource owned by Unity.</param>
            /// <returns>An owned presentation packet, or zero on failure; submit it to transfer ownership to the render callback, or cancel it before submission.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_android_prepare")]
            internal static extern IntPtr FfuAndroidPrepare(IntPtr presenter, IntPtr image, IntPtr target);
#endif
        }
    }
}
