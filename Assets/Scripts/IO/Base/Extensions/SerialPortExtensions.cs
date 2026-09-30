using MajdataPlay.Buffers;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MajdataPlay.IO
{
    internal static class SerialPortExtensions
    {
        public static int Read(this SerialPort serial, Span<byte> buffer)
        {
            if(buffer.IsEmpty || serial.BytesToRead == 0)
            {
                return 0;
            }
            var byte2Read = Math.Min(serial.BytesToRead , buffer.Length);
            using (var readBuffer = Pool<byte>.Rent(byte2Read))
            {
                serial.Read(readBuffer, 0, byte2Read);
                readBuffer.AsSpan(0, byte2Read)
                          .CopyTo(buffer);
            }

            return byte2Read;
        }
        public static void Write(this SerialPort serial, ReadOnlySpan<byte> buffer)
        {
            if(buffer.IsEmpty)
            {
                return;
            }
            using (var writeBuffer = Pool<byte>.Rent(buffer.Length))
            {
                buffer.CopyTo(writeBuffer);
                serial.Write(writeBuffer, 0, buffer.Length);
            }
        }
    }
}
