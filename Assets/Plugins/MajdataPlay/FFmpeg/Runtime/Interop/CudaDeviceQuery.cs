#nullable enable
using System;
using System.Globalization;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
using System.Runtime.InteropServices;
#endif
#if !(UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN) && (UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX)
using System.Text;
#endif

namespace MajdataPlay.FFmpeg.Interop
{
    /// <summary>Maps a GPU to the CUDA device ordinal that NVENC's <c>gpu</c> option selects.</summary>
    /// <remarks>
    /// CUDA orders devices fastest first by default, so neither DXGI nor render-node order can be used as an ordinal.
    /// Windows matches the adapter LUID and Linux matches the PCI bus location.
    /// </remarks>
    internal static unsafe class CudaDeviceQuery
    {
        /// <summary>Finds the CUDA ordinal of a GPU.</summary>
        /// <param name="gpu">The GPU, with its LUID on Windows or its PCI bus location on Linux.</param>
        /// <param name="ordinal">Receives the CUDA device ordinal, or -1 when no device matches.</param>
        /// <returns>True when a CUDA device has the same LUID or PCI bus location.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="gpu"/> is null.</exception>
        /// <exception cref="NotSupportedException">The CUDA driver is unavailable or fails to enumerate devices.</exception>
        internal static bool TryGetOrdinal(HardwareGpu gpu, out int ordinal)
        {
            if (gpu == null)
            {
                throw new ArgumentNullException(nameof(gpu));
            }

            ordinal = -1;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
            try
            {
                Check(Native.CuInit(0), "cuInit");
                int count;
                Check(Native.CuDeviceGetCount(&count), "cuDeviceGetCount");
                for (var i = 0; i < count; i++)
                {
                    int device;
                    Check(Native.CuDeviceGet(&device, i), "cuDeviceGet");
                    if (Matches(gpu, device))
                    {
                        ordinal = i;
                        return true;
                    }
                }

                return false;
            }
            catch (Exception error) when (error is DllNotFoundException || error is EntryPointNotFoundException)
            {
                throw new NotSupportedException("The CUDA driver library is unavailable: " + error.Message, error);
            }
#else
            return false;
#endif
        }

        /// <summary>Compares two PCI bus locations numerically, so <c>0000:01:00.0</c> equals CUDA's <c>00000000:01:00.0</c>.</summary>
        /// <param name="left">The first location in <c>domain:bus:device.function</c> hexadecimal form.</param>
        /// <param name="right">The second location in the same form.</param>
        /// <returns>True when both locations parse and name the same PCI function.</returns>
        internal static bool IsSameBusLocation(string? left, string? right)
        {
            return TryParseBusLocation(left, out var first) && TryParseBusLocation(right, out var second) && first == second;
        }

        /// <summary>Parses a PCI bus location into one comparable number.</summary>
        /// <param name="text">The location in <c>domain:bus:device.function</c> hexadecimal form.</param>
        /// <param name="value">Receives the domain, bus, device, and function packed into one value.</param>
        /// <returns>True when <paramref name="text"/> is a valid bus location.</returns>
        private static bool TryParseBusLocation(string? text, out long value)
        {
            value = 0;
            var parts = text?.Trim().Split(':', '.');
            if (parts == null || parts.Length != 4
                || !uint.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var domain)
                || !byte.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bus)
                || !byte.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var device)
                || !byte.TryParse(parts[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var function)
                || device > 0x1F || function > 7)
            {
                return false;
            }

            value = ((long)domain << 16) | ((long)bus << 8) | ((long)device << 3) | function;
            return true;
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
        /// <summary>Checks whether a CUDA device is the described GPU.</summary>
        /// <param name="gpu">The GPU to find.</param>
        /// <param name="device">The CUDA device handle.</param>
        /// <returns>True when the device has the GPU's LUID on Windows or its PCI bus location on Linux.</returns>
        /// <exception cref="NotSupportedException">The CUDA driver cannot describe the device.</exception>
        private static bool Matches(HardwareGpu gpu, int device)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (gpu.Luid == 0)
            {
                return false;
            }

