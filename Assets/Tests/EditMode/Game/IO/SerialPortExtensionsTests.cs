using System;
using System.IO.Ports;
using MajdataPlay.IO;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>
    /// Verifies the span based <see cref="SerialPort"/> helpers of <see cref="SerialPortExtensions"/> on a port that is
    /// never opened: empty buffers short-circuit before the port is touched, everything else reaches the port.
    /// </summary>
    public class SerialPortExtensionsTests
    {
        /// <summary>The closed port under test.</summary>
        private SerialPort _port = null!;

        /// <summary>Creates a port object without opening it.</summary>
        [SetUp]
        public void SetUp()
        {
            _port = new SerialPort("COM254");
        }

        /// <summary>Releases the port object.</summary>
        [TearDown]
        public void TearDown()
        {
            _port.Dispose();
        }

        /// <summary>Checks that reading into an empty buffer returns zero without consulting the closed port.</summary>
        [Test]
        public void Read_EmptyBuffer_ReturnsZeroWithoutTouchingPort()
        {
            var read = _port.Read(Span<byte>.Empty);

            Assert.That(read, Is.Zero);
            Assert.That(_port.IsOpen, Is.False);
        }

        /// <summary>Checks that reading into a real buffer queries the port and therefore fails while it is closed.</summary>
        [Test]
        public void Read_ClosedPort_Throws()
        {
            var buffer = new byte[8];

            Assert.Throws<InvalidOperationException>(() => _port.Read(buffer.AsSpan()));
        }

        /// <summary>Checks that writing an empty span is a no-op even on a closed port.</summary>
        [Test]
        public void Write_EmptySpan_IsNoOp()
        {
            Assert.DoesNotThrow(() => _port.Write(ReadOnlySpan<byte>.Empty));
            Assert.That(_port.IsOpen, Is.False);
        }

        /// <summary>Checks that writing bytes forwards them to the port and therefore fails while it is closed.</summary>
        [Test]
        public void Write_ClosedPort_Throws()
        {
            var bytes = new byte[] { 1, 2, 3 };

            Assert.Throws<InvalidOperationException>(() => _port.Write(new ReadOnlySpan<byte>(bytes)));
        }
    }
}
