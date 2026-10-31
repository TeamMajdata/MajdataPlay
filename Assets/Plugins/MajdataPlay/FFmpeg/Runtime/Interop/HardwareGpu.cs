#nullable enable
using System;
using System.Globalization;

namespace MajdataPlay.FFmpeg.Interop
{
    /// <summary>Describes the relationship between a hardware codec GPU and Unity's renderer.</summary>
    internal enum HardwareGpuRole
    {
        /// <summary>Unity renders on this GPU.</summary>
        Renderer,
        /// <summary>The system default GPU, which stands in for a renderer that could not be identified.</summary>
        Default,
        /// <summary>Another GPU that Unity does not render on.</summary>
        Other
    }

    /// <summary>Describes one GPU that FFmpeg can open as an explicit hardware device.</summary>
    internal sealed class HardwareGpu
    {
        /// <summary>Gets the PCI vendor ID of NVIDIA GPUs.</summary>
        internal const int NvidiaVendorId = 0x10DE;
        /// <summary>Gets the PCI vendor ID of AMD GPUs.</summary>
        internal const int AmdVendorId = 0x1002;

        /// <summary>Initializes a GPU description that is not yet related to Unity's renderer.</summary>
        /// <param name="device">The FFmpeg device string: a DXGI adapter index on Windows or a DRM render node path on Linux.</param>
        /// <param name="name">The adapter description or driver name used in diagnostics.</param>
        /// <param name="vendorId">The PCI vendor ID, or zero when unknown.</param>
        /// <param name="deviceId">The PCI device ID, or zero when unknown.</param>
        /// <param name="busLocation">The PCI bus location such as <c>0000:01:00.0</c>, or null when unknown.</param>
        /// <param name="luid">The Windows adapter LUID with its high part in the upper 32 bits, or zero when unknown.</param>
        /// <exception cref="ArgumentException"><paramref name="device"/> or <paramref name="name"/> is null, empty, or whitespace.</exception>
        internal HardwareGpu(string device, string name, int vendorId, int deviceId, string? busLocation = null, long luid = 0)
            : this(device, name, vendorId, deviceId, busLocation, luid, HardwareGpuRole.Other)
        {
        }

        /// <summary>Initializes a GPU description with its relationship to Unity's renderer.</summary>
        /// <param name="device">The FFmpeg device string: a DXGI adapter index on Windows or a DRM render node path on Linux.</param>
        /// <param name="name">The adapter description or driver name used in diagnostics.</param>
        /// <param name="vendorId">The PCI vendor ID, or zero when unknown.</param>
        /// <param name="deviceId">The PCI device ID, or zero when unknown.</param>
        /// <param name="busLocation">The PCI bus location such as <c>0000:01:00.0</c>, or null when unknown.</param>
        /// <param name="luid">The Windows adapter LUID with its high part in the upper 32 bits, or zero when unknown.</param>
        /// <param name="role">The relationship between this GPU and Unity's renderer.</param>
        /// <exception cref="ArgumentException"><paramref name="device"/> or <paramref name="name"/> is null, empty, or whitespace.</exception>
        private HardwareGpu(string device, string name, int vendorId, int deviceId, string? busLocation, long luid, HardwareGpuRole role)
        {
            if (string.IsNullOrWhiteSpace(device))
            {
                throw new ArgumentException("A hardware device string is required.", nameof(device));
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A GPU name is required.", nameof(name));
            }

            Device = device;
            Name = name;
            VendorId = vendorId;
            DeviceId = deviceId;
            BusLocation = busLocation;
            Luid = luid;
            Role = role;
        }

        /// <summary>Gets the FFmpeg device string: a DXGI adapter index on Windows or a DRM render node path on Linux.</summary>
        public string Device { get; }
        /// <summary>Gets the adapter description or driver name used in diagnostics.</summary>
        public string Name { get; }
        /// <summary>Gets the PCI vendor ID, or zero when unknown.</summary>
        public int VendorId { get; }
        /// <summary>Gets the PCI device ID, or zero when unknown.</summary>
        public int DeviceId { get; }
        /// <summary>Gets the PCI bus location such as <c>0000:01:00.0</c>, or null when unknown.</summary>
        public string? BusLocation { get; }
        /// <summary>Gets the Windows adapter LUID with its high part in the upper 32 bits, or zero when unknown.</summary>
        public long Luid { get; }
        /// <summary>Gets the relationship between this GPU and Unity's renderer.</summary>
        public HardwareGpuRole Role { get; }

        /// <summary>Creates a copy of this description with another relationship to Unity's renderer.</summary>
        /// <param name="role">The relationship to assign.</param>
        /// <returns>A description of the same GPU with the requested role.</returns>
        internal HardwareGpu WithRole(HardwareGpuRole role) => role == Role ? this : new HardwareGpu(Device, Name, VendorId, DeviceId, BusLocation, Luid, role);

        /// <summary>Describes the GPU, its FFmpeg device string, PCI identity, and relationship to Unity's renderer.</summary>
        /// <returns>A diagnostic description such as <c>NVIDIA GeForce RTX 3060 (device 0, PCI 10DE:2504, Unity renderer GPU)</c>.</returns>
        public override string ToString()
        {
            var role = Role switch
            {
                HardwareGpuRole.Renderer => "Unity renderer GPU",
                HardwareGpuRole.Default => "system default GPU; Unity renderer not identified",
                _ => "other GPU"
            };
            var identity = VendorId != 0
                ? ", PCI " + VendorId.ToString("X4", CultureInfo.InvariantCulture) + ":" + DeviceId.ToString("X4", CultureInfo.InvariantCulture)
                : string.Empty;
            return Name + " (device " + Device + identity + ", " + role + ")";
        }
    }
}
