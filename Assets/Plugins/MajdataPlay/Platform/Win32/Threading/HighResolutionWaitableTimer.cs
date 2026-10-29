using MajdataPlay.Syscall.Win32;
using System;
using System.Runtime.InteropServices;

#nullable enable
namespace MajdataPlay.Platform.Win32.Threading
{
    /// <summary>
    /// Blocks the calling thread for short durations with a high-resolution waitable timer, which wakes within about
    /// half a millisecond instead of the default 15.6 ms Windows timer granularity.
    /// </summary>
    public sealed class HighResolutionWaitableTimer : IDisposable
    {
        /// <summary>The timer handle, or zero after disposal.</summary>
        private IntPtr _handle;

        /// <summary>Wraps an existing timer handle.</summary>
        /// <param name="handle">The handle created with the high-resolution flag.</param>
        private HighResolutionWaitableTimer(IntPtr handle)
        {
            _handle = handle;
        }

        /// <summary>Releases the timer handle if <see cref="Dispose"/> was not called.</summary>
        ~HighResolutionWaitableTimer()
        {
            ReleaseHandle();
        }

        /// <summary>Creates a timer; fails on Windows versions before 10 1803 and on other operating systems.</summary>
        /// <param name="timer">The created timer, or null on failure.</param>
        /// <returns>Whether the timer was created.</returns>
        public static bool TryCreate(out HighResolutionWaitableTimer? timer)
        {
            timer = null;
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return false;
            }

            var handle = Win32API.Timer.CreateWaitableTimerEx(IntPtr.Zero, null,
                Win32API.Timer.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, Win32API.Timer.TIMER_ALL_ACCESS);
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            timer = new HighResolutionWaitableTimer(handle);
            return true;
        }

        /// <summary>Blocks the calling thread for about <paramref name="duration"/>.</summary>
        /// <param name="duration">The time to wait; zero or negative durations return immediately.</param>
        /// <returns>Whether the wait completed through the timer; false when the timer could not be armed or waited on.</returns>
        /// <exception cref="ObjectDisposedException">The timer has been disposed.</exception>
        public bool Wait(TimeSpan duration)
        {
            if (_handle == IntPtr.Zero)
            {
                throw new ObjectDisposedException(nameof(HighResolutionWaitableTimer));
            }
            if (duration <= TimeSpan.Zero)
            {
                return true;
            }

            // Negative due times are relative, in 100 ns units, which equal TimeSpan ticks.
            var dueTime = -duration.Ticks;
            if (!Win32API.Timer.SetWaitableTimer(_handle, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false))
            {
                return false;
            }

            return Win32API.Timer.WaitForSingleObject(_handle, Win32API.Timer.INFINITE) == Win32API.Timer.WAIT_OBJECT_0;
        }

        /// <summary>Closes the timer handle.</summary>
        public void Dispose()
        {
            ReleaseHandle();
            GC.SuppressFinalize(this);
        }

        /// <summary>Closes the timer handle once.</summary>
        private void ReleaseHandle()
        {
            if (_handle == IntPtr.Zero)
            {
                return;
            }

            Win32API.Timer.CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
