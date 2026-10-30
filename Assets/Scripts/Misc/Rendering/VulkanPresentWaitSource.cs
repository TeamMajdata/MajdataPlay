#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Runtime.InteropServices;

#nullable enable
namespace MajdataPlay.Rendering
{
    /// <summary>
    /// Reads Vulkan presentation progress from the FFmpeg graphics bridge, which enables VK_KHR_present_id and
    /// VK_KHR_present_wait on Unity's device when the driver supports them and tags every presentation with an id.
    /// </summary>
    internal sealed class VulkanPresentWaitSource : IPresentWaitSource
    {
        /// <summary>The status reported when present waiting is enabled on Unity's Vulkan device.</summary>
        internal const int StatusEnabled = 0;

        /// <summary>The status reported when the bridge library is not loaded.</summary>
        internal const int StatusBridgeMissing = -100;

        /// <summary>The status reported when the loaded bridge predates present waiting.</summary>
        internal const int StatusBridgeOutdated = -101;

        /// <summary>Reads whether the bridge enabled present waiting on Unity's Vulkan device.</summary>
        /// <returns><see cref="StatusEnabled"/>, a positive bridge status, <see cref="StatusBridgeMissing"/> or <see cref="StatusBridgeOutdated"/>.</returns>
        public static int GetStatus()
        {
            try
            {
                return Native.Status();
            }
            catch (DllNotFoundException)
            {
                return StatusBridgeMissing;
            }
            catch (EntryPointNotFoundException)
            {
                return StatusBridgeOutdated;
            }
        }

        /// <summary>Describes a status returned by <see cref="GetStatus"/>.</summary>
        /// <param name="status">The status.</param>
        /// <returns>A short English description for logs.</returns>
        public static string DescribeStatus(int status)
        {
            return status switch
            {
                StatusEnabled => "enabled",
                1 => "no Vulkan device was created through the bridge",
                2 => "the GPU driver does not support VK_KHR_present_id and VK_KHR_present_wait",
                3 => "the GPU driver rejected VK_KHR_present_id and VK_KHR_present_wait",
                4 => "vkWaitForPresentKHR is unavailable",
                StatusBridgeMissing => "the FFmpeg graphics bridge is not loaded",
                StatusBridgeOutdated => "the FFmpeg graphics bridge predates present waiting",
                _ => "unknown status " + status,
            };
        }

        /// <inheritdoc/>
        public bool TryQuery(out ulong generation, out ulong presentId)
        {
            return Native.Query(out generation, out presentId) == 1;
        }

        /// <inheritdoc/>
        public int Poll(ulong generation, ulong presentId)
        {
            return Native.Poll(generation, presentId);
        }

        /// <summary>Declares the Cdecl bridge entry points for present waiting.</summary>
        private static class Native
        {
            /// <summary>Names the native FFmpeg graphics bridge.</summary>
            private const string Library = "FFmpegUnityBridge";

            /// <summary>Reads whether present waiting is enabled on Unity's Vulkan device.</summary>
            /// <returns>Zero when enabled, otherwise a positive reason code.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_present_wait_status")]
            internal static extern int Status();

            /// <summary>Reads the tracked swapchain generation and its latest successful presentation id.</summary>
            /// <param name="generation">Receives the swapchain generation.</param>
            /// <param name="presentId">Receives the latest presentation id, or zero.</param>
            /// <returns>One on success, or a negative code when present waiting is unavailable.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_present_wait_query")]
            internal static extern int Query(out ulong generation, out ulong presentId);

            /// <summary>Checks without blocking whether a presentation has been shown.</summary>
            /// <param name="generation">The generation the id belongs to.</param>
            /// <param name="presentId">The presentation id.</param>
            /// <returns>One when shown, zero while pending, minus one when the swapchain changed, or another negative error.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_present_wait_poll")]
            internal static extern int Poll(ulong generation, ulong presentId);
        }
    }
}
#endif
