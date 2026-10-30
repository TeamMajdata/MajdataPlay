#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Runtime.InteropServices;

#nullable enable
namespace MajdataPlay.Rendering
{
    /// <summary>
    /// Calls the MajdataPlayVulkanPacing native layer (sources in Tools/Native/VulkanPacing), which wraps Unity's
    /// Vulkan swapchain calls and provides the driver's low-latency mode and present waiting.
    /// </summary>
    internal static class VulkanPacingLayer
    {
        /// <summary>The status reported when the layer can be used.</summary>
        internal const int StatusAvailable = 0;

        /// <summary>The status reported when the native layer is not loaded.</summary>
        internal const int StatusLayerMissing = -100;

        /// <summary>The status reported when the loaded native layer predates the required ABI.</summary>
        internal const int StatusLayerOutdated = -101;

        /// <summary>The native ABI version this code requires.</summary>
        internal const int RequiredAbiVersion = 2;

        /// <summary>The backend value for VK_NV_low_latency2.</summary>
        internal const int BackendNvidia = 1;

        /// <summary>The backend value for VK_AMD_anti_lag.</summary>
        internal const int BackendAmd = 2;

        /// <summary>The cached result of <see cref="Availability"/>.</summary>
        private static int? _availability;

        /// <summary>Gets whether the layer is loaded with the required ABI.</summary>
        /// <returns><see cref="StatusAvailable"/>, <see cref="StatusLayerMissing"/> or <see cref="StatusLayerOutdated"/>.</returns>
        public static int Availability
        {
            get
            {
                if (_availability is int availability)
                {
                    return availability;
                }
                try
                {
                    availability = Native.AbiVersion() >= RequiredAbiVersion ? StatusAvailable : StatusLayerOutdated;
                }
                catch (DllNotFoundException)
                {
                    availability = StatusLayerMissing;
                }
                catch (EntryPointNotFoundException)
                {
                    availability = StatusLayerOutdated;
                }
                _availability = availability;
                return availability;
            }
        }

        /// <summary>Reads whether present waiting is enabled on Unity's Vulkan device.</summary>
        /// <returns>Zero when enabled, a positive layer status, or a negative <see cref="Availability"/> status.</returns>
        public static int GetPresentWaitStatus()
        {
            return Availability != StatusAvailable ? Availability : Native.PresentWaitStatus();
        }

        /// <summary>Reads the driver low-latency backend active on Unity's Vulkan device.</summary>
        /// <param name="status">Receives zero when active, a positive layer status, or a negative <see cref="Availability"/> status.</param>
        /// <returns><see cref="BackendNvidia"/>, <see cref="BackendAmd"/>, or zero when none is active.</returns>
        public static int GetLowLatencyBackend(out int status)
        {
            if (Availability != StatusAvailable)
            {
                status = Availability;
                return 0;
            }
            status = Native.LowLatencyStatus();
            return Native.LowLatencyBackend();
        }

        /// <summary>Reads the generation of the tracked swapchain, which changes whenever Unity recreates it.</summary>
        /// <returns>The generation, or zero when the layer is unavailable.</returns>
        public static ulong GetSwapchainGeneration()
        {
            return Availability != StatusAvailable ? 0 : Native.SwapchainGeneration();
        }

        /// <summary>Reads where presentation ids come from.</summary>
        /// <returns>Zero before the first tracked presentation, one when the layer attaches them, two when Unity does, three when Unity's are replaced with frame ids.</returns>
        public static int GetPresentIdSource()
        {
            return Availability != StatusAvailable ? 0 : Native.PresentIdSource();
        }

        /// <summary>Describes a status returned by <see cref="GetPresentWaitStatus"/>.</summary>
        /// <param name="status">The status.</param>
        /// <returns>A short English description for logs.</returns>
        public static string DescribePresentWaitStatus(int status)
        {
            return status switch
            {
                0 => "enabled",
                1 => "no Vulkan device was created through the pacing layer",
                2 => "Unity created its Vulkan device without VK_KHR_present_id and VK_KHR_present_wait",
                3 => "vkWaitForPresentKHR is unavailable",
                _ => DescribeAvailability(status),
            };
        }

        /// <summary>Describes a status returned by <see cref="GetLowLatencyBackend"/>.</summary>
        /// <param name="status">The status.</param>
        /// <returns>A short English description for logs.</returns>
        public static string DescribeLowLatencyStatus(int status)
        {
            return status switch
            {
                0 => "active",
                1 => "no Vulkan device was created through the pacing layer",
                2 => "the driver supports neither VK_NV_low_latency2 (with Unity's present ids) nor VK_AMD_anti_lag",
                3 => "the driver rejected the device with the low-latency extension",
                4 => "a low-latency entry point or the timeline semaphore is unavailable",
                _ => DescribeAvailability(status),
            };
        }

