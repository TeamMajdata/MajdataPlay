using System;
using System.Collections.Generic;
using MajdataPlay.FFmpeg;
using MajdataPlay.FFmpeg.Interop;
using NUnit.Framework;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System.Globalization;
using UnityEngine;
#endif

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Verifies how hardware codecs order GPUs, apply fallback routes, and discover the system's GPUs.</summary>
    public class HardwareGpuCatalogTests
    {
        /// <summary>The PCI vendor ID of Intel GPUs.</summary>
        private const int IntelVendorId = 0x8086;

        /// <summary>Creates an integrated Intel, a discrete NVIDIA, and a discrete AMD GPU in system order.</summary>
        /// <returns>Three GPUs with distinct identities.</returns>
        private static List<HardwareGpu> ThreeGpus()
        {
            return new List<HardwareGpu>
            {
                new HardwareGpu("0", "Intel Iris Xe", IntelVendorId, 0x46A6),
                new HardwareGpu("1", "NVIDIA GeForce RTX 3060", HardwareGpu.NvidiaVendorId, 0x2504),
                new HardwareGpu("2", "AMD Radeon RX 6700 XT", HardwareGpu.AmdVendorId, 0x73DF)
            };
        }

        /// <summary>Checks that Unity's renderer moves to the front and the remaining GPUs keep their system order.</summary>
        [Test]
        public void Order_MovesRendererFirstAndKeepsSystemOrder()
        {
            var ordered = HardwareGpuCatalog.Order(ThreeGpus(), HardwareGpu.NvidiaVendorId, 0x2504);

            Assert.That(ordered.Count, Is.EqualTo(3));
            Assert.That(ordered[0].Device, Is.EqualTo("1"));
            Assert.That(ordered[0].Role, Is.EqualTo(HardwareGpuRole.Renderer));
            Assert.That(ordered[1].Device, Is.EqualTo("0"));
            Assert.That(ordered[1].Role, Is.EqualTo(HardwareGpuRole.Other));
            Assert.That(ordered[2].Device, Is.EqualTo("2"));
            Assert.That(ordered[2].Role, Is.EqualTo(HardwareGpuRole.Other));
        }

        /// <summary>Checks that the first GPU stands in for an unknown or unlisted renderer.</summary>
        /// <param name="vendorId">The renderer's PCI vendor ID.</param>
        /// <param name="deviceId">The renderer's PCI device ID.</param>
        [TestCase(0, 0)]
        [TestCase(0x1234, 0x5678)]
        public void Order_UnidentifiedRenderer_UsesFirstGpuAsDefault(int vendorId, int deviceId)
        {
            var ordered = HardwareGpuCatalog.Order(ThreeGpus(), vendorId, deviceId);

            Assert.That(ordered[0].Device, Is.EqualTo("0"));
            Assert.That(ordered[0].Role, Is.EqualTo(HardwareGpuRole.Default));
            Assert.That(ordered[1].Role, Is.EqualTo(HardwareGpuRole.Other));
            Assert.That(ordered[0].ToString(), Does.Contain("Unity renderer not identified"));
        }

        /// <summary>Checks that repeated identical GPUs, such as virtual display adapters, are listed once.</summary>
        [Test]
        public void Order_DropsRepeatedIdenticalGpus()
        {
            var gpus = new List<HardwareGpu>
            {
                new HardwareGpu("0", "NVIDIA GeForce RTX 3060", HardwareGpu.NvidiaVendorId, 0x2504),
                new HardwareGpu("1", "NVIDIA GeForce RTX 3060", HardwareGpu.NvidiaVendorId, 0x2504),
                new HardwareGpu("2", "Intel Iris Xe", IntelVendorId, 0x46A6),
                new HardwareGpu("/dev/dri/renderD130", "renderD130", 0, 0),
                new HardwareGpu("/dev/dri/renderD131", "renderD130", 0, 0)
            };

            var ordered = HardwareGpuCatalog.Order(gpus, IntelVendorId, 0x46A6);

            Assert.That(ordered.Count, Is.EqualTo(4));
            Assert.That(ordered[0].Device, Is.EqualTo("2"));
            Assert.That(ordered[1].Device, Is.EqualTo("0"));
            // Without a PCI identity, equal names do not prove that two devices are the same GPU.
            Assert.That(ordered[2].Device, Is.EqualTo("/dev/dri/renderD130"));
            Assert.That(ordered[3].Device, Is.EqualTo("/dev/dri/renderD131"));
        }

        /// <summary>Checks that ordering an empty list returns an empty list and a null list is rejected.</summary>
        [Test]
        public void Order_EmptyOrNull()
        {
            Assert.That(HardwareGpuCatalog.Order(Array.Empty<HardwareGpu>(), 0, 0), Is.Empty);
            Assert.Throws<ArgumentNullException>(() => HardwareGpuCatalog.Order(null!, 0, 0));
            Assert.Throws<ArgumentNullException>(() => HardwareGpuCatalog.Restrict(null!, HardwareFallbackRoute.OtherGpus));
        }

        /// <summary>Checks that the software codec route keeps only the renderer, while the other-GPU route keeps every GPU.</summary>
        [Test]
        public void Restrict_AppliesFallbackRoute()
        {
            var ordered = HardwareGpuCatalog.Order(ThreeGpus(), HardwareGpu.AmdVendorId, 0x73DF);

            var software = HardwareGpuCatalog.Restrict(ordered, HardwareFallbackRoute.SoftwareCodec);
            var others = HardwareGpuCatalog.Restrict(ordered, HardwareFallbackRoute.OtherGpus);

            Assert.That(software.Count, Is.EqualTo(1));
            Assert.That(software[0].Device, Is.EqualTo("2"));
            Assert.That(others, Is.SameAs(ordered));
            Assert.That(HardwareGpuCatalog.Restrict(Array.Empty<HardwareGpu>(), HardwareFallbackRoute.SoftwareCodec), Is.Empty);
        }

        /// <summary>Checks the diagnostic description and role copies of a GPU.</summary>
        [Test]
        public void HardwareGpu_DescribesIdentityAndRole()
        {
            var gpu = new HardwareGpu("1", "NVIDIA GeForce RTX 3060", HardwareGpu.NvidiaVendorId, 0x2504, "0000:01:00.0", 0x1_0000C055L);

            var renderer = gpu.WithRole(HardwareGpuRole.Renderer);

            Assert.That(gpu.Role, Is.EqualTo(HardwareGpuRole.Other));
            Assert.That(gpu.WithRole(HardwareGpuRole.Other), Is.SameAs(gpu));
            Assert.That(renderer.Device, Is.EqualTo("1"));
            Assert.That(renderer.BusLocation, Is.EqualTo("0000:01:00.0"));
            Assert.That(renderer.Luid, Is.EqualTo(0x1_0000C055L));
            Assert.That(renderer.ToString(), Is.EqualTo("NVIDIA GeForce RTX 3060 (device 1, PCI 10DE:2504, Unity renderer GPU)"));
            Assert.That(gpu.ToString(), Does.EndWith("other GPU)"));
            Assert.That(new HardwareGpu("/dev/dri/renderD128", "renderD128", 0, 0).ToString(), Is.EqualTo("renderD128 (device /dev/dri/renderD128, other GPU)"));
        }

        /// <summary>Checks that a GPU requires a device string and a name.</summary>
        /// <param name="device">The device string.</param>
        /// <param name="name">The GPU name.</param>
        [TestCase("", "GPU")]
        [TestCase(" ", "GPU")]
        [TestCase("0", "")]
        public void HardwareGpu_RejectsMissingDeviceOrName(string device, string name)
        {
            Assert.Throws<ArgumentException>(() => new HardwareGpu(device, name, 0, 0));
        }

        /// <summary>Checks the render node number parser.</summary>
        /// <param name="name">The DRM device name.</param>
        /// <param name="expected">The expected render node number, or -1 when the name is not a render node.</param>
        [TestCase("renderD128", 128)]
        [TestCase("renderD129", 129)]
        [TestCase("card0", -1)]
        [TestCase("renderD", -1)]
        [TestCase("renderD12x", -1)]
        [TestCase("renderD-1", -1)]
        public void DrmRenderNode_ParsesRenderNodeNumbers(string name, int expected)
        {
            var parsed = DrmRenderNodeQuery.TryGetRenderMinor(name, out var minor);

            Assert.That(parsed, Is.EqualTo(expected >= 0));
            if (parsed)
            {
                Assert.That(minor, Is.EqualTo(expected));
            }
        }

        /// <summary>Checks that a render node's sysfs uevent provides its driver, PCI identity, and bus location.</summary>
        [Test]
        public void DrmRenderNode_ReadsPciIdentity()
        {
            var gpu = DrmRenderNodeQuery.ParseRenderNode("renderD129", "DRIVER=amdgpu\nPCI_CLASS=30000\nPCI_ID=1002:73DF\nPCI_SUBSYS_ID=1458:2408\nPCI_SLOT_NAME=0000:03:00.0\n");

            Assert.That(gpu, Is.Not.Null);
            Assert.That(gpu!.Device, Is.EqualTo("/dev/dri/renderD129"));
            Assert.That(gpu.Name, Is.EqualTo("amdgpu renderD129"));
            Assert.That(gpu.VendorId, Is.EqualTo(HardwareGpu.AmdVendorId));
            Assert.That(gpu.DeviceId, Is.EqualTo(0x73DF));
            Assert.That(gpu.BusLocation, Is.EqualTo("0000:03:00.0"));
        }

        /// <summary>Checks that the virtual vgem driver is skipped and that missing or malformed device information is tolerated.</summary>
        [Test]
        public void DrmRenderNode_SkipsVgemAndToleratesMissingInformation()
        {
            Assert.That(DrmRenderNodeQuery.ParseRenderNode("renderD130", "DRIVER=vgem\n"), Is.Null);

            var unknown = DrmRenderNodeQuery.ParseRenderNode("renderD131", null);
            var malformed = DrmRenderNodeQuery.ParseRenderNode("renderD132", "DRIVER=virtio_gpu\r\nPCI_ID=XYZ\r\n");

            Assert.That(unknown!.Name, Is.EqualTo("renderD131"));
            Assert.That(unknown.VendorId, Is.EqualTo(0));
            Assert.That(unknown.BusLocation, Is.Null);
            Assert.That(malformed!.Name, Is.EqualTo("virtio_gpu renderD132"));
            Assert.That(malformed.VendorId, Is.EqualTo(0));
            Assert.That(malformed.DeviceId, Is.EqualTo(0));
        }

        /// <summary>Checks the numeric PCI bus location comparison used to find CUDA ordinals.</summary>
        /// <param name="left">The first location.</param>
        /// <param name="right">The second location.</param>
        /// <param name="expected">Whether both name the same PCI function.</param>
        [TestCase("0000:01:00.0", "00000000:01:00.0", true)]
        [TestCase("0000:0a:1f.7", "0000:0A:1F.7", true)]
        [TestCase("0000:01:00.0", "0000:02:00.0", false)]
        [TestCase("0000:01:00.0", "0001:01:00.0", false)]
        [TestCase("0000:01:20.0", "0000:01:20.0", false)]
        [TestCase("0000:01:00.8", "0000:01:00.8", false)]
        [TestCase("01:00.0", "01:00.0", false)]
        [TestCase(null, "0000:01:00.0", false)]
        public void CudaDevice_ComparesBusLocations(string? left, string? right, bool expected)
        {
            Assert.That(CudaDeviceQuery.IsSameBusLocation(left, right), Is.EqualTo(expected));
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        /// <summary>Checks that DXGI enumeration reports sequential FFmpeg device indices and excludes software adapters by default.</summary>
        [Test]
        public void Dxgi_ListsAdaptersWithFFmpegDeviceIndices()
        {
            var all = DxgiAdapterQuery.GetAdapters(true);
            var hardware = DxgiAdapterQuery.GetAdapters(false);

            Assert.That(all.Count, Is.GreaterThanOrEqualTo(hardware.Count));
            for (var i = 0; i < all.Count; i++)
            {
                Assert.That(all[i].Device, Is.EqualTo(i.ToString(CultureInfo.InvariantCulture)));
                Assert.That(all[i].Name, Is.Not.Empty);
                Assert.That(all[i].Role, Is.EqualTo(HardwareGpuRole.Other));
            }

            foreach (var adapter in hardware)
            {
                // The Microsoft Basic Render Driver is a software adapter without video codecs.
                Assert.That(adapter.VendorId == 0x1414 && adapter.DeviceId == 0x8C, Is.False, adapter.ToString());
                Assert.That(adapter.Luid, Is.Not.EqualTo(0L), adapter.ToString());
                Assert.That(all[int.Parse(adapter.Device, CultureInfo.InvariantCulture)].Name, Is.EqualTo(adapter.Name));
            }
        }

        /// <summary>Checks that system candidates start with Unity's renderer and that the software codec route keeps only it.</summary>
        [Test]
        public void GetCandidates_StartsWithUnityRenderer()
        {
            HardwareGpuCatalog.CaptureRenderer();

            var others = HardwareGpuCatalog.GetCandidates(HardwareFallbackRoute.OtherGpus);
            var software = HardwareGpuCatalog.GetCandidates(HardwareFallbackRoute.SoftwareCodec);

            Assert.That(software.Count, Is.LessThanOrEqualTo(1));
            if (others.Count == 0)
            {
                Assert.That(software, Is.Empty);
                Assert.Ignore("This machine has no DXGI hardware adapter.");
            }

            Assert.That(software[0].Device, Is.EqualTo(others[0].Device));
            if (others[0].Role == HardwareGpuRole.Renderer)
            {
                Assert.That(others[0].VendorId, Is.EqualTo(SystemInfo.graphicsDeviceVendorID));
                Assert.That(others[0].DeviceId, Is.EqualTo(SystemInfo.graphicsDeviceID));
            }
            else
            {
                Assert.That(others[0].Role, Is.EqualTo(HardwareGpuRole.Default));
            }

            for (var i = 1; i < others.Count; i++)
            {
                Assert.That(others[i].Role, Is.EqualTo(HardwareGpuRole.Other));
            }
        }
#endif
    }
}
