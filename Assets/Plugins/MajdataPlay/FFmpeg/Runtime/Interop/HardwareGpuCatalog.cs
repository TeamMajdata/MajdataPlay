#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using FFmpeg.AutoGen;
using MajdataPlay.Diagnostics;
using UnityEngine;

namespace MajdataPlay.FFmpeg.Interop
{
    /// <summary>Lists the GPUs that hardware codecs may use, Unity's renderer first.</summary>
    /// <remarks>
    /// Windows lists DXGI hardware adapters and Linux lists DRM render nodes; other platforms select hardware codecs
    /// internally and return no GPUs. Unity's renderer is matched by PCI vendor and device ID, so identical GPUs are
    /// indistinguishable: the first of them is treated as the renderer and the others are not listed.
    /// </remarks>
    internal static class HardwareGpuCatalog
    {
        /// <summary>Stores the PCI vendor ID of Unity's renderer, or zero before it is captured or when it is unknown.</summary>
        private static int s_rendererVendorId;
        /// <summary>Stores the PCI device ID of Unity's renderer, or zero before it is captured or when it is unknown.</summary>
        private static int s_rendererDeviceId;

        /// <summary>Gets the FFmpeg hardware device type whose device strings select the listed GPUs, or NONE on platforms without GPU selection.</summary>
        internal static AVHWDeviceType DeviceType =>
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA;
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
            AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI;
#else
            AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
#endif

        /// <summary>Records Unity's renderer identity on the main thread for later use by codec workers.</summary>
        /// <remarks>Runs before the first scene loads and after each Editor domain reload; batch mode without graphics records an unknown renderer.</remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        internal static void CaptureRenderer()
        {
            Volatile.Write(ref s_rendererVendorId, SystemInfo.graphicsDeviceVendorID);
            Volatile.Write(ref s_rendererDeviceId, SystemInfo.graphicsDeviceID);
        }

        /// <summary>Lists the system's GPUs for hardware codecs, Unity's renderer first.</summary>
        /// <param name="route">Whether GPUs other than Unity's renderer remain in the list.</param>
        /// <returns>
        /// The ordered GPUs, or an empty list when this platform does not select GPUs or enumeration failed;
        /// callers then use FFmpeg's default hardware device in place of Unity's renderer.
        /// </returns>
        internal static IReadOnlyList<HardwareGpu> GetCandidates(HardwareFallbackRoute route)
        {
            IReadOnlyList<HardwareGpu> gpus;
            try
            {
                gpus = Enumerate();
            }
            catch (Exception error) when (error is DllNotFoundException || error is EntryPointNotFoundException
                || error is InvalidOperationException || error is IOException || error is UnauthorizedAccessException
                || error is NotSupportedException)
            {
                MajDebug.LogWarning("FFmpeg", "[Interop] GPU enumeration failed; using FFmpeg's default hardware device. " + error.Message);
                return Array.Empty<HardwareGpu>();
            }

            return Restrict(Order(gpus, Volatile.Read(ref s_rendererVendorId), Volatile.Read(ref s_rendererDeviceId)), route);
        }

        /// <summary>Moves Unity's renderer to the front, keeps the remaining GPUs in system order, and drops repeated identical GPUs.</summary>
        /// <param name="gpus">The system's GPUs in enumeration order.</param>
        /// <param name="rendererVendorId">The PCI vendor ID of Unity's renderer, or zero when unknown.</param>
        /// <param name="rendererDeviceId">The PCI device ID of Unity's renderer.</param>
        /// <returns>
        /// The GPUs with the renderer first. When the renderer is unknown or not listed, the first GPU becomes the default
        /// stand-in because Unity normally renders on the system default adapter, which DXGI and FFmpeg enumerate first.
        /// A GPU with the same PCI identity and name as an earlier one is dropped: identical GPUs have the same codecs, and
        /// virtual display adapters repeat the description of the GPU that renders for them.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="gpus"/> is null.</exception>
        internal static IReadOnlyList<HardwareGpu> Order(IReadOnlyList<HardwareGpu> gpus, int rendererVendorId, int rendererDeviceId)
        {
            if (gpus == null)
            {
                throw new ArgumentNullException(nameof(gpus));
            }

            if (gpus.Count == 0)
            {
                return Array.Empty<HardwareGpu>();
            }

            var renderer = -1;
            if (rendererVendorId != 0)
            {
                for (var i = 0; i < gpus.Count; i++)
                {
                    if (gpus[i].VendorId == rendererVendorId && gpus[i].DeviceId == rendererDeviceId)
                    {
                        renderer = i;
                        break;
                    }
                }
            }

            var first = Math.Max(renderer, 0);
            var ordered = new List<HardwareGpu>(gpus.Count)
            {
                gpus[first].WithRole(renderer >= 0 ? HardwareGpuRole.Renderer : HardwareGpuRole.Default)
            };
            for (var i = 0; i < gpus.Count; i++)
            {
                if (i != first && !ContainsIdentical(ordered, gpus[i]))
                {
                    ordered.Add(gpus[i].WithRole(HardwareGpuRole.Other));
                }
            }

            return ordered;
        }

        /// <summary>Checks whether a list already contains a GPU with the same PCI identity and name.</summary>
        /// <param name="gpus">The GPUs selected so far.</param>
        /// <param name="gpu">The candidate GPU.</param>
        /// <returns>True when <paramref name="gpu"/> has a known PCI identity that an earlier GPU repeats exactly.</returns>
        private static bool ContainsIdentical(List<HardwareGpu> gpus, HardwareGpu gpu)
        {
            if (gpu.VendorId == 0)
            {
                return false;
            }

            foreach (var existing in gpus)
            {
                if (existing.VendorId == gpu.VendorId && existing.DeviceId == gpu.DeviceId
                    && string.Equals(existing.Name, gpu.Name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Applies a fallback route to an ordered GPU list.</summary>
        /// <param name="ordered">The GPUs with Unity's renderer or its default stand-in first.</param>
        /// <param name="route">Whether GPUs other than the first remain in the list.</param>
        /// <returns>The complete list for <see cref="HardwareFallbackRoute.OtherGpus"/>; otherwise only its first GPU.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="ordered"/> is null.</exception>
        internal static IReadOnlyList<HardwareGpu> Restrict(IReadOnlyList<HardwareGpu> ordered, HardwareFallbackRoute route)
        {
            if (ordered == null)
            {
                throw new ArgumentNullException(nameof(ordered));
            }

            return route == HardwareFallbackRoute.OtherGpus || ordered.Count <= 1 ? ordered : new[] { ordered[0] };
        }

        /// <summary>Enumerates the system's GPUs in the order FFmpeg's default device search uses.</summary>
        /// <returns>The hardware GPUs, or an empty list on platforms without GPU selection.</returns>
        /// <exception cref="DllNotFoundException">The platform graphics library is unavailable.</exception>
        /// <exception cref="InvalidOperationException">The platform reports an enumeration failure.</exception>
        /// <exception cref="IOException">The DRM device directory cannot be read.</exception>
        /// <exception cref="UnauthorizedAccessException">Reading the DRM device directory was denied.</exception>
        private static IReadOnlyList<HardwareGpu> Enumerate()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            return DxgiAdapterQuery.GetAdapters(false);
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
            return DrmRenderNodeQuery.GetRenderNodes();
#else
            return Array.Empty<HardwareGpu>();
#endif
        }
    }
}
