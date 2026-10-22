using System;
using System.Buffers.Binary;
using System.Threading;
using MajdataPlay.IO;
using MajdataPlay.Settings;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>Verifies the report decoding of the button ring devices without opening any hardware.</summary>
    public class ButtonRingDeviceTests
    {
        /// <summary>The installed default settings.</summary>
        private SettingsScope _settings = null!;

        /// <summary>The player slot before the test.</summary>
        private int _playerIndex;

        /// <summary>Installs default settings and selects player 1.</summary>
        [SetUp]
        public void SetUp()
        {
            _settings = new SettingsScope();
            _playerIndex = DeviceTestSupport.SetPlayerIndex(1);
        }

        /// <summary>Restores the settings and player slot.</summary>
        [TearDown]
        public void TearDown()
        {
            DeviceTestSupport.SetPlayerIndex(_playerIndex);
            _settings.Dispose();
        }

        /// <summary>Checks the active-low ring buttons and active-high function buttons of a 1P IO4 report.</summary>
        [Test]
        public void Rosen_Player1Report_DecodesButtons()
        {
            var device = new RosenButtonRingDevice();
            var report = new byte[32];
            report[1 + 28] = 0b0100_1001;
            report[1 + 29] = 0b1011_1010;

            DeviceTestSupport.BindParser(device)(report);
            device.OnPreUpdate();

            Assert.That(ReadButtons(device), Is.EqualTo(new[]
            {
                true, false, false, false, true, false, false, false, true, false, false, true,
            }));
            Assert.That(device.IsButtonCurrentlyOn(0), Is.True);
            Assert.That(device.IsButtonCurrentlyOn(1), Is.False);
        }

        /// <summary>Checks that a 2P IO4 report reads the ring buttons from the second player's bytes.</summary>
        [Test]
        public void Rosen_Player2Report_UsesSecondPlayerBytes()
        {
            DeviceTestSupport.SetPlayerIndex(2);
            var device = new RosenButtonRingDevice();
            var report = new byte[34];
            report[1 + 28] = 0b1111_1111;
            report[1 + 29] = 0b1111_1111;
            report[1 + 30] = 0b0000_1001;
            report[1 + 31] = 0b1111_1000;
            report[1 + 25] = 0b0000_0001;

            DeviceTestSupport.BindParser(device)(report);
            device.OnPreUpdate();

            var buttons = ReadButtons(device);
            Assert.That(buttons[0], Is.True, "BA1 pressed");
            Assert.That(buttons[1], Is.False);
            Assert.That(buttons[3], Is.False);
            Assert.That(buttons[10], Is.True, "service");
        }

        /// <summary>Checks that reports shorter than the player's layout are ignored.</summary>
        [Test]
        public void Rosen_ShortReport_IsIgnored()
        {
            DeviceTestSupport.SetPlayerIndex(2);
            var device = new RosenButtonRingDevice();

            DeviceTestSupport.BindParser(device)(new byte[32]);
            device.OnPreUpdate();

            Assert.That(ReadButtons(device), Is.All.False);
        }

        /// <summary>Checks the polling interval and disconnect handling of the IO4 device.</summary>
        [Test]
        public void Rosen_PollingIntervalAndDisconnect()
        {
            _settings.Settings.IO.InputDevice.ButtonRing.PollingRateMs = 3;
            var device = new RosenButtonRingDevice();
            var report = new byte[32];
            report[1 + 28] = 0b0100_1001;
            report[1 + 29] = 0b1111_1000;
            DeviceTestSupport.BindParser(device)(report);

            PrivateMember.Invoke(device, "OnDisconnected");
            device.OnPreUpdate();

            Assert.That(PrivateMember.GetProperty<TimeSpan>(device, "PollingInterval"), Is.EqualTo(TimeSpan.FromMilliseconds(3)));
            Assert.That(PrivateMember.GetProperty<string>(device, "DaemonThreadName"), Is.EqualTo("IO/ButtonRing Thread"));
            Assert.That(device.IsButtonCurrentlyOn(0), Is.False);
            Assert.That(device.IsConnected, Is.False);
        }

        /// <summary>Checks the byte layout of a Yuan HID report.</summary>
        [Test]
        public void Yuan_Report_DecodesEveryButton()
        {
            var device = new YuanButtonRingDevice();
            var report = new byte[14];
            report[1 + 4] = 1;
            report[1 + 1] = 1;
            report[1 + 5] = 1;
            report[1 + 10] = 1;
            report[1 + 12] = 1;
            report[1 + 3] = 2;

            DeviceTestSupport.BindParser(device)(report);
            device.OnPreUpdate();

            Assert.That(ReadButtons(device), Is.EqualTo(new[]
            {
                true, false, false, true, false, false, false, true, true, false, true, false,
            }));
        }

        /// <summary>Checks that every Yuan slot maps to its button and that short reports are ignored.</summary>
        [Test]
        public void Yuan_SlotMapping_AndShortReport()
        {
            var slots = new[] { 4, 3, 2, 1, 8, 7, 6, 5, 10, 9, 12, 11 };
            for (var button = 0; button < slots.Length; button++)
            {
                var device = new YuanButtonRingDevice();
                var report = new byte[14];
                report[1 + slots[button]] = 1;

                DeviceTestSupport.BindParser(device)(report);
                device.OnPreUpdate();

                Assert.That(ReadButtons(device)[button], Is.True, $"button {button}");
            }

            var shortDevice = new YuanButtonRingDevice();
            DeviceTestSupport.BindParser(shortDevice)(new byte[] { 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 });
            shortDevice.OnPreUpdate();
            Assert.That(ReadButtons(shortDevice), Is.All.False);
            PrivateMember.Invoke(shortDevice, "OnDisconnected");
            Assert.That(shortDevice.IsButtonCurrentlyOn(0), Is.False);
        }

        /// <summary>Checks that the Dao composite report fills both the buttons and the touch sensors.</summary>
        [Test]
        public void Dao_Report_DecodesButtonsAndTouch()
        {
            var device = new DaoCompositeDevice(false);
            var report = new byte[]
            {
                0x00,
                0b0000_0001,
                0b1000_0000,
                0b0000_0011,
                0b0000_0010,
                0b1000_0000,
                0b0000_0101,
                0b0000_1101,
            };

            DeviceTestSupport.BindParser(device)(report);
            device.OnPreUpdate();

            var buttons = ReadButtons(device);
            Assert.That(buttons, Is.EqualTo(new[]
            {
                true, false, true, false, false, false, false, false, true, true, true, false,
            }));
            var sensors = ReadTouch(device);
            Assert.That(sensors[0], Is.True, "A1");
            Assert.That(sensors[15], Is.True, "B8");
            Assert.That(sensors[16], Is.True, "C1");
            Assert.That(sensors[17], Is.True, "C2");
            Assert.That(sensors[19], Is.True, "D2");
            Assert.That(sensors[33], Is.True, "E8");
            Assert.That(device.IsSensorCurrentlyOn(16), Is.True);
            Assert.That(device.IsButtonCurrentlyOn(2), Is.True);
        }

        /// <summary>Checks that short Dao reports are ignored and a disconnect releases everything.</summary>
        [Test]
        public void Dao_ShortReportAndDisconnect()
        {
            var device = new DaoCompositeDevice(true);
            var parse = DeviceTestSupport.BindParser(device);
            parse(new byte[7]);
            parse(new byte[] { 0, 0xFF, 0xFF, 0x03, 0xFF, 0xFF, 0xFF, 0xFF });

            PrivateMember.Invoke(device, "OnDisconnected");
            device.OnPreUpdate();

            Assert.That(ReadButtons(device), Is.All.False);
            Assert.That(ReadTouch(device), Is.All.False);
            Assert.DoesNotThrow(() => device.WriteLeds(new UnityEngine.Color[8], 128));
        }

        /// <summary>Checks that the Dao polling interval is the fastest enabled capability.</summary>
        [Test]
        public void Dao_PollingInterval_UsesFastestEnabledCapability()
        {
            var input = _settings.Settings.IO.InputDevice;
            input.ButtonRing.PollingRateMs = 8;
            input.TouchPanel.PollingRateMs = 4;
            _settings.Settings.IO.OutputDevice.Led.RefreshRateMs = 2;

            var withLeds = new DaoCompositeDevice(true);
            var withoutLeds = new DaoCompositeDevice(false);
            input.ButtonRing = new ButtonRingOptions { Enable = false };
            input.TouchPanel = new TouchPanelOptions { Enable = false };
            var idle = new DaoCompositeDevice(false);

            Assert.That(PrivateMember.GetProperty<TimeSpan>(withLeds, "PollingInterval"), Is.EqualTo(TimeSpan.FromMilliseconds(2)));
            Assert.That(PrivateMember.GetProperty<TimeSpan>(idle, "PollingInterval"), Is.EqualTo(TimeSpan.Zero));
            Assert.That(PrivateMember.GetProperty<string>(withoutLeds, "DaemonThreadName"), Is.EqualTo("IO/Dao Thread"));
        }

        /// <summary>Checks that pipe report packets set the buttons from their bit mask.</summary>
        [Test]
        public void Pipe_ReportPacket_SetsButtons()
        {
            var device = new PipeButtonRingDevice();
            var parse = DeviceTestSupport.BindParser(device);
            var payload = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(payload, 0b1000_0000_0101UL);
            var packet = BuildPipePacket(PipePacketType.Report, payload);

            parse(packet.AsSpan(0, 5));
            device.OnPreUpdate();
            Assert.That(ReadButtons(device), Is.All.False, "incomplete packets are retained");

            parse(packet.AsSpan(5));
            device.OnPreUpdate();
            var buttons = ReadButtons(device);

            Assert.That(buttons[0], Is.True);
            Assert.That(buttons[2], Is.True);
            Assert.That(buttons[11], Is.True);
            Assert.That(buttons[1], Is.False);
            Assert.That(device.IsButtonCurrentlyOn(11), Is.True);
        }

        /// <summary>Checks that heartbeats and malformed payloads are ignored and a disconnect clears the state.</summary>
        [Test]
        public void Pipe_IgnoresHeartbeatsAndMalformedPayloads()
        {
            var device = new PipeButtonRingDevice();
            var parse = DeviceTestSupport.BindParser(device);
            parse(BuildPipePacket(PipePacketType.HeartBeat, new byte[8] { 0xFF, 0, 0, 0, 0, 0, 0, 0 }));
            parse(BuildPipePacket(PipePacketType.Report, new byte[] { 0xFF }));
            device.OnPreUpdate();
            Assert.That(ReadButtons(device), Is.All.False);

            parse(BuildPipePacket(PipePacketType.Report, new byte[8] { 0xFF, 0x0F, 0, 0, 0, 0, 0, 0 }));
            PrivateMember.Invoke(device, "OnDisconnected");
            device.OnPreUpdate();

            Assert.That(device.IsButtonCurrentlyOn(0), Is.False);
            Assert.That(PrivateMember.GetProperty<string>(device, "DaemonThreadName"), Is.EqualTo("IO/ButtonRing Thread"));
        }

        /// <summary>Checks that the pipe parser rejects data that overflows its pending buffer.</summary>
        [Test]
        public void Pipe_PendingOverflow_Throws()
        {
            var device = new PipeButtonRingDevice();

            Assert.Throws<System.IO.InvalidDataException>(() => DeviceTestSupport.BindParser(device)(new byte[9000]));
        }

        /// <summary>Checks that the keyboard ring device connects without hardware and samples the keyboard.</summary>
        [Test]
        public void Keyboard_ConnectsAndPublishesSamples()
        {
            _settings.Settings.IO.InputDevice.ButtonRing.PollingRateMs = 5;
            var device = new KeyboardButtonRingDevice();

            Assert.That(PrivateMember.Invoke<bool>(device, "Connect", CancellationToken.None), Is.True);
            DeviceTestSupport.BindTokenAction(device, "Update")(CancellationToken.None);
            DeviceTestSupport.BindParser(device)(new byte[] { 1, 2, 3 });
            device.OnPreUpdate();
            PrivateMember.Invoke(device, "OnDisconnected");
            device.OnPreUpdate();

            Assert.That(ReadButtons(device), Has.Length.EqualTo(12));
            Assert.That(device.IsButtonCurrentlyOn(0), Is.False);
            Assert.That(PrivateMember.GetProperty<TimeSpan>(device, "PollingInterval"), Is.EqualTo(TimeSpan.FromMilliseconds(5)));
        }

        /// <summary>Reads the button snapshot of a device.</summary>
        /// <param name="device">The device.</param>
        /// <returns>The twelve button states.</returns>
        private static bool[] ReadButtons(IButtonRingDevice device)
        {
            var states = new bool[12];
            device.ReadButtons(states, new bool[12], new bool[12]);
            return states;
        }

        /// <summary>Reads the touch snapshot of a device.</summary>
        /// <param name="device">The device.</param>
        /// <returns>The 35 sensor slots.</returns>
        private static bool[] ReadTouch(ITouchPanelDevice device)
        {
            var states = new bool[35];
            device.ReadTouchPanel(states, new bool[35], new bool[35]);
            return states;
        }

        /// <summary>Serialises a pipe packet.</summary>
        /// <param name="type">The packet type.</param>
        /// <param name="payload">The payload.</param>
        /// <returns>The packet bytes.</returns>
        private static byte[] BuildPipePacket(PipePacketType type, byte[] payload)
        {
            var buffer = new byte[PipePacket.PACKET_HEADER_LENGTH + payload.Length];
            new PipePacket { Type = type, Version = 1, Length = (ushort)payload.Length, Payload = payload }.Write(buffer);
            return buffer;
        }
    }
}
