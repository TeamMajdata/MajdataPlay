using System;
using MajdataPlay.IO;
using MajdataPlay.Settings;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>Verifies LED frame encoding and the output snapshot shared with LED worker threads.</summary>
    public class LedDeviceTests
    {
        /// <summary>The installed default settings.</summary>
        private SettingsScope _settings = null!;

        /// <summary>Installs default settings.</summary>
        [SetUp]
        public void SetUp()
        {
            _settings = new SettingsScope();
        }

        /// <summary>Restores the settings.</summary>
        [TearDown]
        public void TearDown()
        {
            _settings.Dispose();
        }

        /// <summary>Checks that the output state hands over a complete snapshot.</summary>
        [Test]
        public void LedOutputState_RoundTripsColoursAndBrightness()
        {
            var state = new LedOutputState();
            var colors = new Color[8];
            for (var i = 0; i < colors.Length; i++)
            {
                colors[i] = new Color(i / 8f, 0.5f, 1f - (i / 8f));
            }

            var initial = new Color[8];
            state.ReadLeds(initial, out var initialBrightness);
            state.WriteLeds(colors, 42);
            var read = new Color[8];
            state.ReadLeds(read, out var brightness);

            Assert.That(initialBrightness, Is.EqualTo(byte.MaxValue));
            Assert.That(initial, Is.All.EqualTo(new Color(0f, 0f, 0f, 0f)));
            Assert.That(read, Is.EqualTo(colors));
            Assert.That(brightness, Is.EqualTo(42));
        }

        /// <summary>Checks the serial LED colour command and its checksum.</summary>
        [Test]
        public void RosenLed_BuildSetColorPacket_EncodesColourAndChecksum()
        {
            var buffer = new byte[10];

            var packet = RosenLedDevice.BuildSetColorPacket(buffer, 3, new Color(1f, 0.5f, 0.25f), 1f).ToArray();

            var checksum = unchecked((byte)(0x11 + 0x01 + 0x05 + 0x31 + 0x03 + 0xFF + 0x7F + 0x3F));
            Assert.That(packet, Is.EqualTo(new byte[] { 0xE0, 0x11, 0x01, 0x05, 0x31, 0x03, 0xFF, 0x7F, 0x3F, checksum }));
        }

        /// <summary>Checks that the brightness factor scales the encoded colour.</summary>
        [Test]
        public void RosenLed_BuildSetColorPacket_AppliesBrightness()
        {
            var packet = RosenLedDevice.BuildSetColorPacket(new byte[10], 0, Color.white, 0.5f).ToArray();

            Assert.That(packet[6], Is.EqualTo(127));
            Assert.That(packet[7], Is.EqualTo(127));
            Assert.That(packet[8], Is.EqualTo(127));
        }

        /// <summary>Checks the configuration read by the serial LED device, including the Yuan refresh floor.</summary>
        [Test]
        public void RosenLed_Configuration()
        {
            _settings.Settings.IO.OutputDevice.Led.RefreshRateMs = 20;
            var device = new RosenLedDevice();
            device.WriteLeds(new Color[8], 10);
            DeviceTestSupport.BindParser(device)(new byte[] { 1, 2, 3 });

            Assert.That(PrivateMember.GetProperty<TimeSpan>(device, "PollingInterval"), Is.EqualTo(TimeSpan.FromMilliseconds(20)));
            Assert.That(PrivateMember.GetProperty<string>(device, "DaemonThreadName"), Is.EqualTo("IO/Led Thread"));

            var manufacturer = IODetector.DeviceManufacturer;
            try
            {
                PrivateMember.SetStaticProperty(typeof(IODetector), nameof(IODetector.DeviceManufacturer), DeviceManufacturerOption.Yuan);
                var yuan = new RosenLedDevice();
                Assert.That(PrivateMember.GetProperty<TimeSpan>(yuan, "PollingInterval"), Is.EqualTo(TimeSpan.FromMilliseconds(100)));
            }
            finally
            {
                PrivateMember.SetStaticProperty(typeof(IODetector), nameof(IODetector.DeviceManufacturer), manufacturer);
            }
        }

        /// <summary>Checks the HID LED report layout of Dao devices.</summary>
        [Test]
        public void DaoLed_BuildUpdatePacket_EncodesEveryColourAndCabinetBrightness()
        {
            var colors = new Color[8];
            colors[0] = Color.red;
            colors[7] = new Color(0f, 1f, 0.5f);
            var buffer = new byte[30];
            Array.Fill(buffer, (byte)0xAA);

            var packet = DaoLedOutput.BuildUpdatePacket(buffer, 5, colors, 200, 1f).ToArray();

            Assert.That(packet, Has.Length.EqualTo(30));
            Assert.That(packet[0], Is.EqualTo(5));
            Assert.That(packet[1], Is.EqualTo(255));
            Assert.That(packet[2], Is.EqualTo(0));
            Assert.That(packet[22], Is.EqualTo(0));
            Assert.That(packet[23], Is.EqualTo(255));
            Assert.That(packet[24], Is.EqualTo(127));
            Assert.That(packet[25], Is.EqualTo(200));
            Assert.That(packet[29], Is.EqualTo(0), "unused bytes are cleared");
        }

        /// <summary>Checks that the Dao brightness factor scales both the ring and the cabinet light.</summary>
        [Test]
        public void DaoLed_BuildUpdatePacket_AppliesBrightness()
        {
            var colors = new Color[8];
            colors[1] = Color.white;

            var packet = DaoLedOutput.BuildUpdatePacket(new byte[26], 0, colors, 200, 0.5f).ToArray();

            Assert.That(packet[4], Is.EqualTo(127));
            Assert.That(packet[25], Is.EqualTo(100));
        }

        /// <summary>Checks that the Dao LED output accepts colours and resets without a device.</summary>
        [Test]
        public void DaoLed_WriteAndReset_DoNotNeedADevice()
        {
            _settings.Settings.IO.OutputDevice.Led.RefreshRateMs = -5;
            var output = new DaoLedOutput();

            Assert.DoesNotThrow(() => output.WriteLeds(new Color[8], 1));
            Assert.DoesNotThrow(() => output.Reset());
        }

        /// <summary>Checks the pipe LED configuration, which clamps the refresh interval.</summary>
        [Test]
        public void PipeLed_Configuration_ClampsRefreshInterval()
        {
            _settings.Settings.IO.OutputDevice.Led.RefreshRateMs = 5000;
            var slow = new PipeLedDevice();
            _settings.Settings.IO.OutputDevice.Led.RefreshRateMs = -1;
            var fast = new PipeLedDevice();
            fast.WriteLeds(new Color[8], 0);
            DeviceTestSupport.BindParser(fast)(new byte[] { 1 });

            Assert.That(PrivateMember.GetProperty<TimeSpan>(slow, "PollingInterval"), Is.EqualTo(TimeSpan.FromMilliseconds(500)));
            Assert.That(PrivateMember.GetProperty<TimeSpan>(fast, "PollingInterval"), Is.EqualTo(TimeSpan.Zero));
            Assert.That(PrivateMember.GetProperty<string>(fast, "DaemonThreadName"), Is.EqualTo("IO/Led Thread"));
        }
    }
}
