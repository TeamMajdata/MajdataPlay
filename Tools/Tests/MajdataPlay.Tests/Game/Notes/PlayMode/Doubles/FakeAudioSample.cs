using System;
using System.Threading.Tasks;
using MajdataPlay.IO;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode.Doubles
{
    /// <summary>
    /// An in-memory sound effect that counts how often it is played or stopped instead of producing audio.
    /// </summary>
    internal sealed class FakeAudioSample : AudioSampleWrap
    {
        /// <summary>Creates a sample with a name and volume.</summary>
        /// <param name="name">The sound effect file name the audio manager looks up.</param>
        /// <param name="volume">The volume; zero marks the sample as muted.</param>
        public FakeAudioSample(string name, float volume)
        {
            Name = name;
            Volume = volume;
        }

        /// <summary>Gets the number of one-shot playbacks.</summary>
        public int PlayOneShotCount { get; private set; }

        /// <summary>Gets the number of stop requests.</summary>
        public int StopCount { get; private set; }

        /// <summary>Gets a value indicating whether the sample is empty; always false.</summary>
        public override bool IsEmpty => false;

        /// <summary>Gets a value indicating whether the sample is playing; always false.</summary>
        public override bool IsPlaying => false;

        /// <summary>Gets or sets the volume.</summary>
        public override float Volume { get; set; }

        /// <summary>Gets or sets the playback speed.</summary>
        public override float Speed { get; set; } = 1f;

        /// <summary>Gets or sets the playback position.</summary>
        public override double CurrentSec { get; set; }

        /// <summary>Gets the length; always zero.</summary>
        public override TimeSpan Length => TimeSpan.Zero;

        /// <summary>Gets or sets a value indicating whether the sample loops.</summary>
        public override bool IsLoop { get; set; }

        /// <summary>Does nothing.</summary>
        public override void Play()
        {
        }

        /// <summary>Does nothing.</summary>
        public override void Pause()
        {
        }

        /// <summary>Counts the stop request.</summary>
        public override void Stop()
        {
            StopCount++;
        }

        /// <summary>Counts the one-shot playback.</summary>
        public override void PlayOneShot()
        {
            PlayOneShotCount++;
        }

        /// <summary>Stores the volume.</summary>
        /// <param name="volume">The new volume.</param>
        public override void SetVolume(float volume)
        {
            Volume = volume;
        }

        /// <summary>Does nothing; the sample owns no resources.</summary>
        public override void Dispose()
        {
        }

        /// <summary>Does nothing; the sample owns no resources.</summary>
        /// <returns>A completed task.</returns>
        public override ValueTask DisposeAsync()
        {
            return default;
        }
    }
}
