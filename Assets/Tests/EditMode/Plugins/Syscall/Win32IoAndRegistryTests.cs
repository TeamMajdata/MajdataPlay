using System;
using System.Runtime.InteropServices;
using System.Text;
using MajdataPlay.Syscall.Win32;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Syscall
{
    /// <summary>
    /// Covers <see cref="Win32API.IO"/> against a temporary file and <see cref="Win32API.Registry"/> against a read-only
    /// system key. Serial-port specific calls are only checked to fail cleanly on a regular file handle.
    /// </summary>
    public class Win32IoAndRegistryTests
    {
#if UNITY_EDITOR_WIN
        /// <summary>The value returned by CreateFile on failure.</summary>
        private static readonly IntPtr s_invalidHandle = new IntPtr(-1);

        /// <summary>The Win32 error reported for a missing file.</summary>
        private const int ErrorFileNotFound = 2;

        /// <summary>The Win32 error reported when there is no I/O request to cancel.</summary>
        private const int ErrorNotFound = 1168;

        /// <summary>The temporary directory that holds the test files.</summary>
        private TemporaryDirectory _directory = null!;

        /// <summary>Creates the temporary directory.</summary>
        [SetUp]
        public void SetUp()
        {
            _directory = new TemporaryDirectory();
        }

        /// <summary>Deletes the temporary directory.</summary>
        [TearDown]
        public void TearDown()
        {
            _directory.Dispose();
        }

        /// <summary>Opens an existing file for reading and writing.</summary>
        /// <param name="path">The file path.</param>
        /// <param name="flags">The file flags, for example overlapped mode.</param>
        /// <returns>The open handle.</returns>
        private static IntPtr Open(string path, uint flags = 0)
        {
            var handle = Win32API.IO.CreateFile(path, Win32API.IO.GENERIC_READ | Win32API.IO.GENERIC_WRITE, 0, IntPtr.Zero,
                Win32API.IO.OPEN_EXISTING, flags, IntPtr.Zero);
            Assert.That(handle, Is.Not.EqualTo(s_invalidHandle), $"CreateFile failed with error {Marshal.GetLastWin32Error()}.");
            return handle;
        }

        /// <summary>Checks that opening a missing file fails with the not-found error.</summary>
        [Test]
        public void CreateFile_MissingFile_FailsWithNotFound()
        {
            var path = _directory.Location + "\\missing.bin";

            var handle = Win32API.IO.CreateFile(path, Win32API.IO.GENERIC_READ, 0, IntPtr.Zero, Win32API.IO.OPEN_EXISTING, 0, IntPtr.Zero);
            var error = Marshal.GetLastWin32Error();

            Assert.That(handle, Is.EqualTo(s_invalidHandle));
            Assert.That(error, Is.EqualTo(ErrorFileNotFound));
        }

        /// <summary>Checks synchronous WriteFile and ReadFile through freshly opened handles.</summary>
        [Test]
        public void WriteFileAndReadFile_Synchronous_RoundTrip()
        {
            var file = _directory.WriteText("sync.bin", "hello");
            var buffer = Marshal.AllocHGlobal(8);
            try
            {
                Marshal.Copy(Encoding.ASCII.GetBytes("HEL"), 0, buffer, 3);
                var writer = Open(file.Location);
                var wrote = Win32API.IO.WriteFile(writer, buffer, 3, out var written, IntPtr.Zero);
                Assert.That(Win32API.IO.CloseHandle(writer), Is.True);

                Assert.That(wrote, Is.True);
                Assert.That(written, Is.EqualTo(3u));
                Assert.That(file.ReadAllText(), Is.EqualTo("HELlo"));

                var reader = Open(file.Location);
                var read = Win32API.IO.ReadFile(reader, buffer, 8, out var count, IntPtr.Zero);
                Assert.That(Win32API.IO.CloseHandle(reader), Is.True);

                Assert.That(read, Is.True);
                Assert.That(count, Is.EqualTo(5u));
                var bytes = new byte[5];
                Marshal.Copy(buffer, bytes, 0, 5);
                Assert.That(Encoding.ASCII.GetString(bytes), Is.EqualTo("HELlo"));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>Checks an overlapped read at an offset completed through GetOverlappedResult, and CancelIoEx without pending I/O.</summary>
        [Test]
        public void ReadFile_Overlapped_CompletesThroughGetOverlappedResult()
        {
            var file = _directory.WriteText("overlapped.bin", "abcdef");
            var buffer = Marshal.AllocHGlobal(8);
            var overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<Win32API.IO.OVERLAPPED>());
            var handle = Open(file.Location, Win32API.IO.FILE_FLAG_OVERLAPPED);
            try
            {
                Marshal.StructureToPtr(new Win32API.IO.OVERLAPPED { Offset = 2 }, overlapped, false);

                var started = Win32API.IO.ReadFile(handle, buffer, 3, out _, overlapped);
                var error = Marshal.GetLastWin32Error();
                Assert.That(started || error == Win32API.IO.ERROR_IO_PENDING, Is.True, $"ReadFile failed with error {error}.");

                var completed = Win32API.IO.GetOverlappedResult(handle, overlapped, out var transferred, true);

                Assert.That(completed, Is.True);
                Assert.That(transferred, Is.EqualTo(3u));
                var bytes = new byte[3];
                Marshal.Copy(buffer, bytes, 0, 3);
                Assert.That(Encoding.ASCII.GetString(bytes), Is.EqualTo("cde"));

                var cancelled = Win32API.IO.CancelIoEx(handle, IntPtr.Zero);
                var cancelError = Marshal.GetLastWin32Error();
                Assert.That(cancelled, Is.False);
                Assert.That(cancelError, Is.EqualTo(ErrorNotFound));
            }
            finally
            {
                Win32API.IO.CloseHandle(handle);
                Marshal.FreeHGlobal(overlapped);
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>Checks that the serial-port functions fail cleanly on a regular file handle.</summary>
        [Test]
        public void CommFunctions_OnRegularFile_Fail()
        {
            var file = _directory.WriteText("comm.bin", "x");
            var handle = Open(file.Location);
            try
            {
                var dcb = new Win32API.IO.DCB { DCBlength = (uint)Marshal.SizeOf<Win32API.IO.DCB>() };
                var timeouts = new Win32API.IO.COMMTIMEOUTS { ReadTotalTimeoutConstant = 10 };

                Assert.That(Win32API.IO.GetCommState(handle, ref dcb), Is.False);
                Assert.That(Win32API.IO.SetCommState(handle, ref dcb), Is.False);
                Assert.That(Win32API.IO.SetCommTimeouts(handle, ref timeouts), Is.False);
                Assert.That(Win32API.IO.GetCommTimeouts(handle, out _), Is.False);
                Assert.That(Win32API.IO.GetCommModemStatus(handle, out _), Is.False);
                Assert.That(Win32API.IO.SetCommBreak(handle), Is.False);
                Assert.That(Win32API.IO.ClearCommBreak(handle), Is.False);
            }
            finally
            {
                Win32API.IO.CloseHandle(handle);
            }
        }

        /// <summary>Checks that a read-only system key can be opened, enumerated to the end and closed.</summary>
        [Test]
        public void Registry_EnumeratesValuesOfSystemKey()
        {
            var opened = Win32API.Registry.RegOpenKeyEx(Win32API.Registry.HKEY_LOCAL_MACHINE,
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", 0, Win32API.Registry.KEY_READ, out var key);
            Assert.That(opened, Is.EqualTo(Win32API.Registry.ERROR_SUCCESS));
            try
            {
                var count = 0u;
                var foundName = false;
                int result;
                while (true)
                {
                    var name = new StringBuilder(256);
                    var data = new StringBuilder(1024);
                    var nameLength = (uint)name.Capacity;
                    var dataLength = (uint)(data.Capacity * 2);
                    result = Win32API.Registry.RegEnumValue(key, count, name, ref nameLength, IntPtr.Zero, out _, data, ref dataLength);
                    if (result != Win32API.Registry.ERROR_SUCCESS)
                    {
                        break;
                    }
                    foundName |= name.ToString() == "ProcessorNameString";
                    count++;
                }

                Assert.That(result, Is.EqualTo(Win32API.Registry.ERROR_NO_MORE_ITEMS));
                Assert.That(count, Is.GreaterThan(0u));
                Assert.That(foundName, Is.True);
            }
            finally
            {
                Assert.That(Win32API.Registry.RegCloseKey(key), Is.EqualTo(Win32API.Registry.ERROR_SUCCESS));
            }
        }

        /// <summary>Checks that opening a missing key reports the not-found error.</summary>
        [Test]
        public void Registry_MissingKey_ReportsNotFound()
        {
            var opened = Win32API.Registry.RegOpenKeyEx(Win32API.Registry.HKEY_LOCAL_MACHINE,
                @"HARDWARE\MajdataPlay.Tests\Missing", 0, Win32API.Registry.KEY_READ, out _);

            Assert.That(opened, Is.EqualTo(ErrorFileNotFound));
        }
#endif
    }
}
