using System;
using System.Runtime.InteropServices;

#nullable enable
namespace MajdataPlay.Net.LanSync
{
    /// <summary>
    /// Absolute clock shared by every machine of a LAN room.
    /// </summary>
    /// <remarks>
    /// Values are 100ns ticks counted from 1601-01-01 00:00:00 UTC, the same domain that
    /// <see cref="MajdataPlay.Timer.WinapiTimeProvider"/> samples, so two machines that agree on
    /// the wall clock agree on these values as well. The value is not monotonic when the system
    /// clock is adjusted; the LAN start schedule only spans a few seconds, so this is acceptable.
    /// </remarks>
    internal static class LanSyncClock
    {
        public static long NowTicks()
        {
#if UNITY_STANDALONE_WIN
            GetSystemTimePreciseAsFileTime(out var fileTime);
            return fileTime;
#else
            return DateTime.UtcNow.Ticks;
#endif
        }

        public static long MillisecondsToTicks(int milliseconds)
        {
            return milliseconds * TimeSpan.TicksPerMillisecond;
        }

        public static double TicksToMilliseconds(long ticks)
        {
            return ticks / (double)TimeSpan.TicksPerMillisecond;
        }

        public static double TicksToSeconds(long ticks)
        {
            return ticks / (double)TimeSpan.TicksPerSecond;
        }

#if UNITY_STANDALONE_WIN
        [DllImport("Kernel32.dll", CallingConvention = CallingConvention.Winapi)]
        static extern void GetSystemTimePreciseAsFileTime(out long filetime);
#endif
    }
}
