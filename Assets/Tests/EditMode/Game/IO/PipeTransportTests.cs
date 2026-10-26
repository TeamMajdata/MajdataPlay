#if UNITY_STANDALONE
using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.IO;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>
    /// Verifies the named pipe transports of the pipe devices against in-process pipe servers, using a player slot that no
    /// real IO bridge uses.
    /// </summary>
    public class PipeTransportTests
    {
        /// <summary>The player slot used to build unique pipe names.</summary>
        private const int TestPlayer = 71;

        /// <summary>The installed default settings.</summary>
        private SettingsScope _settings = null!;

        /// <summary>The player slot before the test.</summary>
        private int _playerIndex;

        /// <summary>The pipe server of the test.</summary>
        private NamedPipeServerStream? _server;

        /// <summary>Installs default settings and the test player slot.</summary>
        [SetUp]
        public void SetUp()
        {
            _settings = new SettingsScope();
            _playerIndex = DeviceTestSupport.SetPlayerIndex(TestPlayer);
        }

        /// <summary>Closes the server and restores the player slot and settings.</summary>
        [TearDown]
        public void TearDown()
        {
            _server?.Dispose();
            _server = null;
            DeviceTestSupport.SetPlayerIndex(_playerIndex);
            _settings.Dispose();
        }

        /// <summary>Checks that the LED pipe sends every colour first, then heartbeats while the colours do not change.</summary>
        [Test]
        public void Led_SendsColoursThenHeartbeats()
        {
            var device = new PipeLedDevice();
            Connect(device, "Led");
            var colors = new Color[8];
            colors[0] = Color.red;
            colors[7] = Color.blue;
            device.WriteLeds(colors, 255);
            var update = DeviceTestSupport.BindTokenAction(device, "Update");

            update(CancellationToken.None);
            var report = ReadFromServer(PipePacket.PACKET_HEADER_LENGTH + 32);
            update(CancellationToken.None);
            var heartbeat = ReadFromServer(PipePacket.PACKET_HEADER_LENGTH);

            Assert.That(report[2], Is.EqualTo((byte)PipePacketType.Report));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(5)), Is.EqualTo(32));
            Assert.That(report.AsSpan(7, 4).ToArray(), Is.EqualTo(new byte[] { 0, 255, 0, 0 }));
            Assert.That(report.AsSpan(7 + 28, 4).ToArray(), Is.EqualTo(new byte[] { 7, 0, 0, 255 }));
            Assert.That(heartbeat[2], Is.EqualTo((byte)PipePacketType.HeartBeat));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(heartbeat.AsSpan(5)), Is.EqualTo(0));

            PrivateMember.Invoke(device, "Disconnect");
            Assert.Throws<IOException>(() => update(CancellationToken.None));
        }

        /// <summary>Checks that a changed colour is sent alone when throttling is enabled.</summary>
        [Test]
        public void Led_SendsOnlyChangedColours()
        {
            var device = new PipeLedDevice();
            Connect(device, "Led");
            var colors = new Color[8];
            device.WriteLeds(colors, 255);
            var update = DeviceTestSupport.BindTokenAction(device, "Update");
            update(CancellationToken.None);
            ReadFromServer(PipePacket.PACKET_HEADER_LENGTH + 32);

            colors[3] = Color.green;
            device.WriteLeds(colors, 255);
            update(CancellationToken.None);
            var report = ReadFromServer(PipePacket.PACKET_HEADER_LENGTH + 4);

            Assert.That(report.AsSpan(7, 4).ToArray(), Is.EqualTo(new byte[] { 3, 0, 255, 0 }));
            PrivateMember.Invoke(device, "Disconnect");
        }

        /// <summary>Checks that button reports from the pipe are parsed and a closed pipe ends the session.</summary>
        [Test]
        public void ButtonRing_ReadsReportsUntilThePipeCloses()
        {
            var device = new PipeButtonRingDevice();
            Connect(device, "ButtonRing");
            var payload = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(payload, 0b101UL);
            WriteToServer(Packet(PipePacketType.Report, payload));
            var update = DeviceTestSupport.BindTokenAction(device, "Update");

            update(CancellationToken.None);
            Assert.That(device.IsButtonCurrentlyOn(0) && device.IsButtonCurrentlyOn(2), Is.True);

            _server!.Dispose();
            Assert.Throws<EndOfStreamException>(() => update(CancellationToken.None));
            PrivateMember.Invoke(device, "Disconnect");
        }

        /// <summary>Checks that touch reports from the pipe are parsed, a closed pipe ends the session and a missing pipe is reported.</summary>
        [Test]
        public void TouchPanel_ReadsReportsUntilThePipeCloses()
        {
            var device = new PipeTouchPanelDevice();
            var update = DeviceTestSupport.BindTokenAction(device, "Update");
            Assert.Throws<IOException>(() => update(CancellationToken.None), "not connected yet");

            Connect(device, "TouchPanel");
            var payload = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(payload, 1UL << 20);
            WriteToServer(Packet(PipePacketType.Report, payload));

            update(CancellationToken.None);
            Assert.That(device.IsSensorCurrentlyOn(20), Is.True);

            _server!.Dispose();
            Assert.Throws<EndOfStreamException>(() => update(CancellationToken.None));
            PrivateMember.Invoke(device, "Disconnect");
        }

        /// <summary>Starts a pipe server for a device kind and connects the device to it.</summary>
        /// <param name="device">The pipe device.</param>
        /// <param name="kind">The device kind in the pipe name.</param>
        private void Connect(object device, string kind)
        {
            _server = new NamedPipeServerStream($"MajdataPlay.IO.{kind}.{TestPlayer}P", PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096);
            var waiting = _server.WaitForConnectionAsync();

            var connected = PrivateMember.Invoke<bool>(device, "Connect", CancellationToken.None);

            Assert.That(connected, Is.True);
            Assert.That(waiting.Wait(TimeSpan.FromSeconds(5)), Is.True);
        }

        /// <summary>Reads an exact number of bytes from the server with a timeout.</summary>
        /// <param name="count">The number of bytes.</param>
        /// <returns>The bytes.</returns>
        private byte[] ReadFromServer(int count)
        {
            var buffer = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = _server!.ReadAsync(buffer, offset, count - offset);
                Assert.That(read.Wait(TimeSpan.FromSeconds(5)), Is.True, "timed out reading from the device");
                Assert.That(read.Result, Is.GreaterThan(0));
                offset += read.Result;
            }
            return buffer;
        }

        /// <summary>Writes bytes to the device through the server.</summary>
        /// <param name="data">The bytes.</param>
        private void WriteToServer(byte[] data)
        {
            _server!.Write(data, 0, data.Length);
            _server.Flush();
        }

        /// <summary>Serialises a pipe packet.</summary>
        /// <param name="type">The packet type.</param>
        /// <param name="payload">The payload.</param>
        /// <returns>The packet bytes.</returns>
        private static byte[] Packet(PipePacketType type, byte[] payload)
        {
            var buffer = new byte[PipePacket.PACKET_HEADER_LENGTH + payload.Length];
            new PipePacket { Type = type, Version = 1, Length = (ushort)payload.Length, Payload = payload }.Write(buffer);
            return buffer;
        }
    }
}
#endif
