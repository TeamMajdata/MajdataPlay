using System;
using System.Threading.Tasks;
using MajdataPlay.IO;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.Support
{
    /// <summary>
    /// An audio sample that records how scene code drives it, without any audio backend.
    /// </summary>
    public sealed class FakeAudioSample : AudioSampleWrap
    {
        /// <summary>Creates a sample.</summary>
        /// <param name="name">The SFX name used for lookups.</param>
        /// <param name="length">The reported sample length.</param>
        /// <param name="canSeek">Whether the sample supports seeking.</param>
        public FakeAudioSample(string name = "fake", TimeSpan length = default, bool canSeek = true)
        {
            Name = name;
            Length = length;
            CanSeek = canSeek;
        }

        /// <summary>Gets or sets a value indicating whether the sample reports itself as empty.</summary>
        public bool Empty { get; set; }

        /// <inheritdoc/>
        public override bool IsEmpty => Empty;

        /// <summary>Gets or sets the reported playing state.</summary>
        public bool Playing { get; set; }

        /// <inheritdoc/>
        public override bool IsPlaying => Playing;

        /// <inheritdoc/>
        public override float Volume { get; set; } = 1f;

        /// <inheritdoc/>
        public override float Speed { get; set; } = 1f;

        /// <inheritdoc/>
        public override double CurrentSec { get; set; }

        /// <inheritdoc/>
        public override TimeSpan Length { get; }

        /// <inheritdoc/>
        public override bool IsLoop { get; set; }

        /// <summary>Gets the number of <see cref="Play"/> calls.</summary>
        public int PlayCount { get; private set; }

        /// <summary>Gets the number of <see cref="Pause"/> calls.</summary>
        public int PauseCount { get; private set; }

        /// <summary>Gets the number of <see cref="Stop"/> calls.</summary>
        public int StopCount { get; private set; }

        /// <summary>Gets the number of <see cref="PlayOneShot"/> calls.</summary>
        public int PlayOneShotCount { get; private set; }

        /// <summary>Gets the last volume passed to <see cref="SetVolume"/>.</summary>
        public float LastSetVolume { get; private set; } = float.NaN;

        /// <summary>Gets a value indicating whether the sample was disposed.</summary>
        public bool IsDisposed => _isDisposed;

        /// <summary>Gets or sets a value indicating whether a call to play leaves the sample reported as playing.</summary>
        public bool KeepsPlaying { get; set; }

        /// <inheritdoc/>
        public override void Play()
        {
            PlayCount++;
            Playing = KeepsPlaying;
        }

        /// <inheritdoc/>
        public override void Pause()
        {
            PauseCount++;
            Playing = false;
        }

        /// <inheritdoc/>
        public override void Stop()
        {
            StopCount++;
            Playing = false;
        }

        /// <inheritdoc/>
        public override void PlayOneShot()
        {
            PlayOneShotCount++;
            Playing = KeepsPlaying;
        }

        /// <inheritdoc/>
        public override void SetVolume(float volume)
        {
            LastSetVolume = volume;
            Volume = volume;
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            _isDisposed = true;
        }

        /// <inheritdoc/>
        public override ValueTask DisposeAsync()
        {
            _isDisposed = true;
            return default;
        }
    }
}
