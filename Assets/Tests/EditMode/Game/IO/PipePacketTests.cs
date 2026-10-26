using System;
using System.Collections.Generic;
using MajdataPlay.Buffers;
using MajdataPlay.IO;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>Verifies the framing, resynchronisation and serialisation of <see cref="PipePacket"/>.</summary>
    public class PipePacketTests
    {
        /// <summary>The packets received by the parser callback in the current test.</summary>
        private readonly List<(PipePacketType Type, ushort Version, byte[] Payload)> _received = new();

        /// <summary>Clears the received packets.</summary>
        [SetUp]
        public void SetUp()
        {
            _received.Clear();
        }

        /// <summary>Checks the serialised header layout and the returned length.</summary>
        [Test]
        public void Write_ProducesHeaderAndPayload()
        {
            var buffer = new byte[16];
            var packet = new PipePacket
            {
                Type = PipePacketType.Report,
                Version = 0x0102,
                Length = 3,
                Payload = new byte[] { 7, 8, 9 },
            };

            var length = packet.Write(buffer);

            Assert.That(length, Is.EqualTo(PipePacket.PACKET_HEADER_LENGTH + 3));
            Assert.That(buffer.AsSpan(0, length).ToArray(), Is.EqualTo(new byte[] { 0x48, 0x04, 0x01, 0x02, 0x01, 0x03, 0x00, 7, 8, 9 }));
        }

        /// <summary>Checks that a written packet is parsed back with the same fields and fully consumed.</summary>
        [Test]
        public void Parse_RoundTripsAWrittenPacket()
        {
            var data = Build(PipePacketType.Report, 5, new byte[] { 1, 2, 3, 4 });
            var buffer = new SpanBuffer(new byte[64]);
            buffer.Write(data);

            PipePacket.Parse(ref buffer, 64, OnPacket);

            Assert.That(_received, Has.Count.EqualTo(1));
            Assert.That(_received[0].Type, Is.EqualTo(PipePacketType.Report));
            Assert.That(_received[0].Version, Is.EqualTo(5));
            Assert.That(_received[0].Payload, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            Assert.That(buffer.IsEmpty, Is.True);
        }

        /// <summary>Checks that consecutive packets and an empty heartbeat are all delivered.</summary>
        [Test]
        public void Parse_ConsecutivePackets_AreAllDelivered()
        {
            var data = Concat(
                Build(PipePacketType.HeartBeat, 1, Array.Empty<byte>()),
                Build(PipePacketType.Report, 1, new byte[] { 0xAA }));
            var buffer = new SpanBuffer(new byte[64]);
            buffer.Write(data);

            PipePacket.Parse(ref buffer, 64, OnPacket);

            Assert.That(_received, Has.Count.EqualTo(2));
            Assert.That(_received[0].Type, Is.EqualTo(PipePacketType.HeartBeat));
            Assert.That(_received[0].Payload, Is.Empty);
            Assert.That(_received[1].Payload, Is.EqualTo(new byte[] { 0xAA }));
        }

        /// <summary>Checks that bytes before the sync word are discarded.</summary>
        [Test]
        public void Parse_LeadingGarbage_IsSkipped()
        {
            var data = Concat(new byte[] { 0x00, 0x48, 0x11, 0x04 }, Build(PipePacketType.Report, 2, new byte[] { 5 }));
            var buffer = new SpanBuffer(new byte[64]);
            buffer.Write(data);

            PipePacket.Parse(ref buffer, 64, OnPacket);

            Assert.That(_received, Has.Count.EqualTo(1));
            Assert.That(_received[0].Payload, Is.EqualTo(new byte[] { 5 }));
        }

        /// <summary>Checks that an incomplete packet is retained until the rest arrives.</summary>
        [Test]
        public void Parse_IncompletePacket_IsRetained()
        {
            var data = Build(PipePacketType.Report, 3, new byte[] { 1, 2, 3, 4, 5, 6 });
            var buffer = new SpanBuffer(new byte[64]);
            buffer.Write(data.AsSpan(0, 9));

            PipePacket.Parse(ref buffer, 64, OnPacket);
            Assert.That(_received, Is.Empty);
            Assert.That(buffer.Data.Length, Is.EqualTo(9));

            buffer.Write(data.AsSpan(9));
            PipePacket.Parse(ref buffer, 64, OnPacket);
            Assert.That(_received, Has.Count.EqualTo(1));
            Assert.That(buffer.IsEmpty, Is.True);
        }

        /// <summary>Checks that an incomplete header is retained.</summary>
        [Test]
        public void Parse_IncompleteHeader_IsRetained()
        {
            var buffer = new SpanBuffer(new byte[64]);
            buffer.Write(new byte[] { 0x48, 0x04, 0x01, 0x00 });

            PipePacket.Parse(ref buffer, 64, OnPacket);

            Assert.That(_received, Is.Empty);
            Assert.That(buffer.Data.Length, Is.EqualTo(4));
        }

        /// <summary>Checks that data without a sync word is dropped except a trailing first sync byte.</summary>
        [Test]
        public void Parse_NoSyncWord_KeepsOnlyAPossibleSyncPrefix()
        {
            var withPrefix = new SpanBuffer(new byte[64]);
            withPrefix.Write(new byte[] { 1, 2, 3, 0x48 });
            PipePacket.Parse(ref withPrefix, 64, OnPacket);

            var withoutPrefix = new SpanBuffer(new byte[64]);
            withoutPrefix.Write(new byte[] { 1, 2, 3 });
            PipePacket.Parse(ref withoutPrefix, 64, OnPacket);

            Assert.That(withPrefix.Data.ToArray(), Is.EqualTo(new byte[] { 0x48 }));
            Assert.That(withoutPrefix.IsEmpty, Is.True);
            Assert.That(_received, Is.Empty);
        }

        /// <summary>Checks that a header announcing an oversized payload is skipped and parsing resynchronises.</summary>
        [Test]
        public void Parse_OversizedPayload_ResynchronisesOnTheNextPacket()
        {
            var oversized = new byte[] { 0x48, 0x04, 0x01, 0x00, 0x00, 0xFF, 0x00 };
            var data = Concat(oversized, Build(PipePacketType.Report, 9, new byte[] { 3, 3 }));
            var buffer = new SpanBuffer(new byte[64]);
            buffer.Write(data);

            PipePacket.Parse(ref buffer, 16, OnPacket);

            Assert.That(_received, Has.Count.EqualTo(1));
            Assert.That(_received[0].Version, Is.EqualTo(9));
        }

        /// <summary>Checks that a throwing callback still consumes its packet before the exception propagates.</summary>
        [Test]
        public void Parse_ThrowingCallback_ConsumesThePacket()
        {
            var buffer = new SpanBuffer(new byte[64]);
            buffer.Write(Build(PipePacketType.Report, 1, new byte[] { 1 }));
            var threw = false;

            try
            {
                PipePacket.Parse(ref buffer, 64, static _ => throw new InvalidOperationException("callback"));
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            Assert.That(threw, Is.True);
            Assert.That(buffer.IsEmpty, Is.True);
        }

        /// <summary>Records a received packet.</summary>
        /// <param name="packet">The packet passed by the parser.</param>
        private void OnPacket(PipePacket packet)
        {
            _received.Add((packet.Type, packet.Version, packet.Payload.ToArray()));
            Assert.That(packet.Length, Is.EqualTo(packet.Payload.Length));
        }

        /// <summary>Serialises a packet.</summary>
        /// <param name="type">The packet type.</param>
        /// <param name="version">The protocol version.</param>
        /// <param name="payload">The payload.</param>
        /// <returns>The packet bytes.</returns>
        private static byte[] Build(PipePacketType type, ushort version, byte[] payload)
        {
            var buffer = new byte[PipePacket.PACKET_HEADER_LENGTH + payload.Length];
            var packet = new PipePacket
            {
                Type = type,
                Version = version,
                Length = (ushort)payload.Length,
                Payload = payload,
            };
            packet.Write(buffer);
            return buffer;
        }

        /// <summary>Concatenates byte arrays.</summary>
        /// <param name="first">The first part.</param>
        /// <param name="second">The second part.</param>
        /// <returns>The joined bytes.</returns>
        private static byte[] Concat(byte[] first, byte[] second)
        {
            var result = new byte[first.Length + second.Length];
            first.CopyTo(result, 0);
            second.CopyTo(result, first.Length);
            return result;
        }
    }
}
