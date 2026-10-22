using System;
using System.Threading.Tasks;
using MajdataPlay.IO;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>An in-memory <see cref="AudioSampleWrap"/> that records the calls made by <see cref="AudioManager"/>.</summary>
    internal sealed class FakeAudioSample : AudioSampleWrap
    {
        /// <summary>Creates a named sample of the given type.</summary>
        /// <param name="name">The sample name.</param>
        /// <param name="type">The sound effect group.</param>
        /// <param name="isEmpty">Whether the sample reports itself as empty.</param>
        public FakeAudioSample(string name, SFXSampleType type, bool isEmpty = false)
        {
            Name = name;
            SampleType = type;
            Empty = isEmpty;
        }

        /// <summary>Gets a value indicating whether the sample reports itself as empty.</summary>
        public bool Empty { get; }

        /// <summary>Gets the number of one-shot plays.</summary>
        public int OneShots { get; private set; }

        /// <summary>Gets the number of stops.</summary>
        public int Stops { get; private set; }

        /// <summary>Gets the number of disposals.</summary>
        public int Disposals { get; private set; }

        /// <summary>Gets the last volume passed to <see cref="SetVolume"/>, or a negative value.</summary>
        public float LastSetVolume { get; private set; } = -1f;

        /// <inheritdoc/>
        public override bool IsEmpty => Empty;

        /// <inheritdoc/>
        public override bool IsPlaying => OneShots > Stops;

        /// <inheritdoc/>
        public override float Volume { get; set; } = 1f;

        /// <inheritdoc/>
        public override float Speed { get; set; } = 1f;

        /// <inheritdoc/>
        public override double CurrentSec { get; set; }

        /// <inheritdoc/>
        public override TimeSpan Length => TimeSpan.FromSeconds(1);

        /// <inheritdoc/>
        public override bool IsLoop { get; set; }

        /// <inheritdoc/>
        public override void Play()
        {
            OneShots++;
        }

        /// <inheritdoc/>
        public override void Pause()
        {
        }

        /// <inheritdoc/>
        public override void Stop()
        {
            Stops++;
        }

        /// <inheritdoc/>
        public override void PlayOneShot()
        {
            OneShots++;
        }

        /// <inheritdoc/>
        public override void SetVolume(float volume)
        {
            LastSetVolume = volume;
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            Disposals++;
        }

        /// <inheritdoc/>
        public override ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }
    }
}
