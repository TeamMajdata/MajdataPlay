#if UNITY_STANDALONE
using System;
using System.Buffers.Binary;
using MajdataPlay.IO;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>Verifies the report decoding of serial, pipe and NPro touch devices without opening any hardware.</summary>
    public class TouchPanelDeviceTests
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

        /// <summary>Checks that a serial report spreads five sensor bits over each data byte.</summary>
        [Test]
        public void Rosen_CompleteReport_SetsSensors()
        {
            var device = new RosenTouchPanelDevice();

            DeviceTestSupport.BindParser(device)(Report(0b00001, 0, 0, 0b10000, 0, 0, 0b11111));
            device.OnPreUpdate();
            var sensors = ReadTouch(device);

            Assert.That(sensors[0], Is.True);
            Assert.That(sensors[19], Is.True);
            for (var i = 30; i < 35; i++)
            {
                Assert.That(sensors[i], Is.True, $"sensor {i}");
            }
            Assert.That(Count(sensors), Is.EqualTo(7));
        }

        /// <summary>Checks that bytes before a header are skipped and a report split across reads is assembled.</summary>
        [Test]
        public void Rosen_GarbageAndSplitReport_AreHandled()
        {
            var device = new RosenTouchPanelDevice();
            var parse = DeviceTestSupport.BindParser(device);
            var report = Report(0b00010, 0, 0, 0, 0, 0, 0);

            parse(new byte[] { 0x01, 0x02, (byte)')' });
            parse(report.AsSpan(0, 4));
            Assert.That(device.IsSensorCurrentlyOn(1), Is.False);
            parse(report.AsSpan(4));

            Assert.That(device.IsSensorCurrentlyOn(1), Is.True);
        }

        /// <summary>Checks that a report without its terminator resynchronises on a later header.</summary>
        [Test]
        public void Rosen_MissingTerminator_ResynchronisesOnNextHeader()
        {
            var device = new RosenTouchPanelDevice();
            var parse = DeviceTestSupport.BindParser(device);
            var broken = new byte[] { (byte)'(', 1, 2, (byte)'(', 4, 5, 6, 7, (byte)'X' };
            var valid = Report(0, 0b00100, 0, 0, 0, 0, 0);

            parse(broken);
            parse(valid);

            Assert.That(device.IsSensorCurrentlyOn(7), Is.True);
        }

        /// <summary>Checks that a broken report without another header is dropped entirely.</summary>
        [Test]
        public void Rosen_BrokenReportWithoutHeader_IsDropped()
        {
            var device = new RosenTouchPanelDevice();
            var parse = DeviceTestSupport.BindParser(device);

            parse(new byte[] { (byte)'(', 1, 1, 1, 1, 1, 1, 1, 0 });
            parse(Report(0, 0, 0, 0, 0, 0, 0b00001));

            Assert.That(device.IsSensorCurrentlyOn(0), Is.False);
            Assert.That(device.IsSensorCurrentlyOn(30), Is.True);
        }

        /// <summary>Checks that every report within one read is published, keeping short pulses.</summary>
        [Test]
        public void Rosen_TwoReportsInOneRead_LatchesThePulse()
        {
            var device = new RosenTouchPanelDevice();
            var data = new byte[18];
            Report(0b00001, 0, 0, 0, 0, 0, 0).CopyTo(data, 0);
            Report(0, 0, 0, 0, 0, 0, 0).CopyTo(data, 9);

            DeviceTestSupport.BindParser(device)(data);
            device.OnPreUpdate();
            var hadOn = new bool[35];
            device.ReadTouchPanel(new bool[35], hadOn, new bool[35]);

            Assert.That(device.IsSensorCurrentlyOn(0), Is.False);
            Assert.That(hadOn[0], Is.True);
        }

        /// <summary>Checks the disconnect handling and configuration properties of the serial touch panel.</summary>
        [Test]
        public void Rosen_DisconnectAndProperties()
        {
            _settings.Settings.IO.InputDevice.TouchPanel.PollingRateMs = 7;
            var device = new RosenTouchPanelDevice();
            var parse = DeviceTestSupport.BindParser(device);
            parse(Report(0b11111, 0, 0, 0, 0, 0, 0));
            parse(new byte[] { (byte)'(', 1, 2 });

            PrivateMember.Invoke(device, "OnDisconnected");
            parse(Report(0, 0b00001, 0, 0, 0, 0, 0));

            Assert.That(device.IsSensorCurrentlyOn(0), Is.False);
            Assert.That(device.IsSensorCurrentlyOn(5), Is.True, "a partial report is discarded on disconnect");
            Assert.That(PrivateMember.GetProperty<TimeSpan>(device, "PollingInterval"), Is.EqualTo(TimeSpan.FromMilliseconds(7)));
            Assert.That(PrivateMember.GetProperty<string>(device, "DaemonThreadName"), Is.EqualTo("IO/TouchPanel Thread"));
        }

        /// <summary>Checks the sensitivity codes sent for the outer ring sensors.</summary>
        /// <param name="sensitivity">The configured sensitivity.</param>
        /// <param name="expected">The expected device code.</param>
        [TestCase((short)-5, (byte)0x5A)]
        [TestCase((short)-4, (byte)0x50)]
        [TestCase((short)-3, (byte)0x46)]
        [TestCase((short)-2, (byte)0x3C)]
        [TestCase((short)-1, (byte)0x32)]
        [TestCase((short)0, (byte)0x28)]
        [TestCase((short)1, (byte)0x1E)]
        [TestCase((short)2, (byte)0x1A)]
        [TestCase((short)3, (byte)0x17)]
        [TestCase((short)4, (byte)0x14)]
        [TestCase((short)5, (byte)0x0A)]
        [TestCase((short)9, (byte)0x28)]
        public void Rosen_Sensitivity_OuterRing(short sensitivity, byte expected)
        {
            var sens = ((short)sensitivity, (short)0, (short)0, (short)0, (short)0);

            for (var sensor = 0x41; sensor <= 0x48; sensor++)
            {
                Assert.That(Sensitivity((byte)sensor, sens), Is.EqualTo(expected), $"sensor 0x{sensor:X2}");
            }
        }

        /// <summary>Checks the sensitivity codes sent for the inner sensors of each group.</summary>
        /// <param name="sensitivity">The configured sensitivity.</param>
        /// <param name="expected">The expected device code.</param>
        [TestCase((short)-5, (byte)0x46)]
        [TestCase((short)-4, (byte)0x3C)]
        [TestCase((short)-3, (byte)0x32)]
        [TestCase((short)-2, (byte)0x28)]
        [TestCase((short)-1, (byte)0x1E)]
        [TestCase((short)0, (byte)0x14)]
        [TestCase((short)1, (byte)0x0F)]
        [TestCase((short)2, (byte)0x0A)]
        [TestCase((short)3, (byte)0x05)]
        [TestCase((short)4, (byte)0x01)]
        [TestCase((short)5, (byte)0x01)]
        public void Rosen_Sensitivity_InnerGroups(short sensitivity, byte expected)
        {
            Assert.That(Sensitivity(0x49, ((short)0, sensitivity, (short)0, (short)0, (short)0)), Is.EqualTo(expected), "B");
            Assert.That(Sensitivity(0x52, ((short)0, (short)0, sensitivity, (short)0, (short)0)), Is.EqualTo(expected), "C");
            Assert.That(Sensitivity(0x5A, ((short)0, (short)0, (short)0, sensitivity, (short)0)), Is.EqualTo(expected), "D");
            Assert.That(Sensitivity(0x62, ((short)0, (short)0, (short)0, (short)0, sensitivity)), Is.EqualTo(expected), "E");
        }

        /// <summary>Checks that an unknown sensor code falls back to the neutral sensitivity.</summary>
        [Test]
        public void Rosen_Sensitivity_UnknownSensorUsesDefault()
        {
            Assert.That(Sensitivity(0x40, ((short)5, (short)5, (short)5, (short)5, (short)5)), Is.EqualTo(0x28));
            Assert.That(Sensitivity(0x63, ((short)-5, (short)-5, (short)-5, (short)-5, (short)-5)), Is.EqualTo(0x28));
        }

        /// <summary>Checks that pipe touch reports set every sensor slot from the bit mask.</summary>
        [Test]
        public void Pipe_ReportPacket_SetsSensors()
        {
            var device = new PipeTouchPanelDevice();
            var payload = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(payload, (1UL << 0) | (1UL << 16) | (1UL << 34));

            DeviceTestSupport.BindParser(device)(PipePacketBytes(PipePacketType.Report, payload));
            device.OnPreUpdate();
            var sensors = ReadTouch(device);

            Assert.That(sensors[0] && sensors[16] && sensors[34], Is.True);
            Assert.That(Count(sensors), Is.EqualTo(3));
            Assert.That(device.IsSensorCurrentlyOn(16), Is.True);
        }

        /// <summary>Checks that pipe heartbeats and malformed payloads are ignored and a disconnect clears the panel.</summary>
        [Test]
        public void Pipe_IgnoresHeartbeatsAndClearsOnDisconnect()
        {
            _settings.Settings.IO.InputDevice.TouchPanel.PollingRateMs = 2;
            var device = new PipeTouchPanelDevice();
            var parse = DeviceTestSupport.BindParser(device);
            parse(PipePacketBytes(PipePacketType.HeartBeat, new byte[8] { 1, 0, 0, 0, 0, 0, 0, 0 }));
            parse(PipePacketBytes(PipePacketType.Report, new byte[] { 1, 0, 0, 0 }));
            Assert.That(device.IsSensorCurrentlyOn(0), Is.False);

            parse(PipePacketBytes(PipePacketType.Report, new byte[8] { 1, 0, 0, 0, 0, 0, 0, 0 }));
            Assert.That(device.IsSensorCurrentlyOn(0), Is.True);
            parse(new byte[] { 0x48, 0x04, 0x01 });
            PrivateMember.Invoke(device, "OnDisconnected");

            Assert.That(device.IsSensorCurrentlyOn(0), Is.False);
            Assert.That(PrivateMember.GetProperty<TimeSpan>(device, "PollingInterval"), Is.EqualTo(TimeSpan.FromMilliseconds(2)));
            Assert.Throws<System.IO.InvalidDataException>(() => parse(new byte[9000]));
        }

