using System;
using MajdataPlay.Timer;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.Support
{
    /// <summary>
    /// A manually advanced time source that makes <see cref="MajTimeline"/> deterministic in tests.
    /// </summary>
    public sealed class FakeTimeProvider : ITimeProvider
    {
        /// <summary>Gets the provider type reported to the timeline.</summary>
        public BuiltInTimeProvider Type => BuiltInTimeProvider.Unity;

        /// <summary>Gets or sets the current time in ticks.</summary>
        public long Ticks { get; set; }

        /// <summary>Does nothing; time only moves through <see cref="Advance"/>.</summary>
        public void OnPreUpdate()
        {
        }

        /// <summary>Moves the current time forward.</summary>
        /// <param name="duration">The time to add.</param>
        public void Advance(TimeSpan duration)
        {
            Ticks += duration.Ticks;
        }

        /// <summary>Moves the current time forward by milliseconds.</summary>
        /// <param name="milliseconds">The milliseconds to add.</param>
        public void AdvanceMilliseconds(double milliseconds)
        {
            Advance(TimeSpan.FromMilliseconds(milliseconds));
        }
    }
}
