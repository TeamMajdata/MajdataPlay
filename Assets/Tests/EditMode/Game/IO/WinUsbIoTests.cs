using System;
using System.Collections.Generic;
using MajdataPlay.IO;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>Verifies the pure string handling of <see cref="WinUsbIo"/> used to identify and match USB interfaces.</summary>
    public class WinUsbIoTests
    {
        /// <summary>A device interface path with a serial number.</summary>
        private const string InterfacePath = @"\\?\usb#vid_2e3c&pid_5751&mi_00#7&1a2b3c4d&0&0000#{7f2a9c41-5e3b-4d8a-9f16-3c7e5b2d9a48}";

        /// <summary>Checks that an empty identifier matches any device.</summary>
        /// <param name="identifier">The empty identifier.</param>
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void MatchesIdentifier_EmptyIdentifier_MatchesAnything(string? identifier)
        {
            Assert.That(new WinUsbIo.DevicePath { Path = InterfacePath }.MatchesIdentifier(identifier!), Is.True);
        }

        /// <summary>Checks that the instance id, friendly name, location and interface serial are all searched.</summary>
        [Test]
        public void MatchesIdentifier_SearchesTheDescriptiveFields()
        {
            var device = new WinUsbIo.DevicePath
            {
                Path = InterfacePath,
                InstanceId = @"USB\VID_2E3C&PID_5751&MI_00\7&1A2B3C4D&0&0000",
                FriendlyName = "NoronDX GAME",
                LocationInformation = "Port_#0003.Hub_#0002",
            };

            Assert.That(device.MatchesIdentifier("1a2b3c4d"), Is.True, "instance id, ignoring case");
            Assert.That(device.MatchesIdentifier("norondx"), Is.True, "friendly name");
            Assert.That(device.MatchesIdentifier("hub_#0002"), Is.True, "location information");
            Assert.That(device.MatchesIdentifier(" 7&1A2B3C4D&0&0000 "), Is.True, "interface serial after trimming");
            Assert.That(device.MatchesIdentifier("other"), Is.False);
        }

        /// <summary>Checks that location paths are compared by their port chain.</summary>
        [Test]
        public void MatchesIdentifier_ComparesLocationPortChains()
        {
            var device = new WinUsbIo.DevicePath
            {
                Path = @"\\?\usb#vid_227d&pid_0103#serial#{guid}",
                LocationPaths = new[] { "PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(2)#USB(3)" },
            };

            Assert.That(device.MatchesIdentifier("2-3"), Is.True);
            Assert.That(device.MatchesIdentifier("2.3"), Is.True);
            Assert.That(device.MatchesIdentifier("3-2"), Is.False);
            Assert.That(device.MatchesIdentifier("serial"), Is.True, "the segment after vid/pid is the serial");
        }

        /// <summary>Checks the port based location comparison directly, including its rejection paths.</summary>
        /// <param name="device">The device location.</param>
        /// <param name="target">The configured location.</param>
        /// <param name="expected">Whether they match.</param>
        [TestCase("PCIROOT(0)#USB(2)#USB(3)", "USB(3)", true)]
        [TestCase("PCIROOT(0)#USB(2)#USB(5)#USB(3)", "2-3", true)]
        [TestCase("PCIROOT(0)#USB(2)", "2-3", false)]
        [TestCase("bus1-addr4.2.7", "2.7", true)]
        [TestCase("Port_#0003.Hub_#0002", "1-2", false)]
        [TestCase("", "1", false)]
        [TestCase("1-2", " ", false)]
        [TestCase("1-2", "x", false)]
        public void MatchesLocation_ComparesPorts(string device, string target, bool expected)
        {
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(WinUsbIo), "MatchesLocation", device, target), Is.EqualTo(expected));
        }

        /// <summary>Checks the extraction of USB port numbers from both location formats.</summary>
        [Test]
        public void ExtractPortNumbers_ReadsBothFormats()
        {
            var usb = PrivateMember.InvokeStatic<List<int>>(typeof(WinUsbIo), "ExtractPortNumbers", "PCIROOT(0)#USB(12)#USB(4)");
            var dotted = PrivateMember.InvokeStatic<List<int>>(typeof(WinUsbIo), "ExtractPortNumbers", "bus3-addr9.1-6");

            Assert.That(usb, Is.EqualTo(new[] { 12, 4 }));
            Assert.That(dotted, Is.EqualTo(new[] { 1, 6 }));
        }

        /// <summary>Checks that the interface serial is the segment following the vid/pid segment.</summary>
        [Test]
        public void GetInterfaceSerial_ReadsTheSegmentAfterVidPid()
        {
            Assert.That(PrivateMember.InvokeStatic<string?>(typeof(WinUsbIo), "GetInterfaceSerial", InterfacePath), Is.EqualTo("7&1a2b3c4d&0&0000"));
            Assert.That(PrivateMember.InvokeStatic<string?>(typeof(WinUsbIo), "GetInterfaceSerial", @"\\?\hid#something"), Is.Null);
        }

        /// <summary>Checks the parsing of the interface number from hardware ids.</summary>
        [Test]
        public void TryGetInterfaceNumber_ParsesCompositeAndSingleInterfaceIds()
        {
            var composite = new object?[] { new[] { @"USB\VID_2E3C&PID_5751&REV_0100&MI_02", @"USB\VID_2E3C&PID_5751&MI_02" }, (ushort)0x2E3C, (ushort)0x5751, (byte)0 };
            var single = new object?[] { new[] { @"USB\VID_227D&PID_0103&REV_0001" }, (ushort)0x227D, (ushort)0x0103, (byte)9 };
            var other = new object?[] { new[] { @"USB\VID_1234&PID_5678" }, (ushort)0x227D, (ushort)0x0103, (byte)9 };
            var missing = new object?[] { null, (ushort)0x227D, (ushort)0x0103, (byte)9 };
            var badSuffix = new object?[] { new[] { @"USB\VID_227D&PID_0103&MI_ZZ" }, (ushort)0x227D, (ushort)0x0103, (byte)9 };

            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(WinUsbIo), "TryGetInterfaceNumber", composite), Is.True);
            Assert.That(composite[3], Is.EqualTo((byte)2));
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(WinUsbIo), "TryGetInterfaceNumber", single), Is.True);
            Assert.That(single[3], Is.EqualTo((byte)0));
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(WinUsbIo), "TryGetInterfaceNumber", other), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(WinUsbIo), "TryGetInterfaceNumber", missing), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(WinUsbIo), "TryGetInterfaceNumber", badSuffix), Is.True);
            Assert.That(badSuffix[3], Is.EqualTo((byte)0));
        }

        /// <summary>Checks the parsing of vendor and product ids from interface paths.</summary>
        [Test]
        public void TryParseVidPid_ReadsHexIds()
        {
            var valid = new object?[] { InterfacePath, (ushort)0, (ushort)0 };
            var missing = new object?[] { @"\\?\usb#nothing", (ushort)0, (ushort)0 };
            var invalid = new object?[] { @"\\?\usb#vid_zzzz&pid_0001", (ushort)0, (ushort)0 };

            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(WinUsbIo), "TryParseVidPid", valid), Is.True);
            Assert.That(valid[1], Is.EqualTo((ushort)0x2E3C));
            Assert.That(valid[2], Is.EqualTo((ushort)0x5751));
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(WinUsbIo), "TryParseVidPid", missing), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(WinUsbIo), "TryParseVidPid", invalid), Is.False);
        }

        /// <summary>Checks that interface GUID lists are parsed with and without braces.</summary>
        [Test]
        public void ParseGuids_ReadsBracedAndBareLists()
        {
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();

            var braced = PrivateMember.InvokeStatic<List<Guid>>(typeof(WinUsbIo), "ParseGuids", $"{{{first}}}\0{{{second}}}\0\0");
            var bare = PrivateMember.InvokeStatic<List<Guid>>(typeof(WinUsbIo), "ParseGuids", $"{first}\0not-a-guid\0");
            var broken = PrivateMember.InvokeStatic<List<Guid>>(typeof(WinUsbIo), "ParseGuids", "{unterminated");

            Assert.That(braced, Is.EqualTo(new[] { first, second }));
            Assert.That(bare, Is.EqualTo(new[] { first }));
            Assert.That(broken, Is.Empty);
        }

        /// <summary>Checks that enumerating a vendor and product id that no device uses returns an empty list.</summary>
        [Test]
        public void EnumerateWinUsbInterfaces_UnknownDevice_ReturnsEmpty()
        {
            Assert.That(WinUsbIo.EnumerateWinUsbInterfaces(0xFFFE, 0xFFFD), Is.Empty);
            Assert.That(WinUsbIo.EnumerateWinUsbInterfaces(0xFFFE, 0xFFFD, 0, "None", Guid.NewGuid()), Is.Empty);
        }
    }
}
