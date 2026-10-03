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
        static readonly object AndroidInitializationLock = new object();
        static bool _androidInitialized;
#endif
        internal static string DescribeError(int error)
        {
            string detail;
            switch (Math.Abs((long)error))
            {
                case 102: detail = "Too many GPU frames remain in flight."; break;
                case 103: detail = "Unity's output texture has an incompatible Vulkan format or size."; break;
                case 104: detail = "The graphics device changed while a frame was pending."; break;
                case 203: detail = "The decoder did not provide a valid hardware surface."; break;
                case 204: detail = "The VAAPI frame is not a supported 8-bit NV12 SDR image."; break;
                case 205: detail = "The Vulkan driver lacks a required external-image function."; break;
                case 206: detail = "The DRM image has an unsupported plane or allocation layout."; break;
                case 207: detail = "The Vulkan driver cannot import this decoder's DRM format modifier."; break;
                case 208: detail = "The DMA-BUF allocation cannot be bound to the Vulkan image."; break;
                case 2001: detail = "Android API 26 image-reader functions are unavailable."; break;
                case 2002: detail = "FFmpeg has no Android Java VM."; break;
                case 2003: detail = "Android could not create the decoder's GPU output surface."; break;
                case 2004: detail = "FFmpeg could not initialize the MediaCodec hardware device."; break;
                case 2006: detail = "MediaCodec did not return the requested hardware image before the deadline."; break;
                case 2010: detail = "This hardware image uses an unsupported color space or HDR transfer function."; break;
                case 400: detail = "Vulkan Video was not negotiated before Unity device creation; enable plugin Preload and restart Unity."; break;
                case 401: detail = "Vulkan Video requires Vulkan 1.3 on both the instance and physical device."; break;
                case 402: detail = "The Vulkan driver could not report video decoding capabilities."; break;
                case 403: detail = "The Vulkan driver does not expose video queue and video decode queue extensions."; break;
                case 404: detail = "Vulkan Video requires timeline semaphores, synchronization2, and sampler YCbCr conversion."; break;
                case 405: detail = "Unity's Vulkan feature chain could not be safely extended for video decoding."; break;
                case 406: detail = "No independent graphics/compute queue is available for FFmpeg Vulkan operations."; break;
                case 407: detail = "No compatible Vulkan video decode queue or codec extension is available."; break;
                case 408: detail = "The driver rejected Vulkan Video device creation; Unity retained its original device configuration."; break;
                case 409: detail = "The loaded FFmpeg library cannot allocate a Vulkan device context."; break;
                case 410: detail = "The Vulkan video frame could not be retained."; break;
                case 411: detail = "The decoded Vulkan image belongs to a different device than Unity."; break;
                case 412: detail = "The decoded Vulkan frame has an unsupported image layout, layer count, or pixel format."; break;
                case 413: detail = "A required Vulkan Video device function is unavailable."; break;
                case 414: detail = "The Vulkan driver cannot sample the decoded format with YCbCr conversion."; break;
                case 415: detail = "The Vulkan decoded frame has invalid dimensions."; break;
                case 416: detail = "The Vulkan decoded frame has unsupported queue ownership or synchronization state."; break;
                default: detail = "The native hardware image operation failed."; break;
            }
            return detail + " (native code " + error + ")";
        }
        internal static IntPtr AcquireVideoDevice()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX || (UNITY_ANDROID && !UNITY_EDITOR)
            var device = Native.ffu_vulkan_video_acquire_device();
            if (device == IntPtr.Zero)
                throw new NotSupportedException("Vulkan Video device acquisition failed (native code " + Native.ffu_vulkan_video_status() + ").");
            return device;
#else
            throw new PlatformNotSupportedException();
#endif
        }

        internal static int VideoStatus()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX || (UNITY_ANDROID && !UNITY_EDITOR)
            return Native.ffu_vulkan_video_status();
#else
            return -1;
#endif
        }

        internal static IntPtr AcquireLinuxDevice()
        {
#if UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
            var device = Native.ffu_vulkan_acquire_decode_device(0, 0);
            if (device != IntPtr.Zero)
                MajDebug.LogDebug("FFmpeg", "[Interop] Acquired a VAAPI device matched to Unity's Vulkan render node.");
            return device;
#else
            throw new PlatformNotSupportedException();
#endif
        }

        internal static void InitializeAndroid()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            lock (AndroidInitializationLock)
            {
                if (_androidInitialized) return;
                var javaVm = UnityEngine.AndroidJNI.GetJavaVM();
                if (javaVm == IntPtr.Zero)
                    throw new NotSupportedException("Unity's Android Java VM is unavailable.");
                // ByteBuffer decoding needs libavcodec's JVM registration even when
                // the optional graphics bridge is absent or cannot share textures.
                int result = Native.av_jni_set_java_vm(javaVm, IntPtr.Zero);
                if (result < 0)
                    throw new NotSupportedException("Could not initialize FFmpeg's Android Java VM (" + result + ").");
                _androidInitialized = true;
                MajDebug.LogDebug("FFmpeg", "[Interop] Registered Android's Java VM directly with libavcodec.");
            }
#else
            throw new PlatformNotSupportedException();
