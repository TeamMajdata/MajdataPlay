using System;
using System.Runtime.InteropServices;
using System.Threading;
using MajdataPlay.Syscall.Win32;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Syscall
{
    /// <summary>
    /// Covers the side-effect-free parts of <see cref="Win32API"/> in the Windows Editor: thread and module queries,
    /// window enumeration and lookup, the key code table and the declared constants. Calls that change windows, hooks or
    /// focus are not exercised.
    /// </summary>
    public class Win32ApiTests
    {
#if UNITY_EDITOR_WIN
        /// <summary>Checks that the kernel thread id is stable on one thread and differs on another.</summary>
        [Test]
        public void GetCurrentThreadId_IdentifiesThreads()
        {
            var first = Win32API.GetCurrentThreadId();
            var other = 0u;
            var thread = new Thread(() => other = Win32API.GetCurrentThreadId());

            thread.Start();
            thread.Join();

            Assert.That(first, Is.Not.EqualTo(0u));
            Assert.That(Win32API.GetCurrentThreadId(), Is.EqualTo(first));
            Assert.That(other, Is.Not.EqualTo(0u));
            Assert.That(other, Is.Not.EqualTo(first));
        }

        /// <summary>Checks that loaded modules are found and unknown modules are not.</summary>
        [Test]
        public void GetModuleHandle_FindsLoadedModulesOnly()
        {
            Assert.That(Win32API.GetModuleHandle("kernel32.dll"), Is.Not.EqualTo(IntPtr.Zero));
            Assert.That(Win32API.GetModuleHandle("MajdataPlay.Tests.Missing.dll"), Is.EqualTo(IntPtr.Zero));
        }

        /// <summary>Checks that a window with an unused class and title cannot be found.</summary>
        [Test]
        public void FindWindow_UnknownWindow_ReturnsZero()
        {
            Assert.That(Win32API.FindWindow("MajdataPlay.Tests.NoSuchClass", "MajdataPlay.Tests.NoSuchWindow"), Is.EqualTo(IntPtr.Zero));
        }

        /// <summary>Checks that the top-level windows can be enumerated and stopped early, and that their owners can be queried.</summary>
        [Test]
        public void EnumWindows_VisitsWindowsAndStopsWhenAsked()
        {
            var visited = 0;
            var firstWindow = IntPtr.Zero;
            var parameter = 0;
            Win32API.EnumWindowsProc callback = (handle, lParam) =>
            {
                parameter = lParam;
                visited++;
                firstWindow = handle;
                return false;
            };

            Win32API.EnumWindows(callback, 1234);
            GC.KeepAlive(callback);

            Assume.That(visited, Is.GreaterThan(0), "The session has no top-level windows.");
            Assert.That(visited, Is.EqualTo(1));
            Assert.That(parameter, Is.EqualTo(1234));
            var threadId = Win32API.GetWindowThreadProcessId(firstWindow, out var processId);
            Assert.That(threadId, Is.Not.EqualTo(0));
            Assert.That(processId, Is.Not.EqualTo(0));
            Assert.DoesNotThrow(() => Win32API.IsWindowVisible(firstWindow));
        }

        /// <summary>Checks that queries about the null window report no owner and no visibility.</summary>
        [Test]
        public void GetWindowThreadProcessId_InvalidWindow_ReturnsZero()
        {
            var threadId = Win32API.GetWindowThreadProcessId(IntPtr.Zero, out var processId);

            Assert.That(threadId, Is.EqualTo(0));
            Assert.That(processId, Is.EqualTo(0));
            Assert.That(Win32API.IsWindowVisible(IntPtr.Zero), Is.False);
        }

        /// <summary>Checks that querying a key state does not fail.</summary>
        [Test]
        public void GetAsyncKeyState_CanBeQueried()
        {
            Assert.DoesNotThrow(() => Win32API.GetAsyncKeyState((int)Win32API.RawKey.F24));
        }
#endif

        /// <summary>Checks the window positioning constants and the topmost sentinel.</summary>
        [Test]
        public void Constants_MatchWin32Values()
        {
            Assert.That(Win32API.HWND_TOPMOST, Is.EqualTo(new IntPtr(-1)));
            Assert.That(Win32API.SWP_NOSIZE, Is.EqualTo(0x0001u));
            Assert.That(Win32API.SWP_NOMOVE, Is.EqualTo(0x0002u));
            Assert.That(Win32API.SWP_NOZORDER, Is.EqualTo(0x0004u));
        }

        /// <summary>Checks representative entries of the hook type table against the Win32 values.</summary>
        [Test]
        public void HookType_MatchesWin32Values()
        {
            Assert.That((int)Win32API.HookType.WH_JOURNALRECORD, Is.EqualTo(0));
            Assert.That((int)Win32API.HookType.WH_KEYBOARD, Is.EqualTo(2));
            Assert.That((int)Win32API.HookType.WH_KEYBOARD_LL, Is.EqualTo(13));
            Assert.That((int)Win32API.HookType.WH_MOUSE_LL, Is.EqualTo(14));
        }

        /// <summary>Checks representative virtual key codes, aliases and the scan-code entries of the key table.</summary>
        [Test]
        public void RawKey_MatchesVirtualKeyCodes()
        {
            Assert.That((uint)Win32API.RawKey.LeftButton, Is.EqualTo(0x01u));
            Assert.That((uint)Win32API.RawKey.Return, Is.EqualTo(0x0Du));
            Assert.That((uint)Win32API.RawKey.Space, Is.EqualTo(0x20u));
            Assert.That((uint)Win32API.RawKey.N0, Is.EqualTo(0x30u));
            Assert.That((uint)Win32API.RawKey.A, Is.EqualTo(0x41u));
            Assert.That((uint)Win32API.RawKey.Z, Is.EqualTo(0x5Au));
            Assert.That((uint)Win32API.RawKey.Numpad0, Is.EqualTo(0x60u));
            Assert.That((uint)Win32API.RawKey.F1, Is.EqualTo(0x70u));
            Assert.That((uint)Win32API.RawKey.F24, Is.EqualTo(0x87u));
            Assert.That((uint)Win32API.RawKey.LeftShift, Is.EqualTo(0xA0u));
            Assert.That((uint)Win32API.RawKey.OEMClear, Is.EqualTo(0xFEu));
            Assert.That(Win32API.RawKey.Hangul, Is.EqualTo(Win32API.RawKey.Kana));
            Assert.That((uint)Win32API.RawKey.International2, Is.EqualTo(0x7000u));
            Assert.That((uint)Win32API.RawKey.International3, Is.EqualTo(0x7D00u));
        }

        /// <summary>Checks the registry constants and the predefined local machine key.</summary>
        [Test]
        public void RegistryConstants_MatchWin32Values()
        {
            Assert.That(Win32API.Registry.HKEY_LOCAL_MACHINE, Is.EqualTo(new UIntPtr(0x80000002u)));
            Assert.That(Win32API.Registry.KEY_READ, Is.EqualTo(0x20019));
            Assert.That(Win32API.Registry.ERROR_SUCCESS, Is.EqualTo(0));
            Assert.That(Win32API.Registry.ERROR_NO_MORE_ITEMS, Is.EqualTo(259));
        }

        /// <summary>Checks the I/O constants against the Win32 values.</summary>
        [Test]
        public void IOConstants_MatchWin32Values()
        {
            Assert.That(Win32API.IO.GENERIC_READ, Is.EqualTo(0x80000000u));
            Assert.That(Win32API.IO.GENERIC_WRITE, Is.EqualTo(0x40000000u));
            Assert.That(Win32API.IO.OPEN_EXISTING, Is.EqualTo(3u));
            Assert.That(Win32API.IO.FILE_FLAG_OVERLAPPED, Is.EqualTo(0x40000000u));
            Assert.That(Win32API.IO.ERROR_IO_PENDING, Is.EqualTo(997));
            Assert.That(Win32API.IO.ERROR_OPERATION_ABORTED, Is.EqualTo(995));
            Assert.That(Win32API.IO.MS_CTS_ON, Is.EqualTo(0x10u));
            Assert.That(Win32API.IO.MS_DSR_ON, Is.EqualTo(0x20u));
            Assert.That(Win32API.IO.MS_RING_ON, Is.EqualTo(0x40u));
            Assert.That(Win32API.IO.MS_RLSD_ON, Is.EqualTo(0x80u));
        }

        /// <summary>Checks that the marshalled structure sizes match the native Win32 layouts for the process bitness.</summary>
        [Test]
        public void IOStructures_MatchNativeLayout()
        {
            Assert.That(Marshal.SizeOf<Win32API.IO.OVERLAPPED>(), Is.EqualTo(IntPtr.Size == 8 ? 32 : 20));
            Assert.That(Marshal.SizeOf<Win32API.IO.COMMTIMEOUTS>(), Is.EqualTo(20));
            Assert.That(Marshal.SizeOf<Win32API.IO.DCB>(), Is.EqualTo(28));
            Assert.That(Marshal.OffsetOf<Win32API.IO.OVERLAPPED>(nameof(Win32API.IO.OVERLAPPED.hEvent)).ToInt32(), Is.EqualTo(IntPtr.Size == 8 ? 24 : 16));
            Assert.That(Marshal.OffsetOf<Win32API.IO.DCB>(nameof(Win32API.IO.DCB.ByteSize)).ToInt32(), Is.EqualTo(18));
            Assert.That(Marshal.OffsetOf<Win32API.IO.DCB>(nameof(Win32API.IO.DCB.wReserved1)).ToInt32(), Is.EqualTo(26));
        }
    }
}
