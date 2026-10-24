#nullable enable
namespace MajdataPlay.Net.LanSync
{
    /// <summary>
    /// Result of the chart start gate. <see cref="ScheduledUtcTicks"/> is the instant the chart was
    /// supposed to start at (0 when the room is not synchronized) and
    /// <see cref="ReleasedUtcTicks"/> is the instant the gate let the load pipeline continue, both
    /// in the local clock.
    /// </summary>
    internal readonly struct ChartStartSchedule
    {
        public static readonly ChartStartSchedule None = default;

        public long ScheduledUtcTicks { get; }
        public long ReleasedUtcTicks { get; }
        public bool HasValue => ScheduledUtcTicks > 0;

        public ChartStartSchedule(long scheduledUtcTicks, long releasedUtcTicks)
        {
            ScheduledUtcTicks = scheduledUtcTicks;
            ReleasedUtcTicks = releasedUtcTicks;
        }

        /// <summary>
        /// How late the chart actually started compared with the scheduled instant.
        /// </summary>
        public double GetStartErrorMilliseconds(long startedUtcTicks)
        {
            return LanSyncClock.TicksToMilliseconds(startedUtcTicks - ScheduledUtcTicks);
        }
    }
}