        /// <summary>Names a backend returned by <see cref="GetLowLatencyBackend"/>.</summary>
        /// <param name="backend">The backend.</param>
        /// <returns>The extension name.</returns>
        public static string DescribeBackend(int backend)
        {
            return backend switch
            {
                BackendNvidia => "VK_NV_low_latency2",
                BackendAmd => "VK_AMD_anti_lag",
                _ => "none",
            };
        }

        /// <summary>Describes a negative <see cref="Availability"/> status.</summary>
        /// <param name="status">The status.</param>
        /// <returns>A short English description for logs.</returns>
        private static string DescribeAvailability(int status)
        {
            return status switch
            {
                StatusLayerMissing => "the MajdataPlayVulkanPacing layer is not loaded",
                StatusLayerOutdated => "the MajdataPlayVulkanPacing layer predates ABI " + RequiredAbiVersion,
                _ => "unknown status " + status,
            };
        }

        /// <summary>Starts and ends frames through the driver's low-latency mode.</summary>
        internal sealed class Driver : ILowLatencyDriver
        {
            /// <inheritdoc/>
            public int BeginFrame(ulong timeoutNanoseconds)
            {
                return Native.FrameBegin(timeoutNanoseconds);
            }

            /// <inheritdoc/>
            public int EndFrame()
            {
                return Native.FrameEnd();
            }
        }

        /// <summary>Declares the Cdecl entry points of the native pacing layer.</summary>
        internal static class Native
        {
            /// <summary>Names the native Vulkan pacing layer.</summary>
            private const string Library = "MajdataPlayVulkanPacing";

            /// <summary>Reads the version of the layer's C ABI.</summary>
            /// <returns>The ABI version.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "majvk_abi_version")]
            internal static extern int AbiVersion();

            /// <summary>Reads whether present waiting is enabled on Unity's Vulkan device.</summary>
            /// <returns>Zero when enabled, otherwise a positive reason code.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "majvk_present_wait_status")]
            internal static extern int PresentWaitStatus();

            /// <summary>Reads the tracked swapchain generation and its latest successful presentation id.</summary>
            /// <param name="generation">Receives the swapchain generation.</param>
            /// <param name="presentId">Receives the latest presentation id, or zero.</param>
            /// <returns>One on success, or a negative code when present waiting is unavailable.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "majvk_present_wait_query")]
            internal static extern int PresentWaitQuery(out ulong generation, out ulong presentId);

            /// <summary>Checks without blocking whether a presentation has been shown.</summary>
            /// <param name="generation">The generation the id belongs to.</param>
            /// <param name="presentId">The presentation id.</param>
            /// <returns>One when shown, zero while pending, minus one when the swapchain changed, or another negative error.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "majvk_present_wait_poll")]
            internal static extern int PresentWaitPoll(ulong generation, ulong presentId);

            /// <summary>Reads the active driver low-latency backend.</summary>
            /// <returns>Zero for none, one for VK_NV_low_latency2, two for VK_AMD_anti_lag.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "majvk_low_latency_backend")]
            internal static extern int LowLatencyBackend();

            /// <summary>Reads why the driver low-latency backend is or is not active.</summary>
            /// <returns>Zero when active, otherwise a positive reason code.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "majvk_low_latency_status")]
            internal static extern int LowLatencyStatus();

            /// <summary>Starts a frame through the driver's low-latency mode.</summary>
            /// <param name="timeoutNanoseconds">The longest time to block.</param>
            /// <returns>One when started, zero when skipped, two on timeout, or a negative error.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "majvk_frame_begin")]
            internal static extern int FrameBegin(ulong timeoutNanoseconds);

            /// <summary>Ends the main-thread part of the frame started last.</summary>
            /// <returns>One when recorded, zero when skipped, or a negative code when inactive.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "majvk_frame_end")]
            internal static extern int FrameEnd();

            /// <summary>Reads the generation of the tracked swapchain.</summary>
            /// <returns>The generation.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "majvk_swapchain_generation")]
            internal static extern ulong SwapchainGeneration();

            /// <summary>Reads where presentation ids come from.</summary>
            /// <returns>Zero for none yet, one for the layer, two for Unity.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "majvk_present_id_source")]
            internal static extern int PresentIdSource();
        }
    }
}
#endif
