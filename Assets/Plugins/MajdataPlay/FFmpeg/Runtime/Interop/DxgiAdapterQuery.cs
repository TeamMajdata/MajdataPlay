#nullable enable
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace MajdataPlay.FFmpeg.Interop
{
    /// <summary>Enumerates DXGI adapters with the indices that FFmpeg's D3D11VA device strings select.</summary>
    /// <remarks>
    /// FFmpeg opens a D3D11VA device string as an <c>IDXGIFactory::EnumAdapters</c> index, and every factory in a process
    /// enumerates adapters in the same order. COM methods are called through their vtable slots because the bridge does not
    /// expose adapter enumeration. Delegates are created per call; enumeration only runs while a codec opens.
    /// </remarks>
    internal static unsafe class DxgiAdapterQuery
    {
        /// <summary>Is returned by <c>EnumAdapters1</c> after the last adapter.</summary>
        private const int NotFound = unchecked((int)0x887A0002);
        /// <summary>Marks the Microsoft Basic Render Driver and other software adapters, which have no video codecs.</summary>
        private const uint SoftwareAdapterFlag = 2;
        /// <summary>Is the vtable slot of <c>IUnknown::Release</c>.</summary>
        private const int ReleaseSlot = 2;
        /// <summary>Is the vtable slot of <c>IDXGIFactory1::EnumAdapters1</c>.</summary>
        private const int EnumAdapters1Slot = 12;
        /// <summary>Is the vtable slot of <c>IDXGIAdapter1::GetDesc1</c>.</summary>
        private const int GetDesc1Slot = 10;
        /// <summary>Identifies the <c>IDXGIFactory1</c> interface.</summary>
        private static readonly Guid s_factory1 = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");

        /// <summary>Calls <c>IDXGIFactory1::EnumAdapters1</c>.</summary>
        /// <param name="factory">The factory instance.</param>
        /// <param name="index">The zero-based adapter index.</param>
        /// <param name="adapter">Receives an owned adapter reference.</param>
        /// <returns>An HRESULT; <see cref="NotFound"/> after the last adapter.</returns>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int EnumAdapters1Method(IntPtr factory, uint index, IntPtr* adapter);

        /// <summary>Calls <c>IDXGIAdapter1::GetDesc1</c>.</summary>
        /// <param name="adapter">The adapter instance.</param>
        /// <param name="description">Receives the adapter description.</param>
        /// <returns>An HRESULT.</returns>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDesc1Method(IntPtr adapter, AdapterDescription* description);

        /// <summary>Calls <c>IUnknown::Release</c>.</summary>
        /// <param name="instance">The COM instance to release.</param>
        /// <returns>The remaining reference count.</returns>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint ReleaseMethod(IntPtr instance);

        /// <summary>Lists DXGI adapters in enumeration order.</summary>
        /// <param name="includeSoftwareAdapters">Whether to include software adapters such as the Microsoft Basic Render Driver.</param>
        /// <returns>The adapters, each with its DXGI index as the FFmpeg D3D11VA device string.</returns>
        /// <exception cref="DllNotFoundException">DXGI is unavailable.</exception>
        /// <exception cref="InvalidOperationException">DXGI cannot create a factory or enumerate an adapter.</exception>
        internal static IReadOnlyList<HardwareGpu> GetAdapters(bool includeSoftwareAdapters)
        {
            var factoryId = s_factory1;
            IntPtr factory;
            Check(CreateDXGIFactory1(&factoryId, &factory), "CreateDXGIFactory1");
            try
            {
                var enumerate = GetMethod<EnumAdapters1Method>(factory, EnumAdapters1Slot);
                var adapters = new List<HardwareGpu>();
                for (uint index = 0; ; index++)
                {
                    IntPtr adapter;
                    var result = enumerate(factory, index, &adapter);
                    if (result == NotFound)
                    {
                        return adapters;
                    }

                    Check(result, "IDXGIFactory1::EnumAdapters1");
                    try
                    {
                        AdapterDescription description;
                        Check(GetMethod<GetDesc1Method>(adapter, GetDesc1Slot)(adapter, &description), "IDXGIAdapter1::GetDesc1");
                        if (includeSoftwareAdapters || (description.Flags & SoftwareAdapterFlag) == 0)
                        {
                            var name = ReadName(description.Description, AdapterDescription.DescriptionLength);
                            adapters.Add(new HardwareGpu(index.ToString(CultureInfo.InvariantCulture),
                                string.IsNullOrWhiteSpace(name) ? "DXGI adapter " + index.ToString(CultureInfo.InvariantCulture) : name,
                                (int)description.VendorId, (int)description.DeviceId, null,
                                ((long)description.LuidHighPart << 32) | description.LuidLowPart));
                        }
                    }
                    finally
                    {
                        Release(adapter);
                    }
                }
            }
            finally
            {
                Release(factory);
            }
        }

        /// <summary>Reads a fixed-length, optionally NUL-terminated UTF-16 adapter name.</summary>
        /// <param name="characters">The first UTF-16 code unit of the fixed buffer.</param>
        /// <param name="length">The buffer length in code units.</param>
        /// <returns>The trimmed adapter name.</returns>
        private static string ReadName(ushort* characters, int length)
        {
            var count = 0;
            while (count < length && characters[count] != 0)
            {
                count++;
            }

            return new string((char*)characters, 0, count).Trim();
        }

        /// <summary>Creates a delegate for one method in a COM instance's vtable.</summary>
        /// <typeparam name="T">The unmanaged delegate type of the method.</typeparam>
        /// <param name="instance">The COM instance.</param>
        /// <param name="slot">The zero-based vtable slot.</param>
        /// <returns>A delegate that calls the method.</returns>
        private static T GetMethod<T>(IntPtr instance, int slot) where T : Delegate
        {
            var vtable = *(IntPtr**)instance;
            return Marshal.GetDelegateForFunctionPointer<T>(vtable[slot]);
        }

        /// <summary>Releases one COM reference.</summary>
        /// <param name="instance">The COM instance to release; zero is ignored.</param>
        private static void Release(IntPtr instance)
        {
            if (instance != IntPtr.Zero)
            {
                GetMethod<ReleaseMethod>(instance, ReleaseSlot)(instance);
            }
        }

        /// <summary>Converts a failed HRESULT to an exception.</summary>
        /// <param name="result">The HRESULT to check.</param>
        /// <param name="operation">The DXGI call that returned it.</param>
        /// <exception cref="InvalidOperationException"><paramref name="result"/> indicates failure.</exception>
        private static void Check(int result, string operation)
        {
            if (result < 0)
            {
                throw new InvalidOperationException(operation + " failed (HRESULT 0x" + result.ToString("X8", CultureInfo.InvariantCulture) + ").");
            }
        }

        /// <summary>Creates a DXGI 1.1 factory.</summary>
        /// <param name="interfaceId">The requested factory interface.</param>
        /// <param name="factory">Receives an owned factory reference.</param>
        /// <returns>An HRESULT.</returns>
        [DllImport("dxgi.dll", ExactSpelling = true)]
        private static extern int CreateDXGIFactory1(Guid* interfaceId, IntPtr* factory);

        /// <summary>Mirrors <c>DXGI_ADAPTER_DESC1</c>, whose memory sizes are pointer-sized <c>SIZE_T</c> values.</summary>
        /// <remarks>The name uses <see cref="ushort"/> code units because a <see cref="char"/> buffer would make the struct non-blittable for pointer marshaling.</remarks>
        [StructLayout(LayoutKind.Sequential)]
        private struct AdapterDescription
        {
            /// <summary>Is the length of <see cref="Description"/> in UTF-16 code units.</summary>
            internal const int DescriptionLength = 128;
            /// <summary>Stores the adapter name as UTF-16 code units.</summary>
            internal fixed ushort Description[DescriptionLength];
            /// <summary>Stores the PCI vendor ID.</summary>
            internal uint VendorId;
            /// <summary>Stores the PCI device ID.</summary>
            internal uint DeviceId;
            /// <summary>Stores the PCI subsystem ID.</summary>
            internal uint SubsystemId;
            /// <summary>Stores the PCI revision.</summary>
            internal uint Revision;
            /// <summary>Stores the dedicated video memory size in bytes.</summary>
            internal UIntPtr DedicatedVideoMemory;
            /// <summary>Stores the dedicated system memory size in bytes.</summary>
            internal UIntPtr DedicatedSystemMemory;
            /// <summary>Stores the shared system memory size in bytes.</summary>
            internal UIntPtr SharedSystemMemory;
            /// <summary>Stores the low part of the adapter LUID.</summary>
            internal uint LuidLowPart;
            /// <summary>Stores the high part of the adapter LUID.</summary>
            internal int LuidHighPart;
            /// <summary>Stores <c>DXGI_ADAPTER_FLAG</c> values.</summary>
            internal uint Flags;
        }
    }
}
#endif