#if UNITY_STANDALONE_WIN
        /// <summary>Checks that NPro reports are reassembled across reads and published for buttons and touch.</summary>
        [Test]
        public void Npro_Reports_PublishButtonsAndTouch()
        {
            var device = new NproDevice(false);
            var first = new byte[] { 0x01, 0, 0, 0, 0, 0, 0b0000_0010, 0b0000_0100 };
            var second = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 };

            device.ParseReport(first.AsSpan(0, 3));
            Assert.That(device.IsSensorCurrentlyOn(0), Is.False);
            var rest = new byte[5 + 8];
            first.AsSpan(3).CopyTo(rest);
            second.CopyTo(rest, 5);
            device.ParseReport(rest);
            device.OnPreUpdate();

            var buttonHadOn = new bool[12];
            var buttons = new bool[12];
            device.ReadButtons(buttons, buttonHadOn, new bool[12]);
            var sensorHadOn = new bool[35];
            device.ReadTouchPanel(new bool[35], sensorHadOn, new bool[35]);

            Assert.That(buttons[1], Is.False, "the second report released the button");
            Assert.That(buttonHadOn[1], Is.True);
            Assert.That(buttonHadOn[11], Is.True, "select P2");
            Assert.That(sensorHadOn[0], Is.True);
            Assert.That(device.IsButtonCurrentlyOn(1), Is.False);
        }

        /// <summary>Checks the NPro transport configuration and the reset performed on disconnect.</summary>
        [Test]
        public void Npro_ConfigurationAndReset()
        {
            DeviceTestSupport.SetPlayerIndex(2);
            var device = new NproDevice(true);
            device.WriteLeds(new Color[8], 255);
            device.ParseReport(new byte[] { 0xFF, 0, 0, 0, 0, 0, 0xFF });

            device.ResetTransport();
            device.ParseReport(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0 });

            Assert.That(device.IsSensorCurrentlyOn(1), Is.True, "the partial report was dropped on reset");
            Assert.That(device.IsSensorCurrentlyOn(0), Is.False);
            Assert.That(PrivateMember.GetProperty<string>(device, "DiagnosticName"), Is.EqualTo("NPro"));
            Assert.That(PrivateMember.GetProperty<string>(device, "DaemonThreadName"), Is.EqualTo("IO/NPro Thread"));
            Assert.That(PrivateMember.GetProperty<TimeSpan>(device, "PollingInterval"), Is.EqualTo(TimeSpan.Zero));
            Assert.That(device.IsConnected, Is.False);
        }
