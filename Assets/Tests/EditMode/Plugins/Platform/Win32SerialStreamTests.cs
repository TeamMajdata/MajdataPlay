using System;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.Platform.Win32.IO;
using NUnit.Framework;
using SerialPortFactory = MajdataPlay.Platform.Win32.IO.SerialPort;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Platform
{
    /// <summary>
    /// Verifies the Win32 serial stream with overlapped I/O against a temporary file opened through the Win32 device
    /// namespace, which needs no serial hardware; serial-only control calls report their Win32 failures.
    /// </summary>
    public class Win32SerialStreamTests
    {
        /// <summary>Owns the temporary backing file.</summary>
        private TemporaryDirectory _directory = null!;
        /// <summary>Stores the device-namespace path of the backing file.</summary>
        private string _port = "";

        /// <summary>Creates a backing file with known content.</summary>
        [SetUp]
        public void SetUp()
        {
            _directory = new TemporaryDirectory();
            var file = _directory.WriteText("port.bin", "hello serial world");
            _port = @"\\.\" + file.Location;
        }

        /// <summary>Deletes the backing file.</summary>
        [TearDown]
        public void TearDown()
        {
            _directory.Dispose();
        }

        /// <summary>Reads the backing file contents.</summary>
        /// <returns>The file text.</returns>
        private string ReadBackingFile()
        {
            return _directory.Directory.FindFile("port.bin")!.ReadAllText();
        }

        /// <summary>Checks that a missing port reports the Win32 failure.</summary>
        [Test]
        public void Open_MissingPort_ThrowsIOException()
        {
            var error = Assert.Throws<IOException>(() => new Win32SerialStream("COM_DOES_NOT_EXIST_9"));

            Assert.That(error!.Message, Does.Contain("COM_DOES_NOT_EXIST_9"));
        }

        /// <summary>Checks synchronous overlapped reads and writes, which always start at the beginning of the device.</summary>
        [Test]
        public void ReadAndWrite_UseOverlappedIo()
        {
            using (var stream = new Win32SerialStream(_port, 115200, Parity.None, 8, StopBits.One))
            {
                var buffer = new byte[8];

                var read = stream.Read(buffer, 2, 5);

                Assert.That(read, Is.EqualTo(5));
                Assert.That(Encoding.ASCII.GetString(buffer, 2, 5), Is.EqualTo("hello"));
                stream.Write(Encoding.ASCII.GetBytes("JELLO"), 0, 5);
                stream.Flush();
                Assert.That(stream.PortName, Is.EqualTo(_port));
                Assert.That(stream.CanRead, Is.True);
                Assert.That(stream.CanWrite, Is.True);
                Assert.That(stream.CanSeek, Is.False);
            }

            Assert.That(ReadBackingFile(), Is.EqualTo("JELLO serial world"));
        }

        /// <summary>Checks asynchronous reads and writes, including empty buffers.</summary>
        [Test]
        public void ReadAsyncAndWriteAsync_CompleteOverlappedOperations()
        {
            using (var stream = new Win32SerialStream(_port, 9600, Parity.None, 8))
            {
                var buffer = new byte[5];

                Assert.That(stream.ReadAsync(Memory<byte>.Empty).Result, Is.EqualTo(0));
                Assert.That(stream.WriteAsync(ReadOnlyMemory<byte>.Empty, CancellationToken.None).IsCompletedSuccessfully, Is.True);
                var read = stream.ReadAsync(buffer.AsMemory(), CancellationToken.None).AsTask();
                Assert.That(read.Wait(5000), Is.True);
                Assert.That(read.Result, Is.EqualTo(5));
                Assert.That(Encoding.ASCII.GetString(buffer), Is.EqualTo("hello"));
                var write = stream.WriteAsync(Encoding.ASCII.GetBytes("HE").AsMemory(), CancellationToken.None).AsTask();
                Assert.That(write.Wait(5000), Is.True);
            }

            Assert.That(ReadBackingFile(), Does.StartWith("HEllo"));
        }

        /// <summary>Checks that every constructor overload opens the port.</summary>
        [Test]
        public void Constructors_OpenDevice()
        {
            using (var first = new Win32SerialStream(_port))
            {
                Assert.That(first.PortName, Is.EqualTo(_port));
            }

            using (var second = new Win32SerialStream(_port, 19200))
            {
                Assert.That(second.CanRead, Is.True);
            }

            using (var third = new Win32SerialStream(_port, 19200, Parity.Even))
            {
                Assert.That(third.CanWrite, Is.True);
            }
        }

        /// <summary>Checks that serial-only state queries and changes report Win32 failures on a non-serial device.</summary>
        [Test]
        public void SerialControls_OnNonSerialDevice_ThrowIOException()
        {
            using var stream = new Win32SerialStream(_port);

            Assert.Throws<IOException>(() => _ = stream.BaudRate);
            Assert.Throws<IOException>(() => stream.BaudRate = 9600);
            Assert.Throws<IOException>(() => _ = stream.DataBits);
            Assert.Throws<IOException>(() => stream.DataBits = 7);
            Assert.Throws<IOException>(() => _ = stream.Parity);
            Assert.Throws<IOException>(() => stream.Parity = Parity.Odd);
            Assert.Throws<IOException>(() => _ = stream.StopBits);
            Assert.Throws<IOException>(() => stream.StopBits = StopBits.Two);
            Assert.Throws<IOException>(() => _ = stream.Handshake);
            foreach (Handshake handshake in Enum.GetValues(typeof(Handshake)))
            {
                Assert.Throws<IOException>(() => stream.Handshake = handshake);
            }

            Assert.Throws<IOException>(() => _ = stream.ParityReplace);
            Assert.Throws<IOException>(() => stream.ParityReplace = 63);
            Assert.Throws<IOException>(() => _ = stream.DtrEnable);
            Assert.Throws<IOException>(() => stream.DtrEnable = true);
            Assert.Throws<IOException>(() => _ = stream.CtsHolding);
            Assert.Throws<IOException>(() => _ = stream.DsrHolding);
            Assert.Throws<IOException>(() => _ = stream.CDHolding);
            Assert.Throws<IOException>(() => stream.BreakState = true);
            Assert.Throws<IOException>(() => stream.BreakState = false);
            Assert.That(stream.BreakState, Is.False);
        }

        /// <summary>Checks timeout validation and that applying timeouts to a non-serial device fails after storing them.</summary>
        [Test]
        public void Timeouts_ValidateAndApply()
        {
            using var stream = new Win32SerialStream(_port);

            Assert.That(stream.ReadTimeout, Is.EqualTo(Timeout.Infinite));
            Assert.That(stream.WriteTimeout, Is.EqualTo(Timeout.Infinite));
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.ReadTimeout = -5);
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.WriteTimeout = -5);
            foreach (var timeout in new[] { 0, 250, Timeout.Infinite })
            {
                Assert.Throws<IOException>(() => stream.ReadTimeout = timeout);
                Assert.That(stream.ReadTimeout, Is.EqualTo(timeout));
                Assert.Throws<IOException>(() => stream.WriteTimeout = timeout);
                Assert.That(stream.WriteTimeout, Is.EqualTo(timeout));
            }
        }

        /// <summary>Checks the members inherited from the serial stream base class.</summary>
        [Test]
        public void BaseStreamMembers_RejectSeeking()
        {
            using var stream = new Win32SerialStream(_port);

            Assert.Throws<NotSupportedException>(() => _ = stream.Length);
            Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
            Assert.Throws<NotSupportedException>(() => stream.SetLength(1));
            Assert.Throws<NotSupportedException>(() => _ = stream.Position);
        }

        /// <summary>Checks that disposing twice is harmless.</summary>
        [Test]
        public void Dispose_IsRepeatable()
        {
            var stream = new Win32SerialStream(_port);

            stream.Dispose();

            Assert.DoesNotThrow(() => stream.Dispose());
        }

        /// <summary>Checks the private DCB flag helpers used by the serial control setters.</summary>
        [Test]
        public void FlagHelpers_ManipulateBitFields()
        {
            var type = typeof(Win32SerialStream);
            object[] arguments = { 0u, 0x0004u, true };

            PrivateMember.InvokeStatic(type, "SetFlag", arguments);
            Assert.That(arguments[0], Is.EqualTo(0x0004u));
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "GetFlag", 0x0005u, 0x0004u), Is.True);
            Assert.That(PrivateMember.InvokeStatic<bool>(type, "GetFlag", 0x0001u, 0x0004u), Is.False);
            arguments[2] = false;
            PrivateMember.InvokeStatic(type, "SetFlag", arguments);
            Assert.That(arguments[0], Is.EqualTo(0u));

            object[] bits = { 0xFFFFFFFFu, 12, 0x3u, 1u };
            PrivateMember.InvokeStatic(type, "SetBits", bits);
            Assert.That(bits[0], Is.EqualTo(0xFFFFDFFFu));
            Assert.That(PrivateMember.InvokeStatic<uint>(type, "GetBits", bits[0], 12, 0x3u), Is.EqualTo(1u));
            Assert.That(PrivateMember.InvokeStatic<uint>(type, "GetBits", 0x30u, 4, 0x3u), Is.EqualTo(3u));
        }

        /// <summary>Checks the unimplemented serial port factory's argument validation.</summary>
        [Test]
        public void SerialPortFactory_ValidatesPortName()
        {
            Assert.Throws<ArgumentException>(() => SerialPortFactory.Open(new SerialPortOptions()));
            var options = new SerialPortOptions
            {
                PortName = "COM3",
                BaudRate = 115200,
                Parity = Parity.Mark,
                DataBits = 7,
                StopBits = StopBits.Two
            };

            Assert.That(SerialPortFactory.Open(options), Is.Null);
            Assert.That(options.PortName, Is.EqualTo("COM3"));
            Assert.That(options.BaudRate, Is.EqualTo(115200));
            Assert.That(options.Parity, Is.EqualTo(Parity.Mark));
            Assert.That(options.DataBits, Is.EqualTo(7));
            Assert.That(options.StopBits, Is.EqualTo(StopBits.Two));
        }
    }
}
