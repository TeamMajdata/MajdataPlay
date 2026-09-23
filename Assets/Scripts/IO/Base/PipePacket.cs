using System;
using MajdataPlay.Buffers; 
using System.Collections.Generic;
using System.Text;
using System.Buffers.Binary;

namespace MajdataPlay.IO
{
    readonly ref struct PipePacket
    {
        /// Package structure
        /// Header (56bit)
        ///         16bit              16bit           8bit        16bit (fixed)
        /// |<-    Length   ->| |     Version     | |  Type  |  |    identity     |          
        ///  00000000 00000000   00000000 00000000   00000000    00000100 01001000
        /// 
        public delegate void PacketReceivedCallback(PipePacket packet);
        public PipePacketType Type { get; init; }
        public ushort Version { get; init; }
        public ushort Length { get; init; }
        public ReadOnlySpan<byte> Payload { get; init; }

        public const byte FLAG_HEARTBEAT_PAKCET = 0b0000_0000;
        public const byte FLAG_REPORT_PAKCET = 0b0000_0001;

        public const int PACKET_HEADER_LENGTH = 7;

        public const ushort PACKET_FIXED_IDENTITY = 0x0448;

        public readonly int Write(Span<byte> buffer)
        {
            buffer[0] = 0x48;
            buffer[1] = 0x04;
            buffer[2] = (byte)Type;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(3, 2), Version);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(5, 2), Length);
            Payload.CopyTo(buffer.Slice(PACKET_HEADER_LENGTH));

            return PACKET_HEADER_LENGTH + Payload.Length;
        }

        public static void Parse(ref SpanBuffer buffer, ushort maxPayloadLen, PacketReceivedCallback onPacketReceived)
        {
            while (buffer.Data.Length > 0)
            {
                var data = buffer.Data;
                var syncIndex = -1;

                for (var i = 0; i <= data.Length - 2; i++)
                {
                    if (data[i] == 0x48 && data[i + 1] == 0x04)
                    {
                        syncIndex = i;
                        break;
                    }
                }

                if (syncIndex == -1)
                {
                    if (data[data.Length - 1] == 0x48)
                    {
                        buffer.Skip(data.Length - 1);
                    }
                    else
                    {
                        buffer.Skip(data.Length);
                    }
                    return;
                }

                if (syncIndex > 0)
                {
                    buffer.Skip(syncIndex);
                    data = buffer.Data;
                }

                if (data.Length < PACKET_HEADER_LENGTH)
                {
                    return;
                }

                var payloadLen = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
                var packetLen = PACKET_HEADER_LENGTH + payloadLen;

                if (payloadLen > maxPayloadLen)
                {
                    buffer.Skip(2);
                    continue;
                }

                if (data.Length < packetLen)
                {
                    return;
                }

                if (data.Length >= packetLen)
                {
                    var rawPacket = data.Slice(0, packetLen);
                    var packet = new PipePacket()
                    {
                        Type = (PipePacketType)rawPacket[2],
                        Version = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(3, 2)),
                        Length = payloadLen,
                        Payload = rawPacket.Slice(PACKET_HEADER_LENGTH, payloadLen)
                    };
                    try
                    {
                        onPacketReceived(packet);
                    }
                    finally
                    {
                        buffer.Skip(packetLen);
                    }
                }
            }
        }
    }
    enum PipePacketType : byte
    {
        HeartBeat = 0b0000_0000,
        Report = 0b0000_0001
    }
}