#endif
        }

        internal static IntPtr MapLinuxFrame(IntPtr frame)
        {
#if UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
            using var profile = UnityProfiler.Create("FFmpeg.Interop.MapVaapiToDrm");
            int result = Native.ffu_vulkan_map_frame(frame, out var mapped);
            if (result < 0 || mapped == IntPtr.Zero)
                throw new NotSupportedException(DescribeError(result));
            return mapped;
#else
            throw new PlatformNotSupportedException();
#endif
        }

        internal static IHardwareDecodeSession CreateAndroidSession(int width, int height)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidSession(width, height);
#else
            throw new PlatformNotSupportedException();
#endif
        }

        internal static IntPtr Create()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX || (UNITY_ANDROID && !UNITY_EDITOR)
            return Native.ffu_vulkan_create();
#else
            return IntPtr.Zero;
#endif
        }
        internal static int Error(IntPtr presenter)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX || (UNITY_ANDROID && !UNITY_EDITOR)
            return Native.ffu_vulkan_error(presenter);
#else
            return -1;
#endif
        }
        internal static IntPtr Prepare(IntPtr presenter, DecodedVideoFrame frame, IntPtr target)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX || (UNITY_ANDROID && !UNITY_EDITOR)
            if (frame.PixelFormat == global::FFmpeg.AutoGen.AVPixelFormat.AV_PIX_FMT_VULKAN)
                return Native.ffu_vulkan_video_prepare(presenter, frame.NativeFrame, target);
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
            return Native.ffu_android_prepare(presenter, frame.NativeImage, target);
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
            return Native.ffu_vulkan_prepare(presenter, frame.NativeFrame, target);
#else
            return IntPtr.Zero;
#endif
        }
        internal static void Cancel(IntPtr packet)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX || (UNITY_ANDROID && !UNITY_EDITOR)
            Native.ffu_vulkan_cancel(packet);
#endif
        }
        internal static void Release(IntPtr presenter)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX || (UNITY_ANDROID && !UNITY_EDITOR)
            Native.ffu_vulkan_release(presenter);
#endif
        }
        internal static int PollRetiredFrames()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX || (UNITY_ANDROID && !UNITY_EDITOR)
            return Native.ffu_vulkan_poll();
#else
            return 0;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        sealed class AndroidSession : IHardwareDecodeSession
        {
            IntPtr _session;
            internal AndroidSession(int width, int height)
            {
                _session = Native.ffu_android_create(width, height);
                if (_session == IntPtr.Zero)
                    throw new NotSupportedException(DescribeError(Native.ffu_android_error(IntPtr.Zero)));
                MajDebug.LogDebug("FFmpeg", "[Interop] Created Android GPU image session " + width + "x" + height + ".");
            }
            public IntPtr AcquireDevice() => Native.ffu_android_device(_session);
            public IntPtr CaptureFrame(IntPtr frame)
            {
                using var profile = UnityProfiler.Create("FFmpeg.Interop.CaptureAndroidImage");
                // Rendering a MediaCodec buffer consumes it exactly once. A timeout
                // fails this session; retrying the same AVFrame could reorder images.
                IntPtr image = Native.ffu_android_capture(_session, frame, 500);
                if (image == IntPtr.Zero)
                    throw new NotSupportedException(DescribeError(Native.ffu_android_error(_session)));
                return image;
            }
            // Native image references retain their reader even after session disposal.
            public void ReleaseImage(IntPtr image) => Native.ffu_android_image_release(image);
            public void Flush() => Native.ffu_android_flush(_session);
            public void Dispose()
            {
                if (_session == IntPtr.Zero) return;
                Native.ffu_android_release(_session);
                _session = IntPtr.Zero;
                MajDebug.LogDebug("FFmpeg", "[Interop] Released Android GPU image session; pending images retain their own native references.");
            }
        }
#endif

        static class Native
        {
            const string Library = "FFmpegUnityBridge";
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX || (UNITY_ANDROID && !UNITY_EDITOR)
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ffu_vulkan_video_acquire_device();
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int ffu_vulkan_video_status();
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ffu_vulkan_video_prepare(IntPtr presenter, IntPtr frame, IntPtr target);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ffu_vulkan_create();
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int ffu_vulkan_error(IntPtr presenter);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ffu_vulkan_release(IntPtr presenter);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ffu_vulkan_cancel(IntPtr packet);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int ffu_vulkan_poll();
#endif
#if UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ffu_vulkan_acquire_decode_device(int width, int height);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int ffu_vulkan_map_frame(IntPtr frame, out IntPtr mapped);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ffu_vulkan_prepare(IntPtr presenter, IntPtr frame, IntPtr target);
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
            [DllImport("avcodec", CallingConvention = CallingConvention.Cdecl)] internal static extern int av_jni_set_java_vm(IntPtr javaVm, IntPtr logContext);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ffu_android_create(int width, int height);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ffu_android_device(IntPtr session);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ffu_android_capture(IntPtr session, IntPtr frame, int timeoutMs);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int ffu_android_error(IntPtr session);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ffu_android_image_release(IntPtr image);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ffu_android_flush(IntPtr session);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ffu_android_release(IntPtr session);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ffu_android_prepare(IntPtr presenter, IntPtr image, IntPtr target);
#endif
        }
    }
}