#endif

        /// <summary>Builds a nine byte serial touch report.</summary>
        /// <param name="data">The seven data bytes.</param>
        /// <returns>The framed report.</returns>
        private static byte[] Report(params byte[] data)
        {
            var report = new byte[9];
            report[0] = (byte)'(';
            data.CopyTo(report, 1);
            report[8] = (byte)')';
            return report;
        }

        /// <summary>Invokes the private sensitivity lookup of the serial touch panel.</summary>
        /// <param name="sensor">The sensor command character.</param>
        /// <param name="sens">The configured sensitivities of groups A to E.</param>
        /// <returns>The device code.</returns>
        private static byte Sensitivity(byte sensor, (short A, short B, short C, short D, short E) sens)
        {
            return PrivateMember.InvokeStatic<byte>(typeof(RosenTouchPanelDevice), "GetSensitivityValue", sensor, sens);
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

        /// <summary>Counts the set flags.</summary>
        /// <param name="values">The flags.</param>
        /// <returns>The number of true values.</returns>
        private static int Count(bool[] values)
        {
            var count = 0;
            foreach (var value in values)
            {
                if (value)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>Serialises a pipe packet.</summary>
        /// <param name="type">The packet type.</param>
        /// <param name="payload">The payload.</param>
        /// <returns>The packet bytes.</returns>
        private static byte[] PipePacketBytes(PipePacketType type, byte[] payload)
        {
            var buffer = new byte[PipePacket.PACKET_HEADER_LENGTH + payload.Length];
            new PipePacket { Type = type, Version = 1, Length = (ushort)payload.Length, Payload = payload }.Write(buffer);
            return buffer;
        }
    }
}
#endif
