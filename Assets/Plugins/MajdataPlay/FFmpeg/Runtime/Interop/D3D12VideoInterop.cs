#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using MajdataPlay.Diagnostics;

namespace MajdataPlay.FFmpeg.Interop
{
    /// <summary>Coordinates D3D12VA frame completion on the decoding worker.</summary>
    internal static class D3D12VideoInterop
    {
        /// <summary>Waits for a borrowed D3D12VA frame's decode fence before it enters the presentation queue.</summary>
        /// <param name="frame">The borrowed FFmpeg hardware frame retained by the caller throughout the wait.</param>
        /// <param name="cancellationToken">Cancels the wait when the decoding session closes.</param>
        /// <param name="timeoutMilliseconds">The maximum GPU wait time in milliseconds.</param>
        /// <exception cref="OperationCanceledException">The decoding session was canceled.</exception>
        /// <exception cref="TimeoutException">The GPU did not complete before the timeout.</exception>
        /// <exception cref="NotSupportedException">The bridge or hardware frame cannot provide completion status.</exception>
        /// <exception cref="PlatformNotSupportedException">D3D12VA interop is unavailable on this platform.</exception>
        internal static void WaitForVideoFrame(IntPtr frame, CancellationToken cancellationToken, int timeoutMilliseconds)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            using var profile = UnityProfiler.Create("FFmpeg.Decoder.WaitForD3D12Frame");
            var deadline = Stopwatch.GetTimestamp() + (long)(timeoutMilliseconds * (double)Stopwatch.Frequency / 1000);
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = Native.FfuD3D12VAFrameReady(frame);
                    if (result > 0)
                    {
                        return;
                    }

                    if (result < 0)
                    {
                        throw new NotSupportedException("D3D12VA GPU completion query failed (native code " + result + ").");
                    }

                    if (Stopwatch.GetTimestamp() >= deadline)
                    {
                        throw new TimeoutException("Timed out waiting for D3D12VA GPU video decoding on the background worker.");
                    }

                    if (cancellationToken.WaitHandle.WaitOne(1))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }
            }
            catch (EntryPointNotFoundException error)
            {
                throw new NotSupportedException("Rebuild FFmpegUnityBridge and restart Unity to enable worker-side D3D12VA frame synchronization.", error);
            }
#else
            throw new PlatformNotSupportedException();
#endif
        }

        /// <summary>Declares the native bridge's D3D12VA frame completion query.</summary>
        private static class Native
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            /// <summary>Names the shared native FFmpeg graphics bridge.</summary>
            private const string Library = "FFmpegUnityBridge";
            /// <summary>Queries a borrowed D3D12VA frame's decode fence without waiting for GPU work.</summary>
            /// <param name="frame">The borrowed FFmpeg D3D12VA frame.</param>
            /// <returns>One when complete, zero while pending, or a negative native error.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_d3d12va_frame_ready")]
            internal static extern int FfuD3D12VAFrameReady(IntPtr frame);
#endif
        }
    }
}
