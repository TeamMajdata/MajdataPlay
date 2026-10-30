using System;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.IO;
using MajdataPlay.Net;
using MajSimai;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.PlayMode.Support
{
    /// <summary>
    /// An in-memory <see cref="ISongDetail"/> whose metadata and maidata are supplied by the test, so gameplay
    /// components can be driven without chart files, network access or audio decoding.
    /// </summary>
    internal sealed class GamePlayFakeSongDetail : ISongDetail
    {
        /// <summary>The designer names of the seven chart levels.</summary>
        private readonly string[] _designers;

        /// <summary>The level labels of the seven chart levels.</summary>
        private readonly string[] _levels;

        /// <summary>Creates a fake song with distinct designer and level labels for every chart level.</summary>
        /// <param name="hash">The song hash; a unique value keeps chart settings of tests apart.</param>
        public GamePlayFakeSongDetail(string? hash = null)
        {
            Hash = hash ?? Guid.NewGuid().ToString("N");
            _designers = new[] { "D0", "D1", "D2", "D3", "D4", "D5", "D6" };
            _levels = new[] { "1", "3", "7", "10", "13+", "14", "15" };
        }

        /// <summary>Gets or sets the song title.</summary>
        public string Title { get; set; } = "Fake Title";

        /// <summary>Gets or sets the song artist.</summary>
        public string Artist { get; set; } = "Fake Artist";

        /// <summary>Gets the song description.</summary>
        public string Description { get; } = "Fake Description";

        /// <summary>Gets the designer names indexed by chart level.</summary>
        public ReadOnlySpan<string> Designers => _designers;

        /// <summary>Gets the level labels indexed by chart level.</summary>
        public ReadOnlySpan<string> Levels => _levels;

        /// <summary>Gets an empty tag list for the in-memory song.</summary>
        public ReadOnlySpan<string> Tags => ReadOnlySpan<string>.Empty;

        /// <summary>Gets or sets where the song is stored.</summary>
        public ChartStorageLocation Location { get; set; } = ChartStorageLocation.Local;

        /// <summary>Gets the song timestamp.</summary>
        public DateTime Timestamp { get; } = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Gets the song identity, which fakes derive from the hash.</summary>
        public string Id => Hash;

        /// <summary>Gets the song hash.</summary>
        public string Hash { get; }

        /// <summary>Gets a value indicating whether the video was loaded; always false.</summary>
        public bool IsVideoLoaded => false;

        /// <summary>Gets a value indicating whether the cover was loaded; always false.</summary>
        public bool IsCoverLoaded => false;

        /// <summary>Gets a value indicating whether the compressed cover was loaded; always false.</summary>
        public bool IsCompressedCoverLoaded => false;

        /// <summary>Gets a value indicating whether the audio track was loaded; always false.</summary>
        public bool IsAudioTrackLoaded => false;

        /// <summary>Gets a value indicating whether the preview track was loaded; always false.</summary>
        public bool IsPreviewAudioTrackLoaded => false;

        /// <summary>Gets a value indicating whether the maidata was loaded.</summary>
        public bool IsMaidataLoaded => Maidata is not null;

        /// <summary>Gets or sets the maidata returned by <see cref="GetMaidataAsync"/>.</summary>
        public SimaiFile? Maidata { get; set; }

        /// <summary>Gets or sets the exception thrown by <see cref="GetMaidataAsync"/> when no maidata is set.</summary>
        public Exception MaidataFailure { get; set; } = new InvalidOperationException("No maidata was supplied.");

        /// <summary>Gets or sets the cover returned by <see cref="GetCoverAsync"/>.</summary>
        public Sprite? Cover { get; set; }

        /// <summary>Gets the number of maidata requests received.</summary>
        public int MaidataRequestCount { get; private set; }

        /// <summary>Completes immediately because nothing needs to be preloaded.</summary>
        /// <param name="progress">Ignored.</param>
        /// <param name="token">Ignored.</param>
        /// <returns>A completed task.</returns>
        public ValueTask PreloadAsync(INetProgress? progress = null, CancellationToken token = default)
        {
            return default;
        }

        /// <summary>Returns an empty video path.</summary>
        /// <param name="progress">Ignored.</param>
        /// <param name="token">Ignored.</param>
        /// <returns>An empty string.</returns>
        public ValueTask<string> GetVideoPathAsync(INetProgress? progress = null, CancellationToken token = default)
        {
            return new ValueTask<string>(string.Empty);
        }

        /// <summary>Returns the configured cover.</summary>
        /// <param name="isCompressed">Ignored.</param>
        /// <param name="progress">Ignored.</param>
        /// <param name="token">Ignored.</param>
        /// <returns>The configured cover, which may be null.</returns>
        public ValueTask<Sprite> GetCoverAsync(bool isCompressed, INetProgress? progress = null, CancellationToken token = default)
        {
            return new ValueTask<Sprite>(Cover!);
        }

        /// <summary>Returns the shared empty audio sample.</summary>
        /// <param name="progress">Ignored.</param>
        /// <param name="token">Ignored.</param>
        /// <returns>The empty audio sample.</returns>
        public ValueTask<AudioSampleWrap> GetAudioTrackAsync(INetProgress? progress = null, CancellationToken token = default)
        {
            return new ValueTask<AudioSampleWrap>(AudioSampleWrap.Empty);
        }

        /// <summary>Returns the shared empty audio sample.</summary>
        /// <param name="progress">Ignored.</param>
        /// <param name="token">Ignored.</param>
        /// <returns>The empty audio sample.</returns>
        public ValueTask<AudioSampleWrap> GetPreviewAudioTrackAsync(INetProgress? progress = null, CancellationToken token = default)
        {
            return new ValueTask<AudioSampleWrap>(AudioSampleWrap.Empty);
        }

        /// <summary>Returns the configured maidata or fails with <see cref="MaidataFailure"/>.</summary>
        /// <param name="ignoreCache">Ignored.</param>
        /// <param name="progress">Ignored.</param>
        /// <param name="token">Ignored.</param>
        /// <returns>The configured maidata.</returns>
        /// <exception cref="Exception">The configured <see cref="MaidataFailure"/> when no maidata is set.</exception>
        public ValueTask<SimaiFile> GetMaidataAsync(bool ignoreCache = false, INetProgress? progress = null, CancellationToken token = default)
        {
            MaidataRequestCount++;
            if (Maidata is null)
            {
                return new ValueTask<SimaiFile>(Task.FromException<SimaiFile>(MaidataFailure));
            }
            return new ValueTask<SimaiFile>(Maidata);
        }
    }
}