            // The 8-byte LUID is a little-endian LowPart DWORD followed by a HighPart LONG.
            long luid;
            uint nodeMask;
            Check(Native.CuDeviceGetLuid((byte*)&luid, &nodeMask, device), "cuDeviceGetLuid");
            return luid == gpu.Luid;
#else
            var text = stackalloc byte[32];
            Check(Native.CuDeviceGetPCIBusId(text, 32, device), "cuDeviceGetPCIBusId");
            var length = 0;
            while (length < 32 && text[length] != 0)
            {
                length++;
            }

            return IsSameBusLocation(Encoding.ASCII.GetString(text, length), gpu.BusLocation);
#endif
        }

        /// <summary>Converts a failed CUDA driver result to an exception.</summary>
        /// <param name="result">The CUresult to check.</param>
        /// <param name="operation">The CUDA call that returned it.</param>
        /// <exception cref="NotSupportedException"><paramref name="result"/> indicates failure.</exception>
        private static void Check(int result, string operation)
        {
            if (result != 0)
            {
                throw new NotSupportedException(operation + " failed (CUresult " + result.ToString(CultureInfo.InvariantCulture) + ").");
            }
        }

        /// <summary>Declares the CUDA driver API calls used to identify devices.</summary>
        /// <remarks>The driver API uses <c>__stdcall</c> on Windows and the C calling convention on Linux.</remarks>
        private static class Native
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            /// <summary>Names the CUDA driver library that NVIDIA's Windows driver installs.</summary>
            private const string Library = "nvcuda.dll";
            /// <summary>Is the calling convention of the Windows driver API.</summary>
            private const CallingConvention Convention = CallingConvention.StdCall;
#else
            /// <summary>Names the CUDA driver library that NVIDIA's Linux driver installs.</summary>
            private const string Library = "libcuda.so.1";
            /// <summary>Is the calling convention of the Linux driver API.</summary>
            private const CallingConvention Convention = CallingConvention.Cdecl;
#endif

            /// <summary>Initializes the CUDA driver API; repeated calls succeed.</summary>
            /// <param name="flags">Must be zero.</param>
            /// <returns>A CUresult.</returns>
            [DllImport(Library, CallingConvention = Convention, EntryPoint = "cuInit")]
            internal static extern int CuInit(uint flags);

            /// <summary>Counts CUDA devices.</summary>
            /// <param name="count">Receives the device count.</param>
            /// <returns>A CUresult.</returns>
            [DllImport(Library, CallingConvention = Convention, EntryPoint = "cuDeviceGetCount")]
            internal static extern int CuDeviceGetCount(int* count);

            /// <summary>Gets the device handle of a CUDA ordinal.</summary>
            /// <param name="device">Receives the device handle.</param>
            /// <param name="ordinal">The zero-based CUDA ordinal.</param>
            /// <returns>A CUresult.</returns>
            [DllImport(Library, CallingConvention = Convention, EntryPoint = "cuDeviceGet")]
            internal static extern int CuDeviceGet(int* device, int ordinal);

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            /// <summary>Gets a device's 8-byte adapter LUID and node mask.</summary>
            /// <param name="luid">Receives the LUID bytes.</param>
            /// <param name="deviceNodeMask">Receives the device node mask.</param>
            /// <param name="device">The device handle.</param>
            /// <returns>A CUresult.</returns>
            [DllImport(Library, CallingConvention = Convention, EntryPoint = "cuDeviceGetLuid")]
            internal static extern int CuDeviceGetLuid(byte* luid, uint* deviceNodeMask, int device);
#else
            /// <summary>Writes a device's PCI bus location as a NUL-terminated ASCII string.</summary>
            /// <param name="busId">Receives the bus location.</param>
            /// <param name="length">The capacity of <paramref name="busId"/> in bytes.</param>
            /// <param name="device">The device handle.</param>
            /// <returns>A CUresult.</returns>
            [DllImport(Library, CallingConvention = Convention, EntryPoint = "cuDeviceGetPCIBusId")]
            internal static extern int CuDeviceGetPCIBusId(byte* busId, int length, int device);
#endif
        }
#endif
    }
}
