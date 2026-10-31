#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MajdataPlay.IO.Storage;

namespace MajdataPlay.FFmpeg.Interop
{
    /// <summary>Enumerates Linux DRM render nodes, which select the GPU of an FFmpeg VAAPI device.</summary>
    /// <remarks>Each node's PCI identity is read from its sysfs <c>uevent</c> file. Nodes are sorted like FFmpeg's default VAAPI device search.</remarks>
    internal static class DrmRenderNodeQuery
    {
        /// <summary>Is the sysfs directory that lists DRM devices.</summary>
        private const string ClassDirectory = "/sys/class/drm";
        /// <summary>Is the name prefix of DRM render nodes.</summary>
        private const string RenderNodePrefix = "renderD";

        /// <summary>Lists the DRM render nodes of hardware GPUs in render-node order.</summary>
        /// <returns>The GPUs, each with its <c>/dev/dri</c> render node path as the FFmpeg VAAPI device string.</returns>
        /// <exception cref="IOException">The sysfs DRM directory cannot be read.</exception>
        /// <exception cref="UnauthorizedAccessException">Reading the sysfs DRM directory was denied.</exception>
        internal static IReadOnlyList<HardwareGpu> GetRenderNodes()
        {
            var nodes = new List<KeyValuePair<int, HardwareGpu>>();
            foreach (var entry in FileSystem.Directory.Open(ClassDirectory).EnumerateEntries())
            {
                if (!TryGetRenderMinor(entry.Name, out var minor))
                {
                    continue;
                }

                var gpu = ParseRenderNode(entry.Name, ReadUevent(entry.Name));
                if (gpu != null)
                {
                    nodes.Add(new KeyValuePair<int, HardwareGpu>(minor, gpu));
                }
            }

            nodes.Sort((left, right) => left.Key.CompareTo(right.Key));
            var gpus = new List<HardwareGpu>(nodes.Count);
            foreach (var node in nodes)
            {
                gpus.Add(node.Value);
            }

            return gpus;
        }

        /// <summary>Parses the render node number from a DRM device name.</summary>
        /// <param name="name">A DRM device name such as <c>renderD128</c>.</param>
        /// <param name="minor">Receives the render node number.</param>
        /// <returns>True when <paramref name="name"/> names a render node.</returns>
        internal static bool TryGetRenderMinor(string name, out int minor)
        {
            minor = 0;
            return name.StartsWith(RenderNodePrefix, StringComparison.Ordinal)
                && int.TryParse(name.Substring(RenderNodePrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out minor);
        }

        /// <summary>Describes one render node from its sysfs <c>uevent</c> text.</summary>
        /// <param name="name">The render node name such as <c>renderD128</c>.</param>
        /// <param name="uevent">The node's <c>device/uevent</c> text, or null when it could not be read.</param>
        /// <returns>The GPU, or null for the virtual <c>vgem</c> driver, which FFmpeg's device search also skips.</returns>
        internal static HardwareGpu? ParseRenderNode(string name, string? uevent)
        {
            string? driver = null;
            string? busLocation = null;
            int vendorId = 0, deviceId = 0;
            foreach (var line in (uevent ?? string.Empty).Split('\n'))
            {
                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = line.Substring(0, separator);
                var value = line.Substring(separator + 1).Trim();
                if (key == "DRIVER")
                {
                    driver = value;
                }
                else if (key == "PCI_SLOT_NAME")
                {
                    busLocation = value;
                }
                else if (key == "PCI_ID")
                {
                    // PCI_ID is VENDOR:DEVICE in hexadecimal, for example 10DE:2504.
                    var ids = value.Split(':');
                    if (ids.Length != 2
                        || !int.TryParse(ids[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vendorId)
                        || !int.TryParse(ids[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out deviceId))
                    {
                        vendorId = 0;
                        deviceId = 0;
                    }
                }
            }

            if (driver == "vgem")
            {
                return null;
            }

            var description = driver == null ? name : driver + " " + name;
            return new HardwareGpu("/dev/dri/" + name, description, vendorId, deviceId, busLocation);
        }

        /// <summary>Reads a render node's sysfs <c>device/uevent</c> file.</summary>
        /// <param name="name">The render node name.</param>
        /// <returns>The file text, or null when the node exposes no readable device information.</returns>
        private static string? ReadUevent(string name)
        {
            try
            {
                using var stream = FileSystem.File.OpenRead(ClassDirectory + "/" + name + "/device/uevent");
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is NotSupportedException)
            {
                return null;
            }
        }
    }
}
