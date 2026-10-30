#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Runtime.InteropServices;

#nullable enable
namespace MajdataPlay.Rendering
{
    /// <summary>
    /// Reads Vulkan presentation progress from the MajdataPlayVulkanPacing native layer, which wraps Unity's
    /// swapchain calls and tags every presentation with an id when Unity's device has VK_KHR_present_id and
    /// VK_KHR_present_wait. Sources live in Tools/Native/VulkanPacing.
    /// </summary>
    internal sealed class VulkanPresentWaitSource : IPresentWaitSource
    {
        /// <summary>The status reported when present waiting is enabled on Unity's Vulkan device.</summary>
        internal const int StatusEnabled = 0;

        /// <summary>The status reported when the native layer is not loaded.</summary>
        internal const int StatusLayerMissing = -100;

        /// <summary>The status reported when the loaded native layer lacks the expected entry points.</summary>
        internal const int StatusLayerOutdated = -101;

        /// <summary>Reads whether present waiting is enabled on Unity's Vulkan device.</summary>
        /// <returns><see cref="StatusEnabled"/>, a positive layer status, <see cref="StatusLayerMissing"/> or <see cref="StatusLayerOutdated"/>.</returns>
        public static int GetStatus()
        {
            try
            {
                return Native.Status();
            }
            catch (DllNotFoundException)
            {
                return StatusLayerMissing;
            }
            catch (EntryPointNotFoundException)
            {
                return StatusLayerOutdated;
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
                1 => "no Vulkan device was created through the pacing layer",
                2 => "Unity created its Vulkan device without VK_KHR_present_id and VK_KHR_present_wait",
                3 => "vkWaitForPresentKHR is unavailable",
                StatusLayerMissing => "the MajdataPlayVulkanPacing layer is not loaded",
                StatusLayerOutdated => "the MajdataPlayVulkanPacing layer lacks the present wait entry points",
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

        /// <summary>Declares the Cdecl entry points of the native pacing layer.</summary>
        private static class Native
        {
            /// <summary>Names the native Vulkan pacing layer.</summary>
            private const string Library = "MajdataPlayVulkanPacing";

            /// <summary>Reads whether present waiting is enabled on Unity's Vulkan device.</summary>
            /// <returns>Zero when enabled, otherwise a positive reason code.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "majvk_present_wait_status")]
            internal static extern int Status();

            /// <summary>Reads the tracked swapchain generation and its latest successful presentation id.</summary>
            /// <param name="generation">Receives the swapchain generation.</param>
            /// <param name="presentId">Receives the latest presentation id, or zero.</param>
            /// <returns>One on success, or a negative code when present waiting is unavailable.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "majvk_present_wait_query")]
            internal static extern int Query(out ulong generation, out ulong presentId);

            /// <summary>Checks without blocking whether a presentation has been shown.</summary>
            /// <param name="generation">The generation the id belongs to.</param>
            /// <param name="presentId">The presentation id.</param>
            /// <returns>One when shown, zero while pending, minus one when the swapchain changed, or another negative error.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "majvk_present_wait_poll")]
            internal static extern int Poll(ulong generation, ulong presentId);
        }
    }
}
#endif
