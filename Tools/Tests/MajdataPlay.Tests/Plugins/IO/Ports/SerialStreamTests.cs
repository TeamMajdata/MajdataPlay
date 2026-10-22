#nullable enable
using System;
using System.IO;
using NUnit.Framework;

namespace MajdataPlay.Tests.Plugins.IO.Ports
{
    /// <summary>Covers the sealed, non-seekable stream contract shared by every serial stream implementation.</summary>
    public class SerialStreamTests
    {
        /// <summary>Checks that serial streams are always readable, writable and never seekable.</summary>
        [Test]
        public void Capabilities_AreFixed()
        {
            using var stream = new FakeSerialStream(Array.Empty<byte>());

            Assert.That(stream.CanRead, Is.True);
            Assert.That(stream.CanWrite, Is.True);
            Assert.That(stream.CanSeek, Is.False);
        }

        /// <summary>Checks that length, position, seeking and resizing are not supported.</summary>
        [Test]
        public void SeekOperations_AreNotSupported()
        {
            using var stream = new FakeSerialStream(Array.Empty<byte>());

            Assert.Throws<NotSupportedException>(() => _ = stream.Length);
            Assert.Throws<NotSupportedException>(() => _ = stream.Position);
            Assert.Throws<NotSupportedException>(() => stream.Position = 1);
            Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
            Assert.Throws<NotSupportedException>(() => stream.SetLength(1));
        }

        /// <summary>Checks that the timeouts redeclared by the serial stream hide the base stream timeouts.</summary>
        [Test]
        public void Timeouts_AreRedeclaredBySerialStream()
        {
            using var stream = new FakeSerialStream(Array.Empty<byte>());

            stream.ReadTimeout = 50;
            stream.WriteTimeout = 75;

            Assert.That(stream.ReadTimeout, Is.EqualTo(50));
            Assert.That(stream.WriteTimeout, Is.EqualTo(75));
            Assert.That(stream.PortName, Is.EqualTo("COM-TEST"));
        }

        /// <summary>Checks that a serial stream can be consumed through the general stream API.</summary>
        [Test]
        public void StreamApi_ReadsAndWrites()
        {
            using var stream = new FakeSerialStream(new byte[] { 1, 2, 3 });
            Stream asStream = stream;
            var buffer = new byte[3];

            var read = asStream.Read(buffer, 0, 3);
            asStream.Write(new byte[] { 9 }, 0, 1);

            Assert.That(read, Is.EqualTo(3));
            Assert.That(buffer, Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(stream.Output.ToArray(), Is.EqualTo(new byte[] { 9 }));
        }
    }
}
