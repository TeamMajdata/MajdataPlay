#nullable enable
using System;

namespace MajdataPlay.Net.Editor
{
    /// <summary>
    /// One consecutive phase of a request on its own time line, starting when the request was queued.
    /// </summary>
    internal readonly struct CurlNetworkTimingPhase
    {
        /// <summary>
        /// Creates a phase.
        /// </summary>
        /// <param name="name">The phase name shown in the Timing tab.</param>
        /// <param name="start">The time from queueing until the phase started.</param>
        /// <param name="duration">The length of the phase.</param>
        public CurlNetworkTimingPhase(string name, TimeSpan start, TimeSpan duration)
        {
            Name = name;
            Start = start;
            Duration = duration;
        }

        /// <summary>
        /// Gets the phase name shown in the Timing tab.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the time from queueing until the phase started.
        /// </summary>
        public TimeSpan Start { get; }

        /// <summary>
        /// Gets the length of the phase.
        /// </summary>
        public TimeSpan Duration { get; }
    }
}
