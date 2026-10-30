using System;
using System.Runtime.InteropServices;

#nullable enable
namespace MajdataPlay.Syscall.Win32
{
    public static partial class Win32API
    {
        /// <summary>Waitable timer and wait functions from kernel32.</summary>
        public static class Timer
        {
            /// <summary>Creates a timer with sub-millisecond resolution; available since Windows 10 version 1803.</summary>
            public const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;

            /// <summary>Grants all access rights to a timer object.</summary>
            public const uint TIMER_ALL_ACCESS = 0x001F0003;

            /// <summary>Waits without a timeout.</summary>
            public const uint INFINITE = 0xFFFFFFFF;

            /// <summary>Returned by wait functions when the object was signaled.</summary>
            public const uint WAIT_OBJECT_0 = 0x00000000;

            /// <summary>Creates or opens a waitable timer object.</summary>
            /// <param name="lpTimerAttributes">The security attributes, or zero for defaults.</param>
            /// <param name="lpTimerName">The object name, or null for an unnamed timer.</param>
            /// <param name="dwFlags">The creation flags, such as <see cref="CREATE_WAITABLE_TIMER_HIGH_RESOLUTION"/>.</param>
            /// <param name="dwDesiredAccess">The requested access rights.</param>
            /// <returns>The timer handle, or zero on failure.</returns>
            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateWaitableTimerExW")]
            public static extern IntPtr CreateWaitableTimerEx(IntPtr lpTimerAttributes, string? lpTimerName, uint dwFlags,
                uint dwDesiredAccess);

            /// <summary>Activates a waitable timer.</summary>
            /// <param name="hTimer">The timer handle.</param>
            /// <param name="lpDueTime">The due time in 100-nanosecond units; negative values are relative to now.</param>
            /// <param name="lPeriod">The period in milliseconds, or zero for a one-shot timer.</param>
            /// <param name="pfnCompletionRoutine">The completion routine, or zero for none.</param>
            /// <param name="lpArgToCompletionRoutine">The completion routine argument, or zero.</param>
            /// <param name="fResume">Whether to resume a suspended system when the timer is signaled.</param>
            /// <returns>Whether the timer was activated.</returns>
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetWaitableTimer(IntPtr hTimer, ref long lpDueTime, int lPeriod,
                IntPtr pfnCompletionRoutine, IntPtr lpArgToCompletionRoutine, [MarshalAs(UnmanagedType.Bool)] bool fResume);

            /// <summary>Waits until an object is signaled or the timeout elapses.</summary>
            /// <param name="hHandle">The object handle.</param>
            /// <param name="dwMilliseconds">The timeout in milliseconds, or <see cref="INFINITE"/>.</param>
            /// <returns><see cref="WAIT_OBJECT_0"/> when signaled, otherwise a timeout or failure code.</returns>
            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

            /// <summary>Closes an object handle.</summary>
            /// <param name="hObject">The handle to close.</param>
            /// <returns>Whether the handle was closed.</returns>
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CloseHandle(IntPtr hObject);
        }
    }
}
