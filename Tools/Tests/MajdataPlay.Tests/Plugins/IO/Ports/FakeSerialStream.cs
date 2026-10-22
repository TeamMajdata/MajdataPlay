#nullable enable
using System;
using System.IO;
using System.IO.Ports;
using MajdataPlay.IO.Ports;

namespace MajdataPlay.Tests.Plugins.IO.Ports
{
    /// <summary>A serial stream double backed by memory, used to observe the sealed base stream contract without a device.</summary>
    internal sealed class FakeSerialStream : SerialStream
    {
        /// <summary>The bytes returned by reads.</summary>
        private readonly MemoryStream _input;

        /// <summary>Creates a stream that reads the given bytes and records writes.</summary>
        /// <param name="input">The bytes returned by reads.</param>
        public FakeSerialStream(byte[] input)
        {
            _input = new MemoryStream(input, false);
        }

        /// <summary>Gets the bytes written to the stream.</summary>
        public MemoryStream Output { get; } = new MemoryStream();

        /// <inheritdoc />
        public override string PortName => "COM-TEST";

        /// <inheritdoc />
        public override int BaudRate { get; set; } = 9600;

        /// <inheritdoc />
        public override int DataBits { get; set; } = 8;

        /// <inheritdoc />
        public override Parity Parity { get; set; } = Parity.None;

        /// <inheritdoc />
        public override StopBits StopBits { get; set; } = StopBits.One;

        /// <inheritdoc />
        public override Handshake Handshake { get; set; } = Handshake.None;

        /// <inheritdoc />
        public override byte ParityReplace { get; set; } = 63;

        /// <inheritdoc />
        public override bool DtrEnable { get; set; }

        /// <inheritdoc />
        public override bool CtsHolding => true;

        /// <inheritdoc />
        public override bool DsrHolding => false;

        /// <inheritdoc />
        public override bool CDHolding => false;

        /// <inheritdoc />
        public override bool BreakState { get; set; }

        /// <inheritdoc />
        public override int ReadTimeout { get; set; } = 100;

        /// <inheritdoc />
        public override int WriteTimeout { get; set; } = 200;

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            return _input.Read(buffer, offset, count);
        }

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            Output.Write(buffer, offset, count);
        }

        /// <inheritdoc />
        public override void Flush()
        {
        }
    }
}
