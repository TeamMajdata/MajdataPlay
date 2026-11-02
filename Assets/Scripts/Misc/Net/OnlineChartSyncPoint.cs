using System;

#nullable enable
namespace MajdataPlay.Net
{
    /// <summary>Where the cached chart list of a server was last brought up to, as recorded by <see cref="OnlineChartCache"/>.</summary>
    internal readonly struct OnlineChartSyncPoint
    {
        /// <summary>
        /// Gets the end of the last <c>maichart/changes</c> range the cached chart list reflects, in UTC on the server's
        /// clock; the next query continues from it.
        /// </summary>
        public DateTime ChangesTo { get; }
        /// <summary>Gets when the whole chart list was last read, in UTC on the local clock.</summary>
        public DateTime FullSyncAt { get; }

        /// <summary>Creates a sync point.</summary>
        /// <param name="changesTo">The end of the last changes range, in UTC on the server's clock.</param>
        /// <param name="fullSyncAt">When the whole chart list was last read, in UTC on the local clock.</param>
        public OnlineChartSyncPoint(DateTime changesTo, DateTime fullSyncAt)
        {
            ChangesTo = changesTo;
            FullSyncAt = fullSyncAt;
        }
    }
}
